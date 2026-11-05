#if UNITY_EDITOR
using UnityEngine;

public sealed class PsdPreviewJointOptions
{
    public int AlgorithmMode;
    public AnimationCurve WeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve SpecialWeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public float SpecialJointDistance = 0.5f;
    public float BoneBaseOffset = 0.5f;
    public float CPairOffset = 8f;
    public int LongPoints = 4;
    public float StartLongPointsOffset = 12f;
    public float EndLongPointsOffset = 12f;

    public void Sanitize()
    {
        AlgorithmMode = Mathf.Clamp(AlgorithmMode, 0, 1);
        if (WeightCurve == null || WeightCurve.length == 0)
        {
            WeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
        if (SpecialWeightCurve == null || SpecialWeightCurve.length == 0)
            SpecialWeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        SpecialJointDistance = Mathf.Clamp01(SpecialJointDistance);
        BoneBaseOffset = Mathf.Clamp01(BoneBaseOffset);
        CPairOffset = Mathf.Max(0f, CPairOffset);
        LongPoints = Mathf.Max(0, LongPoints);
        StartLongPointsOffset = Mathf.Max(0f, StartLongPointsOffset);
        EndLongPointsOffset = Mathf.Max(0f, EndLongPointsOffset);
    }
}
#endif
