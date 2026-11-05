#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorVolumeCurveAlgorithms
{
    public static int ApplyCurveToWeights(
        PsdLayerPreviewRenderer.VolumeCurve curve,
        IReadOnlyList<Vector2> verticesDoc,
        IReadOnlyList<BoneWeight> sourceWeights,
        int targetSpriteBone,
        int spriteBoneCount,
        out BoneWeight[] result)
    {
        result = null;
        if (curve == null || verticesDoc == null || verticesDoc.Count == 0)
            return 0;
        if (targetSpriteBone < 0 || targetSpriteBone >= spriteBoneCount)
            return 0;

        result = new BoneWeight[verticesDoc.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = sourceWeights != null && i < sourceWeights.Count ? sourceWeights[i] : default;

        var radius = Mathf.Max(0.0001f, curve.radius);
        var strength = Mathf.Clamp01(curve.strength);
        if (radius <= 0.0001f || strength <= 0.0001f)
            return 0;

        var curvePoints = BuildDocumentDrawPoints(curve);
        if (curvePoints.Count < 2)
            return 0;

        var changed = 0;
        for (var i = 0; i < verticesDoc.Count; i++)
        {
            var influence = EvaluateInfluence(curvePoints, verticesDoc[i], radius, strength, curve.falloff);
            if (influence <= 0.0001f)
                continue;

            var next = BuildInfluencedBoneWeight(result[i], targetSpriteBone, spriteBoneCount, influence);
            if (!AreBoneWeightsEqual(result[i], next))
            {
                result[i] = next;
                changed++;
            }
        }

        return changed;
    }

    public static float EvaluateInfluence(
        IReadOnlyList<Vector2> curvePointsDoc,
        Vector2 pointDoc,
        float radius,
        float strength,
        AnimationCurve falloff)
    {
        if (curvePointsDoc == null || curvePointsDoc.Count < 2)
            return 0f;

        radius = Mathf.Max(0.0001f, radius);
        var bestDistanceSqr = float.MaxValue;
        for (var i = 0; i + 1 < curvePointsDoc.Count; i++)
        {
            var distanceSqr = DistancePointToSegmentSqr(pointDoc, curvePointsDoc[i], curvePointsDoc[i + 1]);
            if (distanceSqr < bestDistanceSqr)
                bestDistanceSqr = distanceSqr;
        }

        if (bestDistanceSqr > radius * radius)
            return 0f;

        var proximity = 1f - Mathf.Clamp01(Mathf.Sqrt(bestDistanceSqr) / radius);
        var falloffValue = falloff != null ? falloff.Evaluate(proximity) : proximity;
        return Mathf.Clamp01(falloffValue) * Mathf.Clamp01(strength);
    }

    public static List<Vector2> BuildDocumentDrawPoints(PsdLayerPreviewRenderer.VolumeCurve curve)
    {
        var result = new List<Vector2>();
        if (curve == null || curve.points == null)
            return result;

        var segmentCount = curve.closed ? curve.points.Count : curve.points.Count - 1;
        for (var i = 0; i < segmentCount; i++)
        {
            var a = curve.points[i];
            var b = curve.points[(i + 1) % curve.points.Count];
            if (a == null || b == null)
                continue;

            AppendSegmentPoints(result, curve, i, a);
        }

        return result;
    }

    public static Vector2 EvaluateSegment(PsdLayerPreviewRenderer.VolumeCurve curve, int pointIndex, float t)
    {
        if (curve == null || curve.points == null || curve.points.Count == 0)
            return Vector2.zero;
        if (pointIndex < 0 || pointIndex >= curve.points.Count)
            return Vector2.zero;

        var points = curve.points;
        var a = points[pointIndex];
        var b = points[(pointIndex + 1) % points.Count];
        if (a == null || b == null)
            return Vector2.zero;

        t = Mathf.Clamp01(t);
        switch (a.outType)
        {
            case PsdLayerPreviewRenderer.VolumeCurvePointType.Catmull:
                return EvaluateCatmull(points, pointIndex, t, curve.closed);
            case PsdLayerPreviewRenderer.VolumeCurvePointType.Euler:
                return Vector2.Lerp(a.positionDoc, b.positionDoc, Mathf.SmoothStep(0f, 1f, t));
            case PsdLayerPreviewRenderer.VolumeCurvePointType.Bezier:
                return EvaluateQuadraticBezier(points, pointIndex, t, curve.closed);
            default:
                return Vector2.Lerp(a.positionDoc, b.positionDoc, t);
        }
    }

    public static List<Vector3> BuildDrawPoints(
        PsdLayerPreviewRenderer.VolumeCurve curve,
        Func<Vector2, Vector2> documentToView)
    {
        var result = new List<Vector3>();
        if (curve == null || curve.points == null || documentToView == null)
            return result;

        var segmentCount = curve.closed ? curve.points.Count : curve.points.Count - 1;
        for (var i = 0; i < segmentCount; i++)
        {
            var a = curve.points[i];
            var b = curve.points[(i + 1) % curve.points.Count];
            if (a == null || b == null)
                continue;

            AppendSegmentPoints(result, documentToView, curve, i, a);
        }

        return result;
    }

    public static int FindCurvePointAt(
        PsdLayerPreviewRenderer.VolumeCurve curve,
        Func<Vector2, Vector2> documentToView,
        Vector2 mouseCanvas,
        float radius)
    {
        if (curve == null || curve.points == null || documentToView == null)
            return -1;

        var bestIndex = -1;
        var bestDistanceSqr = radius * radius;
        for (var i = 0; i < curve.points.Count; i++)
        {
            var point = curve.points[i];
            if (point == null)
                continue;

            var canvas = documentToView(point.positionDoc);
            var distanceSqr = (canvas - mouseCanvas).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestIndex = i;
        }

        return bestIndex;
    }

    public static int FindAnyCurvePointAt(
        IReadOnlyList<PsdLayerPreviewRenderer.VolumeCurve> curves,
        Func<Vector2, Vector2> documentToView,
        Vector2 mouseCanvas,
        float radius,
        out int curveIndex)
    {
        curveIndex = -1;
        if (curves == null || documentToView == null)
            return -1;

        var bestPointIndex = -1;
        var bestDistanceSqr = radius * radius;
        for (var i = 0; i < curves.Count; i++)
        {
            var curve = curves[i];
            if (curve == null || curve.points == null)
                continue;

            var pointIndex = FindCurvePointAt(curve, documentToView, mouseCanvas, Mathf.Sqrt(bestDistanceSqr));
            if (pointIndex < 0)
                continue;

            var point = curve.points[pointIndex];
            if (point == null)
                continue;

            var canvas = documentToView(point.positionDoc);
            var distanceSqr = (canvas - mouseCanvas).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            curveIndex = i;
            bestPointIndex = pointIndex;
        }

        return bestPointIndex;
    }

    public static int FindCurveAt(
        IReadOnlyList<PsdLayerPreviewRenderer.VolumeCurve> curves,
        Func<Vector2, Vector2> documentToView,
        Vector2 mouseCanvas,
        float maxDistance)
    {
        return TryFindCurveSegmentAt(curves, documentToView, mouseCanvas, maxDistance, out var curveIndex, out _, out _)
            ? curveIndex
            : -1;
    }

    public static bool TryFindCurveSegmentAt(
        IReadOnlyList<PsdLayerPreviewRenderer.VolumeCurve> curves,
        Func<Vector2, Vector2> documentToView,
        Vector2 mouseCanvas,
        float maxDistance,
        out int curveIndex,
        out int segmentIndex,
        out Vector2 nearestDocumentPoint)
    {
        curveIndex = -1;
        segmentIndex = -1;
        nearestDocumentPoint = Vector2.zero;
        if (curves == null || documentToView == null)
            return false;

        var bestDistanceSqr = maxDistance * maxDistance;
        var bestSegmentT = 0f;
        for (var i = 0; i < curves.Count; i++)
        {
            var curve = curves[i];
            if (curve == null || curve.points == null)
                continue;

            var segmentCount = curve.closed ? curve.points.Count : curve.points.Count - 1;
            for (var segment = 0; segment < segmentCount; segment++)
            {
                var a = curve.points[segment];
                if (a == null)
                    continue;

                var steps = a.outType == PsdLayerPreviewRenderer.VolumeCurvePointType.Linear ? 1 : 16;
                var previousDoc = EvaluateSegment(curve, segment, 0f);
                var previousView = documentToView(previousDoc);
                for (var step = 1; step <= steps; step++)
                {
                    var t1 = step / (float)steps;
                    var currentDoc = EvaluateSegment(curve, segment, t1);
                    var currentView = documentToView(currentDoc);
                    var subSegmentT = ClosestSegmentT(mouseCanvas, previousView, currentView);
                    var closestView = Vector2.Lerp(previousView, currentView, subSegmentT);
                    var distanceSqr = (mouseCanvas - closestView).sqrMagnitude;
                    if (distanceSqr <= bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
                        curveIndex = i;
                        segmentIndex = segment;
                        bestSegmentT = ((step - 1) + subSegmentT) / steps;
                    }

                    previousDoc = currentDoc;
                    previousView = currentView;
                }
            }
        }

        if (curveIndex < 0 || segmentIndex < 0)
            return false;

        nearestDocumentPoint = EvaluateSegment(curves[curveIndex], segmentIndex, bestSegmentT);
        return true;
    }

    public static Vector2 BuildDefaultPointPosition(PsdLayerPreviewRenderer.VolumeCurve curve)
    {
        if (curve != null && curve.points != null && curve.points.Count > 0)
            return curve.points[curve.points.Count - 1].positionDoc + new Vector2(24f, 0f);

        return Vector2.zero;
    }

    public static bool TryGetBoundsCenter(PsdLayerPreviewRenderer.VolumeCurve curve, out Vector2 center)
    {
        center = Vector2.zero;
        if (curve == null || curve.points == null || curve.points.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < curve.points.Count; i++)
        {
            var point = curve.points[i];
            if (point == null)
                continue;

            min = Vector2.Min(min, point.positionDoc);
            max = Vector2.Max(max, point.positionDoc);
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return false;

        center = (min + max) * 0.5f;
        return true;
    }

    private static BoneWeight BuildInfluencedBoneWeight(BoneWeight source, int targetSpriteBone, int spriteBoneCount, float targetWeight)
    {
        source = ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(source, spriteBoneCount, false, true);
        targetWeight = Mathf.Clamp01(Mathf.Max(GetBoneWeight(source, targetSpriteBone), targetWeight));
        var otherInfluences = new Dictionary<int, float>(4);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex0, source.weight0, targetSpriteBone, spriteBoneCount);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex1, source.weight1, targetSpriteBone, spriteBoneCount);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex2, source.weight2, targetSpriteBone, spriteBoneCount);
        AccumulateOtherInfluence(otherInfluences, source.boneIndex3, source.weight3, targetSpriteBone, spriteBoneCount);

        if (otherInfluences.Count == 0)
            targetWeight = 1f;

        var otherList = new List<KeyValuePair<int, float>>(otherInfluences);
        otherList.Sort((lhs, rhs) => rhs.Value.CompareTo(lhs.Value));
        if (otherList.Count > 3)
            otherList.RemoveRange(3, otherList.Count - 3);

        var otherSum = 0f;
        for (var i = 0; i < otherList.Count; i++)
            otherSum += Mathf.Max(0f, otherList[i].Value);

        var otherCapacity = Mathf.Clamp01(1f - targetWeight);
        if (otherSum > 0.0001f)
        {
            var scale = otherCapacity / otherSum;
            for (var i = 0; i < otherList.Count; i++)
                otherList[i] = new KeyValuePair<int, float>(otherList[i].Key, Mathf.Max(0f, otherList[i].Value) * scale);
        }

        return new BoneWeight
        {
            boneIndex0 = targetSpriteBone,
            weight0 = targetWeight,
            boneIndex1 = otherList.Count > 0 ? otherList[0].Key : targetSpriteBone,
            weight1 = otherList.Count > 0 ? otherList[0].Value : 0f,
            boneIndex2 = otherList.Count > 1 ? otherList[1].Key : targetSpriteBone,
            weight2 = otherList.Count > 1 ? otherList[1].Value : 0f,
            boneIndex3 = otherList.Count > 2 ? otherList[2].Key : targetSpriteBone,
            weight3 = otherList.Count > 2 ? otherList[2].Value : 0f
        };
    }

    private static float GetBoneWeight(BoneWeight source, int targetSpriteBone)
    {
        var result = 0f;
        if (source.boneIndex0 == targetSpriteBone)
            result += source.weight0;
        if (source.boneIndex1 == targetSpriteBone)
            result += source.weight1;
        if (source.boneIndex2 == targetSpriteBone)
            result += source.weight2;
        if (source.boneIndex3 == targetSpriteBone)
            result += source.weight3;
        return Mathf.Clamp01(result);
    }

    private static void AccumulateOtherInfluence(Dictionary<int, float> influences, int spriteBone, float weight, int targetSpriteBone, int spriteBoneCount)
    {
        if (influences == null || spriteBone < 0 || spriteBone == targetSpriteBone || weight <= 0.0001f)
            return;
        if (spriteBoneCount > 0 && spriteBone >= spriteBoneCount)
            return;

        influences.TryGetValue(spriteBone, out var current);
        influences[spriteBone] = current + Mathf.Max(0f, weight);
    }

    private static bool AreBoneWeightsEqual(BoneWeight lhs, BoneWeight rhs)
    {
        return lhs.boneIndex0 == rhs.boneIndex0 &&
               lhs.boneIndex1 == rhs.boneIndex1 &&
               lhs.boneIndex2 == rhs.boneIndex2 &&
               lhs.boneIndex3 == rhs.boneIndex3 &&
               Mathf.Abs(lhs.weight0 - rhs.weight0) <= 0.0001f &&
               Mathf.Abs(lhs.weight1 - rhs.weight1) <= 0.0001f &&
               Mathf.Abs(lhs.weight2 - rhs.weight2) <= 0.0001f &&
               Mathf.Abs(lhs.weight3 - rhs.weight3) <= 0.0001f;
    }

    private static void AppendSegmentPoints(
        List<Vector3> result,
        Func<Vector2, Vector2> documentToView,
        PsdLayerPreviewRenderer.VolumeCurve curve,
        int pointIndex,
        PsdLayerPreviewRenderer.VolumeCurvePoint a)
    {
        var steps = a.outType == PsdLayerPreviewRenderer.VolumeCurvePointType.Linear ? 1 : 16;
        for (var step = 0; step <= steps; step++)
        {
            if (result.Count > 0 && step == 0)
                continue;

            var t = step / (float)steps;
            var doc = EvaluateSegment(curve, pointIndex, t);
            var canvas = documentToView(doc);
            result.Add(new Vector3(canvas.x, canvas.y, 0f));
        }
    }

    private static void AppendSegmentPoints(
        List<Vector2> result,
        PsdLayerPreviewRenderer.VolumeCurve curve,
        int pointIndex,
        PsdLayerPreviewRenderer.VolumeCurvePoint a)
    {
        var steps = a.outType == PsdLayerPreviewRenderer.VolumeCurvePointType.Linear ? 1 : 16;
        for (var step = 0; step <= steps; step++)
        {
            if (result.Count > 0 && step == 0)
                continue;

            var t = step / (float)steps;
            result.Add(EvaluateSegment(curve, pointIndex, t));
        }
    }

    private static Vector2 EvaluateCatmull(IReadOnlyList<PsdLayerPreviewRenderer.VolumeCurvePoint> points, int index, float t, bool closed)
    {
        var p0 = GetCurvePoint(points, index - 1, closed);
        var p1 = GetCurvePoint(points, index, closed);
        var p2 = GetCurvePoint(points, index + 1, closed);
        var p3 = GetCurvePoint(points, index + 2, closed);
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static Vector2 EvaluateQuadraticBezier(IReadOnlyList<PsdLayerPreviewRenderer.VolumeCurvePoint> points, int index, float t, bool closed)
    {
        var a = GetCurvePoint(points, index, closed);
        var b = GetCurvePoint(points, index + 1, closed);
        var control = (a + b) * 0.5f;
        if (points.Count > 2)
            control = (control + GetCurvePoint(points, index - 1, closed) * 0.25f + GetCurvePoint(points, index + 2, closed) * 0.25f) * 0.6666667f;

        var oneMinusT = 1f - t;
        return oneMinusT * oneMinusT * a + 2f * oneMinusT * t * control + t * t * b;
    }

    private static Vector2 GetCurvePoint(IReadOnlyList<PsdLayerPreviewRenderer.VolumeCurvePoint> points, int index, bool closed)
    {
        if (points == null || points.Count == 0)
            return Vector2.zero;

        if (closed)
        {
            var wrapped = index % points.Count;
            if (wrapped < 0)
                wrapped += points.Count;
            return points[wrapped] != null ? points[wrapped].positionDoc : Vector2.zero;
        }

        index = Mathf.Clamp(index, 0, points.Count - 1);
        return points[index] != null ? points[index].positionDoc : Vector2.zero;
    }

    private static float DistancePointToSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
    {
        var t = ClosestSegmentT(point, a, b);
        var closest = Vector2.Lerp(a, b, t);
        return (point - closest).sqrMagnitude;
    }

    private static float ClosestSegmentT(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSqr = ab.sqrMagnitude;
        if (lengthSqr <= 0.0001f)
            return 0f;

        return Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
    }
}
#endif
