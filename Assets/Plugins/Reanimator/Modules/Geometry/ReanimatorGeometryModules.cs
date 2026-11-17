#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

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
    private static readonly string[] Modes = { "Simple", "Auto", "Belly" };

    public ReanimatorSoftContourModule()
        : base("geometry.soft-contour", "Geometry", 30, 60, "SC", ReanimatorToolMode.SoftContour, 30,
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

        if (api?.PreviewRenderer is { SoftContourDrawnBellyMode: true } preview)
            preview.SelectSoftContourBellyBone();
        base.Invoke(api);
    }

    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => true;
    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (api?.PreviewRenderer == null || context == null) return;
        var preview = api.PreviewRenderer;
        var handled = preview.SoftContourDrawnBellyMode
            ? preview.HandleSoftContourBellyInput(context)
            : !preview.SoftContourAutoMode && preview.HandleSoftContourPointInput(context);
        if (handled)
            api.Repaint();
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (api?.PreviewRenderer is { } renderer)
            renderer.DrawSoftContour(context?.Coordinates);
    }

    private static void DrawPanel(Reanimator.SystemApi api, float availableHeight)
    {
        EditorGUILayout.LabelField("Soft Contour", EditorStyles.boldLabel);
        var preview = api?.PreviewRenderer;
        if (preview == null)
            return;

        var currentMode = preview.SoftContourDrawnBellyMode ? 2 : preview.SoftContourAutoMode ? 1 : 0;
        var mode = GUILayout.Toolbar(currentMode, Modes);
        if (mode != currentMode)
        {
            preview.SoftContourDrawnBellyMode = mode == 2;
            preview.SoftContourAutoMode = mode == 1;
            preview.SelectingSoftContourPoints = false;
            preview.CancelSoftContourBellyStroke();
            if (mode == 2)
                preview.SelectSoftContourBellyBone();
            api.Repaint();
        }

        if (mode == 2)
        {
            if (!preview.HasSoftContourBellyBone)
                EditorGUILayout.HelpBox("Select the parent bone in the preview.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(preview.IsDrawingSoftContourBelly ? "Drawing contour" : "Draw contour"))
                {
                    if (preview.IsDrawingSoftContourBelly)
                        preview.CancelSoftContourBellyStroke();
                    else
                        preview.IsDrawingSoftContourBelly = true;
                    api.Repaint();
                }

                if (GUILayout.Button("Clear"))
                {
                    preview.ClearSoftContourBellyContour();
                    api.Repaint();
                }
            }

            using (new EditorGUI.DisabledScope(!preview.HasSoftContourBellyContour || !preview.HasSoftContourBellyBone))
            {
                if (GUILayout.Button("Apply"))
                {
                    preview.ApplySoftContourBelly(out var message);
                    api.ShowNotification(message);
                    api.Repaint();
                }
            }

            return;
        }

        if (mode == 1)
        {
            EditorGUI.BeginChangeCheck();
            var angle = EditorGUILayout.Slider("Max End Angle", preview.SoftContourMaxEndAngle, 0f, 180f);
            var splitAngle = EditorGUILayout.Slider("Split Angle", preview.SoftContourSplitAngle, 0f, 180f);
            var autoDepth = EditorGUILayout.Slider("Depth", preview.SoftContourDepth, 0.01f, 0.99f);
            var hips = EditorGUILayout.Toggle("SC hips", preview.SoftContourHipsMode);
            if (EditorGUI.EndChangeCheck())
            {
                preview.SoftContourMaxEndAngle = angle;
                preview.SoftContourSplitAngle = splitAngle;
                preview.SoftContourDepth = autoDepth;
                preview.SoftContourHipsMode = hips;
                api.Repaint();
            }

            using (new EditorGUI.DisabledScope(!preview.CanApplyAutoSoftContour))
            {
                if (GUILayout.Button("Apply"))
                {
                    preview.ApplySoftContour(out var message);
                    api.ShowNotification(message);
                    api.Repaint();
                }
            }
            return;
        }
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

internal sealed class ReanimatorVGenModule : ReanimatorToolModeModule, IReanimatorPreviewModule
{
    public ReanimatorVGenModule()
        : base("geometry.v-gen", "Geometry", 30, 65, "V-gen", ReanimatorToolMode.VGen, 30, DrawPanel)
    {
    }

    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => false;

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (api?.PreviewRenderer?.HandleVGenInput(context) == true)
            api.Repaint();
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        api?.PreviewRenderer?.DrawVGenContours(context?.Coordinates);
    }

    private static void DrawPanel(Reanimator.SystemApi api, float availableHeight)
    {
        var renderer = api?.PreviewRenderer;
        if (renderer == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(renderer.IsDrawingVGenSide(0) ? "Drawing left" : "Draw left"))
                renderer.SetVGenSide(0);
            if (GUILayout.Button(renderer.IsDrawingVGenSide(1) ? "Drawing right" : "Draw right"))
                renderer.SetVGenSide(1);
            if (GUILayout.Button("Clear"))
                renderer.ClearVGenContours();
        }

        using (new EditorGUI.DisabledScope(!renderer.HasVGenContours))
        {
            if (!GUILayout.Button("Apply V-gen")) return;
            renderer.ApplyVGen(out var message);
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
