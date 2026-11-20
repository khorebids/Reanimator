#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;

public static class ReanimatorAllInShoulderProbe
{
    private static readonly List<string> Calls = new();
    public static object Dispatch(string name)
    {
        Calls.Add(name);
        if(name=="get_SideAssTargets")return 3;
        if (name == "ApplySelectedAutoLimbJoints") return Enumerable.Repeat("Joint applied", 64).ToArray();
        if (name == "CommitCurrentBonePoseAsRest") return null;
        if (new[] { "ApplyDefaultAutoGeometry", "ApplyDeterministicAutoWeights", "ApplyShoulderContours", "ApplyBellyLimb",
                "ApplyAutomaticElbowJoints", "ApplyAutoSoftContour", "ApplySelectedSideAss", "ApplyFaceEyesAutomation" }.Contains(name)) return "Complete";
        throw new InvalidOperationException("Unexpected ALLIN call (including forbidden Qwen paths): " + name);
    }
    public static string Run()
    {
        var panelType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("ComfimatorPanel")).First(type => type != null);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        string targets = (string)panelType.GetField("AllInRemainingJointsKey", flags).GetRawConstantValue();
        bool hadTargets = EditorPrefs.HasKey(targets); int oldTargets = EditorPrefs.GetInt(targets, -1);
        var settings = new Dictionary<string, (bool present, bool value)>();
        foreach (string stage in new[] { "Geometry", "Skeleton", "Weights", "Limbs", "Shoulders", "Belly", "Elbows", "SoftContour", "RestPose", "FaceEyes" })
        {
            string key = "Reanimator.ALLIN.Stage." + stage;
            settings[key] = (EditorPrefs.HasKey(key), EditorPrefs.GetBool(key)); EditorPrefs.SetBool(key, true);
        }
        try
        {
            EditorPrefs.SetInt(targets, -1); Calls.Clear();
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ShoulderAllInProbe" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("Probe").DefineType("Host", TypeAttributes.Public);
            type.AddInterfaceImplementation(typeof(IReanimatorExternalPanelHost));
            type.DefineDefaultConstructor(MethodAttributes.Public);
            foreach (var method in typeof(IReanimatorExternalPanelHost).GetMethods())
            {
                var implementation = type.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                    method.ReturnType, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                var il = implementation.GetILGenerator();
                il.Emit(OpCodes.Ldstr, method.Name); il.Emit(OpCodes.Call, typeof(ReanimatorAllInShoulderProbe).GetMethod(nameof(Dispatch)));
                if (method.ReturnType == typeof(void)) il.Emit(OpCodes.Pop);
                else if (method.ReturnType.IsValueType) il.Emit(OpCodes.Unbox_Any, method.ReturnType);
                else il.Emit(OpCodes.Castclass, method.ReturnType);
                il.Emit(OpCodes.Ret); type.DefineMethodOverride(implementation, method);
            }
            var host = (IReanimatorExternalPanelHost)Activator.CreateInstance(type.CreateType());
            var panel = Activator.CreateInstance(panelType, true);
            panelType.GetMethod("ApplyAllInStages", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel,
                new object[] { host, new Func<string>(() => { Calls.Add("Skeleton"); return "Complete"; }), null, null });
            int limbs = Calls.IndexOf("ApplySelectedAutoLimbJoints"), shoulders = Calls.IndexOf("ApplyShoulderContours");
            if (limbs < 0 || shoulders != limbs + 1) throw new InvalidOperationException("Shoulders must execute immediately after Limbs.");
            if (Calls.Count(call => call == "ApplyShoulderContours") != 1) throw new InvalidOperationException("Shoulders must execute exactly once.");
            int sc=Calls.IndexOf("ApplyAutoSoftContour"),sideAss=Calls.IndexOf("ApplySelectedSideAss");
            if(sc<0 || sideAss<=sc || sideAss>=Calls.IndexOf("ApplyFaceEyesAutomation") || Calls.Count(call=>call=="ApplySelectedSideAss")!=1)
                throw new InvalidOperationException("Selected Side Ass must execute once after SC and before face effects.");
            return "PASS: actual ALLIN runs Shoulders after Limbs and selected Side Ass after SC, exactly once; no Qwen/FullBoobs calls.";
        }
        finally
        {
            foreach (var pair in settings) if (pair.Value.present) EditorPrefs.SetBool(pair.Key, pair.Value.value); else EditorPrefs.DeleteKey(pair.Key);
            if (hadTargets) EditorPrefs.SetInt(targets, oldTargets); else EditorPrefs.DeleteKey(targets);
        }
    }
}
#endif
