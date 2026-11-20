#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal void ConfigureAutomaticElbowRuntime(IReadOnlyList<Transform> transforms)
    {
        ConfigureSideAssRuntime(transforms);
        LoadPreviewBoneRigs();
        foreach (var recipe in previewBoneRigs.bones)
        {
            if (!recipe.enabled || (recipe.mode != PreviewBoneRigMode.ArmAttachedMajorAngle && recipe.mode != PreviewBoneRigMode.ShouldersAttachedRetreat && !recipe.shoulder && !recipe.bone.Contains("_elbow_")) ||
                !TryFindBoneIndexByName(recipe.bone, out var helper) ||
                !TryFindBoneIndexByName(recipe.jointBone, out var joint))
                continue;
            Transform target = null;
            Transform source = null;
            foreach (var bone in transforms)
            {
                if (bone.name == recipe.bone) target = bone;
                if (bone.name == recipe.jointBone) source = bone;
            }
            if (!target || !source || !target.parent)
                continue;
            if (recipe.mode == PreviewBoneRigMode.ArmAttachedMajorAngle || recipe.mode == PreviewBoneRigMode.ShouldersAttachedRetreat)
            {
                var oldRig = target.GetComponent<ElbowJointRig2D>();
                if (oldRig) UnityEngine.Object.DestroyImmediate(oldRig);
                var shoulders = source.parent;
                float docLength = Vector2.Distance(bones[joint].worldPosition, bones[bones[joint].parentId].worldPosition);
                float units = target.parent.InverseTransformVector(shoulders.position - source.position).magnitude;
                if (docLength < .0001f)
                    foreach (var candidate in transforms)
                        if (candidate.parent == source && candidate != target && TryFindBoneIndexByName(candidate.name, out var childIndex))
                        {
                            docLength = Vector2.Distance(bones[joint].worldPosition, bones[childIndex].worldPosition);
                            units = target.parent.InverseTransformVector(candidate.position - source.position).magnitude;
                            if (docLength > .0001f) break;
                        }
                var attachment = target.GetComponent<ShoulderAttachmentRig2D>();
                if (!attachment) attachment = target.gameObject.AddComponent<ShoulderAttachmentRig2D>();
                attachment.Configure(source, shoulders, recipe.shiftOnBend && docLength > .0001f ? recipe.maximumShift * units / docLength : 0,
                    recipe.referenceMajorAngle, recipe.flip);
                continue;
            }
            var parent = bones[helper].parentId;
            var documentLength = Vector2.Distance(bones[parent].worldPosition, bones[joint].worldPosition);
            var localLength = target.parent.InverseTransformVector(source.position - target.parent.position).magnitude;
            var shift = documentLength > 0.0001f && recipe.shiftOnBend
                ? recipe.maximumShift * localLength / documentLength : 0f;
            var rig = target.GetComponent<ElbowJointRig2D>();
            if (!rig) rig = target.gameObject.AddComponent<ElbowJointRig2D>();
            if (recipe.shoulder) rig.ConfigureShoulder(source, recipe.flip, shift, recipe.shoulderOpening);
            else rig.Configure(source, recipe.flip, shift);
        }
    }

    internal string ApplyAutomaticElbowJoints(int targets)
    {
        var specs = new[] { LeftArmAutoLimbJointSpecs[0], RightArmAutoLimbJointSpecs[0],
            LeftLegAutoLimbJointSpecs[0], RightLegAutoLimbJointSpecs[0],
            LeftSecondaryArmAutoLimbJointSpecs[0], RightSecondaryArmAutoLimbJointSpecs[0] };
        var results = new List<string>();
        var previousLayer = selectedLayerSelectionKey;
        var previousBone = selectedBoneIndex;
        try
        {
            for (var i = 0; i < specs.Length; i++)
            {
                if ((targets & (1 << i)) == 0)
                    continue;
                var spec = specs[i];
                if (!TryFindRendererBonePairByNamePrefixes(spec.ParentBoneNames, spec.ChildBoneNames,
                        out _, out var child))
                {
                    results.Add(spec.Label + ": absent from skeleton");
                    continue;
                }
                var layer = layerInfos.Find(item => item != null && item.visible &&
                    Array.Exists(spec.PrimaryLayerNames, name => NormalizeAutoWeightName(item.name) == NormalizeAutoWeightName(name)));
                if (layer == null)
                {
                    results.Add(spec.Label + ": sprite absent");
                    continue;
                }
                if(FindSplitArmLowerLayer(layer.name)!=null)
                {
                    // Already separated at the joint: the two sprites rotate
                    // with their own segments. A single-mesh corrective fan
                    // would inject lower-arm weights into the upper sprite.
                    results.Add(spec.Label+": split sprites retain separate segment weights");
                    continue;
                }
                SelectLayer(layer);
                selectedBoneIndex = child;
                if (!ApplyCorrectiveElbow(out var error))
                    throw new InvalidOperationException(spec.Label + ": " + error);
                results.Add(spec.Label + ": applied");
            }
        }
        finally
        {
            selectedLayerSelectionKey = previousLayer;
            selectedBoneIndex = previousBone;
        }
        return results.Count == 0 ? "No Elbow joints selected" : string.Join("; ", results);
    }
}
#endif
