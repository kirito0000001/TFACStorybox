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
    /// <summary>
    /// "其实是同一个素材改了名"的那些项。脚本会对它们做 rename（保留引用），
    /// 而这些**旧路径已经从删除组里摘掉了** —— 不然会被"删掉旧的"那一步清掉。
    /// </summary>
    List<UnrealSyncRenameEntry> Renames,
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

/// <summary>
/// 一条"其实是同一个素材改了名"的项。
///
/// 【为什么需要它】
/// 源文件改了名，老做法是"旧资产进删除组被删掉 + 新资产再导入一遍"——
/// 那会把 Unreal 里**所有引用旧资产的地方全部打断**。
/// 改名的正确做法是保留引用（零境那边专门有一个 OP_RENAME 干这个）。
///
/// 【怎么判出来的】
/// 不用新状态字段：state.SourceHashes 是"目标对象路径 → 源文件内容哈希"，
/// 而**文件改名内容不变**，所以新目标路径的哈希会等于某个"即将被删的旧路径"的哈希。
/// 内容改过的话哈希不同，就不算改名 —— 那种本该导新删旧，是对的。
/// </summary>
/// <param name="SourceObjectPath">旧的对象路径（<c>/Game/…/旧名.旧名</c>）。</param>
/// <param name="TargetObjectPath">新的对象路径。</param>
/// <param name="DisplayName">给人看的名字，进日志与对比表。</param>
internal sealed record UnrealSyncRenameEntry(
    string SourceObjectPath,
    string TargetObjectPath,
    string DisplayName);

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

    /// <summary>
    /// **执行后复扫验证**的逐项结果（脚本收尾时再扫一遍实际状态写进来的）。
    ///
    /// 为什么要它：光比计数判断不了"同步成功"—— 计数能对上、内容却是错的
    /// （导入了 27 个文件，但不是那 27 个）完全可能。
    /// 零境那边专门有一个 UnrealBridgeVerificationService 做这件事：执行完拿
    /// **重新扫描的两侧快照**逐项核对计划，核对不过就**拒绝写入成功状态**。
    /// 这里只做 Unreal 侧那一半（工具箱侧的文件本来就在本地，窗口自己看得见）。
    /// </summary>
    public List<UnrealSyncVerificationEntry> Verification { get; set; } = [];
}

/// <summary>复扫验证里的一条：计划要它变成什么样，实际扫出来是什么样。</summary>
internal sealed class UnrealSyncVerificationEntry
{
    /// <summary>给人看的名字，例如「导入 bg_01」。失败时直接报这个。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>被核对的对象路径（<c>/Game/…</c>）。</summary>
    public string ObjectPath { get; set; } = string.Empty;

    /// <summary>计划期望它在（导入项）还是期望它不在（删除项）。</summary>
    public bool ExpectPresent { get; set; }

    /// <summary>复扫时实际在不在。</summary>
    public bool Present { get; set; }

    /// <summary>这一条过没过（<c>Present == ExpectPresent</c>）。</summary>
    public bool Ok { get; set; }
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
