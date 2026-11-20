#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKEditor
{
    internal static void RecordSceneControl(Object owner,string operation,bool recordKeys=true)
    {
        if(!owner)return;
        if(owner is ReaniIK rig)rig.SynchronizeAnimationControls();
        if(recordKeys)Undo.RecordObject(owner,operation);
        else
        {
            Undo.FlushUndoRecordObjects();
            // Complete-object Undo does not submit property diffs to auto-key.
            // Keep Undo for resets/settings while leaving the current clip intact.
            Undo.RegisterCompleteObjectUndo(owner,operation);
        }
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
}
#endif
