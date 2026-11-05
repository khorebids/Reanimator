#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static HashSet<ulong> BuildEdgeKeySet(int[] edgePairs, int vertexCount)
    {
        if (edgePairs == null || edgePairs.Length < 2 || vertexCount <= 0)
            return null;

        var set = new HashSet<ulong>();
        var pairCount = edgePairs.Length / 2;
        for (var i = 0; i < pairCount; i++)
        {
            var a = edgePairs[i * 2];
            var b = edgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;
            set.Add(MakeEdgeKey(a, b));
        }

        return set;
    }

    private static ulong MakeEdgeKey(int a, int b)
    {
        var min = (uint)Mathf.Min(a, b);
        var max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    private static int[] BuildEdgePairsFromTriangles(int[] triangles, int vertexCount)
    {
        if (triangles == null || triangles.Length < 3 || vertexCount <= 0)
            return Array.Empty<int>();

        var keys = new HashSet<ulong>();
        var edges = new List<int>(triangles.Length * 2);
        var limit = triangles.Length - (triangles.Length % 3);
        for (var i = 0; i < limit; i += 3)
        {
            AddTriangleEdge(triangles[i], triangles[i + 1], vertexCount, keys, edges);
            AddTriangleEdge(triangles[i + 1], triangles[i + 2], vertexCount, keys, edges);
            AddTriangleEdge(triangles[i + 2], triangles[i], vertexCount, keys, edges);
        }

        return edges.Count > 0 ? edges.ToArray() : Array.Empty<int>();
    }

    private static void AddTriangleEdge(int a, int b, int vertexCount, HashSet<ulong> keys, List<int> edges)
    {
        if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
            return;

        var key = MakeEdgeKey(a, b);
        if (!keys.Add(key))
            return;

        edges.Add(a);
        edges.Add(b);
    }

    private static void BuildEdgePairsAndBoundaryFromTriangles(
        int[] triangles,
        int vertexCount,
        out int[] allPairs,
        out int[] boundaryPairs)
    {
        allPairs = Array.Empty<int>();
        boundaryPairs = Array.Empty<int>();
        if (triangles == null || triangles.Length < 3 || vertexCount <= 0)
            return;

        var edgeCounts = new Dictionary<ulong, int>(triangles.Length);
        var limit = triangles.Length - (triangles.Length % 3);
        for (var i = 0; i < limit; i += 3)
        {
            CountTriangleEdge(triangles[i], triangles[i + 1], vertexCount, edgeCounts);
            CountTriangleEdge(triangles[i + 1], triangles[i + 2], vertexCount, edgeCounts);
            CountTriangleEdge(triangles[i + 2], triangles[i], vertexCount, edgeCounts);
        }

        if (edgeCounts.Count == 0)
            return;

        var all = new List<int>(edgeCounts.Count * 2);
        var boundary = new List<int>(edgeCounts.Count * 2);
        foreach (var kv in edgeCounts)
        {
            var a = (int)(kv.Key >> 32);
            var b = (int)(kv.Key & 0xFFFFFFFFu);
            all.Add(a);
            all.Add(b);
            if (kv.Value == 1)
            {
                boundary.Add(a);
                boundary.Add(b);
            }
        }

        allPairs = all.Count > 0 ? all.ToArray() : Array.Empty<int>();
        boundaryPairs = boundary.Count > 0 ? boundary.ToArray() : Array.Empty<int>();
    }

    private static void CountTriangleEdge(int a, int b, int vertexCount, Dictionary<ulong, int> edgeCounts)
    {
        if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
            return;

        var key = MakeEdgeKey(a, b);
        edgeCounts.TryGetValue(key, out var count);
        edgeCounts[key] = count + 1;
    }

    private string ResolvePreviewEdgePairs(
        LayerVertexData data,
        int vertexCount,
        out int[] allPairs,
        out int[] boundaryPairs,
        out HashSet<ulong> contourEdgeKeys)
    {
        allPairs = data != null ? data.allEdgePairs : Array.Empty<int>();
        boundaryPairs = data != null ? data.boundaryEdgePairs : Array.Empty<int>();
        contourEdgeKeys = BuildEdgeKeySet(boundaryPairs, vertexCount);

        var hasTriangles = data != null && data.triangles != null && data.triangles.Length >= 3;
        if (hasTriangles)
        {
            BuildEdgePairsAndBoundaryFromTriangles(data.triangles, vertexCount, out var triAllPairs, out var triBoundaryPairs);
            if (triAllPairs != null && triAllPairs.Length >= 2)
            {
                allPairs = triAllPairs;
                boundaryPairs = triBoundaryPairs;
                contourEdgeKeys = BuildEdgeKeySet(boundaryPairs, vertexCount);
                return "triangles";
            }
        }

        if (allPairs == null || allPairs.Length < 2)
        {
            allPairs = Array.Empty<int>();
            return "missing_edges";
        }

        var currentInner = CountInnerPairs(allPairs, contourEdgeKeys, vertexCount);
        return currentInner > 0 ? "all_edge_pairs" : "all_edge_pairs_no_inner";
    }

    private static int CountInnerPairs(int[] allPairs, int[] boundaryPairs, int vertexCount)
    {
        return CountInnerPairs(allPairs, BuildEdgeKeySet(boundaryPairs, vertexCount), vertexCount);
    }

    private static int CountInnerPairs(int[] allPairs, HashSet<ulong> boundarySet, int vertexCount)
    {
        if (allPairs == null || allPairs.Length < 2 || vertexCount <= 0)
            return 0;

        var innerSet = new HashSet<ulong>();
        var pairCount = allPairs.Length / 2;
        for (var i = 0; i < pairCount; i++)
        {
            var a = allPairs[i * 2];
            var b = allPairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;

            var key = MakeEdgeKey(a, b);
            if (boundarySet != null && boundarySet.Contains(key))
                continue;
            innerSet.Add(key);
        }

        return innerSet.Count;
    }

    private static void BuildEdgePairs(ushort[] triangles, int vertexCount, out int[] allEdgePairs, out int[] boundaryEdgePairs)
    {
        if (triangles == null || triangles.Length < 3 || vertexCount <= 0)
        {
            allEdgePairs = Array.Empty<int>();
            boundaryEdgePairs = Array.Empty<int>();
            return;
        }

        var edgeCounts = new Dictionary<ulong, int>(triangles.Length);
        for (var t = 0; t + 2 < triangles.Length; t += 3)
        {
            CountEdge(triangles[t], triangles[t + 1], vertexCount, edgeCounts);
            CountEdge(triangles[t + 1], triangles[t + 2], vertexCount, edgeCounts);
            CountEdge(triangles[t + 2], triangles[t], vertexCount, edgeCounts);
        }

        var all = new List<int>(edgeCounts.Count * 2);
        var boundary = new List<int>(edgeCounts.Count * 2);
        foreach (var kv in edgeCounts)
        {
            var a = (int)(kv.Key >> 32);
            var b = (int)(kv.Key & 0xFFFFFFFFu);
            all.Add(a);
            all.Add(b);
            if (kv.Value == 1)
            {
                boundary.Add(a);
                boundary.Add(b);
            }
        }

        allEdgePairs = all.Count == 0 ? Array.Empty<int>() : all.ToArray();
        boundaryEdgePairs = boundary.Count == 0 ? Array.Empty<int>() : boundary.ToArray();
    }

    private static void BuildEdgePairs(int[] triangles, int vertexCount, out int[] allEdgePairs, out int[] boundaryEdgePairs)
    {
        if (triangles == null || triangles.Length < 3 || vertexCount <= 0)
        {
            allEdgePairs = Array.Empty<int>();
            boundaryEdgePairs = Array.Empty<int>();
            return;
        }

        var edgeCounts = new Dictionary<ulong, int>(triangles.Length);
        for (var t = 0; t + 2 < triangles.Length; t += 3)
        {
            CountEdge(triangles[t], triangles[t + 1], vertexCount, edgeCounts);
            CountEdge(triangles[t + 1], triangles[t + 2], vertexCount, edgeCounts);
            CountEdge(triangles[t + 2], triangles[t], vertexCount, edgeCounts);
        }

        var all = new List<int>(edgeCounts.Count * 2);
        var boundary = new List<int>(edgeCounts.Count * 2);
        foreach (var kv in edgeCounts)
        {
            var a = (int)(kv.Key >> 32);
            var b = (int)(kv.Key & 0xFFFFFFFFu);
            all.Add(a);
            all.Add(b);
            if (kv.Value == 1)
            {
                boundary.Add(a);
                boundary.Add(b);
            }
        }

        allEdgePairs = all.Count == 0 ? Array.Empty<int>() : all.ToArray();
        boundaryEdgePairs = boundary.Count == 0 ? Array.Empty<int>() : boundary.ToArray();
    }

    private static void CountEdge(int a, int b, int vertexCount, IDictionary<ulong, int> edgeCounts)
    {
        if (a == b)
            return;
        if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
            return;

        var min = Mathf.Min(a, b);
        var max = Mathf.Max(a, b);
        var key = ((ulong)(uint)min << 32) | (uint)max;
        if (edgeCounts.TryGetValue(key, out var count))
            edgeCounts[key] = count + 1;
        else
            edgeCounts[key] = 1;
    }

    private static Vector2Int[] BuildImporterEdgesFromIndices(IReadOnlyList<int> indices, int vertexCount)
    {
        if (indices == null || indices.Count < 3 || vertexCount <= 0)
            return Array.Empty<Vector2Int>();

        var triangleArray = new int[indices.Count];
        for (var i = 0; i < indices.Count; i++)
            triangleArray[i] = indices[i];
        triangleArray = SanitizeTriangles(triangleArray, vertexCount);
        if (triangleArray.Length < 3)
            return Array.Empty<Vector2Int>();

        BuildEdgePairs(triangleArray, vertexCount, out _, out var boundaryEdgePairs);
        if (boundaryEdgePairs == null || boundaryEdgePairs.Length < 2)
            return Array.Empty<Vector2Int>();

        var count = boundaryEdgePairs.Length / 2;
        var result = new Vector2Int[count];
        for (var i = 0; i < count; i++)
            result[i] = new Vector2Int(boundaryEdgePairs[i * 2], boundaryEdgePairs[i * 2 + 1]);
        return result;
    }
}
#endif
