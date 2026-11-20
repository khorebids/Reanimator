#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

internal sealed class ReanimatorSkeletronModule : ReanimatorToolModeModule, IReanimatorPreviewModule
{
    private sealed class Placement
    {
        public string assetPath;
        public bool armed;
        public bool extraArms, extraLegs;
        public int control;
        public Vector2 start, end;
        public int endpoint = -1;
        public Vector2[] points;
        public Vector2 shoulderGuide, guideDragStart;
        public readonly HashSet<PsdLayerPreviewRenderer.LayerInfo> hiddenLayers = new();
    }

    private static readonly ConditionalWeakTable<PsdLayerPreviewRenderer, Placement> Placements = new();
    [Serializable] private sealed class PlacementDraft
    { public Vector2[] points; public bool extraArms, extraLegs; public Vector2 shoulderGuide; }
    internal static string ExportDraft(Reanimator.SystemApi api)
    {
        var state=GetPlacement(api);
        return JsonUtility.ToJson(new PlacementDraft{points=state?.points,extraArms=state?.extraArms??false,extraLegs=state?.extraLegs??false,shoulderGuide=state?.shoulderGuide??Vector2.zero});
    }
    internal static void ImportDraft(Reanimator.SystemApi api,string json)
    {
        if(string.IsNullOrEmpty(json))return;
        var draft=JsonUtility.FromJson<PlacementDraft>(json);var state=GetPlacement(api);
        Cancel(state);state.points=draft.points!=null && draft.points.Length>0?draft.points:null;state.extraArms=draft.extraArms;state.extraLegs=draft.extraLegs;
        state.shoulderGuide=draft.shoulderGuide;api.PreviewRenderer.SkeletronShoulderGuideOffset=draft.shoulderGuide;
    }
    private static readonly Vector2[] Template =
    {
        new(.50f, .48f), new(.50f, .55f), new(.50f, .65f), new(.50f, .78f), new(.50f, .86f), new(.50f, 1f),
        new(.34f, .77f), new(.20f, .63f), new(.08f, .50f), new(0f, .44f),
        new(.66f, .77f), new(.80f, .63f), new(.92f, .50f), new(1f, .44f),
        new(.41f, .50f), new(.38f, .27f), new(.37f, .06f), new(.30f, 0f),
        new(.59f, .50f), new(.62f, .27f), new(.63f, .06f), new(.70f, 0f)
    };
    private static readonly (int first, int torsoParent, string[] names)[] Chains =
    {
        (0, -1, new[] { "hips", "belly", "spine", "neck", "head" }),
        (6, 3, new[] { "l_arm", "l_arm_1", "l_hand" }),
        (10, 3, new[] { "r_arm", "r_arm_1", "r_hand" }),
        (14, 0, new[] { "l_leg", "l_leg_1", "l_foot" }),
        (18, 0, new[] { "r_leg", "r_leg_1", "r_foot" }),
        (22, 3, new[] { "l_arm_2", "l_arm_3", "l_hand_2" }),
        (26, 3, new[] { "r_arm_2", "r_arm_3", "r_hand_2" }),
        (30, 0, new[] { "l_leg_2", "l_leg_3", "l_foot_2" }),
        (34, 0, new[] { "r_leg_2", "r_leg_3", "r_foot_2" })
    };

    public ReanimatorSkeletronModule()
        : base("bones.skeletron", "Bones", 20, 30, "Skeletron", ReanimatorToolMode.Skeletron)
    {
    }

    public bool AllowBoneInputAfterPreviewInput => false;
    public bool OverridesSelectedGeometryPreview => false;

    public override bool DrawSettings(Reanimator.SystemApi api, float availableHeight)
    {
        DrawPlacementSettings(api, true);
        return true;
    }

