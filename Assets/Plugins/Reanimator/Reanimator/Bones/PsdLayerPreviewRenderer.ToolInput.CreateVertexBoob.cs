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
    private void HandleBoobInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        if (BoobToolVersion == 1)
        {
            HandleBoobV2Input(localRect, coordinates, drawList);
            return;
        }

        var e = Event.current;
        if (e == null || drawList == null)
            return;
        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.alt || e.button != 0)
            return;

        if (!localRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == boobControlId)
            {
                GUIUtility.hotControl = 0;
                boobIsDragging = false;
                boobControlId = 0;
                e.Use();
            }
            return;
        }

        var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
        if (e.type == EventType.MouseDown)
        {
            var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out var blockedByLayerLock);
            if (!hasHitLayer && blockedByLayerLock)
            {
                e.Use();
                return;
            }

            if (hitLayer == null)
            {
                e.Use();
                return;
            }

            var selectionKey = BuildSelectionKey(hitLayer);
            if (string.IsNullOrEmpty(selectionKey))
            {
                e.Use();
                return;
            }

            var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
            RegisterUndo("Set Boob Point");
            selectedLayerSelectionKey = selectionKey;
            boobLayerStableKey = stableKey;

            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var data) && data != null)
                boobCenterDoc = ConvertPointToBaseDoc(data, mouseDoc);
            else
                boobCenterDoc = mouseDoc;

            boobHasPoint = true;
            boobIsDragging = true;
            boobControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = boobControlId;
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (GUIUtility.hotControl != boobControlId || !boobIsDragging)
                return;

            if (TryGetLayerVertexDataForKeys(selectedLayerSelectionKey, boobLayerStableKey, out var data) && data != null)
                boobCenterDoc = ConvertPointToBaseDoc(data, mouseDoc);
            else
                boobCenterDoc = mouseDoc;

            boobHasPoint = true;
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseUp && GUIUtility.hotControl == boobControlId)
        {
            GUIUtility.hotControl = 0;
            boobIsDragging = false;
            boobControlId = 0;
            e.Use();
            GUI.changed = true;
        }
    }

    private void HandleBoobV2Input(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null || drawList == null)
            return;
        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.alt || e.button != 0)
            return;

        if (!localRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == boobControlId)
            {
                FinishBoobV2Interaction();
                e.Use();
                GUI.changed = true;
            }
            return;
        }

        var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
        if (e.type == EventType.MouseDown)
        {
            if (BoobV2Selection <= 1)
            {
                RegisterUndo(BoobV2Selection == 0 ? "Set Left Boob V2 Target" : "Set Right Boob V2 Target");
                LayerInfo targetLayer = null;
                if (!TryFindBoobConstraintLayer(mouseDoc, out targetLayer) || targetLayer == null)
                    targetLayer = FindTopLayerAt(mouseDoc, drawList);
                if (targetLayer != null)
                {
                    var selectionKey = BuildSelectionKey(targetLayer);
                    var stableKey = BuildKey(targetLayer.name, targetLayer.layerId);
                    if (!string.IsNullOrEmpty(selectionKey))
                        selectedLayerSelectionKey = selectionKey;
                    if (!string.IsNullOrEmpty(stableKey))
                        boobV2LayerStableKey = stableKey;
                }

                boobV2ActiveTarget = BoobV2Selection;
                if (boobV2ActiveTarget == 0)
                {
                    boobV2TargetADoc = mouseDoc;
                    boobV2HasTargetA = true;
                }
                else
                {
                    boobV2TargetBDoc = mouseDoc;
                    boobV2HasTargetB = true;
                }
            }
            else
            {
                RegisterUndo(BoobV2Selection == 2 ? "Draw Left Boob V2 Contour" : "Draw Right Boob V2 Contour");
                boobV2DrawingContour = BoobV2Selection - 2;
                boobV2WorkingContourDoc.Clear();
                boobV2WorkingContourDoc.Add(mouseDoc);
                boobV2LastStrokeView = e.mousePosition;
            }

            boobIsDragging = true;
            boobControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = boobControlId;
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (GUIUtility.hotControl != boobControlId || !boobIsDragging)
                return;

            if (boobV2ActiveTarget == 0)
                boobV2TargetADoc = mouseDoc;
            else if (boobV2ActiveTarget == 1)
                boobV2TargetBDoc = mouseDoc;
            else if (boobV2DrawingContour >= 0 &&
                     Vector2.Distance(e.mousePosition, boobV2LastStrokeView) >= 3f)
            {
                boobV2WorkingContourDoc.Add(mouseDoc);
                boobV2LastStrokeView = e.mousePosition;
            }

            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseUp && GUIUtility.hotControl == boobControlId)
        {
            FinishBoobV2Interaction();
            e.Use();
            GUI.changed = true;
        }
    }

    private void FinishBoobV2Interaction()
    {
        if (GUIUtility.hotControl == boobControlId && boobControlId != 0)
            GUIUtility.hotControl = 0;

        if (boobV2DrawingContour >= 0)
        {
            var raw = new List<Vector2>(boobV2WorkingContourDoc);
            var smoothed = BuildSmoothedClosedBoobV2Contour(
                raw,
                BoobV2SmoothingPasses,
                BoobV2ContourDensity);
            if (smoothed.Count >= 3)
            {
                if (boobV2DrawingContour == 0)
                {
                    boobV2RawContourADoc = raw;
                    boobV2ContourADoc = smoothed;
                }
                else
                {
                    boobV2RawContourBDoc = raw;
                    boobV2ContourBDoc = smoothed;
                }
            }
        }

        boobIsDragging = false;
        boobControlId = 0;
        boobV2ActiveTarget = -1;
        boobV2DrawingContour = -1;
        boobV2WorkingContourDoc.Clear();
    }

    private void RebuildBoobV2ContoursFromRaw()
    {
        RebuildBoobV2ContourFromRaw(ref boobV2RawContourADoc, ref boobV2ContourADoc);
        RebuildBoobV2ContourFromRaw(ref boobV2RawContourBDoc, ref boobV2ContourBDoc);
    }

    private void RebuildBoobV2ContourFromRaw(
        ref List<Vector2> rawContour,
        ref List<Vector2> smoothedContour)
    {
        if (rawContour == null)
            rawContour = new List<Vector2>();
        if (smoothedContour == null)
            smoothedContour = new List<Vector2>();

        IReadOnlyList<Vector2> source = rawContour.Count >= 3 ? rawContour : smoothedContour;
        if (source.Count < 3)
            return;
        if (rawContour.Count < 3)
            rawContour = new List<Vector2>(source);

        var rebuilt = BuildSmoothedClosedBoobV2Contour(
            rawContour,
            BoobV2SmoothingPasses,
            BoobV2ContourDensity);
        if (rebuilt.Count >= 3)
            smoothedContour = rebuilt;
    }

    private static List<Vector2> BuildSmoothedClosedBoobV2Contour(
        IReadOnlyList<Vector2> source,
        int smoothingPasses,
        int targetPointCount)
    {
        var cleaned = new List<Vector2>();
        if (source == null)
            return cleaned;
        for (var i = 0; i < source.Count; i++)
        {
            if (cleaned.Count > 0 && (cleaned[cleaned.Count - 1] - source[i]).sqrMagnitude < 0.0625f)
                continue;
            cleaned.Add(source[i]);
        }
        if (cleaned.Count > 2 && (cleaned[0] - cleaned[cleaned.Count - 1]).sqrMagnitude < 0.0625f)
            cleaned.RemoveAt(cleaned.Count - 1);
        if (cleaned.Count < 3)
            return new List<Vector2>();

        var perimeter = 0f;
        for (var i = 0; i < cleaned.Count; i++)
            perimeter += Vector2.Distance(cleaned[i], cleaned[(i + 1) % cleaned.Count]);
        if (perimeter <= 0.001f)
            return new List<Vector2>();

        var pointCount = Mathf.Clamp(targetPointCount, 8, 128);
        var step = perimeter / pointCount;
        var resampled = new List<Vector2>(pointCount);
        var edgeIndex = 0;
        var edgeStart = cleaned[0];
        var edgeEnd = cleaned[1];
        var edgeLength = Vector2.Distance(edgeStart, edgeEnd);
        var walked = 0f;
        for (var sample = 0; sample < pointCount; sample++)
        {
            var targetDistance = sample * step;
            var edgeGuard = 0;
            while (walked + edgeLength < targetDistance && edgeGuard++ < cleaned.Count)
            {
                walked += edgeLength;
                edgeIndex = (edgeIndex + 1) % cleaned.Count;
                edgeStart = cleaned[edgeIndex];
                edgeEnd = cleaned[(edgeIndex + 1) % cleaned.Count];
                edgeLength = Vector2.Distance(edgeStart, edgeEnd);
            }
            var t = edgeLength > 0.0001f ? Mathf.Clamp01((targetDistance - walked) / edgeLength) : 0f;
            resampled.Add(Vector2.Lerp(edgeStart, edgeEnd, t));
        }

        var passes = Mathf.Clamp(smoothingPasses, 0, 6);
        for (var pass = 0; pass < passes; pass++)
        {
            var next = new List<Vector2>(resampled.Count);
            for (var i = 0; i < resampled.Count; i++)
            {
                var previous = resampled[(i + resampled.Count - 1) % resampled.Count];
                var current = resampled[i];
                var following = resampled[(i + 1) % resampled.Count];
                next.Add(Vector2.Lerp(current, (previous + following) * 0.5f, 0.35f));
            }
            resampled = next;
        }
        return resampled;
    }
}
#endif
