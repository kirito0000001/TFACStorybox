using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

internal static class StoryBranchService
{
    public static string GetOptionKey(int optionIndex)
    {
        var value = Math.Max(0, optionIndex) + 1;
        var characters = new Stack<char>();
        while (value > 0)
        {
            value--;
            characters.Push((char)('A' + value % 26));
            value /= 26;
        }

        return new string(characters.ToArray());
    }

    public static string? GetSectionFirstRowId(StoryDocument document, int section)
    {
        for (var index = 0; index < document.Rows.Count; index++)
        {
            if (GetSectionAtRowIndex(document, index) == section)
            {
                return document.Rows[index].StableId;
            }
        }

        return null;
    }

    public static List<StoryChoiceTargetOption> GetTargetOptions(StoryDocument document)
    {
        return Enumerable.Range(0, document.Rows.Count)
            .Select(index => GetSectionAtRowIndex(document, index))
            .Distinct()
            .OrderBy(section => section)
            .Select(section =>
            {
                var firstRowIndex = Enumerable.Range(0, document.Rows.Count)
                    .First(index => GetSectionAtRowIndex(document, index) == section);
                var firstRow = document.Rows[firstRowIndex];
                return new StoryChoiceTargetOption(
                    GetSectionFirstRowId(document, section) ?? string.Empty,
                    section,
                    StorySectionDisplay.FormatName(section),
                    FirstSentencePreview: TextUtility.NormalizeSingleLineText(firstRow.Get("Tesxt")));
            })
            .Where(option => !string.IsNullOrWhiteSpace(option.TargetSectionFirstRowId))
            .ToList();
    }

    public static void ReplaceRoutesForChoice(
        StoryDocument document,
        string choiceFunction,
        IReadOnlyList<StoryChoiceEditorOption> options)
    {
        document.BranchRoutes.RemoveAll(route =>
            string.Equals(route.ChoiceFunction, choiceFunction, StringComparison.OrdinalIgnoreCase));
        for (var optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            var targetId = options[optionIndex].TargetSectionFirstRowId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetId))
            {
                continue;
            }

