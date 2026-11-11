#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed partial class ReanimatorCurveModule : ReanimatorToolModeModule
{
    public ReanimatorCurveModule()
        : base("weights.curve", "Weights", 40, 30, "Curve (X)", ReanimatorToolMode.Curve, 20, (api, height) => api?.DrawCurveSettings(height))
    {
    }
}

internal sealed partial class ReanimatorSliderModule : ReanimatorToolModeModule
{
    public ReanimatorSliderModule()
        : base("weights.slider", "Weights", 40, 40, "Slider (C)", ReanimatorToolMode.WeightSlider, 20, (api, height) => api?.DrawWeightSliderSettings(height))
    {
    }
}

internal sealed partial class ReanimatorProjectorModule : ReanimatorModuleBase, IReanimatorPreviewModule
{
    private static readonly string[] ProjectorModeLabels = { "Weights", "Stretch" };
    private static readonly string[] StretchModeLabels = { "APL", "IN", "COMBO" };

    private readonly ReanimatorProjectorSettings settings = new ReanimatorProjectorSettings();
    private readonly ReanimatorProjectorCandidateState candidateState = new ReanimatorProjectorCandidateState();
    private Vector2 scroll;
    private ReanimatorPreviewContext lastPreviewContext;

    public override string Id => "weights.projector";
    public override string Section => "Weights";
    public override int SectionOrder => 40;
    public override int ButtonOrder => 50;
    public override int RowGroup => 30;
    public override string Label(Reanimator.SystemApi api) => "Proj (R)";
    public override bool IsActive(Reanimator.SystemApi api) => api != null && api.ActiveToolMode == ReanimatorToolMode.Projector;
    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => settings.Mode == ReanimatorProjectorMode.Stretch && candidateState.BuildSelectedKeys().Length > 0;

    public override void Invoke(Reanimator.SystemApi api)
    {
        candidateState.Reset();
        scroll = Vector2.zero;
        api?.SetToolMode(ReanimatorToolMode.Projector);
    }

