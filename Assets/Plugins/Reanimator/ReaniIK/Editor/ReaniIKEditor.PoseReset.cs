#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKEditor
{
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
