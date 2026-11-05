#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    public bool SetLayerSpriteRect(LayerInfo layer, Rect rect)
    {
        return SetLayerSpriteRect(layer, rect, registerUndo: true);
    }

    private bool SetLayerSpriteRect(LayerInfo layer, Rect rect, bool registerUndo)
    {
        if (layer == null)
            return false;

        rect = NormalizeSpriteRect(rect);
        if (registerUndo &&
            !RectsRoughlyEqual(layer.sourceRect, rect))
            RegisterUndo("Edit Sprite Rect");
        return SetLayerSpriteRectAndPosition(layer, rect, layer.position);
    }

    private bool SetLayerSpriteRectAndPosition(LayerInfo layer, Rect rect, Vector2 position)
    {
        if (layer == null)
            return false;

        var oldSelectionKey = BuildSelectionKey(layer);
        rect = NormalizeSpriteRect(rect);
        if (RectsRoughlyEqual(layer.sourceRect, rect) && (layer.position - position).sqrMagnitude <= 1e-8f)
            return false;

        vertexDataByLayerSelectionKey.TryGetValue(oldSelectionKey, out var vertexData);
        if (vertexData != null)
            vertexDataByLayerSelectionKey.Remove(oldSelectionKey);
        var positionChanged = (layer.position - position).sqrMagnitude > 1e-8f;
        var textureSize = layer.texture != null
            ? new Vector2(layer.texture.width, layer.texture.height)
            : Vector2.zero;
        var textureReferenceRect = PsdCoordinateSpace.LayerTextureRect(layer, textureSize);
        var nextTextureRect = PsdCoordinateSpace.AtlasRectToTextureSamplingRect(
            layer.sourceRect,
            textureReferenceRect,
            rect);
        layer.sourceRect = rect;
        layer.textureRect = nextTextureRect;
        layer.textureSamplingRect = nextTextureRect;
        layer.position = position;
        var newSelectionKey = BuildSelectionKey(layer);
        if (vertexData != null)
            vertexDataByLayerSelectionKey[newSelectionKey] = vertexData;
        if (string.Equals(selectedLayerSelectionKey, oldSelectionKey, StringComparison.OrdinalIgnoreCase))
            selectedLayerSelectionKey = newSelectionKey;
        MarkLayerSpriteStateDirty(layer);
        if (positionChanged)
            MarkLayerPositionStateDirty(layer);
        hasPendingChanges = true;
        return true;
    }

    private bool SetLayerSpriteRectAndPosition(LayerInfo layer, Rect rect, Rect textureRect, Vector2 position)
    {
        if (layer == null)
            return false;

        var oldSelectionKey = BuildSelectionKey(layer);
        rect = NormalizeSpriteRect(rect);
        textureRect = NormalizeSpriteRect(textureRect);
        if (RectsRoughlyEqual(layer.sourceRect, rect) &&
            RectsRoughlyEqual(layer.textureRect, textureRect) &&
            (layer.position - position).sqrMagnitude <= 1e-8f)
            return false;

        vertexDataByLayerSelectionKey.TryGetValue(oldSelectionKey, out var vertexData);
        if (vertexData != null)
            vertexDataByLayerSelectionKey.Remove(oldSelectionKey);
        var positionChanged = (layer.position - position).sqrMagnitude > 1e-8f;
        layer.sourceRect = rect;
        layer.textureRect = textureRect;
        layer.textureSamplingRect = textureRect;
        layer.position = position;
        var newSelectionKey = BuildSelectionKey(layer);
        if (vertexData != null)
            vertexDataByLayerSelectionKey[newSelectionKey] = vertexData;
        if (string.Equals(selectedLayerSelectionKey, oldSelectionKey, StringComparison.OrdinalIgnoreCase))
            selectedLayerSelectionKey = newSelectionKey;
        MarkLayerSpriteStateDirty(layer);
        if (positionChanged)
            MarkLayerPositionStateDirty(layer);
        hasPendingChanges = true;
        return true;
    }

    public int OptimizeSpriteSheetRectsToGeometry()
    {
        if (layerInfos == null || layerInfos.Count == 0)
            return 0;

        var targetLayers = GetSpriteSheetOperationLayers();
        if (targetLayers.Count == 0)
            return 0;

        RegisterUndo("Optimize Sprite Sheet Rects");

        var changed = 0;
        for (var i = 0; i < targetLayers.Count; i++)
        {
            var layer = targetLayers[i];
            if (layer == null)
                continue;

            var texture = layer.texture != null
                    ? layer.texture
                    : GetPreviewAtlasTexture();
            if (texture == null)
                continue;

            var textureSize = new Vector2(texture.width, texture.height);
            var stableKey = BuildKey(layer.name, layer.layerId);
            var repairsPsdLayerRect = PsdCoordinateSpace.RequiresPsdLayerRectRepair(
                layer.sourceRect,
                layer.spriteSheetSerializedImportRect,
                layer.spriteSheetPsdLayerReferenceRect);

            if (!TryFitLayerSpriteRectToOpaquePixels(
                    layer,
                    texture,
                    out var fittedRect,
                    out var fittedTextureRect,
                    out _))
                continue;

            var targetPosition = GetSpriteSheetOptimizeTargetPosition(layer, fittedRect);
            var rectChanged = SetLayerSpriteRectAndPosition(layer, fittedRect, fittedTextureRect, targetPosition);
            if (repairsPsdLayerRect && !rectChanged)
            {
                MarkLayerSpriteStateDirty(layer);
                hasPendingChanges = true;
                rectChanged = true;
            }
            var geometryChanged = false;
            if (TryGetLayerVertexData(layer, out var data) && data != null)
            {
                data.vertexUvs = BuildVertexUvsFromLayer(layer, data.vertices);
                var culled = false;
                var cullTextureRect = GetTextureRect(layer, textureSize);
                if (data.vertexUvs != null &&
                    data.vertexUvs.Length == data.vertices.Length &&
                    TryCullLayerGeometryToSpriteRect(data, textureSize, cullTextureRect, out culled) &&
                    culled)
                {
                    geometryChanged = true;
                }

                if (rectChanged || geometryChanged)
                    MarkEditGeometryDataChanged(data, BuildSelectionKey(layer), stableKey);
            }

            if (rectChanged || geometryChanged)
                changed++;
        }

        return changed;
    }

    private List<LayerInfo> GetSpriteSheetOperationLayers(bool forceAll = false)
    {
        var targets = new List<LayerInfo>();
        if (layerInfos == null || layerInfos.Count == 0)
            return targets;

        if (!forceAll && !string.IsNullOrEmpty(selectedLayerSelectionKey))
        {
            var selected = FindLayerBySelectionKey(layerInfos, selectedLayerSelectionKey);
            if (selected != null)
                targets.Add(selected);
            return targets;
        }

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer != null)
                targets.Add(layer);
        }

        return targets;
    }

    private bool TryFitLayerSpriteRectToOpaquePixels(
        LayerInfo layer,
        Texture2D texture,
        out Rect fittedRect,
        out Rect fittedTextureRect,
        out Vector2 positionDelta)
    {
        fittedRect = Rect.zero;
        fittedTextureRect = Rect.zero;
        positionDelta = Vector2.zero;
        if (layer == null || texture == null)
            return false;

        var textureSize = new Vector2(texture.width, texture.height);
        var textureReferenceRect = GetTextureRect(layer, textureSize);

        if (PsdCoordinateSpace.RequiresPsdLayerRectRepair(
                layer.sourceRect,
                layer.spriteSheetSerializedImportRect,
                layer.spriteSheetPsdLayerReferenceRect))
        {
            fittedRect = layer.spriteSheetPsdLayerReferenceRect;
            fittedTextureRect = textureReferenceRect;
            positionDelta = fittedRect.position - layer.sourceRect.position;
            return true;
        }

        if (!TryGetTexturePixels(texture, out var pixels))
            return false;

        var referenceRect = GetSpriteSheetOptimizeReferenceRect(layer);
        if (ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToOpaquePixelUnion(
                referenceRect,
                pixels,
                texture.width,
                texture.height,
                textureSize,
                0,
                out fittedTextureRect,
                out _))
        {
            fittedRect = PsdCoordinateSpace.TextureSamplingRectToAtlasRect(
                layer.sourceRect,
                textureReferenceRect,
                fittedTextureRect);
            positionDelta = fittedRect.position - layer.sourceRect.position;
            return true;
        }

        if (!ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToOpaquePixelUnion(
                textureReferenceRect,
                pixels,
                texture.width,
                texture.height,
                textureSize,
                0,
                out fittedTextureRect,
                out _))
            return false;

        fittedRect = PsdCoordinateSpace.TextureSamplingRectToAtlasRect(
            layer.sourceRect,
            textureReferenceRect,
            fittedTextureRect);
        positionDelta = fittedRect.position - layer.sourceRect.position;
        return true;
    }

    private Rect GetSpriteSheetOptimizeReferenceRect(LayerInfo layer)
    {
        if (layer == null)
            return Rect.zero;

        var textureSize = layer.texture != null
            ? new Vector2(layer.texture.width, layer.texture.height)
            : Vector2.zero;
        var textureReferenceRect = GetTextureRect(layer, textureSize);
        if (layer.spriteSheetOptimizeReferenceRect.width > 0f && layer.spriteSheetOptimizeReferenceRect.height > 0f)
        {
            return PsdCoordinateSpace.AtlasRectToTextureSamplingRect(
                layer.sourceRect,
                textureReferenceRect,
                layer.spriteSheetOptimizeReferenceRect);
        }

        if (!string.IsNullOrEmpty(cachedPath) && !string.IsNullOrEmpty(layer.spriteGuid))
        {
            var sprites = LoadSpriteCandidates(cachedPath);
            for (var i = 0; i < sprites.Count; i++)
            {
                var sprite = sprites[i];
                if (sprite == null)
                    continue;
                if (!string.Equals(TryGetSpriteStableGuid(sprite), layer.spriteGuid, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rect = PsdCoordinateSpace.ResolveCurrentTextureSamplingRect(
                    textureReferenceRect,
                    sprite.rect,
                    sprite.texture != null ? new Vector2(sprite.texture.width, sprite.texture.height) : Vector2.zero);
                if (rect.width > 0f && rect.height > 0f)
                    return rect;
            }
        }

        return textureReferenceRect.width > 0f && textureReferenceRect.height > 0f
            ? textureReferenceRect
            : layer.sourceRect;
    }

    private Vector2 GetSpriteSheetOptimizeTargetPosition(LayerInfo layer, Rect fittedRect)
    {
        if (layer == null)
            return Vector2.zero;

        var referencePosition = layer.spriteSheetOptimizeReferencePosition;
        if (referencePosition.sqrMagnitude <= 0.0001f)
            referencePosition = layer.position;

        var referenceRect = layer.spriteSheetOptimizeReferenceRect;
        if (referenceRect.width <= 0f || referenceRect.height <= 0f)
            referenceRect = layer.sourceRect;

        return referencePosition;
    }

    private static bool TryGetTexturePixels(Texture2D texture, out Color32[] pixels)
    {
        pixels = null;
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            return false;

        try
        {
            pixels = texture.GetPixels32();
            return pixels != null && pixels.Length >= texture.width * texture.height;
        }
        catch
        {
            return TryGetTexturePixelsViaGpu(texture, out pixels);
        }
    }

    private static bool TryGetTexturePixelsViaGpu(Texture2D texture, out Color32[] pixels)
    {
        pixels = null;
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            return false;

        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(
            texture.width,
            texture.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear);
        Texture2D readable = null;
        try
        {
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0, false);
            readable.Apply(false, false);
            pixels = readable.GetPixels32();
            return pixels != null && pixels.Length >= texture.width * texture.height;
        }
        catch
        {
            pixels = null;
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (readable != null)
                UnityEngine.Object.DestroyImmediate(readable);
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static bool RectContainsOpaquePixel(
        IReadOnlyList<Color32> pixels,
        int textureWidth,
        int textureHeight,
        Rect rect,
        byte alphaTolerance)
    {
        if (pixels == null || textureWidth <= 0 || textureHeight <= 0 || pixels.Count < textureWidth * textureHeight)
            return false;

        var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, textureWidth - 1);
        var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, textureHeight - 1);
        var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), xMin + 1, textureWidth);
        var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), yMin + 1, textureHeight);
        for (var y = yMin; y < yMax; y++)
        {
            var row = y * textureWidth;
            for (var x = xMin; x < xMax; x++)
            {
                if (pixels[row + x].a > alphaTolerance)
                    return true;
            }
        }

        return false;
    }

    private static bool RectContainsRect(Rect container, Rect candidate, float tolerance)
    {
        return candidate.xMin >= container.xMin - tolerance &&
               candidate.yMin >= container.yMin - tolerance &&
               candidate.xMax <= container.xMax + tolerance &&
               candidate.yMax <= container.yMax + tolerance;
    }

    private static bool RectCoversReferenceFraction(Rect candidate, Rect reference, float minimumFraction)
    {
        if (candidate.width <= 0f || candidate.height <= 0f || reference.width <= 0f || reference.height <= 0f)
            return false;

        var fraction = Mathf.Clamp01(minimumFraction);
        return candidate.width >= reference.width * fraction &&
               candidate.height >= reference.height * fraction;
    }

    private bool EnsureLayerVertexUvsForSpriteSheetOptimize(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;
        if (data.vertexUvs != null && data.vertexUvs.Length == data.vertices.Length)
            return true;

        data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        return data.vertexUvs != null && data.vertexUvs.Length == data.vertices.Length;
    }

    private bool TryCullLayerGeometryToSpriteRect(LayerVertexData data, Vector2 textureSize, Rect spriteRect, out bool changed)
    {
        changed = false;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            return false;

        if (!ReanimatorSpriteSheetGeometryAlgorithms.TryBuildKeepMaskForTextureUvs(
                spriteRect,
                data.vertexUvs,
                textureSize,
                out var keepMask,
                out var keptCount))
            return false;
        if (keptCount <= 0)
            return false;
        if (keptCount == data.vertices.Length)
            return true;
        if (keptCount < 3)
            return false;

        var removeMask = new bool[keepMask.Length];
        var removedCount = 0;
        for (var i = 0; i < keepMask.Length; i++)
        {
            removeMask[i] = !keepMask[i];
            if (removeMask[i])
                removedCount++;
        }

        if (removedCount <= 0)
            return true;

        var previousVertices = data.vertices;
        var previousVertexUvs = data.vertexUvs;
        var previousBoneWeights = data.boneWeights;
        var previousTriangles = data.triangles;
        var previousAllEdgePairs = data.allEdgePairs;
        var previousBoundaryEdgePairs = data.boundaryEdgePairs;
        var previousDeformedVertices = data.deformedVertices;
        var previousSkinMatrices = data.skinMatrices;
        var previousLastPoseVersion = data.lastPoseVersion;

        void RestorePreviousGeometry()
        {
            data.vertices = previousVertices;
            data.vertexUvs = previousVertexUvs;
            data.boneWeights = previousBoneWeights;
            data.triangles = previousTriangles;
            data.allEdgePairs = previousAllEdgePairs;
            data.boundaryEdgePairs = previousBoundaryEdgePairs;
            data.deformedVertices = previousDeformedVertices;
            data.skinMatrices = previousSkinMatrices;
            data.lastPoseVersion = previousLastPoseVersion;
        }

        var vertexCount = data.vertices.Length;
        var boundaryLoop = default(List<int>);
        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out boundaryLoop))
        {
            var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
            if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
            {
                BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
                contourMask = BuildContourVertexMask(fallbackBoundaryPairs, vertexCount);
            }

            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        }

        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out _);
        var keptVertices = new List<Vector2>(keptCount);
        var keptUvs = new List<Vector2>(keptCount);
        var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
            ? new List<BoneWeight>(keptCount)
            : null;

        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                continue;

            keptVertices.Add(data.vertices[i]);
            keptUvs.Add(data.vertexUvs[i]);
            if (keptWeights != null)
                keptWeights.Add(data.boneWeights[i]);
        }

        data.vertices = keptVertices.ToArray();
        data.vertexUvs = keptUvs.ToArray();
        data.boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>();
        data.triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, data.vertices.Length);

        var newBoundaryLoop = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
        if (newBoundaryLoop.Count >= 3)
            data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop);
        else
            BuildEdgePairs(data.triangles, data.vertices.Length, out _, out data.boundaryEdgePairs);

        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
        {
            RestorePreviousGeometry();
            return false;
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            RestorePreviousGeometry();
            return false;
        }

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        changed = true;
        return true;
    }

}
#endif
