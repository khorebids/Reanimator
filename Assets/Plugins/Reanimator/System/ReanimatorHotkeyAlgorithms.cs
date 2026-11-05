#if UNITY_EDITOR
using UnityEngine;

internal static class ReanimatorHotkeyAlgorithms
{
    private const float PreviewCameraPanStep = 16f;

    public static bool ShouldHandleToolHotkeyEvent(
        Event e,
        bool spriteMode,
        bool editingTextField,
        int hotControl,
        ReanimatorToolMode activeMode)
    {
        if (e == null || e.type != EventType.KeyDown)
            return false;
        if (spriteMode)
            return false;
        if (e.alt || e.control || e.command || e.shift)
            return false;
        if (editingTextField)
            return false;
        if (hotControl != 0)
        {
            var cancelsActiveLimbs =
                ReanimatorToolModeAlgorithms.NormalizeToolMode(activeMode) == ReanimatorToolMode.Limbs &&
                TryGetToolHotkeyMode(e, out var targetMode) &&
                targetMode == ReanimatorToolMode.PreviewPose;
            if (!cancelsActiveLimbs)
                return false;
        }

        return true;
    }

    public static bool ShouldHandlePreviewCameraPanEvent(Event e, bool editingTextField, int hotControl)
    {
        if (e == null || e.type != EventType.KeyDown)
            return false;
        if (e.alt || e.control || e.command)
            return false;
        if (hotControl != 0 || editingTextField)
            return false;

        return TryGetPreviewCameraPanDelta(e.keyCode, e.character, e.shift, out _);
    }

    public static bool TryGetPreviewCameraPanDelta(Event e, out Vector2 panDelta)
    {
        panDelta = Vector2.zero;
        return e != null && TryGetPreviewCameraPanDelta(e.keyCode, e.character, e.shift, out panDelta);
    }

    public static bool TryGetPreviewCameraPanDelta(KeyCode keyCode, char character, bool fast, out Vector2 panDelta)
    {
        panDelta = Vector2.zero;
        var step = PreviewCameraPanStep * (fast ? 3f : 1f);
        switch (keyCode)
        {
            case KeyCode.W:
                panDelta = new Vector2(0f, step);
                return true;
            case KeyCode.A:
                panDelta = new Vector2(step, 0f);
                return true;
            case KeyCode.S:
                panDelta = new Vector2(0f, -step);
                return true;
            case KeyCode.D:
                panDelta = new Vector2(-step, 0f);
                return true;
        }

        switch (char.ToLowerInvariant(character))
        {
            case 'w':
            case '\u0446':
                panDelta = new Vector2(0f, step);
                return true;
            case 'a':
            case '\u0444':
                panDelta = new Vector2(step, 0f);
                return true;
            case 's':
            case '\u044b':
                panDelta = new Vector2(0f, -step);
                return true;
            case 'd':
            case '\u0432':
                panDelta = new Vector2(-step, 0f);
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetToolHotkeyMode(Event e, out ReanimatorToolMode mode)
    {
        mode = ReanimatorToolMode.NoSettings;
        return e != null && TryGetToolHotkeyMode(e.keyCode, e.character, out mode);
    }

    public static bool TryGetToolHotkeyMode(KeyCode keyCode, char character, out ReanimatorToolMode mode)
    {
        mode = ReanimatorToolMode.NoSettings;
        switch (keyCode)
        {
            case KeyCode.Q:
                mode = ReanimatorToolMode.PreviewPose;
                return true;
            case KeyCode.B:
                mode = ReanimatorToolMode.WeightBrush;
                return true;
            case KeyCode.R:
                mode = ReanimatorToolMode.Projector;
                return true;
            case KeyCode.C:
                mode = ReanimatorToolMode.WeightSlider;
                return true;
            case KeyCode.V:
                mode = ReanimatorToolMode.EditGeometry;
                return true;
            case KeyCode.X:
                mode = ReanimatorToolMode.Curve;
                return true;
        }

        switch (char.ToLowerInvariant(character))
        {
            case 'q':
            case '\u0439':
                mode = ReanimatorToolMode.PreviewPose;
                return true;
            case 'b':
            case '\u0438':
                mode = ReanimatorToolMode.WeightBrush;
                return true;
            case 'r':
            case '\u043a':
                mode = ReanimatorToolMode.Projector;
                return true;
            case 'c':
            case '\u0441':
                mode = ReanimatorToolMode.WeightSlider;
                return true;
            case 'v':
            case '\u043c':
                mode = ReanimatorToolMode.EditGeometry;
                return true;
            case 'x':
            case '\u0447':
                mode = ReanimatorToolMode.Curve;
                return true;
            default:
                return false;
        }
    }

    public static bool IsRestorePoseHotkey(Event e)
    {
        return e != null && IsRestorePoseHotkey(e.keyCode, e.character);
    }

    public static bool IsRestorePoseHotkey(KeyCode keyCode, char character)
    {
        if (keyCode == KeyCode.E)
            return true;

        var normalized = char.ToLowerInvariant(character);
        return normalized == 'e' || normalized == '\u0443';
    }

    public static bool IsAutoGeometryHotkey(Event e)
    {
        return e != null && IsAutoGeometryHotkey(e.keyCode, e.character);
    }

    public static bool IsAutoGeometryHotkey(KeyCode keyCode, char character)
    {
        if (keyCode == KeyCode.G)
            return true;

        var normalized = char.ToLowerInvariant(character);
        return normalized == 'g' || normalized == '\u043f';
    }

    public static bool IsConfirmedDoublePress(
        bool sameToolMode,
        double lastPressTime,
        double currentTime,
        double doublePressSeconds)
    {
        return sameToolMode &&
               lastPressTime > 0d &&
               currentTime >= lastPressTime &&
               currentTime - lastPressTime <= doublePressSeconds;
    }
}
#endif
