using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ReaniIKArmSetup
{
    public static int Configure(ReaniIK rig, IReadOnlyList<Transform> bones, Transform controls)
    {
        int count=0;
        Setup(rig.leftArm,"l",false);Setup(rig.rightArm,"r",false);
        Setup(rig.leftArm2,"l",true);Setup(rig.rightArm2,"r",true);
        rig.SynchronizeAnimationControls();
        return count;
        Transform Find(string name)=>bones.FirstOrDefault(b=>b && !b.GetComponent<SpriteRenderer>() && string.Equals(b.name,name,StringComparison.OrdinalIgnoreCase));
        void Setup(ReaniIK.Limb limb,string side,bool second)
        {
            var upper=Find(side+(second?"_arm_2":"_arm"));if(!upper)return;
            var last=Find(side+(second?"_hand_2":"_hand"));
            if(!last || !last.IsChildOf(upper))
            {
                last=Find(side+(second?"_arm_3":"_arm_1")) ?? Find(side+(second?"_forearm_2":"_forearm")) ?? Find(side+(second?"_elbow_2":"_elbow"));
                if(!last || !last.IsChildOf(upper))last=upper;
            }
            var chain=new List<Transform>();
            for(var bone=last;bone;bone=bone.parent){chain.Add(bone);if(bone==upper)break;}
            chain.Reverse();if(chain.Count>3)chain.RemoveRange(0,chain.Count-3);
            var first=chain[0];var middle=chain.Count==3?chain[1]:null;
            bool changed=limb.upper!=first || limb.lower!=middle || limb.tip!=last;
            if(changed)limb.RestorePose();
            string name=side+(second?"_hand_2_target":"_hand_target");
            var target=limb.target && !limb.target.IsChildOf(first)?limb.target:controls.Find(name);
            bool created=!target;
            if(created){target=new GameObject(name).transform;target.SetParent(controls,false);}
            if(created || changed)target.SetPositionAndRotation(last.position,last.rotation);
            limb.upper=first;limb.lower=middle;limb.tip=last;limb.target=target;limb.softnessSource=Find("spine");
            if(created || changed){limb.MatchAuthoredBend();limb.CaptureDefaultPose();}
            count++;
        }
    }
    public static int Configure(ReaniIK rig)
    {
        var skeleton=rig.transform.Find("root/skeleton");
        var bones=(skeleton?skeleton:rig.transform).GetComponentsInChildren<Transform>(true);
        var controls=rig.transform.Find("root/rigs") ?? rig.transform.Find("rigs");
        if(!controls){controls=new GameObject("rigs").transform;controls.SetParent(rig.transform.Find("root")??rig.transform,false);}
        return Configure(rig,bones,controls);
    }
}
