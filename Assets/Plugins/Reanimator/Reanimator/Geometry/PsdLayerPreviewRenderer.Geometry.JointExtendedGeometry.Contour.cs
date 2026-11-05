#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static float GetPerimeterDirectedDistance(
        float fromDistance,
        float toDistance,
        float perimeterLength,
        bool forward)
    {
        if (perimeterLength <= 0.001f)
            return 0f;

        return forward
            ? Mathf.Repeat(toDistance - fromDistance, perimeterLength)
            : Mathf.Repeat(fromDistance - toDistance, perimeterLength);
    }

    private bool TryBuildJointBoundaryLoopPoints(LayerVertexData data, out List<int> boundaryLoop, out List<Vector2> loopPoints)
    {
        boundaryLoop = new List<int>();
        loopPoints = new List<Vector2>();
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, data.vertices.Length, out boundaryLoop) || boundaryLoop.Count < 3)
        {
            var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, data.vertices.Length);
            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        }

        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;

        loopPoints.Capacity = boundaryLoop.Count;
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var index = boundaryLoop[i];
            if (index < 0 || index >= data.vertices.Length)
                continue;
            loopPoints.Add(data.vertices[index]);
        }

        return loopPoints.Count >= 3;
    }

    private bool TryResolveJointBlueArcDirection(
        IReadOnlyList<Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        float startDistance,
        float endDistance,
        Vector2 centerDoc,
        out bool blueForward,
        out float blueArcLength)
    {
        blueForward = true;
        blueArcLength = 0f;
        if (loopPoints == null || segmentLengths == null || loopPoints.Count < 3 || perimeterLength <= 0.001f)
            return false;

        var forwardSpan = Mathf.Repeat(endDistance - startDistance, perimeterLength);
        var backwardSpan = perimeterLength - forwardSpan;
        var forwardInside = false;
        var backwardInside = false;

        if (TrySampleContourPointAtDistance(
                loopPoints,
                segmentLengths,
                perimeterLength,
                startDistance + forwardSpan * 0.5f,
                out var forwardSample))
        {
            forwardInside = IsPointInsideCurrentJointSector(forwardSample, centerDoc);
        }

        if (TrySampleContourPointAtDistance(
                loopPoints,
                segmentLengths,
                perimeterLength,
                startDistance - backwardSpan * 0.5f,
                out var backwardSample))
        {
            backwardInside = IsPointInsideCurrentJointSector(backwardSample, centerDoc);
        }

        if (forwardInside != backwardInside)
        {
            blueForward = forwardInside;
            blueArcLength = blueForward ? forwardSpan : backwardSpan;
            return blueArcLength > 0.0001f;
        }

        blueForward = forwardSpan <= backwardSpan;
        blueArcLength = blueForward ? forwardSpan : backwardSpan;
        return blueArcLength > 0.0001f;
    }

    private bool TryResolveJointTransparentAngleRange(
        float boundaryAngleRad,
        float grayAngleRad,
        out bool transparentForward,
        out float transparentSpan)
    {
        transparentForward = true;
        transparentSpan = 0f;
        var signedDelta = NormalizeAngleRad(grayAngleRad - boundaryAngleRad);
        if (Mathf.Abs(signedDelta) <= 0.0001f)
            return false;

        transparentForward = signedDelta >= 0f;
        transparentSpan = Mathf.Abs(signedDelta);
        return transparentSpan > 0.0001f;
    }

    private static float GetDirectedAngleDistance(float fromAngleRad, float toAngleRad, bool forward)
    {
        return ReanimatorJointGeometryAlgorithms.GetDirectedAngleDistance(fromAngleRad, toAngleRad, forward);
    }

    private static bool IsAngleOnDirectedArc(
        float angleRad,
        float arcStartAngleRad,
        float arcSpanRad,
        bool forward,
        float tolerance)
    {
        return ReanimatorJointGeometryAlgorithms.IsAngleOnDirectedArc(angleRad, arcStartAngleRad, arcSpanRad, forward, tolerance);
    }

    private static float GetAngleAlongDirectedArc(float arcStartAngleRad, float arcSpanRad, bool forward, float t)
    {
        var delta = Mathf.Clamp01(t) * arcSpanRad;
        return NormalizeAngleRad(arcStartAngleRad + (forward ? delta : -delta));
    }

    private static bool IsPerimeterDistanceOnArc(
        float distance,
        float arcStartDistance,
        float arcLength,
        float perimeterLength,
        bool forward,
        float tolerance)
    {
        return ReanimatorJointGeometryAlgorithms.IsPerimeterDistanceOnArc(distance, arcStartDistance, arcLength, perimeterLength, forward, tolerance);
    }

}
#endif
