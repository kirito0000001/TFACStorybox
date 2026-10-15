using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.ViewModels;

/// <summary>
/// 「发现新版本」确认对话框要显示的东西（设计 §11.3）。<see cref="PackageSize"/> 是原始字节数，
/// 由壳负责格式化。
/// </summary>
internal sealed record UpdatePromptInfo(
    string CurrentVersion,
    string NewVersion,
    string ChannelText,
    string PackageName,
    long PackageSize,
    string? ReleaseNotes);

/// <summary>
/// 更新流程要「壳」提供的东西：进度条、取消令牌、打开网页。
///
/// 项目惯例：能力由 MainWindow 提供，VM 只调用（对照 <c>ISequenceFrameDuplicateDetectionHost</c> /
/// <c>IUnrealSyncPublishHost</c>）。这样 VM 里不会出现 <c>Process.Start</c>，<c>Services</c> 层也不会
/// ——那里有护栏盯着（<c>ProcessOrchestrationIsNotDuplicated</c>）。
/// </summary>
internal interface IUpdateHost
{
    /// <summary>打开全局进度条（手动检查 / 下载时用；启动时的静默检查<b>不</b>打开）。</summary>
    void ShowGlobalProgress(string title, string detail);

    /// <summary>更新进度条上的文字与百分比（下载阶段用，对照 <c>MainWindow.Progress.cs</c> 的四参签名）。</summary>
    void UpdateGlobalProgress(string message, double percent, string? detail = null);

    /// <summary>下载前的确认对话框（设计 §11.3）。用户点「继续使用旧版」返回 false。</summary>
    Task<bool> ConfirmUpdateAsync(UpdatePromptInfo info);

    /// <summary>把进度条切到完成态并写一句结果。</summary>
    void CompleteGlobalProgress(string message, string? detail = null);

    /// <summary>延迟收起进度条（留一点时间让用户看到结果）。</summary>
    Task HideGlobalProgressAfterDelayAsync(int delayMilliseconds = 1400);

    /// <summary>
    /// 进度条上的取消令牌（<c>MainWindow.Progress.cs</c> 的 ✕ 入口）。
    /// 只有进度条打开时它才有意义——静默检查必须传 <see cref="CancellationToken.None"/>。
    /// </summary>
    CancellationToken GetGlobalProgressCancellationToken();

    /// <summary>用系统默认浏览器打开 Release 页面。</summary>
    void OpenReleasePage(string url);

    /// <summary>
    /// 收尾并退出主程序，把安装目录让给已经在等的那份覆盖器（设计 §9.9）。
    /// <b>只能在 updater 写出 READY 之后调用</b>：反过来（先退出再预检）一旦预检失败，
    /// 用户就只剩一个关掉的窗口和一个没更新的目录。
    /// 退出前必须把各处防抖保存落盘——走路要跟用户按 ✕ 关窗时完全一样。
    /// </summary>
    void ExitForUpdate();
}
