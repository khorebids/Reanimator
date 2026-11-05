#if UNITY_EDITOR
using System;
using System.Collections.Generic;

internal static class ReanimatorEditGeometryAlgorithms
{
    public static bool[] BuildRemoveMask(IReadOnlyList<int> selectedIndices, int vertexCount, out int removedCount)
    {
        removedCount = 0;
        var removeMask = new bool[Math.Max(0, vertexCount)];
        if (selectedIndices == null || vertexCount <= 0)
            return removeMask;

        for (var i = 0; i < selectedIndices.Count; i++)
        {
            var index = selectedIndices[i];
            if (index < 0 || index >= vertexCount || removeMask[index])
                continue;

            removeMask[index] = true;
            removedCount++;
        }

        return removeMask;
    }

    public static T[] RemoveElementAt<T>(T[] source, int index)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<T>();
        if (index < 0 || index >= source.Length)
            return source;
        if (source.Length == 1)
            return Array.Empty<T>();

        var result = new T[source.Length - 1];
        if (index > 0)
            Array.Copy(source, 0, result, 0, index);
        if (index < source.Length - 1)
            Array.Copy(source, index + 1, result, index, source.Length - index - 1);
        return result;
    }

    public static T[] CompactByRemoveMask<T>(IReadOnlyList<T> source, IReadOnlyList<bool> removeMask)
    {
        if (source == null || source.Count == 0)
            return Array.Empty<T>();

        var result = new List<T>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            if (removeMask != null && i < removeMask.Count && removeMask[i])
                continue;
            result.Add(source[i]);
        }

        return result.ToArray();
    }

    public static List<int> BuildFilteredBoundaryLoop(IReadOnlyList<int> originalLoop, IReadOnlyList<bool> removeMask, IReadOnlyList<int> remap)
    {
        var result = new List<int>();
        if (originalLoop == null || removeMask == null || remap == null)
            return result;

        for (var i = 0; i < originalLoop.Count; i++)
        {
            var oldIndex = originalLoop[i];
            if (oldIndex < 0 || oldIndex >= removeMask.Count || oldIndex >= remap.Count)
                continue;
            if (removeMask[oldIndex] || remap[oldIndex] < 0)
                continue;

            var mapped = remap[oldIndex];
            if (result.Count > 0 && result[result.Count - 1] == mapped)
                continue;
            result.Add(mapped);
        }

        if (result.Count > 1 && result[0] == result[result.Count - 1])
            result.RemoveAt(result.Count - 1);
        return result;
    }

    public static int[] BuildBoundaryPairsFromLoop(IReadOnlyList<int> loop)
    {
        if (loop == null || loop.Count < 3)
            return Array.Empty<int>();

        var pairs = new List<int>(loop.Count * 2);
        var uniqueEdges = new HashSet<ulong>();
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            if (a < 0 || b < 0 || a == b)
                continue;

            var key = MakeEdgeKey(a, b);
            if (!uniqueEdges.Add(key))
                continue;
            pairs.Add(a);
            pairs.Add(b);
        }

        return pairs.Count >= 6 ? pairs.ToArray() : Array.Empty<int>();
    }

    public static int[] RemapTrianglesAfterCull(int[] oldTriangles, IReadOnlyList<bool> removeMask, IReadOnlyList<int> remap)
    {
        if (oldTriangles == null || oldTriangles.Length < 3 || removeMask == null || remap == null)
            return Array.Empty<int>();

        var list = new List<int>(oldTriangles.Length);
        for (var i = 0; i + 2 < oldTriangles.Length; i += 3)
        {
            var a = oldTriangles[i];
            var b = oldTriangles[i + 1];
            var c = oldTriangles[i + 2];
            if (a < 0 || b < 0 || c < 0 ||
                a >= removeMask.Count || b >= removeMask.Count || c >= removeMask.Count ||
                a >= remap.Count || b >= remap.Count || c >= remap.Count)
                continue;
            if (removeMask[a] || removeMask[b] || removeMask[c])
                continue;

            a = remap[a];
            b = remap[b];
            c = remap[c];
            if (a < 0 || b < 0 || c < 0 || a == b || b == c || c == a)
                continue;

            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        return list.ToArray();
    }

    private static ulong MakeEdgeKey(int a, int b)
    {
        var min = a < b ? a : b;
        var max = a < b ? b : a;
        return ((ulong)(uint)min << 32) | (uint)max;
    }
}
#endif
