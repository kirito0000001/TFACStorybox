using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

internal static class StoryFunctionListService
{
    public static List<string> GetValues(StoryRow row)
    {
        return StoryFunctionService.SplitFunctionValues(row.Get("Custom")).ToList();
    }

    public static int Add(StoryRow row, string value)
    {
        var values = GetValues(row);
        values.Add(ValidateToken(value));
        SetValues(row, values);
        return values.Count - 1;
    }

    public static bool Move(StoryRow row, int fromIndex, int toIndex)
    {
        var values = GetValues(row);
        ValidateIndex(values, fromIndex);
        ValidateIndex(values, toIndex);
        if (fromIndex == toIndex)
        {
            return false;
        }

        var value = values[fromIndex];
        values.RemoveAt(fromIndex);
        values.Insert(toIndex, value);
        SetValues(row, values);
        return true;
    }

    public static int InsertAfter(StoryRow row, int selectedIndex, string value)
    {
        var values = GetValues(row);
        var insertIndex = selectedIndex >= 0 && selectedIndex < values.Count
            ? selectedIndex + 1
            : values.Count;
        values.Insert(insertIndex, ValidateToken(value));
        SetValues(row, values);
        return insertIndex;
    }

    public static bool ReplaceAt(StoryRow row, int index, string value)
    {
        var values = GetValues(row);
        ValidateIndex(values, index);
        var replacement = ValidateToken(value);
        if (string.Equals(values[index], replacement, StringComparison.Ordinal))
        {
            return false;
        }

        values[index] = replacement;
        SetValues(row, values);
        return true;
    }

    public static string CopyAt(StoryRow row, int index)
    {
        var values = GetValues(row);
        ValidateIndex(values, index);
        return values[index];
    }

    public static string RemoveAt(StoryRow row, int index)
    {
        var values = GetValues(row);
        ValidateIndex(values, index);
        var removed = values[index];
        values.RemoveAt(index);
        SetValues(row, values);
        return removed;
    }

    public static List<string> Clear(StoryRow row)
    {
        var removed = GetValues(row);
        row.Set("Custom", string.Empty);
        return removed;
    }

    private static void SetValues(StoryRow row, IReadOnlyList<string> values)
    {
        row.Set("Custom", string.Join("/", values));
    }

    private static string ValidateToken(string value)
    {
        var token = value.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("函数值不能为空。", nameof(value));
        }
        if (token.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("单个函数值不能包含 / 分隔符。", nameof(value));
        }

        return token;
    }

    private static void ValidateIndex(IReadOnlyList<string> values, int index)
    {
        if (index < 0 || index >= values.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
