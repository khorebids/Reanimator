#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

public partial class PsdLayerPreviewRenderer
{
    private void TryBuildSkinningData(
        Sprite sprite,
        LayerInfo layer,
        int vertexCount,
        out BoneWeight[] boneWeights,
        out int[] spriteBoneToRendererBone,
        out Matrix4x4[] bindBoneInverseMatrices,
        out bool usesSpriteBindPoses)
    {
        boneWeights = null;
        spriteBoneToRendererBone = null;
        bindBoneInverseMatrices = null;
        usesSpriteBindPoses = false;

        if (sprite == null || layer == null || vertexCount <= 0)
            return;

        try
        {
            var weightSlice = sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);
            if (weightSlice.Length != vertexCount)
                return;

            var spriteBones = sprite.GetBones();
            if (spriteBones == null || spriteBones.Length == 0)
                return;

            boneWeights = new BoneWeight[vertexCount];
            for (var i = 0; i < vertexCount; i++)
                boneWeights[i] = weightSlice[i];

            spriteBoneToRendererBone = BuildSpriteBoneToRendererBoneMap(spriteBones);
            var spriteBindPoses = sprite.GetBindPoses();
            if (spriteBindPoses.Length == spriteBones.Length)
            {
                bindBoneInverseMatrices = new Matrix4x4[spriteBindPoses.Length];
                for (var i = 0; i < spriteBindPoses.Length; i++)
                    bindBoneInverseMatrices[i] = spriteBindPoses[i];
                usesSpriteBindPoses = true;
            }
            else
            {
                bindBoneInverseMatrices = BuildBindBoneInverseMatrices(spriteBoneToRendererBone);
            }
        }
        catch
        {
            boneWeights = null;
            spriteBoneToRendererBone = null;
            bindBoneInverseMatrices = null;
            usesSpriteBindPoses = false;
        }
    }

    private bool TryBuildImporterSkinningData(
        IReadOnlyList<BoneWeight> boneWeights,
        IReadOnlyList<SpriteBone> spriteBones,
        out int[] spriteBoneToRendererBone,
        out Matrix4x4[] bindBoneInverseMatrices)
    {
        spriteBoneToRendererBone = null;
        bindBoneInverseMatrices = null;
        if (boneWeights == null || boneWeights.Count == 0 || bones == null || bones.Count == 0)
            return false;

        var maxBoneIndex = -1;
        for (var i = 0; i < boneWeights.Count; i++)
        {
            var weight = boneWeights[i];
            RegisterWeightedBoneIndex(weight.boneIndex0, weight.weight0, ref maxBoneIndex);
            RegisterWeightedBoneIndex(weight.boneIndex1, weight.weight1, ref maxBoneIndex);
            RegisterWeightedBoneIndex(weight.boneIndex2, weight.weight2, ref maxBoneIndex);
            RegisterWeightedBoneIndex(weight.boneIndex3, weight.weight3, ref maxBoneIndex);
        }

        if (maxBoneIndex < 0)
            return false;

        if (spriteBones != null && spriteBones.Count > 0)
        {
            spriteBoneToRendererBone = BuildSpriteBoneToRendererBoneMap(ToSpriteBoneArray(spriteBones));
            if (HasMappedRendererBone(spriteBoneToRendererBone) && maxBoneIndex < spriteBoneToRendererBone.Length)
            {
                bindBoneInverseMatrices = BuildBindBoneInverseMatrices(spriteBoneToRendererBone);
                return bindBoneInverseMatrices != null &&
                       bindBoneInverseMatrices.Length == spriteBoneToRendererBone.Length;
            }
        }

        spriteBoneToRendererBone = BuildIndexOrderSpriteBoneMap(maxBoneIndex);
        if (spriteBoneToRendererBone == null || spriteBoneToRendererBone.Length == 0)
            return false;

        bindBoneInverseMatrices = BuildBindBoneInverseMatrices(spriteBoneToRendererBone);
        return bindBoneInverseMatrices != null && bindBoneInverseMatrices.Length == spriteBoneToRendererBone.Length;
    }

    internal static bool ShouldPreferImporterPreviewSkinning(
        bool builtFromImporter,
        IReadOnlyList<BoneWeight> importerBoneWeights,
        int vertexCount)
    {
        if (!builtFromImporter ||
            importerBoneWeights == null ||
            vertexCount <= 0 ||
            importerBoneWeights.Count != vertexCount)
        {
            return false;
        }

        for (var i = 0; i < importerBoneWeights.Count; i++)
        {
            var weight = importerBoneWeights[i];
            if (HasPositiveWeight(weight.weight0, weight.boneIndex0) ||
                HasPositiveWeight(weight.weight1, weight.boneIndex1) ||
                HasPositiveWeight(weight.weight2, weight.boneIndex2) ||
                HasPositiveWeight(weight.weight3, weight.boneIndex3))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPositiveWeight(float weight, int boneIndex)
    {
        return boneIndex >= 0 && weight > 0.0001f;
    }

    private int[] BuildIndexOrderSpriteBoneMap(int maxBoneIndex)
    {
        if (maxBoneIndex < 0 || bones == null || bones.Count == 0)
            return Array.Empty<int>();

        var count = Mathf.Min(maxBoneIndex + 1, bones.Count);
        if (count <= 0)
            return Array.Empty<int>();

        var result = new int[count];
        for (var i = 0; i < count; i++)
            result[i] = i;
        return result;
    }

    private static List<SpriteBone> TryGetSpriteBones(ISpriteBoneDataProvider boneProvider, LayerSpriteBinding binding)
    {
        if (boneProvider == null)
            return null;

        try
        {
            var spriteBones = boneProvider.GetBones(binding.spriteId);
            return spriteBones != null && spriteBones.Count > 0 ? spriteBones : null;
        }
        catch
        {
            return null;
        }
    }

    private static SpriteBone[] ToSpriteBoneArray(IReadOnlyList<SpriteBone> source)
    {
        if (source == null || source.Count == 0)
            return Array.Empty<SpriteBone>();

        var result = new SpriteBone[source.Count];
        for (var i = 0; i < source.Count; i++)
            result[i] = source[i];
        return result;
    }

    private static bool HasMappedRendererBone(IReadOnlyList<int> spriteBoneToRendererBone)
    {
        if (spriteBoneToRendererBone == null)
            return false;

        for (var i = 0; i < spriteBoneToRendererBone.Count; i++)
        {
            if (spriteBoneToRendererBone[i] >= 0)
                return true;
        }

        return false;
    }

    private static void RegisterWeightedBoneIndex(int boneIndex, float weight, ref int maxBoneIndex)
    {
        if (weight <= 0.0001f || boneIndex < 0)
            return;

        maxBoneIndex = Mathf.Max(maxBoneIndex, boneIndex);
    }

    private Matrix4x4[] BuildBindBoneInverseMatrices(int[] spriteBoneToRendererBone)
    {
        if (spriteBoneToRendererBone == null || spriteBoneToRendererBone.Length == 0)
            return Array.Empty<Matrix4x4>();

        return ReanimatorSkinningAlgorithms.BuildRendererBindBoneInverseMatrices(bones, spriteBoneToRendererBone);
    }

    private int[] BuildSpriteBoneToRendererBoneMap(SpriteBone[] spriteBones)
    {
        if (spriteBones == null || spriteBones.Length == 0)
            return Array.Empty<int>();

        var map = new int[spriteBones.Length];
        for (var i = 0; i < spriteBones.Length; i++)
            map[i] = -1;

        if (bones.Count == 0)
            return map;

        var used = new bool[bones.Count];

        // First pass: strict match by name and parent relation.
        for (var i = 0; i < spriteBones.Length; i++)
        {
            var spriteBone = spriteBones[i];
            var best = -1;
            for (var j = 0; j < bones.Count; j++)
            {
                if (used[j])
                    continue;
                if (!string.Equals(spriteBone.name, bones[j].name, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Prefer candidates with matching root/non-root relation.
                var spriteIsRoot = spriteBone.parentId < 0;
                var rendererIsRoot = bones[j].parentId < 0;
                if (spriteIsRoot != rendererIsRoot)
                    continue;

                best = j;
                break;
            }

            if (best >= 0)
            {
                map[i] = best;
                used[best] = true;
            }
        }

        // Second pass: relaxed match by name only.
        for (var i = 0; i < spriteBones.Length; i++)
        {
            if (map[i] >= 0)
                continue;

            var spriteBone = spriteBones[i];
            for (var j = 0; j < bones.Count; j++)
            {
                if (used[j])
                    continue;
                if (!string.Equals(spriteBone.name, bones[j].name, StringComparison.OrdinalIgnoreCase))
                    continue;
                map[i] = j;
                used[j] = true;
                break;
            }
        }

        ReconcileSpriteBoneMapWithParentLinks(spriteBones, map, used);

        // PSD importer often serializes unnamed bones as "bone_<renderer index>".
        // Prefer that stable index before falling back to array order.
        for (var i = 0; i < spriteBones.Length; i++)
        {
            if (map[i] >= 0)
                continue;
            if (!TryParseGeneratedRendererBoneName(spriteBones[i].name, out var rendererIndex))
                continue;
            if (rendererIndex < 0 || rendererIndex >= bones.Count || used[rendererIndex])
                continue;

            map[i] = rendererIndex;
            used[rendererIndex] = true;
        }

        // Final fallback by index for unresolved entries.
        for (var i = 0; i < spriteBones.Length; i++)
        {
            if (map[i] >= 0)
                continue;
            if (i < bones.Count && !used[i])
            {
                map[i] = i;
                used[i] = true;
            }
        }

        return map;
    }

    private void ReconcileSpriteBoneMapWithParentLinks(
        IReadOnlyList<SpriteBone> spriteBones,
        int[] map,
        bool[] used)
    {
        if (spriteBones == null || map == null || used == null || bones == null || bones.Count == 0)
            return;

        var changed = true;
        var guard = 0;
        while (changed && guard++ <= map.Length + 1)
        {
            changed = false;
            for (var i = 0; i < spriteBones.Count && i < map.Length; i++)
            {
                var spriteBone = spriteBones[i];
                if (spriteBone.parentId < 0 || spriteBone.parentId >= map.Length)
                    continue;

                var parentRendererBone = map[spriteBone.parentId];
                if (parentRendererBone < 0 || parentRendererBone >= bones.Count)
                    continue;

                var mappedRendererBone = map[i];
                // Entries reaching this pass with a mapping were resolved by an
                // exact bone-name match. A sprite is allowed to omit intermediate
                // character bones (body sprites intentionally skip boobs_root,
                // and secondary-arm sprites can skip their attachment chain), so
                // its local parentId is not authoritative enough to replace that
                // exact match. The old reconciliation did exactly that after an
                // Apply/Rebuild and silently turned nipple_l/r or *_arm_2 bones
                // into an unrelated direct child of the sprite parent.
                if (mappedRendererBone >= 0)
                    continue;

                if (!TryFindRendererChildForSpriteBone(spriteBone, parentRendererBone, used, out var childRendererBone))
                    continue;

                map[i] = childRendererBone;
                used[childRendererBone] = true;
                changed = true;
            }
        }
    }

    private bool TryFindRendererChildForSpriteBone(
        SpriteBone spriteBone,
        int parentRendererBone,
        IReadOnlyList<bool> used,
        out int childRendererBone)
    {
        childRendererBone = -1;
        if (bones == null || parentRendererBone < 0 || parentRendererBone >= bones.Count)
            return false;

        var best = -1;
        var bestScore = float.PositiveInfinity;
        for (var i = 0; i < bones.Count; i++)
        {
            if (i < used.Count && used[i])
                continue;

            var candidate = bones[i];
            if (candidate == null || candidate.parentId != parentRendererBone)
                continue;

            var score = Mathf.Abs(candidate.length - spriteBone.length);
            if (!string.IsNullOrWhiteSpace(spriteBone.name) &&
                string.Equals(spriteBone.name, candidate.name, StringComparison.OrdinalIgnoreCase))
            {
                score -= 100000f;
            }

            if (score >= bestScore)
                continue;

            best = i;
            bestScore = score;
        }

        if (best < 0)
            return false;

        childRendererBone = best;
        return true;
    }

    private static bool TryParseGeneratedRendererBoneName(string name, out int rendererIndex)
    {
        rendererIndex = -1;
        const string prefix = "bone_";
        if (string.IsNullOrWhiteSpace(name) ||
            !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(name.Substring(prefix.Length), out rendererIndex);
    }
}
#endif
