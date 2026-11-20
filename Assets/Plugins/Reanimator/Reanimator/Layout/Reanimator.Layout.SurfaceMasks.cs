#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    [SerializeField] private ReaniIKSurfacePainter surfaceMaskPainter;
    [SerializeField] private ReaniIK surfaceMaskRig;
    [SerializeField] private Vector2 surfaceMaskSettingsScroll;

    public static void OpenSurfaceMasks(ReaniIK rig)
    {
        var window = GetWindow<Reanimator>(WindowTitle);
        window.surfaceMaskRig = rig;
        window.SetPreviewMode(PreviewMode.SurfaceMasks);
        window.Show(); window.Focus(); window.Repaint();
    }
    private void DrawSurfaceMasksWorkspace()
    {
        if (!surfaceMaskPainter)
        {
            surfaceMaskPainter = CreateInstance<ReaniIKSurfacePainter>();
            surfaceMaskPainter.hideFlags = HideFlags.HideAndDontSave;
        }
        const float sidebarWidth = 220f;
        using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(sidebarWidth), GUILayout.ExpandHeight(true)))
            {
                surfaceMaskSettingsScroll = EditorGUILayout.BeginScrollView(surfaceMaskSettingsScroll);
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 90;
                try
                {
                    EditorGUILayout.LabelField("Surface Masks", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Scene rig (optional)");
                    surfaceMaskRig = (ReaniIK)EditorGUILayout.ObjectField(surfaceMaskRig, typeof(ReaniIK), true);
                    string path = GetSelectedAssetPath();
                    if (surfaceMaskRig && GUILayout.Button("Use selected PSD instead")) surfaceMaskRig = null;
                    if (!surfaceMaskRig) EditorGUILayout.LabelField(path ?? "Select a PSD asset", EditorStyles.wordWrappedLabel);
                    surfaceMaskPainter.Bind(surfaceMaskRig, path, Repaint, layerPreviewRenderer);
                    surfaceMaskPainter.DrawSettings();
                }
                finally { EditorGUIUtility.labelWidth = previousLabelWidth; EditorGUILayout.EndScrollView(); }
            }
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                surfaceMaskPainter.DrawCanvas(Mathf.Max(100, position.width - sidebarWidth - 240));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(220), GUILayout.ExpandHeight(true)))
                surfaceMaskPainter.DrawSpriteSelector();
        }
    }
}
#endif
