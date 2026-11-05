#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void HandleCreateEdgeInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null || coordinates == null || drawList == null)
            return;

        if (e.type != EventType.MouseDown || e.alt || e.button != 0)
            return;

        if (!localRect.Contains(e.mousePosition))
            return;

        var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
        const float hitRadiusPx = 8f;
        if (TryFindClosestEditableVertex(
                selectedLayerSelectionKey,
                editGeometrySelectedLayerStableKey,
                coordinates,
                e.mousePosition,
                hitRadiusPx,
                out var selectedHit))
        {
            HandleCreateEdgeVertexClick(selectedHit.selectionKey, selectedHit.stableKey, selectedHit.vertexIndex);
            e.Use();
            GUI.changed = true;
            return;
        }

        var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out var blockedByLayerLock);
        if (!hasHitLayer && blockedByLayerLock)
        {
            e.Use();
            return;
        }

        if (hitLayer == null)
        {
            ClearEditGeometryVertexSelection(true);
            createEdgeStartVertexIndex = -1;
            createEdgeLayerStableKey = string.Empty;
            e.Use();
            GUI.changed = true;
            return;
        }

        var layerSelectionKey = BuildSelectionKey(hitLayer);
        var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
        selectedLayerSelectionKey = layerSelectionKey;
        editGeometrySelectedLayerStableKey = stableKey;
        if (TryFindClosestEditableVertex(layerSelectionKey, stableKey, coordinates, e.mousePosition, hitRadiusPx, out var layerHit))
            HandleCreateEdgeVertexClick(layerHit.selectionKey, layerHit.stableKey, layerHit.vertexIndex);
        else
            ClearEditGeometryVertexSelection(false);

        e.Use();
        GUI.changed = true;
    }

    private void HandleCreateEdgeVertexClick(string selectionKey, string stableKey, int vertexIndex)
    {
        ApplyEditGeometryVertexSelection(selectionKey, stableKey, vertexIndex);

        var sameLayer = !string.IsNullOrEmpty(createEdgeLayerStableKey) &&
                        string.Equals(createEdgeLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase);
        if (createEdgeStartVertexIndex < 0 || !sameLayer)
        {
            createEdgeStartVertexIndex = vertexIndex;
            createEdgeLayerStableKey = stableKey;
            return;
        }

        if (createEdgeStartVertexIndex == vertexIndex)
        {
            createEdgeStartVertexIndex = -1;
            createEdgeLayerStableKey = stableKey;
            return;
        }

        if (!TryCreateEditGeometryEdge(createEdgeStartVertexIndex, vertexIndex, out var error))
        {
            ReanimatorLog.Trace(
                $"[Reanimator][CreateEdge][Warning] {error}",
                !string.IsNullOrEmpty(error) && IsDebugModeEnabled);
            return;
        }

        createEdgeStartVertexIndex = vertexIndex;
        createEdgeLayerStableKey = stableKey;
    }

    private void HandleSplitEdgeInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null || coordinates == null || drawList == null)
            return;

        if (e.type != EventType.MouseDown || e.alt || e.button != 0)
            return;

        if (!localRect.Contains(e.mousePosition))
            return;

        var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
        const float hitRadiusPx = 7f;
        if (TryFindClosestEditableEdge(
                selectedLayerSelectionKey,
                editGeometrySelectedLayerStableKey,
                coordinates,
                e.mousePosition,
                hitRadiusPx,
                out var selectedHit) ||
            TryFindClosestEditableEdgeAtHitLayer(
                mouseDoc,
                e.mousePosition,
                coordinates,
                drawList,
                hitRadiusPx,
                out selectedHit))
        {
            selectedLayerSelectionKey = selectedHit.selectionKey;
            editGeometrySelectedLayerStableKey = selectedHit.stableKey;
            if (!TrySplitEditGeometryEdge(selectedHit.vertexA, selectedHit.vertexB, selectedHit.t, out var error))
                ReanimatorLog.Trace(
                    $"[Reanimator][SplitEdge][Warning] {error}",
                    !string.IsNullOrEmpty(error) && IsDebugModeEnabled);

            e.Use();
            GUI.changed = true;
        }
    }

    private bool TryFindClosestEditableEdgeAtHitLayer(
        Vector2 mouseDoc,
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        float hitRadiusPx,
        out EditEdgeHit hit)
    {
        hit = default;
        var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out var blockedByLayerLock);
        if (!hasHitLayer || blockedByLayerLock || hitLayer == null)
            return false;

        var selectionKey = BuildSelectionKey(hitLayer);
        var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
        return TryFindClosestEditableEdge(selectionKey, stableKey, coordinates, mouseCanvas, hitRadiusPx, out hit);
    }

}
#endif
