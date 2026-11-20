#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const int ElbowSampleCount = 8;
    private const string CorrectiveElbowCurvePrefsKey = "Reanimator.ElbowCorrective.WeightCurve";

    [Serializable]
    private sealed class CorrectiveElbowCurvePreference
    {
        public AnimationCurve curve;
    }

    [NonSerialized] private AnimationCurve correctiveElbowWeightCurve;
    internal AnimationCurve CorrectiveElbowWeightCurve
    {
        get => correctiveElbowWeightCurve ??= LoadCorrectiveElbowWeightCurve();
        set => correctiveElbowWeightCurve = value;
    }

    private static AnimationCurve LoadCorrectiveElbowWeightCurve()
    {
        var json = EditorPrefs.GetString(CorrectiveElbowCurvePrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var preference = JsonUtility.FromJson<CorrectiveElbowCurvePreference>(json);
                if (preference?.curve != null && preference.curve.length > 0)
                    return preference.curve;
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning("Could not restore the elbow weight curve: " + exception.Message);
            }
        }
        return CreateDefaultCorrectiveElbowWeightCurve();
    }

    private static AnimationCurve CreateDefaultCorrectiveElbowWeightCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0.75f, -1.77533424f, -1.77533424f, 0f, 0.09784681f) { weightedMode = WeightedMode.None },
            new Keyframe(0.33629197f, 0.26103503f, -0.6748671f, -0.6748671f, 0.33333334f, 0.09297404f) { weightedMode = WeightedMode.None },
            new Keyframe(1f, 0f, -0.011242f, -0.011242f, 0.17705911f, 0f) { weightedMode = WeightedMode.None })
        {
            preWrapMode = WrapMode.ClampForever,
            postWrapMode = WrapMode.ClampForever
        };
    }

    internal void SaveCorrectiveElbowWeightCurve()
    {
        if (CorrectiveElbowWeightCurve == null || CorrectiveElbowWeightCurve.length == 0)
            return;
        var preference = new CorrectiveElbowCurvePreference { curve = CorrectiveElbowWeightCurve };
        EditorPrefs.SetString(CorrectiveElbowCurvePrefsKey, JsonUtility.ToJson(preference));
    }
    internal float CorrectiveElbowDepth = 0.15f;
    internal float CorrectiveElbowShift = 0.15f;

    private sealed class AutomaticElbowPlan
    {
        public LayerVertexData data;
        public int upper, lower;
        public Vector2 joint;
        public Vector2[][] contour = new Vector2[2][];
        public Vector2[][] inner = new Vector2[2][];
        public Vector2[][] parentContour = new Vector2[2][];
        public Vector2[][] parentInner = new Vector2[2][];
        public List<int>[] parentOld = { new(), new() };
        public float width;
        public readonly Vector2[] middle = new Vector2[ElbowSampleCount];
        public readonly bool[] middleInside = new bool[ElbowSampleCount];
        public readonly HashSet<int> obsoleteContour = new();
    }

    private bool TryBuildAutomaticElbowPlan(out AutomaticElbowPlan plan, out string message)
    {
        plan = null;
        message = "Select the sprite and the child bone of the elbow.";
        if (!TryGetBoneParent(selectedBoneIndex, out var child, out var parentIndex, out var parent) ||
            !TryGetSelectedLayer(out var layer) || !TryGetLayerVertexData(layer, out var data) ||
            data?.triangles == null || data.boneWeights == null)
            return false;
        var vertices = GetLayerVerticesForDraw(data);
        var incoming = (Vector2)(parent.worldRotation * Vector3.right);
        var outgoing = (Vector2)(child.worldRotation * Vector3.right);
        var joint = GetBoneBasePoint(child);
        if (!TryGetLimbCrossSection(vertices, data.triangles, data.boundaryEdgePairs, joint,
                incoming, outgoing, out var a, out var b) ||
            !TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertices.Length, out var loop))
        {
            message = "Could not find both contour intersections at the joint.";
            return false;
        }
        var width = Vector2.Distance(a, b);
        if (width < 0.01f)
            return false;
        var result = new AutomaticElbowPlan
        {
            data = data, upper = parentIndex, lower = selectedBoneIndex, joint = joint, width = width
        };
        for (var side = 0; side < 2; side++)
        {
            var obsolete = new List<int>();
            if (!TrySampleElbowContour(vertices, loop, side == 0 ? a : b, outgoing, width, out var samples, obsolete))
            {
                message = "The contour is too short in the child bone direction.";
                return false;
            }
            var inner = new Vector2[samples.Length];
            for (var i = 0; i < samples.Length; i++)
                if (!TryGetElbowInnerPoint(data, vertices, loop, samples[i], width * CorrectiveElbowDepth, out inner[i]))
                {
                    message = "Could not place the inner row. Reduce Inner Depth.";
                    return false;
                }
            result.obsoleteContour.UnionWith(obsolete);
            result.contour[side] = samples;
            result.inner[side] = inner;
            float parentReach = Mathf.Min(width, Vector2.Distance(joint, GetBoneBasePoint(parent)) * .5f);
            if (!TrySampleElbowContour(vertices, loop, side == 0 ? a : b, -incoming, parentReach,
                    out var parentSamples, result.parentOld[side]))
            { message = "Could not sample the parent-side elbow transition."; return false; }
            var parentInner = new Vector2[parentSamples.Length];
            for (int i = 0; i < parentSamples.Length; i++)
                if (!TryGetElbowInnerPoint(data, vertices, loop, parentSamples[i], width * CorrectiveElbowDepth, out parentInner[i]))
                { message = "Could not place the parent-side elbow inner row."; return false; }
            result.parentContour[side] = parentSamples;
            result.parentInner[side] = parentInner;
        }
        for (var i = 0; i < result.middle.Length; i++)
        {
            result.middle[i] = (result.contour[0][i] + result.contour[1][i]) * 0.5f;
            result.middleInside[i] = TryFindContainingTriangle(data.triangles, vertices, result.middle[i],
                out _, out _, out _, out _, out _, out _, out _, out _);
        }
        plan = result;
        message = null;
        return true;
    }

    internal static bool TrySampleElbowContour(Vector2[] vertices, IReadOnlyList<int> loop, Vector2 start,
        Vector2 childDirection, float distance, out Vector2[] samples, List<int> obsoleteVertices = null,
        int sampleCount = ElbowSampleCount, bool linear = false)
    {
        samples = null;
        if (loop == null || loop.Count < 3 || distance <= 0f)
            return false;
        var edgeIndex = -1;
        var best = float.PositiveInfinity;
        for (var i = 0; i < loop.Count; i++)
        {
            var candidate = DistancePointToSegment(start, vertices[loop[i]], vertices[loop[(i + 1) % loop.Count]]);
            if (candidate < best)
            {
                edgeIndex = i;
                best = candidate;
            }
        }
        if (edgeIndex < 0 || best > 0.1f)
            return false;
        var forwardOld = new List<int>();
        var backwardOld = new List<int>();
        var forward = Walk(1, forwardOld);
        var backward = Walk(-1, backwardOld);
        var forwardScore = Score(forward);
        var backwardScore = Score(backward);
        var useForward = forwardScore >= backwardScore;
        samples = useForward ? forward : backward;
        if (samples != null)
            obsoleteVertices?.AddRange(useForward ? forwardOld : backwardOld);
        return samples != null;

        float Score(Vector2[] points)
        {
            if (points == null)
                return float.NegativeInfinity;
            var score = 0f;
            for (var i = 1; i < points.Length; i++)
                score += Vector2.Dot(points[i] - start, childDirection);
            return score;
        }

        Vector2[] Walk(int direction, List<int> obsolete)
        {
            var points = new Vector2[sampleCount];
            points[0] = start;
            var nextIndex = direction > 0 ? (edgeIndex + 1) % loop.Count : edgeIndex;
            var current = start;
            var accumulated = 0f;
            var sample = 1;
            for (var count = 0; count < loop.Count && sample < points.Length; count++)
            {
                var end = vertices[loop[nextIndex]];
                var length = Vector2.Distance(current, end);
                while (sample < points.Length && distance * SampleDistance(sample) <= accumulated + length + 0.0001f)
                {
                    var t = length > 1e-6f ? (distance * SampleDistance(sample) - accumulated) / length : 0f;
                    points[sample++] = Vector2.Lerp(current, end, t);
                }
                accumulated += length;
                if (accumulated > 0.01f && accumulated < distance - 0.01f)
                    obsolete.Add(loop[nextIndex]);
                current = end;
                nextIndex = (nextIndex + direction + loop.Count) % loop.Count;
            }
            return sample == points.Length ? points : null;
        }
        float SampleDistance(int sample) => linear ? sample / (float)(sampleCount - 1) : ElbowSampleDistance(sample);
    }

    private bool TryGetElbowInnerPoint(LayerVertexData data, Vector2[] vertices, IReadOnlyList<int> loop,
        Vector2 point, float requestedDepth, out Vector2 inner)
    {
        inner = point;
        var normal = Vector2.zero;
        double area = 0d;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = vertices[loop[i]];
            var b = vertices[loop[(i + 1) % loop.Count]];
            area += (double)a.x * b.y - (double)a.y * b.x;
        }
        for (var i = 0; i < loop.Count; i++)
        {
            var a = vertices[loop[i]];
            var b = vertices[loop[(i + 1) % loop.Count]];
            if (DistancePointToSegment(point, a, b) > 0.01f)
                continue;
            var edge = (b - a).normalized;
            normal += new Vector2(-edge.y, edge.x) * (area >= 0d ? 1f : -1f);
        }
        if (normal.sqrMagnitude < 1e-6f)
            return false;
        normal.Normalize();
        var origin = point + normal * Mathf.Min(0.05f, requestedDepth * 0.01f);
        if (!TryGetNearestContourRayIntersection(vertices, data.boundaryEdgePairs, origin,
                Mathf.Atan2(normal.y, normal.x), out var opposite))
            return false;
        var depth = Mathf.Min(requestedDepth, Vector2.Distance(point, opposite) * 0.4f);
        inner = point + normal * depth;
        return depth > 0.001f && IsPointInsidePolygonOrOnEdge(inner, vertices, loop);
    }

    private void DrawCorrectiveElbow(PsdCoordinateSpace coordinates, LayerInfo layer)
    {
        if (Event.current.type != EventType.Repaint || layer == null ||
            !TryBuildAutomaticElbowPlan(out var plan, out _))
            return;
        Handles.BeginGUI();
        for (var side = 0; side < 2; side++)
            using (new Handles.DrawingScope(side == 0 ? Color.cyan : new Color(1f, 0.4f, 0.7f)))
            {
                Handles.DrawAAPolyLine(4f, coordinates.DocumentToView(plan.joint),
                    coordinates.DocumentToView(plan.contour[side][0]));
                for (var i = 0; i < ElbowSampleCount; i++)
                {
                    Vector3 outer = coordinates.DocumentToView(plan.contour[side][i]);
                    Vector3 inner = coordinates.DocumentToView(plan.inner[side][i]);
                    Handles.DrawSolidDisc(outer, Vector3.forward, i == 0 ? 5f : 3.5f);
                    Handles.DrawSolidDisc(inner, Vector3.forward, 3f);
                    Handles.DrawAAPolyLine(1f, outer, inner);
                    if (i > 0)
                    {
                        Handles.DrawAAPolyLine(2f, coordinates.DocumentToView(plan.contour[side][i - 1]), outer);
                        Handles.DrawAAPolyLine(1f, coordinates.DocumentToView(plan.inner[side][i - 1]), inner);
                    }
                }
            }
        using (new Handles.DrawingScope(new Color(1f, 0.8f, 0.2f)))
            for (var i = 0; i < plan.middle.Length; i++)
            {
                if (!plan.middleInside[i])
                    continue;
                Vector3 point = coordinates.DocumentToView(plan.middle[i]);
                Handles.DrawSolidDisc(point, Vector3.forward, i == 0 ? 4f : 3f);
                Handles.DrawDottedLine(coordinates.DocumentToView(plan.contour[0][i]), point, 3f);
                Handles.DrawDottedLine(point, coordinates.DocumentToView(plan.contour[1][i]), 3f);
            }
        using (new Handles.DrawingScope(Color.red))
            Handles.DrawSolidDisc(coordinates.DocumentToView(plan.joint), Vector3.forward, 4f);
        Handles.EndGUI();
    }

    internal bool CanApplyAutomaticElbow(out string message)
    {
        using var preview = BeginPreviewBoneRigs();
        return TryBuildAutomaticElbowPlan(out _, out message);
    }

    internal bool ApplyCorrectiveElbow(out string message)
    {
        using var preview = BeginPreviewBoneRigs();
        if (!TryBuildAutomaticElbowPlan(out var plan, out message))
            return false;
        var data = plan.data;
        var sourceDraw = (Vector2[])GetLayerVerticesForDraw(data).Clone();
        var staged = CloneProjectorLayerData(data);
        var rows = new int[2, 2, ElbowSampleCount];
        var parentRows = new int[2, 2, ElbowSampleCount];
        var middleIndices = new int[ElbowSampleCount];
        Array.Fill(middleIndices, -1);
        var desired = new Dictionary<int, Vector2>();
        for (var side = 0; side < 2; side++)
            for (var row = 0; row < 2; row++)
                for (var i = 0; i < ElbowSampleCount; i++)
                {
                    var point = row == 0 ? plan.contour[side][i] : plan.inner[side][i];
                    var index = InsertPoint(point);
                    if (index < 0)
                    {
                        message = "Could not insert an elbow row point.";
                        return false;
                    }
                    rows[side, row, i] = index;
                    int parentIndex = InsertPoint(row == 0 ? plan.parentContour[side][i] : plan.parentInner[side][i]);
                    if (parentIndex < 0) { message = "Could not insert a parent-side elbow point."; return false; }
                    parentRows[side, row, i] = parentIndex;
                }
        for (var i = 0; i < middleIndices.Length; i++)
        {
            if (!plan.middleInside[i])
                continue;
            middleIndices[i] = InsertPoint(plan.middle[i]);
            if (middleIndices[i] < 0)
            {
                message = "Could not insert an elbow middle point.";
                return false;
            }
        }
        RegisterUndo("Apply Automatic Elbow");
        var originalBones = CloneBones(bones);
        var committed = false;
        var helpers = new int[2];
        try
        {
            for (var side = 0; side < 2; side++)
            {
                var name = bones[plan.lower].name + (side == 0 ? "_elbow_a" : "_elbow_b");
                var index = FindOrCreateVGenBone(plan.upper, name, plan.joint, true);
                helpers[side] = index;
                var bone = bones[index];
                var direction = plan.contour[side][0] - plan.joint;
                var rotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
                bone.position = bone.initialPosition = ConvertBoneWorldPositionToLocal(plan.upper, plan.joint);
                bone.rotation = bone.initialRotation = ConvertBoneWorldRotationToLocal(plan.upper, rotation);
                bone.length = direction.magnitude;
                bone.depth = bones[plan.upper].depth - 2 + side;
                RecomputeBoneWorldTransforms();
            }
            if (!TryEnsureFullBoobSpriteBone(staged, plan.upper, out var upper) ||
                !TryEnsureFullBoobSpriteBone(staged, plan.lower, out var lower))
            {
                message = "Could not bind the source bones.";
                return false;
            }
            EnsureBoneWeightsArraySize(staged, staged.vertices.Length);
            var weights = new Dictionary<int, BoneWeight>();
            for (var side = 0; side < 2; side++)
            {
                if (!TryEnsureFullBoobSpriteBone(staged, helpers[side], out var helper) ||
                    !TryBuildBindInverseMatrixForRendererBone(staged, helpers[side], out var inverse))
                {
                    message = "Could not bind an elbow helper.";
                    return false;
                }
                staged.bindBoneInverseMatrices[helper] = inverse;
                for (var i = 0; i < ElbowSampleCount; i++)
                {
                    var weight = ElbowSampleWeight(i, upper, lower, helper);
                    weights[rows[side, 0, i]] = weight;
                    weights[rows[side, 1, i]] = weight;
                }
                for (var vertex = 0; vertex < data.vertices.Length; vertex++)
                {
                    if (weights.ContainsKey(vertex))
                        continue;
                    var point = sourceDraw[vertex];
                    for (var i = 1; i < ElbowSampleCount; i++)
                    {
                        var polygon = new[] { plan.contour[side][i - 1], plan.contour[side][i],
                            plan.inner[side][i], plan.inner[side][i - 1] };
                        if (!ContainsFaceMouthPoint(point, polygon))
                            continue;
                        var edge = polygon[1] - polygon[0];
                        var t = Mathf.Clamp01(Vector2.Dot(point - polygon[0], edge) / Mathf.Max(edge.sqrMagnitude, 1e-6f));
                        weights[vertex] = BlendBoneWeights(ElbowSampleWeight(i - 1, upper, lower, helper), 1f - t,
                            ElbowSampleWeight(i, upper, lower, helper), t, default, 0f, null);
                        desired[vertex] = point;
                        break;
                    }
                }
            }
            for (var i = 0; i < middleIndices.Length; i++)
                if (middleIndices[i] >= 0)
                    weights[middleIndices[i]] = BlendBoneWeights(weights[rows[0, 0, i]], 0.5f,
                        weights[rows[1, 0, i]], 0.5f, default, 0f, null);
            var helperSlots = new int[2];
            for (int side = 0; side < 2; side++)
            {
                TryEnsureFullBoobSpriteBone(staged, helpers[side], out helperSlots[side]);
                for (int i = 0; i < ElbowSampleCount; i++)
                {
                    var weight = ElbowParentWeight(ElbowSampleDistance(i), upper, helperSlots[side]);
                    weights[parentRows[side, 0, i]] = weight;
                    weights[parentRows[side, 1, i]] = weight;
                }
                // Retain existing contour vertices, but give them the same
                // continuous falloff as the new samples instead of stale weights.
                foreach (int vertex in plan.parentOld[side])
                {
                    if (weights.ContainsKey(vertex)) continue;
                    weights[vertex] = ElbowParentWeight(ProjectElbowParentProgress(sourceDraw[vertex], plan.parentContour[side]), upper, helperSlots[side]);
                    desired[vertex] = sourceDraw[vertex];
                }
            }
            // Cover the whole parent-side strip, including interior vertices;
            // contour-only reweighting would leave another seam through its middle.
            for (int vertex = 0; vertex < staged.vertices.Length; vertex++)
            {
                if (weights.ContainsKey(vertex)) continue;
                Vector2 point = desired.TryGetValue(vertex, out var inserted) ? inserted : sourceDraw[vertex];
                if (!TryEvaluateElbowParentBand(point, plan.parentContour[0], plan.parentContour[1], upper,
                        helperSlots[0], helperSlots[1], out var weight)) continue;
                weights[vertex] = weight; desired[vertex] = point;
            }
            staged.skinMatrices = new Matrix4x4[staged.bindBoneInverseMatrices.Length];
            foreach (var pair in weights)
            {
                if (!TryUnskinEditGeometryPoint(staged, desired[pair.Key], pair.Value, out var basePoint))
                {
                    message = "The current pose collapses the elbow skinning transform.";
                    return false;
                }
                staged.vertices[pair.Key] = basePoint;
                staged.boneWeights[pair.Key] = pair.Value;
            }
            var protectedPoints = new HashSet<int>();
            foreach (var index in rows)
                protectedPoints.Add(index);
            foreach (var index in parentRows)
                protectedPoints.Add(index);
            foreach (var index in middleIndices)
                if (index >= 0)
                    protectedPoints.Add(index);
            var remove = new List<int>();
            foreach (var index in plan.obsoleteContour)
                if (!protectedPoints.Contains(index))
                    remove.Add(index);
            if (remove.Count > 0 && !TryDeleteVerticesAndRebuild(staged, remove))
            {
                message = "Could not rebuild the elbow after removing old contour vertices.";
                return false;
            }
            BuildEdgePairs(staged.triangles, staged.vertices.Length, out staged.allEdgePairs, out staged.boundaryEdgePairs);
            CommitOtherRegion(data.layer, data, staged);
            committed = true;
            bones[plan.lower].depth = bones[plan.upper].depth + 1;
            boneOrderVersion++;
            for (var side = 0; side < 2; side++)
            {
                var rig = GetPreviewBoneRig(helpers[side]);
                rig.enabled = true;
                rig.jointBone = bones[plan.lower].name;
                rig.flip = side == 1;
                rig.shiftOnBend = true;
                rig.maximumShift = plan.width * CorrectiveElbowShift;
            }
            SavePreviewBoneRigs();
            BuildBoneDrawOrder();
            MarkBoneStateChanged();
            message = "Applied two elbow helpers with child rows and a smooth parent-side transition.";
            return true;
        }
        finally
        {
            if (!committed)
                bones = originalBones;
            RecomputeBoneWorldTransforms();
        }

        int InsertPoint(Vector2 point)
        {
            var basePoint = ConvertPointToBaseDoc(data, point);
            var index = FindPoint(basePoint);
            if (index < 0)
            {
                index = staged.vertices.Length;
                if (!InsertAppliedVertexIntoLayerData(staged, basePoint, true) ||
                    Array.IndexOf(staged.triangles, index) < 0)
                    return -1;
            }
            desired[index] = point;
            return index;
        }

        int FindPoint(Vector2 point)
        {
            for (var i = 0; i < staged.vertices.Length; i++)
                if ((staged.vertices[i] - point).sqrMagnitude <= 0.0001f)
                    return i;
            return -1;
        }

    }

    private static float ElbowSampleDistance(int sample) =>
        sample <= 3 ? sample / 15f : (sample - 2) / 5f;

    private BoneWeight ElbowSampleWeight(int sample, int upper, int lower, int helper)
    {
        if (sample == 0)
            return BuildLimbTwoBoneWeight(upper, 0.5f, helper, 0.5f);
        if (sample < 3)
            return BlendBoneWeights(ElbowSampleWeight(0, upper, lower, helper), 1f - sample / 3f,
                ElbowSampleWeight(3, upper, lower, helper), sample / 3f, default, 0f, null);
        var curveValue = sample == 3 ? CorrectiveElbowWeightCurve[0].value :
            CorrectiveElbowWeightCurve.Evaluate((sample - 3) / 4f);
        var influence = sample == ElbowSampleCount - 1 ? 0f : Mathf.Clamp01(curveValue);
        return BuildLimbTwoBoneWeight(helper, influence, lower, 1f - influence);
    }
}
#endif
