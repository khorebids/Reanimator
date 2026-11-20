#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifySideAssAutoArcMath()
    {
        int cases=0;
        foreach(float scale in new[]{.1f,1f,200f})foreach(float angle in new[]{0f,37f,180f})foreach(bool reverse in new[]{false,true})
        {
            Quaternion rotation=Quaternion.Euler(0,0,angle);Vector2 offset=new Vector2(13,-7);
            Vector2 Map(Vector2 p)=>offset+(Vector2)(rotation*(Vector3)(p*scale));
            var boundary=new[]{new Vector2(-2,-2),new Vector2(6,-2),new Vector2(6,2),new Vector2(-2,2)}.Select(Map).ToArray();
            if(reverse)Array.Reverse(boundary);
            Vector2 direction=rotation*Vector3.right,away=rotation*Vector3.up;
            if(!TryBuildSideAssAutoArc(boundary,offset,direction,4*scale,away,out var arc,out var error))throw new Exception(error);
            if(Vector2.Distance(arc[0],Map(new Vector2(-2,0)))>scale*.001f || Vector2.Distance(arc.Last(),Map(new Vector2(1,2)))>scale*.001f)
                throw new Exception("Auto arc chose the wrong contour side or did not stop at 1/4 length.");
            if(arc.Length!=3 || Vector2.Distance(arc[1],Map(new Vector2(-2,2)))>scale*.001f)throw new Exception("Auto arc did not follow the contour away from hips.");
            cases++;
        }
        return "PASS: "+cases+" auto arc cases: scales, rotations, reversed contour winding, back ray and 25% cut.";
    }
    public string VerifySideAssAutoArcOnLegs()
    {
        foreach(string name in new[]{"l_leg","r_leg"})
        {
            SelectLayer(layerInfos.First(l=>l.name==name));selectedBoneIndex=bones.FindIndex(b=>b.name==name);
            if(!AutoDrawSideAssArc(out var error))throw new Exception(name+": "+error);
            var bone=bones[selectedBoneIndex];Vector2 direction=bone.worldRotation*Vector3.right;
            Vector2 first=sideAssContour[0]-bone.worldPosition,last=sideAssContour.Last()-bone.worldPosition;
            if(Vector2.Dot(first,direction)>.001f || Mathf.Abs(first.x*direction.y-first.y*direction.x)>.05f ||
                Mathf.Abs(Vector2.Dot(last,direction)-bone.length*.25f)>.05f)throw new Exception(name+": generated endpoints violate the bone construction.");
        }
        return " Real PSD: auto arcs generated on both upper legs.";
    }
}
#endif
