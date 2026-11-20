#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal static readonly string[] FaceMouthContourLabels = { "Mouth", "Upper Lip", "Lower Lip", "Nose" };
    private const int NoseContourIndex = 3;
    private static readonly Color[] FaceMouthContourColors = { Color.cyan, new Color(1f, 0.45f, 0.65f), Color.yellow, Color.green };
    private readonly List<Vector2>[] faceMouthContours = { new(), new(), new(), new() };
    private readonly List<Vector2> faceMouthStroke = new();
    private LayerInfo faceMouthLayer;
    private int faceMouthControl;
    internal int FaceMouthDrawingContour { get; private set; } = -1;
    internal bool IsFaceMouthStrokeActive => faceMouthControl != 0 && GUIUtility.hotControl == faceMouthControl;
    [SerializeField] internal float NoseOuterOffset;
    [SerializeField] internal float NoseOuterOffsetY;

    internal bool HasFaceMouthContour(int index) => faceMouthContours[index].Count >= 3;
    internal bool HasFaceMouthContour(LayerInfo layer, int index) =>
        ReferenceEquals(faceMouthLayer, layer) && HasFaceMouthContour(index);

    internal void BeginFaceMouthContour(int index)
    {
        if (TryGetSelectedLayer(out var layer))
            BeginFaceMouthContour(layer, index);
    }

    internal void BeginFaceMouthContour(LayerInfo layer, int index)
    {
        if (!ReferenceEquals(faceMouthLayer, layer))
            ClearFaceMouthContours();
        faceMouthLayer = layer;
        FaceMouthDrawingContour = index;
    }

    internal void ClearFaceMouthContours()
    {
        CancelFaceMouthStroke();
        foreach (var contour in faceMouthContours)
            contour.Clear();
        faceMouthLayer = null;
    }
    internal bool HasAnyFaceMouthContours => System.Array.Exists(faceMouthContours,c=>c.Count>0);
    internal void DisableReanimateMouth()
    {RegisterUndo("No Mouth");ClearFaceMouthContours();}

    internal void CancelFaceMouthStroke()
    {
        if (faceMouthControl != 0 && GUIUtility.hotControl == faceMouthControl)
            GUIUtility.hotControl = 0;
        faceMouthControl = 0;
        FaceMouthDrawingContour = -1;
        faceMouthStroke.Clear();
    }

    internal bool HandleFaceMouthInput(ReanimatorPreviewContext context, out string error)
    {
        error = null;
        var evt = Event.current;
        if (FaceMouthDrawingContour < 0 || evt == null || context?.Coordinates == null)
            return false;

        if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            CancelFaceMouthStroke();
        else if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt &&
                 context.LocalRect.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
        {
            faceMouthControl = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = faceMouthControl;
            AddPoint();
        }
        else if (faceMouthControl != 0 && GUIUtility.hotControl == faceMouthControl &&
                 evt.type == EventType.MouseDrag)
            AddPoint();
        else if (faceMouthControl != 0 && GUIUtility.hotControl == faceMouthControl &&
                 evt.type == EventType.MouseUp && evt.button == 0)
        {
            AddPoint();
            var contour = BuildSmoothedClosedBoobV2Contour(faceMouthStroke, 2, 32);
            var target = faceMouthContours[FaceMouthDrawingContour];
            target.Clear();
            target.AddRange(contour);
            CancelFaceMouthStroke();
            error = GetFaceMouthContourError();
        }
        else
            return false;

        evt.Use();
        return true;

        void AddPoint()
        {
            var point = context.Coordinates.ViewToDocument(evt.mousePosition);
            if (faceMouthStroke.Count == 0 || (point - faceMouthStroke[faceMouthStroke.Count - 1]).sqrMagnitude >= 0.25f)
                faceMouthStroke.Add(point);
        }
    }

    internal void DrawFaceMouthContours(PsdCoordinateSpace coordinates)
    {
        if (TryGetSelectedLayer(out var layer))
            DrawFaceMouthContours(coordinates, layer);
    }

    internal void DrawFaceMouthContours(PsdCoordinateSpace coordinates, LayerInfo layer, bool showNoseOuter = true)
    {
        if (coordinates == null || Event.current.type != EventType.Repaint ||
            !ReferenceEquals(layer, faceMouthLayer))
            return;

        Handles.BeginGUI();
        var color = Handles.color;
        for (var i = 0; i < faceMouthContours.Length; i++)
            DrawBoobV2Contour(coordinates.DocumentToView, faceMouthContours[i], FaceMouthContourColors[i], true);
        if (showNoseOuter && HasFaceMouthContour(NoseContourIndex) && HasFaceMouthContour(0) &&
            TryPrepareNoseOuterContour(faceMouthContours[NoseContourIndex], faceMouthContours[0], out _, out var noseOuter,faceMouthContours[1],faceMouthContours[2]))
            DrawBoobV2Contour(coordinates.DocumentToView, noseOuter, new Color(0.6f, 1f, 0.6f), true);
        if (FaceMouthDrawingContour >= 0)
            DrawBoobV2Contour(coordinates.DocumentToView, faceMouthStroke, FaceMouthContourColors[FaceMouthDrawingContour], false);
        Handles.color = color;
        Handles.EndGUI();
    }

    internal string GetFaceMouthContourError()
    {
        if (!HasFaceMouthContour(0))
            return null;
        for (var i = 1; i < NoseContourIndex; i++)
        {
            if (HasFaceMouthContour(i) && !IsFaceMouthContourContained(faceMouthContours[i], faceMouthContours[0]))
                return FaceMouthContourLabels[i] + " extends outside the mouth contour.";
        }
        return null;
    }

    internal static bool IsFaceMouthContourContained(IReadOnlyList<Vector2> lip, IReadOnlyList<Vector2> mouth)
    {
        // Splitting at intersections tests the entire edge, including excursions across a concave mouth boundary.
        var split = SplitFullBoobContour(lip, mouth);
        for (var i = 0; i < split.Count; i++)
        {
            var point = split[i];
            var midpoint = (point + split[(i + 1) % split.Count]) * 0.5f;
            if (!ContainsFaceMouthPoint(point, mouth) || !ContainsFaceMouthPoint(midpoint, mouth))
                return false;
        }
        return true;
    }

    private static bool ContainsFaceMouthPoint(Vector2 point, IReadOnlyList<Vector2> contour) =>
        IsPointInsideFullBoobContour(point, contour) || DistanceToFullBoobContour(point, contour) <= 0.001f;

    internal string GetFaceMouthApplyError()
    {
        if (!TryGetSelectedLayer(out var layer) || !ReferenceEquals(layer, faceMouthLayer))
            return "Select the face sprite and draw the mouth and both lips.";
        var contourError = GetFaceMouthContourError();
        if (contourError != null)
            return contourError;
        for (var i = 0; i < NoseContourIndex; i++)
        {
            if (!HasFaceMouthContour(i) || !TryGetVGenCenter(faceMouthContours[i], out _))
                return "Draw a closed " + FaceMouthContourLabels[i].ToLowerInvariant() + " contour.";
        }
        if (!TryFindBoneIndexByName("head", out var head))
            return "Bone 'head' is required.";
        if (bones[head].depth > int.MaxValue - 4)
            return "Lower the head bone order to leave room for the face bones above it.";
        if (!TryGetLayerVertexData(layer, out var data) || data?.vertices == null || data.triangles == null)
            return "The selected sprite needs geometry.";
        if (HasFaceMouthContour(NoseContourIndex) &&
            !TryPrepareNoseOuterContour(faceMouthContours[NoseContourIndex], faceMouthContours[0], out _, out _,faceMouthContours[1],faceMouthContours[2]))
            return "The nose contour must be separate from both lips and define a valid outer support.";
        return null;
    }

    internal bool TryApplyFaceMouth(float lowerLipMaxHeadWeight, float upperLipMaxHeadWeight, out string message)
    {
        message = GetFaceMouthApplyError();
        if (message != null)
            return false;

        var layer = faceMouthLayer;
        TryGetLayerVertexData(layer, out var data);
        TryFindBoneIndexByName("head", out var head);
        var staged = CloneProjectorLayerData(data);
        var contours = new Vector2[3][];
        var centers = new Vector2[3];
        var centerIndices = new int[3];
        var contourIndices = new int[3][];
        for (var i = 0; i < contours.Length; i++)
            contours[i] = MapFullBoobContourToBase(layer, faceMouthContours[i]);
        if (!IsFaceMouthContourContained(contours[1], contours[0]) ||
            !IsFaceMouthContourContained(contours[2], contours[0]))
        {
            message = "A lip extends outside the mouth contour in the bind pose.";
            return false;
        }

        var removeMask = new bool[staged.vertices.Length];
        for (var i = 0; i < removeMask.Length; i++)
            removeMask[i] = IsStrictlyInsideFullBoobContour(staged.vertices[i], contours[0]);
        if (!TryRemoveVerticesByMask(staged, removeMask))
        {
            message = "Could not rebuild the mesh after removing vertices inside the mouth.";
            return false;
        }

        for (var i = 0; i < contours.Length; i++)
        {
            if (!TryGetVGenCenter(contours[i], out centers[i]) || (centerIndices[i] = InsertPoint(centers[i])) < 0)
            {
                message = "Draw all three contours over the face mesh.";
                return false;
            }
            contourIndices[i] = new int[contours[i].Length];
            for (var j = 0; j < contours[i].Length; j++)
            {
                contourIndices[i][j] = InsertPoint(contours[i][j]);
                if (contourIndices[i][j] >= 0)
                    continue;
                message = "Draw all three contours over the face mesh.";
                return false;
            }
        }
        var noseCenter = Vector2.zero;
        var noseCenterIndex = -1;
        var nosePointIndices = new List<int>();
        var noseOuterIndices = new List<int>();
        if (HasFaceMouthContour(NoseContourIndex))
        {
            var noseContour = MapFullBoobContourToBase(layer, faceMouthContours[NoseContourIndex]);
            if (!TryPrepareNoseOuterContour(noseContour, contours[0], out noseCenter, out var noseOuter,contours[1],contours[2]))
            {
                message = "The nose outer support could not be built in the bind pose.";
                return false;
            }
            noseCenterIndex = InsertPoint(noseCenter);
            foreach (var point in noseContour)
                nosePointIndices.Add(InsertPoint(point));
            foreach (var point in noseOuter)
                noseOuterIndices.Add(InsertPoint(point));
            if (noseCenterIndex < 0 || nosePointIndices.Contains(-1) || noseOuterIndices.Contains(-1))
            {
                message = "Draw the nose so its outer contour fits on the face mesh.";
                return false;
            }
        }

        // Sequential point insertion preserves the surface but leaves long, thin
        // fans around the dense lip rings. Balance connectivity after all points
        // are present; edge flips preserve positions, UVs and the exterior.
        OptimizeLayerTriangleQuality(staged);

        RegisterUndo("Apply Face Mouth");
        var originalBoneCount = bones.Count;
        var lipLength = Mathf.Max(0.5f, bones[head].length * 0.4f) * 0.5f;
        var lowerPivot = centers[2];
        var upperPivot = centers[1];
        var mouthPivot = centers[0];
        var mouth = FindOrCreateVGenBone(head, "mouth", mouthPivot, true);
        var lower = FindLip("lower_lip", lowerPivot);
        var upper = FindLip("upper_lip", upperPivot);
        var cornersPivot = lowerPivot + Vector2.down * Mathf.Max(Vector2.Distance(upperPivot, lowerPivot), lipLength * 0.5f);
        var corners = FindLip("lip_corners", cornersPivot);
        var nose = noseCenterIndex >= 0 ? FindOrCreateVGenBone(head, "nose", noseCenter, true) : -1;
        var noseSlot = -1;
        if (!TryEnsureRendererBindPoseSkinning(staged) ||
            !TryEnsureFullBoobSpriteBone(staged, head, out var headSlot) ||
            !TryEnsureFullBoobSpriteBone(staged, mouth, out var mouthSlot) ||
            !TryEnsureFullBoobSpriteBone(staged, lower, out var lowerSlot) ||
            !TryEnsureFullBoobSpriteBone(staged, upper, out var upperSlot) ||
            !TryEnsureFullBoobSpriteBone(staged, corners, out var cornersSlot) ||
            (nose >= 0 && !TryEnsureFullBoobSpriteBone(staged, nose, out noseSlot)))
        {
            bones.RemoveRange(originalBoneCount, bones.Count - originalBoneCount);
            RecomputeBoneWorldTransforms();
            message = "Could not bind the mouth and lip bones to the face sprite.";
            return false;
        }

        TryBuildBindInverseMatrixForRendererBone(null, head, out var headBindInverse);
        UpdateMouthBone(mouth, head, mouthPivot, headBindInverse, lipLength * 2f);
        var mouthRotation = Quaternion.AngleAxis(90f, Vector3.forward);
        if (bonesTreatAsWorldSpace)
            mouthRotation = headBindInverse.inverse.rotation * mouthRotation;
        SetMouthBoneRotation(mouth, mouthRotation);
        TryBuildBindInverseMatrixForRendererBone(null, mouth, out var mouthBindInverse);
        UpdateMouthBone(lower, mouth, lowerPivot, mouthBindInverse, lipLength);
        UpdateMouthBone(upper, mouth, upperPivot, mouthBindInverse, lipLength);
        UpdateMouthBone(corners, mouth, cornersPivot, mouthBindInverse, lipLength);
        var lipRotation = bonesTreatAsWorldSpace ? mouthRotation : Quaternion.identity;
        SetMouthBoneRotation(lower, lipRotation);
        SetMouthBoneRotation(upper, lipRotation);
        SetMouthBoneRotation(corners, lipRotation);
        if (nose >= 0)
            UpdateMouthBone(nose, head, noseCenter, headBindInverse, lipLength);
        RecomputeBoneWorldTransforms();
        if (staged.bindBoneInverseMatrices == null ||
            staged.bindBoneInverseMatrices.Length != staged.spriteBoneToRendererBone.Length)
            TryBuildRendererBindMatrices(staged.spriteBoneToRendererBone, out staged.bindBoneInverseMatrices);
        TryBuildBindInverseMatrixForRendererBone(staged, mouth, out staged.bindBoneInverseMatrices[mouthSlot]);
        TryBuildBindInverseMatrixForRendererBone(staged, lower, out staged.bindBoneInverseMatrices[lowerSlot]);
        TryBuildBindInverseMatrixForRendererBone(staged, upper, out staged.bindBoneInverseMatrices[upperSlot]);
        TryBuildBindInverseMatrixForRendererBone(staged, corners, out staged.bindBoneInverseMatrices[cornersSlot]);
        if (nose >= 0)
            TryBuildBindInverseMatrixForRendererBone(staged, nose, out staged.bindBoneInverseMatrices[noseSlot]);

        var faceOrder = bones[head].depth;
        bones[lower].depth = faceOrder + 1;
        bones[upper].depth = faceOrder + 2;
        bones[corners].depth = faceOrder + 3;
        bones[mouth].depth = faceOrder + 4;
        var noseOrderBone = nose;
        if (noseOrderBone < 0 && TryFindBoneIndexByName("nose", out var existingNose) &&
            bones[existingNose].parentId == head)
            noseOrderBone = existingNose;
        if (noseOrderBone >= 0)
            bones[noseOrderBone].depth = faceOrder + 5;
        EnsureBoneWeightsArraySize(staged, staged.vertices.Length);
        var mouthBoundary = new HashSet<int>(contourIndices[0]);
        var upperBoundary = new HashSet<int>(contourIndices[1]);
        var lowerBoundary = new HashSet<int>(contourIndices[2]);
        var lowerLipVertices = new List<int>();
        var upperLipVertices = new List<int>();
        for (var i = 0; i < staged.vertices.Length; i++)
        {
            var point = staged.vertices[i];
            var previous = staged.boneWeights[i];
            if (i == centerIndices[0] || mouthBoundary.Contains(i) || DistanceToFullBoobContour(point, contours[0]) <= 0.001f)
                staged.boneWeights[i] = BuildSingleBoneWeight(mouthSlot);
            else if (upperBoundary.Contains(i) || ContainsFaceMouthPoint(point, contours[1]))
                upperLipVertices.Add(i);
            else if (lowerBoundary.Contains(i) || ContainsFaceMouthPoint(point, contours[2]))
                lowerLipVertices.Add(i);
            else if (ContainsFaceMouthPoint(point, contours[0]) ||
                     (previous.weight0 == 1f &&
                      (previous.boneIndex0 == mouthSlot || previous.boneIndex0 == upperSlot || previous.boneIndex0 == lowerSlot || previous.boneIndex0 == cornersSlot)) ||
                     (previous.weight0 > 0f && previous.weight1 > 0f && previous.weight2 == 0f &&
                      ((IsLipSlot(previous.boneIndex0) && previous.boneIndex1 == headSlot) ||
                       (previous.boneIndex0 == headSlot && IsLipSlot(previous.boneIndex1)))))
                staged.boneWeights[i] = BuildSingleBoneWeight(headSlot);
        }
        ApplyLipWeights(lowerLipVertices, centers[2], lowerSlot, lowerLipMaxHeadWeight);
        ApplyLipWeights(upperLipVertices, centers[1], upperSlot, upperLipMaxHeadWeight);
        if (nose >= 0)
        {
            foreach (var index in noseOuterIndices)
            {
                var touchesMouth = DistanceToFullBoobContour(staged.vertices[index], contours[0]) <= 0.002f;
                staged.boneWeights[index] = BuildSingleBoneWeight(touchesMouth ? mouthSlot : headSlot);
            }
            foreach (var index in nosePointIndices)
                staged.boneWeights[index] = BuildLimbTwoBoneWeight(headSlot, 0.5f, noseSlot, 0.5f);
            staged.boneWeights[noseCenterIndex] = BuildSingleBoneWeight(noseSlot);
        }

        // Rebuild the original mouth/lip assignments first, including the mouth
        // center and boundary. Only the head share inside the mouth becomes corners.
        var noseProtected=new HashSet<int>(noseOuterIndices);
        noseProtected.UnionWith(nosePointIndices);
        if(noseCenterIndex>=0)noseProtected.Add(noseCenterIndex);
        for (var i = 0; i < staged.vertices.Length; i++)
            if (!noseProtected.Contains(i) && !mouthBoundary.Contains(i) && DistanceToFullBoobContour(staged.vertices[i], contours[0]) > 0.001f &&
                ContainsFaceMouthPoint(staged.vertices[i], contours[0]))
                staged.boneWeights[i] = RemapMouthInteriorHeadWeight(staged.boneWeights[i], headSlot, cornersSlot,
                    staged.spriteBoneToRendererBone.Length);

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
        MarkEditGeometryDataChanged(data, BuildSelectionKey(layer), BuildKey(layer.name, layer.layerId));
        RefreshFaceMouthBindings(data, mouth, lower, upper, corners);
        message = "Mouth rig applied.";
        return true;

        bool IsLipSlot(int slot) => slot == lowerSlot || slot == upperSlot;

        void ApplyLipWeights(List<int> indices, Vector2 center, int lipSlot, float maxHeadWeight)
        {
            var maxDistance = 0f;
            foreach (var index in indices)
                maxDistance = Mathf.Max(maxDistance, Vector2.Distance(staged.vertices[index], center));
            maxHeadWeight = Mathf.Clamp01(maxHeadWeight);
            foreach (var index in indices)
            {
                var distance = Vector2.Distance(staged.vertices[index], center);
                var headWeight = maxDistance > 0f ? maxHeadWeight * (distance / maxDistance) : 0f;
                staged.boneWeights[index] = BuildLimbTwoBoneWeight(headSlot, headWeight, lipSlot, 1f - headWeight);
            }
        }

        int FindLip(string name, Vector2 pivot)
        {
            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone != null && (bone.parentId == head || bone.parentId == mouth) &&
                    string.Equals(bone.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return FindOrCreateVGenBone(mouth, name, pivot, true);
        }

        void UpdateMouthBone(int index, int parent, Vector2 pivot, Matrix4x4 parentBindInverse, float length)
        {
            var bone = bones[index];
            var local = bonesTreatAsWorldSpace ? pivot
                : rotateLocalBoneOffsetsByParent ? (Vector2)parentBindInverse.MultiplyPoint3x4(pivot)
                : pivot - (Vector2)parentBindInverse.inverse.GetColumn(3);
            bone.position = bone.parentId == parent ? local + (bone.position - bone.initialPosition) : local;
            bone.parentId = parent;
            bone.initialPosition = local;
            bone.length = length;
        }

        void SetMouthBoneRotation(int index, Quaternion rotation)
        {
            var bone = bones[index];
            var poseOffset = Quaternion.Inverse(bone.initialRotation) * bone.rotation;
            bone.initialRotation = rotation;
            bone.rotation = rotation * poseOffset;
        }

        int InsertPoint(Vector2 point)
        {
            for (var i = 0; i < staged.vertices.Length; i++)
                if ((staged.vertices[i] - point).sqrMagnitude <= 0.000001f)
                    return i;
            if (!TryFindContainingTriangle(staged.triangles, staged.vertices, point,
                    out _, out _, out _, out _, out _, out _, out _, out _))
                return -1;
            return TryInsertElbowVertexExact(staged,point,out var index)?index:-1;
        }
    }

    private bool TryPrepareNoseOuterContour(
        IReadOnlyList<Vector2> nose, IReadOnlyList<Vector2> mouth, out Vector2 center, out Vector2[] outer,
        IReadOnlyList<Vector2> upperLip=null,IReadOnlyList<Vector2> lowerLip=null)
    {
        outer = null;
        if (!TryGetVGenCenter(nose, out center))return false;
        // The mouth support outline may include the nose. The actual lips are
        // the exclusion regions; an enclosing support outline is not a lip.
        if(ContainsFaceMouthPoint(center,mouth)&&upperLip?.Count>=3&&lowerLip?.Count>=3)
        {
            foreach(var point in nose)if(ContainsFaceMouthPoint(point,upperLip)||ContainsFaceMouthPoint(point,lowerLip))return false;
            mouth=DistanceToFullBoobContour(center,upperLip)<=DistanceToFullBoobContour(center,lowerLip)?upperLip:lowerLip;
        }
        if(ContainsFaceMouthPoint(center,mouth)||!TryBuildNoseOuterContour(nose,mouth,out outer))return false;

        OffsetNoseOuterContour(outer, new Vector2(NoseOuterOffset, NoseOuterOffsetY));
        PullNoseOuterContourAwayFromMouth(outer, mouth, center);
        if(upperLip?.Count>=3)PullNoseOuterContourAwayFromMouth(outer,upperLip,center);
        if(lowerLip?.Count>=3)PullNoseOuterContourAwayFromMouth(outer,lowerLip,center);
        outer = SplitFullBoobContour(outer, mouth).ToArray();
        return true;
    }

    private static void PullNoseOuterContourAwayFromMouth(
        Vector2[] outer, IReadOnlyList<Vector2> mouth, Vector2 center)
    {
        if (!OverlapsMouth(outer))
            return;

        var candidate = new Vector2[outer.Length];
        var safeScale = 0f;
        var overlapScale = 1f;
        for (var step = 0; step < 24; step++)
        {
            var scale = (safeScale + overlapScale) * 0.5f;
            for (var i = 0; i < outer.Length; i++)
                candidate[i] = Vector2.Lerp(center, outer[i], scale);
            if (OverlapsMouth(candidate))
                overlapScale = scale;
            else
                safeScale = scale;
        }
        for (var i = 0; i < outer.Length; i++)
            outer[i] = Vector2.Lerp(center, outer[i], safeScale);

        bool OverlapsMouth(IReadOnlyList<Vector2> contour) =>
            HasInteriorEdge(contour, mouth) || HasInteriorEdge(mouth, contour);

        bool HasInteriorEdge(IReadOnlyList<Vector2> contour, IReadOnlyList<Vector2> other)
        {
            var split = SplitFullBoobContour(contour, other);
            for (var i = 0; i < split.Count; i++)
            {
                var start = split[i];
                var end = split[(i + 1) % split.Count];
                if (IsStrictlyInsideFullBoobContour(start, other) ||
                    IsStrictlyInsideFullBoobContour((start + end) * 0.5f, other))
                    return true;
            }
            return false;
        }
    }

    private static void OffsetNoseOuterContour(Vector2[] contour, Vector2 offset)
    {
        var min = contour[0];
        var max = min;
        foreach (var point in contour)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        var normalizedOffset = new Vector2(Mathf.Clamp(offset.x, -1f, 1f), Mathf.Clamp(offset.y, -1f, 1f));
        var shift = Vector2.Scale(normalizedOffset, (max - min) * 0.5f);
        for (var i = 0; i < contour.Length; i++)
            contour[i] += shift;
    }

    internal static bool TryBuildNoseOuterContour(IReadOnlyList<Vector2> nose, IReadOnlyList<Vector2> mouth, out Vector2[] outer)
    {
        outer = null;
        var area = 0f;
        var rayLength = 1f;
        for (var i = 0; i < nose.Count; i++)
        {
            area += FullBoobCross(nose[i] - nose[0], nose[(i + 1) % nose.Count] - nose[0]);
            foreach (var point in mouth)
                rayLength = Mathf.Max(rayLength, Vector2.Distance(nose[i], point) + 1f);
            if (ContainsFaceMouthPoint(nose[i], mouth))
                return false;
        }
        if (Mathf.Abs(area) <= 0.0001f)
            return false;

        var normals = new Vector2[nose.Count];
        var offset = float.PositiveInfinity;
        for (var i = 0; i < nose.Count; i++)
        {
            var point = nose[i];
            var incoming = (point - nose[(i + nose.Count - 1) % nose.Count]).normalized;
            var outgoing = (nose[(i + 1) % nose.Count] - point).normalized;
            var tangent = incoming + outgoing;
            var normal = new Vector2(tangent.y, -tangent.x).normalized * Mathf.Sign(area);
            normals[i] = normal;
            var end = point + normal * rayLength;
            for (var j = 0; j < mouth.Count; j++)
            {
                if (TryGetSegmentIntersection(point, end, mouth[j], mouth[(j + 1) % mouth.Count], out var t, out _) && t > 0f)
                    offset = Mathf.Min(offset, t * rayLength);
            }
        }
        if (float.IsPositiveInfinity(offset))
        {
            foreach(var point in nose)offset=Mathf.Min(offset,DistanceToFullBoobContour(point,mouth));
            if(!float.IsFinite(offset)||offset<=.001f)return false;
        }

        outer = new Vector2[nose.Count];
        for (var i = 0; i < nose.Count; i++)
            outer[i] = nose[i] + normals[i] * offset;
        return true;
    }

    private static BoneWeight RemapMouthInteriorHeadWeight(BoneWeight weight, int headSlot, int cornersSlot, int boneCount)
    {
        if (GetSpriteBoneWeight(weight, headSlot) <= 0f)
            return weight;
        var indices = new[] { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
        var values = new[] { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
        for (var i = 0; i < indices.Length; i++)
            if (indices[i] == headSlot)
                indices[i] = cornersSlot;
        return ReanimatorWeightSliderAlgorithms.BuildBoneWeightFromExplicitSlots(indices, values, boneCount, true);
    }

    private void RefreshFaceMouthBindings(LayerVertexData editedMesh, int mouth, int lower, int upper, int corners)
    {
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var data = pair.Value;
            if (data == null || data == editedMesh || data.layer == null)
                continue;
            var changed = false;
            if (mouth >= 0 && data.vertices?.Length > 0 &&
                string.Equals(data.layer.name, "mouth", System.StringComparison.OrdinalIgnoreCase))
            {
                data.spriteBoneToRendererBone = new[] { mouth };
                data.usesSpriteBindPoses = false;
                data.bindBoneInverseMatrices = new Matrix4x4[1];
                data.boneWeights = new BoneWeight[data.vertices.Length];
                for (var i = 0; i < data.boneWeights.Length; i++)
                    data.boneWeights[i] = BuildSingleBoneWeight(0);
                changed = true;
            }

            var map = data.spriteBoneToRendererBone;
            if (map == null)
                continue;
            for (var slot = 0; slot < map.Length; slot++)
            {
                var bone = map[slot];
                if (bone < 0 || (bone != mouth && bone != lower && bone != upper && bone != corners))
                    continue;
                if (!TryBuildBindInverseMatrixForRendererBone(data, bone, out var bind))
                    continue;
                if (data.bindBoneInverseMatrices == null || data.bindBoneInverseMatrices.Length != map.Length)
                {
                    TryBuildRendererBindMatrices(map, out data.bindBoneInverseMatrices);
                    data.usesSpriteBindPoses = false;
                    changed = true;
                    break;
                }
                if (data.bindBoneInverseMatrices[slot] == bind)
                    continue;
                data.bindBoneInverseMatrices[slot] = bind;
                changed = true;
            }
            if (!changed)
                continue;
            MarkSkinningWeightsChanged(data);
            MarkEditGeometryDataChanged(data, pair.Key, BuildKey(data.layer.name, data.layer.layerId));
        }
    }
}
#endif
