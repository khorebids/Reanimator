#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Sprites;
using UnityEngine;

internal static class ReanimatorImporterGeometryAlgorithms
{
    private const string RuntimeUnitImporterMode = "raw_ppu_plus_pivot";
    private const string CanonicalImporterMode = "raw_local";

    public static string SanitizeImporterWriteMode(string mode)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? CanonicalImporterMode : mode.Trim();
        const string flipSuffix = "_flip_y";
        var flip = false;
        if (mode.EndsWith(flipSuffix, StringComparison.Ordinal))
        {
            mode = mode.Substring(0, mode.Length - flipSuffix.Length);
            flip = true;
        }

        var sanitized = string.Equals(mode, RuntimeUnitImporterMode, StringComparison.Ordinal)
            ? CanonicalImporterMode
            : mode;
        return flip ? sanitized + flipSuffix : sanitized;
    }

    public static bool IsLikelyRuntimeUnitImporterGeometry(
        IReadOnlyList<Vertex2DMetaData> vertices,
        Rect spriteRect,
        float pixelsPerUnit,
        Vector2 pivotPixels)
    {
        if (vertices == null || vertices.Count == 0)
            return false;
        if (spriteRect.width <= 0f || spriteRect.height <= 0f || pixelsPerUnit <= 1.01f)
            return false;
        if (!TryGetVertexBounds(vertices, out var rawBounds))
            return false;

        var maxSpriteDim = Mathf.Max(spriteRect.width, spriteRect.height);
        var rawMaxExtent = Mathf.Max(rawBounds.width, rawBounds.height);
        var rawMaxAbs = Mathf.Max(
            Mathf.Abs(rawBounds.xMin),
            Mathf.Abs(rawBounds.xMax),
            Mathf.Abs(rawBounds.yMin),
            Mathf.Abs(rawBounds.yMax));

        var convertedBounds = Rect.MinMaxRect(
            rawBounds.xMin * pixelsPerUnit + pivotPixels.x,
            rawBounds.yMin * pixelsPerUnit + pivotPixels.y,
            rawBounds.xMax * pixelsPerUnit + pivotPixels.x,
            rawBounds.yMax * pixelsPerUnit + pivotPixels.y);

        var padding = Mathf.Max(4f, maxSpriteDim * 0.15f);
        var convertedFits =
            convertedBounds.xMin >= -padding &&
            convertedBounds.yMin >= -padding &&
            convertedBounds.xMax <= spriteRect.width + padding &&
            convertedBounds.yMax <= spriteRect.height + padding;
        if (!convertedFits)
            return false;

        if (rawBounds.xMin < -0.01f || rawBounds.yMin < -0.01f)
            return true;

        var unitExtentLimit = Mathf.Max(2f, maxSpriteDim / pixelsPerUnit * 2f);
        if (rawMaxAbs > unitExtentLimit)
            return false;

        var convertedMaxExtent = Mathf.Max(convertedBounds.width, convertedBounds.height);
        return convertedMaxExtent > rawMaxExtent * Mathf.Min(8f, pixelsPerUnit * 0.25f);
    }

    public static Vertex2DMetaData[] ConvertRuntimeUnitVerticesToRawLocal(
        IReadOnlyList<Vertex2DMetaData> vertices,
        float pixelsPerUnit,
        Vector2 pivotPixels)
    {
        if (vertices == null || vertices.Count == 0)
            return Array.Empty<Vertex2DMetaData>();

        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        var result = new Vertex2DMetaData[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertex.position = vertex.position * ppu + pivotPixels;
            result[i] = vertex;
        }

        return result;
    }

    public static int FindNearestReferenceVertexIndex(IReadOnlyList<Vector2> referenceVertices, int referenceCount, Vector2 point)
    {
        if (referenceVertices == null || referenceCount <= 0)
            return -1;

        var clampedCount = Mathf.Min(referenceCount, referenceVertices.Count);
        if (clampedCount <= 0)
            return -1;

        var bestIndex = 0;
        var bestDistance = (referenceVertices[0] - point).sqrMagnitude;
        for (var i = 1; i < clampedCount; i++)
        {
            var dist = (referenceVertices[i] - point).sqrMagnitude;
            if (dist >= bestDistance)
                continue;

            bestDistance = dist;
            bestIndex = i;
        }

        return bestIndex;
    }

    public static bool TryMapPointToImporterByTriangles(
        IReadOnlyList<Vector2> referenceDocVertices,
        IReadOnlyList<Vertex2DMetaData> importerVertices,
        IReadOnlyList<int> importerIndices,
        Vector2 docPos,
        int referenceCount,
        out Vector2 mapped)
    {
        mapped = Vector2.zero;
        if (referenceDocVertices == null || importerVertices == null || importerIndices == null)
            return false;
        if (referenceCount < 3 || importerIndices.Count < 3)
            return false;

        var clampedReferenceCount = Mathf.Min(referenceCount, referenceDocVertices.Count);
        var hasResult = false;
        var bestScore = float.NegativeInfinity;
        const float epsilon = 0.001f;

        for (var i = 0; i + 2 < importerIndices.Count; i += 3)
        {
            var ia = importerIndices[i];
            var ib = importerIndices[i + 1];
            var ic = importerIndices[i + 2];
            if (ia < 0 || ib < 0 || ic < 0)
                continue;
            if (ia >= clampedReferenceCount || ib >= clampedReferenceCount || ic >= clampedReferenceCount)
                continue;
            if (ia >= importerVertices.Count || ib >= importerVertices.Count || ic >= importerVertices.Count)
                continue;

            var a = referenceDocVertices[ia];
            var b = referenceDocVertices[ib];
            var c = referenceDocVertices[ic];
            if (!TryGetBarycentric(docPos, a, b, c, out var wa, out var wb, out var wc))
                continue;
            if (wa < -epsilon || wb < -epsilon || wc < -epsilon)
                continue;

            var score = Mathf.Min(wa, Mathf.Min(wb, wc));
            if (hasResult && score < bestScore)
                continue;

            var pa = importerVertices[ia].position;
            var pb = importerVertices[ib].position;
            var pc = importerVertices[ic].position;
            mapped = new Vector2(
                pa.x * wa + pb.x * wb + pc.x * wc,
                pa.y * wa + pb.y * wb + pc.y * wc);
            bestScore = score;
            hasResult = true;
        }

        return hasResult;
    }

    public static int[] BuildImporterIndices(IReadOnlyList<int> preferred, int vertexCount, IReadOnlyList<int> fallback)
    {
        if (preferred != null && preferred.Count >= 3 && AreTriangleIndicesInRange(preferred, vertexCount))
        {
            var sanitized = SanitizeTriangles(preferred, vertexCount);
            if (sanitized.Length >= 3)
                return sanitized;
        }

        return SanitizeTriangles(fallback, vertexCount);
    }

    public static int[] SanitizeTriangles(IReadOnlyList<int> triangles, int vertexCount)
    {
        if (triangles == null || triangles.Count < 3 || vertexCount <= 0)
            return Array.Empty<int>();

        var result = new List<int>(triangles.Count);
        var limit = triangles.Count - (triangles.Count % 3);
        for (var i = 0; i + 2 < limit; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0)
                continue;
            if (a >= vertexCount || b >= vertexCount || c >= vertexCount)
                continue;
            if (a == b || b == c || c == a)
                continue;

            result.Add(a);
            result.Add(b);
            result.Add(c);
        }

        return result.Count == 0 ? Array.Empty<int>() : result.ToArray();
    }

    public static bool IsWritableImporterMeshSane(
        IReadOnlyList<Vertex2DMetaData> vertices,
        IReadOnlyList<int> indices,
        Rect spriteRect,
        out string reason)
    {
        reason = null;
        var vertexCount = vertices != null ? vertices.Count : 0;
        if (vertexCount < 3)
        {
            reason = "mesh has fewer than 3 vertices";
            return false;
        }

        if (spriteRect.width <= 0f || spriteRect.height <= 0f)
        {
            reason = "sprite rect has invalid size";
            return false;
        }

        var sanitized = SanitizeTriangles(indices, vertexCount);
        if (sanitized.Length < 3)
        {
            reason = "mesh has no valid triangles";
            return false;
        }

        if (!TryGetVertexBounds(vertices, out var bounds))
        {
            reason = "mesh bounds are invalid";
            return false;
        }

        var maxSpriteDim = Mathf.Max(1f, spriteRect.width, spriteRect.height);
        var padding = Mathf.Max(64f, maxSpriteDim);
        if (bounds.xMin < -padding ||
            bounds.yMin < -padding ||
            bounds.xMax > spriteRect.width + padding ||
            bounds.yMax > spriteRect.height + padding)
        {
            reason = $"mesh bounds {FormatRect(bounds)} are outside sprite-local rect {FormatRect(new Rect(0f, 0f, spriteRect.width, spriteRect.height))}";
            return false;
        }

        if (!HasNonDegenerateTriangle(vertices, sanitized))
        {
            reason = "mesh triangles are degenerate";
            return false;
        }

        return true;
    }

    private static bool AreTriangleIndicesInRange(IReadOnlyList<int> triangles, int vertexCount)
    {
        if (triangles == null || vertexCount <= 0)
            return false;

        for (var i = 0; i < triangles.Count; i++)
        {
            var index = triangles[i];
            if (index < 0 || index >= vertexCount)
                return false;
        }

        return true;
    }

    private static bool TryGetBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float wa, out float wb, out float wc)
    {
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var d00 = Vector2.Dot(v0, v0);
        var d01 = Vector2.Dot(v0, v1);
        var d11 = Vector2.Dot(v1, v1);
        var d20 = Vector2.Dot(v2, v0);
        var d21 = Vector2.Dot(v2, v1);
        var denom = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denom) < 1e-6f)
        {
            wa = wb = wc = 0f;
            return false;
        }

        wb = (d11 * d20 - d01 * d21) / denom;
        wc = (d00 * d21 - d01 * d20) / denom;
        wa = 1f - wb - wc;
        return true;
    }

    private static bool TryGetVertexBounds(IReadOnlyList<Vertex2DMetaData> vertices, out Rect bounds)
    {
        bounds = Rect.zero;
        if (vertices == null || vertices.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < vertices.Count; i++)
        {
            var position = vertices[i].position;
            if (!IsFinite(position.x) || !IsFinite(position.y))
                return false;
            min = Vector2.Min(min, position);
            max = Vector2.Max(max, position);
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return false;

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static bool HasNonDegenerateTriangle(IReadOnlyList<Vertex2DMetaData> vertices, IReadOnlyList<int> indices)
    {
        if (vertices == null || indices == null || vertices.Count < 3 || indices.Count < 3)
            return false;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = indices[i];
            var b = indices[i + 1];
            var c = indices[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertices.Count || b >= vertices.Count || c >= vertices.Count)
                continue;

            var area = Mathf.Abs(SignedTriangleArea(vertices[a].position, vertices[b].position, vertices[c].position));
            if (area > 1e-4f)
                return true;
        }

        return false;
    }

    private static float SignedTriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private static string FormatRect(Rect rect)
    {
        return $"({rect.xMin:F2},{rect.yMin:F2})-({rect.xMax:F2},{rect.yMax:F2})";
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
#endif
