#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Reanimator
{
    public string VerifyOtherScLocality()
    {
        var operations=CaptureReanimateOthers(activeAssetPath).Where(o=>o.mode==ReanimateOtherMode.SC).ToArray();
        foreach(var operation in operations)
            layerPreviewRenderer.VerifyOtherScRegion(operation.name,operation.contour,operation.region);
        if(operations.Length==0)throw new Exception("No actual SC regions to test.");
        return "PASS: "+operations.Length+" actual SC regions, including repeated application; no SC weight outside selected polygons.";
    }
}

public partial class PsdLayerPreviewRenderer
{
    internal void VerifyOtherScRegion(string name,Vector2[] contour,int region)
    {
        var copy=new PsdLayerPreviewRenderer();copy.cachedPath=null;copy.cachedDocumentSize=cachedDocumentSize;
        copy.layerInfos.AddRange(layerInfos);
        foreach(var bone in bones)copy.bones.Add(JsonUtility.FromJson<BoneInfo>(JsonUtility.ToJson(bone)));
        copy.RecomputeBoneWorldTransforms();
        var layer=layerInfos.First(l=>l.name==name);
        if(!TryGetLayerVertexData(layer,out var original))throw new Exception("SC mesh missing.");
        var data=CloneProjectorLayerData(original);copy.vertexDataByLayerSelectionKey[BuildSelectionKey(layer)]=data;
        for(int repeat=0;repeat<2;repeat++)
        {
            if(!copy.TryApplyOtherSprite(layer,ReanimateOtherMode.SC,Array.Empty<Vector2>(),contour,region,out var error))throw new Exception(error);
            string boneName=layer.name.Trim()+(region>0?"_"+(region+1):"")+"_SC";
            copy.TryFindBoneIndexByName(boneName,out var bone);int slot=FindSpriteBoneIndexForRendererBone(data,bone);
            var polygon=copy.MapFullBoobContourToBase(layer,contour);int outside=0;
            for(int i=0;i<data.vertices.Length;i++)
                if(!ContainsFaceMouthPoint(data.vertices[i],polygon))
                {outside++;if(GetSpriteBoneWeight(data.boneWeights[i],slot)>.0001f)throw new Exception(name+": SC escaped the selected polygon.");}
            if(outside==0)throw new Exception(name+": locality test requires an unselected part of the sprite.");
        }
    }
}
#endif
