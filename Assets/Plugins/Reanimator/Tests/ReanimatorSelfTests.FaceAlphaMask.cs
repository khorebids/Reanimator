#if UNITY_EDITOR
using System.Linq;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    private static void FaceAlphaMaskFindsLargestLeftAndRightHoles()
    {
        const int width = 24;
        const int height = 14;
        var pixels = new Color32[width * height];
        FillFaceMaskRect(pixels, width, 1, 1, 22, 12, 255);
        FillFaceMaskRect(pixels, width, 3, 4, 4, 4, 0);
        FillFaceMaskRect(pixels, width, 16, 4, 5, 4, 0);
        FillFaceMaskRect(pixels, width, 9, 5, 1, 1, 0);
        FillFaceMaskRect(pixels, width, 14, 8, 1, 1, 0);

        var analysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(pixels, width, height);

        AssertTrue(analysis.HasContour, "Alpha mask should expose a common exterior contour.");
        AssertEqual(new RectInt(1, 1, 22, 12), analysis.ContourBounds, "Common contour bounds.");
        AssertVector(new Vector2(12f, 7f), analysis.Center, 0.0001f, "Common contour center.");
        AssertEqual(4, analysis.Holes.Length, "All enclosed alpha holes, including one-pixel holes, should be retained.");
        AssertTrue(analysis.Holes.Any(hole => hole.Area == 1), "One-pixel alpha holes should not be filtered out.");
        AssertTrue(analysis.HasLeftHole, "Largest left alpha hole should be found.");
        AssertTrue(analysis.HasRightHole, "Largest right alpha hole should be found.");
        AssertEqual(16, analysis.LeftHole.Area, "Largest left alpha hole area.");
        AssertEqual(20, analysis.RightHole.Area, "Largest right alpha hole area.");
        AssertVector(new Vector2(5f, 6f), analysis.LeftHole.Center, 0.0001f, "Largest left alpha hole center.");
        AssertVector(new Vector2(18.5f, 6f), analysis.RightHole.Center, 0.0001f, "Largest right alpha hole center.");
    }

    private static void FaceAlphaMaskRejectsRegionsOpenToExterior()
    {
        const int width = 10;
        const int height = 10;
        var pixels = new Color32[width * height];
        FillFaceMaskRect(pixels, width, 1, 1, 8, 8, 255);
        FillFaceMaskRect(pixels, width, 1, 5, 4, 1, 0);

        var analysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(pixels, width, height);

        AssertTrue(analysis.HasContour, "Opened mask should still expose its exterior contour.");
        AssertEqual(0, analysis.Holes.Length, "Transparent regions connected to the sprite exterior are not holes.");
        AssertFalse(analysis.HasLeftHole, "Opened region should not become a left target.");
        AssertFalse(analysis.HasRightHole, "Opened region should not become a right target.");
    }

    private static void FaceEyeVirtualPointsFollowHoleDensity()
    {
        const int width = 12;
        const int height = 10;
        var pixels = new Color32[width * height];
        FillFaceMaskRect(pixels, width, 1, 1, 10, 8, 255);
        FillFaceMaskRect(pixels, width, 3, 3, 4, 4, 0);

        var analysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(pixels, width, height);
        var hole = analysis.Holes.Single();
        var sparse = ReanimatorFaceAlphaMaskAlgorithms.BuildReverseCullContour(hole, 0.25f);
        var dense = ReanimatorFaceAlphaMaskAlgorithms.BuildReverseCullContour(hole, 1f);

        AssertTrue(Mathf.Abs(hole.Perimeter - 16f) <= 0.0001f, "Four-by-four hole perimeter should be pixel-accurate.");
        AssertEqual(4, sparse.Length, "Quarter density should retain the four-point minimum.");
        AssertEqual(16, dense.Length, "Full density should create one virtual point per perimeter pixel.");
        AssertEqual(dense.Length, dense.Distinct().Count(), "Reverse contour points should be unique around the closed contour.");
        AssertTrue(
            ReanimatorFaceAlphaMaskAlgorithms.CalculateSignedArea(sparse) < 0f,
            "Eye hole contour should use reverse winding after Cull.");
    }

    private static void FaceEyePointsFollowHeadDirection()
    {
        const int width = 12;
        const int height = 10;
        var pixels = new Color32[width * height];
        FillFaceMaskRect(pixels, width, 1, 1, 10, 8, 255);
        FillFaceMaskRect(pixels, width, 3, 3, 4, 4, 0);

        var analysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(pixels, width, height);
        var hole = analysis.Holes.Single();
        var expansion = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            hole,
            1f,
            Vector2.up,
            2f,
            3f);

        AssertEqual(
            expansion.BaseContour.Length + expansion.DuplicatePoints.Length + expansion.OutlineContour.Length,
            expansion.TotalPointCount,
            "Eye point total should include base, upper duplicates, and the red outline.");
        AssertTrue(
            expansion.Regions.Any(region => region == ReanimatorFaceEyePointRegion.Edge),
            "A pixel-aligned fixture should expose lateral points whose normals are perpendicular to the head direction.");
        AssertTrue(
            expansion.Regions.Any(region => region == ReanimatorFaceEyePointRegion.Lower),
            "Eye contour fixture should include lower points for the no-duplicate check.");
        AssertEqual(
            expansion.Regions.Count(region => region != ReanimatorFaceEyePointRegion.Lower),
            expansion.DuplicatePoints.Length,
            "Every upper or lateral edge point should create exactly one duplicate.");
        AssertEqual(
            expansion.DuplicateSourceIndices.Length,
            expansion.DuplicateSourceIndices.Distinct().Count(),
            "Duplicate sources should not repeat contour points.");

        for (var i = 0; i < expansion.DuplicatePoints.Length; i++)
        {
            var sourceIndex = expansion.DuplicateSourceIndices[i];
            AssertTrue(
                expansion.Regions[sourceIndex] != ReanimatorFaceEyePointRegion.Lower,
                "Lower eye points must not create duplicates.");
            var radial = expansion.BaseContour[sourceIndex] - hole.Center;
            var displacement = expansion.DuplicatePoints[i] - expansion.BaseContour[sourceIndex];
            AssertTrue(Vector2.Dot(radial, displacement) > 0f, "Duplicate should move outward from the eye center.");
            AssertTrue(
                Mathf.Abs(radial.x * displacement.y - radial.y * displacement.x) <= 0.0001f,
                "Duplicate offset should follow the center-to-vertex direction.");
            AssertTrue(Mathf.Abs(displacement.magnitude - 2f) <= 0.0001f, "Duplicate should use the requested radial offset.");
        }

        AssertEqual(
            expansion.BaseContour.Length,
            expansion.OutlineContour.Length,
            "Red outline should provide one ordered point for every base contour point.");
        for (var i = 0; i < expansion.BaseContour.Length; i++)
        {
            var duplicateIndex = System.Array.IndexOf(expansion.DuplicateSourceIndices, i);
            var outlineSource = duplicateIndex >= 0
                ? expansion.DuplicatePoints[duplicateIndex]
                : expansion.BaseContour[i];
            var radial = expansion.BaseContour[i] - hole.Center;
            var displacement = expansion.OutlineContour[i] - outlineSource;
            AssertTrue(Vector2.Dot(radial, displacement) > 0f, "Red outline point should move outward from its source.");
            AssertTrue(
                Mathf.Abs(radial.x * displacement.y - radial.y * displacement.x) <= 0.0001f,
                "Red outline offset should follow the eye-center radial direction.");
            AssertTrue(Mathf.Abs(displacement.magnitude - 3f) <= 0.0001f, "Red outline should use its own offset value.");
            if (expansion.Regions[i] == ReanimatorFaceEyePointRegion.Lower)
                AssertTrue(duplicateIndex < 0, "Lower red outline points should start directly from the base contour.");
            else
                AssertTrue(duplicateIndex >= 0, "Upper and edge red outline points should start from their offset duplicates.");
        }

        var rotatedExpansion = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            hole,
            1f,
            Vector2.right,
            2f,
            3f);
        for (var i = 0; i < rotatedExpansion.BaseContour.Length; i++)
        {
            var directionDot = Vector2.Dot(rotatedExpansion.OutwardNormals[i], Vector2.right);
            if (rotatedExpansion.Regions[i] == ReanimatorFaceEyePointRegion.Upper)
                AssertTrue(directionDot > 0f, "An outward normal facing along the head direction should classify as upper.");
            else if (rotatedExpansion.Regions[i] == ReanimatorFaceEyePointRegion.Lower)
                AssertTrue(directionDot < 0f, "An outward normal facing against the head direction should classify as lower.");
            else
                AssertTrue(Mathf.Abs(directionDot) <= 0.0001f, "Only a normal perpendicular to the head direction should classify as edge.");
        }
    }

    private static void FaceEyeNormalsKeepSteepUpperPointsUpper()
    {
        var steepContour = new[]
        {
            new Vector2(0f, 2f),
            new Vector2(2.9f, 1f),
            new Vector2(3f, 0f),
            new Vector2(3.1f, -1f),
            new Vector2(0f, -2f),
            new Vector2(-3f, 0f)
        };
        var steepRegions = ReanimatorFaceAlphaMaskAlgorithms.ClassifyEyeContourByNormals(
            steepContour,
            Vector2.zero,
            Vector2.up,
            out var steepNormals);
        var steepProjection = Vector2.Dot(steepNormals[2], Vector2.up);

        AssertTrue(steepProjection > 0f && steepProjection < 0.2f, "Fixture normal should be steep but still point slightly upward.");
        AssertEqual(
            ReanimatorFaceEyePointRegion.Upper,
            steepRegions[2],
            "Any positive normal projection, even at a steep angle, should remain upper.");

        var perpendicularContour = new[]
        {
            new Vector2(0f, 2f),
            new Vector2(3f, 1f),
            new Vector2(3f, 0f),
            new Vector2(3f, -1f),
            new Vector2(0f, -2f),
            new Vector2(-3f, 0f)
        };
        var perpendicularRegions = ReanimatorFaceAlphaMaskAlgorithms.ClassifyEyeContourByNormals(
            perpendicularContour,
            Vector2.zero,
            Vector2.up,
            out var perpendicularNormals);

        AssertTrue(
            Mathf.Abs(Vector2.Dot(perpendicularNormals[2], Vector2.up)) <= 0.0001f,
            "Fixture edge normal should be exactly perpendicular to the head direction.");
        AssertEqual(
            ReanimatorFaceEyePointRegion.Edge,
            perpendicularRegions[2],
            "Only an effectively perpendicular normal should classify as edge.");
    }

    private static void FaceVirtualEyelidsSkinFollowsBonePose()
    {
        var skinMatrix = ReanimatorFaceAlphaMaskAlgorithms.BuildVirtualBoneSkinMatrix(
            new Vector2(10f, 10f),
            Quaternion.identity,
            new Vector2(12f, 11f),
            Quaternion.Euler(0f, 0f, 90f));

        var transformedOrigin = skinMatrix.MultiplyPoint3x4(new Vector3(10f, 10f, 0f));
        var transformedPoint = skinMatrix.MultiplyPoint3x4(new Vector3(11f, 10f, 0f));
        AssertVector(new Vector2(12f, 11f), transformedOrigin, 0.0001f, "A fully weighted green point at the eyelids base should follow bone translation.");
        AssertVector(new Vector2(12f, 12f), transformedPoint, 0.0001f, "A fully weighted green point should rotate around the eyelids rest base.");

        var unmovedPoint = ReanimatorFaceAlphaMaskAlgorithms.ApplyVirtualBoneSkinWeight(
            new Vector2(11f, 10f),
            skinMatrix,
            0f);
        var halfWeightedPoint = ReanimatorFaceAlphaMaskAlgorithms.ApplyVirtualBoneSkinWeight(
            new Vector2(11f, 10f),
            skinMatrix,
            0.5f);
        AssertVector(new Vector2(11f, 10f), unmovedPoint, 0.0001f, "A zero-weight point should remain in its rest position.");
        AssertVector(new Vector2(11.5f, 11f), halfWeightedPoint, 0.0001f, "A partial weight should blend linearly between rest and eyelids pose.");
    }

    private static void FaceEyelidsPreviewWeightsRunFromCenterToEdges()
    {
        const int width = 18;
        const int height = 14;
        var pixels = new Color32[width * height];
        FillFaceMaskRect(pixels, width, 1, 1, 16, 12, 255);
        FillFaceMaskRect(pixels, width, 5, 6, 8, 4, 0);

        var analysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(pixels, width, height);
        var hole = analysis.Holes.Single();
        var curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        var expansion = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            hole,
            1f,
            Vector2.up,
            2f,
            0f,
            null,
            null,
            curve);

        var side = Vector2.left;
        AssertTrue(
            Mathf.Abs(Vector2.Dot(expansion.ZeroWeightPoint - hole.Center, side)) <= 0.0001f,
            "The zero control point should lie on the eye center axis parallel to the head bone.");
        AssertTrue(
            Vector2.Dot(expansion.ZeroWeightPoint - hole.Center, Vector2.up) >= -0.0001f,
            "The zero control point should be placed from the eye center toward the upper contour.");

        var positiveBaseIndex = -1;
        var negativeBaseIndex = -1;
        var positiveBaseExtent = float.NegativeInfinity;
        var negativeBaseExtent = float.PositiveInfinity;
        for (var i = 0; i < expansion.BaseContour.Length; i++)
        {
            if (expansion.Regions[i] == ReanimatorFaceEyePointRegion.Lower)
            {
                AssertTrue(expansion.BasePointWeights[i] <= 0.0001f, "Lower contour points must stay at weight zero.");
                continue;
            }

            var projection = Vector2.Dot(expansion.BaseContour[i] - hole.Center, side);
            if (projection > positiveBaseExtent)
            {
                positiveBaseExtent = projection;
                positiveBaseIndex = i;
            }
            if (projection < negativeBaseExtent)
            {
                negativeBaseExtent = projection;
                negativeBaseIndex = i;
            }
        }
        AssertTrue(positiveBaseIndex >= 0 && negativeBaseIndex >= 0, "Upper contour should have extreme points on both sides.");
        AssertTrue(expansion.BasePointWeights[positiveBaseIndex] <= 0.0001f, "One upper edge must use the curve value at input one.");
        AssertTrue(expansion.BasePointWeights[negativeBaseIndex] <= 0.0001f, "The opposite upper edge must use the curve value at input one.");

        var positiveGreenIndex = 0;
        var negativeGreenIndex = 0;
        for (var i = 1; i < expansion.DuplicatePoints.Length; i++)
        {
            if (Vector2.Dot(expansion.DuplicatePoints[i] - hole.Center, side) >
                Vector2.Dot(expansion.DuplicatePoints[positiveGreenIndex] - hole.Center, side))
            {
                positiveGreenIndex = i;
            }
            if (Vector2.Dot(expansion.DuplicatePoints[i] - hole.Center, side) <
                Vector2.Dot(expansion.DuplicatePoints[negativeGreenIndex] - hole.Center, side))
            {
                negativeGreenIndex = i;
            }
        }
        AssertTrue(expansion.DuplicatePointWeights[positiveGreenIndex] <= 0.0001f, "One green edge must use the curve value at input one.");
        AssertTrue(expansion.DuplicatePointWeights[negativeGreenIndex] <= 0.0001f, "The opposite green edge must use the curve value at input one.");
        AssertTrue(
            Mathf.Abs(ReanimatorFaceAlphaMaskAlgorithms.EvaluateEyePreviewWeight(curve, 0.5f) - 0.5f) <= 0.0001f,
            "The editable curve should shape intermediate eyelids preview weights.");
        AssertTrue(
            Mathf.Abs(ReanimatorFaceAlphaMaskAlgorithms.EvaluateEyePreviewWeight(curve, 0f) - 1f) <= 0.0001f,
            "The center must use the curve value at input zero.");
        AssertTrue(
            ReanimatorFaceAlphaMaskAlgorithms.EvaluateEyePreviewWeight(curve, 1f) <= 0.0001f,
            "Extreme points must use the curve value at input one.");
    }

    private static void FaceRedOutlineStrictlyContainsOnlyInteriorPoints()
    {
        var outline = new[]
        {
            new Vector2(-2f, -1f),
            new Vector2(2f, -1f),
            new Vector2(2f, 1f),
            new Vector2(-2f, 1f)
        };
        AssertTrue(
            ReanimatorFaceAlphaMaskAlgorithms.IsPointStrictlyInsideContour(Vector2.zero, outline),
            "An existing geometry point inside the red outline should be removed on Apply.");
        AssertFalse(
            ReanimatorFaceAlphaMaskAlgorithms.IsPointStrictlyInsideContour(new Vector2(2f, 0f), outline),
            "A geometry point on the red outline should be retained for reuse.");
        AssertFalse(
            ReanimatorFaceAlphaMaskAlgorithms.IsPointStrictlyInsideContour(new Vector2(3f, 0f), outline),
            "A geometry point outside the red outline should remain untouched.");
    }

    private static void FaceEyeOutlineStopsEveryPointAtFirstCollision()
    {
        const int width = 16;
        const int height = 14;
        var pixels = new Color32[width * height];
        FillFaceMaskRect(pixels, width, 1, 1, 14, 12, 255);
        FillFaceMaskRect(pixels, width, 5, 9, 6, 2, 0);

        var analysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(pixels, width, height);
        var hole = analysis.Holes.Single();
        var atZero = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            hole,
            1f,
            Vector2.up,
            2f,
            0f);
        var atLargeOffset = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            hole,
            1f,
            Vector2.up,
            2f,
            32f);
        var expandedFurther = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            hole,
            1f,
            Vector2.up,
            2f,
            64f);

        for (var i = 0; i < atZero.OutlineContour.Length; i++)
        {
            var duplicateIndex = System.Array.IndexOf(atZero.DuplicateSourceIndices, i);
            var parent = duplicateIndex >= 0 ? atZero.DuplicatePoints[duplicateIndex] : atZero.BaseContour[i];
            AssertVector(parent, atZero.OutlineContour[i], 0.0001f, "At zero offset every red point should equal its parent point.");
            AssertVector(
                atLargeOffset.OutlineContour[i],
                expandedFurther.OutlineContour[i],
                0.0001f,
                "After the first collision, a larger Outline Offset must not move any red point.");
        }
        AssertEqual(0, atZero.OutlineCollisionPoints.Length, "Outline Offset zero should not create a collision marker.");

        AssertEqual(1, atLargeOffset.OutlineCollisionPoints.Length, "The first colliding red point should create one green point on the sprite contour.");
        AssertEqual(1, atLargeOffset.OutlineCollisionSourceIndices.Length, "The green point should retain the colliding red point index.");
        AssertTrue(atLargeOffset.AppliedOutlineOffset < 32f, "A large Outline Offset should saturate at the first sprite collision.");

        var stoppedIndex = atLargeOffset.OutlineCollisionSourceIndices[0];
        var greenPoint = atLargeOffset.OutlineCollisionPoints[0];
        var stoppedRedPoint = atLargeOffset.OutlineContour[stoppedIndex];
        AssertTrue(
            Mathf.Abs(Vector2.Distance(greenPoint, stoppedRedPoint) - 0.35f) <= 0.001f,
            "The colliding red point should move slightly back from its green contour point.");

        var otherIndex = (stoppedIndex + 1) % atLargeOffset.OutlineContour.Length;
        var otherDuplicateIndex = System.Array.IndexOf(atLargeOffset.DuplicateSourceIndices, otherIndex);
        var otherParent = otherDuplicateIndex >= 0
            ? atLargeOffset.DuplicatePoints[otherDuplicateIndex]
            : atLargeOffset.BaseContour[otherIndex];
        AssertTrue(
            Mathf.Abs(Vector2.Distance(otherParent, atLargeOffset.OutlineContour[otherIndex]) - atLargeOffset.AppliedOutlineOffset) <= 0.0001f,
            "Every non-colliding red point should stop at the same first-collision offset without being pulled back.");

        const int pairWidth = 24;
        const int pairHeight = 14;
        var pairPixels = new Color32[pairWidth * pairHeight];
        FillFaceMaskRect(pairPixels, pairWidth, 1, 1, 22, 12, 255);
        FillFaceMaskRect(pairPixels, pairWidth, 3, 4, 4, 4, 0);
        FillFaceMaskRect(pairPixels, pairWidth, 16, 4, 5, 4, 0);
        var pairAnalysis = ReanimatorFaceAlphaMaskAlgorithms.Analyze(pairPixels, pairWidth, pairHeight);
        var leftEye = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            pairAnalysis,
            pairAnalysis.LeftHole,
            1f,
            Vector2.up,
            2f,
            32f);
        var rightEye = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            pairAnalysis,
            pairAnalysis.RightHole,
            1f,
            Vector2.up,
            2f,
            32f);
        ReanimatorFaceAlphaMaskAlgorithms.ApplySharedEyeOutlineCollisionStop(32f, leftEye, rightEye);
        AssertTrue(
            Mathf.Abs(leftEye.AppliedOutlineOffset - rightEye.AppliedOutlineOffset) <= 0.0001f,
            "The first collision should stop every red point on both eyes at the same offset.");
        AssertEqual(
            1,
            leftEye.OutlineCollisionPoints.Length + rightEye.OutlineCollisionPoints.Length,
            "Only the first colliding point across both eyes should create a green marker.");

        var geometryBoundaryVertices = new[]
        {
            new Vector2(-4f, -4f),
            new Vector2(20f, -4f),
            new Vector2(20f, 18f),
            new Vector2(-4f, 18f)
        };
        var geometryBoundaryPairs = new[] { 0, 1, 1, 2, 2, 3, 3, 0 };
        var geometryStopped = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            hole,
            1f,
            Vector2.up,
            2f,
            32f,
            geometryBoundaryVertices,
            geometryBoundaryPairs);
        AssertEqual(1, geometryStopped.OutlineCollisionPoints.Length, "Geometry collision should create one green marker.");
        var geometryMarker = geometryStopped.OutlineCollisionPoints[0];
        AssertTrue(
            Mathf.Abs(geometryMarker.x + 4f) <= 0.001f ||
            Mathf.Abs(geometryMarker.x - 20f) <= 0.001f ||
            Mathf.Abs(geometryMarker.y + 4f) <= 0.001f ||
            Mathf.Abs(geometryMarker.y - 18f) <= 0.001f,
            "The green marker must lie on the supplied sprite geometry boundary, not on the alpha silhouette.");
    }

    private static void FillFaceMaskRect(
        Color32[] pixels,
        int textureWidth,
        int x,
        int y,
        int width,
        int height,
        byte alpha)
    {
        var color = alpha >= 128
            ? new Color32(255, 255, 255, 255)
            : new Color32(0, 0, 0, 255);
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
                pixels[row * textureWidth + column] = color;
        }
    }
}
#endif
