#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const int SideAssFanVersion=5;
    private string sideAssContourDiagnostic;
    [Serializable] private sealed class SideAssGeometryStamp
    {public Vector2[] vertices,uv;public int[] triangles,map,boundary;public BoneWeight[] weights;public Matrix4x4[] bind;public bool spriteBind;}
    private static string SideAssStamp(LayerVertexData data)=>Hash128.Compute(JsonUtility.ToJson(new SideAssGeometryStamp{vertices=data.vertices,uv=data.vertexUvs,triangles=data.triangles,map=data.spriteBoneToRendererBone,boundary=data.boundaryEdgePairs,weights=data.boneWeights,bind=data.bindBoneInverseMatrices,spriteBind=data.usesSpriteBindPoses})).ToString();
    internal int SideAssFanPoints { get; set; } = 12;
    internal int ResetSideAssHelperOrigins()
    {
        LoadSideAss(); int changed=0; var moved=new HashSet<int>();
        foreach(var recipe in sideAssFile.regions.Where(r=>r.manualFan))
        {
            if(!TryFindBoneIndexByName(recipe.leg,out var leg))continue;
            foreach(string name in new[]{recipe.volume,recipe.press})
            {
                if(!TryFindBoneIndexByName(name,out var helper))continue;
                if(bones[helper].parentId==leg && Vector2.Distance(bones[helper].worldPosition,bones[leg].worldPosition)<.001f)continue;
                if(changed==0)RegisterUndo("Reset Side Ass helper origins");
                var rotation=bones[helper].worldRotation;
                bones[helper].parentId=leg;
                bones[helper].position=bones[helper].initialPosition=Vector2.zero;
                bones[helper].rotation=bones[helper].initialRotation=ConvertBoneWorldRotationToLocal(leg,rotation);
                moved.Add(helper);moved.Add(SideAssSkinBoneIndex(helper)); changed++; RecomputeBoneWorldTransforms();
            }
        }
        if(changed==0)return 0;
        foreach(var data in vertexDataByLayerSelectionKey.Values.Distinct())
        {
            if(data?.spriteBoneToRendererBone==null || data.bindBoneInverseMatrices==null)continue;
            bool touched=false;
            for(int slot=0;slot<data.spriteBoneToRendererBone.Length;slot++)
                if(moved.Contains(data.spriteBoneToRendererBone[slot]))
                {TryBuildBindInverseMatrixForRendererBone(data,data.spriteBoneToRendererBone[slot],out data.bindBoneInverseMatrices[slot]);touched=true;}
            if(touched){MarkSkinningWeightsChanged(data);MarkEditGeometryDataChanged(data,BuildSelectionKey(data.layer),BuildKey(data.layer.name,data.layer.layerId));}
        }
        BuildBoneDrawOrder(); MarkBoneStateChanged(); return changed;
    }
    internal static Vector3 SideAssFanWeights(float angle, float radial)
    {
        float falloff=Mathf.Clamp01(radial); falloff*=falloff;
        falloff*=SideAssEndFade(angle);
        float a=.5f*Mathf.Cos(Mathf.Clamp(angle,0,Mathf.PI*.5f))*falloff;
        float b=.5f*Mathf.Sin(Mathf.Clamp(angle,0,Mathf.PI*.5f))*falloff;
        return new Vector3(a,b,1-a-b);
    }
    private const float SideAssEndDegrees=18f;
    internal static float SideAssEndFade(float angle)
    {
        float degrees=angle*Mathf.Rad2Deg;
        if(degrees<0)return Mathf.SmoothStep(0,1,(degrees+SideAssEndDegrees)/SideAssEndDegrees);
        if(degrees>90)return 1-Mathf.SmoothStep(0,1,(degrees-90)/SideAssEndDegrees);
        return 1;
    }
    internal bool ApplySideAss(out string message)
    {
        LoadSideAss();
        var snapshot=CloneBones(bones);
        TryGetSelectedLayer(out var selectedLayer);TryGetLayerVertexData(selectedLayer,out var originalData);
        var dataSnapshot=originalData!=null?CloneProjectorLayerData(originalData):null;
        var recipesSnapshot=JsonUtility.ToJson(sideAssFile);
        try { return ApplySideAssCore(out message); }
        catch(Exception exception)
        {
            bones.Clear();bones.AddRange(snapshot);RecomputeBoneWorldTransforms();BuildBoneDrawOrder();InvalidateBonePose();
            if(originalData!=null && dataSnapshot!=null)
                foreach(var field in typeof(LayerVertexData).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))field.SetValue(originalData,field.GetValue(dataSnapshot));
            sideAssFile=JsonUtility.FromJson<SideAssFile>(recipesSnapshot);
            message="Side Ass: "+exception.Message;
            Debug.LogError(message);
            return false;
        }
    }
    private bool ApplySideAssCore(out string message, bool previewOnly=false)
    {
        LoadSideAss(); message="Select the leg sprite and upper leg bone, then draw the outer quarter arc.";
        if(sideAssContour.Count<2 || selectedBoneIndex<0 || selectedBoneIndex>=bones.Count || !TryGetSelectedLayer(out var layer) ||
            !TryGetLayerVertexData(layer,out var data) || data?.vertices==null) return false;
        int leg=selectedBoneIndex;
        var unchanged=sideAssFile.regions.Find(r=>r.leg==bones[leg].name && r.layer==BuildSelectionKey(layer));
        // Auto arc redraws its posed visualization after Apply moves helpers back
        // to their origins. Compare the stable bind-space arc as well, otherwise
        // the same Quick operation unnecessarily rebuilds its own generated mesh.
        bool sameDrawing=unchanged!=null &&
            (unchanged.drawingDoc!=null && unchanged.drawingDoc.SequenceEqual(sideAssContour) ||
             unchanged.baseDrawing!=null && sideAssAutoBaseArc!=null && sideAssAutoLeg==bones[leg].name &&
             sideAssAutoDrawingDoc!=null && sideAssAutoDrawingDoc.SequenceEqual(sideAssContour) && unchanged.baseDrawing.SequenceEqual(sideAssAutoBaseArc));
        if(!previewOnly && unchanged!=null && unchanged.fanVersion==SideAssFanVersion && unchanged.fanPointCount==SideAssFanPoints && unchanged.fanFlip==SideAssFlip &&
            sameDrawing && unchanged.curveStamp==SideAssCurveStamp && unchanged.geometryStamp==SideAssStamp(data) &&
            unchanged.validatedExterior!=null && SideAssJoinIsClosed(data,unchanged.validatedExterior))
        {message="Side Ass fan is already applied; validated mesh preserved.";return true;}
        var drawn=sideAssAutoBaseArc!=null && sideAssAutoLeg==bones[leg].name && sideAssAutoDrawingDoc!=null && sideAssAutoDrawingDoc.SequenceEqual(sideAssContour)?sideAssAutoBaseArc:
            unchanged?.baseDrawing!=null && unchanged.drawingDoc!=null && unchanged.drawingDoc.SequenceEqual(sideAssContour)?unchanged.baseDrawing:MapFullBoobContourToBase(layer,sideAssContour);
        var restBones=CloneBones(bones);
        foreach(var bone in restBones){bone.position=bone.initialPosition;if(!bone.initialRotation.Equals(default(Quaternion)))bone.rotation=bone.initialRotation;}
        new BonePreviewTransform(bonesTreatAsWorldSpace,rotateLocalBoneOffsetsByParent).ResolveAll(restBones);
        Vector2 a=-(Vector2)(restBones[leg].worldRotation*Vector3.right);
        if(SideAssFlip)a=-a;
        Vector2 first=drawn[0], last=drawn[drawn.Length-1];
        if(Vector2.Dot(first-last,a)<0) (first,last)=(last,first);
        Vector2 b=new Vector2(-a.y,a.x);
        if(Vector2.Dot(last-first,b)<0)b=-b;
        float ra=Vector2.Dot(first-last,a), rb=Vector2.Dot(last-first,b);
        if(ra<.001f || rb<.001f) {message="Arc endpoints must span both axes: opposite the leg and perpendicular to it.";return false;}
        Vector2 center=a*Vector2.Dot(last,a)+b*Vector2.Dot(first,b);
        int count=Mathf.Clamp(SideAssFanPoints,8,24);
        var polygon=new List<Vector2>{center};
        int endSteps=Mathf.Max(3,Mathf.CeilToInt(SideAssEndDegrees/(90f/(count-1))));
        for(int i=0;i<endSteps;i++)ArcPoint((-SideAssEndDegrees+SideAssEndDegrees*i/endSteps)*Mathf.Deg2Rad);
        for(int i=0;i<count;i++) {float angle=i*Mathf.PI*.5f/(count-1);polygon.Add(center+a*(ra*Mathf.Cos(angle))+b*(rb*Mathf.Sin(angle)));}
        for(int i=1;i<=endSteps;i++)ArcPoint((90+SideAssEndDegrees*i/endSteps)*Mathf.Deg2Rad);
        var staged=CloneProjectorLayerData(data);
        var previous=sideAssFile.regions.Find(r=>r.leg==bones[leg].name && r.layer==BuildSelectionKey(layer));
        if(previous?.weights!=null && previous.weightBones!=null)
            foreach(var saved in previous.weights)
            {
                int index=Array.FindIndex(staged.vertices,p=>(p-saved.point).sqrMagnitude<.0001f);
                if(index>=0 && staged.boneWeights[index].Equals(Remap(saved.applied))) staged.boneWeights[index]=Remap(saved.original);
            }
        // Old generated weights outside a redrawn fan must not keep following
        // the same helper after its rest position is rebuilt.
        if(!TryEnsureFullBoobSpriteBone(staged,leg,out var legSlot))throw new InvalidOperationException("Cannot bind the selected upper leg.");
        if(!TryResolveFullBoobOuterBoundaryLoop(staged,out var boundaryLoop)){message="Sprite has no recoverable mesh boundary.";return false;}
        var exteriorSource=boundaryLoop.Select(i=>staged.vertices[i]).ToArray();
        var sourceArc=SideAssSourceArc(exteriorSource,drawn,first,last);
        var importedBoundary=SideAssImportedBoundary(layer);
        var importedArc=importedBoundary.Length>=3?SideAssSourceArc(importedBoundary,drawn,first,last):Array.Empty<Vector2>();
        if(importedArc.Length>=2)sourceArc=importedArc;
        bool followSource=sourceArc.Length>=2;
        sideAssContourDiagnostic="source="+sourceArc.Length+", imported="+importedArc.Length;
        float endDegrees=90+SideAssEndDegrees;
        if(followSource)
        {
            // The handles pivot at the leg origin, so their angular influence
            // must be measured from that same origin, not the fitted ellipse corner.
            center=restBones[leg].worldPosition;
            var end=sourceArc[sourceArc.Length-1]-center;
            endDegrees=Mathf.Atan2(Vector2.Dot(end,b),Vector2.Dot(end,a))*Mathf.Rad2Deg;
        }
        bool extendEnd=!followSource || endDegrees<90+SideAssEndDegrees;
        sourceArc=SideAssExtendArc(importedArc.Length>=2?importedBoundary:exteriorSource,sourceArc,center,out var prefixLength,out var suffixLength,extendEnd);
        var fullWeightArc=sourceArc;
        if(followSource)
        {
            sourceArc=SideAssResampleArc(sourceArc,count);
            var end=sourceArc[sourceArc.Length-1]-center;
            endDegrees=Mathf.Max(90.1f,Mathf.Atan2(Vector2.Dot(end,b),Vector2.Dot(end,a))*Mathf.Rad2Deg);
            sideAssContourDiagnostic+=", end="+endDegrees;
            polygon.Clear();polygon.Add(center);polygon.AddRange(sourceArc);
            if(importedArc.Length>=2)exteriorSource=SideAssReplaceBoundaryArc(exteriorSource,sourceArc);
        }
        else
        {
            var sampled=SideAssResampleArc(polygon.Skip(1).ToArray(),count);
            polygon.Clear();polygon.Add(center);polygon.AddRange(sampled);
        }
        var seamArc=sourceArc;
        if(followSource)sourceArc=fullWeightArc;
        exteriorSource=SideAssInsertContourPoints(exteriorSource,seamArc);
        var sourceRegion=new List<Vector2>{center};sourceRegion.AddRange(sourceArc);
        float sourceLength=SideAssArcLength(sourceArc),perpendicularDistance=sourceLength-suffixLength;
        float traversed=0;
        for(int i=0;i+1<sourceArc.Length;i++)
        {
            var x=sourceArc[i]-center;var y=sourceArc[i+1]-center;
            float start=Vector2.Dot(x,a),end=Vector2.Dot(y,a),segment=Vector2.Distance(x,y);
            if(start>=0 && end<=0 && start-end>.000001f && Vector2.Dot(Vector2.Lerp(x,y,start/(start-end)),b)>0)
            {perpendicularDistance=traversed+segment*start/(start-end);break;}
            traversed+=segment;
        }
        for(int i=0;i<staged.vertices.Length;i++)
        {
            if(IsPointInsideFullBoobContour(staged.vertices[i],polygon))continue;
            var w=staged.boneWeights[i];var sum=new Dictionary<int,float>(); bool changed=false;
            Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
            if(changed)staged.boneWeights[i]=BuildVertexFixWeight(sum);
            void Add(int slot,float value)
            {
                if(value<=0)return;
                int renderer=staged.spriteBoneToRendererBone[slot];string name=bones[renderer].name;
                if(name==bones[leg].name+"_booty" || name==bones[leg].name+"_booty_press" || name==bones[leg].name+"_booty_SC" || name==bones[leg].name+"_booty_press_SC"){slot=legSlot;changed=true;}
                sum[slot]=sum.TryGetValue(slot,out var old)?old+value:value;
            }
        }
        var contour=SplitFullBoobContour(polygon,exteriorSource).ToArray();
        var empty=Array.Empty<Vector2>();
        var remove=staged.vertices.Select(p=>IsPointInsideFullBoobContour(p,contour) ||
            (previous?.manualFan==true && previous.contour!=null && (IsPointInsideFullBoobContour(p,previous.contour)||SideAssDistance(p,previous.contour)<.001f))).ToArray();
        if(!staged.vertices.Any(p=>IsPointInsideFullBoobContour(p,contour)) && !contour.Any(p=>IsPointInsideFullBoobContour(p,exteriorSource)))
        {message="The fan must overlap the selected sprite.";return false;}
        if(!CompactFullBoobGeometryKeepingOutside(staged,remove,boundaryLoop,contour,empty,0,0,out var retainedCount,out var retainedEdges,out var retainedTriangles))
        {message="Could not retain the mesh around the fan.";return false;}
        if(!previewOnly)RegisterUndo("Side Ass manual fan");
        var poseRotation=bones[leg].worldRotation*Quaternion.Inverse(restBones[leg].worldRotation);
        int helperA=Helper(ReaniIKButtControls.ParallelBoneName(bones[leg].name),bones[leg].worldPosition,poseRotation*a,ra);
        int helperB=Helper(ReaniIKButtControls.PerpendicularBoneName(bones[leg].name),bones[leg].worldPosition,poseRotation*b,rb);
        int skinA=EnsureSideAssSkinChild(helperA,true),skinB=EnsureSideAssSkinChild(helperB,true);
        TransferSideAssSkinWeights(staged,helperA,skinA);TransferSideAssSkinWeights(staged,helperB,skinB);
        if(!TryEnsureFullBoobSpriteBone(staged,skinA,out var slotA) || !TryEnsureFullBoobSpriteBone(staged,skinB,out var slotB))
            throw new InvalidOperationException("Cannot bind manual Side Ass helpers.");
        TryBuildBindInverseMatrixForRendererBone(staged,skinA,out staged.bindBoneInverseMatrices[slotA]);
        TryBuildBindInverseMatrixForRendererBone(staged,skinB,out staged.bindBoneInverseMatrices[slotB]);
        var vertices=new List<Vector2>(staged.vertices);var uv=new List<Vector2>(staged.vertexUvs);var weights=new List<BoneWeight>(staged.boneWeights);
        var local=new List<int>();var constraints=new List<Vector2Int>(retainedEdges);
        int origin=Point(center);
        // Only the configured arc samples create radial spokes. Intersections
        // needed for the seam are inserted separately, not expanded into rings.
        var rows=new int[3,polygon.Count];
        for(int row=0;row<3;row++)for(int i=0;i<polygon.Count;i++)rows[row,i]=Point(Vector2.Lerp(center,polygon[i],(row+1)/3f));
        for(int i=0;i<polygon.Count;i++)
        {
            int j=(i+1)%polygon.Count;
            constraints.Add(new Vector2Int(rows[2,i],rows[2,j]));
            Triangle(origin,rows[0,i],rows[0,j]);
            for(int row=0;row<2;row++)
            {Triangle(rows[row,i],rows[row+1,i],rows[row+1,j]);Triangle(rows[row,i],rows[row+1,j],rows[row,j]);}
        }
        staged.boundaryEdgePairs=BuildFullBoobExterior(exteriorSource,contour,empty,layer,vertices,uv,weights);
        staged.vertices=vertices.ToArray();staged.vertexUvs=uv.ToArray();staged.boneWeights=weights.ToArray();
        var exterior=staged.boundaryEdgePairs;
        if(!TryRetriangulateFullBoobsWithConstraints(staged,constraints,retainedCount,retainedEdges.Count))
            throw new InvalidOperationException("Could not join the fan to the retained sprite mesh.");
        staged.triangles=SanitizeTriangles(BuildFullBoobCombinedTriangles(staged.vertices,staged.triangles,contour,empty,retainedTriangles,local),staged.vertices.Length);
        // Splits introduced where the fan crosses the source boundary must also
        // split its deterministic local triangles (avoid T junctions).
        for(int v=0;v<staged.vertices.Length;v++)
        {
            var edges=new HashSet<(int,int)>();
            for(int i=0;i<staged.triangles.Length;i+=3)for(int e=0;e<3;e++)
            {int x=staged.triangles[i+e],y=staged.triangles[i+(e+1)%3];if(x!=v&&y!=v&&DistancePointToSegment(staged.vertices[v],staged.vertices[x],staged.vertices[y])<.0001f&&Vector2.Distance(staged.vertices[v],staged.vertices[x])>.001f&&Vector2.Distance(staged.vertices[v],staged.vertices[y])>.001f)edges.Add((Mathf.Min(x,y),Mathf.Max(x,y)));}
            foreach(var edge in edges)SplitTrianglesOnEdge(ref staged.triangles,staged.vertices,staged.vertices[v],v,edge.Item1,edge.Item2);
        }
        staged.triangles=ReanimatorMeshWinding.Normalize(staged.triangles,out _);
        BuildEdgePairs(staged.triangles,staged.vertices.Length,out staged.allEdgePairs,out staged.boundaryEdgePairs);
        if(!SideAssJoinIsClosed(staged,exterior) && !RepairSideAssJoin(staged,exterior,retainedEdges,local))
            throw new InvalidOperationException("Fan triangulation left an incomplete boundary.");
        // The retained sprite silhouette and intersections added by the exterior
        // builder also need the radial gradient. Otherwise the original edge can
        // stay leg-weighted while the new inner rows follow the helpers.
        var silhouette=new HashSet<int>(staged.boundaryEdgePairs);
        float supportBand=Mathf.Min(ra,rb)*.08f;
        for(int i=0;i<staged.vertices.Length;i++)
        {
            // Follow the actual selected silhouette chain, not just the fitted
            // ellipse: retained SC vertices can lie far outside that ellipse.
            if(sourceArc.Length>=2)
            {
                SideAssArcDistance(staged.vertices[i],sourceArc,out var edgeDistance,out var arcTime);
                bool onArc=edgeDistance<=.001f;
                if(onArc || IsPointInsideFullBoobContour(staged.vertices[i],sourceRegion))
                {
                    float fromCenter=Vector2.Distance(staged.vertices[i],center);
                    float radialToEdge=onArc?1:fromCenter/Mathf.Max(.0001f,fromCenter+edgeDistance);
                    // Existing fan rows keep their analytic angular rule. This
                    // pass fills the real edge and the retained gap outside it.
                    if(onArc || !IsPointInsideFullBoobContour(staged.vertices[i],contour))
                    {
                        var edgeWeights=AtSource(staged.vertices[i],radialToEdge);
                        staged.boneWeights[i]=BlendBoneWeights(BuildSingleBoneWeight(slotA),edgeWeights.x,BuildSingleBoneWeight(slotB),edgeWeights.y,BuildSingleBoneWeight(legSlot),edgeWeights.z,null);
                        continue;
                    }
                }
            }
            if(followSource && IsPointInsideFullBoobContour(staged.vertices[i],sourceRegion))
            {
                SideAssArcDistance(staged.vertices[i],sourceArc,out var edgeDistance,out _);
                float radius=Vector2.Distance(staged.vertices[i],center);
                var value=AtSource(staged.vertices[i],radius/Mathf.Max(.0001f,radius+edgeDistance));
                staged.boneWeights[i]=BlendBoneWeights(BuildSingleBoneWeight(slotA),value.x,BuildSingleBoneWeight(slotB),value.y,BuildSingleBoneWeight(legSlot),value.z,null);
                continue;
            }
            Vector2 delta=staged.vertices[i]-center;
            float x=Vector2.Dot(delta,a)/ra,y=Vector2.Dot(delta,b)/rb;
            float angle=Mathf.Atan2(y,x);
            if(angle< -SideAssEndDegrees*Mathf.Deg2Rad-.00001f || angle>(90+SideAssEndDegrees)*Mathf.Deg2Rad+.00001f)continue;
            float radial=Mathf.Sqrt(x*x+y*y);
            float distance=radial>0 ? Vector2.Distance(delta,delta/radial) : 0;
            if(radial>1 && distance>supportBand)continue;
            var w=SideAssCurveWeights(angle,radial);
            if(radial>1 && !silhouette.Contains(i))
            {
                float support=1-Mathf.SmoothStep(0,1,distance/Mathf.Max(.0001f,supportBand));
                w.x*=support;w.y*=support;w.z=1-w.x-w.y;
            }
            staged.boneWeights[i]=BlendBoneWeights(BuildSingleBoneWeight(slotA),w.x,BuildSingleBoneWeight(slotB),w.y,BuildSingleBoneWeight(legSlot),w.z,null);
        }
        var generatedRecipe=new SideAssRecipe{leg=bones[leg].name,volume=bones[helperA].name,press=bones[helperB].name,layer=BuildSelectionKey(layer),contour=contour,manualFan=true,radius=Mathf.Max(ra,rb),fanVersion=SideAssFanVersion,fanPointCount=SideAssFanPoints,fanFlip=SideAssFlip,drawingDoc=sideAssContour.ToArray(),baseDrawing=drawn.ToArray(),geometryStamp=SideAssStamp(staged),validatedExterior=exterior,arcSamples=polygon.Skip(1).ToArray(),weightArc=sourceArc,weightCenter=center,weightA=a,weightB=b,weightPrefix=prefixLength,weightPerpendicular=perpendicularDistance,weightRA=ra,weightRB=rb,sourceWeights=followSource,curveStamp=SideAssCurveStamp};

        if(previewOnly)
        {
            sideAssTestData=staged;sideAssTestRecipe=generatedRecipe;sideAssTestBones=CloneBones(bones);
            message="Temporary Side Ass preview ready.";return true;
        }
        data.vertices=staged.vertices;data.vertexUvs=staged.vertexUvs;data.triangles=staged.triangles;data.boneWeights=staged.boneWeights;
        data.allEdgePairs=staged.allEdgePairs;data.boundaryEdgePairs=staged.boundaryEdgePairs;data.spriteBoneToRendererBone=staged.spriteBoneToRendererBone;
        data.bindBoneInverseMatrices=staged.bindBoneInverseMatrices;data.usesSpriteBindPoses=staged.usesSpriteBindPoses;
        sideAssFile.regions.RemoveAll(r=>r.leg==bones[leg].name);
        sideAssFile.regions.Add(generatedRecipe);
        Directory.CreateDirectory(Path.GetDirectoryName(SideAssPath));File.WriteAllText(SideAssPath,JsonUtility.ToJson(sideAssFile,true));
        foreach(var other in vertexDataByLayerSelectionKey.Values.Distinct())
        {
            if(ReferenceEquals(other,data))continue;
            bool transferred=TransferSideAssSkinWeights(other,helperA,skinA);
            transferred|=TransferSideAssSkinWeights(other,helperB,skinB);
            if(transferred){MarkSkinningWeightsChanged(other);MarkEditGeometryDataChanged(other,BuildSelectionKey(other.layer),BuildKey(other.layer.name,other.layer.layerId));}
        }
        BuildBoneDrawOrder();MarkBoneStateChanged();MarkSkinningWeightsChanged(data);MarkEditGeometryDataChanged(data,BuildSelectionKey(layer),BuildKey(layer.name,layer.layerId));
        message="Manual Side Ass fan applied. Both helpers are independent children of the selected leg; move them along their axes.";return true;

        int Helper(string name,Vector2 position,Vector2 direction,float radius)
        {
            if(!TryFindBoneIndexByName(name,out var index))index=FindOrCreateVGenBone(leg,name,position,true);
            var bone=bones[index];bone.parentId=leg;
            bone.position=bone.initialPosition=ConvertBoneWorldPositionToLocal(leg,position);
            bone.rotation=bone.initialRotation=ConvertBoneWorldRotationToLocal(leg,Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg));
            bone.length=radius*.2f;RecomputeBoneWorldTransforms();return index;
        }
        int Point(Vector2 p)
        {
            int index=vertices.FindIndex(v=>(v-p).sqrMagnitude<.000001f);
            if(index<0){index=vertices.Count;vertices.Add(p);uv.Add(ComputeUvForPoint(layer,p));weights.Add(default);}
            Vector2 q=p-center;float x=Vector2.Dot(q,a)/ra,y=Vector2.Dot(q,b)/rb;
            Vector3 w;
            if(followSource)
            {
                SideAssArcDistance(p,sourceArc,out var distance,out _);float radius=Vector2.Distance(p,center);
                w=AtSource(p,radius/Mathf.Max(.0001f,radius+distance));
            }
            else w=SideAssCurveWeights(Mathf.Atan2(y,x),Mathf.Sqrt(x*x+y*y));
            weights[index]=BlendBoneWeights(BuildSingleBoneWeight(slotA),w.x,BuildSingleBoneWeight(slotB),w.y,BuildSingleBoneWeight(legSlot),w.z,null);
            return index;
        }
        void Triangle(int x,int y,int z)
        {if(x!=y&&y!=z&&x!=z&&Mathf.Abs(FullBoobCross(vertices[y]-vertices[x],vertices[z]-vertices[x]))>.00001f){local.Add(x);local.Add(y);local.Add(z);}}
        void ArcPoint(float angle)=>polygon.Add(center+a*(ra*Mathf.Cos(angle))+b*(rb*Mathf.Sin(angle)));
        Vector3 AtSource(Vector2 p,float radial)
        {
            return SideAssSourceWeights(p,radial,center,a,b,sourceArc,prefixLength,perpendicularDistance);
        }
        BoneWeight Remap(BoneWeight w)
        {
            var result=w;result.boneIndex0=Slot(w.boneIndex0);result.boneIndex1=Slot(w.boneIndex1);result.boneIndex2=Slot(w.boneIndex2);result.boneIndex3=Slot(w.boneIndex3);return result;
            int Slot(int index)
            {if(index<0||index>=previous.weightBones.Length)return 0;int renderer=bones.FindIndex(bone=>bone.name==previous.weightBones[index]);return Mathf.Max(0,Array.IndexOf(staged.spriteBoneToRendererBone,renderer));}
        }
    }
    private static bool SideAssJoinIsClosed(LayerVertexData data,int[] expected)
    {
        var counts=new Dictionary<ulong,int>();
        for(int i=0;i<data.triangles.Length;i+=3)for(int j=0;j<3;j++)
        {
            ulong key=MakeEdgeKey(data.triangles[i+j],data.triangles[i+(j+1)%3]);
            counts[key]=counts.TryGetValue(key,out var count)?count+1:1;
            if(counts[key]>2)return false;
        }
        for(int i=0;i<data.boundaryEdgePairs.Length;i+=2)
        {
            Vector2 a=data.vertices[data.boundaryEdgePairs[i]],b=data.vertices[data.boundaryEdgePairs[i+1]];
            if(!Covered(a,b,expected))return false;
        }
        for(int i=0;i<expected.Length;i+=2)if(!Covered(data.vertices[expected[i]],data.vertices[expected[i+1]],data.boundaryEdgePairs))return false;
        return true;
        bool Covered(Vector2 a,Vector2 b,int[] edges)
        {
            var d=b-a;float length=d.magnitude;if(length<.001f)return true;
            var intervals=new List<Vector2>();
            for(int i=0;i<edges.Length;i+=2)
            {
                var p=data.vertices[edges[i]]-a;var q=data.vertices[edges[i+1]]-a;
                if(Mathf.Abs(FullBoobCross(d,p))/length>.002f || Mathf.Abs(FullBoobCross(d,q))/length>.002f)continue;
                float x=Vector2.Dot(p,d)/(length*length),y=Vector2.Dot(q,d)/(length*length);
                intervals.Add(new Vector2(Mathf.Min(x,y),Mathf.Max(x,y)));
            }
            float covered=0,tolerance=.002f/length;
            foreach(var interval in intervals.OrderBy(v=>v.x))
            {if(interval.y<covered)continue;if(interval.x>covered+tolerance)return false;covered=Mathf.Max(covered,interval.y);if(covered>=1-tolerance)return true;}
            return false;
        }
    }
}
#endif
