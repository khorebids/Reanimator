#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static void AccumulateCurveWeight(Dictionary<int, float> totals, int spriteBoneIndex, float weight)
    {
        if (totals == null || spriteBoneIndex < 0 || weight <= 0.0001f)
            return;

        if (totals.TryGetValue(spriteBoneIndex, out var sum))
            totals[spriteBoneIndex] = sum + weight;
        else
            totals.Add(spriteBoneIndex, weight);
    }

    private static float GetBoneWeight(BoneWeight weight, int spriteBoneIndex)
    {
        var result = 0f;
        if (weight.boneIndex0 == spriteBoneIndex)
            result += weight.weight0;
        if (weight.boneIndex1 == spriteBoneIndex)
            result += weight.weight1;
        if (weight.boneIndex2 == spriteBoneIndex)
            result += weight.weight2;
        if (weight.boneIndex3 == spriteBoneIndex)
            result += weight.weight3;
        return Mathf.Clamp01(result);
    }

    private static float GetOtherCurveWeight(BoneWeight weight, int firstSpriteBone, int secondSpriteBone)
    {
        return ReanimatorCurveWeightAlgorithms.GetOtherWeight(weight, firstSpriteBone, secondSpriteBone);
    }

    private static float ClampCurveChildWeight(BoneWeight sourceWeight, int firstSpriteBone, int secondSpriteBone, float childWeight)
    {
        return ReanimatorCurveWeightAlgorithms.ClampChildWeight(sourceWeight, firstSpriteBone, secondSpriteBone, childWeight);
    }

    private static float GetCurveMainWeightCapacity(float otherWeight)
    {
        return Mathf.Clamp01(1f - Mathf.Clamp01(otherWeight));
    }

    private static BoneWeight BuildCurveBoneWeightPreservingOthers(
        BoneWeight source,
        int firstSpriteBone,
        int secondSpriteBone,
        float secondWeight,
        int spriteBoneCount)
    {
        return ReanimatorCurveWeightAlgorithms.BuildPreservingOthers(source, firstSpriteBone, secondSpriteBone, secondWeight, spriteBoneCount);
    }

}
#endif
