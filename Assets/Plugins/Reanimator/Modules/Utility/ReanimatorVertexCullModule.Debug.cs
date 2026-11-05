#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorVertexCullModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (api == null || context == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        var layerName = string.Empty;
        var vertexCount = 0;
        var mergeGroupCount = 0;
        var removedCount = 0;
        var canAnalyze = api.TryAnalyzeVertexCull(distance, out layerName, out vertexCount, out mergeGroupCount, out removedCount, out var error);
        var status = canAnalyze ? "ok" : string.IsNullOrWhiteSpace(error) ? "unavailable" : error;
        var label =
            "VertexCull\n" +
            $"distance={distance:F2} status={status}\n" +
            $"sprite={layerName} vertices={vertexCount}\n" +
            $"groups={mergeGroupCount} remove={removedCount}";

        ReanimatorDebugPreviewGui.DrawLabel(context.LocalRect.position + new Vector2(8f, 34f), label, canAnalyze ? ReanimatorDebugPreviewGui.Neutral : ReanimatorDebugPreviewGui.Selected);
    }
}
#endif
