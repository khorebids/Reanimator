#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static string quadNetLastError;

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

    private static bool TryGenerateQuadNetGeometryLocalImpl(
        ITextureDataProvider textureDataProvider,
        LayerSpriteBinding binding,
        int outlineDetail,
        int alphaTolerance,
        double density,
        float rotationDegrees,
        out Vector2[] localVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs)
    {
        localVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        quadNetLastError = null;

        if (textureDataProvider == null || textureDataProvider.texture == null ||
            binding.rect.width <= 0f || binding.rect.height <= 0f)
        {
            quadNetLastError = "QuadNet: invalid texture data";
            return false;
        }

        textureDataProvider.GetTextureActualWidthAndHeight(out var actualWidth, out var actualHeight);
        if (actualWidth <= 0 || actualHeight <= 0)
        {
            quadNetLastError = "QuadNet: texture size unavailable";
            return false;
        }

        var cappedTexture = textureDataProvider.texture;
        var scale = new Vector2(cappedTexture.width / (float)actualWidth, cappedTexture.height / (float)actualHeight);
        var scaleInv = new Vector2(1f / Mathf.Max(1e-4f, scale.x), 1f / Mathf.Max(1e-4f, scale.y));
        var rectOffset = binding.rect.size * 0.5f;
        var scaledRect = binding.rect;
        scaledRect.min = Vector2.Scale(scaledRect.min, scale);
        scaledRect.max = Vector2.Scale(scaledRect.max, scale);

        var detail = Mathf.Clamp01(outlineDetail / 100f);
        var alpha = (byte)Mathf.Clamp(alphaTolerance, 0, 254);
        if (!TryGenerateOutlineWithAnimationModule(textureDataProvider, cappedTexture, scaledRect, detail, alpha, false, out var paths))
        {
            quadNetLastError = "QuadNet: failed to generate outline";
            return false;
        }

        if (!TrySelectPrimaryContour(paths, scaleInv, rectOffset, out var contour))
        {
            quadNetLastError = "QuadNet: failed to resolve primary contour";
            return false;
        }

        TryBuildContourContainmentMesh(contour, out var contourContainmentVertices, out var contourContainmentTriangles);

        if (!TryBuildQuadNetPointCloud(
                contour,
                new Rect(0f, 0f, binding.rect.width, binding.rect.height),
                density,
                rotationDegrees,
                contourContainmentVertices,
                contourContainmentTriangles,
                out var sourceVertices,
                out var boundaryEdges,
                out var boundaryEdgeKeys,
                out var desiredEdgeKeys))
        {
            quadNetLastError = "QuadNet: failed to build grid point cloud";
            return false;
        }

        if (!TryTriangulateWithUnityTessellation(sourceVertices, boundaryEdges, 0, out localVertices, out triangles, out _))
        {
            if (!TryBuildContourFallback(contour, out localVertices, out triangles, out boundaryEdgePairs))
            {
                quadNetLastError = "QuadNet: triangulation failed (main + fallback)";
                return false;
            }

            triangles = SanitizeTriangles(triangles, localVertices.Length);
            if (triangles.Length < 3)
            {
                quadNetLastError = "QuadNet: fallback produced empty triangles";
                return false;
            }

            BuildEdgePairs(triangles, localVertices.Length, out allEdgePairs, out var fallbackBoundary);
            if (boundaryEdgePairs == null || boundaryEdgePairs.Length < 2)
                boundaryEdgePairs = fallbackBoundary;
            quadNetLastError = null;
            return allEdgePairs.Length >= 2 && boundaryEdgePairs.Length >= 2;
        }

        triangles = SanitizeTriangles(triangles, localVertices.Length);
        if (triangles.Length < 3)
        {
            quadNetLastError = "QuadNet: triangulation produced empty triangles";
            return false;
        }

        ClipAndCompactMeshToContour(
            contour,
            contourContainmentVertices,
            contourContainmentTriangles,
            ref localVertices,
            ref triangles);
        triangles = SanitizeTriangles(triangles, localVertices.Length);
        if (localVertices.Length < 3 || triangles.Length < 3)
        {
            quadNetLastError = "QuadNet: clipped mesh is empty";
            return false;
        }

        BuildEdgePairs(triangles, localVertices.Length, out var triAllPairs, out var triBoundaryPairs);

        if (!TryBuildVertexRemap(sourceVertices, localVertices, out var sourceToLocalRemap))
        {
            allEdgePairs = triAllPairs;
            boundaryEdgePairs = triBoundaryPairs;
            quadNetLastError = null;
            return allEdgePairs.Length >= 2 && boundaryEdgePairs.Length >= 2;
        }

        var desiredPairs = RemapEdgeKeysToPairs(desiredEdgeKeys, sourceToLocalRemap, localVertices.Length);
        var mappedBoundaryPairs = RemapEdgeKeysToPairs(boundaryEdgeKeys, sourceToLocalRemap, localVertices.Length);

        allEdgePairs = desiredPairs.Length >= 2 ? desiredPairs : triAllPairs;
        boundaryEdgePairs = mappedBoundaryPairs.Length >= 2 ? mappedBoundaryPairs : triBoundaryPairs;
        quadNetLastError = null;
        return allEdgePairs.Length >= 2 && boundaryEdgePairs.Length >= 2;
    }

    private static string ConsumeQuadNetLastError()
    {
        var error = quadNetLastError;
        quadNetLastError = null;
        return error;
    }

    private static bool TryBuildQuadNetPointCloud(
        IReadOnlyList<Vector2> contour,
        Rect gridFrameBounds,
        double density,
        float rotationDegrees,
        IReadOnlyList<Vector2> contourContainmentVertices,
        IReadOnlyList<int> contourContainmentTriangles,
        out List<Vector2> vertices,
        out List<Vector2Int> boundaryEdges,
        out HashSet<ulong> boundaryEdgeKeys,
        out HashSet<ulong> desiredEdgeKeys)
    {
        vertices = new List<Vector2>(1024);
        boundaryEdges = new List<Vector2Int>(512);
        boundaryEdgeKeys = new HashSet<ulong>();
        desiredEdgeKeys = new HashSet<ulong>();

        if (contour == null || contour.Count < 3)
            return false;

        var sourceBounds = gridFrameBounds.width > 1e-4f && gridFrameBounds.height > 1e-4f
            ? gridFrameBounds
            : ComputePolygonBounds(contour);
        var rotation = ReanimatorQuadNetAlgorithms.NormalizeQuadNetRotationDegrees(rotationDegrees);
        var pivot = sourceBounds.center;
        var gridContour = contour;
        var gridContainmentVertices = contourContainmentVertices;
        var gridContainmentTriangles = contourContainmentTriangles;
        if (Mathf.Abs(rotation) > 1e-4f)
        {
            gridContour = RotateQuadNetPoints(contour, pivot, -rotation);
            gridContainmentVertices = null;
            gridContainmentTriangles = null;
            TryBuildContourContainmentMesh(gridContour, out var rotatedContainmentVertices, out var rotatedContainmentTriangles);
            gridContainmentVertices = rotatedContainmentVertices;
            gridContainmentTriangles = rotatedContainmentTriangles;
        }

        var bounds = ReanimatorQuadNetAlgorithms.ComputeQuadNetGridBounds(sourceBounds, rotation);
        if (bounds.width <= 1e-4f || bounds.height <= 1e-4f)
            return false;

        var step = ComputeQuadNetStep(bounds, density);
        if (step <= 1e-4f)
            return false;

        var xLines = new List<float>(128);
        var yLines = new List<float>(128);
        BuildGridLineCoordinates(bounds.xMin, bounds.xMax, step, xLines);
        BuildGridLineCoordinates(bounds.yMin, bounds.yMax, step, yLines);

        var vertexLookup = new Dictionary<QuantizedPointKey, int>(2048);
        var boundaryOrdered = new List<int>(1024);
        var splitTs = new List<float>(64);

        BuildBoundaryFromGridIntersections(gridContour, xLines, yLines, vertices, vertexLookup, boundaryOrdered, splitTs, false);
        if (boundaryOrdered.Count < 3)
        {
            vertices.Clear();
            vertexLookup.Clear();
            BuildBoundaryFromGridIntersections(gridContour, xLines, yLines, vertices, vertexLookup, boundaryOrdered, splitTs, true);
        }

        if (boundaryOrdered.Count >= 2 && boundaryOrdered[0] == boundaryOrdered[boundaryOrdered.Count - 1])
            boundaryOrdered.RemoveAt(boundaryOrdered.Count - 1);
        if (boundaryOrdered.Count < 3)
            return false;

        var boundaryKeySet = new HashSet<ulong>();
        for (var i = 0; i < boundaryOrdered.Count; i++)
        {
            var aIdx = boundaryOrdered[i];
            var bIdx = boundaryOrdered[(i + 1) % boundaryOrdered.Count];
            if (aIdx == bIdx)
                continue;

            var key = MakeEdgeKey(aIdx, bIdx);
            if (!boundaryKeySet.Add(key))
                continue;

            boundaryEdges.Add(new Vector2Int(aIdx, bIdx));
            boundaryEdgeKeys.Add(key);
            desiredEdgeKeys.Add(key);
        }

        if (boundaryEdges.Count < 3)
            return false;

        AddGridNodesInsideContour(
            xLines,
            yLines,
            gridContour,
            gridContainmentVertices,
            gridContainmentTriangles,
            vertices,
            vertexLookup);

        AddScanlineGridEdges(
            gridContour,
            xLines,
            yLines,
            vertices,
            vertexLookup,
            desiredEdgeKeys);

        if (Mathf.Abs(rotation) > 1e-4f)
        {
            for (var i = 0; i < vertices.Count; i++)
                vertices[i] = ReanimatorQuadNetAlgorithms.RotateQuadNetPoint(vertices[i], pivot, rotation);
        }

        return vertices.Count >= 3 && boundaryEdges.Count >= 3 && desiredEdgeKeys.Count >= 3;
    }

    private static List<Vector2> RotateQuadNetPoints(IReadOnlyList<Vector2> points, Vector2 pivot, float degrees)
    {
        var result = new List<Vector2>(points != null ? points.Count : 0);
        if (points == null)
            return result;

        for (var i = 0; i < points.Count; i++)
            result.Add(ReanimatorQuadNetAlgorithms.RotateQuadNetPoint(points[i], pivot, degrees));
        return result;
    }

    private static void BuildBoundaryFromGridIntersections(
        IReadOnlyList<Vector2> contour,
        IReadOnlyList<float> xLines,
        IReadOnlyList<float> yLines,
        List<Vector2> vertices,
        Dictionary<QuantizedPointKey, int> vertexLookup,
        List<int> boundaryOrdered,
        List<float> splitTs,
        bool includeLegacyEndpoints)
    {
        boundaryOrdered.Clear();
        if (contour == null || contour.Count < 3)
            return;

        for (var edgeIndex = 0; edgeIndex < contour.Count; edgeIndex++)
        {
            var a = contour[edgeIndex];
            var b = contour[(edgeIndex + 1) % contour.Count];
            CollectEdgeSplitTsByGrid(a, b, xLines, yLines, splitTs, includeLegacyEndpoints);
            if (splitTs.Count == 0)
                continue;

            splitTs.Sort();
            for (var i = 0; i < splitTs.Count; i++)
            {
                var t = splitTs[i];
                var p = Vector2.LerpUnclamped(a, b, t);
                var idx = GetOrAddVertexIndex(p, vertices, vertexLookup);
                if (boundaryOrdered.Count == 0 || boundaryOrdered[boundaryOrdered.Count - 1] != idx)
                    boundaryOrdered.Add(idx);
            }
        }
    }

    private static void AddGridNodesInsideContour(
        IReadOnlyList<float> xLines,
        IReadOnlyList<float> yLines,
        IReadOnlyList<Vector2> contour,
        IReadOnlyList<Vector2> contourContainmentVertices,
        IReadOnlyList<int> contourContainmentTriangles,
        List<Vector2> vertices,
        Dictionary<QuantizedPointKey, int> vertexLookup)
    {
        const int maxInsidePoints = 20000;
        if (xLines == null || yLines == null || vertices == null || vertexLookup == null)
            return;

        for (var xi = 0; xi < xLines.Count; xi++)
        {
            var x = xLines[xi];
            for (var yi = 0; yi < yLines.Count; yi++)
            {
                if (vertices.Count >= maxInsidePoints)
                    return;

                var p = new Vector2(x, yLines[yi]);
                if (!IsInsideContourForQuadNet(p, contour, contourContainmentVertices, contourContainmentTriangles))
                    continue;

                GetOrAddVertexIndex(p, vertices, vertexLookup);
            }
        }
    }

    private static void AddScanlineGridEdges(
        IReadOnlyList<Vector2> contour,
        IReadOnlyList<float> xLines,
        IReadOnlyList<float> yLines,
        List<Vector2> vertices,
        Dictionary<QuantizedPointKey, int> vertexLookup,
        HashSet<ulong> desiredEdgeKeys)
    {
        if (contour == null || contour.Count < 3 || xLines == null || yLines == null || vertices == null || vertexLookup == null || desiredEdgeKeys == null)
            return;

        var intersections = new List<float>(64);
        var samples = new List<float>(64);

        // Vertical scanlines: connect contour intersections to internal grid nodes.
        for (var xi = 0; xi < xLines.Count; xi++)
        {
            var x = xLines[xi];
            CollectContourIntersectionsAtX(contour, x, intersections);
            if (intersections.Count < 2)
                continue;

            for (var i = 0; i + 1 < intersections.Count; i += 2)
            {
                var y0 = intersections[i];
                var y1 = intersections[i + 1];
                if (Mathf.Abs(y1 - y0) <= 1e-4f)
                    continue;

                BuildAxisSamplesOnGrid(y0, y1, yLines, samples);
                AddSampledPolylineEdges(true, x, samples, vertices, vertexLookup, desiredEdgeKeys);
            }
        }

        // Horizontal scanlines: same behavior for the orthogonal direction.
        for (var yi = 0; yi < yLines.Count; yi++)
        {
            var y = yLines[yi];
            CollectContourIntersectionsAtY(contour, y, intersections);
            if (intersections.Count < 2)
                continue;

            for (var i = 0; i + 1 < intersections.Count; i += 2)
            {
                var x0 = intersections[i];
                var x1 = intersections[i + 1];
                if (Mathf.Abs(x1 - x0) <= 1e-4f)
                    continue;

                BuildAxisSamplesOnGrid(x0, x1, xLines, samples);
                AddSampledPolylineEdges(false, y, samples, vertices, vertexLookup, desiredEdgeKeys);
            }
        }
    }

    private static void AddSampledPolylineEdges(
        bool vertical,
        float fixedAxis,
        IReadOnlyList<float> samples,
        List<Vector2> vertices,
        Dictionary<QuantizedPointKey, int> vertexLookup,
        HashSet<ulong> desiredEdgeKeys)
    {
        if (samples == null || samples.Count < 2)
            return;

        var prev = -1;
        for (var i = 0; i < samples.Count; i++)
        {
            var point = vertical
                ? new Vector2(fixedAxis, samples[i])
                : new Vector2(samples[i], fixedAxis);
            var index = GetOrAddVertexIndex(point, vertices, vertexLookup);
            if (prev >= 0 && prev != index)
                desiredEdgeKeys.Add(MakeEdgeKey(prev, index));
            prev = index;
        }
    }

    private static void BuildAxisSamplesOnGrid(float start, float end, IReadOnlyList<float> gridLines, List<float> samples)
    {
        samples.Clear();
        if (end < start)
        {
            var tmp = start;
            start = end;
            end = tmp;
        }

        samples.Add(start);
        if (gridLines != null)
        {
            for (var i = 0; i < gridLines.Count; i++)
            {
                var value = gridLines[i];
                if (value <= start + 1e-4f || value >= end - 1e-4f)
                    continue;
                samples.Add(value);
            }
        }
        samples.Add(end);

        for (var i = samples.Count - 2; i >= 0; i--)
        {
            if (Mathf.Abs(samples[i + 1] - samples[i]) <= 1e-4f)
                samples.RemoveAt(i + 1);
        }
    }

    private static void CollectContourIntersectionsAtX(IReadOnlyList<Vector2> contour, float x, List<float> result)
    {
        result.Clear();
        if (contour == null || contour.Count < 3)
            return;

        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            var dx = b.x - a.x;
            if (Mathf.Abs(dx) <= 1e-6f)
                continue;

            var crosses = (a.x <= x && b.x > x) || (b.x <= x && a.x > x);
            if (!crosses)
                continue;

            var t = (x - a.x) / dx;
            var y = Mathf.Lerp(a.y, b.y, t);
            AddUniqueValue(result, y, 1e-4f);
        }

        result.Sort();
    }

    private static void CollectContourIntersectionsAtY(IReadOnlyList<Vector2> contour, float y, List<float> result)
    {
        result.Clear();
        if (contour == null || contour.Count < 3)
            return;

        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            var dy = b.y - a.y;
            if (Mathf.Abs(dy) <= 1e-6f)
                continue;

            var crosses = (a.y <= y && b.y > y) || (b.y <= y && a.y > y);
            if (!crosses)
                continue;

            var t = (y - a.y) / dy;
            var x = Mathf.Lerp(a.x, b.x, t);
            AddUniqueValue(result, x, 1e-4f);
        }

        result.Sort();
    }

    private static bool TryBuildContourFallback(
        IReadOnlyList<Vector2> contour,
        out Vector2[] localVertices,
        out int[] triangles,
        out int[] boundaryEdgePairs)
    {
        return ReanimatorQuadNetAlgorithms.TryBuildContourFallback(contour, out localVertices, out triangles, out boundaryEdgePairs);
    }

    private static void ClipAndCompactMeshToContour(
        IReadOnlyList<Vector2> contour,
        IReadOnlyList<Vector2> contourContainmentVertices,
        IReadOnlyList<int> contourContainmentTriangles,
        ref Vector2[] vertices,
        ref int[] triangles)
    {
        if (contour == null || contour.Count < 3 || vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
            return;

        var validTriangles = new List<int>(triangles.Length);
        var limit = triangles.Length - (triangles.Length % 3);
        for (var i = 0; i < limit; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                continue;

            var a = vertices[ia];
            var b = vertices[ib];
            var c = vertices[ic];
            if (!IsInsideContourForQuadNet(a, contour, contourContainmentVertices, contourContainmentTriangles) ||
                !IsInsideContourForQuadNet(b, contour, contourContainmentVertices, contourContainmentTriangles) ||
                !IsInsideContourForQuadNet(c, contour, contourContainmentVertices, contourContainmentTriangles))
                continue;

            var center = (a + b + c) / 3f;
            if (!IsInsideContourForQuadNet(center, contour, contourContainmentVertices, contourContainmentTriangles))
                continue;

            validTriangles.Add(ia);
            validTriangles.Add(ib);
            validTriangles.Add(ic);
        }

        if (validTriangles.Count < 3)
        {
            vertices = Array.Empty<Vector2>();
            triangles = Array.Empty<int>();
            return;
        }

        var keep = new bool[vertices.Length];
        for (var i = 0; i < validTriangles.Count; i++)
            keep[validTriangles[i]] = true;

        var remap = new int[vertices.Length];
        for (var i = 0; i < remap.Length; i++)
            remap[i] = -1;

        var compactVertices = new List<Vector2>(vertices.Length);
        for (var i = 0; i < vertices.Length; i++)
        {
            if (!keep[i])
                continue;
            remap[i] = compactVertices.Count;
            compactVertices.Add(vertices[i]);
        }

        var compactTriangles = new List<int>(validTriangles.Count);
        for (var i = 0; i < validTriangles.Count; i += 3)
        {
            var a = remap[validTriangles[i]];
            var b = remap[validTriangles[i + 1]];
            var c = remap[validTriangles[i + 2]];
            if (a < 0 || b < 0 || c < 0 || a == b || b == c || c == a)
                continue;

            compactTriangles.Add(a);
            compactTriangles.Add(b);
            compactTriangles.Add(c);
        }

        vertices = compactVertices.ToArray();
        triangles = compactTriangles.ToArray();
    }

    private static bool TryBuildVertexRemap(IReadOnlyList<Vector2> sourceVertices, IReadOnlyList<Vector2> targetVertices, out int[] remap)
    {
        return ReanimatorQuadNetAlgorithms.TryBuildVertexRemap(sourceVertices, targetVertices, out remap);
    }

    private static int[] RemapEdgeKeysToPairs(HashSet<ulong> sourceEdgeKeys, int[] remap, int vertexCount)
    {
        return ReanimatorQuadNetAlgorithms.RemapEdgeKeysToPairs(sourceEdgeKeys, remap, vertexCount);
    }

    private static bool TrySelectPrimaryContour(
        IReadOnlyList<Vector2[]> paths,
        Vector2 scaleInv,
        Vector2 rectOffset,
        out List<Vector2> contour)
    {
        return ReanimatorQuadNetAlgorithms.TrySelectPrimaryContour(paths, scaleInv, rectOffset, out contour);
    }

    private static float ComputeQuadNetStep(Rect bounds, double density)
    {
        return ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(bounds, density);
    }

    private static Rect ComputePolygonBounds(IReadOnlyList<Vector2> polygon)
    {
        if (polygon == null || polygon.Count == 0)
            return new Rect(0f, 0f, 0f, 0f);

        var minX = polygon[0].x;
        var minY = polygon[0].y;
        var maxX = polygon[0].x;
        var maxY = polygon[0].y;
        for (var i = 1; i < polygon.Count; i++)
        {
            var p = polygon[i];
            minX = Mathf.Min(minX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxX = Mathf.Max(maxX, p.x);
            maxY = Mathf.Max(maxY, p.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static void BuildGridLineCoordinates(float min, float max, float step, List<float> result)
    {
        result.Clear();
        if (step <= 1e-4f || max < min)
            return;

        var start = Mathf.Floor(min / step) * step;
        var end = Mathf.Ceil(max / step) * step;
        var guard = 0;
        for (var value = start; value <= end + 1e-4f && guard < 4096; value += step)
        {
            result.Add(value);
            guard++;
        }

        if (result.Count == 0)
        {
            result.Add(min);
            if (Mathf.Abs(max - min) > 1e-4f)
                result.Add(max);
        }
    }

    private static void CollectEdgeSplitTsByGrid(
        Vector2 a,
        Vector2 b,
        IReadOnlyList<float> xLines,
        IReadOnlyList<float> yLines,
        List<float> result,
        bool includeLegacyEndpoints)
    {
        result.Clear();
        var includeStart = includeLegacyEndpoints || IsPointOnAnyGridLine(a, xLines, yLines);
        var includeEnd = includeLegacyEndpoints || IsPointOnAnyGridLine(b, xLines, yLines);
        if (includeStart)
            result.Add(0f);
        if (includeEnd)
            AddUniqueValue(result, 1f, 1e-5f);

        var dx = b.x - a.x;
        if (Mathf.Abs(dx) > 1e-6f && xLines != null)
        {
            for (var i = 0; i < xLines.Count; i++)
            {
                var t = (xLines[i] - a.x) / dx;
                if (t < -1e-5f || t > 1f + 1e-5f)
                    continue;
                if (t <= 1e-5f)
                {
                    if (includeStart)
                        AddUniqueValue(result, 0f, 1e-5f);
                    continue;
                }
                if (t >= 1f - 1e-5f)
                {
                    if (includeEnd)
                        AddUniqueValue(result, 1f, 1e-5f);
                    continue;
                }

                AddUniqueValue(result, t, 1e-5f);
            }
        }

        var dy = b.y - a.y;
        if (Mathf.Abs(dy) > 1e-6f && yLines != null)
        {
            for (var i = 0; i < yLines.Count; i++)
            {
                var t = (yLines[i] - a.y) / dy;
                if (t < -1e-5f || t > 1f + 1e-5f)
                    continue;
                if (t <= 1e-5f)
                {
                    if (includeStart)
                        AddUniqueValue(result, 0f, 1e-5f);
                    continue;
                }
                if (t >= 1f - 1e-5f)
                {
                    if (includeEnd)
                        AddUniqueValue(result, 1f, 1e-5f);
                    continue;
                }

                AddUniqueValue(result, t, 1e-5f);
            }
        }
    }

    private static bool IsPointOnAnyGridLine(Vector2 point, IReadOnlyList<float> xLines, IReadOnlyList<float> yLines)
    {
        return ReanimatorQuadNetAlgorithms.IsPointOnAnyGridLine(point, xLines, yLines);
    }

    private static void AddUniqueValue(List<float> values, float value, float epsilon)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (Mathf.Abs(values[i] - value) <= epsilon)
                return;
        }

        values.Add(value);
    }

    private static int GetOrAddVertexIndex(
        Vector2 point,
        List<Vector2> vertices,
        Dictionary<QuantizedPointKey, int> lookup)
    {
        var key = new QuantizedPointKey(point);
        if (lookup.TryGetValue(key, out var index))
            return index;

        index = vertices.Count;
        vertices.Add(point);
        lookup.Add(key, index);
        return index;
    }

    private static long MakeGridKey(int x, int y)
    {
        return ((long)(uint)x << 32) | (uint)y;
    }

    private static bool TryBuildContourContainmentMesh(
        IReadOnlyList<Vector2> contour,
        out Vector2[] vertices,
        out int[] triangles)
    {
        vertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        if (contour == null || contour.Count < 3)
            return false;

        var sourceVertices = new List<Vector2>(contour.Count);
        var sourceEdges = new List<Vector2Int>(contour.Count);
        for (var i = 0; i < contour.Count; i++)
        {
            sourceVertices.Add(contour[i]);
            sourceEdges.Add(new Vector2Int(i, (i + 1) % contour.Count));
        }

        if (!TryTriangulateWithUnityTessellation(sourceVertices, sourceEdges, 0, out vertices, out triangles, out _))
            return false;

        triangles = SanitizeTriangles(triangles, vertices.Length);
        return vertices.Length >= 3 && triangles.Length >= 3;
    }

    private static bool IsInsideContourForQuadNet(
        Vector2 point,
        IReadOnlyList<Vector2> contour,
        IReadOnlyList<Vector2> contourContainmentVertices,
        IReadOnlyList<int> contourContainmentTriangles)
    {
        if (contour == null || contour.Count < 3)
            return false;

        if (IsPointInsideOrOnPolygon(point, contour))
            return true;

        if (contourContainmentVertices != null &&
            contourContainmentTriangles != null &&
            contourContainmentVertices.Count >= 3 &&
            contourContainmentTriangles.Count >= 3)
        {
            var containmentVertices = contourContainmentVertices as Vector2[] ?? new List<Vector2>(contourContainmentVertices).ToArray();
            var containmentTriangles = contourContainmentTriangles as int[] ?? new List<int>(contourContainmentTriangles).ToArray();
            if (TryFindContainingTriangle(
                    containmentTriangles,
                    containmentVertices,
                    point,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
                return true;
        }

        return false;
    }

    private static bool IsPointInsideOrOnPolygon(Vector2 p, IReadOnlyList<Vector2> polygon)
    {
        return ReanimatorQuadNetAlgorithms.IsPointInsideOrOnPolygon(p, polygon);
    }

    private static bool IsPointOnSegment(Vector2 p, Vector2 a, Vector2 b, float epsilon)
    {
        return ReanimatorQuadNetAlgorithms.IsPointOnSegment(p, a, b, epsilon);
    }
}
#endif
