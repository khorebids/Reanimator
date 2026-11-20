#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyPartialBellyClothing()
    {
        var raw=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/PartialBellyClothing.json"));
        var renderer=new PsdLayerPreviewRenderer();
        foreach(var b in raw["bones"])renderer.bones.Add(JsonUtility.FromJson<BoneInfo>((string)b));
        foreach(var value in raw["data"])
        {
            var d=JsonUtility.FromJson<LayerVertexData>((string)value);renderer.layerInfos.Add(d.layer);
            renderer.vertexDataByLayerSelectionKey[BuildSelectionKey(d.layer)]=d;
        }
        var target=renderer.layerInfos.First(l=>l.name=="panty");renderer.TryGetLayerVertexData(target,out var data);
        var original=CloneProjectorLayerData(data);
        var contour=raw["contour"].Select(p=>new Vector2((float)p[0],(float)p[1])).ToArray();
        if(contour.All(p=>TryFindContainingTriangle(data.triangles,data.vertices,p,out _,out _,out _,out _,out _,out _,out _,out _)))
            throw new Exception("Fixture must only partially cover belly outline.");
        BoneWeight[] first=null;
        for(int pass=0;pass<2;pass++)
        {
            if(!renderer.TryInheritReanimateBelly(target,contour,out var error))throw new Exception(error);
            for(int i=0;i<original.vertices.Length;i++)
                if(data.vertices[i]!=original.vertices[i]||data.vertexUvs[i]!=original.vertexUvs[i])throw new Exception("Belly inheritance changed original clothing vertices/UVs.");
            if(!SideAssJoinIsClosed(data,original.boundaryEdgePairs))throw new Exception("Belly inheritance changed clothing exterior.");
            int slot=Array.FindIndex(data.spriteBoneToRendererBone,i=>renderer.bones[i].name=="belly_SC");
            if(slot<0||!data.boneWeights.Any(w=>GetSpriteBoneWeight(w,slot)>.001f))throw new Exception("Partial clothing did not inherit belly_SC.");
            if(first!=null&&(first.Length!=data.boneWeights.Length||first.Where((w,i)=>!AreBoneWeightsApproximatelyEqual(w,data.boneWeights[i])).Any()))throw new Exception("Repeated belly inheritance changed weights.");
            first=data.boneWeights.ToArray();
        }
        return "PASS: captured partial belly clothing inherits belly_SC twice; exterior, original vertices/UVs and repeated weights preserved.";
    }
}
#endif
