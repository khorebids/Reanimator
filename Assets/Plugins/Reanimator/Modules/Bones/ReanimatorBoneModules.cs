#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

internal sealed partial class ReanimatorEditBoneModule : ReanimatorToolModeModule
{
    public ReanimatorEditBoneModule()
        : base("bones.edit", "Bones", 20, 20, "Edit", ReanimatorToolMode.EditBone, 10, (api, height) => api?.DrawEditBoneSettings(height))
    {
    }
}

internal sealed partial class ReanimatorCreateBoneModule : ReanimatorToolModeModule, IReanimatorPreviewModule
{
    private bool isCreating;
    private int controlId;
    private Vector2 startCanvas;
    private Vector2 currentCanvas;

    public ReanimatorCreateBoneModule()
        : base("bones.create", "Bones", 20, 10, "Add/Split", ReanimatorToolMode.CreateBone, 10)
    {
    }

    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => false;

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        var e = Event.current;
        if (api == null || context == null || e == null)
            return;

        if (e.type == EventType.MouseMove)
        {
            api.Repaint();
            return;
        }

        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.button != 0 || e.alt)
            return;
        if (IsControlModifier(e))
            return;

        if (!context.LocalRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp)
                EndCreate();
            return;
        }

        if (e.type == EventType.MouseDown)
        {
            if (IsShiftModifier(e))
            {
                api.TrySplitBoneAt(context, e.mousePosition);
                EndCreate();
                e.Use();
                GUI.changed = true;
                api.Repaint();
                return;
            }

            if (api.TryPrepareCreateBoneStart(context, e.mousePosition, out var preparedStartCanvas, out var shouldCreate))
            {
                if (!shouldCreate)
                {
                    EndCreate();
                    e.Use();
                    GUI.changed = true;
                    api.Repaint();
                    return;
                }

                startCanvas = preparedStartCanvas;
            }
            else
            {
                startCanvas = e.mousePosition;
            }

            isCreating = true;
            currentCanvas = e.mousePosition;
            controlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = controlId;
            e.Use();
            GUI.changed = true;
            api.Repaint();
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (!isCreating || GUIUtility.hotControl != controlId)
                return;

            currentCanvas = e.mousePosition;
            e.Use();
            GUI.changed = true;
            api.Repaint();
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            if (isCreating && GUIUtility.hotControl == controlId)
            {
                currentCanvas = e.mousePosition;
                api.TryCreateBone(context, startCanvas, currentCanvas);
            }

            EndCreate();
            e.Use();
            GUI.changed = true;
            api.Repaint();
        }
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        Handles.BeginGUI();
        var prev = Handles.color;
        if (isCreating)
        {
            Handles.color = new Color(0.2f, 0.9f, 1f, 0.95f);
            BoneShapeDrawer.Draw(startCanvas, currentCanvas, 2f);
        }
        else
        {
            DrawCreateBoneAnchorHoverMarker(api, context, Event.current.mousePosition);
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private static void DrawCreateBoneAnchorHoverMarker(
        Reanimator.SystemApi api,
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas)
    {
        if (api == null || context == null)
            return;
        if (!context.LocalRect.Contains(mouseCanvas))
            return;
        if (!api.TryGetCreateBoneParentAnchor(context, mouseCanvas, out _, out var anchorCanvas, out var anchorKind, out var anchorBoneIndex))
            return;

        var isBase = anchorKind == PsdLayerPreviewRenderer.CreateBoneAnchorKind.Base;
        var outerRadius = isBase ? 10f : 5f;
        var innerRadius = isBase ? 4.2f : 2.4f;
        if (api.TryGetCreateBoneAnchorMarkerRadii(context, anchorBoneIndex, anchorKind, out var scaledOuterRadius, out var scaledInnerRadius))
        {
            outerRadius = scaledOuterRadius;
            innerRadius = scaledInnerRadius;
        }

        Handles.color = new Color(0.02f, 0.08f, 0.1f, 0.95f);
        Handles.DrawSolidDisc(anchorCanvas, Vector3.forward, outerRadius);
        Handles.color = new Color(0.2f, 0.95f, 1f, 0.98f);
        Handles.DrawSolidDisc(anchorCanvas, Vector3.forward, innerRadius);
        Handles.DrawWireDisc(anchorCanvas, Vector3.forward, outerRadius);
    }

    private void EndCreate()
    {
        if (GUIUtility.hotControl == controlId && controlId != 0)
            GUIUtility.hotControl = 0;

        isCreating = false;
        controlId = 0;
    }

    private static bool IsShiftModifier(Event e)
    {
        return e != null && (e.shift || (e.modifiers & EventModifiers.Shift) != 0);
    }

    private static bool IsControlModifier(Event e)
    {
        return e != null && (e.control || (e.modifiers & EventModifiers.Control) != 0 || (e.modifiers & EventModifiers.Command) != 0);
    }
}
#endif
