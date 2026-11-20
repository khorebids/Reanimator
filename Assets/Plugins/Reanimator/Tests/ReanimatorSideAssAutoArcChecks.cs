#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifySideAssAutoArcMath()
    {
        VerifySideAssExteriorRecovery();
        VerifySideAssClippedJoin();
        int cases=0;
        foreach(float scale in new[]{.1f,1f,200f})foreach(float angle in new[]{0f,37f,180f})foreach(bool reverse in new[]{false,true})foreach(bool mirror in new[]{false,true})
        {
            Quaternion rotation=Quaternion.Euler(0,0,angle);Vector2 offset=new Vector2(13,-7);
            Vector2 Map(Vector2 p)=>offset+(Vector2)(rotation*(Vector3)(new Vector2(mirror?-p.x:p.x,p.y)*scale));
            var boundary=new[]{new Vector2(-2,-2),new Vector2(6,-2),new Vector2(6,2),new Vector2(-2,2)}.Select(Map).ToArray();
            if(reverse)Array.Reverse(boundary);
            Vector2 direction=rotation*(mirror?Vector3.left:Vector3.right),away=rotation*Vector3.up;
            if(!TryBuildSideAssAutoArc(boundary,offset,direction,4*scale,away,out var arc,out var error))throw new Exception(error);
            if(Vector2.Distance(arc[0],Map(new Vector2(-2,0)))>scale*.001f || Vector2.Distance(arc.Last(),Map(new Vector2(1,2)))>scale*.001f)
                throw new Exception("Auto arc chose the wrong contour side or did not stop at 1/4 length.");
            if(arc.Length!=3 || Vector2.Distance(arc[1],Map(new Vector2(-2,2)))>scale*.001f)throw new Exception("Auto arc did not follow the contour away from hips.");
            cases++;
        }
        foreach(var origin in new[]{new Vector2(-4,0),new Vector2(0,4),new Vector2(8,0)})
        {
            var boundary=new[]{new Vector2(-2,-2),new Vector2(6,-2),new Vector2(6,2),new Vector2(-2,2)};
            if(!TryBuildSideAssAutoArc(boundary,origin,Vector2.right,4,Vector2.up,out var arc,out var error) || arc.Length<2)
                throw new Exception("Clipped/outside origin must recover: "+error);
        }
        var captured=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/SideAssOutsideOrigin.json"));
        foreach(var sample in captured)
        {
            Vector2 V(Newtonsoft.Json.Linq.JToken p)=>new Vector2((float)p[0],(float)p[1]);
            var boundary=sample["boundary"].Select(V).ToArray();
            if(!TryBuildSideAssAutoArc(boundary,V(sample["origin"]),V(sample["direction"]),(float)sample["length"],V(sample["away"]),out var arc,out var error) || arc.Length<2)
                throw new Exception("Captured "+sample["leg"]+": "+error);
        }
        return "PASS: "+cases+" regular auto arcs, clipped/lateral/outside origins and both legs from the failing PSD.";
    }
    private static void VerifySideAssExteriorRecovery()
    {
        var r=new PsdLayerPreviewRenderer();
        var data=new LayerVertexData
        {
            vertices=new[]{Vector2.zero,new Vector2(2,0),new Vector2(2,2),new Vector2(0,2),new Vector2(1,0),new Vector2(1,2)},
            vertexUvs=Enumerable.Repeat(Vector2.zero,6).ToArray(),
            boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1},6).ToArray(),
            spriteBoneToRendererBone=new[]{0}
        };
        var graph=new[]{0,4,4,1,1,2,2,5,5,3,3,0,4,5};
        var exterior=r.NormalizeSideAssExterior(data,graph);
        var degree=exterior.GroupBy(i=>i).ToArray();
        if(exterior.Length!=12 || degree.Any(g=>g.Count()!=2))throw new Exception("Overlapping cap retained an internal edge in the silhouette.");
        if(!TryFindGeometryVerticesOutsideContours(data.vertices,exterior,out var outside,out var error) || outside.Count!=0)throw new Exception(error ?? "Exterior recovery discarded boundary vertices.");
    }
    private static void VerifySideAssClippedJoin()
    {
        var sample=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/SideAssClippedJoin.json"));
        var data=JsonUtility.FromJson<LayerVertexData>((string)sample["dataJson"]);
        var vertices=data.vertices.ToArray();var uv=data.vertexUvs.ToArray();var weights=data.boneWeights.ToArray();
        var exterior=sample["exterior"].Select(v=>(int)v).ToArray();
        var retained=sample["retained"].Select(v=>new Vector2Int((int)v["x"],(int)v["y"])).ToList();
        var local=sample["local"].Select(v=>(int)v).ToList();
        if(!new PsdLayerPreviewRenderer().RepairSideAssJoin(data,exterior,retained,local) || !SideAssJoinIsClosed(data,exterior))
            throw new Exception("Captured clipped thigh must produce a closed fan.");
        for(int i=0;i<vertices.Length;i++)
            if(data.vertices[i]!=vertices[i] || data.vertexUvs[i]!=uv[i] || !data.boneWeights[i].Equals(weights[i]))
                throw new Exception("Join recovery changed existing vertex attributes.");
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
