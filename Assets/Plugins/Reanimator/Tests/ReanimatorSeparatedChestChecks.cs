#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string VerifySeparatedChestOnCurrentSprites()
    {
        var r=new PsdLayerPreviewRenderer();
        r.bones.Add(new BoneInfo{name="spine",parentId=-1,rotation=Quaternion.identity,initialRotation=Quaternion.identity,length=50});
        r.RecomputeBoneWorldTransforms();
        var originalBody=layerInfos.Find(l=>NormalizeAutoWeightName(l.name)=="body");
        var sourceLayers=new[]{originalBody,FindSeparatedChestSprite(0),FindSeparatedChestSprite(1)};
        if(sourceLayers.Any(l=>l==null))throw new Exception("Source requires body, l_boob and r_boob.");
        foreach(var layer in sourceLayers)
        {
            if(!TryGetLayerVertexData(layer,out var source))throw new Exception("Source mesh missing: "+layer.name);
            var copy=CloneProjectorLayerData(source);copy.layer=JsonUtility.FromJson<LayerInfo>(JsonUtility.ToJson(layer));
            ClearProjectorTargetSkinningData(copy,copy.vertices.Length);
            if(!r.TryAppendSkinningTargetBone(copy,0,out var slot))throw new Exception("Test baseline binding failed.");
            for(int i=0;i<copy.boneWeights.Length;i++)copy.boneWeights[i]=BuildSingleBoneWeight(slot);
            r.layerInfos.Add(copy.layer);r.vertexDataByLayerSelectionKey[BuildSelectionKey(copy.layer)]=copy;
        }
        r.SetReanimateChestMode(2);var contours=new Vector2[2][];var targets=new Vector2[2];
        for(int side=0;side<2;side++)
        {
            r.TryGetLayerVertexData(r.FindSeparatedChestSprite(side),out var data);
            var loops=BuildAllBoundaryLoops(data.boundaryEdgePairs,data.vertices.Length);
            var loop=loops.OrderByDescending(l=>Math.Abs(ReanimatorTriangulationAlgorithms.ComputeSignedArea(l.Select(i=>data.vertices[i]).ToArray()))).First();
            contours[side]=loop.Select(i=>data.vertices[i]).ToArray();
            if(!TryGetVGenCenter(contours[side],out targets[side]))throw new Exception("Source outline has no interior center.");
        }
        var body=r.layerInfos[0];
        r.RestoreReanimateChestDraft(body,new ReanimateChestDraft{Mode=2,LeftTarget=targets[0],RightTarget=targets[1],
            LeftContour=contours[0],RightContour=contours[1],LeftRaw=contours[0],RightRaw=contours[1]});
        for(int repeat=0;repeat<2;repeat++)if(!r.TryApplyReanimateChest(body,out var error))throw new Exception(error);
        return "PASS: current source l_boob/r_boob outlines rebuilt twice on isolated copies; source PSD and drafts unchanged.";
    }
    public static string VerifySeparatedChest()
    {
        foreach(bool mirror in new[]{false,true})
        {
            var r=new PsdLayerPreviewRenderer();
            r.bones.Add(new BoneInfo{name="spine",parentId=-1,rotation=Quaternion.identity,initialRotation=Quaternion.identity,length=50});
            r.RecomputeBoneWorldTransforms();
            r.SetReanimateChestMode(2);
            if(r.GetReanimateChestModeError()==null)throw new Exception("Missing separate sprites must report an error.");
            LayerVertexData Add(string name,Vector2 position,float size)
            {
                var layer=new LayerInfo{name=name,visible=true,position=position,sourceRect=new Rect(0,0,size,size)};
                r.layerInfos.Add(layer);var vertices=new List<Vector2>();var triangles=new List<int>();
                for(int y=0;y<=8;y++)for(int x=0;x<=8;x++)vertices.Add(position+new Vector2(x*size/8,y*size/8));
                for(int y=0;y<8;y++)for(int x=0;x<8;x++){int a=y*9+x;triangles.AddRange(new[]{a,a+1,a+10,a,a+10,a+9});}
                var data=new LayerVertexData{layer=layer,vertices=vertices.ToArray(),triangles=triangles.ToArray(),
                    vertexUvs=vertices.Select(p=>(p-position)/size).ToArray(),boneWeights=vertices.Select(p=>new BoneWeight{boneIndex0=0,weight0=1}).ToArray(),
                    spriteBoneToRendererBone=new[]{0},bindBoneInverseMatrices=new[]{Matrix4x4.identity}};
                BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
                r.vertexDataByLayerSelectionKey[BuildSelectionKey(layer)]=data;return data;
            }
            var body=Add("body",Vector2.zero,220);var originalBody=CloneProjectorLayerData(body);
            var left=Add("l_boob",new Vector2(mirror?110:10,10),80);
            if(!r.GetReanimateChestModeError().Contains("r_boob"))throw new Exception("Missing right sprite was not reported.");
            var right=Add("r_boob",new Vector2(mirror?10:110,10),80);
            var centers=new[]{left.layer.position+Vector2.one*40,right.layer.position+Vector2.one*40};
            var contours=new[]{new List<Vector2>(),new List<Vector2>()};
            for(int side=0;side<2;side++)for(int i=0;i<24;i++)
            {float angle=i*Mathf.PI*2/24;contours[side].Add(centers[side]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*30);}
            r.RestoreReanimateChestDraft(body.layer,new ReanimateChestDraft{Mode=2,LeftTarget=centers[0],RightTarget=centers[1],
                LeftContour=contours[0].ToArray(),RightContour=contours[1].ToArray(),LeftRaw=contours[0].ToArray(),RightRaw=contours[1].ToArray()});
            var cached=r.ExportReanimateDraftCache();r.reanimateChestMode=1;r.ImportReanimateDraftCache(cached);
            if(r.ReanimateChestMode!=2||r.GetSeparatedChestPreviewLayers().Count!=2)throw new Exception("Separate mode/preview sprites were not restored.");
            if(r.ResolveReanimateChestInputLayer(body.layer,2)!=left.layer||r.ResolveReanimateChestInputLayer(body.layer,3)!=right.layer)
                throw new Exception("Contours are routed to the wrong sprites.");
            for(int repeat=0;repeat<2;repeat++)
            {
                if(!r.TryApplyReanimateChest(body.layer,out var error))throw new Exception(error);
                foreach(var data in new[]{left,right})
                {
                    var center=data==left?centers[0]:centers[1];
                    int nipple=r.bones.FindIndex(b=>b.name.Contains("nipple") && (GetBoneBasePoint(b)-center).sqrMagnitude<.01f);
                    int slot=Array.IndexOf(data.spriteBoneToRendererBone,nipple);
                    if(nipple<0||slot<0||!data.boneWeights.Any(w=>GetSpriteBoneWeight(w,slot)>.99f))throw new Exception("Separate sprite has no own nipple binding.");
                    if(!ReanimatorImporterGeometryAlgorithms.HasClosedSpriteOutline(data.triangles,data.vertices.Length))throw new Exception("Separate sprite mesh is open.");
                }
                if(!body.vertices.SequenceEqual(originalBody.vertices)||!body.vertexUvs.SequenceEqual(originalBody.vertexUvs)||
                    !body.boneWeights.SequenceEqual(originalBody.boneWeights))throw new Exception("Separated chest changed the body mesh or weights.");
                if(r.bones.Count!=4)throw new Exception("Repeated separate chest duplicated bones.");
            }
            var top=Add("top",Vector2.zero,220);
            var draft=r.CaptureReanimateChestDraft();
            for(int repeat=0;repeat<2;repeat++)for(int side=0;side<2;side++)
                if(!r.TryInheritReanimateChestDraft(top.layer,body.layer,draft,side,out var error))throw new Exception(error);
            foreach(var center in centers)
            {
                int nipple=r.bones.FindIndex(b=>b.name.Contains("nipple") && (GetBoneBasePoint(b)-center).sqrMagnitude<.01f);
                int slot=Array.IndexOf(top.spriteBoneToRendererBone,nipple);
                if(slot<0||!top.boneWeights.Any(w=>GetSpriteBoneWeight(w,slot)>.01f))throw new Exception("Clothing did not inherit from separated breast sprites.");
            }
            r.SetReanimateChestMode(0);
            if(r.HasAnyReanimateChestInput||r.CaptureReanimateChestDraft()!=null)throw new Exception("None mode retained chest input.");
        }
        return "PASS: separated chest on normal/mirrored sprites twice; correct contour routing, independent nipple weights, body preserved, mode cached and missing sprites reported.";
    }
}
#endif
