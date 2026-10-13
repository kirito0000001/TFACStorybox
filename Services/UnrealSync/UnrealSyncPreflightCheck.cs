using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools.Services;

/// <summary>
/// 同步前检测：**只读磁盘，不跑 Unreal**。
///
/// 【为什么要单独一步】
/// 同步一旦开始就要动 Unreal 工程，而最常见的失败是"目标目录根本不在这儿"——
/// 那种情况应该在点同步之前就说清楚，而不是跑一半再报。
/// 因为只读磁盘，它很便宜，可以进页面就跑一次。
///
/// 【为什么不止查"在不在"】
/// 光知道文件在是不够的：把 UI_Story 建成了普通 Blueprint、把 DA_LustrationInfor
/// 建成了 DataTable —— 文件都在、同步也"成功"，但游戏侧取不到东西。
/// 所以资产那几项要连**类型**一起查（类型从 .uasset 里读，见 <see cref="UnrealAssetTypeProbe"/>）。
///
/// 【一条从零境学来的规矩：读不到 ≠ 不对】
/// 它有一句注释原话：「拿"没读到"当成"父类不对"，会把角色 Item 永久判成不合规 ——
/// 那是**假红**」。所以这里凡是"读不出来"的情况一律**不判失败**，
/// 宁可少报一条，也不要报一条假的 —— 假红会让人不再信这张表。
/// </summary>
internal static class UnrealSyncPreflightCheck
{
    public static UnrealSyncPreflightResult Run(
        string? unrealProjectPath,
        string? enginePath,
        string? assetLibraryPath)
    {
        var items = new List<UnrealSyncCheckItem>
        {
            // ── 1. 关联：引擎与工程（这两项可「更换」）───────────────────
            CheckEngine(enginePath),
            CheckProjectFile(unrealProjectPath),
        };

        // 工程路径都不对的话，下面按它拼出来的路径全是假的 —— 硬凑只会刷一屏假红。
        if (items.Any(item => !item.IsCompliant))
        {
            return BuildResult(items);
        }

        var contentPath = UnrealSyncPathPolicy.ResolveContentFolderPath(unrealProjectPath!);
        var hasContent = Directory.Exists(contentPath);
        items.Add(new UnrealSyncCheckItem(
            "Content 目录",
            contentPath,
            hasContent ? contentPath : string.Empty,
            hasContent,
            hasContent ? string.Empty : "工程下没有 Content 目录",
            Kind: UnrealSyncCheckKind.Directory));

        if (!hasContent)
        {
            return BuildResult(items);
        }

        // ── 2. 落点目录：逐个点名 ────────────────────────────────────
        // 这是这一步的主要价值：把"素材要去的每一个目录"列出来，缺哪个说哪个。
        // 显示名就用相对路径 —— 用户在 Unreal 内容浏览器里看到的正是这一段。
        foreach (var relative in UnrealSyncPathPolicy.ExpectedContentFolders)
        {
            items.Add(CheckFolder(contentPath, relative));
        }

        // ── 3. 要写进去的目标资产：连类型一起查 ──────────────────────
        items.Add(CheckAsset(
            contentPath,
            UnrealSyncPathPolicy.StoryWidgetAsset,
            "剧情 UI 蓝图（背景图列表的落点）",
            "WidgetBlueprint"));
        items.Add(CheckAsset(
            contentPath,
            UnrealSyncPathPolicy.MusicPlayerAsset,
            "MusicActor 蓝图（BGM / 环境音 / 语音的落点）",
            "Blueprint"));
        items.Add(CheckAsset(
            contentPath,
            UnrealSyncPathPolicy.LustrationInfoAsset,
            "立绘信息数据资产",
            "DataAsset"));
        items.Add(CheckAsset(
            contentPath,
            UnrealSyncPathPolicy.PortraitsInfoAsset,
            "小预览数据资产（仅开启小预览时用）",
            "DataAsset",
            UnrealSyncCheckSeverity.Warning));
        items.Add(CheckAsset(
            contentPath,
            UnrealSyncPathPolicy.EffectIndexTableAsset,
            "特殊音效索引表",
            "DataTable"));
        items.Add(CheckAsset(
            contentPath,
            UnrealSyncPathPolicy.StoryBranchTableAsset,
            "剧情选项分支表",
            "DataTable",
            UnrealSyncCheckSeverity.Warning));

        // ── 4. 同步前的回退点：git 仓库 ──────────────────────────────
        // 没有仓库**不拦路**，只提醒 —— 用户可能故意不用版本控制；
        // 但"这次没有可回退的提交点"得让人看见。
        items.Add(CheckGitRepository(unrealProjectPath!));

        // ── 5. 素材库（从项目推导出来的那个）─────────────────────────
        if (!string.IsNullOrWhiteSpace(assetLibraryPath))
        {
            items.Add(CheckAssetLibrary(assetLibraryPath));
        }

        return BuildResult(items);
    }

    // ── 各类检查 ──────────────────────────────────────────────────────

    private static UnrealSyncCheckItem CheckEngine(string? enginePath)
    {
        var configured = enginePath ?? string.Empty;
        var executable = UnrealSyncService.ResolveEditorExecutable(configured);
        var found = executable is not null;
        return new UnrealSyncCheckItem(
            "Unreal 编辑器",
            configured,
            found ? executable! : string.Empty,
            found,
            found ? string.Empty : "没找到 UnrealEditor-Cmd.exe",
            Severity: UnrealSyncCheckSeverity.Error,
            ReplaceTarget: UnrealSyncCheckReplaceTarget.Engine,
            Kind: UnrealSyncCheckKind.File);
    }

