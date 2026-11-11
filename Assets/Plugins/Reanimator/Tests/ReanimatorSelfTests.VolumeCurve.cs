#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

internal static partial class ReanimatorSelfTests
{
    private static void VolumeCurveStateIsSerializableAndEditable()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        AssertTrue(renderer.CreateVolumeCurve(), "CreateVolumeCurve should report a change.");
        AssertEqual(1, renderer.VolumeCurves.Count, "Curve count after create.");
        AssertEqual(0, renderer.SelectedVolumeCurveIndex, "Selected curve after create.");
        AssertEqual(-1, renderer.SelectedVolumeCurvePointIndex, "Selected point before add.");
        AssertFloat(0.25f, renderer.VolumeCurves[0].falloff.Evaluate(1f), 0.0001f, "Default Volume curve should add only a subtle target bone influence.");

        AssertTrue(renderer.RenameSelectedVolumeCurve("  Main Volume  "), "Rename should report a change.");
        AssertEqual("Main Volume", renderer.VolumeCurves[0].name, "Rename should trim whitespace.");
        AssertTrue(renderer.SetSelectedVolumeCurveClosed(true), "Closed flag should change.");
        AssertTrue(renderer.VolumeCurves[0].closed, "Closed flag persisted.");

        AssertTrue(renderer.AddVolumeCurvePoint(new Vector2(10f, 20f)), "First point add should change.");
        AssertTrue(renderer.AddVolumeCurvePoint(new Vector2(30f, 40f)), "Second point add should change.");
        AssertEqual(2, renderer.VolumeCurves[0].points.Count, "Point count after adds.");
        AssertEqual(1, renderer.SelectedVolumeCurvePointIndex, "Last point should be selected.");

        AssertTrue(renderer.SetSelectedVolumeCurvePointInType(PsdLayerPreviewRenderer.VolumeCurvePointType.Bezier), "In type should change.");
        AssertTrue(renderer.SetSelectedVolumeCurvePointOutType(PsdLayerPreviewRenderer.VolumeCurvePointType.Euler), "Out type should change.");
        AssertEqual(PsdLayerPreviewRenderer.VolumeCurvePointType.Bezier, renderer.VolumeCurves[0].points[1].inType, "In type persisted.");
        AssertEqual(PsdLayerPreviewRenderer.VolumeCurvePointType.Euler, renderer.VolumeCurves[0].points[1].outType, "Out type persisted.");

