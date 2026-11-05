#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorEditBoneModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (context == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        if (api == null || !api.TryGetSelectedBoneDebugInfo(out var info))
        {
            ReanimatorToolDebugOverlay.Draw(context, "EditBone", "selected=<none>\ninput=select/drag bones or resize tip", ReanimatorDebugPreviewGui.Neutral);
            return;
        }

        var weightedRatio = info.mappedVertexCount > 0
            ? info.influencedVertexCount / (float)info.mappedVertexCount
            : 0f;
        var label =
            "EditBone\n" +
            $"selected={info.rendererBoneIndex} {info.boneName}\n" +
            $"parent={info.parentIndex} {info.parentName} children={info.childCount}\n" +
            $"length={info.length:F2} depthSprites={info.mappedSpriteCount}/{info.weightedSpriteCount}\n" +
            $"weights={info.influencedVertexCount}/{info.mappedVertexCount} ({weightedRatio:P0}) max={info.maxWeight:F2}\n" +
            $"poseDelta pos={info.posePositionDelta:F2} rot={info.poseRotationDeltaDeg:F1}\n" +
            $"issues={info.issues}";

        var color = info.issues == PsdLayerPreviewRenderer.BoneDebugIssueFlags.None
            ? ReanimatorDebugPreviewGui.Neutral
            : ReanimatorDebugPreviewGui.Selected;
        var anchor = context.Coordinates != null
            ? context.Coordinates.DocumentToView(info.worldPosition) + new Vector2(12f, 12f)
            : context.LocalRect.position + new Vector2(8f, 58f);

        ReanimatorDebugPreviewGui.DrawLabel(anchor, label, color);
    }
}
#endif
