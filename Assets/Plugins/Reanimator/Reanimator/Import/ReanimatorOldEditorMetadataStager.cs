#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

internal static class ReanimatorOldEditorMetadataStager
{
    private static readonly string[] CharacterDataRootNames =
    {
        "m_CharacterData",
        "characterData",
        "m_Character",
        "character"
    };

    public static bool Stage(
        PSDImporter importer,
        bool clearResliceRequest,
        bool syncPsdLayerMosaicPositions = true)
    {
        if (importer == null)
            return false;

        var changed = ReanimatorPsdSpritePositionSync.SyncCharacterDataFromSpriteImportData(importer);

        var so = new SerializedObject(importer);
        var serializedChanged = false;
        serializedChanged |= StageLegacySpriteImportDataFromCurrentLists(so);
        serializedChanged |= SyncCharacterPartRectsFromCurrentSpriteImportData(so);
        if (syncPsdLayerMosaicPositions)
            serializedChanged |= SyncPsdLayerMosaicPositionsFromCurrentSpriteImportData(so);
        if (clearResliceRequest)
            serializedChanged |= ClearPsdLayerResliceRequest(so);
        if (serializedChanged)
            ApplyImporterSerializedProperties(so, importer);

        return changed || serializedChanged;
    }

    public static bool StageLegacySpriteImportDataFromCurrentLists(SerializedObject importer)
    {
        if (importer == null)
            return false;

        var source = ReanimatorSpriteListResolver.ResolveSpriteList(importer);
        if (source == null || !source.isArray || source.arraySize == 0)
            return false;

        var changed = false;
        changed |= CopySpriteImportListIfDifferent(importer.FindProperty("m_RigSpriteImportData") ?? importer.FindProperty("rigSpriteImportData"), source);
        var legacySpriteImportData = importer.FindProperty("m_SpriteImportData") ?? importer.FindProperty("spriteImportData");
        if (!IsEmptyLegacySpriteImportPlaceholder(legacySpriteImportData))
            changed |= CopySpriteImportListIfDifferent(legacySpriteImportData, source);
        return changed;
    }

