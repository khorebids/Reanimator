using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ReaniIKLegSetup
{
    public static int Configure(ReaniIK rig, IReadOnlyList<Transform> bones, Transform controls)
    {
        int count = 0;
        if (ConfigureLeg(rig.leftLeg, "l")) count++;
        if (ConfigureLeg(rig.rightLeg, "r")) count++;
        rig.SynchronizeAnimationControls();
        return count;

        Transform Find(string name) => bones.FirstOrDefault(bone => bone &&
            !bone.GetComponent<SpriteRenderer>() && string.Equals(bone.name, name, StringComparison.OrdinalIgnoreCase));

        bool ConfigureLeg(ReaniIK.Limb limb, string side)
        {
            var upper = Find(side + "_leg");
            if (!upper) return false;
            var last = Find(side + "_foot");
            if (!last || !last.IsChildOf(upper))
            {
                last = Find(side + "_leg_1") ?? Find(side + "_knee");
                if (!last || !last.IsChildOf(upper)) last = upper;
            }
            var chain = new List<Transform>();
            for (var bone = last; bone; bone = bone.parent)
            {
                chain.Add(bone);
                if (bone == upper) break;
            }
            chain.Reverse();
            // Keep the last three actual bones for a longer custom leg, without introducing body bones.
            if (chain.Count > 3) chain.RemoveRange(0, chain.Count - 3);
            var first = chain[0];
            var middle = chain.Count == 3 ? chain[1] : null;
            bool changed = limb.upper != first || limb.lower != middle || limb.tip != last;
            bool validTarget = limb.target && !limb.target.IsChildOf(first);
            if (changed) limb.RestorePose();
            var target = validTarget ? limb.target : controls.Find(side + "_foot_target");
            bool created = !target;
            if (created)
            {
                target = new GameObject(side + "_foot_target").transform;
                target.SetParent(controls, false);
            }
            if (created || changed) target.SetPositionAndRotation(last.position, last.rotation);
            limb.upper = first;
            limb.lower = middle;
            limb.tip = last;
            limb.target = target;
            limb.softnessSource = Find("spine");
            if (created || changed) { limb.MatchAuthoredBend(); limb.CaptureDefaultPose(); }
            return true;
        }
    }

    public static int Configure(ReaniIK rig)
    {
        var skeleton = rig.transform.Find("root/skeleton");
        var bones = (skeleton ? skeleton : rig.transform).GetComponentsInChildren<Transform>(true);
        var controls = rig.transform.Find("root/rigs") ?? rig.transform.Find("rigs");
        if (!controls)
        {
            controls = new GameObject("rigs").transform;
            controls.SetParent(rig.transform.Find("root") ?? rig.transform, false);
        }
        return Configure(rig, bones, controls);
    }
}