    internal void DrawPlacementSettings(Reanimator.SystemApi api, bool applyButton)
    {
        EditorGUILayout.LabelField("Skeletron", EditorStyles.boldLabel);
        var state = GetPlacement(api);
        using (new EditorGUI.DisabledScope(state == null || string.IsNullOrEmpty(state.assetPath)))
        {
            if (GUILayout.Button(state?.armed == true ? "Cancel Placement" : "Place"))
            {
                state.armed = !state.armed;
                api.Repaint();
            }
        }
        if (api?.PreviewRenderer != null)
        {
            var renderer = api.PreviewRenderer;
            renderer.SkeletronBellyParentRoot = EditorGUILayout.ToggleLeft(
                "Belly Parent: Root", renderer.SkeletronBellyParentRoot);
            int targets=renderer.SideAssAllInTargets;
            EditorGUI.BeginChangeCheck();
            bool left=EditorGUILayout.ToggleLeft("Side Ass — Left Leg",(targets&1)!=0);
            bool right=EditorGUILayout.ToggleLeft("Side Ass — Right Leg",(targets&2)!=0);
            if(EditorGUI.EndChangeCheck())renderer.SideAssAllInTargets=(left?1:0)|(right?2:0);
        }
        if (state != null)
        {
            EditorGUI.BeginChangeCheck();
            state.extraArms = EditorGUILayout.ToggleLeft("Additional Arms", state.extraArms);
            state.extraLegs = EditorGUILayout.ToggleLeft("Additional Legs", state.extraLegs);
            if(state.points!=null)
            {
                EditorGUILayout.LabelField("Gold line: drag either end to set the spine / shoulders boundary.",EditorStyles.wordWrappedMiniLabel);
                if(GUILayout.Button("Reset shoulder guide",EditorStyles.miniButton))
                {state.shoulderGuide=Vector2.zero;api.PreviewRenderer.SkeletronShoulderGuideOffset=Vector2.zero;api.Repaint();}
            }
            if (EditorGUI.EndChangeCheck())
                api.Repaint();
        }
        if (state?.armed == true)
            EditorGUILayout.HelpBox("Drag a rectangle on the canvas to size the skeleton.", MessageType.Info);
        if (!applyButton)
            return;
        using (new EditorGUI.DisabledScope(state?.points == null || state.armed || state.control != 0))
        {
            if (GUILayout.Button("Apply"))
            {
                var size = api.PreviewRenderer.DocumentSize;
                var specs = BuildBoneSpecs(state.points, new Rect(0f, 0f, size.x, size.y), state.extraArms, state.extraLegs);
                if (api.PreviewRenderer.TryApplySkeletron(specs, out var error))
                {
                    api.SetToolMode(ReanimatorToolMode.EditBone);
                    api.ShowNotification($"Created {specs.Count} bones");
                }
                else
                    api.ShowNotification(error);
                api.Repaint();
            }
        }
    }

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        var state = GetPlacement(api);
        var input = Event.current;
        if (state == null || input == null || context?.Coordinates == null)
            return;

