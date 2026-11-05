#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

public partial class PsdLayerPreviewRenderer
{
    private BoneWeight[] BuildAutoGeometryWeights(Vector2[] docVertices, LayerVertexData sourceData, bool generateWeights)
    {
        if (docVertices == null || docVertices.Length == 0)
            return Array.Empty<BoneWeight>();

        var result = new BoneWeight[docVertices.Length];
        for (var i = 0; i < docVertices.Length; i++)
        {
            var point = docVertices[i];
            if (generateWeights && TryBuildWeightFromBones(point, sourceData, out var autoWeight))
            {
                result[i] = autoWeight;
                continue;
            }

            if (TryTransferWeightFromSource(point, sourceData, false, out var sourceWeight))
            {
                result[i] = sourceWeight;
                continue;
            }

            result[i] = default;
        }

        return result;
    }

    private bool TryBuildWeightFromBones(Vector2 pointDoc, LayerVertexData sourceData, out BoneWeight weight)
    {
        weight = default;
        if (sourceData == null || sourceData.spriteBoneToRendererBone == null || sourceData.spriteBoneToRendererBone.Length == 0 || bones == null || bones.Count == 0)
            return false;

        var rendererToSprite = BuildRendererToSpriteBoneMap(sourceData.spriteBoneToRendererBone);
        if (rendererToSprite.Count == 0)
            return false;

        var topSprite = new int[4] { -1, -1, -1, -1 };
        var topRawWeights = new float[4];
        for (var i = 0; i < sourceData.spriteBoneToRendererBone.Length; i++)
        {
            var rendererBoneIndex = sourceData.spriteBoneToRendererBone[i];
            if (!CanUseAutoWeightRendererBone(rendererBoneIndex))
                continue;
            if (!rendererToSprite.TryGetValue(rendererBoneIndex, out var spriteBoneIndex))
                continue;

            var start = bones[rendererBoneIndex].worldPosition;
            var end = GetBoneEnd(rendererBoneIndex);
            var distance = DistancePointToSegment(pointDoc, start, end);
            var rawWeight = 1f / Mathf.Max(0.001f, distance);
            InsertTopWeight(topSprite, topRawWeights, spriteBoneIndex, rawWeight);
        }

        var sum = topRawWeights[0] + topRawWeights[1] + topRawWeights[2] + topRawWeights[3];
        if (sum <= 0.0001f)
            return false;

        var inv = 1f / sum;
        weight.boneIndex0 = topSprite[0] >= 0 ? topSprite[0] : 0;
        weight.boneIndex1 = topSprite[1] >= 0 ? topSprite[1] : 0;
        weight.boneIndex2 = topSprite[2] >= 0 ? topSprite[2] : 0;
        weight.boneIndex3 = topSprite[3] >= 0 ? topSprite[3] : 0;
        weight.weight0 = topRawWeights[0] * inv;
        weight.weight1 = topRawWeights[1] * inv;
        weight.weight2 = topRawWeights[2] * inv;
        weight.weight3 = topRawWeights[3] * inv;
        return true;
    }

    private bool TryBuildNearestBoneWeight(Vector2 pointDoc, LayerVertexData sourceData, out BoneWeight weight)
    {
        weight = default;
        if (sourceData == null || sourceData.spriteBoneToRendererBone == null || sourceData.spriteBoneToRendererBone.Length == 0 || bones == null || bones.Count == 0)
            return false;

        var bestSpriteBoneIndex = -1;
        var bestDistanceSqr = float.MaxValue;
        for (var spriteBoneIndex = 0; spriteBoneIndex < sourceData.spriteBoneToRendererBone.Length; spriteBoneIndex++)
        {
            var rendererBoneIndex = sourceData.spriteBoneToRendererBone[spriteBoneIndex];
            if (!CanUseAutoWeightRendererBone(rendererBoneIndex))
                continue;

            var start = bones[rendererBoneIndex].worldPosition;
            var end = GetBoneEnd(rendererBoneIndex);
            var closest = ClosestPointOnSegment(pointDoc, start, end);
            var distanceSqr = (pointDoc - closest).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestSpriteBoneIndex = spriteBoneIndex;
        }

        if (bestSpriteBoneIndex < 0)
            return false;

        weight = new BoneWeight
        {
            boneIndex0 = bestSpriteBoneIndex,
            boneIndex1 = bestSpriteBoneIndex,
            boneIndex2 = bestSpriteBoneIndex,
            boneIndex3 = bestSpriteBoneIndex,
            weight0 = 1f,
            weight1 = 0f,
            weight2 = 0f,
            weight3 = 0f
        };
        return true;
    }

