using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools.Services;

/// <summary>检测项的严重程度。只有 Error 会拦住同步。</summary>
internal enum UnrealSyncCheckSeverity
{
    /// <summary>拦路的问题：不修就没法同步。</summary>
    Error,

    /// <summary>提醒：能同步，但结果可能不是你要的。</summary>
    Warning,
}

/// <summary>
/// 一条检测项。形态照零境第一步那张表（显示名 / 期望 / 实际 / 是否合规 / 问题），
/// 那张表的好处是**同一套结构既能逐条显示、也能一句话汇总**。
/// </summary>
/// <param name="DisplayName">给人看的名字，例如「目标内容目录」。</param>
/// <param name="ExpectedPath">期望的路径。磁盘路径或 <c>/Game</c> 路径都可以，写清就行。</param>
/// <param name="ActualPath">实际找到的路径；没找到就是空串。</param>
/// <param name="IsCompliant">合不合规。</param>
/// <param name="Severity">不合规时算拦路还是提醒。</param>
/// <param name="Problem">不合规的原因；合规时为空串。</param>
internal sealed record UnrealSyncCheckItem(
    string DisplayName,
    string ExpectedPath,
    string ActualPath,
    bool IsCompliant,
    UnrealSyncCheckSeverity Severity,
    string Problem);

/// <summary>
/// 同步前检测的结果。
/// </summary>
/// <param name="CanSync">没有任何 Error 级问题。</param>
/// <param name="Items">全部检测项（合规的也在里面 —— 界面要能"只看没过的"，也得能看全）。</param>
/// <param name="SummaryText">一句话汇总，例如「共 14 项：合规 12，待处理 2」。</param>
internal sealed record UnrealSyncPreflightResult(
    bool CanSync,
    IReadOnlyList<UnrealSyncCheckItem> Items,
    string SummaryText);

/// <summary>
/// 同步前检测：**只读磁盘，不跑 Unreal**。
///
/// 【为什么要单独一步】
/// 同步一旦开始就要动 Unreal 工程，而最常见的失败是"目标目录根本不在这儿"——
/// 那种情况应该在点同步之前就说清楚，而不是跑一半再报。
/// 因为只读磁盘，它很便宜，可以进页面就跑一次。
///
/// 【口径：只报告，绝不自动创建】
/// 这是照零境第一步来的：目录该由人在 Unreal 内容浏览器里整理，
/// 工具替他 CreateDirectory 只会让素材落到没人预期的地方。
/// 所以这里全部是 <c>Directory.Exists</c> / <c>File.Exists</c>，一个创建动作都没有。
///
/// 【它替掉了什么】
/// 老代码那条「目标同步文件夹必须命名为 Narrative，避免素材同步到错误目录」的硬校验。
/// 新工程的落点跨了 /Game 下三个顶层目录，选一个"目标夹"已经没有意义，
/// 所以那条连同"手选目标内容夹"一起退休，换成这里逐目录点名。
/// </summary>
internal static class UnrealSyncPreflightCheck
{
    /// <summary>
    /// 跑一次检测。
    /// </summary>
    /// <param name="unrealProjectPath"><c>.uproject</c> 的路径。Content 由它推导。</param>
    /// <param name="enginePath">引擎路径（可以是引擎根，也可以是 UnrealEditor.exe）。</param>
    /// <param name="assetLibraryPath">素材库根目录；可以为空（那就不查这一项）。</param>
    public static UnrealSyncPreflightResult Run(
        string? unrealProjectPath,
        string? enginePath,
        string? assetLibraryPath)
    {
        var items = new List<UnrealSyncCheckItem>();

        // ── 1. 引擎 ──────────────────────────────────────────────────
        var editorExecutable = UnrealSyncService.ResolveEditorExecutable(enginePath ?? string.Empty);
        items.Add(new UnrealSyncCheckItem(
            "Unreal 编辑器",
            "引擎目录下的 UnrealEditor-Cmd.exe",
            editorExecutable ?? string.Empty,
            editorExecutable is not null,
            UnrealSyncCheckSeverity.Error,
            editorExecutable is not null ? string.Empty : "没找到可用的 UnrealEditor-Cmd.exe，检查引擎路径。"));

        // ── 2. 工程文件 ──────────────────────────────────────────────
        var hasProject = !string.IsNullOrWhiteSpace(unrealProjectPath) && File.Exists(unrealProjectPath);
        items.Add(new UnrealSyncCheckItem(
            "Unreal 工程",
            unrealProjectPath ?? string.Empty,
            hasProject ? unrealProjectPath! : string.Empty,
            hasProject,
            UnrealSyncCheckSeverity.Error,
            hasProject ? string.Empty : "找不到 .uproject 文件，检查工程路径。"));

        // 工程都没有就没必要往下查目录了 —— 硬凑出来的路径只会刷一屏假红。
        if (!hasProject)
        {
            return BuildResult(items);
        }

        var contentFolderPath = UnrealSyncPathPolicy.ResolveContentFolderPath(unrealProjectPath!);
        var hasContent = Directory.Exists(contentFolderPath);
        items.Add(new UnrealSyncCheckItem(
            "目标内容目录",
            contentFolderPath,
            hasContent ? contentFolderPath : string.Empty,
            hasContent,
            UnrealSyncCheckSeverity.Error,
            hasContent ? string.Empty : "工程下没有 Content 目录（.uproject 与它同级）。"));

        if (!hasContent)
        {
            return BuildResult(items);
        }

        // ── 3. 落点目录：逐个点名 ────────────────────────────────────
        // 这是这一步的主要价值：把"素材要去的每一个目录"列出来，缺哪个说哪个。
        foreach (var relative in UnrealSyncPathPolicy.ExpectedContentFolders)
        {
            var diskPath = Path.Combine(contentFolderPath, relative.Replace('/', Path.DirectorySeparatorChar));
            var exists = Directory.Exists(diskPath);
            items.Add(new UnrealSyncCheckItem(
                relative,
                diskPath,
                exists ? diskPath : string.Empty,
                exists,
                UnrealSyncCheckSeverity.Error,
                exists ? string.Empty : "目录不存在。请在 Unreal 内容浏览器里先建好，工具不会替你创建。"));
        }

        // ── 4. 要写变量的目标资产 ────────────────────────────────────
        // 目录在、但资产不在，是另一种失败：变量会写到一个不存在的资产上。
        AddAssetCheck(items, contentFolderPath, UnrealSyncPathPolicy.StoryWidgetAsset, "剧情 UI 蓝图（背景图列表的落点）");
        AddAssetCheck(items, contentFolderPath, UnrealSyncPathPolicy.MusicPlayerAsset, "MusicActor 蓝图（BGM/环境音/语音的落点）");
        AddAssetCheck(items, contentFolderPath, UnrealSyncPathPolicy.LustrationInfoAsset, "立绘信息数据资产");
        // 小预览是**可选功能**：IsPortraitPreviewEnabled 关着时整段跳过，
        // 所以它缺了只提醒、不拦路。
        AddAssetCheck(
            items,
            contentFolderPath,
            UnrealSyncPathPolicy.PortraitsInfoAsset,
            "小预览数据资产（仅开启小预览时用）",
            UnrealSyncCheckSeverity.Warning);

        // ── 5. 素材库 ────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(assetLibraryPath))
        {
            var hasLibrary = Directory.Exists(assetLibraryPath);
            items.Add(new UnrealSyncCheckItem(
                "素材库目录",
                assetLibraryPath,
                hasLibrary ? assetLibraryPath : string.Empty,
                hasLibrary,
                UnrealSyncCheckSeverity.Error,
                hasLibrary ? string.Empty : "素材库目录不存在。"));
        }

        return BuildResult(items);
    }

