#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const string BoneNamePresetsAssetPath = "Assets/Plugins/Reanimator/Presets/Reanimator Bone Name Presets.asset";
    private const float BoneNamePresetButtonWidth = 24f;

    private void DrawEditBoneToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Bones Edit", EditorStyles.boldLabel);
                isolateBoneModification = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Isolate Bone Modification",
                        "Modify only the selected bone while keeping its child branches in their current world pose."),
                    isolateBoneModification);
                EditorGUILayout.Space(2f);

                var renderer = layerPreviewRenderer;
                var bones = renderer != null ? renderer.Bones : null;
                var selectedBoneIndex = renderer != null ? renderer.SelectedBoneIndex : -1;
                if (bones == null || bones.Count == 0)
                {
                    renderer?.SetPanelHoveredBoneIndex(-1);
                    EditorGUILayout.LabelField("No bones loaded.");
                    return;
                }

                if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count)
                {
                    renderer?.SetPanelHoveredBoneIndex(-1);
                    EditorGUILayout.LabelField("Select a bone in preview or Bones list.");
                    return;
                }

                var bone = bones[selectedBoneIndex];
                if (bone == null)
                {
                    renderer?.SetPanelHoveredBoneIndex(-1);
                    EditorGUILayout.LabelField("Selected bone is missing.");
                    return;
                }

                DrawSelectedBoneEditableFields(renderer, bones, selectedBoneIndex, bone);
            }
        }
    }

    private void DrawSelectedBoneEditableFields(
        PsdLayerPreviewRenderer renderer,
        System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneIndex,
        PsdLayerPreviewRenderer.BoneInfo bone)
    {
        DrawBoneNameField(renderer, bones, boneIndex, bone);

        EditorGUI.BeginChangeCheck();
        var visible = EditorGUILayout.Toggle("Visible", bone.visible);
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneVisible(boneIndex, visible))
            Repaint();

        EditorGUI.BeginChangeCheck();
        var depth = EditorGUILayout.IntField("Order", bone.depth);
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneDepth(boneIndex, depth))
            Repaint();

        EditorGUI.BeginChangeCheck();
        var color = EditorGUILayout.ColorField("Color", bone.color);
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneColor(boneIndex, color))
            Repaint();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.IntField("Index", boneIndex);

        var panelHoveredBone = -1;
        DrawBoneParentPopup(renderer, bones, boneIndex, bone.parentId, ref panelHoveredBone);
        DrawBoneParentActionButtons(renderer, bones, boneIndex, bone.parentId, ref panelHoveredBone);
        if (renderer.SetPanelHoveredBoneIndex(panelHoveredBone))
            Repaint();

        EditorGUI.BeginChangeCheck();
        var localPosition = EditorGUILayout.Vector2Field("Local Pos", bone.position);
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneLocalPosition(boneIndex, localPosition))
            Repaint();

        EditorGUI.BeginChangeCheck();
        var worldPosition = EditorGUILayout.Vector2Field("World Pos", bone.worldPosition);
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneWorldPosition(boneIndex, worldPosition))
            Repaint();

        EditorGUI.BeginChangeCheck();
        var localRotation = EditorGUILayout.FloatField("Local Rot", NormalizeAngle(bone.rotation.eulerAngles.z));
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneLocalRotationAngle(boneIndex, localRotation))
            Repaint();

        EditorGUI.BeginChangeCheck();
        var worldRotation = EditorGUILayout.FloatField("World Rot", NormalizeAngle(bone.worldRotation.eulerAngles.z));
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneWorldRotationAngle(boneIndex, worldRotation))
            Repaint();

        EditorGUI.BeginChangeCheck();
        var length = EditorGUILayout.FloatField("Length", bone.length);
        if (EditorGUI.EndChangeCheck() && renderer.SetBoneLength(boneIndex, length))
            Repaint();
    }

    private void DrawBoneNameField(
        PsdLayerPreviewRenderer renderer,
        System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneIndex,
        PsdLayerPreviewRenderer.BoneInfo bone)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.DelayedTextField("Name", string.IsNullOrEmpty(bone.name) ? $"bone_{boneIndex}" : bone.name);
            if (EditorGUI.EndChangeCheck() && renderer.RenameBone(boneIndex, name))
                Repaint();

            if (GUILayout.Button("R", EditorStyles.miniButton, GUILayout.Width(BoneNamePresetButtonWidth)))
                ShowBoneNamePresetMenu(renderer, bones, boneIndex);
        }
    }

    private void ShowBoneNamePresetMenu(
        PsdLayerPreviewRenderer renderer,
        System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneIndex)
    {
        var presets = LoadBoneNamePresets();
        var menu = new GenericMenu();
        if (presets == null)
        {
            menu.AddDisabledItem(new GUIContent("Preset asset not loaded"));
            menu.AddItem(new GUIContent("Create Default Preset Asset"), false, () =>
            {
                CreateDefaultBoneNamePresetAsset();
                Repaint();
            });
            menu.ShowAsContext();
            return;
        }

        if (presets.names == null || presets.names.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("No bone names configured"));
            menu.AddItem(new GUIContent("Sort Preset Asset By Name"), false, SortBoneNamePresetAssetByName);
            menu.AddItem(new GUIContent("Edit Preset Asset"), false, SelectBoneNamePresetAsset);
            menu.ShowAsContext();
            return;
        }

        var added = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < presets.names.Count; i++)
        {
            var presetName = SanitizeBoneNamePreset(presets.names[i]);
            if (string.IsNullOrEmpty(presetName) || !added.Add(presetName))
                continue;

            var finalName = BuildBoneNameFromParent(presetName, bones, boneIndex);
            var capturedName = finalName;
            var label = string.Equals(finalName, presetName, System.StringComparison.Ordinal)
                ? presetName
                : $"{presetName} -> {finalName}";
            menu.AddItem(new GUIContent(label), false, () =>
            {
                CancelBoneRename();
                if (renderer != null && renderer.RenameBone(boneIndex, capturedName))
                    Repaint();
            });
        }

        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("Sort Preset Asset By Name"), false, SortBoneNamePresetAssetByName);
        menu.AddItem(new GUIContent("Edit Preset Asset"), false, SelectBoneNamePresetAsset);
        menu.ShowAsContext();
    }

    private static ReanimatorBoneNamePresetAsset LoadBoneNamePresets()
    {
        return AssetDatabase.LoadAssetAtPath<ReanimatorBoneNamePresetAsset>(BoneNamePresetsAssetPath);
    }

    private static void CreateDefaultBoneNamePresetAsset()
    {
        var existing = LoadBoneNamePresets();
        if (existing != null)
        {
            SelectBoneNamePresetAsset();
            return;
        }

        var preset = ScriptableObject.CreateInstance<ReanimatorBoneNamePresetAsset>();
        preset.names = new System.Collections.Generic.List<string>(DefaultBoneNamePresets);
        var directory = System.IO.Path.GetDirectoryName(BoneNamePresetsAssetPath);
        if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            AssetDatabase.CreateFolder("Assets/Plugins/Reanimator", "Presets");
        if (AssetDatabase.LoadMainAssetAtPath(BoneNamePresetsAssetPath) != null)
            AssetDatabase.DeleteAsset(BoneNamePresetsAssetPath);

        AssetDatabase.CreateAsset(preset, BoneNamePresetsAssetPath);
        AssetDatabase.SaveAssetIfDirty(preset);
        AssetDatabase.Refresh();
        Selection.activeObject = preset;
        EditorGUIUtility.PingObject(preset);
    }

    private static void SortBoneNamePresetAssetByName()
    {
        var presets = LoadBoneNamePresets();
        if (presets == null)
        {
            CreateDefaultBoneNamePresetAsset();
            presets = LoadBoneNamePresets();
        }

        if (presets == null)
            return;

        var sortedNames = SortBoneNamePresetsByName(presets.names);
        if (!BoneNamePresetListsEqual(presets.names, sortedNames))
        {
            Undo.RegisterCompleteObjectUndo(presets, "Sort Bone Name Presets");
            presets.names = sortedNames;
            EditorUtility.SetDirty(presets);
            AssetDatabase.SaveAssetIfDirty(presets);
        }

        Selection.activeObject = presets;
        EditorGUIUtility.PingObject(presets);
    }

    internal static System.Collections.Generic.List<string> SortBoneNamePresetsByName(
        System.Collections.Generic.IReadOnlyList<string> names)
    {
        var sortedNames = names != null
            ? new System.Collections.Generic.List<string>(names)
            : new System.Collections.Generic.List<string>();
        sortedNames.Sort(CompareBoneNamePresets);
        return sortedNames;
    }

    private static int CompareBoneNamePresets(string left, string right)
    {
        var leftKey = SanitizeBoneNamePreset(left);
        var rightKey = SanitizeBoneNamePreset(right);
        var compare = string.Compare(leftKey, rightKey, System.StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;

        compare = string.Compare(leftKey, rightKey, System.StringComparison.Ordinal);
        if (compare != 0)
            return compare;

        return string.Compare(left ?? string.Empty, right ?? string.Empty, System.StringComparison.Ordinal);
    }

    private static bool BoneNamePresetListsEqual(
        System.Collections.Generic.IReadOnlyList<string> left,
        System.Collections.Generic.IReadOnlyList<string> right)
    {
        var leftCount = left != null ? left.Count : 0;
        var rightCount = right != null ? right.Count : 0;
        if (leftCount != rightCount)
            return false;

        for (var i = 0; i < leftCount; i++)
        {
            if (!string.Equals(left[i], right[i], System.StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static readonly string[] DefaultBoneNamePresets =
    {
        "belly", "boobs_root", "bot", "chest", "face_form", "head", "l_arm", "l_boob",
        "l_elbow", "l_foot", "l_foot_4", "l_hand", "l_knee", "l_leg", "l_lock", "l_lock_5",
        "l_nipple", "neck", "pony", "pony_2", "r_arm", "r_boob", "r_elbow", "r_foot",
        "r_foot_3", "r_hand", "r_knee", "r_leg", "r_lock", "r_lock_6", "r_nipple", "root"
    };

    private static void SelectBoneNamePresetAsset()
    {
        var presets = LoadBoneNamePresets();
        if (presets == null)
            return;

        Selection.activeObject = presets;
        EditorGUIUtility.PingObject(presets);
    }

    internal static string BuildBoneNameFromParent(
        string presetName,
        System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneIndex)
    {
        presetName = SanitizeBoneNamePreset(presetName);
        if (string.IsNullOrEmpty(presetName) || bones == null || boneIndex < 0 || boneIndex >= bones.Count)
            return presetName;

        var bone = bones[boneIndex];
        var parentIndex = bone != null ? bone.parentId : -1;
        if (parentIndex < 0 || parentIndex >= bones.Count || bones[parentIndex] == null)
            return presetName;

        var parentName = SanitizeBoneNamePreset(bones[parentIndex].name);
        if (string.IsNullOrEmpty(parentName))
            return presetName;

        if (string.Equals(parentName, presetName, System.StringComparison.OrdinalIgnoreCase))
            return presetName + "_1";

        if (TryParseBoneNameSuffix(parentName, out var parentBaseName, out var suffix) &&
            string.Equals(parentBaseName, presetName, System.StringComparison.OrdinalIgnoreCase))
            return presetName + "_" + (suffix + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return presetName;
    }

    private static string SanitizeBoneNamePreset(string value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static bool TryParseBoneNameSuffix(string value, out string baseName, out int suffix)
    {
        baseName = value;
        suffix = 0;
        if (string.IsNullOrEmpty(value))
            return false;

        var underscore = value.LastIndexOf('_');
        if (underscore <= 0 || underscore >= value.Length - 1)
            return false;

        var suffixText = value.Substring(underscore + 1);
        if (!int.TryParse(suffixText, out suffix) || suffix < 0)
            return false;

        baseName = value.Substring(0, underscore);
        return !string.IsNullOrEmpty(baseName);
    }

    private void DrawBoneParentPopup(
        PsdLayerPreviewRenderer renderer,
        System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneIndex,
        int currentParent,
        ref int panelHoveredBone)
    {
        var parentOptions = BuildBoneParentOptions(bones, boneIndex, out var parentIds);
        var currentOption = 0;
        for (var i = 0; i < parentIds.Length; i++)
        {
            if (parentIds[i] != currentParent)
                continue;

            currentOption = i;
            break;
        }

        EditorGUI.BeginChangeCheck();
        var nextOption = EditorGUILayout.Popup("Parent", currentOption, parentOptions);
        var parentRect = GUILayoutUtility.GetLastRect();
        if (IsMouseOverControl(parentRect))
            panelHoveredBone = currentParent;
        if (EditorGUI.EndChangeCheck() &&
            nextOption >= 0 &&
            nextOption < parentIds.Length &&
            renderer.SetBoneParent(boneIndex, parentIds[nextOption]))
        {
            panelHoveredBone = parentIds[nextOption];
            Repaint();
        }
    }

    private void DrawBoneParentActionButtons(
        PsdLayerPreviewRenderer renderer,
        System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneIndex,
        int currentParent,
        ref int panelHoveredBone)
    {
        var hasParent = currentParent >= 0 && bones != null && currentParent < bones.Count && bones[currentParent] != null;
        using (new EditorGUI.DisabledScope(!hasParent))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("To Parent Tip"))
                {
                    if (renderer.MoveBoneToParentTip(boneIndex))
                        Repaint();
                    panelHoveredBone = currentParent;
                }

                var positionButtonRect = GUILayoutUtility.GetLastRect();
                if (hasParent && IsMouseOverControl(positionButtonRect))
                    panelHoveredBone = currentParent;

                if (GUILayout.Button("To Parent Rot"))
                {
                    if (renderer.RotateBoneToParentRotation(boneIndex))
                        Repaint();
                    panelHoveredBone = currentParent;
                }

                var rotationButtonRect = GUILayoutUtility.GetLastRect();
                if (hasParent && IsMouseOverControl(rotationButtonRect))
                    panelHoveredBone = currentParent;
            }
        }
    }

    private static string[] BuildBoneParentOptions(
        System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneIndex,
        out int[] parentIds)
    {
        var options = new System.Collections.Generic.List<string> { "None" };
        var ids = new System.Collections.Generic.List<int> { -1 };
        if (bones != null)
        {
            for (var i = 0; i < bones.Count; i++)
            {
                if (i == boneIndex || IsBoneDescendant(bones, i, boneIndex))
                    continue;

                ids.Add(i);
                options.Add(FormatBoneParent(bones, i));
            }
        }

        parentIds = ids.ToArray();
        return options.ToArray();
    }

    private static string FormatBoneParent(System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, int parentId)
    {
        if (parentId < 0)
            return "None";
        if (bones == null || parentId >= bones.Count || bones[parentId] == null)
            return parentId.ToString();

        var parent = bones[parentId];
        var name = string.IsNullOrEmpty(parent.name) ? $"bone_{parentId}" : parent.name;
        return $"{parentId}: {name}";
    }

    private static bool IsBoneDescendant(System.Collections.Generic.IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, int candidateIndex, int ancestorIndex)
    {
        if (bones == null || candidateIndex < 0 || ancestorIndex < 0)
            return false;

        var guard = 0;
        var current = candidateIndex;
        while (current >= 0 && current < bones.Count && guard++ <= bones.Count)
        {
            if (current == ancestorIndex)
                return true;

            var bone = bones[current];
            current = bone != null ? bone.parentId : -1;
        }

        return false;
    }

    private static bool IsMouseOverControl(Rect rect)
    {
        var current = Event.current;
        return current != null &&
               current.type != EventType.Layout &&
               rect.width > 0f &&
               rect.height > 0f &&
               rect.Contains(current.mousePosition);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        if (angle <= -180f)
            angle += 360f;
        return angle;
    }
}
#endif
