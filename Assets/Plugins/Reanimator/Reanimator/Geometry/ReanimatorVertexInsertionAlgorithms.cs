#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorVertexInsertionAlgorithms
{
    public static bool SplitSingleTriangle(ref int[] triangles, int triangleStart, int ia, int ib, int ic, int newIndex)
    {
        if (triangles == null || triangleStart < 0 || triangleStart + 2 >= triangles.Length)
            return false;

        var rebuilt = new List<int>(triangles.Length + 6);
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            if (i != triangleStart)
            {
                rebuilt.Add(triangles[i]);
                rebuilt.Add(triangles[i + 1]);
                rebuilt.Add(triangles[i + 2]);
                continue;
            }

            rebuilt.Add(ia);
            rebuilt.Add(ib);
            rebuilt.Add(newIndex);

            rebuilt.Add(ib);
            rebuilt.Add(ic);
            rebuilt.Add(newIndex);

            rebuilt.Add(ic);
            rebuilt.Add(ia);
            rebuilt.Add(newIndex);
        }

        triangles = rebuilt.ToArray();
        return true;
    }

    public static bool SplitTrianglesOnEdge(
        ref int[] triangles,
        Vector2[] verticesBeforeInsert,
        Vector2 pointDoc,
        int newIndex,
        int edgeA,
        int edgeB)
    {
        if (triangles == null || triangles.Length < 3)
            return false;
        if (verticesBeforeInsert == null || edgeA < 0 || edgeB < 0 || edgeA >= verticesBeforeInsert.Length || edgeB >= verticesBeforeInsert.Length)
            return false;

        var rebuilt = new List<int>(triangles.Length + 6);
        var changed = false;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var t0 = triangles[i];
            var t1 = triangles[i + 1];
            var t2 = triangles[i + 2];
            if (!TryGetThirdVertexForEdge(t0, t1, t2, edgeA, edgeB, out var third) ||
                third < 0 ||
                third >= verticesBeforeInsert.Length)
            {
                rebuilt.Add(t0);
                rebuilt.Add(t1);
                rebuilt.Add(t2);
                continue;
            }

            changed = true;
            AppendEdgeSplitTriangles(rebuilt, t0, t1, t2, edgeA, edgeB, third, newIndex, verticesBeforeInsert, pointDoc);
        }

        if (!changed)
            return false;

        triangles = rebuilt.ToArray();
        return true;
    }

    public static bool TryGetThirdVertexForEdge(int t0, int t1, int t2, int edgeA, int edgeB, out int third)
    {
        third = -1;
        if ((t0 == edgeA && t1 == edgeB) || (t0 == edgeB && t1 == edgeA))
        {
            third = t2;
            return true;
        }

        if ((t1 == edgeA && t2 == edgeB) || (t1 == edgeB && t2 == edgeA))
        {
            third = t0;
            return true;
        }

        if ((t2 == edgeA && t0 == edgeB) || (t2 == edgeB && t0 == edgeA))
        {
            third = t1;
            return true;
        }

        return false;
    }

    public static bool TryInsertVertexIntoBoundaryLoop(
        int[] boundaryEdgePairs,
        Vector2[] verticesBeforeInsert,
        Vector2 pointDoc,
        int newIndex,
        out int[] updatedBoundaryPairs)
    {
        updatedBoundaryPairs = null;
        if (boundaryEdgePairs == null || boundaryEdgePairs.Length < 6)
            return false;
        if (verticesBeforeInsert == null || verticesBeforeInsert.Length < 3)
            return false;
        if (!TryBuildBoundaryLoopFromPairs(boundaryEdgePairs, verticesBeforeInsert.Length, out var boundaryLoop))
            return false;
        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;

        var bestSegmentIndex = -1;
        var bestDistance = float.PositiveInfinity;
        var lengthSum = 0f;
        var validLengthCount = 0;
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var aIndex = boundaryLoop[i];
            var bIndex = boundaryLoop[(i + 1) % boundaryLoop.Count];
            if (aIndex < 0 || bIndex < 0 || aIndex >= verticesBeforeInsert.Length || bIndex >= verticesBeforeInsert.Length)
                continue;

            var a = verticesBeforeInsert[aIndex];
            var b = verticesBeforeInsert[bIndex];
            var length = Vector2.Distance(a, b);
            if (length <= 1e-6f)
                continue;

            lengthSum += length;
            validLengthCount++;

            var distance = DistancePointToSegment(pointDoc, a, b);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestSegmentIndex = i;
        }

        if (bestSegmentIndex < 0)
            return false;

        var avgLength = validLengthCount > 0 ? lengthSum / validLengthCount : 0f;
        var snapDistance = Mathf.Max(0.75f, avgLength * 0.1f);
        if (bestDistance > snapDistance)
            return false;

        boundaryLoop.Insert(bestSegmentIndex + 1, newIndex);
        updatedBoundaryPairs = ReanimatorEditGeometryAlgorithms.BuildBoundaryPairsFromLoop(boundaryLoop);
        return updatedBoundaryPairs != null && updatedBoundaryPairs.Length >= 6;
    }

    public static void OptimizeTrianglesByEdgeFlips(Vector2[] vertices, int[] triangles, HashSet<ulong> protectedEdges)
    {
        if (vertices == null || triangles == null || vertices.Length < 4 || triangles.Length < 6)
            return;

        // One pass performs one flip because all cached edge adjacency becomes stale
        // immediately afterwards. Large projected meshes can require hundreds of
        // legal flips; the previous fixed limit of 12 left visible triangle fans.
        var maxPasses = Mathf.Clamp(triangles.Length * 4, 64, 50000);
        for (var pass = 0; pass < maxPasses; pass++)
        {
            var edges = BuildTriangleEdgeInfos(triangles, vertices.Length);
            var changed = false;
            foreach (var edge in edges.Values)
            {
                if (edge.useCount != 2 || edge.secondTriangle < 0)
                    continue;
                if (protectedEdges != null && protectedEdges.Contains(MakeEdgeKey(edge.a, edge.b)))
                    continue;
                if (!TryFlipTriangleEdgeForQuality(vertices, triangles, edge))
                    continue;

                changed = true;
                break;
            }

            if (!changed)
                break;
        }
    }

    public static float SignedTriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private static void AppendEdgeSplitTriangles(
        List<int> target,
        int t0,
        int t1,
        int t2,
        int edgeA,
        int edgeB,
        int third,
        int newIndex,
        Vector2[] verticesBeforeInsert,
        Vector2 newPoint)
    {
        var a = verticesBeforeInsert[edgeA];
        var b = verticesBeforeInsert[edgeB];
        var c = verticesBeforeInsert[third];
        var originalSign = SignedTriangleArea(verticesBeforeInsert[t0], verticesBeforeInsert[t1], verticesBeforeInsert[t2]);

        var option1a = SignedTriangleArea(a, newPoint, c);
        var option1b = SignedTriangleArea(newPoint, b, c);
        var option1Valid = IsWindingCompatible(originalSign, option1a) && IsWindingCompatible(originalSign, option1b);

        if (option1Valid)
        {
            target.Add(edgeA);
            target.Add(newIndex);
            target.Add(third);
            target.Add(newIndex);
            target.Add(edgeB);
            target.Add(third);
            return;
        }

        target.Add(edgeB);
        target.Add(newIndex);
        target.Add(third);
        target.Add(newIndex);
        target.Add(edgeA);
        target.Add(third);
    }

    private static bool TryBuildBoundaryLoopFromPairs(int[] boundaryEdgePairs, int vertexCount, out List<int> loop)
    {
        loop = new List<int>(Mathf.Max(3, vertexCount));
        if (boundaryEdgePairs == null || boundaryEdgePairs.Length < 6 || vertexCount < 3)
            return false;

        var adjacency = new Dictionary<int, List<int>>();
        for (var i = 0; i + 1 < boundaryEdgePairs.Length; i += 2)
        {
            var a = boundaryEdgePairs[i];
            var b = boundaryEdgePairs[i + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;

            AddAdjacent(adjacency, a, b);
            AddAdjacent(adjacency, b, a);
        }

        if (adjacency.Count < 3)
            return false;

        var start = -1;
        foreach (var pair in adjacency)
        {
            if (pair.Value.Count < 2)
                continue;
            if (start < 0 || pair.Key < start)
                start = pair.Key;
        }
        if (start < 0)
            return false;

        var visitedEdges = new HashSet<ulong>();
        var current = start;
        var previous = -1;
        var guard = 0;
        while (guard++ < vertexCount * 4)
        {
            loop.Add(current);
            if (!adjacency.TryGetValue(current, out var neighbors) || neighbors.Count == 0)
                return false;

            var next = -1;
            for (var i = 0; i < neighbors.Count; i++)
            {
                var candidate = neighbors[i];
                if (candidate == previous && neighbors.Count > 1)
                    continue;
                var edgeKey = MakeEdgeKey(current, candidate);
                if (visitedEdges.Contains(edgeKey) && candidate != start)
                    continue;
                next = candidate;
                break;
            }

            if (next < 0)
                return false;

            var walkKey = MakeEdgeKey(current, next);
            if (!visitedEdges.Add(walkKey) && next != start)
                return false;

            previous = current;
            current = next;
            if (current == start)
                break;
        }

        if (current != start || loop.Count < 3)
            return false;
        return true;
    }

    private static void AddAdjacent(Dictionary<int, List<int>> adjacency, int key, int value)
    {
        if (!adjacency.TryGetValue(key, out var list))
        {
            list = new List<int>(2);
            adjacency.Add(key, list);
        }

        if (!list.Contains(value))
            list.Add(value);
    }

    private static Dictionary<ulong, TriangleEdgeInfo> BuildTriangleEdgeInfos(int[] triangles, int vertexCount)
    {
        var result = new Dictionary<ulong, TriangleEdgeInfo>();
        if (triangles == null || vertexCount <= 0)
            return result;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if (!IsValidTriangleIndex(a, vertexCount) ||
                !IsValidTriangleIndex(b, vertexCount) ||
                !IsValidTriangleIndex(c, vertexCount) ||
                a == b || b == c || c == a)
                continue;

            AddTriangleEdgeInfo(result, a, b, c, i / 3);
            AddTriangleEdgeInfo(result, b, c, a, i / 3);
            AddTriangleEdgeInfo(result, c, a, b, i / 3);
        }

        return result;
    }

    private static void AddTriangleEdgeInfo(Dictionary<ulong, TriangleEdgeInfo> edges, int a, int b, int opposite, int triangleIndex)
    {
        if (edges == null || a < 0 || b < 0 || opposite < 0 || a == b || a == opposite || b == opposite)
            return;

        var key = MakeEdgeKey(a, b);
        if (edges.TryGetValue(key, out var edge))
        {
            edge.useCount++;
            if (edge.secondTriangle < 0)
            {
                edge.secondTriangle = triangleIndex;
                edge.secondOpposite = opposite;
            }
            edges[key] = edge;
            return;
        }

        edges[key] = new TriangleEdgeInfo
        {
            a = a,
            b = b,
            firstTriangle = triangleIndex,
            firstOpposite = opposite,
            secondTriangle = -1,
            secondOpposite = -1,
            useCount = 1
        };
    }

    private static bool TryFlipTriangleEdgeForQuality(Vector2[] vertices, int[] triangles, TriangleEdgeInfo edge)
    {
        var a = edge.a;
        var b = edge.b;
        var c = edge.firstOpposite;
        var d = edge.secondOpposite;
        if (!IsValidTriangleIndex(a, vertices.Length) ||
            !IsValidTriangleIndex(b, vertices.Length) ||
            !IsValidTriangleIndex(c, vertices.Length) ||
            !IsValidTriangleIndex(d, vertices.Length))
            return false;
        if (a == b || a == c || a == d || b == c || b == d || c == d)
            return false;
        if (HasTriangleEdge(triangles, c, d))
            return false;
        if (!IsConvexFlipQuad(vertices[a], vertices[b], vertices[c], vertices[d]))
            return false;

        var beforeQuality = Mathf.Min(
            TriangleMinAngle(vertices[a], vertices[b], vertices[c]),
            TriangleMinAngle(vertices[b], vertices[a], vertices[d]));
        var afterQuality = Mathf.Min(
            TriangleMinAngle(vertices[c], vertices[d], vertices[a]),
            TriangleMinAngle(vertices[d], vertices[c], vertices[b]));
        if (afterQuality <= beforeQuality + 0.001f)
            return false;

        var firstOffset = edge.firstTriangle * 3;
        var secondOffset = edge.secondTriangle * 3;
        if (firstOffset < 0 || firstOffset + 2 >= triangles.Length ||
            secondOffset < 0 || secondOffset + 2 >= triangles.Length)
            return false;

        var firstSign = SignedTriangleArea(vertices[triangles[firstOffset]], vertices[triangles[firstOffset + 1]], vertices[triangles[firstOffset + 2]]);
        var secondSign = SignedTriangleArea(vertices[triangles[secondOffset]], vertices[triangles[secondOffset + 1]], vertices[triangles[secondOffset + 2]]);

        WriteTriangleWithWinding(vertices, triangles, firstOffset, c, d, a, firstSign);
        WriteTriangleWithWinding(vertices, triangles, secondOffset, d, c, b, secondSign);
        return true;
    }

    private static bool IsConvexFlipQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var sideC = SignedTriangleArea(a, b, c);
        var sideD = SignedTriangleArea(a, b, d);
        if (sideC * sideD >= -1e-6f)
            return false;

        var sideA = SignedTriangleArea(c, d, a);
        var sideB = SignedTriangleArea(c, d, b);
        return sideA * sideB < -1e-6f;
    }

    private static float TriangleMinAngle(Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ba = a - b;
        var bc = c - b;
        var ca = a - c;
        var cb = b - c;
        return Mathf.Min(AngleBetween(ab, ac), Mathf.Min(AngleBetween(ba, bc), AngleBetween(ca, cb)));
    }

    private static float AngleBetween(Vector2 a, Vector2 b)
    {
        var denom = Mathf.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
        if (denom <= 1e-8f)
            return 0f;

        return Mathf.Acos(Mathf.Clamp(Vector2.Dot(a, b) / denom, -1f, 1f));
    }

    private static void WriteTriangleWithWinding(Vector2[] vertices, int[] triangles, int offset, int a, int b, int c, float expectedSign)
    {
        var sign = SignedTriangleArea(vertices[a], vertices[b], vertices[c]);
        if (!IsWindingCompatible(expectedSign, sign))
        {
            triangles[offset] = a;
            triangles[offset + 1] = c;
            triangles[offset + 2] = b;
            return;
        }

        triangles[offset] = a;
        triangles[offset + 1] = b;
        triangles[offset + 2] = c;
    }

    private static bool IsWindingCompatible(float expectedSign, float sign)
    {
        const float eps = 1e-6f;
        if (Mathf.Abs(expectedSign) <= eps || Mathf.Abs(sign) <= eps)
            return true;
        return Mathf.Sign(expectedSign) == Mathf.Sign(sign);
    }

    private static bool HasTriangleEdge(int[] triangles, int a, int b)
    {
        if (triangles == null || a < 0 || b < 0 || a == b)
            return false;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            if (TriangleContainsEdge(triangles[i], triangles[i + 1], triangles[i + 2], a, b))
                return true;
        }

        return false;
    }

    private static bool TriangleContainsEdge(int t0, int t1, int t2, int a, int b)
    {
        return ((t0 == a || t1 == a || t2 == a) && (t0 == b || t1 == b || t2 == b));
    }

    private static bool IsValidTriangleIndex(int index, int vertexCount)
    {
        return index >= 0 && index < vertexCount;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var denom = ab.sqrMagnitude;
        if (denom <= 1e-8f)
            return Vector2.Distance(p, a);

        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
        return Vector2.Distance(p, a + ab * t);
    }

    private static ulong MakeEdgeKey(int a, int b)
    {
        var min = a < b ? a : b;
        var max = a < b ? b : a;
        return ((ulong)(uint)min << 32) | (uint)max;
    }

    private struct TriangleEdgeInfo
    {
        public int a;
        public int b;
        public int firstTriangle;
        public int firstOpposite;
        public int secondTriangle;
        public int secondOpposite;
        public int useCount;
    }
}
#endif
