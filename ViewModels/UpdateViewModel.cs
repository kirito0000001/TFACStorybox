using System;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.Services.Update;

namespace GalExcleTools.ViewModels;

/// <summary>
/// 热更新面板的状态与命令（设计 §11）。
///
/// 它只做三件事：算这一次该不该发请求（请求预算）、把结果翻成界面文案、把用户动作变成对
/// <see cref="UpdateService"/> 的一次调用。HTTP、文件、进程都在别处。
///
/// ★ 状态机是 fail-closed 的（设计 §11.6）：只有 <see cref="UpdatePhase.UpdateAvailable"/>
/// 允许进入下载与覆盖；失败**绝不**退化成「已是最新版本」。
/// </summary>
internal sealed class UpdateViewModel : ObservableObject
{
    private readonly SettingsViewModel _settings;
    private IUpdateHost? _host;
    private readonly Func<DateTimeOffset> _clock;
    private readonly UpdateLaunchService? _launchService;

    private UpdatePhase _phase = UpdatePhase.Idle;
    private string _statusText = string.Empty;
    private string _detailText = string.Empty;
    private UpdateCheckResult? _lastResult;
    private DateTimeOffset? _lastCheckAt;
    private bool _autoCheckAttempted;
    private bool _isDownloading;

    public UpdateViewModel(
        SettingsViewModel settings,
        UpdateService? service = null,
        IUpdateHost? host = null,
        Func<DateTimeOffset>? clock = null,
        UpdateLaunchService? launchService = null)
    {
        _settings = settings;
        Service = service ?? new UpdateService();
        _host = host;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _launchService = launchService;

        CurrentVersionText = ApplicationVersionReader.ReadCurrentVersionText();
        CheckUpdateCommand = new AsyncRelayCommand(ExecuteCheckCommandAsync, () => CanCheck);
        TestConnectionCommand = new AsyncRelayCommand(ExecuteTestConnectionCommandAsync, () => CanCheck);
        OpenReleasePageCommand = new RelayCommand(OpenReleasePage);
        DownloadAndInstallCommand = new AsyncRelayCommand(ExecuteDownloadCommandAsync, () => CanDownload);
    }

    /// <summary>
    /// 界面壳接上来（进度条、取消令牌、打开网页）。走「构造之后再接」而不是构造参数，
    /// 跟 <c>AtlasTools.AttachHost</c> / <c>SequenceFrames.AttachCommandHost</c> 一个路子：
    /// 壳里先建 VM 再把自己交出去，测试里不接也能跑（没壳就只记日志）。
    /// </summary>
    internal void AttachHost(IUpdateHost host)
    {
        _host = host;
    }

    /// <summary>检查/下载的编排器（M3 起下载也走它）。</summary>
    public UpdateService Service { get; }

    /// <summary>本机版本（读程序集，四级回退；见设计 §11.4）。</summary>
    public string CurrentVersionText { get; }

    /// <summary>关于区显示的版本号。</summary>
    public string AboutVersionText => $"版本 {CurrentVersionText}";

