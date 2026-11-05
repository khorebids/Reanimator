#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

internal static partial class ReanimatorSelfTests
{
    private static PsdLayerPreviewRenderer.LayerInfo FindLayer(PsdLayerPreviewRenderer renderer, string name)
    {
        if (renderer == null)
            return null;

        var layers = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos");
        return layers != null
            ? layers.FirstOrDefault(layer => layer != null && string.Equals(layer.name, name, StringComparison.Ordinal))
            : null;
    }

    private static void AssertHeadImportState(
        string assetPath,
        Vector2 expectedUvTransform,
        Rect expectedRect,
        Vector2 stableSpritePosition,
        Vector2 expectedVisualAnchor,
        string operation)
    {
        AssertTrue(TryReadHeadSpriteImportVector(assetPath, "uvTransform", out var uvTransform), $"{operation} should keep the serialized head uvTransform readable.");
        AssertTrue(TryReadHeadSpriteImportVector(assetPath, "spritePosition", out var spritePosition), $"{operation} should keep the serialized head spritePosition readable.");
        AssertTrue(TryReadHeadCharacterPartRect(assetPath, out var characterPartRect), $"{operation} should keep the head character part readable.");
        AssertVector(expectedUvTransform, uvTransform, 0.0001f, $"{operation} should sync head uvTransform to the serialized rect position.");
        AssertVector(stableSpritePosition, spritePosition, 0.0001f, $"{operation} should persist the expected serialized spritePosition.");
        AssertEqual(
            new RectInt(
                Mathf.RoundToInt(stableSpritePosition.x),
                Mathf.RoundToInt(stableSpritePosition.y),
                Mathf.RoundToInt(expectedRect.width),
                Mathf.RoundToInt(expectedRect.height)),
            characterPartRect,
            $"{operation} should sync the old Skinning Editor character part rect.");
        AssertVector(
            expectedVisualAnchor,
            PsdCoordinateSpace.ResolveSpriteVisualAnchor(spritePosition, expectedRect, uvTransform),
            0.0001f,
            $"{operation} should preserve the visual source-image anchor.");
    }

    private static bool TryReadHeadSpriteImportVector(string assetPath, string propertyName, out Vector2 value)
    {
        var importer = AssetImporter.GetAtPath(assetPath);
        return TryFindSerializedVector2(importer, "m_RigSpriteImportData", "head", propertyName, out value) ||
               TryFindSerializedVector2(importer, "rigSpriteImportData", "head", propertyName, out value);
    }

    private static bool TryReadHeadCharacterPartRect(string assetPath, out RectInt rect)
    {
        rect = default;
        var importer = AssetImporter.GetAtPath(assetPath);
        return TryFindSerializedCharacterPartRect(importer, "m_CharacterData", "0ea0f407f5c52714a93f836f812049a0", out rect) ||
               TryFindSerializedCharacterPartRect(importer, "characterData", "0ea0f407f5c52714a93f836f812049a0", out rect) ||
               TryFindSerializedCharacterPartRect(importer, "m_SharedRigCharacterData", "0ea0f407f5c52714a93f836f812049a0", out rect) ||
               TryFindSerializedCharacterPartRect(importer, "sharedRigCharacterData", "0ea0f407f5c52714a93f836f812049a0", out rect);
    }

    private static SpriteRect FindSpriteRect(ISpriteEditorDataProvider dataProvider, string name)
    {
        if (dataProvider == null)
            return null;

        dataProvider.InitSpriteEditorDataProvider();
        var spriteRects = dataProvider.GetSpriteRects();
        if (spriteRects == null || spriteRects.Length == 0)
            return null;

        for (var i = 0; i < spriteRects.Length; i++)
        {
            var spriteRect = spriteRects[i];
            if (spriteRect != null && string.Equals(spriteRect.name, name, StringComparison.Ordinal))
                return spriteRect;
        }

        return null;
    }

    private static string DescribeHeadImporterState(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath);
        var dataProviderRect = Rect.zero;
        var hasDataProviderRect = false;
        if (importer is ISpriteEditorDataProvider dataProvider)
        {
            dataProvider.InitSpriteEditorDataProvider();
            var spriteRect = FindSpriteRect(dataProvider, "head");
            if (spriteRect != null)
            {
                dataProviderRect = spriteRect.rect;
                hasDataProviderRect = true;
            }
        }