    private static bool IsEmptyLegacySpriteImportPlaceholder(SerializedProperty spriteImportData)
    {
        if (spriteImportData == null || !spriteImportData.isArray || spriteImportData.arraySize != 1)
            return false;

        var element = spriteImportData.GetArrayElementAtIndex(0);
        if (element == null)
            return false;

        var name = GetStringProperty(element, "name", "m_Name");
        var originalName = GetStringProperty(element, "originalName", "m_OriginalName");
        var spriteId = GetGuidStringProperty(element, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
        var rect = ReadRectProperty(element.FindPropertyRelative("m_Rect") ?? element.FindPropertyRelative("rect"), Rect.zero);
        var spritePosition = GetVector2FromProperty(element.FindPropertyRelative("spritePosition") ?? element.FindPropertyRelative("m_SpritePosition"));

        return string.IsNullOrEmpty(name) &&
               string.IsNullOrEmpty(originalName) &&
               string.IsNullOrEmpty(spriteId) &&
               rect.width <= 0f &&
               rect.height <= 0f &&
               spritePosition == Vector2.zero;
    }

    private static bool CopySpriteImportListIfDifferent(SerializedProperty target, SerializedProperty source)
    {
        if (target == null ||
            source == null ||
            !target.isArray ||
            !source.isArray ||
            SerializedProperty.EqualContents(target, source))
        {
            return false;
        }

        var changed = target.arraySize != source.arraySize;
        target.arraySize = source.arraySize;
        for (var i = 0; i < source.arraySize; i++)
        {
            changed |= CopySerializedPropertyValue(
                target.GetArrayElementAtIndex(i),
                source.GetArrayElementAtIndex(i));
        }

        return changed;
    }

    private static bool CopySerializedPropertyValue(SerializedProperty target, SerializedProperty source)
    {
        if (target == null || source == null)
            return false;

        if (source.isArray && source.propertyType != SerializedPropertyType.String)
        {
            var changed = target.arraySize != source.arraySize;
            target.arraySize = source.arraySize;
            for (var i = 0; i < source.arraySize; i++)
            {
                changed |= CopySerializedPropertyValue(
                    target.GetArrayElementAtIndex(i),
                    source.GetArrayElementAtIndex(i));
            }

            return changed;
        }

        switch (source.propertyType)
        {
            case SerializedPropertyType.Integer:
                if (target.intValue == source.intValue)
                    return false;
                target.intValue = source.intValue;
                return true;
            case SerializedPropertyType.Boolean:
                if (target.boolValue == source.boolValue)
                    return false;
                target.boolValue = source.boolValue;
                return true;
            case SerializedPropertyType.Float:
                if (target.floatValue == source.floatValue)
                    return false;
                target.floatValue = source.floatValue;
                return true;
            case SerializedPropertyType.String:
                if (string.Equals(target.stringValue, source.stringValue, StringComparison.Ordinal))
                    return false;
                target.stringValue = source.stringValue;
                return true;
            case SerializedPropertyType.Color:
                if (target.colorValue == source.colorValue)
                    return false;
                target.colorValue = source.colorValue;
                return true;
            case SerializedPropertyType.ObjectReference:
                if (target.objectReferenceValue == source.objectReferenceValue)
                    return false;
                target.objectReferenceValue = source.objectReferenceValue;
                return true;
            case SerializedPropertyType.Enum:
                if (target.enumValueIndex == source.enumValueIndex)
                    return false;
                target.enumValueIndex = source.enumValueIndex;
                return true;
            case SerializedPropertyType.Vector2:
                if (target.vector2Value == source.vector2Value)
                    return false;
                target.vector2Value = source.vector2Value;
                return true;
            case SerializedPropertyType.Vector3:
                if (target.vector3Value == source.vector3Value)
                    return false;
                target.vector3Value = source.vector3Value;
                return true;
            case SerializedPropertyType.Vector4:
                if (target.vector4Value == source.vector4Value)
                    return false;
                target.vector4Value = source.vector4Value;
                return true;
            case SerializedPropertyType.Rect:
                if (target.rectValue == source.rectValue)
                    return false;
                target.rectValue = source.rectValue;
                return true;
            case SerializedPropertyType.Quaternion:
                if (target.quaternionValue == source.quaternionValue)
                    return false;
                target.quaternionValue = source.quaternionValue;
                return true;
            case SerializedPropertyType.Vector2Int:
                if (target.vector2IntValue == source.vector2IntValue)
                    return false;
                target.vector2IntValue = source.vector2IntValue;
                return true;
            case SerializedPropertyType.Vector3Int:
                if (target.vector3IntValue == source.vector3IntValue)
                    return false;
                target.vector3IntValue = source.vector3IntValue;
                return true;
            case SerializedPropertyType.RectInt:
                if (target.rectIntValue.Equals(source.rectIntValue))
                    return false;
                target.rectIntValue = source.rectIntValue;
                return true;
            case SerializedPropertyType.Bounds:
                if (target.boundsValue == source.boundsValue)
                    return false;
                target.boundsValue = source.boundsValue;
                return true;
            case SerializedPropertyType.BoundsInt:
                if (target.boundsIntValue == source.boundsIntValue)
                    return false;
                target.boundsIntValue = source.boundsIntValue;
                return true;
        }

        return CopySerializedPropertyChildren(target, source);
    }

    private static bool CopySerializedPropertyChildren(SerializedProperty target, SerializedProperty source)
    {
        var changed = false;
        var child = source.Copy();
        var end = source.GetEndProperty();
        var enterChildren = true;
        while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;
            if (child.depth != source.depth + 1)
                continue;

            var targetChild = target.FindPropertyRelative(child.name);
            if (targetChild != null)
                changed |= CopySerializedPropertyValue(targetChild, child);
        }

        return changed;
    }

