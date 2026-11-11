#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class ReanimatorDirtyLayerLookupBuilder
{
    public static PsdLayerPreviewRenderer.DirtyLayerLookup Build(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers,
        ICollection<string> dirtyGuids,
        ICollection<string> dirtyStableKeys)
    {
        var lookup = new PsdLayerPreviewRenderer.DirtyLayerLookup();
        if (layers == null || layers.Count == 0)
            return lookup;

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer == null || !IsDirtyLayerState(layer, layer.spriteGuid, dirtyGuids, dirtyStableKeys))
                continue;

            var record = new PsdLayerPreviewRenderer.DirtyLayerRecord
            {
                layer = layer,
                position = layer.position,
                sourceRect = layer.sourceRect
            };

            var guid = NormalizeGuidString(layer.spriteGuid);
            if (!string.IsNullOrEmpty(guid))
                lookup.byGuid[guid] = record;

            var stableKey = BuildStableKey(layer.name, layer.layerId);
            if (!string.IsNullOrEmpty(stableKey))
                lookup.byStableKey[stableKey] = record;
        }

        return lookup;
    }

    public static PsdLayerPreviewRenderer.DirtyLayerLookup Merge(
        PsdLayerPreviewRenderer.DirtyLayerLookup primary,
        PsdLayerPreviewRenderer.DirtyLayerLookup secondary)
    {
        if (primary == null)
            return secondary;
        if (secondary == null)
            return primary;

        var merged = new PsdLayerPreviewRenderer.DirtyLayerLookup();
        Copy(primary, merged, overwrite: true);
        Copy(secondary, merged, overwrite: false);
        return merged;
    }

    public static PsdLayerPreviewRenderer.DirtyLayerRecord ResolveFromSerializedSprite(
        SerializedProperty spriteElement,
        PsdLayerPreviewRenderer.DirtyLayerLookup lookup,
        string guid)
    {
        if (lookup == null)
            return null;

        if (!string.IsNullOrEmpty(guid) && lookup.byGuid.TryGetValue(guid, out var byGuid))
            return byGuid;

        var layerIdProp = spriteElement != null
            ? spriteElement.FindPropertyRelative("m_LayerID") ??
              spriteElement.FindPropertyRelative("layerID") ??
              spriteElement.FindPropertyRelative("layerId")
            : null;
        var stableKey = BuildStableKey(string.Empty, layerIdProp != null ? layerIdProp.intValue : 0);
        if (!string.IsNullOrEmpty(stableKey) && lookup.byStableKey.TryGetValue(stableKey, out var byStableKey))
            return byStableKey;

        return null;
    }

    public static bool ContainsRecord(
        PsdLayerPreviewRenderer.DirtyLayerLookup lookup,
        string guid,
        PsdLayerPreviewRenderer.DirtyLayerRecord record)
    {
        if (lookup == null)
            return false;

        guid = NormalizeGuidString(guid);
        if (!string.IsNullOrEmpty(guid) &&
            lookup.byGuid.TryGetValue(guid, out var byGuid) &&
            ReferenceEquals(byGuid, record))
            return true;

        if (record?.layer == null)
            return false;

        var stableKey = BuildStableKey(record.layer.name, record.layer.layerId);
        return !string.IsNullOrEmpty(stableKey) &&
               lookup.byStableKey.TryGetValue(stableKey, out var byStableKey) &&
               ReferenceEquals(byStableKey, record);
    }

    private static void Copy(
        PsdLayerPreviewRenderer.DirtyLayerLookup source,
        PsdLayerPreviewRenderer.DirtyLayerLookup target,
        bool overwrite)
    {
        if (source == null || target == null)
            return;

        Copy(source.byGuid, target.byGuid, overwrite);
        Copy(source.byStableKey, target.byStableKey, overwrite);
    }

    private static void Copy(
        Dictionary<string, PsdLayerPreviewRenderer.DirtyLayerRecord> source,
        Dictionary<string, PsdLayerPreviewRenderer.DirtyLayerRecord> target,
        bool overwrite)
    {
        if (source == null || target == null)
            return;

        foreach (var pair in source)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                continue;
            if (overwrite || !target.ContainsKey(pair.Key))
                target[pair.Key] = pair.Value;
        }
    }

    private static bool IsDirtyLayerState(
        PsdLayerPreviewRenderer.LayerInfo layer,
        string spriteGuid,
        ICollection<string> dirtyGuids,
        ICollection<string> dirtyStableKeys)
    {
        if ((dirtyGuids == null || dirtyGuids.Count == 0) &&
            (dirtyStableKeys == null || dirtyStableKeys.Count == 0))
            return false;

        var guid = NormalizeGuidString(spriteGuid);
        if (!string.IsNullOrEmpty(guid) &&
            dirtyGuids != null &&
            dirtyGuids.Contains(guid, StringComparer.OrdinalIgnoreCase))
            return true;

        if (layer == null)
            return false;

        var layerGuid = NormalizeGuidString(layer.spriteGuid);
        if (!string.IsNullOrEmpty(layerGuid) &&
            dirtyGuids != null &&
            dirtyGuids.Contains(layerGuid, StringComparer.OrdinalIgnoreCase))
            return true;

        var stableKey = BuildStableKey(layer.name, layer.layerId);
        return !string.IsNullOrEmpty(stableKey) &&
               dirtyStableKeys != null &&
               dirtyStableKeys.Contains(stableKey, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildStableKey(string name, int layerId)
    {
        return layerId != 0 ? layerId.ToString() : string.Empty;
    }

    private static string NormalizeGuidString(string value)
    {
        return ReanimatorRebuildMetaAlgorithms.NormalizeGuidString(value);
    }
}
#endif
