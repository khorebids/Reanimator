#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    [NonSerialized] private string reanimateDraftCachePath, reanimateDraftCacheJson;
    [NonSerialized] private double reanimateDraftCacheNext;
    [NonSerialized] private int reanimateDraftCacheGeneration = -1;
    [NonSerialized] private PsdLayerPreviewRenderer reanimateDraftCacheRenderer;
    internal static readonly JsonSerializerSettings DraftJsonSettings = new()
    { Converters = new List<JsonConverter> { new DraftVectorConverter() } };
    private sealed class DraftVectorConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(Vector2);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        { var p=(Vector2)value; writer.WriteStartArray();writer.WriteValue(p.x);writer.WriteValue(p.y);writer.WriteEndArray(); }
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        { var p=JArray.Load(reader);return new Vector2((float)p[0],(float)p[1]); }
    }
    private static string ReanimateDraftFile(string path)
    {
        var guid=AssetDatabase.AssetPathToGUID(path);
        return string.IsNullOrEmpty(guid)?null:Path.Combine("UserSettings","ReanimatorDrafts",guid+".json");
    }
    private void TickReanimateDraftCache()
    {
        if(reanimateRunning || layerPreviewRenderer==null || string.IsNullOrEmpty(activeAssetPath))return;
        if(layerPreviewRenderer.IsPreviewCacheRefreshInProgress || !layerPreviewRenderer.IsPreviewCacheReady(activeAssetPath))
        {
            // Recovery cannot depend on the window being attended: after a
            // reload the ordinary preview pump may be paused before it is ready.
            if(!layerPreviewRenderer.IsPreviewCacheRefreshInProgress)layerPreviewRenderer.RequestPreviewCacheRefresh(activeAssetPath);
            layerPreviewRenderer.TickPreviewCacheRefresh();
            if(layerPreviewRenderer.IsPreviewCacheRefreshInProgress || !layerPreviewRenderer.IsPreviewCacheReady(activeAssetPath))return;
        }
        if(reanimateDraftCachePath!=activeAssetPath || reanimateDraftCacheRenderer!=layerPreviewRenderer || reanimateDraftCacheGeneration!=layerPreviewRenderer.DraftCacheGeneration)
        {
            reanimateDraftCachePath=activeAssetPath;reanimateDraftCacheJson=null;
            RestoreReanimateDraftCache(activeAssetPath);
            reanimateDraftCacheGeneration=layerPreviewRenderer.DraftCacheGeneration;
            reanimateDraftCacheRenderer=layerPreviewRenderer;
        }
        if(EditorApplication.timeSinceStartup<reanimateDraftCacheNext)return;
        reanimateDraftCacheNext=EditorApplication.timeSinceStartup+.5;
        SaveReanimateDraftCache();
    }
    private string EncodeReanimateDraftCache(string path)
    {
        var serializer=JsonSerializer.Create(DraftJsonSettings);
        var root=new JObject { ["version"]=1, ["guid"]=AssetDatabase.AssetPathToGUID(path),
            ["renderer"]=layerPreviewRenderer.ExportReanimateDraftCache(),
            ["skeleton"]=ReanimatorSkeletronModule.ExportDraft(Api) };
        root["sprites"]=new JArray(layerPreviewRenderer.Layers.Select(layer=>new JObject {
            ["key"]=ReanimateOtherSpriteKey(layer),["name"]=layer.name,["index"]=layer.layerDataIndex }));
        Add("limbs",reanimateLimbDrafts);Add("others",reanimateOtherDrafts);
        Add("boobs",reanimateOtherBoobs);Add("belly",reanimateOtherBelly);
        Add("ass",reanimateOtherAss);
        Add("shoulders",reanimateOtherShoulders);
        return root.ToString(Formatting.None);
        void Add<T>(string key,Dictionary<string,T> dictionary)
        { if(dictionary.TryGetValue(path,out var value))root[key]=JToken.FromObject(value,serializer); }
    }
    private void TickReanimateDraftCacheForRun(string path)
    {
        if(reanimateDraftCachePath!=path || reanimateDraftCacheRenderer!=layerPreviewRenderer || reanimateDraftCacheGeneration!=layerPreviewRenderer.DraftCacheGeneration)
            RestoreReanimateDraftCache(path);
    }
    private void SaveReanimateDraftCache(bool beforeRun = false)
    {
        var path=activeAssetPath;
        // A window disabled during startup/reload has not recovered its inputs
        // yet. Never replace a valid file with that transient empty state.
        if(!beforeRun && reanimateDraftCachePath!=path)return;
        if(!beforeRun && reanimateDraftCacheRenderer!=layerPreviewRenderer)return;
        if(!beforeRun && layerPreviewRenderer!=null && reanimateDraftCacheGeneration!=layerPreviewRenderer.DraftCacheGeneration)return;
        if((reanimateRunning && !beforeRun) || layerPreviewRenderer==null || string.IsNullOrEmpty(path) ||
            layerPreviewRenderer.IsPreviewCacheRefreshInProgress || !layerPreviewRenderer.IsPreviewCacheReady(path))return;
        if(beforeRun && (reanimateDraftCachePath!=path || reanimateDraftCacheRenderer!=layerPreviewRenderer || reanimateDraftCacheGeneration!=layerPreviewRenderer.DraftCacheGeneration))
            RestoreReanimateDraftCache(path);
        var file=ReanimateDraftFile(path);if(file==null)return;
        try
        {
            var json=EncodeReanimateDraftCache(path);
            if(json==reanimateDraftCacheJson)return;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file+".tmp",json);
            if(File.Exists(file))File.Replace(file+".tmp",file,file+".bak");
            else File.Move(file+".tmp",file);
            reanimateDraftCacheJson=json;
            reanimateDraftCachePath=path;
            reanimateDraftCacheGeneration=layerPreviewRenderer.DraftCacheGeneration;
            reanimateDraftCacheRenderer=layerPreviewRenderer;
        }
        catch(Exception e){Debug.LogWarning("Reanimator draft cache: "+e.Message);}
    }
    private void RestoreReanimateDraftCache(string path)
    {
        var file=ReanimateDraftFile(path);if(file==null)return;
        foreach(var candidate in new[]{file,file+".bak"})
        {
            if(!File.Exists(candidate))continue;
            try
            {
                var root=JObject.Parse(File.ReadAllText(candidate));
                if((int?)root["version"]!=1 || (string)root["guid"]!=AssetDatabase.AssetPathToGUID(path))continue;
                RebindCachedOtherSprites(root);
                var serializer=JsonSerializer.Create(DraftJsonSettings);
                Read("limbs",reanimateLimbDrafts);Read("others",reanimateOtherDrafts);
                Read("boobs",reanimateOtherBoobs);Read("belly",reanimateOtherBelly);
                Read("ass",reanimateOtherAss);
                Read("shoulders",reanimateOtherShoulders);
                // Canvas references must point into the recovered dictionaries,
                // not the discarded objects from before the preview refresh.
                reanimateOtherDraft=null;reanimateOtherRegions=null;
                layerPreviewRenderer.ImportReanimateDraftCache((JObject)root["renderer"]);
                ReanimatorSkeletronModule.ImportDraft(Api,(string)root["skeleton"]);
                reanimateDraftCacheJson=EncodeReanimateDraftCache(path);
                reanimateDraftCachePath=path;
                reanimateDraftCacheGeneration=layerPreviewRenderer.DraftCacheGeneration;
                reanimateDraftCacheRenderer=layerPreviewRenderer;
                return;
                void Read<T>(string key,Dictionary<string,T> dictionary)
                { if(root[key]!=null)dictionary[path]=root[key].ToObject<T>(serializer);else dictionary.Remove(path); }
            }
            catch(Exception e){Debug.LogWarning("Reanimator draft recovery: "+e.Message);}
        }
    }
    private void RebindCachedOtherSprites(JObject root)
    {
        if(root["sprites"] is not JArray sprites)return;
        var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var saved in sprites)
        {
            string old=(string)saved["key"],name=(string)saved["name"];
            var matches=layerPreviewRenderer.Layers.Where(l=>string.Equals(l.name,name,StringComparison.OrdinalIgnoreCase)).ToArray();
            var current=matches.Length==1?matches[0]:matches.FirstOrDefault(l=>l.layerDataIndex==(int?)saved["index"]);
            if(current!=null && !string.IsNullOrEmpty(old))map[old]=ReanimateOtherSpriteKey(current);
        }
        if(root["others"] is JObject others)
        {
            var remapped=new JObject();
            foreach(var entry in others.Properties())
            {
                var key=Map(entry.Name);
                if(remapped[key] is JArray previous && entry.Value is JArray incoming)
                    foreach(var region in incoming)previous.Add(region.DeepClone());
                else remapped[key]=entry.Value.DeepClone();
            }
            foreach(var region in remapped.Properties().SelectMany(p=>p.Value))
                if(region["projectorSources"] is JArray sources)
                    region["projectorSources"]=new JArray(sources.Select(source=>Map((string)source)).Distinct(StringComparer.OrdinalIgnoreCase));
            root["others"]=remapped;
        }
        foreach(var name in new[]{"boobs","belly","shoulders","ass"})
            if(root[name] is JArray flags)root[name]=new JArray(flags.Select(flag=>Map((string)flag)).Distinct(StringComparer.OrdinalIgnoreCase));
        string Map(string key)=>key!=null && map.TryGetValue(key,out var next)?next:key;
    }
    private void DeleteReanimateDraftCache(string path)
    {
        var file=ReanimateDraftFile(path);if(file==null)return;
        foreach(var candidate in new[]{file,file+".bak",file+".tmp"})if(File.Exists(candidate))File.Delete(candidate);
        reanimateDraftCachePath=path;reanimateDraftCacheJson=null;
    }
}