        if (input.type == EventType.MouseDown && input.button == 1 &&
            context.LocalRect.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
        {
            var hit = api.PreviewRenderer.FindTopLayerAt(
                context.Coordinates.ViewToDocument(input.mousePosition), context.DrawList, state.hiddenLayers);
            if (hit == null)
                state.hiddenLayers.Clear();
            else
                state.hiddenLayers.Add(hit);
            input.Use();
            api.Repaint();
            return;
        }

        if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
        {
            if (!state.armed && state.control == 0)
                return;
            if (state.endpoint >= 0)
                MoveJoint(state.points, state.endpoint, state.start - state.end);
            if(state.endpoint < -1)
            {state.shoulderGuide=state.guideDragStart;api.PreviewRenderer.SkeletronShoulderGuideOffset=state.shoulderGuide;}
            Cancel(state);
        }
        else if (input.type == EventType.MouseDown && input.button == 0 && !input.alt &&
                 context.LocalRect.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
        {
            if (!state.armed)
            {
                state.endpoint = FindShoulderGuideEnd(state,context.Coordinates,input.mousePosition);
                if(state.endpoint==-1)state.endpoint = FindEndpoint(state.points, context.Coordinates, input.mousePosition, state.extraArms, state.extraLegs);
                if (state.endpoint == -1)
                    return;
                state.guideDragStart=state.shoulderGuide;
            }
            state.control = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = state.control;
            state.start = state.end = context.Coordinates.ViewToDocument(input.mousePosition);
        }
        else if (state.control != 0 && GUIUtility.hotControl == state.control)
        {
            if (input.type != EventType.MouseDrag && (input.type != EventType.MouseUp || input.button != 0))
                return;
            var previous = state.end;
            state.end = context.Coordinates.ViewToDocument(input.mousePosition);
            if(state.endpoint < -1)
            {
                var offset=(state.end-state.points[3])*(state.endpoint==-2?-1f:1f);
                if(offset.sqrMagnitude>.0001f)state.shoulderGuide=offset;
                api.PreviewRenderer.SkeletronShoulderGuideOffset=state.shoulderGuide;
                if(input.type==EventType.MouseUp)Cancel(state);
            }
            else if (state.endpoint >= 0)
            {
                MoveJoint(state.points, state.endpoint, state.end - previous);
                if (input.type == EventType.MouseUp)
                    Cancel(state);
            }
            else if (input.type == EventType.MouseUp)
            {
                var bounds = GetBounds(state.start, state.end);
                var viewSize = context.Coordinates.DocumentToView(state.end) - context.Coordinates.DocumentToView(state.start);
                if (Mathf.Abs(viewSize.x) >= 6f && Mathf.Abs(viewSize.y) >= 6f)
                {
                    state.points = BuildPoints(bounds);
                    state.shoulderGuide=Vector2.zero;api.PreviewRenderer.SkeletronShoulderGuideOffset=Vector2.zero;
                    Cancel(state);
                }
                else
                {
                    GUIUtility.hotControl = 0;
                    state.control = 0;
                }
            }
        }
        else
            return;
        input.Use();
        api.Repaint();
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (Event.current.type != EventType.Repaint || context?.Coordinates == null)
            return;
        var state = GetPlacement(api);
        if (state == null)
            return;
        var points = state.control != 0 && state.armed ? BuildPoints(GetBounds(state.start, state.end)) : state.points;
        if (points == null || points.Length<4)
            return;

        Handles.BeginGUI();
        var guide=ShoulderGuideOffset(points,state.shoulderGuide);
        var guideA=context.Coordinates.DocumentToView(points[3]-guide);
        var guideB=context.Coordinates.DocumentToView(points[3]+guide);
        using(new Handles.DrawingScope(new Color(1f,.78f,.18f)))
        {
            Handles.DrawAAPolyLine(3f,guideA,guideB);
            Handles.DrawSolidRectangleWithOutline(new Rect(guideA.x-5,guideA.y-5,10,10),new Color(1f,.78f,.18f),Color.black);
            Handles.DrawSolidRectangleWithOutline(new Rect(guideB.x-5,guideB.y-5,10,10),new Color(1f,.78f,.18f),Color.black);
            GUI.Label(new Rect(guideB.x+8,guideB.y-20,140,18),"Spine / shoulders",EditorStyles.whiteMiniLabel);
        }
        using (new Handles.DrawingScope(new Color(0.35f, 0.95f, 0.95f)))
        {
            var coordinates = context.Coordinates;
            foreach (var chain in Chains)
            {
                if (!IsChainEnabled(chain.first, state.extraArms, state.extraLegs))
                    continue;
                if(chain.first+chain.names.Length>=points.Length)continue;
                Handles.color = chain.first >= 22 ? new Color(0.25f, 0.58f, 1f) : new Color(0.35f, 0.95f, 0.95f);
                var line = new Vector3[chain.names.Length + 1];
                for (var i = 0; i < line.Length; i++)
                    line[i] = coordinates.DocumentToView(points[chain.first + i]);
                if (chain.torsoParent >= 0)
                    Handles.DrawDottedLine(coordinates.DocumentToView(points[chain.torsoParent]), line[0], 4f);
                Handles.DrawAAPolyLine(5f, line);
                foreach (var joint in line)
                    Handles.DrawSolidDisc(joint, Vector3.forward, 4f);
            }
        }
        Handles.EndGUI();
    }

    private static Placement GetPlacement(Reanimator.SystemApi api)
    {
        var renderer = api?.PreviewRenderer;
        if (renderer == null)
            return null;
        var state = Placements.GetValue(renderer, _ => new Placement());
        var path = api.ActiveAssetPath;
        if (!string.Equals(path, state.assetPath, StringComparison.OrdinalIgnoreCase))
        {
            Cancel(state);
            state.assetPath = path;
            state.points = null;
            state.shoulderGuide=Vector2.zero;renderer.SkeletronShoulderGuideOffset=Vector2.zero;
            state.hiddenLayers.Clear();
        }
        return state;
    }

    private static Vector2 ShoulderGuideOffset(Vector2[] points,Vector2 manual)
    {
        if(manual.sqrMagnitude>.0001f)return manual;
        if(points==null||points.Length<5)return Vector2.zero;
        var axis=points[4]-points[3];if(axis.sqrMagnitude<.0001f)axis=Vector2.up;
        var side=new Vector2(axis.y,-axis.x).normalized;
        float span=points.Length>10?Vector2.Distance(points[6],points[10])*.5f:0;
        return side*Mathf.Max(span,axis.magnitude);
    }
    private static int FindShoulderGuideEnd(Placement state,PsdCoordinateSpace coordinates,Vector2 mouse)
    {
        if(state.points==null||state.points.Length<4)return -1;
        var offset=ShoulderGuideOffset(state.points,state.shoulderGuide);
        if(Vector2.Distance(coordinates.DocumentToView(state.points[3]-offset),mouse)<=10f)return -2;
        if(Vector2.Distance(coordinates.DocumentToView(state.points[3]+offset),mouse)<=10f)return -3;
        return -1;
    }

    internal static void CancelPlacement(PsdLayerPreviewRenderer renderer)
    {
        if (renderer != null && Placements.TryGetValue(renderer, out var state))
        {
            state.hiddenLayers.Clear();
            Cancel(state);
        }
    }

    internal static ISet<PsdLayerPreviewRenderer.LayerInfo> GetHiddenLayers(Reanimator.SystemApi api) =>
        GetPlacement(api)?.hiddenLayers;

    internal static bool IsPlacementDragging(Reanimator.SystemApi api) => GetPlacement(api)?.control > 0;

    internal static bool HasPlacement(Reanimator.SystemApi api) => GetPlacement(api)?.points?.Length >= 6;

    internal static List<(string name, int parent, Vector2 start, Vector2 end)> CapturePlacement(
        Reanimator.SystemApi api)
    {
        var state = GetPlacement(api);
        if (state?.points == null || state.points.Length<6)
            return null;
        var size = api.PreviewRenderer.DocumentSize;
        return BuildBoneSpecs(state.points, new Rect(0f, 0f, size.x, size.y), state.extraArms, state.extraLegs);
    }

    internal static void ClearPlacement(PsdLayerPreviewRenderer renderer)
    {
        CancelPlacement(renderer);
        if (renderer != null)
            Placements.Remove(renderer);
    }

    private static void Cancel(Placement state)
    {
        if (state.control != 0 && GUIUtility.hotControl == state.control)
            GUIUtility.hotControl = 0;
        state.control = 0;
        state.armed = false;
        state.endpoint = -1;
    }

    private static Vector2[] BuildPoints(Rect bounds)
    {
        var points = new Vector2[38];
        for (var i = 0; i < Template.Length; i++)
            points[i] = bounds.min + Vector2.Scale(Template[i], bounds.size);
        // Duplicate the original limb poses with a proportional outward offset.
        for (var limb = 0; limb < 4; limb++)
        {
            var offset = Vector2.right * bounds.width * (limb % 2 == 0 ? -0.07f : 0.07f);
            for (var joint = 0; joint < 4; joint++)
                points[22 + limb * 4 + joint] = points[6 + limb * 4 + joint] + offset;
        }
        return points;
    }

    private static bool IsChainEnabled(int first, bool extraArms, bool extraLegs) =>
        first < 22 || (first < 30 ? extraArms : extraLegs);

    private static void MoveJoint(Vector2[] points, int joint, Vector2 delta)
    {
        foreach (var chain in Chains)
        {
            var end = chain.first + chain.names.Length + 1;
            if(end>points.Length)continue;
            if (joint < chain.first || joint >= end)
                continue;
            for (var i = joint; i < end; i++)
                points[i] += delta;
            return;
        }
    }

    private static List<(string name, int parent, Vector2 start, Vector2 end)> BuildBoneSpecs(
        Vector2[] points, Rect canvas, bool extraArms = false, bool extraLegs = false)
    {
        var specs = new List<(string name, int parent, Vector2 start, Vector2 end)>();
        if (points==null || points.Length<6 || canvas.width <= 0f || canvas.height <= 0f)
            return specs;
        var rootLength = Mathf.Max(1f, Vector2.Distance(points[0], points[5]) * 0.15f);
        specs.Add(("root", -1, Vector2.zero, Vector2.right * rootLength));
        var torsoBones = new int[Chains[0].names.Length];
        foreach (var chain in Chains)
        {
            if (!IsChainEnabled(chain.first, extraArms, extraLegs))
                continue;
            if(chain.first+chain.names.Length>=points.Length)continue;
            var parent = chain.torsoParent == 3 ? torsoBones[2] : chain.torsoParent == 0 ? torsoBones[0] : 0;
            for (var i = 0; i < chain.names.Length; i++)
            {
                var point = chain.first + i;
                var start = points[point];
                var end = points[point + 1];
                if (IntersectsCanvas(start, end, canvas))
                {
                    specs.Add((chain.names[i], parent, start, end));
                    parent = specs.Count - 1;
                }
                if (chain.torsoParent < 0)
                    torsoBones[i] = parent;
            }
        }
        if (specs.Count == 1)
            specs.Clear();
        return specs;
    }

    private static bool IntersectsCanvas(Vector2 start, Vector2 end, Rect canvas)
    {
        var direction = end - start;
        var enter = 0f;
        var exit = 1f;
        return IntersectsAxis(start.x, direction.x, canvas.xMin, canvas.xMax) &&
               IntersectsAxis(start.y, direction.y, canvas.yMin, canvas.yMax);

        bool IntersectsAxis(float position, float delta, float min, float max)
        {
            if (delta == 0f)
                return position >= min && position <= max;
            var first = (min - position) / delta;
            var second = (max - position) / delta;
            enter = Mathf.Max(enter, Mathf.Min(first, second));
            exit = Mathf.Min(exit, Mathf.Max(first, second));
            return enter <= exit;
        }
    }

    private static int FindEndpoint(Vector2[] points, PsdCoordinateSpace coordinates, Vector2 mouse,
        bool extraArms = false, bool extraLegs = false)
    {
        if (points == null)
            return -1;
        var bestDistance = 9f * 9f;
        var best = -1;
        for (var i = 0; i < points.Length; i++)
        {
            if (!IsChainEnabled(i < 22 ? 0 : i < 30 ? 22 : 30, extraArms, extraLegs))
                continue;
            var distance = (coordinates.DocumentToView(points[i]) - mouse).sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }

    private static Rect GetBounds(Vector2 a, Vector2 b) =>
        new Rect(Vector2.Min(a, b), Vector2.Max(a, b) - Vector2.Min(a, b));

}
#endif
