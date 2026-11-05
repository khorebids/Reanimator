#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

internal static class ReanimatorDebugPreviewGui
{
    public static readonly Color Hot = new Color(1f, 0.82f, 0.1f, 1f);
    public static readonly Color Selected = new Color(1f, 0.28f, 0.16f, 1f);
    public static readonly Color Neutral = new Color(0.55f, 0.65f, 0.8f, 1f);
    public static readonly Color Segment = new Color(0.28f, 0.95f, 0.72f, 1f);

    public static void DrawLabel(Vector2 position, string text, Color color)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var style = EditorStyles.miniLabel;
        var content = new GUIContent(text);
        var size = style.CalcSize(content);
        size.x = Mathf.Min(Mathf.Max(size.x + 8f, 48f), 240f);
        size.y += 4f;

        var rect = new Rect(position.x, position.y, size.x, size.y);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.68f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), color);

        var previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.95f);
        GUI.Label(new Rect(rect.x + 5f, rect.y + 2f, rect.width - 7f, rect.height - 4f), content, style);
        GUI.color = previousColor;
    }
}
#endif
