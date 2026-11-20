#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyAssAndOptionalContours()
    {
        var r=new PsdLayerPreviewRenderer();
        if(TryGetVGenCenter(null,out _) || TryGetVGenCenter(Array.Empty<Vector2>(),out _) || TryGetVGenCenter(new[]{Vector2.zero},out _))
            throw new Exception("An unfinished contour must not have an automatic center.");
        var layer=new LayerInfo{name="body",visible=true,sourceRect=new Rect(0,0,200,100),position=Vector2.zero};
        r.layerInfos.Add(layer);
        r.bones.Add(new BoneInfo{name="hips",parentId=-1,rotation=Quaternion.identity,initialRotation=Quaternion.identity,length=20,visible=true});
        r.bones.Add(new BoneInfo{name="spine",parentId=0,position=new Vector2(100,80),initialPosition=new Vector2(100,80),rotation=Quaternion.identity,initialRotation=Quaternion.identity,length=20,visible=true});
        r.RecomputeBoneWorldTransforms();
        var points=new List<Vector2>();var triangles=new List<int>();
        for(int y=0;y<=10;y++)for(int x=0;x<=20;x++)points.Add(new Vector2(x*10,y*10));
        for(int y=0;y<10;y++)for(int x=0;x<20;x++){int a=y*21+x;triangles.AddRange(new[]{a,a+1,a+22,a,a+22,a+21});}
        var data=new LayerVertexData{layer=layer,vertices=points.ToArray(),vertexUvs=points.Select(p=>new Vector2(p.x/200,p.y/100)).ToArray(),triangles=triangles.ToArray(),
            boneWeights=points.Select(p=>new BoneWeight{boneIndex0=0,weight0=1}).ToArray(),spriteBoneToRendererBone=new[]{0},bindBoneInverseMatrices=new[]{Matrix4x4.identity}};
        BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
        r.vertexDataByLayerSelectionKey[BuildSelectionKey(layer)]=data;
        var clothing=new LayerInfo{name="clothing",visible=true,sourceRect=layer.sourceRect,position=layer.position};
        var clothingData=CloneProjectorLayerData(data);clothingData.layer=clothing;
        r.layerInfos.Add(clothing);r.vertexDataByLayerSelectionKey[BuildSelectionKey(clothing)]=clothingData;
        var originalClothing=CloneProjectorLayerData(clothingData);
        r.assLayerStableKey=BuildKey(layer.name,layer.layerId);
        foreach(var spec in new[]{(60f,r.assLeftContour),(140f,r.assRightContour)})
            for(int i=0;i<24;i++){float angle=i*Mathf.PI*2/24;spec.Item2.Add(new Vector2(spec.Item1+Mathf.Cos(angle)*25,50+Mathf.Sin(angle)*25));}
        var saved=r.ExportReanimateDraftCache();r.ClearAss(false);r.ImportReanimateDraftCache(saved);
        if(!r.HasAssContour(0)||!r.HasAssContour(1))throw new Exception("Ass draft did not survive cache round trip.");
        var outside=points.Where(p=>p.x<25||p.x>175||p.y<10||p.y>90).ToArray();
        for(int pass=0;pass<2;pass++)
        {
            if(!r.ApplyAss(out _,out var error))throw new Exception(error);
            if(!r.TryFindBoneIndexByName("ass_root",out var root)||r.bones[root].parentId!=0)throw new Exception("Ass root is not attached to hips.");
            foreach(var spec in new[]{("l_ass",new Vector2(60,50)),("r_ass",new Vector2(140,50))})
            {
                if(!r.TryFindBoneIndexByName(spec.Item1,out var bone)||(GetBoneBasePoint(r.bones[bone])-spec.Item2).sqrMagnitude>.01f)
                    throw new Exception("Ass center is not at the contour center.");
                int slot=Array.IndexOf(data.spriteBoneToRendererBone,bone);
                if(slot<0||!data.boneWeights.Any(w=>GetSpriteBoneWeight(w,slot)>.01f))throw new Exception("Ass has no skin weights.");
            }
            if(r.bones.Count!=5)throw new Exception("Repeated Ass application duplicated bones.");
            foreach(var point in outside)
            {
                int i=Array.IndexOf(data.vertices,point);
                if(i<0 || data.vertexUvs[i]!=new Vector2(point.x/200,point.y/100) || GetSpriteBoneWeight(data.boneWeights[i],0)<.999f)
                    throw new Exception("Ass changed geometry/UVs/weights outside its local region.");
            }
            if(!ReanimatorImporterGeometryAlgorithms.HasClosedSpriteOutline(data.triangles,data.vertices.Length))throw new Exception("Ass mesh is open.");
        }
        for(int pass=0;pass<2;pass++)
        {
            for(int side=0;side<2;side++)if(!r.TryInheritReanimateAss(clothing,side,out var error))throw new Exception(error);
            for(int i=0;i<originalClothing.vertices.Length;i++)
            {
                if(clothingData.vertices[i]!=originalClothing.vertices[i]||clothingData.vertexUvs[i]!=originalClothing.vertexUvs[i])
                    throw new Exception("Ass inheritance moved existing clothing vertices/UVs.");
                if(!ContainsFaceMouthPoint(originalClothing.vertices[i],r.assLeftContour) && !ContainsFaceMouthPoint(originalClothing.vertices[i],r.assRightContour) &&
                    !AreBoneWeightsApproximatelyEqual(originalClothing.boneWeights[i],clothingData.boneWeights[i]))
                    throw new Exception("Ass inheritance changed clothing weights outside contours.");
            }
            foreach(string name in new[]{"l_ass","r_ass"})
            {
                r.TryFindBoneIndexByName(name,out var bone);int slot=Array.IndexOf(clothingData.spriteBoneToRendererBone,bone);
                if(slot<0||!clothingData.boneWeights.Any(w=>GetSpriteBoneWeight(w,slot)>.01f))throw new Exception("Clothing did not inherit Ass weights.");
            }
            if(!SideAssJoinIsClosed(clothingData,originalClothing.boundaryEdgePairs))throw new Exception("Ass inheritance changed clothing silhouette.");
        }
        r.boobV2HasTargetA=r.boobV2HasTargetB=true;r.boobV2ContourADoc.AddRange(r.assLeftContour);r.boobV2ContourBDoc.AddRange(r.assRightContour);
        r.softContourBellyContour.AddRange(r.assLeftContour);
        r.ClearBoobV2();r.ClearSoftContourBellyContour();
        if(r.HasAnyReanimateChestInput||r.CaptureReanimateChestDraft()!=null||r.CaptureSoftContourBellyDraft().Length!=0||r.CaptureAssDraft()==null)
            throw new Exception("Optional chest/belly clearing erased Ass or retained disabled contours.");
        r.ClearAss(false);var empty=r.ExportReanimateDraftCache();r.ImportReanimateDraftCache(empty);
        if(r.CaptureAssDraft()!=null || r.HasAnyReanimateChestInput || r.HasSoftContourBellyContour)
            throw new Exception("Empty optional regions did not survive a cache round trip.");
        r.boobV2HasTargetA=r.boobV2HasTargetB=true;
        if(r.HasReanimateChestContours || r.CaptureReanimateChestDraft()!=null)
            throw new Exception("Nipple markers without contours must not enable chest application.");
        return "PASS: Ass automatic centers/hips and repeat application; repeated clothing inheritance preserves silhouette and exterior weights/UVs; optional contours cache independently.";
    }

    public static string VerifyOptionalMouth()
    {
        var r=new PsdLayerPreviewRenderer();var layer=new LayerInfo{name="head"};r.layerInfos.Add(layer);
        if(r.GetReanimateFaceDraftError(layer)!=null)throw new Exception("Empty Face must be optional.");
        r.faceMouthLayer=layer;r.faceMouthContours[0].AddRange(new[]{Vector2.zero,Vector2.right,Vector2.up});
        if(r.GetReanimateFaceDraftError(layer)==null)throw new Exception("Partial Face must still be validated.");
        r.DisableReanimateMouth();
        if(r.HasAnyFaceMouthContours || r.CaptureReanimateFaceDraft(layer)!=null || r.FaceMouthDrawingContour!=-1)
            throw new Exception("No Mouth did not clear/cancel all facial contours.");
        var saved=r.ExportReanimateDraftCache();r.ImportReanimateDraftCache(saved);
        r.RestoreReanimateFaceDraft(layer,null);
        if(r.GetReanimateFaceDraftError(layer)!=null || r.HasAnyFaceMouthContours)throw new Exception("Optional Face was not restored as empty.");
        return "PASS: empty Face skips validation, partial Face is rejected, No Mouth clears/cancels all contours and survives cache/restoration.";
    }
    public static string VerifyOptionalFaceHeadBinding()
    {
        foreach(string name in new[]{"face","head","Face"})
        {
            var r=new PsdLayerPreviewRenderer();
            var layer=new LayerInfo{name=name,visible=true,sourceRect=new Rect(0,0,100,100),position=Vector2.zero};r.layerInfos.Add(layer);
            r.bones.Add(new BoneInfo{name="head",parentId=-1,position=new Vector2(50,50),initialPosition=new Vector2(50,50),rotation=Quaternion.identity,initialRotation=Quaternion.identity,length=20});
            r.RecomputeBoneWorldTransforms();
            var vertices=new[]{Vector2.zero,Vector2.right*100,Vector2.one*100,Vector2.up*100};
            var uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};var triangles=new[]{0,1,2,0,2,3};
            var data=new LayerVertexData{layer=layer,pixelsPerUnit=100,pivotPixels=new Vector2(50,50),vertices=vertices.ToArray(),vertexUvs=uv.ToArray(),triangles=triangles.ToArray(),
                spriteBoneToRendererBone=new[]{999},bindBoneInverseMatrices=new[]{Matrix4x4.identity},boneWeights=new BoneWeight[4]};
            BuildEdgePairs(data.triangles,4,out data.allEdgePairs,out data.boundaryEdgePairs);
            r.vertexDataByLayerSelectionKey[BuildSelectionKey(layer)]=data;
            for(int pass=0;pass<2;pass++)
            {
                if(!r.TryBindReanimateFaceToHead(layer,out var error))throw new Exception(error);
                if(!data.vertices.SequenceEqual(vertices)||!data.vertexUvs.SequenceEqual(uv)||!data.triangles.SequenceEqual(triangles))
                    throw new Exception("No Mouth head binding changed face geometry.");
                if(!data.hasSkinning||data.spriteBoneToRendererBone.Length!=1||data.spriteBoneToRendererBone[0]!=0||data.bindBoneInverseMatrices.Length!=1||
                    data.boneWeights.Any(w=>w.boneIndex0!=0||Mathf.Abs(w.weight0-1)>.0001f))throw new Exception("No Mouth has no valid complete head binding.");
            }
            if(r.bones.Count!=1||r.HasAnyFaceMouthContours)throw new Exception("No Mouth created facial bones or contours.");
        }
        return "PASS: face/head aliases bind completely to head twice, repair stale bindings, preserve geometry/UVs and create no facial bones or contours.";
    }
}
#endif
