#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

internal static class ReanimatorLayerSpriteBindingResolver
{
    public static Dictionary<string, PsdLayerPreviewRenderer.LayerSpriteBinding> BuildBindings(
        ISpriteEditorDataProvider dataProvider,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers,
        Func<PsdLayerPreviewRenderer.LayerInfo, string> buildSelectionKey)
    {
        var result = new Dictionary<string, PsdLayerPreviewRenderer.LayerSpriteBinding>(StringComparer.OrdinalIgnoreCase);
        if (dataProvider == null || layers == null || layers.Count == 0 || buildSelectionKey == null)
            return result;

        var spriteRects = dataProvider.GetSpriteRects();
        if (spriteRects == null || spriteRects.Length == 0)
            return result;

        var ppu = Mathf.Max(1e-4f, dataProvider.pixelsPerUnit);
        var used = new bool[spriteRects.Length];
        var spriteRectIndexByGuid = BuildSpriteRectIndexByGuid(spriteRects);

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var rectIndex = ResolveSpriteRectIndex(spriteRects, used, spriteRectIndexByGuid, layer);
            if (rectIndex < 0)
                continue;
            if (string.IsNullOrEmpty(layer.spriteGuid))
                used[rectIndex] = true;

            var spriteRect = spriteRects[rectIndex];
            var key = buildSelectionKey(layer);
            if (string.IsNullOrEmpty(key))
                continue;

            result[key] = new PsdLayerPreviewRenderer.LayerSpriteBinding
            {
                spriteId = spriteRect.spriteID,
                pixelsPerUnit = ppu,
                pivotPixels = new Vector2(spriteRect.rect.width * spriteRect.pivot.x, spriteRect.rect.height * spriteRect.pivot.y),
                rect = spriteRect.rect
            };
        }

