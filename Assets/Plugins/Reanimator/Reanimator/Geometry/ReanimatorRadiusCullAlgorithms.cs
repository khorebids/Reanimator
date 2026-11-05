#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorRadiusCullAlgorithms
{
    public static bool[] BuildContourMask(int[] boundaryEdgePairs, int vertexCount)
    {
        var result = new bool[Mathf.Max(0, vertexCount)];
        if (boundaryEdgePairs == null || boundaryEdgePairs.Length < 2 || vertexCount <= 0)
            return result;

        for (var i = 0; i + 1 < boundaryEdgePairs.Length; i += 2)
        {
            var a = boundaryEdgePairs[i];
            var b = boundaryEdgePairs[i + 1];
            if (a >= 0 && a < vertexCount)
                result[a] = true;
            if (b >= 0 && b < vertexCount)
                result[b] = true;
        }

        return result;
    }

    public static int CountTrue(IReadOnlyList<bool> values)
    {
        if (values == null)
            return 0;

        var count = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i])
                count++;
        }

        return count;
    }

    public static bool[] BuildRemoveMask(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<bool> contourMask,
        Vector2 center,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices,
        out int removedCount)
    {
        removedCount = 0;
        var vertexCount = vertices != null ? vertices.Count : 0;
        var removeMask = new bool[vertexCount];
        if (vertexCount == 0 || contourMask == null || radius <= 0f)
            return removeMask;

        var radiusSqr = radius * radius;
        for (var i = 0; i < vertexCount; i++)
        {
            var isContour = i < contourMask.Count && contourMask[i];
            if (isContour && !removeContourVertices)
                continue;
            if (!isContour && !removeInnerVertices)
                continue;
            if ((vertices[i] - center).sqrMagnitude > radiusSqr)
                continue;

            removeMask[i] = true;
            removedCount++;
        }

        return removeMask;
    }

    public static int[] BuildRemap(IReadOnlyList<bool> removeMask, out int keptCount)
    {
        keptCount = 0;
        var count = removeMask != null ? removeMask.Count : 0;
        var remap = new int[count];
        for (var i = 0; i < remap.Length; i++)
        {
            if (removeMask[i])
            {
                remap[i] = -1;
                continue;
            }

            remap[i] = keptCount;
            keptCount++;
        }

        return remap;
    }
}
#endif
