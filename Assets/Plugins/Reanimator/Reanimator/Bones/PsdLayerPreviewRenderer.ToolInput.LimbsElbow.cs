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
    private void HandleLimbsInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null)
            return;

        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.alt || e.button != 0)
            return;

        if (!localRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp)
                EndLimbInteraction();
            return;
        }

        var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
        var selectedLayer = FindLayerBySelectionKey(drawList, selectedLayerSelectionKey);
        var selectedStableKey = selectedLayer != null ? BuildKey(selectedLayer.name, selectedLayer.layerId) : string.Empty;

        if (e.type == EventType.MouseDown)
        {
            var hit = FindLimbHandleAt(e.mousePosition, coordinates, selectedLayerSelectionKey, selectedStableKey);
            if (hit.index >= 0)
            {
                RegisterUndo("Edit Limb");
                limbIsDragging = true;
                limbDraggedLineIndex = hit.index;
                limbDragLastMouseDoc = mouseDoc;
                limbSelectedLineIndex = hit.index;
                if (limbDraggedLineIndex >= 0 && limbDraggedLineIndex < limbLines.Count && !string.IsNullOrEmpty(selectedLayerSelectionKey))
                    limbLines[limbDraggedLineIndex].layerSelectionKey = selectedLayerSelectionKey;
                limbDragMode = hit.type == ElbowHandleType.Start
                    ? ElbowDragMode.MoveStart
                    : hit.type == ElbowHandleType.End
                        ? ElbowDragMode.MoveEnd
                        : ElbowDragMode.MoveLine;
                limbControlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = limbControlId;
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

            if (hitLayer != null)
            {
                RegisterUndo("Create Limb");
                selectedLayerSelectionKey = BuildSelectionKey(hitLayer);
                limbCreateLayerStableKey = BuildKey(hitLayer.name, hitLayer.layerId);
                if (CancelAllLimbLines())
                    hasPendingChanges = true;
                limbCreateStartDoc = mouseDoc;
                limbCreateEndDoc = mouseDoc;
                limbIsCreating = true;
                limbDragMode = ElbowDragMode.None;
                limbSelectedLineIndex = -1;
                limbControlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = limbControlId;
                e.Use();
                GUI.changed = true;
                return;
            }

            selectedLayerSelectionKey = string.Empty;
            limbDragMode = ElbowDragMode.None;
            limbSelectedLineIndex = -1;
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (GUIUtility.hotControl != limbControlId)
                return;

            if (limbIsDragging && limbDraggedLineIndex >= 0 && limbDraggedLineIndex < limbLines.Count)
            {
                var line = limbLines[limbDraggedLineIndex];
                if (line != null)
                {
                    var delta = mouseDoc - limbDragLastMouseDoc;
                    switch (limbDragMode)
                    {
                        case ElbowDragMode.MoveStart:
                            line.startDoc += delta;
                            break;
                        case ElbowDragMode.MoveEnd:
                            line.endDoc += delta;
                            break;
                        default:
                            line.startDoc += delta;
                            line.endDoc += delta;
                            break;
                    }
                    limbDragLastMouseDoc = mouseDoc;
                    RefreshLimbLineContour(line);
                    hasPendingChanges = true;
                }
                e.Use();
                GUI.changed = true;
                return;
            }

            if (limbIsCreating)
            {
                limbCreateEndDoc = mouseDoc;
                e.Use();
                GUI.changed = true;
            }
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            if (limbIsCreating && GUIUtility.hotControl == limbControlId)
                FinalizeLimbLineCreation();

            EndLimbInteraction();
            e.Use();
            GUI.changed = true;
        }
    }

    private void FinalizeLimbLineCreation()
    {
        if (!limbIsCreating)
            return;
        if (string.IsNullOrEmpty(limbCreateLayerStableKey))
            return;
        if ((limbCreateEndDoc - limbCreateStartDoc).sqrMagnitude <= 1e-6f)
            return;

        var line = new LimbLine
        {
            layerStableKey = limbCreateLayerStableKey,
            layerSelectionKey = selectedLayerSelectionKey,
            startDoc = limbCreateStartDoc,
            endDoc = limbCreateEndDoc
        };

        RefreshLimbLineContour(line);
        if (!line.hasContourPoints)
            return;

        limbLines.Add(line);
        limbSelectedLineIndex = limbLines.Count - 1;
        hasPendingChanges = true;
    }

    private void EndLimbInteraction()
    {
        limbIsCreating = false;
        limbCreateLayerStableKey = string.Empty;
        limbIsDragging = false;
        limbDraggedLineIndex = -1;
        limbDragLastMouseDoc = Vector2.zero;
        limbDragMode = ElbowDragMode.None;

        if (GUIUtility.hotControl == limbControlId)
            GUIUtility.hotControl = 0;
        limbControlId = 0;
    }

    private int FindActiveLimbLineIndex()
    {
        if (limbLines == null || limbLines.Count == 0)
            return -1;

        if (limbSelectedLineIndex >= 0 && limbSelectedLineIndex < limbLines.Count)
            return limbSelectedLineIndex;

        if (!string.IsNullOrEmpty(selectedLayerSelectionKey))
        {
            var layer = FindLayerBySelectionKey(layerInfos, selectedLayerSelectionKey);
            var stableKey = layer != null ? BuildKey(layer.name, layer.layerId) : string.Empty;
            for (var i = limbLines.Count - 1; i >= 0; i--)
            {
                var line = limbLines[i];
                if (line == null)
                    continue;
                if (IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectedLayerSelectionKey, stableKey))
                    return i;
            }
        }

        return limbLines.Count - 1;
    }

    private void RemoveLimbLineAt(int index)
    {
        if (index < 0 || index >= limbLines.Count)
            return;

        if (limbDraggedLineIndex == index)
            EndLimbInteraction();

        limbLines.RemoveAt(index);
        if (limbSelectedLineIndex == index)
            limbSelectedLineIndex = -1;
        else if (limbSelectedLineIndex > index)
            limbSelectedLineIndex--;
        if (limbDraggedLineIndex > index)
            limbDraggedLineIndex--;
    }

    private bool CancelAllLimbLines()
    {
        if (limbLines == null || limbLines.Count == 0)
            return false;

        for (var i = limbLines.Count - 1; i >= 0; i--)
            RemoveLimbLineAt(i);

        return true;
    }

    private ElbowHandleHit FindLimbHandleAt(
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        string selectionKey,
        string layerStableKey)
    {
        if (coordinates == null || string.IsNullOrEmpty(layerStableKey))
            return ElbowHandleHit.None;

        const float hitDistance = 7f;
        for (var i = limbLines.Count - 1; i >= 0; i--)
        {
            var line = limbLines[i];
            if (line == null)
                continue;
            if (!IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectionKey, layerStableKey))
                continue;

            var a = coordinates.DocumentToView(line.startDoc);
            var b = coordinates.DocumentToView(line.endDoc);
            var drawThickness = i == limbSelectedLineIndex ? 3f : 2f;

            if (BoneShapeDrawer.IsPointOverBase(mouseCanvas, a, drawThickness, 3f))
                return new ElbowHandleHit(i, ElbowHandleType.Start);

            if (BoneShapeDrawer.IsPointOverBase(mouseCanvas, b, drawThickness, 3f))
                return new ElbowHandleHit(i, ElbowHandleType.End);

            if (DistancePointToSegment(mouseCanvas, a, b) <= hitDistance)
                return new ElbowHandleHit(i, ElbowHandleType.Line);
        }

        return ElbowHandleHit.None;
    }

    private void HandleElbowInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null)
            return;

        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.alt || e.button != 0)
            return;

        if (!localRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp)
                EndElbowInteraction();
            return;
        }

        var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
        var selectedLayer = FindLayerBySelectionKey(drawList, selectedLayerSelectionKey);
        var selectedStableKey = selectedLayer != null ? BuildKey(selectedLayer.name, selectedLayer.layerId) : string.Empty;

        if (e.type == EventType.MouseDown)
        {
            var hit = FindElbowHandleAt(e.mousePosition, coordinates, selectedLayerSelectionKey, selectedStableKey);
            if (hit.index >= 0)
            {
                RegisterUndo("Edit Elbow");
                elbowIsDragging = true;
                elbowDraggedLineIndex = hit.index;
                elbowDragLastMouseDoc = mouseDoc;
                elbowSelectedLineIndex = hit.index;
                if (elbowDraggedLineIndex >= 0 && elbowDraggedLineIndex < elbowLines.Count && !string.IsNullOrEmpty(selectedLayerSelectionKey))
                    elbowLines[elbowDraggedLineIndex].layerSelectionKey = selectedLayerSelectionKey;
                elbowDragMode = hit.type == ElbowHandleType.Start
                    ? ElbowDragMode.MoveStart
                    : hit.type == ElbowHandleType.End
                        ? ElbowDragMode.MoveEnd
                        : ElbowDragMode.MoveLine;
                elbowControlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = elbowControlId;
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

            if (hitLayer != null)
            {
                RegisterUndo("Create Elbow");
                selectedLayerSelectionKey = BuildSelectionKey(hitLayer);
                elbowCreateLayerStableKey = BuildKey(hitLayer.name, hitLayer.layerId);
                if (CancelAllElbowLines())
                    hasPendingChanges = true;
                elbowCreateStartDoc = mouseDoc;
                elbowCreateEndDoc = mouseDoc;
                elbowIsCreating = true;
                elbowDragMode = ElbowDragMode.None;
                elbowSelectedLineIndex = -1;
                elbowControlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = elbowControlId;
                e.Use();
                GUI.changed = true;
                return;
            }

            selectedLayerSelectionKey = string.Empty;
            elbowDragMode = ElbowDragMode.None;
            elbowSelectedLineIndex = -1;
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (GUIUtility.hotControl != elbowControlId)
                return;

            if (elbowIsDragging && elbowDraggedLineIndex >= 0 && elbowDraggedLineIndex < elbowLines.Count)
            {
                var line = elbowLines[elbowDraggedLineIndex];
                if (line != null)
                {
                    var delta = mouseDoc - elbowDragLastMouseDoc;
                    switch (elbowDragMode)
                    {
                        case ElbowDragMode.MoveStart:
                            line.startDoc += delta;
                            break;
                        case ElbowDragMode.MoveEnd:
                            line.endDoc += delta;
                            break;
                        default:
                            line.startDoc += delta;
                            line.endDoc += delta;
                            break;
                    }
                    elbowDragLastMouseDoc = mouseDoc;
                    RefreshElbowLineContour(line);
                    hasPendingChanges = true;
                }
                e.Use();
                GUI.changed = true;
                return;
            }

            if (elbowIsCreating)
            {
                elbowCreateEndDoc = mouseDoc;
                e.Use();
                GUI.changed = true;
            }
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            if (elbowIsCreating && GUIUtility.hotControl == elbowControlId)
                FinalizeElbowLineCreation();

            EndElbowInteraction();
            e.Use();
            GUI.changed = true;
        }
    }

    private void FinalizeElbowLineCreation()
    {
        if (!elbowIsCreating)
            return;
        if (string.IsNullOrEmpty(elbowCreateLayerStableKey))
            return;
        if ((elbowCreateEndDoc - elbowCreateStartDoc).sqrMagnitude <= 1e-6f)
            return;

        var line = new ElbowLine
        {
            layerStableKey = elbowCreateLayerStableKey,
            layerSelectionKey = selectedLayerSelectionKey,
            startDoc = elbowCreateStartDoc,
            endDoc = elbowCreateEndDoc
        };

        RefreshElbowLineContour(line);
        if (!line.hasContourPoints)
            return;

        elbowLines.Add(line);
        elbowSelectedLineIndex = elbowLines.Count - 1;
        hasPendingChanges = true;
    }

    private void EndElbowInteraction()
    {
        elbowIsCreating = false;
        elbowCreateLayerStableKey = string.Empty;
        elbowIsDragging = false;
        elbowDraggedLineIndex = -1;
        elbowDragLastMouseDoc = Vector2.zero;
        elbowDragMode = ElbowDragMode.None;

        if (GUIUtility.hotControl == elbowControlId)
            GUIUtility.hotControl = 0;
        elbowControlId = 0;
    }

    private int FindActiveElbowLineIndex()
    {
        if (elbowLines == null || elbowLines.Count == 0)
            return -1;

        if (elbowSelectedLineIndex >= 0 && elbowSelectedLineIndex < elbowLines.Count)
            return elbowSelectedLineIndex;

        if (!string.IsNullOrEmpty(selectedLayerSelectionKey))
        {
            var layer = FindLayerBySelectionKey(layerInfos, selectedLayerSelectionKey);
            var stableKey = layer != null ? BuildKey(layer.name, layer.layerId) : string.Empty;
            for (var i = elbowLines.Count - 1; i >= 0; i--)
            {
                var line = elbowLines[i];
                if (line == null)
                    continue;
                if (IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectedLayerSelectionKey, stableKey))
                    return i;
            }
        }

        return elbowLines.Count - 1;
    }

    private void RemoveElbowLineAt(int index)
    {
        if (index < 0 || index >= elbowLines.Count)
            return;

        if (elbowDraggedLineIndex == index)
            EndElbowInteraction();

        elbowLines.RemoveAt(index);
        if (elbowSelectedLineIndex == index)
            elbowSelectedLineIndex = -1;
        else if (elbowSelectedLineIndex > index)
            elbowSelectedLineIndex--;
        if (elbowDraggedLineIndex > index)
            elbowDraggedLineIndex--;
    }

    private bool CancelAllElbowLines()
    {
        if (elbowLines == null || elbowLines.Count == 0)
            return false;

        for (var i = elbowLines.Count - 1; i >= 0; i--)
            RemoveElbowLineAt(i);

        return true;
    }

    private LayerInfo FindLayerBySelectionKey(IReadOnlyList<LayerInfo> drawList, string selectionKey)
    {
        if (drawList == null || string.IsNullOrEmpty(selectionKey))
            return null;

        for (var i = 0; i < drawList.Count; i++)
        {
            var layer = drawList[i];
            if (layer == null)
                continue;
            if (!string.Equals(BuildSelectionKey(layer), selectionKey, StringComparison.OrdinalIgnoreCase))
                continue;
            return layer;
        }

        return null;
    }

    private LayerInfo FindTopLayerAt(Vector2 mouseDoc, IReadOnlyList<LayerInfo> drawList)
    {
        if (drawList == null)
            return null;

        for (var i = drawList.Count - 1; i >= 0; i--)
        {
            var layer = drawList[i];
            if (layer == null || !layer.visible)
                continue;
            if (!IsPointInsideLayer(layer, mouseDoc))
                continue;
            return layer;
        }

        return null;
    }

    private bool TryGetToolLockedHitLayer(
        Vector2 mouseDoc,
        IReadOnlyList<LayerInfo> drawList,
        out LayerInfo hitLayer,
        out bool blockedByLayerLock)
    {
        blockedByLayerLock = false;
        if (string.IsNullOrEmpty(selectedLayerSelectionKey))
        {
            hitLayer = FindTopLayerAt(mouseDoc, drawList);
            return hitLayer != null;
        }

        var lockedLayer = FindLayerBySelectionKey(drawList, selectedLayerSelectionKey);
        if (lockedLayer == null)
        {
            hitLayer = FindTopLayerAt(mouseDoc, drawList);
            return hitLayer != null;
        }

        // Tool lock: always target the currently selected layer if the click hits it,
        // even when another visible sprite is above it.
        if (IsPointInsideLayer(lockedLayer, mouseDoc))
        {
            hitLayer = lockedLayer;
            return true;
        }

        hitLayer = null;
        blockedByLayerLock = true;
        return false;
    }

    private ElbowHandleHit FindElbowHandleAt(
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        string selectionKey,
        string layerStableKey)
    {
        if (coordinates == null || string.IsNullOrEmpty(layerStableKey))
            return ElbowHandleHit.None;

        const float hitDistance = 7f;
        for (var i = elbowLines.Count - 1; i >= 0; i--)
        {
            var line = elbowLines[i];
            if (line == null)
                continue;
            if (!IsElbowLineForLayer(line, selectionKey, layerStableKey))
                continue;

            var a = coordinates.DocumentToView(line.startDoc);
            var b = coordinates.DocumentToView(line.endDoc);
            var drawThickness = i == elbowSelectedLineIndex ? 3f : 2f;

            if (BoneShapeDrawer.IsPointOverBase(mouseCanvas, a, drawThickness, 3f))
                return new ElbowHandleHit(i, ElbowHandleType.Start);

            if (BoneShapeDrawer.IsPointOverBase(mouseCanvas, b, drawThickness, 3f))
                return new ElbowHandleHit(i, ElbowHandleType.End);

            if (DistancePointToSegment(mouseCanvas, a, b) <= hitDistance)
                return new ElbowHandleHit(i, ElbowHandleType.Line);
        }

        return ElbowHandleHit.None;
    }
}
#endif
