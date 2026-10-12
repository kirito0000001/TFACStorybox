using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

/// <summary>
/// 同步前的本地提交结果。
/// </summary>
/// <param name="IsRepository">这个目录在不在 git 仓库里。</param>
/// <param name="Committed">真的产生了一次提交。</param>
/// <param name="HeadSha">提交后的 HEAD 短号；拿不到就是空串。</param>
/// <param name="Detail">给人看的一句话（进日志、必要时弹提示）。</param>
internal sealed record UnrealSyncGitCommitResult(
    bool IsRepository,
    bool Committed,
    string HeadSha,
    string Detail);

/// <summary>
/// 同步前的**全量本地提交**。
///
/// 【它取代了什么】
/// 原先同步前会给 Unreal 工程打一份 zip（.uproject + Config + 目标内容夹，留最近 5 份）。
/// 换成 git 提交之后：回退点更细（每次一个 commit）、不占额外磁盘、
/// 而且 diff 看得见 —— 对一个本来就用 git 管的工程，zip 那套是重复建设。
///
/// 【口径：只提交，不推送】
/// 用户明确要求"全量**本地**提交"。这里一个 <c>git push</c> 都不会跑 ——
/// 同步是高频动作，顺手往远端推是在替用户做他没同意的事。
///
/// 【没有仓库怎么办】
/// **不阻断**，只报告。用户可能故意不用版本控制，同步本身不该因此停下；
/// 但"这次没有可回退的提交点"必须说清楚（同步前检测里也有一条 Warning 提醒）。
///
/// 【命令怎么跑】
/// 复用 <see cref="UnrealSyncProcessRunner"/>：它已经把子进程输出固定在 UTF-8 上，
/// 并且取消/超时会先杀进程树 —— git 在 LFS 上跑 <c>add -A</c> 可能要几十秒到几分钟，
/// 取消时留一个孤儿 git 进程继续占着 .git/index.lock 会很难查。
/// </summary>
internal static class UnrealSyncGitCommitService
{
    /// <summary>
    /// git 命令的超时。Unreal 工程里 <c>add -A</c> 要哈希大二进制（尤其走 LFS 时），
    /// 比一般仓库慢得多，所以给得比较宽。
    /// </summary>
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 纯磁盘判断：这个目录（或它的任一祖先）有没有 <c>.git</c>。
    ///
    /// 给同步前检测用的 —— 那一步是同步的、跑在界面上，不该为一条提示去起进程。
    /// 真正的提交走 <see cref="CommitEverythingAsync"/>，那里会用 git 自己再确认一次。
    /// 注意 <c>.git</c> 可能是**文件**（worktree / submodule），所以两种都认。
    /// </summary>
    public static bool HasGitRepositoryOnDisk(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var current = new DirectoryInfo(Path.GetFullPath(directoryPath));
        while (current is not null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    /// 把工作区里的全部改动提交一次（<c>git add -A</c> + <c>git commit</c>）。
    /// </summary>
    /// <param name="workspaceDirectory">要提交的目录（Unreal 工程根）。</param>
    /// <param name="commitMessage">提交信息。调用方负责让它一眼看得出是哪次同步。</param>
    /// <param name="onProgress">进度回调（"正在暂存…" 这种），进日志或进度条。</param>
    public static async Task<UnrealSyncGitCommitResult> CommitEverythingAsync(
        string workspaceDirectory,
        string commitMessage,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceDirectory) || !Directory.Exists(workspaceDirectory))
        {
            return new UnrealSyncGitCommitResult(false, false, string.Empty, "目录不存在，跳过同步前的本地提交。");
        }

        var isRepository = await TryRunGitAsync(
            workspaceDirectory,
            ["rev-parse", "--is-inside-work-tree"],
            cancellationToken).ConfigureAwait(false);
        if (isRepository is null || isRepository.ExitCode != 0 ||
            !isRepository.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return new UnrealSyncGitCommitResult(
                false,
                false,
                string.Empty,
                "这个目录不是 git 仓库，本次没有可回退的提交点（同步会照常进行）。");
        }

        onProgress?.Invoke("正在暂存全部改动（git add -A）...");
        await RunGitAsync(workspaceDirectory, ["add", "-A"], cancellationToken).ConfigureAwait(false);

        // 先看有没有东西可提交。没有就直接返回 —— 空提交会让 git 退出码非 0，
        // 而那不该被当成同步失败。
        var status = await RunGitAsync(
            workspaceDirectory,
            ["status", "--porcelain"],
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status.Output))
        {
            return new UnrealSyncGitCommitResult(
                true,
                false,
                await TryReadHeadShaAsync(workspaceDirectory, cancellationToken).ConfigureAwait(false),
                "工作区本来就干净，没有产生新提交。");
        }

        onProgress?.Invoke("正在提交本地改动（git commit）...");
        await RunGitAsync(workspaceDirectory, ["commit", "-m", commitMessage], cancellationToken).ConfigureAwait(false);

        var head = await TryReadHeadShaAsync(workspaceDirectory, cancellationToken).ConfigureAwait(false);
        return new UnrealSyncGitCommitResult(
            true,
            true,
            head,
            string.IsNullOrWhiteSpace(head)
                ? "已提交本次同步前的全部本地改动。"
                : $"已提交本次同步前的全部本地改动（HEAD {head}）。");
    }

    private static async Task<string> TryReadHeadShaAsync(string workspaceDirectory, CancellationToken cancellationToken)
    {
        var result = await TryRunGitAsync(
            workspaceDirectory,
            ["rev-parse", "--short", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        return result is not null && result.ExitCode == 0 ? result.Output.Trim() : string.Empty;
    }

    private static async Task<UnrealSyncProcessResult> RunGitAsync(
        string workspaceDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await TryRunGitAsync(workspaceDirectory, arguments, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            throw new InvalidOperationException("无法启动 git。请确认已安装 Git 并且在 PATH 里。");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} 失败（退出码 {result.ExitCode}）：{result.Output.Trim()}");
        }

        return result;
    }

    /// <summary>跑一条 git 并拿回输出；**起不来**（比如没装 git）返回 null，由调用方决定怎么说。</summary>
    private static async Task<UnrealSyncProcessResult?> TryRunGitAsync(
        string workspaceDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspaceDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            // 不传 verdict：退出码的含义各条命令不同（status/rev-parse 非 0 都可能是"正常的否"），
            // 由调用方按命令自己判。
            return await UnrealSyncProcessRunner.RunAsync(
                startInfo,
                GitTimeout,
                "无法启动 git。",
                "git 超过 10 分钟没有结束，已终止进程。",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 找不到 git.exe —— 这是环境问题，不是仓库问题，交给调用方给一句像样的话。
            return null;
        }
    }
}
