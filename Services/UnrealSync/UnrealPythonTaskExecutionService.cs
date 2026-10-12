using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

/// <summary>
/// 「这次 Unreal 任务该怎么起进程」。调用方只认这个返回值，不关心走的是哪条路。
/// </summary>
/// <param name="StartInfo">真正拿去做 Process.Start 的那一份。</param>
/// <param name="UsesRunningEditor">
/// 是不是在线路（连已经在运行的编辑器）。false 表示这是离线路（冷启无头编辑器）。
/// 调用方需要在日志里体现出来 —— 两条路的耗时差一个量级，不说清就会有人以为卡死了。
/// </param>
internal sealed record UnrealPythonTaskLaunch(ProcessStartInfo StartInfo, bool UsesRunningEditor);

/// <summary>
/// 虚幻任务的**在线 / 离线**选路与启动构造。这是「双路」的唯一定义处。
///
/// 【两条路是什么】
///   在线：引擎自带 Python 跑 <c>Tools/UnrealBridge/run_remote_unreal_job.py</c>，
///         走 Unreal 的 Python 远程执行协议，任务脚本在**已经在运行的编辑器**里执行。
///         编辑器开着时快约十倍，而且能读到离线实例读不到的那类资产
///         （WidgetBlueprint / MetaSoundSource —— 实测同一工程在线 23 项 0 错、
///         离线 4 项全报"未找到"）。
///   离线：冷启 <c>UnrealEditor-Cmd.exe -ExecutePythonScript=...</c>（本项目原来的唯一一条路）。
///         慢十几到 40 秒，但不要求编辑器开着。
///
/// 【回退规则】
/// 只有「连不上编辑器」才回退。脚本自己失败**不**回退 —— 退回去也是一样的错，
/// 白等一次编辑器冷启动。判据是输出里带 <see cref="RemoteUnavailableMarker"/>。
///
/// 本项目选的是「先搭骨架、只给同步这一条链路走在线」（2026-10-12 决定），
/// 所以眼下只有同步会调这里；其它链路继续直接走离线。
/// </summary>
internal sealed class UnrealPythonTaskExecutionService
{
    /// <summary>
    /// 在线脚本连不上编辑器时会带上的标记。
    /// **必须和 Tools/UnrealBridge/run_remote_unreal_job.py 里的字面量一致。**
    /// </summary>
    public const string RemoteUnavailableMarker = "GAL_REMOTE_UNAVAILABLE";

    /// <summary>随程序发布的在线执行脚本（相对 BaseDirectory）。</summary>
    public const string RemoteRunnerRelativePath = @"Tools\UnrealBridge\run_remote_unreal_job.py";

