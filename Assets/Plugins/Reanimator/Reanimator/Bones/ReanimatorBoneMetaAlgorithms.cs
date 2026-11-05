#if UNITY_EDITOR
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

internal static class ReanimatorBoneMetaAlgorithms
{
    public static Vector2 ParseVector2FromInlineMap(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Vector2.zero;

        var mx = Regex.Match(line, @"x:\s*([-+0-9eE\.]+)");
        var my = Regex.Match(line, @"y:\s*([-+0-9eE\.]+)");
        if (!mx.Success || !my.Success)
            return Vector2.zero;

        var x = ParseFloatInvariant(mx.Groups[1].Value, 0f);
        var y = ParseFloatInvariant(my.Groups[1].Value, 0f);
        return new Vector2(x, y);
    }

    public static Vector3 ParseVector3FromInlineMap(string line)
    {
        var xy = ParseVector2FromInlineMap(line);
        if (string.IsNullOrEmpty(line))
            return xy;

        var mz = Regex.Match(line, @"z:\s*([-+0-9eE\.]+)");
        var z = mz.Success ? ParseFloatInvariant(mz.Groups[1].Value, 0f) : 0f;
        return new Vector3(xy.x, xy.y, z);
    }

    public static bool TryParseUnityPackedColor(string text, Color fallback, out Color color)
    {
        color = fallback;
        if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rgba))
            return false;

        var r = (byte)(rgba & 0xffu);
        var g = (byte)((rgba >> 8) & 0xffu);
        var b = (byte)((rgba >> 16) & 0xffu);
        var a = (byte)((rgba >> 24) & 0xffu);
        color = SanitizeColor(new Color32(r, g, b, a), fallback);
        return true;
    }

    public static bool AreQuaternionsApproximatelyEqual(Quaternion lhs, Quaternion rhs)
    {
        return Mathf.Abs(lhs.x - rhs.x) <= 0.0001f &&
               Mathf.Abs(lhs.y - rhs.y) <= 0.0001f &&
               Mathf.Abs(lhs.z - rhs.z) <= 0.0001f &&
               Mathf.Abs(lhs.w - rhs.w) <= 0.0001f;
    }

    public static bool AreColorsApproximatelyEqual(Color lhs, Color rhs)
    {
        return Mathf.Abs(lhs.r - rhs.r) <= 0.0001f &&
               Mathf.Abs(lhs.g - rhs.g) <= 0.0001f &&
               Mathf.Abs(lhs.b - rhs.b) <= 0.0001f &&
               Mathf.Abs(lhs.a - rhs.a) <= 0.0001f;
    }

    private static float ParseFloatInvariant(string text, float fallback)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        return fallback;
    }

    private static Color SanitizeColor(Color color, Color fallback)
    {
        if (color.a <= 0.0001f)
            color.a = fallback.a;
        return color;
    }
}
#endif
