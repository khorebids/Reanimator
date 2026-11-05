#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal int WeightBrushCapInfluenceCount
    {
        get => weightBrushCapInfluenceCount <= 0 ? 3 : Mathf.Clamp(weightBrushCapInfluenceCount, 1, 3);
        set => weightBrushCapInfluenceCount = Mathf.Clamp(value, 1, 3);
    }

    internal bool TryApplyWeightBrushAt(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        int spriteBoneIndex,
        int modeIndex,
        float amount,
        float radius,
        float hardness,
        bool normalize,
        bool subtractModifier,
        bool smoothModifier,
        int capInfluenceCount)
    {
        if (context == null)
            return false;

        var coordinates = context.Coordinates;
        var drawList = context.DrawList;
        if (!TryGetWeightBrushLayerDataAt(coordinates, drawList, mouseCanvas, out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length == 0 || data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone.Length;
        var spriteBone = ReanimatorWeightBrushAlgorithms.ResolveSpriteBoneIndex(
            spriteBoneIndex,
            selectedBoneIndex,
            data.spriteBoneToRendererBone);
        var capModifier = subtractModifier && smoothModifier;
        if (!capModifier && !smoothModifier && spriteBone < 0)
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return false;

        radius = Mathf.Max(1f, radius);
        var radiusSqr = radius * radius;
        hardness = Mathf.Clamp01(hardness);
        amount = Mathf.Clamp01(amount);
        var mode = Mathf.Clamp(modeIndex, 0, WeightModesLengthFallback);
        if (mode == 0)
            mode = subtractModifier ? 2 : 1;

        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var changed = false;
        for (var i = 0; i < drawVertices.Length && i < data.boneWeights.Length; i++)
        {
            var vertexCanvas = toCanvasVertex(drawVertices[i]);
            var distanceSqr = (vertexCanvas - mouseCanvas).sqrMagnitude;
            if (distanceSqr > radiusSqr)
                continue;

            var distance = Mathf.Sqrt(distanceSqr);
            var falloff = EvaluateWeightBrushFalloff(distance, radius, hardness);
            if (falloff <= 0.0001f)
                continue;

            var previous = data.boneWeights[i];
            BoneWeight next;
            if (capModifier)
            {
                next = ReanimatorWeightBrushAlgorithms.CapInfluences(previous, spriteBoneCount, capInfluenceCount);
            }
            else if (smoothModifier)
            {
                if (!TryBuildSmoothedWeightBrushBoneWeight(data, i, amount * falloff, out next))
                    continue;
            }
            else
            {
                var currentWeight = GetBoneWeight(previous, spriteBone);
                var nextWeight = currentWeight;
                switch (mode)
                {
                    case 1:
                        nextWeight = currentWeight + amount * falloff;
                        break;
                    case 2:
                        nextWeight = currentWeight - amount * falloff;
                        break;
                    case 3:
                        nextWeight = Mathf.Lerp(currentWeight, amount, falloff);
                        break;
                }

                nextWeight = Mathf.Clamp01(nextWeight);
                next = BuildWeightBrushBoneWeight(
                    previous,
                    spriteBone,
                    nextWeight,
                    spriteBoneCount,
                    normalize,
                    mode == 2);
            }
            if (AreBoneWeightsApproximatelyEqual(previous, next))
                continue;

            RegisterWeightBrushStrokeUndoOnce();
            data.boneWeights[i] = next;
            changed = true;
        }

        if (!changed)
            return false;

        selectedLayerSelectionKey = selectionKey;
        editGeometrySelectedLayerStableKey = stableKey;
        weightBrushLayerStableKey = stableKey;
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private void RegisterWeightBrushStrokeUndoOnce()
    {
        if (weightBrushStrokeUndoRegistered)
            return;

        RegisterUndo("Brush Weights");
        weightBrushStrokeUndoRegistered = true;
    }

    private const int WeightModesLengthFallback = 3;

    private static float EvaluateWeightBrushFalloff(float distance, float radius, float hardness)
    {
        if (radius <= 0.0001f)
            return 1f;

        var hardRadius = radius * Mathf.Clamp01(hardness);
        if (distance <= hardRadius)
            return 1f;

        var softRange = Mathf.Max(0.0001f, radius - hardRadius);
        return Mathf.Clamp01(1f - (distance - hardRadius) / softRange);
    }

    private BoneWeight BuildWeightBrushBoneWeight(
        BoneWeight source,
        int spriteBone,
        float targetWeight,
        int spriteBoneCount,
        bool normalize,
        bool allowPartialSingleBoneWeight = false)
    {
        var weightsByBone = new Dictionary<int, float>();
        AccumulateWeightBrushBone(weightsByBone, source.boneIndex0, source.weight0, spriteBoneCount);
        AccumulateWeightBrushBone(weightsByBone, source.boneIndex1, source.weight1, spriteBoneCount);
        AccumulateWeightBrushBone(weightsByBone, source.boneIndex2, source.weight2, spriteBoneCount);
        AccumulateWeightBrushBone(weightsByBone, source.boneIndex3, source.weight3, spriteBoneCount);

        weightsByBone[spriteBone] = Mathf.Clamp01(targetWeight);
        if (normalize)
            NormalizeWeightBrushWeights(weightsByBone, spriteBone, spriteBoneCount, allowPartialSingleBoneWeight);

        return BuildWeightBrushBoneWeightFromDictionary(weightsByBone, spriteBoneCount, false);
    }

    private bool TryBuildSmoothedWeightBrushBoneWeight(
        LayerVertexData data,
        int vertexIndex,
        float strength,
        out BoneWeight boneWeight)
    {
        boneWeight = default;
        if (data == null || data.boneWeights == null || vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
            return false;

        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        if (spriteBoneCount <= 0)
            return false;

        var neighbors = new HashSet<int>();
        AddWeightBrushEdgeNeighbors(data.allEdgePairs, vertexIndex, data.boneWeights.Length, neighbors);
        if (neighbors.Count == 0)
            AddWeightBrushTriangleNeighbors(data.triangles, vertexIndex, data.boneWeights.Length, neighbors);
        if (neighbors.Count == 0)
            return false;

        var current = BuildWeightBrushWeightDictionary(data.boneWeights[vertexIndex], spriteBoneCount);
        var average = new Dictionary<int, float>();
        foreach (var neighbor in neighbors)
        {
            if (neighbor < 0 || neighbor >= data.boneWeights.Length)
                continue;

            AccumulateWeightBrushBone(average, data.boneWeights[neighbor].boneIndex0, data.boneWeights[neighbor].weight0, spriteBoneCount);
            AccumulateWeightBrushBone(average, data.boneWeights[neighbor].boneIndex1, data.boneWeights[neighbor].weight1, spriteBoneCount);
            AccumulateWeightBrushBone(average, data.boneWeights[neighbor].boneIndex2, data.boneWeights[neighbor].weight2, spriteBoneCount);
            AccumulateWeightBrushBone(average, data.boneWeights[neighbor].boneIndex3, data.boneWeights[neighbor].weight3, spriteBoneCount);
        }

        if (neighbors.Count > 0)
        {
            var averageKeys = new List<int>(average.Keys);
            var invNeighborCount = 1f / neighbors.Count;
            for (var i = 0; i < averageKeys.Count; i++)
                average[averageKeys[i]] *= invNeighborCount;
        }

        var mixed = new Dictionary<int, float>();
        var keys = new HashSet<int>(current.Keys);
        foreach (var key in average.Keys)
            keys.Add(key);

        var t = Mathf.Clamp01(strength);
        foreach (var key in keys)
        {
            current.TryGetValue(key, out var from);
            average.TryGetValue(key, out var to);
            var value = Mathf.Lerp(from, to, t);
            if (value > 0.0001f)
                mixed[key] = value;
        }

        NormalizeWeightBrushExistingWeights(mixed);
        boneWeight = BuildWeightBrushBoneWeightFromDictionary(mixed, spriteBoneCount, true);
        return true;
    }

    private static Dictionary<int, float> BuildWeightBrushWeightDictionary(BoneWeight weight, int spriteBoneCount)
    {
        return ReanimatorWeightSliderAlgorithms.BuildWeightBrushWeightDictionary(weight, spriteBoneCount);
    }

    private static void AddWeightBrushEdgeNeighbors(int[] edgePairs, int vertexIndex, int vertexCount, HashSet<int> neighbors)
    {
        if (edgePairs == null || neighbors == null)
            return;

        for (var i = 0; i + 1 < edgePairs.Length; i += 2)
        {
            var a = edgePairs[i];
            var b = edgePairs[i + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;

            if (a == vertexIndex)
                neighbors.Add(b);
            else if (b == vertexIndex)
                neighbors.Add(a);
        }
    }

    private static void AddWeightBrushTriangleNeighbors(int[] triangles, int vertexIndex, int vertexCount, HashSet<int> neighbors)
    {
        if (triangles == null || neighbors == null)
            return;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                continue;

            if (a == vertexIndex)
            {
                neighbors.Add(b);
                neighbors.Add(c);
            }
            else if (b == vertexIndex)
            {
                neighbors.Add(a);
                neighbors.Add(c);
            }
            else if (c == vertexIndex)
            {
                neighbors.Add(a);
                neighbors.Add(b);
            }
        }
    }

    private BoneWeight BuildWeightBrushBoneWeightFromDictionary(
        Dictionary<int, float> weightsByBone,
        int spriteBoneCount,
        bool normalize)
    {
        var ordered = new List<KeyValuePair<int, float>>(weightsByBone);
        ordered.RemoveAll(pair => pair.Key < 0 || pair.Key >= spriteBoneCount || pair.Value <= 0.0001f);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

        var indices = new int[4];
        var weights = new float[4];
        for (var i = 0; i < ordered.Count && i < 4; i++)
        {
            indices[i] = ordered[i].Key;
            weights[i] = Mathf.Clamp01(ordered[i].Value);
        }

        return BuildSanitizedEditGeometryBoneWeight(indices, weights, spriteBoneCount, normalize);
    }

    private static void AccumulateWeightBrushBone(Dictionary<int, float> weightsByBone, int bone, float weight, int spriteBoneCount)
    {
        if (weightsByBone == null || bone < 0 || bone >= spriteBoneCount || weight <= 0.0001f)
            return;

        if (weightsByBone.TryGetValue(bone, out var existing))
            weightsByBone[bone] = existing + weight;
        else
            weightsByBone.Add(bone, weight);
    }

    private static void NormalizeWeightBrushWeights(
        Dictionary<int, float> weightsByBone,
        int preservedBone,
        int spriteBoneCount,
        bool allowPartialSingleBoneWeight = false)
    {
        if (weightsByBone == null || spriteBoneCount <= 0)
            return;

        var keys = new List<int>(weightsByBone.Keys);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (key < 0 || key >= spriteBoneCount || weightsByBone[key] <= 0.0001f)
                weightsByBone.Remove(key);
            else
                weightsByBone[key] = Mathf.Clamp01(weightsByBone[key]);
        }

        var hasPreserved = weightsByBone.TryGetValue(preservedBone, out var preserved);
        if (!hasPreserved)
        {
            NormalizeWeightBrushExistingWeights(weightsByBone);
            return;
        }

        preserved = Mathf.Clamp01(preserved);
        weightsByBone[preservedBone] = preserved;
        var remainder = Mathf.Clamp01(1f - preserved);
        var otherSum = 0f;
        foreach (var pair in weightsByBone)
        {
            if (pair.Key == preservedBone)
                continue;
            otherSum += Mathf.Max(0f, pair.Value);
        }

        if (otherSum <= 0.0001f)
        {
            if (preserved <= 0.0001f)
            {
                weightsByBone.Remove(preservedBone);
                return;
            }

            weightsByBone[preservedBone] = allowPartialSingleBoneWeight ? preserved : 1f;
            return;
        }

        var scale = remainder / otherSum;
        keys = new List<int>(weightsByBone.Keys);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (key == preservedBone)
                continue;
            weightsByBone[key] = Mathf.Max(0f, weightsByBone[key]) * scale;
        }
    }

    private static void NormalizeWeightBrushExistingWeights(Dictionary<int, float> weightsByBone)
    {
        if (weightsByBone == null || weightsByBone.Count == 0)
            return;

        var sum = 0f;
        foreach (var pair in weightsByBone)
            sum += Mathf.Max(0f, pair.Value);

        if (sum <= 0.0001f)
        {
            weightsByBone.Clear();
            return;
        }

        var keys = new List<int>(weightsByBone.Keys);
        var inv = 1f / sum;
        for (var i = 0; i < keys.Count; i++)
            weightsByBone[keys[i]] = Mathf.Clamp01(weightsByBone[keys[i]] * inv);
    }

    private static bool AreBoneWeightsApproximatelyEqual(BoneWeight a, BoneWeight b)
    {
        const float epsilon = 0.0001f;
        return a.boneIndex0 == b.boneIndex0 &&
               a.boneIndex1 == b.boneIndex1 &&
               a.boneIndex2 == b.boneIndex2 &&
               a.boneIndex3 == b.boneIndex3 &&
               Mathf.Abs(a.weight0 - b.weight0) <= epsilon &&
               Mathf.Abs(a.weight1 - b.weight1) <= epsilon &&
               Mathf.Abs(a.weight2 - b.weight2) <= epsilon &&
               Mathf.Abs(a.weight3 - b.weight3) <= epsilon;
    }

    private bool TryGetWeightBrushLayerDataAt(
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        Vector2 mouseCanvas,
        out LayerVertexData data,
        out string selectionKey,
        out string stableKey)
    {
        data = null;
        selectionKey = string.Empty;
        stableKey = string.Empty;
        if (coordinates == null || drawList == null)
            return false;

        var mouseDoc = coordinates.ViewToDocument(mouseCanvas);
        if (TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out _) && hitLayer != null)
        {
            selectionKey = BuildSelectionKey(hitLayer);
            stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) && data != null)
                return true;
        }

        return TryGetWeightBrushLayerData(out data, out selectionKey, out stableKey);
    }

    private bool TryGetWeightBrushLayerData(out LayerVertexData data, out string selectionKey, out string stableKey)
    {
        data = null;
        selectionKey = selectedLayerSelectionKey;
        stableKey = !string.IsNullOrEmpty(weightBrushLayerStableKey)
            ? weightBrushLayerStableKey
            : editGeometrySelectedLayerStableKey;

        if (!string.IsNullOrEmpty(selectionKey) &&
            TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) &&
            data != null)
        {
            stableKey = BuildKey(data.layer.name, data.layer.layerId);
            weightBrushLayerStableKey = stableKey;
            editGeometrySelectedLayerStableKey = stableKey;
            return true;
        }

        if (!string.IsNullOrEmpty(stableKey) &&
            TryGetLayerVertexDataByStableKey(stableKey, out data, out var resolvedSelectionKey) &&
            data != null)
        {
            selectionKey = resolvedSelectionKey;
            selectedLayerSelectionKey = selectionKey;
            editGeometrySelectedLayerStableKey = stableKey;
            weightBrushLayerStableKey = stableKey;
            return true;
        }

        if (TryGetSelectedLayer(out var layer) && layer != null)
        {
            selectionKey = BuildSelectionKey(layer);
            stableKey = BuildKey(layer.name, layer.layerId);
            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) && data != null)
            {
                selectedLayerSelectionKey = selectionKey;
                editGeometrySelectedLayerStableKey = stableKey;
                weightBrushLayerStableKey = stableKey;
                return true;
            }
        }

        return false;
    }

}
#endif
