#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorPreviewPoseModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "PreviewPose", "overlay=pose preview\ninput=pose/test animation", ReanimatorDebugPreviewGui.Neutral);
    }
}
#endif
