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
    private bool DrawRadiusCullGeometryPreview(
        PsdCoordinateSpace coordinateMapper,
        Func<Vector2, Vector2> toCanvas,
        LayerInfo selectedLayer,
        float alpha,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices,
        bool includeJointContourIntersections = false,
        float jointCPairOffset = 0f,
        int jointLongPoints = 0,
        float jointStartLongPointsOffset = 0f,
        float jointEndLongPointsOffset = 0f)
    {
        if (coordinateMapper == null || toCanvas == null || selectedLayer == null)
            return false;
        if (radius <= 0.0001f || (!removeContourVertices && !removeInnerVertices && !includeJointContourIntersections))
            return false;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return radiusCullPreviewCache != null && radiusCullPreviewCache.hasGeometryResult;

        var selectionKey = selectedLayerSelectionKey;
        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (!TryGetLayerVertexDataForKeys(selectedLayerSelectionKey, stableKey, out var data) || data == null)
            return false;
        if (!TryGetCachedRadiusCullPreviewGeometry(
                data,
                selectionKey,
                stableKey,
                radius,
                removeContourVertices,
                removeInnerVertices,
                includeJointContourIntersections,
                jointCPairOffset,
                jointLongPoints,
                jointStartLongPointsOffset,
                jointEndLongPointsOffset,
                out var previewDrawVertices,
                out var previewAllEdgePairs,
                out var previewBoundaryEdgePairs,
                out var previewTriangles,
                out var previewBoneWeights,
                out var previewSpriteBoneToRendererBone))
            return false;

        var vertexCount = previewDrawVertices != null ? previewDrawVertices.Length : 0;
        if (vertexCount < 3)
            return false;

        var toCanvasVertex = ResolveLayerVertexToViewMapper(coordinateMapper, data.layer, toCanvas);
        var previewSpriteBoneMap = previewSpriteBoneToRendererBone != null && previewSpriteBoneToRendererBone.Length > 0
            ? previewSpriteBoneToRendererBone
            : data.spriteBoneToRendererBone;
        DrawBoneInfluenceOnGeometryTriangles(
            toCanvasVertex,
            previewDrawVertices,
            previewBoneWeights,
            previewTriangles,
            previewSpriteBoneMap,
            alpha,
            int.MaxValue,
            includeJointContourIntersections ? 1f : -1f);
        var contourEdgeKeys = BuildEdgeKeySet(previewBoundaryEdgePairs, vertexCount);

        var innerLineColor = new Color(0.35f, 0.98f, 1f, Mathf.Clamp01(alpha));
        var boundaryLineColor = new Color(1f, 0.6f, 0.12f, Mathf.Clamp01(alpha));
        var pointColor = new Color(0.55f, 0.92f, 1f, Mathf.Clamp01(alpha));
        const float thickOffset = 0.65f;
        const float markerHalfSize = 2f;

        Handles.BeginGUI();
        var prev = Handles.color;

        var edgePairCount = previewAllEdgePairs != null ? previewAllEdgePairs.Length / 2 : 0;
        var edgeSegmentCount = edgePairCount * 2;
        EnsureVertexBuffer(ref vertexEdgeSegments, edgeSegmentCount);
        var edgeWrite = 0;
        for (var i = 0; i < edgePairCount; i++)
        {
            var a = previewAllEdgePairs[i * 2];
            var b = previewAllEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            if (contourEdgeKeys != null && contourEdgeKeys.Contains(MakeEdgeKey(a, b)))
                continue;
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(previewDrawVertices[a]);
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(previewDrawVertices[b]);
        }

        if (edgeWrite <= 1 && edgePairCount > 0)
        {
            edgeWrite = 0;
            for (var i = 0; i < edgePairCount; i++)
            {
                var a = previewAllEdgePairs[i * 2];
                var b = previewAllEdgePairs[i * 2 + 1];
                if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                    continue;
                vertexEdgeSegments[edgeWrite++] = toCanvasVertex(previewDrawVertices[a]);
                vertexEdgeSegments[edgeWrite++] = toCanvasVertex(previewDrawVertices[b]);
            }
        }

        if (edgeWrite > 1)
        {
            Handles.color = innerLineColor;
            DrawWrittenSegments(vertexEdgeSegments, edgeWrite);
        }

        var boundaryPairCount = previewBoundaryEdgePairs != null ? previewBoundaryEdgePairs.Length / 2 : 0;
        var boundarySegmentCount = boundaryPairCount * 2;
        EnsureVertexBuffer(ref vertexBoundarySegments, boundarySegmentCount);
        var boundaryWrite = 0;
        for (var i = 0; i < boundaryPairCount; i++)
        {
            var a = previewBoundaryEdgePairs[i * 2];
            var b = previewBoundaryEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(previewDrawVertices[a]);
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(previewDrawVertices[b]);
        }

        if (boundaryWrite > 1)
        {
            Handles.color = boundaryLineColor;
            DrawWrittenSegments(vertexBoundarySegments, boundaryWrite);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(thickOffset, 0f), boundaryLineColor);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(0f, thickOffset), boundaryLineColor);
        }

        var drawWeightedJointPoints = includeJointContourIntersections &&
                                      previewBoneWeights != null &&
                                      previewBoneWeights.Length >= vertexCount &&
                                      previewSpriteBoneMap != null &&
                                      previewSpriteBoneMap.Length > 0;

        var pointSegmentCount = vertexCount * 4;
        EnsureVertexBuffer(ref vertexPointSegments, pointSegmentCount);
        var pointWrite = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            var p = toCanvasVertex(previewDrawVertices[i]);
            vertexPointSegments[pointWrite++] = p + new Vector2(-markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, -markerHalfSize);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, markerHalfSize);
        }

        if (pointWrite > 1)
        {
            if (drawWeightedJointPoints)
            {
                for (var i = 0; i < vertexCount; i++)
                {
                    var weightedPointColor = EvaluateInfluenceVertexColor(previewBoneWeights[i], previewSpriteBoneMap, alpha);
                    if (weightedPointColor.a <= 0.0001f)
                        weightedPointColor = pointColor;
                    else
                        weightedPointColor.a = Mathf.Max(weightedPointColor.a, Mathf.Clamp01(alpha * 0.9f));

                    var p = toCanvasVertex(previewDrawVertices[i]);
                    Handles.color = weightedPointColor;
                    Handles.DrawLine(p + new Vector2(-markerHalfSize, 0f), p + new Vector2(markerHalfSize, 0f));
                    Handles.DrawLine(p + new Vector2(0f, -markerHalfSize), p + new Vector2(0f, markerHalfSize));
                }
            }
            else
            {
                Handles.color = pointColor;
                DrawWrittenSegments(vertexPointSegments, pointWrite);
            }
        }

        Handles.color = prev;
        Handles.EndGUI();
        return true;
    }

}
#endif
