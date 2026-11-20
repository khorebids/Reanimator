#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal enum PreviewBoneRigMode { JointPerpendicular, ArmAttachedMajorAngle, ShouldersAttachedRetreat }
    [Serializable]
    internal sealed class PreviewBoneRig
    {
        public string bone;
        public string jointBone;
        public bool enabled;
        public bool shiftOnBend;
        public bool flip;
        public float maximumShift = 10f;
        public bool shoulder;
        public float shoulderOpening = 180f;
        public PreviewBoneRigMode mode;
        public float referenceMajorAngle = 270f;
    }

    [Serializable]
    private sealed class PreviewBoneRigFile
    {
        public List<PreviewBoneRig> bones = new();
    }

    private PreviewBoneRigFile previewBoneRigs;
    private string previewBoneRigAsset;
    private int previewBoneRigDepth;
    private bool previewBoneRigApplied;
    internal bool PreviewBoneRigActive => previewBoneRigDepth > 0 && previewBoneRigApplied;

    internal PreviewBoneRig GetPreviewBoneRig(int boneIndex)
    {
        LoadPreviewBoneRigs();
        var name = bones[boneIndex].name;
        var rig = previewBoneRigs.bones.Find(item => item.bone == name);
        if (rig != null)
            return rig;
        var parent = bones[boneIndex].parentId;
        var lower = bones.FindIndex(item => item != null && item.index != boneIndex &&
            item.parentId == parent && !item.name.Contains("_corrective"));
        if (lower < 0)
            lower = parent;
        rig = new PreviewBoneRig
        {
            bone = name,
            jointBone = lower >= 0 ? bones[lower].name : null,
            maximumShift = Mathf.Max(1f, bones[boneIndex].length * 0.5f)
        };
        previewBoneRigs.bones.Add(rig);
        return rig;
    }

    internal string ValidatePreviewBoneRig(PreviewBoneRig rig)
    {
        if (!TryFindBoneIndexByName(rig.bone, out var driven) ||
            !TryFindBoneIndexByName(rig.jointBone, out var lower))
            return "Choose the joint's child bone.";
        var upper = bones[lower].parentId;
        if (upper < 0 || upper >= bones.Count || bones[upper] == null)
            return "The joint bone must have a parent.";
        if (rig.mode == PreviewBoneRigMode.ArmAttachedMajorAngle && bones[driven].parentId != lower)
            return "Arm-attached mode requires the helper to be a child of the selected arm bone.";
        if (rig.mode == PreviewBoneRigMode.ShouldersAttachedRetreat && bones[driven].parentId != upper)
            return "Shoulders-attached retreat requires the helper to be a child of the arm's parent.";
        if (IsPreviewRigDescendant(lower, driven) || IsPreviewRigDescendant(upper, driven))
            return "The source bones must be outside the driven bone's branch.";
        if (previewBoneRigs.bones.Exists(item => item.enabled && item.bone != rig.bone &&
            (item.bone == bones[lower].name || item.bone == bones[upper].name)))
            return "Choose source bones without a preview rig.";
        return null;
    }

    internal void SavePreviewBoneRigs()
    {
        var path = PreviewBoneRigFilePath();
        if (path == null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(previewBoneRigs, true));
        InvalidateBonePose();
    }

    private string PreviewBoneRigFilePath()
    {
        var guid = AssetDatabase.AssetPathToGUID(cachedPath);
        return string.IsNullOrEmpty(guid) ? null :
            Path.Combine("Library", "Reanimator", "PreviewBoneRigs", guid + ".json");
    }

    private void LoadPreviewBoneRigs()
    {
        if (previewBoneRigs != null && previewBoneRigAsset == cachedPath)
            return;
        previewBoneRigAsset = cachedPath;
        previewBoneRigs = new PreviewBoneRigFile();
        var path = PreviewBoneRigFilePath();
        if (path == null || !File.Exists(path))
            return;
        try
        {
            previewBoneRigs = JsonUtility.FromJson<PreviewBoneRigFile>(File.ReadAllText(path)) ?? new PreviewBoneRigFile();
            previewBoneRigs.bones ??= new List<PreviewBoneRig>();
            previewBoneRigs.bones.RemoveAll(item => item == null);
        }
        catch (Exception exception) when (exception is IOException || exception is ArgumentException)
        {
            Debug.LogWarning("Could not load Reanimator preview bone rigs: " + exception.Message);
        }
    }

    internal IDisposable BeginPreviewBoneRigs()
    {
        LoadPreviewBoneRigs();
        previewBoneRigDepth++;
        RecomputeBoneWorldTransforms();
        return new PreviewBoneRigScope(this);
    }

    private sealed class PreviewBoneRigScope : IDisposable
    {
        private PsdLayerPreviewRenderer renderer;
        internal PreviewBoneRigScope(PsdLayerPreviewRenderer renderer) => this.renderer = renderer;
        public void Dispose()
        {
            if (renderer == null)
                return;
            var changed = renderer.previewBoneRigApplied;
            renderer.previewBoneRigDepth--;
            renderer.RecomputeBoneWorldTransforms();
            if (changed)
                renderer.InvalidateBonePose();
            renderer = null;
        }
    }

    private bool IsPreviewRigDescendant(int bone, int ancestor)
    {
        for (var count = 0; bone >= 0 && bone < bones.Count && count < bones.Count; count++)
        {
            if (bone == ancestor)
                return true;
            bone = bones[bone].parentId;
        }
        return false;
    }

    private void ApplyPreviewBoneRigs()
    {
        previewBoneRigApplied = false;
        foreach (var bone in bones) if (bone != null) bone.previewScale = Vector3.one;
        if (previewBoneRigDepth == 0 || previewBoneRigs == null)
            return;
        previewBoneRigApplied = ApplySideAssPreview();
        foreach (var rig in previewBoneRigs.bones)
        {
            if (!rig.enabled || ValidatePreviewBoneRig(rig) != null)
                continue;
            TryFindBoneIndexByName(rig.bone, out var index);
            TryFindBoneIndexByName(rig.jointBone, out var lower);
            var upper = bones[lower].parentId;
            var incoming = (Vector2)(bones[upper].worldRotation * Vector3.right);
            var outgoing = (Vector2)(bones[lower].worldRotation * Vector3.right);
            EvaluatePreviewBoneRig(incoming, outgoing, rig.flip, rig.shiftOnBend ? rig.maximumShift : 0f,
                out var rotation, out var offset);
            if (rig.mode == PreviewBoneRigMode.ArmAttachedMajorAngle || rig.mode == PreviewBoneRigMode.ShouldersAttachedRetreat)
            {
                rotation = bones[index].worldRotation;
                var direction = (Vector2)(rotation * Vector3.right) * (rig.flip ? -1 : 1);
                var bend = ShoulderAttachmentRig2D.EvaluateRetreat(Vector2.Angle(incoming, outgoing), rig.referenceMajorAngle);
                offset = rig.shiftOnBend ? direction * rig.maximumShift * bend : Vector2.zero;
            }
            else if (rig.shoulder)
            {
                var direction = GetShoulderHelperDirection(incoming, outgoing) * (rig.flip ? -1f : 1f);
                rotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
                var opening = Vector2.Angle(outgoing, direction);
                var bend = ElbowJointRig2D.EvaluateShoulderBend(opening, rig.shoulderOpening, rig.flip);
                offset = rig.shiftOnBend ? direction * (rig.maximumShift * bend) : Vector2.zero;
            }
            var target = bones[index];
            var originalPosition = target.worldPosition;
            var deltaRotation = rotation * Quaternion.Inverse(target.worldRotation);
            var targetPosition = originalPosition + offset;
            for (var child = 0; child < bones.Count; child++)
            {
                if (!IsPreviewRigDescendant(child, index))
                    continue;
                var bone = bones[child];
                var displacement = deltaRotation * (Vector3)(bone.worldPosition - originalPosition);
                bone.worldPosition = targetPosition + (Vector2)displacement;
                bone.worldRotation = deltaRotation * bone.worldRotation;
            }
            previewBoneRigApplied = true;
        }
        if (previewBoneRigApplied)
            InvalidateBonePose();
    }

    internal static void EvaluatePreviewBoneRig(Vector2 incoming, Vector2 outgoing, bool flip,
        float maximumShift, out Quaternion rotation, out Vector2 offset)
    {
        var tangent = incoming.normalized + outgoing.normalized;
        if (tangent.sqrMagnitude < 1e-6f)
            tangent = incoming.normalized;
        var direction = new Vector2(-tangent.y, tangent.x).normalized * (flip ? -1f : 1f);
        rotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
        var signedBend = Vector2.SignedAngle(incoming, outgoing);
        var bend = Mathf.Clamp01(signedBend * (flip ? 1f : -1f) / 180f);
        offset = direction * (Mathf.Max(0f, maximumShift) * bend);
    }

    internal bool IsBoneAutomated(int index)
    {
        LoadPreviewBoneRigs();
        LoadSideAss();
        return index >= 0 && index < bones.Count && (previewBoneRigs.bones.Exists(rig => rig.enabled && rig.bone == bones[index].name) || sideAssFile.regions.Exists(r=>!r.manualFan && (r.volume==bones[index].name || r.press==bones[index].name)));
    }
}
#endif
