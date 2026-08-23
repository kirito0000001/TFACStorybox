namespace GalExcleTools;

internal sealed class StoryChoiceManagerSession
{
    public StoryChoiceManagerSession(ChapterInfo chapter, StoryDocument draft)
    {
        Chapter = chapter;
        Draft = draft;
        Flow = Services.StoryBranchFlowService.Build(draft);
    }

    public ChapterInfo Chapter { get; }

    public StoryDocument Draft { get; }

    public StoryChapterBranchFlow Flow { get; private set; }

    public void RefreshFlow()
    {
        Flow = Services.StoryBranchFlowService.Build(Draft);
    }
}

internal sealed record StoryChoiceManagerSectionItem(
    int Section,
    string Title,
    string FirstSentence,
    string Status,
    StoryBranchSectionFlow Flow);

internal sealed record StoryChoiceManagerOptionItem(
    string OptionKey,
    string Text,
    string ChoiceFunction,
    string TargetSummary,
    StoryBranchFlowOption? FlowOption);

internal sealed record StoryChoiceManagerTargetItem(
    string FirstRowId,
    int? Section,
    string DisplayName);
