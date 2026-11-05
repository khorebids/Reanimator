#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void MarkLayerSpriteStateDirty(LayerInfo layer)
    {
        AddDirtyLayerState(layer, ref dirtyLayerSpriteGuids, ref dirtyLayerStableKeys);
    }

    private void MarkLayerPositionStateDirty(LayerInfo layer)
    {
        AddDirtyLayerState(layer, ref dirtyLayerPositionGuids, ref dirtyLayerPositionStableKeys);
    }

    private static void AddDirtyLayerState(LayerInfo layer, ref HashSet<string> dirtyGuids, ref HashSet<string> dirtyStableKeys)
    {
        if (layer == null)
            return;

        dirtyGuids ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        dirtyStableKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var guid = NormalizeGuidString(layer.spriteGuid);
        if (!string.IsNullOrEmpty(guid))
            dirtyGuids.Add(guid);

        var stableKey = BuildKey(layer.name, layer.layerId);
        if (!string.IsNullOrEmpty(stableKey))
            dirtyStableKeys.Add(stableKey);
    }

    private bool IsLayerSpriteStateDirty(LayerInfo layer, string spriteGuid)
    {
        return IsDirtyLayerState(layer, spriteGuid, dirtyLayerSpriteGuids, dirtyLayerStableKeys);
    }

    private bool IsLayerPositionStateDirty(LayerInfo layer, string spriteGuid)
    {
        return IsDirtyLayerState(layer, spriteGuid, dirtyLayerPositionGuids, dirtyLayerPositionStableKeys);
    }

    private static bool IsDirtyLayerState(
        LayerInfo layer,
        string spriteGuid,
        HashSet<string> dirtyGuids,
        HashSet<string> dirtyStableKeys)
    {
        if ((dirtyGuids == null || dirtyGuids.Count == 0) &&
            (dirtyStableKeys == null || dirtyStableKeys.Count == 0))
            return false;

        var guid = NormalizeGuidString(spriteGuid);
        if (!string.IsNullOrEmpty(guid) &&
            dirtyGuids != null &&
            dirtyGuids.Contains(guid))
            return true;

        if (layer == null)
            return false;

        var layerGuid = NormalizeGuidString(layer.spriteGuid);
        if (!string.IsNullOrEmpty(layerGuid) &&
            dirtyGuids != null &&
            dirtyGuids.Contains(layerGuid))
            return true;

        var stableKey = BuildKey(layer.name, layer.layerId);
        return !string.IsNullOrEmpty(stableKey) &&
               dirtyStableKeys != null &&
               dirtyStableKeys.Contains(stableKey);
    }

    private bool HasDirtyLayerSpriteState()
    {
        return (dirtyLayerSpriteGuids != null && dirtyLayerSpriteGuids.Count > 0) ||
               (dirtyLayerStableKeys != null && dirtyLayerStableKeys.Count > 0);
    }

    private bool HasDirtyLayerPositionState()
    {
        return (dirtyLayerPositionGuids != null && dirtyLayerPositionGuids.Count > 0) ||
               (dirtyLayerPositionStableKeys != null && dirtyLayerPositionStableKeys.Count > 0);
    }

    private void ClearDirtyLayerSpriteState()
    {
        dirtyLayerSpriteGuids?.Clear();
        dirtyLayerStableKeys?.Clear();
        dirtyLayerPositionGuids?.Clear();
        dirtyLayerPositionStableKeys?.Clear();
    }

    internal sealed class DirtyLayerRecord
    {
        public LayerInfo layer;
        public Vector2 position;
        public Rect sourceRect;
    }

    internal sealed class DirtyLayerLookup
    {
        public readonly Dictionary<string, DirtyLayerRecord> byGuid = new Dictionary<string, DirtyLayerRecord>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, DirtyLayerRecord> byStableKey = new Dictionary<string, DirtyLayerRecord>(StringComparer.OrdinalIgnoreCase);
    }

    private DirtyLayerLookup BuildDirtyLayerLookup(bool positionState)
    {
        return ReanimatorDirtyLayerLookupBuilder.Build(
            layerInfos,
            positionState ? dirtyLayerPositionGuids : dirtyLayerSpriteGuids,
            positionState ? dirtyLayerPositionStableKeys : dirtyLayerStableKeys);
    }

    private static DirtyLayerLookup MergeDirtyLayerLookups(DirtyLayerLookup primary, DirtyLayerLookup secondary)
    {
        return ReanimatorDirtyLayerLookupBuilder.Merge(primary, secondary);
    }

    private static DirtyLayerRecord ResolveDirtyLayerFromSerializedSprite(
        SerializedProperty spriteElement,
        DirtyLayerLookup lookup,
        string guid)
    {
        return ReanimatorDirtyLayerLookupBuilder.ResolveFromSerializedSprite(spriteElement, lookup, guid);
    }
}
#endif
