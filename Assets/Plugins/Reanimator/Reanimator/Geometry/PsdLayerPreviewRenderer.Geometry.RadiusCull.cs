#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool ApplyRadiusCull(float radius, bool removeContourVertices, bool removeInnerVertices, out string error)
    {
        return ApplyRadiusCull(radius, removeContourVertices, removeInnerVertices, true, out error);
    }

    private bool ApplyRadiusCull(float radius, bool removeContourVertices, bool removeInnerVertices, bool registerUndo, out string error)
    {
        error = null;
        if (radius <= 0.0001f)
        {
            error = "Radius must be > 0";
            return false;
        }

        if (!removeContourVertices && !removeInnerVertices)
        {
            error = "Enable CW and/or IW";
            return false;
        }

        if (!radiusCullHasPoint)
        {
            error = "Set point in preview first";
            return false;
        }

        if (!TryGetRadiusCullLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite geometry first";
            return false;
        }

        if (data.vertices == null || data.vertices.Length < 3)
        {
            error = "Geometry is empty";
            return false;
        }

        var vertexCount = data.vertices.Length;
        var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
            if (fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6)
            {
                data.boundaryEdgePairs = fallbackBoundaryPairs;
                contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
            }
        }

        if (CountTrue(contourMask) < 3)
        {
            error = "Failed to resolve contour";
            return false;
        }

        var centerDoc = radiusCullCenterDoc;
        var removeMask = ReanimatorRadiusCullAlgorithms.BuildRemoveMask(
            data.vertices,
            contourMask,
            centerDoc,
            radius,
            removeContourVertices,
            removeInnerVertices,
            out var removedCount);

        if (removedCount <= 0)
        {
            error = "No vertices in radius";
            return false;
        }

        var remainingVertexCount = vertexCount - removedCount;
        if (remainingVertexCount < 3)
        {
            error = "Too many vertices removed";
            return false;
        }

        if (registerUndo)
            RegisterUndo("Apply Radius Cull");
        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out var boundaryLoop))
            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);

        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out _);
        var keptVertices = new List<Vector2>(remainingVertexCount);
        var keptUvs = new List<Vector2>(remainingVertexCount);
        var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
            ? new List<BoneWeight>(remainingVertexCount)
            : null;
        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                continue;
            keptVertices.Add(data.vertices[i]);
            if (data.vertexUvs != null && i < data.vertexUvs.Length)
                keptUvs.Add(data.vertexUvs[i]);
            else
                keptUvs.Add(ComputeUvForPoint(data.layer, data.vertices[i]));
            if (keptWeights != null)
                keptWeights.Add(data.boneWeights[i]);
        }

        var newBoundaryLoop = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
        if (newBoundaryLoop.Count < 3)
        {
            var remappedContourVertices = new List<int>(remainingVertexCount);
            for (var i = 0; i < contourMask.Length; i++)
            {
                if (!contourMask[i] || removeMask[i] || remap[i] < 0)
                    continue;
                remappedContourVertices.Add(remap[i]);
            }
            newBoundaryLoop = BuildFallbackBoundaryLoop(keptVertices, remappedContourVertices);
        }

        if (newBoundaryLoop.Count < 3)
        {
            error = "Contour collapsed after cull";
            return false;
        }

        data.vertices = keptVertices.ToArray();
        data.vertexUvs = keptUvs.ToArray();
        data.boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>();
        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop);
        data.triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, data.vertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!TryRetriangulateKeepingVertexSet(data))
        {
            if (!TryRetriangulateWithDirectFallback(data))
            {
                error = "Failed to rebuild mesh";
                return false;
            }
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            error = "Failed to build valid triangles";
            return false;
        }

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        data.hasSkinning = data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;

        if (string.Equals(selectedLayerSelectionKey, selectionKey, StringComparison.OrdinalIgnoreCase) &&
            editGeometrySelectedVertexIndex >= 0 &&
            editGeometrySelectedVertexIndex < remap.Length)
        {
            editGeometrySelectedVertexIndex = remap[editGeometrySelectedVertexIndex];
            if (editGeometrySelectedVertexIndices != null)
            {
                editGeometrySelectedVertexIndices.Clear();
                if (editGeometrySelectedVertexIndex >= 0)
                    editGeometrySelectedVertexIndices.Add(editGeometrySelectedVertexIndex);
            }
        }

        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private bool TryBuildRadiusCullPreviewGeometry(
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices,
        out Vector2[] previewVertices,
        out int[] previewTriangles,
        out int[] previewAllEdgePairs,
        out int[] previewBoundaryEdgePairs,
        out BoneWeight[] previewBoneWeights,
        out int[] previewSpriteBoneToRendererBone,
        bool includeJointContourIntersections = false,
        float jointCPairOffset = 0f,
        int jointLongPoints = 0,
        float jointStartLongPointsOffset = 0f,
        float jointEndLongPointsOffset = 0f,
        LayerVertexData explicitSource = null,
        Vector2 explicitCenter = default)
    {
        previewVertices = Array.Empty<Vector2>();
        previewTriangles = Array.Empty<int>();
        previewAllEdgePairs = Array.Empty<int>();
        previewBoundaryEdgePairs = Array.Empty<int>();
        previewBoneWeights = Array.Empty<BoneWeight>();
        previewSpriteBoneToRendererBone = Array.Empty<int>();

        if (radius <= 0.0001f)
            return false;

        var applyCull = removeContourVertices || removeInnerVertices;
        if (!applyCull && !includeJointContourIntersections)
            return false;
        var data = explicitSource;
        if (data == null && (!radiusCullHasPoint || !TryGetRadiusCullLayerData(out data, out _, out _) || data == null))
            return false;
        if (data.vertices == null || data.vertices.Length < 3)
            return false;

        var vertexCount = data.vertices.Length;
        var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
            if (fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6)
                contourMask = BuildContourVertexMask(fallbackBoundaryPairs, vertexCount);
        }

        if (CountTrue(contourMask) < 3)
            return false;

        var centerDoc = explicitSource != null ? explicitCenter : radiusCullCenterDoc;
        LayerVertexData previewData;

        if (applyCull)
        {
            var removeMask = ReanimatorRadiusCullAlgorithms.BuildRemoveMask(
                data.vertices,
                contourMask,
                centerDoc,
                radius,
                removeContourVertices,
                removeInnerVertices,
                out var removedCount);

            if (removedCount <= 0)
                return false;

            var remainingVertexCount = vertexCount - removedCount;
            if (remainingVertexCount < 3)
                return false;

            if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out var boundaryLoop))
                boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);

            var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out _);
            var keptVertices = new List<Vector2>(remainingVertexCount);
            var keptUvs = new List<Vector2>(remainingVertexCount);
            var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
                ? new List<BoneWeight>(remainingVertexCount)
                : null;
            for (var i = 0; i < vertexCount; i++)
            {
                if (removeMask[i])
                    continue;
                keptVertices.Add(data.vertices[i]);
                if (data.vertexUvs != null && i < data.vertexUvs.Length)
                    keptUvs.Add(data.vertexUvs[i]);
                else
                    keptUvs.Add(ComputeUvForPoint(data.layer, data.vertices[i]));
                if (keptWeights != null)
                    keptWeights.Add(data.boneWeights[i]);
            }

            var newBoundaryLoop = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
            if (newBoundaryLoop.Count < 3)
            {
                var remappedContourVertices = new List<int>(remainingVertexCount);
                for (var i = 0; i < contourMask.Length; i++)
                {
                    if (!contourMask[i] || removeMask[i] || remap[i] < 0)
                        continue;
                    remappedContourVertices.Add(remap[i]);
                }
                newBoundaryLoop = BuildFallbackBoundaryLoop(keptVertices, remappedContourVertices);
            }

            if (newBoundaryLoop.Count < 3)
                return false;

            previewData = new LayerVertexData
            {
                layer = data.layer,
                texture = data.texture,
                pixelsPerUnit = data.pixelsPerUnit,
                pivotPixels = data.pivotPixels,
                vertices = keptVertices.ToArray(),
                vertexUvs = keptUvs.ToArray(),
                boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>(),
                boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop),
                triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, keptVertices.Count),
                spriteBoneToRendererBone = data.spriteBoneToRendererBone,
                bindBoneInverseMatrices = data.bindBoneInverseMatrices,
                usesSpriteBindPoses = data.usesSpriteBindPoses,
                hasSkinning = false,
                lastPoseVersion = -1,
                deformedVertices = null,
                skinMatrices = null
            };

            if (!TryRetriangulateKeepingVertexSet(previewData))
            {
                if (!TryRetriangulateWithDirectFallback(previewData))
                    return false;
            }
        }
        else
        {
            previewData = new LayerVertexData
            {
                layer = data.layer,
                texture = data.texture,
                pixelsPerUnit = data.pixelsPerUnit,
                pivotPixels = data.pivotPixels,
                vertices = CloneArray(data.vertices) ?? Array.Empty<Vector2>(),
                vertexUvs = CloneArray(data.vertexUvs),
                triangles = CloneArray(data.triangles) ?? Array.Empty<int>(),
                allEdgePairs = CloneArray(data.allEdgePairs) ?? Array.Empty<int>(),
                boundaryEdgePairs = CloneArray(data.boundaryEdgePairs) ?? Array.Empty<int>(),
                boneWeights = CloneArray(data.boneWeights) ?? Array.Empty<BoneWeight>(),
                spriteBoneToRendererBone = data.spriteBoneToRendererBone,
                bindBoneInverseMatrices = data.bindBoneInverseMatrices,
                usesSpriteBindPoses = data.usesSpriteBindPoses,
                hasSkinning = data.hasSkinning,
                lastPoseVersion = -1,
                deformedVertices = null,
                skinMatrices = null
            };

            if (previewData.vertexUvs == null || previewData.vertexUvs.Length != previewData.vertices.Length)
                previewData.vertexUvs = BuildVertexUvsFromLayer(data.layer, previewData.vertices);
            EnsureBoneWeightsArraySize(previewData, previewData.vertices.Length);
        }

        previewData.hasSkinning = previewData.spriteBoneToRendererBone != null &&
                                  previewData.bindBoneInverseMatrices != null &&
                                  previewData.boneWeights != null &&
                                  previewData.boneWeights.Length == previewData.vertices.Length &&
                                  previewData.bindBoneInverseMatrices.Length == previewData.spriteBoneToRendererBone.Length &&
                                  previewData.bindBoneInverseMatrices.Length > 0;

        if (includeJointContourIntersections &&
            TryResolveJointInfluenceSpriteBones(previewData, centerDoc, out var jointParentSpriteBone, out var jointNearestSpriteBone, false))
        {
            TryApplyJointExtendedGeometry(
                previewData,
                centerDoc,
                radius,
                jointParentSpriteBone,
                jointNearestSpriteBone,
                jointCPairOffset,
                jointLongPoints,
                jointStartLongPointsOffset,
                jointEndLongPointsOffset,
                out _,
                out _,
                false);
        }

        previewData.triangles = SanitizeTriangles(previewData.triangles, previewData.vertices.Length);
        if (previewData.triangles == null || previewData.triangles.Length < 3)
            return false;

        BuildEdgePairs(
            previewData.triangles,
            previewData.vertices.Length,
            out previewData.allEdgePairs,
            out previewData.boundaryEdgePairs);

        if (previewData.vertexUvs == null || previewData.vertexUvs.Length != previewData.vertices.Length)
            previewData.vertexUvs = BuildVertexUvsFromLayer(data.layer, previewData.vertices);
        EnsureBoneWeightsArraySize(previewData, previewData.vertices.Length);

        previewVertices = previewData.vertices ?? Array.Empty<Vector2>();
        previewTriangles = previewData.triangles ?? Array.Empty<int>();
        previewAllEdgePairs = previewData.allEdgePairs ?? Array.Empty<int>();
        previewBoundaryEdgePairs = previewData.boundaryEdgePairs ?? Array.Empty<int>();
        previewBoneWeights = previewData.boneWeights ?? Array.Empty<BoneWeight>();
        previewSpriteBoneToRendererBone = previewData.spriteBoneToRendererBone ?? Array.Empty<int>();
        return previewVertices.Length >= 3 && previewTriangles.Length >= 3;
    }
    private bool TryGetRadiusCullLayerData(out LayerVertexData data, out string selectionKey, out string stableKey)
    {
        data = null;
        selectionKey = selectedLayerSelectionKey;
        stableKey = radiusCullLayerStableKey;

        if (!string.IsNullOrEmpty(selectionKey) &&
            TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) &&
            data != null)
        {
            stableKey = BuildKey(data.layer.name, data.layer.layerId);
            radiusCullLayerStableKey = stableKey;
            return true;
        }

        if (!string.IsNullOrEmpty(stableKey) &&
            TryGetLayerVertexDataByStableKey(stableKey, out data, out var resolvedSelectionKey) &&
            data != null)
        {
            selectionKey = resolvedSelectionKey;
            selectedLayerSelectionKey = selectionKey;
            radiusCullLayerStableKey = stableKey;
            return true;
        }

        return false;
    }

    private static bool[] BuildContourVertexMask(int[] boundaryEdgePairs, int vertexCount)
    {
        return ReanimatorRadiusCullAlgorithms.BuildContourMask(boundaryEdgePairs, vertexCount);
    }

    private static int CountTrue(IReadOnlyList<bool> values)
    {
        return ReanimatorRadiusCullAlgorithms.CountTrue(values);
    }
}
#endif
