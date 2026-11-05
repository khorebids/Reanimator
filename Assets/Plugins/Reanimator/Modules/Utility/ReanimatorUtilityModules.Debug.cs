#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorCircleCleanModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "CircleClean", "overlay=radius cull circle\ninput=radius/vertex cull", ReanimatorDebugPreviewGui.Selected);
    }
}
#endif
