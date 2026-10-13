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
        var items = new List<UnrealSyncCheckItem>();

        // ── 1. 关联：引擎与工程（这两项可「更换」）───────────────────
        var projectCheck = CheckProjectFile(unrealProjectPath);
        items.Add(CheckEngine(enginePath));
        items.Add(projectCheck);

        // 只有**工程**找不到才提前收工：下面所有路径都是按 .uproject 拼出来的，
        // 工程不对的话它们全是假的，硬凑只会刷一屏假红。
        //
        // ⚠️ 引擎不对**不能**提前收工。这两件事互不相干 ——
        // 引擎选错了，照样该告诉你"目录都在、资产类型对不对"。
        // 2026-10-12：我重写这一段时把条件写成了 items.Any(不合规)，
        // 于是引擎一红后面十几项全不出现 —— 那个"只剩两项"的老毛病又犯了一遍。
        // 它不是"没删干净"，是把判据写宽了：判据必须是**这一条**（工程），
        // 而不是"有没有任何一条不合规"。
        if (!projectCheck.IsCompliant)
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
        //
        // 类型**优先问清单**（同步脚本在 Unreal 里写的那份，权威），
        // 没有清单才退回落文件头扫（粗，但不用跑引擎）。
        // 这跟零境一样：它的 assetClass 也是读清单读来的，不是猜的。
        var assetTypes = UnrealAssetTypeManifest.Read(unrealProjectPath!);
        items.Add(CheckAsset(
            contentPath,
            assetTypes,
            UnrealSyncPathPolicy.StoryWidgetAsset,
            "剧情 UI 蓝图（背景图列表的落点）",
            "WidgetBlueprint"));
        items.Add(CheckAsset(
            contentPath,
            assetTypes,
            UnrealSyncPathPolicy.MusicPlayerAsset,
            "MusicActor 蓝图（BGM / 环境音 / 语音的落点）",
            "Blueprint"));
        // ⚠️ 期望类型写的是 **Unreal 会报的那个类名**，不是"它逻辑上是什么"。
        // 数据资产那一项尤其容易写错：DA_LustrationInfor 是**蓝图化的数据资产**
        // （父类 PDA_LustrationData 是个蓝图），Unreal 对它报的 asset_class 就是
        // /Script/Engine.Blueprint —— 所以这里期望 "Blueprint" 而不是 "DataAsset"。
        // 2026-10-12：我原来写 "DataAsset"，结果两项正确资产被判成「类型不对」——
        // 那是**假红**，比不查更糟。
        items.Add(CheckAsset(
            contentPath,
            assetTypes,
            UnrealSyncPathPolicy.LustrationInfoAsset,
            "立绘信息数据资产（蓝图化，所以类名是 Blueprint）",
            "Blueprint"));
        items.Add(CheckAsset(
            contentPath,
            assetTypes,
            UnrealSyncPathPolicy.PortraitsInfoAsset,
            "小预览数据资产（蓝图化，所以类名是 Blueprint）",
            "Blueprint",
            UnrealSyncCheckSeverity.Warning));
        items.Add(CheckAsset(
            contentPath,
            assetTypes,
            UnrealSyncPathPolicy.EffectIndexTableAsset,
            "特殊音效索引表",
            "DataTable"));
        items.Add(CheckAsset(
            contentPath,
            assetTypes,
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

    /// <summary>
    /// 引擎那一项。**只看配置里那个路径** —— 那一行是让人填
    /// <c>UnrealEditor.exe</c> / <c>UnrealEditor-Cmd.exe</c> 的，填错就该报错，
    /// 而不是悄悄换一个引擎去用（那样更难查）。
    ///
    /// ⚠️ 卡片上显示**解析出来的那个可执行文件**，不是"你填的那个"。
    /// 你填 <c>UnrealEditor.exe</c> 时，工具会优先用同目录的
    /// <c>UnrealEditor-Cmd.exe</c>（离线执行走的是它）—— 这一点必须看得见。
    /// 2026-10-12：我重写成卡片时把这条信息弄丢了，界面上一直显示 .exe，
    /// 看起来就像"没在用 cmd"。
    ///
    /// 失败原因分三种说：还没选 / 路径不存在 / 不是那两个 exe。
    /// 原来三种共用一句"没找到 UnrealEditor-Cmd.exe"，照那句话去找会白费功夫。
    /// </summary>
    private static UnrealSyncCheckItem CheckEngine(string? enginePath)
    {
        var configured = enginePath ?? string.Empty;
        var executable = UnrealSyncService.ResolveEditorExecutable(configured);
        var found = executable is not null;

        var problem = found
            ? string.Empty
            : string.IsNullOrWhiteSpace(configured)
                ? "还没有选择引擎"
                : File.Exists(configured) || Directory.Exists(configured)
                    ? "这不是 UnrealEditor.exe 或 UnrealEditor-Cmd.exe"
                    : "这个路径不存在";

        // 解析出来和你填的不是同一个时，说一句 —— 否则"我填的是 UnrealEditor.exe，
        // 它却用了 UnrealEditor-Cmd.exe"这件事没人知道。
        var note = !found
            ? problem
            : string.Equals(executable, configured, StringComparison.OrdinalIgnoreCase)
                ? "已找到。"
                : $"已找到。实际用的是同目录的 {Path.GetFileName(executable!)}（离线执行走它）。";

        return new UnrealSyncCheckItem(
            "Unreal 编辑器",
            // 显示**解析出来的那个**（通常就是 UnrealEditor-Cmd.exe），
            // 而不是"你填的那个" —— 工具用的是前者，界面就该说前者。
            found ? executable! : configured,
            found ? executable! : string.Empty,
            found,
            found ? string.Empty : problem,
            Severity: UnrealSyncCheckSeverity.Error,
            ReplaceTarget: UnrealSyncCheckReplaceTarget.Engine,
            Kind: UnrealSyncCheckKind.File,
            DetailOverride: note);
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
    /// 类型从哪来，按可信度排：
    /// 1. **清单**（同步脚本在 Unreal 里问出来的，权威）—— 有就用它；
    /// 2. 清单里没有，就**扫文件头**（<see cref="UnrealAssetTypeProbe"/>，粗但不用跑引擎）；
    /// 3. 两条都拿不到 → 空串 → <see cref="UnrealAssetTypeProbe.Matches"/> 当"不判"。
    ///
    /// 类型对不上才算错；**读不到类型当"不判"**（见类注释里那条"读不到 ≠ 不对"）。
    /// </summary>
    private static UnrealSyncCheckItem CheckAsset(
        string contentPath,
        IReadOnlyDictionary<string, string> assetTypes,
        string assetObjectPath,
        string displayName,
        string expectedType,
        UnrealSyncCheckSeverity severity = UnrealSyncCheckSeverity.Error)
    {
        var diskPath = ResolveAssetDiskPath(contentPath, assetObjectPath);
        var exists = File.Exists(diskPath);
        var actualType = ResolveActualType(assetTypes, assetObjectPath, diskPath, exists);
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

    private static string ResolveActualType(
        IReadOnlyDictionary<string, string> assetTypes,
        string assetObjectPath,
        string diskPath,
        bool exists)
    {
        if (assetTypes.TryGetValue(assetObjectPath, out var fromManifest) &&
            !string.IsNullOrWhiteSpace(fromManifest))
        {
            return fromManifest;
        }

        return exists ? UnrealAssetTypeProbe.Detect(diskPath) : string.Empty;
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
