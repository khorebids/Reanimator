#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorJointV2Defaults
{
    public const float SpecialJointDistance = 0.5f;
    public const float BoneBaseOffset = 0.1f;

    public static AnimationCurve CreateWeightCurve()
    {
        return new AnimationCurve(
            CreateKey(0f, 0.2830229f, 2.32073045f, 2.32073045f, 0f, 0.166666672f),
            CreateKey(1f, 1f, 0.0344211981f, 0.0344211981f, 0.6166667f, 0f));
    }

    public static AnimationCurve CreateSpecialWeightCurve()
    {
        return new AnimationCurve(
            CreateKey(0f, 0.007080078f, 0f, 0f, 0f, 0f),
            CreateKey(0.28133598f, 0.0734239742f, 0.393174767f, 0.393174767f, 0.333333343f, 0.333333343f),
            CreateKey(0.5208435f, 0.556594849f, 4.04313f, 4.04313f, 0.333333343f, 1f),
            CreateKey(0.5931835f, 0.7480831f, 1.16900718f, 1.16900718f, 0.917930245f, 0.333333343f),
            CreateKey(0.722418964f, 0.873701155f, 0.8679093f, 0.8679093f, 1f, 0.212386265f),
            CreateKey(1f, 1f, 0.121293828f, 0.121293828f, 0.661798f, 0f));
    }

    private static Keyframe CreateKey(
        float time,
        float value,
        float inTangent,
        float outTangent,
        float inWeight,
        float outWeight)
    {
        return new Keyframe(time, value, inTangent, outTangent, inWeight, outWeight)
        {
            weightedMode = WeightedMode.None
        };
    }
}

internal static class ReanimatorJointGeometryAlgorithms
{
    public static float GetDirectedAngleDistance(float fromAngleRad, float toAngleRad, bool forward)
    {
        var twoPi = Mathf.PI * 2f;
        return forward
            ? Mathf.Repeat(toAngleRad - fromAngleRad, twoPi)
            : Mathf.Repeat(fromAngleRad - toAngleRad, twoPi);
    }

    public static bool IsAngleOnDirectedArc(
        float angleRad,
        float arcStartAngleRad,
        float arcSpanRad,
        bool forward,
        float tolerance)
    {
        return GetDirectedAngleDistance(arcStartAngleRad, angleRad, forward) <= arcSpanRad + Mathf.Max(0f, tolerance);
    }

    public static bool IsPerimeterDistanceOnArc(
        float distance,
        float arcStartDistance,
        float arcLength,
        float perimeterLength,
        bool forward,
        float tolerance)
    {
        if (perimeterLength <= 0.001f)
            return false;

        var rel = forward
            ? Mathf.Repeat(distance - arcStartDistance, perimeterLength)
            : Mathf.Repeat(arcStartDistance - distance, perimeterLength);
        return rel <= arcLength + Mathf.Max(0f, tolerance);
    }

    public static bool TryGetNearestContourRayIntersection(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> boundaryPairs,
        Vector2 rayOriginDoc,
        float angleRad,
        out Vector2 hitPointDoc)
    {
        hitPointDoc = Vector2.zero;
        if (vertices == null || boundaryPairs == null || vertices.Count < 3 || boundaryPairs.Count < 2)
            return false;

        var rayDir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
        if (rayDir.sqrMagnitude <= 1e-8f)
            return false;

        var hasHit = false;
        var bestT = float.PositiveInfinity;
        for (var i = 0; i + 1 < boundaryPairs.Count; i += 2)
        {
            var aIndex = boundaryPairs[i];
            var bIndex = boundaryPairs[i + 1];
            if (aIndex < 0 || bIndex < 0 || aIndex >= vertices.Count || bIndex >= vertices.Count || aIndex == bIndex)
                continue;

            if (!TryIntersectInfiniteLineSegment(rayOriginDoc, rayDir, vertices[aIndex], vertices[bIndex], out var lineT, out var hit))
                continue;
            if (lineT <= 1e-4f)
                continue;
            if (lineT >= bestT)
                continue;

            bestT = lineT;
            hitPointDoc = hit;
            hasHit = true;
        }

        return hasHit;
    }

