#if UNITY_EDITOR
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class ReanimatorPsdArmNames
{
    private const string UndoLabel = "Swap PSD Arm Names";
    private const string UndoKey = "Reanimator.PsdArmNames.";
    private const string LongBlockKeys =
        " LMsk Lr16 Lr32 Layr Mt16 Mt32 Mtrn Alph FMsk lnk2 FEid FXid PxSD ";

    [Serializable]
    private sealed class Change
    {
        public string guid;
        public int[] ids;
        public string[] names;
        public bool[] nameMapped;
        public string before;
        public string after;
    }

    private sealed class Layer
    {
        public int id;
        public int extraLength;
        public (int start, int size) pascal;
        public (int start, int size) unicode;
        public string name;
    }

    static ReanimatorPsdArmNames()
    {
        Undo.undoRedoEvent += OnUndoRedo;
    }

    internal static void Swap(string path, int[] previewIds, string[] names, string[] spriteGuids)
    {
        var ids = ResolveImporterIds(path, previewIds, names, spriteGuids);
        var before = File.ReadAllBytes(path);
        var after = SwapNames(before, ids, names, out var nameMapped);
        var change = new Change
        {
            guid = AssetDatabase.AssetPathToGUID(path),
            ids = ids,
            names = names,
            nameMapped = nameMapped,
            before = Hash(before),
            after = Hash(after)
        };
        SaveAndImport(path, before, after, ids, names, nameMapped);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        // Unity owns the Undo entry. The group points to the reversible source-file edit.
        var marker = ScriptableObject.CreateInstance<ReanimatorPsdArmNamesUndo>();
        marker.hideFlags = HideFlags.HideAndDontSave;
        marker.name = change.guid;
        Undo.RegisterCreatedObjectUndo(marker, UndoLabel);
        SessionState.SetString(UndoKey + Undo.GetCurrentGroup(), JsonUtility.ToJson(change));
        Undo.IncrementCurrentGroup();
    }

    private static int[] ResolveImporterIds(string path, int[] previewIds, string[] names, string[] spriteGuids)
    {
        var importer = AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new InvalidOperationException("The PSD importer is unavailable.");

        using var serialized = new SerializedObject(importer);
        var layers = serialized.FindProperty("m_PsdLayers");
        if (layers == null)
            throw new InvalidOperationException("The PSD importer layer table is unavailable.");

        var imported = new List<(int id, string name, string guid)>();
        for (var index = 0; index < layers.arraySize; index++)
        {
            var layer = layers.GetArrayElementAtIndex(index);
            if (layer.FindPropertyRelative("m_IsGroup").boolValue)
                continue;
            imported.Add((
                layer.FindPropertyRelative("m_LayerID").intValue,
                layer.FindPropertyRelative("m_Name").stringValue,
                layer.FindPropertyRelative("m_SpriteID").stringValue));
        }

        // The preview can carry the raw PSD ID (zero) while Unity has generated its own ID.
        var ids = new int[names.Length];
        for (var index = 0; index < names.Length; index++)
            ids[index] = ResolveImporterId(imported, previewIds[index], names[index], spriteGuids[index]);
        return ids;
    }

    internal static int ResolveImporterId(
        IReadOnlyList<(int id, string name, string guid)> imported, int previewId, string name, string spriteGuid)
    {
        var candidates = imported.Where(layer => layer.name == name).ToList();
        var byGuid = candidates.Where(layer => !string.IsNullOrEmpty(spriteGuid) &&
            string.Equals(layer.guid, spriteGuid, StringComparison.OrdinalIgnoreCase)).ToList();
        var byId = candidates.Where(layer => previewId != 0 && layer.id == previewId).ToList();
        var matches = byGuid.Count > 0 ? byGuid : byId.Count > 0 ? byId : candidates;
        if (matches.Count != 1)
            throw new InvalidDataException($"Could not uniquely match arm layer '{name}' in the PSD importer.");
        return matches[0].id;
    }

    private static void OnUndoRedo(in UndoRedoInfo info)
    {
        if (info.undoName != UndoLabel)
            return;
        var json = SessionState.GetString(UndoKey + info.undoGroup, "");
        if (string.IsNullOrEmpty(json))
            return;

        try
        {
            var change = JsonUtility.FromJson<Change>(json);
            var path = AssetDatabase.GUIDToAssetPath(change.guid);
            if (string.IsNullOrEmpty(path))
                return;
            var current = File.ReadAllBytes(path);
            if (Hash(current) != (info.isRedo ? change.before : change.after))
                throw new InvalidOperationException("The PSD changed on disk after WA. Its current contents were kept.");

            var names = info.isRedo ? change.names : SwapPairs(change.names);
            var replacement = SwapNames(current, change.ids, names, out _);
            if (Hash(replacement) != (info.isRedo ? change.after : change.before))
                throw new InvalidDataException("Could not restore the recorded PSD layer names.");
            var undo = !info.isRedo;
            var currentIds = change.ids.Select((id, index) =>
                undo && change.nameMapped[index] ? change.ids[index ^ 1] : id).ToArray();
            SaveAndImport(path, current, replacement, currentIds, names, change.nameMapped);
        }
        catch (Exception ex)
        {
            ReanimatorLog.Error(UndoLabel + ": " + ex.Message);
        }
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    private static string[] SwapPairs(string[] names)
    {
        return names.Select((_, index) => names[index ^ 1]).ToArray();
    }

    private static void SaveAndImport(string path, byte[] original, byte[] replacement, int[] ids, string[] names, bool[] nameMapped)
    {
        var importer = AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new InvalidOperationException("The PSD importer is unavailable.");
        using var serialized = new SerializedObject(importer);
        var layers = serialized.FindProperty("m_PsdLayers");
        if (layers == null)
            throw new InvalidOperationException("The PSD importer layer table is unavailable.");

        // Some PSDs have no embedded IDs. Unity falls back to this cached name table.
        // Keep sprite GUIDs attached to their original pixels when Unity regenerates layer IDs.
        var spriteIds = new string[ids.Length];
        for (var index = 0; index < layers.arraySize; index++)
        {
            var layer = layers.GetArrayElementAtIndex(index);
            if (layer.FindPropertyRelative("m_IsGroup").boolValue)
                continue;
            var target = Array.IndexOf(ids, layer.FindPropertyRelative("m_LayerID").intValue);
            if (target < 0)
                continue;
            var name = layer.FindPropertyRelative("m_Name");
            if (spriteIds[target] != null || name.stringValue != names[target])
                throw new InvalidDataException("The PSD importer layer names do not match the selected arm sprites.");
            spriteIds[target] = layer.FindPropertyRelative("m_SpriteID").stringValue;
            name.stringValue = names[target ^ 1];
            if (nameMapped[target])
                layer.FindPropertyRelative("m_LayerID").intValue = ids[target ^ 1];
        }
        if (Array.Exists(spriteIds, string.IsNullOrEmpty))
            throw new InvalidDataException("Could not locate both arms in the PSD importer.");

        var meta = File.ReadAllBytes(path + ".meta");
        var fullPath = Path.GetFullPath(path);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp~";
        void Replace(byte[] bytes)
        {
            File.WriteAllBytes(temp, bytes);
            // Unity's asset scanner can briefly hold the PSD without delete sharing on Windows.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Replace(temp, fullPath, null);
                    break;
                }
                catch (IOException ex) when (attempt < 5 && IsTransientReplaceError(ex) && File.Exists(temp))
                {
                    System.Threading.Thread.Sleep(25 * (attempt + 1));
                }
            }
        }
        void Import()
        {
            using (ReanimatorPsdSpritePositionPostprocessor.SuppressForAsset(path))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        try
        {
            Replace(replacement);
            try
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.WriteImportSettingsIfDirty(path);
                Import();
                using var imported = new SerializedObject(AssetImporter.GetAtPath(path));
                var importedLayers = imported.FindProperty("m_PsdLayers");
                var verified = 0;
                for (var index = 0; index < importedLayers.arraySize; index++)
                {
                    var layer = importedLayers.GetArrayElementAtIndex(index);
                    var target = Array.IndexOf(spriteIds, layer.FindPropertyRelative("m_SpriteID").stringValue);
                    if (target >= 0 && layer.FindPropertyRelative("m_Name").stringValue == names[target ^ 1])
                        verified++;
                }
                if (verified != ids.Length)
                    throw new InvalidDataException("The PSD importer could not preserve the arm sprite IDs.");
            }
            catch
            {
                Replace(original);
                File.WriteAllBytes(path + ".meta", meta);
                Import();
                throw;
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static bool IsTransientReplaceError(IOException exception)
    {
        var error = exception.HResult & 0xffff;
        return error == 32 || error == 33 || error == 1175;
    }

    private static byte[] SwapNames(byte[] source, int[] ids, string[] expectedNames, out bool[] nameMapped)
    {
        var version = Read16(source, 4);
        if (Key(source, 0) != "8BPS" || (version != 1 && version != 2))
            throw new InvalidDataException("Expected a layered PSD or PSB file.");

        var large = version == 2;
        var position = SectionEnd(source, 26, 4, source.Length);
        position = SectionEnd(source, position, 4, source.Length);
        var lengthSize = large ? 8 : 4;
        var maskEnd = SectionEnd(source, position, lengthSize, source.Length);
        position += lengthSize;
        var layerEnd = SectionEnd(source, position, lengthSize, maskEnd);
        var layers = new List<List<Layer>>();
        if (layerEnd > position + lengthSize)
            layers.Add(ReadLayers(source, position + lengthSize, layerEnd, large));

        position = layerEnd;
        if (position < maskEnd)
            position = SectionEnd(source, position, 4, maskEnd);
        while (maskEnd - position >= 12)
        {
            var block = ReadBlock(source, position, maskEnd, large);
            if (block.key == "Layr" || block.key == "Lr16" || block.key == "Lr32")
                layers.Add(ReadLayers(source, block.data, block.end, large));
            position = checked(block.data + Align(block.end - block.data, 4));
        }

        var edits = new List<(int start, int size, byte[] replacement)>();
        var matched = new HashSet<int>();
        nameMapped = new bool[ids.Length];
        foreach (var table in layers)
        {
            for (var index = 0; index < ids.Length; index += 2)
            {
                var left = FindLayer(table, ids[index], expectedNames[index]);
                var right = FindLayer(table, ids[index + 1], expectedNames[index + 1]);
                if (left == null && right == null)
                    continue;
                if (left == null || right == null ||
                    left.name != expectedNames[index] || right.name != expectedNames[index + 1])
                    throw new InvalidDataException("The arm names in the PSD do not match the selected sprite pair.");
                if ((left.unicode.size == 0) != (right.unicode.size == 0))
                    throw new InvalidDataException("The arm layers use different PSD name encodings.");

                AddSwap(left.pascal, right.pascal);
                if (left.unicode.size > 0)
                    AddSwap(left.unicode, right.unicode);
                var delta = right.pascal.size + right.unicode.size - left.pascal.size - left.unicode.size;
                if (delta != 0)
                {
                    UpdateExtraLength(left.extraLength, delta);
                    UpdateExtraLength(right.extraLength, -delta);
                }
                nameMapped[index] = left.id == 0 && right.id == 0;
                nameMapped[index + 1] = nameMapped[index];
                matched.Add(index);
            }
        }

        if (matched.Count != ids.Length / 2)
            throw new InvalidDataException("Could not locate both arm layers in the source PSD.");

        var result = new byte[source.Length];
        var read = 0;
        var write = 0;
        foreach (var edit in edits.OrderBy(edit => edit.start))
        {
            var unchanged = edit.start - read;
            Buffer.BlockCopy(source, read, result, write, unchanged);
            write += unchanged;
            Buffer.BlockCopy(edit.replacement, 0, result, write, edit.replacement.Length);
            write += edit.replacement.Length;
            read = edit.start + edit.size;
        }
        Buffer.BlockCopy(source, read, result, write, source.Length - read);
        return result;

        void AddSwap((int start, int size) first, (int start, int size) second)
        {
            edits.Add((first.start, first.size, source.AsSpan(second.start, second.size).ToArray()));
            edits.Add((second.start, second.size, source.AsSpan(first.start, first.size).ToArray()));
        }
        void UpdateExtraLength(int offset, int delta)
        {
            var replacement = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(replacement, checked(Read32(source, offset) + delta));
            edits.Add((offset, 4, replacement));
        }
    }

    private static Layer FindLayer(List<Layer> layers, int id, string name)
    {
        return layers.SingleOrDefault(layer => layer.id != 0 && layer.id == id) ??
               layers.SingleOrDefault(layer => layer.id == 0 && layer.name == name);
    }

    private static List<Layer> ReadLayers(byte[] source, int position, int end, bool large)
    {
        var count = Math.Abs((int)Read16(source, position));
        position += 2;
        var layers = new List<Layer>();
        for (var index = 0; index < count; index++)
        {
            var channels = (ushort)Read16(source, position + 16);
            var extraLength = position + 18 + channels * (large ? 10 : 6) + 12;
            var extraEnd = SectionEnd(source, extraLength, 4, end);
            position = SectionEnd(source, extraLength + 4, 4, extraEnd);
            position = SectionEnd(source, position, 4, extraEnd);
            var nameLength = source[position];
            var nameSize = Align(nameLength + 1, 4);
            if (position + nameSize > extraEnd)
                throw new InvalidDataException("Invalid PSD layer name.");
            var layer = new Layer
            {
                extraLength = extraLength,
                pascal = (position, nameSize),
                name = Encoding.ASCII.GetString(source, position + 1, nameLength)
            };
            position += nameSize;
            while (extraEnd - position >= 12)
            {
                var block = ReadBlock(source, position, extraEnd, large);
                if (block.key == "lyid" && block.end - block.data >= 4)
                    layer.id = Read32(source, block.data);
                if (block.key == "luni")
                {
                    var bytes = checked(Read32(source, block.data) * 2);
                    if (layer.unicode.size != 0 || bytes < 0 || bytes > block.end - block.data - 4)
                        throw new InvalidDataException("Invalid PSD Unicode layer name.");
                    layer.unicode = (position, block.end - position);
                    layer.name = Encoding.BigEndianUnicode.GetString(source, block.data + 4, bytes).TrimEnd('\0');
                }
                position = block.end;
            }
            layers.Add(layer);
            position = extraEnd;
        }
        return layers;
    }

    private static (string key, int data, int end) ReadBlock(byte[] source, int position, int limit, bool large)
    {
        var signature = Key(source, position);
        if (signature != "8BIM" && signature != "8B64")
            throw new InvalidDataException("Invalid PSD layer information signature.");
        var key = Key(source, position + 4);
        var lengthSize = large && LongBlockKeys.Contains(" " + key + " ") ? 8 : 4;
        return (key, position + 8 + lengthSize, SectionEnd(source, position + 8, lengthSize, limit));
    }

    private static int SectionEnd(byte[] source, int offset, int lengthSize, int limit)
    {
        var length = lengthSize == 8 ? BinaryPrimitives.ReadInt64BigEndian(source.AsSpan(offset, 8)) : Read32(source, offset);
        if (length < 0 || length > limit - (long)offset - lengthSize)
            throw new InvalidDataException("Invalid PSD section length.");
        return offset + lengthSize + (int)length;
    }

    private static int Align(int size, int alignment) => (size + alignment - 1) & -alignment;
    private static short Read16(byte[] source, int offset) => BinaryPrimitives.ReadInt16BigEndian(source.AsSpan(offset, 2));
    private static int Read32(byte[] source, int offset) => BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(offset, 4));
    private static string Key(byte[] source, int offset) => Encoding.ASCII.GetString(source, offset, 4);
}

internal sealed class ReanimatorPsdArmNamesUndo : ScriptableObject { }
#endif
