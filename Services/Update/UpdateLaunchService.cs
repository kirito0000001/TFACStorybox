using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 一次热更新交接的结果。<see cref="Ready"/> 为真表示 updater 已经写出预检信号，
/// 此后主程序才可以退出（§9.9 的握手）—— 顺序反了，更新失败时用户就没有界面可用了。
/// </summary>
internal sealed record UpdateLaunchOutcome(
    bool Ready,
    bool ProcessStarted,
    string Message,
    string SignalPath,
    string RunnerDirectory,
    int ProcessId);

/// <summary>
/// 把「下载校验完成的更新包」交给覆盖器（设计 §9.3 / §9.7 / §9.9）：
/// <list type="number">
///   <item>把覆盖脚本与生效中的白名单放进 <c>%LOCALAPPDATA%\GalExcleTools\UpdateRunners\</c>；</item>
///   <item>用参数列表（**不经过 cmd.exe**）起 powershell 跑那个脚本，工作目录也设在 UpdateRunners；</item>
///   <item>轮询等待 READY 信号 —— 拿到了才轮到主程序退出。</item>
/// </list>
/// 真正去起进程的是界面壳（<see cref="IUpdaterProcessLauncher"/>），
/// 这一层只负责准备、组装命令行、等待与判定。
/// </summary>
internal sealed class UpdateLaunchService
{
    /// <summary>覆盖器的工作目录名（在 <c>%LOCALAPPDATA%\GalExcleTools\</c> 下）。</summary>
    public const string RunnerDirectoryName = "UpdateRunners";

    /// <summary>覆盖脚本的文件名。它随包发布，也住在安装目录的 <c>Scripts\</c> 里。</summary>
    public const string ScriptFileName = "热更新覆盖.ps1";

    /// <summary>信号文件名前缀（§9.9）。</summary>
    public const string ReadySignalPrefix = "UpdaterReady-";

    public const string ReadySignalExtension = ".signal";

    /// <summary>信号内容里的关键字。用包含判断而不是相等：脚本会往里多写几行诊断信息。</summary>
    public const string ReadyMarker = "READY";

    /// <summary>等 READY 的总超时。90 秒要够覆盖「SHA-256 复核 + 解压」两段（§9.2 的 18%~34%）。</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(90);

    public static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IUpdaterProcessLauncher? _launcher;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _runnerDirectory;

    /// <param name="launcher">真正起进程的那一层（界面壳）。</param>
    /// <param name="clock">取当前时间的口子，用例要压信号文件名的日期部分。</param>
    /// <param name="runnerDirectory">
    /// UpdateRunners 目录。默认 <see cref="ResolveRunnerDirectory"/>；用例传临时目录，
    /// 免得把演练文件写进真实的 <c>%LOCALAPPDATA%</c>。
    /// </param>
    public UpdateLaunchService(
        IUpdaterProcessLauncher? launcher = null,
        Func<DateTimeOffset>? clock = null,
        string? runnerDirectory = null)
    {
        _launcher = launcher;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _runnerDirectory = string.IsNullOrWhiteSpace(runnerDirectory)
            ? ResolveRunnerDirectory()
            : runnerDirectory;
    }