    public override bool DrawSettings(Reanimator.SystemApi api, float availableHeight)
    {
        var contentHeight = Mathf.Max(1f, availableHeight - 8f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(contentHeight)))
        {
            EditorGUILayout.LabelField("Projector", EditorStyles.boldLabel);
            var nextMode = (ReanimatorProjectorMode)GUILayout.Toolbar((int)settings.Mode, ProjectorModeLabels, GUILayout.Height(22f));
            if (nextMode != settings.Mode)
            {
                settings.Mode = nextMode;
                if (settings.Mode == ReanimatorProjectorMode.Stretch)
                    candidateState.SetAllEnabled(true);
                api?.Repaint();
            }
            if (settings.Mode == ReanimatorProjectorMode.Stretch)
            {
                var nextStretchMode = (ReanimatorProjectorStretchMode)GUILayout.Toolbar((int)settings.StretchMode, StretchModeLabels, GUILayout.Height(20f));
                if (nextStretchMode != settings.StretchMode)
                {
                    settings.StretchMode = nextStretchMode;
                    api?.Repaint();
                }

                EditorGUI.BeginChangeCheck();
                using (new EditorGUI.DisabledScope(settings.UseAplStretch))
                    settings.StretchKeepContour = EditorGUILayout.ToggleLeft("Keep Contour", settings.StretchKeepContour);
                if (EditorGUI.EndChangeCheck())
                    api?.Repaint();

                EditorGUI.BeginChangeCheck();
                settings.StretchUseCull = EditorGUILayout.ToggleLeft("Cull", settings.StretchUseCull);
                if (settings.StretchUseCull)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Distance", GUILayout.Width(58f));
                        settings.StretchCullDistance = Mathf.Max(0.01f, GUILayout.HorizontalSlider(settings.StretchCullDistance, 0.01f, 128f));
                        settings.StretchCullDistance = EditorGUILayout.FloatField(settings.StretchCullDistance, GUILayout.Width(58f));
                        settings.StretchCullDistance = Mathf.Clamp(settings.StretchCullDistance, 0.01f, 128f);
                    }
                }
                if (EditorGUI.EndChangeCheck())
                    api?.Repaint();
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.ClearTargetWeights = EditorGUILayout.ToggleLeft("Clear Target Weights", settings.ClearTargetWeights, GUILayout.Width(154f));
                    settings.FilterWeightTargetsToPreview = EditorGUILayout.ToggleLeft("In Preview", settings.FilterWeightTargetsToPreview);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.PostProcessExpandedCoverage = EditorGUILayout.ToggleLeft(
                        "Post Process",
                        settings.PostProcessExpandedCoverage,
                        GUILayout.Width(112f));
                    using (new EditorGUI.DisabledScope(!settings.PostProcessExpandedCoverage))
                    {
                        settings.SmoothPostProcessWeights = EditorGUILayout.ToggleLeft(
                            "Smooth",
                            settings.SmoothPostProcessWeights);
                    }
                }
                if (EditorGUI.EndChangeCheck())
                    api?.Repaint();
            }

            var targetName = string.Empty;
            var targetKey = string.Empty;
            var error = api == null ? "Preview unavailable" : null;
            var selectedTargetName = string.Empty;
            var selectedTargetKey = string.Empty;
            var selectedTargetError = string.Empty;
            var hasSelectedTarget = api != null &&
                                    api.TryGetProjectorSelectedTarget(out selectedTargetName, out selectedTargetKey, out selectedTargetError);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasSelectedTarget))
                {
                    if (GUILayout.Button("Set Target", GUILayout.Height(22f)))
                    {
                        settings.TargetSelectionKey = selectedTargetKey;
                        candidateState.Reset();
                        scroll = Vector2.zero;
                        api?.Repaint();
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(settings.TargetSelectionKey)))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(52f), GUILayout.Height(22f)))
                    {
                        settings.TargetSelectionKey = string.Empty;
                        candidateState.Reset();
                        scroll = Vector2.zero;
                        api?.Repaint();
                    }
                }
            }

            var candidates = Array.Empty<PsdLayerPreviewRenderer.ProjectorCandidateInfo>();
            if (api == null)
            {
                error = "Preview unavailable";
            }
            else if (string.IsNullOrWhiteSpace(settings.TargetSelectionKey))
            {
                targetName = hasSelectedTarget ? selectedTargetName : string.Empty;
                targetKey = string.Empty;
                error = string.IsNullOrWhiteSpace(selectedTargetError)
                    ? "Set target sprite first."
                    : selectedTargetError;
            }
            else
            {
                candidates = api.GetProjectorCandidates(settings.TargetSelectionKey, out targetName, out targetKey, out error);
            }

            if (settings.Mode == ReanimatorProjectorMode.Weights && settings.FilterWeightTargetsToPreview)
                candidates = ReanimatorProjectorUiAlgorithms.FilterCandidatesToPreview(candidates, lastPreviewContext);

            if (candidateState.ResetForTargetIfChanged(targetKey))
            {
                scroll = Vector2.zero;
                api?.Repaint();
            }

            if (!string.IsNullOrWhiteSpace(targetName))
                EditorGUILayout.LabelField("Target", targetName);
            if (string.IsNullOrWhiteSpace(settings.TargetSelectionKey) && hasSelectedTarget)
                EditorGUILayout.LabelField("Selected", selectedTargetName, EditorStyles.miniLabel);

            var selectedCount = 0;
            var nextHoveredCandidateKey = string.Empty;
            // Keep the list viewport separate from the two rows of action buttons.
            var headerHeight = settings.Mode == ReanimatorProjectorMode.Stretch
                ? (settings.StretchUseCull ? 178f : 154f)
                : 128f;
            if (string.IsNullOrWhiteSpace(settings.TargetSelectionKey) && hasSelectedTarget)
                headerHeight += EditorGUIUtility.singleLineHeight;
            const float footerHeight = 72f;
            var scrollHeight = Mathf.Max(24f, contentHeight - headerHeight - footerHeight);
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Height(scrollHeight)))
            {
                scroll = scrollScope.scrollPosition;
                candidateState.BeginCandidateFrame();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    EditorGUILayout.LabelField(error, EditorStyles.wordWrappedMiniLabel);
                }
                else if (candidates == null || candidates.Length == 0)
                {
                    EditorGUILayout.LabelField("No intersecting geometry.", EditorStyles.miniLabel);
                }
                else
                {
                    for (var i = 0; i < candidates.Length; i++)
                    {
                        var candidate = candidates[i];
                        if (!candidateState.TrackCandidate(candidate.SelectionKey, settings.Mode == ReanimatorProjectorMode.Stretch))
                            continue;

                        var label = $"{candidate.LayerName} ({candidate.VertexCount})";
                        candidateState.SetEnabled(candidate.SelectionKey, EditorGUILayout.ToggleLeft(label, candidateState.IsEnabled(candidate.SelectionKey)));
                        var rowRect = GUILayoutUtility.GetLastRect();
                        if (IsCandidateRowHovered(rowRect, Event.current))
                        {
                            nextHoveredCandidateKey = candidate.SelectionKey;
                            if (Event.current.type == EventType.Repaint)
                                EditorGUI.DrawRect(rowRect, new Color(1f, 0.82f, 0.14f, 0.14f));
                        }
                        if (candidateState.IsEnabled(candidate.SelectionKey))
                            selectedCount++;
                    }
                }
            }
            if (CanUpdateCandidateHoverFromEvent(Event.current))
                SetHoveredCandidateKey(api, nextHoveredCandidateKey);

            using (new EditorGUI.DisabledScope(candidates == null || candidates.Length == 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("All", GUILayout.Height(22f)))
                    {
                        candidateState.SetAllEnabled(true);
                        api?.Repaint();
                    }

                    if (GUILayout.Button("Clear", GUILayout.Height(22f)))
                    {
                        candidateState.SetAllEnabled(false);
                        api?.Repaint();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(api == null || selectedCount <= 0))
            {
                if (GUILayout.Button("GO", GUILayout.Height(28f)))
                {
                    ReanimatorProjectorOperationRunner.Apply(api, settings, candidateState.BuildSelectedKeys());
                }
            }
        }

        return true;
    }

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        StorePreviewContext(context);
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        StorePreviewContext(context);
        if (settings.Mode == ReanimatorProjectorMode.Stretch)
            api?.DrawProjectorStretchPreviewOverlay(
                settings.TargetSelectionKey,
                context,
                candidateState.BuildSelectedKeys(),
                settings.UseAplStretch,
                settings.SourceInsideTargetOnly,
                settings.StretchKeepContour && !settings.UseAplStretch,
                settings.StretchUseCull,
                settings.StretchCullDistance);
        DrawSelectedSourcePreview(api, context);
        if (!string.IsNullOrEmpty(candidateState.HoveredCandidateKey))
            api?.DrawProjectorCandidateHoverOverlay(context, candidateState.HoveredCandidateKey);
    }

    private void StorePreviewContext(ReanimatorPreviewContext context)
    {
        if (context != null)
            lastPreviewContext = context;
    }

    private void SetHoveredCandidateKey(Reanimator.SystemApi api, string key)
    {
        if (candidateState.SetHoveredKey(key))
            api?.Repaint();
    }

    private void DrawSelectedSourcePreview(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (api == null || context == null)
            return;

        var keys = candidateState.BuildSelectedKeys();
        for (var i = 0; i < keys.Length; i++)
        {
            if (string.Equals(keys[i], candidateState.HoveredCandidateKey, StringComparison.OrdinalIgnoreCase))
                continue;

            api.DrawProjectorCandidateHoverOverlay(context, keys[i]);
        }
    }

    internal static bool IsCandidateRowHovered(Rect rowRect, Event e)
    {
        return ReanimatorProjectorUiAlgorithms.IsCandidateRowHovered(rowRect, e);
    }

    internal static bool CanUpdateCandidateHoverFromEvent(Event e)
    {
        return ReanimatorProjectorUiAlgorithms.CanUpdateCandidateHoverFromEvent(e);
    }

    internal static bool IsCandidateViewRectVisible(Rect previewRect, Rect candidateRect, float padding = 0f)
    {
        return ReanimatorProjectorUiAlgorithms.IsCandidateViewRectVisible(previewRect, candidateRect, padding);
    }
}

