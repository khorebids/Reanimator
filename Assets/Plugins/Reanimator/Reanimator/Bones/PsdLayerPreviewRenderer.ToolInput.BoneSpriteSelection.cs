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
    private const float BoneEndpointSnapDistanceCanvas = 10f;

    private void HandleBoneInput(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        bool allowPoseEditing = true,
        bool allowLengthEditing = false,
        ReanimatorBoneInputOptions inputOptions = default(ReanimatorBoneInputOptions))
    {
        var e = Event.current;
        if (e == null)
            return;

        var localMouse = e.mousePosition;
        var isMouseEvent = e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.MouseUp;
        var isHoverEvent = e.type == EventType.MouseMove;
        if ((!isMouseEvent && !isHoverEvent) || e.alt)
        {
            if (SetHoveredBoneIndex(-1))
                GUI.changed = true;
            return;
        }
        if (isMouseEvent && e.button != inputOptions.MouseButton)
            return;

        if (!localRect.Contains(localMouse))
        {
            if (SetHoveredBoneIndex(-1))
                GUI.changed = true;
            if (e.type == EventType.MouseUp)
            {
                var shouldAutosaveBoneTransform = boneTransformChangedDuringDrag;
                isRotating = false;
                isDragging = false;
                isResizingBone = false;
                boneBaseDragPreservingTip = false;
                boneTransformChangedDuringDrag = false;
                if (GUIUtility.hotControl == dragControlId)
                    GUIUtility.hotControl = 0;
                if (shouldAutosaveBoneTransform)
                    MarkBoneStateChanged();
            }
            return;
        }

        var mouseDoc = coordinates.ViewToDocument(localMouse);
        var hit = FindBoneHandleAt(
            localMouse,
            coordinates,
            allowLengthEditing && !inputOptions.ForceRotation);
        if ((e.type == EventType.MouseMove || e.type == EventType.MouseDown || e.type == EventType.MouseDrag) &&
            SetHoveredBoneIndex(hit.index))
        {
            GUI.changed = true;
        }

        if (e.type == EventType.MouseDown)
        {
            selectedBoneIndex = hit.index;
            isRotating = false;
            isDragging = false;
            isResizingBone = false;
            boneBaseDragPreservingTip = false;
            boneTransformChangedDuringDrag = false;
            if (selectedBoneIndex >= 0)
            {
                if (!allowPoseEditing)
                {
                    e.Use();
                    GUI.changed = true;
                    return;
                }

                RegisterUndo("Edit Bone");
                dragControlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = dragControlId;
                if (inputOptions.ForceRotation)
                {
                    isRotating = true;
                }
                else if (hit.type == BoneHandleType.Base)
                {
                    isDragging = true;
                    var bone = bones[selectedBoneIndex];
                    dragOffsetDoc = bone.worldPosition - mouseDoc;
                    dragPreservedTipDoc = GetBoneEnd(selectedBoneIndex);
                    boneBaseDragPreservingTip = e.shift;
                }
                else if (hit.type == BoneHandleType.Tip)
                {
                    isResizingBone = true;
                }
                else
                {
                    isRotating = true;
                }
                e.Use();
            }
            GUI.changed = true;
        }
        else if (e.type == EventType.MouseDrag && allowPoseEditing && selectedBoneIndex >= 0 && GUIUtility.hotControl == dragControlId)
        {
            var bone = bones[selectedBoneIndex];
            var isolatedChildren = CaptureIsolatedChildBonePoses(selectedBoneIndex);
            if (isResizingBone)
            {
                var targetTip = SnapBoneTipToNearbyBase(
                    selectedBoneIndex,
                    localMouse,
                    mouseDoc,
                    coordinates);
                ResizeBoneToWorldTip(bone, targetTip);
            }
            else if (isRotating)
            {
                var start = bone.worldPosition;
                var angle = Mathf.Atan2(mouseDoc.y - start.y, mouseDoc.x - start.x) * Mathf.Rad2Deg;
                var newWorldRot = Quaternion.AngleAxis(angle, Vector3.forward);

                if (bonesTreatAsWorldSpace)
                {
                    bone.rotation = newWorldRot;
                }
                else if (bone.parentId >= 0 && bone.parentId < bones.Count)
                {
                    bone.rotation = ConvertBoneWorldRotationToLocal(bone, newWorldRot);
                }
                else
                {
                    bone.rotation = newWorldRot;
                }
            }
            else if (isDragging)
            {
                if (e.shift && !boneBaseDragPreservingTip)
                    dragPreservedTipDoc = GetBoneEnd(selectedBoneIndex);
                boneBaseDragPreservingTip = e.shift;

                var targetWorld = mouseDoc + dragOffsetDoc;
                targetWorld = SnapBoneBaseToNearbyTip(
                    selectedBoneIndex,
                    targetWorld,
                    coordinates);
                MoveBoneBaseToWorldPosition(
                    bone,
                    targetWorld,
                    boneBaseDragPreservingTip,
                    dragPreservedTipDoc);
            }

            bones[selectedBoneIndex] = bone;
            RecomputeBoneWorldTransformsPreservingIsolatedChildren(isolatedChildren);

            InvalidateBonePose();
            hasPendingChanges = true;
            boneTransformChangedDuringDrag = true;
            e.Use();
            GUI.changed = true;
        }
        else if (e.type == EventType.MouseUp)
        {
            var shouldAutosaveBoneTransform = boneTransformChangedDuringDrag;
            isRotating = false;
            isDragging = false;
            isResizingBone = false;
            boneBaseDragPreservingTip = false;
            boneTransformChangedDuringDrag = false;
            if (GUIUtility.hotControl == dragControlId)
                GUIUtility.hotControl = 0;
            if (shouldAutosaveBoneTransform)
                MarkBoneStateChanged();
            e.Use();
        }
    }

    private bool SetHoveredBoneIndex(int boneIndex)
    {
        var nextIndex = ReanimatorToolModeAlgorithms.NormalizeHoveredBoneIndex(boneIndex, bones != null ? bones.Count : 0);
        if (hoveredBoneIndex == nextIndex)
            return false;

        hoveredBoneIndex = nextIndex;
        return true;
    }

    internal bool SetPanelHoveredBoneIndex(int boneIndex)
    {
        var nextIndex = ReanimatorToolModeAlgorithms.NormalizeHoveredBoneIndex(boneIndex, bones != null ? bones.Count : 0);
        if (panelHoveredBoneIndex == nextIndex)
            return false;

        panelHoveredBoneIndex = nextIndex;
        return true;
    }

    private void HandleSpriteSelection(Rect localRect, PsdCoordinateSpace coordinates, IReadOnlyList<LayerInfo> drawList)
    {
        if (!spriteSelectionAllowedForCurrentDraw)
            return;

        var e = Event.current;
        if (e == null || e.alt || e.button != 0 || e.type != EventType.MouseDown)
            return;
        if (!localRect.Contains(e.mousePosition))
            return;

        var mouseView = e.mousePosition;
        var mouseDoc = coordinates.ViewToDocument(mouseView);
        spriteHitBuffer.Clear();

        for (var i = drawList.Count - 1; i >= 0; i--)
        {
            var layer = drawList[i];
            if (layer == null || !layer.visible)
                continue;
            if (!ShouldIncludeLayerForSpriteSelectionInput(layer))
                continue;

            if (!IsPointInsideLayer(layer, coordinates, mouseView, mouseDoc))
                continue;

            spriteHitBuffer.Add(layer);
        }

        ApplySpriteSelectionHits(e.mousePosition);
        e.Use();
    }

    private void HandleSpriteAtlasSelection(Rect localRect, PsdCoordinateSpace coordinates, IReadOnlyList<LayerInfo> drawList)
    {
        if (!spriteSelectionAllowedForCurrentDraw)
            return;

        var e = Event.current;
        if (e == null || e.alt || e.button != 0 || e.type != EventType.MouseDown)
            return;
        if (!localRect.Contains(e.mousePosition))
            return;

        spriteHitBuffer.Clear();

        for (var i = drawList.Count - 1; i >= 0; i--)
        {
            var layer = drawList[i];
            if (layer == null || !layer.visible)
                continue;
            if (!ShouldIncludeLayerForSpriteSelectionInput(layer))
                continue;

            if (!IsPointInsideAtlasLayer(layer, coordinates, e.mousePosition))
                continue;

            spriteHitBuffer.Add(layer);
        }

        ApplySpriteSelectionHits(e.mousePosition);
        e.Use();
    }

    private void ApplySpriteSelectionHits(Vector2 mousePosition)
    {
        var action = ReanimatorSpriteSelectionAlgorithms.ResolveClickAction(spriteHitBuffer.Count, spriteSelectionLockedForCurrentDraw);
        switch (action)
        {
            case ReanimatorSpriteSelectionClickAction.KeepSelection:
                break;
            case ReanimatorSpriteSelectionClickAction.ClearSelection:
                SetSpriteSelectionKey(string.Empty);
                break;
            case ReanimatorSpriteSelectionClickAction.SelectSingle:
                SetSpriteSelectionKey(BuildSelectionKey(spriteHitBuffer[0]));
                break;
            case ReanimatorSpriteSelectionClickAction.ShowPicker:
                ShowSpriteSelectionPicker(spriteHitBuffer, mousePosition);
                break;
        }
    }

    private bool ShouldIncludeLayerForSpriteSelectionInput(LayerInfo layer)
    {
        if (!isolateSelectedSpriteForCurrentDraw || string.IsNullOrEmpty(selectedLayerSelectionKey))
            return true;

        return layer != null &&
               string.Equals(BuildSelectionKey(layer), selectedLayerSelectionKey, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowSpriteSelectionPicker(IReadOnlyList<LayerInfo> hits, Vector2 mousePosition)
    {
        if (hits == null || hits.Count <= 1)
            return;

        var menu = new GenericMenu();
        for (var i = 0; i < hits.Count; i++)
        {
            var layer = hits[i];
            if (layer == null)
                continue;

            var selectionKey = BuildSelectionKey(layer);
            var label = ReanimatorSpriteSelectionAlgorithms.BuildPickerLabel(layer.name, layer.layerId, i);
            var selected = string.Equals(selectionKey, selectedLayerSelectionKey, StringComparison.OrdinalIgnoreCase);
            menu.AddItem(new GUIContent(label), selected, () => SetSpriteSelectionKey(selectionKey));
        }

        menu.DropDown(new Rect(mousePosition.x, mousePosition.y, 1f, 1f));
    }

    private void SetSpriteSelectionKey(string selectionKey)
    {
        selectionKey = selectionKey ?? string.Empty;
        if (string.Equals(selectedLayerSelectionKey, selectionKey, StringComparison.OrdinalIgnoreCase))
            return;

        selectedLayerSelectionKey = selectionKey;
        GUI.changed = true;
        var focusedWindow = EditorWindow.focusedWindow;
        if (focusedWindow != null)
            focusedWindow.Repaint();
    }

    private void HandleSpriteAtlasFrameInput(Rect localRect, PsdCoordinateSpace coordinates, IReadOnlyList<LayerInfo> drawList)
    {
        var e = Event.current;
        if (e == null || e.alt || e.button != 0)
            return;

        if (e.type == EventType.MouseUp && spriteRectIsDragging)
        {
            EndSpriteRectDrag();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && spriteRectIsDragging && GUIUtility.hotControl == spriteRectDragControlId)
        {
            var point = coordinates.ViewToDocument(e.mousePosition);
            var rect = BuildDraggedSpriteRect(spriteRectDragStartRect, spriteRectDragHandle, point - spriteRectDragStartPoint);
            if (TryGetSelectedLayer(out var selected))
                SetLayerSpriteRect(selected, ClampSpriteRectToTexture(rect, spriteRectDragHandle), registerUndo: false);
            e.Use();
            GUI.changed = true;
            return;
        }

        if (e.type != EventType.MouseDown)
            return;

        if (!localRect.Contains(e.mousePosition))
            return;

        var hitLayer = FindSpriteAtlasFrameHit(coordinates, drawList, e.mousePosition, out var handle);
        if (hitLayer == null)
            return;

        var nextKey = BuildSelectionKey(hitLayer);
        if (!string.Equals(selectedLayerSelectionKey, nextKey, StringComparison.OrdinalIgnoreCase))
        {
            selectedLayerSelectionKey = nextKey;
            GUI.changed = true;
        }

        if (handle != SpriteRectDragHandle.None)
        {
            spriteRectIsDragging = true;
            spriteRectDragHandle = handle;
            spriteRectDragStartRect = hitLayer.sourceRect;
            spriteRectDragStartPoint = coordinates.ViewToDocument(e.mousePosition);
            spriteRectDragControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = spriteRectDragControlId;
            RegisterSpriteRectDragUndoOnce();
        }

        e.Use();
    }

    private void EndSpriteRectDrag()
    {
        spriteRectIsDragging = false;
        spriteRectDragHandle = SpriteRectDragHandle.None;
        if (GUIUtility.hotControl == spriteRectDragControlId)
            GUIUtility.hotControl = 0;
        spriteRectDragControlId = 0;
        spriteRectDragUndoRegistered = false;
    }

    private void RegisterSpriteRectDragUndoOnce()
    {
        if (spriteRectDragUndoRegistered)
            return;

        RegisterUndo("Edit Sprite Rect");
        spriteRectDragUndoRegistered = true;
    }

    private LayerInfo FindSpriteAtlasFrameHit(
        PsdCoordinateSpace coordinates,
        IReadOnlyList<LayerInfo> drawList,
        Vector2 mouseView,
        out SpriteRectDragHandle handle)
    {
        handle = SpriteRectDragHandle.None;
        if (coordinates == null || drawList == null)
            return null;

        if (TryGetSelectedLayer(out var selectedLayer) && selectedLayer != null && selectedLayer.visible)
        {
            var selectedRect = coordinates.LayerToViewRect(selectedLayer);
            handle = GetSpriteRectHandle(selectedRect, mouseView);
            if (handle != SpriteRectDragHandle.None)
                return selectedLayer;
        }

        for (var i = drawList.Count - 1; i >= 0; i--)
        {
            var layer = drawList[i];
            if (layer == null || !layer.visible)
                continue;

            if (!IsPointInsideAtlasLayer(layer, coordinates, mouseView))
                continue;

            handle = SpriteRectDragHandle.Move;
            return layer;
        }

        return null;
    }

    private static SpriteRectDragHandle GetSpriteRectHandle(Rect rect, Vector2 point)
    {
        const float handleSize = 8f;
        const float edgeSize = 4f;

        if (rect.width <= 0f || rect.height <= 0f)
            return SpriteRectDragHandle.None;

        if (HandleRect(rect.xMin, rect.yMin, handleSize).Contains(point))
            return SpriteRectDragHandle.TopLeft;
        if (HandleRect(rect.xMax, rect.yMin, handleSize).Contains(point))
            return SpriteRectDragHandle.TopRight;
        if (HandleRect(rect.xMin, rect.yMax, handleSize).Contains(point))
            return SpriteRectDragHandle.BottomLeft;
        if (HandleRect(rect.xMax, rect.yMax, handleSize).Contains(point))
            return SpriteRectDragHandle.BottomRight;

        if (Mathf.Abs(point.x - rect.xMin) <= edgeSize && point.y >= rect.yMin && point.y <= rect.yMax)
            return SpriteRectDragHandle.Left;
        if (Mathf.Abs(point.x - rect.xMax) <= edgeSize && point.y >= rect.yMin && point.y <= rect.yMax)
            return SpriteRectDragHandle.Right;
        if (Mathf.Abs(point.y - rect.yMin) <= edgeSize && point.x >= rect.xMin && point.x <= rect.xMax)
            return SpriteRectDragHandle.Top;
        if (Mathf.Abs(point.y - rect.yMax) <= edgeSize && point.x >= rect.xMin && point.x <= rect.xMax)
            return SpriteRectDragHandle.Bottom;

        return rect.Contains(point) ? SpriteRectDragHandle.Move : SpriteRectDragHandle.None;
    }

    private static Rect HandleRect(float x, float y, float size)
    {
        var half = size * 0.5f;
        return new Rect(x - half, y - half, size, size);
    }

    private static Rect BuildDraggedSpriteRect(Rect start, SpriteRectDragHandle handle, Vector2 delta)
    {
        const float minSize = 1f;
        var left = start.xMin;
        var right = start.xMax;
        var bottom = start.yMin;
        var top = start.yMax;

        switch (handle)
        {
            case SpriteRectDragHandle.Move:
                left += delta.x;
                right += delta.x;
                bottom += delta.y;
                top += delta.y;
                break;
            case SpriteRectDragHandle.Left:
                left += delta.x;
                break;
            case SpriteRectDragHandle.Right:
                right += delta.x;
                break;
            case SpriteRectDragHandle.Bottom:
                bottom += delta.y;
                break;
            case SpriteRectDragHandle.Top:
                top += delta.y;
                break;
            case SpriteRectDragHandle.TopLeft:
                left += delta.x;
                top += delta.y;
                break;
            case SpriteRectDragHandle.TopRight:
                right += delta.x;
                top += delta.y;
                break;
            case SpriteRectDragHandle.BottomLeft:
                left += delta.x;
                bottom += delta.y;
                break;
            case SpriteRectDragHandle.BottomRight:
                right += delta.x;
                bottom += delta.y;
                break;
        }

        if (right - left < minSize)
        {
            if (MovesLeftEdge(handle))
                left = right - minSize;
            else
                right = left + minSize;
        }
        if (top - bottom < minSize)
        {
            if (MovesBottomEdge(handle))
                bottom = top - minSize;
            else
                top = bottom + minSize;
        }

        return Rect.MinMaxRect(left, bottom, right, top);
    }

    private Rect ClampSpriteRectToTexture(Rect rect, SpriteRectDragHandle handle)
    {
        var atlasSize = GetPreviewAtlasSize();
        var textureWidth = Mathf.Max(1f, atlasSize.x);
        var textureHeight = Mathf.Max(1f, atlasSize.y);
        if (handle == SpriteRectDragHandle.Move)
        {
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, textureWidth - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, textureHeight - rect.height));
            return rect;
        }

        var left = Mathf.Clamp(rect.xMin, 0f, textureWidth);
        var right = Mathf.Clamp(rect.xMax, 0f, textureWidth);
        var bottom = Mathf.Clamp(rect.yMin, 0f, textureHeight);
        var top = Mathf.Clamp(rect.yMax, 0f, textureHeight);
        if (right - left < 1f)
        {
            if (MovesLeftEdge(handle))
                left = Mathf.Max(0f, right - 1f);
            else
                right = Mathf.Min(textureWidth, left + 1f);
        }
        if (top - bottom < 1f)
        {
            if (MovesBottomEdge(handle))
                bottom = Mathf.Max(0f, top - 1f);
            else
                top = Mathf.Min(textureHeight, bottom + 1f);
        }

        return Rect.MinMaxRect(left, bottom, right, top);
    }

    private static bool MovesLeftEdge(SpriteRectDragHandle handle)
    {
        return handle == SpriteRectDragHandle.Left ||
               handle == SpriteRectDragHandle.TopLeft ||
               handle == SpriteRectDragHandle.BottomLeft;
    }

    private static bool MovesBottomEdge(SpriteRectDragHandle handle)
    {
        return handle == SpriteRectDragHandle.Bottom ||
               handle == SpriteRectDragHandle.BottomLeft ||
               handle == SpriteRectDragHandle.BottomRight;
    }

    private bool IsPointInsideAtlasLayer(LayerInfo layer, PsdCoordinateSpace coordinates, Vector2 viewPoint)
    {
        if (layer == null || coordinates == null)
            return false;

        var rect = coordinates.LayerToViewRect(layer);
        if (!rect.Contains(viewPoint))
            return false;

        var texture = GetPreviewAtlasTexture();
        if (texture == null || !texture.isReadable)
            return true;

        var textureRect = GetTextureRect(layer, new Vector2(texture.width, texture.height));
        var nx = rect.width > 0f ? Mathf.Clamp01((viewPoint.x - rect.xMin) / rect.width) : 0f;
        var ny = rect.height > 0f ? Mathf.Clamp01((rect.yMax - viewPoint.y) / rect.height) : 0f;
        var texX = Mathf.Clamp(Mathf.FloorToInt(textureRect.x + nx * textureRect.width), 0, texture.width - 1);
        var texY = Mathf.Clamp(Mathf.FloorToInt(textureRect.y + ny * textureRect.height), 0, texture.height - 1);

        try
        {
            return texture.GetPixel(texX, texY).a > 0.02f;
        }
        catch
        {
            return true;
        }
    }

    private bool IsPointInsideLayer(LayerInfo layer, PsdCoordinateSpace coordinates, Vector2 viewPoint, Vector2 docPoint)
    {
        if (layer == null)
            return false;

        if (coordinates != null && IsPointInsideLayerDrawMesh(layer, coordinates, viewPoint))
            return true;

        if (!PsdCoordinateSpace.IsLayerDocumentPointInsideRect(layer, docPoint))
            return false;

        var texture = GetPreviewAtlasTexture();
        if (texture == null || !texture.isReadable)
            return true;

        if (!PsdCoordinateSpace.TryLayerDocumentToTexturePixel(layer, docPoint, new Vector2Int(texture.width, texture.height), out var texX, out var texY))
            return false;

        try
        {
            return texture.GetPixel(texX, texY).a > 0.02f;
        }
        catch
        {
            return true;
        }
    }

    private bool IsPointInsideLayer(LayerInfo layer, Vector2 docPoint)
    {
        return IsPointInsideLayer(layer, null, Vector2.zero, docPoint);
    }

    private BoneHandleHit FindBoneHandleAt(
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        bool allowLengthEditing = false)
    {
        var order = boneDrawOrder.Count == bones.Count ? boneDrawOrder : null;
        var count = order != null ? order.Count : bones.Count;
        if (allowLengthEditing)
        {
            if (IsBoneTipHandleHit(selectedBoneIndex, mouseCanvas, coordinates))
                return new BoneHandleHit(selectedBoneIndex, BoneHandleType.Tip);

            for (var i = count - 1; i >= 0; i--)
            {
                var boneIndex = order != null ? order[i] : i;
                if (boneIndex == selectedBoneIndex)
                    continue;
                if (IsBoneTipHandleHit(boneIndex, mouseCanvas, coordinates))
                    return new BoneHandleHit(boneIndex, BoneHandleType.Tip);
            }
        }

        for (var i = count - 1; i >= 0; i--)
        {
            var boneIndex = order != null ? order[i] : i;
            if (boneIndex < 0 || boneIndex >= bones.Count)
                continue;

            var drawThickness = boneIndex == selectedBoneIndex ? 3f : 2f;
            var bone = bones[boneIndex];
            if (!bone.visible)
                continue;

            var start = coordinates.DocumentToView(bone.worldPosition);
            var end = coordinates.DocumentToView(GetBoneEnd(boneIndex));

            if (BoneShapeDrawer.IsPointOverBase(mouseCanvas, start, drawThickness, 3f))
                return new BoneHandleHit(boneIndex, BoneHandleType.Base);

            if (BoneShapeDrawer.IsPointOverBodyOrTip(mouseCanvas, start, end, drawThickness, 5f))
                return new BoneHandleHit(boneIndex, BoneHandleType.Rotate);
        }

        return BoneHandleHit.None;
    }

    private bool IsBoneTipHandleHit(
        int boneIndex,
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count || coordinates == null)
            return false;

        var bone = bones[boneIndex];
        if (bone == null || !bone.visible)
            return false;

        var drawThickness = boneIndex == selectedBoneIndex ? 3f : 2f;
        var end = coordinates.DocumentToView(GetBoneEnd(boneIndex));
        return BoneShapeDrawer.IsPointOverTip(mouseCanvas, end, drawThickness, 7f);
    }

    private enum BoneHandleType
    {
        None,
        Base,
        Tip,
        Rotate
    }

    private readonly struct BoneHandleHit
    {
        public static readonly BoneHandleHit None = new BoneHandleHit(-1, BoneHandleType.None);

        public readonly int index;
        public readonly BoneHandleType type;

        public BoneHandleHit(int index, BoneHandleType type)
        {
            this.index = index;
            this.type = type;
        }
    }

    private Vector2 GetBoneEnd(int index)
    {
        var bone = bones[index];
        var rotated = bone.worldRotation * Vector3.right;
        var dir = new Vector2(rotated.x, rotated.y);
        return bone.worldPosition + dir * bone.length;
    }

    private void ResizeBoneToWorldTip(BoneInfo bone, Vector2 worldTip)
    {
        if (bone == null)
            return;

        ResizeBoneBetweenWorldPoints(bone, bone.worldPosition, worldTip);
    }

    private void ResizeBoneBetweenWorldPoints(BoneInfo bone, Vector2 worldBase, Vector2 worldTip)
    {
        if (bone == null)
            return;

        var direction = worldTip - worldBase;
        var length = direction.magnitude;
        if (length <= 0.01f)
            return;

        var worldRotation = Quaternion.AngleAxis(
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg,
            Vector3.forward);
        bone.length = length;
        bone.rotation = bonesTreatAsWorldSpace || bone.parentId < 0 || bone.parentId >= bones.Count
            ? worldRotation
            : ConvertBoneWorldRotationToLocal(bone, worldRotation);
    }

    private void MoveBoneBaseToWorldPosition(
        BoneInfo bone,
        Vector2 targetWorld,
        bool preserveWorldTip,
        Vector2 preservedWorldTip)
    {
        if (bone == null)
            return;

        if (bonesTreatAsWorldSpace)
        {
            bone.position = targetWorld;
        }
        else if (bone.parentId >= 0 && bone.parentId < bones.Count)
        {
            bone.position = ConvertBoneWorldPositionToLocal(bone, targetWorld);
        }
        else
        {
            bone.position = targetWorld;
        }

        if (preserveWorldTip)
            ResizeBoneBetweenWorldPoints(bone, targetWorld, preservedWorldTip);
    }

    private Vector2 SnapBoneTipToNearbyBase(
        int resizingBoneIndex,
        Vector2 mouseCanvas,
        Vector2 unsnappedWorldTip,
        PsdCoordinateSpace coordinates)
    {
        if (coordinates == null || bones == null || bones.Count == 0)
            return unsnappedWorldTip;

        var maxDistanceSqr = BoneEndpointSnapDistanceCanvas * BoneEndpointSnapDistanceCanvas;
        var bestDistanceSqr = maxDistanceSqr;
        var snappedWorldTip = unsnappedWorldTip;
        for (var i = 0; i < bones.Count; i++)
        {
            if (i == resizingBoneIndex)
                continue;
            if (!isolateBoneModificationForCurrentDraw &&
                !bonesTreatAsWorldSpace &&
                IsBoneDescendantOf(i, resizingBoneIndex))
            {
                continue;
            }

            var candidate = bones[i];
            if (candidate == null || !candidate.visible)
                continue;

            var candidateCanvas = coordinates.DocumentToView(candidate.worldPosition);
            var distanceSqr = (mouseCanvas - candidateCanvas).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            snappedWorldTip = candidate.worldPosition;
        }

        return snappedWorldTip;
    }

    private Vector2 SnapBoneBaseToNearbyTip(
        int movingBoneIndex,
        Vector2 unsnappedWorldBase,
        PsdCoordinateSpace coordinates)
    {
        if (coordinates == null || bones == null || bones.Count == 0)
            return unsnappedWorldBase;

        var movingBaseCanvas = coordinates.DocumentToView(unsnappedWorldBase);
        var maxDistanceSqr = BoneEndpointSnapDistanceCanvas * BoneEndpointSnapDistanceCanvas;
        var bestDistanceSqr = maxDistanceSqr;
        var snappedWorldBase = unsnappedWorldBase;
        for (var i = 0; i < bones.Count; i++)
        {
            if (i == movingBoneIndex)
                continue;
            if (!isolateBoneModificationForCurrentDraw &&
                !bonesTreatAsWorldSpace &&
                IsBoneDescendantOf(i, movingBoneIndex))
            {
                continue;
            }

            var candidate = bones[i];
            if (candidate == null || !candidate.visible)
                continue;

            var candidateTip = GetBoneEnd(i);
            var candidateTipCanvas = coordinates.DocumentToView(candidateTip);
            var distanceSqr = (movingBaseCanvas - candidateTipCanvas).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            snappedWorldBase = candidateTip;
        }

        return snappedWorldBase;
    }

    private bool IsBoneDescendantOf(int candidateBoneIndex, int ancestorBoneIndex)
    {
        if (candidateBoneIndex < 0 || candidateBoneIndex >= bones.Count)
            return false;

        var parentIndex = bones[candidateBoneIndex] != null
            ? bones[candidateBoneIndex].parentId
            : -1;
        var guard = 0;
        while (parentIndex >= 0 && parentIndex < bones.Count && guard++ < bones.Count)
        {
            if (parentIndex == ancestorBoneIndex)
                return true;

            var parent = bones[parentIndex];
            parentIndex = parent != null ? parent.parentId : -1;
        }

        return false;
    }

    private void InvalidateBonePose()
    {
        bonePoseAtRest = false;
        bonePoseVersion++;
        if (bonePoseVersion < 0)
            bonePoseVersion = 1;
    }

    private static void DrawRectOutline(Rect rect, Color color, float thickness)
    {
        if (rect.width <= 0f || rect.height <= 0f || thickness <= 0f)
            return;

        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }

    private static void DrawSelectedLayerOutline(
        LayerInfo layer,
        PsdCoordinateSpace coordinateMapper)
    {
        if (layer == null || coordinateMapper == null)
            return;

        var dst = coordinateMapper.LayerToViewRect(layer);

        DrawRectOutline(dst, new Color(0.72f, 0.25f, 1f, 0.98f), 2.4f);
    }
}
#endif
