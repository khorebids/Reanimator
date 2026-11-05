#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal readonly struct ReanimatorFaceAlphaHole
{
    public ReanimatorFaceAlphaHole(
        Vector2 center,
        int area,
        RectInt bounds,
        Vector2[] contour,
        float perimeter)
    {
        Center = center;
        Area = area;
        Bounds = bounds;
        Contour = contour ?? Array.Empty<Vector2>();
        Perimeter = perimeter;
    }

    public Vector2 Center { get; }
    public int Area { get; }
    public RectInt Bounds { get; }
    public Vector2[] Contour { get; }
    public float Perimeter { get; }
}

internal sealed class ReanimatorFaceAlphaMaskAnalysis
{
    public int Width;
    public int Height;
    public Vector2 Center;
    public RectInt ContourBounds;
    public Vector2Int[] ContourPixels = Array.Empty<Vector2Int>();
    public bool[] ExteriorPixels = Array.Empty<bool>();
    public ReanimatorFaceAlphaHole[] Holes = Array.Empty<ReanimatorFaceAlphaHole>();
    public ReanimatorFaceAlphaHole LeftHole;
    public ReanimatorFaceAlphaHole RightHole;
    public bool HasContour => ContourPixels != null && ContourPixels.Length > 0;
    public bool HasLeftHole;
    public bool HasRightHole;
}

internal static partial class ReanimatorFaceAlphaMaskAlgorithms
{
    private static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(-1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(1, 1)
    };

    public static ReanimatorFaceAlphaMaskAnalysis Analyze(Color32[] pixels, int width, int height)
    {
        var result = new ReanimatorFaceAlphaMaskAnalysis
        {
            Width = Mathf.Max(0, width),
            Height = Mathf.Max(0, height)
        };
        if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height)
            return result;

        var pixelCount = width * height;
        var opaque = new bool[pixelCount];
        var hasOpaquePixel = false;
        for (var i = 0; i < pixelCount; i++)
        {
            opaque[i] = pixels[i].r >= 128;
            hasOpaquePixel |= opaque[i];
        }
        if (!hasOpaquePixel)
            return result;

        var queue = new int[pixelCount];
        var exterior = new bool[pixelCount];
        var queueEnd = 0;
        for (var x = 0; x < width; x++)
        {
            TryQueueTransparent(x, 0, width, height, opaque, exterior, queue, ref queueEnd);
            TryQueueTransparent(x, height - 1, width, height, opaque, exterior, queue, ref queueEnd);
        }
        for (var y = 1; y < height - 1; y++)
        {
            TryQueueTransparent(0, y, width, height, opaque, exterior, queue, ref queueEnd);
            TryQueueTransparent(width - 1, y, width, height, opaque, exterior, queue, ref queueEnd);
        }
        FloodQueuedTransparent(width, height, opaque, exterior, queue, queueEnd);
        result.ExteriorPixels = exterior;

        var contour = new List<Vector2Int>();
        var contourMinX = width;
        var contourMinY = height;
        var contourMaxX = -1;
        var contourMaxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (!opaque[index] || !TouchesExterior(x, y, width, height, exterior))
                    continue;

