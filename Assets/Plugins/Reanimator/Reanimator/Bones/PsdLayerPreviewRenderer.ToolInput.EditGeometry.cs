#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    private void HandleEditGeometryInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null)
            return;

        if (HandleEditGeometryDeleteHotkey(e))
            return;

        if (e.rawType == EventType.MouseUp &&
            editGeometryControlId != 0 &&
            GUIUtility.hotControl == editGeometryControlId &&
            (editGeometryIsDragging || editGeometryIsRectSelecting))
        {
            if (editGeometryIsRectSelecting)
                SelectEditGeometryVerticesInRect(coordinates);

            EndEditGeometryRectSelection();
            if (GUIUtility.hotControl == editGeometryControlId)
                GUIUtility.hotControl = 0;
            editGeometryIsDragging = false;
            editGeometryMoveUndoRegistered = false;
            editGeometryControlId = 0;
            e.Use();
            GUI.changed = true;
            return;
        }

        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.alt)
            return;

        if (e.type == EventType.MouseDown && e.button == 1 && e.clickCount >= 2)
        {
            if (localRect.Contains(e.mousePosition) &&
                TryDeleteEditGeometryVertexUnderMouse(coordinates, drawList, coordinates.ViewToDocument(e.mousePosition), e.mousePosition))
            {
                e.Use();
                GUI.changed = true;
            }
            return;
        }

        if (e.button != 0)
            return;

        var hasActiveInteraction =
            GUIUtility.hotControl == editGeometryControlId &&
            editGeometryControlId != 0 &&
            (editGeometryIsDragging || editGeometryIsRectSelecting);
        if (!localRect.Contains(e.mousePosition) && !hasActiveInteraction)
        {
            if (e.type == EventType.MouseUp)
            {
                EndEditGeometryInteraction(false);
                EndEditGeometryRectSelection();
            }
            return;
        }

        if (e.type == EventType.MouseDown)
        {
            var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
            if (e.clickCount >= 2 &&
                TryHandleEditGeometryCreateVertexDoubleClick(mouseDoc, e.mousePosition, coordinates, drawList))
            {
                e.Use();
                GUI.changed = true;
                return;
            }

            if (TryBeginEditGeometryMouseInteraction(
                    mouseDoc,
                    e.mousePosition,
                    coordinates,
                    drawList,
                    e.shift,
                    out var startedDrag,
                    out var startedRectSelection))
            {
                if (startedDrag || startedRectSelection)
                {
                    editGeometryControlId = GUIUtility.GetControlID(FocusType.Passive);
                    GUIUtility.hotControl = editGeometryControlId;
                }

                if (startedDrag)
                {
                    BeginEditGeometryDrag(mouseDoc);
                    editGeometryIsDragging = true;
                }
                else
                {
                    editGeometryIsDragging = false;
                }
            }
            else
            {
                editGeometryIsDragging = false;
            }

            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (editGeometryIsDragging && GUIUtility.hotControl == editGeometryControlId)
            {
                var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
                if (MoveSelectedEditGeometryVertex(mouseDoc))
                    GUI.changed = true;
                e.Use();
                return;
            }

            if (editGeometryIsRectSelecting && GUIUtility.hotControl == editGeometryControlId)
            {
                editGeometryRectCurrentCanvas = e.mousePosition;
                GUI.changed = true;
                e.Use();
            }
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            if (editGeometryIsRectSelecting && GUIUtility.hotControl == editGeometryControlId)
                SelectEditGeometryVerticesInRect(coordinates);

            EndEditGeometryRectSelection();
            if (GUIUtility.hotControl == editGeometryControlId && editGeometryControlId != 0)
                GUIUtility.hotControl = 0;
            editGeometryIsDragging = false;
            editGeometryMoveUndoRegistered = false;
            editGeometryControlId = 0;
            e.Use();
            GUI.changed = true;
        }
    }

    private bool TryHandleEditGeometryCreateVertexDoubleClick(
        Vector2 mouseDoc,
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        if (coordinates == null)
            return false;
        if (!TryGetEditGeometryCreateVertexTarget(mouseDoc, drawList, out var data, out var selectionKey, out var stableKey))
            return false;

        var createPositionDoc = ResolveEditGeometryCreateVertexPosition(data, coordinates, mouseDoc, mouseCanvas);
        TryAddEditGeometryVertex(selectionKey, stableKey, createPositionDoc, true, out _);
        return true;
    }

    private bool TryDeleteEditGeometryVertexUnderMouse(
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        Vector2 mouseDoc,
        Vector2 mouseCanvas)
    {
        const float hitRadiusPx = 8f;
        if (TryFindClosestEditableVertex(
                selectedLayerSelectionKey,
                editGeometrySelectedLayerStableKey,
                coordinates,
                mouseCanvas,
                hitRadiusPx,
                out var selectedHit))
        {
            return TryDeleteEditGeometryVertex(selectedHit.selectionKey, selectedHit.stableKey, selectedHit.vertexIndex);
        }

        var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out _);
        if (!hasHitLayer || hitLayer == null)
            return false;

        var layerSelectionKey = BuildSelectionKey(hitLayer);
        var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
        if (!TryFindClosestEditableVertex(layerSelectionKey, stableKey, coordinates, mouseCanvas, hitRadiusPx, out var layerHit))
            return false;

        return TryDeleteEditGeometryVertex(layerHit.selectionKey, layerHit.stableKey, layerHit.vertexIndex);
    }

    private bool TryGetEditGeometryCreateVertexTarget(
        Vector2 mouseDoc,
        IReadOnlyList<LayerInfo> drawList,
        out LayerVertexData data,
        out string selectionKey,
        out string stableKey)
    {
        data = null;
        selectionKey = string.Empty;
        stableKey = string.Empty;

        var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out _);
        if (hasHitLayer && hitLayer != null)
        {
            selectionKey = BuildSelectionKey(hitLayer);
            stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) && data != null)
                return true;
        }

        return TryGetSelectedEditGeometryLayerData(out data, out selectionKey, out stableKey) && data != null;
    }

    private Vector2 ResolveEditGeometryCreateVertexPosition(
        LayerVertexData data,
        PsdCoordinateSpace coordinates,
        Vector2 mouseDoc,
        Vector2 mouseView)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3 || coordinates == null)
            return mouseDoc;

        var baseMouseDoc = ConvertPointToBaseDoc(data, mouseDoc);
        if (!TryGetBoundaryLoopForEditGeometryCreateVertex(data, out var boundaryLoop))
            return baseMouseDoc;

        if (!TryFindNearestEditGeometryCreateVertexContourPoint(data, coordinates, boundaryLoop, baseMouseDoc, mouseView, out var contourPointDoc, out var contourDistanceView))
            return baseMouseDoc;

        const float contourClickDistanceView = 8f;
        var insideContour = IsPointInsidePolygonOrOnEdge(baseMouseDoc, data.vertices, boundaryLoop);
        if (!insideContour || contourDistanceView <= contourClickDistanceView)
            return contourPointDoc;

        return baseMouseDoc;
    }

    private static bool TryGetBoundaryLoopForEditGeometryCreateVertex(LayerVertexData data, out List<int> boundaryLoop)
    {
        boundaryLoop = null;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        if (TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, data.vertices.Length, out boundaryLoop) &&
            boundaryLoop != null &&
            boundaryLoop.Count >= 3)
            return true;

        var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, data.vertices.Length);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, data.vertices.Length, out _, out var fallbackBoundaryPairs);
            contourMask = BuildContourVertexMask(fallbackBoundaryPairs, data.vertices.Length);
        }

        boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        return boundaryLoop != null && boundaryLoop.Count >= 3;
    }

    private static bool TryFindNearestEditGeometryCreateVertexContourPoint(
        LayerVertexData data,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<int> boundaryLoop,
        Vector2 pointDoc,
        Vector2 pointView,
        out Vector2 contourPointDoc,
        out float contourDistanceView)
    {
        contourPointDoc = pointDoc;
        contourDistanceView = float.PositiveInfinity;
        if (data == null || data.vertices == null || coordinates == null || boundaryLoop == null || boundaryLoop.Count < 3)
            return false;

        var toView = CreateLayerVertexToViewMapper(coordinates, data, data.vertices);
        var found = false;
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var aIndex = boundaryLoop[i];
            var bIndex = boundaryLoop[(i + 1) % boundaryLoop.Count];
            if (aIndex < 0 || bIndex < 0 || aIndex >= data.vertices.Length || bIndex >= data.vertices.Length)
                continue;

            var a = data.vertices[aIndex];
            var b = data.vertices[bIndex];
            var ab = b - a;
            var abLenSqr = ab.sqrMagnitude;
            if (abLenSqr <= 1e-8f)
                continue;

            var t = Mathf.Clamp01(Vector2.Dot(pointDoc - a, ab) / abLenSqr);
            var projectedDoc = a + ab * t;
            var projectedView = toView != null ? toView(projectedDoc) : projectedDoc;
            var distanceView = Vector2.Distance(pointView, projectedView);
            if (distanceView >= contourDistanceView)
                continue;

            contourDistanceView = distanceView;
            contourPointDoc = projectedDoc;
            found = true;
        }

        return found;
    }

    private bool HandleEditGeometryDeleteHotkey(Event e)
    {
        if (e == null || e.type != EventType.KeyDown)
            return false;
        if (e.keyCode != KeyCode.Delete && e.keyCode != KeyCode.Backspace)
            return false;

        if (!DeleteSelectedEditGeometryVertex())
            return false;

        e.Use();
        GUI.changed = true;
        return true;
    }

    private bool TryBeginEditGeometryMouseInteraction(
        Vector2 mouseDoc,
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        bool toggleSelection,
        out bool startedDrag,
        out bool startedRectSelection)
    {
        startedDrag = false;
        startedRectSelection = false;
        const float hitRadiusPx = 8f;

        if (TryFindClosestEditableVertex(
                selectedLayerSelectionKey,
                editGeometrySelectedLayerStableKey,
                coordinates,
                mouseCanvas,
                hitRadiusPx,
                out var selectedHit))
        {
            var wasSelected = IsEditGeometryVertexSelected(selectedHit.stableKey, selectedHit.vertexIndex);
            ApplyEditGeometryVertexSelection(selectedHit.selectionKey, selectedHit.stableKey, selectedHit.vertexIndex, toggleSelection || wasSelected, toggleSelection);
            startedDrag = !toggleSelection && IsEditGeometryVertexSelected(selectedHit.stableKey, selectedHit.vertexIndex);
            return true;
        }

        var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out var blockedByLayerLock);
        if (!hasHitLayer && blockedByLayerLock)
            return true;

        if (hitLayer == null)
        {
            if (TryGetSelectedEditGeometryLayerData(out _, out _, out _))
            {
                StartEditGeometryRectSelection(mouseCanvas, toggleSelection);
                startedRectSelection = true;
                return true;
            }

            ClearEditGeometryVertexSelection(true);
            return true;
        }

        var layerSelectionKey = BuildSelectionKey(hitLayer);
        var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
        if (TryFindClosestEditableVertex(layerSelectionKey, stableKey, coordinates, mouseCanvas, hitRadiusPx, out var layerHit))
        {
            var wasSelected = IsEditGeometryVertexSelected(layerHit.stableKey, layerHit.vertexIndex);
            ApplyEditGeometryVertexSelection(layerHit.selectionKey, layerHit.stableKey, layerHit.vertexIndex, toggleSelection || wasSelected, toggleSelection);
            startedDrag = !toggleSelection && IsEditGeometryVertexSelected(layerHit.stableKey, layerHit.vertexIndex);
            return true;
        }

        selectedLayerSelectionKey = layerSelectionKey;
        editGeometrySelectedLayerStableKey = stableKey;
        StartEditGeometryRectSelection(mouseCanvas, toggleSelection);
        startedRectSelection = true;
        return true;
    }

    private void ApplyEditGeometryVertexSelection(string selectionKey, string stableKey, int vertexIndex)
    {
        ApplyEditGeometryVertexSelection(selectionKey, stableKey, vertexIndex, false, false);
    }

    private void ApplyEditGeometryVertexSelection(string selectionKey, string stableKey, int vertexIndex, bool preserveExisting, bool toggle)
    {
        var previousStableKey = editGeometrySelectedLayerStableKey;
        selectedLayerSelectionKey = selectionKey;
        editGeometrySelectedLayerStableKey = stableKey;
        if (editGeometrySelectedVertexIndices == null)
            editGeometrySelectedVertexIndices = new List<int>();

        if (!preserveExisting || !string.Equals(previousStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            editGeometrySelectedVertexIndices.Clear();

        if (vertexIndex < 0)
        {
            editGeometrySelectedVertexIndex = -1;
            return;
        }

        var existingIndex = editGeometrySelectedVertexIndices.IndexOf(vertexIndex);
        if (toggle)
        {
            if (existingIndex >= 0)
                editGeometrySelectedVertexIndices.RemoveAt(existingIndex);
            else
                editGeometrySelectedVertexIndices.Add(vertexIndex);
        }
        else if (existingIndex < 0)
        {
            editGeometrySelectedVertexIndices.Add(vertexIndex);
        }

        editGeometrySelectedVertexIndex = editGeometrySelectedVertexIndices.IndexOf(vertexIndex) >= 0
            ? vertexIndex
            : (editGeometrySelectedVertexIndices.Count > 0 ? editGeometrySelectedVertexIndices[0] : -1);
    }

    private void StartEditGeometryRectSelection(Vector2 mouseCanvas, bool toggleSelection)
    {
        editGeometryIsRectSelecting = true;
        editGeometryRectToggleSelection = toggleSelection;
        editGeometryRectStartCanvas = mouseCanvas;
        editGeometryRectCurrentCanvas = mouseCanvas;
    }

    private void SelectEditGeometryVerticesInRect(PsdCoordinateSpace coordinates)
    {
        if (coordinates == null)
            return;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
            return;
        if (data.vertices == null || data.vertices.Length == 0)
            return;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return;

        var rect = BuildEditGeometrySelectionRect(editGeometryRectStartCanvas, editGeometryRectCurrentCanvas);
        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var hits = new List<int>();
        for (var i = 0; i < drawVertices.Length; i++)
        {
            if (rect.Contains(toCanvasVertex(drawVertices[i])))
                hits.Add(i);
        }

        if (editGeometrySelectedVertexIndices == null)
            editGeometrySelectedVertexIndices = new List<int>();

        if (!editGeometryRectToggleSelection ||
            !string.Equals(editGeometrySelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
        {
            editGeometrySelectedVertexIndices.Clear();
            editGeometrySelectedVertexIndices.AddRange(hits);
        }
        else
        {
            for (var i = 0; i < hits.Count; i++)
            {
                var existingIndex = editGeometrySelectedVertexIndices.IndexOf(hits[i]);
                if (existingIndex >= 0)
                    editGeometrySelectedVertexIndices.RemoveAt(existingIndex);
                else
                    editGeometrySelectedVertexIndices.Add(hits[i]);
            }
        }

        selectedLayerSelectionKey = selectionKey;
        editGeometrySelectedLayerStableKey = stableKey;
        editGeometrySelectedVertexIndex = editGeometrySelectedVertexIndices.Count > 0 ? editGeometrySelectedVertexIndices[0] : -1;
    }

    private void EndEditGeometryRectSelection()
    {
        if (GUIUtility.hotControl == editGeometryControlId && editGeometryControlId != 0)
            GUIUtility.hotControl = 0;

        editGeometryIsRectSelecting = false;
        editGeometryRectToggleSelection = false;
        editGeometryRectStartCanvas = Vector2.zero;
        editGeometryRectCurrentCanvas = Vector2.zero;
    }

    private static Rect BuildEditGeometrySelectionRect(Vector2 a, Vector2 b)
    {
        var xMin = Mathf.Min(a.x, b.x);
        var xMax = Mathf.Max(a.x, b.x);
        var yMin = Mathf.Min(a.y, b.y);
        var yMax = Mathf.Max(a.y, b.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void DrawEditGeometrySelectionOverlay(PsdCoordinateSpace coordinates, float alpha)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || coordinates == null)
            return;
        if (!editGeometryIsRectSelecting)
            return;

        var rect = BuildEditGeometrySelectionRect(editGeometryRectStartCanvas, editGeometryRectCurrentCanvas);
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        EditorGUI.DrawRect(rect, new Color(1f, 0.85f, 0.1f, 0.10f));
        Handles.BeginGUI();
        var prev = Handles.color;
        Handles.color = new Color(1f, 0.85f, 0.1f, Mathf.Clamp01(alpha));
        Handles.DrawAAPolyLine(
            2f,
            new Vector3(rect.xMin, rect.yMin, 0f),
            new Vector3(rect.xMax, rect.yMin, 0f),
            new Vector3(rect.xMax, rect.yMax, 0f),
            new Vector3(rect.xMin, rect.yMax, 0f),
            new Vector3(rect.xMin, rect.yMin, 0f));
        Handles.color = prev;
        Handles.EndGUI();
    }

    private bool TryFindClosestEditableVertex(
        string selectionKey,
        string stableKey,
        PsdCoordinateSpace coordinates,
        Vector2 mouseCanvas,
        float hitRadiusPx,
        out EditVertexHit hit)
    {
        hit = default;
        if (coordinates == null || string.IsNullOrEmpty(selectionKey))
            return false;
        if (!TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var data) || data == null)
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return false;

        var hitRadiusSqr = hitRadiusPx * hitRadiusPx;
        var bestVertex = -1;
        var bestDistance = hitRadiusSqr;
        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        for (var i = 0; i < drawVertices.Length; i++)
        {
            var canvasPoint = toCanvasVertex(drawVertices[i]);
            var distance = (canvasPoint - mouseCanvas).sqrMagnitude;
            if (distance > bestDistance)
                continue;
            bestDistance = distance;
            bestVertex = i;
        }

        if (bestVertex < 0)
            return false;

        hit = new EditVertexHit(selectionKey, BuildKey(data.layer.name, data.layer.layerId), bestVertex);
        return true;
    }

    private readonly struct EditVertexHit
    {
        public readonly string selectionKey;
        public readonly string stableKey;
        public readonly int vertexIndex;

        public EditVertexHit(string selectionKey, string stableKey, int vertexIndex)
        {
            this.selectionKey = selectionKey;
            this.stableKey = stableKey;
            this.vertexIndex = vertexIndex;
        }
    }

    private bool TryFindClosestEditableEdge(
        string selectionKey,
        string stableKey,
        PsdCoordinateSpace coordinates,
        Vector2 mouseCanvas,
        float hitRadiusPx,
        out EditEdgeHit hit)
    {
        hit = default;
        if (coordinates == null || string.IsNullOrEmpty(selectionKey))
            return false;
        if (!TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var data) || data == null)
            return false;
        if (data.vertices == null || data.vertices.Length < 2)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length)
            return false;

        var pairs = data.allEdgePairs;
        if ((pairs == null || pairs.Length < 2) && data.triangles != null && data.triangles.Length >= 3)
            BuildEdgePairs(data.triangles, data.vertices.Length, out pairs, out _);
        if (pairs == null || pairs.Length < 2)
            return false;

        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var bestDistance = hitRadiusPx * hitRadiusPx;
        var bestA = -1;
        var bestB = -1;
        var bestT = 0f;
        for (var i = 0; i + 1 < pairs.Length; i += 2)
        {
            var a = pairs[i];
            var b = pairs[i + 1];
            if (a < 0 || b < 0 || a >= drawVertices.Length || b >= drawVertices.Length || a == b)
                continue;

            var pa = toCanvasVertex(drawVertices[a]);
            var pb = toCanvasVertex(drawVertices[b]);
            var closest = ClosestPointOnWeightSliderSegment(mouseCanvas, pa, pb, out var t);
            var distance = (closest - mouseCanvas).sqrMagnitude;
            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            bestA = a;
            bestB = b;
            bestT = t;
        }

        if (bestA < 0 || bestB < 0)
            return false;

        hit = new EditEdgeHit(selectionKey, stableKey, bestA, bestB, bestT);
        return true;
    }

    private readonly struct EditEdgeHit
    {
        public readonly string selectionKey;
        public readonly string stableKey;
        public readonly int vertexA;
        public readonly int vertexB;
        public readonly float t;

        public EditEdgeHit(string selectionKey, string stableKey, int vertexA, int vertexB, float t)
        {
            this.selectionKey = selectionKey;
            this.stableKey = stableKey;
            this.vertexA = vertexA;
            this.vertexB = vertexB;
            this.t = t;
        }
    }
}
#endif
