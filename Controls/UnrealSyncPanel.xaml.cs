using System;
using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Controls;

/// <summary>
/// 虚幻同步台的三层界面（选项目 / 检测合规 / 同步），从 MainWindow.xaml 整块搬出来。
///
/// 【它和窗口的分工】
/// 面板对窗口**零依赖**：
/// - 窗口要更新界面，走下面那些属性 / 方法（EnginePath、SetPlanItems 之类）；
/// - 用户点了什么，面板发事件（SyncRequested 之类），**怎么做事是窗口的**。
/// 所以这个文件里不会出现 _unrealSyncService、_dialogService 这些名字 ——
/// 那些是窗口的编排，不是界面的。
///
/// 【为什么用事件，而不是宿主接口】
/// 参考项目里同类面板走 IVoiceMaterialManagerHost 那种宿主接口。
/// 这里没那么做：面板需要的不是"一组服务"，而是"用户点了某个按钮"这六件事，
/// 六行事件比一个几十成员的接口直白得多；而且接口会越长越大，
/// 面板慢慢就长成第二个窗口了。
/// </summary>
public sealed partial class UnrealSyncPanel : UserControl
{
    /// <summary>变更对比表的列宽。改这里要同时改 XAML 里那行表头的列宽。</summary>
    private const double DiffCategoryWidth = 150;
    private const double DiffChangeWidth = 220;

