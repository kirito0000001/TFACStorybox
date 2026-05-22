using System;

namespace GalExcleTools;

internal sealed class AppSettings
{
    public string? ProjectRootPath { get; set; }

    public bool ShowWorkspacePath { get; set; } = true;

    public bool LogEnabled { get; set; } = true;

    public bool LogUserOperations { get; set; } = true;

    public bool LogWarnings { get; set; } = true;

    public bool LogErrors { get; set; } = true;

    public bool UiSoundEnabled { get; set; }

    public double AssetLibraryScrollSpeedMultiplier { get; set; } = 1.5;

    public double StoryTextFontSize { get; set; } = 20;

    public bool ShowFullStoryChapterLength { get; set; }

    public string? UnrealEnginePath { get; set; }

    public string? UnrealProjectPath { get; set; }

    public string? UnrealContentFolderPath { get; set; }

    public string? UnrealToolProjectFolderName { get; set; }
}

internal sealed class ProjectMeta
{
    public string? ProjectName { get; set; }

    public string? ProjectCode { get; set; }

    public string? ThumbnailFileName { get; set; }

    public string? AssetLibraryName { get; set; }

    public string? AssetLibraryFolderName { get; set; }

    public string? UnrealEnginePath { get; set; }

    public string? UnrealProjectPath { get; set; }

    public string? UnrealContentFolderPath { get; set; }

    public DateTime LastEditedAt { get; set; }
}

internal sealed class ChapterMeta
{
    public string? ChapterName { get; set; }

    public string? ChapterCode { get; set; }

    public string? ChapterType { get; set; }

    public DateTime LastEditedAt { get; set; }

    public int LastEditedRowIndex { get; set; }
}
