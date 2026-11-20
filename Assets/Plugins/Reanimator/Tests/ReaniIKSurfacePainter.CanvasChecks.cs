#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKSurfacePainter
{
    public static string VerifyBodyCanvasRouting()
    {
        var painter = CreateInstance<ReaniIKSurfacePainter>();
        painter.hideFlags = HideFlags.HideAndDontSave;
        var textures = new System.Collections.Generic.List<Texture2D>();
        var spriteObjects = new System.Collections.Generic.List<Sprite>();
        try
        {
            painter.bodyDocumentSize = new Vector2(100, 100);
            BodySurface Make(string name, Rect rect, int order, bool hole)
            {
                var mask = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
                var image = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
                var overlay = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
                textures.Add(mask); textures.Add(image); textures.Add(overlay);
                var opacity = Enumerable.Repeat(Color.white, 1024).ToArray();
                if (hole) for (int y = 8; y < 24; y++) for (int x = 8; x < 24; x++) opacity[y * 32 + x].a = 0;
                image.SetPixels(opacity); image.Apply();
                var sprite = Sprite.Create(image, new Rect(0, 0, 32, 32), Vector2.one * .5f); sprite.name = name; spriteObjects.Add(sprite);
                var colors = Enumerable.Repeat(new Color(.2f, .7f, .9f, 0), 1024).ToArray(); mask.SetPixels(colors); mask.Apply();
                return new BodySurface { sprite = sprite, entry = new ReaniIKSurfaceMaps.Entry { sprite = sprite, normalAndBlush = mask },
                    image = image, overlay = overlay, rect = rect, order = order, mask = colors, opacity = opacity,
                    width = 32, height = 32, alphaWidth = 32, alphaHeight = 32 };
            }
            var body = Make("body", new Rect(0, 0, 100, 100), 0, false);
            var arm = Make("arm", new Rect(25, 0, 50, 100), 1, true);
            painter.bodySurfaces.Add(body); painter.bodySurfaces.Add(arm);
            if (painter.BodySpriteAt(new Vector2(30, 50)) != arm.sprite || painter.BodySpriteAt(new Vector2(50, 50)) != body.sprite)
                throw new InvalidOperationException("Visible-layer selection ignored sorting or transparent holes.");
            painter.StrokeBody(new Vector2(50, 50), 40, false);
            if (!body.mask.Any(c => c.a > 0) || !arm.mask.Any(c => c.a > 0)) throw new InvalidOperationException("A shared brush did not paint both visible surfaces.");
            int coveredPixel = 16 * 32 + 9;
            if (body.mask[coveredPixel].a != 0) throw new InvalidOperationException("Brush painted the body hidden beneath the arm.");
            painter.ToggleBodyOccluder(new Vector2(30, 50));
            if (painter.BodySpriteAt(new Vector2(30, 50)) != body.sprite)
                throw new InvalidOperationException("Hiding the occluder did not expose the body to painting.");
            var hiddenMask = arm.mask.ToArray();
            painter.StrokeBody(new Vector2(30, 50), 15, false);
            if (body.mask[coveredPixel].a <= 0 || !hiddenMask.SequenceEqual(arm.mask))
                throw new InvalidOperationException("Painting through a hidden sprite changed its mask or missed the body.");
            painter.ToggleBodyOccluder(new Vector2(-100, -100));
            if (painter.BodySpriteAt(new Vector2(30, 50)) != arm.sprite)
                throw new InvalidOperationException("Shift-right-click elsewhere did not restore the hidden sprite.");
            arm.entry.effectsEnabled = false;
            if (painter.BodySpriteAt(new Vector2(30, 50)) != body.sprite)
                throw new InvalidOperationException("Unchecked sprite still occludes checked sprites.");
            var excludedBefore = arm.mask.ToArray();
            painter.StrokeBody(new Vector2(30, 50), 10, true);
            if (!excludedBefore.SequenceEqual(arm.mask)) throw new InvalidOperationException("Excluded sprite was painted.");
            painter.showNormals = true;
            painter.UpdateBodyOverlay(arm);
            if (arm.overlay.GetPixel(1,1) != Color.white) throw new InvalidOperationException("Excluded sprite displayed normals instead of source colors.");
            arm.entry.effectsEnabled = true;
            painter.UpdateBodyOverlay(arm);
            var normalPreview = arm.overlay.GetPixel(1,1);
            var expectedNormal = ReaniIKSurfaceMaps.DecodeNormal(new Color(.2f,.7f,.9f,0),1);
            if (Mathf.Abs(normalPreview.r-(expectedNormal.x*.5f+.5f))>.01f || Mathf.Abs(normalPreview.g-(expectedNormal.y*.5f+.5f))>.01f) throw new InvalidOperationException("Enabled sprite failed normal preview.");
            painter.showNormals = false;
            if (body.mask.Concat(arm.mask).Any(c => c.r != .2f || c.g != .7f || c.b != .9f)) throw new InvalidOperationException("Alpha painting changed RGB normals.");
            var viewport = new Rect(0, 0, 800, 600); painter.bodyNavigator.PanBy(new Vector2(137, -83));
            var point = new Vector2(37, 71);
            if (Vector2.Distance(point, painter.ViewToBody(viewport, painter.BodyToView(viewport, point))) > .001f)
                throw new InvalidOperationException("Pan/zoom coordinate mapping drifted from the paint hit test.");
            if (!StartsBrush(new Event { type = EventType.MouseDown, button = 0 }) || !StartsBrush(new Event { type = EventType.MouseDown, button = 1 }) ||
                StartsBrush(new Event { type = EventType.MouseDown, button = 0, shift = true }) || StartsBrush(new Event { type = EventType.MouseDown, button = 0, alt = true }))
                throw new InvalidOperationException("Paint/erase buttons conflict with radius adjustment or navigation.");
            painter.HandleBrushRadius(new Event { type = EventType.KeyDown, keyCode = KeyCode.LeftShift, shift = true, mousePosition = new Vector2(100, 100) }, viewport, 1000);
            float oldRadius = painter.radius;
            var move = new Event { type = EventType.MouseMove, shift = true, mousePosition = new Vector2(160, 100) };
            painter.HandleBrushRadius(move, viewport, 1000);
            if (painter.radius != oldRadius) throw new InvalidOperationException("Mouse movement changed the brush radius.");
            var wheel = new Event { type = EventType.ScrollWheel, shift = true, mousePosition = new Vector2(160, 100), delta = new Vector2(0, -3) };
            if (!painter.HandleBrushRadius(wheel, viewport, 1000) || painter.radius <= oldRadius || wheel.type != EventType.Used)
                throw new InvalidOperationException("Shift+wheel failed to resize the brush and consume canvas zoom.");
            oldRadius = painter.radius;
            var horizontalWheel = new Event { type = EventType.ScrollWheel, shift = true, mousePosition = new Vector2(160, 100), delta = new Vector2(-3, 0) };
            if (!painter.HandleBrushRadius(horizontalWheel, viewport, 1000) || painter.radius <= oldRadius || horizontalWheel.type != EventType.Used)
                throw new InvalidOperationException("Shift+wheel with horizontal OS delta did not change radius.");
            oldRadius = painter.radius;
            var zoomWheel = new Event { type = EventType.ScrollWheel, mousePosition = new Vector2(160, 100), delta = new Vector2(0, -3) };
            if (painter.HandleBrushRadius(zoomWheel, viewport, 1000) || painter.radius != oldRadius || zoomWheel.type == EventType.Used)
                throw new InvalidOperationException("Unmodified wheel was stolen from canvas navigation.");
            painter.HandleBrushRadius(new Event { type = EventType.KeyUp, keyCode = KeyCode.LeftShift }, viewport, 1000);
            if (painter.brushResizing) throw new InvalidOperationException("Releasing Shift did not stop radius adjustment.");
            float beforeErase = body.mask.Sum(c => c.a) + arm.mask.Sum(c => c.a);
            painter.StrokeBody(new Vector2(50, 50), 40, true);
            if (body.mask.Sum(c => c.a) + arm.mask.Sum(c => c.a) >= beforeErase)
                throw new InvalidOperationException("Erase brush did not remove painted alpha.");
            var impulse = Enumerable.Repeat(new Color(.2f, .7f, .9f, 0), 81).ToArray(); impulse[40].a = 1;
            var blurred = BlurAlpha(impulse, 9, 9, 2);
            if (blurred[40].a >= 1 || blurred[40].a <= 0 || blurred[41].a <= 0 || blurred[0].a != 0 || impulse[40].a != 1)
                throw new InvalidOperationException("Alpha blur failed to soften the mask independently of its source.");
            for (int i = 0; i < blurred.Length; i++)
                if (blurred[i].r != impulse[i].r || blurred[i].g != impulse[i].g || blurred[i].b != impulse[i].b)
                    throw new InvalidOperationException("Blurring redness changed RGB surface normals.");
            var source = new Color(.8f, .6f, .4f, .3f); var tint = new Color(.9f, .2f, .1f, 1);
            var multiplied = MultiplyRedness(source, tint, 1);
            if (MultiplyRedness(source, tint, 0) != source || Mathf.Abs(multiplied.r - .72f) > .00001f || Mathf.Abs(multiplied.g - .12f) > .00001f || multiplied.a != source.a)
                throw new InvalidOperationException("Multiply preview does not preserve surface color/opacity correctly.");
            return "PASS: visible-layer painting/erasing, brush input, hidden occluders, alpha-only blur preserves RGB, multiply preview preserves opacity.";
        }
        finally
        {
            foreach (var s in painter.bodySurfaces) s.dirty = false;
            foreach (var texture in textures) if (texture) Undo.ClearUndo(texture);
            DestroyImmediate(painter);
            foreach (var sprite in spriteObjects) if (sprite) DestroyImmediate(sprite);
            foreach (var texture in textures) if (texture) DestroyImmediate(texture);
        }
    }
}
#endif
