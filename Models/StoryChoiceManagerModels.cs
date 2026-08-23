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
