#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ReaniIKHeadNeckChecks
{
    public static string Run(string path)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject instance=null;
        try
        {
            instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
            instance.SetActive(false);var rig=instance.GetComponent<ReaniIK>();rig.RefreshFacePoseForEditing();
            var head=rig.facePose.Head;var neck=rig.facePose.Neck;
            if(!head || !neck || head.GetComponent<SpriteRenderer>() || neck.GetComponent<SpriteRenderer>())throw new Exception("Face controls bound to a sprite instead of skeleton bones.");
            rig.RestoreProceduralPoseForEditing();var h=head.localRotation;var n=neck.localRotation;
            rig.facePose.headTurn=.4f;rig.facePose.neckTurn=.3f;rig.Evaluate(0f);
            if(Mathf.Abs(Quaternion.Angle(h,head.localRotation)-.4f*rig.facePose.headTurnDegrees)>.01f ||
               Mathf.Abs(Quaternion.Angle(n,neck.localRotation)-.3f*rig.facePose.neckTurnDegrees)>.01f)throw new Exception("Head/neck rotation was not applied independently.");
            rig.RestoreProceduralPoseForEditing();
            if(Quaternion.Angle(h,head.localRotation)>.001f || Quaternion.Angle(n,neck.localRotation)>.001f)throw new Exception("Head/neck pose restoration drifted.");
            return "PASS: head and neck bind to skeleton bones; independent rotations and pose restoration are exact.";
        }
        finally{if(instance)UnityEngine.Object.DestroyImmediate(instance);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
