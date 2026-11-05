#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

[Serializable]
public class PreviewNavigator
{
    private const float MinimumAllowedZoom = 0f;
    private const float LegacyMaxZoomFloor = 120f;

    [SerializeField] private Vector2 pan;
    [SerializeField] private float zoom = 1f;
    [SerializeField] private float minZoom = 0.2f;
    [SerializeField] private float maxZoom = 120f;

    private bool isPanning;
    private int panControlId;
    [NonSerialized] private double autoCenterLastTime = -1d;

    public void Reset()
    {
        pan = Vector2.zero;
        zoom = 1f;
        EnsureZoomLimits();
    }

    public bool HandleInput(
        Rect rect,
        bool allowPrimaryButtonPan = false,
        bool zoomAroundMouse = true)
    {
        EnsureZoomLimits();
        var e = Event.current;
        switch (e.type)
        {
            case EventType.ScrollWheel:
                if (!rect.Contains(e.mousePosition)) return false;
                var oldZoom = zoom;
                var factor = Mathf.Exp(-e.delta.y * 0.05f);
                var nextZoom = zoom * factor;
                if (minZoom > 0f)
                    nextZoom = Mathf.Max(nextZoom, minZoom);
                if (maxZoom > 0f)
                    nextZoom = Mathf.Min(nextZoom, maxZoom);
                zoom = nextZoom;
                if (!Mathf.Approximately(oldZoom, zoom))
                {
                    if (zoomAroundMouse)
                    {
                        var center = rect.center;
                        var mouse = e.mousePosition;
                        var content = (mouse - center - pan) / oldZoom;
                        pan = mouse - center - content * zoom;
                    }
                }
                e.Use();
                return true;

            case EventType.MouseDown:
                if (!rect.Contains(e.mousePosition)) break;
                if (e.button == 2 || (e.button == 0 && (e.alt || allowPrimaryButtonPan)))
                {
                    isPanning = true;
                    panControlId = GUIUtility.GetControlID(FocusType.Passive);
                    GUIUtility.hotControl = panControlId;
                    e.Use();
                    return true;
                }
                break;

            case EventType.MouseDrag:
                if (!isPanning) break;
                pan += e.delta;
                e.Use();
                return true;

            case EventType.MouseUp:
                if (!isPanning) break;
                isPanning = false;
                if (GUIUtility.hotControl == panControlId)
                    GUIUtility.hotControl = 0;
                e.Use();
                return true;
        }

        return false;
    }

    public bool AutoCenterPan(float speed = 8f)
    {
        EnsureZoomLimits();
        var now = EditorApplication.timeSinceStartup;
        if (autoCenterLastTime < 0d)
        {
            autoCenterLastTime = now;
            return false;
        }

        var deltaTime = Mathf.Clamp((float)(now - autoCenterLastTime), 0f, 0.1f);
        autoCenterLastTime = now;

        if (isPanning || pan.sqrMagnitude <= 0.0001f)
        {
            if (pan.sqrMagnitude <= 0.0001f)
                pan = Vector2.zero;
            return false;
        }

        var t = 1f - Mathf.Exp(-Mathf.Max(0.01f, speed) * deltaTime);
        var nextPan = Vector2.Lerp(pan, Vector2.zero, t);
        if ((nextPan - pan).sqrMagnitude <= 0.000001f)
            nextPan = Vector2.zero;

        var changed = (nextPan - pan).sqrMagnitude > 0f;
        pan = nextPan;
        return changed;
    }

    public void PanBy(Vector2 viewDelta)
    {
        pan += viewDelta;
        autoCenterLastTime = -1d;
    }

    private void EnsureZoomLimits()
    {
        if (minZoom < MinimumAllowedZoom)
            minZoom = MinimumAllowedZoom;

        // Migrate old serialized states that still carry the legacy cap (e.g. 6x).
        if (maxZoom > 0f && maxZoom < LegacyMaxZoomFloor)
            maxZoom = LegacyMaxZoomFloor;

        if (zoom < minZoom)
            zoom = minZoom;
        if (maxZoom > 0f && zoom > maxZoom)
            zoom = maxZoom;
    }

    public Vector2 ToView(Rect rect, Vector2 point)
    {
        var center = rect.center;
        return center + pan + (point - center) * zoom;
    }

    public Vector2 FromView(Rect rect, Vector2 point)
    {
        if (Mathf.Abs(zoom) <= 0.0001f)
            return point;
        var center = rect.center;
        return center + (point - center - pan) / zoom;
    }

