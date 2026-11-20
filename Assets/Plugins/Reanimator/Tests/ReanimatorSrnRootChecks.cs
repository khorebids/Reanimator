#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifySrnRootParentSelection()
    {
        var r=new PsdLayerPreviewRenderer();r.bonesTreatAsWorldSpace=true;
        foreach(var name in new[]{"hips","spine","head"})
        {
            int i=r.bones.Count;var p=new Vector2(0,i*20);
            r.bones.Add(new BoneInfo{index=i,name=name,parentId=i-1,position=p,initialPosition=p,rotation=Quaternion.identity,initialRotation=Quaternion.identity,length=10,visible=true});
        }
        r.RecomputeBoneWorldTransforms();
        var layer=new LayerInfo{name="srn_root_fixture",layerId=9191,sourceRect=new Rect(0,0,20,10),textureRect=new Rect(0,0,20,10)};
        var data=new LayerVertexData{layer=layer,vertices=new[]{new Vector2(0,0),new Vector2(10,0),new Vector2(20,0),new Vector2(0,10),new Vector2(10,10),new Vector2(20,10),new Vector2(5,5),new Vector2(15,5)},triangles=new[]{0,1,6,1,4,6,4,3,6,3,0,6,1,2,7,2,5,7,5,4,7,4,1,7},spriteBoneToRendererBone=new[]{0,1,2},hasSkinning=true};
        data.vertexUvs=data.vertices.Select(p=>new Vector2(p.x/20,p.y/10)).ToArray();
        data.boneWeights=data.vertices.Select(p=>new BoneWeight{boneIndex0=0,weight0=1}).ToArray();
        data.bindBoneInverseMatrices=r.BuildBindBoneInverseMatrices(data.spriteBoneToRendererBone);
        BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
        r.layerInfos.Add(layer);r.vertexDataByLayerSelectionKey[BuildSelectionKey(layer)]=data;
        var points=new[]{new Vector2(5,5),new Vector2(10,5),new Vector2(15,5)};
        var contour=new[]{new Vector2(0,0),new Vector2(20,0),new Vector2(20,10),new Vector2(0,10)};
        int first=-1;
        foreach(var name in new[]{"head","spine","head"})
        {
            if(!r.TryApplyOtherSprite(layer,ReanimateOtherMode.SRN,points,contour,0,out var error,name))throw new Exception(error);
            r.TryFindBoneIndexByName("srn_root_fixture_SRN_1",out var chain);
            if(first<0)first=chain;
            r.TryFindBoneIndexByName(name,out var parent);
            if(chain!=first || r.bones.Count!=5 || r.bones[chain].parentId!=parent)throw new Exception("SRN ignored the selected parent or duplicated the chain on reparenting.");
            if(WeightAtRoot(parent)<.999f || name!="hips" && WeightAtRoot(0)>.0001f)throw new Exception("SRN root still blends to hips instead of its selected parent.");
        }
        if(!r.TryApplyOtherSprite(layer,ReanimateOtherMode.SRN,points,contour,0,out var explicitError,"head",true,"hips") || WeightAtRoot(0)<.999f)
            throw new Exception("An explicitly selected blend source was not honored: "+explicitError);
        if(!r.TryApplyOtherSprite(layer,ReanimateOtherMode.SRN,points,contour,0,out var disabledError,"head",false) || WeightAtRoot(first)<.999f)
            throw new Exception("Disabling root smoothing did not preserve the first chain bone: "+disabledError);
        return "PASS: head/spine/head parent and root-weight selection, stable reparenting, explicit hips override and smoothing-off behavior.";

        float WeightAtRoot(int bone)
        {
            int index=Array.FindIndex(data.vertices,p=>Vector2.Distance(p,points[0])<.001f);
            if(index<0)throw new Exception("SRN fixture root vertex disappeared.");
            var w=data.boneWeights[index];
            float At(int slot,float weight)=>data.spriteBoneToRendererBone[slot]==bone?weight:0;
            return At(w.boneIndex0,w.weight0)+At(w.boneIndex1,w.weight1)+At(w.boneIndex2,w.weight2)+At(w.boneIndex3,w.weight3);
        }
    }
}
#endif
