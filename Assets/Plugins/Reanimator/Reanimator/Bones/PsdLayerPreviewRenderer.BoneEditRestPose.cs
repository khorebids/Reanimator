#if UNITY_EDITOR
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool CommitCurrentBonePoseAsRest()
    {
        if (bones == null || bones.Count == 0)
            return false;

        var changed = false;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var initialRotation = bone.initialRotation.Equals(default(Quaternion))
                ? Quaternion.identity
                : bone.initialRotation;
            if ((bone.initialPosition - bone.position).sqrMagnitude <= 1e-8f &&
                Quaternion.Angle(initialRotation, bone.rotation) <= 1e-4f)
                continue;

            bone.initialPosition = bone.position;
            bone.initialRotation = bone.rotation.Equals(default(Quaternion))
                ? Quaternion.identity
                : bone.rotation;
            bones[i] = bone;
            changed = true;
        }

        if (!changed)
            return false;

        RecomputeBoneWorldTransforms();
        RebindLayerSkinningToCurrentRestPose();
        InvalidateBonePose();
        bonePoseAtRest = true;
        hasPendingChanges = true;
        isDragging = false;
        isRotating = false;
        isResizingBone = false;
        return true;
    }

    private void RebindLayerSkinningToCurrentRestPose()
    {
        if (bones == null || bones.Count == 0)
            return;

        if (vertexDataByLayerSelectionKey != null)
        {
            foreach (var pair in vertexDataByLayerSelectionKey)
                RebindLayerDataSkinningToCurrentRestPose(pair.Value);
        }

        if (autoGeometryPatches == null)
            return;

        for (var i = 0; i < autoGeometryPatches.Count; i++)
            RebindPatchSkinningToCurrentRestPose(autoGeometryPatches[i]);
    }

    private void RebindLayerDataSkinningToCurrentRestPose(LayerVertexData data)
    {
        if (data == null || data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return;

        var bindMatrices = BuildBindBoneInverseMatrices(data.spriteBoneToRendererBone);
        if (bindMatrices == null || bindMatrices.Length != data.spriteBoneToRendererBone.Length)
            return;

        data.bindBoneInverseMatrices = bindMatrices;
        data.usesSpriteBindPoses = false;
        MarkSkinningWeightsChanged(data);
    }

    private void RebindPatchSkinningToCurrentRestPose(AutoGeometryPatch patch)
    {
        if (patch == null || patch.spriteBoneToRendererBone == null || patch.spriteBoneToRendererBone.Length == 0)
            return;

        var bindMatrices = BuildBindBoneInverseMatrices(patch.spriteBoneToRendererBone);
        if (bindMatrices == null || bindMatrices.Length != patch.spriteBoneToRendererBone.Length)
            return;

        patch.bindBoneInverseMatrices = bindMatrices;
        patch.usesSpriteBindPoses = false;
    }
}
#endif
