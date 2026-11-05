#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    private void BuildBones(
        PSDImporter importer,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        bones.Clear();
        boneDrawOrder.Clear();
        if (importer == null)
            return;

        var dataProvider = importer as ISpriteEditorDataProvider;
        if (dataProvider == null)
            return;

        try
        {
            dataProvider.InitSpriteEditorDataProvider();
        }
        catch
        {
            // Continue with fallback parsing below.
        }
        var importedBoneColors = LoadBoneColorsFromSerializedImporter(importer);

        var loadedFromProvider = false;
        if (dataProvider.HasDataProvider(typeof(ICharacterDataProvider)))
        {
            var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
            if (characterProvider != null)
            {
                var data = characterProvider.GetCharacterData();
                if (data.bones != null && data.bones.Length > 0)
                {
                    for (var i = 0; i < data.bones.Length; i++)
                    {
                        var bone = data.bones[i];
                        var info = new BoneInfo
                        {
                            index = i,
                            name = string.IsNullOrEmpty(bone.name) ? $"bone_{i}" : bone.name,
                            parentId = bone.parentId,
                            position = new Vector2(bone.position.x, bone.position.y),
                            rotation = bone.rotation,
                            initialPosition = new Vector2(bone.position.x, bone.position.y),
                            initialRotation = bone.rotation,
                            length = bone.length,
                            depth = ReanimatorBoneOrderAlgorithms.ReadBoneOrder(bone.position),
                            color = ResolveBoneColorFromFile(importedBoneColors, i),
                            visible = true
                        };

                        if (reuseVisibility)
                        {
                            var key = BuildBoneKey(info.name, info.index);
                            if (previousVisibility.TryGetValue(key, out var wasVisible))
                                info.visible = wasVisible;
                            if (previousDepth.TryGetValue(info.index, out var depth))
                                info.depth = depth;
                            if (previousColor.TryGetValue(info.index, out var color))
                                info.color = color;
                        }

                        bones.Add(info);
                    }

                    loadedFromProvider = true;
                }
            }
        }

        if (!loadedFromProvider)
            TryLoadBonesFromSerializedImporter(importer, importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor);
        if (!loadedFromProvider && bones.Count == 0)
            TryLoadBonesFromMetaText(importer, importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor);

        FinalizeImportedBones(IsDebugModeEnabled);
    }

    private void BuildBonesFromMetaFile(
        string assetPath,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        bones.Clear();
        boneDrawOrder.Clear();
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        TryLoadBonesFromMetaPath(assetPath, null, reuseVisibility, previousVisibility, previousDepth, previousColor);
        FinalizeImportedBones(false);
    }

    private IEnumerable BuildBonesFromMetaFileIncremental(
        string assetPath,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        bones.Clear();
        boneDrawOrder.Clear();
        if (string.IsNullOrWhiteSpace(assetPath))
            yield break;

        var metaPath = assetPath + ".meta";
        if (!File.Exists(metaPath))
            yield break;

        SetPreviewCacheRefreshProgress(0.640f, "Bones: scheduling .meta scan");
        yield return null;

        var parseProgress = new MetaBoneParseProgress();
        var parseTask = System.Threading.Tasks.Task.Run(() => ParseBoneMetaFileOnWorker(metaPath, parseProgress));
        while (!parseTask.IsCompleted)
        {
            parseProgress.Snapshot(out var progress, out var status, out var line, out var parsed);
            SetPreviewCacheRefreshProgress(progress, $"{status}; line {line}; parsed {parsed}");
            yield return null;
        }

        List<BoneMetaTemp> parsedBones;
        try
        {
            parsedBones = parseTask.Result;
        }
        catch (Exception ex)
        {
            ReanimatorLog.Exception(ex);
            yield break;
        }

        if (parsedBones != null && parsedBones.Count > 0)
        {
            for (var i = 0; i < parsedBones.Count; i++)
            {
                TryAddParsedBone(parsedBones[i], null, reuseVisibility, previousVisibility, previousDepth, previousColor);
                if ((i & 15) == 15)
                {
                    SetPreviewCacheRefreshProgress(0.675f, $"Bones: applying {i + 1}/{parsedBones.Count}");
                    yield return null;
                }
            }

            SetPreviewCacheRefreshProgress(0.675f, $"Bones: applied {bones.Count}");
            yield return null;
        }

        foreach (var _ in FinalizeImportedBonesIncremental())
            yield return null;
    }

    private static float CalculateBoneMetaProgress(long consumedChars, long totalBytes)
    {
        var t = totalBytes <= 0L ? 0f : Mathf.Clamp01(consumedChars / (float)totalBytes);
        return Mathf.Lerp(0.640f, 0.675f, t);
    }

    private static List<BoneMetaTemp> ParseBoneMetaFileOnWorker(string metaPath, MetaBoneParseProgress progress)
    {
        var result = new List<BoneMetaTemp>();
        if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
            return result;

        var totalBytes = 1L;
        try
        {
            totalBytes = Math.Max(1L, new FileInfo(metaPath).Length);
        }
        catch
        {
            totalBytes = 1L;
        }

        progress.Update(0.640f, "Bones: scanning .meta in worker", 0, 0);

        var state = MetaBoneParseState.SearchingSection;
        var sectionIndent = 0;
        var bonesIndent = 0;
        var parsedAny = false;
        BoneMetaTemp current = default;
        var hasCurrent = false;
        var lineIndex = 0;
        var consumedChars = 0L;
        var sectionName = string.Empty;
        var status = "Bones: scanning .meta in worker";

        foreach (var rawLine in File.ReadLines(metaPath))
        {
            var line = rawLine ?? string.Empty;
            var trimmed = line.TrimStart();
            var indent = CountIndent(line);
            lineIndex++;
            consumedChars += line.Length + 1L;

            switch (state)
            {
                case MetaBoneParseState.SearchingSection:
                    if (trimmed.StartsWith("characterData:", StringComparison.Ordinal) ||
                        trimmed.StartsWith("sharedRigCharacterData:", StringComparison.Ordinal))
                    {
                        sectionIndent = indent;
                        sectionName = trimmed.StartsWith("characterData:", StringComparison.Ordinal)
                            ? "characterData"
                            : "sharedRigCharacterData";
                        state = MetaBoneParseState.SearchingBones;
                        status = $"Bones: found {sectionName}";
                    }
                    break;

                case MetaBoneParseState.SearchingBones:
                    if (indent <= sectionIndent && trimmed.EndsWith(":", StringComparison.Ordinal))
                    {
                        state = MetaBoneParseState.SearchingSection;
                        break;
                    }

                    if (trimmed.StartsWith("bones:", StringComparison.Ordinal))
                    {
                        if (trimmed.IndexOf("[]", StringComparison.Ordinal) >= 0)
                        {
                            state = MetaBoneParseState.SearchingSection;
                            status = $"Bones: empty bones in {sectionName}";
                            break;
                        }

                        bonesIndent = indent;
                        state = MetaBoneParseState.ReadingBones;
                        status = $"Bones: reading bones in {sectionName}";
                    }
                    break;

                case MetaBoneParseState.ReadingBones:
                    if (indent <= bonesIndent && trimmed.EndsWith(":", StringComparison.Ordinal))
                    {
                        if (hasCurrent && TryCollectParsedBone(current, result))
                            parsedAny = true;
                        hasCurrent = false;
                        if (parsedAny)
                        {
                            progress.Update(0.675f, "Bones: worker parsed bone block", lineIndex, result.Count);
                            return result;
                        }

                        state = MetaBoneParseState.SearchingSection;
                        break;
                    }

                    if (trimmed.Length == 0)
                        break;

                    if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                    {
                        if (hasCurrent && TryCollectParsedBone(current, result))
                        {
                            parsedAny = true;
                            status = $"Bones: parsed {result.Count}";
                        }

                        current = default;
                        hasCurrent = true;

                        var inline = trimmed.Substring(2).Trim();
                        if (inline.StartsWith("name:", StringComparison.Ordinal))
                            current.name = inline.Substring(5).Trim();
                        break;
                    }

                    if (hasCurrent)
                        ApplyBoneMetaLine(trimmed, ref current);
                    break;
            }

            if ((lineIndex & 127) == 0)
            {
                progress.Update(
                    CalculateBoneMetaProgress(consumedChars, totalBytes),
                    status,
                    lineIndex,
                    result.Count);
            }
        }

        if (hasCurrent)
            TryCollectParsedBone(current, result);
        progress.Update(0.675f, "Bones: worker finished .meta scan", lineIndex, result.Count);
        return result;
    }

    private static bool TryCollectParsedBone(BoneMetaTemp parsed, List<BoneMetaTemp> result)
    {
        if (string.IsNullOrEmpty(parsed.name) || result == null)
            return false;
        result.Add(parsed);
        return true;
    }

    private sealed class MetaBoneParseProgress
    {
        private readonly object sync = new object();
        private float progress = 0.640f;
        private string status = "Bones: queued .meta scan";
        private int line;
        private int parsed;

        public void Update(float progress, string status, int line, int parsed)
        {
            lock (sync)
            {
                this.progress = progress;
                this.status = status ?? string.Empty;
                this.line = line;
                this.parsed = parsed;
            }
        }

        public void Snapshot(out float progress, out string status, out int line, out int parsed)
        {
            lock (sync)
            {
                progress = this.progress;
                status = this.status;
                line = this.line;
                parsed = this.parsed;
            }
        }
    }

    private void FinalizeImportedBones(bool collectDebugLayoutScores)
    {
        if (bones.Count == 0)
            return;

        CaptureInitialBonePoseFromCurrent();

        // Match Unity's Skinning Editor import path:
        // SkinningCache.CreateBoneCacheFromSpriteBones assigns SpriteBone.position
        // to BoneCache.localPosition, so children are local to their parent and
        // parent rotation affects their world bases. Picking a "better fitting"
        // world/raw layout here makes Reanimator diverge from the standard editor
        // and can write back positions that the standard editor interprets wrong.
        bonesTreatAsWorldSpace = false;
        rotateLocalBoneOffsetsByParent = true;
        debugChosenBoneMode = "UNITY_LOCAL_ROT";

        RecomputeBoneWorldTransforms();

        if (collectDebugLayoutScores)
        {
            var contentBounds = GetLayerContentBoundsDoc();
            var modeLocalRot = EvaluateBoneLayoutScore(contentBounds, false, true);
            var modeLocalNoRot = EvaluateBoneLayoutScore(contentBounds, false, false);
            var modeWorld = EvaluateBoneLayoutScore(contentBounds, true, true);
            debugContentBoundsDoc = contentBounds;
            debugModeScoreLocalRot = modeLocalRot;
            debugModeScoreLocalNoRot = modeLocalNoRot;
            debugModeScoreWorld = modeWorld;
            debugBoneBoundsDocBefore = GetBoneWorldBounds();
            debugRawBoneBoundsDoc = GetRawWorldBoneBounds();
            debugBoneBoundsDocAfter = GetBoneWorldBounds();

            ReanimatorLog.Trace(
                $"[PSD DEBUG RED] BoneImport mode={debugChosenBoneMode} " +
                $"scores(LR={modeLocalRot:F3}, LN={modeLocalNoRot:F3}, W={modeWorld:F3}) " +
                $"content={RectToDebugString(debugContentBoundsDoc)} " +
                $"raw={RectToDebugString(debugRawBoneBoundsDoc)} " +
                $"before={RectToDebugString(debugBoneBoundsDocBefore)} " +
                $"after={RectToDebugString(debugBoneBoundsDocAfter)} " +
                $"count={bones.Count}",
                collectDebugLayoutScores);
        }

        InvalidateBonePose();
        BuildBoneDrawOrder();
    }

    private IEnumerable FinalizeImportedBonesIncremental()
    {
        SetPreviewCacheRefreshProgress(0.680f, $"Bones: finalizing {bones.Count}");
        yield return null;

        if (bones.Count == 0)
            yield break;

        SetPreviewCacheRefreshProgress(0.684f, "Bones: capturing rest pose");
        CaptureInitialBonePoseFromCurrent();
        yield return null;

        SetPreviewCacheRefreshProgress(0.688f, "Bones: setting Unity local mode");
        bonesTreatAsWorldSpace = false;
        rotateLocalBoneOffsetsByParent = true;
        debugChosenBoneMode = "UNITY_LOCAL_ROT";
        yield return null;

        SetPreviewCacheRefreshProgress(0.692f, "Bones: resolving world transforms");
        RecomputeBoneWorldTransforms();
        yield return null;

        SetPreviewCacheRefreshProgress(0.696f, "Bones: sorting draw order");
        InvalidateBonePose();
        BuildBoneDrawOrder();
        yield return null;
    }

    private bool TryLoadBonesFromSerializedImporter(
        PSDImporter importer,
        IReadOnlyDictionary<int, Color> importedBoneColors,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        if (importer == null)
            return false;

        var so = new SerializedObject(importer);
        var characterData = so.FindProperty("m_CharacterData") ?? so.FindProperty("characterData");
        if (characterData == null)
            return false;

        var bonesProp = characterData.FindPropertyRelative("bones") ?? characterData.FindPropertyRelative("m_Bones");
        if (bonesProp == null || !bonesProp.isArray || bonesProp.arraySize <= 0)
            return false;

        var startCount = bones.Count;
        for (var i = 0; i < bonesProp.arraySize; i++)
        {
            var boneProp = bonesProp.GetArrayElementAtIndex(i);
            if (boneProp == null)
                continue;

            var name = GetStringProperty(boneProp, "name", "m_Name");
            var parentId = GetIntProperty(boneProp, -1, "parentId", "m_ParentId");
            var length = GetFloatProperty(boneProp, 0f, "length", "m_Length");
            var serializedPosition = ReanimatorSerializedPropertyAlgorithms.ReadVector3(
                boneProp.FindPropertyRelative("position") ?? boneProp.FindPropertyRelative("m_Position"));
            var position = new Vector2(serializedPosition.x, serializedPosition.y);
            var rotation = GetQuaternionFromProperty(boneProp.FindPropertyRelative("rotation") ?? boneProp.FindPropertyRelative("m_Rotation"));

            var info = new BoneInfo
            {
                index = i,
                name = string.IsNullOrEmpty(name) ? $"bone_{i}" : name,
                parentId = parentId,
                position = position,
                rotation = rotation,
                initialPosition = position,
                initialRotation = rotation,
                length = length,
                depth = ReanimatorBoneOrderAlgorithms.ReadBoneOrder(serializedPosition),
                color = ResolveBoneColorFromFile(importedBoneColors, i),
                visible = true
            };

            if (reuseVisibility)
            {
                var key = BuildBoneKey(info.name, info.index);
                if (previousVisibility.TryGetValue(key, out var wasVisible))
                    info.visible = wasVisible;
                if (previousDepth.TryGetValue(info.index, out var depth))
                    info.depth = depth;
                if (previousColor.TryGetValue(info.index, out var color))
                    info.color = color;
            }

            bones.Add(info);
        }

        return bones.Count > startCount;
    }

    private bool TryLoadBonesFromMetaText(
        PSDImporter importer,
        IReadOnlyDictionary<int, Color> importedBoneColors,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        if (importer == null)
            return false;

        var assetPath = AssetDatabase.GetAssetPath(importer);
        if (string.IsNullOrEmpty(assetPath))
            return false;

        var metaPath = assetPath + ".meta";
        if (!File.Exists(metaPath))
            return false;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(metaPath);
        }
        catch
        {
            return false;
        }

        var startCount = bones.Count;
        if (!ParseBonesFromMetaSection(lines, "characterData:", importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor))
            ParseBonesFromMetaSection(lines, "sharedRigCharacterData:", importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor);

        return bones.Count > startCount;
    }

    private bool TryLoadBonesFromMetaPath(
        string assetPath,
        IReadOnlyDictionary<int, Color> importedBoneColors,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        var metaPath = assetPath + ".meta";
        if (!File.Exists(metaPath))
            return false;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(metaPath);
        }
        catch
        {
            return false;
        }

        var startCount = bones.Count;
        if (!ParseBonesFromMetaSection(lines, "characterData:", importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor))
            ParseBonesFromMetaSection(lines, "sharedRigCharacterData:", importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor);

        return bones.Count > startCount;
    }

    private bool ParseBonesFromMetaSection(
        IReadOnlyList<string> lines,
        string sectionHeader,
        IReadOnlyDictionary<int, Color> importedBoneColors,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        if (lines == null || lines.Count == 0 || string.IsNullOrEmpty(sectionHeader))
            return false;

        var sectionIndex = -1;
        var sectionIndent = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i] ?? string.Empty;
            if (!line.TrimStart().StartsWith(sectionHeader, StringComparison.Ordinal))
                continue;
            sectionIndex = i;
            sectionIndent = CountIndent(line);
            break;
        }

        if (sectionIndex < 0)
            return false;

        var bonesLine = -1;
        var bonesIndent = sectionIndent + 2;
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.TrimStart();
            var indent = CountIndent(line);
            if (indent <= sectionIndent && trimmed.EndsWith(":", StringComparison.Ordinal))
                break;
            if (!trimmed.StartsWith("bones:", StringComparison.Ordinal))
                continue;
            if (trimmed.IndexOf("[]", StringComparison.Ordinal) >= 0)
                return false;
            bonesLine = i;
            bonesIndent = indent;
            break;
        }

        if (bonesLine < 0)
            return false;

        var parsedAny = false;
        BoneMetaTemp current = default;
        var hasCurrent = false;

        for (var i = bonesLine + 1; i < lines.Count; i++)
        {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.TrimStart();
            var indent = CountIndent(line);
            if (indent <= bonesIndent && trimmed.EndsWith(":", StringComparison.Ordinal))
                break;
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (hasCurrent && TryAddParsedBone(current, importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor))
                    parsedAny = true;

                current = default;
                hasCurrent = true;

                var inline = trimmed.Substring(2).Trim();
                if (inline.StartsWith("name:", StringComparison.Ordinal))
                    current.name = inline.Substring(5).Trim();
                continue;
            }

            if (!hasCurrent)
                continue;

            if (trimmed.StartsWith("name:", StringComparison.Ordinal))
            {
                current.name = trimmed.Substring(5).Trim();
                continue;
            }

            if (trimmed.StartsWith("position:", StringComparison.Ordinal))
            {
                current.position = ParseVector2FromInlineMap(trimmed);
                continue;
            }

            if (trimmed.StartsWith("rotation:", StringComparison.Ordinal))
            {
                current.rotation = ParseQuaternionFromInlineMap(trimmed);
                continue;
            }

            if (trimmed.StartsWith("length:", StringComparison.Ordinal))
            {
                current.length = ParseFloatInvariant(trimmed.Substring(7).Trim(), 0f);
                continue;
            }

            if (trimmed.StartsWith("parentId:", StringComparison.Ordinal))
            {
                current.parentId = ParseIntInvariant(trimmed.Substring(9).Trim(), -1);
                continue;
            }

            if (trimmed.StartsWith("color:", StringComparison.Ordinal))
            {
                current.color = ParseColorFromInlineMap(trimmed, DefaultBoneColor);
                current.hasColor = true;
                continue;
            }
        }

        if (hasCurrent && TryAddParsedBone(current, importedBoneColors, reuseVisibility, previousVisibility, previousDepth, previousColor))
            parsedAny = true;

        return parsedAny;
    }

    private static void ApplyBoneMetaLine(string trimmed, ref BoneMetaTemp current)
    {
        if (string.IsNullOrEmpty(trimmed))
            return;

        if (current.readingColor && trimmed.StartsWith("rgba:", StringComparison.Ordinal))
        {
            if (TryParseUnityPackedColor(trimmed.Substring(5).Trim(), out var packedColor))
            {
                current.color = packedColor;
                current.hasColor = true;
            }
            current.readingColor = false;
            return;
        }

        if (trimmed.StartsWith("name:", StringComparison.Ordinal))
        {
            current.name = trimmed.Substring(5).Trim();
            current.readingColor = false;
            return;
        }

        if (trimmed.StartsWith("position:", StringComparison.Ordinal))
        {
            current.position = ReanimatorBoneMetaAlgorithms.ParseVector3FromInlineMap(trimmed);
            current.readingColor = false;
            return;
        }

        if (trimmed.StartsWith("rotation:", StringComparison.Ordinal))
        {
            current.rotation = ParseQuaternionFromInlineMap(trimmed);
            current.readingColor = false;
            return;
        }

        if (trimmed.StartsWith("length:", StringComparison.Ordinal))
        {
            current.length = ParseFloatInvariant(trimmed.Substring(7).Trim(), 0f);
            current.readingColor = false;
            return;
        }

        if (trimmed.StartsWith("parentId:", StringComparison.Ordinal))
        {
            current.parentId = ParseIntInvariant(trimmed.Substring(9).Trim(), -1);
            current.readingColor = false;
            return;
        }

        if (trimmed.StartsWith("color:", StringComparison.Ordinal))
        {
            if (trimmed.IndexOf("rgba:", StringComparison.Ordinal) >= 0 &&
                TryParseUnityPackedColor(trimmed.Substring(trimmed.IndexOf("rgba:", StringComparison.Ordinal) + 5).Trim(), out var packedColor))
            {
                current.color = packedColor;
                current.hasColor = true;
                current.readingColor = false;
                return;
            }

            var inlineColor = ParseColorFromInlineMap(trimmed, DefaultBoneColor);
            current.readingColor = !trimmed.Contains("{");
            if (!current.readingColor)
            {
                current.color = inlineColor;
                current.hasColor = true;
            }
        }
    }

    private bool TryAddParsedBone(
        BoneMetaTemp parsed,
        IReadOnlyDictionary<int, Color> importedBoneColors,
        bool reuseVisibility,
        Dictionary<string, bool> previousVisibility,
        Dictionary<int, int> previousDepth,
        Dictionary<int, Color> previousColor)
    {
        if (string.IsNullOrEmpty(parsed.name))
            return false;

        var index = bones.Count;
        var info = new BoneInfo
        {
            index = index,
            name = parsed.name,
            parentId = parsed.parentId,
            position = new Vector2(parsed.position.x, parsed.position.y),
            rotation = parsed.rotation.Equals(default(Quaternion)) ? Quaternion.identity : parsed.rotation,
            initialPosition = new Vector2(parsed.position.x, parsed.position.y),
            initialRotation = parsed.rotation.Equals(default(Quaternion)) ? Quaternion.identity : parsed.rotation,
            length = parsed.length,
            depth = ReanimatorBoneOrderAlgorithms.ReadBoneOrder(parsed.position),
            color = parsed.hasColor ? SanitizeBoneColor(parsed.color) : ResolveBoneColorFromFile(importedBoneColors, index),
            visible = true
        };

        if (reuseVisibility)
        {
            var key = BuildBoneKey(info.name, info.index);
            if (previousVisibility.TryGetValue(key, out var wasVisible))
                info.visible = wasVisible;
            if (previousDepth.TryGetValue(info.index, out var depth))
                info.depth = depth;
            if (previousColor.TryGetValue(info.index, out var color))
                info.color = color;
        }

        bones.Add(info);
        return true;
    }

    private static Vector2 ParseVector2FromInlineMap(string line)
    {
        return ReanimatorBoneMetaAlgorithms.ParseVector2FromInlineMap(line);
    }

    private static Quaternion ParseQuaternionFromInlineMap(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Quaternion.identity;

        var mx = Regex.Match(line, @"x:\s*([-+0-9eE\.]+)");
        var my = Regex.Match(line, @"y:\s*([-+0-9eE\.]+)");
        var mz = Regex.Match(line, @"z:\s*([-+0-9eE\.]+)");
        var mw = Regex.Match(line, @"w:\s*([-+0-9eE\.]+)");
        if (!mx.Success || !my.Success || !mz.Success || !mw.Success)
            return Quaternion.identity;

        var x = ParseFloatInvariant(mx.Groups[1].Value, 0f);
        var y = ParseFloatInvariant(my.Groups[1].Value, 0f);
        var z = ParseFloatInvariant(mz.Groups[1].Value, 0f);
        var w = ParseFloatInvariant(mw.Groups[1].Value, 1f);
        return new Quaternion(x, y, z, w);
    }

    private static float ParseFloatInvariant(string text, float fallback)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        return fallback;
    }

    private static int ParseIntInvariant(string text, int fallback)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return fallback;
    }

    private static int CountIndent(string line)
    {
        if (string.IsNullOrEmpty(line))
            return 0;
        var i = 0;
        while (i < line.Length && line[i] == ' ')
            i++;
        return i;
    }

    private struct BoneMetaTemp
    {
        public string name;
        public int parentId;
        public float length;
        public Vector3 position;
        public Quaternion rotation;
        public bool hasColor;
        public bool readingColor;
        public Color color;
    }

    private enum MetaBoneParseState
    {
        SearchingSection,
        SearchingBones,
        ReadingBones
    }

    private static Color ParseColorFromInlineMap(string line, Color fallback)
    {
        if (string.IsNullOrEmpty(line))
            return fallback;

        var mr = Regex.Match(line, @"r:\s*([-+0-9eE\.]+)");
        var mg = Regex.Match(line, @"g:\s*([-+0-9eE\.]+)");
        var mb = Regex.Match(line, @"b:\s*([-+0-9eE\.]+)");
        var ma = Regex.Match(line, @"a:\s*([-+0-9eE\.]+)");
        if (!mr.Success || !mg.Success || !mb.Success)
            return fallback;

        var r = ParseFloatInvariant(mr.Groups[1].Value, fallback.r);
        var g = ParseFloatInvariant(mg.Groups[1].Value, fallback.g);
        var b = ParseFloatInvariant(mb.Groups[1].Value, fallback.b);
        var a = ma.Success ? ParseFloatInvariant(ma.Groups[1].Value, fallback.a) : fallback.a;
        return SanitizeBoneColor(new Color(r, g, b, a));
    }

    private static bool TryParseUnityPackedColor(string text, out Color color)
    {
        return ReanimatorBoneMetaAlgorithms.TryParseUnityPackedColor(text, DefaultBoneColor, out color);
    }

    private bool ApplyBoneChangesToImporter(PSDImporter importer)
    {
        if (importer == null || bones == null)
            return false;

        var so = new SerializedObject(importer);
        var changed = false;
        for (var i = 0; i < BoneCharacterDataRootNames.Length; i++)
        {
            var characterData = so.FindProperty(BoneCharacterDataRootNames[i]);
            if (characterData == null)
                continue;
            changed |= ApplyBoneFieldsToCharacterData(characterData);
        }

        if (!changed)
            return false;

        ApplyImporterSerializedProperties(so, importer);
        return true;
    }

    private bool ApplyBoneFieldsToCharacterData(SerializedProperty characterData)
    {
        if (characterData == null)
            return false;

        var bonesProp = characterData.FindPropertyRelative("bones") ?? characterData.FindPropertyRelative("m_Bones");
        if (bonesProp == null || !bonesProp.isArray)
            return false;

        var changed = false;
        if (bonesProp.arraySize != bones.Count)
        {
            bonesProp.arraySize = bones.Count;
            changed = true;
        }

        var limit = Mathf.Min(bones.Count, bonesProp.arraySize);
        for (var i = 0; i < limit; i++)
        {
            var bone = bones[i];
            var boneProp = bonesProp.GetArrayElementAtIndex(i);
            if (bone == null || boneProp == null)
                continue;

            changed |= WriteStringIfChanged(boneProp.FindPropertyRelative("name") ?? boneProp.FindPropertyRelative("m_Name"), bone.name);
            changed |= WriteIntIfChanged(boneProp.FindPropertyRelative("parentId") ?? boneProp.FindPropertyRelative("m_ParentId"), bone.parentId);
            changed |= ReanimatorSerializedPropertyAlgorithms.WriteVector3IfChanged(
                boneProp.FindPropertyRelative("position") ?? boneProp.FindPropertyRelative("m_Position"),
                new Vector3(
                    bone.position.x,
                    bone.position.y,
                    ReanimatorBoneOrderAlgorithms.EncodeBoneOrder(bone.depth)));
            changed |= WriteQuaternionIfChanged(boneProp.FindPropertyRelative("rotation") ?? boneProp.FindPropertyRelative("m_Rotation"), bone.rotation);
            changed |= WriteFloatIfChanged(boneProp.FindPropertyRelative("length") ?? boneProp.FindPropertyRelative("m_Length"), bone.length);

            var colorProp = boneProp.FindPropertyRelative("color") ?? boneProp.FindPropertyRelative("m_Color");
            var desiredColor = SanitizeBoneColor(bone.color);
            if (colorProp != null &&
                (!TryReadColorProperty(colorProp, out var currentColor) || !AreColorsApproximatelyEqual(currentColor, desiredColor)))
            {
                WriteColorProperty(colorProp, desiredColor);
                changed = true;
            }
        }

        return changed;
    }

    private static bool WriteFloatIfChanged(SerializedProperty property, float value)
    {
        return ReanimatorSerializedPropertyAlgorithms.WriteFloatIfChanged(property, value);
    }

    private static bool WriteStringIfChanged(SerializedProperty property, string value)
    {
        return ReanimatorSerializedPropertyAlgorithms.WriteStringIfChanged(property, value);
    }

    private static bool WriteVector2IfChanged(SerializedProperty property, Vector2 value)
    {
        return ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(property, value);
    }

    private static bool WriteQuaternionIfChanged(SerializedProperty property, Quaternion value)
    {
        return ReanimatorSerializedPropertyAlgorithms.WriteQuaternionIfChanged(property, value);
    }

    private static bool AreQuaternionsApproximatelyEqual(Quaternion lhs, Quaternion rhs)
    {
        return ReanimatorBoneMetaAlgorithms.AreQuaternionsApproximatelyEqual(lhs, rhs);
    }

    private static Dictionary<int, Color> LoadBoneColorsFromSerializedImporter(PSDImporter importer)
    {
        var result = new Dictionary<int, Color>();
        if (importer == null)
            return result;

        var so = new SerializedObject(importer);
        for (var rootIndex = 0; rootIndex < BoneCharacterDataRootNames.Length; rootIndex++)
        {
            var characterData = so.FindProperty(BoneCharacterDataRootNames[rootIndex]);
            if (characterData == null)
                continue;

            var bonesProp = characterData.FindPropertyRelative("bones") ?? characterData.FindPropertyRelative("m_Bones");
            if (bonesProp == null || !bonesProp.isArray || bonesProp.arraySize <= 0)
                continue;

            for (var i = 0; i < bonesProp.arraySize; i++)
            {
                var boneProp = bonesProp.GetArrayElementAtIndex(i);
                if (!TryReadBoneColorFromProperty(boneProp, out var color))
                    continue;
                result[i] = SanitizeBoneColor(color);
            }

            if (result.Count > 0)
                break;
        }

        return result;
    }

    private static Color ResolveBoneColorFromFile(IReadOnlyDictionary<int, Color> importedBoneColors, int boneIndex)
    {
        if (importedBoneColors != null && importedBoneColors.TryGetValue(boneIndex, out var color))
            return SanitizeBoneColor(color);
        return DefaultBoneColor;
    }

    private static bool TryReadBoneColorFromProperty(SerializedProperty boneProp, out Color color)
    {
        color = DefaultBoneColor;
        if (boneProp == null)
            return false;

        var colorProp = boneProp.FindPropertyRelative("color") ?? boneProp.FindPropertyRelative("m_Color");
        return TryReadColorProperty(colorProp, out color);
    }

    private static bool TryReadColorProperty(SerializedProperty colorProp, out Color color)
    {
        return ReanimatorSerializedPropertyAlgorithms.TryReadColorProperty(colorProp, DefaultBoneColor, out color);
    }

    private static void WriteColorProperty(SerializedProperty colorProp, Color color)
    {
        ReanimatorSerializedPropertyAlgorithms.WriteColorProperty(colorProp, color);
    }

    private static bool AreColorsApproximatelyEqual(Color lhs, Color rhs)
    {
        return ReanimatorBoneMetaAlgorithms.AreColorsApproximatelyEqual(lhs, rhs);
    }

    private static Color SanitizeBoneColor(Color color)
    {
        if (color.a <= 0.0001f)
            color.a = DefaultBoneColor.a;
        return color;
    }

    private static readonly string[] BoneCharacterDataRootNames =
    {
        "m_CharacterData",
        "characterData",
        "m_SharedRigCharacterData",
        "sharedRigCharacterData"
    };

    private static readonly Color DefaultBoneColor = new Color(0.25f, 0.9f, 0.4f, 0.95f);
}
#endif
