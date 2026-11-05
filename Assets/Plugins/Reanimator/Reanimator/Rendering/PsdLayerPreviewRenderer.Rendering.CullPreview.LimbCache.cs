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
    private bool TryGetCachedLimbAfterCullPreviewGeometry(
        LayerVertexData sourceData,
        string selectionKey,
        string stableKey,
        LimbLine line,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices,
        out LimbPreviewCache cache)
    {
        removeContourVertices = true;
        cache = null;
        if (sourceData == null || sourceData.vertices == null || sourceData.vertices.Length < 3)
            return false;
        if (line == null || !line.hasContourPoints)
            return false;
        if (limbPreviewCache == null)
            limbPreviewCache = new LimbPreviewCache();

        cache = limbPreviewCache;
        var now = EditorApplication.timeSinceStartup;
        var rebuildInterval = (limbIsDragging || limbIsCreating || radiusCullIsDragging || radiusCullHandleIsDragging) ? 0.03d : 0.09d;
        var needRebuild = !IsLimbPreviewCacheMatch(
            cache,
            sourceData,
            selectionKey,
            stableKey,
            line,
            radius,
            removeContourVertices,
            removeInnerVertices);

        if (needRebuild && (now >= cache.nextRebuildTime || !cache.hasGeometryResult))
        {
            cache.nextRebuildTime = now + rebuildInterval;
            cache.selectionKey = selectionKey;
            cache.stableKey = stableKey;
            cache.lineStartDoc = line.startDoc;
            cache.lineEndDoc = line.endDoc;
            cache.contourPointA = line.contourPointA;
            cache.contourPointB = line.contourPointB;
            cache.contourSteps = LimbContourSteps;
            cache.contourStepDistance = LimbContourStepDistance;
            cache.contourShift = LimbTowardCenterShift;
            cache.curveHash = ComputeCurveHash(LimbCurve);
            cache.centerDoc = radiusCullCenterDoc;
            cache.radius = radius;
            cache.removeContourVertices = removeContourVertices;
            cache.removeInnerVertices = removeInnerVertices;
            cache.revision = radiusCullPreviewRevision;
            cache.sourceVerticesRef = sourceData.vertices;
            cache.sourceTrianglesRef = sourceData.triangles;
            cache.sourceVertexCount = sourceData.vertices != null ? sourceData.vertices.Length : 0;
            cache.sourceTriangleCount = sourceData.triangles != null ? sourceData.triangles.Length : 0;
            cache.sourcePoseVersion = int.MinValue;
            cache.sourceDrawVerticesRef = null;
            cache.previewVerticesDraw = Array.Empty<Vector2>();

            var generatedPoints = new List<LimbGeneratedPoint>(64);
            var contourPoints = new List<Vector2>(32);
            if (!TryBuildLimbGeneratedPoints(
                    line,
                    sourceData,
                    LimbContourSteps,
                    LimbContourStepDistance,
                    LimbTowardCenterShift,
                    generatedPoints,
                    contourPoints) ||
                generatedPoints.Count == 0)
            {
                cache.hasGeometryResult = false;
            }
            else if (!TryBuildRadiusCullPreviewGeometry(
                         radius,
                         removeContourVertices,
                         removeInnerVertices,
                         out var culledVertices,
                         out var culledTriangles,
                         out _,
                         out var culledBoundaryPairs,
                         out var culledBoneWeights,
                         out _))
            {
                cache.hasGeometryResult = false;
            }
            else
            {
                var previewData = new LayerVertexData
                {
                    layer = sourceData.layer,
                    texture = sourceData.texture,
                    pixelsPerUnit = sourceData.pixelsPerUnit,
                    pivotPixels = sourceData.pivotPixels,
                    vertices = culledVertices ?? Array.Empty<Vector2>(),
                    triangles = culledTriangles ?? Array.Empty<int>(),
                    boundaryEdgePairs = culledBoundaryPairs ?? Array.Empty<int>(),
                    allEdgePairs = Array.Empty<int>(),
                    vertexUvs = BuildVertexUvsFromLayer(sourceData.layer, culledVertices ?? Array.Empty<Vector2>()),
                    boneWeights = culledBoneWeights ?? Array.Empty<BoneWeight>(),
                    spriteBoneToRendererBone = sourceData.spriteBoneToRendererBone,
                    bindBoneInverseMatrices = sourceData.bindBoneInverseMatrices,
                    usesSpriteBindPoses = sourceData.usesSpriteBindPoses,
                    hasSkinning = false,
                    lastPoseVersion = -1
                };
                previewData.hasSkinning = previewData.spriteBoneToRendererBone != null &&
                                          previewData.bindBoneInverseMatrices != null &&
                                          previewData.boneWeights != null &&
                                          previewData.boneWeights.Length == previewData.vertices.Length &&
                                          previewData.bindBoneInverseMatrices.Length == previewData.spriteBoneToRendererBone.Length &&
                                          previewData.bindBoneInverseMatrices.Length > 0;

                var createdContourVertexIndices = new List<int>(generatedPoints.Count);
                var createdContourSourceIndices = new List<int>(generatedPoints.Count);
                var createdCenterVertexIndices = new List<int>(generatedPoints.Count);
                var createdShiftedVertexIndices = new List<int>(generatedPoints.Count);
                var createdShiftedSourceIndices = new List<int>(generatedPoints.Count);
                for (var i = 0; i < generatedPoints.Count; i++)
                {
                    var point = generatedPoints[i];
                    var basePoint = ConvertPointToBaseDoc(sourceData, point.pointDoc);
                    var oldVertexCount = previewData.vertices != null ? previewData.vertices.Length : 0;
                    if (!InsertAppliedVertexIntoLayerData(previewData, basePoint, true))
                        continue;

                    var createdVertexIndex = oldVertexCount;
                    if (point.type == LimbGeneratedPointType.Contour)
                    {
                        createdContourVertexIndices.Add(createdVertexIndex);
                        createdContourSourceIndices.Add(point.contourSourceIndex);
                    }
                    else if (point.type == LimbGeneratedPointType.Center)
                    {
                        createdCenterVertexIndices.Add(createdVertexIndex);
                    }
                    else if (point.type == LimbGeneratedPointType.Shifted)
                    {
                        createdShiftedVertexIndices.Add(createdVertexIndex);
                        createdShiftedSourceIndices.Add(point.contourSourceIndex);
                    }
                }

                if (!TryRebuildCulledLimbBoundary(sourceData, previewData, contourPoints, radius, removeInnerVertices))
                {
                    cache.hasGeometryResult = false;
                    return false;
                }

                if (!TryRetriangulateKeepingVertexSet(previewData) && !TryRetriangulateWithDirectFallback(previewData))
                {
                    cache.hasGeometryResult = false;
                }
                else
                {
                    previewData.triangles = SanitizeTriangles(previewData.triangles, previewData.vertices.Length);
                    if (previewData.triangles == null || previewData.triangles.Length < 3)
                    {
                        cache.hasGeometryResult = false;
                    }
                    else
                    {
                        // Keep explicit contour loop built during insertion; recompute only global edge pairs.
                        var preservedBoundaryPairs = previewData.boundaryEdgePairs;
                        BuildEdgePairs(
                            previewData.triangles,
                            previewData.vertices.Length,
                            out previewData.allEdgePairs,
                            out _);
                        if (preservedBoundaryPairs != null && preservedBoundaryPairs.Length >= 6)
                            previewData.boundaryEdgePairs = preservedBoundaryPairs;
                        EnsureBoneWeightsArraySize(previewData, previewData.vertices.Length);
                        ApplyLimbWeightsToGeometry(
                            previewData,
                            sourceData,
                            line,
                            contourPoints,
                            createdContourVertexIndices,
                            createdContourSourceIndices,
                            createdCenterVertexIndices,
                            createdShiftedVertexIndices,
                            createdShiftedSourceIndices);

                        cache.previewVerticesBase = previewData.vertices ?? Array.Empty<Vector2>();
                        cache.previewTriangles = previewData.triangles ?? Array.Empty<int>();
                        cache.previewBoneWeights = previewData.boneWeights ?? Array.Empty<BoneWeight>();
                        cache.boundaryEdgePairs = previewData.boundaryEdgePairs ?? Array.Empty<int>();

                        var contourEdgeKeys = BuildEdgeKeySet(cache.boundaryEdgePairs, cache.previewVerticesBase.Length);
                        var inner = new List<int>(previewData.allEdgePairs != null ? previewData.allEdgePairs.Length : 0);
                        if (previewData.allEdgePairs != null)
                        {
                            for (var i = 0; i + 1 < previewData.allEdgePairs.Length; i += 2)
                            {
                                var a = previewData.allEdgePairs[i];
                                var b = previewData.allEdgePairs[i + 1];
                                if (a < 0 || b < 0 || a == b)
                                    continue;
                                if (contourEdgeKeys != null && contourEdgeKeys.Contains(MakeEdgeKey(a, b)))
                                    continue;
                                inner.Add(a);
                                inner.Add(b);
                            }
                        }

                        if (inner.Count < 2 && previewData.allEdgePairs != null)
                            inner.AddRange(previewData.allEdgePairs);
                        cache.innerEdgePairs = inner.ToArray();

                        cache.totalTriangleCount = cache.previewTriangles.Length / 3;
                        cache.totalInnerPairCount = cache.innerEdgePairs.Length / 2;
                        cache.totalBoundaryPairCount = cache.boundaryEdgePairs.Length / 2;
                        cache.totalPointCount = cache.previewVerticesBase.Length;
                        cache.visibleTriangleCount = Mathf.Min(cache.totalTriangleCount, 96);
                        cache.visibleInnerPairCount = Mathf.Min(cache.totalInnerPairCount, 160);
                        cache.visibleBoundaryPairCount = Mathf.Min(cache.totalBoundaryPairCount, 120);
                        cache.visiblePointCount = Mathf.Min(cache.totalPointCount, 160);
                        cache.hasGeometryResult = cache.previewVerticesBase.Length >= 3 && cache.previewTriangles.Length >= 3;
                    }
                }
            }
        }

        if (!cache.hasGeometryResult || cache.previewVerticesBase == null || cache.previewVerticesBase.Length < 3)
            return false;

        var drawVertices = GetLayerVerticesForDraw(sourceData);
        if (NeedsLimbPreviewDrawRemap(cache, sourceData, drawVertices))
        {
            if (drawVertices != null &&
                sourceData.vertices != null &&
                drawVertices.Length == sourceData.vertices.Length &&
                !ReferenceEquals(drawVertices, sourceData.vertices) &&
                sourceData.triangles != null &&
                sourceData.triangles.Length >= 3)
            {
                cache.previewVerticesDraw = new Vector2[cache.previewVerticesBase.Length];
                for (var i = 0; i < cache.previewVerticesBase.Length; i++)
                {
                    if (PsdCoordinateSpace.TryMapPointBetweenMeshes(sourceData.vertices, drawVertices, sourceData.triangles, cache.previewVerticesBase[i], out var mapped))
                        cache.previewVerticesDraw[i] = mapped;
                    else
                        cache.previewVerticesDraw[i] = cache.previewVerticesBase[i];
                }
            }
            else
            {
                cache.previewVerticesDraw = cache.previewVerticesBase;
            }

            cache.sourcePoseVersion = bonePoseVersion;
            cache.sourceDrawVerticesRef = drawVertices;
        }

        return cache.previewVerticesDraw != null && cache.previewVerticesDraw.Length >= 3;
    }

    private bool IsLimbPreviewCacheMatch(
        LimbPreviewCache cache,
        LayerVertexData sourceData,
        string selectionKey,
        string stableKey,
        LimbLine line,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices)
    {
        if (cache == null || !cache.hasGeometryResult)
            return false;
        if (cache.revision != radiusCullPreviewRevision)
            return false;
        if (!string.Equals(cache.selectionKey, selectionKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(cache.stableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!ReferenceEquals(cache.sourceVerticesRef, sourceData.vertices))
            return false;
        if (!ReferenceEquals(cache.sourceTrianglesRef, sourceData.triangles))
            return false;
        if (cache.sourceVertexCount != (sourceData.vertices != null ? sourceData.vertices.Length : 0))
            return false;
        if (cache.sourceTriangleCount != (sourceData.triangles != null ? sourceData.triangles.Length : 0))
            return false;
        if (!IsApproximately(cache.lineStartDoc, line.startDoc, 0.0001f) ||
            !IsApproximately(cache.lineEndDoc, line.endDoc, 0.0001f))
            return false;
        if (!IsApproximately(cache.contourPointA, line.contourPointA, 0.0001f) ||
            !IsApproximately(cache.contourPointB, line.contourPointB, 0.0001f))
            return false;
        if (cache.contourSteps != LimbContourSteps)
            return false;
        if (Mathf.Abs(cache.contourStepDistance - LimbContourStepDistance) > 0.0001f)
            return false;
        if (Mathf.Abs(cache.contourShift - LimbTowardCenterShift) > 0.0001f)
            return false;
        if (cache.curveHash != ComputeCurveHash(LimbCurve))
            return false;
        if (!IsApproximately(cache.centerDoc, radiusCullCenterDoc, 0.0001f))
            return false;
        if (Mathf.Abs(cache.radius - radius) > 0.0001f)
            return false;
        if (cache.removeContourVertices != removeContourVertices || cache.removeInnerVertices != removeInnerVertices)
            return false;
        return true;
    }

    private bool NeedsLimbPreviewDrawRemap(LimbPreviewCache cache, LayerVertexData data, Vector2[] drawVertices)
    {
        if (cache == null)
            return true;
        if (cache.previewVerticesDraw == null || cache.previewVerticesDraw.Length != cache.previewVerticesBase.Length)
            return true;
        if (cache.sourcePoseVersion != bonePoseVersion)
            return true;
        if (drawVertices == null || data == null || data.vertices == null || drawVertices.Length != data.vertices.Length)
            return false;
        return !ReferenceEquals(cache.sourceDrawVerticesRef, drawVertices);
    }

}
#endif
