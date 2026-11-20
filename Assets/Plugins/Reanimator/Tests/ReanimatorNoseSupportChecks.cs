#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyNestedNoseSupport()
    {
        var raw=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/NoseInsideMouthSupport.json"));
        var contours=raw.Select(c=>c.Select(p=>new Vector2((float)p[0],(float)p[1])).ToArray()).ToArray();
        var renderer=new PsdLayerPreviewRenderer();
        for(int rotation=0;rotation<360;rotation+=45)
        {
            var q=Quaternion.Euler(0,0,rotation);
            var transformed=contours.Select(c=>c.Select(p=>(Vector2)(q*(Vector3)p)).ToArray()).ToArray();
            if(!renderer.TryPrepareNoseOuterContour(transformed[3],transformed[0],out var center,out var outer,transformed[1],transformed[2]))
                throw new Exception("Nested nose support failed at rotation "+rotation);
            if(outer.Length<3||outer.Any(p=>IsStrictlyInsideFullBoobContour(p,transformed[1])||IsStrictlyInsideFullBoobContour(p,transformed[2])))
                throw new Exception("Nose support overlaps actual lips.");
        }
        return "PASS: captured nose inside mouth support builds a lip-safe outer contour at eight face rotations.";
    }
}
#endif
