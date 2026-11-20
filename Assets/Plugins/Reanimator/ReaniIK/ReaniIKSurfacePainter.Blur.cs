#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKSurfacePainter
{
    private static Color MultiplyRedness(Color source, Color tint, float alpha)
    {
        float a = Mathf.Clamp01(alpha);
        return new Color(source.r * Mathf.Lerp(1, Mathf.Clamp01(tint.r), a),
            source.g * Mathf.Lerp(1, Mathf.Clamp01(tint.g), a),
            source.b * Mathf.Lerp(1, Mathf.Clamp01(tint.b), a), source.a);
    }
    private static Color[] BlurAlpha(Color[] source, int width, int height, int radius = 4)
    {
        if (source == null || source.Length != width * height) throw new ArgumentException("Mask dimensions do not match.");
        radius = Mathf.Clamp(radius, 1, 32);
        float sigma = Mathf.Max(.5f, radius * .5f), total = 0;
        var kernel = new float[radius * 2 + 1];
        for (int i = -radius; i <= radius; i++) { float w = Mathf.Exp(-(i * i) / (2 * sigma * sigma)); kernel[i + radius] = w; total += w; }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= total;
        var temporary = new float[source.Length]; var result = (Color[])source.Clone();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            float value = 0;
            for (int i = -radius; i <= radius; i++) value += source[y * width + Mathf.Clamp(x + i, 0, width - 1)].a * kernel[i + radius];
            temporary[y * width + x] = value;
        }
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            float value = 0;
            for (int i = -radius; i <= radius; i++) value += temporary[Mathf.Clamp(y + i, 0, height - 1) * width + x] * kernel[i + radius];
            result[y * width + x].a = Mathf.Clamp01(value);
        }
        return result;
    }
    private void BlurRednessMasks()
    {
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Blur redness masks");
        if (wholeBody)
        {
            if (!EnsureBodyCanvas()) return;
            foreach (var s in bodySurfaces)
            {
                if (s == hiddenBodySurface || !s.entry.effectsEnabled) continue;
                bool painted = false; foreach (var pixel in s.mask) if (pixel.a > 0) { painted = true; break; }
                if (!painted) continue;
                Undo.RegisterCompleteObjectUndo(s.entry.normalAndBlush, "Blur redness alpha");
                s.mask = BlurAlpha(s.mask, s.width, s.height);
                s.entry.normalAndBlush.SetPixels(s.mask); s.entry.normalAndBlush.Apply(false); s.dirty = true;
                if (s.entry == entry) { pixels = s.mask; UpdateOverlay(); }
                UpdateBodyOverlay(s);
            }
            SaveBodyCanvas();
        }
        else if (entry != null && entry.effectsEnabled && pixels != null)
        {
            Undo.RegisterCompleteObjectUndo(entry.normalAndBlush, "Blur redness alpha");
            pixels = BlurAlpha(pixels, overlay.width, overlay.height);
            entry.normalAndBlush.SetPixels(pixels); entry.normalAndBlush.Apply(false); Save(); UpdateOverlay();
        }
        Undo.CollapseUndoOperations(group);
        EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); Repaint();
    }
}
#endif
