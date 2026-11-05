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
    private static void RendererCancelClearsWeightToolState()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        SetPrivateField(renderer, "weightSliderIsRectSelecting", true);
        SetPrivateField(renderer, "weightSliderRectToggleSelection", true);
        SetPrivateField(renderer, "weightSliderControlId", 17);
        SetPrivateField(renderer, "weightSliderIsWeightDragging", true);
        SetPrivateField(renderer, "weightSliderWeightDragLayerStableKey", "layer-a");
        SetPrivateField(renderer, "weightSliderWeightDragVertexIndex", 3);
        SetPrivateField(renderer, "weightSliderWeightDragBones", new[] { 1, 2 });
        SetNonEmptyPrivateArray(renderer, "weightSliderWeightDragStates");
        SetPrivateField(renderer, "weightSliderWeightDragMoved", true);
        SetPrivateField(renderer, "weightSliderWeightDragUndoRegistered", true);
        SetPrivateField(renderer, "weightSliderSelectedLayerStableKey", "layer-a");
        GetPrivateField<List<int>>(renderer, "weightSliderSelectedVertexIndices").AddRange(new[] { 4, 5 });

        AssertTrue(renderer.CancelWeightSliderTool(), "CancelWeightSliderTool should report changed state.");
        AssertArray(new[] { "Cancel Weight Slider Tool" }, undoLabels, "Weight slider cancel undo labels.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderIsRectSelecting"), "Weight slider rect select should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderRectToggleSelection"), "Weight slider rect toggle should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "weightSliderControlId"), "Weight slider control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderIsWeightDragging"), "Weight slider weight drag should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "weightSliderWeightDragLayerStableKey"), "Weight slider drag layer should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "weightSliderWeightDragVertexIndex"), "Weight slider drag vertex should reset.");
        AssertEqual(0, GetPrivateField<int[]>(renderer, "weightSliderWeightDragBones").Length, "Weight slider drag bones should reset.");
        AssertEqual(0, GetPrivateField<Array>(renderer, "weightSliderWeightDragStates").Length, "Weight slider per-vertex drag states should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderWeightDragMoved"), "Weight slider moved flag should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderWeightDragUndoRegistered"), "Weight slider undo flag should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "weightSliderSelectedLayerStableKey"), "Weight slider selection layer should reset.");
        AssertEqual(0, GetPrivateField<List<int>>(renderer, "weightSliderSelectedVertexIndices").Count, "Weight slider selection should clear.");
        AssertFalse(renderer.CancelWeightSliderTool(), "CancelWeightSliderTool should be idempotent.");
        AssertArray(new[] { "Cancel Weight Slider Tool" }, undoLabels, "Weight slider idempotent cancel should not add undo.");

        SetPrivateField(renderer, "weightBrushLayerStableKey", "brush-layer");
        AssertTrue(renderer.CancelWeightBrushTool(), "CancelWeightBrushTool should report changed state.");
        AssertArray(new[] { "Cancel Weight Slider Tool", "Cancel Weight Brush Tool" }, undoLabels, "Weight brush cancel undo labels.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "weightBrushLayerStableKey"), "Weight brush layer should reset.");
        AssertFalse(renderer.CancelWeightBrushTool(), "CancelWeightBrushTool should be idempotent.");
        AssertArray(new[] { "Cancel Weight Slider Tool", "Cancel Weight Brush Tool" }, undoLabels, "Weight brush idempotent cancel should not add undo.");
    }

    private static void RendererCancelClearsGeometryRadiusAndJointState()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        SetPrivateField(renderer, "createEdgeStartVertexIndex", 12);
        SetPrivateField(renderer, "createEdgeLayerStableKey", "edge-layer");
        AssertTrue(renderer.CancelCreateEdgeTool(), "CancelCreateEdgeTool should report changed state.");
        AssertArray(new[] { "Cancel Create Edge Tool" }, undoLabels, "Create edge cancel undo labels.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "createEdgeStartVertexIndex"), "Create edge start should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "createEdgeLayerStableKey"), "Create edge layer should reset.");
        AssertFalse(renderer.CancelCreateEdgeTool(), "CancelCreateEdgeTool should be idempotent.");
        AssertArray(new[] { "Cancel Create Edge Tool" }, undoLabels, "Create edge idempotent cancel should not add undo.");

        SetPrivateField(renderer, "editGeometryIsDragging", true);
        SetPrivateField(renderer, "editGeometryIsRectSelecting", true);
        SetPrivateField(renderer, "editGeometryRectToggleSelection", true);
        SetPrivateField(renderer, "editGeometryMoveUndoRegistered", true);
        SetPrivateField(renderer, "editGeometryControlId", 22);
        SetPrivateField(renderer, "editGeometrySelectedVertexIndex", 5);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", "selection-layer");
        GetPrivateField<List<int>>(renderer, "editGeometrySelectedVertexIndices").Add(5);
        AssertTrue(renderer.CancelEditGeometryTool(), "CancelEditGeometryTool should report changed interaction state.");
        AssertFalse(GetPrivateField<bool>(renderer, "editGeometryIsDragging"), "Edit geometry drag should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "editGeometryIsRectSelecting"), "Edit geometry rect select should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "editGeometryRectToggleSelection"), "Edit geometry rect toggle should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "editGeometryMoveUndoRegistered"), "Edit geometry undo flag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "editGeometryControlId"), "Edit geometry control id should reset.");
        AssertEqual(5, GetPrivateField<int>(renderer, "editGeometrySelectedVertexIndex"), "Edit geometry selection should be preserved.");
        AssertEqual("selection-layer", GetPrivateField<string>(renderer, "editGeometrySelectedLayerStableKey"), "Edit geometry selection layer should be preserved.");
        AssertFalse(renderer.CancelEditGeometryTool(), "CancelEditGeometryTool should be idempotent after interaction reset.");

        SetPrivateField(renderer, "radiusCullHasPoint", true);
        SetPrivateField(renderer, "radiusCullLayerStableKey", "radius-layer");
        SetPrivateField(renderer, "radiusCullIsDragging", true);
        SetPrivateField(renderer, "radiusCullHandleIsDragging", true);
        SetPrivateField(renderer, "radiusCullControlId", 33);
        SetPrivateField(renderer, "radiusCullHasRadiusOverride", true);
        SetPrivateField(renderer, "radiusCullRadiusOverride", 42f);
        AssertTrue(renderer.CancelRadiusCullTool(), "CancelRadiusCullTool should report changed state.");
        AssertArray(new[] { "Cancel Create Edge Tool", "Cancel Radius Cull Tool" }, undoLabels, "Radius cull cancel undo labels.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHasPoint"), "Radius cull point should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "radiusCullLayerStableKey"), "Radius cull layer should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullIsDragging"), "Radius cull drag should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHandleIsDragging"), "Radius cull handle drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "radiusCullControlId"), "Radius cull control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHasRadiusOverride"), "Radius override flag should reset.");
        AssertFloat(0f, GetPrivateField<float>(renderer, "radiusCullRadiusOverride"), 0.0001f, "Radius override value should reset.");

        SetPrivateField(renderer, "jointIsDragging", true);
        SetPrivateField(renderer, "jointStartHandleIsDragging", true);
        SetPrivateField(renderer, "jointEndHandleIsDragging", true);
        SetPrivateField(renderer, "jointMiddleHandleIsDragging", true);
        SetPrivateField(renderer, "jointStartOffsetHandleIsDragging", true);
        SetPrivateField(renderer, "jointEndOffsetHandleIsDragging", true);
        SetPrivateField(renderer, "jointControlId", 44);
        SetPrivateField(renderer, "jointCenterLockEnabled", true);
        SetPrivateField(renderer, "jointNearestBoneIndex", 3);
        SetPrivateField(renderer, "radiusCullHasPoint", true);
        SetPrivateField(renderer, "radiusCullLayerStableKey", "joint-radius-layer");
        SetPrivateField(renderer, "radiusCullIsDragging", true);
        SetPrivateField(renderer, "radiusCullHandleIsDragging", true);
        SetPrivateField(renderer, "radiusCullControlId", 45);
        SetPrivateField(renderer, "radiusCullHasRadiusOverride", true);
        SetPrivateField(renderer, "radiusCullRadiusOverride", 24f);
        AssertTrue(renderer.CancelJointTool(), "CancelJointTool should report changed state.");
        AssertArray(new[] { "Cancel Create Edge Tool", "Cancel Radius Cull Tool", "Cancel Joint Tool" }, undoLabels, "Joint cancel undo labels.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointIsDragging"), "Joint drag should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointStartHandleIsDragging"), "Joint start handle should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointEndHandleIsDragging"), "Joint end handle should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointMiddleHandleIsDragging"), "Joint middle handle should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointStartOffsetHandleIsDragging"), "Joint start offset handle should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointEndOffsetHandleIsDragging"), "Joint end offset handle should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "jointControlId"), "Joint control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointCenterLockEnabled"), "Joint center lock should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "jointNearestBoneIndex"), "Joint nearest bone should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHasPoint"), "Joint cancel should reset radius point.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "radiusCullLayerStableKey"), "Joint cancel should reset radius layer.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullIsDragging"), "Joint cancel should reset radius drag.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHandleIsDragging"), "Joint cancel should reset radius handle drag.");
        AssertEqual(0, GetPrivateField<int>(renderer, "radiusCullControlId"), "Joint cancel should reset radius control id.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHasRadiusOverride"), "Joint cancel should reset radius override flag.");
        AssertFloat(0f, GetPrivateField<float>(renderer, "radiusCullRadiusOverride"), 0.0001f, "Joint cancel should reset radius override value.");
        AssertFalse(renderer.CancelJointTool(), "CancelJointTool should be idempotent.");
        AssertArray(new[] { "Cancel Create Edge Tool", "Cancel Radius Cull Tool", "Cancel Joint Tool" }, undoLabels, "Joint idempotent cancel should not add undo.");
    }

    private static void RendererCancelClearsCurveBoobAndVolumeState()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        SetPrivateField(renderer, "curveLayerStableKey", "curve-layer");
        GetPrivateField<List<int>>(renderer, "curveSelectedLoopPositions").AddRange(new[] { 1, 2 });
        SetPrivateField(renderer, "curveGraphSelectionSignature", "graph");
        GetPrivateField<List<float>>(renderer, "curveEditedChildWeights").AddRange(new[] { 0.1f, 0.2f });
        SetPrivateField(renderer, "curveSecondBoneSelectionSignature", "second");
        SetPrivateField(renderer, "curveSelectedFirstSpriteBone", 1);
        SetPrivateField(renderer, "curveSelectedSecondSpriteBone", 2);
        SetPrivateField(renderer, "curveIsDragging", true);
        SetPrivateField(renderer, "curveControlId", 55);
        SetPrivateField(renderer, "curveDragLayerSelectionKey", "drag-layer");

        renderer.CancelCurveTool();
        AssertArray(new[] { "Cancel WeightCurve Tool" }, undoLabels, "Curve cancel undo labels.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "curveLayerStableKey"), "Curve layer should reset.");
        AssertEqual(0, GetPrivateField<List<int>>(renderer, "curveSelectedLoopPositions").Count, "Curve loop selection should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "curveGraphSelectionSignature"), "Curve graph signature should reset.");
        AssertEqual(0, GetPrivateField<List<float>>(renderer, "curveEditedChildWeights").Count, "Curve edited weights should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "curveSecondBoneSelectionSignature"), "Curve second bone signature should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "curveSelectedFirstSpriteBone"), "Curve first sprite bone should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "curveSelectedSecondSpriteBone"), "Curve second sprite bone should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "curveIsDragging"), "Curve drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "curveControlId"), "Curve control id should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "curveDragLayerSelectionKey"), "Curve drag layer should reset.");

        SetPrivateField(renderer, "boobHasPoint", true);
        SetPrivateField(renderer, "boobLayerStableKey", "boob-layer");
        SetPrivateField(renderer, "boobIsDragging", true);
        SetPrivateField(renderer, "boobControlId", 66);
        AssertTrue(renderer.CancelBoobTool(), "CancelBoobTool should report changed state.");
        AssertArray(new[] { "Cancel WeightCurve Tool", "Cancel Boob Tool" }, undoLabels, "Boob cancel undo labels.");
        AssertFalse(GetPrivateField<bool>(renderer, "boobHasPoint"), "Boob point should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "boobLayerStableKey"), "Boob layer should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "boobIsDragging"), "Boob drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "boobControlId"), "Boob control id should reset.");
        AssertFalse(renderer.CancelBoobTool(), "CancelBoobTool should be idempotent.");
        AssertArray(new[] { "Cancel WeightCurve Tool", "Cancel Boob Tool" }, undoLabels, "Boob idempotent cancel should not add undo.");

        SetPrivateField(renderer, "volumeCurveIsDragging", true);
        SetPrivateField(renderer, "volumeCurveControlId", 77);
        SetPrivateField(renderer, "volumeCurvePointMoveUndoRegistered", true);
        renderer.CancelVolumeCurveTool();
        AssertArray(new[] { "Cancel WeightCurve Tool", "Cancel Boob Tool" }, undoLabels, "Volume drag cancel should not add persistent undo.");
        AssertFalse(GetPrivateField<bool>(renderer, "volumeCurveIsDragging"), "Volume curve drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "volumeCurveControlId"), "Volume curve control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "volumeCurvePointMoveUndoRegistered"), "Volume curve drag undo grouping should reset.");
    }

    private static void RendererBoobAppendsMissingBreastBones()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "random_parent",
                parentId = -1,
                worldPosition = Vector2.zero,
                worldRotation = Quaternion.identity,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "random_child",
                parentId = 0,
                worldPosition = new Vector2(-3f, 0f),
                worldRotation = Quaternion.identity,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = "boobs_root",
                parentId = -1,
                worldPosition = new Vector2(-3f, 0f),
                worldRotation = Quaternion.identity,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 3,
                name = "nipple_l",
                parentId = 2,
                worldPosition = new Vector2(2f, 0f),
                worldRotation = Quaternion.identity,
                visible = true
            }
        });

        var layer = new PsdLayerPreviewRenderer.LayerInfo
        {
            name = "l_boob",
            layerId = 42,
            spriteGuid = "boob-sprite-guid",
            position = new Vector2(-10f, -10f),
            sourceRect = new Rect(0f, 0f, 20f, 20f),
            textureSamplingRect = new Rect(0f, 0f, 20f, 20f)
        };
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var stableKey = (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(-10f, -10f),
            new Vector2(10f, -10f),
            new Vector2(10f, 10f),
            new Vector2(-10f, 10f)
        });
        SetPrivateField(data, "vertexUvs", new Vector2[4]);
        SetPrivateField(data, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 2 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        SetPrivateField(data, "boneWeights", new BoneWeight[4]);
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;
        SetPrivateField(renderer, "selectedLayerSelectionKey", selectionKey);
        SetPrivateField(renderer, "boobLayerStableKey", stableKey);
        SetPrivateField(renderer, "boobCenterDoc", new Vector2(1f, 0f));
        SetPrivateField(renderer, "boobHasPoint", true);

        AssertTrue(renderer.ApplyBoob(out var error), "Boob apply should add its breast bones to an unskinned sprite. " + error);

        var rebuiltVertices = GetPrivateField<Vector2[]>(data, "vertices");
        var rebuiltTriangles = GetPrivateField<int[]>(data, "triangles");
        var rebuiltWeights = GetPrivateField<BoneWeight[]>(data, "boneWeights");
        AssertTrue(rebuiltVertices.Length > 4, "Boob apply should add interior rings and a center vertex.");
        AssertTrue(rebuiltTriangles.Length > 6, "Boob apply should rebuild the interior triangulation.");
        AssertEqual(rebuiltVertices.Length, rebuiltWeights.Length, "Boob apply should assign one weight per rebuilt vertex.");
        AssertArray(new[] { 0, 1, 2, 3 }, GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), "Boob apply must ignore an existing random mapped pair and append the correctly named boobs_root/nipple_l bones.");
        AssertTrue(GetPrivateField<bool>(data, "hasSkinning"), "Boob apply should leave the rebuilt geometry skinned.");
        AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Boob apply should mark the renderer dirty.");

        var centerWeight = rebuiltWeights[rebuiltWeights.Length - 1];
        var childWeight = 0f;
        if (centerWeight.boneIndex0 == 3) childWeight += centerWeight.weight0;
        if (centerWeight.boneIndex1 == 3) childWeight += centerWeight.weight1;
        if (centerWeight.boneIndex2 == 3) childWeight += centerWeight.weight2;
        if (centerWeight.boneIndex3 == 3) childWeight += centerWeight.weight3;
        AssertFloat(1f, childWeight, 0.0001f, "Boob center should be fully assigned to the mapped nipple bone.");
    }

    private static void BoobV2ApplySnapsExistingNippleBonesToTargets()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "boobs_root",
                parentId = -1,
                position = Vector2.zero,
                initialPosition = Vector2.zero,
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                length = 5f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "nipple_l",
                parentId = 0,
                position = new Vector2(10f, 0f),
                initialPosition = new Vector2(10f, 0f),
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                length = 3f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = "nipple_r",
                parentId = 0,
                position = new Vector2(-10f, 0f),
                initialPosition = new Vector2(-10f, 0f),
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                length = 3f,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        var moved = (int)InvokePrivateMethod(
            renderer,
            "SnapBoobV2NippleBonesToTargets",
            new Vector2(-22f, 4f),
            new Vector2(31f, 6f));
        AssertEqual(2, moved, "Boob V2 should snap both existing nipple bones.");

        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(-22f, 4f), bones[2].worldPosition, 0.0001f,
            "The spatially viewer-left nipple bone should snap to the viewer-left target regardless of its l/r name.");
        AssertVector(new Vector2(31f, 6f), bones[1].worldPosition, 0.0001f,
            "The spatially viewer-right nipple bone should snap to the viewer-right target regardless of its l/r name.");
        AssertVector(new Vector2(-22f, 4f), bones[2].initialPosition, 0.0001f,
            "Snapped nipple positions should become the new rest pose.");
        AssertVector(new Vector2(31f, 6f), bones[1].initialPosition, 0.0001f,
            "Both snapped nipple positions should become the new rest pose.");
    }

    private static void RendererBoneEditSettersUpdateTransformsWithUndo()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = Vector2.zero,
                rotation = Quaternion.identity,
                length = 10f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "child",
                parentId = 0,
                position = new Vector2(10f, 0f),
                rotation = Quaternion.identity,
                length = 5f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = "sibling",
                parentId = -1,
                position = new Vector2(20f, 20f),
                rotation = Quaternion.AngleAxis(90f, Vector3.forward),
                length = 8f,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        AssertTrue(renderer.SetBoneWorldPosition(1, new Vector2(12f, 3f)), "World position setter should change child.");
        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(12f, 3f), bones[1].worldPosition, 0.0001f, "World position setter should preserve requested world position.");
        AssertVector(new Vector2(12f, 3f), bones[1].position, 0.0001f, "World position setter should update local position under identity parent.");

        AssertTrue(renderer.SetBoneWorldRotationAngle(1, 45f), "World rotation setter should change child.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertFloat(45f, bones[1].worldRotation.eulerAngles.z, 0.001f, "World rotation setter should update world angle.");

        AssertTrue(renderer.SetBoneLength(1, 9f), "Length setter should change child.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertFloat(9f, bones[1].length, 0.0001f, "Length setter should update length.");

        AssertTrue(renderer.MoveBoneToParentTip(1), "Move-to-parent-tip should change child.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(10f, 0f), bones[1].worldPosition, 0.0001f, "Move-to-parent-tip should align child world position to parent tip.");

        AssertTrue(renderer.RotateBoneToParentRotation(1), "Rotate-to-parent-rotation should change child.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertFloat(bones[0].worldRotation.eulerAngles.z, bones[1].worldRotation.eulerAngles.z, 0.001f, "Rotate-to-parent-rotation should align child world rotation to parent.");

        AssertFalse(renderer.MoveBoneToParentTip(0), "Move-to-parent-tip should ignore root bones.");
        AssertFalse(renderer.RotateBoneToParentRotation(0), "Rotate-to-parent-rotation should ignore root bones.");

        AssertTrue(renderer.SetPanelHoveredBoneIndex(0), "Panel hover setter should accept valid bone.");
        AssertEqual(0, GetPrivateField<int>(renderer, "panelHoveredBoneIndex"), "Panel hover setter should store valid bone.");
        AssertTrue(renderer.SetPanelHoveredBoneIndex(99), "Panel hover setter should normalize invalid bone to none.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "panelHoveredBoneIndex"), "Panel hover setter should clear invalid bone.");

        var beforeReparentWorldPosition = bones[1].worldPosition;
        var beforeReparentWorldRotation = bones[1].worldRotation;
        AssertTrue(renderer.SetBoneParent(1, 2), "Parent setter should allow valid parent.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertEqual(2, bones[1].parentId, "Parent setter should update parent id.");
        AssertVector(beforeReparentWorldPosition, bones[1].worldPosition, 0.0001f, "Parent setter should preserve world position.");
        AssertFloat(beforeReparentWorldRotation.eulerAngles.z, bones[1].worldRotation.eulerAngles.z, 0.001f, "Parent setter should preserve world rotation.");

        AssertFalse(renderer.SetBoneParent(2, 1), "Parent setter should reject descendant parent cycles.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertEqual(-1, bones[2].parentId, "Rejected parent cycle should not mutate parent id.");
        AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Bone edit setters should mark pending changes.");
        AssertArray(
            new[]
            {
                "Set Bone World Position",
                "Set Bone World Rotation",
                "Set Bone Length",
                "Move Bone To Parent Tip",
                "Rotate Bone To Parent Rotation",
                "Set Bone Parent"
            },
            undoLabels,
            "Bone edit setters undo labels.");
    }

    private static void RendererCreatesEyelidsBoneFromHeadDefaults()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = " Head ",
                parentId = -1,
                position = new Vector2(20f, 10f),
                rotation = Quaternion.AngleAxis(90f, Vector3.forward),
                initialPosition = new Vector2(20f, 10f),
                initialRotation = Quaternion.AngleAxis(90f, Vector3.forward),
                length = 30f,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        AssertTrue(renderer.TryFindBoneIndexByName("head", out var headIndex), "Head lookup should trim and ignore case.");
        AssertEqual(0, headIndex, "Head lookup index.");
        AssertFalse(renderer.TryFindBoneIndexByName("eyelids", out _), "Eyelids should initially be missing.");

        var head = renderer.Bones[headIndex];
        var eyelidsLength = head.length / 3f;
        var leftOffset = Vector2.left * (head.length * 0.1f);
        AssertTrue(
            renderer.TryCreateNamedChildBone("eyelids", headIndex, eyelidsLength, leftOffset, out var eyelidsIndex),
            "Eyelids creation should succeed from a valid head bone.");

        var bones = renderer.Bones;
        AssertEqual(1, eyelidsIndex, "Eyelids created index.");
        AssertEqual(2, bones.Count, "Eyelids creation bone count.");
        AssertEqual("eyelids", bones[eyelidsIndex].name, "Eyelids name.");
        AssertEqual(headIndex, bones[eyelidsIndex].parentId, "Eyelids parent should be head.");
        AssertFloat(10f, bones[eyelidsIndex].length, 0.0001f, "Eyelids length should be one third of head.");
        AssertVector(new Vector2(17f, 10f), bones[eyelidsIndex].worldPosition, 0.0001f, "Eyelids should start slightly left of head.");
        AssertFloat(90f, bones[eyelidsIndex].worldRotation.eulerAngles.z, 0.001f, "Eyelids should inherit head orientation.");
        AssertEqual(eyelidsIndex, renderer.SelectedBoneIndex, "Created eyelids should become selected.");
        AssertTrue(renderer.HasPendingChanges, "Eyelids creation should wait for top Apply.");
        AssertArray(new[] { "Create Eyelids Bone" }, undoLabels, "Eyelids creation undo label.");

        AssertFalse(
            renderer.TryCreateNamedChildBone("EYELIDS", headIndex, eyelidsLength, leftOffset, out _),
            "Duplicate eyelids creation should be rejected case-insensitively.");
        AssertArray(new[] { "Create Eyelids Bone" }, undoLabels, "Rejected duplicate should not register undo.");
    }

    private static void RendererBoneTipResizeUpdatesLengthAndRotation()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = Vector2.zero,
                rotation = Quaternion.identity,
                length = 10f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "child",
                parentId = 0,
                position = new Vector2(20f, 0f),
                rotation = Quaternion.identity,
                length = 5f,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        InvokePrivateMethod(renderer, "ResizeBoneToWorldTip", bones[0], new Vector2(0f, 20f));
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(Vector2.zero, bones[0].worldPosition, 0.0001f, "Tip resize should keep the bone base fixed.");
        AssertFloat(20f, bones[0].length, 0.0001f, "Tip resize should update bone length from base to cursor.");
        AssertFloat(90f, bones[0].worldRotation.eulerAngles.z, 0.001f, "Tip resize should aim the bone at the cursor.");
        AssertVector(new Vector2(0f, 20f), (Vector2)InvokePrivateMethod(renderer, "GetBoneEnd", 0), 0.0001f, "Tip resize should place the endpoint at the cursor.");

        var childBase = bones[1].worldPosition;
        InvokePrivateMethod(renderer, "ResizeBoneToWorldTip", bones[1], childBase + new Vector2(7f, 0f));
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertFloat(7f, bones[1].length, 0.0001f, "Child tip resize should update child length.");
        AssertFloat(0f, Mathf.DeltaAngle(0f, bones[1].worldRotation.eulerAngles.z), 0.001f, "Child tip resize should convert world direction back to local rotation.");
    }

    private static void RendererBoneTipSnapsToNearbyBases()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "resized",
                parentId = -1,
                position = new Vector2(10f, 10f),
                rotation = Quaternion.identity,
                length = 20f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "snap-target",
                parentId = -1,
                position = new Vector2(60f, 35f),
                rotation = Quaternion.identity,
                length = 15f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = "moving-child",
                parentId = 0,
                position = new Vector2(20f, 0f),
                rotation = Quaternion.identity,
                length = 10f,
                visible = true
            }
        };
        SetPrivateField(renderer, "bones", bones);
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        var coordinates = new PsdCoordinateSpace(
            new Rect(0f, 0f, 200f, 200f),
            new Vector2Int(100, 100),
            Array.Empty<PsdLayerPreviewRenderer.LayerInfo>(),
            null);
        var targetCanvas = coordinates.DocumentToView(bones[1].worldPosition);
        var nearCanvas = targetCanvas + new Vector2(6f, 0f);
        var nearWorld = coordinates.ViewToDocument(nearCanvas);
        var snapped = (Vector2)InvokePrivateMethod(
            renderer,
            "SnapBoneTipToNearbyBase",
            0,
            nearCanvas,
            nearWorld,
            coordinates);
        AssertVector(bones[1].worldPosition, snapped, 0.0001f, "Tip should snap to another visible bone base inside the screen-space radius.");

        var farCanvas = targetCanvas + new Vector2(12f, 0f);
        var farWorld = coordinates.ViewToDocument(farCanvas);
        var unsnapped = (Vector2)InvokePrivateMethod(
            renderer,
            "SnapBoneTipToNearbyBase",
            0,
            farCanvas,
            farWorld,
            coordinates);
        AssertVector(farWorld, unsnapped, 0.0001f, "Tip should stay under the cursor outside the snap radius.");

        var childBaseCanvas = coordinates.DocumentToView(bones[2].worldPosition);
        var besideChildBaseCanvas = childBaseCanvas + new Vector2(6f, 0f);
        var besideChildBaseWorld = coordinates.ViewToDocument(besideChildBaseCanvas);
        var childUnsnapped = (Vector2)InvokePrivateMethod(
            renderer,
            "SnapBoneTipToNearbyBase",
            0,
            besideChildBaseCanvas,
            besideChildBaseWorld,
            coordinates);
        AssertVector(
            besideChildBaseWorld,
            childUnsnapped,
            0.0001f,
            "Tip snapping should ignore descendants that move together with the resized bone.");
    }

    private static void RendererBoneBaseSnapsToNearbyTips()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "moved",
                parentId = -1,
                position = new Vector2(10f, 10f),
                rotation = Quaternion.identity,
                length = 20f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "snap-target",
                parentId = -1,
                position = new Vector2(60f, 35f),
                rotation = Quaternion.identity,
                length = 15f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = "moving-child",
                parentId = 0,
                position = new Vector2(20f, 0f),
                rotation = Quaternion.identity,
                length = 10f,
                visible = true
            }
        };
        SetPrivateField(renderer, "bones", bones);
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        var coordinates = new PsdCoordinateSpace(
            new Rect(0f, 0f, 200f, 200f),
            new Vector2Int(100, 100),
            Array.Empty<PsdLayerPreviewRenderer.LayerInfo>(),
            null);
        var targetTip = (Vector2)InvokePrivateMethod(renderer, "GetBoneEnd", 1);
        var targetTipCanvas = coordinates.DocumentToView(targetTip);
        var nearCanvas = targetTipCanvas + new Vector2(6f, 0f);
        var nearWorld = coordinates.ViewToDocument(nearCanvas);
        var snapped = (Vector2)InvokePrivateMethod(
            renderer,
            "SnapBoneBaseToNearbyTip",
            0,
            nearWorld,
            coordinates);
        AssertVector(targetTip, snapped, 0.0001f, "Base should snap to another visible bone tip inside the screen-space radius.");

        var farCanvas = targetTipCanvas + new Vector2(12f, 0f);
        var farWorld = coordinates.ViewToDocument(farCanvas);
        var unsnapped = (Vector2)InvokePrivateMethod(
            renderer,
            "SnapBoneBaseToNearbyTip",
            0,
            farWorld,
            coordinates);
        AssertVector(farWorld, unsnapped, 0.0001f, "Base should stay under the drag target outside the snap radius.");

        var childTip = (Vector2)InvokePrivateMethod(renderer, "GetBoneEnd", 2);
        var besideChildTipCanvas = coordinates.DocumentToView(childTip) + new Vector2(6f, 0f);
        var besideChildTipWorld = coordinates.ViewToDocument(besideChildTipCanvas);
        var childUnsnapped = (Vector2)InvokePrivateMethod(
            renderer,
            "SnapBoneBaseToNearbyTip",
            0,
            besideChildTipWorld,
            coordinates);
        AssertVector(
            besideChildTipWorld,
            childUnsnapped,
            0.0001f,
            "Base snapping should ignore descendant tips that move together with the dragged bone.");
    }

    private static void RendererShiftBaseDragPreservesTip()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = Vector2.zero,
                rotation = Quaternion.identity,
                length = 10f,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        var fixedTip = (Vector2)InvokePrivateMethod(renderer, "GetBoneEnd", 0);
        InvokePrivateMethod(
            renderer,
            "MoveBoneBaseToWorldPosition",
            bones[0],
            new Vector2(4f, 3f),
            true,
            fixedTip);
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(4f, 3f), bones[0].worldPosition, 0.0001f, "Shift base drag should move the base.");
        AssertVector(fixedTip, (Vector2)InvokePrivateMethod(renderer, "GetBoneEnd", 0), 0.0001f, "Shift base drag should keep the original tip fixed.");
        AssertFloat(Mathf.Sqrt(45f), bones[0].length, 0.0001f, "Shift base drag should recalculate length to the fixed tip.");

        InvokePrivateMethod(
            renderer,
            "MoveBoneBaseToWorldPosition",
            bones[0],
            new Vector2(5f, 3f),
            false,
            fixedTip);
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");
        AssertVector(fixedTip + Vector2.right, (Vector2)InvokePrivateMethod(renderer, "GetBoneEnd", 0), 0.0001f, "Regular base drag should continue moving the tip with the bone.");
    }

    private static void RendererIsolatedBoneModificationPreservesChildBranches()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = Vector2.zero,
                rotation = Quaternion.identity,
                length = 10f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "child",
                parentId = 0,
                position = new Vector2(10f, 0f),
                rotation = Quaternion.identity,
                length = 5f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = "grandchild",
                parentId = 1,
                position = new Vector2(5f, 0f),
                rotation = Quaternion.identity,
                length = 3f,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");
        SetPrivateField(renderer, "isolateBoneModificationForCurrentDraw", true);

        AssertTrue(renderer.SetBoneWorldPosition(0, new Vector2(4f, 3f)), "Isolated root move should succeed.");
        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(4f, 3f), bones[0].worldPosition, 0.0001f, "Isolated root move should update the selected bone.");
        AssertVector(new Vector2(10f, 0f), bones[1].worldPosition, 0.0001f, "Isolated root move should keep the direct child in world space.");
        AssertVector(new Vector2(15f, 0f), bones[2].worldPosition, 0.0001f, "Isolated root move should keep the child branch in world space.");

        AssertTrue(renderer.SetBoneWorldRotationAngle(0, 90f), "Isolated root rotation should succeed.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertFloat(90f, bones[0].worldRotation.eulerAngles.z, 0.001f, "Isolated root rotation should update the selected bone.");
        AssertVector(new Vector2(10f, 0f), bones[1].worldPosition, 0.0001f, "Isolated root rotation should keep the direct child position.");
        AssertFloat(0f, bones[1].worldRotation.eulerAngles.z, 0.001f, "Isolated root rotation should keep the direct child rotation.");
        AssertVector(new Vector2(15f, 0f), bones[2].worldPosition, 0.0001f, "Isolated root rotation should keep the child branch position.");

        SetPrivateField(renderer, "isolateBoneModificationForCurrentDraw", false);
        AssertTrue(renderer.SetBoneWorldPosition(0, new Vector2(5f, 3f)), "Regular root move should succeed after disabling isolation.");
        bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(11f, 0f), bones[1].worldPosition, 0.0001f, "Disabled isolation should let the direct child follow its parent.");
        AssertVector(new Vector2(16f, 0f), bones[2].worldPosition, 0.0001f, "Disabled isolation should let the child branch follow its parent.");
    }

    private static void RendererBoneEditKeepsSpriteVerticesRestored()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = new Vector2(8f, 0f),
                initialPosition = Vector2.zero,
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = new Vector2(8f, 0f),
                worldRotation = Quaternion.identity,
                length = 10f,
                visible = true
            }
        });
        SetPrivateField(renderer, "deformSpritesWithBonePoseForCurrentDraw", false);

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        var restVertices = new[] { new Vector2(1f, 2f), new Vector2(3f, 4f) };
        SetPrivateField(data, "vertices", restVertices);
        SetPrivateField(data, "hasSkinning", true);
        SetPrivateField(data, "deformedVertices", new[] { new Vector2(100f, 200f), new Vector2(300f, 400f) });
        SetPrivateField(data, "lastPoseVersion", 99);

        var drawVertices = (Vector2[])InvokePrivateMethod(renderer, "GetLayerVerticesForDraw", data);
        AssertTrue(ReferenceEquals(restVertices, drawVertices), "Bone Edit draw should keep sprite vertices restored even when edited bones differ from initial pose.");
        AssertFalse(GetPrivateField<bool>(renderer, "bonePoseAtRest"), "The test setup should still detect changed bone transforms.");
    }

    private static void PreviewVisualMeshLookupSurvivesVolatileSelectionKeyChange()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = new PsdLayerPreviewRenderer.LayerInfo
        {
            name = "l_leg",
            layerId = 10,
            spriteGuid = "leg-guid",
            position = new Vector2(120f, 80f),
            sourceRect = new Rect(0f, 0f, 64f, 128f)
        };
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        var originalSelectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var layerData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(layerData, "layer", layer);
        SetPrivateField(layerData, "vertices", new[]
        {
            new Vector2(120f, 80f),
            new Vector2(184f, 80f),
            new Vector2(120f, 208f)
        });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[originalSelectionKey] = layerData;

        layer.position += new Vector2(5f, -3f);
        var args = new object[] { layer, null };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryGetLayerMeshDataForDraw", args),
            "Preview visual mesh lookup should not lose importer geometry when the volatile selection key changes.");
        AssertTrue(ReferenceEquals(layerData, args[1]), "Preview visual mesh lookup should resolve the LayerVertexData by layer reference.");
    }

    private static void PreviewVisualAcceptsImporterUvsOutsideStaleLayerRect()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(100, 100);
        try
        {
            var layer = new PsdLayerPreviewRenderer.LayerInfo
            {
                name = "l_leg",
                layerId = 10,
                position = Vector2.zero,
                sourceRect = new Rect(0f, 0f, 10f, 10f),
                textureSamplingRect = new Rect(0f, 0f, 10f, 10f)
            };

            var layerData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(layerData, "layer", layer);
            SetPrivateField(layerData, "texture", texture);
            SetPrivateField(layerData, "vertices", new[]
            {
                Vector2.zero,
                new Vector2(10f, 0f),
                new Vector2(0f, 10f)
            });
            SetPrivateField(layerData, "vertexUvs", new[]
            {
                new Vector2(0.75f, 0.75f),
                new Vector2(0.80f, 0.75f),
                new Vector2(0.75f, 0.80f)
            });

            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "AreLayerVertexUvsUsable", layerData),
                "Preview visual mesh should accept valid importer UVs even when stale layer texture rect bounds do not contain them.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void WeightSliderWeightsInitializePreviewSkinning()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = Vector2.zero,
                initialPosition = Vector2.zero,
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = Vector2.zero,
                worldRotation = Quaternion.identity,
                length = 10f,
                visible = true
            }
        });
        SetPrivateField(renderer, "selectedLayerSelectionKey", "slider-layer");
        SetPrivateField(renderer, "editGeometrySelectedVertexIndex", 0);
        SetPrivateField(renderer, "deformSpritesWithBonePoseForCurrentDraw", true);

        var layer = new PsdLayerPreviewRenderer.LayerInfo
        {
            name = "Slider Layer",
            layerId = 7
        };
        var layerSelectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        SetPrivateField(renderer, "selectedLayerSelectionKey", layerSelectionKey);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        var layerData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(layerData, "layer", layer);
        SetPrivateField(layerData, "vertices", new[] { new Vector2(1f, 0f) });
        SetPrivateField(layerData, "boneWeights", new[] { default(BoneWeight) });
        SetPrivateField(layerData, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(layerData, "bindBoneInverseMatrices", (Matrix4x4[])null);
        SetPrivateField(layerData, "usesSpriteBindPoses", false);
        SetPrivateField(layerData, "hasSkinning", false);

        var vertexData = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        vertexData[layerSelectionKey] = layerData;

        AssertTrue(
            renderer.TrySetWeightSliderSelectedVertexWeights(
                new[] { 0, -1, -1, -1 },
                new[] { 1f, 0f, 0f, 0f },
                true),
            "Weight slider should assign vertex weights even when bind matrices are missing.");
        AssertTrue(GetPrivateField<bool>(layerData, "hasSkinning"), "Weight slider should enable live skinning after assigning weights.");
        var bindMatrices = GetPrivateField<Matrix4x4[]>(layerData, "bindBoneInverseMatrices");
        AssertEqual(1, bindMatrices != null ? bindMatrices.Length : 0, "Weight slider should synthesize missing bind matrices.");

        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        bones[0].position = new Vector2(2f, 0f);
        bones[0].worldPosition = new Vector2(2f, 0f);
        SetPrivateField(renderer, "bones", bones);

        var drawVertices = (Vector2[])InvokePrivateMethod(renderer, "GetLayerVerticesForDraw", layerData);
        AssertEqual(1, drawVertices.Length, "Weight slider preview draw vertex count.");
        AssertVector(new Vector2(3f, 0f), drawVertices[0], 0.0001f, "Weight slider assigned vertex should deform with the moved bone.");

        bones[0].rotation = Quaternion.AngleAxis(90f, Vector3.forward);
        bones[0].worldRotation = Quaternion.AngleAxis(90f, Vector3.forward);
        SetPrivateField(renderer, "bones", bones);
        SetPrivateField(renderer, "bonePoseVersion", 1);

        drawVertices = (Vector2[])InvokePrivateMethod(renderer, "GetLayerVerticesForDraw", layerData);
        AssertVector(new Vector2(2f, 1f), drawVertices[0], 0.0001f, "Weight slider assigned vertex should also follow bone rotation.");
    }

    private static void CurveWeightsInitializePreviewSkinningAndRotation()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var childRotation = Quaternion.AngleAxis(90f, Vector3.forward);
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "parent",
                parentId = -1,
                position = Vector2.zero,
                initialPosition = Vector2.zero,
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = Vector2.zero,
                worldRotation = Quaternion.identity,
                length = 10f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "child",
                parentId = -1,
                position = Vector2.zero,
                initialPosition = Vector2.zero,
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = Vector2.zero,
                worldRotation = Quaternion.identity,
                length = 10f,
                visible = true
            }
        });
        SetPrivateField(renderer, "curveLayerStableKey", "7");
        GetPrivateField<List<int>>(renderer, "curveSelectedLoopPositions").AddRange(new[] { 0, 1 });
        SetPrivateField(renderer, "curveSecondBoneSelectionSignature", "7|0,1");
        SetPrivateField(renderer, "curveSelectedFirstSpriteBone", 0);
        SetPrivateField(renderer, "curveSelectedSecondSpriteBone", 1);
        SetPrivateField(renderer, "deformSpritesWithBonePoseForCurrentDraw", true);

        var layer = new PsdLayerPreviewRenderer.LayerInfo
        {
            name = "Slider Layer",
            layerId = 7
        };
        var layerSelectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        SetPrivateField(renderer, "selectedLayerSelectionKey", layerSelectionKey);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        var layerData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(layerData, "layer", layer);
        SetPrivateField(layerData, "vertices", new[] { new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(1f, 1f) });
        SetPrivateField(layerData, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(layerData, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(layerData, "bindBoneInverseMatrices", (Matrix4x4[])null);
        SetPrivateField(layerData, "usesSpriteBindPoses", false);
        SetPrivateField(layerData, "hasSkinning", false);
        SetPrivateField(layerData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });

        var vertexData = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        vertexData[layerSelectionKey] = layerData;

        AssertTrue(
            renderer.TrySetCurveGraphPointValue(0, 1f),
            "Curve tool should update selected contour weights even when bind matrices are missing.");
        AssertTrue(GetPrivateField<bool>(layerData, "hasSkinning"), "Curve tool should enable live skinning after editing weights.");
        var bindMatrices = GetPrivateField<Matrix4x4[]>(layerData, "bindBoneInverseMatrices");
        AssertEqual(2, bindMatrices != null ? bindMatrices.Length : 0, "Curve tool should synthesize missing bind matrices.");

        var updatedBones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        updatedBones[1].rotation = childRotation;
        updatedBones[1].worldRotation = childRotation;
        SetPrivateField(renderer, "bones", updatedBones);

        var drawVertices = (Vector2[])InvokePrivateMethod(renderer, "GetLayerVerticesForDraw", layerData);
        AssertEqual(4, drawVertices.Length, "Curve preview draw vertex count.");
        AssertVector(new Vector2(0f, 1f), drawVertices[0], 0.0001f, "Curve edited vertex should follow the selected child bone rotation.");
    }

    private static void CurveGraphDragUsesOneUndoGroup()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "parent", -1, Vector2.zero),
            BuildVisibleWorldBone(1, "child", -1, Vector2.zero)
        });
        SetPrivateField(renderer, "curveLayerStableKey", "7");
        GetPrivateField<List<int>>(renderer, "curveSelectedLoopPositions").AddRange(new[] { 0, 1 });
        SetPrivateField(renderer, "curveSecondBoneSelectionSignature", "7|0,1");
        SetPrivateField(renderer, "curveSelectedFirstSpriteBone", 0);
        SetPrivateField(renderer, "curveSelectedSecondSpriteBone", 1);

        var layer = new PsdLayerPreviewRenderer.LayerInfo
        {
            name = "Slider Layer",
            layerId = 7
        };
        var layerSelectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        SetPrivateField(renderer, "selectedLayerSelectionKey", layerSelectionKey);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        var layerData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(layerData, "layer", layer);
        SetPrivateField(layerData, "vertices", new[] { new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(1f, 1f) });
        SetPrivateField(layerData, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(layerData, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(layerData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });
        SetPrivateField(layerData, "usesSpriteBindPoses", false);
        SetPrivateField(layerData, "hasSkinning", true);
        SetPrivateField(layerData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });

        var vertexData = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        vertexData[layerSelectionKey] = layerData;

        renderer.BeginCurveGraphPointDrag();
        AssertTrue(renderer.TrySetCurveGraphPointValue(0, 0.75f), "First curve graph drag step should change weights.");
        AssertTrue(renderer.TrySetCurveGraphPointValue(0, 0.25f), "Second curve graph drag step should change weights.");
        AssertTrue(renderer.TrySetCurveGraphPointValue(0, 0.5f), "Third curve graph drag step should change weights.");
        renderer.EndCurveGraphPointDrag();

        AssertArray(new[] { "Edit WeightCurve" }, undoLabels.ToArray(), "Curve graph drag should emit one undo label for the whole gesture.");

        AssertTrue(renderer.TrySetCurveGraphPointValue(0, 0.75f), "Non-drag curve edit should still be undoable.");
        AssertEqual(2, undoLabels.Count, "Non-drag curve edit should emit its own undo.");
    }

    private static void RendererBoneEditCommitMakesEditedPoseRest()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layerData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(layerData, "vertices", new[] { new Vector2(9f, 2f) });
        SetPrivateField(layerData, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(layerData, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(layerData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });
        SetPrivateField(layerData, "usesSpriteBindPoses", true);
        SetPrivateField(layerData, "hasSkinning", true);
        SetPrivateField(layerData, "skinMatrices", new[] { Matrix4x4.identity });
        SetPrivateField(layerData, "deformedVertices", new[] { Vector2.one });
        SetPrivateField(layerData, "lastPoseVersion", 42);

        var vertexData = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        vertexData["layer"] = layerData;

        var patch = CreatePrivateNestedInstance(renderer, "AutoGeometryPatch");
        SetPrivateField(patch, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(patch, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });
        SetPrivateField(patch, "usesSpriteBindPoses", true);
        GetPrivateList(renderer, "autoGeometryPatches").Add(patch);

        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = new Vector2(8f, 2f),
                initialPosition = Vector2.zero,
                rotation = Quaternion.AngleAxis(25f, Vector3.forward),
                initialRotation = Quaternion.identity,
                worldPosition = new Vector2(8f, 2f),
                worldRotation = Quaternion.AngleAxis(25f, Vector3.forward),
                length = 10f,
                visible = true
            }
        });

        AssertTrue(renderer.CommitCurrentBonePoseAsRest(), "Committing Bone Edit should store edited transforms as rest pose.");

        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(8f, 2f), bones[0].initialPosition, 0.0001f, "Committed rest position should match edited position.");
        AssertFloat(25f, bones[0].initialRotation.eulerAngles.z, 0.001f, "Committed rest rotation should match edited rotation.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "IsCurrentBonePoseRest"), "Committed Bone Edit pose should no longer count as preview deformation.");
        AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Committed Bone Edit rest pose should wait for top Apply.");

        var bindMatrices = GetPrivateField<Matrix4x4[]>(layerData, "bindBoneInverseMatrices");
        var skinMatrices = new Matrix4x4[bindMatrices.Length];
        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            GetPrivateField<int[]>(layerData, "spriteBoneToRendererBone"),
            bindMatrices,
            GetPrivateField<bool>(layerData, "usesSpriteBindPoses"),
            null,
            1f,
            Vector2.zero,
            skinMatrices);
        var restVertex = skinMatrices[0].MultiplyPoint3x4(new Vector3(9f, 2f, 0f));
        AssertVector(new Vector2(9f, 2f), new Vector2(restVertex.x, restVertex.y), 0.0001f, "Committed Bone Edit rest bind matrix should keep weighted vertices in place.");
        AssertFalse(GetPrivateField<bool>(layerData, "usesSpriteBindPoses"), "Bone Edit commit should convert live skinning data to renderer bind pose space.");
        AssertEqual(null, GetPrivateField<Vector2[]>(layerData, "deformedVertices"), "Bone Edit commit should invalidate live deformed vertices.");
        AssertEqual(null, GetPrivateField<Matrix4x4[]>(layerData, "skinMatrices"), "Bone Edit commit should invalidate live skin matrices.");
        AssertEqual(-1, GetPrivateField<int>(layerData, "lastPoseVersion"), "Bone Edit commit should invalidate live pose cache.");

        var patchBindMatrices = GetPrivateField<Matrix4x4[]>(patch, "bindBoneInverseMatrices");
        var patchSkinMatrices = new Matrix4x4[patchBindMatrices.Length];
        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            GetPrivateField<int[]>(patch, "spriteBoneToRendererBone"),
            patchBindMatrices,
            GetPrivateField<bool>(patch, "usesSpriteBindPoses"),
            null,
            1f,
            Vector2.zero,
            patchSkinMatrices);
        var patchRestVertex = patchSkinMatrices[0].MultiplyPoint3x4(new Vector3(9f, 2f, 0f));
        AssertVector(new Vector2(9f, 2f), new Vector2(patchRestVertex.x, patchRestVertex.y), 0.0001f, "Committed Bone Edit rest bind matrix should update pending patches.");
        AssertFalse(GetPrivateField<bool>(patch, "usesSpriteBindPoses"), "Bone Edit commit should convert pending patch skinning to renderer bind pose space.");

        AssertFalse(renderer.CommitCurrentBonePoseAsRest(), "Committing an unchanged rest pose should be a no-op.");
    }

    private static void RendererBoneEditCommitIgnoresDefaultIdentityRestPose()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "hasPendingChanges", false);
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = new Vector2(3f, 4f),
                initialPosition = new Vector2(3f, 4f),
                rotation = Quaternion.identity,
                initialRotation = default,
                worldPosition = new Vector2(3f, 4f),
                worldRotation = Quaternion.identity,
                length = 10f,
                visible = true
            }
        });

        AssertFalse(renderer.CommitCurrentBonePoseAsRest(), "Default initial rotation plus identity current rotation is already a rest pose.");
        AssertFalse(GetPrivateField<bool>(renderer, "hasPendingChanges"), "No-op Bone Edit commit must not dirty the renderer.");
    }

    private static void RendererRevertClearsAssetToolStateAndAssetScopedPatches()
    {
        var renderer = new PsdLayerPreviewRenderer();
        const string assetPath = "Assets/__ReanimatorMissingAssetForSelfTests.psd";
        const string otherAssetPath = "Assets/__ReanimatorOtherAssetForSelfTests.psd";

        SetPrivateField(renderer, "selectedBoneIndex", 3);
        SetPrivateField(renderer, "isRotating", true);
        SetPrivateField(renderer, "isDragging", true);
        SetPrivateField(renderer, "hasPendingChanges", true);
        SetPrivateField(renderer, "cachedPath", otherAssetPath);

        AddPrivateNestedListItem(renderer, "elbowLines", "ElbowLine");
        AddPrivateNestedListItem(renderer, "limbLines", "LimbLine");
        SetPrivateField(renderer, "elbowIsCreating", true);
        SetPrivateField(renderer, "elbowIsDragging", true);
        SetPrivateField(renderer, "elbowControlId", 91);
        SetPrivateField(renderer, "limbIsCreating", true);
        SetPrivateField(renderer, "limbIsDragging", true);
        SetPrivateField(renderer, "limbControlId", 92);

        SetPrivateField(renderer, "boobHasPoint", true);
        SetPrivateField(renderer, "boobLayerStableKey", "boob-layer");
        SetPrivateField(renderer, "boobIsDragging", true);
        SetPrivateField(renderer, "curveLayerStableKey", "curve-layer");
        GetPrivateField<List<int>>(renderer, "curveSelectedLoopPositions").Add(1);
        GetPrivateField<List<float>>(renderer, "curveEditedChildWeights").Add(0.5f);
        SetPrivateField(renderer, "curveGraphSelectionSignature", "curve-graph");
        SetPrivateField(renderer, "curveSecondBoneSelectionSignature", "curve-second");
        SetPrivateField(renderer, "curveSelectedFirstSpriteBone", 1);
        SetPrivateField(renderer, "curveSelectedSecondSpriteBone", 2);
        SetPrivateField(renderer, "curveIsDragging", true);
        SetPrivateField(renderer, "curveControlId", 93);
        SetPrivateField(renderer, "volumeCurveIsDragging", true);
        SetPrivateField(renderer, "volumeCurveControlId", 94);

        SetPrivateField(renderer, "editGeometrySelectedVertexIndex", 7);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", "edit-layer");
        GetPrivateField<List<int>>(renderer, "editGeometrySelectedVertexIndices").AddRange(new[] { 7, 8 });
        SetPrivateField(renderer, "editGeometryIsDragging", true);
        SetPrivateField(renderer, "editGeometryControlId", 95);
        SetPrivateField(renderer, "editGeometryIsRectSelecting", true);
        SetPrivateField(renderer, "editGeometryRectToggleSelection", true);
        SetPrivateField(renderer, "weightSliderSelectedLayerStableKey", "slider-layer");
        GetPrivateField<List<int>>(renderer, "weightSliderSelectedVertexIndices").Add(9);
        SetPrivateField(renderer, "weightSliderIsRectSelecting", true);
        SetPrivateField(renderer, "weightSliderControlId", 96);
        SetPrivateField(renderer, "weightSliderIsWeightDragging", true);
        SetPrivateField(renderer, "weightSliderWeightDragLayerStableKey", "slider-layer");
        SetPrivateField(renderer, "weightSliderWeightDragVertexIndex", 9);
        SetPrivateField(renderer, "weightSliderWeightDragBones", new[] { 1 });
        SetNonEmptyPrivateArray(renderer, "weightSliderWeightDragStates");
        SetPrivateField(renderer, "weightSliderWeightDragMoved", true);

        SetPrivateField(renderer, "createEdgeStartVertexIndex", 10);
        SetPrivateField(renderer, "createEdgeLayerStableKey", "edge-layer");
        SetPrivateField(renderer, "radiusCullHasPoint", true);
        SetPrivateField(renderer, "radiusCullLayerStableKey", "radius-layer");
        SetPrivateField(renderer, "radiusCullIsDragging", true);
        SetPrivateField(renderer, "radiusCullHandleIsDragging", true);
        SetPrivateField(renderer, "radiusCullControlId", 97);
        SetPrivateField(renderer, "radiusCullHasRadiusOverride", true);
        SetPrivateField(renderer, "radiusCullRadiusOverride", 10f);
        SetPrivateField(renderer, "jointIsDragging", true);
        SetPrivateField(renderer, "jointControlId", 98);
        SetPrivateField(renderer, "jointNearestBoneIndex", 4);

        AddPrivateAssetScopedListItem(renderer, "appliedElbowVertices", "AppliedElbowVertex", assetPath);
        AddPrivateAssetScopedListItem(renderer, "appliedElbowVertices", "AppliedElbowVertex", otherAssetPath);
        AddPrivateAssetScopedListItem(renderer, "autoGeometryPatches", "AutoGeometryPatch", assetPath);
        AddPrivateAssetScopedListItem(renderer, "autoGeometryPatches", "AutoGeometryPatch", otherAssetPath);

        renderer.Revert(assetPath);

        AssertEqual(-1, GetPrivateField<int>(renderer, "selectedBoneIndex"), "Selected bone should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "isRotating"), "Bone rotation drag should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "isDragging"), "Bone position drag should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Pending changes should reset.");
        AssertEqual(assetPath, GetPrivateField<string>(renderer, "cachedPath"), "Revert should update cached path.");

        AssertEqual(0, GetPrivateListCount(renderer, "elbowLines"), "Elbow lines should clear.");
        AssertEqual(0, GetPrivateListCount(renderer, "limbLines"), "Limb lines should clear.");
        AssertFalse(GetPrivateField<bool>(renderer, "elbowIsCreating"), "Elbow create should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "elbowIsDragging"), "Elbow drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "elbowControlId"), "Elbow control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "limbIsCreating"), "Limb create should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "limbIsDragging"), "Limb drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "limbControlId"), "Limb control id should reset.");

        AssertFalse(GetPrivateField<bool>(renderer, "boobHasPoint"), "Boob point should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "boobLayerStableKey"), "Boob layer should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "boobIsDragging"), "Boob drag should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "curveLayerStableKey"), "Curve layer should reset.");
        AssertEqual(0, GetPrivateField<List<int>>(renderer, "curveSelectedLoopPositions").Count, "Curve loop selection should clear.");
        AssertEqual(0, GetPrivateField<List<float>>(renderer, "curveEditedChildWeights").Count, "Curve edited weights should clear.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "curveGraphSelectionSignature"), "Curve graph signature should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "curveSecondBoneSelectionSignature"), "Curve second bone signature should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "curveSelectedFirstSpriteBone"), "Curve first sprite bone should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "curveSelectedSecondSpriteBone"), "Curve second sprite bone should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "curveIsDragging"), "Curve drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "curveControlId"), "Curve control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "volumeCurveIsDragging"), "Volume curve drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "volumeCurveControlId"), "Volume curve control id should reset.");

        AssertEqual(-1, GetPrivateField<int>(renderer, "editGeometrySelectedVertexIndex"), "Edit geometry selected vertex should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "editGeometrySelectedLayerStableKey"), "Edit geometry selection layer should reset.");
        AssertEqual(0, GetPrivateField<List<int>>(renderer, "editGeometrySelectedVertexIndices").Count, "Edit geometry selection list should clear.");
        AssertFalse(GetPrivateField<bool>(renderer, "editGeometryIsDragging"), "Edit geometry drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "editGeometryControlId"), "Edit geometry control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "editGeometryIsRectSelecting"), "Edit geometry rect select should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "editGeometryRectToggleSelection"), "Edit geometry rect toggle should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "weightSliderSelectedLayerStableKey"), "Weight slider selection layer should reset.");
        AssertEqual(0, GetPrivateField<List<int>>(renderer, "weightSliderSelectedVertexIndices").Count, "Weight slider selected vertices should clear.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderIsRectSelecting"), "Weight slider rect select should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "weightSliderControlId"), "Weight slider control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderIsWeightDragging"), "Weight slider drag should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "weightSliderWeightDragLayerStableKey"), "Weight slider drag layer should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "weightSliderWeightDragVertexIndex"), "Weight slider drag vertex should reset.");
        AssertEqual(0, GetPrivateField<int[]>(renderer, "weightSliderWeightDragBones").Length, "Weight slider drag bones should clear.");
        AssertEqual(0, GetPrivateField<Array>(renderer, "weightSliderWeightDragStates").Length, "Weight slider per-vertex drag states should clear.");
        AssertFalse(GetPrivateField<bool>(renderer, "weightSliderWeightDragMoved"), "Weight slider moved should reset.");

        AssertEqual(-1, GetPrivateField<int>(renderer, "createEdgeStartVertexIndex"), "Create edge start should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "createEdgeLayerStableKey"), "Create edge layer should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHasPoint"), "Radius cull point should reset.");
        AssertEqual(string.Empty, GetPrivateField<string>(renderer, "radiusCullLayerStableKey"), "Radius cull layer should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullIsDragging"), "Radius cull drag should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHandleIsDragging"), "Radius cull handle drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "radiusCullControlId"), "Radius cull control id should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "radiusCullHasRadiusOverride"), "Radius override flag should reset.");
        AssertFloat(0f, GetPrivateField<float>(renderer, "radiusCullRadiusOverride"), 0.0001f, "Radius override should reset.");
        AssertFalse(GetPrivateField<bool>(renderer, "jointIsDragging"), "Joint drag should reset.");
        AssertEqual(0, GetPrivateField<int>(renderer, "jointControlId"), "Joint control id should reset.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "jointNearestBoneIndex"), "Joint nearest bone should reset.");

        AssertEqual(1, GetPrivateListCount(renderer, "appliedElbowVertices"), "Only other asset applied vertices should remain.");
        AssertEqual(otherAssetPath, GetPrivateListItemAssetPath(renderer, "appliedElbowVertices", 0), "Remaining applied vertex should belong to other asset.");
        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Only other asset auto patches should remain.");
        AssertEqual(otherAssetPath, GetPrivateListItemAssetPath(renderer, "autoGeometryPatches", 0), "Remaining auto patch should belong to other asset.");
    }

    private static void SetNonEmptyPrivateArray(object target, string fieldName)
    {
        var field = GetRequiredPrivateField(target, fieldName);
        var elementType = field.FieldType.GetElementType();
        AssertTrue(elementType != null, $"{fieldName} should be an array field.");
        field.SetValue(target, Array.CreateInstance(elementType, 1));
    }

    private static void ReanimatorProjectChangeIgnoresActiveApply()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            var layer = BuildLayer("apply-layer", 94, new Vector2(10f, 20f));
            layer.sourceRect = new Rect(4f, 1294f, 341f, 309f);
            layer.textureRect = layer.sourceRect;
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
            SetPrivateField(window, "layerPreviewRenderer", renderer);

            SetPrivateField(window, "isApplyingChanges", true);
            InvokePrivateMethod(window, "OnProjectChange");
            AssertEqual(1, GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos").Count, "Project change during Apply must not clear optimized preview state.");

            SetPrivateField(window, "isApplyingChanges", false);
            InvokePrivateMethod(window, "OnProjectChange");
            AssertEqual(0, GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos").Count, "Project change outside Apply should still dirty the preview cache.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorProjectChangePreservesPendingSnapshot()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            var layer = BuildLayer("pending-layer", 95, new Vector2(10f, 20f));
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
            SetPrivateField(renderer, "hasPendingChanges", true);
            SetPrivateField(window, "layerPreviewRenderer", renderer);

            InvokePrivateMethod(window, "OnProjectChange");
            AssertEqual(1, GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos").Count, "Project change must not discard pending Reanimator snapshot.");
            AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Project change must leave pending edits pending until Apply/Revert.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void RendererPendingSnapshotSuppressesDependencyHashRefresh()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(1, 1);
        try
        {
            SetPrivateField(renderer, "cachedPath", "Assets/A.psd");
            SetPrivateField(renderer, "cachedAssetDependencyHash", "snapshot-hash");
            SetPrivateField(renderer, "cachedTexture", texture);
            SetPrivateField(renderer, "hasPendingChanges", true);

            AssertFalse(
                (bool)InvokePrivateMethod(renderer, "ShouldCheckAssetDependencyHash", "Assets/A.psd"),
                "Pending Reanimator snapshot must not accept external dependency hash changes as the new baseline.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void FailedApplyPreservesBrushAltBoneMapAndWeights()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var editedRotation = Quaternion.AngleAxis(27f, Vector3.forward);
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "spine",
                parentId = -1,
                position = new Vector2(8f, 3f),
                initialPosition = Vector2.zero,
                rotation = editedRotation,
                initialRotation = Quaternion.identity,
                length = 12f,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "l_nipple",
                parentId = 0,
                position = new Vector2(4f, 1f),
                initialPosition = new Vector2(4f, 1f),
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                length = 4f,
                visible = true
            }
        });
        SetPrivateField(renderer, "bonePoseAtRest", false);
        SetPrivateField(renderer, "hasPendingChanges", true);

        var layer = BuildLayer("body", 991, Vector2.zero);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[] { Vector2.zero });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 1, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });

        var dataByKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataByKey["body-key"] = data;
        var failureState = InvokePrivateMethod(renderer, "CaptureApplyFailureWorkingState");

        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(data, "boneWeights", new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f } });
        dataByKey.Clear();
        GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones").Clear();
        SetPrivateField(renderer, "hasPendingChanges", false);

        InvokePrivateMethod(renderer, "RestoreApplyFailureWorkingState", failureState);

        var restoredData = ((System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey"))["body-key"];
        var restoredMap = GetPrivateField<int[]>(restoredData, "spriteBoneToRendererBone");
        var restoredWeights = GetPrivateField<BoneWeight[]>(restoredData, "boneWeights");
        var restoredBones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertTrue(restoredMap.SequenceEqual(new[] { 0, 1 }), "Failed Apply must retain the bone selected through Brush Alt.");
        AssertEqual(1, restoredWeights[0].boneIndex0, "Failed Apply must retain weights painted for the replacement bone.");
        AssertFloat(1f, restoredWeights[0].weight0, 0.0001f, "Failed Apply must retain the replacement bone weight.");
        AssertEqual(2, restoredBones.Count, "Failed Apply must restore the complete working skeleton.");
        AssertVector(new Vector2(8f, 3f), restoredBones[0].position, 0.0001f, "Failed Apply must not restore an older bone pose.");
        AssertFloat(27f, restoredBones[0].rotation.eulerAngles.z, 0.001f, "Failed Apply must retain the pre-Apply bone rotation.");
        AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Failed Apply must leave the working changes pending for retry.");
    }

    private static void RendererExternalAssetChangeInvalidatesPreviewCacheOnly()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(1, 1);
        try
        {
            var layer = BuildLayer("external-change-layer", 96, new Vector2(10f, 20f));
            SetPrivateField(renderer, "cachedPath", "Assets/A.psd");
            SetPrivateField(renderer, "cachedAssetDependencyHash", "snapshot-hash");
            SetPrivateField(renderer, "cachedTexture", texture);
            SetPrivateField(renderer, "cachedDocumentSize", new Vector2Int(256, 512));
            SetPrivateField(renderer, "cachedPreviewPipelineVersion", 31);
            SetPrivateField(renderer, "selectedLayerSelectionKey", "96");
            GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos").Add(layer);

            InvokePrivateMethod(renderer, "InvalidatePreviewCacheForExternalAssetChange");

            AssertTrue(GetPrivateField<Texture2D>(renderer, "cachedTexture") == null, "External PSD saves should force the next preview rebuild.");
            AssertEqual(default(Vector2Int), GetPrivateField<Vector2Int>(renderer, "cachedDocumentSize"), "External PSD saves should drop stale document size.");
            AssertEqual(-1, GetPrivateField<int>(renderer, "cachedPreviewPipelineVersion"), "External PSD saves should mark the cache version stale.");
            AssertEqual(1, GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos").Count, "External invalidation should preserve old layers long enough to carry UI visibility into rebuild.");
            AssertEqual("96", GetPrivateField<string>(renderer, "selectedLayerSelectionKey"), "External invalidation should preserve the selected layer key for same-asset rebuild.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void IncrementalPreviewRebuildResolvesPsdLayerRects()
    {
        var source = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.Rebuild.PreviewCache.cs"));

        AssertTrue(
            source.Contains("BuildPsdLayerMosaicPositionBySpriteGuid(so)"),
            "Incremental preview rebuild should read PSD layer mosaic anchors.");
        AssertTrue(
            source.Contains("PsdCoordinateSpace.ResolveSpriteRectForPsdLayerImport("),
            "Incremental preview rebuild should repair imported rects the same way as full rebuild.");
        AssertTrue(
            source.Contains("textureRect = serializedRect"),
            "Incremental preview rebuild should sample from the physical imported texture rect while drawing logical PSD bounds.");
        AssertFalse(
            source.Contains("sourceRect = rect,\r\n                textureRect = rect") ||
            source.Contains("sourceRect = rect,\n                textureRect = rect"),
            "Incremental preview rebuild must not collapse logical source rects and physical texture rects back together.");
    }

    private static void CreateBoneHoverPreviewRequestsMouseMove()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            SetPrivateField(window, "layerPreviewRenderer", new PsdLayerPreviewRenderer());
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.CreateBone);

            InvokePrivateMethod(window, "UpdateWindowEventOptions");
            AssertTrue(window.wantsMouseMove, "Create Bone hover marker should receive immediate mouse move events.");

            AssertTrue((bool)InvokePrivateMethod(window, "ShouldRepaintCreateBoneHoverPreview", 10d), "Create Bone hover should request an update repaint immediately.");
            SetPrivateField(window, "lastCreateBoneHoverRepaintTime", 10d);
            AssertFalse((bool)InvokePrivateMethod(window, "ShouldRepaintCreateBoneHoverPreview", 10.01d), "Create Bone hover repaint should be throttled inside the interval.");
            AssertTrue((bool)InvokePrivateMethod(window, "ShouldRepaintCreateBoneHoverPreview", 10.04d), "Create Bone hover repaint should resume after the throttle interval.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void CreateBoneSnapsToBoneBases()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "parent",
                parentId = -1,
                visible = true,
                worldPosition = new Vector2(20f, 20f),
                worldRotation = Quaternion.identity,
                length = 40f
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "child",
                parentId = 0,
                visible = true,
                worldPosition = new Vector2(105f, 35f),
                worldRotation = Quaternion.identity,
                length = 25f
            }
        };
        SetPrivateField(renderer, "bones", bones);

        var localRect = new Rect(0f, 0f, 220f, 180f);
        var coordinates = new PsdCoordinateSpace(
            localRect,
            new Vector2Int(160, 120),
            Array.Empty<PsdLayerPreviewRenderer.LayerInfo>(),
            null);
        var context = new ReanimatorPreviewContext(localRect, coordinates, Array.Empty<PsdLayerPreviewRenderer.LayerInfo>(), new PsdPreviewVisibilityOptions());
        var childBaseCanvas = coordinates.DocumentToView(bones[1].worldPosition);
        var mouseNearBase = childBaseCanvas + new Vector2(3f, 0f);

        AssertTrue(
            renderer.TryPrepareCreateBoneStart(context, mouseNearBase, out var startCanvas, out var shouldCreate),
            "Create Bone should detect the hovered bone base as a creation anchor.");
        AssertTrue(shouldCreate, "Base anchor should start creating a new bone instead of only selecting the existing one.");
        AssertVector(childBaseCanvas, startCanvas, 0.0001f, "Create Bone should snap start to the hovered bone base.");
        AssertEqual(0, renderer.SelectedBoneIndex, "Base anchor should inherit the hovered bone parent as the new bone parent.");

        AssertTrue(
            renderer.TryGetCreateBoneParentAnchor(context, mouseNearBase, out var parentBoneIndex, out var anchorCanvas, out var anchorKind),
            "Create Bone hover marker should detect base anchors.");
        AssertEqual(0, parentBoneIndex, "Base hover marker should report the parent used for creation.");
        AssertVector(childBaseCanvas, anchorCanvas, 0.0001f, "Base hover marker should draw on the hovered base.");
        AssertEqual(PsdLayerPreviewRenderer.CreateBoneAnchorKind.Base, anchorKind, "Base hover marker should use the larger base marker.");

        AssertTrue(
            renderer.TryGetCreateBoneParentAnchor(context, mouseNearBase, out _, out _, out _, out var anchorBoneIndex),
            "Create Bone hover marker should expose the source bone used for visual sizing.");
        AssertEqual(1, anchorBoneIndex, "Base hover marker should size from the hovered child bone, not the creation parent.");
    }

    private static void CreateBoneBaseHoverRadiusScalesWithBoneSize()
    {
        var smallBaseOuter = PsdLayerPreviewRenderer.CalculateCreateBoneAnchorOuterRadius(
            PsdLayerPreviewRenderer.CreateBoneAnchorKind.Base,
            40f);
        var largeBaseOuter = PsdLayerPreviewRenderer.CalculateCreateBoneAnchorOuterRadius(
            PsdLayerPreviewRenderer.CreateBoneAnchorKind.Base,
            260f);
        var veryLargeBaseOuter = PsdLayerPreviewRenderer.CalculateCreateBoneAnchorOuterRadius(
            PsdLayerPreviewRenderer.CreateBoneAnchorKind.Base,
            1000f);

        AssertFloat(10f, smallBaseOuter, 0.0001f, "Small bones should keep the existing minimum base hover marker.");
        AssertTrue(largeBaseOuter > smallBaseOuter, "Large bones should receive a visibly larger base hover marker.");
        AssertFloat(28f, veryLargeBaseOuter, 0.0001f, "Very large bones should clamp the base hover marker to avoid covering the preview.");

        var largeInner = PsdLayerPreviewRenderer.CalculateCreateBoneAnchorInnerRadius(largeBaseOuter);
        AssertTrue(largeInner > 4.2f, "Inner base hover marker should scale together with the outer marker.");
    }

}
#endif
