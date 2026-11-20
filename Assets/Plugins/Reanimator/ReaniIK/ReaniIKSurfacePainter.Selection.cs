#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKSurfacePainter
{
    private Vector2 spriteSelectorScroll;
    public void DrawSpriteSelector()
    {
        EditorGUILayout.LabelField("Affected sprites", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Only checked sprites are shown, painted and sent to Comfy. Click a name to select.", EditorStyles.wordWrappedLabel);
        if (!maps || sprites == null) return;
        spriteSelectorScroll = EditorGUILayout.BeginScrollView(spriteSelectorScroll);
        try
        {
            for (int i=0; i<sprites.Length; i++)
            {
                var item=ReaniIKSurfaceAuthoring.EnsureEntry(maps,sprites[i]);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool enabled=EditorGUILayout.Toggle(item.effectsEnabled,GUILayout.Width(18));
                    if (enabled!=item.effectsEnabled)
                    {
                        SaveBodyCanvas(); Save(); Undo.RecordObject(maps,"Select surface effects sprite");
                        item.effectsEnabled=enabled; EditorUtility.SetDirty(maps); AssetDatabase.SaveAssetIfDirty(maps);
                        UpdateOverlay(); foreach(var s in bodySurfaces) UpdateBodyOverlay(s);
                        EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); Repaint();
                    }
                    if (GUILayout.Toggle(selected==i,sprites[i].name,"Button") && selected!=i)
                    { SaveBodyCanvas(); Save(); selected=i; Load(); Repaint(); }
                }
            }
        }
        finally { EditorGUILayout.EndScrollView(); }
    }
}
#endif
