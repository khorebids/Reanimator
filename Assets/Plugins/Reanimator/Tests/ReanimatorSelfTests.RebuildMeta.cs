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
    private static void PsdArmNamesResolveGeneratedImporterIds()
    {
        var layers = new[]
        {
            (id: 31, name: "l_arm", guid: "left"),
            (id: 32, name: "r_arm", guid: "right"),
            (id: 33, name: "l_arm", guid: "duplicate")
        };
        AssertEqual(31, ReanimatorPsdArmNames.ResolveImporterId(layers, 0, "l_arm", "LEFT"),
            "An arm without a source ID should resolve by sprite GUID.");
        AssertEqual(32, ReanimatorPsdArmNames.ResolveImporterId(layers, 0, "r_arm", null),
            "An arm without a source ID or GUID should resolve by its unique name.");
    }

    private static void RebuildMetaAlgorithmsBuildStableHierarchyKeys()
    {
        AssertEqual("G:3:17", ReanimatorRebuildMetaAlgorithms.BuildLayerHierarchyKey(true, 3, "Group", 17), "Group hierarchy key should prefer layer id.");
        AssertEqual("L:4:Layer Name", ReanimatorRebuildMetaAlgorithms.BuildLayerHierarchyKey(false, 4, "Layer Name", 0), "Layer hierarchy key should fall back to name.");
        AssertEqual("L:25", ReanimatorRebuildMetaAlgorithms.BuildFlatLayerHierarchyKey("Ignored", 25), "Flat key should prefer layer id.");
        AssertEqual("L:Loose", ReanimatorRebuildMetaAlgorithms.BuildFlatLayerHierarchyKey("Loose", 0), "Flat key should fall back to name.");
        AssertEqual(string.Empty, ReanimatorRebuildMetaAlgorithms.BuildKey(null, 0), "Empty key fallback.");
    }

    private static void RebuildMetaAlgorithmsChooseMetaSpritePositionsSafely()
    {
        var rect = new Rect(12f, 20f, 30f, 40f);
        var meta = new Vector2(100f, 200f);

        AssertTrue(
            ReanimatorRebuildMetaAlgorithms.ShouldUseMetaSpritePosition(new Vector2(7f, 8f), rect, meta, hasSerializedPosition: false),
            "Missing serialized position should use meta.");
        AssertFalse(
            ReanimatorRebuildMetaAlgorithms.ShouldUseMetaSpritePosition(meta, rect, meta, hasSerializedPosition: true),
            "Matching current position should not rewrite.");
        AssertTrue(
            ReanimatorRebuildMetaAlgorithms.ShouldUseMetaSpritePosition(rect.position, rect, meta, hasSerializedPosition: true),
            "Atlas-position fallback should use meta.");
        AssertTrue(
            ReanimatorRebuildMetaAlgorithms.ShouldUseMetaSpritePosition(Vector2.zero, rect, meta, hasSerializedPosition: true),
            "Zero current position should use meta.");
        AssertFalse(
            ReanimatorRebuildMetaAlgorithms.ShouldUseMetaSpritePosition(new Vector2(15f, 21f), rect, meta, hasSerializedPosition: true),
            "Distinct non-atlas serialized position should be preserved.");
    }

    private static void RebuildMetaAlgorithmsPreferVisualSpritePositionOverCharacterPartBounds()
    {
        var rect = new Rect(904f, 694f, 50f, 45f);
        var spritePosition = new Vector2(1183f, 155f);
        var characterPartBoundsPosition = new Vector2(1154f, 106f);
        var metaPosition = new Vector2(1183f, 155f);

        var chosen = ReanimatorRebuildMetaAlgorithms.ChooseLayerSpritePosition(
            rect,
            hasSpritePosition: true,
            spritePosition,
            hasMetaPosition: true,
            metaPosition,
            hasCharacterPartPosition: true,
            characterPartBoundsPosition,
            preferCharacterPartPosition: false,
            out var hasResolvedPosition);

        AssertTrue(hasResolvedPosition, "Visual sprite position should be resolved.");
        AssertVector(spritePosition, chosen, 0.0001f, "Visual sprite position should not be overwritten by character part bounds.");

        var fallback = ReanimatorRebuildMetaAlgorithms.ChooseLayerSpritePosition(
            rect,
            hasSpritePosition: false,
            Vector2.zero,
            hasMetaPosition: false,
            Vector2.zero,
            hasCharacterPartPosition: true,
            characterPartBoundsPosition,
            preferCharacterPartPosition: false,
            out var hasFallbackPosition);

        AssertTrue(hasFallbackPosition, "Character part position should remain a fallback.");
        AssertVector(characterPartBoundsPosition, fallback, 0.0001f, "Character part position fallback.");
    }

    private static void RebuildMetaAlgorithmsParseSpriteGuidAndPosition()
    {
        AssertTrue(
            ReanimatorRebuildMetaAlgorithms.TryParseSpriteGuidLine("m_SpriteID: AABBCCDD-0011-2233-4455-66778899AAFF", out var guid),
            "Dashed sprite guid should parse.");
        AssertEqual("aabbccdd00112233445566778899aaff", guid, "Guid should normalize.");
        AssertFalse(ReanimatorRebuildMetaAlgorithms.TryParseSpriteGuidLine("spriteID: not-a-guid", out _), "Invalid sprite guid should fail.");

        AssertTrue(
            ReanimatorRebuildMetaAlgorithms.TryParseInlineVector2("spritePosition: {x: -12.5, y: 3.25}", out var inlinePosition),
            "Inline vector should parse.");
        AssertVector(new Vector2(-12.5f, 3.25f), inlinePosition, 0.0001f, "Inline vector value.");

        var lines = new[]
        {
            "spritePosition:",
            "  x: 42.5",
            "  y: -9.25"
        };
        AssertTrue(ReanimatorRebuildMetaAlgorithms.TryReadSpritePosition(lines, 0, out var blockPosition), "Block vector should parse.");
        AssertVector(new Vector2(42.5f, -9.25f), blockPosition, 0.0001f, "Block vector value.");
        AssertFalse(ReanimatorRebuildMetaAlgorithms.TryReadSpritePosition(lines, 8, out _), "Out-of-range vector read should fail.");
    }

    private static void RebuildMetaAlgorithmsParseMetaSpritePositionMap()
    {
        var lines = new[]
        {
            "TextureImporter:",
            "  sprites:",
            "  - spriteID: AAAA-BBBB",
            "    spritePosition: {x: 10, y: 20}",
            "  - m_SpriteID: CCCC-DDDD",
            "    spritePosition:",
            "      x: -5.5",
            "      y: 7.25",
            "  - spriteID: AAAA-BBBB",
            "    spritePosition: {x: 99, y: 99}",
            "  - spriteID: EEEE-FFFF"
        };

        var positions = ReanimatorRebuildMetaAlgorithms.ParseMetaSpritePositionBySpriteGuid(lines);
        AssertEqual(2, positions.Count, "Meta sprite position count.");
        AssertTrue(positions.TryGetValue("aaaabbbb", out var first), "First guid should exist.");
        AssertVector(new Vector2(10f, 20f), first, 0.0001f, "First guid position.");
        AssertTrue(positions.TryGetValue("ccccdddd", out var second), "Second guid should exist.");
        AssertVector(new Vector2(-5.5f, 7.25f), second, 0.0001f, "Second guid position.");
        AssertFalse(positions.ContainsKey("eeeeffff"), "Guid without position should be ignored.");
    }

}
#endif
