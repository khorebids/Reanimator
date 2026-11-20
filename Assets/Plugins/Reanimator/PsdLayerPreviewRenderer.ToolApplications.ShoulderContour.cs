#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal int RemoveOppositeShoulderHelpers()
    {
        const string suffix = "_shoulder_helper_opposite";
        LoadPreviewBoneRigs();
        string selected = selectedBoneIndex >= 0 && selectedBoneIndex < bones.Count ? bones[selectedBoneIndex].name : null;
        int removed = 0;
        for (int i = bones.Count - 1; i >= 0; i--)
        {
            if (!bones[i].name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            selectedBoneIndex = i;
            if (DeleteSelectedBone()) removed++;
        }
        int recipes = previewBoneRigs.bones.RemoveAll(rig => rig.bone != null && rig.bone.EndsWith(suffix, StringComparison.Ordinal));
        selectedBoneIndex = selected == null ? -1 : bones.FindIndex(bone => bone.name == selected);
        if (recipes > 0 || removed > 0) SavePreviewBoneRigs();
        return removed;
    }
    internal const float ShoulderSeamLengthInWidths = 2f;
    internal const float ShoulderHelperReachInWidths = 1f;
    [SerializeField] private AnimationCurve shoulderContourHelperCurve;
    [SerializeField] private AnimationCurve shoulderTransitionCurve;
    internal AnimationCurve ShoulderTransitionCurve
    {
        get { if (shoulderTransitionCurve == null) LoadShoulderDefaults(); return shoulderTransitionCurve ??= AnimationCurve.Linear(0, 0, 1, 1); }
        set => shoulderTransitionCurve = value;
    }
    internal AnimationCurve ShoulderContourHelperCurve
    {
        get { if (shoulderContourHelperCurve == null) LoadShoulderDefaults(); return shoulderContourHelperCurve ??= AnimationCurve.EaseInOut(0, 1, 1, 0); }
        set => shoulderContourHelperCurve = value;
    }

    internal sealed class ShoulderBoundaryTrack
    {
        internal readonly Vector2[] points;
        internal readonly float[] starts;
        internal readonly float length;
        internal ShoulderBoundaryTrack(Vector2[] vertices, IReadOnlyList<int> loop)
        {
            points = loop.Select(index => vertices[index]).ToArray();
            starts = new float[points.Length + 1];
            for (int i = 0; i < points.Length; i++)
                starts[i + 1] = starts[i] + Vector2.Distance(points[i], points[(i + 1) % points.Length]);
            length = starts[points.Length];
        }
        internal float Wrap(float distance) => Mathf.Repeat(distance, length);
        internal Vector2 At(float distance)
        {
            distance = Wrap(distance);
            for (int i = 0; i < points.Length; i++)
                if (distance <= starts[i + 1])
                    return Vector2.Lerp(points[i], points[(i + 1) % points.Length],
                        Mathf.InverseLerp(starts[i], starts[i + 1], distance));
            return points[0];
        }
        internal float Project(Vector2 point, out float distance)
        {
            float result = 0; distance = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i]; var b = points[(i + 1) % points.Length]; var delta = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / Mathf.Max(delta.sqrMagnitude, 1e-10f));
                float candidate = Vector2.Distance(point, Vector2.Lerp(a, b, t));
                if (candidate >= distance) continue;
                distance = candidate;
                result = Mathf.Lerp(starts[i], starts[i + 1], t);
            }
            return Wrap(result);
        }
        internal float Along(float start, float point, int direction) => Wrap((point - start) * direction);
        internal float Around(float a, float b) => Mathf.Min(Wrap(a - b), Wrap(b - a));
    }
    private sealed class ShoulderSeamArc
    {
        public float start;
        public int direction;
    }
    private sealed class ShoulderContourPlan
    {
        public LayerInfo layer;
        public LayerVertexData data;
        public ShoulderBoundaryTrack track;
        public List<int> loop;
        public readonly List<ShoulderSeamArc> seams = new();
        public int arm, shoulders;
        public Vector2 joint, direction, crossA, crossB, helperPoint;
        public float width, helperArc;
    }

    private bool TryBuildShoulderContour(out ShoulderContourPlan plan, out string error)
    {
        plan = null; error = "Select the arm sprite and upper arm bone, then draw the weld outline.";
        if (!TryGetBoneParent(selectedBoneIndex, out var arm, out var parent, out var shoulders) ||
            !TryGetSelectedLayer(out var layer) || !TryGetLayerVertexData(layer, out var data) ||
            data?.boneWeights == null || !TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, data.vertices.Length, out var loop))
            return false;
        var draw = GetLayerVerticesForDraw(data);
        var joint = GetBoneBasePoint(arm);
        var direction = GetShoulderHelperDirection(shoulders.worldRotation * Vector3.right, arm.worldRotation * Vector3.right);
        if (!TryGetShoulderReferenceWidth(draw, data.boundaryEdgePairs, joint, arm.worldRotation * Vector3.right, out var a, out var b))
        { error = "The perpendicular through the arm base must intersect both contours."; return false; }
        var result = new ShoulderContourPlan { layer = layer, data = data, loop = loop, arm = selectedBoneIndex,
            shoulders = parent, joint = joint, direction = direction, crossA = a, crossB = b,
            width = Vector2.Distance(a, b), track = new ShoulderBoundaryTrack(draw, loop) };
        if (!TryGetNearestContourRayIntersection(draw, data.boundaryEdgePairs, joint,
                Mathf.Atan2(direction.y, direction.x), out result.helperPoint))
        { error = "The major-angle bisector must intersect the arm contour."; return false; }
        result.helperArc = result.track.Project(result.helperPoint, out _);


        if (shoulderWeldContour == null || shoulderWeldContour.Count < 3 ||
            shoulderWeldLayer != selectedLayerSelectionKey || shoulderWeldBone != arm.name)
            return false;
        var crossings = new List<float>();
        for (int i = 0; i < result.track.points.Length; i++)
            for (int j = 0; j < shoulderWeldContour.Count; j++)
                if (TryGetSegmentIntersection(result.track.points[i], result.track.points[(i + 1) % loop.Count],
                        shoulderWeldContour[j], shoulderWeldContour[(j + 1) % shoulderWeldContour.Count], out var t, out _))
                {
                    float s = Mathf.Lerp(result.track.starts[i], result.track.starts[i + 1], t);
                    if (!crossings.Exists(old => result.track.Around(old, s) <= result.width * 0.0001f)) crossings.Add(s);
                }
        foreach (float crossing in crossings)
        {
            float epsilon = result.width * 0.0001f;
            bool before = ContainsFaceMouthPoint(result.track.At(crossing - epsilon), shoulderWeldContour);
            bool after = ContainsFaceMouthPoint(result.track.At(crossing + epsilon), shoulderWeldContour);
            if (before != after)
                result.seams.Add(new ShoulderSeamArc { start = crossing, direction = after ? -1 : 1 });
        }
        result.seams.Sort((x, y) => (result.track.At(x.start) - joint).sqrMagnitude.CompareTo((result.track.At(y.start) - joint).sqrMagnitude));
        if (result.seams.Count > 2) result.seams.RemoveRange(2, result.seams.Count - 2);
        if (result.seams.Count < 2)
        { error = "The weld outline must cross the arm silhouette on both sides of the attachment."; return false; }
        plan = result; error = null; return true;
    }
    internal bool CanApplyShoulderFan(out string error) => TryBuildShoulderContour(out _, out error);

    private void DrawShoulderFan(PsdCoordinateSpace coordinates)
    {
        if (Event.current.type != EventType.Repaint || coordinates == null) return;
        Handles.BeginGUI();
        var outline = ShoulderDrawing && shoulderWeldStroke.Count > 0 ? shoulderWeldStroke : shoulderWeldContour;
        if (outline != null && outline.Count > 1)
            DrawBoobV2Contour(coordinates.DocumentToView, outline, Color.green, !ShoulderDrawing);
        if (TryBuildShoulderContour(out var plan, out _))
        {
            using (new Handles.DrawingScope(Color.cyan))
                Handles.DrawAAPolyLine(3, coordinates.DocumentToView(plan.crossA), coordinates.DocumentToView(plan.crossB));
            using (new Handles.DrawingScope(new Color(1, .7f, .2f)))
                foreach (var seam in plan.seams)
                    for (int i = 0; i < 5; i++)
                        Handles.DrawSolidDisc(coordinates.DocumentToView(plan.track.At(seam.start + seam.direction * plan.width * ShoulderSeamLengthInWidths * i / 4f)), Vector3.forward, 4);
            using (new Handles.DrawingScope(Color.magenta))
            {
                Handles.DrawAAPolyLine(3, coordinates.DocumentToView(plan.joint), coordinates.DocumentToView(plan.helperPoint));
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 5; i++)
                        Handles.DrawSolidDisc(coordinates.DocumentToView(plan.track.At(plan.helperArc + side * plan.width * ShoulderHelperReachInWidths * i / 4f)), Vector3.forward, i == 0 ? 6 : 3);
            }

        }
        Handles.EndGUI();
    }

    internal bool ApplyShoulderFan(out string message)
    {
        SaveShoulderDefaults();
        SaveCurrentShoulderContour();
        RemoveOppositeShoulderHelpers();
        using var preview = BeginPreviewBoneRigs();
        if (!TryBuildShoulderContour(out var plan, out message)) return false;
        shoulderFanStamps ??= new List<ShoulderFanStamp>();
        string key = cachedPath + "|" + selectedLayerSelectionKey + "|" + bones[plan.arm].name;
        var stamp = shoulderFanStamps.Find(item => item.key == key);
        if (stamp != null && stamp.signature == ShoulderFanSignature(plan.data))
        { message = "Shoulder contour rig is already up to date."; return true; }
        var data = plan.data;
        var source = (Vector2[])GetLayerVerticesForDraw(data).Clone();
        var staged = CloneProjectorLayerData(data);
        var desired = new List<Vector2>(source);
        var protectedPoints = new HashSet<int>();
        foreach (var seam in plan.seams)
            for (int i = 0; i < 5; i++)
                protectedPoints.Add(Insert(plan.track.At(seam.start + seam.direction * plan.width * ShoulderSeamLengthInWidths * i / 4f)));
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 5; i++)
                protectedPoints.Add(Insert(plan.track.At(plan.helperArc + side * plan.width * ShoulderHelperReachInWidths * i / 4f)));
        if (protectedPoints.Contains(-1))
        { message = "Could not insert a shoulder contour sample."; return false; }
        int Insert(Vector2 point)
        {
            var basePoint = ConvertPointToBaseDoc(data, point);
            int nearest = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < plan.loop.Count; i++)
            {
                int a = plan.loop[i], b = plan.loop[(i + 1) % plan.loop.Count];
                var delta = source[b] - source[a];
                float t = Mathf.Clamp01(Vector2.Dot(point - source[a], delta) / Mathf.Max(delta.sqrMagnitude, 1e-10f));
                float distance = (point - Vector2.Lerp(source[a], source[b], t)).sqrMagnitude;
                if (distance < best) { best = distance; basePoint = Vector2.Lerp(data.vertices[a], data.vertices[b], t); }
            }
            nearest = Array.FindIndex(staged.vertices, candidate => (candidate - basePoint).sqrMagnitude <= .0001f);
            if (nearest >= 0) return nearest;
            int index = staged.vertices.Length;
            if (!InsertAppliedVertexIntoLayerData(staged, basePoint, true, allowBoundarySnap: false) || Array.IndexOf(staged.triangles, index) < 0) return -1;
            desired.Add(point); return index;
        }
        var remove = new List<int>();
        foreach (int index in plan.loop)
        {
            if (protectedPoints.Contains(index)) continue;
            float arc = plan.track.Project(source[index], out _);
            if (plan.seams.Exists(seam => { float distance = plan.track.Along(seam.start, arc, seam.direction); return distance > .001f && distance < plan.width * ShoulderSeamLengthInWidths - .001f; })) remove.Add(index);
        }
        if (remove.Count > 0 && !TryDeleteVerticesAndRebuild(staged, remove))
        { message = "Could not rebuild the resampled shoulder contour."; return false; }
        foreach (int index in remove.OrderByDescending(index => index)) desired.RemoveAt(index);
        if (!TryBuildBoundaryLoopFromPairs(staged.boundaryEdgePairs, staged.vertices.Length, out var smoothedLoop))
        { message = "Could not resolve the resampled shoulder contour."; return false; }
        var originalBones = CloneBones(bones);
        RegisterUndo("Apply Shoulder Contour Rig");
        bool committed = false;
        try
        {
            int helperIndex = FindOrCreateVGenBone(plan.shoulders, bones[plan.arm].name + "_shoulder_helper", plan.joint, true);
            var helper = bones[helperIndex];
            helper.parentId = plan.shoulders;
            var rotation = Quaternion.Euler(0, 0, Mathf.Atan2(plan.direction.y, plan.direction.x) * Mathf.Rad2Deg);
            helper.position = helper.initialPosition = ConvertBoneWorldPositionToLocal(plan.shoulders, plan.joint);
            helper.rotation = helper.initialRotation = ConvertBoneWorldRotationToLocal(plan.shoulders, rotation);
            helper.length = plan.width * ShoulderFanDepth;
            helper.depth = bones[plan.arm].depth - 1;

            RecomputeBoneWorldTransforms();
            if (!TryEnsureFullBoobSpriteBone(staged, helperIndex, out var helperSlot) ||
                !TryEnsureFullBoobSpriteBone(staged, plan.shoulders, out var shouldersSlot) ||
                !TryEnsureFullBoobSpriteBone(staged, plan.arm, out var armSlot) ||
                !TryBuildBindInverseMatrixForRendererBone(staged, helperIndex, out var inverse))
            { message = "Could not bind shoulder contour bones."; return false; }
            staged.bindBoneInverseMatrices[helperSlot] = inverse;

            staged.skinMatrices = new Matrix4x4[staged.bindBoneInverseMatrices.Length];
            EnsureBoneWeightsArraySize(staged, staged.vertices.Length);
            var boundary = BuildContourVertexMask(staged.boundaryEdgePairs, staged.vertices.Length);
            var changed = new bool[staged.vertices.Length];
            var helperTimes = new float[staged.vertices.Length];
            var inHelperRegion = new bool[staged.vertices.Length];
            for (int i = 0; i < staged.vertices.Length; i++)
            {
                var point = desired[i];
                float arc = plan.track.Project(point, out _);
                bool weld = ContainsFaceMouthPoint(point, shoulderWeldContour);
                float armWeight = weld ? 0 : 1;
                bool inSeam = false;
                if (!weld)
                    foreach (var seam in plan.seams)
                    {
                        float progress = plan.track.Along(seam.start, arc, seam.direction) / (plan.width * ShoulderSeamLengthInWidths);
                        if (progress > 1.0001f) continue;
                        armWeight = EvaluateShoulderTransition(progress, ShoulderTransitionCurve);
                        inSeam = true;
                        break;
                    }
                float helperDistance = plan.track.Around(arc, plan.helperArc) / (plan.width * ShoulderHelperReachInWidths);
                bool inHelper = boundary[i] && helperDistance <= 1.0001f;
                if (!weld && !inSeam && !inHelper) continue;

                staged.boneWeights[i] = EvaluateShoulderContourWeights(armWeight, helperDistance,
                    null, shouldersSlot, armSlot, helperSlot);
                changed[i] = true;
                helperTimes[i] = helperDistance;
                inHelperRegion[i] = inHelper;
            }
            // Smooth the next two contour points beyond each transition before the helper overlay.
            SmoothShoulderContourNeighbors(desired, smoothedLoop, plan.track,
                plan.seams.Select(seam => seam.start + seam.direction * plan.width * ShoulderSeamLengthInWidths).ToArray(),
                plan.seams.Select(seam => seam.direction).ToArray(), staged.boneWeights, changed, neighborCount: 2);
            for (int i = 0; i < staged.vertices.Length; i++)
            {
                if (!inHelperRegion[i]) continue;
                float h = Mathf.Clamp01(ShoulderContourHelperCurve.Evaluate(Mathf.Clamp01(helperTimes[i])));
                staged.boneWeights[i] = BlendBoneWeights(staged.boneWeights[i], 1 - h,
                    BuildSingleBoneWeight(helperSlot), h, default, 0, null);
            }


            // Then smooth two contour points outside both helper-curve endpoints.
            var overlayEnds = new List<float> { plan.helperArc - plan.width * ShoulderHelperReachInWidths, plan.helperArc + plan.width * ShoulderHelperReachInWidths };
            var overlayDirections = new List<int> { -1, 1 };

            SmoothShoulderContourNeighbors(desired, smoothedLoop, plan.track,
                overlayEnds, overlayDirections, staged.boneWeights, changed, neighborCount: 2);
            for (int i = 0; i < staged.vertices.Length; i++)
            {
                if (!changed[i]) continue;
                if (!TryUnskinEditGeometryPoint(staged, desired[i], staged.boneWeights[i], out var basePoint))
                { message = "The shoulder pose collapses the skinning transform."; return false; }
                staged.vertices[i] = basePoint;
            }
            CommitOtherRegion(plan.layer, data, staged);
            var rig = GetPreviewBoneRig(helperIndex);
            rig.enabled = true; rig.mode = PreviewBoneRigMode.ShouldersAttachedRetreat;
            rig.shoulder = false; rig.jointBone = bones[plan.arm].name;
            rig.shiftOnBend = true; rig.maximumShift = plan.width * ShoulderFanShiftWidth;
            rig.referenceMajorAngle = 360 - Vector2.Angle(bones[plan.shoulders].worldRotation * Vector3.right, bones[plan.arm].worldRotation * Vector3.right);


            SavePreviewBoneRigs();
            committed = true;
            boneOrderVersion++; BuildBoneDrawOrder(); MarkBoneStateChanged();
            if (stamp == null) { stamp = new ShoulderFanStamp { key = key }; shoulderFanStamps.Add(stamp); }
            stamp.signature = ShoulderFanSignature(data);
            message = $"Shoulder contour rig applied: a={plan.width:0.##}, two 5-point weld arcs of 2a, helper falloff a; translation only.";
            return true;
        }
        finally { if (!committed) bones = originalBones; RecomputeBoneWorldTransforms(); }
    }

    internal static BoneWeight EvaluateShoulderContourWeights(float armProgress, float helperTime,
        AnimationCurve helperCurve, int shoulders, int arm, int helper)
    {
        float t = Mathf.Clamp01(armProgress);
        float h = helperCurve == null ? 0f : Mathf.Clamp01(helperCurve.Evaluate(Mathf.Clamp01(helperTime)));
        return new BoneWeight
        {
            boneIndex0 = shoulders, weight0 = (1f - t) * (1f - h),
            boneIndex1 = arm, weight1 = t * (1f - h),
            boneIndex2 = helper, weight2 = h
        };
    }

    internal static float EvaluateShoulderTransition(float progress, AnimationCurve curve) =>
        progress <= .0001f ? 0f : progress >= .9999f ? 1f : Mathf.Clamp01(curve.Evaluate(progress));

    internal static void SmoothShoulderContourNeighbors(IReadOnlyList<Vector2> points, IReadOnlyList<int> loop,
        ShoulderBoundaryTrack track, IReadOnlyList<float> endpoints, IReadOnlyList<int> directions,
        BoneWeight[] weights, bool[] changed, int neighborCount = 1)
    {
        if (loop.Count < 3) return;
        var snapshot = (BoneWeight[])weights.Clone();
        var counts = new Dictionary<int, int>();
        for (int endpoint = 0; endpoint < endpoints.Count; endpoint++)
        {
            int closest = 0; float best = float.PositiveInfinity;
            for (int i = 0; i < loop.Count; i++)
            {
                float arc = track.Project(points[loop[i]], out _);
                float distance = track.Around(arc, endpoints[endpoint]);
                if (distance >= best) continue;
                best = distance; closest = i;
            }
            int previous = (closest + loop.Count - 1) % loop.Count, next = (closest + 1) % loop.Count;
            float previousArc = track.Project(points[loop[previous]], out _);
            float nextArc = track.Project(points[loop[next]], out _);
            int step = track.Along(endpoints[endpoint], nextArc, directions[endpoint]) <=
                track.Along(endpoints[endpoint], previousArc, directions[endpoint]) ? 1 : -1;
            int edge = loop[closest];
            int countToSmooth = Mathf.Clamp(neighborCount, 1, loop.Count - 2);
            int far = loop[(closest + step * (countToSmooth + 1) + loop.Count * 2) % loop.Count];
            float total = 0;
            for (int offset = 1; offset <= countToSmooth + 1; offset++)
                total += Vector2.Distance(points[loop[(closest + step * (offset - 1) + loop.Count * 2) % loop.Count]],
                    points[loop[(closest + step * offset + loop.Count * 2) % loop.Count]]);
            if (total <= 1e-8f) continue;
            float distanceAlong = 0;
            for (int offset = 1; offset <= countToSmooth; offset++)
            {
                int neighbor = loop[(closest + step * offset + loop.Count * 2) % loop.Count];
                int previousIndex = loop[(closest + step * (offset - 1) + loop.Count * 2) % loop.Count];
                distanceAlong += Vector2.Distance(points[previousIndex], points[neighbor]);
                float fraction = distanceAlong / total;
                var weight = BlendBoneWeights(snapshot[edge], 1 - fraction, snapshot[far], fraction, default, 0, null);
                counts.TryGetValue(neighbor, out int count);
                weights[neighbor] = count == 0 ? weight : BlendBoneWeights(weights[neighbor], count / (float)(count + 1), weight, 1f / (count + 1), default, 0, null);
                counts[neighbor] = count + 1;
                changed[neighbor] = true;
            }
        }
    }
}
#endif
