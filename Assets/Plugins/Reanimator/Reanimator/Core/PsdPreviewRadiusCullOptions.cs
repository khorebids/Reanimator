#if UNITY_EDITOR
using UnityEngine;

public sealed class PsdPreviewRadiusCullOptions
{
    public float Radius = 0.01f;
    public bool RemoveContourVertices = true;
    public bool RemoveInnerVertices = true;
    public bool JointOverlayActive;
    public bool LimbOverlayActive;
    public bool LimbHandleInputActive;

    public void Sanitize()
    {
        Radius = Mathf.Max(0.01f, Radius);
    }
}
#endif
