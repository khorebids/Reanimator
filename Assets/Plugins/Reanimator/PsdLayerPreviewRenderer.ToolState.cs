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
    public bool CancelSelectedElbow()
    {
        var lineIndex = FindActiveElbowLineIndex();
        if (lineIndex < 0 || lineIndex >= elbowLines.Count)
            return false;

        RegisterUndo("Cancel Elbow");
        RemoveElbowLineAt(lineIndex);
        hasPendingChanges = true;
        return true;
    }

    public bool CancelSelectedLimb()
    {
        var lineIndex = FindActiveLimbLineIndex();
        if (lineIndex < 0 || lineIndex >= limbLines.Count)
            return false;

        RegisterUndo("Cancel Limb");
        RemoveLimbLineAt(lineIndex);
        hasPendingChanges = true;
        return true;
    }

    public bool CancelElbowTool()
    {
        var changed = false;
        if ((elbowLines != null && elbowLines.Count > 0) ||
            elbowIsCreating ||
            elbowIsDragging ||
            elbowControlId != 0 ||
            elbowDragMode != ElbowDragMode.None ||
            elbowSelectedLineIndex != -1)
        {
            RegisterUndo("Cancel Elbow Tool");
        }

        if (CancelAllElbowLines())
            changed = true;

        if (elbowIsCreating || elbowIsDragging || elbowControlId != 0 || elbowDragMode != ElbowDragMode.None)
        {
            EndElbowInteraction();
            changed = true;
        }

        elbowSelectedLineIndex = -1;
        if (changed)
            hasPendingChanges = true;
        return changed;
    }

    public bool CancelLimbsTool()
    {
        var changed = false;
        if ((limbLines != null && limbLines.Count > 0) ||
            limbIsCreating ||
            limbIsDragging ||
            limbControlId != 0 ||
            limbDragMode != ElbowDragMode.None ||
            limbSelectedLineIndex != -1)
        {
            RegisterUndo("Cancel Limb Tool");
        }

        if (CancelAllLimbLines())
            changed = true;

        if (limbIsCreating || limbIsDragging || limbControlId != 0 || limbDragMode != ElbowDragMode.None)
        {
            EndLimbInteraction();
            changed = true;
        }

        limbSelectedLineIndex = -1;
        if (changed)
            hasPendingChanges = true;
        return changed;
    }

    public bool HasBoobPoint()
    {
        return boobHasPoint;
    }

    public void ClearBoobPoint()
    {
        if (GUIUtility.hotControl == boobControlId && boobControlId != 0)
            GUIUtility.hotControl = 0;
        boobIsDragging = false;
        boobControlId = 0;
        boobHasPoint = false;
        boobCenterDoc = Vector2.zero;
        boobLayerStableKey = string.Empty;
    }

    public bool CancelBoobTool()
    {
        var changed = boobHasPoint || !string.IsNullOrEmpty(boobLayerStableKey) ||
                      boobV2HasTargetA || boobV2HasTargetB ||
                      boobV2ContourADoc.Count > 0 || boobV2ContourBDoc.Count > 0 ||
                      !string.IsNullOrEmpty(boobV2LayerStableKey);
        if (changed)
            RegisterUndo("Cancel Boob Tool");
        ClearBoobPoint();
        ClearBoobV2();
        return changed;
    }

    public string BoobV2SelectionDescription
    {
        get
        {
            switch (BoobV2Selection)
            {
                case 0: return "Selected: viewer-left nipple target. Click or drag it into place.";
                case 1: return "Selected: viewer-right nipple target. Click or drag it into place.";
                case 2: return "Selected: viewer-left breast contour. Hold LMB and trace it.";
                default: return "Selected: viewer-right breast contour. Hold LMB and trace it.";
            }
        }
    }

    public bool CanApplyBoobV2()
    {
        return boobV2HasTargetA && boobV2HasTargetB &&
               boobV2ContourADoc != null && boobV2ContourADoc.Count >= 3 &&
               boobV2ContourBDoc != null && boobV2ContourBDoc.Count >= 3;
    }

    public int BoobV2FirstContourPointCount => boobV2ContourADoc != null ? boobV2ContourADoc.Count : 0;
    public int BoobV2SecondContourPointCount => boobV2ContourBDoc != null ? boobV2ContourBDoc.Count : 0;
    public bool BoobV2HasLeftTarget => boobV2HasTargetA;
    public bool BoobV2HasRightTarget => boobV2HasTargetB;

    public void ClearBoobV2()
    {
        if (GUIUtility.hotControl == boobControlId && boobControlId != 0)
            GUIUtility.hotControl = 0;
        boobIsDragging = false;
        boobControlId = 0;
        boobV2ActiveTarget = -1;
        boobV2DrawingContour = -1;
        boobV2WorkingContourDoc.Clear();
        boobV2Selection = 0;
        boobV2HasTargetA = false;
        boobV2HasTargetB = false;
        boobV2TargetADoc = Vector2.zero;
        boobV2TargetBDoc = Vector2.zero;
        boobV2RawContourADoc.Clear();
        boobV2RawContourBDoc.Clear();
        boobV2ContourADoc.Clear();
        boobV2ContourBDoc.Clear();
        boobV2LayerStableKey = string.Empty;
    }

    public void ClearSelectedBoobV2()
    {
        if (boobV2DrawingContour >= 0 || boobV2ActiveTarget >= 0)
        {
            if (GUIUtility.hotControl == boobControlId && boobControlId != 0)
                GUIUtility.hotControl = 0;
            boobIsDragging = false;
            boobControlId = 0;
            boobV2ActiveTarget = -1;
            boobV2DrawingContour = -1;
            boobV2WorkingContourDoc.Clear();
        }

        RegisterUndo("Clear Selected Boob V2 Item");
        switch (BoobV2Selection)
        {
            case 0:
                boobV2HasTargetA = false;
                boobV2TargetADoc = Vector2.zero;
                break;
            case 1:
                boobV2HasTargetB = false;
                boobV2TargetBDoc = Vector2.zero;
                break;
            case 2:
                boobV2RawContourADoc.Clear();
                boobV2ContourADoc.Clear();
                break;
            default:
                boobV2RawContourBDoc.Clear();
                boobV2ContourBDoc.Clear();
                break;
        }

        if (!boobV2HasTargetA && !boobV2HasTargetB &&
            boobV2ContourADoc.Count == 0 && boobV2ContourBDoc.Count == 0)
            boobV2LayerStableKey = string.Empty;
    }

    public bool HasRadiusCullPoint()
    {
        return radiusCullHasPoint;
    }

    public bool HasJointPoint()
    {
        return radiusCullHasPoint;
    }

    public bool TryConsumeRadiusCullRadiusOverride(out float radius)
    {
        if (radiusCullHasRadiusOverride)
        {
            radius = Mathf.Max(0.01f, radiusCullRadiusOverride);
            radiusCullHasRadiusOverride = false;
            return true;
        }

        radius = 0f;
        return false;
    }

    public bool ConsumeLimbPreviewRepaintRequest()
    {
        var requested = limbPreviewNeedsRepaint;
        limbPreviewNeedsRepaint = false;
        return requested;
    }

    private void SetRadiusCullRadiusOverride(float radius)
    {
        radiusCullRadiusOverride = Mathf.Max(0.01f, radius);
        radiusCullHasRadiusOverride = true;
    }

    private void InvalidateRadiusCullPreviewCache(bool clearCache)
    {
        radiusCullPreviewRevision++;
        if (radiusCullPreviewRevision < 0)
            radiusCullPreviewRevision = 1;

        if (radiusCullPreviewCache == null)
            radiusCullPreviewCache = new RadiusCullPreviewCache();
        if (clearCache)
            radiusCullPreviewCache.Clear();

        InvalidateLimbPreviewCache(clearCache);
    }

    private void InvalidateLimbPreviewCache(bool clearCache)
    {
        if (limbPreviewCache == null)
            limbPreviewCache = new LimbPreviewCache();
        if (clearCache)
            limbPreviewCache.Clear();
        limbPreviewNeedsRepaint = false;
        limbNearestBoneMissingLogged = false;
    }

    private void SyncRadiusCullPointToActiveLimbLine(bool clearIfMissing)
    {
        var lineIndex = FindActiveLimbLineIndex();
        if (lineIndex < 0 || lineIndex >= limbLines.Count)
        {
            if (clearIfMissing)
                radiusCullHasPoint = false;
            return;
        }

        var line = limbLines[lineIndex];
        if (line == null)
        {
            if (clearIfMissing)
                radiusCullHasPoint = false;
            return;
        }

        if (!line.hasContourPoints)
            RefreshLimbLineContour(line);
        if (!line.hasContourPoints)
        {
            if (clearIfMissing)
                radiusCullHasPoint = false;
            return;
        }

        if (!TryGetLayerVertexDataForLimbLine(line, out var data) || data == null)
        {
            if (clearIfMissing)
                radiusCullHasPoint = false;
            return;
        }

        if (string.IsNullOrEmpty(line.layerSelectionKey) && data.layer != null)
            line.layerSelectionKey = BuildSelectionKey(data.layer);
        if (string.IsNullOrEmpty(line.layerStableKey) && data.layer != null)
            line.layerStableKey = BuildKey(data.layer.name, data.layer.layerId);

        selectedLayerSelectionKey = line.layerSelectionKey;
        radiusCullLayerStableKey = line.layerStableKey;

        var centerDrawDoc = GetLimbContourMidpoint(line);
        radiusCullCenterDoc = ConvertPointToBaseDoc(data, centerDrawDoc);
        radiusCullHasPoint = true;
    }

    public void ClearRadiusCullPoint()
    {
        if (GUIUtility.hotControl == radiusCullControlId && radiusCullControlId != 0)
            GUIUtility.hotControl = 0;
        radiusCullIsDragging = false;
        radiusCullHandleIsDragging = false;
        radiusCullControlId = 0;
        radiusCullHasRadiusOverride = false;
        radiusCullRadiusOverride = 0f;
        radiusCullHasPoint = false;
        radiusCullCenterDoc = Vector2.zero;
        radiusCullLayerStableKey = string.Empty;
        radiusCullHandleAngleRad = Mathf.PI * 0.25f;
        InvalidateRadiusCullPreviewCache(true);
    }

    public void ClearJointPoint()
    {
        if (GUIUtility.hotControl == jointControlId && jointControlId != 0)
            GUIUtility.hotControl = 0;
        jointIsDragging = false;
        jointStartHandleIsDragging = false;
        jointEndHandleIsDragging = false;
        jointMiddleHandleIsDragging = false;
        jointStartOffsetHandleIsDragging = false;
        jointEndOffsetHandleIsDragging = false;
        jointControlId = 0;
        jointStartAngleRad = -Mathf.PI * 0.16666667f;
        jointEndAngleRad = Mathf.PI * 0.16666667f;
        jointCenterLockEnabled = false;
        jointLockedCenterDrawDoc = Vector2.zero;
        jointNearestBoneIndex = -1;
        ClearRadiusCullPoint();
    }

    public bool CancelRadiusCullTool()
    {
        var changed =
            radiusCullHasPoint ||
            !string.IsNullOrEmpty(radiusCullLayerStableKey) ||
            radiusCullIsDragging ||
            radiusCullHandleIsDragging ||
            radiusCullControlId != 0 ||
            radiusCullHasRadiusOverride;
        if (changed)
            RegisterUndo("Cancel Radius Cull Tool");
        ClearRadiusCullPoint();
        return changed;
    }

    public bool CancelJointTool()
    {
        var changed =
            jointIsDragging ||
            jointStartHandleIsDragging ||
            jointEndHandleIsDragging ||
            jointMiddleHandleIsDragging ||
            jointStartOffsetHandleIsDragging ||
            jointEndOffsetHandleIsDragging ||
            jointControlId != 0 ||
            jointCenterLockEnabled ||
            jointNearestBoneIndex >= 0 ||
            radiusCullHasPoint ||
            !string.IsNullOrEmpty(radiusCullLayerStableKey) ||
            radiusCullIsDragging ||
            radiusCullHandleIsDragging ||
            radiusCullControlId != 0 ||
            radiusCullHasRadiusOverride;

        if (changed)
            RegisterUndo("Cancel Joint Tool");

        ClearJointPoint();
        return changed;
    }
}

#endif
