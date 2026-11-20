#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public sealed partial class ReaniIKEditor
{
    public static string VerifyAnimationRecording(string prefabPath)
    {
        var window=Resources.FindObjectsOfTypeAll<AnimationWindow>().FirstOrDefault();
        if(!window)throw new Exception("Open Animation window before running this check.");
        if(window.recording)throw new Exception("Finish the current recording before running this isolated check.");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if(!prefab)throw new Exception("Missing rig prefab.");
        var selection=Selection.objects;var oldClip=window.animationClip;float oldTime=window.time;bool preview=window.previewing;
        string originalClip=oldClip?EditorJsonUtility.ToJson(oldClip):null;
        var scene=EditorSceneManager.NewPreviewScene();
        GameObject instance=null;AnimationClip clip=null;AnimatorController controller=null;int cases=0;
        try
        {
            window.previewing=false;
            instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var rig=instance.GetComponent<ReaniIK>();rig.enabled=false;
            foreach(var b in instance.GetComponentsInChildren<SideAssRig2D>(true))b.enabled=false;
            clip=new AnimationClip{name="Temporary floating-rig recording regression",frameRate=60};
            controller=new AnimatorController();controller.AddLayer("Base Layer");
            controller.layers[0].stateMachine.AddState("Recording check").motion=clip;
            var animator=instance.GetComponent<Animator>();if(!animator)animator=instance.AddComponent<Animator>();
            animator.runtimeAnimatorController=controller;
            Selection.activeGameObject=instance;ForceAnimationSelection(window);
            if(AnimationRoot(window)!=instance)throw new Exception("Animation window did not select isolated fixture.");
            window.recording=true;window.time=.5f;
            if(!window.recording)throw new Exception("Animation window could not start recording.");

            // Every face circle/square and every scene slider's serialized channel.
            foreach(string name in new[]{"headTurn","neckTurn","upperLipHeight","upperLipSize","lowerLipHeight","lowerLipSize","lipCornersHeight","eyelidClosure"})
                Scalar(rig,"facePose."+name,.37f);
            Scalar(rig,"facePose.gaze.x",.24f);Scalar(rig,"facePose.gaze.y",-.31f);
            Scalar(rig,"bodyScale.size",.83f);
            Scalar(rig,"bodyScale.leftArmSize",1.17f);Scalar(rig,"bodyScale.rightArmSize",.89f);
            foreach(string name in new[]{"tremor","redness","sweat","darkness","contrast","desaturation"})Scalar(rig,"surfaceEffects."+name,.42f);
            foreach(string limbName in new[]{"leftArm","rightArm","leftLeg","rightLeg","leftArm2","rightArm2"})
            {
                var limb=(ReaniIK.Limb)typeof(ReaniIK).GetField(limbName).GetValue(rig);
                Boolean(rig,limbName+".flip");Boolean(rig,limbName+".invertDinoFlip");
                if(limb.target)Pose(limb.target);
            }
            foreach(string name in new[]{"leftArmMask","rightArmMask"})
            {Scalar(rig,name+".amount",.61f);Boolean(rig,name+".flip");}
            foreach(string name in new[]{"root","hips","spine","belly","shoulders"})
            {
                var bone=instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name&&!t.GetComponent<SpriteRenderer>());
                if(bone)Pose(bone);
            }
            var pairs=ReaniIKButtControls.FindPairs(instance.transform);
            if(pairs.Count==0)
            {
                foreach(var leg in new[]{rig.leftLeg.upper,rig.rightLeg.upper}.Where(t=>t))
                {
                    var a=new GameObject(ReaniIKButtControls.ParallelBoneName(leg.name)).transform;a.SetParent(leg,false);
                    var b=new GameObject(ReaniIKButtControls.PerpendicularBoneName(leg.name)).transform;b.SetParent(leg,false);b.localRotation=Quaternion.Euler(0,0,90);
                }
                pairs=ReaniIKButtControls.FindPairs(instance.transform);
            }
            if(pairs.Count==0)throw new Exception("Fixture has no Side Ass helper pairs.");
            foreach(var pair in pairs)
            {
                RecordButtUndo(pair,"Recording check Butt square");
                ReaniIKButtControls.MoveSquareWorld(pair,new Vector3(.03f,.04f,0));FinishButtEdit(pair);Undo.FlushUndoRecordObjects();
                Expect(pair.parallel,"m_LocalPosition.x");Expect(pair.perpendicular,"m_LocalPosition.y");cases++;
                RecordButtUndo(pair,"Recording check Butt circle");
                ReaniIKButtControls.RotateBoth(pair,Quaternion.Euler(0,0,12));FinishButtEdit(pair);Undo.FlushUndoRecordObjects();
                ExpectRotation(pair.parallel);ExpectRotation(pair.perpendicular);cases++;
            }
            var booties=instance.GetComponentsInChildren<SideAssRig2D>(true);
            if(booties.Length==0)booties=new[]{new GameObject("Booty slider fixture").AddComponent<SideAssRig2D>()};
            foreach(var booty in booties)
            {
                if(!booty.transform.IsChildOf(instance.transform))booty.transform.SetParent(instance.transform,false);
                booty.enabled=false;Scalar(booty,"protrusion",.29f);Scalar(booty,"compression",.38f);
            }
            // A second frame must update an existing curve and replay its value.
            window.time=.75f;Scalar(rig,"facePose.headTurn",-.21f);
            var binding=Find(rig,"facePose.headTurn");var curve=AnimationUtility.GetEditorCurve(clip,binding);
            if(!curve.keys.Any(k=>Mathf.Abs(k.time-.5f)<.001f&&Mathf.Abs(k.value-.37f)<.001f) ||
                !curve.keys.Any(k=>Mathf.Abs(k.time-.75f)<.001f&&Mathf.Abs(k.value+.21f)<.001f))throw new Exception("Slider did not key both recording frames.");
            var headBefore=rig.facePose.Head.localRotation;
            bool maskFlip=AnimationUtility.GetEditorCurve(clip,Find(rig,"leftArmMask.flip")).Evaluate(.5f)>.5f;
            clip.SampleAnimation(instance,.5f);rig.EvaluateAnimationPoseForEditing();
            if(Mathf.Abs(rig.facePose.headTurn-.37f)>.0001f||rig.leftArmMask.flip!=maskFlip)throw new Exception("Recorded float/mask direction did not replay.");
            if(Quaternion.Angle(headBefore,rig.facePose.Head.localRotation)<.1f)throw new Exception("Recorded head control did not drive the actual bone in animation preview.");
            rig.RestoreProceduralPoseForEditing();
            window.time=.75f;

            string frozen=CurveSnapshot(clip);
            NoKey("breathing / SC / SCR",()=>
            {
                RecordSceneControl(rig,"Simulation settings check",false);
                var so=new SerializedObject(rig);so.FindProperty("breathingFrequency").floatValue=2.1f;
                so.FindProperty("breathingMultiplier").floatValue=.3f;
                so.FindProperty("breathing.enabled").boolValue=!rig.breathing.enabled;
                so.FindProperty("softContour.frequency").floatValue=4.3f;
                so.FindProperty("softContour.enabled").boolValue=!rig.softContour.enabled;
                so.FindProperty("springRotation.frequency").floatValue=4.6f;
                so.FindProperty("springRotation.enabled").boolValue=!rig.springRotation.enabled;
                so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
            });
            NoKey("slider R",()=>{RecordSceneControl(rig,"Reset slider",false);rig.facePose.upperLipSize=0;});
            NoKey("Booty slider R",()=>{RecordSceneControl(booties[0],"Reset Booty",false);booties[0].compression=0;});
            NoKey("Head / Neck R",()=>{RecordSceneControl(rig,"Reset Head/Neck",false);rig.facePose.headTurn=rig.facePose.neckTurn=0;rig.facePose.gaze=Vector2.zero;});
            NoKey("circle / square R",()=>ResetControlPoseToPrefab(rig.leftArm.target));
            NoKey("Face Reset",()=>ResetFaceToPrefab(rig));
            NoKey("Body Size Reset",()=>ResetBodySizeToPrefab(rig));
            NoKey("Left Arm Size Reset",()=>ResetArmSizeToPrefab(rig,true));
            NoKey("Right Arm Size Reset",()=>ResetArmSizeToPrefab(rig,false));
            NoKey("Spring Reset",rig.ResetSpringMotion);
            int bindings=AnimationUtility.GetCurveBindings(clip).Length;
            NoKey("Full Reset",()=>ResetAllToPrefab(rig));
            if(oldClip&&EditorJsonUtility.ToJson(oldClip)!=originalClip)throw new Exception("User clip changed during isolated test.");
            return $"PASS: {cases} floating-control recording cases, {bindings} curve bindings; two frames and float/bool replay; breathing/SC/SCR and all reset categories add or modify zero keys. User clip unchanged.";

            void Scalar(Object owner,string path,float value)
            {
                RecordSceneControl(owner,"Recording check "+path);
                SetField(owner,path,value);if(owner is ReaniIK r)r.SynchronizeAnimationControls();Undo.FlushUndoRecordObjects();Expect(owner,path);cases++;
            }
            void Boolean(Object owner,string path)
            {
                RecordSceneControl(owner,"Recording check "+path);
                var so=new SerializedObject(owner);var p=so.FindProperty(path);
                SetField(owner,path,!p.boolValue);if(owner is ReaniIK r)r.SynchronizeAnimationControls();Undo.FlushUndoRecordObjects();Expect(owner,path);cases++;
            }
            void Pose(Transform bone)
            {
                RecordSceneControl(bone,"Recording check square");bone.localPosition+=new Vector3(.02f,.03f,0);Undo.FlushUndoRecordObjects();Expect(bone,"m_LocalPosition.x");cases++;
                RecordSceneControl(bone,"Recording check circle");bone.localRotation*=Quaternion.Euler(0,0,7);Undo.FlushUndoRecordObjects();ExpectRotation(bone);cases++;
            }
            EditorCurveBinding Find(Object owner,string property)
            {
                if(owner is ReaniIK)property=ReaniIK.AnimationControlProperty(property);
                var component=(Component)owner;string path=AnimationUtility.CalculateTransformPath(component.transform,instance.transform);
                var bindings=AnimationUtility.GetCurveBindings(clip);
                if(!bindings.Any(b=>b.path==path&&b.type==owner.GetType()&&b.propertyName==property))throw new Exception("No recorded key for "+owner.name+" / "+property+"; active root="+AnimationRoot(window)+"; recording="+window.recording+"; curves="+string.Join(",",bindings.Select(b=>b.propertyName))+"; animatable="+string.Join(",",AnimationUtility.GetAnimatableBindings(component.gameObject,instance).Where(b=>b.propertyName.Contains("facePose")).Select(b=>b.propertyName)));
                return bindings.First(b=>b.path==path&&b.type==owner.GetType()&&b.propertyName==property);
            }
            void Expect(Object owner,string property)
            {
                var c=AnimationUtility.GetEditorCurve(clip,Find(owner,property));
                if(!c.keys.Any(k=>Mathf.Abs(k.time-window.time)<.001f))throw new Exception("No key at current frame: "+property);
            }
            void ExpectRotation(Transform bone)
            {
                string path=AnimationUtility.CalculateTransformPath(bone,instance.transform);
                if(!AnimationUtility.GetCurveBindings(clip).Any(b=>b.path==path&&b.type==typeof(Transform)&&(b.propertyName.Contains("Euler")||b.propertyName.Contains("Rotation"))))throw new Exception("No rotation key: "+bone.name);
            }
            void NoKey(string label,Action action)
            {
                action();if(rig)rig.SynchronizeAnimationControls();Undo.FlushUndoRecordObjects();
                if(CurveSnapshot(clip)!=frozen)throw new Exception(label+" changed animation keys.");cases++;
            }
        }
        finally
        {
            window.recording=false;window.previewing=false;
            Selection.objects=selection;ForceAnimationSelection(window);window.animationClip=oldClip;window.time=oldTime;window.previewing=preview;
            if(instance)Object.DestroyImmediate(instance);
            if(controller)Object.DestroyImmediate(controller);
            if(clip)Object.DestroyImmediate(clip);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static void ForceAnimationSelection(AnimationWindow window)=>typeof(AnimationWindow).GetMethod("OnSelectionChangeInternal",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{true});
    private static GameObject AnimationRoot(AnimationWindow window)
    {
        var state=typeof(AnimationWindow).GetProperty("state",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
        return (GameObject)state.GetType().GetProperty("activeRootGameObject").GetValue(state);
    }
    private static string CurveSnapshot(AnimationClip clip)=>Newtonsoft.Json.JsonConvert.SerializeObject(AnimationUtility.GetCurveBindings(clip).Select(b=>new{b.path,type=b.type.FullName,b.propertyName,keys=AnimationUtility.GetEditorCurve(clip,b).keys}));
    private static void SetField(object owner,string path,object value)
    {
        var parts=path.Split('.');Set(owner,0);
        object Set(object current,int index)
        {
            var field=current.GetType().GetField(parts[index]);
            field.SetValue(current,index==parts.Length-1?value:Set(field.GetValue(current),index+1));return current;
        }
    }
}
#endif
