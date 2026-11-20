#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;

public static class ReanimatorPrefabArmLayers
{
    // Called after skin binding, so the rear renderer shares the exact same skeleton.
    public static int Configure(ReaniIK rig)
    {
        rig.leftArmMask.Release();
        rig.rightArmMask.Release();
        // Every fabrication starts with both front arm layers hidden by the mask.
        rig.leftArmMask.amount = 1f;
        rig.rightArmMask.amount = 1f;
        var renderers = rig.GetComponentsInChildren<SpriteRenderer>(true);
        Resolve(rig.leftArmMask, rig.leftArm);
        Resolve(rig.rightArmMask, rig.rightArm);
        var originals = renderers.Where(renderer => renderer != rig.leftArmMask.backRenderer &&
            renderer != rig.rightArmMask.backRenderer).ToArray();
        if (originals.Length == 0) return 0;
        var bodyAndLegs = originals.Where(renderer => renderer.sprite &&
            (string.Equals(renderer.sprite.name, "body", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(renderer.sprite.name, "l_leg", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(renderer.sprite.name, "r_leg", StringComparison.OrdinalIgnoreCase))).ToArray();
        int min = (bodyAndLegs.Length > 0 ? bodyAndLegs : originals).Min(SourceOrder);
        // Reserve an actual slot above lower artwork. min - 1 can equal the
        // background order, making the rear arms disappear depending on tie order.
        var lower = originals.Where(renderer => SourceOrder(renderer) < min).ToArray();
        if (lower.Length > 0)
        {
            int shift = Mathf.Max(0, lower.Max(SourceOrder) + 2 - min);
            if (shift > 0)
            {
                foreach (var renderer in originals)
                {
                    int order = SourceOrder(renderer);
                    if (order < min) continue;
                    if (renderer == rig.leftArmMask.frontRenderer) rig.leftArmMask.sourceSortingOrder = order + shift;
                    else if (renderer == rig.rightArmMask.frontRenderer) rig.rightArmMask.sourceSortingOrder = order + shift;
                    else renderer.sortingOrder = order + shift;
                }
                min += shift;
            }
        }
        int max = originals.Max(SourceOrder);
        int added = 0;
        Build(rig.leftArmMask);
        Build(rig.rightArmMask);
        return added;

        int SourceOrder(SpriteRenderer renderer)
        {
            if (renderer == rig.leftArmMask.frontRenderer) return rig.leftArmMask.sourceSortingOrder;
            if (renderer == rig.rightArmMask.frontRenderer) return rig.rightArmMask.sourceSortingOrder;
            return renderer.sortingOrder;
        }

        void Resolve(ReaniIKArmMask mask, ReaniIK.Limb limb)
        {
            if (!limb.upper) return;
            if (!mask.frontRenderer)
                mask.frontRenderer = renderers.FirstOrDefault(renderer => renderer.sprite &&
                    renderer.name == limb.upper.name &&
                    string.Equals(renderer.sprite.name, limb.upper.name, StringComparison.OrdinalIgnoreCase));
            if (!mask.frontRenderer) return;
            if (!mask.sourceOrderCaptured)
            {
                mask.sourceSortingOrder = mask.frontRenderer.sortingOrder;
                mask.sourceOrderCaptured = true;
            }
            if (!mask.backRenderer)
            {
                var existing = mask.frontRenderer.transform.parent.Find(mask.frontRenderer.name + "_back");
                if (existing) mask.backRenderer = existing.GetComponent<SpriteRenderer>();
            }
        }

        void Build(ReaniIKArmMask mask)
        {
            var front = mask.frontRenderer;
            if (!front) return;
            var skin = front.GetComponent<SpriteSkin>();
            if (!skin) throw new InvalidOperationException("Arm sprite has no SpriteSkin: " + front.name);
            if (!mask.backRenderer)
            {
                var copy = UnityEngine.Object.Instantiate(front.gameObject, front.transform.parent, false);
                copy.name = front.name + "_back";
                mask.backRenderer = copy.GetComponent<SpriteRenderer>();
                added++;
            }
            var back = mask.backRenderer;
            back.transform.SetParent(front.transform.parent, false);
            back.transform.localPosition = front.transform.localPosition;
            back.transform.localRotation = front.transform.localRotation;
            back.transform.localScale = front.transform.localScale;
            EditorUtility.CopySerialized(front, back);
            var backSkin = back.GetComponent<SpriteSkin>();
            if (!backSkin) backSkin = back.gameObject.AddComponent<SpriteSkin>();
            backSkin.SetRootBone(skin.rootBone);
            backSkin.SetBoneTransforms(skin.boneTransforms.ToArray());
            backSkin.alwaysUpdate = skin.alwaysUpdate;
            backSkin.enabled = skin.enabled;
            back.gameObject.SetActive(front.gameObject.activeSelf);
            front.sortingOrder = max + 1;
            back.sortingOrder = min - 1;
        }
    }
}
#endif