    public UpdatePhase Phase
    {
        get => _phase;
        private set
        {
            if (!SetProperty(ref _phase, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsChecking));
            OnPropertyChanged(nameof(HasUpdate));
            CheckUpdateCommand.NotifyCanExecuteChanged();
            TestConnectionCommand.NotifyCanExecuteChanged();
            DownloadAndInstallCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>正在检查（此期间按钮禁用；「取消」在进度条上）。</summary>
    public bool IsChecking => _phase == UpdatePhase.Checking;

    /// <summary>有可用的更新（plan 五项齐全）——M3 的「下载并更新」按钮用它做唯一入口条件。</summary>
    public bool HasUpdate => _phase == UpdatePhase.UpdateAvailable;

    /// <summary>正在下载并校验更新包（此期间「下载并更新」禁用；「取消」在进度条上）。</summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                DownloadAndInstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>这一次/最近一次的状态文案（失败原因原样显示，不美化）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>细节（版本号、通道、包名……），给愿意看的人看。</summary>
    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public AsyncRelayCommand CheckUpdateCommand { get; }

    public AsyncRelayCommand TestConnectionCommand { get; }

    public RelayCommand OpenReleasePageCommand { get; }

    /// <summary>下载并校验更新包（设计 §8）。覆盖替换属于 M4，这里只把包准备好。</summary>
    public AsyncRelayCommand DownloadAndInstallCommand { get; }

    /// <summary>检查更新：手点。</summary>
    public Task CheckForUpdateAsync(CancellationToken commandToken = default) =>
        RunCheckAsync(isManual: true, commandToken);

    /// <summary>
    /// 启动时的自动检查（设计 §7.1：启动后<b>只</b>触发一次；延时由壳负责，VM 不碰计时器）。
    /// </summary>
    public async Task CheckOnStartupAsync()
    {
        if (_autoCheckAttempted)
        {
            // §7.3 第 2 条：每次启动只做一次。
            return;
        }

        _autoCheckAttempted = true;

        if (!_settings.UpdateAutoCheckEnabled)
        {
            StatusText = "自动检查更新已关闭。";
            DetailText = "可以随时点「检查更新」。";
            return;
        }

        await RunCheckAsync(isManual: false, CancellationToken.None);

        // ★ 启动时查到新版本也要**弹窗**（2026-10-06 用户要求，对齐 FantasyTools 的
        //   CheckForUpdatesAsync(showNoUpdateTip: true, allowUpdatePrompt: true, isStartupCheck: true)）：
        //   启动路径不显示进度框，但查到可装的更新就走和「下载并更新」按钮**同一条**确认 →
        //   下载 → 校验 → 覆盖的路线（不另写一份，免得两条路各长各的）。
        if (HasUpdate && _host is not null)
        {
            await ExecuteDownloadCommandAsync(CancellationToken.None);
        }
    }

    private bool CanCheck => !IsChecking;

    private bool CanDownload => HasUpdate && !IsDownloading;

    private Task ExecuteCheckCommandAsync(CancellationToken commandToken) => RunCheckAsync(isManual: true, commandToken);

    private async Task ExecuteTestConnectionCommandAsync(CancellationToken commandToken)
    {
        if (IsChecking)
        {
            // §7.3 第 6 条：绝不并发。
            return;
        }

        var timeoutSeconds = UpdateRequestBudget.ClampTimeoutSeconds(_settings.UpdateConnectionTimeoutSecondsValue);
        var host = _host;

        Phase = UpdatePhase.Checking;
        StatusText = "正在测试 Gitee 连接…";
        DetailText = $"仓库 {Service.Repository}，超时 {timeoutSeconds} 秒。";

        // ★ 同 RunCheckAsync：先开进度框（它会新建取消令牌源）再取令牌。
        host?.ShowGlobalProgress("测试 Gitee 连接", $"正在访问 {Service.Repository}…");

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            host?.GetGlobalProgressCancellationToken() ?? CancellationToken.None);

        try
        {
            var result = await Service.TestConnectionAsync(timeoutSeconds, linkedSource.Token);
            StatusText = result.Message;

            if (!result.Ok)
            {
                Phase = UpdatePhase.Failed;
            }
            else
            {
                Phase = UpdatePhase.Idle;
            }

            host?.CompleteGlobalProgress(result.Ok ? "连接正常" : "连接失败", result.Message);
        }
        catch (OperationCanceledException)
        {
            Phase = UpdatePhase.Idle;
            StatusText = "测试已取消";
            DetailText = "没有下载或替换任何文件。";
            host?.CompleteGlobalProgress("测试已取消", string.Empty);
        }
        finally
        {
            if (host is not null)
            {
                await host.HideGlobalProgressAfterDelayAsync(900);
            }
        }
    }

    private async Task RunCheckAsync(bool isManual, CancellationToken commandToken)
    {
        if (IsChecking)
        {
            // §7.3 第 6 条：绝不并发。
            return;
        }

        var now = _clock();
        var verdict = UpdateRequestBudget.Evaluate(
            isManual,
            now,
            _lastCheckAt ?? _settings.UpdateLastCheckAt,
            _lastResult is not null);

        if (verdict.Decision == UpdateBudgetDecision.ReuseCache && _lastResult is not null)
        {
            ApplyResult(_lastResult, now, reused: true, verdict.Note);
            return;
        }

        var channel = _settings.UpdateChannel == UpdateChannel.Beta ? UpdateChannel.Beta : UpdateChannel.Stable;
        var runtime = UpdateRuntime.Current;
        var timeoutSeconds = UpdateRequestBudget.ClampTimeoutSeconds(_settings.UpdateConnectionTimeoutSecondsValue);
        var host = isManual ? _host : null;

        Phase = UpdatePhase.Checking;
        StatusText = "正在检查更新…";
        DetailText = $"通道 {channel}，当前版本 {CurrentVersionText}，包类型 {runtime}。"
            + (string.IsNullOrWhiteSpace(verdict.Note) ? string.Empty : $" {verdict.Note}");

        // ★ 顺序不能反：`ShowGlobalProgress` 会**新建**取消令牌源，所以必须先开进度框、再取令牌。
        //   先取的话拿到的是「上一次已经结束的操作」留下的令牌（没有就是 None），
        //   进度条上的 ✕ 点了等于没点 —— 检查和下载都会cancel不掉（2026-10-06 修）。
        host?.ShowGlobalProgress("检查更新", $"正在查询 Gitee（{channel} 通道）…");

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            host?.GetGlobalProgressCancellationToken() ?? CancellationToken.None);

        try
        {
            var request = new UpdateCheckRequest(channel, CurrentVersionText, runtime, timeoutSeconds);
            var result = await Service.CheckForUpdateAsync(request, linkedSource.Token);
            ApplyResult(result, _clock(), reused: false, verdict.Note);

            host?.CompleteGlobalProgress(
                result.HasUpdate ? "发现新版本" : "检查完成",
                result.Message);
        }
        catch (OperationCanceledException)
        {
            Phase = UpdatePhase.Idle;
            StatusText = "检查已取消";
            DetailText = "没有下载或替换任何文件。";
            host?.CompleteGlobalProgress("检查已取消", string.Empty);
        }
        finally
        {
            if (host is not null)
            {
                await host.HideGlobalProgressAfterDelayAsync(900);
            }
        }
    }

