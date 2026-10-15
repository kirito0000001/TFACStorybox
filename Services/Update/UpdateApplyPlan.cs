using System;
using System.Collections.Generic;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 覆盖动作集合的**纯逻辑**实现（设计 §9.6）：给「安装目录现在的文件清单」和「包里的文件清单」，
/// 算出哪些要换、哪些要删、哪些绝不碰。
/// <para>
/// 纯到什么程度：不碰文件系统、不读 zip、不发网络，输入输出全是参数与返回值 ——
/// 所以 <c>applyplan-whitelist</c> 那条用例可以直接喂字符串断言，不需要造临时目录。
/// 真正遍历目录、读 zip 的是调用方（M5 的启动预检与 updater 脚本）。
/// </para>
/// <para>
/// ⚠️ updater 脚本（<c>Scripts\热更新覆盖.ps1</c>）里有同一套算法的 PowerShell 版本，两边靠
/// <c>Scripts\update-whitelist.json</c> 共用规则数据（见 <see cref="UpdateWhitelist"/> 的注释）。
/// 改这里的判定顺序时，必须同步改脚本里的 <c>Get-ZdApplyPlan</c>。
/// </para>
/// </summary>
internal static class UpdateApplyPlan
{
    /// <summary>
    /// 算动作集合（§9.6「准备」段）：
    /// <list type="bullet">
    /// <item><c>ToReplace</c> = 包内有、且与安装目录现有的**内容不同**（先比大小，大小相同再比 SHA-256）；</item>
    /// <item><c>ToDelete</c> = 安装目录有、包内没有、且落在白名单内（镜像语义）；</item>
    /// <item><c>Skip</c> = 白名单外的一切（用户自己塞的东西），外加内容完全相同的文件 —— 永不触碰。</item>
    /// </list>
    /// </summary>
    public static UpdateApplyPlanResult Compute(
        UpdateWhitelist whitelist,
        IReadOnlyList<UpdateFileEntry> installFiles,
        IReadOnlyList<UpdateFileEntry> packageFiles,
        string targetVersion)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        ArgumentNullException.ThrowIfNull(installFiles);
        ArgumentNullException.ThrowIfNull(packageFiles);

        var install = new Dictionary<string, UpdateFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in installFiles)
        {
            install[UpdateWhitelist.NormalizeRelative(file.RelativePath)] = file;
        }

        var package = new Dictionary<string, UpdateFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in packageFiles)
        {
            package[UpdateWhitelist.NormalizeRelative(file.RelativePath)] = file;
        }

        var replace = new List<UpdateApplyAction>();
        var delete = new List<UpdateApplyAction>();
        var skipped = new List<string>();

        // 包内文件：内容的真源。相同的跳过（不折腾盘），不同的整批腾位 + 落位。
        foreach (var file in packageFiles)
        {
            var path = UpdateWhitelist.NormalizeRelative(file.RelativePath);
            if (!whitelist.IsMirrored(path))
            {
                // 包内混进了白名单外的文件（打包脚本本该拦住）—— 不覆盖、不删除，只记账。
                skipped.Add(path);
                continue;
            }

            if (install.TryGetValue(path, out var existing) && IsSameContent(existing, package[path]))
            {
                skipped.Add(path);
                continue;
            }

            replace.Add(new UpdateApplyAction(UpdateApplyActionKind.Replace, path));
        }

        // 安装目录里有、包里没有：镜像语义要删 —— 但只删白名单内的，用户自己的东西一个字都不动。
        foreach (var file in installFiles)
        {
            var path = UpdateWhitelist.NormalizeRelative(file.RelativePath);
            if (!whitelist.IsMirrored(path) || package.ContainsKey(path))
            {
                skipped.Add(path);
                continue;
            }

            delete.Add(new UpdateApplyAction(UpdateApplyActionKind.Delete, path));
        }

        return new UpdateApplyPlanResult(replace, delete, skipped, ".old-" + targetVersion);
    }

    /// <summary>
    /// 「内容一样吗」：先比大小（便宜、能挡掉绝大多数），大小相同才比 SHA-256。
    /// 两边只要有一边没算哈希，就按「大小相同 = 相同」处理（updater 的包内条目就是这么比的）。
    /// </summary>
    public static bool IsSameContent(UpdateFileEntry left, UpdateFileEntry right)
    {
        if (left.SizeBytes != right.SizeBytes)
        {
            return false;
        }

        if (string.IsNullOrEmpty(left.Sha256) || string.IsNullOrEmpty(right.Sha256))
        {
            return true;
        }

        return string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把一批「安装目录里的相对路径」按白名单分类（给 M5 的启动预检/清理用，也方便用例直接断言）。</summary>
    public static UpdateWhitelistClassification Classify(UpdateWhitelist whitelist, IEnumerable<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var mirrored = new List<string>();
        var untouched = new List<string>();
        foreach (var path in relativePaths)
        {
            var normalized = UpdateWhitelist.NormalizeRelative(path);
            if (whitelist.IsMirrored(normalized))
            {
                mirrored.Add(normalized);
            }
            else
            {
                untouched.Add(normalized);
            }
        }

        return new UpdateWhitelistClassification(mirrored, untouched);
    }
}
