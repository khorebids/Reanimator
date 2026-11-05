#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal enum ReanimatorFaceEyePointRegion
{
    Upper,
    Lower,
    Edge
}

internal sealed class ReanimatorFaceEyePointExpansion
{
    public Vector2[] BaseContour = Array.Empty<Vector2>();
    public Vector2[] OutwardNormals = Array.Empty<Vector2>();
    public Vector2[] DuplicatePoints = Array.Empty<Vector2>();
    public int[] DuplicateSourceIndices = Array.Empty<int>();
    public float[] BasePointWeights = Array.Empty<float>();
    public float[] DuplicatePointWeights = Array.Empty<float>();
    public Vector2 EyeCenter;
    public Vector2 ZeroWeightPoint;
    public Vector2[] OutlineContour = Array.Empty<Vector2>();
    public Vector2[] OutlineCollisionPoints = Array.Empty<Vector2>();
    public int[] OutlineCollisionSourceIndices = Array.Empty<int>();
    public float AppliedOutlineOffset;
    public ReanimatorFaceEyePointRegion[] Regions = Array.Empty<ReanimatorFaceEyePointRegion>();
    public Vector2 HeadDirection = Vector2.up;
    public int TotalPointCount => BaseContour.Length + DuplicatePoints.Length + OutlineContour.Length;

    internal Vector2[] OutlineSources = Array.Empty<Vector2>();
    internal Vector2[] OutlineDirections = Array.Empty<Vector2>();
    internal float[] OutlineHitDistances = Array.Empty<float>();
}

internal static partial class ReanimatorFaceAlphaMaskAlgorithms
{
    private const float FaceEyeNormalPerpendicularEpsilon = 0.0001f;
    private const float FaceEyeOutlineCollisionBackoff = 0.35f;

    private readonly struct FaceBoundaryEdge
    {
        public FaceBoundaryEdge(Vector2Int start, Vector2Int end, int vertexStride)
        {
            Start = start;
            End = end;
            StartKey = start.y * vertexStride + start.x;
            EndKey = end.y * vertexStride + end.x;
        }

        public Vector2Int Start { get; }
        public Vector2Int End { get; }
        public int StartKey { get; }
        public int EndKey { get; }
    }

    public static Vector2[] BuildReverseCullContour(ReanimatorFaceAlphaHole hole, float density)
    {
        var contour = hole.Contour;
        var perimeter = hole.Perimeter;
        if (contour == null || contour.Length < 3 || perimeter <= 0.0001f)
            return Array.Empty<Vector2>();

        var pointCount = Mathf.Clamp(
            Mathf.RoundToInt(perimeter * Mathf.Clamp(density, 0.1f, 1f)),
            4,
            256);
        pointCount = Mathf.Min(pointCount, contour.Length);
        var result = new Vector2[pointCount];
        var group = new List<int>();
        for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            var start = Mathf.FloorToInt(pointIndex * contour.Length / (float)pointCount);
            var end = Mathf.FloorToInt((pointIndex + 1) * contour.Length / (float)pointCount);
            end = Mathf.Max(start + 1, end);
            group.Clear();
            for (var contourIndex = start; contourIndex < end && contourIndex < contour.Length; contourIndex++)
                group.Add(contourIndex);
            result[pointIndex] = ReanimatorVertexCullAlgorithms.AveragePoints(contour, group);
        }