    /// <summary>
    /// 把 <c>/Game/...</c> 的对象路径换成磁盘上的 <c>.uasset</c> 路径并查存在性。
    ///
    /// 对象路径是 <c>/Game/A/B.C</c>（包路径 + 资产名），磁盘上是 <c>Content/A/B/C.uasset</c> ——
    /// 中间那一段要去掉，这一点很容易写错，所以集中在这里。
    /// </summary>
    private static void AddAssetCheck(
        List<UnrealSyncCheckItem> items,
        string contentFolderPath,
        string assetObjectPath,
        string displayName,
        UnrealSyncCheckSeverity severity = UnrealSyncCheckSeverity.Error)
    {
        var diskPath = ResolveAssetDiskPath(contentFolderPath, assetObjectPath);
        var exists = File.Exists(diskPath);
        items.Add(new UnrealSyncCheckItem(
            displayName,
            assetObjectPath,
            exists ? diskPath : string.Empty,
            exists,
            severity,
            exists
                ? string.Empty
                : severity == UnrealSyncCheckSeverity.Warning
                    ? "资产不存在；用到这个功能之前需要先建好。"
                    : "资产不存在。目录在但资产不在，变量会写到一个不存在的资产上。"));
    }

    /// <summary>对象路径 → 磁盘 <c>.uasset</c> 路径。资产名那段（<c>.C</c>）是包内名字，不进路径。</summary>
    internal static string ResolveAssetDiskPath(string contentFolderPath, string assetObjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetObjectPath);

        var withoutRoot = assetObjectPath.StartsWith(UnrealSyncPathPolicy.GameRoot + "/", StringComparison.Ordinal)
            ? assetObjectPath[(UnrealSyncPathPolicy.GameRoot.Length + 1)..]
            : assetObjectPath;

        // /Game/A/B.C → 包路径 A/B、资产名 C；磁盘上是 A/B/C.uasset
        var lastSlash = withoutRoot.LastIndexOf('/');
        var lastDot = withoutRoot.LastIndexOf('.');
        string packagePath;
        string assetName;
        if (lastDot > lastSlash)
        {
            packagePath = withoutRoot[..lastSlash];
            assetName = withoutRoot[(lastDot + 1)..];
        }
        else
        {
            packagePath = withoutRoot;
            assetName = withoutRoot[(lastSlash + 1)..];
        }

        return Path.Combine(
            contentFolderPath,
            packagePath.Replace('/', Path.DirectorySeparatorChar),
            assetName + ".uasset");
    }

    private static UnrealSyncPreflightResult BuildResult(IReadOnlyList<UnrealSyncCheckItem> items)
    {
        var compliant = items.Count(item => item.IsCompliant);
        var pending = items.Count - compliant;
        var canSync = items.All(item => item.IsCompliant || item.Severity != UnrealSyncCheckSeverity.Error);

        return new UnrealSyncPreflightResult(
            canSync,
            items,
            $"共 {items.Count} 项：合规 {compliant}，待处理 {pending}");
    }
}