    public Rect ToView(Rect rect, Rect source)
    {
        var min = ToView(rect, source.min);
        var max = ToView(rect, source.max);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    public void CenterOn(Rect rect, Vector2 point)
    {
        EnsureZoomLimits();
        pan = (rect.center - point) * zoom;
        autoCenterLastTime = -1d;
    }

    public void Draw(Rect rect, Action<Rect> drawContent)
    {
        GUI.BeginGroup(rect);
        var localRect = new Rect(0f, 0f, rect.width, rect.height);
        var previous = GUI.matrix;
        var matrix = Matrix4x4.TRS(localRect.center + pan, Quaternion.identity, new Vector3(zoom, zoom, 1f))
                     * Matrix4x4.TRS(-localRect.center, Quaternion.identity, Vector3.one);
        GUI.matrix = previous * matrix;
        drawContent?.Invoke(localRect);
        GUI.matrix = previous;
        GUI.EndGroup();
    }
}

internal static class BoneShapeDrawer
{
    private const float MinBaseRadius = 4f;
    private const float MinTipRadius = 2f;

    public static float GetBaseRadius(float thickness)
    {
        return Mathf.Max(MinBaseRadius * 1.3f, thickness * 2.86f);
    }

    public static float GetTipRadius(float thickness)
    {
        return Mathf.Max(MinTipRadius * 0.5f, thickness * 0.525f);
    }

    public static void Draw(Vector2 start, Vector2 end, float thickness)
    {
        var dir = end - start;
        var length = dir.magnitude;
        var baseDotRadius = GetBaseRadius(thickness);
        var tipDotRadius = GetTipRadius(thickness);
        var bodyWidth = Mathf.Max(1.2f, thickness * 0.8f);

        if (length <= baseDotRadius * 2f)
        {
            Handles.DrawSolidDisc(start, Vector3.forward, baseDotRadius);
            return;
        }

        if (TryGetTangents(start, baseDotRadius, end, tipDotRadius, out var aTop, out var aBottom, out var bTop, out var bBottom))
        {
            Handles.DrawAAConvexPolygon(aTop, bTop, bBottom, aBottom);
            Handles.DrawAAPolyLine(bodyWidth, aTop, bTop);
            Handles.DrawAAPolyLine(bodyWidth, aBottom, bBottom);
        }
        else
        {
            Handles.DrawAAPolyLine(bodyWidth, start, end);
        }

        // Base pivot dot.
        Handles.DrawSolidDisc(start, Vector3.forward, baseDotRadius);
        // End direction dot.
        Handles.DrawSolidDisc(end, Vector3.forward, tipDotRadius);
    }

    public static bool IsPointOverBase(Vector2 point, Vector2 baseCenter, float thickness, float extraPixels = 2f)
    {
        var r = GetBaseRadius(thickness) + Mathf.Max(0f, extraPixels);
        return (point - baseCenter).sqrMagnitude <= r * r;
    }

    public static bool IsPointOverBodyOrTip(Vector2 point, Vector2 start, Vector2 end, float thickness, float extraPixels = 3f)
    {
        var baseR = GetBaseRadius(thickness);
        var tipR = GetTipRadius(thickness);
        var extra = Mathf.Max(0f, extraPixels);

        if (IsPointOverTip(point, end, thickness, extra))
            return true;

        if (TryGetTangents(start, baseR, end, tipR, out var aTop, out var aBottom, out var bTop, out var bBottom))
        {
            if (DistancePointToSegment(point, aTop, bTop) <= extra + thickness)
                return true;
            if (DistancePointToSegment(point, aBottom, bBottom) <= extra + thickness)
                return true;
            return false;
        }

        return DistancePointToSegment(point, start, end) <= extra + thickness;
    }

    public static bool IsPointOverTip(Vector2 point, Vector2 tipCenter, float thickness, float extraPixels = 3f)
    {
        var r = GetTipRadius(thickness) + Mathf.Max(0f, extraPixels);
        return (point - tipCenter).sqrMagnitude <= r * r;
    }

    private static bool TryGetTangents(
        Vector2 c1,
        float r1,
        Vector2 c2,
        float r2,
        out Vector2 c1Top,
        out Vector2 c1Bottom,
        out Vector2 c2Top,
        out Vector2 c2Bottom)
    {
        c1Top = c1Bottom = c2Top = c2Bottom = Vector2.zero;
        var delta = c2 - c1;
        var d = delta.magnitude;
        if (d <= 0.0001f)
            return false;

        var k = Mathf.Clamp((r1 - r2) / d, -1f, 1f);
        var hSq = 1f - k * k;
        if (hSq < 0.0001f)
            return false;

        var h = Mathf.Sqrt(hSq);
        var u = delta / d;
        var v = new Vector2(-u.y, u.x);

        var nTop = u * k + v * h;
        var nBottom = u * k - v * h;

        c1Top = c1 + nTop * r1;
        c1Bottom = c1 + nBottom * r1;
        c2Top = c2 + nTop * r2;
        c2Bottom = c2 + nBottom * r2;
        return true;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.sqrMagnitude > 0f ? Vector2.Dot(p - a, ab) / ab.sqrMagnitude : 0f;
        t = Mathf.Clamp01(t);
        var proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }
}
#endif
