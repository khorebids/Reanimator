#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string VerifyCapturedSideAssJoin(string fixture="Assets/Plugins/Reanimator/Tests/Fixtures/SideAssJoinFailure.json")
    {
        var json=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(fixture));
        var layer=layerInfos.First(l=>l.name=="l_leg");TryGetLayerVertexData(layer,out var original);
        var captured=JsonUtility.FromJson<LayerVertexData>((string)json["data"]);captured.layer=layer;captured.texture=original.texture;
        vertexDataByLayerSelectionKey[BuildSelectionKey(layer)]=captured;
        bones.Clear();foreach(var item in json["bones"])bones.Add(JsonUtility.FromJson<BoneInfo>((string)item));
        bonesTreatAsWorldSpace=(bool)json["worldSpace"];rotateLocalBoneOffsetsByParent=(bool)json["rotateOffsets"];
        selectedBoneIndex=(int)json["selected"];SelectLayer(layer);LoadSideAss();sideAssContour.Clear();
        foreach(var p in json["arc"])sideAssContour.Add(new Vector2((float)p[0],(float)p[1]));
        RecomputeBoneWorldTransforms();
        var profileBefore=GetLayerVerticesForDraw(captured).ToArray();var edgesBefore=captured.boundaryEdgePairs.ToArray();
        var manualOffsets=bones.Where(bone=>bone.name.Contains("_booty")).ToDictionary(bone=>bone.name,bone=>bone.position-bone.initialPosition);
        int before=captured.vertices.Length;
        if(fixture.Contains("Smoothing") && !AutoDrawSideAssArc(out var autoError))throw new Exception(autoError);
        if(!ApplySideAss(out var message))throw new Exception(message);
        TryGetLayerVertexData(layer,out var appliedData);
        if(!ReferenceEquals(appliedData,captured))throw new Exception("Captured data was replaced during Apply.");
        if(!ApplySideAss(out message))throw new Exception("Repeated captured fan: "+message);
        foreach(var bone in bones)if(manualOffsets.TryGetValue(bone.name,out var offset))bone.position=bone.initialPosition+offset;
        RecomputeBoneWorldTransforms();InvalidateBonePose();
        var curveRecipe=sideAssFile.regions.First(r=>r.leg==bones[selectedBoneIndex].name);
        if(curveRecipe.arcSamples.Length!=SideAssFanPoints)throw new Exception("Configured arc count was not respected.");
        var curveSettings=JsonUtility.ToJson(GetSideAssCurves());var curveBefore=captured.boneWeights.ToArray();var triangleBefore=captured.triangles.ToArray();
        var positionsBefore=bones.Select(b=>b.position).ToArray();int vertexCount=captured.vertices.Length;
        SideAssArcInfluence=AnimationCurve.Constant(0,1,1);
        RefreshSideAssCurveWeights();
        if(captured.boneWeights.SequenceEqual(curveBefore))throw new Exception("Curve editing did not update mesh weights.");
        if(captured.vertices.Length!=vertexCount || !captured.triangles.SequenceEqual(triangleBefore) || !bones.Select(b=>b.position).SequenceEqual(positionsBefore))throw new Exception("Curve editing rebuilt geometry or reset helper pose.");
        sideAssCurves=JsonUtility.FromJson<SideAssCurves>(curveSettings);RefreshSideAssCurveWeights();
        SaveSideAssProfileComparison(profileBefore,edgesBefore,GetLayerVerticesForDraw(captured),captured.boundaryEdgePairs);
        foreach(var w in captured.boneWeights)if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>.001f)throw new Exception("Captured fan has invalid weights.");
        return "Captured failing mesh ("+before+" -> "+captured.vertices.Length+" vertices): Apply twice repaired the join and preserved normalized weights. "+sideAssContourDiagnostic;
    }
    private static void SaveSideAssProfileComparison(Vector2[] before,int[] beforeEdges,Vector2[] after,int[] afterEdges)
    {
        var points=beforeEdges.Select(i=>before[i]).Concat(afterEdges.Select(i=>after[i])).ToArray();
        Vector2 min=new Vector2(points.Min(p=>p.x),points.Min(p=>p.y)),max=new Vector2(points.Max(p=>p.x),points.Max(p=>p.y));
        float scale=Mathf.Min(550/Mathf.Max(1,max.x-min.x),650/Mathf.Max(1,max.y-min.y));
        var image=new Texture2D(1200,700,TextureFormat.RGBA32,false);
        image.SetPixels(Enumerable.Repeat(new Color(.12f,.12f,.12f,1),1200*700).ToArray());
        Draw(before,beforeEdges,0,new Color(.8f,.8f,.8f,1));Draw(after,afterEdges,600,Color.cyan);
        image.Apply();File.WriteAllBytes("Temp/SideAssSmoothingProfiles.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        void Draw(Vector2[] vertices,int[] edges,int column,Color color)
        {
            for(int i=0;i<edges.Length;i+=2)
            {
                var a=(vertices[edges[i]]-min)*scale+new Vector2(column+25,25);var b=(vertices[edges[i+1]]-min)*scale+new Vector2(column+25,25);
                int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)));
                for(int j=0;j<=steps;j++){var p=Vector2.Lerp(a,b,j/(float)steps);int x=Mathf.RoundToInt(p.x),y=Mathf.RoundToInt(p.y);if(x>=0&&x<1200&&y>=0&&y<700)image.SetPixel(x,y,color);}
            }
        }
    }
    public string VerifyRetainedSideAssContour()
    {
        var captured=JsonUtility.FromJson<SideAssFile>(File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/SideAssRetainedContour.json"));
        var region=captured.regions.First(r=>r.manualFan);
        var layer=layerInfos.First(l=>l.name==region.leg);SelectLayer(layer);selectedBoneIndex=bones.FindIndex(b=>b.name==region.leg);
        TryGetLayerVertexData(layer,out var data);
        TryResolveFullBoobOuterBoundaryLoop(data,out var loop);
        Vector2 a=-(Vector2)(bones[selectedBoneIndex].worldRotation*Vector3.right);
        Vector2 b=new Vector2(-a.y,a.x);var center=region.contour[0];
        if(Vector2.Dot(region.contour.Last()-center,b)<0)b=-b;
        Vector2 first=region.contour.OrderByDescending(p=>Vector2.Dot(p-center,a)).First();
        Vector2 last=region.contour.OrderByDescending(p=>Vector2.Dot(p-center,b)).First();
        sideAssContour.Clear();sideAssContour.Add(first);sideAssContour.Add(last);
        var source=loop.Select(i=>data.vertices[i]).ToArray();
        var arc=SideAssSourceArc(source,new[]{first,last},first,last);
        if(arc.Length<3)throw new Exception("Captured contour did not exercise a retained silhouette chain.");
        if(!ApplySideAss(out var error))throw new Exception(error);
        var recipe=sideAssFile.regions.First(r=>r.leg==region.leg);
        int sa=Array.IndexOf(data.spriteBoneToRendererBone,SideAssSkinBoneIndex(bones.FindIndex(bone=>bone.name==recipe.volume)));
        int sb=Array.IndexOf(data.spriteBoneToRendererBone,SideAssSkinBoneIndex(bones.FindIndex(bone=>bone.name==recipe.press)));
        int checkedVertices=0;float maximumInfluence=0;
        int legSlot=Array.IndexOf(data.spriteBoneToRendererBone,bones.FindIndex(bone=>bone.name==region.leg));
        foreach(var p in arc)
        {
            int index=Array.FindIndex(data.vertices,v=>(v-p).sqrMagnitude<.0001f);
            if(index<0)continue;
            var w=data.boneWeights[index];
            float sum=Weight(w.boneIndex0,w.weight0)+Weight(w.boneIndex1,w.weight1)+Weight(w.boneIndex2,w.weight2)+Weight(w.boneIndex3,w.weight3);
            maximumInfluence=Mathf.Max(maximumInfluence,sum);
            if(Foreign(w.boneIndex0,w.weight0)+Foreign(w.boneIndex1,w.weight1)+Foreign(w.boneIndex2,w.weight2)+Foreign(w.boneIndex3,w.weight3)>.0001f)
                throw new Exception("Retained contour still has unrelated SC weights: "+p);
            checkedVertices++;
            float Foreign(int slot,float value)=>slot==sa||slot==sb||slot==legSlot?0:value;
        }
        if(checkedVertices<3)throw new Exception("Too few retained contour vertices were verified.");
        if(maximumInfluence<.4f)throw new Exception("Real contour lost its main helper influence.");
        return " Captured real contour: "+checkedVertices+" retained edge vertices have helper influence, none pinned to SC.";
        float Weight(int slot,float value)=>slot==sa||slot==sb?value:0;
    }
    public string VerifySideAssRegions()
    {
        sideAssCurves=new SideAssCurves();
        foreach(var name in new[]{"l_leg","r_leg"})
        {
            LoadSideAss(); var layer=layerInfos.First(l=>l.name==name); SelectLayer(layer);
            selectedBoneIndex=bones.FindIndex(b=>b.name==name);
            var leg=bones[selectedBoneIndex];
            if(!TryGetLayerVertexData(layer,out var data))throw new Exception("Missing leg geometry.");
            Vector2 axis=-(Vector2)(leg.worldRotation*Vector3.right), side=new Vector2(-axis.y,axis.x);
            var original=data.vertices.ToArray(); var uv=data.vertexUvs.ToArray(); var weights=data.boneWeights.ToArray();
            var nearby=original.Where(p=>Vector2.Distance(p,leg.worldPosition)<leg.length*.4f).ToArray();
            var center=nearby.Aggregate(Vector2.zero,(a,b)=>a+b)/nearby.Length;
            float radius=leg.length*.12f;
            sideAssContour.Clear();
            for(int i=0;i<12;i++){float angle=i*Mathf.PI*.5f/11;sideAssContour.Add(center+axis*(radius*Mathf.Cos(angle))+side*(radius*Mathf.Sin(angle)));}
            VerifySideAssInteractivePreview();
            if(!ApplySideAss(out var error))throw new Exception(name+": "+error);
            var recipe=sideAssFile.regions.First(r=>r.leg==name);
            int centerVertex=Array.FindIndex(data.vertices,p=>(p-center).sqrMagnitude<.0001f);
            int selectedLegSlot=Array.IndexOf(data.spriteBoneToRendererBone,leg.index);
            if(centerVertex<0 || data.boneWeights[centerVertex].boneIndex0!=selectedLegSlot || data.boneWeights[centerVertex].weight0<.999f)
                throw new Exception("Fan center must be fully weighted to the selected upper leg.");
            foreach(int i in Enumerable.Range(0,original.Length).Where(i=>!IsPointInsideFullBoobContour(original[i],recipe.contour)&&SideAssDistance(original[i],recipe.contour)>radius*.08f+.01f))
            {
                int kept=Array.FindIndex(data.vertices,p=>(p-original[i]).sqrMagnitude<.000001f);
                if(kept<0||data.vertexUvs[kept]!=uv[i]||!data.boneWeights[kept].Equals(weights[i]))throw new Exception("Outside vertex/UV/weight was changed.");
            }
            int count=data.vertices.Length, boneCount=bones.Count;
            if(!ApplySideAss(out error))throw new Exception(error);
            if(bones.Count!=boneCount || data.vertices.Length>count+4)throw new Exception("Repeated fan duplicated geometry or helpers.");
            foreach(var w in data.boneWeights)if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>.001f)throw new Exception("Unnormalized fan weight.");
            VerifySideAssAttachmentCurveDomain();
            float previousInfluence=-1;
            int slotA=Array.IndexOf(data.spriteBoneToRendererBone,SideAssSkinBoneIndex(bones.FindIndex(bone=>bone.name==recipe.volume)));
            int slotB=Array.IndexOf(data.spriteBoneToRendererBone,SideAssSkinBoneIndex(bones.FindIndex(bone=>bone.name==recipe.press)));
            foreach(float radial in new[]{0f,1f/3,2f/3,1f})
            {
                Vector2 p=Vector2.Lerp(recipe.weightCenter,recipe.arcSamples[recipe.arcSamples.Length/2],radial);
                int vertex=Array.FindIndex(data.vertices,v=>(v-p).sqrMagnitude<.0001f);
                if(vertex<0)throw new Exception("Fan radial row is missing.");
                var w=data.boneWeights[vertex];
                float influence=Weight(w.boneIndex0,w.weight0)+Weight(w.boneIndex1,w.weight1)+Weight(w.boneIndex2,w.weight2)+Weight(w.boneIndex3,w.weight3);
                if(influence<=previousInfluence)throw new Exception("Actual helper weights must increase toward the contour.");
                previousInfluence=influence;
                float Weight(int slot,float value)=>slot==slotA||slot==slotB?value:0;
            }
            int ha=bones.FindIndex(b=>b.name==recipe.volume),hb=bones.FindIndex(b=>b.name==recipe.press);
            if(recipe.arcSamples.Length!=SideAssFanPoints)throw new Exception("Arc point setting is not respected.");
            foreach(var p in new[]{recipe.arcSamples.First(),recipe.arcSamples.Last()})
            {
                int vertex=Array.FindIndex(data.vertices,v=>(v-p).sqrMagnitude<.0001f);
                if(vertex<0)throw new Exception("Fan endpoint missing.");
                var w=data.boneWeights[vertex];
                float total=Amount(w.boneIndex0,w.weight0)+Amount(w.boneIndex1,w.weight1)+Amount(w.boneIndex2,w.weight2)+Amount(w.boneIndex3,w.weight3);
                if(total>.0001f)throw new Exception("Fan endpoint must return fully to leg.");
                float Amount(int slot,float value)=>slot==slotA||slot==slotB?value:0;
            }
            if(bones[ha].parentId!=leg.index||bones[hb].parentId!=leg.index)throw new Exception("Manual helpers are not independent selected-leg children.");
            if(Vector2.Distance(bones[ha].worldPosition,leg.worldPosition)>.001f || Vector2.Distance(bones[hb].worldPosition,leg.worldPosition)>.001f)
                throw new Exception("Both helpers must start at the upper leg bone origin.");
            var legRotation=leg.rotation; var helperRotationA=bones[ha].worldRotation; var helperRotationB=bones[hb].worldRotation;
            leg.rotation*=Quaternion.Euler(0,0,30);RecomputeBoneWorldTransforms();
            if(Mathf.Abs(Quaternion.Angle(helperRotationA,bones[ha].worldRotation)-30)>.01f || Mathf.Abs(Quaternion.Angle(helperRotationB,bones[hb].worldRotation)-30)>.01f)
                throw new Exception("Helpers do not inherit upper leg rotation.");
            leg.rotation=legRotation;RecomputeBoneWorldTransforms();InvalidateBonePose();
            var rest=GetLayerVerticesForDraw(data).ToArray();var position=bones[ha].position;
            bones[ha].position+=ConvertBoneWorldPositionToLocal(leg.index,bones[ha].worldPosition+axis*radius)-ConvertBoneWorldPositionToLocal(leg.index,bones[ha].worldPosition);
            RecomputeBoneWorldTransforms();InvalidateBonePose();
            var moved=GetLayerVerticesForDraw(data);
            if(!rest.Where((p,i)=>Vector2.Distance(p,moved[i])>.001f).Any())throw new Exception("Manual helper did not deform the fan.");
            var secondPosition=bones[hb].position;
            bones[hb].position+=ConvertBoneWorldPositionToLocal(leg.index,bones[hb].worldPosition+side*radius)-ConvertBoneWorldPositionToLocal(leg.index,bones[hb].worldPosition);
            RecomputeBoneWorldTransforms();InvalidateBonePose();
            var circular=GetLayerVerticesForDraw(data);
            foreach(var p in recipe.arcSamples)
            {
                int vertex=Array.FindIndex(data.vertices,v=>(v-p).sqrMagnitude<.0001f);
                if(vertex<0)throw new Exception("Fan arc vertex missing.");
                var w=data.boneWeights[vertex];
                float wa=Amount(slotA),wb=Amount(slotB);
                var expected=rest[vertex]+axis*(radius*wa)+side*(radius*wb);
                if(Vector2.Distance(circular[vertex],expected)>.05f)throw new Exception("Manual shifts do not follow the curve's stored helper weights.");
                float Amount(int slot)=>(w.boneIndex0==slot?w.weight0:0)+(w.boneIndex1==slot?w.weight1:0)+(w.boneIndex2==slot?w.weight2:0)+(w.boneIndex3==slot?w.weight3:0);
            }
            bones[hb].position=secondPosition;
            bones[ha].position=position;RecomputeBoneWorldTransforms();InvalidateBonePose();
        }
        return "Both legs: local outside geometry preserved, single-curve weights, two independent manual controls, repeated apply and manual deformation.";
    }
}
public static class ReanimatorSideAssFixture
{
    private static PsdLayerPreviewRenderer renderer;
    private static string asset,guid;
    private static UnityEngine.Object selection;
    private static bool capturedOnly;
    private static string capturedFile;
    public static string Begin(string source,bool captured=false,string fixture=null)
    {
        capturedOnly=captured;
        capturedFile=fixture;
        selection=Selection.activeObject;
        asset=AssetDatabase.GenerateUniqueAssetPath("Assets/__SideAssFixture.psd");
        if(!AssetDatabase.CopyAsset(source,asset))throw new Exception("Cannot copy Side Ass test asset.");
        guid=AssetDatabase.AssetPathToGUID(asset);
        renderer=new PsdLayerPreviewRenderer();renderer.RequestPreviewCacheRefresh(asset);return Tick();
    }
    public static string Tick()
    {
        for(int i=0;i<100&&renderer.IsPreviewCacheRefreshInProgress;i++)renderer.TickPreviewCacheRefresh(.03);
        if(renderer.IsPreviewCacheRefreshInProgress)return "Pending: "+renderer.PreviewCacheRefreshStatus;
        try
        {
            string result=capturedOnly?renderer.VerifyCapturedSideAssJoin(capturedFile??"Assets/Plugins/Reanimator/Tests/Fixtures/SideAssJoinFailure.json"):PsdLayerPreviewRenderer.VerifySideAssAutoArcMath()+renderer.VerifySideAssAutoArcOnLegs()+renderer.VerifySideAssRegions()+renderer.VerifyRetainedSideAssContour();
            result+=" "+PsdLayerPreviewRenderer.VerifySideAssAttachmentCurveDomain()+" "+PsdLayerPreviewRenderer.VerifySideAssHelperSmoothing()+" "+renderer.VerifySideAssInteractivePreview();
            result+=" "+renderer.VerifyQuickSideAss();
            result+=" "+renderer.VerifySelectedSideAssAutomation();
            result+=" "+renderer.VerifySideAssSpringChildren();
            if(!renderer.Apply(asset,false,out var error))throw new Exception("Apply: "+error);
            var cooked=ReanimatorPrefabCooker.CookNextToPsd(asset,new[]{"l_leg","r_leg"},renderer);
            if(!string.IsNullOrEmpty(cooked.Error))throw new Exception("Cook: "+cooked.Error);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path.ChangeExtension(asset,".prefab"));
            var skeleton=prefab.transform.Find("root/skeleton");
            if(!skeleton || skeleton.GetComponentsInChildren<Transform>(true).Any(bone=>bone!=skeleton && bone.gameObject.activeSelf))
                throw new Exception("Cooked skeleton bones must all be hidden.");
            if(!prefab.GetComponent<ReaniIK>().enabled || !prefab.transform.Find("root/rigs").gameObject.activeSelf ||
               !prefab.transform.Find("root/sprites").gameObject.activeSelf || prefab.GetComponentsInChildren<SpriteRenderer>(true).Any(sprite=>sprite.transform.IsChildOf(skeleton)))
                throw new Exception("Hiding bones disabled a rig or sprite.");
            var valid=typeof(UnityEngine.U2D.Animation.SpriteSkin).GetProperty("isValid",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var instance=UnityEngine.Object.Instantiate(prefab);instance.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                instance.GetComponent<ReaniIK>().enabled=false;
                foreach(var skin in instance.GetComponentsInChildren<UnityEngine.U2D.Animation.SpriteSkin>(true).Where(s=>s.name=="l_leg" || s.name=="r_leg"))
                    if(!(bool)valid.GetValue(skin))throw new Exception("Inactive bones invalidated live SpriteSkin bindings.");
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);}
            var buttPairs=ReaniIKButtControls.FindPairs(prefab.transform);
            if(buttPairs.Count==0)throw new Exception("Cooked Side Ass helpers did not auto-bind to Butt controls.");
            var sc=prefab.GetComponent<ReaniIK>().softContour.bones;
            foreach(var pair in buttPairs)
                foreach(var control in new[]{pair.parallel,pair.perpendicular})
                {
                    var child=control.Find(control.name+"_SC");
                    if(!child || !sc.Contains(child) || sc.Contains(control))throw new Exception("Cook did not bind only the Butt SC children to springs.");
                }
            if(prefab.GetComponentsInChildren<SideAssRig2D>(true).Length!=0)throw new Exception("Manual fan unexpectedly has automatic motion.");
            var repeated=ReanimatorPrefabCooker.CookNextToPsd(asset,new[]{"l_leg","r_leg"});
            if(!string.IsNullOrEmpty(repeated.Error))throw new Exception("Standalone Cook: "+repeated.Error);
            prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path.ChangeExtension(asset,".prefab"));
            if(prefab.GetComponentsInChildren<SideAssRig2D>(true).Length!=0)throw new Exception("Standalone Cook attached an automatic rig.");
            return "PASS: "+result+" PSD Apply and repeated Cook preserve manual-only helpers.";
        }
        finally
        {
            Selection.activeObject=selection;
            AssetDatabase.DeleteAsset(Path.ChangeExtension(asset,".prefab"));AssetDatabase.DeleteAsset(asset);
            string recipe=Path.Combine("Library","Reanimator","SideAss",guid+".json");if(File.Exists(recipe))File.Delete(recipe);
            string backup=Path.GetFullPath(asset+".backups~");
            if(backup.StartsWith(Path.GetFullPath(Application.dataPath)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(backup).StartsWith("__SideAssFixture",StringComparison.Ordinal)&&Directory.Exists(backup))Directory.Delete(backup,true);
            renderer=null;
        }
    }
}
#endif
