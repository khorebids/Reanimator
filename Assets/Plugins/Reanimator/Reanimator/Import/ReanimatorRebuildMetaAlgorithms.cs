#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

internal static class ReanimatorRebuildMetaAlgorithms
{
    public static string BuildLayerHierarchyKey(bool isGroup, int index, string name, int layerId)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}:{2}",
            isGroup ? "G" : "L",
            index,
            BuildKey(name, layerId));
    }

    public static string BuildFlatLayerHierarchyKey(string name, int layerId)
    {
        return "L:" + BuildKey(name, layerId);
    }

    public static bool ShouldUseMetaSpritePosition(Vector2 currentPosition, Rect rect, Vector2 metaPosition, bool hasSerializedPosition)
    {
        if (!hasSerializedPosition)
            return true;
        if ((currentPosition - metaPosition).sqrMagnitude <= 0.0001f)
            return false;

        var atlasPosition = rect.position;
        return (currentPosition - atlasPosition).sqrMagnitude <= 0.0001f ||
               currentPosition.sqrMagnitude <= 0.0001f;
    }

    public static Vector2 ChooseLayerSpritePosition(
        Rect spriteRect,
        bool hasSpritePosition,
        Vector2 spritePosition,
        bool hasMetaPosition,
        Vector2 metaPosition,
        bool hasCharacterPartPosition,
        Vector2 characterPartPosition,
        bool preferCharacterPartPosition,
        out bool hasResolvedPosition)
    {
        var result = spriteRect.position;
        hasResolvedPosition = false;

        if (preferCharacterPartPosition && hasCharacterPartPosition)
        {
            result = characterPartPosition;
            hasResolvedPosition = true;
        }

        if (hasSpritePosition)
        {
            result = spritePosition;
            hasResolvedPosition = true;
        }

        if (hasMetaPosition && ShouldUseMetaSpritePosition(result, spriteRect, metaPosition, hasResolvedPosition))
        {
            result = metaPosition;
            hasResolvedPosition = true;
        }

        if ((!hasResolvedPosition || preferCharacterPartPosition) && hasCharacterPartPosition)
        {
            result = characterPartPosition;
            hasResolvedPosition = true;
        }

        return result;
    }

    public static Dictionary<string, Vector2> ParseMetaSpritePositionBySpriteGuid(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        if (lines == null)
            return result;

        var active = false;
        var itemIndent = 0;
        var currentGuid = string.Empty;
        var currentPosition = Vector2.zero;
        var hasPosition = false;

        void Flush()
        {
            if (!active || string.IsNullOrEmpty(currentGuid) || !hasPosition)
                return;
            if (!result.ContainsKey(currentGuid))
                result[currentGuid] = currentPosition;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.TrimStart();
            var indent = CountIndent(line);
            var isListItem = trimmed.StartsWith("- ", StringComparison.Ordinal);
            if (active && isListItem && indent <= itemIndent)
            {
                Flush();
                active = false;
                currentGuid = string.Empty;
                currentPosition = Vector2.zero;
                hasPosition = false;
            }

            if (!active && isListItem && (indent == 2 || trimmed.StartsWith("- spritePosition:", StringComparison.Ordinal)))
            {
                active = true;
                itemIndent = indent;
            }

            if (!active)
                continue;

            if (TryParseSpriteGuidLine(trimmed, out var guid))
                currentGuid = guid;
            if (trimmed.StartsWith("spritePosition:", StringComparison.Ordinal) ||
                trimmed.StartsWith("- spritePosition:", StringComparison.Ordinal))
            {
                if (TryReadSpritePosition(lines, i, out var position))
                {
                    currentPosition = position;
                    hasPosition = true;
                }
            }
        }

        Flush();
        return result;
    }

    public static bool TryParseSpriteGuidLine(string trimmedLine, out string guid)
    {
        guid = string.Empty;
        if (string.IsNullOrEmpty(trimmedLine))
            return false;

        if (trimmedLine.StartsWith("- ", StringComparison.Ordinal))
            trimmedLine = trimmedLine.Substring(2).TrimStart();

        var match = Regex.Match(trimmedLine, @"^(spriteID|m_SpriteID|spriteId|m_SpriteId):\s*([0-9a-fA-F\-]+)\s*$");
        if (!match.Success)
            return false;

        guid = NormalizeGuidString(match.Groups[2].Value);
        return !string.IsNullOrEmpty(guid);
    }

    public static bool TryReadSpritePosition(IReadOnlyList<string> lines, int lineIndex, out Vector2 position)
    {
        position = Vector2.zero;
        if (lines == null || lineIndex < 0 || lineIndex >= lines.Count)
            return false;

        var line = lines[lineIndex] ?? string.Empty;
        if (TryParseInlineVector2(line, out position))
            return true;

        var hasX = false;
        var hasY = false;
        var x = 0f;
        var y = 0f;
        for (var i = lineIndex + 1; i < lines.Count && i <= lineIndex + 6; i++)
        {
            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.StartsWith("x:", StringComparison.Ordinal))
            {
                x = ParseFloatInvariant(trimmed.Substring(2).Trim(), 0f);
                hasX = true;
            }
            else if (trimmed.StartsWith("y:", StringComparison.Ordinal))
            {
                y = ParseFloatInvariant(trimmed.Substring(2).Trim(), 0f);
                hasY = true;
            }

            if (hasX && hasY)
            {
                position = new Vector2(x, y);
                return true;
            }
        }

        return false;
    }

    public static bool TryParseInlineVector2(string line, out Vector2 position)
    {
        position = Vector2.zero;
        if (string.IsNullOrEmpty(line))
            return false;

        var mx = Regex.Match(line, @"x:\s*([-+0-9eE\.]+)");
        var my = Regex.Match(line, @"y:\s*([-+0-9eE\.]+)");
        if (!mx.Success || !my.Success)
            return false;

        position = new Vector2(
            ParseFloatInvariant(mx.Groups[1].Value, 0f),
            ParseFloatInvariant(my.Groups[1].Value, 0f));
        return true;
    }

    public static string BuildKey(string name, int layerId)
    {
        return layerId != 0 ? layerId.ToString(CultureInfo.InvariantCulture) : name ?? string.Empty;
    }

    public static string NormalizeGuidString(string guid)
    {
        return string.IsNullOrEmpty(guid) ? string.Empty : guid.Replace("-", string.Empty).ToLowerInvariant();
    }

    public static int CountIndent(string line)
    {
        if (string.IsNullOrEmpty(line))
            return 0;

        var count = 0;
        while (count < line.Length && line[count] == ' ')
            count++;
        return count;
    }

    public static float ParseFloatInvariant(string text, float fallback)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }
}
#endif
