using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.ViewModels;

/// <summary>
/// 界面上的一行 = 一条检测项 + 它该显示成什么颜色。
///
/// 文字全部**转发**自 <see cref="UnrealSyncCheckItem"/> —— 那边已经算好了
/// （照零境的做法：标题 / 期望 / 说明 / 状态 / 复制什么，都写在模型里），
/// 这里只补一样模型不该有的东西：颜色。
/// </summary>
internal sealed record UnrealSyncCheckRow(UnrealSyncCheckItem Item, Brush? StatusBrush)
{
    public string DisplayName => Item.DisplayName;

    public string ExpectedSummaryText => Item.ExpectedSummaryText;

    /// <summary>它该建在哪个文件夹 —— 弹窗里要用它单独说一句。</summary>
    public string FolderPath => Item.FolderPath;

    public string DetailText => Item.DetailText;

    public string StatusText => Item.StatusText;

    public bool IsCompliant => Item.IsCompliant;

    /// <summary>点整张卡复制的东西 —— 是**叶子资产名**，不是路径（见模型里的说明）。</summary>
    public string CorrectName => Item.CorrectName;

    public UnrealSyncCheckReplaceTarget ReplaceTarget => Item.ReplaceTarget;

    /// <summary>
    /// 只有引擎和工程这两项需要「更换」入口 —— 它们填错了不是"去 Unreal 里建一个"能解决的，
    /// 得重新选文件。其余各项点一下把名字复制走就够了。
    /// </summary>
    public Visibility ReplaceVisibility =>
        ReplaceTarget == UnrealSyncCheckReplaceTarget.None ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// 虚幻同步台的界面状态。
///
/// 这里只放**显示状态**：选中的东西、检测结果、变更清单、状态条与进度。
/// 真正干活的留在 <c>Services/UnrealSync</c> 与窗口分部类里 ——
/// 这个类不该知道怎么起进程、怎么调 git。
/// </summary>
internal sealed class UnrealSyncViewModel : ObservableObject
{
    private bool _runFullSyncRequested;
    private bool _isLoadingProjects;
    private bool _hidePassedChecks;
    private bool _isChecking;
    private IReadOnlyList<UnrealSyncCheckRow> _visibleCheckRows = [];
    private string _checkSummaryText = "尚未检测。点「开始检测」会检查引擎、工程、落点目录与目标资产。";
    private bool _canSync;
    private string _checkHeaderText = "同步前检测";

    public bool RunFullSyncRequested
    {
        get => _runFullSyncRequested;
        set => SetProperty(ref _runFullSyncRequested, value);
    }

    public bool IsLoadingProjects
    {
        get => _isLoadingProjects;
        set => SetProperty(ref _isLoadingProjects, value);
    }

    // ── 检测层 ────────────────────────────────────────────────────────

    /// <summary>全部检测项（合规的也在里面 —— 「只看没过的」靠过滤显示，不是靠不收集）。</summary>
    public ObservableCollection<UnrealSyncCheckRow> CheckRows { get; } = [];

    /// <summary>按「只看没过的」过滤之后实际显示的条目。</summary>
    public IReadOnlyList<UnrealSyncCheckRow> VisibleCheckRows
    {
        get => _visibleCheckRows;
        private set
        {
            if (SetProperty(ref _visibleCheckRows, value))
            {
                OnPropertyChanged(nameof(CheckListVisibility));
                OnPropertyChanged(nameof(CheckEmptyVisibility));
            }
        }
    }

    /// <summary>检测是否正在跑（跑的时候按钮要置灰）。</summary>
    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(CheckButtonEnabled));
            }
        }
    }

    public bool CheckButtonEnabled => !IsChecking;

    /// <summary>「只看没过的」。默认关 —— 全看一遍才知道工程是不是齐的。</summary>
    public bool HidePassedChecks
    {
        get => _hidePassedChecks;
        set
        {
            if (SetProperty(ref _hidePassedChecks, value))
            {
                RefreshVisibleCheckRows();
            }
        }
    }

    /// <summary>检测区标题，例如「同步前检测 · 合规 12 / 14」。</summary>
    public string CheckHeaderText
    {
        get => _checkHeaderText;
        private set => SetProperty(ref _checkHeaderText, value);
    }

    /// <summary>一句话汇总。</summary>
    public string CheckSummaryText
    {
        get => _checkSummaryText;
        private set => SetProperty(ref _checkSummaryText, value);
    }

    /// <summary>
    /// 能不能同步。只有 **Error 级**的不合规项会把它压成 false；
    /// Warning（例如"这个工程不在 git 仓库里"）不拦路。
    /// </summary>
    public bool CanSync
    {
        get => _canSync;
        private set => SetProperty(ref _canSync, value);
    }

    public Visibility CheckListVisibility =>
        VisibleCheckRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CheckEmptyVisibility =>
        VisibleCheckRows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// 把一次检测的结果铺到界面上。
    /// </summary>
    public void ApplyCheckResult(UnrealSyncPreflightResult result)
    {
        CheckRows.Clear();
        foreach (var item in result.Items)
        {
            CheckRows.Add(new UnrealSyncCheckRow(item, ResolveStatusBrush(item)));
        }

        CheckSummaryText = result.SummaryText;
        CanSync = result.CanSync;
        RefreshVisibleCheckRows();
    }

    /// <summary>还没检测（或检测被清掉）时把界面退回"未检测"态。</summary>
    public void ClearCheckResult(string summary)
    {
        CheckRows.Clear();
        CheckSummaryText = summary;
        CanSync = false;
        RefreshVisibleCheckRows();
    }

    private void RefreshVisibleCheckRows()
    {
        VisibleCheckRows = CheckRows
            .Where(row => !HidePassedChecks || !row.IsCompliant)
            .ToArray();

        var passed = CheckRows.Count(row => row.IsCompliant);
        CheckHeaderText = CheckRows.Count == 0
            ? "同步前检测"
            : $"同步前检测 · 合规 {passed} / {CheckRows.Count}";
    }

    /// <summary>
    /// 状态那一列的颜色。用 WinUI 的语义色，深浅两套主题都自动有合适取值 ——
    /// 和业务日志那几个画刷同理（见 App.xaml 的 ThemeDictionaries）。
    ///
    /// 注：零境那边这一行是**固定绿色**（`TipsTextStyle`），不分合规与否。
    /// 这里分了色，因为同步台这张表的失败项是要"照着去建"的，
    /// 一眼能挑出来比读文字快。这是刻意的偏离，不是漏看。
    /// </summary>
    private static Brush? ResolveStatusBrush(UnrealSyncCheckItem item)
    {
        var key = item switch
        {
            { IsCompliant: true } => "SystemFillColorSuccessBrush",
            { Severity: UnrealSyncCheckSeverity.Warning } => "SystemFillColorCautionBrush",
            _ => "SystemFillColorCriticalBrush",
        };
        return Application.Current?.Resources[key] as Brush;
    }
}
