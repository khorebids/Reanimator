#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    [Serializable] private sealed class SideAssRecipe
    {
        public string leg, volume, press, layer;
        public string referenceBone;
        public float referenceDistance;
        public bool manualFan;
        public int fanVersion,fanPointCount;
        public bool fanFlip;
        public Vector2[] drawingDoc;
        public Vector2[] baseDrawing;
        public string geometryStamp;
        public int[] validatedExterior;
        public Vector2[] arcSamples,weightArc;
        public Vector2 weightCenter,weightA,weightB;
        public float weightPrefix,weightPerpendicular,weightRA,weightRB;
        public bool sourceWeights;
        public string curveStamp;
        public Vector2[] contour;
        public float radius, restLegAngle, bendProtrusion = .35f;
        public string[] weightBones;
        public List<SideAssSavedWeight> weights = new();
    }
    [Serializable] private sealed class SideAssSavedWeight { public Vector2 point; public BoneWeight original, applied; }
    [Serializable] private sealed class SideAssFile { public List<SideAssRecipe> regions = new(); public int enabledTargets; }
    private SideAssFile sideAssFile;
    private string sideAssAsset;
    private readonly List<Vector2> sideAssContour = new(), sideAssStroke = new();
    internal bool SideAssDrawing { get; set; }
    internal float SideAssProtrusion { get; set; } = .35f;
    internal bool SideAssFlip { get; set; }
    internal bool HasSideAssContour => sideAssContour.Count >= 2;
    internal float SideAssPreviewCompression { get; set; }
    internal float SideAssPreviewProtrusion { get; set; }
    internal void RefreshSideAssPreview() => InvalidateBonePose();
    private int sideAssControl;
    private Vector2[] sideAssAutoBaseArc;
    private Vector2[] sideAssAutoDrawingDoc;
    private string sideAssAutoLeg;
    private string SideAssPath => string.IsNullOrEmpty(cachedPath) ? null : Path.Combine("Library","Reanimator","SideAss",AssetDatabase.AssetPathToGUID(cachedPath)+".json");
    private void LoadSideAss()
    {
        if (sideAssFile != null && sideAssAsset == cachedPath) return;
        sideAssAsset = cachedPath; sideAssFile = new SideAssFile(); sideAssContour.Clear();
        if (SideAssPath != null && File.Exists(SideAssPath))
            sideAssFile = JsonUtility.FromJson<SideAssFile>(File.ReadAllText(SideAssPath)) ?? new SideAssFile();
    }
    internal void ClearSideAssContour()
    {
        SideAssTestEnabled=false;
        if (sideAssControl != 0 && GUIUtility.hotControl == sideAssControl) GUIUtility.hotControl = 0;
        sideAssControl = 0; SideAssDrawing = false; sideAssStroke.Clear(); sideAssContour.Clear();
        sideAssAutoBaseArc=null;
        sideAssAutoDrawingDoc=null;sideAssAutoLeg=null;
    }
    private void HandleSideAssInput(Rect area, PsdCoordinateSpace coordinates)
    {
        LoadSideAss();
        if(HandleSideAssTestInput(area,coordinates))return;
        var e = Event.current;
        if (e == null || !SideAssDrawing || coordinates == null) return;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ClearSideAssContour(); e.Use(); return; }
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && area.Contains(e.mousePosition) && GUIUtility.hotControl == 0)
        { sideAssControl = GUIUtility.GetControlID(FocusType.Passive); GUIUtility.hotControl = sideAssControl; sideAssStroke.Clear(); }
        else if (sideAssControl == 0 || GUIUtility.hotControl != sideAssControl || (e.type != EventType.MouseDrag && e.type != EventType.MouseUp)) return;
        var p = coordinates.ViewToDocument(e.mousePosition);
        if (sideAssStroke.Count == 0 || Vector2.Distance(sideAssStroke[sideAssStroke.Count-1],p) > .5f) sideAssStroke.Add(p);
        if (e.type == EventType.MouseUp)
        {
            var contour = sideAssStroke.ToArray();
            ClearSideAssContour(); sideAssContour.AddRange(contour);
        }
        e.Use();
    }
    private void DrawSideAssPreview(Func<Vector2,Vector2> toCanvas)
    {
        LoadSideAss(); Handles.BeginGUI();
        DrawBoobV2Contour(toCanvas,sideAssContour,new Color(.1f,1,.85f),false);
        DrawBoobV2Contour(toCanvas,sideAssStroke,Color.yellow,false);
        DrawSideAssTestHandles(toCanvas);
        Handles.EndGUI();
    }
    private bool ApplySideAssLegacy(out string message)
    {
        LoadSideAss(); message = "Select a leg sprite, its upper leg bone and draw the soft area.";
        if (!HasSideAssContour || selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null ||
            !TryGetSelectedLayer(out var layer) || !TryGetLayerVertexData(layer,out var data) || data?.vertices == null) return false;
        int leg = selectedBoneIndex, pelvis = bones[leg].parentId;
        if (pelvis < 0 || pelvis >= bones.Count) { message = "Upper leg needs a pelvis parent."; return false; }
        var contour = MapFullBoobContourToBase(layer,sideAssContour);
        if (!TryGetVGenCenter(contour,out var center)) { message = "Draw a closed contour with an interior."; return false; }
        float radius = contour.Max(p=>Vector2.Distance(p,center));
        if (radius < .001f) { message = "The contour is too small."; return false; }
        var staged = CloneProjectorLayerData(data);
        var previous = sideAssFile.regions.Find(r=>r.leg==bones[leg].name && r.layer==BuildSelectionKey(layer));
        if(previous?.weights != null && previous.weightBones != null)
            foreach(var saved in previous.weights)
                for(int i=0;i<staged.vertices.Length;i++)
                    if((staged.vertices[i]-saved.point).sqrMagnitude<.0001f && staged.boneWeights[i].Equals(Remap(saved.applied,previous.weightBones)))
                    { staged.boneWeights[i]=Remap(saved.original,previous.weightBones); break; }
        int initialCount = data.vertices.Length;
        // Insert locally; no vertex, UV or prior geometry outside this region is removed.
        foreach (var p in contour) Insert(p);
        foreach (float ring in new[]{.7f,.35f}) foreach(var p in contour) Insert(Vector2.Lerp(center,p,ring));
        Insert(center);
        var affected = Enumerable.Range(0,staged.vertices.Length).Where(i=>IsStrictlyInsideFullBoobContour(staged.vertices[i],contour) || SideAssDistance(staged.vertices[i],contour)<.01f).ToArray();
        if (affected.Length < 3) { message = "Contour must overlap the leg mesh."; return false; }
        Vector2 legDirection = bones[leg].worldRotation * Vector3.right;
        Vector2 outward = new Vector2(-legDirection.y,legDirection.x);
        if (Vector2.Dot(outward,center-bones[leg].worldPosition)<0) outward=-outward;
        if (SideAssFlip) outward=-outward;
        // A small side leg contour can straddle the bone. Its center determines the outward side.
        Quaternion rotation = Quaternion.Euler(0,0,Mathf.Atan2(outward.y,outward.x)*Mathf.Rad2Deg);
        RegisterUndo("Apply Booty Side Ass");
        int volume=FindOrCreateVGenBone(pelvis,bones[leg].name+"_booty",center,true);
        Setup(volume,pelvis);
        int press=FindOrCreateVGenBone(volume,bones[leg].name+"_booty_press",center,true);
        Setup(press,volume);
        if (!TryEnsureFullBoobSpriteBone(staged,leg,out var legSlot) || !TryEnsureFullBoobSpriteBone(staged,pelvis,out var hipsSlot) ||
            !TryEnsureFullBoobSpriteBone(staged,volume,out var volumeSlot) || !TryEnsureFullBoobSpriteBone(staged,press,out var pressSlot))
            throw new InvalidOperationException("Could not bind Side Ass helpers.");
        if (!TryBuildBindInverseMatrixForRendererBone(staged,volume,out staged.bindBoneInverseMatrices[volumeSlot]) ||
            !TryBuildBindInverseMatrixForRendererBone(staged,press,out staged.bindBoneInverseMatrices[pressSlot]))
            throw new InvalidOperationException("Could not rebuild Side Ass rest bindings.");
        EnsureBoneWeightsArraySize(staged,staged.vertices.Length);
        var backups = new List<SideAssSavedWeight>();
        var boundary = new HashSet<int>(staged.boundaryEdgePairs ?? Array.Empty<int>());
        float reach = Mathf.Max(radius*.2f, contour.Max(p=>Vector2.Dot(p-bones[leg].worldPosition,outward)));
        foreach(int i in affected)
        {
            var p=staged.vertices[i];
            float region=boundary.Contains(i) ? 1 : Mathf.SmoothStep(0,1,SideAssDistance(p,contour)/Mathf.Max(.001f,radius*.2f));
            float soft=Mathf.SmoothStep(0,1,Mathf.Clamp01(Vector2.Dot(p-bones[leg].worldPosition,outward)/reach));
            var tissue=BuildLimbTwoBoneWeight(hipsSlot,1-soft,pressSlot,soft);
            var original=staged.boneWeights[i];
            staged.boneWeights[i]=BlendBoneWeights(original,1-region,tissue,region,default,0,null);
            backups.Add(new SideAssSavedWeight {point=p,original=original,applied=staged.boneWeights[i]});
        }
        data.vertices=staged.vertices; data.vertexUvs=staged.vertexUvs; data.triangles=staged.triangles;
        data.allEdgePairs=staged.allEdgePairs; data.boundaryEdgePairs=staged.boundaryEdgePairs; data.boneWeights=staged.boneWeights;
        data.spriteBoneToRendererBone=staged.spriteBoneToRendererBone; data.bindBoneInverseMatrices=staged.bindBoneInverseMatrices; data.usesSpriteBindPoses=staged.usesSpriteBindPoses;
        var recipe=new SideAssRecipe {leg=bones[leg].name,volume=bones[volume].name,press=bones[press].name,layer=BuildSelectionKey(layer),contour=contour.ToArray(),radius=radius,restLegAngle=bones[leg].rotation.eulerAngles.z,bendProtrusion=SideAssProtrusion};
        recipe.weights=backups; recipe.weightBones=staged.spriteBoneToRendererBone.Select(b=>bones[b].name).ToArray();
        var reference=bones.Where(b=>b!=null).OrderByDescending(b=>(b.worldPosition-bones[leg].worldPosition).sqrMagnitude).First();
        recipe.referenceBone=reference.name; recipe.referenceDistance=Vector2.Distance(reference.worldPosition,bones[leg].worldPosition);
        sideAssFile.regions.RemoveAll(r=>r.leg==recipe.leg); sideAssFile.regions.Add(recipe);
        Directory.CreateDirectory(Path.GetDirectoryName(SideAssPath)); File.WriteAllText(SideAssPath,JsonUtility.ToJson(sideAssFile,true));
        BuildBoneDrawOrder(); MarkBoneStateChanged(); MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data,BuildSelectionKey(layer),BuildKey(layer.name,layer.layerId));
        message=$"Side Ass: {affected.Length} local vertices, {staged.vertices.Length-initialCount} added. Cook prefab to use protrusion and compression controls.";
        return true;

        void Setup(int i,int parent)
        {
            var b=bones[i]; b.position=b.initialPosition=ConvertBoneWorldPositionToLocal(parent,center);
            b.rotation=b.initialRotation=ConvertBoneWorldRotationToLocal(parent,rotation); b.length=radius*.2f;
            RecomputeBoneWorldTransforms();
        }
        void Insert(Vector2 p)
        {
            if (staged.vertices.Any(v=>(v-p).sqrMagnitude<.0001f)) return;
            if (TryFindContainingTriangle(staged.triangles,staged.vertices,p,out _,out _,out _,out _,out _,out _,out _,out _)) InsertAppliedVertexIntoLayerData(staged,p,true);
        }
        BoneWeight Remap(BoneWeight w,string[] names)
        {
            var result=w;
            result.boneIndex0=Slot(w.boneIndex0); result.boneIndex1=Slot(w.boneIndex1);
            result.boneIndex2=Slot(w.boneIndex2); result.boneIndex3=Slot(w.boneIndex3);
            return result;
            int Slot(int i)
            {
                if(i<0 || i>=names.Length) return 0;
                int renderer=bones.FindIndex(b=>b!=null && b.name==names[i]);
                return Mathf.Max(0,Array.IndexOf(staged.spriteBoneToRendererBone,renderer));
            }
        }
    }
    private static float SideAssDistance(Vector2 p,IReadOnlyList<Vector2> contour)
    {
        float d=float.PositiveInfinity;
        for(int i=0;i<contour.Count;i++) d=Mathf.Min(d,DistancePointToSegment(p,contour[i],contour[(i+1)%contour.Count]));
        return d;
    }
    private void ConfigureSideAssRuntime(IReadOnlyList<Transform> transforms)
    {
        LoadSideAss(); ConfigureSideAssRecipes(sideAssFile,transforms);
    }
    internal static void ConfigureSavedSideAssRuntime(string asset,IReadOnlyList<Transform> transforms)
    {
        string path=Path.Combine("Library","Reanimator","SideAss",AssetDatabase.AssetPathToGUID(asset)+".json");
        if(File.Exists(path)) ConfigureSideAssRecipes(JsonUtility.FromJson<SideAssFile>(File.ReadAllText(path)),transforms);
    }
    private static void ConfigureSideAssRecipes(SideAssFile file,IReadOnlyList<Transform> transforms)
    {
        if(file?.regions==null)return;
        foreach(var recipe in file.regions)
        {
            var leg=transforms.FirstOrDefault(t=>t && t.name==recipe.leg);
            var volume=transforms.FirstOrDefault(t=>t && t.name==recipe.volume);
            var press=transforms.FirstOrDefault(t=>t && t.name==recipe.press);
            if (recipe.manualFan)
            {
                if(volume && volume.TryGetComponent<SideAssRig2D>(out var old)) UnityEngine.Object.DestroyImmediate(old);
                continue;
            }
            // A removed generated region has no helpers and must not create an orphan rig.
            if (!leg || !volume || !press) continue;
            var reference=transforms.FirstOrDefault(t=>t && t.name==recipe.referenceBone);
            if(!reference || recipe.referenceDistance<.0001f) throw new InvalidOperationException("Side Ass reference skeleton is incomplete: "+recipe.leg);
            float doc=recipe.referenceDistance;
            float units=volume.parent.InverseTransformVector(reference.position-leg.position).magnitude;
            var runtime=volume.GetComponent<SideAssRig2D>(); if(!runtime)runtime=volume.gameObject.AddComponent<SideAssRig2D>();
            runtime.Configure(leg,press,recipe.radius*units/doc,recipe.bendProtrusion);
        }
    }
    private bool ApplySideAssPreview()
    {
        LoadSideAss(); bool changed=false;
        foreach(var recipe in sideAssFile.regions)
        {
            if(recipe.manualFan) continue;
            if(!TryFindBoneIndexByName(recipe.volume,out var v) || !TryFindBoneIndexByName(recipe.press,out var p) || !TryFindBoneIndexByName(recipe.leg,out var l)) continue;
            float bend=Mathf.SmoothStep(0,1,Mathf.Abs(Mathf.DeltaAngle(recipe.restLegAngle,bones[l].rotation.eulerAngles.z))/90);
            Vector2 shift=bones[v].worldRotation*Vector3.right*(recipe.radius*(SideAssPreviewProtrusion+recipe.bendProtrusion*bend));
            bones[v].worldPosition+=shift; bones[p].worldPosition+=shift;
            var scale=SideAssRig2D.CompressionScale(SideAssPreviewCompression);
            bones[p].previewScale=new Vector3(scale.x,scale.y,1);
            changed=true;
        }
        return changed;
    }
}
#endif
