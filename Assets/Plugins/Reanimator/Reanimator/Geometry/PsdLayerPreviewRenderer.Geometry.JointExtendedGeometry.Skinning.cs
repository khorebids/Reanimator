#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private bool TryResolveJointInfluenceSpriteBones(
        LayerVertexData data,
        Vector2 centerBaseDoc,
        out int parentSpriteBone,
        out int nearestSpriteBone,
        bool logDebug = false)
    {
        parentSpriteBone = -1;
        nearestSpriteBone = -1;
        if (data == null || data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
        {
            if (logDebug)
                LogJointApplyWarning("Resolve failed: spriteBoneToRendererBone is missing");
            return false;
        }
        if (bones == null || bones.Count == 0)
        {
            if (logDebug)
                LogJointApplyWarning("Resolve failed: bones list is empty");
            return false;
        }

        var nearestRendererBone =
            jointNearestBoneIndex >= 0 && jointNearestBoneIndex < bones.Count && bones[jointNearestBoneIndex] != null
                ? jointNearestBoneIndex
                : FindNearestBoneIndex(centerBaseDoc);

        if (logDebug)
            LogJointApplyDebug($"Resolve start center={FormatJointVector(centerBaseDoc)} cachedNearest={jointNearestBoneIndex} nearestRenderer={DescribeJointRendererBone(nearestRendererBone)}");

        if (nearestRendererBone < 0 || nearestRendererBone >= bones.Count || bones[nearestRendererBone] == null)
        {
            if (logDebug)
                LogJointApplyWarning($"Resolve failed: nearest renderer bone is invalid ({nearestRendererBone})");
            return false;
        }

        var parentRendererBone = bones[nearestRendererBone].parentId;
        if (parentRendererBone < 0 || parentRendererBone >= bones.Count || bones[parentRendererBone] == null)
        {
            if (logDebug)
                LogJointApplyWarning($"Resolve failed: nearest renderer parent is invalid for {DescribeJointRendererBone(nearestRendererBone)}");
            return false;
        }

        if (logDebug)
            LogJointApplyDebug($"Resolve using exact pair nearest={DescribeJointRendererBone(nearestRendererBone)} parent={DescribeJointRendererBone(parentRendererBone)}");

        if (!TryEnsureRendererBoneMappedToLayerSkinning(data, nearestRendererBone, out nearestSpriteBone, logDebug, "nearest"))
            return false;
        if (!TryEnsureRendererBoneMappedToLayerSkinning(data, parentRendererBone, out parentSpriteBone, logDebug, "parent"))
            return false;

        if (parentSpriteBone == nearestSpriteBone)
        {
            if (logDebug)
                LogJointApplyError($"Resolve failed: exact parent and nearest collapsed into the same sprite bone {DescribeJointSpriteBone(parentSpriteBone, data.spriteBoneToRendererBone)}");
            return false;
        }

        if (logDebug)
            LogJointApplyDebug($"Resolve success parent={DescribeJointSpriteBone(parentSpriteBone, data.spriteBoneToRendererBone)} nearest={DescribeJointSpriteBone(nearestSpriteBone, data.spriteBoneToRendererBone)}");

        return true;
    }

    private bool TryEnsureRendererBoneMappedToLayerSkinning(
        LayerVertexData data,
        int rendererBoneIndex,
        out int spriteBoneIndex,
        bool logDebug,
        string role)
    {
        spriteBoneIndex = -1;
        if (data == null)
        {
            if (logDebug)
                LogJointApplyWarning($"Resolve failed: layer data is missing while ensuring {role} mapping");
            return false;
        }
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count || bones[rendererBoneIndex] == null)
        {
            if (logDebug)
                LogJointApplyWarning($"Resolve failed: renderer bone for {role} is invalid ({rendererBoneIndex})");
            return false;
        }
        if (data.spriteBoneToRendererBone == null)
        {
            if (logDebug)
                LogJointApplyWarning($"Resolve failed: spriteBoneToRendererBone is missing while ensuring {role} mapping");
            return false;
        }

        var rendererToSprite = BuildRendererToSpriteBoneMap(data.spriteBoneToRendererBone);
        if (rendererToSprite.TryGetValue(rendererBoneIndex, out spriteBoneIndex))
        {
            if (logDebug)
                LogJointApplyDebug($"Resolved {role} sprite directly: {DescribeJointSpriteBone(spriteBoneIndex, data.spriteBoneToRendererBone)}");
            return true;
        }

        var sourceMap = data.spriteBoneToRendererBone;
        var expandedMap = new int[sourceMap.Length + 1];
        Array.Copy(sourceMap, expandedMap, sourceMap.Length);
        spriteBoneIndex = sourceMap.Length;
        expandedMap[spriteBoneIndex] = rendererBoneIndex;

        var expandedBind = new Matrix4x4[expandedMap.Length];
        var existingBind = data.bindBoneInverseMatrices;
        if (existingBind != null && existingBind.Length == sourceMap.Length)
        {
            Array.Copy(existingBind, expandedBind, existingBind.Length);
        }
        else
        {
            var rebuiltExistingBind = BuildBindBoneInverseMatrices(sourceMap);
            if (rebuiltExistingBind != null)
                Array.Copy(rebuiltExistingBind, expandedBind, Mathf.Min(rebuiltExistingBind.Length, sourceMap.Length));
        }

        if (!TryBuildBindInverseMatrixForRendererBone(data, rendererBoneIndex, out expandedBind[spriteBoneIndex]))
        {
            if (logDebug)
                LogJointApplyWarning($"Resolve failed: could not build bind inverse for {role} {DescribeJointRendererBone(rendererBoneIndex)}");
            return false;
        }

        data.spriteBoneToRendererBone = expandedMap;
        data.bindBoneInverseMatrices = expandedBind;
        data.skinMatrices = null;
        data.deformedVertices = null;
        data.lastPoseVersion = -1;
        EnsureBoneWeightsArraySize(data, data.vertices != null ? data.vertices.Length : 0);
        data.hasSkinning = data.bindBoneInverseMatrices != null &&
                          data.boneWeights != null &&
                          data.boneWeights.Length == (data.vertices != null ? data.vertices.Length : 0) &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;

        if (logDebug)
            LogJointApplyDebug($"Extended layer skinning for {role}: added {DescribeJointRendererBone(rendererBoneIndex)} as sprite[{spriteBoneIndex}] without rebuilding existing bind matrices");

        return true;
    }

    private bool TryBuildBindInverseMatrixForRendererBone(LayerVertexData data, int rendererBoneIndex, out Matrix4x4 bindInverseMatrix)
    {
        bindInverseMatrix = Matrix4x4.identity;
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count || bones[rendererBoneIndex] == null)
            return false;

        var bindPosition = bones[rendererBoneIndex].worldPosition;
        var bindRotation = bones[rendererBoneIndex].worldRotation;
        var bindBones = CloneBones(bones);
        if (bindBones != null && bindBones.Count == bones.Count)
        {
            for (var i = 0; i < bindBones.Count; i++)
            {
                var bone = bindBones[i];
                bone.position = bone.initialPosition;
                if (!bone.initialRotation.Equals(default(Quaternion)))
                    bone.rotation = bone.initialRotation;
                bindBones[i] = bone;
            }

            var bindTransform = new BonePreviewTransform(bonesTreatAsWorldSpace, rotateLocalBoneOffsetsByParent);
            bindTransform.ResolveAll(bindBones);
            var bindBone = bindBones[rendererBoneIndex];
            bindPosition = bindBone.worldPosition;
            bindRotation = bindBone.worldRotation;
        }

        bindRotation = NormalizeBindRotation(bindRotation);
        if (data != null && data.usesSpriteBindPoses)
            bindPosition = DocumentToSpriteSkinPosition(data, bindPosition);

        var bindMatrix = Matrix4x4.TRS(
            new Vector3(bindPosition.x, bindPosition.y, 0f),
            bindRotation,
            Vector3.one);
        bindInverseMatrix = bindMatrix.inverse;
        return true;
    }

    private static Quaternion NormalizeBindRotation(Quaternion rotation)
    {
        var lengthSqr = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        if (lengthSqr <= 0.000001f)
            return Quaternion.identity;

        return rotation;
    }

    private bool TryResolveMappedSpriteBoneAlongAncestors(
        int rendererBoneIndex,
        IReadOnlyDictionary<int, int> rendererToSprite,
        out int spriteBoneIndex,
        out int mappedRendererBoneIndex)
    {
        spriteBoneIndex = -1;
        mappedRendererBoneIndex = -1;
        if (rendererToSprite == null || bones == null || bones.Count == 0)
            return false;
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            return false;

        var probe = rendererBoneIndex;
        var guard = 0;
        while (probe >= 0 && probe < bones.Count && guard++ <= bones.Count + 1)
        {
            if (rendererToSprite.TryGetValue(probe, out var mappedSpriteBone))
            {
                spriteBoneIndex = mappedSpriteBone;
                mappedRendererBoneIndex = probe;
                return true;
            }

            var probeBone = bones[probe];
            probe = probeBone != null ? probeBone.parentId : -1;
        }

        return false;
    }

    private bool TryResolveDistinctAncestorSpriteBone(
        int rendererBoneIndex,
        int excludedSpriteBone,
        IReadOnlyDictionary<int, int> rendererToSprite,
        out int spriteBoneIndex,
        out int mappedRendererBoneIndex)
    {
        spriteBoneIndex = -1;
        mappedRendererBoneIndex = -1;
        if (rendererToSprite == null || bones == null || bones.Count == 0)
            return false;
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            return false;

        var probe = bones[rendererBoneIndex] != null ? bones[rendererBoneIndex].parentId : -1;
        var guard = 0;
        while (probe >= 0 && probe < bones.Count && guard++ <= bones.Count + 1)
        {
            if (rendererToSprite.TryGetValue(probe, out var mappedSpriteBone) && mappedSpriteBone != excludedSpriteBone)
            {
                spriteBoneIndex = mappedSpriteBone;
                mappedRendererBoneIndex = probe;
                return true;
            }

            var probeBone = bones[probe];
            probe = probeBone != null ? probeBone.parentId : -1;
        }

        return false;
    }

    private bool TryResolveNearestMappedSpriteBoneInDescendants(
        int rendererBoneIndex,
        Vector2 anchor,
        IReadOnlyDictionary<int, int> rendererToSprite,
        out int spriteBoneIndex,
        out int mappedRendererBoneIndex)
    {
        spriteBoneIndex = -1;
        mappedRendererBoneIndex = -1;
        if (rendererToSprite == null || bones == null || bones.Count == 0)
            return false;
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            return false;

        var bestVisibleDistanceSqr = float.MaxValue;
        var bestVisibleRendererBone = -1;
        var bestVisibleSpriteBone = -1;
        var bestAnyDistanceSqr = float.MaxValue;
        var bestAnyRendererBone = -1;
        var bestAnySpriteBone = -1;
        for (var i = 0; i < bones.Count; i++)
        {
            if (i == rendererBoneIndex)
                continue;
            if (!IsDescendantRendererBone(i, rendererBoneIndex))
                continue;
            if (!rendererToSprite.TryGetValue(i, out var mappedSpriteBone))
                continue;

            var bone = bones[i];
            if (bone == null)
                continue;

            var distanceSqr = (GetBoneBasePoint(bone) - anchor).sqrMagnitude;
            if (distanceSqr < bestAnyDistanceSqr)
            {
                bestAnyDistanceSqr = distanceSqr;
                bestAnyRendererBone = i;
                bestAnySpriteBone = mappedSpriteBone;
            }

            if (!bone.visible || distanceSqr >= bestVisibleDistanceSqr)
                continue;

            bestVisibleDistanceSqr = distanceSqr;
            bestVisibleRendererBone = i;
            bestVisibleSpriteBone = mappedSpriteBone;
        }

        if (bestVisibleRendererBone >= 0)
        {
            mappedRendererBoneIndex = bestVisibleRendererBone;
            spriteBoneIndex = bestVisibleSpriteBone;
            return true;
        }

        if (bestAnyRendererBone >= 0)
        {
            mappedRendererBoneIndex = bestAnyRendererBone;
            spriteBoneIndex = bestAnySpriteBone;
            return true;
        }

        return false;
    }

    private bool IsDescendantRendererBone(int rendererBoneIndex, int ancestorRendererBoneIndex)
    {
        if (bones == null || rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            return false;
        if (ancestorRendererBoneIndex < 0 || ancestorRendererBoneIndex >= bones.Count)
            return false;

        var probe = bones[rendererBoneIndex] != null ? bones[rendererBoneIndex].parentId : -1;
        var guard = 0;
        while (probe >= 0 && probe < bones.Count && guard++ <= bones.Count + 1)
        {
            if (probe == ancestorRendererBoneIndex)
                return true;
            var probeBone = bones[probe];
            probe = probeBone != null ? probeBone.parentId : -1;
        }

        return false;
    }

    private bool TryResolveNearestDistinctMappedSpriteBone(
        Vector2 anchor,
        int excludedSpriteBone,
        IReadOnlyDictionary<int, int> rendererToSprite,
        out int spriteBoneIndex,
        out int rendererBoneIndex)
    {
        spriteBoneIndex = -1;
        rendererBoneIndex = -1;
        if (rendererToSprite == null || bones == null || bones.Count == 0)
            return false;

        var bestVisibleDistanceSqr = float.MaxValue;
        var bestVisibleRendererBone = -1;
        var bestVisibleSpriteBone = -1;
        var bestAnyDistanceSqr = float.MaxValue;
        var bestAnyRendererBone = -1;
        var bestAnySpriteBone = -1;
        for (var i = 0; i < bones.Count; i++)
        {
            if (!rendererToSprite.TryGetValue(i, out var mappedSpriteBone))
                continue;
            if (mappedSpriteBone == excludedSpriteBone)
                continue;

            var bone = bones[i];
            if (bone == null)
                continue;

            var distanceSqr = (GetBoneBasePoint(bone) - anchor).sqrMagnitude;
            if (distanceSqr < bestAnyDistanceSqr)
            {
                bestAnyDistanceSqr = distanceSqr;
                bestAnyRendererBone = i;
                bestAnySpriteBone = mappedSpriteBone;
            }

            if (!bone.visible || distanceSqr >= bestVisibleDistanceSqr)
                continue;

            bestVisibleDistanceSqr = distanceSqr;
            bestVisibleRendererBone = i;
            bestVisibleSpriteBone = mappedSpriteBone;
        }

        if (bestVisibleRendererBone >= 0)
        {
            rendererBoneIndex = bestVisibleRendererBone;
            spriteBoneIndex = bestVisibleSpriteBone;
            return true;
        }

        if (bestAnyRendererBone >= 0)
        {
            rendererBoneIndex = bestAnyRendererBone;
            spriteBoneIndex = bestAnySpriteBone;
            return true;
        }

        return false;
    }
}
#endif