        AssertTrue(renderer.SetSelectedVolumeCurvePointPosition(new Vector2(35f, 45f)), "Point move should change.");
        AssertVector(new Vector2(35f, 45f), renderer.VolumeCurves[0].points[1].positionDoc, 0.0001f, "Point position persisted.");
        AssertTrue(undoLabels.Count >= 6, "Undo labels should be emitted for editing actions.");
    }

    private static void VolumeCurveSelectionAndDeletionStaysClamped()
    {
        var renderer = new PsdLayerPreviewRenderer();
        renderer.CreateVolumeCurve();
        renderer.AddVolumeCurvePoint(new Vector2(0f, 0f));
        renderer.AddVolumeCurvePoint(new Vector2(1f, 0f));
        renderer.AddVolumeCurvePoint(new Vector2(2f, 0f));

        AssertTrue(renderer.SelectVolumeCurvePoint(99), "Selecting outside range should change to no selection.");
        AssertEqual(-1, renderer.SelectedVolumeCurvePointIndex, "Out-of-range point selection should clear.");
        AssertFalse(renderer.DeleteSelectedVolumeCurvePoint(), "Deleting no point should not change.");

        AssertTrue(renderer.SelectVolumeCurve(99), "Selecting outside curve range should change to no selection.");
        AssertEqual(-1, renderer.SelectedVolumeCurveIndex, "Out-of-range curve selection should clear.");
        AssertFalse(renderer.DeleteSelectedVolumeCurve(), "Deleting no curve should not change.");
        AssertTrue(renderer.SelectVolumeCurve(0), "Selecting curve 0 should change.");

        AssertTrue(renderer.SelectVolumeCurvePoint(1), "Selecting point 1 should change.");
        AssertTrue(renderer.DeleteSelectedVolumeCurvePoint(), "Deleting selected point should change.");
        AssertEqual(2, renderer.VolumeCurves[0].points.Count, "Point count after delete.");
        AssertEqual(1, renderer.SelectedVolumeCurvePointIndex, "Selected point should clamp to valid point.");

        AssertTrue(renderer.DeleteSelectedVolumeCurve(), "Deleting selected curve should change.");
        AssertEqual(0, renderer.VolumeCurves.Count, "Curve count after delete.");
        AssertEqual(-1, renderer.SelectedVolumeCurveIndex, "Selected curve after deleting last curve.");
        AssertEqual(-1, renderer.SelectedVolumeCurvePointIndex, "Selected point after deleting last curve.");
    }

    private static void VolumeCurveAlgorithmsEvaluateSegmentTypes()
    {
        var curve = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(20f, 10f),
            new Vector2(30f, 0f)
        });

        curve.points[0].outType = PsdLayerPreviewRenderer.VolumeCurvePointType.Linear;
        AssertVector(new Vector2(2.5f, 0f), ReanimatorVolumeCurveAlgorithms.EvaluateSegment(curve, 0, 0.25f), 0.0001f, "Linear segment.");

        curve.points[0].outType = PsdLayerPreviewRenderer.VolumeCurvePointType.Euler;
        AssertVector(new Vector2(1.5625f, 0f), ReanimatorVolumeCurveAlgorithms.EvaluateSegment(curve, 0, 0.25f), 0.0001f, "Euler smooth segment.");

        curve.points[1].outType = PsdLayerPreviewRenderer.VolumeCurvePointType.Catmull;
        AssertVector(new Vector2(15f, 5.625f), ReanimatorVolumeCurveAlgorithms.EvaluateSegment(curve, 1, 0.5f), 0.0001f, "Catmull segment.");

        curve.points[1].outType = PsdLayerPreviewRenderer.VolumeCurvePointType.Bezier;
        AssertVector(new Vector2(15f, 4.166667f), ReanimatorVolumeCurveAlgorithms.EvaluateSegment(curve, 1, 0.5f), 0.0001f, "Quadratic bezier segment.");

        AssertVector(Vector2.zero, ReanimatorVolumeCurveAlgorithms.EvaluateSegment(curve, 99, 0.5f), 0.0001f, "Invalid segment should return zero.");
    }

    private static void VolumeCurveAlgorithmsBuildOpenAndClosedDrawPoints()
    {
        Func<Vector2, Vector2> identity = point => point;
        var open = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(10f, 10f)
        });

        var openPoints = ReanimatorVolumeCurveAlgorithms.BuildDrawPoints(open, identity);
        AssertEqual(3, openPoints.Count, "Open linear draw point count.");
        AssertVector(new Vector2(0f, 0f), openPoints[0], 0.0001f, "Open draw should start at first point.");
        AssertVector(new Vector2(10f, 0f), openPoints[1], 0.0001f, "Open draw first segment end.");
        AssertVector(new Vector2(10f, 10f), openPoints[2], 0.0001f, "Open draw final point.");

        var closed = BuildVolumeCurve(true, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(10f, 10f)
        });

        var closedPoints = ReanimatorVolumeCurveAlgorithms.BuildDrawPoints(closed, identity);
        AssertEqual(4, closedPoints.Count, "Closed linear draw point count.");
        AssertVector(new Vector2(0f, 0f), closedPoints[0], 0.0001f, "Closed draw should start at first point.");
        AssertVector(new Vector2(0f, 0f), closedPoints[3], 0.0001f, "Closed draw should return to first point.");

        AssertTrue(ReanimatorVolumeCurveAlgorithms.TryGetBoundsCenter(closed, out var center), "Bounds center should resolve.");
        AssertVector(new Vector2(5f, 5f), center, 0.0001f, "Bounds center.");
        AssertVector(new Vector2(34f, 10f), ReanimatorVolumeCurveAlgorithms.BuildDefaultPointPosition(closed), 0.0001f, "Default next point position.");
    }

    private static void VolumeCurveAlgorithmsHitTestPointsAndCurves()
    {
        Func<Vector2, Vector2> scaled = point => point * 2f;
        var first = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(20f, 0f)
        });
        var second = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 20f),
            new Vector2(10f, 20f),
            new Vector2(20f, 20f)
        });

        AssertEqual(1, ReanimatorVolumeCurveAlgorithms.FindCurvePointAt(first, scaled, new Vector2(21f, 1f), 3f), "Point hit should use view mapper.");
        AssertEqual(-1, ReanimatorVolumeCurveAlgorithms.FindCurvePointAt(first, scaled, new Vector2(21f, 8f), 3f), "Distant point should miss.");

        var curves = new[] { first, second };
        var pointIndex = ReanimatorVolumeCurveAlgorithms.FindAnyCurvePointAt(curves, scaled, new Vector2(21f, 41f), 5f, out var curveIndex);
        AssertEqual(1, curveIndex, "Nearest point curve index.");
        AssertEqual(1, pointIndex, "Nearest point index.");

        AssertEqual(0, ReanimatorVolumeCurveAlgorithms.FindCurveAt(curves, scaled, new Vector2(20f, 2f), 3f), "Curve segment hit.");
        AssertEqual(1, ReanimatorVolumeCurveAlgorithms.FindCurveAt(curves, scaled, new Vector2(20f, 38f), 3f), "Second curve segment hit.");
        AssertEqual(-1, ReanimatorVolumeCurveAlgorithms.FindCurveAt(curves, scaled, new Vector2(20f, 20f), 3f), "Middle gap should miss.");
    }

    private static void VolumeCurveAlgorithmsHitTestInsertSegments()
    {
        Func<Vector2, Vector2> scaled = point => point * 2f;
        var curve = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(20f, 0f)
        });

        AssertTrue(
            ReanimatorVolumeCurveAlgorithms.TryFindCurveSegmentAt(
                new[] { curve },
                scaled,
                new Vector2(30f, 2f),
                3f,
                out var curveIndex,
                out var segmentIndex,
                out var nearestDoc),
            "Double-click hit-test should resolve the clicked curve segment.");
        AssertEqual(0, curveIndex, "Insert hit curve index.");
        AssertEqual(1, segmentIndex, "Insert hit segment index.");
        AssertVector(new Vector2(15f, 0f), nearestDoc, 0.0001f, "Insert document point should be projected onto clicked segment.");
    }

    private static void VolumeCurveAlgorithmsEvaluateInfluenceByRadiusAndFalloff()
    {
        var curve = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f)
        });
        curve.radius = 10f;
        curve.strength = 0.5f;
        curve.falloff = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        var points = ReanimatorVolumeCurveAlgorithms.BuildDocumentDrawPoints(curve);
        AssertFloat(0.5f, ReanimatorVolumeCurveAlgorithms.EvaluateInfluence(points, new Vector2(5f, 0f), curve.radius, curve.strength, curve.falloff), 0.0001f, "Centerline influence.");
        AssertFloat(0.25f, ReanimatorVolumeCurveAlgorithms.EvaluateInfluence(points, new Vector2(5f, 5f), curve.radius, curve.strength, curve.falloff), 0.0001f, "Half-radius influence.");
        AssertFloat(0f, ReanimatorVolumeCurveAlgorithms.EvaluateInfluence(points, new Vector2(5f, 11f), curve.radius, curve.strength, curve.falloff), 0.0001f, "Outside radius influence.");
    }

    private static void VolumeCurveAlgorithmsApplyWeightsToTargetBone()
    {
        var curve = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f)
        });
        curve.radius = 10f;
        curve.strength = 0.75f;
        curve.falloff = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        var vertices = new[]
        {
            new Vector2(5f, 0f),
            new Vector2(5f, 5f),
            new Vector2(5f, 20f)
        };
        var weights = new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 0f, boneIndex1 = 0, weight1 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        };

        var changed = ReanimatorVolumeCurveAlgorithms.ApplyCurveToWeights(curve, vertices, weights, 1, 3, out var result);
        AssertEqual(2, changed, "Affected vertex count.");
        AssertFloat(0.75f, GetWeight(result[0], 1), 0.0001f, "Target weight at centerline.");
        AssertFloat(0.25f, GetWeight(result[0], 0), 0.0001f, "Other weight scaled at centerline.");
        AssertFloat(0.375f, GetWeight(result[1], 1), 0.0001f, "Zero target slot should become editable.");
        AssertFloat(0.625f, GetWeight(result[1], 0), 0.0001f, "Zero target slot should preserve remaining capacity.");
        AssertFloat(0f, GetWeight(result[2], 1), 0.0001f, "Outside vertex target weight unchanged.");
        AssertFloat(1f, SumWeight(result[0]), 0.0001f, "Centerline sum normalized.");
        AssertFloat(1f, SumWeight(result[1]), 0.0001f, "Half-radius sum normalized.");
        AssertFloat(1f, SumWeight(result[2]), 0.0001f, "Outside sum preserved.");
    }

    private static void VolumeCurveInsertsAndDeletesExactPoints()
    {
        var renderer = new PsdLayerPreviewRenderer();
        renderer.CreateVolumeCurve();
        renderer.AddVolumeCurvePoint(new Vector2(0f, 0f));
        renderer.AddVolumeCurvePoint(new Vector2(10f, 0f));
        renderer.AddVolumeCurvePoint(new Vector2(20f, 0f));

        AssertTrue(renderer.InsertVolumeCurvePointOnSegment(0, 1, new Vector2(15f, 0f)), "Insert on segment should change curve.");
        AssertEqual(4, renderer.VolumeCurves[0].points.Count, "Point count after segment insert.");
        AssertEqual(2, renderer.SelectedVolumeCurvePointIndex, "Inserted point should be selected.");
        AssertVector(new Vector2(15f, 0f), renderer.VolumeCurves[0].points[2].positionDoc, 0.0001f, "Inserted point should sit between segment endpoints.");
        AssertVector(new Vector2(20f, 0f), renderer.VolumeCurves[0].points[3].positionDoc, 0.0001f, "Later points should keep sequence after insert.");

        AssertTrue(renderer.SelectVolumeCurvePoint(3), "Last point selection should change.");
        AssertTrue(renderer.DeleteVolumeCurvePoint(0, 1), "Targeted delete should remove the requested row, not the selected row.");
        AssertEqual(3, renderer.VolumeCurves[0].points.Count, "Point count after targeted delete.");
        AssertVector(new Vector2(15f, 0f), renderer.VolumeCurves[0].points[1].positionDoc, 0.0001f, "Targeted delete should remove the old second point.");
        AssertVector(new Vector2(20f, 0f), renderer.VolumeCurves[0].points[2].positionDoc, 0.0001f, "Previously selected last point should remain.");
    }

    private static void VolumeCurvePresetsCloneAndLoadFromAssetsSafely()
    {
        var renderer = new PsdLayerPreviewRenderer();
        renderer.CreateVolumeCurve();
        renderer.RenameSelectedVolumeCurve("Sleeve soft");
        renderer.SetSelectedVolumeCurveRadius(42f);
        renderer.SetSelectedVolumeCurveStrength(0.5f);
        renderer.SetSelectedVolumeCurveFalloff(AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
        renderer.AddVolumeCurvePoint(new Vector2(1f, 2f));
        renderer.AddVolumeCurvePoint(new Vector2(5f, 8f));

        var preset = ScriptableObject.CreateInstance<ReanimatorVolumeCurvePresetAsset>();
        preset.curves = renderer.CloneVolumeCurvesForPreset();
        preset.curves[0].name = "Mutated preset copy";
        preset.curves[0].points[0].positionDoc = new Vector2(99f, 99f);
        AssertEqual("Sleeve soft", renderer.VolumeCurves[0].name, "Preset clone should not mutate live curve name.");
        AssertVector(new Vector2(1f, 2f), renderer.VolumeCurves[0].points[0].positionDoc, 0.0001f, "Preset clone should not share point references.");

        var loaded = new PsdLayerPreviewRenderer();
        AssertTrue(loaded.ReplaceVolumeCurves(preset.curves), "Preset load should replace live curves.");
        AssertEqual(1, loaded.VolumeCurves.Count, "Loaded preset curve count.");
        AssertEqual("Mutated preset copy", loaded.VolumeCurves[0].name, "Loaded preset curve name.");
        AssertVector(new Vector2(99f, 99f), loaded.VolumeCurves[0].points[0].positionDoc, 0.0001f, "Loaded preset point.");

        preset.curves[0].points[0].positionDoc = Vector2.zero;
        AssertVector(new Vector2(99f, 99f), loaded.VolumeCurves[0].points[0].positionDoc, 0.0001f, "Loaded curve should not share preset point references.");
        UnityEngine.Object.DestroyImmediate(preset);
    }

    private static void VolumeCurvePresetsDefaultToSubtleInfluence()
    {
        var renderer = new PsdLayerPreviewRenderer();
        renderer.CreateVolumeCurve();

        AssertTrue(renderer.ApplySelectedVolumeCurvePreset(PsdLayerPreviewRenderer.VolumeCurveFalloffPreset.Linear), "Linear preset should apply.");
        AssertFloat(0.25f, renderer.VolumeCurves[0].falloff.Evaluate(1f), 0.0001f, "Linear preset should cap default volume influence.");

        AssertTrue(renderer.ApplySelectedVolumeCurvePreset(PsdLayerPreviewRenderer.VolumeCurveFalloffPreset.Soft), "Soft preset should apply.");
        AssertFloat(0.25f, renderer.VolumeCurves[0].falloff.Evaluate(1f), 0.0001f, "Soft preset should cap default volume influence.");

        AssertTrue(renderer.ApplySelectedVolumeCurvePreset(PsdLayerPreviewRenderer.VolumeCurveFalloffPreset.Hard), "Hard preset should apply.");
        AssertFloat(0.25f, renderer.VolumeCurves[0].falloff.Evaluate(1f), 0.0001f, "Hard preset should cap default volume influence.");
    }

    private static void VolumeCurveDragMovementUsesOneUndoGroup()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;
        renderer.CreateVolumeCurve();
        renderer.AddVolumeCurvePoint(Vector2.zero);

        undoLabels.Clear();
        renderer.BeginVolumeCurvePointDrag(17);
        AssertTrue(renderer.SetVolumeCurvePointPosition(0, new Vector2(1f, 0f)), "First drag move should change point.");
        AssertTrue(renderer.SetVolumeCurvePointPosition(0, new Vector2(2f, 0f)), "Second drag move should change point.");
        AssertTrue(renderer.SetVolumeCurvePointPosition(0, new Vector2(3f, 0f)), "Third drag move should change point.");
        renderer.EndVolumeCurvePointDrag();

        AssertArray(new[] { "Move Volume Curve Point" }, undoLabels.ToArray(), "Drag should emit one undo label for the whole stroke.");

        AssertTrue(renderer.SetVolumeCurvePointPosition(0, new Vector2(4f, 0f)), "Panel move after drag should still be undoable.");
        AssertEqual(2, undoLabels.Count, "Non-drag point move should emit its own undo.");
    }

    private static void VolumeCurveRadiusFalloffWorksOnDenseAndSparseMeshes()
    {
        var curve = BuildVolumeCurve(false, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(100f, 0f)
        });
        curve.radius = 6f;
        curve.strength = 1f;
        curve.falloff = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        var denseVertices = new List<Vector2>();
        var denseWeights = new List<BoneWeight>();
        for (var y = -8; y <= 8; y += 2)
        {
            denseVertices.Add(new Vector2(50f, y));
            denseWeights.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        }

        var denseChanged = ReanimatorVolumeCurveAlgorithms.ApplyCurveToWeights(curve, denseVertices, denseWeights, 1, 2, out var denseResult);
        AssertEqual(5, denseChanged, "Dense mesh should affect vertices inside radius only.");
        AssertFloat(1f, GetWeight(denseResult[4], 1), 0.0001f, "Dense centerline target weight.");
        AssertFloat(0f, GetWeight(denseResult[0], 1), 0.0001f, "Dense outside target weight.");

        var sparseVertices = new[]
        {
            new Vector2(10f, -12f),
            new Vector2(50f, 4f),
            new Vector2(90f, 12f)
        };
        var sparseWeights = new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        };
        var sparseChanged = ReanimatorVolumeCurveAlgorithms.ApplyCurveToWeights(curve, sparseVertices, sparseWeights, 1, 2, out var sparseResult);
        AssertEqual(1, sparseChanged, "Sparse mesh should still affect the one vertex inside radius.");
        AssertFloat(1f - (4f / 6f), GetWeight(sparseResult[1], 1), 0.0001f, "Sparse falloff weight.");
        AssertFloat(0f, GetWeight(sparseResult[0], 1), 0.0001f, "Sparse outside vertex should stay untouched.");
        AssertFloat(0f, GetWeight(sparseResult[2], 1), 0.0001f, "Sparse far vertex should stay untouched.");
    }

    private static void VolumeCurveApplyDirtiesSelectedLayerOnly()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var selected = BuildLayer("volume-selected", 410, Vector2.zero);
        var other = BuildLayer("volume-other", 411, Vector2.zero);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected, other });
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo { index = 0, name = "base" },
            new PsdLayerPreviewRenderer.BoneInfo { index = 1, name = "volume" }
        });

        AssertTrue(renderer.SelectLayer(selected), "Selected layer fixture should be selectable.");
        var selectedKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
        var selectedStableKey = (string)InvokePrivateMethod(renderer, "BuildKey", selected.name, selected.layerId);
        var otherKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", other);
        var selectedData = CreateVolumeApplyLayerData(renderer, selected, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(8f, 0f)
        });
        var otherData = CreateVolumeApplyLayerData(renderer, other, new[]
        {
            new Vector2(0f, 20f),
            new Vector2(4f, 20f),
            new Vector2(8f, 20f)
        });
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectedKey] = selectedData;
        dataBySelectionKey[otherKey] = otherData;

        renderer.CreateVolumeCurve();
        renderer.SetSelectedVolumeCurveTargetRendererBone(1);
        renderer.SetSelectedVolumeCurveRadius(5f);
        renderer.SetSelectedVolumeCurveStrength(1f);
        renderer.SetSelectedVolumeCurveFalloff(AnimationCurve.Linear(0f, 0f, 1f, 1f));
        renderer.AddVolumeCurvePoint(new Vector2(0f, 0f));
        renderer.AddVolumeCurvePoint(new Vector2(8f, 0f));

        AssertTrue(renderer.ApplySelectedVolumeCurve(out var error), $"Volume apply should succeed. Error={error}");
        AssertTrue(renderer.HasPendingChanges, "Volume apply should mark renderer pending changes for Apply/reimport.");
        AssertTrue(GetPrivateField<List<string>>(renderer, "dirtyLayerStableKeys").Contains(selectedStableKey), "Volume apply should dirty selected layer stable key.");
        AssertFalse(GetPrivateField<List<string>>(renderer, "dirtyLayerStableKeys").Contains((string)InvokePrivateMethod(renderer, "BuildKey", other.name, other.layerId)), "Volume apply should not dirty unrelated layer.");
        AssertFloat(1f, GetWeight(GetPrivateField<BoneWeight[]>(selectedData, "boneWeights")[1], 1), 0.0001f, "Selected layer center vertex should receive target bone.");
        AssertFloat(0f, GetWeight(GetPrivateField<BoneWeight[]>(otherData, "boneWeights")[1], 1), 0.0001f, "Other layer weights should stay untouched.");
    }

    private static void VolumeCurveApplyAppendsMissingTargetBone()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var selected = BuildLayer("volume-missing-target", 412, Vector2.zero);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected });
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo { index = 0, name = "base" },
            new PsdLayerPreviewRenderer.BoneInfo { index = 1, name = "volume" }
        });

        AssertTrue(renderer.SelectLayer(selected), "Selected layer fixture should be selectable.");
        var selectedKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
        var data = CreateVolumeApplyLayerData(renderer, selected, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(8f, 0f)
        }, new[] { 0 });
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectedKey] = data;

        renderer.CreateVolumeCurve();
        renderer.SetSelectedVolumeCurveTargetRendererBone(1);
        renderer.SetSelectedVolumeCurveRadius(5f);
        renderer.SetSelectedVolumeCurveStrength(1f);
        renderer.SetSelectedVolumeCurveFalloff(AnimationCurve.Linear(0f, 0f, 1f, 1f));
        renderer.AddVolumeCurvePoint(new Vector2(0f, 0f));
        renderer.AddVolumeCurvePoint(new Vector2(8f, 0f));

        AssertTrue(renderer.ApplySelectedVolumeCurve(out var error), $"Volume apply should append missing target bone. Error={error}");
        AssertArray(new[] { 0, 1 }, GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), "Volume apply should append target renderer bone to selected sprite map.");
        AssertEqual(2, GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length, "Volume apply should append matching bind matrix.");
        AssertFloat(1f, GetWeight(GetPrivateField<BoneWeight[]>(data, "boneWeights")[1], 1), 0.0001f, "Center vertex should receive newly appended target sprite bone.");
    }

    private static void VolumeCurveApplyConvertsSpriteBindPosesBeforeTargetAppend()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var selected = BuildLayer("volume-sprite-bind-target", 413, new Vector2(1000f, 1000f));
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected });
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "base",
                parentId = -1,
                position = new Vector2(1000f, 1000f),
                initialPosition = new Vector2(1000f, 1000f),
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = new Vector2(1000f, 1000f),
                worldRotation = Quaternion.identity,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "volume",
                parentId = -1,
                position = new Vector2(1004f, 1000f),
                initialPosition = new Vector2(1004f, 1000f),
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = new Vector2(1004f, 1000f),
                worldRotation = Quaternion.identity,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        AssertTrue(renderer.SelectLayer(selected), "Selected layer fixture should be selectable.");
        var selectedKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
        var vertices = new[]
        {
            new Vector2(1000f, 1000f),
            new Vector2(1004f, 1000f),
            new Vector2(1008f, 1000f)
        };
        var data = CreateVolumeApplyLayerData(renderer, selected, vertices, new[] { 0 });
        SetPrivateField(data, "usesSpriteBindPoses", true);
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectedKey] = data;

        renderer.CreateVolumeCurve();
        renderer.SetSelectedVolumeCurveTargetRendererBone(1);
        renderer.SetSelectedVolumeCurveRadius(5f);
        renderer.SetSelectedVolumeCurveStrength(1f);
        renderer.SetSelectedVolumeCurveFalloff(AnimationCurve.Linear(0f, 0f, 1f, 1f));
        renderer.AddVolumeCurvePoint(new Vector2(1000f, 1000f));
        renderer.AddVolumeCurvePoint(new Vector2(1008f, 1000f));

        AssertTrue(renderer.ApplySelectedVolumeCurve(out var error), $"Volume apply should append missing target bone from a sprite-bind layer. Error={error}");
        AssertFalse(GetPrivateField<bool>(data, "usesSpriteBindPoses"), "Appending a missing target bone must convert the whole layer to renderer bind pose space.");
        AssertArray(new[] { 0, 1 }, GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), "Converted layer should keep old renderer bone and append target renderer bone.");

        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        var target = bones[1];
        target.rotation = Quaternion.AngleAxis(35f, Vector3.forward);
        target.worldRotation = target.rotation;
        bones[1] = target;

        var skinMatrices = new Matrix4x4[GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length];
        var deformed = new Vector2[vertices.Length];
        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            GetPrivateField<int[]>(data, "spriteBoneToRendererBone"),
            GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices"),
            GetPrivateField<bool>(data, "usesSpriteBindPoses"),
            selected,
            GetPrivateField<float>(data, "pixelsPerUnit"),
            GetPrivateField<Vector2>(data, "pivotPixels"),
            skinMatrices);
        ReanimatorSkinningAlgorithms.DeformVertices(
            vertices,
            GetPrivateField<BoneWeight[]>(data, "boneWeights"),
            skinMatrices,
            GetPrivateField<bool>(data, "usesSpriteBindPoses"),
            selected,
            GetPrivateField<float>(data, "pixelsPerUnit"),
            GetPrivateField<Vector2>(data, "pivotPixels"),
            deformed);

        for (var i = 0; i < deformed.Length; i++)
        {
            AssertTrue(!float.IsNaN(deformed[i].x) && !float.IsInfinity(deformed[i].x) && !float.IsNaN(deformed[i].y) && !float.IsInfinity(deformed[i].y), $"Deformed vertex {i} should stay finite after target bone rotation.");
            AssertTrue((deformed[i] - vertices[i]).magnitude < 20f, $"Deformed vertex {i} should not fly away after target bone rotation.");
        }
    }

    private static void VolumeCurveApplyConvertsSpriteBindPosesForExistingTarget()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var selected = BuildLayer("volume-existing-target-sprite-bind", 414, new Vector2(500f, 500f));
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected });
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "base",
                parentId = -1,
                position = new Vector2(500f, 500f),
                initialPosition = new Vector2(500f, 500f),
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = new Vector2(500f, 500f),
                worldRotation = Quaternion.identity,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "volume",
                parentId = -1,
                position = new Vector2(504f, 500f),
                initialPosition = new Vector2(504f, 500f),
                rotation = Quaternion.identity,
                initialRotation = Quaternion.identity,
                worldPosition = new Vector2(504f, 500f),
                worldRotation = Quaternion.identity,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        AssertTrue(renderer.SelectLayer(selected), "Selected layer fixture should be selectable.");
        var selectedKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
        var data = CreateVolumeApplyLayerData(renderer, selected, new[]
        {
            new Vector2(500f, 500f),
            new Vector2(504f, 500f),
            new Vector2(508f, 500f)
        }, new[] { 0, 1 });
        SetPrivateField(data, "usesSpriteBindPoses", true);
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectedKey] = data;

        renderer.CreateVolumeCurve();
        renderer.SetSelectedVolumeCurveTargetRendererBone(1);
        renderer.SetSelectedVolumeCurveRadius(5f);
        renderer.SetSelectedVolumeCurveStrength(1f);
        renderer.SetSelectedVolumeCurveFalloff(AnimationCurve.Linear(0f, 0f, 1f, 1f));
        renderer.AddVolumeCurvePoint(new Vector2(500f, 500f));
        renderer.AddVolumeCurvePoint(new Vector2(508f, 500f));

        AssertTrue(renderer.ApplySelectedVolumeCurve(out var error), $"Volume apply should normalize sprite-bind layers even when target already exists. Error={error}");
        AssertFalse(GetPrivateField<bool>(data, "usesSpriteBindPoses"), "Volume apply should not leave an existing target in sprite-bind mode after editing weights.");
        AssertArray(new[] { 0, 1 }, GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), "Existing target map should stay in the same order.");
        AssertEqual(2, GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length, "Existing target bind matrix count should stay aligned.");
    }

    private static void VolumeCurveApplyRestoresPreviewPoseBeforeWeights()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var selected = BuildLayer("volume-rest-pose-apply", 415, Vector2.zero);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected });
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "base",
                parentId = -1,
                position = new Vector2(15f, 10f),
                initialPosition = new Vector2(0f, 0f),
                rotation = Quaternion.AngleAxis(20f, Vector3.forward),
                initialRotation = Quaternion.identity,
                visible = true
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "volume",
                parentId = -1,
                position = new Vector2(25f, 10f),
                initialPosition = new Vector2(4f, 0f),
                rotation = Quaternion.AngleAxis(-30f, Vector3.forward),
                initialRotation = Quaternion.identity,
                visible = true
            }
        });
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        AssertTrue(renderer.SelectLayer(selected), "Selected layer fixture should be selectable.");
        var selectedKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
        var data = CreateVolumeApplyLayerData(renderer, selected, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(8f, 0f)
        }, new[] { 0, 1 });
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectedKey] = data;

        renderer.CreateVolumeCurve();
        renderer.SetSelectedVolumeCurveTargetRendererBone(1);
        renderer.SetSelectedVolumeCurveRadius(5f);
        renderer.SetSelectedVolumeCurveStrength(1f);
        renderer.SetSelectedVolumeCurveFalloff(AnimationCurve.Linear(0f, 0f, 1f, 1f));
        renderer.AddVolumeCurvePoint(new Vector2(0f, 0f));
        renderer.AddVolumeCurvePoint(new Vector2(8f, 0f));

        AssertTrue(renderer.ApplySelectedVolumeCurve(out var error), $"Volume apply should succeed from a stale preview pose. Error={error}");
        var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
        AssertVector(new Vector2(0f, 0f), bones[0].position, 0.0001f, "Volume apply should restore base bone position before editing weights.");
        AssertVector(new Vector2(4f, 0f), bones[1].position, 0.0001f, "Volume apply should restore target bone position before editing weights.");
        AssertTrue(Quaternion.Angle(Quaternion.identity, bones[0].rotation) <= 0.0001f, "Volume apply should restore base bone rotation before editing weights.");
        AssertTrue(Quaternion.Angle(Quaternion.identity, bones[1].rotation) <= 0.0001f, "Volume apply should restore target bone rotation before editing weights.");
        AssertFloat(1f, GetWeight(GetPrivateField<BoneWeight[]>(data, "boneWeights")[1], 1), 0.0001f, "Rest-pose Volume apply should still write target weights.");
    }

    private static PsdLayerPreviewRenderer.VolumeCurve BuildVolumeCurve(bool closed, IReadOnlyList<Vector2> positions)
    {
        var curve = new PsdLayerPreviewRenderer.VolumeCurve
        {
            closed = closed,
            points = new List<PsdLayerPreviewRenderer.VolumeCurvePoint>()
        };

        for (var i = 0; i < positions.Count; i++)
        {
            curve.points.Add(new PsdLayerPreviewRenderer.VolumeCurvePoint
            {
                positionDoc = positions[i],
                type = PsdLayerPreviewRenderer.VolumeCurvePointType.Linear,
                inType = PsdLayerPreviewRenderer.VolumeCurvePointType.Linear,
                outType = PsdLayerPreviewRenderer.VolumeCurvePointType.Linear
            });
        }

        return curve;
    }

    private static object CreateVolumeApplyLayerData(PsdLayerPreviewRenderer renderer, PsdLayerPreviewRenderer.LayerInfo layer, Vector2[] vertices, int[] spriteBoneToRendererBone = null)
    {
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "pixelsPerUnit", 1f);
        SetPrivateField(data, "pivotPixels", Vector2.zero);
        SetPrivateField(data, "vertices", vertices);
        SetPrivateField(data, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        var weights = new BoneWeight[vertices.Length];
        for (var i = 0; i < weights.Length; i++)
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        SetPrivateField(data, "boneWeights", weights);
        var boneMap = spriteBoneToRendererBone ?? new[] { 0, 1 };
        var bindMatrices = new Matrix4x4[boneMap.Length];
        for (var i = 0; i < bindMatrices.Length; i++)
            bindMatrices[i] = Matrix4x4.identity;
        SetPrivateField(data, "spriteBoneToRendererBone", boneMap);
        SetPrivateField(data, "bindBoneInverseMatrices", bindMatrices);
        return data;
    }

}
#endif
