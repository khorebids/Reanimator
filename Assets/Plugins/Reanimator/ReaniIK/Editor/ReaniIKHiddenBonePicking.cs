#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;

// The 2D Animation bone tool ignores inactive bone GameObjects. Scene picking
// locks do not affect its custom handles, so filter its input before it lays out
// those handles. Leave ordinary SpriteSkin editors and visible skeletons alone.
[InitializeOnLoad]
internal static class ReaniIKHiddenBonePicking
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    private static readonly List<SpriteSkin> hiddenSkins = new List<SpriteSkin>();
    private static readonly List<Transform> hiddenBones = new List<Transform>();
    private static bool reportedFailure;

    static ReaniIKHiddenBonePicking()
    {
        SceneView.beforeSceneGui += BeforeSceneGUI;
    }

    internal static object GetController()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("UnityEditor.U2D.Animation.BoneGizmo");
            if (type == null) continue;
            var instance = type.GetProperty("instance", Flags)?.GetValue(null);
            return type.GetProperty("boneGizmoController", Flags)?.GetValue(instance);
        }
        return null;
    }

    private static void BeforeSceneGUI(SceneView view)
    {
        try { FilterController(GetController()); }
        catch (Exception error)
        {
            if (reportedFailure) return;
            reportedFailure = true;
            Debug.LogWarning("ReaniIK: could not exclude hidden skeleton handles: " + error.Message);
        }
    }

    internal static int FilterController(object controller)
    {
        if (controller == null) return 0;
        var type = controller.GetType();
        var skins = (HashSet<SpriteSkin>)type.GetField("m_SkinComponents", Flags).GetValue(controller);
        var cached = (HashSet<Transform>)type.GetField("m_CachedBones", Flags).GetValue(controller);
        var data = (Dictionary<Transform, Vector2>)type.GetField("m_BoneData", Flags).GetValue(controller);
        hiddenSkins.Clear();
        foreach (var skin in skins)
            if (HasHiddenSkeleton(skin)) hiddenSkins.Add(skin);
        foreach (var skin in hiddenSkins) skins.Remove(skin);

        // Remove handles cached before the selection changed or before this
        // filter was installed, as well as bones from the current skin list.
        hiddenBones.Clear();
        foreach (var bone in cached) if (IsHiddenRigBone(bone)) hiddenBones.Add(bone);
        foreach (var bone in data.Keys)
            if (IsHiddenRigBone(bone) && !hiddenBones.Contains(bone)) hiddenBones.Add(bone);
        foreach (var bone in hiddenBones) { cached.Remove(bone); data.Remove(bone); }
        return hiddenSkins.Count;
    }

    internal static bool HasHiddenSkeleton(SpriteSkin skin)
    {
        if (!skin || skin.boneTransforms == null || !skin.GetComponentInParent<ReaniIK>(true)) return false;
        bool found = false;
        foreach (var bone in skin.boneTransforms)
        {
            if (!bone) continue;
            found = true;
            if (bone.gameObject.activeInHierarchy) return false;
        }
        return found;
    }

    private static bool IsHiddenRigBone(Transform bone) => bone &&
        !bone.gameObject.activeInHierarchy && bone.GetComponentInParent<ReaniIK>(true);
}
#endif
