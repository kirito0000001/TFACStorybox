using System;
using System.Collections.Generic;
using System.Globalization;

namespace GalExcleTools.Services;

internal static class StoryAssetFieldService
{
    public static int ReplaceForwardContiguousIndex(
        IReadOnlyList<StoryRow> rows,
        int startIndex,
        string fieldName,
        int replacementIndex)
    {
        if (fieldName is not ("BGindex" or "BGM" or "Scene"))
        {
            throw new InvalidOperationException($"素材字段不支持向后连续修改：{fieldName}");
        }

        if (startIndex < 0 || startIndex >= rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        var originalIndex = ParseAssetIndex(rows[startIndex].Get(fieldName));
        var replacementValue = replacementIndex.ToString(CultureInfo.InvariantCulture);
        var changedCount = 0;
        for (var index = startIndex; index < rows.Count; index++)
        {
            if (ParseAssetIndex(rows[index].Get(fieldName)) != originalIndex)
            {
                break;
            }

            rows[index].Set(fieldName, replacementValue);
            changedCount++;
        }

        return changedCount;
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
}
