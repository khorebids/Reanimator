#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

internal static partial class ReanimatorSelfTests
{
    private static void BoneShapeDrawerRadiiScaleWithThickness()
    {
        AssertFloat(5.2f, BoneShapeDrawer.GetBaseRadius(0.1f), 0.0001f, "Base radius minimum.");
        AssertFloat(1f, BoneShapeDrawer.GetTipRadius(0.1f), 0.0001f, "Tip radius minimum.");
        AssertFloat(11.44f, BoneShapeDrawer.GetBaseRadius(4f), 0.0001f, "Base radius should scale with thickness.");
        AssertFloat(2.1f, BoneShapeDrawer.GetTipRadius(4f), 0.0001f, "Tip radius should scale with thickness.");
    }

    private static void BoneShapeDrawerHitTestsBaseBodyAndTip()
    {
        var start = new Vector2(0f, 0f);
        var end = new Vector2(40f, 0f);
        var thickness = 2f;

        AssertTrue(BoneShapeDrawer.IsPointOverBase(new Vector2(6f, 0f), start, thickness, 1f), "Base hit inside radius.");
        AssertFalse(BoneShapeDrawer.IsPointOverBase(new Vector2(7f, 0f), start, thickness, 1f), "Base hit outside radius.");

        AssertTrue(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(20f, 3.35f), start, end, thickness, 1f), "Body hit near upper tangent.");
        AssertTrue(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(20f, -3.35f), start, end, thickness, 1f), "Body hit near lower tangent.");
        AssertFalse(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(20f, 7f), start, end, thickness, 1f), "Body hit outside tangent band.");

        AssertTrue(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(40f, 1.9f), start, end, thickness, 1f), "Tip hit inside radius.");
        AssertFalse(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(40f, 5f), start, end, thickness, 1f), "Tip hit outside radius.");
    }

    private static void BoneShapeDrawerDetectsChildParentTipHover()
    {
        var start = new Vector2(0f, 0f);
        var end = new Vector2(40f, 0f);
        var thickness = 2f;

        AssertTrue(BoneShapeDrawer.IsPointOverTip(new Vector2(40f, 2f), end, thickness, 1f), "Tip hover should accept points inside the child-parent snap radius.");
        AssertFalse(BoneShapeDrawer.IsPointOverTip(new Vector2(20f, 0f), end, thickness, 1f), "Tip hover should not accept the bone body.");
        AssertFalse(BoneShapeDrawer.IsPointOverTip(new Vector2(0f, 0f), end, thickness, 1f), "Tip hover should not accept the bone base.");
    }

    private static void BoneShapeDrawerFallsBackForShortBones()
    {
        var start = new Vector2(0f, 0f);
        var end = new Vector2(1f, 0f);
        var thickness = 2f;

        AssertTrue(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(0.5f, 2.5f), start, end, thickness, 1f), "Short bone fallback segment hit.");
        AssertFalse(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(0.5f, 3.5f), start, end, thickness, 1f), "Short bone fallback segment miss.");
        AssertTrue(BoneShapeDrawer.IsPointOverBodyOrTip(new Vector2(1f, 1.5f), start, end, thickness, 1f), "Short bone tip still hits.");
    }

    private static void LimbElbowAlgorithmsNormalizeTwoBoneWeights()
    {
        var weight = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(2, 0.8f, 5, 0.4f);
        AssertEqual(2, weight.boneIndex0, "Parent sprite bone.");
        AssertEqual(5, weight.boneIndex1, "Child sprite bone.");
        AssertFloat(2f / 3f, weight.weight0, 0.0001f, "Parent normalized weight.");
        AssertFloat(1f / 3f, weight.weight1, 0.0001f, "Child normalized weight.");
        AssertFloat(1f, SumWeight(weight), 0.0001f, "Two-bone weight sum.");
        AssertFloat(0f, weight.weight2, 0.0001f, "Third weight should be empty.");
        AssertFloat(0f, weight.weight3, 0.0001f, "Fourth weight should be empty.");

        var fallback = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(3, 0f, 4, 0f);
        AssertFloat(0.5f, fallback.weight0, 0.0001f, "Zero weights should fall back to parent half.");
        AssertFloat(0.5f, fallback.weight1, 0.0001f, "Zero weights should fall back to child half.");

        var clamped = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(3, -1f, 4, 3f);
        AssertFloat(0f, clamped.weight0, 0.0001f, "Negative parent weight should clamp.");
        AssertFloat(1f, clamped.weight1, 0.0001f, "Child weight should clamp and normalize.");
    }

    private static void LimbWeightsUseBoneSidesAndStayHalfOnGuideLine()
    {
        var axis = new Vector2(0f, 10f);
        var origin = Vector2.zero;
        var parentMidpoint = new Vector2(-5f, 0f);
        var childMidpoint = new Vector2(5f, 0f);
        var curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var positiveSideUsesParent = ReanimatorLimbElbowAlgorithms.PositiveSideUsesParent(
            axis,
            origin,
            parentMidpoint,
            childMidpoint);

        AssertTrue(positiveSideUsesParent, "Parent midpoint should assign the positive side to the parent bone.");
        var lineDistance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, origin, new Vector2(0f, 4f));
        AssertFloat(0f, lineDistance, 0.0001f, "Every point along the Limb guide line should have zero perpendicular distance.");
        ReanimatorLimbElbowAlgorithms.EvaluateLimbSideWeights(
            lineDistance,
            5f,
            5f,
            positiveSideUsesParent,
            curve,
            out var parentWeight,
            out var childWeight);
        AssertFloat(0.5f, parentWeight, 0.0001f, "Guide-line parent weight should always be half.");
        AssertFloat(0.5f, childWeight, 0.0001f, "Guide-line child weight should always be half.");

        var parentSideDistance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(axis, origin, parentMidpoint);
        ReanimatorLimbElbowAlgorithms.EvaluateLimbSideWeights(parentSideDistance, 5f, 5f, positiveSideUsesParent, curve, out parentWeight, out childWeight);
        AssertFloat(1f, parentWeight, 0.0001f, "Parent midpoint side should be parent-dominant.");
        AssertFloat(0f, childWeight, 0.0001f, "Parent midpoint side should exclude child at the outer edge.");

        var reversedAxis = -axis;
        var reversedPositiveSideUsesParent = ReanimatorLimbElbowAlgorithms.PositiveSideUsesParent(
            reversedAxis,
            origin,
            parentMidpoint,
            childMidpoint);
        var reversedParentDistance = ReanimatorLimbElbowAlgorithms.SignedDistanceToLine(reversedAxis, origin, parentMidpoint);
        AssertFalse(reversedPositiveSideUsesParent, "Reversing the guide should reverse its sign assignment.");
        ReanimatorLimbElbowAlgorithms.EvaluateLimbSideWeights(reversedParentDistance, 5f, 5f, reversedPositiveSideUsesParent, curve, out parentWeight, out childWeight);
        AssertFloat(1f, parentWeight, 0.0001f, "Reversing guide endpoints must not swap physical bone dominance.");
        AssertFloat(0f, childWeight, 0.0001f, "Reversed guide should still exclude child on the parent side.");
    }

    private static void LimbGuideSegmentIncludesMergedGeometryPoints()
    {
        var start = new Vector2(2f, 3f);
        var end = new Vector2(12f, 3f);

        AssertTrue(ReanimatorLimbElbowAlgorithms.IsPointOnGuideSegment(start, start, end), "Guide start should belong to the guide segment.");
        AssertTrue(ReanimatorLimbElbowAlgorithms.IsPointOnGuideSegment(new Vector2(7f, 3f), start, end), "Guide interior should belong to the guide segment.");
        AssertTrue(
            ReanimatorLimbElbowAlgorithms.IsPointOnGuideSegment(new Vector2(7f, 3.009f), start, end),
            "A vertex reused by the geometry merge tolerance should remain on the guide segment.");
        AssertFalse(
            ReanimatorLimbElbowAlgorithms.IsPointOnGuideSegment(new Vector2(7f, 3.011f), start, end),
            "A vertex outside the geometry merge tolerance should not belong to the guide segment.");
        AssertFalse(
            ReanimatorLimbElbowAlgorithms.IsPointOnGuideSegment(new Vector2(12.011f, 3f), start, end),
            "The finite guide segment should not affect vertices beyond its endpoints.");
    }

    private static void LimbContourReplacementUsesGeneratedVertices()
    {
        var sourceVertices = new[]
        {
            new Vector2(-10f, -5f),
            new Vector2(-2f, -5f),
            new Vector2(2f, -5f),
            new Vector2(10f, -5f),
            new Vector2(10f, 5f),
            new Vector2(2f, 5f),
            new Vector2(-2f, 5f),
            new Vector2(-10f, 5f)
        };
        var sourceLoop = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var removeMask = new[] { false, true, true, false, false, true, true, false };
        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out var keptCount);
        var replacementPoints = new[]
        {
            sourceVertices[1],
            sourceVertices[2],
            sourceVertices[5],
            sourceVertices[6]
        };
        var replacementIndices = new[] { keptCount, keptCount + 1, keptCount + 2, keptCount + 3 };

        var loop = ReanimatorLimbElbowAlgorithms.BuildContourReplacementLoop(
            sourceLoop,
            sourceVertices,
            removeMask,
            remap,
            replacementPoints,
            replacementIndices,
            keptCount + replacementIndices.Length);

        AssertArray(
            new[] { 0, 4, 5, 1, 2, 6, 7, 3 },
            loop,
            "Removed contour vertices should be replaced by generated Limb vertices in source perimeter order.");
    }

    private static void LimbElbowAlgorithmsBuildLinearInteriorPoints()
    {
        var points = new List<Vector2>();
        ReanimatorLimbElbowAlgorithms.BuildLinearInteriorPoints(new Vector2(0f, 0f), new Vector2(8f, 4f), 3, points);

        AssertEqual(3, points.Count, "Linear interior point count.");
        AssertVector(new Vector2(2f, 1f), points[0], 0.0001f, "First linear interior point.");
        AssertVector(new Vector2(4f, 2f), points[1], 0.0001f, "Second linear interior point.");
        AssertVector(new Vector2(6f, 3f), points[2], 0.0001f, "Third linear interior point.");

        AssertTrue(
            ReanimatorLimbElbowAlgorithms.TryBuildElbowArcInteriorPoints(new Vector2(0f, 0f), new Vector2(8f, 0f), 2, 0f, points),
            "Zero bend should build linear fallback.");
        AssertVector(new Vector2(8f / 3f, 0f), points[0], 0.0001f, "Zero bend first fallback point.");
        AssertVector(new Vector2(16f / 3f, 0f), points[1], 0.0001f, "Zero bend second fallback point.");

        AssertFalse(
            ReanimatorLimbElbowAlgorithms.TryBuildElbowArcInteriorPoints(Vector2.zero, Vector2.zero, 2, 1f, points),
            "Zero-length chord should fail.");
        AssertEqual(0, points.Count, "Failed arc should clear output.");
    }

    private static void LimbElbowAlgorithmsBuildSignedArcPoints()
    {
        var points = new List<Vector2>();
        AssertTrue(
            ReanimatorLimbElbowAlgorithms.TryBuildElbowArcInteriorPoints(new Vector2(0f, 0f), new Vector2(10f, 0f), 3, 1f, points),
            "Positive bend arc should build.");

        AssertEqual(3, points.Count, "Positive bend point count.");
        AssertTrue(points[0].x > 0f && points[0].x < points[1].x, "Positive bend x ordering first.");
        AssertTrue(points[1].x < points[2].x && points[2].x < 10f, "Positive bend x ordering second.");
        AssertTrue(points[0].y > 0f && points[1].y > 0f && points[2].y > 0f, "Positive bend should arc above chord.");
        AssertVector(new Vector2(5f, 2.5f), points[1], 0.0001f, "Positive bend middle point.");

        AssertTrue(
            ReanimatorLimbElbowAlgorithms.TryBuildElbowArcInteriorPoints(new Vector2(0f, 0f), new Vector2(10f, 0f), 1, -1f, points),
            "Negative bend arc should build.");
        AssertEqual(1, points.Count, "Negative bend point count.");
        AssertVector(new Vector2(5f, -2.5f), points[0], 0.0001f, "Negative bend middle point.");
    }

    private static void LimbElbowAlgorithmsResolveCircleAndSweep()
    {
        AssertTrue(
            ReanimatorLimbElbowAlgorithms.TryComputeCircleFromThreePoints(new Vector2(0f, 0f), new Vector2(5f, 2.5f), new Vector2(10f, 0f), out var center),
            "Circle through arc points should resolve.");
        AssertVector(new Vector2(5f, -3.75f), center, 0.0001f, "Circle center.");

        AssertFalse(
            ReanimatorLimbElbowAlgorithms.TryComputeCircleFromThreePoints(new Vector2(0f, 0f), new Vector2(5f, 0f), new Vector2(10f, 0f), out _),
            "Collinear circle points should fail.");

        AssertFloat(Mathf.PI * 0.5f, ReanimatorLimbElbowAlgorithms.PositiveDeltaAngle(Mathf.PI * 1.75f, Mathf.PI * 0.25f), 0.0001f, "Positive delta should wrap.");

        var angleA = Mathf.Atan2(0f - center.y, 0f - center.x);
        var angleB = Mathf.Atan2(0f - center.y, 10f - center.x);
        var angleM = Mathf.Atan2(2.5f - center.y, 5f - center.x);
        AssertTrue(ReanimatorLimbElbowAlgorithms.TryResolveArcSweepThroughPoint(angleA, angleB, angleM, out var direction, out var sweep), "Arc sweep should resolve through control point.");
        AssertFloat(-1f, direction, 0.0001f, "Arc direction.");
        AssertFloat(1.85459f, sweep, 0.0001f, "Arc sweep.");
    }

    private static void LimbBoneResolutionSkipsNearestBoneWithoutPair()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "orphan", -1, new Vector2(-4f, 0f)),
            BuildVisibleWorldBone(1, "upper", -1, Vector2.zero),
            BuildVisibleWorldBone(2, "lower", 1, new Vector2(0f, 8f))
        });

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 1, 2 });

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(0f, -4f));
        SetPrivateField(line, "endDoc", new Vector2(0f, 4f));
        SetPrivateField(line, "contourPointA", new Vector2(-4f, 0f));
        SetPrivateField(line, "contourPointB", new Vector2(4f, 0f));

        AssertTrue(
            TryResolveLimbTwoBoneSpriteIndicesForTest(renderer, data, line, out var parentSpriteBone, out var childSpriteBone),
            "Limb bone resolution should ignore the nearest orphan bone and still find the valid pair.");
        AssertEqual(0, parentSpriteBone, "Resolved parent sprite bone should come from the valid upper limb bone.");
        AssertEqual(1, childSpriteBone, "Resolved child sprite bone should come from the valid lower limb bone.");
    }

    private static void LimbBoneResolutionUsesContourMidpointInsteadOfFirstSide()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "left_upper", -1, new Vector2(-5f, 0f)),
            BuildVisibleWorldBone(1, "left_lower", 0, new Vector2(-5f, 8f)),
            BuildVisibleWorldBone(2, "center_upper", -1, Vector2.zero),
            BuildVisibleWorldBone(3, "center_lower", 2, new Vector2(0f, 8f))
        });

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1, 2, 3 });

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(0f, -4f));
        SetPrivateField(line, "endDoc", new Vector2(0f, 6f));
        SetPrivateField(line, "contourPointA", new Vector2(-5f, 1f));
        SetPrivateField(line, "contourPointB", new Vector2(5f, 1f));

        AssertTrue(
            TryResolveLimbTwoBoneSpriteIndicesForTest(renderer, data, line, out var parentSpriteBone, out var childSpriteBone),
            "Limb bone resolution should evaluate the limb centerline, not just the first contour side.");
        AssertEqual(2, parentSpriteBone, "Resolved parent sprite bone should come from the centered chain.");
        AssertEqual(3, childSpriteBone, "Resolved child sprite bone should come from the centered chain.");
    }

    private static void LimbBoneResolutionPrefersChildJointOverAdjacentSegment()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "lower", 2, new Vector2(0f, 10f)),
            BuildVisibleWorldBone(1, "upper", -1, new Vector2(0f, -10f)),
            BuildVisibleWorldBone(2, "middle", 1, Vector2.zero)
        });

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1, 2 });

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(-5f, 0f));
        SetPrivateField(line, "endDoc", new Vector2(5f, 0f));
        SetPrivateField(line, "contourPointA", new Vector2(-5f, 0f));
        SetPrivateField(line, "contourPointB", new Vector2(5f, 0f));

        AssertTrue(
            TryResolveLimbTwoBoneSpriteIndicesForTest(renderer, data, line, out var parentSpriteBone, out var childSpriteBone),
            "Limb should resolve the hierarchy pair whose child base is the guide joint.");
        AssertEqual(1, parentSpriteBone, "Joint should resolve the upper parent even when the adjacent pair appears first in the array.");
        AssertEqual(2, childSpriteBone, "Joint should resolve the middle child rather than the next lower child.");
    }

    private static void LimbGuideSegmentsStayOnResolvedBonePair()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "left_upper", -1, new Vector2(-5f, 0f)),
            BuildVisibleWorldBone(1, "left_lower", 0, new Vector2(-5f, 8f)),
            BuildVisibleWorldBone(2, "center_upper", -1, Vector2.zero),
            BuildVisibleWorldBone(3, "center_lower", 2, new Vector2(0f, 8f))
        });

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(0f, -4f));
        SetPrivateField(line, "endDoc", new Vector2(0f, 6f));
        SetPrivateField(line, "contourPointA", new Vector2(-5f, 1f));
        SetPrivateField(line, "contourPointB", new Vector2(5f, 1f));

        AssertTrue(
            TryGetLimbNearestBoneGuideSegmentForTest(renderer, line, out var guideStart, out var guideEnd, out var nearestBoneIndex),
            "Nearest limb guide segment should resolve from the centered limb pair.");
        AssertVector(new Vector2(0f, 1f), guideStart, 0.0001f, "Nearest guide anchor should use the limb midpoint.");
        AssertVector(Vector2.zero, guideEnd, 0.0001f, "Nearest guide endpoint should stay on the centered parent bone.");
        AssertEqual(2, nearestBoneIndex, "Nearest guide should resolve the centered parent bone.");

        AssertTrue(
            TryGetLimbNearestChildGuideSegmentForTest(renderer, line, nearestBoneIndex, out var childGuideStart, out var childGuideEnd),
            "Second limb guide segment should stay on the same resolved limb pair.");
        AssertVector(new Vector2(0f, 1f), childGuideStart, 0.0001f, "Child guide anchor should match the resolved midpoint.");
        AssertVector(new Vector2(0f, 8f), childGuideEnd, 0.0001f, "Child guide endpoint should stay on the centered child bone.");
    }

    private static void LimbApplyFailsWhenNoVerticesAreCreated()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("limb-duplicates", 916, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "cachedPath", "Assets/limb-duplicates.psd");

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var stableKey = (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(5f, 0f)
        });
        SetPrivateField(data, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "layerSelectionKey", selectionKey);
        SetPrivateField(line, "layerStableKey", stableKey);
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(0f, 0f));
        SetPrivateField(line, "endDoc", new Vector2(10f, 0f));
        SetPrivateField(line, "contourPointA", new Vector2(0f, 0f));
        SetPrivateField(line, "contourPointB", new Vector2(10f, 0f));
        var limbLines = (System.Collections.IList)GetPrivateField<object>(renderer, "limbLines");
        limbLines.Add(line);
        SetPrivateField(renderer, "limbSelectedLineIndex", 0);
        renderer.LimbContourSteps = 0;
        renderer.LimbTowardCenterShift = 0f;

        AssertFalse(
            renderer.ApplySelectedLimb(false, 0f, true, true, out var error),
            "Limb Apply should not report success when all generated points already exist.");
        AssertEqual("Limb did not add any mesh vertices.", error, "Duplicate-only Limb apply should explain why it failed.");
        AssertEqual(1, GetPrivateListCount(renderer, "limbLines"), "Failed Limb apply should keep the editable line.");
        AssertEqual(3, GetPrivateField<Vector2[]>(data, "vertices").Length, "Failed Limb apply should not mutate mesh vertex count.");
    }

    private static void LimbApplyCreatesPendingGeometryPatch()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("limb-pending-patch", 917, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "cachedPath", "Assets/limb-pending-patch.psd");
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "upper", -1, new Vector2(0f, 0f)),
            BuildVisibleWorldBone(1, "lower", 0, new Vector2(10f, 0f))
        });

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var stableKey = (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(5f, 8f)
        });
        SetPrivateField(data, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", (Matrix4x4[])null);
        SetPrivateField(data, "hasSkinning", false);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "layerSelectionKey", selectionKey);
        SetPrivateField(line, "layerStableKey", stableKey);
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(0f, 0f));
        SetPrivateField(line, "endDoc", new Vector2(10f, 0f));
        SetPrivateField(line, "contourPointA", new Vector2(0f, 0f));
        SetPrivateField(line, "contourPointB", new Vector2(10f, 0f));
        var limbLines = (System.Collections.IList)GetPrivateField<object>(renderer, "limbLines");
        limbLines.Add(line);
        SetPrivateField(renderer, "limbSelectedLineIndex", 0);
        renderer.LimbContourSteps = 0;
        renderer.LimbTowardCenterShift = 0f;

        AssertTrue(
            renderer.ApplySelectedLimb(false, 0f, true, true, out var error),
            "Limb Apply should succeed when it creates a real center vertex. Error=" + error);
        AssertEqual(0, GetPrivateListCount(renderer, "limbLines"), "Successful Limb apply should remove the guide line.");
        AssertEqual(4, GetPrivateField<Vector2[]>(data, "vertices").Length, "Successful Limb apply should append the center vertex.");
        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Successful Limb apply should create a pending geometry patch for top Apply.");
        var weights = GetPrivateField<BoneWeight[]>(data, "boneWeights");
        AssertFloat(0.5f, GetWeight(weights[0], 0), 0.0001f, "Existing guide start should replace its old parent-only weight.");
        AssertFloat(0.5f, GetWeight(weights[0], 1), 0.0001f, "Existing guide start should gain half child influence.");
        AssertFloat(0.5f, GetWeight(weights[1], 0), 0.0001f, "Existing guide end should gain half parent influence.");
        AssertFloat(0.5f, GetWeight(weights[1], 1), 0.0001f, "Existing guide end should replace its old child-only weight.");
        AssertFloat(0.5f, GetWeight(weights[3], 0), 0.0001f, "Created Limb center vertex should keep parent influence.");
        AssertFloat(0.5f, GetWeight(weights[3], 1), 0.0001f, "Created Limb center vertex should keep child influence.");
        AssertTrue(GetPrivateField<bool>(data, "hasSkinning"), "Successful Limb apply should enable live skinning for PreviewPose.");
        AssertEqual(2, GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length, "Successful Limb apply should synthesize missing bind matrices.");
        AssertEqual(null, GetPrivateField<Vector2[]>(data, "deformedVertices"), "Successful Limb apply should clear stale deformed vertices.");
        AssertEqual(-1, GetPrivateField<int>(data, "lastPoseVersion"), "Successful Limb apply should invalidate pose cache.");
    }

    private static void AutoWeightsDefaultLimbJointsApplyExistingPairs()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("l_arm", 920, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "cachedPath", "Assets/auto-limb-pairs.psd");
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "l_arm", -1, new Vector2(0f, 0f)),
            BuildVisibleWorldBone(1, "l_elbow", 0, new Vector2(10f, 0f))
        });

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(8f, -2f),
            new Vector2(16f, -2f),
            new Vector2(16f, 7f),
            new Vector2(8f, 4f)
        });
        SetPrivateField(data, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 2 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", (Matrix4x4[])null);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        var status = renderer.ApplyDefaultAutoLimbJointsAfterAutoWeights();

        AssertTrue(status.Contains("limbs applied 1/4"), "Only the existing left elbow pair should apply. Status=" + status);
        AssertEqual(0, GetPrivateListCount(renderer, "limbLines"), "Auto limb apply should not leave editable guide lines behind.");
        AssertTrue(GetPrivateField<Vector2[]>(data, "vertices").Length > 4, "Auto limb should add geometry around the detected joint.");
        AssertTrue(
            Array.Exists(GetPrivateField<Vector2[]>(data, "vertices"), point => (point - new Vector2(10f, 0f)).sqrMagnitude <= 0.0001f),
            "Auto limb must create an exact geometry anchor at the l_elbow bone base, not at the asymmetric contour midpoint.");
        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Auto limb should create one pending geometry patch.");
        AssertTrue(GetPrivateField<bool>(data, "hasSkinning"), "Auto limb should keep the layer skinned after geometry changes.");

        var autoWeightsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Geometry/PsdLayerPreviewRenderer.Geometry.AutoGeometryWeights.cs"));
        var pipelineStart = autoWeightsSource.IndexOf("public string ApplyRiggedLimbAutoWeights()", StringComparison.Ordinal);
        var pipelineEnd = autoWeightsSource.IndexOf("public string ApplyHeadAutoWeights()", pipelineStart, StringComparison.Ordinal);
        AssertTrue(pipelineStart >= 0 && pipelineEnd > pipelineStart,
            "Rigged Auto Weights pipeline should be locatable.");
        var pipeline = autoWeightsSource.Substring(pipelineStart, pipelineEnd - pipelineStart);
        var rightLegIndex = pipeline.IndexOf("ApplyRightLegAutoWeights()", StringComparison.Ordinal);
        var leftHipV2Index = pipeline.IndexOf(
            "ApplyLegHipJointV2AfterAutoWeights(LeftHipAutoJointSpec)",
            StringComparison.Ordinal);
        var rightHipV2Index = pipeline.IndexOf(
            "ApplyLegHipJointV2AfterAutoWeights(RightHipAutoJointSpec)",
            StringComparison.Ordinal);
        AssertTrue(rightLegIndex >= 0 && leftHipV2Index > rightLegIndex && rightHipV2Index > leftHipV2Index,
            "Rigged Auto Weights should run left and right hip Joint V2 passes after all leg Limbs.");
    }

    private static void ArmAutoLimbsResolveIndexedArmPrefixes()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("l_arm", 923, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 160f, 64f);
        layer.textureRect = layer.sourceRect;
        var hiddenLayer = BuildLayer("l_arm", 924, Vector2.zero);
        hiddenLayer.sourceRect = layer.sourceRect;
        hiddenLayer.textureRect = layer.textureRect;
        hiddenLayer.visible = false;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { hiddenLayer, layer });
        SetPrivateField(renderer, "cachedPath", "Assets/auto-arm-canonical-joints.psd");
        var hiddenSpine = BuildVisibleWorldBone(0, "spine", -1, Vector2.zero);
        hiddenSpine.visible = false;
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            hiddenSpine,
            BuildVisibleWorldBone(1, "l_arm_7", 0, new Vector2(20f, 0f)),
            BuildVisibleWorldBone(2, "l_arm_12", 1, new Vector2(60f, 0f)),
            BuildVisibleWorldBone(3, "l_hand_4", 2, new Vector2(100f, 0f))
        });

        var hiddenSelectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", hiddenLayer);
        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var hiddenData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(hiddenData, "layer", hiddenLayer);
        SetPrivateField(hiddenData, "vertices", new[]
        {
            new Vector2(0f, -12f),
            new Vector2(130f, -12f),
            new Vector2(130f, 18f),
            new Vector2(0f, 8f)
        });
        SetPrivateField(hiddenData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(hiddenData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 2 });
        SetPrivateField(hiddenData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        SetPrivateField(hiddenData, "boneWeights", new BoneWeight[4]);
        SetPrivateField(hiddenData, "spriteBoneToRendererBone", new[] { 1, 2, 3 });
        SetPrivateField(hiddenData, "bindBoneInverseMatrices", (Matrix4x4[])null);

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(0f, -12f),
            new Vector2(130f, -12f),
            new Vector2(130f, 18f),
            new Vector2(0f, 8f)
        });
        SetPrivateField(data, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 2 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        SetPrivateField(data, "boneWeights", new BoneWeight[4]);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[hiddenSelectionKey] = hiddenData;
        dataBySelectionKey[selectionKey] = data;

        var status = renderer.ApplyLeftArmAutoWeights();
        var vertices = GetPrivateField<Vector2[]>(data, "vertices");

        AssertTrue(status.Contains("left elbow limb applied"), "Arbitrary indexed l_arm_* -> l_arm_* names must drive the elbow Limbs pass. Status=" + status);
        AssertTrue(status.Contains("left wrist limb applied"), "Arbitrary indexed l_arm_* -> l_hand_* names must drive the wrist Limbs pass. Status=" + status);
        AssertTrue(
            Array.Exists(vertices, point => (point - new Vector2(60f, 0f)).sqrMagnitude <= 0.0001f),
            "Elbow Limbs geometry must contain the exact l_elbow base.");
        AssertTrue(
            Array.Exists(vertices, point => (point - new Vector2(100f, 0f)).sqrMagnitude <= 0.0001f),
            "Wrist Limbs geometry must contain the exact l_hand base.");
        var mappedBones = GetPrivateField<int[]>(data, "spriteBoneToRendererBone");
        AssertTrue(Array.IndexOf(mappedBones, 1) >= 0 && Array.IndexOf(mappedBones, 2) >= 0 && Array.IndexOf(mappedBones, 3) >= 0,
            "Auto arm map must contain the shared arm bone chain.");
        AssertEqual(4, GetPrivateField<Vector2[]>(hiddenData, "vertices").Length,
            "Hidden arm layers must not receive Limbs geometry.");

        var repeated = renderer.ApplyLeftArmAutoWeights();
        AssertTrue(repeated.Contains("left elbow limb applied") && repeated.Contains("left wrist limb applied"),
            "Auto Weights must reapply both joints when their guide vertices already exist. Status=" + repeated);
    }

    private static void ArmAutoWeightsIsolateBothArmPairs()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "spine", -1, Vector2.zero),
            BuildVisibleWorldBone(1, "l_arm", 0, new Vector2(-10f, 0f)),
            BuildVisibleWorldBone(2, "l_arm_1", 1, new Vector2(-20f, 0f)),
            BuildVisibleWorldBone(3, "l_hand", 2, new Vector2(-30f, 0f)),
            BuildVisibleWorldBone(4, "l_arm_2", 0, new Vector2(-10f, 10f)),
            BuildVisibleWorldBone(5, "l_arm_3", 4, new Vector2(-20f, 20f)),
            BuildVisibleWorldBone(6, "l_hand_2", 5, new Vector2(-30f, 30f)),
            BuildVisibleWorldBone(7, "r_arm", 0, new Vector2(10f, 0f)),
            BuildVisibleWorldBone(8, "r_arm_1", 7, new Vector2(20f, 0f)),
            BuildVisibleWorldBone(9, "r_hand", 8, new Vector2(30f, 0f)),
            BuildVisibleWorldBone(10, "r_arm_2", 0, new Vector2(10f, 10f)),
            BuildVisibleWorldBone(11, "r_arm_3", 10, new Vector2(20f, 20f)),
            BuildVisibleWorldBone(12, "r_hand_2", 11, new Vector2(30f, 30f))
        });

        var leftPrimary = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(leftPrimary, "layer", BuildLayer("l_arm", 951, Vector2.zero));
        var leftSecondary = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(leftSecondary, "layer", BuildLayer("l_arm_2", 952, Vector2.zero));
        var rightPrimary = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(rightPrimary, "layer", BuildLayer("r_arm", 953, Vector2.zero));
        var rightSecondary = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(rightSecondary, "layer", BuildLayer("r_arm_2", 954, Vector2.zero));

        var leftPrimaryMap = (int[])InvokePrivateMethod(renderer, "BuildLeftArmAutoWeightBoneMap", leftPrimary);
        var leftSecondaryMap = (int[])InvokePrivateMethod(renderer, "BuildLeftArmAutoWeightBoneMap", leftSecondary);
        var rightPrimaryMap = (int[])InvokePrivateMethod(renderer, "BuildRightArmAutoWeightBoneMap", rightPrimary);
        var rightSecondaryMap = (int[])InvokePrivateMethod(renderer, "BuildRightArmAutoWeightBoneMap", rightSecondary);

        AssertTrue(leftPrimaryMap.SequenceEqual(new[] { 0, 1, 2, 3 }), "Primary left arm must map only spine and its own chain.");
        AssertTrue(leftSecondaryMap.SequenceEqual(new[] { 0, 4, 5, 6 }), "Second left arm must map only spine and its own chain.");
        AssertTrue(rightPrimaryMap.SequenceEqual(new[] { 0, 7, 8, 9 }), "Primary right arm must map only spine and its own chain.");
        AssertTrue(rightSecondaryMap.SequenceEqual(new[] { 0, 10, 11, 12 }), "Second right arm must map only spine and its own chain.");
    }

    private static void AutoLimbGuideFollowsTwoBoneBisectorNormal()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var straightNormal = (Vector2)InvokePrivateMethod(
            renderer,
            "ResolveAutoLimbBisectorNormal",
            Vector2.right,
            Vector2.right);
        AssertFloat(0f, Vector2.Dot(straightNormal, Vector2.right), 0.0001f,
            "A straight limb guide must be perpendicular to both bones.");

        var parent = Vector2.right;
        var child = Vector2.up;
        var bentNormal = (Vector2)InvokePrivateMethod(renderer, "ResolveAutoLimbBisectorNormal", parent, child);
        var bisector = (parent.normalized + child.normalized).normalized;
        AssertFloat(0f, Vector2.Dot(bentNormal, bisector), 0.0001f,
            "A bent limb guide must be perpendicular to the two-bone bisector.");
        AssertFloat(
            Mathf.Abs(Vector2.Dot(bentNormal, parent.normalized)),
            Mathf.Abs(Vector2.Dot(bentNormal, child.normalized)),
            0.0001f,
            "A bent limb guide must have equal angular relation to both bones.");
    }

    private static void LimbApplyWithCircleCleanCreatesPendingGeometryPatch()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("limb-circleclean-patch", 918, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "cachedPath", "Assets/limb-circleclean-patch.psd");
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "upper", -1, new Vector2(-8f, 5f)),
            BuildVisibleWorldBone(1, "lower", 0, new Vector2(2f, 5f))
        });

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var stableKey = (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(2f, 0f),
            new Vector2(4f, 0f),
            new Vector2(4f, 5f),
            new Vector2(4f, 10f),
            new Vector2(2f, 10f),
            new Vector2(0f, 10f),
            new Vector2(0f, 5f),
            new Vector2(2f, 5f)
        });
        SetPrivateField(data, "triangles", new[]
        {
            0, 1, 8, 1, 2, 8, 2, 3, 8, 3, 4, 8,
            4, 5, 8, 5, 6, 8, 6, 7, 8, 7, 0, 8
        });
        SetPrivateField(data, "allEdgePairs", new[]
        {
            0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 0,
            0, 8, 1, 8, 2, 8, 3, 8, 4, 8, 5, 8, 6, 8, 7, 8
        });
        SetPrivateField(data, "boundaryEdgePairs", new[]
        {
            0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 0
        });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", (Matrix4x4[])null);
        SetPrivateField(data, "hasSkinning", false);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "layerSelectionKey", selectionKey);
        SetPrivateField(line, "layerStableKey", stableKey);
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(2f, 0f));
        SetPrivateField(line, "endDoc", new Vector2(2f, 10f));
        SetPrivateField(line, "contourPointA", new Vector2(2f, 0f));
        SetPrivateField(line, "contourPointB", new Vector2(2f, 10f));
        var limbLines = (System.Collections.IList)GetPrivateField<object>(renderer, "limbLines");
        limbLines.Add(line);
        SetPrivateField(renderer, "limbSelectedLineIndex", 0);
        renderer.LimbContourSteps = 1;
        renderer.LimbContourStepDistance = 5f;
        renderer.LimbTowardCenterShift = 0f;

        AssertTrue(
            renderer.ApplySelectedLimb(true, 3f, false, false, out var error),
            "CircleClean-before-Limb should replace contour even when the standalone CW/IW settings are disabled. Error=" + error);
        AssertEqual(0, GetPrivateListCount(renderer, "limbLines"), "Successful CircleClean Limb apply should remove the guide line.");
        var resultVertices = GetPrivateField<Vector2[]>(data, "vertices");
        AssertEqual(11, resultVertices.Length, "CircleClean Limb should replace two old contour points with four generated contour points.");
        AssertFalse(Array.Exists(resultVertices, point => (point - new Vector2(4f, 5f)).sqrMagnitude <= 0.0001f), "Old right contour point inside CircleClean must be removed.");
        AssertFalse(Array.Exists(resultVertices, point => (point - new Vector2(0f, 5f)).sqrMagnitude <= 0.0001f), "Old left contour point inside CircleClean must be removed.");
        var resultBoundaryMask = ReanimatorRadiusCullAlgorithms.BuildContourMask(GetPrivateField<int[]>(data, "boundaryEdgePairs"), resultVertices.Length);
        for (var i = 7; i < resultVertices.Length; i++)
            AssertTrue(resultBoundaryMask[i], "Every generated Limb contour point should belong to the rebuilt boundary. Index=" + i);
        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "CircleClean Limb apply should create a pending geometry patch for top Apply.");
        AssertTrue(GetPrivateField<bool>(data, "hasSkinning"), "CircleClean Limb apply should enable live skinning for PreviewPose.");
        AssertEqual(2, GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length, "CircleClean Limb apply should synthesize missing bind matrices.");
    }

    private static void LimbApplyFallsBackWhenCircleCleanCannotClean()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("limb-circleclean-fallback", 919, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "cachedPath", "Assets/limb-circleclean-fallback.psd");
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "upper", -1, new Vector2(0f, 0f)),
            BuildVisibleWorldBone(1, "lower", 0, new Vector2(10f, 0f))
        });

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var stableKey = (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(5f, 8f)
        });
        SetPrivateField(data, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", (Matrix4x4[])null);
        SetPrivateField(data, "hasSkinning", false);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        var line = CreatePrivateNestedInstance(renderer, "LimbLine");
        SetPrivateField(line, "layerSelectionKey", selectionKey);
        SetPrivateField(line, "layerStableKey", stableKey);
        SetPrivateField(line, "hasContourPoints", true);
        SetPrivateField(line, "startDoc", new Vector2(0f, 0f));
        SetPrivateField(line, "endDoc", new Vector2(10f, 0f));
        SetPrivateField(line, "contourPointA", new Vector2(0f, 0f));
        SetPrivateField(line, "contourPointB", new Vector2(10f, 0f));
        var limbLines = (System.Collections.IList)GetPrivateField<object>(renderer, "limbLines");
        limbLines.Add(line);
        SetPrivateField(renderer, "limbSelectedLineIndex", 0);
        renderer.LimbContourSteps = 0;
        renderer.LimbTowardCenterShift = 0f;

        AssertTrue(
            renderer.ApplySelectedLimb(true, 0.01f, true, false, out var error),
            "Limb Apply should fall back when CircleClean has no vertices in radius. Error=" + error);
        AssertEqual("Limb applied; CircleClean skipped.", error, "Fallback should report that CircleClean was skipped without failing Apply.");
        AssertEqual(0, GetPrivateListCount(renderer, "limbLines"), "Fallback Limb apply should remove the guide line.");
        AssertEqual(4, GetPrivateField<Vector2[]>(data, "vertices").Length, "Fallback Limb apply should append the center vertex.");
        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Fallback Limb apply should create a pending geometry patch for top Apply.");
        AssertTrue(GetPrivateField<bool>(data, "hasSkinning"), "Fallback Limb apply should enable live skinning for PreviewPose.");
        AssertEqual(2, GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length, "Fallback Limb apply should synthesize missing bind matrices.");
    }

    private static PsdLayerPreviewRenderer.BoneInfo BuildVisibleWorldBone(int index, string name, int parentId, Vector2 worldPosition)
    {
        return new PsdLayerPreviewRenderer.BoneInfo
        {
            index = index,
            name = name,
            parentId = parentId,
            position = worldPosition,
            initialPosition = worldPosition,
            worldPosition = worldPosition,
            rotation = Quaternion.identity,
            initialRotation = Quaternion.identity,
            worldRotation = Quaternion.identity,
            visible = true,
            length = 10f
        };
    }

    private static bool TryResolveLimbTwoBoneSpriteIndicesForTest(
        PsdLayerPreviewRenderer renderer,
        object data,
        object line,
        out int parentSpriteBone,
        out int childSpriteBone)
    {
        var method = typeof(PsdLayerPreviewRenderer).GetMethod(
            "TryResolveLimbTwoBoneSpriteIndices",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[]
            {
                data.GetType(),
                line.GetType(),
                typeof(int).MakeByRefType(),
                typeof(int).MakeByRefType(),
                typeof(Vector2).MakeByRefType(),
                typeof(Vector2).MakeByRefType()
            },
            null);
        if (method == null)
            throw new InvalidOperationException("TryResolveLimbTwoBoneSpriteIndices not found.");

        var args = new object[]
        {
            data,
            line,
            -1,
            -1,
            Vector2.zero,
            Vector2.zero
        };

        var resolved = (bool)method.Invoke(renderer, args);
        parentSpriteBone = (int)args[2];
        childSpriteBone = (int)args[3];
        return resolved;
    }

    private static bool TryGetLimbNearestBoneGuideSegmentForTest(
        PsdLayerPreviewRenderer renderer,
        object line,
        out Vector2 segStart,
        out Vector2 segEnd,
        out int nearestBoneIndex)
    {
        var method = typeof(PsdLayerPreviewRenderer).GetMethod(
            "TryGetLimbNearestBoneGuideSegment",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[]
            {
                line.GetType(),
                typeof(Vector2).MakeByRefType(),
                typeof(Vector2).MakeByRefType(),
                typeof(int).MakeByRefType()
            },
            null);
        if (method == null)
            throw new InvalidOperationException("TryGetLimbNearestBoneGuideSegment not found.");

        var args = new object[] { line, Vector2.zero, Vector2.zero, -1 };
        var resolved = (bool)method.Invoke(renderer, args);
        segStart = (Vector2)args[1];
        segEnd = (Vector2)args[2];
        nearestBoneIndex = (int)args[3];
        return resolved;
    }

    private static bool TryGetLimbNearestChildGuideSegmentForTest(
        PsdLayerPreviewRenderer renderer,
        object line,
        int boneIndex,
        out Vector2 segStart,
        out Vector2 segEnd)
    {
        var method = typeof(PsdLayerPreviewRenderer).GetMethod(
            "TryGetLimbNearestChildGuideSegment",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException("TryGetLimbNearestChildGuideSegment not found.");

        var args = new object[] { line, boneIndex, Vector2.zero, Vector2.zero };
        var resolved = (bool)method.Invoke(renderer, args);
        segStart = (Vector2)args[2];
        segEnd = (Vector2)args[3];
        return resolved;
    }

}
#endif
