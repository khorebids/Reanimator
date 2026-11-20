#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private bool TryApplyShouldersWeightsIfReady(out string error)
    {
        error = null;
        if (!TryFindBoneIndexByName("shoulders", out _) || !TryFindFullBoobSpineRendererBone(out var spine))
            return true;
        var body = layerInfos.Find(item => item != null && item.visible &&
            NormalizeAutoWeightName(item.name) is "body" or "torso");
        if (body == null || !TryGetLayerVertexData(body, out var data) || data?.boneWeights == null)
            return true;
        var spineSlot = FindSpriteBoneIndexForRendererBone(data, spine);
        if (spineSlot < 0 || !Array.Exists(data.boneWeights, weight => GetSpriteBoneWeight(weight, spineSlot) > 0f))
            return true;
        if (!TryApplyShoulders(out error))
            return false;
        error = null;
        return true;
    }

    private bool TryBuildShoulderWeightBand(LayerVertexData data, Vector2[] draw, Vector2 start, Vector2 axis,
        float length, out LayerVertexData staged, out List<Vector2> desired, out string message)
    {
        message = null;
        staged = CloneProjectorLayerData(data);
        desired = new List<Vector2>(draw);
        var targets = desired;
        var vertices = new List<Vector2>(data.vertices);
        var uvs = new List<Vector2>(data.vertexUvs);
        var weights = new List<BoneWeight>(data.boneWeights);
        var triangles = new List<int>(data.triangles);
        // Split the current triangles directly on each plane. Interactive insertion
        // snaps to nearby edges and flips diagonals across earlier cuts; both can
        // leave orphan vertices and add new cuts on every subsequent run.
        for (var level = 0; level <= 3; level++)
        {
            var distance = length * level / 3f;
            var intersections = new Dictionary<ulong, int>();
            var next = new List<int>();
            for (var t = 0; t + 2 < triangles.Count; t += 3)
            {
                var a = triangles[t]; var b = triangles[t + 1]; var c = triangles[t + 2];
                var da = Distance(a); var db = Distance(b); var dc = Distance(c);
                if (!(Mathf.Min(da, Mathf.Min(db, dc)) < -0.001f && Mathf.Max(da, Mathf.Max(db, dc)) > 0.001f))
                {
                    next.Add(a); next.Add(b); next.Add(c);
                    continue;
                }
                Clip(a, b, c, true);
                Clip(a, b, c, false);
            }
            triangles = next;

            float Distance(int index) => Vector2.Dot(targets[index] - start, axis) - distance;

            int Intersect(int a, int b)
            {
                var da = Distance(a); var db = Distance(b);
                if (Mathf.Abs(da) <= 0.001f) return a;
                if (Mathf.Abs(db) <= 0.001f) return b;
                var key = MakeEdgeKey(a, b);
                if (intersections.TryGetValue(key, out var found)) return found;
                var fraction = Mathf.Clamp01(da / (da - db));
                var point = Vector2.Lerp(vertices[a], vertices[b], fraction);
                var index = vertices.Count;
                vertices.Add(point);
                targets.Add(Vector2.Lerp(targets[a], targets[b], fraction));
                uvs.Add(Vector2.Lerp(uvs[a], uvs[b], fraction));
                var accum = new Dictionary<int, float>();
                Add(weights[a], 1f - fraction);
                Add(weights[b], fraction);
                weights.Add(BuildVertexFixWeight(accum));
                intersections.Add(key, index);
                return index;

                void Add(BoneWeight weight, float scale)
                {
                    AccumulateWeightChannel(accum, weight.boneIndex0, weight.weight0 * scale);
                    AccumulateWeightChannel(accum, weight.boneIndex1, weight.weight1 * scale);
                    AccumulateWeightChannel(accum, weight.boneIndex2, weight.weight2 * scale);
                    AccumulateWeightChannel(accum, weight.boneIndex3, weight.weight3 * scale);
                }
            }

            void Clip(int a, int b, int c, bool positive)
            {
                var polygon = new List<int>(4);
                var source = new[] { a, b, c };
                for (var i = 0; i < 3; i++)
                {
                    var current = source[i]; var previous = source[(i + 2) % 3];
                    var currentInside = positive ? Distance(current) >= -0.001f : Distance(current) <= 0.001f;
                    var previousInside = positive ? Distance(previous) >= -0.001f : Distance(previous) <= 0.001f;
                    if (currentInside != previousInside) Append(Intersect(previous, current));
                    if (currentInside) Append(current);
                }
                if (polygon.Count > 1 && polygon[0] == polygon[polygon.Count - 1]) polygon.RemoveAt(polygon.Count - 1);
                for (var i = 1; i + 1 < polygon.Count; i++)
                {
                    next.Add(polygon[0]); next.Add(polygon[i]); next.Add(polygon[i + 1]);
                }
                void Append(int index)
                {
                    if (polygon.Count == 0 || polygon[polygon.Count - 1] != index) polygon.Add(index);
                }
            }
        }
        staged.vertices = vertices.ToArray();
        staged.vertexUvs = uvs.ToArray();
        staged.boneWeights = weights.ToArray();
        staged.triangles = triangles.ToArray();
        BuildEdgePairs(staged.triangles, staged.vertices.Length, out staged.allEdgePairs, out staged.boundaryEdgePairs);
        return true;
    }

    internal bool TryApplyShoulders(out string message)
    {
        message = "The shoulders rig needs a spine bone and weighted body sprite.";
        if (!TryFindFullBoobSpineRendererBone(out var spine))
            return false;
        var layer = layerInfos.Find(item => item != null && item.visible &&
            NormalizeAutoWeightName(item.name) == "body");
        if (layer == null)
            layer = layerInfos.Find(item => item != null && item.visible &&
                NormalizeAutoWeightName(item.name) == "torso");
        if (layer == null || !TryGetLayerVertexData(layer, out var data) ||
            data?.vertices == null || data.triangles == null || data.boneWeights == null ||
            FindSpriteBoneIndexForRendererBone(data, spine) < 0 || bones[spine].length < 0.01f)
            return false;

        var arms = new List<int>();
        foreach (var name in new[] { "l_arm", "r_arm" })
            if (TryFindBoneIndexByName(name, out var arm))
                arms.Add(arm);
        if (arms.Count == 0)
        {
            message = "No l_arm or r_arm bone was found.";
            return false;
        }
        var originals = CloneBones(bones);
        var rest = CloneBones(bones);
        foreach (var bone in rest)
        {
            bone.position = bone.initialPosition;
            bone.rotation = bone.initialRotation;
        }
        new BonePreviewTransform(bonesTreatAsWorldSpace, rotateLocalBoneOffsetsByParent).ResolveAll(rest);

        int neckReference=TryFindBoneIndexByName("neck",out var neckIndex)?neckIndex:spine;
        var start = neckReference==spine?GetBoneEnd(spine):GetBoneBasePoint(bones[neckReference]);
        var axis = -(Vector2)(bones[neckReference].worldRotation * Vector3.right);
        if(SkeletronShoulderGuideOffset.sqrMagnitude>.0001f)
        {
            var normal=new Vector2(-SkeletronShoulderGuideOffset.y,SkeletronShoulderGuideOffset.x).normalized;
            if(Vector2.Dot(normal,axis)<0f)normal=-normal;
            axis=normal;
        }
        float length=0f;
        foreach(var arm in arms)length=Mathf.Max(length,Vector2.Dot(GetBoneBasePoint(bones[arm])-start,axis));
        if(length<.01f)length=Mathf.Max(.01f,bones[neckReference].length);
        var draw = GetLayerVerticesForDraw(data);
        if (!TryBuildShoulderWeightBand(data, draw, start, axis, length, out var staged, out var desired, out message))
            return false;

        RegisterUndo("Create Shoulders Rig");
        var committed = false;
        try
        {
            var shoulders = TryFindBoneIndexByName("shoulders", out var existing)
                ? existing : FindOrCreateVGenBone(spine, "shoulders", start, true);
            if (shoulders == spine || IsBoneDescendant(spine, shoulders))
            {
                message = "The existing shoulders bone cannot be a parent of spine.";
                return false;
            }
            var rotation = Quaternion.Euler(0f,0f,Mathf.Atan2(axis.y,axis.x)*Mathf.Rad2Deg);
            var shoulder = bones[shoulders];
            shoulder.parentId = spine;
            shoulder.position = ConvertBoneWorldPositionToLocal(spine, start);
            shoulder.rotation = ConvertBoneWorldRotationToLocal(spine, rotation);
            shoulder.length = length;
            shoulder.depth = bones[spine].depth + 1;
            var restStart = neckReference==spine?rest[spine].worldPosition+(Vector2)(rest[spine].worldRotation*Vector3.right)*bones[spine].length:rest[neckReference].worldPosition;
            var restRotation = rest[neckReference].worldRotation*Quaternion.Inverse(bones[neckReference].worldRotation)*rotation;
            shoulder.initialPosition = LocalPosition(rest[spine].worldPosition, rest[spine].worldRotation, restStart);
            shoulder.initialRotation = bonesTreatAsWorldSpace ? restRotation :
                Quaternion.Inverse(rest[spine].worldRotation) * restRotation;
            RecomputeBoneWorldTransforms();
            foreach (var arm in arms)
            {
                var bone = bones[arm];
                bone.parentId = shoulders;
                bone.position = ConvertBoneWorldPositionToLocal(shoulders, originals[arm].worldPosition);
                bone.rotation = ConvertBoneWorldRotationToLocal(shoulders, originals[arm].worldRotation);
                bone.initialPosition = LocalPosition(restStart, restRotation, rest[arm].worldPosition);
                bone.initialRotation = bonesTreatAsWorldSpace ? rest[arm].worldRotation :
                    Quaternion.Inverse(restRotation) * rest[arm].worldRotation;
            }
            RecomputeBoneWorldTransforms();
            if (!TryEnsureFullBoobSpriteBone(staged, spine, out var spineSlot) ||
                !TryEnsureFullBoobSpriteBone(staged, shoulders, out var shoulderSlot) ||
                !TryBuildBindInverseMatrixForRendererBone(staged, shoulders, out var inverse))
            {
                message = "Could not bind the shoulders bone to the body.";
                return false;
            }
            staged.bindBoneInverseMatrices[shoulderSlot] = inverse;
            var neckSlot = -1;
            if (TryFindBoneIndexByName("neck", out var neck) &&
                !TryEnsureFullBoobSpriteBone(staged, neck, out neckSlot))
            {
                message = "Could not bind neck to the body.";
                return false;
            }
            staged.skinMatrices = new Matrix4x4[staged.bindBoneInverseMatrices.Length];
            for (var i = 0; i < staged.vertices.Length; i++)
            {
                var original = staged.boneWeights[i];
                var mass = GetSpriteBoneWeight(original, spineSlot) + GetSpriteBoneWeight(original, shoulderSlot) +
                    (neckSlot >= 0 ? GetSpriteBoneWeight(original, neckSlot) : 0f);
                if (mass <= 0f)
                    continue;
                var distance = Vector2.Dot(desired[i] - start, axis);
                var shoulderInfluence = 1f - Mathf.Clamp01(distance / length);
                var replacement = distance < -0.0001f && neckSlot >= 0
                    ? BuildSingleBoneWeight(neckSlot)
                    : BuildLimbTwoBoneWeight(shoulderSlot, shoulderInfluence, spineSlot, 1f - shoulderInfluence);
                var accum = new Dictionary<int, float>();
                Keep(original.boneIndex0, original.weight0);
                Keep(original.boneIndex1, original.weight1);
                Keep(original.boneIndex2, original.weight2);
                Keep(original.boneIndex3, original.weight3);
                AccumulateWeightChannel(accum, replacement.boneIndex0, replacement.weight0 * mass);
                AccumulateWeightChannel(accum, replacement.boneIndex1, replacement.weight1 * mass);
                var weight = BuildVertexFixWeight(accum);
                if (!TryUnskinEditGeometryPoint(staged, desired[i], weight, out var basePoint))
                {
                    message = "The current pose collapses the body skinning transform.";
                    return false;
                }
                staged.vertices[i] = basePoint;
                staged.boneWeights[i] = weight;

                void Keep(int slot, float influence)
                {
                    if (slot != spineSlot && slot != shoulderSlot && slot != neckSlot)
                        AccumulateWeightChannel(accum, slot, influence);
                }
            }
            CommitOtherRegion(layer, data, staged);
            committed = true;
            boneOrderVersion++;
            BuildBoneDrawOrder();
            InvalidateBonePose();
            MarkBoneStateChanged();
            selectedBoneIndex = shoulders;
            message = neckSlot >= 0 ? "Shoulders created. Arms reparented; body gradient and neck weights applied." :
                "Shoulders created. Arms reparented; body gradient applied. No neck bone found.";
            return true;
        }
        finally
        {
            if (!committed)
                bones = originals;
            RecomputeBoneWorldTransforms();
        }

        Vector2 LocalPosition(Vector2 parentPosition, Quaternion parentRotation, Vector2 world)
        {
            if (bonesTreatAsWorldSpace)
                return world;
            var offset = (Vector3)(world - parentPosition);
            if (rotateLocalBoneOffsetsByParent)
                offset = Quaternion.Inverse(parentRotation) * offset;
            return offset;
        }
    }
}
#endif