    public static bool SyncCharacterPartRectsFromCurrentSpriteImportData(SerializedObject importer)
    {
        if (importer == null)
            return false;

        var desiredPartRectByGuid = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var spriteLists = ReanimatorSpriteListResolver.ResolveSpriteLists(importer);
        for (var listIndex = 0; listIndex < spriteLists.Count; listIndex++)
        {
            var spriteList = spriteLists[listIndex];
            if (spriteList == null || !spriteList.isArray)
                continue;

            CollectCharacterPartRectsFromSpriteList(spriteList, desiredPartRectByGuid);
        }

        return SyncCharacterPartRectsToSpriteImportData(importer, desiredPartRectByGuid);
    }

    public static bool SyncPsdLayerMosaicPositionsFromCurrentSpriteImportData(SerializedObject importer)
    {
        if (importer == null)
            return false;

        var atlasPositionByGuid = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        var spriteLists = ReanimatorSpriteListResolver.ResolveSpriteLists(importer);
        for (var listIndex = 0; listIndex < spriteLists.Count; listIndex++)
        {
            var spriteList = spriteLists[listIndex];
            if (spriteList == null || !spriteList.isArray)
                continue;

            for (var i = 0; i < spriteList.arraySize; i++)
            {
                var spriteElement = spriteList.GetArrayElementAtIndex(i);
                if (spriteElement == null)
                    continue;

                var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
                if (string.IsNullOrEmpty(guid))
                    continue;

                var rect = ReadRectProperty(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"), Rect.zero);
                if (rect.width <= 0f || rect.height <= 0f)
                    continue;

                atlasPositionByGuid[guid] = rect.position;
            }
        }

        return SyncPsdLayerMosaicPositionsToSpriteImportData(importer, atlasPositionByGuid);
    }

    public static void CollectCharacterPartRectsFromSpriteList(
        SerializedProperty spriteList,
        Dictionary<string, Rect> desiredPartRectByGuid)
    {
        if (spriteList == null || !spriteList.isArray || desiredPartRectByGuid == null)
            return;

        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var spriteElement = spriteList.GetArrayElementAtIndex(i);
            if (spriteElement == null)
                continue;

            var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
            if (string.IsNullOrEmpty(guid))
                continue;

            var rect = ReadRectProperty(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"), Rect.zero);
            if (rect.width <= 0f || rect.height <= 0f)
                continue;

            var positionProp = spriteElement.FindPropertyRelative("spritePosition") ?? spriteElement.FindPropertyRelative("m_SpritePosition");
            var spritePosition = positionProp != null ? GetVector2FromProperty(positionProp) : Vector2.zero;
            var partRect = ReanimatorAssetChangeAlgorithms.BuildCharacterPartSpritePosition(spritePosition, rect);
            desiredPartRectByGuid[guid] = new Rect(partRect.x, partRect.y, partRect.width, partRect.height);
        }
    }

    public static bool SyncPsdLayerMosaicPositionsToSpriteImportData(
        SerializedObject importer,
        Dictionary<string, Vector2> desiredAtlasPositionByGuid)
    {
        if (importer == null || desiredAtlasPositionByGuid == null || desiredAtlasPositionByGuid.Count == 0)
            return false;

        var changed = false;
        changed |= SyncPsdLayerMosaicPositionListToSpriteImportData(importer.FindProperty("m_RigPSDLayers"), desiredAtlasPositionByGuid);
        changed |= SyncPsdLayerMosaicPositionListToSpriteImportData(importer.FindProperty("rigPSDLayers"), desiredAtlasPositionByGuid);
        changed |= SyncPsdLayerMosaicPositionListToSpriteImportData(importer.FindProperty("m_SharedRigPSDLayers"), desiredAtlasPositionByGuid);
        changed |= SyncPsdLayerMosaicPositionListToSpriteImportData(importer.FindProperty("sharedRigPSDLayers"), desiredAtlasPositionByGuid);
        changed |= SyncPsdLayerMosaicPositionListToSpriteImportData(importer.FindProperty("m_MosaicPSDLayers"), desiredAtlasPositionByGuid);
        changed |= SyncPsdLayerMosaicPositionListToSpriteImportData(importer.FindProperty("mosaicPSDLayers"), desiredAtlasPositionByGuid);
        return changed;
    }

    public static bool SyncCharacterPartRectsToSpriteImportData(
        SerializedObject importer,
        Dictionary<string, Rect> desiredPartRectByGuid)
    {
        if (importer == null || desiredPartRectByGuid == null || desiredPartRectByGuid.Count == 0)
            return false;

        var changed = false;
        for (var rootIndex = 0; rootIndex < CharacterDataRootNames.Length; rootIndex++)
        {
            var characterData = importer.FindProperty(CharacterDataRootNames[rootIndex]);
            if (characterData == null)
                continue;

            var partsProp = characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts");
            if (partsProp == null || !partsProp.isArray)
                continue;

            var handledGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < partsProp.arraySize; i++)
            {
                var part = partsProp.GetArrayElementAtIndex(i);
                if (part == null)
                    continue;

                var guid = GetGuidStringProperty(part, "spriteId", "m_SpriteId", "spriteID", "m_SpriteID");
                if (string.IsNullOrEmpty(guid) || !desiredPartRectByGuid.TryGetValue(guid, out var rect))
                    continue;

                handledGuids.Add(guid);
                changed |= WriteRectIntIfChanged(
                    part.FindPropertyRelative("spritePosition") ?? part.FindPropertyRelative("m_SpritePosition"),
                    rect.position,
                    rect.size);
            }

            foreach (var pair in desiredPartRectByGuid)
            {
                var guid = NormalizeGuidString(pair.Key);
                if (string.IsNullOrEmpty(guid) || handledGuids.Contains(guid))
                    continue;

                var rect = pair.Value;
                var index = partsProp.arraySize;
                partsProp.InsertArrayElementAtIndex(index);
                var part = partsProp.GetArrayElementAtIndex(index);
                if (part == null)
                    continue;

                changed |= WriteStringIfChanged(part.FindPropertyRelative("spriteId") ?? part.FindPropertyRelative("m_SpriteId"), guid);
                changed |= WriteRectIntIfChanged(
                    part.FindPropertyRelative("spritePosition") ?? part.FindPropertyRelative("m_SpritePosition"),
                    rect.position,
                    rect.size);
                var bonesProp = part.FindPropertyRelative("bones") ?? part.FindPropertyRelative("m_Bones");
                if (bonesProp != null && bonesProp.isArray && bonesProp.arraySize != 0)
                {
                    bonesProp.ClearArray();
                    changed = true;
                }
                changed |= WriteIntIfChanged(part.FindPropertyRelative("parentGroup") ?? part.FindPropertyRelative("m_ParentGroup"), -1);
                changed |= WriteIntIfChanged(part.FindPropertyRelative("order") ?? part.FindPropertyRelative("m_Order"), index);
                handledGuids.Add(guid);
            }
        }

        return changed;
    }