        return result;
    }

    public static int ResolveSpriteIndex(
        IReadOnlyList<Sprite> sprites,
        IReadOnlyList<bool> used,
        IReadOnlyDictionary<string, int> spriteIndexByGuid,
        PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (sprites == null || layer == null)
            return -1;

        var spriteGuid = NormalizeGuidString(layer.spriteGuid);
        if (!string.IsNullOrEmpty(spriteGuid) &&
            spriteIndexByGuid != null &&
            spriteIndexByGuid.TryGetValue(spriteGuid, out var byGuidIndex) &&
            byGuidIndex >= 0 &&
            byGuidIndex < sprites.Count)
        {
            return byGuidIndex;
        }

        return FindMatchingSprite(sprites, used, layer);
    }

    public static Dictionary<string, int> BuildSpriteIndexByGuid(IReadOnlyList<Sprite> sprites)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sprites == null)
            return result;

        for (var i = 0; i < sprites.Count; i++)
        {
            var guid = TryGetSpriteStableGuid(sprites[i]);
            if (string.IsNullOrEmpty(guid))
                continue;
            if (!result.ContainsKey(guid))
                result[guid] = i;
        }

        return result;
    }

    public static string TryGetSpriteStableGuid(Sprite sprite)
    {
        if (sprite == null)
            return string.Empty;

        try
        {
            var so = new SerializedObject(sprite);
            var rootProp = so.FindProperty("m_SpriteID")
                          ?? so.FindProperty("spriteID")
                          ?? so.FindProperty("m_SpriteId")
                          ?? so.FindProperty("spriteId");
            if (rootProp == null)
                return string.Empty;

            if (rootProp.propertyType == SerializedPropertyType.String)
                return NormalizeGuidString(rootProp.stringValue);

            if (rootProp.propertyType == SerializedPropertyType.Generic)
            {
                var guidProp = rootProp.FindPropertyRelative("m_GuidString")
                               ?? rootProp.FindPropertyRelative("guid")
                               ?? rootProp.FindPropertyRelative("m_Guid");
                if (guidProp != null && guidProp.propertyType == SerializedPropertyType.String)
                    return NormalizeGuidString(guidProp.stringValue);
            }
        }
        catch
        {
            // Ignore serialized data errors and use fallback matcher.
        }

        return string.Empty;
    }

    private static int ResolveSpriteRectIndex(
        IReadOnlyList<SpriteRect> spriteRects,
        IReadOnlyList<bool> used,
        IReadOnlyDictionary<string, int> spriteRectIndexByGuid,
        PsdLayerPreviewRenderer.LayerInfo layer)
    {
        var rectIndex = -1;
        var layerGuid = NormalizeGuidString(layer != null ? layer.spriteGuid : null);
        if (!string.IsNullOrEmpty(layerGuid) &&
            spriteRectIndexByGuid != null &&
            spriteRectIndexByGuid.TryGetValue(layerGuid, out var byGuidIndex) &&
            byGuidIndex >= 0 &&
            byGuidIndex < spriteRects.Count)
        {
            rectIndex = byGuidIndex;
        }

        return rectIndex >= 0 ? rectIndex : FindMatchingSpriteRect(spriteRects, used, layer);
    }

    private static Dictionary<string, int> BuildSpriteRectIndexByGuid(IReadOnlyList<SpriteRect> spriteRects)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (spriteRects == null)
            return result;

        for (var i = 0; i < spriteRects.Count; i++)
        {
            var rect = spriteRects[i];
            if (rect == null)
                continue;
            var guid = NormalizeGuidString(rect.spriteID.ToString());
            if (string.IsNullOrEmpty(guid))
                continue;
            if (!result.ContainsKey(guid))
                result[guid] = i;
        }

        return result;
    }

    public static int FindMatchingSprite(IReadOnlyList<Sprite> sprites, IReadOnlyList<bool> used, PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (sprites == null || layer == null)
            return -1;

        for (var i = 0; i < sprites.Count; i++)
        {
            if (used[i])
                continue;
            var s = sprites[i];
            if (!string.Equals(s.name, layer.name, StringComparison.Ordinal))
                continue;
            if (!RectsRoughlyEqual(s.rect, layer.sourceRect))
                continue;
            return i;
        }

        for (var i = 0; i < sprites.Count; i++)
        {
            if (used[i])
                continue;
            var s = sprites[i];
            if (!string.Equals(s.name, layer.name, StringComparison.Ordinal))
                continue;
            return i;
        }

        for (var i = 0; i < sprites.Count; i++)
        {
            if (used[i])
                continue;
            var s = sprites[i];
            if (!RectsRoughlyEqual(s.rect, layer.sourceRect))
                continue;
            return i;
        }

        return -1;
    }

    public static int FindMatchingSpriteRect(IReadOnlyList<SpriteRect> spriteRects, IReadOnlyList<bool> used, PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (spriteRects == null || layer == null)
            return -1;

        for (var i = 0; i < spriteRects.Count; i++)
        {
            if (used[i])
                continue;
            var rect = spriteRects[i];
            if (rect == null)
                continue;
            if (!string.Equals(rect.name, layer.name, StringComparison.Ordinal))
                continue;
            if (!RectsRoughlyEqual(rect.rect, layer.sourceRect))
                continue;
            return i;
        }

        for (var i = 0; i < spriteRects.Count; i++)
        {
            if (used[i])
                continue;
            var rect = spriteRects[i];
            if (rect == null)
                continue;
            if (!string.Equals(rect.name, layer.name, StringComparison.Ordinal))
                continue;
            return i;
        }

        return -1;
    }

    private static bool RectsRoughlyEqual(Rect a, Rect b)
    {
        const float eps = 0.01f;
        return Mathf.Abs(a.x - b.x) <= eps
               && Mathf.Abs(a.y - b.y) <= eps
               && Mathf.Abs(a.width - b.width) <= eps
               && Mathf.Abs(a.height - b.height) <= eps;
    }

    private static string NormalizeGuidString(string value)
    {
        return ReanimatorRebuildMetaAlgorithms.NormalizeGuidString(value);
    }
}
#endif
