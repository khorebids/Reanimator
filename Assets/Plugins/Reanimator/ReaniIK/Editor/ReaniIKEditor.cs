#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(ReaniIK))]
public sealed partial class ReaniIKEditor : Editor
{
    private const float SceneControlScale = .7f;
    private const float SceneSliderSpacing = .336f;
    private const float FaceColumnSpacing = 2.8f;
    private const float BodyColumnSpacing = 1.3f;
    private const float BodyRowSpacing = 1.4f;
    private const string BlackControlsKey = "Reanimator.ReaniIK.BlackSceneControls";
    private static bool? blackControls;
    private static GUIStyle blackControlLabel;
    private static GUIStyle sliderResetStyle;
    private static bool BlackSceneControls
    {
        get => blackControls ??= EditorPrefs.GetBool(BlackControlsKey, false);
        set { blackControls = value; EditorPrefs.SetBool(BlackControlsKey, value); }
    }
    private static Color SceneControlColor(Color normal) => BlackSceneControls ? Color.black : normal;
    private static void ControlLabel(Vector3 position, string text)
    {
        if (!BlackSceneControls) { Handles.Label(position, text); return; }
        if (blackControlLabel == null)
        {
            blackControlLabel = new GUIStyle(EditorStyles.label);
            foreach (var state in new[] { blackControlLabel.normal, blackControlLabel.hover, blackControlLabel.active, blackControlLabel.focused }) state.textColor = Color.black;
        }
        Handles.Label(position, text, blackControlLabel);
    }
    private Transform hips, spine, neck, belly, shoulders, rootBone;
    private float hipsHandleOffset;
    private float faceHandleOffset;
    private int activePoseControl;
    private Transform activePoseBone;
    private Vector3 activePoseOffset;
    private ReorderableList vModeList;
    private static readonly string[] FaceBreathingTabs = { "Mouth", "Nose" };
    private int faceBreathingTab;
    [SerializeField] private bool sceneControlsExpanded;
    [SerializeField] private bool bodySizeExpanded,leftArmMaskExpanded,rightArmMaskExpanded;

