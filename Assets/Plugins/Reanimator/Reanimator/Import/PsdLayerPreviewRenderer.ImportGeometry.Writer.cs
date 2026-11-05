#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private bool ApplyElbowVerticesToImporterMesh(string assetPath, ISpriteEditorDataProvider dataProvider, out string error)
    {
        error = null;
        if (dataProvider == null || appliedElbowVertices == null || appliedElbowVertices.Count == 0)
            return false;
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return false;
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        var normalizedAssetPath = NormalizeAssetPath(assetPath);
        MigrateLegacyAppliedVerticesToAsset(normalizedAssetPath);

        var meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
        if (meshProvider == null)
            return false;

        var bindings = BuildLayerSpriteBindings(dataProvider);
        if (bindings.Count == 0)
            return false;

        var changed = false;
        foreach (var kv in bindings)
        {
            var selectionKey = kv.Key;
            var binding = kv.Value;
            if (!vertexDataByLayerSelectionKey.TryGetValue(selectionKey, out var data) || data == null)
                continue;
            if (!HasAppliedVerticesForLayer(normalizedAssetPath, selectionKey, BuildKey(data.layer.name, data.layer.layerId)))
                continue;

            var importerVertices = NormalizeRuntimeUnitImporterVertices(
                meshProvider.GetVertices(binding.spriteId),
                binding);
            var importerIndices = meshProvider.GetIndices(binding.spriteId) ?? Array.Empty<int>();
            var metaVertices = BuildMeshMetaVertices(data, binding, importerVertices, importerIndices);
            var indices = BuildImporterIndices(data, metaVertices.Length, importerIndices);
            var edges = BuildImporterEdgesFromIndices(indices, metaVertices.Length);
            var importerEdges = meshProvider.GetEdges(binding.spriteId) ?? Array.Empty<Vector2Int>();
            if (!ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(
                    metaVertices,
                    indices,
                    binding.rect,
                    out var invalidReason))
            {
                error = $"Refusing to write invalid mesh for '{GetLayerDisplayName(data.layer)}': {invalidReason}.";
                return false;
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

    private static Vertex2DMetaData[] BuildMeshMetaVertices(
        LayerVertexData data,
        LayerSpriteBinding binding,
        IReadOnlyList<Vertex2DMetaData> currentImporterVertices,
        IReadOnlyList<int> currentImporterIndices)
    {
        if (data == null || data.vertices == null || data.layer == null)
            return Array.Empty<Vertex2DMetaData>();

        var importerCount = currentImporterVertices != null ? currentImporterVertices.Count : 0;
        var targetCount = Mathf.Max(importerCount, data.vertices.Length);
        var result = new Vertex2DMetaData[targetCount];

        // Keep existing importer vertices untouched; append only new vertices.
        for (var i = 0; i < importerCount; i++)
            result[i] = currentImporterVertices[i];

        for (var i = importerCount; i < targetCount; i++)
        {
            var docPos = data.vertices[i];
            result[i].position = ConvertDocToImporterPosition(data, binding, currentImporterVertices, currentImporterIndices, docPos);

            if (data.boneWeights != null && i < data.boneWeights.Length)
            {
                var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
                result[i].boneWeight = spriteBoneCount > 0
                    ? ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(data.boneWeights[i], spriteBoneCount, false, true)
                    : data.boneWeights[i];
            }
            else if (importerCount > 0)
            {
                var nearest = FindNearestReferenceVertexIndex(data.vertices, importerCount, docPos);
                if (nearest >= 0 && nearest < importerCount)
                    result[i].boneWeight = currentImporterVertices[nearest].boneWeight;
                else
                    result[i].boneWeight = currentImporterVertices[Mathf.Min(importerCount - 1, i)].boneWeight;
            }
            else
            {
                result[i].boneWeight = default;
            }
        }

        return result;
    }

    private static IReadOnlyList<Vertex2DMetaData> NormalizeRuntimeUnitImporterVertices(
        IReadOnlyList<Vertex2DMetaData> currentImporterVertices,
        LayerSpriteBinding binding)
    {
        if (!ReanimatorImporterGeometryAlgorithms.IsLikelyRuntimeUnitImporterGeometry(
                currentImporterVertices,
                binding.rect,
                binding.pixelsPerUnit,
                binding.pivotPixels))
            return currentImporterVertices;

        return ReanimatorImporterGeometryAlgorithms.ConvertRuntimeUnitVerticesToRawLocal(
            currentImporterVertices,
            binding.pixelsPerUnit,
            binding.pivotPixels);
    }

    private static bool AreMetaVerticesEqual(IReadOnlyList<Vertex2DMetaData> lhs, IReadOnlyList<Vertex2DMetaData> rhs)
    {
        var lhsCount = lhs != null ? lhs.Count : 0;
        var rhsCount = rhs != null ? rhs.Count : 0;
        if (lhsCount != rhsCount)
            return false;

        for (var i = 0; i < lhsCount; i++)
        {
            if (!AreMetaVerticesEqual(lhs[i], rhs[i]))
                return false;
        }

        return true;
    }

    private static bool AreMetaVerticesEqual(Vertex2DMetaData lhs, Vertex2DMetaData rhs)
    {
        if (!ApproximatelyEqual(lhs.position.x, rhs.position.x) ||
            !ApproximatelyEqual(lhs.position.y, rhs.position.y))
            return false;

        return AreBoneWeightsEqual(lhs.boneWeight, rhs.boneWeight);
    }

    private static bool AreBoneWeightsEqual(BoneWeight lhs, BoneWeight rhs)
    {
        return lhs.boneIndex0 == rhs.boneIndex0 &&
               lhs.boneIndex1 == rhs.boneIndex1 &&
               lhs.boneIndex2 == rhs.boneIndex2 &&
               lhs.boneIndex3 == rhs.boneIndex3 &&
               ApproximatelyEqual(lhs.weight0, rhs.weight0) &&
               ApproximatelyEqual(lhs.weight1, rhs.weight1) &&
               ApproximatelyEqual(lhs.weight2, rhs.weight2) &&
               ApproximatelyEqual(lhs.weight3, rhs.weight3);
    }

    private static bool AreEdgesEqual(IReadOnlyList<Vector2Int> lhs, IReadOnlyList<Vector2Int> rhs)
    {
        var lhsCount = lhs != null ? lhs.Count : 0;
        var rhsCount = rhs != null ? rhs.Count : 0;
        if (lhsCount != rhsCount)
            return false;

        for (var i = 0; i < lhsCount; i++)
        {
            if (lhs[i].x != rhs[i].x || lhs[i].y != rhs[i].y)
                return false;
        }

        return true;
    }

    private static bool AreIndicesEqual(IReadOnlyList<int> lhs, IReadOnlyList<int> rhs)
    {
        var lhsCount = lhs != null ? lhs.Count : 0;
        var rhsCount = rhs != null ? rhs.Count : 0;
        if (lhsCount != rhsCount)
            return false;

        for (var i = 0; i < lhsCount; i++)
        {
            if (lhs[i] != rhs[i])
                return false;
        }

        return true;
    }

    private static bool ApproximatelyEqual(float lhs, float rhs)
    {
        return Mathf.Abs(lhs - rhs) <= 0.0001f;
    }

    private static bool RectsRoughlyEqual(Rect a, Rect b)
    {
        const float eps = 0.01f;
        return Mathf.Abs(a.x - b.x) <= eps
               && Mathf.Abs(a.y - b.y) <= eps
               && Mathf.Abs(a.width - b.width) <= eps
               && Mathf.Abs(a.height - b.height) <= eps;
    }
}
#endif
