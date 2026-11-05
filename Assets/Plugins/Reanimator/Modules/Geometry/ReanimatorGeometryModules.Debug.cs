#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorAutoGeometryModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "AutoGeometry", "overlay=generated geometry\ninput=selected sprite", ReanimatorDebugPreviewGui.Neutral);
    }
}

internal sealed partial class ReanimatorEditGeometryModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "EditGeometry", "overlay=selected vertices\ninput=move/select vertices", ReanimatorDebugPreviewGui.Neutral);
    }
}

internal sealed partial class ReanimatorCreateEdgeModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "CreateEdge", "overlay=geometry vertices\ninput=create edge", ReanimatorDebugPreviewGui.Segment);
    }
}

internal sealed partial class ReanimatorSplitEdgeModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "SplitEdge", "overlay=geometry edges\ninput=split edge", ReanimatorDebugPreviewGui.Segment);
    }
}

internal sealed partial class ReanimatorElbowModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "Elbow", "overlay=elbow handles\ninput=drag elbow points", ReanimatorDebugPreviewGui.Selected);
    }
}

internal sealed partial class ReanimatorLimbsModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "Limbs", "overlay=limb lines/cull preview\ninput=drag limb handles", ReanimatorDebugPreviewGui.Selected);
    }
}

internal sealed partial class ReanimatorJointModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "Joint", "overlay=joint radius/long points\ninput=joint geometry", ReanimatorDebugPreviewGui.Hot);
    }
}

internal sealed partial class ReanimatorBoobModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "Boob", "overlay=boob preview handles\ninput=drag boob points", ReanimatorDebugPreviewGui.Selected);
    }
}
#endif