    /// <summary>
    /// 「下载并更新」：确认 → 下载 → 大小 / magic / SHA-256 / 包结构四道校验（设计 §8.1）
    /// → 交接给覆盖器（M4）。
    /// 校验的事全在 <c>UpdateDownloader</c> 里；这里只把结果翻成界面文案，并守住
    /// 「失败绝不进「已是最新」、也绝不进覆盖」这条线（§11.6）。
    /// </summary>
    private async Task ExecuteDownloadCommandAsync(CancellationToken commandToken)
    {
        if (IsDownloading || IsChecking)
        {
            return;
        }

        var plan = _lastResult?.Plan;
        if (plan is null || !UpdatePackagePlan.IsComplete(plan) || !HasUpdate)
        {
            StatusText = "当前没有可用的更新包。";
            DetailText = "请先点「检查更新」。";
            return;
        }

        var host = _host;
        if (host is null)
        {
            // 没有壳（测试里）就不下载：确认对话框是这条路线唯一的入口，不能绕过。
            ToolboxLog.Info("没有界面壳，已跳过下载更新包。");
            return;
        }

        // ★ 多实例预检放在下载**之前**（设计 §9.7）：另一个实例占着安装目录时，
        // 覆盖阶段必然失败 —— 那就不该先让用户等一百多兆。
        var (hasOtherInstance, instanceDetail) = UpdateLaunchService.DetectOtherInstance();
        if (hasOtherInstance)
        {
            Phase = UpdatePhase.UpdateAvailable;
            StatusText = "检测到另一个本体工具正在运行，请先关闭它再更新。";
            DetailText = instanceDetail;
            return;
        }

        var info = new UpdatePromptInfo(
            CurrentVersionText,
            plan.Version,
            _settings.UpdateChannelText,
            plan.FileName,
            plan.SizeBytes,
            _lastResult?.ReleaseNotes);

        if (!await host.ConfirmUpdateAsync(info))
        {
            StatusText = "已取消更新，继续使用当前版本。";
            DetailText = "没有下载或替换任何文件。";
            return;
        }

        var packageText = $"{plan.FileName}（{UpdateDownloadProgress.FormatSize(plan.SizeBytes)}）";
        IsDownloading = true;
        StatusText = "正在下载更新包…";
        DetailText = $"{packageText}，SHA-256 {plan.Sha256[..8]}…";

        // ★ 同 RunCheckAsync：先开进度框（它会新建取消令牌源）再取令牌，否则下载「取消」不掉。
        host.ShowGlobalProgress("下载更新包", packageText);

        var timeoutSeconds = UpdateRequestBudget.ClampTimeoutSeconds(_settings.UpdateConnectionTimeoutSecondsValue);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            host.GetGlobalProgressCancellationToken());

