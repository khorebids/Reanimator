#if UNITY_EDITOR
using UnityEngine;

internal readonly struct ReanimatorBoneDisplayOverride
{
    public ReanimatorBoneDisplayOverride(Color color, float alphaMultiplier)
    {
        Color = color;
        AlphaMultiplier = Mathf.Clamp01(alphaMultiplier);
    }

    public Color Color { get; }
    public float AlphaMultiplier { get; }
}
#endif
