#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void ApplyJointInfluenceWeights(
        LayerVertexData data,
        Vector2 centerDoc,
        float radius,
        Vector2 startHitDoc,
        Vector2 endHitDoc,
        int parentSpriteBone,
        int nearestSpriteBone,
        int longPoints,
        float startLongPointsOffset,
        float endLongPointsOffset)
    {
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return;
        if (radius <= 0.0001f)
            return;

        if (parentSpriteBone < 0 && nearestSpriteBone < 0)
            return;
        if (parentSpriteBone < 0)
            parentSpriteBone = nearestSpriteBone;
        if (nearestSpriteBone < 0)
            nearestSpriteBone = parentSpriteBone;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);

        var vertexCount = data.vertices.Length;
        var radiusSqr = radius * radius;
        var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
            if (fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6)
                contourMask = BuildContourVertexMask(fallbackBoundaryPairs, vertexCount);
        }

        var parentRatios = new float[vertexCount];
        for (var i = 0; i < parentRatios.Length; i++)
            parentRatios[i] = -1f;

        if (TryBuildJointBoundaryLoopPoints(data, out var boundaryLoop, out var loopPoints) &&
            TryBuildContourPerimeterSamples(loopPoints, out var segmentLengths, out var perimeterLength) &&
            TryProjectPointToContourPerimeter(loopPoints, segmentLengths, perimeterLength, startHitDoc, out var startDistance) &&
            TryProjectPointToContourPerimeter(loopPoints, segmentLengths, perimeterLength, endHitDoc, out var endDistance) &&
            TryResolveJointBlueArcDirection(
                loopPoints,
                segmentLengths,
                perimeterLength,
                startDistance,
                endDistance,
                centerDoc,
                out var blueForward,
                out _))
        {
            var startGrayHitDoc = Vector2.zero;
            var endGrayHitDoc = Vector2.zero;
            var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
            var hasStartGrayHit = startLongPointsOffset > 0.0001f &&
                                  TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, GetJointOffsetHandleAngleRad(radius, true), out startGrayHitDoc);
            var hasEndGrayHit = endLongPointsOffset > 0.0001f &&
                                TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, GetJointOffsetHandleAngleRad(radius, false), out endGrayHitDoc);

            var startTransparentForward = false;
            var endTransparentForward = false;
            var startTransparentSpan = 0f;
            var endTransparentSpan = 0f;
            hasStartGrayHit = hasStartGrayHit &&
                              TryResolveJointTransparentAngleRange(
                                  jointStartAngleRad,
                                  GetJointOffsetHandleAngleRad(radius, true),
                                  out startTransparentForward,
                                  out startTransparentSpan);
            hasEndGrayHit = hasEndGrayHit &&
                            TryResolveJointTransparentAngleRange(
                                jointEndAngleRad,
                                GetJointOffsetHandleAngleRad(radius, false),
                                out endTransparentForward,
                                out endTransparentSpan);
            for (var i = 0; i < boundaryLoop.Count; i++)
            {
                var vertexIndex = boundaryLoop[i];
                if (vertexIndex < 0 || vertexIndex >= vertexCount)
                    continue;
                if ((data.vertices[vertexIndex] - centerDoc).sqrMagnitude > radiusSqr)
                    continue;

                var angle = Mathf.Atan2(data.vertices[vertexIndex].y - centerDoc.y, data.vertices[vertexIndex].x - centerDoc.x);
                if (IsPointInsideCurrentJointSector(data.vertices[vertexIndex], centerDoc))
                {
                    parentRatios[vertexIndex] = 1f;
                    continue;
                }

                var bestParentRatio = 0f;
                if (hasStartGrayHit &&
                    IsAngleOnDirectedArc(angle, jointStartAngleRad, startTransparentSpan, startTransparentForward, 0.0001f))
                {
                    var rel = GetDirectedAngleDistance(jointStartAngleRad, angle, startTransparentForward);
                    var t = Mathf.Clamp01(rel / startTransparentSpan);
                    bestParentRatio = Mathf.Max(bestParentRatio, 1f - t);
                }

                if (hasEndGrayHit &&
                    IsAngleOnDirectedArc(angle, jointEndAngleRad, endTransparentSpan, endTransparentForward, 0.0001f))
                {
                    var rel = GetDirectedAngleDistance(jointEndAngleRad, angle, endTransparentForward);
                    var t = Mathf.Clamp01(rel / endTransparentSpan);
                    bestParentRatio = Mathf.Max(bestParentRatio, 1f - t);
                }

                parentRatios[vertexIndex] = bestParentRatio;
            }
        }

        var hasSecondaryBone = nearestSpriteBone >= 0 && nearestSpriteBone != parentSpriteBone;
        var centerParentRatio = hasSecondaryBone ? 0.5f : 1f;
        var centerVertexIndex = FindNearestVertexIndex(centerDoc, data.vertices);
        if (centerVertexIndex >= 0 &&
            centerVertexIndex < vertexCount &&
            (data.vertices[centerVertexIndex] - centerDoc).sqrMagnitude <= radiusSqr)
        {
            parentRatios[centerVertexIndex] = centerParentRatio;
        }

        for (var i = 0; i < vertexCount; i++)
        {
            if (!contourMask[i])
                continue;
            if ((data.vertices[i] - centerDoc).sqrMagnitude > radiusSqr)
                continue;
            if (parentRatios[i] >= 0f)
                continue;
            parentRatios[i] = IsPointInsideCurrentJointSector(data.vertices[i], centerDoc) ? 1f : 0f;
        }

        var contourSampleIndices = new List<int>();
        for (var i = 0; i < vertexCount; i++)
        {
            if (parentRatios[i] < 0f)
                continue;
            SetJointVertexWeightByParentRatio(data, i, parentSpriteBone, nearestSpriteBone, parentRatios[i]);
            if (contourMask[i] && (data.vertices[i] - centerDoc).sqrMagnitude <= radiusSqr)
                contourSampleIndices.Add(i);
        }

        var safeRadius = Mathf.Max(0.0001f, radius);
        for (var i = 0; i < vertexCount; i++)
        {
            if (contourMask[i])
                continue;
            if (i == centerVertexIndex && parentRatios[i] >= 0f)
            {
                SetJointVertexWeightByParentRatio(data, i, parentSpriteBone, nearestSpriteBone, parentRatios[i]);
                continue;
            }
            var toCenter = data.vertices[i] - centerDoc;
            var distanceSqr = toCenter.sqrMagnitude;
            if (distanceSqr > radiusSqr)
                continue;

            var targetRatio = centerParentRatio;
            if (contourSampleIndices.Count > 0)
            {
                var nearestContour = -1;
                var nearestDistanceSqr = float.MaxValue;
                for (var sampleIndex = 0; sampleIndex < contourSampleIndices.Count; sampleIndex++)
                {
                    var contourVertex = contourSampleIndices[sampleIndex];
                    var sampleSqr = (data.vertices[contourVertex] - data.vertices[i]).sqrMagnitude;
                    if (sampleSqr >= nearestDistanceSqr)
                        continue;
                    nearestDistanceSqr = sampleSqr;
                    nearestContour = contourVertex;
                }

                if (nearestContour >= 0)
                    targetRatio = Mathf.Clamp01(parentRatios[nearestContour]);
            }
            else
            {
                targetRatio = IsPointInsideCurrentJointSector(data.vertices[i], centerDoc) ? 1f : 0f;
            }

            var radialT = Mathf.Clamp01(Mathf.Sqrt(distanceSqr) / safeRadius);
            radialT = radialT * radialT * (3f - 2f * radialT);
            var parentRatio = Mathf.Lerp(centerParentRatio, targetRatio, radialT);
            parentRatios[i] = parentRatio;
            SetJointVertexWeightByParentRatio(data, i, parentSpriteBone, nearestSpriteBone, parentRatio);
        }
    }

    private void SetJointVertexWeightByParentRatio(
        LayerVertexData data,
        int vertexIndex,
        int parentSpriteBone,
        int nearestSpriteBone,
        float parentRatio)
    {
        if (data == null || data.boneWeights == null)
            return;
        if (vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
            return;

        parentRatio = Mathf.Clamp01(parentRatio);
        if (parentSpriteBone < 0 && nearestSpriteBone < 0)
            return;
        if (parentSpriteBone < 0)
            parentSpriteBone = nearestSpriteBone;
        if (nearestSpriteBone < 0 || nearestSpriteBone == parentSpriteBone)
        {
            data.boneWeights[vertexIndex] = BuildSingleBoneWeight(parentSpriteBone);
            return;
        }

        data.boneWeights[vertexIndex] = BuildLimbTwoBoneWeight(
            parentSpriteBone,
            parentRatio,
            nearestSpriteBone,
            1f - parentRatio);
    }

}
#endif
