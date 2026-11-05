#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

internal sealed class ReanimatorRigSpriteMeta
{
    public string name;
    public string spriteId;
    public Vector2 position;
    public Vector2 size;
}

internal static class ReanimatorBoneUtilityAlgorithms
{
    public static List<string> BuildEmptyCharacterDataBlock(string header, Vector2 pivot)
    {
        return new List<string>
        {
            header,
            "    bones: []",
            "    parts: []",
            "    dimension: {x: 0, y: 0}",
            "    characterGroups: []",
            $"    pivot: {{x: {FormatFloat(pivot.x)}, y: {FormatFloat(pivot.y)}}}",
            "    boneReadOnly: 0"
        };
    }

    public static List<string> BuildCopiedCharacterDataBlock(
        string header,
        IReadOnlyList<string> sourceBonesBlock,
        IReadOnlyDictionary<string, string> sourcePartsBySpriteId,
        IReadOnlyDictionary<string, string> sourceSpriteIdByName,
        IReadOnlyList<ReanimatorRigSpriteMeta> targetSprites,
        out int mappedParts)
    {
        mappedParts = 0;
        var lines = new List<string> { string.IsNullOrWhiteSpace(header) ? "  characterData:" : header };
        if (sourceBonesBlock != null)
        {
            foreach (var line in sourceBonesBlock)
            {
                if (line.TrimStart().StartsWith("guid:", StringComparison.Ordinal))
                    lines.Add(GetIndent(line) + "guid: " + Guid.NewGuid().ToString("N"));
                else
                    lines.Add(line);
            }
        }

        lines.Add("    parts:");
        if (targetSprites != null)
        {
            foreach (var sprite in targetSprites)
            {
                var sourceName = ResolveSourceSpriteName(sprite.name, sourceSpriteIdByName);
                var bones = string.Empty;
                if (!string.IsNullOrWhiteSpace(sourceName) &&
                    sourceSpriteIdByName != null &&
                    sourceSpriteIdByName.TryGetValue(sourceName, out var sourceSpriteId) &&
                    sourcePartsBySpriteId != null &&
                    sourcePartsBySpriteId.TryGetValue(sourceSpriteId, out var sourceBones))
                {
                    bones = sourceBones;
                    if (!string.IsNullOrWhiteSpace(bones))
                        mappedParts++;
                }

                lines.Add("    - spritePosition:");
                lines.Add($"        x: {FormatFloat(sprite.position.x)}");
                lines.Add($"        y: {FormatFloat(sprite.position.y)}");
                lines.Add($"        width: {FormatFloat(sprite.size.x)}");
                lines.Add($"        height: {FormatFloat(sprite.size.y)}");
                lines.Add($"      spriteId: {sprite.spriteId}");
                lines.Add($"      bones: {bones}");
                lines.Add("      parentGroup: 0");
                lines.Add("      order: 0");
            }
        }

        lines.Add("    dimension: {x: 0, y: 0}");
        lines.Add("    characterGroups: []");
        lines.Add("    pivot: {x: 0.5, y: 0}");
        lines.Add("    boneReadOnly: 0");
        return lines;
    }

    public static bool TryExtractBonesBlock(IReadOnlyList<string> lines, out List<string> bonesBlock, out int boneCount, out string error)
    {
        bonesBlock = null;
        boneCount = 0;
        error = null;

        if (!TryFindCharacterDataRange(lines, "characterData:", out var start, out var end) &&
            !TryFindCharacterDataRange(lines, "m_CharacterData:", out start, out end))
        {
            error = "Source characterData section not found.";
            return false;
        }

        var bonesLine = -1;
        for (var i = start + 1; i < end; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("bones:", StringComparison.Ordinal))
                continue;

            if (trimmed.Contains("[]"))
            {
                error = "Source has no bones.";
                return false;
            }

            bonesLine = i;
            break;
        }

        if (bonesLine < 0)
        {
            error = "Source bones block not found.";
            return false;
        }

        var blockEnd = end;
        for (var i = bonesLine + 1; i < end; i++)
        {
            if (lines[i].TrimStart().StartsWith("parts:", StringComparison.Ordinal))
            {
                blockEnd = i;
                break;
            }
        }

