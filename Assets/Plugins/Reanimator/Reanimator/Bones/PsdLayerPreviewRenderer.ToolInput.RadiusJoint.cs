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
    private void HandleRadiusCullInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        float radiusCullPreviewRadius,
        bool handleOnly = false)
    {
        var e = Event.current;
        if (e == null || drawList == null)
            return;
        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.alt || e.button != 0)
            return;

        var activeRadius = radiusCullHasRadiusOverride
            ? Mathf.Max(0.01f, radiusCullRadiusOverride)
            : Mathf.Max(0.01f, radiusCullPreviewRadius);
        var maxRadius = coordinates.MaxDocumentRadius(localRect);

        if (!localRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == radiusCullControlId)
            {
                GUIUtility.hotControl = 0;
                radiusCullIsDragging = false;
                radiusCullHandleIsDragging = false;
                radiusCullControlId = 0;
                e.Use();
            }
            return;
        }

        if (e.type == EventType.MouseDown)
        {
            if (radiusCullHasPoint &&
                activeRadius > 0.0001f &&
                TryGetRadiusCullLayerData(out var selectedData, out var selectedSelectionKey, out var selectedStableKey) &&
                selectedData != null &&
                TryGetRadiusCullCenterInDrawDoc(selectedData, out var centerDrawDoc))
            {
                var mouseDrawDoc = coordinates.ViewToDocument(e.mousePosition);
                var handleBaseDoc = radiusCullCenterDoc + new Vector2(Mathf.Cos(radiusCullHandleAngleRad), Mathf.Sin(radiusCullHandleAngleRad)) * activeRadius;
                var handleDrawDoc = handleBaseDoc;
                TryMapRadiusCullBasePointToDrawDoc(selectedData, handleBaseDoc, out handleDrawDoc);
                var handleCanvas = coordinates.DocumentToView(handleDrawDoc);
                var handleHit = (handleCanvas - e.mousePosition).sqrMagnitude <= 64f;
                var ringTolerance = coordinates.DocumentDistanceForViewPixels(e.mousePosition, 8f);
                var ringHit = false;
                if (TryBuildRadiusCullRingDrawDoc(selectedData, activeRadius, 96, out var ringPointsDrawDoc) &&
                    ringPointsDrawDoc != null &&
                    ringPointsDrawDoc.Length >= 3)
                {
                    ringHit = DistanceToClosedPolyline(mouseDrawDoc, ringPointsDrawDoc) <= ringTolerance;
                }
                else
                {
                    ringHit = Mathf.Abs((mouseDrawDoc - centerDrawDoc).magnitude - activeRadius) <= ringTolerance;
                }
                if (handleHit || ringHit)
                {
                    RegisterUndo("Edit Radius Cull");
                    selectedLayerSelectionKey = selectedSelectionKey;
                    radiusCullLayerStableKey = selectedStableKey;
                    radiusCullHandleIsDragging = true;
                    radiusCullIsDragging = false;
                    radiusCullControlId = GUIUtility.GetControlID(FocusType.Passive);
                    GUIUtility.hotControl = radiusCullControlId;
                    var mouseBaseDoc = mouseDrawDoc;
                    if (TryMapRadiusCullDrawPointToBaseDoc(selectedData, mouseDrawDoc, out var mappedBaseDoc))
                        mouseBaseDoc = mappedBaseDoc;
                    var downDelta = mouseBaseDoc - radiusCullCenterDoc;
                    if (downDelta.sqrMagnitude > 1e-8f)
                    {
                        radiusCullHandleAngleRad = Mathf.Atan2(downDelta.y, downDelta.x);
                        SetRadiusCullRadiusOverride(Mathf.Clamp(downDelta.magnitude, 0.01f, maxRadius));
                    }
                    e.Use();
                    GUI.changed = true;
                    return;
                }
            }

            if (handleOnly)
                return;

            var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
            var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out var blockedByLayerLock);
            if (!hasHitLayer && blockedByLayerLock)
            {
                e.Use();
                return;
            }

            if (hitLayer == null)
            {
                if (radiusCullHasPoint || !string.IsNullOrEmpty(radiusCullLayerStableKey))
                    RegisterUndo("Clear Radius Cull Point");
                ClearRadiusCullPoint();
                selectedLayerSelectionKey = string.Empty;
                e.Use();
                GUI.changed = true;
                return;
            }

            var selectionKey = BuildSelectionKey(hitLayer);
            var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
            RegisterUndo("Set Radius Cull Point");
            selectedLayerSelectionKey = selectionKey;
            radiusCullLayerStableKey = stableKey;
            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var data) && data != null)
                radiusCullCenterDoc = ConvertPointToBaseDoc(data, mouseDoc);
            else
                radiusCullCenterDoc = mouseDoc;
            radiusCullHasPoint = true;
            radiusCullHandleAngleRad = Mathf.PI * 0.25f;
            radiusCullIsDragging = true;
            radiusCullHandleIsDragging = false;
            radiusCullControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = radiusCullControlId;

            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (GUIUtility.hotControl != radiusCullControlId)
                return;

            if (radiusCullHandleIsDragging)
            {
                var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
                var delta = Vector2.zero;
                var hasDelta = false;
                if (TryGetRadiusCullLayerData(out var data, out _, out _) && data != null)
                {
                    var mouseBaseDoc = mouseDoc;
                    if (TryMapRadiusCullDrawPointToBaseDoc(data, mouseDoc, out var mappedBaseDoc))
                        mouseBaseDoc = mappedBaseDoc;
                    delta = mouseBaseDoc - radiusCullCenterDoc;
                    hasDelta = true;
                }
                else
                {
                    delta = mouseDoc - radiusCullCenterDoc;
                    hasDelta = true;
                }
                if (hasDelta && delta.sqrMagnitude > 1e-8f)
                {
                    radiusCullHandleAngleRad = Mathf.Atan2(delta.y, delta.x);
                    SetRadiusCullRadiusOverride(Mathf.Clamp(delta.magnitude, 0.01f, maxRadius));
                    radiusCullHasPoint = true;
                    e.Use();
                    GUI.changed = true;
                    return;
                }
            }

            if (radiusCullIsDragging)
            {
                var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
                if (TryGetRadiusCullLayerData(out var data, out _, out _) && data != null)
                    radiusCullCenterDoc = ConvertPointToBaseDoc(data, mouseDoc);
                else
                    radiusCullCenterDoc = mouseDoc;
                radiusCullHasPoint = true;
                e.Use();
                GUI.changed = true;
                return;
            }
        }

        if (e.type == EventType.MouseUp && GUIUtility.hotControl == radiusCullControlId)
        {
            GUIUtility.hotControl = 0;
            radiusCullIsDragging = false;
            radiusCullHandleIsDragging = false;
            radiusCullControlId = 0;
            e.Use();
        }
    }

    private void HandleJointInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        float radiusCullPreviewRadius)
    {
        var e = Event.current;
        if (e == null || drawList == null)
            return;
        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        if (!isMouseEvent || e.alt || e.button != 0)
            return;

        var activeRadius = radiusCullHasRadiusOverride
            ? Mathf.Max(0.01f, radiusCullRadiusOverride)
            : Mathf.Max(0.01f, radiusCullPreviewRadius);
        var maxRadius = coordinates.MaxDocumentRadius(localRect);

        if (!localRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == jointControlId)
            {
                GUIUtility.hotControl = 0;
                jointIsDragging = false;
                jointStartHandleIsDragging = false;
                jointEndHandleIsDragging = false;
                jointMiddleHandleIsDragging = false;
                jointStartOffsetHandleIsDragging = false;
                jointEndOffsetHandleIsDragging = false;
                jointControlId = 0;
                e.Use();
            }
            return;
        }

        if (e.type == EventType.MouseDown)
        {
            if (radiusCullHasPoint &&
                activeRadius > 0.0001f &&
                TryGetRadiusCullLayerData(out var selectedData, out var selectedSelectionKey, out var selectedStableKey) &&
                selectedData != null &&
                TryGetJointCenterInDrawDoc(selectedData, out var centerDrawDoc))
            {
                var centerCanvas = coordinates.DocumentToView(centerDrawDoc);
                var startBaseDoc = radiusCullCenterDoc + new Vector2(Mathf.Cos(jointStartAngleRad), Mathf.Sin(jointStartAngleRad)) * activeRadius;
                var endBaseDoc = radiusCullCenterDoc + new Vector2(Mathf.Cos(jointEndAngleRad), Mathf.Sin(jointEndAngleRad)) * activeRadius;
                var startDrawDoc = startBaseDoc;
                var endDrawDoc = endBaseDoc;
                TryMapJointBasePointToDrawDoc(selectedData, startBaseDoc, out startDrawDoc);
                TryMapJointBasePointToDrawDoc(selectedData, endBaseDoc, out endDrawDoc);
                var middleDrawDoc = GetJointMiddleHandleDrawDoc(selectedData, activeRadius);
                var startOffsetDrawDoc = GetJointOffsetHandleDrawDoc(selectedData, activeRadius, true);
                var endOffsetDrawDoc = GetJointOffsetHandleDrawDoc(selectedData, activeRadius, false);
                var startCanvas = coordinates.DocumentToView(startDrawDoc);
                var endCanvas = coordinates.DocumentToView(endDrawDoc);
                var middleCanvas = coordinates.DocumentToView(middleDrawDoc);
                var startOffsetCanvas = coordinates.DocumentToView(startOffsetDrawDoc);
                var endOffsetCanvas = coordinates.DocumentToView(endOffsetDrawDoc);

                if (TryGetJointLockButtonRect(coordinates, selectedData, activeRadius, out var lockButtonRect))
                {
                    var hitLock = lockButtonRect.Contains(e.mousePosition);
                    if (hitLock)
                    {
                        RegisterUndo("Toggle Joint Center Lock");
                        selectedLayerSelectionKey = selectedSelectionKey;
                        radiusCullLayerStableKey = selectedStableKey;
                        JointCenterLockEnabled = !JointCenterLockEnabled;
                        if (JointCenterLockEnabled)
                            jointLockedCenterDrawDoc = centerDrawDoc;
                        radiusCullHasPoint = true;
                        jointIsDragging = false;
                        jointStartHandleIsDragging = false;
                        jointEndHandleIsDragging = false;
                        jointMiddleHandleIsDragging = false;
                        jointStartOffsetHandleIsDragging = false;
                        jointEndOffsetHandleIsDragging = false;
                        e.Use();
                        GUI.changed = true;
                        return;
                    }
                }

                var hitStart = (startCanvas - e.mousePosition).sqrMagnitude <= 81f;
                var hitEnd = !hitStart && (endCanvas - e.mousePosition).sqrMagnitude <= 81f;
                var hitMiddle = !hitStart && !hitEnd && (middleCanvas - e.mousePosition).sqrMagnitude <= 81f;
                var hitStartOffset = !hitStart && !hitEnd && !hitMiddle && (startOffsetCanvas - e.mousePosition).sqrMagnitude <= 81f;
                var hitEndOffset = !hitStart && !hitEnd && !hitMiddle && !hitStartOffset && (endOffsetCanvas - e.mousePosition).sqrMagnitude <= 81f;
                var hitCenter = !JointCenterLockEnabled && !hitStart && !hitEnd && !hitMiddle && !hitStartOffset && !hitEndOffset && (centerCanvas - e.mousePosition).sqrMagnitude <= 64f;

                if (hitStart || hitEnd || hitMiddle || hitStartOffset || hitEndOffset || hitCenter)
                {
                    RegisterUndo("Edit Joint");
                    selectedLayerSelectionKey = selectedSelectionKey;
                    radiusCullLayerStableKey = selectedStableKey;
                    jointStartHandleIsDragging = hitStart;
                    jointEndHandleIsDragging = hitEnd;
                    jointMiddleHandleIsDragging = hitMiddle;
                    jointStartOffsetHandleIsDragging = hitStartOffset;
                    jointEndOffsetHandleIsDragging = hitEndOffset;
                    jointIsDragging = hitCenter;
                    jointControlId = GUIUtility.GetControlID(FocusType.Passive);
                    GUIUtility.hotControl = jointControlId;

                    if (hitStart || hitEnd || hitMiddle || hitStartOffset || hitEndOffset)
                    {
                        var mouseDrawDoc = coordinates.ViewToDocument(e.mousePosition);
                        var mouseBaseDoc = mouseDrawDoc;
                        if (TryMapJointDrawPointToBaseDoc(selectedData, mouseDrawDoc, out var mappedBaseDoc))
                            mouseBaseDoc = mappedBaseDoc;
                        var downDelta = mouseBaseDoc - radiusCullCenterDoc;
                        if (downDelta.sqrMagnitude > 1e-8f)
                        {
                            var angle = Mathf.Atan2(downDelta.y, downDelta.x);
                            if (hitStart)
                                SetJointStartAngleWithMinGap(angle);
                            else if (hitMiddle)
                                SetJointFromMiddleHandle(angle, downDelta.magnitude, maxRadius);
                            else if (hitStartOffset)
                                SetJointLongPointsOffsetFromHandleAngle(angle, activeRadius, true);
                            else if (hitEndOffset)
                                SetJointLongPointsOffsetFromHandleAngle(angle, activeRadius, false);
                            else
                                SetJointEndAngleWithMinGap(angle);
                        }
                    }

                    e.Use();
                    GUI.changed = true;
                    return;
                }
            }

            if (JointCenterLockEnabled && radiusCullHasPoint)
            {
                e.Use();
                return;
            }

            var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
            var hasHitLayer = TryGetToolLockedHitLayer(mouseDoc, drawList, out var hitLayer, out var blockedByLayerLock);
            if (!hasHitLayer && blockedByLayerLock)
            {
                e.Use();
                return;
            }

            if (hitLayer == null)
            {
                if (radiusCullHasPoint || !string.IsNullOrEmpty(radiusCullLayerStableKey))
                    RegisterUndo("Clear Joint Point");
                ClearJointPoint();
                selectedLayerSelectionKey = string.Empty;
                e.Use();
                GUI.changed = true;
                return;
            }

            var selectionKey = BuildSelectionKey(hitLayer);
            var stableKey = BuildKey(hitLayer.name, hitLayer.layerId);
            RegisterUndo("Set Joint Point");
            var previousStableKey = radiusCullLayerStableKey;
            var hadJointPoint = radiusCullHasPoint;
            if (!hadJointPoint)
                JointCenterLockEnabled = false;
            selectedLayerSelectionKey = selectionKey;
            radiusCullLayerStableKey = stableKey;
            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var data) && data != null)
            {
                if (TryMapJointDrawPointToBaseDoc(data, mouseDoc, out var mappedBaseDoc))
                    radiusCullCenterDoc = mappedBaseDoc;
                else
                    radiusCullCenterDoc = ConvertPointToBaseDoc(data, mouseDoc);
            }
            else
            {
                radiusCullCenterDoc = mouseDoc;
            }
            radiusCullHasPoint = true;
            if (JointCenterLockEnabled)
                jointLockedCenterDrawDoc = mouseDoc;
            UpdateJointNearestBoneByCenter();

            var shouldResetAngles = !hadJointPoint ||
                                    !string.Equals(previousStableKey, stableKey, StringComparison.OrdinalIgnoreCase);
            if (shouldResetAngles)
            {
                var centerAngle = 0f;
                if (jointNearestBoneIndex >= 0 && jointNearestBoneIndex < bones.Count && bones[jointNearestBoneIndex] != null)
                {
                    var targetBoneIndex = jointNearestBoneIndex;
                    var nearestBone = bones[jointNearestBoneIndex];
                    if (nearestBone != null &&
                        nearestBone.parentId >= 0 &&
                        nearestBone.parentId < bones.Count &&
                        bones[nearestBone.parentId] != null)
                    {
                        targetBoneIndex = nearestBone.parentId;
                    }

                    var targetDirection = GetBoneBasePoint(bones[targetBoneIndex]) - radiusCullCenterDoc;
                    if (targetDirection.sqrMagnitude > 1e-8f)
                        centerAngle = Mathf.Atan2(targetDirection.y, targetDirection.x);
                }

                SetJointAnglesAround(centerAngle, 30f * Mathf.Deg2Rad);
            }

            jointIsDragging = true;
            jointStartHandleIsDragging = false;
            jointEndHandleIsDragging = false;
            jointMiddleHandleIsDragging = false;
            jointStartOffsetHandleIsDragging = false;
            jointEndOffsetHandleIsDragging = false;
            jointControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = jointControlId;

            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (GUIUtility.hotControl != jointControlId)
                return;

            if (jointStartHandleIsDragging || jointEndHandleIsDragging || jointMiddleHandleIsDragging || jointStartOffsetHandleIsDragging || jointEndOffsetHandleIsDragging)
            {
                var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
                var delta = Vector2.zero;
                if (TryGetRadiusCullLayerData(out var data, out _, out _) && data != null)
                {
                    var mouseBaseDoc = mouseDoc;
                    if (TryMapJointDrawPointToBaseDoc(data, mouseDoc, out var mappedBaseDoc))
                        mouseBaseDoc = mappedBaseDoc;
                    delta = mouseBaseDoc - radiusCullCenterDoc;
                }
                else
                {
                    delta = mouseDoc - radiusCullCenterDoc;
                }

                if (delta.sqrMagnitude > 1e-8f)
                {
                    var angle = Mathf.Atan2(delta.y, delta.x);
                    if (jointStartHandleIsDragging)
                        SetJointStartAngleWithMinGap(angle);
                    else if (jointMiddleHandleIsDragging)
                        SetJointFromMiddleHandle(angle, delta.magnitude, maxRadius);
                    else if (jointStartOffsetHandleIsDragging)
                        SetJointLongPointsOffsetFromHandleAngle(angle, activeRadius, true);
                    else if (jointEndOffsetHandleIsDragging)
                        SetJointLongPointsOffsetFromHandleAngle(angle, activeRadius, false);
                    else
                        SetJointEndAngleWithMinGap(angle);
                    radiusCullHasPoint = true;
                    e.Use();
                    GUI.changed = true;
                    return;
                }
            }
            if (jointIsDragging)
            {
                var mouseDoc = coordinates.ViewToDocument(e.mousePosition);
                if (TryGetRadiusCullLayerData(out var data, out _, out _) && data != null)
                {
                    if (TryMapJointDrawPointToBaseDoc(data, mouseDoc, out var mappedBaseDoc))
                        radiusCullCenterDoc = mappedBaseDoc;
                    else
                        radiusCullCenterDoc = ConvertPointToBaseDoc(data, mouseDoc);
                }
                else
                {
                    radiusCullCenterDoc = mouseDoc;
                }
                radiusCullHasPoint = true;
                UpdateJointNearestBoneByCenter();
                e.Use();
                GUI.changed = true;
                return;
            }
        }

        if (e.type == EventType.MouseUp && GUIUtility.hotControl == jointControlId)
        {
            GUIUtility.hotControl = 0;
            jointIsDragging = false;
            jointStartHandleIsDragging = false;
            jointEndHandleIsDragging = false;
            jointMiddleHandleIsDragging = false;
            jointStartOffsetHandleIsDragging = false;
            jointEndOffsetHandleIsDragging = false;
            jointControlId = 0;
            e.Use();
        }
    }

    private void UpdateJointNearestBoneByCenter()
    {
        jointNearestBoneIndex = FindNearestBoneIndex(radiusCullCenterDoc);
    }

    private void CaptureJointLockedCenterDrawDoc(LayerVertexData data = null)
    {
        if (!jointCenterLockEnabled || !radiusCullHasPoint)
            return;

        if (data == null)
            TryGetRadiusCullLayerData(out data, out _, out _);
        if (data != null && TryGetRadiusCullCenterInDrawDoc(data, out var centerDrawDoc))
            jointLockedCenterDrawDoc = centerDrawDoc;
        else
            jointLockedCenterDrawDoc = radiusCullCenterDoc;
    }

    private bool ShouldUseLockedJointCenter()
    {
        return jointCenterLockEnabled && radiusCullHasPoint;
    }

    private bool TryGetJointLockedDrawOffset(LayerVertexData data, out Vector2 drawOffset)
    {
        drawOffset = Vector2.zero;
        if (!ShouldUseLockedJointCenter())
            return false;

        var currentCenterDrawDoc = radiusCullCenterDoc;
        TryGetRadiusCullCenterInDrawDoc(data, out currentCenterDrawDoc);
        drawOffset = jointLockedCenterDrawDoc - currentCenterDrawDoc;
        return drawOffset.sqrMagnitude > 1e-8f;
    }

    private bool TryGetJointCenterInDrawDoc(LayerVertexData data, out Vector2 centerDrawDoc)
    {
        if (ShouldUseLockedJointCenter())
        {
            centerDrawDoc = jointLockedCenterDrawDoc;
            return true;
        }

        return TryGetRadiusCullCenterInDrawDoc(data, out centerDrawDoc);
    }

    private bool TryMapJointBasePointToDrawDoc(LayerVertexData data, Vector2 baseDocPoint, out Vector2 drawDocPoint)
    {
        drawDocPoint = baseDocPoint;
        var mappedPoint = baseDocPoint;
        var hadMappedPoint = TryMapRadiusCullBasePointToDrawDoc(data, baseDocPoint, out mappedPoint);
        drawDocPoint = mappedPoint;
        if (TryGetJointLockedDrawOffset(data, out var drawOffset))
            drawDocPoint += drawOffset;
        return hadMappedPoint || ShouldUseLockedJointCenter();
    }

    private bool TryMapJointDrawPointToBaseDoc(LayerVertexData data, Vector2 drawDocPoint, out Vector2 baseDocPoint)
    {
        baseDocPoint = drawDocPoint;
        var mappedDrawDoc = drawDocPoint;
        if (TryGetJointLockedDrawOffset(data, out var drawOffset))
            mappedDrawDoc -= drawOffset;

        if (TryMapRadiusCullDrawPointToBaseDoc(data, mappedDrawDoc, out var mappedBaseDoc))
        {
            baseDocPoint = mappedBaseDoc;
            return true;
        }

        baseDocPoint = mappedDrawDoc;
        return false;
    }

    private int FindNearestBoneIndex(Vector2 anchorDoc)
    {
        if (bones == null || bones.Count == 0)
            return -1;

        if (TryFindNearestBoneIndex(anchorDoc, true, out var nearestWithParent))
            return nearestWithParent;
        if (TryFindNearestBoneIndex(anchorDoc, false, out var nearestAny))
            return nearestAny;
        return -1;
    }

    private bool TryFindNearestBoneIndex(Vector2 anchorDoc, bool requireParentSegment, out int boneIndex)
    {
        boneIndex = -1;
        if (bones == null || bones.Count == 0)
            return false;

        var nearestVisibleIndex = -1;
        var nearestAnyIndex = -1;
        var nearestVisibleDistanceSqr = float.MaxValue;
        var nearestAnyDistanceSqr = float.MaxValue;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null)
                continue;
            if (!TryGetJointBoneGuideSegment(i, anchorDoc, requireParentSegment, out var segStart, out var segEnd))
                continue;

            var sqrDistance = Mathf.Pow(DistancePointToSegment(anchorDoc, segStart, segEnd), 2f);
            if (sqrDistance < nearestAnyDistanceSqr)
            {
                nearestAnyDistanceSqr = sqrDistance;
                nearestAnyIndex = i;
            }

            if (!bone.visible || sqrDistance >= nearestVisibleDistanceSqr)
                continue;

            nearestVisibleDistanceSqr = sqrDistance;
            nearestVisibleIndex = i;
        }

        boneIndex = nearestVisibleIndex >= 0 ? nearestVisibleIndex : nearestAnyIndex;
        return boneIndex >= 0;
    }

    private bool TryGetJointBoneGuideSegment(int boneIndex, Vector2 anchorDoc, bool requireParentSegment, out Vector2 segStart, out Vector2 segEnd)
    {
        segStart = Vector2.zero;
        segEnd = Vector2.zero;
        if (bones == null || boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var bone = bones[boneIndex];
        if (bone == null)
            return false;

        segStart = GetBoneBasePoint(bone);
        var parentIndex = bone.parentId;
        if (parentIndex >= 0 && parentIndex < bones.Count && bones[parentIndex] != null)
        {
            segEnd = requireParentSegment ? GetBoneEnd(boneIndex) : GetBoneBasePoint(bones[parentIndex]);
            return (segEnd - segStart).sqrMagnitude > 1e-8f;
        }

        if (!requireParentSegment &&
            TryFindNearestChildBoneIndex(boneIndex, anchorDoc, out var childIndex) &&
            childIndex >= 0 && childIndex < bones.Count && bones[childIndex] != null)
        {
            segEnd = GetBoneBasePoint(bones[childIndex]);
            return true;
        }

        return false;
    }
    private bool TryGetRadiusCullCenterInDrawDoc(LayerVertexData data, out Vector2 centerDrawDoc)
    {
        centerDrawDoc = radiusCullCenterDoc;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length)
            return true;
        if (ReferenceEquals(drawVertices, data.vertices))
            return true;

        if (PsdCoordinateSpace.TryMapPointBetweenMeshes(data.vertices, drawVertices, data.triangles, radiusCullCenterDoc, out var mapped))
            centerDrawDoc = mapped;

        return true;
    }

    private bool TryMapRadiusCullBasePointToDrawDoc(LayerVertexData data, Vector2 baseDocPoint, out Vector2 drawDocPoint)
    {
        drawDocPoint = baseDocPoint;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length)
            return false;
        if (ReferenceEquals(drawVertices, data.vertices))
            return false;
        if (data.triangles == null || data.triangles.Length < 3)
            return false;

        if (PsdCoordinateSpace.TryMapPointBetweenMeshes(data.vertices, drawVertices, data.triangles, baseDocPoint, out var mapped))
        {
            drawDocPoint = mapped;
            return true;
        }

        return false;
    }

    private bool TryMapRadiusCullDrawPointToBaseDoc(LayerVertexData data, Vector2 drawDocPoint, out Vector2 baseDocPoint)
    {
        baseDocPoint = drawDocPoint;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length)
            return false;
        if (ReferenceEquals(drawVertices, data.vertices))
            return false;
        if (data.triangles == null || data.triangles.Length < 3)
            return false;

        if (PsdCoordinateSpace.TryMapPointBetweenMeshes(drawVertices, data.vertices, data.triangles, drawDocPoint, out var mapped))
        {
            baseDocPoint = mapped;
            return true;
        }

        return false;
    }

    private bool TryBuildRadiusCullRingDrawDoc(
        LayerVertexData data,
        float radius,
        int segmentCount,
        out Vector2[] ringPointsDrawDoc)
    {
        ringPointsDrawDoc = Array.Empty<Vector2>();
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;
        if (radius <= 0.0001f)
            return false;

        segmentCount = Mathf.Clamp(segmentCount, 12, 256);
        ringPointsDrawDoc = new Vector2[segmentCount];
        var centerBaseDoc = radiusCullCenterDoc;
        for (var i = 0; i < segmentCount; i++)
        {
            var t = i / (float)segmentCount;
            var angle = t * Mathf.PI * 2f;
            var basePoint = centerBaseDoc + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            var drawPoint = basePoint;
            TryMapRadiusCullBasePointToDrawDoc(data, basePoint, out drawPoint);
            ringPointsDrawDoc[i] = drawPoint;
        }

        return ringPointsDrawDoc.Length >= 3;
    }

    private bool TryBuildJointRingDrawDoc(
        LayerVertexData data,
        float radius,
        int segmentCount,
        out Vector2[] ringPointsDrawDoc)
    {
        ringPointsDrawDoc = Array.Empty<Vector2>();
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;
        if (radius <= 0.0001f)
            return false;

        segmentCount = Mathf.Clamp(segmentCount, 12, 256);
        ringPointsDrawDoc = new Vector2[segmentCount];
        var centerBaseDoc = radiusCullCenterDoc;
        for (var i = 0; i < segmentCount; i++)
        {
            var t = i / (float)segmentCount;
            var angle = t * Mathf.PI * 2f;
            var basePoint = centerBaseDoc + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            var drawPoint = basePoint;
            TryMapJointBasePointToDrawDoc(data, basePoint, out drawPoint);
            ringPointsDrawDoc[i] = drawPoint;
        }

        return ringPointsDrawDoc.Length >= 3;
    }

    private static float DistanceToClosedPolyline(Vector2 point, IReadOnlyList<Vector2> polyline)
    {
        if (polyline == null || polyline.Count < 2)
            return float.PositiveInfinity;

        var best = float.PositiveInfinity;
        for (var i = 0; i < polyline.Count; i++)
        {
            var a = polyline[i];
            var b = polyline[(i + 1) % polyline.Count];
            var distance = DistancePointToSegment(point, a, b);
            if (distance < best)
                best = distance;
        }

        return best;
    }

    private bool TryGetJointLockButtonRect(
        PsdCoordinateSpace coordinates,
        LayerVertexData data,
        float radius,
        out Rect lockRect)
    {
        lockRect = Rect.zero;
        if (coordinates == null || !radiusCullHasPoint || radius <= 0.0001f)
            return false;

        var topBaseDoc = radiusCullCenterDoc + Vector2.up * Mathf.Max(0.01f, radius);
        var topDrawDoc = topBaseDoc;
        if (data != null)
            TryMapJointBasePointToDrawDoc(data, topBaseDoc, out topDrawDoc);

        var topCanvas = coordinates.DocumentToView(topDrawDoc);
        const float buttonHeight = 14f;
        const float yOffset = 3f;
        const float lockWidth = 34f;

        var x = topCanvas.x - lockWidth * 0.5f;
        var y = topCanvas.y - buttonHeight - yOffset;
        lockRect = new Rect(x, y, lockWidth, buttonHeight);
        return true;
    }

    private float GetJointCenterAngleRad()
    {
        GetJointMinorSector(jointStartAngleRad, jointEndAngleRad, out var sectorStartAngle, out _, out var sectorSpan);
        return NormalizeAngleRad(sectorStartAngle + sectorSpan * 0.5f);
    }

    private void SetJointCenterAngleKeepingSpan(float centerAngleRad)
    {
        GetJointMinorSector(jointStartAngleRad, jointEndAngleRad, out _, out _, out var sectorSpan);
        SetJointAnglesAround(centerAngleRad, Mathf.Max(GetJointMinimumGapRad() * 0.5f, sectorSpan * 0.5f));
    }

    private void SetJointFromMiddleHandle(float centerAngleRad, float handleDistance, float maxRadius)
    {
        SetJointCenterAngleKeepingSpan(centerAngleRad);
        var radius = Mathf.Clamp(handleDistance / GetJointMiddleHandleDistanceMultiplier(), 0.01f, Mathf.Max(0.01f, maxRadius));
        SetRadiusCullRadiusOverride(radius);
    }

    private Vector2 GetJointMiddleHandleDrawDoc(LayerVertexData data, float radius)
    {
        var middleAngleRad = GetJointCenterAngleRad();
        var middleBaseDoc = radiusCullCenterDoc + new Vector2(Mathf.Cos(middleAngleRad), Mathf.Sin(middleAngleRad)) * Mathf.Max(0.01f, radius * GetJointMiddleHandleDistanceMultiplier());
        var middleDrawDoc = middleBaseDoc;
        if (data != null)
            TryMapJointBasePointToDrawDoc(data, middleBaseDoc, out middleDrawDoc);
        return middleDrawDoc;
    }

    private Vector2 GetJointOffsetHandleDrawDoc(LayerVertexData data, float radius, bool startSide)
    {
        var handleAngleRad = GetJointOffsetHandleAngleRad(radius, startSide);
        var handleBaseDoc = radiusCullCenterDoc + new Vector2(Mathf.Cos(handleAngleRad), Mathf.Sin(handleAngleRad)) * Mathf.Max(0.01f, radius);
        var handleDrawDoc = handleBaseDoc;
        if (data != null)
            TryMapJointBasePointToDrawDoc(data, handleBaseDoc, out handleDrawDoc);
        return handleDrawDoc;
    }

    private float GetJointOffsetHandleAngleRad(float radius, bool startSide)
    {
        var safeRadius = Mathf.Max(0.01f, radius);
        var centerAngleRad = GetJointCenterAngleRad();
        var boundaryAngleRad = startSide ? jointStartAngleRad : jointEndAngleRad;
        var boundarySideSign = GetJointBoundaryOuterDirectionSign(boundaryAngleRad, centerAngleRad, startSide ? -1f : 1f);
        var offset = startSide ? JointStartLongPointsOffset : JointEndLongPointsOffset;
        return NormalizeAngleRad(boundaryAngleRad + boundarySideSign * (offset / safeRadius));
    }

    private void SetJointLongPointsOffsetFromHandleAngle(float handleAngleRad, float radius, bool startSide)
    {
        var safeRadius = Mathf.Max(0.01f, radius);
        var centerAngleRad = GetJointCenterAngleRad();
        var boundaryAngleRad = startSide ? jointStartAngleRad : jointEndAngleRad;
        var boundarySideSign = GetJointBoundaryOuterDirectionSign(boundaryAngleRad, centerAngleRad, startSide ? -1f : 1f);
        var directedDelta = NormalizeAngleRad(handleAngleRad - boundaryAngleRad) * boundarySideSign;
        var offset = Mathf.Max(0f, directedDelta) * safeRadius;
        if (startSide)
            JointStartLongPointsOffset = offset;
        else
            JointEndLongPointsOffset = offset;
        InvalidateRadiusCullPreviewCache(false);
    }

    private static float GetJointBoundaryOuterDirectionSign(float boundaryAngleRad, float centerAngleRad, float fallbackSign)
    {
        var sign = Mathf.Sign(NormalizeAngleRad(boundaryAngleRad - centerAngleRad));
        return Mathf.Abs(sign) > 0.0001f ? sign : Mathf.Sign(fallbackSign);
    }

    private static float NormalizeAngleRad(float angle)
    {
        var twoPi = Mathf.PI * 2f;
        angle = Mathf.Repeat(angle + Mathf.PI, twoPi) - Mathf.PI;
        return angle;
    }

    private static float NormalizePositiveAngleRad(float angle)
    {
        return Mathf.Repeat(angle, Mathf.PI * 2f);
    }

    private static float GetJointMinimumGapRad()
    {
        return 10f * Mathf.Deg2Rad;
    }

    private static float GetJointMiddleHandleDistanceMultiplier()
    {
        return 1.08f;
    }

    private void SetJointAnglesAround(float centerAngleRad, float halfSpanRad)
    {
        var minGap = GetJointMinimumGapRad();
        halfSpanRad = Mathf.Max(minGap * 0.5f, halfSpanRad);
        jointStartAngleRad = NormalizeAngleRad(centerAngleRad - halfSpanRad);
        jointEndAngleRad = NormalizeAngleRad(centerAngleRad + halfSpanRad);
        var span = NormalizePositiveAngleRad(jointEndAngleRad - jointStartAngleRad);
        if (span < minGap)
            jointEndAngleRad = NormalizeAngleRad(jointStartAngleRad + minGap);
        InvalidateRadiusCullPreviewCache(false);
    }

    private void SetJointStartAngleWithMinGap(float newStartAngleRad)
    {
        jointStartAngleRad = NormalizeAngleRad(newStartAngleRad);
        var minGap = GetJointMinimumGapRad();
        var span = NormalizePositiveAngleRad(jointEndAngleRad - jointStartAngleRad);
        if (span < minGap)
            jointEndAngleRad = NormalizeAngleRad(jointStartAngleRad + minGap);
        InvalidateRadiusCullPreviewCache(false);
    }

    private void SetJointEndAngleWithMinGap(float newEndAngleRad)
    {
        jointEndAngleRad = NormalizeAngleRad(newEndAngleRad);
        var minGap = GetJointMinimumGapRad();
        var span = NormalizePositiveAngleRad(jointEndAngleRad - jointStartAngleRad);
        if (span < minGap)
            jointStartAngleRad = NormalizeAngleRad(jointEndAngleRad - minGap);
        InvalidateRadiusCullPreviewCache(false);
    }

    private static bool IsAngleInsideJointSector(float angleRad, float startAngleRad, float endAngleRad)
    {
        var span = NormalizePositiveAngleRad(endAngleRad - startAngleRad);
        var relative = NormalizePositiveAngleRad(angleRad - startAngleRad);
        return relative <= span + 1e-5f;
    }

    private static void GetJointMinorSector(float angleA, float angleB, out float startAngleRad, out float endAngleRad, out float spanRad)
    {
        var forwardSpan = NormalizePositiveAngleRad(angleB - angleA);
        var backwardSpan = NormalizePositiveAngleRad(angleA - angleB);
        if (forwardSpan <= backwardSpan)
        {
            startAngleRad = NormalizeAngleRad(angleA);
            spanRad = forwardSpan;
        }
        else
        {
            startAngleRad = NormalizeAngleRad(angleB);
            spanRad = backwardSpan;
        }

        endAngleRad = NormalizeAngleRad(startAngleRad + spanRad);
    }

    private bool IsPointInsideCurrentJointSector(Vector2 pointDoc, Vector2 centerDoc)
    {
        var delta = pointDoc - centerDoc;
        if (delta.sqrMagnitude <= 1e-8f)
            return true;
        var angle = Mathf.Atan2(delta.y, delta.x);
        GetJointMinorSector(jointStartAngleRad, jointEndAngleRad, out var minorStart, out _, out var minorSpan);
        var relative = NormalizePositiveAngleRad(angle - minorStart);
        return relative <= minorSpan + 1e-5f;
    }
}
#endif
