using System;
using System.Collections.Generic;

namespace GalExcleTools;

internal sealed class StoryDocumentFile
{
    public int SchemaVersion { get; set; } = 3;

    public string ChapterCode { get; set; } = string.Empty;

    public DateTimeOffset SavedAt { get; set; }

    public List<StoryDocumentSectionFile> Sections { get; set; } = [];

    public StoryChoiceNoteState ChoiceNotes { get; set; } = new();

    public List<StoryChoiceRoute> BranchRoutes { get; set; } = [];

    public List<string> CustomBranchSectionFirstRowIds { get; set; } = [];
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

    /// <summary>
    /// 素材引用的身份指纹（列名 → 文件名）。**只在这份 JSON 里**，
    /// 不写进剧情 CSV、游戏侧看不到 —— 见 StoryRow.AssetIdentities 的说明。
    /// </summary>
    public Dictionary<string, string> AssetIdentities { get; set; } = new(StringComparer.Ordinal);
}
