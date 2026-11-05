#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorTriangulationAlgorithms
{
    public static bool TryTriangulateSimplePolygon(IReadOnlyList<Vector2> polygon, List<int> triangles)
    {
        if (polygon == null || triangles == null || polygon.Count < 3)
            return false;

        var count = polygon.Count;
        var signedArea = ComputeSignedArea(polygon);
        if (Mathf.Abs(signedArea) <= 1e-6f)
            return false;

        var windingSign = signedArea > 0f ? 1f : -1f;
        var remaining = new List<int>(count);
        for (var i = 0; i < count; i++)
            remaining.Add(i);

        var guard = 0;
        while (remaining.Count > 3 && guard < count * count)
        {
            guard++;
            var clippedEar = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var prev = remaining[(i - 1 + remaining.Count) % remaining.Count];
                var curr = remaining[i];
                var next = remaining[(i + 1) % remaining.Count];

                if (!IsConvexCorner(polygon[prev], polygon[curr], polygon[next], windingSign))
                    continue;
                if (ContainsPointInTriangle(polygon, remaining, prev, curr, next))
                    continue;

                AddOrientedTriangle(triangles, prev, curr, next, windingSign);
                remaining.RemoveAt(i);
                clippedEar = true;
                break;
            }

            if (!clippedEar)
                break;
        }

        if (remaining.Count == 3)
        {
            AddOrientedTriangle(triangles, remaining[0], remaining[1], remaining[2], windingSign);
            return true;
        }

        return TryBuildTriangleFan(polygon.Count, triangles, windingSign);
    }

    public static float ComputeSignedArea(IReadOnlyList<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return 0f;

        var area = 0f;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5f;
    }

    public static void SubdivideGeneratedGeometry(ref Vector2[] vertices, ref int[] triangles, int subdivide)
    {
        if (vertices == null || triangles == null || vertices.Length < 3 || triangles.Length < 3)
            return;

        var clamped = Mathf.Clamp(subdivide, 0, 100);
        if (clamped <= 0)
            return;

        var largestAreaFactor = Mathf.Lerp(0.5f, 0.05f, clamped / 100f);
        var currentVertices = new List<Vector2>(vertices);
        var currentTriangles = new List<int>(triangles);

        const int maxIterations = 4;
        const int maxVertices = 8192;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            if (currentVertices.Count >= maxVertices || currentTriangles.Count < 3)
                break;

            var maxArea = 0f;
            for (var i = 0; i + 2 < currentTriangles.Count; i += 3)
            {
                var ia = currentTriangles[i];
                var ib = currentTriangles[i + 1];
                var ic = currentTriangles[i + 2];
                if (ia < 0 || ib < 0 || ic < 0)
                    continue;
                if (ia >= currentVertices.Count || ib >= currentVertices.Count || ic >= currentVertices.Count)
                    continue;
                var area = Mathf.Abs(SignedTriangleArea(currentVertices[ia], currentVertices[ib], currentVertices[ic])) * 0.5f;
                if (area > maxArea)
                    maxArea = area;
            }

            if (maxArea <= 1e-6f)
                break;

            var areaThreshold = maxArea * largestAreaFactor;
            var nextTriangles = new List<int>(currentTriangles.Count * 2);
            var changed = false;

            for (var i = 0; i + 2 < currentTriangles.Count; i += 3)
            {
                var ia = currentTriangles[i];
                var ib = currentTriangles[i + 1];
                var ic = currentTriangles[i + 2];
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= currentVertices.Count || ib >= currentVertices.Count || ic >= currentVertices.Count)
                    continue;

                var a = currentVertices[ia];
                var b = currentVertices[ib];
                var c = currentVertices[ic];
                var signed = SignedTriangleArea(a, b, c);
                var area = Mathf.Abs(signed) * 0.5f;
                if (area <= areaThreshold || currentVertices.Count >= maxVertices)
                {
                    nextTriangles.Add(ia);
                    nextTriangles.Add(ib);
                    nextTriangles.Add(ic);
                    continue;
                }

                var center = (a + b + c) / 3f;
                var centerIndex = currentVertices.Count;
                currentVertices.Add(center);
                changed = true;

                if (signed >= 0f)
                {
                    nextTriangles.Add(ia);
                    nextTriangles.Add(ib);
                    nextTriangles.Add(centerIndex);
                    nextTriangles.Add(ib);
                    nextTriangles.Add(ic);
                    nextTriangles.Add(centerIndex);
                    nextTriangles.Add(ic);
                    nextTriangles.Add(ia);
                    nextTriangles.Add(centerIndex);
                }
                else
                {
                    nextTriangles.Add(ia);
                    nextTriangles.Add(centerIndex);
                    nextTriangles.Add(ib);
                    nextTriangles.Add(ib);
                    nextTriangles.Add(centerIndex);
                    nextTriangles.Add(ic);
                    nextTriangles.Add(ic);
                    nextTriangles.Add(centerIndex);
                    nextTriangles.Add(ia);
                }
            }

            if (!changed)
                break;
            currentTriangles = nextTriangles;
        }

        vertices = currentVertices.ToArray();
        triangles = currentTriangles.ToArray();
    }

    public static void SubdivideGeneratedGeometryConforming(
        ref Vector2[] vertices,
        ref int[] triangles,
        int passes)
    {
        if (vertices == null || triangles == null || vertices.Length < 3 || triangles.Length < 3)
            return;

        var passCount = Mathf.Clamp(passes, 0, 4);
        if (passCount == 0)
            return;

        const int maxVertices = 8192;
        var currentVertices = new List<Vector2>(vertices);
        var currentTriangles = new List<int>(triangles);
        for (var pass = 0; pass < passCount; pass++)
        {
            // A complete 1-to-4 pass is required to keep adjacent triangles
            // conforming. Never leave a partially split mesh with T-junctions.
            if (currentVertices.Count + currentTriangles.Count > maxVertices)
                break;

            var midpoints = new Dictionary<ulong, int>(currentTriangles.Count);
            var nextTriangles = new List<int>(currentTriangles.Count * 4);
            for (var i = 0; i + 2 < currentTriangles.Count; i += 3)
            {
                var a = currentTriangles[i];
                var b = currentTriangles[i + 1];
                var c = currentTriangles[i + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= currentVertices.Count || b >= currentVertices.Count || c >= currentVertices.Count)
                    continue;

                var ab = GetOrCreateMidpoint(a, b, currentVertices, midpoints);
                var bc = GetOrCreateMidpoint(b, c, currentVertices, midpoints);
                var ca = GetOrCreateMidpoint(c, a, currentVertices, midpoints);

                nextTriangles.Add(a);
                nextTriangles.Add(ab);
                nextTriangles.Add(ca);
                nextTriangles.Add(ab);
                nextTriangles.Add(b);
                nextTriangles.Add(bc);
                nextTriangles.Add(ca);
                nextTriangles.Add(bc);
                nextTriangles.Add(c);
                nextTriangles.Add(ab);
                nextTriangles.Add(bc);
                nextTriangles.Add(ca);
            }

            if (nextTriangles.Count < 3)
                break;
            currentTriangles = nextTriangles;
        }

        vertices = currentVertices.ToArray();
        triangles = currentTriangles.ToArray();
    }

    public static bool RetriangulateGeneratedGeometry(
        IReadOnlyList<Vector2> vertices,
        ref int[] triangles)
    {
        if (vertices == null || triangles == null || vertices.Count < 4 || triangles.Length < 6)
            return false;

        var working = new int[triangles.Length];
        System.Array.Copy(triangles, working, triangles.Length);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < vertices.Count; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }

        var diagonalSqr = Mathf.Max(1f, (max - min).sqrMagnitude);
        var maxFlips = Mathf.Min(16384, Mathf.Max(32, working.Length * 16));
        var changed = false;
        for (var flip = 0; flip < maxFlips; flip++)
        {
            var edges = BuildRetriangulationEdges(working, vertices.Count, out var incidence);
            if (edges.Count == 0)
                break;

            var keys = new List<ulong>(edges.Keys);
            keys.Sort();
            var bestImprovement = 0.00001f;
            var best = default(RetriangulationEdge);
            var found = false;
            for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                var edge = edges[keys[keyIndex]];
                if (edge.secondTriangleStart < 0 || edge.nonManifold)
                    continue;
                if (!TryScoreRetriangulationFlip(
                        vertices,
                        incidence,
                        diagonalSqr,
                        edge,
                        out var improvement))
                    continue;
                if (improvement <= bestImprovement)
                    continue;

                bestImprovement = improvement;
                best = edge;
                found = true;
            }

            if (!found)
                break;

            WriteRetriangulatedTriangle(
                working,
                best.firstTriangleStart,
                best.firstOpposite,
                best.secondOpposite,
                best.edgeA,
                vertices);
            WriteRetriangulatedTriangle(
                working,
                best.secondTriangleStart,
                best.secondOpposite,
                best.firstOpposite,
                best.edgeB,
                vertices);
            changed = true;
        }

        if (changed)
            triangles = working;
        return changed;
    }

    private struct RetriangulationEdge
    {
        public int edgeA;
        public int edgeB;
        public int firstTriangleStart;
        public int firstOpposite;
        public int secondTriangleStart;
        public int secondOpposite;
        public bool nonManifold;
    }

    private static Dictionary<ulong, RetriangulationEdge> BuildRetriangulationEdges(
        IReadOnlyList<int> triangles,
        int vertexCount,
        out int[] incidence)
    {
        incidence = new int[Mathf.Max(0, vertexCount)];
        var edges = new Dictionary<ulong, RetriangulationEdge>(triangles != null ? triangles.Count : 0);
        if (triangles == null)
            return edges;

        for (var triangleStart = 0; triangleStart + 2 < triangles.Count; triangleStart += 3)
        {
            var a = triangles[triangleStart];
            var b = triangles[triangleStart + 1];
            var c = triangles[triangleStart + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount ||
                a == b || b == c || c == a)
                continue;

            incidence[a]++;
            incidence[b]++;
            incidence[c]++;
            AddRetriangulationEdge(edges, a, b, c, triangleStart);
            AddRetriangulationEdge(edges, b, c, a, triangleStart);
            AddRetriangulationEdge(edges, c, a, b, triangleStart);
        }

        return edges;
    }

    private static void AddRetriangulationEdge(
        IDictionary<ulong, RetriangulationEdge> edges,
        int a,
        int b,
        int opposite,
        int triangleStart)
    {
        var edgeA = Mathf.Min(a, b);
        var edgeB = Mathf.Max(a, b);
        var key = ((ulong)(uint)edgeA << 32) | (uint)edgeB;
        if (!edges.TryGetValue(key, out var edge))
        {
            edges[key] = new RetriangulationEdge
            {
                edgeA = edgeA,
                edgeB = edgeB,
                firstTriangleStart = triangleStart,
                firstOpposite = opposite,
                secondTriangleStart = -1,
                secondOpposite = -1
            };
            return;
        }

        if (edge.secondTriangleStart < 0)
        {
            edge.secondTriangleStart = triangleStart;
            edge.secondOpposite = opposite;
        }
        else
        {
            edge.nonManifold = true;
        }
        edges[key] = edge;
    }

    private static bool TryScoreRetriangulationFlip(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> incidence,
        float diagonalSqr,
        RetriangulationEdge edge,
        out float improvement)
    {
        improvement = 0f;
        var a = edge.edgeA;
        var b = edge.edgeB;
        var c = edge.firstOpposite;
        var d = edge.secondOpposite;
        if (a == c || a == d || b == c || b == d || c == d)
            return false;

        var pa = vertices[a];
        var pb = vertices[b];
        var pc = vertices[c];
        var pd = vertices[d];
        const float epsilon = 0.00001f;
        if (Cross(pa, pb, pc) * Cross(pa, pb, pd) >= -epsilon ||
            Cross(pc, pd, pa) * Cross(pc, pd, pb) >= -epsilon)
            return false;

        var beforeFirst = TriangleQuality(pa, pb, pc);
        var beforeSecond = TriangleQuality(pa, pb, pd);
        var afterFirst = TriangleQuality(pc, pd, pa);
        var afterSecond = TriangleQuality(pc, pd, pb);
        var beforeMinimum = Mathf.Min(beforeFirst, beforeSecond);
        var afterMinimum = Mathf.Min(afterFirst, afterSecond);
        if (afterMinimum + epsilon < beforeMinimum * 0.8f)
            return false;

        var beforeDegreePenalty = incidence[a] * incidence[a] + incidence[b] * incidence[b] +
                                  incidence[c] * incidence[c] + incidence[d] * incidence[d];
        var afterDegreePenalty = (incidence[a] - 1) * (incidence[a] - 1) +
                                 (incidence[b] - 1) * (incidence[b] - 1) +
                                 (incidence[c] + 1) * (incidence[c] + 1) +
                                 (incidence[d] + 1) * (incidence[d] + 1);
        var shapeImprovement = (afterFirst + afterSecond) - (beforeFirst + beforeSecond);
        var edgeImprovement = ((pa - pb).sqrMagnitude - (pc - pd).sqrMagnitude) / diagonalSqr;
        var degreeImprovement = (beforeDegreePenalty - afterDegreePenalty) * 0.01f;
        improvement = shapeImprovement * 8f + edgeImprovement + degreeImprovement;
        return improvement > 0.00001f;
    }

    private static float TriangleQuality(Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = (b - a).sqrMagnitude;
        var bc = (c - b).sqrMagnitude;
        var ca = (a - c).sqrMagnitude;
        var denominator = Mathf.Max(0.000001f, ab + bc + ca);
        return Mathf.Abs(Cross(a, b, c)) / denominator;
    }

    private static void WriteRetriangulatedTriangle(
        IList<int> triangles,
        int triangleStart,
        int a,
        int b,
        int c,
        IReadOnlyList<Vector2> vertices)
    {
        var oldA = triangles[triangleStart];
        var oldB = triangles[triangleStart + 1];
        var oldC = triangles[triangleStart + 2];
        var oldSign = Mathf.Sign(Cross(vertices[oldA], vertices[oldB], vertices[oldC]));
        if (oldSign * Cross(vertices[a], vertices[b], vertices[c]) < 0f)
        {
            var swap = b;
            b = c;
            c = swap;
        }

        triangles[triangleStart] = a;
        triangles[triangleStart + 1] = b;
        triangles[triangleStart + 2] = c;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private static int GetOrCreateMidpoint(
        int a,
        int b,
        List<Vector2> vertices,
        Dictionary<ulong, int> midpoints)
    {
        var min = Mathf.Min(a, b);
        var max = Mathf.Max(a, b);
        var key = ((ulong)(uint)min << 32) | (uint)max;
        if (midpoints.TryGetValue(key, out var midpoint))
            return midpoint;

        midpoint = vertices.Count;
        vertices.Add((vertices[a] + vertices[b]) * 0.5f);
        midpoints[key] = midpoint;
        return midpoint;
    }

    private static bool TryBuildTriangleFan(int vertexCount, List<int> triangles, float windingSign)
    {
        if (vertexCount < 3 || triangles == null)
            return false;

        for (var i = 1; i < vertexCount - 1; i++)
            AddOrientedTriangle(triangles, 0, i, i + 1, windingSign);
        return true;
    }

    private static bool IsConvexCorner(Vector2 prev, Vector2 curr, Vector2 next, float windingSign)
    {
        var cross = Cross2D(curr - prev, next - curr);
        return cross * windingSign > 1e-6f;
    }

    private static bool ContainsPointInTriangle(
        IReadOnlyList<Vector2> polygon,
        IReadOnlyList<int> remaining,
        int aIndex,
        int bIndex,
        int cIndex)
    {
        if (polygon == null || remaining == null)
            return false;

        var a = polygon[aIndex];
        var b = polygon[bIndex];
        var c = polygon[cIndex];
        for (var i = 0; i < remaining.Count; i++)
        {
            var index = remaining[i];
            if (index == aIndex || index == bIndex || index == cIndex)
                continue;
            if (!TryGetBarycentric(polygon[index], a, b, c, out var wa, out var wb, out var wc))
                continue;
            if (wa >= -0.0001f && wb >= -0.0001f && wc >= -0.0001f)
                return true;
        }

        return false;
    }

    public static void AddOrientedTriangle(List<int> triangles, int a, int b, int c, float windingSign)
    {
        if (triangles == null)
            return;

        if (windingSign >= 0f)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
        else
        {
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
        }
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

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float SignedTriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross2D(b - a, c - a);
    }
}
#endif
