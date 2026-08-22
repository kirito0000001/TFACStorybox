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
            .Select(section => new StoryChoiceTargetOption(
                GetSectionFirstRowId(document, section) ?? string.Empty,
                section,
                $"第 {section} 小节"))
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
        var targetId = GetSectionFirstRowId(document, section);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return [];
        }

        var result = new List<StoryBranchInspectorItem>();
        foreach (var route in document.BranchRoutes
                     .Where(route => string.Equals(
                         route.TargetSectionFirstRowId,
                         targetId,
                         StringComparison.OrdinalIgnoreCase))
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
                    ? $"进入第 {section} 小节"
                    : $"第 {sourceSection.Value} 小节 → 第 {section} 小节"));
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

    private static int? FindChoiceSourceSection(StoryDocument document, string choiceFunction)
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
