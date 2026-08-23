using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GalExcleTools.Services;

internal sealed class StoryChoiceManagerService
{
    private readonly StoryDocumentRepository _repository;

    public StoryChoiceManagerService(StoryDocumentRepository repository)
    {
        _repository = repository;
    }

    public StoryChoiceManagerSession CreateSession(ChapterInfo chapter)
    {
        var draft = new StoryDocument();
        draft.ReplaceWith(_repository.Load(chapter));
        return new StoryChoiceManagerSession(chapter, draft);
    }

    public void UpdateOption(
        StoryChoiceManagerSession session,
        string choiceFunction,
        int optionIndex,
        string text,
        string targetSectionFirstRowId)
    {
        if (!session.Draft.ChoiceNotes.Choices.TryGetValue(choiceFunction, out var notes) ||
            optionIndex < 0 || optionIndex >= notes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(optionIndex), "选项已经不存在。");
        }

        notes[optionIndex] = TextUtility.NormalizeFunctionChoiceNote(text);
        var targets = session.Draft.BranchRoutes
            .Where(route => string.Equals(route.ChoiceFunction, choiceFunction, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(route => route.OptionIndex, route => route.TargetSectionFirstRowId);
        targets[optionIndex] = targetSectionFirstRowId?.Trim() ?? string.Empty;
        var options = notes
            .Select((optionText, index) => new StoryChoiceEditorOption(
                optionText,
                targets.GetValueOrDefault(index) ?? string.Empty))
            .ToList();
        StoryBranchService.ReplaceRoutesForChoice(session.Draft, choiceFunction, options);
        session.RefreshFlow();
    }

    public void SetCustomSection(StoryChoiceManagerSession session, int section, bool isCustom)
    {
        StoryBranchService.SetCustomBranchSection(session.Draft, section, isCustom);
        session.RefreshFlow();
    }

    public IReadOnlyList<string> Validate(StoryChoiceManagerSession session)
    {
        var issues = session.Flow.Issues.ToList();
        foreach (var pair in session.Draft.ChoiceNotes.Choices)
        {
            for (var index = 0; index < pair.Value.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(pair.Value[index]))
                {
                    issues.Add($"{pair.Key} 的选项 {StoryBranchService.GetOptionKey(index)} 内容为空。");
                }
            }
        }

        return issues;
    }

    public void Save(StoryChoiceManagerSession session)
    {
        var issues = Validate(session);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        }

        _repository.Save(session.Chapter, session.Draft);
    }

    public static string GetSectionDisplayName(
        StoryChapterBranchFlow flow,
        StoryBranchSectionFlow section)
    {
        if (section.Options.Count > 0)
        {
            return $"选项{GetChoiceNumber(section.Options[0].ChoiceFunction)}";
        }

        var explicitBranch = flow.Sections
            .SelectMany(item => item.Options)
            .FirstOrDefault(option => option.ExplicitTargetSection == section.Section);
        if (explicitBranch is not null)
        {
            return $"选项{GetChoiceNumber(explicitBranch.ChoiceFunction)}-分支{explicitBranch.OptionIndex + 1}";
        }

        var common = flow.Sections
            .SelectMany(item => item.Options)
            .FirstOrDefault(option => option.UsesInferredCommon && option.EffectiveTargetSection == section.Section);
        if (common is not null)
        {
            return $"选项{GetChoiceNumber(common.ChoiceFunction)}-共通线";
        }

        var convergingBranch = flow.Sections.FirstOrDefault(item =>
            item.Kind == StoryBranchSectionKind.Branch &&
            item.DefaultTargetSection == section.Section);
        if (convergingBranch is not null)
        {
            var parentChoice = flow.Sections
                .SelectMany(item => item.Options)
                .FirstOrDefault(option => option.ExplicitTargetSection == convergingBranch.Section);
            if (parentChoice is not null)
            {
                return $"选项{GetChoiceNumber(parentChoice.ChoiceFunction)}-共通线";
            }
        }

        return section.Kind switch
        {
            StoryBranchSectionKind.Terminal => "章节结尾",
            StoryBranchSectionKind.Custom => "自定义逻辑",
            StoryBranchSectionKind.Common => "共通线",
            _ => $"剧情段落{section.Section}"
        };
    }

    public static string GetOptionTargetDisplayName(
        StoryChapterBranchFlow flow,
        StoryBranchFlowOption option)
    {
        var targetSection = option.ExplicitTargetSection ?? option.EffectiveTargetSection;
        var target = targetSection is null
            ? null
            : flow.Sections.FirstOrDefault(section => section.Section == targetSection.Value);
        return target is null
            ? "章节结束"
            : GetSectionDisplayName(flow, target);
    }

    private static int GetChoiceNumber(string choiceFunction)
    {
        var match = Regex.Match(choiceFunction, @"-Choice(?<index>\d+)$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["index"].Value, out var index)
            ? index
            : 1;
    }
}
