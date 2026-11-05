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
    private static void AssetChangeAlgorithmsNormalizeSpriteRects()
    {
        var flipped = ReanimatorAssetChangeAlgorithms.NormalizeSpriteRect(new Rect(10f, 20f, -4f, -6f));
        AssertFloat(6f, flipped.x, 0.0001f, "Negative width should shift x.");
        AssertFloat(14f, flipped.y, 0.0001f, "Negative height should shift y.");
        AssertFloat(4f, flipped.width, 0.0001f, "Negative width should become positive.");
        AssertFloat(6f, flipped.height, 0.0001f, "Negative height should become positive.");

        var clamped = ReanimatorAssetChangeAlgorithms.NormalizeSpriteRect(new Rect(1f, 2f, 0f, 0.25f));
        AssertFloat(1f, clamped.width, 0.0001f, "Width should clamp to at least one pixel.");
        AssertFloat(1f, clamped.height, 0.0001f, "Height should clamp to at least one pixel.");
        AssertTrue(ReanimatorAssetChangeAlgorithms.RectsRoughlyEqual(new Rect(1f, 2f, 1f, 1f), clamped), "Normalized rect rough compare.");
    }

    private static void AssetChangeAlgorithmsSkipAtlasSyncDuringNormalApply()
    {
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldReimportAfterPostAtlasSync(hasDataProviderChanges: false),
            "Normal Apply should not run a post-import atlas cache write.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldReimportAfterPostAtlasSync(hasDataProviderChanges: true),
            "SpriteDataProvider changes should persist through the provider/importer, not a post-import atlas cache write.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldSyncAtlasBeforeMainReimport(hasPendingMeshWork: false),
            "Normal Apply should not write atlas cache fields before the main reimport.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldSyncAtlasBeforeMainReimport(hasPendingMeshWork: true),
            "Apply after Optimize/Default Geometry should leave atlas cache fields under PSDImporter control.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldWritePreviewStateToImporter(hasPendingChanges: false, hasPendingMeshWork: false),
            "Apply with no pending edits should not write stale preview rects or positions back to the importer.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.ShouldWritePreviewStateToImporter(hasPendingChanges: true, hasPendingMeshWork: false),
            "Apply should write preview state when the user has pending non-mesh edits.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.ShouldWritePreviewStateToImporter(hasPendingChanges: false, hasPendingMeshWork: true),
            "Apply should write preview state when pending mesh work needs matching sprite rect data.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldApplyDataProviderBeforeSerializedPreviewState(hasPendingMeshWork: true, hasDataProviderSpriteChanges: false),
            "Apply after Optimize/Default Geometry should stage SpriteDataProvider data and commit it once at the end.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldApplyDataProviderBeforeSerializedPreviewState(hasPendingMeshWork: false, hasDataProviderSpriteChanges: false),
            "Apply without data provider changes does not need the SpriteDataProvider-before-serialized ordering.");
    }

    private static void AssetChangeAlgorithmsDetectExternalAssetChange()
    {
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.HasSourceAssetChangedSinceSnapshot(
                "Assets/A.psd",
                "Assets/A.psd",
                string.Empty,
                "hash-b"),
            "Missing snapshot hash should not block initial Apply.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.HasSourceAssetChangedSinceSnapshot(
                "Assets/A.psd",
                "Assets/B.psd",
                "hash-a",
                "hash-b"),
            "Path switches should be handled as a new snapshot, not a stale overwrite conflict.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.HasSourceAssetChangedSinceSnapshot(
                "Assets/A.psd",
                "Assets/A.psd",
                "hash-a",
                "hash-a"),
            "Unchanged dependency hash should allow Apply.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.HasSourceAssetChangedSinceSnapshot(
                "Assets/A.psd",
                "Assets/A.psd",
                "hash-a",
                "hash-b"),
            "Changed dependency hash should block silent Apply.");
    }

    private static void PsdImporterAutoRepairRunsOnFirstAssetOpen()
    {
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.TryDetectUniformPsdLayerResize(
                new[]
                {
                    new Rect(10f, 20f, 100f, 80f),
                    new Rect(220f, 40f, 50f, 60f),
                    new Rect(300f, 90f, 30f, 40f)
                },
                new[]
                {
                    new Rect(20f, 40f, 200f, 160f),
                    new Rect(440f, 80f, 100f, 120f),
                    new Rect(600f, 180f, 60f, 80f)
                },
                out var scale,
                out var changed,
                out var comparable),
            "First open after an external 2x PSD edit should detect a uniform importer-local layer resize.");
        AssertVector(new Vector2(2f, 2f), scale, 0.0001f, "Detected uniform PSD resize scale.");
        AssertEqual(3, changed, "Every comparable layer should contribute to the resize signal.");
        AssertEqual(3, comparable, "Every valid layer should be comparable.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.ShouldAutoRepairPsdImporterState(hasPendingChanges: false, hasUniformLayerResize: true),
            "First open after an external PSD edit should be allowed to repair stale rect and geometry state.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldAutoRepairPsdImporterState(hasPendingChanges: true, hasUniformLayerResize: true),
            "Unsaved Reanimator edits should block automatic importer mutation.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldAutoRepairPsdImporterState(hasPendingChanges: false, hasUniformLayerResize: false),
            "Importer state without a uniform asset-local resize signal should not auto-repair.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.TryDetectUniformPsdLayerResize(
                new[]
                {
                    new Rect(10f, 20f, 100f, 80f),
                    new Rect(220f, 40f, 50f, 60f),
                    new Rect(300f, 90f, 30f, 40f)
                },
                new[]
                {
                    new Rect(20f, 40f, 200f, 160f),
                    new Rect(220f, 40f, 50f, 60f),
                    new Rect(300f, 90f, 30f, 40f)
                },
                out _,
                out _,
                out _),
            "A single stale layer inside an otherwise stable PSD should remain a manual repair case.");

        AssertTrue(
            ReanimatorAssetChangeAlgorithms.TryDetectUniformPsdLayerResize(
                new[]
                {
                    new Rect(10f, 20f, 100f, 80f),
                    new Rect(220f, 40f, 50f, 60f),
                    new Rect(300f, 90f, 30f, 40f),
                    new Rect(4f, 4f, 1024f, 1024f)
                },
                new[]
                {
                    new Rect(20f, 40f, 200f, 160f),
                    new Rect(440f, 80f, 100f, 120f),
                    new Rect(600f, 180f, 60f, 80f),
                    new Rect(4f, 4f, 2925f, 2350f)
                },
                out var robustScale,
                out var robustChanged,
                out var robustComparable),
            "Auto-repair should keep the global resize signal when Krita expands one merged/background layer bounds differently.");
        AssertVector(new Vector2(2f, 2f), robustScale, 0.0001f, "Outlier PSD bounds should not change the detected global resize scale.");
        AssertEqual(3, robustChanged, "Only the uniformly resized layers should contribute to the median scale.");
        AssertEqual(4, robustComparable, "The outlier remains comparable but should not become the repair scale.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.PsdLayerSizeMatchesUniformResize(
                new Rect(2972f, 1058f, 209f, 253f),
                new Vector2Int(418, 506),
                new Vector2(2f, 2f)),
            "A normal PSD layer whose file bounds doubled should be repaired from PSD bounds.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.PsdLayerSizeMatchesUniformResize(
                new Rect(4f, 4f, 1024f, 1024f),
                new Vector2Int(2925, 2350),
                new Vector2(2f, 2f)),
            "A Krita-expanded merged/background layer should be repaired from the detected uniform scale, not its outlier file bounds.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.PsdLayerSizeMatchesUniformResize(
                new Rect(12f, 14f, 300f, 180f),
                new Vector2Int(150, 90),
                new Vector2(0.5f, 0.5f)),
            "Downscaled PSD layer bounds should still be accepted when they match the detected scale.");

        AssertVector(
            new Vector2(1.5f, 0.5f),
            ReanimatorAssetChangeAlgorithms.CalculatePsdDocumentResizeScale(
                new Vector2Int(100, 200),
                new Vector2Int(150, 100)),
            0.0001f,
            "Auto-resize scale should preserve independent X/Y factors.");
    }

    private static void AssetChangeAlgorithmsSyncDataProviderSpriteRectsForRectOnlyChanges()
    {
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.ShouldSyncDataProviderSpriteRects(hasPendingChanges: true, hasPendingMeshWork: false),
            "Optimize can be a rect-only edit, but Skinning Editor still needs SpriteDataProvider rect sync.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldApplyDataProviderBeforeSerializedPreviewState(hasPendingMeshWork: false, hasDataProviderSpriteChanges: true),
            "Rect-only SpriteDataProvider changes should be committed once after serialized importer fields are staged.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldSyncDataProviderSpriteRects(hasPendingChanges: false, hasPendingMeshWork: false),
            "No-op Apply should not touch SpriteDataProvider rects.");
    }

    private static void ApplySyncsSpriteBoneNamesFromCharacterDataByGuid()
    {
        var characterBones = new[]
        {
            new SpriteBone { name = "r_foot", guid = "feb85b1e2a535d84498056287c342191" },
            new SpriteBone { name = "duplicate_a", guid = "11111111111111111111111111111111" },
            new SpriteBone { name = "duplicate_b", guid = "11111111111111111111111111111111" }
        };
        var spriteBones = new[]
        {
            new SpriteBone { name = "bone_23", guid = "feb85b1e2a535d84498056287c342191" },
            new SpriteBone { name = "bone_duplicate", guid = "11111111111111111111111111111111" },
            new SpriteBone { name = "bone_without_guid" }
        };

        AssertTrue(
            PsdLayerPreviewRenderer.SyncSpriteBoneNamesFromCharacterData(characterBones, spriteBones, out var syncedBones),
            "Apply should sync per-sprite bone names from CharacterData when a stable GUID matches.");
        AssertEqual("r_foot", syncedBones[0].name, "Unique GUID should receive the renamed CharacterData bone name.");
        AssertEqual("bone_duplicate", syncedBones[1].name, "Duplicate CharacterData GUID should not be used for name sync.");
        AssertEqual("bone_without_guid", syncedBones[2].name, "Bones without GUID should not be renamed.");

        AssertTrue(
            PsdLayerPreviewRenderer.SyncSpriteBoneNamesFromCharacterData(characterBones, spriteBones, new[] { 0, 2, -1 }, out var indexedSyncedBones),
            "Apply should prefer CharacterPart bone indices over GUID fallback for per-sprite bone names.");
        AssertEqual("r_foot", indexedSyncedBones[0].name, "Indexed sync should still use the selected CharacterData bone name.");
        AssertEqual("duplicate_b", indexedSyncedBones[1].name, "Indexed sync should rename even when CharacterData GUIDs are duplicated.");
        AssertEqual("bone_without_guid", indexedSyncedBones[2].name, "Invalid CharacterPart bone index should leave the sprite bone name unchanged.");
    }

    private static void ApplyPipelineUsesSingleSpriteDataProviderFlush()
    {
        var path = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.AssetChanges.ApplyPipeline.cs");
        AssertTrue(File.Exists(path), "Apply pipeline source should be available for the release contract check.");

        var source = File.ReadAllText(path);
        AssertEqual(
            1,
            CountOccurrences(source, "session.DataProvider.Apply();"),
            "Apply pipeline should commit SpriteDataProvider changes once per top-level Apply operation.");
        AssertFalse(
            source.Contains("FlushFinalDataProviderStageIfNeeded"),
            "Apply pipeline should not re-run a final SpriteDataProvider flush after serialized importer metadata is staged.");

        var normalizedSource = source.Replace("\r\n", "\n");
        AssertEqual(
            1,
            CountOccurrences(normalizedSource, "SyncSerializedSpriteBoneNamesFromCharacterData(session.Importer)"),
            "Apply pipeline should run sprite bone name sync once per top-level Apply.");
        AssertTrue(
            normalizedSource.Contains("ApplyBoneChangesToImporter(session.Importer);\n        }\n\n        session.HasImporterChanges |= SyncSerializedSpriteBoneNamesFromCharacterData(session.Importer);"),
            "Apply should repair stale per-sprite bone names even when no preview-state write is pending.");
    }

    private static void ImmediateToolsUseSharedDataProviderCommit()
    {
        var root = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator");
        AssertTrue(Directory.Exists(root), "Reanimator source root should be available for the immediate-operation contract check.");

        var directApplyOffenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
            .Where(path => !path.EndsWith("PsdLayerPreviewRenderer.AssetChanges.ApplyPipeline.cs", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("PsdLayerPreviewRenderer.AssetChanges.Persistence.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("dataProvider.Apply();"))
            .Select(path => path.Replace(Application.dataPath, "Assets").Replace('\\', '/'))
            .ToArray();

        AssertEqual(
            0,
            directApplyOffenders.Length,
            "Immediate Reanimator tools should commit SpriteDataProvider changes through the shared persistence helper. Offenders: " + string.Join(", ", directApplyOffenders));
    }

    private static void BoneUtilityMetaWritesUseSharedPersistence()
    {
        var path = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Actions/Reanimator.BoneUtilityActions.cs");
        AssertTrue(File.Exists(path), "Bone utility source should be available for the lifecycle contract check.");

        var source = File.ReadAllText(path);
        AssertFalse(source.Contains("File.WriteAllText("), "Bone utility actions should not write .meta files directly.");
        AssertFalse(source.Contains("AssetDatabase.ImportAsset("), "Bone utility actions should not reimport outside the shared meta persistence helper.");
        AssertTrue(source.Contains("ReanimatorAssetMetaPersistence.TryRewriteMetaLinesAndReimport"), "Bone utility actions should use the shared meta persistence helper.");
    }

    private static void MetaPersistencePreservesNewlineStyle()
    {
        AssertEqual("\r\n", ReanimatorAssetMetaPersistence.DetectNewLine("a: 1\r\nb: 2\r\n"), "CRLF meta files should keep CRLF writes.");
        AssertEqual("\n", ReanimatorAssetMetaPersistence.DetectNewLine("a: 1\nb: 2\n"), "LF meta files should keep LF writes.");
        AssertEqual("a\r\nb\r\n", ReanimatorAssetMetaPersistence.JoinMetaLinesForWrite(new[] { "a", "b" }, "\r\n"), "Meta write join should preserve CRLF.");
        AssertEqual("a\nb\n", ReanimatorAssetMetaPersistence.JoinMetaLinesForWrite(new[] { "a", "b" }, "\n"), "Meta write join should preserve LF.");
    }

    private static void RepairPsdParamsUsesSinglePersistAfterRuntimeGeometryApply()
    {
        var path = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.AssetChanges.Repair.cs");
        AssertTrue(File.Exists(path), "Repair source should be available for the lifecycle contract check.");

        var source = File.ReadAllText(path);
        AssertEqual(
            1,
            CountOccurrences(source, "PersistImporterMetadataAndReimport(assetPath)"),
            "Repair PSD Params should perform one importer metadata persist/reimport per changed asset.");
        AssertTrue(
            source.Contains("ApplyImmediateDataProviderChanges(importer as ISpriteEditorDataProvider"),
            "Runtime-unit geometry repair should commit SpriteDataProvider changes through the shared helper before metadata persist.");
    }

    private static void ReleaseFixtureMatrixCoversRealAssetVariety()
    {
        var path = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Tests/ReanimatorSelfTests.SpriteSheetProjectFixtures.cs");
        AssertTrue(File.Exists(path), "Release fixture source should be available for the matrix coverage check.");

        var source = File.ReadAllText(path);
        AssertTrue(
            CountOccurrences(source, "new ReleaseFixtureCase(") >= 6,
            "Release fixture matrix should cover at least six real PSD assets.");
        AssertTrue(source.Contains("alternate nude pose with expressions"), "Release fixture matrix should cover a large expression-heavy body PSD.");
        AssertTrue(source.Contains("side view duplicate names"), "Release fixture matrix should cover duplicate/nonstandard side-view layer names.");
        AssertTrue(source.Contains("compact locker pose"), "Release fixture matrix should cover a compact lower-layer-count pose PSD.");
    }

    private static void AssetChangeAlgorithmsRunSpriteSheetGeometryRepairOnlyForStaleState()
    {
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.ShouldRunSpriteSheetGeometryRepair(repairedImporterRects: false, hasMissingGeometry: false),
            "No-op PSD param repair should not regenerate authored sprite sheet geometry.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.ShouldRunSpriteSheetGeometryRepair(repairedImporterRects: true, hasMissingGeometry: false),
            "Repairing stale importer rects should trigger a sprite sheet geometry rebuild.");
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.ShouldRunSpriteSheetGeometryRepair(repairedImporterRects: false, hasMissingGeometry: true),
            "Missing geometry should trigger the same full sprite sheet recovery path.");
    }

    private static void AssetChangeAlgorithmsBuildCharacterPartRectFromSpriteImportState()
    {
        var partRect = ReanimatorAssetChangeAlgorithms.BuildCharacterPartSpritePosition(
            new Vector2(150f, 78f),
            new Rect(53f, 538f, 382f, 262f));

        AssertEqual(new RectInt(150, 78, 382, 262), partRect, "CharacterData part rect should use spritePosition for placement and PSD-sized sprite rect for dimensions.");
    }

    private static void ImporterGeometryAlgorithmsRepairRuntimeUnitData()
    {
        var rect = new Rect(225f, 250f, 91f, 71f);
        var ppu = 100f;
        var pivot = new Vector2(45.5f, 35.5f);
        var localVertices = new[]
        {
            new Vector2(10.075562f, 60.275757f),
            new Vector2(25.196655f, 52.480408f),
            new Vector2(76.22274f, 35.5f)
        };
        var unitVertices = new Vertex2DMetaData[localVertices.Length];
        for (var i = 0; i < localVertices.Length; i++)
            unitVertices[i] = BuildImporterVertex((localVertices[i] - pivot) / ppu);

        AssertTrue(
            ReanimatorImporterGeometryAlgorithms.IsLikelyRuntimeUnitImporterGeometry(unitVertices, rect, ppu, pivot),
            "Runtime unit geometry should be detected before Repair writes importer data.");

        var repairedVertices = ReanimatorImporterGeometryAlgorithms.ConvertRuntimeUnitVerticesToRawLocal(unitVertices, ppu, pivot);
        AssertVector(localVertices[0], repairedVertices[0].position, 0.0001f, "Runtime unit vertex 0 should convert back to raw local pixels.");
        AssertVector(localVertices[1], repairedVertices[1].position, 0.0001f, "Runtime unit vertex 1 should convert back to raw local pixels.");
        AssertVector(localVertices[2], repairedVertices[2].position, 0.0001f, "Runtime unit vertex 2 should convert back to raw local pixels.");

        var bones = new List<SpriteBone>
        {
            new SpriteBone
            {
                name = "root",
                parentId = -1,
                position = new Vector3(-0.35424438f, 0.24775757f, 0f),
                length = 0.33918664f
            },
            new SpriteBone
            {
                name = "child",
                parentId = 0,
                position = new Vector3(0.31026083f, 0f, 0f),
                length = 0.33527994f
            }
        };
        var renderer = new PsdLayerPreviewRenderer();
        var repairedBones = (List<SpriteBone>)InvokePrivateMethod(
            renderer,
            "ConvertRuntimeUnitBonesToRawLocal",
            bones,
            ppu,
            pivot);
        AssertVector(new Vector2(10.075562f, 60.275757f), repairedBones[0].position, 0.0001f, "Root bone unit position should include pivot when repaired.");
        AssertFloat(33.918664f, repairedBones[0].length, 0.0001f, "Root bone unit length should convert back to pixels.");
        AssertVector(new Vector2(31.026083f, 0f), repairedBones[1].position, 0.0001f, "Child bone unit position should stay parent-local when repaired.");
        AssertFloat(33.527996f, repairedBones[1].length, 0.0001f, "Child bone unit length should convert back to pixels.");

        var pixelVertices = new[]
        {
            BuildImporterVertex(new Vector2(10f, 12f)),
            BuildImporterVertex(new Vector2(30f, 14f)),
            BuildImporterVertex(new Vector2(28f, 45f))
        };
        AssertFalse(
            ReanimatorImporterGeometryAlgorithms.IsLikelyRuntimeUnitImporterGeometry(pixelVertices, rect, ppu, pivot),
            "Normal raw local pixel geometry must not be treated as corrupted runtime unit geometry.");
    }

    private static void ImporterGeometryAlgorithmsValidateWritableMeshBounds()
    {
        var rect = new Rect(225f, 250f, 91f, 71f);
        var validVertices = new[]
        {
            BuildImporterVertex(new Vector2(10f, 12f)),
            BuildImporterVertex(new Vector2(30f, 14f)),
            BuildImporterVertex(new Vector2(28f, 45f))
        };
        var indices = new[] { 0, 1, 2 };

        AssertTrue(
            ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(validVertices, indices, rect, out var validReason),
            $"Sprite-local importer mesh should be writable: {validReason}");

        var documentSpaceVertices = new[]
        {
            BuildImporterVertex(new Vector2(1010f, 2012f)),
            BuildImporterVertex(new Vector2(1030f, 2014f)),
            BuildImporterVertex(new Vector2(1028f, 2045f))
        };
        AssertFalse(
            ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(documentSpaceVertices, indices, rect, out var invalidReason),
            "Document-space importer mesh must be rejected before SetVertices.");
        AssertTrue(
            !string.IsNullOrEmpty(invalidReason) && invalidReason.Contains("outside"),
            "Rejected importer mesh should report bounds as the reason.");
    }

    private static void AutoGeometryWritesRawLocalImporterDataByDefault()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("auto-write-mode", 94, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(10f, 20f, 91f, 71f);
        layer.textureRect = layer.sourceRect;

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(10f, 12f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(30f, 14f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(28f, 45f))
        });
        SetPrivateField(data, "boneWeights", Array.Empty<BoneWeight>());

        var binding = CreatePrivateNestedInstance(renderer, "LayerSpriteBinding");
        SetPrivateField(binding, "pixelsPerUnit", 100f);
        SetPrivateField(binding, "pivotPixels", new Vector2(45.5f, 35.5f));
        SetPrivateField(binding, "rect", layer.sourceRect);

        var currentImporterVertices = new[]
        {
            BuildImporterVertex((new Vector2(10f, 12f) - new Vector2(45.5f, 35.5f)) / 100f),
            BuildImporterVertex((new Vector2(30f, 14f) - new Vector2(45.5f, 35.5f)) / 100f),
            BuildImporterVertex((new Vector2(28f, 45f) - new Vector2(45.5f, 35.5f)) / 100f)
        };
        var args = new object[]
        {
            data,
            binding,
            currentImporterVertices,
            Array.Empty<int>(),
            null,
            null
        };
        var metaVertices = (Vertex2DMetaData[])InvokePrivateMethod(renderer, "BuildAutoGeometryMetaVertices", args);

        AssertEqual("raw_local", (string)args[5], "Auto geometry writer should use raw local importer mode by default.");
        AssertVector(new Vector2(10f, 12f), metaVertices[0].position, 0.0001f, "Auto geometry vertex 0 should be written as raw local pixels.");
        AssertVector(new Vector2(30f, 14f), metaVertices[1].position, 0.0001f, "Auto geometry vertex 1 should be written as raw local pixels.");
        AssertVector(new Vector2(28f, 45f), metaVertices[2].position, 0.0001f, "Auto geometry vertex 2 should be written as raw local pixels.");
    }

    private static void ImporterWriterPreservesAppendedVertexBoneWeights()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("limb-appended-weight", 96, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(10f, 20f, 80f, 60f);
        layer.textureRect = layer.sourceRect;

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(10f, 10f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(30f, 10f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(20f, 30f))
        });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f }
        });

        var binding = CreatePrivateNestedInstance(renderer, "LayerSpriteBinding");
        SetPrivateField(binding, "pixelsPerUnit", 100f);
        SetPrivateField(binding, "pivotPixels", new Vector2(40f, 30f));
        SetPrivateField(binding, "rect", layer.sourceRect);

        var currentImporterVertices = new[]
        {
            BuildImporterVertex(new Vector2(10f, 10f)),
            BuildImporterVertex(new Vector2(30f, 10f))
        };
        currentImporterVertices[0].boneWeight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        currentImporterVertices[1].boneWeight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };

        var metaVertices = (Vertex2DMetaData[])InvokePrivateMethod(
            renderer,
            "BuildMeshMetaVertices",
            data,
            binding,
            currentImporterVertices,
            Array.Empty<int>());

        AssertEqual(3, metaVertices.Length, "Writer should include the appended Limb vertex.");
        AssertFloat(1f, GetWeight(metaVertices[2].boneWeight, 1), 0.0001f, "Appended Limb vertex should keep its generated child weight.");
        AssertFloat(0f, GetWeight(metaVertices[2].boneWeight, 0), 0.0001f, "Appended Limb vertex should not inherit nearest old parent weight.");
    }

    private static void AssetChangeAlgorithmsForceDefaultGeometryRawLocalImporterMode()
    {
        AssertTrue(
            ReanimatorAssetChangeAlgorithms.TryUseForcedAutoGeometryImporterMode(" raw_local ", out var forcedMode),
            "Forced importer mode should be accepted.");
        AssertEqual("raw_local", forcedMode, "Forced importer mode should trim whitespace.");
        AssertFalse(
            ReanimatorAssetChangeAlgorithms.TryUseForcedAutoGeometryImporterMode(null, out _),
            "Missing forced importer mode should fall back to auto-detection.");

        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("forced-default", 92, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(10f, 20f, 50f, 40f);
        layer.textureRect = layer.sourceRect;

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(3f, 4f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(30f, 7f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(8f, 33f))
        });
        SetPrivateField(data, "boneWeights", Array.Empty<BoneWeight>());

        var binding = CreatePrivateNestedInstance(renderer, "LayerSpriteBinding");
        SetPrivateField(binding, "pixelsPerUnit", 2f);
        SetPrivateField(binding, "pivotPixels", new Vector2(9f, 11f));
        SetPrivateField(binding, "rect", layer.sourceRect);

        var currentImporterVertices = new[]
        {
            BuildImporterVertex(new Vector2(999f, 888f)),
            BuildImporterVertex(new Vector2(1000f, 889f)),
            BuildImporterVertex(new Vector2(1001f, 890f))
        };
        var args = new object[]
        {
            data,
            binding,
            currentImporterVertices,
            Array.Empty<int>(),
            "raw_local",
            null
        };
        var metaVertices = (Vertex2DMetaData[])InvokePrivateMethod(renderer, "BuildAutoGeometryMetaVertices", args);

        AssertEqual(3, metaVertices.Length, "Forced raw local default geometry vertex count.");
        AssertVector(new Vector2(3f, 4f), metaVertices[0].position, 0.0001f, "Forced raw local vertex 0.");
        AssertVector(new Vector2(30f, 7f), metaVertices[1].position, 0.0001f, "Forced raw local vertex 1.");
        AssertVector(new Vector2(8f, 33f), metaVertices[2].position, 0.0001f, "Forced raw local vertex 2.");
        AssertEqual("raw_local", (string)args[5], "Forced raw local mode should be reported to bone conversion.");

        var transform = new GeometryPreviewTransform(layer);
        transform.FromImporterVertices(
            metaVertices,
            GeometryPreviewTransform.ImporterInterpretation.RawLocal,
            2f,
            new Vector2(9f, 11f),
            layer.sourceRect.position,
            flipLocalY: false,
            flipHeight: layer.sourceRect.height,
            out var reloadedLocalPixels,
            out var reloadedDocumentVertices);
        AssertEqual(3, reloadedDocumentVertices.Length, "Forced raw local round-trip vertex count.");
        AssertVector(new Vector2(3f, 4f), reloadedLocalPixels[0], 0.0001f, "Forced raw local round-trip local vertex 0.");
        AssertVector(GetPrivateField<Vector2[]>(data, "vertices")[0], reloadedDocumentVertices[0], 0.0001f, "Forced raw local round-trip document vertex 0.");
        AssertVector(GetPrivateField<Vector2[]>(data, "vertices")[1], reloadedDocumentVertices[1], 0.0001f, "Forced raw local round-trip document vertex 1.");
        AssertVector(GetPrivateField<Vector2[]>(data, "vertices")[2], reloadedDocumentVertices[2], 0.0001f, "Forced raw local round-trip document vertex 2.");
    }

    private static void AutoGeometryPreservesFlippedImporterYMode()
    {
        AssertEqual(
            "raw_local_flip_y",
            ReanimatorImporterGeometryAlgorithms.SanitizeImporterWriteMode("raw_ppu_plus_pivot_flip_y"),
            "Runtime-unit flipped importer mode should normalize units but keep the Y flip.");

        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("flipped-panty-like", 97, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(10f, 20f, 50f, 40f);
        layer.textureRect = layer.sourceRect;

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "importerMode", "raw_local_flip_y");
        SetPrivateField(data, "vertices", new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(3f, 4f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(30f, 7f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(8f, 33f))
        });
        SetPrivateField(data, "boneWeights", Array.Empty<BoneWeight>());

        var binding = CreatePrivateNestedInstance(renderer, "LayerSpriteBinding");
        SetPrivateField(binding, "pixelsPerUnit", 2f);
        SetPrivateField(binding, "pivotPixels", new Vector2(9f, 11f));
        SetPrivateField(binding, "rect", layer.sourceRect);

        var args = new object[]
        {
            data,
            binding,
            Array.Empty<Vertex2DMetaData>(),
            Array.Empty<int>(),
            null,
            null
        };
        var metaVertices = (Vertex2DMetaData[])InvokePrivateMethod(renderer, "BuildAutoGeometryMetaVertices", args);

        AssertEqual(3, metaVertices.Length, "Flipped raw local vertex count.");
        AssertEqual("raw_local_flip_y", (string)args[5], "Writer should preserve flipped raw local mode.");
        AssertVector(new Vector2(3f, 36f), metaVertices[0].position, 0.0001f, "Flipped raw local vertex 0.");
        AssertVector(new Vector2(30f, 33f), metaVertices[1].position, 0.0001f, "Flipped raw local vertex 1.");
        AssertVector(new Vector2(8f, 7f), metaVertices[2].position, 0.0001f, "Flipped raw local vertex 2.");

        var transform = new GeometryPreviewTransform(layer);
        transform.FromImporterVertices(
            metaVertices,
            GeometryPreviewTransform.ImporterInterpretation.RawLocal,
            2f,
            new Vector2(9f, 11f),
            layer.sourceRect.position,
            flipLocalY: true,
            flipHeight: layer.sourceRect.height,
            out var reloadedLocalPixels,
            out var reloadedDocumentVertices);
        AssertEqual(3, reloadedDocumentVertices.Length, "Flipped raw local round-trip vertex count.");
        AssertVector(new Vector2(3f, 4f), reloadedLocalPixels[0], 0.0001f, "Flipped raw local round-trip local vertex 0.");
        AssertVector(GetPrivateField<Vector2[]>(data, "vertices")[0], reloadedDocumentVertices[0], 0.0001f, "Flipped raw local round-trip document vertex 0.");
        AssertVector(GetPrivateField<Vector2[]>(data, "vertices")[1], reloadedDocumentVertices[1], 0.0001f, "Flipped raw local round-trip document vertex 1.");
        AssertVector(GetPrivateField<Vector2[]>(data, "vertices")[2], reloadedDocumentVertices[2], 0.0001f, "Flipped raw local round-trip document vertex 2.");
    }

    private static void ImporterGeometryPrefersCanonicalRawLocalOverAlphaFlippedLocal()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("reload-panty-like", 98, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(0f, 0f, 50f, 40f);
        layer.textureRect = layer.sourceRect;
        layer.textureSamplingRect = layer.sourceRect;

        var binding = CreatePrivateNestedInstance(renderer, "LayerSpriteBinding");
        SetPrivateField(binding, "pixelsPerUnit", 1f);
        SetPrivateField(binding, "pivotPixels", Vector2.zero);
        SetPrivateField(binding, "rect", layer.sourceRect);

        var importerVertices = new[]
        {
            BuildImporterVertex(new Vector2(3f, 4f)),
            BuildImporterVertex(new Vector2(30f, 7f)),
            BuildImporterVertex(new Vector2(8f, 33f))
        };

        var texture = new Texture2D(50, 40, TextureFormat.RGBA32, false);
        var transparent = new Color32[50 * 40];
        texture.SetPixels32(transparent);
        texture.SetPixel(3, 36, Color.white);
        texture.SetPixel(30, 33, Color.white);
        texture.SetPixel(8, 7, Color.white);
        texture.Apply(false, true);

        var args = new object[]
        {
            layer,
            binding,
            importerVertices,
            texture,
            null,
            null,
            null
        };

        InvokePrivateMethod(renderer, "SelectBestImporterVerticesInterpretation", args);
        AssertEqual("raw_local", (string)args[6], "Valid raw-local importer geometry should not reload as raw_local_flip_y.");
        var localPixels = (Vector2[])args[4];
        AssertVector(new Vector2(3f, 4f), localPixels[0], 0.0001f, "Raw-local reload local vertex 0.");
    }

}
#endif
