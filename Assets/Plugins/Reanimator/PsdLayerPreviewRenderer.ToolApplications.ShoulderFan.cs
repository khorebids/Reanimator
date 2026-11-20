#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    [SerializeField] private List<Vector2> shoulderWeldContour = new();
    [SerializeField] private string shoulderWeldLayer;
    [SerializeField] private string shoulderWeldBone;
    [SerializeField] internal float ShoulderFanDepth = 0.15f;
    [SerializeField] internal float ShoulderFanShiftWidth = 0.25f;
    [Serializable] private sealed class ShoulderFanStamp { public string key, signature; }
    [SerializeField] private List<ShoulderFanStamp> shoulderFanStamps = new();
    private List<Vector2> shoulderWeldStroke = new();
    private int shoulderWeldControl;
    internal bool ShoulderDrawing { get; private set; }
    internal void ToggleShoulderOutline()
    {
        var next = !ShoulderDrawing;
        CancelShoulderOutline();
        ShoulderDrawing = next;
    }

    internal void CancelShoulderOutline()
    {
        ShoulderDrawing = false;
        shoulderWeldStroke ??= new List<Vector2>();
        shoulderWeldStroke.Clear();
        if (shoulderWeldControl != 0 && GUIUtility.hotControl == shoulderWeldControl)
            GUIUtility.hotControl = 0;
        shoulderWeldControl = 0;
    }

    private bool HandleShoulderOutline(Rect viewport, PsdCoordinateSpace coordinates)
    {
        if (!ShoulderDrawing || coordinates == null)
            return false;
        shoulderWeldStroke ??= new List<Vector2>();
        var input = Event.current;
        if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
            ToggleShoulderOutline();
        else if (input.type == EventType.MouseDown && input.button == 0 && !input.alt &&
                 viewport.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
        {
            shoulderWeldControl = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = shoulderWeldControl;
            shoulderWeldStroke.Clear();
            shoulderWeldStroke.Add(coordinates.ViewToDocument(input.mousePosition));
        }
        else if (shoulderWeldControl != 0 && GUIUtility.hotControl == shoulderWeldControl &&
                 input.type == EventType.MouseDrag)
        {
            var point = coordinates.ViewToDocument(input.mousePosition);
            if ((point - shoulderWeldStroke[shoulderWeldStroke.Count - 1]).sqrMagnitude >= 4f)
                shoulderWeldStroke.Add(point);
        }
        else if (shoulderWeldControl != 0 && GUIUtility.hotControl == shoulderWeldControl &&
                 input.type == EventType.MouseUp && input.button == 0)
        {
            var contour = BuildSmoothedClosedBoobV2Contour(shoulderWeldStroke, 1, 32);
            if (contour.Count >= 3 && selectedBoneIndex >= 0 && selectedBoneIndex < bones.Count)
            {
                shoulderWeldContour = contour;
                shoulderWeldLayer = selectedLayerSelectionKey;
                shoulderWeldBone = bones[selectedBoneIndex].name;
                SaveCurrentShoulderContour();
            }
            ToggleShoulderOutline();
        }
        else
            return false;
        input.Use();
        return true;
    }

    internal static bool TryGetShoulderReferenceWidth(Vector2[] vertices, int[] boundary, Vector2 origin,
        Vector2 armDirection, out Vector2 a, out Vector2 b)
    {
        a = b = default;
        if (armDirection.sqrMagnitude < 1e-10f) return false;
        var normal = Mathf.Atan2(armDirection.y, armDirection.x) + Mathf.PI * 0.5f;
        return TryGetNearestContourRayIntersection(vertices, boundary, origin, normal, out a) &&
            TryGetNearestContourRayIntersection(vertices, boundary, origin, normal + Mathf.PI, out b) &&
            (a - b).sqrMagnitude > 1e-8f;
    }

    internal float ShoulderReferenceWidth
    {
        get
        {
            if (!TryGetBoneParent(selectedBoneIndex, out var arm, out _, out _) ||
                !TryGetSelectedLayer(out var layer) || !TryGetLayerVertexData(layer, out var data)) return 0;
            return TryGetShoulderReferenceWidth(GetLayerVerticesForDraw(data), data.boundaryEdgePairs,
                GetBoneBasePoint(arm), arm.worldRotation * Vector3.right, out var a, out var b) ? Vector2.Distance(a, b) : 0;
        }
    }

    private string ShoulderFanSignature(LayerVertexData data)
    {
        var text = new System.Text.StringBuilder("ShoulderContourV10SingleHelper|");
        var used = new HashSet<int>(data.spriteBoneToRendererBone.Where(index => index >= 0 && index < bones.Count));
        foreach (int index in used.ToArray())
            for (int parent = bones[index].parentId, guard = 0; parent >= 0 && parent < bones.Count && guard < bones.Count; parent = bones[parent].parentId, guard++) used.Add(parent);
        var names = new HashSet<string>(used.Select(index => bones[index].name));
        foreach (var rig in previewBoneRigs.bones.Where(rig => names.Contains(rig.bone))) text.Append(JsonUtility.ToJson(rig));
        foreach (var curve in new[] { ShoulderTransitionCurve, ShoulderContourHelperCurve })
            foreach (var key in curve.keys) { text.Append(key.time).Append(":").Append(key.value).Append(":").Append(key.inTangent).Append(":").Append(key.outTangent).Append(":").Append(key.inWeight).Append(":").Append(key.outWeight).Append(":").Append((int)key.weightedMode); }
        void Number(float value) => text.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
        Number(ShoulderFanDepth); Number(ShoulderFanShiftWidth);
        foreach (var point in shoulderWeldContour) { Number(point.x); Number(point.y); }
        foreach (var bone in used.OrderBy(index => index).Select(index => bones[index]))
        {
            text.Append(bone.name).Append(':').Append(bone.parentId).Append(':').Append(bone.depth);
            Number(bone.position.x); Number(bone.position.y); Number(bone.rotation.x); Number(bone.rotation.y);
            Number(bone.rotation.z); Number(bone.rotation.w); Number(bone.length);
        }
        foreach (var point in data.vertices) { Number(point.x); Number(point.y); }
        foreach (var point in data.vertexUvs) { Number(point.x); Number(point.y); }
        foreach (var triangle in data.triangles) text.Append(triangle).Append(',');
        foreach (var mapping in data.spriteBoneToRendererBone) text.Append(mapping).Append(',');
        foreach (var matrix in data.bindBoneInverseMatrices)
            for (int i = 0; i < 16; i++) Number(matrix[i]);
        foreach (var weight in data.boneWeights)
        {
            text.Append(weight.boneIndex0).Append(','); Number(weight.weight0);
            text.Append(weight.boneIndex1).Append(','); Number(weight.weight1);
            text.Append(weight.boneIndex2).Append(','); Number(weight.weight2);
            text.Append(weight.boneIndex3).Append(','); Number(weight.weight3);
        }
        return Hash128.Compute(text.ToString()).ToString();
    }
}
#endif
