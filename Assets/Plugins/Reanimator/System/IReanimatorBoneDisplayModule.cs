#if UNITY_EDITOR
internal interface IReanimatorBoneDisplayModule : IReanimatorModule
{
    bool TryGetBoneDisplayOverride(
        Reanimator.SystemApi api,
        int rendererBoneIndex,
        out ReanimatorBoneDisplayOverride displayOverride);
}
#endif
