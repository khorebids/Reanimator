#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private readonly List<Vector2> softContourBellyContour = new();
    private readonly List<Vector2> softContourBellyStroke = new();
    private int softContourBellyControl;

    internal bool SoftContourDrawnBellyMode { get; set; }
    internal bool IsDrawingSoftContourBelly { get; set; }
    internal bool HasSoftContourBellyContour => softContourBellyContour.Count >= 3;
    internal bool IsSoftContourBellyStrokeActive => softContourBellyControl != 0 && GUIUtility.hotControl == softContourBellyControl;
    internal Vector2[] CaptureSoftContourBellyDraft() => softContourBellyContour.ToArray();
    internal bool HasSoftContourBellyBone =>
        selectedBoneIndex >= 0 && selectedBoneIndex < bones.Count && bones[selectedBoneIndex] != null;

    internal void SelectSoftContourBellyBone()
    {
        if (TryFindBoneIndexByName("belly", out var belly))
            selectedBoneIndex = belly;
    }

    internal void CancelSoftContourBellyStroke()
    {
        if (softContourBellyControl != 0 && GUIUtility.hotControl == softContourBellyControl)
            GUIUtility.hotControl = 0;
        softContourBellyControl = 0;
        IsDrawingSoftContourBelly = false;
        softContourBellyStroke.Clear();
    }

    internal void ClearSoftContourBellyContour()
    {
        CancelSoftContourBellyStroke();
        softContourBellyContour.Clear();
    }
    internal void DisableReanimateBelly()
    {RegisterUndo("No Belly");ClearSoftContourBellyContour();}

    internal bool HandleSoftContourBellyInput(ReanimatorPreviewContext context)
    {
        var evt = Event.current;
        if (!IsDrawingSoftContourBelly || evt == null || context.Coordinates == null)
            return false;

        if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
        {
            CancelSoftContourBellyStroke();
            evt.Use();
            return true;
        }

        if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt &&
            context.LocalRect.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
        {
            softContourBellyControl = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = softContourBellyControl;
            softContourBellyStroke.Clear();
            AddPoint();
        }
        else if (softContourBellyControl != 0 && GUIUtility.hotControl == softContourBellyControl &&
                 evt.type == EventType.MouseDrag)
        {
            AddPoint();
        }
        else if (softContourBellyControl != 0 && GUIUtility.hotControl == softContourBellyControl &&
                 evt.type == EventType.MouseUp && evt.button == 0)
        {
            AddPoint();
            var contour = BuildSmoothedClosedBoobV2Contour(softContourBellyStroke, 2, 32);
            if (contour.Count >= 3)
            {
                softContourBellyContour.Clear();
                softContourBellyContour.AddRange(contour);
            }
            CancelSoftContourBellyStroke();
        }
        else
            return false;

        evt.Use();
        return true;

        void AddPoint()
        {
            var point = context.Coordinates.ViewToDocument(evt.mousePosition);
            if (softContourBellyStroke.Count == 0 ||
                (point - softContourBellyStroke[softContourBellyStroke.Count - 1]).sqrMagnitude >= 0.25f)
                softContourBellyStroke.Add(point);
        }
    }

    internal void DrawSoftContourBelly(PsdCoordinateSpace coordinates)
    {
        if (coordinates == null)
            return;

        var color = new Color(0.12f, 1f, 0.86f);
        Handles.BeginGUI();
        var previous = Handles.color;
        DrawBoobV2Contour(coordinates.DocumentToView, softContourBellyContour, color, true);
        DrawBoobV2Contour(coordinates.DocumentToView, softContourBellyStroke, color, false);
        Handles.color = previous;
        Handles.EndGUI();
    }

    internal string ApplyReanimateBelly(LayerInfo layer, Vector2[] contour)
    {
        var previousLayer = selectedLayerSelectionKey;
        var previousBone = selectedBoneIndex;
        try
        {
            if (!TryFindBoneIndexByName("belly", out selectedBoneIndex))
                throw new System.InvalidOperationException("Bone 'belly' is required for the belly contour.");
            SelectLayer(layer);
            softContourBellyContour.Clear();
            softContourBellyContour.AddRange(contour);
            if (!ApplySoftContourBelly(out var message))
                throw new System.InvalidOperationException(message);
            return message;
        }
        finally
        {
            softContourBellyContour.Clear();
            softContourBellyContour.AddRange(contour);
            selectedLayerSelectionKey = previousLayer;
            selectedBoneIndex = previousBone;
        }
    }

    internal bool TryInheritReanimateBelly(LayerInfo target,Vector2[] contour,out string error)
    {
        error=null;
        var body=layerInfos.Find(l=>l!=null&&(NormalizeAutoWeightName(l.name)=="body"||NormalizeAutoWeightName(l.name)=="torso"));
        if(body==null){error="Body sprite is missing.";return false;}
        if(body==target)return true;
        if(contour==null||contour.Length<3){error="Belly contour is missing.";return false;}
        var region=MapFullBoobContourToBase(body,contour);
        // Clothing inherits the completed body surface only where they overlap.
        // Its sprite does not need to contain the body's complete belly outline.
        return TryInheritSurfaceRegion(target,new[]{body},p=>ContainsFaceMouthPoint(p,region),out error);
    }

    internal bool ApplySoftContourBelly(out string message)
    {
        message = "Select a belly bone and draw a closed contour.";
        if (!HasSoftContourBellyBone || !HasSoftContourBellyContour ||
            !TryGetVGenCenter(softContourBellyContour, out var drawnCenter))
            return false;

        if (!TryGetSelectedLayer(out var layer))
            TryFindBoobConstraintLayer(drawnCenter, out layer);
        if (layer == null || !TryGetLayerVertexData(layer, out var data) ||
            data?.vertices == null || data.triangles == null)
        {
            message = "Select a sprite with geometry.";
            return false;
        }

        var contour = MapFullBoobContourToBase(layer, softContourBellyContour);
        if (!TryGetVGenCenter(contour, out var center))
        {
            message = "The belly contour has no interior.";
            return false;
        }

        var originalVertexCount = data.vertices.Length;
        var staged = CloneProjectorLayerData(data);
        var centerIndex = InsertPoint(center);
        var outer = new HashSet<int>();
        var inner = new HashSet<int>();
        foreach (var point in contour)
        {
            var outerIndex = InsertPoint(point);
            var innerPoint = Vector2.Lerp(point, center, 0.2f);
            var innerIndex = IsStrictlyInsideFullBoobContour(innerPoint, contour) ? InsertPoint(innerPoint) : -1;
            if (outerIndex >= 0)
                outer.Add(outerIndex);
            if (innerIndex >= 0)
                inner.Add(innerIndex);
        }
        inner.ExceptWith(outer);
        inner.Remove(centerIndex);
        if (centerIndex < 0 || outer.Count < 3 || inner.Count < 3)
        {
            message = "Draw the belly contour over the sprite mesh.";
            return false;
        }

        RegisterUndo("Apply SC Belly");
        var parent = selectedBoneIndex;
        var originalBoneCount = bones.Count;
        var child = FindOrCreateVGenBone(parent, "belly_SC", center, true);
        if (!TryEnsureFullBoobSpriteBone(staged, parent, out var parentSpriteBone) ||
            !TryEnsureFullBoobSpriteBone(staged, child, out var childSpriteBone))
        {
            if (bones.Count > originalBoneCount)
            {
                bones.RemoveRange(originalBoneCount, bones.Count - originalBoneCount);
                RecomputeBoneWorldTransforms();
            }
            message = "Could not bind belly_SC to the sprite.";
            return false;
        }

        EnsureBoneWeightsArraySize(staged, staged.vertices.Length);
        foreach (var index in outer)
        {
            if (index >= originalVertexCount)
                staged.boneWeights[index] = BuildSingleBoneWeight(parentSpriteBone);
        }
        foreach (var index in inner)
            staged.boneWeights[index] = BuildLimbTwoBoneWeight(parentSpriteBone, 0.25f, childSpriteBone, 0.75f);
        staged.boneWeights[centerIndex] = BuildSingleBoneWeight(childSpriteBone);

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
        ClearSoftContourBellyContour();
        message = $"SC Belly: {bones[child].name}, added {staged.vertices.Length - originalVertexCount} vertices.";
        return true;

        int InsertPoint(Vector2 point)
        {
            for (var i = 0; i < staged.vertices.Length; i++)
            {
                if ((staged.vertices[i] - point).sqrMagnitude <= 0.0001f)
                    return i;
            }
            if (!TryFindContainingTriangle(staged.triangles, staged.vertices, point,
                    out _, out _, out _, out _, out _, out _, out _, out _))
                return -1;

            var index = staged.vertices.Length;
            return InsertAppliedVertexIntoLayerData(staged, point, true) &&
                System.Array.IndexOf(staged.triangles, index) >= 0 ? index : -1;
        }
    }
}
#endif
