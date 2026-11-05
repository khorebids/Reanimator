#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

public partial class PsdLayerPreviewRenderer
{
    public bool FixSelectedSpriteSkinWeights(
        string assetPath,
        out int fixedVertexCount,
        out bool savedChanges,
        out string layerName,
        out string error)
    {
        fixedVertexCount = 0;
        savedChanges = false;
        layerName = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "PSD path is empty.";
            return false;
        }

        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select a sprite layer in Reanimator.";
            return false;
        }

        layerName = data.layer != null ? data.layer.name : string.Empty;

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        if (importer == null)
        {
            error = "Selected asset is not a PSD importer.";
            return false;
        }

        var dataProvider = importer as ISpriteEditorDataProvider;
        if (dataProvider == null)
        {
            error = "Sprite editor data provider is unavailable.";
            return false;
        }

        try
        {
            RestorePreviewTestAnimationPose();
            RestorePose();

            dataProvider.InitSpriteEditorDataProvider();

            var bindings = BuildLayerSpriteBindings(dataProvider);
            if (!TryResolveLayerBinding(bindings, selectionKey, stableKey, out var binding, out _))
            {
                error = "Selected sprite was not found in the importer.";
                return false;
            }

            var meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
            if (meshProvider == null)
            {
                error = "Sprite mesh data provider is unavailable.";
                return false;
            }

            var vertices = meshProvider.GetVertices(binding.spriteId) ?? Array.Empty<Vertex2DMetaData>();
            if (vertices.Length == 0)
            {
                error = "Selected sprite has no geometry.";
                return false;
            }

            Undo.RegisterCompleteObjectUndo(importer, "Fix Sprite Skin Weights");
            var hasImporterChanges = false;
            var boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
            var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
            var currentBoneCount = GetSpriteBoneCount(boneProvider, binding);
            var previewBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;

            if (boneProvider != null && previewBoneCount > currentBoneCount)
            {
                var importerMode = "raw_ppu_plus_pivot";
                SelectBestImporterVerticesInterpretation(data.layer, binding, vertices, null, out _, out _, out importerMode);
                importerMode = ReanimatorImporterGeometryAlgorithms.SanitizeImporterWriteMode(importerMode);

                var templates = BuildSpriteBoneTemplateMap(boneProvider, bindings);
                if (ApplySpriteBonesForLayerData(boneProvider, binding, data, templates, importerMode))
                    hasImporterChanges = true;

                currentBoneCount = Mathf.Max(currentBoneCount, previewBoneCount);
            }

            if (ApplyCharacterPartBonesForLayerData(characterProvider, binding, data))
                hasImporterChanges = true;

            var validBoneCount = Mathf.Max(currentBoneCount, previewBoneCount);
            if (validBoneCount <= 0)
            {
                error = "Selected sprite has no bones.";
                return false;
            }

            var fixedVertices = new Vertex2DMetaData[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                var fixedWeight = SanitizeSpriteSkinBoneWeight(vertex.boneWeight, validBoneCount);
                if (!AreBoneWeightsEqual(vertex.boneWeight, fixedWeight))
                    fixedVertexCount++;
                vertex.boneWeight = fixedWeight;
                fixedVertices[i] = vertex;
            }

            if (fixedVertexCount > 0)
            {
                meshProvider.SetVertices(binding.spriteId, fixedVertices);
                hasImporterChanges = true;
            }

            if (!hasImporterChanges)
                return true;

            if (!CommitImmediateDataProviderOperation(assetPath, dataProvider, "Fix Sprite Skin Weights", out error))
                return false;

            savedChanges = true;
            hasPendingChanges = false;
            Rebuild(assetPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static int GetSpriteBoneCount(ISpriteBoneDataProvider boneProvider, LayerSpriteBinding binding)
    {
        if (boneProvider == null)
            return 0;

        try
        {
            var bones = boneProvider.GetBones(binding.spriteId);
            return bones != null ? bones.Count : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static BoneWeight SanitizeSpriteSkinBoneWeight(BoneWeight source, int validBoneCount)
    {
        var indices = new[]
        {
            source.boneIndex0,
            source.boneIndex1,
            source.boneIndex2,
            source.boneIndex3
        };
        var weights = new[]
        {
            source.weight0,
            source.weight1,
            source.weight2,
            source.weight3
        };

        var resultIndices = new int[4];
        var resultWeights = new float[4];
        var influenceCount = 0;

        for (var i = 0; i < 4; i++)
        {
            var weight = Mathf.Max(0f, weights[i]);
            var index = indices[i];
            if (weight <= 0.0001f || index < 0 || index >= validBoneCount)
                continue;

            var insertAt = influenceCount;
            while (insertAt > 0 && resultWeights[insertAt - 1] < weight)
            {
                if (insertAt < 4)
                {
                    resultWeights[insertAt] = resultWeights[insertAt - 1];
                    resultIndices[insertAt] = resultIndices[insertAt - 1];
                }
                insertAt--;
            }

            if (insertAt < 4)
            {
                resultWeights[insertAt] = weight;
                resultIndices[insertAt] = index;
            }

            influenceCount = Mathf.Min(4, influenceCount + 1);
        }

        if (influenceCount == 0)
        {
            return new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 1f,
                boneIndex1 = 0,
                boneIndex2 = 0,
                boneIndex3 = 0
            };
        }

        var sum = 0f;
        for (var i = 0; i < 4; i++)
            sum += resultWeights[i];

        if (sum > 0.0001f)
        {
            var inv = 1f / sum;
            for (var i = 0; i < 4; i++)
                resultWeights[i] *= inv;
        }

        return new BoneWeight
        {
            boneIndex0 = resultIndices[0],
            boneIndex1 = resultIndices[1],
            boneIndex2 = resultIndices[2],
            boneIndex3 = resultIndices[3],
            weight0 = resultWeights[0],
            weight1 = resultWeights[1],
            weight2 = resultWeights[2],
            weight3 = resultWeights[3]
        };
    }
}
#endif