internal sealed class ReanimatorProjectorFixWeightsModule : ReanimatorActionModule
{
    public ReanimatorProjectorFixWeightsModule()
        : base("weights.projector-fix", "Weights", 40, 55, "FIX", 30)
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        if (api == null)
            return;

        if (api.FixMissingVertexWeights(out var fixedCount, out var missingCount, out _, out var error))
            api.ShowNotification($"FIX weights: {fixedCount}/{missingCount}");
        else
            api.ShowNotification(string.IsNullOrWhiteSpace(error) ? "FIX weights failed" : error);

        api.Repaint();
    }
}

internal sealed class ReanimatorAutoWeightsModule : ReanimatorActionModule
{
    public ReanimatorAutoWeightsModule()
        : base("weights.auto", "Weights", 40, 10, "Auto", 10)
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.ApplyAutoWeights();
    }
}

internal sealed partial class ReanimatorWeightBrushModule : ReanimatorToolModeModule, IReanimatorPreviewModule, IReanimatorBoneDisplayModule, IReanimatorBoneInputModule
{
    private static readonly string[] Modes = { "AddAndSubtract", "Add", "Subtract", "Replace" };
    private static readonly Color DisabledBoneColor = new Color(0.74f, 0.74f, 0.74f, 1f);
    private static readonly Color HoveredBoneColor = new Color(1f, 0.9f, 0.15f, 1f);
    private const float DisabledBoneAlpha = 0.28f;
    private const int BonePickerCandidateCount = 10;
    private const string ShortcutHint =
        "B - activate Weight Brush\n" +
        "LMB drag - paint | Ctrl/Cmd + LMB - subtract (AddAndSubtract mode) | Shift + LMB - smooth\n" +
        "Ctrl/Cmd + Shift + LMB - cap influences\n" +
        "Alt + LMB - replace active bone | Alt + Shift + LMB - add bone\n" +
        "RMB - select bone | RMB drag - rotate bone\n" +
        "Ctrl/Cmd + Shift + Wheel - cap (1-3)\n" +
        "Shift + Wheel - radius | Ctrl/Cmd + Wheel - amount | Alt + Wheel - hardness\n" +
        "Bone color: LMB - select | double-click - center | RMB - remove";

    private int boneIndex = -1;
    private int modeIndex;
    private bool normalize = true;
    private float amount = 0.25f;
    private float radius = 32f;
    private float hardness = 0.65f;
    private bool isPainting;
    private int controlId;
    private bool hasCursor;
    private Vector2 cursorCanvas;
    private bool showAllBonesForAltSelection;
    private int hoveredRendererBoneIndex = -1;

