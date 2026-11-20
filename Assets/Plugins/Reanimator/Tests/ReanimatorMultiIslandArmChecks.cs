#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyMultiIslandArmGeometry()
    {
        var fixture=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/MultiIslandArm.json"));
        var renderer=new PsdLayerPreviewRenderer();
        foreach(var bone in fixture["bones"])renderer.bones.Add(JsonUtility.FromJson<BoneInfo>((string)bone));
        var data=JsonUtility.FromJson<LayerVertexData>((string)fixture["dataJson"]);
        renderer.layerInfos.Add(data.layer);renderer.vertexDataByLayerSelectionKey[BuildSelectionKey(data.layer)]=data;
        renderer.SelectLayer(data.layer);renderer.selectedBoneIndex=renderer.bones.FindIndex(b=>b.name=="l_arm_1");
        var loops=BuildAllBoundaryLoops(data.boundaryEdgePairs,data.vertices.Length);
        if(loops.Count!=2)throw new Exception("Captured arm must have two separate islands.");
        var originals=data.vertices.ToArray();var uv=data.vertexUvs.ToArray();
        foreach(var loop in loops)
        {
            var p=(data.vertices[loop[0]]+data.vertices[loop[1]])*.5f;
            if(!renderer.InsertAppliedVertexIntoLayerData(data,p,true,allowBoundarySnap:false))throw new Exception("Boundary insert failed.");
            if(BuildAllBoundaryLoops(data.boundaryEdgePairs,data.vertices.Length).Count!=2)throw new Exception("Boundary insertion erased an island.");
        }
        for(int i=0;i<originals.Length;i++)if(originals[i]!=data.vertices[i]||uv[i]!=data.vertexUvs[i])throw new Exception("Insertion changed original vertices/UVs.");
        if(!renderer.TryDeleteVerticesAndRebuild(data,new[]{loops[1][1]}))throw new Exception("Local boundary delete failed.");
        if(BuildAllBoundaryLoops(data.boundaryEdgePairs,data.vertices.Length).Count!=2)throw new Exception("Local deletion erased another island.");
        var draw=renderer.GetLayerVerticesForDraw(data);
        var joint=GetBoneBasePoint(renderer.bones[renderer.selectedBoneIndex]);
        if(!TryBuildBoundaryLoopNearPoint(data.boundaryEdgePairs,draw,joint,out var active)||active.Count<10)throw new Exception("Joint selected the tiny island.");
        data=JsonUtility.FromJson<LayerVertexData>((string)fixture["dataJson"]);
        renderer.layerInfos.Clear();renderer.layerInfos.Add(data.layer);
        renderer.vertexDataByLayerSelectionKey[BuildSelectionKey(data.layer)]=data;
        renderer.SelectLayer(data.layer);
        if(!renderer.TryBuildAutomaticElbowPlan(out _,out var beforeError))throw new Exception("Source fixture elbow invalid: "+beforeError);
        renderer.ApplySelectedAutoLimbJoints(1<<1);
        renderer.TryGetLayerVertexData(data.layer,out data);
        if(BuildAllBoundaryLoops(data.boundaryEdgePairs,data.vertices.Length).Count!=2)
            throw new Exception($"Wrist pipeline erased an island: vertices={data.vertices.Length}, triangles={data.triangles.Length}.");
        renderer.selectedBoneIndex=renderer.bones.FindIndex(b=>b.name=="l_arm_1");
        if(!renderer.TryBuildAutomaticElbowPlan(out _,out var error))throw new Exception("Wrist output breaks elbow: "+error);
        return "PASS: captured two-island arm retains both components through insertion, deletion and full wrist pipeline; original vertices/UVs preserved; elbow plan remains valid.";
    }
}
#endif
