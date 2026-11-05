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
    private void CapturePendingUiVisibilityState(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        pendingUiVisibilityPath = assetPath;
        pendingLayerVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        pendingLayerDepth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        pendingLayerColor = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        pendingHierarchyVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        pendingBoneVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        pendingBoneDepth = new Dictionary<int, int>();
        pendingBoneColor = new Dictionary<int, Color>();

        CaptureCurrentUiVisibilityState(
            pendingLayerVisibility,
            pendingLayerDepth,
            pendingLayerColor,
            pendingHierarchyVisibility,
            pendingBoneVisibility,
            pendingBoneDepth,
            pendingBoneColor);
        StoreUiVisibilityCacheForCurrentAsset();
    }

    private void CaptureCurrentUiVisibilityState(
        Dictionary<string, bool> layerVisibility,
        Dictionary<string, int> layerDepth,
        Dictionary<string, Color> layerColor,
        Dictionary<string, bool> hierarchyVisibility,
        Dictionary<string, bool> boneVisibility,
        Dictionary<int, int> boneDepth,
        Dictionary<int, Color> boneColor)
    {
        if (layerVisibility != null || layerDepth != null || layerColor != null)
        {
            foreach (var info in layerInfos)
            {
                if (info == null)
                    continue;

                var key = BuildKey(info.name, info.layerId);
                if (layerVisibility != null)
                    layerVisibility[key] = info.visible;
                if (layerDepth != null)
                    layerDepth[key] = info.depth;
                if (layerColor != null)
                    layerColor[key] = info.color;
            }
        }

        if (hierarchyVisibility != null)
        {
            foreach (var node in layerHierarchyNodes)
            {
                if (node == null || string.IsNullOrEmpty(node.key))
                    continue;

                hierarchyVisibility[node.key] = node.visible;
            }
        }

        if (boneVisibility == null && boneDepth == null && boneColor == null)
            return;

        foreach (var bone in bones)
        {
            if (boneVisibility != null)
                boneVisibility[BuildBoneKey(bone.name, bone.index)] = bone.visible;
            if (boneDepth != null)
                boneDepth[bone.index] = bone.depth;
            if (boneColor != null)
                boneColor[bone.index] = bone.color;
        }
    }

    private bool TryConsumePendingUiVisibilityState(
        string assetPath,
        Dictionary<string, bool> layerVisibility,
        Dictionary<string, int> layerDepth,
        Dictionary<string, Color> layerColor,
        Dictionary<string, bool> hierarchyVisibility,
        Dictionary<string, bool> boneVisibility,
        Dictionary<int, int> boneDepth,
        Dictionary<int, Color> boneColor)
    {
        if (string.IsNullOrWhiteSpace(assetPath) ||
            string.IsNullOrWhiteSpace(pendingUiVisibilityPath) ||
            !string.Equals(pendingUiVisibilityPath, assetPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CopyDictionary(pendingLayerVisibility, layerVisibility);
        CopyDictionary(pendingLayerDepth, layerDepth);
        CopyDictionary(pendingLayerColor, layerColor);
        CopyDictionary(pendingHierarchyVisibility, hierarchyVisibility);
        CopyDictionary(pendingBoneVisibility, boneVisibility);
        CopyDictionary(pendingBoneDepth, boneDepth);
        CopyDictionary(pendingBoneColor, boneColor);
        ClearPendingUiVisibilityState();
        return true;
    }

    private void ClearPendingUiVisibilityState()
    {
        pendingUiVisibilityPath = string.Empty;
        pendingLayerVisibility = null;
        pendingLayerDepth = null;
        pendingLayerColor = null;
        pendingHierarchyVisibility = null;
        pendingBoneVisibility = null;
        pendingBoneDepth = null;
        pendingBoneColor = null;
    }

    private void StoreUiVisibilityCacheForCurrentAsset()
    {
        if (string.IsNullOrWhiteSpace(cachedPath))
            return;

        uiVisibilityCache ??= new List<UiVisibilityCacheEntry>();
        var entry = FindUiVisibilityCacheEntry(cachedPath);
        if (entry == null)
        {
            entry = new UiVisibilityCacheEntry { assetPath = cachedPath };
            uiVisibilityCache.Add(entry);
        }

        entry.layerVisibility = BuildVisibilityListFromLayers();
        entry.hierarchyVisibility = BuildVisibilityListFromHierarchy();
        entry.boneVisibility = BuildVisibilityListFromBones();
    }

    private bool TryCopyCachedUiVisibilityState(
        string assetPath,
        Dictionary<string, bool> layerVisibility,
        Dictionary<string, bool> hierarchyVisibility,
        Dictionary<string, bool> boneVisibility)
    {
        var entry = FindUiVisibilityCacheEntry(assetPath);
        if (entry == null)
            return false;

        var changed = false;
        changed |= CopyVisibilityList(entry.layerVisibility, layerVisibility);
        changed |= CopyVisibilityList(entry.hierarchyVisibility, hierarchyVisibility);
        changed |= CopyVisibilityList(entry.boneVisibility, boneVisibility);
        return changed;
    }

    private UiVisibilityCacheEntry FindUiVisibilityCacheEntry(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || uiVisibilityCache == null)
            return null;

        for (var i = 0; i < uiVisibilityCache.Count; i++)
        {
            var entry = uiVisibilityCache[i];
            if (entry != null && string.Equals(entry.assetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    private List<UiVisibilityBool> BuildVisibilityListFromLayers()
    {
        var result = new List<UiVisibilityBool>();
        if (layerInfos == null)
            return result;

        foreach (var info in layerInfos)
        {
            if (info == null)
                continue;

            var key = BuildKey(info.name, info.layerId);
            if (string.IsNullOrEmpty(key))
                continue;

            result.Add(new UiVisibilityBool { key = key, value = info.visible });
        }

        return result;
    }

    private List<UiVisibilityBool> BuildVisibilityListFromHierarchy()
    {
        var result = new List<UiVisibilityBool>();
        if (layerHierarchyNodes == null)
            return result;

        foreach (var node in layerHierarchyNodes)
        {
            if (node == null || string.IsNullOrEmpty(node.key))
                continue;

            result.Add(new UiVisibilityBool { key = node.key, value = node.visible });
        }

        return result;
    }

    private List<UiVisibilityBool> BuildVisibilityListFromBones()
    {
        var result = new List<UiVisibilityBool>();
        if (bones == null)
            return result;

        foreach (var bone in bones)
            result.Add(new UiVisibilityBool { key = BuildBoneKey(bone.name, bone.index), value = bone.visible });

        return result;
    }

    private static bool CopyVisibilityList(List<UiVisibilityBool> source, Dictionary<string, bool> target)
    {
        if (source == null || target == null)
            return false;

        var changed = false;
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            if (item == null || string.IsNullOrEmpty(item.key))
                continue;

            target[item.key] = item.value;
            changed = true;
        }

        return changed;
    }

    private static void CopyDictionary<TKey, TValue>(Dictionary<TKey, TValue> source, Dictionary<TKey, TValue> target)
    {
        if (source == null || target == null)
            return;

        foreach (var pair in source)
            target[pair.Key] = pair.Value;
    }

}

#endif
