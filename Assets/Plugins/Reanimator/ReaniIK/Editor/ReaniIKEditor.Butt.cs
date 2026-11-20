#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKEditor
{
    private const float ButtSquareStrength=2f;
    private List<ReaniIKButtControls.Pair> buttPairs=new();
    private int activeButtControl;
    private Transform activeButtLeg;
    private Vector3 activeButtCenter;

    private void InitializeButtControls()
    {
        RefreshButtBindings();
        EditorApplication.hierarchyChanged+=RefreshButtBindings;
    }

    private void OnDisable() => EditorApplication.hierarchyChanged-=RefreshButtBindings;

    private void RefreshButtBindings()
    {
        if(target is ReaniIK rig)buttPairs=ReaniIKButtControls.FindPairs(rig.transform);
        SceneView.RepaintAll();
    }

    private void DrawButtControls(ReaniIK rig,Vector3 center,float size)
    {
        if(!rig.showButtControl)return;
        var root=rig.transform;
        using var scope=new Handles.DrawingScope(SceneControlColor(new Color(.65f,1,.45f)));
        int row=0;
        foreach(var pair in buttPairs)
        {
            if(!pair.IsValid)continue;
            var position=center+root.right*(size*1.65f*row++);
            int hotBefore=GUIUtility.hotControl;
            if(hotBefore!=0 && hotBefore==activeButtControl && activeButtLeg==pair.leg)position=activeButtCenter;
            float radius=size*.6f*SceneControlScale;
            DrawCircleHeader(root,position,radius,"Butt / "+pair.leg.name,pair.parallel,pair.perpendicular);

            Handles.DrawWireDisc(position,root.forward,radius,3f);
            var startRotation=pair.parallel.rotation;
            EditorGUI.BeginChangeCheck();
            var rotation=Handles.Disc(startRotation,position,root.forward,radius,false,0f);
            if(EditorGUI.EndChangeCheck())
            {
                RecordButtUndo(pair,"Rotate Butt helpers");
                ReaniIKButtControls.RotateBoth(pair,rotation*Quaternion.Inverse(startRotation));
                FinishButtEdit(pair);
            }

            var thumb=position+ReaniIKButtControls.SquareWorldOffset(pair)/ButtSquareStrength;
            EditorGUI.BeginChangeCheck();
            var moved=Handles.Slider2D(thumb,root.forward,root.right,root.up,size*.10f*SceneControlScale,Handles.RectangleHandleCap,Vector2.zero);
            if(EditorGUI.EndChangeCheck())
            {
                RecordButtUndo(pair,"Move Butt helpers");
                ReaniIKButtControls.MoveSquareWorld(pair,(moved-thumb)*ButtSquareStrength);
                FinishButtEdit(pair);
            }
            if(hotBefore==0 && GUIUtility.hotControl!=0)
            {activeButtControl=GUIUtility.hotControl;activeButtLeg=pair.leg;activeButtCenter=position;}
        }
    }

    private static void RecordButtUndo(ReaniIKButtControls.Pair pair,string label) =>
        Undo.RecordObjects(new Object[]{pair.parallel,pair.perpendicular},label);

    private static void FinishButtEdit(ReaniIKButtControls.Pair pair)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(pair.parallel);
        PrefabUtility.RecordPrefabInstancePropertyModifications(pair.perpendicular);
        EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
    }
}
#endif
