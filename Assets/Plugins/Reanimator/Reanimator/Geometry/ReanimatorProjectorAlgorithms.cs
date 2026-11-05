#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorProjectorAlgorithms
{
    private const float PointToleranceSqr = 0.0001f;

    public static Vector2[] ScaleVerticesAroundBoundsCenter(IReadOnlyList<Vector2> vertices, float scale)
    {
        if (vertices == null || vertices.Count == 0)
            return System.Array.Empty<Vector2>();

        var min = vertices[0];
        var max = vertices[0];
        for (var i = 1; i < vertices.Count; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }

        var center = (min + max) * 0.5f;
        var safeScale = Mathf.Max(0f, scale);
        var result = new Vector2[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
            result[i] = center + (vertices[i] - center) * safeScale;
        return result;
    }

    public static bool ContainsStretchPoint(IReadOnlyList<Vector2> vertices, Vector2 point)
    {
        if (vertices == null)
            return false;

        for (var i = 0; i < vertices.Count; i++)
        {
            if ((vertices[i] - point).sqrMagnitude <= PointToleranceSqr)
                return true;
        }

        return false;
    }

    public static bool HasUsableWeight(BoneWeight weight)
    {
        return weight.weight0 > 0.0001f ||
               weight.weight1 > 0.0001f ||
               weight.weight2 > 0.0001f ||
               weight.weight3 > 0.0001f;
    }

    public static bool TryGetSourceVertexWeight(
        IReadOnlyList<Vector2> sourceVertices,
        IReadOnlyList<BoneWeight> sourceWeights,
        int vertexIndex,
        out BoneWeight weight)
    {
        weight = default;
        if (sourceVertices == null || sourceWeights == null)
            return false;
        if (vertexIndex < 0 || vertexIndex >= sourceVertices.Count || vertexIndex >= sourceWeights.Count)
            return false;

        weight = sourceWeights[vertexIndex];
        return HasUsableWeight(weight);
    }

    public static List<int> BuildStretchBoundaryLoop(IReadOnlyList<Vector2> vertices)
    {
        var result = new List<int>();
        if (vertices == null || vertices.Count < 3)
            return result;

        var sorted = new List<int>(vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
            sorted.Add(i);
        sorted.Sort((lhs, rhs) => CompareHullPoints(vertices[lhs], vertices[rhs]));

        var hull = new List<int>(vertices.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var index = sorted[i];
            while (hull.Count >= 2 &&
                   HullCross(vertices[hull[hull.Count - 2]], vertices[hull[hull.Count - 1]], vertices[index]) <= 0.0001f)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(index);
        }

        var lowerCount = hull.Count;
        for (var i = sorted.Count - 2; i >= 0; i--)
        {
            var index = sorted[i];
            while (hull.Count > lowerCount &&
                   HullCross(vertices[hull[hull.Count - 2]], vertices[hull[hull.Count - 1]], vertices[index]) <= 0.0001f)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(index);
        }

        if (hull.Count > 1)
            hull.RemoveAt(hull.Count - 1);
        return hull.Count >= 3 ? hull : BuildFallbackLoop(vertices, sorted);
    }

    private static List<int> BuildFallbackLoop(IReadOnlyList<Vector2> vertices, IReadOnlyList<int> contourVertices)
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

    private static int CompareHullPoints(Vector2 lhs, Vector2 rhs)
    {
        var x = lhs.x.CompareTo(rhs.x);
        return x != 0 ? x : lhs.y.CompareTo(rhs.y);
    }

    private static float HullCross(Vector2 origin, Vector2 a, Vector2 b)
    {
        return (a.x - origin.x) * (b.y - origin.y) - (a.y - origin.y) * (b.x - origin.x);
    }
}
#endif
