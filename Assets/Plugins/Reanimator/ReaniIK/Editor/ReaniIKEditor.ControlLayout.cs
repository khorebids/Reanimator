#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKEditor
{
    private Vector2 bodyControlsOffset;
    private bool bodyControlsHidden;
    private bool limbControlsHidden;
    private string bodyControlsLayoutKey;
    private int bodyControlsGripId;
    private Vector3 bodyControlsDragAnchor;
    private Vector2 bodyControlsDragOffset;
    private float bodyControlsDragSize;

    private void LoadBodyControlsLayout(ReaniIK rig)
    {
        bodyControlsLayoutKey="Reanimator.ReaniIK.BodyControls."+rig.GetInstanceID();
        bodyControlsOffset=new Vector2(SessionState.GetFloat(bodyControlsLayoutKey+".x",0),SessionState.GetFloat(bodyControlsLayoutKey+".y",0));
        bodyControlsHidden=SessionState.GetBool(bodyControlsLayoutKey+".hidden",false);
        limbControlsHidden=SessionState.GetBool(bodyControlsLayoutKey+".limbsHidden",false);
    }

    private Vector3 DrawBodyControlsGrip(ReaniIK rig,Vector3 origin,float size)
    {
        var right=rig.transform.right;var up=rig.transform.up;
        // Above both floating panels. Offset is editor layout only, never rig data.
        var anchor=origin+right*(size*BodyColumnSpacing*.5f)+up*(size*(BodyRowSpacing+1.4f));
        anchor+=(right*bodyControlsOffset.x+up*bodyControlsOffset.y)*size;
        int hotBefore=GUIUtility.hotControl;
        bool dragging=hotBefore!=0&&hotBefore==bodyControlsGripId;
        if(dragging)anchor=bodyControlsDragAnchor;
        using var color=new Handles.DrawingScope(SceneControlColor(new Color(.65f,1,.45f)));
        EditorGUI.BeginChangeCheck();
        var moved=Handles.Slider2D(anchor,rig.transform.forward,right,up,size*.13f,Handles.RectangleHandleCap,Vector2.zero);
        bool changed=EditorGUI.EndChangeCheck();
        if(hotBefore==0&&GUIUtility.hotControl!=0)
        {
            bodyControlsGripId=GUIUtility.hotControl;bodyControlsDragAnchor=anchor;
            bodyControlsDragOffset=bodyControlsOffset;bodyControlsDragSize=size;
        }
        if(changed)
        {
            var delta=moved-bodyControlsDragAnchor;
            bodyControlsOffset=bodyControlsDragOffset+new Vector2(Vector3.Dot(delta,right),Vector3.Dot(delta,up))/Mathf.Max(.0001f,bodyControlsDragSize);
            SessionState.SetFloat(bodyControlsLayoutKey+".x",bodyControlsOffset.x);
            SessionState.SetFloat(bodyControlsLayoutKey+".y",bodyControlsOffset.y);
            SceneView.RepaintAll();
        }
        var displayed=origin+right*(size*BodyColumnSpacing*.5f)+up*(size*(BodyRowSpacing+1.4f))+(right*bodyControlsOffset.x+up*bodyControlsOffset.y)*size;
        ControlLabel(displayed+right*(size*.2f),"Move controls");
        var labelPoint=HandleUtility.WorldToGUIPoint(displayed+right*(size*.2f));
        float labelWidth=EditorStyles.label.CalcSize(new GUIContent("Move controls")).x;
        Handles.BeginGUI();
        bool hidden=GUI.Toggle(new Rect(labelPoint.x+labelWidth+8f,labelPoint.y-9f,44f,20f),bodyControlsHidden,
            new GUIContent("Hide","Hide the floating face and body controls"),EditorStyles.miniButton);
        bool limbsHidden=GUI.Toggle(new Rect(labelPoint.x+labelWidth+8f,labelPoint.y+15f,44f,20f),limbControlsHidden,
            new GUIContent("Limbs","Hide arm and leg controls, flips, masks and labels"),EditorStyles.miniButton);
        Handles.EndGUI();
        if(hidden!=bodyControlsHidden)
        {
            bodyControlsHidden=hidden;
            SessionState.SetBool(bodyControlsLayoutKey+".hidden",hidden);
            SceneView.RepaintAll();
        }
        if(limbsHidden!=limbControlsHidden)
        {
            limbControlsHidden=limbsHidden;
            SessionState.SetBool(bodyControlsLayoutKey+".limbsHidden",limbsHidden);
            SceneView.RepaintAll();
        }
        return (right*bodyControlsOffset.x+up*bodyControlsOffset.y)*size;
    }
}
#endif