    private static UnrealSyncCheckItem CheckProjectFile(string? unrealProjectPath)
    {
        var configured = unrealProjectPath ?? string.Empty;
        var exists = !string.IsNullOrWhiteSpace(configured) && File.Exists(configured);
        return new UnrealSyncCheckItem(
            "Unreal 工程",
            configured,
            exists ? configured : string.Empty,
            exists,
            exists ? string.Empty : "找不到 .uproject 文件",
            Severity: UnrealSyncCheckSeverity.Error,
            ReplaceTarget: UnrealSyncCheckReplaceTarget.Project,
            Kind: UnrealSyncCheckKind.File);
    }

    private static UnrealSyncCheckItem CheckFolder(string contentPath, string relative)
    {
        var diskPath = Path.Combine(contentPath, relative.Replace('/', Path.DirectorySeparatorChar));
        var exists = Directory.Exists(diskPath);
        return new UnrealSyncCheckItem(
            relative,
            diskPath,
            exists ? diskPath : string.Empty,
            exists,
            exists ? string.Empty : "目录不存在",
            Kind: UnrealSyncCheckKind.Directory);
    }

    /// <summary>
    /// 查一个资产：在不在 + **类型对不对**。
    ///
    /// 类型对不上才算错；**读不到类型当"不判"**（见类注释里那条"读不到 ≠ 不对"）。
    /// </summary>
    private static UnrealSyncCheckItem CheckAsset(
        string contentPath,
        string assetObjectPath,
        string displayName,
        string expectedType,
        UnrealSyncCheckSeverity severity = UnrealSyncCheckSeverity.Error)
    {
        var diskPath = ResolveAssetDiskPath(contentPath, assetObjectPath);
        var exists = File.Exists(diskPath);
        var actualType = exists ? UnrealAssetTypeProbe.Detect(diskPath) : string.Empty;
        var typeMatches = UnrealAssetTypeProbe.Matches(expectedType, actualType);
        var compliant = exists && typeMatches;

        var problem = !exists
            ? "资产不存在"
            : typeMatches
                ? string.Empty
                : "类型不对";

        return new UnrealSyncCheckItem(
            displayName,
            assetObjectPath,
            exists ? diskPath : string.Empty,
            compliant,
            problem,
            expectedType,
            actualType,
            severity,
            UnrealSyncCheckReplaceTarget.None,
            UnrealSyncCheckKind.Asset);
    }

    private static UnrealSyncCheckItem CheckGitRepository(string unrealProjectPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(unrealProjectPath)) ?? string.Empty;
        var hasRepository = UnrealSyncGitCommitService.HasGitRepositoryOnDisk(projectDirectory);
        return new UnrealSyncCheckItem(
            "git 仓库（同步前的本地提交用）",
            projectDirectory,
            hasRepository ? projectDirectory : string.Empty,
            hasRepository,
            hasRepository ? string.Empty : "这个工程不在 git 仓库里",
            Severity: UnrealSyncCheckSeverity.Warning,
            Kind: UnrealSyncCheckKind.Directory);
    }

    private static UnrealSyncCheckItem CheckAssetLibrary(string assetLibraryPath)
    {
        var exists = Directory.Exists(assetLibraryPath);
        return new UnrealSyncCheckItem(
            "素材库目录",
            assetLibraryPath,
            exists ? assetLibraryPath : string.Empty,
            exists,
            exists ? string.Empty : "素材库目录不存在",
            Kind: UnrealSyncCheckKind.Directory);
    }

    // ── 汇总 ──────────────────────────────────────────────────────────

    private static UnrealSyncPreflightResult BuildResult(IReadOnlyList<UnrealSyncCheckItem> items)
    {
        var blockers = items.Count(item => !item.IsCompliant && item.Severity == UnrealSyncCheckSeverity.Error);
        var reminders = items.Count(item => !item.IsCompliant && item.Severity == UnrealSyncCheckSeverity.Warning);
        var passed = items.Count(item => item.IsCompliant);

        // 提醒不拦路，所以"能不能同步"只看 Error 那几项 ——
        // 把 Warning 也算进拦路，就会出现"明明只是没建 git 仓库却同步不了"。
        var summary = blockers > 0
            ? $"共 {items.Count} 项：合规 {passed}，{blockers} 项拦路"
              + (reminders > 0 ? $"，{reminders} 项提醒" : string.Empty)
              + "。标红的几项不解决就没法同步。"
            : reminders > 0
                ? $"共 {items.Count} 项：合规 {passed}，{reminders} 项提醒。提醒不拦路，可以继续。"
                : $"共 {items.Count} 项全部合规，可以同步。";

        return new UnrealSyncPreflightResult(blockers == 0, items, summary);
    }

    /// <summary>对象路径 → 磁盘 <c>.uasset</c> 路径。资产名那段（<c>.C</c>）是包内名字，不进路径。</summary>
    internal static string ResolveAssetDiskPath(string contentFolderPath, string assetObjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetObjectPath);

        var relative = assetObjectPath;
        if (relative.StartsWith(UnrealSyncPathPolicy.GameRoot + "/", StringComparison.Ordinal))
        {
            relative = relative[(UnrealSyncPathPolicy.GameRoot.Length + 1)..];
        }

        var dotIndex = relative.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0)
        {
            relative = relative[..dotIndex];
        }

        return Path.Combine(
            contentFolderPath,
            relative.Replace('/', Path.DirectorySeparatorChar) + ".uasset");
    }
}
