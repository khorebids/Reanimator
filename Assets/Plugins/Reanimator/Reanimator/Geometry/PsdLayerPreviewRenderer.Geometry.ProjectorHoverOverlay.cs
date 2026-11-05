#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void DrawProjectorCandidateHoverGeometry(ReanimatorPreviewContext context, LayerVertexData data)
    {
        if (context == null || context.Coordinates == null || data == null || data.vertices == null || data.vertices.Length == 0)
            return;

        var alpha = context.Visibility != null ? Mathf.Clamp01(context.Visibility.VertexAlpha) : 1f;
        if (alpha <= 0.0001f)
            return;

        var vertices = data.vertices;
        var vertexCount = vertices.Length;
        var toCanvasVertex = CreateLayerVertexToViewMapper(context.Coordinates, data, vertices);
        var contourEdgeKeys = BuildEdgeKeySet(data.boundaryEdgePairs, vertexCount);
        var innerLineColor = new Color(0.1f, 0.95f, 1f, Mathf.Clamp01(alpha * 0.78f));
        var boundaryLineColor = new Color(1f, 0.82f, 0.14f, Mathf.Clamp01(alpha));
        var pointColor = new Color(1f, 0.95f, 0.35f, Mathf.Clamp01(alpha));
        const float markerHalfSize = 3f;
        const float thickOffset = 0.8f;

        Handles.BeginGUI();
        var prev = Handles.color;

        var edgePairCount = data.allEdgePairs != null ? data.allEdgePairs.Length / 2 : 0;
        EnsureVertexBuffer(ref vertexEdgeSegments, edgePairCount * 2);
        var edgeWrite = 0;
        for (var i = 0; i < edgePairCount; i++)
        {
            var a = data.allEdgePairs[i * 2];
            var b = data.allEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            if (contourEdgeKeys != null && contourEdgeKeys.Contains(MakeEdgeKey(a, b)))
                continue;

            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(vertices[a]);
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(vertices[b]);
        }

        if (edgeWrite > 1)
            DrawProjectorHoverSegments(vertexEdgeSegments, edgeWrite, innerLineColor);

        var boundaryPairCount = data.boundaryEdgePairs != null ? data.boundaryEdgePairs.Length / 2 : 0;
        EnsureVertexBuffer(ref vertexBoundarySegments, boundaryPairCount * 2);
        var boundaryWrite = 0;
        for (var i = 0; i < boundaryPairCount; i++)
        {
            var a = data.boundaryEdgePairs[i * 2];
            var b = data.boundaryEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;

            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(vertices[a]);
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(vertices[b]);
        }

        if (boundaryWrite > 1)
        {
            DrawProjectorHoverSegments(vertexBoundarySegments, boundaryWrite, boundaryLineColor);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(thickOffset, 0f), boundaryLineColor);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(0f, thickOffset), boundaryLineColor);
        }
        else if (data.layer != null)
        {
            DrawProjectorHoverLayerRect(context, data.layer, boundaryLineColor);
        }

        EnsureVertexBuffer(ref vertexPointSegments, vertexCount * 4);
        var pointWrite = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            var p = toCanvasVertex(vertices[i]);
            vertexPointSegments[pointWrite++] = p + new Vector2(-markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, -markerHalfSize);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, markerHalfSize);
        }

        if (pointWrite > 1)
            DrawProjectorHoverSegments(vertexPointSegments, pointWrite, pointColor);

        Handles.color = prev;
        Handles.EndGUI();
    }

    private static void DrawProjectorHoverSegments(Vector3[] buffer, int count, Color color)
    {
        if (buffer == null || count <= 1)
            return;

        Handles.color = color;
        if (count == buffer.Length)
        {
            Handles.DrawLines(buffer);
            return;
        }

        var exact = new Vector3[count];
        Array.Copy(buffer, exact, count);
        Handles.DrawLines(exact);
    }

    private static void DrawProjectorHoverLayerRect(ReanimatorPreviewContext context, LayerInfo layer, Color color)
    {
        if (context == null || context.Coordinates == null || layer == null)
            return;

        var rect = context.Coordinates.LayerToViewRect(layer);
        var p0 = new Vector3(rect.xMin, rect.yMin, 0f);
        var p1 = new Vector3(rect.xMax, rect.yMin, 0f);
        var p2 = new Vector3(rect.xMax, rect.yMax, 0f);
        var p3 = new Vector3(rect.xMin, rect.yMax, 0f);
        Handles.color = color;
        Handles.DrawAAPolyLine(3f, p0, p1, p2, p3, p0);
    }
}
#endif
