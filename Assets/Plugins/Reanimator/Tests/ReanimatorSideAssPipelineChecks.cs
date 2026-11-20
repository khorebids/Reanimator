#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyNonOrientableSideAssRecovery()
    {
        var sample=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/SideAssNonOrientableJoin.json"));
        var data=JsonUtility.FromJson<LayerVertexData>((string)sample["dataJson"]);
        if(TryNormalizeSideAssTriangles(data))throw new Exception("Fixture no longer exercises non-orientable adjacency.");
        var vertices=data.vertices.ToArray();var uv=data.vertexUvs.ToArray();var weights=data.boneWeights.ToArray();
        var exterior=sample["exterior"].Select(v=>(int)v).ToArray();
        var retained=sample["retained"].Select(v=>new Vector2Int((int)v["x"],(int)v["y"])).ToList();
        var local=sample["local"].Select(v=>(int)v).ToList();
        var renderer=new PsdLayerPreviewRenderer();
        for(int repeat=0;repeat<2;repeat++)
        {
            if(!renderer.RepairSideAssJoin(data,exterior,retained,local)||!TryNormalizeSideAssTriangles(data)||!SideAssJoinIsClosed(data,exterior))
                throw new Exception("Captured seam did not recover a closed orientable mesh.");
            for(int i=0;i<vertices.Length;i++)if(data.vertices[i]!=vertices[i]||data.vertexUvs[i]!=uv[i]||!data.boneWeights[i].Equals(weights[i]))
                throw new Exception("Captured repair changed existing vertex attributes.");
        }
        return "PASS: captured non-orientable right-leg seam recovers twice; closed, orientable, existing vertex attributes intact.";
    }
    internal Action CaptureSideAssPipelineRollback()
    {
        LoadSideAss();var state=CaptureApplyFailureWorkingState();var recipe=JsonUtility.ToJson(sideAssFile);
        var path=SideAssPath;var saved=path!=null&&File.Exists(path)?File.ReadAllText(path):null;
        return ()=>{RestoreApplyFailureWorkingState(state);sideAssFile=JsonUtility.FromJson<SideAssFile>(recipe);if(saved!=null)File.WriteAllText(path,saved);};
    }
    internal void ValidateAppliedSideAssTopology()
    {
        LoadSideAss();
        foreach(var recipe in sideAssFile.regions.Where(r=>r.manualFan))
        {
            if(!vertexDataByLayerSelectionKey.TryGetValue(recipe.layer,out var data)||!TryNormalizeSideAssTriangles(data)||!SideAssJoinIsClosed(data,recipe.validatedExterior))
                throw new Exception(recipe.leg+": pipeline output is not closed and orientable.");
        }
    }
}
public partial class Reanimator
{
    public async Task<string> VerifyRepeatedReanimateGeometry()
    {
        if(reanimateRunning)throw new Exception("Another run is active.");
        var path=activeAssetPath;var renderer=layerPreviewRenderer;
        TickReanimateDraftCacheForRun(path);
        var validation=ValidateReanimateDrafts(path);if(validation!=null)throw new Exception(validation);
        if(!ReanimatorExternalPanelRegistry.TryGet("Comfimator",out var panel)||panel is not IReanimatorReanimatePanel automation)throw new Exception("Comfimator missing.");
        var face=renderer.CaptureReanimateFaceDraft(FindReanimateStepSprite(renderer.Layers,ReanimateFaceStep));
        var chest=renderer.CaptureReanimateChestDraft();var belly=renderer.CaptureSoftContourBellyDraft();
        var skeleton=ReanimatorSkeletronModule.CapturePlacement(Api);
        var limbs=reanimateLimbDrafts.TryGetValue(path,out var drafts)?CaptureReanimateLimbMasks(drafts):Array.Empty<Vector2[][]>();
        var others=CaptureReanimateOthers(path);var rollback=renderer.CaptureSideAssPipelineRollback();
        var timer=System.Diagnostics.Stopwatch.StartNew();
        reanimateRunning=true;reanimateRunPath=path;
        try
        {
            for(int repeat=0;repeat<2;repeat++)
            {
                reanimateProgress=0;reanimateDisplayedProgress=0;
                await automation.RunReanimateAsync(this,()=>{
                    if(!renderer.TryApplySkeletron(skeleton,out var error,includeHeadControls:true))throw new Exception(error);
                    return "Skeleton applied";
                },async()=>{
                    await ApplyReanimateDraftGeometryAsync(face,chest,others,belly,limbs);
                    renderer.ValidateAppliedSideAssTopology();return "Geometry verified";
                },belly.Length>=3?new Func<string>(()=>renderer.ApplyReanimateBelly(FindReanimateStepSprite(renderer.Layers,ReanimateChestStep),belly)):null);
            }
            return $"PASS: actual Reanimate geometry pipeline twice in {timer.Elapsed.TotalSeconds:F2}s; Side Ass closed/orientable on each run; PSD and prefab not written.";
        }
        finally {rollback();RestoreReanimateDrafts(face,chest);reanimateRunning=false;Repaint();}
    }
}
#endif
