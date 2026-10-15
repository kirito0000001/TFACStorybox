using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.Services.Update;
using GalExcleTools.ViewModels;
using GalExcleTools.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools;

/// <summary>
/// 热更新在界面壳上的接线：把进度条、取消令牌、打开网页、起覆盖器交给
/// <c>UpdateViewModel</c>，外加启动 5 秒后的那一次自动检查、以及上一轮的延迟清理。
///
/// 为什么起进程 / 开网页这类动作必须走壳：<c>Services</c> 层只允许「准备与等待」
/// （<see cref="UpdateLaunchService"/> 就是那条线），真正 <c>Process.Start</c> 只出现在窗口里。
/// 顺带把这个类实现成 <see cref="IToolboxLogSink"/>，让更新链路的日志落进底部输出框。
/// </summary>
public sealed partial class MainWindow : IUpdateHost, IUpdaterProcessLauncher, IToolboxLogSink
{
    /// <summary>启动后延时多久做那一次自动检查（每次启动只做一次，没有定时器）。</summary>
    private static readonly TimeSpan UpdateStartupCheckDelay = TimeSpan.FromSeconds(5);

    private UpdateViewModel Update => _applicationViewModel.Update;

    /// <summary>
    /// 把窗口接成更新流程的宿主。必须在 <c>_applicationViewModel</c> 建好之后调，
    /// 且要早于 <see cref="CheckUpdateOnStartupAsync"/> —— 没有壳的话启动检查只会记日志。
    /// </summary>
    internal void AttachUpdateHost()
    {
        Update.AttachHost(this);
        ToolboxLog.SetSink(this);
    }

    void IUpdateHost.ShowGlobalProgress(string title, string detail) => ShowGlobalProgress(title, detail);

    void IUpdateHost.UpdateGlobalProgress(string message, double percent, string? detail) =>
        UpdateGlobalProgress(message, percent, detail);

    void IUpdateHost.CompleteGlobalProgress(string message, string? detail) => CompleteGlobalProgress(message, detail);

    Task IUpdateHost.HideGlobalProgressAfterDelayAsync(int delayMilliseconds) =>
        HideGlobalProgressAfterDelayAsync(delayMilliseconds);

    CancellationToken IUpdateHost.GetGlobalProgressCancellationToken() => GetGlobalProgressCancellationToken();

    void IUpdateHost.OpenReleasePage(string url) => OpenUrlInBrowser(url);

    bool IUpdaterProcessLauncher.TryStart(UpdaterLaunchRequest request, out int processId, out string error) =>
        TryStartUpdaterProcess(request, out processId, out error);

    /// <summary>
    /// 收尾并退出主程序，把安装目录让给已经在等的那份覆盖器。
    /// <b>只能在覆盖器写出 READY 之后调用</b>：反过来（先退出再预检）一旦预检失败，
    /// 用户就只剩一个关掉的窗口和一个没更新的目录。
    ///
    /// 这里只把待落盘的章节刷下去 —— 语音播放的释放与协调器的 Dispose 交给
    /// <c>AppWindow.Closing</c> 那条正常关窗路径做（<c>Application.Exit()</c> 会触发它），
    /// 免得同一套收尾跑两遍。
    /// </summary>
    void IUpdateHost.ExitForUpdate()
    {
        ToolboxLog.Info("主程序即将退出以完成热更新。");
        try
        {
            _storySaveCoordinator.FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            // 退出前的最后一次保存失败也要留痕，但绝不能因此卡住更新。
            ToolboxLog.Warn("退出前刷写章节保存失败，仍然继续退出。", error);
        }

        Application.Current.Exit();
    }

    /// <summary>
    /// 下载前的确认对话框。只有用户点「下载并更新」才会返回 true；
    /// 点关闭、按 Esc 都算「继续使用旧版」。
    /// </summary>
    async Task<bool> IUpdateHost.ConfirmUpdateAsync(UpdatePromptInfo info)
    {
        var request = new ContentDialogRequest(
            "发现新版本",
            DialogContentFactory.CreateUpdatePromptBody(
                info.CurrentVersion,
                info.NewVersion,
                info.ChannelText,
                info.PackageName,
                info.PackageSize,
                info.ReleaseNotes),
            "下载并更新",
            "继续使用旧版",
            DefaultButton: ContentDialogButton.Primary);
        var result = await _dialogService.ShowContentAsync(request);
        return result == DialogResultKind.Primary;
    }

