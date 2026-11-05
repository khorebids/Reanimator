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
    private bool TryGetCachedRadiusCullPreviewGeometry(
        LayerVertexData data,
        string selectionKey,
        string stableKey,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices,
        bool includeJointContourIntersections,
        float jointCPairOffset,
        int jointLongPoints,
        float jointStartLongPointsOffset,
        float jointEndLongPointsOffset,
        out Vector2[] previewDrawVertices,
        out int[] previewAllEdgePairs,
        out int[] previewBoundaryEdgePairs,
        out int[] previewTriangles,
        out BoneWeight[] previewBoneWeights,
        out int[] previewSpriteBoneToRendererBone)
    {
        previewDrawVertices = Array.Empty<Vector2>();
        previewAllEdgePairs = Array.Empty<int>();
        previewBoundaryEdgePairs = Array.Empty<int>();
        previewTriangles = Array.Empty<int>();
        previewBoneWeights = Array.Empty<BoneWeight>();
        previewSpriteBoneToRendererBone = Array.Empty<int>();

        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;
        if (radiusCullPreviewCache == null)
            radiusCullPreviewCache = new RadiusCullPreviewCache();

        var cache = radiusCullPreviewCache;
        var now = EditorApplication.timeSinceStartup;
        var rebuildInterval = (radiusCullIsDragging || radiusCullHandleIsDragging) ? 0.05d : 0.12d;
        var needRebuild = !IsRadiusCullPreviewCacheMatch(
            cache,
            data,
            selectionKey,
            stableKey,
            radius,
            removeContourVertices,
            removeInnerVertices,
            includeJointContourIntersections,
            jointCPairOffset,
            jointLongPoints,
            jointStartLongPointsOffset,
            jointEndLongPointsOffset);

        if (needRebuild && now >= cache.nextRebuildTime)
        {
            cache.nextRebuildTime = now + rebuildInterval;
            cache.selectionKey = selectionKey;
            cache.stableKey = stableKey;
            cache.centerDoc = radiusCullCenterDoc;
            cache.radius = radius;
            cache.removeContourVertices = removeContourVertices;
            cache.removeInnerVertices = removeInnerVertices;
            cache.includeJointContourIntersections = includeJointContourIntersections;
            cache.jointCPairOffset = jointCPairOffset;
            cache.jointLongPoints = jointLongPoints;
            cache.jointStartLongPointsOffset = jointStartLongPointsOffset;
            cache.jointEndLongPointsOffset = jointEndLongPointsOffset;
            cache.jointCenterLockEnabled = jointCenterLockEnabled;
            cache.jointLockedCenterDrawDoc = jointLockedCenterDrawDoc;
            cache.revision = radiusCullPreviewRevision;
            cache.sourceVerticesRef = data.vertices;
            cache.sourceTrianglesRef = data.triangles;
            cache.sourceVertexCount = data.vertices != null ? data.vertices.Length : 0;
            cache.sourceTriangleCount = data.triangles != null ? data.triangles.Length : 0;
            cache.sourcePoseVersion = int.MinValue;
            cache.sourceDrawVerticesRef = null;
            cache.previewVerticesDraw = Array.Empty<Vector2>();

            if (TryBuildRadiusCullPreviewGeometry(
                    radius,
                    removeContourVertices,
                    removeInnerVertices,
                    out var previewVertices,
                    out var builtPreviewTriangles,
                    out var previewAllPairs,
                    out var previewBoundaryPairs,
                    out var builtPreviewBoneWeights,
                    out var builtPreviewSpriteBoneToRendererBone,
                    includeJointContourIntersections,
                    jointCPairOffset,
                    jointLongPoints,
                    jointStartLongPointsOffset,
                    jointEndLongPointsOffset))
            {
                cache.hasGeometryResult = true;
                cache.previewVerticesBase = previewVertices ?? Array.Empty<Vector2>();
                cache.allEdgePairs = previewAllPairs ?? Array.Empty<int>();
                cache.boundaryEdgePairs = previewBoundaryPairs ?? Array.Empty<int>();
                cache.previewTriangles = builtPreviewTriangles ?? Array.Empty<int>();
                cache.previewBoneWeights = builtPreviewBoneWeights ?? Array.Empty<BoneWeight>();
                cache.previewSpriteBoneToRendererBone = builtPreviewSpriteBoneToRendererBone ?? Array.Empty<int>();
            }
            else
            {
                cache.hasGeometryResult = false;
                cache.previewVerticesBase = Array.Empty<Vector2>();
                cache.allEdgePairs = Array.Empty<int>();
                cache.boundaryEdgePairs = Array.Empty<int>();
                cache.previewTriangles = Array.Empty<int>();
                cache.previewBoneWeights = Array.Empty<BoneWeight>();
                cache.previewSpriteBoneToRendererBone = Array.Empty<int>();
            }
        }

        if (!cache.hasGeometryResult || cache.previewVerticesBase == null || cache.previewVerticesBase.Length < 3)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        var needDrawRemap = NeedsRadiusCullPreviewDrawRemap(cache, data, drawVertices);
        if (needDrawRemap)
        {
            if (drawVertices != null &&
                data.vertices != null &&
                drawVertices.Length == data.vertices.Length &&
                !ReferenceEquals(drawVertices, data.vertices) &&
                data.triangles != null &&
                data.triangles.Length >= 3)
            {
                cache.previewVerticesDraw = new Vector2[cache.previewVerticesBase.Length];
                for (var i = 0; i < cache.previewVerticesBase.Length; i++)
                {
                    if (PsdCoordinateSpace.TryMapPointBetweenMeshes(data.vertices, drawVertices, data.triangles, cache.previewVerticesBase[i], out var mapped))
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

        previewDrawVertices = cache.previewVerticesDraw ?? Array.Empty<Vector2>();
        previewAllEdgePairs = cache.allEdgePairs ?? Array.Empty<int>();
        previewBoundaryEdgePairs = cache.boundaryEdgePairs ?? Array.Empty<int>();
        previewTriangles = cache.previewTriangles ?? Array.Empty<int>();
        previewBoneWeights = cache.previewBoneWeights ?? Array.Empty<BoneWeight>();
        previewSpriteBoneToRendererBone = cache.previewSpriteBoneToRendererBone ?? Array.Empty<int>();
        return previewDrawVertices.Length >= 3;
    }
    private bool IsRadiusCullPreviewCacheMatch(
        RadiusCullPreviewCache cache,
        LayerVertexData data,
        string selectionKey,
        string stableKey,
        float radius,
        bool removeContourVertices,
        bool removeInnerVertices,
        bool includeJointContourIntersections,
        float jointCPairOffset,
        int jointLongPoints,
        float jointStartLongPointsOffset,
        float jointEndLongPointsOffset)
    {
        if (cache == null || !cache.hasGeometryResult)
            return false;
        if (cache.revision != radiusCullPreviewRevision)
            return false;
        if (!string.Equals(cache.selectionKey, selectionKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(cache.stableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!IsApproximately(cache.centerDoc, radiusCullCenterDoc, 0.0001f))
            return false;
        if (Mathf.Abs(cache.radius - radius) > 0.0001f)
            return false;
        if (cache.removeContourVertices != removeContourVertices || cache.removeInnerVertices != removeInnerVertices)
            return false;
        if (cache.includeJointContourIntersections != includeJointContourIntersections)
            return false;
        if (Mathf.Abs(cache.jointCPairOffset - jointCPairOffset) > 0.0001f)
            return false;
        if (cache.jointLongPoints != jointLongPoints)
            return false;
        if (Mathf.Abs(cache.jointStartLongPointsOffset - jointStartLongPointsOffset) > 0.0001f)
            return false;
        if (Mathf.Abs(cache.jointEndLongPointsOffset - jointEndLongPointsOffset) > 0.0001f)
            return false;
        if (cache.jointCenterLockEnabled != jointCenterLockEnabled)
            return false;
        if (cache.jointCenterLockEnabled &&
            !IsApproximately(cache.jointLockedCenterDrawDoc, jointLockedCenterDrawDoc, 0.0001f))
            return false;
        if (!ReferenceEquals(cache.sourceVerticesRef, data.vertices))
            return false;
        if (!ReferenceEquals(cache.sourceTrianglesRef, data.triangles))
            return false;
        if (cache.sourceVertexCount != (data.vertices != null ? data.vertices.Length : 0))
            return false;
        if (cache.sourceTriangleCount != (data.triangles != null ? data.triangles.Length : 0))
            return false;
        if (cache.previewTriangles == null || cache.previewTriangles.Length < 3)
            return false;
        var requiresPreviewWeights = data.boneWeights != null && data.boneWeights.Length == data.vertices.Length;
        if (requiresPreviewWeights &&
            (cache.previewBoneWeights == null || cache.previewBoneWeights.Length != cache.previewVerticesBase.Length))
            return false;
        return true;
    }

    private bool NeedsRadiusCullPreviewDrawRemap(RadiusCullPreviewCache cache, LayerVertexData data, Vector2[] drawVertices)
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

    private static bool IsApproximately(Vector2 a, Vector2 b, float epsilon)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return dx * dx + dy * dy <= epsilon * epsilon;
    }
}
#endif
