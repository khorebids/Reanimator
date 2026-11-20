#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ReaniIKFullResetChecks
{
    public static string Run(string prefabPath)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if(!prefab)throw new Exception("Prefab missing.");
        var scene=EditorSceneManager.NewPreviewScene();
        GameObject instance=null;int resetGroup=-1;
        try
        {
            instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            instance.SetActive(false);
            var rig=instance.GetComponent<ReaniIK>();var source=prefab.GetComponent<ReaniIK>();
            var bone=instance.transform.Find("root/skeleton").GetChild(0);
            var sourceBone=PrefabUtility.GetCorrespondingObjectFromSource(bone);
            var renderer=instance.GetComponentInChildren<SpriteRenderer>(true);
            var sourceRenderer=PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            var changedPosition=new Vector3(7,8,9);
            instance.transform.localPosition=changedPosition;instance.transform.localScale=Vector3.one*1.7f;
            bone.localPosition+=Vector3.right;bone.localRotation=Quaternion.Euler(0,0,43);bone.localScale=Vector3.one*1.2f;
            rig.bodyScale.size=1.09f;rig.surfaceEffects.darkness=.73f;rig.leftArmMask.amount=.24f;
            renderer.sortingOrder+=17;
            var extra=new GameObject("FullResetAddedObject");extra.transform.SetParent(instance.transform,false);
            instance.AddComponent<BoxCollider>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            ReaniIKEditor.ResetAllToPrefab(rig);resetGroup=Undo.GetCurrentGroup();Check();
            Undo.PerformUndo();
            if(instance.transform.localPosition!=changedPosition || Mathf.Abs(rig.surfaceEffects.darkness-.73f)>.0001f ||
                !instance.transform.Find("FullResetAddedObject") || !instance.GetComponent<BoxCollider>())throw new Exception("Undo did not restore all edits.");
            Undo.PerformRedo();Check();
            return "PASS: full prefab reset restores root/bone transforms, rig settings, masks and renderer overrides; removes added objects/components; native Undo and Redo restore correctly.";
            void Check()
            {
                if(instance.transform.localPosition!=prefab.transform.localPosition || instance.transform.localScale!=prefab.transform.localScale ||
                    bone.localPosition!=sourceBone.localPosition || Quaternion.Angle(bone.localRotation,sourceBone.localRotation)>.001f || bone.localScale!=sourceBone.localScale ||
                    rig.bodyScale.size!=source.bodyScale.size || rig.surfaceEffects.darkness!=source.surfaceEffects.darkness || rig.leftArmMask.amount!=source.leftArmMask.amount ||
                    renderer.sortingOrder!=sourceRenderer.sortingOrder || instance.transform.Find("FullResetAddedObject") || instance.GetComponent<BoxCollider>())
                    throw new Exception("Full Reset mismatch: root="+instance.transform.localPosition+", scale="+instance.transform.localScale+
                        ", bone="+bone.localPosition+", size="+rig.bodyScale.size+", darkness="+rig.surfaceEffects.darkness+
                        ", mask="+rig.leftArmMask.amount+", order="+renderer.sortingOrder+", added="+(bool)instance.transform.Find("FullResetAddedObject")+"/"+(bool)instance.GetComponent<BoxCollider>());
            }
        }
        finally
        {
            if(resetGroup>=0)Undo.RevertAllDownToGroup(resetGroup);
            if(instance)UnityEngine.Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
