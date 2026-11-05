#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private bool ApplySpriteAtlasPositionsToImporter(PSDImporter importer)
    {
        if (importer == null)
            return false;
        if (!HasDirtyLayerSpriteState())
            return false;

        var so = new SerializedObject(importer);
        var spriteList = ResolveSpriteList(so);
        if (spriteList == null || !spriteList.isArray)
            return false;

        var layerByGuid = new Dictionary<string, LayerInfo>(StringComparer.OrdinalIgnoreCase);
        if (layerInfos != null)
        {
            for (var i = 0; i < layerInfos.Count; i++)
            {
                var layer = layerInfos[i];
                if (layer == null || string.IsNullOrEmpty(layer.spriteGuid))
                    continue;
                if (!IsLayerSpriteStateDirty(layer, layer.spriteGuid))
                    continue;

                var guid = NormalizeGuidString(layer.spriteGuid);
                if (!string.IsNullOrEmpty(guid))
                    layerByGuid[guid] = layer;
            }
        }

        var changed = false;
        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var spriteElement = spriteList.GetArrayElementAtIndex(i);
            if (spriteElement == null)
                continue;

            LayerInfo layer = null;
            var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
            if (!string.IsNullOrEmpty(guid))
                layerByGuid.TryGetValue(guid, out layer);

            if (!IsLayerSpriteStateDirty(layer, guid))
                continue;

            var layerRect = layer != null ? layer.sourceRect : Rect.zero;
            var spriteRect = ReadRectProperty(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"), layerRect);
            if (spriteRect.width <= 0f || spriteRect.height <= 0f)
                continue;

            var atlasPosition = PsdCoordinateSpace.ResolveSpriteAtlasPositionForImportSync(spriteRect, layerRect);
            changed |= SyncSpriteUvTransformIfChanged(spriteElement, atlasPosition);
        }

        if (!changed)
            return false;

        ApplyImporterSerializedProperties(so, importer);
        return true;
    }

    private bool ApplySpriteMosaicPositionsToImporter(
        SerializedObject importer,
        Dictionary<string, LayerInfo> layerByGuid,
        Dictionary<string, Vector2> atlasPositionByGuid,
        Dictionary<string, Vector2> atlasPositionByStableKey)
    {
        if (importer == null)
            return false;

        var layerByStableKey = new Dictionary<string, LayerInfo>(StringComparer.OrdinalIgnoreCase);
        if (layerInfos != null)
        {
            for (var i = 0; i < layerInfos.Count; i++)
            {
                var layer = layerInfos[i];
                if (layer == null)
                    continue;
                if (!IsLayerSpriteStateDirty(layer, layer.spriteGuid))
                    continue;

                var stableKey = layer.layerId != 0 ? BuildKey(layer.name, layer.layerId) : string.Empty;
                if (!string.IsNullOrEmpty(stableKey))
                    layerByStableKey[stableKey] = layer;
            }
        }

        var changed = false;
        var rigLayers = importer.FindProperty("m_RigPSDLayers") ?? importer.FindProperty("rigPSDLayers");
        var mosaicLayers = importer.FindProperty("m_MosaicPSDLayers") ?? importer.FindProperty("mosaicPSDLayers");
        changed |= ApplySpriteMosaicPositionsToList(
            rigLayers,
            layerByGuid,
            layerByStableKey,
            atlasPositionByGuid,
            atlasPositionByStableKey);
        changed |= ApplySpriteMosaicPositionsToList(
            mosaicLayers,
            layerByGuid,
            layerByStableKey,
            atlasPositionByGuid,
            atlasPositionByStableKey);
        return changed;
    }

    private bool ApplySpriteMosaicPositionsToList(
        SerializedProperty psdLayerList,
        Dictionary<string, LayerInfo> layerByGuid,
        Dictionary<string, LayerInfo> layerByStableKey,
        Dictionary<string, Vector2> atlasPositionByGuid,
        Dictionary<string, Vector2> atlasPositionByStableKey)
    {
        if (psdLayerList == null || !psdLayerList.isArray)
            return false;

        var changed = false;
        for (var i = 0; i < psdLayerList.arraySize; i++)
        {
            var psdLayerElement = psdLayerList.GetArrayElementAtIndex(i);
            if (psdLayerElement == null)
                continue;

            LayerInfo layer = null;
            var atlasPosition = Vector2.zero;
            var hasAtlasPosition = false;
            var guid = GetGuidStringProperty(psdLayerElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
            if (!string.IsNullOrEmpty(guid) && layerByGuid != null)
            {
                layerByGuid.TryGetValue(guid, out layer);
                hasAtlasPosition = atlasPositionByGuid != null && atlasPositionByGuid.TryGetValue(guid, out atlasPosition);
            }

            if (layer == null && layerByStableKey != null)
            {
                var nameProp = psdLayerElement.FindPropertyRelative("m_Name") ?? psdLayerElement.FindPropertyRelative("name");
                var layerIdProp = psdLayerElement.FindPropertyRelative("m_LayerID") ??
                                  psdLayerElement.FindPropertyRelative("layerID") ??
                                  psdLayerElement.FindPropertyRelative("layerId");
                var layerId = layerIdProp != null ? layerIdProp.intValue : 0;
                var stableKey = layerId != 0 ? BuildKey(nameProp != null ? nameProp.stringValue : string.Empty, layerId) : string.Empty;
                if (!string.IsNullOrEmpty(stableKey))
                {
                    layerByStableKey.TryGetValue(stableKey, out layer);
                    if (!hasAtlasPosition && atlasPositionByStableKey != null)
                        hasAtlasPosition = atlasPositionByStableKey.TryGetValue(stableKey, out atlasPosition);
                }
            }

            if (!IsLayerSpriteStateDirty(layer, guid))
                continue;

            if (layer == null && !hasAtlasPosition)
                continue;

            if (!hasAtlasPosition)
                atlasPosition = layer.sourceRect.position;
            changed |= SyncSpriteMosaicPositionIfChanged(psdLayerElement, atlasPosition);
        }

        return changed;
    }

    private static bool SyncSpriteUvTransformWithLayerIfChanged(SerializedProperty spriteElement, LayerInfo layer)
    {
        var spriteRect = spriteElement != null
            ? ReadRectProperty(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"), layer != null ? layer.sourceRect : Rect.zero)
            : Rect.zero;
        var atlasPosition = layer != null
            ? PsdCoordinateSpace.ResolveSpriteAtlasPositionForImportSync(spriteRect, layer.sourceRect)
            : Vector2.zero;
        return SyncSpriteUvTransformWithLayerIfChanged(spriteElement, layer, atlasPosition);
    }

    private static bool SyncSpriteUvTransformWithLayerIfChanged(SerializedProperty spriteElement, LayerInfo layer, Vector2 atlasPosition)
    {
        if (spriteElement == null || layer == null)
            return false;

        return SyncSpriteUvTransformIfChanged(spriteElement, atlasPosition);
    }

    private static bool SyncSpriteUvTransformIfChanged(SerializedProperty spriteElement, Vector2 atlasPosition)
    {
        if (spriteElement == null)
            return false;

        return WriteVector2IfChanged(
            spriteElement.FindPropertyRelative("m_UVTransform") ?? spriteElement.FindPropertyRelative("uvTransform"),
            atlasPosition);
    }

    private static bool SyncSpriteMosaicPositionWithLayerIfChanged(SerializedProperty psdLayerElement, LayerInfo layer)
    {
        return SyncSpriteMosaicPositionWithLayerIfChanged(
            psdLayerElement,
            layer,
            layer != null ? layer.sourceRect.position : Vector2.zero);
    }

    private static bool SyncSpriteMosaicPositionWithLayerIfChanged(SerializedProperty psdLayerElement, LayerInfo layer, Vector2 atlasPosition)
    {
        if (psdLayerElement == null || layer == null)
            return false;

        return SyncSpriteMosaicPositionIfChanged(psdLayerElement, atlasPosition);
    }

    private static bool SyncSpriteMosaicPositionIfChanged(SerializedProperty psdLayerElement, Vector2 atlasPosition)
    {
        if (psdLayerElement == null)
            return false;

        return WriteVector2IfChanged(
            psdLayerElement.FindPropertyRelative("m_MosaicPosition") ?? psdLayerElement.FindPropertyRelative("mosaicPosition"),
            atlasPosition);
    }

    private static Rect ReadRectProperty(SerializedProperty property, Rect fallback)
    {
        if (property == null)
            return fallback;

        if (property.propertyType == SerializedPropertyType.Rect)
            return property.rectValue;

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        var width = property.FindPropertyRelative("width");
        var height = property.FindPropertyRelative("height");
        if (x == null || y == null || width == null || height == null)
            return fallback;

        return new Rect(x.floatValue, y.floatValue, width.floatValue, height.floatValue);
    }

    private static string ReadSerializedStringProperty(SerializedProperty property)
    {
        if (property == null)
            return string.Empty;

        if (property.propertyType == SerializedPropertyType.String)
            return property.stringValue ?? string.Empty;

        if (!property.isArray)
            return string.Empty;

        var chars = new char[property.arraySize];
        for (var i = 0; i < property.arraySize; i++)
        {
            var element = property.GetArrayElementAtIndex(i);
            if (element == null)
                return string.Empty;

            switch (element.propertyType)
            {
                case SerializedPropertyType.Integer:
                    chars[i] = (char)element.intValue;
                    break;
                case SerializedPropertyType.String:
                    var value = element.stringValue;
                    chars[i] = !string.IsNullOrEmpty(value) ? value[0] : '\0';
                    break;
                default:
                    return string.Empty;
            }
        }

        return new string(chars).TrimEnd('\0');
    }

    private static bool SyncSpriteRectWithLayerIfChanged(SpriteRect spriteRect, LayerInfo layer, Rect sourceRect)
    {
        if (spriteRect == null || layer == null)
            return false;

        var changed = false;

        if (!string.Equals(spriteRect.name, layer.name, StringComparison.Ordinal))
        {
            spriteRect.name = layer.name;
            changed = true;
        }

        if (!RectsRoughlyEqual(spriteRect.rect, sourceRect))
        {
            spriteRect.rect = sourceRect;
            changed = true;
        }

        if ((spriteRect.pivot - layer.pivot).sqrMagnitude > 1e-8f)
        {
            spriteRect.pivot = layer.pivot;
            changed = true;
        }

        if (spriteRect.alignment != (SpriteAlignment)layer.alignment)
        {
            spriteRect.alignment = (SpriteAlignment)layer.alignment;
            changed = true;
        }

        if ((spriteRect.border - layer.border).sqrMagnitude > 1e-8f)
        {
            spriteRect.border = layer.border;
            changed = true;
        }

        return changed;
    }

    private static bool SyncSpriteRectWithLayerIfChanged(SpriteRect spriteRect, LayerInfo layer)
    {
        return SyncSpriteRectWithLayerIfChanged(spriteRect, layer, layer != null ? layer.sourceRect : Rect.zero);
    }

    private static Rect NormalizeSpriteRect(Rect rect)
    {
        return ReanimatorAssetChangeAlgorithms.NormalizeSpriteRect(rect);
    }

    private static bool WriteRectIfChanged(SerializedProperty property, Rect value)
    {
        return ReanimatorAssetChangeAlgorithms.WriteRectIfChanged(property, value);
    }

    private static void WriteRectValue(SerializedProperty property, Rect value)
    {
        if (property == null)
            return;

        if (property.propertyType == SerializedPropertyType.Rect)
        {
            property.rectValue = value;
            return;
        }

        WriteFloatValue(property.FindPropertyRelative("x"), value.x);
        WriteFloatValue(property.FindPropertyRelative("y"), value.y);
        WriteFloatValue(property.FindPropertyRelative("width"), value.width);
        WriteFloatValue(property.FindPropertyRelative("height"), value.height);
    }

    private static void WriteVector2Value(SerializedProperty property, Vector2 value)
    {
        if (property == null)
            return;

        if (property.propertyType == SerializedPropertyType.Vector2)
        {
            property.vector2Value = value;
            return;
        }

        if (property.propertyType == SerializedPropertyType.Vector3)
        {
            var current = property.vector3Value;
            property.vector3Value = new Vector3(value.x, value.y, current.z);
            return;
        }

        WriteFloatValue(property.FindPropertyRelative("x"), value.x);
        WriteFloatValue(property.FindPropertyRelative("y"), value.y);
    }

    private static void WriteFloatValue(SerializedProperty property, float value)
    {
        if (property == null)
            return;

        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
    }

    private static bool WriteVector4IfChanged(SerializedProperty property, Vector4 value)
    {
        return ReanimatorAssetChangeAlgorithms.WriteVector4IfChanged(property, value);
    }

    private static bool WriteIntIfChanged(SerializedProperty property, int value)
    {
        return ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(property, value);
    }

    private static bool WriteBoolOrIntIfChanged(SerializedProperty property, bool value)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Boolean)
        {
            if (property.boolValue == value)
                return false;

            property.boolValue = value;
            return true;
        }

        if (property.propertyType == SerializedPropertyType.Integer)
        {
            var intValue = value ? 1 : 0;
            if (property.intValue == intValue)
                return false;

            property.intValue = intValue;
            return true;
        }

        return false;
    }

    private static bool WriteRectIntIfChanged(SerializedProperty property, Vector2 position, Vector2 size)
    {
        if (property == null)
            return false;

        var value = new RectInt(
            Mathf.RoundToInt(position.x),
            Mathf.RoundToInt(position.y),
            Mathf.RoundToInt(size.x),
            Mathf.RoundToInt(size.y));

        if (property.propertyType == SerializedPropertyType.RectInt)
        {
            var current = property.rectIntValue;
            if (current.x == value.x &&
                current.y == value.y &&
                current.width == value.width &&
                current.height == value.height)
                return false;
            property.rectIntValue = value;
            return true;
        }

        var changed = false;
        changed |= WriteIntIfChanged(property.FindPropertyRelative("x"), value.x);
        changed |= WriteIntIfChanged(property.FindPropertyRelative("y"), value.y);
        changed |= WriteIntIfChanged(property.FindPropertyRelative("width"), value.width);
        changed |= WriteIntIfChanged(property.FindPropertyRelative("height"), value.height);
        return changed;
    }

    private static bool WriteRectIntPositionIfChanged(SerializedProperty property, Vector2 position)
    {
        if (property == null)
            return false;

        var x = Mathf.RoundToInt(position.x);
        var y = Mathf.RoundToInt(position.y);

        if (property.propertyType == SerializedPropertyType.RectInt)
        {
            var current = property.rectIntValue;
            if (current.x == x && current.y == y)
                return false;

            current.x = x;
            current.y = y;
            property.rectIntValue = current;
            return true;
        }

        var changed = false;
        changed |= WriteIntIfChanged(property.FindPropertyRelative("x"), x);
        changed |= WriteIntIfChanged(property.FindPropertyRelative("y"), y);
        return changed;
    }
}
#endif
