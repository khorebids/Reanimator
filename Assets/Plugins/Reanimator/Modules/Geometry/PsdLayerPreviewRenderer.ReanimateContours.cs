#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal sealed class ReanimateChestDraft
    {
        internal Vector2 LeftTarget, RightTarget;
        internal Vector2[] LeftContour, RightContour, LeftRaw, RightRaw;
    }

    internal Vector2[][] CaptureReanimateFaceDraft(LayerInfo layer)
    {
        if (!ReferenceEquals(layer, faceMouthLayer))
            return null;
        var result = new Vector2[faceMouthContours.Length][];
        for (var i = 0; i < result.Length; i++)
        {
            if (!HasFaceMouthContour(i) || !TryGetVGenCenter(faceMouthContours[i], out _))
                return null;
            result[i] = faceMouthContours[i].ToArray();
        }
        return result;
    }

    internal ReanimateChestDraft CaptureReanimateChestDraft()
    {
        if (!CanApplyBoobV2() ||
            !IsPointInsideFullBoobContour(boobV2TargetADoc, boobV2ContourADoc) ||
            !IsPointInsideFullBoobContour(boobV2TargetBDoc, boobV2ContourBDoc))
            return null;
        return new ReanimateChestDraft
        {
            LeftTarget = boobV2TargetADoc,
            RightTarget = boobV2TargetBDoc,
            LeftContour = boobV2ContourADoc.ToArray(),
            RightContour = boobV2ContourBDoc.ToArray(),
            LeftRaw = boobV2RawContourADoc.ToArray(),
            RightRaw = boobV2RawContourBDoc.ToArray()
        };
    }

    internal void RestoreReanimateFaceDraft(LayerInfo layer, Vector2[][] contours)
    {
        ClearFaceMouthContours();
        faceMouthLayer = layer;
        for (var i = 0; i < contours.Length; i++)
            faceMouthContours[i].AddRange(contours[i]);
    }

    internal void RestoreReanimateChestDraft(LayerInfo layer, ReanimateChestDraft draft)
    {
        ClearBoobV2();
        boobV2LayerStableKey = BuildKey(layer.name, layer.layerId);
        boobV2TargetADoc = draft.LeftTarget;
        boobV2TargetBDoc = draft.RightTarget;
        boobV2HasTargetA = boobV2HasTargetB = true;
        boobV2ContourADoc.AddRange(draft.LeftContour);
        boobV2ContourBDoc.AddRange(draft.RightContour);
        boobV2RawContourADoc.AddRange(draft.LeftRaw);
        boobV2RawContourBDoc.AddRange(draft.RightRaw);
    }

    internal bool TryApplyReanimateChest(LayerInfo layer, out string error)
    {
        if (!CanApplyBoobV2())
        {
            error = "Draw both chest contours and place their nipple points.";
            return false;
        }

        return TryPlaceFullBoobBones(layer, boobV2TargetADoc, boobV2TargetBDoc, out error) &&
            ApplyBoobV2(out _, out error, layer);
    }

    private bool TryPlaceFullBoobBones(LayerInfo layer, Vector2 firstPoint, Vector2 secondPoint, out string error)
    {
        error = null;
        if (!TryFindFullBoobSpineRendererBone(out var spine))
        {
            error = "Chest requires the spine bone.";
            return false;
        }
        TryFindFullBoobRendererBones(out var left, out var right);
        var sorted = firstPoint.x <= secondPoint.x;
        var leftPoint = MapNippleGuessToBase(layer, sorted ? firstPoint : secondPoint);
        var rightPoint = MapNippleGuessToBase(layer, sorted ? secondPoint : firstPoint);
        var nippleSpan = Vector2.Distance(leftPoint, rightPoint);
        var shoulderSpan = TryFindBoneIndexByName("l_arm", out var leftShoulder) &&
                           TryFindBoneIndexByName("r_arm", out var rightShoulder)
            ? Vector2.Distance(GetBoneBasePoint(bones[leftShoulder]), GetBoneBasePoint(bones[rightShoulder]))
            : nippleSpan * 2f;
        var nippleLength = ResolveAutoSkeletonNippleLength(shoulderSpan);
        var pivot = (leftPoint + rightPoint) * 0.5f + Vector2.up * Mathf.Max(3f, nippleSpan * 0.12f);

        RegisterUndo("Place chest bones");
        var root = FindOrCreateVGenBone(spine, "boob_root", pivot, true);
        SetChestBone(root, spine, pivot, Vector2.up, Mathf.Max(6f, nippleSpan * 0.16f));
        RecomputeBoneWorldTransforms();
        if (left < 0)
            left = FindOrCreateVGenBone(root, "l_nipple", leftPoint, true);
        if (right < 0)
            right = FindOrCreateVGenBone(root, "r_nipple", rightPoint, true);
        SetChestBone(left, root, leftPoint, leftPoint - pivot, nippleLength);
        SetChestBone(right, root, rightPoint, rightPoint - pivot, nippleLength);
        RecomputeBoneWorldTransforms();
        BuildBoneDrawOrder();
        MarkBoneStateChanged();
        return true;

        void SetChestBone(int index, int parent, Vector2 point, Vector2 direction, float length)
        {
            var bone = bones[index];
            var rotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
            bone.parentId = parent;
            bone.position = bone.initialPosition = ConvertBoneWorldPositionToLocal(parent, point);
            bone.rotation = bone.initialRotation = ConvertBoneWorldRotationToLocal(parent, rotation);
            bone.length = length;
        }
    }

    internal bool TryApplyReanimateLimbSc(LayerInfo layer, string rootBone, string[] lowerAliases,
        Vector2[][] masks, out bool applied, out string error)
    {
        applied = false;
        error = null;
        if (masks == null || !System.Array.Exists(masks, mask => mask != null && mask.Length >= 3) ||
            !TryGetLayerVertexData(layer, out var mesh) || mesh?.triangles == null)
            return true;

        if (TryFindBoneIndexByName(rootBone, out var upper) && !ApplySegment(upper, ref applied, out error))
            return false;
        foreach (var alias in lowerAliases)
            if (TryFindBoneIndexByName(alias, out var lower))
                return ApplySegment(lower, ref applied, out error);
        return true;

        bool ApplySegment(int bone, ref bool changed, out string message)
        {
            message = null;
            var segmentMesh=mesh;
            if(!string.Equals(bones[bone].name,rootBone,System.StringComparison.OrdinalIgnoreCase))
            {
                var lowerLayer=FindSplitArmLowerLayer(rootBone);
                if(lowerLayer!=null && TryGetLayerVertexData(lowerLayer,out var lowerMesh))segmentMesh=lowerMesh;
            }
            if(FindSpriteBoneIndexForRendererBone(segmentMesh,bone)<0)return true;
            var pairs = new List<(Vector2 contour, Vector2 support, bool soften)>();
            ProcessSoftContour(null, false, draw: false, maskedMesh: segmentMesh, maskedBone: bone,
                masks: masks, maskedPairs: pairs);
            if (pairs.Count == 0)
                return true;
            if (!ApplySoftContourPairs(segmentMesh, bone, pairs, out message,
                    inheritParentOrder: !string.Equals(rootBone, "hips", System.StringComparison.OrdinalIgnoreCase)))
                return false;
            changed = true;
            return true;
        }
    }
}
#endif
