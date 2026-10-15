using System;
using System.Collections.Generic;

namespace GalExcleTools;

/// <summary>
/// 章节编辑包的清单（包里的 <c>chapter-package.json</c>）。
///
/// 【这个包是干什么的】
/// "两边分工写同一章"的对接件：我导出这一章的**剧情数据**给另一边编辑，
/// 他编完导出回来，我这边做差异对比、选一方合并。
/// 所以包里**不放素材**（两边素材一致），只放剧情主数据 + 本章 TODO 备注 + 这份清单。
///
/// 【为什么要有清单】
/// 三件事：
///   1. 让人（和程序）一眼认出这是什么包 —— <see cref="PackageType"/> + <see cref="SchemaVersion"/>；
///   2. 导入前先做基本校验，不必等到把文档反序列化才炸 —— 章节代号 / 文档 schema / 行数 / 小节数；
///   3. 提供"两边是不是从同一份出发的"这个判断依据 —— <see cref="StoryDocumentSha256"/>
///      （做法照 AI 剧情填表那套：导出时记下文档指纹）。
///
/// 【导入时要用的身份】
/// 逐句对齐靠的是**行的 StableId**（剧情行和 TODO 备注的定位键都是它）。
/// 只要两边是从同一份文档分出去的，同一个 StableId 在两边指的就是同一句 —— 这是
/// "逐句比对、逐句选边"能成立的前提。清单里的 <see cref="PackageId"/> 则用来记
/// "这个包我导入过没有"。
/// </summary>
internal sealed class ChapterPackageManifest
{
    /// <summary>包类型标识。别人拿到一个 zip 时，靠它认出这是哪路货。</summary>
    public const string PackageTypeValue = "TFAC.ChapterEdit";

    /// <summary>包格式版本。以后加减字段时靠它决定怎么读。</summary>
    public int SchemaVersion { get; set; } = 1;

    public string PackageType { get; set; } = PackageTypeValue;

    /// <summary>这一次导出的唯一编号（GUID），导入时用来记"这个包导过没有"。</summary>
    public string PackageId { get; set; } = string.Empty;

    public DateTimeOffset ExportedAt { get; set; }

    /// <summary>导出方用的工具箱版本 —— 对方工具版本比这老时，能看出该不该提醒他升级。</summary>
    public string ToolVersion { get; set; } = string.Empty;

    public string ChapterCode { get; set; } = string.Empty;

    public string ChapterName { get; set; } = string.Empty;

    /// <summary>章节类型（<see cref="ChapterKind"/> 里的字符串），例如 MainThread。</summary>
    public string ChapterType { get; set; } = string.Empty;

    /// <summary>包里那份 <c>story.document.json</c> 的 schema 号（当前是 3）。</summary>
    public int DocumentSchemaVersion { get; set; }

    /// <summary>导出时那份文档文件的 SHA-256（大写十六进制）。判断"两边同源"用。</summary>
    public string StoryDocumentSha256 { get; set; } = string.Empty;

    public int RowCount { get; set; }

    /// <summary>小节数（内部小节号的最大值；行数为 0 时是 0）。</summary>
    public int SectionCount { get; set; }

    /// <summary>带走的 TODO 备注条数。</summary>
    public int TodoCount { get; set; }

    /// <summary>包里都有哪些文件（导入时按它检查有没有缺件）。</summary>
    public List<string> Files { get; set; } = [];
}

/// <summary>导出结果：包落在哪、清单长什么样（界面拿它显示"多少行 / 多少小节 / 多少备注"）。</summary>
internal sealed record ChapterPackageExportResult(
    string PackagePath,
    ChapterPackageManifest Manifest);

/// <summary>拆包结果：清单 + 包里的文档 + 包里的备注。</summary>
internal sealed record ChapterPackageContent(
    string PackagePath,
    ChapterPackageManifest Manifest,
    StoryDocumentFile Document,
    List<StoryTodoNote> Todos);

/// <summary>一条差异属于哪一类。</summary>
internal enum ChapterMergeItemKind
{
    /// <summary>两边都有这一句，但内容不一样（谁改的分不出来 —— 二方对比的固有歧义）。</summary>
    Modified,

    /// <summary>只有包里（对方那边）有：可能是他新增的，也可能是这边删了。分不清，所以默认不动。</summary>
    OnlyInPackage,

    /// <summary>只有本机有：可能是这边新增的，也可能是对方删了。同上，默认不动。</summary>
    OnlyLocal
}

/// <summary>这一条用哪边。默认一律 <see cref="KeepLocal"/> —— 合并是"我这边收下对方的改动"，
/// 没点名的地方就不该被动。</summary>
internal enum ChapterMergeChoice
{
    KeepLocal,
    UsePackage
}

