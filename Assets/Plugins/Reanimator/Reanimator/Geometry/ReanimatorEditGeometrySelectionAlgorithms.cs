#if UNITY_EDITOR
using System.Collections.Generic;

internal static class ReanimatorEditGeometrySelectionAlgorithms
{
    public static HashSet<int> RemapSelection(IReadOnlyList<int> selectedIndices, IReadOnlyList<int> remap)
    {
        var selected = new HashSet<int>();
        if (selectedIndices == null || remap == null)
            return selected;

        for (var i = 0; i < selectedIndices.Count; i++)
        {
            var oldIndex = selectedIndices[i];
            if (oldIndex < 0 || oldIndex >= remap.Count)
                continue;

            var newIndex = remap[oldIndex];
            if (newIndex >= 0)
                selected.Add(newIndex);
        }

        return selected;
    }

    public static List<int> NormalizeSelection(
        IReadOnlyList<int> selectedIndices,
        int primaryIndex,
        int vertexCount)
    {
        if (vertexCount <= 0)
            return new List<int>();

        var result = selectedIndices != null
            ? new List<int>(selectedIndices)
            : new List<int>();
        var used = new HashSet<int>();
        if (selectedIndices != null)
        {
            for (var i = result.Count - 1; i >= 0; i--)
            {
                var index = result[i];
                if (index < 0 || index >= vertexCount || !used.Add(index))
                    result.RemoveAt(i);
            }
        }

        if (result.Count == 0 && primaryIndex >= 0 && primaryIndex < vertexCount)
            result.Add(primaryIndex);

        return result;
    }
}
#endif