                contour.Add(new Vector2Int(x, y));
                contourMinX = Mathf.Min(contourMinX, x);
                contourMinY = Mathf.Min(contourMinY, y);
                contourMaxX = Mathf.Max(contourMaxX, x);
                contourMaxY = Mathf.Max(contourMaxY, y);
            }
        }

        if (contour.Count == 0)
            return result;

        result.ContourPixels = contour.ToArray();
        result.ContourBounds = new RectInt(
            contourMinX,
            contourMinY,
            contourMaxX - contourMinX + 1,
            contourMaxY - contourMinY + 1);
        result.Center = new Vector2(
            (contourMinX + contourMaxX + 1) * 0.5f,
            (contourMinY + contourMaxY + 1) * 0.5f);

        var holes = CollectHoles(opaque, exterior, width, height, queue);
        holes.Sort(CompareHolesBySize);
        result.Holes = holes.ToArray();
        for (var i = 0; i < holes.Count; i++)
        {
            var hole = holes[i];
            if (!result.HasLeftHole && hole.Center.x < result.Center.x)
            {
                result.LeftHole = hole;
                result.HasLeftHole = true;
            }
            else if (!result.HasRightHole && hole.Center.x > result.Center.x)
            {
                result.RightHole = hole;
                result.HasRightHole = true;
            }

            if (result.HasLeftHole && result.HasRightHole)
                break;
        }

        return result;
    }

    private static List<ReanimatorFaceAlphaHole> CollectHoles(
        bool[] opaque,
        bool[] exterior,
        int width,
        int height,
        int[] queue)
    {
        var holes = new List<ReanimatorFaceAlphaHole>();
        var visited = new bool[opaque.Length];
        var componentMask = new bool[opaque.Length];
        var componentPixels = new List<int>();
        for (var start = 0; start < opaque.Length; start++)
        {
            if (opaque[start] || exterior[start] || visited[start])
                continue;

            var queueStart = 0;
            var queueEnd = 0;
            queue[queueEnd++] = start;
            visited[start] = true;
            componentMask[start] = true;
            componentPixels.Clear();
            var area = 0;
            long sumX = 0;
            long sumY = 0;
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;
            while (queueStart < queueEnd)
            {
                var index = queue[queueStart++];
                componentPixels.Add(index);
                var x = index % width;
                var y = index / width;
                area++;
                sumX += x;
                sumY += y;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);

                for (var i = 0; i < NeighborOffsets.Length; i++)
                {
                    var nextX = x + NeighborOffsets[i].x;
                    var nextY = y + NeighborOffsets[i].y;
                    if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                        continue;
                    var next = nextY * width + nextX;
                    if (opaque[next] || exterior[next] || visited[next])
                        continue;
                    visited[next] = true;
                    componentMask[next] = true;
                    queue[queueEnd++] = next;
                }
            }

            var contour = BuildHoleContour(componentPixels, componentMask, width, height, out var perimeter);
            holes.Add(new ReanimatorFaceAlphaHole(
                new Vector2((float)sumX / area + 0.5f, (float)sumY / area + 0.5f),
                area,
                new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1),
                contour,
                perimeter));
            for (var i = 0; i < componentPixels.Count; i++)
                componentMask[componentPixels[i]] = false;
        }

        return holes;
    }

    private static void TryQueueTransparent(
        int x,
        int y,
        int width,
        int height,
        bool[] opaque,
        bool[] queued,
        int[] queue,
        ref int queueEnd)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;
        var index = y * width + x;
        if (opaque[index] || queued[index])
            return;
        queued[index] = true;
        queue[queueEnd++] = index;
    }

    private static void FloodQueuedTransparent(
        int width,
        int height,
        bool[] opaque,
        bool[] exterior,
        int[] queue,
        int queueEnd)
    {
        var queueStart = 0;
        while (queueStart < queueEnd)
        {
            var index = queue[queueStart++];
            var x = index % width;
            var y = index / width;
            for (var i = 0; i < NeighborOffsets.Length; i++)
            {
                var nextX = x + NeighborOffsets[i].x;
                var nextY = y + NeighborOffsets[i].y;
                if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                    continue;
                var next = nextY * width + nextX;
                if (opaque[next] || exterior[next])
                    continue;
                exterior[next] = true;
                queue[queueEnd++] = next;
            }
        }
    }

    private static bool TouchesExterior(int x, int y, int width, int height, bool[] exterior)
    {
        for (var i = 0; i < NeighborOffsets.Length; i++)
        {
            var nextX = x + NeighborOffsets[i].x;
            var nextY = y + NeighborOffsets[i].y;
            if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                return true;
            if (exterior[nextY * width + nextX])
                return true;
        }
        return false;
    }

    private static int CompareHolesBySize(ReanimatorFaceAlphaHole left, ReanimatorFaceAlphaHole right)
    {
        var areaCompare = right.Area.CompareTo(left.Area);
        if (areaCompare != 0)
            return areaCompare;
        var xCompare = left.Center.x.CompareTo(right.Center.x);
        return xCompare != 0 ? xCompare : left.Center.y.CompareTo(right.Center.y);
    }
}
#endif
