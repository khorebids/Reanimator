#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    [Serializable] private sealed class WindingMeshFixture { public string name; public int[] indices; }
    [Serializable] private sealed class WindingFixtures { public WindingMeshFixture[] meshes; }
    private static void MixedTriangleWindingSurvivesSpriteSkinOutline()
    {
        var fixtures = JsonUtility.FromJson<WindingFixtures>(File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/MixedTriangleWinding.json"));
        var type = typeof(UnityEngine.U2D.Animation.SpriteSkin).Assembly.GetType("UnityEngine.U2D.Animation.MeshUtilities");
        var outline = type.GetMethod("GetOutlineEdges", BindingFlags.Public | BindingFlags.Static);
        foreach (var fixture in fixtures.meshes)
        {
            var original = (int[])fixture.indices.Clone();
            var normalized = ReanimatorMeshWinding.Normalize(fixture.indices, out int flipped);
            AssertEqual(72, flipped, "Only the reversed patch must be repaired on " + fixture.name);
            AssertTrue(original.SequenceEqual(fixture.indices), "Normalization must not mutate the source array.");
            for (int i = 0; i < original.Length; i += 3)
                AssertTrue(original.Skip(i).Take(3).OrderBy(x => x).SequenceEqual(normalized.Skip(i).Take(3).OrderBy(x => x)),
                    "Repair must preserve every triangle and its vertex membership.");
            AssertTrue(normalized.SequenceEqual(ReanimatorMeshWinding.Normalize(normalized, out int repeated)), "Repeated repair must be stable.");
            AssertEqual(0, repeated, "Valid topology must not be rewritten.");
            using var input = new NativeArray<ushort>(normalized.Select(i => (ushort)i).ToArray(), Allocator.Temp);
            var output = outline.Invoke(null, new object[] { input });
            try { AssertTrue((int)output.GetType().GetProperty("Length").GetValue(output) > 0, "Unity must construct a contour for " + fixture.name); }
            finally { ((IDisposable)output).Dispose(); }
        }
    }
}
#endif
