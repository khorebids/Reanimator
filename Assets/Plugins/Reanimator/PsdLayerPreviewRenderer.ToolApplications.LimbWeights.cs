#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

    internal string ApplyBellyLimb(float radius)
    {
        if (!TryFindBoneIndexByName("belly", out var belly) ||
            !TryFindBoneIndexByName("spine", out var spine) ||
            bones[spine].parentId != belly)
            return "Belly: no belly → spine chain";

        TryGetSelectedLayer(out var selection);
        var steps = LimbContourSteps;
        var distance = LimbContourStepDistance;
        var shift = LimbTowardCenterShift;
        try
        {
            LimbContourSteps = 1;
            LimbContourStepDistance = 20.22f;
            LimbTowardCenterShift = 0.209f;

            var candidates = vertexDataByLayerSelectionKey.Values
                .Where(data => data?.layer != null && data.spriteBoneToRendererBone != null &&
                               Array.IndexOf(data.spriteBoneToRendererBone, belly) >= 0)
                .OrderByDescending(data => string.Equals(data.layer.name, "body", StringComparison.OrdinalIgnoreCase))
                .ThenBy(data => data.layer.layerId)
                .ToArray();
            foreach (var candidate in candidates)
            {
                SelectLayer(candidate.layer);
                if (ApplyLimbBoneWeights(belly, spine, true, radius, true, out _))
                    return $"Belly: {candidate.layer.name}";
            }

            throw new InvalidOperationException("Belly: no weighted sprite crosses the belly bone midpoint.");
        }
        finally
        {
            LimbContourSteps = steps;
            LimbContourStepDistance = distance;
            LimbTowardCenterShift = shift;
            SelectLayer(selection);
        }
    }

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

    private void DrawShoulderHelperPreview(PsdCoordinateSpace coordinates)
    {
        if (Event.current.type != EventType.Repaint || coordinates == null ||
            !TryGetBoneParent(selectedBoneIndex, out var child, out _, out var parent))
            return;

        var direction = GetShoulderHelperDirection(
            parent.worldRotation * Vector3.right, child.worldRotation * Vector3.right);
        var length = Mathf.Max(1f, ShoulderReferenceWidth * ShoulderFanDepth);
        var origin = GetBoneBasePoint(child);
        Vector3 start = coordinates.DocumentToView(origin);
        Vector3 end = coordinates.DocumentToView(origin + direction * length);
        var screenDirection = (end - start).normalized;
        var normal = new Vector3(-screenDirection.y, screenDirection.x);
        var shoulder = Vector3.Lerp(start, end, 0.2f);
        var width = Mathf.Min(7f, Vector3.Distance(start, end) * 0.15f);
        var outline = new[] { start, shoulder + normal * width, end, shoulder - normal * width, start };

        Handles.BeginGUI();
        using (new Handles.DrawingScope(new Color(1f, 0.72f, 0.18f)))
        {
            Handles.DrawAAPolyLine(3f, outline);
            Handles.DrawSolidDisc(start, Vector3.forward, 4f);
            Handles.DrawSolidDisc(end, Vector3.forward, 3f);
        }
        Handles.EndGUI();
        DrawShoulderFan(coordinates);
    }

    internal static Vector2 GetShoulderHelperDirection(Vector2 parentDirection, Vector2 childDirection)
    {
        var bisector = -(parentDirection.normalized + childDirection.normalized);
        if (bisector.sqrMagnitude < 1e-6f)
            bisector = new Vector2(-childDirection.y, childDirection.x);
        return bisector.normalized;
    }

    internal static bool TryGetLimbCrossSection(Vector2[] vertices, int[] triangles, int[] boundary,
        Vector2 joint, Vector2 incoming, Vector2 outgoing, out Vector2 start, out Vector2 end)
    {
        start = end = default;
        if (vertices == null || triangles == null ||
            !TryFindContainingTriangle(triangles, vertices, joint, out _, out _, out _, out _,
                out _, out _, out _, out _))
            return false;

        // Use the normal to the average chain direction through the joint.
        var tangent = incoming.normalized + outgoing.normalized;
        if (tangent.sqrMagnitude < 1e-6f)
            tangent = outgoing;
        if (tangent.sqrMagnitude < 1e-6f)
            return false;

        var normalAngle = Mathf.Atan2(tangent.y, tangent.x) + Mathf.PI * 0.5f;
        return TryGetNearestContourRayIntersection(vertices, boundary, joint, normalAngle, out start) &&
            TryGetNearestContourRayIntersection(vertices, boundary, joint, normalAngle + Mathf.PI, out end);
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
        IReadOnlyList<int> createdContourVertexIndices,
        IReadOnlyList<int> createdCenterVertexIndices,
        IReadOnlyList<int> createdShiftedVertexIndices)
    {
        if (targetData == null || targetData.vertices == null || targetData.vertices.Length == 0)
            return;
        if (line == null || !line.hasContourPoints || sourceData == null)
            return;

        if (!TryResolveLimbTwoBoneSpriteIndices(targetData, line, out var parentSpriteBone, out var childSpriteBone, out var parentMidpoint, out var childMidpoint))
            return;

        EnsureBoneWeightsArraySize(targetData, targetData.vertices.Length);

        var guideStart = ConvertPointToBaseDoc(sourceData, line.contourPointA);
        var guideEnd = ConvertPointToBaseDoc(sourceData, line.contourPointB);
        var axis = guideEnd - guideStart;
        var axisOrigin = (guideStart + guideEnd) * 0.5f;
        parentMidpoint = ConvertPointToBaseDoc(sourceData, parentMidpoint);
        childMidpoint = ConvertPointToBaseDoc(sourceData, childMidpoint);
        var positiveSideUsesParent = ReanimatorLimbElbowAlgorithms.PositiveSideUsesParent(
            axis,
            axisOrigin,
            parentMidpoint,
            childMidpoint);

        var positiveRanges = new float[2];
        var negativeRanges = new float[2];
        var across = guideEnd - guideStart;
        var curve = LimbCurve;

        int GetBank(Vector2 point) => Vector2.Dot(point - axisOrigin, across) < 0f ? 0 : 1;

        foreach (var index in createdContourVertexIndices)
        {
            var point = targetData.vertices[index];
            var bank = GetBank(point);
            var distance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, axisOrigin, point);
            positiveRanges[bank] = Mathf.Max(positiveRanges[bank], distance);
            negativeRanges[bank] = Mathf.Max(negativeRanges[bank], -distance);
        }

        void ApplyWeights(IReadOnlyList<int> indices)
        {
            foreach (var index in indices)
            {
                var point = targetData.vertices[index];
                var bank = GetBank(point);
                var distance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, axisOrigin, point);
                var range = distance >= 0f ? positiveRanges[bank] : negativeRanges[bank];
                var parentWeight = ReanimatorLimbElbowAlgorithms.EvaluateAccordionParentWeight(
                    distance, range, positiveSideUsesParent, curve);
                targetData.boneWeights[index] = BuildLimbTwoBoneWeight(
                    parentSpriteBone, parentWeight, childSpriteBone, 1f - parentWeight);
            }
        }

        ApplyWeights(createdContourVertexIndices);
        ApplyWeights(createdShiftedVertexIndices);

        var centerWeight = BuildLimbTwoBoneWeight(parentSpriteBone, 0.5f, childSpriteBone, 0.5f);
        foreach (var index in createdCenterVertexIndices)
            targetData.boneWeights[index] = centerWeight;
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
