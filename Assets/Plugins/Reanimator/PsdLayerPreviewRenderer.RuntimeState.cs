#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize() => EnsureRuntimeState();

    internal void EnsureRuntimeState()
    {
        layerInfos ??= new List<LayerInfo>();
        layerHierarchyNodes ??= new List<LayerHierarchyNode>();
        bones ??= new List<BoneInfo>();
        sortedLayers ??= new List<LayerInfo>();
        boneDrawOrder ??= new List<int>();
        vertexDataByLayerSelectionKey ??= new Dictionary<string, LayerVertexData>(StringComparer.OrdinalIgnoreCase);
        defaultGeometryBySpriteKey ??= new Dictionary<string, DefaultGeometrySnapshot>(StringComparer.OrdinalIgnoreCase);
        spriteHitBuffer ??= new List<LayerInfo>();
        elbowConnectionBuffer ??= new List<int>(8);
        boobV2WorkingContourDoc ??= new List<Vector2>();
        previewTestAnimSavedRotations ??= new List<Quaternion>();
        radiusCullPreviewCache ??= new RadiusCullPreviewCache();
        limbPreviewCache ??= new LimbPreviewCache();
        debugLayerVertexBoundsDocByLayerKey ??= new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        debugLayerVertexScoreByLayerKey ??= new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        debugLayerGeometryByLayerKey ??= new Dictionary<string, LayerGeometryDebugInfo>(StringComparer.OrdinalIgnoreCase);
        reanimaxHardMaskEdgePointPathsLocal ??= Array.Empty<Vector2[]>();
        vertexEdgeSegments ??= Array.Empty<Vector3>();
        vertexBoundarySegments ??= Array.Empty<Vector3>();
        vertexPointSegments ??= Array.Empty<Vector3>();
        vertexBoundarySegmentsShifted ??= Array.Empty<Vector3>();
        defaultGeometryAssetPath ??= string.Empty;
        debugChosenBoneMode ??= string.Empty;
        faceAlphaAnalysisLayerKey ??= string.Empty;
    }
}
#endif
