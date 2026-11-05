#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    // ALLIN uses this path instead of the interactive Auto Geometry controls.
    // Keep the production profile dense enough for deformation: a simplified
    // outline with no interior tessellation produces valid but unusable giant
    // triangles on long limbs and broad clothing layers.
    private const int DefaultGeometryOutlineDetail = 10;
    private const int DefaultGeometryAlphaTolerance = 10;
    private const int DefaultGeometryQualitySubdivide = 100;

    public int GenerateDefaultGeometryForSpriteSheet(string assetPath, out string error)
    {
        return GenerateDefaultGeometryForSpriteSheet(assetPath, forceAll: false, out error);
    }

    public int GenerateDefaultGeometryForAllSprites(string assetPath, out string error)
    {
        return GenerateDefaultGeometryForSpriteSheet(assetPath, forceAll: true, out error);
    }

    private int GenerateDefaultGeometryForSpriteSheet(string assetPath, bool forceAll, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "Select PSD/PSB first";
            return 0;
        }

        var targetLayers = GetSpriteSheetOperationLayers(forceAll);
        if (targetLayers.Count == 0)
        {
            error = !forceAll && !string.IsNullOrEmpty(selectedLayerSelectionKey)
                ? "Selected sprite is missing"
                : "No sprites found";
            return 0;
        }

        RegisterUndo("Generate Default Geometry");

        var generated = 0;
        string lastError = null;
        for (var i = 0; i < targetLayers.Count; i++)
        {
            if (GenerateDefaultGeometryForLayer(assetPath, targetLayers[i], out lastError))
                generated++;
        }

        error = generated > 0 ? null : lastError;
        return generated;
    }

    public bool GenerateDefaultGeometryForSelected(string assetPath, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "Select PSD/PSB first";
            return false;
        }

        if (string.IsNullOrEmpty(selectedLayerSelectionKey))
        {
            error = "Select sprite first";
            return false;
        }

        var selectedLayer = FindLayerBySelectionKey(layerInfos, selectedLayerSelectionKey);
        if (selectedLayer == null)
        {
            error = "Selected sprite is missing";
            return false;
        }

        RegisterUndo("Generate Default Geometry");
        return GenerateDefaultGeometryForLayer(assetPath, selectedLayer, out error);
    }

    private bool GenerateDefaultGeometryForLayer(string assetPath, LayerInfo selectedLayer, out string error)
    {
        error = null;
        if (selectedLayer == null)
        {
            error = "Selected sprite is missing";
            return false;
        }

        var selectionKey = BuildSelectionKey(selectedLayer);
        if (string.IsNullOrEmpty(selectionKey))
        {
            error = "Selected sprite key is missing";
            return false;
        }

        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (!TryGetLayerVertexData(selectedLayer, out var layerData) || layerData == null)
        {
            layerData = new LayerVertexData
            {
                layer = selectedLayer,
                texture = selectedLayer.texture,
                pixelsPerUnit = 1f,
                pivotPixels = Vector2.zero,
                lastPoseVersion = -1
            };
        }
        vertexDataByLayerSelectionKey[selectionKey] = layerData;

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        var dataProvider = importer as ISpriteEditorDataProvider;
        if (importer == null || dataProvider == null)
            return GenerateDefaultGeometryFromSnapshotFallback(assetPath, selectedLayer, selectionKey, stableKey, layerData, out error);

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
        if (textureDataProvider == null || textureDataProvider.texture == null)
        {
            error = "Texture data provider unavailable";
            return false;
        }

        var textureSize = new Vector2(textureDataProvider.texture.width, textureDataProvider.texture.height);
        if (!TryBuildDefaultGeometryBindingFromProvider(dataProvider, selectedLayer, selectionKey, stableKey, out var binding))
        {
            binding = BuildDefaultGeometryBindingForLayer(
                selectedLayer,
                layerData,
                textureSize);
        }
        if (binding.rect.width <= 0f || binding.rect.height <= 0f)
        {
            error = "Selected sprite rect is invalid";
            return false;
        }

        var samplingLayer = BuildDefaultGeometrySamplingLayer(selectedLayer, binding.rect);
        var localVertices = Array.Empty<Vector2>();
        var triangles = Array.Empty<int>();
        var allEdgePairs = Array.Empty<int>();
        var boundaryEdgePairs = Array.Empty<int>();
        var generated = TryGenerateDefaultGeometryFromTextureRect(
            textureDataProvider,
            binding,
            out localVertices,
            out triangles,
            out allEdgePairs,
            out boundaryEdgePairs);
        if (generated)
        {
            localVertices = OrientDefaultGeometryLocalVerticesToOpaquePixels(
                samplingLayer,
                textureDataProvider.texture,
                localVertices,
                triangles);
            if (!GeneratedDefaultGeometryCoversLayer(
                    samplingLayer,
                    textureDataProvider.texture,
                    localVertices,
                    triangles))
                generated = false;
        }
        if (!generated &&
            TryBuildDefaultGeometrySamplingFallbackBindingForLayer(
                selectedLayer,
                layerData,
                textureSize,
                binding,
                out var samplingBinding,
                out var samplingToLogicalScale))
        {
            var fallbackSamplingLayer = BuildDefaultGeometrySamplingLayer(selectedLayer, samplingBinding.rect);
            generated = TryGenerateDefaultGeometryFromTextureRect(
                textureDataProvider,
                samplingBinding,
                out localVertices,
                out triangles,
                out allEdgePairs,
                out boundaryEdgePairs);
            if (generated)
                localVertices = ScaleDefaultGeometryLocalVertices(localVertices, samplingToLogicalScale);
            if (generated)
                localVertices = OrientDefaultGeometryLocalVerticesToOpaquePixels(
                    fallbackSamplingLayer,
                    textureDataProvider.texture,
                    localVertices,
                    triangles);
            if (generated &&
                !GeneratedDefaultGeometryCoversLayer(fallbackSamplingLayer, textureDataProvider.texture, localVertices, triangles))
                generated = false;
            if (generated)
                samplingLayer = fallbackSamplingLayer;
        }
        if (!generated &&
            TryGetDefaultGeometrySnapshot(selectedLayer, out var snapshot) &&
            IsDefaultGeometrySnapshotUsableForLayer(snapshot, selectedLayer, textureDataProvider.texture))
        {
            return GenerateDefaultGeometryFromSnapshotFallback(
                assetPath,
                selectedLayer,
                selectionKey,
                stableKey,
                layerData,
                refineForDeformation: true,
                out error);
        }
        if (!generated)
        {
            error = "Failed to generate default geometry from sprite alpha";
            return false;
        }

        localVertices = OrientDefaultGeometryLocalVerticesToOpaquePixels(
            samplingLayer,
            textureDataProvider.texture,
            localVertices,
            triangles);

        var docVertices = new Vector2[localVertices.Length];
        for (var i = 0; i < localVertices.Length; i++)
            docVertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(selectedLayer, localVertices[i]);

        var vertexUvs = BuildVertexUvsFromLocalPixels(samplingLayer, localVertices);
        ApplyAutoGeometryToLayerData(
            layerData,
            selectedLayer,
            docVertices,
            vertexUvs,
            triangles,
            allEdgePairs,
            boundaryEdgePairs,
            Array.Empty<BoneWeight>());
        layerData.importerMode = "raw_local";

        UpsertAutoGeometryPatch(
            assetPath,
            selectionKey,
            stableKey,
            layerData,
            false,
            "raw_local");

        hasPendingChanges = true;
        return true;
    }

    private bool TryGetDefaultGeometrySnapshot(LayerInfo selectedLayer, out DefaultGeometrySnapshot snapshot)
    {
        snapshot = null;
        var defaultGeometryKey = BuildDefaultGeometrySnapshotKey(selectedLayer);
        return !string.IsNullOrEmpty(defaultGeometryKey) &&
               defaultGeometryBySpriteKey != null &&
               defaultGeometryBySpriteKey.TryGetValue(defaultGeometryKey, out snapshot) &&
               snapshot != null &&
               snapshot.verticesDoc != null &&
               snapshot.verticesDoc.Length > 0;
    }

    private static bool IsDefaultGeometrySnapshotUsableForLayer(
        DefaultGeometrySnapshot snapshot,
        LayerInfo layer,
        Texture2D texture)
    {
        if (snapshot == null || layer == null)
            return false;
        if (!TryBuildDefaultGeometryVerticesForCurrentRect(snapshot, layer, out var remappedVertices))
            return false;
        if (!TryGetDocumentVerticesLocalBounds(layer, remappedVertices, out var vertexBounds))
            return false;
        Rect referenceBounds;
        if (!TryGetLayerOpaqueLocalBounds(layer, texture, out var opaqueBounds))
        {
            referenceBounds = new Rect(0f, 0f, layer.sourceRect.width, layer.sourceRect.height);
        }
        else
        {
            referenceBounds = opaqueBounds;
        }

        return LocalBoundsCoverReference(vertexBounds, referenceBounds, 0.35f, opaqueBounds.width > 0f ? 0.25f : 0.2f) &&
               LocalTriangleAreaCoversReference(layer, remappedVertices, snapshot.triangles, referenceBounds, 0.08f) &&
               TryGetDocumentVerticesAsLocal(layer, remappedVertices, out var localVertices) &&
               LocalTrianglesFollowOpaquePixels(layer, texture, localVertices, snapshot.triangles);
    }

    private static bool TryGenerateDefaultGeometryFromTextureRect(
        ITextureDataProvider sourceProvider,
        LayerSpriteBinding binding,
        out Vector2[] localVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs)
    {
        localVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        if (sourceProvider == null || sourceProvider.texture == null ||
            binding.rect.width <= 0f || binding.rect.height <= 0f)
            return false;

        var readableTexture = sourceProvider.GetReadableTexture2D();
        if (readableTexture == null)
            readableTexture = sourceProvider.texture;
        if (readableTexture == null)
            return false;

        Texture2D alphaTexture = null;
        try
        {
            sourceProvider.GetTextureActualWidthAndHeight(out var actualWidth, out var actualHeight);
            if (actualWidth <= 0 || actualHeight <= 0)
                return false;

            var scaleX = readableTexture.width / (float)actualWidth;
            var scaleY = readableTexture.height / (float)actualHeight;
            var xMin = Mathf.Clamp(Mathf.FloorToInt(binding.rect.xMin * scaleX), 0, readableTexture.width - 1);
            var yMin = Mathf.Clamp(Mathf.FloorToInt(binding.rect.yMin * scaleY), 0, readableTexture.height - 1);
            var xMax = Mathf.Clamp(Mathf.CeilToInt(binding.rect.xMax * scaleX), xMin + 1, readableTexture.width);
            var yMax = Mathf.Clamp(Mathf.CeilToInt(binding.rect.yMax * scaleY), yMin + 1, readableTexture.height);
            var width = xMax - xMin;
            var height = yMax - yMin;
            if (width <= 0 || height <= 0)
                return false;

            if (!TryGetTexturePixels(readableTexture, out var sourcePixels))
                return false;
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                var sourceRow = (yMin + y) * readableTexture.width + xMin;
                var targetRow = y * width;
                Array.Copy(sourcePixels, sourceRow, pixels, targetRow, width);
            }

            alphaTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            alphaTexture.SetPixels32(pixels);
            alphaTexture.Apply(false, false);

            var provider = new DefaultGeometryTextureDataProvider(alphaTexture);
            var localBinding = new LayerSpriteBinding
            {
                rect = new Rect(0f, 0f, alphaTexture.width, alphaTexture.height),
                pivotPixels = Vector2.zero,
                pixelsPerUnit = 1f
            };
            var generated = TryGenerateAutoGeometryLocal(
                provider,
                localBinding,
                DefaultGeometryOutlineDetail,
                DefaultGeometryAlphaTolerance,
                DefaultGeometryQualitySubdivide,
                out localVertices,
                out triangles,
                out allEdgePairs,
                out boundaryEdgePairs);
            if (!generated)
                return false;

            if (ReanimatorTriangulationAlgorithms.RetriangulateGeneratedGeometry(localVertices, ref triangles))
            {
                triangles = SanitizeTriangles(triangles, localVertices.Length);
                BuildEdgePairs(triangles, localVertices.Length, out allEdgePairs, out boundaryEdgePairs);
            }

            localVertices = ScaleDefaultGeometryLocalVertices(
                localVertices,
                new Vector2(binding.rect.width / width, binding.rect.height / height));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (alphaTexture != null)
                UnityEngine.Object.DestroyImmediate(alphaTexture);
        }
    }

    private sealed class DefaultGeometryTextureDataProvider : ITextureDataProvider
    {
        public Texture2D texture { get; }
        public Texture2D previewTexture => texture;

        public DefaultGeometryTextureDataProvider(Texture2D texture)
        {
            this.texture = texture;
        }

        public void GetTextureActualWidthAndHeight(out int width, out int height)
        {
            width = texture != null ? texture.width : 0;
            height = texture != null ? texture.height : 0;
        }

        public Texture2D GetReadableTexture2D()
        {
            return texture;
        }
    }

    private static bool GeneratedDefaultGeometryCoversLayer(
        LayerInfo layer,
        Texture2D texture,
        Vector2[] localVertices,
        int[] triangles)
    {
        if (layer == null || localVertices == null || localVertices.Length < 3 || triangles == null || triangles.Length < 3)
            return false;
        if (!TryGetLocalVerticesBounds(localVertices, out var vertexBounds))
            return false;

        Rect referenceBounds;
        var hasOpaqueBounds = TryGetLayerOpaqueLocalBounds(layer, texture, out var opaqueBounds);
        if (hasOpaqueBounds)
            referenceBounds = opaqueBounds;
        else
            referenceBounds = new Rect(0f, 0f, layer.sourceRect.width, layer.sourceRect.height);

        return LocalBoundsCoverReference(vertexBounds, referenceBounds, 0.35f, hasOpaqueBounds ? 0.25f : 0.2f) &&
               (!hasOpaqueBounds || LocalBoundsDoNotOverfillReference(vertexBounds, referenceBounds, 1.35f, 2.25f)) &&
               LocalTriangleAreaCoversReference(localVertices, triangles, referenceBounds, 0.005f) &&
               (!hasOpaqueBounds || LocalTrianglesFollowOpaquePixels(layer, texture, localVertices, triangles));
    }

    private bool GenerateDefaultGeometryFromSnapshotFallback(
        string assetPath,
        LayerInfo selectedLayer,
        string selectionKey,
        string stableKey,
        LayerVertexData layerData,
        out string error)
    {
        return GenerateDefaultGeometryFromSnapshotFallback(
            assetPath,
            selectedLayer,
            selectionKey,
            stableKey,
            layerData,
            refineForDeformation: false,
            out error);
    }

    private bool GenerateDefaultGeometryFromSnapshotFallback(
        string assetPath,
        LayerInfo selectedLayer,
        string selectionKey,
        string stableKey,
        LayerVertexData layerData,
        bool refineForDeformation,
        out string error)
    {
        error = null;
        var defaultGeometryKey = BuildDefaultGeometrySnapshotKey(selectedLayer);
        if (string.IsNullOrEmpty(defaultGeometryKey) ||
            defaultGeometryBySpriteKey == null ||
            !defaultGeometryBySpriteKey.TryGetValue(defaultGeometryKey, out var snapshot) ||
            snapshot == null ||
            snapshot.verticesDoc == null ||
            snapshot.verticesDoc.Length == 0)
        {
            error = "Default geometry is missing";
            return false;
        }

        if (!TryBuildDefaultGeometryVerticesForCurrentRect(snapshot, selectedLayer, out var remappedVertices))
        {
            error = "Selected sprite rect is invalid";
            return false;
        }

        ApplyDefaultGeometrySnapshotToLayerData(snapshot, layerData, selectedLayer, remappedVertices);
        if (refineForDeformation)
        {
            var refinedVertices = layerData.vertices ?? Array.Empty<Vector2>();
            var refinedTriangles = layerData.triangles ?? Array.Empty<int>();
            if (TryGenerateDefaultGeometryFromLayerHardMask(
                    selectedLayer,
                    out var alphaVertices,
                    out var alphaTriangles,
                    out var alphaAllEdgePairs,
                    out var alphaBoundaryEdgePairs))
            {
                layerData.vertices = alphaVertices;
                layerData.triangles = alphaTriangles;
                layerData.allEdgePairs = alphaAllEdgePairs;
                layerData.boundaryEdgePairs = alphaBoundaryEdgePairs;
                layerData.vertexUvs = BuildVertexUvsFromLayer(selectedLayer, alphaVertices);
                layerData.boneWeights = new BoneWeight[alphaVertices.Length];
                layerData.importerMode = "raw_local";
                refinedVertices = Array.Empty<Vector2>();
                refinedTriangles = Array.Empty<int>();
            }

            if (refinedVertices.Length >= 3 && refinedTriangles.Length >= 3)
            {
            refinedTriangles = SanitizeTriangles(refinedTriangles, refinedVertices.Length);
            BuildEdgePairs(
                refinedTriangles,
                refinedVertices.Length,
                out _,
                out var rebuiltBoundaryEdgePairs);
            var sourceBoundaryEdgePairs = layerData.boundaryEdgePairs != null && layerData.boundaryEdgePairs.Length >= 6
                ? layerData.boundaryEdgePairs
                : rebuiltBoundaryEdgePairs;
            var sourceBoundaryEdges = BuildDefaultGeometryBoundaryEdges(
                sourceBoundaryEdgePairs,
                refinedVertices.Length);
            var tessellationVertices = BuildDefaultGeometrySnapshotTessellationVertices(
                refinedVertices,
                refinedTriangles);
            var tessellationBoundaryEdges = BuildDensifiedDefaultGeometryBoundaryEdges(
                tessellationVertices,
                sourceBoundaryEdges);
            if (tessellationBoundaryEdges.Count >= 3 &&
                TryTriangulateWithUnityTessellation(
                    tessellationVertices,
                    tessellationBoundaryEdges,
                    0,
                    out var tessellatedVertices,
                    out var tessellatedTriangles,
                    out _))
            {
                tessellatedTriangles = SanitizeTriangles(tessellatedTriangles, tessellatedVertices.Length);
                if (tessellatedVertices.Length >= 3 && tessellatedTriangles.Length >= 3)
                {
                    refinedVertices = tessellatedVertices;
                    refinedTriangles = tessellatedTriangles;
                }
            }

            ReanimatorTriangulationAlgorithms.RetriangulateGeneratedGeometry(
                refinedVertices,
                ref refinedTriangles);
            refinedTriangles = SanitizeTriangles(refinedTriangles, refinedVertices.Length);
            layerData.vertices = refinedVertices;
            layerData.triangles = refinedTriangles;
            BuildEdgePairs(
                refinedTriangles,
                refinedVertices.Length,
                out layerData.allEdgePairs,
                out layerData.boundaryEdgePairs);
            layerData.vertexUvs = BuildVertexUvsFromLayer(selectedLayer, refinedVertices);
            layerData.boneWeights = new BoneWeight[refinedVertices.Length];
            layerData.importerMode = "raw_local";
            }
        }
        if (layerData.vertexUvs == null || layerData.vertexUvs.Length != layerData.vertices.Length)
            layerData.vertexUvs = BuildVertexUvsFromLayer(selectedLayer, layerData.vertices);
        EnsureBoneWeightsArraySize(layerData, layerData.vertices.Length);

        UpsertAutoGeometryPatch(
            assetPath,
            selectionKey,
            stableKey,
            layerData,
            false,
            refineForDeformation ? "raw_local" : null);

        hasPendingChanges = true;
        return true;
    }

    private bool TryGenerateDefaultGeometryFromLayerHardMask(
        LayerInfo layer,
        out Vector2[] documentVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs)
    {
        documentVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        if (layer == null || layer.sourceRect.width <= 0f || layer.sourceRect.height <= 0f)
            return false;

        var hardMask = GetReanimaxHardMaskPreviewTexture(
            layer,
            DefaultGeometryAlphaTolerance / 255f);
        if (hardMask == null || hardMask.width <= 0 || hardMask.height <= 0)
            return false;

        Texture2D alphaTexture = null;
        try
        {
            var maskPixels = hardMask.GetPixels32();
            var alphaPixels = new Color32[maskPixels.Length];
            var opaquePixelCount = 0;
            for (var i = 0; i < maskPixels.Length; i++)
            {
                var alpha = maskPixels[i].r;
                if (alpha > 0)
                    opaquePixelCount++;
                alphaPixels[i] = new Color32(255, 255, 255, alpha);
            }
            if (opaquePixelCount < 3 || opaquePixelCount >= maskPixels.Length)
                return false;

            alphaTexture = new Texture2D(hardMask.width, hardMask.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            alphaTexture.SetPixels32(alphaPixels);
            alphaTexture.Apply(false, false);

            var provider = new DefaultGeometryTextureDataProvider(alphaTexture);
            var binding = new LayerSpriteBinding
            {
                rect = new Rect(0f, 0f, hardMask.width, hardMask.height),
                pivotPixels = Vector2.zero,
                pixelsPerUnit = 1f
            };
            if (!TryGenerateAutoGeometryLocal(
                    provider,
                    binding,
                    DefaultGeometryOutlineDetail,
                    DefaultGeometryAlphaTolerance,
                    DefaultGeometryQualitySubdivide,
                    out var localVertices,
                    out triangles,
                    out allEdgePairs,
                    out boundaryEdgePairs))
                return false;

            localVertices = ScaleDefaultGeometryLocalVertices(
                localVertices,
                new Vector2(
                    layer.sourceRect.width / hardMask.width,
                    layer.sourceRect.height / hardMask.height));
            localVertices = OrientDefaultGeometryLocalVerticesToOpaquePixels(
                layer,
                layer.texture,
                localVertices,
                triangles);
            if (!GeneratedDefaultGeometryCoversLayer(layer, layer.texture, localVertices, triangles))
                return false;

            if (ReanimatorTriangulationAlgorithms.RetriangulateGeneratedGeometry(localVertices, ref triangles))
            {
                triangles = SanitizeTriangles(triangles, localVertices.Length);
                BuildEdgePairs(triangles, localVertices.Length, out allEdgePairs, out boundaryEdgePairs);
            }

            documentVertices = new Vector2[localVertices.Length];
            for (var i = 0; i < localVertices.Length; i++)
                documentVertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, localVertices[i]);
            return documentVertices.Length >= 3 && triangles.Length >= 3;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (alphaTexture != null)
                UnityEngine.Object.DestroyImmediate(alphaTexture);
        }
    }

    private static List<Vector2Int> BuildDefaultGeometryBoundaryEdges(
        IReadOnlyList<int> boundaryEdgePairs,
        int vertexCount)
    {
        var result = new List<Vector2Int>(boundaryEdgePairs != null ? boundaryEdgePairs.Count / 2 : 0);
        if (boundaryEdgePairs == null || vertexCount <= 0)
            return result;

        for (var i = 0; i + 1 < boundaryEdgePairs.Count; i += 2)
        {
            var a = boundaryEdgePairs[i];
            var b = boundaryEdgePairs[i + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;
            result.Add(new Vector2Int(a, b));
        }

        return result;
    }

    private static List<Vector2Int> BuildDensifiedDefaultGeometryBoundaryEdges(
        List<Vector2> vertices,
        IReadOnlyList<Vector2Int> sourceEdges)
    {
        var result = new List<Vector2Int>(sourceEdges != null ? sourceEdges.Count * 2 : 0);
        if (vertices == null || vertices.Count < 2 || sourceEdges == null || sourceEdges.Count == 0)
            return result;

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < vertices.Count; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }
        var maximumSegmentLength = Mathf.Max(4f, (max - min).magnitude * 0.06f);
        for (var edgeIndex = 0; edgeIndex < sourceEdges.Count; edgeIndex++)
        {
            var edge = sourceEdges[edgeIndex];
            if (edge.x < 0 || edge.y < 0 || edge.x >= vertices.Count || edge.y >= vertices.Count || edge.x == edge.y)
                continue;

            var start = vertices[edge.x];
            var end = vertices[edge.y];
            var segmentCount = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / maximumSegmentLength));
            var previous = edge.x;
            for (var segment = 1; segment < segmentCount; segment++)
            {
                var next = vertices.Count;
                vertices.Add(Vector2.Lerp(start, end, segment / (float)segmentCount));
                result.Add(new Vector2Int(previous, next));
                previous = next;
            }
            result.Add(new Vector2Int(previous, edge.y));
        }

        return result;
    }

    private static List<Vector2> BuildDefaultGeometrySnapshotTessellationVertices(
        IReadOnlyList<Vector2> sourceVertices,
        IReadOnlyList<int> sourceTriangles)
    {
        var result = new List<Vector2>(sourceVertices != null ? sourceVertices.Count + 256 : 256);
        if (sourceVertices == null || sourceVertices.Count < 3 || sourceTriangles == null || sourceTriangles.Count < 3)
            return result;

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < sourceVertices.Count; i++)
        {
            var point = sourceVertices[i];
            result.Add(point);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        var diagonal = (max - min).magnitude;
        if (diagonal <= 0.0001f)
            return result;
        var spacing = Mathf.Max(4f, diagonal * 0.06f);
        var rowStep = spacing * 0.8660254f;
        var minimumSeparationSqr = spacing * spacing * 0.04f;
        var row = 0;
        for (var y = min.y + rowStep * 0.5f; y < max.y && result.Count < 2048; y += rowStep, row++)
        {
            var xOffset = (row & 1) == 0 ? spacing * 0.5f : spacing;
            for (var x = min.x + xOffset; x < max.x && result.Count < 2048; x += spacing)
            {
                var point = new Vector2(x, y);
                if (!DefaultGeometryPointInsideTriangles(point, sourceVertices, sourceTriangles))
                    continue;

                var separated = true;
                for (var existingIndex = 0; existingIndex < result.Count; existingIndex++)
                {
                    if ((result[existingIndex] - point).sqrMagnitude >= minimumSeparationSqr)
                        continue;
                    separated = false;
                    break;
                }
                if (separated)
                    result.Add(point);
            }
        }

        return result;
    }

    private static bool DefaultGeometryPointInsideTriangles(
        Vector2 point,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> triangles)
    {
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertices.Count || b >= vertices.Count || c >= vertices.Count)
                continue;

            var pa = vertices[a];
            var pb = vertices[b];
            var pc = vertices[c];
            var area = SignedAutoGeometryTriangleArea(pa, pb, pc);
            if (Mathf.Abs(area) <= 0.0001f)
                continue;
            var ab = SignedAutoGeometryTriangleArea(pa, pb, point);
            var bc = SignedAutoGeometryTriangleArea(pb, pc, point);
            var ca = SignedAutoGeometryTriangleArea(pc, pa, point);
            if (area > 0f)
            {
                if (ab >= -0.0001f && bc >= -0.0001f && ca >= -0.0001f)
                    return true;
            }
            else if (ab <= 0.0001f && bc <= 0.0001f && ca <= 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDocumentVerticesLocalBounds(
        LayerInfo layer,
        Vector2[] verticesDoc,
        out Rect bounds)
    {
        bounds = Rect.zero;
        if (layer == null || verticesDoc == null || verticesDoc.Length == 0)
            return false;

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < verticesDoc.Length; i++)
        {
            var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, verticesDoc[i]);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        if (!float.IsFinite(min.x) || !float.IsFinite(min.y) || !float.IsFinite(max.x) || !float.IsFinite(max.y))
            return false;

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return bounds.width > 0.0001f && bounds.height > 0.0001f;
    }

    private static bool TryGetLocalVerticesBounds(Vector2[] localVertices, out Rect bounds)
    {
        bounds = Rect.zero;
        if (localVertices == null || localVertices.Length == 0)
            return false;

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < localVertices.Length; i++)
        {
            min = Vector2.Min(min, localVertices[i]);
            max = Vector2.Max(max, localVertices[i]);
        }

        if (!float.IsFinite(min.x) || !float.IsFinite(min.y) || !float.IsFinite(max.x) || !float.IsFinite(max.y))
            return false;

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return bounds.width > 0.0001f && bounds.height > 0.0001f;
    }

    private static bool TryGetDocumentVerticesAsLocal(LayerInfo layer, Vector2[] verticesDoc, out Vector2[] localVertices)
    {
        localVertices = Array.Empty<Vector2>();
        if (layer == null || verticesDoc == null || verticesDoc.Length == 0)
            return false;

        localVertices = new Vector2[verticesDoc.Length];
        for (var i = 0; i < verticesDoc.Length; i++)
            localVertices[i] = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, verticesDoc[i]);
        return true;
    }

    private static Vector2[] OrientDefaultGeometryLocalVerticesToOpaquePixels(
        LayerInfo layer,
        Texture2D texture,
        Vector2[] localVertices,
        int[] triangles)
    {
        if (layer == null || texture == null || localVertices == null || localVertices.Length == 0)
            return localVertices ?? Array.Empty<Vector2>();
        if (triangles == null || triangles.Length < 3 || layer.sourceRect.height <= 0f)
            return localVertices;

        var normalScore = ScoreDefaultGeometryOpaqueSupport(layer, texture, localVertices, triangles);
        var flipped = FlipLocalVerticesY(localVertices, layer.sourceRect.height);
        var flippedScore = ScoreDefaultGeometryOpaqueSupport(layer, texture, flipped, triangles);
        if (flippedScore + 0.02f < normalScore)
            return flipped;

        return localVertices;
    }

    private static float ScoreDefaultGeometryOpaqueSupport(
        LayerInfo layer,
        Texture2D texture,
        Vector2[] localVertices,
        int[] triangles)
    {
        if (layer == null || texture == null || localVertices == null || triangles == null)
            return 0f;
        if (localVertices.Length < 3 || triangles.Length < 3)
            return 0f;
        if (!TryGetTexturePixels(texture, out var pixels))
            return 0f;

        var textureReferenceRect = PsdCoordinateSpace.LayerTextureRect(layer, new Vector2(texture.width, texture.height));
        if (textureReferenceRect.width <= 0f || textureReferenceRect.height <= 0f)
            return 0f;

        var totalArea = 0f;
        var unsupportedArea = 0f;
        var outsidePenalty = 0f;
        var localRect = new Rect(0f, 0f, layer.sourceRect.width, layer.sourceRect.height);
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= localVertices.Length || ib >= localVertices.Length || ic >= localVertices.Length)
                continue;

            var a = localVertices[ia];
            var b = localVertices[ib];
            var c = localVertices[ic];
            var area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            if (area <= 0.0001f)
                continue;

            totalArea += area;
            var opaqueCoverage = EstimateTriangleOpaqueCoverage(layer, pixels, texture.width, texture.height, textureReferenceRect, a, b, c);
            unsupportedArea += area * (1f - opaqueCoverage);
            outsidePenalty += DistanceOutsideRectSqr(a, localRect) + DistanceOutsideRectSqr(b, localRect) + DistanceOutsideRectSqr(c, localRect);
        }

        if (totalArea <= 0.0001f)
            return 0f;

        return unsupportedArea / totalArea + outsidePenalty / Mathf.Max(1f, totalArea);
    }

    private static Vector2[] FlipLocalVerticesY(IReadOnlyList<Vector2> vertices, float height)
    {
        if (vertices == null || vertices.Count == 0)
            return Array.Empty<Vector2>();

        var flipHeight = Mathf.Max(0f, height);
        var result = new Vector2[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
            result[i] = new Vector2(vertices[i].x, flipHeight - vertices[i].y);
        return result;
    }

    private static bool LocalTrianglesFollowOpaquePixels(
        LayerInfo layer,
        Texture2D texture,
        Vector2[] localVertices,
        int[] triangles)
    {
        if (layer == null || texture == null || localVertices == null || triangles == null)
            return true;
        if (localVertices.Length < 3 || triangles.Length < 3)
            return false;
        if (!TryGetTexturePixels(texture, out var pixels))
            return true;

        var textureSize = new Vector2(texture.width, texture.height);
        var textureReferenceRect = PsdCoordinateSpace.LayerTextureRect(layer, textureSize);
        if (textureReferenceRect.width <= 0f || textureReferenceRect.height <= 0f)
            return true;

        var totalArea = 0f;
        var unsupportedArea = 0f;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= localVertices.Length || ib >= localVertices.Length || ic >= localVertices.Length)
                continue;

            var a = localVertices[ia];
            var b = localVertices[ib];
            var c = localVertices[ic];
            var area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            if (area <= 0.0001f)
                continue;

            totalArea += area;
            var opaqueCoverage = EstimateTriangleOpaqueCoverage(layer, pixels, texture.width, texture.height, textureReferenceRect, a, b, c);
            unsupportedArea += area * (1f - opaqueCoverage);
        }

        if (totalArea <= 0.0001f)
            return false;

        return unsupportedArea <= totalArea * 0.25f;
    }

    private static float EstimateTriangleOpaqueCoverage(
        LayerInfo layer,
        IReadOnlyList<Color32> pixels,
        int textureWidth,
        int textureHeight,
        Rect textureReferenceRect,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        var samples = 0;
        var opaque = 0;
        const int steps = 5;
        for (var iy = 0; iy <= steps; iy++)
        {
            for (var ix = 0; ix <= steps - iy; ix++)
            {
                var u = ix / (float)steps;
                var v = iy / (float)steps;
                var w = 1f - u - v;
                var point = a * w + b * u + c * v;
                samples++;
                if (LocalPointTouchesOpaquePixels(layer, pixels, textureWidth, textureHeight, textureReferenceRect, point))
                    opaque++;
            }
        }

        return samples > 0 ? opaque / (float)samples : 0f;
    }

    private static bool LocalPointTouchesOpaquePixels(
        LayerInfo layer,
        IReadOnlyList<Color32> pixels,
        int textureWidth,
        int textureHeight,
        Rect textureReferenceRect,
        Vector2 localPoint)
    {
        var texel = PsdCoordinateSpace.LayerLocalToTextureSamplingPoint(layer, localPoint, textureReferenceRect);
        var centerX = Mathf.RoundToInt(texel.x);
        var centerY = Mathf.RoundToInt(texel.y);
        for (var y = centerY - 2; y <= centerY + 2; y++)
        {
            if (y < 0 || y >= textureHeight)
                continue;
            for (var x = centerX - 2; x <= centerX + 2; x++)
            {
                if (x < 0 || x >= textureWidth)
                    continue;
                if (pixels[y * textureWidth + x].a > 0)
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetLayerOpaqueLocalBounds(
        LayerInfo layer,
        Texture2D texture,
        out Rect localBounds)
    {
        localBounds = Rect.zero;
        if (layer == null || texture == null || texture.width <= 0 || texture.height <= 0)
            return false;
        if (!TryGetTexturePixels(texture, out var pixels))
            return false;

        var textureSize = new Vector2(texture.width, texture.height);
        var textureReferenceRect = PsdCoordinateSpace.LayerTextureRect(layer, textureSize);
        if (textureReferenceRect.width <= 0f || textureReferenceRect.height <= 0f)
            return false;
        if (!ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToOpaquePixelUnion(
                textureReferenceRect,
                pixels,
                texture.width,
                texture.height,
                textureSize,
                0,
                out var fittedTextureRect,
                out _))
            return false;

        var fittedAtlasRect = PsdCoordinateSpace.TextureSamplingRectToAtlasRect(
            layer.sourceRect,
            textureReferenceRect,
            fittedTextureRect);
        localBounds = new Rect(
            fittedAtlasRect.x - layer.sourceRect.x,
            fittedAtlasRect.y - layer.sourceRect.y,
            fittedAtlasRect.width,
            fittedAtlasRect.height);
        return localBounds.width > 0.0001f && localBounds.height > 0.0001f;
    }

    private static bool LocalBoundsCoverReference(Rect candidate, Rect reference, float minimumSizeFraction, float minimumOverlapFraction)
    {
        if (candidate.width <= 0f || candidate.height <= 0f || reference.width <= 0f || reference.height <= 0f)
            return false;
        if (candidate.width < reference.width * Mathf.Clamp01(minimumSizeFraction) ||
            candidate.height < reference.height * Mathf.Clamp01(minimumSizeFraction))
            return false;

        var overlap = Rect.MinMaxRect(
            Mathf.Max(candidate.xMin, reference.xMin),
            Mathf.Max(candidate.yMin, reference.yMin),
            Mathf.Min(candidate.xMax, reference.xMax),
            Mathf.Min(candidate.yMax, reference.yMax));
        if (overlap.width <= 0f || overlap.height <= 0f)
            return false;

        var referenceArea = reference.width * reference.height;
        var overlapArea = overlap.width * overlap.height;
        return overlapArea >= referenceArea * Mathf.Clamp01(minimumOverlapFraction);
    }

    private static bool LocalBoundsDoNotOverfillReference(Rect candidate, Rect reference, float maximumSizeFraction, float maximumAreaFraction)
    {
        if (candidate.width <= 0f || candidate.height <= 0f || reference.width <= 0f || reference.height <= 0f)
            return false;

        var padding = Mathf.Max(4f, Mathf.Min(reference.width, reference.height) * 0.08f);
        var maxWidth = reference.width * Mathf.Max(1f, maximumSizeFraction) + padding;
        var maxHeight = reference.height * Mathf.Max(1f, maximumSizeFraction) + padding;
        if (candidate.width > maxWidth || candidate.height > maxHeight)
            return false;

        var candidateArea = candidate.width * candidate.height;
        var referenceArea = reference.width * reference.height;
        return candidateArea <= referenceArea * Mathf.Max(1f, maximumAreaFraction);
    }

    private static bool LocalTriangleAreaCoversReference(
        LayerInfo layer,
        Vector2[] verticesDoc,
        int[] triangles,
        Rect reference,
        float minimumAreaFraction)
    {
        if (layer == null || verticesDoc == null || triangles == null || verticesDoc.Length < 3 || triangles.Length < 3)
            return false;
        if (reference.width <= 0f || reference.height <= 0f)
            return false;

        var area = 0f;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= verticesDoc.Length || ib >= verticesDoc.Length || ic >= verticesDoc.Length)
                continue;

            var a = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, verticesDoc[ia]);
            var b = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, verticesDoc[ib]);
            var c = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, verticesDoc[ic]);
            area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
        }

        return area >= reference.width * reference.height * Mathf.Clamp01(minimumAreaFraction);
    }

    private static bool LocalTriangleAreaCoversReference(
        Vector2[] localVertices,
        int[] triangles,
        Rect reference,
        float minimumAreaFraction)
    {
        if (localVertices == null || triangles == null || localVertices.Length < 3 || triangles.Length < 3)
            return false;
        if (reference.width <= 0f || reference.height <= 0f)
            return false;

        var area = 0f;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= localVertices.Length || ib >= localVertices.Length || ic >= localVertices.Length)
                continue;

            var a = localVertices[ia];
            var b = localVertices[ib];
            var c = localVertices[ic];
            area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
        }

        return area >= reference.width * reference.height * Mathf.Clamp01(minimumAreaFraction);
    }

    private static LayerSpriteBinding BuildDefaultGeometryBindingForLayer(
        LayerInfo layer,
        LayerVertexData data,
        Vector2 textureSize)
    {
        if (layer == null)
            return default;

        var rect = PsdCoordinateSpace.LayerSpriteImportRect(layer, textureSize);
        var ppu = data != null && data.pixelsPerUnit > 0f ? data.pixelsPerUnit : 1f;
        var pivot = new Vector2(
            Mathf.Clamp01(layer.pivot.x) * Mathf.Max(0f, rect.width),
            Mathf.Clamp01(layer.pivot.y) * Mathf.Max(0f, rect.height));
        return new LayerSpriteBinding
        {
            rect = rect,
            pixelsPerUnit = ppu,
            pivotPixels = pivot
        };
    }

    private bool TryBuildDefaultGeometryBindingFromProvider(
        ISpriteEditorDataProvider dataProvider,
        LayerInfo layer,
        string selectionKey,
        string stableKey,
        out LayerSpriteBinding binding)
    {
        binding = default;
        if (dataProvider == null || layer == null)
            return false;

        var bindings = BuildLayerSpriteBindings(dataProvider);
        return TryResolveLayerBinding(bindings, selectionKey, stableKey, out binding, out _);
    }

    private static LayerInfo BuildDefaultGeometrySamplingLayer(LayerInfo source, Rect textureSamplingRect)
    {
        if (source == null)
            return null;

        return new LayerInfo
        {
            name = source.name,
            layerId = source.layerId,
            spriteGuid = source.spriteGuid,
            depth = source.depth,
            visible = source.visible,
            layerDataIndex = source.layerDataIndex,
            parentLayerDataIndex = source.parentLayerDataIndex,
            color = source.color,
            sourceRect = source.sourceRect,
            textureRect = textureSamplingRect,
            textureSamplingRect = textureSamplingRect,
            spriteSheetOptimizeReferenceRect = source.spriteSheetOptimizeReferenceRect,
            spriteSheetSerializedImportRect = source.spriteSheetSerializedImportRect,
            spriteSheetPsdLayerReferenceRect = source.spriteSheetPsdLayerReferenceRect,
            spriteSheetOptimizeReferencePosition = source.spriteSheetOptimizeReferencePosition,
            position = source.position,
            texture = source.texture,
            border = source.border,
            pivot = source.pivot,
            alignment = source.alignment
        };
    }

    private static bool TryBuildDefaultGeometrySamplingFallbackBindingForLayer(
        LayerInfo layer,
        LayerVertexData data,
        Vector2 textureSize,
        LayerSpriteBinding logicalBinding,
        out LayerSpriteBinding samplingBinding,
        out Vector2 samplingToLogicalScale)
    {
        samplingBinding = default;
        samplingToLogicalScale = Vector2.one;
        if (layer == null || logicalBinding.rect.width <= 0f || logicalBinding.rect.height <= 0f)
            return false;

        var samplingRect = PsdCoordinateSpace.LayerTextureRect(layer, textureSize);
        if (samplingRect.width <= 0f || samplingRect.height <= 0f)
            return false;
        if (RectApproximatelyEqual(samplingRect, logicalBinding.rect))
            return false;

        samplingBinding = BuildDefaultGeometryBindingForLayer(layer, data, textureSize);
        samplingBinding.rect = samplingRect;
        samplingToLogicalScale = new Vector2(
            logicalBinding.rect.width / samplingRect.width,
            logicalBinding.rect.height / samplingRect.height);
        return samplingToLogicalScale.x > 0f &&
               samplingToLogicalScale.y > 0f &&
               !float.IsNaN(samplingToLogicalScale.x) &&
               !float.IsNaN(samplingToLogicalScale.y) &&
               !float.IsInfinity(samplingToLogicalScale.x) &&
               !float.IsInfinity(samplingToLogicalScale.y);
    }

    private static Vector2[] ScaleDefaultGeometryLocalVertices(Vector2[] vertices, Vector2 scale)
    {
        if (vertices == null || vertices.Length == 0)
            return Array.Empty<Vector2>();

        var scaled = new Vector2[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
            scaled[i] = Vector2.Scale(vertices[i], scale);
        return scaled;
    }

    private static bool RectApproximatelyEqual(Rect a, Rect b)
    {
        const float epsilon = 0.0001f;
        return Mathf.Abs(a.x - b.x) <= epsilon &&
               Mathf.Abs(a.y - b.y) <= epsilon &&
               Mathf.Abs(a.width - b.width) <= epsilon &&
               Mathf.Abs(a.height - b.height) <= epsilon;
    }

    private static Rect NormalizeRect(Rect rect)
    {
        if (rect.width < 0f)
        {
            rect.x += rect.width;
            rect.width = -rect.width;
        }
        if (rect.height < 0f)
        {
            rect.y += rect.height;
            rect.height = -rect.height;
        }
        return rect;
    }

}
#endif
