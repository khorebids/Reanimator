#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const float SoftContourMinimumWeight = 0.0001f;
    private static readonly float[] SoftContourLevels = { 0.2f, 0.4f, 0.6f, 0.8f };
    private readonly HashSet<(int, int)> selectedSoftContourPoints = new HashSet<(int, int)>();
    private LayerVertexData softContourSelectionLayer;
    private int[] softContourSelectionTriangles;
    private int softContourSelectionBone = -1;
    private bool selectingSoftContourPoints;
    private Vector2? softContourDragStart;
    private Vector2 softContourDragEnd;
    private int softContourDragControl;
    private readonly List<(Vector2 contour, Vector2 support)> softContourPairs = new();
    private float softContourPreviewDepth;
    internal float SoftContourDepth { get; set; } = 1f / 3f;
    internal bool HasSelectedSoftContourPoints => selectedSoftContourPoints.Count > 0;

    internal bool ApplySoftContour(out string message)
    {
        message = "SC requires a red support point for every selected contour point.";
        if (!HasSoftContourSelection() ||
            !vertexDataByLayerSelectionKey.TryGetValue(selectedLayerSelectionKey, out var data) ||
            data != softContourSelectionLayer || data.triangles != softContourSelectionTriangles ||
            selectedBoneIndex != softContourSelectionBone || SoftContourDepth != softContourPreviewDepth ||
            softContourPairs.Count == 0 || softContourPairs.Count != selectedSoftContourPoints.Count)
            return false;

        int source = FindSpriteBoneIndexForRendererBone(data, selectedBoneIndex);
        if (source < 0) return false;
        var staged = CloneProjectorLayerData(data);
        var contourIndices = new HashSet<int>();
        var supportIndices = new HashSet<int>();
        foreach (var pair in softContourPairs)
        {
            int contour = InsertOrFind(pair.contour);
            int support = InsertOrFind(pair.support);
            if (contour < 0 || support < 0)
            {
                message = "SC could not insert a point into the selected sprite mesh.";
                return false;
            }
            contourIndices.Add(contour);
            supportIndices.Add(support);
        }
        if (contourIndices.Overlaps(supportIndices))
        {
            message = "SC support and contour points overlap. Increase Depth.";
            return false;
        }

        var parent = bones[selectedBoneIndex];
        string name = parent.name + "_SC";
        int childIndex = bones.FindIndex(bone => bone.parentId == selectedBoneIndex && bone.name == name);
        bool addBone = childIndex < 0;
        RegisterUndo("Apply Soft Contour");
        if (addBone)
        {
            childIndex = bones.Count;
            Vector2 position = bonesTreatAsWorldSpace ? GetBoneMidpoint(parent) : Vector2.right * (parent.length * 0.5f);
            Quaternion rotation = bonesTreatAsWorldSpace ? parent.worldRotation : Quaternion.identity;
            bones.Add(new BoneInfo
            {
                index = childIndex,
                name = name,
                parentId = selectedBoneIndex,
                position = position,
                initialPosition = position,
                rotation = rotation,
                initialRotation = rotation,
                length = Mathf.Max(0.5f, parent.length / 3f),
                depth = childIndex,
                color = CreateDefaultBoneColor(childIndex),
                visible = true
            });
            RecomputeBoneWorldTransforms();
        }

        int child = FindSpriteBoneIndexForRendererBone(staged, childIndex);
        if (child < 0 && !TryAppendSkinningTargetBone(staged, childIndex, out child))
        {
            if (addBone) bones.RemoveAt(childIndex);
            message = "SC could not bind the generated bone to the selected sprite.";
            return false;
        }
        source = FindSpriteBoneIndexForRendererBone(staged, selectedBoneIndex);
        foreach (int index in supportIndices)
            staged.boneWeights[index] = new BoneWeight { boneIndex0 = source, weight0 = 1f };
        foreach (int index in contourIndices)
            staged.boneWeights[index] = new BoneWeight { boneIndex0 = child, weight0 = 1f };

        data.vertices = staged.vertices;
        data.vertexUvs = staged.vertexUvs;
        data.triangles = staged.triangles;
        data.allEdgePairs = staged.allEdgePairs;
        data.boundaryEdgePairs = staged.boundaryEdgePairs;
        data.boneWeights = staged.boneWeights;
        data.spriteBoneToRendererBone = staged.spriteBoneToRendererBone;
        data.bindBoneInverseMatrices = staged.bindBoneInverseMatrices;
        data.usesSpriteBindPoses = staged.usesSpriteBindPoses;
        BuildBoneDrawOrder();
        MarkBoneStateChanged();
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectedLayerSelectionKey, BuildKey(data.layer.name, data.layer.layerId));
        selectedSoftContourPoints.Clear();
        softContourPairs.Clear();
        SelectingSoftContourPoints = false;
        message = $"SC: {name}, {contourIndices.Count} contour and {supportIndices.Count} support points.";
        return true;

        int InsertOrFind(Vector2 point)
        {
            for (int i = 0; i < staged.vertices.Length; i++)
                if ((staged.vertices[i] - point).sqrMagnitude <= 0.0001f) return i;
            int index = staged.vertices.Length;
            return InsertAppliedVertexIntoLayerData(staged, point, true) &&
                System.Array.IndexOf(staged.triangles, index) >= 0 ? index : -1;
        }
    }

    internal bool SelectingSoftContourPoints
    {
        get => selectingSoftContourPoints;
        set
        {
            selectingSoftContourPoints = value;
            if (!value) EndSoftContourDrag();
        }
    }

    internal bool HasSoftContourSelection()
    {
        return selectedBoneIndex >= 0 && selectedBoneIndex < bones.Count &&
            bones[selectedBoneIndex] != null && TryGetSelectedLayer(out _);
    }

    internal void DrawSoftContour(PsdCoordinateSpace coordinates)
    {
        if (Event.current?.type == EventType.Repaint)
        {
            ProcessSoftContour(coordinates, false);
            if (softContourDragStart.HasValue)
            {
                Rect rectangle = GetSoftContourSelectionRect();
                EditorGUI.DrawRect(rectangle, new Color(0.1f, 1f, 0.9f, 0.13f));
                DrawRectOutline(rectangle, new Color(0.1f, 1f, 0.9f, 0.9f), 1f);
            }
        }
    }

    internal bool HandleSoftContourPointInput(ReanimatorPreviewContext context)
    {
        var evt = Event.current;
        if (!SelectingSoftContourPoints || evt == null) return false;
        if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt &&
            GUIUtility.hotControl == 0 && context.LocalRect.Contains(evt.mousePosition))
        {
            softContourDragStart = evt.shift ? evt.mousePosition : (Vector2?)null;
            softContourDragEnd = evt.mousePosition;
            if (evt.shift)
            {
                softContourDragControl = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = softContourDragControl;
            }
            else ProcessSoftContour(context.Coordinates, true);
            if (evt.type != EventType.Used) evt.Use();
            return true;
        }
        if (!softContourDragStart.HasValue || GUIUtility.hotControl != softContourDragControl) return false;
        if (evt.type == EventType.MouseDrag)
        {
            softContourDragEnd = evt.mousePosition;
            evt.Use();
            return true;
        }
        if (evt.type != EventType.MouseUp) return false;
        softContourDragEnd = evt.mousePosition;
        Rect? rectangle = (softContourDragEnd - softContourDragStart.Value).sqrMagnitude >= 16f
            ? GetSoftContourSelectionRect() : (Rect?)null;
        EndSoftContourDrag();
        ProcessSoftContour(context.Coordinates, true, rectangle, extendSelection: true);
        if (evt.type != EventType.Used) evt.Use();
        return true;
    }

    private void EndSoftContourDrag()
    {
        if (softContourDragControl != 0 && GUIUtility.hotControl == softContourDragControl)
            GUIUtility.hotControl = 0;
        softContourDragControl = 0;
        softContourDragStart = null;
    }

    private Rect GetSoftContourSelectionRect()
    {
        Vector2 start = softContourDragStart.Value;
        Vector2 min = Vector2.Min(start, softContourDragEnd);
        Vector2 max = Vector2.Max(start, softContourDragEnd);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private bool ProcessSoftContour(PsdCoordinateSpace coordinates, bool selectPoint,
        Rect? selectionRectangle = null, bool extendSelection = false)
    {
        if (coordinates == null ||
            !HasSoftContourSelection() ||
            !vertexDataByLayerSelectionKey.TryGetValue(selectedLayerSelectionKey, out var data) ||
            data.layer == null || !data.layer.visible || data.triangles == null || data.boneWeights == null)
            return false;

        int bone = FindSpriteBoneIndexForRendererBone(data, selectedBoneIndex);
        if (bone < 0) return false;
        var vertices = GetLayerVerticesForDraw(data);
        if (vertices == null || vertices.Length != data.boneWeights.Length) return false;
        if (softContourSelectionLayer != data || softContourSelectionTriangles != data.triangles ||
            softContourSelectionBone != selectedBoneIndex)
        {
            selectedSoftContourPoints.Clear();
            softContourSelectionLayer = data;
            softContourSelectionTriangles = data.triangles;
            softContourSelectionBone = selectedBoneIndex;
        }
        var toView = CreateLayerVertexToViewMapper(coordinates, data, vertices);
        var weights = new float[vertices.Length];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = GetSpriteBoneWeight(data.boneWeights[i], bone);

        var points = new Dictionary<(int, int), Vector2>();
        var documentPoints = new Dictionary<(int, int), Vector2>();
        var segments = new List<(Vector2 a, Vector2 b, Vector2 inward)>();
        if (!selectPoint) Handles.BeginGUI();
        var previousColor = Handles.color;
        var line = new Vector3[2];
        try
        {
            Handles.color = new Color(1f, 0.72f, 0.28f, 0.9f);
            if (!selectPoint)
                foreach (float level in SoftContourLevels) DrawLevel(level, 1.5f);
            Handles.color = new Color(1f, 0.85f, 0.44f);
            DrawLevel(SoftContourMinimumWeight, 3.5f);

            // Close the influence region where it reaches the edge of the sprite mesh.
            var boundary = data.boundaryEdgePairs;
            for (int i = 0; boundary != null && i + 1 < boundary.Length; i += 2)
            {
                int a = boundary[i], b = boundary[i + 1];
                if (!Valid(a) || !Valid(b)) continue;
                bool aInside = weights[a] >= SoftContourMinimumWeight;
                bool bInside = weights[b] >= SoftContourMinimumWeight;
                if (!aInside && !bInside) continue;
                Vector2 start = aInside ? AddPoint(a, a, vertices[a]) : Intersect(a, b, SoftContourMinimumWeight);
                Vector2 end = bInside ? AddPoint(b, b, vertices[b]) : Intersect(a, b, SoftContourMinimumWeight);
                DrawSegment(start, end, 3.5f);
                if (!selectPoint)
                {
                    for (int t = 0; t + 2 < data.triangles.Length; t += 3)
                    {
                        int x = data.triangles[t], y = data.triangles[t + 1], z = data.triangles[t + 2];
                        if (TryGetThirdVertexForEdge(x, y, z, a, b, out int third) && Valid(third))
                        {
                            AddSegment(start, end, vertices[third]);
                            break;
                        }
                    }
                }
            }

            if (selectPoint)
            {
                var evt = Event.current;
                if (selectionRectangle.HasValue)
                {
                    softContourPairs.Clear();
                    foreach (var point in points)
                        if (selectionRectangle.Value.Contains(point.Value))
                            selectedSoftContourPoints.Add(point.Key);
                    return true;
                }
                float closestDistance = 10f * 10f;
                (int, int)? closest = null;
                foreach (var point in points)
                {
                    float distance = (point.Value - evt.mousePosition).sqrMagnitude;
                    if (distance >= closestDistance) continue;
                    closestDistance = distance;
                    closest = point.Key;
                }
                if (!closest.HasValue) return false;
                softContourPairs.Clear();
                bool extend = extendSelection || evt.shift || evt.control || evt.command;
                if (!extend) selectedSoftContourPoints.Clear();
                if (!selectedSoftContourPoints.Add(closest.Value))
                    selectedSoftContourPoints.Remove(closest.Value);
                evt.Use();
                return true;
            }

            softContourPairs.Clear();
            softContourPreviewDepth = SoftContourDepth;
            foreach (var point in points)
            {
                bool selected = selectedSoftContourPoints.Contains(point.Key);
                Handles.color = selected ? new Color(0.1f, 1f, 0.9f) : new Color(0.62f, 0.62f, 0.62f);
                Handles.DrawSolidDisc(point.Value, Vector3.forward, selected ? 5.5f : 3.75f);
            }
            foreach (var point in documentPoints)
            {
                if (!selectedSoftContourPoints.Contains(point.Key) ||
                    !TryGetSoftContourSupport(point.Value, segments, SoftContourDepth, out var support))
                    continue;
                softContourPairs.Add((ConvertPointToBaseDoc(data, point.Value), ConvertPointToBaseDoc(data, support)));
                Handles.color = new Color(1f, 0.24f, 0.28f);
                Handles.DrawSolidDisc(toView(support), Vector3.forward, 4.5f);
            }
        }
        finally
        {
            Handles.color = previousColor;
            if (!selectPoint) Handles.EndGUI();
        }
        return false;

        bool Valid(int index) => (uint)index < (uint)vertices.Length;

        Vector2 Intersect(int a, int b, float level)
        {
            float fraction = (level - weights[a]) / (weights[b] - weights[a]);
            Vector2 point = Vector2.Lerp(vertices[a], vertices[b], fraction);
            if (level != SoftContourMinimumWeight) return point;
            if (fraction <= 0f) return AddPoint(a, a, point);
            if (fraction >= 1f) return AddPoint(b, b, point);
            return AddPoint(a, b, point);
        }

        Vector2 AddPoint(int a, int b, Vector2 point)
        {
            var key = (Mathf.Min(a, b), Mathf.Max(a, b));
            points[key] = toView(point);
            documentPoints[key] = point;
            return point;
        }

        void AddSegment(Vector2 start, Vector2 end, Vector2 inside)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude < 1e-10f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
            if (Vector2.Dot(normal, inside - start) < 0f) normal = -normal;
            segments.Add((start, end, normal));
        }

        void DrawSegment(Vector2 start, Vector2 end, float width)
        {
            if (selectPoint) return;
            line[0] = toView(start);
            line[1] = toView(end);
            if (line[0] != line[1]) Handles.DrawAAPolyLine(width, line);
        }

        void DrawLevel(float level, float width)
        {
            for (int i = 0; i + 2 < data.triangles.Length; i += 3)
            {
                int a = data.triangles[i], b = data.triangles[i + 1], c = data.triangles[i + 2];
                if (!Valid(a) || !Valid(b) || !Valid(c)) continue;
                bool aInside = weights[a] >= level;
                bool bInside = weights[b] >= level;
                bool cInside = weights[c] >= level;
                if (aInside == bInside && bInside == cInside) continue;

                // The vertex on its own side of the level crosses both incident edges.
                int single = aInside == bInside ? c : aInside == cInside ? b : a;
                int otherA = single == a ? b : a;
                int otherB = single == c ? b : c;
                Vector2 start = Intersect(single, otherA, level);
                Vector2 end = Intersect(single, otherB, level);
                DrawSegment(start, end, width);
                if (!selectPoint && level == SoftContourMinimumWeight)
                    AddSegment(start, end, vertices[weights[single] >= level ? single : otherA]);
            }
        }
    }

    internal static bool TryGetSoftContourSupport(Vector2 point,
        IReadOnlyList<(Vector2 a, Vector2 b, Vector2 inward)> segments, float fraction, out Vector2 support)
    {
        support = point;
        Vector2 normal = Vector2.zero;
        foreach (var segment in segments)
            if ((segment.a - point).sqrMagnitude < 1e-6f || (segment.b - point).sqrMagnitude < 1e-6f)
                normal += segment.inward;
        if (normal.sqrMagnitude < 1e-6f) return false;
        normal.Normalize();
        float depth = float.PositiveInfinity;
        foreach (var segment in segments)
        {
            Vector2 edge = segment.b - segment.a;
            float determinant = Cross(normal, edge);
            if (Mathf.Abs(determinant) < 1e-6f || Vector2.Dot(normal, segment.inward) >= 0f) continue;
            Vector2 delta = segment.a - point;
            float rayDistance = Cross(delta, edge) / determinant;
            float edgeFraction = Cross(delta, normal) / determinant;
            if (rayDistance > 0.001f && edgeFraction >= -0.0001f && edgeFraction <= 1.0001f)
                depth = Mathf.Min(depth, rayDistance);
        }
        if (float.IsInfinity(depth)) return false;
        support = point + normal * (depth * Mathf.Clamp(fraction, 0.01f, 0.99f));
        return true;

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
#endif
