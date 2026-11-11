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
    private sealed class Case
    {
        public readonly string Name;
        public readonly Action Body;

        public Case(string name, Action body)
        {
            Name = name;
            Body = body;
        }
    }

    private sealed class Result
    {
        public readonly string Name;
        public readonly Exception Error;

        public Result(string name, Exception error)
        {
            Name = name;
            Error = error;
        }
    }

    private readonly struct ToolFlagCase
    {
        public readonly ReanimatorToolMode Mode;
        public readonly string Name;
        public readonly Func<PsdPreviewToolOptions, bool> GetFlag;

        public ToolFlagCase(ReanimatorToolMode mode, string name, Func<PsdPreviewToolOptions, bool> getFlag)
        {
            Mode = mode;
            Name = name;
            GetFlag = getFlag;
        }
    }

    [MenuItem("KhorebdisLab/Reanimator/Run Self Tests")]
    public static void RunAllFromMenu()
    {
        RunAll(exitEditor: false);
    }

    public static void RunAll()
    {
        RunAll(exitEditor: Application.isBatchMode);
    }

    private static void RunAll(bool exitEditor)
    {
        var results = new List<Result>();
        var cases = BuildCases();
        ReanimatorReadinessRuntimeState.BeginSelfTestRun();
        for (var i = 0; i < cases.Count; i++)
        {
            var test = cases[i];
            try
            {
                test.Body();
                results.Add(new Result(test.Name, null));
                ReanimatorReadinessRuntimeState.RecordSelfTestResult(test.Name, true);
            }
            catch (Exception ex)
            {
                results.Add(new Result(test.Name, ex));
                ReanimatorReadinessRuntimeState.RecordSelfTestResult(test.Name, false);
            }
        }

        var failed = results.Count(result => result.Error != null);
        var report = BuildReport(results);
        Debug.Log(report);
        WriteReportIfRequested(report);

        if (failed > 0)
        {
            if (exitEditor)
                EditorApplication.Exit(1);
            throw new InvalidOperationException($"Reanimator self tests failed: {failed}/{results.Count}");
        }

        if (exitEditor)
            EditorApplication.Exit(0);
    }

    private static void AutoSkeletonScopesOutputsAndDetectsErrors()
    {
        const string graphJson = @"{""4"":{""class_type"":""SavePoseKpsAsJsonFile"",""inputs"":{""filename_prefix"":""shared""}}}";
        var graph = Newtonsoft.Json.Linq.JObject.Parse(graphJson);
        var client = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("ComfimatorComfyClient"))
            .First(type => type != null);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var scope = client.GetMethod("ScopeAutoSkeletonKeypoints", flags);
        var completed = client.GetMethod("GetCompletedAutoSkeletonOutputs", flags);
        var first = (string)scope.Invoke(null, new object[] { graph });
        var second = (string)scope.Invoke(null, new object[] { graph });
        AssertTrue(first != second && (string)graph["4"]["inputs"]["filename_prefix"] == second,
            "Concurrent prompts must use distinct keypoint prefixes.");

        const string foreign = @"{""foreign"":{""status"":{""completed"":true},""outputs"":{""4"":{}}}}";
        AssertTrue(((Newtonsoft.Json.Linq.JObject)completed.Invoke(null, new object[] { foreign, "own" })) == null,
            "Completed output for a different prompt must not be consumed.");
        const string complete = @"{""own"":{""status"":{""completed"":true},""outputs"":{""4"":{}}}}";
        AssertTrue(((Newtonsoft.Json.Linq.JObject)completed.Invoke(null, new object[] { complete, "own" }))?["4"] != null,
            "Only the requested completed prompt can supply output.");

        const string failed = @"{""own"":{""status"":{""completed"":false,""status_str"":""error"",""messages"":[""missing model""]}}}";
        try
        {
            completed.Invoke(null, new object[] { failed, "own" });
        }
        catch (TargetInvocationException ex) when (ex.InnerException?.Message.Contains("missing model") == true)
        {
            return;
        }
        throw new InvalidOperationException("Comfy failure must report the server's cause immediately.");
    }

    private static void ApplyRestoresPersistedImporterMetadata()
    {
        const string source = "Assets/Plugins/Reanimator/Graphics/icons.psd";
        var copy = "Assets/__ReanimatorRollback_" + Guid.NewGuid().ToString("N") + ".psd";
        AssertTrue(AssetDatabase.CopyAsset(source, copy), "Could not copy the PSD rollback fixture.");
        try
        {
            var original = File.ReadAllBytes(copy + ".meta");
            var importer = AssetImporter.GetAtPath(copy);
            AssertTrue(importer != null, "PSD fixture must have an importer.");
            var initialUserData = importer.userData;
            importer.userData = "Reanimator rollback regression";
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(copy);
            AssertFalse(original.SequenceEqual(File.ReadAllBytes(copy + ".meta")), "Fixture must persist a metadata change.");

            PsdLayerPreviewRenderer.RestoreApplyImporterMetadata(copy, original);

            AssertTrue(original.SequenceEqual(File.ReadAllBytes(copy + ".meta")), "Rollback must restore the original metadata bytes.");
            AssertTrue(AssetImporter.GetAtPath(copy).userData == initialUserData, "Retry must see the restored importer state.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(copy);
        }
    }

    private static void PreviewLinesExcludeStaleScreenCoordinates()
    {
        var previous = PsdLayerPreviewRenderer.GetPreviewLineIndices(8);
        var current = PsdLayerPreviewRenderer.GetPreviewLineIndices(4);
        AssertArray(new[] { 0, 1, 2, 3 }, current,
            "After filtering, only the two freshly projected line pairs can be submitted.");
        AssertEqual(8, previous.Length, "A smaller redraw must not truncate the cached larger draw.");
        AssertTrue(ReferenceEquals(current, PsdLayerPreviewRenderer.GetPreviewLineIndices(4)),
            "Repeated redraws of the same edge count should reuse the index array.");
    }

    private static void SpriteDirtyStateSurvivesUnitySerialization()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("undo-sprite", 205, Vector2.zero);
        layer.spriteGuid = "cccccccccccccccccccccccccccccccc";
        layer.sourceRect = new Rect(0f, 0f, 32f, 48f);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        var before = JsonUtility.ToJson(renderer);
        AssertTrue(renderer.SetLayerSpriteRect(layer, new Rect(0f, 0f, 28f, 48f)), "Sprite edit must change its rect.");
        var after = JsonUtility.ToJson(renderer);

        JsonUtility.FromJsonOverwrite(before, renderer);
        AssertFalse((bool)InvokePrivateMethod(renderer, "HasDirtyLayerSpriteState"), "Undo must clear the restored dirty state.");
        JsonUtility.FromJsonOverwrite(after, renderer);
        AssertTrue((bool)InvokePrivateMethod(renderer, "HasDirtyLayerSpriteState"), "Redo must restore the dirty state needed by Apply.");
    }

    private static void RestorePoseCapturesPreviousBoneTransform()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var bone = new PsdLayerPreviewRenderer.BoneInfo
        {
            parentId = -1,
            position = new Vector2(4f, 5f),
            rotation = Quaternion.identity,
            initialRotation = Quaternion.identity
        };
        var original = bone.position;
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo> { bone });
        var recorded = 0;
        renderer.UndoRequest = label =>
        {
            AssertEqual("Restore Pose", label, "Restore should add one labeled Unity Undo entry.");
            AssertEqual(original, renderer.Bones[0].position, "Undo must capture the pose before restoring it.");
            recorded++;
        };

        AssertTrue(renderer.RestorePose(), "Restore must change the bone to its initial pose.");
        AssertEqual(Vector2.zero, renderer.Bones[0].position, "Restore should apply the initial pose.");
        AssertEqual(1, recorded, "Restore must record exactly one snapshot.");
    }

    private static void GeometrySnapshotSupersedesDeferredElbowPoints()
    {
        const string path = "Assets/undo-elbow.psd";
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("elbow", 204, Vector2.zero);
        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var stableKey = (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[] { Vector2.zero, Vector2.right, Vector2.up });
        SetPrivateField(data, "vertexUvs", new[] { Vector2.zero, Vector2.right, Vector2.up });
        SetPrivateField(data, "triangles", new[] { 0, 1, 2 });
        InvokePrivateMethod(renderer, "TryAddAppliedVertex", path, selectionKey, stableKey, Vector2.one, false);
        InvokePrivateMethod(renderer, "TryAddAppliedVertex", path, "other-selection", "other-layer", Vector2.one, false);

        InvokePrivateMethod(renderer, "UpsertAutoGeometryPatch", path, selectionKey, stableKey, data, false, null);

        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Geometry must have a snapshot for Redo.");
        AssertFalse((bool)InvokePrivateMethod(renderer, "HasAppliedVerticesForLayer", path, selectionKey, stableKey),
            "Redo must not reinsert superseded Elbow points into the snapshot.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "HasAppliedVerticesForLayer", path, "other-selection", "other-layer"),
            "A geometry edit must preserve deferred points on a different layer.");
    }

    private static List<Case> BuildCases()
    {
        return new List<Case>
        {
            new Case("Geometry snapshot supersedes deferred Elbow points", GeometrySnapshotSupersedesDeferredElbowPoints),
            new Case("Restore Pose captures previous bone transform", RestorePoseCapturesPreviousBoneTransform),
            new Case("Sprite dirty state survives Unity serialization", SpriteDirtyStateSurvivesUnitySerialization),
            new Case("Preview lines exclude stale screen coordinates", PreviewLinesExcludeStaleScreenCoordinates),
            new Case("Auto Skeleton scopes outputs and detects Comfy errors", AutoSkeletonScopesOutputsAndDetectsErrors),
            new Case("Apply restores persisted PSD importer metadata", ApplyRestoresPersistedImporterMetadata),
            new Case("VolumeCurve state is serializable and editable", VolumeCurveStateIsSerializableAndEditable),
            new Case("VolumeCurve selection and deletion stays clamped", VolumeCurveSelectionAndDeletionStaysClamped),
            new Case("VolumeCurve algorithms evaluate segment types", VolumeCurveAlgorithmsEvaluateSegmentTypes),
            new Case("VolumeCurve algorithms build open and closed draw points", VolumeCurveAlgorithmsBuildOpenAndClosedDrawPoints),
            new Case("VolumeCurve algorithms hit-test points and curves", VolumeCurveAlgorithmsHitTestPointsAndCurves),
            new Case("VolumeCurve algorithms hit-test insert segments", VolumeCurveAlgorithmsHitTestInsertSegments),
            new Case("VolumeCurve algorithms evaluate influence by radius and falloff", VolumeCurveAlgorithmsEvaluateInfluenceByRadiusAndFalloff),
            new Case("VolumeCurve algorithms apply weights to target bone", VolumeCurveAlgorithmsApplyWeightsToTargetBone),
            new Case("VolumeCurve inserts and deletes exact points", VolumeCurveInsertsAndDeletesExactPoints),
            new Case("VolumeCurve presets clone and load from assets safely", VolumeCurvePresetsCloneAndLoadFromAssetsSafely),
            new Case("VolumeCurve presets default to subtle influence", VolumeCurvePresetsDefaultToSubtleInfluence),
            new Case("VolumeCurve drag movement uses one undo group", VolumeCurveDragMovementUsesOneUndoGroup),
            new Case("VolumeCurve radius falloff works on dense and sparse meshes", VolumeCurveRadiusFalloffWorksOnDenseAndSparseMeshes),
            new Case("VolumeCurve apply dirties selected layer only", VolumeCurveApplyDirtiesSelectedLayerOnly),
            new Case("VolumeCurve apply appends missing target bone", VolumeCurveApplyAppendsMissingTargetBone),
            new Case("VolumeCurve apply converts sprite bind poses before target append", VolumeCurveApplyConvertsSpriteBindPosesBeforeTargetAppend),
            new Case("VolumeCurve apply converts sprite bind poses for existing target", VolumeCurveApplyConvertsSpriteBindPosesForExistingTarget),
            new Case("VolumeCurve apply restores preview pose before weights", VolumeCurveApplyRestoresPreviewPoseBeforeWeights),
            new Case("Coordinate mapper document/view round trip is stable", CoordinateMapperRoundTripIsStable),
            new Case("Coordinate mapper maps sprite sheet geometry through atlas rect", CoordinateMapperMapsSpriteSheetGeometryThroughAtlasRect),
            new Case("Coordinate mapper ignores stale imported texture positions", CoordinateMapperIgnoresStaleImportedTexturePositions),
            new Case("Coordinate mapper trusts PSD layer size over stale sprite rect size", CoordinateMapperTrustsPsdLayerSizeOverStaleSpriteRectSize),
            new Case("Geometry preview transform maps sprite vertices", GeometryPreviewTransformMapsSpriteVertices),
            new Case("Geometry preview transform maps sprite UVs", GeometryPreviewTransformMapsSpriteUvs),
            new Case("Geometry preview transform maps importer interpretations", GeometryPreviewTransformMapsImporterInterpretations),
            new Case("Skinning algorithms keep sprite bind pose space stable", SkinningAlgorithmsKeepSpriteBindPoseSpaceStable),
            new Case("Skinning algorithms normalize imported bone rotations", SkinningAlgorithmsNormalizeImportedBoneRotations),
            new Case("Skinning algorithms reject non-finite weights", SkinningAlgorithmsRejectNonFiniteWeights),
            new Case("Skinning import maps generated bone names by renderer index", SkinningImportMapsGeneratedBoneNamesByRendererIndex),
            new Case("Skinning import preserves exact names across flattened sprite hierarchy", SkinningImportNeverOverwritesExactNamesForFlattenedSpriteHierarchy),
            new Case("Importer geometry preview prefers importer skinning over Sprite skinning", ImporterGeometryPreviewPrefersImporterSkinningOverSpriteSkinning),
            new Case("PreviewNavigator center preserves target point at rect center", PreviewNavigatorCenterPreservesTarget),
            new Case("Reanimator preview modes keep separate navigation", ReanimatorPreviewModesKeepSeparateNavigation),
            new Case("Reanimator Comfimator mode clears selected sprite", ReanimatorComfimatorModeClearsSelectedSprite),
            new Case("Bone shape drawer radii scale with thickness", BoneShapeDrawerRadiiScaleWithThickness),
            new Case("Bone shape drawer hit-tests base body and tip", BoneShapeDrawerHitTestsBaseBodyAndTip),
            new Case("Bone shape drawer detects child-parent tip hover", BoneShapeDrawerDetectsChildParentTipHover),
            new Case("Bone shape drawer falls back for short bones", BoneShapeDrawerFallsBackForShortBones),
            new Case("Limb elbow algorithms normalize two-bone weights", LimbElbowAlgorithmsNormalizeTwoBoneWeights),
            new Case("Limb weights use bone sides and stay half on guide line", LimbWeightsUseBoneSidesAndStayHalfOnGuideLine),
            new Case("Limb guide segment includes merged geometry points", LimbGuideSegmentIncludesMergedGeometryPoints),
            new Case("Limb contour replacement uses generated vertices", LimbContourReplacementUsesGeneratedVertices),
            new Case("Limb elbow algorithms build linear interior points", LimbElbowAlgorithmsBuildLinearInteriorPoints),
            new Case("Limb elbow algorithms build signed arc points", LimbElbowAlgorithmsBuildSignedArcPoints),
            new Case("Limb elbow algorithms resolve circle and sweep", LimbElbowAlgorithmsResolveCircleAndSweep),
            new Case("Limb bone resolution skips nearest bone without pair", LimbBoneResolutionSkipsNearestBoneWithoutPair),
            new Case("Limb bone resolution uses contour midpoint instead of first side", LimbBoneResolutionUsesContourMidpointInsteadOfFirstSide),
            new Case("Limb bone resolution prefers the child joint over adjacent segment", LimbBoneResolutionPrefersChildJointOverAdjacentSegment),
            new Case("Limb guide segments stay on resolved bone pair", LimbGuideSegmentsStayOnResolvedBonePair),
            new Case("Limb apply fails when no vertices are created", LimbApplyFailsWhenNoVerticesAreCreated),
            new Case("Limb apply creates pending geometry patch", LimbApplyCreatesPendingGeometryPatch),
            new Case("Auto weights default limb joints apply existing pairs", AutoWeightsDefaultLimbJointsApplyExistingPairs),
            new Case("Arm auto Limbs resolve indexed arm prefixes", ArmAutoLimbsResolveIndexedArmPrefixes),
            new Case("Arm auto weights isolate both arm pairs", ArmAutoWeightsIsolateBothArmPairs),
            new Case("Auto limb guide follows the two-bone bisector normal", AutoLimbGuideFollowsTwoBoneBisectorNormal),
            new Case("Limb apply with CircleClean creates pending geometry patch", LimbApplyWithCircleCleanCreatesPendingGeometryPatch),
            new Case("Limb apply falls back when CircleClean cannot clean", LimbApplyFallsBackWhenCircleCleanCannotClean),
            new Case("WA resolves generated PSD importer IDs", PsdArmNamesResolveGeneratedImporterIds),
            new Case("Rebuild meta algorithms build stable hierarchy keys", RebuildMetaAlgorithmsBuildStableHierarchyKeys),
            new Case("Rebuild meta algorithms choose meta sprite positions safely", RebuildMetaAlgorithmsChooseMetaSpritePositionsSafely),
            new Case("Rebuild meta algorithms prefer visual sprite position over character part bounds", RebuildMetaAlgorithmsPreferVisualSpritePositionOverCharacterPartBounds),
            new Case("Rebuild meta algorithms parse sprite guid and position", RebuildMetaAlgorithmsParseSpriteGuidAndPosition),
            new Case("Rebuild meta algorithms parse meta sprite position map", RebuildMetaAlgorithmsParseMetaSpritePositionMap),
            new Case("Asset change algorithms normalize sprite rects", AssetChangeAlgorithmsNormalizeSpriteRects),
            new Case("Asset change algorithms skip atlas sync during normal apply", AssetChangeAlgorithmsSkipAtlasSyncDuringNormalApply),
            new Case("Asset change algorithms sync data provider sprite rects for rect only changes", AssetChangeAlgorithmsSyncDataProviderSpriteRectsForRectOnlyChanges),
            new Case("Apply syncs sprite bone names from CharacterData by guid", ApplySyncsSpriteBoneNamesFromCharacterDataByGuid),
            new Case("Apply pipeline uses single SpriteDataProvider flush", ApplyPipelineUsesSingleSpriteDataProviderFlush),
            new Case("Immediate tools use shared DataProvider commit", ImmediateToolsUseSharedDataProviderCommit),
            new Case("Bone utility meta writes use shared persistence", BoneUtilityMetaWritesUseSharedPersistence),
            new Case("Meta persistence preserves newline style", MetaPersistencePreservesNewlineStyle),
            new Case("Repair PSD Params uses single persist after runtime geometry apply", RepairPsdParamsUsesSinglePersistAfterRuntimeGeometryApply),
            new Case("Asset change algorithms run sprite sheet geometry repair only for stale state", AssetChangeAlgorithmsRunSpriteSheetGeometryRepairOnlyForStaleState),
            new Case("Asset change algorithms build character part rect from sprite import state", AssetChangeAlgorithmsBuildCharacterPartRectFromSpriteImportState),
            new Case("PSD importer auto repair runs on first asset open", PsdImporterAutoRepairRunsOnFirstAssetOpen),
            new Case("Importer geometry algorithms repair runtime unit data", ImporterGeometryAlgorithmsRepairRuntimeUnitData),
            new Case("Importer geometry algorithms validate writable mesh bounds", ImporterGeometryAlgorithmsValidateWritableMeshBounds),
            new Case("Auto geometry writes raw local importer data by default", AutoGeometryWritesRawLocalImporterDataByDefault),
            new Case("Importer geometry prefers canonical raw local over alpha-flipped local", ImporterGeometryPrefersCanonicalRawLocalOverAlphaFlippedLocal),
            new Case("Auto geometry preserves flipped importer Y mode", AutoGeometryPreservesFlippedImporterYMode),
            new Case("Asset change algorithms force default geometry raw local importer mode", AssetChangeAlgorithmsForceDefaultGeometryRawLocalImporterMode),
            new Case("Sprite rect changes keep selected layer stable", SpriteRectChangesKeepSelectedLayerStable),
            new Case("Sprite sheet rect drag undo registers once", SpriteSheetRectDragUndoRegistersOnce),
            new Case("Sprite sheet geometry optimize fits rect to vertices", SpriteSheetGeometryOptimizeFitsRectToVertices),
            new Case("Sprite sheet optimize snaps shifted rect to sprite pixels", SpriteSheetOptimizeSnapsShiftedRectToSpritePixels),
            new Case("Sprite sheet optimize apply state preserves skinning layout", SpriteSheetOptimizeApplyStatePreservesSkinningLayout),
            new Case("Sprite sheet optimize then default geometry keeps sprite stable", SpriteSheetOptimizeThenDefaultGeometryKeepsSpriteStable),
            new Case("Sprite sheet optimize targets selected sprite or all", SpriteSheetOptimizeTargetsSelectedSpriteOrAll),
            new Case("Sprite sheet optimize all preserves logical atlas scale", SpriteSheetOptimizeAllPreservesLogicalAtlasScale),
            new Case("Sprite sheet default geometry restores imported selected only", SpriteSheetDefaultGeometryRestoresImportedSelectedOnly),
            new Case("Sprite sheet default geometry targets selected sprite or all", SpriteSheetDefaultGeometryTargetsSelectedSpriteOrAll),
            new Case("Sprite sheet default geometry uses current sprite rect", SpriteSheetDefaultGeometryUsesCurrentSpriteRect),
            new Case("Sprite sheet auto geometry triangulates disconnected outlines separately", SpriteSheetAutoGeometryTriangulatesDisconnectedOutlinesSeparately),
            new Case("Sprite sheet auto geometry rejects partial outline triangulation", SpriteSheetAutoGeometryRejectsPartialOutlineTriangulation),
            new Case("Sprite sheet default geometry outline points stay layer local", SpriteSheetDefaultGeometryOutlinePointsStayLayerLocal),
            new Case("Sprite sheet default geometry uses one outline space for multiple islands", SpriteSheetDefaultGeometryUsesOneOutlineSpaceForMultipleIslands),
            new Case("Sprite sheet default geometry clamps generated outline to rect", SpriteSheetDefaultGeometryClampsGeneratedOutlineToRect),
            new Case("Sprite sheet default geometry keys snapshots by sprite guid", SpriteSheetDefaultGeometryKeysSnapshotsBySpriteGuid),
            new Case("Sprite sheet geometry selection filters visible geometry", SpriteSheetGeometrySelectionFiltersVisibleGeometry),
            new Case("Sprite sheet data provider sprite rect syncs optimized state", SpriteSheetDataProviderSpriteRectSyncsOptimizedState),
            new Case("Sprite sheet importer atlas repair syncs optimized rect", SpriteSheetImporterUvTransformSyncsOptimizedRect),
            new Case("Sprite sheet importer atlas repair syncs PSD layer cache", SpriteSheetImporterAtlasRepairSyncsPsdLayerCache),
            new Case("Sprite sheet Apply preserves PSD layer mosaic anchor", SpriteSheetApplyPreservesPsdLayerMosaicAnchor),
            new Case("Sprite sheet importer repair requests PSD reslice", SpriteSheetImporterRepairRequestsPsdReslice),
            new Case("Sprite sheet importer repair syncs character data after reslice", SpriteSheetImporterRepairSyncsCharacterDataAfterReslice),
            new Case("Sprite list resolver skips empty mosaic data", SpriteListResolverSkipsEmptyMosaicData),
            new Case("Sprite list resolver uses layered sprite import data", SpriteListResolverUsesLayeredSpriteImportData),
            new Case("Sprite list resolver prefers layered over stale legacy data", SpriteListResolverPrefersLayeredOverStaleLegacyData),
            new Case("Old editor metadata stage mirrors layered sprite weights to legacy lists", OldEditorMetadataStageMirrorsLayeredSpriteWeightsToLegacyLists),
            new Case("Old editor metadata stage preserves sprite import placeholder", OldEditorMetadataStagePreservesSpriteImportPlaceholder),
            new Case("Layer sprite binding resolver prefers sprite guid", LayerSpriteBindingResolverPrefersSpriteGuid),
            new Case("Dirty layer lookup ignores name fallback", DirtyLayerLookupIgnoresNameFallback),
            new Case("Asset change algorithms detect external asset change", AssetChangeAlgorithmsDetectExternalAssetChange),
            new Case("Project house interactions rebuild finds sprite layers", ProjectHouseInteractionsRebuildFindsSpriteLayers),
            new Case("PSD post import sync keeps optimized sprite import data", PsdPostImportSyncKeepsOptimizedSpriteImportData),
            new Case("Sprite apply tracks dirty layer state only", SpriteApplyTracksDirtyLayerStateOnly),
            new Case("Position apply preserves sprite rect metadata", PositionApplyPreservesSpriteRectMetadata),
            new Case("Apply preserves UI visibility after reimport", ApplyPreservesUiVisibilityAfterReimport),
            new Case("Bone create waits for top Apply", BoneCreateWaitsForTopApply),
            new Case("Project head sprite data provider keeps optimized rect", ProjectHeadSpriteDataProviderKeepsOptimizedRect),
            new Case("Project head rebuild keeps optimized rect", ProjectHeadRebuildKeepsOptimizedRect),
            new Case("Project head optimize apply persists rect", ProjectHeadOptimizeApplyPersistsRect),
            new Case("Project new PSD layer optimize apply persists rect", ProjectNewPsdLayerOptimizeApplyPersistsRect),
            new Case("Project PSD layer repair scales geometry with rect", ProjectPsdLayerRepairScalesGeometryWithRect),
            new Case("Project optimize all apply persists every sprite rect", ProjectOptimizeAllApplyPersistsEverySpriteRect),
            new Case("Project selected PSD layer optimize is idempotent", ProjectSelectedPsdLayerOptimizeIsIdempotent),
            new Case("Project hoodie arm default geometry covers sprite", ProjectHoodieArmDefaultGeometryCoversSprite),
            new Case("Project Dance girl ALLIN geometry avoids giant triangles", ProjectDanceGirlAllInGeometryAvoidsGiantTriangles),
            new Case("Project head apply does not leave meta timestamp mismatch", ProjectHeadApplyDoesNotLeaveMetaTimestampMismatch),
            new Case("Importer metadata save and reimport does not leave timestamp mismatch", ImporterMetadataSaveAndReimportDoesNotLeaveTimestampMismatch),
            new Case("SofaTD repair generate optimize default does not leave meta timestamp mismatch", SofaTdRepairGenerateOptimizeDefaultDoesNotLeaveMetaTimestampMismatch),
            new Case("Release PSD fixtures round trip through open edit undo apply reimport", ReleasePsdFixturesRoundTripThroughOpenEditUndoApplyReimport),
            new Case("Release fixture matrix covers real asset variety", ReleaseFixtureMatrixCoversRealAssetVariety),
            new Case("Release tool matrix covers every production tool scenario", ReleaseToolMatrixCoversEveryProductionToolScenario),
            new Case("Asset change algorithms write rect and vector fields", AssetChangeAlgorithmsWriteRectAndVectorFields),
            new Case("Asset change algorithms write integer fields only when changed", AssetChangeAlgorithmsWriteIntegerFieldsOnlyWhenChanged),
            new Case("VertexCull clusters remap topology safely", VertexCullClustersRemapTopologySafely),
            new Case("VertexCull weight merge keeps normalized top four", VertexCullWeightMergeKeepsNormalizedTopFour),
            new Case("RadiusCull masks contour and inner vertices separately", RadiusCullMasksContourAndInnerVerticesSeparately),
            new Case("Curve weights preserve other influence capacity", CurveWeightsPreserveOtherInfluenceCapacity),
            new Case("EditGeometry selection normalize and remap is stable", EditGeometrySelectionNormalizeAndRemapIsStable),
            new Case("EditGeometry delete remaps triangles and boundary safely", EditGeometryDeleteRemapsTrianglesAndBoundarySafely),
            new Case("Projector candidate preview filter removes offscreen sprites", ProjectorCandidatePreviewFilterRemovesOffscreenSprites),
            new Case("Projector candidate row hover detects preview target", ProjectorCandidateRowHoverDetectsPreviewTarget),
            new Case("Projector explicit target filters intersecting clothing sources", ProjectorExplicitTargetFiltersIntersectingClothingSources),
            new Case("Projector rejects document-space distant layer geometry", ProjectorRejectsDocumentSpaceDistantLayerGeometry),
            new Case("Projector clear target weights removes old skinning data", ProjectorClearTargetWeightsRemovesOldSkinningData),
            new Case("Projector clear target weights keeps remapped root chain", ProjectorClearTargetWeightsKeepsRemappedRootChain),
            new Case("Projector apply does not repair uncovered vertices with target bones", ProjectorApplyDoesNotRepairUncoveredVerticesWithTargetBones),
            new Case("Projector apply does not pull nearest source triangle into uncovered vertices", ProjectorApplyDoesNotPullNearestSourceTriangleIntoUncoveredVertices),
            new Case("Projector expanded coverage post process fills missing vertices", ProjectorExpandedCoveragePostProcessFillsMissingVertices),
            new Case("Projector preserves explicit zero-weight key bones", ProjectorPreservesExplicitZeroWeightKeyBones),
            new Case("Projector clear target weights writes only source bones", ProjectorClearTargetWeightsWritesOnlySourceBones),
            new Case("Apply compacts unused sprite bones before importer write", ApplyCompactsUnusedSpriteBonesBeforeImporterWrite),
            new Case("Apply rebuilds compacted sprite bone slot from renderer mapping", ApplyRebuildsCompactedSpriteBoneSlotFromRendererMapping),
            new Case("Apply treats zero weights as no writable skinning", ApplyTreatsZeroWeightsAsNoWritableSkinning),
            new Case("Importer writer preserves appended vertex bone weights", ImporterWriterPreservesAppendedVertexBoneWeights),
            new Case("Projector apply uses pending geometry snapshot", ProjectorApplyUsesPendingGeometrySnapshot),
            new Case("Projector apply resolves target binding by sprite guid", ProjectorApplyResolvesTargetBindingBySpriteGuid),
            new Case("Projector stretch dedupes points and builds boundary", ProjectorStretchDedupesPointsAndBuildsBoundary),
            new Case("Projector APL preserves A-derived contour and transfers nearest source weights", ProjectorAplUnionsContoursAndTransfersNearestSourceWeights),
            new Case("Fashionable maps CL folders to exact APL targets", FashionableMapsClFoldersToExactAplTargets),
            new Case("Triangulation handles concave polygons and winding safely", TriangulationHandlesConcavePolygonsAndWindingSafely),
            new Case("Generated geometry subdivision keeps valid triangles", GeneratedGeometrySubdivisionKeepsValidTriangles),
            new Case("Importer geometry sanitizes preferred and fallback indices", ImporterGeometrySanitizesPreferredAndFallbackIndices),
            new Case("Importer geometry maps document points through reference triangles", ImporterGeometryMapsDocumentPointsThroughReferenceTriangles),
            new Case("Vertex insertion splits a containing triangle safely", VertexInsertionSplitsContainingTriangleSafely),
            new Case("Vertex insertion splits shared edge triangles safely", VertexInsertionSplitsSharedEdgeTrianglesSafely),
            new Case("Vertex insertion updates boundary loop near edge", VertexInsertionUpdatesBoundaryLoopNearEdge),
            new Case("Weight slider builds line and triangle weights", WeightSliderBuildsLineAndTriangleWeights),
            new Case("Weight slider preview drag preserves grab offset and rejects collapsed points", WeightSliderPreviewDragPreservesGrabOffsetAndRejectsCollapsedPoints),
            new Case("Weight slider multi preview drag uses per-vertex cursor offsets", WeightSliderMultiPreviewDragUsesPerVertexCursorOffsets),
            new Case("Weight slider preview drag handles coincident and collinear maxima", WeightSliderPreviewDragHandlesCoincidentAndCollinearMaxima),
            new Case("Weight slider builds quad mean-value weights", WeightSliderBuildsQuadMeanValueWeights),
            new Case("Weight slider preserves one to four influence identities", WeightSliderPreservesOneToFourInfluenceIdentities),
            new Case("Weight slider sanitizes overweight bone weights", WeightSliderSanitizesOverweightBoneWeights),
            new Case("Weight slider stable slots keep bone identity while dragging", WeightSliderStableSlotsKeepBoneIdentityWhileDragging),
            new Case("Weight slider preview drag keeps zero-weight identities", WeightSliderPreviewDragKeepsZeroWeightIdentities),
            new Case("Weight slider preview drag keeps explicit zero-index bone at endpoint", WeightSliderPreviewDragKeepsExplicitZeroIndexBoneAtEndpoint),
            new Case("Weight slider smooths weights from topology neighbors", WeightSliderSmoothsWeightsFromTopologyNeighbors),
            new Case("Weight brush extracts active and accumulated weights", WeightBrushExtractsActiveAndAccumulatedWeights),
            new Case("Weight brush bone removal drops influence and remaps indices", WeightBrushBoneRemovalDropsInfluenceAndRemapsIndices),
            new Case("Weight brush rem can reduce single normalized bone", WeightBrushRemCanReduceSingleNormalizedBone),
            new Case("Weight brush resolves selected sprite bone without zero fallback", WeightBrushResolvesSelectedSpriteBoneWithoutZeroFallback),
            new Case("Weight brush cap limits and redistributes influences", WeightBrushCapLimitsAndRedistributesInfluences),
            new Case("Weight brush cap count serializes", WeightBrushCapCountSerializes),
            new Case("QuadNet fallback triangulates contour safely", QuadNetFallbackTriangulatesContourSafely),
            new Case("QuadNet remaps vertices and edge keys safely", QuadNetRemapsVerticesAndEdgeKeysSafely),
            new Case("QuadNet selects primary contour and classifies points", QuadNetSelectsPrimaryContourAndClassifiesPoints),
            new Case("QuadNet computes grid step from density", QuadNetComputesGridStepFromDensity),
            new Case("QuadNet preview cell snaps to generation grid", QuadNetPreviewCellSnapsToGenerationGrid),
            new Case("Auto geometry exposes Reanimax mode", AutoGeometryExposesReanimaxMode),
            new Case("Reanimax edge tracing rejects open tails", ReanimaxEdgeTracingRejectsOpenTails),
            new Case("Joint geometry resolves directed angle arcs", JointGeometryResolvesDirectedAngleArcs),
            new Case("Joint geometry resolves perimeter arcs", JointGeometryResolvesPerimeterArcs),
            new Case("Joint geometry finds nearest contour ray intersection", JointGeometryFindsNearestContourRayIntersection),
            new Case("Joint V2 calculates bone angle relative to initial pose", JointV2CalculatesBoneAngleRelativeToInitialPose),
            new Case("Joint V2 prioritizes body parent sprite", JointV2PrioritizesBodyParentSprite),
            new Case("Joint V2 ignores hidden sprite candidates", JointV2IgnoresHiddenSpriteCandidates),
            new Case("Joint V2 keeps current and parent sprites distinct", JointV2KeepsCurrentAndParentSpritesDistinct),
            new Case("Joint V2 moves dynamic base while preserving tip and children", JointV2MovesDynamicBaseWhilePreservingTipAndChildren),
            new Case("Joint V2 detects shallow overlap and offsets special anchors along contour", JointV2DetectsShallowOverlapAndOffsetsSpecialAnchorsAlongContour),
            new Case("Joint V2 rotates geometry for sample poses", JointV2RotatesGeometryForSamplePoses),
            new Case("Joint V2 regular and special weight curves serialize", JointV2WeightCurvesSerialize),
            new Case("Bone hierarchy resolves world and local transforms", BoneHierarchyResolvesWorldAndLocalTransforms),
            new Case("Bone preview transform respects visited single resolve", BonePreviewTransformRespectsVisitedSingleResolve),
            new Case("Bone preview transform resolves parent cycles defensively", BonePreviewTransformResolvesParentCyclesDefensively),
            new Case("Bone preview transform converts nested world chain to local", BonePreviewTransformConvertsNestedWorldChainToLocal),
            new Case("Pivot presets map to normalized positions", PivotPresetsMapToNormalizedPositions),
            new Case("Bone meta parses inline vectors and packed colors", BoneMetaParsesInlineVectorsAndPackedColors),
            new Case("Bone order sorts preview triangles like Unity", BoneOrderSortsPreviewTrianglesLikeUnity),
            new Case("Bone order persists in sprite and character bone z", BoneOrderPersistsInSpriteAndCharacterBoneZ),
            new Case("Bone utility extracts character bones and parts", BoneUtilityExtractsCharacterBonesAndParts),
            new Case("Bone utility parses rig sprites and maps copied parts", BoneUtilityParsesRigSpritesAndMapsCopiedParts),
            new Case("Bone utility replaces character data range", BoneUtilityReplacesCharacterDataRange),
            new Case("Bone utility deletes all bones from meta lines", BoneUtilityDeletesAllBonesFromMetaLines),
            new Case("Bone utility copies bones between meta lines", BoneUtilityCopiesBonesBetweenMetaLines),
            new Case("Serialized property write-back updates scalar and transform fields", SerializedPropertyWriteBackUpdatesScalarAndTransformFields),
            new Case("Serialized property write-back reads and writes color fields", SerializedPropertyWriteBackReadsAndWritesColorFields),
            new Case("Tool mode algorithms normalize edit geometry modes", ToolModeAlgorithmsNormalizeEditGeometryModes),
            new Case("Tool mode algorithms default empty mode to preview pose", ToolModeAlgorithmsDefaultEmptyModeToPreviewPose),
            new Case("Tool mode algorithms cancel only exited preview tools", ToolModeAlgorithmsCancelOnlyExitedPreviewTools),
            new Case("Sprite selection click opens picker only for stacked hits", SpriteSelectionClickOpensPickerOnlyForStackedHits),
            new Case("Hotkey algorithms map tool keys and characters", HotkeyAlgorithmsMapToolKeysAndCharacters),
            new Case("Hotkey algorithms map restore pose key", HotkeyAlgorithmsMapRestorePoseKey),
            new Case("Hotkey algorithms map auto geometry keys", HotkeyAlgorithmsMapAutoGeometryKeys),
            new Case("Hotkey algorithms confirm auto geometry on second press only", HotkeyAlgorithmsConfirmAutoGeometryOnSecondPressOnly),
            new Case("Hotkey algorithms filter unavailable editor events", HotkeyAlgorithmsFilterUnavailableEditorEvents),
            new Case("Hotkey Q cancels active Limbs interaction", HotkeyQCancelActiveLimbsInteraction),
            new Case("Hotkey algorithms move preview camera with WASD", HotkeyAlgorithmsMovePreviewCameraWithWasd),
            new Case("Utility kill weights clears selected sprite weights", UtilityKillWeightsClearsSelectedSpriteWeights),
            new Case("Utility kill weights module requires selected sprite", UtilityKillWeightsModuleRequiresSelectedSprite),
            new Case("Weight brush bone removal compacts selected sprite skinning", WeightBrushBoneRemovalCompactsSelectedSpriteSkinning),
            new Case("Visibility layout virtualizes rows with stable content metrics", VisibilityLayoutVirtualizesRowsWithStableContentMetrics),
            new Case("Visibility layout pins depth and color columns during horizontal scroll", VisibilityLayoutPinsDepthAndColorColumnsDuringHorizontalScroll),
            new Case("Bone hierarchy marks generated bone names", BoneHierarchyMarksGeneratedBoneNames),
            new Case("Visibility cache restores sprite state after live reload", VisibilityCacheRestoresSpriteStateAfterLiveReload),
            new Case("Visibility hierarchy shows ancestors when child is shown", VisibilityHierarchyShowsAncestorsWhenChildIsShown),
            new Case("Visibility reveal selected sprite scrolls list", VisibilityRevealSelectedSpriteScrollsList),
            new Case("Visibility reveal selected sprite expands hierarchy", VisibilityRevealSelectedSpriteExpandsHierarchy),
            new Case("Release UI hides diagnostics until debug mode", ReleaseUiHidesDiagnosticsUntilDebugMode),
            new Case("Release UI hides debug module from ordinary toolbar", ReleaseUiHidesDebugModuleFromOrdinaryToolbar),
            new Case("Release UI uses production button labels", ReleaseUiUsesProductionButtonLabels),
            new Case("PSD inspector header offers Reanimator open", PsdInspectorHeaderOffersReanimatorOpen),
            new Case("Release UI maps production modules to custom icons", ReleaseUiMapsProductionModulesToCustomIcons),
            new Case("Release UI icon sheet uses current PSD importer metadata", ReleaseUiIconSheetUsesCurrentPsdImporterMetadata),
            new Case("Naming indicator loads current PSD importer metadata", NamingIndicatorLoadsCurrentPsdImporterMetadata),
            new Case("Naming indicator reports swapped limbs and duplicate names", NamingIndicatorReportsSwappedLimbsAndDuplicateNames),
            new Case("Auto Skeleton merges a second visible arm pair", AutoSkeletonMergesSecondArmPair),
            new Case("Preview always sets linear gamma shader uniform", PreviewAlwaysSetsLinearGammaShaderUniform),
            new Case("Reanimator undo isolates consecutive operations", ReanimatorUndoIsolatesConsecutiveOperations),
            new Case("Reanimator bone rename undo steps one rename at a time", ReanimatorBoneRenameUndoStepsOneRenameAtATime),
            new Case("Reanimator window Unity Undo restores renderer state", ReanimatorWindowUnityUndoRestoresRendererState),
            new Case("Joint V2 Apply undo restores bone and geometry patch together", JointV2ApplyUndoRestoresBoneAndGeometryPatchTogether),
            new Case("Reanimator window restores preview pose before bone edit", ReanimatorWindowRestoresPreviewPoseBeforeBoneEdit),
            new Case("Reanimator window commits bone edit pose on tool exit", ReanimatorWindowCommitsBoneEditPoseOnToolExit),
            new Case("Reanimator window commits bone edit pose before apply", ReanimatorWindowCommitsBoneEditPoseBeforeApply),
            new Case("Reanimator restore does not revert bone edit rest pose", ReanimatorRestoreDoesNotRevertBoneEditRestPose),
            new Case("Production Reanimator code avoids Unity warning spam", ProductionReanimatorCodeAvoidsUnityWarningSpam),
            new Case("Production Reanimator trace logs use wrapper", ProductionReanimatorTraceLogsUseWrapper),
            new Case("Production Reanimator avoids global asset save", ProductionReanimatorAvoidsGlobalAssetSave),
            new Case("Window layout clamps right panel width safely", WindowLayoutClampsRightPanelWidthSafely),
            new Case("Window layout calculates non-overlapping panels", WindowLayoutCalculatesNonOverlappingPanels),
            new Case("Window layout preserves preview width when possible", WindowLayoutPreservesPreviewWidthWhenPossible),
            new Case("Preview toolbar buttons stay inside preview", PreviewToolbarButtonsStayInsidePreview),
            new Case("Preview draw loop routes bone input through transform flag", PreviewDrawLoopRoutesBoneInputThroughTransformFlag),
            new Case("Weight brush mouse down paints instead of selecting preview bone", WeightBrushMouseDownPaintsInsteadOfSelectingPreviewBone),
            new Case("Weight brush right click selects mapped preview bone", WeightBrushRightClickSelectsMappedPreviewBone),
            new Case("Weight brush right drag rotates mapped preview bone", WeightBrushRightDragRotatesMappedPreviewBone),
            new Case("Weight brush Alt Shift click adds bone instead of replacing", WeightBrushAltShiftClickAddsBoneInsteadOfReplacing),
            new Case("Weight brush None keeps selected preview bone for painting", WeightBrushNoneKeepsSelectedPreviewBoneForPainting),
            new Case("Weight brush color right click removes bone slot", WeightBrushColorRightClickRemovesBoneSlot),
            new Case("Weight brush add bone preserves existing slots and weights", WeightBrushAddBonePreservesExistingSlotsAndWeights),
            new Case("Weight slider disabling slot clears bone identity", WeightSliderDisablingSlotClearsBoneIdentity),
            new Case("Weight slider preview points are always enabled", WeightSliderPreviewPointsAreAlwaysEnabled),
            new Case("Weight slider rows reorder only after drag release", WeightSliderRowsReorderOnlyAfterDragRelease),
            new Case("Weight slider preview drag uses pointer grab offset", WeightSliderPreviewDragUsesPointerGrabOffset),
            new Case("Weight slider multi preview drag uses independent vertex states", WeightSliderMultiPreviewDragUsesIndependentVertexStates),
            new Case("Weight slider preview drag keeps available region at zero", WeightSliderPreviewDragKeepsAvailableRegionAtZero),
            new Case("Weight slider vertex right click smooths selection", WeightSliderVertexRightClickSmoothsSelection),
            new Case("Bone name presets resolve parent suffixes", BoneNamePresetsResolveParentSuffixes),
            new Case("Limb apply stays in Limbs mode", LimbApplyStaysInLimbsMode),
            new Case("Bone name presets sort by name", BoneNamePresetsSortByName),
            new Case("Bone name presets asset contains nude body bones", BoneNamePresetsAssetContainsNudeBodyBones),
            new Case("Preview draw options activate one tool in skinning mode", PreviewDrawOptionsActivateOneToolInSkinningMode),
            new Case("Preview draw options disable tools in sprite atlas mode", PreviewDrawOptionsDisableToolsInSpriteAtlasMode),
            new Case("Preview draw options configure radius and joint settings", PreviewDrawOptionsConfigureRadiusAndJointSettings),
            new Case("SpriteSkin prefab bone assigner targets selected object only", SpriteSkinPrefabBoneAssignerTargetsSelectedObjectOnly),
            new Case("SpriteSkin prefab bone assigner assigns multi-selection and adds missing skins", SpriteSkinPrefabBoneAssignerAssignsMultiSelectionAndAddsMissingSkins),
            new Case("SpriteSkin prefab bone assigner fills missing sprites by selection and children", SpriteSkinPrefabBoneAssignerFillsMissingSpritesBySelectionAndChildren),
            new Case("SpriteSkin prefab bone assigner requires selected SpriteRenderer", SpriteSkinPrefabBoneAssignerRequiresSelectedSpriteRenderer),
            new Case("SpriteSkin prefab bone assigner resolves sprite bones by name", SpriteSkinPrefabBoneAssignerResolvesSpriteBonesByName),
            new Case("SpriteSkin prefab bone assigner refresh preserves enabled state", SpriteSkinPrefabBoneAssignerRefreshPreservesEnabledState),
            new Case("SpriteSkin prefab bone assigner recreates skeleton", SpriteSkinPrefabBoneAssignerRecreatesSkeleton),
            new Case("SpriteSkin prefab bone assigner uses each sprite's parentId=-1 root bone", SpriteSkinPrefabBoneAssignerUsesSpriteSpecificRootBone),
            new Case("SpriteSkin prefab bone assigner scales PSD bones by pixels per unit", SpriteSkinPrefabBoneAssignerScalesPsdBonesByPixelsPerUnit),
            new Case("SpriteSkin prefab bone assigner rebinds all SpriteSkins to recreated skeleton", SpriteSkinPrefabBoneAssignerRebindsAllSpriteSkinsToRecreatedSkeleton),
            new Case("SpriteSkin prefab bone assigner reports missing sprite or bones", SpriteSkinPrefabBoneAssignerReportsMissingSpriteOrBones),
            new Case("Prefab cooker adds limb solvers for both arm pairs", PrefabCookerAddsOptionalHandLimbSolvers),
            new Case("Prefab cooker uses character document coordinates instead of packing atlas", PrefabCookerUsesCharacterDocumentCoordinatesInsteadOfPackingAtlas),
            new Case("Prefab sprite issue scanner groups sprite and skin problems", PrefabSpriteIssueScannerGroupsSpriteAndSkinProblems),
            new Case("Prefab sprite issue scanner resolves opened prefab object paths", PrefabSpriteIssueScannerResolvesOpenedPrefabObjectPaths),
            new Case("Sprite sheet repair tools expose first-tab fixers", SpriteSheetRepairToolsExposeFirstTabFixers),
            new Case("Missing geometry scan alert lights once and clears on click", MissingGeometryScanAlertLightsOnceAndClearsOnClick),
            new Case("Missing geometry scan uses generation criteria", MissingGeometryScanUsesGenerationCriteria),
            new Case("Sprite sheet missing geometry tint requires selection", SpriteSheetMissingGeometryTintRequiresSelection),
            new Case("Renderer cancel clears weight tool state", RendererCancelClearsWeightToolState),
            new Case("Renderer cancel clears geometry radius and joint state", RendererCancelClearsGeometryRadiusAndJointState),
            new Case("Renderer cancel clears curve boob and volume state", RendererCancelClearsCurveBoobAndVolumeState),
            new Case("Renderer boob appends missing breast bones", RendererBoobAppendsMissingBreastBones),
            new Case("Boob V2 manual contour is default and smooths closed stroke", BoobV2ManualContourIsDefaultAndSmoothsClosedStroke),
            new Case("FullBoobs local cull preserves prior-tool vertices outside contours", FullBoobsLocalCullPreservesPriorToolVerticesOutsideContours),
            new Case("FullBoobs repairs touching contours and crossing retained edges", FullBoobsRepairsTouchingContoursAndCrossingRetainedEdges),
            new Case("Boob V2 Apply snaps existing nipple bones to targets", BoobV2ApplySnapsExistingNippleBonesToTargets),
            new Case("Renderer bone edit setters update transforms with undo", RendererBoneEditSettersUpdateTransformsWithUndo),
            new Case("Renderer creates eyelids bone from head defaults", RendererCreatesEyelidsBoneFromHeadDefaults),
            new Case("Face alpha mask finds the largest left and right holes", FaceAlphaMaskFindsLargestLeftAndRightHoles),
            new Case("Face alpha mask rejects transparent regions open to the exterior", FaceAlphaMaskRejectsRegionsOpenToExterior),
            new Case("Face eye reverse Cull contour follows density", FaceEyeVirtualPointsFollowHoleDensity),
            new Case("Face eye duplicates and red outline use radial offsets", FaceEyePointsFollowHeadDirection),
            new Case("Face eye normals keep steep upper points upper", FaceEyeNormalsKeepSteepUpperPointsUpper),
            new Case("Face virtual eyelids skin follows bone pose", FaceVirtualEyelidsSkinFollowsBonePose),
            new Case("Face eyelids preview curve input runs from center zero to edge one", FaceEyelidsPreviewWeightsRunFromCenterToEdges),
            new Case("Face red outline strictly contains only interior points", FaceRedOutlineStrictlyContainsOnlyInteriorPoints),
            new Case("Face red outline stops every point at first collision", FaceEyeOutlineStopsEveryPointAtFirstCollision),
            new Case("Renderer bone tip resize updates length and rotation", RendererBoneTipResizeUpdatesLengthAndRotation),
            new Case("Renderer bone tip snaps to nearby bases", RendererBoneTipSnapsToNearbyBases),
            new Case("Renderer bone base snaps to nearby tips", RendererBoneBaseSnapsToNearbyTips),
            new Case("Renderer Shift base drag preserves tip", RendererShiftBaseDragPreservesTip),
            new Case("Renderer isolated bone modification preserves child branches", RendererIsolatedBoneModificationPreservesChildBranches),
            new Case("Renderer bone edit keeps sprite vertices restored", RendererBoneEditKeepsSpriteVerticesRestored),
            new Case("Preview visual mesh lookup survives volatile selection key change", PreviewVisualMeshLookupSurvivesVolatileSelectionKeyChange),
            new Case("Preview visual accepts importer UVs outside stale layer rect", PreviewVisualAcceptsImporterUvsOutsideStaleLayerRect),
            new Case("Weight slider weights initialize preview skinning", WeightSliderWeightsInitializePreviewSkinning),
            new Case("Curve weights initialize preview skinning and rotation", CurveWeightsInitializePreviewSkinningAndRotation),
            new Case("Curve graph drag uses one undo group", CurveGraphDragUsesOneUndoGroup),
            new Case("Renderer bone edit commit makes edited pose rest", RendererBoneEditCommitMakesEditedPoseRest),
            new Case("Renderer bone edit commit ignores default identity rest pose", RendererBoneEditCommitIgnoresDefaultIdentityRestPose),
            new Case("Readiness overlay reports current score", ReadinessOverlayReportsCurrentScore),
            new Case("Readiness progress is calculated from checklist", ReadinessProgressIsCalculatedFromChecklist),
            new Case("Readiness checklist is backed by self tests", ReadinessChecklistIsBackedBySelfTests),
            new Case("Reanimator plugin folder is self-contained", ReanimatorPluginFolderIsSelfContained),
            new Case("Renderer revert clears asset tool state and asset-scoped patches", RendererRevertClearsAssetToolStateAndAssetScopedPatches),
            new Case("Reanimator project change ignores active apply", ReanimatorProjectChangeIgnoresActiveApply),
            new Case("Reanimator project change preserves pending snapshot", ReanimatorProjectChangePreservesPendingSnapshot),
            new Case("Renderer pending snapshot suppresses dependency hash refresh", RendererPendingSnapshotSuppressesDependencyHashRefresh),
            new Case("Failed Apply preserves Brush Alt bone map and weights", FailedApplyPreservesBrushAltBoneMapAndWeights),
            new Case("Renderer external asset change invalidates preview cache only", RendererExternalAssetChangeInvalidatesPreviewCacheOnly),
            new Case("Incremental preview rebuild resolves PSD layer rects", IncrementalPreviewRebuildResolvesPsdLayerRects),
            new Case("Create bone hover preview requests mouse move", CreateBoneHoverPreviewRequestsMouseMove),
            new Case("Create bone snaps to bone bases", CreateBoneSnapsToBoneBases),
            new Case("Create bone base hover radius scales with bone size", CreateBoneBaseHoverRadiusScalesWithBoneSize),
            new Case("Module registry discovers required module anchors", ModuleRegistryDiscoversRequiredModuleAnchors),
            new Case("Face Eyes panel requires sprite and eyelids prerequisites", FaceEyesPanelRequiresSpriteAndEyelidsPrerequisites),
            new Case("Module registry keeps module ids unique and usable", ModuleRegistryKeepsModuleIdsUniqueAndUsable),
            new Case("Boob module second press cancels preview", BoobModuleSecondPressCancelsPreview),
            new Case("Module registry sorts modules by stable order", ModuleRegistrySortsModulesByStableOrder),
            new Case("Bone hover algorithms normalize hovered bone index", BoneHoverAlgorithmsNormalizeHoveredBoneIndex),
            new Case("Bone selection hover preview requests mouse move", BoneSelectionHoverPreviewRequestsMouseMove),
            new Case("Every active Reanimator tool mode has debug module coverage", ToolModesHaveDebugModuleCoverage),
            new Case("Debug preview modules are isolated in Debug files", DebugPreviewModulesAreIsolatedInDebugFiles)
        };
    }

    private static void FillTexture(Texture2D texture, Color32 color)
    {
        var pixels = new Color32[texture.width * texture.height];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        texture.SetPixels32(pixels);
    }

    private static void FillTextureRect(Texture2D texture, RectInt rect, Color32 color)
    {
        var xMin = Mathf.Clamp(rect.xMin, 0, texture.width);
        var xMax = Mathf.Clamp(rect.xMax, 0, texture.width);
        var yMin = Mathf.Clamp(rect.yMin, 0, texture.height);
        var yMax = Mathf.Clamp(rect.yMax, 0, texture.height);
        for (var y = yMin; y < yMax; y++)
        {
            for (var x = xMin; x < xMax; x++)
                texture.SetPixel(x, y, color);
        }
    }

    private static Vertex2DMetaData BuildImporterVertex(Vector2 position)
    {
        return new Vertex2DMetaData { position = position };
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = GetRequiredPrivateField(target, fieldName);
        return (T)field.GetValue(target);
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = GetRequiredPrivateField(target, fieldName);
        field.SetValue(target, value);
    }

    private static int GetPrivateListCount(object target, string fieldName)
    {
        return GetPrivateList(target, fieldName).Count;
    }

    private static string GetPrivateListItemAssetPath(object target, string fieldName, int index)
    {
        var item = GetPrivateList(target, fieldName)[index];
        return GetPrivateField<string>(item, "assetPath");
    }

    private static void AddPrivateNestedListItem(object target, string listFieldName, string nestedTypeName)
    {
        GetPrivateList(target, listFieldName).Add(CreatePrivateNestedInstance(target, nestedTypeName));
    }

    private static void AddPrivateAssetScopedListItem(object target, string listFieldName, string nestedTypeName, string assetPath)
    {
        var item = CreatePrivateNestedInstance(target, nestedTypeName);
        SetPrivateField(item, "assetPath", assetPath);
        GetPrivateList(target, listFieldName).Add(item);
    }

    private static System.Collections.IList GetPrivateList(object target, string fieldName)
    {
        return (System.Collections.IList)GetRequiredPrivateField(target, fieldName).GetValue(target);
    }

    private static object CreatePrivateNestedInstance(object target, string nestedTypeName)
    {
        var nestedType = target.GetType().GetNestedType(nestedTypeName, BindingFlags.NonPublic);
        if (nestedType == null)
            throw new InvalidOperationException($"Private nested type '{nestedTypeName}' not found on {target.GetType().Name}.");

        return Activator.CreateInstance(nestedType, true);
    }

    private static object InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        if (target == null)
            throw new InvalidOperationException($"Target is null for private method '{methodName}'.");

        var methods = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                             method.GetParameters().Length == (args != null ? args.Length : 0))
            .ToArray();
        var method = methods.Length == 1 ? methods[0] : null;
        if (method == null)
            throw new InvalidOperationException($"Private method '{methodName}' not found on {target.GetType().Name}.");

        return method.Invoke(target, args);
    }

    private static FieldInfo GetRequiredPrivateField(object target, string fieldName)
    {
        if (target == null)
            throw new InvalidOperationException($"Target is null for private field '{fieldName}'.");

        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException($"Private field '{fieldName}' not found on {target.GetType().Name}.");

        return field;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null);
        }
    }

    private static void AssertTriangleIndicesValid(IReadOnlyList<int> triangles, int vertexCount, string message)
    {
        AssertTrue(triangles != null, message + " should not be null.");
        AssertEqual(0, triangles.Count % 3, message + " length should be divisible by 3.");
        for (var i = 0; i < triangles.Count; i++)
            AssertTrue(triangles[i] >= 0 && triangles[i] < vertexCount, $"{message} index {i} should be in range.");
    }

    private static void AssertEdgeIndicesValid(IReadOnlyList<int> edgePairs, int vertexCount, string message)
    {
        AssertTrue(edgePairs != null, message + " should not be null.");
        AssertEqual(0, edgePairs.Count % 2, message + " length should be divisible by 2.");
        for (var i = 0; i < edgePairs.Count; i++)
            AssertTrue(edgePairs[i] >= 0 && edgePairs[i] < vertexCount, $"{message} index {i} should be in range.");
    }

    private static void AssertTrianglesNonDegenerate(IReadOnlyList<Vector2> vertices, IReadOnlyList<int> triangles, string message)
    {
        AssertTrue(vertices != null, message + " vertices should not be null.");
        AssertTrue(triangles != null, message + " triangles should not be null.");
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            var area = TriangleArea(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
            AssertTrue(area > 0.000001f, $"{message} triangle {i / 3} should not be degenerate.");
        }
    }

    private static float SumTriangleArea(IReadOnlyList<Vector2> vertices, IReadOnlyList<int> triangles)
    {
        var area = 0f;
        if (vertices == null || triangles == null)
            return area;

        for (var i = 0; i + 2 < triangles.Count; i += 3)
            area += TriangleArea(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
        return area;
    }

    private static float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
    }

    private static bool HasEdgePair(IReadOnlyList<int> edgePairs, int a, int b)
    {
        if (edgePairs == null)
            return false;

        for (var i = 0; i + 1 < edgePairs.Count; i += 2)
        {
            var e0 = edgePairs[i];
            var e1 = edgePairs[i + 1];
            if ((e0 == a && e1 == b) || (e0 == b && e1 == a))
                return true;
        }

        return false;
    }

    private static float SumWeights(IReadOnlyList<float> weights, int count)
    {
        var sum = 0f;
        if (weights == null)
            return sum;

        for (var i = 0; i < count && i < weights.Count; i++)
            sum += weights[i];
        return sum;
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * Mathf.Deg2Rad;
    }

    private static float SumWeight(BoneWeight weight)
    {
        return weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
    }

    private static float GetWeight(BoneWeight weight, int boneIndex)
    {
        var result = 0f;
        if (weight.boneIndex0 == boneIndex)
            result += weight.weight0;
        if (weight.boneIndex1 == boneIndex)
            result += weight.weight1;
        if (weight.boneIndex2 == boneIndex)
            result += weight.weight2;
        if (weight.boneIndex3 == boneIndex)
            result += weight.weight3;
        return result;
    }

    private static int CountActiveToolFlags(PsdPreviewToolOptions tools)
    {
        var count = 0;
        if (tools.AutoGeometry)
            count++;
        if (tools.Elbow)
            count++;
        if (tools.Limb)
            count++;
        if (tools.Boob)
            count++;
        if (tools.Curve)
            count++;
        if (tools.WeightSlider)
            count++;
        if (tools.CreateEdge)
            count++;
        if (tools.SplitEdge)
            count++;
        if (tools.EditGeometry)
            count++;
        if (tools.RadiusCull)
            count++;
        if (tools.Joint)
            count++;
        if (tools.BoneInfluence)
            count++;
        if (tools.SpriteInfluence)
            count++;
        return count;
    }

    private static string BuildReport(IReadOnlyList<Result> results)
    {
        var passed = results.Count(result => result.Error == null);
        var lines = new List<string> { $"Reanimator self tests: {passed}/{results.Count} passed" };
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (result.Error == null)
            {
                lines.Add($"PASS {result.Name}");
                continue;
            }

            lines.Add($"FAIL {result.Name}");
            lines.Add(result.Error.ToString());
        }

        return string.Join("\n", lines);
    }

    private static void WriteReportIfRequested(string report)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "-reanimatorTestResultPath", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = args[i + 1];
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, report);
            return;
        }
    }

    private static Event BuildHotkeyEvent(EventModifiers modifiers = EventModifiers.None)
    {
        return new Event
        {
            type = EventType.KeyDown,
            keyCode = KeyCode.Q,
            modifiers = modifiers
        };
    }

    private static void AssertHotkey(KeyCode keyCode, char character, ReanimatorToolMode expected, string message)
    {
        AssertTrue(ReanimatorHotkeyAlgorithms.TryGetToolHotkeyMode(keyCode, character, out var mode), message + " should map.");
        AssertEqual(expected, mode, message + " mode.");
    }

    private static void AssertImporterInterpretation(
        GeometryPreviewTransform transform,
        IReadOnlyList<Vertex2DMetaData> vertices,
        GeometryPreviewTransform.ImporterInterpretation interpretation,
        float pixelsPerUnit,
        Vector2 pivotPixels,
        Vector2 sourceRectOffset,
        Vector2 expectedLocal,
        Vector2 expectedWorld,
        string message)
    {
        transform.FromImporterVertices(
            vertices,
            interpretation,
            pixelsPerUnit,
            pivotPixels,
            sourceRectOffset,
            flipLocalY: false,
            flipHeight: 0f,
            out var localPixels,
            out var worldVertices);

        AssertEqual(1, localPixels.Length, message + " local count.");
        AssertEqual(1, worldVertices.Length, message + " world count.");
        AssertVector(expectedLocal, localPixels[0], 0.0001f, message + " local.");
        AssertVector(expectedWorld, worldVertices[0], 0.0001f, message + " document.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual}");
    }

    private static void AssertContains(HashSet<string> values, string expected, string message)
    {
        AssertTrue(values != null && values.Contains(expected), $"{message} should contain '{expected}'.");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static void AssertFloat(float expected, float actual, float tolerance, string message)
    {
        if (Mathf.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual} Tolerance={tolerance}");
    }

    private static void AssertArray<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    {
        AssertTrue(expected != null, message + " expected should not be null.");
        AssertTrue(actual != null, message + " actual should not be null.");
        AssertEqual(expected.Count, actual.Count, message + " length.");
        for (var i = 0; i < expected.Count; i++)
            AssertEqual(expected[i], actual[i], $"{message} item {i}.");
    }

    private static void AssertVector(Vector2 expected, Vector2 actual, float tolerance, string message)
    {
        if ((expected - actual).sqrMagnitude > tolerance * tolerance)
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual} Tolerance={tolerance}");
    }

    private static void AssertRect(Rect expected, Rect actual, float tolerance, string message)
    {
        AssertFloat(expected.x, actual.x, tolerance, message + " x.");
        AssertFloat(expected.y, actual.y, tolerance, message + " y.");
        AssertFloat(expected.width, actual.width, tolerance, message + " width.");
        AssertFloat(expected.height, actual.height, tolerance, message + " height.");
    }

    private static void AssertColor(Color expected, Color actual, float tolerance, string message)
    {
        if (Mathf.Abs(expected.r - actual.r) > tolerance ||
            Mathf.Abs(expected.g - actual.g) > tolerance ||
            Mathf.Abs(expected.b - actual.b) > tolerance ||
            Mathf.Abs(expected.a - actual.a) > tolerance)
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual} Tolerance={tolerance}");
    }

    private static void AssertQuaternion(Quaternion expected, Quaternion actual, float toleranceDegrees, string message)
    {
        var angle = Quaternion.Angle(expected, actual);
        if (angle > toleranceDegrees)
            throw new InvalidOperationException($"{message} Expected={expected.eulerAngles} Actual={actual.eulerAngles} Angle={angle} Tolerance={toleranceDegrees}");
    }
}
#endif
