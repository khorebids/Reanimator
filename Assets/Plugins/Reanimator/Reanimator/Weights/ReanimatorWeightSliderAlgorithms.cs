#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ReanimatorWeightSliderAlgorithms
{
    public static BoneWeight SanitizeBoneWeight(
        BoneWeight source,
        int spriteBoneCount,
        bool normalizePositiveWeights,
        bool preserveZeroWeightIdentities)
    {
        if (spriteBoneCount <= 0)
            return default;

        var weightsByBone = BuildWeightBrushWeightDictionary(source, spriteBoneCount);
        var positiveSum = 0.0;
        foreach (var pair in weightsByBone)
            positiveSum += Math.Max(0.0, pair.Value);

        if (positiveSum > 0.0001)
        {
            if (normalizePositiveWeights || positiveSum > 1.0001)
            {
                var inv = 1.0 / positiveSum;
                var keys = new List<int>(weightsByBone.Keys);
                for (var i = 0; i < keys.Count; i++)
                    weightsByBone[keys[i]] = Mathf.Clamp01((float)(weightsByBone[keys[i]] * inv));
            }
            else
            {
                var keys = new List<int>(weightsByBone.Keys);
                for (var i = 0; i < keys.Count; i++)
                    weightsByBone[keys[i]] = Mathf.Clamp01(weightsByBone[keys[i]]);
            }
        }

        var ordered = new List<KeyValuePair<int, float>>(weightsByBone);
        ordered.RemoveAll(pair => pair.Key < 0 || pair.Key >= spriteBoneCount || !IsFinite(pair.Value) || pair.Value <= 0.0001f);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

        var indices = new[] { 0, 0, 0, 0 };
        var weights = new[] { 0f, 0f, 0f, 0f };
        var fallback = ordered.Count > 0 ? ordered[0].Key : FindFirstValidWeightBone(
            new[] { source.boneIndex0, source.boneIndex1, source.boneIndex2, source.boneIndex3 },
            spriteBoneCount);
        if (fallback < 0)
            fallback = 0;

        for (var i = 0; i < indices.Length; i++)
            indices[i] = fallback;

        var write = 0;
        if (preserveZeroWeightIdentities &&
            source.boneIndex0 == 0 &&
            IsFinite(source.weight0) &&
            source.weight0 <= 0.0001f &&
            !weightsByBone.ContainsKey(0))
        {
            indices[write] = 0;
            weights[write] = 0f;
            write++;
        }

        for (var orderedIndex = 0; orderedIndex < ordered.Count && write < 4; orderedIndex++)
        {
            var pair = ordered[orderedIndex];
            var duplicate = false;
            for (var i = 0; i < write; i++)
            {
                if (indices[i] != pair.Key)
                    continue;

                duplicate = true;
                break;
            }
            if (duplicate)
                continue;

            indices[write] = pair.Key;
            weights[write] = Mathf.Clamp01(pair.Value);
            write++;
        }

        if (preserveZeroWeightIdentities && write < 4)
        {
            TryAppendZeroWeightIdentity(0, source.boneIndex0, source.weight0, spriteBoneCount, indices, weights, ref write);
            TryAppendZeroWeightIdentity(1, source.boneIndex1, source.weight1, spriteBoneCount, indices, weights, ref write);
            TryAppendZeroWeightIdentity(2, source.boneIndex2, source.weight2, spriteBoneCount, indices, weights, ref write);
            TryAppendZeroWeightIdentity(3, source.boneIndex3, source.weight3, spriteBoneCount, indices, weights, ref write);
        }

        return new BoneWeight
        {
            boneIndex0 = indices[0],
            boneIndex1 = indices[1],
            boneIndex2 = indices[2],
            boneIndex3 = indices[3],
            weight0 = weights[0],
            weight1 = weights[1],
            weight2 = weights[2],
            weight3 = weights[3]
        };
    }

    public static BoneWeight BuildBoneWeightFromExplicitSlots(
        IReadOnlyList<int> boneIndices,
        IReadOnlyList<float> weights,
        int spriteBoneCount,
        bool normalizePositiveWeights)
    {
        var explicitIndices = new[] { -1, -1, -1, -1 };
        var explicitWeights = new float[4];
        var count = Mathf.Min(4, Mathf.Min(
            boneIndices != null ? boneIndices.Count : 0,
            weights != null ? weights.Count : 0));
        var zeroBoneSlot = -1;
        var hasPositiveZeroBone = false;
        for (var slot = 0; slot < count; slot++)
        {
            if (boneIndices[slot] != 0 || !IsFinite(weights[slot]))
                continue;

            if (weights[slot] > 0.0001f)
                hasPositiveZeroBone = true;
            else if (zeroBoneSlot < 0)
                zeroBoneSlot = slot;
        }

        var write = 0;
        if (!hasPositiveZeroBone && zeroBoneSlot >= 0)
        {
            explicitIndices[write] = 0;
            explicitWeights[write] = 0f;
            write++;
        }

        for (var slot = 0; slot < count && write < 4; slot++)
        {
            if (slot == zeroBoneSlot && !hasPositiveZeroBone)
                continue;

            explicitIndices[write] = boneIndices[slot];
            explicitWeights[write] = weights[slot];
            write++;
        }

        var source = new BoneWeight
        {
            boneIndex0 = explicitIndices[0],
            weight0 = explicitWeights[0],
            boneIndex1 = explicitIndices[1],
            weight1 = explicitWeights[1],
            boneIndex2 = explicitIndices[2],
            weight2 = explicitWeights[2],
            boneIndex3 = explicitIndices[3],
            weight3 = explicitWeights[3]
        };
        return SanitizeBoneWeight(source, spriteBoneCount, normalizePositiveWeights, true);
    }

    public static void BuildStableSlotState(
        BoneWeight source,
        int spriteBoneCount,
        IReadOnlyList<int> preferredBoneIndices,
        out int[] boneIndices,
        out float[] weights)
    {
        boneIndices = new[] { -1, -1, -1, -1 };
        weights = new float[4];
        if (spriteBoneCount <= 0)
            return;

        var sanitized = SanitizeBoneWeight(source, spriteBoneCount, false, true);
        var sourceIndices = new[]
        {
            sanitized.boneIndex0,
            sanitized.boneIndex1,
            sanitized.boneIndex2,
            sanitized.boneIndex3
        };
        var sourceWeights = new[]
        {
            sanitized.weight0,
            sanitized.weight1,
            sanitized.weight2,
            sanitized.weight3
        };
        var sourceIdentities = new List<int>(4);
        var primaryBone = FindFirstValidWeightBone(sourceIndices, spriteBoneCount);
        for (var slot = 0; slot < sourceIndices.Length; slot++)
            TryAddSliderIdentityBone(slot, sourceIndices, sourceWeights, spriteBoneCount, primaryBone, true, sourceIdentities);

        var weightsByBone = BuildWeightBrushWeightDictionary(sanitized, spriteBoneCount);
        var used = new HashSet<int>();
        var preferredCount = Mathf.Min(boneIndices.Length, preferredBoneIndices != null ? preferredBoneIndices.Count : 0);
        for (var slot = 0; slot < preferredCount; slot++)
        {
            var bone = preferredBoneIndices[slot];
            if (!sourceIdentities.Contains(bone) || !used.Add(bone))
                continue;

            boneIndices[slot] = bone;
            weightsByBone.TryGetValue(bone, out weights[slot]);
        }

        for (var sourceSlot = 0; sourceSlot < sourceIdentities.Count; sourceSlot++)
        {
            var bone = sourceIdentities[sourceSlot];
            if (!used.Add(bone))
                continue;

            var targetSlot = Array.IndexOf(boneIndices, -1);
            if (targetSlot < 0)
                break;

            boneIndices[targetSlot] = bone;
            weightsByBone.TryGetValue(bone, out weights[targetSlot]);
        }
    }

    public static bool TryBuildDragWeights(Vector2[] points, int count, Vector2 mouseCanvas, float[] weights)
    {
        return TryBuildDragWeights(points, count, mouseCanvas, null, weights);
    }

    public static bool TryBuildDragWeights(
        Vector2[] points,
        int count,
        Vector2 mouseCanvas,
        IReadOnlyList<float> sourceWeights,
        float[] weights)
    {
        if (points == null || weights == null || count < 2 || count > 4 || weights.Length < 4)
            return false;

        var clampedCount = Mathf.Min(count, points.Length);
        var uniquePoints = new Vector2[4];
        var pointGroups = new int[4];
        var uniqueCount = 0;
        for (var i = 0; i < clampedCount; i++)
        {
            var group = -1;
            for (var uniqueIndex = 0; uniqueIndex < uniqueCount; uniqueIndex++)
            {
                if ((points[i] - uniquePoints[uniqueIndex]).sqrMagnitude >= 1f)
                    continue;

                group = uniqueIndex;
                break;
            }

            if (group < 0)
            {
                group = uniqueCount;
                uniquePoints[uniqueCount] = points[i];
                uniqueCount++;
            }
            pointGroups[i] = group;
        }
        if (uniqueCount < 2)
            return false;

        for (var i = 0; i < weights.Length; i++)
            weights[i] = 0f;

        var groupedWeights = new float[4];
        if (!TryBuildDistinctDragWeights(uniquePoints, uniqueCount, mouseCanvas, groupedWeights))
            return false;

        var groupSourceSums = new float[4];
        var groupMemberCounts = new int[4];
        for (var i = 0; i < clampedCount; i++)
        {
            var group = pointGroups[i];
            groupMemberCounts[group]++;
            var sourceWeight = sourceWeights != null && i < sourceWeights.Count && IsFinite(sourceWeights[i])
                ? Mathf.Max(0f, sourceWeights[i])
                : 0f;
            groupSourceSums[group] += sourceWeight;
        }

        for (var i = 0; i < clampedCount; i++)
        {
            var group = pointGroups[i];
            var sourceWeight = sourceWeights != null && i < sourceWeights.Count && IsFinite(sourceWeights[i])
                ? Mathf.Max(0f, sourceWeights[i])
                : 0f;
            var share = groupSourceSums[group] > 0.0001f
                ? sourceWeight / groupSourceSums[group]
                : 1f / Mathf.Max(1, groupMemberCounts[group]);
            weights[i] = groupedWeights[group] * share;
        }
        NormalizeWeights(weights, clampedCount);
        return true;
    }

    private static bool TryBuildDistinctDragWeights(Vector2[] points, int count, Vector2 mouseCanvas, float[] weights)
    {
        if (count == 2)
        {
            ClosestPointOnSegment(mouseCanvas, points[0], points[1], out var t);
            weights[0] = 1f - t;
            weights[1] = t;
            return true;
        }

        if (count == 3)
        {
            var point = ClampPointToTriangle(mouseCanvas, points[0], points[1], points[2]);
            if (!TryGetBarycentric(point, points[0], points[1], points[2], out weights[0], out weights[1], out weights[2]))
                return TryBuildCollinearDragWeights(points, count, mouseCanvas, weights);
            NormalizeWeights(weights, count);
            return true;
        }

        var order = BuildPolygonOrder(points, count);
        var polygon = new Vector2[count];
        for (var i = 0; i < count; i++)
            polygon[i] = points[order[i]];

        var clampedPoint = IsPointInsidePolygon(mouseCanvas, polygon, count)
            ? mouseCanvas
            : ClosestPointOnPolygon(mouseCanvas, polygon, count);

        var orderedWeights = new float[4];
        if (!TryBuildMeanValueWeights(polygon, count, clampedPoint, orderedWeights))
            return TryBuildCollinearDragWeights(points, count, mouseCanvas, weights);

        for (var i = 0; i < count; i++)
            weights[order[i]] = orderedWeights[i];
        NormalizeWeights(weights, count);
        return true;
    }

    private static bool TryBuildCollinearDragWeights(Vector2[] points, int count, Vector2 mouseCanvas, float[] weights)
    {
        var first = 0;
        var last = 1;
        var maxDistanceSqr = 0f;
        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                var distanceSqr = (points[i] - points[j]).sqrMagnitude;
                if (distanceSqr <= maxDistanceSqr)
                    continue;

                maxDistanceSqr = distanceSqr;
                first = i;
                last = j;
            }
        }
        if (maxDistanceSqr < 1f)
            return false;

        var axis = points[last] - points[first];
        var axisLengthSqr = axis.sqrMagnitude;
        var order = new int[count];
        var projections = new float[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
            projections[i] = Vector2.Dot(points[i] - points[first], axis) / axisLengthSqr;
        }
        Array.Sort(order, (a, b) => projections[a].CompareTo(projections[b]));

        var mouseProjection = Mathf.Clamp01(Vector2.Dot(mouseCanvas - points[first], axis) / axisLengthSqr);
        for (var i = 0; i < count; i++)
            weights[i] = 0f;

        if (mouseProjection <= projections[order[0]])
        {
            weights[order[0]] = 1f;
            return true;
        }

        for (var i = 0; i + 1 < count; i++)
        {
            var a = order[i];
            var b = order[i + 1];
            if (mouseProjection > projections[b])
                continue;

            var range = projections[b] - projections[a];
            var t = range > 0.000001f ? (mouseProjection - projections[a]) / range : 0f;
            weights[a] = 1f - Mathf.Clamp01(t);
            weights[b] = Mathf.Clamp01(t);
            return true;
        }

        weights[order[count - 1]] = 1f;
        return true;
    }

    public static bool TryBuildDragWeights(
        Vector2[] points,
        int count,
        Vector2 mouseCanvas,
        Vector2 pointerOffsetCanvas,
        float[] weights)
    {
        return TryBuildDragWeights(points, count, mouseCanvas - pointerOffsetCanvas, weights);
    }

    public static bool TryBuildDragWeights(
        Vector2[] points,
        int count,
        Vector2 mouseCanvas,
        Vector2 pointerOffsetCanvas,
        IReadOnlyList<float> sourceWeights,
        float[] weights)
    {
        return TryBuildDragWeights(points, count, mouseCanvas - pointerOffsetCanvas, sourceWeights, weights);
    }

    public static Vector2 ClampPointToTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        if (TryGetBarycentric(point, a, b, c, out var wa, out var wb, out var wc) &&
            wa >= 0f && wb >= 0f && wc >= 0f)
            return point;

        var ab = ClosestPointOnSegment(point, a, b, out _);
        var bc = ClosestPointOnSegment(point, b, c, out _);
        var ca = ClosestPointOnSegment(point, c, a, out _);
        var best = ab;
        var bestDistance = (point - ab).sqrMagnitude;
        var distance = (point - bc).sqrMagnitude;
        if (distance < bestDistance)
        {
            best = bc;
            bestDistance = distance;
        }

        distance = (point - ca).sqrMagnitude;
        if (distance < bestDistance)
            best = ca;
        return best;
    }

    public static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b, out float t)
    {
        var ab = b - a;
        t = ab.sqrMagnitude > 0.000001f ? Vector2.Dot(point - a, ab) / ab.sqrMagnitude : 0f;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    public static int[] BuildPolygonOrder(Vector2[] points, int count)
    {
        var clampedCount = Mathf.Clamp(count, 0, points != null ? points.Length : 0);
        var order = new int[clampedCount];
        var center = Vector2.zero;
        for (var i = 0; i < clampedCount; i++)
        {
            order[i] = i;
            center += points[i];
        }

        center /= Mathf.Max(1, clampedCount);
        Array.Sort(order, (a, b) =>
        {
            var angleA = Mathf.Atan2(points[a].y - center.y, points[a].x - center.x);
            var angleB = Mathf.Atan2(points[b].y - center.y, points[b].x - center.x);
            return angleA.CompareTo(angleB);
        });
        return order;
    }

    public static bool IsPointInsidePolygon(Vector2 point, Vector2[] polygon, int count)
    {
        if (polygon == null || count < 3)
            return false;

        var clampedCount = Mathf.Min(count, polygon.Length);
        var inside = false;
        for (int i = 0, j = clampedCount - 1; i < clampedCount; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if (DistancePointToSegment(point, a, b) <= 0.001f)
                return true;

            var crosses = (a.y > point.y) != (b.y > point.y);
            if (crosses)
            {
                var x = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (point.x < x)
                    inside = !inside;
            }
        }

        return inside;
    }

    public static Vector2 ClosestPointOnPolygon(Vector2 point, Vector2[] polygon, int count)
    {
        if (polygon == null || polygon.Length == 0 || count <= 0)
            return Vector2.zero;

        var clampedCount = Mathf.Min(count, polygon.Length);
        var best = polygon[0];
        var bestDistance = float.MaxValue;
        for (var i = 0; i < clampedCount; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % clampedCount];
            var candidate = ClosestPointOnSegment(point, a, b, out _);
            var distance = (point - candidate).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    public static bool TryBuildMeanValueWeights(Vector2[] polygon, int count, Vector2 point, float[] weights)
    {
        if (polygon == null || weights == null || count < 3 || weights.Length < count)
            return false;

        const float epsilon = 0.0001f;
        var clampedCount = Mathf.Min(count, polygon.Length);
        for (var i = 0; i < clampedCount; i++)
        {
            weights[i] = 0f;
            var toVertex = polygon[i] - point;
            var distance = toVertex.magnitude;
            if (distance <= epsilon)
            {
                weights[i] = 1f;
                return true;
            }
        }

        for (var i = 0; i < clampedCount; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % clampedCount];
            var closest = ClosestPointOnSegment(point, a, b, out var t);
            if ((point - closest).sqrMagnitude > epsilon * epsilon)
                continue;

            weights[i] = 1f - t;
            weights[(i + 1) % clampedCount] = t;
            return true;
        }

        var sum = 0f;
        for (var i = 0; i < clampedCount; i++)
        {
            var prev = polygon[(i + clampedCount - 1) % clampedCount] - point;
            var current = polygon[i] - point;
            var next = polygon[(i + 1) % clampedCount] - point;
            var prevAngle = Mathf.Abs(Mathf.Atan2(Cross2D(prev, current), Vector2.Dot(prev, current)));
            var nextAngle = Mathf.Abs(Mathf.Atan2(Cross2D(current, next), Vector2.Dot(current, next)));
            var weight = (Mathf.Tan(prevAngle * 0.5f) + Mathf.Tan(nextAngle * 0.5f)) / Mathf.Max(epsilon, current.magnitude);
            weights[i] = Mathf.Max(0f, weight);
            sum += weights[i];
        }

        return sum > 0.0001f;
    }

    public static void NormalizeWeights(float[] weights, int count)
    {
        if (weights == null)
            return;

        var clampedCount = Mathf.Min(count, weights.Length);
        var sum = 0f;
        for (var i = 0; i < clampedCount; i++)
        {
            weights[i] = Mathf.Max(0f, weights[i]);
            sum += weights[i];
        }

        if (sum <= 0.0001f)
            return;

        var inv = 1f / sum;
        for (var i = 0; i < clampedCount; i++)
            weights[i] *= inv;
    }

    public static List<int> BuildActiveWeightBoneList(
        BoneWeight weight,
        IReadOnlyList<int> spriteBoneToRendererBone,
        bool includeZeroWeightIdentities = true)
    {
        var result = new List<int>(4);
        var spriteBoneCount = spriteBoneToRendererBone != null ? spriteBoneToRendererBone.Count : 0;
        var indices = new[] { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
        var weights = new[] { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
        var primaryBone = FindFirstValidWeightBone(indices, spriteBoneCount);
        for (var slot = 0; slot < indices.Length; slot++)
            TryAddSliderIdentityBone(slot, indices, weights, spriteBoneCount, primaryBone, includeZeroWeightIdentities, result);
        return result;
    }

    public static bool TryBuildSmoothedBoneWeight(
        BoneWeight[] sourceWeights,
        int vertexIndex,
        int[] edgePairs,
        int[] triangles,
        int spriteBoneCount,
        float strength,
        out BoneWeight boneWeight)
    {
        boneWeight = default;
        if (sourceWeights == null || vertexIndex < 0 || vertexIndex >= sourceWeights.Length || spriteBoneCount <= 0)
            return false;

        var neighbors = new HashSet<int>();
        AddEdgeNeighbors(edgePairs, vertexIndex, sourceWeights.Length, neighbors);
        if (neighbors.Count == 0)
            AddTriangleNeighbors(triangles, vertexIndex, sourceWeights.Length, neighbors);
        if (neighbors.Count == 0)
            return false;

        var current = BuildWeightBrushWeightDictionary(sourceWeights[vertexIndex], spriteBoneCount);
        var average = new Dictionary<int, float>();
        foreach (var neighbor in neighbors)
        {
            if (neighbor < 0 || neighbor >= sourceWeights.Length)
                continue;

            AccumulateWeightBrushBone(average, sourceWeights[neighbor].boneIndex0, sourceWeights[neighbor].weight0, spriteBoneCount);
            AccumulateWeightBrushBone(average, sourceWeights[neighbor].boneIndex1, sourceWeights[neighbor].weight1, spriteBoneCount);
            AccumulateWeightBrushBone(average, sourceWeights[neighbor].boneIndex2, sourceWeights[neighbor].weight2, spriteBoneCount);
            AccumulateWeightBrushBone(average, sourceWeights[neighbor].boneIndex3, sourceWeights[neighbor].weight3, spriteBoneCount);
        }

        if (average.Count == 0)
            return false;

        var averageKeys = new List<int>(average.Keys);
        var invNeighborCount = 1f / Mathf.Max(1, neighbors.Count);
        for (var i = 0; i < averageKeys.Count; i++)
            average[averageKeys[i]] *= invNeighborCount;

        var mixed = new Dictionary<int, float>();
        var keys = new HashSet<int>(current.Keys);
        foreach (var key in average.Keys)
            keys.Add(key);

        var t = Mathf.Clamp01(strength);
        foreach (var key in keys)
        {
            current.TryGetValue(key, out var from);
            average.TryGetValue(key, out var to);
            var value = Mathf.Lerp(from, to, t);
            if (value > 0.0001f)
                mixed[key] = value;
        }

        NormalizeWeightDictionary(mixed);
        if (mixed.Count == 0)
            return false;

        boneWeight = BuildBoneWeightFromDictionary(mixed, spriteBoneCount);
        return true;
    }

    public static Dictionary<int, float> BuildWeightBrushWeightDictionary(BoneWeight weight, int spriteBoneCount)
    {
        var result = new Dictionary<int, float>();
        AccumulateWeightBrushBone(result, weight.boneIndex0, weight.weight0, spriteBoneCount);
        AccumulateWeightBrushBone(result, weight.boneIndex1, weight.weight1, spriteBoneCount);
        AccumulateWeightBrushBone(result, weight.boneIndex2, weight.weight2, spriteBoneCount);
        AccumulateWeightBrushBone(result, weight.boneIndex3, weight.weight3, spriteBoneCount);
        return result;
    }

    public static BoneWeight RemoveBoneInfluenceAndRemap(
        BoneWeight source,
        int removedSpriteBoneIndex,
        int originalSpriteBoneCount)
    {
        if (originalSpriteBoneCount <= 0 ||
            removedSpriteBoneIndex < 0 ||
            removedSpriteBoneIndex >= originalSpriteBoneCount)
            return source;

        var remainingSpriteBoneCount = originalSpriteBoneCount - 1;
        if (remainingSpriteBoneCount <= 0)
            return default;

        var sourceWeights = BuildWeightBrushWeightDictionary(source, originalSpriteBoneCount);
        var remappedWeights = new Dictionary<int, float>();
        foreach (var pair in sourceWeights)
        {
            if (pair.Key == removedSpriteBoneIndex)
                continue;

            var remappedBoneIndex = pair.Key > removedSpriteBoneIndex ? pair.Key - 1 : pair.Key;
            AccumulateWeightBrushBone(remappedWeights, remappedBoneIndex, pair.Value, remainingSpriteBoneCount);
        }

        NormalizeWeightDictionary(remappedWeights);
        return remappedWeights.Count > 0
            ? BuildBoneWeightFromDictionary(remappedWeights, remainingSpriteBoneCount)
            : default;
    }

    private static int FindFirstValidWeightBone(IReadOnlyList<int> indices, int spriteBoneCount)
    {
        if (indices == null)
            return -1;

        for (var i = 0; i < indices.Count; i++)
        {
            var index = indices[i];
            if (index >= 0 && index < spriteBoneCount)
                return index;
        }

        return -1;
    }

    private static void TryAddSliderIdentityBone(
        int slot,
        IReadOnlyList<int> indices,
        IReadOnlyList<float> weights,
        int spriteBoneCount,
        int primaryBone,
        bool includeZeroWeightIdentities,
        List<int> result)
    {
        if (result == null || result.Count >= 4 || indices == null || weights == null)
            return;

        var boneIndex = indices[slot];
        if (boneIndex < 0 || boneIndex >= spriteBoneCount || result.Contains(boneIndex))
            return;

        var weight = Mathf.Clamp01(weights[slot]);
        if (weight > 0.0001f ||
            (includeZeroWeightIdentities && IsZeroWeightSliderIdentitySlot(slot, boneIndex, primaryBone)))
            result.Add(boneIndex);
    }

    private static bool IsZeroWeightSliderIdentitySlot(int slot, int boneIndex, int primaryBone)
    {
        if (slot == 0)
            return true;

        if (boneIndex == primaryBone)
            return false;

        // Unity's unused BoneWeight slots often read back as bone 0, weight 0.
        // Preserve explicit zero influences, but do not invent bone 0 from those trailing defaults.
        return boneIndex != 0;
    }

    private static void TryAppendZeroWeightIdentity(
        int slot,
        int boneIndex,
        float weight,
        int spriteBoneCount,
        int[] indices,
        float[] weights,
        ref int write)
    {
        if (write >= 4 || boneIndex < 0 || boneIndex >= spriteBoneCount || !IsFinite(weight) || weight > 0.0001f)
            return;
        if (slot > 0 && boneIndex == 0)
            return;

        for (var i = 0; i < indices.Length; i++)
        {
            if (indices[i] == boneIndex && weights[i] > 0.0001f)
                return;
        }

        for (var i = 0; i < write; i++)
        {
            if (indices[i] == boneIndex)
                return;
        }

        indices[write] = boneIndex;
        weights[write] = 0f;
        write++;
    }

    private static void AddEdgeNeighbors(int[] edgePairs, int vertexIndex, int vertexCount, HashSet<int> neighbors)
    {
        if (edgePairs == null || neighbors == null)
            return;

        for (var i = 0; i + 1 < edgePairs.Length; i += 2)
        {
            var a = edgePairs[i];
            var b = edgePairs[i + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
                continue;

            if (a == vertexIndex)
                neighbors.Add(b);
            else if (b == vertexIndex)
                neighbors.Add(a);
        }
    }

    private static void AddTriangleNeighbors(int[] triangles, int vertexIndex, int vertexCount, HashSet<int> neighbors)
    {
        if (triangles == null || neighbors == null)
            return;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                continue;

            if (a == vertexIndex)
            {
                neighbors.Add(b);
                neighbors.Add(c);
            }
            else if (b == vertexIndex)
            {
                neighbors.Add(a);
                neighbors.Add(c);
            }
            else if (c == vertexIndex)
            {
                neighbors.Add(a);
                neighbors.Add(b);
            }
        }
    }

    private static BoneWeight BuildBoneWeightFromDictionary(Dictionary<int, float> weightsByBone, int spriteBoneCount)
    {
        var ordered = new List<KeyValuePair<int, float>>(weightsByBone);
        ordered.RemoveAll(pair => pair.Key < 0 || pair.Key >= spriteBoneCount || !IsFinite(pair.Value) || pair.Value <= 0.0001f);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

        var indices = new[] { 0, 0, 0, 0 };
        var weights = new[] { 0f, 0f, 0f, 0f };
        var fallback = ordered.Count > 0 ? ordered[0].Key : 0;
        for (var i = 0; i < indices.Length; i++)
            indices[i] = fallback;

        for (var i = 0; i < ordered.Count && i < 4; i++)
        {
            indices[i] = ordered[i].Key;
            weights[i] = Mathf.Clamp01(ordered[i].Value);
        }

        return new BoneWeight
        {
            boneIndex0 = indices[0],
            boneIndex1 = indices[1],
            boneIndex2 = indices[2],
            boneIndex3 = indices[3],
            weight0 = weights[0],
            weight1 = weights[1],
            weight2 = weights[2],
            weight3 = weights[3]
        };
    }

    private static void NormalizeWeightDictionary(Dictionary<int, float> weightsByBone)
    {
        if (weightsByBone == null || weightsByBone.Count == 0)
            return;

        var sum = 0.0;
        foreach (var pair in weightsByBone)
        {
            if (!IsFinite(pair.Value))
                continue;
            sum += Math.Max(0.0, pair.Value);
        }

        if (sum <= 0.0001)
        {
            weightsByBone.Clear();
            return;
        }

        var keys = new List<int>(weightsByBone.Keys);
        var inv = 1.0 / sum;
        for (var i = 0; i < keys.Count; i++)
            weightsByBone[keys[i]] = IsFinite(weightsByBone[keys[i]])
                ? Mathf.Clamp01((float)(weightsByBone[keys[i]] * inv))
                : 0f;
    }

    private static void AccumulateWeightBrushBone(Dictionary<int, float> weightsByBone, int bone, float weight, int spriteBoneCount)
    {
        if (weightsByBone == null || bone < 0 || bone >= spriteBoneCount || !IsFinite(weight) || weight <= 0.0001f)
            return;

        if (weightsByBone.TryGetValue(bone, out var existing))
            weightsByBone[bone] = (float)Math.Min(float.MaxValue, (double)existing + weight);
        else
            weightsByBone.Add(bone, weight);
    }

    private static bool TryGetBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float wa, out float wb, out float wc)
    {
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var d00 = Vector2.Dot(v0, v0);
        var d01 = Vector2.Dot(v0, v1);
        var d11 = Vector2.Dot(v1, v1);
        var d20 = Vector2.Dot(v2, v0);
        var d21 = Vector2.Dot(v2, v1);
        var denom = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denom) < 1e-6f)
        {
            wa = wb = wc = 0f;
            return false;
        }

        wb = (d11 * d20 - d01 * d21) / denom;
        wc = (d00 * d21 - d01 * d20) / denom;
        wa = 1f - wb - wc;
        return true;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var denom = ab.sqrMagnitude;
        if (denom <= 1e-8f)
            return Vector2.Distance(p, a);

        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
        return Vector2.Distance(p, a + ab * t);
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

internal static class ReanimatorWeightBrushAlgorithms
{
    public static BoneWeight CapInfluences(BoneWeight source, int spriteBoneCount, int maxInfluenceCount)
    {
        if (spriteBoneCount <= 0)
            return default;

        var ordered = new List<KeyValuePair<int, float>>(
            ReanimatorWeightSliderAlgorithms.BuildWeightBrushWeightDictionary(source, spriteBoneCount));
        ordered.RemoveAll(pair => pair.Value <= 0.0001f);
        ordered.Sort((a, b) =>
        {
            var weightOrder = b.Value.CompareTo(a.Value);
            return weightOrder != 0 ? weightOrder : a.Key.CompareTo(b.Key);
        });

        var cap = Mathf.Clamp(maxInfluenceCount, 1, 3);
        if (ordered.Count <= cap)
            return source;

        var removedWeight = 0f;
        for (var i = cap; i < ordered.Count; i++)
            removedWeight += ordered[i].Value;

        var sharedWeight = removedWeight / cap;
        var fallbackBone = ordered[0].Key;
        var indices = new[] { fallbackBone, fallbackBone, fallbackBone, fallbackBone };
        var weights = new float[4];
        for (var i = 0; i < cap; i++)
        {
            indices[i] = ordered[i].Key;
            weights[i] = Mathf.Clamp01(ordered[i].Value + sharedWeight);
        }

        return new BoneWeight
        {
            boneIndex0 = indices[0],
            boneIndex1 = indices[1],
            boneIndex2 = indices[2],
            boneIndex3 = indices[3],
            weight0 = weights[0],
            weight1 = weights[1],
            weight2 = weights[2],
            weight3 = weights[3]
        };
    }

    public static int ResolveSpriteBoneIndex(
        int requestedSpriteBoneIndex,
        int selectedRendererBoneIndex,
        IReadOnlyList<int> spriteBoneToRendererBone)
    {
        if (spriteBoneToRendererBone == null || spriteBoneToRendererBone.Count == 0)
            return -1;

        if (requestedSpriteBoneIndex >= 0 && requestedSpriteBoneIndex < spriteBoneToRendererBone.Count)
            return spriteBoneToRendererBone[requestedSpriteBoneIndex] >= 0
                ? requestedSpriteBoneIndex
                : -1;

        if (selectedRendererBoneIndex >= 0)
        {
            for (var i = 0; i < spriteBoneToRendererBone.Count; i++)
            {
                if (spriteBoneToRendererBone[i] == selectedRendererBoneIndex)
                    return i;
            }
        }

        return -1;
    }
}
#endif
