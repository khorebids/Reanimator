#if UNITY_EDITOR
internal sealed partial class ReanimatorFaceModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        ReanimatorToolDebugOverlay.Draw(context, "Face", "layout=Eyes/Mouth/Earrings\nautomation=placeholder", ReanimatorDebugPreviewGui.Neutral);
    }
}
#endif
