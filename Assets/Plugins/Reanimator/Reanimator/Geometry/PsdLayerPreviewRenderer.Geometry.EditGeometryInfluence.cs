#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool TryGetWeightSliderSelection(out EditGeometrySelectionInfo selection, out int selectedVertexCount)
    {
        selectedVertexCount = 0;
        if (!TryGetEditGeometrySelection(out selection))
            return false;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out _, out var stableKey))
            return false;

        selectedVertexCount = NormalizeWeightSliderSelection(data, stableKey);
        return selectedVertexCount > 0;
    }

    public bool TrySetWeightSliderSelectedVertexWeights(int[] boneIndices, float[] weights, bool normalize)
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;
        if (boneIndices == null || boneIndices.Length < 4 || weights == null || weights.Length < 4)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        if (spriteBoneCount <= 0)
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        var selectedCount = NormalizeWeightSliderSelection(data, stableKey);
        if (selectedCount <= 0)
            return false;

        var boneWeight = BuildSanitizedEditGeometryBoneWeight(boneIndices, weights, spriteBoneCount, normalize);
        for (var i = 0; i < weightSliderSelectedVertexIndices.Count; i++)
        {
            var vertexIndex = weightSliderSelectedVertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
                continue;

            data.boneWeights[vertexIndex] = boneWeight;
        }

        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    public bool TrySmoothWeightSliderSelection(float strength)
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        if (spriteBoneCount <= 0)
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        var selectedCount = NormalizeWeightSliderSelection(data, stableKey);
        if (selectedCount <= 0)
            return false;

        var sourceWeights = (BoneWeight[])data.boneWeights.Clone();
        var changed = false;
        for (var i = 0; i < weightSliderSelectedVertexIndices.Count; i++)
        {
            var vertexIndex = weightSliderSelectedVertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= sourceWeights.Length)
                continue;

            if (!ReanimatorWeightSliderAlgorithms.TryBuildSmoothedBoneWeight(
                    sourceWeights,
                    vertexIndex,
                    data.allEdgePairs,
                    data.triangles,
                    spriteBoneCount,
                    strength,
                    out var next))
                continue;

            if (AreBoneWeightsApproximatelyEqual(sourceWeights[vertexIndex], next))
                continue;

            if (!changed)
                RegisterUndo("Smooth Slider Weights");

            data.boneWeights[vertexIndex] = next;
            changed = true;
        }

        if (!changed)
            return false;

        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    public bool TrySuggestWeightSliderBone(int[] excludedSpriteBoneIndices, out int spriteBoneIndex)
    {
        spriteBoneIndex = -1;
        if (!TryGetSelectedWeightSliderLayerData(out var data, out _, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0 || bones == null || bones.Count == 0)
            return false;

        var selectedCount = NormalizeWeightSliderSelection(data, stableKey);
        if (selectedCount <= 0)
            return false;

        var excluded = new HashSet<int>();
        if (excludedSpriteBoneIndices != null)
        {
            for (var i = 0; i < excludedSpriteBoneIndices.Length; i++)
            {
                if (excludedSpriteBoneIndices[i] >= 0)
                    excluded.Add(excludedSpriteBoneIndices[i]);
            }
        }

        var drawVertices = GetLayerVerticesForDraw(data) ?? data.vertices;
        var bestScore = float.MaxValue;
        var bestSpriteBone = -1;
        for (var spriteBone = 0; spriteBone < data.spriteBoneToRendererBone.Length; spriteBone++)
        {
            if (excluded.Contains(spriteBone))
                continue;

            var rendererBoneIndex = data.spriteBoneToRendererBone[spriteBone];
            if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
                continue;

            var start = bones[rendererBoneIndex].worldPosition;
            var end = GetBoneEnd(rendererBoneIndex);
            var score = 0f;
            var scoredCount = 0;
            for (var i = 0; i < weightSliderSelectedVertexIndices.Count; i++)
            {
                var vertexIndex = weightSliderSelectedVertexIndices[i];
                if (vertexIndex < 0 || vertexIndex >= drawVertices.Length)
                    continue;

                score += DistancePointToSegment(drawVertices[vertexIndex], start, end);
                scoredCount++;
            }

            if (scoredCount <= 0)
                continue;

            score /= scoredCount;
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestSpriteBone = spriteBone;
        }

        if (bestSpriteBone < 0)
            return false;

        spriteBoneIndex = bestSpriteBone;
        return true;
    }

    public void BeginWeightBrushStroke()
    {
        weightBrushStrokeUndoRegistered = false;
    }

    public bool TryGetWeightBrushTargetInfo(out WeightBrushTargetInfo info)
    {
        info = default;
        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        var spriteBoneNames = BuildSpriteBoneNameList(data.spriteBoneToRendererBone, spriteBoneCount);
        var spriteBoneColors = BuildSpriteBoneColorList(data.spriteBoneToRendererBone, spriteBoneCount);
        info = new WeightBrushTargetInfo(
            ComputeEditGeometryLayerToken(selectionKey, stableKey),
            data.layer != null && !string.IsNullOrEmpty(data.layer.name) ? data.layer.name : "<unknown>",
            data.vertices.Length,
            spriteBoneCount,
            spriteBoneNames,
            data.spriteBoneToRendererBone,
            spriteBoneColors);
        return true;
    }

    public bool TryGetBoneInfluenceInfo(out BoneInfluenceInfo info)
    {
        info = default;
        if (selectedBoneIndex < 0 || bones == null || selectedBoneIndex >= bones.Count)
            return false;

        var bone = bones[selectedBoneIndex];
        if (bone == null)
            return false;

        var layers = new List<BoneInfluenceLayerInfo>();
        var totalVertexCount = 0;
        var totalInfluencedCount = 0;
        var totalWeight = 0f;
        var maxWeight = 0f;

        if (vertexDataByLayerSelectionKey != null)
        {
            foreach (var pair in vertexDataByLayerSelectionKey)
            {
                var data = pair.Value;
                if (data == null || data.vertices == null || data.vertices.Length == 0)
                    continue;

                var spriteBoneIndex = FindSpriteBoneIndexForRendererBone(data, selectedBoneIndex);
                if (spriteBoneIndex < 0)
                    continue;

                var vertexCount = data.vertices.Length;
                totalVertexCount += vertexCount;
                var influencedCount = 0;
                var layerTotalWeight = 0f;
                var layerMaxWeight = 0f;
                var weightCount = data.boneWeights != null ? Mathf.Min(data.boneWeights.Length, vertexCount) : 0;

                for (var i = 0; i < weightCount; i++)
                {
                    var weight = GetSpriteBoneWeight(data.boneWeights[i], spriteBoneIndex);
                    if (weight <= 0.0001f)
                        continue;

                    influencedCount++;
                    layerTotalWeight += weight;
                    layerMaxWeight = Mathf.Max(layerMaxWeight, weight);
                }

                if (influencedCount <= 0)
                    continue;

                totalInfluencedCount += influencedCount;
                totalWeight += layerTotalWeight;
                maxWeight = Mathf.Max(maxWeight, layerMaxWeight);
                layers.Add(new BoneInfluenceLayerInfo(GetLayerDisplayName(data), vertexCount, influencedCount, layerTotalWeight, layerMaxWeight));
            }
        }

        var boneName = string.IsNullOrWhiteSpace(bone.name) ? $"bone_{selectedBoneIndex}" : bone.name;
        info = new BoneInfluenceInfo(selectedBoneIndex, boneName, bone.color, totalVertexCount, totalInfluencedCount, totalWeight, maxWeight, layers.ToArray());
        return true;
    }

    public bool TryGetSelectedBoneDebugInfo(out BoneDebugInfo info)
    {
        return TryGetBoneDebugInfo(selectedBoneIndex, out info);
    }

    public bool TryGetBoneDebugInfo(int rendererBoneIndex, out BoneDebugInfo info)
    {
        info = default;
        if (rendererBoneIndex < 0 || bones == null || rendererBoneIndex >= bones.Count)
            return false;

        var bone = bones[rendererBoneIndex];
        if (bone == null)
            return false;

        var issues = BoneDebugIssueFlags.None;
        if (!bone.visible)
            issues |= BoneDebugIssueFlags.Hidden;
        if (bone.parentId >= 0 && (bone.parentId >= bones.Count || bones[bone.parentId] == null))
            issues |= BoneDebugIssueFlags.MissingParent;
        if (bone.length <= 0.0001f && CountBoneChildren(rendererBoneIndex) == 0)
            issues |= BoneDebugIssueFlags.ZeroLength;

        var posePositionDelta = Vector2.Distance(bone.position, bone.initialPosition);
        var poseRotationDelta = Quaternion.Angle(bone.rotation, bone.initialRotation);
        if (posePositionDelta > 0.001f || poseRotationDelta > 0.01f)
            issues |= BoneDebugIssueFlags.PoseChanged;

        var sprites = new List<BoneDebugSpriteInfo>();
        var mappedSpriteCount = 0;
        var weightedSpriteCount = 0;
        var influencedVertexCount = 0;
        var mappedVertexCount = 0;
        var totalWeight = 0f;
        var maxWeight = 0f;

        if (vertexDataByLayerSelectionKey != null)
        {
            foreach (var pair in vertexDataByLayerSelectionKey)
            {
                var data = pair.Value;
                if (data == null || data.layer == null)
                    continue;

                var layerStatus = GetLayerDebugStatus(data.layer);
                if ((layerStatus.issues & LayerDebugIssueFlags.MissingGeometry) != 0)
                    issues |= BoneDebugIssueFlags.MissingGeometry;
                if ((layerStatus.issues & LayerDebugIssueFlags.WeightIssue) != 0)
                    issues |= BoneDebugIssueFlags.WeightIssue;
                if ((layerStatus.issues & LayerDebugIssueFlags.BoneMapIssue) != 0)
                    issues |= BoneDebugIssueFlags.BoneMapIssue;
                if ((layerStatus.issues & LayerDebugIssueFlags.BindPoseIssue) != 0)
                    issues |= BoneDebugIssueFlags.BindPoseIssue;

                var spriteBoneIndex = FindSpriteBoneIndexForRendererBone(data, rendererBoneIndex);
                if (spriteBoneIndex < 0)
                    continue;

                mappedSpriteCount++;
                var vertexCount = data.vertices != null ? data.vertices.Length : 0;
                mappedVertexCount += vertexCount;
                var layerInfluencedCount = 0;
                var layerTotalWeight = 0f;
                var layerMaxWeight = 0f;
                var weightCount = data.boneWeights != null ? Mathf.Min(data.boneWeights.Length, vertexCount) : 0;
                for (var i = 0; i < weightCount; i++)
                {
                    var weight = GetSpriteBoneWeight(data.boneWeights[i], spriteBoneIndex);
                    if (weight <= 0.0001f)
                        continue;

                    layerInfluencedCount++;
                    layerTotalWeight += weight;
                    layerMaxWeight = Mathf.Max(layerMaxWeight, weight);
                }

                if (layerInfluencedCount > 0)
                    weightedSpriteCount++;
                influencedVertexCount += layerInfluencedCount;
                totalWeight += layerTotalWeight;
                maxWeight = Mathf.Max(maxWeight, layerMaxWeight);
                sprites.Add(new BoneDebugSpriteInfo(
                    GetLayerDisplayName(data),
                    vertexCount,
                    layerInfluencedCount,
                    spriteBoneIndex,
                    layerTotalWeight,
                    layerMaxWeight,
                    layerStatus.issues));
            }
        }

        if (weightedSpriteCount <= 0)
            issues |= BoneDebugIssueFlags.NoWeightedSprites;

        var parentName = "<root>";
        if (bone.parentId >= 0 && bone.parentId < bones.Count && bones[bone.parentId] != null)
            parentName = string.IsNullOrWhiteSpace(bones[bone.parentId].name) ? $"bone_{bone.parentId}" : bones[bone.parentId].name;
        var boneName = string.IsNullOrWhiteSpace(bone.name) ? $"bone_{rendererBoneIndex}" : bone.name;
        info = new BoneDebugInfo(
            rendererBoneIndex,
            boneName,
            bone.color,
            bone.parentId,
            parentName,
            CountBoneChildren(rendererBoneIndex),
            bone.initialPosition,
            bone.position,
            bone.worldPosition,
            bone.length,
            posePositionDelta,
            poseRotationDelta,
            mappedSpriteCount,
            weightedSpriteCount,
            influencedVertexCount,
            mappedVertexCount,
            totalWeight,
            maxWeight,
            issues,
            sprites.ToArray());
        return true;
    }

    private int CountBoneChildren(int parentIndex)
    {
        if (bones == null || parentIndex < 0)
            return 0;

        var count = 0;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone != null && bone.parentId == parentIndex)
                count++;
        }

        return count;
    }

    public bool TrySelectSelectedBoneInfluenceVertices(out int selectedCount, out string layerName, out string boneName, out string error)
    {
        selectedCount = 0;
        layerName = string.Empty;
        boneName = string.Empty;
        error = null;

        if (selectedBoneIndex < 0 || bones == null || selectedBoneIndex >= bones.Count)
        {
            error = "Select a bone.";
            return false;
        }

        var bone = bones[selectedBoneIndex];
        boneName = bone != null && !string.IsNullOrWhiteSpace(bone.name) ? bone.name : $"bone_{selectedBoneIndex}";

        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select a sprite.";
            return false;
        }
        if (data.vertices == null || data.vertices.Length == 0)
        {
            error = "Selected sprite has no geometry.";
            return false;
        }

        layerName = GetLayerDisplayName(data);
        var spriteBoneIndex = FindSpriteBoneIndexForRendererBone(data, selectedBoneIndex);
        if (spriteBoneIndex < 0)
        {
            error = "Selected bone is not mapped to this sprite.";
            return false;
        }
        if (data.boneWeights == null || data.boneWeights.Length == 0)
        {
            error = "Selected sprite has no weights.";
            return false;
        }

        var selected = new HashSet<int>();
        var weightCount = Mathf.Min(data.boneWeights.Length, data.vertices.Length);
        for (var i = 0; i < weightCount; i++)
        {
            if (GetSpriteBoneWeight(data.boneWeights[i], spriteBoneIndex) > 0.0001f)
                selected.Add(i);
        }

        if (selected.Count <= 0)
        {
            error = "No influenced vertices on selected sprite.";
            return false;
        }

        selectedLayerSelectionKey = selectionKey;
        ApplyEditGeometrySelectionSet(stableKey, selected);
        selectedCount = selected.Count;
        return true;
    }

    public bool TryGetSpriteInfluenceInfo(out SpriteInfluenceInfo info)
    {
        info = default;
        if (!TryGetWeightBrushLayerData(out var data, out _, out _) || data == null)
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        if (spriteBoneCount <= 0)
            return false;

        var names = BuildSpriteBoneNameList(data.spriteBoneToRendererBone, spriteBoneCount);
        var colors = BuildSpriteBoneColorList(data.spriteBoneToRendererBone, spriteBoneCount);
        var bonesInfo = new SpriteInfluenceBoneInfo[spriteBoneCount];
        var weightCount = data.boneWeights != null ? Mathf.Min(data.boneWeights.Length, data.vertices.Length) : 0;

        for (var spriteBone = 0; spriteBone < spriteBoneCount; spriteBone++)
        {
            var influencedCount = 0;
            var totalWeight = 0f;
            var maxWeight = 0f;
            for (var i = 0; i < weightCount; i++)
            {
                var weight = GetSpriteBoneWeight(data.boneWeights[i], spriteBone);
                if (weight <= 0.0001f)
                    continue;

                influencedCount++;
                totalWeight += weight;
                maxWeight = Mathf.Max(maxWeight, weight);
            }

            var rendererBoneIndex = data.spriteBoneToRendererBone != null && spriteBone < data.spriteBoneToRendererBone.Length
                ? data.spriteBoneToRendererBone[spriteBone]
                : -1;
            var boneName = names != null && spriteBone < names.Length ? names[spriteBone] : $"Bone {spriteBone}";
            var color = colors != null && spriteBone < colors.Length ? colors[spriteBone] : Color.gray;
            bonesInfo[spriteBone] = new SpriteInfluenceBoneInfo(spriteBone, rendererBoneIndex, boneName, color, influencedCount, totalWeight, maxWeight);
        }

        info = new SpriteInfluenceInfo(GetLayerDisplayName(data), data.vertices.Length, bonesInfo);
        return true;
    }

    public bool TrySelectSpriteInfluenceVertices(int spriteBoneIndex, out int selectedCount, out string layerName, out string boneName, out string error)
    {
        selectedCount = 0;
        layerName = string.Empty;
        boneName = string.Empty;
        error = null;

        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select a sprite.";
            return false;
        }
        if (data.vertices == null || data.vertices.Length == 0)
        {
            error = "Selected sprite has no geometry.";
            return false;
        }
        if (data.spriteBoneToRendererBone == null || spriteBoneIndex < 0 || spriteBoneIndex >= data.spriteBoneToRendererBone.Length)
        {
            error = "Select a sprite bone.";
            return false;
        }
        if (data.boneWeights == null || data.boneWeights.Length == 0)
        {
            error = "Selected sprite has no weights.";
            return false;
        }

        var names = BuildSpriteBoneNameList(data.spriteBoneToRendererBone, data.spriteBoneToRendererBone.Length);
        layerName = GetLayerDisplayName(data);
        boneName = names != null && spriteBoneIndex < names.Length ? names[spriteBoneIndex] : $"Bone {spriteBoneIndex}";

        var selected = new HashSet<int>();
        var weightCount = Mathf.Min(data.boneWeights.Length, data.vertices.Length);
        for (var i = 0; i < weightCount; i++)
        {
            if (GetSpriteBoneWeight(data.boneWeights[i], spriteBoneIndex) > 0.0001f)
                selected.Add(i);
        }

        if (selected.Count <= 0)
        {
            error = "No influenced vertices for selected bone.";
            return false;
        }

        selectedLayerSelectionKey = selectionKey;
        ApplyEditGeometrySelectionSet(stableKey, selected);
        selectedCount = selected.Count;
        return true;
    }

    public bool TrySelectWeightBrushBone(int spriteBoneIndex)
    {
        if (spriteBoneIndex < 0)
        {
            if (selectedBoneIndex < 0)
                return false;

            selectedBoneIndex = -1;
            return true;
        }

        if (!TryGetWeightBrushLayerData(out var data, out _, out _))
            return false;
        if (data.spriteBoneToRendererBone == null ||
            spriteBoneIndex >= data.spriteBoneToRendererBone.Length)
            return false;

        var rendererBoneIndex = data.spriteBoneToRendererBone[spriteBoneIndex];
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            return false;
        if (selectedBoneIndex == rendererBoneIndex)
            return false;

        selectedBoneIndex = rendererBoneIndex;
        return true;
    }

    internal bool TrySelectWeightBrushBoneAt(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out int spriteBoneIndex)
    {
        spriteBoneIndex = -1;
        if (context == null || context.Coordinates == null)
            return false;

        var hit = FindBoneHandleAt(mouseCanvas, context.Coordinates);
        if (hit.index < 0 || hit.index >= bones.Count)
            return false;

        if (!TryGetWeightBrushLayerData(out var data, out _, out _))
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return false;

        for (var i = 0; i < data.spriteBoneToRendererBone.Length; i++)
        {
            if (data.spriteBoneToRendererBone[i] != hit.index)
                continue;

            spriteBoneIndex = i;
            selectedBoneIndex = hit.index;
            return true;
        }

        return false;
    }

    internal bool TryReplaceWeightBrushBoneAt(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        int spriteBoneIndex,
        out int replacedSpriteBoneIndex)
    {
        replacedSpriteBoneIndex = -1;
        if (context == null || context.Coordinates == null)
            return false;

        var hit = FindBoneHandleAt(mouseCanvas, context.Coordinates);
        if (hit.index < 0 || hit.index >= bones.Count)
            return false;

        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
        {
            RegisterUndo("Add Weight Brush Bone");
            if (!TrySetSpriteBoneMapPreservingBindMatrices(data, null, new[] { hit.index }, 0))
                return false;
            EnsureBoneWeightsArraySize(data, data.vertices != null ? data.vertices.Length : 0);
            if (data.boneWeights != null)
            {
                var weight = BuildSingleBoneWeight(0);
                for (var i = 0; i < data.boneWeights.Length; i++)
                    data.boneWeights[i] = weight;
            }
            MarkSkinningWeightsChanged(data);
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);

            selectedBoneIndex = hit.index;
            replacedSpriteBoneIndex = 0;
            return true;
        }

        var targetSpriteBone = spriteBoneIndex >= 0 && spriteBoneIndex < data.spriteBoneToRendererBone.Length
            ? spriteBoneIndex
            : 0;

        var changed = data.spriteBoneToRendererBone[targetSpriteBone] != hit.index;
        if (changed)
        {
            RegisterUndo("Replace Weight Brush Bone");
            var previousMap = data.spriteBoneToRendererBone;
            var nextMap = CloneArray(previousMap) ?? Array.Empty<int>();
            nextMap[targetSpriteBone] = hit.index;
            if (!TrySetSpriteBoneMapPreservingBindMatrices(data, previousMap, nextMap, targetSpriteBone))
                return false;
            MarkSkinningWeightsChanged(data);
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        }

        selectedBoneIndex = hit.index;
        replacedSpriteBoneIndex = targetSpriteBone;
        return true;
    }

    internal bool TryAddWeightBrushBoneAt(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out int addedSpriteBoneIndex,
        out string error)
    {
        addedSpriteBoneIndex = -1;
        error = null;
        if (context == null || context.Coordinates == null)
            return false;

        var hit = FindBoneHandleAt(mouseCanvas, context.Coordinates);
        if (hit.index < 0 || hit.index >= bones.Count)
            return false;

        return AddWeightBrushBone(hit.index, out addedSpriteBoneIndex, out error);
    }

    internal bool AddWeightBrushBone(
        int rendererBoneIndex,
        out int addedSpriteBoneIndex,
        out string error)
    {
        addedSpriteBoneIndex = -1;
        error = null;
        if (bones == null || rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
        {
            error = "Invalid bone";
            return false;
        }
        if (!TryGetWeightBrushLayerData(out var data, out _, out _) || data == null)
        {
            error = "Select sprite geometry first";
            return false;
        }

        var map = data.spriteBoneToRendererBone;
        if (map != null)
        {
            for (var i = 0; i < map.Length; i++)
            {
                if (map[i] != rendererBoneIndex)
                    continue;

                selectedBoneIndex = rendererBoneIndex;
                addedSpriteBoneIndex = i;
                return true;
            }
        }

        return AssignWeightBrushBoneSlot(
            map != null ? map.Length : 0,
            rendererBoneIndex,
            out addedSpriteBoneIndex,
            out error);
    }

    public WeightBrushBoneCandidateInfo[] GetNearestWeightBrushBoneCandidates(int maxCount)
    {
        if (!TryGetWeightBrushLayerData(out var data, out _, out _) || data == null)
            return Array.Empty<WeightBrushBoneCandidateInfo>();
        if (data.vertices == null || data.vertices.Length == 0 || bones == null || bones.Count == 0)
            return Array.Empty<WeightBrushBoneCandidateInfo>();

        var center = Vector2.zero;
        for (var i = 0; i < data.vertices.Length; i++)
            center += data.vertices[i];
        center /= data.vertices.Length;

        var mapped = BuildRendererToSpriteBoneMap(data.spriteBoneToRendererBone);
        var result = new List<WeightBrushBoneCandidateInfo>(bones.Count);
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null)
                continue;
            if (mapped != null && mapped.ContainsKey(i))
                continue;

            var distance = DistancePointToSegment(center, bone.worldPosition, GetBoneEnd(i));
            var name = string.IsNullOrWhiteSpace(bone.name) ? $"bone_{i}" : bone.name;
            result.Add(new WeightBrushBoneCandidateInfo(i, name, distance, bone.color));
        }

        result.Sort((lhs, rhs) => lhs.distance.CompareTo(rhs.distance));
        var count = Mathf.Clamp(maxCount, 0, result.Count);
        if (count <= 0)
            return Array.Empty<WeightBrushBoneCandidateInfo>();
        if (count < result.Count)
            result.RemoveRange(count, result.Count - count);

        return result.ToArray();
    }

    public bool AssignWeightBrushBoneSlot(
        int requestedSpriteBoneIndex,
        int rendererBoneIndex,
        out int assignedSpriteBoneIndex,
        out string error)
    {
        assignedSpriteBoneIndex = -1;
        error = null;
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
        {
            error = "Invalid bone";
            return false;
        }
        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite geometry first";
            return false;
        }

        var previousMap = data.spriteBoneToRendererBone;
        var hadAnyValidBone = false;
        if (previousMap != null)
        {
            for (var i = 0; i < previousMap.Length; i++)
            {
                if (previousMap[i] < 0 || previousMap[i] >= bones.Count)
                    continue;
                hadAnyValidBone = true;
                break;
            }
        }

        var targetSpriteBone = previousMap == null || previousMap.Length == 0
            ? 0
            : requestedSpriteBoneIndex >= 0 && requestedSpriteBoneIndex <= previousMap.Length
                ? requestedSpriteBoneIndex
                : previousMap.Length;
        var nextLength = Mathf.Max(targetSpriteBone + 1, previousMap != null ? previousMap.Length : 0);
        if (nextLength <= 0)
            nextLength = 1;

        var nextMap = new int[nextLength];
        for (var i = 0; i < nextMap.Length; i++)
            nextMap[i] = -1;
        if (previousMap != null)
            Array.Copy(previousMap, nextMap, Mathf.Min(previousMap.Length, nextMap.Length));

        if (nextMap[targetSpriteBone] == rendererBoneIndex)
        {
            selectedBoneIndex = rendererBoneIndex;
            assignedSpriteBoneIndex = targetSpriteBone;
            return true;
        }

        RegisterUndo(hadAnyValidBone ? "Assign Weight Brush Bone" : "Add Weight Brush Bone");
        nextMap[targetSpriteBone] = rendererBoneIndex;
        if (!TrySetSpriteBoneMapPreservingBindMatrices(data, previousMap, nextMap, targetSpriteBone))
        {
            error = "Could not build bind pose for selected bone";
            return false;
        }
        EnsureBoneWeightsArraySize(data, data.vertices != null ? data.vertices.Length : 0);
        if (!hadAnyValidBone && data.boneWeights != null)
        {
            var weight = BuildSingleBoneWeight(targetSpriteBone);
            for (var i = 0; i < data.boneWeights.Length; i++)
                data.boneWeights[i] = weight;
        }

        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);

        selectedBoneIndex = rendererBoneIndex;
        assignedSpriteBoneIndex = targetSpriteBone;
        return true;
    }

    public bool RemoveWeightBrushBoneSlot(
        int spriteBoneIndex,
        out int removedInfluencedVertexCount,
        out string error)
    {
        removedInfluencedVertexCount = 0;
        error = null;
        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite geometry first";
            return false;
        }

        var previousMap = data.spriteBoneToRendererBone;
        if (previousMap == null || spriteBoneIndex < 0 || spriteBoneIndex >= previousMap.Length)
        {
            error = "Invalid sprite bone";
            return false;
        }

        var nextMap = new int[previousMap.Length - 1];
        if (spriteBoneIndex > 0)
            Array.Copy(previousMap, 0, nextMap, 0, spriteBoneIndex);
        if (spriteBoneIndex < nextMap.Length)
            Array.Copy(previousMap, spriteBoneIndex + 1, nextMap, spriteBoneIndex, nextMap.Length - spriteBoneIndex);

        var previousBind = data.bindBoneInverseMatrices;
        Matrix4x4[] nextBind;
        var preservedBindMatrices = previousBind != null && previousBind.Length == previousMap.Length;
        if (preservedBindMatrices)
        {
            nextBind = new Matrix4x4[nextMap.Length];
            if (spriteBoneIndex > 0)
                Array.Copy(previousBind, 0, nextBind, 0, spriteBoneIndex);
            if (spriteBoneIndex < nextBind.Length)
                Array.Copy(previousBind, spriteBoneIndex + 1, nextBind, spriteBoneIndex, nextBind.Length - spriteBoneIndex);
        }
        else
        {
            nextBind = BuildBindBoneInverseMatrices(nextMap);
        }

        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        var nextWeights = new BoneWeight[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var source = data.boneWeights != null && i < data.boneWeights.Length
                ? data.boneWeights[i]
                : default;
            if (GetSpriteBoneWeight(source, spriteBoneIndex) > 0.0001f)
                removedInfluencedVertexCount++;
            nextWeights[i] = ReanimatorWeightSliderAlgorithms.RemoveBoneInfluenceAndRemap(
                source,
                spriteBoneIndex,
                previousMap.Length);
        }

        var removedRendererBoneIndex = previousMap[spriteBoneIndex];
        RegisterUndo("Remove Weight Brush Bone");
        data.spriteBoneToRendererBone = nextMap;
        data.bindBoneInverseMatrices = nextBind ?? Array.Empty<Matrix4x4>();
        data.boneWeights = nextWeights;
        if (!preservedBindMatrices)
            data.usesSpriteBindPoses = false;

        if (selectedBoneIndex == removedRendererBoneIndex && Array.IndexOf(nextMap, removedRendererBoneIndex) < 0)
            selectedBoneIndex = -1;

        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private bool TrySetSpriteBoneMapPreservingBindMatrices(
        LayerVertexData data,
        int[] previousMap,
        int[] nextMap,
        int changedSpriteBoneIndex)
    {
        if (data == null || nextMap == null || nextMap.Length == 0)
            return false;

        var previousBind = data.bindBoneInverseMatrices;
        var nextBind = new Matrix4x4[nextMap.Length];
        for (var i = 0; i < nextBind.Length; i++)
        {
            var canReuseExisting =
                i != changedSpriteBoneIndex &&
                previousMap != null &&
                previousBind != null &&
                i < previousMap.Length &&
                i < previousBind.Length &&
                previousMap[i] == nextMap[i];

            if (canReuseExisting)
            {
                nextBind[i] = previousBind[i];
                continue;
            }

            var rendererBoneIndex = nextMap[i];
            if (rendererBoneIndex < 0)
            {
                nextBind[i] = Matrix4x4.identity;
                continue;
            }

            if (!TryBuildBindInverseMatrixForRendererBone(data, rendererBoneIndex, out nextBind[i]))
                return false;
        }

        data.spriteBoneToRendererBone = nextMap;
        data.bindBoneInverseMatrices = nextBind;
        data.skinMatrices = null;
        data.deformedVertices = null;
        data.lastPoseVersion = -1;
        return true;
    }
}
#endif
