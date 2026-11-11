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
    private (LayerVertexData data, Vector2[] vertices, BoneWeight[] weights, int middle, int child,
        int revision, int pose, int steps, float distance, float shift, int curve, float radius, bool inner) limbBonePreviewKey;
    private LayerVertexData limbBonePreviewData;
    private LimbLine limbBonePreviewLine;

    public bool ApplyLimbBoneWeights(int middle, int child, bool useCull, float radius, bool removeInner, out string message)
    {
        if (!TryBuildLimbBonePreview(middle, child, useCull, radius, removeInner, out var data, out var staged, out message))
            return false;

        RegisterUndo("Apply Limb Bone Weights");
        data.vertices = staged.vertices;
        data.vertexUvs = staged.vertexUvs;
        data.triangles = staged.triangles;
        data.allEdgePairs = staged.allEdgePairs;
        data.boundaryEdgePairs = staged.boundaryEdgePairs;
        data.spriteBoneToRendererBone = staged.spriteBoneToRendererBone;
        data.bindBoneInverseMatrices = staged.bindBoneInverseMatrices;
        data.usesSpriteBindPoses = staged.usesSpriteBindPoses;
        data.boneWeights = staged.boneWeights;
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, BuildSelectionKey(data.layer), BuildKey(data.layer.name, data.layer.layerId));
        return true;
    }

    private bool TryBuildLimbBonePreview(
        int middle, int child, bool useCull, float radius, bool removeInner,
        out LayerVertexData data, out LayerVertexData staged, out string message)
    {
        data = null;
        staged = null;
        message = "Select a bone with a parent and a direct child.";
        if (middle < 0 || middle >= bones.Count || bones[middle] == null)
            return false;
        var parent = bones[middle].parentId;
        if (parent < 0 || parent >= bones.Count || parent == middle || bones[parent] == null)
            return false;
        if (child < 0 || child >= bones.Count || bones[child]?.parentId != middle)
            child = bones.FindIndex(bone => bone != null && bone.parentId == middle);
        if (child < 0 || child == middle || child == parent)
            return false;

        message = "Select a sprite with skin weights.";
        if (!TryGetSelectedLayer(out var layer))
            return false;
        var selectionKey = BuildSelectionKey(layer);
        var stableKey = BuildKey(layer.name, layer.layerId);
        if (!TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) ||
            data?.vertices == null || data.boneWeights == null || data.vertices.Length != data.boneWeights.Length ||
            data.spriteBoneToRendererBone == null)
            return false;

        var key = (data, data.vertices, data.boneWeights, middle, child, radiusCullPreviewRevision, bonePoseVersion,
            LimbContourSteps, LimbContourStepDistance, LimbTowardCenterShift, ComputeCurveHash(LimbCurve),
            useCull ? radius : 0f, useCull && removeInner);
        if (limbBonePreviewData != null && limbBonePreviewKey.Equals(key))
        {
            staged = limbBonePreviewData;
            message = "Limb Bone applied";
            return true;
        }

        message = "The bone midpoint must cross the selected sprite contour.";
        if (!TryBuildLimbBoneGuide(data, selectionKey, stableKey, middle, out var line, out var axis))
            return false;

        var center = GetLimbContourMidpoint(line);
        var generated = new List<LimbGeneratedPoint>();
        var contour = new List<Vector2>();
        if (!TryBuildLimbGeneratedPoints(line, data, LimbContourSteps, LimbContourStepDistance,
                LimbTowardCenterShift, generated, contour))
            return false;

        var working = CloneProjectorLayerData(data);
        var centerBase = ConvertPointToBaseDoc(data, center);
        var startBase = ConvertPointToBaseDoc(data, line.contourPointA);
        var endBase = ConvertPointToBaseDoc(data, line.contourPointB);
        if (!useCull || !TryApplyLimbWithAdaptiveRadiusCull(working, line, generated, contour, radius, true,
                removeInner, out _, centerBase))
            TryApplyLimbPointsToLayerData(working, working, line, generated, contour, out _, stageOnly: true);

        var rendererBones = new[] { parent, middle, child };
        var spriteBones = new int[rendererBones.Length];
        for (var index = 0; index < rendererBones.Length; index++)
        {
            var rendererBone = rendererBones[index];
            spriteBones[index] = Array.IndexOf(working.spriteBoneToRendererBone, rendererBone);
            if (spriteBones[index] < 0 && !TryAppendSkinningTargetBone(working, rendererBone, out spriteBones[index]))
            {
                message = "Could not bind the selected bone chain to the sprite.";
                return false;
            }
        }

        MarkSkinningWeightsChanged(working);
        var points = GetLayerVerticesForDraw(working);
        var parentRange = Mathf.Max(0.01f, Vector2.Distance(center, GetBoneMidpoint(bones[parent])));
        var childRange = Mathf.Max(0.01f, Vector2.Distance(center, GetBoneMidpoint(bones[child])));
        var curve = LimbCurve;
        var weights = new BoneWeight[working.vertices.Length];
        for (var index = 0; index < weights.Length; index++)
        {
            var point = working.vertices[index];
            var onGuide = (point - startBase).sqrMagnitude <= 0.0001f ||
                          (point - centerBase).sqrMagnitude <= 0.0001f ||
                          (point - endBase).sqrMagnitude <= 0.0001f;
            var distance = onGuide ? 0f : Vector2.Dot(points[index] - center, axis);
            weights[index] = EvaluateLimbBoneGuideWeight(distance, parentRange, childRange,
                spriteBones[0], spriteBones[1], spriteBones[2], curve);
        }
        working.boneWeights = weights;
        MarkSkinningWeightsChanged(working);

        limbBonePreviewLine = line;
        limbBonePreviewKey = key;
        staged = limbBonePreviewData = working;
        message = "Limb Bone applied";
        return true;
    }

    private bool DrawLimbBonePreview(PsdCoordinateSpace coordinates, float alpha, int child, bool useCull, float radius, bool removeInner)
    {
        if (Event.current.type != EventType.Repaint ||
            !TryBuildLimbBonePreview(selectedBoneIndex, child, useCull, radius, removeInner, out _, out var staged, out _))
            return false;

        var vertices = GetLayerVerticesForDraw(staged);
        var toView = CreateLayerVertexToViewMapper(coordinates, staged, vertices);
        DrawBoneInfluenceOnGeometryTriangles(toView, vertices, staged.boneWeights, staged.triangles,
            staged.spriteBoneToRendererBone, alpha * 0.7f, int.MaxValue, 1f);

        Handles.BeginGUI();
        var previousColor = Handles.color;
        var boundary = BuildEdgeKeySet(staged.boundaryEdgePairs, vertices.Length);
        for (var index = 0; index + 1 < staged.allEdgePairs.Length; index += 2)
        {
            var a = staged.allEdgePairs[index];
            var b = staged.allEdgePairs[index + 1];
            if (a < 0 || a >= vertices.Length || b < 0 || b >= vertices.Length)
                continue;
            Handles.color = boundary != null && boundary.Contains(MakeEdgeKey(a, b))
                ? new Color(1f, 0.56f, 0.08f, alpha)
                : new Color(0.35f, 0.98f, 1f, alpha);
            Handles.DrawLine(toView(vertices[a]), toView(vertices[b]));
        }
        Handles.color = new Color(1f, 0.9f, 0.25f, alpha);
        Handles.DrawAAPolyLine(2f,
            coordinates.DocumentToView(limbBonePreviewLine.contourPointA),
            coordinates.DocumentToView(limbBonePreviewLine.contourPointB));
        Handles.color = previousColor;
        Handles.EndGUI();
        return true;
    }

    private bool TryBuildLimbBoneGuide(
        LayerVertexData data, string selectionKey, string stableKey, int middle, out LimbLine line, out Vector2 axis)
    {
        var bone = bones[middle];
        var center = GetBoneMidpoint(bone);
        axis = (Vector2)(bone.worldRotation * Vector3.right);
        axis.Normalize();
        var normal = new Vector2(-axis.y, axis.x);
        var bounds = BuildAutoLimbBounds(data.vertices);
        var halfLength = Mathf.Max(16f, new Vector2(bounds.width, bounds.height).magnitude);
        line = BuildAutoLimbLineCandidate(stableKey, selectionKey, center, normal, halfLength, bone.parentId, middle);
        if (!TryGetElbowContourPoints(line.startDoc, line.endDoc, data, out line.contourPointA, out line.contourPointB) ||
            Vector2.Dot(line.contourPointA - center, normal) > 0.01f ||
            Vector2.Dot(line.contourPointB - center, normal) < -0.01f)
            return false;

        line.hasContourPoints = true;
        return true;
    }

    private static BoneWeight EvaluateLimbBoneGuideWeight(
        float distance, float parentRange, float childRange, int parent, int middle, int child, AnimationCurve curve)
    {
        if (Mathf.Abs(distance) <= 0.01f)
            return BuildSingleBoneWeight(middle);

        var towardParent = distance < 0f;
        var t = Mathf.Clamp01(Mathf.Abs(distance) / (towardParent ? parentRange : childRange));
        var middleWeight = t >= 1f ? 0f : Mathf.Clamp01(curve.Evaluate(1f - t));
        return BuildLimbTwoBoneWeight(middle, middleWeight, towardParent ? parent : child, 1f - middleWeight);
    }

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