    /// <summary>
    /// 启动时那一次自动检查。延时是为了让主界面先画出来；**失败绝不弹窗、绝不影响启动**。
    ///
    /// 检查之前先做一次**延迟一轮的清理**：只有上一轮那个版本确实启动过，
    /// 才轮到删 <c>*.old-{版本号}</c> 与待清理清单。它跑在后台线程上、自己也从不抛异常。
    ///
    /// AX 编译的 Debug 开发版**不做**自动检查（设置页那句说明就是这么写的）：
    /// 开发版跑在 <c>bin\</c> 下，静默把 Release 包铺进去会把开发目录搅乱。
    /// 手动点「检查更新 / 下载并更新」不受影响。
    /// </summary>
    internal async Task CheckUpdateOnStartupAsync()
    {
#if DEBUG
        await Task.CompletedTask;
#else
        try
        {
            await Task.Run(RunStartupCleanup);
            await Task.Delay(UpdateStartupCheckDelay);
            await Update.CheckOnStartupAsync();
        }
        catch (Exception error)
        {
            // 自动检查失败不能牵动启动流程：这里吞掉，但一定留痕。
            ToolboxLog.Warn("启动时的自动检查更新失败，已忽略。", error);
        }
#endif
    }

    /// <summary>
    /// 启动清理：回滚窗口的延迟删除 + 待清理清单 + 过期包。
    /// 放在后台线程上是因为它要递归扫一遍程序目录；<c>UpdateCleanup</c> 自己吞掉所有异常，
    /// 这里再兜一层，保证「清理」这件事永远不可能影响启动。
    /// </summary>
    private static void RunStartupCleanup()
    {
        try
        {
            var cleanup = new UpdateCleanup();
            var outcome = cleanup.Run(ApplicationVersionReader.ReadCurrentVersionText());
            if (outcome.PreviousRunProven)
            {
                // 留一行「为什么这轮清了 / 没清」，出问题时能一眼看出回滚窗口还在不在。
                ToolboxLog.Info($"启动清理：{outcome.Message}");
            }
        }
        catch (Exception error)
        {
            ToolboxLog.Warn("启动清理回滚文件失败，已忽略（回滚窗口多留一轮，方向是安全的）。", error);
        }
    }

    /// <summary>用系统默认浏览器打开 Release 页面。</summary>
    private void OpenUrlInBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception error)
        {
            AppendLog(LogKind.Warning, $"打开更新页面失败：{url}", error);
            ShowFloatingTip(InfoBarSeverity.Warning, "打不开更新页面", error.Message, error, $"打开更新页面失败：{url}");
        }
    }

    /// <summary>
    /// 起那份覆盖器（powershell 跑 <c>UpdateRunners\热更新覆盖.ps1</c>）。
    /// 全程 <c>ArgumentList</c>：程序名里有全角冒号、安装路径可能带中文，
    /// 自己拼引号或者过一层 <c>cmd.exe</c> 都会在这些字符上翻车。
    /// </summary>
    private bool TryStartUpdaterProcess(UpdaterLaunchRequest request, out int processId, out string error)
    {
        processId = 0;
        error = string.Empty;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.ExecutablePath,
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "进程没有起来（Process.Start 返回空）。";
                return false;
            }

            using (process)
            {
                processId = process.Id;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 更新链路（Services/Update）的日志出口。它可能从任何线程被调进来，
    /// <see cref="AppendLog"/> 自己会把动作排队回 UI 线程。
    /// </summary>
    void IToolboxLogSink.Write(ToolboxLogLevel level, string message, Exception? error)
    {
        var kind = level switch
        {
            ToolboxLogLevel.Warning => LogKind.Warning,
            ToolboxLogLevel.Error => LogKind.Error,
            _ => LogKind.Info,
        };
        AppendLog(kind, message, error);
    }
}
