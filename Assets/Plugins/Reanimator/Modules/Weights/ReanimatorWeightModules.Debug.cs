#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorCurveModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "WeightCurve", "overlay=weight curve graph\ninput=curve handles", ReanimatorDebugPreviewGui.Neutral);
    }
}

internal sealed partial class ReanimatorSliderModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "WeightSlider", "overlay=slider vertex weights\ninput=weight slider", ReanimatorDebugPreviewGui.Neutral);
    }
}

internal sealed partial class ReanimatorBoneInfluenceModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "BoneInfluence", "overlay=selected influence vertices\ninput=bone influence edit", ReanimatorDebugPreviewGui.Segment);
    }
}

internal sealed partial class ReanimatorSpriteInfluenceModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "SpriteInfluence", "overlay=selected influence vertices\ninput=sprite influence edit", ReanimatorDebugPreviewGui.Segment);
    }
}
#endif
