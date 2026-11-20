#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public static class ReaniIKHiddenBonePickingChecks
{
    private sealed class ControllerFixture
    {
        public HashSet<SpriteSkin> m_SkinComponents = new HashSet<SpriteSkin>();
        public HashSet<Transform> m_CachedBones = new HashSet<Transform>();
        public Dictionary<Transform, Vector2> m_BoneData = new Dictionary<Transform, Vector2>();
    }

    public static string Run()
    {
        var root = new GameObject("Hidden picking regression");
        root.SetActive(false);
        var unrelated = new GameObject("Ordinary skeleton");
        try
        {
            root.AddComponent<ReaniIK>().enabled = false;
            var hidden = CreateSkin(root.transform, "hidden", false);
            var visible = CreateSkin(root.transform, "visible", true);
            var ordinary = CreateSkin(unrelated.transform, "ordinary", false);
            root.SetActive(true);
            var controller = new ControllerFixture();
            foreach (var skin in new[] { hidden, visible, ordinary })
            {
                controller.m_SkinComponents.Add(skin);
                controller.m_CachedBones.Add(skin.boneTransforms[0]);
                controller.m_BoneData.Add(skin.boneTransforms[0], Vector2.one);
            }
            if (ReaniIKHiddenBonePicking.FilterController(controller) != 1 ||
                controller.m_SkinComponents.Contains(hidden) ||
                controller.m_CachedBones.Contains(hidden.boneTransforms[0]) ||
                controller.m_BoneData.ContainsKey(hidden.boneTransforms[0]))
                throw new Exception("Hidden skeleton retained a selectable bone handle.");
            if (controller.m_SkinComponents.Count != 2 || controller.m_CachedBones.Count != 2 || controller.m_BoneData.Count != 2)
                throw new Exception("Visible or unrelated skeleton was filtered.");
            if (ReaniIKHiddenBonePicking.FilterController(controller) != 0)
                throw new Exception("Repeated filtering is not stable.");
            if (ReaniIKHiddenBonePicking.GetController() == null)
                throw new Exception("Installed 2D Animation bone controller could not be reached.");
            return "PASS: hidden ReaniIK skins and stale bone hit targets removed; visible and ordinary skeletons preserved; repeat stable; installed Unity controller reachable.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(unrelated);
        }
    }

    private static SpriteSkin CreateSkin(Transform parent, string name, bool activeBone)
    {
        var bone = new GameObject(name + " bone").transform;
        bone.SetParent(parent, false);
        bone.gameObject.SetActive(activeBone);
        var sprite = new GameObject(name);
        sprite.transform.SetParent(parent, false);
        sprite.AddComponent<SpriteRenderer>();
        var skin = sprite.AddComponent<SpriteSkin>();
        skin.SetBoneTransforms(new[] { bone });
        return skin;
    }
}
#endif
