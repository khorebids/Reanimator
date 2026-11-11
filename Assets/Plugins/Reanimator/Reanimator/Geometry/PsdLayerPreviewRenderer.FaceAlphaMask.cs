#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    [NonSerialized] private Texture2D faceAlphaAnalysisMask;
    [NonSerialized] private string faceAlphaAnalysisLayerKey = string.Empty;
    [NonSerialized] private float faceAlphaAnalysisThreshold = -1f;
    [NonSerialized] private ReanimatorFaceAlphaMaskAnalysis faceAlphaAnalysis;

    internal bool TryAnalyzeSelectedFaceAlphaMask(
        float alphaThreshold,
        out ReanimatorFaceAlphaMaskAnalysis analysis)
    {
        analysis = null;
        if (!TryGetSelectedLayer(out var layer) || layer == null)
            return false;

        return TryAnalyzeFaceAlphaMask(layer, alphaThreshold, out analysis);
    }

    private bool TryAnalyzeFaceAlphaMask(
        LayerInfo layer,
        float alphaThreshold,
        out ReanimatorFaceAlphaMaskAnalysis analysis)
    {
        analysis = null;
        if (layer == null)
            return false;

        var threshold = Mathf.Clamp01(alphaThreshold);
        var mask = GetReanimaxHardMaskPreviewTexture(layer, threshold);
        if (mask == null)
            return false;

        var layerKey = BuildSelectionKey(layer);
        if (faceAlphaAnalysis != null &&
            faceAlphaAnalysisMask == mask &&
            string.Equals(faceAlphaAnalysisLayerKey, layerKey, StringComparison.Ordinal) &&
            Mathf.Abs(faceAlphaAnalysisThreshold - threshold) <= 0.0001f)
        {
            analysis = faceAlphaAnalysis;
            return true;
        }

        faceAlphaAnalysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(mask.GetPixels32(), mask.width, mask.height);
        faceAlphaAnalysisMask = mask;
        faceAlphaAnalysisLayerKey = layerKey;
        faceAlphaAnalysisThreshold = threshold;
        analysis = faceAlphaAnalysis;
        return analysis != null;
    }

    internal bool TryApplyFaceEyesAutomation(
        float pointDensity,
        float pointOffset,
        float outlineOffset,
        AnimationCurve weightCurve,
        out string result,
        out string error)
    {
        const float alphaThreshold = 0.5f;
        const string headBoneName = "head";
        const string eyelidsBoneName = "eyelids";
        const float eyelidsLengthRatio = 1f / 6f;
        result = string.Empty;
        error = string.Empty;

        if (!TryFindBoneIndexByName(headBoneName, out var headBoneIndex) ||
            bones == null || headBoneIndex < 0 || headBoneIndex >= bones.Count || bones[headBoneIndex] == null)
        {
            error = "Bone 'head' is required for Face Eyes automation.";
            return false;
        }

        if (!TryFindFaceEyesAutomationLayer(alphaThreshold, out var layer, out var analysis))
        {
            error = "Could not find an editable face sprite with two eye holes.";
            return false;
        }

        SelectLayer(layer);
        var createdEyelidsBone = false;
        if (!TryFindBoneIndexByName(eyelidsBoneName, out _))
        {
            var headBone = bones[headBoneIndex];
            var eyeMidpointMask = (analysis.LeftHole.Center + analysis.RightHole.Center) * 0.5f;
            var eyeMidpointDocument = FaceMaskPointToBaseDocument(layer, analysis, eyeMidpointMask);
            var eyeMidpointOffset = eyeMidpointDocument - headBone.worldPosition;
            if (!TryCreateNamedChildBone(
                    eyelidsBoneName,
                    headBoneIndex,
                    Mathf.Max(0.01f, headBone.length * eyelidsLengthRatio),
                    eyeMidpointOffset,
                    out _))
            {
                error = "Bone 'eyelids' could not be created automatically.";
                return false;
            }
            createdEyelidsBone = true;
        }

        if (!TryGetFaceBoneDirectionInMask(headBoneName, layer, analysis, out var headDirection))
        {
            error = "Bone 'head' direction could not be resolved for Face Eyes automation.";
            return false;
        }

        TryGetFaceGeometryBoundaryInMask(
            layer,
            analysis,
            out var geometryBoundaryVertices,
            out var geometryBoundaryEdgePairs);
        var curve = weightCurve != null && weightCurve.length > 0
            ? weightCurve
            : AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var density = Mathf.Clamp(pointDensity, 0.1f, 1f);
        var clampedPointOffset = Mathf.Clamp(pointOffset, 0f, 32f);
        var clampedOutlineOffset = Mathf.Clamp(outlineOffset, 0f, 32f);
        var leftEye = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            analysis.LeftHole,
            density,
            headDirection,
            clampedPointOffset,
            clampedOutlineOffset,
            geometryBoundaryVertices,
            geometryBoundaryEdgePairs,
            curve);
        var rightEye = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            analysis.RightHole,
            density,
            headDirection,
            clampedPointOffset,
            clampedOutlineOffset,
            geometryBoundaryVertices,
            geometryBoundaryEdgePairs,
            curve);
        ReanimatorFaceAlphaMaskAlgorithms.ApplySharedEyeOutlineCollisionStop(
            clampedOutlineOffset,
            leftEye,
            rightEye);

        if (!ApplyFaceEyesGeometry(
                analysis,
                leftEye,
                rightEye,
                out var addedVertexCount,
                out var removedVertexCount,
                out var weightedVertexCount,
                out error))
        {
            return false;
        }

        result = $"{layer.name}: {removedVertexCount} removed, {addedVertexCount} added, " +
                 $"{weightedVertexCount} weighted" + (createdEyelidsBone ? ", eyelids bone created" : string.Empty);
        return true;
    }

    private bool TryFindFaceEyesAutomationLayer(
        float alphaThreshold,
        out LayerInfo selectedLayer,
        out ReanimatorFaceAlphaMaskAnalysis selectedAnalysis)
    {
        selectedLayer = null;
        selectedAnalysis = null;
        if (layerInfos == null || layerInfos.Count == 0)
            return false;

        TryGetSelectedLayer(out var currentLayer);
        var bestPriority = int.MinValue;
        var bestArea = -1;
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var candidate = layerInfos[i];
            if (candidate == null ||
                !TryGetLayerVertexData(candidate, out var data) || data == null ||
                data.vertices == null || data.vertices.Length < 3 ||
                !TryAnalyzeFaceAlphaMask(candidate, alphaThreshold, out var candidateAnalysis) ||
                candidateAnalysis == null || !candidateAnalysis.HasContour ||
                !candidateAnalysis.HasLeftHole || !candidateAnalysis.HasRightHole)
            {
                continue;
            }

            var normalizedName = NormalizeFaceEyesLayerName(candidate.name);
            var priority = normalizedName == "head"
                ? 3
                : normalizedName == "face" ? 2
                : ReferenceEquals(candidate, currentLayer) ? 1 : 0;
            var area = candidateAnalysis.LeftHole.Area + candidateAnalysis.RightHole.Area;
            if (priority < bestPriority || (priority == bestPriority && area <= bestArea))
                continue;

            bestPriority = priority;
            bestArea = area;
            selectedLayer = candidate;
            selectedAnalysis = candidateAnalysis;
        }

        return selectedLayer != null && selectedAnalysis != null;
    }

    private static string NormalizeFaceEyesLayerName(string layerName)
    {
        return (layerName ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    internal bool TryGetFaceBoneDirectionInMask(
        string boneName,
        LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        out Vector2 direction)
    {
        direction = Vector2.zero;
        if (layer == null || analysis == null ||
            string.IsNullOrWhiteSpace(boneName) ||
            !TryFindBoneIndexByName(boneName, out var boneIndex) ||
            boneIndex < 0 || boneIndex >= bones.Count || bones[boneIndex] == null)
        {
            return false;
        }

        var bone = bones[boneIndex];
        var rotation = bone.worldRotation.Equals(default(Quaternion)) ? bone.rotation : bone.worldRotation;
        if (rotation.Equals(default(Quaternion)))
            rotation = Quaternion.identity;
        var rotated = rotation * Vector3.right;
        var documentDirection = new Vector2(rotated.x, rotated.y);
        var scaleX = layer.sourceRect.width / Mathf.Max(1f, analysis.Width);
        var scaleY = layer.sourceRect.height / Mathf.Max(1f, analysis.Height);
        if (Mathf.Abs(scaleX) <= 0.0001f || Mathf.Abs(scaleY) <= 0.0001f)
            return false;

        direction = new Vector2(documentDirection.x / scaleX, documentDirection.y / scaleY);
        if (direction.sqrMagnitude <= 0.0001f)
            return false;
        direction.Normalize();
        return true;
    }

    internal bool TryGetFaceGeometryBoundaryInMask(
        LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        out Vector2[] vertices,
        out int[] boundaryEdgePairs)
    {
        vertices = Array.Empty<Vector2>();
        boundaryEdgePairs = Array.Empty<int>();
        if (layer == null || analysis == null ||
            !TryGetLayerVertexData(layer, out var data) || data == null)
        {
            return false;
        }

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length < 3)
            return false;
        ResolvePreviewEdgePairs(data, drawVertices.Length, out _, out var resolvedBoundaryPairs, out _);
        if (resolvedBoundaryPairs == null || resolvedBoundaryPairs.Length < 6)
            return false;

        var scaleX = analysis.Width / Mathf.Max(1f, layer.sourceRect.width);
        var scaleY = analysis.Height / Mathf.Max(1f, layer.sourceRect.height);
        vertices = new Vector2[drawVertices.Length];
        for (var i = 0; i < drawVertices.Length; i++)
        {
            var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, drawVertices[i]);
            vertices[i] = new Vector2(local.x * scaleX, local.y * scaleY);
        }
        boundaryEdgePairs = resolvedBoundaryPairs;
        return true;
    }

    internal bool TryGetFaceVirtualBoneSkinMatrix(
        string boneName,
        out Matrix4x4 skinMatrix)
    {
        skinMatrix = Matrix4x4.identity;
        if (string.IsNullOrWhiteSpace(boneName) ||
            !TryFindBoneIndexByName(boneName, out var boneIndex) ||
            bones == null ||
            boneIndex < 0 ||
            boneIndex >= bones.Count ||
            bones[boneIndex] == null)
        {
            return false;
        }

        var restBones = new List<BoneInfo>(bones.Count);
        for (var i = 0; i < bones.Count; i++)
        {
            var source = bones[i];
            if (source == null)
            {
                restBones.Add(new BoneInfo
                {
                    index = i,
                    parentId = -1,
                    position = Vector2.zero,
                    rotation = Quaternion.identity
                });
                continue;
            }

            restBones.Add(new BoneInfo
            {
                index = source.index,
                name = source.name,
                parentId = source.parentId,
                position = source.initialPosition,
                rotation = source.initialRotation.Equals(default(Quaternion))
                    ? Quaternion.identity
                    : source.initialRotation
            });
        }

        var restTransform = new BonePreviewTransform(
            bonesTreatAsWorldSpace,
            rotateLocalBoneOffsetsByParent);
        restTransform.ResolveAll(restBones);
        var restBone = restBones[boneIndex];
        var currentBone = bones[boneIndex];
        skinMatrix = ReanimatorFaceAlphaMaskAlgorithms.BuildVirtualBoneSkinMatrix(
            restBone.worldPosition,
            restBone.worldRotation,
            currentBone.worldPosition,
            currentBone.worldRotation);
        return true;
    }

    internal bool ApplyFaceEyesGeometry(
        ReanimatorFaceAlphaMaskAnalysis analysis,
        ReanimatorFaceEyePointExpansion leftEye,
        ReanimatorFaceEyePointExpansion rightEye,
        out int addedVertexCount,
        out int removedVertexCount,
        out int weightedVertexCount,
        out string error)
    {
        const string headBoneName = "head";
        const string eyelidsBoneName = "eyelids";
        addedVertexCount = 0;
        removedVertexCount = 0;
        weightedVertexCount = 0;
        error = string.Empty;
        if (analysis == null || !TryGetSelectedLayer(out var layer) || layer == null)
        {
            error = "Sprite is not selected.";
            return false;
        }
        if (!TryGetLayerVertexData(layer, out var data) || data == null || data.vertices == null || data.vertices.Length < 3)
        {
            error = "Selected sprite has no editable geometry.";
            return false;
        }
        if (!TryFindBoneIndexByName(headBoneName, out var headRendererBone) ||
            !TryFindBoneIndexByName(eyelidsBoneName, out var eyelidsRendererBone))
        {
            error = "Bones 'head' and 'eyelids' are required.";
            return false;
        }

        var leftPoints = new List<KeyValuePair<Vector2, float>>();
        var rightPoints = new List<KeyValuePair<Vector2, float>>();
        CollectFaceEyeApplyPoints(leftEye, leftPoints);
        CollectFaceEyeApplyPoints(rightEye, rightPoints);
        var points = new List<KeyValuePair<Vector2, float>>(leftPoints.Count + rightPoints.Count);
        points.AddRange(leftPoints);
        points.AddRange(rightPoints);
        if (points.Count == 0)
        {
            error = "No detected eye points to apply.";
            return false;
        }
        var previousVertices = CloneArray(data.vertices);
        var previousUvs = CloneArray(data.vertexUvs);
        var previousWeights = CloneArray(data.boneWeights);
        var previousTriangles = CloneArray(data.triangles);
        var previousAllEdges = CloneArray(data.allEdgePairs);
        var previousBoundaryEdges = CloneArray(data.boundaryEdgePairs);
        var previousBoneMap = CloneArray(data.spriteBoneToRendererBone);
        var previousBindMatrices = CloneArray(data.bindBoneInverseMatrices);
        var previousDeformedVertices = CloneArray(data.deformedVertices);
        var previousSkinMatrices = CloneArray(data.skinMatrices);
        var previousUsesSpriteBindPoses = data.usesSpriteBindPoses;
        var previousHasSkinning = data.hasSkinning;
        var previousLastPoseVersion = data.lastPoseVersion;

        void RestorePreviousState()
        {
            data.vertices = previousVertices;
            data.vertexUvs = previousUvs;
            data.boneWeights = previousWeights;
            data.triangles = previousTriangles;
            data.allEdgePairs = previousAllEdges;
            data.boundaryEdgePairs = previousBoundaryEdges;
            data.spriteBoneToRendererBone = previousBoneMap;
            data.bindBoneInverseMatrices = previousBindMatrices;
            data.deformedVertices = previousDeformedVertices;
            data.skinMatrices = previousSkinMatrices;
            data.usesSpriteBindPoses = previousUsesSpriteBindPoses;
            data.hasSkinning = previousHasSkinning;
            data.lastPoseVersion = previousLastPoseVersion;
        }

        RegisterUndo("Apply Face Eyes");
        var rendererToSprite = BuildRendererToSpriteBoneMap(data.spriteBoneToRendererBone);
        if (!rendererToSprite.TryGetValue(headRendererBone, out var headSpriteBone) &&
            !TryAppendSkinningTargetBone(data, headRendererBone, out headSpriteBone))
        {
            RestorePreviousState();
            removedVertexCount = 0;
            error = "Bone 'head' could not be added to the selected sprite skinning.";
            return false;
        }

        rendererToSprite = BuildRendererToSpriteBoneMap(data.spriteBoneToRendererBone);
        if (!rendererToSprite.TryGetValue(eyelidsRendererBone, out var eyelidsSpriteBone) &&
            !TryAppendSkinningTargetBone(data, eyelidsRendererBone, out eyelidsSpriteBone))
        {
            RestorePreviousState();
            removedVertexCount = 0;
            error = "Bone 'eyelids' could not be added to the selected sprite skinning.";
            return false;
        }

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        // The imported head is intentionally coarse. If the dense eye rings are
        // inserted directly into one of its large triangles, every ring vertex
        // becomes part of the local convex hull and produces a destructive fan
        // to remote head vertices. Seed a small head-weighted cage around both
        // eyes first; all eyelid detail is then triangulated inside that cage.
        var supportCage = BuildFaceEyeSupportCage(layer, analysis, leftEye, rightEye);
        for (var supportIndex = 0; supportIndex < supportCage.Count; supportIndex++)
        {
            var supportPoint = supportCage[supportIndex];
            var supportVertex = FindFaceEyeVertexAt(data.vertices, supportPoint);
            if (supportVertex < 0)
            {
                var oldCount = data.vertices.Length;
                if (!InsertAppliedVertexIntoLayerData(data, supportPoint, true))
                {
                    RestorePreviousState();
                    addedVertexCount = 0;
                    error = "Failed to insert an eye support point into geometry.";
                    return false;
                }
                supportVertex = oldCount;
                addedVertexCount++;
            }

            EnsureBoneWeightsArraySize(data, data.vertices.Length);
            data.boneWeights[supportVertex] = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(
                headSpriteBone,
                1f,
                eyelidsSpriteBone,
                0f);
        }

        var affectedVertices = new HashSet<int>();
        for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var request = points[pointIndex];
            var basePoint = FaceMaskPointToBaseDocument(layer, analysis, request.Key);
            var vertexIndex = FindFaceEyeVertexAt(data.vertices, basePoint);
            if (vertexIndex < 0)
            {
                var oldCount = data.vertices.Length;
                if (!InsertAppliedVertexIntoLayerData(data, basePoint, true))
                {
                    RestorePreviousState();
                    addedVertexCount = 0;
                    removedVertexCount = 0;
                    error = "Failed to insert an eye point into geometry.";
                    return false;
                }
                vertexIndex = oldCount;
                addedVertexCount++;
            }

            EnsureBoneWeightsArraySize(data, data.vertices.Length);
            var eyelidsWeight = Mathf.Clamp01(request.Value);
            data.boneWeights[vertexIndex] = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(
                headSpriteBone,
                1f - eyelidsWeight,
                eyelidsSpriteBone,
                eyelidsWeight);
            affectedVertices.Add(vertexIndex);
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles.Length < 3)
        {
            RestorePreviousState();
            addedVertexCount = 0;
            removedVertexCount = 0;
            error = "Eye geometry produced no valid triangles.";
            return false;
        }
        OptimizeLayerTriangleQuality(data);
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        for (int i = 0; i < data.boneWeights.Length; i++)
        {
            var weight = data.boneWeights[i];
            if (weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3 <= 0.0001f)
                data.boneWeights[i] = new BoneWeight { boneIndex0 = headSpriteBone, weight0 = 1f };
        }
        MarkSkinningWeightsChanged(data);
        var selectionKey = BuildSelectionKey(layer);
        var stableKey = BuildKey(layer.name, layer.layerId);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        weightedVertexCount = affectedVertices.Count;
        return true;
    }

    private static List<Vector2> BuildFaceEyeSupportCage(
        LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        ReanimatorFaceEyePointExpansion leftEye,
        ReanimatorFaceEyePointExpansion rightEye)
    {
        var result = new List<Vector2>(12);
        if (layer == null || analysis == null || leftEye == null || rightEye == null)
            return result;

        var hasPoint = false;
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var expansions = new[] { leftEye, rightEye };
        for (var eyeIndex = 0; eyeIndex < expansions.Length; eyeIndex++)
        {
            var contour = expansions[eyeIndex].OutlineContour;
            if (contour == null)
                continue;
            for (var i = 0; i < contour.Length; i++)
            {
                var point = FaceMaskPointToBaseDocument(layer, analysis, contour[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
                hasPoint = true;
            }
        }
        if (!hasPoint)
            return result;

        var size = max - min;
        var margin = new Vector2(
            Mathf.Max(4f, size.x * 0.08f),
            Mathf.Max(4f, size.y * 0.35f));
        min -= margin;
        max += margin;
        var x1 = Mathf.Lerp(min.x, max.x, 1f / 3f);
        var x2 = Mathf.Lerp(min.x, max.x, 2f / 3f);
        var y1 = Mathf.Lerp(min.y, max.y, 1f / 3f);
        var y2 = Mathf.Lerp(min.y, max.y, 2f / 3f);

        result.Add(new Vector2(min.x, min.y));
        result.Add(new Vector2(x1, min.y));
        result.Add(new Vector2(x2, min.y));
        result.Add(new Vector2(max.x, min.y));
        result.Add(new Vector2(max.x, y1));
        result.Add(new Vector2(max.x, y2));
        result.Add(new Vector2(max.x, max.y));
        result.Add(new Vector2(x2, max.y));
        result.Add(new Vector2(x1, max.y));
        result.Add(new Vector2(min.x, max.y));
        result.Add(new Vector2(min.x, y2));
        result.Add(new Vector2(min.x, y1));
        return result;
    }

    private static int ApplyFaceEyeWeightsToImportedGeometry(
        LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        ReanimatorFaceEyePointExpansion leftEye,
        ReanimatorFaceEyePointExpansion rightEye,
        LayerVertexData data,
        int headSpriteBone,
        int eyelidsSpriteBone)
    {
        if (layer == null || analysis == null || data == null ||
            data.vertices == null || data.vertices.Length == 0 ||
            data.boneWeights == null || data.boneWeights.Length != data.vertices.Length)
        {
            return 0;
        }

        var expansions = new[] { leftEye, rightEye };
        var requestedWeights = new Dictionary<int, float>();
        for (var eyeIndex = 0; eyeIndex < expansions.Length; eyeIndex++)
        {
            var expansion = expansions[eyeIndex];
            if (expansion == null)
                continue;

            var center = FaceMaskPointToBaseDocument(layer, analysis, expansion.EyeCenter);
            var radius = 0f;
            if (expansion.OutlineContour != null)
            {
                for (var i = 0; i < expansion.OutlineContour.Length; i++)
                {
                    var outlinePoint = FaceMaskPointToBaseDocument(
                        layer,
                        analysis,
                        expansion.OutlineContour[i]);
                    radius = Mathf.Max(radius, Vector2.Distance(center, outlinePoint));
                }
            }
            radius = Mathf.Max(1f, radius);

            var affectedThisEye = false;
            var nearestVertex = -1;
            var nearestDistance = float.PositiveInfinity;
            for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
            {
                var distance = Vector2.Distance(data.vertices[vertexIndex], center);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestVertex = vertexIndex;
                }

                var normalizedDistance = distance / radius;
                if (normalizedDistance > 1.25f)
                    continue;

                var weight = Mathf.Clamp01(1f - normalizedDistance / 1.25f);
                if (weight <= 0.0001f)
                    continue;
                if (!requestedWeights.TryGetValue(vertexIndex, out var current) || weight > current)
                    requestedWeights[vertexIndex] = weight;
                affectedThisEye = true;
            }

            // Coarse imported meshes may not have a vertex inside the alpha
            // eye outline. Still attach the nearest support vertex so each eye
            // gets a functional eyelids influence without changing topology.
            if (!affectedThisEye && nearestVertex >= 0)
            {
                if (!requestedWeights.TryGetValue(nearestVertex, out var current) || current < 1f)
                    requestedWeights[nearestVertex] = 1f;
            }
        }

        foreach (var pair in requestedWeights)
        {
            data.boneWeights[pair.Key] = ReanimatorLimbElbowAlgorithms.BuildLimbTwoBoneWeight(
                headSpriteBone,
                1f - pair.Value,
                eyelidsSpriteBone,
                pair.Value);
        }
        return requestedWeights.Count;
    }

    private static List<int> BuildUniqueFaceEyeVertexList(
        IReadOnlyList<int> pointVertexIndices,
        int start,
        int count)
    {
        var result = new List<int>();
        if (pointVertexIndices == null || count <= 0)
            return result;

        var seen = new HashSet<int>();
        var end = Mathf.Min(pointVertexIndices.Count, start + count);
        for (var i = Mathf.Max(0, start); i < end; i++)
        {
            var vertexIndex = pointVertexIndices[i];
            if (vertexIndex >= 0 && seen.Add(vertexIndex))
                result.Add(vertexIndex);
        }
        return result;
    }

    private static bool TryAppendFaceEyeOutlineConstraints(
        LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        ReanimatorFaceEyePointExpansion expansion,
        IReadOnlyList<Vector2> vertices,
        ICollection<Vector2Int> constraints,
        ICollection<int> outlineVertices)
    {
        if (layer == null || analysis == null || expansion == null ||
            expansion.OutlineContour == null || expansion.OutlineContour.Length < 3 ||
            vertices == null || constraints == null || outlineVertices == null)
        {
            return false;
        }

        var ring = new int[expansion.OutlineContour.Length];
        for (var i = 0; i < ring.Length; i++)
        {
            var documentPoint = FaceMaskPointToBaseDocument(
                layer,
                analysis,
                expansion.OutlineContour[i]);
            ring[i] = FindFaceEyeVertexAt(vertices, documentPoint);
            if (ring[i] < 0)
                return false;
            outlineVertices.Add(ring[i]);
        }

        for (var i = 0; i < ring.Length; i++)
        {
            var next = (i + 1) % ring.Length;
            if (ring[i] == ring[next])
                continue;
            constraints.Add(new Vector2Int(ring[i], ring[next]));
        }
        return constraints.Count >= 3;
    }

    private bool TryBuildFaceEyeLocalTriangles(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> localVertexIndices,
        IReadOnlyList<int> outlineVertexIndices,
        out int[] triangles)
    {
        triangles = Array.Empty<int>();
        if (vertices == null || localVertexIndices == null || localVertexIndices.Count < 3 ||
            outlineVertexIndices == null || outlineVertexIndices.Count < 3)
        {
            return false;
        }

        var fullToLocal = new Dictionary<int, int>();
        var localToFull = new List<int>();
        for (var i = 0; i < localVertexIndices.Count; i++)
        {
            var fullIndex = localVertexIndices[i];
            if (fullIndex < 0 || fullIndex >= vertices.Count || fullToLocal.ContainsKey(fullIndex))
                continue;
            fullToLocal.Add(fullIndex, localToFull.Count);
            localToFull.Add(fullIndex);
        }
        for (var i = 0; i < outlineVertexIndices.Count; i++)
        {
            var fullIndex = outlineVertexIndices[i];
            if (fullIndex < 0 || fullIndex >= vertices.Count || fullToLocal.ContainsKey(fullIndex))
                continue;
            fullToLocal.Add(fullIndex, localToFull.Count);
            localToFull.Add(fullIndex);
        }
        if (localToFull.Count < 3)
            return false;

        var localVertices = new List<Vector2>(localToFull.Count);
        for (var i = 0; i < localToFull.Count; i++)
            localVertices.Add(vertices[localToFull[i]]);

        var localConstraints = new List<Vector2Int>(outlineVertexIndices.Count);
        for (var i = 0; i < outlineVertexIndices.Count; i++)
        {
            var next = (i + 1) % outlineVertexIndices.Count;
            if (!fullToLocal.TryGetValue(outlineVertexIndices[i], out var a) ||
                !fullToLocal.TryGetValue(outlineVertexIndices[next], out var b) || a == b)
            {
                continue;
            }
            localConstraints.Add(new Vector2Int(a, b));
        }
        if (localConstraints.Count < 3 ||
            !TryTriangulateWithUnityTessellation(
                localVertices,
                localConstraints,
                0,
                out var triangulatedVertices,
                out var triangulatedTriangles,
                out _) ||
            triangulatedVertices.Length != localVertices.Count ||
            !TryBuildVertexRemap(localVertices, triangulatedVertices, out var sourceToTri) ||
            !TryBuildInversePermutation(sourceToTri, triangulatedVertices.Length, out var triToSource))
        {
            return false;
        }

        triangles = new int[triangulatedTriangles.Length];
        for (var i = 0; i < triangulatedTriangles.Length; i++)
        {
            var triangulatedIndex = triangulatedTriangles[i];
            if (triangulatedIndex < 0 || triangulatedIndex >= triToSource.Length)
                return false;
            var localIndex = triToSource[triangulatedIndex];
            if (localIndex < 0 || localIndex >= localToFull.Count)
                return false;
            triangles[i] = localToFull[localIndex];
        }
        triangles = SanitizeTriangles(triangles, vertices.Count);
        return triangles.Length >= 3;
    }

    private static void CollectFaceEyeOutlinePolygon(
        LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        ReanimatorFaceEyePointExpansion expansion,
        ICollection<Vector2[]> polygons)
    {
        if (layer == null || analysis == null || expansion == null ||
            expansion.OutlineContour == null || expansion.OutlineContour.Length < 3 || polygons == null)
        {
            return;
        }

        var polygon = new Vector2[expansion.OutlineContour.Length];
        for (var i = 0; i < polygon.Length; i++)
            polygon[i] = FaceMaskPointToBaseDocument(layer, analysis, expansion.OutlineContour[i]);
        polygons.Add(polygon);
    }

    private bool TryRemoveVerticesInsideFaceEyeOutlines(
        LayerVertexData data,
        IReadOnlyList<Vector2[]> outlinePolygons,
        out int removedVertexCount)
    {
        removedVertexCount = 0;
        if (data == null || data.vertices == null || data.vertices.Length < 3 ||
            outlinePolygons == null || outlinePolygons.Count == 0)
        {
            return true;
        }

        var vertexCount = data.vertices.Length;
        var removeMask = new bool[vertexCount];
        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            for (var polygonIndex = 0; polygonIndex < outlinePolygons.Count; polygonIndex++)
            {
                if (!ReanimatorFaceAlphaMaskAlgorithms.IsPointStrictlyInsideContour(
                        data.vertices[vertexIndex],
                        outlinePolygons[polygonIndex]))
                {
                    continue;
                }
                removeMask[vertexIndex] = true;
                removedVertexCount++;
                break;
            }
        }

        if (removedVertexCount == 0)
            return true;
        if (vertexCount - removedVertexCount < 3)
            return false;

        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out var boundaryLoop))
        {
            var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
            if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
            {
                BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
                contourMask = BuildContourVertexMask(fallbackBoundaryPairs, vertexCount);
            }
            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        }

        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out _);
        var keptVertices = new List<Vector2>(vertexCount - removedVertexCount);
        var keptUvs = new List<Vector2>(vertexCount - removedVertexCount);
        var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
            ? new List<BoneWeight>(vertexCount - removedVertexCount)
            : null;
        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                continue;
            keptVertices.Add(data.vertices[i]);
            keptUvs.Add(data.vertexUvs != null && i < data.vertexUvs.Length
                ? data.vertexUvs[i]
                : ComputeUvForPoint(data.layer, data.vertices[i]));
            if (keptWeights != null)
                keptWeights.Add(data.boneWeights[i]);
        }

        var filteredBoundary = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
        if (filteredBoundary.Count < 3)
            return false;

        data.vertices = keptVertices.ToArray();
        data.vertexUvs = keptUvs.ToArray();
        data.boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>();
        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(filteredBoundary);
        data.triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, data.vertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
            return false;
        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
            return false;
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        return true;
    }

    private static void CollectFaceEyeApplyPoints(
        ReanimatorFaceEyePointExpansion expansion,
        ICollection<KeyValuePair<Vector2, float>> points)
    {
        if (expansion == null || points == null)
            return;

        for (var i = 0; i < expansion.BaseContour.Length; i++)
        {
            var weight = i < expansion.BasePointWeights.Length ? expansion.BasePointWeights[i] : 0f;
            points.Add(new KeyValuePair<Vector2, float>(expansion.BaseContour[i], weight));
        }
        for (var i = 0; i < expansion.DuplicatePoints.Length; i++)
        {
            var weight = i < expansion.DuplicatePointWeights.Length ? expansion.DuplicatePointWeights[i] : 0f;
            points.Add(new KeyValuePair<Vector2, float>(expansion.DuplicatePoints[i], weight));
        }

        // The red outline and its optional green collision marker stay entirely on head.
        for (var i = 0; i < expansion.OutlineContour.Length; i++)
            points.Add(new KeyValuePair<Vector2, float>(expansion.OutlineContour[i], 0f));
        for (var i = 0; i < expansion.OutlineCollisionPoints.Length; i++)
            points.Add(new KeyValuePair<Vector2, float>(expansion.OutlineCollisionPoints[i], 0f));
    }

    private static Vector2 FaceMaskPointToBaseDocument(
        LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        Vector2 maskPoint)
    {
        var scaleX = layer.sourceRect.width / Mathf.Max(1f, analysis.Width);
        var scaleY = layer.sourceRect.height / Mathf.Max(1f, analysis.Height);
        return PsdCoordinateSpace.LayerLocalToDocumentPoint(
            layer,
            new Vector2(maskPoint.x * scaleX, maskPoint.y * scaleY));
    }

    private static int FindFaceEyeVertexAt(IReadOnlyList<Vector2> vertices, Vector2 point)
    {
        if (vertices == null)
            return -1;
        for (var i = 0; i < vertices.Count; i++)
        {
            if ((vertices[i] - point).sqrMagnitude <= 0.0001f)
                return i;
        }
        return -1;
    }
}
#endif