    private void OnEnable()
    {
        InitializeButtControls();
        vModeList = new ReorderableList(serializedObject, serializedObject.FindProperty("v_mode"), true, true, true, true);
        vModeList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "v_mode");
        };
        vModeList.drawElementCallback = (rect, index, active, focused) =>
        {
            var entry = vModeList.serializedProperty.GetArrayElementAtIndex(index);
            rect.y += 1f;
            rect.height = EditorGUIUtility.singleLineHeight;
            const float invertWidth = 54f;
            var boneWidth = rect.width * 0.33f;
            var valueWidth = (rect.width - boneWidth - invertWidth - 12f) * 0.5f;
            EditorGUI.PropertyField(new Rect(rect.x, rect.y, boneWidth, rect.height),
                entry.FindPropertyRelative("bone"), GUIContent.none);
            var valueRect = new Rect(rect.x + boneWidth + 4f, rect.y, valueWidth, rect.height);
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 52f;
            EditorGUI.PropertyField(valueRect, entry.FindPropertyRelative("direction"),
                new GUIContent("Angle", "Rotation in degrees in the grandparent's XY plane."));
            valueRect.x += valueWidth + 4f;
            EditorGUI.PropertyField(valueRect, entry.FindPropertyRelative("strength"),
                new GUIContent("Strength", "Multiplier for the parent's displacement."));
            var invertRect = new Rect(valueRect.xMax + 4f, rect.y, invertWidth, rect.height);
            EditorGUIUtility.labelWidth = 36f;
            EditorGUI.PropertyField(invertRect, entry.FindPropertyRelative("invert"),
                new GUIContent("Invert", "Reverse the resulting displacement."));
            EditorGUIUtility.labelWidth = previousLabelWidth;
        };

        var rig = (ReaniIK)target;
        LoadBodyControlsLayout(rig);
        foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
        {
            if(bone.GetComponent<SpriteRenderer>())continue;
            switch (bone.name.ToLowerInvariant())
            {
                case "hips": hips ??= bone; break;
                case "spine": spine ??= bone; break;
                case "neck": neck ??= bone; break;
                case "belly": belly ??= bone; break;
                case "shoulders": shoulders ??= bone; break;
            }
        }

        rootBone = hips;
        while (rootBone && rootBone != rig.transform &&
               !string.Equals(rootBone.name, "root", System.StringComparison.OrdinalIgnoreCase))
            rootBone = rootBone.parent;
        if (rootBone == rig.transform)
            rootBone = null;

        Vector3 right = rig.transform.right;
        Vector3 origin = hips ? hips.position : rig.transform.position;
        Vector3 absoluteRight = new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z));
        foreach (var sprite in rig.GetComponentsInChildren<SpriteRenderer>())
        {
            Bounds bounds = sprite.bounds;
            float edge = Vector3.Dot(bounds.center - origin, right) +
                Vector3.Dot(bounds.extents, absoluteRight);
            hipsHandleOffset = Mathf.Max(hipsHandleOffset, edge);
            faceHandleOffset = Mathf.Max(faceHandleOffset,
                Vector3.Dot(bounds.center - rig.transform.position, right) +
                Vector3.Dot(bounds.extents, absoluteRight));
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        bool black = EditorGUILayout.ToggleLeft(new GUIContent("Black scene controls (light background)", "Draw IK handles, lines and labels in black. This is an editor display preference."), BlackSceneControls);
        if (EditorGUI.EndChangeCheck()) { BlackSceneControls = black; SceneView.RepaintAll(); }
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("breathingFrequency"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("breathingMultiplier"));
        DrawBreathing(serializedObject.FindProperty("breathing"));
        if(EditorGUI.EndChangeCheck())
        {
            RecordSceneControl(target,"Adjust breathing",false);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
        bodySizeExpanded=EditorGUILayout.Foldout(bodySizeExpanded,"Body Size",true);
        if(bodySizeExpanded)
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var scaleSettings = serializedObject.FindProperty("bodyScale");
            EditorGUILayout.PropertyField(scaleSettings.FindPropertyRelative("size"));
            EditorGUILayout.PropertyField(scaleSettings.FindPropertyRelative("leftArmSize"));
            EditorGUILayout.PropertyField(scaleSettings.FindPropertyRelative("rightArmSize"));
            EditorGUILayout.PropertyField(scaleSettings.FindPropertyRelative("distribution"), new GUIContent("Distribution"));
            EditorGUILayout.PropertyField(scaleSettings.FindPropertyRelative("back_arms"), new GUIContent("back_arms"));
            EditorGUILayout.PropertyField(scaleSettings.FindPropertyRelative("back_height"), new GUIContent("back_height"));
            EditorGUILayout.LabelField("belly → spine → neck → head", EditorStyles.miniLabel);
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("facePose"),
            new GUIContent("Face Control Multipliers"), true);
        DrawPropertiesExcluding(serializedObject, "bodyScale", "breathingFrequency", "breathingMultiplier", "breathing", "facePose",
            "leftArm", "leftArmMask", "rightArm", "rightArmMask", "leftLeg", "rightLeg", "leftArm2", "rightArm2", "v_mode", "surfaceEffects", "softContour", "springRotation",
            "showHipsControl", "showRootControl", "showSpineControl", "showBellyControl", "showShouldersControl", "showNeckControl", "showFaceControl", "showButtControl");
        sceneControlsExpanded=EditorGUILayout.Foldout(sceneControlsExpanded,"Scene Controls",true);
        if(sceneControlsExpanded)
        {
            using(new EditorGUI.IndentLevelScope())
                foreach(var name in new[]{"showHipsControl","showRootControl","showSpineControl","showBellyControl","showShouldersControl","showNeckControl","showFaceControl","showButtControl"})
                {
                    var property=serializedObject.FindProperty(name);
                    property.boolValue=EditorGUILayout.Toggle(new GUIContent(property.displayName,property.tooltip),property.boolValue);
                }
        }
        DrawSideAssSettings((ReaniIK)target);
        DrawSurfaceEffects(serializedObject.FindProperty("surfaceEffects"));
        DrawLimb(serializedObject.FindProperty("leftArm"));
        DrawArmMask("leftArmMask", "Left Arm Mask", ((ReaniIK)target).leftArmMask);
        DrawLimb(serializedObject.FindProperty("rightArm"));
        DrawArmMask("rightArmMask", "Right Arm Mask", ((ReaniIK)target).rightArmMask);
        DrawLimb(serializedObject.FindProperty("leftLeg"));
        DrawLimb(serializedObject.FindProperty("rightLeg"));
        DrawLimb(serializedObject.FindProperty("leftArm2"));
        DrawLimb(serializedObject.FindProperty("rightArm2"));
        vModeList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
        ((ReaniIK)target).SynchronizeAnimationControls();
        // These simulation settings remain editable with Undo, but never enter
        // AnimationWindow's property-diff recording pipeline.
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("softContour"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("springRotation"), true);
        if(EditorGUI.EndChangeCheck())
        {
            RecordSceneControl(target,"Adjust breathing / SC / SCR",false);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
        var rig = (ReaniIK)target;
        if (GUILayout.Button("Paint redness / surface normals")) ReaniIKSurfacePainter.Open(rig);
        if (GUILayout.Button("Reset spring motion"))
            rig.ResetSpringMotion();
        if (GUILayout.Button("Capture default limb poses"))
        {
            RecordSceneControl(rig, "Capture default limb poses", false);
            rig.CaptureDefaultLimbPoses();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        }
    }

    private static void DrawSideAssSettings(ReaniIK rig)
    {
        foreach (var booty in rig.GetComponentsInChildren<SideAssRig2D>(true))
        {
            EditorGUILayout.LabelField("Booty / " + (booty.upperLeg ? booty.upperLeg.name : booty.name), EditorStyles.boldLabel);
            var settings = new SerializedObject(booty); settings.Update();
            using (new EditorGUI.IndentLevelScope())
                foreach (var name in new[] { "bendProtrusion", "protrusion", "compression", "frequency", "damping", "inertia" })
                    EditorGUILayout.PropertyField(settings.FindProperty(name));
            if (settings.ApplyModifiedProperties()) { EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); }
        }
    }
    private static void DrawSurfaceEffects(SerializedProperty effects)
    {
        effects.isExpanded = EditorGUILayout.Foldout(effects.isExpanded, "Surface Effects", true);
        if (!effects.isExpanded) return;
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(effects.FindPropertyRelative("maps"));
            EditorGUILayout.PropertyField(effects.FindPropertyRelative("tremorMode"), new GUIContent("Tremor Mode"));
            EditorGUILayout.Slider(effects.FindPropertyRelative("tremor"), 0, 1, new GUIContent("Tremor Quantity"));
            EditorGUILayout.IntSlider(effects.FindPropertyRelative("tremorGroups"), 1, ReaniIKSurfaceEffects.MaximumTremorGroups, new GUIContent("Maximum Tremor Groups"));
            EditorGUILayout.PropertyField(effects.FindPropertyRelative("tremorSpeed"), new GUIContent("Tremor Frequency (Hz)"));
            EditorGUILayout.PropertyField(effects.FindPropertyRelative("outlineWidth"), new GUIContent("Tremor Peak Height", "Base peak height relative to character height."));
            EditorGUILayout.PropertyField(effects.FindPropertyRelative("tremorWidth"), new GUIContent("Tremor Peak Width", "Peak base width multiplier. 1 preserves the original width; height is unchanged."));
            EditorGUILayout.PropertyField(effects.FindPropertyRelative("tremorColorDepth"), new GUIContent("Tremor Color Depth (px)", "Sample color this many texture pixels inward from the opaque contour."));
            foreach (string name in new[] { "redness", "rednessColor", "sweat", "sweatSizeMin", "sweatSizeMax", "sweatFadeIn", "sweatSpeed", "sweatSlowdown", "trailMaxLength", "trailLifetime", "darkness", "contrast", "desaturation", "showControls" })
            {
                var property=effects.FindPropertyRelative(name);
                if(name is "sweatSizeMin" or "sweatSizeMax")
                {
                    EditorGUI.BeginChangeCheck();
                    float percent=EditorGUILayout.FloatField(new GUIContent(property.displayName,"Droplet radius as a percentage of sprite height. 1 = 1%."),property.floatValue*100f);
                    if(EditorGUI.EndChangeCheck())property.floatValue=Mathf.Max(.01f,percent)/100f;
                }
                else EditorGUILayout.PropertyField(property);
            }
        }
    }

    private void DrawBreathing(SerializedProperty breathing)
    {
        breathing.isExpanded = EditorGUILayout.Foldout(breathing.isExpanded, breathing.displayName, true);
        if (!breathing.isExpanded)
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            Draw("enabled");
            Draw("period");
            Draw("height");
            Draw("spineExpansion");
            Draw("spineRotationDegrees");
            Draw("belly");
            Draw("spine");
            Draw("neck");
            Draw("leftArm");
            Draw("rightArm");
            Draw("faceEnabled");
            faceBreathingTab = GUILayout.Toolbar(faceBreathingTab, FaceBreathingTabs);
            if (faceBreathingTab == 0)
            {
                Draw("mouth");
                Draw("mouthCompression");
                Draw("mouthLift");
                Draw("mouthRotationDegrees");
            }
            else
            {
                Draw("nose");
                Draw("noseExpansion");
                Draw("noseLift");
                Draw("noseRotationDegrees");
            }
        }

        void Draw(string field) => EditorGUILayout.PropertyField(breathing.FindPropertyRelative(field), true);
    }

    private void DrawArmMask(string property, string label, ReaniIKArmMask settings)
    {
        var mask = serializedObject.FindProperty(property);
        bool left=property=="leftArmMask";
        bool expanded=EditorGUILayout.Foldout(left?leftArmMaskExpanded:rightArmMaskExpanded,label,true);
        if(left)leftArmMaskExpanded=expanded;else rightArmMaskExpanded=expanded;
        if(!expanded)return;
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.Slider(mask.FindPropertyRelative("amount"), 0f, 1f,
                new GUIContent("Hidden Amount", "0 = fully visible, 1 = fully hidden. Fingertips to shoulder by default."));
            EditorGUILayout.PropertyField(mask.FindPropertyRelative("flip"), new GUIContent("Flip Direction"));
            EditorGUILayout.PropertyField(mask.FindPropertyRelative("softness"), new GUIContent("Edge Softness"));
            if (!string.IsNullOrEmpty(settings.Status))
                EditorGUILayout.HelpBox(settings.Status, MessageType.Warning);
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
        try
        {
        Vector3 origin = hips ? hips.position : rig.transform.position;
        float size = HandleUtility.GetHandleSize(origin);
        Vector3 column = origin + rig.transform.right * (hipsHandleOffset + size);
        Vector3 row = rig.transform.up * (size * BodyRowSpacing);
        var hipsControl = column;
        if(!bodyControlsHidden)
        foreach (var booty in rig.GetComponentsInChildren<SideAssRig2D>(true))
        {
            using var bootyColor = new Handles.DrawingScope(SceneControlColor(new Color(.65f,1,.45f)));
            var center = booty.transform.position + rig.transform.right * size * 2;
            EditorGUI.BeginChangeCheck();
            string side = booty.upperLeg ? booty.upperLeg.name : "Side Ass";
            booty.protrusion = DrawFaceSlider(rig, center, size, "Booty protrusion / " + side, booty.protrusion, -1, true, recordOwner:booty);
            booty.compression = DrawFaceSlider(rig, center - rig.transform.up * size * .4f, size, "Booty contact / " + side, booty.compression, 0, true, recordOwner:booty);
            if (EditorGUI.EndChangeCheck()) PrefabUtility.RecordPrefabInstancePropertyModifications(booty);
        }
        var spineControl = column + row;
        var neckControl = column + row * 2f;
        var bodyControlSize = size;
        Vector3? faceControlsCenter=null;
        if (rig.showFaceControl && rig.facePose.Head)
        {
            var root = rig.transform;
            var faceSize = HandleUtility.GetHandleSize(root.position);
            var restHead = root.TransformPoint(rig.facePose.HeadRestRootPosition);
            var faceCenter = root.position + root.up * Vector3.Dot(restHead - root.position, root.up)
                + root.right * (faceHandleOffset + faceSize);
            neckControl = faceCenter - root.up * (faceSize * 3.75f);
            spineControl = faceCenter + root.right * (faceSize * FaceColumnSpacing);
            hipsControl = spineControl - root.up * (faceSize * BodyRowSpacing);
            bodyControlSize = faceSize;
            faceControlsCenter=faceCenter;
        }
        var blockOffset=DrawBodyControlsGrip(rig,hipsControl,bodyControlSize);
        if(!bodyControlsHidden)
        {
        if(faceControlsCenter.HasValue)DrawFaceControls(rig,faceControlsCenter.Value+blockOffset,bodyControlSize);
        else DrawSurfaceSceneControls(rig,column+blockOffset-rig.transform.up*(size*2.5f),size);
        hipsControl+=blockOffset;spineControl+=blockOffset;
        if (rig.showHipsControl) DrawBoneControl(rig.transform, hips, hipsControl, "Hips", bodyControlSize);
        if (rig.showSpineControl) DrawBoneControl(rig.transform, spine, spineControl, "Spine", bodyControlSize, allowMove: false);
        var extraColumn = rig.transform.right * (bodyControlSize * BodyColumnSpacing);
        if (rig.showBellyControl)
        {
            var bellyControl = hipsControl + extraColumn;
            DrawBoneControl(rig.transform, belly, bellyControl, "Belly", bodyControlSize);
        }
        if (rig.showShouldersControl) DrawBoneControl(rig.transform, shoulders, spineControl + extraColumn, "Shoulders", bodyControlSize);
        if (rig.showRootControl)
        {
            var rootControl=hipsControl-rig.transform.up*(bodyControlSize*BodyRowSpacing);
            DrawBoneControl(rig.transform, rootBone,
                rootControl,
                "Root", bodyControlSize);
        }
        // Neck rotation shares the head control as an outer lower semicircle.
        DrawButtControls(rig,hipsControl+extraColumn-rig.transform.up*(bodyControlSize*BodyRowSpacing),bodyControlSize);
        if(rig.showBellyControl&&belly)
            DrawBellyScale(rig,hipsControl-rig.transform.up*(bodyControlSize*(BodyRowSpacing+1.9f))-rig.transform.right*(bodyControlSize*SceneControlScale*.76f),bodyControlSize);
        }
        if(!limbControlsHidden)
        {
        DrawTarget(rig.leftArm, "Left hand", new Color(0.25f, 0.85f, 1f), isHand: true);
        DrawArmMaskControl(rig, rig.leftArm, rig.leftArmMask, "Left");
        DrawTarget(rig.rightArm, "Right hand", new Color(1f, 0.65f, 0.2f), isHand: true);
        DrawArmMaskControl(rig, rig.rightArm, rig.rightArmMask, "Right");
        DrawTarget(rig.leftLeg, "Left leg", new Color(0.25f, 0.85f, 1f), isHand: true);
        DrawTarget(rig.leftArm2, "Left hand 2", new Color(0.25f, 0.85f, 1f), isHand: true);
        DrawTarget(rig.rightArm2, "Right hand 2", new Color(1f, 0.65f, 0.2f), isHand: true);
        DrawTarget(rig.rightLeg, "Right leg", new Color(1f, 0.65f, 0.2f), isHand: true);
        }
        }
        finally
        {
            if(rig)rig.SynchronizeAnimationControls();
            Undo.FlushUndoRecordObjects();
        }
    }

    private static void DrawSurfaceSceneControls(ReaniIK rig,Vector3 center,float size)
    {
        if(!rig.surfaceEffects.showControls)return;
        using var scope=new Handles.DrawingScope(SceneControlColor(new Color(.65f,1f,.45f)));
        var effects=rig.surfaceEffects;
        EditorGUI.BeginChangeCheck();
        effects.tremor=DrawFaceSlider(rig,center,size,"Tremor groups "+effects.TargetTremorGroups,effects.tremor,0,true);
        effects.redness=DrawFaceSlider(rig,center-rig.transform.up*size*SceneSliderSpacing,size,"Redness",effects.redness,0,true);
        effects.sweat=DrawFaceSlider(rig,center-rig.transform.up*size*SceneSliderSpacing*2,size,"Sweat "+effects.TargetSweatDrops+"/"+ReaniIKSurfaceEffects.MaximumDrops,effects.sweat,0,true);
        effects.darkness=DrawFaceSlider(rig,center-rig.transform.up*size*SceneSliderSpacing*3,size,"Darkness",effects.darkness,0,true);
        effects.contrast=DrawFaceSlider(rig,center-rig.transform.up*size*SceneSliderSpacing*4,size,"Contrast",effects.contrast,-1,true,3,1);
        effects.desaturation=DrawFaceSlider(rig,center-rig.transform.up*size*SceneSliderSpacing*5,size,"Desaturation",effects.desaturation,0,true);
        if(EditorGUI.EndChangeCheck())PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
    }

    private void DrawFaceControls(ReaniIK rig, Vector3 center, float size)
    {
        var pose = rig.facePose;
        var root = rig.transform;
        var radius = size * 0.6f * SceneControlScale;
        using var scope = new Handles.DrawingScope(SceneControlColor(new Color(0.65f, 1f, 0.45f)));
        Handles.DrawWireDisc(center, root.forward, radius, 3f);
        var neckRadius=radius*1.4f;
        if(pose.Neck)
        {
            Handles.DrawWireArc(center,root.forward,-root.right,180f,neckRadius,2f);
            float neckAngle=Mathf.Clamp(pose.neckTurn,-1f,1f)*90f;
            var thumb=center+(root.right*Mathf.Sin(neckAngle*Mathf.Deg2Rad)-root.up*Mathf.Cos(neckAngle*Mathf.Deg2Rad))*neckRadius;
            EditorGUI.BeginChangeCheck();
            var moved=Handles.Slider2D(thumb,root.forward,root.right,root.up,size*.065f*SceneControlScale,Handles.DotHandleCap,Vector2.zero);
            if(EditorGUI.EndChangeCheck())
            {
                RecordFaceControl(rig,"Turn Neck");
                var offset=moved-center;
                pose.neckTurn=Mathf.Clamp(Mathf.Atan2(Vector3.Dot(offset,root.right),-Vector3.Dot(offset,root.up))*Mathf.Rad2Deg/90f,-1f,1f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
            }
            var neckLabel=center+root.right*(neckRadius+size*.15f)-root.up*(size*.2f);
            ControlLabel(neckLabel,"Neck");
            if(DrawCircleResetButton(neckLabel,"Neck",true,pose.Neck))
            {RecordSceneControl(rig,"Reset Neck control",false);var source=PrefabUtility.GetCorrespondingObjectFromSource(rig);pose.neckTurn=source?source.facePose.neckTurn:0f;}
        }
        var headLabel = center + root.right * (radius + size * .15f);
        ControlLabel(headLabel, "Head");
        var prefabRig = PrefabUtility.GetCorrespondingObjectFromSource(rig);
        if (DrawCircleResetButton(headLabel, "Head", true, pose.Head))
        {
            RecordSceneControl(rig, "Reset Head control", false);
            pose.headTurn = prefabRig ? prefabRig.facePose.headTurn : 0f;
            pose.gaze = prefabRig ? prefabRig.facePose.gaze : Vector2.zero;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        }

        EditorGUI.BeginChangeCheck();
        pose.eyelidClosure = DrawFaceSlider(rig, center + root.up * (radius + size * 0.3f), size,
            "Eyelid Closure", pose.eyelidClosure, -1f, pose.HasEyelids);
        var angle = pose.headTurn * pose.headTurnDegrees;
        var startRotation = root.rotation * Quaternion.AngleAxis(angle, Vector3.forward);
        EditorGUI.BeginChangeCheck();
        var rotation = Handles.Disc(startRotation, center, root.forward, radius, false, 0f);
        if (EditorGUI.EndChangeCheck() && pose.headTurnDegrees > 0f)
        {
            var delta = Mathf.DeltaAngle(0f, (Quaternion.Inverse(startRotation) * rotation).eulerAngles.z);
            RecordFaceControl(rig, "Turn Head");
            pose.headTurn = Mathf.Clamp((angle + delta) / pose.headTurnDegrees, -1f, 1f);
        }

        if (pose.HasEyes)
        {
            var travel = radius * 0.65f;
            var thumb = center + (root.right * pose.gaze.x + root.up * pose.gaze.y) * travel;
            EditorGUI.BeginChangeCheck();
            thumb = Handles.Slider2D(thumb, root.forward, root.right, root.up,
                size * 0.10f * SceneControlScale, Handles.RectangleHandleCap, Vector2.zero);
            if (EditorGUI.EndChangeCheck())
            {
                var offset = (thumb - center) / travel;
                RecordFaceControl(rig, "Aim Eyes");
                pose.gaze = Vector2.ClampMagnitude(
                    new Vector2(Vector3.Dot(offset, root.right), Vector3.Dot(offset, root.up)), 1f);
            }
        }

        var firstRow = center - root.up * ((pose.Neck?neckRadius:radius) + size * 0.35f);
        pose.upperLipHeight = DrawFaceSlider(rig, firstRow, size, "Upper Lip Height", pose.upperLipHeight, -1f, pose.HasUpperLip);
        pose.upperLipSize = DrawFaceSlider(rig, firstRow - root.up * (size * SceneSliderSpacing), size,
            "Upper Lip X", pose.upperLipSize, -1f, pose.HasUpperLip);
        pose.lowerLipHeight = DrawFaceSlider(rig, firstRow - root.up * (size * SceneSliderSpacing * 2), size,
            "Lower Lip Height", pose.lowerLipHeight, -1f, pose.HasLowerLip);
        pose.lowerLipSize = DrawFaceSlider(rig, firstRow - root.up * (size * SceneSliderSpacing * 3), size,
            "Lower Lip X", pose.lowerLipSize, -1f, pose.HasLowerLip);
        pose.lipCornersHeight = DrawFaceSlider(rig, firstRow - root.up * (size * SceneSliderSpacing * 4), size,
            "Lip Corners Height", pose.lipCornersHeight, -1f, pose.HasLipCorners);
        var changed = EditorGUI.EndChangeCheck();
        DrawSurfaceSceneControls(rig,firstRow-root.up*(size*SceneSliderSpacing*5),size);
        var resetPosition = firstRow - root.up * (size * (SceneSliderSpacing*(rig.surfaceEffects.showControls?11:5)+.048f));
        if (Handles.Button(resetPosition, root.rotation, size * 0.10f, size * 0.14f, Handles.RectangleHandleCap))
        {
            ResetFaceToPrefab(rig);
            changed = true;
        }
        ControlLabel(resetPosition + root.right * (size * 0.16f), "Reset Face");
        if(DrawFullResetButton(rig,resetPosition))return;
        if (changed)
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
    }

    private static float DrawFaceSlider(ReaniIK rig, Vector3 center, float size, string label,
        float value, float minimum, bool hasBone, float maximum=1f, float defaultValue=0f, Object recordOwner=null)
    {
        var right = rig.transform.right;
        var distance = size * 1.2f;
        var start = center - right * (distance * 0.5f);
        var end = start + right * distance;
        using var scope = new Handles.DrawingScope(SceneControlColor(hasBone ? Handles.color : new Color(0.55f, 0.6f, 0.52f)));
        Handles.DrawLine(start, end, 2f);
        if (minimum < 0f)
        {
            var tick = rig.transform.up * (size * 0.045f);
            Handles.DrawLine(center - tick, center + tick, 2f);
        }
        bool reset=DrawSliderResetButton(end+right*(size*.2f),label);
        ControlLabel(end + right * (size * 0.4f), label);
        if(reset){RecordSceneControl(recordOwner?recordOwner:rig,"Reset "+label,false);return Mathf.Clamp(defaultValue,minimum,maximum);}
        var thumb = Vector3.Lerp(start, end, Mathf.InverseLerp(minimum, maximum, value));
        if (!hasBone)
            return value;
        EditorGUI.BeginChangeCheck();
        thumb = Handles.Slider(thumb, right, size * 0.10f, Handles.RectangleHandleCap, 0f);
        if (!EditorGUI.EndChangeCheck())
            return value;
        var t = Mathf.Clamp01(Vector3.Dot(thumb - start, right) / distance);
        RecordSceneControl(recordOwner?recordOwner:rig, "Adjust " + label);
        return Mathf.Lerp(minimum, maximum, t);
    }

    private static bool DrawSliderResetButton(Vector3 position,string label)
    {
        var point=HandleUtility.WorldToGUIPoint(position);
        sliderResetStyle??=new GUIStyle(EditorStyles.miniButton){padding=new RectOffset(0,0,0,0),alignment=TextAnchor.MiddleCenter,fontSize=9};
        Handles.BeginGUI();
        bool clicked=GUI.Button(new Rect(point.x-7,point.y-7,14,14),new GUIContent("R","Reset "+label),sliderResetStyle);
        Handles.EndGUI();return clicked;
    }

    private static void RecordFaceControl(ReaniIK rig, string operation)
    {
        RecordSceneControl(rig, operation);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    private void DrawBoneControl(Transform rig, Transform bone, Vector3 position, string label, float size,
        bool allowMove = true, bool allowRotate = true)
    {
        if (!bone) return;
        using var scope = new Handles.DrawingScope(SceneControlColor(new Color(0.65f, 1f, 0.45f)));
        DrawCircleHeader(rig, position, HandleUtility.GetHandleSize(position) * SceneControlScale * .76f, label, bone);
        DrawPoseHandle(bone, position - bone.position, rig.forward, rig.up, true, allowMove, allowRotate);
    }

    private void DrawTarget(ReaniIK.Limb limb, string label, Color color, bool isHand = false)
    {
        if (!limb.enabled || !limb.IsValid) return;
        using var scope = new Handles.DrawingScope(SceneControlColor(color));
        if (limb.lower)
        {
            Handles.DrawLine(limb.upper.position, limb.lower.position);
            Handles.DrawLine(limb.lower.position, limb.tip.position);
        }
        else if (limb.upper != limb.tip)
            Handles.DrawLine(limb.upper.position, limb.tip.position);
        var labelPosition = limb.target.position + Vector3.up * 0.12f;
        ControlLabel(labelPosition, $"{label} ({limb.tip.name})");
        DrawCircleResetButton(labelPosition, label, limb.target);
        DrawPoseHandle(limb.target, Vector3.zero, limb.upper.forward, limb.upper.up, isHand);
        if (isHand)
            DrawArmFlip((ReaniIK)target, limb);
    }

    private static void DrawArmFlip(ReaniIK rig, ReaniIK.Limb limb)
    {
        var size = HandleUtility.GetHandleSize(limb.target.position);
        var position = limb.target.position + rig.transform.right * (size * 1.1f);
        var dino = limb.flipMode == ReaniIK.Limb.FlipMode.Dino;
        var flipped = dino ? limb.invertDinoFlip : limb.flip;
        using var scope = new Handles.DrawingScope(SceneControlColor(flipped
            ? new Color(0.65f, 1f, 0.45f) : new Color(0.35f, 0.5f, 0.3f)));
        if (Handles.Button(position, rig.transform.rotation, size * 0.10f, size * 0.14f, Handles.RectangleHandleCap))
        {
            RecordFaceControl(rig, "Flip Limb");
            if (dino)
                limb.invertDinoFlip = !limb.invertDinoFlip;
            else
                limb.flip = !limb.flip;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        }
        ControlLabel(position + rig.transform.right * (size * 0.18f), "Flip");
    }

    private static void DrawArmMaskControl(ReaniIK rig, ReaniIK.Limb limb, ReaniIKArmMask mask, string side)
    {
        if (!limb.IsValid) return;
        float size = HandleUtility.GetHandleSize(limb.target.position);
        var center = limb.target.position - rig.transform.up * (size * 1.1f);
        using var scope = new Handles.DrawingScope(SceneControlColor(new Color(0.25f, 0.85f, 1f)));
        float previous = mask.amount;
        mask.amount = DrawFaceSlider(rig, center, size,
            $"{side} Arm Mask {mask.amount:P0}", previous, 0f, true,1f,1f);
        if (mask.amount != previous)
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        var button = center - rig.transform.up * (size * 0.35f);
        if (Handles.Button(button, rig.transform.rotation, size * 0.08f, size * 0.12f, Handles.RectangleHandleCap))
        {
            RecordFaceControl(rig, "Flip " + side + " Arm Mask");
            mask.flip = !mask.flip;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        }
        ControlLabel(button + rig.transform.right * (size * 0.18f),
            mask.flip ? "Mask: Shoulder → Fingers" : "Mask: Fingers → Shoulder");
    }

    private void DrawBellyScale(ReaniIK rig, Vector3 center, float size)
    {
        DrawSizeSlider(rig,center,size,"Body",rig.bodyScale.size,v=>rig.bodyScale.size=v,()=>ResetBodySizeToPrefab(rig));
        DrawSizeSlider(rig,center+rig.transform.right*(size*.55f),size,"L Arm",rig.bodyScale.leftArmSize,v=>rig.bodyScale.leftArmSize=v,()=>ResetArmSizeToPrefab(rig,true));
        DrawSizeSlider(rig,center+rig.transform.right*(size*1.1f),size,"R Arm",rig.bodyScale.rightArmSize,v=>rig.bodyScale.rightArmSize=v,()=>ResetArmSizeToPrefab(rig,false));
    }

    private void DrawSizeSlider(ReaniIK rig,Vector3 center,float size,string label,float current,System.Action<float> setValue,System.Action reset)
    {
        var up = rig.transform.up;
        var right = rig.transform.right;
        var length = size * 1.4f;
        var start = center - up * (length * 0.5f);
        var end = center + up * (length * 0.5f);
        using var scope = new Handles.DrawingScope(SceneControlColor(new Color(0.65f, 1f, 0.45f)));
        Handles.DrawLine(start, end, 2f);
        var neutral = Vector3.Lerp(start, end, Mathf.InverseLerp(ReaniIKBodyScale.MinimumSize, ReaniIKBodyScale.MaximumSize, 1f));
        Handles.DrawLine(neutral - right * (size * 0.06f), neutral + right * (size * 0.06f), 2f);
        ControlLabel(end + right * (size * 0.12f), ReaniIKBodyScale.MaximumSize.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        ControlLabel(start + right * (size * 0.12f), ReaniIKBodyScale.MinimumSize.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        ControlLabel(end+up*(size*.5f)-right*(size*.13f),label);
        if(DrawSliderResetButton(end+up*(size*.25f),label+" Size"))
        {
            reset();
            return;
        }
        var value = Mathf.Clamp(current, ReaniIKBodyScale.MinimumSize, ReaniIKBodyScale.MaximumSize);
        var thumb = Vector3.Lerp(start, end, Mathf.InverseLerp(ReaniIKBodyScale.MinimumSize, ReaniIKBodyScale.MaximumSize, value));
        EditorGUI.BeginChangeCheck();
        thumb = Handles.Slider(thumb, up, size * 0.10f, Handles.RectangleHandleCap, 0f);
        if (!EditorGUI.EndChangeCheck())
            return;
        var t = Mathf.Clamp01(Vector3.Dot(thumb - start, up) / length);
        RecordSceneControl(rig, "Scale "+label);
        setValue(Mathf.Lerp(ReaniIKBodyScale.MinimumSize, ReaniIKBodyScale.MaximumSize, t));
        PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
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
        float size = HandleUtility.GetHandleSize(position) * SceneControlScale;
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
            RecordSceneControl(controlled, "Edit " + controlled.name + " pose");
            if (position != startPosition) controlled.position = position - offset;
            if (rotation != startRotation) controlled.rotation = rotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(controlled);
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
#endif
