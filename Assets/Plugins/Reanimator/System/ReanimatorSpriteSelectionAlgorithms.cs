#if UNITY_EDITOR
using System;
using System.Globalization;

internal enum ReanimatorSpriteSelectionClickAction
{
    KeepSelection,
    ClearSelection,
    SelectSingle,
    ShowPicker
}

internal static class ReanimatorSpriteSelectionAlgorithms
{
    public static ReanimatorSpriteSelectionClickAction ResolveClickAction(int hitCount, bool locked = false)
    {
        if (locked)
            return ReanimatorSpriteSelectionClickAction.KeepSelection;
        if (hitCount <= 0)
            return ReanimatorSpriteSelectionClickAction.ClearSelection;
        return hitCount == 1
            ? ReanimatorSpriteSelectionClickAction.SelectSingle
            : ReanimatorSpriteSelectionClickAction.ShowPicker;
    }

    public static string BuildPickerLabel(string layerName, int layerId, int hitIndex)
    {
        var name = string.IsNullOrWhiteSpace(layerName) ? "<unnamed sprite>" : layerName.Trim();
        if (layerId != 0)
            return string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", name, layerId);

        return string.Format(CultureInfo.InvariantCulture, "{0} #{1}", name, Math.Max(0, hitIndex) + 1);
    }
}
#endif
