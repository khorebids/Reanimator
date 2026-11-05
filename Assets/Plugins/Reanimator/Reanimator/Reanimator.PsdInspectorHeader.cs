#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class ReanimatorPsdInspectorHeader
{
    private const float ButtonHeight = 26f;

    static ReanimatorPsdInspectorHeader()
    {
        Editor.finishedDefaultHeaderGUI -= DrawHeader;
        Editor.finishedDefaultHeaderGUI += DrawHeader;
    }

    internal static bool SupportsAssetPath(string assetPath)
    {
        return Reanimator.IsPsdImporterAsset(assetPath);
    }

    private static void DrawHeader(Editor editor)
    {
        if (!TryGetInspectedPsdPath(editor, out var assetPath))
            return;

        GUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(8f);

            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 0.62f, 0.16f, 1f)
                : new Color(0.95f, 0.67f, 0.24f, 1f);

            var style = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = ButtonHeight,
                fontStyle = FontStyle.Bold
            };
            var content = new GUIContent(
                "Open in Reanimator",
                Reanimator.LoadWindowIcon(),
                "Open this PSD in Reanimator");

            if (GUILayout.Button(content, style, GUILayout.ExpandWidth(true)))
                Reanimator.OpenAsset(assetPath);

            GUI.backgroundColor = previousColor;
            GUILayout.Space(8f);
        }
        GUILayout.Space(2f);
    }

    private static bool TryGetInspectedPsdPath(Editor editor, out string assetPath)
    {
        assetPath = null;
        if (editor == null || editor.targets == null || editor.targets.Length != 1)
            return false;
        if (!(editor.target is AssetImporter importer))
            return false;
        if (!SupportsAssetPath(importer.assetPath))
            return false;

        assetPath = importer.assetPath;
        return true;
    }
}
#endif
