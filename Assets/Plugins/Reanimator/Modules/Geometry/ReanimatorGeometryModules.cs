#if UNITY_EDITOR
internal sealed partial class ReanimatorAutoGeometryModule : ReanimatorToolModeModule
{
    public ReanimatorAutoGeometryModule()
        : base("geometry.auto", "Geometry", 30, 10, "Auto (G)", ReanimatorToolMode.AutoGeometry, 10, (api, height) => api?.DrawAutoGeometrySettings(height))
    {
    }
}

internal sealed partial class ReanimatorEditGeometryModule : ReanimatorToolModeModule
{
    public ReanimatorEditGeometryModule()
        : base("geometry.edit", "Geometry", 30, 20, "Edit (V)", ReanimatorToolMode.EditGeometry, 10, (api, height) => api?.DrawEditGeometrySettings(height))
    {
    }
}

internal sealed partial class ReanimatorSplitEdgeModule : ReanimatorToolModeModule
{
    public ReanimatorSplitEdgeModule()
        : base("geometry.split-edge", "Edge", 35, 20, "Split", ReanimatorToolMode.SplitEdge, 10, (api, height) => api?.DrawSplitEdgeSettings(height))
    {
    }
}

internal sealed partial class ReanimatorCreateEdgeModule : ReanimatorToolModeModule
{
    public ReanimatorCreateEdgeModule()
        : base("geometry.create-edge", "Edge", 35, 10, "Create", ReanimatorToolMode.CreateEdge, 10, (api, height) => api?.DrawCreateEdgeSettings(height))
    {
    }
}

internal sealed partial class ReanimatorElbowModule : ReanimatorToolModeModule
{
    public ReanimatorElbowModule()
        : base("geometry.elbow", "Geometry", 30, 110, "Elbow", ReanimatorToolMode.Elbow, 70, (api, height) => api?.DrawElbowSettings(height))
    {
    }
}

internal sealed partial class ReanimatorLimbsModule : ReanimatorToolModeModule
{
    public ReanimatorLimbsModule()
        : base("geometry.limbs", "Geometry", 30, 30, "Limbs", ReanimatorToolMode.Limbs, 20, (api, height) => api?.DrawLimbsSettings(height))
    {
    }
}

internal sealed partial class ReanimatorJointModule : ReanimatorToolModeModule
{
    public ReanimatorJointModule()
        : base("geometry.joint", "Geometry", 30, 40, "Joint", ReanimatorToolMode.Joint, 20, (api, height) => api?.DrawJointSettings(height))
    {
    }
}

internal sealed partial class ReanimatorBoobModule : ReanimatorToolModeModule
{
    public ReanimatorBoobModule()
        : base("geometry.boob", "Geometry", 30, 50, "Boob", ReanimatorToolMode.Boob, 30, (api, height) => api?.DrawBoobSettings(height))
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        if (api == null)
            return;

        if (api.ActiveToolMode == ReanimatorToolMode.Boob)
        {
            api.ExitActiveTool();
            api.Repaint();
            return;
        }

        api.SetToolMode(ReanimatorToolMode.Boob);
    }
}

internal sealed class ReanimatorSoftContourModule : ReanimatorToolModeModule, IReanimatorPreviewModule
{
    public ReanimatorSoftContourModule()
        : base("geometry.soft-contour", "Geometry", 30, 130, "SC", ReanimatorToolMode.SoftContour, 80,
            DrawPanel)
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        if (api?.ActiveToolMode == ReanimatorToolMode.SoftContour)
        {
            api.ExitActiveTool();
            api.Repaint();
            return;
        }

        if (api == null) return;
        if (api.PreviewRenderer == null || !api.PreviewRenderer.HasSoftContourSelection())
        {
            api.ShowNotification("SC: select a sprite and a bone.");
            return;
        }
        base.Invoke(api);
    }

    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => true;
    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (api?.PreviewRenderer == null || context == null) return;
        if (api.PreviewRenderer.HandleSoftContourPointInput(context))
            api.Repaint();
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        api?.PreviewRenderer?.DrawSoftContour(context?.Coordinates);
    }

    private static void DrawPanel(Reanimator.SystemApi api, float availableHeight)
    {
        UnityEditor.EditorGUILayout.LabelField("Soft Contour", UnityEditor.EditorStyles.boldLabel);
        if (api?.PreviewRenderer == null || !api.PreviewRenderer.HasSoftContourSelection())
            UnityEditor.EditorGUILayout.HelpBox("Select a sprite and a bone.", UnityEditor.MessageType.Info);
        else if (UnityEngine.GUILayout.Button(api.PreviewRenderer.SelectingSoftContourPoints
                     ? "Select Points [ON]" : "Select Points"))
        {
            api.PreviewRenderer.SelectingSoftContourPoints = !api.PreviewRenderer.SelectingSoftContourPoints;
            api.Repaint();
        }
        if (api?.PreviewRenderer == null || !api.PreviewRenderer.HasSoftContourSelection() ||
            !api.PreviewRenderer.HasSelectedSoftContourPoints) return;
        var renderer = api.PreviewRenderer;
        float depth = UnityEditor.EditorGUILayout.Slider("Depth", renderer.SoftContourDepth, 0.01f, 0.99f);
        if (depth != renderer.SoftContourDepth)
        {
            renderer.SoftContourDepth = depth;
            api.Repaint();
        }
        if (UnityEngine.GUILayout.Button("SC"))
        {
            renderer.ApplySoftContour(out var message);
            api.ShowNotification(message);
            api.Repaint();
        }
    }
}

internal sealed class ReanimatorRetriangulateGeometryModule : ReanimatorActionModule
{
    public ReanimatorRetriangulateGeometryModule()
        : base("geometry.retriangulate", "Geometry", 30, 85, "ReTri", 40)
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        if (api == null)
            return;

        if (api.RetriangulateSelectedGeometry(out var layerName, out var triangleCount, out var error))
        {
            api.ShowNotification($"ReTri: {layerName} ({triangleCount})");
            api.Repaint();
            return;
        }

        api.ShowNotification(string.IsNullOrWhiteSpace(error) ? "ReTri failed" : error);
    }
}

#endif
