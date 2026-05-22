using System;
using System.Linq;

namespace GalExcleTools.Services;

internal static class StoryCharacterSlotService
{
    public static StoryCharacterSlotClipboard CreateClipboard(StoryRow row, int slotIndex)
    {
        return new StoryCharacterSlotClipboard(
            row.Get(GetCharacterColumn(slotIndex)),
            row.Get(GetLayerColumn(slotIndex, "Body")),
            row.Get(GetLayerColumn(slotIndex, "Face")),
            row.Get(GetLayerColumn(slotIndex, "Adorn")),
            row.Get(GetLayerColumn(slotIndex, "Vfx")));
    }

    public static bool MatchesClipboard(StoryRow row, int slotIndex, StoryCharacterSlotClipboard clipboard)
    {
        return string.Equals(row.Get(GetCharacterColumn(slotIndex)), clipboard.Character, StringComparison.Ordinal) &&
            string.Equals(row.Get(GetLayerColumn(slotIndex, "Body")), clipboard.Body, StringComparison.Ordinal) &&
            string.Equals(row.Get(GetLayerColumn(slotIndex, "Face")), clipboard.Face, StringComparison.Ordinal) &&
            string.Equals(row.Get(GetLayerColumn(slotIndex, "Adorn")), clipboard.Adorn, StringComparison.Ordinal) &&
            string.Equals(row.Get(GetLayerColumn(slotIndex, "Vfx")), clipboard.Vfx, StringComparison.Ordinal);
    }

    public static void ApplyClipboard(StoryRow row, int slotIndex, StoryCharacterSlotClipboard clipboard)
    {
        row.Set(GetCharacterColumn(slotIndex), clipboard.Character);
        row.Set(GetLayerColumn(slotIndex, "Body"), clipboard.Body);
        row.Set(GetLayerColumn(slotIndex, "Face"), clipboard.Face);
        row.Set(GetLayerColumn(slotIndex, "Adorn"), clipboard.Adorn);
        row.Set(GetLayerColumn(slotIndex, "Vfx"), clipboard.Vfx);
    }

    public static bool IsEmpty(StoryRow row, int slotIndex)
    {
        return string.IsNullOrWhiteSpace(row.Get(GetCharacterColumn(slotIndex))) &&
            ParseInt(row.Get(GetLayerColumn(slotIndex, "Body"))) == 0 &&
            ParseInt(row.Get(GetLayerColumn(slotIndex, "Face"))) == 0 &&
            ParseInt(row.Get(GetLayerColumn(slotIndex, "Adorn"))) == 0 &&
            ParseInt(row.Get(GetLayerColumn(slotIndex, "Vfx"))) == 0;
    }

    public static void ResetLayerColumns(StoryRow row, int slotIndex)
    {
        ResetLayerColumnsIfNeeded(row, slotIndex);
    }

    public static bool ResetLayerColumnsIfNeeded(StoryRow row, int slotIndex)
    {
        var changed = false;
        changed |= SetCellIfChanged(row, GetLayerColumn(slotIndex, "Body"), "0");
        changed |= SetCellIfChanged(row, GetLayerColumn(slotIndex, "Face"), "0");
        changed |= SetCellIfChanged(row, GetLayerColumn(slotIndex, "Adorn"), "0");
        changed |= SetCellIfChanged(row, GetLayerColumn(slotIndex, "Vfx"), "0");
        return changed;
    }

    public static bool SetCellIfChanged(StoryRow row, string columnName, string value)
    {
        if (string.Equals(row.Get(columnName), value, StringComparison.Ordinal))
        {
            return false;
        }

        row.Set(columnName, value);
        return true;
    }

    public static string GetCharacterColumn(int slotIndex)
    {
        return slotIndex == 0 ? "TalkChar" : $"Chara{slotIndex}";
    }

    public static string GetLayerColumn(int slotIndex, string fieldPrefix)
    {
        return slotIndex == 0 ? $"Talk{fieldPrefix}" : $"{fieldPrefix}{slotIndex}";
    }

    public static string GetSlotDisplayName(int slotIndex)
    {
        return slotIndex == 0 ? "当前说话人" : $"{slotIndex}号位";
    }

    public static string FormatClipboard(StoryCharacterSlotClipboard data)
    {
        var character = string.IsNullOrWhiteSpace(data.Character) ? "无角色" : data.Character;
        return $"{character} / 服装 {data.Body} / 表情 {data.Face} / 装饰 {data.Adorn} / 滤镜 {data.Vfx}";
    }

    public static bool ContainsCjk(string value)
    {
        return value.Any(ch =>
            ch is >= '\u3400' and <= '\u4DBF' ||
            ch is >= '\u4E00' and <= '\u9FFF' ||
            ch is >= '\uF900' and <= '\uFAFF');
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }
}