        var rigRect = Rect.zero;
        var hasRigRect = TryFindSerializedSpriteRect(importer, "m_RigSpriteImportData", "head", out rigRect) ||
                         TryFindSerializedSpriteRect(importer, "rigSpriteImportData", "head", out rigRect);
        var rigPosition = Vector2.zero;
        var hasRigPosition = TryFindSerializedVector2(importer, "m_RigSpriteImportData", "head", "spritePosition", out rigPosition) ||
                             TryFindSerializedVector2(importer, "rigSpriteImportData", "head", "spritePosition", out rigPosition);
        var characterPartRect = default(RectInt);
        var hasCharacterPartRect = TryReadHeadCharacterPartRect(assetPath, out characterPartRect);
        var mosaicPosition = Vector2.zero;
        var hasMosaicPosition = TryFindSerializedVector2(importer, "m_RigPSDLayers", "head", "mosaicPosition", out mosaicPosition) ||
                                TryFindSerializedVector2(importer, "rigPSDLayers", "head", "mosaicPosition", out mosaicPosition);
        return $"providerRect={(hasDataProviderRect ? FormatRect(dataProviderRect) : "<none>")}, rigRect={(hasRigRect ? FormatRect(rigRect) : "<none>")}, rigPosition={(hasRigPosition ? FormatVector2(rigPosition) : "<none>")}, characterPartRect={(hasCharacterPartRect ? characterPartRect.ToString() : "<none>")}, mosaicPosition={(hasMosaicPosition ? FormatVector2(mosaicPosition) : "<none>")}";
    }

    private static bool TryFindSerializedSpriteRect(AssetImporter importer, string arrayName, string spriteName, out Rect rect)
    {
        rect = Rect.zero;
        if (importer == null)
            return false;

        var so = new SerializedObject(importer);
        var array = so.FindProperty(arrayName);
        if (array == null || !array.isArray)
            return false;

        for (var i = 0; i < array.arraySize; i++)
        {
            var element = array.GetArrayElementAtIndex(i);
            var nameProp = element.FindPropertyRelative("name") ?? element.FindPropertyRelative("m_Name");
            if (nameProp == null || !string.Equals(nameProp.stringValue, spriteName, StringComparison.Ordinal))
                continue;

            var rectProp = element.FindPropertyRelative("rect") ?? element.FindPropertyRelative("m_Rect");
            if (rectProp == null)
                return false;

            rect = rectProp.propertyType == SerializedPropertyType.Rect
                ? rectProp.rectValue
                : new Rect(
                    ReadSerializedNumber(rectProp.FindPropertyRelative("x")),
                    ReadSerializedNumber(rectProp.FindPropertyRelative("y")),
                    ReadSerializedNumber(rectProp.FindPropertyRelative("width")),
                    ReadSerializedNumber(rectProp.FindPropertyRelative("height")));
            return true;
        }

        return false;
    }

    private static bool TryFindSerializedVector2(AssetImporter importer, string arrayName, string spriteName, string propertyName, out Vector2 value)
    {
        value = Vector2.zero;
        if (importer == null)
            return false;

        var so = new SerializedObject(importer);
        var array = so.FindProperty(arrayName);
        if (array == null || !array.isArray)
            return false;

        for (var i = 0; i < array.arraySize; i++)
        {
            var element = array.GetArrayElementAtIndex(i);
            var nameProp = element.FindPropertyRelative("name") ?? element.FindPropertyRelative("m_Name");
            if (nameProp == null || !string.Equals(nameProp.stringValue, spriteName, StringComparison.Ordinal))
                continue;

            var valueProp = element.FindPropertyRelative(propertyName);
            if (valueProp == null)
                return false;

            value = valueProp.propertyType == SerializedPropertyType.Vector2
                ? valueProp.vector2Value
                : new Vector2(
                    ReadSerializedNumber(valueProp.FindPropertyRelative("x")),
                    ReadSerializedNumber(valueProp.FindPropertyRelative("y")));
            return true;
        }

        return false;
    }

    private static bool TryFindSerializedCharacterPartRect(AssetImporter importer, string characterDataName, string spriteId, out RectInt rect)
    {
        rect = default;
        if (importer == null)
            return false;

        var so = new SerializedObject(importer);
        var characterData = so.FindProperty(characterDataName);
        if (characterData == null)
            return false;

        var parts = characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts");
        if (parts == null || !parts.isArray)
            return false;

        for (var i = 0; i < parts.arraySize; i++)
        {
            var part = parts.GetArrayElementAtIndex(i);
            var idProp = part.FindPropertyRelative("spriteId") ?? part.FindPropertyRelative("m_SpriteId") ??
                         part.FindPropertyRelative("spriteID") ?? part.FindPropertyRelative("m_SpriteID");
            if (idProp == null || !string.Equals(idProp.stringValue, spriteId, StringComparison.OrdinalIgnoreCase))
                continue;

            var rectProp = part.FindPropertyRelative("spritePosition") ?? part.FindPropertyRelative("m_SpritePosition");
            if (rectProp == null)
                return false;

            rect = ReadSerializedRectInt(rectProp);
            return true;
        }

        return false;
    }

    private static RectInt ReadSerializedRectInt(SerializedProperty property)
    {
        if (property == null)
            return default;

        if (property.propertyType == SerializedPropertyType.RectInt)
            return property.rectIntValue;

        return new RectInt(
            Mathf.RoundToInt(ReadSerializedNumber(property.FindPropertyRelative("x"))),
            Mathf.RoundToInt(ReadSerializedNumber(property.FindPropertyRelative("y"))),
            Mathf.RoundToInt(ReadSerializedNumber(property.FindPropertyRelative("width"))),
            Mathf.RoundToInt(ReadSerializedNumber(property.FindPropertyRelative("height"))));
    }

    private static float ReadSerializedNumber(SerializedProperty property)
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

    private static string FormatRect(Rect rect)
    {
        return $"({rect.x},{rect.y},{rect.width},{rect.height})";
    }

    private static string FormatVector2(Vector2 value)
    {
        return $"({value.x},{value.y})";
    }

    private static void SpriteSheetDefaultGeometryKeysSnapshotsBySpriteGuid()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(128, 128);
        try
        {
            var selected = BuildLayer("duplicate-default", 77, new Vector2(10f, 20f));
            selected.spriteGuid = "selected-sprite-guid";
            selected.sourceRect = new Rect(5f, 6f, 30f, 40f);
            selected.textureRect = selected.sourceRect;
            selected.texture = texture;

            var other = BuildLayer("duplicate-default", 77, new Vector2(90f, 100f));
            other.spriteGuid = "other-sprite-guid";
            other.sourceRect = new Rect(50f, 60f, 20f, 20f);
            other.textureRect = other.sourceRect;
            other.texture = texture;

            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected, other });

            var selectedDefaultLocal = new[]
            {
                new Vector2(3f, 4f),
                new Vector2(20f, 8f),
                new Vector2(15f, 35f)
            };
            var selectedDefaultVertices = new Vector2[selectedDefaultLocal.Length];
            for (var i = 0; i < selectedDefaultLocal.Length; i++)
                selectedDefaultVertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(selected, selectedDefaultLocal[i]);

            var selectedData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(selectedData, "layer", selected);
            SetPrivateField(selectedData, "texture", texture);
            SetPrivateField(selectedData, "vertices", selectedDefaultVertices);
            SetPrivateField(selectedData, "vertexUvs", new[]
            {
                new Vector2(8f / 128f, 10f / 128f),
                new Vector2(25f / 128f, 14f / 128f),
                new Vector2(20f / 128f, 41f / 128f)
            });
            SetPrivateField(selectedData, "triangles", new[] { 0, 1, 2 });
            SetPrivateField(selectedData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
            SetPrivateField(selectedData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
            SetPrivateField(selectedData, "boneWeights", Array.Empty<BoneWeight>());

            var otherData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(otherData, "layer", other);
            SetPrivateField(otherData, "texture", texture);
            SetPrivateField(otherData, "vertices", new[]
            {
                PsdCoordinateSpace.LayerLocalToDocumentPoint(other, new Vector2(1f, 1f)),
                PsdCoordinateSpace.LayerLocalToDocumentPoint(other, new Vector2(18f, 1f)),
                PsdCoordinateSpace.LayerLocalToDocumentPoint(other, new Vector2(18f, 18f)),
                PsdCoordinateSpace.LayerLocalToDocumentPoint(other, new Vector2(1f, 18f))
            });
            SetPrivateField(otherData, "vertexUvs", new[]
            {
                new Vector2(51f / 128f, 61f / 128f),
                new Vector2(68f / 128f, 61f / 128f),
                new Vector2(68f / 128f, 78f / 128f),
                new Vector2(51f / 128f, 78f / 128f)
            });
            SetPrivateField(otherData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
            SetPrivateField(otherData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
            SetPrivateField(otherData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
            SetPrivateField(otherData, "boneWeights", Array.Empty<BoneWeight>());

            InvokePrivateMethod(renderer, "StoreDefaultGeometrySnapshot", other, otherData);
            InvokePrivateMethod(renderer, "StoreDefaultGeometrySnapshot", selected, selectedData);

            AssertTrue(renderer.SelectLayer(selected), "Selected duplicate layer should become selected.");
            var selectedSelectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
            var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
            dataBySelectionKey[selectedSelectionKey] = selectedData;

            SetPrivateField(selectedData, "vertices", new[] { PsdCoordinateSpace.LayerLocalToDocumentPoint(selected, new Vector2(12f, 12f)) });
            SetPrivateField(selectedData, "vertexUvs", new[] { new Vector2(0.1f, 0.1f) });
            SetPrivateField(selectedData, "triangles", Array.Empty<int>());
            SetPrivateField(selectedData, "allEdgePairs", Array.Empty<int>());
            SetPrivateField(selectedData, "boundaryEdgePairs", Array.Empty<int>());

            AssertTrue(
                renderer.GenerateDefaultGeometryForSelected("Assets/test.psd", out var error),
                $"Default geometry should generate from selected sprite guid. Error={error}");

            var restoredVertices = GetPrivateField<Vector2[]>(selectedData, "vertices");
            var restoredTriangles = GetPrivateField<int[]>(selectedData, "triangles");
            AssertEqual(selectedDefaultVertices.Length, restoredVertices.Length, "Selected snapshot vertex count should win over duplicate stable key.");
            for (var i = 0; i < selectedDefaultVertices.Length; i++)
                AssertVector(selectedDefaultVertices[i], restoredVertices[i], 0.0001f, $"Selected snapshot vertex {i}.");
            AssertArray(new[] { 0, 1, 2 }, restoredTriangles, "Selected snapshot triangles should win over duplicate stable key.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
#endif
