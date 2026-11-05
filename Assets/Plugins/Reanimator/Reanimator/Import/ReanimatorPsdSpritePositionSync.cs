#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

internal static class ReanimatorPsdSpritePositionSync
{
    private static readonly string[] SpriteImportDataRootNames =
    {
        "m_LayeredSpriteImportData",
        "layeredSpriteImportData",
        "m_RigSpriteImportData",
        "rigSpriteImportData",
        "m_SharedRigSpriteImportData",
        "sharedRigSpriteImportData",
        "m_MosaicSpriteImportData",
        "mosaicSpriteImportData",
        "m_SpriteImportData",
        "spriteImportData"
    };

    private static readonly string[] CharacterDataRootNames =
    {
        "m_CharacterData",
        "characterData",
        "m_SharedRigCharacterData",
        "sharedRigCharacterData"
    };

    public static bool SyncSpriteImportPositionsFromCharacterData(PSDImporter importer)
    {
        if (importer == null)
            return false;

        var so = new SerializedObject(importer);
        var partsByGuid = BuildCharacterPartLookup(so);
        if (partsByGuid.Count == 0)
            return false;

        var changed = false;
        for (var i = 0; i < SpriteImportDataRootNames.Length; i++)
        {
            var spriteList = so.FindProperty(SpriteImportDataRootNames[i]);
            if (spriteList == null || !spriteList.isArray)
                continue;

            changed |= SyncSpriteList(spriteList, partsByGuid);
        }

        if (!changed)
            return false;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(importer);
        return true;
    }

    public static bool SyncCharacterDataFromSpriteImportData(PSDImporter importer)
    {
        if (importer == null)
            return false;

        var so = new SerializedObject(importer);
        return SyncCharacterDataFromSpriteImportData(so, importer);
    }

    internal static bool SyncCharacterDataFromSpriteImportData(SerializedObject so, UnityEngine.Object owner)
    {
        if (so == null)
            return false;

        var spritesByGuid = BuildSpriteImportLookup(so);
        if (spritesByGuid.Count == 0)
            return false;

        var changed = false;
        for (var rootIndex = 0; rootIndex < CharacterDataRootNames.Length; rootIndex++)
        {
            var characterData = so.FindProperty(CharacterDataRootNames[rootIndex]);
            if (characterData == null)
                continue;

            var parts = characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts");
            if (parts == null || !parts.isArray)
                continue;

            for (var i = 0; i < parts.arraySize; i++)
            {
                var part = parts.GetArrayElementAtIndex(i);
                if (part == null)
                    continue;

                var guid = GetGuidStringProperty(part, "spriteId", "m_SpriteId", "spriteID", "m_SpriteID");
                if (string.IsNullOrEmpty(guid) || !spritesByGuid.TryGetValue(guid, out var sprite))
                    continue;

                changed |= WriteRectIntIfChanged(
                    part.FindPropertyRelative("spritePosition") ?? part.FindPropertyRelative("m_SpritePosition"),
                    sprite.position,
                    sprite.rect.size);
            }
        }

        if (!changed)
            return false;

        so.ApplyModifiedPropertiesWithoutUndo();
        if (owner != null)
            EditorUtility.SetDirty(owner);
        return true;
    }