    public static bool TryIntersectInfiniteLineSegment(
        Vector2 linePoint,
        Vector2 lineDir,
        Vector2 segA,
        Vector2 segB,
        out float lineT,
        out Vector2 hit)
    {
        lineT = 0f;
        hit = Vector2.zero;
        var segDir = segB - segA;
        var denom = Cross2D(lineDir, segDir);
        if (Mathf.Abs(denom) <= 1e-6f)
            return false;

        var rel = segA - linePoint;
        lineT = Cross2D(rel, segDir) / denom;
        var segU = Cross2D(rel, lineDir) / denom;
        if (segU < -1e-4f || segU > 1f + 1e-4f)
            return false;

        hit = linePoint + lineDir * lineT;
        return true;
    }

    public static float GetRelativeBoneAngleDegrees(Quaternion initialRotation, Quaternion currentRotation)
    {
        var initialDirection = NormalizeRotation(initialRotation) * Vector3.right;
        var currentDirection = NormalizeRotation(currentRotation) * Vector3.right;
        var initialAngle = Mathf.Atan2(initialDirection.y, initialDirection.x) * Mathf.Rad2Deg;
        var currentAngle = Mathf.Atan2(currentDirection.y, currentDirection.x) * Mathf.Rad2Deg;
        return Mathf.DeltaAngle(initialAngle, currentAngle);
    }

    public static int SelectPreferredParentSpriteIndex(IReadOnlyList<string> normalizedLayerNames)
    {
        if (normalizedLayerNames == null || normalizedLayerNames.Count == 0)
            return -1;

        for (var i = 0; i < normalizedLayerNames.Count; i++)
        {
            if (string.Equals(normalizedLayerNames[i], "body", System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    public static Vector2 RotatePointAround(Vector2 point, Vector2 pivot, float angleDegrees)
    {
        if (Mathf.Abs(angleDegrees) <= 0.0001f)
            return point;

        var angleRadians = angleDegrees * Mathf.Deg2Rad;
        var sin = Mathf.Sin(angleRadians);
        var cos = Mathf.Cos(angleRadians);
        var offset = point - pivot;
        return pivot + new Vector2(
            offset.x * cos - offset.y * sin,
            offset.x * sin + offset.y * cos);
    }

    public static float EvaluateJointV2DynamicWeight(float projection, float minProjection, float maxProjection)
    {
        if (maxProjection - minProjection <= 1e-6f)
            return 0.5f;

        return Mathf.InverseLerp(minProjection, maxProjection, projection);
    }

    public static float EvaluateJointV2ChainWeight(float outerDynamicWeight, float distanceFromWhite)
    {
        return Mathf.Lerp(
            0.5f,
            Mathf.Clamp01(outerDynamicWeight),
            Mathf.Clamp01(distanceFromWhite));
    }

    public static float EvaluateJointV2WeightCurve(
        AnimationCurve weightCurve,
        float normalizedPosition)
    {
        normalizedPosition = Mathf.Clamp01(normalizedPosition);
        var weight = weightCurve != null && weightCurve.length > 0
            ? weightCurve.Evaluate(normalizedPosition)
            : normalizedPosition;
        if (float.IsNaN(weight) || float.IsInfinity(weight))
            weight = normalizedPosition;
        return Mathf.Clamp01(weight);
    }

    public static float GetSamplePoseDeltaDegrees(float currentRelativeAngleDegrees, float targetRelativeAngleDegrees)
    {
        return Mathf.DeltaAngle(currentRelativeAngleDegrees, targetRelativeAngleDegrees);
    }

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        var lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y +
                            rotation.z * rotation.z + rotation.w * rotation.w;
        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.000001f)
            return Quaternion.identity;

        var inverseLength = 1f / Mathf.Sqrt(lengthSquared);
        return new Quaternion(
            rotation.x * inverseLength,
            rotation.y * inverseLength,
            rotation.z * inverseLength,
            rotation.w * inverseLength);
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
#endif
