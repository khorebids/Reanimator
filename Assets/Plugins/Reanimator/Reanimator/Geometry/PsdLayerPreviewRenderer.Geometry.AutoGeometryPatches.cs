#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static void ApplyAutoGeometryToLayerData(
        LayerVertexData data,
        LayerInfo layer,
        Vector2[] verticesDoc,
        Vector2[] vertexUvs,
        int[] triangles,
        int[] allEdgePairs,
        int[] boundaryEdgePairs,
        BoneWeight[] boneWeights)
    {
        if (data == null)
            return;

        data.layer = layer;
        data.vertices = verticesDoc ?? Array.Empty<Vector2>();
        data.vertexUvs = vertexUvs ?? Array.Empty<Vector2>();
        data.triangles = triangles ?? Array.Empty<int>();
        data.allEdgePairs = allEdgePairs ?? Array.Empty<int>();
        data.boundaryEdgePairs = boundaryEdgePairs ?? Array.Empty<int>();
        data.boneWeights = boneWeights ?? Array.Empty<BoneWeight>();
        data.hasSkinning = data.boneWeights != null &&
                          data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
    }

    private void UpsertAutoGeometryPatch(
        string assetPath,
        string selectionKey,
        string stableKey,
        LayerVertexData data,
        bool generateWeights,
        string importerMode = null)
    {
        if (data == null || string.IsNullOrWhiteSpace(assetPath))
            return;

        var normalizedPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalizedPath))
            return;
        TryEnsureLayerVertexUvs(data);
        autoGeometryPatches ??= new List<AutoGeometryPatch>();

        for (var i = 0; i < autoGeometryPatches.Count; i++)
        {
            var patch = autoGeometryPatches[i];
            if (patch == null)
                continue;
            if (!IsAssetPathMatch(patch.assetPath, normalizedPath))
                continue;
            if (!IsLayerMatch(patch.layerSelectionKey, patch.layerStableKey, selectionKey, stableKey))
                continue;

            OverwritePatch(patch, normalizedPath, selectionKey, stableKey, data, generateWeights, importerMode);
            return;
        }

        var created = new AutoGeometryPatch();
        OverwritePatch(created, normalizedPath, selectionKey, stableKey, data, generateWeights, importerMode);
        autoGeometryPatches.Add(created);
    }

    private static void OverwritePatch(
        AutoGeometryPatch patch,
        string assetPath,
        string selectionKey,
        string stableKey,
        LayerVertexData data,
        bool generateWeights,
        string importerMode)
    {
        if (patch == null || data == null)
            return;

        patch.assetPath = assetPath;
        patch.layerSelectionKey = selectionKey;
        patch.layerStableKey = stableKey;
        AutoGeometryPatchIdentity.Capture(patch, data);
        patch.verticesDoc = CloneArray(data.vertices);
        patch.vertexUvs = CloneArray(data.vertexUvs);
        patch.triangles = CloneArray(data.triangles);
        patch.allEdgePairs = CloneArray(data.allEdgePairs);
        patch.boundaryEdgePairs = CloneArray(data.boundaryEdgePairs);
        patch.boneWeights = CloneArray(data.boneWeights);
        patch.spriteBoneToRendererBone = CloneArray(data.spriteBoneToRendererBone);
        patch.bindBoneInverseMatrices = CloneArray(data.bindBoneInverseMatrices);
        patch.usesSpriteBindPoses = data.usesSpriteBindPoses;
        patch.generateWeights = generateWeights;
        patch.importerMode = !string.IsNullOrWhiteSpace(importerMode) ? importerMode : data.importerMode;
    }

    private void ApplyPendingAutoGeometryToPreview(string assetPath)
    {
        if (autoGeometryPatches == null || autoGeometryPatches.Count == 0 || string.IsNullOrWhiteSpace(assetPath))
            return;

        var normalizedPath = NormalizeAssetPath(assetPath);
        for (var i = 0; i < autoGeometryPatches.Count; i++)
        {
            var patch = autoGeometryPatches[i];
            if (patch == null)
                continue;
            if (!IsAssetPathMatch(patch.assetPath, normalizedPath))
                continue;
            if (!TryGetLayerVertexDataForKeys(patch.layerSelectionKey, patch.layerStableKey, out var data) || data == null)
                continue;
            ApplyAutoGeometryPatchToLayerData(patch, data);
            TryEnsureLayerVertexUvs(data);
        }
    }

    private static void ApplyAutoGeometryPatchToLayerData(AutoGeometryPatch patch, LayerVertexData data)
    {
        if (patch == null || data == null)
            return;

        data.vertices = CloneArray(patch.verticesDoc) ?? Array.Empty<Vector2>();
        data.vertexUvs = CloneArray(patch.vertexUvs) ?? Array.Empty<Vector2>();
        data.triangles = CloneArray(patch.triangles) ?? Array.Empty<int>();
        data.allEdgePairs = CloneArray(patch.allEdgePairs) ?? Array.Empty<int>();
        data.boundaryEdgePairs = CloneArray(patch.boundaryEdgePairs) ?? Array.Empty<int>();
        data.boneWeights = CloneArray(patch.boneWeights) ?? Array.Empty<BoneWeight>();
        data.spriteBoneToRendererBone = CloneArray(patch.spriteBoneToRendererBone) ?? Array.Empty<int>();
        data.bindBoneInverseMatrices = CloneArray(patch.bindBoneInverseMatrices) ?? Array.Empty<Matrix4x4>();
        data.usesSpriteBindPoses = patch.usesSpriteBindPoses;
        data.importerMode = patch.importerMode;
        data.hasSkinning = data.boneWeights != null &&
                          data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
    }

    private bool ApplyPendingAutoGeometryToImporter(string assetPath, ISpriteEditorDataProvider dataProvider, out string error)
    {
        error = null;
        if (dataProvider == null || autoGeometryPatches == null || autoGeometryPatches.Count == 0)
            return false;
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        var normalizedPath = NormalizeAssetPath(assetPath);
        var meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
        if (meshProvider == null)
            return false;
        var boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
        var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();

        var bindings = BuildLayerSpriteBindings(dataProvider);
        if (bindings.Count == 0)
            return false;
        var spriteBoneTemplates = BuildSpriteBoneTemplateMap(boneProvider, bindings);

        var changed = false;
        for (var i = 0; i < autoGeometryPatches.Count; i++)
        {
            var patch = autoGeometryPatches[i];
            if (patch == null)
                continue;
            if (!IsAssetPathMatch(patch.assetPath, normalizedPath))
                continue;
            if (!TryBuildLayerDataFromAutoGeometryPatch(patch, out var data) || data == null)
                continue;
            if (data.vertices == null || data.vertices.Length == 0)
                continue;
            if (!TryResolveLayerBindingForAutoGeometryPatch(bindings, patch, out var binding, out _))
                continue;

            var writeData = BuildImporterSkinningWriteData(data);
            var importerVertices = meshProvider.GetVertices(binding.spriteId) ?? Array.Empty<Vertex2DMetaData>();
            var importerIndices = meshProvider.GetIndices(binding.spriteId) ?? Array.Empty<int>();
            var importerEdges = meshProvider.GetEdges(binding.spriteId) ?? Array.Empty<Vector2Int>();

            var metaVertices = BuildAutoGeometryMetaVertices(writeData, binding, importerVertices, importerIndices, patch.importerMode, out var importerMode);
            if (metaVertices.Length == 0)
                continue;
            var indices = BuildImporterIndices(writeData, metaVertices.Length, importerIndices);
            var edges = BuildImporterEdgesFromIndices(indices, metaVertices.Length);
            if (!ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(
                    metaVertices,
                    indices,
                    binding.rect,
                    out var invalidReason))
            {
                error = $"Refusing to write invalid mesh for '{GetLayerDisplayName(writeData.layer)}': {invalidReason}.";
                return false;
            }

            if (HasWritableImporterSkinningData(writeData))
            {
                if (ApplySpriteBonesForLayerData(boneProvider, binding, writeData, spriteBoneTemplates, importerMode, true))
                    changed = true;
                if (ApplyCharacterPartBonesForLayerData(characterProvider, binding, writeData))
                    changed = true;
            }
            else
            {
                if (ClearSpriteBonesForLayerData(boneProvider, binding))
                    changed = true;
                if (ClearCharacterPartBonesForLayerData(characterProvider, binding))
                    changed = true;
            }

            var layerChanged = !AreMetaVerticesEqual(importerVertices, metaVertices) ||
                               !AreIndicesEqual(importerIndices, indices) ||
                               !AreEdgesEqual(importerEdges, edges);
            if (!layerChanged)
                continue;

            meshProvider.SetVertices(binding.spriteId, metaVertices);
            meshProvider.SetIndices(binding.spriteId, indices);
            meshProvider.SetEdges(binding.spriteId, edges);
            changed = true;
        }

        return changed;
    }

    private bool TryBuildLayerDataFromAutoGeometryPatch(AutoGeometryPatch patch, out LayerVertexData data)
    {
        data = null;
        if (patch == null)
            return false;

        var layer = ResolveLayerForAutoGeometryPatch(patch);
        if (layer == null)
            return false;

        data = new LayerVertexData
        {
            layer = layer,
            texture = layer.texture,
            pixelsPerUnit = 1f,
            pivotPixels = Vector2.zero,
            lastPoseVersion = -1
        };
        ApplyAutoGeometryPatchToLayerData(patch, data);

        if (TryGetLayerVertexDataForKeys(patch.layerSelectionKey, patch.layerStableKey, out var liveData) && liveData != null)
            ApplyAutoGeometryPatchToLayerData(patch, liveData);

        var selectionKey = BuildSelectionKey(layer);
        if (!string.IsNullOrEmpty(selectionKey))
            vertexDataByLayerSelectionKey[selectionKey] = data;

        return true;
    }

    private bool TryGetLayerDataForAutoGeometryPatch(AutoGeometryPatch patch, out LayerVertexData data)
    {
        data = null;
        if (patch == null)
            return false;

        if (TryGetLayerVertexDataForKeys(patch.layerSelectionKey, patch.layerStableKey, out data) && data != null)
        {
            ApplyAutoGeometryPatchToLayerData(patch, data);
            return true;
        }

        var layer = ResolveLayerForAutoGeometryPatch(patch);
        if (layer == null)
            return false;

        data = new LayerVertexData
        {
            layer = layer,
            texture = layer.texture,
            pixelsPerUnit = 1f,
            pivotPixels = Vector2.zero,
            lastPoseVersion = -1
        };
        ApplyAutoGeometryPatchToLayerData(patch, data);

        var selectionKey = BuildSelectionKey(layer);
        if (!string.IsNullOrEmpty(selectionKey))
            vertexDataByLayerSelectionKey[selectionKey] = data;

        return true;
    }

    private LayerInfo ResolveLayerForAutoGeometryPatch(AutoGeometryPatch patch)
    {
        if (patch == null)
            return null;

        var layer = FindLayerBySelectionKey(layerInfos, patch.layerSelectionKey);
        if (AutoGeometryPatchIdentity.Matches(layer, patch) ||
            string.IsNullOrEmpty(patch.layerStableKey) ||
            layerInfos == null)
        {
            return layer;
        }

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var candidate = layerInfos[i];
            if (candidate == null)
                continue;
            if (!string.Equals(BuildKey(candidate.name, candidate.layerId), patch.layerStableKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!AutoGeometryPatchIdentity.Matches(candidate, patch))
                continue;

            return candidate;
        }

        return null;
    }

    private bool TryResolveLayerBinding(
        IReadOnlyDictionary<string, LayerSpriteBinding> bindings,
        string selectionKey,
        string stableKey,
        out LayerSpriteBinding binding,
        out string resolvedSelectionKey)
    {
        var patch = new AutoGeometryPatch
        {
            layerSelectionKey = selectionKey,
            layerStableKey = stableKey
        };
        return TryResolveLayerBindingForAutoGeometryPatch(bindings, patch, out binding, out resolvedSelectionKey);
    }

    private bool TryResolveLayerBindingForAutoGeometryPatch(
        IReadOnlyDictionary<string, LayerSpriteBinding> bindings,
        AutoGeometryPatch patch,
        out LayerSpriteBinding binding,
        out string resolvedSelectionKey)
    {
        binding = default;
        resolvedSelectionKey = string.Empty;
        if (bindings == null || bindings.Count == 0)
            return false;

        var spriteGuid = NormalizeGuidString(patch != null ? patch.spriteGuid : null);
        if (!string.IsNullOrEmpty(spriteGuid))
        {
            foreach (var kv in bindings)
            {
                if (!string.Equals(NormalizeGuidString(kv.Value.spriteId.ToString()), spriteGuid, StringComparison.OrdinalIgnoreCase))
                    continue;

                binding = kv.Value;
                resolvedSelectionKey = kv.Key;
                return true;
            }
        }

        var selectionKey = patch != null ? patch.layerSelectionKey : string.Empty;
        if (!string.IsNullOrEmpty(selectionKey) && bindings.TryGetValue(selectionKey, out binding))
        {
            resolvedSelectionKey = selectionKey;
            return true;
        }

        var stableKey = patch != null ? patch.layerStableKey : string.Empty;
        if (string.IsNullOrEmpty(stableKey) || layerInfos == null)
            return false;

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;
            if (!string.Equals(BuildKey(layer.name, layer.layerId), stableKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!AutoGeometryPatchIdentity.Matches(layer, patch))
                continue;

            var key = BuildSelectionKey(layer);
            if (string.IsNullOrEmpty(key))
                continue;
            if (!bindings.TryGetValue(key, out binding))
                continue;

            resolvedSelectionKey = key;
            return true;
        }

        return false;
    }

    private void RemoveAutoGeometryPatchesForAsset(string assetPath)
    {
        if (autoGeometryPatches == null || autoGeometryPatches.Count == 0 || string.IsNullOrWhiteSpace(assetPath))
            return;

        var normalizedPath = NormalizeAssetPath(assetPath);
        for (var i = autoGeometryPatches.Count - 1; i >= 0; i--)
        {
            var patch = autoGeometryPatches[i];
            if (patch == null || IsAssetPathMatch(patch.assetPath, normalizedPath))
                autoGeometryPatches.RemoveAt(i);
        }
    }

    private static class AutoGeometryPatchIdentity
    {
        public static void Capture(AutoGeometryPatch patch, LayerVertexData data)
        {
            if (patch == null)
                return;

            var layer = data != null ? data.layer : null;
            patch.spriteGuid = NormalizeGuidString(layer != null ? layer.spriteGuid : null);
            patch.layerName = layer != null ? layer.name : string.Empty;
            patch.layerId = layer != null ? layer.layerId : 0;
            patch.layerPosition = layer != null ? layer.position : Vector2.zero;
            patch.sourceRect = layer != null ? layer.sourceRect : default;
        }

        public static bool Matches(LayerInfo layer, AutoGeometryPatch patch)
        {
            if (layer == null || patch == null)
                return false;

            var patchGuid = NormalizeGuidString(patch.spriteGuid);
            if (!string.IsNullOrEmpty(patchGuid))
                return string.Equals(NormalizeGuidString(layer.spriteGuid), patchGuid, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(patch.layerName) &&
                !string.Equals(layer.name, patch.layerName, StringComparison.Ordinal))
            {
                return false;
            }

            if (patch.layerId != 0 && layer.layerId != patch.layerId)
                return false;

            if (!IsZeroRect(patch.sourceRect) && !RectsRoughlyEqual(layer.sourceRect, patch.sourceRect))
                return false;

            return patch.layerPosition == Vector2.zero ||
                   Vector2.SqrMagnitude(layer.position - patch.layerPosition) <= 0.0001f;
        }

        private static bool IsZeroRect(Rect rect)
        {
            return Mathf.Abs(rect.x) <= 0.0001f &&
                   Mathf.Abs(rect.y) <= 0.0001f &&
                   Mathf.Abs(rect.width) <= 0.0001f &&
                   Mathf.Abs(rect.height) <= 0.0001f;
        }
    }
}
#endif
