using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
}
