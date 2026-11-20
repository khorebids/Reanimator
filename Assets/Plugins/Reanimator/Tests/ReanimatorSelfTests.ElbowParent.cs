#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    private static void ElbowParentBandIsContinuousAndScaleIndependent()
    {
        foreach (float scale in new[] { .1f, 1f, 200f })
        {
            float[] knots = { 0, 1f/15, 2f/15, .2f, .4f, .6f, .8f, 1 };
            Vector2[] a = knots.Select(t => new Vector2(-t * 2, 1) * scale).ToArray();
            Vector2[] b = knots.Select(t => new Vector2(-t * 2, -1) * scale).ToArray();
            AssertTrue(PsdLayerPreviewRenderer.TryEvaluateElbowParentBand(Vector2.zero, a, b, 0, 1, 2, out var seam), "Parent strip must include the entire joint cross-section.");
            AssertFloat(.5f, GetWeight(seam, 0), .0001f, "Parent side must match the existing elbow seam.");
            AssertFloat(.25f, GetWeight(seam, 1), .0001f, "Middle must blend both helpers continuously.");
            AssertFloat(.25f, GetWeight(seam, 2), .0001f, "Middle must blend both helpers continuously.");
            float previous = 1;
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                var w = PsdLayerPreviewRenderer.ElbowParentWeight(t, 0, 1);
                AssertFloat(1, w.weight0 + w.weight1 + w.weight2 + w.weight3, .0001f, "Parent weights must remain normalized.");
                AssertTrue(GetWeight(w, 1) <= previous + .00001f, "Helper must fade toward the parent."); previous = GetWeight(w, 1);
            }
            AssertTrue(PsdLayerPreviewRenderer.TryEvaluateElbowParentBand(new Vector2(-2, 0) * scale, a, b, 0, 1, 2, out var end), "Parent transition endpoint must be included.");
            AssertFloat(1, GetWeight(end, 0), .0001f, "Far endpoint must be fully attached to parent.");
            AssertFalse(PsdLayerPreviewRenderer.TryEvaluateElbowParentBand(new Vector2(-3, 0) * scale, a, b, 0, 1, 2, out _), "Vertices outside the parent transition must not be touched.");
        }
    }
}
#endif
