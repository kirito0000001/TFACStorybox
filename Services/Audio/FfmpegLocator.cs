using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

/// <summary>
/// 「用户自己配过路径、只是文件不在了」和「压根没配过 / 包里也没有」是两件事，
/// 界面要说的也是两句不同的话，所以不能用 null 一律代替。
/// </summary>
internal enum FfmpegResolveKind
{
    /// <summary>找到了，能用。</summary>
    Found,

    /// <summary>用户配过一个路径，但那个文件现在不在了（换机器、移动了目录）。</summary>
    ConfiguredPathMissing,

    /// <summary>没配过，包里也没有随附副本。</summary>
    NotFound
}

internal sealed record FfmpegResolution(
    FfmpegResolveKind Kind,
    string? ExecutablePath,
    string Detail)
{
    public bool IsFound => Kind == FfmpegResolveKind.Found;
}

/// <summary>
/// 验明正身的结果。真起一次 <c>-version</c>，能读出「ffmpeg version」才算数。
/// </summary>
internal sealed record FfmpegProbeResult(
    bool IsUsable,
    string VersionText,
    string? FailureReason)
{
    public static FfmpegProbeResult Failure(string reason) => new(false, string.Empty, reason);
}

/// <summary>
/// ffmpeg 在哪。
///
/// ★ <b>只认 exe 路径，不依赖 PATH。</b> 原因不是洁癖：PATH 里的 ffmpeg 版本不可控
/// （有的构建没有 <c>loudnorm</c>），而语音规整要用的正是那个滤镜。
/// 找一个「看起来像 ffmpeg」的东西然后跑到一半报错，比一开始就说「没找到」难查得多。
///
/// 查找顺序（2026-11-02 定）：**设置里填的路径优先，其次随包副本**。
/// 配过就只认配的那个、不再偷偷回落到随包版本 —— 否则用户「明明改了路径却没生效」，
/// 而且不知道为什么。
/// </summary>
internal sealed class FfmpegLocator
{
    /// <summary>
    /// 随包副本的相对落点。改这个目录名要同时看三处：
    /// ① <c>.gitignore</c>（150 MB 二进制不进仓库）；
    /// ② <c>GalExcleTools.csproj</c> 的 <c>Tools\**\*</c> Content 规则（负责复制到输出目录）；
    /// ③ 热更新白名单 <c>Scripts\update-whitelist.json</c>（认 <c>Tools\</c> 一级目录与 exe/dll 扩展名）。
    /// </summary>
    public const string BundledFolderRelativePath = @"Tools\Audio\ffmpeg";

    public const string ExecutableFileName = "ffmpeg.exe";

    private const string VersionMarker = "ffmpeg version";

    /// <summary>探测超时。正常机器上几十毫秒；给 30 秒纯粹是防「网络盘上的 exe」。</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<string?> _readConfiguredPath;
    private readonly string _baseDirectory;

    public FfmpegLocator(Func<string?> readConfiguredPath, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(readConfiguredPath);
        _readConfiguredPath = readConfiguredPath;
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
    }

    /// <summary>随包副本的完整路径（不管存不存在，用于给用户看「应该放哪」）。</summary>
    public string BundledExecutablePath =>
        Path.Combine(_baseDirectory, BundledFolderRelativePath, ExecutableFileName);

    public FfmpegResolution Resolve()
    {
        var configured = NormalizePath(_readConfiguredPath());
        if (configured is not null)
        {
            return File.Exists(configured)
                ? new FfmpegResolution(FfmpegResolveKind.Found, configured, $"使用设置里指定的 ffmpeg：{configured}")
                : new FfmpegResolution(
                    FfmpegResolveKind.ConfiguredPathMissing,
                    null,
                    $"设置里指定的 ffmpeg 不在了：{configured}");
        }

        foreach (var candidate in EnumerateBundledCandidates())
        {
            if (File.Exists(candidate))
            {
                return new FfmpegResolution(FfmpegResolveKind.Found, candidate, $"使用随包附带的 ffmpeg：{candidate}");
            }
        }

        return new FfmpegResolution(
            FfmpegResolveKind.NotFound,
            null,
            $"没找到 ffmpeg。设置里没填，包里也没有 {BundledExecutablePath}。");
    }

    /// <summary>
    /// 包内副本的候选位置。第二个是「程序旁边还有一份导出目录」的历史形态，
    /// 留着不占成本 —— 找不到就算了，<b>绝不猜 PATH</b>。
    /// </summary>
    private IEnumerable<string> EnumerateBundledCandidates()
    {
        yield return BundledExecutablePath;
        yield return Path.Combine(_baseDirectory, ExecutableFileName);
    }

    /// <summary>
    /// 真起一次 <c>-version</c>。一个改了名的假 exe 会在这里被挡下，
    /// 而不是在批量规整跑到一半时炸。
    /// </summary>
    public async Task<FfmpegProbeResult> ProbeAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return FfmpegProbeResult.Failure("这个路径下没有文件。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // ★ 重定向了输出就必须固定编码，否则中文 Windows 按 936 解码 ffmpeg 的 UTF-8 输出，
            //   探测失败时那句诊断会整段乱码（和 UnrealSyncProcessRunner 里同一条规矩）。
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? _baseDirectory
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-version");

        try
        {
            // 复用同步台的进程运行器：它已经把「取消/超时先杀进程树」做对了。
            var result = await UnrealSyncProcessRunner.RunAsync(
                startInfo,
                ProbeTimeout,
                "没能启动 ffmpeg（文件可能被安全软件拦了）。",
                "ffmpeg 探测超时，已中止（进程树已结束）。",
                onPoll: null,
                verdict: null,
                cancellationToken).ConfigureAwait(false);

            return Interpret(result.ExitCode, result.Output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return FfmpegProbeResult.Failure($"这个文件起不来：{error.Message}");
        }
    }

    /// <summary>纯函数：判退出码 + 输出算不算「真 ffmpeg」。抽出来方便直接断言。</summary>
    internal static FfmpegProbeResult Interpret(int exitCode, string output)
    {
        if (exitCode != 0)
        {
            return FfmpegProbeResult.Failure($"ffmpeg -version 退出码是 {exitCode}，不是可用的 ffmpeg。");
        }

        var line = FirstVersionLine(output);
        if (line is null)
        {
            return FfmpegProbeResult.Failure("这个程序对 -version 的回复里没有版本号，像是个同名文件而不是 ffmpeg。");
        }

        return new FfmpegProbeResult(true, line, null);
    }

    private static string? FirstVersionLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var raw in output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(VersionMarker, StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return null;
    }

    private static string? NormalizePath(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim().Trim('"');
        return trimmed.Length == 0 ? null : trimmed;
    }
}
