#if UNITY_EDITOR
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal static BoneWeight ElbowParentWeight(float progress, int upper, int helper)
    {
        float h = .5f * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress)));
        return BuildLimbTwoBoneWeight(upper, 1 - h, helper, h);
    }
    private static float ProjectElbowParentProgress(Vector2 point, Vector2[] contour)
    {
        float best = float.PositiveInfinity, result = 0;
        for (int i = 1; i < contour.Length; i++)
        {
            Vector2 delta = contour[i] - contour[i - 1];
            float t = Mathf.Clamp01(Vector2.Dot(point - contour[i - 1], delta) / Mathf.Max(delta.sqrMagnitude, 1e-10f));
            float distance = (point - Vector2.Lerp(contour[i - 1], contour[i], t)).sqrMagnitude;
            if (distance >= best) continue;
            best = distance; result = Mathf.Lerp(ElbowSampleDistance(i - 1), ElbowSampleDistance(i), t);
        }
        return result;
    }
    internal static bool TryEvaluateElbowParentBand(Vector2 point, Vector2[] a, Vector2[] b,
        int upper, int helperA, int helperB, out BoneWeight weight)
    {
        weight = default;
        for (int i = 1; i < a.Length; i++)
        {
            var a0 = ElbowParentWeight(ElbowSampleDistance(i - 1), upper, helperA);
            var a1 = ElbowParentWeight(ElbowSampleDistance(i), upper, helperA);
            var b0 = ElbowParentWeight(ElbowSampleDistance(i - 1), upper, helperB);
            var b1 = ElbowParentWeight(ElbowSampleDistance(i), upper, helperB);
            if (Triangle(point, a[i - 1], b[i - 1], b[i], out var bary))
            { weight = BlendBoneWeights(a0, bary.x, b0, bary.y, b1, bary.z, null); return true; }
            if (Triangle(point, a[i - 1], b[i], a[i], out bary))
            { weight = BlendBoneWeights(a0, bary.x, b1, bary.y, a1, bary.z, null); return true; }
        }
        return false;
        static bool Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 weights)
        {
            var u = b - a; var v = c - a; var q = p - a;
            float det = u.x * v.y - u.y * v.x;
            weights = default;
            if (Mathf.Abs(det) < 1e-10f) return false;
            float y = (q.x * v.y - q.y * v.x) / det, z = (u.x * q.y - u.y * q.x) / det;
            weights = new Vector3(1 - y - z, y, z);
            return weights.x >= -.00001f && weights.y >= -.00001f && weights.z >= -.00001f;
        }
    }
}
#endif
