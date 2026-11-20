#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    [SerializeField] private List<Vector2> assLeftContour=new();
    [SerializeField] private List<Vector2> assRightContour=new();
    [SerializeField] private string assLayerStableKey;
    [SerializeField] private List<Vector2> assLeftRaw=new(),assRightRaw=new();
    [SerializeField] private int assDensity=32,assSmoothing=2;
    internal int AssDensity {get=>assDensity;set{value=Mathf.Clamp(value,8,128);if(value==assDensity)return;RegisterUndo("Ass density");assDensity=value;RebuildAssContours();}}
    internal int AssSmoothing {get=>assSmoothing;set{value=Mathf.Clamp(value,0,6);if(value==assSmoothing)return;RegisterUndo("Ass smoothing");assSmoothing=value;RebuildAssContours();}}
    private void RebuildAssContours()
    {
        if(assLeftRaw.Count>=3){assLeftContour.Clear();assLeftContour.AddRange(BuildSmoothedClosedBoobV2Contour(assLeftRaw,assSmoothing,assDensity));}
        if(assRightRaw.Count>=3){assRightContour.Clear();assRightContour.AddRange(BuildSmoothedClosedBoobV2Contour(assRightRaw,assSmoothing,assDensity));}
    }
    internal int AssSelection;
    private readonly List<Vector2> assStroke=new();
    private int assControl;
    internal bool IsAssStrokeActive=>assControl!=0;
    internal sealed class AssDraft
    {
        internal string Layer;
        internal Vector2[] Left,Right;
        internal Vector2[] LeftRaw,RightRaw;
    }
    internal static AssDraft CreateAssDraft(LayerInfo layer,Vector2[] left,Vector2[] right)=>
        new AssDraft{Layer=BuildKey(layer.name,layer.layerId),Left=left,Right=right};
    internal AssDraft CaptureAssDraft()=>assLeftContour.Count==0 && assRightContour.Count==0 ? null :
        new AssDraft{Layer=assLayerStableKey,Left=assLeftContour.ToArray(),Right=assRightContour.ToArray(),LeftRaw=assLeftRaw.ToArray(),RightRaw=assRightRaw.ToArray()};
    internal void RestoreAssDraft(AssDraft draft)
    {
        CancelAssStroke();assLeftContour.Clear();assRightContour.Clear();assLeftRaw.Clear();assRightRaw.Clear();assLayerStableKey=draft?.Layer;
        if(draft!=null){assLeftContour.AddRange(draft.Left);assRightContour.AddRange(draft.Right);assLeftRaw.AddRange(draft.LeftRaw??draft.Left);assRightRaw.AddRange(draft.RightRaw??draft.Right);}
    }
    internal void ClearAss(bool recordUndo=true)
    {if(recordUndo)RegisterUndo("Clear Ass");RestoreAssDraft(null);}
    internal void CancelAssStroke()
    {if(assControl!=0 && GUIUtility.hotControl==assControl)GUIUtility.hotControl=0;assControl=0;assStroke.Clear();}
    internal bool HasAssContour(int side)=>(side==0?assLeftContour:assRightContour).Count>=3;
    internal string GetAssDraftError()
    {
        if(assLeftContour.Count==0 && assRightContour.Count==0)return null;
        if(!TryGetVGenCenter(assLeftContour,out _) || !TryGetVGenCenter(assRightContour,out _))
            return "Ass: draw both closed contours.";
        return null;
    }
    internal bool HandleAssInput(Rect viewport,PsdCoordinateSpace coordinates,LayerInfo fixedLayer=null)
    {
        var e=Event.current;if(e==null)return false;
        if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape){CancelAssStroke();e.Use();return true;}
        if(e.type==EventType.MouseDown && e.button==0 && !e.alt && viewport.Contains(e.mousePosition) && GUIUtility.hotControl==0)
        {
            var layer=fixedLayer;
            if(layer==null && !TryGetSelectedLayer(out layer))
                TryFindBoobConstraintLayer(coordinates.ViewToDocument(e.mousePosition),out layer);
            if(layer==null)return false;
            var key=BuildKey(layer.name,layer.layerId);
            if(!string.IsNullOrEmpty(assLayerStableKey) && assLayerStableKey!=key)ClearAss();
            assLayerStableKey=key;assControl=GUIUtility.GetControlID(FocusType.Passive);GUIUtility.hotControl=assControl;
            assStroke.Clear();assStroke.Add(coordinates.ViewToDocument(e.mousePosition));e.Use();return true;
        }
        if(assControl==0 || GUIUtility.hotControl!=assControl || (e.type!=EventType.MouseDrag && e.type!=EventType.MouseUp))return false;
        var point=coordinates.ViewToDocument(e.mousePosition);
        if(assStroke.Count==0 || (point-assStroke[assStroke.Count-1]).sqrMagnitude>=.25f)assStroke.Add(point);
        if(e.type==EventType.MouseUp)
        {
            var contour=BuildSmoothedClosedBoobV2Contour(assStroke,assSmoothing,assDensity);
            if(contour.Count>=3 && TryGetVGenCenter(contour,out _))
            {
                RegisterUndo("Draw Ass contour");var target=AssSelection==0?assLeftContour:assRightContour;
                var raw=AssSelection==0?assLeftRaw:assRightRaw;raw.Clear();raw.AddRange(assStroke);
                target.Clear();target.AddRange(contour);AssSelection=1-AssSelection;
            }
            CancelAssStroke();
        }
        e.Use();return true;
    }
    internal void DrawAssPreview(Func<Vector2,Vector2> toView)
    {
        if(Event.current?.type!=EventType.Repaint)return;
        Handles.BeginGUI();
        foreach(var contour in new[]{assLeftContour,assRightContour})
        {
            DrawBoobV2Contour(toView,contour,Color.cyan,true);
            if(TryGetVGenCenter(contour,out var center))
            {using(new Handles.DrawingScope(Color.yellow))Handles.DrawSolidDisc(toView(center),Vector3.forward,3f);}
        }
        DrawBoobV2Contour(toView,assStroke,Color.yellow,false);Handles.EndGUI();
    }
    internal bool ApplyAss(out string spriteName,out string error)
    {
        spriteName=null;error=GetAssDraftError();
        if(error!=null || !HasAssContour(0) || !HasAssContour(1)){error??="Draw both Ass contours.";return false;}
        var layer=layerInfos.FirstOrDefault(l=>BuildKey(l.name,l.layerId)==assLayerStableKey);
        if(layer==null || !TryGetLayerVertexData(layer,out var data)){error="Ass sprite geometry is missing.";return false;}
        if(!TryFindBoneIndexByName("hips",out var hips)){error="Ass requires the hips bone.";return false;}
        TryGetVGenCenter(assLeftContour,out var a);TryGetVGenCenter(assRightContour,out var b);
        IReadOnlyList<Vector2> first=assLeftContour,second=assRightContour;
        if(a.x>b.x){(a,b)=(b,a);(first,second)=(second,first);}
        var left=MapNippleGuessToBase(layer,a);var right=MapNippleGuessToBase(layer,b);
        var ca=MapFullBoobContourToBase(layer,first);var cb=MapFullBoobContourToBase(layer,second);
        if(!TryRepairFullBoobContourPair(left,right,ca,cb,out ca,out cb)){error="Ass contours are invalid.";return false;}
        RegisterUndo("Apply Ass");
        var pivot=(left+right)*.5f;var root=FindOrCreateVGenBone(hips,"ass_root",pivot,true);
        Place(root,hips,pivot,Mathf.Max(6,Vector2.Distance(left,right)*.16f));
        var leftBone=FindOrCreateVGenBone(root,"l_ass",left,true);Place(leftBone,root,left,Mathf.Max(3,Vector2.Distance(left,right)*.12f));
        var rightBone=FindOrCreateVGenBone(root,"r_ass",right,true);Place(rightBone,root,right,Mathf.Max(3,Vector2.Distance(left,right)*.12f));
        RecomputeBoneWorldTransforms();BuildBoneDrawOrder();MarkBoneStateChanged();
        if(!TryRebuildPairedContours(data,left,right,ca,cb,hips,leftBone,rightBone,false,out _,out error))return false;
        RestorePoseAfterBoobApply();spriteName=layer.name;return true;
        void Place(int index,int parent,Vector2 point,float length)
        {
            var bone=bones[index];bone.parentId=parent;
            bone.position=bone.initialPosition=ConvertBoneWorldPositionToLocal(parent,point);
            bone.rotation=bone.initialRotation=ConvertBoneWorldRotationToLocal(parent,Quaternion.identity);
            bone.length=length;RecomputeBoneWorldTransforms();
        }
    }
    internal bool TryInheritReanimateAss(LayerInfo target,int side,out string error)
    {
        error=null;var draft=CaptureAssDraft();if(draft==null)return true;
        var source=layerInfos.FirstOrDefault(l=>BuildKey(l.name,l.layerId)==draft.Layer);
        if(source==null){error="Ass source sprite is missing.";return false;}
        if(ReferenceEquals(source,target))return true;
        return TryInheritReanimateChest(target,source,draft.Left,draft.Right,out error,side);
    }
}
#endif
