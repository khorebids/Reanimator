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
    public bool TryLocateNippleTargets(
        out Vector2 leftDocumentPoint,
        out Vector2 rightDocumentPoint,
        out float leftConfidence,
        out float rightConfidence,
        out string result,
        out string error)
    {
        if (!TryFindNippleControlBone(true, out var leftBoneIndex) ||
            !TryFindNippleControlBone(false, out var rightBoneIndex) ||
            bones == null || leftBoneIndex < 0 || rightBoneIndex < 0 ||
            leftBoneIndex >= bones.Count || rightBoneIndex >= bones.Count ||
            bones[leftBoneIndex] == null || bones[rightBoneIndex] == null)
        {
            leftDocumentPoint = Vector2.zero;
            rightDocumentPoint = Vector2.zero;
            leftConfidence = 0f;
            rightConfidence = 0f;
            result = string.Empty;
            error = "Bones 'nipple_l' and 'nipple_r' are required. Run Auto Skeleton first.";
            return false;
        }

        var leftBoneGuess = GetNippleControlPoint(bones[leftBoneIndex]);
        var rightBoneGuess = GetNippleControlPoint(bones[rightBoneIndex]);
        return TryLocateNippleTargetsFromGuesses(
            leftBoneGuess,
            rightBoneGuess,
            true,
            out leftDocumentPoint,
            out rightDocumentPoint,
            out leftConfidence,
            out rightConfidence,
            out result,
            out error);
    }

    public bool TryLocateNippleTargetsFromGuesses(
        Vector2 viewerLeftGuessDocumentPoint,
        Vector2 viewerRightGuessDocumentPoint,
        out Vector2 leftDocumentPoint,
        out Vector2 rightDocumentPoint,
        out float leftConfidence,
        out float rightConfidence,
        out string result,
        out string error)
    {
        return TryLocateNippleTargetsFromGuesses(
            viewerLeftGuessDocumentPoint,
            viewerRightGuessDocumentPoint,
            false,
            out leftDocumentPoint,
            out rightDocumentPoint,
            out leftConfidence,
            out rightConfidence,
            out result,
            out error);
    }

    private bool TryLocateNippleTargetsFromGuesses(
        Vector2 leftBoneGuess,
        Vector2 rightBoneGuess,
        bool guessesAreTrusted,
        out Vector2 leftDocumentPoint,
        out Vector2 rightDocumentPoint,
        out float leftConfidence,
        out float rightConfidence,
        out string result,
        out string error)
    {
        leftDocumentPoint = Vector2.zero;
        rightDocumentPoint = Vector2.zero;
        leftConfidence = 0f;
        rightConfidence = 0f;
        result = string.Empty;
        error = string.Empty;

        var leftAnatomyGuess = leftBoneGuess;
        var rightAnatomyGuess = rightBoneGuess;
        var shoulderSpan = Vector2.Distance(leftBoneGuess, rightBoneGuess);
        TryBuildNippleAnatomyGuesses(
            ref leftAnatomyGuess,
            ref rightAnatomyGuess,
            ref shoulderSpan);
        if (shoulderSpan <= 0.001f || Vector2.Distance(leftBoneGuess, rightBoneGuess) <= 0.001f)
        {
            error = "Nipple bone positions overlap.";
            return false;
        }

        var primarySearchRadius = Mathf.Max(
            guessesAreTrusted ? 10f : 16f,
            Vector2.Distance(leftBoneGuess, rightBoneGuess) * (guessesAreTrusted ? 0.34f : 0.5f));
        var anatomySearchRadius = Mathf.Max(8f, shoulderSpan * 0.17f);
        var leftDetected = TryRefineNippleTarget(
            true,
            leftBoneGuess,
            primarySearchRadius,
            guessesAreTrusted,
            out leftDocumentPoint,
            out leftConfidence,
            out var leftLayerName);
        var rightDetected = TryRefineNippleTarget(
            false,
            rightBoneGuess,
            primarySearchRadius,
            guessesAreTrusted,
            out rightDocumentPoint,
            out rightConfidence,
            out var rightLayerName);

        if (!leftDetected && (leftAnatomyGuess - leftBoneGuess).sqrMagnitude > 0.01f)
        {
            leftDetected = TryRefineNippleTarget(
                true,
                leftAnatomyGuess,
                anatomySearchRadius,
                false,
                out leftDocumentPoint,
                out leftConfidence,
                out leftLayerName);
        }
        if (!rightDetected && (rightAnatomyGuess - rightBoneGuess).sqrMagnitude > 0.01f)
        {
            rightDetected = TryRefineNippleTarget(
                false,
                rightAnatomyGuess,
                anatomySearchRadius,
                false,
                out rightDocumentPoint,
                out rightConfidence,
                out rightLayerName);
        }

        if (!leftDetected)
        {
            leftDocumentPoint = leftAnatomyGuess;
            leftConfidence = 0f;
            leftLayerName = string.IsNullOrWhiteSpace(leftLayerName)
                ? "anatomical fallback"
                : "anatomical fallback: " + leftLayerName;
        }
        if (!rightDetected)
        {
            rightDocumentPoint = rightAnatomyGuess;
            rightConfidence = 0f;
            rightLayerName = string.IsNullOrWhiteSpace(rightLayerName)
                ? "anatomical fallback"
                : "anatomical fallback: " + rightLayerName;
        }

        if (leftDetected && rightDetected)
        {
            var sideAxis = (rightAnatomyGuess - leftAnatomyGuess).normalized;
            var torsoAxis = new Vector2(-sideAxis.y, sideAxis.x);
            var leftDelta = leftDocumentPoint - leftAnatomyGuess;
            var rightDelta = rightDocumentPoint - rightAnatomyGuess;
            var verticalMismatch = Mathf.Abs(Vector2.Dot(leftDelta - rightDelta, torsoAxis));
            var mirroredSideMismatch = Mathf.Abs(Vector2.Dot(leftDelta + rightDelta, sideAxis));
            var pairError = (verticalMismatch + mirroredSideMismatch) / Mathf.Max(1f, anatomySearchRadius * 1.4f);
            var pairQuality = Mathf.Clamp01(1f - pairError);
            var pairFactor = Mathf.Lerp(0.65f, 1f, pairQuality);
            leftConfidence *= pairFactor;
            rightConfidence *= pairFactor;
        }

        result = $"left {Mathf.RoundToInt(leftConfidence * 100f)}% ({leftLayerName}), " +
                 $"right {Mathf.RoundToInt(rightConfidence * 100f)}% ({rightLayerName})";
        return true;
    }

    public bool TryConstrainBoobContourToVisibleSprite(
        Vector2 centerDocumentPoint,
        IReadOnlyList<Vector2> contourDocumentPoints,
        out Vector2[] constrainedDocumentPoints,
        out string layerName)
    {
        constrainedDocumentPoints = Array.Empty<Vector2>();
        layerName = string.Empty;
        if (contourDocumentPoints == null || contourDocumentPoints.Count < 3 ||
            !TryFindBoobConstraintLayer(centerDocumentPoint, out var layer) || layer == null)
        {
            return false;
        }

        layerName = CleanNippleLayerLabel(layer.name);
        var source = layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
        if (source == null)
            return false;

        var width = Mathf.Clamp(Mathf.RoundToInt(layer.sourceRect.width), 1, 4096);
        var height = Mathf.Clamp(Mathf.RoundToInt(layer.sourceRect.height), 1, 4096);
        Texture2D readable = null;
        try
        {
            if (!TryReadNippleLayerPixels(layer, source, width, height, out var pixels))
            {
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                var uvRect = PsdCoordinateSpace.LayerTextureUvRect(
                    layer,
                    new Vector2(source.width, source.height),
                    false);
                if (!TryReadReanimaxHardMaskSource(source, uvRect, readable))
                    return false;
                readable.Apply(false, false);
                pixels = readable.GetPixels32();
            }

            Vector2[] constraintDrawVertices = null;
            int[] constraintTriangles = null;
            if (TryGetLayerVertexData(layer, out var constraintData) && constraintData != null &&
                constraintData.triangles != null && constraintData.triangles.Length >= 3)
            {
                var drawVertices = GetLayerVerticesForDraw(constraintData);
                if (drawVertices != null && drawVertices.Length >= 3)
                {
                    constraintDrawVertices = drawVertices;
                    constraintTriangles = constraintData.triangles;
                }
            }

            var centerBase = MapNippleGuessToBase(layer, centerDocumentPoint);
            if (!IsOpaqueBoobLayerPoint(layer, pixels, width, height, centerBase))
            {
                // Mesh rest-pose mapping can fail for a point close to a newly
                // generated boundary. First try the same document point without
                // rest-pose remapping, then snap to the nearest opaque body pixel.
                if (IsOpaqueBoobLayerPoint(layer, pixels, width, height, centerDocumentPoint))
                {
                    centerBase = centerDocumentPoint;
                }
                else if (!TryFindNearestOpaqueBoobLayerPoint(
                             layer,
                             pixels,
                             width,
                             height,
                             centerBase,
                             out centerBase))
                {
                    return false;
                }
            }

            var constraintCenterDraw = MapNippleTargetToCurrentDraw(layer, centerBase);
            if (!IsInsideBoobConstraintGeometry(
                    constraintCenterDraw,
                    constraintDrawVertices,
                    constraintTriangles))
            {
                // Alpha is still authoritative. Disable only the optional mesh
                // guard when its rest/current mapping cannot represent the
                // corrected opaque center.
                constraintDrawVertices = null;
                constraintTriangles = null;
                constraintCenterDraw = centerDocumentPoint;
            }

            var constrained = new Vector2[contourDocumentPoints.Count];
            for (var i = 0; i < contourDocumentPoints.Count; i++)
            {
                var originalDraw = contourDocumentPoints[i];
                var originalBase = MapNippleGuessToBase(layer, originalDraw);
                var insideT = 0f;
                var outsideT = -1f;

                // Scan the complete ray even when its endpoint is opaque. A
                // contour ray may leave the body silhouette and later enter a
                // different opaque island (arm, hair overlap, another lobe).
                // The first alpha exit is the only valid sprite boundary.
                var centerLocal = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, centerBase);
                var targetLocal = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, originalBase);
                var pixelDelta = new Vector2(
                    (targetLocal.x - centerLocal.x) * width / Mathf.Max(1f, layer.sourceRect.width),
                    (targetLocal.y - centerLocal.y) * height / Mathf.Max(1f, layer.sourceRect.height));
                var raySteps = Mathf.Clamp(Mathf.CeilToInt(pixelDelta.magnitude * 2f), 32, 4096);
                for (var step = 1; step <= raySteps; step++)
                {
                    var sampleT = step / (float)raySteps;
                    var sampleBase = Vector2.Lerp(centerBase, originalBase, sampleT);
                    var sampleDraw = Vector2.Lerp(constraintCenterDraw, originalDraw, sampleT);
                    if (IsOpaqueBoobLayerPoint(layer, pixels, width, height, sampleBase) &&
                        IsInsideBoobConstraintGeometry(
                            sampleDraw,
                            constraintDrawVertices,
                            constraintTriangles))
                    {
                        insideT = sampleT;
                        continue;
                    }

                    outsideT = sampleT;
                    break;
                }

                if (outsideT < 0f)
                {
                    constrained[i] = originalDraw;
                    continue;
                }

                for (var step = 0; step < 18; step++)
                {
                    var middleT = (insideT + outsideT) * 0.5f;
                    var middleBase = Vector2.Lerp(centerBase, originalBase, middleT);
                    var middleDraw = Vector2.Lerp(constraintCenterDraw, originalDraw, middleT);
                    if (IsOpaqueBoobLayerPoint(layer, pixels, width, height, middleBase) &&
                        IsInsideBoobConstraintGeometry(
                            middleDraw,
                            constraintDrawVertices,
                            constraintTriangles))
                        insideT = middleT;
                    else
                        outsideT = middleT;
                }

                var drawDistance = Vector2.Distance(constraintCenterDraw, originalDraw);
                var insetT = Mathf.Max(0.012f, 2f / Mathf.Max(2f, drawDistance));
                constrained[i] = Vector2.Lerp(
                    constraintCenterDraw,
                    originalDraw,
                    Mathf.Max(0f, insideT - insetT));
            }

            constrainedDocumentPoints = constrained;
            return true;
        }
        finally
        {
            if (readable != null)
                UnityEngine.Object.DestroyImmediate(readable);
        }
    }

    private static bool IsInsideBoobConstraintGeometry(
        Vector2 documentPoint,
        Vector2[] drawVertices,
        int[] triangles)
    {
        if (drawVertices == null || drawVertices.Length < 3 ||
            triangles == null || triangles.Length < 3)
        {
            return true;
        }

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= drawVertices.Length || ib >= drawVertices.Length || ic >= drawVertices.Length)
            {
                continue;
            }

            if (TryGetNippleBarycentric(
                    documentPoint,
                    drawVertices[ia],
                    drawVertices[ib],
                    drawVertices[ic],
                    out _,
                    out _,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindNearestOpaqueBoobLayerPoint(
        LayerInfo layer,
        Color32[] pixels,
        int width,
        int height,
        Vector2 preferredBaseDocumentPoint,
        out Vector2 opaqueBaseDocumentPoint)
    {
        opaqueBaseDocumentPoint = preferredBaseDocumentPoint;
        if (layer == null || pixels == null || pixels.Length != width * height)
            return false;

        var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, preferredBaseDocumentPoint);
        var centerX = Mathf.Clamp(
            Mathf.RoundToInt(local.x * width / Mathf.Max(1f, layer.sourceRect.width)),
            0,
            width - 1);
        var centerY = Mathf.Clamp(
            Mathf.RoundToInt(local.y * height / Mathf.Max(1f, layer.sourceRect.height)),
            0,
            height - 1);
        var maxRadius = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(width, height) * 0.25f), 8, 1024);
        for (var radius = 0; radius <= maxRadius; radius++)
        {
            var minX = Mathf.Max(0, centerX - radius);
            var maxX = Mathf.Min(width - 1, centerX + radius);
            var minY = Mathf.Max(0, centerY - radius);
            var maxY = Mathf.Min(height - 1, centerY + radius);
            for (var x = minX; x <= maxX; x++)
            {
                if (TryUseOpaqueBoobPixel(layer, pixels, width, height, x, minY, out opaqueBaseDocumentPoint) ||
                    (maxY != minY && TryUseOpaqueBoobPixel(
                        layer, pixels, width, height, x, maxY, out opaqueBaseDocumentPoint)))
                {
                    return true;
                }
            }
            for (var y = minY + 1; y < maxY; y++)
            {
                if (TryUseOpaqueBoobPixel(layer, pixels, width, height, minX, y, out opaqueBaseDocumentPoint) ||
                    (maxX != minX && TryUseOpaqueBoobPixel(
                        layer, pixels, width, height, maxX, y, out opaqueBaseDocumentPoint)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryUseOpaqueBoobPixel(
        LayerInfo layer,
        Color32[] pixels,
        int width,
        int height,
        int x,
        int y,
        out Vector2 opaqueBaseDocumentPoint)
    {
        opaqueBaseDocumentPoint = default;
        if (x < 0 || y < 0 || x >= width || y >= height || pixels[y * width + x].a < 40)
            return false;

        var local = new Vector2(
            (x + 0.5f) * layer.sourceRect.width / width,
            (y + 0.5f) * layer.sourceRect.height / height);
        opaqueBaseDocumentPoint = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
        return true;
    }

    private bool TryFindBoobConstraintLayer(Vector2 centerDocumentPoint, out LayerInfo selectedLayer)
    {
        selectedLayer = null;
        if (layerInfos == null || layerInfos.Count == 0)
            return false;

        var bestPriority = int.MinValue;
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null || !layer.visible ||
                layer.sourceRect.width <= 0f || layer.sourceRect.height <= 0f)
            {
                continue;
            }

            var normalizedName = NormalizeNippleLayerName(layer.name);
            if (ContainsNippleLayerToken(normalizedName, "bra", "cloth", "shirt", "coat", "hair", "over"))
            {
                continue;
            }

            var baseCenter = MapNippleGuessToBase(layer, centerDocumentPoint);
            var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, baseCenter);
            if (local.x < 0f || local.y < 0f ||
                local.x > layer.sourceRect.width || local.y > layer.sourceRect.height)
            {
                // Layer selection must not depend exclusively on the live mesh.
                // During an automatic pipeline the geometry/bind caches can be in
                // transition, while the document-space point is still valid.
                baseCenter = centerDocumentPoint;
                local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, baseCenter);
                if (local.x < 0f || local.y < 0f ||
                    local.x > layer.sourceRect.width || local.y > layer.sourceRect.height)
                {
                    continue;
                }
            }

            var selected = IsLayerSelected(layer);
            var body = ContainsNippleLayerToken(normalizedName, "body", "torso", "skin");
            var mislabeledBody = string.Equals(normalizedName, "topwear", StringComparison.Ordinal);
            var topFallbackBody = string.Equals(normalizedName, "top", StringComparison.Ordinal);
            var boob = ContainsNippleLayerToken(normalizedName, "boob", "breast");
            var priority = body
                ? (selected ? 120 : 90)
                : mislabeledBody
                    ? (selected ? 110 : 85)
                    : boob
                        ? (selected ? 80 : 70)
                        : topFallbackBody
                            ? (selected ? 75 : 60)
                            : 0;
            if (priority <= bestPriority || priority <= 0)
                continue;

            bestPriority = priority;
            selectedLayer = layer;
        }

        return selectedLayer != null;
    }

    private static bool IsOpaqueBoobLayerPoint(
        LayerInfo layer,
        Color32[] pixels,
        int width,
        int height,
        Vector2 baseDocumentPoint)
    {
        if (layer == null || pixels == null || pixels.Length != width * height)
            return false;

        var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, baseDocumentPoint);
        if (local.x < 0f || local.y < 0f ||
            local.x >= layer.sourceRect.width || local.y >= layer.sourceRect.height)
        {
            return false;
        }

        var x = Mathf.Clamp(
            Mathf.FloorToInt(local.x * width / Mathf.Max(1f, layer.sourceRect.width)),
            0,
            width - 1);
        var y = Mathf.Clamp(
            Mathf.FloorToInt(local.y * height / Mathf.Max(1f, layer.sourceRect.height)),
            0,
            height - 1);
        return pixels[y * width + x].a >= 40;
    }

    private void TryBuildNippleAnatomyGuesses(
        ref Vector2 leftGuess,
        ref Vector2 rightGuess,
        ref float shoulderSpan)
    {
        if (!TryFindBoneIndexByName("hips", out var hipsIndex) ||
            !TryFindBoneIndexByName("neck", out var neckIndex) ||
            !TryFindBoneIndexByName("l_arm", out var leftArmIndex) ||
            !TryFindBoneIndexByName("r_arm", out var rightArmIndex) ||
            bones == null || hipsIndex < 0 || neckIndex < 0 || leftArmIndex < 0 || rightArmIndex < 0 ||
            hipsIndex >= bones.Count || neckIndex >= bones.Count ||
            leftArmIndex >= bones.Count || rightArmIndex >= bones.Count ||
            bones[hipsIndex] == null || bones[neckIndex] == null ||
            bones[leftArmIndex] == null || bones[rightArmIndex] == null)
        {
            return;
        }

        var hips = bones[hipsIndex].worldPosition;
        var neck = bones[neckIndex].worldPosition;
        var shoulderA = bones[leftArmIndex].worldPosition;
        var shoulderB = bones[rightArmIndex].worldPosition;
        var span = Vector2.Distance(shoulderA, shoulderB);
        if (Vector2.Distance(hips, neck) <= 0.001f || span <= 0.001f)
            return;

        // Auto Skeleton's boobs_root is intentionally high on the upper chest.
        // Nipple anchors sit lower: near the spine point on the hips-to-neck axis.
        var center = Vector2.Lerp(hips, neck, 0.56f);
        var viewerLeftShoulder = shoulderA.x <= shoulderB.x ? shoulderA : shoulderB;
        var viewerRightShoulder = shoulderA.x <= shoulderB.x ? shoulderB : shoulderA;
        var sideAxis = (viewerRightShoulder - viewerLeftShoulder).normalized;
        var halfNippleSpacing = span * 0.26f;
        leftGuess = center - sideAxis * halfNippleSpacing;
        rightGuess = center + sideAxis * halfNippleSpacing;
        shoulderSpan = span;
    }

    private bool TryFindNippleControlBone(bool left, out int boneIndex)
    {
        if (TryFindBoneIndexByName(left ? "nipple_l" : "nipple_r", out boneIndex))
            return true;
        return TryFindBoneIndexByName(left ? "l_nipple" : "r_nipple", out boneIndex);
    }

    private static Vector2 GetNippleControlPoint(BoneInfo bone)
    {
        if (bone == null)
            return Vector2.zero;

        var direction3 = bone.worldRotation * Vector3.right;
        var direction = new Vector2(direction3.x, direction3.y);
        if (direction.sqrMagnitude <= 0.000001f)
            direction = Vector2.right;
        return bone.worldPosition + direction.normalized * Mathf.Max(1f, bone.length);
    }

    private bool TryRefineNippleTarget(
        bool left,
        Vector2 guessDocument,
        float searchRadiusDocument,
        bool guessIsTrusted,
        out Vector2 targetDocument,
        out float confidence,
        out string layerName)
    {
        targetDocument = guessDocument;
        confidence = 0f;
        layerName = "no matching nipple layer";
        if (!TryFindNippleAnalysisLayer(left, guessDocument, searchRadiusDocument, out var layer) || layer == null)
            return false;

        layerName = CleanNippleLayerLabel(layer.name);
        var source = layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
        if (source == null)
        {
            layerName += " has no texture";
            return false;
        }

        var width = Mathf.Clamp(Mathf.RoundToInt(layer.sourceRect.width), 1, 4096);
        var height = Mathf.Clamp(Mathf.RoundToInt(layer.sourceRect.height), 1, 4096);
        Texture2D readable = null;

        try
        {
            if (!TryReadNippleLayerPixels(layer, source, width, height, out var pixels))
            {
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                var uvRect = PsdCoordinateSpace.LayerTextureUvRect(
                    layer,
                    new Vector2(source.width, source.height),
                    false);
                if (!TryReadReanimaxHardMaskSource(source, uvRect, readable))
                {
                    layerName += " texture read failed";
                    return false;
                }
                readable.Apply(false, false);
                pixels = readable.GetPixels32();
            }

            var baseGuessDocument = MapNippleGuessToBase(layer, guessDocument);
            var guessLocal = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, baseGuessDocument);
            var localScale = 0.5f * (
                width / Mathf.Max(1f, layer.sourceRect.width) +
                height / Mathf.Max(1f, layer.sourceRect.height));
            var guessPixel = guessLocal * localScale;
            var searchRadiusPixels = Mathf.Max(6f, searchRadiusDocument * localScale);
            var normalizedLayerName = NormalizeNippleLayerName(layer.name);
            var isDedicatedBoobLayer = ContainsNippleLayerToken(
                normalizedLayerName,
                "lboob",
                "leftboob",
                "rboob",
                "rightboob",
                "lbreast",
                "leftbreast",
                "rbreast",
                "rightbreast");
            var isCombinedTorsoLayer = string.Equals(normalizedLayerName, "topwear", StringComparison.Ordinal) ||
                                       ContainsNippleLayerToken(normalizedLayerName, "body", "torso", "skin");
            if (isDedicatedBoobLayer || isCombinedTorsoLayer)
            {
                if (!guessIsTrusted &&
                    TryFindReddestOpaqueNipplePixelInRegion(
                        pixels,
                        width,
                        height,
                        Mathf.Clamp01((guessPixel.x - searchRadiusPixels) / width),
                        Mathf.Clamp01((guessPixel.x + searchRadiusPixels) / width),
                        Mathf.Clamp01((guessPixel.y - searchRadiusPixels) / height),
                        Mathf.Clamp01((guessPixel.y + searchRadiusPixels) / height),
                        out var rednessPixel,
                        out var rednessConfidence) &&
                    rednessConfidence >= 0.2f &&
                    Vector2.Distance(rednessPixel, guessPixel) <= searchRadiusPixels * 1.15f)
                {
                    // Qwen is only a coarse search window. For an explicitly
                    // visible nipple, the strongest compact red/brown cluster in
                    // that window is a substantially better center than either a
                    // breast bounding box or generic circular-shadow scoring.
                    var rednessTargetLocal = rednessPixel / Mathf.Max(0.0001f, localScale);
                    var rednessBaseTarget = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, rednessTargetLocal);
                    // Pixel coordinates were sampled from the imported sprite
                    // itself and are already in document space. Mapping them
                    // through deformedVertices a second time makes targets wander
                    // whenever weights or rest-pose caches change.
                    targetDocument = rednessBaseTarget;
                    confidence = Mathf.Max(0.55f, rednessConfidence);
                    layerName = CleanNippleLayerLabel(layer.name) + ": local redness center";
                    return true;
                }

                // nipple_l / nipple_r already provide the semantic location in the
                // deformed preview. Search only in a small neighbourhood of that anchor;
                // a global redness maximum on a breast or full-body sprite is commonly
                // an edge, highlight or shadow and must never pull the result away.
                var anchorRadiusFactor = isDedicatedBoobLayer ? 0.12f : 0.08f;
                var anchorRadiusPixels = Mathf.Clamp(
                    Mathf.Min(searchRadiusPixels, Mathf.Min(width, height) * anchorRadiusFactor),
                    6f,
                    Mathf.Max(6f, Mathf.Min(width, height) * anchorRadiusFactor));
                if (TryFindBestNipplePixel(
                        pixels,
                        width,
                        height,
                        guessPixel,
                        anchorRadiusPixels,
                        out var anchorPixel,
                        out var anchorConfidence))
                {
                    var anchorTargetLocal = anchorPixel / Mathf.Max(0.0001f, localScale);
                    var anchorBaseTarget = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, anchorTargetLocal);
                    var anchorDrawTarget = MapNippleTargetToCurrentDraw(layer, anchorBaseTarget);
                    var maxAnchorShiftFactor = guessIsTrusted
                        ? isDedicatedBoobLayer ? 0.045f : 0.025f
                        : isDedicatedBoobLayer ? 0.1f : 0.08f;
                    var maxAnchorShift = Mathf.Max(
                        3f,
                        Mathf.Min(layer.sourceRect.width, layer.sourceRect.height) * maxAnchorShiftFactor);
                    if (Vector2.Distance(anchorDrawTarget, guessDocument) <= maxAnchorShift)
                    {
                        targetDocument = anchorDrawTarget;
                        confidence = Mathf.Max(0.35f, anchorConfidence);
                        layerName = CleanNippleLayerLabel(layer.name);
                        return true;
                    }
                }

                targetDocument = guessDocument;
                confidence = 0.65f;
                layerName = CleanNippleLayerLabel(layer.name) + ": bone anchor";
                return true;
            }
            if (!TryFindBestNipplePixel(
                    pixels,
                    width,
                    height,
                    guessPixel,
                    searchRadiusPixels,
                    out var targetPixel,
                    out confidence))
            {
                layerName += " has no pixel candidate";
                return false;
            }

            var targetLocal = targetPixel / Mathf.Max(0.0001f, localScale);
            targetDocument = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, targetLocal);
            targetDocument = MapNippleTargetToCurrentDraw(layer, targetDocument);
            layerName = CleanNippleLayerLabel(layer.name);
            return true;
        }
        finally
        {
            if (readable != null)
                UnityEngine.Object.DestroyImmediate(readable);
        }
    }

    private Vector2 MapNippleTargetToCurrentDraw(LayerInfo layer, Vector2 baseDocumentPoint)
    {
        if (!TryGetLayerVertexData(layer, out var data) || data == null ||
            data.vertices == null || data.vertices.Length < 3 ||
            data.triangles == null || data.triangles.Length < 3)
        {
            return baseDocumentPoint;
        }

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length ||
            ReferenceEquals(drawVertices, data.vertices))
        {
            return baseDocumentPoint;
        }

        return PsdCoordinateSpace.TryMapPointBetweenMeshes(
            data.vertices,
            drawVertices,
            data.triangles,
            baseDocumentPoint,
            out var mappedPoint)
            ? mappedPoint
            : baseDocumentPoint;
    }

    private Vector2 MapNippleGuessToBase(LayerInfo layer, Vector2 drawDocumentPoint)
    {
        if (!TryGetLayerVertexData(layer, out var data) || data == null ||
            data.vertices == null || data.vertices.Length < 3 ||
            data.triangles == null || data.triangles.Length < 3)
        {
            return drawDocumentPoint;
        }

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length ||
            ReferenceEquals(drawVertices, data.vertices))
        {
            return drawDocumentPoint;
        }

        return PsdCoordinateSpace.TryMapPointBetweenMeshes(
            drawVertices,
            data.vertices,
            data.triangles,
            drawDocumentPoint,
            out var mappedPoint)
            ? mappedPoint
            : drawDocumentPoint;
    }

    private bool TryReadNippleLayerPixels(
        LayerInfo layer,
        Texture2D source,
        int targetWidth,
        int targetHeight,
        out Color32[] pixels)
    {
        pixels = null;
        if (layer == null || source == null || targetWidth <= 0 || targetHeight <= 0 ||
            !TryGetTexturePixels(source, out var sourcePixels) || sourcePixels == null ||
            sourcePixels.Length < source.width * source.height)
        {
            return false;
        }

        // Imported PSD sprites are not guaranteed to be an axis-aligned slice of the
        // backing texture. In particular, their serialized sampling rect can be stale
        // while the mesh UVs are still correct. Rasterize through the same UV triangles
        // used by the preview renderer before falling back to rectangular atlas sampling.
        if (TryGetLayerVertexData(layer, out var data) && data != null &&
            TryRasterizeNippleLayerFromMeshUvs(
                layer,
                data,
                sourcePixels,
                source.width,
                source.height,
                targetWidth,
                targetHeight,
                out pixels))
        {
            return true;
        }

        var textureRect = PsdCoordinateSpace.LayerTextureRect(
            layer,
            new Vector2(source.width, source.height));
        if (textureRect.width <= 0f || textureRect.height <= 0f)
            return false;

        pixels = new Color32[targetWidth * targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Mathf.Clamp(
                Mathf.FloorToInt(textureRect.y + (y + 0.5f) * textureRect.height / targetHeight),
                0,
                source.height - 1);
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Mathf.Clamp(
                    Mathf.FloorToInt(textureRect.x + (x + 0.5f) * textureRect.width / targetWidth),
                    0,
                    source.width - 1);
                pixels[y * targetWidth + x] = sourcePixels[sourceY * source.width + sourceX];
            }
        }
        return true;
    }

    private static bool TryRasterizeNippleLayerFromMeshUvs(
        LayerInfo layer,
        LayerVertexData data,
        Color32[] sourcePixels,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        out Color32[] pixels)
    {
        pixels = null;
        if (layer == null || data == null || sourcePixels == null ||
            data.vertices == null || data.vertexUvs == null || data.triangles == null ||
            data.vertices.Length < 3 || data.vertexUvs.Length != data.vertices.Length ||
            data.triangles.Length < 3 || sourceWidth <= 0 || sourceHeight <= 0 ||
            sourcePixels.Length < sourceWidth * sourceHeight)
        {
            return false;
        }

        var logicalWidth = Mathf.Max(1f, layer.sourceRect.width);
        var logicalHeight = Mathf.Max(1f, layer.sourceRect.height);
        pixels = new Color32[targetWidth * targetHeight];
        var written = 0;

        for (var triangle = 0; triangle + 2 < data.triangles.Length; triangle += 3)
        {
            var ia = data.triangles[triangle];
            var ib = data.triangles[triangle + 1];
            var ic = data.triangles[triangle + 2];
            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= data.vertices.Length || ib >= data.vertices.Length || ic >= data.vertices.Length)
            {
                continue;
            }

            var a = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, data.vertices[ia]);
            var b = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, data.vertices[ib]);
            var c = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, data.vertices[ic]);
            var minX = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * targetWidth / logicalWidth),
                0,
                targetWidth - 1);
            var maxX = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * targetWidth / logicalWidth),
                0,
                targetWidth - 1);
            var minY = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) * targetHeight / logicalHeight),
                0,
                targetHeight - 1);
            var maxY = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) * targetHeight / logicalHeight),
                0,
                targetHeight - 1);

            for (var y = minY; y <= maxY; y++)
            {
                var localY = (y + 0.5f) * logicalHeight / targetHeight;
                for (var x = minX; x <= maxX; x++)
                {
                    var localX = (x + 0.5f) * logicalWidth / targetWidth;
                    if (!TryGetNippleBarycentric(new Vector2(localX, localY), a, b, c, out var wa, out var wb, out var wc))
                        continue;

                    var uv = data.vertexUvs[ia] * wa + data.vertexUvs[ib] * wb + data.vertexUvs[ic] * wc;
                    var sourceX = Mathf.Clamp(Mathf.FloorToInt(uv.x * sourceWidth), 0, sourceWidth - 1);
                    var sourceY = Mathf.Clamp(Mathf.FloorToInt(uv.y * sourceHeight), 0, sourceHeight - 1);
                    pixels[y * targetWidth + x] = sourcePixels[sourceY * sourceWidth + sourceX];
                    written++;
                }
            }
        }

        if (written > 0)
            return true;

        pixels = null;
        return false;
    }

    private static bool TryGetNippleBarycentric(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        out float wa,
        out float wb,
        out float wc)
    {
        wa = 0f;
        wb = 0f;
        wc = 0f;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = point - a;
        var denominator = v0.x * v1.y - v1.x * v0.y;
        if (Mathf.Abs(denominator) <= 1e-8f)
            return false;

        wb = (v2.x * v1.y - v1.x * v2.y) / denominator;
        wc = (v0.x * v2.y - v2.x * v0.y) / denominator;
        wa = 1f - wb - wc;
        const float tolerance = -0.0001f;
        return wa >= tolerance && wb >= tolerance && wc >= tolerance;
    }

    private bool TryFindNippleAnalysisLayer(
        bool left,
        Vector2 guessDocument,
        float searchRadiusDocument,
        out LayerInfo selectedLayer)
    {
        selectedLayer = null;
        if (layerInfos == null || layerInfos.Count == 0)
            return false;

        var bestPriority = int.MinValue;
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null || layer.sourceRect.width <= 0f || layer.sourceRect.height <= 0f)
                continue;

            var normalizedName = NormalizeNippleLayerName(layer.name);
            if (ContainsNippleLayerToken(normalizedName, "bra", "cloth", "shirt", "coat", "hair", "over") ||
                string.Equals(normalizedName, "top", StringComparison.Ordinal))
                continue;

            var isLeftLayer = ContainsNippleLayerToken(normalizedName, "lboob", "leftboob", "lbreast", "leftbreast");
            var isRightLayer = ContainsNippleLayerToken(normalizedName, "rboob", "rightboob", "rbreast", "rightbreast");
            if ((left && isRightLayer) || (!left && isLeftLayer))
                continue;
            var isRequestedDedicatedLayer = left ? isLeftLayer : isRightLayer;
            var isCombinedTorsoLayer = string.Equals(normalizedName, "topwear", StringComparison.Ordinal) ||
                                       ContainsNippleLayerToken(normalizedName, "body", "torso", "skin");

            var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, guessDocument);
            var padding = Mathf.Max(2f, searchRadiusDocument * 0.35f);
            if (!isRequestedDedicatedLayer && !isCombinedTorsoLayer &&
                (local.x < -padding || local.y < -padding ||
                 local.x > layer.sourceRect.width + padding ||
                 local.y > layer.sourceRect.height + padding))
            {
                continue;
            }

            var priority = left && isLeftLayer || !left && isRightLayer
                ? 100
                : string.Equals(normalizedName, "topwear", StringComparison.Ordinal)
                    ? 80
                : ContainsNippleLayerToken(normalizedName, "boob", "breast")
                    ? 75
                    : ContainsNippleLayerToken(normalizedName, "body", "torso", "skin")
                        ? 60
                        : IsLayerSelected(layer) ? 30 : 0;
            if (priority <= 0)
                continue;
            if (priority <= bestPriority)
                continue;

            bestPriority = priority;
            selectedLayer = layer;
        }

        return selectedLayer != null;
    }

    private static bool TryFindReddestOpaqueNipplePixel(
        Color32[] pixels,
        int width,
        int height,
        out Vector2 point,
        out float confidence)
    {
        return TryFindReddestOpaqueNipplePixelInRegion(
            pixels,
            width,
            height,
            0f,
            1f,
            0f,
            1f,
            out point,
            out confidence);
    }

    private static bool TryFindReddestOpaqueNipplePixelInRegion(
        Color32[] pixels,
        int width,
        int height,
        float normalizedXMin,
        float normalizedXMax,
        float normalizedYMin,
        float normalizedYMax,
        out Vector2 point,
        out float confidence)
    {
        point = Vector2.zero;
        confidence = 0f;
        if (pixels == null || pixels.Length != width * height || width < 9 || height < 9)
            return false;

        var contourMargin = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(width, height) * 0.025f), 3, 12);
        var bestRedness = float.MinValue;
        var bestX = 0;
        var bestY = 0;
        var xMin = Mathf.Clamp(Mathf.FloorToInt(width * normalizedXMin), contourMargin, width - contourMargin - 1);
        var xMax = Mathf.Clamp(Mathf.CeilToInt(width * normalizedXMax), xMin + 1, width - contourMargin);
        var yMin = Mathf.Clamp(Mathf.FloorToInt(height * normalizedYMin), contourMargin, height - contourMargin - 1);
        var yMax = Mathf.Clamp(Mathf.CeilToInt(height * normalizedYMax), yMin + 1, height - contourMargin);
        for (var y = yMin; y < yMax; y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                var pixel = pixels[y * width + x];
                if (pixel.a < 128 ||
                    pixels[y * width + x - contourMargin].a < 64 ||
                    pixels[y * width + x + contourMargin].a < 64 ||
                    pixels[(y - contourMargin) * width + x].a < 64 ||
                    pixels[(y + contourMargin) * width + x].a < 64)
                {
                    continue;
                }

                var redness = (pixel.r - (pixel.g + pixel.b) * 0.5f) / 255f;
                if (redness <= bestRedness)
                    continue;
                bestRedness = redness;
                bestX = x;
                bestY = y;
            }
        }

        if (bestRedness < 0.02f)
            return false;

        point = new Vector2(bestX, bestY);
        var refineRadius = Mathf.Max(6, Mathf.RoundToInt(Mathf.Min(width, height) * 0.08f));
        if (TryRefineNippleCenterByRedness(
                pixels,
                width,
                height,
                point,
                refineRadius,
                out var refinedPoint))
        {
            point = refinedPoint;
        }

        confidence = Mathf.Clamp01((bestRedness - 0.02f) / 0.25f);
        return true;
    }

    private static bool TryGetNippleOpaqueCenter(
        Color32[] pixels,
        int width,
        int height,
        out Vector2 center)
    {
        center = Vector2.zero;
        if (pixels == null || pixels.Length != width * height)
            return false;

        double sumX = 0d;
        double sumY = 0d;
        double sumWeight = 0d;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = pixels[y * width + x].a / 255f;
                if (alpha <= 0.1f)
                    continue;
                sumX += x * alpha;
                sumY += y * alpha;
                sumWeight += alpha;
            }
        }

        if (sumWeight <= 0.001d)
            return false;
        center = new Vector2((float)(sumX / sumWeight), (float)(sumY / sumWeight));
        return true;
    }

    private static bool TryFindBestNipplePixel(
        Color32[] pixels,
        int width,
        int height,
        Vector2 guess,
        float searchRadius,
        out Vector2 bestPoint,
        out float confidence)
    {
        bestPoint = guess;
        confidence = 0f;
        if (pixels == null || pixels.Length != width * height || width < 3 || height < 3)
            return false;

        var minX = Mathf.Clamp(Mathf.FloorToInt(guess.x - searchRadius), 1, width - 2);
        var maxX = Mathf.Clamp(Mathf.CeilToInt(guess.x + searchRadius), 1, width - 2);
        var minY = Mathf.Clamp(Mathf.FloorToInt(guess.y - searchRadius), 1, height - 2);
        var maxY = Mathf.Clamp(Mathf.CeilToInt(guess.y + searchRadius), 1, height - 2);
        var searchStep = Mathf.Max(1, Mathf.RoundToInt(searchRadius / 42f));
        var minSpotRadius = Mathf.Max(2, Mathf.RoundToInt(searchRadius * 0.045f));
        var maxSpotRadius = Mathf.Max(minSpotRadius + 1, Mathf.RoundToInt(searchRadius * 0.18f));
        var bestScore = float.MinValue;
        var bestContrast = 0f;
        var bestSpotRadius = 0;

        for (var y = minY; y <= maxY; y += searchStep)
        {
            for (var x = minX; x <= maxX; x += searchStep)
            {
                var delta = new Vector2(x - guess.x, y - guess.y);
                if (delta.sqrMagnitude > searchRadius * searchRadius)
                    continue;

                for (var scaleIndex = 0; scaleIndex < 4; scaleIndex++)
                {
                    var spotRadius = Mathf.RoundToInt(Mathf.Lerp(minSpotRadius, maxSpotRadius, scaleIndex / 3f));
                    if (!TryScoreNipplePixel(
                            pixels,
                            width,
                            height,
                            x,
                            y,
                            spotRadius,
                            delta.magnitude / Mathf.Max(1f, searchRadius),
                            out var score,
                            out var contrast))
                    {
                        continue;
                    }

                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    bestContrast = contrast;
                    bestSpotRadius = spotRadius;
                    bestPoint = new Vector2(x, y);
                }
            }
        }

        if (bestScore <= float.MinValue || bestContrast < 0.025f)
            return false;

        if (bestSpotRadius > 0 && TryRefineNippleCenterByRedness(
                pixels,
                width,
                height,
                bestPoint,
                bestSpotRadius,
                out var refinedPoint))
        {
            bestPoint = refinedPoint;
        }

        confidence = Mathf.Clamp01((bestScore - 0.28f) / 0.5f);
        return confidence > 0.01f;
    }

    private static bool TryScoreNipplePixel(
        Color32[] pixels,
        int width,
        int height,
        int centerX,
        int centerY,
        int spotRadius,
        float normalizedDistance,
        out float score,
        out float contrast)
    {
        score = 0f;
        contrast = 0f;
        var centerSum = Vector3.zero;
        var centerCount = 0;
        var centerSampleRadius = Mathf.Max(1, Mathf.RoundToInt(spotRadius * 0.38f));
        for (var oy = -1; oy <= 1; oy++)
        {
            for (var ox = -1; ox <= 1; ox++)
            {
                if (!TrySampleNipplePixel(
                        pixels,
                        width,
                        height,
                        centerX + ox * centerSampleRadius,
                        centerY + oy * centerSampleRadius,
                        out var color))
                {
                    return false;
                }
                centerSum += color;
                centerCount++;
            }
        }

        var ringSum = Vector3.zero;
        var ringSquareSum = Vector3.zero;
        const int ringSamples = 16;
        var ringRadius = Mathf.Max(2f, spotRadius * 1.45f);
        for (var sample = 0; sample < ringSamples; sample++)
        {
            var angle = sample * Mathf.PI * 2f / ringSamples;
            var x = centerX + Mathf.RoundToInt(Mathf.Cos(angle) * ringRadius);
            var y = centerY + Mathf.RoundToInt(Mathf.Sin(angle) * ringRadius);
            if (!TrySampleNipplePixel(pixels, width, height, x, y, out var color))
                return false;
            ringSum += color;
            ringSquareSum += Vector3.Scale(color, color);
        }

        var centerMean = centerSum / Mathf.Max(1, centerCount);
        var ringMean = ringSum / ringSamples;
        contrast = (centerMean - ringMean).magnitude / Mathf.Sqrt(3f);
        var ringVariance = ringSquareSum / ringSamples - Vector3.Scale(ringMean, ringMean);
        var ringStandardDeviation = Mathf.Sqrt(Mathf.Max(0f, (ringVariance.x + ringVariance.y + ringVariance.z) / 3f));
        var contrastScore = Mathf.Clamp01(contrast * 5f);
        var radialConsistency = Mathf.Clamp01(1f - ringStandardDeviation * 4.5f);
        var anatomyProximity = Mathf.Clamp01(1f - normalizedDistance);
        var centerRedness = centerMean.x - (centerMean.y + centerMean.z) * 0.5f;
        var ringRedness = ringMean.x - (ringMean.y + ringMean.z) * 0.5f;
        var rednessScore = Mathf.Clamp01(Mathf.Max(0f, centerRedness - ringRedness) * 10f);
        score = contrastScore * Mathf.Lerp(0.4f, 1f, radialConsistency) * 0.45f +
                rednessScore * 0.25f +
                radialConsistency * 0.15f +
                anatomyProximity * 0.15f;
        return true;
    }

    private static bool TryRefineNippleCenterByRedness(
        Color32[] pixels,
        int width,
        int height,
        Vector2 coarsePoint,
        int spotRadius,
        out Vector2 refinedPoint)
    {
        refinedPoint = coarsePoint;
        var radius = Mathf.Max(2, Mathf.RoundToInt(spotRadius * 0.8f));
        var centerX = Mathf.RoundToInt(coarsePoint.x);
        var centerY = Mathf.RoundToInt(coarsePoint.y);
        var maxRedness = float.MinValue;
        for (var y = Mathf.Max(0, centerY - radius); y <= Mathf.Min(height - 1, centerY + radius); y++)
        {
            for (var x = Mathf.Max(0, centerX - radius); x <= Mathf.Min(width - 1, centerX + radius); x++)
            {
                if ((new Vector2(x - centerX, y - centerY)).sqrMagnitude > radius * radius)
                    continue;
                var pixel = pixels[y * width + x];
                if (pixel.a < 64)
                    continue;
                var redness = (pixel.r - (pixel.g + pixel.b) * 0.5f) / 255f;
                if (redness > maxRedness)
                    maxRedness = redness;
            }
        }

        if (maxRedness <= float.MinValue)
            return false;

        var threshold = maxRedness - 0.03f;
        var weightedPoint = Vector2.zero;
        var totalWeight = 0f;
        for (var y = Mathf.Max(0, centerY - radius); y <= Mathf.Min(height - 1, centerY + radius); y++)
        {
            for (var x = Mathf.Max(0, centerX - radius); x <= Mathf.Min(width - 1, centerX + radius); x++)
            {
                if ((new Vector2(x - centerX, y - centerY)).sqrMagnitude > radius * radius)
                    continue;
                var pixel = pixels[y * width + x];
                if (pixel.a < 64)
                    continue;
                var redness = (pixel.r - (pixel.g + pixel.b) * 0.5f) / 255f;
                if (redness < threshold)
                    continue;
                var weight = Mathf.Max(0.0001f, redness - threshold);
                weightedPoint += new Vector2(x, y) * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0.0001f)
            return false;
        refinedPoint = weightedPoint / totalWeight;
        return true;
    }

    private static bool TrySampleNipplePixel(
        Color32[] pixels,
        int width,
        int height,
        int x,
        int y,
        out Vector3 color)
    {
        color = Vector3.zero;
        if (x < 0 || y < 0 || x >= width || y >= height)
            return false;
        var pixel = pixels[y * width + x];
        if (pixel.a < 64)
            return false;
        color = new Vector3(pixel.r / 255f, pixel.g / 255f, pixel.b / 255f);
        return true;
    }

    private static bool ContainsNippleLayerToken(string normalizedName, params string[] tokens)
    {
        if (string.IsNullOrEmpty(normalizedName) || tokens == null)
            return false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!string.IsNullOrEmpty(tokens[i]) && normalizedName.IndexOf(tokens[i], StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }

    private static string NormalizeNippleLayerName(string layerName)
    {
        return (layerName ?? string.Empty)
            .Replace("\0", string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static string CleanNippleLayerLabel(string layerName)
    {
        var cleaned = (layerName ?? string.Empty).Replace("\0", string.Empty).Trim();
        return string.IsNullOrEmpty(cleaned) ? "unnamed layer" : cleaned;
    }

    public bool ApplyBoobV2(out string result, out string error)
    {
        result = string.Empty;
        error = string.Empty;
        if (!CanApplyBoobV2())
        {
            error = "Set two targets and draw both breast contours first";
            return false;
        }

        var firstTarget = boobV2TargetADoc;
        var secondTarget = boobV2TargetBDoc;
        IReadOnlyList<Vector2> firstContour = boobV2ContourADoc;
        IReadOnlyList<Vector2> secondContour = boobV2ContourBDoc;
        if (firstTarget.x > secondTarget.x)
        {
            var targetSwap = firstTarget;
            firstTarget = secondTarget;
            secondTarget = targetSwap;
            var contourSwap = firstContour;
            firstContour = secondContour;
            secondContour = contourSwap;
        }

        if (!IsPointInsideFullBoobContour(firstTarget, firstContour) ||
            !IsPointInsideFullBoobContour(secondTarget, secondContour))
        {
            error = "Each target must be inside its breast contour";
            return false;
        }

        if (!TryApplyFullBoobsWeights(
                firstTarget,
                secondTarget,
                firstContour,
                secondContour,
                out result,
                out error))
        {
            return false;
        }

        var snappedBones = SnapBoobV2NippleBonesToTargets(firstTarget, secondTarget);
        result = "Boob V2 manual contours applied; " + result;
        if (snappedBones > 0)
            result += "; snapped " + snappedBones + " nipple bone" + (snappedBones == 1 ? string.Empty : "s") + " to targets";
        return true;
    }

    private int SnapBoobV2NippleBonesToTargets(
        Vector2 viewerLeftTargetDocumentPoint,
        Vector2 viewerRightTargetDocumentPoint)
    {
        if (!TryFindFullBoobRendererBones(out var firstNippleBone, out var secondNippleBone) ||
            firstNippleBone < 0 || secondNippleBone < 0 ||
            firstNippleBone >= bones.Count || secondNippleBone >= bones.Count ||
            bones[firstNippleBone] == null || bones[secondNippleBone] == null)
        {
            return 0;
        }

        var viewerLeftBone = GetBoneBasePoint(bones[firstNippleBone]).x <=
                             GetBoneBasePoint(bones[secondNippleBone]).x
            ? firstNippleBone
            : secondNippleBone;
        var viewerRightBone = viewerLeftBone == firstNippleBone
            ? secondNippleBone
            : firstNippleBone;

        var moved = 0;
        moved += SetBoobV2NippleBoneWorldPosition(viewerLeftBone, viewerLeftTargetDocumentPoint) ? 1 : 0;
        moved += SetBoobV2NippleBoneWorldPosition(viewerRightBone, viewerRightTargetDocumentPoint) ? 1 : 0;
        if (moved <= 0)
            return 0;

        RecomputeBoneWorldTransforms();
        InvalidateBonePose();
        MarkBoneStateChanged();
        CommitCurrentBonePoseAsRest();
        return moved;
    }

    private bool SetBoobV2NippleBoneWorldPosition(int boneIndex, Vector2 worldPosition)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count || bones[boneIndex] == null)
            return false;

        var bone = bones[boneIndex];
        if ((GetBoneBasePoint(bone) - worldPosition).sqrMagnitude <= 0.000001f)
            return false;
        bone.position = ConvertBoneWorldPositionToLocal(bone, worldPosition);
        bones[boneIndex] = bone;
        return true;
    }

    public bool ApplyBoob(out string error)
    {
        error = null;
        if (!boobHasPoint)
        {
            error = "Set point in preview first";
            return false;
        }

        if (!TryGetBoobLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite geometry first";
            return false;
        }

        RegisterUndo("Apply Boob");
        if (!TryBuildBoobGeometry(
                data,
                true,
                out var newVertices,
                out var triangleArray,
                out var allEdgePairs,
                out var boundaryPairs,
                out var newWeights,
                out _,
                out error))
        {
            return false;
        }

        var vertexUvs = BuildVertexUvsFromLayer(data.layer, newVertices);
        data.vertices = newVertices;
        data.vertexUvs = vertexUvs ?? Array.Empty<Vector2>();
        data.boneWeights = newWeights ?? Array.Empty<BoneWeight>();
        data.triangles = triangleArray ?? Array.Empty<int>();
        data.allEdgePairs = allEdgePairs ?? Array.Empty<int>();
        data.boundaryEdgePairs = boundaryPairs ?? Array.Empty<int>();
        data.hasSkinning = data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!string.IsNullOrEmpty(selectionKey))
            selectedLayerSelectionKey = selectionKey;
        editGeometrySelectedVertexIndex = -1;
        if (editGeometrySelectedVertexIndices != null)
            editGeometrySelectedVertexIndices.Clear();
        editGeometrySelectedLayerStableKey = stableKey ?? string.Empty;
        createEdgeStartVertexIndex = -1;
        createEdgeLayerStableKey = stableKey ?? string.Empty;
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private bool TryBuildBoobGeometry(
        LayerVertexData data,
        bool appendMissingBones,
        out Vector2[] vertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs,
        out BoneWeight[] boneWeights,
        out int[] resolvedSpriteBoneMap,
        out string error)
    {
        vertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        boneWeights = Array.Empty<BoneWeight>();
        resolvedSpriteBoneMap = data != null && data.spriteBoneToRendererBone != null
            ? data.spriteBoneToRendererBone
            : Array.Empty<int>();
        error = null;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
        {
            error = "Geometry is empty";
            return false;
        }

        var vertexCount = data.vertices.Length;
        var boundarySource = data.boundaryEdgePairs;
        var contourMask = BuildContourVertexMask(boundarySource, vertexCount);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
            if (fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6)
            {
                boundarySource = fallbackBoundaryPairs;
                contourMask = BuildContourVertexMask(boundarySource, vertexCount);
            }
        }

        if (CountTrue(contourMask) < 3)
        {
            error = "Failed to resolve contour";
            return false;
        }

        if (!TryBuildBoundaryLoopFromPairs(boundarySource, vertexCount, out var contourLoop))
            contourLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        if (contourLoop == null || contourLoop.Count < 3)
        {
            error = "Contour is invalid";
            return false;
        }

        var contourPoints = new List<Vector2>(contourLoop.Count);
        for (var i = 0; i < contourLoop.Count; i++)
        {
            var vertexIndex = contourLoop[i];
            if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
                continue;
            contourPoints.Add(data.vertices[vertexIndex]);
        }

        var contourCount = contourPoints.Count;
        if (contourCount < 3)
        {
            error = "Contour collapsed";
            return false;
        }

        var center = boobCenterDoc;
        if (!TryResolveBoobTwoBoneSpriteIndices(
                data,
                center,
                appendMissingBones,
                out var parentSpriteBone,
                out var childSpriteBone,
                out resolvedSpriteBoneMap))
        {
            error = "Failed to resolve two-bone influence";
            return false;
        }

        var outline = Mathf.Clamp(BoobOutline, 0.001f, 0.999f);
        var stages = Mathf.Clamp(BoobStages, 0, 16);
        var ringLerps = new List<float>(stages + 1) { outline };
        for (var stage = 1; stage <= stages; stage++)
            ringLerps.Add(Mathf.Lerp(outline, 1f, stage / (stages + 1f)));

        var ringCount = ringLerps.Count + 1; // contour + inner rings
        var newVertexCount = contourCount * ringCount + 1;
        vertices = new Vector2[newVertexCount];
        boneWeights = new BoneWeight[newVertexCount];
        var ringStarts = new int[ringCount];

        ringStarts[0] = 0;
        for (var i = 0; i < contourCount; i++)
        {
            vertices[i] = contourPoints[i];
            boneWeights[i] = BuildLimbTwoBoneWeight(parentSpriteBone, 1f, childSpriteBone, 0f);
        }

        for (var ring = 1; ring < ringCount; ring++)
        {
            var t = ringLerps[ring - 1];
            var ringStart = ring * contourCount;
            ringStarts[ring] = ringStart;
            for (var i = 0; i < contourCount; i++)
            {
                var index = ringStart + i;
                vertices[index] = Vector2.Lerp(contourPoints[i], center, t);
                boneWeights[index] = BuildLimbTwoBoneWeight(parentSpriteBone, 1f - t, childSpriteBone, t);
            }
        }

        var centerIndex = newVertexCount - 1;
        vertices[centerIndex] = center;
        boneWeights[centerIndex] = BuildLimbTwoBoneWeight(parentSpriteBone, 0f, childSpriteBone, 1f);

        var rebuiltTriangles = new List<int>(Mathf.Max(3, (ringCount - 1) * contourCount * 6 + contourCount * 3));
        for (var ring = 0; ring < ringCount - 1; ring++)
        {
            var currStart = ringStarts[ring];
            var nextStart = ringStarts[ring + 1];
            for (var i = 0; i < contourCount; i++)
            {
                var nextI = (i + 1) % contourCount;
                var a = currStart + i;
                var b = nextStart + i;
                var c = nextStart + nextI;
                var d = currStart + nextI;
                rebuiltTriangles.Add(a);
                rebuiltTriangles.Add(b);
                rebuiltTriangles.Add(c);
                rebuiltTriangles.Add(a);
                rebuiltTriangles.Add(c);
                rebuiltTriangles.Add(d);
            }
        }

        var lastRingStart = ringStarts[ringCount - 1];
        for (var i = 0; i < contourCount; i++)
        {
            var nextI = (i + 1) % contourCount;
            rebuiltTriangles.Add(lastRingStart + i);
            rebuiltTriangles.Add(lastRingStart + nextI);
            rebuiltTriangles.Add(centerIndex);
        }

        triangles = SanitizeTriangles(rebuiltTriangles.ToArray(), vertices.Length);
        if (triangles == null || triangles.Length < 3)
        {
            error = "Failed to build valid triangles";
            return false;
        }

        var contourLoopRemapped = new List<int>(contourCount);
        for (var i = 0; i < contourCount; i++)
            contourLoopRemapped.Add(i);
        boundaryEdgePairs = BuildBoundaryPairsFromLoop(contourLoopRemapped);
        if (boundaryEdgePairs == null || boundaryEdgePairs.Length < 6)
        {
            error = "Failed to rebuild contour edges";
            return false;
        }

        BuildEdgePairs(triangles, vertices.Length, out allEdgePairs, out _);
        if (allEdgePairs == null)
            allEdgePairs = Array.Empty<int>();
        return true;
    }

    private bool TryGetBoobLayerData(out LayerVertexData data, out string selectionKey, out string stableKey)
    {
        data = null;
        selectionKey = selectedLayerSelectionKey;
        stableKey = boobLayerStableKey;

        if (!string.IsNullOrEmpty(selectionKey) &&
            TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) &&
            data != null)
        {
            stableKey = BuildKey(data.layer.name, data.layer.layerId);
            boobLayerStableKey = stableKey;
            return true;
        }

        if (!string.IsNullOrEmpty(stableKey) &&
            TryGetLayerVertexDataByStableKey(stableKey, out data, out var resolvedSelectionKey) &&
            data != null)
        {
            selectionKey = resolvedSelectionKey;
            selectedLayerSelectionKey = selectionKey;
            boobLayerStableKey = stableKey;
            return true;
        }

        return false;
    }

    private bool TryResolveBoobTwoBoneSpriteIndices(
        LayerVertexData data,
        Vector2 centerBaseDoc,
        bool appendMissingBones,
        out int parentSpriteBone,
        out int childSpriteBone,
        out int[] resolvedSpriteBoneMap)
    {
        parentSpriteBone = -1;
        childSpriteBone = -1;
        resolvedSpriteBoneMap = data != null && data.spriteBoneToRendererBone != null
            ? data.spriteBoneToRendererBone
            : Array.Empty<int>();
        if (data == null)
            return false;
        if (bones == null || bones.Count == 0)
            return false;

        var rendererToSprite = BuildRendererToSpriteBoneMap(data.spriteBoneToRendererBone);
        if (!TryFindBoobRendererBonePair(
                data,
                centerBaseDoc,
                rendererToSprite,
                false,
                out var parentRendererBone,
                out var childRendererBone))
        {
            return false;
        }

        if (appendMissingBones)
        {
            if (!rendererToSprite.TryGetValue(parentRendererBone, out parentSpriteBone) &&
                !TryAppendSkinningTargetBone(data, parentRendererBone, out parentSpriteBone))
            {
                return false;
            }

            rendererToSprite = BuildRendererToSpriteBoneMap(data.spriteBoneToRendererBone);
            if (!rendererToSprite.TryGetValue(childRendererBone, out childSpriteBone) &&
                !TryAppendSkinningTargetBone(data, childRendererBone, out childSpriteBone))
            {
                return false;
            }

            resolvedSpriteBoneMap = data.spriteBoneToRendererBone ?? Array.Empty<int>();
            return parentSpriteBone >= 0 && childSpriteBone >= 0 && parentSpriteBone != childSpriteBone;
        }

        var previewMap = new List<int>(data.spriteBoneToRendererBone ?? Array.Empty<int>());
        parentSpriteBone = previewMap.IndexOf(parentRendererBone);
        if (parentSpriteBone < 0)
        {
            parentSpriteBone = previewMap.Count;
            previewMap.Add(parentRendererBone);
        }

        childSpriteBone = previewMap.IndexOf(childRendererBone);
        if (childSpriteBone < 0)
        {
            childSpriteBone = previewMap.Count;
            previewMap.Add(childRendererBone);
        }

        resolvedSpriteBoneMap = previewMap.ToArray();
        return parentSpriteBone != childSpriteBone;
    }

    private bool TryFindBoobRendererBonePair(
        LayerVertexData data,
        Vector2 centerBaseDoc,
        IReadOnlyDictionary<int, int> rendererToSprite,
        bool preferMappedPair,
        out int parentRendererBone,
        out int childRendererBone)
    {
        parentRendererBone = -1;
        childRendererBone = -1;
        var layerName = NormalizeAutoWeightName(data != null && data.layer != null ? data.layer.name : string.Empty);
        var wantsLeft = layerName.StartsWith("lboob", StringComparison.OrdinalIgnoreCase) ||
                        layerName.IndexOf("leftboob", StringComparison.OrdinalIgnoreCase) >= 0;
        var wantsRight = layerName.StartsWith("rboob", StringComparison.OrdinalIgnoreCase) ||
                         layerName.IndexOf("rightboob", StringComparison.OrdinalIgnoreCase) >= 0;
        var bestPriority = int.MaxValue;
        var bestDistanceSqr = float.MaxValue;
        for (var i = 0; i < bones.Count; i++)
        {
            var childBone = bones[i];
            if (childBone == null)
                continue;

            var parentIndex = childBone.parentId;
            if (parentIndex < 0 || parentIndex >= bones.Count ||
                bones[parentIndex] == null)
            {
                continue;
            }
            if (preferMappedPair &&
                (rendererToSprite == null ||
                 !rendererToSprite.ContainsKey(i) ||
                 !rendererToSprite.ContainsKey(parentIndex)))
            {
                continue;
            }

            var childName = NormalizeAutoWeightName(childBone.name);
            var parentName = NormalizeAutoWeightName(bones[parentIndex].name);
            var isNipple = childName.IndexOf("nipple", StringComparison.OrdinalIgnoreCase) >= 0;
            var isLeftNipple = childName.StartsWith("lnipple", StringComparison.OrdinalIgnoreCase) ||
                               childName.EndsWith("nipplel", StringComparison.OrdinalIgnoreCase);
            var isRightNipple = childName.StartsWith("rnipple", StringComparison.OrdinalIgnoreCase) ||
                                childName.EndsWith("nippler", StringComparison.OrdinalIgnoreCase);
            var sideMatches = (!wantsLeft && !wantsRight) ||
                              (wantsLeft && isLeftNipple) ||
                              (wantsRight && isRightNipple);
            var parentIsBoob = parentName.IndexOf("boob", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               parentName.IndexOf("breast", StringComparison.OrdinalIgnoreCase) >= 0;
            var priority = isNipple && sideMatches
                ? 0
                : isNipple
                    ? 1
                    : parentIsBoob
                        ? 2
                        : 3;

            var basePoint = GetBoneBasePoint(childBone);
            var distanceSqr = (basePoint - centerBaseDoc).sqrMagnitude;
            if (priority > bestPriority || (priority == bestPriority && distanceSqr >= bestDistanceSqr))
                continue;

            bestPriority = priority;
            bestDistanceSqr = distanceSqr;
            parentRendererBone = parentIndex;
            childRendererBone = i;
        }

        if (parentRendererBone >= 0 && childRendererBone >= 0)
            return true;
        if (!preferMappedPair)
            return false;

        return TryFindBoobRendererBonePair(
            data,
            centerBaseDoc,
            rendererToSprite,
            false,
            out parentRendererBone,
            out childRendererBone);
    }

    public bool TryApplyFullBoobsWeights(
        Vector2 viewerLeftNippleDocumentPoint,
        Vector2 viewerRightNippleDocumentPoint,
        IReadOnlyList<Vector2> viewerLeftContourDocumentPoints,
        IReadOnlyList<Vector2> viewerRightContourDocumentPoints,
        out string result,
        out string error)
    {
        result = string.Empty;
        error = string.Empty;
        if (viewerLeftContourDocumentPoints == null || viewerLeftContourDocumentPoints.Count < 3 ||
            viewerRightContourDocumentPoints == null || viewerRightContourDocumentPoints.Count < 3)
        {
            error = "Detect both breast contours first";
            return false;
        }

        if (!TryFindFullBoobRendererBones(out var firstBoobBone, out var secondBoobBone))
        {
            error = "Bones l_nipple and r_nipple are required";
            return false;
        }
        if (!TryFindFullBoobSpineRendererBone(out var spineRendererBone))
        {
            error = "Bone spine is required";
            return false;
        }

        var middle = (viewerLeftNippleDocumentPoint + viewerRightNippleDocumentPoint) * 0.5f;
        if (!TryFindBoobConstraintLayer(middle, out var layer) || layer == null)
        {
            if (!TryFindBoobConstraintLayer(viewerLeftNippleDocumentPoint, out layer) || layer == null)
            {
                error = "Body or topwear sprite was not found";
                return false;
            }
        }
        if (!TryGetLayerVertexData(layer, out var data) || data == null ||
            data.vertices == null || data.vertices.Length < 3)
        {
            error = "Body geometry is empty";
            return false;
        }

        var leftCenter = MapNippleGuessToBase(layer, viewerLeftNippleDocumentPoint);
        var rightCenter = MapNippleGuessToBase(layer, viewerRightNippleDocumentPoint);
        var mappedLeftContour = MapFullBoobContourToBase(layer, viewerLeftContourDocumentPoints);
        var mappedRightContour = MapFullBoobContourToBase(layer, viewerRightContourDocumentPoints);
        if (!TryRepairFullBoobContourPair(
                leftCenter,
                rightCenter,
                mappedLeftContour,
                mappedRightContour,
                out var leftContour,
                out var rightContour))
        {
            error = "Detected breast contours are invalid";
            return false;
        }

        // l/r bone names describe character space. Match them to viewer-left and
        // viewer-right contours spatially so mirrored characters remain correct.
        var directCost = (GetBoneBasePoint(bones[firstBoobBone]) - leftCenter).sqrMagnitude +
                         (GetBoneBasePoint(bones[secondBoobBone]) - rightCenter).sqrMagnitude;
        var swappedCost = (GetBoneBasePoint(bones[secondBoobBone]) - leftCenter).sqrMagnitude +
                          (GetBoneBasePoint(bones[firstBoobBone]) - rightCenter).sqrMagnitude;
        var viewerLeftBoobRenderer = directCost <= swappedCost ? firstBoobBone : secondBoobBone;
        var viewerRightBoobRenderer = directCost <= swappedCost ? secondBoobBone : firstBoobBone;

        var previousVertices = CloneArray(data.vertices);
        var previousUvs = CloneArray(data.vertexUvs);
        var previousWeights = CloneArray(data.boneWeights);
        var previousTriangles = CloneArray(data.triangles);
        var previousAllEdges = CloneArray(data.allEdgePairs);
        var previousBoundaryEdges = CloneArray(data.boundaryEdgePairs);
        var previousBoneMap = CloneArray(data.spriteBoneToRendererBone);
        var previousBindMatrices = CloneArray(data.bindBoneInverseMatrices);
        var previousDeformedVertices = CloneArray(data.deformedVertices);
        var previousSkinMatrices = CloneArray(data.skinMatrices);
        var previousUsesSpriteBindPoses = data.usesSpriteBindPoses;
        var previousHasSkinning = data.hasSkinning;
        var previousLastPoseVersion = data.lastPoseVersion;

        void RestorePreviousState()
        {
            data.vertices = previousVertices;
            data.vertexUvs = previousUvs;
            data.boneWeights = previousWeights;
            data.triangles = previousTriangles;
            data.allEdgePairs = previousAllEdges;
            data.boundaryEdgePairs = previousBoundaryEdges;
            data.spriteBoneToRendererBone = previousBoneMap;
            data.bindBoneInverseMatrices = previousBindMatrices;
            data.deformedVertices = previousDeformedVertices;
            data.skinMatrices = previousSkinMatrices;
            data.usesSpriteBindPoses = previousUsesSpriteBindPoses;
            data.hasSkinning = previousHasSkinning;
            data.lastPoseVersion = previousLastPoseVersion;
        }

        RegisterUndo("FullBoobs Geometry And Weights");
        if (!TryResolveFullBoobOuterBoundaryLoop(data, out var outerBoundaryLoop))
        {
            RestorePreviousState();
            error = "Body geometry has no recoverable outer boundary";
            return false;
        }

        var outerBoundaryMask = new bool[data.vertices.Length];
        for (var i = 0; i < outerBoundaryLoop.Count; i++)
        {
            var vertexIndex = outerBoundaryLoop[i];
            if (vertexIndex >= 0 && vertexIndex < outerBoundaryMask.Length)
                outerBoundaryMask[vertexIndex] = true;
        }
        var removeMask = new bool[data.vertices.Length];
        var leftLocalCullDistance = CalculateFullBoobLocalCullDistance(leftContour);
        var rightLocalCullDistance = CalculateFullBoobLocalCullDistance(rightContour);
        var removedVertices = 0;
        var removedInsideBreasts = 0;
        var removedNearOutsideBreasts = 0;
        for (var i = 0; i < data.vertices.Length; i++)
        {
            if (i < outerBoundaryMask.Length && outerBoundaryMask[i])
                continue;

            if (!ShouldCullFullBoobVertex(
                    data.vertices[i],
                    leftContour,
                    rightContour,
                    leftLocalCullDistance,
                    rightLocalCullDistance,
                    out var insideBreasts))
                continue;

            // FullBoobs is a local replacement. Remove the old vertices inside
            // either breast plus one narrow conflicting band immediately outside
            // the manual contours. Every earlier tool vertex beyond that band,
            // together with its UV and weights, must survive unchanged.
            removeMask[i] = true;
            removedVertices++;
            if (insideBreasts)
                removedInsideBreasts++;
            else
                removedNearOutsideBreasts++;
        }
        // Compact once without intermediate triangulation. The final constrained
        // tessellation receives every retained vertex, so repeated FullBoobs and
        // other tools remain composable instead of erasing one another.
        if (!CompactFullBoobGeometryKeepingOutside(
                data,
                removeMask,
                outerBoundaryLoop,
                leftContour,
                rightContour,
                leftLocalCullDistance,
                rightLocalCullDistance,
                out var retainedVertexCount,
                out var retainedTopologyConstraints,
                out var retainedTopologyTriangles))
        {
            RestorePreviousState();
            error = "FullBoobs local cull left no valid body geometry";
            return false;
        }

        if (!TryEnsureFullBoobSpriteBone(data, spineRendererBone, out var spineSpriteBone) ||
            !TryEnsureFullBoobSpriteBone(data, viewerLeftBoobRenderer, out var viewerLeftBoobSprite) ||
            !TryEnsureFullBoobSpriteBone(data, viewerRightBoobRenderer, out var viewerRightBoobSprite))
        {
            RestorePreviousState();
            error = "Failed to bind boob bones to body sprite";
            return false;
        }

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        var vertices = new List<Vector2>(data.vertices);
        var uvs = new List<Vector2>(data.vertexUvs != null && data.vertexUvs.Length == data.vertices.Length
            ? data.vertexUvs
            : BuildVertexUvsFromLayer(layer, data.vertices));
        var weights = new List<BoneWeight>(data.boneWeights);
        var smoothRingVertexIndices = new List<int>();
        var fullBoobConstraints = new List<Vector2Int>(retainedTopologyConstraints);
        var fullBoobLocalTriangles = new List<int>();
        AppendFullBoobGeometry(
            layer,
            leftCenter,
            leftContour,
            spineSpriteBone,
            viewerLeftBoobSprite,
            vertices,
            uvs,
            weights,
            smoothRingVertexIndices,
            fullBoobConstraints,
            fullBoobLocalTriangles);
        AppendFullBoobGeometry(
            layer,
            rightCenter,
            rightContour,
            spineSpriteBone,
            viewerRightBoobSprite,
            vertices,
            uvs,
            weights,
            smoothRingVertexIndices,
            fullBoobConstraints,
            fullBoobLocalTriangles);
        data.vertices = vertices.ToArray();
        data.vertexUvs = uvs.ToArray();
        data.boneWeights = weights.ToArray();

        // Tessellate the body around two constrained breast holes, then fill
        // those holes with deterministic local ring triangles. Treating every
        // breast point as an unconstrained global Delaunay point creates long
        // star-fan edges from the nipple rings to the remote body boundary.
        var usedTopologyOnlyRecovery = false;
        if (!TryRetriangulateFullBoobsWithConstraints(
                data,
                fullBoobConstraints,
                retainedVertexCount,
                retainedTopologyConstraints.Count))
        {
            // The generated local rings are themselves deterministic valid
            // topology. If the editor tessellator is unavailable or rejects even
            // the repaired boundary-only graph, retain every unaffected old
            // triangle and finish with the local rebuild instead of exposing a
            // recoverable terminal failure.
            data.triangles = Array.Empty<int>();
            usedTopologyOnlyRecovery = true;
        }

        data.triangles = BuildFullBoobCombinedTriangles(
            data.vertices,
            data.triangles,
            leftContour,
            rightContour,
            retainedTopologyTriangles,
            fullBoobLocalTriangles);

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            RestorePreviousState();
            error = "FullBoobs geometry produced no valid triangles";
            return false;
        }
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        var smoothedVertices = SmoothFullBoobRingWeights(data, smoothRingVertexIndices, 0.5f);
        MarkSkinningWeightsChanged(data);
        var stableKey = BuildKey(layer.name, layer.layerId);
        MarkEditGeometryDataChanged(data, BuildSelectionKey(layer), stableKey);
        var addedVertices = leftContour.Length * 2 + 1 + rightContour.Length * 2 + 1;
        result = "FullBoobs locally rebuilt " + CleanNippleLayerLabel(layer.name) +
                 ": preserved " + retainedVertexCount + " existing vertices, culled " +
                 removedVertices + " local vertices (" + removedInsideBreasts +
                 " inside, " + removedNearOutsideBreasts + " nearest outside), added " +
                 addedVertices + ", smoothed " +
                 smoothedVertices + " ring vertices" +
                 (usedTopologyOnlyRecovery ? ", repaired stale tessellation topology" : string.Empty);
        return true;
    }

    private bool TryResolveFullBoobOuterBoundaryLoop(
        LayerVertexData data,
        out List<int> boundaryLoop)
    {
        boundaryLoop = new List<int>();
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        if (TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, data.vertices.Length, out boundaryLoop) &&
            boundaryLoop.Count >= 3)
        {
            return true;
        }

        if (data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, data.vertices.Length, out _, out var triangleBoundaryPairs);
            if (TryBuildBoundaryLoopFromPairs(triangleBoundaryPairs, data.vertices.Length, out boundaryLoop) &&
                boundaryLoop.Count >= 3)
            {
                data.boundaryEdgePairs = triangleBoundaryPairs;
                return true;
            }
        }

        // Last deterministic repair for stale/imported metadata: the hull keeps
        // the sprite exterior and discards every generated internal FullBoobs ring.
        boundaryLoop = ReanimatorProjectorAlgorithms.BuildStretchBoundaryLoop(data.vertices);
        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;
        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(boundaryLoop);
        return data.boundaryEdgePairs != null && data.boundaryEdgePairs.Length >= 6;
    }

    private bool CompactFullBoobGeometryKeepingOutside(
        LayerVertexData data,
        IReadOnlyList<bool> removeMask,
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<Vector2> leftContour,
        IReadOnlyList<Vector2> rightContour,
        float leftCullDistance,
        float rightCullDistance,
        out int retainedVertexCount,
        out List<Vector2Int> retainedTopologyConstraints,
        out List<int> retainedTopologyTriangles)
    {
        retainedVertexCount = 0;
        retainedTopologyConstraints = new List<Vector2Int>();
        retainedTopologyTriangles = new List<int>();
        if (data == null || data.vertices == null || removeMask == null ||
            removeMask.Count < data.vertices.Length || boundaryLoop == null || boundaryLoop.Count < 3)
        {
            return false;
        }

        var sourceVertices = data.vertices;
        var sourceUvs = data.vertexUvs;
        var sourceWeights = data.boneWeights;
        var sourceAllEdges = data.allEdgePairs;
        if ((sourceAllEdges == null || sourceAllEdges.Length < 2) &&
            data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, sourceVertices.Length, out sourceAllEdges, out _);
        }
        var hasSourceUvs = sourceUvs != null && sourceUvs.Length == sourceVertices.Length;
        var hasSourceWeights = sourceWeights != null && sourceWeights.Length == sourceVertices.Length;
        var remap = new int[sourceVertices.Length];
        for (var i = 0; i < remap.Length; i++)
            remap[i] = -1;
        var compactVertices = new List<Vector2>(sourceVertices.Length);
        var compactUvs = new List<Vector2>(sourceVertices.Length);
        var compactWeights = hasSourceWeights
            ? new List<BoneWeight>(sourceVertices.Length)
            : null;

        for (var i = 0; i < sourceVertices.Length; i++)
        {
            if (removeMask[i])
                continue;
            remap[i] = compactVertices.Count;
            compactVertices.Add(sourceVertices[i]);
            compactUvs.Add(hasSourceUvs
                ? sourceUvs[i]
                : ComputeUvForPoint(data.layer, sourceVertices[i]));
            if (compactWeights != null)
                compactWeights.Add(sourceWeights[i]);
        }
        if (compactVertices.Count < 3)
            return false;

        var compactLoop = new List<int>(boundaryLoop.Count);
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var sourceIndex = boundaryLoop[i];
            if (sourceIndex < 0 || sourceIndex >= remap.Length || remap[sourceIndex] < 0)
                continue;
            compactLoop.Add(remap[sourceIndex]);
        }
        if (compactLoop.Count < 3)
            return false;

        if (sourceAllEdges != null)
        {
            var retainedKeys = new HashSet<ulong>();
            for (var i = 0; i + 1 < sourceAllEdges.Length; i += 2)
            {
                var sourceA = sourceAllEdges[i];
                var sourceB = sourceAllEdges[i + 1];
                if (sourceA < 0 || sourceB < 0 ||
                    sourceA >= remap.Length || sourceB >= remap.Length ||
                    remap[sourceA] < 0 || remap[sourceB] < 0 ||
                    !IsFullBoobEdgeOutsideLocalArea(
                        sourceVertices[sourceA],
                        sourceVertices[sourceB],
                        leftContour,
                        rightContour,
                        leftCullDistance,
                        rightCullDistance))
                {
                    continue;
                }

                var compactA = remap[sourceA];
                var compactB = remap[sourceB];
                if (compactA == compactB || !retainedKeys.Add(MakeEdgeKey(compactA, compactB)))
                    continue;
                retainedTopologyConstraints.Add(new Vector2Int(compactA, compactB));
            }
        }
        if (data.triangles != null)
        {
            for (var i = 0; i + 2 < data.triangles.Length; i += 3)
            {
                var sourceA = data.triangles[i];
                var sourceB = data.triangles[i + 1];
                var sourceC = data.triangles[i + 2];
                if (sourceA < 0 || sourceB < 0 || sourceC < 0 ||
                    sourceA >= remap.Length || sourceB >= remap.Length || sourceC >= remap.Length ||
                    remap[sourceA] < 0 || remap[sourceB] < 0 || remap[sourceC] < 0 ||
                    !IsFullBoobEdgeOutsideLocalArea(sourceVertices[sourceA], sourceVertices[sourceB], leftContour, rightContour, leftCullDistance, rightCullDistance) ||
                    !IsFullBoobEdgeOutsideLocalArea(sourceVertices[sourceB], sourceVertices[sourceC], leftContour, rightContour, leftCullDistance, rightCullDistance) ||
                    !IsFullBoobEdgeOutsideLocalArea(sourceVertices[sourceC], sourceVertices[sourceA], leftContour, rightContour, leftCullDistance, rightCullDistance))
                {
                    continue;
                }
                retainedTopologyTriangles.Add(remap[sourceA]);
                retainedTopologyTriangles.Add(remap[sourceB]);
                retainedTopologyTriangles.Add(remap[sourceC]);
            }
        }

        data.vertices = compactVertices.ToArray();
        data.vertexUvs = compactUvs.ToArray();
        data.boneWeights = compactWeights != null ? compactWeights.ToArray() : Array.Empty<BoneWeight>();
        data.triangles = Array.Empty<int>();
        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(compactLoop);
        data.allEdgePairs = CloneArray(data.boundaryEdgePairs);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        retainedVertexCount = data.vertices.Length;
        return true;
    }

    private static float CalculateFullBoobLocalCullDistance(IReadOnlyList<Vector2> contour)
    {
        if (contour == null || contour.Count < 3)
            return 1f;
        var min = contour[0];
        var max = contour[0];
        for (var i = 1; i < contour.Count; i++)
        {
            min = Vector2.Min(min, contour[i]);
            max = Vector2.Max(max, contour[i]);
        }
        var localSize = Mathf.Max(1f, Mathf.Min(max.x - min.x, max.y - min.y));
        return Mathf.Clamp(localSize * 0.035f, 1f, 10f);
    }

    private static float DistanceToFullBoobContour(
        Vector2 point,
        IReadOnlyList<Vector2> contour)
    {
        if (contour == null || contour.Count < 2)
            return float.PositiveInfinity;
        var distance = float.PositiveInfinity;
        for (var i = 0; i < contour.Count; i++)
            distance = Mathf.Min(distance, DistancePointToSegment(point, contour[i], contour[(i + 1) % contour.Count]));
        return distance;
    }

    private static bool ShouldCullFullBoobVertex(
        Vector2 point,
        IReadOnlyList<Vector2> leftContour,
        IReadOnlyList<Vector2> rightContour,
        float leftCullDistance,
        float rightCullDistance,
        out bool insideBreasts)
    {
        insideBreasts = IsPointInsideFullBoobContour(point, leftContour) ||
                        IsPointInsideFullBoobContour(point, rightContour);
        if (insideBreasts)
            return true;
        return DistanceToFullBoobContour(point, leftContour) <= Mathf.Max(0f, leftCullDistance) ||
               DistanceToFullBoobContour(point, rightContour) <= Mathf.Max(0f, rightCullDistance);
    }

    private static bool IsFullBoobEdgeOutsideLocalArea(
        Vector2 a,
        Vector2 b,
        IReadOnlyList<Vector2> leftContour,
        IReadOnlyList<Vector2> rightContour,
        float leftCullDistance,
        float rightCullDistance)
    {
        return DistanceFromSegmentToFullBoobContour(a, b, leftContour) > Mathf.Max(0f, leftCullDistance) &&
               DistanceFromSegmentToFullBoobContour(a, b, rightContour) > Mathf.Max(0f, rightCullDistance);
    }

    private static float DistanceFromSegmentToFullBoobContour(
        Vector2 a,
        Vector2 b,
        IReadOnlyList<Vector2> contour)
    {
        if (contour == null || contour.Count < 2)
            return float.PositiveInfinity;
        var distance = float.PositiveInfinity;
        for (var i = 0; i < contour.Count; i++)
        {
            var c = contour[i];
            var d = contour[(i + 1) % contour.Count];
            if (FullBoobSegmentsIntersect(a, b, c, d))
                return 0f;
            distance = Mathf.Min(
                distance,
                Mathf.Min(
                    Mathf.Min(DistancePointToSegment(a, c, d), DistancePointToSegment(b, c, d)),
                    Mathf.Min(DistancePointToSegment(c, a, b), DistancePointToSegment(d, a, b))));
        }
        return distance;
    }

    private static bool FullBoobSegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var ab = b - a;
        var cd = d - c;
        var denominator = FullBoobCross(ab, cd);
        if (Mathf.Abs(denominator) <= 0.000001f)
            return false;
        var offset = c - a;
        var t = FullBoobCross(offset, cd) / denominator;
        var u = FullBoobCross(offset, ab) / denominator;
        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }

    private bool TryFindFullBoobRendererBones(out int leftOrFirst, out int rightOrSecond)
    {
        leftOrFirst = -1;
        rightOrSecond = -1;
        if (bones == null)
            return false;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null)
                continue;
            var name = NormalizeAutoWeightName(bone.name);
            if (name == "lnipple" || name == "nipplel" || name == "leftnipple")
                leftOrFirst = i;
            else if (name == "rnipple" || name == "nippler" || name == "rightnipple")
                rightOrSecond = i;
        }
        return leftOrFirst >= 0 && rightOrSecond >= 0 && leftOrFirst != rightOrSecond;
    }

    private bool TryFindFullBoobSpineRendererBone(out int spineRendererBone)
    {
        spineRendererBone = -1;
        if (bones == null)
            return false;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null || NormalizeAutoWeightName(bone.name) != "spine")
                continue;
            spineRendererBone = i;
            return true;
        }
        return false;
    }

    private bool TryEnsureFullBoobSpriteBone(LayerVertexData data, int rendererBone, out int spriteBone)
    {
        spriteBone = -1;
        if (data == null || rendererBone < 0)
            return false;
        var map = data.spriteBoneToRendererBone ?? Array.Empty<int>();
        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] != rendererBone)
                continue;
            spriteBone = i;
            return true;
        }
        return TryAppendSkinningTargetBone(data, rendererBone, out spriteBone);
    }

    private void AppendFullBoobGeometry(
        LayerInfo layer,
        Vector2 nippleCenter,
        IReadOnlyList<Vector2> contour,
        int parentSpriteBone,
        int nippleSpriteBone,
        IList<Vector2> vertices,
        IList<Vector2> uvs,
        IList<BoneWeight> weights,
        ICollection<int> smoothRingVertexIndices,
        ICollection<Vector2Int> outerRingConstraints,
        ICollection<int> localTriangles)
    {
        var count = contour.Count;
        var outerStart = vertices.Count;
        for (var i = 0; i < count; i++)
        {
            vertices.Add(contour[i]);
            uvs.Add(ComputeUvForPoint(layer, contour[i]));
            weights.Add(BuildLimbTwoBoneWeight(parentSpriteBone, 1f, nippleSpriteBone, 0f));
            smoothRingVertexIndices.Add(outerStart + i);
        }

        var middleStart = vertices.Count;
        // Keep the middle ring close to the outer contour while making it
        // follow the nipple strongly enough to preserve breast volume.
        // The outer ring remains fully on spine and the center fully on nipple.
        const float middleContourLerp = 0.2f;
        const float middleNippleWeight = 0.75f;
        for (var i = 0; i < count; i++)
        {
            var middle = Vector2.Lerp(contour[i], nippleCenter, middleContourLerp);
            vertices.Add(middle);
            uvs.Add(ComputeUvForPoint(layer, middle));
            weights.Add(BuildLimbTwoBoneWeight(
                parentSpriteBone,
                1f - middleNippleWeight,
                nippleSpriteBone,
                middleNippleWeight));
            smoothRingVertexIndices.Add(middleStart + i);
        }

        var centerIndex = vertices.Count;
        vertices.Add(nippleCenter);
        uvs.Add(ComputeUvForPoint(layer, nippleCenter));
        weights.Add(BuildLimbTwoBoneWeight(parentSpriteBone, 0f, nippleSpriteBone, 1f));

        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            var outer = outerStart + i;
            var outerNext = outerStart + next;
            var middle = middleStart + i;
            var middleNext = middleStart + next;

            outerRingConstraints.Add(new Vector2Int(outer, outerNext));

            // Outer-to-middle annulus.
            localTriangles.Add(outer);
            localTriangles.Add(outerNext);
            localTriangles.Add(middleNext);
            localTriangles.Add(outer);
            localTriangles.Add(middleNext);
            localTriangles.Add(middle);

            // Middle ring-to-nipple fan; this fan is deliberately local and
            // cannot connect to remote body-boundary vertices.
            localTriangles.Add(middle);
            localTriangles.Add(middleNext);
            localTriangles.Add(centerIndex);
        }
    }

    private static int[] BuildFullBoobCombinedTriangles(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> bodyTriangles,
        IReadOnlyList<Vector2> leftContour,
        IReadOnlyList<Vector2> rightContour,
        IReadOnlyList<int> retainedTopologyTriangles,
        IReadOnlyList<int> localTriangles)
    {
        if (vertices == null)
            return Array.Empty<int>();

        var combined = new List<int>(
            (bodyTriangles != null ? bodyTriangles.Count : 0) +
            (retainedTopologyTriangles != null ? retainedTopologyTriangles.Count : 0) +
            (localTriangles != null ? localTriangles.Count : 0));
        var triangleKeys = new HashSet<ulong>();
        AppendUniqueFullBoobTriangles(combined, triangleKeys, retainedTopologyTriangles, vertices.Count);
        if (bodyTriangles != null)
        {
            for (var i = 0; i + 2 < bodyTriangles.Count; i += 3)
            {
                var a = bodyTriangles[i];
                var b = bodyTriangles[i + 1];
                var c = bodyTriangles[i + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= vertices.Count || b >= vertices.Count || c >= vertices.Count)
                {
                    continue;
                }

                if (DoesFullBoobTriangleOverlapContour(vertices[a], vertices[b], vertices[c], leftContour) ||
                    DoesFullBoobTriangleOverlapContour(vertices[a], vertices[b], vertices[c], rightContour))
                {
                    continue;
                }

                AppendUniqueFullBoobTriangle(combined, triangleKeys, a, b, c, vertices.Count);
            }
        }

        AppendUniqueFullBoobTriangles(combined, triangleKeys, localTriangles, vertices.Count);

        return combined.ToArray();
    }

    private static bool DoesFullBoobTriangleOverlapContour(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        IReadOnlyList<Vector2> contour)
    {
        if (contour == null || contour.Count < 3)
            return false;
        var centroid = (a + b + c) / 3f;
        if (IsPointInsideFullBoobContour(centroid, contour))
        {
            return true;
        }

        for (var i = 0; i < contour.Count; i++)
        {
            var contourPoint = contour[i];
            var contourNext = contour[(i + 1) % contour.Count];
            if (IsPointStrictlyInsideFullBoobTriangle(contourPoint, a, b, c) ||
                FullBoobSegmentsProperlyIntersect(a, b, contourPoint, contourNext) ||
                FullBoobSegmentsProperlyIntersect(b, c, contourPoint, contourNext) ||
                FullBoobSegmentsProperlyIntersect(c, a, contourPoint, contourNext))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPointStrictlyInsideFullBoobTriangle(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        const float epsilon = 0.000001f;
        var ab = FullBoobCross(b - a, point - a);
        var bc = FullBoobCross(c - b, point - b);
        var ca = FullBoobCross(a - c, point - c);
        return (ab > epsilon && bc > epsilon && ca > epsilon) ||
               (ab < -epsilon && bc < -epsilon && ca < -epsilon);
    }

    private static bool FullBoobSegmentsProperlyIntersect(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d)
    {
        var ab = b - a;
        var cd = d - c;
        var denominator = FullBoobCross(ab, cd);
        if (Mathf.Abs(denominator) <= 0.000001f)
            return false;
        var offset = c - a;
        var t = FullBoobCross(offset, cd) / denominator;
        var u = FullBoobCross(offset, ab) / denominator;
        const float endpointEpsilon = 0.00001f;
        return t > endpointEpsilon && t < 1f - endpointEpsilon &&
               u > endpointEpsilon && u < 1f - endpointEpsilon;
    }

    private static void AppendUniqueFullBoobTriangles(
        ICollection<int> target,
        ISet<ulong> triangleKeys,
        IReadOnlyList<int> source,
        int vertexCount)
    {
        if (source == null)
            return;
        for (var i = 0; i + 2 < source.Count; i += 3)
            AppendUniqueFullBoobTriangle(target, triangleKeys, source[i], source[i + 1], source[i + 2], vertexCount);
    }

    private static void AppendUniqueFullBoobTriangle(
        ICollection<int> target,
        ISet<ulong> triangleKeys,
        int a,
        int b,
        int c,
        int vertexCount)
    {
        if (a < 0 || b < 0 || c < 0 ||
            a >= vertexCount || b >= vertexCount || c >= vertexCount ||
            a == b || b == c || c == a)
        {
            return;
        }
        var key = MakeFullBoobTriangleKey(a, b, c);
        if (!triangleKeys.Add(key))
            return;
        target.Add(a);
        target.Add(b);
        target.Add(c);
    }

    private static ulong MakeFullBoobTriangleKey(int a, int b, int c)
    {
        if (a > b) { var swap = a; a = b; b = swap; }
        if (b > c) { var swap = b; b = c; c = swap; }
        if (a > b) { var swap = a; a = b; b = swap; }
        return ((ulong)(uint)a << 42) | ((ulong)(uint)b << 21) | (uint)c;
    }

    private static int SmoothFullBoobRingWeights(
        LayerVertexData data,
        IReadOnlyList<int> ringVertexIndices,
        float strength)
    {
        if (data == null || data.boneWeights == null ||
            data.boneWeights.Length != (data.vertices != null ? data.vertices.Length : 0) ||
            ringVertexIndices == null || ringVertexIndices.Count == 0)
        {
            return 0;
        }

        var spriteBoneCount = data.spriteBoneToRendererBone != null
            ? data.spriteBoneToRendererBone.Length
            : 0;
        if (spriteBoneCount <= 0)
            return 0;

        // Read every result from one immutable snapshot so smoothing does not
        // depend on traversal order around either closed ring.
        var sourceWeights = (BoneWeight[])data.boneWeights.Clone();
        var nextWeights = (BoneWeight[])sourceWeights.Clone();
        var changedCount = 0;
        for (var i = 0; i < ringVertexIndices.Count; i++)
        {
            var vertexIndex = ringVertexIndices[i];
            if (vertexIndex < 0 || vertexIndex >= sourceWeights.Length)
                continue;
            if (!ReanimatorWeightSliderAlgorithms.TryBuildSmoothedBoneWeight(
                    sourceWeights,
                    vertexIndex,
                    data.allEdgePairs,
                    data.triangles,
                    spriteBoneCount,
                    strength,
                    out var smoothed))
            {
                continue;
            }
            if (AreBoneWeightsApproximatelyEqual(sourceWeights[vertexIndex], smoothed))
                continue;
            nextWeights[vertexIndex] = smoothed;
            changedCount++;
        }

        if (changedCount > 0)
            data.boneWeights = nextWeights;
        return changedCount;
    }

    private bool TryRetriangulateFullBoobsWithConstraints(
        LayerVertexData data,
        IReadOnlyList<Vector2Int> internalConstraints,
        int includeExistingVertexCount = 0,
        int recoverableConstraintCount = 0)
    {
        if (TryRetriangulateFullBoobsConstraintSet(
                data,
                internalConstraints,
                includeExistingVertexCount,
                null))
        {
            return true;
        }

        // Retained edges are useful hints for preserving previous-tool topology,
        // but stale/repeated-run topology can cross a new manual breast ring.
        // Unity then inserts an unowned intersection vertex or rejects the whole
        // planar graph. Retry without only those conflicting hints: every old
        // vertex, UV and weight is still supplied, and retained outer triangles
        // are merged back verbatim after tessellation.
        var skipCount = Mathf.Clamp(
            recoverableConstraintCount,
            0,
            internalConstraints != null ? internalConstraints.Count : 0);
        if (skipCount <= 0 || internalConstraints == null || skipCount >= internalConstraints.Count)
            return false;

        var requiredRingConstraints = new List<Vector2Int>(internalConstraints.Count - skipCount);
        for (var i = skipCount; i < internalConstraints.Count; i++)
            requiredRingConstraints.Add(internalConstraints[i]);
        if (TryRetriangulateFullBoobsConstraintSet(
                data,
                requiredRingConstraints,
                includeExistingVertexCount,
                null))
        {
            return true;
        }

        // A contour may touch the sprite exterior on malformed/imported state.
        // The last tessellation tier keeps all ring vertices but makes only the
        // repaired sprite boundary mandatory. Overlapping body triangles are
        // removed geometrically before the deterministic local rings are merged.
        return TryRetriangulateFullBoobsConstraintSet(
            data,
            Array.Empty<Vector2Int>(),
            includeExistingVertexCount,
            requiredRingConstraints);
    }

    private bool TryRetriangulateFullBoobsConstraintSet(
        LayerVertexData data,
        IReadOnlyList<Vector2Int> internalConstraints,
        int includeExistingVertexCount,
        IReadOnlyList<Vector2Int> forceIncludedConstraints)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3 ||
            data.boundaryEdgePairs == null || data.boundaryEdgePairs.Length < 6)
        {
            return false;
        }

        var constraints = new List<Vector2Int>(data.boundaryEdgePairs.Length / 2 +
                                               (internalConstraints != null ? internalConstraints.Count : 0));
        var constraintKeys = new HashSet<ulong>();
        for (var i = 0; i + 1 < data.boundaryEdgePairs.Length; i += 2)
        {
            var a = data.boundaryEdgePairs[i];
            var b = data.boundaryEdgePairs[i + 1];
            if (a < 0 || b < 0 || a >= data.vertices.Length || b >= data.vertices.Length || a == b ||
                !constraintKeys.Add(MakeEdgeKey(a, b)))
                continue;
            constraints.Add(new Vector2Int(a, b));
        }
        if (internalConstraints != null)
        {
            for (var i = 0; i < internalConstraints.Count; i++)
            {
                var edge = internalConstraints[i];
                if (edge.x < 0 || edge.y < 0 ||
                    edge.x >= data.vertices.Length || edge.y >= data.vertices.Length || edge.x == edge.y ||
                    !constraintKeys.Add(MakeEdgeKey(edge.x, edge.y)))
                {
                    continue;
                }
                constraints.Add(edge);
            }
        }
        if (constraints.Count < 3)
            return false;

        // Only the body boundary and the two outer breast rings belong to the
        // constrained body tessellation. The middle rings and nipple centers
        // live inside those holes and are filled by our deterministic local
        // triangles afterwards. Passing the inner points to Unity's
        // tessellator makes it legally omit them from the hole interiors,
        // which used to make the vertex-permutation check fail and skip
        // FullBoobs altogether.
        var constrainedVertex = new bool[data.vertices.Length];
        for (var i = 0; i < constraints.Count; i++)
        {
            constrainedVertex[constraints[i].x] = true;
            constrainedVertex[constraints[i].y] = true;
        }
        if (forceIncludedConstraints != null)
        {
            for (var i = 0; i < forceIncludedConstraints.Count; i++)
            {
                var edge = forceIncludedConstraints[i];
                if (edge.x >= 0 && edge.x < constrainedVertex.Length)
                    constrainedVertex[edge.x] = true;
                if (edge.y >= 0 && edge.y < constrainedVertex.Length)
                    constrainedVertex[edge.y] = true;
            }
        }
        for (var i = 0; i < Mathf.Min(includeExistingVertexCount, constrainedVertex.Length); i++)
            constrainedVertex[i] = true;

        var subsetToFull = new List<int>();
        var fullToSubset = new int[data.vertices.Length];
        for (var i = 0; i < fullToSubset.Length; i++)
            fullToSubset[i] = -1;
        for (var i = 0; i < constrainedVertex.Length; i++)
        {
            if (!constrainedVertex[i])
                continue;
            fullToSubset[i] = subsetToFull.Count;
            subsetToFull.Add(i);
        }
        if (subsetToFull.Count < 3)
            return false;

        var sourceVertices = new List<Vector2>(subsetToFull.Count);
        for (var i = 0; i < subsetToFull.Count; i++)
            sourceVertices.Add(data.vertices[subsetToFull[i]]);

        var subsetConstraints = new List<Vector2Int>(constraints.Count);
        for (var i = 0; i < constraints.Count; i++)
        {
            var a = fullToSubset[constraints[i].x];
            var b = fullToSubset[constraints[i].y];
            if (a < 0 || b < 0 || a == b)
                continue;
            subsetConstraints.Add(new Vector2Int(a, b));
        }

        if (!TryTriangulateWithUnityTessellation(
                sourceVertices,
                subsetConstraints,
                0,
                out var triangulatedVertices,
                out var triangulatedTriangles,
                out _))
        {
            return false;
        }

        int[] triToSource;
        if (triangulatedVertices.Length == sourceVertices.Count &&
            TryBuildVertexRemap(sourceVertices, triangulatedVertices, out var sourceToTri) &&
            TryBuildInversePermutation(sourceToTri, triangulatedVertices.Length, out triToSource))
        {
            // Exact permutation is the preferred path.
        }
        else if (!TryMapTriangulatedSubsetToSource(sourceVertices, triangulatedVertices, out triToSource))
        {
            return false;
        }

        var remappedTriangles = new int[triangulatedTriangles.Length];
        for (var i = 0; i < triangulatedTriangles.Length; i++)
        {
            var triangulatedIndex = triangulatedTriangles[i];
            if (triangulatedIndex < 0 || triangulatedIndex >= triToSource.Length)
                return false;
            var subsetIndex = triToSource[triangulatedIndex];
            if (subsetIndex < 0 || subsetIndex >= subsetToFull.Count)
                return false;
            remappedTriangles[i] = subsetToFull[subsetIndex];
        }
        data.triangles = SanitizeTriangles(remappedTriangles, data.vertices.Length);
        return data.triangles != null && data.triangles.Length >= 3;
    }

    private static bool TryRepairFullBoobContourPair(
        Vector2 leftCenter,
        Vector2 rightCenter,
        IReadOnlyList<Vector2> sourceLeft,
        IReadOnlyList<Vector2> sourceRight,
        out Vector2[] repairedLeft,
        out Vector2[] repairedRight)
    {
        repairedLeft = Array.Empty<Vector2>();
        repairedRight = Array.Empty<Vector2>();
        if (!TrySanitizeFullBoobContour(sourceLeft, out repairedLeft) ||
            !TrySanitizeFullBoobContour(sourceRight, out repairedRight))
        {
            return false;
        }

        var pairAxis = rightCenter - leftCenter;
        if (pairAxis.sqrMagnitude <= 0.000001f)
            return false;
        pairAxis.Normalize();
        var midpoint = (leftCenter + rightCenter) * 0.5f;
        var localSize = Mathf.Min(
            CalculateFullBoobContourSize(repairedLeft),
            CalculateFullBoobContourSize(repairedRight));
        var halfGap = Mathf.Max(0.0001f, localSize * 0.0025f);

        // Manual contours commonly meet at the cleavage. Separate them by a
        // scale-relative sub-pixel gap so the constrained graph has no duplicate
        // coordinates, touching loops or overlap, without changing either remote
        // side of the user's contour.
        for (var i = 0; i < repairedLeft.Length; i++)
        {
            var signedDistance = Vector2.Dot(repairedLeft[i] - midpoint, pairAxis);
            if (signedDistance > -halfGap)
                repairedLeft[i] -= pairAxis * (signedDistance + halfGap);
        }
        for (var i = 0; i < repairedRight.Length; i++)
        {
            var signedDistance = Vector2.Dot(repairedRight[i] - midpoint, pairAxis);
            if (signedDistance < halfGap)
                repairedRight[i] += pairAxis * (halfGap - signedDistance);
        }

        return TrySanitizeFullBoobContour(repairedLeft, out repairedLeft) &&
               TrySanitizeFullBoobContour(repairedRight, out repairedRight);
    }

    private static bool TrySanitizeFullBoobContour(
        IReadOnlyList<Vector2> source,
        out Vector2[] sanitized)
    {
        sanitized = Array.Empty<Vector2>();
        if (source == null || source.Count < 3)
            return false;

        var finite = new List<Vector2>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var point = source[i];
            if (float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                float.IsNaN(point.y) || float.IsInfinity(point.y))
            {
                continue;
            }
            finite.Add(point);
        }
        if (finite.Count < 3)
            return false;

        var tolerance = Mathf.Max(0.0001f, CalculateFullBoobContourSize(finite) * 0.00001f);
        var toleranceSqr = tolerance * tolerance;
        var unique = new List<Vector2>(finite.Count);
        for (var i = 0; i < finite.Count; i++)
        {
            if (unique.Count > 0 && (unique[unique.Count - 1] - finite[i]).sqrMagnitude <= toleranceSqr)
                continue;
            unique.Add(finite[i]);
        }
        if (unique.Count > 1 && (unique[0] - unique[unique.Count - 1]).sqrMagnitude <= toleranceSqr)
            unique.RemoveAt(unique.Count - 1);
        if (unique.Count < 3)
            return false;

        if (DoesFullBoobContourSelfIntersect(unique))
        {
            var hull = ReanimatorProjectorAlgorithms.BuildStretchBoundaryLoop(unique);
            if (hull == null || hull.Count < 3)
                return false;
            var repaired = new List<Vector2>(hull.Count);
            for (var i = 0; i < hull.Count; i++)
                repaired.Add(unique[hull[i]]);
            unique = repaired;
        }

        sanitized = unique.ToArray();
        return sanitized.Length >= 3;
    }

    private static bool DoesFullBoobContourSelfIntersect(IReadOnlyList<Vector2> contour)
    {
        if (contour == null || contour.Count < 4)
            return false;
        for (var i = 0; i < contour.Count; i++)
        {
            var nextI = (i + 1) % contour.Count;
            for (var j = i + 1; j < contour.Count; j++)
            {
                var nextJ = (j + 1) % contour.Count;
                if (i == j || nextI == j || nextJ == i)
                    continue;
                if (FullBoobSegmentsIntersect(contour[i], contour[nextI], contour[j], contour[nextJ]))
                    return true;
            }
        }
        return false;
    }

    private static float CalculateFullBoobContourSize(IReadOnlyList<Vector2> contour)
    {
        if (contour == null || contour.Count == 0)
            return 1f;
        var min = contour[0];
        var max = contour[0];
        for (var i = 1; i < contour.Count; i++)
        {
            min = Vector2.Min(min, contour[i]);
            max = Vector2.Max(max, contour[i]);
        }
        return Mathf.Max(0.0001f, Mathf.Min(max.x - min.x, max.y - min.y));
    }

    private static bool TryMapTriangulatedSubsetToSource(
        IReadOnlyList<Vector2> sourceVertices,
        IReadOnlyList<Vector2> triangulatedVertices,
        out int[] triangulatedToSource)
    {
        triangulatedToSource = Array.Empty<int>();
        if (sourceVertices == null || triangulatedVertices == null || triangulatedVertices.Count < 3)
            return false;

        const float matchToleranceSqr = 0.0001f;
        triangulatedToSource = new int[triangulatedVertices.Count];
        for (var triangulatedIndex = 0; triangulatedIndex < triangulatedVertices.Count; triangulatedIndex++)
        {
            var bestSource = -1;
            var bestDistance = float.MaxValue;
            for (var sourceIndex = 0; sourceIndex < sourceVertices.Count; sourceIndex++)
            {
                var distance = (sourceVertices[sourceIndex] - triangulatedVertices[triangulatedIndex]).sqrMagnitude;
                if (distance > matchToleranceSqr || distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestSource = sourceIndex;
            }

            if (bestSource < 0)
            {
                triangulatedToSource = Array.Empty<int>();
                return false;
            }
            triangulatedToSource[triangulatedIndex] = bestSource;
        }
        return true;
    }

    private Vector2[] MapFullBoobContourToBase(
        LayerInfo layer,
        IReadOnlyList<Vector2> contourDocumentPoints)
    {
        var result = new Vector2[contourDocumentPoints.Count];
        for (var i = 0; i < contourDocumentPoints.Count; i++)
            result[i] = MapNippleGuessToBase(layer, contourDocumentPoints[i]);
        return result;
    }

    private static bool TryGetFullBoobNormalizedRadius(
        Vector2 point,
        Vector2 center,
        IReadOnlyList<Vector2> contour,
        out float normalizedRadius)
    {
        normalizedRadius = 1f;
        if (!IsPointInsideFullBoobContour(point, contour))
            return false;

        var delta = point - center;
        var distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            normalizedRadius = 0f;
            return true;
        }

        var direction = delta / distance;
        var boundaryDistance = float.PositiveInfinity;
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            var segment = b - a;
            var denominator = FullBoobCross(direction, segment);
            if (Mathf.Abs(denominator) <= 0.000001f)
                continue;
            var fromCenter = a - center;
            var rayT = FullBoobCross(fromCenter, segment) / denominator;
            var segmentT = FullBoobCross(fromCenter, direction) / denominator;
            if (rayT < distance - 0.001f || segmentT < -0.0001f || segmentT > 1.0001f)
                continue;
            boundaryDistance = Mathf.Min(boundaryDistance, rayT);
        }

        if (float.IsInfinity(boundaryDistance) || boundaryDistance <= 0.0001f)
            return false;
        normalizedRadius = Mathf.Clamp01(distance / boundaryDistance);
        return true;
    }

    private static bool IsPointInsideFullBoobContour(Vector2 point, IReadOnlyList<Vector2> contour)
    {
        var inside = false;
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            if ((a.y > point.y) == (b.y > point.y))
                continue;
            var crossingX = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (point.x < crossingX)
                inside = !inside;
        }
        return inside;
    }

    private static float FullBoobCross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

}
#endif
