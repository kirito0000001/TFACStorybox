using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

internal static class StoryBranchFlowService
{
    public static StoryChapterBranchFlow Build(StoryDocument document)
    {
        var targets = StoryBranchService.GetTargetOptions(document)
            .OrderBy(target => target.Section)
            .ToList();
        var issues = new List<string>();
        var optionsBySection = new Dictionary<int, List<StoryBranchFlowOption>>();
        var branchTargetToCommon = new Dictionary<int, int?>();
        var commonSections = new HashSet<int>();

        foreach (var pair in document.ChoiceNotes.Choices.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var sourceSection = StoryBranchService.FindChoiceSourceSection(document, pair.Key);
            if (sourceSection is null)
            {
                issues.Add($"选项函数 {pair.Key} 没有对应的触发行。");
                continue;
            }

            var explicitRoutes = document.BranchRoutes
                .Where(route => route.ManagedByToolbox &&
                                string.Equals(route.ChoiceFunction, pair.Key, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(route => route.OptionIndex);
            var explicitTargets = explicitRoutes.Values
                .Select(route => StoryBranchService.FindTargetSection(document, route.TargetSectionFirstRowId))
                .Where(section => section is not null)
                .Select(section => section!.Value)
                .Distinct()
                .OrderBy(section => section)
                .ToList();
            var convergenceSection = explicitTargets.Count == 0
                ? targets.FirstOrDefault(target => target.Section > sourceSection.Value)?.Section
                : targets.FirstOrDefault(target => target.Section > explicitTargets[^1])?.Section;
            foreach (var branchSection in explicitTargets)
            {
                branchTargetToCommon[branchSection] = convergenceSection;
            }
            if (convergenceSection is not null)
            {
                commonSections.Add(convergenceSection.Value);
            }

            var options = new List<StoryBranchFlowOption>();
            for (var optionIndex = 0; optionIndex < pair.Value.Count; optionIndex++)
            {
                explicitRoutes.TryGetValue(optionIndex, out var explicitRoute);
                var explicitTarget = explicitRoute is null
                    ? null
                    : StoryBranchService.FindTargetSection(document, explicitRoute.TargetSectionFirstRowId);
                if (explicitRoute is not null && explicitTarget is null)
                {
                    issues.Add($"{pair.Key} 的选项 {StoryBranchService.GetOptionKey(optionIndex)} 目标小节已不存在。");
                }

                options.Add(new StoryBranchFlowOption(
                    pair.Key,
                    optionIndex,
                    StoryBranchService.GetOptionKey(optionIndex),
                    pair.Value[optionIndex],
                    explicitTarget,
                    explicitTarget ?? convergenceSection,
                    explicitTarget is null && convergenceSection is not null));
            }

            optionsBySection[sourceSection.Value] = options;
        }

        var customSections = document.CustomBranchSectionFirstRowIds
            .Select(id => StoryBranchService.FindTargetSection(document, id))
            .Where(section => section is not null)
            .Select(section => section!.Value)
            .ToHashSet();
        var sections = new List<StoryBranchSectionFlow>();
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var options = optionsBySection.GetValueOrDefault(target.Section) ?? [];
            var nextSection = index + 1 < targets.Count ? targets[index + 1].Section : (int?)null;
            var defaultTarget = options.Count > 0 || customSections.Contains(target.Section)
                ? null
                : branchTargetToCommon.GetValueOrDefault(target.Section, nextSection);
            var kind = customSections.Contains(target.Section)
                ? StoryBranchSectionKind.Custom
                : options.Count > 0
                    ? StoryBranchSectionKind.Choice
                    : branchTargetToCommon.ContainsKey(target.Section)
                        ? StoryBranchSectionKind.Branch
                        : commonSections.Contains(target.Section)
                            ? StoryBranchSectionKind.Common
                            : nextSection is null
                                ? StoryBranchSectionKind.Terminal
                                : StoryBranchSectionKind.Sequential;
            sections.Add(new StoryBranchSectionFlow(
                target.Section,
                target.TargetSectionFirstRowId,
                target.FirstSentencePreview,
                kind,
                defaultTarget,
                options));
        }

        return new StoryChapterBranchFlow(sections, issues);
    }
}
