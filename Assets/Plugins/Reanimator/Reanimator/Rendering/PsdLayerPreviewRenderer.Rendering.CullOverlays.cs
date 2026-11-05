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
    private void DrawRadiusCullOverlay(PsdCoordinateSpace coordinates, LayerInfo selectedLayer, float alpha, float radius, Color overlayColor)
    {
        if (!radiusCullHasPoint || selectedLayer == null || coordinates == null)
            return;
        if (radius <= 0.0001f)
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;

        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (!string.IsNullOrEmpty(radiusCullLayerStableKey) &&
            !string.Equals(radiusCullLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return;

        var centerDoc = radiusCullCenterDoc;
        LayerVertexData data = null;
        if (TryGetLayerVertexDataForKeys(selectedLayerSelectionKey, stableKey, out data) && data != null)
            TryGetJointCenterInDrawDoc(data, out centerDoc);

        Handles.BeginGUI();
        var prev = Handles.color;
        var centerCanvas = coordinates.DocumentToView(centerDoc);
        var baseColor = new Color(overlayColor.r, overlayColor.g, overlayColor.b, Mathf.Clamp01(alpha * 0.95f));
        Handles.color = baseColor;

        if (data != null &&
            TryBuildJointRingDrawDoc(data, radius, 96, out var ringDrawDoc) &&
            ringDrawDoc != null &&
            ringDrawDoc.Length >= 3)
        {
            EnsureVertexBuffer(ref vertexBoundarySegmentsShifted, ringDrawDoc.Length + 1);
            for (var i = 0; i < ringDrawDoc.Length; i++)
                vertexBoundarySegmentsShifted[i] = coordinates.DocumentToView(ringDrawDoc[i]);
            vertexBoundarySegmentsShifted[ringDrawDoc.Length] = vertexBoundarySegmentsShifted[0];
            Handles.DrawAAPolyLine(1.8f, vertexBoundarySegmentsShifted);
        }
        else
        {
            Handles.DrawWireDisc(centerCanvas, Vector3.forward, Mathf.Max(0.01f, radius));
        }

        Handles.DrawSolidDisc(centerCanvas, Vector3.forward, 2.5f);

        var handleDir = new Vector2(Mathf.Cos(radiusCullHandleAngleRad), Mathf.Sin(radiusCullHandleAngleRad));
        var handleBaseDoc = radiusCullCenterDoc + handleDir * Mathf.Max(0.01f, radius);
        var handleDrawDoc = handleBaseDoc;
        if (data != null)
            TryMapRadiusCullBasePointToDrawDoc(data, handleBaseDoc, out handleDrawDoc);
        var handleCanvas = coordinates.DocumentToView(handleDrawDoc);
        Handles.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, Mathf.Clamp01(alpha));
        Handles.DrawAAPolyLine(1.2f, centerCanvas, handleCanvas);
        Handles.DrawSolidDisc(handleCanvas, Vector3.forward, 3.3f);
        Handles.color = new Color(0.1f, 0.1f, 0.1f, Mathf.Clamp01(alpha * 0.85f));
        Handles.DrawWireDisc(handleCanvas, Vector3.forward, 4.6f);

        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawJointOverlay(PsdCoordinateSpace coordinates, LayerInfo selectedLayer, float alpha, float radius)
    {
        if (!radiusCullHasPoint || selectedLayer == null || coordinates == null)
            return;
        if (radius <= 0.0001f)
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;

        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (!string.IsNullOrEmpty(radiusCullLayerStableKey) &&
            !string.Equals(radiusCullLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return;

        var centerDoc = radiusCullCenterDoc;
        LayerVertexData data = null;
        if (TryGetLayerVertexDataForKeys(selectedLayerSelectionKey, stableKey, out data) && data != null)
            TryGetRadiusCullCenterInDrawDoc(data, out centerDoc);

        var overlayColor = new Color(0.2f, 0.55f, 1f, 1f);
        Handles.BeginGUI();
        var prev = Handles.color;
        var centerCanvas = coordinates.DocumentToView(centerDoc);
        Handles.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, Mathf.Clamp01(alpha * 0.95f));

        if (data != null &&
            TryBuildRadiusCullRingDrawDoc(data, radius, 96, out var ringDrawDoc) &&
            ringDrawDoc != null &&
            ringDrawDoc.Length >= 3)
        {
            EnsureVertexBuffer(ref vertexBoundarySegmentsShifted, ringDrawDoc.Length + 1);
            for (var i = 0; i < ringDrawDoc.Length; i++)
                vertexBoundarySegmentsShifted[i] = coordinates.DocumentToView(ringDrawDoc[i]);
            vertexBoundarySegmentsShifted[ringDrawDoc.Length] = vertexBoundarySegmentsShifted[0];
            Handles.DrawAAPolyLine(1.8f, vertexBoundarySegmentsShifted);
        }
        else
        {
            Handles.DrawWireDisc(centerCanvas, Vector3.forward, Mathf.Max(0.01f, radius));
        }

        var topBaseDoc = radiusCullCenterDoc + Vector2.up * Mathf.Max(0.01f, radius);
        var topDrawDoc = topBaseDoc;
        if (data != null)
            TryMapJointBasePointToDrawDoc(data, topBaseDoc, out topDrawDoc);
        var topCanvas = coordinates.DocumentToView(topDrawDoc);

        var nearestBoneIndex = jointNearestBoneIndex;
        if ((nearestBoneIndex < 0 || nearestBoneIndex >= bones.Count || bones[nearestBoneIndex] == null) && radiusCullHasPoint)
        {
            nearestBoneIndex = FindNearestBoneIndex(radiusCullCenterDoc);
            jointNearestBoneIndex = nearestBoneIndex;
        }

        if (nearestBoneIndex >= 0 && nearestBoneIndex < bones.Count && bones[nearestBoneIndex] != null)
        {
            var nearestBone = bones[nearestBoneIndex];
            Handles.color = new Color(1f, 0.2f, 0.2f, Mathf.Clamp01(alpha * 0.95f));
            Handles.DrawDottedLine(topCanvas, coordinates.DocumentToView(GetBoneBasePoint(nearestBone)), 4f);

            var parentIndex = nearestBone.parentId;
            if (parentIndex >= 0 && parentIndex < bones.Count && bones[parentIndex] != null)
            {
                Handles.color = new Color(1f, 0.58f, 0.12f, Mathf.Clamp01(alpha * 0.95f));
                Handles.DrawDottedLine(topCanvas, coordinates.DocumentToView(GetBoneBasePoint(bones[parentIndex])), 4f);
            }
        }

        var startBaseDoc = radiusCullCenterDoc + new Vector2(Mathf.Cos(jointStartAngleRad), Mathf.Sin(jointStartAngleRad)) * Mathf.Max(0.01f, radius);
        var endBaseDoc = radiusCullCenterDoc + new Vector2(Mathf.Cos(jointEndAngleRad), Mathf.Sin(jointEndAngleRad)) * Mathf.Max(0.01f, radius);
        var startDrawDoc = startBaseDoc;
        var endDrawDoc = endBaseDoc;
        if (data != null)
        {
            TryMapJointBasePointToDrawDoc(data, startBaseDoc, out startDrawDoc);
            TryMapJointBasePointToDrawDoc(data, endBaseDoc, out endDrawDoc);
        }

        var startCanvas = coordinates.DocumentToView(startDrawDoc);
        var endCanvas = coordinates.DocumentToView(endDrawDoc);
        var middleDrawDoc = GetJointMiddleHandleDrawDoc(data, radius);
        var middleCanvas = coordinates.DocumentToView(middleDrawDoc);
        var startOffsetDrawDoc = GetJointOffsetHandleDrawDoc(data, radius, true);
        var endOffsetDrawDoc = GetJointOffsetHandleDrawDoc(data, radius, false);
        var startOffsetCanvas = coordinates.DocumentToView(startOffsetDrawDoc);
        var endOffsetCanvas = coordinates.DocumentToView(endOffsetDrawDoc);
        Handles.DrawAAPolyLine(1.2f, centerCanvas, startCanvas);
        Handles.DrawAAPolyLine(1.2f, centerCanvas, endCanvas);

        GetJointMinorSector(jointStartAngleRad, jointEndAngleRad, out var sectorStartAngle, out _, out var sectorSpan);
        var segmentCount = Mathf.Clamp(Mathf.RoundToInt(sectorSpan / (Mathf.PI * 2f) * 96f), 6, 96);

        var fillPoints = new Vector3[segmentCount + 2];
        fillPoints[0] = centerCanvas;
        for (var i = 0; i <= segmentCount; i++)
        {
            var t = segmentCount > 0 ? i / (float)segmentCount : 0f;
            var angle = sectorStartAngle + sectorSpan * t;
            var arcBase = radiusCullCenterDoc + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Max(0.01f, radius);
            var arcDraw = arcBase;
            if (data != null)
                TryMapJointBasePointToDrawDoc(data, arcBase, out arcDraw);
            fillPoints[i + 1] = coordinates.DocumentToView(arcDraw);
        }
        Handles.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, 0.1f * Mathf.Clamp01(alpha));
        Handles.DrawAAConvexPolygon(fillPoints);

        EnsureVertexBuffer(ref vertexBoundarySegmentsShifted, segmentCount + 1);
        for (var i = 0; i <= segmentCount; i++)
        {
            var t = segmentCount > 0 ? i / (float)segmentCount : 0f;
            var angle = sectorStartAngle + sectorSpan * t;
            var arcBase = radiusCullCenterDoc + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Max(0.01f, radius);
            var arcDraw = arcBase;
            if (data != null)
                TryMapJointBasePointToDrawDoc(data, arcBase, out arcDraw);
            vertexBoundarySegmentsShifted[i] = coordinates.DocumentToView(arcDraw);
        }
        Handles.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, Mathf.Clamp01(alpha * 0.95f));
        Handles.DrawAAPolyLine(2.1f, vertexBoundarySegmentsShifted);

        Handles.DrawSolidDisc(centerCanvas, Vector3.forward, 2.5f);
        Handles.DrawSolidDisc(startCanvas, Vector3.forward, 3.3f);
        Handles.DrawSolidDisc(endCanvas, Vector3.forward, 3.3f);
        Handles.color = new Color(0.05f, 0.1f, 0.2f, Mathf.Clamp01(alpha * 0.9f));
        Handles.DrawWireDisc(startCanvas, Vector3.forward, 4.8f);
        Handles.DrawWireDisc(endCanvas, Vector3.forward, 4.8f);

        var middleColor = new Color(1f, 0.58f, 0.12f, Mathf.Clamp01(alpha * 0.98f));
        Handles.color = middleColor;
        Handles.DrawAAPolyLine(2.2f, centerCanvas, middleCanvas);
        Handles.DrawSolidDisc(middleCanvas, Vector3.forward, 4.2f);
        Handles.color = new Color(0.35f, 0.16f, 0.02f, Mathf.Clamp01(alpha * 0.95f));
        Handles.DrawWireDisc(middleCanvas, Vector3.forward, 5.6f);

        var offsetHandleColor = new Color(0.72f, 0.72f, 0.72f, Mathf.Clamp01(alpha * 0.96f));
        Handles.color = offsetHandleColor;
        Handles.DrawAAPolyLine(1.5f, centerCanvas, startOffsetCanvas);
        Handles.DrawAAPolyLine(1.5f, centerCanvas, endOffsetCanvas);
        Handles.DrawSolidDisc(startOffsetCanvas, Vector3.forward, 3.8f);
        Handles.DrawSolidDisc(endOffsetCanvas, Vector3.forward, 3.8f);
        Handles.color = new Color(0.22f, 0.22f, 0.22f, Mathf.Clamp01(alpha * 0.92f));
        Handles.DrawWireDisc(startOffsetCanvas, Vector3.forward, 5f);
        Handles.DrawWireDisc(endOffsetCanvas, Vector3.forward, 5f);

        if (TryGetJointLockButtonRect(coordinates, data, radius, out var lockButtonRect))
        {
            var buttonFill = new Color(0.2f, 0.5f, 0.95f, Mathf.Clamp01(alpha * 0.95f));
            var buttonBorder = new Color(0.05f, 0.1f, 0.2f, Mathf.Clamp01(alpha));
            EditorGUI.DrawRect(lockButtonRect, jointCenterLockEnabled
                ? new Color(0.95f, 0.6f, 0.18f, Mathf.Clamp01(alpha * 0.98f))
                : buttonFill);
            DrawRectOutline(lockButtonRect, buttonBorder, 1f);

            var buttonLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 8,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.96f, 0.98f, 1f, Mathf.Clamp01(alpha)) }
            };
            GUI.Label(lockButtonRect, "LOCK", buttonLabelStyle);
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawJointV2BoneAngleOverlay(Rect previewRect)
    {
        if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null)
            return;

        var currentEvent = Event.current;
        if (currentEvent == null || currentEvent.type != EventType.Repaint || !previewRect.Contains(currentEvent.mousePosition))
            return;

        var bone = bones[selectedBoneIndex];
        var angle = ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
            bone.initialRotation,
            bone.rotation);
        var label = angle.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "\u00B0";
        var style = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        var content = new GUIContent(label);
        var size = style.CalcSize(content);
        var width = Mathf.Max(48f, size.x + 10f);
        const float height = 20f;
        const float cursorGap = 10f;
        var labelRect = new Rect(
            Mathf.Max(previewRect.x + 2f, currentEvent.mousePosition.x - width - cursorGap),
            Mathf.Clamp(currentEvent.mousePosition.y - height * 0.5f, previewRect.y + 2f, previewRect.yMax - height - 2f),
            width,
            height);

        EditorGUI.DrawRect(labelRect, new Color(0.05f, 0.07f, 0.1f, 0.88f));
        GUI.Label(labelRect, content, style);
    }

    private void DrawJointV2PoseIntersectionOverlay(
        PsdCoordinateSpace coordinates,
        float specialJointDistance)
    {
        if (coordinates == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        Handles.BeginGUI();
        var previousColor = Handles.color;
        DrawJointV2PoseIntersectionPair(coordinates, 0f, new Color(1f, 0.52f, 0.08f, 1f));
        DrawJointV2PoseIntersectionPair(coordinates, -45f, new Color(0.08f, 0.9f, 0.82f, 1f));
        DrawJointV2PoseIntersectionPair(coordinates, -90f, new Color(0.12f, 0.42f, 1f, 1f));
        DrawJointV2AttachedBonePoints(coordinates);
        DrawJointV2SpecialAnchorPoints(coordinates, specialJointDistance);
        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private void DrawJointV2PoseIntersectionPair(
        PsdCoordinateSpace coordinates,
        float poseAngleDegrees,
        Color color)
    {
        if (!TryGetJointV2PoseIntersectionPair(poseAngleDegrees, out var pointA, out var pointB))
            return;

        DrawJointV2PoseIntersectionPoint(coordinates.DocumentToView(pointA), color);
        DrawJointV2PoseIntersectionPoint(coordinates.DocumentToView(pointB), color);
    }

    private static void DrawJointV2PoseIntersectionPoint(Vector2 viewPoint, Color color)
    {
        Handles.color = new Color(0.025f, 0.035f, 0.055f, 0.96f);
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, 8.5f);
        Handles.color = color;
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, 6.5f);
    }

    private void DrawJointV2AttachedBonePoints(PsdCoordinateSpace coordinates)
    {
        if (!TryGetJointV2AttachedBonePoints(out var pointA, out var pointB))
            return;

        DrawJointV2AttachedBonePoint(coordinates.DocumentToView(pointA));
        DrawJointV2AttachedBonePoint(coordinates.DocumentToView(pointB));
    }

    private static void DrawJointV2AttachedBonePoint(Vector2 viewPoint)
    {
        Handles.color = Color.white;
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, 4.8f);
        Handles.color = new Color(0.025f, 0.035f, 0.055f, 0.98f);
        Handles.DrawWireDisc(viewPoint, Vector3.forward, 5.9f);
    }

    private void DrawJointV2SpecialAnchorPoints(
        PsdCoordinateSpace coordinates,
        float specialJointDistance)
    {
        var contourPoints = new List<Vector2>(16);
        if (!TryGetJointV2AttachedGuidePoints(
                specialJointDistance,
                out _,
                out var anchorPointA,
                out var anchorPointB,
                out var isSpecial,
                contourPoints) ||
            !isSpecial)
        {
            return;
        }

        var green = new Color(0.18f, 1f, 0.28f, 1f);
        DrawJointV2PoseIntersectionPoint(coordinates.DocumentToView(anchorPointA), green);
        DrawJointV2PoseIntersectionPoint(coordinates.DocumentToView(anchorPointB), green);
    }

    private void DrawJointV2AttachedGuidePoints(
        PsdCoordinateSpace coordinates,
        float specialJointDistance)
    {
        var contourPoints = new List<Vector2>(16);
        if (!TryGetJointV2AttachedGuidePoints(
                specialJointDistance,
                out var centerPoint,
                out _,
                out _,
                out _,
                contourPoints))
            return;

        DrawJointV2GuidePoint(
            coordinates.DocumentToView(centerPoint),
            new Color(1f, 0.08f, 0.06f, 1f),
            6.1f);
        for (var pointIndex = 0; pointIndex < contourPoints.Count; pointIndex++)
        {
            DrawJointV2GuidePoint(
                coordinates.DocumentToView(contourPoints[pointIndex]),
                new Color(1f, 0.04f, 0.42f, 1f),
                5.4f);
        }
    }

    private static void DrawJointV2GuidePoint(Vector2 viewPoint, Color color, float radius)
    {
        Handles.color = new Color(0.025f, 0.035f, 0.055f, 0.98f);
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, radius + 2f);
        Handles.color = color;
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, radius);
    }

}
#endif
