using System;
using System.Collections.Generic;
using System.Globalization;

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
        }

        var replacementValue = plan.ReplacementIndex.ToString(CultureInfo.InvariantCulture);
        for (var index = plan.StartIndex; index <= plan.EndIndex; index++)
        {
            rows[index].Set(plan.FieldName, replacementValue);
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
        if (fieldName is not ("BGindex" or "BGM" or "Scene"))
        {
            throw new InvalidOperationException($"素材字段不支持向后连续修改：{fieldName}");
        }
        if (startIndex < 0 || startIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }
    }
}

internal enum StoryAssetReplacementStopReason
{
    NoChange,
    DifferentValue,
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
    int? StoppingValue);
