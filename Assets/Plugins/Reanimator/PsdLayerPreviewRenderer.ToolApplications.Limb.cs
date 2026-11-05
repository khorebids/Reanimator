#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    public bool ApplySelectedLimb()
    {
        return ApplySelectedLimb(false, 0f, true, true, out _);
    }

    public bool ApplySelectedLimb(
        bool applyRadiusCullFirst,
        float radiusCullRadius,
        bool removeContourVertices,
        bool removeInnerVertices,
        out string error)
    {
        error = null;
        var lineIndex = FindActiveLimbLineIndex();
        if (lineIndex < 0 || lineIndex >= limbLines.Count)
        {
            error = "No selected limb line.";
            return false;
        }

        var line = limbLines[lineIndex];
        if (line == null)
        {
            error = "Selected limb line is invalid.";
            return false;
        }

        if (!line.hasContourPoints)
            RefreshLimbLineContour(line);
        if (!line.hasContourPoints)
        {
            error = "Failed to resolve limb contour points.";
            return false;
        }
        if (!TryGetLayerVertexDataForLimbLine(line, out var data) || data == null)
        {
            error = "Failed to resolve limb layer geometry.";
            return false;
        }

        if (string.IsNullOrEmpty(line.layerSelectionKey) && data.layer != null)
            line.layerSelectionKey = BuildSelectionKey(data.layer);
        if (string.IsNullOrEmpty(line.layerStableKey) && data.layer != null)
            line.layerStableKey = BuildKey(data.layer.name, data.layer.layerId);

        var generatedPoints = new List<LimbGeneratedPoint>();
        var contourPoints = new List<Vector2>();
        if (!TryBuildLimbGeneratedPoints(
                line,
                data,
                LimbContourSteps,
                LimbContourStepDistance,
                LimbTowardCenterShift,
                generatedPoints,
                contourPoints) ||
            generatedPoints.Count == 0)
        {
                error = "Failed to build limb preview points.";
                return false;
        }

        RegisterUndo(applyRadiusCullFirst ? "Apply Limb With CircleClean" : "Apply Limb");
        if (applyRadiusCullFirst)
        {
            if (radiusCullRadius <= 0.0001f)
            {
                error = "Radius must be > 0.";
                return false;
            }

            var cullCenterDraw = GetLimbContourMidpoint(line);
            selectedLayerSelectionKey = line.layerSelectionKey;
            radiusCullLayerStableKey = line.layerStableKey;
            radiusCullCenterDoc = ConvertPointToBaseDoc(data, cullCenterDraw);
            radiusCullHasPoint = true;

            if (!TryApplyLimbWithRadiusCullPipeline(
                    data,
                    line,
                    generatedPoints,
                    contourPoints,
                    radiusCullRadius,
                    true,
                    removeInnerVertices,
                    out var cullError))
            {
                if (!TryApplyLimbPointsToLayerData(data, data, line, generatedPoints, contourPoints, out var limbError))
                {
                    error = string.IsNullOrEmpty(cullError)
                        ? limbError
                        : cullError;
                    return false;
                }

                error = "Limb applied; CircleClean skipped.";
            }
        }
        else
        {
            if (!TryApplyLimbPointsToLayerData(data, data, line, generatedPoints, contourPoints, out error))
                return false;
        }

        RemoveLimbLineAt(lineIndex);
        hasPendingChanges = true;
        return true;
    }

    private bool TryApplyLimbWithRadiusCullPipeline(
        LayerVertexData sourceData,
        LimbLine line,
        IReadOnlyList<LimbGeneratedPoint> limbPoints,
        IReadOnlyList<Vector2> contourPointsDoc,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices,
        out string error)
    {
        error = null;
        if (sourceData == null)
        {
            error = "Source geometry is invalid.";
            return false;
        }

        if (!TryBuildRadiusCullPreviewGeometry(
                radius,
                removeContourVertices,
                removeInnerVertices,
                out var culledVertices,
                out var culledTriangles,
                out var culledAllEdgePairs,
                out var culledBoundaryEdgePairs,
                out var culledBoneWeights,
                out _))
        {
            error = "Failed to apply CircleClean before Limb.";
            return false;
        }

        var workingData = new LayerVertexData
        {
            layer = sourceData.layer,
            texture = sourceData.texture,
            pixelsPerUnit = sourceData.pixelsPerUnit,
            pivotPixels = sourceData.pivotPixels,
            vertices = culledVertices ?? Array.Empty<Vector2>(),
            triangles = culledTriangles ?? Array.Empty<int>(),
            boundaryEdgePairs = culledBoundaryEdgePairs ?? Array.Empty<int>(),
            allEdgePairs = culledAllEdgePairs ?? Array.Empty<int>(),
            vertexUvs = BuildVertexUvsFromLayer(sourceData.layer, culledVertices ?? Array.Empty<Vector2>()),
            boneWeights = culledBoneWeights ?? Array.Empty<BoneWeight>(),
            spriteBoneToRendererBone = sourceData.spriteBoneToRendererBone,
            bindBoneInverseMatrices = sourceData.bindBoneInverseMatrices,
            usesSpriteBindPoses = sourceData.usesSpriteBindPoses,
            hasSkinning = false,
            lastPoseVersion = -1,
            deformedVertices = null,
            skinMatrices = null
        };

        workingData.hasSkinning = workingData.spriteBoneToRendererBone != null &&
                                  workingData.bindBoneInverseMatrices != null &&
                                  workingData.boneWeights != null &&
                                  workingData.boneWeights.Length == workingData.vertices.Length &&
                                  workingData.bindBoneInverseMatrices.Length == workingData.spriteBoneToRendererBone.Length &&
                                  workingData.bindBoneInverseMatrices.Length > 0;

        var createdContourVertexIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdContourSourceIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdCenterVertexIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdShiftedVertexIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdShiftedSourceIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        if (limbPoints != null)
        {
            for (var i = 0; i < limbPoints.Count; i++)
            {
                var point = limbPoints[i];
                var basePoint = ConvertPointToBaseDoc(sourceData, point.pointDoc);
                var oldVertexCount = workingData.vertices != null ? workingData.vertices.Length : 0;
                if (!InsertAppliedVertexIntoLayerData(workingData, basePoint, true))
                    continue;

                var createdVertexIndex = oldVertexCount;
                if (point.type == LimbGeneratedPointType.Contour)
                {
                    createdContourVertexIndices.Add(createdVertexIndex);
                    createdContourSourceIndices.Add(point.contourSourceIndex);
                }
                else if (point.type == LimbGeneratedPointType.Center)
                {
                    createdCenterVertexIndices.Add(createdVertexIndex);
                }
                else if (point.type == LimbGeneratedPointType.Shifted)
                {
                    createdShiftedVertexIndices.Add(createdVertexIndex);
                    createdShiftedSourceIndices.Add(point.contourSourceIndex);
                }
            }
        }

        if (!TryRebuildCulledLimbBoundary(sourceData, workingData, contourPointsDoc, radius, removeInnerVertices))
        {
            error = "Failed to rebuild Limb contour after CircleClean.";
            return false;
        }

        if (!TryRetriangulateKeepingVertexSet(workingData))
        {
            if (!TryRetriangulateWithDirectFallback(workingData))
            {
                error = "Failed to rebuild mesh after Limb.";
                return false;
            }
        }

        workingData.triangles = SanitizeTriangles(workingData.triangles, workingData.vertices.Length);
        if (workingData.triangles == null || workingData.triangles.Length < 3)
        {
            error = "Failed to build valid triangles after Limb.";
            return false;
        }

        if (!HasCreatedLimbVertices(
                createdContourVertexIndices,
                createdCenterVertexIndices,
                createdShiftedVertexIndices))
        {
            error = "Limb did not add any mesh vertices.";
            return false;
        }

        // Keep explicit contour loop built during insertion; recompute only global edge pairs.
        var preservedBoundaryPairs = workingData.boundaryEdgePairs;
        BuildEdgePairs(
            workingData.triangles,
            workingData.vertices.Length,
            out workingData.allEdgePairs,
            out _);
        if (preservedBoundaryPairs != null && preservedBoundaryPairs.Length >= 6)
            workingData.boundaryEdgePairs = preservedBoundaryPairs;

        if (workingData.vertexUvs == null || workingData.vertexUvs.Length != workingData.vertices.Length)
            workingData.vertexUvs = BuildVertexUvsFromLayer(workingData.layer, workingData.vertices);
        EnsureBoneWeightsArraySize(workingData, workingData.vertices.Length);
        ApplyLimbWeightsToGeometry(
            workingData,
            sourceData,
            line,
            contourPointsDoc,
            createdContourVertexIndices,
            createdContourSourceIndices,
            createdCenterVertexIndices,
            createdShiftedVertexIndices,
            createdShiftedSourceIndices);
        workingData.hasSkinning = workingData.spriteBoneToRendererBone != null &&
                                  workingData.bindBoneInverseMatrices != null &&
                                  workingData.boneWeights.Length == workingData.vertices.Length &&
                                  workingData.bindBoneInverseMatrices.Length == workingData.spriteBoneToRendererBone.Length &&
                                  workingData.bindBoneInverseMatrices.Length > 0;

        sourceData.vertices = workingData.vertices;
        sourceData.vertexUvs = workingData.vertexUvs;
        sourceData.boneWeights = workingData.boneWeights;
        sourceData.triangles = workingData.triangles;
        sourceData.allEdgePairs = workingData.allEdgePairs;
        sourceData.boundaryEdgePairs = workingData.boundaryEdgePairs;
        MarkSkinningWeightsChanged(sourceData);

        MarkEditGeometryDataChanged(sourceData, line.layerSelectionKey, line.layerStableKey);
        return true;
    }

    private bool TryApplyLimbPointsToLayerData(
        LayerVertexData targetData,
        LayerVertexData sourceData,
        LimbLine line,
        IReadOnlyList<LimbGeneratedPoint> limbPoints,
        IReadOnlyList<Vector2> contourPointsDoc,
        out string error)
    {
        error = null;
        if (targetData == null)
        {
            error = "Target geometry is invalid.";
            return false;
        }

        var createdContourVertexIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdContourSourceIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdCenterVertexIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdShiftedVertexIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        var createdShiftedSourceIndices = new List<int>(limbPoints != null ? limbPoints.Count : 0);
        if (limbPoints != null)
        {
            for (var i = 0; i < limbPoints.Count; i++)
            {
                var point = limbPoints[i];
                var oldVertexCount = targetData.vertices != null ? targetData.vertices.Length : 0;
                if (!InsertAppliedVertexIntoLayerData(targetData, point.pointDoc))
                    continue;

                var createdVertexIndex = oldVertexCount;
                if (point.type == LimbGeneratedPointType.Contour)
                {
                    createdContourVertexIndices.Add(createdVertexIndex);
                    createdContourSourceIndices.Add(point.contourSourceIndex);
                }
                else if (point.type == LimbGeneratedPointType.Center)
                {
                    createdCenterVertexIndices.Add(createdVertexIndex);
                }
                else if (point.type == LimbGeneratedPointType.Shifted)
                {
                    createdShiftedVertexIndices.Add(createdVertexIndex);
                    createdShiftedSourceIndices.Add(point.contourSourceIndex);
                }
            }
        }

        if (!HasCreatedLimbVertices(
                createdContourVertexIndices,
                createdCenterVertexIndices,
                createdShiftedVertexIndices))
        {
            error = "Limb did not add any mesh vertices.";
            return false;
        }

        ApplyLimbWeightsToGeometry(
            targetData,
            sourceData,
            line,
            contourPointsDoc,
            createdContourVertexIndices,
            createdContourSourceIndices,
            createdCenterVertexIndices,
            createdShiftedVertexIndices,
            createdShiftedSourceIndices);
        MarkSkinningWeightsChanged(targetData);
        MarkEditGeometryDataChanged(targetData, line.layerSelectionKey, line.layerStableKey);
        return true;
    }

    private static bool HasCreatedLimbVertices(
        IReadOnlyList<int> createdContourVertexIndices,
        IReadOnlyList<int> createdCenterVertexIndices,
        IReadOnlyList<int> createdShiftedVertexIndices)
    {
        return (createdContourVertexIndices != null && createdContourVertexIndices.Count > 0) ||
               (createdCenterVertexIndices != null && createdCenterVertexIndices.Count > 0) ||
               (createdShiftedVertexIndices != null && createdShiftedVertexIndices.Count > 0);
    }

}
#endif