            document.BranchRoutes.Add(new StoryChoiceRoute(
                choiceFunction,
                optionIndex,
                GetOptionKey(optionIndex),
                targetId));
        }
    }

    public static List<StoryBranchInspectorItem> GetRoutesForSection(
        StoryDocument document,
        int section)
    {
        if (GetSectionFirstRowId(document, section) is null)
        {
            return [];
        }

        var result = new List<StoryBranchInspectorItem>();
        foreach (var route in document.BranchRoutes
                     .Where(route => FindTargetSection(document, route.TargetSectionFirstRowId) == section)
                     .OrderBy(route => route.ChoiceFunction, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(route => route.OptionIndex))
        {
            var content = document.ChoiceNotes.Choices.TryGetValue(route.ChoiceFunction, out var notes) &&
                          route.OptionIndex >= 0 &&
                          route.OptionIndex < notes.Count
                ? notes[route.OptionIndex]
                : string.Empty;
            var sourceSection = FindChoiceSourceSection(document, route.ChoiceFunction);
            result.Add(new StoryBranchInspectorItem(
                $"选项 {route.OptionKey}",
                content,
                sourceSection is null
                    ? $"进入{StorySectionDisplay.FormatName(section)}"
                    : $"{StorySectionDisplay.FormatName(sourceSection.Value)} → {StorySectionDisplay.FormatName(section)}"));
        }

        return result;
    }

    public static void RemoveRoutesForChoices(StoryDocument document, IEnumerable<string> choiceFunctions)
    {
        var targets = choiceFunctions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        document.BranchRoutes.RemoveAll(route => targets.Contains(route.ChoiceFunction));
    }

    public static void RemapChoiceFunction(StoryDocument document, string oldValue, string newValue)
    {
        for (var index = 0; index < document.BranchRoutes.Count; index++)
        {
            var route = document.BranchRoutes[index];
            if (string.Equals(route.ChoiceFunction, oldValue, StringComparison.OrdinalIgnoreCase))
            {
                document.BranchRoutes[index] = route with { ChoiceFunction = newValue };
            }
        }
    }

    public static int? FindChoiceSourceSection(StoryDocument document, string choiceFunction)
    {
        for (var index = 0; index < document.Rows.Count; index++)
        {
            if (StoryFunctionService.SplitFunctionValues(document.Rows[index].Get("Custom"))
                .Any(function => string.Equals(function, choiceFunction, StringComparison.OrdinalIgnoreCase)))
            {
                return GetSectionAtRowIndex(document, index);
            }
        }

        return null;
    }

    public static int? FindTargetSection(StoryDocument document, string targetSectionFirstRowId)
    {
        var rowIndex = document.Rows.FindIndex(row => string.Equals(
            row.StableId,
            targetSectionFirstRowId,
            StringComparison.OrdinalIgnoreCase));
        return rowIndex < 0 ? null : GetSectionAtRowIndex(document, rowIndex);
    }

    /// <summary>
    /// 删除某一句之后，把「指向这一行的小节锚点」挪到接替它的那一行。
    ///
    /// 【为什么必须有这一步】
    /// 选项目标小节和「自定义跳转逻辑」标记，存的都是**小节第一行的 StableId**。
    /// 那一行一旦被删掉，引用就成了悬空 ID —— 而且没有任何一步会报错：
    /// - 选项编辑器的目标下拉里找不到它 → 悄悄退回「进入共通线」，确认一次分支就没了；
    /// - 选项管理器把它当校验错误 → 整章选项直接保存不了；
    /// - 自定义跳转标记查不到 → 开关静默变回「自动同步」，
    ///   下次同步 AllRoad 就把手工写的那套盖掉。
    ///
    /// 【接手规则】
    /// - 选项目标：一律改指向接替行。接替行占的就是被删行的位置，
    ///   小节通常还在原地，指过去仍然落在同一节里；
    ///   被删行是「只有一句的小节」时，接替行是后面那一节的开头 ——
    ///   也比留个悬空 ID 强（悬空会让整章选项存不进去）。
    /// - 自定义跳转标记：**只有接替行还在被删行的那一小节里**才跟着挪。
    ///   那一节已经整个不存在的时候跟着挪，等于把「保留手工设置」平白罩到下一节头上，
    ///   所以这里直接丢掉。
    /// </summary>
    /// <returns>是否真的改动了引用。</returns>
    public static bool RepointSectionAnchorsAfterRowDelete(
        StoryDocument document,
        string deletedRowStableId,
        int deletedRowSection,
        string? replacementRowStableId)
    {
        var deletedId = deletedRowStableId?.Trim() ?? string.Empty;
        if (deletedId.Length == 0)
        {
            return false;
        }

        var replacementId = string.IsNullOrWhiteSpace(replacementRowStableId)
            ? null
            : replacementRowStableId.Trim();
        var changed = false;

        var repointedRoutes = new List<StoryChoiceRoute>(document.BranchRoutes.Count);
        foreach (var route in document.BranchRoutes)
        {
            if (!string.Equals(
                    route.TargetSectionFirstRowId?.Trim() ?? string.Empty,
                    deletedId,
                    StringComparison.OrdinalIgnoreCase))
            {
                repointedRoutes.Add(route);
                continue;
            }

            changed = true;
            if (replacementId is not null)
            {
                repointedRoutes.Add(route with { TargetSectionFirstRowId = replacementId });
            }
            // 接替行为空 = 这一行是整章唯一的一句（删除时被替换成空白行）：无从接手，丢掉路线，
            // 不留悬空 ID。
        }

        document.BranchRoutes.Clear();
        document.BranchRoutes.AddRange(repointedRoutes);

        if (document.CustomBranchSectionFirstRowIds.Remove(deletedId))
        {
            changed = true;
            if (replacementId is not null &&
                GetSectionOfRowStableId(document, replacementId) == deletedRowSection)
            {
                document.CustomBranchSectionFirstRowIds.Add(replacementId);
            }
        }

        return changed;
    }

    private static int? GetSectionOfRowStableId(StoryDocument document, string stableId)
    {
        var rowIndex = document.Rows.FindIndex(row => string.Equals(
            row.StableId,
            stableId,
            StringComparison.OrdinalIgnoreCase));
        return rowIndex < 0 ? null : GetSectionAtRowIndex(document, rowIndex);
    }

    public static bool IsCustomBranchSection(StoryDocument document, int section)
    {
        var firstRowId = GetSectionFirstRowId(document, section);
        return firstRowId is not null && document.CustomBranchSectionFirstRowIds.Contains(firstRowId);
    }

    public static void SetCustomBranchSection(StoryDocument document, int section, bool isCustom)
    {
        var firstRowId = GetSectionFirstRowId(document, section);
        if (firstRowId is null)
        {
            return;
        }

        document.CustomBranchSectionFirstRowIds.RemoveWhere(id =>
            FindTargetSection(document, id) == section);
        if (isCustom)
        {
            document.CustomBranchSectionFirstRowIds.Add(firstRowId);
        }
    }

    private static int GetSectionAtRowIndex(StoryDocument document, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= document.Rows.Count)
        {
            return 1;
        }

        var rowName = document.Rows[rowIndex].Get("Name");
        return document.RowSections.TryGetValue(rowName, out var section)
            ? Math.Max(1, section)
            : 1;
    }
}
