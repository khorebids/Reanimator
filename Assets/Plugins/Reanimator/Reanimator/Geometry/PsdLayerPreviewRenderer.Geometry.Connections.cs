#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static void BuildAddVertexStyleConnections(
        LayerVertexData data,
        Vector2[] vertices,
        Vector2 pointDoc,
        List<int> result)
    {
        const int maxLinks = 8;
        result.Clear();
        if (vertices == null || vertices.Length == 0)
            return;

        if (TryBuildTriangleSplitConnections(data, vertices, pointDoc, result))
            return;

        var nearest = FindNearestVertexIndex(pointDoc, vertices);
        if (nearest < 0)
            return;

        AddUniqueIndex(result, nearest);
        AddIncidentNeighbors(data, nearest, result);

        var maxDistance = EstimateLocalConnectionDistance(data, vertices, nearest);
        var maxDistanceSqr = maxDistance * maxDistance;
        for (var i = 0; i < vertices.Length; i++)
        {
            if (i == nearest)
                continue;
            if ((vertices[i] - pointDoc).sqrMagnitude > maxDistanceSqr)
                continue;
            AddUniqueIndex(result, i);
            if (result.Count >= maxLinks)
                break;
        }

        EnsureMinimumNearestLinks(pointDoc, vertices, result, Mathf.Min(4, maxLinks));
    }

    private static bool TryBuildTriangleSplitConnections(
        LayerVertexData data,
        Vector2[] vertices,
        Vector2 pointDoc,
        List<int> result)
    {
        if (data == null || data.triangles == null || data.triangles.Length < 3)
            return false;

        if (!TryFindContainingTriangle(
                data.triangles,
                vertices,
                pointDoc,
                out var triangleStart,
                out var ia,
                out var ib,
                out var ic,
                out var dAB,
                out var dBC,
                out var dCA,
                out var avgEdgeLength))
            return false;

        var minDistanceToEdge = Mathf.Min(dAB, Mathf.Min(dBC, dCA));
        var edgeSnap = Mathf.Max(0.45f, avgEdgeLength * 0.1f);
        if (minDistanceToEdge <= edgeSnap)
        {
            var edgeA = ia;
            var edgeB = ib;
            var opposite = ic;
            if (dBC <= dAB && dBC <= dCA)
            {
                edgeA = ib;
                edgeB = ic;
                opposite = ia;
            }
            else if (dCA <= dAB && dCA <= dBC)
            {
                edgeA = ic;
                edgeB = ia;
                opposite = ib;
            }

            AddUniqueIndex(result, edgeA);
            AddUniqueIndex(result, edgeB);
            AddUniqueIndex(result, opposite);
            AddAdjacentEdgeOpposites(data.triangles, triangleStart, edgeA, edgeB, result);
        }
        else
        {
            AddUniqueIndex(result, ia);
            AddUniqueIndex(result, ib);
            AddUniqueIndex(result, ic);
        }

        return result.Count > 0;
    }

    private static bool TryFindContainingTriangle(
        int[] triangles,
        Vector2[] vertices,
        Vector2 point,
        out int triangleStart,
        out int ia,
        out int ib,
        out int ic,
        out float dAB,
        out float dBC,
        out float dCA,
        out float avgEdgeLength)
    {
        triangleStart = -1;
        ia = ib = ic = -1;
        dAB = dBC = dCA = avgEdgeLength = 0f;

        var hasResult = false;
        var bestScore = float.NegativeInfinity;
        const float epsilon = 0.001f;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var aIndex = triangles[i];
            var bIndex = triangles[i + 1];
            var cIndex = triangles[i + 2];
            if (aIndex < 0 || bIndex < 0 || cIndex < 0)
                continue;
            if (aIndex >= vertices.Length || bIndex >= vertices.Length || cIndex >= vertices.Length)
                continue;

            var a = vertices[aIndex];
            var b = vertices[bIndex];
            var c = vertices[cIndex];
            if (!TryGetBarycentric(point, a, b, c, out var wa, out var wb, out var wc))
                continue;
            if (wa < -epsilon || wb < -epsilon || wc < -epsilon)
                continue;

            var score = Mathf.Min(wa, Mathf.Min(wb, wc));
            if (hasResult && score < bestScore)
                continue;

            hasResult = true;
            bestScore = score;
            triangleStart = i;
            ia = aIndex;
            ib = bIndex;
            ic = cIndex;
            dAB = DistancePointToSegment(point, a, b);
            dBC = DistancePointToSegment(point, b, c);
            dCA = DistancePointToSegment(point, c, a);
            avgEdgeLength = (Vector2.Distance(a, b) + Vector2.Distance(b, c) + Vector2.Distance(c, a)) / 3f;
        }

        return hasResult;
    }

    private static bool TryGetBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float wa, out float wb, out float wc)
    {
        wa = wb = wc = 0f;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var den = v0.x * v1.y - v1.x * v0.y;
        if (Mathf.Abs(den) <= 1e-8f)
            return false;

        wb = (v2.x * v1.y - v1.x * v2.y) / den;
        wc = (v0.x * v2.y - v2.x * v0.y) / den;
        wa = 1f - wb - wc;
        return true;
    }

    private static void AddAdjacentEdgeOpposites(
        int[] triangles,
        int sourceTriangleStart,
        int edgeA,
        int edgeB,
        List<int> result)
    {
        if (triangles == null || triangles.Length < 3)
            return;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            if (i == sourceTriangleStart)
                continue;

            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            var hasA = a == edgeA || b == edgeA || c == edgeA;
            var hasB = a == edgeB || b == edgeB || c == edgeB;
            if (!hasA || !hasB)
                continue;

            var opposite = a;
            if (opposite == edgeA || opposite == edgeB)
                opposite = b;
            if (opposite == edgeA || opposite == edgeB)
                opposite = c;

            AddUniqueIndex(result, opposite);
        }
    }

    private static void AddIncidentNeighbors(LayerVertexData data, int vertexIndex, List<int> result)
    {
        if (data == null || data.allEdgePairs == null || data.allEdgePairs.Length < 2 || vertexIndex < 0)
            return;

        for (var i = 0; i + 1 < data.allEdgePairs.Length; i += 2)
        {
            var a = data.allEdgePairs[i];
            var b = data.allEdgePairs[i + 1];
            if (a == vertexIndex)
                AddUniqueIndex(result, b);
            else if (b == vertexIndex)
                AddUniqueIndex(result, a);
        }
    }

    private static float EstimateLocalConnectionDistance(LayerVertexData data, Vector2[] vertices, int centerIndex)
    {
        if (centerIndex < 0 || centerIndex >= vertices.Length)
            return 2f;

        var sum = 0f;
        var count = 0;
        if (data != null && data.allEdgePairs != null && data.allEdgePairs.Length >= 2)
        {
            for (var i = 0; i + 1 < data.allEdgePairs.Length; i += 2)
            {
                var a = data.allEdgePairs[i];
                var b = data.allEdgePairs[i + 1];
                if (a != centerIndex && b != centerIndex)
                    continue;
                var other = a == centerIndex ? b : a;
                if (other < 0 || other >= vertices.Length)
                    continue;
                sum += Vector2.Distance(vertices[centerIndex], vertices[other]);
                count++;
            }
        }

        if (count > 0)
            return Mathf.Max(1f, (sum / count) * 1.6f);

        var nearestDist = float.PositiveInfinity;
        for (var i = 0; i < vertices.Length; i++)
        {
            if (i == centerIndex)
                continue;
            var d = Vector2.Distance(vertices[centerIndex], vertices[i]);
            if (d < nearestDist)
                nearestDist = d;
        }

        if (float.IsInfinity(nearestDist))
            return 2f;
        return Mathf.Max(1f, nearestDist * 2.25f);
    }

    private static int FindNearestVertexIndex(Vector2 point, Vector2[] vertices)
    {
        if (vertices == null || vertices.Length == 0)
            return -1;

        var bestIndex = 0;
        var bestDist = (vertices[0] - point).sqrMagnitude;
        for (var i = 1; i < vertices.Length; i++)
        {
            var dist = (vertices[i] - point).sqrMagnitude;
            if (dist >= bestDist)
                continue;
            bestDist = dist;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static void EnsureMinimumNearestLinks(Vector2 point, Vector2[] vertices, List<int> result, int minCount)
    {
        while (result.Count < minCount)
        {
            var next = FindNearestNotInList(point, vertices, result);
            if (next < 0)
                break;
            result.Add(next);
        }
    }

    private static int FindNearestNotInList(Vector2 point, Vector2[] vertices, List<int> exclude)
    {
        var bestIndex = -1;
        var bestDist = float.PositiveInfinity;
        for (var i = 0; i < vertices.Length; i++)
        {
            if (ContainsIndex(exclude, i))
                continue;
            var dist = (vertices[i] - point).sqrMagnitude;
            if (dist >= bestDist)
                continue;
            bestDist = dist;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static void AddUniqueIndex(List<int> list, int value)
    {
        if (value < 0 || ContainsIndex(list, value))
            return;
        list.Add(value);
    }

    private static bool ContainsIndex(List<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
                return true;
        }
        return false;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.sqrMagnitude > 0f ? Vector2.Dot(p - a, ab) / ab.sqrMagnitude : 0f;
        t = Mathf.Clamp01(t);
        var proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }
}
#endif
