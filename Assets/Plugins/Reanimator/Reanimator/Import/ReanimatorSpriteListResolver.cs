#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class ReanimatorSpriteListResolver
{
    public static SerializedProperty ResolveSpriteList(SerializedObject importer)
    {
        if (importer == null)
            return null;

        var mosaicProp = importer.FindProperty("m_MosaicLayers") ?? importer.FindProperty("mosaicLayers");
        var characterProp = importer.FindProperty("m_CharacterMode") ?? importer.FindProperty("characterMode");
        var skeletonProp = importer.FindProperty("m_SkeletonAssetReferenceID") ?? importer.FindProperty("skeletonAssetReferenceID");

        var mosaic = ReadSerializedBool(mosaicProp);
        var character = ReadSerializedBool(characterProp);
        var skeletonGuid = skeletonProp != null ? skeletonProp.stringValue : string.Empty;
        var hasSkeleton = !string.IsNullOrEmpty(skeletonGuid) && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(skeletonGuid));

        var candidates = new List<SerializedProperty>();
        var candidatePaths = new HashSet<string>(StringComparer.Ordinal);
        void AddCandidate(SerializedProperty property)
        {
            if (property == null || !property.isArray || !candidatePaths.Add(property.propertyPath))
                return;

            candidates.Add(property);
        }

        var sharedRig = TryGetArray(importer, "m_SharedRigSpriteImportData", "sharedRigSpriteImportData");
        var rig = TryGetArray(importer, "m_RigSpriteImportData", "rigSpriteImportData");
        var mosaicArray = TryGetArray(importer, "m_MosaicSpriteImportData", "mosaicSpriteImportData");
        var spriteArray = TryGetArray(importer, "m_SpriteImportData", "spriteImportData");
        var layeredArray = TryGetArray(importer, "m_LayeredSpriteImportData", "layeredSpriteImportData");

        AddCandidate(layeredArray);

        if (character)
        {
            if (hasSkeleton && sharedRig != null)
                AddCandidate(sharedRig);
            AddCandidate(rig);
            AddCandidate(sharedRig);
        }

        if (mosaic)
            AddCandidate(mosaicArray);

        AddCandidate(rig);
        AddCandidate(sharedRig);
        AddCandidate(mosaicArray);
        AddCandidate(spriteArray);

        for (var i = 0; i < candidates.Count; i++)
        {
            if (HasUsableSpriteImportEntries(candidates[i]))
                return candidates[i];
        }

        return candidates.Count > 0 ? candidates[0] : null;
    }

    public static List<SerializedProperty> ResolveSpriteLists(SerializedObject importer)
    {
        var result = new List<SerializedProperty>();
        if (importer == null)
            return result;

        var paths = new HashSet<string>(StringComparer.Ordinal);
        AddArray(importer, result, paths, "m_RigSpriteImportData", "rigSpriteImportData");
        AddArray(importer, result, paths, "m_SharedRigSpriteImportData", "sharedRigSpriteImportData");
        AddArray(importer, result, paths, "m_MosaicSpriteImportData", "mosaicSpriteImportData");
        AddArray(importer, result, paths, "m_SpriteImportData", "spriteImportData");
        AddArray(importer, result, paths, "m_LayeredSpriteImportData", "layeredSpriteImportData");

        if (result.Count == 0)
        {
            var spriteList = ResolveSpriteList(importer);
            if (spriteList != null && spriteList.isArray)
                result.Add(spriteList);
        }

        return result;
    }

    private static SerializedProperty TryGetArray(SerializedObject importer, params string[] names)
    {
        if (importer == null || names == null)
            return null;

        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            if (string.IsNullOrEmpty(name))
                continue;

            var prop = importer.FindProperty(name);
            if (prop != null && prop.isArray)
                return prop;
        }

        return null;
    }

    private static void AddArray(
        SerializedObject importer,
        List<SerializedProperty> result,
        HashSet<string> paths,
        params string[] names)
    {
        if (names == null)
            return;

        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            if (string.IsNullOrEmpty(name))
                continue;

            var prop = importer.FindProperty(name);
            if (prop == null || !prop.isArray || !paths.Add(prop.propertyPath))
                continue;

            result.Add(prop);
        }
    }

    private static bool HasUsableSpriteImportEntries(SerializedProperty spriteList)
    {
        if (spriteList == null || !spriteList.isArray)
            return false;

        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var element = spriteList.GetArrayElementAtIndex(i);
            if (element == null)
                continue;

            var rectProp = element.FindPropertyRelative("m_Rect") ?? element.FindPropertyRelative("rect");
            if (rectProp == null)
                continue;

            var rect = rectProp.rectValue;
            if (rect.width > 0f && rect.height > 0f)
                return true;
        }

        return false;
    }

    private static bool ReadSerializedBool(SerializedProperty property)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Boolean)
            return property.boolValue;
        if (property.propertyType == SerializedPropertyType.Integer)
            return property.intValue != 0;
        return false;
    }
}
#endif
