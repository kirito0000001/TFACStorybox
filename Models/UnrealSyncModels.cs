using System;
using System.Collections.Generic;
using GalExcleTools.Services;

namespace GalExcleTools;

internal sealed record UnrealSyncContext(
    string EditorPath,
    string UnrealProjectPath,
    string TargetContentFolderPath,
    string TargetAssetRoot,
    ProjectInfo Project,
    AssetLibraryInfo AssetLibrary);

internal sealed record UnrealSyncProgressUpdate(string Message, double Percent)
{
    /// <summary>
    /// 脚本报的阶段名（进度 JSON 里的 <c>Stage</c>，例如 ReadManifest / UpdateVoiceMaps）。
    ///
    /// 原来这个字段**被丢掉了**：脚本一直在写，而 C# 这边只留 Message + Percent，
    /// 于是界面上永远只有一句话、看不出"现在到哪一段"。
    /// </summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>已完成项数（脚本报的 <c>Completed</c>）。</summary>
    public int Completed { get; init; }

    /// <summary>总项数（脚本报的 <c>Total</c>）；0 表示这一步没有可数的项。</summary>
    public int Total { get; init; }

    /// <summary>「3/12」这种计数文案；脚本没报总数时是空串。</summary>
    public string CountText => Total > 0 ? $"{Completed}/{Total}" : string.Empty;
}

internal sealed record UnrealProjectBinding(string? EnginePath, string? ProjectPath, string? ContentFolderPath)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(EnginePath) &&
        !string.IsNullOrWhiteSpace(ProjectPath) &&
        !string.IsNullOrWhiteSpace(ContentFolderPath);
}

internal sealed record UnrealSyncChangePlan(
    List<UnrealSyncImportGroup> ImportGroups,
    List<UnrealSyncDeleteGroup> DeleteGroups,
    List<UnrealStoryTableSyncEntry> StoryTables,
    List<UnrealAssetIndexTableSyncEntry> AssetIndexTables,
    UnrealSyncVariablePlan VariablePlan,
    bool StoryBranchesChanged,
    List<UnrealStoryBranchSourceSyncEntry> StoryBranches,
    string StoryBranchesHash,
    bool VoiceMapsChanged,
    List<UnrealVoiceMapSyncEntry> VoiceMaps,
    string VoiceMapsHash,
    bool LustrationChanged,
    List<UnrealLustrationSyncEntry> LustrationRows,
    bool PortraitsChanged,
    List<UnrealLustrationSyncEntry> PortraitRows,
    string LustrationHash,
    string PortraitsHash,
    string AssetIndexTablesHash,
    Dictionary<string, string> SourceHashes,
    bool IsFullSync,
    int TotalChangedItems,
    string Summary,
    List<string> ChangeItems,
    List<string> UnchangedItems)
{
    public bool HasChanges => TotalChangedItems > 0;
}

internal sealed record UnrealSyncImportGroup(string Destination, List<string> Files);

internal sealed record UnrealSyncDeleteGroup(string Destination, List<string> Assets);

internal sealed record UnrealStoryTableSource(ChapterInfo Chapter, List<StoryTableCsvEntry> CsvEntries);

internal sealed record UnrealStoryTableSyncEntry(
    string CsvPath,
    string TableAsset,
    string RowStruct,
    List<string> LegacyTableAssets,
    string ChapterCode,
    int Section);

internal sealed record UnrealAssetIndexTableSyncEntry(
    string CsvPath,
    string TableAsset,
    string RowStruct,
    string SourceHash);

internal sealed record UnrealStoryBranchRouteSyncEntry(
    string ChoiceFunction,
    string OptionKey,
    string OptionText,
    string TargetTable,
    bool RequiresChoiceCondition);

internal sealed record UnrealStoryBranchSourceSyncEntry(
    string ManagerRow,
    string SourceTable,
    List<UnrealStoryBranchRouteSyncEntry> Routes);

internal sealed record UnrealVoiceMapSyncEntry(
    string TableAsset,
    List<string?> Waves,
    int AssignedCount);

internal sealed record UnrealLinearColor(double R, double G, double B, double A);

internal sealed record UnrealLustrationSyncEntry(
    string Key,
    string Name,
    UnrealLinearColor Color,
    List<string> Cloth,
    List<string> Face,
    List<string?> Adorn);

internal sealed record UnrealSyncResult(
    int ExitCode,
    string ManifestPath,
    string ScriptPath,
    string ProgressPath,
    string ResultPath,
    string Output,
    UnrealSyncExecutionReport? Report);

internal sealed class UnrealSyncExecutionReport
{
    public int ProtocolVersion { get; set; }

    public bool Completed { get; set; }

    public bool Success { get; set; }

    public int ImportedCount { get; set; }

    public int DeletedCount { get; set; }

    public int StoryTableCount { get; set; }

    public int AssetIndexTableCount { get; set; }

    public bool StoryBranchesUpdated { get; set; }

    public bool VoiceMapsUpdated { get; set; }

    public bool LustrationUpdated { get; set; }

    public bool PortraitsUpdated { get; set; }

    public List<string> Errors { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

internal sealed class UnrealSyncProgressFile
{
    public int ProtocolVersion { get; set; }

    public string? Stage { get; set; }

    public string? Message { get; set; }

    public double Percent { get; set; }

    public int Completed { get; set; }

    public int Total { get; set; }
}

internal sealed class UnrealSyncState
{
    public int StateVersion { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }

    public string? LustrationHash { get; set; }

    public string? PortraitsHash { get; set; }

    public string? AssetIndexTablesHash { get; set; }

    public string? VoiceMapsHash { get; set; }

    public string? StoryBranchesHash { get; set; }

    public string? StoryBranchesAssetHash { get; set; }

    public Dictionary<string, string> SourceHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
