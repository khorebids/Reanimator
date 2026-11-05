#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private BoneWeight BuildSanitizedEditGeometryBoneWeight(int[] boneIndices, float[] weights, int spriteBoneCount, bool normalize)
    {
        return ReanimatorWeightSliderAlgorithms.BuildBoneWeightFromExplicitSlots(
            boneIndices,
            weights,
            spriteBoneCount,
            normalize);
    }

    private static BoneWeight SanitizeEditableBoneWeight(BoneWeight weight, int spriteBoneCount, bool normalize = false)
    {
        return ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(weight, spriteBoneCount, normalize, true);
    }

    private void MarkSkinningWeightsChanged(LayerVertexData data)
    {
        if (data == null)
            return;

        // Weight edits must materialize bind matrices before preview skinning can react to bones.
        EnsureLayerBindMatricesForWeights(data);

        data.hasSkinning = data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights != null &&
                          data.vertices != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        data.boneOrderTriangles = null;
        data.boneOrderSourceTriangles = null;
        data.boneOrderSourceWeights = null;
        data.lastBoneOrderVersion = -1;
    }

    private void EnsureLayerBindMatricesForWeights(LayerVertexData data)
    {
        if (data == null)
            return;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return;
        if (data.bindBoneInverseMatrices != null &&
            data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
            data.bindBoneInverseMatrices.Length > 0)
            return;

        data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(data.spriteBoneToRendererBone);
        data.usesSpriteBindPoses = false;
    }

    private int NormalizeWeightSliderSelection(LayerVertexData data, string stableKey)
    {
        if (weightSliderSelectedVertexIndices == null)
            weightSliderSelectedVertexIndices = new List<int>();

        if (data == null || data.vertices == null || data.vertices.Length == 0)
        {
            weightSliderSelectedVertexIndices.Clear();
            weightSliderSelectedLayerStableKey = string.Empty;
            return 0;
        }

        if (!string.Equals(weightSliderSelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            weightSliderSelectedVertexIndices.Clear();

        var used = new HashSet<int>();
        for (var i = weightSliderSelectedVertexIndices.Count - 1; i >= 0; i--)
        {
            var vertexIndex = weightSliderSelectedVertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= data.vertices.Length || !used.Add(vertexIndex))
                weightSliderSelectedVertexIndices.RemoveAt(i);
        }

        if (weightSliderSelectedVertexIndices.Count == 0 &&
            editGeometrySelectedVertexIndex >= 0 &&
            editGeometrySelectedVertexIndex < data.vertices.Length)
        {
            weightSliderSelectedVertexIndices.Add(editGeometrySelectedVertexIndex);
        }

        if (weightSliderSelectedVertexIndices.Count > 0)
        {
            weightSliderSelectedLayerStableKey = stableKey;
            editGeometrySelectedVertexIndex = weightSliderSelectedVertexIndices[0];
            editGeometrySelectedLayerStableKey = stableKey;
        }

        return weightSliderSelectedVertexIndices.Count;
    }

    private int NormalizeEditGeometrySelection(LayerVertexData data, string stableKey)
    {
        if (editGeometrySelectedVertexIndices == null)
            editGeometrySelectedVertexIndices = new List<int>();

        if (data == null || data.vertices == null || data.vertices.Length == 0)
        {
            editGeometrySelectedVertexIndices.Clear();
            editGeometrySelectedVertexIndex = -1;
            return 0;
        }

        if (!string.Equals(editGeometrySelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            editGeometrySelectedVertexIndices.Clear();

        var normalized = ReanimatorEditGeometrySelectionAlgorithms.NormalizeSelection(
            editGeometrySelectedVertexIndices,
            editGeometrySelectedVertexIndex,
            data.vertices.Length);
        editGeometrySelectedVertexIndices.Clear();
        editGeometrySelectedVertexIndices.AddRange(normalized);

        if (editGeometrySelectedVertexIndices.Count > 0)
        {
            editGeometrySelectedVertexIndex = editGeometrySelectedVertexIndices[0];
            editGeometrySelectedLayerStableKey = stableKey;
        }
        else
        {
            editGeometrySelectedVertexIndex = -1;
        }

        return editGeometrySelectedVertexIndices.Count;
    }

    private static float GetSpriteBoneWeight(BoneWeight weight, int spriteBoneIndex)
    {
        if (spriteBoneIndex < 0)
            return 0f;

        var value = 0f;
        if (weight.boneIndex0 == spriteBoneIndex)
            value += weight.weight0;
        if (weight.boneIndex1 == spriteBoneIndex)
            value += weight.weight1;
        if (weight.boneIndex2 == spriteBoneIndex)
            value += weight.weight2;
        if (weight.boneIndex3 == spriteBoneIndex)
            value += weight.weight3;

        return Mathf.Clamp01(value);
    }

    private void EnsureBoneWeightsArraySize(LayerVertexData data, int targetSize)
    {
        if (data == null || targetSize < 0)
            return;
        if (data.boneWeights == null)
            data.boneWeights = new BoneWeight[targetSize];
        else if (data.boneWeights.Length != targetSize)
            Array.Resize(ref data.boneWeights, targetSize);
    }
}
#endif
