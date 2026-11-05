#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const string SpriteSheetRepairPsdParamsLabel = "Repair PSD Params";
    private const string LeftToolIconSheetPath = "Assets/Plugins/Reanimator/Graphics/icons.psd";
    private static readonly string[] SpriteSheetRepairButtonLabels =
    {
        SpriteSheetRepairPsdParamsLabel,
        "Generate Missing Geometry",
        "Fix Skin Weights",
        "Refresh Cache"
    };
    private static Dictionary<string, Sprite> leftToolIconSprites;

    private int spriteInfluenceBoneIndex = -1;

    private struct SpriteVisibilityRow
    {
        public PsdLayerPreviewRenderer.LayerInfo Layer;
        public PsdLayerPreviewRenderer.LayerHierarchyNode Node;
        public string Label;
        public int Level;
        public bool Hierarchical;
    }

    private void DrawLeftTools()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            DrawReanimatorVersionLabel();

            if (previewMode == PreviewMode.Comfimator)
            {
                DrawComfimatorLeftTools();
                DrawNamingIndicatorFooter();
                return;
            }

            if (previewMode == PreviewMode.Sprite)
            {
                DrawSpriteSheetLeftTools();
                DrawNamingIndicatorFooter();
                return;
            }

            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

            leftToolsScroll = GUILayout.BeginScrollView(
                leftToolsScroll,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.ExpandHeight(true));

            DrawModuleToolSections();

            GUILayout.EndScrollView();
            DrawNamingIndicatorFooter();
        }
    }

    private static void DrawReanimatorVersionLabel()
    {
        var previousColor = GUI.color;
        GUI.color = new Color(0.68f, 0.68f, 0.68f, 0.9f);
        EditorGUILayout.LabelField(ReanimatorVersion.DisplayName, EditorStyles.miniLabel);
        GUI.color = previousColor;
    }

    private void DrawModuleToolSections()
    {
        var modules = ReanimatorModuleRegistry.Modules;
        if (modules == null || modules.Count == 0)
        {
            EditorGUILayout.LabelField("No modules loaded", EditorStyles.miniLabel);
            return;
        }

        var index = 0;
        while (index < modules.Count)
        {
            var section = modules[index].Section ?? "Modules";
            var sectionStart = index;
            while (index < modules.Count && string.Equals(modules[index].Section ?? "Modules", section, StringComparison.Ordinal))
                index++;

            var expanded = GetModuleSectionExpanded(section);
            DrawToolSection(section, ref expanded, () => DrawModuleButtons(modules, sectionStart, index));
            SetModuleSectionExpanded(section, expanded);
        }
    }

    private void DrawModuleButtons(IReadOnlyList<IReanimatorModule> modules, int start, int end)
    {
        var index = start;
        while (index < end)
        {
            var module = modules[index];
            var rowGroup = module.RowGroup;
            if (rowGroup > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    while (index < end && modules[index].RowGroup == rowGroup)
                    {
                        DrawModuleButton(modules[index]);
                        index++;
                    }
                }
                continue;
            }

            DrawModuleButton(module);
            index++;
        }
    }

    private void DrawModuleButton(IReanimatorModule module)
    {
        if (module == null)
            return;
        if (!ShouldDrawModuleInProductionUi(module, Api))
            return;

        var label = module.Label(Api);
        var fixedWidth = string.Equals(label, "FIX", StringComparison.Ordinal) ? 22f : 0f;
        using (new EditorGUI.DisabledScope(!module.IsEnabled(Api)))
        {
            if (DrawLeftToolButton(label, module.IsActive(Api), fixedWidth, module))
            {
                module.Invoke(Api);
                Repaint();
            }
        }
    }

    private bool DrawActiveModuleSettings(float availableHeight)
    {
        var modules = ReanimatorModuleRegistry.Modules;
        if (modules == null)
            return false;

        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (module == null || !module.IsActive(Api))
                continue;

            if (module.DrawSettings(Api, availableHeight))
                return true;
        }

        return false;
    }

    private bool GetModuleSectionExpanded(string section)
    {
        switch (section)
        {
            case "Pose":
                return leftPoseExpanded;
            case "Bones":
                return leftBonesExpanded;
            case "Geometry":
                return leftGeometryExpanded;
            case "Weights":
                return leftWeightsExpanded;
            case "Utility":
                return leftUtilityExpanded;
            case "Trash":
                return leftTodoExpanded;
            default:
                return true;
        }
    }

    private void SetModuleSectionExpanded(string section, bool expanded)
    {
        switch (section)
        {
            case "Pose":
                leftPoseExpanded = expanded;
                break;
            case "Bones":
                leftBonesExpanded = expanded;
                break;
            case "Geometry":
                leftGeometryExpanded = expanded;
                break;
            case "Weights":
                leftWeightsExpanded = expanded;
                break;
            case "Utility":
                leftUtilityExpanded = expanded;
                break;
            case "Trash":
                leftTodoExpanded = expanded;
                break;
        }
    }

    private void SetPreviewMode(PreviewMode mode)
    {
        if (previewMode == mode)
            return;

        EnsurePreviewNavigators();
        StoreActivePreviewNavigator();

        if (IsSpritePreviewMode(mode))
        {
            CommitBoneEditRestPoseIfLeaving(ReanimatorToolMode.NoSettings);
            CancelActivePreviewTool();
        }

        previewMode = mode;
        previewNavigator = GetPreviewNavigator(previewMode);
        if (previewMode == PreviewMode.Comfimator)
            layerPreviewRenderer?.SelectLayer(null);
        Repaint();
    }

    private void ExitElbowModeIfActive()
    {
        if (bottomToolMode == ReanimatorToolMode.Elbow ||
            bottomToolMode == ReanimatorToolMode.Limbs ||
            bottomToolMode == ReanimatorToolMode.Joint ||
            bottomToolMode == ReanimatorToolMode.Boob ||
            bottomToolMode == ReanimatorToolMode.Curve ||
            bottomToolMode == ReanimatorToolMode.WeightBrush ||
            bottomToolMode == ReanimatorToolMode.CreateBone ||
            bottomToolMode == ReanimatorToolMode.CreateEdge ||
            bottomToolMode == ReanimatorToolMode.SplitEdge ||
            bottomToolMode == ReanimatorToolMode.EditGeometry ||
            bottomToolMode == ReanimatorToolMode.BoneInfluence ||
            bottomToolMode == ReanimatorToolMode.SpriteInfluence ||
            bottomToolMode == ReanimatorToolMode.VolumeCurve ||
            bottomToolMode == ReanimatorToolMode.RadiusCull ||
            bottomToolMode == ReanimatorToolMode.PreviewPose ||
            bottomToolMode == ReanimatorToolMode.Projector ||
            bottomToolMode == ReanimatorToolMode.VertexCull ||
            bottomToolMode == ReanimatorToolMode.Face)
            SetBottomToolMode(ReanimatorToolMode.NoSettings);
    }

    private void SetNoSettingsToolMode()
    {
        ExitElbowModeIfActive();
        SetBottomToolMode(ReanimatorToolMode.NoSettings);
    }

    private void EnsureDefaultToolMode()
    {
        var normalizedMode = ReanimatorToolModeAlgorithms.NormalizeDefaultToolMode(bottomToolMode);
        if (normalizedMode != bottomToolMode)
            SetBottomToolMode(normalizedMode);
    }

    private void CancelActivePreviewTool()
    {
        SetBottomToolMode(bottomToolMode);
        layerPreviewRenderer?.CancelElbowTool();
        layerPreviewRenderer?.CancelLimbsTool();
        layerPreviewRenderer?.CancelJointTool();
        layerPreviewRenderer?.CancelBoobTool();
        layerPreviewRenderer?.CancelCurveTool();
        layerPreviewRenderer?.CancelCreateEdgeTool();
        layerPreviewRenderer?.CancelEditGeometryTool();
        layerPreviewRenderer?.CancelRadiusCullTool();
        layerPreviewRenderer?.CancelWeightSliderTool();
        layerPreviewRenderer?.CancelWeightBrushTool();
        layerPreviewRenderer?.CancelVolumeCurveTool();
    }

    private void SetBottomToolMode(ReanimatorToolMode mode)
    {
        mode = ReanimatorToolModeAlgorithms.NormalizeToolMode(mode);
        CommitBoneEditRestPoseIfLeaving(mode);

        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.Elbow))
            layerPreviewRenderer?.CancelElbowTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.Limbs))
            layerPreviewRenderer?.CancelLimbsTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.Joint))
            layerPreviewRenderer?.CancelJointTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.Boob))
            layerPreviewRenderer?.CancelBoobTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.Curve))
            layerPreviewRenderer?.CancelCurveTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.CreateEdge))
            layerPreviewRenderer?.CancelCreateEdgeTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.EditGeometry))
            layerPreviewRenderer?.CancelEditGeometryTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.RadiusCull))
            layerPreviewRenderer?.CancelRadiusCullTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.WeightSlider))
            layerPreviewRenderer?.CancelWeightSliderTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.WeightBrush))
            layerPreviewRenderer?.CancelWeightBrushTool();
        if (ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(bottomToolMode, mode, ReanimatorToolCancelTarget.VolumeCurve))
            layerPreviewRenderer?.CancelVolumeCurveTool();

        RestorePreviewPoseIfEnteringBoneEdit(mode);

        bottomToolMode = mode;
    }

    private void RestorePreviewPoseIfEnteringBoneEdit(ReanimatorToolMode nextMode)
    {
        if (bottomToolMode == ReanimatorToolMode.EditBone ||
            ReanimatorToolModeAlgorithms.NormalizeToolMode(nextMode) != ReanimatorToolMode.EditBone)
            return;

        layerPreviewRenderer?.RestorePoseForAssetTool();
    }

    private void CommitBoneEditRestPoseIfLeaving(ReanimatorToolMode nextMode)
    {
        if (bottomToolMode != ReanimatorToolMode.EditBone ||
            ReanimatorToolModeAlgorithms.NormalizeToolMode(nextMode) == ReanimatorToolMode.EditBone)
            return;

        CommitBoneEditRestPoseIfActive();
    }

    private bool CommitBoneEditRestPoseIfActive()
    {
        if (bottomToolMode != ReanimatorToolMode.EditBone)
            return false;

        return layerPreviewRenderer != null && layerPreviewRenderer.CommitCurrentBonePoseAsRest();
    }

    private static bool IsEditGeometrySelectionToolMode(ReanimatorToolMode mode)
    {
        return ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(mode);
    }

    internal static int CalculateReanimatorReadinessPercent(IReadOnlyList<ReanimatorReadinessItem> items)
    {
        return CalculateReanimatorReadinessPercent(items, null);
    }

    internal static int CalculateReanimatorReadinessPercent(
        IReadOnlyList<ReanimatorReadinessItem> items,
        Func<string, bool> hasPassedEvidence)
    {
        if (items == null || items.Count == 0)
            return 0;

        var total = 0;
        var done = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var points = Mathf.Max(0, items[i].Points);
            total += points;
            if (IsReadinessItemDone(items[i], hasPassedEvidence))
                done += points;
        }

        return total > 0 ? Mathf.RoundToInt(done * 100f / total) : 0;
    }

    internal static string[] BuildReanimatorReadinessLines(IReadOnlyList<ReanimatorReadinessItem> items)
    {
        return BuildReanimatorReadinessLines(items, null);
    }

    internal static string[] BuildReanimatorReadinessLines(
        IReadOnlyList<ReanimatorReadinessItem> items,
        Func<string, bool> hasPassedEvidence)
    {
        var percent = CalculateReanimatorReadinessPercent(items, hasPassedEvidence);
        var lines = new List<string> { $"Current production readiness: {percent}/100" };
        if (items == null)
            return lines.ToArray();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var status = IsReadinessItemDone(item, hasPassedEvidence) ? "Done" : "Missing";
            lines.Add($"{status}: {item.Label}");
        }

        return lines.ToArray();
    }

    private static bool IsReadinessItemDone(ReanimatorReadinessItem item, Func<string, bool> hasPassedEvidence)
    {
        if (!item.HasEvidence)
            return false;

        if (hasPassedEvidence == null)
            return true;

        for (var i = 0; i < item.EvidenceCaseNames.Length; i++)
        {
            if (!hasPassedEvidence(item.EvidenceCaseNames[i]))
                return false;
        }

        return true;
    }

    private bool ShouldShowReleaseDiagnostics()
    {
        return ShouldShowReleaseDiagnostics(layerPreviewRenderer);
    }

    internal static bool ShouldShowReleaseDiagnostics(PsdLayerPreviewRenderer renderer)
    {
        return renderer != null &&
               (renderer.IsDebugModeEnabled || renderer.IsDebugModeDoublePressArmed);
    }

    internal static bool ShouldDrawModuleInProductionUi(IReanimatorModule module, Reanimator.SystemApi api)
    {
        if (module is ReanimatorDebugModeModule)
            return api != null && api.IsDebugDiagnosticsVisible;

        return true;
    }

    private bool DrawLeftToolButton(
        string label,
        bool active = false,
        float fixedWidth = 0f,
        IReanimatorModule module = null,
        bool mutedWarning = false)
    {
        var alert = ShouldHighlightMissingGeometryButton(label);
        var incomplete = IsLeftToolMarkedIncomplete(label, module);
        var prevColor = GUI.backgroundColor;
        if (alert && mutedWarning)
            GUI.backgroundColor = new Color(0.48f, 0.32f, 0.24f, 1f);
        else if (alert)
            GUI.backgroundColor = new Color(0.9f, 0.12f, 0.08f, 1f);
        else if (incomplete && mutedWarning)
            GUI.backgroundColor = new Color(0.34f, 0.29f, 0.25f, 1f);
        else if (incomplete)
            GUI.backgroundColor = active
                ? new Color(0.92f, 0.26f, 0.16f, 1f)
                : new Color(0.62f, 0.12f, 0.11f, 1f);
        else if (active)
            GUI.backgroundColor = new Color(0.95f, 0.64f, 0.18f, 1f);

        var style = LeftToolButtonStyle();
        var rect = fixedWidth > 0f
            ? GUILayoutUtility.GetRect(fixedWidth, fixedWidth, 20f, 20f, style)
            : GUILayoutUtility.GetRect(GUIContent.none, style, GUILayout.Height(20f), GUILayout.ExpandWidth(true));
        var clicked = GUI.Button(rect, GUIContent.none, style);
        GUI.backgroundColor = prevColor;

        DrawLeftToolButtonContent(rect, label, alert, incomplete && !mutedWarning, module);
        return clicked;
    }

    private static GUIStyle LeftToolButtonStyle()
    {
        var style = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter
        };
        style.padding.left = 6;
        style.padding.right = 6;
        style.fixedHeight = 20f;
        return style;
    }

    private void DrawLeftToolButtonContent(Rect rect, string label, bool alert = false, bool incomplete = false, IReanimatorModule module = null)
    {
        var sprite = GetLeftToolButtonIconSprite(label, module);
        var iconName = GetLeftToolButtonIconName(label);
        var icon = sprite == null && !string.IsNullOrEmpty(iconName) ? EditorGUIUtility.IconContent(iconName).image : null;
        const float iconSize = 16f;
        const float iconLeft = 6f;
        const float textLeft = 26f;

        if (sprite != null || icon != null)
        {
            var iconX = rect.width <= textLeft + 8f
                ? rect.x + Mathf.Round((rect.width - iconSize) * 0.5f)
                : rect.x + iconLeft;
            var iconRect = new Rect(
                iconX,
                rect.y + Mathf.Round((rect.height - iconSize) * 0.5f),
                iconSize,
                iconSize);
            if (sprite != null)
                DrawSpriteIcon(iconRect, sprite);
            else
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        }

        var textStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            normal = { textColor = GetLeftToolButtonTextColor(label, alert, incomplete) },
            active = { textColor = GUI.skin.button.active.textColor },
            focused = { textColor = GUI.skin.button.focused.textColor },
            hover = { textColor = GUI.skin.button.hover.textColor }
        };
        var textInset = sprite != null || icon != null ? textLeft : 4f;
        var textRect = new Rect(rect.x + textInset, rect.y, rect.width - textInset - 4f, rect.height);
        if (textRect.width >= 8f)
            GUI.Label(textRect, label, textStyle);
    }

    private static Sprite GetLeftToolButtonIconSprite(string label, IReanimatorModule module)
    {
        var spriteName = GetLeftToolButtonIconSpriteName(label, module);
        if (string.IsNullOrEmpty(spriteName))
            return null;

        var sprites = GetLeftToolIconSprites();
        return sprites.TryGetValue(spriteName, out var sprite) ? sprite : null;
    }

    internal static string GetLeftToolButtonIconSpriteName(string label, IReanimatorModule module)
    {
        var moduleId = module != null ? module.Id : string.Empty;
        if (!string.IsNullOrEmpty(moduleId))
        {
            switch (moduleId)
            {
                case "pose.preview":
                    return "Preview Pose";
                case "pose.restore":
                    return "Restore Pose";
                case "bones.create":
                    return "Add / Split";
                case "bones.edit":
                    return "Edit bones";
                case "geometry.auto":
                    return "Auto geometry";
                case "geometry.edit":
                    return "Edit geometry";
                case "geometry.limbs":
                    return "Limbs";
                case "geometry.joint":
                    return "Joint";
                case "geometry.boob":
                    return "Boob";
                case "geometry.volume-curve":
                    return "Volume";
                case "geometry.circle-clean":
                    return "CirCir";
                case "geometry.retriangulate":
                    return "ReTri";
                case "geometry.vertex-cull":
                    return "Cull";
                case "geometry.elbow":
                    return "Elbow";
                case "geometry.create-edge":
                    return "Create edge";
                case "geometry.split-edge":
                    return "Split edge";
                case "weights.auto":
                    return "auto weights";
                case "weights.brush":
                    return "brush";
                case "weights.curve":
                    return "curve";
                case "weights.slider":
                    return "slider";
                case "weights.projector":
                    return "projector";
                case "weights.projector-fix":
                    return "fix";
                case "face.main":
                    return "Preview Pose";
                case "influence.bone":
                    return "bone influence";
                case "influence.sprite":
                    return "sprite influence";
                case "utility.generate-missing-geometry":
                    return "Generate Missing Geometry";
                case "utility.fix-skin-weights":
                    return "Fix Skin Weights";
                case "utility.kill-selected-sprite-weights":
                    return "Kill Weights";
                case "utility.delete-bones":
                    return "Delete Bones";
                case "utility.copy-bones":
                    return "Copy Bones";
                case "utility.refresh-cache":
                    return "Refresh Cache";
            }
        }

        switch (NormalizeLeftToolButtonLabel(label))
        {
            case "Preview Pose":
                return "Preview Pose";
            case "Restore Pose":
                return "Restore Pose";
            case "Create Bone":
            case "Add/Split":
                return "Add / Split";
            case "Edit Bone":
                return "Edit bones";
            case "Auto Geometry":
                return "Auto geometry";
            case "Edit Geometry":
                return "Edit geometry";
            case "Limbs":
                return "Limbs";
            case "Joint":
                return "Joint";
            case "Boob":
                return "Boob";
            case "Volume":
                return "Volume";
            case "CircleClean":
            case "Radius Cull":
            case "CirCl":
                return "CirCir";
            case "ReTri":
                return "ReTri";
            case "Cull":
                return "Cull";
            case "Elbow":
                return "Elbow";
            case "Create":
            case "Create Edge":
                return "Create edge";
            case "Split":
            case "Split Edge":
                return "Split edge";
            case "FIX":
                return "fix";
            case "Auto Weights":
                return "auto weights";
            case "Weight Brush":
                return "brush";
            case "Curve":
                return "curve";
            case "Slider":
                return "slider";
            case "Projector":
                return "projector";
            case "Bone Influence":
                return "bone influence";
            case "Sprite Influence":
                return "sprite influence";
            case "Generate Missing Geometry":
                return "Generate Missing Geometry";
            case "Fix Skin Weights":
                return "Fix Skin Weights";
            case "Kill Weights":
                return "Kill Weights";
            case "CopyBon":
            case "Copy Bones":
                return "Copy Bones";
            case "Refresh Cache":
                return "Refresh Cache";
        }

        var key = NormalizeLeftToolButtonLabel(label);
        if (key.StartsWith("Delete Bones", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("DelBon", StringComparison.OrdinalIgnoreCase))
            return "Delete Bones";
        return string.Empty;
    }

    internal static bool HasLeftToolIconSprite(string spriteName)
    {
        return !string.IsNullOrEmpty(spriteName) && GetLeftToolIconSprites().ContainsKey(spriteName);
    }

    internal static bool IsLeftToolMarkedIncomplete(string label, IReanimatorModule module = null)
    {
        if (module != null)
        {
            switch (module.Id)
            {
                case "pose.restore":
                case "pose.preview":
                case "bones.create":
                case "bones.edit":
                case "geometry.auto":
                case "geometry.edit":
                case "geometry.limbs":
                case "geometry.joint":
                case "weights.slider":
                case "utility.generate-missing-geometry":
                case "utility.kill-selected-sprite-weights":
                case "utility.delete-bones":
                case "utility.copy-bones":
                case "utility.refresh-cache":
                    return false;
            }
        }

        switch (NormalizeLeftToolButtonLabel(label))
        {
            case "Preview Pose":
            case "Restore Pose":
            case "Geometry":
            case "Optimize":
            case "Default Geometry":
            case "Limbs":
            case "Joint":
            case "Generate Missing Geometry":
            case "Kill Weights":
            case "CopyBon":
            case "Copy Bones":
            case "Refresh Cache":
                return false;
        }

        var key = NormalizeLeftToolButtonLabel(label);
        if (key.StartsWith("Delete Bones", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("DelBon", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static IReadOnlyDictionary<string, Sprite> GetLeftToolIconSprites()
    {
        if (leftToolIconSprites != null)
            return leftToolIconSprites;

        leftToolIconSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        var assets = AssetDatabase.LoadAllAssetsAtPath(LeftToolIconSheetPath);
        if (assets == null)
            return leftToolIconSprites;

        for (var i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Sprite sprite && !string.IsNullOrWhiteSpace(sprite.name))
                leftToolIconSprites[sprite.name.Trim()] = sprite;
        }

        return leftToolIconSprites;
    }

    private static void DrawSpriteIcon(Rect rect, Sprite sprite)
    {
        if (sprite == null || sprite.texture == null)
            return;

        var texture = sprite.texture;
        var textureRect = sprite.textureRect;
        var texCoords = new Rect(
            textureRect.x / texture.width,
            textureRect.y / texture.height,
            textureRect.width / texture.width,
            textureRect.height / texture.height);
        GUI.DrawTextureWithTexCoords(rect, texture, texCoords, true);
    }

    private bool ShouldHighlightMissingGeometryButton(string label)
    {
        var key = NormalizeLeftToolButtonLabel(label);
        if (!string.Equals(key, "Generate Missing Geometry", StringComparison.Ordinal) &&
            !string.Equals(key, "Generate Geo", StringComparison.Ordinal))
            return false;

        return missingGeometryScanState.HasAlertForPath(NormalizeReanimatorAssetPath(GetSelectedAssetPath()));
    }

    private static Color GetLeftToolButtonTextColor(string label, bool alert = false, bool incomplete = false)
    {
        if (alert || incomplete)
            return Color.white;

        if (string.Equals(label, "10% Volume", StringComparison.Ordinal))
            return new Color(1f, 0.18f, 0.12f, 1f);

        return GUI.skin.button.normal.textColor;
    }

    private static string GetLeftToolButtonIconName(string label)
    {
        var key = NormalizeLeftToolButtonLabel(label);
        switch (key)
        {
            case "Preview Pose":
                return "Animation.Play";
            case "Restore Pose":
                return "TreeEditor.Refresh";
            case "Edit Bone":
                return "Avatar Icon";
            case "Create Bone":
                return "CreateAddNew";
            case "Split Bone":
                return "d_Toolbar Minus";
            case "Readiness":
                return "console.infoicon";
            case "Auto Geometry":
                return "Grid.BoxTool";
            case "Edit Geometry":
                return "PreMatQuad";
            case "Limbs":
                return "AvatarMask Icon";
            case "Joint":
                return string.Empty;
            case "Boob":
                return "Profiler.Physics";
            case "Skeletron":
                return "d_AvatarPivot";
            case "Volume":
                return "BoxCollider Icon";
            case "10% Volume":
                return string.Empty;
            case "Create":
            case "Create Edge":
                return "d_editconstraints_16";
            case "Split":
            case "Split Edge":
                return "d_Toolbar Minus";
            case "Curve":
                return string.Empty;
            case "Slider":
                return string.Empty;
            case "Auto Weights":
                return string.Empty;
            case "Weight Brush":
                return string.Empty;
            case "FIX":
                return string.Empty;
            case "Projector":
                return "Projector Icon";
            case "Bone Influence":
                return "d_AvatarSelector";
            case "Sprite Influence":
                return "Sprite Icon";
            case "CircleClean":
            case "Radius Cull":
                return "CircleCollider2D Icon";
            case "Cull":
                return "d_Toolbar Minus";
            case "ReTri":
                return "d_preAudioLoopOff";
            case "Generate Missing Geometry":
                return "d_preAudioLoopOff";
            case "Fix Skin Weights":
                return "AvatarMask Icon";
            case SpriteSheetRepairPsdParamsLabel:
                return "TreeEditor.Refresh";
            case "CopyBon":
            case "Copy Bones":
                return string.Empty;
            case "Refresh Cache":
                return "TreeEditor.Trash";
            case "Elbow":
                return "d_Animation.Record";
        }

        if (key.StartsWith("Delete Bones", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (key.StartsWith("DelBon", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (key.StartsWith("Debug", StringComparison.OrdinalIgnoreCase))
            return "d_DebuggerAttached";
        return "d_UnityEditor.InspectorWindow";
    }

    private static string NormalizeLeftToolButtonLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
            return string.Empty;

        label = StripLeftToolHotkeySuffix(label);

        if (string.Equals(label, "Auto", StringComparison.Ordinal))
            return "Auto Geometry";
        if (string.Equals(label, "Edit", StringComparison.Ordinal))
            return "Edit Geometry";
        if (string.Equals(label, "Proj", StringComparison.Ordinal))
            return "Projector";
        if (label.StartsWith("Projector", StringComparison.OrdinalIgnoreCase))
            return "Projector";

        return label;
    }

    private static string StripLeftToolHotkeySuffix(string label)
    {
        if (string.IsNullOrEmpty(label))
            return string.Empty;

        var trimmed = label.TrimEnd();
        if (trimmed.Length < 4 || trimmed[trimmed.Length - 1] != ')')
            return label;

        var openIndex = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
        if (openIndex < 0 || trimmed.Length - openIndex != 4)
            return label;

        var hotkey = trimmed[openIndex + 2];
        if (hotkey < 'A' || hotkey > 'Z')
            return label;

        return trimmed.Substring(0, openIndex);
    }

    private void DrawToolSection(string title, ref bool expanded, Action content)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            if (expanded)
            {
                EditorGUILayout.Space(2f);
                content?.Invoke();
            }
        }
    }
    private void DrawPivotSettings()
    {
        EditorGUI.BeginChangeCheck();
        var nextPreset = (PsdLayerPreviewRenderer.PivotPreset)EditorGUILayout.Popup(
            "Pivot",
            (int)pivotPreset,
            PivotPresetLabels);
        if (EditorGUI.EndChangeCheck())
        {
            pivotPreset = nextPreset;
            if (pivotPreset != PsdLayerPreviewRenderer.PivotPreset.Custom &&
                pivotPreset != PsdLayerPreviewRenderer.PivotPreset.RootBonePosition)
            {
                pivotPosition = PsdLayerPreviewRenderer.GetPresetNormalizedPosition(pivotPreset);
            }
            else if (pivotPreset == PsdLayerPreviewRenderer.PivotPreset.RootBonePosition &&
                     layerPreviewRenderer != null &&
                     layerPreviewRenderer.TryGetRootBoneNormalizedPosition(out var rootNormalized))
            {
                pivotPosition = rootNormalized;
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Position", GUILayout.Width(52f));
            GUILayout.Label("X", GUILayout.Width(12f));
            pivotPosition.x = EditorGUILayout.FloatField(pivotPosition.x, GUILayout.Width(58f));
            GUILayout.Space(4f);
            GUILayout.Label("Y", GUILayout.Width(12f));
            pivotPosition.y = EditorGUILayout.FloatField(pivotPosition.y, GUILayout.Width(58f));
            GUILayout.FlexibleSpace();
        }
    }

    private void ApplyPivotFromUi()
    {
        if (layerPreviewRenderer == null)
            return;

        if (!layerPreviewRenderer.ApplyPivotPreset(pivotPreset, pivotPosition))
            return;

        ShowNotification(new GUIContent("Pivot set"));
        Repaint();
    }

    private void DrawRightPanels(Rect panelRect)
    {
        const float splitterHeight = 6f;
        const float minPanelHeight = 120f;
        var availableHeight = Mathf.Max(0f, panelRect.height);
        var maxTopHeight = Mathf.Max(minPanelHeight, availableHeight - minPanelHeight - splitterHeight);
        var topHeight = Mathf.Clamp((availableHeight - splitterHeight) * rightPanelSplit, minPanelHeight, maxTopHeight);
        var bottomHeight = Mathf.Max(minPanelHeight, availableHeight - topHeight - splitterHeight);

        var visibilityRect = new Rect(panelRect.x, panelRect.y, panelRect.width, topHeight);
        var splitterRect = new Rect(panelRect.x, visibilityRect.yMax, panelRect.width, splitterHeight);
        var weightRect = new Rect(panelRect.x, splitterRect.yMax, panelRect.width, bottomHeight);

        HandleRightPanelSplitter(panelRect, splitterRect, splitterHeight, minPanelHeight);

        GUILayout.BeginArea(visibilityRect);
        DrawVisibilityPanel(visibilityRect.height);
        GUILayout.EndArea();

        EditorGUI.DrawRect(splitterRect, new Color(0.12f, 0.12f, 0.12f, 0.9f));
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

        GUILayout.BeginArea(weightRect);
        if (previewMode == PreviewMode.Comfimator)
        {
            DrawComfimatorRightPanel(weightRect.height);
        }
        else if (previewMode == PreviewMode.Sprite)
        {
            DrawSpriteSheetPanel(weightRect.height);
        }
        else if (ShouldDrawBoneDebugPanel())
        {
            DrawBoneDebugPanel(weightRect.height);
        }
        else
        {
            if (!DrawActiveModuleSettings(weightRect.height))
                DrawEmptyToolPanel(weightRect.height);
        }
        GUILayout.EndArea();
    }

    private bool ShouldDrawBoneDebugPanel()
    {
        return layerPreviewRenderer != null &&
               layerPreviewRenderer.IsDebugModeEnabled &&
               visibilityBone &&
               layerPreviewRenderer.SelectedBoneIndex >= 0;
    }

    private void HandleRightPanelSplitter(Rect panelRect, Rect splitterRect, float splitterHeight, float minPanelHeight)
    {
        var e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && e.button == 0 && splitterRect.Contains(e.mousePosition))
        {
            isRightPanelResizing = true;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            isRightPanelResizing = false;
            return;
        }

        if (e.type != EventType.MouseDrag || !isRightPanelResizing)
            return;

        var totalHeight = Mathf.Max(0f, panelRect.height);
        if (totalHeight <= splitterHeight + minPanelHeight * 2f)
            return;

        var minY = panelRect.y + minPanelHeight;
        var maxY = panelRect.yMax - minPanelHeight - splitterHeight;
        var clampedY = Mathf.Clamp(e.mousePosition.y, minY, maxY);
        var topHeight = clampedY - panelRect.y;
        rightPanelSplit = Mathf.Clamp01(topHeight / (totalHeight - splitterHeight));
        e.Use();
        Repaint();
    }

    private void DrawEmptyToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            var rect = GUILayoutUtility.GetRect(1f, contentHeight, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, Color.black);
            DrawEmptyPreviewLogo(rect);
        }
    }

    private void DrawSpriteSheetPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;

                EditorGUILayout.LabelField("Sprite", EditorStyles.boldLabel);
                if (layerPreviewRenderer == null || !layerPreviewRenderer.TryGetSelectedLayer(out var layer) || layer == null)
                {
                    EditorGUILayout.HelpBox("Select a sprite frame in Sprite Sheet.", MessageType.Info);
                    return;
                }

                EditorGUI.BeginChangeCheck();
                var spriteName = DrawSpriteTextRow("Name", layer.name ?? string.Empty);
                if (EditorGUI.EndChangeCheck())
                    layerPreviewRenderer.SetLayerSpriteName(layer, spriteName);

                var rect = layer.sourceRect;
                EditorGUI.BeginChangeCheck();
                var position = DrawSpriteTwoFloatRow("Position", "X", rect.x, "Y", rect.y);
                var size = DrawSpriteTwoFloatRow(string.Empty, "W", rect.width, "H", rect.height);
                rect.x = position.x;
                rect.y = position.y;
                rect.width = size.x;
                rect.height = size.y;
                if (EditorGUI.EndChangeCheck())
                    layerPreviewRenderer.SetLayerSpriteRect(layer, rect);

                var border = layer.border;
                EditorGUI.BeginChangeCheck();
                var borderLt = DrawSpriteTwoFloatRow("Border", "L", border.x, "T", border.w);
                var borderRb = DrawSpriteTwoFloatRow(string.Empty, "R", border.z, "B", border.y);
                border.x = borderLt.x;
                border.w = borderLt.y;
                border.z = borderRb.x;
                border.y = borderRb.y;
                if (EditorGUI.EndChangeCheck())
                    layerPreviewRenderer.SetLayerSpriteBorder(layer, border);

                var alignment = Mathf.Clamp(layer.alignment, 0, SpritePivotLabels.Length - 1);
                var pivot = layer.pivot;
                EditorGUI.BeginChangeCheck();
                alignment = DrawSpritePopupRow("Pivot", alignment, SpritePivotLabels);
                spritePivotUnitMode = DrawSpritePopupRow("Pivot Unit Mode", Mathf.Clamp(spritePivotUnitMode, 0, SpritePivotUnitModes.Length - 1), SpritePivotUnitModes);

                using (new EditorGUI.DisabledScope(alignment != 9))
                {
                    var customPivot = spritePivotUnitMode == 1
                        ? new Vector2(pivot.x * Mathf.Max(1f, rect.width), pivot.y * Mathf.Max(1f, rect.height))
                        : pivot;
                    customPivot = DrawSpriteTwoFloatRow("Custom Pivot", "X", customPivot.x, "Y", customPivot.y);
                    if (spritePivotUnitMode == 1)
                    {
                        pivot = new Vector2(
                            customPivot.x / Mathf.Max(1f, rect.width),
                            customPivot.y / Mathf.Max(1f, rect.height));
                    }
                    else
                    {
                        pivot = customPivot;
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    if (alignment != 9)
                        pivot = GetSpritePivotForAlignment(alignment);
                    layerPreviewRenderer.SetLayerSpritePivot(layer, alignment, pivot);
                }
            }
        }
    }

    private static Vector2 GetSpritePivotForAlignment(int alignment)
    {
        switch (alignment)
        {
            case 1: return new Vector2(0f, 1f);
            case 2: return new Vector2(0.5f, 1f);
            case 3: return new Vector2(1f, 1f);
            case 4: return new Vector2(0f, 0.5f);
            case 5: return new Vector2(1f, 0.5f);
            case 6: return new Vector2(0f, 0f);
            case 7: return new Vector2(0.5f, 0f);
            case 8: return new Vector2(1f, 0f);
            default: return new Vector2(0.5f, 0.5f);
        }
    }

    private static string DrawSpriteTextRow(string label, string value)
    {
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        SplitSpriteRow(rect, out var labelRect, out var fieldRect);
        EditorGUI.LabelField(labelRect, label);
        return EditorGUI.TextField(fieldRect, value);
    }

    private static int DrawSpritePopupRow(string label, int value, string[] labels)
    {
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        SplitSpriteRow(rect, out var labelRect, out var fieldRect);
        EditorGUI.LabelField(labelRect, label);
        return EditorGUI.Popup(fieldRect, value, labels);
    }

    private static Vector2 DrawSpriteTwoFloatRow(string label, string aLabel, float a, string bLabel, float b)
    {
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        var labelWidth = Mathf.Min(92f, rect.width * 0.34f);
        const float tagWidth = 14f;
        const float gap = 4f;
        var remaining = Mathf.Max(90f, rect.width - labelWidth - gap);
        var fieldWidth = Mathf.Max(42f, (remaining - tagWidth * 2f - gap * 3f) * 0.5f);

        var x = rect.x;
        EditorGUI.LabelField(new Rect(x, rect.y, labelWidth, rect.height), label);
        x += labelWidth + gap;
        EditorGUI.LabelField(new Rect(x, rect.y, tagWidth, rect.height), aLabel);
        x += tagWidth + gap;
        a = EditorGUI.FloatField(new Rect(x, rect.y, fieldWidth, rect.height), a);
        x += fieldWidth + gap;
        EditorGUI.LabelField(new Rect(x, rect.y, tagWidth, rect.height), bLabel);
        x += tagWidth + gap;
        var secondWidth = Mathf.Max(42f, rect.xMax - x);
        b = EditorGUI.FloatField(new Rect(x, rect.y, secondWidth, rect.height), b);

        return new Vector2(a, b);
    }

    private static void SplitSpriteRow(Rect rect, out Rect labelRect, out Rect fieldRect)
    {
        var labelWidth = Mathf.Min(92f, rect.width * 0.34f);
        const float gap = 4f;
        labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
        fieldRect = new Rect(labelRect.xMax + gap, rect.y, Mathf.Max(50f, rect.width - labelWidth - gap), rect.height);
    }

    private const string NamingIndicatorAssetPath = "Assets/Plugins/Reanimator/Graphics/indication.psd";
    private const float NamingIndicatorFooterHeight = 236f;
    private const float NamingIndicatorPadding = 6f;
    private const float NamingIndicatorDiagnosticButtonWidth = 34f;
    private const float NamingIndicatorDiagnosticButtonHeight = 22f;
    private const float NamingIndicatorDiagnosticGap = 4f;

    private static List<NamingIndicatorPart> namingIndicatorParts;
    private static Rect namingIndicatorCanvasRect;
    private static readonly Dictionary<int, NamingIndicatorHitMask> namingIndicatorHitMasks =
        new Dictionary<int, NamingIndicatorHitMask>();
    private string namingIndicatorSourcePath;
    private HashSet<string> namingIndicatorSourceSpriteNames;
    private int namingIndicatorDuplicateSelectionIndex;

    private sealed class NamingIndicatorPart
    {
        public string Name;
        public Sprite Sprite;
        public Rect DocumentRect;
        public float RotationDegrees;
        public Vector2 RotationPivotNormalized = new Vector2(0.5f, 0.5f);
    }

    private sealed class NamingIndicatorHitMask
    {
        public int Width;
        public int Height;
        public byte[] Alpha;
    }

    private sealed class NamingIndicatorIssueState
    {
        public PsdLayerPreviewRenderer.LayerInfo ArmOrderLayer;
        public PsdLayerPreviewRenderer.LayerInfo LegOrderLayer;
        public readonly List<PsdLayerPreviewRenderer.LayerInfo> DuplicateLayers = new List<PsdLayerPreviewRenderer.LayerInfo>();
    }

    private static string BuildNamingIndicatorAllInBlockReason(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers)
    {
        var issues = BuildNamingIndicatorIssueState(layers);
        var reasons = new List<string>(3);
        if (issues.ArmOrderLayer != null)
            reasons.Add("WA: left/right arm sprite names are swapped");
        if (issues.LegOrderLayer != null)
            reasons.Add("WL: left/right leg sprite names are swapped");
        if (issues.DuplicateLayers.Count > 0)
            reasons.Add("DD: duplicate sprite names");

        return reasons.Count > 0
            ? "red naming indicator: " + string.Join("; ", reasons)
            : string.Empty;
    }

    private bool TryValidateAllInPreflight(out string error)
    {
        error = BuildNamingIndicatorAllInBlockReason(
            layerPreviewRenderer != null ? layerPreviewRenderer.Layers : null);
        return string.IsNullOrEmpty(error);
    }

    private void DrawNamingIndicatorFooter()
    {
        var rect = GUILayoutUtility.GetRect(
            1f,
            NamingIndicatorFooterHeight,
            GUILayout.ExpandWidth(true),
            GUILayout.Height(NamingIndicatorFooterHeight));

        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdImporterAsset(path))
            return;

        var parts = GetNamingIndicatorParts();
        if (parts == null || parts.Count == 0 || namingIndicatorCanvasRect.width <= 0f || namingIndicatorCanvasRect.height <= 0f)
            return;

        var diagnosticColumnWidth = NamingIndicatorDiagnosticButtonWidth + NamingIndicatorDiagnosticGap;
        var available = new Rect(
            rect.x + NamingIndicatorPadding,
            rect.y + NamingIndicatorPadding,
            Mathf.Max(1f, rect.width - NamingIndicatorPadding * 2f - diagnosticColumnWidth),
            Mathf.Max(1f, rect.height - NamingIndicatorPadding * 2f));
        var scale = Mathf.Min(available.width / namingIndicatorCanvasRect.width, available.height / namingIndicatorCanvasRect.height);
        if (scale <= 0f)
            return;

        var drawWidth = namingIndicatorCanvasRect.width * scale;
        var drawHeight = namingIndicatorCanvasRect.height * scale;
        var origin = new Vector2(
            available.x + (available.width - drawWidth) * 0.5f,
            available.y + (available.height - drawHeight) * 0.5f);
        var loadedLayers = layerPreviewRenderer != null ? layerPreviewRenderer.Layers : null;
        var hierarchyNodes = layerPreviewRenderer != null ? layerPreviewRenderer.LayerHierarchyNodes : null;
        var sourceNames = BuildNamingIndicatorSourceNames(
            GetNamingIndicatorSourceSpriteNames(path),
            loadedLayers,
            hierarchyNodes);
        var issueState = BuildNamingIndicatorIssueState(loadedLayers);
        HandleNamingIndicatorInput(origin, scale, parts, sourceNames, issueState, out var partTooltip);
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                var part = parts[i];
                if (part == null || !ShouldShowNamingIndicatorPart(part.Name, sourceNames))
                    continue;

                var isProblem = IsNamingIndicatorPartProblem(part.Name, sourceNames, issueState);
                DrawNamingIndicatorPart(origin, scale, part, isProblem);
            }
        }

        // Buttons and the hover card must be drawn after the mannequin so that
        // the image cannot cover the diagnostics.
        DrawNamingIndicatorDiagnosticButtons(rect, origin, drawHeight, issueState, path, partTooltip);
    }

    private static NamingIndicatorIssueState BuildNamingIndicatorIssueState(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers)
    {
        var result = new NamingIndicatorIssueState();
        if (layers == null || layers.Count == 0)
            return result;

        PsdLayerPreviewRenderer.LayerInfo leftArm = null;
        PsdLayerPreviewRenderer.LayerInfo rightArm = null;
        PsdLayerPreviewRenderer.LayerInfo leftArm2 = null;
        PsdLayerPreviewRenderer.LayerInfo rightArm2 = null;
        PsdLayerPreviewRenderer.LayerInfo leftLeg = null;
        PsdLayerPreviewRenderer.LayerInfo rightLeg = null;
        var layersByName = new Dictionary<string, List<PsdLayerPreviewRenderer.LayerInfo>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer == null)
                continue;

            var normalizedName = NormalizeNamingIndicatorName(layer.name);
            if (string.IsNullOrEmpty(normalizedName))
                continue;

            if (!layersByName.TryGetValue(normalizedName, out var matchingLayers))
            {
                matchingLayers = new List<PsdLayerPreviewRenderer.LayerInfo>();
                layersByName.Add(normalizedName, matchingLayers);
            }
            matchingLayers.Add(layer);

            switch (normalizedName)
            {
                case "l_arm":
                    leftArm ??= layer;
                    break;
                case "r_arm":
                    rightArm ??= layer;
                    break;
                case "l_arm_2":
                    leftArm2 ??= layer;
                    break;
                case "r_arm_2":
                    rightArm2 ??= layer;
                    break;
                case "l_leg":
                    leftLeg ??= layer;
                    break;
                case "r_leg":
                    rightLeg ??= layer;
                    break;
            }
        }

        if (IsNamingIndicatorLeftPartRightOfRightPart(leftArm, rightArm))
            result.ArmOrderLayer = leftArm;
        else if (IsNamingIndicatorLeftPartRightOfRightPart(leftArm2, rightArm2))
            result.ArmOrderLayer = leftArm2;
        if (IsNamingIndicatorLeftPartRightOfRightPart(leftLeg, rightLeg))
            result.LegOrderLayer = leftLeg;

        foreach (var pair in layersByName)
        {
            var matchingLayers = pair.Value;
            if (matchingLayers == null || matchingLayers.Count < 2)
                continue;

            // The first sprite is the canonical occurrence; subsequent ones are the duplicates.
            for (var i = 1; i < matchingLayers.Count; i++)
                result.DuplicateLayers.Add(matchingLayers[i]);
        }

        return result;
    }

    private static bool IsNamingIndicatorLeftPartRightOfRightPart(
        PsdLayerPreviewRenderer.LayerInfo left,
        PsdLayerPreviewRenderer.LayerInfo right)
    {
        if (left == null || right == null)
            return false;

        var leftCenterX = left.position.x + left.sourceRect.width * 0.5f;
        var rightCenterX = right.position.x + right.sourceRect.width * 0.5f;
        return leftCenterX > rightCenterX;
    }

    private void DrawNamingIndicatorDiagnosticButtons(
        Rect footerRect,
        Vector2 indicatorOrigin,
        float indicatorHeight,
        NamingIndicatorIssueState issues,
        string path,
        string partTooltip)
    {
        if (issues == null)
            return;

        var x = footerRect.xMax - NamingIndicatorPadding - NamingIndicatorDiagnosticButtonWidth;
        var warningStartY = Mathf.Clamp(
            indicatorOrigin.y + indicatorHeight * 0.42f,
            footerRect.y + NamingIndicatorPadding,
            footerRect.yMax - NamingIndicatorPadding - NamingIndicatorDiagnosticButtonHeight * 4f - NamingIndicatorDiagnosticGap * 3f);

        var hoveredTooltip = partTooltip ?? string.Empty;
        if (issues.ArmOrderLayer != null)
        {
            var clicked = DrawNamingIndicatorDiagnosticButton(
                new Rect(x, warningStartY, NamingIndicatorDiagnosticButtonWidth, NamingIndicatorDiagnosticButtonHeight),
                "WA",
                new Color(0.86f, 0.12f, 0.1f, 1f),
                "WA — Wrong Arms\nНазвания/стороны спрайтов рук перепутаны относительно их положения. Нажми WA: будет выбран подозрительный l_arm. Переименуй или поменяй местами l_arm и r_arm, пока WA не исчезнет. Красный WA полностью блокирует ALLIN.",
                out var tooltip);
            if (!string.IsNullOrEmpty(tooltip))
                hoveredTooltip = tooltip;
            if (clicked)
                SelectNamingIndicatorIssueLayer(issues.ArmOrderLayer);
        }

        var legY = warningStartY + NamingIndicatorDiagnosticButtonHeight + NamingIndicatorDiagnosticGap;
        if (issues.LegOrderLayer != null)
        {
            var clicked = DrawNamingIndicatorDiagnosticButton(
                new Rect(x, legY, NamingIndicatorDiagnosticButtonWidth, NamingIndicatorDiagnosticButtonHeight),
                "WL",
                new Color(0.86f, 0.12f, 0.1f, 1f),
                "WL — Wrong Legs\nНазвания/стороны спрайтов ног перепутаны относительно их положения. Нажми WL: будет выбран подозрительный l_leg. Переименуй или поменяй местами l_leg и r_leg, пока WL не исчезнет. Красный WL полностью блокирует ALLIN.",
                out var tooltip);
            if (!string.IsNullOrEmpty(tooltip))
                hoveredTooltip = tooltip;
            if (clicked)
                SelectNamingIndicatorIssueLayer(issues.LegOrderLayer);
        }

        var duplicatesY = legY + NamingIndicatorDiagnosticButtonHeight + NamingIndicatorDiagnosticGap;
        if (issues.DuplicateLayers.Count > 0)
        {
            var clicked = DrawNamingIndicatorDiagnosticButton(
                new Rect(x, duplicatesY, NamingIndicatorDiagnosticButtonWidth, NamingIndicatorDiagnosticButtonHeight),
                "DD",
                new Color(0.86f, 0.12f, 0.1f, 1f),
                "DD — Duplicate Definitions\nНайдены несколько спрайтов с одинаковым именем. Нажимай DD повторно, чтобы по очереди выбирать дубликаты, и дай каждому уникальное имя. Красный DD полностью блокирует ALLIN.",
                out var tooltip);
            if (!string.IsNullOrEmpty(tooltip))
                hoveredTooltip = tooltip;
            if (clicked)
            {
                var index = namingIndicatorDuplicateSelectionIndex % issues.DuplicateLayers.Count;
                SelectNamingIndicatorIssueLayer(issues.DuplicateLayers[index]);
                namingIndicatorDuplicateSelectionIndex = (index + 1) % issues.DuplicateLayers.Count;
            }
        }

        var refreshRect = new Rect(
            x,
            footerRect.yMax - NamingIndicatorPadding - NamingIndicatorDiagnosticButtonHeight,
            NamingIndicatorDiagnosticButtonWidth,
            NamingIndicatorDiagnosticButtonHeight);
        var refreshClicked = DrawNamingIndicatorDiagnosticButton(
                refreshRect,
                "R",
                new Color(0.12f, 0.38f, 0.9f, 1f),
                "R — Refresh\nПересчитывает индикатор по текущим именам и положениям спрайтов, очищает старые сигнатуры предупреждений и обновляет preview cache. R синий, это не ошибка и он не блокирует ALLIN.",
                out var refreshTooltip);
        if (!string.IsNullOrEmpty(refreshTooltip))
            hoveredTooltip = refreshTooltip;
        if (refreshClicked)
            RefreshNamingIndicator(path);

        DrawNamingIndicatorHoverCard(footerRect, hoveredTooltip);
    }

    private static bool DrawNamingIndicatorDiagnosticButton(
        Rect rect,
        string label,
        Color color,
        string tooltip,
        out string hoveredTooltip)
    {
        var evt = Event.current;
        hoveredTooltip = evt != null && rect.Contains(evt.mousePosition) ? tooltip : string.Empty;
        var previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = color;
        var style = new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        style.hover.textColor = Color.white;
        style.active.textColor = Color.white;
        style.focused.textColor = Color.white;
        var clicked = GUI.Button(rect, new GUIContent(label, tooltip), style);
        GUI.backgroundColor = previousBackground;
        return clicked;
    }

    private static void DrawNamingIndicatorHoverCard(Rect footerRect, string tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip) || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        var width = Mathf.Max(
            120f,
            footerRect.width - NamingIndicatorDiagnosticButtonWidth - NamingIndicatorPadding * 3f - NamingIndicatorDiagnosticGap);
        var content = new GUIContent(tooltip);
        var style = new GUIStyle(EditorStyles.helpBox)
        {
            wordWrap = true,
            richText = false,
            alignment = TextAnchor.UpperLeft,
            fontSize = 11,
            padding = new RectOffset(8, 8, 7, 7)
        };
        var height = Mathf.Min(
            footerRect.height - NamingIndicatorPadding * 2f,
            Mathf.Max(72f, style.CalcHeight(content, width)));
        var cardRect = new Rect(
            footerRect.x + NamingIndicatorPadding,
            footerRect.y + NamingIndicatorPadding,
            width,
            height);

        GUI.Box(cardRect, content, style);
    }

    private void SelectNamingIndicatorIssueLayer(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (layer == null || layerPreviewRenderer == null)
            return;

        layerPreviewRenderer.SelectLayer(layer);
        visibilitySprite = true;
        visibilityBone = false;
        visibilityAutoScrolledLayerSelectionKey = string.Empty;
        Repaint();
    }

    private void RefreshNamingIndicator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        EditorApplication.delayCall += () =>
        {
            if (this == null)
                return;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            InvalidateNamingIndicatorCaches(true);
            namingIndicatorDuplicateSelectionIndex = 0;

            if (layerPreviewRenderer != null && !layerPreviewRenderer.HasPendingChanges)
            {
                layerPreviewRenderer.MarkDirty();
                layerPreviewRenderer.RequestPreviewCacheRefresh(path, false);
            }

            DeferPreviewContent(2);
            ShowNotification(new GUIContent("Indicator refreshed"));
            Repaint();
        };
    }

    private void InvalidateNamingIndicatorCaches(bool includeTemplate)
    {
        namingIndicatorSourcePath = string.Empty;
        namingIndicatorSourceSpriteNames = null;
        if (!includeTemplate)
            return;

        namingIndicatorParts = null;
        namingIndicatorCanvasRect = Rect.zero;
        namingIndicatorHitMasks.Clear();
    }

    private void HandleNamingIndicatorInput(
        Vector2 origin,
        float scale,
        IReadOnlyList<NamingIndicatorPart> parts,
        HashSet<string> sourceNames,
        NamingIndicatorIssueState issues,
        out string hoveredTooltip)
    {
        hoveredTooltip = string.Empty;
        var evt = Event.current;
        if (evt == null || parts == null)
            return;

        NamingIndicatorPart hoveredPart = null;
        var smallestHitArea = float.PositiveInfinity;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part == null || !ShouldShowNamingIndicatorPart(part.Name, sourceNames))
                continue;

            var partRect = BuildNamingIndicatorPartRect(origin, scale, part.DocumentRect);
            var hitPoint = InverseRotateNamingIndicatorPoint(
                evt.mousePosition,
                BuildNamingIndicatorRotationPivot(partRect, part),
                part.RotationDegrees);
            if (!IsNamingIndicatorPartHit(partRect, part.Sprite, hitPoint))
                continue;

            // The template contains broad base sprites (especially topwear)
            // underneath specific body parts. Prefer the smallest opaque part
            // so a leg/eye/arm wins over the full-body base at the same pixel.
            var hitArea = partRect.width * partRect.height;
            if (hitArea >= smallestHitArea)
                continue;

            hoveredPart = part;
            smallestHitArea = hitArea;
        }

        if (hoveredPart == null)
            return;

        hoveredTooltip = BuildNamingIndicatorPartHoverTooltip(hoveredPart.Name, sourceNames, issues);
        var layer = FindNamingIndicatorLayer(hoveredPart.Name);
        if (layer != null)
            EditorGUIUtility.AddCursorRect(
                new Rect(evt.mousePosition.x - 1f, evt.mousePosition.y - 1f, 2f, 2f),
                MouseCursor.Link);
        if (evt.type != EventType.MouseDown || evt.button != 0 || layer == null)
            return;

        SelectNamingIndicatorIssueLayer(layer);
        if (evt.clickCount >= 2)
            TryCenterPreviewOnLayer(layer);

        evt.Use();
        Repaint();
    }

    private static bool ShouldShowNamingIndicatorPart(string partName, HashSet<string> sourceNames)
    {
        var normalized = NormalizeNamingIndicatorName(partName);
        if (normalized != "l_arm_2" && normalized != "r_arm_2")
            return true;

        return sourceNames != null &&
               (sourceNames.Contains("l_arm_2") || sourceNames.Contains("r_arm_2"));
    }

    private static Vector2 BuildNamingIndicatorRotationPivot(Rect drawRect, NamingIndicatorPart part)
    {
        var normalized = part != null
            ? part.RotationPivotNormalized
            : new Vector2(0.5f, 0.5f);
        return new Vector2(
            drawRect.xMin + drawRect.width * normalized.x,
            drawRect.yMin + drawRect.height * normalized.y);
    }

    private static Vector2 InverseRotateNamingIndicatorPoint(Vector2 point, Vector2 pivot, float degrees)
    {
        if (Mathf.Abs(degrees) <= 0.001f)
            return point;

        var radians = -degrees * Mathf.Deg2Rad;
        var sine = Mathf.Sin(radians);
        var cosine = Mathf.Cos(radians);
        var delta = point - pivot;
        return pivot + new Vector2(
            delta.x * cosine - delta.y * sine,
            delta.x * sine + delta.y * cosine);
    }

    private static bool IsNamingIndicatorPartHit(Rect drawRect, Sprite sprite, Vector2 mousePosition)
    {
        if (!drawRect.Contains(mousePosition))
            return false;
        if (sprite == null || sprite.texture == null || drawRect.width <= 0f || drawRect.height <= 0f)
            return true;

        var mask = GetNamingIndicatorHitMask(sprite);
        if (mask == null || mask.Alpha == null || mask.Alpha.Length != mask.Width * mask.Height)
            return true;

        var normalizedX = Mathf.Clamp01((mousePosition.x - drawRect.xMin) / drawRect.width);
        var normalizedY = Mathf.Clamp01((mousePosition.y - drawRect.yMin) / drawRect.height);
        var pixelX = Mathf.Clamp(Mathf.FloorToInt(normalizedX * mask.Width), 0, mask.Width - 1);
        // GUI coordinates grow downward; texture pixels grow upward.
        var pixelY = Mathf.Clamp(Mathf.FloorToInt((1f - normalizedY) * mask.Height), 0, mask.Height - 1);
        return mask.Alpha[pixelY * mask.Width + pixelX] > 8;
    }

    private static NamingIndicatorHitMask GetNamingIndicatorHitMask(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null)
            return null;

        var key = sprite.GetInstanceID();
        if (namingIndicatorHitMasks.TryGetValue(key, out var cached))
            return cached;

        var textureRect = sprite.textureRect;
        var width = Mathf.Max(1, Mathf.RoundToInt(textureRect.width));
        var height = Mathf.Max(1, Mathf.RoundToInt(textureRect.height));
        RenderTexture temporary = null;
        Texture2D readable = null;
        var previousActive = RenderTexture.active;
        try
        {
            temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            Graphics.Blit(
                sprite.texture,
                temporary,
                new Vector2(textureRect.width / sprite.texture.width, textureRect.height / sprite.texture.height),
                new Vector2(textureRect.x / sprite.texture.width, textureRect.y / sprite.texture.height));

            RenderTexture.active = temporary;
            readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            readable.Apply(false, false);
            var pixels = readable.GetPixels32();
            var alpha = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
                alpha[i] = pixels[i].a;

            cached = new NamingIndicatorHitMask
            {
                Width = width,
                Height = height,
                Alpha = alpha
            };
            namingIndicatorHitMasks[key] = cached;
            return cached;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Naming indicator alpha hit mask failed for " + sprite.name + ": " + exception.Message);
            namingIndicatorHitMasks[key] = null;
            return null;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (readable != null)
                UnityEngine.Object.DestroyImmediate(readable);
            if (temporary != null)
                RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static string BuildNamingIndicatorPartHoverTooltip(
        string partName,
        HashSet<string> sourceNames,
        NamingIndicatorIssueState issues)
    {
        var normalized = NormalizeNamingIndicatorName(partName);
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        if (!ShouldDrawNamingIndicatorPart(partName, sourceNames))
        {
            if (normalized == "body" || normalized == "topwear")
            {
                return "body / topwear — НЕ НАЙДЕН\n" +
                       "Для этой области допустимо любое из имён: \"body\" или \"topwear\". " +
                       "Создай соответствующий слой или переименуй его одним из этих имён.";
            }

            return normalized + " — НЕ НАЙДЕН\n" +
                   "В открытой иерархии нет слоя, подходящего для части \"" + normalized + "\". " +
                   "Создай его или переименуй соответствующий слой в \"" + normalized + "\".";
        }

        var reasons = new List<string>();
        if (issues != null)
        {
            if (issues.ArmOrderLayer != null && IsNamingIndicatorSameArmPair(normalized, issues.ArmOrderLayer.name))
            {
                var pairKey = GetNamingIndicatorArmPairKey(normalized);
                var leftName = "l_" + pairKey;
                var rightName = "r_" + pairKey;
                reasons.Add(
                    "WA: стороны рук перепутаны — " + leftName + " находится правее " + rightName + ". " +
                    "Поменяй имена " + leftName + " и " + rightName + " местами.");
            }

            if (issues.LegOrderLayer != null && (normalized == "l_leg" || normalized == "r_leg"))
            {
                reasons.Add(
                    "WL: стороны ног перепутаны — l_leg находится правее r_leg. " +
                    "Поменяй имена l_leg и r_leg местами.");
            }

            for (var i = 0; i < issues.DuplicateLayers.Count; i++)
            {
                var duplicate = issues.DuplicateLayers[i];
                if (duplicate == null || !string.Equals(
                        normalized,
                        NormalizeNamingIndicatorName(duplicate.name),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                reasons.Add(
                    "DD: найдено несколько слоёв с именем \"" + normalized + "\". " +
                    "Оставь это имя только у одного слоя.");
                break;
            }
        }

        if (reasons.Count > 0)
            return normalized + " — ПРОБЛЕМА\n" + string.Join("\n", reasons);

        if (normalized == "body" || normalized == "topwear")
        {
            var matchedName = sourceNames != null && sourceNames.Contains(normalized)
                ? normalized
                : normalized == "body" ? "topwear" : "body";
            return matchedName + " — OK\nНайден допустимый слой \"" + matchedName + "\". " +
                   "Для этой области работают оба имени: body и topwear.";
        }

        return normalized + " — OK\nСлой найден, ошибок имени или стороны для этой части нет.";
    }

    private PsdLayerPreviewRenderer.LayerInfo FindNamingIndicatorLayer(string partName)
    {
        if (layerPreviewRenderer == null || layerPreviewRenderer.Layers == null)
            return null;

        PsdLayerPreviewRenderer.LayerInfo fallback = null;
        for (var i = 0; i < layerPreviewRenderer.Layers.Count; i++)
        {
            var layer = layerPreviewRenderer.Layers[i];
            if (layer == null)
                continue;

            var score = GetNamingIndicatorLayerMatchScore(partName, layer.name);
            if (score <= 0)
                continue;
            if (score >= 100)
                return layer;
            if (fallback == null)
                fallback = layer;
        }

        return fallback;
    }

    private static int GetNamingIndicatorLayerMatchScore(string partName, string layerName)
    {
        var part = NormalizeNamingIndicatorName(partName);
        var layer = NormalizeNamingIndicatorName(layerName);
        if (string.IsNullOrEmpty(part) || string.IsNullOrEmpty(layer))
            return 0;
        if (string.Equals(part, layer, StringComparison.OrdinalIgnoreCase))
            return 100;

        switch (part)
        {
            case "body":
            case "topwear":
                return layer == "body" || layer == "topwear" ? 75 : 0;
            case "l_eye":
            case "r_eye":
                return layer == "eyes" || layer == "irides" ? 50 : 0;
            case "eyewhites":
                return layer.Contains("eyewhite") ? 50 : 0;
            default:
                return 0;
        }
    }

    private static bool IsNamingIndicatorPartProblem(
        string partName,
        HashSet<string> sourceNames,
        NamingIndicatorIssueState issues)
    {
        if (!ShouldDrawNamingIndicatorPart(partName, sourceNames))
            return true;

        var normalized = NormalizeNamingIndicatorName(partName);
        if (string.IsNullOrEmpty(normalized) || issues == null)
            return false;
        if (issues.ArmOrderLayer != null && IsNamingIndicatorSameArmPair(normalized, issues.ArmOrderLayer.name))
            return true;
        if (issues.LegOrderLayer != null && (normalized == "l_leg" || normalized == "r_leg"))
            return true;

        for (var i = 0; i < issues.DuplicateLayers.Count; i++)
        {
            var duplicate = issues.DuplicateLayers[i];
            if (duplicate != null && string.Equals(
                    normalized,
                    NormalizeNamingIndicatorName(duplicate.name),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNamingIndicatorSameArmPair(string firstName, string secondName)
    {
        var firstKey = GetNamingIndicatorArmPairKey(NormalizeNamingIndicatorName(firstName));
        var secondKey = GetNamingIndicatorArmPairKey(NormalizeNamingIndicatorName(secondName));
        return !string.IsNullOrEmpty(firstKey) && string.Equals(firstKey, secondKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNamingIndicatorArmPairKey(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName) ||
            (!normalizedName.StartsWith("l_arm", StringComparison.OrdinalIgnoreCase) &&
             !normalizedName.StartsWith("r_arm", StringComparison.OrdinalIgnoreCase)))
        {
            return string.Empty;
        }

        return normalizedName.Length > 2 ? normalizedName.Substring(2) : string.Empty;
    }

    private static void DrawNamingIndicatorPart(
        Vector2 origin,
        float scale,
        NamingIndicatorPart part,
        bool isProblem)
    {
        var prevColor = GUI.color;
        var previousMatrix = GUI.matrix;
        GUI.color = isProblem
            ? new Color(1f, 0.12f, 0.1f, 1f)
            : Color.white;
        var drawRect = BuildNamingIndicatorPartRect(origin, scale, part.DocumentRect);
        if (Mathf.Abs(part.RotationDegrees) > 0.001f)
            GUIUtility.RotateAroundPivot(part.RotationDegrees, BuildNamingIndicatorRotationPivot(drawRect, part));
        DrawSpriteIcon(drawRect, part.Sprite);
        GUI.matrix = previousMatrix;
        GUI.color = prevColor;
    }

    private static Rect BuildNamingIndicatorPartRect(Vector2 origin, float scale, Rect documentRect)
    {
        return new Rect(
            origin.x + (documentRect.xMin - namingIndicatorCanvasRect.xMin) * scale,
            origin.y + (namingIndicatorCanvasRect.yMax - documentRect.yMax) * scale,
            documentRect.width * scale,
            documentRect.height * scale);
    }

    private HashSet<string> GetNamingIndicatorSourceSpriteNames(string path)
    {
        path = NormalizeReanimatorAssetPath(path);
        if (namingIndicatorSourceSpriteNames != null &&
            string.Equals(namingIndicatorSourcePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return namingIndicatorSourceSpriteNames;
        }

        namingIndicatorSourcePath = path;
        namingIndicatorSourceSpriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets == null)
            return namingIndicatorSourceSpriteNames;

        for (var i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Sprite sprite && !string.IsNullOrWhiteSpace(sprite.name))
                namingIndicatorSourceSpriteNames.Add(NormalizeNamingIndicatorName(sprite.name));
        }

        return namingIndicatorSourceSpriteNames;
    }

    private static HashSet<string> BuildNamingIndicatorSourceNames(
        IEnumerable<string> importedSpriteNames,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> loadedLayers,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> hierarchyNodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The preview renderer contains the layers the user is actually working with.
        // PSD importer subassets are only a fallback: Unity can omit valid PSD layers
        // (for example body/neck/legs) from LoadAllAssetsAtPath even though Reanimator
        // has loaded and displays them.
        if (loadedLayers != null)
        {
            for (var i = 0; i < loadedLayers.Count; i++)
            {
                var layer = loadedLayers[i];
                if (layer == null || string.IsNullOrWhiteSpace(layer.name))
                    continue;

                var normalized = NormalizeNamingIndicatorName(layer.name);
                if (!string.IsNullOrEmpty(normalized))
                    result.Add(normalized);
            }
        }

        // The right-hand Hierarchy panel is rendered from this collection, not
        // directly from Layers. Anything shown there must be treated as present
        // by the mannequin as well, including group-backed body parts.
        if (hierarchyNodes != null)
        {
            for (var i = 0; i < hierarchyNodes.Count; i++)
            {
                var node = hierarchyNodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.name))
                    continue;

                var normalized = NormalizeNamingIndicatorName(node.name);
                if (!string.IsNullOrEmpty(normalized))
                    result.Add(normalized);
            }
        }

        if (result.Count == 0 && importedSpriteNames != null)
        {
            foreach (var name in importedSpriteNames)
            {
                var normalized = NormalizeNamingIndicatorName(name);
                if (!string.IsNullOrEmpty(normalized))
                    result.Add(normalized);
            }
        }

        return result;
    }

    private static bool ShouldDrawNamingIndicatorPart(string partName, HashSet<string> sourceNames)
    {
        if (sourceNames == null || sourceNames.Count == 0)
            return false;

        var normalized = NormalizeNamingIndicatorName(partName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        switch (normalized)
        {
            case "body":
            case "topwear":
                return sourceNames.Contains("body") || sourceNames.Contains("topwear");
            case "l_eye":
            case "r_eye":
                return sourceNames.Contains(normalized) || sourceNames.Contains("eyes") || sourceNames.Contains("irides");
            case "eyewhites":
                return sourceNames.Any(name => name.Contains("eyewhite"));
            default:
                return sourceNames.Contains(normalized);
        }
    }

    private static List<NamingIndicatorPart> GetNamingIndicatorParts()
    {
        if (namingIndicatorParts != null)
            return namingIndicatorParts;

        namingIndicatorParts = new List<NamingIndicatorPart>();
        namingIndicatorCanvasRect = Rect.zero;

        var sprites = AssetDatabase.LoadAllAssetsAtPath(NamingIndicatorAssetPath)
            .OfType<Sprite>()
            .Where(sprite => sprite != null && !string.IsNullOrWhiteSpace(sprite.name))
            .ToDictionary(sprite => NormalizeNamingIndicatorName(sprite.name), StringComparer.OrdinalIgnoreCase);
        if (sprites.Count == 0)
            return namingIndicatorParts;

        var importer = AssetImporter.GetAtPath(NamingIndicatorAssetPath);
        if (importer == null)
            return namingIndicatorParts;

        var serialized = new SerializedObject(importer);
        var importData = FindNamingIndicatorSpriteImportData(serialized);
        if (importData == null || !importData.isArray)
            return namingIndicatorParts;

        var boundsInitialized = false;
        for (var i = 0; i < importData.arraySize; i++)
        {
            var element = importData.GetArrayElementAtIndex(i);
            if (element == null)
                continue;

            var name = ReadStringProperty(element.FindPropertyRelative("name") ?? element.FindPropertyRelative("m_Name"));
            var normalized = NormalizeNamingIndicatorName(name);
            if (string.IsNullOrEmpty(normalized) || !sprites.TryGetValue(normalized, out var sprite))
                continue;

            var spritePosition = ReadVector2Property(element.FindPropertyRelative("spritePosition") ?? element.FindPropertyRelative("m_SpritePosition"));
            var spriteRect = ReadRectProperty(element.FindPropertyRelative("rect") ?? element.FindPropertyRelative("m_Rect"));
            if (spriteRect.width <= 0f || spriteRect.height <= 0f)
                continue;

            var documentRect = new Rect(spritePosition.x, spritePosition.y, spriteRect.width, spriteRect.height);
            namingIndicatorParts.Add(new NamingIndicatorPart
            {
                Name = name,
                Sprite = sprite,
                DocumentRect = documentRect
            });

            namingIndicatorCanvasRect = boundsInitialized ? RectUnion(namingIndicatorCanvasRect, documentRect) : documentRect;
            boundsInitialized = true;
        }

        AddRotatedNamingIndicatorArm(
            namingIndicatorParts,
            "l_arm",
            "l_arm_2",
            45f,
            new Vector2(0.86f, 0.08f));
        AddRotatedNamingIndicatorArm(
            namingIndicatorParts,
            "r_arm",
            "r_arm_2",
            -45f,
            new Vector2(0.14f, 0.08f));

        return namingIndicatorParts;
    }

    private static void AddRotatedNamingIndicatorArm(
        List<NamingIndicatorPart> parts,
        string sourceName,
        string targetName,
        float rotationDegrees,
        Vector2 shoulderPivotNormalized)
    {
        if (parts == null || parts.Any(part => part != null && string.Equals(
                NormalizeNamingIndicatorName(part.Name),
                NormalizeNamingIndicatorName(targetName),
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var source = parts.FirstOrDefault(part => part != null && string.Equals(
            NormalizeNamingIndicatorName(part.Name),
            NormalizeNamingIndicatorName(sourceName),
            StringComparison.OrdinalIgnoreCase));
        if (source == null)
            return;

        parts.Add(new NamingIndicatorPart
        {
            Name = targetName,
            Sprite = source.Sprite,
            DocumentRect = source.DocumentRect,
            RotationDegrees = rotationDegrees,
            RotationPivotNormalized = shoulderPivotNormalized
        });
    }

    private static SerializedProperty FindNamingIndicatorSpriteImportData(SerializedObject serialized)
    {
        if (serialized == null)
            return null;

        var propertyNames = new[]
        {
            "layeredSpriteImportData",
            "m_LayeredSpriteImportData",
            "rigSpriteImportData",
            "m_RigSpriteImportData",
            "spriteImportData",
            "m_SpriteImportData"
        };

        for (var i = 0; i < propertyNames.Length; i++)
        {
            var property = serialized.FindProperty(propertyNames[i]);
            if (property != null && property.isArray && property.arraySize > 0)
                return property;
        }

        return null;
    }

    private static string NormalizeNamingIndicatorName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var source = value.Trim().ToLowerInvariant();
        var chars = new char[source.Length];
        var write = 0;
        var previousSeparator = false;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            var separator = char.IsWhiteSpace(c) || c == '-' || c == '_';
            if (separator)
            {
                if (write > 0 && !previousSeparator)
                    chars[write++] = '_';
                previousSeparator = true;
                continue;
            }

            chars[write++] = c;
            previousSeparator = false;
        }

        if (write > 0 && chars[write - 1] == '_')
            write--;

        return write > 0 ? new string(chars, 0, write) : string.Empty;
    }

    private static string ReadStringProperty(SerializedProperty property)
    {
        return property != null && property.propertyType == SerializedPropertyType.String
            ? property.stringValue
            : string.Empty;
    }

    private static Vector2 ReadVector2Property(SerializedProperty property)
    {
        if (property == null)
            return Vector2.zero;

        if (property.propertyType == SerializedPropertyType.Vector2)
            return property.vector2Value;

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        return new Vector2(
            x != null ? x.floatValue : 0f,
            y != null ? y.floatValue : 0f);
    }

    private static Rect ReadRectProperty(SerializedProperty property)
    {
        if (property == null)
            return Rect.zero;

        if (property.propertyType == SerializedPropertyType.Rect)
            return property.rectValue;

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        var width = property.FindPropertyRelative("width");
        var height = property.FindPropertyRelative("height");
        return new Rect(
            x != null ? x.floatValue : 0f,
            y != null ? y.floatValue : 0f,
            width != null ? width.floatValue : 0f,
            height != null ? height.floatValue : 0f);
    }

    private static Rect RectUnion(Rect a, Rect b)
    {
        var xMin = Mathf.Min(a.xMin, b.xMin);
        var yMin = Mathf.Min(a.yMin, b.yMin);
        var xMax = Mathf.Max(a.xMax, b.xMax);
        var yMax = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }
}
#endif













