#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static bool HasProjectorGeometry(LayerVertexData data)
    {
        return data != null &&
               data.vertices != null &&
               data.vertices.Length > 0 &&
               data.triangles != null &&
               data.triangles.Length >= 3;
    }

    private static bool GeometriesIntersect(LayerVertexData targetData, LayerVertexData sourceData)
    {
        if (!HasProjectorGeometry(targetData) || !HasProjectorGeometry(sourceData))
            return false;

        if (!BuildVertexBounds(targetData.vertices).Overlaps(BuildVertexBounds(sourceData.vertices)))
            return false;

        for (var i = 0; i < targetData.vertices.Length; i++)
        {
            if (IsPointInsideProjectorGeometry(sourceData, targetData.vertices[i]))
                return true;
        }

        for (var i = 0; i < sourceData.vertices.Length; i++)
        {
            if (IsPointInsideProjectorGeometry(targetData, sourceData.vertices[i]))
                return true;
        }

        return AnyProjectorEdgesIntersect(targetData, sourceData);
    }

    private static bool IsPointInsideProjectorGeometry(LayerVertexData data, Vector2 point)
    {
        return HasProjectorGeometry(data) &&
               TryFindContainingTriangle(
                   data.triangles,
                   data.vertices,
                   point,
                   out _,
                   out _,
                   out _,
                   out _,
                   out _,
                   out _,
                   out _,
                   out _);
    }

    private static bool IsTargetVertexCoveredByProjectorSource(
        LayerVertexData targetData,
        int targetVertexIndex,
        LayerVertexData sourceData)
    {
        if (!HasProjectorGeometry(targetData) || !HasProjectorGeometry(sourceData))
            return false;
        if (!IsValidVertexIndex(targetData.vertices, targetVertexIndex))
            return false;

        var point = targetData.vertices[targetVertexIndex];
        if (IsPointInsideProjectorGeometry(sourceData, point))
            return true;

        var targetTriangles = targetData.triangles;
        for (var i = 0; i + 2 < targetTriangles.Length; i += 3)
        {
            var ia = targetTriangles[i];
            var ib = targetTriangles[i + 1];
            var ic = targetTriangles[i + 2];
            if (ia != targetVertexIndex && ib != targetVertexIndex && ic != targetVertexIndex)
                continue;
            if (!IsValidVertexIndex(targetData.vertices, ia) ||
                !IsValidVertexIndex(targetData.vertices, ib) ||
                !IsValidVertexIndex(targetData.vertices, ic))
                continue;

            if (DoesTriangleIntersectProjectorGeometry(
                    sourceData,
                    targetData.vertices[ia],
                    targetData.vertices[ib],
                    targetData.vertices[ic]))
                return true;
        }

        return false;
    }

    private static bool IsTargetVertexInsideProjectorSource(
        LayerVertexData targetData,
        int targetVertexIndex,
        LayerVertexData sourceData)
    {
        if (!HasProjectorGeometry(targetData) || !HasProjectorGeometry(sourceData))
            return false;
        if (!IsValidVertexIndex(targetData.vertices, targetVertexIndex))
            return false;

        return IsPointInsideProjectorGeometry(sourceData, targetData.vertices[targetVertexIndex]);
    }

    private static bool DoesTriangleIntersectProjectorGeometry(
        LayerVertexData sourceData,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        if (!HasProjectorGeometry(sourceData))
            return false;
        if (IsPointInsideProjectorGeometry(sourceData, a) ||
            IsPointInsideProjectorGeometry(sourceData, b) ||
            IsPointInsideProjectorGeometry(sourceData, c))
            return true;

        var sourceVertices = sourceData.vertices;
        for (var i = 0; i < sourceVertices.Length; i++)
        {
            if (IsPointInTriangleInclusive(sourceVertices[i], a, b, c))
                return true;
        }

        var sourceEdges = GetProjectorEdgePairs(sourceData);
        for (var i = 0; i + 1 < sourceEdges.Length; i += 2)
        {
            var s0 = sourceEdges[i];
            var s1 = sourceEdges[i + 1];
            if (!IsValidVertexIndex(sourceVertices, s0) || !IsValidVertexIndex(sourceVertices, s1))
                continue;

            var p0 = sourceVertices[s0];
            var p1 = sourceVertices[s1];
            if (SegmentsIntersect(a, b, p0, p1) ||
                SegmentsIntersect(b, c, p0, p1) ||
                SegmentsIntersect(c, a, p0, p1))
                return true;
        }

        return false;
    }

    private static Rect BuildVertexBounds(Vector2[] vertices)
    {
        if (vertices == null || vertices.Length == 0)
            return new Rect();

        var min = vertices[0];
        var max = vertices[0];
        for (var i = 1; i < vertices.Length; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool AnyProjectorEdgesIntersect(LayerVertexData a, LayerVertexData b, IReadOnlyList<Vector2> bVerticesInASpace = null)
    {
        var aEdges = GetProjectorEdgePairs(a);
        var bEdges = GetProjectorEdgePairs(b);
        if (aEdges == null || bEdges == null || aEdges.Length < 2 || bEdges.Length < 2)
            return false;

        for (var ai = 0; ai + 1 < aEdges.Length; ai += 2)
        {
            var a0Index = aEdges[ai];
            var a1Index = aEdges[ai + 1];
            if (!IsValidVertexIndex(a.vertices, a0Index) || !IsValidVertexIndex(a.vertices, a1Index))
                continue;

            var a0 = a.vertices[a0Index];
            var a1 = a.vertices[a1Index];
            for (var bi = 0; bi + 1 < bEdges.Length; bi += 2)
            {
                var b0Index = bEdges[bi];
                var b1Index = bEdges[bi + 1];
                if (!IsValidVertexIndex(b.vertices, b0Index) || !IsValidVertexIndex(b.vertices, b1Index))
                    continue;

                var b0 = bVerticesInASpace != null && b0Index < bVerticesInASpace.Count
                    ? bVerticesInASpace[b0Index]
                    : b.vertices[b0Index];
                var b1 = bVerticesInASpace != null && b1Index < bVerticesInASpace.Count
                    ? bVerticesInASpace[b1Index]
                    : b.vertices[b1Index];
                if (SegmentsIntersect(a0, a1, b0, b1))
                    return true;
            }
        }

        return false;
    }

    private static int[] GetProjectorEdgePairs(LayerVertexData data)
    {
        if (data == null)
            return Array.Empty<int>();
        if (data.boundaryEdgePairs != null && data.boundaryEdgePairs.Length >= 2)
            return data.boundaryEdgePairs;
        if (data.allEdgePairs != null && data.allEdgePairs.Length >= 2)
            return data.allEdgePairs;
        return BuildTriangleEdgePairs(data.triangles);
    }

    private static int[] BuildTriangleEdgePairs(int[] triangles)
    {
        if (triangles == null || triangles.Length < 3)
            return Array.Empty<int>();

        var result = new int[(triangles.Length / 3) * 6];
        var write = 0;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            result[write++] = a;
            result[write++] = b;
            result[write++] = b;
            result[write++] = c;
            result[write++] = c;
            result[write++] = a;
        }

        return result;
    }

    private static bool IsValidVertexIndex(Vector2[] vertices, int index)
    {
        return vertices != null && index >= 0 && index < vertices.Length;
    }

    private static bool SegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        const float epsilon = 0.0001f;
        var d1 = Cross(a1 - a0, b0 - a0);
        var d2 = Cross(a1 - a0, b1 - a0);
        var d3 = Cross(b1 - b0, a0 - b0);
        var d4 = Cross(b1 - b0, a1 - b0);

        if (((d1 > epsilon && d2 < -epsilon) || (d1 < -epsilon && d2 > epsilon)) &&
            ((d3 > epsilon && d4 < -epsilon) || (d3 < -epsilon && d4 > epsilon)))
            return true;

        return Mathf.Abs(d1) <= epsilon && IsPointOnSegment(a0, a1, b0) ||
               Mathf.Abs(d2) <= epsilon && IsPointOnSegment(a0, a1, b1) ||
               Mathf.Abs(d3) <= epsilon && IsPointOnSegment(b0, b1, a0) ||
               Mathf.Abs(d4) <= epsilon && IsPointOnSegment(b0, b1, a1);
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool IsPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        return p.x >= Mathf.Min(a.x, b.x) - 0.0001f &&
               p.x <= Mathf.Max(a.x, b.x) + 0.0001f &&
               p.y >= Mathf.Min(a.y, b.y) - 0.0001f &&
               p.y <= Mathf.Max(a.y, b.y) + 0.0001f;
    }
}
#endif
