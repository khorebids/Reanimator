#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const string EmptyPreviewLogoPath = "Assets/Plugins/Reanimator/Graphics/logo.png";
    private static Texture2D emptyPreviewLogo;
    private static GUIStyle orangeToolbarButtonStyle;
    private static Texture2D orangeToolbarButtonNormalTexture;
    private static Texture2D orangeToolbarButtonHoverTexture;
    private static Texture2D orangeToolbarButtonActiveTexture;

    private void DrawTestTab()
    {
        UpdateWindowEventOptions();
        EnsureWeightArrays();
        EnsureDefaultToolMode();
        if (TryGetExternalBlockingProgress(out _, out _, out _))
            HandleExternalBlockingProgressInput();

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Toggle(previewMode == PreviewMode.Skel, "Skinning Editor", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                SetPreviewMode(PreviewMode.Skel);
            if (GUILayout.Toggle(previewMode == PreviewMode.Sprite, "Sprite Sheet", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                SetPreviewMode(PreviewMode.Sprite);
            if (GUILayout.Toggle(previewMode == PreviewMode.Comfimator, "Comfimator", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                SetPreviewMode(PreviewMode.Comfimator);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!TryValidateAllInPreflight(out _)))
            {
                if (GUILayout.Button("ALLIN", GetOrangeToolbarButtonStyle(), GUILayout.Width(55f)))
                    InvokeComfimatorButton("ALLIN");
            }
            GUILayout.Space(4f);
            GUILayout.Button("Visibility", EditorStyles.toolbarButton, GUILayout.Width(70f));
            using (new EditorGUI.DisabledScope(layerPreviewRenderer == null || !layerPreviewRenderer.HasPendingChanges))
            {
                if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                    RevertChanges();
                if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                    ApplyChanges();
            }
        }

        var toolbarRect = GUILayoutUtility.GetLastRect();
        if (Event.current.type == EventType.Repaint)
            cachedToolbarRect = toolbarRect;

        if (previewNavigator == null)
            previewNavigator = new PreviewNavigator();
        if (layerPreviewRenderer == null)
            layerPreviewRenderer = new PsdLayerPreviewRenderer();

        rightPanelWidth = ReanimatorWindowLayout.ClampRightPanelWidth(rightPanelWidth, position.width);
        var windowLayout = ReanimatorWindowLayout.Calculate(position, default, cachedToolbarRect, rightPanelWidth);

        GUI.BeginGroup(windowLayout.ContentRect);
        var localLeftRect = windowLayout.LeftPanelRect;
        var localRightRect = windowLayout.RightPanelRect;
        var localPreviewRect = windowLayout.PreviewRect;
        HandleRightPanelWidthSplitter(windowLayout.ContentRect, windowLayout.RightPanelSplitterRect);

        if (localPreviewRect.width > 0f && localPreviewRect.height > 0f)
        {
            GUI.BeginGroup(localPreviewRect);
            var previewRect = new Rect(0f, 0f, localPreviewRect.width, localPreviewRect.height);
            cachedPreviewRect = previewRect;
            GUI.BeginClip(previewRect);
            var clippedPreviewRect = new Rect(0f, 0f, previewRect.width, previewRect.height);
            var path = GetSelectedAssetPath();
            var isEmptyPreview = string.IsNullOrWhiteSpace(path) || !IsPsdImporterAsset(path);
            var previewCacheReady = !isEmptyPreview &&
                                    layerPreviewRenderer != null &&
                                    layerPreviewRenderer.IsPreviewCacheReady(path);
            if (!isEmptyPreview && !previewCacheReady)
                layerPreviewRenderer.RequestPreviewCacheRefresh(path);
            var spriteSelectionLockButtonRect = GetPreviewToolbarButtonRect(clippedPreviewRect, 0);
            var spriteIsolationButtonRect = GetPreviewToolbarButtonRect(clippedPreviewRect, 1);
            var boneDeselectButtonRect = GetPreviewToolbarButtonRect(clippedPreviewRect, 2);
            var spriteDeselectButtonRect = GetPreviewToolbarButtonRect(clippedPreviewRect, 3);
            var vertexInfluenceVisibilityButtonRect = GetPreviewToolbarButtonRect(clippedPreviewRect, 4);
            var showSpriteSelectionControls = previewMode != PreviewMode.Comfimator;
            if (showSpriteSelectionControls)
            {
                HandlePreviewSpriteSelectionLockButtonInput(spriteSelectionLockButtonRect);
                HandlePreviewSelectedSpriteIsolationButtonInput(spriteIsolationButtonRect);
                HandlePreviewBoneDeselectButtonInput(boneDeselectButtonRect);
                HandlePreviewSpriteDeselectButtonInput(spriteDeselectButtonRect);
                HandlePreviewVertexInfluenceVisibilityButtonInput(vertexInfluenceVisibilityButtonRect);
            }
            var shouldProcessPreview = ShouldProcessPreviewForCurrentEvent() && ShouldDrawPreviewContentThisFrame();
            var drawOptions = CreatePreviewDrawOptions();
            var previewEvent = Event.current;
            var suppressNavigatorForCurve =
                !isEmptyPreview &&
                previewEvent != null &&
                ((bottomToolMode == ReanimatorToolMode.WeightSlider &&
                  !previewEvent.alt &&
                  previewEvent.button == 0 &&
                  (previewEvent.type == EventType.MouseDown ||
                   previewEvent.type == EventType.MouseDrag ||
                   previewEvent.type == EventType.MouseUp)) ||
                 (bottomToolMode == ReanimatorToolMode.WeightBrush &&
                  previewEvent.type == EventType.ScrollWheel &&
                  (IsShiftModifier(previewEvent) || IsControlModifier(previewEvent) || IsAltModifier(previewEvent))) ||
                 (bottomToolMode == ReanimatorToolMode.WeightBrush &&
                  previewEvent.alt &&
                  previewEvent.button == 0 &&
                  (previewEvent.type == EventType.MouseDown ||
                   previewEvent.type == EventType.MouseDrag ||
                   previewEvent.type == EventType.MouseUp)));
            if (!suppressNavigatorForCurve &&
                previewNavigator.HandleInput(
                    clippedPreviewRect,
                    isEmptyPreview,
                    zoomAroundMouse: !isEmptyPreview))
                Repaint();

            if (isEmptyPreview)
                EditorGUI.DrawRect(clippedPreviewRect, Color.black);
            else
                DrawChecker(clippedPreviewRect, 16f, new Color(0.32f, 0.32f, 0.32f), new Color(0.22f, 0.22f, 0.22f));
            layerPreviewRenderer.BoneInfluenceOverlay = ShouldDrawInfluenceOverlayForCurrentMode()
                ? influenceVisibilityValue
                : 0f;
            layerPreviewRenderer.WeightSliderSmoothStrength = weightSmoothAmount;
            layerPreviewRenderer.UndoRequest = RegisterReanimatorUndo;
            var previousLayerSelectionKey = layerPreviewRenderer.SelectedLayerSelectionKey;
            var drewLayers = shouldProcessPreview &&
                             previewCacheReady &&
                             !string.IsNullOrWhiteSpace(path) && IsPsdImporterAsset(path) &&
                             layerPreviewRenderer.Draw(path, clippedPreviewRect, drawOptions);
            if (shouldProcessPreview &&
                previewCacheReady &&
                previewMode == PreviewMode.Comfimator &&
                ReanimatorExternalPanelRegistry.TryGet("Comfimator", out var comfimatorPanel))
            {
                comfimatorPanel.DrawPreviewOverlay(this);
            }
            RevealSelectedLayerInVisibilityPanelIfChanged(previousLayerSelectionKey);
            if (layerPreviewRenderer != null && layerPreviewRenderer.TryConsumeRadiusCullRadiusOverride(out var overriddenRadius))
                radiusCullRadius = Mathf.Max(0.01f, overriddenRadius);
            if (layerPreviewRenderer != null && layerPreviewRenderer.ConsumeLimbPreviewRepaintRequest())
                Repaint();
            if (bottomToolMode == ReanimatorToolMode.PreviewPose &&
                layerPreviewRenderer != null &&
                layerPreviewRenderer.PreviewTestAnimEnabled &&
                ShouldRunContinuousPreviewUpdates())
                Repaint();
            if (!drewLayers)
            {
                DrawEmptyPreviewLogo(clippedPreviewRect);
                if (!isEmptyPreview && !previewCacheReady)
                    DrawPreviewLoadingOverlay(clippedPreviewRect);
            }
            if (showSpriteSelectionControls)
            {
                DrawPreviewToolbarButton(
                    spriteSelectionLockButtonRect,
                    "PIN",
                    previewSpriteSelectionLocked,
                    "Sprite selection locked",
                    "Lock sprite selection");
                DrawPreviewToolbarButton(
                    spriteIsolationButtonRect,
                    "ISO",
                    previewSelectedSpriteIsolated,
                    "Selected sprite isolated",
                    "Isolate selected sprite");
                DrawPreviewToolbarButton(
                    boneDeselectButtonRect,
                    "DES",
                    layerPreviewRenderer != null && layerPreviewRenderer.SelectedBoneIndex >= 0,
                    "Deselect selected bone",
                    "No selected bone");
                DrawPreviewToolbarButton(
                    spriteDeselectButtonRect,
                    "SPD",
                    layerPreviewRenderer != null && !string.IsNullOrEmpty(layerPreviewRenderer.SelectedLayerSelectionKey),
                    "Deselect selected sprite",
                    "No selected sprite");
                DrawPreviewToolbarButton(
                    vertexInfluenceVisibilityButtonRect,
                    "V/I",
                    previewVertexInfluenceVisibilityMuted,
                    "Restore vertex and influence visibility",
                    "Hide vertex and influence visibility");
            }
            GUI.EndClip();

            GUI.EndGroup();
        }

        var panelBg = new Color(0.23f, 0.23f, 0.23f);
        EditorGUI.DrawRect(localLeftRect, panelBg);
        EditorGUI.DrawRect(localRightRect, panelBg);
        DrawRightPanelWidthSplitter(windowLayout.RightPanelSplitterRect);

        GUILayout.BeginArea(localLeftRect);
        DrawLeftTools();
        GUILayout.EndArea();

        DrawRightPanels(localRightRect);
        GUI.EndGroup();

        DrawExternalBlockingProgressOverlay();
    }

    private void RevealSelectedLayerInVisibilityPanelIfChanged(string previousSelectionKey)
    {
        if (layerPreviewRenderer == null)
            return;

        var currentSelectionKey = layerPreviewRenderer.SelectedLayerSelectionKey;
        if (string.IsNullOrEmpty(currentSelectionKey) ||
            string.Equals(previousSelectionKey ?? string.Empty, currentSelectionKey, StringComparison.OrdinalIgnoreCase))
            return;

        visibilitySprite = true;
        visibilityBone = false;
        visibilityAutoScrolledLayerSelectionKey = string.Empty;
        Repaint();
    }

    private static Rect GetPreviewToolbarButtonRect(Rect previewRect, int index)
    {
        return new Rect(previewRect.xMin + 6f + index * 38f, previewRect.yMin + 6f, 34f, 20f);
    }

    private void HandlePreviewSpriteSelectionLockButtonInput(Rect rect)
    {
        var e = Event.current;
        if (e == null)
            return;

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition))
            return;

        previewSpriteSelectionLocked = !previewSpriteSelectionLocked;
        e.Use();
        GUI.changed = true;
        Repaint();
    }

    private void HandlePreviewSelectedSpriteIsolationButtonInput(Rect rect)
    {
        var e = Event.current;
        if (e == null)
            return;

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition))
            return;

        previewSelectedSpriteIsolated = !previewSelectedSpriteIsolated;
        e.Use();
        GUI.changed = true;
        Repaint();
    }

    private void HandlePreviewBoneDeselectButtonInput(Rect rect)
    {
        var e = Event.current;
        if (e == null)
            return;

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition))
            return;

        var changed = false;
        if (GetActivePreviewModule() is ReanimatorWeightBrushModule weightBrushModule)
            changed |= weightBrushModule.ClearSelectedBone(Api);
        if (layerPreviewRenderer != null && layerPreviewRenderer.SelectBone(-1))
            changed = true;

        if (changed)
        {
            GUI.changed = true;
            Repaint();
        }

        e.Use();
    }

    private void HandlePreviewSpriteDeselectButtonInput(Rect rect)
    {
        var e = Event.current;
        if (e == null)
            return;

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition))
            return;

        if (layerPreviewRenderer != null && layerPreviewRenderer.SelectLayer(null))
        {
            GUI.changed = true;
            Repaint();
        }

        e.Use();
    }

    private void HandlePreviewVertexInfluenceVisibilityButtonInput(Rect rect)
    {
        var e = Event.current;
        if (e == null)
            return;

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition))
            return;

        TogglePreviewVertexInfluenceVisibility();
        e.Use();
        GUI.changed = true;
        Repaint();
    }

    private void TogglePreviewVertexInfluenceVisibility()
    {
        if (!previewVertexInfluenceVisibilityMuted)
        {
            previewVertexVisibilityBeforeMute = Mathf.Clamp01(vertexVisibilityValue);
            previewInfluenceVisibilityBeforeMute = Mathf.Clamp01(influenceVisibilityValue);
            vertexVisibilityValue = 0f;
            influenceVisibilityValue = 0f;
            previewVertexInfluenceVisibilityMuted = true;
            return;
        }

        vertexVisibilityValue = Mathf.Clamp01(previewVertexVisibilityBeforeMute);
        influenceVisibilityValue = Mathf.Clamp01(previewInfluenceVisibilityBeforeMute);
        previewVertexInfluenceVisibilityMuted = false;
    }

    private void DrawPreviewToolbarButton(Rect rect, string label, bool active, string activeTooltip, string inactiveTooltip)
    {
        var previousColor = GUI.color;
        EditorGUI.DrawRect(
            rect,
            active
                ? new Color(0.12f, 0.42f, 0.72f, 0.95f)
                : new Color(0.08f, 0.08f, 0.08f, 0.82f));
        GUI.color = Color.white;
        GUI.Label(
            rect,
            new GUIContent(label, active ? activeTooltip : inactiveTooltip),
            EditorStyles.centeredGreyMiniLabel);
        GUI.color = previousColor;
    }

    private bool TryCenterPreviewOnRendererBone(int rendererBoneIndex)
    {
        if (previewNavigator == null || layerPreviewRenderer == null)
            return false;
        if (cachedPreviewRect.width <= 0f || cachedPreviewRect.height <= 0f)
            return false;
        if (!layerPreviewRenderer.TryGetRendererBoneCenterCanvas(rendererBoneIndex, cachedPreviewRect, out var boneCenterCanvas))
            return false;

        previewNavigator.CenterOn(cachedPreviewRect, boneCenterCanvas);
        Repaint();
        return true;
    }

    private bool TryCenterPreviewOnLayer(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (previewNavigator == null || layerPreviewRenderer == null || layer == null)
            return false;
        if (cachedPreviewRect.width <= 0f || cachedPreviewRect.height <= 0f)
            return false;
        if (!layerPreviewRenderer.TryGetLayerCenterCanvas(layer, cachedPreviewRect, previewMode == PreviewMode.Sprite, out var layerCenterCanvas))
            return false;

        previewNavigator.CenterOn(cachedPreviewRect, layerCenterCanvas);
        Repaint();
        return true;
    }

    private bool TryCenterPreviewOnLayerHierarchyNode(string nodeKey)
    {
        if (previewNavigator == null || layerPreviewRenderer == null || string.IsNullOrEmpty(nodeKey))
            return false;
        if (cachedPreviewRect.width <= 0f || cachedPreviewRect.height <= 0f)
            return false;
        if (!layerPreviewRenderer.TryGetLayerHierarchyNodeCenterCanvas(nodeKey, cachedPreviewRect, previewMode == PreviewMode.Sprite, out var nodeCenterCanvas))
            return false;

        previewNavigator.CenterOn(cachedPreviewRect, nodeCenterCanvas);
        Repaint();
        return true;
    }

    private void HandleRightPanelWidthSplitter(Rect contentRect, Rect splitterRect)
    {
        var e = Event.current;
        if (e == null)
            return;

        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        if (e.type == EventType.MouseDown && e.button == 0 && splitterRect.Contains(e.mousePosition))
        {
            isRightPanelWidthResizing = true;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            isRightPanelWidthResizing = false;
            return;
        }

        if (!isRightPanelWidthResizing || e.type != EventType.MouseDrag)
            return;

        rightPanelWidth = ReanimatorWindowLayout.ClampRightPanelWidth(contentRect.xMax - e.mousePosition.x, contentRect.width);
        e.Use();
        Repaint();
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

    private static bool ShouldProcessPreviewForCurrentEvent()
    {
        var e = Event.current;
        if (e == null)
            return true;

        switch (e.type)
        {
            case EventType.Repaint:
            case EventType.MouseDown:
            case EventType.MouseDrag:
            case EventType.MouseUp:
            case EventType.ScrollWheel:
            case EventType.KeyDown:
            case EventType.KeyUp:
            case EventType.DragUpdated:
            case EventType.DragPerform:
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetExternalBlockingProgress(out string title, out string status, out float progress)
    {
        title = string.Empty;
        status = string.Empty;
        progress = 0f;

        if (!ReanimatorExternalPanelRegistry.TryGet("Comfimator", out var panel) ||
            !(panel is IReanimatorExternalBlockingProgressPanel progressPanel))
        {
            return false;
        }

        return progressPanel.TryGetBlockingProgress(out title, out status, out progress);
    }

    private void HandleExternalBlockingProgressInput()
    {
        var e = Event.current;
        if (e == null)
            return;

        switch (e.type)
        {
            case EventType.MouseDown:
            case EventType.MouseUp:
            case EventType.MouseDrag:
            case EventType.ScrollWheel:
            case EventType.KeyDown:
            case EventType.KeyUp:
            case EventType.DragUpdated:
            case EventType.DragPerform:
            case EventType.ContextClick:
                e.Use();
                Repaint();
                break;
        }
    }

    private void DrawExternalBlockingProgressOverlay()
    {
        if (!TryGetExternalBlockingProgress(out var title, out var status, out var progress))
            return;

        var rect = new Rect(0f, 0f, position.width, position.height);
        var boxWidth = Mathf.Min(440f, Mathf.Max(260f, rect.width - 48f));
        var boxHeight = 116f;
        var boxRect = new Rect(
            rect.center.x - boxWidth * 0.5f,
            rect.center.y - boxHeight * 0.5f,
            boxWidth,
            boxHeight);
        var titleRect = new Rect(boxRect.x + 18f, boxRect.y + 16f, boxRect.width - 36f, 22f);
        var barRect = new Rect(boxRect.x + 18f, titleRect.yMax + 12f, boxRect.width - 36f, 18f);
        var statusRect = new Rect(boxRect.x + 18f, barRect.yMax + 10f, boxRect.width - 36f, 32f);

        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.68f));
        EditorGUI.DrawRect(boxRect, new Color(0.13f, 0.13f, 0.13f, 0.96f));
        DrawVisibilityElementOutline(boxRect, new Color(1f, 0.48f, 0.08f, 0.95f));

        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        var statusStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            wordWrap = true,
            normal = { textColor = new Color(0.86f, 0.86f, 0.86f, 1f) }
        };

        GUI.Label(titleRect, string.IsNullOrWhiteSpace(title) ? "ALLIN" : title, titleStyle);
        EditorGUI.ProgressBar(barRect, Mathf.Clamp01(progress), $"{Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f)}%");
        GUI.Label(statusRect, string.IsNullOrWhiteSpace(status) ? "Working..." : status, statusStyle);
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Arrow);
        Repaint();
    }

    private static GUIStyle GetOrangeToolbarButtonStyle()
    {
        if (orangeToolbarButtonStyle != null)
            return orangeToolbarButtonStyle;

        orangeToolbarButtonNormalTexture = CreateSolidToolbarTexture(new Color(1f, 0.48f, 0.08f, 1f));
        orangeToolbarButtonHoverTexture = CreateSolidToolbarTexture(new Color(1f, 0.58f, 0.16f, 1f));
        orangeToolbarButtonActiveTexture = CreateSolidToolbarTexture(new Color(0.9f, 0.34f, 0.02f, 1f));

        orangeToolbarButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
        orangeToolbarButtonStyle.fontStyle = FontStyle.Bold;
        orangeToolbarButtonStyle.normal.background = orangeToolbarButtonNormalTexture;
        orangeToolbarButtonStyle.normal.textColor = Color.white;
        orangeToolbarButtonStyle.hover.background = orangeToolbarButtonHoverTexture;
        orangeToolbarButtonStyle.hover.textColor = Color.white;
        orangeToolbarButtonStyle.active.background = orangeToolbarButtonActiveTexture;
        orangeToolbarButtonStyle.active.textColor = Color.white;
        orangeToolbarButtonStyle.focused.background = orangeToolbarButtonNormalTexture;
        orangeToolbarButtonStyle.focused.textColor = Color.white;
        return orangeToolbarButtonStyle;
    }

    private static Texture2D CreateSolidToolbarTexture(Color color)
    {
        var texture = new Texture2D(1, 1)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void DrawPreviewLoadingOverlay(Rect rect)
    {
        var labelHeight = 18f;
        var barWidth = Mathf.Min(260f, Mathf.Max(120f, rect.width - 48f));
        var barHeight = 16f;
        var blockGap = 8f;
        var blockHeight = labelHeight + 4f + barHeight;
        var blockY = rect.center.y + 22f;
        if (TryGetEmptyPreviewLogoRect(rect, out var logoRect))
            blockY = logoRect.yMax + 10f;
        blockY = Mathf.Min(blockY, rect.yMax - blockHeight - 18f);
        blockY = Mathf.Max(blockY, rect.yMin + 12f);

        var centerX = rect.center.x;
        var labelRect = new Rect(centerX - 180f, blockY, 360f, labelHeight);
        var barRect = new Rect(centerX - barWidth * 0.5f, labelRect.yMax + 4f, barWidth, barHeight);
        var backRect = new Rect(
            centerX - Mathf.Max(390f, barWidth + 30f) * 0.5f,
            labelRect.y - blockGap,
            Mathf.Max(390f, barWidth + 30f),
            blockHeight + blockGap * 2f);

        var labelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            normal = { textColor = new Color(0.86f, 0.86f, 0.86f, 1f) }
        };
        var status = layerPreviewRenderer != null && !string.IsNullOrWhiteSpace(layerPreviewRenderer.PreviewCacheRefreshStatus)
            ? layerPreviewRenderer.PreviewCacheRefreshStatus
            : "Loading preview...";
        var progress = layerPreviewRenderer != null ? layerPreviewRenderer.PreviewCacheRefreshProgress : 0f;
        EditorGUI.DrawRect(backRect, new Color(0f, 0f, 0f, 0.58f));
        GUI.Label(labelRect, status, labelStyle);
        EditorGUI.ProgressBar(barRect, Mathf.Clamp01(progress), $"{Mathf.RoundToInt(progress * 100f)}%");
    }

    private static void DrawRightPanelWidthSplitter(Rect splitterRect)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        var line = new Rect(splitterRect.center.x - 0.5f, splitterRect.y, 1f, splitterRect.height);
        EditorGUI.DrawRect(line, new Color(0.12f, 0.12f, 0.12f, 1f));
    }

    private static void DrawChecker(Rect rect, float size, Color a, Color b)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;

        EditorGUI.DrawRect(rect, a);

        var hexHeight = Mathf.Max(2f, size);
        var radius = hexHeight / Mathf.Sqrt(3f);
        var hexWidth = radius * 2f;
        var columnStep = radius * 1.5f;
        var rowStep = hexHeight;
        var columnCount = Mathf.CeilToInt(rect.width / columnStep) + 4;
        var rowCount = Mathf.CeilToInt(rect.height / rowStep) + 4;
        var startX = rect.x - hexWidth;
        var startY = rect.y - hexHeight;

        Handles.BeginGUI();
        var previousColor = Handles.color;
        Handles.color = a;
        for (var col = 0; col < columnCount; col++)
        {
            var centerX = startX + col * columnStep;
            var yOffset = (col & 1) == 0 ? 0f : rowStep * 0.5f;
            for (var row = 0; row < rowCount; row++)
                DrawFilledFlatTopHex(new Vector2(centerX, startY + row * rowStep + yOffset), radius);
        }

        Handles.color = b;
        for (var col = 0; col < columnCount; col++)
        {
            var centerX = startX + col * columnStep;
            var yOffset = (col & 1) == 0 ? 0f : rowStep * 0.5f;
            for (var row = 0; row < rowCount; row++)
                DrawFlatTopHexInnerOutline(new Vector2(centerX, startY + row * rowStep + yOffset), radius);
        }

        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private static void DrawFilledFlatTopHex(Vector2 center, float radius)
    {
        var halfRadius = radius * 0.5f;
        var halfHeight = Mathf.Sqrt(3f) * radius * 0.5f;
        Handles.DrawAAConvexPolygon(
            new Vector3(center.x + radius, center.y, 0f),
            new Vector3(center.x + halfRadius, center.y + halfHeight, 0f),
            new Vector3(center.x - halfRadius, center.y + halfHeight, 0f),
            new Vector3(center.x - radius, center.y, 0f),
            new Vector3(center.x - halfRadius, center.y - halfHeight, 0f),
            new Vector3(center.x + halfRadius, center.y - halfHeight, 0f));
    }

    private static void DrawFlatTopHexInnerOutline(Vector2 center, float radius)
    {
        var outlineRadius = Mathf.Max(1f, radius - 0.5f);
        var halfRadius = outlineRadius * 0.5f;
        var halfHeight = Mathf.Sqrt(3f) * outlineRadius * 0.5f;
        Handles.DrawAAPolyLine(
            1f,
            new Vector3(center.x + outlineRadius, center.y, 0f),
            new Vector3(center.x + halfRadius, center.y + halfHeight, 0f),
            new Vector3(center.x - halfRadius, center.y + halfHeight, 0f),
            new Vector3(center.x - outlineRadius, center.y, 0f),
            new Vector3(center.x - halfRadius, center.y - halfHeight, 0f),
            new Vector3(center.x + halfRadius, center.y - halfHeight, 0f),
            new Vector3(center.x + outlineRadius, center.y, 0f));
    }

    private static void DrawCenteredLabel(Rect rect, string text)
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 1f, 1f, 0.7f) }
        };
        GUI.Label(rect, text, style);
    }

    private static void DrawEmptyPreviewLogo(Rect rect)
    {
        var logo = GetEmptyPreviewLogo();
        if (logo == null || !TryGetEmptyPreviewLogoRect(rect, out var logoRect))
        {
            DrawCenteredLabel(rect, "Preview Area");
            return;
        }

        GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit, true);
    }

    private static bool TryGetEmptyPreviewLogoRect(Rect rect, out Rect logoRect)
    {
        logoRect = default;
        var logo = GetEmptyPreviewLogo();
        if (logo == null)
            return false;

        var maxWidth = rect.width * 0.78f;
        var maxHeight = rect.height * 0.42f;
        var aspect = logo.width > 0 && logo.height > 0 ? logo.width / (float)logo.height : 1f;
        var width = maxWidth;
        var height = width / aspect;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * aspect;
        }

        logoRect = new Rect(
            rect.center.x - width * 0.5f,
            rect.center.y - height * 0.5f,
            width,
            height);
        return logoRect.width > 0f && logoRect.height > 0f;
    }

    private static Texture2D GetEmptyPreviewLogo()
    {
        if (emptyPreviewLogo == null)
            emptyPreviewLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(EmptyPreviewLogoPath);
        return emptyPreviewLogo;
    }

    private void EnsureWeightArrays()
    {
        if (vertexWeights == null || vertexWeights.Length != 4)
            vertexWeights = new float[4];
        if (vertexWeightEnabled == null || vertexWeightEnabled.Length != 4)
            vertexWeightEnabled = new bool[4];
        if (editGeometryWeightBoneIndices == null || editGeometryWeightBoneIndices.Length != 4)
            editGeometryWeightBoneIndices = new int[4];
        if (editGeometryWeightValues == null || editGeometryWeightValues.Length != 4)
            editGeometryWeightValues = new float[4];
    }

    private void EnsureVisibilityMode()
    {
        if (visibilityBone == visibilitySprite)
        {
            visibilityBone = true;
            visibilitySprite = false;
        }
    }
}
#endif