    private static Dictionary<int, int> BuildRendererToSpriteBoneMap(IReadOnlyList<int> spriteToRenderer)
    {
        var result = new Dictionary<int, int>();
        if (spriteToRenderer == null)
            return result;

        for (var spriteIndex = 0; spriteIndex < spriteToRenderer.Count; spriteIndex++)
        {
            var rendererIndex = spriteToRenderer[spriteIndex];
            if (rendererIndex < 0)
                continue;
            if (!result.ContainsKey(rendererIndex))
                result[rendererIndex] = spriteIndex;
        }

        return result;
    }

    private static void InsertTopWeight(int[] topSprite, float[] topWeight, int spriteIndex, float rawWeight)
    {
        for (var i = 0; i < topWeight.Length; i++)
        {
            if (rawWeight <= topWeight[i])
                continue;

            for (var j = topWeight.Length - 1; j > i; j--)
            {
                topWeight[j] = topWeight[j - 1];
                topSprite[j] = topSprite[j - 1];
            }

            topWeight[i] = rawWeight;
            topSprite[i] = spriteIndex;
            break;
        }
    }

    private static bool TryTransferWeightFromSource(Vector2 pointDoc, LayerVertexData sourceData, out BoneWeight weight)
    {
        return TryTransferWeightFromSource(pointDoc, sourceData, false, out weight);
    }

    private static bool TryTransferWeightFromSource(
        Vector2 pointDoc,
        LayerVertexData sourceData,
        bool preserveZeroWeightIdentitySlots,
        out BoneWeight weight,
        bool useClosestTriangleFallback = false)
    {
        weight = default;
        if (sourceData == null || sourceData.boneWeights == null || sourceData.boneWeights.Length == 0 || sourceData.vertices == null || sourceData.vertices.Length == 0)
            return false;

        var sourceCount = Mathf.Min(sourceData.vertices.Length, sourceData.boneWeights.Length);
        if (sourceCount <= 0)
            return false;

        if (sourceData.triangles != null &&
            sourceData.triangles.Length >= 3 &&
            TryFindContainingTriangle(
                sourceData.triangles,
                sourceData.vertices,
                pointDoc,
                out _,
                out var ia,
                out var ib,
                out var ic,
                out _,
                out _,
                out _,
                out _))
        {
            if (ia >= 0 && ib >= 0 && ic >= 0 &&
                ia < sourceCount && ib < sourceCount && ic < sourceCount &&
                TryGetBarycentric(
                    pointDoc,
                    sourceData.vertices[ia],
                    sourceData.vertices[ib],
                    sourceData.vertices[ic],
                    out var wa,
                    out var wb,
                    out var wc))
            {
                weight = BlendBoneWeights(
                    sourceData.boneWeights[ia], wa,
                    sourceData.boneWeights[ib], wb,
                    sourceData.boneWeights[ic], wc,
                    preserveZeroWeightIdentitySlots ? sourceData.spriteBoneToRendererBone : null);
                return true;
            }
        }

        if (useClosestTriangleFallback &&
            TryTransferWeightFromClosestSourceTriangle(pointDoc, sourceData, sourceCount, preserveZeroWeightIdentitySlots, out weight))
            return true;

        var nearest = FindNearestVertexIndex(pointDoc, sourceData.vertices);
        if (nearest < 0 || nearest >= sourceCount)
            return false;

        weight = sourceData.boneWeights[nearest];
        return true;
    }

    private static bool TryTransferWeightFromClosestSourceTriangle(
        Vector2 pointDoc,
        LayerVertexData sourceData,
        int sourceCount,
        bool preserveZeroWeightIdentitySlots,
        out BoneWeight weight)
    {
        weight = default;
        if (sourceData == null ||
            sourceData.triangles == null ||
            sourceData.triangles.Length < 3 ||
            sourceData.vertices == null ||
            sourceData.boneWeights == null)
            return false;

        var bestPoint = pointDoc;
        var bestIa = -1;
        var bestIb = -1;
        var bestIc = -1;
        var bestDistanceSqr = float.PositiveInfinity;
        for (var i = 0; i + 2 < sourceData.triangles.Length; i += 3)
        {
            var ia = sourceData.triangles[i];
            var ib = sourceData.triangles[i + 1];
            var ic = sourceData.triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= sourceCount || ib >= sourceCount || ic >= sourceCount)
                continue;

            var a = sourceData.vertices[ia];
            var b = sourceData.vertices[ib];
            var c = sourceData.vertices[ic];
            var closest = ClosestPointOnTriangle(pointDoc, a, b, c);
            var distanceSqr = (closest - pointDoc).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestPoint = closest;
            bestIa = ia;
            bestIb = ib;
            bestIc = ic;
        }

