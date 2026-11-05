#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal sealed class BonePreviewTransform
{
    private const byte VisitInProgress = 1;
    private const byte VisitDone = 2;

    private readonly bool treatAsWorldSpace;
    private readonly bool rotateChildOffsetsByParent;

    public BonePreviewTransform(bool treatAsWorldSpace, bool rotateChildOffsetsByParent)
    {
        this.treatAsWorldSpace = treatAsWorldSpace;
        this.rotateChildOffsetsByParent = rotateChildOffsetsByParent;
    }

    public void ResolveSingle(IList<PsdLayerPreviewRenderer.BoneInfo> bones, int index, bool[] visited)
    {
        if (bones == null || index < 0 || index >= bones.Count)
            return;
        if (IsVisited(visited, index))
            return;

        var state = new byte[bones.Count];
        if (visited != null)
        {
            var copyCount = visited.Length < state.Length ? visited.Length : state.Length;
            for (var i = 0; i < copyCount; i++)
            {
                if (visited[i])
                    state[i] = VisitDone;
            }
        }

        ResolveSingleInternal(bones, index, state);

        if (visited == null)
            return;

        var resultCount = visited.Length < state.Length ? visited.Length : state.Length;
        for (var i = 0; i < resultCount; i++)
            visited[i] = state[i] == VisitDone;
    }

    public void ResolveAll(IList<PsdLayerPreviewRenderer.BoneInfo> bones)
    {
        if (bones == null || bones.Count == 0)
            return;

        var state = new byte[bones.Count];
        for (var i = 0; i < bones.Count; i++)
            ResolveSingleInternal(bones, i, state);
    }

    private void ResolveSingleInternal(IList<PsdLayerPreviewRenderer.BoneInfo> bones, int index, byte[] state)
    {
        if (bones == null || index < 0 || index >= bones.Count)
            return;
        if (IsStateDone(state, index))
            return;

        if (IsStateInProgress(state, index))
        {
            // Defensive fallback for malformed rigs with parent cycles.
            SetBoneToRawWorld(bones, index);
            SetStateDone(state, index);
            return;
        }

        SetStateInProgress(state, index);

        var bone = bones[index];
        if (treatAsWorldSpace)
        {
            bone.worldPosition = bone.position;
            bone.worldRotation = NormalizeRotation(bone.rotation);
            bones[index] = bone;
            SetStateDone(state, index);
            return;
        }

        var hasParent = bone.parentId >= 0 && bone.parentId < bones.Count;
        if (hasParent)
        {
            var parentIndex = bone.parentId;
            ResolveSingleInternal(bones, parentIndex, state);

            if (IsStateDone(state, index))
                return;

            if (IsStateDone(state, parentIndex))
            {
                var parent = bones[parentIndex];
                var parentWorldRotation = NormalizeRotation(parent.worldRotation);
                var localOffset3 = rotateChildOffsetsByParent
                    ? parentWorldRotation * new Vector3(bone.position.x, bone.position.y, 0f)
                    : new Vector3(bone.position.x, bone.position.y, 0f);
                bone.worldPosition = parent.worldPosition + new Vector2(localOffset3.x, localOffset3.y);
                bone.worldRotation = NormalizeRotation(parentWorldRotation * NormalizeRotation(bone.rotation));
            }
            else
            {
                hasParent = false;
            }
        }

        if (!hasParent)
        {
            bone.worldPosition = bone.position;
            bone.worldRotation = NormalizeRotation(bone.rotation);
        }

        bones[index] = bone;
        SetStateDone(state, index);
    }

    public void ConvertWorldToLocal(IList<PsdLayerPreviewRenderer.BoneInfo> bones)
    {
        if (bones == null || bones.Count == 0)
            return;

        var count = bones.Count;
        var worldPositions = new Vector2[count];
        var worldRotations = new Quaternion[count];
        for (var i = 0; i < count; i++)
        {
            worldPositions[i] = bones[i].position;
            worldRotations[i] = bones[i].rotation;
        }

        var localPositions = new Vector2[count];
        var localRotations = new Quaternion[count];
        var resolved = new bool[count];

        for (var pass = 0; pass < count + 1; pass++)
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                if (resolved[i])
                    continue;

                var bone = bones[i];
                var parentIndex = bone.parentId;
                var hasParent = parentIndex >= 0 && parentIndex < count;
                if (hasParent && !resolved[parentIndex])
                    continue;

                if (hasParent)
                {
                    var parentWorldPos = worldPositions[parentIndex];
                    var parentWorldRot = worldRotations[parentIndex];
                    var localPos3 = Quaternion.Inverse(parentWorldRot) * new Vector3(
                        worldPositions[i].x - parentWorldPos.x,
                        worldPositions[i].y - parentWorldPos.y,
                        0f);
                    localPositions[i] = new Vector2(localPos3.x, localPos3.y);
                    localRotations[i] = Quaternion.Inverse(parentWorldRot) * worldRotations[i];
                }
                else
                {
                    localPositions[i] = worldPositions[i];
                    localRotations[i] = worldRotations[i];
                }

                resolved[i] = true;
                changed = true;
            }

            if (!changed)
                break;
        }

        for (var i = 0; i < count; i++)
        {
            if (!resolved[i])
            {
                localPositions[i] = worldPositions[i];
                localRotations[i] = worldRotations[i];
            }

            var bone = bones[i];
            bone.position = localPositions[i];
            bone.rotation = localRotations[i];
            bones[i] = bone;
        }
    }

    private static bool IsVisited(bool[] visited, int index)
    {
        return visited != null && index >= 0 && index < visited.Length && visited[index];
    }

    private static bool IsStateDone(byte[] state, int index)
    {
        return state != null && index >= 0 && index < state.Length && state[index] == VisitDone;
    }

    private static bool IsStateInProgress(byte[] state, int index)
    {
        return state != null && index >= 0 && index < state.Length && state[index] == VisitInProgress;
    }

    private static void SetStateInProgress(byte[] state, int index)
    {
        if (state == null || index < 0 || index >= state.Length)
            return;
        state[index] = VisitInProgress;
    }

    private static void SetStateDone(byte[] state, int index)
    {
        if (state == null || index < 0 || index >= state.Length)
            return;
        state[index] = VisitDone;
    }

    private static void SetBoneToRawWorld(IList<PsdLayerPreviewRenderer.BoneInfo> bones, int index)
    {
        if (bones == null || index < 0 || index >= bones.Count)
            return;

        var bone = bones[index];
        bone.worldPosition = bone.position;
        bone.worldRotation = NormalizeRotation(bone.rotation);
        bones[index] = bone;
    }

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        var lengthSquared = rotation.x * rotation.x +
                            rotation.y * rotation.y +
                            rotation.z * rotation.z +
                            rotation.w * rotation.w;
        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.000001f)
            return Quaternion.identity;

        var inverseLength = 1f / Mathf.Sqrt(lengthSquared);
        return new Quaternion(
            rotation.x * inverseLength,
            rotation.y * inverseLength,
            rotation.z * inverseLength,
            rotation.w * inverseLength);
    }
}
#endif
