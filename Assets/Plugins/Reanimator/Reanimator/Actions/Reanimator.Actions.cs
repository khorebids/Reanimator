#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    [MenuItem("KhorebdisLab/Reanimator/Repair Sprite Sheet PSD Params")]
    private static void RepairSpriteSheetPsdParametersFromMenu()
    {
        var window = GetWindow<Reanimator>();
        window.RepairSpriteSheetPsdParametersFromUi();
    }

    private void QueueApplyChanges()
    {
        if (pendingApplyPath != null)
            return;
        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ShowNotification(new GUIContent("Select PSD/PSB first"));
            return;
        }

        pendingApplyPath = path;
        pendingApplyStart = double.PositiveInfinity;
        EditorApplication.update += RunPendingApply;
        Repaint();
    }

    private void RunPendingApply()
    {
        if (EditorApplication.timeSinceStartup < pendingApplyStart)
            return;

        EditorApplication.update -= RunPendingApply;
        try
        {
            if (string.Equals(pendingApplyPath, GetSelectedAssetPath(), StringComparison.OrdinalIgnoreCase))
                ApplyChanges();
            else
                ShowNotification(new GUIContent("Apply canceled: selected PSD changed"));
        }
        finally
        {
            ClearPendingApply();
            Repaint();
        }
    }

    private void ClearPendingApply()
    {
        EditorApplication.update -= RunPendingApply;
        pendingApplyPath = null;
    }

    private void ApplyChanges()
    {
        CommitBoneRename();
        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ShowNotification(new GUIContent("Select PSD/PSB first"));
            return;
        }

        if (layerPreviewRenderer == null)
            return;

        var allowExternalOverwrite = false;
        if (layerPreviewRenderer.HasExternalAssetChanges(path))
        {
            var choice = EditorUtility.DisplayDialogComplex(
                "Reanimator source changed",
                "The PSD/importer changed outside Reanimator after this working snapshot was opened. Choose how to continue.",
                "Reload",
                "Cancel",
                "Overwrite");
            if (choice == 0)
            {
                layerPreviewRenderer.Revert(path);
                DeferPreviewContent();
                ShowNotification(new GUIContent("Reloaded"));
                Repaint();
                return;
            }

            if (choice == 1)
            {
                ShowNotification(new GUIContent("Apply canceled"));
                return;
            }

            allowExternalOverwrite = true;
        }

        CommitBoneEditRestPoseIfActive();
        layerPreviewRenderer.StopPreviewTestAnimation();
        isApplyingChanges = true;
        try
        {
            if (layerPreviewRenderer.Apply(path, allowExternalOverwrite, out var error))
            {
                DeferPreviewContent();
                ReselectAssetDeferred(path);
                ShowNotification(new GUIContent("Applied"));
                Repaint();
                return;
            }

            ReanimatorLog.Error($"[Reanimator] Apply failed: {error}");
            ShowNotification(new GUIContent("Apply failed"));
        }
        finally
        {
            isApplyingChanges = false;
        }
    }

    private void RevertChanges()
    {
        CancelBoneRename();
        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ShowNotification(new GUIContent("Select PSD/PSB first"));
            return;
        }

        if (layerPreviewRenderer != null && layerPreviewRenderer.HasPendingChanges)
            RegisterReanimatorUndo("Revert Reanimator Changes");
        layerPreviewRenderer?.Revert(path);
        DeferPreviewContent();
        ShowNotification(new GUIContent("Reverted"));
        Repaint();
    }

    private void ForceCleanPreviewCache()
    {
        CancelBoneRename();
        EndDepthDrag();
        InvalidateNamingIndicatorCaches(false);
        namingIndicatorDuplicateSelectionIndex = 0;
        layerPreviewRenderer?.MarkDirty();
        DeferPreviewContent();
        if (previewNavigator != null)
        {
            skinningPreviewNavigator = new PreviewNavigator();
            spriteSheetPreviewNavigator = new PreviewNavigator();
            comfimatorPreviewNavigator = new PreviewNavigator();
            previewNavigator = GetPreviewNavigator(previewMode);
        }
        ShowNotification(new GUIContent("Preview cache cleaned"));
        Repaint();
    }

    private void GenerateMissingGeometry()
    {
        CancelBoneRename();
        EndDepthDrag();

        var path = GetSelectedAssetPath();
        missingGeometryScanState.ClearAlert(NormalizeReanimatorAssetPath(path));
        TryGenerateMissingGeometry(path, true, true);
    }

    private void FixSelectedSpriteSkinWeights()
    {
        CancelBoneRename();
        EndDepthDrag();

        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ShowNotification(new GUIContent("Select PSD/PSB first"));
            return;
        }

        if (layerPreviewRenderer == null)
        {
            ShowNotification(new GUIContent("Preview renderer unavailable"));
            return;
        }

        layerPreviewRenderer.StopPreviewTestAnimation();
        if (!layerPreviewRenderer.FixSelectedSpriteSkinWeights(path, out var fixedVertexCount, out var savedChanges, out var layerName, out var error))
        {
            ReanimatorLog.Error($"[Reanimator] Fix Skin Weights failed: {error}");
            ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(error) ? "Fix Skin Weights failed" : error));
            return;
        }

        ReselectAsset(path);
        var message = fixedVertexCount > 0
            ? $"Fixed weights: {layerName} ({fixedVertexCount})"
            : savedChanges
                ? "Skin data saved"
                : "Skin weights already valid";
        ShowNotification(new GUIContent(message));
        Repaint();
    }

    private bool TryScheduleMissingGeometryScan(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
            return false;

        if (layerPreviewRenderer == null)
            return false;

        if (layerPreviewRenderer.IsPreviewCacheRefreshInProgress)
            return false;

        var normalizedPath = NormalizeReanimatorAssetPath(path);
        if (!missingGeometryScanState.ShouldScheduleScan(normalizedPath))
            return false;

        missingGeometryScanState.MarkScheduled(normalizedPath);
        EditorApplication.delayCall += () =>
        {
            if (!missingGeometryScanState.IsScheduledPath(normalizedPath))
                return;

            missingGeometryScanState.ClearScheduledPath(normalizedPath);

            var selectedPath = GetSelectedAssetPath();
            if (!string.Equals(NormalizeReanimatorAssetPath(selectedPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (layerPreviewRenderer == null)
                return;

            missingGeometryScanState.RecordScanResult(normalizedPath, layerPreviewRenderer.HasMissingGeometry());
            Repaint();
        };
        return true;
    }

    private bool TryGenerateMissingGeometry(string path, bool notifyUser, bool reselectAsset)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            if (notifyUser)
                ShowNotification(new GUIContent("Select PSD/PSB first"));
            return false;
        }

        if (layerPreviewRenderer == null)
        {
            if (notifyUser)
                ShowNotification(new GUIContent("Preview renderer unavailable"));
            return false;
        }

        if (!layerPreviewRenderer.GenerateMissingGeometry(path, out var generatedCount, out var error))
        {
            ReanimatorLog.Error($"[Reanimator] Generate Missing Geometry failed: {error}");
            if (notifyUser)
                ShowNotification(new GUIContent("Geometry generation failed"));
            return false;
        }

        ReanimatorLog.Trace(
            $"[Reanimator] Auto-generated missing geometry: {generatedCount}",
            generatedCount > 0 && !notifyUser && layerPreviewRenderer.IsDebugModeEnabled);

        if (reselectAsset)
            ReselectAsset(path);

        if (notifyUser)
            ShowNotification(new GUIContent(generatedCount > 0
                ? $"Generated geometry: {generatedCount}"
                : "No missing geometry"));
        Repaint();
        return true;
    }

    private void RestorePreviewPose()
    {
        CancelBoneRename();
        EndDepthDrag();
        if (layerPreviewRenderer == null)
        {
            ShowNotification(new GUIContent("Preview renderer unavailable"));
            return;
        }

        if (bottomToolMode == ReanimatorToolMode.EditBone)
        {
            CommitBoneEditRestPoseIfActive();
            ShowNotification(new GUIContent("Bones/Edit uses rest pose"));
            Repaint();
            return;
        }

        if (!layerPreviewRenderer.RestorePose())
        {
            ShowNotification(new GUIContent("Pose already restored"));
            return;
        }

        ShowNotification(new GUIContent("Pose restored"));
        Repaint();
    }

    private void ApplyAutoWeights()
    {
        CancelBoneRename();
        EndDepthDrag();
        if (layerPreviewRenderer == null)
        {
            ShowNotification(new GUIContent("Preview renderer unavailable"));
            return;
        }

        if (!layerPreviewRenderer.ApplyAutoWeightsToSelectedLayer(out var weightedCount, out var layerName, out var error))
        {
            ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(error) ? "Auto Weights failed" : error));
            return;
        }

        ShowNotification(new GUIContent($"Auto Weights: {layerName} ({weightedCount})"));
        Repaint();
    }

    private string GetDebugModeButtonLabel()
    {
        if (layerPreviewRenderer == null)
            return "Debug [OFF]";

        var state = layerPreviewRenderer.IsDebugModeEnabled ? "ON" : "OFF";
        if (layerPreviewRenderer.IsDebugModeDoublePressArmed)
            return $"Debug [{state}] x2";

        return $"Debug [{state}]";
    }

    private void ToggleDebugModeByDoublePress()
    {
        if (layerPreviewRenderer == null)
        {
            ShowNotification(new GUIContent("Preview renderer unavailable"));
            return;
        }

        var result = layerPreviewRenderer.ToggleDebugModeByDoublePress();
        if (result == PsdPreviewDebugMode.PressResult.Armed)
            ShowNotification(new GUIContent("Press Debug again to toggle"));
        else
            ShowNotification(new GUIContent(layerPreviewRenderer.IsDebugModeEnabled ? "Debug ON" : "Debug OFF"));

        Repaint();
    }

    [MenuItem("KhorebdisLab/Reanimator/Toggle Debug Diagnostics")]
    private static void ToggleDebugDiagnosticsFromMenu()
    {
        var window = GetWindow<Reanimator>();
        if (window.layerPreviewRenderer == null)
            window.layerPreviewRenderer = new PsdLayerPreviewRenderer();

        window.ToggleDebugModeByDoublePress();
        window.ToggleDebugModeByDoublePress();
    }

    private static void ReselectAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset != null)
            Selection.activeObject = asset;

        EditorApplication.delayCall += () =>
        {
            var delayedAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (delayedAsset == null)
                return;

            Selection.activeObject = delayedAsset;
            EditorGUIUtility.PingObject(delayedAsset);
        };
    }

    private static void ReselectAssetDeferred(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        EditorApplication.delayCall += () => ReselectAsset(path);
    }

    private string GetSelectedAssetPath()
    {
        TryUpdateActiveAssetPathFromSelection();
        return string.IsNullOrWhiteSpace(activeAssetPath) ? null : activeAssetPath;
    }

    private bool TryUpdateActiveAssetPathFromSelection()
    {
        if (!TryGetSelectedCompatibleAssetPath(out var selectedPath))
            return false;

        selectedPath = NormalizeReanimatorAssetPath(selectedPath);
        if (string.Equals(activeAssetPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            return false;

        activeAssetPath = selectedPath;
        return true;
    }

    private static bool TryGetSelectedCompatibleAssetPath(out string path)
    {
        path = null;
        var active = Selection.activeObject;
        if (active == null)
            return false;

        var selectedPath = AssetDatabase.GetAssetPath(active);
        if (string.IsNullOrWhiteSpace(selectedPath) || !IsPsdImporterAsset(selectedPath))
            return false;

        path = selectedPath;
        return true;
    }

    internal static bool IsPsdPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && (path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsPsdImporterAsset(string path)
    {
        return IsPsdPath(path) && AssetImporter.GetAtPath(path) is PSDImporter;
    }

    private static string NormalizeReanimatorAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
    }
}
#endif