    public static bool ClearPsdLayerResliceRequest(SerializedObject importer)
    {
        if (importer == null)
            return false;

        var changed = false;
        changed |= WriteBoolOrIntIfChanged(importer.FindProperty("m_ResliceFromLayer") ?? importer.FindProperty("resliceFromLayer"), false);
        changed |= WriteBoolOrIntIfChanged(importer.FindProperty("m_SpriteSizeExpandChanged") ?? importer.FindProperty("spriteSizeExpandChanged"), false);
        return changed;
    }

    private static bool SyncPsdLayerMosaicPositionListToSpriteImportData(
        SerializedProperty psdLayerList,
        Dictionary<string, Vector2> desiredAtlasPositionByGuid)
    {
        if (psdLayerList == null || !psdLayerList.isArray)
            return false;

        var changed = false;
        for (var i = 0; i < psdLayerList.arraySize; i++)
        {
            var psdLayerElement = psdLayerList.GetArrayElementAtIndex(i);
            if (psdLayerElement == null)
                continue;

            var guid = GetGuidStringProperty(psdLayerElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
            if (string.IsNullOrEmpty(guid) || !desiredAtlasPositionByGuid.TryGetValue(guid, out var desiredAtlasPosition))
                continue;

            changed |= WriteVector2IfChanged(
                psdLayerElement.FindPropertyRelative("m_MosaicPosition") ?? psdLayerElement.FindPropertyRelative("mosaicPosition"),
                desiredAtlasPosition);
        }

        return changed;
    }

    private static void ApplyImporterSerializedProperties(SerializedObject serializedObject, UnityEngine.Object importer)
    {
        if (serializedObject == null)
            return;

        serializedObject.ApplyModifiedProperties();
        if (importer != null)
            EditorUtility.SetDirty(importer);
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

    private static Vector2 GetVector2FromProperty(SerializedProperty property)
    {
        if (property == null)
            return Vector2.zero;
        if (property.propertyType == SerializedPropertyType.Vector2)
            return property.vector2Value;
        if (property.propertyType == SerializedPropertyType.Vector2Int)
        {
            var value = property.vector2IntValue;
            return new Vector2(value.x, value.y);
        }

        return new Vector2(
            GetFloatProperty(property, 0f, "x", "m_X"),
            GetFloatProperty(property, 0f, "y", "m_Y"));
    }

    private static string GetGuidStringProperty(SerializedProperty parent, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return string.Empty;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var prop = parent.FindPropertyRelative(relativeNames[i]);
            if (prop == null)
                continue;
            if (prop.propertyType == SerializedPropertyType.String)
                return NormalizeGuidString(prop.stringValue);
            if (prop.propertyType == SerializedPropertyType.Generic)
            {
                var guidProp = prop.FindPropertyRelative("m_GuidString")
                               ?? prop.FindPropertyRelative("guid")
                               ?? prop.FindPropertyRelative("m_Guid");
                if (guidProp != null && guidProp.propertyType == SerializedPropertyType.String)
                    return NormalizeGuidString(guidProp.stringValue);
            }
        }

        return string.Empty;
    }

    private static string GetStringProperty(SerializedProperty parent, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return string.Empty;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var prop = parent.FindPropertyRelative(relativeNames[i]);
            if (prop != null && prop.propertyType == SerializedPropertyType.String)
                return prop.stringValue ?? string.Empty;
        }

        return string.Empty;
    }

