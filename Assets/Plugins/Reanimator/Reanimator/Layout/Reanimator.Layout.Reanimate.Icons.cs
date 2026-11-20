#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    private bool DrawReanimateIconButton(string label, string icon, bool selected = false,
        bool toggle = false, bool orange = false, float height = 18f)
    {
        // miniButton has a fixed height. A GUILayout height alone reserves a
        // taller row while drawing the same short button, creating empty gaps.
        var style = new GUIStyle(orange ? GetOrangeToolbarButtonStyle() : EditorStyles.miniButton)
        {
            fixedHeight = height
        };
        var content = new GUIContent(string.Empty, label);
        bool result = toggle
            ? GUILayout.Toggle(selected, content, style, GUILayout.Height(height), GUILayout.ExpandWidth(true))
            : GUILayout.Button(content, style, GUILayout.Height(height), GUILayout.ExpandWidth(true));
        DrawLeftToolButtonContent(GUILayoutUtility.GetLastRect(), label, iconSpriteName: icon);
        return result;
    }
}
#endif
