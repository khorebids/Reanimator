#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static BoneWeight BuildSingleBoneWeight(int spriteBoneIndex)
    {
        var safeIndex = Mathf.Max(0, spriteBoneIndex);
        return new BoneWeight
        {
            boneIndex0 = safeIndex,
            boneIndex1 = safeIndex,
            boneIndex2 = safeIndex,
            boneIndex3 = safeIndex,
            weight0 = 1f,
            weight1 = 0f,
            weight2 = 0f,
            weight3 = 0f
        };
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

            if (!adjacency.TryGetValue(a, out var aNeighbors))
            {
                aNeighbors = new List<int>(2);
                adjacency.Add(a, aNeighbors);
            }
            if (!adjacency.TryGetValue(b, out var bNeighbors))
            {
                bNeighbors = new List<int>(2);
                adjacency.Add(b, bNeighbors);
            }

            if (!aNeighbors.Contains(b))
                aNeighbors.Add(b);
            if (!bNeighbors.Contains(a))
                bNeighbors.Add(a);
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

    private static List<int> BuildFallbackBoundaryLoop(IReadOnlyList<Vector2> vertices, IReadOnlyList<bool> contourMask)
    {
        var contour = new List<int>();
        if (vertices == null || contourMask == null)
            return contour;
        for (var i = 0; i < contourMask.Count && i < vertices.Count; i++)
        {
            if (contourMask[i])
                contour.Add(i);
        }
        return BuildFallbackBoundaryLoop(vertices, contour);
    }

    private static List<int> BuildFallbackBoundaryLoop(IReadOnlyList<Vector2> vertices, IReadOnlyList<int> contourVertices)
    {
        var result = new List<int>();
        if (vertices == null || contourVertices == null || contourVertices.Count < 3)
            return result;

        var center = Vector2.zero;
        for (var i = 0; i < contourVertices.Count; i++)
            center += vertices[contourVertices[i]];
        center /= contourVertices.Count;

        result.AddRange(contourVertices);
        result.Sort((lhs, rhs) =>
        {
            var la = Mathf.Atan2(vertices[lhs].y - center.y, vertices[lhs].x - center.x);
            var ra = Mathf.Atan2(vertices[rhs].y - center.y, vertices[rhs].x - center.x);
            return la.CompareTo(ra);
        });

        for (var i = result.Count - 2; i >= 0; i--)
        {
            if (result[i] == result[i + 1])
                result.RemoveAt(i + 1);
        }

        return result;
    }

    private static List<int> BuildFilteredBoundaryLoop(IReadOnlyList<int> originalLoop, IReadOnlyList<bool> removeMask, IReadOnlyList<int> remap)
    {
        return ReanimatorEditGeometryAlgorithms.BuildFilteredBoundaryLoop(originalLoop, removeMask, remap);
    }

    private static int[] BuildBoundaryPairsFromLoop(IReadOnlyList<int> loop)
    {
        return ReanimatorEditGeometryAlgorithms.BuildBoundaryPairsFromLoop(loop);
    }

    private static int[] RemapTrianglesAfterCull(int[] oldTriangles, IReadOnlyList<bool> removeMask, IReadOnlyList<int> remap, int newVertexCount)
    {
        return SanitizeTriangles(ReanimatorEditGeometryAlgorithms.RemapTrianglesAfterCull(oldTriangles, removeMask, remap), newVertexCount);
    }

    private bool TryRetriangulateWithDirectFallback(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3 || data.boundaryEdgePairs == null || data.boundaryEdgePairs.Length < 6)
            return false;

        var boundaryEdges = new List<Vector2Int>(data.boundaryEdgePairs.Length / 2);
        for (var i = 0; i + 1 < data.boundaryEdgePairs.Length; i += 2)
        {
            var a = data.boundaryEdgePairs[i];
            var b = data.boundaryEdgePairs[i + 1];
            if (a < 0 || b < 0 || a >= data.vertices.Length || b >= data.vertices.Length || a == b)
                continue;
            boundaryEdges.Add(new Vector2Int(a, b));
        }
        if (boundaryEdges.Count < 3)
            return false;

        var sourceVertices = new List<Vector2>(data.vertices);
        if (!TryTriangulateWithUnityTessellation(sourceVertices, boundaryEdges, 0, out var triangulatedVertices, out var triangles, out _))
            return false;
        triangles = SanitizeTriangles(triangles, triangulatedVertices.Length);
        if (triangles.Length < 3 || triangulatedVertices.Length != data.vertices.Length)
            return false;
        if (!TryBuildVertexRemap(sourceVertices, triangulatedVertices, out var sourceToTri))
            return false;
        if (!TryBuildInversePermutation(sourceToTri, triangulatedVertices.Length, out var triToSource))
            return false;

        var remappedTriangles = new int[triangles.Length];
        for (var i = 0; i < triangles.Length; i++)
        {
            var triIndex = triangles[i];
            if (triIndex < 0 || triIndex >= triToSource.Length)
                return false;
            remappedTriangles[i] = triToSource[triIndex];
        }

        data.triangles = SanitizeTriangles(remappedTriangles, data.vertices.Length);
        return data.triangles.Length >= 3;
    }
}
#endif
