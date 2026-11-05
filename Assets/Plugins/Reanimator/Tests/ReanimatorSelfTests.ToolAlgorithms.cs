#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    private static void ToolModeAlgorithmsNormalizeEditGeometryModes()
    {
        AssertEqual(
            ReanimatorToolMode.EditGeometry,
            ReanimatorToolModeAlgorithms.NormalizeToolMode(ReanimatorToolMode.DeprecatedCreateVertex),
            "Deprecated create vertex should normalize to edit geometry.");
        AssertEqual(
            ReanimatorToolMode.PreviewPose,
            ReanimatorToolModeAlgorithms.NormalizeToolMode(ReanimatorToolMode.SetPivot),
            "Removed SetPivot tool mode should normalize to PreviewPose.");

        AssertTrue(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.EditGeometry), "EditGeometry should be an edit selection mode.");
        AssertTrue(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.CreateEdge), "CreateEdge should be an edit selection mode.");
        AssertTrue(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.SplitEdge), "SplitEdge should be an edit selection mode.");
        AssertTrue(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.BoneInfluence), "BoneInfluence should be an edit selection mode.");
        AssertTrue(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.SpriteInfluence), "SpriteInfluence should be an edit selection mode.");
        AssertTrue(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.DeprecatedCreateVertex), "DeprecatedCreateVertex should inherit edit selection behavior.");
        AssertFalse(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.WeightSlider), "WeightSlider should not be an edit selection mode.");
        AssertFalse(ReanimatorToolModeAlgorithms.IsEditGeometrySelectionToolMode(ReanimatorToolMode.VolumeCurve), "VolumeCurve should not be an edit selection mode.");

        AssertTrue(ReanimatorToolModeAlgorithms.AllowsBonePoseEditing(ReanimatorToolMode.PreviewPose), "PreviewPose should be allowed to edit preview bone pose.");
        AssertTrue(ReanimatorToolModeAlgorithms.AllowsBonePoseEditing(ReanimatorToolMode.WeightBrush), "WeightBrush should allow rotating bones while painting weights.");
        AssertTrue(ReanimatorToolModeAlgorithms.AllowsBonePoseEditing(ReanimatorToolMode.WeightSlider), "WeightSlider should allow preview bone pose outside slider input.");
        AssertTrue(ReanimatorToolModeAlgorithms.AllowsBonePoseEditing(ReanimatorToolMode.VolumeCurve), "VolumeCurve should not be globally pose-locked by tool mode.");
        AssertTrue(ReanimatorToolModeAlgorithms.AllowsBoneTransformEditing(ReanimatorToolMode.PreviewPose), "PreviewPose should allow bone transform input.");
        AssertTrue(ReanimatorToolModeAlgorithms.AllowsBoneTransformEditing(ReanimatorToolMode.EditBone), "EditBone should allow moving and rotating rest bones.");
        AssertTrue(ReanimatorToolModeAlgorithms.AllowsBoneTransformEditing(ReanimatorToolMode.VolumeCurve), "VolumeCurve should allow bone transform input outside curve input.");
        AssertTrue(ReanimatorToolModeAlgorithms.DeformsSpritesWithBonePose(ReanimatorToolMode.PreviewPose), "PreviewPose should deform sprites with preview bone pose.");
        AssertTrue(ReanimatorToolModeAlgorithms.DeformsSpritesWithBonePose(ReanimatorToolMode.WeightBrush), "WeightBrush should show painted weights under preview bone pose.");
        AssertFalse(ReanimatorToolModeAlgorithms.AllowsBonePoseEditing(ReanimatorToolMode.EditBone), "EditBone edits skeleton structure, not preview pose.");
        AssertFalse(ReanimatorToolModeAlgorithms.DeformsSpritesWithBonePose(ReanimatorToolMode.EditBone), "EditBone should edit the rest skeleton without deforming sprites.");
        AssertFalse(ReanimatorToolModeAlgorithms.AllowsBonePoseEditing(ReanimatorToolMode.CreateBone), "CreateBone edits skeleton structure, not preview pose.");
        AssertFalse(ReanimatorToolModeAlgorithms.AllowsBoneTransformEditing(ReanimatorToolMode.CreateBone), "CreateBone should reserve bone input for create/split gestures.");
        AssertFalse(ReanimatorToolModeAlgorithms.DeformsSpritesWithBonePose(ReanimatorToolMode.CreateBone), "CreateBone should keep sprites in restored pose while editing bones.");
    }

    private static void ToolModeAlgorithmsDefaultEmptyModeToPreviewPose()
    {
        AssertEqual(
            ReanimatorToolMode.PreviewPose,
            ReanimatorToolModeAlgorithms.NormalizeDefaultToolMode(ReanimatorToolMode.NoSettings),
            "Empty deserialized tool mode should fall back to PreviewPose.");
        AssertEqual(
            ReanimatorToolMode.EditGeometry,
            ReanimatorToolModeAlgorithms.NormalizeDefaultToolMode(ReanimatorToolMode.DeprecatedCreateVertex),
            "Deprecated create vertex should still normalize when defaulting tool mode.");
        AssertEqual(
            ReanimatorToolMode.PreviewPose,
            ReanimatorToolModeAlgorithms.NormalizeDefaultToolMode(ReanimatorToolMode.SetPivot),
            "Removed SetPivot mode should default to PreviewPose.");
        AssertEqual(
            ReanimatorToolMode.VolumeCurve,
            ReanimatorToolModeAlgorithms.NormalizeDefaultToolMode(ReanimatorToolMode.VolumeCurve),
            "Active tool modes should not be changed by default normalization.");

        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.NoSettings);
            InvokePrivateMethod(window, "EnsureDefaultToolMode");
            AssertEqual(
                ReanimatorToolMode.PreviewPose,
                GetPrivateField<ReanimatorToolMode>(window, "bottomToolMode"),
                "Window reload with no active tool should activate PreviewPose.");

            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.VolumeCurve);
            InvokePrivateMethod(window, "EnsureDefaultToolMode");
            AssertEqual(
                ReanimatorToolMode.VolumeCurve,
                GetPrivateField<ReanimatorToolMode>(window, "bottomToolMode"),
                "Window default guard should preserve a valid active tool.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void BoneHoverAlgorithmsNormalizeHoveredBoneIndex()
    {
        AssertEqual(0, ReanimatorToolModeAlgorithms.NormalizeHoveredBoneIndex(0, 3), "First bone should be hoverable.");
        AssertEqual(2, ReanimatorToolModeAlgorithms.NormalizeHoveredBoneIndex(2, 3), "Last bone should be hoverable.");
        AssertEqual(-1, ReanimatorToolModeAlgorithms.NormalizeHoveredBoneIndex(-1, 3), "Negative hover should clear.");
        AssertEqual(-1, ReanimatorToolModeAlgorithms.NormalizeHoveredBoneIndex(3, 3), "Out-of-range hover should clear.");
        AssertEqual(-1, ReanimatorToolModeAlgorithms.NormalizeHoveredBoneIndex(0, 0), "Empty bone list should clear hover.");
    }

    private static void BoneSelectionHoverPreviewRequestsMouseMove()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            SetPrivateField(window, "layerPreviewRenderer", new PsdLayerPreviewRenderer());
            var previewModeType = typeof(Reanimator).GetNestedType("PreviewMode", BindingFlags.NonPublic);
            SetPrivateField(window, "previewMode", Enum.Parse(previewModeType, "Skel"));
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.PreviewPose);

            InvokePrivateMethod(window, "UpdateWindowEventOptions");
            AssertTrue(window.wantsMouseMove, "Bone selection hover should receive mouse move events before drag starts.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void ToolModeAlgorithmsCancelOnlyExitedPreviewTools()
    {
        AssertTrue(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.Elbow, ReanimatorToolMode.Limbs, ReanimatorToolCancelTarget.Elbow),
            "Leaving Elbow should cancel elbow state.");
        AssertFalse(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.Elbow, ReanimatorToolMode.Elbow, ReanimatorToolCancelTarget.Elbow),
            "Staying in Elbow should not cancel elbow state.");

        AssertTrue(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.CreateEdge, ReanimatorToolMode.PreviewPose, ReanimatorToolCancelTarget.CreateEdge),
            "Leaving CreateEdge should cancel create-edge state.");
        AssertTrue(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.CreateEdge, ReanimatorToolMode.PreviewPose, ReanimatorToolCancelTarget.EditGeometry),
            "Leaving CreateEdge to non-edit mode should also cancel edit selection state.");
        AssertFalse(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.CreateEdge, ReanimatorToolMode.SplitEdge, ReanimatorToolCancelTarget.EditGeometry),
            "Moving inside edit selection modes should preserve edit selection state.");
        AssertTrue(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.BoneInfluence, ReanimatorToolMode.VolumeCurve, ReanimatorToolCancelTarget.EditGeometry),
            "Leaving influence mode should cancel edit selection state.");

        AssertTrue(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.WeightSlider, ReanimatorToolMode.WeightBrush, ReanimatorToolCancelTarget.WeightSlider),
            "Leaving WeightSlider should cancel slider state.");
        AssertFalse(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.WeightSlider, ReanimatorToolMode.WeightBrush, ReanimatorToolCancelTarget.WeightBrush),
            "Leaving WeightSlider should not cancel WeightBrush state.");
        AssertTrue(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.VolumeCurve, ReanimatorToolMode.NoSettings, ReanimatorToolCancelTarget.VolumeCurve),
            "Leaving VolumeCurve should cancel volume curve drag state.");
        AssertFalse(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(ReanimatorToolMode.DeprecatedCreateVertex, ReanimatorToolMode.EditGeometry, ReanimatorToolCancelTarget.EditGeometry),
            "DeprecatedCreateVertex normalized to EditGeometry should not cancel edit state.");
    }

    private static void SpriteSelectionClickOpensPickerOnlyForStackedHits()
    {
        AssertEqual(
            ReanimatorSpriteSelectionClickAction.ClearSelection,
            ReanimatorSpriteSelectionAlgorithms.ResolveClickAction(0),
            "No sprite under cursor should clear the selection.");
        AssertEqual(
            ReanimatorSpriteSelectionClickAction.SelectSingle,
            ReanimatorSpriteSelectionAlgorithms.ResolveClickAction(1),
            "One sprite under cursor should select directly.");
        AssertEqual(
            ReanimatorSpriteSelectionClickAction.ShowPicker,
            ReanimatorSpriteSelectionAlgorithms.ResolveClickAction(2),
            "Two sprites under cursor should open the picker.");
        AssertEqual(
            ReanimatorSpriteSelectionClickAction.ShowPicker,
            ReanimatorSpriteSelectionAlgorithms.ResolveClickAction(5),
            "Stacked sprites under cursor should open the picker.");
        AssertEqual(
            ReanimatorSpriteSelectionClickAction.KeepSelection,
            ReanimatorSpriteSelectionAlgorithms.ResolveClickAction(1, locked: true),
            "Locked selection should ignore direct sprite hits.");
        AssertEqual(
            ReanimatorSpriteSelectionClickAction.KeepSelection,
            ReanimatorSpriteSelectionAlgorithms.ResolveClickAction(3, locked: true),
            "Locked selection should not open the stacked sprite picker.");
        AssertEqual(
            "arm [42]",
            ReanimatorSpriteSelectionAlgorithms.BuildPickerLabel(" arm ", 42, 0),
            "Picker label should include stable layer id.");
        AssertEqual(
            "<unnamed sprite> #3",
            ReanimatorSpriteSelectionAlgorithms.BuildPickerLabel(null, 0, 2),
            "Picker label without layer id should include hit order.");
    }

    private static void HotkeyAlgorithmsMapToolKeysAndCharacters()
    {
        AssertHotkey(KeyCode.Q, '\0', ReanimatorToolMode.PreviewPose, "Q key");
        AssertHotkey(KeyCode.B, '\0', ReanimatorToolMode.WeightBrush, "B key");
        AssertHotkey(KeyCode.R, '\0', ReanimatorToolMode.Projector, "R key");
        AssertHotkey(KeyCode.C, '\0', ReanimatorToolMode.WeightSlider, "C key");
        AssertHotkey(KeyCode.V, '\0', ReanimatorToolMode.EditGeometry, "V key");
        AssertHotkey(KeyCode.X, '\0', ReanimatorToolMode.Curve, "X key");

        AssertHotkey(KeyCode.None, 'q', ReanimatorToolMode.PreviewPose, "q char");
        AssertHotkey(KeyCode.None, 'Q', ReanimatorToolMode.PreviewPose, "Q char");
        AssertHotkey(KeyCode.None, 'c', ReanimatorToolMode.WeightSlider, "c char");
        AssertHotkey(KeyCode.None, 'C', ReanimatorToolMode.WeightSlider, "C char");
        AssertHotkey(KeyCode.None, 'v', ReanimatorToolMode.EditGeometry, "v char");
        AssertHotkey(KeyCode.None, 'V', ReanimatorToolMode.EditGeometry, "V char");
        AssertHotkey(KeyCode.None, 'x', ReanimatorToolMode.Curve, "x char");
        AssertHotkey(KeyCode.None, 'X', ReanimatorToolMode.Curve, "X char");
        AssertHotkey(KeyCode.None, '\u0439', ReanimatorToolMode.PreviewPose, "Russian q char");
        AssertHotkey(KeyCode.None, '\u0438', ReanimatorToolMode.WeightBrush, "Russian b char");
        AssertHotkey(KeyCode.None, '\u043a', ReanimatorToolMode.Projector, "Russian r char");
        AssertHotkey(KeyCode.None, '\u0441', ReanimatorToolMode.WeightSlider, "Russian c char");
        AssertHotkey(KeyCode.None, '\u043c', ReanimatorToolMode.EditGeometry, "Russian v char");
        AssertHotkey(KeyCode.None, '\u0447', ReanimatorToolMode.Curve, "Russian x char");

        AssertFalse(ReanimatorHotkeyAlgorithms.TryGetToolHotkeyMode(KeyCode.Z, 'z', out var mode), "Unknown hotkey should fail.");
        AssertEqual(ReanimatorToolMode.NoSettings, mode, "Unknown hotkey fallback mode.");
    }

    private static void HotkeyAlgorithmsMapRestorePoseKey()
    {
        AssertTrue(ReanimatorHotkeyAlgorithms.IsRestorePoseHotkey(KeyCode.E, '\0'), "E key should restore pose.");
        AssertTrue(ReanimatorHotkeyAlgorithms.IsRestorePoseHotkey(KeyCode.None, 'e'), "e char should restore pose.");
        AssertTrue(ReanimatorHotkeyAlgorithms.IsRestorePoseHotkey(KeyCode.None, 'E'), "E char should restore pose.");
        AssertTrue(ReanimatorHotkeyAlgorithms.IsRestorePoseHotkey(KeyCode.None, '\u0443'), "Russian e key char should restore pose.");
        AssertFalse(ReanimatorHotkeyAlgorithms.IsRestorePoseHotkey(KeyCode.Q, 'q'), "Preview hotkey should not restore pose.");
    }

    private static void HotkeyAlgorithmsMapAutoGeometryKeys()
    {
        AssertTrue(ReanimatorHotkeyAlgorithms.IsAutoGeometryHotkey(KeyCode.G, '\0'), "G key should map to auto geometry.");
        AssertTrue(ReanimatorHotkeyAlgorithms.IsAutoGeometryHotkey(KeyCode.None, 'g'), "g char should map to auto geometry.");
        AssertTrue(ReanimatorHotkeyAlgorithms.IsAutoGeometryHotkey(KeyCode.None, 'G'), "G char should map to auto geometry.");
        AssertTrue(ReanimatorHotkeyAlgorithms.IsAutoGeometryHotkey(KeyCode.None, '\u043f'), "Russian g char should map to auto geometry.");
        AssertFalse(ReanimatorHotkeyAlgorithms.IsAutoGeometryHotkey(KeyCode.C, 'c'), "Curve key should not map to auto geometry.");
    }

    private static void HotkeyAlgorithmsConfirmAutoGeometryOnSecondPressOnly()
    {
        AssertFalse(
            ReanimatorHotkeyAlgorithms.IsConfirmedDoublePress(
                sameToolMode: true,
                lastPressTime: 0d,
                currentTime: 10d,
                doublePressSeconds: 0.35d),
            "An already selected Auto Geometry tool without a pending first press must not generate on a single G.");
        AssertFalse(
            ReanimatorHotkeyAlgorithms.IsConfirmedDoublePress(
                sameToolMode: false,
                lastPressTime: 9.9d,
                currentTime: 10d,
                doublePressSeconds: 0.35d),
            "Switching into Auto Geometry should arm the hotkey, not generate.");
        AssertTrue(
            ReanimatorHotkeyAlgorithms.IsConfirmedDoublePress(
                sameToolMode: true,
                lastPressTime: 9.9d,
                currentTime: 10d,
                doublePressSeconds: 0.35d),
            "Second G inside the double-press window should generate.");
        AssertFalse(
            ReanimatorHotkeyAlgorithms.IsConfirmedDoublePress(
                sameToolMode: true,
                lastPressTime: 9.0d,
                currentTime: 10d,
                doublePressSeconds: 0.35d),
            "Second G after the double-press window should only re-arm.");
    }

    private static void HotkeyAlgorithmsFilterUnavailableEditorEvents()
    {
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(null, false, false, 0, ReanimatorToolMode.NoSettings), "Null event should not handle.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(new Event { type = EventType.MouseDown }, false, false, 0, ReanimatorToolMode.NoSettings), "Mouse event should not handle.");
        AssertTrue(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(), false, false, 0, ReanimatorToolMode.NoSettings), "Plain key event should handle.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(), true, false, 0, ReanimatorToolMode.NoSettings), "Sprite mode should block hotkeys.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(), false, true, 0, ReanimatorToolMode.NoSettings), "Text field editing should block hotkeys.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(), false, false, 7, ReanimatorToolMode.NoSettings), "Hot control should block hotkeys.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(EventModifiers.Shift), false, false, 0, ReanimatorToolMode.NoSettings), "Shift should block hotkeys.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(EventModifiers.Control), false, false, 0, ReanimatorToolMode.NoSettings), "Control should block hotkeys.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(EventModifiers.Alt), false, false, 0, ReanimatorToolMode.NoSettings), "Alt should block hotkeys.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(EventModifiers.Command), false, false, 0, ReanimatorToolMode.NoSettings), "Command should block hotkeys.");
    }

    private static void HotkeyQCancelActiveLimbsInteraction()
    {
        var q = BuildHotkeyEvent();
        AssertTrue(
            ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(q, false, false, 7, ReanimatorToolMode.Limbs),
            "Q should interrupt an active Limbs mouse interaction.");
        AssertFalse(
            ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(q, false, true, 7, ReanimatorToolMode.Limbs),
            "Q should not cancel Limbs while editing text.");
        AssertFalse(
            ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(q, false, false, 7, ReanimatorToolMode.Elbow),
            "Q should not bypass hot control for unrelated tools.");
        AssertFalse(
            ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(BuildHotkeyEvent(EventModifiers.Shift), false, false, 7, ReanimatorToolMode.Limbs),
            "Modified Q should not cancel Limbs.");
        AssertTrue(
            ReanimatorToolModeAlgorithms.ShouldCancelOnTransition(
                ReanimatorToolMode.Limbs,
                ReanimatorToolMode.PreviewPose,
                ReanimatorToolCancelTarget.Limbs),
            "Q transition to PreviewPose should invoke the Limbs cancel lifecycle.");
    }

    private static void HotkeyAlgorithmsMovePreviewCameraWithWasd()
    {
        AssertTrue(
            ReanimatorHotkeyAlgorithms.TryGetPreviewCameraPanDelta(KeyCode.W, '\0', false, out var panDelta),
            "W should move preview camera.");
        AssertVector(new Vector2(0f, 16f), panDelta, 0.0001f, "W should move camera up by shifting content down.");

        AssertTrue(
            ReanimatorHotkeyAlgorithms.TryGetPreviewCameraPanDelta(KeyCode.D, '\0', true, out panDelta),
            "Shift+D should move preview camera faster.");
        AssertVector(new Vector2(-48f, 0f), panDelta, 0.0001f, "Shift+D should use the 3x speed.");

        AssertTrue(
            ReanimatorHotkeyAlgorithms.TryGetPreviewCameraPanDelta(KeyCode.None, '\u0444', false, out panDelta),
            "Russian A character should move preview camera.");
        AssertVector(new Vector2(16f, 0f), panDelta, 0.0001f, "Russian A character should map to camera-left movement.");

        var plain = new Event { type = EventType.KeyDown, keyCode = KeyCode.S };
        var shifted = new Event { type = EventType.KeyDown, keyCode = KeyCode.S, modifiers = EventModifiers.Shift };
        var controlled = new Event { type = EventType.KeyDown, keyCode = KeyCode.S, modifiers = EventModifiers.Control };
        AssertTrue(ReanimatorHotkeyAlgorithms.ShouldHandlePreviewCameraPanEvent(plain, false, 0), "Plain WASD should pan preview.");
        AssertTrue(ReanimatorHotkeyAlgorithms.ShouldHandlePreviewCameraPanEvent(shifted, false, 0), "Shift+WASD should pan preview.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandlePreviewCameraPanEvent(controlled, false, 0), "Control+WASD should stay available to editor shortcuts.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandlePreviewCameraPanEvent(plain, true, 0), "Text field editing should block preview pan.");
        AssertFalse(ReanimatorHotkeyAlgorithms.ShouldHandlePreviewCameraPanEvent(plain, false, 3), "Active hot control should block preview pan.");
    }
}
#endif
