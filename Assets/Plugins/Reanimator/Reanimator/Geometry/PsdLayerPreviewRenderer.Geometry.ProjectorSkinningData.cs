#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static bool HasProjectorTargetSkinningData(LayerVertexData data)
    {
        return ProjectorTargetSkinningData.HasAny(data);
    }

    private static void ClearProjectorTargetSkinningData(LayerVertexData data, int vertexCount)
    {
        ProjectorTargetSkinningData.Clear(data, vertexCount);
    }

    private static bool HasUsableVertexWeights(IReadOnlyList<BoneWeight> weights)
    {
        return ProjectorTargetSkinningData.HasUsableVertexWeights(weights);
    }

    private static class ProjectorTargetSkinningData
    {
        public static bool HasAny(LayerVertexData data)
        {
            if (data == null)
                return false;
            if (HasUsableVertexWeights(data.boneWeights))
                return true;
            if (data.spriteBoneToRendererBone != null && data.spriteBoneToRendererBone.Length > 0)
                return true;
            if (data.bindBoneInverseMatrices != null && data.bindBoneInverseMatrices.Length > 0)
                return true;
            return data.usesSpriteBindPoses ||
                   data.hasSkinning ||
                   data.skinMatrices != null ||
                   data.deformedVertices != null ||
                   data.lastPoseVersion >= 0;
        }

        public static void Clear(LayerVertexData data, int vertexCount)
        {
            if (data == null)
                return;

            data.boneWeights = new BoneWeight[Mathf.Max(0, vertexCount)];
            data.spriteBoneToRendererBone = Array.Empty<int>();
            data.bindBoneInverseMatrices = Array.Empty<Matrix4x4>();
            data.usesSpriteBindPoses = false;
            data.hasSkinning = false;
            data.lastPoseVersion = -1;
            data.deformedVertices = null;
            data.skinMatrices = null;
        }

        public static bool HasUsableVertexWeights(IReadOnlyList<BoneWeight> weights)
        {
            if (weights == null)
                return false;

            for (var i = 0; i < weights.Count; i++)
            {
                if (IsUsableVertexWeight(weights[i]))
                    return true;
            }

            return false;
        }
    }
}
#endif
