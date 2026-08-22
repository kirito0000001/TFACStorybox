using System;
using System.Collections.Generic;

namespace GalExcleTools;

internal sealed class StoryDocumentFile
{
    public int SchemaVersion { get; set; } = 2;

    public string ChapterCode { get; set; } = string.Empty;

    public DateTimeOffset SavedAt { get; set; }

    public List<StoryDocumentSectionFile> Sections { get; set; } = [];

    public StoryChoiceNoteState ChoiceNotes { get; set; } = new();

    public List<StoryChoiceRoute> BranchRoutes { get; set; } = [];
}

internal sealed class StoryDocumentSectionFile
{
    public int Number { get; set; }

    public List<StoryDocumentLineFile> Lines { get; set; } = [];
}

internal sealed class StoryDocumentLineFile
{
    public string StableId { get; set; } = string.Empty;

    public Dictionary<string, string> Cells { get; set; } = new(StringComparer.Ordinal);

}
