#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void HandleWeightSliderInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null || coordinates == null || drawList == null)
            return;

        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        var smoothVertex = e.type == EventType.MouseDown && e.button == 1;
        if (!isMouseEvent || e.alt || (!smoothVertex && e.button != 0))
            return;

        if (!localRect.Contains(e.mousePosition) && !weightSliderIsWeightDragging)
        {
            if (e.type == EventType.MouseUp)
            {
                EndWeightSliderRectSelection();
                EndWeightSliderWeightDrag();
            }
            return;
        }

        if (e.type == EventType.MouseDrag && weightSliderIsWeightDragging && GUIUtility.hotControl == weightSliderControlId)
        {
            weightSliderWeightDragMoved |= HasWeightSliderWeightDragMoved(e.mousePosition);
            if (weightSliderWeightDragMoved && ApplyWeightSliderWeightDrag(coordinates, e.mousePosition))
                GUI.changed = true;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && weightSliderIsWeightDragging && GUIUtility.hotControl == weightSliderControlId)
        {
            weightSliderWeightDragMoved |= HasWeightSliderWeightDragMoved(e.mousePosition);
            if (weightSliderWeightDragMoved && ApplyWeightSliderWeightDrag(coordinates, e.mousePosition))
                GUI.changed = true;
            EndWeightSliderWeightDrag();
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDown)
        {
            var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
            const float hitRadiusPx = 8f;
            var hasSelectedLayerData = TryGetSelectedWeightSliderLayerData(out var selectedData, out var selectedSelectionKey, out var selectedStableKey);
            if (TryFindClosestEditableVertex(
                    hasSelectedLayerData ? selectedSelectionKey : selectedLayerSelectionKey,
                    hasSelectedLayerData ? selectedStableKey : editGeometrySelectedLayerStableKey,
                    coordinates,
                    e.mousePosition,
                    hitRadiusPx,
                    out var selectedHit))
            {
                var preserveSelectedGroup = !smoothVertex &&
                                            !e.shift &&
                                            IsWeightSliderVertexSelected(selectedHit.stableKey, selectedHit.vertexIndex);
                ApplyWeightSliderVertexSelection(
                    selectedHit.selectionKey,
                    selectedHit.stableKey,
                    selectedHit.vertexIndex,
                    smoothVertex ? false : e.shift,
                    preserveSelectedGroup);
                if (smoothVertex)
                    TrySmoothWeightSliderSelection(weightSliderSmoothStrength);
                else if (!e.shift)
                    BeginWeightSliderWeightDrag(coordinates, selectedHit.stableKey, selectedHit.vertexIndex, e.mousePosition);
                e.Use();
                GUI.changed = true;
                return;
            }

            var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out _);
            if (hasHitLayer && hitLayer != null)
            {
                var layerSelectionKey = BuildSelectionKey(hitLayer);
                var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
                selectedLayerSelectionKey = layerSelectionKey;
                editGeometrySelectedLayerStableKey = stableKey;
                weightSliderSelectedLayerStableKey = stableKey;
                if (TryFindClosestEditableVertex(layerSelectionKey, stableKey, coordinates, e.mousePosition, hitRadiusPx, out var layerHit))
                {
                    var preserveSelectedGroup = !smoothVertex &&
                                                !e.shift &&
                                                IsWeightSliderVertexSelected(layerHit.stableKey, layerHit.vertexIndex);
                    ApplyWeightSliderVertexSelection(
                        layerHit.selectionKey,
                        layerHit.stableKey,
                        layerHit.vertexIndex,
                        smoothVertex ? false : e.shift,
                        preserveSelectedGroup);
                    if (smoothVertex)
                        TrySmoothWeightSliderSelection(weightSliderSmoothStrength);
                    else if (!e.shift)
                        BeginWeightSliderWeightDrag(coordinates, layerHit.stableKey, layerHit.vertexIndex, e.mousePosition);
                    e.Use();
                    GUI.changed = true;
                    return;
                }
            }

            if (smoothVertex)
                return;

            if (bones != null && bones.Count > 0 && FindBoneHandleAt(e.mousePosition, coordinates).index >= 0)
                return;

            weightSliderControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = weightSliderControlId;
            weightSliderIsRectSelecting = true;
            weightSliderRectToggleSelection = e.shift;
            weightSliderRectStartCanvas = e.mousePosition;
            weightSliderRectCurrentCanvas = e.mousePosition;
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (!weightSliderIsRectSelecting || GUIUtility.hotControl != weightSliderControlId)
                return;

            weightSliderRectCurrentCanvas = e.mousePosition;
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            if (weightSliderIsRectSelecting && GUIUtility.hotControl == weightSliderControlId)
                SelectWeightSliderVerticesInRect(coordinates, drawList);

            EndWeightSliderRectSelection();
            e.Use();
            GUI.changed = true;
        }
    }

    private void ApplyWeightSliderVertexSelection(string selectionKey, string stableKey, int vertexIndex)
    {
        ApplyWeightSliderVertexSelection(selectionKey, stableKey, vertexIndex, false, false);
    }

    private void ApplyWeightSliderVertexSelection(
        string selectionKey,
        string stableKey,
        int vertexIndex,
        bool toggle,
        bool preserveExisting)
    {
        ApplyEditGeometryVertexSelection(selectionKey, stableKey, vertexIndex, preserveExisting, false);
        var sameLayer = string.Equals(weightSliderSelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase);
        if ((!toggle && !preserveExisting) || !sameLayer)
        {
            weightSliderSelectedLayerStableKey = stableKey;
            weightSliderSelectedVertexIndices.Clear();
            if (vertexIndex >= 0)
                weightSliderSelectedVertexIndices.Add(vertexIndex);
            return;
        }

        weightSliderSelectedLayerStableKey = stableKey;
        if (vertexIndex < 0)
            return;

        if (preserveExisting)
        {
            if (!weightSliderSelectedVertexIndices.Contains(vertexIndex))
                weightSliderSelectedVertexIndices.Add(vertexIndex);
            editGeometrySelectedVertexIndex = vertexIndex;
            return;
        }

        var existingIndex = weightSliderSelectedVertexIndices.IndexOf(vertexIndex);
        if (existingIndex >= 0)
            weightSliderSelectedVertexIndices.RemoveAt(existingIndex);
        else
            weightSliderSelectedVertexIndices.Add(vertexIndex);

        editGeometrySelectedVertexIndex = weightSliderSelectedVertexIndices.Count > 0 ? weightSliderSelectedVertexIndices[0] : -1;
    }

    private bool IsWeightSliderVertexSelected(string stableKey, int vertexIndex)
    {
        return vertexIndex >= 0 &&
               string.Equals(weightSliderSelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase) &&
               weightSliderSelectedVertexIndices != null &&
               weightSliderSelectedVertexIndices.Contains(vertexIndex);
    }

    private void SelectWeightSliderVerticesInRect(PsdCoordinateSpace coordinates, IReadOnlyList<LayerInfo> drawList)
    {
        if (coordinates == null)
            return;
        var rect = BuildWeightSliderRect(weightSliderRectStartCanvas, weightSliderRectCurrentCanvas);
        if (!TryGetWeightSliderLayerDataForRectSelection(coordinates, drawList, rect, out var data, out var selectionKey, out var stableKey))
            return;
        if (data.vertices == null || data.vertices.Length == 0)
            return;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return;

        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var hits = new List<int>();
        for (var i = 0; i < drawVertices.Length; i++)
        {
            if (rect.Contains(toCanvasVertex(drawVertices[i])))
                hits.Add(i);
        }

        if (!weightSliderRectToggleSelection ||
            !string.Equals(weightSliderSelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
        {
            weightSliderSelectedVertexIndices.Clear();
            weightSliderSelectedVertexIndices.AddRange(hits);
        }
        else
        {
            for (var i = 0; i < hits.Count; i++)
            {
                var existingIndex = weightSliderSelectedVertexIndices.IndexOf(hits[i]);
                if (existingIndex >= 0)
                    weightSliderSelectedVertexIndices.RemoveAt(existingIndex);
                else
                    weightSliderSelectedVertexIndices.Add(hits[i]);
            }
        }

        selectedLayerSelectionKey = selectionKey;
        editGeometrySelectedLayerStableKey = stableKey;
        weightSliderSelectedLayerStableKey = stableKey;
        editGeometrySelectedVertexIndex = weightSliderSelectedVertexIndices.Count > 0 ? weightSliderSelectedVertexIndices[0] : -1;
    }

    private bool TryGetWeightSliderLayerDataForRectSelection(
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        Rect rect,
        out LayerVertexData data,
        out string selectionKey,
        out string stableKey)
    {
        data = null;
        selectionKey = string.Empty;
        stableKey = string.Empty;
        if (coordinates == null || drawList == null)
            return false;

        if (TryGetSelectedWeightSliderLayerData(out var selectedData, out var selectedSelectionKey, out var selectedStableKey) &&
            selectedData != null &&
            DoesWeightSliderRectHitLayer(coordinates, rect, selectedData))
        {
            data = selectedData;
            selectionKey = selectedSelectionKey;
            stableKey = selectedStableKey;
            return true;
        }

        for (var i = drawList.Count - 1; i >= 0; i--)
        {
            var layer = drawList[i];
            if (layer == null || !layer.visible)
                continue;

            var candidateSelectionKey = BuildSelectionKey(layer);
            var candidateStableKey = BuildKey(layer.name, layer.layerId);
            if (!TryGetLayerVertexDataForKeys(candidateSelectionKey, candidateStableKey, out var candidateData) || candidateData == null)
                continue;
            if (!DoesWeightSliderRectHitLayer(coordinates, rect, candidateData))
                continue;

            data = candidateData;
            selectionKey = candidateSelectionKey;
            stableKey = candidateStableKey;
            selectedLayerSelectionKey = selectionKey;
            editGeometrySelectedLayerStableKey = stableKey;
            weightSliderSelectedLayerStableKey = stableKey;
            return true;
        }

        return false;
    }

    private bool DoesWeightSliderRectHitLayer(PsdCoordinateSpace coordinates, Rect rect, LayerVertexData data)
    {
        if (coordinates == null || data == null)
            return false;

        var candidateVertices = GetLayerVerticesForDraw(data);
        if (candidateVertices == null || candidateVertices.Length == 0)
            return false;

        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, candidateVertices);
        for (var vertexIndex = 0; vertexIndex < candidateVertices.Length; vertexIndex++)
        {
            if (rect.Contains(toCanvasVertex(candidateVertices[vertexIndex])))
                return true;
        }

        return false;
    }

    private void EndWeightSliderRectSelection()
    {
        if (GUIUtility.hotControl == weightSliderControlId && weightSliderControlId != 0)
            GUIUtility.hotControl = 0;

        weightSliderIsRectSelecting = false;
        weightSliderRectToggleSelection = false;
        weightSliderControlId = 0;
    }

    private bool BeginWeightSliderWeightDrag(
        PsdCoordinateSpace coordinates,
        string stableKey,
        int vertexIndex,
        Vector2 mouseCanvas)
    {
        EndWeightSliderWeightDrag();

        if (coordinates == null)
            return false;
        if (!TryGetSelectedWeightSliderLayerData(out var data, out _, out var resolvedStableKey))
            return false;
        if (!string.Equals(stableKey, resolvedStableKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (data.boneWeights == null || vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || vertexIndex >= drawVertices.Length)
            return false;
        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        NormalizeWeightSliderSelection(data, resolvedStableKey);
        var dragStates = new List<WeightSliderVertexDragState>();
        WeightSliderVertexDragState draggedState = null;
        for (var i = 0; i < weightSliderSelectedVertexIndices.Count; i++)
        {
            var selectedVertexIndex = weightSliderSelectedVertexIndices[i];
            if (selectedVertexIndex < 0 || selectedVertexIndex >= data.boneWeights.Length || selectedVertexIndex >= drawVertices.Length)
                continue;

            var sourceWeight = SanitizeEditableBoneWeight(data.boneWeights[selectedVertexIndex], spriteBoneCount);
            var activeBones = BuildActiveWeightBoneList(sourceWeight, data.spriteBoneToRendererBone);
            if (activeBones == null || activeBones.Count < 2)
                continue;

            var activeSourceWeights = new float[activeBones.Count];
            for (var boneIndex = 0; boneIndex < activeBones.Count; boneIndex++)
                activeSourceWeights[boneIndex] = GetSpriteBoneWeight(sourceWeight, activeBones[boneIndex]);

            var state = new WeightSliderVertexDragState
            {
                vertexIndex = selectedVertexIndex,
                bones = activeBones.ToArray(),
                sourceWeights = activeSourceWeights,
                pointerOffsetCanvas = mouseCanvas - toCanvasVertex(drawVertices[selectedVertexIndex])
            };
            dragStates.Add(state);
            if (selectedVertexIndex == vertexIndex)
                draggedState = state;
        }
        if (draggedState == null)
            return false;

        weightSliderControlId = GUIUtility.GetControlID(FocusType.Passive);
        GUIUtility.hotControl = weightSliderControlId;
        weightSliderIsWeightDragging = true;
        weightSliderWeightDragLayerStableKey = resolvedStableKey;
        weightSliderWeightDragVertexIndex = vertexIndex;
        weightSliderWeightDragBones = draggedState.bones;
        weightSliderWeightDragStates = dragStates.ToArray();
        weightSliderWeightDragStartCanvas = mouseCanvas;
        weightSliderWeightDragPointerOffsetCanvas = draggedState.pointerOffsetCanvas;
        weightSliderWeightDragMoved = false;
        weightSliderWeightDragUndoRegistered = false;
        return true;
    }

    private void EndWeightSliderWeightDrag()
    {
        if (GUIUtility.hotControl == weightSliderControlId && weightSliderControlId != 0)
            GUIUtility.hotControl = 0;

        weightSliderIsWeightDragging = false;
        weightSliderWeightDragLayerStableKey = string.Empty;
        weightSliderWeightDragVertexIndex = -1;
        weightSliderWeightDragBones = Array.Empty<int>();
        weightSliderWeightDragStates = Array.Empty<WeightSliderVertexDragState>();
        weightSliderWeightDragStartCanvas = Vector2.zero;
        weightSliderWeightDragPointerOffsetCanvas = Vector2.zero;
        weightSliderWeightDragMoved = false;
        weightSliderWeightDragUndoRegistered = false;
        weightSliderControlId = 0;
    }

    private bool HasWeightSliderWeightDragMoved(Vector2 mouseCanvas)
    {
        return (mouseCanvas - weightSliderWeightDragStartCanvas).sqrMagnitude >= 4f;
    }

    private bool ApplyWeightSliderWeightDrag(PsdCoordinateSpace coordinates, Vector2 mouseCanvas)
    {
        if (coordinates == null ||
            !weightSliderIsWeightDragging ||
            weightSliderWeightDragStates == null ||
            weightSliderWeightDragStates.Length == 0)
            return false;
        if (!TryGetSelectedWeightSliderLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (!string.Equals(weightSliderWeightDragLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (data.vertices == null || data.vertices.Length == 0 || data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        var selectedCount = NormalizeWeightSliderSelection(data, stableKey);
        if (selectedCount <= 0)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return false;

        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var updates = new List<KeyValuePair<int, BoneWeight>>(weightSliderWeightDragStates.Length);
        for (var stateIndex = 0; stateIndex < weightSliderWeightDragStates.Length; stateIndex++)
        {
            var state = weightSliderWeightDragStates[stateIndex];
            if (state == null ||
                state.vertexIndex < 0 ||
                state.vertexIndex >= data.boneWeights.Length ||
                state.bones == null ||
                state.bones.Length < 2)
                continue;

            var boneIndices = new[] { -1, -1, -1, -1 };
            var weights = new float[4];
            var previewCanvasPoints = new Vector2[4];
            var activeCount = Mathf.Min(4, state.bones.Length);
            var hasAllPreviewPoints = true;
            for (var boneIndex = 0; boneIndex < activeCount; boneIndex++)
            {
                var spriteBoneIndex = state.bones[boneIndex];
                if (!TryGetSingleBonePreviewVertex(data, state.vertexIndex, spriteBoneIndex, out var previewDocPoint))
                {
                    hasAllPreviewPoints = false;
                    break;
                }

                boneIndices[boneIndex] = spriteBoneIndex;
                previewCanvasPoints[boneIndex] = toCanvasVertex(previewDocPoint);
            }
            if (!hasAllPreviewPoints ||
                !TryBuildWeightSliderDragWeights(
                    previewCanvasPoints,
                    activeCount,
                    mouseCanvas,
                    state.pointerOffsetCanvas,
                    state.sourceWeights,
                    weights))
                continue;

            var boneWeight = BuildSanitizedEditGeometryBoneWeight(
                boneIndices,
                weights,
                data.spriteBoneToRendererBone.Length,
                true);
            updates.Add(new KeyValuePair<int, BoneWeight>(state.vertexIndex, boneWeight));
        }
        if (updates.Count == 0)
            return false;

        RegisterWeightSliderWeightDragUndoOnce();
        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];
            data.boneWeights[update.Key] = update.Value;
        }

        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private void RegisterWeightSliderWeightDragUndoOnce()
    {
        if (weightSliderWeightDragUndoRegistered)
            return;

        RegisterUndo("Slider Weights");
        weightSliderWeightDragUndoRegistered = true;
    }

    private static bool TryBuildWeightSliderDragWeights(Vector2[] points, int count, Vector2 mouseCanvas, float[] weights)
    {
        return ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(points, count, mouseCanvas, weights);
    }

    private static bool TryBuildWeightSliderDragWeights(
        Vector2[] points,
        int count,
        Vector2 mouseCanvas,
        Vector2 pointerOffsetCanvas,
        IReadOnlyList<float> sourceWeights,
        float[] weights)
    {
        return ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
            points,
            count,
            mouseCanvas,
            pointerOffsetCanvas,
            sourceWeights,
            weights);
    }

    private static Vector2 ClampPointToWeightSliderTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        return ReanimatorWeightSliderAlgorithms.ClampPointToTriangle(point, a, b, c);
    }

    private static Vector2 ClosestPointOnWeightSliderSegment(Vector2 point, Vector2 a, Vector2 b, out float t)
    {
        return ReanimatorWeightSliderAlgorithms.ClosestPointOnSegment(point, a, b, out t);
    }

    private static int[] BuildWeightSliderPolygonOrder(Vector2[] points, int count)
    {
        return ReanimatorWeightSliderAlgorithms.BuildPolygonOrder(points, count);
    }

    private static bool IsPointInsideWeightSliderPolygon(Vector2 point, Vector2[] polygon, int count)
    {
        return ReanimatorWeightSliderAlgorithms.IsPointInsidePolygon(point, polygon, count);
    }

    private static Vector2 ClosestPointOnWeightSliderPolygon(Vector2 point, Vector2[] polygon, int count)
    {
        return ReanimatorWeightSliderAlgorithms.ClosestPointOnPolygon(point, polygon, count);
    }

    private static bool TryBuildMeanValueWeightSliderWeights(Vector2[] polygon, int count, Vector2 point, float[] weights)
    {
        return ReanimatorWeightSliderAlgorithms.TryBuildMeanValueWeights(polygon, count, point, weights);
    }

    private static void NormalizeWeightSliderDragWeights(float[] weights, int count)
    {
        ReanimatorWeightSliderAlgorithms.NormalizeWeights(weights, count);
    }

    private static Rect BuildWeightSliderRect(Vector2 a, Vector2 b)
    {
        var xMin = Mathf.Min(a.x, b.x);
        var xMax = Mathf.Max(a.x, b.x);
        var yMin = Mathf.Min(a.y, b.y);
        var yMax = Mathf.Max(a.y, b.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void DrawWeightSliderOverlay(PsdCoordinateSpace coordinates, float alpha)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || coordinates == null)
            return;

        DrawWeightSliderSelectedVertices(coordinates, alpha);

        if (!weightSliderIsRectSelecting)
            return;

        var rect = BuildWeightSliderRect(weightSliderRectStartCanvas, weightSliderRectCurrentCanvas);
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        EditorGUI.DrawRect(rect, new Color(0.2f, 0.65f, 1f, 0.12f));
        Handles.BeginGUI();
        var prev = Handles.color;
        Handles.color = new Color(0.2f, 0.75f, 1f, 0.9f);
        Handles.DrawAAPolyLine(
            2f,
            new Vector3(rect.xMin, rect.yMin, 0f),
            new Vector3(rect.xMax, rect.yMin, 0f),
            new Vector3(rect.xMax, rect.yMax, 0f),
            new Vector3(rect.xMin, rect.yMax, 0f),
            new Vector3(rect.xMin, rect.yMin, 0f));
        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawWeightSliderSelectedVertices(PsdCoordinateSpace coordinates, float alpha)
    {
        if (alpha <= 0f || weightSliderSelectedVertexIndices == null || weightSliderSelectedVertexIndices.Count == 0)
            return;
        if (!TryGetSelectedWeightSliderLayerData(out var data, out _, out var stableKey))
            return;
        if (!string.Equals(weightSliderSelectedLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return;

        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        Handles.BeginGUI();
        var prev = Handles.color;
        for (var i = 0; i < weightSliderSelectedVertexIndices.Count; i++)
        {
            var vertexIndex = weightSliderSelectedVertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= drawVertices.Length)
                continue;

            var p = toCanvasVertex(drawVertices[vertexIndex]);
            DrawWeightSliderBonePreviewPoints(data, stableKey, vertexIndex, p, toCanvasVertex, alpha);

            Handles.color = new Color(0.2f, 0.75f, 1f, Mathf.Clamp01(alpha));
            Handles.DrawWireDisc(p, Vector3.forward, 6f);
        }
        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawWeightSliderBonePreviewPoints(
        LayerVertexData data,
        string stableKey,
        int vertexIndex,
        Vector2 currentCanvasPoint,
        Func<Vector2, Vector2> toCanvasVertex,
        float alpha)
    {
        if (data == null || data.boneWeights == null || vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
            return;

        var dragState = string.Equals(stableKey, weightSliderWeightDragLayerStableKey, StringComparison.OrdinalIgnoreCase)
            ? FindWeightSliderVertexDragState(vertexIndex)
            : null;
        var activeBones = weightSliderIsWeightDragging && dragState != null
            ? new List<int>(dragState.bones ?? Array.Empty<int>())
            : BuildActiveWeightBoneList(data.boneWeights[vertexIndex], data.spriteBoneToRendererBone);
        if (activeBones == null || activeBones.Count < 2)
            return;

        if (activeBones.Count >= 3)
            DrawWeightSliderPreviewPointPolygon(data, vertexIndex, activeBones, toCanvasVertex, alpha);

        for (var i = 0; i < activeBones.Count; i++)
            DrawWeightSliderSingleBonePreviewPoint(data, vertexIndex, activeBones[i], currentCanvasPoint, toCanvasVertex, alpha);
    }

    private WeightSliderVertexDragState FindWeightSliderVertexDragState(int vertexIndex)
    {
        if (weightSliderWeightDragStates == null)
            return null;

        for (var i = 0; i < weightSliderWeightDragStates.Length; i++)
        {
            var state = weightSliderWeightDragStates[i];
            if (state != null && state.vertexIndex == vertexIndex)
                return state;
        }
        return null;
    }

    private sealed class WeightSliderVertexDragState
    {
        public int vertexIndex;
        public int[] bones = Array.Empty<int>();
        public float[] sourceWeights = Array.Empty<float>();
        public Vector2 pointerOffsetCanvas;
    }

    private void DrawWeightSliderPreviewPointPolygon(
        LayerVertexData data,
        int vertexIndex,
        IReadOnlyList<int> activeBones,
        Func<Vector2, Vector2> toCanvasVertex,
        float alpha)
    {
        if (activeBones == null || activeBones.Count < 3)
            return;

        var points = new Vector2[activeBones.Count];
        for (var i = 0; i < activeBones.Count; i++)
        {
            if (!TryGetSingleBonePreviewVertex(data, vertexIndex, activeBones[i], out var previewDocPoint))
                return;
            points[i] = toCanvasVertex != null ? toCanvasVertex(previewDocPoint) : previewDocPoint;
        }

        var order = BuildWeightSliderPolygonOrder(points, points.Length);
        var prevColor = Handles.color;
        Handles.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * 0.75f));
        for (var i = 0; i < order.Length; i++)
        {
            var a = points[order[i]];
            var b = points[order[(i + 1) % order.Length]];
            Handles.DrawDottedLine(a, b, 4f);
        }
        Handles.color = prevColor;
    }

    private void DrawWeightSliderSingleBonePreviewPoint(
        LayerVertexData data,
        int vertexIndex,
        int spriteBoneIndex,
        Vector2 currentCanvasPoint,
        Func<Vector2, Vector2> toCanvasVertex,
        float alpha)
    {
        if (!TryGetSingleBonePreviewVertex(data, vertexIndex, spriteBoneIndex, out var previewDocPoint))
            return;

        var previewCanvasPoint = toCanvasVertex != null ? toCanvasVertex(previewDocPoint) : previewDocPoint;
        var color = GetSpriteBonePreviewColor(data, spriteBoneIndex, alpha);
        Handles.color = color;
        Handles.DrawDottedLine(currentCanvasPoint, previewCanvasPoint, 4f);
        Handles.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        Handles.DrawWireDisc(previewCanvasPoint, Vector3.forward, 4f);
        Handles.color = color;
        Handles.DrawWireDisc(previewCanvasPoint, Vector3.forward, 3f);
    }

    private bool TryGetSingleBonePreviewVertex(LayerVertexData data, int vertexIndex, int spriteBoneIndex, out Vector2 previewDocPoint)
    {
        previewDocPoint = Vector2.zero;
        if (data == null || data.vertices == null || vertexIndex < 0 || vertexIndex >= data.vertices.Length)
            return false;
        if (data.bindBoneInverseMatrices == null || data.bindBoneInverseMatrices.Length == 0)
            return false;
        if (spriteBoneIndex < 0 || spriteBoneIndex >= data.bindBoneInverseMatrices.Length)
            return false;

        if (data.skinMatrices == null || data.skinMatrices.Length != data.bindBoneInverseMatrices.Length)
            data.skinMatrices = new Matrix4x4[data.bindBoneInverseMatrices.Length];

        BuildSkinMatrices(data);

        var basePoint = data.usesSpriteBindPoses
            ? DocumentToSpriteSkinPosition(data, data.vertices[vertexIndex])
            : data.vertices[vertexIndex];
        var basePos = new Vector3(basePoint.x, basePoint.y, 0f);
        var skinned = data.skinMatrices[spriteBoneIndex].MultiplyPoint3x4(basePos);
        previewDocPoint = data.usesSpriteBindPoses
            ? SpriteSkinPositionToDocument(data, new Vector2(skinned.x, skinned.y))
            : new Vector2(skinned.x, skinned.y);
        return true;
    }

    private static List<int> BuildActiveWeightBoneList(
        BoneWeight weight,
        IReadOnlyList<int> spriteBoneToRendererBone,
        bool includeZeroWeightIdentities = true)
    {
        return ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(weight, spriteBoneToRendererBone, includeZeroWeightIdentities);
    }

    private Color GetSpriteBonePreviewColor(LayerVertexData data, int spriteBoneIndex, float alpha)
    {
        if (data == null ||
            data.spriteBoneToRendererBone == null ||
            spriteBoneIndex < 0 ||
            spriteBoneIndex >= data.spriteBoneToRendererBone.Length)
            return new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

        var rendererBoneIndex = data.spriteBoneToRendererBone[spriteBoneIndex];
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            return new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

        var color = bones[rendererBoneIndex].color;
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    private bool TryGetSelectedWeightSliderLayerData(out LayerVertexData data, out string selectionKey, out string stableKey)
    {
        data = null;
        selectionKey = selectedLayerSelectionKey;
        stableKey = !string.IsNullOrEmpty(weightSliderSelectedLayerStableKey)
            ? weightSliderSelectedLayerStableKey
            : editGeometrySelectedLayerStableKey;

        if (!string.IsNullOrEmpty(selectionKey) &&
            TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) &&
            data != null)
        {
            stableKey = BuildKey(data.layer.name, data.layer.layerId);
            editGeometrySelectedLayerStableKey = stableKey;
            weightSliderSelectedLayerStableKey = stableKey;
            return true;
        }

        if (!string.IsNullOrEmpty(stableKey) &&
            TryGetLayerVertexDataByStableKey(stableKey, out data, out var resolvedSelectionKey) &&
            data != null)
        {
            selectionKey = resolvedSelectionKey;
            selectedLayerSelectionKey = selectionKey;
            editGeometrySelectedLayerStableKey = stableKey;
            weightSliderSelectedLayerStableKey = stableKey;
            return true;
        }

        if (TryGetSelectedLayer(out var layer) && layer != null)
        {
            selectionKey = BuildSelectionKey(layer);
            stableKey = BuildKey(layer.name, layer.layerId);
            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) && data != null)
            {
                selectedLayerSelectionKey = selectionKey;
                editGeometrySelectedLayerStableKey = stableKey;
                weightSliderSelectedLayerStableKey = stableKey;
                return true;
            }
        }

        return false;
    }
}
#endif