    private static int GetIntProperty(SerializedProperty parent, int defaultValue, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return defaultValue;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var prop = parent.FindPropertyRelative(relativeNames[i]);
            if (prop == null)
                continue;
            if (prop.propertyType == SerializedPropertyType.Integer)
                return prop.intValue;
            if (prop.propertyType == SerializedPropertyType.Float)
                return Mathf.RoundToInt(prop.floatValue);
        }

        return defaultValue;
    }

    private static float GetFloatProperty(SerializedProperty parent, float defaultValue, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return defaultValue;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var prop = parent.FindPropertyRelative(relativeNames[i]);
            if (prop == null)
                continue;
            if (prop.propertyType == SerializedPropertyType.Float)
                return prop.floatValue;
            if (prop.propertyType == SerializedPropertyType.Integer)
                return prop.intValue;
        }

        return defaultValue;
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

    private static bool WriteVector2IfChanged(SerializedProperty property, Vector2 value)
    {
        return ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(property, value);
    }

    private static bool WriteIntIfChanged(SerializedProperty property, int value)
    {
        return ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(property, value);
    }

    private static bool WriteStringIfChanged(SerializedProperty property, string value)
    {
        return ReanimatorSerializedPropertyAlgorithms.WriteStringIfChanged(property, value);
    }

    private static string NormalizeGuidString(string value)
    {
        return ReanimatorRebuildMetaAlgorithms.NormalizeGuidString(value);
    }
}
#endif
