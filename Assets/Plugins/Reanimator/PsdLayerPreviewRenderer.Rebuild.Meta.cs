#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    private static Dictionary<string, RectInt> BuildCharacterPartRectBySpriteGuid(SerializedObject importer)
    {
        var result = new Dictionary<string, RectInt>(StringComparer.OrdinalIgnoreCase);
        if (importer == null)
            return result;

        var characterData = importer.FindProperty("m_CharacterData")
                            ?? importer.FindProperty("characterData")
                            ?? importer.FindProperty("m_SharedRigCharacterData")
                            ?? importer.FindProperty("sharedRigCharacterData");
        if (characterData == null)
            return result;

        var partsProp = characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts");
        if (partsProp == null || !partsProp.isArray || partsProp.arraySize <= 0)
            return result;

        for (var i = 0; i < partsProp.arraySize; i++)
        {
            var partProp = partsProp.GetArrayElementAtIndex(i);
            if (partProp == null)
                continue;

            var spriteGuid = GetGuidStringProperty(partProp, "spriteId", "m_SpriteId", "spriteID", "m_SpriteID");
            if (string.IsNullOrEmpty(spriteGuid) || result.ContainsKey(spriteGuid))
                continue;

            var positionProp = partProp.FindPropertyRelative("spritePosition") ?? partProp.FindPropertyRelative("m_SpritePosition");
            if (positionProp == null)
                continue;

            result[spriteGuid] = GetRectIntFromProperty(positionProp);
        }

        return result;
    }

    private static bool CharacterPartRectSizeMatchesSpriteRect(RectInt characterPartRect, Rect spriteRect)
    {
        return Mathf.Abs(characterPartRect.width - spriteRect.width) <= 0.0001f &&
               Mathf.Abs(characterPartRect.height - spriteRect.height) <= 0.0001f;
    }

    private static RectInt GetRectIntFromProperty(SerializedProperty property)
    {
        if (property == null)
            return default;

        if (property.propertyType == SerializedPropertyType.RectInt)
            return property.rectIntValue;

        TryGetNumericPropertyValue(property.FindPropertyRelative("x"), out var x);
        TryGetNumericPropertyValue(property.FindPropertyRelative("y"), out var y);
        TryGetNumericPropertyValue(property.FindPropertyRelative("width"), out var width);
        TryGetNumericPropertyValue(property.FindPropertyRelative("height"), out var height);
        return new RectInt(
            Mathf.RoundToInt(x),
            Mathf.RoundToInt(y),
            Mathf.RoundToInt(width),
            Mathf.RoundToInt(height));
    }

    private static Dictionary<string, Vector2> BuildMetaSpritePositionBySpriteGuid(string assetPath)
    {
        var result = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(assetPath))
            return result;

        var metaPath = assetPath + ".meta";
        if (!File.Exists(metaPath))
            return result;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(metaPath);
        }
        catch
        {
            return result;
        }

        return ReanimatorRebuildMetaAlgorithms.ParseMetaSpritePositionBySpriteGuid(lines);
    }

    private static bool ShouldUseMetaSpritePosition(Vector2 currentPosition, Rect rect, Vector2 metaPosition, bool hasSerializedPosition)
    {
        return ReanimatorRebuildMetaAlgorithms.ShouldUseMetaSpritePosition(currentPosition, rect, metaPosition, hasSerializedPosition);
    }

    private static bool TryParseSpriteGuidLine(string trimmedLine, out string guid)
    {
        return ReanimatorRebuildMetaAlgorithms.TryParseSpriteGuidLine(trimmedLine, out guid);
    }

    private static bool TryReadSpritePosition(IReadOnlyList<string> lines, int lineIndex, out Vector2 position)
    {
        return ReanimatorRebuildMetaAlgorithms.TryReadSpritePosition(lines, lineIndex, out position);
    }

    private static bool TryParseInlineVector2(string line, out Vector2 position)
    {
        return ReanimatorRebuildMetaAlgorithms.TryParseInlineVector2(line, out position);
    }

    private static Rect GetTextureRect(LayerInfo layer)
    {
        return PsdCoordinateSpace.LayerTextureRect(layer, Vector2.zero);
    }

    private static Rect GetTextureRect(LayerInfo layer, Vector2 textureSize)
    {
        return PsdCoordinateSpace.LayerTextureRect(layer, textureSize);
    }

}

#endif
