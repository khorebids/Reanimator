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
    private void BuildLayerHierarchy(
        IReadOnlyList<PsdLayerDataInfo> layerData,
        Dictionary<string, bool> previousHierarchyVisibility,
        bool reuseVisibility)
    {
        layerHierarchyNodes.Clear();
        if (layerData == null || layerData.Count == 0)
        {
            BuildFlatLayerHierarchy(previousHierarchyVisibility, reuseVisibility);
            return;
        }

        var layerIndexByDataIndex = new Dictionary<int, int>();
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer != null && layer.layerDataIndex >= 0 && !layerIndexByDataIndex.ContainsKey(layer.layerDataIndex))
                layerIndexByDataIndex[layer.layerDataIndex] = i;
        }

        var dataKeyByIndex = new Dictionary<int, string>();
        for (var i = 0; i < layerData.Count; i++)
        {
            var data = layerData[i];
            if (data.isGroup || layerIndexByDataIndex.ContainsKey(data.index))
                dataKeyByIndex[data.index] = BuildLayerHierarchyKey(data);
        }

        for (var i = 0; i < layerData.Count; i++)
        {
            var data = layerData[i];
            var isLayer = layerIndexByDataIndex.TryGetValue(data.index, out var layerIndex);
            if (!data.isGroup && !isLayer)
                continue;

            var key = BuildLayerHierarchyKey(data);
            var parentKey = data.parentIndex >= 0 && dataKeyByIndex.TryGetValue(data.parentIndex, out var resolvedParentKey)
                ? resolvedParentKey
                : string.Empty;
            var visible = data.isGroup ? data.isVisible : layerInfos[layerIndex].visible;
            if (reuseVisibility && previousHierarchyVisibility != null && previousHierarchyVisibility.TryGetValue(key, out var previousVisible))
                visible = previousVisible;

            if (isLayer)
                layerInfos[layerIndex].visible = visible && AreLayerHierarchyAncestorsVisible(data.parentIndex, layerData, previousHierarchyVisibility, reuseVisibility);

            layerHierarchyNodes.Add(new LayerHierarchyNode
            {
                key = key,
                parentKey = parentKey,
                name = string.IsNullOrEmpty(data.name) ? (data.isGroup ? $"Group {data.index}" : $"Layer {data.index}") : data.name,
                isGroup = data.isGroup,
                visible = visible,
                layerIndex = isLayer ? layerIndex : -1
            });
        }

        if (layerHierarchyNodes.Count == 0)
            BuildFlatLayerHierarchy(previousHierarchyVisibility, reuseVisibility);
    }

    private void BuildFlatLayerHierarchy(Dictionary<string, bool> previousHierarchyVisibility, bool reuseVisibility)
    {
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;

            var key = BuildLayerHierarchyKey(layer);
            var visible = layer.visible;
            if (reuseVisibility && previousHierarchyVisibility != null && previousHierarchyVisibility.TryGetValue(key, out var previousVisible))
                visible = previousVisible;
            layer.visible = visible;

            layerHierarchyNodes.Add(new LayerHierarchyNode
            {
                key = key,
                parentKey = string.Empty,
                name = string.IsNullOrEmpty(layer.name) ? $"Layer {i}" : layer.name,
                isGroup = false,
                visible = visible,
                layerIndex = i
            });
        }
    }

    private static bool TryDequeueLayerData(
        Dictionary<string, Queue<PsdLayerDataInfo>> layerByName,
        string name,
        out PsdLayerDataInfo data)
    {
        data = default;
        if (layerByName == null || string.IsNullOrEmpty(name))
            return false;
        if (!layerByName.TryGetValue(name, out var queue) || queue == null || queue.Count == 0)
            return false;

        data = queue.Dequeue();
        return true;
    }

    private sealed class PsdLayerDataLookup
    {
        public readonly Dictionary<int, Queue<PsdLayerDataInfo>> byLayerId = new Dictionary<int, Queue<PsdLayerDataInfo>>();
        public readonly Dictionary<string, Queue<PsdLayerDataInfo>> byName = new Dictionary<string, Queue<PsdLayerDataInfo>>(StringComparer.OrdinalIgnoreCase);
    }

    private static PsdLayerDataLookup BuildPsdLayerDataLookup(IReadOnlyList<PsdLayerDataInfo> layerData)
    {
        var lookup = new PsdLayerDataLookup();
        if (layerData == null)
            return lookup;

        for (var i = 0; i < layerData.Count; i++)
        {
            var data = layerData[i];
            if (data.isGroup)
                continue;

            if (data.layerId != 0)
            {
                if (!lookup.byLayerId.TryGetValue(data.layerId, out var byIdQueue))
                {
                    byIdQueue = new Queue<PsdLayerDataInfo>();
                    lookup.byLayerId[data.layerId] = byIdQueue;
                }
                byIdQueue.Enqueue(data);
            }

            if (!string.IsNullOrEmpty(data.name))
            {
                if (!lookup.byName.TryGetValue(data.name, out var byNameQueue))
                {
                    byNameQueue = new Queue<PsdLayerDataInfo>();
                    lookup.byName[data.name] = byNameQueue;
                }
                byNameQueue.Enqueue(data);
            }
        }

        return lookup;
    }

    private static bool TryResolveLayerData(
        PsdLayerDataLookup lookup,
        string name,
        int layerId,
        out PsdLayerDataInfo data)
    {
        data = default;
        if (lookup == null)
            return false;

        if (layerId != 0 &&
            lookup.byLayerId.TryGetValue(layerId, out var byIdQueue) &&
            byIdQueue != null &&
            byIdQueue.Count > 0)
        {
            data = byIdQueue.Dequeue();
            return true;
        }

        return TryDequeueLayerData(lookup.byName, name, out data);
    }

    private static int ReadLayerIdFromSpriteElement(SerializedProperty spriteElement)
    {
        var layerIdProp = spriteElement != null
            ? spriteElement.FindPropertyRelative("m_LayerID") ??
              spriteElement.FindPropertyRelative("layerID") ??
              spriteElement.FindPropertyRelative("layerId")
            : null;
        return layerIdProp != null && layerIdProp.propertyType == SerializedPropertyType.Integer
            ? layerIdProp.intValue
            : 0;
    }

    private static Dictionary<string, int> BuildLayerIdBySpriteGuid(SerializedObject importer)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (importer == null)
            return result;

        void CollectSpriteList(SerializedProperty spriteList)
        {
            if (spriteList == null || !spriteList.isArray)
                return;

            for (var i = 0; i < spriteList.arraySize; i++)
            {
                var element = spriteList.GetArrayElementAtIndex(i);
                if (element == null)
                    continue;

                var guid = NormalizeGuidString(GetGuidStringProperty(element, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId"));
                if (string.IsNullOrEmpty(guid) || result.ContainsKey(guid))
                    continue;

                var layerId = ReadLayerIdFromSpriteElement(element);
                if (layerId != 0)
                    result[guid] = layerId;
            }
        }

        var spriteLists = ResolveSpriteLists(importer);
        for (var i = 0; i < spriteLists.Count; i++)
            CollectSpriteList(spriteLists[i]);

        CollectSpriteList(importer.FindProperty("m_RigPSDLayers") ?? importer.FindProperty("rigPSDLayers"));
        CollectSpriteList(importer.FindProperty("m_SharedRigPSDLayers") ?? importer.FindProperty("sharedRigPSDLayers"));
        CollectSpriteList(importer.FindProperty("m_MosaicPSDLayers") ?? importer.FindProperty("mosaicPSDLayers"));
        CollectSpriteList(importer.FindProperty("psdLayers") ?? importer.FindProperty("m_PsdLayers"));
        return result;
    }

    private static string BuildLayerHierarchyKey(PsdLayerDataInfo data)
    {
        return ReanimatorRebuildMetaAlgorithms.BuildLayerHierarchyKey(data.isGroup, data.index, data.name, data.layerId);
    }

    private static string BuildLayerHierarchyKey(LayerInfo layer)
    {
        return layer == null ? string.Empty : ReanimatorRebuildMetaAlgorithms.BuildFlatLayerHierarchyKey(layer.name, layer.layerId);
    }

    private static bool AreLayerHierarchyAncestorsVisible(
        int parentIndex,
        IReadOnlyList<PsdLayerDataInfo> layerData,
        Dictionary<string, bool> previousHierarchyVisibility,
        bool reuseVisibility)
    {
        var guard = 0;
        while (parentIndex >= 0 && layerData != null && parentIndex < layerData.Count && guard++ < layerData.Count)
        {
            var parent = layerData[parentIndex];
            var visible = parent.isVisible;
            if (reuseVisibility && previousHierarchyVisibility != null &&
                previousHierarchyVisibility.TryGetValue(BuildLayerHierarchyKey(parent), out var previousVisible))
                visible = previousVisible;
            if (!visible)
                return false;
            parentIndex = parent.parentIndex;
        }

        return true;
    }

}

#endif
