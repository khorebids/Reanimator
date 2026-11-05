#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private bool TryApplyJointExtendedGeometry(
        LayerVertexData data,
        Vector2 centerDoc,
        float radius,
        int parentSpriteBone,
        int nearestSpriteBone,
        float cPairOffset,
        int longPoints,
        float startLongPointsOffset,
        float endLongPointsOffset,
        out int affectedContourVertices,
        out int insertedVertices,
        bool logDebug = false)
    {
        affectedContourVertices = 0;
        insertedVertices = 0;
        if (logDebug)
        {
            LogJointApplyDebug($"Geometry start center={FormatJointVector(centerDoc)} radius={radius:0.###} parent={DescribeJointSpriteBone(parentSpriteBone, data != null ? data.spriteBoneToRendererBone : null)} nearest={DescribeJointSpriteBone(nearestSpriteBone, data != null ? data.spriteBoneToRendererBone : null)} cPairOffset={cPairOffset:0.###} longPoints={longPoints} startOffset={startLongPointsOffset:0.###} endOffset={endLongPointsOffset:0.###} target={DescribeJointLayer(data)}");
        }
        if (radius <= 0.0001f)
        {
            if (logDebug)
                LogJointApplyWarning("Geometry failed: radius <= 0.0001f");
            return false;
        }
        if (data == null || data.vertices == null || data.vertices.Length < 3)
        {
            if (logDebug)
                LogJointApplyWarning($"Geometry failed: invalid data or not enough vertices ({DescribeJointLayer(data)})");
            return false;
        }

        cPairOffset = Mathf.Max(0f, cPairOffset);
        longPoints = Mathf.Clamp(longPoints, 0, 10);
        startLongPointsOffset = Mathf.Max(0f, startLongPointsOffset);
        endLongPointsOffset = Mathf.Max(0f, endLongPointsOffset);
        var parentWeight = BuildSingleBoneWeight(parentSpriteBone);
        var radiusSqr = radius * radius;

        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 6)
        {
            if (logDebug)
                LogJointApplyWarning($"Geometry failed: boundaryPairs unavailable length={(boundaryPairs != null ? boundaryPairs.Length : 0)}");
            return false;
        }
        if (data.boundaryEdgePairs == null || data.boundaryEdgePairs.Length < 6)
            data.boundaryEdgePairs = boundaryPairs;

        var hasStartHit = TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, jointStartAngleRad, out var startHitDoc);
        var hasEndHit = TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, jointEndAngleRad, out var endHitDoc);
        if (!hasStartHit || !hasEndHit)
        {
            if (logDebug)
                LogJointApplyWarning($"Geometry failed: contour ray hits start={hasStartHit} end={hasEndHit} boundaryPairs={boundaryPairs.Length}");
            return false;
        }

        if (logDebug)
            LogJointApplyDebug($"Contour ray hits start={FormatJointVector(startHitDoc)} end={FormatJointVector(endHitDoc)} startInRadius={((startHitDoc - centerDoc).sqrMagnitude <= radiusSqr)} endInRadius={((endHitDoc - centerDoc).sqrMagnitude <= radiusSqr)}");

        var startGrayHitDoc = Vector2.zero;
        var endGrayHitDoc = Vector2.zero;
        var hasStartGrayHit = startLongPointsOffset > 0.0001f &&
                              TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, GetJointOffsetHandleAngleRad(radius, true), out startGrayHitDoc);
        var hasEndGrayHit = endLongPointsOffset > 0.0001f &&
                            TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, GetJointOffsetHandleAngleRad(radius, false), out endGrayHitDoc);

        if ((startHitDoc - centerDoc).sqrMagnitude <= radiusSqr &&
            TryEnsureJointContourIntersectionVertex(data, startHitDoc, parentWeight, out var startChanged) &&
            startChanged)
        {
            insertedVertices++;
        }
        if (hasStartGrayHit &&
            (startGrayHitDoc - centerDoc).sqrMagnitude <= radiusSqr &&
            TryEnsureJointContourIntersectionVertex(data, startGrayHitDoc, parentWeight, out var startGrayChanged) &&
            startGrayChanged)
        {
            insertedVertices++;
        }
        if (hasEndGrayHit &&
            (endGrayHitDoc - centerDoc).sqrMagnitude <= radiusSqr &&
            TryEnsureJointContourIntersectionVertex(data, endGrayHitDoc, parentWeight, out var endGrayChanged) &&
            endGrayChanged)
        {
            insertedVertices++;
        }
        if ((endHitDoc - centerDoc).sqrMagnitude <= radiusSqr &&
            TryEnsureJointContourIntersectionVertex(data, endHitDoc, parentWeight, out var endChanged) &&
            endChanged)
        {
            insertedVertices++;
        }

        boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 6)
        {
            if (logDebug)
                LogJointApplyWarning($"Geometry failed: boundaryPairs unavailable after contour insertion length={(boundaryPairs != null ? boundaryPairs.Length : 0)}");
            return false;
        }
        data.boundaryEdgePairs = boundaryPairs;

        if (!TryBuildJointBoundaryLoopPoints(data, out var boundaryLoop, out var loopPoints))
        {
            if (logDebug)
                LogJointApplyWarning("Geometry failed: TryBuildJointBoundaryLoopPoints returned false");
            return false;
        }
        if (!TryBuildContourPerimeterSamples(loopPoints, out var segmentLengths, out var perimeterLength))
        {
            if (logDebug)
                LogJointApplyWarning($"Geometry failed: TryBuildContourPerimeterSamples returned false loopPoints={loopPoints.Count}");
            return false;
        }
        if (!TryProjectPointToContourPerimeter(loopPoints, segmentLengths, perimeterLength, startHitDoc, out var startDistance))
        {
            if (logDebug)
                LogJointApplyWarning("Geometry failed: could not project startHit to contour perimeter");
            return false;
        }
        if (!TryProjectPointToContourPerimeter(loopPoints, segmentLengths, perimeterLength, endHitDoc, out var endDistance))
        {
            if (logDebug)
                LogJointApplyWarning("Geometry failed: could not project endHit to contour perimeter");
            return false;
        }

        var startGrayDistance = 0f;
        var endGrayDistance = 0f;
        if (hasStartGrayHit && !TryProjectPointToContourPerimeter(loopPoints, segmentLengths, perimeterLength, startGrayHitDoc, out startGrayDistance))
            hasStartGrayHit = false;
        if (hasEndGrayHit && !TryProjectPointToContourPerimeter(loopPoints, segmentLengths, perimeterLength, endGrayHitDoc, out endGrayDistance))
            hasEndGrayHit = false;

        if (!TryResolveJointBlueArcDirection(
                loopPoints,
                segmentLengths,
                perimeterLength,
                startDistance,
                endDistance,
                centerDoc,
                out var blueForward,
                out var blueArcLength))
        {
            if (logDebug)
                LogJointApplyWarning($"Geometry failed: TryResolveJointBlueArcDirection returned false startDistance={startDistance:0.###} endDistance={endDistance:0.###} perimeter={perimeterLength:0.###}");
            return false;
        }

        if (logDebug)
            LogJointApplyDebug($"Boundary loop resolved count={boundaryLoop.Count} perimeter={perimeterLength:0.###} startDistance={startDistance:0.###} endDistance={endDistance:0.###} blueForward={blueForward} blueArcLength={blueArcLength:0.###}");
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        var vertexDistances = new float[boundaryLoop.Count];
        var accumulated = 0f;
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            vertexDistances[i] = accumulated;
            accumulated += segmentLengths[i];
        }
        var boundaryLoopForRemoval = boundaryLoop.ToArray();
        var vertexDistancesForRemoval = (float[])vertexDistances.Clone();

        var blueContourPoints = new List<Vector2>(Mathf.Max(8, boundaryLoop.Count / 3));
        AddUniqueDocPoint(blueContourPoints, startHitDoc, 0.001f);
        AddUniqueDocPoint(blueContourPoints, endHitDoc, 0.001f);
        var protectedContourVertexIndices = new HashSet<int>();
        const float protectVertexDistanceSqr = 0.25f;
        var startIndex = FindNearestVertexIndex(startHitDoc, data.vertices);
        if (startIndex >= 0 &&
            (startHitDoc - centerDoc).sqrMagnitude <= radiusSqr &&
            (data.vertices[startIndex] - startHitDoc).sqrMagnitude <= protectVertexDistanceSqr)
        {
            protectedContourVertexIndices.Add(startIndex);
        }
        var endIndex = FindNearestVertexIndex(endHitDoc, data.vertices);
        if (endIndex >= 0 &&
            (endHitDoc - centerDoc).sqrMagnitude <= radiusSqr &&
            (data.vertices[endIndex] - endHitDoc).sqrMagnitude <= protectVertexDistanceSqr)
        {
            protectedContourVertexIndices.Add(endIndex);
        }

        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var vertexIndex = boundaryLoop[i];
            if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
                continue;
            if (!IsPerimeterDistanceOnArc(vertexDistances[i], startDistance, blueArcLength, perimeterLength, blueForward, 0.001f))
                continue;

            AddUniqueDocPoint(blueContourPoints, data.vertices[vertexIndex], 0.001f);
            if ((data.vertices[vertexIndex] - centerDoc).sqrMagnitude > radiusSqr)
                continue;

            data.boneWeights[vertexIndex] = parentWeight;
            affectedContourVertices++;
        }

        if (affectedContourVertices <= 0 && blueContourPoints.Count <= 0)
        {
            if (logDebug)
                LogJointApplyWarning("Geometry failed: no contour points were collected in joint sector");
            return false;
        }

        if (TryEnsureJointInteriorVertex(data, centerDoc, parentWeight, out var centerChanged) && centerChanged)
            insertedVertices++;

        if (cPairOffset > 0.0001f)
        {
            for (var i = 0; i < blueContourPoints.Count; i++)
            {
                var source = blueContourPoints[i];
                var toCenter = centerDoc - source;
                if (toCenter.sqrMagnitude <= 1e-8f)
                    continue;
                var shifted = source + toCenter.normalized * cPairOffset;
                if ((shifted - centerDoc).sqrMagnitude > radiusSqr)
                    continue;
                if (TryEnsureJointInteriorVertex(data, shifted, parentWeight, out var changed) && changed)
                    insertedVertices++;
            }
        }

        if (longPoints > 0 && (startLongPointsOffset > 0.0001f || endLongPointsOffset > 0.0001f))
        {
            var transparentBasePoints = new List<Vector2>(longPoints * 2);
            var startTransparentForward = false;
            var endTransparentForward = false;
            var startTransparentSpan = 0f;
            var endTransparentSpan = 0f;
            var startGrayAngleRad = GetJointOffsetHandleAngleRad(radius, true);
            var endGrayAngleRad = GetJointOffsetHandleAngleRad(radius, false);
            hasStartGrayHit = hasStartGrayHit &&
                              TryResolveJointTransparentAngleRange(
                                  jointStartAngleRad,
                                  startGrayAngleRad,
                                  out startTransparentForward,
                                  out startTransparentSpan);
            hasEndGrayHit = hasEndGrayHit &&
                            TryResolveJointTransparentAngleRange(
                                jointEndAngleRad,
                                endGrayAngleRad,
                                out endTransparentForward,
                                out endTransparentSpan);
            for (var step = 0; step < longPoints; step++)
            {
                var t = (step + 1f) / (longPoints + 1f);
                if (hasStartGrayHit &&
                    TryGetNearestContourRayIntersection(
                        data.vertices,
                        boundaryPairs,
                        centerDoc,
                        GetAngleAlongDirectedArc(jointStartAngleRad, startTransparentSpan, startTransparentForward, t),
                        out var startPoint) &&
                    (startPoint - centerDoc).sqrMagnitude <= radiusSqr)
                {
                    AddUniqueDocPoint(transparentBasePoints, startPoint, 0.001f);
                }
                if (hasEndGrayHit &&
                    TryGetNearestContourRayIntersection(
                        data.vertices,
                        boundaryPairs,
                        centerDoc,
                        GetAngleAlongDirectedArc(jointEndAngleRad, endTransparentSpan, endTransparentForward, t),
                        out var endPoint) &&
                    (endPoint - centerDoc).sqrMagnitude <= radiusSqr)
                {
                    AddUniqueDocPoint(transparentBasePoints, endPoint, 0.001f);
                }
            }

            for (var i = 0; i < transparentBasePoints.Count; i++)
            {
                var transparentPoint = transparentBasePoints[i];
                if ((transparentPoint - centerDoc).sqrMagnitude <= radiusSqr)
                {
                    if (TryEnsureJointContourIntersectionVertex(data, transparentPoint, parentWeight, out var changed) && changed)
                        insertedVertices++;

                    var vertexIndex = FindNearestVertexIndex(transparentPoint, data.vertices);
                    if (vertexIndex >= 0 &&
                        vertexIndex < data.vertices.Length &&
                        (data.vertices[vertexIndex] - transparentPoint).sqrMagnitude <= protectVertexDistanceSqr)
                    {
                        protectedContourVertexIndices.Add(vertexIndex);
                    }
                }
            }

            if ((startTransparentSpan > 0.0001f || endTransparentSpan > 0.0001f) &&
                boundaryLoopForRemoval.Length > 0 &&
                vertexDistancesForRemoval.Length == boundaryLoopForRemoval.Length)
            {
                var removeMask = new bool[data.vertices.Length];
                for (var i = 0; i < boundaryLoopForRemoval.Length; i++)
                {
                    var vertexIndex = boundaryLoopForRemoval[i];
                    if (vertexIndex < 0 || vertexIndex >= removeMask.Length)
                        continue;
                    if ((data.vertices[vertexIndex] - centerDoc).sqrMagnitude > radiusSqr)
                        continue;
                    if (protectedContourVertexIndices.Contains(vertexIndex))
                        continue;

                    var distance = vertexDistancesForRemoval[i];
                    var angle = Mathf.Atan2(data.vertices[vertexIndex].y - centerDoc.y, data.vertices[vertexIndex].x - centerDoc.x);
                    var onStartSideArc = hasStartGrayHit &&
                                         IsAngleOnDirectedArc(
                                             angle,
                                             jointStartAngleRad,
                                             startTransparentSpan,
                                             startTransparentForward,
                                             0.0001f);
                    var onEndSideArc = hasEndGrayHit &&
                                       IsAngleOnDirectedArc(
                                           angle,
                                           jointEndAngleRad,
                                           endTransparentSpan,
                                           endTransparentForward,
                                           0.0001f);
                    if (onStartSideArc || onEndSideArc)
                        removeMask[vertexIndex] = true;
                }

                if (!TryRemoveVerticesByMask(data, removeMask))
                {
                    if (logDebug)
                        LogJointApplyWarning("Geometry failed: TryRemoveVerticesByMask returned false");
                    return false;
                }
            }

            if (cPairOffset > 0.0001f && transparentBasePoints.Count > 0)
            {
                for (var i = 0; i < transparentBasePoints.Count; i++)
                {
                    var point = transparentBasePoints[i];
                    var toCenter = centerDoc - point;
                    if (toCenter.sqrMagnitude <= 1e-8f)
                        continue;

                    var shifted = point + toCenter.normalized * cPairOffset;
                    if ((shifted - centerDoc).sqrMagnitude > radiusSqr)
                        continue;
                    if (TryEnsureJointInteriorVertex(data, shifted, parentWeight, out var changed) && changed)
                        insertedVertices++;
                }
            }
        }

        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
            {
                if (logDebug)
                    LogJointApplyWarning("Geometry failed: retriangulation failed after edits");
                return false;
            }
            data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
            if (data.triangles == null || data.triangles.Length < 3)
            {
                if (logDebug)
                    LogJointApplyWarning("Geometry failed: triangles still invalid after retriangulation");
                return false;
            }
        }

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        ApplyJointInfluenceWeights(
            data,
            centerDoc,
            radius,
            startHitDoc,
            endHitDoc,
            parentSpriteBone,
            nearestSpriteBone,
            longPoints,
            startLongPointsOffset,
            endLongPointsOffset);
        if (logDebug)
            LogJointApplyDebug($"Geometry success affectedContourVertices={affectedContourVertices} insertedVertices={insertedVertices} blueContourPoints={blueContourPoints.Count} finalVertices={(data.vertices != null ? data.vertices.Length : 0)} finalTriangles={(data.triangles != null ? data.triangles.Length / 3 : 0)}");
        return affectedContourVertices > 0 || insertedVertices > 0;
    }

    private bool TryRemoveVerticesByMask(LayerVertexData data, bool[] removeMask)
    {
        if (data == null || data.vertices == null || removeMask == null)
            return false;

        var vertexCount = data.vertices.Length;
        if (vertexCount < 3 || removeMask.Length < vertexCount)
            return false;

        var removedCount = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                removedCount++;
        }

        if (removedCount <= 0)
            return true;

        var remainingVertexCount = vertexCount - removedCount;
        if (remainingVertexCount < 3)
            return false;

        var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
            if (fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6)
            {
                data.boundaryEdgePairs = fallbackBoundaryPairs;
                contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
            }
        }

        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out var boundaryLoop))
            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;

        var remap = new int[vertexCount];
        for (var i = 0; i < remap.Length; i++)
            remap[i] = -1;

        var keptVertices = new List<Vector2>(remainingVertexCount);
        var keptUvs = new List<Vector2>(remainingVertexCount);
        var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
            ? new List<BoneWeight>(remainingVertexCount)
            : null;

        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                continue;
            remap[i] = keptVertices.Count;
            keptVertices.Add(data.vertices[i]);
            if (data.vertexUvs != null && i < data.vertexUvs.Length)
                keptUvs.Add(data.vertexUvs[i]);
            else
                keptUvs.Add(ComputeUvForPoint(data.layer, data.vertices[i]));
            if (keptWeights != null)
                keptWeights.Add(data.boneWeights[i]);
        }

        var newBoundaryLoop = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
        if (newBoundaryLoop.Count < 3)
        {
            var remappedContourVertices = new List<int>(remainingVertexCount);
            for (var i = 0; i < contourMask.Length; i++)
            {
                if (!contourMask[i] || removeMask[i] || remap[i] < 0)
                    continue;
                remappedContourVertices.Add(remap[i]);
            }
            newBoundaryLoop = BuildFallbackBoundaryLoop(keptVertices, remappedContourVertices);
        }

        if (newBoundaryLoop.Count < 3)
            return false;

        data.vertices = keptVertices.ToArray();
        data.vertexUvs = keptUvs.ToArray();
        data.boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>();
        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop);
        data.triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, data.vertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!TryRetriangulateKeepingVertexSet(data))
        {
            if (!TryRetriangulateWithDirectFallback(data))
                return false;
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
            return false;

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        return true;
    }

}
#endif
