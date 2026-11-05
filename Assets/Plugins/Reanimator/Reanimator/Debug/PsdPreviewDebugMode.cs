#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

[Serializable]
public sealed class PsdPreviewDebugMode
{
    private const double DoublePressWindowSeconds = 1.25d;

    [SerializeField] private bool enabled = false;
    [NonSerialized] private double armUntilTimestamp;

    public enum PressResult
    {
        Armed,
        Toggled
    }

    public bool Enabled => enabled;

    public bool IsArmed => armUntilTimestamp > EditorApplication.timeSinceStartup;

    public PressResult PressToggle()
    {
        var now = EditorApplication.timeSinceStartup;
        if (now <= armUntilTimestamp)
        {
            enabled = !enabled;
            armUntilTimestamp = 0d;
            return PressResult.Toggled;
        }

        armUntilTimestamp = now + DoublePressWindowSeconds;
        return PressResult.Armed;
    }
}
#endif
