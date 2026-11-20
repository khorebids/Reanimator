using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ReaniIKBodyScale
{
    public const float MinimumSize = .5f;
    public const float MaximumSize = 1.1f;
    [Range(MinimumSize, MaximumSize), Tooltip("The single Belly scene slider controls this value. 1 restores the unscaled pose.")]
    public float size = 1f;
    [Tooltip("X: belly=0, spine=1/3, neck=2/3, head=1. Y: local scale influence. Inherited scale accumulates along the hierarchy.")]
    public AnimationCurve distribution = AnimationCurve.Linear(0, 1, 1, 1);
    [Range(0, 1), Tooltip("0: arms inherit the body size. 1: cancel the combined size multiplier of all scaled ancestors of each arm.")]
    public float back_arms = 1f;
    [Range(0, 1), Tooltip("0: inherit added height. 1: cancel the vertical displacement caused by body scaling. Higher bones receive more compensation. Uses the rig's up direction.")]
    public float back_height = 1f;

    private static readonly string[] Names = { "belly", "spine", "neck", "head" };
    [NonSerialized] private Transform boundRoot;
    [NonSerialized] private readonly Transform[] chain = new Transform[Names.Length];
    [NonSerialized] private readonly float[] factors = new float[Names.Length];
    [NonSerialized] private readonly Vector3[] originalPositions = new Vector3[Names.Length];
    [NonSerialized] private readonly Vector3[] scaledPositions = new Vector3[Names.Length];
    [Serializable] private struct AppliedScale { public Transform bone; public Vector3 before, after; public float factor; }
    [Serializable] private struct AppliedPosition { public Transform bone; public Vector3 before, after; }
    // Keep the undo of a procedural pose together with its serialized transforms,
    // including inspector serialization, Undo and domain reloads.
    [SerializeField, HideInInspector] private List<AppliedScale> applied = new List<AppliedScale>(9);
    [SerializeField, HideInInspector] private List<AppliedPosition> moved = new List<AppliedPosition>(4);

    public void Restore()
    {
        for (int i = moved.Count - 1; i >= 0; i--)
        {
            var entry = moved[i];
            if (!entry.bone) continue;
            if (entry.bone.localPosition == entry.after) entry.bone.localPosition = entry.before;
            else if (entry.bone.localPosition != entry.before)
                entry.bone.localPosition -= entry.after - entry.before;
        }
        moved.Clear();
        for (int i = applied.Count - 1; i >= 0; i--)
        {
            var entry = applied[i];
            if (!entry.bone) continue;
            if (entry.bone.localScale == entry.after) entry.bone.localScale = entry.before;
            else if (entry.bone.localScale != entry.before && entry.factor > 0)
                entry.bone.localScale /= entry.factor;
        }
        applied.Clear();
    }

    public void Apply(ReaniIK rig)
    {
        Restore();
        if (boundRoot != rig.transform)
        {
            boundRoot = rig.transform;
            Array.Clear(chain, 0, chain.Length);
            foreach (var bone in boundRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.GetComponent<SpriteRenderer>()) continue;
                for (int i = 0; i < Names.Length; i++)
                    if (!chain[i] && string.Equals(bone.name, Names[i], StringComparison.OrdinalIgnoreCase)) chain[i] = bone;
            }
        }
        float delta = Mathf.Clamp(size, MinimumSize, MaximumSize) - 1f;
        for (int i = 0; i < chain.Length; i++)
            if (chain[i]) originalPositions[i] = chain[i].position;
        for (int i = 0; i < chain.Length; i++)
        {
            float influence = distribution == null ? 1f : Mathf.Clamp01(distribution.Evaluate(i / (float)(chain.Length - 1)));
            factors[i] = 1f + delta * influence;
            Scale(chain[i], factors[i]);
        }
        Compensate(rig.leftArm.upper);
        Compensate(rig.rightArm.upper);
        Compensate(rig.leftArm2.upper);
        Compensate(rig.rightArm2.upper);
        CompensateHeight(rig.transform.up);
    }

    private void CompensateHeight(Vector3 up)
    {
        float strength = Mathf.Clamp01(back_height);
        if (strength <= 0) return;
        // Capture all scaled positions before moving parents, otherwise their
        // displacement would be counted again when correcting each child.
        for (int i = 0; i < chain.Length; i++)
            if (chain[i]) scaledPositions[i] = chain[i].position;
        for (int i = 0; i < chain.Length; i++)
        {
            var bone = chain[i];
            if (!bone) continue;
            float addedHeight = Vector3.Dot(scaledPositions[i] - originalPositions[i], up);
            Vector3 desired = scaledPositions[i] - up * (addedHeight * strength);
            if ((desired - bone.position).sqrMagnitude < 1e-12f) continue;
            var entry = new AppliedPosition { bone = bone, before = bone.localPosition };
            bone.position = desired;
            entry.after = bone.localPosition;
            moved.Add(entry);
        }
    }

    private void Compensate(Transform arm)
    {
        if (!arm || back_arms <= 0) return;
        foreach (var entry in applied) if (entry.bone == arm) return;
        float inherited = 1f;
        for (int i = 0; i < chain.Length; i++)
            if (chain[i] && arm != chain[i] && arm.IsChildOf(chain[i])) inherited *= factors[i];
        Scale(arm, Mathf.Lerp(1f, 1f / inherited, Mathf.Clamp01(back_arms)));
    }

    private void Scale(Transform bone, float factor)
    {
        if (!bone || Mathf.Approximately(factor, 1f)) return;
        var entry = new AppliedScale { bone = bone, before = bone.localScale, after = bone.localScale * factor, factor = factor };
        bone.localScale = entry.after;
        applied.Add(entry);
    }
}
