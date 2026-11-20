#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    private void ShowFixGeometryMenu()
    {
        var menu = new GenericMenu();
        if (layerPreviewRenderer != null && layerPreviewRenderer.TryGetSelectedLayer(out _))
            menu.AddItem(new GUIContent("Selected geometry"), false, () => FixGeometry(false));
        else
            menu.AddDisabledItem(new GUIContent("Selected geometry"));
        menu.AddItem(new GUIContent("All geometry"), false, () => FixGeometry(true));
        menu.ShowAsContext();
    }

    private void FixGeometry(bool allSprites)
    {
        if (layerPreviewRenderer == null) return;
        if (!layerPreviewRenderer.FixGeometryOutsideContours(allSprites, out int removed, out int sprites, out string error))
            ShowNotification(new GUIContent(error));
        else
            ShowNotification(new GUIContent($"Fix Geo: removed {removed} vertices in {sprites} sprites."));
        Repaint();
    }
}
#endif
