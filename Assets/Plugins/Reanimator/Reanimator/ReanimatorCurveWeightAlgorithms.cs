#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorCurveWeightAlgorithms
{
    public static float GetOtherWeight(BoneWeight weight, int firstSpriteBone, int secondSpriteBone)
    {
        var result = 0f;
        AccumulateOtherWeight(ref result, weight.boneIndex0, weight.weight0, firstSpriteBone, secondSpriteBone);
        AccumulateOtherWeight(ref result, weight.boneIndex1, weight.weight1, firstSpriteBone, secondSpriteBone);
        AccumulateOtherWeight(ref result, weight.boneIndex2, weight.weight2, firstSpriteBone, secondSpriteBone);
        AccumulateOtherWeight(ref result, weight.boneIndex3, weight.weight3, firstSpriteBone, secondSpriteBone);
        return Mathf.Clamp01(result);
    }

    public static float ClampChildWeight(BoneWeight sourceWeight, int firstSpriteBone, int secondSpriteBone, float childWeight)
    {
        var otherWeight = GetOtherWeight(sourceWeight, firstSpriteBone, secondSpriteBone);
        return Mathf.Clamp(childWeight, 0f, GetMainWeightCapacity(otherWeight));
    }

    public static BoneWeight BuildPreservingOthers(
        BoneWeight source,
        int firstSpriteBone,
        int secondSpriteBone,
        float secondWeight,
        int spriteBoneCount)
    {
        source = ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(source, spriteBoneCount, false, true);
        var otherInfluences = new Dictionary<int, float>(4);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex0, source.weight0, firstSpriteBone, secondSpriteBone, spriteBoneCount);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex1, source.weight1, firstSpriteBone, secondSpriteBone, spriteBoneCount);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex2, source.weight2, firstSpriteBone, secondSpriteBone, spriteBoneCount);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex3, source.weight3, firstSpriteBone, secondSpriteBone, spriteBoneCount);

        var allOtherSum = 0f;
        foreach (var influence in otherInfluences)
            allOtherSum += influence.Value;
        allOtherSum = Mathf.Clamp01(allOtherSum);

        var otherList = new List<KeyValuePair<int, float>>(otherInfluences);
        otherList.Sort((lhs, rhs) => rhs.Value.CompareTo(lhs.Value));
        if (otherList.Count > 2)
            otherList.RemoveRange(2, otherList.Count - 2);

        var selectedOtherSum = 0f;
        for (var i = 0; i < otherList.Count; i++)
            selectedOtherSum += otherList[i].Value;

        if (selectedOtherSum > 0.0001f && allOtherSum > 0f)
        {
            var scale = allOtherSum / selectedOtherSum;
            for (var i = 0; i < otherList.Count; i++)
                otherList[i] = new KeyValuePair<int, float>(otherList[i].Key, otherList[i].Value * scale);
        }

        var mainCapacity = GetMainWeightCapacity(allOtherSum);
        secondWeight = Mathf.Clamp(secondWeight, 0f, mainCapacity);
        var firstWeight = Mathf.Max(0f, mainCapacity - secondWeight);
        var fallbackIndex = firstSpriteBone >= 0 ? firstSpriteBone : 0;

        return new BoneWeight
        {
            boneIndex0 = firstSpriteBone >= 0 ? firstSpriteBone : fallbackIndex,
            weight0 = firstWeight,
            boneIndex1 = secondSpriteBone >= 0 ? secondSpriteBone : fallbackIndex,
            weight1 = secondWeight,
            boneIndex2 = otherList.Count > 0 ? otherList[0].Key : fallbackIndex,
            weight2 = otherList.Count > 0 ? otherList[0].Value : 0f,
            boneIndex3 = otherList.Count > 1 ? otherList[1].Key : fallbackIndex,
            weight3 = otherList.Count > 1 ? otherList[1].Value : 0f
        };
    }

    private static float GetMainWeightCapacity(float otherWeight)
    {
        return Mathf.Clamp01(1f - Mathf.Clamp01(otherWeight));
    }

    private static void AccumulateOtherWeight(ref float sum, int spriteBone, float weight, int firstSpriteBone, int secondSpriteBone)
    {
        if (spriteBone < 0 || weight <= 0.0001f || spriteBone == firstSpriteBone || spriteBone == secondSpriteBone)
            return;

        sum += Mathf.Max(0f, weight);
    }

    private static void AccumulateOtherInfluence(
        Dictionary<int, float> influences,
        int spriteBone,
        float weight,
        int firstSpriteBone,
        int secondSpriteBone,
        int spriteBoneCount)
    {
        if (influences == null || spriteBone < 0 || spriteBone == firstSpriteBone || spriteBone == secondSpriteBone || weight <= 0.0001f)
            return;
        if (spriteBoneCount > 0 && spriteBone >= spriteBoneCount)
            return;

        influences.TryGetValue(spriteBone, out var current);
        influences[spriteBone] = current + Mathf.Max(0f, weight);
    }
}
#endif
