#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private void DrawSpriteSheetLeftTools()
    {
        EditorGUILayout.LabelField("Sprite Sheet", EditorStyles.boldLabel);

        leftToolsScroll = GUILayout.BeginScrollView(
            leftToolsScroll,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
            GUILayout.ExpandHeight(true));

        DrawToolSection("Tools", ref spriteSheetToolsExpanded, DrawSpriteSheetMainTools);
        DrawToolSection("Repair", ref spriteSheetRepairExpanded, DrawSpriteSheetRepairTools);
        DrawToolSection("ToolKit", ref spriteSheetToolkitExpanded, DrawSpriteSheetToolkitTools);

        GUILayout.EndScrollView();
    }

    private void DrawComfimatorLeftTools()
    {
        if (ReanimatorExternalPanelRegistry.TryGet("Comfimator", out var panel))
        {
            panel.DrawLeftPanel(this);
            return;
        }

        EditorGUILayout.LabelField("Comfimator", EditorStyles.boldLabel);
        leftToolsScroll = GUILayout.BeginScrollView(
            leftToolsScroll,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
        GUILayout.ExpandHeight(true));

        DrawToolSection("Tools", ref spriteSheetToolkitExpanded, DrawComfimatorTools);
        DrawToolSection("ALLIN", ref spriteSheetToolsExpanded, DrawComfimatorAllInTools);

        GUILayout.EndScrollView();
    }

    private void DrawComfimatorRightPanel(float availableHeight)
    {
        if (ReanimatorExternalPanelRegistry.TryGet("Comfimator", out var panel))
        {
            panel.DrawRightPanel(this, availableHeight);
            return;
        }

        DrawEmptyToolPanel(availableHeight);
    }

    private void DrawComfimatorTools()
    {
        if (DrawLeftToolButton("Auto Geometry", mutedWarning: true))
            InvokeComfimatorButton("Auto Geometry");

        if (DrawLeftToolButton("Auto Skeleton", mutedWarning: true))
            InvokeComfimatorButton("Auto Skeleton");

        if (DrawLeftToolButton("Auto Weights", mutedWarning: true))
            InvokeComfimatorButton("Auto Weights");
    }

    private void DrawComfimatorAllInTools()
    {
        using (new EditorGUI.DisabledScope(!TryValidateAllInPreflight(out _)))
        {
            if (DrawLeftToolButton("ALLIN", mutedWarning: true))
                InvokeComfimatorButton("ALLIN");
        }
    }

    private void InvokeComfimatorButton(string label)
    {
        if (string.Equals((label ?? string.Empty).Trim(), "ALLIN", System.StringComparison.OrdinalIgnoreCase) &&
            !TryValidateAllInPreflight(out var preflightError))
        {
            ShowNotification(new GUIContent("ALLIN blocked: " + preflightError));
            ReanimatorLog.Trace("[Reanimator][ALLIN] blocked before start: " + preflightError, true);
            Repaint();
            return;
        }

        if (ReanimatorExternalPanelRegistry.TryGet("Comfimator", out var panel) &&
            panel is IReanimatorExternalCommandPanel commandPanel &&
            commandPanel.InvokeCommand(label, this))
        {
            return;
        }

        ShowNotification(new GUIContent($"{label}: not implemented"));
        Repaint();
    }

    private void DrawSpriteSheetMainTools()
    {
        if (DrawLeftToolButton("Geometry", spriteSheetGeometryMode))
        {
            spriteSheetGeometryMode = !spriteSheetGeometryMode;
            Repaint();
        }

        using (new EditorGUI.DisabledScope(layerPreviewRenderer == null))
        {
            if (DrawLeftToolButton("Optimize"))
            {
                var changed = layerPreviewRenderer != null
                    ? layerPreviewRenderer.OptimizeSpriteSheetRectsToGeometry()
                    : 0;
                ReanimatorLog.Trace(
                    $"Reanimator Sprite Sheet Optimize: updated {changed} sprite rect(s).",
                    layerPreviewRenderer != null && layerPreviewRenderer.IsDebugModeEnabled);
                Repaint();
            }

            if (DrawLeftToolButton("Default Geometry"))
                GenerateDefaultSpriteSheetGeometryFromUi();
        }
    }

    private void DrawSpriteSheetRepairTools()
    {
        var labels = GetSpriteSheetRepairButtonLabels();
        using (new EditorGUI.DisabledScope(layerPreviewRenderer == null))
        {
            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (!DrawLeftToolButton(label, mutedWarning: true))
                    continue;

                InvokeSpriteSheetRepairButton(label);
            }
        }
    }

    private void DrawSpriteSheetToolkitTools()
    {
        if (DrawLeftToolButton("Fill Missing Sprites", mutedWarning: true))
            FillMissingSpritesFromSelectedSpriteSheet();

        if (DrawLeftToolButton("Assign Bones On Selected GameObject", mutedWarning: true))
            RunSpriteSheetToolkitAction(SpriteSkinPrefabBoneAssignerWindow.AssignSelectedInOpenPrefab);

        using (new EditorGUI.DisabledScope(!SpriteSkinPrefabBoneAssignerWindow.CanRecreateSkeletonForSelection(Selection.activeGameObject)))
        {
            if (DrawLeftToolButton("Recreate Skeleton", mutedWarning: true))
                RunSpriteSheetToolkitAction(SpriteSkinPrefabBoneAssignerWindow.RecreateSelectedSkeleton);

            if (DrawLeftToolButton("Rename bone_N From Character Skeleton", mutedWarning: true))
                RunSpriteSheetToolkitAction(SpriteSkinPrefabBoneAssignerWindow.RenameSelectedLinkedPsdBonePlaceholders);
        }

        if (!string.IsNullOrWhiteSpace(spriteSheetToolkitStatus))
            EditorGUILayout.LabelField(spriteSheetToolkitStatus, EditorStyles.wordWrappedMiniLabel);
    }

    private void RunSpriteSheetToolkitAction(System.Func<string> action)
    {
        if (action == null)
            return;

        spriteSheetToolkitStatus = action();
        if (!string.IsNullOrWhiteSpace(spriteSheetToolkitStatus))
            ShowNotification(new GUIContent(spriteSheetToolkitStatus));
        Repaint();
    }

    private void FillMissingSpritesFromSelectedSpriteSheet()
    {
        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
            path = activeAssetPath;

        if (string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            spriteSheetToolkitStatus = "Select PSD/PSB sprite sheet in Reanimator.";
            ShowNotification(new GUIContent(spriteSheetToolkitStatus));
            return;
        }

        var stats = SpriteSkinPrefabBoneAssignerWindow.FillMissingSpritesFromAsset(
            path,
            Selection.gameObjects,
            includeChildren: true);
        spriteSheetToolkitStatus = stats.ToStatus();
        ShowNotification(new GUIContent(spriteSheetToolkitStatus));
        Repaint();
    }

    internal static IReadOnlyList<string> GetSpriteSheetRepairButtonLabels()
    {
        return SpriteSheetRepairButtonLabels;
    }

    private void InvokeSpriteSheetRepairButton(string label)
    {
        switch (label)
        {
            case SpriteSheetRepairPsdParamsLabel:
                RepairSpriteSheetPsdParametersFromUi();
                break;
            case "Generate Missing Geometry":
                GenerateMissingGeometry();
                break;
            case "Fix Skin Weights":
                FixSelectedSpriteSkinWeights();
                break;
            case "Refresh Cache":
                ForceCleanPreviewCache();
                break;
        }
    }

    private bool RepairSpriteSheetPsdParametersFromUi()
    {
        var path = GetSelectedAssetPath();
        if (layerPreviewRenderer == null || string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ShowNotification(new GUIContent("Select PSD sprite"));
            return false;
        }

        layerPreviewRenderer.StopPreviewTestAnimation();
        if (!layerPreviewRenderer.RepairSpriteSheetImporterParameters(path, out var repairedGroups, out var error))
        {
            ReanimatorLog.Error($"[Reanimator] Repair PSD Params failed: {error}");
            ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(error) ? "Repair failed" : error));
            return false;
        }

        DeferPreviewContent();
        ReselectAssetDeferred(path);
        ReanimatorLog.Trace(
            $"Reanimator Sprite Sheet Repair PSD Params: repaired {repairedGroups} parameter group(s).",
            layerPreviewRenderer.IsDebugModeEnabled);
        ShowNotification(new GUIContent(repairedGroups > 0 ? $"PSD params repaired: {repairedGroups}" : "PSD params already valid"));
        Repaint();
        return true;
    }

    private bool GenerateDefaultSpriteSheetGeometryFromUi()
    {
        var path = GetSelectedAssetPath();
        if (layerPreviewRenderer == null || string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ShowNotification(new GUIContent("Select PSD sprite"));
            return false;
        }

        var generated = layerPreviewRenderer.GenerateDefaultGeometryForSpriteSheet(path, out var error);
        if (generated > 0)
        {
            ReanimatorLog.Trace(
                $"Reanimator Sprite Sheet Default Geometry: generated {generated} sprite geometry item(s).",
                layerPreviewRenderer.IsDebugModeEnabled);
            ShowNotification(new GUIContent("Default geometry generated"));
            Repaint();
            return true;
        }

        ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Generate failed" : error));
        return false;
    }
}
#endif
