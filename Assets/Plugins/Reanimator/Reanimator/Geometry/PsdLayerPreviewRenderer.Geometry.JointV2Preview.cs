#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private readonly struct JointV2BoundaryEntry
    {
        public JointV2BoundaryEntry(float distance, int vertexIndex)
        {
            this.distance = distance;
            this.vertexIndex = vertexIndex;
        }

        public readonly float distance;
        public readonly int vertexIndex;
    }

    public bool ApplyJointV2(
        AnimationCurve weightCurve,
        AnimationCurve specialWeightCurve,
        float specialJointDistance,
        float boneBaseOffset,
        out string error)
    {
        error = string.Empty;
        if (!TryBuildJointV2PreviewGeometry(
                out var sourceData,
                out var previewData,
                out _,
                out _,
                out _,
                weightCurve,
                specialWeightCurve,
                specialJointDistance) ||
            sourceData == null || previewData == null || sourceData.layer == null)
        {
            error = "Joint V2 preview is unavailable";
            return false;
        }

        var attachedContourPoints = new List<Vector2>(16);
        if (!TryGetJointV2AttachedGuidePoints(
                specialJointDistance,
                out var centerPoint,
                out _,
                out _,
                out _,
                attachedContourPoints) ||
            !IsFinite(centerPoint))
        {
            error = "Joint V2 center point is unavailable";
            return false;
        }

        var dynamicBoneBaseTarget = GetJointV2InterpolatedBaseTarget(centerPoint, boneBaseOffset);
        if (!IsFinite(dynamicBoneBaseTarget) ||
            (GetBoneEnd(selectedBoneIndex) - dynamicBoneBaseTarget).sqrMagnitude <= 0.0001f)
        {
            error = "Joint V2 center point is unavailable";
            return false;
        }

        var selectionKey = BuildSelectionKey(sourceData.layer);
        var stableKey = BuildKey(sourceData.layer.name, sourceData.layer.layerId);
        RegisterUndo("Apply Joint V2");
        sourceData.vertices = CloneArray(previewData.vertices) ?? Array.Empty<Vector2>();
        sourceData.vertexUvs = CloneArray(previewData.vertexUvs) ?? Array.Empty<Vector2>();
        sourceData.triangles = CloneArray(previewData.triangles) ?? Array.Empty<int>();
        sourceData.allEdgePairs = CloneArray(previewData.allEdgePairs) ?? Array.Empty<int>();
        sourceData.boundaryEdgePairs = CloneArray(previewData.boundaryEdgePairs) ?? Array.Empty<int>();
        sourceData.boneWeights = CloneArray(previewData.boneWeights) ?? Array.Empty<BoneWeight>();
        sourceData.spriteBoneToRendererBone = CloneArray(previewData.spriteBoneToRendererBone) ?? Array.Empty<int>();
        sourceData.bindBoneInverseMatrices = CloneArray(previewData.bindBoneInverseMatrices) ?? Array.Empty<Matrix4x4>();
        sourceData.importerMode = previewData.importerMode;
        sourceData.usesSpriteBindPoses = previewData.usesSpriteBindPoses;
        sourceData.hasSkinning = previewData.hasSkinning;
        sourceData.lastPoseVersion = -1;
        sourceData.deformedVertices = null;
        sourceData.skinMatrices = null;
        sourceData.boneOrderTriangles = null;
        sourceData.boneOrderSourceTriangles = null;
        sourceData.boneOrderSourceWeights = null;
        sourceData.lastBoneOrderVersion = -1;
        var preservedSkinMatrices = CaptureJointV2SkinMatrices(sourceData);
        MoveJointV2DynamicBoneBasePreservingTipAndChildren(dynamicBoneBaseTarget);
        RestoreJointV2SkinningAfterBaseMove(sourceData, preservedSkinMatrices);
        MarkEditGeometryDataChanged(sourceData, selectionKey, stableKey);
        return true;
    }

    private Vector2 GetJointV2InterpolatedBaseTarget(Vector2 centerPoint, float normalizedOffset)
    {
        if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null)
            return centerPoint;

        return Vector2.Lerp(
            GetBoneBasePoint(bones[selectedBoneIndex]),
            centerPoint,
            Mathf.Clamp01(normalizedOffset));
    }

    private Matrix4x4[] CaptureJointV2SkinMatrices(LayerVertexData data)
    {
        if (data == null || data.spriteBoneToRendererBone == null ||
            data.bindBoneInverseMatrices == null ||
            data.spriteBoneToRendererBone.Length == 0 ||
            data.spriteBoneToRendererBone.Length != data.bindBoneInverseMatrices.Length)
        {
            return null;
        }

        var skinMatrices = new Matrix4x4[data.bindBoneInverseMatrices.Length];
        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            data.spriteBoneToRendererBone,
            data.bindBoneInverseMatrices,
            data.usesSpriteBindPoses,
            data.layer,
            data.pixelsPerUnit,
            data.pivotPixels,
            skinMatrices);
        return skinMatrices;
    }

    private void RestoreJointV2SkinningAfterBaseMove(
        LayerVertexData data,
        IReadOnlyList<Matrix4x4> preservedSkinMatrices)
    {
        if (data == null || preservedSkinMatrices == null ||
            data.spriteBoneToRendererBone == null ||
            data.spriteBoneToRendererBone.Length == 0 ||
            data.spriteBoneToRendererBone.Length != preservedSkinMatrices.Count)
        {
            return;
        }

        var identityBindMatrices = new Matrix4x4[preservedSkinMatrices.Count];
        for (var matrixIndex = 0; matrixIndex < identityBindMatrices.Length; matrixIndex++)
            identityBindMatrices[matrixIndex] = Matrix4x4.identity;

        var movedBoneMatrices = new Matrix4x4[preservedSkinMatrices.Count];
        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            data.spriteBoneToRendererBone,
            identityBindMatrices,
            data.usesSpriteBindPoses,
            data.layer,
            data.pixelsPerUnit,
            data.pivotPixels,
            movedBoneMatrices);

        var compensatedBindMatrices = new Matrix4x4[preservedSkinMatrices.Count];
        for (var matrixIndex = 0; matrixIndex < compensatedBindMatrices.Length; matrixIndex++)
        {
            compensatedBindMatrices[matrixIndex] =
                movedBoneMatrices[matrixIndex].inverse * preservedSkinMatrices[matrixIndex];
        }

        data.bindBoneInverseMatrices = compensatedBindMatrices;
        MarkSkinningWeightsChanged(data);
    }

    private bool MoveJointV2DynamicBoneBasePreservingTipAndChildren(Vector2 targetWorld)
    {
        if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null)
            return false;

        var dynamicBone = bones[selectedBoneIndex];
        if ((dynamicBone.worldPosition - targetWorld).sqrMagnitude <= 0.000001f)
            return false;

        var preservedWorldTip = GetBoneEnd(selectedBoneIndex);
        if ((preservedWorldTip - targetWorld).sqrMagnitude <= 0.0001f)
            return false;

        var previousDynamicPosition = dynamicBone.position;
        var previousDynamicRotation = dynamicBone.rotation;
        var previousDynamicInitialPosition = dynamicBone.initialPosition;
        var previousDynamicInitialRotation = dynamicBone.initialRotation.Equals(default(Quaternion))
            ? Quaternion.identity
            : dynamicBone.initialRotation;

        List<IsolatedChildBonePose> childPoses = null;
        var previousChildBones = new Dictionary<int, BoneInfo>();
        for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            var child = bones[boneIndex];
            if (child == null || child.parentId != selectedBoneIndex)
                continue;

            childPoses ??= new List<IsolatedChildBonePose>();
            childPoses.Add(new IsolatedChildBonePose(
                boneIndex,
                child.worldPosition,
                child.worldRotation));
            previousChildBones[boneIndex] = CloneJointV2Bone(child);
        }

        MoveBoneBaseToWorldPosition(
            dynamicBone,
            targetWorld,
            true,
            preservedWorldTip);
        var dynamicRotationAdjustment = dynamicBone.rotation * Quaternion.Inverse(previousDynamicRotation);
        dynamicBone.initialPosition = previousDynamicInitialPosition +
                                      (dynamicBone.position - previousDynamicPosition);
        dynamicBone.initialRotation = dynamicRotationAdjustment * previousDynamicInitialRotation;
        bones[selectedBoneIndex] = dynamicBone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(childPoses);

        foreach (var pair in previousChildBones)
        {
            var boneIndex = pair.Key;
            if (boneIndex < 0 || boneIndex >= bones.Count || bones[boneIndex] == null)
                continue;

            var previousChild = pair.Value;
            var child = bones[boneIndex];
            var previousChildInitialRotation = previousChild.initialRotation.Equals(default(Quaternion))
                ? Quaternion.identity
                : previousChild.initialRotation;
            var childRotationAdjustment = child.rotation * Quaternion.Inverse(previousChild.rotation);
            child.initialPosition = previousChild.initialPosition +
                                    (child.position - previousChild.position);
            child.initialRotation = childRotationAdjustment * previousChildInitialRotation;
            bones[boneIndex] = child;
        }

        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    private static BoneInfo CloneJointV2Bone(BoneInfo source)
    {
        return new BoneInfo
        {
            index = source.index,
            name = source.name,
            parentId = source.parentId,
            position = source.position,
            rotation = source.rotation,
            initialPosition = source.initialPosition,
            initialRotation = source.initialRotation,
            length = source.length,
            depth = source.depth,
            color = source.color,
            visible = source.visible,
            worldPosition = source.worldPosition,
            worldRotation = source.worldRotation
        };
    }

    private bool TryBuildJointV2VirtualBonePose(
        Vector2 targetWorld,
        out List<BoneInfo> virtualBones)
    {
        virtualBones = CloneBones(bones);
        if (selectedBoneIndex < 0 || selectedBoneIndex >= virtualBones.Count ||
            virtualBones[selectedBoneIndex] == null || !IsFinite(targetWorld))
        {
            return false;
        }

        var dynamicBone = virtualBones[selectedBoneIndex];
        var preservedWorldTip = GetBoneEnd(selectedBoneIndex);
        var direction = preservedWorldTip - targetWorld;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        var worldRotation = Quaternion.AngleAxis(
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg,
            Vector3.forward);
        dynamicBone.position = ConvertJointV2VirtualWorldPositionToLocal(
            virtualBones,
            dynamicBone.parentId,
            targetWorld);
        dynamicBone.rotation = ConvertJointV2VirtualWorldRotationToLocal(
            virtualBones,
            dynamicBone.parentId,
            worldRotation);
        dynamicBone.length = direction.magnitude;
        virtualBones[selectedBoneIndex] = dynamicBone;

        var previewTransform = new BonePreviewTransform(
            bonesTreatAsWorldSpace,
            rotateLocalBoneOffsetsByParent);
        previewTransform.ResolveAll(virtualBones);

        for (var boneIndex = 0; boneIndex < virtualBones.Count; boneIndex++)
        {
            var child = virtualBones[boneIndex];
            if (child == null || child.parentId != selectedBoneIndex)
                continue;

            var sourceChild = bones[boneIndex];
            child.position = ConvertJointV2VirtualWorldPositionToLocal(
                virtualBones,
                child.parentId,
                sourceChild.worldPosition);
            child.rotation = ConvertJointV2VirtualWorldRotationToLocal(
                virtualBones,
                child.parentId,
                sourceChild.worldRotation);
            virtualBones[boneIndex] = child;
        }

        previewTransform.ResolveAll(virtualBones);
        return true;
    }

    private Vector2 ConvertJointV2VirtualWorldPositionToLocal(
        IReadOnlyList<BoneInfo> virtualBones,
        int parentIndex,
        Vector2 worldPosition)
    {
        if (bonesTreatAsWorldSpace || virtualBones == null ||
            parentIndex < 0 || parentIndex >= virtualBones.Count || virtualBones[parentIndex] == null)
        {
            return worldPosition;
        }

        var parent = virtualBones[parentIndex];
        var offset = new Vector3(
            worldPosition.x - parent.worldPosition.x,
            worldPosition.y - parent.worldPosition.y,
            0f);
        if (rotateLocalBoneOffsetsByParent)
            offset = Quaternion.Inverse(parent.worldRotation) * offset;
        return new Vector2(offset.x, offset.y);
    }

    private Quaternion ConvertJointV2VirtualWorldRotationToLocal(
        IReadOnlyList<BoneInfo> virtualBones,
        int parentIndex,
        Quaternion worldRotation)
    {
        if (bonesTreatAsWorldSpace || virtualBones == null ||
            parentIndex < 0 || parentIndex >= virtualBones.Count || virtualBones[parentIndex] == null)
        {
            return worldRotation;
        }

        return Quaternion.Inverse(virtualBones[parentIndex].worldRotation) * worldRotation;
    }

    private bool TryBuildJointV2PreviewGeometry(
        out LayerVertexData sourceData,
        out LayerVertexData previewData,
        out List<int> generatedVertexIndices,
        out int dynamicSpriteBone,
        out int staticSpriteBone,
        AnimationCurve weightCurve,
        AnimationCurve specialWeightCurve,
        float specialJointDistance)
    {
        sourceData = null;
        previewData = null;
        generatedVertexIndices = new List<int>();
        dynamicSpriteBone = -1;
        staticSpriteBone = -1;

        if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null ||
            !TryResolveJointV2CurrentBoneLayer(out sourceData) || sourceData == null ||
            sourceData.vertices == null || sourceData.vertices.Length < 3 ||
            sourceData.triangles == null || sourceData.triangles.Length < 3)
        {
            return false;
        }

        var selectedBone = bones[selectedBoneIndex];
        var parentRendererBone = selectedBone.parentId;
        if (parentRendererBone < 0 || parentRendererBone >= bones.Count)
            return false;

        var skinningTemplate = CloneProjectorLayerData(sourceData);
        if (skinningTemplate == null ||
            !TryEnsureRendererBoneMappedToLayerSkinning(
                skinningTemplate,
                selectedBoneIndex,
                out dynamicSpriteBone,
                false,
                "V2 dynamic") ||
            !TryEnsureRendererBoneMappedToLayerSkinning(
                skinningTemplate,
                parentRendererBone,
                out staticSpriteBone,
                false,
                "V2 static") ||
            dynamicSpriteBone == staticSpriteBone)
        {
            return false;
        }

        var displayedContourPoints = new List<Vector2>(16);
        if (!TryGetJointV2AttachedGuidePoints(
                specialJointDistance,
                out var displayedCenterPoint,
                out var displayedAnchorPointA,
                out var displayedAnchorPointB,
                out var isSpecialJoint,
                displayedContourPoints) ||
            displayedContourPoints.Count != 16)
        {
            return false;
        }
        var activeWeightCurve = ResolveJointV2WeightCurve(
            isSpecialJoint,
            weightCurve,
            specialWeightCurve);

        var currentRelativeAngle = ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
            selectedBone.initialRotation,
            selectedBone.rotation);
        var pivot = GetBoneBasePoint(selectedBone);
        var bindCenterPoint = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            displayedCenterPoint,
            pivot,
            -currentRelativeAngle);
        var whiteBindPointA = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            displayedAnchorPointA,
            pivot,
            -currentRelativeAngle);
        var whiteBindPointB = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            displayedAnchorPointB,
            pivot,
            -currentRelativeAngle);
        var bindContourPoints = new List<Vector2>(displayedContourPoints.Count);
        for (var pointIndex = 0; pointIndex < displayedContourPoints.Count; pointIndex++)
        {
            bindContourPoints.Add(ReanimatorJointGeometryAlgorithms.RotatePointAround(
                displayedContourPoints[pointIndex],
                pivot,
                -currentRelativeAngle));
        }

        var currentDrawVertices = GetLayerVerticesForDraw(sourceData);
        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(sourceData);
        if (currentDrawVertices == null || currentDrawVertices.Length != sourceData.vertices.Length ||
            boundaryPairs == null || boundaryPairs.Length < 6)
        {
            return false;
        }

        var normalizedVertices = new Vector2[currentDrawVertices.Length];
        var poseDeltaDegrees = ReanimatorJointGeometryAlgorithms.GetSamplePoseDeltaDegrees(currentRelativeAngle, 0f);
        for (var vertexIndex = 0; vertexIndex < currentDrawVertices.Length; vertexIndex++)
        {
            normalizedVertices[vertexIndex] = ReanimatorJointGeometryAlgorithms.RotatePointAround(
                currentDrawVertices[vertexIndex],
                pivot,
                poseDeltaDegrees);
        }

        if (!TryBuildBoundaryLoopFromPairs(boundaryPairs, normalizedVertices.Length, out var boundaryLoop) ||
            boundaryLoop == null || boundaryLoop.Count < 3)
        {
            return false;
        }

        var loopPoints = new List<Vector2>(boundaryLoop.Count);
        for (var loopIndex = 0; loopIndex < boundaryLoop.Count; loopIndex++)
            loopPoints.Add(normalizedVertices[boundaryLoop[loopIndex]]);
        if (!TryBuildContourPerimeterSamples(loopPoints, out var segmentLengths, out var perimeterLength))
            return false;

        var extremeBindPoints = new[]
        {
            bindContourPoints[0],
            bindContourPoints[4],
            bindContourPoints[8],
            bindContourPoints[12]
        };
        var extremeWhitePoints = new[]
        {
            whiteBindPointA,
            whiteBindPointA,
            whiteBindPointB,
            whiteBindPointB
        };
        var extremeDistances = new float[extremeBindPoints.Length];
        var whiteDistances = new float[extremeBindPoints.Length];
        for (var extremeIndex = 0; extremeIndex < extremeBindPoints.Length; extremeIndex++)
        {
            if (!TryProjectPointToContourPerimeter(
                    loopPoints,
                    segmentLengths,
                    perimeterLength,
                    extremeBindPoints[extremeIndex],
                    out extremeDistances[extremeIndex]) ||
                !TryProjectPointToContourPerimeter(
                    loopPoints,
                    segmentLengths,
                    perimeterLength,
                    extremeWhitePoints[extremeIndex],
                    out whiteDistances[extremeIndex]))
            {
                return false;
            }
        }

        var removalPolygon = new List<Vector2>(extremeBindPoints);
        removalPolygon.Sort((left, right) =>
        {
            var leftAngle = Mathf.Atan2(left.y - bindCenterPoint.y, left.x - bindCenterPoint.x);
            var rightAngle = Mathf.Atan2(right.y - bindCenterPoint.y, right.x - bindCenterPoint.x);
            return leftAngle.CompareTo(rightAngle);
        });

        var removeMask = new bool[sourceData.vertices.Length];
        var boundaryVertexMask = BuildContourVertexMask(boundaryPairs, sourceData.vertices.Length);
        var accumulatedDistance = 0f;
        for (var loopIndex = 0; loopIndex < boundaryLoop.Count; loopIndex++)
        {
            var sourceIndex = boundaryLoop[loopIndex];
            for (var extremeIndex = 0; extremeIndex < extremeBindPoints.Length; extremeIndex++)
            {
                if (!IsJointV2DistanceOnShortestArc(
                        accumulatedDistance,
                        whiteDistances[extremeIndex],
                        extremeDistances[extremeIndex],
                        perimeterLength))
                {
                    continue;
                }

                removeMask[sourceIndex] = true;
                break;
            }

            accumulatedDistance += segmentLengths[loopIndex];
        }

        for (var vertexIndex = 0; vertexIndex < normalizedVertices.Length; vertexIndex++)
        {
            if (boundaryVertexMask != null && vertexIndex < boundaryVertexMask.Length && boundaryVertexMask[vertexIndex])
                continue;
            if (ReanimatorQuadNetAlgorithms.IsPointInsideOrOnPolygon(normalizedVertices[vertexIndex], removalPolygon))
                removeMask[vertexIndex] = true;
        }

        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out var keptVertexCount);
        if (keptVertexCount < 3)
            return false;

        var outsideNeighborOutputIndices = new int[extremeDistances.Length];
        for (var extremeIndex = 0; extremeIndex < extremeDistances.Length; extremeIndex++)
        {
            outsideNeighborOutputIndices[extremeIndex] = FindClosestJointV2KeptBoundaryOutputIndex(
                boundaryLoop,
                segmentLengths,
                perimeterLength,
                extremeDistances[extremeIndex],
                removeMask,
                remap);
        }

        var vertices = new List<Vector2>(keptVertexCount + 19);
        var uvs = new List<Vector2>(keptVertexCount + 19);
        var weights = new List<BoneWeight>(keptVertexCount + 19);
        for (var sourceIndex = 0; sourceIndex < sourceData.vertices.Length; sourceIndex++)
        {
            if (removeMask[sourceIndex])
                continue;

            var sourcePoint = sourceData.vertices[sourceIndex];
            vertices.Add(sourcePoint);
            uvs.Add(sourceData.vertexUvs != null && sourceIndex < sourceData.vertexUvs.Length
                ? sourceData.vertexUvs[sourceIndex]
                : ComputeUvForPoint(sourceData.layer, sourcePoint));
            weights.Add(skinningTemplate.boneWeights != null && sourceIndex < skinningTemplate.boneWeights.Length
                ? skinningTemplate.boneWeights[sourceIndex]
                : default);
        }

        var newBoundaryBindPoints = new List<Vector2>(18);
        newBoundaryBindPoints.AddRange(bindContourPoints);
        newBoundaryBindPoints.Add(whiteBindPointA);
        newBoundaryBindPoints.Add(whiteBindPointB);
        var newBoundaryIndices = new List<int>(newBoundaryBindPoints.Count);
        for (var pointIndex = 0; pointIndex < newBoundaryBindPoints.Count; pointIndex++)
        {
            var basePoint = MapJointV2NormalizedPointToBase(
                normalizedVertices,
                sourceData,
                newBoundaryBindPoints[pointIndex]);
            newBoundaryIndices.Add(vertices.Count);
            generatedVertexIndices.Add(vertices.Count);
            vertices.Add(basePoint);
            uvs.Add(ComputeUvForPoint(sourceData.layer, basePoint));
            weights.Add(default);
        }

        var centerBasePoint = MapJointV2NormalizedPointToBase(normalizedVertices, sourceData, bindCenterPoint);
        var centerVertexIndex = vertices.Count;
        generatedVertexIndices.Add(centerVertexIndex);
        vertices.Add(centerBasePoint);
        uvs.Add(ComputeUvForPoint(sourceData.layer, centerBasePoint));
        weights.Add(default);

        var normalizedBoneEnd = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            GetBoneEnd(selectedBoneIndex),
            pivot,
            -currentRelativeAngle);
        var weightAxis = normalizedBoneEnd - pivot;
        if (weightAxis.sqrMagnitude <= 1e-8f)
            weightAxis = Vector2.up;
        weightAxis.Normalize();

        var generatedBindPoints = new List<Vector2>(newBoundaryBindPoints.Count + 1);
        generatedBindPoints.AddRange(newBoundaryBindPoints);
        generatedBindPoints.Add(bindCenterPoint);
        var generatedOutputIndices = new List<int>(newBoundaryIndices.Count + 1);
        generatedOutputIndices.AddRange(newBoundaryIndices);
        generatedOutputIndices.Add(centerVertexIndex);
        var generatedDynamicWeights = new float[generatedBindPoints.Count];
        var outsideDynamicWeights = new float[extremeDistances.Length];
        AssignJointV2ChainWeights(
            generatedBindPoints,
            generatedDynamicWeights,
            weightAxis,
            0,
            4,
            16,
            activeWeightCurve,
            out outsideDynamicWeights[0],
            out outsideDynamicWeights[1]);
        AssignJointV2ChainWeights(
            generatedBindPoints,
            generatedDynamicWeights,
            weightAxis,
            8,
            12,
            17,
            activeWeightCurve,
            out outsideDynamicWeights[2],
            out outsideDynamicWeights[3]);
        generatedDynamicWeights[generatedDynamicWeights.Length - 1] = 0.5f;

        for (var generatedIndex = 0; generatedIndex < generatedBindPoints.Count; generatedIndex++)
        {
            var dynamicWeight = generatedDynamicWeights[generatedIndex];
            weights[generatedOutputIndices[generatedIndex]] = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(
                staticSpriteBone,
                1f - dynamicWeight,
                dynamicSpriteBone,
                dynamicWeight);
        }
        for (var extremeIndex = 0; extremeIndex < outsideNeighborOutputIndices.Length; extremeIndex++)
        {
            var outputIndex = outsideNeighborOutputIndices[extremeIndex];
            if (outputIndex < 0 || outputIndex >= weights.Count)
                continue;

            var dynamicWeight = outsideDynamicWeights[extremeIndex];
            weights[outputIndex] = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(
                staticSpriteBone,
                1f - dynamicWeight,
                dynamicSpriteBone,
                dynamicWeight);
        }

        var boundaryEntries = new List<JointV2BoundaryEntry>(boundaryLoop.Count + newBoundaryBindPoints.Count);
        accumulatedDistance = 0f;
        for (var loopIndex = 0; loopIndex < boundaryLoop.Count; loopIndex++)
        {
            var sourceIndex = boundaryLoop[loopIndex];
            if (!removeMask[sourceIndex] && remap[sourceIndex] >= 0)
                boundaryEntries.Add(new JointV2BoundaryEntry(accumulatedDistance, remap[sourceIndex]));
            accumulatedDistance += segmentLengths[loopIndex];
        }
        for (var pointIndex = 0; pointIndex < newBoundaryBindPoints.Count; pointIndex++)
        {
            if (!TryProjectPointToContourPerimeter(
                    loopPoints,
                    segmentLengths,
                    perimeterLength,
                    newBoundaryBindPoints[pointIndex],
                    out var distance))
            {
                continue;
            }
            boundaryEntries.Add(new JointV2BoundaryEntry(distance, newBoundaryIndices[pointIndex]));
        }
        boundaryEntries.Sort((left, right) => left.distance.CompareTo(right.distance));

        var newBoundaryLoop = new List<int>(boundaryEntries.Count);
        for (var entryIndex = 0; entryIndex < boundaryEntries.Count; entryIndex++)
        {
            var vertexIndex = boundaryEntries[entryIndex].vertexIndex;
            if (newBoundaryLoop.Count == 0 || newBoundaryLoop[newBoundaryLoop.Count - 1] != vertexIndex)
                newBoundaryLoop.Add(vertexIndex);
        }
        if (newBoundaryLoop.Count < 3)
            return false;

        previewData = new LayerVertexData
        {
            layer = sourceData.layer,
            texture = sourceData.texture,
            pixelsPerUnit = sourceData.pixelsPerUnit,
            pivotPixels = sourceData.pivotPixels,
            vertices = vertices.ToArray(),
            vertexUvs = uvs.ToArray(),
            triangles = Array.Empty<int>(),
            allEdgePairs = Array.Empty<int>(),
            boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop),
            boneWeights = weights.ToArray(),
            spriteBoneToRendererBone = CloneArray(skinningTemplate.spriteBoneToRendererBone) ?? Array.Empty<int>(),
            bindBoneInverseMatrices = CloneArray(skinningTemplate.bindBoneInverseMatrices) ?? Array.Empty<Matrix4x4>(),
            usesSpriteBindPoses = skinningTemplate.usesSpriteBindPoses,
            hasSkinning = false,
            lastPoseVersion = -1,
            deformedVertices = null,
            skinMatrices = null
        };

        if (!TryRetriangulateKeepingVertexSet(previewData) && !TryRetriangulateWithDirectFallback(previewData))
            return false;
        BuildEdgePairs(
            previewData.triangles,
            previewData.vertices.Length,
            out previewData.allEdgePairs,
            out var rebuiltBoundaryPairs);
        if (previewData.boundaryEdgePairs == null || previewData.boundaryEdgePairs.Length < 6)
            previewData.boundaryEdgePairs = rebuiltBoundaryPairs;
        previewData.hasSkinning = previewData.spriteBoneToRendererBone.Length > 0 &&
                                  previewData.bindBoneInverseMatrices.Length == previewData.spriteBoneToRendererBone.Length &&
                                  previewData.boneWeights.Length == previewData.vertices.Length;
        return previewData.triangles != null && previewData.triangles.Length >= 3;
    }

    private static AnimationCurve ResolveJointV2WeightCurve(
        bool isSpecialJoint,
        AnimationCurve regularWeightCurve,
        AnimationCurve specialWeightCurve)
    {
        return isSpecialJoint && specialWeightCurve != null && specialWeightCurve.length > 0
            ? specialWeightCurve
            : regularWeightCurve;
    }

    private static void AssignJointV2ChainWeights(
        IReadOnlyList<Vector2> bindPoints,
        float[] dynamicWeights,
        Vector2 weightAxis,
        int firstHalfStart,
        int secondHalfStart,
        int whitePointIndex,
        AnimationCurve weightCurve,
        out float firstOutsideWeight,
        out float secondOutsideWeight)
    {
        firstOutsideWeight = 0f;
        secondOutsideWeight = 1f;
        if (bindPoints == null || dynamicWeights == null || bindPoints.Count != dynamicWeights.Length ||
            firstHalfStart < 0 || firstHalfStart + 3 >= bindPoints.Count ||
            secondHalfStart < 0 || secondHalfStart + 3 >= bindPoints.Count ||
            whitePointIndex < 0 || whitePointIndex >= bindPoints.Count)
        {
            return;
        }

        var firstProjection = Vector2.Dot(bindPoints[firstHalfStart], weightAxis);
        var secondProjection = Vector2.Dot(bindPoints[secondHalfStart], weightAxis);
        var firstIsStaticSide = firstProjection <= secondProjection;
        var firstExtremePosition = firstIsStaticSide ? 0.1f : 0.9f;
        var secondExtremePosition = firstIsStaticSide ? 0.9f : 0.1f;
        firstOutsideWeight = ReanimatorJointGeometryAlgorithms.EvaluateJointV2WeightCurve(
            weightCurve,
            firstIsStaticSide ? 0f : 1f);
        secondOutsideWeight = ReanimatorJointGeometryAlgorithms.EvaluateJointV2WeightCurve(
            weightCurve,
            firstIsStaticSide ? 1f : 0f);
        AssignJointV2HalfChainWeights(dynamicWeights, firstHalfStart, firstExtremePosition, weightCurve);
        AssignJointV2HalfChainWeights(dynamicWeights, secondHalfStart, secondExtremePosition, weightCurve);
        dynamicWeights[whitePointIndex] =
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2WeightCurve(weightCurve, 0.5f);
    }

    private static void AssignJointV2HalfChainWeights(
        float[] dynamicWeights,
        int halfStart,
        float outerPosition,
        AnimationCurve weightCurve)
    {
        dynamicWeights[halfStart] =
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2WeightCurve(weightCurve, outerPosition);
        for (var subdivisionIndex = 1; subdivisionIndex < 4; subdivisionIndex++)
        {
            var normalizedPosition = Mathf.Lerp(0.5f, outerPosition, subdivisionIndex / 4f);
            dynamicWeights[halfStart + subdivisionIndex] =
                ReanimatorJointGeometryAlgorithms.EvaluateJointV2WeightCurve(
                    weightCurve,
                    normalizedPosition);
        }
    }

    private static int FindClosestJointV2KeptBoundaryOutputIndex(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        float extremeDistance,
        IReadOnlyList<bool> removeMask,
        IReadOnlyList<int> remap)
    {
        if (boundaryLoop == null || segmentLengths == null || removeMask == null || remap == null ||
            boundaryLoop.Count == 0 || segmentLengths.Count != boundaryLoop.Count || perimeterLength <= 1e-6f)
        {
            return -1;
        }

        var closestOutputIndex = -1;
        var closestDistance = float.PositiveInfinity;
        var accumulatedDistance = 0f;
        for (var loopIndex = 0; loopIndex < boundaryLoop.Count; loopIndex++)
        {
            var sourceIndex = boundaryLoop[loopIndex];
            if (sourceIndex >= 0 && sourceIndex < removeMask.Count && sourceIndex < remap.Count &&
                !removeMask[sourceIndex] && remap[sourceIndex] >= 0)
            {
                var forwardDistance = Mathf.Repeat(accumulatedDistance - extremeDistance, perimeterLength);
                var circularDistance = Mathf.Min(forwardDistance, perimeterLength - forwardDistance);
                if (circularDistance < closestDistance)
                {
                    closestDistance = circularDistance;
                    closestOutputIndex = remap[sourceIndex];
                }
            }

            accumulatedDistance += segmentLengths[loopIndex];
        }

        return closestOutputIndex;
    }

    private static bool IsJointV2DistanceOnShortestArc(
        float candidateDistance,
        float startDistance,
        float endDistance,
        float perimeterLength)
    {
        var forwardLength = Mathf.Repeat(endDistance - startDistance, perimeterLength);
        var backwardLength = perimeterLength - forwardLength;
        var forward = forwardLength <= backwardLength;
        var arcLength = forward ? forwardLength : backwardLength;
        return ReanimatorJointGeometryAlgorithms.IsPerimeterDistanceOnArc(
            candidateDistance,
            startDistance,
            arcLength,
            perimeterLength,
            forward,
            0.001f);
    }

    private static Vector2 MapJointV2NormalizedPointToBase(
        Vector2[] normalizedVertices,
        LayerVertexData sourceData,
        Vector2 point)
    {
        if (sourceData != null &&
            sourceData.vertices != null &&
            sourceData.triangles != null &&
            PsdCoordinateSpace.TryMapPointBetweenMeshes(
                normalizedVertices,
                sourceData.vertices,
                sourceData.triangles,
                point,
                out var mapped))
        {
            return mapped;
        }

        return point;
    }
}
#endif
