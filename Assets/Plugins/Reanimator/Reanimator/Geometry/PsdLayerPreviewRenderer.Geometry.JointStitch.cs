#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void LogJointApplyDebug(string message)
    {
        if (!IsDebugModeEnabled)
            return;

        ReanimatorLog.Trace($"[Reanimator][Joint][Apply] {message}", IsDebugModeEnabled);
    }

    private void LogJointApplyWarning(string message)
    {
        if (!IsDebugModeEnabled)
            return;

        ReanimatorLog.Trace($"[Reanimator][Joint][Apply][Warning] {message}", IsDebugModeEnabled);
    }

    private void LogJointApplyError(string message)
    {
        ReanimatorLog.Error($"[Reanimator][Joint][Apply] {message}");
    }

    private static string FormatJointVector(Vector2 value)
    {
        return $"({value.x:0.###}, {value.y:0.###})";
    }

    private string DescribeJointLayer(LayerVertexData data)
    {
        if (data == null)
            return "layer=<null>";

        var layerName = data.layer != null ? data.layer.name : "<null>";
        var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : "<none>";
        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        var triangleCount = data.triangles != null ? data.triangles.Length / 3 : 0;
        return $"layer={layerName} stableKey={stableKey} vertices={vertexCount} triangles={triangleCount}";
    }

    private string DescribeJointRendererBone(int rendererBoneIndex)
    {
        if (rendererBoneIndex < 0 || bones == null || rendererBoneIndex >= bones.Count)
            return $"renderer[{rendererBoneIndex}]<invalid>";

        var bone = bones[rendererBoneIndex];
        if (bone == null)
            return $"renderer[{rendererBoneIndex}]<null>";

        return $"renderer[{rendererBoneIndex}] name={bone.name} parent={bone.parentId} base={FormatJointVector(GetBoneBasePoint(bone))} end={FormatJointVector(GetBoneEnd(rendererBoneIndex))}";
    }

    private string DescribeJointSpriteBone(int spriteBoneIndex, IReadOnlyList<int> spriteToRenderer)
    {
        if (spriteBoneIndex < 0)
            return $"sprite[{spriteBoneIndex}]<invalid>";

        var rendererBoneIndex = spriteToRenderer != null && spriteBoneIndex < spriteToRenderer.Count
            ? spriteToRenderer[spriteBoneIndex]
            : -1;
        return $"sprite[{spriteBoneIndex}] -> {DescribeJointRendererBone(rendererBoneIndex)}";
    }
    public bool ApplyJointStitch(
        float radius,
        bool applyRadiusCull,
        bool removeContourVertices,
        bool removeInnerVertices,
        float cPairOffset,
        int longPoints,
        float startLongPointsOffset,
        float endLongPointsOffset,
        out string error)
    {
        error = null;
        LogJointApplyDebug($"Start radius={radius:0.###} center={FormatJointVector(radiusCullCenterDoc)} applyRadiusCull={applyRadiusCull} removeContour={removeContourVertices} removeInner={removeInnerVertices} cPairOffset={cPairOffset:0.###} longPoints={longPoints} startOffset={startLongPointsOffset:0.###} endOffset={endLongPointsOffset:0.###} hasPoint={radiusCullHasPoint}");
        if (radius <= 0.0001f)
        {
            error = "Radius must be > 0";
            LogJointApplyWarning($"Abort: {error}");
            return false;
        }

        if (!radiusCullHasPoint)
        {
            error = "Set point in preview first";
            LogJointApplyWarning($"Abort: {error}");
            return false;
        }

        if (!TryGetRadiusCullLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite geometry first";
            LogJointApplyWarning($"Abort: {error}");
            return false;
        }

        LogJointApplyDebug($"Source data: {DescribeJointLayer(data)} selectionKey={selectionKey} stableKey={stableKey}");

        if (data.vertices == null || data.vertices.Length < 3)
        {
            error = "Geometry is empty";
            LogJointApplyWarning($"Abort: {error}; {DescribeJointLayer(data)}");
            return false;
        }

        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
        {
            error = "Sprite has no skinning bones";
            LogJointApplyWarning($"Abort: {error}; {DescribeJointLayer(data)}");
            return false;
        }

        if (!TryResolveJointInfluenceSpriteBones(data, radiusCullCenterDoc, out var parentSpriteBone, out var nearestSpriteBone, true))
        {
            error = "Failed to resolve joint influence bones";
            LogJointApplyError($"Abort: {error}; {DescribeJointLayer(data)}");
            return false;
        }

        LogJointApplyDebug($"Resolved influence bones parent={DescribeJointSpriteBone(parentSpriteBone, data.spriteBoneToRendererBone)} nearest={DescribeJointSpriteBone(nearestSpriteBone, data.spriteBoneToRendererBone)}");

        var centerDoc = radiusCullCenterDoc;
        cPairOffset = Mathf.Max(0f, cPairOffset);
        longPoints = Mathf.Clamp(longPoints, 0, 10);
        startLongPointsOffset = Mathf.Max(0f, startLongPointsOffset);
        endLongPointsOffset = Mathf.Max(0f, endLongPointsOffset);

        var radiusCullError = string.Empty;
        RegisterUndo("Apply Joint");
        if (applyRadiusCull)
        {
            LogJointApplyDebug($"Applying CircleClean radius={radius:0.###} removeContour={removeContourVertices} removeInner={removeInnerVertices}");
            if (!ApplyRadiusCull(radius, removeContourVertices, removeInnerVertices, false, out var cullError))
            {
                radiusCullError = string.IsNullOrEmpty(cullError)
                    ? "Joint applied, CircleClean skipped"
                    : $"Joint applied, CircleClean failed: {cullError}";
                LogJointApplyWarning($"CircleClean warning: {radiusCullError}");
            }
        }

        if (!TryGetRadiusCullLayerData(out var finalData, out var finalSelectionKey, out var finalStableKey) || finalData == null)
        {
            LogJointApplyWarning("Final layer data fallback to original source after CircleClean stage");
            finalData = data;
            finalSelectionKey = selectionKey;
            finalStableKey = stableKey;
        }

        LogJointApplyDebug($"Final target data: {DescribeJointLayer(finalData)} selectionKey={finalSelectionKey} stableKey={finalStableKey}");

        if (!TryApplyJointExtendedGeometry(
                finalData,
                centerDoc,
                radius,
                parentSpriteBone,
                nearestSpriteBone,
                cPairOffset,
                longPoints,
                startLongPointsOffset,
                endLongPointsOffset,
                out var affectedContourVertices,
                out var insertedVertices,
                true))
        {
            error = "No contour vertices in joint sector";
            LogJointApplyError($"Abort: {error}; geometry stage returned false for {DescribeJointLayer(finalData)}");
            return false;
        }

        if (affectedContourVertices <= 0 && insertedVertices <= 0)
        {
            error = "No contour vertices in joint sector";
            LogJointApplyError($"Abort: {error}; affectedContourVertices={affectedContourVertices} insertedVertices={insertedVertices}");
            return false;
        }

        finalData.lastPoseVersion = -1;
        finalData.hasSkinning = finalData.spriteBoneToRendererBone != null &&
                               finalData.bindBoneInverseMatrices != null &&
                               finalData.boneWeights != null &&
                               finalData.boneWeights.Length == finalData.vertices.Length &&
                               finalData.bindBoneInverseMatrices.Length == finalData.spriteBoneToRendererBone.Length &&
                               finalData.bindBoneInverseMatrices.Length > 0;
        MarkEditGeometryDataChanged(finalData, finalSelectionKey, finalStableKey);
        if (!string.IsNullOrEmpty(radiusCullError))
        {
            error = radiusCullError;
            LogJointApplyWarning($"Completed with CircleClean warning: {radiusCullError}");
        }

        LogJointApplyDebug($"Success: affectedContourVertices={affectedContourVertices} insertedVertices={insertedVertices} finalVertices={(finalData.vertices != null ? finalData.vertices.Length : 0)} finalTriangles={(finalData.triangles != null ? finalData.triangles.Length / 3 : 0)}");
        return true;
    }
}
#endif
