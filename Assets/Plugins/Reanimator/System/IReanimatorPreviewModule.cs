#if UNITY_EDITOR
internal interface IReanimatorPreviewModule : IReanimatorModule
{
    bool AllowBoneInputAfterPreviewInput { get; }
    bool OverridesSelectedGeometryPreview { get; }
    void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context);
    void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context);
}

internal readonly struct ReanimatorBoneInputOptions
{
    public readonly int MouseButton;
    public readonly bool ForceRotation;

    public ReanimatorBoneInputOptions(int mouseButton, bool forceRotation)
    {
        MouseButton = mouseButton;
        ForceRotation = forceRotation;
    }
}

internal interface IReanimatorBoneInputModule
{
    ReanimatorBoneInputOptions BoneInputOptions { get; }
}
#endif
