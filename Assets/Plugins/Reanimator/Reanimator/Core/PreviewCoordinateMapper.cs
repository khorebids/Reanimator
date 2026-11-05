#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal enum PsdPreviewLayoutMode
{
    Skeleton = 0,
    SpriteAtlas = 1
}

internal class PsdCoordinateSpace
{
    private readonly Rect localRect;
    private readonly PreviewNavigator navigator;
    private readonly PsdPreviewLayoutMode layoutMode;
    private readonly Vector2 canvasMin;
    private readonly Vector2 scaledDrawSize;
    private readonly Vector2 docOffset;
    private readonly float fitScale;

    public Rect CanvasRectView => ToViewRect(new Rect(docOffset.x, docOffset.y, scaledDrawSize.x, scaledDrawSize.y));

    public PsdCoordinateSpace(
        Rect localRect,
        Vector2Int documentSize,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers,
        PreviewNavigator navigator,
        PsdPreviewLayoutMode layoutMode = PsdPreviewLayoutMode.Skeleton)
    {
        this.localRect = localRect;
        this.navigator = navigator;
        this.layoutMode = layoutMode;

        var docMax = new Vector2(
            Mathf.Max(1f, documentSize.x),
            Mathf.Max(1f, documentSize.y));

        var boundsMin = new Vector2(float.MaxValue, float.MaxValue);
        var boundsMax = new Vector2(float.MinValue, float.MinValue);
        if (layers != null)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null)
                    continue;

