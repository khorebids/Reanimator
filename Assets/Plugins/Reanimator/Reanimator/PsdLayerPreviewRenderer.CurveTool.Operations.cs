#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool TryAdjustCurveSelectionEdge(bool leftEdge, bool add)
    {
        if (!TryGetCurveSelection(out _, out var boundaryLoop, out _, out _))
            return false;
        if (boundaryLoop == null || boundaryLoop.Count <= 0 || curveSelectedLoopPositions == null || curveSelectedLoopPositions.Count <= 0)
            return false;

        if (!add)
        {
            if (leftEdge)
                curveSelectedLoopPositions.RemoveAt(0);
            else
                curveSelectedLoopPositions.RemoveAt(curveSelectedLoopPositions.Count - 1);

            ResetCurveGraphToCurrentWeights();
            return true;
        }

        var loopCount = boundaryLoop.Count;
        if (curveSelectedLoopPositions.Count >= loopCount)
            return false;

        var loopPosition = leftEdge
            ? GetPreviousLoopPosition(curveSelectedLoopPositions[0], loopCount)
            : GetNextLoopPosition(curveSelectedLoopPositions[curveSelectedLoopPositions.Count - 1], loopCount);
        if (loopPosition < 0 || curveSelectedLoopPositions.Contains(loopPosition))
            return false;

        if (leftEdge)
            curveSelectedLoopPositions.Insert(0, loopPosition);
        else
            curveSelectedLoopPositions.Add(loopPosition);

        ResetCurveGraphToCurrentWeights();
        return true;
    }

    public bool TryInsertCurveContourVertex(int graphSegmentIndex, float segmentT)
    {
        if (!TryGetCurveGraphContext(
                out var data,
                out var boundaryLoop,
                out var drawVertices,
                out var layer,
                out var vertexIndices,
                out var parentSpriteBone,
                out var childSpriteBone,
                out _,
                out _,
                out var actualChildWeights))
            return false;

        EnsureCurveEditableState(actualChildWeights);
        if (data == null || data.vertices == null || data.vertices.Length == 0 || data.boneWeights == null)
            return false;
        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;
        if (graphSegmentIndex < 0 || graphSegmentIndex + 1 >= vertexIndices.Length)
            return false;

        segmentT = Mathf.Clamp01(segmentT);
        if (segmentT <= 0.001f || segmentT >= 0.999f)
            return false;

        var vertexA = vertexIndices[graphSegmentIndex];
        var vertexB = vertexIndices[graphSegmentIndex + 1];
        if (vertexA < 0 || vertexB < 0 || vertexA >= data.vertices.Length || vertexB >= data.vertices.Length)
            return false;
        if (drawVertices == null || vertexA >= drawVertices.Length || vertexB >= drawVertices.Length)
            return false;

        var drawPoint = Vector2.Lerp(drawVertices[vertexA], drawVertices[vertexB], segmentT);
        var basePoint = curveUseAbsoluteGeometry ? drawPoint : ConvertPointToBaseDoc(data, drawPoint);
        var childWeightA = graphSegmentIndex < curveEditedChildWeights.Count
            ? Mathf.Clamp01(curveEditedChildWeights[graphSegmentIndex])
            : Mathf.Clamp01(actualChildWeights[graphSegmentIndex]);
        var childWeightB = graphSegmentIndex + 1 < curveEditedChildWeights.Count
            ? Mathf.Clamp01(curveEditedChildWeights[graphSegmentIndex + 1])
            : Mathf.Clamp01(actualChildWeights[graphSegmentIndex + 1]);
        var childWeight = Mathf.Lerp(childWeightA, childWeightB, segmentT);
        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        var sourceWeight = InterpolateBoneWeight(
            vertexA < data.boneWeights.Length ? SanitizeEditableBoneWeight(data.boneWeights[vertexA], spriteBoneCount) : default,
            vertexB < data.boneWeights.Length ? SanitizeEditableBoneWeight(data.boneWeights[vertexB], spriteBoneCount) : default,
            segmentT,
            spriteBoneCount);
        var boneWeight = BuildCurveBoneWeightPreservingOthers(sourceWeight, parentSpriteBone, childSpriteBone, childWeight, spriteBoneCount);

        var insertAfterLoopPosition = curveSelectedLoopPositions[graphSegmentIndex];
        if (insertAfterLoopPosition < 0 || insertAfterLoopPosition >= boundaryLoop.Count)
            return false;

        var oldVertices = data.vertices;
        var oldVertexUvs = data.vertexUvs;
        var oldBoneWeights = data.boneWeights;
        var oldTriangles = data.triangles;
        var oldAllEdgePairs = data.allEdgePairs;
        var oldBoundaryEdgePairs = data.boundaryEdgePairs;
        var oldDeformedVertices = data.deformedVertices;
        var oldSkinMatrices = data.skinMatrices;
        var oldLastPoseVersion = data.lastPoseVersion;

        RegisterUndo("Insert WeightCurve Vertex");
        void RestorePreviousData()
        {
            data.vertices = oldVertices;
            data.vertexUvs = oldVertexUvs;
            data.boneWeights = oldBoneWeights;
            data.triangles = oldTriangles;
            data.allEdgePairs = oldAllEdgePairs;
            data.boundaryEdgePairs = oldBoundaryEdgePairs;
            data.deformedVertices = oldDeformedVertices;
            data.skinMatrices = oldSkinMatrices;
            data.lastPoseVersion = oldLastPoseVersion;
        }

        var newVertexIndex = oldVertices.Length;
        Array.Resize(ref data.vertices, newVertexIndex + 1);
        data.vertices[newVertexIndex] = basePoint;

        if (data.vertexUvs == null || data.vertexUvs.Length < newVertexIndex)
        {
            var repaired = new Vector2[newVertexIndex + 1];
            if (data.vertexUvs != null && data.vertexUvs.Length > 0)
                Array.Copy(data.vertexUvs, repaired, Mathf.Min(data.vertexUvs.Length, newVertexIndex));
            data.vertexUvs = repaired;
        }
        else
        {
            Array.Resize(ref data.vertexUvs, newVertexIndex + 1);
        }

        data.vertexUvs[newVertexIndex] = ComputeUvForPoint(data.layer, basePoint);

        EnsureBoneWeightsArraySize(data, newVertexIndex);
        Array.Resize(ref data.boneWeights, newVertexIndex + 1);
        data.boneWeights[newVertexIndex] = boneWeight;

        if (data.deformedVertices != null && data.deformedVertices.Length == newVertexIndex)
        {
            Array.Resize(ref data.deformedVertices, newVertexIndex + 1);
            data.deformedVertices[newVertexIndex] = drawPoint;
        }

        var newBoundaryLoop = new List<int>(boundaryLoop);
        var insertBoundaryIndex = insertAfterLoopPosition + 1;
        if (insertBoundaryIndex < 0 || insertBoundaryIndex > newBoundaryLoop.Count)
        {
            RestorePreviousData();
            return false;
        }

        newBoundaryLoop.Insert(insertBoundaryIndex, newVertexIndex);
        var updatedBoundaryPairs = BuildBoundaryPairsFromLoop(newBoundaryLoop);
        if (updatedBoundaryPairs == null || updatedBoundaryPairs.Length < 6)
        {
            RestorePreviousData();
            return false;
        }

        data.boundaryEdgePairs = updatedBoundaryPairs;
        var triangulated = TryInsertVertexIntoTriangles(data, oldVertices, basePoint, newVertexIndex);
        if (!triangulated && !TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
        {
            RestorePreviousData();
            return false;
        }

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out _);
        data.boundaryEdgePairs = updatedBoundaryPairs;
        MarkSkinningWeightsChanged(data);

        for (var i = 0; i < curveSelectedLoopPositions.Count; i++)
        {
            if (curveSelectedLoopPositions[i] > insertAfterLoopPosition)
                curveSelectedLoopPositions[i]++;
        }

        curveSelectedLoopPositions.Insert(graphSegmentIndex + 1, insertAfterLoopPosition + 1);
        ResetCurveGraphToCurrentWeights();

        var selectionKey = layer != null ? BuildSelectionKey(layer) : selectedLayerSelectionKey;
        var stableKey = layer != null ? BuildKey(layer.name, layer.layerId) : curveLayerStableKey;
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    public bool TrySetCurveGraphPointValue(int sampleIndex, float childWeight)
    {
        if (!TryGetCurveGraphContext(
                out var data,
                out _,
                out _,
                out _,
                out var vertexIndices,
                out var parentSpriteBone,
                out var childSpriteBone,
                out _,
                out _,
                out var actualChildWeights))
            return false;

        EnsureCurveEditableState(actualChildWeights);
        if (sampleIndex < 0 || sampleIndex >= curveEditedChildWeights.Count)
            return false;

        var vertexIndex = sampleIndex < vertexIndices.Length ? vertexIndices[sampleIndex] : -1;
        var spriteBoneCount = data != null && data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        var sourceWeight = data != null && data.boneWeights != null && vertexIndex >= 0 && vertexIndex < data.boneWeights.Length
            ? SanitizeEditableBoneWeight(data.boneWeights[vertexIndex], spriteBoneCount)
            : default;
        var nextChildWeight = ClampCurveChildWeight(sourceWeight, parentSpriteBone, childSpriteBone, childWeight);
        if (Mathf.Abs(curveEditedChildWeights[sampleIndex] - nextChildWeight) <= 0.0001f)
            return false;

        if (curveGraphPointDragActive)
        {
            if (!curveGraphPointMoveUndoRegistered)
            {
                RegisterUndo("Edit WeightCurve");
                curveGraphPointMoveUndoRegistered = true;
            }
        }
        else
        {
            RegisterUndo("Edit WeightCurve");
        }

        curveEditedChildWeights[sampleIndex] = nextChildWeight;
        return ApplyCurveEditedWeightsToSelection(false);
    }

    public bool ApplyCurveGraph(out string error)
    {
        error = string.Empty;
        if (!ApplyCurveEditedWeightsToSelection(true))
        {
            error = "Apply failed";
            return false;
        }

        error = "AnalizeCurve applied";
        return true;
    }

    public bool KillCurveAdditionalWeights(out string error)
    {
        error = string.Empty;
        if (!TryGetCurveGraphContext(
                out var data,
                out _,
                out _,
                out var layer,
                out var vertexIndices,
                out var parentSpriteBone,
                out var childSpriteBone,
                out _,
                out _,
                out var actualChildWeights))
        {
            error = "No Curve selection";
            return false;
        }

        if (data == null || data.vertices == null || data.boneWeights == null)
        {
            error = "No Curve weights";
            return false;
        }

        RegisterUndo("Kill WeightCurve Additions");
        EnsureCurveEditableState(actualChildWeights);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);

        var changed = false;
        for (var i = 0; i < vertexIndices.Length; i++)
        {
            var vertexIndex = vertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= data.vertices.Length || vertexIndex >= data.boneWeights.Length)
                continue;

            var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
            var source = SanitizeEditableBoneWeight(data.boneWeights[vertexIndex], spriteBoneCount);
            var otherWeight = GetOtherCurveWeight(source, parentSpriteBone, childSpriteBone);
            var childWeight = i < curveEditedChildWeights.Count
                ? ClampCurveChildWeight(source, parentSpriteBone, childSpriteBone, curveEditedChildWeights[i])
                : GetBoneWeight(source, childSpriteBone);
            var parentWeight = i < curveEditedChildWeights.Count
                ? Mathf.Max(0f, GetCurveMainWeightCapacity(otherWeight) - childWeight)
                : GetBoneWeight(source, parentSpriteBone);
            var mainSum = parentWeight + childWeight;
            if (mainSum <= 0.0001f)
            {
                parentWeight = 0.5f;
                childWeight = 0.5f;
            }
            else
            {
                var inv = 1f / mainSum;
                parentWeight *= inv;
                childWeight *= inv;
            }

            var next = BuildLimbTwoBoneWeight(parentSpriteBone, parentWeight, childSpriteBone, childWeight);
            if (otherWeight > 0.0001f || !AreBoneWeightsEqual(source, next))
                changed = true;
            data.boneWeights[vertexIndex] = next;
        }

        if (!changed)
        {
            error = "No additional weights";
            return true;
        }

        MarkSkinningWeightsChanged(data);
        curveGraphSelectionSignature = string.Empty;
        curveEditedChildWeights.Clear();

        var selectionKey = layer != null ? BuildSelectionKey(layer) : curveDragLayerSelectionKey;
        var stableKey = layer != null ? BuildKey(layer.name, layer.layerId) : curveLayerStableKey;
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        error = "Additional weights removed";
        return true;
    }

    private bool ApplyCurveEditedWeightsToSelection(bool markPendingChanges)
    {
        if (!TryGetCurveGraphContext(
                out var data,
                out _,
                out _,
                out var layer,
                out var vertexIndices,
                out var parentSpriteBone,
                out var childSpriteBone,
                out _,
                out _,
                out var actualChildWeights))
            return false;

        EnsureCurveEditableState(actualChildWeights);
        EnsureBoneWeightsArraySize(data, data.vertices != null ? data.vertices.Length : 0);

        for (var i = 0; i < vertexIndices.Length; i++)
        {
            var vertexIndex = vertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= data.vertices.Length || vertexIndex >= data.boneWeights.Length)
                continue;

            var childWeight = i < curveEditedChildWeights.Count ? Mathf.Clamp01(curveEditedChildWeights[i]) : 0f;
            var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
            var source = SanitizeEditableBoneWeight(data.boneWeights[vertexIndex], spriteBoneCount);
            data.boneWeights[vertexIndex] = BuildCurveBoneWeightPreservingOthers(source, parentSpriteBone, childSpriteBone, childWeight, spriteBoneCount);
        }

        MarkSkinningWeightsChanged(data);
        if (markPendingChanges)
        {
            var selectionKey = layer != null ? BuildSelectionKey(layer) : curveDragLayerSelectionKey;
            var stableKey = layer != null ? BuildKey(layer.name, layer.layerId) : curveLayerStableKey;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        }
        else
        {
            InvalidateRadiusCullPreviewCache(true);
        }
        return true;
    }

}
#endif
