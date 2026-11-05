#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

internal sealed class ReanimatorWindowLayout
{
    private const float TopPadding = 4f;
    private const float ContentPadding = 4f;
    private const float LeftPanelWidth = 220f;
    private const float MinPreviewWidth = 180f;
    public const float DefaultRightPanelWidth = 300f;
    public const float MinRightPanelWidth = 240f;
    public const float MaxRightPanelWidth = 560f;
    public const float RightPanelSplitterWidth = 5f;

    public Rect ContentRect { get; private set; }
    public Rect LeftPanelRect { get; private set; }
    public Rect PreviewRect { get; private set; }
    public Rect RightPanelRect { get; private set; }
    public Rect RightPanelSplitterRect { get; private set; }

    public static float ClampRightPanelWidth(float width, float windowWidth)
    {
        var maxByWindow = Mathf.Max(MinRightPanelWidth, windowWidth - LeftPanelWidth - MinPreviewWidth);
        var maxWidth = Mathf.Min(MaxRightPanelWidth, maxByWindow);
        return Mathf.Clamp(width <= 0f ? DefaultRightPanelWidth : width, MinRightPanelWidth, maxWidth);
    }

    public static ReanimatorWindowLayout Calculate(Rect windowRect, Rect tabsRect, Rect toolbarRect, float rightPanelWidth)
    {
        var contentTop = Mathf.Max(tabsRect.yMax, toolbarRect.yMax);
        if (contentTop <= 0f)
            contentTop = (EditorGUIUtility.singleLineHeight + 6f) * 2f;

        var contentY = contentTop + TopPadding + ContentPadding;
        var contentHeight = Mathf.Max(0f, windowRect.height - contentY);
        var contentRect = new Rect(0f, contentY, windowRect.width, contentHeight);
        var clampedRightWidth = ClampRightPanelWidth(rightPanelWidth, contentRect.width);
        var previewWidth = Mathf.Max(0f, contentRect.width - LeftPanelWidth - clampedRightWidth);
        var rightPanelX = contentRect.width - clampedRightWidth;

        return new ReanimatorWindowLayout
        {
            ContentRect = contentRect,
            LeftPanelRect = new Rect(0f, 0f, LeftPanelWidth, contentRect.height),
            PreviewRect = new Rect(LeftPanelWidth, 0f, previewWidth, contentRect.height),
            RightPanelRect = new Rect(rightPanelX, 0f, clampedRightWidth, contentRect.height),
            RightPanelSplitterRect = new Rect(rightPanelX - RightPanelSplitterWidth * 0.5f, 0f, RightPanelSplitterWidth, contentRect.height)
        };
    }
}
#endif
