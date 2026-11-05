#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

public partial class PsdLayerPreviewRenderer
{
    private static bool TryBuildWorldVerticesFromImporterMesh(
        LayerInfo layer,
        LayerSpriteBinding binding,
        ISpriteMeshDataProvider meshProvider,
        Texture2D texture,
        out Vector2[] localPixels,
        out Vector2[] uvLocalPixels,
        out Vector2[] worldVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs,
        out BoneWeight[] boneWeights,
        out string importerMode)
    {
        localPixels = Array.Empty<Vector2>();
        uvLocalPixels = Array.Empty<Vector2>();
        worldVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        boneWeights = null;
        importerMode = "none";

        if (layer == null || meshProvider == null)
            return false;

        var importerVertices = meshProvider.GetVertices(binding.spriteId);
        if (importerVertices == null || importerVertices.Length == 0)
            return false;

        SelectBestImporterVerticesInterpretation(
            layer,
            binding,
            importerVertices,
            texture,
            out localPixels,
            out worldVertices,
            out importerMode);
        uvLocalPixels = BuildImporterUvLocalPixels(layer, binding, importerVertices, importerMode, localPixels);
        var vertexCount = importerVertices.Length;
        boneWeights = new BoneWeight[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            boneWeights[i] = importerVertices[i].boneWeight;

        var importerIndices = meshProvider.GetIndices(binding.spriteId);
        if (importerIndices != null && importerIndices.Length >= 3)
        {
            var raw = new int[importerIndices.Length];
            for (var i = 0; i < importerIndices.Length; i++)
                raw[i] = importerIndices[i];
            triangles = SanitizeTriangles(raw, vertexCount);
        }

        var importerEdges = meshProvider.GetEdges(binding.spriteId);
        if (importerEdges != null && importerEdges.Length > 0)
        {
            BuildEdgePairsFromImporterEdges(importerEdges, triangles, vertexCount, out allEdgePairs, out boundaryEdgePairs);
        }
        else if (triangles.Length >= 3)
        {
            BuildEdgePairs(triangles, vertexCount, out allEdgePairs, out boundaryEdgePairs);
        }

        return true;
    }

    private static bool TryBuildWorldVerticesFromSerializedMesh(
        LayerInfo layer,
        LayerSpriteBinding binding,
        SerializedSpriteMeshData meshData,
        Texture2D texture,
        out Vector2[] localPixels,
        out Vector2[] uvLocalPixels,
        out Vector2[] worldVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs,
        out BoneWeight[] boneWeights,
        out string importerMode)
    {
        localPixels = Array.Empty<Vector2>();
        uvLocalPixels = Array.Empty<Vector2>();
        worldVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        boneWeights = null;
        importerMode = "none";

        if (layer == null || meshData == null || meshData.vertices == null || meshData.vertices.Length == 0)
            return false;

        SelectBestImporterVerticesInterpretation(
            layer,
            binding,
            meshData.vertices,
            texture,
            out localPixels,
            out worldVertices,
            out importerMode);
        uvLocalPixels = BuildImporterUvLocalPixels(layer, binding, meshData.vertices, importerMode, localPixels);
        var vertexCount = meshData.vertices.Length;
        boneWeights = new BoneWeight[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            boneWeights[i] = meshData.vertices[i].boneWeight;

        triangles = SanitizeTriangles(meshData.indices, vertexCount);
        BuildEdgePairsFromImporterEdges(meshData.edges, triangles, vertexCount, out allEdgePairs, out boundaryEdgePairs);
        return worldVertices != null && worldVertices.Length > 0;
    }

    private static bool ShouldPreferSerializedMesh(
        SerializedSpriteMeshData serializedMesh,
        IReadOnlyList<Vector2> currentVertices,
        IReadOnlyList<int> currentTriangles)
    {
        if (serializedMesh == null || serializedMesh.vertices == null || serializedMesh.vertices.Length == 0)
            return false;

        var currentVertexCount = currentVertices != null ? currentVertices.Count : 0;
        var currentTriangleIndexCount = currentTriangles != null ? currentTriangles.Count : 0;
        if (currentVertexCount <= 0 || currentTriangleIndexCount <= 0)
            return true;

        var serializedTriangleIndexCount = serializedMesh.indices != null ? serializedMesh.indices.Length : 0;
        if (serializedMesh.vertices.Length >= currentVertexCount + 4 &&
            serializedTriangleIndexCount >= currentTriangleIndexCount)
            return true;

        return serializedTriangleIndexCount >= currentTriangleIndexCount + 12;
    }

    private Dictionary<string, SerializedSpriteMeshData> BuildSerializedSpriteMeshDataBySelectionKey(PSDImporter importer)
    {
        return ReanimatorSerializedSpriteMeshReader.BuildBySelectionKey(importer, layerInfos, BuildSelectionKey);
    }

    private static void BuildEdgePairsFromImporterEdges(
        IReadOnlyList<Vector2Int> edges,
        int[] triangles,
        int vertexCount,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs)
    {
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        if (vertexCount <= 0)
            return;

        var seen = new HashSet<ulong>();

        void AddPair(int a, int b, List<int> target)
        {
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                return;

            var min = Mathf.Min(a, b);
            var max = Mathf.Max(a, b);
            var key = ((ulong)(uint)min << 32) | (uint)max;
            if (!seen.Add(key))
                return;

            target.Add(a);
            target.Add(b);
        }

        var pairs = new List<int>(
            Mathf.Max(
                edges != null ? edges.Count * 2 : 0,
                triangles != null ? triangles.Length * 2 : 0));

        if (triangles != null && triangles.Length >= 3)
        {
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddPair(triangles[i], triangles[i + 1], pairs);
                AddPair(triangles[i + 1], triangles[i + 2], pairs);
                AddPair(triangles[i + 2], triangles[i], pairs);
            }

            BuildEdgePairs(triangles, vertexCount, out _, out boundaryEdgePairs);
        }

        if (edges != null)
        {
            for (var i = 0; i < edges.Count; i++)
                AddPair(edges[i].x, edges[i].y, pairs);
        }

        allEdgePairs = pairs.Count == 0 ? Array.Empty<int>() : pairs.ToArray();
        if (boundaryEdgePairs == null || boundaryEdgePairs.Length == 0)
            boundaryEdgePairs = allEdgePairs;
    }

    private static Vector2[] BuildImporterUvLocalPixels(
        LayerInfo layer,
        LayerSpriteBinding binding,
        IReadOnlyList<Vertex2DMetaData> importerVertices,
        string importerMode,
        IReadOnlyList<Vector2> fallbackLocalPixels)
    {
        if (layer == null || importerVertices == null || importerVertices.Count == 0)
            return CopyVector2List(fallbackLocalPixels);

        var flipLocalY = false;
        var mode = importerMode ?? string.Empty;
        const string flipSuffix = "_flip_y";
        if (mode.EndsWith(flipSuffix, StringComparison.Ordinal))
        {
            mode = mode.Substring(0, mode.Length - flipSuffix.Length);
            flipLocalY = true;
        }

        if (!TryParseImporterInterpretation(mode, out var interpretation))
            return CopyVector2List(fallbackLocalPixels);

        var transform = new GeometryPreviewTransform(layer);
        transform.FromImporterVertices(
            importerVertices,
            interpretation,
            binding.pixelsPerUnit,
            binding.pivotPixels,
            new Vector2(layer.sourceRect.x, layer.sourceRect.y),
            flipLocalY,
            Mathf.Max(1f, layer.sourceRect.height),
            out var uvLocalPixels,
            out _);

        return uvLocalPixels != null && uvLocalPixels.Length == importerVertices.Count
            ? uvLocalPixels
            : CopyVector2List(fallbackLocalPixels);
    }

    private static bool TryParseImporterInterpretation(
        string mode,
        out GeometryPreviewTransform.ImporterInterpretation interpretation)
    {
        switch (mode)
        {
            case "raw_local":
                interpretation = GeometryPreviewTransform.ImporterInterpretation.RawLocal;
                return true;
            case "raw_plus_pivot":
                interpretation = GeometryPreviewTransform.ImporterInterpretation.RawPlusPivot;
                return true;
            case "raw_ppu_plus_pivot":
                interpretation = GeometryPreviewTransform.ImporterInterpretation.RawTimesPpuPlusPivot;
                return true;
            case "raw_minus_source_rect":
                interpretation = GeometryPreviewTransform.ImporterInterpretation.RawMinusSourceRect;
                return true;
            case "raw_as_world":
                interpretation = GeometryPreviewTransform.ImporterInterpretation.RawAsWorld;
                return true;
            default:
                interpretation = GeometryPreviewTransform.ImporterInterpretation.RawLocal;
                return false;
        }
    }

    private static Vector2[] CopyVector2List(IReadOnlyList<Vector2> source)
    {
        if (source == null || source.Count == 0)
            return Array.Empty<Vector2>();

        var result = new Vector2[source.Count];
        for (var i = 0; i < source.Count; i++)
            result[i] = source[i];
        return result;
    }

    private Vector2[] BuildVertexUvsFromLocalPixels(LayerInfo layer, IReadOnlyList<Vector2> localPixels)
    {
        var texture = layer != null && layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
        return BuildVertexUvsFromLocalPixels(layer, localPixels, texture);
    }

    private static bool TryCopySpriteUvs(Sprite sprite, int vertexCount, out Vector2[] uvs)
    {
        uvs = null;
        if (sprite == null || vertexCount <= 0)
            return false;

        var spriteUvs = sprite.uv;
        if (spriteUvs == null || spriteUvs.Length != vertexCount)
            return false;

        uvs = new Vector2[vertexCount];
        Array.Copy(spriteUvs, uvs, vertexCount);
        return true;
    }

    private static Vector2[] BuildVertexUvsFromLocalPixels(
        LayerInfo layer,
        IReadOnlyList<Vector2> localPixels,
        Texture2D texture)
    {
        if (layer == null || localPixels == null || localPixels.Count == 0 || texture == null)
            return Array.Empty<Vector2>();

        var result = new Vector2[localPixels.Count];
        for (var i = 0; i < localPixels.Count; i++)
            result[i] = ComputeTextureUvForLayerLocalPixel(layer, localPixels[i], texture);

        return result;
    }

    private static float ScoreVerticesAgainstLayer(IReadOnlyList<Vector2> vertices, Rect layerRect)
    {
        if (vertices == null || vertices.Count == 0)
            return float.MaxValue;
        if (layerRect.width <= 0f || layerRect.height <= 0f)
            return float.MaxValue;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < vertices.Count; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return float.MaxValue;

        var bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        var refSize = Mathf.Max(1f, Mathf.Max(layerRect.width, layerRect.height));
        var centerPenalty = Vector2.Distance(bounds.center, layerRect.center) / refSize;

        var bw = Mathf.Max(1f, bounds.width);
        var bh = Mathf.Max(1f, bounds.height);
        var sw = Mathf.Max(1f, layerRect.width);
        var sh = Mathf.Max(1f, layerRect.height);
        var scalePenalty = Mathf.Abs(Mathf.Log(bw / sw)) + Mathf.Abs(Mathf.Log(bh / sh));

        var outLeft = Mathf.Max(0f, layerRect.xMin - bounds.xMin);
        var outRight = Mathf.Max(0f, bounds.xMax - layerRect.xMax);
        var outBottom = Mathf.Max(0f, layerRect.yMin - bounds.yMin);
        var outTop = Mathf.Max(0f, bounds.yMax - layerRect.yMax);
        var outsidePenalty = (outLeft + outRight + outBottom + outTop) / refSize;

        return centerPenalty + scalePenalty + outsidePenalty * 0.75f;
    }

    private static bool TryGetBounds(IReadOnlyList<Vector2> points, out Rect bounds)
    {
        bounds = Rect.zero;
        if (points == null || points.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return false;

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static void SelectBestImporterVerticesInterpretation(
        LayerInfo layer,
        LayerSpriteBinding binding,
        IReadOnlyList<Vertex2DMetaData> importerVertices,
        Texture2D texture,
        out Vector2[] localPixels,
        out Vector2[] worldVertices,
        out string mode)
    {
        localPixels = Array.Empty<Vector2>();
        worldVertices = Array.Empty<Vector2>();
        mode = "none";
        if (layer == null || importerVertices == null || importerVertices.Count == 0)
            return;

        var transform = new GeometryPreviewTransform(layer);
        var targetRect = PsdCoordinateSpace.GetLayerDocumentRect(layer);
        var sourceOffset = new Vector2(layer.sourceRect.x, layer.sourceRect.y);
        var flipHeight = Mathf.Max(1f, layer.sourceRect.height);
        var bestScore = float.MaxValue;
        var bestLocal = Array.Empty<Vector2>();
        var bestWorld = Array.Empty<Vector2>();
        var bestMode = "raw_local";

        transform.FromImporterVertices(
            importerVertices,
            GeometryPreviewTransform.ImporterInterpretation.RawLocal,
            binding.pixelsPerUnit,
            binding.pivotPixels,
            sourceOffset,
            false,
            flipHeight,
            out var rawLocalPixels,
            out var rawLocalWorldVertices);
        if (IsCanonicalRawLocalImporterGeometry(layer, rawLocalPixels, rawLocalWorldVertices, targetRect))
        {
            localPixels = rawLocalPixels;
            worldVertices = rawLocalWorldVertices;
            mode = "raw_local";
            return;
        }

        void Evaluate(
            GeometryPreviewTransform.ImporterInterpretation interpretation,
            string interpretationName,
            bool flipLocalY)
        {
            transform.FromImporterVertices(
                importerVertices,
                interpretation,
                binding.pixelsPerUnit,
                binding.pivotPixels,
                sourceOffset,
                flipLocalY,
                flipHeight,
                out var candidateLocal,
                out var candidateWorld);
            if (candidateWorld == null || candidateWorld.Length == 0)
                return;

            var score = ScoreVerticesAgainstLayer(candidateWorld, targetRect) +
                        ScoreLocalPixelsAgainstSpriteAlpha(layer, candidateLocal, texture);
            if (flipLocalY)
                score += 0.85f;
            if (score >= bestScore)
                return;

            bestScore = score;
            bestLocal = candidateLocal;
            bestWorld = candidateWorld;
            bestMode = flipLocalY ? interpretationName + "_flip_y" : interpretationName;
        }

        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawLocal, "raw_local", false);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawLocal, "raw_local", true);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawPlusPivot, "raw_plus_pivot", false);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawPlusPivot, "raw_plus_pivot", true);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawTimesPpuPlusPivot, "raw_ppu_plus_pivot", false);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawTimesPpuPlusPivot, "raw_ppu_plus_pivot", true);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawMinusSourceRect, "raw_minus_source_rect", false);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawMinusSourceRect, "raw_minus_source_rect", true);
        Evaluate(GeometryPreviewTransform.ImporterInterpretation.RawAsWorld, "raw_as_world", false);

        localPixels = bestLocal;
        worldVertices = bestWorld;
        mode = bestMode;
    }

    private static bool IsCanonicalRawLocalImporterGeometry(
        LayerInfo layer,
        IReadOnlyList<Vector2> localPixels,
        IReadOnlyList<Vector2> worldVertices,
        Rect targetRect)
    {
        if (layer == null || localPixels == null || worldVertices == null || localPixels.Count == 0 || worldVertices.Count == 0)
            return false;
        if (layer.sourceRect.width <= 0f || layer.sourceRect.height <= 0f)
            return false;
        if (!TryGetBounds(localPixels, out var localBounds))
            return false;

        var padding = Mathf.Max(1f, Mathf.Min(layer.sourceRect.width, layer.sourceRect.height) * 0.04f);
        if (localBounds.xMin < -padding ||
            localBounds.yMin < -padding ||
            localBounds.xMax > layer.sourceRect.width + padding ||
            localBounds.yMax > layer.sourceRect.height + padding)
        {
            return false;
        }

        return ScoreVerticesAgainstLayer(worldVertices, targetRect) <= 0.05f;
    }

    private static float ScoreLocalPixelsAgainstSpriteAlpha(
        LayerInfo layer,
        IReadOnlyList<Vector2> localPixels,
        Texture2D texture)
    {
        if (layer == null || localPixels == null || localPixels.Count == 0 || texture == null)
            return 0f;

        var textureRect = GetTextureRect(layer, new Vector2(texture.width, texture.height));
        if (textureRect.width <= 0f || textureRect.height <= 0f || texture.width <= 0 || texture.height <= 0)
            return 0f;

        try
        {
            var alphaSum = 0f;
            var outside = 0;
            var count = 0;
            for (var i = 0; i < localPixels.Count; i++)
            {
                var p = localPixels[i];
                if (p.x < -0.5f || p.y < -0.5f || p.x > textureRect.width + 0.5f || p.y > textureRect.height + 0.5f)
                {
                    outside++;
                    count++;
                    continue;
                }

                var x = Mathf.RoundToInt(textureRect.x + p.x);
                var y = Mathf.RoundToInt(textureRect.y + p.y);
                alphaSum += SampleMaxAlpha(texture, x, y);
                count++;
            }

            if (count == 0)
                return 0f;

            var alphaPenalty = 1f - Mathf.Clamp01(alphaSum / count);
            var outsidePenalty = outside / (float)count;
            return alphaPenalty * 1.25f + outsidePenalty * 2f;
        }
        catch
        {
            return 0f;
        }
    }

    private static float SampleMaxAlpha(Texture2D texture, int x, int y)
    {
        var maxAlpha = 0f;
        for (var oy = -1; oy <= 1; oy++)
        {
            var py = Mathf.Clamp(y + oy, 0, texture.height - 1);
            for (var ox = -1; ox <= 1; ox++)
            {
                var px = Mathf.Clamp(x + ox, 0, texture.width - 1);
                maxAlpha = Mathf.Max(maxAlpha, texture.GetPixel(px, py).a);
            }
        }

        return maxAlpha;
    }

    private static Vector2[] ExtractImporterRawPositions(IReadOnlyList<Vertex2DMetaData> importerVertices)
    {
        if (importerVertices == null || importerVertices.Count == 0)
            return Array.Empty<Vector2>();

        var result = new Vector2[importerVertices.Count];
        for (var i = 0; i < importerVertices.Count; i++)
            result[i] = importerVertices[i].position;
        return result;
    }
}
#endif
