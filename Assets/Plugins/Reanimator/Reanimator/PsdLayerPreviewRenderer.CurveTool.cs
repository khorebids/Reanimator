#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public readonly struct CurveGraphData
    {
        public readonly string layerName;
        public readonly int totalBoneCount;
        public readonly string parentBoneName;
        public readonly string childBoneName;
        public readonly string otherBoneName;
        public readonly Color parentColor;
        public readonly Color childColor;
        public readonly Color otherColor;
        public readonly Vector2[] parentSamples;
        public readonly Vector2[] childSamples;
        public readonly Vector2[] otherSamples;

        public CurveGraphData(
            string layerName,
            int totalBoneCount,
            string parentBoneName,
            string childBoneName,
            string otherBoneName,
            Color parentColor,
            Color childColor,
            Color otherColor,
            Vector2[] parentSamples,
            Vector2[] childSamples,
            Vector2[] otherSamples)
        {
            this.layerName = layerName;
            this.totalBoneCount = totalBoneCount;
            this.parentBoneName = parentBoneName;
            this.childBoneName = childBoneName;
            this.otherBoneName = otherBoneName;
            this.parentColor = parentColor;
            this.childColor = childColor;
            this.otherColor = otherColor;
            this.parentSamples = parentSamples ?? Array.Empty<Vector2>();
            this.childSamples = childSamples ?? Array.Empty<Vector2>();
            this.otherSamples = otherSamples ?? Array.Empty<Vector2>();
        }
    }

    public bool HasCurveSelection()
    {
        return !string.IsNullOrEmpty(curveLayerStableKey) && curveSelectedLoopPositions != null && curveSelectedLoopPositions.Count > 0;
    }

    public void CancelCurveTool()
    {
        var changed = !string.IsNullOrEmpty(curveLayerStableKey) ||
                      (curveSelectedLoopPositions != null && curveSelectedLoopPositions.Count > 0) ||
                      !string.IsNullOrEmpty(curveGraphSelectionSignature) ||
                      (curveEditedChildWeights != null && curveEditedChildWeights.Count > 0) ||
                      !string.IsNullOrEmpty(curveSecondBoneSelectionSignature) ||
                      curveSelectedFirstSpriteBone != -1 ||
                      curveSelectedSecondSpriteBone != -1;
        if (changed)
            RegisterUndo("Cancel WeightCurve Tool");

        if (GUIUtility.hotControl == curveControlId && curveControlId != 0)
            GUIUtility.hotControl = 0;
        curveLayerStableKey = string.Empty;
        curveSelectedLoopPositions.Clear();
        curveGraphSelectionSignature = string.Empty;
        curveEditedChildWeights.Clear();
        ClearCurveSecondBoneSelection();
        curveIsDragging = false;
        curveDragStartCanvas = Vector2.zero;
        curveDragCurrentCanvas = Vector2.zero;
        curveControlId = 0;
        curveDragLayerSelectionKey = string.Empty;
        curveGraphPointDragActive = false;
        curveGraphPointMoveUndoRegistered = false;
    }

    public void ResetCurveGraphToCurrentWeights()
    {
        curveGraphSelectionSignature = string.Empty;
        curveEditedChildWeights.Clear();
        ClearCurveSecondBoneSelection();
        curveGraphPointDragActive = false;
        curveGraphPointMoveUndoRegistered = false;
    }

    public void BeginCurveGraphPointDrag()
    {
        curveGraphPointDragActive = true;
        curveGraphPointMoveUndoRegistered = false;
    }

    public void EndCurveGraphPointDrag()
    {
        curveGraphPointDragActive = false;
        curveGraphPointMoveUndoRegistered = false;
    }

    private void ClearCurveSecondBoneSelection()
    {
        curveSecondBoneSelectionSignature = string.Empty;
        curveSelectedFirstSpriteBone = -1;
        curveSelectedSecondSpriteBone = -1;
    }

    private Vector2[] GetCurveGraphVertices(LayerVertexData data)
    {
        if (data == null)
            return null;

        return curveUseAbsoluteGeometry ? data.vertices : GetLayerVerticesForDraw(data);
    }

    public bool TryGetCurveGraphData(out CurveGraphData graphData)
    {
        graphData = default;
        if (!TryGetCurveGraphContext(
                out var data,
                out _,
                out _,
                out var layer,
                out var vertexIndices,
                out var parentSpriteBone,
                out var childSpriteBone,
                out var parentRendererBone,
                out var childRendererBone,
                out var actualChildWeights))
            return false;

        EnsureCurveEditableState(actualChildWeights);

        var childSamples = new Vector2[vertexIndices.Length];
        var parentSamples = new Vector2[vertexIndices.Length];
        var otherSamples = new Vector2[vertexIndices.Length];
        for (var i = 0; i < vertexIndices.Length; i++)
        {
            var x = vertexIndices.Length <= 1 ? 0f : actualChildWeights[vertexIndices.Length + i];
            var vertexIndex = vertexIndices[i];
            var sourceWeight = data.boneWeights != null && vertexIndex >= 0 && vertexIndex < data.boneWeights.Length
                ? SanitizeEditableBoneWeight(data.boneWeights[vertexIndex], data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0)
                : default;
            var otherWeight = GetOtherCurveWeight(sourceWeight, parentSpriteBone, childSpriteBone);
            var childWeight = i < curveEditedChildWeights.Count ? curveEditedChildWeights[i] : actualChildWeights[i];
            childWeight = ClampCurveChildWeight(sourceWeight, parentSpriteBone, childSpriteBone, childWeight);
            childSamples[i] = new Vector2(x, childWeight);
            parentSamples[i] = new Vector2(x, Mathf.Max(0f, GetCurveMainWeightCapacity(otherWeight) - childWeight));
            otherSamples[i] = new Vector2(x, otherWeight);
        }

        var parentName = parentRendererBone >= 0 && parentRendererBone < bones.Count ? bones[parentRendererBone].name : "Max";
        var childName = childRendererBone >= 0 && childRendererBone < bones.Count ? bones[childRendererBone].name : "Min";
        var otherName = BuildCurveOtherBoneName(data, vertexIndices, parentSpriteBone, childSpriteBone);
        var parentColor = parentRendererBone >= 0 && parentRendererBone < bones.Count ? bones[parentRendererBone].color : new Color(0.95f, 0.74f, 0.15f, 1f);
        var childColor = childRendererBone >= 0 && childRendererBone < bones.Count ? bones[childRendererBone].color : new Color(1f, 0.2f, 0.55f, 1f);
        var otherColor = new Color(0.38f, 0.75f, 1f, 1f);
        var totalBoneCount = CountCurveBoneCandidates(data, vertexIndices);

        graphData = new CurveGraphData(
            layer != null ? layer.name : string.Empty,
            totalBoneCount,
            parentName,
            childName,
            otherName,
            parentColor,
            childColor,
            otherColor,
            parentSamples,
            childSamples,
            otherSamples);
        return true;
    }

    private bool TryGetCurveGraphContext(
        out LayerVertexData data,
        out List<int> boundaryLoop,
        out Vector2[] drawVertices,
        out LayerInfo layer,
        out int[] vertexIndices,
        out int parentSpriteBone,
        out int childSpriteBone,
        out int parentRendererBone,
        out int childRendererBone,
        out float[] childWeightsAndXs)
    {
        data = null;
        boundaryLoop = null;
        drawVertices = null;
        layer = null;
        vertexIndices = Array.Empty<int>();
        parentSpriteBone = -1;
        childSpriteBone = -1;
        parentRendererBone = -1;
        childRendererBone = -1;
        childWeightsAndXs = Array.Empty<float>();

        if (!TryGetCurveSelection(out data, out boundaryLoop, out _, out layer))
            return false;

        drawVertices = GetCurveGraphVertices(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return false;

        var selectionCount = curveSelectedLoopPositions != null ? curveSelectedLoopPositions.Count : 0;
        if (selectionCount <= 0)
            return false;

        vertexIndices = new int[selectionCount];
        for (var i = 0; i < selectionCount; i++)
        {
            var loopPosition = curveSelectedLoopPositions[i];
            if (loopPosition < 0 || loopPosition >= boundaryLoop.Count)
                return false;

            vertexIndices[i] = boundaryLoop[loopPosition];
            if (vertexIndices[i] < 0 || vertexIndices[i] >= drawVertices.Length)
                return false;
        }

        if (!TryResolveCurveBonePair(data, vertexIndices, out parentSpriteBone, out childSpriteBone, out parentRendererBone, out childRendererBone))
            return false;

        var totalDistance = 0f;
        for (var i = 1; i < vertexIndices.Length; i++)
            totalDistance += Vector2.Distance(drawVertices[vertexIndices[i - 1]], drawVertices[vertexIndices[i]]);

        childWeightsAndXs = new float[vertexIndices.Length * 2];
        var distance = 0f;
        for (var i = 0; i < vertexIndices.Length; i++)
        {
            if (i > 0)
                distance += Vector2.Distance(drawVertices[vertexIndices[i - 1]], drawVertices[vertexIndices[i]]);

            var x = totalDistance > 0.0001f ? distance / totalDistance : (vertexIndices.Length <= 1 ? 0f : (float)i / (vertexIndices.Length - 1));
            var weight = vertexIndices[i] < data.boneWeights.Length
                ? SanitizeEditableBoneWeight(data.boneWeights[vertexIndices[i]], data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0)
                : default;
            childWeightsAndXs[i] = GetBoneWeight(weight, childSpriteBone);
            childWeightsAndXs[vertexIndices.Length + i] = x;
        }

        return true;
    }

    private void EnsureCurveEditableState(IReadOnlyList<float> actualChildWeights)
    {
        if (actualChildWeights == null)
            return;

        var count = actualChildWeights.Count / 2;
        var signature = BuildCurveSelectionSignature();
        if (!string.Equals(curveGraphSelectionSignature, signature, StringComparison.Ordinal) || curveEditedChildWeights == null || curveEditedChildWeights.Count != count)
        {
            curveGraphSelectionSignature = signature;
            if (curveEditedChildWeights == null)
                curveEditedChildWeights = new List<float>(count);
            else
                curveEditedChildWeights.Clear();

            for (var i = 0; i < count; i++)
                curveEditedChildWeights.Add(Mathf.Clamp01(actualChildWeights[i]));
            return;
        }

        for (var i = 0; i < curveEditedChildWeights.Count; i++)
            curveEditedChildWeights[i] = Mathf.Clamp01(curveEditedChildWeights[i]);
    }

    private string BuildCurveSelectionSignature()
    {
        if (string.IsNullOrEmpty(curveLayerStableKey) || curveSelectedLoopPositions == null || curveSelectedLoopPositions.Count == 0)
            return string.Empty;

        return curveLayerStableKey + "|" + string.Join(",", curveSelectedLoopPositions);
    }

    private static int GetPreviousLoopPosition(int position, int count)
    {
        return count <= 0 ? -1 : (position - 1 + count) % count;
    }

    private static int GetNextLoopPosition(int position, int count)
    {
        return count <= 0 ? -1 : (position + 1) % count;
    }

}
#endif


