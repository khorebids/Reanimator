#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool TryGetEditGeometrySelection(out EditGeometrySelectionInfo selection)
    {
        selection = default;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;
        if (editGeometrySelectedVertexIndex < 0 || editGeometrySelectedVertexIndex >= data.vertices.Length)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        var spriteBoneNames = BuildSpriteBoneNameList(data.spriteBoneToRendererBone, spriteBoneCount);
        var weight = default(BoneWeight);
        if (data.boneWeights != null && editGeometrySelectedVertexIndex < data.boneWeights.Length)
            weight = data.boneWeights[editGeometrySelectedVertexIndex];
        weight = SanitizeEditableBoneWeight(weight, spriteBoneCount);

        selection = new EditGeometrySelectionInfo(
            ComputeEditGeometryLayerToken(selectionKey, stableKey),
            data.layer != null && !string.IsNullOrEmpty(data.layer.name) ? data.layer.name : "<unknown>",
            editGeometrySelectedVertexIndex,
            data.vertices.Length,
            data.vertices[editGeometrySelectedVertexIndex],
            weight,
            spriteBoneCount,
            spriteBoneNames);
        return true;
    }

    public bool TryGetEditGeometrySelectionInfo(out EditGeometrySelectionInfo selection, out int selectedVertexCount)
    {
        selectedVertexCount = 0;
        if (!TryGetEditGeometrySelection(out selection))
            return false;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out _, out var stableKey))
            return false;

        selectedVertexCount = NormalizeEditGeometrySelection(data, stableKey);
        return selectedVertexCount > 0;
    }

    public bool GrowEditGeometryVertexSelection()
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out _, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;
        if (NormalizeEditGeometrySelection(data, stableKey) <= 0)
            return false;

        var selected = new HashSet<int>(editGeometrySelectedVertexIndices);
        var originalSelected = new HashSet<int>(selected);
        var changed = false;
        AddEditGeometrySelectionNeighbors(data, (a, b) =>
        {
            if (originalSelected.Contains(a) && selected.Add(b))
                changed = true;
            if (originalSelected.Contains(b) && selected.Add(a))
                changed = true;
        });

        if (!changed)
            return false;

        ApplyEditGeometrySelectionSet(stableKey, selected);
        return true;
    }

    public bool ShrinkEditGeometryVertexSelection()
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out _, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;
        if (NormalizeEditGeometrySelection(data, stableKey) <= 0)
            return false;

        var selected = new HashSet<int>(editGeometrySelectedVertexIndices);
        var touchingUnselected = new HashSet<int>();
        AddEditGeometrySelectionNeighbors(data, (a, b) =>
        {
            var aSelected = selected.Contains(a);
            var bSelected = selected.Contains(b);
            if (aSelected && !bSelected)
                touchingUnselected.Add(a);
            if (bSelected && !aSelected)
                touchingUnselected.Add(b);
        });

        if (touchingUnselected.Count == 0)
            return false;

        foreach (var vertexIndex in touchingUnselected)
            selected.Remove(vertexIndex);

        ApplyEditGeometrySelectionSet(stableKey, selected);
        return true;
    }

    public bool KeepOnlyContourEditGeometryVertexSelection()
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out _, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;
        if (NormalizeEditGeometrySelection(data, stableKey) <= 0)
            return false;

        var contourVertices = BuildEditGeometryContourVertexSet(data);
        if (contourVertices.Count == 0)
            return false;

        var selected = new HashSet<int>(editGeometrySelectedVertexIndices);
        var changed = false;
        for (var i = editGeometrySelectedVertexIndices.Count - 1; i >= 0; i--)
        {
            var vertexIndex = editGeometrySelectedVertexIndices[i];
            if (contourVertices.Contains(vertexIndex))
                continue;

            selected.Remove(vertexIndex);
            changed = true;
        }

        if (!changed)
            return false;

        ApplyEditGeometrySelectionSet(stableKey, selected);
        return true;
    }

    public bool DeselectEditGeometryVertexSelection()
    {
        if (!TryGetSelectedEditGeometryLayerData(out _, out _, out _))
            return false;
        if (editGeometrySelectedVertexIndex < 0 &&
            (editGeometrySelectedVertexIndices == null || editGeometrySelectedVertexIndices.Count == 0))
            return false;

        ClearEditGeometryVertexSelection(false);
        return true;
    }

    public bool InvertEditGeometryVertexSelection()
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out _, out var stableKey))
            return false;
        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        if (vertexCount <= 0)
            return false;

        NormalizeEditGeometrySelection(data, stableKey);
        var current = new HashSet<int>(editGeometrySelectedVertexIndices ?? new List<int>());
        var inverted = new HashSet<int>();
        for (var i = 0; i < vertexCount; i++)
        {
            if (!current.Contains(i))
                inverted.Add(i);
        }

        if (inverted.SetEquals(current))
            return false;

        ApplyEditGeometrySelectionSet(stableKey, inverted);
        return true;
    }

    public bool TrySetEditGeometrySelectedVertexWeights(int[] boneIndices, float[] weights, bool normalize)
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0)
            return false;
        if (editGeometrySelectedVertexIndex < 0 || editGeometrySelectedVertexIndex >= data.vertices.Length)
            return false;
        if (boneIndices == null || boneIndices.Length < 4 || weights == null || weights.Length < 4)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        if (spriteBoneCount <= 0)
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);

        RegisterUndo("Set Vertex Weights");
        data.boneWeights[editGeometrySelectedVertexIndex] = BuildSanitizedEditGeometryBoneWeight(boneIndices, weights, spriteBoneCount, normalize);
        MarkSkinningWeightsChanged(data);

        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    public bool CancelEditGeometryTool()
    {
        return EndEditGeometryInteraction(false);
    }

    private void ApplyEditGeometrySelectionSet(string stableKey, HashSet<int> selected)
    {
        if (editGeometrySelectedVertexIndices == null)
            editGeometrySelectedVertexIndices = new List<int>();

        editGeometrySelectedVertexIndices.Clear();
        if (selected != null && selected.Count > 0)
        {
            editGeometrySelectedVertexIndices.AddRange(selected);
            editGeometrySelectedVertexIndices.Sort();
        }

        editGeometrySelectedLayerStableKey = stableKey;
        editGeometrySelectedVertexIndex = editGeometrySelectedVertexIndices.Count > 0 ? editGeometrySelectedVertexIndices[0] : -1;
    }

    private void AddEditGeometrySelectionNeighbors(LayerVertexData data, Action<int, int> visitEdge)
    {
        if (data == null || visitEdge == null)
            return;

        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        if (vertexCount <= 0)
            return;

        var edgePairs = data.allEdgePairs;
        if (edgePairs != null && edgePairs.Length >= 2)
        {
            for (var i = 0; i + 1 < edgePairs.Length; i += 2)
                VisitValidEditGeometrySelectionEdge(edgePairs[i], edgePairs[i + 1], vertexCount, visitEdge);
            return;
        }

        var triangles = data.triangles;
        if (triangles == null || triangles.Length < 3)
            return;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            VisitValidEditGeometrySelectionEdge(a, b, vertexCount, visitEdge);
            VisitValidEditGeometrySelectionEdge(b, c, vertexCount, visitEdge);
            VisitValidEditGeometrySelectionEdge(c, a, vertexCount, visitEdge);
        }
    }

    private static void VisitValidEditGeometrySelectionEdge(int a, int b, int vertexCount, Action<int, int> visitEdge)
    {
        if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
            return;

        visitEdge(a, b);
    }

    private HashSet<int> BuildEditGeometryContourVertexSet(LayerVertexData data)
    {
        var result = new HashSet<int>();
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return result;

        var vertexCount = data.vertices.Length;
        var boundaryPairs = data.boundaryEdgePairs;
        if ((boundaryPairs == null || boundaryPairs.Length < 2) && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out boundaryPairs);
            data.boundaryEdgePairs = boundaryPairs;
        }

        if (boundaryPairs == null)
            return result;

        for (var i = 0; i + 1 < boundaryPairs.Length; i += 2)
        {
            var a = boundaryPairs[i];
            var b = boundaryPairs[i + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;

            result.Add(a);
            result.Add(b);
        }

        return result;
    }

    private bool IsEditGeometryVertexSelected(string stableKey, int vertexIndex)
    {
        if (vertexIndex < 0)
            return false;
        if (!string.Equals(editGeometrySelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (editGeometrySelectedVertexIndices == null)
            return false;
        return editGeometrySelectedVertexIndices.IndexOf(vertexIndex) >= 0;
    }

    private void ClearEditGeometryVertexSelection(bool clearLayer)
    {
        if (clearLayer)
        {
            selectedLayerSelectionKey = string.Empty;
            editGeometrySelectedLayerStableKey = string.Empty;
        }

        editGeometrySelectedVertexIndex = -1;
        if (editGeometrySelectedVertexIndices != null)
            editGeometrySelectedVertexIndices.Clear();
    }

    private bool TryGetSelectedEditGeometryLayerData(out LayerVertexData data, out string selectionKey, out string stableKey)
    {
        data = null;
        selectionKey = selectedLayerSelectionKey;
        stableKey = !string.IsNullOrEmpty(editGeometrySelectedLayerStableKey)
            ? editGeometrySelectedLayerStableKey
            : weightSliderSelectedLayerStableKey;

        if (!string.IsNullOrEmpty(selectionKey) &&
            TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) &&
            data != null)
        {
            stableKey = BuildKey(data.layer.name, data.layer.layerId);
            editGeometrySelectedLayerStableKey = stableKey;
            weightSliderSelectedLayerStableKey = stableKey;
            return true;
        }

        if (!string.IsNullOrEmpty(stableKey) &&
            TryGetLayerVertexDataByStableKey(stableKey, out data, out var resolvedSelectionKey) &&
            data != null)
        {
            selectionKey = resolvedSelectionKey;
            selectedLayerSelectionKey = selectionKey;
            editGeometrySelectedLayerStableKey = stableKey;
            weightSliderSelectedLayerStableKey = stableKey;
            return true;
        }

        editGeometrySelectedVertexIndex = -1;
        if (editGeometrySelectedVertexIndices != null)
            editGeometrySelectedVertexIndices.Clear();
        createEdgeStartVertexIndex = -1;
        createEdgeLayerStableKey = string.Empty;
        return false;
    }

    private static int ComputeEditGeometryLayerToken(string selectionKey, string stableKey)
    {
        var key = $"{selectionKey}|{stableKey}";
        return StringComparer.OrdinalIgnoreCase.GetHashCode(key);
    }

    private string[] BuildSpriteBoneNameList(int[] spriteBoneToRendererBone, int spriteBoneCount)
    {
        if (spriteBoneCount <= 0)
            return Array.Empty<string>();

        var names = new string[spriteBoneCount];
        for (var i = 0; i < spriteBoneCount; i++)
        {
            var fallback = $"Bone {i}";
            if (spriteBoneToRendererBone == null || i >= spriteBoneToRendererBone.Length)
            {
                names[i] = fallback;
                continue;
            }

            var rendererIndex = spriteBoneToRendererBone[i];
            if (rendererIndex < 0 || rendererIndex >= bones.Count)
            {
                names[i] = fallback;
                continue;
            }

            var boneName = bones[rendererIndex].name;
            names[i] = string.IsNullOrWhiteSpace(boneName) ? fallback : boneName;
        }

        return names;
    }

    private Color[] BuildSpriteBoneColorList(int[] spriteBoneToRendererBone, int spriteBoneCount)
    {
        if (spriteBoneCount <= 0)
            return Array.Empty<Color>();

        var colors = new Color[spriteBoneCount];
        for (var i = 0; i < spriteBoneCount; i++)
        {
            if (spriteBoneToRendererBone == null || i >= spriteBoneToRendererBone.Length)
            {
                colors[i] = Color.gray;
                continue;
            }

            var rendererIndex = spriteBoneToRendererBone[i];
            if (rendererIndex < 0 || rendererIndex >= bones.Count)
            {
                colors[i] = Color.gray;
                continue;
            }

            var color = bones[rendererIndex].color;
            color.a = 1f;
            colors[i] = color;
        }

        return colors;
    }

    private static string GetLayerDisplayName(LayerVertexData data)
    {
        return data != null && data.layer != null && !string.IsNullOrWhiteSpace(data.layer.name)
            ? data.layer.name
            : "<unknown>";
    }

    private static int FindSpriteBoneIndexForRendererBone(LayerVertexData data, int rendererBoneIndex)
    {
        if (data == null || data.spriteBoneToRendererBone == null || rendererBoneIndex < 0)
            return -1;

        for (var i = 0; i < data.spriteBoneToRendererBone.Length; i++)
        {
            if (data.spriteBoneToRendererBone[i] == rendererBoneIndex)
                return i;
        }

        return -1;
    }

}
#endif