    private static readonly JsonSerializerOptions JobJsonOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>这次失败是不是「编辑器不可用」，可以退回离线执行。</summary>
    public static bool IsRemoteUnavailable(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(RemoteUnavailableMarker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 造出「这次该起哪个进程」。
    ///
    /// <paramref name="useRunningEditor"/> 传 null 才自动判定；
    /// 传 true 表示这条链路**只能**走在线（判不出来也该明确报错，不该悄悄降级）。
    /// </summary>
    public UnrealPythonTaskLaunch BuildLaunch(
        string editorPath,
        string projectPath,
        string scriptPath,
        string remoteJobPath,
        ProcessStartInfo offlineStartInfo,
        IReadOnlyDictionary<string, string>? environment = null,
        bool? useRunningEditor = null)
    {
        ArgumentNullException.ThrowIfNull(offlineStartInfo);

        var normalizedEditorPath = Path.GetFullPath(editorPath);
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var normalizedScriptPath = Path.GetFullPath(scriptPath);
        if (!(useRunningEditor ?? ShouldUseRunningEditor(normalizedEditorPath, normalizedProjectPath)))
        {
            return new UnrealPythonTaskLaunch(offlineStartInfo, UsesRunningEditor: false);
        }

        var engineRoot = ResolveEngineRoot(normalizedEditorPath);
        var pythonPath = ResolveBundledPythonPath(engineRoot);
        var runnerPath = Path.Combine(AppContext.BaseDirectory, RemoteRunnerRelativePath);
        if (!File.Exists(pythonPath))
        {
            throw new FileNotFoundException("当前 Unreal 引擎没有找到远程执行所需的 Python。", pythonPath);
        }

        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException("工具箱内置的 Unreal 在线执行脚本不存在。", runnerPath);
        }

        var job = new UnrealRemotePythonJob(
            engineRoot,
            normalizedProjectPath,
            normalizedScriptPath,
            environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        WriteJobFile(remoteJobPath, job);

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"{Quote(runnerPath)} --job {Quote(Path.GetFullPath(remoteJobPath))}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(pythonPath) ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 把子进程锁死在 UTF-8 上（见下面 PYTHONUTF8），父进程这边也必须按 UTF-8 解码。
            // 不指定的话 .NET 按 OEM 代码页解（中文 Windows 是 936），
            // 而这条路的报错**只能**从这段输出里看 —— 乱码等于线索全丢。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        return new UnrealPythonTaskLaunch(startInfo, UsesRunningEditor: true);
    }

    /// <summary>
    /// 跑一次任务，在线连不上时自动退回离线。
    ///
    /// 放在服务里而不是窗口分部类里：这是**选路逻辑**，不是界面逻辑。
    /// 窗口只需要传两个"要说的话"的回调（进日志 / 弹提示条），
    /// 别让它在 MainWindow 里再抄一遍 try/catch。
    /// </summary>
    /// <param name="launch"><see cref="BuildLaunch"/> 的结果。</param>
    /// <param name="offlineStartInfo">离线的启动参数。**即使这次走在线也要给** —— 回退时用它。</param>
    /// <param name="run">真正把 StartInfo 拿去跑的那一步（通常包给 <see cref="UnrealSyncProcessRunner"/>）。</param>
    /// <param name="onNoOnlineEditor">
    /// 没能匹配到在线编辑器、直接走离线时的回调。
    ///
    /// 这条**必须留痕**：编辑器明明开着，却因为"匹配不上"而起了一个离线实例，
    /// 耗时从"瞬间"变成 40 秒，离线上下文还可能读不到某些资产、报出看着像工程坏了的错。
    /// 不留痕的话，用户只会觉得"这次怎么又慢又报错"。
    /// </param>
    /// <param name="onOfflineFallback">在线失败、退回离线时的回调，参数是原始错误消息。</param>
    public static async Task<T> RunWithOfflineFallbackAsync<T>(
        UnrealPythonTaskLaunch launch,
        ProcessStartInfo offlineStartInfo,
        Func<ProcessStartInfo, Task<T>> run,
        Action<string>? onNoOnlineEditor = null,
        Action<string>? onOfflineFallback = null)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(offlineStartInfo);
        ArgumentNullException.ThrowIfNull(run);

        if (!launch.UsesRunningEditor)
        {
            onNoOnlineEditor?.Invoke("没有匹配到可用的在线编辑器，本次直接走离线执行（较慢）。");
            return await run(launch.StartInfo).ConfigureAwait(false);
        }

        try
        {
            return await run(launch.StartInfo).ConfigureAwait(false);
        }
        catch (Exception error) when (IsRemoteUnavailable(error))
        {
            // 只有「连不上编辑器」才回退。脚本自己失败退回去也是一样的错，
            // 白等一次编辑器冷启动。
            onOfflineFallback?.Invoke(error.Message);
            return await run(offlineStartInfo).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 只有「打开的正是当前选中项目、且用的是当前选中引擎」的编辑器才算在线执行目标。
    ///
    /// 只判断"有没有 UnrealEditor 进程"是不够的：开着**别的**工程时也会走在线路，
    /// 然后在连接阶段整个失败 —— 而这种情况本该直接走离线。
    /// </summary>
    public bool ShouldUseRunningEditor(string? editorPath, string? projectPath)
    {
        var normalizedProjectPath = SafeFullPath(projectPath);
        if (string.IsNullOrWhiteSpace(normalizedProjectPath))
        {
            return false;
        }

        var engineRoot = SafeFullPath(TryResolveEngineRoot(editorPath));
        foreach (var descriptor in EnumerateRunningEditorDescriptors())
        {
            if (!MatchesProject(descriptor, normalizedProjectPath))
            {
                continue;
            }

            // 引擎根目录拿不到时不再额外设限，项目匹配已经足够定位目标编辑器。
            if (string.IsNullOrWhiteSpace(engineRoot) ||
                descriptor.Contains(engineRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>引擎根目录：<c>&lt;UE&gt;/Engine</c>（编辑器的上两级）。</summary>
    public static string ResolveEngineRoot(string editorPath)
    {
        var editorDirectory = Path.GetDirectoryName(Path.GetFullPath(editorPath))
            ?? throw new InvalidOperationException("无法确定 UnrealEditor.exe 所在目录。");
        return Path.GetFullPath(Path.Combine(editorDirectory, "..", ".."));
    }

    /// <summary>引擎自带的 Python（不是系统 Python）。</summary>
    public static string ResolveBundledPythonPath(string engineRoot) =>
        Path.Combine(engineRoot, "Binaries", "ThirdParty", "Python3", "Win64", "python.exe");

    private static void WriteJobFile(string remoteJobPath, UnrealRemotePythonJob job)
    {
        var normalizedJobPath = Path.GetFullPath(remoteJobPath);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedJobPath)!);
        // 先写临时文件再改名：编辑器侧可能正在读上一份，直接覆盖会读出半截 JSON。
        var temporaryPath = normalizedJobPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(job, JobJsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, normalizedJobPath, overwrite: true);
    }

    private static bool MatchesProject(string descriptor, string normalizedProjectPath)
    {
        // 编辑器主窗口标题形如「Lvl_Xxx - MyWhiteHairedKiller」，只带项目名不带路径。
        var projectName = Path.GetFileNameWithoutExtension(normalizedProjectPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return false;
        }

        return descriptor.Contains(normalizedProjectPath, StringComparison.OrdinalIgnoreCase) ||
            descriptor.Split('|')[^1]
                .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(token => string.Equals(token, projectName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 取运行中编辑器的可识别信息：主模块路径（定位引擎）和主窗口标题（含项目名）。
    ///
    /// 不用 WMI 读命令行 —— 为这一个判断给工具箱引入 System.Management 依赖不划算。
    /// </summary>
    private static IEnumerable<string> EnumerateRunningEditorDescriptors()
    {
        List<string> descriptors = [];
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("UnrealEditor");
        }
        catch
        {
            return descriptors;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    var modulePath = string.Empty;
                    try
                    {
                        modulePath = process.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        // 32/64 位或权限差异会读不到主模块，标题仍然可用。
                    }

                    descriptors.Add($"{modulePath}|{process.MainWindowTitle}");
                }
                catch
                {
                    // 单个进程读不到就跳过，不影响其它候选。
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return descriptors;
    }

    private static string TryResolveEngineRoot(string? editorPath)
    {
        try
        {
            return string.IsNullOrWhiteSpace(editorPath)
                ? string.Empty
                : ResolveEngineRoot(Path.GetFullPath(editorPath));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeFullPath(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value);
        }
        catch
        {
            return value ?? string.Empty;
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
