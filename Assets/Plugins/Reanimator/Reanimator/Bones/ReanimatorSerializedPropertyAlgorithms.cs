#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

internal static class ReanimatorSerializedPropertyAlgorithms
{
    public static Vector3 ReadVector3(SerializedProperty property)
    {
        if (property == null)
            return Vector3.zero;

        if (property.propertyType == SerializedPropertyType.Vector3)
            return property.vector3Value;
        if (property.propertyType == SerializedPropertyType.Vector2)
            return property.vector2Value;

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        var z = property.FindPropertyRelative("z");
        if (!IsWritableNumericProperty(x) || !IsWritableNumericProperty(y))
            return Vector3.zero;

        return new Vector3(
            ReadNumericProperty(x),
            ReadNumericProperty(y),
            IsWritableNumericProperty(z) ? ReadNumericProperty(z) : 0f);
    }

    public static bool WriteFloatIfChanged(SerializedProperty property, float value)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Float)
        {
            if (Mathf.Abs(property.floatValue - value) <= 0.0001f)
                return false;

            property.floatValue = value;
            return true;
        }

        if (property.propertyType == SerializedPropertyType.Integer)
        {
            var intValue = Mathf.RoundToInt(value);
            if (property.intValue == intValue)
                return false;

            property.intValue = intValue;
            return true;
        }

        return false;
    }

    public static bool WriteStringIfChanged(SerializedProperty property, string value)
    {
        if (property == null || property.propertyType != SerializedPropertyType.String)
            return false;

        value ??= string.Empty;
        if (string.Equals(property.stringValue, value, StringComparison.Ordinal))
            return false;

        property.stringValue = value;
        return true;
    }

    public static bool WriteVector2IfChanged(SerializedProperty property, Vector2 value)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Vector2)
        {
            if ((property.vector2Value - value).sqrMagnitude <= 1e-8f)
                return false;
            property.vector2Value = value;
            return true;
        }

        if (property.propertyType == SerializedPropertyType.Vector3)
        {
            var current = property.vector3Value;
            if (Mathf.Abs(current.x - value.x) <= 0.0001f && Mathf.Abs(current.y - value.y) <= 0.0001f)
                return false;
            property.vector3Value = new Vector3(value.x, value.y, current.z);
            return true;
        }

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        if (!IsWritableNumericProperty(x) || !IsWritableNumericProperty(y))
            return false;
        if (Mathf.Abs(ReadNumericProperty(x) - value.x) <= 0.0001f && Mathf.Abs(ReadNumericProperty(y) - value.y) <= 0.0001f)
            return false;

        WriteNumericProperty(x, value.x);
        WriteNumericProperty(y, value.y);
        return true;
    }

    public static bool WriteVector3IfChanged(SerializedProperty property, Vector3 value)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Vector3)
        {
            if ((property.vector3Value - value).sqrMagnitude <= 1e-8f)
                return false;
            property.vector3Value = value;
            return true;
        }

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        var z = property.FindPropertyRelative("z");
        if (!IsWritableNumericProperty(x) || !IsWritableNumericProperty(y) || !IsWritableNumericProperty(z))
            return false;
        if (Mathf.Abs(ReadNumericProperty(x) - value.x) <= 0.0001f &&
            Mathf.Abs(ReadNumericProperty(y) - value.y) <= 0.0001f &&
            Mathf.Abs(ReadNumericProperty(z) - value.z) <= 0.0001f)
        {
            return false;
        }

        WriteNumericProperty(x, value.x);
        WriteNumericProperty(y, value.y);
        WriteNumericProperty(z, value.z);
        return true;
    }

    public static bool WriteQuaternionIfChanged(SerializedProperty property, Quaternion value)
    {
        if (property == null)
            return false;

        if (property.propertyType == SerializedPropertyType.Quaternion)
        {
            var current = property.quaternionValue;
            if (ReanimatorBoneMetaAlgorithms.AreQuaternionsApproximatelyEqual(current, value))
                return false;
            property.quaternionValue = value;
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

    public static bool TryReadColorProperty(SerializedProperty colorProp, Color fallback, out Color color)
    {
        color = fallback;
        if (colorProp == null)
            return false;

        if (colorProp.propertyType == SerializedPropertyType.Color)
        {
            color = colorProp.colorValue;
            return true;
        }

        var r = colorProp.FindPropertyRelative("r");
        var g = colorProp.FindPropertyRelative("g");
        var b = colorProp.FindPropertyRelative("b");
        var a = colorProp.FindPropertyRelative("a");
        if (r == null || g == null || b == null)
            return false;

        color = new Color(
            r.floatValue,
            g.floatValue,
            b.floatValue,
            a != null ? a.floatValue : fallback.a);
        return true;
    }

    public static void WriteColorProperty(SerializedProperty colorProp, Color color)
    {
        if (colorProp == null)
            return;

        if (colorProp.propertyType == SerializedPropertyType.Color)
        {
            colorProp.colorValue = color;
            return;
        }

        var r = colorProp.FindPropertyRelative("r");
        var g = colorProp.FindPropertyRelative("g");
        var b = colorProp.FindPropertyRelative("b");
        var a = colorProp.FindPropertyRelative("a");
        if (r == null || g == null || b == null)
            return;

        r.floatValue = color.r;
        g.floatValue = color.g;
        b.floatValue = color.b;
        if (a != null)
            a.floatValue = color.a;
    }

    private static bool IsWritableNumericProperty(SerializedProperty property)
    {
        return property != null &&
               (property.propertyType == SerializedPropertyType.Float ||
                property.propertyType == SerializedPropertyType.Integer);
    }

    private static float ReadNumericProperty(SerializedProperty property)
    {
        return property.propertyType == SerializedPropertyType.Integer
            ? property.intValue
            : property.floatValue;
    }

    private static void WriteNumericProperty(SerializedProperty property, float value)
    {
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else
            property.floatValue = value;
    }
}
#endif
