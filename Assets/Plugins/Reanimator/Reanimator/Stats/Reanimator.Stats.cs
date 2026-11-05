#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private void DrawStatsTab()
    {
        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            EditorGUILayout.HelpBox("Select a PSD/PSB file in Project to see statistics.", MessageType.Info);
            return;
        }

        if (!IsPsdPath(path))
        {
            EditorGUILayout.HelpBox("Selected file is not PSD/PSB. Pick a PSD/PSB in Project.", MessageType.Warning);
            return;
        }

        var importer = AssetImporter.GetAtPath(path) as PSDImporter;
        if (importer == null)
        {
            EditorGUILayout.HelpBox("Importer for selected file is not PSD Importer.", MessageType.Warning);
            return;
        }

        var sprites = CollectSprites(path);
        var stats = CalculateStats(sprites);

        EditorGUILayout.LabelField("File", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(path, EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(6f);

        EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Sprites", stats.SpriteCount.ToString());
        EditorGUILayout.LabelField("Vertices (total)", stats.TotalVertices.ToString());
        if (stats.SpriteCount > 0)
        {
            EditorGUILayout.LabelField("Vertices (min/max)", $"{stats.MinVertices}/{stats.MaxVertices}");
            EditorGUILayout.LabelField("Vertices (avg)", stats.AvgVertices.ToString());
        }

        EditorGUILayout.Space(6f);
        showPerSprite = EditorGUILayout.Foldout(showPerSprite, "Per sprite", true);
        if (showPerSprite)
        {
            using (var scope = new EditorGUILayout.ScrollViewScope(statsScroll))
            {
                statsScroll = scope.scrollPosition;
                foreach (var item in stats.PerSprite)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(item.Name, GUILayout.MinWidth(200f));
                        EditorGUILayout.LabelField(item.Vertices.ToString(), GUILayout.Width(80f));
                    }
                }
            }
        }
    }

    private static List<Sprite> CollectSprites(string path)
    {
        var sprites = new List<Sprite>();
        var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        foreach (var asset in subAssets)
        {
            if (asset is Sprite sprite) sprites.Add(sprite);
        }

        var mainSprite = AssetDatabase.LoadMainAssetAtPath(path) as Sprite;
        if (mainSprite != null && !sprites.Contains(mainSprite))
            sprites.Add(mainSprite);

        return sprites;
    }

    private static StatsResult CalculateStats(List<Sprite> sprites)
    {
        var result = new StatsResult();
        if (sprites == null || sprites.Count == 0) return result;

        result.SpriteCount = sprites.Count;
        result.MinVertices = int.MaxValue;

        foreach (var sprite in sprites)
        {
            var vertexCount = GetVertexCount(sprite);
            result.TotalVertices += vertexCount;
            result.MinVertices = Math.Min(result.MinVertices, vertexCount);
            result.MaxVertices = Math.Max(result.MaxVertices, vertexCount);
            result.PerSprite.Add(new SpriteStats(sprite.name, vertexCount));
        }

        result.AvgVertices = Mathf.RoundToInt((float)result.TotalVertices / result.SpriteCount);
        result.PerSprite.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static int GetVertexCount(Sprite sprite)
    {
        if (sprite == null) return 0;
        var vertices = sprite.vertices;
        return vertices != null ? vertices.Length : 0;
    }

    private sealed class StatsResult
    {
        public int SpriteCount;
        public int TotalVertices;
        public int MinVertices;
        public int MaxVertices;
        public int AvgVertices;
        public readonly List<SpriteStats> PerSprite = new List<SpriteStats>();
    }

    private readonly struct SpriteStats
    {
        public readonly string Name;
        public readonly int Vertices;

        public SpriteStats(string name, int vertices)
        {
            Name = name;
            Vertices = vertices;
        }
    }
}
#endif

