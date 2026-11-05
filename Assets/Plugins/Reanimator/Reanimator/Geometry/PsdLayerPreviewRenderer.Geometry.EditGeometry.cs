#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void BeginEditGeometryDrag(Vector2 mouseDoc)
    {
        editGeometryMoveUndoRegistered = false;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out _, out _))
        {
            editGeometryDragLastBaseDoc = mouseDoc;
            return;
        }

        editGeometryDragLastBaseDoc = ConvertPointToBaseDoc(data, mouseDoc);
    }

    private bool MoveSelectedEditGeometryVertex(Vector2 targetPositionDoc)
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;

        var selectedCount = NormalizeEditGeometrySelection(data, stableKey);
        if (selectedCount <= 0)
            return false;

        var basePoint = ConvertPointToBaseDoc(data, targetPositionDoc);
        var delta = basePoint - editGeometryDragLastBaseDoc;
        if (delta.sqrMagnitude <= 1e-8f)
            return false;

        RegisterEditGeometryMoveUndoOnce();
        editGeometryDragLastBaseDoc = basePoint;
        var changed = false;
        for (var i = 0; i < editGeometrySelectedVertexIndices.Count; i++)
        {
            var vertexIndex = editGeometrySelectedVertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
                continue;

            var clamped = ClampPointToLayerBounds(data.layer, data.vertices[vertexIndex] + delta);
            if ((data.vertices[vertexIndex] - clamped).sqrMagnitude <= 1e-8f)
                continue;

            data.vertices[vertexIndex] = clamped;
            if (data.vertexUvs != null && vertexIndex < data.vertexUvs.Length)
                data.vertexUvs[vertexIndex] = ComputeUvForPoint(data.layer, clamped);
            changed = true;
        }

        if (!changed)
            return false;

        if (!TryRemoveNonContourVerticesOutsideContour(data, stableKey, out var removedOutsideVertices) ||
            !removedOutsideVertices)
        {
            TryRetriangulateKeepingVertexSet(data);
            BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        }
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private void RegisterEditGeometryMoveUndoOnce()
    {
        if (editGeometryMoveUndoRegistered)
            return;

        RegisterUndo("Move Geometry Vertices");
        editGeometryMoveUndoRegistered = true;
    }

    private static Vector2 ClampPointToLayerBounds(LayerInfo layer, Vector2 pointDoc)
    {
        if (layer == null)
            return pointDoc;

        var bounds = PsdCoordinateSpace.GetLayerDocumentRect(layer);
        return new Vector2(
            Mathf.Clamp(pointDoc.x, bounds.xMin, bounds.xMax),
            Mathf.Clamp(pointDoc.y, bounds.yMin, bounds.yMax));
    }

    private bool TryRetriangulateKeepingVertexSet(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        var boundaryPairs = data.boundaryEdgePairs;
        if (boundaryPairs == null || boundaryPairs.Length < 6)
            return false;

        var boundaryEdges = new List<Vector2Int>(boundaryPairs.Length / 2);
        for (var i = 0; i + 1 < boundaryPairs.Length; i += 2)
        {
            var a = boundaryPairs[i];
            var b = boundaryPairs[i + 1];
            if (a < 0 || b < 0 || a >= data.vertices.Length || b >= data.vertices.Length || a == b)
                continue;
            boundaryEdges.Add(new Vector2Int(a, b));
        }

        if (boundaryEdges.Count < 3)
            return false;

        var sourceVertices = new List<Vector2>(data.vertices);
        if (!TryTriangulateWithUnityTessellation(sourceVertices, boundaryEdges, 0, out var triangulatedVertices, out var triangles, out _))
            return false;

        triangles = SanitizeTriangles(triangles, triangulatedVertices.Length);
        if (triangles.Length < 3 || triangulatedVertices.Length != data.vertices.Length)
            return false;
        if (!TryBuildVertexRemap(sourceVertices, triangulatedVertices, out var sourceToTri))
            return false;
        if (!TryBuildInversePermutation(sourceToTri, triangulatedVertices.Length, out var triToSource))
            return false;

        var remappedTriangles = new int[triangles.Length];
        for (var i = 0; i < triangles.Length; i++)
        {
            var triIndex = triangles[i];
            if (triIndex < 0 || triIndex >= triToSource.Length)
                return false;
            remappedTriangles[i] = triToSource[triIndex];
        }

        remappedTriangles = SanitizeTriangles(remappedTriangles, data.vertices.Length);
        if (remappedTriangles.Length < 3)
            return false;

        data.triangles = remappedTriangles;
        return true;
    }

    private static bool TryBuildInversePermutation(int[] sourceToTarget, int targetCount, out int[] targetToSource)
    {
        targetToSource = Array.Empty<int>();
        if (sourceToTarget == null || sourceToTarget.Length == 0 || targetCount <= 0)
            return false;

        targetToSource = new int[targetCount];
        for (var i = 0; i < targetToSource.Length; i++)
            targetToSource[i] = -1;

        for (var source = 0; source < sourceToTarget.Length; source++)
        {
            var target = sourceToTarget[source];
            if (target < 0 || target >= targetCount)
                return false;
            if (targetToSource[target] >= 0)
                return false;
            targetToSource[target] = source;
        }

        for (var i = 0; i < targetToSource.Length; i++)
        {
            if (targetToSource[i] < 0)
                return false;
        }

        return true;
    }

    private void MarkEditGeometryDataChanged(LayerVertexData data, string selectionKey, string stableKey)
    {
        if (data == null)
            return;

        InvalidateRadiusCullPreviewCache(true);
        hasPendingChanges = true;
        MarkLayerSpriteStateDirty(data.layer);
        var patchPath = !string.IsNullOrWhiteSpace(cachedPath) ? cachedPath : string.Empty;
        if (!string.IsNullOrWhiteSpace(patchPath))
            UpsertAutoGeometryPatch(patchPath, selectionKey, stableKey, data, false);
    }

    private bool EndEditGeometryInteraction(bool clearSelection)
    {
        var changed = false;
        if (GUIUtility.hotControl == editGeometryControlId && editGeometryControlId != 0)
        {
            GUIUtility.hotControl = 0;
            changed = true;
        }

        if (editGeometryIsDragging || editGeometryControlId != 0)
            changed = true;

        editGeometryIsDragging = false;
        editGeometryIsRectSelecting = false;
        editGeometryRectToggleSelection = false;
        editGeometryRectStartCanvas = Vector2.zero;
        editGeometryRectCurrentCanvas = Vector2.zero;
        editGeometryMoveUndoRegistered = false;
        editGeometryControlId = 0;
        if (clearSelection)
        {
            if (editGeometrySelectedVertexIndex >= 0 ||
                !string.IsNullOrEmpty(editGeometrySelectedLayerStableKey) ||
                (editGeometrySelectedVertexIndices != null && editGeometrySelectedVertexIndices.Count > 0))
                changed = true;
            ClearEditGeometryVertexSelection(true);
            if (createEdgeStartVertexIndex >= 0 || !string.IsNullOrEmpty(createEdgeLayerStableKey))
                changed = true;
            createEdgeStartVertexIndex = -1;
            createEdgeLayerStableKey = string.Empty;
        }

        return changed;
    }
}
#endif
