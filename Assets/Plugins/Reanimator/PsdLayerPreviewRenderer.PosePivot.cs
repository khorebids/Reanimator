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
    public static Vector2 GetPresetNormalizedPosition(PivotPreset preset)
    {
        return ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(preset);
    }

    public bool TryGetRootBonePosition(out Vector2 rootPosition)
    {
        rootPosition = Vector2.zero;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (!string.Equals(bone.name, "root", StringComparison.OrdinalIgnoreCase))
                continue;

            rootPosition = bone.worldPosition;
            return true;
        }

        return false;
    }

    public bool TryGetRootBoneNormalizedPosition(out Vector2 normalized)
    {
        normalized = Vector2.zero;
        if (!TryGetRootBonePosition(out var rootPos))
            return false;

        var bounds = GetContentBoundsDoc();
        var w = Mathf.Max(1e-4f, bounds.width);
        var h = Mathf.Max(1e-4f, bounds.height);
        normalized = new Vector2((rootPos.x - bounds.xMin) / w, (rootPos.y - bounds.yMin) / h);
        return true;
    }

    public bool ApplyPivotPreset(PivotPreset preset, Vector2 customNormalized)
    {
        if (!TryGetPivotPointDoc(preset, customNormalized, out var pivotPoint))
            return false;

        var delta = -pivotPoint;
        if (delta.sqrMagnitude <= 1e-8f)
            return false;

        RegisterUndo("Apply Pivot Preset");
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            layer.position += delta;
            MarkLayerPositionStateDirty(layer);
        }

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (!bonesTreatAsWorldSpace && bone.parentId >= 0 && bone.parentId < bones.Count)
                continue;

            bone.position += delta;
            bones[i] = bone;
        }

        for (var i = 0; i < elbowLines.Count; i++)
        {
            var line = elbowLines[i];
            if (line == null)
                continue;
            line.startDoc += delta;
            line.endDoc += delta;
            line.contourPointA += delta;
            line.contourPointB += delta;
        }

        for (var i = 0; i < limbLines.Count; i++)
        {
            var line = limbLines[i];
            if (line == null)
                continue;
            line.startDoc += delta;
            line.endDoc += delta;
            line.contourPointA += delta;
            line.contourPointB += delta;
        }

        if (boobHasPoint)
            boobCenterDoc += delta;
        if (radiusCullHasPoint && !jointCenterLockEnabled)
            radiusCullCenterDoc += delta;

        var activeAssetPath = NormalizeAssetPath(cachedPath);
        for (var i = 0; i < appliedElbowVertices.Count; i++)
        {
            var point = appliedElbowVertices[i];
            if (point == null)
                continue;
            if (!IsAssetPathMatch(point.assetPath, activeAssetPath))
                continue;
            point.positionDoc += delta;
        }

        for (var i = 0; i < autoGeometryPatches.Count; i++)
        {
            var patch = autoGeometryPatches[i];
            if (patch == null || patch.verticesDoc == null)
                continue;
            if (!IsAssetPathMatch(patch.assetPath, activeAssetPath))
                continue;

            for (var v = 0; v < patch.verticesDoc.Length; v++)
                patch.verticesDoc[v] += delta;
        }

        RecomputeBoneWorldTransforms();

        InvalidateBonePose();
        bonePoseAtRest = true;
        hasPendingChanges = true;

        if (!string.IsNullOrEmpty(cachedPath))
            BuildVertices(cachedPath);
        if (!LayerExistsBySelectionKey(selectedLayerSelectionKey))
            selectedLayerSelectionKey = string.Empty;

        return true;
    }

    public bool RestorePose()
    {
        return RestorePoseInternal(registerUndo: true, clearSelection: true, markPending: true);
    }

    public bool RestorePoseForAssetTool()
    {
        return RestorePoseInternal(registerUndo: false, clearSelection: false, markPending: false);
    }

    private bool RestorePoseInternal(bool registerUndo, bool clearSelection, bool markPending)
    {
        if (bones == null || bones.Count == 0)
            return false;

        var changed = false;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone.initialRotation.Equals(default(Quaternion)))
            {
                bone.initialPosition = bone.position;
                bone.initialRotation = bone.rotation;
                bones[i] = bone;
                continue;
            }

            var targetRotation = bone.initialRotation;
            if ((bone.position - bone.initialPosition).sqrMagnitude <= 1e-8f &&
                Quaternion.Angle(bone.rotation, targetRotation) <= 1e-4f)
                continue;

            bone.position = bone.initialPosition;
            bone.rotation = targetRotation;
            bones[i] = bone;
            changed = true;
        }

        if (!changed)
            return false;

        if (registerUndo)
            RegisterUndo("Restore Pose");
        RecomputeBoneWorldTransforms();
        InvalidateBonePose();
        bonePoseAtRest = true;
        if (markPending)
            hasPendingChanges = true;
        if (clearSelection)
            selectedBoneIndex = -1;
        isDragging = false;
        isRotating = false;
        isResizingBone = false;

        if (!string.IsNullOrEmpty(cachedPath))
            BuildVertices(cachedPath);
        if (!LayerExistsBySelectionKey(selectedLayerSelectionKey))
            selectedLayerSelectionKey = string.Empty;

        return true;
    }
}

#endif