public partial class PsdLayerPreviewRenderer
{
    [NonSerialized] internal int DraftCacheGeneration;
    // Only user inputs: never transient controls, generated geometry or live LayerInfo references.
    private static readonly string[] ReanimateDraftFields={"faceMouthContours","softContourBellyContour",
        "boobV2HasTargetA","boobV2HasTargetB","boobV2TargetADoc","boobV2TargetBDoc",
        "boobV2RawContourADoc","boobV2RawContourBDoc","boobV2ContourADoc","boobV2ContourBDoc","boobV2LayerStableKey","reanimateChestMode",
        "assLeftContour","assRightContour","assLeftRaw","assRightRaw","assLayerStableKey","assDensity","assSmoothing"};
    internal JObject ExportReanimateDraftCache()
    {
        var json=new JObject();var serializer=JsonSerializer.Create(Reanimator.DraftJsonSettings);
        foreach(var name in ReanimateDraftFields)
        {
            var value=GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(this);
            json[name]=value==null?JValue.CreateNull():JToken.FromObject(value,serializer);
        }
        json["faceLayer"]=faceMouthLayer?.name;
        json["bellyParentRoot"]=SkeletronBellyParentRoot;
        json["shoulderGuide"]=JToken.FromObject(SkeletronShoulderGuideOffset,serializer);
        json["sideAssTargets"]=SideAssAllInTargets;
        return json;
    }
    internal void ImportReanimateDraftCache(JObject json)
    {
        if(json==null)return;
        if(json["reanimateChestMode"]==null)reanimateChestMode=1;
        if(json["assLeftContour"]==null)ClearAss(false);
        var serializer=JsonSerializer.Create(Reanimator.DraftJsonSettings);
        foreach(var name in ReanimateDraftFields)
        {
            if(json[name]==null)continue;
            var field=GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var next=json[name].ToObject(field.FieldType,serializer);
            if(field.IsInitOnly && field.GetValue(this) is System.Collections.IList list)
            {
                if(list.IsFixedSize){var array=(Array)next;for(int i=0;i<list.Count;i++)list[i]=array.GetValue(i);}
                else {list.Clear();foreach(var value in (System.Collections.IEnumerable)next)list.Add(value);}
            }
            else field.SetValue(this,next);
        }
        faceMouthLayer=layerInfos.Find(layer=>layer.name==(string)json["faceLayer"]);
        if(json["bellyParentRoot"]!=null)SkeletronBellyParentRoot=(bool)json["bellyParentRoot"];
        if(json["shoulderGuide"]!=null)SkeletronShoulderGuideOffset=json["shoulderGuide"].ToObject<Vector2>(serializer);
        if(json["sideAssTargets"]!=null)SideAssAllInTargets=(int)json["sideAssTargets"];
    }
}
#endif
