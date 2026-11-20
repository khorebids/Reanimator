#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public static class ReanimatorShoulderPlacementChecks
{
    public static string Run()
    {
        var type=typeof(Reanimator).Assembly.GetType("ReanimatorSkeletronModule");
        var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        var build=type.GetMethod("BuildBoneSpecs",flags);
        var template=(Vector2[])type.GetMethod("BuildPoints",flags).Invoke(null,new object[]{new Rect(0,0,1000,1000)});
        int cases=0;
        for(int count=0;count<=template.Length;count++)
        foreach(bool extras in new[]{false,true})
        {
            var points=template.Take(count).ToArray();
            var result=(System.Collections.ICollection)build.Invoke(null,new object[]{points,new Rect(0,0,1000,1000),extras,extras});
            if(count<6 && result.Count!=0)throw new Exception("An empty/incomplete torso must not count as placement.");
            if(count>=6 && result.Count==0)throw new Exception("A complete torso was discarded.");
            if(!points.SequenceEqual(template.Take(count)))throw new Exception("Capturing partial placement moved authored points.");
            cases++;
        }
        return "PASS: "+cases+" empty/partial/full placement cases with optional limbs; no index exceptions, authored points preserved.";
    }
}
#endif