        // Progress 在 UI 线程上构造，回调自然回到 UI 线程（§11.5：进度只走全局进度条）。
        var progress = new Progress<UpdateDownloadProgress>(state =>
        {
            var received = state.FormatBytes();
            StatusText = state.Stage;
            DetailText = $"{received}（{state.Percent}%）";
            host.UpdateGlobalProgress(state.Stage, state.Percent, received);
        });

        try
        {
            var result = await Service.DownloadAndVerifyAsync(plan, timeoutSeconds, progress, linkedSource.Token);

            if (result.Ok && result.PackagePath is { Length: > 0 } packagePath)
            {
                StatusText = result.Message;
                DetailText = result.Reused
                    ? $"已复用缓存：{packagePath}"
                    : $"已保存到：{packagePath}";

                host.CompleteGlobalProgress("更新包已准备就绪", result.Message);

                // 包已经落盘、四道校验都过了，接下来才是 M4 的交接（§9.9）。
                await LaunchUpdaterAsync(host, plan, packagePath, linkedSource.Token);
            }
            else if (result.Ok)
            {
                // 不该发生（Success 工厂一定会给包路径）。真发生了也绝不能进覆盖。
                Phase = UpdatePhase.Failed;
                StatusText = "更新包校验通过，但没有拿到包路径。";
                DetailText = "可以重试，或者自己从 Release 页面手动下载。";
                host.CompleteGlobalProgress("下载失败", StatusText);
            }
            else
            {
                // fail-closed：下载/校验失败既不进「已是最新」，也不许进覆盖（§11.6）。
                Phase = UpdatePhase.Failed;
                StatusText = result.Message;
                DetailText = string.IsNullOrWhiteSpace(result.Detail)
                    ? "可以重试，或者自己从 Release 页面手动下载。"
                    : result.Detail!;
                host.CompleteGlobalProgress("下载失败", result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            // 取消不算失败：plan 还有效，随时可以再来一次（设计 §11.5）。
            Phase = UpdatePhase.UpdateAvailable;
            StatusText = "已取消，没有下载或替换任何文件。";
            DetailText = "更新包没有准备好，可以再点一次「下载并更新」。";
            host.CompleteGlobalProgress("下载已取消", string.Empty);
        }
        finally
        {
            IsDownloading = false;
            await host.HideGlobalProgressAfterDelayAsync(900);
        }
    }

    /// <summary>
    /// 把已经校验好的更新包交给覆盖器，并在它写出 READY 之后才让主程序退出（设计 §9.9）。
    ///
    /// 顺序不能反：先预检、再退出，是「预检没过时主程序还开着、安装目录一个字没动」的唯一保证。
    /// 反过来的话，用户看到的就是一个关掉的窗口和半个安装目录。
    /// </summary>
    private async Task LaunchUpdaterAsync(
        IUpdateHost host,
        UpdatePackagePlan plan,
        string packagePath,
        CancellationToken cancellationToken)
    {
        if (_launchService is null && host is not IUpdaterProcessLauncher)
        {
            Phase = UpdatePhase.Failed;
            StatusText = "更新程序启动器不可用，无法自动替换。";
            DetailText = "请到 Release 页面手动下载更新包，解压后覆盖安装目录。";
            host.CompleteGlobalProgress("无法自动替换", StatusText);
            return;
        }

        var launchService = _launchService
            ?? new UpdateLaunchService((IUpdaterProcessLauncher)host, _clock);

        StatusText = "正在准备热更新程序…";
        DetailText = $"覆盖程序目录：{UpdateLaunchService.ResolveRunnerDirectory()}";

        var outcome = await launchService.RunAsync(
            plan,
            UpdateLaunchService.ResolveInstallDirectory(),
            Service.StableKey,
            packagePath,
            message =>
            {
                StatusText = message;
                host.UpdateGlobalProgress(message, 0, null);
            },
            cancellationToken);

        if (!outcome.Ready)
        {
            // 预检没过（或者覆盖器压根没起来）：主程序继续跑，安装目录一个字没动（§9.9）。
            Phase = UpdatePhase.Failed;
            StatusText = outcome.Message;
            DetailText = outcome.ProcessStarted
                ? $"覆盖程序目录：{outcome.RunnerDirectory}，日志在同级的 UpdateLogs 下；这次没有替换任何文件。"
                : "更新包已经下载好了，可以稍后再试，也可以自己解压覆盖。";
            host.CompleteGlobalProgress("热更新未开始", outcome.Message);
            return;
        }

        StatusText = outcome.Message;
        DetailText = "主程序即将退出并开始覆盖，覆盖完成后会自动重新打开。";
        host.UpdateGlobalProgress("主程序即将退出并开始覆盖...", 100, null);
        host.ExitForUpdate();
    }

    private void ApplyResult(UpdateCheckResult result, DateTimeOffset now, bool reused, string? verdictNote)
    {
        _lastResult = result;
        _lastCheckAt = now;

        // fail-closed：只有「确认有更新且 plan 齐全」和「确认已是最新」才是好消息（§11.6）。
        // 其余（空列表 / 无清单 / 没有本机包 / 需要手动迁移 / 限流 / 403 / 404 / 任何失败）一律 Failed，
        // 因为对用户来说它们的下一步都是「重来或手动下载」，绝不是「你已是最新」。
        Phase = result.Status switch
        {
            UpdateCheckStatus.Latest or UpdateCheckStatus.LocalIsNewer => UpdatePhase.Ready,
            UpdateCheckStatus.UpdateAvailable when result.HasUpdate => UpdatePhase.UpdateAvailable,
            UpdateCheckStatus.NotChecked => UpdatePhase.Idle,
            _ => UpdatePhase.Failed
        };

        StatusText = reused ? $"（沿用 {UpdateRequestBudget.ManualCacheWindow.TotalSeconds:0} 秒内的结果）{result.Message}" : result.Message;
        DetailText = BuildDetail(result, reused, verdictNote);

        // 记时间到 settings.json：请求预算的手点缓存靠它（界面上不再显示「上次检查」）。
        _settings.RecordUpdateCheckResult(now);
    }

    private static string BuildDetail(UpdateCheckResult result, bool reused, string? verdictNote)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (!string.IsNullOrWhiteSpace(result.ReleaseTag))
        {
            parts.Add($"Release {result.ReleaseTag}");
        }

        if (result.Plan is { } plan)
        {
            parts.Add($"{plan.FileName}（{plan.SizeBytes} 字节，SHA-256 {plan.Sha256[..8]}…）");
        }

        if (result.RateLimitRemaining is { } remaining)
        {
            parts.Add($"剩余配额 {remaining} 次");
        }

        if (reused)
        {
            parts.Add($"本次未发请求（{UpdateRequestBudget.ManualCacheWindow.TotalSeconds:0} 秒缓存）");
        }

        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            parts.Add(result.Detail!);
        }

        if (!string.IsNullOrWhiteSpace(verdictNote))
        {
            parts.Add(verdictNote!);
        }

        return string.Join(" · ", parts);
    }

    private void OpenReleasePage()
    {
        var url = _lastResult?.ReleaseNotesUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = Service.Repository.ReleasePageUrl;
        }

        if (_host is { } host)
        {
            host.OpenReleasePage(url!);
            return;
        }

        // 没有壳（测试里）时只记一行，不做任何进程或浏览器动作。
        ToolboxLog.Info($"Release 页面：{url}");
    }
}
