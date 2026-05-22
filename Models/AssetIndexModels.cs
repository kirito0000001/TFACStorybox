using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools;

internal sealed record AssetIndexSyncProgress(
    string Message,
    double Percent,
    int CompletedCsvFiles,
    int TotalCsvFiles,
    int ChangeCount,
    int WarningCount,
    string? CurrentCsvName);

internal sealed record AssetIndexSyncResult(
    string Title,
    int ScannedCsvCount,
    int ChangedCsvCount,
    int ChangeCount,
    int WarningCount,
    IReadOnlyList<string> ChangedCsvPaths,
    IReadOnlyList<AssetIndexChange> Changes,
    IReadOnlyList<AssetIndexWarning> Warnings);

internal sealed record AssetIndexChange(
    string ProjectName,
    string ChapterName,
    string ChapterCode,
    string CsvName,
    string RowName,
    int RowNumber,
    string ColumnName,
    string OldValue,
    string NewValue,
    string OldValueLabel,
    string NewValueLabel);

internal sealed record AssetIndexWarning(
    string ProjectName,
    string ChapterName,
    string ChapterCode,
    string CsvName,
    string RowName,
    int RowNumber,
    string ColumnName,
    string Message);

internal sealed record RelatedStoryCsvFile(
    string ProjectName,
    string ChapterName,
    string ChapterCode,
    string CsvPath);

internal sealed record StoryIndexRowContext(
    RelatedStoryCsvFile CsvFile,
    StoryRow Row,
    int RowIndex,
    List<AssetIndexChange> Changes,
    List<AssetIndexWarning> Warnings)
{
    public AssetIndexChange CreateChange(
        string columnName,
        string oldValue,
        string newValue,
        string oldValueLabel,
        string newValueLabel)
    {
        return new AssetIndexChange(
            CsvFile.ProjectName,
            CsvFile.ChapterName,
            CsvFile.ChapterCode,
            Path.GetFileName(CsvFile.CsvPath),
            Row.Get("Name"),
            RowIndex + 1,
            columnName,
            oldValue,
            newValue,
            oldValueLabel,
            newValueLabel);
    }

    public AssetIndexWarning CreateWarning(string columnName, string message)
    {
        return new AssetIndexWarning(
            CsvFile.ProjectName,
            CsvFile.ChapterName,
            CsvFile.ChapterCode,
            Path.GetFileName(CsvFile.CsvPath),
            Row.Get("Name"),
            RowIndex + 1,
            columnName,
            message);
    }
}

internal sealed record ChapterRepairProgress(
    string Message,
    double Percent,
    int CompletedCsvFiles,
    int TotalCsvFiles,
    int IssueCount,
    int FixedCount,
    string? CurrentCsvName);

internal sealed record ChapterRepairResult(
    string ProjectName,
    string ChapterName,
    string ChapterCode,
    int ScannedCsvCount,
    int IssueCount,
    int FixedCount,
    IReadOnlyList<string> ChangedCsvPaths,
    IReadOnlyList<ChapterRepairIssue> Issues)
{
    public int AutoFixableCount => Issues.Count(issue => issue.CanAutoFix);
}

internal sealed record ChapterRepairIssue(
    string ProjectName,
    string ChapterName,
    string ChapterCode,
    string CsvName,
    string RowName,
    int RowNumber,
    string ColumnName,
    string Message,
    bool CanAutoFix);

internal sealed record ChapterRepairAssetContext(
    int BackgroundCount,
    int BgmCount,
    int SceneCount,
    int FilterCount,
    IReadOnlyDictionary<string, string> CharacterAliases,
    IReadOnlyDictionary<string, CharacterRepairAssetCounts> CharacterAssets);

internal sealed record CharacterRepairAssetCounts(int ClothCount, int FaceCount, int AdornCount);