        if (CalculateSignedArea(result) > 0f)
            Array.Reverse(result);
        return result;
    }

    public static float CalculateSignedArea(IReadOnlyList<Vector2> contour)
    {
        if (contour == null || contour.Count < 3)
            return 0f;
        var doubleArea = 0f;
        for (var i = 0; i < contour.Count; i++)
        {
            var current = contour[i];
            var next = contour[(i + 1) % contour.Count];
            doubleArea += current.x * next.y - next.x * current.y;
        }
        return doubleArea * 0.5f;
    }

    public static Matrix4x4 BuildVirtualBoneSkinMatrix(
        Vector2 restWorldPosition,
        Quaternion restWorldRotation,
        Vector2 currentWorldPosition,
        Quaternion currentWorldRotation)
    {
        var restMatrix = Matrix4x4.TRS(
            new Vector3(restWorldPosition.x, restWorldPosition.y, 0f),
            NormalizeFacePreviewRotation(restWorldRotation),
            Vector3.one);
        var currentMatrix = Matrix4x4.TRS(
            new Vector3(currentWorldPosition.x, currentWorldPosition.y, 0f),
            NormalizeFacePreviewRotation(currentWorldRotation),
            Vector3.one);
        return currentMatrix * restMatrix.inverse;
    }

    public static Vector2 ApplyVirtualBoneSkinWeight(
        Vector2 point,
        Matrix4x4 skinMatrix,
        float weight)
    {
        var clampedWeight = Mathf.Clamp01(weight);
        if (clampedWeight <= 0.0001f)
            return point;
        var transformed = skinMatrix.MultiplyPoint3x4(new Vector3(point.x, point.y, 0f));
        return Vector2.Lerp(point, new Vector2(transformed.x, transformed.y), clampedWeight);
    }

    private static Quaternion NormalizeFacePreviewRotation(Quaternion rotation)
    {
        var lengthSquared = rotation.x * rotation.x +
                            rotation.y * rotation.y +
                            rotation.z * rotation.z +
                            rotation.w * rotation.w;
        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.000001f)
            return Quaternion.identity;
        var inverseLength = 1f / Mathf.Sqrt(lengthSquared);
        return new Quaternion(
            rotation.x * inverseLength,
            rotation.y * inverseLength,
            rotation.z * inverseLength,
            rotation.w * inverseLength);
    }

    public static ReanimatorFaceEyePointExpansion BuildHeadDirectedEyePoints(
        ReanimatorFaceAlphaMaskAnalysis analysis,
        ReanimatorFaceAlphaHole hole,
        float density,
        Vector2 headDirection,
        float offset,
        float outlineOffset,
        IReadOnlyList<Vector2> geometryBoundaryVertices = null,
        IReadOnlyList<int> geometryBoundaryEdgePairs = null,
        AnimationCurve weightCurve = null)
    {
        var result = new ReanimatorFaceEyePointExpansion();
        var contour = BuildReverseCullContour(hole, density);
        if (contour.Length < 4)
            return result;

        var up = headDirection.sqrMagnitude > 0.0001f ? headDirection.normalized : Vector2.up;
        var center = hole.Center;
        var regions = ClassifyEyeContourByNormals(contour, center, up, out var outwardNormals);

        var duplicateSourceIndices = CollectUpperAndEdgeSourceIndices(regions);
        var duplicatePoints = new Vector2[duplicateSourceIndices.Length];
        var distance = Mathf.Max(0f, offset);
        for (var i = 0; i < duplicateSourceIndices.Length; i++)
        {
            var sourceIndex = duplicateSourceIndices[i];
            var radialDirection = contour[sourceIndex] - center;
            if (radialDirection.sqrMagnitude <= 0.0001f)
                radialDirection = up;
            duplicatePoints[i] = contour[sourceIndex] + radialDirection.normalized * distance;
        }

        BuildEyePreviewWeights(
            contour,
            regions,
            duplicatePoints,
            center,
            up,
            weightCurve,
            out var zeroWeightPoint,
            out var basePointWeights,
            out var duplicatePointWeights);

        var duplicateBySource = new Vector2[contour.Length];
        var hasDuplicate = new bool[contour.Length];
        for (var i = 0; i < duplicateSourceIndices.Length; i++)
        {
            var sourceIndex = duplicateSourceIndices[i];
            duplicateBySource[sourceIndex] = duplicatePoints[i];
            hasDuplicate[sourceIndex] = true;
        }

        var outlineDistance = Mathf.Max(0f, outlineOffset);
        var outlineSources = new Vector2[contour.Length];
        var radialDirections = new Vector2[contour.Length];
        for (var i = 0; i < contour.Length; i++)
        {
            var radialDirection = contour[i] - center;
            if (radialDirection.sqrMagnitude <= 0.0001f)
                radialDirection = up;
            radialDirections[i] = radialDirection.normalized;
            outlineSources[i] = hasDuplicate[i] ? duplicateBySource[i] : contour[i];
        }

        var outlineHitDistances = new float[contour.Length];
        var useGeometryBoundary = geometryBoundaryVertices != null &&
                                  geometryBoundaryEdgePairs != null &&
                                  geometryBoundaryVertices.Count >= 3 &&
                                  geometryBoundaryEdgePairs.Count >= 6;
        for (var i = 0; i < contour.Length; i++)
        {
            outlineHitDistances[i] = float.PositiveInfinity;
            var hasCollision = useGeometryBoundary
                ? TryFindFirstGeometryBoundaryDistance(
                    outlineSources[i],
                    radialDirections[i],
                    outlineDistance,
                    geometryBoundaryVertices,
                    geometryBoundaryEdgePairs,
                    out var exitDistance)
                : TryFindFirstSpriteExitDistance(
                    analysis,
                    outlineSources[i],
                    radialDirections[i],
                    outlineDistance,
                    out exitDistance);
            if (!hasCollision)
            {
                continue;
            }
            outlineHitDistances[i] = exitDistance;
        }

        result.BaseContour = contour;
        result.OutwardNormals = outwardNormals;
        result.DuplicatePoints = duplicatePoints;
        result.DuplicateSourceIndices = duplicateSourceIndices;
        result.BasePointWeights = basePointWeights;
        result.DuplicatePointWeights = duplicatePointWeights;
        result.EyeCenter = center;
        result.ZeroWeightPoint = zeroWeightPoint;
        result.OutlineContour = new Vector2[contour.Length];
        result.OutlineSources = outlineSources;
        result.OutlineDirections = radialDirections;
        result.OutlineHitDistances = outlineHitDistances;
        result.Regions = regions;
        result.HeadDirection = up;
        ApplySharedEyeOutlineCollisionStop(outlineDistance, result);
        return result;
    }

    private static void BuildEyePreviewWeights(
        IReadOnlyList<Vector2> contour,
        IReadOnlyList<ReanimatorFaceEyePointRegion> regions,
        IReadOnlyList<Vector2> duplicatePoints,
        Vector2 center,
        Vector2 headDirection,
        AnimationCurve weightCurve,
        out Vector2 zeroWeightPoint,
        out float[] basePointWeights,
        out float[] duplicatePointWeights)
    {
        var up = headDirection.sqrMagnitude > 0.0001f ? headDirection.normalized : Vector2.up;
        var side = new Vector2(-up.y, up.x);
        var centralUpperProjection = 0f;
        var closestToAxis = float.PositiveInfinity;
        for (var i = 0; i < contour.Count; i++)
        {
            if (regions[i] == ReanimatorFaceEyePointRegion.Lower)
                continue;
            var fromCenter = contour[i] - center;
            var lateralDistance = Mathf.Abs(Vector2.Dot(fromCenter, side));
            var upwardDistance = Vector2.Dot(fromCenter, up);
            if (lateralDistance > closestToAxis + 0.0001f ||
                (Mathf.Abs(lateralDistance - closestToAxis) <= 0.0001f &&
                 upwardDistance <= centralUpperProjection))
            {
                continue;
            }
            closestToAxis = lateralDistance;
            centralUpperProjection = upwardDistance;
        }
        zeroWeightPoint = center + up * Mathf.Max(0f, centralUpperProjection);

        basePointWeights = BuildEyeBandWeights(
            contour,
            center,
            side,
            weightCurve,
            index => regions[index] != ReanimatorFaceEyePointRegion.Lower);
        duplicatePointWeights = BuildEyeBandWeights(
            duplicatePoints,
            center,
            side,
            weightCurve,
            _ => true);
    }

    private static float[] BuildEyeBandWeights(
        IReadOnlyList<Vector2> points,
        Vector2 center,
        Vector2 side,
        AnimationCurve weightCurve,
        Func<int, bool> includePoint)
    {
        var count = points != null ? points.Count : 0;
        var result = new float[count];
        var positiveExtent = 0f;
        var negativeExtent = 0f;
        for (var i = 0; i < count; i++)
        {
            if (!includePoint(i))
                continue;
            var projection = Vector2.Dot(points[i] - center, side);
            positiveExtent = Mathf.Max(positiveExtent, projection);
            negativeExtent = Mathf.Max(negativeExtent, -projection);
        }

        for (var i = 0; i < count; i++)
        {
            if (!includePoint(i))
                continue;
            var projection = Vector2.Dot(points[i] - center, side);
            var extent = projection >= 0f ? positiveExtent : negativeExtent;
            var normalizedDistance = extent > 0.0001f ? Mathf.Abs(projection) / extent : 0f;
            result[i] = EvaluateEyePreviewWeight(weightCurve, normalizedDistance);
        }
        return result;
    }

    public static float EvaluateEyePreviewWeight(AnimationCurve curve, float normalizedDistance)
    {
        var distance = Mathf.Clamp01(normalizedDistance);
        return Mathf.Clamp01(curve != null && curve.length > 0 ? curve.Evaluate(distance) : distance);
    }

    public static bool IsPointStrictlyInsideContour(
        Vector2 point,
        IReadOnlyList<Vector2> contour,
        float edgeEpsilon = 0.001f)
    {
        if (contour == null || contour.Count < 3)
            return false;

        var epsilonSquared = Mathf.Max(0f, edgeEpsilon) * Mathf.Max(0f, edgeEpsilon);
        var inside = false;
        for (int i = 0, previous = contour.Count - 1; i < contour.Count; previous = i++)
        {
            var a = contour[previous];
            var b = contour[i];
            var segment = b - a;
            var segmentLengthSquared = segment.sqrMagnitude;
            var t = segmentLengthSquared > 0.0000001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / segmentLengthSquared)
                : 0f;
            if ((point - (a + segment * t)).sqrMagnitude <= epsilonSquared)
                return false;

            if ((a.y > point.y) == (b.y > point.y))
                continue;
            var intersectionX = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (point.x < intersectionX)
                inside = !inside;
        }
        return inside;
    }

    private static bool TryFindFirstGeometryBoundaryDistance(
        Vector2 source,
        Vector2 direction,
        float maxDistance,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> boundaryEdgePairs,
        out float hitDistance)
    {
        hitDistance = 0f;
        if (maxDistance <= 0.0001f ||
            direction.sqrMagnitude <= 0.0001f ||
            vertices == null ||
            boundaryEdgePairs == null)
        {
            return false;
        }

        var rayDirection = direction.normalized;
        var nearestDistance = float.PositiveInfinity;
        for (var pairIndex = 0; pairIndex + 1 < boundaryEdgePairs.Count; pairIndex += 2)
        {
            var aIndex = boundaryEdgePairs[pairIndex];
            var bIndex = boundaryEdgePairs[pairIndex + 1];
            if (aIndex < 0 || bIndex < 0 || aIndex >= vertices.Count || bIndex >= vertices.Count)
                continue;

            var a = vertices[aIndex];
            var segment = vertices[bIndex] - a;
            var denominator = Cross2D(rayDirection, segment);
            if (Mathf.Abs(denominator) <= 0.000001f)
                continue;

            var sourceToA = a - source;
            var distance = Cross2D(sourceToA, segment) / denominator;
            var segmentPosition = Cross2D(sourceToA, rayDirection) / denominator;
            if (distance < -0.0001f ||
                distance > maxDistance + 0.0001f ||
                segmentPosition < -0.0001f ||
                segmentPosition > 1.0001f)
            {
                continue;
            }

            nearestDistance = Mathf.Min(nearestDistance, Mathf.Max(0f, distance));
        }

        if (float.IsPositiveInfinity(nearestDistance))
            return false;
        hitDistance = nearestDistance;
        return true;
    }

    private static float Cross2D(Vector2 lhs, Vector2 rhs)
    {
        return lhs.x * rhs.y - lhs.y * rhs.x;
    }

    public static void ApplySharedEyeOutlineCollisionStop(
        float requestedOffset,
        params ReanimatorFaceEyePointExpansion[] expansions)
    {
        if (expansions == null || expansions.Length == 0)
            return;

        var requestedDistance = Mathf.Max(0f, requestedOffset);
        var stopDistance = float.PositiveInfinity;
        var stoppedExpansionIndex = -1;
        var stoppedSourceIndex = -1;
        for (var expansionIndex = 0; expansionIndex < expansions.Length; expansionIndex++)
        {
            var expansion = expansions[expansionIndex];
            if (expansion == null || expansion.OutlineHitDistances == null)
                continue;
            for (var sourceIndex = 0; sourceIndex < expansion.OutlineHitDistances.Length; sourceIndex++)
            {
                var hitDistance = expansion.OutlineHitDistances[sourceIndex];
                if (float.IsPositiveInfinity(hitDistance) || hitDistance >= stopDistance)
                    continue;
                stopDistance = hitDistance;
                stoppedExpansionIndex = expansionIndex;
                stoppedSourceIndex = sourceIndex;
            }
        }

        var collisionReached = stoppedExpansionIndex >= 0;
        var appliedDistance = collisionReached ? stopDistance : requestedDistance;
        for (var expansionIndex = 0; expansionIndex < expansions.Length; expansionIndex++)
        {
            var expansion = expansions[expansionIndex];
            if (expansion == null ||
                expansion.OutlineSources == null ||
                expansion.OutlineDirections == null ||
                expansion.OutlineSources.Length != expansion.OutlineDirections.Length)
            {
                continue;
            }

            if (expansion.OutlineContour == null ||
                expansion.OutlineContour.Length != expansion.OutlineSources.Length)
            {
                expansion.OutlineContour = new Vector2[expansion.OutlineSources.Length];
            }

            for (var sourceIndex = 0; sourceIndex < expansion.OutlineSources.Length; sourceIndex++)
            {
                var pointDistance = appliedDistance;
                if (expansionIndex == stoppedExpansionIndex && sourceIndex == stoppedSourceIndex)
                    pointDistance = Mathf.Max(0f, appliedDistance - FaceEyeOutlineCollisionBackoff);
                expansion.OutlineContour[sourceIndex] =
                    expansion.OutlineSources[sourceIndex] +
                    expansion.OutlineDirections[sourceIndex] * pointDistance;
            }

            if (expansionIndex == stoppedExpansionIndex)
            {
                expansion.OutlineCollisionSourceIndices = new[] { stoppedSourceIndex };
                expansion.OutlineCollisionPoints = new[]
                {
                    expansion.OutlineSources[stoppedSourceIndex] +
                    expansion.OutlineDirections[stoppedSourceIndex] * stopDistance
                };
            }
            else
            {
                expansion.OutlineCollisionSourceIndices = Array.Empty<int>();
                expansion.OutlineCollisionPoints = Array.Empty<Vector2>();
            }
            expansion.AppliedOutlineOffset = appliedDistance;
        }
    }

    public static ReanimatorFaceEyePointRegion[] ClassifyEyeContourByNormals(
        IReadOnlyList<Vector2> contour,
        Vector2 center,
        Vector2 headDirection,
        out Vector2[] outwardNormals)
    {
        var count = contour != null ? contour.Count : 0;
        outwardNormals = new Vector2[count];
        var regions = new ReanimatorFaceEyePointRegion[count];
        if (count == 0)
            return regions;

        var up = headDirection.sqrMagnitude > 0.0001f ? headDirection.normalized : Vector2.up;
        var clockwise = CalculateSignedArea(contour) <= 0f;
        for (var i = 0; i < count; i++)
        {
            var previous = contour[(i - 1 + count) % count];
            var current = contour[i];
            var next = contour[(i + 1) % count];
            var tangent = next - previous;
            if (tangent.sqrMagnitude <= 0.0001f)
                tangent = next - current;
            if (tangent.sqrMagnitude <= 0.0001f)
                tangent = current - previous;

            Vector2 outwardNormal;
            if (tangent.sqrMagnitude > 0.0001f)
            {
                tangent.Normalize();
                outwardNormal = clockwise
                    ? new Vector2(-tangent.y, tangent.x)
                    : new Vector2(tangent.y, -tangent.x);
            }
            else
            {
                outwardNormal = current - center;
                if (outwardNormal.sqrMagnitude <= 0.0001f)
                    outwardNormal = up;
                outwardNormal.Normalize();
            }

            outwardNormals[i] = outwardNormal;
            var normalProjection = Vector2.Dot(outwardNormal, up);
            if (normalProjection > FaceEyeNormalPerpendicularEpsilon)
                regions[i] = ReanimatorFaceEyePointRegion.Upper;
            else if (normalProjection < -FaceEyeNormalPerpendicularEpsilon)
                regions[i] = ReanimatorFaceEyePointRegion.Lower;
            else
                regions[i] = ReanimatorFaceEyePointRegion.Edge;
        }
        return regions;
    }

    public static bool IsInsideSpriteOuterContour(
        ReanimatorFaceAlphaMaskAnalysis analysis,
        Vector2 point)
    {
        if (analysis == null ||
            analysis.ExteriorPixels == null ||
            analysis.ExteriorPixels.Length < analysis.Width * analysis.Height ||
            point.x < 0f || point.y < 0f ||
            point.x >= analysis.Width || point.y >= analysis.Height)
        {
            return false;
        }

        var x = Mathf.Clamp(Mathf.FloorToInt(point.x), 0, analysis.Width - 1);
        var y = Mathf.Clamp(Mathf.FloorToInt(point.y), 0, analysis.Height - 1);
        return !analysis.ExteriorPixels[y * analysis.Width + x];
    }

    private static bool TryFindFirstSpriteExitDistance(
        ReanimatorFaceAlphaMaskAnalysis analysis,
        Vector2 source,
        Vector2 direction,
        float maxDistance,
        out float exitDistance)
    {
        exitDistance = 0f;
        if (maxDistance <= 0.0001f)
            return false;
        if (!IsInsideSpriteOuterContour(analysis, source))
            return true;

        var steps = Mathf.Max(1, Mathf.CeilToInt(maxDistance * 2f));
        var previousDistance = 0f;
        for (var step = 1; step <= steps; step++)
        {
            var distance = maxDistance * step / steps;
            if (IsInsideSpriteOuterContour(analysis, source + direction * distance))
            {
                previousDistance = distance;
                continue;
            }

            var insideDistance = previousDistance;
            var outsideDistance = distance;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var middle = (insideDistance + outsideDistance) * 0.5f;
                if (IsInsideSpriteOuterContour(analysis, source + direction * middle))
                    insideDistance = middle;
                else
                    outsideDistance = middle;
            }
            exitDistance = insideDistance;
            return true;
        }
        return false;
    }

    private static int[] CollectUpperAndEdgeSourceIndices(
        IReadOnlyList<ReanimatorFaceEyePointRegion> regions)
    {
        var count = regions != null ? regions.Count : 0;
        if (count <= 0)
            return Array.Empty<int>();

        var start = 0;
        var longestLowerGap = -1;
        for (var candidate = 0; candidate < count; candidate++)
        {
            if (regions[candidate] == ReanimatorFaceEyePointRegion.Lower)
                continue;
            var lowerGap = 0;
            var previous = (candidate - 1 + count) % count;
            while (lowerGap < count && regions[previous] == ReanimatorFaceEyePointRegion.Lower)
            {
                lowerGap++;
                previous = (previous - 1 + count) % count;
            }
            if (lowerGap <= longestLowerGap)
                continue;
            longestLowerGap = lowerGap;
            start = candidate;
        }

        var result = new List<int>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var index = (start + offset) % count;
            if (regions[index] != ReanimatorFaceEyePointRegion.Lower)
                result.Add(index);
        }
        return result.ToArray();
    }

    private static Vector2[] BuildHoleContour(
        IReadOnlyList<int> componentPixels,
        bool[] componentMask,
        int width,
        int height,
        out float perimeter)
    {
        perimeter = 0f;
        if (componentPixels == null || componentPixels.Count == 0 || componentMask == null)
            return Array.Empty<Vector2>();

        var vertexStride = width + 1;
        var edges = new List<FaceBoundaryEdge>();
        var outgoing = new Dictionary<int, List<int>>();
        for (var i = 0; i < componentPixels.Count; i++)
        {
            var index = componentPixels[i];
            var x = index % width;
            var y = index / width;
            if (!IsHoleComponentPixel(x, y - 1, width, height, componentMask))
                AddFaceBoundaryEdge(edges, outgoing, new Vector2Int(x, y), new Vector2Int(x + 1, y), vertexStride);
            if (!IsHoleComponentPixel(x + 1, y, width, height, componentMask))
                AddFaceBoundaryEdge(edges, outgoing, new Vector2Int(x + 1, y), new Vector2Int(x + 1, y + 1), vertexStride);
            if (!IsHoleComponentPixel(x, y + 1, width, height, componentMask))
                AddFaceBoundaryEdge(edges, outgoing, new Vector2Int(x + 1, y + 1), new Vector2Int(x, y + 1), vertexStride);
            if (!IsHoleComponentPixel(x - 1, y, width, height, componentMask))
                AddFaceBoundaryEdge(edges, outgoing, new Vector2Int(x, y + 1), new Vector2Int(x, y), vertexStride);
        }

        var used = new bool[edges.Count];
        var bestPath = new List<Vector2>();
        var bestPerimeter = 0f;
        for (var startEdgeIndex = 0; startEdgeIndex < edges.Count; startEdgeIndex++)
        {
            if (used[startEdgeIndex])
                continue;

            var path = new List<Vector2>();
            var currentEdgeIndex = startEdgeIndex;
            var startKey = edges[startEdgeIndex].StartKey;
            var closed = false;
            for (var step = 0; step <= edges.Count; step++)
            {
                if (currentEdgeIndex < 0 || currentEdgeIndex >= edges.Count || used[currentEdgeIndex])
                    break;

                var edge = edges[currentEdgeIndex];
                used[currentEdgeIndex] = true;
                path.Add(edge.Start);
                if (edge.EndKey == startKey)
                {
                    closed = true;
                    break;
                }

                currentEdgeIndex = FindNextFaceBoundaryEdge(edge, edges, outgoing, used);
            }

            if (!closed || path.Count < 3)
                continue;
            var pathPerimeter = CalculateClosedFacePathPerimeter(path);
            if (pathPerimeter <= bestPerimeter)
                continue;
            bestPerimeter = pathPerimeter;
            bestPath = path;
        }

        perimeter = bestPerimeter;
        return bestPath.ToArray();
    }

    private static void AddFaceBoundaryEdge(
        List<FaceBoundaryEdge> edges,
        Dictionary<int, List<int>> outgoing,
        Vector2Int start,
        Vector2Int end,
        int vertexStride)
    {
        var edge = new FaceBoundaryEdge(start, end, vertexStride);
        var edgeIndex = edges.Count;
        edges.Add(edge);
        if (!outgoing.TryGetValue(edge.StartKey, out var indices))
        {
            indices = new List<int>(1);
            outgoing.Add(edge.StartKey, indices);
        }
        indices.Add(edgeIndex);
    }

    private static int FindNextFaceBoundaryEdge(
        FaceBoundaryEdge incoming,
        IReadOnlyList<FaceBoundaryEdge> edges,
        IReadOnlyDictionary<int, List<int>> outgoing,
        IReadOnlyList<bool> used)
    {
        if (!outgoing.TryGetValue(incoming.EndKey, out var candidates))
            return -1;

        var incomingDirection = (Vector2)(incoming.End - incoming.Start);
        var bestIndex = -1;
        var bestTurn = float.MaxValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidateIndex = candidates[i];
            if (used[candidateIndex])
                continue;
            var candidate = edges[candidateIndex];
            var direction = (Vector2)(candidate.End - candidate.Start);
            var turn = Mathf.Atan2(
                incomingDirection.x * direction.y - incomingDirection.y * direction.x,
                Vector2.Dot(incomingDirection, direction));
            if (turn < -0.0001f)
                turn += Mathf.PI * 2f;
            else if (Mathf.Abs(turn) <= 0.0001f)
                turn = 0f;
            if (turn >= bestTurn)
                continue;
            bestTurn = turn;
            bestIndex = candidateIndex;
        }

        return bestIndex;
    }

    private static float CalculateClosedFacePathPerimeter(IReadOnlyList<Vector2> path)
    {
        var result = 0f;
        for (var i = 0; i < path.Count; i++)
            result += Vector2.Distance(path[i], path[(i + 1) % path.Count]);
        return result;
    }

    private static bool IsHoleComponentPixel(int x, int y, int width, int height, bool[] componentMask)
    {
        return x >= 0 && y >= 0 && x < width && y < height && componentMask[y * width + x];
    }
}
#endif
