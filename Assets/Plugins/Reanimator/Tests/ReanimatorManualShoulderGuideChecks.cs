#if UNITY_EDITOR
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class Reanimator
{
    public string VerifyManualShoulderGuide()
    {
        string path=activeAssetPath,original=EncodeReanimateDraftCache(path);
        string file=ReanimateDraftFile(path);
        try
        {
            var skeleton=JObject.Parse(ReanimatorSkeletronModule.ExportDraft(Api));
            var manual=new Vector2(87f,31f);
            skeleton["shoulderGuide"]=new JObject{["x"]=manual.x,["y"]=manual.y};
            ReanimatorSkeletronModule.ImportDraft(Api,skeleton.ToString());
            SaveReanimateDraftCache(beforeRun:true);
            ReanimatorSkeletronModule.ClearPlacement(layerPreviewRenderer);
            layerPreviewRenderer.SkeletronShoulderGuideOffset=Vector2.zero;
            RestoreReanimateDraftCache(path);
            if(layerPreviewRenderer.SkeletronShoulderGuideOffset!=manual)throw new Exception("Manual guide did not recover from disk.");
            var roundtrip=JObject.Parse(ReanimatorSkeletronModule.ExportDraft(Api));
            if((float)roundtrip["shoulderGuide"]["x"]!=manual.x || (float)roundtrip["shoulderGuide"]["y"]!=manual.y)
                throw new Exception("Skeleton handles lost the recovered guide.");
            layerPreviewRenderer.VerifyManualShoulderWeightAxis();
            return "PASS: manual neck guide survives disk recovery, returns to skeleton handles and defines the actual shoulder-weight plane.";
        }
        finally
        {
            File.WriteAllText(file,original);File.WriteAllText(file+".bak",original);RestoreReanimateDraftCache(path);
        }
    }
}

public partial class PsdLayerPreviewRenderer
{
    internal void VerifyManualShoulderWeightAxis()
    {
        var copy=new PsdLayerPreviewRenderer();copy.cachedDocumentSize=cachedDocumentSize;
        copy.SkeletronShoulderGuideOffset=SkeletronShoulderGuideOffset;
        copy.layerInfos.AddRange(layerInfos);
        foreach(var bone in bones)copy.bones.Add(JsonUtility.FromJson<BoneInfo>(JsonUtility.ToJson(bone)));
        copy.RecomputeBoneWorldTransforms();
        foreach(var entry in vertexDataByLayerSelectionKey)
            if(entry.Value?.layer!=null && NormalizeAutoWeightName(entry.Value.layer.name)=="body")
                copy.vertexDataByLayerSelectionKey[entry.Key]=CloneProjectorLayerData(entry.Value);
        if(!copy.TryApplyShoulders(out var error))throw new Exception(error);
        if(!copy.TryFindBoneIndexByName("shoulders",out var shoulders) || !copy.TryFindBoneIndexByName("neck",out var neck))throw new Exception("Shoulders/neck missing.");
        Vector2 axis=copy.bones[shoulders].worldRotation*Vector3.right;
        if(Mathf.Abs(Vector2.Dot(axis.normalized,SkeletronShoulderGuideOffset.normalized))>.0001f ||
            Vector2.Distance(GetBoneBasePoint(copy.bones[shoulders]),GetBoneBasePoint(copy.bones[neck]))>.001f)
            throw new Exception("Shoulder construction ignored the manual guide or neck anchor.");
    }
}
#endif