/// <summary>剧情行的一条差异（按句）。</summary>
internal sealed class ChapterMergeItem
{
    public required ChapterMergeItemKind Kind { get; init; }

    /// <summary>对齐键：行的 StableId。</summary>
    public required string StableId { get; init; }

    /// <summary>本机第几句（0 = 本机没有这一句）。</summary>
    public int LocalRowNumber { get; init; }

    /// <summary>包里第几句（0 = 包里没有这一句）。</summary>
    public int PackageRowNumber { get; init; }

    public int LocalSection { get; init; }

    public int PackageSection { get; init; }

    /// <summary>列表左边那栏：本机这一句长什么样（"说话人：文本"）。</summary>
    public string LocalSummary { get; init; } = string.Empty;

    /// <summary>列表右边那栏：包里这一句长什么样。</summary>
    public string PackageSummary { get; init; } = string.Empty;

    /// <summary>哪些字段不一样（中文名）。只有 <see cref="ChapterMergeItemKind.Modified"/> 才有。</summary>
    public List<string> ChangedFields { get; init; } = [];

    /// <summary>
    /// 界面上的选择。<b>可变</b>：用户逐条切换，最后整份计划交给合并。
    /// </summary>
    public ChapterMergeChoice Choice { get; set; } = ChapterMergeChoice.KeepLocal;
}

/// <summary>TODO 备注的一条差异。</summary>
internal sealed class ChapterTodoMergeItem
{
    public required ChapterMergeItemKind Kind { get; init; }

    /// <summary>对齐键：有 StableId 就用它；老备注（还没迁移出 StableId）退化成 <c>#行号</c>。</summary>
    public required string MergeKey { get; init; }

    public string StableId { get; init; } = string.Empty;

    public int LocalRowNumber { get; init; }

    public int PackageRowNumber { get; init; }

    public string LocalText { get; init; } = string.Empty;

    public string PackageText { get; init; } = string.Empty;

    public bool LocalDone { get; init; }

    public bool PackageDone { get; init; }

    /// <summary>界面上的选择，含义同 <see cref="ChapterMergeItem.Choice"/>。</summary>
    public ChapterMergeChoice Choice { get; set; } = ChapterMergeChoice.KeepLocal;
}

/// <summary>
/// 整章的差异计划：界面照着它画列表、用户选完再交给合并。
///
/// 【两类条目分开】剧情行归 <see cref="RowItems"/>，TODO 备注归 <see cref="TodoItems"/> ——
/// 它们的数据源和落点都不一样（行落 story.document.json，备注落项目级 story-todos.json）。
/// </summary>
internal sealed class ChapterMergePlan
{
    /// <summary>拆包结果（文档、备注、清单都在里面 —— 合并时也用它，避免计划和包对不上）。</summary>
    public required ChapterPackageContent Package { get; init; }

    public ChapterPackageManifest Manifest => Package.Manifest;

    public string PackagePath => Package.PackagePath;

    /// <summary>按**阅读顺序**排好：本机行序为骨架，"只有对方有"的句按锚点插在它该在的位置。</summary>
    public List<ChapterMergeItem> RowItems { get; init; } = [];

    public List<ChapterTodoMergeItem> TodoItems { get; init; } = [];

    /// <summary>本机这一章现在多少行（弹窗上"本机 X 行 / 包里 Y 行"用）。</summary>
    public int LocalRowCount { get; init; }

    /// <summary>本机这一章现在多少个小节。</summary>
    public int LocalSectionCount { get; init; }

    /// <summary>不拦路、但必须让人看见的提醒（不同源 / 章码不一样 / 选项函数缺备注……）。</summary>
    public List<string> Warnings { get; init; } = [];

    public int ModifiedCount => RowItems.Count(item => item.Kind == ChapterMergeItemKind.Modified);

    public int OnlyInPackageCount => RowItems.Count(item => item.Kind == ChapterMergeItemKind.OnlyInPackage);

    public int OnlyLocalCount => RowItems.Count(item => item.Kind == ChapterMergeItemKind.OnlyLocal);

    public int TodoChangeCount => TodoItems.Count;

    public bool HasAnyChange => RowItems.Count > 0 || TodoItems.Count > 0;

    /// <summary>已经点了"用对方"的条数（界面上那个"将应用 N 条"）。</summary>
    public int SelectedFromPackageCount =>
        RowItems.Count(item => item.Choice == ChapterMergeChoice.UsePackage) +
        TodoItems.Count(item => item.Choice == ChapterMergeChoice.UsePackage);
}

/// <summary>合并结果，用来告诉用户到底动了什么。</summary>
internal sealed record ChapterMergeApplyResult(
    int UpdatedRowCount,
    int InsertedRowCount,
    int DeletedRowCount,
    int UpdatedTodoCount,
    string BackupPath);

