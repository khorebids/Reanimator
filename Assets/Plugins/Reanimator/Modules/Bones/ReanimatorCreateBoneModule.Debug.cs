#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorCreateBoneModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (context == null || context.Coordinates == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        var anchor = context.LocalRect.position + new Vector2(8f, 34f);
        if (!isCreating)
        {
            ReanimatorDebugPreviewGui.DrawLabel(anchor, "CreateBone\nstate=idle\nshift=split", ReanimatorDebugPreviewGui.Neutral);
            return;
        }

        var startDoc = context.Coordinates.ViewToDocument(startCanvas);
        var currentDoc = context.Coordinates.ViewToDocument(currentCanvas);
        var lengthView = Vector2.Distance(startCanvas, currentCanvas);
        var lengthDoc = Vector2.Distance(startDoc, currentDoc);
        var label =
            "CreateBone\n" +
            $"state=drag hot={GUIUtility.hotControl == controlId}\n" +
            $"view={lengthView:F1} doc={lengthDoc:F1}\n" +
            $"start={startDoc.x:F1},{startDoc.y:F1}\n" +
            $"end={currentDoc.x:F1},{currentDoc.y:F1}";

        ReanimatorDebugPreviewGui.DrawLabel(currentCanvas + new Vector2(10f, 10f), label, ReanimatorDebugPreviewGui.Hot);
    }
}
#endif
