#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed partial class ReanimatorCircleCleanModule : ReanimatorToolModeModule
{
    public ReanimatorCircleCleanModule()
        : base("geometry.circle-clean", "Geometry", 30, 80, "CirCl", ReanimatorToolMode.RadiusCull, 40, (api, height) => api?.DrawRadiusCullSettings(height))
    {
    }
}

internal sealed partial class ReanimatorVertexCullModule : ReanimatorModuleBase, IReanimatorPreviewModule
{
    private float distance = 4f;

    public override string Id => "geometry.vertex-cull";
    public override string Section => "Geometry";
    public override int SectionOrder => 30;
    public override int ButtonOrder => 90;
    public override int RowGroup => 40;
    public override string Label(Reanimator.SystemApi api) => "Cull";
    public override bool IsActive(Reanimator.SystemApi api) => api != null && api.ActiveToolMode == ReanimatorToolMode.VertexCull;
    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => false;

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.SetToolMode(ReanimatorToolMode.VertexCull);
    }

    public override bool DrawSettings(Reanimator.SystemApi api, float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Vertex Cull", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Merge close geometry vertices on the selected sprite.", EditorStyles.wordWrappedMiniLabel);

            var layerName = string.Empty;
            var vertexCount = 0;
            var mergeGroupCount = 0;
            var removedCount = 0;
            var error = api == null ? "Preview unavailable" : null;
            var canAnalyze = api != null && api.TryAnalyzeVertexCull(
                distance,
                out layerName,
                out vertexCount,
                out mergeGroupCount,
                out removedCount,
                out error);

            if (!string.IsNullOrWhiteSpace(layerName))
                EditorGUILayout.LabelField("Sprite", $"{layerName} ({vertexCount})");
            else if (!string.IsNullOrWhiteSpace(error))
                EditorGUILayout.LabelField(error, EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Distance", GUILayout.Width(58f));
                EditorGUI.BeginChangeCheck();
                distance = Mathf.Max(0.01f, GUILayout.HorizontalSlider(distance, 0.01f, 128f));
                distance = EditorGUILayout.FloatField(distance, GUILayout.Width(58f));
                distance = Mathf.Clamp(distance, 0.01f, 128f);
                if (EditorGUI.EndChangeCheck())
                {
                    GUI.changed = true;
                    api?.Repaint();
                }
            }

            GUILayout.Space(4f);
            if (canAnalyze && string.IsNullOrWhiteSpace(error))
                EditorGUILayout.LabelField($"Groups: {mergeGroupCount}   Remove: {removedCount}", EditorStyles.miniLabel);
            else if (!string.IsNullOrWhiteSpace(error))
                EditorGUILayout.LabelField(error, EditorStyles.wordWrappedMiniLabel);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(api == null || !canAnalyze || removedCount <= 0))
            {
                if (GUILayout.Button("Cull", GUILayout.Height(28f)))
                {
                    if (api.ApplyVertexCull(distance, out var appliedGroups, out var appliedRemoved, out _, out var applyError))
                        api.ShowNotification($"Vertex Cull: {appliedGroups} groups, {appliedRemoved} vertices");
                    else
                        api.ShowNotification(string.IsNullOrWhiteSpace(applyError) ? "Vertex Cull failed" : applyError);

                    api.Repaint();
                }
            }
        }

        return true;
    }

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        var e = Event.current;
        if (api == null || context == null || e == null)
            return;
        if (e.type != EventType.ScrollWheel || !context.LocalRect.Contains(e.mousePosition))
            return;

        var scroll = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
        var step = Mathf.Max(0.05f, distance * 0.08f);
        distance = Mathf.Clamp(distance - scroll * step, 0.01f, 128f);
        e.Use();
        GUI.changed = true;
        api.Repaint();
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        api?.DrawVertexCullPreviewOverlay(context, distance);
        DrawDistanceLabel(context);
    }

    private void DrawDistanceLabel(ReanimatorPreviewContext context)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || context == null)
            return;

        Handles.BeginGUI();
        var rect = new Rect(context.LocalRect.x + 8f, context.LocalRect.y + 8f, 128f, 20f);
        var prevColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, $"Distance {distance:0.##}", EditorStyles.miniLabel);
        GUI.color = prevColor;
        Handles.EndGUI();
    }
}

internal sealed class ReanimatorDebugModeModule : ReanimatorModuleBase
{
    public override string Id => "utility.debug-mode";
    public override string Section => "Utility";
    public override int SectionOrder => 50;
    public override int ButtonOrder => 20;
    public override string Label(Reanimator.SystemApi api) => api != null ? api.GetDebugModeButtonLabel() : "Debug [OFF]";

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.ExitActiveTool();
        api?.ToggleDebugModeByDoublePress();
    }
}

internal sealed class ReanimatorGenerateMissingGeometryModule : ReanimatorActionModule
{
    public ReanimatorGenerateMissingGeometryModule()
        : base("utility.generate-missing-geometry", "Utility", 50, 40, "Generate Missing Geometry")
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.ExitActiveTool();
        api?.GenerateMissingGeometry();
    }
}

internal sealed class ReanimatorFixSkinWeightsModule : ReanimatorActionModule
{
    public ReanimatorFixSkinWeightsModule()
        : base("utility.fix-skin-weights", "Utility", 50, 45, "Fix Skin Weights")
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.ExitActiveTool();
        api?.FixSelectedSpriteSkinWeights();
    }
}

internal sealed class ReanimatorKillSelectedSpriteWeightsModule : ReanimatorActionModule
{
    public ReanimatorKillSelectedSpriteWeightsModule()
        : base("utility.kill-selected-sprite-weights", "Utility", 50, 47, "Kill Weights")
    {
    }

    public override bool IsEnabled(Reanimator.SystemApi api)
    {
        return api != null && api.HasSelectedSprite();
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        if (api == null)
            return;

        api.ExitActiveTool();
        if (api.KillSelectedSpriteWeights(out var layerName, out var clearedCount, out var error))
            api.ShowNotification($"Kill Weights: {layerName} ({clearedCount})");
        else if (!string.IsNullOrWhiteSpace(error))
            api.ShowNotification(error);
    }
}

internal sealed class ReanimatorDeleteBonesModule : ReanimatorModuleBase
{
    public override string Id => "utility.delete-bones";
    public override string Section => "Utility";
    public override int SectionOrder => 50;
    public override int ButtonOrder => 50;
    public override int RowGroup => 10;
    public override string Label(Reanimator.SystemApi api) => api != null ? api.GetDeleteAllBonesButtonLabel() : "DelBon x5";

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.ExitActiveTool();
        api?.PressDeleteAllBones();
    }
}

internal sealed class ReanimatorCopyBonesModule : ReanimatorActionModule
{
    public ReanimatorCopyBonesModule()
        : base("utility.copy-bones", "Utility", 50, 60, "CopyBon", 10)
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.ExitActiveTool();
        api?.CopyBonesFromAnotherPsd();
    }
}

internal sealed class ReanimatorForceCleanCacheModule : ReanimatorActionModule
{
    public ReanimatorForceCleanCacheModule()
        : base("utility.refresh-cache", "Utility", 50, 70, "Refresh Cache")
    {
    }

    public override void Invoke(Reanimator.SystemApi api)
    {
        api?.ExitActiveTool();
        api?.ForceCleanPreviewCache();
    }
}
#endif
