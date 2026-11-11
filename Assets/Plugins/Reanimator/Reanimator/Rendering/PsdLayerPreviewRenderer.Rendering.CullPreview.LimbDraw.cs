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
    private bool DrawLimbAfterCullGeometryPreview(
        PsdCoordinateSpace coordinateMapper,
        Func<Vector2, Vector2> toCanvas,
        LayerInfo selectedLayer,
        float alpha,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices)
    {
        if (coordinateMapper == null || toCanvas == null || selectedLayer == null)
            return false;
        if (radius <= 0.0001f)
            return false;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return false;

        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (!TryGetLayerVertexDataForKeys(selectedLayerSelectionKey, stableKey, out var sourceData) || sourceData == null)
            return false;

        var limbIndex = FindActiveLimbLineIndex();
        if (limbIndex < 0 || limbIndex >= limbLines.Count)
            return false;
        var line = limbLines[limbIndex];
        if (line == null)
            return false;
        if (!line.hasContourPoints)
            RefreshLimbLineContour(line);
        if (!line.hasContourPoints)
            return false;

        if (!TryGetCachedLimbAfterCullPreviewGeometry(
                sourceData,
                selectedLayerSelectionKey,
                stableKey,
                line,
                radius,
                true,
                removeInnerVertices,
                out var cache))
        {
            return false;
        }

        var vertexCount = cache.previewVerticesDraw != null ? cache.previewVerticesDraw.Length : 0;
        if (vertexCount < 3)
            return false;

        var interactive = limbIsDragging || limbIsCreating || radiusCullIsDragging || radiusCullHandleIsDragging;
        var isComplete = AdvanceLimbPreviewVisibility(cache, interactive);
        if (!isComplete)
            limbPreviewNeedsRepaint = true;

        var toCanvasVertex = ResolveLayerVertexToViewMapper(coordinateMapper, sourceData.layer, toCanvas);
        DrawBoneInfluenceOnGeometryTriangles(
            toCanvasVertex,
            cache.previewVerticesDraw,
            cache.previewBoneWeights,
            cache.previewTriangles,
            sourceData.spriteBoneToRendererBone,
            alpha,
            cache.visibleTriangleCount);

        var innerLineColor = new Color(0.35f, 0.98f, 1f, Mathf.Clamp01(alpha));
        var boundaryLineColor = new Color(1f, 0.6f, 0.12f, Mathf.Clamp01(alpha));
        var pointColor = new Color(0.55f, 0.92f, 1f, Mathf.Clamp01(alpha));
        const float thickOffset = 0.65f;
        const float markerHalfSize = 2f;

        Handles.BeginGUI();
        var prev = Handles.color;

        var visibleInnerPairs = Mathf.Clamp(cache.visibleInnerPairCount, 0, cache.totalInnerPairCount);
        EnsureVertexBuffer(ref vertexEdgeSegments, visibleInnerPairs * 2);
        var edgeWrite = 0;
        for (var i = 0; i < visibleInnerPairs; i++)
        {
            var a = cache.innerEdgePairs[i * 2];
            var b = cache.innerEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(cache.previewVerticesDraw[a]);
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(cache.previewVerticesDraw[b]);
        }
        if (edgeWrite > 1)
        {
            Handles.color = innerLineColor;
            DrawWrittenSegments(vertexEdgeSegments, edgeWrite);
        }

        var visibleBoundaryPairs = Mathf.Clamp(cache.visibleBoundaryPairCount, 0, cache.totalBoundaryPairCount);
        EnsureVertexBuffer(ref vertexBoundarySegments, visibleBoundaryPairs * 2);
        var boundaryWrite = 0;
        for (var i = 0; i < visibleBoundaryPairs; i++)
        {
            var a = cache.boundaryEdgePairs[i * 2];
            var b = cache.boundaryEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(cache.previewVerticesDraw[a]);
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(cache.previewVerticesDraw[b]);
        }
        if (boundaryWrite > 1)
        {
            Handles.color = boundaryLineColor;
            DrawWrittenSegments(vertexBoundarySegments, boundaryWrite);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(thickOffset, 0f), boundaryLineColor);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(0f, thickOffset), boundaryLineColor);
        }

        var visiblePoints = Mathf.Clamp(cache.visiblePointCount, 0, cache.totalPointCount);
        EnsureVertexBuffer(ref vertexPointSegments, visiblePoints * 4);
        var pointWrite = 0;
        for (var i = 0; i < visiblePoints; i++)
        {
            var p = toCanvasVertex(cache.previewVerticesDraw[i]);
            vertexPointSegments[pointWrite++] = p + new Vector2(-markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, -markerHalfSize);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, markerHalfSize);
        }
        if (pointWrite > 1)
        {
            Handles.color = pointColor;
            DrawWrittenSegments(vertexPointSegments, pointWrite);
        }

        Handles.color = prev;
        Handles.EndGUI();
        return true;
    }

}
#endif
