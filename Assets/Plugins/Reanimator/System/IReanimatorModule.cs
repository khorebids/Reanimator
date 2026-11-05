#if UNITY_EDITOR
internal interface IReanimatorModule
{
    string Id { get; }
    string Section { get; }
    int SectionOrder { get; }
    int ButtonOrder { get; }
    int RowGroup { get; }
    string Label(Reanimator.SystemApi api);
    bool IsEnabled(Reanimator.SystemApi api);
    bool IsActive(Reanimator.SystemApi api);
    void Invoke(Reanimator.SystemApi api);
    bool DrawSettings(Reanimator.SystemApi api, float availableHeight);
}
#endif
