#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class Reanimator
{
    public string VerifyOthersDraftRecovery()
    {
        string path=activeAssetPath,file=ReanimateDraftFile(path);
        string original=EncodeReanimateDraftCache(path);
        var sprites=FindReanimateOtherSprites();
        var target=sprites.First();
        var source=layerPreviewRenderer.Layers.First(l=>l!=target);
        string key=ReanimateOtherSpriteKey(target),sourceKey=ReanimateOtherSpriteKey(source);
        try
        {
            reanimateOtherDrafts[path]=new Dictionary<string,List<OtherSpriteDraft>>(StringComparer.OrdinalIgnoreCase);
            var expected=new List<OtherSpriteDraft>();
            foreach(var mode in new[]{ReanimateOtherMode.SRN,ReanimateOtherMode.SC,ReanimateOtherMode.Projector})
            {
                var draft=new OtherSpriteDraft {mode=mode,parentBone="spine",smoothRoot=false,
                    rootSourceExplicit=true,rootSourceBone="neck",projectorStretch=true};
                draft.contour.AddRange(new[]{new Vector2(1,2),new Vector2(3,4),new Vector2(5,2)});
                draft.points.AddRange(new[]{new Vector2(1,2),new Vector2(2,3),new Vector2(3,4)});
                draft.projectorSources.Add(sourceKey);expected.Add(draft);
            }
            reanimateOtherDrafts[path][key]=expected;
            reanimateOtherShoulders[path]=new HashSet<string>(StringComparer.OrdinalIgnoreCase){key};
            var saved=JObject.Parse(EncodeReanimateDraftCache(path));
            // Reimport regenerates both the target and source sprite IDs.
            ((JObject)saved["others"]).Property(key).Remove();
            saved["others"]["old-target-guid"]=JToken.FromObject(expected,Newtonsoft.Json.JsonSerializer.Create(DraftJsonSettings));
            foreach(var region in saved["others"]["old-target-guid"])region["projectorSources"]=new JArray("old-source-guid");
            saved["shoulders"]=new JArray("old-target-guid");
            foreach(var identity in saved["sprites"])
            {
                if((string)identity["key"]==key)identity["key"]="old-target-guid";
                else if((string)identity["key"]==sourceKey)identity["key"]="old-source-guid";
            }
            File.WriteAllText(file,saved.ToString());
            reanimateOtherDrafts.Remove(path);reanimateOtherDraft=null;reanimateOtherRegions=null;
            RestoreReanimateDraftCache(path);
            var recovered=reanimateOtherDrafts[path][key];
            if(!CaptureReanimateOthers(path).Any(op=>op.name==target.name && op.inheritShoulders))throw new Exception("Shoulder inheritance flag was not rebound to the recovered sprite.");
            if(!JToken.DeepEquals(JToken.FromObject(expected,Newtonsoft.Json.JsonSerializer.Create(DraftJsonSettings)),
                JToken.FromObject(recovered,Newtonsoft.Json.JsonSerializer.Create(DraftJsonSettings))))
                throw new Exception("Recovered modes/contours/points/bones/projector options differ.");
            var operations=CaptureReanimateOthers(path).Where(op=>op.name==target.name && op.mode!=ReanimateOtherMode.Skip).ToArray();
            if(operations.Length!=3 || !operations.Select(op=>op.mode).SequenceEqual(expected.Select(d=>d.mode)) ||
                operations.Any(op=>op.sources.Length!=1 || op.sources[0]!=sourceKey))
                throw new Exception("Recovered regions are not found by the actual Reanimate consumer.");
            return "PASS: SRN, SC and Projector recovered by the real layer lookup after target/source GUID changes; every setting, outline and bone point preserved.";
        }
        finally
        {
            File.WriteAllText(file,original);RestoreReanimateDraftCache(path);
        }
    }
}
#endif