    public ReanimatorWeightBrushModule()
        : base("weights.brush", "Weights", 40, 20, "Brush (B)", ReanimatorToolMode.WeightBrush, 10)
    {
    }

    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => false;
    public ReanimatorBoneInputOptions BoneInputOptions => new ReanimatorBoneInputOptions(1, true);

    public bool ClearSelectedBone(Reanimator.SystemApi api)
    {
        var changed = boneIndex >= 0;
        boneIndex = -1;
        EndStroke();
        ClearHoveredRendererBone(api);
        if (api != null && api.TrySelectWeightBrushBone(-1))
            changed = true;
        return changed;
    }

    public override bool DrawSettings(Reanimator.SystemApi api, float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(Vector2.zero, GUILayout.Height(contentHeight)))
            {
                EditorGUILayout.LabelField("Weight Brush", EditorStyles.boldLabel);

                if (api == null || !api.TryGetWeightBrushTargetInfo(out var target))
                {
                    EditorGUILayout.LabelField("Select a sprite in preview.");
                    DrawBrushControls(null, 0);
                    DrawShortcutHint();
                    return true;
                }

                var boneOptions = BuildBoneOptions(target.spriteBoneNames);
                EditorGUILayout.LabelField("Sprite", $"{target.layerName} ({target.vertexCount})");
                DrawBrushControls(api, boneOptions, target);
                DrawShortcutHint();
                SyncSelectedBone(api, target);
            }
        }

        return true;
    }

    private static void DrawShortcutHint()
    {
        EditorGUILayout.HelpBox(ShortcutHint, MessageType.Info);
    }

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        var e = Event.current;
        if (api == null || context == null || e == null)
            return;

        var altSelection = IsAltModifier(e);
        if (showAllBonesForAltSelection != altSelection)
        {
            showAllBonesForAltSelection = altSelection;
            GUI.changed = true;
            api.Repaint();
        }

        var cursorChanged = false;
        if (context.LocalRect.Contains(e.mousePosition))
        {
            cursorChanged = !hasCursor || (cursorCanvas - e.mousePosition).sqrMagnitude > 0.01f;
            hasCursor = true;
            cursorCanvas = e.mousePosition;
        }
        else if (e.type == EventType.MouseLeaveWindow)
        {
            cursorChanged = hasCursor;
            hasCursor = false;
        }

        if (cursorChanged)
        {
            GUI.changed = true;
            api.Repaint();
        }

        if (e.type == EventType.ScrollWheel && IsControlModifier(e) && IsShiftModifier(e) && context.LocalRect.Contains(e.mousePosition))
        {
            var scroll = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
            if (Mathf.Abs(scroll) > 0.0001f)
                api.WeightBrushCapInfluenceCount += scroll < 0f ? 1 : -1;
            e.Use();
            GUI.changed = true;
            api.Repaint();
            return;
        }

        if (e.type == EventType.ScrollWheel && IsShiftModifier(e) && !IsControlModifier(e) && context.LocalRect.Contains(e.mousePosition))
        {
            var scroll = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
            radius = Mathf.Clamp(radius - scroll * 4f, 1f, 256f);
            e.Use();
            GUI.changed = true;
            api.Repaint();
            return;
        }

        if (e.type == EventType.ScrollWheel && IsControlModifier(e) && !IsShiftModifier(e) && context.LocalRect.Contains(e.mousePosition))
        {
            var scroll = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
            amount = Mathf.Clamp01(amount - scroll * 0.03f);
            e.Use();
            GUI.changed = true;
            api.Repaint();
            return;
        }

        if (e.type == EventType.ScrollWheel && IsAltModifier(e) && context.LocalRect.Contains(e.mousePosition))
        {
            var scroll = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
            hardness = Mathf.Clamp01(hardness - scroll * 0.03f);
            e.Use();
            GUI.changed = true;
            api.Repaint();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 1 && context.LocalRect.Contains(e.mousePosition))
        {
            if (!api.TrySelectWeightBrushBoneAt(context, e.mousePosition, out var selectedSpriteBone))
            {
                e.Use();
                return;
            }

            boneIndex = selectedSpriteBone;
            EndStroke();
            GUI.changed = true;
            api.Repaint();
            return;
        }

        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.button != 0)
            return;

        if (!context.LocalRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp)
                EndStroke();
            return;
        }

        if (e.type == EventType.MouseDown)
        {
            if (IsAltModifier(e))
            {
                if (IsShiftModifier(e))
                {
                    if (api.TryAddWeightBrushBoneAt(context, e.mousePosition, out var addedSpriteBone, out var error))
                        boneIndex = addedSpriteBone;
                    else if (!string.IsNullOrWhiteSpace(error))
                        api.ShowNotification(error);
                }
                else if (api.TryReplaceWeightBrushBoneAt(context, e.mousePosition, boneIndex, out var replacedSpriteBone))
                {
                    boneIndex = replacedSpriteBone;
                }

                EndStroke();
                e.Use();
                GUI.changed = true;
                api.Repaint();
                return;
            }

            SyncSelectedBone(api);
            api.BeginWeightBrushStroke();
            controlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = controlId;
            isPainting = true;
            ApplyBrushAt(api, context, e.mousePosition, IsControlModifier(e), IsShiftModifier(e));
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (IsAltModifier(e))
            {
                EndStroke();
                e.Use();
                GUI.changed = true;
                return;
            }

            if (!isPainting || GUIUtility.hotControl != controlId)
                return;

            SyncSelectedBone(api);
            ApplyBrushAt(api, context, e.mousePosition, IsControlModifier(e), IsShiftModifier(e));
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            EndStroke();
            e.Use();
            GUI.changed = true;
        }
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || context == null || !hasCursor)
            return;

        var alpha = context.Visibility != null ? context.Visibility.VertexAlpha : 1f;
        var controlModifier = IsControlModifier(Event.current);
        var shiftModifier = IsShiftModifier(Event.current);
        var altModifier = IsAltModifier(Event.current);
        var brushColor = GetBrushCircleColor(controlModifier, shiftModifier);
        var brushRadius = Mathf.Max(1f, radius);
        Handles.BeginGUI();
        var prev = Handles.color;
        if (!altModifier)
        {
            Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, Mathf.Clamp01(alpha));
            Handles.DrawWireDisc(cursorCanvas, Vector3.forward, brushRadius);
            var hardRadius = brushRadius * Mathf.Clamp01(hardness);
            if (hardRadius > 1f && hardRadius < brushRadius - 0.5f)
            {
                Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, Mathf.Clamp01(alpha) * 0.45f);
                Handles.DrawWireDisc(cursorCanvas, Vector3.forward, hardRadius);
            }
        }
        DrawCursorBrushBars(
            context.LocalRect,
            cursorCanvas,
            brushRadius,
            brushColor,
            alpha,
            GetBrushActionLabel(controlModifier, shiftModifier, altModifier),
            api != null ? api.WeightBrushCapInfluenceCount : 3,
            controlModifier && shiftModifier && !altModifier);
        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawCursorBrushBars(
        Rect localRect,
        Vector2 cursor,
        float brushRadius,
        Color brushColor,
        float alpha,
        string actionLabel,
        int capInfluenceCount,
        bool showCapIndicator)
    {
        const float width = 7f;
        const float height = 38f;
        const float padding = 8f;
        const float gap = 3f;

        var x = cursor.x + brushRadius + padding;
        var y = cursor.y - height * 0.5f;
        if (localRect.height > height + 4f)
            y = Mathf.Clamp(y, localRect.yMin + 2f, localRect.yMax - height - 2f);

        DrawCursorValueBar(new Rect(x, y, width, height), amount, brushColor, alpha);
        DrawCursorValueBar(new Rect(x + width + gap, y, width, height), hardness, new Color(1f, 0.82f, 0.15f, 1f), alpha);
        if (showCapIndicator)
            DrawCursorCapIndicator(new Rect(x + (width + gap) * 2f, y, 16f, height), capInfluenceCount, alpha);
        var labelWidth = width * 2f + gap + (showCapIndicator ? 37f : 14f);
        DrawCursorBarLabel(new Rect(x - 7f, y - 15f, labelWidth, 13f), actionLabel, brushColor, alpha);
    }

    private static void DrawCursorCapIndicator(Rect rect, int capInfluenceCount, float alpha)
    {
        var frameColor = new Color(0.72f, 0.38f, 1f, Mathf.Clamp01(alpha));
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, Mathf.Clamp01(alpha * 0.8f)));
        EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), new Color(0.12f, 0.06f, 0.16f, Mathf.Clamp01(alpha * 0.8f)));
        DrawColorButtonBorder(rect, frameColor, 1f);

        var style = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = frameColor }
        };
        GUI.Label(rect, Mathf.Clamp(capInfluenceCount, 1, 3).ToString(), style);
    }

    private static void DrawCursorValueBar(Rect frame, float value, Color color, float alpha)
    {
        var inner = new Rect(frame.x + 1f, frame.y + 1f, frame.width - 2f, frame.height - 2f);
        var fillHeight = inner.height * Mathf.Clamp01(value);
        var fill = new Rect(inner.x, inner.yMax - fillHeight, inner.width, fillHeight);
        var fillColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));

        EditorGUI.DrawRect(frame, new Color(0f, 0f, 0f, Mathf.Clamp01(alpha * 0.8f)));
        EditorGUI.DrawRect(inner, new Color(0.06f, 0.06f, 0.06f, Mathf.Clamp01(alpha * 0.55f)));
        EditorGUI.DrawRect(fill, fillColor);
    }

    private static void DrawCursorBarLabel(Rect rect, string text, Color color, float alpha)
    {
        var style = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha)) }
        };
        GUI.Label(rect, text, style);
    }

    private string GetBrushActionLabel(bool control, bool shift, bool alt)
    {
        if (alt)
            return shift ? "add" : "swi";
        if (control && shift)
            return "cap";
        if (shift)
            return "smu";
        if (control || Mathf.Clamp(modeIndex, 0, Modes.Length - 1) == 2)
            return "rem";
        return "add";
    }

    private Color GetBrushCircleColor(bool control, bool shift)
    {
        if (control && shift)
            return new Color(0.72f, 0.38f, 1f, 1f);
        if (shift)
            return new Color(0.1f, 0.9f, 0.32f, 1f);

        switch (Mathf.Clamp(modeIndex, 0, Modes.Length - 1))
        {
            case 0:
                return control ? new Color(1f, 0.48f, 0.08f, 1f) : new Color(0f, 0.82f, 0.9f, 1f);
            case 1:
                return new Color(0f, 0.82f, 0.9f, 1f);
            case 2:
                return new Color(1f, 0.48f, 0.08f, 1f);
            default:
                return new Color(1f, 0.82f, 0.15f, 1f);
        }
    }

    private static bool IsShiftModifier(Event e)
    {
        return e != null && (e.shift || (e.modifiers & EventModifiers.Shift) != 0);
    }

    private static bool IsControlModifier(Event e)
    {
        return e != null && (e.control || (e.modifiers & EventModifiers.Control) != 0 || (e.modifiers & EventModifiers.Command) != 0);
    }

    private static bool IsAltModifier(Event e)
    {
        return e != null && (e.alt || (e.modifiers & EventModifiers.Alt) != 0);
    }

    public bool TryGetBoneDisplayOverride(
        Reanimator.SystemApi api,
        int rendererBoneIndex,
        out ReanimatorBoneDisplayOverride displayOverride)
    {
        displayOverride = default;
        if (hoveredRendererBoneIndex == rendererBoneIndex)
        {
            displayOverride = new ReanimatorBoneDisplayOverride(HoveredBoneColor, 1f);
            return true;
        }

        if (api == null || !api.TryGetWeightBrushTargetInfo(out var target))
            return false;
        if (showAllBonesForAltSelection || IsAltModifier(Event.current))
            return false;

        if (IsMappedRendererBone(target, rendererBoneIndex))
            return false;

        displayOverride = new ReanimatorBoneDisplayOverride(DisabledBoneColor, DisabledBoneAlpha);
        return true;
    }

    private void EndStroke()
    {
        if (GUIUtility.hotControl == controlId && controlId != 0)
            GUIUtility.hotControl = 0;

        isPainting = false;
        controlId = 0;
    }

    private void ApplyBrushAt(
        Reanimator.SystemApi api,
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        bool subtractModifier,
        bool smoothModifier)
    {
        if (api == null)
            return;

        if (api.TryApplyWeightBrushAt(
                context,
                mouseCanvas,
                boneIndex,
                modeIndex,
                amount,
                radius,
                hardness,
                normalize,
                subtractModifier,
                smoothModifier,
                api.WeightBrushCapInfluenceCount))
            api.Repaint();
    }

    private void DrawBrushControls(string[] boneOptions, int spriteBoneCount)
    {
        modeIndex = EditorGUILayout.Popup("Mode", Mathf.Clamp(modeIndex, 0, Modes.Length - 1), Modes);
        using (new EditorGUI.DisabledScope(boneOptions == null || boneOptions.Length <= 1 || spriteBoneCount <= 0))
        {
            boneIndex = EditorGUILayout.Popup("Bone", Mathf.Clamp(boneIndex + 1, 0, boneOptions != null ? boneOptions.Length - 1 : 0), boneOptions ?? new[] { "None" }) - 1;
        }

        normalize = EditorGUILayout.Toggle("Normalize", normalize);
        amount = EditorGUILayout.Slider("Amount", amount, 0f, 1f);
        radius = EditorGUILayout.Slider("Radius", radius, 1f, 256f);
        hardness = EditorGUILayout.Slider("Hardness", hardness, 0f, 1f);
    }

    private void DrawBrushControls(Reanimator.SystemApi api, string[] boneOptions, PsdLayerPreviewRenderer.WeightBrushTargetInfo target)
    {
        modeIndex = EditorGUILayout.Popup("Mode", Mathf.Clamp(modeIndex, 0, Modes.Length - 1), Modes);
        using (new EditorGUI.DisabledScope(boneOptions == null || boneOptions.Length <= 1 || target.spriteBoneCount <= 0))
        {
            boneIndex = EditorGUILayout.Popup("Bone", Mathf.Clamp(boneIndex + 1, 0, boneOptions != null ? boneOptions.Length - 1 : 0), boneOptions ?? new[] { "None" }) - 1;
        }

        DrawBoneColorButtons(api, target);
        normalize = EditorGUILayout.Toggle("Normalize", normalize);
        amount = EditorGUILayout.Slider("Amount", amount, 0f, 1f);
        radius = EditorGUILayout.Slider("Radius", radius, 1f, 256f);
        hardness = EditorGUILayout.Slider("Hardness", hardness, 0f, 1f);
    }

    private void DrawBoneColorButtons(Reanimator.SystemApi api, PsdLayerPreviewRenderer.WeightBrushTargetInfo target)
    {
        const float size = 24f;
        using (new EditorGUILayout.HorizontalScope())
        {
            var slotCount = Mathf.Max(0, target.spriteBoneCount);
            for (var i = 0; i < slotCount; i++)
            {
                var valid = target.spriteBoneToRendererBone != null &&
                            i < target.spriteBoneToRendererBone.Length &&
                            target.spriteBoneToRendererBone[i] >= 0;
                var color = GetBoneButtonColor(target, i, valid);
                var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
                HandleBoneColorButtonInput(api, target, i, rect, valid);

                EditorGUI.DrawRect(rect, color);
                DrawColorButtonBorder(
                    rect,
                    boneIndex == i ? new Color(1f, 0.9f, 0.15f, 1f) : new Color(0f, 0f, 0f, 0.65f),
                    boneIndex == i ? 2f : 1f);
            }

            if (GUILayout.Button("+", GUILayout.Width(size), GUILayout.Height(size)))
            {
                ShowBonePicker(api, slotCount, GUILayoutUtility.GetLastRect());
                api?.Repaint();
            }

            GUILayout.FlexibleSpace();
        }
    }

    private void HandleBoneColorButtonInput(
        Reanimator.SystemApi api,
        PsdLayerPreviewRenderer.WeightBrushTargetInfo target,
        int index,
        Rect rect,
        bool valid)
    {
        var e = Event.current;
        if (e == null || e.type != EventType.MouseDown || (e.button != 0 && e.button != 1) || !rect.Contains(e.mousePosition))
            return;

        if (e.button == 1)
        {
            if (!valid)
                return;

            EndStroke();
            ClearHoveredRendererBone(api);
            string error = null;
            if (api != null && api.RemoveWeightBrushBoneSlot(index, out _, out error))
            {
                if (boneIndex == index)
                    boneIndex = -1;
                else if (boneIndex > index)
                    boneIndex--;
            }
            else if (!string.IsNullOrEmpty(error))
            {
                api?.ShowNotification(error);
            }

            e.Use();
            api?.Repaint();
            return;
        }

        if (!valid)
        {
            boneIndex = index;
            ShowBonePicker(api, index, rect);
            e.Use();
            api?.Repaint();
            return;
        }

        boneIndex = index;
        SyncSelectedBone(api, target);
        if (e.clickCount >= 2 &&
            target.spriteBoneToRendererBone != null &&
            index < target.spriteBoneToRendererBone.Length)
            api?.TryCenterPreviewOnRendererBone(target.spriteBoneToRendererBone[index]);

        e.Use();
        api?.Repaint();
    }

    private static Color GetBoneButtonColor(PsdLayerPreviewRenderer.WeightBrushTargetInfo target, int index, bool valid)
    {
        if (!valid)
            return new Color(0.22f, 0.22f, 0.22f, 0.45f);

        if (target.spriteBoneColors == null || index >= target.spriteBoneColors.Length)
            return Color.gray;

        var color = target.spriteBoneColors[index];
        color.a = 1f;
        return color;
    }

    private static void DrawColorButtonBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void ShowBonePicker(Reanimator.SystemApi api, int spriteBoneIndex, Rect activatorRect)
    {
        EndStroke();
        ClearHoveredRendererBone(api);
        if (api == null)
            return;

        PopupWindow.Show(activatorRect, new WeightBrushBonePickerPopup(this, api, spriteBoneIndex));
    }

    private void SetHoveredRendererBone(Reanimator.SystemApi api, int rendererBoneIndex)
    {
        if (hoveredRendererBoneIndex == rendererBoneIndex)
            return;

        hoveredRendererBoneIndex = rendererBoneIndex;
        api?.Repaint();
    }

    private void ClearHoveredRendererBone(Reanimator.SystemApi api)
    {
        SetHoveredRendererBone(api, -1);
    }

    private void SyncSelectedBone(Reanimator.SystemApi api)
    {
        if (api == null || boneIndex < 0)
            return;

        if (api.TrySelectWeightBrushBone(boneIndex))
            api.Repaint();
    }

    private void SyncSelectedBone(Reanimator.SystemApi api, PsdLayerPreviewRenderer.WeightBrushTargetInfo target)
    {
        if (api == null)
            return;

        if (target.spriteBoneToRendererBone == null ||
            boneIndex < 0 ||
            boneIndex >= target.spriteBoneToRendererBone.Length)
        {
            if (api.TrySelectWeightBrushBone(-1))
                api.Repaint();
            return;
        }

        if (api.TrySelectWeightBrushBone(boneIndex))
            api.Repaint();
    }

    private static string[] BuildBoneOptions(string[] spriteBoneNames)
    {
        var count = spriteBoneNames != null ? spriteBoneNames.Length : 0;
        var options = new string[count + 1];
        options[0] = "None";
        for (var i = 0; i < count; i++)
        {
            var name = spriteBoneNames[i];
            options[i + 1] = string.IsNullOrWhiteSpace(name) ? $"Bone {i}" : name;
        }

        return options;
    }

    private sealed class WeightBrushBonePickerPopup : PopupWindowContent
    {
        private const float RowHeight = 22f;
        private const float HeaderHeight = 4f;
        private const float Width = 230f;

        private readonly ReanimatorWeightBrushModule owner;
        private readonly Reanimator.SystemApi api;
        private readonly int spriteBoneIndex;
        private readonly PsdLayerPreviewRenderer.WeightBrushBoneCandidateInfo[] candidates;

        public WeightBrushBonePickerPopup(ReanimatorWeightBrushModule owner, Reanimator.SystemApi api, int spriteBoneIndex)
        {
            this.owner = owner;
            this.api = api;
            this.spriteBoneIndex = spriteBoneIndex;
            candidates = api != null
                ? api.GetNearestWeightBrushBoneCandidates(BonePickerCandidateCount)
                : Array.Empty<PsdLayerPreviewRenderer.WeightBrushBoneCandidateInfo>();
        }

        public override Vector2 GetWindowSize()
        {
            var rowCount = candidates != null && candidates.Length > 0 ? candidates.Length : 1;
            return new Vector2(Width, HeaderHeight + rowCount * RowHeight + 4f);
        }

        public override void OnClose()
        {
            owner.ClearHoveredRendererBone(api);
        }

        public override void OnGUI(Rect rect)
        {
            var e = Event.current;
            if (candidates == null || candidates.Length == 0)
            {
                owner.ClearHoveredRendererBone(api);
                GUI.Label(new Rect(8f, HeaderHeight, rect.width - 16f, RowHeight), "No free bones", EditorStyles.miniLabel);
                return;
            }

            var hoveredBone = -1;
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                var row = new Rect(4f, HeaderHeight + i * RowHeight, rect.width - 8f, RowHeight);
                var hovered = row.Contains(e.mousePosition);
                if (hovered)
                {
                    hoveredBone = candidate.rendererBoneIndex;
                    EditorGUI.DrawRect(row, new Color(0.24f, 0.24f, 0.24f, 1f));
                }

                DrawCandidateRow(row, candidate);
                if (hovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    if (api.AssignWeightBrushBoneSlot(spriteBoneIndex, candidate.rendererBoneIndex, out var assignedSpriteBoneIndex, out var error))
                    {
                        owner.boneIndex = assignedSpriteBoneIndex;
                        owner.ClearHoveredRendererBone(api);
                        api.Repaint();
                        editorWindow.Close();
                    }
                    else
                    {
                        api.ShowNotification(string.IsNullOrWhiteSpace(error) ? "Assign bone failed" : error);
                    }

                    e.Use();
                    return;
                }
            }

            owner.SetHoveredRendererBone(api, hoveredBone);
            if (e.type == EventType.MouseMove || e.type == EventType.Repaint)
                editorWindow.Repaint();
        }

        private static void DrawCandidateRow(Rect row, PsdLayerPreviewRenderer.WeightBrushBoneCandidateInfo candidate)
        {
            var swatch = new Rect(row.x + 4f, row.y + 4f, 14f, 14f);
            var label = new Rect(row.x + 22f, row.y + 2f, row.width - 78f, row.height - 4f);
            var distance = new Rect(row.xMax - 52f, row.y + 2f, 48f, row.height - 4f);

            var color = candidate.color;
            color.a = 1f;
            EditorGUI.DrawRect(swatch, color);
            DrawColorButtonBorder(swatch, new Color(0f, 0f, 0f, 0.75f), 1f);

            GUI.Label(label, string.IsNullOrWhiteSpace(candidate.boneName) ? $"bone_{candidate.rendererBoneIndex}" : candidate.boneName, EditorStyles.miniLabel);
            var distanceStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.72f, 0.72f, 0.72f, 1f) }
            };
            GUI.Label(distance, candidate.distance.ToString("0.#"), distanceStyle);
        }
    }

    private static bool IsMappedRendererBone(PsdLayerPreviewRenderer.WeightBrushTargetInfo target, int rendererBoneIndex)
    {
        var map = target.spriteBoneToRendererBone;
        if (map == null)
            return false;

        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] == rendererBoneIndex)
                return true;
        }

        return false;
    }
}

internal sealed partial class ReanimatorBoneInfluenceModule : ReanimatorToolModeModule
{
    public ReanimatorBoneInfluenceModule()
        : base("influence.bone", "Influence", 50, 10, "Bone", ReanimatorToolMode.BoneInfluence, 10, (api, height) => api?.DrawBoneInfluenceSettings(height))
    {
    }
}

internal sealed partial class ReanimatorSpriteInfluenceModule : ReanimatorToolModeModule
{
    public ReanimatorSpriteInfluenceModule()
        : base("influence.sprite", "Influence", 50, 20, "Sprite", ReanimatorToolMode.SpriteInfluence, 10, (api, height) => api?.DrawSpriteInfluenceSettings(height))
    {
    }
}
#endif
