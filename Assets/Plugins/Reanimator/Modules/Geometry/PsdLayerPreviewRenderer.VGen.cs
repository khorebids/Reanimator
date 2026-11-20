#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private readonly List<Vector2>[] vGenContours = { new(), new() };
    private readonly List<Vector2> vGenStroke = new();
    private int vGenSide = -1;
    private int vGenControl;

    internal bool HasVGenContours => vGenContours[0].Count >= 3 && vGenContours[1].Count >= 3;
    internal bool IsDrawingVGenSide(int side) => vGenSide == side;

    internal void SetVGenSide(int side)
    {
        CancelVGenStroke();
        vGenSide = side;
    }

    internal void CancelVGenStroke()
    {
        if (vGenControl != 0 && GUIUtility.hotControl == vGenControl)
            GUIUtility.hotControl = 0;
        vGenControl = 0;
        vGenSide = -1;
        vGenStroke.Clear();
    }

    internal void ClearVGenContours()
    {
        CancelVGenStroke();
        foreach (var contour in vGenContours) contour.Clear();
    }

    internal bool HandleVGenInput(ReanimatorPreviewContext context)
    {
        var evt = Event.current;
        if (vGenSide < 0 || evt == null || context.Coordinates == null)
            return false;

        if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
        {
            CancelVGenStroke();
            evt.Use();
            return true;
        }

        if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt &&
            context.LocalRect.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
        {
            vGenControl = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = vGenControl;
            vGenStroke.Clear();
            AddPoint();
        }
        else if (vGenControl != 0 && GUIUtility.hotControl == vGenControl && evt.type == EventType.MouseDrag)
        {
            AddPoint();
        }
        else if (vGenControl != 0 && GUIUtility.hotControl == vGenControl &&
                 evt.type == EventType.MouseUp && evt.button == 0)
        {
            AddPoint();
            var contour = BuildSmoothedClosedBoobV2Contour(vGenStroke, 2, 32);
            if (contour.Count >= 3)
            {
                vGenContours[vGenSide].Clear();
                vGenContours[vGenSide].AddRange(contour);
            }
            CancelVGenStroke();
        }
        else
            return false;

        evt.Use();
        return true;

        void AddPoint()
        {
            var point = context.Coordinates.ViewToDocument(evt.mousePosition);
            if (vGenStroke.Count == 0 || (point - vGenStroke[vGenStroke.Count - 1]).sqrMagnitude >= 0.25f)
                vGenStroke.Add(point);
        }
    }

    internal void DrawVGenContours(PsdCoordinateSpace coordinates)
    {
        if (coordinates == null || Event.current.type != EventType.Repaint)
            return;
        var leftColor = new Color(0.12f, 1f, 0.86f);
        var rightColor = new Color(1f, 0.24f, 0.72f);
        Handles.BeginGUI();
        var color = Handles.color;
        DrawBoobV2Contour(coordinates.DocumentToView, vGenContours[0], leftColor, true);
        DrawBoobV2Contour(coordinates.DocumentToView, vGenContours[1], rightColor, true);
        DrawBoobV2Contour(coordinates.DocumentToView, vGenStroke, vGenSide == 0 ? leftColor : rightColor, false);
        Handles.color = color;
        Handles.EndGUI();
    }

    internal bool ApplyVGen(out string message)
    {
        message = "Draw both V-gen contours.";
        if (!HasVGenContours ||
            !TryGetVGenCenter(vGenContours[0], out var leftCenter) ||
            !TryGetVGenCenter(vGenContours[1], out var rightCenter))
            return false;

        var parent = selectedBoneIndex;
        if (parent < 0 || parent >= bones.Count || bones[parent] == null)
        {
            if (!TryFindBoneIndexByName("hips", out parent))
            {
                message = "V-gen needs a selected bone or a hips bone.";
                return false;
            }
        }

        if (!TryGetSelectedLayer(out var layer))
            TryFindBoobConstraintLayer((leftCenter + rightCenter) * 0.5f, out layer);
        if (layer == null || !TryGetLayerVertexData(layer, out var data) ||
            data?.vertices == null || data.vertices.Length < 3)
        {
            message = "Select a sprite with geometry.";
            return false;
        }

        var mappedLeft = MapFullBoobContourToBase(layer, vGenContours[0]);
        var mappedRight = MapFullBoobContourToBase(layer, vGenContours[1]);
        leftCenter = MapNippleGuessToBase(layer, leftCenter);
        rightCenter = MapNippleGuessToBase(layer, rightCenter);
        if (!TryRepairFullBoobContourPair(leftCenter, rightCenter, mappedLeft, mappedRight, out var left, out var right))
        {
            message = "V-gen could not resolve the two contours.";
            return false;
        }

        RegisterUndo("Apply V-gen");
        var previousCount = bones.Count;
        var root = FindOrCreateVGenBone(parent, "v_gen", (leftCenter + rightCenter) * 0.5f, true);
        var leftBone = FindOrCreateVGenBone(root, "v_gen_left", leftCenter, false);
        var rightBone = FindOrCreateVGenBone(root, "v_gen_right", rightCenter, false);
        if (!TryRebuildPairedContours(data, leftCenter, rightCenter, left, right, root, leftBone, rightBone,
                true, out var result, out message))
        {
            bones.RemoveRange(previousCount, bones.Count - previousCount);
            RecomputeBoneWorldTransforms();
            return false;
        }

        BuildBoneDrawOrder();
        MarkBoneStateChanged();
        ClearVGenContours();
        message = result;
        return true;
    }

    private int FindOrCreateVGenBone(int parent, string baseName, Vector2 point, bool shared)
    {
        var name = baseName;
        for (var suffix = 2; TryFindBoneIndexByName(name, out var existing); suffix++)
        {
            if (bones[existing].parentId == parent &&
                (shared || (GetBoneBasePoint(bones[existing]) - point).sqrMagnitude < 0.0001f))
                return existing;
            name = baseName + "_" + suffix;
        }

        var position = ConvertBoneWorldPositionToLocal(parent, point);
        var rotation = ConvertBoneWorldRotationToLocal(parent, bones[parent].worldRotation);
        var index = bones.Count;
        bones.Add(new BoneInfo
        {
            index = index,
            name = name,
            parentId = parent,
            position = position,
            initialPosition = position,
            rotation = rotation,
            initialRotation = rotation,
            length = Mathf.Max(0.5f, bones[parent].length * 0.4f),
            depth = index,
            color = CreateDefaultBoneColor(index),
            visible = true
        });
        RecomputeBoneWorldTransforms();
        return index;
    }

    private static bool TryGetVGenCenter(IReadOnlyList<Vector2> contour, out Vector2 center)
    {
        center = Vector2.zero;
        if(contour==null || contour.Count<3)return false;
        var origin = contour[0];
        var moment = Vector2.zero;
        var area = 0f;
        for (var i = 1; i + 1 < contour.Count; i++)
        {
            var a = contour[i] - origin;
            var b = contour[i + 1] - origin;
            var cross = FullBoobCross(a, b);
            area += cross;
            moment += (a + b) * cross;
        }
        if (Mathf.Abs(area) > 0.0001f)
        {
            center = origin + moment / (3f * area);
            if (IsStrictlyInsideFullBoobContour(center, contour)) return true;
        }

        var clearance = 0f;
        for (var i = 0; i < contour.Count; i++)
        {
            var point = (contour[i] + contour[(i + 1) % contour.Count] + contour[(i + 2) % contour.Count]) / 3f;
            if (!IsStrictlyInsideFullBoobContour(point, contour)) continue;
            var distance = DistanceToFullBoobContour(point, contour);
            if (distance <= clearance) continue;
            clearance = distance;
            center = point;
        }
        return clearance > 0f;
    }
}
#endif