    /// <summary>UpdateRunners 目录（不存在时由调用方在使用前建）。</summary>
    public static string ResolveRunnerDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GalExcleTools",
        RunnerDirectoryName);

    /// <summary>安装目录 = 当前 exe 所在目录（绿色版，原地替换，路径永不变）。</summary>
    public static string ResolveInstallDirectory() =>
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>信号文件路径：<c>UpdaterReady-{yyyyMMdd-HHmmss}-{guid}.signal</c>。</summary>
    public static string BuildReadySignalPath(string runnerDirectory, DateTimeOffset now, Guid token) =>
        Path.Combine(
            runnerDirectory,
            string.Format(CultureInfo.InvariantCulture, "{0}{1:yyyyMMdd-HHmmss}-{2:N}{3}", ReadySignalPrefix, now, token, ReadySignalExtension));

    /// <summary>
    /// 多实例预检（§9.7）：同名进程里只要有一个的映像路径**等于自己**，就认为本体工具开了两份。
    /// 按映像路径比对而不是只按进程名 —— 用户从别处复制的副本同在一台机器上跑，
    /// 名字一样但不会占住我们要替换的目录，不该拦住更新。
    /// </summary>
    public static (bool HasOther, string Detail) DetectOtherInstance()
    {
        var executableName = ResolveProcessName();
        if (executableName is null)
        {
            return (false, "拿不到当前进程名，跳过「另一个实例」预检。");
        }

        var selfPath = Environment.ProcessPath;
        var selfId = Environment.ProcessId;
        var others = new List<string>();

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(executableName);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return (false, "进程枚举失败，跳过「另一个实例」预检。");
        }

        foreach (var process in processes)
        {
            using (process)
            {
                if (process.Id == selfId)
                {
                    continue;
                }

                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // 读不到映像路径（权限、位数、保护进程）：宁可当成「另一个实例」拦住。
                    path = null;
                }

                if (path is null)
                {
                    others.Add($"PID {process.Id}（读不到映像路径，按另一个实例算）");
                    continue;
                }

                if (IsSameExecutable(path, selfPath))
                {
                    others.Add($"PID {process.Id}");
                }
            }
        }

        return others.Count == 0
            ? (false, "没有发现另一个本体工具实例。")
            : (true, string.Join("；", others));
    }

    /// <summary>两个路径是不是同一个可执行文件（用例要能脱离进程直接压这条判定）。</summary>
    public static bool IsSameExecutable(string? candidatePath, string? selfPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(selfPath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(selfPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// 准备 + 启动 + 等 READY 一整套。<paramref name="onProgress"/> 收进度文案，
    /// 由界面决定怎么显示（启动时那次自动更新是静默的，没有界面可显示）。
    /// </summary>
    public async Task<UpdateLaunchOutcome> RunAsync(
        UpdatePackagePlan plan,
        string installDirectory,
        string toolboxStableKey,
        string packagePath,
        Action<string>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var install = (installDirectory ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var runners = _runnerDirectory;
        var signalPath = BuildReadySignalPath(runners, _clock(), Guid.NewGuid());

        try
        {
            Directory.CreateDirectory(runners);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Fail(runners, signalPath, $"建不出热更新程序目录 {runners}：{error.Message}");
        }

        // ① 脚本外置：从安装目录抄到 UpdateRunners。抄不过去就不能继续 ——
        //    脚本自己会被镜像替换掉，回头修覆盖逻辑也只能靠这条链路（§9.6 带上 Scripts 的理由）。
        var sourceScript = Path.Combine(install, "Scripts", ScriptFileName);
        if (!File.Exists(sourceScript))
        {
            return Fail(runners, signalPath, $"安装目录里找不到热更新覆盖程序：{sourceScript}");
        }

        var runnerScript = Path.Combine(runners, ScriptFileName);
        if (!TryCopyWithRetry(sourceScript, runnerScript, out var copyError))
        {
            return Fail(runners, signalPath, $"复制热更新覆盖程序失败：{copyError}");
        }

        // ② 白名单：读生效中的那份（读不出来用内置规则），**总是**给 updater 写一份新的。
        var (whitelist, whitelistSource) = UpdateWhitelistLoader.Load(install);
        string whitelistPath;
        try
        {
            whitelistPath = UpdateWhitelistLoader.WriteRuntimeCopy(whitelist, runners);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Fail(runners, signalPath, $"写更新白名单失败：{error.Message}");
        }

        onProgress?.Invoke($"已准备热更新程序（镜像规则来自：{whitelistSource}）。");

        if (plan.Version is null || plan.Sha256 is null)
        {
            return Fail(runners, signalPath, "更新包信息不完整，无法交接给热更新程序。");
        }

        // ③ 组装命令行。★ 全程 ArgumentList：exe 名里有全角冒号、安装路径可能带中文，
        //    自己拼引号或者过 cmd.exe 都会在这些字符上翻车（§9.3）。
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", runnerScript,
            "-AppProcessId", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "-InstallDir", install,
            "-PackagePath", packagePath,
            "-ExpectedSha256", plan.Sha256,
            "-ToolboxStableKey", toolboxStableKey,
            "-TargetVersion", plan.Version,
            // 增量包必须让覆盖器知道`本机应当是哪一版`：它自己会再核对一次（对不上就拒绝执行）。
            // 整包时这个参数用不上（闸门直接放行），传了也无害。
            "-CurrentVersion", plan.DeltaBaseVersion ?? plan.Version,
            "-ReadySignalPath", signalPath,
            "-WorkingRoot", Path.GetDirectoryName(runners) ?? runners,
        };

        if (_launcher is null)
        {
            return Fail(runners, signalPath, "更新程序启动器不可用（界面壳没有实现 IUpdaterProcessLauncher）。");
        }

        var request = new UpdaterLaunchRequest(ResolvePowerShellPath(), arguments, runners, runnerScript, signalPath);
        if (!_launcher.TryStart(request, out var processId, out var launchError))
        {
            return Fail(runners, signalPath, $"启动热更新程序失败：{launchError}");
        }

        onProgress?.Invoke("热更新程序已启动，正在做预检（校验更新包、解压、找包清单）...");

        // ④ 等 READY。拿到了才允许主程序退出。
        var (ready, reason) = await WaitForReadyAsync(signalPath, processId, cancellationToken).ConfigureAwait(true);
        return ready
            ? new UpdateLaunchOutcome(true, true, "热更新程序已完成预检。", signalPath, runners, processId)
            : new UpdateLaunchOutcome(false, true, reason, signalPath, runners, processId);
    }

    /// <summary>
    /// 轮询等信号（§9.9）。三种收场：看到 READY、updater 提前退出、90 秒超时。
    /// 提前退出要单独判：脚本挂在参数预检/SHA/解压上时会立刻带着退出码走人，
    /// 这时再傻等 90 秒只会让用户对着一个假进度条发呆。
    /// </summary>
    public async Task<(bool Ready, string Reason)> WaitForReadyAsync(
        string signalPath,
        int processId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (true)
        {
            if (IsReadySignalWritten(signalPath))
            {
                return (true, "热更新程序已完成预检。");
            }

            if (processId > 0 && !IsProcessAlive(processId))
            {
                return (false, $"热更新程序还没预检完就退出了（日志在 {Path.GetDirectoryName(signalPath)}\\..\\UpdateLogs 下）。");
            }

            if (started.Elapsed >= ReadyTimeout)
            {
                return (false, "热更新程序预检超时。");
            }

            try
            {
                await Task.Delay(ReadyPollInterval, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return (false, "已取消等待热更新程序预检。");
            }
        }
    }

    /// <summary>信号文件在不在、内容有没有 READY。读到一半（文件正在写）当作还没写。</summary>
    public static bool IsReadySignalWritten(string signalPath)
    {
        if (string.IsNullOrWhiteSpace(signalPath) || !File.Exists(signalPath))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(signalPath);
            return text.Contains(ReadyMarker, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// powershell.exe 的绝对路径。优先用 System32 下那份 Windows PowerShell 5.1
    /// （脚本只按 5.1 的语法写，别让 PATH 上的 PowerShell 7 抢了先 —— 7 上
    /// <c>Add-Type -AssemblyName System.IO.Compression.FileSystem</c> 之类会走到不同实现）。
    /// </summary>
    public static string ResolvePowerShellPath()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrWhiteSpace(system))
        {
            var full = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(full))
            {
                return full;
            }
        }

        return "powershell.exe";
    }

    private static string? ResolveProcessName()
    {
        var path = Environment.ProcessPath;
        var name = string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static bool TryCopyWithRetry(string source, string destination, out string error)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                error = string.Empty;
                return true;
            }
            catch (Exception caught) when (caught is IOException or UnauthorizedAccessException)
            {
                last = caught;
                Thread.Sleep(100 * attempt);
            }
        }

        error = last?.Message ?? "未知错误";
        return false;
    }

    private static UpdateLaunchOutcome Fail(string runners, string signalPath, string message) =>
        new(false, false, message, signalPath, runners, 0);
}
