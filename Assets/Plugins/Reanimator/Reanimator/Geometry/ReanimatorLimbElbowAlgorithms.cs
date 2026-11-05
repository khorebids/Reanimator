#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorLimbElbowAlgorithms
{
    public const float GeometryPointMergeTolerance = 0.01f;

    public static float SignedDistanceToLine(Vector2 axis, Vector2 origin, Vector2 point)
    {
        var length = axis.magnitude;
        if (length <= 1e-6f)
            return 0f;

        var normalizedAxis = axis / length;
        var delta = point - origin;
        return normalizedAxis.x * delta.y - normalizedAxis.y * delta.x;
    }

    public static bool PositiveSideUsesParent(
        Vector2 axis,
        Vector2 origin,
        Vector2 parentMidpoint,
        Vector2 childMidpoint)
    {
        var parentSide = SignedDistanceToLine(axis, origin, parentMidpoint);
        if (Mathf.Abs(parentSide) > 1e-5f)
            return parentSide > 0f;

        var childSide = SignedDistanceToLine(axis, origin, childMidpoint);
        if (Mathf.Abs(childSide) > 1e-5f)
            return childSide < 0f;

        return true;
    }

    public static bool IsPointOnGuideSegment(
        Vector2 point,
        Vector2 guideStart,
        Vector2 guideEnd,
        float tolerance = GeometryPointMergeTolerance)
    {
        var segment = guideEnd - guideStart;
        var segmentLengthSqr = segment.sqrMagnitude;
        if (segmentLengthSqr <= 1e-8f)
            return false;

        var t = Mathf.Clamp01(Vector2.Dot(point - guideStart, segment) / segmentLengthSqr);
        var closest = guideStart + segment * t;
        var resolvedTolerance = Mathf.Max(0f, tolerance);
        return (point - closest).sqrMagnitude <= resolvedTolerance * resolvedTolerance;
    }

    public static List<int> BuildContourReplacementLoop(
        IReadOnlyList<int> sourceBoundaryLoop,
        IReadOnlyList<Vector2> sourceVertices,
        IReadOnlyList<bool> removeMask,
        IReadOnlyList<int> remap,
        IReadOnlyList<Vector2> replacementPoints,
        IReadOnlyList<int> replacementVertexIndices,
        int targetVertexCount)
    {
        var result = new List<int>();
        if (sourceBoundaryLoop == null || sourceBoundaryLoop.Count < 3 ||
            sourceVertices == null || removeMask == null || remap == null ||
            replacementPoints == null || replacementVertexIndices == null ||
            replacementPoints.Count != replacementVertexIndices.Count || targetVertexCount < 3)
        {
            return result;
        }

        var replacementsByEdge = new List<ContourReplacement>[sourceBoundaryLoop.Count];
        for (var replacementIndex = 0; replacementIndex < replacementPoints.Count; replacementIndex++)
        {
            var targetIndex = replacementVertexIndices[replacementIndex];
            if (targetIndex < 0 || targetIndex >= targetVertexCount)
                continue;

            var bestEdge = -1;
            var bestT = 0f;
            var bestDistanceSqr = float.PositiveInfinity;
            for (var edgeIndex = 0; edgeIndex < sourceBoundaryLoop.Count; edgeIndex++)
            {
                var aIndex = sourceBoundaryLoop[edgeIndex];
                var bIndex = sourceBoundaryLoop[(edgeIndex + 1) % sourceBoundaryLoop.Count];
                if (aIndex < 0 || bIndex < 0 || aIndex >= sourceVertices.Count || bIndex >= sourceVertices.Count)
                    continue;

                var segment = sourceVertices[bIndex] - sourceVertices[aIndex];
                var lengthSqr = segment.sqrMagnitude;
                if (lengthSqr <= 1e-8f)
                    continue;

                var t = Mathf.Clamp01(Vector2.Dot(replacementPoints[replacementIndex] - sourceVertices[aIndex], segment) / lengthSqr);
                var projected = sourceVertices[aIndex] + segment * t;
                var distanceSqr = (replacementPoints[replacementIndex] - projected).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                    continue;

                bestDistanceSqr = distanceSqr;
                bestEdge = edgeIndex;
                bestT = t;
            }

            if (bestEdge < 0)
                continue;

            if (replacementsByEdge[bestEdge] == null)
                replacementsByEdge[bestEdge] = new List<ContourReplacement>();
            replacementsByEdge[bestEdge].Add(new ContourReplacement(targetIndex, bestT));
        }

        var added = new HashSet<int>();
        for (var edgeIndex = 0; edgeIndex < sourceBoundaryLoop.Count; edgeIndex++)
        {
            var sourceIndex = sourceBoundaryLoop[edgeIndex];
            if (sourceIndex >= 0 && sourceIndex < removeMask.Count && sourceIndex < remap.Count &&
                !removeMask[sourceIndex] && remap[sourceIndex] >= 0 && remap[sourceIndex] < targetVertexCount &&
                added.Add(remap[sourceIndex]))
            {
                result.Add(remap[sourceIndex]);
            }

            var replacements = replacementsByEdge[edgeIndex];
            if (replacements == null || replacements.Count == 0)
                continue;

            replacements.Sort((lhs, rhs) => lhs.t.CompareTo(rhs.t));
            for (var i = 0; i < replacements.Count; i++)
            {
                var targetIndex = replacements[i].vertexIndex;
                if (added.Add(targetIndex))
                    result.Add(targetIndex);
            }
        }

        return result;
    }

    public static void EvaluateLimbSideWeights(
        float signedDistance,
        float maxPositiveDistance,
        float maxNegativeDistance,
        bool positiveSideUsesParent,
        AnimationCurve curve,
        out float parentWeight,
        out float childWeight)
    {
        if (Mathf.Abs(signedDistance) <= 1e-5f)
        {
            parentWeight = 0.5f;
            childWeight = 0.5f;
            return;
        }

        var positiveSide = signedDistance > 0f;
        var maxDistance = positiveSide ? maxPositiveDistance : maxNegativeDistance;
        var normalizedDistance = maxDistance > 1e-6f
            ? Mathf.Clamp01(Mathf.Abs(signedDistance) / maxDistance)
            : 0f;
        var curveX = 1f - normalizedDistance;
        var curveValue = Mathf.Clamp01(curve != null ? curve.Evaluate(curveX) : curveX);
        var oppositeWeight = 0.5f * curveValue;
        var dominantWeight = 1f - oppositeWeight;
        var pointUsesParent = positiveSide == positiveSideUsesParent;
        parentWeight = pointUsesParent ? dominantWeight : oppositeWeight;
        childWeight = pointUsesParent ? oppositeWeight : dominantWeight;
    }

    public static BoneWeight BuildLimbTwoBoneWeight(int parentSpriteBone, float parentWeight, int childSpriteBone, float childWeight)
    {
        parentWeight = Mathf.Clamp01(parentWeight);
        childWeight = Mathf.Clamp01(childWeight);
        var sum = parentWeight + childWeight;
        if (sum <= 1e-6f)
        {
            parentWeight = 0.5f;
            childWeight = 0.5f;
        }
        else
        {
            var inv = 1f / sum;
            parentWeight *= inv;
            childWeight *= inv;
        }

        return new BoneWeight
        {
            boneIndex0 = parentSpriteBone,
            weight0 = parentWeight,
            boneIndex1 = childSpriteBone,
            weight1 = childWeight,
            boneIndex2 = 0,
            weight2 = 0f,
            boneIndex3 = 0,
            weight3 = 0f
        };
    }

    public static bool TryBuildElbowArcInteriorPoints(
        Vector2 a,
        Vector2 b,
        int interiorPointCount,
        float bend,
        List<Vector2> output)
    {
        if (output == null)
            return false;

        output.Clear();
        if (interiorPointCount <= 0)
            return true;

        var chord = b - a;
        var chordLength = chord.magnitude;
        if (chordLength <= 1e-4f)
            return false;

        if (Mathf.Abs(bend) <= 1e-4f)
        {
            BuildLinearInteriorPoints(a, b, interiorPointCount, output);
            return true;
        }

        var normal = new Vector2(-chord.y, chord.x) / chordLength;
        var midpoint = (a + b) * 0.5f;
        var sagitta = chordLength * 0.25f * Mathf.Abs(bend);
        if (sagitta <= 1e-4f)
        {
            BuildLinearInteriorPoints(a, b, interiorPointCount, output);
            return true;
        }

        var control = midpoint + normal * (Mathf.Sign(bend) * sagitta);
        if (!TryComputeCircleFromThreePoints(a, control, b, out var center))
            return false;

        var angleA = Mathf.Atan2(a.y - center.y, a.x - center.x);
        var angleB = Mathf.Atan2(b.y - center.y, b.x - center.x);
        var angleM = Mathf.Atan2(control.y - center.y, control.x - center.x);
        if (!TryResolveArcSweepThroughPoint(angleA, angleB, angleM, out var direction, out var sweep))
            return false;

        for (var i = 1; i <= interiorPointCount; i++)
        {
            var t = i / (interiorPointCount + 1f);
            var angle = angleA + direction * sweep * t;
            output.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Vector2.Distance(center, a));
        }

        return true;
    }

    public static void BuildLinearInteriorPoints(Vector2 a, Vector2 b, int interiorPointCount, List<Vector2> output)
    {
        if (output == null)
            return;

        output.Clear();
        for (var i = 1; i <= interiorPointCount; i++)
        {
            var t = i / (interiorPointCount + 1f);
            output.Add(Vector2.Lerp(a, b, t));
        }
    }

    public static bool TryComputeCircleFromThreePoints(Vector2 a, Vector2 m, Vector2 b, out Vector2 center)
    {
        center = Vector2.zero;

        var ax = a.x;
        var ay = a.y;
        var bx = m.x;
        var by = m.y;
        var cx = b.x;
        var cy = b.y;

        var d = 2f * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Mathf.Abs(d) <= 1e-6f)
            return false;

        var ax2ay2 = ax * ax + ay * ay;
        var bx2by2 = bx * bx + by * by;
        var cx2cy2 = cx * cx + cy * cy;

        var ux = (ax2ay2 * (by - cy) + bx2by2 * (cy - ay) + cx2cy2 * (ay - by)) / d;
        var uy = (ax2ay2 * (cx - bx) + bx2by2 * (ax - cx) + cx2cy2 * (bx - ax)) / d;
        center = new Vector2(ux, uy);
        return true;
    }

    public static bool TryResolveArcSweepThroughPoint(float angleA, float angleB, float angleM, out float direction, out float sweep)
    {
        direction = 1f;
        sweep = 0f;

        var deltaABccw = PositiveDeltaAngle(angleA, angleB);
        var deltaAMccw = PositiveDeltaAngle(angleA, angleM);
        const float epsilon = 1e-5f;
        if (deltaAMccw >= -epsilon && deltaAMccw <= deltaABccw + epsilon)
        {
            direction = 1f;
            sweep = deltaABccw;
            return true;
        }

        var deltaBAccw = PositiveDeltaAngle(angleB, angleA);
        var deltaBMccw = PositiveDeltaAngle(angleB, angleM);
        if (deltaBMccw >= -epsilon && deltaBMccw <= deltaBAccw + epsilon)
        {
            direction = -1f;
            sweep = deltaBAccw;
            return true;
        }

        return false;
    }

    public static float PositiveDeltaAngle(float from, float to)
    {
        var delta = to - from;
        while (delta < 0f)
            delta += Mathf.PI * 2f;
        while (delta >= Mathf.PI * 2f)
            delta -= Mathf.PI * 2f;
        return delta;
    }

    private readonly struct ContourReplacement
    {
        public readonly int vertexIndex;
        public readonly float t;

        public ContourReplacement(int vertexIndex, float t)
        {
            this.vertexIndex = vertexIndex;
            this.t = t;
        }
    }
}
#endif
