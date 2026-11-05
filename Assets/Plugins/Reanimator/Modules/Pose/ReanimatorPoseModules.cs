#if UNITY_EDITOR
internal sealed partial class ReanimatorPreviewPoseModule : ReanimatorToolModeModule
{
    public ReanimatorPreviewPoseModule()
        : base("pose.preview", "Pose", 10, 10, "Prew (Q)", ReanimatorToolMode.PreviewPose, 10, (api, height) => api?.DrawPreviewPoseSettings(height))
    {
    }
}

internal sealed class ReanimatorRestorePoseModule : ReanimatorActionModule
{
    public ReanimatorRestorePoseModule()
        : base("pose.restore", "Pose", 10, 20, "Restore (E)", 10)
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.RestorePreviewPose();
    }
}
#endif
