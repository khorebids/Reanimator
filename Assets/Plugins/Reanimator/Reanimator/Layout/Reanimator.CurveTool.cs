#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private int analizeCurveDraggedPointIndex = -1;
    private int analizeCurveDragControlId;
    private static readonly int CurveGraphResizeHint = "WeightCurveGraphResize".GetHashCode();
    private float analizeCurveGraphResizeStartHeight;
    private float analizeCurveGraphResizeStartMouseY;

    private void DrawCurveToolPanel(float availableHeight)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("WeightCurve", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click contour points one by one, or drag a rectangle to grab many at once. Selection stays on the contour, and box-select collapses to one consecutive contour segment. Drag graph points vertically to edit weights, then apply them back to the selected contour.",
                MessageType.Info);

            curveSpaceMode = Mathf.Clamp(curveSpaceMode, 0, CurveSpaceModeLabels.Length - 1);
            if (layerPreviewRenderer != null)
                layerPreviewRenderer.CurveUseAbsoluteGeometry = curveSpaceMode == 1;

            var hasSelection = layerPreviewRenderer != null && layerPreviewRenderer.HasCurveSelection();
            var graphData = default(PsdLayerPreviewRenderer.CurveGraphData);
            var hasGraph = layerPreviewRenderer != null && layerPreviewRenderer.TryGetCurveGraphData(out graphData);

            if (hasGraph)
            {
                EditorGUILayout.LabelField("Total Bones", graphData.totalBoneCount.ToString());
                EditorGUILayout.LabelField("Layer", string.IsNullOrEmpty(graphData.layerName) ? "-" : graphData.layerName);
                EditorGUILayout.LabelField("Max", graphData.parentBoneName);
                EditorGUILayout.LabelField("Min", graphData.childBoneName);
                if (graphData.totalBoneCount > 2)
                    EditorGUILayout.LabelField("Other", graphData.otherBoneName);
            }
            else if (hasSelection)
            {
                EditorGUILayout.HelpBox("Selection exists, but max/min bone influences could not be resolved from current weights.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Select consecutive contour points in preview.");
            }

            GUILayout.Space(6f);
            GUILayout.FlexibleSpace();

            DrawCurveSelectionEdgeControls(hasSelection);

            var selectedPair = 0;
            var pairOptions = hasGraph ? layerPreviewRenderer.GetCurveBonePairOptions(out selectedPair) : null;
            var showPairPicker = pairOptions != null && pairOptions.Length == 3;
            var maxGraphHeight = Mathf.Max(120f, availableHeight - (showPairPicker ? 230f : 208f));
            var graphHeight = Mathf.Clamp(analizeCurveGraphHeight, 120f, maxGraphHeight);
            var resizeHandleRect = GUILayoutUtility.GetRect(10f, 12f, GUILayout.ExpandWidth(true));
            DrawAnalizeCurveResizeHandle(resizeHandleRect);
            HandleAnalizeCurveGraphResize(resizeHandleRect, graphHeight, maxGraphHeight);

            var graphRect = GUILayoutUtility.GetRect(10f, graphHeight, GUILayout.ExpandWidth(true));
            DrawCurveGraph(graphRect);

            if (showPairPicker)
            {
                var pair = GUILayout.Toolbar(selectedPair, pairOptions, GUILayout.Height(20f));
                if (pair != selectedPair)
                {
                    ResetAnalizeCurveGraphDrag();
                    layerPreviewRenderer.SelectCurveBonePair(pair);
                    Repaint();
                }
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasGraph))
                {
                    if (GUILayout.Button("Apply", GUILayout.Height(22f)))
                    {
                        var error = string.Empty;
                        if (layerPreviewRenderer != null && layerPreviewRenderer.ApplyCurveGraph(out error))
                        {
                            ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "WeightCurve applied" : error));
                            Repaint();
                        }
                        else
                        {
                            ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Apply failed" : error));
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(!hasSelection))
                {
                    if (GUILayout.Button("Clear Points", GUILayout.Height(22f)))
                    {
                        ResetAnalizeCurveGraphDrag();
                        layerPreviewRenderer?.CancelCurveTool();
                    }
                }

                using (new EditorGUI.DisabledScope(!hasGraph))
                {
                    if (GUILayout.Button("Kill Addit", GUILayout.Height(22f)))
                    {
                        var error = string.Empty;
                        if (layerPreviewRenderer != null && layerPreviewRenderer.KillCurveAdditionalWeights(out error))
                        {
                            ResetAnalizeCurveGraphDrag();
                            ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Additional weights removed" : error));
                            Repaint();
                        }
                        else
                        {
                            ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Kill Addit failed" : error));
                        }
                    }
                }
            }
        }
    }

    private void DrawCurveSelectionEdgeControls(bool hasSelection)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Mode", GUILayout.Width(44f));
            curveSpaceMode = GUILayout.Toolbar(
                Mathf.Clamp(curveSpaceMode, 0, CurveSpaceModeLabels.Length - 1),
                CurveSpaceModeLabels,
                GUILayout.Height(20f));
        }

        using (new EditorGUI.DisabledScope(!hasSelection))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("+", "Add previous contour vertex to the left edge."), GUILayout.Width(24f), GUILayout.Height(20f)))
                AdjustCurveSelectionEdge(true, true);
            if (GUILayout.Button(new GUIContent("-", "Remove the left edge vertex from selection."), GUILayout.Width(24f), GUILayout.Height(20f)))
                AdjustCurveSelectionEdge(true, false);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(new GUIContent("+", "Add next contour vertex to the right edge."), GUILayout.Width(24f), GUILayout.Height(20f)))
                AdjustCurveSelectionEdge(false, true);
            if (GUILayout.Button(new GUIContent("-", "Remove the right edge vertex from selection."), GUILayout.Width(24f), GUILayout.Height(20f)))
                AdjustCurveSelectionEdge(false, false);
        }
    }

    private void AdjustCurveSelectionEdge(bool leftEdge, bool add)
    {
        if (layerPreviewRenderer != null && layerPreviewRenderer.TryAdjustCurveSelectionEdge(leftEdge, add))
        {
            ResetAnalizeCurveGraphDrag();
            Repaint();
        }
    }

    private static void DrawAnalizeCurveResizeHandle(Rect rect)
    {
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
        var grip = new Rect(rect.center.x - 14f, rect.center.y - 1f, 28f, 2f);
        EditorGUI.DrawRect(grip, new Color(0.65f, 0.65f, 0.65f, 1f));
        GUI.Label(rect, new GUIContent(string.Empty, "Drag up to make the WeightCurve graph taller."));
    }

    private void HandleAnalizeCurveGraphResize(Rect handleRect, float graphHeight, float maxGraphHeight)
    {
        var e = Event.current;
        var controlId = GUIUtility.GetControlID(CurveGraphResizeHint, FocusType.Passive, handleRect);
        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseDown when e.button == 0 && handleRect.Contains(e.mousePosition):
                GUIUtility.hotControl = controlId;
                analizeCurveGraphResizeStartHeight = graphHeight;
                analizeCurveGraphResizeStartMouseY = GUIUtility.GUIToScreenPoint(e.mousePosition).y;
                e.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                var mouseY = GUIUtility.GUIToScreenPoint(e.mousePosition).y;
                analizeCurveGraphHeight = Mathf.Clamp(
                    analizeCurveGraphResizeStartHeight + analizeCurveGraphResizeStartMouseY - mouseY,
                    120f, maxGraphHeight);
                e.Use();
                Repaint();
                break;
            case EventType.MouseUp when e.button == 0 && GUIUtility.hotControl == controlId:
                GUIUtility.hotControl = 0;
                e.Use();
                break;
        }
    }

    private void DrawCurveGraph(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 0.95f));
        var inner = new Rect(rect.x + 10f, rect.y + 18f, rect.width - 20f, rect.height - 28f);
        if (inner.width <= 8f || inner.height <= 8f)
            return;

        GUI.Label(new Rect(rect.x + 10f, rect.y + 2f, rect.width - 20f, 16f), "WeightCurve Graph", EditorStyles.miniBoldLabel);

        if (layerPreviewRenderer == null || !layerPreviewRenderer.TryGetCurveGraphData(out var graphData))
        {
            ResetAnalizeCurveGraphDrag();
            GUI.Label(inner, "No contour selection", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var hasOtherGraph = graphData.otherSamples != null && graphData.otherSamples.Length > 0 && graphData.totalBoneCount > 2;
        var mainRect = hasOtherGraph
            ? new Rect(inner.x, inner.y, inner.width, Mathf.Max(40f, inner.height * 0.68f))
            : inner;
        var otherRect = hasOtherGraph
            ? new Rect(inner.x, mainRect.yMax + 6f, inner.width, Mathf.Max(28f, inner.yMax - mainRect.yMax - 6f))
            : new Rect();

        DrawCurveGraphFrame(mainRect);
        if (hasOtherGraph)
            DrawCurveGraphFrame(otherRect);

        if (HandleCurveGraphInput(mainRect, graphData))
            layerPreviewRenderer.TryGetCurveGraphData(out graphData);

        DrawCurveBand(mainRect, graphData.parentSamples, graphData.parentColor);
        DrawCurveBand(mainRect, graphData.childSamples, graphData.childColor);
        DrawCurveHandles(mainRect, graphData.childSamples, graphData.childColor);
        if (hasOtherGraph)
            DrawCurveBand(otherRect, graphData.otherSamples, graphData.otherColor);

        var oldColor = GUI.color;
        GUI.color = graphData.childColor;
        GUI.Label(new Rect(mainRect.x + 6f, mainRect.y + 4f, mainRect.width - 12f, 16f), "Min: " + graphData.childBoneName, EditorStyles.miniBoldLabel);
        GUI.color = graphData.parentColor;
        GUI.Label(new Rect(mainRect.x + 6f, mainRect.yMax - 20f, mainRect.width - 12f, 16f), "Max: " + graphData.parentBoneName, EditorStyles.miniBoldLabel);
        if (hasOtherGraph)
        {
            GUI.color = graphData.otherColor;
            GUI.Label(new Rect(otherRect.x + 6f, otherRect.y + 4f, otherRect.width - 12f, 16f), "Other: " + graphData.otherBoneName, EditorStyles.miniBoldLabel);
        }
        GUI.color = oldColor;
    }

    private static void DrawCurveGraphFrame(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.14f, 1f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.center.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.12f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.08f));
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.08f));
    }

    private bool HandleCurveGraphInput(Rect rect, PsdLayerPreviewRenderer.CurveGraphData graphData)
    {
        var e = Event.current;
        if (e == null || layerPreviewRenderer == null || e.alt || e.button != 0)
            return false;

        var points = BuildCurveGraphPoints(rect, graphData.childSamples);
        if (e.type == EventType.MouseDown)
        {
            if (e.clickCount == 2 && TryFindCurveGraphInsertSegment(points, e.mousePosition, out var segmentIndex, out var segmentT))
            {
                if (layerPreviewRenderer.TryInsertCurveContourVertex(segmentIndex, segmentT))
                {
                    EditorUtility.SetDirty(this);
                    ResetAnalizeCurveGraphDrag();
                    e.Use();
                    Repaint();
                    return true;
                }
            }

            var hitIndex = FindCurveGraphPointAt(points, e.mousePosition, 8f);
            if (hitIndex < 0)
                return false;

            analizeCurveDraggedPointIndex = hitIndex;
            analizeCurveDragControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = analizeCurveDragControlId;
            layerPreviewRenderer.BeginCurveGraphPointDrag();
            var changed = UpdateCurveGraphPointFromMouse(rect, hitIndex, e.mousePosition.y);
            e.Use();
            if (changed)
                Repaint();
            return changed;
        }

        if (GUIUtility.hotControl != analizeCurveDragControlId || analizeCurveDraggedPointIndex < 0)
            return false;

        if (e.type == EventType.MouseDrag)
        {
            var changed = UpdateCurveGraphPointFromMouse(rect, analizeCurveDraggedPointIndex, e.mousePosition.y);
            e.Use();
            if (changed)
                Repaint();
            return changed;
        }

        if (e.type == EventType.MouseUp)
        {
            var changed = UpdateCurveGraphPointFromMouse(rect, analizeCurveDraggedPointIndex, e.mousePosition.y);
            ResetAnalizeCurveGraphDrag();
            e.Use();
            if (changed)
                Repaint();
            return changed;
        }

        return false;
    }

    private bool UpdateCurveGraphPointFromMouse(Rect rect, int sampleIndex, float mouseY)
    {
        if (layerPreviewRenderer == null)
            return false;

        var clampedY = Mathf.Clamp(mouseY, rect.y + 6f, rect.yMax - 6f);
        var childWeight = Mathf.InverseLerp(rect.yMax - 6f, rect.y + 6f, clampedY);
        return layerPreviewRenderer.TrySetCurveGraphPointValue(sampleIndex, childWeight);
    }

    private void ResetAnalizeCurveGraphDrag()
    {
        if (GUIUtility.hotControl == analizeCurveDragControlId)
            GUIUtility.hotControl = 0;
        layerPreviewRenderer?.EndCurveGraphPointDrag();
        analizeCurveDraggedPointIndex = -1;
        analizeCurveDragControlId = 0;
    }

    private static Vector2[] BuildCurveGraphPoints(Rect rect, Vector2[] samples)
    {
        if (samples == null || samples.Length == 0)
            return new Vector2[0];

        var points = new Vector2[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var x = Mathf.Lerp(rect.x + 6f, rect.xMax - 6f, Mathf.Clamp01(samples[i].x));
            var y = Mathf.Lerp(rect.yMax - 6f, rect.y + 6f, Mathf.Clamp01(samples[i].y));
            points[i] = new Vector2(x, y);
        }

        return points;
    }

    private static int FindCurveGraphPointAt(Vector2[] points, Vector2 mousePosition, float radius)
    {
        if (points == null || points.Length == 0)
            return -1;

        var bestIndex = -1;
        var bestDistanceSqr = radius * radius;
        for (var i = 0; i < points.Length; i++)
        {
            var distanceSqr = (points[i] - mousePosition).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static bool TryFindCurveGraphInsertSegment(Vector2[] points, Vector2 mousePosition, out int segmentIndex, out float segmentT)
    {
        segmentIndex = -1;
        segmentT = 0f;
        if (points == null || points.Length < 2)
            return false;

        const float maxDistance = 10f;
        var bestDistanceSqr = maxDistance * maxDistance;
        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var ab = b - a;
            var lengthSqr = ab.sqrMagnitude;
            if (lengthSqr <= 0.0001f)
                continue;

            var t = Mathf.Clamp01(Vector2.Dot(mousePosition - a, ab) / lengthSqr);
            if (t <= 0.001f || t >= 0.999f)
                continue;

            var closest = a + ab * t;
            var distanceSqr = (mousePosition - closest).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            segmentIndex = i;
            segmentT = t;
        }

        return segmentIndex >= 0;
    }

    private static void DrawCurveBand(Rect rect, Vector2[] samples, Color color)
    {
        if (samples == null || samples.Length == 0)
            return;

        var points = new Vector3[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var x = Mathf.Lerp(rect.x + 6f, rect.xMax - 6f, Mathf.Clamp01(samples[i].x));
            var y = Mathf.Lerp(rect.yMax - 6f, rect.y + 6f, Mathf.Clamp01(samples[i].y));
            points[i] = new Vector3(x, y, 0f);
        }

        Handles.BeginGUI();
        var previous = Handles.color;
        Handles.color = color;
        if (points.Length >= 2)
            Handles.DrawAAPolyLine(2.5f, points);
        Handles.color = previous;
        Handles.EndGUI();
    }

    private void DrawCurveHandles(Rect rect, Vector2[] samples, Color color)
    {
        var points = BuildCurveGraphPoints(rect, samples);
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var size = i == analizeCurveDraggedPointIndex ? 7f : 5f;
            EditorGUI.DrawRect(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), color);
        }
    }
}
#endif



