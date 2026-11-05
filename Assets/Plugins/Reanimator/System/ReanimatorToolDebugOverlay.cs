#if UNITY_EDITOR
using UnityEngine;

internal static class ReanimatorToolDebugOverlay
{
    public static void Draw(ReanimatorPreviewContext context, string toolName, string details, Color color)
    {
        if (context == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        var visibleLayers = 0;
        var layerCount = context.DrawList != null ? context.DrawList.Count : 0;
        if (context.DrawList != null)
        {
            for (var i = 0; i < context.DrawList.Count; i++)
            {
                if (context.DrawList[i] != null && context.DrawList[i].visible)
                    visibleLayers++;
            }
        }

        var visibility = context.Visibility;
        var label =
            $"{toolName}\n" +
            $"layers={layerCount} visible={visibleLayers}\n" +
            $"view={context.LocalRect.width:F0}x{context.LocalRect.height:F0}";
        if (visibility != null)
            label += $"\nalpha S/V/B={visibility.SpriteAlpha:F2}/{visibility.VertexAlpha:F2}/{visibility.BoneAlpha:F2}";
        if (!string.IsNullOrWhiteSpace(details))
            label += "\n" + details;

        ReanimatorDebugPreviewGui.DrawLabel(context.LocalRect.position + new Vector2(8f, 58f), label, color);
    }
}
#endif
