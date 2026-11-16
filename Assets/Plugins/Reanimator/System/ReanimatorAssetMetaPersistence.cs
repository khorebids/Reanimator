#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

[InitializeOnLoad]
internal static class ReanimatorAssetMetaPersistence
{
    private const string UndoImportersKey = "Reanimator.NativeUndoImporters";

    static ReanimatorAssetMetaPersistence()
    {
        Undo.undoRedoEvent += PersistImportersAfterUndo;
    }

    internal static void RegisterImporterUndo(AssetImporter importer, string operationName)
    {
        Undo.RegisterCompleteObjectUndo(importer, operationName);
        var guid = AssetDatabase.AssetPathToGUID(importer.assetPath);
        if (string.IsNullOrEmpty(guid))
            return;

        // Unity owns the snapshots. Store only the assets whose restored importer must be flushed.
        var key = UndoImportersKey + "." + Undo.GetCurrentGroup();
        var guids = SessionState.GetString(key, "");
        if (Array.IndexOf(guids.Split(','), guid) < 0)
            SessionState.SetString(key, string.IsNullOrEmpty(guids) ? guid : guids + "," + guid);
    }

    private static void PersistImportersAfterUndo(in UndoRedoInfo info)
    {
        var guids = SessionState.GetString(UndoImportersKey + "." + info.undoGroup, "");
        if (string.IsNullOrEmpty(guids))
            return;

        foreach (var guid in guids.Split(','))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                continue;

            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || !EditorUtility.IsDirty(importer))
                continue;

            try
            {
                using (ReanimatorPsdSpritePositionPostprocessor.SuppressForAsset(path))
                {
                    if (AssetDatabase.WriteImportSettingsIfDirty(path))
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
            }
            catch (Exception ex)
            {
                ReanimatorLog.Exception(ex);
            }
        }
    }

    public static bool TryRewriteMetaLinesAndReimport(
        string assetPath,
        string operationName,
        Func<List<string>, bool> rewrite,
        out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "Asset path is empty.";
            return false;
        }

        if (rewrite == null)
        {
            error = "Meta rewrite callback is unavailable.";
            return false;
        }

        var metaPath = assetPath + ".meta";
        if (!File.Exists(metaPath))
        {
            error = "Target meta file not found.";
            return false;
        }

        try
        {
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer != null)
                RegisterImporterUndo(importer, string.IsNullOrWhiteSpace(operationName) ? "Reanimator Meta Edit" : operationName);

            var newLine = DetectNewLine(File.ReadAllText(metaPath));
            var lines = ReadMetaLines(metaPath);
            if (!rewrite(lines))
                return false;

            WriteMetaLines(metaPath, lines, newLine);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryReadMetaLines(string assetPath, out List<string> lines, out string error)
    {
        lines = null;
        error = null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "Asset path is empty.";
            return false;
        }

        var metaPath = assetPath + ".meta";
        if (!File.Exists(metaPath))
        {
            error = "Meta file not found.";
            return false;
        }

        try
        {
            lines = ReadMetaLines(metaPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryResetPsdImporter(string path, out string error)
    {
        error = null;
        var metaPath = path + ".meta";
        if (!(AssetImporter.GetAtPath(path) is UnityEditor.U2D.PSD.PSDImporter) ||
            !TryReadMetaLines(path, out var original, out error))
        {
            error = error ?? "Select an imported PSD or PSB.";
            return false;
        }

        var header = original.IndexOf("ScriptedImporter:");
        var script = original.FindIndex(line => line.StartsWith("  script: ", StringComparison.Ordinal));
        if (header < 0 || script <= header)
        {
            error = "PSD importer metadata is missing its script reference.";
            return false;
        }

        // Keep the asset GUID and importer override. Unity rebuilds all importer data from the source.
        var reset = original.GetRange(0, header + 1);
        reset.Add(original[script]);
        var newLine = DetectNewLine(File.ReadAllText(metaPath));
        using (ReanimatorPsdSpritePositionPostprocessor.SuppressForAsset(path))
        {
            try
            {
                WriteMetaLines(metaPath, reset, newLine);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (!(AssetImporter.GetAtPath(path) is UnityEditor.U2D.PSD.PSDImporter) ||
                    AssetDatabase.LoadMainAssetAtPath(path) == null)
                    throw new InvalidOperationException("Unity could not reimport the PSD.");
                return true;
            }
            catch (Exception ex)
            {
                WriteMetaLines(metaPath, original, newLine);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                error = ex.Message;
                return false;
            }
        }
    }

    internal static string DetectNewLine(string text)
    {
        return !string.IsNullOrEmpty(text) && text.Contains("\r\n") ? "\r\n" : "\n";
    }

    internal static string JoinMetaLinesForWrite(IReadOnlyList<string> lines, string newLine)
    {
        return string.Join(string.IsNullOrEmpty(newLine) ? "\n" : newLine, lines ?? Array.Empty<string>()) +
               (string.IsNullOrEmpty(newLine) ? "\n" : newLine);
    }

    private static List<string> ReadMetaLines(string path)
    {
        return new List<string>(File.ReadAllLines(path));
    }

    private static void WriteMetaLines(string path, IReadOnlyList<string> lines, string newLine)
    {
        var tempPath = path + ".reanimator.tmp";
        var backupPath = path + ".reanimator.bak";
        File.WriteAllText(tempPath, JoinMetaLinesForWrite(lines, newLine));
        try
        {
            File.Replace(tempPath, path, backupPath);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(tempPath, path, true);
            File.Delete(tempPath);
        }
        catch (IOException)
        {
            File.Copy(tempPath, path, true);
            File.Delete(tempPath);
        }
        catch (UnauthorizedAccessException)
        {
            File.Copy(tempPath, path, true);
            File.Delete(tempPath);
        }
    }
}
#endif
