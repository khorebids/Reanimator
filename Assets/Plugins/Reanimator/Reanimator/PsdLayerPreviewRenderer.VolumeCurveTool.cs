#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const float DefaultVolumeCurveMaxInfluence = 0.25f;

    public IReadOnlyList<VolumeCurve> VolumeCurves
    {
        get
        {
            EnsureVolumeCurveState();
            return volumeCurves;
        }
    }

    public int SelectedVolumeCurveIndex
    {
        get
        {
            EnsureVolumeCurveState();
            return selectedVolumeCurveIndex;
        }
    }

    public int SelectedVolumeCurvePointIndex
    {
        get
        {
            EnsureVolumeCurveState();
            return selectedVolumeCurvePointIndex;
        }
    }

    public void CancelVolumeCurveTool()
    {
        if (!volumeCurveIsDragging && volumeCurveControlId == 0)
            return;

        if (GUIUtility.hotControl == volumeCurveControlId && volumeCurveControlId != 0)
            GUIUtility.hotControl = 0;

        volumeCurveIsDragging = false;
        volumeCurveControlId = 0;
        volumeCurvePointMoveUndoRegistered = false;
    }

    public void BeginVolumeCurvePointDrag(int controlId)
    {
        volumeCurveIsDragging = true;
        volumeCurveControlId = controlId;
        volumeCurvePointMoveUndoRegistered = false;
    }

    public void EndVolumeCurvePointDrag()
    {
        volumeCurveIsDragging = false;
        volumeCurveControlId = 0;
        volumeCurvePointMoveUndoRegistered = false;
    }

    public bool CreateVolumeCurve()
    {
        EnsureVolumeCurveState();
        RegisterUndo("Create Volume Curve");

        var curve = new VolumeCurve
        {
            id = Guid.NewGuid().ToString("N"),
            name = BuildDefaultVolumeCurveName(),
            closed = false,
            points = new List<VolumeCurvePoint>()
        };

        volumeCurves.Add(curve);
        selectedVolumeCurveIndex = volumeCurves.Count - 1;
        selectedVolumeCurvePointIndex = -1;
        return true;
    }

    public bool ReplaceVolumeCurves(IReadOnlyList<VolumeCurve> curves)
    {
        EnsureVolumeCurveState();
        RegisterUndo("Load Volume Curve Preset");
        volumeCurves.Clear();
        if (curves != null)
        {
            for (var i = 0; i < curves.Count; i++)
            {
                var clone = CloneVolumeCurve(curves[i]);
                if (clone != null)
                    volumeCurves.Add(clone);
            }
        }

        selectedVolumeCurveIndex = volumeCurves.Count > 0 ? 0 : -1;
        selectedVolumeCurvePointIndex = -1;
        EnsureVolumeCurveState();
        return true;
    }

    public List<VolumeCurve> CloneVolumeCurvesForPreset()
    {
        EnsureVolumeCurveState();
        var result = new List<VolumeCurve>(volumeCurves.Count);
        for (var i = 0; i < volumeCurves.Count; i++)
        {
            var clone = CloneVolumeCurve(volumeCurves[i]);
            if (clone != null)
                result.Add(clone);
        }

        return result;
    }

    public bool DeleteSelectedVolumeCurve()
    {
        EnsureVolumeCurveState();
        if (selectedVolumeCurveIndex < 0 || selectedVolumeCurveIndex >= volumeCurves.Count)
            return false;

        RegisterUndo("Delete Volume Curve");
        volumeCurves.RemoveAt(selectedVolumeCurveIndex);
        selectedVolumeCurveIndex = Mathf.Clamp(selectedVolumeCurveIndex, 0, volumeCurves.Count - 1);
        if (volumeCurves.Count == 0)
            selectedVolumeCurveIndex = -1;
        selectedVolumeCurvePointIndex = -1;
        return true;
    }

    public bool SelectVolumeCurve(int curveIndex)
    {
        EnsureVolumeCurveState();
        var nextIndex = curveIndex >= 0 && curveIndex < volumeCurves.Count ? curveIndex : -1;
        if (selectedVolumeCurveIndex == nextIndex)
            return false;

        selectedVolumeCurveIndex = nextIndex;
        selectedVolumeCurvePointIndex = -1;
        return true;
    }

    public bool RenameSelectedVolumeCurve(string name)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        name = string.IsNullOrWhiteSpace(name) ? "Curve" : name.Trim();
        if (string.Equals(curve.name, name, StringComparison.Ordinal))
            return false;

        RegisterUndo("Rename Volume Curve");
        curve.name = name;
        return true;
    }

    public bool SetSelectedVolumeCurveClosed(bool closed)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;
        if (curve.closed == closed)
            return false;

        RegisterUndo("Set Volume Curve Closed");
        curve.closed = closed;
        return true;
    }

    public bool SetSelectedVolumeCurveRadius(float radius)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        radius = Mathf.Max(0.01f, radius);
        if (Mathf.Abs(curve.radius - radius) <= 0.0001f)
            return false;

        RegisterUndo("Set Volume Curve Radius");
        curve.radius = radius;
        return true;
    }

    public bool SetSelectedVolumeCurveStrength(float strength)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        strength = Mathf.Clamp01(strength);
        if (Mathf.Abs(curve.strength - strength) <= 0.0001f)
            return false;

        RegisterUndo("Set Volume Curve Strength");
        curve.strength = strength;
        return true;
    }

    public bool SetSelectedVolumeCurveTargetRendererBone(int rendererBoneIndex)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        rendererBoneIndex = rendererBoneIndex >= 0 && rendererBoneIndex < bones.Count ? rendererBoneIndex : -1;
        if (curve.targetRendererBoneIndex == rendererBoneIndex)
            return false;

        RegisterUndo("Set Volume Curve Target Bone");
        curve.targetRendererBoneIndex = rendererBoneIndex;
        return true;
    }

    public bool SetSelectedVolumeCurveFalloff(AnimationCurve falloff)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        RegisterUndo("Set Volume Curve Falloff");
        curve.falloff = CloneAnimationCurve(falloff ?? CreateDefaultVolumeFalloffCurve());
        return true;
    }

    public bool ApplySelectedVolumeCurvePreset(VolumeCurveFalloffPreset preset)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        RegisterUndo("Apply Volume Curve Preset");
        switch (preset)
        {
            case VolumeCurveFalloffPreset.Soft:
                curve.radius = 48f;
                curve.strength = 0.65f;
                curve.falloff = AnimationCurve.EaseInOut(0f, 0f, 1f, DefaultVolumeCurveMaxInfluence);
                break;
            case VolumeCurveFalloffPreset.Hard:
                curve.radius = 18f;
                curve.strength = 1f;
                curve.falloff = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.55f, 0f),
                    new Keyframe(1f, DefaultVolumeCurveMaxInfluence));
                break;
            default:
                curve.radius = 32f;
                curve.strength = 1f;
                curve.falloff = CreateDefaultVolumeFalloffCurve();
                break;
        }

        return true;
    }

    public bool ApplySelectedVolumeCurve(out string error)
    {
        error = string.Empty;
        if (!TryGetSelectedVolumeCurve(out var curve))
        {
            error = "Select Volume curve.";
            return false;
        }

        if (curve.points == null || curve.points.Count < 2)
        {
            error = "Volume curve needs at least two points.";
            return false;
        }

        if (!TryGetSelectedVolumeLayerData(out var data, out var selectionKey, out var stableKey))
        {
            error = "Select a sprite with editable geometry.";
            return false;
        }

        if (data.vertices == null || data.vertices.Length == 0)
        {
            error = "Selected sprite has no editable vertices.";
            return false;
        }

        var targetRendererBone = curve.targetRendererBoneIndex >= 0 ? curve.targetRendererBoneIndex : selectedBoneIndex;
        if (targetRendererBone < 0 || targetRendererBone >= bones.Count)
        {
            error = "Select a target bone.";
            return false;
        }

        RestorePoseForAssetTool();

        var appendTargetBone = false;
        var spriteBoneCount = data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0;
        if (!TryResolveVolumeTargetSpriteBone(data, targetRendererBone, out var targetSpriteBone))
        {
            if (!TryBuildBindInverseMatrixForRendererBone(data, targetRendererBone, out _))
            {
                error = "Target bone cannot be added to selected sprite.";
                return false;
            }

            appendTargetBone = true;
            targetSpriteBone = spriteBoneCount;
            spriteBoneCount++;
        }

        if (!TryEnsureRendererBindPoseSkinning(data))
        {
            error = "Volume could not prepare selected sprite bind poses.";
            return false;
        }

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        if (!appendTargetBone)
            EnsureLayerBindMatricesForWeights(data);

        var changedCount = ReanimatorVolumeCurveAlgorithms.ApplyCurveToWeights(
            curve,
            data.vertices,
            data.boneWeights,
            targetSpriteBone,
            spriteBoneCount,
            out var nextWeights);
        if (changedCount <= 0 || nextWeights == null)
        {
            error = "Volume did not affect any vertices.";
            return false;
        }

        RegisterUndo("Apply Volume Curve");
        if (appendTargetBone && !TryAppendSkinningTargetBone(data, targetRendererBone, out targetSpriteBone))
        {
            error = "Target bone cannot be added to selected sprite.";
            return false;
        }

        data.boneWeights = nextWeights;
        curve.targetRendererBoneIndex = targetRendererBone;
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        error = $"Volume affected {changedCount} vertices.";
        return true;
    }

    public bool AddVolumeCurvePoint(Vector2 positionDoc)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        if (curve.points == null)
            curve.points = new List<VolumeCurvePoint>();

        RegisterUndo("Add Volume Curve Point");
        var defaultType = GetDefaultPointType(curve);
        curve.points.Add(new VolumeCurvePoint
        {
            positionDoc = positionDoc,
            type = defaultType,
            inType = defaultType,
            outType = defaultType
        });
        selectedVolumeCurvePointIndex = curve.points.Count - 1;
        return true;
    }

    public bool DeleteSelectedVolumeCurvePoint()
    {
        return DeleteVolumeCurvePoint(selectedVolumeCurveIndex, selectedVolumeCurvePointIndex);
    }

    public bool DeleteVolumeCurvePoint(int curveIndex, int pointIndex)
    {
        SelectVolumeCurve(curveIndex);
        if (!TryGetSelectedVolumeCurve(out var curve) || curve.points == null)
            return false;
        if (pointIndex < 0 || pointIndex >= curve.points.Count)
            return false;

        RegisterUndo("Delete Volume Curve Point");
        curve.points.RemoveAt(pointIndex);
        selectedVolumeCurvePointIndex = Mathf.Clamp(pointIndex, 0, curve.points.Count - 1);
        if (curve.points.Count == 0)
            selectedVolumeCurvePointIndex = -1;
        return true;
    }

    public bool InsertVolumeCurvePointOnSegment(int curveIndex, int segmentIndex, Vector2 positionDoc)
    {
        SelectVolumeCurve(curveIndex);
        if (!TryGetSelectedVolumeCurve(out var curve) || curve.points == null)
            return false;

        var segmentCount = curve.closed ? curve.points.Count : curve.points.Count - 1;
        if (segmentIndex < 0 || segmentIndex >= segmentCount)
            return false;

        var insertIndex = Mathf.Clamp(segmentIndex + 1, 0, curve.points.Count);
        var segmentType = curve.points[segmentIndex] != null ? curve.points[segmentIndex].outType : GetDefaultPointType(curve);
        RegisterUndo("Insert Volume Curve Point");
        curve.points.Insert(insertIndex, new VolumeCurvePoint
        {
            positionDoc = positionDoc,
            type = segmentType,
            inType = segmentType,
            outType = segmentType
        });
        selectedVolumeCurvePointIndex = insertIndex;
        return true;
    }

    public bool SelectVolumeCurvePoint(int pointIndex)
    {
        if (!TryGetSelectedVolumeCurve(out var curve))
            return false;

        var pointCount = curve.points != null ? curve.points.Count : 0;
        var nextIndex = pointIndex >= 0 && pointIndex < pointCount ? pointIndex : -1;
        if (selectedVolumeCurvePointIndex == nextIndex)
            return false;

        selectedVolumeCurvePointIndex = nextIndex;
        return true;
    }

    public bool SetSelectedVolumeCurvePointPosition(Vector2 positionDoc)
    {
        if (!TryGetSelectedVolumeCurvePoint(out var point))
            return false;
        if ((point.positionDoc - positionDoc).sqrMagnitude <= 0.0001f)
            return false;

        RegisterUndo("Move Volume Curve Point");
        point.positionDoc = positionDoc;
        return true;
    }

    public bool SetVolumeCurvePointPosition(int pointIndex, Vector2 positionDoc)
    {
        if (!TryGetSelectedVolumeCurve(out var curve) || curve.points == null)
            return false;
        if (pointIndex < 0 || pointIndex >= curve.points.Count)
            return false;

        var point = curve.points[pointIndex];
        if (point == null)
            return false;
        if ((point.positionDoc - positionDoc).sqrMagnitude <= 0.0001f)
            return false;

        if (volumeCurveIsDragging)
        {
            if (!volumeCurvePointMoveUndoRegistered)
            {
                RegisterUndo("Move Volume Curve Point");
                volumeCurvePointMoveUndoRegistered = true;
            }
        }
        else
        {
            RegisterUndo("Move Volume Curve Point");
        }

        point.positionDoc = positionDoc;
        selectedVolumeCurvePointIndex = pointIndex;
        return true;
    }

    public bool SetSelectedVolumeCurvePointInType(VolumeCurvePointType type)
    {
        if (!TryGetSelectedVolumeCurvePoint(out var point))
            return false;
        if (point.inType == type)
            return false;

        RegisterUndo("Set Volume Curve Point In Type");
        point.inType = type;
        point.type = type;
        return true;
    }

    public bool SetSelectedVolumeCurvePointOutType(VolumeCurvePointType type)
    {
        if (!TryGetSelectedVolumeCurvePoint(out var point))
            return false;
        if (point.outType == type)
            return false;

        RegisterUndo("Set Volume Curve Point Out Type");
        point.outType = type;
        point.type = type;
        return true;
    }

    public bool TryGetSelectedVolumeCurve(out VolumeCurve curve)
    {
        EnsureVolumeCurveState();
        curve = null;
        if (selectedVolumeCurveIndex < 0 || selectedVolumeCurveIndex >= volumeCurves.Count)
            return false;

        curve = volumeCurves[selectedVolumeCurveIndex];
        return curve != null;
    }

    private bool TryGetSelectedVolumeCurvePoint(out VolumeCurvePoint point)
    {
        point = null;
        if (!TryGetSelectedVolumeCurve(out var curve) || curve.points == null)
            return false;
        if (selectedVolumeCurvePointIndex < 0 || selectedVolumeCurvePointIndex >= curve.points.Count)
            return false;

        point = curve.points[selectedVolumeCurvePointIndex];
        return point != null;
    }

    private void EnsureVolumeCurveState()
    {
        if (volumeCurves == null)
            volumeCurves = new List<VolumeCurve>();

        for (var i = volumeCurves.Count - 1; i >= 0; i--)
        {
            var curve = volumeCurves[i];
            if (curve == null)
            {
                volumeCurves.RemoveAt(i);
                continue;
            }

            if (string.IsNullOrWhiteSpace(curve.id))
                curve.id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(curve.name))
                curve.name = $"Curve {i + 1}";
            curve.radius = Mathf.Max(0.01f, curve.radius);
            curve.strength = Mathf.Clamp01(curve.strength);
            if (curve.falloff == null)
                curve.falloff = CreateDefaultVolumeFalloffCurve();
            if (curve.points == null)
                curve.points = new List<VolumeCurvePoint>();

            for (var pointIndex = curve.points.Count - 1; pointIndex >= 0; pointIndex--)
            {
                var point = curve.points[pointIndex];
                if (point == null)
                {
                    curve.points.RemoveAt(pointIndex);
                    continue;
                }

                if (point.inType == VolumeCurvePointType.Linear &&
                    point.outType == VolumeCurvePointType.Linear &&
                    point.type != VolumeCurvePointType.Linear)
                {
                    point.inType = point.type;
                    point.outType = point.type;
                }
            }
        }

        if (volumeCurves.Count == 0)
            selectedVolumeCurveIndex = -1;
        else if (selectedVolumeCurveIndex >= volumeCurves.Count)
            selectedVolumeCurveIndex = volumeCurves.Count - 1;
        else if (selectedVolumeCurveIndex < -1)
            selectedVolumeCurveIndex = -1;

        var selectedPointCount = selectedVolumeCurveIndex >= 0 && selectedVolumeCurveIndex < volumeCurves.Count && volumeCurves[selectedVolumeCurveIndex].points != null
            ? volumeCurves[selectedVolumeCurveIndex].points.Count
            : 0;
        if (selectedPointCount == 0)
            selectedVolumeCurvePointIndex = -1;
        else if (selectedVolumeCurvePointIndex >= selectedPointCount)
            selectedVolumeCurvePointIndex = selectedPointCount - 1;
        else if (selectedVolumeCurvePointIndex < -1)
            selectedVolumeCurvePointIndex = -1;
    }

    private string BuildDefaultVolumeCurveName()
    {
        var index = volumeCurves != null ? volumeCurves.Count + 1 : 1;
        return $"Curve {index}";
    }

    private static VolumeCurve CloneVolumeCurve(VolumeCurve source)
    {
        if (source == null)
            return null;

        var clone = new VolumeCurve
        {
            id = string.IsNullOrWhiteSpace(source.id) ? Guid.NewGuid().ToString("N") : source.id,
            name = string.IsNullOrWhiteSpace(source.name) ? "Curve" : source.name,
            closed = source.closed,
            radius = Mathf.Max(0.01f, source.radius),
            strength = Mathf.Clamp01(source.strength),
            targetRendererBoneIndex = source.targetRendererBoneIndex,
            falloff = CloneAnimationCurve(source.falloff ?? CreateDefaultVolumeFalloffCurve()),
            points = new List<VolumeCurvePoint>()
        };

        if (source.points != null)
        {
            for (var i = 0; i < source.points.Count; i++)
            {
                var point = source.points[i];
                if (point == null)
                    continue;

                clone.points.Add(new VolumeCurvePoint
                {
                    positionDoc = point.positionDoc,
                    type = point.type,
                    inType = point.inType,
                    outType = point.outType
                });
            }
        }

        return clone;
    }

    private static AnimationCurve CloneAnimationCurve(AnimationCurve source)
    {
        if (source == null)
            return CreateDefaultVolumeFalloffCurve();

        var clone = new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
        return clone;
    }

    private static AnimationCurve CreateDefaultVolumeFalloffCurve()
    {
        return AnimationCurve.Linear(0f, 0f, 1f, DefaultVolumeCurveMaxInfluence);
    }

    private static VolumeCurvePointType GetDefaultPointType(VolumeCurve curve)
    {
        if (curve != null && curve.points != null && curve.points.Count > 0)
        {
            var previous = curve.points[curve.points.Count - 1];
            if (previous != null)
                return previous.type;
        }

        return VolumeCurvePointType.Catmull;
    }

    private bool TryGetSelectedVolumeLayerData(out LayerVertexData data, out string selectionKey, out string stableKey)
    {
        data = null;
        selectionKey = selectedLayerSelectionKey;
        stableKey = editGeometrySelectedLayerStableKey;

        if (!string.IsNullOrEmpty(selectionKey) &&
            TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) &&
            data != null)
        {
            stableKey = BuildKey(data.layer.name, data.layer.layerId);
            editGeometrySelectedLayerStableKey = stableKey;
            return true;
        }

        if (!string.IsNullOrEmpty(stableKey) &&
            TryGetLayerVertexDataByStableKey(stableKey, out data, out var resolvedSelectionKey) &&
            data != null)
        {
            selectionKey = resolvedSelectionKey;
            selectedLayerSelectionKey = selectionKey;
            editGeometrySelectedLayerStableKey = stableKey;
            return true;
        }

        if (TryGetSelectedLayer(out var layer) && layer != null)
        {
            selectionKey = BuildSelectionKey(layer);
            stableKey = BuildKey(layer.name, layer.layerId);
            if (TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) && data != null)
            {
                selectedLayerSelectionKey = selectionKey;
                editGeometrySelectedLayerStableKey = stableKey;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveVolumeTargetSpriteBone(LayerVertexData data, int targetRendererBone, out int targetSpriteBone)
    {
        targetSpriteBone = -1;
        if (data == null || data.spriteBoneToRendererBone == null || targetRendererBone < 0)
            return false;

        for (var i = 0; i < data.spriteBoneToRendererBone.Length; i++)
        {
            if (data.spriteBoneToRendererBone[i] != targetRendererBone)
                continue;

            targetSpriteBone = i;
            return true;
        }

        return false;
    }
}
#endif
