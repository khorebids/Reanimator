#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorProjectorModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (context == null)
            return;

        var selectedKeys = candidateState.BuildSelectedKeys();
        var label =
            $"Projector\nmode={settings.Mode} selected={selectedKeys.Length}/{candidateState.CandidateCount}\n" +
            $"targetKey={(string.IsNullOrWhiteSpace(candidateState.CurrentTargetKey) ? "<none>" : candidateState.CurrentTargetKey)}\n" +
            $"clearWeights={settings.ClearTargetWeights} inPreview={settings.FilterWeightTargetsToPreview}\n" +
            $"stretch={settings.StretchMode} contour={settings.StretchKeepContour} cull={settings.StretchUseCull} dist={settings.StretchCullDistance:F2}";

        ReanimatorDebugPreviewGui.DrawLabel(context.LocalRect.position + new Vector2(8f, 58f), label, settings.Mode == ReanimatorProjectorMode.Stretch ? ReanimatorDebugPreviewGui.Segment : ReanimatorDebugPreviewGui.Neutral);
    }
}
#endif
