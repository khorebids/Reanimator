#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string[] GetCurveBonePairOptions(out int selected)
    {
        var pairs = BuildCurveBonePairOptions(out selected);
        return pairs.ConvertAll(pair => pair.label).ToArray();
    }

    public void SelectCurveBonePair(int index)
    {
        var pairs = BuildCurveBonePairOptions(out var selected);
        if (index < 0 || index >= pairs.Count || index == selected)
            return;

        RegisterUndo("Select WeightCurve Bone Pair");
        ResetCurveGraphToCurrentWeights();
        curveSecondBoneSelectionSignature = BuildCurveSelectionSignature();
        curveSelectedFirstSpriteBone = pairs[index].first;
        curveSelectedSecondSpriteBone = pairs[index].second;
    }

    private List<(int first, int second, string label)> BuildCurveBonePairOptions(out int selected)
    {
        selected = -1;
        var pairs = new List<(int first, int second, string label)>();
        if (!TryGetCurveGraphContext(out var data, out _, out _, out _, out var vertices,
                out var currentFirst, out var currentSecond, out _, out _, out _))
            return pairs;

        var candidates = BuildCurveBoneCandidates(data, vertices);
        candidates.RemoveAll(candidate => candidate.Key < 0 || candidate.Key >= data.spriteBoneToRendererBone.Length ||
            data.spriteBoneToRendererBone[candidate.Key] < 0);
        if (candidates.Count != 3)
            return pairs;

        for (var first = 0; first < candidates.Count - 1; first++)
        {
            for (var second = first + 1; second < candidates.Count; second++)
            {
                var a = candidates[first].Key;
                var b = candidates[second].Key;
                if ((a == currentFirst && b == currentSecond) || (b == currentFirst && a == currentSecond))
                {
                    selected = pairs.Count;
                    a = currentFirst;
                    b = currentSecond;
                }
                pairs.Add((a, b, GetName(a) + " / " + GetName(b)));
            }
        }

        return pairs;

        string GetName(int spriteBone)
        {
            var rendererBone = data.spriteBoneToRendererBone[spriteBone];
            return rendererBone < bones.Count ? bones[rendererBone]?.name ?? "Bone " + spriteBone : "Bone " + spriteBone;
        }
    }

    private bool TryResolveCurveBonePair(
        LayerVertexData data,
        IReadOnlyList<int> vertexIndices,
        out int parentSpriteBone,
        out int childSpriteBone,
        out int parentRendererBone,
        out int childRendererBone)
    {
        parentSpriteBone = -1;
        childSpriteBone = -1;
        parentRendererBone = -1;
        childRendererBone = -1;
        if (data == null || vertexIndices == null || vertexIndices.Count == 0)
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return false;
        if (data.boneWeights == null || data.boneWeights.Length == 0)
            return false;

        if (TryGetSelectedCurveBonePair(data, out parentSpriteBone, out childSpriteBone, out parentRendererBone, out childRendererBone))
            return true;

        var candidates = BuildCurveBoneCandidates(data, vertexIndices);
        if (candidates.Count < 2)
            return false;

        var validCandidates = new List<KeyValuePair<int, float>>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var spriteBone = candidates[i].Key;
            if (spriteBone < 0 || spriteBone >= data.spriteBoneToRendererBone.Length)
                continue;

            var rendererBone = data.spriteBoneToRendererBone[spriteBone];
            if (rendererBone < 0)
                continue;

            validCandidates.Add(candidates[i]);
        }

        if (validCandidates.Count < 2)
            return false;

        parentSpriteBone = validCandidates[0].Key;
        childSpriteBone = validCandidates[validCandidates.Count - 1].Key;
        parentRendererBone = data.spriteBoneToRendererBone[parentSpriteBone];
        childRendererBone = data.spriteBoneToRendererBone[childSpriteBone];

        curveSecondBoneSelectionSignature = BuildCurveSelectionSignature();
        curveSelectedFirstSpriteBone = parentSpriteBone;
        curveSelectedSecondSpriteBone = childSpriteBone;
        return true;
    }

    private List<KeyValuePair<int, float>> BuildCurveBoneCandidates(LayerVertexData data, IReadOnlyList<int> vertexIndices)
    {
        var candidates = new List<KeyValuePair<int, float>>();
        if (data == null || vertexIndices == null || vertexIndices.Count == 0 || data.boneWeights == null)
            return candidates;

        var totals = new Dictionary<int, float>();
        for (var i = 0; i < vertexIndices.Count; i++)
        {
            var vertexIndex = vertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
                continue;

            var weight = SanitizeEditableBoneWeight(data.boneWeights[vertexIndex], data.spriteBoneToRendererBone.Length);
            AccumulateCurveWeight(totals, weight.boneIndex0, weight.weight0);
            AccumulateCurveWeight(totals, weight.boneIndex1, weight.weight1);
            AccumulateCurveWeight(totals, weight.boneIndex2, weight.weight2);
            AccumulateCurveWeight(totals, weight.boneIndex3, weight.weight3);
        }

        candidates.AddRange(totals);
        candidates.Sort((lhs, rhs) => rhs.Value.CompareTo(lhs.Value));
        return candidates;
    }

    private int CountCurveBoneCandidates(LayerVertexData data, IReadOnlyList<int> vertexIndices)
    {
        if (data == null || data.spriteBoneToRendererBone == null)
            return 0;

        var candidates = BuildCurveBoneCandidates(data, vertexIndices);
        var count = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var spriteBone = candidates[i].Key;
            if (spriteBone < 0 || spriteBone >= data.spriteBoneToRendererBone.Length)
                continue;
            if (data.spriteBoneToRendererBone[spriteBone] < 0)
                continue;

            count++;
        }

        return count;
    }

    private bool TryGetSelectedCurveBonePair(
        LayerVertexData data,
        out int parentSpriteBone,
        out int childSpriteBone,
        out int parentRendererBone,
        out int childRendererBone)
    {
        parentSpriteBone = -1;
        childSpriteBone = -1;
        parentRendererBone = -1;
        childRendererBone = -1;

        if (data == null || data.spriteBoneToRendererBone == null)
            return false;
        if (!string.Equals(curveSecondBoneSelectionSignature, BuildCurveSelectionSignature(), StringComparison.Ordinal))
            return false;
        if (curveSelectedFirstSpriteBone < 0 || curveSelectedFirstSpriteBone >= data.spriteBoneToRendererBone.Length)
            return false;
        if (curveSelectedSecondSpriteBone < 0 || curveSelectedSecondSpriteBone >= data.spriteBoneToRendererBone.Length)
            return false;
        if (curveSelectedFirstSpriteBone == curveSelectedSecondSpriteBone)
            return false;

        var selectedParentRenderer = data.spriteBoneToRendererBone[curveSelectedFirstSpriteBone];
        var selectedChildRenderer = data.spriteBoneToRendererBone[curveSelectedSecondSpriteBone];
        if (selectedParentRenderer < 0 || selectedChildRenderer < 0)
            return false;

        parentSpriteBone = curveSelectedFirstSpriteBone;
        childSpriteBone = curveSelectedSecondSpriteBone;
        parentRendererBone = selectedParentRenderer;
        childRendererBone = selectedChildRenderer;
        return true;
    }

    private string BuildCurveOtherBoneName(LayerVertexData data, IReadOnlyList<int> vertexIndices, int firstSpriteBone, int secondSpriteBone)
    {
        if (data == null || data.spriteBoneToRendererBone == null)
            return "Other";

        var labels = new List<string>(2);
        var candidates = BuildCurveBoneCandidates(data, vertexIndices);
        for (var i = 0; i < candidates.Count; i++)
        {
            var spriteBone = candidates[i].Key;
            if (spriteBone == firstSpriteBone || spriteBone == secondSpriteBone)
                continue;
            if (spriteBone < 0 || spriteBone >= data.spriteBoneToRendererBone.Length)
                continue;

            var rendererBone = data.spriteBoneToRendererBone[spriteBone];
            if (rendererBone < 0)
                continue;

            labels.Add(bones != null && rendererBone < bones.Count ? bones[rendererBone].name : "Bone " + spriteBone);
            if (labels.Count >= 2)
                break;
        }

        return labels.Count == 0 ? "Other" : string.Join(" + ", labels);
    }

    private int GetRendererBoneDepth(int rendererBoneIndex)
    {
        if (rendererBoneIndex < 0 || bones == null || rendererBoneIndex >= bones.Count)
            return int.MaxValue;

        return bones[rendererBoneIndex].depth;
    }
}
#endif
