#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorSpriteSheetGeometryAlgorithms
{
    public struct OpaquePixelComponent
    {
        public Rect rect;
        public int pixelCount;
    }

    public static bool ShouldDisplayGeometryForSelection(string layerStableKey, string selectedStableKey)
    {
        if (string.IsNullOrEmpty(selectedStableKey))
            return true;
        if (string.IsNullOrEmpty(layerStableKey))
            return false;

        return string.Equals(layerStableKey, selectedStableKey, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldTintMissingGeometryForSelection(string layerStableKey, string selectedStableKey, bool hasGeometry)
    {
        if (hasGeometry || string.IsNullOrEmpty(selectedStableKey))
            return false;

        return ShouldDisplayGeometryForSelection(layerStableKey, selectedStableKey);
    }

    public static bool TryFitRectToGeometry(
        PsdLayerPreviewRenderer.LayerInfo layer,
        IReadOnlyList<Vector2> documentVertices,
        Vector2 atlasSize,
        out Rect fittedRect,
        out Vector2 positionDelta)
    {
        fittedRect = Rect.zero;
        positionDelta = Vector2.zero;
        if (layer == null || documentVertices == null || documentVertices.Count == 0)
            return false;

        var source = NormalizeRect(layer.sourceRect);
        if (source.width <= 0f || source.height <= 0f)
            return false;

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < documentVertices.Count; i++)
        {
            var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, documentVertices[i]);
            if (!IsLocalPointInsideSource(local, source))
                continue;

            local = new Vector2(
                Mathf.Clamp(local.x, 0f, source.width),
                Mathf.Clamp(local.y, 0f, source.height));
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        if (!IsFinite(min.x) || !IsFinite(min.y) || !IsFinite(max.x) || !IsFinite(max.y))
            return false;

        var minX = Mathf.Floor(min.x);
        var minY = Mathf.Floor(min.y);
        var maxX = Mathf.Ceil(max.x);
        var maxY = Mathf.Ceil(max.y);
        if (maxX <= minX)
            maxX = minX + 1f;
        if (maxY <= minY)
            maxY = minY + 1f;

        var fitted = new Rect(
            source.x + minX,
            source.y + minY,
            maxX - minX,
            maxY - minY);
        fitted = ClampRectToAtlas(fitted, atlasSize);
        if (fitted.width <= 0f || fitted.height <= 0f)
            return false;

        fittedRect = fitted;
        positionDelta = fitted.position - source.position;
        return true;
    }

    public static bool TryFitRectToOpaquePixels(
        Rect referenceRect,
        IReadOnlyList<Color32> pixels,
        int textureWidth,
        int textureHeight,
        Vector2 atlasSize,
        byte alphaTolerance,
        out Rect fittedRect,
        out Vector2 positionDelta)
    {
        fittedRect = Rect.zero;
        positionDelta = Vector2.zero;
        if (pixels == null || textureWidth <= 0 || textureHeight <= 0 || pixels.Count < textureWidth * textureHeight)
            return false;

        var reference = NormalizeRect(referenceRect);
        if (reference.width <= 0f || reference.height <= 0f)
            return false;

        var components = new List<OpaquePixelComponent>();
        BuildOpaquePixelComponents(pixels, textureWidth, textureHeight, alphaTolerance, components);
        if (components.Count == 0)
            return false;

        var bestRect = Rect.zero;
        var bestOverlapArea = 0f;
        var bestOverlapDistance = float.PositiveInfinity;
        var bestNearestIndex = -1;
        var bestNearestDistance = float.PositiveInfinity;
        var referenceCenter = reference.center;
        for (var i = 0; i < components.Count; i++)
        {
            var componentRect = components[i].rect;
            if (componentRect.width <= 0f || componentRect.height <= 0f)
                continue;

            var distance = (componentRect.center - referenceCenter).sqrMagnitude;
            if (RectsOverlap(reference, componentRect))
            {
                var overlapArea = OverlapArea(reference, componentRect);
                if (overlapArea > bestOverlapArea ||
                    (Mathf.Approximately(overlapArea, bestOverlapArea) && distance < bestOverlapDistance))
                {
                    bestRect = componentRect;
                    bestOverlapArea = overlapArea;
                    bestOverlapDistance = distance;
                }
                continue;
            }

            if (distance >= bestNearestDistance)
                continue;

            bestNearestDistance = distance;
            bestNearestIndex = i;
        }

        if (bestOverlapArea <= 0f)
        {
            if (bestNearestIndex < 0)
                return false;
            bestRect = components[bestNearestIndex].rect;
        }

        var fitted = ClampRectToAtlas(bestRect, atlasSize);
        if (fitted.width <= 0f || fitted.height <= 0f)
            return false;

        fittedRect = fitted;
        positionDelta = fitted.position - reference.position;
        return true;
    }

    public static bool TryFitRectToOpaquePixelUnion(
        Rect referenceRect,
        IReadOnlyList<Color32> pixels,
        int textureWidth,
        int textureHeight,
        Vector2 atlasSize,
        byte alphaTolerance,
        out Rect fittedRect,
        out Vector2 positionDelta)
    {
        fittedRect = Rect.zero;
        positionDelta = Vector2.zero;
        if (pixels == null || textureWidth <= 0 || textureHeight <= 0 || pixels.Count < textureWidth * textureHeight)
            return false;

        var reference = NormalizeRect(referenceRect);
        if (reference.width <= 0f || reference.height <= 0f)
            return false;

        var components = new List<OpaquePixelComponent>();
        BuildOpaquePixelComponents(pixels, textureWidth, textureHeight, alphaTolerance, components);
        if (components.Count == 0)
            return false;

        var hasUnion = false;
        var union = Rect.zero;
        for (var i = 0; i < components.Count; i++)
        {
            var componentRect = components[i].rect;
            if (componentRect.width <= 0f || componentRect.height <= 0f)
                continue;
            if (!RectsOverlap(reference, componentRect))
                continue;

            union = hasUnion ? UnionRects(union, componentRect) : componentRect;
            hasUnion = true;
        }

        if (!hasUnion)
            return false;

        var fitted = ClampRectToAtlas(union, atlasSize);
        if (fitted.width <= 0f || fitted.height <= 0f)
            return false;

        fittedRect = fitted;
        positionDelta = fitted.position - reference.position;
        return true;
    }

    public static bool TryFitRectToTextureUvs(
        PsdLayerPreviewRenderer.LayerInfo layer,
        IReadOnlyList<Vector2> vertexUvs,
        Vector2 textureSize,
        Vector2 atlasSize,
        out Rect fittedRect,
        out Vector2 positionDelta)
    {
        fittedRect = Rect.zero;
        positionDelta = Vector2.zero;
        if (layer == null || vertexUvs == null || vertexUvs.Count == 0)
            return false;

        var source = NormalizeRect(layer.sourceRect);
        if (source.width <= 0f || source.height <= 0f)
            return false;

        var texW = Mathf.Max(1f, textureSize.x);
        var texH = Mathf.Max(1f, textureSize.y);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < vertexUvs.Count; i++)
        {
            var uv = vertexUvs[i];
            if (!IsFinite(uv.x) || !IsFinite(uv.y))
                continue;

            var texel = new Vector2(uv.x * texW, uv.y * texH);
            if (!IsTexturePointInsideSource(texel, source))
                continue;

            texel = new Vector2(
                Mathf.Clamp(texel.x, source.xMin, source.xMax),
                Mathf.Clamp(texel.y, source.yMin, source.yMax));
            min = Vector2.Min(min, texel);
            max = Vector2.Max(max, texel);
        }

        if (!IsFinite(min.x) || !IsFinite(min.y) || !IsFinite(max.x) || !IsFinite(max.y))
            return false;

        var minX = Mathf.Floor(min.x);
        var minY = Mathf.Floor(min.y);
        var maxX = Mathf.Ceil(max.x);
        var maxY = Mathf.Ceil(max.y);
        if (maxX <= minX)
            maxX = minX + 1f;
        if (maxY <= minY)
            maxY = minY + 1f;

        var fitted = ClampRectToAtlas(Rect.MinMaxRect(minX, minY, maxX, maxY), atlasSize);
        if (fitted.width <= 0f || fitted.height <= 0f)
            return false;

        fittedRect = fitted;
        positionDelta = fitted.position - source.position;
        return true;
    }

    public static bool TryFitRectToAllTextureUvs(
        PsdLayerPreviewRenderer.LayerInfo layer,
        IReadOnlyList<Vector2> vertexUvs,
        Vector2 textureSize,
        Vector2 atlasSize,
        out Rect fittedRect,
        out Vector2 positionDelta)
    {
        fittedRect = Rect.zero;
        positionDelta = Vector2.zero;
        if (layer == null || vertexUvs == null || vertexUvs.Count == 0)
            return false;

        var source = NormalizeRect(layer.sourceRect);
        if (source.width <= 0f || source.height <= 0f)
            return false;

        var texW = Mathf.Max(1f, textureSize.x);
        var texH = Mathf.Max(1f, textureSize.y);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < vertexUvs.Count; i++)
        {
            var uv = vertexUvs[i];
            if (!IsFinite(uv.x) || !IsFinite(uv.y))
                continue;

            var texel = new Vector2(
                Mathf.Clamp(uv.x * texW, 0f, texW),
                Mathf.Clamp(uv.y * texH, 0f, texH));
            min = Vector2.Min(min, texel);
            max = Vector2.Max(max, texel);
        }

        if (!IsFinite(min.x) || !IsFinite(min.y) || !IsFinite(max.x) || !IsFinite(max.y))
            return false;

        var minX = Mathf.Floor(min.x);
        var minY = Mathf.Floor(min.y);
        var maxX = Mathf.Ceil(max.x);
        var maxY = Mathf.Ceil(max.y);
        if (maxX <= minX)
            maxX = minX + 1f;
        if (maxY <= minY)
            maxY = minY + 1f;

        var fitted = ClampRectToAtlas(Rect.MinMaxRect(minX, minY, maxX, maxY), atlasSize);
        if (fitted.width <= 0f || fitted.height <= 0f)
            return false;

        fittedRect = fitted;
        positionDelta = fitted.position - source.position;
        return true;
    }

    public static bool TryBuildKeepMaskForTextureUvs(
        PsdLayerPreviewRenderer.LayerInfo layer,
        IReadOnlyList<Vector2> vertexUvs,
        Vector2 textureSize,
        out bool[] keepMask,
        out int keptCount)
    {
        if (layer == null || vertexUvs == null || vertexUvs.Count == 0)
        {
            keepMask = Array.Empty<bool>();
            keptCount = 0;
            return false;
        }

        return TryBuildKeepMaskForTextureUvs(layer.sourceRect, vertexUvs, textureSize, out keepMask, out keptCount);
    }

    public static bool TryBuildKeepMaskForTextureUvs(
        Rect sourceRect,
        IReadOnlyList<Vector2> vertexUvs,
        Vector2 textureSize,
        out bool[] keepMask,
        out int keptCount)
    {
        keepMask = Array.Empty<bool>();
        keptCount = 0;
        if (vertexUvs == null || vertexUvs.Count == 0)
            return false;

        var source = NormalizeRect(sourceRect);
        if (source.width <= 0f || source.height <= 0f)
            return false;

        var texW = Mathf.Max(1f, textureSize.x);
        var texH = Mathf.Max(1f, textureSize.y);
        keepMask = new bool[vertexUvs.Count];
        for (var i = 0; i < vertexUvs.Count; i++)
        {
            var uv = vertexUvs[i];
            if (!IsFinite(uv.x) || !IsFinite(uv.y))
                continue;

            var texel = new Vector2(uv.x * texW, uv.y * texH);
            if (!IsTexturePointInsideSource(texel, source))
                continue;

            keepMask[i] = true;
            keptCount++;
        }

        return true;
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

    private static Rect ClampRectToAtlas(Rect rect, Vector2 atlasSize)
    {
        var maxX = Mathf.Max(1f, atlasSize.x);
        var maxY = Mathf.Max(1f, atlasSize.y);
        var xMin = Mathf.Clamp(rect.xMin, 0f, maxX - 1f);
        var yMin = Mathf.Clamp(rect.yMin, 0f, maxY - 1f);
        var xMax = Mathf.Clamp(rect.xMax, xMin + 1f, maxX);
        var yMax = Mathf.Clamp(rect.yMax, yMin + 1f, maxY);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Rect UnionRects(Rect a, Rect b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin),
            Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax),
            Mathf.Max(a.yMax, b.yMax));
    }

    private static void BuildOpaquePixelComponents(
        IReadOnlyList<Color32> pixels,
        int width,
        int height,
        byte alphaTolerance,
        List<OpaquePixelComponent> components)
    {
        components.Clear();
        var visited = new bool[width * height];
        var queue = new Queue<int>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (visited[index] || pixels[index].a <= alphaTolerance)
                    continue;

                visited[index] = true;
                queue.Enqueue(index);
                var minX = x;
                var minY = y;
                var maxX = x;
                var maxY = y;
                var count = 0;

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var cx = current % width;
                    var cy = current / width;
                    count++;
                    if (cx < minX) minX = cx;
                    if (cy < minY) minY = cy;
                    if (cx > maxX) maxX = cx;
                    if (cy > maxY) maxY = cy;

                    EnqueueOpaqueNeighbor(cx - 1, cy, pixels, width, height, alphaTolerance, visited, queue);
                    EnqueueOpaqueNeighbor(cx + 1, cy, pixels, width, height, alphaTolerance, visited, queue);
                    EnqueueOpaqueNeighbor(cx, cy - 1, pixels, width, height, alphaTolerance, visited, queue);
                    EnqueueOpaqueNeighbor(cx, cy + 1, pixels, width, height, alphaTolerance, visited, queue);
                }

                components.Add(new OpaquePixelComponent
                {
                    rect = Rect.MinMaxRect(minX, minY, maxX + 1, maxY + 1),
                    pixelCount = count
                });
            }
        }
    }

    private static void EnqueueOpaqueNeighbor(
        int x,
        int y,
        IReadOnlyList<Color32> pixels,
        int width,
        int height,
        byte alphaTolerance,
        bool[] visited,
        Queue<int> queue)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;

        var index = y * width + x;
        if (visited[index] || pixels[index].a <= alphaTolerance)
            return;

        visited[index] = true;
        queue.Enqueue(index);
    }

    private static bool RectsOverlap(Rect a, Rect b)
    {
        return a.xMin < b.xMax &&
               a.xMax > b.xMin &&
               a.yMin < b.yMax &&
               a.yMax > b.yMin;
    }

    private static float OverlapArea(Rect a, Rect b)
    {
        var width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        var height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return width > 0f && height > 0f ? width * height : 0f;
    }

    private static bool IsLocalPointInsideSource(Vector2 local, Rect source)
    {
        const float epsilon = 0.001f;
        return local.x >= -epsilon &&
               local.y >= -epsilon &&
               local.x <= source.width + epsilon &&
               local.y <= source.height + epsilon;
    }

    private static bool IsTexturePointInsideSource(Vector2 texel, Rect source)
    {
        const float epsilon = 0.001f;
        return texel.x >= source.xMin - epsilon &&
               texel.y >= source.yMin - epsilon &&
               texel.x <= source.xMax + epsilon &&
               texel.y <= source.yMax + epsilon;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
#endif
