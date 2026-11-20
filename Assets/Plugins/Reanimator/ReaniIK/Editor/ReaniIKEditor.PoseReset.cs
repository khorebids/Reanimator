#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKEditor
{
    private static bool DrawFullResetButton(ReaniIK rig,Vector3 center,float radius)
    {
        var point=HandleUtility.WorldToGUIPoint(center);float bottom=point.y;
        foreach(var axis in new[]{rig.transform.up,-rig.transform.up,rig.transform.right,-rig.transform.right})
            bottom=Mathf.Max(bottom,HandleUtility.WorldToGUIPoint(center+axis*radius).y);
        Handles.BeginGUI();bool clicked;
        using(new EditorGUI.DisabledScope(!PrefabUtility.GetCorrespondingObjectFromSource(rig)))
            clicked=GUI.Button(new Rect(point.x-43f,bottom+7f,86f,20f),
                new GUIContent("Full Reset","Reset the entire character to its prefab, including all controls, bones, effects and overrides. Undo is available."),EditorStyles.miniButton);
        Handles.EndGUI();
        if(clicked)ResetAllToPrefab(rig);
        return clicked;
    }

    internal static void ResetAllToPrefab(ReaniIK rig)
    {
        if(!rig)return;
        var instance=PrefabUtility.GetNearestPrefabInstanceRoot(rig.gameObject);
        var source=instance?PrefabUtility.GetCorrespondingObjectFromSource(instance):null;
        if(!instance || !source)return;
        rig.RestoreProceduralPoseForEditing();
        rig.leftArmMask.Release();rig.rightArmMask.Release();rig.surfaceEffects.Release();
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Full Reset to Prefab");
        Undo.RegisterFullObjectHierarchyUndo(instance,"Full Reset to Prefab");
        // Clear procedural caches before Unity restores the serialized defaults.
        rig.enabled=false;
        PrefabUtility.RevertPrefabInstance(instance,InteractionMode.UserAction);
        // Root transform values are Unity default overrides and are excluded
        // from a normal Revert All. Full Reset explicitly restores those too.
        Undo.RecordObject(instance.transform,"Full Reset to Prefab");
        instance.transform.SetLocalPositionAndRotation(source.transform.localPosition,source.transform.localRotation);
        instance.transform.localScale=source.transform.localScale;
        instance.name=source.name;
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(group);
        EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
    }

    internal static void ResetFaceToPrefab(ReaniIK rig)
    {
        if (!rig) return;
        rig.RestoreProceduralPoseForEditing();
        var bones = new System.Collections.Generic.List<Transform>();
        var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            { "head", "neck", "eyes", "eyelids", "mouth", "nose", "upper_lip", "lower_lip", "lip_corners" };
        foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
            if (!bone.GetComponent<SpriteRenderer>() && (names.Contains(bone.name) || bone == rig.breathing.mouth || bone == rig.breathing.nose)) bones.Add(bone);
        var objects = new System.Collections.Generic.List<Object> { rig };
        objects.AddRange(bones);
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Reset Face to Prefab");
        Undo.RegisterCompleteObjectUndo(objects.ToArray(), "Reset Face to Prefab");
        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(rig);
        var sourcePose = prefab ? prefab.facePose : new ReaniIKFacePose();
        var pose = rig.facePose;
        pose.headTurn = sourcePose.headTurn;
        pose.neckTurn = sourcePose.neckTurn;
        pose.gaze = sourcePose.gaze;
        pose.upperLipHeight = sourcePose.upperLipHeight;
        pose.upperLipSize = sourcePose.upperLipSize;
        pose.lowerLipHeight = sourcePose.lowerLipHeight;
        pose.lowerLipSize = sourcePose.lowerLipSize;
        pose.lipCornersHeight = sourcePose.lipCornersHeight;
        pose.eyelidClosure = sourcePose.eyelidClosure;
        foreach (var bone in bones)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(bone);
            if (!source) continue;
            bone.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            bone.localScale = source.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
        }
        rig.RefreshFacePoseForEditing();
        PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    internal static void ResetBodySizeToPrefab(ReaniIK rig)
    {
        if (!rig) return;
        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(rig);
        rig.RestoreProceduralPoseForEditing();
        var chain = new System.Collections.Generic.List<Transform>();
        foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
            if (!bone.GetComponent<SpriteRenderer>() &&
                (bone.name == "belly" || bone.name == "spine" || bone.name == "neck" || bone.name == "head")) chain.Add(bone);
        var arms = new System.Collections.Generic.HashSet<Transform>();
        foreach (var limb in new[] { rig.leftArm, rig.rightArm, rig.leftArm2, rig.rightArm2 })
            if (limb.upper) arms.Add(limb.upper);
        var objects = new System.Collections.Generic.List<Object> { rig };
        objects.AddRange(chain);
        foreach (var arm in arms) if (!chain.Contains(arm)) objects.Add(arm);
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Reset Body Size to Prefab");
        Undo.RegisterCompleteObjectUndo(objects.ToArray(), "Reset Body Size to Prefab");
        rig.bodyScale.size = prefab ? prefab.bodyScale.size : 1f;
        foreach (var bone in chain)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(bone);
            if (!source) continue;
            bone.localPosition = source.localPosition;
            bone.localScale = source.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
        }
        foreach (var arm in arms)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(arm);
            if (!source) continue;
            arm.localScale = source.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(arm);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    private static GUIStyle circleHeaderStyle;

    private static void DrawCircleHeader(Transform rig, Vector3 center, float radius, string label, params Transform[] bones)
    {
        var point = HandleUtility.WorldToGUIPoint(center);
        float top = point.y;
        foreach (var offset in new[] { rig.up, -rig.up, rig.right, -rig.right })
            top = Mathf.Min(top, HandleUtility.WorldToGUIPoint(center + offset * radius).y);
        circleHeaderStyle ??= new GUIStyle(EditorStyles.label);
        circleHeaderStyle.normal.textColor = BlackSceneControls ? Color.black : Color.white;
        sliderResetStyle ??= new GUIStyle(EditorStyles.miniButton)
            { padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter, fontSize = 9 };
        float width = circleHeaderStyle.CalcSize(new GUIContent(label)).x + 20f;
        var rect = new Rect(point.x - width * .5f, top - 24f, width, 18f);
        bool available = bones.Length > 0;
        foreach (var bone in bones) available &= bone && PrefabUtility.GetCorrespondingObjectFromSource(bone);
        Handles.BeginGUI();
        bool clicked;
        using (new EditorGUI.DisabledScope(!available))
            clicked = GUI.Button(new Rect(rect.x, rect.y + 2f, 14f, 14f),
                new GUIContent("R", "Reset " + label + " position and rotation to prefab"), sliderResetStyle);
        GUI.Label(new Rect(rect.x + 20f, rect.y, width - 20f, rect.height), label, circleHeaderStyle);
        Handles.EndGUI();
        if (clicked) ResetControlPoseToPrefab(bones);
    }

    private static bool DrawCircleResetButton(Vector3 labelPosition, string label, params Transform[] bones)
    {
        var point = HandleUtility.WorldToGUIPoint(labelPosition);
        sliderResetStyle ??= new GUIStyle(EditorStyles.miniButton)
            { padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter, fontSize = 9 };
        bool available = bones.Length > 0;
        foreach (var bone in bones)
            available &= bone && PrefabUtility.GetCorrespondingObjectFromSource(bone);
        Handles.BeginGUI();
        bool clicked;
        using (new EditorGUI.DisabledScope(!available))
            clicked = GUI.Button(new Rect(point.x, point.y - 27f, 14f, 14f),
                new GUIContent("R", "Reset " + label + " position and rotation to prefab"), sliderResetStyle);
        Handles.EndGUI();
        if (!clicked) return false;
        ResetControlPoseToPrefab(bones);
        return true;
    }

    internal static void ResetControlPoseToPrefab(params Transform[] bones)
    {
        Undo.RecordObjects(bones, "Reset control pose to prefab");
        foreach (var bone in bones)
        {
            if (!bone) continue;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(bone);
            if (!source) continue;
            bone.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
        }
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
}
#endif
