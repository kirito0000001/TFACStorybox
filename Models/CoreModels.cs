using System;

namespace GalExcleTools;

internal sealed record ProjectInfo(
    string Name,
    string Code,
    string FolderName,
    string Path,
    string? ThumbnailPath,
    string AssetLibraryName,
    string? AssetLibraryFolderName,
    DateTime LastEditedAt);

internal sealed record ChapterInfo(
    string Name,
    string Code,
    string Type,
    string Path,
    DateTime LastEditedAt,
    int LastEditedRowIndex);

internal sealed record ChapterEditorInput(string Name, string Code, string Type);

internal sealed record ChapterRenamePlan(ChapterInfo Chapter, string NewCode, string NewPath);

internal sealed record ChapterTypeOption(string Kind, string DisplayName);

internal static class ChapterKind
{
    public const string MainThread = "MainThread";
    public const string Interlude = "Interlude";
    public const string Simulation = "Simulation";
    public const string EventActivity = "EventActivity";
    public const string WorldDialog = "WorldDialog";
    public const string Minecraft = "Minecraft";
}

internal static class ChapterTypes
{
    public static readonly ChapterTypeOption[] Options =
    [
        new(ChapterKind.MainThread, "主线剧情 / Main Thread"),
        new(ChapterKind.Interlude, "间章 / Interlude"),
        new(ChapterKind.Simulation, "养成 / Simulation"),
        new(ChapterKind.EventActivity, "活动关 / Event Activity"),
        new(ChapterKind.WorldDialog, "世界对话 / World Dialog"),
        new(ChapterKind.Minecraft, "我的世界 NPC 对话 / Minecraft")
    ];
}

internal sealed record MigrationResult(int FileCount, int DirectoryCount);

internal enum LogKind
{
    Info,
    User,
    Warning,
    Error
}
