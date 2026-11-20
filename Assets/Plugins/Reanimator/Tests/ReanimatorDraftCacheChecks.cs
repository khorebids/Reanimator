#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

public partial class Reanimator
{
    public string VerifyReanimateDraftRecovery()
    {
        var path=activeAssetPath;
        var original=EncodeReanimateDraftCache(path);
        var file=ReanimateDraftFile(path);
        var inputs=JObject.Parse(original);
        if(JObject.Parse((string)inputs["skeleton"])["points"].Count()<22 ||
            inputs["renderer"]["faceMouthContours"].Any(c=>c.Count()<3) || inputs["others"]==null || inputs["limbs"]==null)
            throw new Exception("Seed the real skeleton, face, SC and Others first.");
        SaveReanimateDraftCache(beforeRun:true);
        try
        {
            ClearInputs();RestoreReanimateDraftCache(path);Check();
            // A same-PSD refresh used to clear inputs without changing the path,
            // so the cache never reloaded and then saved the empty state.
            ClearInputs();layerPreviewRenderer.DraftCacheGeneration++;
            TickReanimateDraftCache();Check();
            // Interrupted/corrupt writes must recover the last valid backup.
            File.WriteAllText(file+".bak",original);File.WriteAllText(file,"{broken");
            ClearInputs();RestoreReanimateDraftCache(path);Check();
            // A different PSD GUID cannot supply this asset's drafts.
            var foreign=JObject.Parse(original);foreign["guid"]="another-asset";
            File.WriteAllText(file,foreign.ToString());
            ClearInputs();RestoreReanimateDraftCache(path);Check();
            return "PASS: skeleton, face/chest/belly contours, limb SC, Others and inheritance flags restored; corrupt primary and foreign GUID recover valid backup.";
        }
        finally
        {
            File.WriteAllText(file,original);
            RestoreReanimateDraftCache(path);
        }
        void ClearInputs()
        {
            layerPreviewRenderer.ClearFaceMouthContours();layerPreviewRenderer.ClearBoobV2();
            layerPreviewRenderer.ClearSoftContourBellyContour();ReanimatorSkeletronModule.ClearPlacement(layerPreviewRenderer);
            reanimateLimbDrafts.Remove(path);reanimateOtherDrafts.Remove(path);
            reanimateOtherBoobs.Remove(path);reanimateOtherBelly.Remove(path);
            reanimateOtherShoulders.Remove(path);
        }
        void Check()
        {
            if(!JToken.DeepEquals(JObject.Parse(original),JObject.Parse(EncodeReanimateDraftCache(path))))
                throw new Exception("Recovered drafts differ from the saved inputs.");
        }
    }
}
#endif
