#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class ReanimatorVertexCullAlgorithms
{
    public static bool TryBuildClusters(
        IReadOnlyList<Vector2> vertices,
        float distance,
        out List<List<int>> groups,
        out int[] remap)
    {
        groups = new List<List<int>>();
        remap = Array.Empty<int>();
        var vertexCount = vertices != null ? vertices.Count : 0;
        if (vertexCount <= 0)
            return false;

        distance = Mathf.Max(0.0001f, distance);
        var distanceSqr = distance * distance;
        var dsu = new DisjointSet(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            for (var j = i + 1; j < vertexCount; j++)
            {
                if ((vertices[i] - vertices[j]).sqrMagnitude <= distanceSqr)
                    dsu.Union(i, j);
            }
        }

        var groupByRoot = new Dictionary<int, List<int>>();
        for (var i = 0; i < vertexCount; i++)
        {
            var root = dsu.Find(i);
            if (!groupByRoot.TryGetValue(root, out var group))
            {
                group = new List<int>();
                groupByRoot[root] = group;
            }
            group.Add(i);
        }

        remap = new int[vertexCount];
        for (var i = 0; i < remap.Length; i++)
            remap[i] = -1;

        foreach (var group in groupByRoot.Values.OrderBy(g => g[0]))
        {
            var groupIndex = groups.Count;
            groups.Add(group);
            for (var i = 0; i < group.Count; i++)
                remap[group[i]] = groupIndex;
        }

        return CountMergedGroups(groups) > 0;
    }

    public static int CountMergedGroups(IReadOnlyList<List<int>> groups)
    {
        var count = 0;
        if (groups == null)
            return 0;

        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i] != null && groups[i].Count > 1)
                count++;
        }

        return count;
    }

    public static Vector2 AveragePoints(IReadOnlyList<Vector2> points, IReadOnlyList<int> group)
    {
        var sum = Vector2.zero;
        var count = 0;
        if (points == null || group == null)
            return Vector2.zero;

        for (var i = 0; i < group.Count; i++)
        {
            var index = group[i];
            if (index < 0 || index >= points.Count)
                continue;
            sum += points[index];
            count++;
        }

        return count > 0 ? sum / count : Vector2.zero;
    }

    public static BoneWeight MergeBoneWeights(IReadOnlyList<BoneWeight> source, IReadOnlyList<int> group)
    {
        var accum = new Dictionary<int, float>();
        if (source != null && group != null)
        {
            for (var i = 0; i < group.Count; i++)
            {
                var index = group[i];
                if (index < 0 || index >= source.Count)
                    continue;
                AddWeight(accum, source[index].boneIndex0, source[index].weight0);
                AddWeight(accum, source[index].boneIndex1, source[index].weight1);
                AddWeight(accum, source[index].boneIndex2, source[index].weight2);
                AddWeight(accum, source[index].boneIndex3, source[index].weight3);
            }
        }

        var top = accum
            .Where(pair => pair.Key >= 0 && pair.Value > 0.0001f)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(4)
            .ToArray();
        var boneIndices = new[] { 0, 0, 0, 0 };
        var weights = new[] { 0f, 0f, 0f, 0f };
        var sum = 0f;
        for (var i = 0; i < top.Length; i++)
        {
            boneIndices[i] = top[i].Key;
            weights[i] = top[i].Value;
            sum += weights[i];
        }

        if (sum > 0.0001f)
        {
            var inv = 1f / sum;
            for (var i = 0; i < weights.Length; i++)
                weights[i] *= inv;
        }

        return new BoneWeight
        {
            boneIndex0 = boneIndices[0],
            boneIndex1 = boneIndices[1],
            boneIndex2 = boneIndices[2],
            boneIndex3 = boneIndices[3],
            weight0 = weights[0],
            weight1 = weights[1],
            weight2 = weights[2],
            weight3 = weights[3]
        };
    }

    public static int[] RemapTriangles(int[] triangles, IReadOnlyList<int> remap, int vertexCount)
    {
        if (triangles == null || triangles.Length < 3 || remap == null || vertexCount < 3)
            return Array.Empty<int>();

        var result = new List<int>(triangles.Length);
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = RemapIndex(triangles[i], remap);
            var b = RemapIndex(triangles[i + 1], remap);
            var c = RemapIndex(triangles[i + 2], remap);
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                continue;
            if (a == b || b == c || c == a)
                continue;
            result.Add(a);
            result.Add(b);
            result.Add(c);
        }

        return result.ToArray();
    }

    public static int[] RemapEdgePairs(int[] edgePairs, IReadOnlyList<int> remap, int vertexCount)
    {
        if (edgePairs == null || edgePairs.Length < 2 || remap == null)
            return Array.Empty<int>();

        var result = new List<int>(edgePairs.Length);
        var used = new HashSet<long>();
        for (var i = 0; i + 1 < edgePairs.Length; i += 2)
        {
            var a = RemapIndex(edgePairs[i], remap);
            var b = RemapIndex(edgePairs[i + 1], remap);
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;
            var min = Mathf.Min(a, b);
            var max = Mathf.Max(a, b);
            var key = ((long)min << 32) ^ (uint)max;
            if (!used.Add(key))
                continue;
            result.Add(a);
            result.Add(b);
        }

        return result.ToArray();
    }

    private static void AddWeight(Dictionary<int, float> accum, int boneIndex, float weight)
    {
        if (accum == null || boneIndex < 0 || weight <= 0.0001f)
            return;
        accum.TryGetValue(boneIndex, out var current);
        accum[boneIndex] = current + weight;
    }

    private static int RemapIndex(int index, IReadOnlyList<int> remap)
    {
        if (remap == null || index < 0 || index >= remap.Count)
            return -1;
        return remap[index];
    }

    private sealed class DisjointSet
    {
        private readonly int[] parent;
        private readonly int[] rank;

        public DisjointSet(int count)
        {
            parent = new int[count];
            rank = new int[count];
            for (var i = 0; i < count; i++)
                parent[i] = i;
        }

        public int Find(int value)
        {
            if (parent[value] != value)
                parent[value] = Find(parent[value]);
            return parent[value];
        }

        public void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == rootB)
                return;

            if (rank[rootA] < rank[rootB])
            {
                parent[rootA] = rootB;
                return;
            }

            if (rank[rootA] > rank[rootB])
            {
                parent[rootB] = rootA;
                return;
            }

            parent[rootB] = rootA;
            rank[rootA]++;
        }
    }
}
#endif
