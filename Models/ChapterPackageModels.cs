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
