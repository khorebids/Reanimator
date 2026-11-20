#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyElbowRepeatRecovery()
    {
        var json=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/ElbowCachedRepeat.json"));
        var renderer=new PsdLayerPreviewRenderer();
        renderer.cachedDocumentSize=new Vector2Int((int)json["width"],(int)json["height"]);
        renderer.bonesTreatAsWorldSpace=(bool)json["world"];renderer.rotateLocalBoneOffsetsByParent=(bool)json["rotate"];
        foreach(var item in json["bones"])renderer.bones.Add(JsonUtility.FromJson<BoneInfo>((string)item));
        renderer.RecomputeBoneWorldTransforms();
        var data=JsonUtility.FromJson<LayerVertexData>((string)json["dataJson"]);
        data.deformedVertices=null;data.skinMatrices=null;data.lastPoseVersion=-1;
        renderer.layerInfos.Add(data.layer);renderer.vertexDataByLayerSelectionKey[BuildSelectionKey(data.layer)]=data;
        renderer.SelectLayer(data.layer);
        if(!renderer.TryFindBoneIndexByName("l_arm_1",out renderer.selectedBoneIndex))throw new Exception("Captured elbow bone missing.");
        var before=CloneProjectorLayerData(data);
        for(int repeat=0;repeat<2;repeat++)
        {
            if(!renderer.ApplyCorrectiveElbow(out var error))throw new Exception(error);
            if(data.vertices.Length<before.vertices.Length)throw new Exception("Repeated elbow deleted source vertices.");
            for(int i=0;i<before.vertices.Length;i++)
                if(data.vertexUvs[i]!=before.vertexUvs[i])throw new Exception("Repeated elbow changed original UVs.");
            if(!SideAssJoinIsClosed(data,data.boundaryEdgePairs))throw new Exception("Repeated elbow left an open/nonmanifold mesh.");
        }
        return "PASS: cached source elbow applies twice, retains every original vertex/UV and produces a closed mesh.";
    }
}
#endif
