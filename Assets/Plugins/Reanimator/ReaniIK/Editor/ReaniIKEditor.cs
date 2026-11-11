#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ReaniIK))]
public sealed class ReaniIKEditor : Editor
{
    private Transform hips, spine, neck, eyes, eyelids;
    private float hipsHandleOffset;
    private int activePoseControl;
    private Transform activePoseBone;
    private Vector3 activePoseOffset;

    private void OnEnable()
    {
        var rig = (ReaniIK)target;
        foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
        {
            switch (bone.name.ToLowerInvariant())
            {
                case "hips": hips ??= bone; break;
                case "spine": spine ??= bone; break;
                case "neck": neck ??= bone; break;
                case "eyes": eyes ??= bone; break;
                case "eyelids": eyelids ??= bone; break;
            }
        }

        Vector3 right = rig.transform.right;
        Vector3 origin = hips ? hips.position : rig.transform.position;
        Vector3 absoluteRight = new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z));
        foreach (var sprite in rig.GetComponentsInChildren<SpriteRenderer>())
        {
            Bounds bounds = sprite.bounds;
            float edge = Vector3.Dot(bounds.center - origin, right) +
                Vector3.Dot(bounds.extents, absoluteRight);
            hipsHandleOffset = Mathf.Max(hipsHandleOffset, edge);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "leftArm", "rightArm", "leftLeg", "rightLeg", "leftArm2", "rightArm2");
        DrawLimb(serializedObject.FindProperty("leftArm"));
        DrawLimb(serializedObject.FindProperty("rightArm"));
        DrawLimb(serializedObject.FindProperty("leftLeg"));
        DrawLimb(serializedObject.FindProperty("rightLeg"));
        DrawLimb(serializedObject.FindProperty("leftArm2"));
        DrawLimb(serializedObject.FindProperty("rightArm2"));
        serializedObject.ApplyModifiedProperties();
        var rig = (ReaniIK)target;
        if (GUILayout.Button("Reset spring motion"))
            rig.ResetSpringMotion();
        if (GUILayout.Button("Capture default limb poses"))
        {
            Undo.RecordObject(rig, "Capture default limb poses");
            rig.CaptureDefaultLimbPoses();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        }
    }

    private static void DrawLimb(SerializedProperty limb)
    {
        limb.isExpanded = EditorGUILayout.Foldout(limb.isExpanded, limb.displayName, true);
        if (!limb.isExpanded) return;

        var mode = limb.FindPropertyRelative("flipMode");
        var field = limb.Copy();
        var end = field.GetEndProperty();
        using var indent = new EditorGUI.IndentLevelScope();
        if (!field.NextVisible(true)) return;
        while (!SerializedProperty.EqualContents(field, end) && field.depth > limb.depth)
        {
            bool dino = mode.enumValueIndex == (int)ReaniIK.Limb.FlipMode.Dino;
            bool visible = field.name switch
            {
                "flip" => !dino,
                "invertDinoFlip" or "dinoFlipSpeed" => dino,
                _ => true
            };
            if (visible) EditorGUILayout.PropertyField(field, true);
            if (!field.NextVisible(false)) break;
        }
    }

    private void OnSceneGUI()
    {
        var rig = (ReaniIK)target;
        Vector3 origin = hips ? hips.position : rig.transform.position;
        float size = HandleUtility.GetHandleSize(origin);
        Vector3 column = origin + rig.transform.right * (hipsHandleOffset + size);
        Vector3 row = rig.transform.up * (size * 2f);
        if (rig.showHipsControl) DrawBoneControl(rig.transform, hips, column, "Hips", size);
        if (rig.showSpineControl) DrawBoneControl(rig.transform, spine, column + row, "Spine", size, allowMove: false);
        if (rig.showNeckControl) DrawBoneControl(rig.transform, neck, column + row * 2f, "Neck", size, allowMove: false);
        if (rig.showEyesControl) DrawBoneControl(rig.transform, eyes, column + row * 3f, "Eyes", size, allowRotate: false);
        if (rig.showEyelidsControl) DrawBoneControl(rig.transform, eyelids, column + row * 4f, "Eyelids", size, allowRotate: false);
        DrawTarget(rig.leftArm, "Left hand", new Color(0.25f, 0.85f, 1f), isHand: true);
        DrawTarget(rig.rightArm, "Right hand", new Color(1f, 0.65f, 0.2f), isHand: true);
        DrawTarget(rig.leftLeg, "Left foot", new Color(0.25f, 0.85f, 1f));
        DrawTarget(rig.leftArm2, "Left hand 2", new Color(0.25f, 0.85f, 1f), isHand: true);
        DrawTarget(rig.rightArm2, "Right hand 2", new Color(1f, 0.65f, 0.2f), isHand: true);
        DrawTarget(rig.rightLeg, "Right foot", new Color(1f, 0.65f, 0.2f));
    }

    private void DrawBoneControl(Transform rig, Transform bone, Vector3 position, string label, float size,
        bool allowMove = true, bool allowRotate = true)
    {
        if (!bone) return;
        using var scope = new Handles.DrawingScope(new Color(0.65f, 1f, 0.45f));
        Handles.DrawDottedLine(bone.position, position, 4f);
        Handles.Label(position + rig.right * size * 0.85f, label);
        DrawPoseHandle(bone, position - bone.position, rig.forward, rig.up, true, allowMove, allowRotate);
    }

    private void DrawTarget(ReaniIK.Limb limb, string label, Color color, bool isHand = false)
    {
        if (!limb.enabled || !limb.IsValid) return;
        using var scope = new Handles.DrawingScope(color);
        Handles.DrawLine(limb.upper.position, limb.lower.position);
        Handles.DrawLine(limb.lower.position, limb.tip.position);
        Handles.DrawDottedLine(limb.tip.position, limb.EffectiveTarget, 4f);
        Handles.DrawDottedLine(limb.EffectiveTarget, limb.target.position, 4f);
        Handles.Label(limb.target.position + Vector3.up * 0.12f, label);
        DrawPoseHandle(limb.target, Vector3.zero, limb.upper.forward, limb.upper.up, isHand);
    }

    private void DrawPoseHandle(Transform controlled, Vector3 offset, Vector3 normal, Vector3 up, bool large,
        bool allowMove = true, bool allowRotate = true)
    {
        int hotBefore = GUIUtility.hotControl;
        // Slider2D reports displacement from mouse-down, so keep the same offset for the whole drag.
        if (hotBefore != 0 && hotBefore == activePoseControl && controlled == activePoseBone)
            offset = activePoseOffset;
        EditorGUI.BeginChangeCheck();
        Vector3 startPosition = controlled.position + offset;
        Quaternion startRotation = controlled.rotation;
        Vector3 position = startPosition;
        Quaternion rotation = startRotation;
        float size = HandleUtility.GetHandleSize(position);
        bool rotateOnly = allowRotate && Tools.current == Tool.Rotate;
        if (allowRotate && (large || rotateOnly || !allowMove))
        {
            float radius = size * (large ? 0.76f : 0.38f);
            Handles.DrawWireDisc(position, normal, radius, 4f);
            rotation = Handles.Disc(rotation, position, normal, radius, false, 0f);
        }
        if (allowMove && !rotateOnly)
        {
            Quaternion plane = Quaternion.LookRotation(normal, up);
            position = Handles.Slider2D(position, normal, plane * Vector3.right,
                plane * Vector3.up, size * (large ? 0.48f : 0.24f), Handles.RectangleHandleCap, Vector2.zero);
        }
        if (hotBefore == 0 && GUIUtility.hotControl != 0)
        {
            activePoseControl = GUIUtility.hotControl;
            activePoseBone = controlled;
            activePoseOffset = offset;
        }
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controlled, "Edit " + controlled.name + " pose");
            if (position != startPosition) controlled.position = position - offset;
            if (rotation != startRotation) controlled.rotation = rotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(controlled);
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
#endif