    private static Dictionary<string, SpriteImportRecord> BuildSpriteImportLookup(SerializedObject so)
    {
        var result = new Dictionary<string, SpriteImportRecord>(StringComparer.OrdinalIgnoreCase);
        if (so == null)
            return result;

        for (var rootIndex = 0; rootIndex < SpriteImportDataRootNames.Length; rootIndex++)
        {
            var spriteList = so.FindProperty(SpriteImportDataRootNames[rootIndex]);
            if (spriteList == null || !spriteList.isArray)
                continue;

            for (var i = 0; i < spriteList.arraySize; i++)
            {
                var spriteElement = spriteList.GetArrayElementAtIndex(i);
                if (spriteElement == null)
                    continue;

                var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
                if (string.IsNullOrEmpty(guid) || result.ContainsKey(guid))
                    continue;

                var rect = ReadRectProperty(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"));
                if (rect.width <= 0f || rect.height <= 0f)
                    continue;

                result[guid] = new SpriteImportRecord
                {
                    rect = rect,
                    position = ReadVector2Property(spriteElement.FindPropertyRelative("spritePosition") ??
                                                   spriteElement.FindPropertyRelative("m_SpritePosition"))
                };
            }
        }

        return result;
    }

    private static bool SyncSpriteList(SerializedProperty spriteList, Dictionary<string, CharacterPartRecord> partsByGuid)
    {
        var changed = false;
        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var spriteElement = spriteList.GetArrayElementAtIndex(i);
            if (spriteElement == null)
                continue;

            var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
            if (string.IsNullOrEmpty(guid) || !partsByGuid.TryGetValue(guid, out var part))
                continue;

            var rect = ReadRectProperty(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"));
            if (rect.width <= 0f || rect.height <= 0f)
                continue;

            if (!RectSizeMatches(rect, part.rect))
                continue;

            var desiredPosition = new Vector2(part.rect.x, part.rect.y);
            var positionProp = spriteElement.FindPropertyRelative("spritePosition") ??
                               spriteElement.FindPropertyRelative("m_SpritePosition");
            changed |= ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(positionProp, desiredPosition);
            changed |= WriteRectIntIfChanged(part.spritePositionProperty, desiredPosition, rect.size);
        }

        return changed;
    }

    private static Dictionary<string, CharacterPartRecord> BuildCharacterPartLookup(SerializedObject so)
    {
        var result = new Dictionary<string, CharacterPartRecord>(StringComparer.OrdinalIgnoreCase);
        if (so == null)
            return result;

        for (var rootIndex = 0; rootIndex < CharacterDataRootNames.Length; rootIndex++)
        {
            var characterData = so.FindProperty(CharacterDataRootNames[rootIndex]);
            if (characterData == null)
                continue;

            var parts = characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts");
            if (parts == null || !parts.isArray)
                continue;

            for (var i = 0; i < parts.arraySize; i++)
            {
                var part = parts.GetArrayElementAtIndex(i);
                if (part == null)
                    continue;

                var guid = GetGuidStringProperty(part, "spriteId", "m_SpriteId", "spriteID", "m_SpriteID");
                if (string.IsNullOrEmpty(guid))
                    continue;

                var spritePosition = part.FindPropertyRelative("spritePosition") ??
                                     part.FindPropertyRelative("m_SpritePosition");
                if (spritePosition == null)
                    continue;

                result[guid] = new CharacterPartRecord
                {
                    spritePositionProperty = spritePosition,
                    rect = ReadRectIntProperty(spritePosition)
                };
            }
        }

        return result;
    }

    private static bool RectSizeMatches(Rect rect, RectInt partRect)
    {
        return Mathf.Abs(rect.width - partRect.width) <= 0.0001f &&
               Mathf.Abs(rect.height - partRect.height) <= 0.0001f;
    }

    private static Rect ReadRectProperty(SerializedProperty property)
    {
        if (property == null)
            return Rect.zero;

        if (property.propertyType == SerializedPropertyType.Rect)
            return property.rectValue;

        return new Rect(
            ReadNumber(property.FindPropertyRelative("x")),
            ReadNumber(property.FindPropertyRelative("y")),
            ReadNumber(property.FindPropertyRelative("width")),
            ReadNumber(property.FindPropertyRelative("height")));
    }

    private static RectInt ReadRectIntProperty(SerializedProperty property)
    {
        if (property == null)
            return default;

        if (property.propertyType == SerializedPropertyType.RectInt)
            return property.rectIntValue;

        return new RectInt(
            Mathf.RoundToInt(ReadNumber(property.FindPropertyRelative("x"))),
            Mathf.RoundToInt(ReadNumber(property.FindPropertyRelative("y"))),
            Mathf.RoundToInt(ReadNumber(property.FindPropertyRelative("width"))),
            Mathf.RoundToInt(ReadNumber(property.FindPropertyRelative("height"))));
    }

    private static Vector2 ReadVector2Property(SerializedProperty property)
    {
        if (property == null)
            return Vector2.zero;

        if (property.propertyType == SerializedPropertyType.Vector2)
            return property.vector2Value;
        if (property.propertyType == SerializedPropertyType.Vector2Int)
            return property.vector2IntValue;
        if (property.propertyType == SerializedPropertyType.Vector3)
        {
            var value = property.vector3Value;
            return new Vector2(value.x, value.y);
        }

        return new Vector2(
            ReadNumber(property.FindPropertyRelative("x")),
            ReadNumber(property.FindPropertyRelative("y")));
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
        changed |= ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(property.FindPropertyRelative("x"), value.x);
        changed |= ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(property.FindPropertyRelative("y"), value.y);
        changed |= ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(property.FindPropertyRelative("width"), value.width);
        changed |= ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(property.FindPropertyRelative("height"), value.height);
        return changed;
    }

    private static float ReadNumber(SerializedProperty property)
    {
        if (property == null)
            return 0f;

        return property.propertyType switch
        {
            SerializedPropertyType.Float => property.floatValue,
            SerializedPropertyType.Integer => property.intValue,
            _ => 0f
        };
    }

    private static string GetGuidStringProperty(SerializedProperty parent, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return string.Empty;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var rel = relativeNames[i];
            if (string.IsNullOrEmpty(rel))
                continue;

            var prop = parent.FindPropertyRelative(rel);
            if (prop == null)
                continue;

            if (prop.propertyType == SerializedPropertyType.String)
                return NormalizeGuidString(prop.stringValue);

            if (prop.propertyType == SerializedPropertyType.Generic)
            {
                var guidProp = prop.FindPropertyRelative("m_GuidString") ??
                               prop.FindPropertyRelative("guid") ??
                               prop.FindPropertyRelative("m_Guid");
                if (guidProp != null && guidProp.propertyType == SerializedPropertyType.String)
                    return NormalizeGuidString(guidProp.stringValue);
            }
        }

        return string.Empty;
    }

    private static string NormalizeGuidString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Replace("-", string.Empty).ToLowerInvariant();
    }

    private struct CharacterPartRecord
    {
        public SerializedProperty spritePositionProperty;
        public RectInt rect;
    }

    private struct SpriteImportRecord
    {
        public Rect rect;
        public Vector2 position;
    }
}
#endif
