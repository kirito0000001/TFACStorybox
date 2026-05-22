using System;
using System.Collections.Generic;

namespace GalExcleTools;

internal sealed record UnrealSyncContext(
    string EditorPath,
    string UnrealProjectPath,
    string TargetContentFolderPath,
    string TargetAssetRoot,
    ProjectInfo Project,
    AssetLibraryInfo AssetLibrary);

internal sealed record UnrealSyncProgressUpdate(string Message, double Percent);

internal sealed record UnrealProjectBinding(string? EnginePath, string? ProjectPath, string? ContentFolderPath)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(EnginePath) &&
        !string.IsNullOrWhiteSpace(ProjectPath) &&
        !string.IsNullOrWhiteSpace(ContentFolderPath);
}

internal sealed record UnrealSyncChangePlan(
    List<UnrealSyncImportGroup> ImportGroups,
    List<UnrealStoryTableSyncEntry> StoryTables,
    List<UnrealAssetIndexTableSyncEntry> AssetIndexTables,
    bool LustrationChanged,
    List<UnrealLustrationSyncEntry> LustrationRows,
    string LustrationHash,
    string AssetIndexTablesHash,
    int TotalChangedItems,
    string Summary,
    List<string> PlanItems)
{
    public bool HasChanges => TotalChangedItems > 0;
}

internal sealed record UnrealSyncImportGroup(string Destination, List<string> Files);

internal sealed record UnrealStoryTableSource(ChapterInfo Chapter, List<StoryTableCsvEntry> CsvEntries);

internal sealed record UnrealStoryTableSyncEntry(
    string CsvPath,
    string TableAsset,
    string RowStruct,
    List<string> LegacyTableAssets);

internal sealed record UnrealAssetIndexTableSyncEntry(
    string CsvPath,
    string TableAsset,
    string RowStruct,
    string SourceHash);

internal sealed record UnrealLinearColor(double R, double G, double B, double A);

internal sealed record UnrealLustrationSyncEntry(
    string Key,
    string Name,
    UnrealLinearColor Color,
    List<string> Cloth,
    List<string> Face,
    List<string?> Adorn);

internal sealed record UnrealSyncResult(int ExitCode, string ManifestPath, string ScriptPath, string Output);

internal sealed class UnrealSyncState
{
    public DateTimeOffset LastSyncedAt { get; set; }

    public string? LustrationHash { get; set; }

    public string? AssetIndexTablesHash { get; set; }
}
