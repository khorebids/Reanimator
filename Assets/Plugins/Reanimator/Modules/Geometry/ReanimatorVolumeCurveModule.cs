#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed partial class ReanimatorVolumeCurveModule : ReanimatorModuleBase, IReanimatorPreviewModule
{
    private static readonly string[] PointTypeLabels = Enum.GetNames(typeof(PsdLayerPreviewRenderer.VolumeCurvePointType));
    private Vector2 curveListScroll;
    private readonly HashSet<string> expandedCurveIds = new HashSet<string>(StringComparer.Ordinal);
    private ReanimatorPreviewContext lastPreviewContext;
    private int hoveredCurveIndex = -1;
    private int hoveredPointIndex = -1;
    private bool isDraggingPoint;
    private int draggedPointIndex = -1;
    private int controlId;

    public override string Id => "geometry.volume-curve";
    public override string Section => "Geometry";
    public override int SectionOrder => 30;
    public override int ButtonOrder => 60;
    public override int RowGroup => 30;
    public override string Label(Reanimator.SystemApi api) => "Volume";
    public override bool IsActive(Reanimator.SystemApi api) => api != null && api.ActiveToolMode == ReanimatorToolMode.VolumeCurve;
    public bool AllowBoneInputAfterPreviewInput => false;
    public bool OverridesSelectedGeometryPreview => false;

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.SetToolMode(ReanimatorToolMode.VolumeCurve);
        if (api?.PreviewRenderer != null && api.PreviewRenderer.VolumeCurves.Count == 0)
            api.PreviewRenderer.CreateVolumeCurve();
    }

    public override bool DrawSettings(Reanimator.SystemApi api, float availableHeight)
    {
        var renderer = api != null ? api.PreviewRenderer : null;
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        var hoverResolvedFromList = false;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Volume Curves", EditorStyles.boldLabel);
            if (renderer == null)
            {
                EditorGUILayout.LabelField("Preview unavailable.");
                return true;
            }

            DrawCurveList(renderer, contentHeight - 44f, api, ref hoverResolvedFromList);
        }

        if (!hoverResolvedFromList && Event.current != null && Event.current.type == EventType.MouseMove)
            ClearHover(api);

        return true;
    }

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        var renderer = api != null ? api.PreviewRenderer : null;
        var e = Event.current;
        if (renderer == null || context == null || context.Coordinates == null || e == null)
            return;

        lastPreviewContext = context;

        if ((e.type == EventType.MouseMove || e.type == EventType.Repaint) && context.LocalRect.Contains(e.mousePosition))
        {
            var hoveredPoint = FindAnyCurvePointAt(renderer, context.Coordinates, e.mousePosition, 9f, out var pointCurveIndex);
            if (hoveredPoint >= 0)
                SetHover(pointCurveIndex, hoveredPoint, api);
            else
                SetHover(FindCurveAt(renderer, context.Coordinates, e.mousePosition, 8f), -1, api);
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
        {
            if (renderer.DeleteSelectedVolumeCurvePoint())
            {
                e.Use();
                api.Repaint();
            }

            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && context.LocalRect.Contains(e.mousePosition))
        {
            if (renderer.VolumeCurves.Count == 0)
                renderer.CreateVolumeCurve();

            var hitPoint = FindAnyCurvePointAt(renderer, context.Coordinates, e.mousePosition, 9f, out var hitCurveIndex);
            if (hitPoint >= 0)
            {
                renderer.SelectVolumeCurve(hitCurveIndex);
                renderer.SelectVolumeCurvePoint(hitPoint);
                BeginPointDrag(renderer, hitPoint);
            }
            else if (e.clickCount >= 2 &&
                     TryFindCurveSegmentAt(renderer, context.Coordinates, e.mousePosition, 8f, out var segmentCurveIndex, out var segmentIndex, out var insertPositionDoc))
            {
                renderer.InsertVolumeCurvePointOnSegment(segmentCurveIndex, segmentIndex, insertPositionDoc);
                BeginPointDrag(renderer, renderer.SelectedVolumeCurvePointIndex);
            }
            else
            {
                renderer.AddVolumeCurvePoint(context.Coordinates.ViewToDocument(e.mousePosition));
                BeginPointDrag(renderer, renderer.SelectedVolumeCurvePointIndex);
            }

            e.Use();
            api.Repaint();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0 && isDraggingPoint && GUIUtility.hotControl == controlId)
        {
            renderer.SetVolumeCurvePointPosition(draggedPointIndex, context.Coordinates.ViewToDocument(e.mousePosition));
            e.Use();
            api.Repaint();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0 && isDraggingPoint)
        {
            EndPointDrag(renderer);
            e.Use();
            api.Repaint();
        }
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        var renderer = api != null ? api.PreviewRenderer : null;
        if (renderer == null || context == null || context.Coordinates == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        lastPreviewContext = context;

        var curves = renderer.VolumeCurves;
        if (curves == null || curves.Count == 0)
            return;

        Handles.BeginGUI();
        var previousColor = Handles.color;
        for (var curveIndex = 0; curveIndex < curves.Count; curveIndex++)
        {
            var curve = curves[curveIndex];
            if (curve == null || curve.points == null || curve.points.Count == 0)
                continue;

            var selected = curveIndex == renderer.SelectedVolumeCurveIndex;
            var hovered = curveIndex == hoveredCurveIndex;
            var alpha = hovered ? 1f : selected ? 0.92f : 0.28f;
            var color = hovered ? new Color(1f, 0.82f, 0.1f, alpha) : selected ? new Color(1f, 0.28f, 0.16f, alpha) : new Color(0.55f, 0.65f, 0.8f, alpha);
            if (selected)
                DrawCurveInfluenceBand(context.Coordinates, curve, new Color(1f, 0.28f, 0.16f, 0.18f));
            DrawCurveLines(context.Coordinates, curve, color, hovered ? 3.5f : 2f);
            DrawCurvePoints(
                context.Coordinates,
                curve,
                selected ? renderer.SelectedVolumeCurvePointIndex : -1,
                hovered ? hoveredPointIndex : -1,
                alpha);
        }

        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private void DrawCurveList(PsdLayerPreviewRenderer renderer, float height, Reanimator.SystemApi api, ref bool hoverResolvedFromList)
    {
        var curves = renderer.VolumeCurves;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("New", GUILayout.Height(22f)))
            {
                renderer.CreateVolumeCurve();
                api?.Repaint();
            }

            if (GUILayout.Button("Save", GUILayout.Height(22f)))
                SavePresetAsset(renderer, api);

            if (GUILayout.Button("Load", GUILayout.Height(22f)))
                LoadPresetAsset(renderer, api);

            using (new EditorGUI.DisabledScope(renderer.SelectedVolumeCurveIndex < 0))
            {
                if (GUILayout.Button("Delete", GUILayout.Height(22f)))
                {
                    renderer.DeleteSelectedVolumeCurve();
                    api?.Repaint();
                }
            }
        }

        using (var scrollScope = new EditorGUILayout.ScrollViewScope(curveListScroll, GUILayout.Height(height)))
        {
            curveListScroll = scrollScope.scrollPosition;
            if (curves.Count == 0)
            {
                EditorGUILayout.LabelField("No curves.");
                return;
            }

            for (var i = 0; i < curves.Count; i++)
            {
                var curve = curves[i];
                DrawCurveTreeRow(renderer, curve, i, api, ref hoverResolvedFromList);
            }
        }
    }

    private void DrawCurveTreeRow(
        PsdLayerPreviewRenderer renderer,
        PsdLayerPreviewRenderer.VolumeCurve curve,
        int curveIndex,
        Reanimator.SystemApi api,
        ref bool hoverResolvedFromList)
    {
        if (curve == null)
            return;

        var curveId = string.IsNullOrWhiteSpace(curve.id) ? curveIndex.ToString() : curve.id;
        if (!expandedCurveIds.Contains(curveId) && curveIndex == renderer.SelectedVolumeCurveIndex)
            expandedCurveIds.Add(curveId);

        var pointCount = curve.points != null ? curve.points.Count : 0;
        var label = string.IsNullOrWhiteSpace(curve.name) ? $"Curve {curveIndex + 1}" : curve.name;
        var rowRect = EditorGUILayout.GetControlRect(false, 24f);
        var selected = curveIndex == renderer.SelectedVolumeCurveIndex;
        var hovered = curveIndex == hoveredCurveIndex && hoveredPointIndex < 0;
        DrawRowBackground(rowRect, selected, hovered);

        var foldoutRect = new Rect(rowRect.x + 2f, rowRect.y + 2f, 18f, rowRect.height - 4f);
        var labelRect = new Rect(rowRect.x + 22f, rowRect.y + 2f, rowRect.width - 112f, rowRect.height - 4f);
        var closedRect = new Rect(rowRect.xMax - 86f, rowRect.y + 2f, 48f, rowRect.height - 4f);
        var countRect = new Rect(rowRect.xMax - 34f, rowRect.y + 2f, 32f, rowRect.height - 4f);

        HandleCurveRowEvents(rowRect, renderer, curve, curveIndex, api, ref hoverResolvedFromList);

        var expanded = expandedCurveIds.Contains(curveId);
        var nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
        if (nextExpanded != expanded)
        {
            if (nextExpanded)
                expandedCurveIds.Add(curveId);
            else
                expandedCurveIds.Remove(curveId);
        }

        GUI.Label(labelRect, $"{label} ({pointCount})", EditorStyles.label);

        EditorGUI.BeginChangeCheck();
        var closed = GUI.Toggle(closedRect, curve.closed, "Closed");
        if (EditorGUI.EndChangeCheck())
        {
            renderer.SelectVolumeCurve(curveIndex);
            renderer.SetSelectedVolumeCurveClosed(closed);
            api?.Repaint();
        }

        GUI.Label(countRect, pointCount.ToString(), EditorStyles.miniLabel);

        if (!expandedCurveIds.Contains(curveId))
            return;

        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();
        var nextName = EditorGUILayout.TextField("Name", curve.name);
        if (EditorGUI.EndChangeCheck())
        {
            renderer.SelectVolumeCurve(curveIndex);
            renderer.RenameSelectedVolumeCurve(nextName);
            api?.Repaint();
        }

        DrawCurveInfluenceSettings(renderer, curve, curveIndex, api);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Point", GUILayout.Height(20f)))
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.AddVolumeCurvePoint(ReanimatorVolumeCurveAlgorithms.BuildDefaultPointPosition(curve));
                api?.Repaint();
            }
        }

        using (new EditorGUI.DisabledScope(!selected || pointCount < 2))
        {
            if (GUILayout.Button("Apply Volume Weights", GUILayout.Height(22f)))
            {
                if (renderer.ApplySelectedVolumeCurve(out var error))
                    api?.ShowNotification(error);
                else
                    api?.ShowNotification(string.IsNullOrEmpty(error) ? "Volume apply failed" : error);
                api?.Repaint();
            }
        }

        if (curve.points == null || curve.points.Count == 0)
        {
            EditorGUILayout.LabelField("Click in preview or press Add Point.");
        }
        else
        {
            for (var i = 0; i < curve.points.Count; i++)
                DrawPointRow(renderer, curve.points[i], curveIndex, i, api, ref hoverResolvedFromList);
        }

        EditorGUI.indentLevel--;
    }

    private static void DrawCurveInfluenceSettings(
        PsdLayerPreviewRenderer renderer,
        PsdLayerPreviewRenderer.VolumeCurve curve,
        int curveIndex,
        Reanimator.SystemApi api)
    {
        if (renderer == null || curve == null)
            return;

        EditorGUI.BeginChangeCheck();
        var radius = Mathf.Max(0.01f, EditorGUILayout.FloatField("Radius", curve.radius));
        if (EditorGUI.EndChangeCheck())
        {
            renderer.SelectVolumeCurve(curveIndex);
            renderer.SetSelectedVolumeCurveRadius(radius);
            api?.Repaint();
        }

        EditorGUI.BeginChangeCheck();
        var strength = EditorGUILayout.Slider("Strength", curve.strength, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            renderer.SelectVolumeCurve(curveIndex);
            renderer.SetSelectedVolumeCurveStrength(strength);
            api?.Repaint();
        }

        var bones = renderer.Bones;
        var currentTarget = curve.targetRendererBoneIndex;
        var labels = BuildBonePopupLabels(bones, out var indices);
        var selectedPopupIndex = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            if (indices[i] == currentTarget)
            {
                selectedPopupIndex = i;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        selectedPopupIndex = EditorGUILayout.Popup("Target Bone", selectedPopupIndex, labels);
        if (EditorGUI.EndChangeCheck())
        {
            renderer.SelectVolumeCurve(curveIndex);
            renderer.SetSelectedVolumeCurveTargetRendererBone(indices[selectedPopupIndex]);
            api?.Repaint();
        }

        using (new EditorGUI.DisabledScope(renderer.SelectedBoneIndex < 0))
        {
            if (GUILayout.Button("Use Selected Bone", GUILayout.Height(20f)))
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.SetSelectedVolumeCurveTargetRendererBone(renderer.SelectedBoneIndex);
                api?.Repaint();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Soft", GUILayout.Height(20f)))
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.ApplySelectedVolumeCurvePreset(PsdLayerPreviewRenderer.VolumeCurveFalloffPreset.Soft);
                api?.Repaint();
            }

            if (GUILayout.Button("Linear", GUILayout.Height(20f)))
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.ApplySelectedVolumeCurvePreset(PsdLayerPreviewRenderer.VolumeCurveFalloffPreset.Linear);
                api?.Repaint();
            }

            if (GUILayout.Button("Hard", GUILayout.Height(20f)))
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.ApplySelectedVolumeCurvePreset(PsdLayerPreviewRenderer.VolumeCurveFalloffPreset.Hard);
                api?.Repaint();
            }
        }

        EditorGUI.BeginChangeCheck();
        var falloff = EditorGUILayout.CurveField("Falloff", curve.falloff ?? AnimationCurve.Linear(0f, 0f, 1f, 0.25f));
        if (EditorGUI.EndChangeCheck())
        {
            renderer.SelectVolumeCurve(curveIndex);
            renderer.SetSelectedVolumeCurveFalloff(falloff);
            api?.Repaint();
        }
    }

    private static string[] BuildBonePopupLabels(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, out int[] indices)
    {
        var count = bones != null ? bones.Count : 0;
        var labels = new string[count + 1];
        indices = new int[count + 1];
        labels[0] = "None";
        indices[0] = -1;
        for (var i = 0; i < count; i++)
        {
            var bone = bones[i];
            indices[i + 1] = i;
            labels[i + 1] = bone != null && !string.IsNullOrWhiteSpace(bone.name)
                ? $"{i}: {bone.name}"
                : $"{i}: Bone";
        }

        return labels;
    }

    private void DrawPointRow(
        PsdLayerPreviewRenderer renderer,
        PsdLayerPreviewRenderer.VolumeCurvePoint point,
        int curveIndex,
        int pointIndex,
        Reanimator.SystemApi api,
        ref bool hoverResolvedFromList)
    {
        if (point == null)
            return;

        var selected = curveIndex == renderer.SelectedVolumeCurveIndex && pointIndex == renderer.SelectedVolumeCurvePointIndex;
        var hovered = curveIndex == hoveredCurveIndex && pointIndex == hoveredPointIndex;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var rowRect = EditorGUILayout.GetControlRect(false, 22f);
            DrawRowBackground(rowRect, selected, hovered);

            var deleteRect = new Rect(rowRect.xMax - 20f, rowRect.y + 2f, 18f, rowRect.height - 4f);
            var labelRect = new Rect(rowRect.x + 2f, rowRect.y + 1f, 82f, rowRect.height - 2f);
            var popupWidth = Mathf.Max(72f, (rowRect.width - labelRect.width - deleteRect.width - 18f) * 0.5f);
            var inRect = new Rect(labelRect.xMax + 4f, rowRect.y + 1f, popupWidth, rowRect.height - 2f);
            var outRect = new Rect(inRect.xMax + 4f, rowRect.y + 1f, Mathf.Max(72f, deleteRect.x - inRect.xMax - 4f), rowRect.height - 2f);

            GUI.Label(labelRect, $"Point {pointIndex + 1}", selected ? EditorStyles.toolbarButton : EditorStyles.miniButton);
            HandlePointRowEvents(labelRect, renderer, point, curveIndex, pointIndex, api, ref hoverResolvedFromList);

            if (GUI.Button(deleteRect, "x", EditorStyles.miniButton))
            {
                renderer.DeleteVolumeCurvePoint(curveIndex, pointIndex);
                api?.Repaint();
                return;
            }

            var inTypeIndex = Mathf.Clamp((int)point.inType, 0, PointTypeLabels.Length - 1);
            var outTypeIndex = Mathf.Clamp((int)point.outType, 0, PointTypeLabels.Length - 1);
            EditorGUI.BeginChangeCheck();
            inTypeIndex = EditorGUI.Popup(inRect, inTypeIndex, PointTypeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.SelectVolumeCurvePoint(pointIndex);
                if (renderer.SetSelectedVolumeCurvePointInType((PsdLayerPreviewRenderer.VolumeCurvePointType)inTypeIndex))
                    api?.Repaint();
            }

            EditorGUI.BeginChangeCheck();
            outTypeIndex = EditorGUI.Popup(outRect, outTypeIndex, PointTypeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.SelectVolumeCurvePoint(pointIndex);
                if (renderer.SetSelectedVolumeCurvePointOutType((PsdLayerPreviewRenderer.VolumeCurvePointType)outTypeIndex))
                    api?.Repaint();
            }

            EditorGUI.BeginChangeCheck();
            var nextPosition = EditorGUILayout.Vector2Field("Position", point.positionDoc);
            if (EditorGUI.EndChangeCheck())
            {
                renderer.SelectVolumeCurve(curveIndex);
                renderer.SetVolumeCurvePointPosition(pointIndex, nextPosition);
                api?.Repaint();
            }
        }
    }

    private void HandleCurveRowEvents(
        Rect rowRect,
        PsdLayerPreviewRenderer renderer,
        PsdLayerPreviewRenderer.VolumeCurve curve,
        int curveIndex,
        Reanimator.SystemApi api,
        ref bool hoverResolvedFromList)
    {
        var e = Event.current;
        if (e == null || !rowRect.Contains(e.mousePosition))
            return;

        hoverResolvedFromList = true;
        SetHover(curveIndex, -1, api);
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            renderer.SelectVolumeCurve(curveIndex);
            if (e.clickCount >= 2)
            {
                CenterOnCurve(api, curve);
                e.Use();
            }
        }
    }

    private void HandlePointRowEvents(
        Rect rowRect,
        PsdLayerPreviewRenderer renderer,
        PsdLayerPreviewRenderer.VolumeCurvePoint point,
        int curveIndex,
        int pointIndex,
        Reanimator.SystemApi api,
        ref bool hoverResolvedFromList)
    {
        var e = Event.current;
        if (e == null || !rowRect.Contains(e.mousePosition))
            return;

        hoverResolvedFromList = true;
        SetHover(curveIndex, pointIndex, api);
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            renderer.SelectVolumeCurve(curveIndex);
            renderer.SelectVolumeCurvePoint(pointIndex);
            if (e.clickCount >= 2)
            {
                api?.TryCenterPreviewOnDocumentPoint(lastPreviewContext, point.positionDoc);
                e.Use();
            }
        }
    }

    private static void DrawRowBackground(Rect rect, bool selected, bool hovered)
    {
        if (!selected && !hovered)
            return;

        var color = hovered
            ? new Color(1f, 0.82f, 0.1f, selected ? 0.26f : 0.16f)
            : new Color(0.35f, 0.48f, 0.72f, 0.18f);
        EditorGUI.DrawRect(rect, color);
    }

    private void SetHover(int curveIndex, int pointIndex, Reanimator.SystemApi api)
    {
        if (hoveredCurveIndex == curveIndex && hoveredPointIndex == pointIndex)
            return;

        hoveredCurveIndex = curveIndex;
        hoveredPointIndex = pointIndex;
        api?.Repaint();
    }

    private void ClearHover(Reanimator.SystemApi api)
    {
        SetHover(-1, -1, api);
    }

    private void BeginPointDrag(PsdLayerPreviewRenderer renderer, int pointIndex)
    {
        draggedPointIndex = pointIndex;
        controlId = GUIUtility.GetControlID(FocusType.Passive);
        GUIUtility.hotControl = controlId;
        isDraggingPoint = true;
        renderer?.BeginVolumeCurvePointDrag(controlId);
    }

    private void EndPointDrag(PsdLayerPreviewRenderer renderer)
    {
        if (GUIUtility.hotControl == controlId && controlId != 0)
            GUIUtility.hotControl = 0;

        renderer?.EndVolumeCurvePointDrag();
        isDraggingPoint = false;
        draggedPointIndex = -1;
        controlId = 0;
    }

    private static void SavePresetAsset(PsdLayerPreviewRenderer renderer, Reanimator.SystemApi api)
    {
        if (renderer == null)
            return;

        var path = EditorUtility.SaveFilePanelInProject(
            "Save Volume Curve Preset",
            "Reanimator Volume Preset",
            "asset",
            "Save Volume curves to a reusable preset asset.");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var preset = AssetDatabase.LoadAssetAtPath<ReanimatorVolumeCurvePresetAsset>(path);
        if (preset == null)
        {
            preset = ScriptableObject.CreateInstance<ReanimatorVolumeCurvePresetAsset>();
            preset.curves = renderer.CloneVolumeCurvesForPreset();
            AssetDatabase.CreateAsset(preset, path);
        }
        else
        {
            Undo.RegisterCompleteObjectUndo(preset, "Save Volume Curve Preset");
            preset.curves = renderer.CloneVolumeCurvesForPreset();
            EditorUtility.SetDirty(preset);
        }

        AssetDatabase.SaveAssetIfDirty(preset);
        api?.ShowNotification($"Saved Volume preset: {System.IO.Path.GetFileNameWithoutExtension(path)}");
        api?.Repaint();
    }

    private static void LoadPresetAsset(PsdLayerPreviewRenderer renderer, Reanimator.SystemApi api)
    {
        if (renderer == null)
            return;

        var absolutePath = EditorUtility.OpenFilePanel(
            "Load Volume Curve Preset",
            Application.dataPath,
            "asset");
        if (string.IsNullOrWhiteSpace(absolutePath))
            return;

        var projectPath = Application.dataPath.Replace('\\', '/');
        absolutePath = absolutePath.Replace('\\', '/');
        if (!absolutePath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
        {
            api?.ShowNotification("Preset must be inside this Unity project.");
            return;
        }

        var assetPath = "Assets" + absolutePath.Substring(projectPath.Length);
        var preset = AssetDatabase.LoadAssetAtPath<ReanimatorVolumeCurvePresetAsset>(assetPath);
        if (preset == null)
        {
            api?.ShowNotification("Selected asset is not a Volume preset.");
            return;
        }

        renderer.ReplaceVolumeCurves(preset.curves);
        api?.ShowNotification($"Loaded Volume preset: {preset.name}");
        api?.Repaint();
    }

    private static int FindCurvePointAt(PsdLayerPreviewRenderer renderer, PsdCoordinateSpace coordinates, Vector2 mouseCanvas, float radius)
    {
        if (!renderer.TryGetSelectedVolumeCurve(out var curve) || curve.points == null)
            return -1;

        return ReanimatorVolumeCurveAlgorithms.FindCurvePointAt(curve, coordinates.DocumentToView, mouseCanvas, radius);
    }

    private static int FindAnyCurvePointAt(
        PsdLayerPreviewRenderer renderer,
        PsdCoordinateSpace coordinates,
        Vector2 mouseCanvas,
        float radius,
        out int curveIndex)
    {
        curveIndex = -1;
        var curves = renderer != null ? renderer.VolumeCurves : null;
        if (curves == null || coordinates == null)
            return -1;

        return ReanimatorVolumeCurveAlgorithms.FindAnyCurvePointAt(curves, coordinates.DocumentToView, mouseCanvas, radius, out curveIndex);
    }

    private static int FindCurveAt(PsdLayerPreviewRenderer renderer, PsdCoordinateSpace coordinates, Vector2 mouseCanvas, float maxDistance)
    {
        var curves = renderer != null ? renderer.VolumeCurves : null;
        if (curves == null || coordinates == null)
            return -1;

        return ReanimatorVolumeCurveAlgorithms.FindCurveAt(curves, coordinates.DocumentToView, mouseCanvas, maxDistance);
    }

    private static bool TryFindCurveSegmentAt(
        PsdLayerPreviewRenderer renderer,
        PsdCoordinateSpace coordinates,
        Vector2 mouseCanvas,
        float maxDistance,
        out int curveIndex,
        out int segmentIndex,
        out Vector2 nearestDocumentPoint)
    {
        var curves = renderer != null ? renderer.VolumeCurves : null;
        if (curves == null || coordinates == null)
        {
            curveIndex = -1;
            segmentIndex = -1;
            nearestDocumentPoint = Vector2.zero;
            return false;
        }

        return ReanimatorVolumeCurveAlgorithms.TryFindCurveSegmentAt(
            curves,
            coordinates.DocumentToView,
            mouseCanvas,
            maxDistance,
            out curveIndex,
            out segmentIndex,
            out nearestDocumentPoint);
    }

    private static void DrawCurveLines(PsdCoordinateSpace coordinates, PsdLayerPreviewRenderer.VolumeCurve curve, Color color, float width)
    {
        if (coordinates == null || curve == null || curve.points == null || curve.points.Count < 2)
            return;

        Handles.color = color;
        var points = ReanimatorVolumeCurveAlgorithms.BuildDrawPoints(curve, coordinates.DocumentToView);
        if (points.Count >= 2)
            Handles.DrawAAPolyLine(Mathf.Max(1f, width), points.ToArray());
    }

    private static void DrawCurveInfluenceBand(PsdCoordinateSpace coordinates, PsdLayerPreviewRenderer.VolumeCurve curve, Color color)
    {
        if (coordinates == null || curve == null || curve.points == null || curve.points.Count < 2)
            return;

        var radius = Mathf.Max(0.01f, curve.radius);
        var points = ReanimatorVolumeCurveAlgorithms.BuildDocumentDrawPoints(curve);
        if (points.Count < 2)
            return;

        var canvasPoints = new Vector3[points.Count];
        var width = 2f;
        for (var i = 0; i < points.Count; i++)
        {
            var canvas = coordinates.DocumentToView(points[i]);
            canvasPoints[i] = new Vector3(canvas.x, canvas.y, 0f);

            var canvasRadius = Vector2.Distance(canvas, coordinates.DocumentToView(points[i] + new Vector2(radius, 0f)));
            if (canvasRadius > width)
                width = canvasRadius;
        }

        Handles.color = color;
        Handles.DrawAAPolyLine(Mathf.Max(2f, width * 2f), canvasPoints);
    }

    private static void DrawCurvePoints(
        PsdCoordinateSpace coordinates,
        PsdLayerPreviewRenderer.VolumeCurve curve,
        int selectedPointIndex,
        int hoveredPointIndex,
        float alpha)
    {
        if (coordinates == null || curve == null || curve.points == null)
            return;

        for (var i = 0; i < curve.points.Count; i++)
        {
            var point = curve.points[i];
            if (point == null)
                continue;

            var canvas = coordinates.DocumentToView(point.positionDoc);
            var selected = i == selectedPointIndex;
            var hovered = i == hoveredPointIndex;
            var size = hovered ? 9f : selected ? 8f : 6f;
            var fill = hovered ? new Color(1f, 0.9f, 0.12f, alpha) : selected ? new Color(1f, 0.9f, 0.12f, alpha) : GetPointColor(point.outType, alpha);
            EditorGUI.DrawRect(new Rect(canvas.x - size * 0.5f, canvas.y - size * 0.5f, size, size), fill);
        }
    }

    private static Color GetPointColor(PsdLayerPreviewRenderer.VolumeCurvePointType type, float alpha)
    {
        switch (type)
        {
            case PsdLayerPreviewRenderer.VolumeCurvePointType.Euler:
                return new Color(0.3f, 0.85f, 1f, alpha);
            case PsdLayerPreviewRenderer.VolumeCurvePointType.Catmull:
                return new Color(1f, 0.28f, 0.16f, alpha);
            case PsdLayerPreviewRenderer.VolumeCurvePointType.Bezier:
                return new Color(0.72f, 0.48f, 1f, alpha);
            default:
                return new Color(0.85f, 0.85f, 0.85f, alpha);
        }
    }

    private void CenterOnCurve(Reanimator.SystemApi api, PsdLayerPreviewRenderer.VolumeCurve curve)
    {
        if (api == null || !ReanimatorVolumeCurveAlgorithms.TryGetBoundsCenter(curve, out var center))
            return;

        api.TryCenterPreviewOnDocumentPoint(lastPreviewContext, center);
    }
}
#endif
