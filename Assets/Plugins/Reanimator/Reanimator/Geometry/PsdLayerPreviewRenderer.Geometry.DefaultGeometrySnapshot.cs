#if UNITY_EDITOR
using System;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void StoreDefaultGeometrySnapshot(LayerInfo layer, LayerVertexData data)
    {
        var key = BuildDefaultGeometrySnapshotKey(layer);
        if (string.IsNullOrEmpty(key) || data == null || data.vertices == null || data.vertices.Length == 0)
            return;
        if (defaultGeometryBySpriteKey.ContainsKey(key))
            return;

        defaultGeometryBySpriteKey[key] = new DefaultGeometrySnapshot
        {
            spriteGuid = layer != null ? layer.spriteGuid : string.Empty,
            layerStableKey = layer != null ? BuildKey(layer.name, layer.layerId) : string.Empty,
            verticesDoc = CloneArray(data.vertices) ?? Array.Empty<Vector2>(),
            vertexUvs = CloneArray(data.vertexUvs) ?? Array.Empty<Vector2>(),
            triangles = CloneArray(data.triangles) ?? Array.Empty<int>(),
            allEdgePairs = CloneArray(data.allEdgePairs) ?? Array.Empty<int>(),
            boundaryEdgePairs = CloneArray(data.boundaryEdgePairs) ?? Array.Empty<int>(),
            boneWeights = CloneArray(data.boneWeights) ?? Array.Empty<BoneWeight>(),
            spriteBoneToRendererBone = CloneArray(data.spriteBoneToRendererBone),
            bindBoneInverseMatrices = CloneArray(data.bindBoneInverseMatrices),
            importerMode = data.importerMode,
            usesSpriteBindPoses = data.usesSpriteBindPoses,
            pixelsPerUnit = data.pixelsPerUnit,
            pivotPixels = data.pivotPixels,
            sourceRect = data.layer != null ? data.layer.sourceRect : Rect.zero,
            layerPosition = data.layer != null ? data.layer.position : Vector2.zero
        };
    }

    private static string BuildDefaultGeometrySnapshotKey(LayerInfo layer)
    {
        if (layer == null)
            return string.Empty;
        if (!string.IsNullOrEmpty(layer.spriteGuid))
            return "sprite:" + layer.spriteGuid;

        var stableKey = BuildKey(layer.name, layer.layerId);
        if (!string.IsNullOrEmpty(stableKey))
            return "layer:" + stableKey;

        var selectionKey = BuildSelectionKey(layer);
        return string.IsNullOrEmpty(selectionKey) ? string.Empty : "selection:" + selectionKey;
    }

    private static void ApplyDefaultGeometrySnapshotToLayerData(
        DefaultGeometrySnapshot snapshot,
        LayerVertexData data,
        LayerInfo layer,
        Vector2[] remappedVertices)
    {
        if (snapshot == null || data == null)
            return;

        data.layer = layer;
        data.vertices = remappedVertices ?? Array.Empty<Vector2>();
        data.vertexUvs = Array.Empty<Vector2>();
        data.triangles = CloneArray(snapshot.triangles) ?? Array.Empty<int>();
        data.allEdgePairs = CloneArray(snapshot.allEdgePairs) ?? Array.Empty<int>();
        data.boundaryEdgePairs = CloneArray(snapshot.boundaryEdgePairs) ?? Array.Empty<int>();
        data.boneWeights = CloneArray(snapshot.boneWeights) ?? Array.Empty<BoneWeight>();
        data.spriteBoneToRendererBone = CloneArray(snapshot.spriteBoneToRendererBone);
        data.bindBoneInverseMatrices = CloneArray(snapshot.bindBoneInverseMatrices);
        data.importerMode = snapshot.importerMode;
        data.usesSpriteBindPoses = snapshot.usesSpriteBindPoses;
        data.pixelsPerUnit = snapshot.pixelsPerUnit;
        data.pivotPixels = snapshot.pivotPixels;
        data.hasSkinning = data.boneWeights != null &&
                          data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
    }

    private static bool TryBuildDefaultGeometryVerticesForCurrentRect(
        DefaultGeometrySnapshot snapshot,
        LayerInfo layer,
        out Vector2[] vertices)
    {
        vertices = Array.Empty<Vector2>();
        if (snapshot == null || snapshot.verticesDoc == null || snapshot.verticesDoc.Length == 0)
            return false;
        if (layer == null)
            return false;

        var source = NormalizeRect(snapshot.sourceRect);
        var target = NormalizeRect(layer.sourceRect);
        if (source.width <= 0f || source.height <= 0f || target.width <= 0f || target.height <= 0f)
            return false;

        var sourceLayer = new LayerInfo
        {
            position = snapshot.layerPosition,
            sourceRect = source,
            textureRect = source
        };
        vertices = new Vector2[snapshot.verticesDoc.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            // StoreDefaultGeometrySnapshot always stores document coordinates.
            // A padded/repacked sprite rect can also contain those numeric values;
            // that does not make them local. Guessing here adds the origin twice.
            var sourceLocal = PsdCoordinateSpace.DocumentToLayerLocalPoint(sourceLayer, snapshot.verticesDoc[i]);
            var normalized = new Vector2(
                Mathf.Clamp01(sourceLocal.x / source.width),
                Mathf.Clamp01(sourceLocal.y / source.height));
            var targetLocal = new Vector2(
                normalized.x * target.width,
                normalized.y * target.height);
            vertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, targetLocal);
        }

        return true;
    }

}
#endif
