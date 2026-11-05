#if UNITY_EDITOR
using System;

internal abstract class ReanimatorModuleBase : IReanimatorModule
{
    public abstract string Id { get; }
    public abstract string Section { get; }
    public virtual int SectionOrder => 100;
    public virtual int ButtonOrder => 100;
    public virtual int RowGroup => 0;
    public abstract string Label(Reanimator.SystemApi api);
    public virtual bool IsEnabled(Reanimator.SystemApi api) => true;
    public virtual bool IsActive(Reanimator.SystemApi api) => false;
    public abstract void Invoke(Reanimator.SystemApi api);
    public virtual bool DrawSettings(Reanimator.SystemApi api, float availableHeight) => false;
}

internal class ReanimatorToolModeModule : ReanimatorModuleBase
{
    private readonly string id;
    private readonly string section;
    private readonly int sectionOrder;
    private readonly int buttonOrder;
    private readonly int rowGroup;
    private readonly string label;
    private readonly ReanimatorToolMode mode;
    private readonly Action<Reanimator.SystemApi, float> drawSettings;

    public ReanimatorToolModeModule(
        string id,
        string section,
        int sectionOrder,
        int buttonOrder,
        string label,
        ReanimatorToolMode mode,
        int rowGroup = 0,
        Action<Reanimator.SystemApi, float> drawSettings = null)
    {
        this.id = id;
        this.section = section;
        this.sectionOrder = sectionOrder;
        this.buttonOrder = buttonOrder;
        this.label = label;
        this.mode = mode;
        this.rowGroup = rowGroup;
        this.drawSettings = drawSettings;
    }

    public override string Id => id;
    public override string Section => section;
    public override int SectionOrder => sectionOrder;
    public override int ButtonOrder => buttonOrder;
    public override int RowGroup => rowGroup;
    public override string Label(Reanimator.SystemApi api) => label;
    public override bool IsActive(Reanimator.SystemApi api) => api != null && api.ActiveToolMode == mode;
    public override void Invoke(Reanimator.SystemApi api) => api?.SetToolMode(mode);
    public override bool DrawSettings(Reanimator.SystemApi api, float availableHeight)
    {
        if (drawSettings == null)
            return false;

        drawSettings(api, availableHeight);
        return true;
    }
}

internal abstract class ReanimatorActionModule : ReanimatorModuleBase
{
    private readonly string id;
    private readonly string section;
    private readonly int sectionOrder;
    private readonly int buttonOrder;
    private readonly int rowGroup;
    private readonly string label;

    protected ReanimatorActionModule(
        string id,
        string section,
        int sectionOrder,
        int buttonOrder,
        string label,
        int rowGroup = 0)
    {
        this.id = id;
        this.section = section;
        this.sectionOrder = sectionOrder;
        this.buttonOrder = buttonOrder;
        this.label = label;
        this.rowGroup = rowGroup;
    }

    public override string Id => id;
    public override string Section => section;
    public override int SectionOrder => sectionOrder;
    public override int ButtonOrder => buttonOrder;
    public override int RowGroup => rowGroup;
    public override string Label(Reanimator.SystemApi api) => label;
}
#endif
