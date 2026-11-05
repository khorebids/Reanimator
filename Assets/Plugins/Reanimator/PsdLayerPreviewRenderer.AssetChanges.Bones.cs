#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public void SelectBoneForExternalPanel(int boneIndex)
    {
        selectedBoneIndex = boneIndex >= 0 && boneIndex < bones.Count ? boneIndex : -1;
    }

    public bool SetBoneWorldTransformFromExternalPanel(
        int boneIndex,
        Vector2 worldPosition,
        Quaternion worldRotation,
        float length,
        bool commit)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        if (bone == null)
            return false;

        selectedBoneIndex = boneIndex;
        var nextPosition = ConvertBoneWorldPositionToLocal(bone, worldPosition);
        var nextRotation = ConvertBoneWorldRotationToLocal(bone, worldRotation);
        var nextLength = Mathf.Max(1f, length);
        var changed =
            (bone.position - nextPosition).sqrMagnitude > 0.0001f ||
            Quaternion.Angle(bone.rotation, nextRotation) > 0.01f ||
            Mathf.Abs(bone.length - nextLength) > 0.01f;
        if (!changed && !commit)
            return false;

        bone.position = nextPosition;
        bone.rotation = nextRotation;
        bone.length = nextLength;
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransforms();
        InvalidateBonePose();
        hasPendingChanges = true;
        if (commit)
            MarkBoneStateChanged();
        return changed;
    }

    public void CommitCurrentBonePoseAsRestFromExternalPanel()
    {
        if (bones == null || bones.Count == 0)
            return;

        RecomputeBoneWorldTransforms();
        CaptureInitialBonePoseFromCurrent();

        // Some automatic geometry stages adjust bones after weights have already
        // created their inverse bind matrices (notably the hip JointV2 pass).
        // Once the resulting skeleton becomes the new rest pose, every weighted
        // sprite must be rebound to that exact pose. Otherwise the first repaint
        // treats the bind/rest mismatch as a live deformation and can throw whole
        // body layers outside the viewport until Refresh Cache rebuilds them.
        if (vertexDataByLayerSelectionKey != null)
        {
            foreach (var pair in vertexDataByLayerSelectionKey)
            {
                var selectionKey = pair.Key;
                var data = pair.Value;
                if (data == null || data.spriteBoneToRendererBone == null ||
                    data.spriteBoneToRendererBone.Length == 0)
                {
                    continue;
                }

                data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(data.spriteBoneToRendererBone);
                data.usesSpriteBindPoses = false;
                data.lastPoseVersion = -1;
                data.deformedVertices = null;
                data.skinMatrices = null;
                data.hasSkinning = data.boneWeights != null && data.vertices != null &&
                                   data.boneWeights.Length == data.vertices.Length &&
                                   data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                                   data.bindBoneInverseMatrices.Length > 0;

                var stableKey = data.layer != null
                    ? BuildKey(data.layer.name, data.layer.layerId)
                    : string.Empty;
                MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            }
        }

        InvalidateBonePose();
        bonePoseAtRest = true;
        hasPendingChanges = true;
        MarkBoneStateChanged();
    }

    public bool RenameBone(int boneIndex, string newName)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var sanitized = (newName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(sanitized))
            return false;

        var bone = bones[boneIndex];
        if (string.Equals(bone.name, sanitized, StringComparison.Ordinal))
            return true;

        RegisterUndo("Rename Bone");
        bone.name = sanitized;
        bones[boneIndex] = bone;
        MarkBoneStateChanged();
        return true;
    }

    public bool TryFindBoneIndexByName(string boneName, out int boneIndex)
    {
        boneIndex = -1;
        var normalizedName = (boneName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedName) || bones == null)
            return false;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null ||
                !string.Equals((bone.name ?? string.Empty).Trim(), normalizedName, StringComparison.OrdinalIgnoreCase))
                continue;

            boneIndex = i;
            return true;
        }

        return false;
    }

    public bool TryCreateNamedChildBone(
        string boneName,
        int parentBoneIndex,
        float length,
        Vector2 worldOffset,
        out int createdBoneIndex)
    {
        createdBoneIndex = -1;
        var normalizedName = (boneName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedName) ||
            bones == null ||
            parentBoneIndex < 0 ||
            parentBoneIndex >= bones.Count ||
            bones[parentBoneIndex] == null ||
            TryFindBoneIndexByName(normalizedName, out _))
            return false;

        var parent = bones[parentBoneIndex];
        var worldPosition = parent.worldPosition + worldOffset;
        var worldRotation = parent.worldRotation;
        var localPosition = ConvertBoneWorldPositionToLocal(parentBoneIndex, worldPosition);
        var localRotation = ConvertBoneWorldRotationToLocal(parentBoneIndex, worldRotation);

        var displayName = char.ToUpperInvariant(normalizedName[0]) + normalizedName.Substring(1);
        RegisterUndo("Create " + displayName + " Bone");

        createdBoneIndex = bones.Count;
        bones.Add(new BoneInfo
        {
            index = createdBoneIndex,
            name = normalizedName,
            parentId = parentBoneIndex,
            position = localPosition,
            rotation = localRotation,
            initialPosition = localPosition,
            initialRotation = localRotation,
            length = Mathf.Max(0.01f, length),
            depth = createdBoneIndex,
            color = CreateDefaultBoneColor(createdBoneIndex),
            visible = true
        });

        selectedBoneIndex = createdBoneIndex;
        RecomputeBoneWorldTransforms();
        BuildBoneDrawOrder();
        bonePoseAtRest = true;
        MarkBoneStateChanged();
        return true;
    }

    public bool SetLayerDepth(LayerInfo layer, int newDepth)
    {
        if (layer == null)
            return false;
        if (layer.depth == newDepth)
            return false;

        RegisterUndo("Set Layer Depth");
        layer.depth = newDepth;
        BuildDrawOrder();
        hasPendingChanges = true;
        return true;
    }

    public bool SetLayerVisible(LayerInfo layer, bool visible)
    {
        if (layer == null)
            return false;
        if (layer.visible == visible)
            return false;

        RegisterUndo("Set Layer Visibility");
        layer.visible = visible;
        StoreUiVisibilityCacheForCurrentAsset();
        hasPendingChanges = true;
        return true;
    }

    public bool SetLayerColor(LayerInfo layer, Color color)
    {
        if (layer == null)
            return false;
        if (AreColorsApproximatelyEqual(layer.color, color))
            return false;

        RegisterUndo("Set Layer Color");
        layer.color = color;
        hasPendingChanges = true;
        return true;
    }

    public bool SetLayerHierarchyNodeVisible(string nodeKey, bool visible)
    {
        if (string.IsNullOrEmpty(nodeKey) || layerHierarchyNodes == null || layerHierarchyNodes.Count == 0)
            return false;

        RegisterUndo("Set Layer Visibility");
        var changed = false;
        if (visible)
            SetLayerHierarchyAncestorsVisible(nodeKey, ref changed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetLayerHierarchyNodeVisibleRecursive(nodeKey, visible, ref changed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (!changed)
            return false;

        StoreUiVisibilityCacheForCurrentAsset();
        hasPendingChanges = true;
        return true;
    }

    private void SetLayerHierarchyAncestorsVisible(string nodeKey, ref bool changed, HashSet<string> path)
    {
        if (string.IsNullOrEmpty(nodeKey) || layerHierarchyNodes == null || !path.Add(nodeKey))
            return;

        LayerHierarchyNode node = null;
        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var candidate = layerHierarchyNodes[i];
            if (candidate == null || !string.Equals(candidate.key, nodeKey, StringComparison.OrdinalIgnoreCase))
                continue;

            node = candidate;
            break;
        }

        if (node == null || string.IsNullOrEmpty(node.parentKey))
        {
            path.Remove(nodeKey);
            return;
        }

        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var parent = layerHierarchyNodes[i];
            if (parent == null || !string.Equals(parent.key, node.parentKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!parent.visible)
            {
                parent.visible = true;
                changed = true;
            }

            if (!parent.isGroup && parent.layerIndex >= 0 && parent.layerIndex < layerInfos.Count)
            {
                var layer = layerInfos[parent.layerIndex];
                if (layer != null && !layer.visible)
                {
                    layer.visible = true;
                    changed = true;
                }
            }

            SetLayerHierarchyAncestorsVisible(parent.key, ref changed, path);
            break;
        }

        path.Remove(nodeKey);
    }

    private void SetLayerHierarchyNodeVisibleRecursive(string nodeKey, bool visible, ref bool changed, HashSet<string> path)
    {
        if (string.IsNullOrEmpty(nodeKey) || !path.Add(nodeKey))
            return;

        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var node = layerHierarchyNodes[i];
            if (node == null || !string.Equals(node.key, nodeKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (node.visible != visible)
            {
                node.visible = visible;
                changed = true;
            }

            if (!node.isGroup && node.layerIndex >= 0 && node.layerIndex < layerInfos.Count)
            {
                var layer = layerInfos[node.layerIndex];
                if (layer != null && layer.visible != visible)
                {
                    layer.visible = visible;
                    changed = true;
                }
            }

            break;
        }

        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var child = layerHierarchyNodes[i];
            if (child == null || !string.Equals(child.parentKey, nodeKey, StringComparison.OrdinalIgnoreCase))
                continue;

            SetLayerHierarchyNodeVisibleRecursive(child.key, visible, ref changed, path);
        }

        path.Remove(nodeKey);
    }

    public bool SetBoneDepth(int boneIndex, int newDepth)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        if (bone.depth == newDepth)
            return false;

        RegisterUndo("Set Bone Depth");
        bone.depth = newDepth;
        bones[boneIndex] = bone;
        boneOrderVersion++;
        BuildBoneDrawOrder();
        MarkBoneStateChanged();
        return true;
    }

    public bool SetBoneVisible(int boneIndex, bool visible)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        if (bone.visible == visible)
            return false;

        RegisterUndo("Set Bone Visibility");
        bone.visible = visible;
        bones[boneIndex] = bone;
        StoreUiVisibilityCacheForCurrentAsset();
        MarkBoneStateChanged();
        return true;
    }

    public bool SetBoneColor(int boneIndex, Color color)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        var sanitized = SanitizeBoneColor(color);
        if (AreColorsApproximatelyEqual(bone.color, sanitized))
            return false;

        RegisterUndo("Set Bone Color");
        bone.color = sanitized;
        bones[boneIndex] = bone;
        MarkBoneStateChanged();
        return true;
    }

    private readonly struct IsolatedChildBonePose
    {
        public readonly int BoneIndex;
        public readonly Vector2 WorldPosition;
        public readonly Quaternion WorldRotation;

        public IsolatedChildBonePose(int boneIndex, Vector2 worldPosition, Quaternion worldRotation)
        {
            BoneIndex = boneIndex;
            WorldPosition = worldPosition;
            WorldRotation = worldRotation;
        }
    }

    private List<IsolatedChildBonePose> CaptureIsolatedChildBonePoses(int modifiedBoneIndex)
    {
        if (!isolateBoneModificationForCurrentDraw || bonesTreatAsWorldSpace)
            return null;
        if (modifiedBoneIndex < 0 || modifiedBoneIndex >= bones.Count)
            return null;

        List<IsolatedChildBonePose> poses = null;
        for (var i = 0; i < bones.Count; i++)
        {
            var child = bones[i];
            if (child == null || child.parentId != modifiedBoneIndex)
                continue;

            poses ??= new List<IsolatedChildBonePose>();
            poses.Add(new IsolatedChildBonePose(i, child.worldPosition, child.worldRotation));
        }

        return poses;
    }

    private void RecomputeBoneWorldTransformsPreservingIsolatedChildren(List<IsolatedChildBonePose> isolatedChildren)
    {
        RecomputeBoneWorldTransforms();
        if (isolatedChildren == null || isolatedChildren.Count == 0)
            return;

        for (var i = 0; i < isolatedChildren.Count; i++)
        {
            var pose = isolatedChildren[i];
            if (pose.BoneIndex < 0 || pose.BoneIndex >= bones.Count)
                continue;

            var child = bones[pose.BoneIndex];
            if (child == null)
                continue;

            child.position = ConvertBoneWorldPositionToLocal(child.parentId, pose.WorldPosition);
            child.rotation = ConvertBoneWorldRotationToLocal(child.parentId, pose.WorldRotation);
            bones[pose.BoneIndex] = child;
        }

        RecomputeBoneWorldTransforms();
    }

    public bool SetBoneLocalPosition(int boneIndex, Vector2 position)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        if ((bone.position - position).sqrMagnitude <= 1e-6f)
            return false;

        var isolatedChildren = CaptureIsolatedChildBonePoses(boneIndex);
        RegisterUndo("Set Bone Local Position");
        bone.position = position;
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    public bool SetBoneWorldPosition(int boneIndex, Vector2 worldPosition)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        if ((bone.worldPosition - worldPosition).sqrMagnitude <= 1e-6f)
            return false;

        var isolatedChildren = CaptureIsolatedChildBonePoses(boneIndex);
        RegisterUndo("Set Bone World Position");
        bone.position = ConvertBoneWorldPositionToLocal(bone, worldPosition);
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    public bool SetBoneLocalRotationAngle(int boneIndex, float angleDegrees)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        var nextRotation = Quaternion.AngleAxis(angleDegrees, Vector3.forward);
        if (Quaternion.Angle(bone.rotation, nextRotation) <= 0.001f)
            return false;

        var isolatedChildren = CaptureIsolatedChildBonePoses(boneIndex);
        RegisterUndo("Set Bone Local Rotation");
        bone.rotation = nextRotation;
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    public bool SetBoneWorldRotationAngle(int boneIndex, float angleDegrees)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        var nextWorldRotation = Quaternion.AngleAxis(angleDegrees, Vector3.forward);
        if (Quaternion.Angle(bone.worldRotation, nextWorldRotation) <= 0.001f)
            return false;

        var isolatedChildren = CaptureIsolatedChildBonePoses(boneIndex);
        RegisterUndo("Set Bone World Rotation");
        bone.rotation = ConvertBoneWorldRotationToLocal(bone, nextWorldRotation);
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    public bool SetBoneLength(int boneIndex, float length)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        length = Mathf.Max(0.01f, length);
        var bone = bones[boneIndex];
        if (Mathf.Abs(bone.length - length) <= 0.0001f)
            return false;

        var isolatedChildren = CaptureIsolatedChildBonePoses(boneIndex);
        RegisterUndo("Set Bone Length");
        bone.length = length;
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    public bool SetBoneParent(int boneIndex, int parentIndex)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        parentIndex = parentIndex >= 0 && parentIndex < bones.Count ? parentIndex : -1;
        if (parentIndex == boneIndex || IsBoneDescendant(parentIndex, boneIndex))
            return false;

        var bone = bones[boneIndex];
        if (bone.parentId == parentIndex)
            return false;

        var worldPosition = bone.worldPosition;
        var worldRotation = bone.worldRotation;

        RegisterUndo("Set Bone Parent");
        bone.parentId = parentIndex;
        bone.position = ConvertBoneWorldPositionToLocal(parentIndex, worldPosition);
        bone.rotation = ConvertBoneWorldRotationToLocal(parentIndex, worldRotation);
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransforms();
        BuildBoneDrawOrder();
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    public bool MoveBoneToParentTip(int boneIndex)
    {
        if (!TryGetBoneParent(boneIndex, out var bone, out var parentIndex, out _))
            return false;

        var parentTip = GetBoneEnd(parentIndex);
        if ((bone.worldPosition - parentTip).sqrMagnitude <= 1e-6f)
            return false;

        var isolatedChildren = CaptureIsolatedChildBonePoses(boneIndex);
        RegisterUndo("Move Bone To Parent Tip");
        bone.position = ConvertBoneWorldPositionToLocal(parentIndex, parentTip);
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    public bool RotateBoneToParentRotation(int boneIndex)
    {
        if (!TryGetBoneParent(boneIndex, out var bone, out var parentIndex, out var parent))
            return false;

        if (Quaternion.Angle(bone.worldRotation, parent.worldRotation) <= 0.001f)
            return false;

        var isolatedChildren = CaptureIsolatedChildBonePoses(boneIndex);
        RegisterUndo("Rotate Bone To Parent Rotation");
        bone.rotation = ConvertBoneWorldRotationToLocal(parentIndex, parent.worldRotation);
        bones[boneIndex] = bone;
        RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);
        InvalidateBonePose();
        MarkBoneStateChanged();
        return true;
    }

    private Vector2 ConvertBoneWorldPositionToLocal(BoneInfo bone, Vector2 worldPosition)
    {
        if (bone == null)
            return worldPosition;
        return ConvertBoneWorldPositionToLocal(bone.parentId, worldPosition);
    }

    private Vector2 ConvertBoneWorldPositionToLocal(int parentIndex, Vector2 worldPosition)
    {
        if (bonesTreatAsWorldSpace || parentIndex < 0 || parentIndex >= bones.Count)
            return worldPosition;

        var parent = bones[parentIndex];
        var offset = new Vector3(worldPosition.x - parent.worldPosition.x, worldPosition.y - parent.worldPosition.y, 0f);
        if (rotateLocalBoneOffsetsByParent)
            offset = Quaternion.Inverse(parent.worldRotation) * offset;

        return new Vector2(offset.x, offset.y);
    }

    private Quaternion ConvertBoneWorldRotationToLocal(BoneInfo bone, Quaternion worldRotation)
    {
        if (bone == null)
            return worldRotation;
        return ConvertBoneWorldRotationToLocal(bone.parentId, worldRotation);
    }

    private Quaternion ConvertBoneWorldRotationToLocal(int parentIndex, Quaternion worldRotation)
    {
        if (bonesTreatAsWorldSpace || parentIndex < 0 || parentIndex >= bones.Count)
            return worldRotation;

        return Quaternion.Inverse(bones[parentIndex].worldRotation) * worldRotation;
    }

    private bool TryGetBoneParent(int boneIndex, out BoneInfo bone, out int parentIndex, out BoneInfo parent)
    {
        bone = null;
        parentIndex = -1;
        parent = null;
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        bone = bones[boneIndex];
        parentIndex = bone != null ? bone.parentId : -1;
        if (parentIndex < 0 || parentIndex >= bones.Count)
            return false;

        parent = bones[parentIndex];
        return parent != null;
    }

    private bool IsBoneDescendant(int candidateIndex, int ancestorIndex)
    {
        if (candidateIndex < 0 || ancestorIndex < 0 || bones == null)
            return false;

        var guard = 0;
        var current = candidateIndex;
        while (current >= 0 && current < bones.Count && guard++ <= bones.Count)
        {
            if (current == ancestorIndex)
                return true;

            var bone = bones[current];
            current = bone != null ? bone.parentId : -1;
        }

        return false;
    }

    internal bool TryAutosaveBoneChanges(out string error)
    {
        error = "Bone autosave is disabled. Use top Apply to persist Reanimator changes.";
        return false;
    }

    private void MarkBoneStateChanged()
    {
        hasPendingChanges = true;
    }

    public bool FlushPendingBoneAutosave()
    {
        if (!boneTransformChangedDuringDrag)
            return false;

        boneTransformChangedDuringDrag = false;
        MarkBoneStateChanged();
        return true;
    }

}
#endif
