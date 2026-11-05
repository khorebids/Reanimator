#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class ReanimatorAssetChangeAlgorithms
{
    public static bool HasSourceAssetChangedSinceSnapshot(
        string cachedPath,
        string requestedPath,
        string cachedDependencyHash,
        string currentDependencyHash)
    {
        if (string.IsNullOrWhiteSpace(cachedDependencyHash))
            return false;
        if (string.IsNullOrWhiteSpace(currentDependencyHash))
            return false;
        if (!string.Equals(cachedPath, requestedPath, System.StringComparison.OrdinalIgnoreCase))
            return false;

        return !string.Equals(cachedDependencyHash, currentDependencyHash, System.StringComparison.Ordinal);
    }

    public static bool ShouldAutoRepairPsdImporterState(bool hasPendingChanges, bool hasUniformLayerResize)
    {
        return !hasPendingChanges && hasUniformLayerResize;
    }

    public static bool TryDetectUniformPsdLayerResize(
        IReadOnlyList<Rect> currentRects,
        IReadOnlyList<Rect> desiredRects,
        out Vector2 scale,
        out int changedCount,
        out int comparableCount)
    {
        scale = Vector2.one;
        changedCount = 0;
        comparableCount = 0;
        if (currentRects == null || desiredRects == null)
            return false;

        var count = Mathf.Min(currentRects.Count, desiredRects.Count);
        if (count <= 0)
            return false;

        var candidates = new List<Vector2>();
        for (var i = 0; i < count; i++)
        {
            var current = currentRects[i];
            var desired = desiredRects[i];
            if (current.width <= 0f || current.height <= 0f ||
                desired.width <= 0f || desired.height <= 0f)
                continue;

            comparableCount++;
            if (RectsRoughlyEqual(current, desired))
                continue;

            var sx = desired.width / current.width;
            var sy = desired.height / current.height;
            if (!IsFinitePositive(sx) || !IsFinitePositive(sy))
                continue;
            if (!IsMeaningfulScale(sx) || !IsMeaningfulScale(sy))
                continue;
            if (Mathf.Abs(sx - sy) > Mathf.Max(0.03f, Mathf.Max(sx, sy) * 0.08f))
                continue;

            candidates.Add(new Vector2(sx, sy));
        }

        changedCount = candidates.Count;
        if (comparableCount <= 0 || changedCount <= 0)
            return false;

        var requiredChanged = comparableCount >= 3
            ? Mathf.Max(3, Mathf.CeilToInt(comparableCount * 0.6f))
            : comparableCount;
        if (changedCount < requiredChanged)
            return false;

        var median = new Vector2(
            Median(candidates, value => value.x),
            Median(candidates, value => value.y));
        var agreeing = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (Mathf.Abs(candidate.x - median.x) <= Mathf.Max(0.03f, Mathf.Abs(median.x) * 0.08f) &&
                Mathf.Abs(candidate.y - median.y) <= Mathf.Max(0.03f, Mathf.Abs(median.y) * 0.08f))
                agreeing++;
        }

        if (agreeing < requiredChanged)
            return false;

        scale = median;
        return true;
    }

    public static Vector2 CalculatePsdDocumentResizeScale(Vector2Int previousDocumentSize, Vector2Int currentDocumentSize)
    {
        if (!IsPositiveDocumentSize(previousDocumentSize) || !IsPositiveDocumentSize(currentDocumentSize))
            return Vector2.one;

        return new Vector2(
            (float)currentDocumentSize.x / previousDocumentSize.x,
            (float)currentDocumentSize.y / previousDocumentSize.y);
    }

    public static bool PsdLayerSizeMatchesUniformResize(
        Rect currentRect,
        Vector2Int layerSizeOnFile,
        Vector2 uniformResizeScale)
    {
        if (currentRect.width <= 0f || currentRect.height <= 0f ||
            layerSizeOnFile.x <= 0 || layerSizeOnFile.y <= 0)
        {
            return true;
        }

        var expectedScaleX = Mathf.Abs(uniformResizeScale.x);
        var expectedScaleY = Mathf.Abs(uniformResizeScale.y);
        if (!IsFinitePositive(expectedScaleX) || !IsFinitePositive(expectedScaleY))
            return true;

        var layerScaleX = layerSizeOnFile.x / currentRect.width;
        var layerScaleY = layerSizeOnFile.y / currentRect.height;
        if (!IsFinitePositive(layerScaleX) || !IsFinitePositive(layerScaleY))
            return true;

        return ScalesRoughlyEqual(layerScaleX, expectedScaleX) &&
               ScalesRoughlyEqual(layerScaleY, expectedScaleY);
    }

    private static bool IsPositiveDocumentSize(Vector2Int size)
    {
        return size.x > 0 && size.y > 0;
    }

    private static bool IsFinitePositive(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsMeaningfulScale(float scale)
    {
        return scale >= 1.15f || scale <= 0.87f;
    }

    private static bool ScalesRoughlyEqual(float actual, float expected)
    {
        return Mathf.Abs(actual - expected) <= Mathf.Max(0.03f, Mathf.Abs(expected) * 0.08f);
    }

    private static float Median(IReadOnlyList<Vector2> values, System.Func<Vector2, float> selector)
    {
        var sorted = new List<float>(values.Count);
        for (var i = 0; i < values.Count; i++)
            sorted.Add(selector(values[i]));
        sorted.Sort();

        var mid = sorted.Count / 2;
        return (sorted.Count & 1) == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) * 0.5f;
    }

    public static Rect NormalizeSpriteRect(Rect rect)
    {
        if (rect.width < 0f)
        {
            rect.x += rect.width;
            rect.width = -rect.width;
        }
        if (rect.height < 0f)
        {
            rect.y += rect.height;
            rect.height = -rect.height;
        }

        rect.width = Mathf.Max(1f, rect.width);
        rect.height = Mathf.Max(1f, rect.height);
        return rect;
    }

    public static bool WriteRectIfChanged(SerializedProperty property, Rect value)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Rect)
        {
            if (RectsRoughlyEqual(property.rectValue, value))
                return false;
            property.rectValue = value;
            return true;
        }

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        var width = property.FindPropertyRelative("width");
        var height = property.FindPropertyRelative("height");
        if (x == null || y == null || width == null || height == null)
            return false;
        if (Mathf.Abs(x.floatValue - value.x) <= 0.0001f &&
            Mathf.Abs(y.floatValue - value.y) <= 0.0001f &&
            Mathf.Abs(width.floatValue - value.width) <= 0.0001f &&
            Mathf.Abs(height.floatValue - value.height) <= 0.0001f)
            return false;

        x.floatValue = value.x;
        y.floatValue = value.y;
        width.floatValue = value.width;
        height.floatValue = value.height;
        return true;
    }

    public static bool WriteVector4IfChanged(SerializedProperty property, Vector4 value)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Vector4)
        {
            if ((property.vector4Value - value).sqrMagnitude <= 1e-8f)
                return false;
            property.vector4Value = value;
            return true;
        }

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        var z = property.FindPropertyRelative("z");
        var w = property.FindPropertyRelative("w");
        if (x == null || y == null || z == null || w == null)
            return false;
        if (Mathf.Abs(x.floatValue - value.x) <= 0.0001f &&
            Mathf.Abs(y.floatValue - value.y) <= 0.0001f &&
            Mathf.Abs(z.floatValue - value.z) <= 0.0001f &&
            Mathf.Abs(w.floatValue - value.w) <= 0.0001f)
            return false;

        x.floatValue = value.x;
        y.floatValue = value.y;
        z.floatValue = value.z;
        w.floatValue = value.w;
        return true;
    }

    public static bool WriteIntIfChanged(SerializedProperty property, int value)
    {
        if (property == null || property.propertyType != SerializedPropertyType.Integer)
            return false;
        if (property.intValue == value)
            return false;

        property.intValue = value;
        return true;
    }

    public static bool RectsRoughlyEqual(Rect a, Rect b)
    {
        return Mathf.Abs(a.x - b.x) <= 0.0001f &&
               Mathf.Abs(a.y - b.y) <= 0.0001f &&
               Mathf.Abs(a.width - b.width) <= 0.0001f &&
               Mathf.Abs(a.height - b.height) <= 0.0001f;
    }

    public static bool ShouldReimportAfterPostAtlasSync(bool hasDataProviderChanges)
    {
        return false;
    }

    public static bool ShouldSyncAtlasBeforeMainReimport(bool hasPendingMeshWork)
    {
        return false;
    }

    public static bool ShouldWritePreviewStateToImporter(bool hasPendingChanges, bool hasPendingMeshWork)
    {
        return hasPendingChanges || hasPendingMeshWork;
    }

    public static bool ShouldSyncDataProviderSpriteRects(bool hasPendingChanges, bool hasPendingMeshWork)
    {
        return hasPendingChanges || hasPendingMeshWork;
    }

    public static bool ShouldApplyDataProviderBeforeSerializedPreviewState(bool hasPendingMeshWork, bool hasDataProviderSpriteChanges)
    {
        return false;
    }

    public static bool ShouldRunSpriteSheetGeometryRepair(bool repairedImporterRects, bool hasMissingGeometry)
    {
        return repairedImporterRects || hasMissingGeometry;
    }

    public static RectInt BuildCharacterPartSpritePosition(Vector2 spritePosition, Rect spriteRect)
    {
        return new RectInt(
            Mathf.RoundToInt(spritePosition.x),
            Mathf.RoundToInt(spritePosition.y),
            Mathf.RoundToInt(spriteRect.width),
            Mathf.RoundToInt(spriteRect.height));
    }

    public static bool TryUseForcedAutoGeometryImporterMode(string forcedMode, out string importerMode)
    {
        importerMode = string.IsNullOrWhiteSpace(forcedMode) ? string.Empty : forcedMode.Trim();
        return !string.IsNullOrEmpty(importerMode);
    }
}
#endif
