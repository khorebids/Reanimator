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
    private static void CoordinateMapperRoundTripIsStable()
    {
        var localRect = new Rect(0f, 0f, 400f, 300f);
        var navigator = new PreviewNavigator();
        var coordinates = new PsdCoordinateSpace(
            localRect,
            new Vector2Int(200, 100),
            Array.Empty<PsdLayerPreviewRenderer.LayerInfo>(),
            navigator);

        var samples = new[]
        {
            Vector2.zero,
            new Vector2(50f, 25f),
            new Vector2(199f, 99f),
            new Vector2(-10f, 110f)
        };

        for (var i = 0; i < samples.Length; i++)
        {
            var view = coordinates.DocumentToView(samples[i]);
            var doc = coordinates.ViewToDocument(view);
            AssertVector(samples[i], doc, 0.01f, $"Round trip sample {i}.");
        }
    }

    private static void CoordinateMapperMapsSpriteSheetGeometryThroughAtlasRect()
    {
        var localRect = new Rect(0f, 0f, 400f, 300f);
        var navigator = new PreviewNavigator();
        var layer = BuildLayer("atlas-mapping", 1001, new Vector2(326f, 646f));
        layer.sourceRect = new Rect(4f, 1306f, 341f, 309f);
        layer.textureRect = layer.sourceRect;
        var localVertex = new Vector2(40f, 50f);
        var documentVertex = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, localVertex);
        var skeletonCoordinates = new PsdCoordinateSpace(
            localRect,
            new Vector2Int(1200, 2200),
            new[] { layer },
            navigator,
            PsdPreviewLayoutMode.Skeleton);
        var atlasCoordinates = new PsdCoordinateSpace(
            localRect,
            new Vector2Int(1200, 2200),
            new[] { layer },
            navigator,
            PsdPreviewLayoutMode.SpriteAtlas);
        AssertVector(
            skeletonCoordinates.DocumentToView(documentVertex),
            skeletonCoordinates.LayerDocumentPointToView(layer, documentVertex),
            0.01f,
            "Skinning geometry should map document vertices as document points.");
        AssertVector(
            atlasCoordinates.LayerLocalPointToView(layer, localVertex),
            atlasCoordinates.LayerDocumentPointToView(layer, documentVertex),
            0.01f,
            "Sprite Sheet geometry should map document vertices through layer-local coordinates into the sprite rect.");
        AssertTrue(
            (atlasCoordinates.DocumentToView(documentVertex) - atlasCoordinates.LayerDocumentPointToView(layer, documentVertex)).sqrMagnitude > 0.01f,
            "Sprite Sheet geometry must not draw document vertices directly as atlas document points.");
    }

    private static void CoordinateMapperIgnoresStaleImportedTexturePositions()
    {
        var source = new Rect(4f, 1294f, 341f, 309f);
        var staleSameSize = new Rect(4f, 1306f, 341f, 309f);
        var staleSamePosition = new Rect(4f, 1294f, 344f, 323f);
        var differentSize = new Rect(4f, 1306f, 339f, 307f);

        AssertRect(
            source,
            PsdCoordinateSpace.ResolveLayerTextureRect(source, staleSameSize),
            0.0001f,
            "A stale imported texture rect with the same size should not shift layer texture sampling.");
        AssertVector(
            source.position,
            PsdCoordinateSpace.ResolveSpriteAtlasPositionForImportSync(source, staleSameSize),
            0.0001f,
            "Importer atlas sync should prefer the serialized sprite rect over stale preview layer state.");
        AssertVector(
            staleSameSize.position,
            PsdCoordinateSpace.ResolveSpriteAtlasPositionForImportSync(Rect.zero, staleSameSize),
            0.0001f,
            "Importer atlas sync should fall back to the preview layer when the serialized rect is missing.");
        AssertRect(
            source,
            PsdCoordinateSpace.ResolveLayerTextureRect(source, staleSamePosition),
            0.0001f,
            "A stale imported texture rect with the same atlas position should not restore the pre-Optimize size.");
        var layer = BuildLayer("stale-texture", 91, Vector2.zero);
        layer.sourceRect = source;
        layer.textureRect = staleSameSize;
        AssertRect(
            source,
            PsdCoordinateSpace.LayerTextureRect(layer, new Vector2(1024f, 2048f)),
            0.0001f,
            "Layer texture rect should route through the same stale texture rect guard.");
        layer.textureSamplingRect = new Rect(182.5f, 407.5f, 153f, 190.5f);
        AssertRect(
            layer.textureSamplingRect,
            PsdCoordinateSpace.LayerTextureRect(layer, new Vector2(1024f, 2048f)),
            0.0001f,
            "A live physical sampling rect should override the editable logical rect only for texture reads.");
        AssertVector(
            new Vector2(349.5f, 753f),
            PsdCoordinateSpace.LayerLocalToTextureSamplingPoint(
                new PsdLayerPreviewRenderer.LayerInfo { sourceRect = new Rect(631f, 1204f, 68f, 302f) },
                new Vector2(68f, 302f),
                new Rect(315.5f, 602f, 34f, 151f)),
            0.0001f,
            "Logical layer pixels should scale into the physical texture sampling rect.");
        AssertVector(
            new Vector2(88f, 200f),
            PsdCoordinateSpace.PreserveLayerDocumentPositionForSpriteRectChange(new Vector2(88f, 200f)),
            0.0001f,
            "Sprite rect edits must not move the PSD layer document position.");
        AssertVector(
            new Vector2(88f, 212f),
            PsdCoordinateSpace.ResolveSpritePositionForSpriteRectChange(
                new Vector2(88f, 200f),
                new Rect(4f, 1294f, 341f, 309f),
                new Rect(4f, 1306f, 341f, 309f)),
            0.0001f,
            "Sprite position should track rect movement so the source image stays in the same PSD document location.");
        AssertVector(
            new Vector2(88f, 200f),
            PsdCoordinateSpace.ResolveSpriteVisualAnchor(
                new Vector2(88f, 212f),
                new Rect(4f, 1306f, 341f, 309f),
                new Vector2(4f, 1294f)),
            0.0001f,
            "Visual anchor should stay stable when rect changes and spritePosition tracks the delta.");
        AssertRect(
            differentSize,
            PsdCoordinateSpace.ResolveLayerTextureRect(source, differentSize),
            0.0001f,
            "A genuinely different imported texture rect should remain available for non-equivalent data.");
        AssertRect(
            source,
            PsdCoordinateSpace.ResolveLayerTextureRect(source, Rect.zero),
            0.0001f,
            "Missing imported texture rect should fall back to the source rect.");
        AssertRect(
            new Rect(182f, 758f, 120f, 160f),
            PsdCoordinateSpace.ResolveLayerTextureRect(
                new Rect(182f, 758f, 120f, 160f),
                new Rect(91f, 379f, 60f, 80f)),
            0.0001f,
            "A uniformly scaled runtime Sprite rect should not replace PSD importer atlas coordinates.");
        AssertRect(
            new Rect(2f, 1907f, 162.5f, 133.5f),
            PsdCoordinateSpace.ResolveLayerTextureRect(
                new Rect(4f, 3814f, 325f, 267f),
                new Rect(2f, 1907f, 162.5f, 133.5f),
                new Vector2(1024f, 2048f)),
            0.0001f,
            "A serialized logical-atlas rect outside the physical texture should use the scaled runtime Sprite rect.");
        AssertRect(
            new Rect(182.5f, 407.5f, 153f, 190.5f),
            PsdCoordinateSpace.ResolveCurrentTextureSamplingRect(
                new Rect(365f, 815f, 306f, 381f),
                new Rect(182.5f, 407.5f, 153f, 190.5f),
                new Vector2(1024f, 2048f)),
            0.0001f,
            "The imported physical Sprite rect should remain authoritative when the logical atlas rect also fits the texture.");
        AssertRect(
            new Rect(182.5f, 407.5f, 153f, 190.5f),
            PsdCoordinateSpace.ResolveCurrentTextureSamplingRect(
                new Rect(365f, 815f, 306f, 381f),
                new Rect(182.5f, 407.5f, 153f, 190.5f),
                new Vector2(1024f, 2048f)),
            0.0001f,
            "A stale serialized sampling rect must not override the current imported Sprite rect.");
        var logicalReferenceRect = new Rect(4f, 3814f, 325f, 267f);
        var textureReferenceRect = new Rect(2f, 1907f, 162.5f, 133.5f);
        var fittedTextureRect = new Rect(4f, 1910f, 150f, 120f);
        var fittedAtlasRect = new Rect(8f, 3820f, 300f, 240f);
        AssertRect(
            fittedAtlasRect,
            PsdCoordinateSpace.TextureSamplingRectToAtlasRect(
                logicalReferenceRect,
                textureReferenceRect,
                fittedTextureRect),
            0.0001f,
            "Physical texture bounds should map back into the serialized logical atlas coordinate system.");
        AssertRect(
            fittedTextureRect,
            PsdCoordinateSpace.AtlasRectToTextureSamplingRect(
                logicalReferenceRect,
                textureReferenceRect,
                fittedAtlasRect),
            0.0001f,
            "Logical atlas bounds should map back into physical texture sampling coordinates.");
    }

    private static void CoordinateMapperTrustsPsdLayerSizeOverStaleSpriteRectSize()
    {
        var stale = new Rect(53f, 538f, 333f, 151f);

        AssertRect(
            new Rect(53f, 538f, 382f, 262f),
            PsdCoordinateSpace.ResolveSpriteRectForPsdLayerSize(stale, new Vector2Int(382, 262)),
            0.0001f,
            "PSD layer size should replace stale imported rect size without moving the atlas rect.");
        AssertRect(
            stale,
            PsdCoordinateSpace.ResolveSpriteRectForPsdLayerSize(stale, default),
            0.0001f,
            "Missing PSD layer size should keep the serialized rect.");
        AssertRect(
            stale,
            PsdCoordinateSpace.ResolveSpriteRectForPsdLayerSize(stale, new Vector2Int(333, 151)),
            0.0001f,
            "Matching PSD layer size should keep the serialized rect.");
        AssertRect(
            new Rect(365f, 1514f, 241f, 321f),
            PsdCoordinateSpace.ResolveSpriteRectForPsdLayerImport(
                new Rect(182f, 758f, 120f, 160f),
                new Vector2Int(241, 321),
                new Vector2(365f, 1514f),
                true),
            0.0001f,
            "A uniformly half-scaled new PSD layer rect should restore its logical mosaic position and source size.");
        AssertRect(
            new Rect(365f, 1514f, 241f, 321f),
            PsdCoordinateSpace.ResolveSpriteRectForPsdLayerSize(
                new Rect(182f, 758f, 120f, 160f),
                new Vector2Int(241, 321),
                new Vector2(365f, 1514f),
                true),
            0.0001f,
            "Explicit PSD parameter repair should use the preserved mosaic anchor when restoring source size.");
        AssertTrue(
            PsdCoordinateSpace.RequiresPsdLayerRectRepair(
                new Rect(373f, 1514f, 241f, 321f),
                new Rect(182f, 758f, 120f, 160f),
                new Rect(365f, 1514f, 241f, 321f)),
            "A moved layer should retain the importer repair signal when its serialized rect is uniformly downscaled.");
        AssertFalse(
            PsdCoordinateSpace.RequiresPsdLayerRectRepair(
                new Rect(373f, 1514f, 241f, 321f),
                new Rect(365f, 1514f, 241f, 321f),
                new Rect(365f, 1514f, 241f, 321f)),
            "A normal serialized PSD rect must continue through regular geometry optimization.");
        AssertVector(
            new Vector2(120.5f, 160.5f),
            PsdCoordinateSpace.ScaleSpriteLocalPointForRectResize(
                new Vector2(60f, 80f),
                new Rect(182f, 758f, 120f, 160f),
                new Rect(365f, 1514f, 241f, 321f)),
            0.0001f,
            "Rect repair should scale importer vertices from the stale local size into the PSD layer size.");
        AssertVector(
            new Vector2(192f, 100f),
            PsdCoordinateSpace.ScaleSpriteLocalVectorForRectResize(
                new Vector2(244f, 100f),
                new Rect(728f, 4f, 488f, 413f),
                new Rect(728f, 4f, 384f, 413f)),
            0.0001f,
            "Non-uniform rect repair should scale local geometry independently per axis.");
        var optimized = new Rect(372f, 1518f, 190f, 250f);
        AssertRect(
            optimized,
            PsdCoordinateSpace.ResolveSpriteRectForPsdLayerImport(
                optimized,
                new Vector2Int(241, 321),
                new Vector2(365f, 1514f),
                true),
            0.0001f,
            "A non-uniform optimized rect must not be expanded automatically when the preview reopens.");
        AssertRect(
            new Rect(182f, 758f, 120f, 160f),
            PsdCoordinateSpace.ResolveLayerTextureRect(
                new Rect(365f, 1514f, 241f, 321f),
                new Rect(182f, 758f, 120f, 160f),
                new Rect(91f, 379f, 60f, 80f),
                new Vector2(1024f, 2048f)),
            0.0001f,
            "Preview sampling should keep the serialized physical rect until the repaired importer has reimported.");
    }

    private static void GeometryPreviewTransformMapsSpriteVertices()
    {
        var layer = BuildLayer("geometry", 17, new Vector2(100f, 200f));
        var transform = new GeometryPreviewTransform(layer);

        transform.FromSpriteVertices(
            new[] { new Vector2(1f, 2f), new Vector2(-0.5f, 0.25f) },
            8f,
            new Vector2(16f, 32f),
            out var localPixels,
            out var worldVertices);

        AssertEqual(2, localPixels.Length, "Sprite vertex local count.");
        AssertEqual(2, worldVertices.Length, "Sprite vertex world count.");
        AssertVector(new Vector2(24f, 48f), localPixels[0], 0.0001f, "First sprite vertex local.");
        AssertVector(new Vector2(124f, 248f), worldVertices[0], 0.0001f, "First sprite vertex document.");
        AssertVector(new Vector2(12f, 34f), localPixels[1], 0.0001f, "Second sprite vertex local.");
        AssertVector(new Vector2(112f, 234f), worldVertices[1], 0.0001f, "Second sprite vertex document.");

        transform.FromSpriteVertices(null, 8f, Vector2.zero, out localPixels, out worldVertices);
        AssertEqual(0, localPixels.Length, "Null sprite vertices local count.");
        AssertEqual(0, worldVertices.Length, "Null sprite vertices world count.");
    }

    private static void GeometryPreviewTransformMapsSpriteUvs()
    {
        var layer = BuildLayer("uv", 18, new Vector2(50f, 75f));
        var transform = new GeometryPreviewTransform(layer);

        transform.FromSpriteUvs(
            new[] { new Vector2(0.25f, 0.5f), new Vector2(1f, 0f) },
            200f,
            100f,
            new Vector2(10f, 20f),
            flipY: true,
            flipHeight: 80f,
            out var localPixels,
            out var worldVertices);

        AssertEqual(2, localPixels.Length, "Sprite UV local count.");
        AssertVector(new Vector2(40f, 50f), localPixels[0], 0.0001f, "First sprite UV local.");
        AssertVector(new Vector2(90f, 125f), worldVertices[0], 0.0001f, "First sprite UV document.");
        AssertVector(new Vector2(190f, 100f), localPixels[1], 0.0001f, "Second sprite UV local.");
        AssertVector(new Vector2(240f, 175f), worldVertices[1], 0.0001f, "Second sprite UV document.");
    }

    private static void GeometryPreviewTransformMapsImporterInterpretations()
    {
        var layer = BuildLayer("importer", 19, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(5f, 7f, 30f, 40f);
        var transform = new GeometryPreviewTransform(layer);
        var vertices = new[] { BuildImporterVertex(new Vector2(2f, 3f)) };
        var pivot = new Vector2(10f, 20f);
        var sourceOffset = new Vector2(layer.sourceRect.x, layer.sourceRect.y);

        AssertImporterInterpretation(transform, vertices, GeometryPreviewTransform.ImporterInterpretation.RawLocal, 4f, pivot, sourceOffset, new Vector2(2f, 3f), new Vector2(102f, 203f), "RawLocal");
        AssertImporterInterpretation(transform, vertices, GeometryPreviewTransform.ImporterInterpretation.RawPlusPivot, 4f, pivot, sourceOffset, new Vector2(12f, 23f), new Vector2(112f, 223f), "RawPlusPivot");
        AssertImporterInterpretation(transform, vertices, GeometryPreviewTransform.ImporterInterpretation.RawTimesPpuPlusPivot, 4f, pivot, sourceOffset, new Vector2(18f, 32f), new Vector2(118f, 232f), "RawTimesPpuPlusPivot");
        AssertImporterInterpretation(transform, vertices, GeometryPreviewTransform.ImporterInterpretation.RawMinusSourceRect, 4f, pivot, sourceOffset, new Vector2(-3f, -4f), new Vector2(97f, 196f), "RawMinusSourceRect");
        AssertImporterInterpretation(transform, vertices, GeometryPreviewTransform.ImporterInterpretation.RawAsWorld, 4f, pivot, sourceOffset, new Vector2(-98f, -197f), new Vector2(2f, 3f), "RawAsWorld");

        transform.FromImporterVertices(
            vertices,
            GeometryPreviewTransform.ImporterInterpretation.RawPlusPivot,
            4f,
            pivot,
            sourceOffset,
            flipLocalY: true,
            flipHeight: 50f,
            out var flippedLocal,
            out var flippedWorld);

        AssertVector(new Vector2(12f, 27f), flippedLocal[0], 0.0001f, "Flipped importer local.");
        AssertVector(new Vector2(112f, 227f), flippedWorld[0], 0.0001f, "Flipped importer document.");
    }

    private static void SkinningAlgorithmsKeepSpriteBindPoseSpaceStable()
    {
        var layer = BuildLayer("skinned", 20, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(904f, 694f, 50f, 45f);
        var pixelsPerUnit = 100f;
        var pivotPixels = new Vector2(25f, 20f);
        var restDocumentVertex = new Vector2(125f, 220f);
        var bones = new[]
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                name = "bone_0",
                worldPosition = restDocumentVertex,
                worldRotation = Quaternion.identity
            }
        };
        var map = new[] { 0 };
        var bindPoses = new[] { Matrix4x4.identity };
        var skinMatrices = new Matrix4x4[1];
        var vertices = new[] { restDocumentVertex };
        var weights = new[]
        {
            new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 1f
            }
        };
        var deformed = new Vector2[1];

        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            map,
            bindPoses,
            usesSpriteBindPoses: true,
            layer,
            pixelsPerUnit,
            pivotPixels,
            skinMatrices);
        ReanimatorSkinningAlgorithms.DeformVertices(
            vertices,
            weights,
            skinMatrices,
            usesSpriteBindPoses: true,
            layer,
            pixelsPerUnit,
            pivotPixels,
            deformed);

        AssertVector(restDocumentVertex, deformed[0], 0.0001f, "Sprite bind pose rest vertex should stay in document position.");

        bones[0].worldPosition = restDocumentVertex + new Vector2(10f, 0f);
        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            map,
            bindPoses,
            usesSpriteBindPoses: true,
            layer,
            pixelsPerUnit,
            pivotPixels,
            skinMatrices);
        ReanimatorSkinningAlgorithms.DeformVertices(
            vertices,
            weights,
            skinMatrices,
            usesSpriteBindPoses: true,
            layer,
            pixelsPerUnit,
            pivotPixels,
            deformed);

        AssertVector(restDocumentVertex + new Vector2(10f, 0f), deformed[0], 0.0001f, "Sprite bind pose vertex should follow document bone motion.");
        var spritePoint = ReanimatorSkinningAlgorithms.DocumentToSpriteSkinPosition(layer, restDocumentVertex, pixelsPerUnit, pivotPixels);
        AssertVector(Vector2.zero, spritePoint, 0.0001f, "Document pivot should map to sprite bind origin.");
        AssertVector(restDocumentVertex, ReanimatorSkinningAlgorithms.SpriteSkinPositionToDocument(layer, spritePoint, pixelsPerUnit, pivotPixels), 0.0001f, "Sprite bind origin should map back to document pivot.");
    }

    private static void SkinningAlgorithmsNormalizeImportedBoneRotations()
    {
        var malformedRotation = new Quaternion(0f, 0f, 0.575796f, 0.649918f);
        var bones = new[]
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                worldPosition = new Vector2(4f, 7f),
                worldRotation = malformedRotation
            }
        };
        var map = new[] { 0 };
        var bindPoses = new[] { Matrix4x4.identity };
        var skinMatrices = new Matrix4x4[1];

        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            map,
            bindPoses,
            usesSpriteBindPoses: false,
            layer: null,
            pixelsPerUnit: 100f,
            pivotPixels: Vector2.zero,
            skinMatrices: skinMatrices);

        var right = skinMatrices[0].MultiplyVector(Vector3.right);
        var up = skinMatrices[0].MultiplyVector(Vector3.up);
        AssertFloat(1f, right.magnitude, 0.0001f, "Malformed imported quaternion should not scale the skinning X axis.");
        AssertFloat(1f, up.magnitude, 0.0001f, "Malformed imported quaternion should not scale the skinning Y axis.");
        AssertFloat(0f, Vector3.Dot(right, up), 0.0001f, "Normalized skinning axes should remain perpendicular.");

        var inverseBindMatrices = ReanimatorSkinningAlgorithms.BuildRendererBindBoneInverseMatrices(bones, map);
        var bindIdentity = skinMatrices[0] * inverseBindMatrices[0];
        AssertFloat(1f, bindIdentity.m00, 0.0001f, "Normalized bind and skin matrices should cancel on X.");
        AssertFloat(1f, bindIdentity.m11, 0.0001f, "Normalized bind and skin matrices should cancel on Y.");
    }

    private static void SkinningAlgorithmsRejectNonFiniteWeights()
    {
        var vertices = new[] { Vector2.zero, new Vector2(4f, 2f) };
        var weights = new[]
        {
            new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = float.PositiveInfinity,
                boneIndex1 = 1,
                weight1 = 1f
            },
            new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = float.NaN,
                boneIndex1 = 1,
                weight1 = float.NegativeInfinity
            }
        };
        var skinMatrices = new[]
        {
            Matrix4x4.Translate(new Vector3(10f, 0f, 0f)),
            Matrix4x4.Translate(new Vector3(0f, 5f, 0f))
        };
        var deformed = new Vector2[vertices.Length];

        ReanimatorSkinningAlgorithms.DeformVertices(
            vertices,
            weights,
            skinMatrices,
            usesSpriteBindPoses: false,
            layer: null,
            pixelsPerUnit: 100f,
            pivotPixels: Vector2.zero,
            deformed);

        AssertTrue(IsFinite(deformed[0]), "Finite channel should survive non-finite weight repair.");
        AssertVector(new Vector2(0f, 5f), deformed[0], 0.0001f, "Infinity channel should be dropped before deformation.");
        AssertTrue(IsFinite(deformed[1]), "All non-finite weights should fall back to the source vertex.");
        AssertVector(vertices[1], deformed[1], 0.0001f, "NaN-only weights should not poison preview vertices.");
    }

    private static void PreviewNavigatorCenterPreservesTarget()
    {
        var rect = new Rect(0f, 0f, 640f, 360f);
        var navigator = new PreviewNavigator();
        var target = new Vector2(122f, 77f);

        navigator.CenterOn(rect, target);
        AssertVector(rect.center, navigator.ToView(rect, target), 0.001f, "Centered target should map to rect center.");
        AssertVector(target, navigator.FromView(rect, rect.center), 0.001f, "Rect center should map back to target.");
    }

    private static void ReanimatorPreviewModesKeepSeparateNavigation()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            InvokePrivateMethod(window, "OnEnable");
            var modeType = typeof(Reanimator).GetNestedType("PreviewMode", BindingFlags.NonPublic);
            var skelMode = Enum.Parse(modeType, "Skel");
            var spriteMode = Enum.Parse(modeType, "Sprite");
            var rect = new Rect(0f, 0f, 640f, 360f);
            var skinningTarget = new Vector2(80f, 120f);
            var spriteTarget = new Vector2(420f, 260f);

            var skinningNavigator = GetPrivateField<PreviewNavigator>(window, "previewNavigator");
            skinningNavigator.CenterOn(rect, skinningTarget);
            AssertVector(rect.center, skinningNavigator.ToView(rect, skinningTarget), 0.001f, "Skinning camera setup should center its target.");

            InvokePrivateMethod(window, "SetPreviewMode", spriteMode);
            var spriteNavigator = GetPrivateField<PreviewNavigator>(window, "previewNavigator");
            AssertTrue(!ReferenceEquals(skinningNavigator, spriteNavigator), "Sprite Sheet mode should use a separate navigator instance.");
            spriteNavigator.CenterOn(rect, spriteTarget);
            AssertVector(rect.center, spriteNavigator.ToView(rect, spriteTarget), 0.001f, "Sprite Sheet camera setup should center its target.");

            InvokePrivateMethod(window, "SetPreviewMode", skelMode);
            var restoredSkinningNavigator = GetPrivateField<PreviewNavigator>(window, "previewNavigator");
            AssertTrue(ReferenceEquals(skinningNavigator, restoredSkinningNavigator), "Switching back to Skinning Editor should restore its navigator instance.");
            AssertVector(rect.center, restoredSkinningNavigator.ToView(rect, skinningTarget), 0.001f, "Skinning Editor camera should keep its previous position.");

            InvokePrivateMethod(window, "SetPreviewMode", spriteMode);
            var restoredSpriteNavigator = GetPrivateField<PreviewNavigator>(window, "previewNavigator");
            AssertTrue(ReferenceEquals(spriteNavigator, restoredSpriteNavigator), "Switching back to Sprite Sheet should restore its navigator instance.");
            AssertVector(rect.center, restoredSpriteNavigator.ToView(rect, spriteTarget), 0.001f, "Sprite Sheet camera should keep its previous position.");
        }
        finally
        {
            InvokePrivateMethod(window, "OnDisable");
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorComfimatorModeClearsSelectedSprite()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            InvokePrivateMethod(window, "OnEnable");
            var modeType = typeof(Reanimator).GetNestedType("PreviewMode", BindingFlags.NonPublic);
            var comfimatorMode = Enum.Parse(modeType, "Comfimator");
            var renderer = GetPrivateField<PsdLayerPreviewRenderer>(window, "layerPreviewRenderer");

            SetPrivateField(renderer, "selectedLayerSelectionKey", "selected-sprite");

            InvokePrivateMethod(window, "SetPreviewMode", comfimatorMode);

            AssertEqual(
                string.Empty,
                GetPrivateField<string>(renderer, "selectedLayerSelectionKey"),
                "Entering Comfimator should deselect the current sprite.");
        }
        finally
        {
            InvokePrivateMethod(window, "OnDisable");
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x) &&
               !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) &&
               !float.IsInfinity(value.y);
    }

}
#endif
