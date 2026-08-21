using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GalExcleTools.Services;

internal static class StoryAssetFieldService
{
    public static StoryAssetReplacementPlan BuildForwardContiguousPlan(
        IReadOnlyList<StoryRow> rows,
        int startIndex,
        string fieldName,
        int replacementIndex)
    {
        ValidateForwardReplacement(fieldName, rows, startIndex);
        var originalIndex = ParseAssetIndex(rows[startIndex].Get(fieldName));
        if (originalIndex == replacementIndex)
        {
            return new StoryAssetReplacementPlan(
                fieldName,
                originalIndex,
                replacementIndex,
                startIndex,
                startIndex,
                0,
                StoryAssetReplacementStopReason.NoChange,
                null,
                null);
        }

        var endIndex = startIndex;
        int? stoppingIndex = null;
        int? stoppingValue = null;
        for (var index = startIndex; index < rows.Count; index++)
        {
            var currentIndex = ParseAssetIndex(rows[index].Get(fieldName));
            if (currentIndex != originalIndex)
            {
                stoppingIndex = index;
                stoppingValue = currentIndex;
                break;
            }

            endIndex = index;
        }

        var stopReason = stoppingIndex is null
            ? StoryAssetReplacementStopReason.EndOfChapter
            : StoryAssetReplacementStopReason.DifferentValue;
        return new StoryAssetReplacementPlan(
            fieldName,
            originalIndex,
            replacementIndex,
            startIndex,
            endIndex,
            endIndex - startIndex + 1,
            stopReason,
            stoppingIndex,
            stoppingValue);
    }

    public static StoryAssetReplacementPlan BuildForwardContiguousCharacterLayerPlan(
        IReadOnlyList<StoryRow> rows,
        int startIndex,
        string fieldName,
        int replacementIndex,
        string characterFieldName)
    {
        ValidateForwardReplacement(fieldName, rows, startIndex);
        ValidateCharacterField(characterFieldName);
        var originalIndex = ParseAssetIndex(rows[startIndex].Get(fieldName));
        var originalCharacter = rows[startIndex].Get(characterFieldName);
        var secondaryContinuityFieldName = GetSecondaryContinuityFieldName(fieldName);
        var originalSecondaryContinuityValue = secondaryContinuityFieldName is null
            ? null
            : rows[startIndex].Get(secondaryContinuityFieldName);
        if (originalIndex == replacementIndex)
        {
            return new StoryAssetReplacementPlan(
                fieldName,
                originalIndex,
                replacementIndex,
                startIndex,
                startIndex,
                0,
                StoryAssetReplacementStopReason.NoChange,
                null,
                null)
            {
                ContinuityFieldName = characterFieldName,
                OriginalContinuityValue = originalCharacter,
                SecondaryContinuityFieldName = secondaryContinuityFieldName,
                OriginalSecondaryContinuityValue = originalSecondaryContinuityValue
            };
        }

        var endIndex = startIndex;
        int? stoppingIndex = null;
        int? stoppingValue = null;
        var stopReason = StoryAssetReplacementStopReason.EndOfChapter;
        for (var index = startIndex; index < rows.Count; index++)
        {
            var currentIndex = ParseAssetIndex(rows[index].Get(fieldName));
            if (!string.Equals(rows[index].Get(characterFieldName), originalCharacter, StringComparison.OrdinalIgnoreCase))
            {
                stoppingIndex = index;
                stoppingValue = currentIndex;
                stopReason = StoryAssetReplacementStopReason.DifferentContext;
                break;
            }
            if (secondaryContinuityFieldName is not null &&
                !string.Equals(
                    rows[index].Get(secondaryContinuityFieldName),
                    originalSecondaryContinuityValue,
                    StringComparison.Ordinal))
            {
                stoppingIndex = index;
                stoppingValue = currentIndex;
                stopReason = StoryAssetReplacementStopReason.DifferentContext;
                break;
            }
            if (currentIndex != originalIndex)
            {
                stoppingIndex = index;
                stoppingValue = currentIndex;
                stopReason = StoryAssetReplacementStopReason.DifferentValue;
                break;
            }

            endIndex = index;
        }

        return new StoryAssetReplacementPlan(
            fieldName,
            originalIndex,
            replacementIndex,
            startIndex,
            endIndex,
            endIndex - startIndex + 1,
            stopReason,
            stoppingIndex,
            stoppingValue)
        {
            ContinuityFieldName = characterFieldName,
            OriginalContinuityValue = originalCharacter,
            SecondaryContinuityFieldName = secondaryContinuityFieldName,
            OriginalSecondaryContinuityValue = originalSecondaryContinuityValue
        };
    }