        if (bestIa < 0)
            return false;
        if (!TryGetBarycentric(
                bestPoint,
                sourceData.vertices[bestIa],
                sourceData.vertices[bestIb],
                sourceData.vertices[bestIc],
                out var wa,
                out var wb,
                out var wc))
            return false;

        wa = Mathf.Clamp01(wa);
        wb = Mathf.Clamp01(wb);
        wc = Mathf.Clamp01(wc);
        var sum = wa + wb + wc;
        if (sum <= 0.0001f)
            return false;
        var inv = 1f / sum;

        weight = BlendBoneWeights(
            sourceData.boneWeights[bestIa], wa * inv,
            sourceData.boneWeights[bestIb], wb * inv,
            sourceData.boneWeights[bestIc], wc * inv,
            preserveZeroWeightIdentitySlots ? sourceData.spriteBoneToRendererBone : null);
        return true;
    }

    private static Vector2 ClosestPointOnTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        if (TryGetBarycentric(point, a, b, c, out var wa, out var wb, out var wc) &&
            wa >= 0f &&
            wb >= 0f &&
            wc >= 0f)
            return point;

        var ab = ClosestPointOnSegment(point, a, b);
        var bc = ClosestPointOnSegment(point, b, c);
        var ca = ClosestPointOnSegment(point, c, a);
        var abDistance = (ab - point).sqrMagnitude;
        var bcDistance = (bc - point).sqrMagnitude;
        var caDistance = (ca - point).sqrMagnitude;
        if (abDistance <= bcDistance && abDistance <= caDistance)
            return ab;
        return bcDistance <= caDistance ? bc : ca;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var denom = ab.sqrMagnitude;
        if (denom <= 1e-8f)
            return a;

        var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom);
        return a + ab * t;
    }

    private static BoneWeight BlendBoneWeights(
        BoneWeight w0, float f0,
        BoneWeight w1, float f1,
        BoneWeight w2, float f2,
        IReadOnlyList<int> spriteBoneToRendererBone = null)
    {
        var accum = new Dictionary<int, float>();
        AccumulateWeightChannel(accum, w0.boneIndex0, w0.weight0 * f0);
        AccumulateWeightChannel(accum, w0.boneIndex1, w0.weight1 * f0);
        AccumulateWeightChannel(accum, w0.boneIndex2, w0.weight2 * f0);
        AccumulateWeightChannel(accum, w0.boneIndex3, w0.weight3 * f0);
        AccumulateWeightChannel(accum, w1.boneIndex0, w1.weight0 * f1);
        AccumulateWeightChannel(accum, w1.boneIndex1, w1.weight1 * f1);
        AccumulateWeightChannel(accum, w1.boneIndex2, w1.weight2 * f1);
        AccumulateWeightChannel(accum, w1.boneIndex3, w1.weight3 * f1);
        AccumulateWeightChannel(accum, w2.boneIndex0, w2.weight0 * f2);
        AccumulateWeightChannel(accum, w2.boneIndex1, w2.weight1 * f2);
        AccumulateWeightChannel(accum, w2.boneIndex2, w2.weight2 * f2);
        AccumulateWeightChannel(accum, w2.boneIndex3, w2.weight3 * f2);

        var topBones = new int[4] { 0, 0, 0, 0 };
        var topWeights = new float[4];
        foreach (var kv in accum)
            InsertTopWeight(topBones, topWeights, kv.Key, kv.Value);

        var sum = topWeights[0] + topWeights[1] + topWeights[2] + topWeights[3];
        if (sum <= 0.0001f)
            return default;

        var inv = 1f / sum;
        var result = new BoneWeight
        {
            boneIndex0 = topBones[0],
            boneIndex1 = topBones[1],
            boneIndex2 = topBones[2],
            boneIndex3 = topBones[3],
            weight0 = topWeights[0] * inv,
            weight1 = topWeights[1] * inv,
            weight2 = topWeights[2] * inv,
            weight3 = topWeights[3] * inv
        };
        PreserveZeroWeightIdentitySlots(ref result, spriteBoneToRendererBone, w0, f0, w1, f1, w2, f2);
        return result;
    }

    private static void PreserveZeroWeightIdentitySlots(
        ref BoneWeight result,
        IReadOnlyList<int> spriteBoneToRendererBone,
        BoneWeight w0,
        float f0,
        BoneWeight w1,
        float f1,
        BoneWeight w2,
        float f2)
    {
        if (spriteBoneToRendererBone == null || spriteBoneToRendererBone.Count == 0)
            return;

        var preserved = new List<int>(4);
        AddPreservedIdentitySlots(preserved, w0, f0, spriteBoneToRendererBone);
        AddPreservedIdentitySlots(preserved, w1, f1, spriteBoneToRendererBone);
        AddPreservedIdentitySlots(preserved, w2, f2, spriteBoneToRendererBone);
        if (preserved.Count == 0)
            return;

        var indices = new[] { result.boneIndex0, result.boneIndex1, result.boneIndex2, result.boneIndex3 };
        var weights = new[] { result.weight0, result.weight1, result.weight2, result.weight3 };
        var used = new HashSet<int>();
        for (var i = 0; i < indices.Length; i++)
        {
            if (weights[i] > 0.0001f)
                used.Add(indices[i]);
        }

        for (var i = 0; i < preserved.Count; i++)
        {
            var boneIndex = preserved[i];
            if (!used.Add(boneIndex))
                continue;

            for (var slot = 0; slot < indices.Length; slot++)
            {
                if (weights[slot] > 0.0001f)
                    continue;

                indices[slot] = boneIndex;
                weights[slot] = 0f;
                break;
            }
        }

        result.boneIndex0 = indices[0];
        result.boneIndex1 = indices[1];
        result.boneIndex2 = indices[2];
        result.boneIndex3 = indices[3];
        result.weight0 = weights[0];
        result.weight1 = weights[1];
        result.weight2 = weights[2];
        result.weight3 = weights[3];
    }

    private static void AddPreservedIdentitySlots(
        List<int> result,
        BoneWeight weight,
        float factor,
        IReadOnlyList<int> spriteBoneToRendererBone)
    {
        if (result == null || result.Count >= 4 || factor <= 0.0001f)
            return;

        var active = ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(weight, spriteBoneToRendererBone);
        for (var i = 0; i < active.Count && result.Count < 4; i++)
        {
            var bone = active[i];
            if (!result.Contains(bone))
                result.Add(bone);
        }
    }

    private static void AccumulateWeightChannel(Dictionary<int, float> accum, int boneIndex, float weight)
    {
        if (accum == null || boneIndex < 0 || weight <= 0.0001f)
            return;

        if (accum.TryGetValue(boneIndex, out var sum))
            accum[boneIndex] = sum + weight;
        else
            accum.Add(boneIndex, weight);
    }

    private Dictionary<int, SpriteBone> BuildSpriteBoneTemplateMap(
        ISpriteBoneDataProvider boneProvider,
        IReadOnlyDictionary<string, LayerSpriteBinding> bindings)
    {
        var result = new Dictionary<int, SpriteBone>();
        if (boneProvider == null || bindings == null || bindings.Count == 0)
            return result;

        foreach (var kv in bindings)
        {
            List<SpriteBone> spriteBones = null;
            try
            {
                spriteBones = boneProvider.GetBones(kv.Value.spriteId);
            }
            catch
            {
                spriteBones = null;
            }

            if (spriteBones == null || spriteBones.Count == 0)
                continue;

            var map = BuildSpriteBoneToRendererBoneMap(spriteBones.ToArray());
            for (var i = 0; i < spriteBones.Count && i < map.Length; i++)
            {
                var rendererBoneIndex = map[i];
                if (rendererBoneIndex < 0 || result.ContainsKey(rendererBoneIndex))
                    continue;
                result[rendererBoneIndex] = spriteBones[i];
            }
        }

        return result;
    }

    private bool ApplyCharacterPartBonesForLayerData(
        ICharacterDataProvider characterProvider,
        LayerSpriteBinding binding,
        LayerVertexData data)
    {
        if (characterProvider == null || data == null || data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return false;

        CharacterData characterData;
        try
        {
            characterData = characterProvider.GetCharacterData();
        }
        catch
        {
            return false;
        }

        if (characterData.parts == null || characterData.parts.Length == 0)
            return false;

        var desiredBones = BuildCharacterPartBoneIndices(data.spriteBoneToRendererBone, characterData.bones);
        if (desiredBones == null || desiredBones.Length == 0)
            return false;

        var spriteId = binding.spriteId.ToString();
        for (var i = 0; i < characterData.parts.Length; i++)
        {
            var part = characterData.parts[i];
            if (!string.Equals(part.spriteId, spriteId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (AreIndicesEqual(part.bones, desiredBones))
                return false;

            part.bones = desiredBones;
            characterData.parts[i] = part;
            characterProvider.SetCharacterData(characterData);
            return true;
        }

        return false;
    }

    private bool ClearCharacterPartBonesForLayerData(
        ICharacterDataProvider characterProvider,
        LayerSpriteBinding binding)
    {
        if (characterProvider == null)
            return false;

        CharacterData characterData;
        try
        {
            characterData = characterProvider.GetCharacterData();
        }
        catch
        {
            return false;
        }

        if (characterData.parts == null || characterData.parts.Length == 0)
            return false;

        var spriteId = binding.spriteId.ToString();
        for (var i = 0; i < characterData.parts.Length; i++)
        {
            var part = characterData.parts[i];
            if (!string.Equals(part.spriteId, spriteId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (part.bones == null || part.bones.Length == 0)
                return false;

            part.bones = Array.Empty<int>();
            characterData.parts[i] = part;
            characterProvider.SetCharacterData(characterData);
            return true;
        }

        return false;
    }

    private static int[] BuildCharacterPartBoneIndices(
        IReadOnlyList<int> spriteBoneToRendererBone,
        IReadOnlyList<SpriteBone> characterBones)
    {
        if (spriteBoneToRendererBone == null || spriteBoneToRendererBone.Count == 0 || characterBones == null || characterBones.Count == 0)
            return null;

        var result = new int[spriteBoneToRendererBone.Count];
        for (var i = 0; i < spriteBoneToRendererBone.Count; i++)
        {
            var rendererBoneIndex = spriteBoneToRendererBone[i];
            if (rendererBoneIndex < 0 || rendererBoneIndex >= characterBones.Count)
                return null;
            result[i] = rendererBoneIndex;
        }

        return result;
    }

    private bool ApplySpriteBonesForLayerData(
        ISpriteBoneDataProvider boneProvider,
        LayerSpriteBinding binding,
        LayerVertexData data,
        IReadOnlyDictionary<int, SpriteBone> spriteBoneTemplates,
        string importerMode,
        bool forceWrite = false)
    {
        if (boneProvider == null || data == null || data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return false;

        List<SpriteBone> currentBones = null;
        try
        {
            currentBones = boneProvider.GetBones(binding.spriteId);
        }
        catch
        {
            currentBones = null;
        }

        currentBones ??= new List<SpriteBone>();
        var desired = BuildSpriteBonesForLayerData(data, binding, importerMode, currentBones, spriteBoneTemplates);
        if (desired == null || desired.Count == 0)
            return false;
        if (!forceWrite && AreSpriteBonesEqual(currentBones, desired))
            return false;

        boneProvider.SetBones(binding.spriteId, desired);
        return true;
    }

    private static bool ClearSpriteBonesForLayerData(ISpriteBoneDataProvider boneProvider, LayerSpriteBinding binding)
    {
        if (boneProvider == null)
            return false;

        List<SpriteBone> currentBones = null;
        try
        {
            currentBones = boneProvider.GetBones(binding.spriteId);
        }
        catch
        {
            currentBones = null;
        }

        if (currentBones == null || currentBones.Count == 0)
            return false;

        boneProvider.SetBones(binding.spriteId, new List<SpriteBone>());
        return true;
    }

    private LayerVertexData BuildImporterSkinningWriteData(LayerVertexData data)
    {
        if (data == null)
            return null;
        if (!TryBuildCompactImporterSkinning(data, out var compactMap, out var compactWeights))
            return data;
        if (AreIndicesEqual(data.spriteBoneToRendererBone, compactMap) &&
            AreBoneWeightsEqual(data.boneWeights, compactWeights))
        {
            return data;
        }

        return new LayerVertexData
        {
            layer = data.layer,
            texture = data.texture,
            pixelsPerUnit = data.pixelsPerUnit,
            pivotPixels = data.pivotPixels,
            vertices = data.vertices,
            vertexUvs = data.vertexUvs,
            importerMode = data.importerMode,
            triangles = data.triangles,
            allEdgePairs = data.allEdgePairs,
            boundaryEdgePairs = data.boundaryEdgePairs,
            boneWeights = compactWeights,
            spriteBoneToRendererBone = compactMap,
            bindBoneInverseMatrices = BuildBindBoneInverseMatrices(compactMap),
            usesSpriteBindPoses = false,
            hasSkinning = compactMap.Length > 0 && HasUsableVertexWeights(compactWeights),
            lastPoseVersion = -1,
            deformedVertices = null,
            skinMatrices = null
        };
    }

    private static bool HasWritableImporterSkinningData(LayerVertexData data)
    {
        return data != null &&
               data.spriteBoneToRendererBone != null &&
               data.spriteBoneToRendererBone.Length > 0 &&
               HasUsableVertexWeights(data.boneWeights);
    }

    private bool TryBuildCompactImporterSkinning(
        LayerVertexData data,
        out int[] compactMap,
        out BoneWeight[] compactWeights)
    {
        compactMap = Array.Empty<int>();
        compactWeights = data != null ? CloneArray(data.boneWeights) ?? Array.Empty<BoneWeight>() : Array.Empty<BoneWeight>();
        if (data == null ||
            data.boneWeights == null ||
            data.boneWeights.Length == 0 ||
            data.spriteBoneToRendererBone == null ||
            data.spriteBoneToRendererBone.Length == 0 ||
            !HasUsableVertexWeights(data.boneWeights))
        {
            return false;
        }

        var rendererBones = new List<int>();
        var included = new HashSet<int>();
        for (var i = 0; i < data.boneWeights.Length; i++)
            AddWeightedRendererBones(data.boneWeights[i], data.spriteBoneToRendererBone, rendererBones, included);

        if (rendererBones.Count == 0)
            return false;

        compactMap = rendererBones.ToArray();
        var rendererToSprite = BuildRendererToSpriteBoneMap(compactMap);
        compactWeights = new BoneWeight[data.boneWeights.Length];
        for (var i = 0; i < data.boneWeights.Length; i++)
            compactWeights[i] = RemapWeightToCompactSpriteBones(data.boneWeights[i], data.spriteBoneToRendererBone, rendererToSprite);
        return true;
    }

    private void AddWeightedRendererBones(
        BoneWeight weight,
        IReadOnlyList<int> spriteBoneToRendererBone,
        List<int> rendererBones,
        HashSet<int> included)
    {
        if (spriteBoneToRendererBone == null || rendererBones == null || included == null)
            return;
        if (!ReanimatorProjectorAlgorithms.HasUsableWeight(weight))
            return;

        var activeBones = ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(weight, spriteBoneToRendererBone);
        for (var i = 0; i < activeBones.Count; i++)
        {
            var spriteBoneIndex = activeBones[i];
            if (spriteBoneIndex < 0 || spriteBoneIndex >= spriteBoneToRendererBone.Count)
                continue;

            AddRendererBone(spriteBoneToRendererBone[spriteBoneIndex], rendererBones, included);
        }
    }

    private static void AddRendererBone(int rendererBoneIndex, List<int> rendererBones, HashSet<int> included)
    {
        if (rendererBoneIndex < 0 || rendererBones == null || included == null)
            return;
        if (!included.Add(rendererBoneIndex))
            return;

        rendererBones.Add(rendererBoneIndex);
    }

    private static BoneWeight RemapWeightToCompactSpriteBones(
        BoneWeight source,
        IReadOnlyList<int> originalSpriteBoneToRendererBone,
        IReadOnlyDictionary<int, int> rendererToCompactSpriteBone)
    {
        if (!ReanimatorProjectorAlgorithms.HasUsableWeight(source))
            return default;

        var accum = new Dictionary<int, float>();
        AccumulateCompactWeight(accum, source.boneIndex0, source.weight0, originalSpriteBoneToRendererBone, rendererToCompactSpriteBone);
        AccumulateCompactWeight(accum, source.boneIndex1, source.weight1, originalSpriteBoneToRendererBone, rendererToCompactSpriteBone);
        AccumulateCompactWeight(accum, source.boneIndex2, source.weight2, originalSpriteBoneToRendererBone, rendererToCompactSpriteBone);
        AccumulateCompactWeight(accum, source.boneIndex3, source.weight3, originalSpriteBoneToRendererBone, rendererToCompactSpriteBone);
        var result = BuildVertexFixWeight(accum);
        PreserveCompactIdentitySlots(ref result, source, originalSpriteBoneToRendererBone, rendererToCompactSpriteBone);
        return result;
    }

    private static void AccumulateCompactWeight(
        Dictionary<int, float> accum,
        int sourceSpriteBoneIndex,
        float weight,
        IReadOnlyList<int> originalSpriteBoneToRendererBone,
        IReadOnlyDictionary<int, int> rendererToCompactSpriteBone)
    {
        if (accum == null ||
            weight <= 0.0001f ||
            sourceSpriteBoneIndex < 0 ||
            originalSpriteBoneToRendererBone == null ||
            sourceSpriteBoneIndex >= originalSpriteBoneToRendererBone.Count ||
            rendererToCompactSpriteBone == null)
        {
            return;
        }

        var rendererBoneIndex = originalSpriteBoneToRendererBone[sourceSpriteBoneIndex];
        if (!rendererToCompactSpriteBone.TryGetValue(rendererBoneIndex, out var compactSpriteBoneIndex))
            return;

        AccumulateWeightChannel(accum, compactSpriteBoneIndex, weight);
    }

    private static void PreserveCompactIdentitySlots(
        ref BoneWeight result,
        BoneWeight source,
        IReadOnlyList<int> originalSpriteBoneToRendererBone,
        IReadOnlyDictionary<int, int> rendererToCompactSpriteBone)
    {
        if (originalSpriteBoneToRendererBone == null || rendererToCompactSpriteBone == null)
            return;

        var activeSourceBones = ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(source, originalSpriteBoneToRendererBone);
        if (activeSourceBones.Count == 0)
            return;

        var indices = new[] { result.boneIndex0, result.boneIndex1, result.boneIndex2, result.boneIndex3 };
        var weights = new[] { result.weight0, result.weight1, result.weight2, result.weight3 };
        var used = new HashSet<int>();
        for (var i = 0; i < indices.Length; i++)
        {
            if (weights[i] > 0.0001f)
                used.Add(indices[i]);
        }

        for (var i = 0; i < activeSourceBones.Count; i++)
        {
            var sourceSpriteBoneIndex = activeSourceBones[i];
            if (sourceSpriteBoneIndex < 0 || sourceSpriteBoneIndex >= originalSpriteBoneToRendererBone.Count)
                continue;

            var rendererBoneIndex = originalSpriteBoneToRendererBone[sourceSpriteBoneIndex];
            if (!rendererToCompactSpriteBone.TryGetValue(rendererBoneIndex, out var compactSpriteBoneIndex))
                continue;
            if (!used.Add(compactSpriteBoneIndex))
                continue;

            for (var slot = 0; slot < indices.Length; slot++)
            {
                if (weights[slot] > 0.0001f)
                    continue;

                indices[slot] = compactSpriteBoneIndex;
                weights[slot] = 0f;
                break;
            }
        }

        result.boneIndex0 = indices[0];
        result.boneIndex1 = indices[1];
        result.boneIndex2 = indices[2];
        result.boneIndex3 = indices[3];
        result.weight0 = weights[0];
        result.weight1 = weights[1];
        result.weight2 = weights[2];
        result.weight3 = weights[3];
    }

    private static bool AreBoneWeightsEqual(IReadOnlyList<BoneWeight> lhs, IReadOnlyList<BoneWeight> rhs)
    {
        var lhsCount = lhs != null ? lhs.Count : 0;
        var rhsCount = rhs != null ? rhs.Count : 0;
        if (lhsCount != rhsCount)
            return false;
        for (var i = 0; i < lhsCount; i++)
        {
            if (!AreBoneWeightsApproximatelyEqual(lhs[i], rhs[i]))
                return false;
        }
        return true;
    }

    private List<SpriteBone> BuildSpriteBonesForLayerData(
        LayerVertexData data,
        LayerSpriteBinding binding,
        string importerMode,
        IReadOnlyList<SpriteBone> currentBones,
        IReadOnlyDictionary<int, SpriteBone> spriteBoneTemplates)
    {
        var spriteBoneToRendererBone = data != null ? data.spriteBoneToRendererBone : null;
        if (spriteBoneToRendererBone == null || spriteBoneToRendererBone.Length == 0)
            return null;

        var desired = new List<SpriteBone>(spriteBoneToRendererBone.Length);
        var rendererToSprite = BuildRendererToSpriteBoneMap(spriteBoneToRendererBone);
        var currentSpriteToRenderer = currentBones != null && currentBones.Count > 0
            ? BuildSpriteBoneToRendererBoneMap(ToSpriteBoneArray(currentBones))
            : Array.Empty<int>();
        for (var i = 0; i < spriteBoneToRendererBone.Length; i++)
        {
            var rendererBoneIndex = spriteBoneToRendererBone[i];
            var canReuseCurrent =
                currentBones != null &&
                i < currentBones.Count &&
                currentSpriteToRenderer != null &&
                i < currentSpriteToRenderer.Length &&
                currentSpriteToRenderer[i] == rendererBoneIndex;
            var spriteBone = canReuseCurrent
                ? currentBones[i]
                : BuildSpriteBoneShellForRendererBone(rendererBoneIndex, spriteBoneTemplates);

            spriteBone.parentId = ResolveSpriteBoneParentIndex(rendererBoneIndex, rendererToSprite);
            ApplyRendererBoneTransformToSpriteBone(ref spriteBone, data, binding, importerMode, rendererBoneIndex, spriteBone.parentId, spriteBoneToRendererBone);
            desired.Add(spriteBone);
        }

        return desired;
    }

    private SpriteBone BuildSpriteBoneShellForRendererBone(
        int rendererBoneIndex,
        IReadOnlyDictionary<int, SpriteBone> spriteBoneTemplates)
    {
        if (rendererBoneIndex >= 0 &&
            spriteBoneTemplates != null &&
            spriteBoneTemplates.TryGetValue(rendererBoneIndex, out var template))
            return template;

        if (bones != null && rendererBoneIndex >= 0 && rendererBoneIndex < bones.Count && bones[rendererBoneIndex] != null)
        {
            var bone = bones[rendererBoneIndex];
            var color = SanitizeBoneColor(bone.color);
            return new SpriteBone
            {
                name = bone.name ?? string.Empty,
                position = new Vector3(
                    bone.position.x,
                    bone.position.y,
                    ReanimatorBoneOrderAlgorithms.EncodeBoneOrder(bone.depth)),
                rotation = bone.rotation,
                length = bone.length,
                parentId = -1,
                color = color
            };
        }

        return new SpriteBone
        {
            name = rendererBoneIndex >= 0 ? $"bone_{rendererBoneIndex}" : "bone",
            parentId = -1,
            color = Color.white
        };
    }

    private void ApplyRendererBoneTransformToSpriteBone(
        ref SpriteBone spriteBone,
        LayerVertexData data,
        LayerSpriteBinding binding,
        string importerMode,
        int rendererBoneIndex,
        int parentSpriteBoneIndex,
        IReadOnlyList<int> spriteBoneToRendererBone)
    {
        if (data == null || rendererBoneIndex < 0 || bones == null || rendererBoneIndex >= bones.Count || bones[rendererBoneIndex] == null)
            return;

        var bone = bones[rendererBoneIndex];
        var bonePosition = ConvertDocToImporterPositionByMode(data.layer, binding, bone.worldPosition, importerMode);
        var boneRotation = bone.worldRotation;
        var localPosition = bonePosition;
        var localRotation = boneRotation;

        if (parentSpriteBoneIndex >= 0 &&
            spriteBoneToRendererBone != null &&
            parentSpriteBoneIndex < spriteBoneToRendererBone.Count)
        {
            var parentRendererBoneIndex = spriteBoneToRendererBone[parentSpriteBoneIndex];
            if (parentRendererBoneIndex >= 0 && parentRendererBoneIndex < bones.Count && bones[parentRendererBoneIndex] != null)
            {
                var parentBone = bones[parentRendererBoneIndex];
                var parentPosition = ConvertDocToImporterPositionByMode(data.layer, binding, parentBone.worldPosition, importerMode);
                var localPosition3 = Quaternion.Inverse(parentBone.worldRotation) *
                                     new Vector3(bonePosition.x - parentPosition.x, bonePosition.y - parentPosition.y, 0f);
                localPosition = new Vector2(localPosition3.x, localPosition3.y);
                localRotation = Quaternion.Inverse(parentBone.worldRotation) * boneRotation;
            }
        }

        var endDoc3 = bone.worldRotation * new Vector3(Mathf.Max(0f, bone.length), 0f, 0f);
        var endPosition = ConvertDocToImporterPositionByMode(
            data.layer,
            binding,
            bone.worldPosition + new Vector2(endDoc3.x, endDoc3.y),
            importerMode);
        var length = Vector2.Distance(bonePosition, endPosition);

        spriteBone.position = new Vector3(
            localPosition.x,
            localPosition.y,
            ReanimatorBoneOrderAlgorithms.EncodeBoneOrder(bone.depth));
        spriteBone.rotation = localRotation;
        spriteBone.length = length;
        if (!string.IsNullOrEmpty(bone.name))
            spriteBone.name = bone.name;
        else if (string.IsNullOrEmpty(spriteBone.name))
            spriteBone.name = bone.name ?? string.Empty;
        spriteBone.color = SanitizeBoneColor(spriteBone.color);
    }

    private int ResolveSpriteBoneParentIndex(
        int rendererBoneIndex,
        IReadOnlyDictionary<int, int> rendererToSprite)
    {
        if (rendererBoneIndex < 0 || bones == null || rendererBoneIndex >= bones.Count || rendererToSprite == null)
            return -1;

        var parent = bones[rendererBoneIndex].parentId;
        var guard = 0;
        while (parent >= 0 && parent < bones.Count && guard++ <= bones.Count + 1)
        {
            if (rendererToSprite.TryGetValue(parent, out var parentSpriteBone))
                return parentSpriteBone;
            parent = bones[parent].parentId;
        }

        return -1;
    }

    private static bool AreSpriteBonesEqual(IReadOnlyList<SpriteBone> lhs, IReadOnlyList<SpriteBone> rhs)
    {
        var lhsCount = lhs != null ? lhs.Count : 0;
        var rhsCount = rhs != null ? rhs.Count : 0;
        if (lhsCount != rhsCount)
            return false;

        for (var i = 0; i < lhsCount; i++)
        {
            if (!AreSpriteBonesEqual(lhs[i], rhs[i]))
                return false;
        }

        return true;
    }

    private static bool AreSpriteBonesEqual(SpriteBone lhs, SpriteBone rhs)
    {
        return string.Equals(lhs.name, rhs.name, StringComparison.Ordinal) &&
               lhs.parentId == rhs.parentId &&
               Vector3.SqrMagnitude(lhs.position - rhs.position) <= 1e-8f &&
               Quaternion.Angle(lhs.rotation, rhs.rotation) <= 0.001f &&
               Mathf.Abs(lhs.length - rhs.length) <= 0.0001f &&
               AreColorsApproximatelyEqual(lhs.color, rhs.color);
    }
}
#endif
