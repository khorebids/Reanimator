#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private LayerVertexData sideAssTestData;
    private SideAssRecipe sideAssTestRecipe;
    private List<BoneInfo> sideAssTestBones;
    private Vector2[] sideAssTestVertices;
    private string sideAssTestAsset;
    private bool sideAssTestDrawing;
    private bool sideAssBuildingTest;
    private string sideAssTestLastCurves;
    private float sideAssTestLastRotation=float.NaN;
    private Vector2 sideAssTestLastBack,sideAssTestLastPerpendicular;
    private int sideAssTestDrag,sideAssTestControl;
    internal bool SideAssTestEnabled { get; set; }
    internal float SideAssTestRotation { get; set; }
    internal Vector2 SideAssTestBack { get; set; }
    internal Vector2 SideAssTestPerpendicular { get; set; }
    internal bool SideAssTestWire { get; set; }=true;
    private bool SideAssTestVisible => sideAssTestDrawing && SideAssTestEnabled && sideAssTestData!=null &&
        cachedPath==sideAssTestAsset && TryGetSelectedLayer(out var layer) && BuildSelectionKey(layer)==sideAssTestRecipe.layer;

    internal bool BuildSideAssTest(out string message)
    {
        SideAssTestEnabled=false;
        LoadSideAss();message="Select the leg and draw an arc first.";
        if(!TryGetSelectedLayer(out var layer))return false;
        int inputBone=SideAssControlBoneIndex(selectedBoneIndex);
        var recipe=sideAssFile.regions.Find(r=>r.manualFan && r.layer==BuildSelectionKey(layer) &&
            (inputBone<0 || r.leg==bones[inputBone].name || r.volume==bones[inputBone].name || r.press==bones[inputBone].name));
        if(!HasSideAssContour && recipe?.drawingDoc!=null)sideAssContour.AddRange(recipe.drawingDoc);
        var originalBones=bones;int originalSelection=selectedBoneIndex;
        try
        {
            // The production builder is used with detached bones and a staged mesh.
            // Its preview branch returns before any undo, asset, recipe or dirty-state writes.
            bones=CloneBones(originalBones);sideAssBuildingTest=true;
            selectedBoneIndex=inputBone;
            foreach(var bone in bones)
            {
                bone.position=bone.initialPosition;
                if(!bone.initialRotation.Equals(default(Quaternion)))bone.rotation=bone.initialRotation;
                bone.previewScale=Vector3.one;
            }
            new BonePreviewTransform(bonesTreatAsWorldSpace,rotateLocalBoneOffsetsByParent).ResolveAll(bones);
            if(recipe!=null && (selectedBoneIndex<0 || bones[selectedBoneIndex].name==recipe.volume || bones[selectedBoneIndex].name==recipe.press))
                TryFindBoneIndexByName(recipe.leg,out selectedBoneIndex);
            if(!ApplySideAssCore(out message,true))return false;
            // Pseudo geometry starts unposed. Imported or previously edited bind matrices
            // must not carry a hidden deformation into this independent test rig.
            for(int slot=0;slot<sideAssTestData.spriteBoneToRendererBone.Length;slot++)
            {
                int index=sideAssTestData.spriteBoneToRendererBone[slot];
                if(index<0 || index>=sideAssTestBones.Count)continue;
                var bone=sideAssTestBones[index];
                var p=sideAssTestData.usesSpriteBindPoses?DocumentToSpriteSkinPosition(sideAssTestData,bone.worldPosition):bone.worldPosition;
                sideAssTestData.bindBoneInverseMatrices[slot]=Matrix4x4.TRS(p,bone.worldRotation,Vector3.one).inverse;
            }
            sideAssTestAsset=cachedPath;SideAssTestEnabled=true;sideAssTestLastCurves=null;
        }
        catch(Exception error){message="Preview: "+error.Message;return false;}
        finally{bones=originalBones;selectedBoneIndex=originalSelection;sideAssBuildingTest=false;}
        UpdateSideAssTestPose();return true;
    }

    internal void ResetSideAssTestPose()
    {
        SideAssTestRotation=0;SideAssTestBack=SideAssTestPerpendicular=Vector2.zero;
        UpdateSideAssTestPose();
    }

    internal void UpdateSideAssTestPose()
    {
        if(sideAssTestData==null || sideAssTestBones==null)return;
        var curveStamp=SideAssCurveStamp;
        if(sideAssTestLastCurves==curveStamp && sideAssTestLastRotation==SideAssTestRotation &&
           sideAssTestLastBack==SideAssTestBack && sideAssTestLastPerpendicular==SideAssTestPerpendicular)return;
        var realBones=bones;
        try
        {
            bones=CloneBones(sideAssTestBones);
            if(!TryFindBoneIndexByName(sideAssTestRecipe.leg,out var leg) ||
               !TryFindBoneIndexByName(sideAssTestRecipe.volume,out var back) ||
               !TryFindBoneIndexByName(sideAssTestRecipe.press,out var perpendicular))return;
            if(sideAssTestLastCurves!=curveStamp)RefreshSideAssCurveWeights(true);
            bones[leg].rotation*=Quaternion.Euler(0,0,SideAssTestRotation);
            new BonePreviewTransform(bonesTreatAsWorldSpace,rotateLocalBoneOffsetsByParent).ResolveAll(bones);
            // Offsets are expressed in the leg's axes regardless of importer offset convention.
            var backOffset=(Vector2)(SideAssTestOrientation*(Vector3)SideAssTestBack);
            var perpendicularOffset=(Vector2)(SideAssTestOrientation*(Vector3)SideAssTestPerpendicular);
            bones[back].worldPosition+=backOffset;bones[perpendicular].worldPosition+=perpendicularOffset;
            int skinBack=SideAssSkinBoneIndex(back),skinPerpendicular=SideAssSkinBoneIndex(perpendicular);
            if(skinBack!=back)bones[skinBack].worldPosition+=backOffset;
            if(skinPerpendicular!=perpendicular)bones[skinPerpendicular].worldPosition+=perpendicularOffset;
            var data=sideAssTestData;
            data.skinMatrices=new Matrix4x4[data.bindBoneInverseMatrices.Length];
            data.deformedVertices=new Vector2[data.vertices.Length];
            BuildSkinMatrices(data);DeformLayerVertices(data);sideAssTestVertices=data.deformedVertices;
            sideAssTestLastCurves=curveStamp;sideAssTestLastRotation=SideAssTestRotation;
            sideAssTestLastBack=SideAssTestBack;sideAssTestLastPerpendicular=SideAssTestPerpendicular;
        }
        finally{bones=realBones;}
    }

    private Vector2 SideAssTestPivot => sideAssTestBones.First(b=>b.name==sideAssTestRecipe.leg).worldPosition;
    private Quaternion SideAssTestOrientation => sideAssTestBones.First(b=>b.name==sideAssTestRecipe.leg).worldRotation*Quaternion.Euler(0,0,SideAssTestRotation);
    private Vector2 SideAssTestHandle(int index)
    {
        if(index==1)return SideAssTestPivot+(Vector2)(SideAssTestOrientation*Vector3.right)*(sideAssTestRecipe.radius*.65f);
        var local=index==2?SideAssTestBack:SideAssTestPerpendicular;
        var direction=index==2?sideAssTestRecipe.weightA:sideAssTestRecipe.weightB;
        // Small offset knobs keep the two zero-position helpers separately selectable.
        var knob=(Vector2)(Quaternion.Euler(0,0,SideAssTestRotation)*(Vector3)direction)*sideAssTestRecipe.radius*.18f;
        return SideAssTestPivot+(Vector2)(SideAssTestOrientation*(Vector3)local)+knob;
    }

    private bool HandleSideAssTestInput(Rect area,PsdCoordinateSpace coordinates)
    {
        if(!SideAssTestVisible || SideAssDrawing)return false;
        var e=Event.current;if(e==null)return false;
        if(e.type==EventType.MouseDown && e.button==0 && !e.alt && area.Contains(e.mousePosition) && GUIUtility.hotControl==0)
        {
            for(int i=1;i<=3;i++)if(Vector2.Distance(e.mousePosition,coordinates.DocumentToView(SideAssTestHandle(i)))<12)
            {sideAssTestDrag=i;sideAssTestControl=GUIUtility.GetControlID(FocusType.Passive);GUIUtility.hotControl=sideAssTestControl;e.Use();return true;}
        }
        if(sideAssTestControl==0 || GUIUtility.hotControl!=sideAssTestControl)return false;
        if(e.type==EventType.MouseDrag)
        {
            var p=coordinates.ViewToDocument(e.mousePosition);
            if(sideAssTestDrag==1)
            {
                var d=p-SideAssTestPivot;
                float baseline=sideAssTestBones.First(b=>b.name==sideAssTestRecipe.leg).worldRotation.eulerAngles.z;
                SideAssTestRotation=Mathf.DeltaAngle(baseline,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);
            }
            else
            {
                var delta=(Vector2)(Quaternion.Inverse(SideAssTestOrientation)*(Vector3)(p-SideAssTestHandle(sideAssTestDrag)));
                if(sideAssTestDrag==2)SideAssTestBack+=delta;else SideAssTestPerpendicular+=delta;
            }
            UpdateSideAssTestPose();GUI.changed=true;e.Use();return true;
        }
        if(e.type==EventType.MouseUp || (e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape))
        {GUIUtility.hotControl=0;sideAssTestControl=sideAssTestDrag=0;e.Use();return true;}
        return false;
    }

    private void DrawSideAssTestHandles(Func<Vector2,Vector2> toCanvas)
    {
        if(!SideAssTestVisible)return;
        var previous=Handles.color;
        if(SideAssTestWire && sideAssTestVertices!=null)
        {
            Handles.color=new Color(.1f,1,.85f,.5f);
            var edges=sideAssTestData.allEdgePairs;
            for(int i=0;i+1<edges.Length;i+=2)Handles.DrawLine(toCanvas(sideAssTestVertices[edges[i]]),toCanvas(sideAssTestVertices[edges[i+1]]));
        }
        for(int i=1;i<=3;i++)
        {
            var p=toCanvas(SideAssTestHandle(i));Handles.color=i==1?Color.yellow:i==2?Color.magenta:Color.cyan;
            Handles.DrawAAPolyLine(3,toCanvas(SideAssTestPivot),p);
            Handles.DrawSolidDisc(p,Vector3.forward,7);
            GUI.Label(new Rect(p.x+10,p.y-20,180,22),i==1?"Preview: rotate leg":i==2?"Preview: back":"Preview: perpendicular");
        }
        Handles.color=previous;
    }
}
#endif