    public static int ApplyForwardContiguousPlan(
        IReadOnlyList<StoryRow> rows,
        StoryAssetReplacementPlan plan)
    {
        ValidateForwardReplacement(plan.FieldName, rows, plan.StartIndex);
        if (plan.Count == 0)
        {
            return 0;
        }
        if (plan.EndIndex < plan.StartIndex || plan.EndIndex >= rows.Count ||
            plan.Count != plan.EndIndex - plan.StartIndex + 1)
        {
            throw new InvalidOperationException("连续素材替换计划范围无效。");
        }

        for (var index = plan.StartIndex; index <= plan.EndIndex; index++)
        {
            if (ParseAssetIndex(rows[index].Get(plan.FieldName)) != plan.OriginalIndex)
            {
                throw new InvalidOperationException("连续素材替换范围已经变化，请重新选择素材。");
            }
            if (!string.IsNullOrWhiteSpace(plan.ContinuityFieldName) &&
                !string.Equals(
                    rows[index].Get(plan.ContinuityFieldName),
                    plan.OriginalContinuityValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("连续素材替换的角色范围已经变化，请重新选择素材。");
            }
            if (!string.IsNullOrWhiteSpace(plan.SecondaryContinuityFieldName) &&
                !string.Equals(
                    rows[index].Get(plan.SecondaryContinuityFieldName),
                    plan.OriginalSecondaryContinuityValue,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("连续素材替换的服装范围已经变化，请重新选择素材。");
            }
        }

        var replacementValue = plan.ReplacementIndex.ToString(CultureInfo.InvariantCulture);
        for (var index = plan.StartIndex; index <= plan.EndIndex; index++)
        {
            rows[index].Set(plan.FieldName, replacementValue);
        }

        return plan.Count;
    }

    public static StoryValueReplacementPlan BuildForwardContiguousValuePlan(
        IReadOnlyList<StoryRow> rows,
        int startIndex,
        string fieldName,
        string replacementValue)
    {
        ValidateCharacterField(fieldName);
        if (startIndex < 0 || startIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        var originalValue = rows[startIndex].Get(fieldName);
        if (string.Equals(originalValue, replacementValue, StringComparison.OrdinalIgnoreCase))
        {
            return new StoryValueReplacementPlan(
                fieldName,
                originalValue,
                replacementValue,
                startIndex,
                startIndex,
                0,
                StoryAssetReplacementStopReason.NoChange,
                null,
                null);
        }

        var endIndex = startIndex;
        int? stoppingIndex = null;
        string? stoppingValue = null;
        for (var index = startIndex; index < rows.Count; index++)
        {
            var currentValue = rows[index].Get(fieldName);
            if (!string.Equals(currentValue, originalValue, StringComparison.OrdinalIgnoreCase))
            {
                stoppingIndex = index;
                stoppingValue = currentValue;
                break;
            }

            endIndex = index;
        }

        return new StoryValueReplacementPlan(
            fieldName,
            originalValue,
            replacementValue,
            startIndex,
            endIndex,
            endIndex - startIndex + 1,
            stoppingIndex is null
                ? StoryAssetReplacementStopReason.EndOfChapter
                : StoryAssetReplacementStopReason.DifferentValue,
            stoppingIndex,
            stoppingValue);
    }

    public static int ApplyForwardContiguousValuePlan(
        IReadOnlyList<StoryRow> rows,
        StoryValueReplacementPlan plan)
    {
        ValidateCharacterField(plan.FieldName);
        if (plan.Count == 0)
        {
            return 0;
        }
        if (plan.StartIndex < 0 ||
            plan.EndIndex < plan.StartIndex ||
            plan.EndIndex >= rows.Count ||
            plan.Count != plan.EndIndex - plan.StartIndex + 1)
        {
            throw new InvalidOperationException("连续角色替换计划范围无效。");
        }

        for (var index = plan.StartIndex; index <= plan.EndIndex; index++)
        {
            if (!string.Equals(rows[index].Get(plan.FieldName), plan.OriginalValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("连续角色替换范围已经变化，请重新选择角色。");
            }
        }
        for (var index = plan.StartIndex; index <= plan.EndIndex; index++)
        {
            rows[index].Set(plan.FieldName, plan.ReplacementValue);
        }

        return plan.Count;
    }

    public static int ReplaceForwardContiguousIndex(
        IReadOnlyList<StoryRow> rows,
        int startIndex,
        string fieldName,
        int replacementIndex)
    {
        var plan = BuildForwardContiguousPlan(rows, startIndex, fieldName, replacementIndex);
        return ApplyForwardContiguousPlan(rows, plan);
    }

    public static StoryAssetClipboard CreateClipboard(StoryRow row, string fieldName)
    {
        return new StoryAssetClipboard(fieldName, row.Get(fieldName));
    }

    public static bool IsSameField(StoryAssetClipboard clipboard, string fieldName)
    {
        return string.Equals(clipboard.FieldName, fieldName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesValue(StoryRow row, string fieldName, StoryAssetClipboard clipboard)
    {
        return string.Equals(row.Get(fieldName), clipboard.Value, StringComparison.Ordinal);
    }

    public static void ApplyClipboard(StoryRow row, string fieldName, StoryAssetClipboard clipboard)
    {
        row.Set(fieldName, clipboard.Value);
    }

    public static string GetDisplayName(string fieldName)
    {
        return fieldName switch
        {
            "BGindex" => "背景图",
            "BGM" => "BGM",
            "Scene" => "环境音",
            "TalkVfx" => "当前说话人滤镜",
            _ when IsCharacterLayerIndexField(fieldName) => "角色分层素材",
            _ => "基础素材"
        };
    }

    private static int ParseAssetIndex(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : 0;
    }

    private static void ValidateForwardReplacement(
        string fieldName,
        IReadOnlyList<StoryRow> rows,
        int startIndex)
    {
        if (fieldName is not ("BGindex" or "BGM" or "Scene") &&
            !IsCharacterLayerIndexField(fieldName))
        {
            throw new InvalidOperationException($"素材字段不支持向后连续修改：{fieldName}");
        }
        if (startIndex < 0 || startIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }
    }

    private static bool IsCharacterLayerIndexField(string fieldName)
    {
        var prefixes = new[] { "Body", "Face", "Adorn", "Vfx" };
        if (prefixes.Any(prefix => string.Equals(fieldName, $"Talk{prefix}", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return fieldName.Length >= 4 &&
            fieldName[^1] is >= '1' and <= '5' &&
            prefixes.Any(prefix => string.Equals(fieldName[..^1], prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetSecondaryContinuityFieldName(string fieldName)
    {
        if (string.Equals(fieldName, "TalkFace", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fieldName, "TalkAdorn", StringComparison.OrdinalIgnoreCase))
        {
            return "TalkBody";
        }
        if ((fieldName.StartsWith("Face", StringComparison.OrdinalIgnoreCase) ||
             fieldName.StartsWith("Adorn", StringComparison.OrdinalIgnoreCase)) &&
            fieldName[^1] is >= '1' and <= '5')
        {
            return $"Body{fieldName[^1]}";
        }

        return null;
    }

    private static void ValidateCharacterField(string fieldName)
    {
        if (!string.Equals(fieldName, "TalkChar", StringComparison.OrdinalIgnoreCase) &&
            !(fieldName.Length == 6 &&
              fieldName.StartsWith("Chara", StringComparison.OrdinalIgnoreCase) &&
              fieldName[5] is >= '1' and <= '5'))
        {
            throw new InvalidOperationException($"角色字段不支持向后连续修改：{fieldName}");
        }
    }
}

internal enum StoryAssetReplacementStopReason
{
    NoChange,
    DifferentValue,
    DifferentContext,
    EndOfChapter
}

internal sealed record StoryAssetReplacementPlan(
    string FieldName,
    int OriginalIndex,
    int ReplacementIndex,
    int StartIndex,
    int EndIndex,
    int Count,
    StoryAssetReplacementStopReason StopReason,
    int? StoppingIndex,
    int? StoppingValue)
{
    public string? ContinuityFieldName { get; init; }
    public string? OriginalContinuityValue { get; init; }
    public string? SecondaryContinuityFieldName { get; init; }
    public string? OriginalSecondaryContinuityValue { get; init; }
}

internal sealed record StoryValueReplacementPlan(
    string FieldName,
    string OriginalValue,
    string ReplacementValue,
    int StartIndex,
    int EndIndex,
    int Count,
    StoryAssetReplacementStopReason StopReason,
    int? StoppingIndex,
    string? StoppingValue);
