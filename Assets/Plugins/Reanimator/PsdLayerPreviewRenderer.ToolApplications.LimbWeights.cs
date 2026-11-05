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
    private void ApplyLimbWeightsToGeometry(
        LayerVertexData targetData,
        LayerVertexData sourceData,
        LimbLine line,
        IReadOnlyList<Vector2> contourPointsDoc,
        IReadOnlyList<int> createdContourVertexIndices,
        IReadOnlyList<int> createdContourSourceIndices,
        IReadOnlyList<int> createdCenterVertexIndices,
        IReadOnlyList<int> createdShiftedVertexIndices,
        IReadOnlyList<int> createdShiftedSourceIndices)
    {
        if (targetData == null || targetData.vertices == null || targetData.vertices.Length == 0)
            return;
        if (line == null || !line.hasContourPoints || sourceData == null)
            return;

        var hasContourTargets =
            createdContourVertexIndices != null &&
            createdContourSourceIndices != null &&
            createdContourVertexIndices.Count > 0 &&
            createdContourVertexIndices.Count == createdContourSourceIndices.Count;
        var hasCenterTargets = createdCenterVertexIndices != null && createdCenterVertexIndices.Count > 0;
        var hasShiftedTargets = createdShiftedVertexIndices != null && createdShiftedVertexIndices.Count > 0;
        if (!hasContourTargets && !hasCenterTargets && !hasShiftedTargets)
            return;
        if (contourPointsDoc == null || contourPointsDoc.Count == 0)
            return;

        if (!TryResolveLimbTwoBoneSpriteIndices(targetData, line, out var parentSpriteBone, out var childSpriteBone, out var parentMidpoint, out var childMidpoint))
            return;

        EnsureBoneWeightsArraySize(targetData, targetData.vertices.Length);

        var contourPointsForWeights = new Vector2[contourPointsDoc.Count];
        for (var i = 0; i < contourPointsDoc.Count; i++)
        {
            var basePoint = ConvertPointToBaseDoc(sourceData, contourPointsDoc[i]);
            var weightPoint = basePoint;
            var vertexIndex = FindNearestVertexIndex(basePoint, targetData.vertices);
            if (vertexIndex >= 0)
            {
                if (vertexIndex < targetData.vertices.Length)
                    weightPoint = targetData.vertices[vertexIndex];
            }

            contourPointsForWeights[i] = weightPoint;
        }

        var guideStart = ConvertPointToBaseDoc(sourceData, line.contourPointA);
        var guideEnd = ConvertPointToBaseDoc(sourceData, line.contourPointB);
        var axisStart = ConvertPointToBaseDoc(sourceData, line.startDoc);
        var axisEnd = ConvertPointToBaseDoc(sourceData, line.endDoc);
        var axis = axisEnd - axisStart;
        if (axis.sqrMagnitude <= 1e-8f)
            axis = guideEnd - guideStart;
        if (axis.sqrMagnitude <= 1e-8f)
            axis = childMidpoint - parentMidpoint;
        if (axis.sqrMagnitude <= 1e-8f)
            axis = Vector2.right;
        var axisOrigin = (guideStart + guideEnd) * 0.5f;
        parentMidpoint = ConvertPointToBaseDoc(sourceData, parentMidpoint);
        childMidpoint = ConvertPointToBaseDoc(sourceData, childMidpoint);
        var positiveSideUsesParent = ReanimatorLimbElbowAlgorithms.PositiveSideUsesParent(
            axis,
            axisOrigin,
            parentMidpoint,
            childMidpoint);

        var maxPositive = 0.0001f;
        var maxNegative = 0.0001f;
        for (var i = 0; i < contourPointsForWeights.Length; i++)
        {
            var point = contourPointsForWeights[i];
            var signedDistance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, axisOrigin, point);
            if (signedDistance >= 0f)
                maxPositive = Mathf.Max(maxPositive, signedDistance);
            else
                maxNegative = Mathf.Max(maxNegative, -signedDistance);
        }

        var contourParentWeights = new float[contourPointsForWeights.Length];
        var curve = LimbCurve;
        for (var i = 0; i < contourPointsForWeights.Length; i++)
        {
            var point = contourPointsForWeights[i];
            var signedDistance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, axisOrigin, point);
            ReanimatorLimbElbowAlgorithms.EvaluateLimbSideWeights(
                signedDistance,
                maxPositive,
                maxNegative,
                positiveSideUsesParent,
                curve,
                out var parentWeight,
                out _);
            contourParentWeights[i] = parentWeight;
        }

        if (hasContourTargets)
        {
            for (var i = 0; i < createdContourVertexIndices.Count; i++)
            {
                var vertexIndex = createdContourVertexIndices[i];
                if (vertexIndex < 0 || vertexIndex >= targetData.vertices.Length)
                    continue;
                var sourceIndex = createdContourSourceIndices[i];
                if (sourceIndex < 0 || sourceIndex >= contourParentWeights.Length)
                    continue;

                var parentWeight = Mathf.Clamp01(contourParentWeights[sourceIndex]);
                var childWeight = 1f - parentWeight;
                targetData.boneWeights[vertexIndex] = BuildLimbTwoBoneWeight(parentSpriteBone, parentWeight, childSpriteBone, childWeight);
            }
        }

        if (hasShiftedTargets)
        {
            for (var i = 0; i < createdShiftedVertexIndices.Count; i++)
            {
                var vertexIndex = createdShiftedVertexIndices[i];
                if (vertexIndex < 0 || vertexIndex >= targetData.vertices.Length)
                    continue;
                if (createdShiftedSourceIndices == null || i >= createdShiftedSourceIndices.Count)
                    continue;

                var sourceContourIndex = createdShiftedSourceIndices[i];
                if (sourceContourIndex < 0 || sourceContourIndex >= contourParentWeights.Length)
                    continue;

                var parentWeight = Mathf.Clamp01(contourParentWeights[sourceContourIndex]);
                var childWeight = 1f - parentWeight;
                targetData.boneWeights[vertexIndex] = BuildLimbTwoBoneWeight(parentSpriteBone, parentWeight, childSpriteBone, childWeight);
            }
        }

        if (hasCenterTargets)
        {
            var centerVertexSet = new HashSet<int>();
            for (var i = 0; i < createdCenterVertexIndices.Count; i++)
            {
                var vertexIndex = createdCenterVertexIndices[i];
                if (vertexIndex < 0 || vertexIndex >= targetData.vertices.Length || !centerVertexSet.Add(vertexIndex))
                    continue;

                targetData.boneWeights[vertexIndex] = BuildLimbTwoBoneWeight(parentSpriteBone, 0.5f, childSpriteBone, 0.5f);
            }
        }

        var guideWeight = BuildLimbTwoBoneWeight(parentSpriteBone, 0.5f, childSpriteBone, 0.5f);
        for (var i = 0; i < targetData.vertices.Length; i++)
        {
            if (ReanimatorLimbElbowAlgorithms.IsPointOnGuideSegment(targetData.vertices[i], guideStart, guideEnd))
                targetData.boneWeights[i] = guideWeight;
        }
    }

    private bool TryResolveLimbTwoBoneSpriteIndices(
        LayerVertexData data,
        LimbLine line,
        out int parentSpriteBone,
        out int childSpriteBone,
        out Vector2 parentAnchor,
        out Vector2 childAnchor)
    {
        parentSpriteBone = -1;
        childSpriteBone = -1;
        parentAnchor = line != null ? line.contourPointA : Vector2.zero;
        childAnchor = parentAnchor;
        if (data == null || line == null || !line.hasContourPoints)
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return false;
        if (!TryResolveLimbRendererBonePair(
                line,
                out var parentRendererBone,
                out var childRendererBone,
                out _,
                out _,
                out _,
                out _))
        {
            return false;
        }

        parentAnchor = GetBoneMidpoint(bones[parentRendererBone]);
        childAnchor = GetBoneMidpoint(bones[childRendererBone]);

        var rendererToSprite = BuildRendererToSpriteBoneMap(data.spriteBoneToRendererBone);
        if (!rendererToSprite.TryGetValue(parentRendererBone, out parentSpriteBone))
            return false;
        if (!rendererToSprite.TryGetValue(childRendererBone, out childSpriteBone))
            return false;
        if (parentSpriteBone < 0 || childSpriteBone < 0 || parentSpriteBone == childSpriteBone)
            return false;
        return true;
    }

    private static BoneWeight BuildLimbTwoBoneWeight(int parentSpriteBone, float parentWeight, int childSpriteBone, float childWeight)
    {
        return ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(parentSpriteBone, parentWeight, childSpriteBone, childWeight);
    }

}
#endif