        bonesBlock = new List<string>();
        for (var i = bonesLine; i < blockEnd; i++)
        {
            var line = lines[i];
            bonesBlock.Add(line);
            if (line.TrimStart().StartsWith("- name:", StringComparison.Ordinal))
                boneCount++;
        }

        if (boneCount <= 0)
        {
            error = "Source has no named bones.";
            return false;
        }

        return true;
    }

    public static bool TryDeleteAllBonesFromMetaLines(List<string> lines, out string error)
    {
        error = null;
        if (lines == null)
        {
            error = "Target meta lines are missing.";
            return false;
        }

        if (!ReplaceCharacterData(lines, "characterData:", BuildEmptyCharacterDataBlock("  characterData:", new Vector2(0.5f, 0f))) &&
            !ReplaceCharacterData(lines, "m_CharacterData:", BuildEmptyCharacterDataBlock("  m_CharacterData:", new Vector2(0.5f, 0f))))
        {
            error = "Target characterData section not found.";
            return false;
        }

        ReplaceCharacterData(lines, "sharedRigCharacterData:", BuildEmptyCharacterDataBlock("  sharedRigCharacterData:", Vector2.zero));
        ReplaceCharacterData(lines, "m_SharedRigCharacterData:", BuildEmptyCharacterDataBlock("  m_SharedRigCharacterData:", Vector2.zero));
        ClearSpriteBoneBlocks(lines);
        return true;
    }

    public static bool TryCopyBonesBetweenMetaLines(
        IReadOnlyList<string> sourceLines,
        List<string> targetLines,
        out int copiedBones,
        out int mappedParts,
        out string error)
    {
        copiedBones = 0;
        mappedParts = 0;
        error = null;

        if (targetLines == null)
        {
            error = "Target meta lines are missing.";
            return false;
        }

        if (!TryExtractBonesBlock(sourceLines, out var sourceBonesBlock, out copiedBones, out error))
            return false;

        var sourcePartsBySpriteId = ParseCharacterPartBonesBySpriteId(sourceLines);
        var sourceSprites = ParseRigSprites(sourceLines);
        var targetSprites = ParseRigSprites(targetLines);
        if (targetSprites.Count == 0)
        {
            error = "Target rigSpriteImportData is empty.";
            return false;
        }

        var sourceSpriteIdByName = BuildSpriteIdByName(sourceSprites);
        var targetCharacterDataHeader = "  characterData:";
        if (!TryFindCharacterDataRange(targetLines, "characterData:", out _, out _) &&
            TryFindCharacterDataRange(targetLines, "m_CharacterData:", out _, out _))
            targetCharacterDataHeader = "  m_CharacterData:";

        var generatedCharacterData = BuildCopiedCharacterDataBlock(
            targetCharacterDataHeader,
            sourceBonesBlock,
            sourcePartsBySpriteId,
            sourceSpriteIdByName,
            targetSprites,
            out mappedParts);

        if (!ReplaceCharacterData(targetLines, "characterData:", generatedCharacterData) &&
            !ReplaceCharacterData(targetLines, "m_CharacterData:", generatedCharacterData))
        {
            error = "Target characterData section not found.";
            return false;
        }

        return true;
    }

    public static Dictionary<string, string> ParseCharacterPartBonesBySpriteId(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryFindCharacterDataRange(lines, "characterData:", out var start, out var end) &&
            !TryFindCharacterDataRange(lines, "m_CharacterData:", out start, out end))
            return result;

        var partsLine = -1;
        for (var i = start + 1; i < end; i++)
        {
            if (lines[i].TrimStart().StartsWith("parts:", StringComparison.Ordinal))
            {
                partsLine = i;
                break;
            }
        }

        if (partsLine < 0)
            return result;

        string currentSpriteId = null;
        string currentBones = null;

        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(currentSpriteId) && currentBones != null)
                result[NormalizeGuidString(currentSpriteId)] = currentBones;
            currentSpriteId = null;
            currentBones = null;
        }

        for (var i = partsLine + 1; i < end; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("- spritePosition:", StringComparison.Ordinal))
                Flush();
            else if (TryReadValue(trimmed, "spriteId:", out var spriteId) ||
                     TryReadValue(trimmed, "spriteID:", out spriteId) ||
                     TryReadValue(trimmed, "m_SpriteId:", out spriteId) ||
                     TryReadValue(trimmed, "m_SpriteID:", out spriteId))
                currentSpriteId = spriteId;
            else if (TryReadValue(trimmed, "bones:", out var bones))
                currentBones = bones;
        }

        Flush();
        return result;
    }

    public static List<ReanimatorRigSpriteMeta> ParseRigSprites(IReadOnlyList<string> lines)
    {
        var result = new List<ReanimatorRigSpriteMeta>();
        var start = FindTopLevelSection(lines, "rigSpriteImportData:");
        if (start < 0)
            start = FindTopLevelSection(lines, "m_RigSpriteImportData:");
        if (start < 0)
            return result;

        ReanimatorRigSpriteMeta current = null;

        void Flush()
        {
            if (current != null &&
                !string.IsNullOrWhiteSpace(current.name) &&
                !string.IsNullOrWhiteSpace(current.spriteId))
                result.Add(current);
            current = null;
        }

        for (var i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var indent = CountIndent(line);
            if (indent <= 2 && trimmed.EndsWith(":", StringComparison.Ordinal) && !trimmed.StartsWith("- ", StringComparison.Ordinal))
                break;

            if (indent == 2 && TryReadValue(trimmed, "- name:", out var name))
            {
                Flush();
                current = new ReanimatorRigSpriteMeta { name = name };
                continue;
            }

            if (current == null)
                continue;

            if (indent == 4 &&
                (TryReadValue(trimmed, "spriteID:", out var spriteId) ||
                 TryReadValue(trimmed, "spriteId:", out spriteId) ||
                 TryReadValue(trimmed, "m_SpriteID:", out spriteId) ||
                 TryReadValue(trimmed, "m_SpriteId:", out spriteId)))
            {
                current.spriteId = NormalizeGuidString(spriteId);
                continue;
            }

            if (indent == 4 && trimmed.StartsWith("rect:", StringComparison.Ordinal))
            {
                if (TryReadRect(lines, i, out var position, out var size))
                {
                    current.position = position;
                    current.size = size;
                }
                continue;
            }

            if (indent == 4 && trimmed.StartsWith("spritePosition:", StringComparison.Ordinal))
                TryReadInlineVector2(trimmed, out current.position);
        }

        Flush();
        return result;
    }

    public static Dictionary<string, string> BuildSpriteIdByName(IReadOnlyList<ReanimatorRigSpriteMeta> sprites)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sprites == null)
            return result;

        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            if (sprite == null || string.IsNullOrWhiteSpace(sprite.name) || string.IsNullOrWhiteSpace(sprite.spriteId))
                continue;
            if (!result.ContainsKey(sprite.name))
                result.Add(sprite.name, sprite.spriteId);
        }

        return result;
    }

    public static string ResolveSourceSpriteName(string targetName, IReadOnlyDictionary<string, string> sourceSpriteIdByName)
    {
        if (string.IsNullOrWhiteSpace(targetName) || sourceSpriteIdByName == null || sourceSpriteIdByName.Count == 0)
            return null;
        if (sourceSpriteIdByName.ContainsKey(targetName))
            return targetName;

        var candidates = new List<string>();
        if (targetName.EndsWith("_bot", StringComparison.OrdinalIgnoreCase))
            candidates.Add(targetName.Substring(0, targetName.Length - 4));
        if (targetName.StartsWith("front_", StringComparison.OrdinalIgnoreCase))
            candidates.Add(targetName.Substring(6));
        if (targetName.StartsWith("back_", StringComparison.OrdinalIgnoreCase))
            candidates.Add(targetName.Substring(5));
        if (targetName.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0 ||
            targetName.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0)
            candidates.Add("hair");
        if (targetName.IndexOf("pony", StringComparison.OrdinalIgnoreCase) >= 0)
            candidates.Add("pony");
        if (targetName.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            candidates.Add("face_normal");
            candidates.Add("face");
        }
        if (targetName.IndexOf("eye_whites", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            candidates.Add("eye_whites_r");
            candidates.Add("eye_whites_l");
        }
        else if (targetName.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            candidates.Add("eye_right");
            candidates.Add("eye_left");
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!string.IsNullOrWhiteSpace(candidate) && sourceSpriteIdByName.ContainsKey(candidate))
                return candidate;
        }

        return null;
    }

    public static bool ReplaceCharacterData(List<string> lines, string sectionHeader, IReadOnlyList<string> replacement)
    {
        if (!TryFindCharacterDataRange(lines, sectionHeader, out var start, out var end))
            return false;

        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, replacement);
        return true;
    }

    public static bool TryFindCharacterDataRange(IReadOnlyList<string> lines, string sectionHeader, out int start, out int end)
    {
        start = -1;
        end = -1;
        if (lines == null || string.IsNullOrWhiteSpace(sectionHeader))
            return false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (CountIndent(line) != 2 || !line.TrimStart().StartsWith(sectionHeader, StringComparison.Ordinal))
                continue;
            start = i;
            break;
        }

        if (start < 0)
            return false;

        end = lines.Count;
        for (var i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (CountIndent(line) == 2 && trimmed.EndsWith(":", StringComparison.Ordinal) && !trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        return end > start;
    }

    public static void ClearSpriteBoneBlocks(List<string> lines)
    {
        if (lines == null)
            return;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (CountIndent(line) != 4 || !line.TrimStart().StartsWith("spriteBone:", StringComparison.Ordinal))
                continue;

            lines[i] = GetIndent(line) + "spriteBone: []";
            var removeStart = i + 1;
            var removeCount = 0;
            for (var j = removeStart; j < lines.Count; j++)
            {
                var trimmed = lines[j].TrimStart();
                var indent = CountIndent(lines[j]);
                if (indent < 4 || (indent == 4 && trimmed.Contains(":") && !trimmed.StartsWith("- ", StringComparison.Ordinal)))
                    break;
                removeCount++;
            }

            if (removeCount > 0)
                lines.RemoveRange(removeStart, removeCount);
        }
    }

    public static bool TryReadRect(IReadOnlyList<string> lines, int lineIndex, out Vector2 position, out Vector2 size)
    {
        position = Vector2.zero;
        size = Vector2.zero;
        var foundX = false;
        var foundY = false;
        var foundWidth = false;
        var foundHeight = false;

        for (var i = lineIndex + 1; i < lines.Count && i <= lineIndex + 8; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!foundX && TryReadFloat(trimmed, "x:", out var x))
            {
                position.x = x;
                foundX = true;
            }
            else if (!foundY && TryReadFloat(trimmed, "y:", out var y))
            {
                position.y = y;
                foundY = true;
            }
            else if (!foundWidth && TryReadFloat(trimmed, "width:", out var width))
            {
                size.x = width;
                foundWidth = true;
            }
            else if (!foundHeight && TryReadFloat(trimmed, "height:", out var height))
            {
                size.y = height;
                foundHeight = true;
            }
        }

        return foundX && foundY && foundWidth && foundHeight;
    }

    public static bool TryReadInlineVector2(string line, out Vector2 value)
    {
        value = Vector2.zero;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var open = line.IndexOf('{');
        var close = line.IndexOf('}');
        if (open < 0 || close <= open)
            return false;

        var body = line.Substring(open + 1, close - open - 1);
        var parts = body.Split(',');
        var hasX = false;
        var hasY = false;
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (TryReadFloat(part, "x:", out var x))
            {
                value.x = x;
                hasX = true;
            }
            else if (TryReadFloat(part, "y:", out var y))
            {
                value.y = y;
                hasY = true;
            }
        }

        return hasX && hasY;
    }

    public static bool TryReadFloat(string line, string prefix, out float value)
    {
        value = 0f;
        if (!TryReadValue(line, prefix, out var raw))
            return false;
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryReadValue(string line, string prefix, out string value)
    {
        value = null;
        if (line == null || !line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        value = line.Substring(prefix.Length).Trim();
        return true;
    }

    public static int FindTopLevelSection(IReadOnlyList<string> lines, string sectionHeader)
    {
        if (lines == null || string.IsNullOrWhiteSpace(sectionHeader))
            return -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (CountIndent(lines[i]) == 2 && lines[i].TrimStart().StartsWith(sectionHeader, StringComparison.Ordinal))
                return i;
        }
        return -1;
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

    public static string GetIndent(string line)
    {
        var count = CountIndent(line);
        return count <= 0 ? string.Empty : new string(' ', count);
    }

    public static string NormalizeGuidString(string guid)
    {
        return string.IsNullOrWhiteSpace(guid) ? string.Empty : guid.Trim().Replace("-", string.Empty);
    }

    public static string FormatFloat(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
#endif
