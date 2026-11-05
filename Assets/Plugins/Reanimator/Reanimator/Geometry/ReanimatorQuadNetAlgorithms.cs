#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorQuadNetAlgorithms
{
    private struct QuantizedPointKey : IEquatable<QuantizedPointKey>
    {
        public readonly int x;
        public readonly int y;

        public QuantizedPointKey(Vector2 point)
        {
            x = Mathf.RoundToInt(point.x * 1000f);
            y = Mathf.RoundToInt(point.y * 1000f);
        }

        public bool Equals(QuantizedPointKey other)
        {
            return x == other.x && y == other.y;
        }

        public override bool Equals(object obj)
        {
            return obj is QuantizedPointKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (x * 397) ^ y;
            }
        }
    }

    public static bool TryBuildContourFallback(
        IReadOnlyList<Vector2> contour,
        out Vector2[] localVertices,
        out int[] triangles,
        out int[] boundaryEdgePairs)
    {
        localVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();

        if (contour == null || contour.Count < 3)
            return false;

        var polygon = new List<Vector2>(contour);
        var tri = new List<int>(polygon.Count * 3);
        if (!ReanimatorTriangulationAlgorithms.TryTriangulateSimplePolygon(polygon, tri))
            return false;

        triangles = ReanimatorImporterGeometryAlgorithms.SanitizeTriangles(tri, polygon.Count);
        if (triangles.Length < 3)
            return false;

        localVertices = polygon.ToArray();

        var boundary = new List<int>(polygon.Count * 2);
        for (var i = 0; i < polygon.Count; i++)
        {
            boundary.Add(i);
            boundary.Add((i + 1) % polygon.Count);
        }

        boundaryEdgePairs = boundary.ToArray();
        return true;
    }

    public static float ComputeQuadNetStep(Rect bounds, double density)
    {
        var minDimension = Mathf.Max(8f, Mathf.Min(bounds.width, bounds.height));
        const double minDensity = 0.01d;
        const double pivotDensity = 1d;
        const double maxDensity = 5d;
        var clampedDensity = Math.Max(minDensity, Math.Min(maxDensity, density));

        float cellsOnMinAxis;
        if (clampedDensity >= pivotDensity)
        {
            var t = Mathf.Clamp01((float)((clampedDensity - pivotDensity) / (maxDensity - pivotDensity)));
            cellsOnMinAxis = Mathf.Lerp(8f, 40f, t);
        }
        else
        {
            var t = Mathf.Clamp01((float)((clampedDensity - minDensity) / (pivotDensity - minDensity)));
            cellsOnMinAxis = Mathf.Lerp(2f, 8f, t);
        }

        cellsOnMinAxis = Mathf.Clamp(cellsOnMinAxis, 2f, 96f);
        return Mathf.Max(1f, minDimension / cellsOnMinAxis);
    }

    public static bool TryGetQuadNetCellForTarget(
        Rect bounds,
        double density,
        Vector2 targetLocal,
        out Vector2 cellMin,
        out Vector2 cellMax)
    {
        return TryGetQuadNetCellForTarget(bounds, density, 0f, targetLocal, out cellMin, out cellMax);
    }

    public static bool TryGetQuadNetCellForTarget(
        Rect bounds,
        double density,
        float rotationDegrees,
        Vector2 targetLocal,
        out Vector2 cellMin,
        out Vector2 cellMax)
    {
        cellMin = Vector2.zero;
        cellMax = Vector2.zero;
        if (bounds.width <= 1e-4f || bounds.height <= 1e-4f)
            return false;

        var rotation = NormalizeQuadNetRotationDegrees(rotationDegrees);
        var pivot = bounds.center;
        var gridBounds = ComputeQuadNetGridBounds(bounds, rotation);
        var targetGrid = Mathf.Abs(rotation) <= 1e-4f
            ? targetLocal
            : RotateQuadNetPoint(targetLocal, pivot, -rotation);
        var step = ComputeQuadNetStep(gridBounds, density);
        if (step <= 1e-4f)
            return false;

        var x = SnapQuadNetCellMin(gridBounds.xMin, gridBounds.xMax, step, targetGrid.x);
        var y = SnapQuadNetCellMin(gridBounds.yMin, gridBounds.yMax, step, targetGrid.y);
        cellMin = new Vector2(x, y);
        cellMax = new Vector2(x + step, y + step);
        return true;
    }

    public static bool TryGetQuadNetCellCornersForTarget(
        Rect bounds,
        double density,
        float rotationDegrees,
        Vector2 targetLocal,
        Vector2[] corners)
    {
        if (corners == null || corners.Length < 4)
            return false;
        if (!TryGetQuadNetCellForTarget(bounds, density, rotationDegrees, targetLocal, out var cellMin, out var cellMax))
            return false;

        var pivot = bounds.center;
        var rotation = NormalizeQuadNetRotationDegrees(rotationDegrees);
        corners[0] = new Vector2(cellMin.x, cellMin.y);
        corners[1] = new Vector2(cellMax.x, cellMin.y);
        corners[2] = new Vector2(cellMax.x, cellMax.y);
        corners[3] = new Vector2(cellMin.x, cellMax.y);
        if (Mathf.Abs(rotation) <= 1e-4f)
            return true;

        for (var i = 0; i < 4; i++)
            corners[i] = RotateQuadNetPoint(corners[i], pivot, rotation);
        return true;
    }

    public static float NormalizeQuadNetRotationDegrees(float rotationDegrees)
    {
        if (float.IsNaN(rotationDegrees) || float.IsInfinity(rotationDegrees))
            return 0f;

        var normalized = rotationDegrees % 360f;
        if (normalized < 0f)
            normalized += 360f;
        return normalized;
    }

    public static Vector2 RotateQuadNetPoint(Vector2 point, Vector2 pivot, float degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        var sin = Mathf.Sin(radians);
        var cos = Mathf.Cos(radians);
        var p = point - pivot;
        return pivot + new Vector2(
            p.x * cos - p.y * sin,
            p.x * sin + p.y * cos);
    }

    public static Rect ComputeQuadNetGridBounds(Rect bounds, float rotationDegrees)
    {
        var rotation = NormalizeQuadNetRotationDegrees(rotationDegrees);
        return Mathf.Abs(rotation) <= 1e-4f
            ? bounds
            : ComputeRotatedRectBounds(bounds, bounds.center, -rotation);
    }

    private static Rect ComputeRotatedRectBounds(Rect rect, Vector2 pivot, float degrees)
    {
        var a = RotateQuadNetPoint(new Vector2(rect.xMin, rect.yMin), pivot, degrees);
        var b = RotateQuadNetPoint(new Vector2(rect.xMax, rect.yMin), pivot, degrees);
        var c = RotateQuadNetPoint(new Vector2(rect.xMax, rect.yMax), pivot, degrees);
        var d = RotateQuadNetPoint(new Vector2(rect.xMin, rect.yMax), pivot, degrees);
        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static float SnapQuadNetCellMin(float min, float max, float step, float target)
    {
        var gridStart = Mathf.Floor(min / step) * step;
        var index = Mathf.Floor((target - gridStart) / step);
        var cellMin = gridStart + index * step;
        while (cellMin + step <= min + 1e-4f)
            cellMin += step;
        while (cellMin >= max - 1e-4f)
            cellMin -= step;
        return cellMin;
    }

    public static bool TryBuildVertexRemap(IReadOnlyList<Vector2> sourceVertices, IReadOnlyList<Vector2> targetVertices, out int[] remap)
    {
        remap = null;
        if (sourceVertices == null || targetVertices == null || sourceVertices.Count == 0 || targetVertices.Count == 0)
            return false;

        remap = new int[sourceVertices.Count];
        for (var i = 0; i < remap.Length; i++)
            remap[i] = -1;

        var lookup = new Dictionary<QuantizedPointKey, int>(targetVertices.Count);
        for (var i = 0; i < targetVertices.Count; i++)
        {
            var key = new QuantizedPointKey(targetVertices[i]);
            if (!lookup.ContainsKey(key))
                lookup.Add(key, i);
        }

        const float tolerance = 0.25f;
        var toleranceSqr = tolerance * tolerance;
        for (var i = 0; i < sourceVertices.Count; i++)
        {
            var source = sourceVertices[i];
            var key = new QuantizedPointKey(source);
            if (lookup.TryGetValue(key, out var mapped))
            {
                remap[i] = mapped;
                continue;
            }

            var bestIndex = -1;
            var bestDist = float.PositiveInfinity;
            for (var j = 0; j < targetVertices.Count; j++)
            {
                var dist = (targetVertices[j] - source).sqrMagnitude;
                if (dist > toleranceSqr || dist >= bestDist)
                    continue;
                bestDist = dist;
                bestIndex = j;
            }

            if (bestIndex >= 0)
                remap[i] = bestIndex;
        }

        return true;
    }

    public static int[] RemapEdgeKeysToPairs(HashSet<ulong> sourceEdgeKeys, int[] remap, int vertexCount)
    {
        if (sourceEdgeKeys == null || sourceEdgeKeys.Count == 0 || remap == null || remap.Length == 0 || vertexCount <= 0)
            return Array.Empty<int>();

        var keys = new HashSet<ulong>();
        var pairs = new List<int>(sourceEdgeKeys.Count * 2);
        foreach (var key in sourceEdgeKeys)
        {
            var sourceA = (int)(key >> 32);
            var sourceB = (int)(key & 0xFFFFFFFFu);
            if (sourceA < 0 || sourceB < 0 || sourceA >= remap.Length || sourceB >= remap.Length)
                continue;

            var a = remap[sourceA];
            var b = remap[sourceB];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;

            var mappedKey = MakeEdgeKey(a, b);
            if (!keys.Add(mappedKey))
                continue;

            pairs.Add(a);
            pairs.Add(b);
        }

        return pairs.Count >= 2 ? pairs.ToArray() : Array.Empty<int>();
    }

    public static bool TrySelectPrimaryContour(
        IReadOnlyList<Vector2[]> paths,
        Vector2 scaleInv,
        Vector2 rectOffset,
        out List<Vector2> contour)
    {
        contour = null;
        if (paths == null || paths.Count == 0)
            return false;

        var bestAbsArea = 0f;
        for (var i = 0; i < paths.Count; i++)
        {
            if (!TryBuildPolygonLocalPoints(paths[i], scaleInv, rectOffset, out var polygon))
                continue;

            var area = Mathf.Abs(ReanimatorTriangulationAlgorithms.ComputeSignedArea(polygon));
            if (area <= bestAbsArea)
                continue;

            bestAbsArea = area;
            contour = polygon;
        }

        return contour != null && contour.Count >= 3;
    }

    public static bool IsPointOnAnyGridLine(Vector2 point, IReadOnlyList<float> xLines, IReadOnlyList<float> yLines)
    {
        const float epsilon = 1e-4f;
        if (xLines != null)
        {
            for (var i = 0; i < xLines.Count; i++)
            {
                if (Mathf.Abs(point.x - xLines[i]) <= epsilon)
                    return true;
            }
        }

        if (yLines != null)
        {
            for (var i = 0; i < yLines.Count; i++)
            {
                if (Mathf.Abs(point.y - yLines[i]) <= epsilon)
                    return true;
            }
        }

        return false;
    }

    public static bool IsPointInsideOrOnPolygon(Vector2 p, IReadOnlyList<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return false;

        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if (IsPointOnSegment(p, a, b, 0.05f))
                return true;
        }

        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];

            var intersects = ((a.y > p.y) != (b.y > p.y));
            if (!intersects)
                continue;

            var xAtY = a.x + (p.y - a.y) * (b.x - a.x) / Mathf.Max(1e-6f, b.y - a.y);
            if (xAtY >= p.x)
                inside = !inside;
        }

        return inside;
    }

    public static bool IsPointOnSegment(Vector2 p, Vector2 a, Vector2 b, float epsilon)
    {
        var ab = b - a;
        var ap = p - a;
        var cross = Mathf.Abs(ab.x * ap.y - ab.y * ap.x);
        if (cross > epsilon)
            return false;

        var dot = Vector2.Dot(ap, ab);
        if (dot < -epsilon)
            return false;

        var lenSqr = ab.sqrMagnitude;
        if (dot - lenSqr > epsilon)
            return false;

        return true;
    }

    public static ulong MakeEdgeKey(int a, int b)
    {
        var min = (uint)Mathf.Min(a, b);
        var max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    private static bool TryBuildPolygonLocalPoints(
        IReadOnlyList<Vector2> rawPath,
        Vector2 scaleInv,
        Vector2 rectOffset,
        out List<Vector2> polygon)
    {
        polygon = new List<Vector2>();
        if (rawPath == null || rawPath.Count < 3)
            return false;

        for (var i = 0; i < rawPath.Count; i++)
        {
            var p = Vector2.Scale(rawPath[i], scaleInv) + rectOffset;
            if (polygon.Count > 0 && (polygon[polygon.Count - 1] - p).sqrMagnitude <= 0.0001f)
                continue;
            polygon.Add(p);
        }

        while (polygon.Count > 1 && (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude <= 0.0001f)
            polygon.RemoveAt(polygon.Count - 1);

        return polygon.Count >= 3;
    }
}
#endif
