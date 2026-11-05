#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const int DeleteAllBonesRequiredPresses = 5;

    [SerializeField] private int deleteAllBonesRemainingPresses = DeleteAllBonesRequiredPresses;

    private string GetDeleteAllBonesButtonLabel()
    {
        var remaining = Mathf.Clamp(deleteAllBonesRemainingPresses, 1, DeleteAllBonesRequiredPresses);
        return $"DelBon x{remaining}";
    }

    private void PressDeleteAllBones()
    {
        CancelBoneRename();
        EndDepthDrag();

        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ResetDeleteAllBonesCounter();
            ShowNotification(new GUIContent("Select PSD/PSB first"));
            return;
        }

        deleteAllBonesRemainingPresses = Mathf.Clamp(deleteAllBonesRemainingPresses, 1, DeleteAllBonesRequiredPresses) - 1;
        if (deleteAllBonesRemainingPresses > 0)
        {
            ShowNotification(new GUIContent($"Press {deleteAllBonesRemainingPresses} more times"));
            Repaint();
            return;
        }

        ResetDeleteAllBonesCounter();
        if (!TryDeleteAllBones(path, out var error))
        {
            ReanimatorLog.Error($"[Reanimator] Delete Bones failed: {error}");
            ShowNotification(new GUIContent("Delete Bones failed"));
            return;
        }

        RefreshEditedPsdAfterReimport(path);
        ShowNotification(new GUIContent("All bones deleted"));
    }

    private void ResetDeleteAllBonesCounter()
    {
        deleteAllBonesRemainingPresses = DeleteAllBonesRequiredPresses;
    }

    private void CopyBonesFromAnotherPsd()
    {
        ResetDeleteAllBonesCounter();
        CancelBoneRename();
        EndDepthDrag();

        var targetPath = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(targetPath) || !IsPsdPath(targetPath))
        {
            ShowNotification(new GUIContent("Select target PSD/PSB first"));
            return;
        }

        var sourcePath = PickSourcePsdPath(targetPath);
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        if (string.Equals(NormalizeReanimatorAssetPath(sourcePath), NormalizeReanimatorAssetPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            ShowNotification(new GUIContent("Pick another PSD/PSB"));
            return;
        }

        if (!TryCopyBonesFromPsd(sourcePath, targetPath, out var copiedBones, out var mappedParts, out var error))
        {
            ReanimatorLog.Error($"[Reanimator] Copy Bones failed: {error}");
            ShowNotification(new GUIContent("Copy Bones failed"));
            return;
        }

        RefreshEditedPsdAfterReimport(targetPath);
        ShowNotification(new GUIContent($"Copied bones: {copiedBones}, parts: {mappedParts}"));
    }

    private static string PickSourcePsdPath(string targetPath)
    {
        var projectPath = NormalizeFilePath(Directory.GetCurrentDirectory());
        var targetFullPath = NormalizeFilePath(Path.GetFullPath(targetPath));
        var startFolder = NormalizeFilePath(Path.Combine(projectPath, "Assets"));

        if (!string.IsNullOrWhiteSpace(targetFullPath))
        {
            var targetFolder = Path.GetDirectoryName(targetFullPath);
            if (!string.IsNullOrWhiteSpace(targetFolder) && Directory.Exists(targetFolder))
                startFolder = NormalizeFilePath(targetFolder);
        }

        var pickedPath = EditorUtility.OpenFilePanelWithFilters(
            "Copy Bones source PSD",
            startFolder,
            new[] { "PSD/PSB files", "psd,psb" });
        if (string.IsNullOrWhiteSpace(pickedPath))
            return null;

        pickedPath = NormalizeFilePath(pickedPath);
        var assetsPrefix = NormalizeFilePath(Path.Combine(projectPath, "Assets")) + "/";
        if (!pickedPath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Copy Bones", "Source PSD must be inside this project's Assets folder.", "OK");
            return null;
        }

        return "Assets/" + pickedPath.Substring(assetsPrefix.Length);
    }

    private bool TryDeleteAllBones(string targetPath, out string error)
    {
        string rewriteError = null;
        if (ReanimatorAssetMetaPersistence.TryRewriteMetaLinesAndReimport(
                targetPath,
                "Delete Bones",
                lines =>
                {
                    if (!ReanimatorBoneUtilityAlgorithms.TryDeleteAllBonesFromMetaLines(lines, out rewriteError))
                        return false;

                    return true;
                },
                out var persistenceError))
        {
            error = null;
            return true;
        }

        error = rewriteError ?? persistenceError;
        return false;
    }

    private bool TryCopyBonesFromPsd(string sourcePath, string targetPath, out int copiedBones, out int mappedParts, out string error)
    {
        copiedBones = 0;
        mappedParts = 0;
        error = null;

        if (!ReanimatorAssetMetaPersistence.TryReadMetaLines(sourcePath, out var sourceLines, out var sourceError))
        {
            error = sourceError;
            return false;
        }

        string rewriteError = null;
        int copied = 0;
        int mapped = 0;
        if (ReanimatorAssetMetaPersistence.TryRewriteMetaLinesAndReimport(
                targetPath,
                "Copy Bones",
                targetLines =>
                {
                    if (!ReanimatorBoneUtilityAlgorithms.TryCopyBonesBetweenMetaLines(sourceLines, targetLines, out copied, out mapped, out rewriteError))
                        return false;

                    return true;
                },
                out var persistenceError))
        {
            copiedBones = copied;
            mappedParts = mapped;
            return true;
        }

        error = rewriteError ?? persistenceError;
        return false;
    }

    private void RefreshEditedPsdAfterReimport(string path)
    {
        layerPreviewRenderer?.Revert(path);
        layerPreviewRenderer?.MarkDirty();
        ReselectAsset(path);
        Repaint();
    }

    private static List<string> BuildEmptyCharacterDataBlock(string header, Vector2 pivot)
    {
        return ReanimatorBoneUtilityAlgorithms.BuildEmptyCharacterDataBlock(header, pivot);
    }

    private static List<string> BuildCopiedCharacterDataBlock(
        string header,
        IReadOnlyList<string> sourceBonesBlock,
        IReadOnlyDictionary<string, string> sourcePartsBySpriteId,
        IReadOnlyDictionary<string, string> sourceSpriteIdByName,
        IReadOnlyList<ReanimatorRigSpriteMeta> targetSprites,
        out int mappedParts)
    {
        return ReanimatorBoneUtilityAlgorithms.BuildCopiedCharacterDataBlock(
            header,
            sourceBonesBlock,
            sourcePartsBySpriteId,
            sourceSpriteIdByName,
            targetSprites,
            out mappedParts);
    }

    private static bool TryExtractBonesBlock(IReadOnlyList<string> lines, out List<string> bonesBlock, out int boneCount, out string error)
    {
        return ReanimatorBoneUtilityAlgorithms.TryExtractBonesBlock(lines, out bonesBlock, out boneCount, out error);
    }

    private static Dictionary<string, string> ParseCharacterPartBonesBySpriteId(IReadOnlyList<string> lines)
    {
        return ReanimatorBoneUtilityAlgorithms.ParseCharacterPartBonesBySpriteId(lines);
    }

    private static List<ReanimatorRigSpriteMeta> ParseRigSprites(IReadOnlyList<string> lines)
    {
        return ReanimatorBoneUtilityAlgorithms.ParseRigSprites(lines);
    }

    private static Dictionary<string, string> BuildSpriteIdByName(IReadOnlyList<ReanimatorRigSpriteMeta> sprites)
    {
        return ReanimatorBoneUtilityAlgorithms.BuildSpriteIdByName(sprites);
    }

    private static string ResolveSourceSpriteName(string targetName, IReadOnlyDictionary<string, string> sourceSpriteIdByName)
    {
        return ReanimatorBoneUtilityAlgorithms.ResolveSourceSpriteName(targetName, sourceSpriteIdByName);
    }

    private static bool ReplaceCharacterData(List<string> lines, string sectionHeader, IReadOnlyList<string> replacement)
    {
        return ReanimatorBoneUtilityAlgorithms.ReplaceCharacterData(lines, sectionHeader, replacement);
    }

    private static bool TryFindCharacterDataRange(IReadOnlyList<string> lines, string sectionHeader, out int start, out int end)
    {
        return ReanimatorBoneUtilityAlgorithms.TryFindCharacterDataRange(lines, sectionHeader, out start, out end);
    }

    private static void ClearSpriteBoneBlocks(List<string> lines)
    {
        ReanimatorBoneUtilityAlgorithms.ClearSpriteBoneBlocks(lines);
    }

    private static bool TryReadRect(IReadOnlyList<string> lines, int lineIndex, out Vector2 position, out Vector2 size)
    {
        return ReanimatorBoneUtilityAlgorithms.TryReadRect(lines, lineIndex, out position, out size);
    }

    private static bool TryReadInlineVector2(string line, out Vector2 value)
    {
        return ReanimatorBoneUtilityAlgorithms.TryReadInlineVector2(line, out value);
    }

    private static bool TryReadFloat(string line, string prefix, out float value)
    {
        return ReanimatorBoneUtilityAlgorithms.TryReadFloat(line, prefix, out value);
    }

    private static bool TryReadValue(string line, string prefix, out string value)
    {
        return ReanimatorBoneUtilityAlgorithms.TryReadValue(line, prefix, out value);
    }

    private static int FindTopLevelSection(IReadOnlyList<string> lines, string sectionHeader)
    {
        return ReanimatorBoneUtilityAlgorithms.FindTopLevelSection(lines, sectionHeader);
    }

    private static int CountIndent(string line)
    {
        return ReanimatorBoneUtilityAlgorithms.CountIndent(line);
    }

    private static string GetIndent(string line)
    {
        return ReanimatorBoneUtilityAlgorithms.GetIndent(line);
    }

    private static string NormalizeFilePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
    }

    private static string NormalizeGuidString(string guid)
    {
        return ReanimatorBoneUtilityAlgorithms.NormalizeGuidString(guid);
    }

    private static string FormatFloat(float value)
    {
        return ReanimatorBoneUtilityAlgorithms.FormatFloat(value);
    }
}
#endif
