#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;

internal static class ReanimatorSerializedSpriteMeshReader
{
    public static Dictionary<string, PsdLayerPreviewRenderer.SerializedSpriteMeshData> BuildBySelectionKey(
        PSDImporter importer,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers,
        Func<PsdLayerPreviewRenderer.LayerInfo, string> buildSelectionKey)
    {
        var result = new Dictionary<string, PsdLayerPreviewRenderer.SerializedSpriteMeshData>(StringComparer.OrdinalIgnoreCase);
        if (importer == null || layers == null || layers.Count == 0 || buildSelectionKey == null)
            return result;

        var so = new SerializedObject(importer);
        var spriteList = ReanimatorSpriteListResolver.ResolveSpriteList(so);
        if (spriteList == null || !spriteList.isArray)
            return result;

        var layerByGuid = BuildLayerIndexByGuid(layers);
        var layerIndex = 0;
        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var spriteElement = spriteList.GetArrayElementAtIndex(i);
            if (spriteElement == null)
                continue;

            var layer = ResolveLayer(spriteElement, layers, layerByGuid, layerIndex);
            layerIndex++;
            if (layer == null)
                continue;

            var vertices = ReadSerializedMeshVertices(spriteElement.FindPropertyRelative("vertices") ?? spriteElement.FindPropertyRelative("m_Vertices"));
            if (vertices == null || vertices.Length == 0)
                continue;

            var key = buildSelectionKey(layer);
            if (string.IsNullOrEmpty(key))
                continue;

            result[key] = new PsdLayerPreviewRenderer.SerializedSpriteMeshData
            {
                vertices = vertices,
                indices = ReadSerializedMeshIndices(spriteElement.FindPropertyRelative("indices") ?? spriteElement.FindPropertyRelative("m_Indices")),
                edges = ReadSerializedMeshEdges(spriteElement.FindPropertyRelative("edges") ?? spriteElement.FindPropertyRelative("m_Edges"))
            };
        }

        return result;
    }

    private static Dictionary<string, PsdLayerPreviewRenderer.LayerInfo> BuildLayerIndexByGuid(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers)
    {
        var result = new Dictionary<string, PsdLayerPreviewRenderer.LayerInfo>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer == null || string.IsNullOrEmpty(layer.spriteGuid))
                continue;
            var guid = NormalizeGuidString(layer.spriteGuid);
            if (!string.IsNullOrEmpty(guid))
                result[guid] = layer;
        }

        return result;
    }

    private static PsdLayerPreviewRenderer.LayerInfo ResolveLayer(
        SerializedProperty spriteElement,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers,
        IReadOnlyDictionary<string, PsdLayerPreviewRenderer.LayerInfo> layerByGuid,
        int layerIndex)
    {
        var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
        if (!string.IsNullOrEmpty(guid) && layerByGuid != null && layerByGuid.TryGetValue(guid, out var layer))
            return layer;

        return layerIndex >= 0 && layerIndex < layers.Count ? layers[layerIndex] : null;
    }

    private static Vertex2DMetaData[] ReadSerializedMeshVertices(SerializedProperty verticesProp)
    {
        if (verticesProp == null || !verticesProp.isArray || verticesProp.arraySize <= 0)
            return Array.Empty<Vertex2DMetaData>();

        var result = new Vertex2DMetaData[verticesProp.arraySize];
        for (var i = 0; i < verticesProp.arraySize; i++)
        {
            var vertexProp = verticesProp.GetArrayElementAtIndex(i);
            if (vertexProp == null)
                continue;

            result[i].position = GetVector2FromProperty(vertexProp.FindPropertyRelative("position") ?? vertexProp.FindPropertyRelative("m_Position"));
            result[i].boneWeight = ReadSerializedBoneWeight(vertexProp.FindPropertyRelative("boneWeight") ?? vertexProp.FindPropertyRelative("m_BoneWeight"));
        }

        return result;
    }

    private static BoneWeight ReadSerializedBoneWeight(SerializedProperty boneWeightProp)
    {
        if (boneWeightProp == null)
            return default;

        return new BoneWeight
        {
            weight0 = GetFloatProperty(boneWeightProp, 0f, "weight0", "m_Weight0"),
            weight1 = GetFloatProperty(boneWeightProp, 0f, "weight1", "m_Weight1"),
            weight2 = GetFloatProperty(boneWeightProp, 0f, "weight2", "m_Weight2"),
            weight3 = GetFloatProperty(boneWeightProp, 0f, "weight3", "m_Weight3"),
            boneIndex0 = GetIntProperty(boneWeightProp, 0, "boneIndex0", "m_BoneIndex0"),
            boneIndex1 = GetIntProperty(boneWeightProp, 0, "boneIndex1", "m_BoneIndex1"),
            boneIndex2 = GetIntProperty(boneWeightProp, 0, "boneIndex2", "m_BoneIndex2"),
            boneIndex3 = GetIntProperty(boneWeightProp, 0, "boneIndex3", "m_BoneIndex3")
        };
    }

    private static int[] ReadSerializedMeshIndices(SerializedProperty indicesProp)
    {
        if (indicesProp == null)
            return Array.Empty<int>();

        if (indicesProp.propertyType == SerializedPropertyType.String)
            return DecodeLittleEndianIntHex(indicesProp.stringValue);

        if (indicesProp.isArray)
        {
            var result = new int[indicesProp.arraySize];
            for (var i = 0; i < indicesProp.arraySize; i++)
                result[i] = indicesProp.GetArrayElementAtIndex(i).intValue;
            return result;
        }

        return Array.Empty<int>();
    }

    private static int[] DecodeLittleEndianIntHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<int>();

        hex = hex.Trim();
        var count = hex.Length / 8;
        if (count <= 0)
            return Array.Empty<int>();

        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * 8;
            var b0 = ParseHexByte(hex, offset);
            var b1 = ParseHexByte(hex, offset + 2);
            var b2 = ParseHexByte(hex, offset + 4);
            var b3 = ParseHexByte(hex, offset + 6);
            result[i] = b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }

        return result;
    }

    private static int ParseHexByte(string hex, int offset)
    {
        if (string.IsNullOrEmpty(hex) || offset < 0 || offset + 2 > hex.Length)
            return 0;
        return int.TryParse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static Vector2Int[] ReadSerializedMeshEdges(SerializedProperty edgesProp)
    {
        if (edgesProp == null || !edgesProp.isArray || edgesProp.arraySize <= 0)
            return Array.Empty<Vector2Int>();

        var result = new Vector2Int[edgesProp.arraySize];
        for (var i = 0; i < edgesProp.arraySize; i++)
        {
            var edgeProp = edgesProp.GetArrayElementAtIndex(i);
            if (edgeProp == null)
                continue;

            var xProp = edgeProp.FindPropertyRelative("x");
            var yProp = edgeProp.FindPropertyRelative("y");
            result[i] = new Vector2Int(
                xProp != null ? xProp.intValue : 0,
                yProp != null ? yProp.intValue : 0);
        }

        return result;
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

    private static string NormalizeGuidString(string value)
    {
        return ReanimatorRebuildMetaAlgorithms.NormalizeGuidString(value);
    }
}
#endif
