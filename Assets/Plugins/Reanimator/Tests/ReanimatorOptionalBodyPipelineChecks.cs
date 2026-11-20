#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

public partial class Reanimator
{
    public async Task<string> VerifyOptionalBodyRegionsPipeline(string preparedFixture=null,string inheritTarget=null)
    {
        TickReanimateDraftCacheForRun(activeAssetPath);
        var renderer=layerPreviewRenderer;var saved=renderer.ExportReanimateDraftCache();
        var body=FindReanimateStepSprite(renderer.Layers,ReanimateChestStep);
        var hadAssFlags=reanimateOtherAss.TryGetValue(activeAssetPath,out var savedAssFlags);
        try
        {
            if(preparedFixture!=null)renderer.ImportReanimateDraftCache((Newtonsoft.Json.Linq.JObject)
                Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(preparedFixture))["renderer"]);
            var chest=renderer.CaptureReanimateChestDraft();
            if(chest==null)throw new Exception("Use a prepared fixture with chest contours to exercise automatic Ass centers.");
            await VerifyRepeatedReanimateGeometry();
            renderer.ClearBoobV2();renderer.ClearSoftContourBellyContour();renderer.ClearAss(false);
            renderer.DisableReanimateMouth();
            await VerifyRepeatedReanimateGeometry();
            renderer.RestoreAssDraft(PsdLayerPreviewRenderer.CreateAssDraft(body,chest.LeftContour,chest.RightContour));
            if(inheritTarget!=null)
            {
                var target=FindReanimateOtherSprites().Find(l=>l.name==inheritTarget);
                if(target==null)throw new Exception("Inheritance fixture sprite missing: "+inheritTarget);
                reanimateOtherAss[activeAssetPath]=new System.Collections.Generic.HashSet<string>{ReanimateOtherSpriteKey(target)};
            }
            await VerifyRepeatedReanimateGeometry();
            return "PASS: actual pipeline twice with Face/chest/belly, twice without, then twice with Ass inheritance and no Face/chest/belly; PSD/prefab not written.";
        }
        finally
        {
            renderer.ImportReanimateDraftCache(saved);
            if(hadAssFlags)reanimateOtherAss[activeAssetPath]=savedAssFlags;else reanimateOtherAss.Remove(activeAssetPath);
            SaveReanimateDraftCache();
        }
    }
    public string VerifyAssInheritanceFlagCache()
    {
        var path=activeAssetPath;var target=FindReanimateOtherSprites()[0];var key=ReanimateOtherSpriteKey(target);
        bool had=reanimateOtherAss.TryGetValue(path,out var saved);
        try
        {
            reanimateOtherAss[path]=new System.Collections.Generic.HashSet<string>{key};
            if(!CaptureReanimateOthers(path).Exists(op=>op.key==key && op.inheritAss))throw new Exception("Standalone Ass inheritance was not captured.");
            var json=Newtonsoft.Json.Linq.JObject.Parse(EncodeReanimateDraftCache(path));
            if((string)json["ass"][0]!=key)throw new Exception("Ass inheritance flag was not serialized.");
            json["sprites"]=new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject{
                ["key"]="stale-test-key",["name"]=target.name,["index"]=target.layerDataIndex});
            json["ass"]=new Newtonsoft.Json.Linq.JArray("stale-test-key");
            RebindCachedOtherSprites(json);
            if((string)json["ass"][0]!=key)throw new Exception("Ass inheritance flag was not rebound after sprite identity changed.");
            return "PASS: standalone Inherit Ass operation, serialized flag and stale sprite-key recovery.";
        }
        finally{if(had)reanimateOtherAss[path]=saved;else reanimateOtherAss.Remove(path);}
    }
}
#endif