                var layerPosition = LayoutPosition(layer);
                var max = layerPosition + LayoutSize(layer);
                boundsMin = Vector2.Min(boundsMin, layerPosition);
                boundsMax = Vector2.Max(boundsMax, max);
            }
        }

        if (boundsMin.x == float.MaxValue || boundsMin.y == float.MaxValue)
        {
            boundsMin = Vector2.zero;
            boundsMax = docMax;
        }

        canvasMin = Vector2.Min(Vector2.zero, boundsMin);
        var canvasMax = Vector2.Max(docMax, boundsMax);
        var drawSize = canvasMax - canvasMin;
        if (drawSize.x <= 0f || drawSize.y <= 0f)
            drawSize = docMax;

        fitScale = 1f;
        if (drawSize.x > 0f && drawSize.y > 0f && localRect.width > 0f && localRect.height > 0f)
        {
            var scaleX = localRect.width / drawSize.x;
            var scaleY = localRect.height / drawSize.y;
            fitScale = Mathf.Min(1f, Mathf.Min(scaleX, scaleY)) * 0.98f;
        }

        scaledDrawSize = drawSize * fitScale;
        docOffset = (localRect.size - scaledDrawSize) * 0.5f;
    }

    public Vector2 DocumentToView(Vector2 point)
    {
        return ToView(DocumentToCanvas(point));
    }

    public Vector2 DocumentToCanvasPoint(Vector2 point)
    {
        return DocumentToCanvas(point);
    }

    public Vector2 ViewToDocument(Vector2 point)
    {
        return CanvasToDocument(FromView(point));
    }

    public Rect LayerToViewRect(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (layer == null)
            return Rect.zero;

        var size = LayoutSize(layer) * fitScale;
        var pos = (LayoutPosition(layer) - canvasMin) * fitScale;
        var dst = new Rect(pos.x, pos.y, size.x, size.y);
        dst.y = scaledDrawSize.y - dst.y - dst.height;
        dst.position += docOffset;
        return ToViewRect(dst);
    }

    public Rect LayerDocumentRect(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        return GetLayerDocumentRect(layer);
    }

    public bool IsDocumentPointInsideLayerRect(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 documentPoint)
    {
        return IsLayerDocumentPointInsideRect(layer, documentPoint);
    }

    public Vector2 LayerDocumentPointToView(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 documentPoint)
    {
        if (layer == null)
            return DocumentToView(documentPoint);

        return LayerLocalPointToView(layer, DocumentToLayerLocalPoint(layer, documentPoint));
    }

    public Vector2 LayerLocalPointToView(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 localPoint)
    {
        if (layer == null)
            return DocumentToView(localPoint);

        var layerRectView = LayerToViewRect(layer);
        var size = LayerSize(layer);
        var w = Mathf.Max(1e-4f, size.x);
        var h = Mathf.Max(1e-4f, size.y);
        var nx = localPoint.x / w;
        var ny = localPoint.y / h;
        return new Vector2(
            layerRectView.xMin + nx * layerRectView.width,
            layerRectView.yMax - ny * layerRectView.height);
    }

    public Func<Vector2, Vector2> CreateLayerDocumentToViewMapper(PsdLayerPreviewRenderer.LayerInfo layer, Func<Vector2, Vector2> fallback)
    {
        if (layer == null)
            return fallback;

        return point => LayerDocumentPointToView(layer, point);
    }

    public Vector2 LayerLocalToDocument(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 localPoint)
    {
        return LayerLocalToDocumentPoint(layer, localPoint);
    }

    public Vector2 DocumentToLayerLocal(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 documentPoint)
    {
        return DocumentToLayerLocalPoint(layer, documentPoint);
    }

    public Vector2 DocumentToTextureUv(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 documentPoint, Vector2 textureSize)
    {
        return LayerDocumentToTextureUv(layer, documentPoint, textureSize);
    }

    public bool TryDocumentToTexturePixel(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 documentPoint,
        Vector2Int textureSize,
        out int x,
        out int y)
    {
        return TryLayerDocumentToTexturePixel(layer, documentPoint, textureSize, out x, out y);
    }

    public float DocumentDistanceForViewPixels(Vector2 viewPoint, float pixels)
    {
        var a = ViewToDocument(viewPoint);
        var b = ViewToDocument(viewPoint + new Vector2(Mathf.Max(0.1f, pixels), 0f));
        return Mathf.Max(0.0001f, Vector2.Distance(a, b));
    }

    public float MaxDocumentRadius(Rect viewRect)
    {
        var topLeft = ViewToDocument(viewRect.position);
        var bottomRight = ViewToDocument(viewRect.position + viewRect.size);
        return Mathf.Max(0.01f, Vector2.Distance(topLeft, bottomRight));
    }

    public Vector2 DocToView(Vector2 point)
    {
        return DocumentToView(point);
    }

    public Vector2 ViewToDoc(Vector2 point)
    {
        return ViewToDocument(point);
    }

    public static Vector2 MapMeshPointOrSelf(
        Vector2[] fromVertices,
        Vector2[] toVertices,
        int[] triangles,
        Vector2 point)
    {
        return TryMapPointBetweenMeshes(fromVertices, toVertices, triangles, point, out var mapped) ? mapped : point;
    }

    public static bool TryMapPointBetweenMeshes(
        Vector2[] fromVertices,
        Vector2[] toVertices,
        int[] triangles,
        Vector2 point,
        out Vector2 mapped)
    {
        mapped = point;
        if (fromVertices == null || toVertices == null || triangles == null)
            return false;
        if (fromVertices.Length == 0 || fromVertices.Length != toVertices.Length || triangles.Length < 3)
            return false;

        var hasResult = false;
        var bestScore = float.NegativeInfinity;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0)
                continue;
            if (ia >= fromVertices.Length || ib >= fromVertices.Length || ic >= fromVertices.Length)
                continue;

            if (!TryGetBarycentric(point, fromVertices[ia], fromVertices[ib], fromVertices[ic], out var wa, out var wb, out var wc))
                continue;

            var score = Mathf.Min(wa, Mathf.Min(wb, wc));
            if (hasResult && score < bestScore)
                continue;

            var ta = toVertices[ia];
            var tb = toVertices[ib];
            var tc = toVertices[ic];
            mapped = ta * wa + tb * wb + tc * wc;
            bestScore = score;
            hasResult = true;
        }

        return hasResult;
    }

    public static Vector2 LayerLocalToDocumentPoint(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 localPoint)
    {
        return layer != null ? layer.position + localPoint : localPoint;
    }

    public static Vector2 DocumentToLayerLocalPoint(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 documentPoint)
    {
        return layer != null ? documentPoint - layer.position : documentPoint;
    }

    public static Vector2 MapDocumentPointBetweenLayers(
        PsdLayerPreviewRenderer.LayerInfo sourceLayer,
        PsdLayerPreviewRenderer.LayerInfo targetLayer,
        Vector2 sourceDocumentPoint)
    {
        return LayerLocalToDocumentPoint(targetLayer, DocumentToLayerLocalPoint(sourceLayer, sourceDocumentPoint));
    }

    public static Vector2[] MapDocumentPointsBetweenLayers(
        PsdLayerPreviewRenderer.LayerInfo sourceLayer,
        PsdLayerPreviewRenderer.LayerInfo targetLayer,
        IReadOnlyList<Vector2> sourceDocumentPoints)
    {
        if (sourceDocumentPoints == null || sourceDocumentPoints.Count == 0)
            return Array.Empty<Vector2>();

        var result = new Vector2[sourceDocumentPoints.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = MapDocumentPointBetweenLayers(sourceLayer, targetLayer, sourceDocumentPoints[i]);
        return result;
    }

    public static Vector2 LayerDocumentToImporterPosition(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 documentPoint,
        float pixelsPerUnit,
        Vector2 pivotPixels)
    {
        var local = DocumentToLayerLocalPoint(layer, documentPoint);
        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        return (local - pivotPixels) / ppu;
    }

    public static Vector2 LayerDocumentToImporterPositionByMode(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 documentPoint,
        float pixelsPerUnit,
        Vector2 pivotPixels,
        string importerMode)
    {
        var local = DocumentToLayerLocalPoint(layer, documentPoint);
        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        var sourceRectOffset = layer != null
            ? new Vector2(layer.sourceRect.x, layer.sourceRect.y)
            : Vector2.zero;
        var mode = string.IsNullOrWhiteSpace(importerMode) ? "raw_local" : importerMode.Trim();
        const string flipSuffix = "_flip_y";
        if (mode.EndsWith(flipSuffix, StringComparison.Ordinal))
        {
            mode = mode.Substring(0, mode.Length - flipSuffix.Length);
            var flipHeight = layer != null ? Mathf.Max(1f, layer.sourceRect.height) : 1f;
            local.y = flipHeight - local.y;
        }

        switch (mode)
        {
            case "raw_local":
                return local;
            case "raw_plus_pivot":
                return local - pivotPixels;
            case "raw_minus_source_rect":
                return local + sourceRectOffset;
            case "raw_as_world":
                return documentPoint;
            case "raw_ppu_plus_pivot":
            default:
                return (local - pivotPixels) / ppu;
        }
    }

    public static Vector2 LayerDocumentToTextureUv(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 documentPoint,
        Vector2 textureSize)
    {
        if (layer == null)
            return Vector2.zero;

        var texW = Mathf.Max(1f, textureSize.x);
        var texH = Mathf.Max(1f, textureSize.y);
        var textureRect = LayerTextureRect(layer, textureSize);
        var local = DocumentToLayerLocalPoint(layer, documentPoint);
        var texel = LayerLocalToTextureSamplingPoint(layer, local, textureRect);
        return new Vector2(texel.x / texW, texel.y / texH);
    }

    public static Rect LayerTextureUvRect(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 textureSize,
        bool flipVerticallyForGui)
    {
        if (layer == null)
            return Rect.zero;

        var texW = Mathf.Max(1f, textureSize.x);
        var texH = Mathf.Max(1f, textureSize.y);
        var textureRect = LayerTextureRect(layer, textureSize);
        var uv = new Rect(
            textureRect.x / texW,
            textureRect.y / texH,
            textureRect.width / texW,
            textureRect.height / texH);
        return flipVerticallyForGui
            ? new Rect(uv.x, uv.y + uv.height, uv.width, -uv.height)
            : uv;
    }

    public static bool TryLayerDocumentToTexturePixel(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 documentPoint,
        Vector2Int textureSize,
        out int x,
        out int y)
    {
        x = 0;
        y = 0;
        if (layer == null || textureSize.x <= 0 || textureSize.y <= 0)
            return false;

        var textureRect = LayerTextureRect(layer, new Vector2(textureSize.x, textureSize.y));
        var local = DocumentToLayerLocalPoint(layer, documentPoint);
        var texel = LayerLocalToTextureSamplingPoint(layer, local, textureRect);
        x = Mathf.Clamp(Mathf.FloorToInt(texel.x), 0, textureSize.x - 1);
        y = Mathf.Clamp(Mathf.FloorToInt(texel.y), 0, textureSize.y - 1);
        return true;
    }

    public static Vector2 LayerLocalToTextureSamplingPoint(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 localPoint,
        Rect textureSamplingRect)
    {
        if (layer == null)
            return localPoint;

        var logicalSize = LayerSize(layer);
        if (logicalSize.x <= 0f || logicalSize.y <= 0f ||
            textureSamplingRect.width <= 0f || textureSamplingRect.height <= 0f)
            return textureSamplingRect.position + localPoint;

        return textureSamplingRect.position + new Vector2(
            localPoint.x * textureSamplingRect.width / logicalSize.x,
            localPoint.y * textureSamplingRect.height / logicalSize.y);
    }

    public static Rect GetLayerDocumentRect(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (layer == null)
            return Rect.zero;

        var size = LayerSize(layer);
        return new Rect(layer.position.x, layer.position.y, size.x, size.y);
    }

    public static bool IsLayerDocumentPointInsideRect(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 documentPoint)
    {
        return GetLayerDocumentRect(layer).Contains(documentPoint);
    }

    private Vector2 DocumentToCanvas(Vector2 point)
    {
        var p = (point - canvasMin) * fitScale;
        p.y = scaledDrawSize.y - p.y;
        p += docOffset;
        return p;
    }

    private Vector2 CanvasToDocument(Vector2 point)
    {
        var p = point - docOffset;
        p.y = scaledDrawSize.y - p.y;
        if (fitScale > 0f)
            p /= fitScale;
        p += canvasMin;
        return p;
    }

    private Vector2 ToView(Vector2 point)
    {
        return navigator != null ? navigator.ToView(localRect, point) : point;
    }

    private Vector2 FromView(Vector2 point)
    {
        return navigator != null ? navigator.FromView(localRect, point) : point;
    }

    private Rect ToViewRect(Rect rect)
    {
        return navigator != null ? navigator.ToView(localRect, rect) : rect;
    }

    private static Vector2 LayerSize(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        return layer != null
            ? new Vector2(layer.sourceRect.width, layer.sourceRect.height)
            : Vector2.zero;
    }

    private Vector2 LayoutPosition(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (layer == null)
            return Vector2.zero;

        if (layoutMode == PsdPreviewLayoutMode.SpriteAtlas)
            return TextureRect(layer).position;

        return layer.position;
    }

    private Vector2 LayoutSize(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (layer == null)
            return Vector2.zero;

        if (layoutMode == PsdPreviewLayoutMode.SpriteAtlas)
        {
            var rect = TextureRect(layer);
            if (rect.width > 0f && rect.height > 0f)
                return new Vector2(rect.width, rect.height);
        }

        return LayerSize(layer);
    }

    private static Rect TextureRect(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (layer == null)
            return Rect.zero;

        return layer.sourceRect.width > 0f && layer.sourceRect.height > 0f
            ? layer.sourceRect
            : layer.textureRect;
    }

    public static Rect LayerTextureRect(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 textureSize)
    {
        if (layer == null)
            return Rect.zero;

        if ((textureSize.x <= 0f || textureSize.y <= 0f) && layer.texture != null)
            textureSize = new Vector2(layer.texture.width, layer.texture.height);
        if (layer.textureSamplingRect.width > 0f &&
            layer.textureSamplingRect.height > 0f &&
            ((textureSize.x <= 0f || textureSize.y <= 0f) || RectFitsTexture(layer.textureSamplingRect, textureSize)))
            return layer.textureSamplingRect;
        return ResolveLayerTextureRect(layer.sourceRect, layer.textureRect, textureSize);
    }

    public static Rect LayerSpriteImportRect(PsdLayerPreviewRenderer.LayerInfo layer, Vector2 textureSize)
    {
        if (layer == null)
            return Rect.zero;
        if (layer.sourceRect.width > 0f && layer.sourceRect.height > 0f)
            return layer.sourceRect;
        if (layer.textureRect.width > 0f && layer.textureRect.height > 0f)
            return layer.textureRect;
        return LayerTextureRect(layer, textureSize);
    }

    public static Rect ResolveLayerTextureRect(Rect sourceRect, Rect importedTextureRect)
    {
        return ResolveLayerTextureRect(sourceRect, importedTextureRect, Vector2.zero);
    }

    public static Rect ResolveLayerTextureRect(Rect sourceRect, Rect importedTextureRect, Vector2 textureSize)
    {
        if (sourceRect.width <= 0f || sourceRect.height <= 0f)
            return importedTextureRect;
        if (importedTextureRect.width <= 0f || importedTextureRect.height <= 0f)
            return sourceRect;

        if (textureSize.x > 0f && textureSize.y > 0f)
        {
            var sourceFits = RectFitsTexture(sourceRect, textureSize);
            var importedFits = RectFitsTexture(importedTextureRect, textureSize);
            if (sourceFits != importedFits)
                return sourceFits ? sourceRect : importedTextureRect;
        }

        var samePosition = Mathf.Abs(sourceRect.x - importedTextureRect.x) <= 0.0001f &&
                           Mathf.Abs(sourceRect.y - importedTextureRect.y) <= 0.0001f;
        var sameSize = Mathf.Abs(sourceRect.width - importedTextureRect.width) <= 0.0001f &&
                       Mathf.Abs(sourceRect.height - importedTextureRect.height) <= 0.0001f;
        if (samePosition || sameSize || IsUniformOriginScale(sourceRect, importedTextureRect))
            return sourceRect;

        return importedTextureRect;
    }

    public static Rect ResolveLayerTextureRect(
        Rect sourceRect,
        Rect serializedTextureRect,
        Rect importedTextureRect,
        Vector2 textureSize)
    {
        if (RectFitsTexture(serializedTextureRect, textureSize))
            return serializedTextureRect;

        return ResolveLayerTextureRect(sourceRect, importedTextureRect, textureSize);
    }

    public static Rect ResolveCurrentTextureSamplingRect(
        Rect serializedTextureRect,
        Rect importedSpriteRect,
        Vector2 textureSize)
    {
        if (RectFitsTexture(importedSpriteRect, textureSize))
            return importedSpriteRect;
        if (RectFitsTexture(serializedTextureRect, textureSize))
            return serializedTextureRect;
        return importedSpriteRect.width > 0f && importedSpriteRect.height > 0f
            ? importedSpriteRect
            : serializedTextureRect;
    }

    private static bool RectFitsTexture(Rect rect, Vector2 textureSize)
    {
        const float epsilon = 0.001f;
        return rect.xMin >= -epsilon &&
               rect.yMin >= -epsilon &&
               rect.xMax <= textureSize.x + epsilon &&
               rect.yMax <= textureSize.y + epsilon;
    }

    public static Rect TextureSamplingRectToAtlasRect(Rect atlasReferenceRect, Rect textureReferenceRect, Rect textureRect)
    {
        if (atlasReferenceRect.width <= 0f || atlasReferenceRect.height <= 0f ||
            textureReferenceRect.width <= 0f || textureReferenceRect.height <= 0f)
            return textureRect;

        var scale = new Vector2(
            atlasReferenceRect.width / textureReferenceRect.width,
            atlasReferenceRect.height / textureReferenceRect.height);
        return new Rect(
            atlasReferenceRect.x + (textureRect.x - textureReferenceRect.x) * scale.x,
            atlasReferenceRect.y + (textureRect.y - textureReferenceRect.y) * scale.y,
            textureRect.width * scale.x,
            textureRect.height * scale.y);
    }

    public static Rect AtlasRectToTextureSamplingRect(Rect atlasReferenceRect, Rect textureReferenceRect, Rect atlasRect)
    {
        if (atlasReferenceRect.width <= 0f || atlasReferenceRect.height <= 0f ||
            textureReferenceRect.width <= 0f || textureReferenceRect.height <= 0f)
            return atlasRect;

        var scale = new Vector2(
            textureReferenceRect.width / atlasReferenceRect.width,
            textureReferenceRect.height / atlasReferenceRect.height);
        return new Rect(
            textureReferenceRect.x + (atlasRect.x - atlasReferenceRect.x) * scale.x,
            textureReferenceRect.y + (atlasRect.y - atlasReferenceRect.y) * scale.y,
            atlasRect.width * scale.x,
            atlasRect.height * scale.y);
    }

    private static bool IsUniformOriginScale(Rect sourceRect, Rect importedTextureRect)
    {
        if (sourceRect.width <= 0f || sourceRect.height <= 0f)
            return false;

        var scaleX = importedTextureRect.width / sourceRect.width;
        var scaleY = importedTextureRect.height / sourceRect.height;
        if (scaleX <= 0f || Mathf.Abs(scaleX - scaleY) > 0.0001f)
            return false;
        if (Mathf.Abs(scaleX - 1f) <= 0.0001f)
            return false;

        return Mathf.Abs(importedTextureRect.x - sourceRect.x * scaleX) <= 0.0001f &&
               Mathf.Abs(importedTextureRect.y - sourceRect.y * scaleY) <= 0.0001f;
    }

    public static Rect ResolveSpriteRectForPsdLayerSize(Rect serializedSpriteRect, Vector2Int layerSizeOnFile)
    {
        if (serializedSpriteRect.width <= 0f || serializedSpriteRect.height <= 0f)
            return serializedSpriteRect;
        if (layerSizeOnFile.x <= 0 || layerSizeOnFile.y <= 0)
            return serializedSpriteRect;

        if (Mathf.Abs(serializedSpriteRect.width - layerSizeOnFile.x) <= 0.0001f &&
            Mathf.Abs(serializedSpriteRect.height - layerSizeOnFile.y) <= 0.0001f)
            return serializedSpriteRect;

        return new Rect(serializedSpriteRect.x, serializedSpriteRect.y, layerSizeOnFile.x, layerSizeOnFile.y);
    }

    public static Rect ResolveSpriteRectForPsdLayerSize(
        Rect serializedSpriteRect,
        Vector2Int layerSizeOnFile,
        Vector2 mosaicPosition,
        bool hasMosaicPosition)
    {
        var resized = ResolveSpriteRectForPsdLayerSize(serializedSpriteRect, layerSizeOnFile);
        if (!hasMosaicPosition || RectsHaveSameSize(serializedSpriteRect, resized))
            return resized;

        resized.position = mosaicPosition;
        return resized;
    }

    public static Rect ResolveSpriteRectForPsdLayerImport(
        Rect serializedSpriteRect,
        Vector2Int layerSizeOnFile,
        Vector2 mosaicPosition,
        bool hasMosaicPosition)
    {
        if (!hasMosaicPosition || !IsUniformlyDownscaledSpriteRect(serializedSpriteRect, layerSizeOnFile, mosaicPosition))
            return serializedSpriteRect;

        return new Rect(mosaicPosition.x, mosaicPosition.y, layerSizeOnFile.x, layerSizeOnFile.y);
    }

    private static bool IsUniformlyDownscaledSpriteRect(
        Rect serializedSpriteRect,
        Vector2Int layerSizeOnFile,
        Vector2 mosaicPosition)
    {
        return IsUniformlyDownscaledRect(
            new Rect(mosaicPosition.x, mosaicPosition.y, layerSizeOnFile.x, layerSizeOnFile.y),
            serializedSpriteRect);
    }

    public static bool RequiresPsdLayerRectRepair(
        Rect currentSpriteRect,
        Rect serializedSpriteRect,
        Rect psdLayerReferenceRect)
    {
        return RectsHaveSameSize(currentSpriteRect, psdLayerReferenceRect) &&
               IsUniformlyDownscaledRect(psdLayerReferenceRect, serializedSpriteRect);
    }

    public static Vector2 ScaleSpriteLocalPointForRectResize(
        Vector2 point,
        Rect previousRect,
        Rect nextRect)
    {
        return Vector2.Scale(point, GetSpriteLocalRectResizeScale(previousRect, nextRect));
    }

    public static Vector2 ScaleSpriteLocalVectorForRectResize(
        Vector2 vector,
        Rect previousRect,
        Rect nextRect)
    {
        return Vector2.Scale(vector, GetSpriteLocalRectResizeScale(previousRect, nextRect));
    }

    private static Vector2 GetSpriteLocalRectResizeScale(Rect previousRect, Rect nextRect)
    {
        if (previousRect.width <= 0f || previousRect.height <= 0f ||
            nextRect.width <= 0f || nextRect.height <= 0f)
            return Vector2.one;

        return new Vector2(
            nextRect.width / previousRect.width,
            nextRect.height / previousRect.height);
    }

    private static bool IsUniformlyDownscaledRect(Rect largerRect, Rect smallerRect)
    {
        if (largerRect.width <= 0f || largerRect.height <= 0f ||
            smallerRect.width <= 0f || smallerRect.height <= 0f)
            return false;

        var scaleX = largerRect.width / smallerRect.width;
        var scaleY = largerRect.height / smallerRect.height;
        if (scaleX < 1.5f || Mathf.Abs(scaleX - scaleY) > 0.03f)
            return false;

        var scale = (scaleX + scaleY) * 0.5f;
        var integerScale = Mathf.Round(scale);
        if (integerScale >= 2f && Mathf.Abs(scale - integerScale) <= 0.1f)
            scale = integerScale;

        return Mathf.Abs(largerRect.x - smallerRect.x * scale) <= 2f &&
               Mathf.Abs(largerRect.y - smallerRect.y * scale) <= 2f;
    }

    private static bool RectsHaveSameSize(Rect left, Rect right)
    {
        return Mathf.Abs(left.width - right.width) <= 0.0001f &&
               Mathf.Abs(left.height - right.height) <= 0.0001f;
    }

    public static Vector2 ResolveSpriteAtlasPositionForImportSync(Rect serializedSpriteRect, Rect layerSourceRect)
    {
        if (serializedSpriteRect.width > 0f && serializedSpriteRect.height > 0f)
            return serializedSpriteRect.position;
        return layerSourceRect.position;
    }

    public static Vector2 PreserveLayerDocumentPositionForSpriteRectChange(Vector2 layerPosition)
    {
        return layerPosition;
    }

    public static Vector2 ResolveSpritePositionForSpriteRectChange(Vector2 currentSpritePosition, Rect previousSpriteRect, Rect nextSpriteRect)
    {
        if (previousSpriteRect.width <= 0f || previousSpriteRect.height <= 0f ||
            nextSpriteRect.width <= 0f || nextSpriteRect.height <= 0f)
            return currentSpritePosition;

        return currentSpritePosition + (nextSpriteRect.position - previousSpriteRect.position);
    }

    public static Vector2 ResolveSpriteVisualAnchor(Vector2 spritePosition, Rect spriteRect, Vector2 uvTransform)
    {
        return spritePosition - (spriteRect.position - uvTransform);
    }

    private static bool TryGetBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float wa, out float wb, out float wc)
    {
        wa = wb = wc = 0f;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var d00 = Vector2.Dot(v0, v0);
        var d01 = Vector2.Dot(v0, v1);
        var d11 = Vector2.Dot(v1, v1);
        var d20 = Vector2.Dot(v2, v0);
        var d21 = Vector2.Dot(v2, v1);
        var denom = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denom) < 1e-5f)
            return false;

        var v = (d11 * d20 - d01 * d21) / denom;
        var w = (d00 * d21 - d01 * d20) / denom;
        var u = 1f - v - w;
        const float eps = -0.001f;
        if (u < eps || v < eps || w < eps)
            return false;

        wa = u;
        wb = v;
        wc = w;
        return true;
    }
}

internal sealed class PreviewCoordinateMapper : PsdCoordinateSpace
{
    public PreviewCoordinateMapper(
        Rect localRect,
        Vector2Int documentSize,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers,
        PreviewNavigator navigator)
        : base(localRect, documentSize, layers, navigator)
    {
    }
}
#endif
