#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyBathroomElbow()
    {
        var raw=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/BathroomElbow.json"));
        var renderer=new PsdLayerPreviewRenderer();
        foreach(var value in raw["bones"])renderer.bones.Add(JsonUtility.FromJson<BoneInfo>((string)value));
        var data=JsonUtility.FromJson<LayerVertexData>((string)raw["dataJson"]);
        renderer.layerInfos.Add(data.layer);renderer.vertexDataByLayerSelectionKey[BuildSelectionKey(data.layer)]=data;
        renderer.SelectLayer(data.layer);renderer.selectedBoneIndex=renderer.bones.FindIndex(b=>b.name=="r_arm_1");
        var original=renderer.bones[renderer.selectedBoneIndex].worldPosition;
        if(!renderer.TryBuildAutomaticElbowPlan(out var plan,out var error))throw new Exception("Captured bathroom elbow: "+error);
        if(plan.joint!=original)throw new Exception("Elbow recovery moved the authored skeleton.");
        var method=typeof(ReanimatorSkeletronModule).GetMethod("ShoulderGuideOffset",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        for(int length=0;length<=38;length++)
        {
            var result=(Vector2)method.Invoke(null,new object[]{new Vector2[length],Vector2.zero});
            if(!float.IsFinite(result.x)||!float.IsFinite(result.y))throw new Exception("Partial skeleton produced an invalid shoulder guide.");
        }
        return "PASS: captured bathroom elbow builds a valid local plan without moving its pivot; shoulder guide handles all partial skeleton lengths 0–38.";
    }
}
#endif
