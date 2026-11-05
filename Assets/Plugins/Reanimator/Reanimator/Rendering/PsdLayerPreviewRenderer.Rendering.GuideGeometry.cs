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
    private void UpdateElbowLinesForLayer(string selectedLayerKey, string layerStableKey, LayerVertexData data)
    {
        if (string.IsNullOrEmpty(layerStableKey) || data == null)
            return;

        for (var i = 0; i < elbowLines.Count; i++)
        {
            var line = elbowLines[i];
            if (line == null)
                continue;
            if (!IsElbowLineForLayer(line, selectedLayerKey, layerStableKey))
                continue;

            if (!string.IsNullOrEmpty(selectedLayerKey))
                line.layerSelectionKey = selectedLayerKey;
            UpdateElbowContourPoints(line, data);
        }
    }

    private void UpdateElbowContourPoints(ElbowLine line, LayerVertexData data)
    {
        if (line == null || data == null)
            return;
        if (!TryGetElbowContourPoints(line.startDoc, line.endDoc, data, out var p0, out var p1))
        {
            line.hasContourPoints = false;
            return;
        }

        line.contourPointA = p0;
        line.contourPointB = p1;
        line.hasContourPoints = true;
    }

    private void RefreshElbowLineContour(ElbowLine line)
    {
        if (line == null)
            return;
        if (!TryGetLayerVertexDataForLine(line, out var data))
        {
            line.hasContourPoints = false;
            return;
        }

        UpdateElbowContourPoints(line, data);
    }

    private bool TryGetLayerVertexDataForLine(ElbowLine line, out LayerVertexData data)
    {
        data = null;
        if (line == null)
            return false;

        if (!string.IsNullOrEmpty(line.layerSelectionKey) &&
            vertexDataByLayerSelectionKey.TryGetValue(line.layerSelectionKey, out data) &&
            data != null)
            return true;

        if (!TryGetLayerVertexDataByStableKey(line.layerStableKey, out data, out var resolvedSelectionKey))
            return false;

        line.layerSelectionKey = resolvedSelectionKey;
        return true;
    }

    private void UpdateLimbLinesForLayer(string selectedLayerKey, string layerStableKey, LayerVertexData data)
    {
        if (string.IsNullOrEmpty(layerStableKey) || data == null)
            return;

        for (var i = 0; i < limbLines.Count; i++)
        {
            var line = limbLines[i];
            if (line == null)
                continue;
            if (!IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectedLayerKey, layerStableKey))
                continue;

            if (!string.IsNullOrEmpty(selectedLayerKey))
                line.layerSelectionKey = selectedLayerKey;
            UpdateLimbContourPoints(line, data);
        }
    }

    private void UpdateLimbContourPoints(LimbLine line, LayerVertexData data)
    {
        if (line == null || data == null)
            return;
        if (!TryGetElbowContourPoints(line.startDoc, line.endDoc, data, out var p0, out var p1))
        {
            line.hasContourPoints = false;
            return;
        }

        line.contourPointA = p0;
        line.contourPointB = p1;
        line.hasContourPoints = true;
    }

    private void RefreshLimbLineContour(LimbLine line)
    {
        if (line == null)
            return;
        if (!TryGetLayerVertexDataForLimbLine(line, out var data))
        {
            line.hasContourPoints = false;
            return;
        }

        UpdateLimbContourPoints(line, data);
    }

    private bool TryGetLayerVertexDataForLimbLine(LimbLine line, out LayerVertexData data)
    {
        data = null;
        if (line == null)
            return false;

        if (!string.IsNullOrEmpty(line.layerSelectionKey) &&
            vertexDataByLayerSelectionKey.TryGetValue(line.layerSelectionKey, out data) &&
            data != null)
            return true;

        if (!TryGetLayerVertexDataByStableKey(line.layerStableKey, out data, out var resolvedSelectionKey))
            return false;

        line.layerSelectionKey = resolvedSelectionKey;
        return true;
    }

    private static Vector2 GetLimbContourMidpoint(LimbLine line)
    {
        if (line == null)
            return Vector2.zero;

        return line.hasRequiredJointAnchor
            ? line.requiredJointAnchorDoc
            : (line.contourPointA + line.contourPointB) * 0.5f;
    }

    private static Vector2 GetLimbGuideDirection(LimbLine line)
    {
        if (line == null)
            return Vector2.right;

        var direction = line.endDoc - line.startDoc;
        if (direction.sqrMagnitude <= 1e-8f)
            direction = line.contourPointB - line.contourPointA;
        if (direction.sqrMagnitude <= 1e-8f)
            return Vector2.right;

        direction.Normalize();
        return direction;
    }

    private bool TryCollectLimbContourPoints(
        LimbLine line,
        LayerVertexData data,
        int contourSteps,
        float contourStepDistance,
        List<Vector2> contourPoints)
    {
        if (contourPoints == null)
            return false;

        contourPoints.Clear();
        if (line == null || data == null || !line.hasContourPoints)
            return false;

        var a = line.contourPointA;
        var b = line.contourPointB;
        AddUniqueDocPoint(contourPoints, a);
        AddUniqueDocPoint(contourPoints, b);

        contourSteps = Mathf.Max(0, contourSteps);
        if (contourSteps <= 0 || contourStepDistance <= 0.0001f)
            return contourPoints.Count > 0;

        if (!TryResolveContourLoopPoints(data, out var contourLoop) ||
            !TryBuildContourPerimeterSamples(contourLoop, out var segmentLengths, out var perimeterLength))
        {
            return contourPoints.Count > 0;
        }

        var stepDistance = Mathf.Max(0.01f, contourStepDistance);
        if (TryProjectPointToContourPerimeter(contourLoop, segmentLengths, perimeterLength, a, out var distanceFromA))
        {
            for (var step = 1; step <= contourSteps; step++)
            {
                if (TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromA + stepDistance * step, out var forward))
                    AddUniqueDocPoint(contourPoints, forward);
                if (TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromA - stepDistance * step, out var backward))
                    AddUniqueDocPoint(contourPoints, backward);
            }
        }

        if (TryProjectPointToContourPerimeter(contourLoop, segmentLengths, perimeterLength, b, out var distanceFromB))
        {
            for (var step = 1; step <= contourSteps; step++)
            {
                if (TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromB + stepDistance * step, out var forward))
                    AddUniqueDocPoint(contourPoints, forward);
                if (TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromB - stepDistance * step, out var backward))
                    AddUniqueDocPoint(contourPoints, backward);
            }
        }

        return contourPoints.Count > 0;
    }

    private bool TryBuildLimbPreviewPoints(
        LimbLine line,
        LayerVertexData data,
        int contourSteps,
        float contourStepDistance,
        float contourShift,
        List<Vector2> output)
    {
        if (output == null)
            return false;

        output.Clear();
        if (line == null || data == null || !line.hasContourPoints)
            return false;

        var contourPoints = new List<Vector2>(Mathf.Max(8, contourSteps * 4 + 2));
        if (!TryCollectLimbContourPoints(line, data, contourSteps, contourStepDistance, contourPoints))
            return false;

        var center = GetLimbContourMidpoint(line);
        var lineDirection = GetLimbGuideDirection(line);
        for (var i = 0; i < contourPoints.Count; i++)
            AddUniqueDocPoint(output, contourPoints[i]);
        AddUniqueDocPoint(output, center);

        var shiftAmount = Mathf.Clamp01(contourShift);
        if (shiftAmount > 0.0001f)
        {
            for (var i = 0; i < contourPoints.Count; i++)
            {
                var fromPointToCenter = center - contourPoints[i];
                var alongLineToCenter = Vector2.Dot(fromPointToCenter, lineDirection);
                var shifted = contourPoints[i] + lineDirection * (alongLineToCenter * shiftAmount);
                AddUniqueDocPoint(output, shifted);
            }
        }

        return output.Count > 0;
    }

    private bool TryGetLimbExtremeGuideSegments(
        LimbLine line,
        LayerVertexData data,
        int contourSteps,
        float contourStepDistance,
        out Vector2 seg0Start,
        out Vector2 seg0End,
        out Vector2 seg1Start,
        out Vector2 seg1End)
    {
        seg0Start = Vector2.zero;
        seg0End = Vector2.zero;
        seg1Start = Vector2.zero;
        seg1End = Vector2.zero;

        if (line == null || data == null || !line.hasContourPoints)
            return false;

        if (!TryResolveContourLoopPoints(data, out var contourLoop))
            return false;
        if (!TryBuildContourPerimeterSamples(contourLoop, out var segmentLengths, out var perimeterLength))
            return false;
        if (!TryProjectPointToContourPerimeter(contourLoop, segmentLengths, perimeterLength, line.contourPointA, out var distanceFromA))
            return false;
        if (!TryProjectPointToContourPerimeter(contourLoop, segmentLengths, perimeterLength, line.contourPointB, out var distanceFromB))
            return false;

        var span = Mathf.Max(0, contourSteps) * Mathf.Max(0.01f, contourStepDistance);
        if (!TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromA + span, out var aForward))
            return false;
        if (!TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromA - span, out var aBackward))
            return false;
        if (!TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromB + span, out var bForward))
            return false;
        if (!TrySampleContourPointAtDistance(contourLoop, segmentLengths, perimeterLength, distanceFromB - span, out var bBackward))
            return false;

        // For the second contour point, directions are mirrored Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В Р вЂ№Р В Р вЂ Р В РІР‚С™Р РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В Р’В Р В РІР‚в„–Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р Р‹Р Р†РІР‚С›РЎС›Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р вЂ™Р’В¦Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В Р вЂ№Р В Р вЂ Р В РІР‚С™Р РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В Р’В Р В РІР‚в„–Р В Р’В Р вЂ™Р’В Р В Р Р‹Р Р†Р вЂљРЎС™Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В Р вЂ№Р В Р вЂ Р В РІР‚С™Р вЂ™Р’ВР В Р’В Р вЂ™Р’В Р В Р’В Р В РІР‚в„–Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р Р‹Р Р†РІР‚С›РЎС›Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’ВµР В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В»Р В Р’В Р вЂ™Р’В Р В Р’В Р В РІР‚в„–Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљР’В°Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р вЂ™Р’В¦Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В Р вЂ№Р В Р вЂ Р В РІР‚С™Р РЋРЎвЂє first point.
        // Pair A+ with B- and A- with B+ to avoid crossed/swapped guides.
        seg0Start = aForward;
        seg0End = bBackward;
        seg1Start = aBackward;
        seg1End = bForward;
        return true;
    }

    private bool TryGetLimbBoneGuideSegments(
        LimbLine line,
        out Vector2 seg0Start,
        out Vector2 seg0End,
        out Vector2 seg1Start,
        out Vector2 seg1End)
    {
        seg0Start = Vector2.zero;
        seg0End = Vector2.zero;
        seg1Start = Vector2.zero;
        seg1End = Vector2.zero;

        if (!TryResolveLimbRendererBonePair(
                line,
                out _,
                out _,
                out var anchor,
                out var parentBase,
                out var childBase,
                out _))
        {
            return false;
        }

        seg0Start = anchor;
        seg0End = parentBase;
        seg1Start = anchor;
        seg1End = childBase;
        return true;
    }

    private bool TryGetLimbNearestBoneGuideSegment(
        LimbLine line,
        out Vector2 segStart,
        out Vector2 segEnd)
    {
        return TryGetLimbNearestBoneGuideSegment(line, out segStart, out segEnd, out _);
    }

    private bool TryGetLimbNearestBoneGuideSegment(
        LimbLine line,
        out Vector2 segStart,
        out Vector2 segEnd,
        out int nearestBoneIndex)
    {
        segStart = Vector2.zero;
        segEnd = Vector2.zero;
        nearestBoneIndex = -1;
        if (!TryResolveLimbRendererBonePair(
                line,
                out var parentBoneIndex,
                out var childBoneIndex,
                out var anchor,
                out var parentBase,
                out var childBase,
                out nearestBoneIndex))
        {
            return false;
        }

        limbNearestBoneMissingLogged = false;
        segStart = anchor;
        if (nearestBoneIndex == childBoneIndex)
            segEnd = childBase;
        else if (nearestBoneIndex == parentBoneIndex)
            segEnd = parentBase;
        else
            segEnd = (anchor - parentBase).sqrMagnitude <= (anchor - childBase).sqrMagnitude ? parentBase : childBase;
        return true;
    }

    private bool TryGetLimbNearestChildGuideSegment(
        LimbLine line,
        int boneIndex,
        out Vector2 segStart,
        out Vector2 segEnd)
    {
        segStart = Vector2.zero;
        segEnd = Vector2.zero;
        if (!TryResolveLimbRendererBonePair(
                line,
                out var parentBoneIndex,
                out var childBoneIndex,
                out var anchor,
                out var parentBase,
                out var childBase,
                out _))
        {
            return false;
        }

        if (boneIndex != parentBoneIndex && boneIndex != childBoneIndex)
            return false;

        segStart = anchor;
        segEnd = boneIndex == parentBoneIndex ? childBase : parentBase;
        return true;
    }

    private void ReportLimbNearestBoneNotFound(LimbLine line, string reason)
    {
        if (limbNearestBoneMissingLogged)
            return;

        limbNearestBoneMissingLogged = true;
        var layerKey = line != null && !string.IsNullOrEmpty(line.layerStableKey) ? line.layerStableKey : "<unknown>";
        var boneCount = bones != null ? bones.Count : 0;
        ReanimatorLog.Error($"[Reanimator][Limbs] Nearest bone not found: {reason}. layer={layerKey}, bones={boneCount}");
    }

    private bool TryFindNearestChildBoneIndex(int parentIndex, Vector2 anchor, out int childIndex)
    {
        childIndex = -1;
        if (parentIndex < 0 || parentIndex >= bones.Count)
            return false;

        var bestDistanceSqr = float.MaxValue;
        for (var i = 0; i < bones.Count; i++)
        {
            var child = bones[i];
            if (child == null)
                continue;
            if (child.parentId != parentIndex)
                continue;

            var childBase = GetBoneBasePoint(child);
            var sqr = (childBase - anchor).sqrMagnitude;
            if (sqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = sqr;
            childIndex = i;
        }

        return childIndex >= 0;
    }

    private bool TryResolveLimbRendererBonePair(
        LimbLine line,
        out int parentBoneIndex,
        out int childBoneIndex,
        out Vector2 anchor,
        out Vector2 parentBase,
        out Vector2 childBase,
        out int nearestBoneIndex)
    {
        parentBoneIndex = -1;
        childBoneIndex = -1;
        nearestBoneIndex = -1;
        anchor = GetLimbContourMidpoint(line);
        parentBase = Vector2.zero;
        childBase = Vector2.zero;

        if (line == null || !line.hasContourPoints)
            return false;
        if (bones == null || bones.Count == 0)
        {
            ReportLimbNearestBoneNotFound(line, "bones list is empty");
            return false;
        }

        if (line.hasRequiredJointAnchor &&
            line.requiredParentBoneIndex >= 0 &&
            line.requiredParentBoneIndex < bones.Count &&
            line.requiredChildBoneIndex >= 0 &&
            line.requiredChildBoneIndex < bones.Count)
        {
            var requiredParent = bones[line.requiredParentBoneIndex];
            var requiredChild = bones[line.requiredChildBoneIndex];
            if (requiredParent != null &&
                requiredChild != null &&
                requiredChild.parentId == line.requiredParentBoneIndex)
            {
                parentBoneIndex = line.requiredParentBoneIndex;
                childBoneIndex = line.requiredChildBoneIndex;
                parentBase = GetBoneBasePoint(requiredParent);
                childBase = GetBoneBasePoint(requiredChild);
                nearestBoneIndex = (anchor - parentBase).sqrMagnitude <= (anchor - childBase).sqrMagnitude
                    ? parentBoneIndex
                    : childBoneIndex;
                limbNearestBoneMissingLogged = false;
                return true;
            }
        }

        var bestJointDistanceSqr = float.PositiveInfinity;
        var bestScore = float.PositiveInfinity;
        var found = false;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null || !bone.visible)
                continue;

            var parentIndex = bone.parentId;
            if (parentIndex >= 0 &&
                parentIndex < bones.Count &&
                bones[parentIndex] != null &&
                bones[parentIndex].visible)
            {
                EvaluateLimbBonePairCandidate(
                    anchor,
                    line,
                    parentIndex,
                    i,
                    ref found,
                    ref bestJointDistanceSqr,
                    ref bestScore,
                    ref parentBoneIndex,
                    ref childBoneIndex,
                    ref parentBase,
                    ref childBase,
                    ref nearestBoneIndex);
            }

            if (TryFindNearestVisibleChildBoneIndex(i, anchor, out var nearestChildIndex))
            {
                EvaluateLimbBonePairCandidate(
                    anchor,
                    line,
                    i,
                    nearestChildIndex,
                    ref found,
                    ref bestJointDistanceSqr,
                    ref bestScore,
                    ref parentBoneIndex,
                    ref childBoneIndex,
                    ref parentBase,
                    ref childBase,
                    ref nearestBoneIndex);
            }
        }

        if (found)
        {
            limbNearestBoneMissingLogged = false;
            return true;
        }

        ReportLimbNearestBoneNotFound(line, "no visible bone pair");
        return false;
    }

    private bool TryFindNearestVisibleChildBoneIndex(int parentIndex, Vector2 anchor, out int childIndex)
    {
        childIndex = -1;
        if (parentIndex < 0 || parentIndex >= bones.Count)
            return false;

        var bestDistanceSqr = float.PositiveInfinity;
        for (var i = 0; i < bones.Count; i++)
        {
            var child = bones[i];
            if (child == null || !child.visible)
                continue;
            if (child.parentId != parentIndex)
                continue;

            var childBaseCandidate = GetBoneBasePoint(child);
            var sqr = (childBaseCandidate - anchor).sqrMagnitude;
            if (sqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = sqr;
            childIndex = i;
        }

        return childIndex >= 0;
    }

    private void EvaluateLimbBonePairCandidate(
        Vector2 anchor,
        LimbLine line,
        int candidateParentIndex,
        int candidateChildIndex,
        ref bool found,
        ref float bestJointDistanceSqr,
        ref float bestScore,
        ref int bestParentIndex,
        ref int bestChildIndex,
        ref Vector2 bestParentBase,
        ref Vector2 bestChildBase,
        ref int bestNearestBoneIndex)
    {
        if (candidateParentIndex < 0 || candidateParentIndex >= bones.Count)
            return;
        if (candidateChildIndex < 0 || candidateChildIndex >= bones.Count)
            return;
        if (candidateParentIndex == candidateChildIndex)
            return;

        var parentBone = bones[candidateParentIndex];
        var childBone = bones[candidateChildIndex];
        if (parentBone == null || childBone == null || !parentBone.visible || !childBone.visible)
            return;

        var parentBase = GetBoneBasePoint(parentBone);
        var childBase = GetBoneBasePoint(childBone);
        var segmentDistanceSqr = DistancePointToSegmentSqr(anchor, parentBase, childBase);
        var parentDistanceSqr = (anchor - parentBase).sqrMagnitude;
        var childDistanceSqr = (anchor - childBase).sqrMagnitude;
        var segmentLengthSqr = (childBase - parentBase).sqrMagnitude;
        var guideDirection = GetLimbGuideDirection(line);
        var boneDirection = childBase - parentBase;
        var alignmentPenalty = 0f;
        if (boneDirection.sqrMagnitude > 1e-8f)
        {
            boneDirection.Normalize();
            alignmentPenalty = 1f - Mathf.Abs(Vector2.Dot(guideDirection, boneDirection));
        }

        var jointDistanceSqr = childDistanceSqr;
        var score = segmentDistanceSqr + parentDistanceSqr * 0.05f + alignmentPenalty * alignmentPenalty * 0.25f;
        if (found)
        {
            const float jointTieEpsilon = 1e-5f;
            if (jointDistanceSqr > bestJointDistanceSqr + jointTieEpsilon)
                return;
            if (Mathf.Abs(jointDistanceSqr - bestJointDistanceSqr) <= jointTieEpsilon && score >= bestScore)
                return;
        }

        found = true;
        bestJointDistanceSqr = jointDistanceSqr;
        bestScore = score;
        bestParentIndex = candidateParentIndex;
        bestChildIndex = candidateChildIndex;
        bestParentBase = parentBase;
        bestChildBase = childBase;
        bestNearestBoneIndex = parentDistanceSqr <= childDistanceSqr ? candidateParentIndex : candidateChildIndex;
    }

    private static float DistancePointToSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSqr = ab.sqrMagnitude;
        if (lengthSqr <= 1e-8f)
            return (point - a).sqrMagnitude;

        var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
        var projected = a + ab * t;
        return (point - projected).sqrMagnitude;
    }

    private static Vector2 GetBoneMidpoint(BoneInfo bone)
    {
        if (bone == null)
            return Vector2.zero;

        var rotated = bone.worldRotation * Vector3.right;
        var dir = new Vector2(rotated.x, rotated.y);
        return bone.worldPosition + dir * bone.length * 0.5f;
    }

    private static Vector2 GetBoneBasePoint(BoneInfo bone)
    {
        return bone != null ? bone.worldPosition : Vector2.zero;
    }

    private bool TryResolveContourLoopPoints(LayerVertexData data, out List<Vector2> loopPoints)
    {
        loopPoints = new List<Vector2>();
        if (data == null)
            return false;

        var vertices = GetLayerVerticesForDraw(data);
        if (vertices == null || vertices.Length < 3)
            return false;

        List<int> boundaryLoop;
        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertices.Length, out boundaryLoop) || boundaryLoop.Count < 3)
        {
            var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertices.Length);
            boundaryLoop = BuildFallbackBoundaryLoop(vertices, contourMask);
        }

        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;

        loopPoints.Capacity = boundaryLoop.Count;
        for (var i = 0; i < boundaryLoop.Count; i++)
        {
            var index = boundaryLoop[i];
            if (index < 0 || index >= vertices.Length)
                continue;
            loopPoints.Add(vertices[index]);
        }

        return loopPoints.Count >= 3;
    }

    private static bool TryBuildContourPerimeterSamples(
        IReadOnlyList<Vector2> loopPoints,
        out float[] segmentLengths,
        out float perimeterLength)
    {
        segmentLengths = null;
        perimeterLength = 0f;
        if (loopPoints == null || loopPoints.Count < 3)
            return false;

        segmentLengths = new float[loopPoints.Count];
        for (var i = 0; i < loopPoints.Count; i++)
        {
            var a = loopPoints[i];
            var b = loopPoints[(i + 1) % loopPoints.Count];
            var length = Vector2.Distance(a, b);
            segmentLengths[i] = length;
            perimeterLength += length;
        }

        return perimeterLength > 0.001f;
    }

    private static bool TryProjectPointToContourPerimeter(
        IReadOnlyList<Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        Vector2 point,
        out float perimeterDistance)
    {
        perimeterDistance = 0f;
        if (loopPoints == null || segmentLengths == null || loopPoints.Count < 3)
            return false;
        if (segmentLengths.Count != loopPoints.Count || perimeterLength <= 0.001f)
            return false;

        var nearestVertexIndex = -1;
        var nearestVertexDistanceSqr = float.MaxValue;
        for (var i = 0; i < loopPoints.Count; i++)
        {
            var sqr = (loopPoints[i] - point).sqrMagnitude;
            if (sqr < nearestVertexDistanceSqr)
            {
                nearestVertexDistanceSqr = sqr;
                nearestVertexIndex = i;
            }
        }

        // Keep perimeter projection stable around contour corners to avoid direction flips.
        var averageSegmentLength = perimeterLength / Mathf.Max(1, loopPoints.Count);
        var snapToVertexDistance = Mathf.Max(0.02f, averageSegmentLength * 0.01f);
        if (nearestVertexIndex >= 0 && nearestVertexDistanceSqr <= snapToVertexDistance * snapToVertexDistance)
        {
            var distanceToVertex = 0f;
            for (var i = 0; i < nearestVertexIndex; i++)
                distanceToVertex += segmentLengths[i];
            perimeterDistance = Mathf.Repeat(distanceToVertex, perimeterLength);
            return true;
        }

        var bestScore = float.MaxValue;
        var bestPerimeter = 0f;
        var accumulated = 0f;
        var found = false;
        for (var i = 0; i < loopPoints.Count; i++)
        {
            var a = loopPoints[i];
            var b = loopPoints[(i + 1) % loopPoints.Count];
            var segment = b - a;
            var sqrLength = segment.sqrMagnitude;
            if (sqrLength <= 1e-8f)
            {
                accumulated += segmentLengths[i];
                continue;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / sqrLength);
            var projected = a + segment * t;
            var sqrDistance = (projected - point).sqrMagnitude;
            var onEdgePenalty = (t <= 0.001f || t >= 0.999f) ? 1e-7f : 0f;
            var score = sqrDistance + onEdgePenalty;
            var onPerimeter = accumulated + segmentLengths[i] * t;
            if (score < bestScore - 1e-8f ||
                (Mathf.Abs(score - bestScore) <= 1e-8f && onPerimeter < bestPerimeter))
            {
                bestScore = score;
                bestPerimeter = onPerimeter;
                found = true;
            }

            accumulated += segmentLengths[i];
        }

        if (!found)
            return false;

        perimeterDistance = Mathf.Repeat(bestPerimeter, perimeterLength);
        return true;
    }

    private static bool TrySampleContourPointAtDistance(
        IReadOnlyList<Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        float perimeterDistance,
        out Vector2 point)
    {
        point = Vector2.zero;
        if (loopPoints == null || segmentLengths == null || loopPoints.Count < 3)
            return false;
        if (segmentLengths.Count != loopPoints.Count || perimeterLength <= 0.001f)
            return false;

        var target = Mathf.Repeat(perimeterDistance, perimeterLength);
        var accumulated = 0f;
        for (var i = 0; i < loopPoints.Count; i++)
        {
            var segmentLength = segmentLengths[i];
            if (segmentLength <= 1e-6f)
            {
                accumulated += segmentLength;
                continue;
            }

            if (target <= accumulated + segmentLength || i == loopPoints.Count - 1)
            {
                var t = Mathf.Clamp01((target - accumulated) / segmentLength);
                point = Vector2.Lerp(loopPoints[i], loopPoints[(i + 1) % loopPoints.Count], t);
                return true;
            }

            accumulated += segmentLength;
        }

        point = loopPoints[0];
        return true;
    }

    private static void AddUniqueDocPoint(List<Vector2> points, Vector2 point, float tolerance = 0.01f)
    {
        if (points == null)
            return;

        var toleranceSqr = tolerance * tolerance;
        for (var i = 0; i < points.Count; i++)
        {
            if ((points[i] - point).sqrMagnitude <= toleranceSqr)
                return;
        }

        points.Add(point);
    }

    private bool TryGetLayerVertexDataForKeys(string selectionKey, string stableKey, out LayerVertexData data)
    {
        data = null;
        if (!string.IsNullOrEmpty(selectionKey) &&
            vertexDataByLayerSelectionKey.TryGetValue(selectionKey, out data) &&
            data != null)
            return true;

        return TryGetLayerVertexDataByStableKey(stableKey, out data, out _);
    }

    private bool TryGetLayerVertexData(LayerInfo layer, out LayerVertexData data)
    {
        data = null;
        if (layer == null)
            return false;

        var selectionKey = BuildSelectionKey(layer);
        if (!string.IsNullOrEmpty(selectionKey) &&
            vertexDataByLayerSelectionKey.TryGetValue(selectionKey, out data) &&
            data != null &&
            ReferenceEquals(data.layer, layer))
            return true;

        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var candidate = pair.Value;
            if (candidate == null || !ReferenceEquals(candidate.layer, layer))
                continue;

            data = candidate;
            return true;
        }

        data = null;
        return false;
    }

    private bool TryGetLayerGeometryVertices(LayerInfo layer, out Vector2[] vertices)
    {
        vertices = null;
        if (!TryGetLayerVertexData(layer, out var data) || data.vertices == null || data.vertices.Length == 0)
            return false;

        vertices = data.vertices;
        return true;
    }

    private bool HasLayerGeometry(LayerInfo layer)
    {
        return TryGetLayerGeometryVertices(layer, out _);
    }

    private bool TryGetLayerVertexDataByStableKey(string stableKey, out LayerVertexData data, out string selectionKey)
    {
        data = null;
        selectionKey = string.Empty;
        if (string.IsNullOrEmpty(stableKey))
            return false;
        if (layerInfos == null || layerInfos.Count == 0 || vertexDataByLayerSelectionKey.Count == 0)
            return false;

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;
            if (!string.Equals(BuildKey(layer.name, layer.layerId), stableKey, StringComparison.OrdinalIgnoreCase))
                continue;

            selectionKey = BuildSelectionKey(layer);
            if (!vertexDataByLayerSelectionKey.TryGetValue(selectionKey, out data) || data == null)
                break;

            return true;
        }

        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var candidate = pair.Value;
            var candidateLayer = candidate != null ? candidate.layer : null;
            if (candidateLayer == null)
                continue;
            if (!string.Equals(BuildKey(candidateLayer.name, candidateLayer.layerId), stableKey, StringComparison.OrdinalIgnoreCase))
                continue;

            selectionKey = pair.Key;
            data = candidate;
            return true;
        }

        data = null;
        selectionKey = string.Empty;
        return false;
    }

    private bool TryGetElbowContourPoints(Vector2 startDoc, Vector2 endDoc, LayerVertexData data, out Vector2 p0, out Vector2 p1)
    {
        p0 = Vector2.zero;
        p1 = Vector2.zero;
        if (data == null)
            return false;

        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 2)
            return false;

        var vertices = GetLayerVerticesForDraw(data);
        if (vertices == null || vertices.Length == 0)
            return false;

        var dir = endDoc - startDoc;
        if (dir.sqrMagnitude <= 1e-8f)
            return false;

        var hits = new List<LineHit>(16);
        var pairCount = boundaryPairs.Length / 2;
        for (var i = 0; i < pairCount; i++)
        {
            var aIdx = boundaryPairs[i * 2];
            var bIdx = boundaryPairs[i * 2 + 1];
            if (aIdx < 0 || bIdx < 0 || aIdx >= vertices.Length || bIdx >= vertices.Length)
                continue;

            if (!TryIntersectInfiniteLineSegment(startDoc, dir, vertices[aIdx], vertices[bIdx], out var t, out var hit))
                continue;

            var duplicate = false;
            for (var h = 0; h < hits.Count; h++)
            {
                if (Vector2.Distance(hits[h].point, hit) <= 0.01f || Mathf.Abs(hits[h].t - t) <= 0.001f)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                hits.Add(new LineHit { t = t, point = hit });
        }

        if (hits.Count < 2)
            return false;

        hits.Sort((a, b) => a.t.CompareTo(b.t));
        if (!TrySelectClosestCrossingPair(hits, 0.5f, out var hitA, out var hitB))
            return false;

        p0 = hitA.point;
        p1 = hitB.point;
        return Vector2.Distance(p0, p1) > 0.01f;
    }

    private static bool TrySelectClosestCrossingPair(
        IReadOnlyList<LineHit> sortedHits,
        float preferredT,
        out LineHit hitA,
        out LineHit hitB)
    {
        hitA = new LineHit();
        hitB = new LineHit();
        if (sortedHits == null || sortedHits.Count < 2)
            return false;

        var bestPairIndex = -1;
        var bestScore = float.MaxValue;
        for (var i = 0; i + 1 < sortedHits.Count; i++)
        {
            var a = sortedHits[i];
            var b = sortedHits[i + 1];
            if (b.t <= a.t + 1e-5f)
                continue;
            if ((a.point - b.point).sqrMagnitude <= 1e-4f)
                continue;

            var clamped = Mathf.Clamp(preferredT, a.t, b.t);
            var intervalDistance = Mathf.Abs(preferredT - clamped);
            var midpointDistance = Mathf.Abs(((a.t + b.t) * 0.5f) - preferredT);
            // Any interval containing preferredT wins first, then choose the closest midpoint.
            var score = intervalDistance * 1000f + midpointDistance;
            if (score < bestScore)
            {
                bestScore = score;
                bestPairIndex = i;
            }
        }

        if (bestPairIndex < 0)
        {
            var bestDistanceSqr = 0f;
            for (var i = 0; i + 1 < sortedHits.Count; i++)
            {
                for (var j = i + 1; j < sortedHits.Count; j++)
                {
                    var a = sortedHits[i];
                    var b = sortedHits[j];
                    if (Mathf.Abs(b.t - a.t) <= 1e-5f)
                        continue;
                    var sqr = (a.point - b.point).sqrMagnitude;
                    if (sqr <= 1e-4f || sqr <= bestDistanceSqr)
                        continue;
                    bestDistanceSqr = sqr;
                    hitA = a;
                    hitB = b;
                }
            }

            return bestDistanceSqr > 1e-4f;
        }

        hitA = sortedHits[bestPairIndex];
        hitB = sortedHits[bestPairIndex + 1];
        return true;
    }

    private static bool TryIntersectInfiniteLineSegment(
        Vector2 linePoint,
        Vector2 lineDir,
        Vector2 segA,
        Vector2 segB,
        out float lineT,
        out Vector2 hit)
    {
        return ReanimatorJointGeometryAlgorithms.TryIntersectInfiniteLineSegment(linePoint, lineDir, segA, segB, out lineT, out hit);
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
#endif
