#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    private const string ReanimateSuccessImagePath = "Assets/Plugins/Reanimator/Graphics/sucsess_tab.png";
    [NonSerialized] private Texture2D reanimateSuccessImage;
    [NonSerialized] private double reanimateSuccessUntil;
    [NonSerialized] private string reanimateSuccessElapsed;
    private GUIStyle reanimateSuccessTimerStyle;

    private void ShowReanimateSuccessNotification(int seconds)
    {
        RemoveNotification();
        reanimateSuccessElapsed = FormatReanimateElapsed(Mathf.Max(0, seconds));
        reanimateSuccessUntil = EditorApplication.timeSinceStartup + 7d;
        Repaint();
    }

    private Rect ReanimateSuccessRect()
    {
        const float aspect = 1541f / 887f;
        var width = Mathf.Min(800f, Mathf.Max(1f, position.width - 32f), Mathf.Max(1f, position.height - 32f) * aspect);
        var height = width / aspect;
        return new Rect((position.width - width) * .5f, (position.height - height) * .5f, width, height);
    }

    private void HandleReanimateSuccessNotificationInput(Event input)
    {
        if (reanimateSuccessUntil <= EditorApplication.timeSinceStartup) return;
        if ((input.type == EventType.MouseDown && ReanimateSuccessRect().Contains(input.mousePosition)) ||
            (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape))
        {
            reanimateSuccessUntil = 0;
            input.Use();
            Repaint();
        }
    }

    private void TickReanimateSuccessNotification()
    {
        if (reanimateSuccessUntil <= 0 || EditorApplication.timeSinceStartup < reanimateSuccessUntil) return;
        reanimateSuccessUntil = 0;
        Repaint();
    }

    private void DrawReanimateSuccessNotification()
    {
        if (reanimateSuccessUntil <= EditorApplication.timeSinceStartup || Event.current.type != EventType.Repaint) return;
        if (!reanimateSuccessImage)
            reanimateSuccessImage = AssetDatabase.LoadAssetAtPath<Texture2D>(ReanimateSuccessImagePath);
        if (!reanimateSuccessImage) return;
        var rect = ReanimateSuccessRect();
        var previous = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(rect, reanimateSuccessImage, ScaleMode.StretchToFill, true);
        reanimateSuccessTimerStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
        reanimateSuccessTimerStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(rect.width * 60f / 1541f));
        var timer = new Rect(rect.x, rect.y + rect.height * .875f, rect.width, rect.height * .09f);
        reanimateSuccessTimerStyle.normal.textColor = new Color(0, 0, 0, .85f);
        GUI.Label(new Rect(timer.x + 2, timer.y + 2, timer.width, timer.height), reanimateSuccessElapsed, reanimateSuccessTimerStyle);
        reanimateSuccessTimerStyle.normal.textColor = Color.white;
        GUI.Label(timer, reanimateSuccessElapsed, reanimateSuccessTimerStyle);
        GUI.color = previous;
    }
}
#endif