    public UnrealSyncPanel()
    {
        InitializeComponent();

        // 卡片列表不做出现动画：一次刷新几十张卡，动画只会让人觉得卡。
        // 这一行原来在 MainWindow.xaml.cs 里对着一串 GridView 逐个设，
        // 既然卡片归面板管，就归面板自己设。
        UnrealSyncProjectCardsGridView.ItemContainerTransitions = null;

        // 里面那个检测层自己发事件；这里原样转发出去，
        // 让窗口只订阅一个对象就够了（不必知道面板内部还有个子控件）。
        SyncCheckPanel.CheckRequested += (_, _) => CheckRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── 用户动作：面板只宣告"点了什么" ──────────────────────────────

    /// <summary>点了「选择引擎」。</summary>
    public event EventHandler? EngineChooseRequested;

    /// <summary>点了「选择 .uproject」。</summary>
    public event EventHandler? ProjectChooseRequested;

    /// <summary>换了工作台项目。</summary>
    public event EventHandler? ProjectSelectionChanged;

    /// <summary>点了「开始检测」（检测层转发上来的）。</summary>
    public event EventHandler? CheckRequested;

    /// <summary>点了「全部重新同步」。</summary>
    public event EventHandler? FullSyncRequested;

    /// <summary>点了「确认同步到虚幻」。</summary>
    public event EventHandler? SyncRequested;

    // ── 窗口 → 面板：路径 ──────────────────────────────────────────

    public string EnginePath
    {
        get => UnrealEnginePathTextBox.Text;
        set => UnrealEnginePathTextBox.Text = value ?? string.Empty;
    }

    public string ProjectPath
    {
        get => UnrealProjectPathTextBox.Text;
        set => UnrealProjectPathTextBox.Text = value ?? string.Empty;
    }

    /// <summary>目标落点。只读框，显示的是推理出来的路径 —— 不是让用户填的。</summary>
    public string TargetFolderText
    {
        get => UnrealContentFolderTextBox.Text;
        set => UnrealContentFolderTextBox.Text = value ?? string.Empty;
    }

    // ── 窗口 → 面板：按钮与状态 ────────────────────────────────────

    public bool SyncButtonEnabled
    {
        get => RunUnrealSyncButton.IsEnabled;
        set => RunUnrealSyncButton.IsEnabled = value;
    }

    public bool CheckButtonEnabled
    {
        get => CheckUnrealSyncButton.IsEnabled;
        set => CheckUnrealSyncButton.IsEnabled = value;
    }

    public string SummaryText
    {
        get => UnrealSyncSummaryText.Text;
        set => UnrealSyncSummaryText.Text = value ?? string.Empty;
    }

    /// <summary>状态条。原来窗口里要连设四个属性，现在一句话。</summary>
    public void SetStatus(InfoBarSeverity severity, string title, string message)
    {
        UnrealSyncStatusInfoBar.IsOpen = true;
        UnrealSyncStatusInfoBar.Severity = severity;
        UnrealSyncStatusInfoBar.Title = title;
        UnrealSyncStatusInfoBar.Message = message;
    }

    // ── 窗口 → 面板：变更对比表 ────────────────────────────────────
    //
    // 一行 = 一个类别变了多少。列宽**必须和 XAML 里那行表头一致**，
    // 否则表头和内容会错位 —— 这也是原来那版"一坨文字"的问题之一。

    public void ClearDiffRows() => UnrealSyncDiffItemsControl.Items.Clear();

    public void AddDiffRow(string category, string change, string note)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DiffCategoryWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DiffChangeWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(BuildDiffCell(category, 0, primary: true));
        row.Children.Add(BuildDiffCell(change, 1, primary: false));
        row.Children.Add(BuildDiffCell(note, 2, primary: false));

        UnrealSyncDiffItemsControl.Items.Add(new Border
        {
            BorderBrush = Application.Current.Resources["DividerStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        });
    }

    private static TextBlock BuildDiffCell(string? text, int column, bool primary)
    {
        var cell = new TextBlock
        {
            Text = text ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = primary ? 14 : 13,
            Foreground = Application.Current.Resources[
                primary ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"] as Brush
        };
        Grid.SetColumn(cell, column);
        return cell;
    }

    // ── 窗口 → 面板：工作台项目 ────────────────────────────────────
    //
    // 有两个东西在存项目：一个**隐藏的 ComboBox**（存 Tag，代码按它取值）和
    // 一排给人看的卡片。所以这里的接口是"两个一起管"，而不是让人各管各的。

    /// <summary>清空项目列表（隐藏的下拉与卡片列表一起）。填之前先调它。</summary>
    public void BeginProjectOptions()
    {
        UnrealSyncProjectComboBox.Items.Clear();
        UnrealSyncProjectCardsGridView.Items.Clear();
    }

    /// <summary>
    /// 加一个工作台项目。<paramref name="card"/> 由窗口造（卡片内容归窗口的业务），
    /// 面板只负责往里放。
    /// </summary>
    public void AddProjectOption(string? folderName, string displayName, UIElement? card)
    {
        UnrealSyncProjectComboBox.Items.Add(new ComboBoxItem
        {
            Content = displayName,
            Tag = folderName
        });

        if (card is not null)
        {
            UnrealSyncProjectCardsGridView.Items.Add(card);
        }
    }

    /// <summary>当前选中的项目目录名；没选就是 null。</summary>
    public string? SelectedProjectTag =>
        (UnrealSyncProjectComboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>按目录名选中项目。找不到就保持原样，返回 false。</summary>
    public bool SelectProjectByTag(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        foreach (var item in UnrealSyncProjectComboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(comboItem.Tag as string, folderName, StringComparison.OrdinalIgnoreCase))
            {
                UnrealSyncProjectComboBox.SelectedItem = comboItem;
                return true;
            }
        }

        return false;
    }

    // ── 窗口 → 面板：滚动位置 ──────────────────────────────────────
    //
    // 切页面时窗口会记住 / 恢复滚动位置。ScrollViewer 在面板里面，
    // 所以把这个位置做成一个属性，让窗口不用去翻面板内部。

    public double ScrollOffset
    {
        get => PageScrollViewer.VerticalOffset;
        set => PageScrollViewer.ChangeView(null, value, null, disableAnimation: true);
    }

    // ── 六个按钮的处理函数：只转发，不做事 ────────────────────────

    private void ChooseUnrealEngineButton_Click(object sender, RoutedEventArgs e) =>
        EngineChooseRequested?.Invoke(this, EventArgs.Empty);

    private void ChooseUnrealProjectButton_Click(object sender, RoutedEventArgs e) =>
        ProjectChooseRequested?.Invoke(this, EventArgs.Empty);

    private void UnrealSyncProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ProjectSelectionChanged?.Invoke(this, EventArgs.Empty);

    private void CheckUnrealSyncButton_Click(object sender, RoutedEventArgs e) =>
        CheckRequested?.Invoke(this, EventArgs.Empty);

    private void RunFullUnrealSyncButton_Click(object sender, RoutedEventArgs e) =>
        FullSyncRequested?.Invoke(this, EventArgs.Empty);

    private void RunUnrealSyncButton_Click(object sender, RoutedEventArgs e) =>
        SyncRequested?.Invoke(this, EventArgs.Empty);
}
