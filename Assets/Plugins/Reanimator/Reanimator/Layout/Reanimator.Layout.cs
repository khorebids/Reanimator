#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const string EmptyPreviewLogoPath = "Assets/Plugins/Reanimator/Graphics/logo.png";
    private static Texture2D emptyPreviewLogo;
    private static Texture2D loadingMascot;
    private static GUIStyle orangeToolbarButtonStyle;
    private static Texture2D orangeToolbarButtonNormalTexture;
    private static Texture2D orangeToolbarButtonHoverTexture;
    private static Texture2D orangeToolbarButtonActiveTexture;

    private void DrawTestTab()
    {
        UpdateWindowEventOptions();
        EnsureWeightArrays();
        EnsureDefaultToolMode();
        if (pendingApplyPath != null || (!reanimateRunning && TryGetExternalBlockingProgress(out _, out _, out _)))
            HandleExternalBlockingProgressInput();

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Toggle(previewMode == PreviewMode.Reanimate, "Reanimate", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                SetPreviewMode(PreviewMode.Reanimate);
            if (GUILayout.Toggle(previewMode == PreviewMode.Skel, "Skinning Editor", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                SetPreviewMode(PreviewMode.Skel);
            if (GUILayout.Toggle(previewMode == PreviewMode.Sprite, "Sprite Sheet", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                SetPreviewMode(PreviewMode.Sprite);
            if (GUILayout.Toggle(previewMode == PreviewMode.Comfimator, "Comfimator", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                SetPreviewMode(PreviewMode.Comfimator);
            if (GUILayout.Toggle(previewMode == PreviewMode.AllInGraph, "ALLIN Graph", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                SetPreviewMode(PreviewMode.AllInGraph);
            if (GUILayout.Toggle(previewMode == PreviewMode.SurfaceMasks, "Surface Masks", EditorStyles.toolbarButton, GUILayout.Width(105f)))
                SetPreviewMode(PreviewMode.SurfaceMasks);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(reanimateRunning || !TryValidateAllInPreflight(out _)))
            {
                if (GUILayout.Button("ALLIN", GetOrangeToolbarButtonStyle(), GUILayout.Width(55f)))
                    InvokeComfimatorButton("ALLIN");
            }
            GUILayout.Space(4f);
            GUILayout.Button("Visibility", EditorStyles.toolbarButton, GUILayout.Width(70f));
            using (new EditorGUI.DisabledScope(reanimateRunning || layerPreviewRenderer == null || !layerPreviewRenderer.HasPendingChanges))
            {
                if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                    RevertChanges();
                if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                    QueueApplyChanges();
            }
        }

        var toolbarRect = GUILayoutUtility.GetLastRect();
        if (Event.current.type == EventType.Repaint)
            cachedToolbarRect = toolbarRect;

        if (previewMode == PreviewMode.SurfaceMasks)
        {
            DrawSurfaceMasksWorkspace();
            DrawExternalBlockingProgressOverlay();
            return;
        }

        if (previewMode == PreviewMode.AllInGraph)
        {
            if (ReanimatorExternalPanelRegistry.TryGet("Comfimator", out var panel) &&
                panel is IReanimatorAllInGraphPanel graph)
                graph.DrawAllInGraph(this);
            else
                EditorGUILayout.HelpBox("Comfimator is unavailable.", MessageType.Info);
            DrawExternalBlockingProgressOverlay();
            return;
        }

        if (previewMode == PreviewMode.Reanimate)
        {
            DrawReanimateWorkspace();
            DrawExternalBlockingProgressOverlay();
            return;
        }

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
                (((bottomToolMode == ReanimatorToolMode.WeightSlider ||
                   (bottomToolMode == ReanimatorToolMode.SoftContour && layerPreviewRenderer.SelectingSoftContourPoints)) &&
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
                DrawPreviewSelectionLabel(clippedPreviewRect, vertexInfluenceVisibilityButtonRect);
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

        if (pendingApplyPath != null)
        {
            localPreviewRect.position += windowLayout.ContentRect.position;
            DrawApplyProcessingOverlay(localPreviewRect);
        }
        else
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

    private void DrawPreviewSelectionLabel(Rect previewRect, Rect lastButtonRect)
    {
        if (layerPreviewRenderer == null)
            return;

        var boneIndex = layerPreviewRenderer.SelectedBoneIndex;
        var bones = layerPreviewRenderer.Bones;
        var boneName = boneIndex >= 0 && boneIndex < bones.Count ? bones[boneIndex]?.name : null;
        var spriteName = layerPreviewRenderer.TryGetSelectedLayer(out var layer) ? layer.name : null;
        var text = $"Bone: {boneName ?? "—"}  |  Sprite: {spriteName ?? "—"}";

        var x = lastButtonRect.xMax + 4f;
        var availableWidth = previewRect.xMax - x - 4f;
        if (availableWidth <= 0f)
            return;

        var content = new GUIContent(text, text);
        var style = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            padding = new RectOffset(6, 6, 0, 0),
            normal = { textColor = Color.white }
        };
        var width = Mathf.Min(availableWidth, style.CalcSize(content).x);
        var rect = new Rect(x, lastButtonRect.y, width, lastButtonRect.height);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.65f));
        GUI.Label(rect, content, style);
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
        if (previewMode == PreviewMode.Reanimate && layer != null)
        {
            reanimateDiagnosticLayer = layer;
            Repaint();
            return true;
        }
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

    private void DrawApplyProcessingOverlay(Rect previewRect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        var center = previewRect.center;
        var box = new Rect(center.x - 92f, center.y - 19f, 184f, 38f);
        EditorGUI.DrawRect(box, new Color(0.13f, 0.13f, 0.13f, 0.96f));
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        GUI.Label(box, "Apply processing", style);

        // Give the editor time to present the repaint before the synchronous import.
        if (double.IsPositiveInfinity(pendingApplyStart))
            pendingApplyStart = EditorApplication.timeSinceStartup + 0.08d;
    }

    private void DrawExternalBlockingProgressOverlay()
    {
        if(reanimateRunning)
        {
            var now=EditorApplication.timeSinceStartup;
            // Animate only towards confirmed stage progress, never invent work
            // or let a time-based estimate creep to 99% during a stalled stage.
            reanimateDisplayedProgress=reanimateProgress>=1f?1f:Mathf.MoveTowards(
                Mathf.Min(reanimateDisplayedProgress,reanimateProgress),reanimateProgress,
                Mathf.Max(0f,(float)(now-reanimateProgressLastDraw))*.5f);
            reanimateProgressLastDraw=now;
            var cardWidth=Mathf.Min(378f,Mathf.Max(1f,position.width-16f));
            var card=new Rect(Mathf.Max(8f,position.width-cardWidth-8f),Mathf.Max(8f,position.height-90f),cardWidth,82f);
            EditorGUI.DrawRect(card,new Color(.10f,.10f,.10f,.98f));
            DrawVisibilityElementOutline(card,new Color(1f,.48f,.08f,1f));
            DrawLoadingMascot(card);
            GUI.Label(new Rect(card.x+12,card.y+14,card.width-24,20),"REANIMATOR · "+reanimateProgressTitle,EditorStyles.boldLabel);
            var bar=new Rect(card.x+12,card.y+39,card.width-24,18);
            EditorGUI.DrawRect(bar,new Color(.22f,.22f,.22f));
            EditorGUI.DrawRect(new Rect(bar.x,bar.y,bar.width*reanimateDisplayedProgress,bar.height),new Color(1f,.48f,.08f));
            GUI.Label(bar,$"{reanimateDisplayedProgress*100f:0.0}%",new GUIStyle(EditorStyles.boldLabel){alignment=TextAnchor.MiddleCenter});
            GUI.Label(new Rect(card.x+12,card.y+60,card.width-24,16),"Processing stages",EditorStyles.miniLabel);
            Repaint();
            return;
        }
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

    private static void DrawLoadingMascot(Rect card)
    {
        if(Event.current.type!=EventType.Repaint)return;
        if(!loadingMascot)loadingMascot=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/Reanimator/Graphics/loading-mascot.png");
        if(!loadingMascot)return;
        // The supplied artwork rests on row 486 of its 546px canvas; its hand
        // hangs below that row, overlapping the top edge of the card.
        const float ledge=.89011f;
        float width=Mathf.Min(card.width*1.04f,Mathf.Max(0,card.y-8f)*loadingMascot.width/(loadingMascot.height*ledge));
        float height=width*loadingMascot.height/loadingMascot.width;
        var artwork=new Rect(card.center.x-width*.5f,card.y-height*ledge,width,height);
        var tint=GUI.color;GUI.color=Color.white;
        GUI.DrawTexture(artwork,loadingMascot,ScaleMode.StretchToFill,true);
        GUI.color=tint;
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

    private static Texture2D checkerTile;
    private static Color checkerFill;
    private static Color checkerOutline;

    private static void DrawChecker(Rect rect, float size, Color a, Color b)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        if (!checkerTile || checkerFill != a || checkerOutline != b)
        {
            if (checkerTile)
                UnityEngine.Object.DestroyImmediate(checkerTile);
            checkerFill = a;
            checkerOutline = b;
            const int width = 112;
            const int height = 64;
            checkerTile = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[width * height];
            var columnStep = Mathf.Sqrt(3f) * 0.5f;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5f) / width * 2f * columnStep;
                var v = (y + 0.5f) / height;
                var edge = float.PositiveInfinity;
                for (var column = -1; column <= 3; column++)
                {
                    var dx = Mathf.Abs(u - column * columnStep);
                    var row = v - ((column & 1) == 0 ? 0f : 0.5f);
                    var dy = Mathf.Abs(row - Mathf.Round(row));
                    var distance = Mathf.Max(dy, dx * columnStep + dy * 0.5f);
                    edge = Mathf.Min(edge, Mathf.Abs(distance - 0.5f));
                }

                pixels[y * width + x] = Color.Lerp(a, b, 1f - Mathf.SmoothStep(0f, 1f, edge * 32f));
            }
            checkerTile.SetPixels32(pixels);
            checkerTile.Apply(false, true);
        }

        var heightInPoints = Mathf.Max(2f, size);
        GUI.DrawTextureWithTexCoords(rect, checkerTile,
            new Rect(0f, 0f, rect.width / (heightInPoints * Mathf.Sqrt(3f)), rect.height / heightInPoints));
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
