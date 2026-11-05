#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorWeightBrushModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (context == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        var anchor = hasCursor ? cursorCanvas + new Vector2(radius + 14f, 18f) : context.LocalRect.position + new Vector2(8f, 34f);
        var modeLabel = modeIndex >= 0 && modeIndex < Modes.Length ? Modes[modeIndex] : modeIndex.ToString();
        var docLabel = string.Empty;
        if (hasCursor && context.Coordinates != null)
        {
            var doc = context.Coordinates.ViewToDocument(cursorCanvas);
            docLabel = $"\ndoc={doc.x:F1},{doc.y:F1}";
        }

        var label =
            "WeightBrush\n" +
            $"mode={modeLabel} bone={boneIndex}\n" +
            $"radius={radius:F1} hard={hardness:F2} amount={amount:F2}\n" +
            $"normalize={normalize} painting={isPainting}\n" +
            $"altBones={showAllBonesForAltSelection} hoverBone={hoveredRendererBoneIndex}" +
            docLabel;

        ReanimatorDebugPreviewGui.DrawLabel(anchor, label, isPainting ? ReanimatorDebugPreviewGui.Hot : ReanimatorDebugPreviewGui.Neutral);
    }
}
#endif
