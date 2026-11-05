#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    private bool HandleVisibilityLabelClick(VisibilityElement element, Rect labelHitRect, float value)
    {
        var evt = Event.current;
        if (evt == null ||
            evt.type != EventType.MouseDown ||
            evt.button != 0 ||
            !labelHitRect.Contains(evt.mousePosition))
            return false;

        if (evt.clickCount == 2)
        {
            if (IsVisibilityElementIsolated(element))
                RestoreAllVisibilityElements();
            else
                IsolateVisibilityElement(element);

            evt.Use();
            GUI.changed = true;
            Repaint();
            return true;
        }

        if (evt.clickCount == 1 && !IsVisibilityValueEnabled(value))
        {
            SetVisibilityElementValue(element, GetVisibilityElementRestoreValue(element));
            evt.Use();
            GUI.changed = true;
            Repaint();
            return true;
        }

        return false;
    }

    private void IsolateVisibilityElement(VisibilityElement element)
    {
        RememberAllVisibilityElementValues();
        spriteVisibilityValue = element == VisibilityElement.Sprite ? GetVisibilityElementRestoreValue(VisibilityElement.Sprite) : 0f;
        boneVisibilityValue = element == VisibilityElement.Bone ? GetVisibilityElementRestoreValue(VisibilityElement.Bone) : 0f;
        vertexVisibilityValue = element == VisibilityElement.Vertex ? GetVisibilityElementRestoreValue(VisibilityElement.Vertex) : 0f;
        influenceVisibilityValue = element == VisibilityElement.Influence ? GetVisibilityElementRestoreValue(VisibilityElement.Influence) : 0f;
    }

    private void RestoreAllVisibilityElements()
    {
        spriteVisibilityValue = GetVisibilityElementRestoreValue(VisibilityElement.Sprite);
        boneVisibilityValue = GetVisibilityElementRestoreValue(VisibilityElement.Bone);
        vertexVisibilityValue = GetVisibilityElementRestoreValue(VisibilityElement.Vertex);
        influenceVisibilityValue = GetVisibilityElementRestoreValue(VisibilityElement.Influence);
    }

    private void RememberAllVisibilityElementValues()
    {
        RememberVisibilityElementValue(VisibilityElement.Sprite, spriteVisibilityValue);
        RememberVisibilityElementValue(VisibilityElement.Bone, boneVisibilityValue);
        RememberVisibilityElementValue(VisibilityElement.Vertex, vertexVisibilityValue);
        RememberVisibilityElementValue(VisibilityElement.Influence, influenceVisibilityValue);
    }

    private bool IsVisibilityElementIsolated(VisibilityElement element)
    {
        return IsVisibilityValueEnabled(GetVisibilityElementValue(element)) &&
               (element == VisibilityElement.Sprite || !IsVisibilityValueEnabled(spriteVisibilityValue)) &&
               (element == VisibilityElement.Bone || !IsVisibilityValueEnabled(boneVisibilityValue)) &&
               (element == VisibilityElement.Vertex || !IsVisibilityValueEnabled(vertexVisibilityValue)) &&
               (element == VisibilityElement.Influence || !IsVisibilityValueEnabled(influenceVisibilityValue));
    }

    private float GetVisibilityElementValue(VisibilityElement element)
    {
        switch (element)
        {
            case VisibilityElement.Sprite:
                return spriteVisibilityValue;
            case VisibilityElement.Bone:
                return boneVisibilityValue;
            case VisibilityElement.Vertex:
                return vertexVisibilityValue;
            case VisibilityElement.Influence:
                return influenceVisibilityValue;
            default:
                return 1f;
        }
    }

    private void SetVisibilityElementValue(VisibilityElement element, float value)
    {
        value = Mathf.Clamp01(value);
        switch (element)
        {
            case VisibilityElement.Sprite:
                spriteVisibilityValue = value;
                break;
            case VisibilityElement.Bone:
                boneVisibilityValue = value;
                break;
            case VisibilityElement.Vertex:
                vertexVisibilityValue = value;
                break;
            case VisibilityElement.Influence:
                influenceVisibilityValue = value;
                break;
        }

        RememberVisibilityElementValue(element, value);
    }

    private float GetVisibilityElementRestoreValue(VisibilityElement element)
    {
        var value = 1f;
        switch (element)
        {
            case VisibilityElement.Sprite:
                value = spriteVisibilityRestoreValue;
                break;
            case VisibilityElement.Bone:
                value = boneVisibilityRestoreValue;
                break;
            case VisibilityElement.Vertex:
                value = vertexVisibilityRestoreValue;
                break;
            case VisibilityElement.Influence:
                value = influenceVisibilityRestoreValue;
                break;
        }

        return IsVisibilityValueEnabled(value) ? Mathf.Clamp01(value) : 1f;
    }

    private void RememberVisibilityElementValue(VisibilityElement element, float value)
    {
        value = Mathf.Clamp01(value);
        if (!IsVisibilityValueEnabled(value))
            return;

        switch (element)
        {
            case VisibilityElement.Sprite:
                spriteVisibilityRestoreValue = value;
                break;
            case VisibilityElement.Bone:
                boneVisibilityRestoreValue = value;
                break;
            case VisibilityElement.Vertex:
                vertexVisibilityRestoreValue = value;
                break;
            case VisibilityElement.Influence:
                influenceVisibilityRestoreValue = value;
                break;
        }
    }

    private static bool IsVisibilityValueEnabled(float value)
    {
        return value > VisibilityEnabledEpsilon;
    }
}
#endif
