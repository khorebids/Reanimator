#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    private void ApplyLimbPreviewTwoBoneInfluence(LayerVertexData previewData, LayerVertexData sourceData, LimbLine line)
    {
        if (previewData == null || previewData.vertices == null || previewData.vertices.Length == 0)
            return;
        if (sourceData == null || line == null || !line.hasContourPoints)
            return;
        if (previewData.spriteBoneToRendererBone == null || previewData.spriteBoneToRendererBone.Length == 0)
            return;
        if (!TryResolveLimbRendererBonePair(
                line,
                out var parentRendererBone,
                out var childRendererBone,
                out _,
                out _,
                out _,
                out _))
        {
            return;
        }

        var rendererToSprite = BuildRendererToSpriteBoneMap(previewData.spriteBoneToRendererBone);
        if (!rendererToSprite.TryGetValue(parentRendererBone, out var parentSpriteBone))
            return;
        if (!rendererToSprite.TryGetValue(childRendererBone, out var childSpriteBone))
            return;
        if (parentSpriteBone < 0 || childSpriteBone < 0 || parentSpriteBone == childSpriteBone)
            return;

        EnsureBoneWeightsArraySize(previewData, previewData.vertices.Length);

        var guideStart = ConvertPointToBaseDoc(sourceData, line.contourPointA);
        var guideEnd = ConvertPointToBaseDoc(sourceData, line.contourPointB);
        var anchor = (guideStart + guideEnd) * 0.5f;
        var axisStart = ConvertPointToBaseDoc(sourceData, line.startDoc);
        var axisEnd = ConvertPointToBaseDoc(sourceData, line.endDoc);
        var axis = axisEnd - axisStart;
        if (axis.sqrMagnitude <= 1e-8f)
        {
            var contourA = ConvertPointToBaseDoc(sourceData, line.contourPointA);
            var contourB = ConvertPointToBaseDoc(sourceData, line.contourPointB);
            axis = contourB - contourA;
        }
        if (axis.sqrMagnitude <= 1e-8f)
            axis = Vector2.right;

        var parentMidpoint = ConvertPointToBaseDoc(sourceData, GetBoneMidpoint(bones[parentRendererBone]));
        var childMidpoint = ConvertPointToBaseDoc(sourceData, GetBoneMidpoint(bones[childRendererBone]));
        var positiveSideUsesParent = ReanimatorLimbElbowAlgorithms.PositiveSideUsesParent(
            axis,
            anchor,
            parentMidpoint,
            childMidpoint);

        var maxPositive = 0.0001f;
        var maxNegative = 0.0001f;
        for (var i = 0; i < previewData.vertices.Length; i++)
        {
            var side = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, anchor, previewData.vertices[i]);
            if (side >= 0f)
                maxPositive = Mathf.Max(maxPositive, side);
            else
                maxNegative = Mathf.Max(maxNegative, -side);
        }

        var curve = LimbCurve;
        for (var i = 0; i < previewData.vertices.Length; i++)
        {
            var vertex = previewData.vertices[i];
            if (ReanimatorLimbElbowAlgorithms.IsPointOnGuideSegment(vertex, guideStart, guideEnd))
            {
                previewData.boneWeights[i] = BuildLimbTwoBoneWeight(
                    parentSpriteBone,
                    0.5f,
                    childSpriteBone,
                    0.5f);
                continue;
            }

            var signedDistance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, anchor, vertex);
            ReanimatorLimbElbowAlgorithms.EvaluateLimbSideWeights(
                signedDistance,
                maxPositive,
                maxNegative,
                positiveSideUsesParent,
                curve,
                out var parentWeight,
                out var childWeight);

            previewData.boneWeights[i] = BuildLimbTwoBoneWeight(
                parentSpriteBone,
                parentWeight,
                childSpriteBone,
                childWeight);
        }
    }

    private bool AdvanceLimbPreviewVisibility(LimbPreviewCache cache, bool interactive)
    {
        if (cache == null)
            return true;

        var targetTriangles = ResolveLimbPreviewTarget(cache.totalTriangleCount, interactive, 0.35f, 64);
        var targetInnerPairs = ResolveLimbPreviewTarget(cache.totalInnerPairCount, interactive, 0.4f, 96);
        var targetBoundaryPairs = ResolveLimbPreviewTarget(cache.totalBoundaryPairCount, interactive, 0.5f, 96);
        var targetPoints = ResolveLimbPreviewTarget(cache.totalPointCount, interactive, 0.45f, 96);

        cache.visibleTriangleCount = MoveTowards(cache.visibleTriangleCount, targetTriangles, interactive ? 64 : 192);
        cache.visibleInnerPairCount = MoveTowards(cache.visibleInnerPairCount, targetInnerPairs, interactive ? 96 : 256);
        cache.visibleBoundaryPairCount = MoveTowards(cache.visibleBoundaryPairCount, targetBoundaryPairs, interactive ? 96 : 256);
        cache.visiblePointCount = MoveTowards(cache.visiblePointCount, targetPoints, interactive ? 96 : 256);

        return cache.visibleTriangleCount >= targetTriangles &&
               cache.visibleInnerPairCount >= targetInnerPairs &&
               cache.visibleBoundaryPairCount >= targetBoundaryPairs &&
               cache.visiblePointCount >= targetPoints;
    }

    private static int ResolveLimbPreviewTarget(int totalCount, bool interactive, float interactiveFraction, int interactiveMin)
    {
        if (totalCount <= 0)
            return 0;
        if (!interactive)
            return totalCount;
        var scaled = Mathf.CeilToInt(totalCount * Mathf.Clamp01(interactiveFraction));
        return Mathf.Clamp(scaled, Mathf.Min(interactiveMin, totalCount), totalCount);
    }

    private static int MoveTowards(int current, int target, int maxStep)
    {
        if (current < target)
            return Mathf.Min(target, current + Mathf.Max(1, maxStep));
        if (current > target)
            return Mathf.Max(target, current - Mathf.Max(1, maxStep));
        return current;
    }

    private static int ComputeCurveHash(AnimationCurve curve)
    {
        if (curve == null)
            return 0;

        unchecked
        {
            var hash = 17;
            var keys = curve.keys;
            hash = hash * 31 + (keys != null ? keys.Length : 0);
            if (keys != null)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                    hash = hash * 31 + key.inWeight.GetHashCode();
                    hash = hash * 31 + key.outWeight.GetHashCode();
                    hash = hash * 31 + ((int)key.weightedMode).GetHashCode();
                }
            }

            hash = hash * 31 + ((int)curve.preWrapMode).GetHashCode();
            hash = hash * 31 + ((int)curve.postWrapMode).GetHashCode();
            return hash;
        }
    }

}
#endif
