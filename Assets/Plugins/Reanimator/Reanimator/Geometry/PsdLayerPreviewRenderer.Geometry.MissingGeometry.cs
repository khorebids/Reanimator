#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static string GetLayerDisplayName(LayerInfo layer)
    {
        return layer != null && !string.IsNullOrEmpty(layer.name) ? layer.name : "<unknown>";
    }

    internal static bool RepairInvalidSpriteOutlines(string assetPath, out string error)
    {
        error = null;
        var importer = AssetImporter.GetAtPath(assetPath);
        if (!(importer is ISpriteEditorDataProvider provider))
            return true;

        provider.InitSpriteEditorDataProvider();
        var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
        var texture = provider.GetDataProvider<ITextureDataProvider>();
        if (mesh == null || texture == null)
            return true;

        var repairs = new List<(GUID id, Vertex2DMetaData[] vertices, int[] indices)>();
        foreach (var sprite in provider.GetSpriteRects())
        {
            var original = mesh.GetVertices(sprite.spriteID);
            var indices = mesh.GetIndices(sprite.spriteID);
            if (original == null || indices == null || indices.Length == 0 ||
                ReanimatorImporterGeometryAlgorithms.HasClosedSpriteOutline(indices, original.Length))
                continue;

            var binding = new LayerSpriteBinding { rect = sprite.rect };
            if (!TryGenerateDefaultGeometryFromTextureRect(texture, binding, out var positions, out var rebuilt, out _, out _) ||
                !ReanimatorImporterGeometryAlgorithms.HasClosedSpriteOutline(rebuilt, positions.Length))
            {
                error = $"Could not rebuild the sprite outline for '{sprite.name}'.";
                return false;
            }

            var vertices = new Vertex2DMetaData[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                vertices[i].position = positions[i];
                var nearest = -1;
                var distance = float.PositiveInfinity;
                for (var j = 0; j < original.Length; j++)
                {
                    var candidate = (original[j].position - positions[i]).sqrMagnitude;
                    if (candidate >= distance)
                        continue;
                    nearest = j;
                    distance = candidate;
                }

                if (nearest >= 0)
                    vertices[i].boneWeight = original[nearest].boneWeight;
            }
            repairs.Add((sprite.spriteID, vertices, rebuilt));
        }

        if (repairs.Count == 0)
            return true;

        ReanimatorAssetMetaPersistence.RegisterImporterUndo(importer, "Repair Sprite Outlines");
        foreach (var repair in repairs)
        {
            mesh.SetVertices(repair.id, repair.vertices);
            mesh.SetIndices(repair.id, repair.indices);
            mesh.SetEdges(repair.id, BuildImporterEdgesFromIndices(repair.indices, repair.vertices.Length));
        }

        using (ReanimatorPsdSpritePositionPostprocessor.SuppressForAsset(assetPath))
        {
            provider.Apply();
            AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
        return true;
    }

    public bool GenerateMissingGeometry(
        string assetPath,
        out int generatedCount,
        out string error)
    {
        generatedCount = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "Select PSD/PSB first";
            return false;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        var dataProvider = importer as ISpriteEditorDataProvider;
        if (importer == null || dataProvider == null)
        {
            error = "PSD importer data provider unavailable";
            return false;
        }

        try
        {
            dataProvider.InitSpriteEditorDataProvider();
        }
        catch
        {
            error = "Failed to initialize sprite data provider";
            return false;
        }

        var textureDataProvider = dataProvider.GetDataProvider<ITextureDataProvider>();
        var meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
        if (textureDataProvider == null || textureDataProvider.texture == null || meshProvider == null)
        {
            error = "Sprite texture or mesh provider unavailable";
            return false;
        }

        var bindings = BuildLayerSpriteBindings(dataProvider);
        if (bindings.Count == 0)
        {
            error = "No sprite bindings found";
            return false;
        }

        var changed = false;
        var undoRegistered = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in bindings)
        {
            var binding = kv.Value;
            var spriteIdKey = binding.spriteId.ToString();
            if (!seen.Add(spriteIdKey))
                continue;

            var currentVertices = meshProvider.GetVertices(binding.spriteId) ?? Array.Empty<Vertex2DMetaData>();
            var currentIndices = meshProvider.GetIndices(binding.spriteId) ?? Array.Empty<int>();
            var currentEdges = meshProvider.GetEdges(binding.spriteId) ?? Array.Empty<Vector2Int>();
            if (!ShouldGenerateMissingGeometry(currentVertices, currentIndices, currentEdges))
                continue;

            if (!TryGenerateAutoGeometryLocal(
                    textureDataProvider,
                    binding,
                    10,
                    10,
                    0,
                    out var localVertices,
                    out var triangles,
                    out _,
                    out _))
                continue;

            if (localVertices == null || localVertices.Length < 3 || triangles == null || triangles.Length < 3)
                continue;

            var metaVertices = new Vertex2DMetaData[localVertices.Length];
            for (var i = 0; i < localVertices.Length; i++)
                metaVertices[i].position = localVertices[i];

            var indices = SanitizeTriangles(triangles, metaVertices.Length);
            if (indices.Length < 3)
                continue;

            var edges = BuildImporterEdgesFromIndices(indices, metaVertices.Length);
            if (!ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(
                    metaVertices,
                    indices,
                    binding.rect,
                    out _))
            {
                continue;
            }

            if (!undoRegistered)
            {
                ReanimatorAssetMetaPersistence.RegisterImporterUndo(importer, "Generate Missing Geometry");
                undoRegistered = true;
            }
            meshProvider.SetVertices(binding.spriteId, metaVertices);
            meshProvider.SetIndices(binding.spriteId, indices);
            meshProvider.SetEdges(binding.spriteId, edges);
            generatedCount++;
            changed = true;
        }

        if (!changed)
            return true;

        if (!CommitImmediateDataProviderOperation(assetPath, dataProvider, "Generate Missing Geometry", out error))
            return false;

        MarkDirty();
        Rebuild(assetPath, false);
        return true;
    }

    public bool HasMissingGeometry()
    {
        if (layerInfos == null || layerInfos.Count == 0)
            return false;

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;

            var selectionKey = BuildSelectionKey(layer);
            if (string.IsNullOrEmpty(selectionKey) ||
                !vertexDataByLayerSelectionKey.TryGetValue(selectionKey, out var data) ||
                data == null)
                return true;

            var vertexCount = data.vertices != null ? data.vertices.Length : 0;
            var edgeCount = data.allEdgePairs != null ? data.allEdgePairs.Length / 2 : 0;
            if (ShouldGenerateMissingGeometry(vertexCount, data.triangles, edgeCount))
                return true;
        }

        return false;
    }

    private static bool ShouldGenerateMissingGeometry(
        IReadOnlyList<Vertex2DMetaData> vertices,
        IReadOnlyList<int> indices,
        IReadOnlyList<Vector2Int> edges)
    {
        return ShouldGenerateMissingGeometry(
            vertices != null ? vertices.Count : 0,
            indices,
            edges != null ? edges.Count : 0);
    }

    internal static bool ShouldGenerateMissingGeometry(
        int vertexCount,
        IReadOnlyList<int> indices,
        int edgeCount)
    {
        if (vertexCount < 8)
            return true;

        if (indices == null || SanitizeTriangles(CopyIntList(indices), vertexCount).Length < 3)
            return true;

        return edgeCount < 3;
    }

    private static int[] CopyIntList(IReadOnlyList<int> source)
    {
        if (source == null || source.Count == 0)
            return Array.Empty<int>();

        var result = new int[source.Count];
        for (var i = 0; i < source.Count; i++)
            result[i] = source[i];
        return result;
    }

}
#endif
