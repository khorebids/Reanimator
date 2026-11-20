#if UNITY_EDITOR
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

public partial class Reanimator
{
    public string VerifyChestNavigationCache()
    {
        var original=layerPreviewRenderer.ExportReanimateDraftCache();
        if((int)original["reanimateChestMode"]!=2 || original["boobV2ContourADoc"].Count()<3 || original["boobV2ContourBDoc"].Count()<3)
            throw new Exception("Prepare separate chest contours first.");
        var mode=previewMode;
        try
        {
            for(int pass=0;pass<2;pass++)
            {
                SetPreviewMode(PreviewMode.Reanimate);
                SetPreviewMode(PreviewMode.Comfimator);
                SetPreviewMode(PreviewMode.Reanimate);
                if(!JToken.DeepEquals(original,layerPreviewRenderer.ExportReanimateDraftCache()))
                    throw new Exception("Navigation erased Reanimate inputs.");
                SaveReanimateDraftCache(beforeRun:true);
                layerPreviewRenderer.ClearBoobV2();
                RestoreReanimateDraftCache(activeAssetPath);
                if(!JToken.DeepEquals(original,layerPreviewRenderer.ExportReanimateDraftCache()))
                    throw new Exception("Separate chest disk recovery changed inputs.");
            }
            return "PASS: current separate chest survives mode navigation and disk recovery twice, including both contours and nipple targets.";
        }
        finally
        {
            SetPreviewMode(mode);
            layerPreviewRenderer.ImportReanimateDraftCache(original);
            SaveReanimateDraftCache(beforeRun:true);
        }
    }
}
#endif
