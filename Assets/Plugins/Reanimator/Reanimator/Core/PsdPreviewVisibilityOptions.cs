#if UNITY_EDITOR
using UnityEngine;

public sealed class PsdPreviewVisibilityOptions
{
    public float SpriteAlpha = 1f;
    public float BoneAlpha = 1f;
    public float VertexAlpha = 1f;

    public void Sanitize()
    {
        SpriteAlpha = Mathf.Clamp01(SpriteAlpha);
        BoneAlpha = Mathf.Clamp01(BoneAlpha);
        VertexAlpha = Mathf.Clamp01(VertexAlpha);
    }
}
#endif
