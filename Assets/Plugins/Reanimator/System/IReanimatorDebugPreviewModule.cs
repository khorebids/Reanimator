#if UNITY_EDITOR
internal interface IReanimatorDebugPreviewModule : IReanimatorModule
{
    void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context);
}
#endif
