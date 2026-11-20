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
    private readonly List<(Vector2 contour, Vector2 support, bool soften)> softContourPairs = new();
    private float softContourPreviewDepth;
    private bool softContourPreviewAutoMode;
    private (float end, float split, bool hips) softContourPreviewSettings;
    private int softContourAutoPointCount;

    internal bool CanApplyAutoSoftContour => SoftContourAutoMode && softContourPreviewAutoMode &&
        softContourPreviewSettings.Equals((SoftContourMaxEndAngle, SoftContourSplitAngle, SoftContourHipsMode)) &&
        softContourPreviewDepth == SoftContourDepth && softContourAutoPointCount > 0 &&
        softContourPairs.Count == softContourAutoPointCount &&
        selectedBoneIndex == softContourSelectionBone &&
        vertexDataByLayerSelectionKey.TryGetValue(selectedLayerSelectionKey ?? string.Empty, out var layer) &&
        layer == softContourSelectionLayer && layer.triangles == softContourSelectionTriangles;
    internal bool SoftContourAutoMode { get; set; }
    internal bool SoftContourHipsMode { get; set; }
    internal float SoftContourMaxEndAngle { get; set; } = 25f;
    internal float SoftContourSplitAngle { get; set; } = 25f;
    private float softContourSimpleDepth = 1f / 3f;
    private float softContourAutoDepth = 0.2f;
    internal float SoftContourDepth
    {
        get => SoftContourAutoMode ? softContourAutoDepth : softContourSimpleDepth;
        set
        {
            if (SoftContourAutoMode)
                softContourAutoDepth = value;
            else
                softContourSimpleDepth = value;
        }
    }
    internal bool HasSelectedSoftContourPoints => selectedSoftContourPoints.Count > 0;

    internal string ApplyAllInSoftContour(int targets)
    {
        var originalLayer = selectedLayerSelectionKey;
        var originalBone = selectedBoneIndex;
        var originalAuto = SoftContourAutoMode;
        var originalHips = SoftContourHipsMode;
        var results = new List<string>();
        try
        {
            SoftContourAutoMode = true;
            for (var bit = 0; bit < 9; bit++)
            {
                if ((targets & (1 << bit)) == 0)
                    continue;

                SoftContourHipsMode = bit == 8;
                var side = bit < 4 ? "l_" : "r_";
                var sprite = SoftContourHipsMode ? "body" : side + (bit % 4 < 2 ? "arm" : "leg");
                var bone = SoftContourHipsMode ? "hips" : sprite + (bit % 2 == 0 ? "" : "_1");
                var label = sprite + " / " + bone;
                if (!TryFindBoneIndexByName(bone, out selectedBoneIndex))
                {
                    results.Add(label + ": no matching bone");
                    continue;
                }

                var layers = new List<string>();
                foreach (var entry in vertexDataByLayerSelectionKey)
                {
                    var data = entry.Value;
                    if (data?.layer != null && data.layer.visible &&
                        string.Equals(data.layer.name, sprite, System.StringComparison.OrdinalIgnoreCase) &&
                        FindSpriteBoneIndexForRendererBone(data, selectedBoneIndex) >= 0)
                        layers.Add(entry.Key);
                }
                layers.Sort(System.StringComparer.Ordinal);
                if (layers.Count == 0)
                    results.Add(label + ": no matching weighted sprite");

                foreach (var layer in layers)
                {
                    selectedLayerSelectionKey = layer;
                    softContourPairs.Clear();
                    softContourAutoPointCount = 0;
                    ProcessSoftContour(null, false, draw: false);
                    if (softContourAutoPointCount == 0)
                    {
                        results.Add(label + ": no eligible contour");
                        continue;
                    }

                    if (!ApplySoftContour(out var message))
                        throw new System.InvalidOperationException(label + ": " + message);
                    results.Add(label + ": " + message);
                }
            }
        }
        finally
        {
            selectedLayerSelectionKey = originalLayer;
            selectedBoneIndex = originalBone;
            SoftContourAutoMode = originalAuto;
            SoftContourHipsMode = originalHips;
            softContourPairs.Clear();
        }

        return results.Count == 0 ? "No SC targets enabled" : string.Join("; ", results);
    }

    private bool IsSoftContourHipsPointBlocked(Vector2 point)
    {
        foreach (var layer in vertexDataByLayerSelectionKey.Values)
        {
            var name = layer?.layer?.name;
            if ((!string.Equals(name, "l_leg", System.StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(name, "r_leg", System.StringComparison.OrdinalIgnoreCase)) || layer.triangles == null)
                continue;

            var vertices = GetLayerVerticesForDraw(layer);
            if (vertices == null)
                continue;

            var triangles = layer.triangles;
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                if ((uint)a < vertices.Length && (uint)b < vertices.Length && (uint)c < vertices.Length &&
                    IsPointInTriangleInclusive(point, vertices[a], vertices[b], vertices[c]))
                    return true;
            }
        }

        return false;
    }

    internal bool ApplySoftContour(out string message)
    {
        message = "SC requires a red support point for every selected contour point.";
        if (!HasSoftContourSelection() ||
            !vertexDataByLayerSelectionKey.TryGetValue(selectedLayerSelectionKey, out var data) ||
            data != softContourSelectionLayer || data.triangles != softContourSelectionTriangles ||
            selectedBoneIndex != softContourSelectionBone || SoftContourDepth != softContourPreviewDepth ||
            softContourPairs.Count == 0 || softContourPreviewAutoMode != SoftContourAutoMode ||
            (SoftContourAutoMode ? !CanApplyAutoSoftContour : softContourPairs.Count != selectedSoftContourPoints.Count))
            return false;

        if (!ApplySoftContourPairs(data, selectedBoneIndex, softContourPairs, out message, inheritParentOrder: !SoftContourHipsMode))
            return false;
        selectedSoftContourPoints.Clear();
        softContourPairs.Clear();
        SelectingSoftContourPoints = false;
        return true;
    }

    private bool ApplySoftContourPairs(LayerVertexData data, int parentIndex,
        IReadOnlyList<(Vector2 contour, Vector2 support, bool soften)> pairs, out string message, bool inheritParentOrder = true)
    {
        message = "SC requires a weighted source bone.";
        int source = FindSpriteBoneIndexForRendererBone(data, parentIndex);
        if (source < 0) return false;
        var staged = CloneProjectorLayerData(data);
        var contourIndices = new HashSet<int>();
        var softenedIndices = new HashSet<int>();
        var supportIndices = new HashSet<int>();
        foreach (var pair in pairs)
        {
            int contour = InsertOrFind(pair.contour);
            int support = InsertOrFind(pair.support);
            if (contour < 0 || support < 0)
            {
                message = "SC could not insert a point into the selected sprite mesh.";
                return false;
            }
            contourIndices.Add(contour);
            if (pair.soften) softenedIndices.Add(contour);
            supportIndices.Add(support);
        }
        if (contourIndices.Overlaps(supportIndices))
        {
            message = "SC support and contour points overlap. Increase Depth.";
            return false;
        }

        var parent = bones[parentIndex];
        string name = parent.name + "_SC";
        int childIndex = bones.FindIndex(bone => bone.parentId == parentIndex && bone.name == name);
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
                parentId = parentIndex,
                position = position,
                initialPosition = position,
                rotation = rotation,
                initialRotation = rotation,
                length = Mathf.Max(0.5f, parent.length / 3f),
                depth = inheritParentOrder ? parent.depth : childIndex,
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
        if (inheritParentOrder)
        {
            bones[childIndex].depth = parent.depth;
            boneOrderVersion++;
        }
        source = FindSpriteBoneIndexForRendererBone(staged, parentIndex);
        foreach (int index in supportIndices)
            staged.boneWeights[index] = RedistributeSoftContourWeight(staged.boneWeights[index], source, child, 0f);
        foreach (int index in contourIndices)
            staged.boneWeights[index] = RedistributeSoftContourWeight(staged.boneWeights[index], source, child,
                softenedIndices.Contains(index) ? .5f : 1f);

        SmoothSoftContourEnds(staged, softenedIndices, supportIndices, source, child);

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
        MarkEditGeometryDataChanged(data, BuildSelectionKey(data.layer), BuildKey(data.layer.name, data.layer.layerId));
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

    // SC owns only the source/SC share. Joint helpers and adjacent segments
    // already describe the bent surface and must survive both rows and smoothing.
    internal static BoneWeight RedistributeSoftContourWeight(BoneWeight weight, int source, int child, float fraction)
    {
        float budget = GetSpriteBoneWeight(weight, source) + GetSpriteBoneWeight(weight, child);
        if (budget <= 0f) return weight;
        var values = new Dictionary<int, float>();
        Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
        Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
        fraction = Mathf.Clamp01(fraction);
        AccumulateWeightChannel(values, source, budget * (1 - fraction));
        AccumulateWeightChannel(values, child, budget * fraction);
        // Unity BoneWeight has four channels. Do not discard an existing joint
        // influence just to add a fifth channel for endpoint softening.
        if (values.Count > 4) return weight;
        return BuildVertexFixWeight(values);

        void Add(int bone, float value)
        {
            if (bone != source && bone != child) AccumulateWeightChannel(values, bone, value);
        }
    }

    private static void SmoothSoftContourEnds(LayerVertexData data, HashSet<int> ends, HashSet<int> supports, int source, int child)
    {
        if (ends.Count == 0)
            return;
        // Select only the immediate contour neighbors, never the inward supports
        // or a second ring reached through another selected endpoint.
        var selected = new HashSet<int>(ends);
        BuildEdgePairs(data.triangles, data.vertices.Length, out _, out var boundary);
        for (var i = 0; i + 1 < boundary.Length; i += 2)
        {
            var a = boundary[i];
            var b = boundary[i + 1];
            if (ends.Contains(a) && !supports.Contains(b)) selected.Add(b);
            if (ends.Contains(b) && !supports.Contains(a)) selected.Add(a);
        }
        selected.ExceptWith(supports);
        // The shared smoother reads one snapshot, keeping both endpoint results
        // independent of traversal order and of each other's updated weights.
        var original = (BoneWeight[])data.boneWeights.Clone();
        SmoothFullBoobRingWeights(data, new List<int>(selected), 0.5f);
        foreach (int index in selected)
        {
            var smoothed = data.boneWeights[index];
            float budget = GetSpriteBoneWeight(smoothed, source) + GetSpriteBoneWeight(smoothed, child);
            data.boneWeights[index] = RedistributeSoftContourWeight(original[index], source, child,
                budget > 0f ? GetSpriteBoneWeight(smoothed, child) / budget : 0f);
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
            if (SoftContourDrawnBellyMode)
            {
                DrawSoftContourBelly(coordinates);
                return;
            }

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
        Rect? selectionRectangle = null, bool extendSelection = false, bool draw = true, LayerVertexData maskedMesh = null,
        int maskedBone = -1, Vector2[][] masks = null,
        List<(Vector2 contour, Vector2 support, bool soften)> maskedPairs = null)
    {
        var data = maskedMesh;
        if (data == null && ((draw && coordinates == null) ||
            !HasSoftContourSelection() ||
            !vertexDataByLayerSelectionKey.TryGetValue(selectedLayerSelectionKey, out data) ||
            data.layer == null || !data.layer.visible || data.triangles == null || data.boneWeights == null))
            return false;

        int bone = FindSpriteBoneIndexForRendererBone(data, maskedMesh == null ? selectedBoneIndex : maskedBone);
        if (bone < 0 || data.boneWeights == null) return false;
        var vertices = GetLayerVerticesForDraw(data);
        if (vertices == null || vertices.Length != data.boneWeights.Length) return false;
        if (maskedMesh == null && (softContourSelectionLayer != data || softContourSelectionTriangles != data.triangles ||
            softContourSelectionBone != selectedBoneIndex))
        {
            selectedSoftContourPoints.Clear();
            softContourSelectionLayer = data;
            softContourSelectionTriangles = data.triangles;
            softContourSelectionBone = selectedBoneIndex;
        }
        System.Func<Vector2, Vector2> toView = draw ? CreateLayerVertexToViewMapper(coordinates, data, vertices) : point => point;
        var weights = new float[vertices.Length];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = GetSpriteBoneWeight(data.boneWeights[i], bone);

        var points = new Dictionary<(int, int), Vector2>();
        var documentPoints = new Dictionary<(int, int), Vector2>();
        var segments = new List<(Vector2 a, Vector2 b, Vector2 inward)>();
        if (draw && !selectPoint) Handles.BeginGUI();
        var previousColor = Handles.color;
        var line = new Vector3[2];
        try
        {
            if (draw) Handles.color = new Color(1f, 0.72f, 0.28f, 0.9f);
            if (draw && !selectPoint)
                foreach (float level in SoftContourLevels) DrawLevel(level, 1.5f);
            if (draw) Handles.color = new Color(1f, 0.85f, 0.44f);
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

            if (maskedMesh != null)
            {
                if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertices.Length, out var loop))
                    return false;

                var eligible = new bool[loop.Count];
                for (var i = 0; i < loop.Count; i++)
                {
                    var index = loop[i];
                    eligible[i] = weights[index] > 0f && System.Array.Exists(masks,
                        mask => mask != null && mask.Length >= 3 && ContainsFaceMouthPoint(vertices[index], mask));
                }

                for (var i = 0; i < loop.Count; i++)
                {
                    if (!eligible[i])
                        continue;
                    var index = loop[i];
                    var contour = vertices[index];
                    if (!TryGetSoftContourSupport(contour, segments, SoftContourDepth, out var support) &&
                        !TryGetAutoSoftContourInteriorSupport(index, vertices, data.triangles, out support))
                        continue;
                    bool endpoint = !eligible[(i + loop.Count - 1) % loop.Count] ||
                        !eligible[(i + 1) % loop.Count];
                    maskedPairs.Add((ConvertPointToBaseDoc(data, contour), ConvertPointToBaseDoc(data, support), endpoint));
                }
                return maskedPairs.Count > 0;
            }

            if (SoftContourAutoMode)
            {
                softContourPairs.Clear();
                softContourPreviewAutoMode = true;
                softContourPreviewDepth = SoftContourDepth;
                softContourPreviewSettings = (SoftContourMaxEndAngle, SoftContourSplitAngle, SoftContourHipsMode);
                softContourAutoPointCount = 0;
                BuildAutoSoftContour(data, vertices, weights, segments, toView, draw);
                return false;
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
            softContourPreviewAutoMode = false;
            softContourPreviewDepth = SoftContourDepth;
            foreach (var point in points)
            {
                bool selected = selectedSoftContourPoints.Contains(point.Key);
                Handles.color = selected ? new Color(0.1f, 1f, 0.9f) : new Color(0.62f, 0.62f, 0.62f);
                Handles.DrawSolidDisc(point.Value, Vector3.forward, selected ? 5.5f : 3.75f);
            }
            var selectedPoints = new List<Vector2>(selectedSoftContourPoints.Count);
            foreach (var entry in documentPoints)
                if (selectedSoftContourPoints.Contains(entry.Key)) selectedPoints.Add(entry.Value);
            var softened = FindSoftContourChainEnds(selectedPoints, segments);
            for (var i = 0; i < selectedPoints.Count; i++)
            {
                var point = selectedPoints[i];
                if (!TryGetSoftContourSupport(point, segments, SoftContourDepth, out var support))
                    continue;
                softContourPairs.Add((ConvertPointToBaseDoc(data, point), ConvertPointToBaseDoc(data, support), softened.Contains(i)));
                Handles.color = new Color(1f, 0.24f, 0.28f);
                Handles.DrawSolidDisc(toView(support), Vector3.forward, 4.5f);
            }
        }
        finally
        {
            if (draw) Handles.color = previousColor;
            if (draw && !selectPoint) Handles.EndGUI();
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
            if (!draw || selectPoint) return;
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

    private void BuildAutoSoftContour(LayerVertexData data,
        Vector2[] vertices, float[] weights,
        IReadOnlyList<(Vector2 a, Vector2 b, Vector2 inward)> influenceBoundary,
        System.Func<Vector2, Vector2> toView, bool draw)
    {
        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertices.Length, out var boundary))
            return;

        var points = new Vector2[boundary.Count];
        var eligible = new bool[boundary.Count];
        for (var i = 0; i < boundary.Count; i++)
        {
            var index = boundary[i];
            points[i] = vertices[index];
            eligible[i] = SoftContourHipsMode
                ? weights[index] > 0f && !IsSoftContourHipsPointBlocked(points[i])
                : weights[index] >= 1f - SoftContourMinimumWeight;
        }

        var chains = new List<(int start, int end, float length)>();
        for (var i = 0; i < points.Length; i++)
        {
            var previous = (i + points.Length - 1) % points.Length;
            if (!eligible[i] || eligible[previous])
                continue;

            var end = i;
            var next = (end + 1) % points.Length;
            var length = 0f;
            while (eligible[next])
            {
                length += Vector2.Distance(points[end], points[next]);
                end = next;
                next = (end + 1) % points.Length;
            }

            if (end != i)
                chains.Add((i, end, length));
            else
                eligible[i] = false;
        }

        if (System.Array.TrueForAll(eligible, point => point))
        {
            var start = 0;
            for (var i = 1; i < points.Length; i++)
            {
                if (TurnAt(i) > TurnAt(start))
                    start = i;
            }
            chains.Add((start, (start + points.Length - 1) % points.Length, float.PositiveInfinity));
        }

        chains.Sort((left, right) =>
        {
            var byLength = right.length.CompareTo(left.length);
            return byLength != 0 ? byLength : left.start.CompareTo(right.start);
        });
        var segments = new List<(int start, int end)>();
        var splitSegments = new List<(int start, int end)>();
        for (var i = 0; i < Mathf.Min(2, chains.Count); i++)
        {
            var chain = chains[i];
            AddSoftContourSegments(points, chain.start, chain.end, SoftContourSplitAngle, splitSegments);
        }

        var trimmed = new HashSet<int>();
        var retained = new HashSet<int>();
        for (var i = 0; i < splitSegments.Count; i++)
        {
            var segment = splitSegments[i];
            var start = segment.start;
            var end = segment.end;
            if (i > 0 && splitSegments[i - 1].end == start && TurnAt(start) > SoftContourMaxEndAngle)
                start = (start + 1) % points.Length;
            if (start != end && i + 1 < splitSegments.Count && splitSegments[i + 1].start == end &&
                TurnAt(end) > SoftContourMaxEndAngle)
                end = (end + points.Length - 1) % points.Length;
            if (start != end)
                (start, end) = TrimSoftContourChainEnds(points, start, end, SoftContourMaxEndAngle);
            for (var point = segment.start; point != start; point = (point + 1) % points.Length)
                trimmed.Add(point);
            for (var point = segment.end; point != end; point = (point + points.Length - 1) % points.Length)
                trimmed.Add(point);
            if (start == end)
            {
                trimmed.Add(start);
                continue;
            }
            for (var point = start; point != end; point = (point + 1) % points.Length)
                retained.Add(point);
            retained.Add(end);
            segments.Add((start, end));
        }

        float TurnAt(int point)
        {
            var previous = (point + points.Length - 1) % points.Length;
            var next = (point + 1) % points.Length;
            return Vector2.Angle(points[point] - points[previous], points[next] - points[point]);
        }

        foreach (var point in trimmed)
        {
            if (!retained.Contains(point))
                eligible[point] = false;
        }

        if (draw) Handles.color = new Color(1f, 0.2f, 0.2f);
        foreach (var segment in segments)
        {
            for (var point = segment.start; point != segment.end; point = (point + 1) % points.Length)
                if (draw) Handles.DrawAAPolyLine(2f, toView(points[point]), toView(points[(point + 1) % points.Length]));
        }

        for (var i = 0; i < points.Length; i++)
        {
            if (draw && eligible[i])
                Handles.DrawSolidDisc(toView(points[i]), Vector3.forward, 4f);
        }

        if (draw) Handles.color = new Color(0.15f, 0.55f, 1f);
        var endpoints = new HashSet<int>();
        foreach (var segment in segments)
        {
            endpoints.Add(segment.start);
            endpoints.Add(segment.end);
        }
        foreach (var point in endpoints)
            if (draw) Handles.DrawSolidDisc(toView(points[point]), Vector3.forward, 5f);

        softContourAutoPointCount = retained.Count;
        if (draw) Handles.color = new Color(1f, 0.24f, 0.28f);
        foreach (var point in new SortedSet<int>(retained))
        {
            var contour = vertices[boundary[point]];
            if (!TryGetSoftContourSupport(contour, influenceBoundary, SoftContourDepth, out var support) &&
                !TryGetAutoSoftContourInteriorSupport(boundary[point], vertices, data.triangles, out support))
                continue;

            softContourPairs.Add((ConvertPointToBaseDoc(data, contour), ConvertPointToBaseDoc(data, support),
                endpoints.Contains(point)));
            if (draw) Handles.DrawSolidDisc(toView(support), Vector3.forward, 4.5f);
        }
    }

    private bool TryGetAutoSoftContourInteriorSupport(int index, Vector2[] vertices, int[] triangles, out Vector2 support)
    {
        support = Vector2.zero;
        var largestArea = 0f;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if ((index != a && index != b && index != c) ||
                (uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length)
                continue;

            var ab = vertices[b] - vertices[a];
            var ac = vertices[c] - vertices[a];
            var area = Mathf.Abs(ab.x * ac.y - ab.y * ac.x);
            if (area <= largestArea)
                continue;

            largestArea = area;
            var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            support = Vector2.Lerp(vertices[index], center, SoftContourDepth);
        }
        return largestArea > 0f;
    }

    internal static void AddSoftContourSegments(IReadOnlyList<Vector2> points, int start, int end,
        float maxAngle, List<(int start, int end)> segments)
    {
        int Next(int index) => (index + 1) % points.Count;
        if (start == end)
            return;

        var segmentStart = start;
        var previous = start;
        for (var point = Next(start); point != end; point = Next(point))
        {
            var angle = Vector2.Angle(points[point] - points[previous], points[Next(point)] - points[point]);
            if (angle > maxAngle)
            {
                segments.Add((segmentStart, point));
                segmentStart = point;
            }
            previous = point;
        }
        segments.Add((segmentStart, end));
    }

    internal static (int start, int end) TrimSoftContourChainEnds(
        IReadOnlyList<Vector2> points, int start, int end, float maxAngle)
    {
        int Next(int index) => (index + 1) % points.Count;
        int Previous(int index) => (index + points.Count - 1) % points.Count;

        while (Next(start) != end)
        {
            var afterStart = Next(start);
            var beforeEnd = Previous(end);
            var startAngle = Vector2.Angle(points[afterStart] - points[start],
                points[Next(afterStart)] - points[afterStart]);
            var endAngle = Vector2.Angle(points[beforeEnd] - points[Previous(beforeEnd)],
                points[end] - points[beforeEnd]);

            if (Mathf.Max(startAngle, endAngle) <= maxAngle)
                break;

            if (startAngle >= endAngle)
                start = afterStart;
            else
                end = beforeEnd;
        }

        return (start, end);
    }

    internal static HashSet<int> FindSoftContourChainEnds(IReadOnlyList<Vector2> points,
        IReadOnlyList<(Vector2 a, Vector2 b, Vector2 inward)> segments)
    {
        var ends = new HashSet<int>();
        if (points.Count < 3) return ends;

        var neighbors = new HashSet<int>[points.Count];
        for (var i = 0; i < neighbors.Length; i++) neighbors[i] = new HashSet<int>();
        foreach (var segment in segments)
        {
            var a = FindPoint(segment.a);
            var b = FindPoint(segment.b);
            if (a < 0 || b < 0 || a == b) continue;
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        for (var i = 0; i < neighbors.Length; i++)
        {
            if (neighbors[i].Count != 1 || ends.Contains(i)) continue;
            var previous = i;
            var current = i;
            foreach (var neighbor in neighbors[i]) current = neighbor;
            var count = 2;
            while (neighbors[current].Count == 2)
            {
                var next = previous;
                foreach (var neighbor in neighbors[current])
                    if (neighbor != previous) next = neighbor;
                previous = current;
                current = next;
                count++;
            }
            if (count > 2 && neighbors[current].Count == 1)
            {
                ends.Add(i);
                ends.Add(current);
            }
        }
        return ends;

        int FindPoint(Vector2 point)
        {
            for (var i = 0; i < points.Count; i++)
                if ((point - points[i]).sqrMagnitude < 1e-6f) return i;
            return -1;
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
