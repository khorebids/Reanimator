#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void HandleCurveInput(Rect localRect, PsdCoordinateSpace coordinates, IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null || e.alt || e.button != 0)
            return;

        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent)
            return;

        if (e.type == EventType.MouseDown)
        {
            if (!localRect.Contains(e.mousePosition))
                return;

            if (e.clickCount >= 2)
            {
                SelectCurveLayerFromDoubleClick(coordinates, drawList);
                e.Use();
                return;
            }

            var hitLayer = ResolveCurveDragLayer(drawList);
            if (hitLayer == null)
                return;

            curveLayerStableKey = BuildKey(hitLayer.name, hitLayer.layerId);
            curveDragLayerSelectionKey = BuildSelectionKey(hitLayer);
            selectedLayerSelectionKey = curveDragLayerSelectionKey;
            curveIsDragging = true;
            curveDragStartCanvas = e.mousePosition;
            curveDragCurrentCanvas = e.mousePosition;
            curveControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = curveControlId;
            GUI.changed = true;
            e.Use();
            return;
        }

        if (!curveIsDragging || GUIUtility.hotControl != curveControlId)
            return;

        if (e.type == EventType.MouseDrag)
        {
            curveDragCurrentCanvas = ClampPointToRect(e.mousePosition, localRect);
            GUI.changed = true;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            curveDragCurrentCanvas = ClampPointToRect(e.mousePosition, localRect);
            CompleteCurveSelectionFromDrag(coordinates);
            curveIsDragging = false;
            curveDragStartCanvas = Vector2.zero;
            curveDragCurrentCanvas = Vector2.zero;
            curveDragLayerSelectionKey = string.Empty;
            if (GUIUtility.hotControl == curveControlId)
                GUIUtility.hotControl = 0;
            curveControlId = 0;
            GUI.changed = true;
            e.Use();
        }
    }

    private void SelectCurveLayerFromDoubleClick(PsdCoordinateSpace coordinates, IReadOnlyList<LayerInfo> drawList)
    {
        if (coordinates == null || drawList == null)
            return;

        var mouseDoc = coordinates.ViewToDocument(Event.current.mousePosition);
        spriteHitBuffer.Clear();

        for (var i = drawList.Count - 1; i >= 0; i--)
        {
            var layer = drawList[i];
            if (layer == null || !layer.visible)
                continue;
            if (!IsPointInsideLayer(layer, mouseDoc))
                continue;

            spriteHitBuffer.Add(layer);
        }

        var nextKey = string.Empty;
        if (spriteHitBuffer.Count > 0)
        {
            var selectedIndex = -1;
            for (var i = 0; i < spriteHitBuffer.Count; i++)
            {
                var key = BuildSelectionKey(spriteHitBuffer[i]);
                if (!string.Equals(key, selectedLayerSelectionKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                selectedIndex = i;
                break;
            }

            var targetIndex = selectedIndex >= 0 ? (selectedIndex + 1) % spriteHitBuffer.Count : 0;
            nextKey = BuildSelectionKey(spriteHitBuffer[targetIndex]);
        }

        if (string.Equals(selectedLayerSelectionKey, nextKey, StringComparison.OrdinalIgnoreCase))
            return;

        selectedLayerSelectionKey = nextKey;
        curveLayerStableKey = string.Empty;
        curveDragLayerSelectionKey = string.Empty;
        curveSelectedLoopPositions.Clear();
        ResetCurveGraphToCurrentWeights();
        GUI.changed = true;
    }

    private LayerInfo ResolveCurveDragLayer(IReadOnlyList<LayerInfo> drawList)
    {
        var selectedLayer = FindLayerBySelectionKey(drawList, selectedLayerSelectionKey);
        if (selectedLayer != null && selectedLayer.visible)
            return selectedLayer;

        if (!string.IsNullOrEmpty(curveLayerStableKey) &&
            TryGetLayerVertexDataByStableKey(curveLayerStableKey, out var data, out var resolvedSelectionKey) &&
            data != null &&
            data.layer != null &&
            data.layer.visible)
        {
            if (!string.IsNullOrEmpty(resolvedSelectionKey))
                selectedLayerSelectionKey = resolvedSelectionKey;
            return data.layer;
        }

        if (TryGetSelectedLayer(out selectedLayer) && selectedLayer != null && selectedLayer.visible)
            return selectedLayer;

        return null;
    }

    private void DrawCurveOverlay(PsdCoordinateSpace coordinates, LayerInfo selectedLayer, float alpha)
    {
        if (selectedLayer != null && alpha > 0f && TryGetCurveSelection(out var data, out var boundaryLoop, out var drawVertices, out _) && curveSelectedLoopPositions != null && curveSelectedLoopPositions.Count > 0)
        {
            var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
            var linePoints = new Vector3[curveSelectedLoopPositions.Count];
            for (var i = 0; i < curveSelectedLoopPositions.Count; i++)
            {
                var loopPosition = curveSelectedLoopPositions[i];
                if (loopPosition < 0 || loopPosition >= boundaryLoop.Count)
                    return;

                var vertexIndex = boundaryLoop[loopPosition];
                if (vertexIndex < 0 || vertexIndex >= drawVertices.Length)
                    return;

                var canvas = toCanvasVertex(drawVertices[vertexIndex]);
                linePoints[i] = new Vector3(canvas.x, canvas.y, 0f);
            }

            Handles.BeginGUI();
            var prevColor = Handles.color;
            Handles.color = new Color(1f, 0.78f, 0.15f, Mathf.Clamp01(alpha));
            if (linePoints.Length >= 2)
                Handles.DrawAAPolyLine(3f, linePoints);
            Handles.color = prevColor;
            Handles.EndGUI();

            for (var i = 0; i < linePoints.Length; i++)
            {
                var point = linePoints[i];
                var size = i == 0 || i == linePoints.Length - 1 ? 8f : 6f;
                var rect = new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size);
                EditorGUI.DrawRect(rect, new Color(1f, 0.92f, 0.35f, Mathf.Clamp01(alpha)));
            }
        }

        if (!curveIsDragging)
            return;

        var selectionRect = BuildCurveSelectionRect(curveDragStartCanvas, curveDragCurrentCanvas);
        if (selectionRect.width <= 0f || selectionRect.height <= 0f)
            return;

        var fill = new Color(1f, 0.78f, 0.15f, 0.12f);
        var stroke = new Color(1f, 0.85f, 0.25f, 0.9f);
        EditorGUI.DrawRect(selectionRect, fill);
        DrawRectOutline(selectionRect, stroke, 1f);
    }

    private void CompleteCurveSelectionFromDrag(PsdCoordinateSpace coordinates)
    {
        if (!TryGetLayerVertexDataForKeys(curveDragLayerSelectionKey, curveLayerStableKey, out var data) || data == null)
            return;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return;

        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, drawVertices.Length, out var boundaryLoop) || boundaryLoop.Count < 3)
        {
            var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, drawVertices.Length);
            boundaryLoop = BuildFallbackBoundaryLoop(drawVertices, contourMask);
        }

        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return;

        var dragDelta = curveDragCurrentCanvas - curveDragStartCanvas;
        if (dragDelta.sqrMagnitude <= 36f)
        {
            var loopPosition = FindNearestContourLoopPosition(curveDragCurrentCanvas, data, boundaryLoop, drawVertices, coordinates, 12f);
            if (loopPosition >= 0)
                ApplySingleCurveLoopPosition(loopPosition, boundaryLoop.Count);
            ResetCurveGraphToCurrentWeights();
            return;
        }

        var selectionRect = BuildCurveSelectionRect(curveDragStartCanvas, curveDragCurrentCanvas);
        if (!TryBuildCurveLoopSegmentFromRect(selectionRect, data, boundaryLoop, drawVertices, coordinates, out var segment))
            return;

        curveSelectedLoopPositions.Clear();
        curveSelectedLoopPositions.AddRange(segment);
        ResetCurveGraphToCurrentWeights();
    }

    private void ApplySingleCurveLoopPosition(int loopPosition, int loopCount)
    {
        if (loopPosition < 0 || loopCount <= 0)
            return;

        if (curveSelectedLoopPositions.Count == 0)
        {
            curveSelectedLoopPositions.Add(loopPosition);
            ResetCurveGraphToCurrentWeights();
            return;
        }

        if (curveSelectedLoopPositions.Contains(loopPosition))
            return;

        var first = curveSelectedLoopPositions[0];
        var last = curveSelectedLoopPositions[curveSelectedLoopPositions.Count - 1];
        if (GetPreviousLoopPosition(first, loopCount) == loopPosition)
        {
            curveSelectedLoopPositions.Insert(0, loopPosition);
        }
        else if (GetNextLoopPosition(last, loopCount) == loopPosition)
        {
            curveSelectedLoopPositions.Add(loopPosition);
        }
        else
        {
            curveSelectedLoopPositions.Clear();
            curveSelectedLoopPositions.Add(loopPosition);
        }

        ResetCurveGraphToCurrentWeights();
    }

    private bool TryGetCurveSelection(out LayerVertexData data, out List<int> boundaryLoop, out Vector2[] drawVertices, out LayerInfo layer)
    {
        data = null;
        boundaryLoop = null;
        drawVertices = null;
        layer = null;

        if (string.IsNullOrEmpty(curveLayerStableKey) || curveSelectedLoopPositions == null || curveSelectedLoopPositions.Count == 0)
            return false;
        if (!TryGetLayerVertexDataByStableKey(curveLayerStableKey, out data, out _) || data == null)
            return false;

        drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return false;

        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, drawVertices.Length, out boundaryLoop) || boundaryLoop.Count < 3)
        {
            var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, drawVertices.Length);
            boundaryLoop = BuildFallbackBoundaryLoop(drawVertices, contourMask);
        }

        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;

        layer = data.layer;
        return true;
    }

    private static Rect BuildCurveSelectionRect(Vector2 a, Vector2 b)
    {
        var xMin = Mathf.Min(a.x, b.x);
        var xMax = Mathf.Max(a.x, b.x);
        var yMin = Mathf.Min(a.y, b.y);
        var yMax = Mathf.Max(a.y, b.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Vector2 ClampPointToRect(Vector2 point, Rect rect)
    {
        return new Vector2(
            Mathf.Clamp(point.x, rect.xMin, rect.xMax),
            Mathf.Clamp(point.y, rect.yMin, rect.yMax));
    }

    private LayerInfo FindTopmostCurveLayer(IReadOnlyList<LayerInfo> drawList, Vector2 mouseDoc)
    {
        if (drawList == null)
            return null;

        for (var i = drawList.Count - 1; i >= 0; i--)
        {
            var candidate = drawList[i];
            if (candidate == null || !candidate.visible)
                continue;
            if (!IsPointInsideLayer(candidate, mouseDoc))
                continue;

            return candidate;
        }

        return null;
    }

    private static bool TryBuildCurveLoopSegmentFromRect(
        Rect selectionRect,
        LayerVertexData data,
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<Vector2> drawVertices,
        PsdCoordinateSpace coordinates,
        out List<int> segment)
    {
        segment = null;
        if (boundaryLoop == null || drawVertices == null || coordinates == null || boundaryLoop.Count == 0)
            return false;

        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var selectedMask = new bool[boundaryLoop.Count];
        var anySelected = false;
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var vertexIndex = boundaryLoop[i];
            if (vertexIndex < 0 || vertexIndex >= drawVertices.Count)
                continue;

            if (!selectionRect.Contains(toCanvasVertex(drawVertices[vertexIndex])))
                continue;

            selectedMask[i] = true;
            anySelected = true;
        }

        if (!anySelected)
            return false;

        var bestLength = 0;
        var bestEnd = -1;
        var currentLength = 0;
        for (var i = 0; i < boundaryLoop.Count * 2; i++)
        {
            if (selectedMask[i % boundaryLoop.Count])
            {
                currentLength++;
                if (currentLength > boundaryLoop.Count)
                    currentLength = boundaryLoop.Count;
                if (currentLength > bestLength)
                {
                    bestLength = currentLength;
                    bestEnd = i;
                }
            }
            else
            {
                currentLength = 0;
            }
        }

        if (bestLength <= 0 || bestEnd < 0)
            return false;

        var start = (bestEnd - bestLength + 1) % boundaryLoop.Count;
        if (start < 0)
            start += boundaryLoop.Count;

        segment = new List<int>(bestLength);
        for (var i = 0; i < bestLength; i++)
            segment.Add((start + i) % boundaryLoop.Count);

        return segment.Count > 0;
    }

    private static int FindNearestContourLoopPosition(
        Vector2 mouseCanvas,
        LayerVertexData data,
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<Vector2> drawVertices,
        PsdCoordinateSpace coordinates,
        float maxCanvasDistance)
    {
        var bestLoopPosition = -1;
        var bestDistanceSqr = maxCanvasDistance * maxCanvasDistance;
        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var vertexIndex = boundaryLoop[i];
            if (vertexIndex < 0 || vertexIndex >= drawVertices.Count)
                continue;

            var canvasPoint = toCanvasVertex(drawVertices[vertexIndex]);
            var distanceSqr = (canvasPoint - mouseCanvas).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestLoopPosition = i;
        }

        return bestLoopPosition;
    }

}
#endif
