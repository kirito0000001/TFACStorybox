using System;
using System.Collections.Generic;
using System.Linq;
using GalExcleTools.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

/// <summary>
/// 「导入章节」那个差异对比弹窗的内容。
///
/// 【为什么是弹窗而不是整页覆盖层】差异条数通常几十条、最多几百条，
/// 弹窗里一个滚动列表就够；而且确认 / 取消的快捷键（Enter / Esc）是仓库统一的规定，
/// ContentDialog 天生就有。选项管理器那种整页覆盖层是给"要长时间编辑"的场景用的。
///
/// 【默认一律"保持本机"】每一条的勾选框默认不勾 —— 合并是"我这边收下对方的改动"，
/// 没点名的地方不该被动。三个批量按钮负责常见的一次性选择。
///
/// 【不做数据绑定】每行的勾选框直接用事件回写 <see cref="ChapterMergeItem.Choice"/>。
/// 差异列表是"一次性渲染完、点完就丢"的东西，为它套一层 ViewModel + 通知机制
/// 换不来什么，反而多一处可能不同步的状态。
/// </summary>
internal sealed class ChapterMergeDialogContent
{
    private readonly ChapterMergePlan _plan;
    private readonly List<Action> _refreshRowStates = [];

    public ChapterMergeDialogContent(ChapterMergePlan plan)
    {
        _plan = plan;
        Content = Build();
    }

    /// <summary>勾选变化时通知外面（用来更新主按钮上那个"将应用 N 条"）。</summary>
    public event Action? SelectionChanged;

    public UIElement Content { get; }

    public int SelectedCount => _plan.SelectedFromPackageCount;

    public string PrimaryButtonText => SelectedCount == 0
        ? "没有勾选任何一条"
        : $"合并（将应用 {SelectedCount} 条）";

    /// <summary>一条都没勾就想合并时给个提示（对应 ContentDialog 的 PrimaryButtonClick 拦截）。</summary>
    public void ShowValidation()
    {
        _validationText.Visibility = Visibility.Visible;
    }

    private TextBlock _validationText = null!;

    public void SelectAllLocal()
    {
        ApplyChoice(_plan.RowItems, item => item.Choice = ChapterMergeChoice.KeepLocal);
        ApplyChoice(_plan.TodoItems, item => item.Choice = ChapterMergeChoice.KeepLocal);
    }

    public void SelectAllFromPackage()
    {
        ApplyChoice(_plan.RowItems, item => item.Choice = ChapterMergeChoice.UsePackage);
        ApplyChoice(_plan.TodoItems, item => item.Choice = ChapterMergeChoice.UsePackage);
    }

    /// <summary>
    /// "只收对方的改动"：把两边都有、但内容不同的那句用对方的，以及对方**新增**的句子插进来；
    /// 本机独有的句子不动（分不清是他删了还是我新增的，不敢替他删）。
    /// 这是对接里最常用的一档。
    /// </summary>
    public void SelectPackageEditsOnly()
    {
        ApplyChoice(_plan.RowItems, item => item.Choice =
            item.Kind == ChapterMergeItemKind.OnlyLocal
                ? ChapterMergeChoice.KeepLocal
                : ChapterMergeChoice.UsePackage);
        ApplyChoice(_plan.TodoItems, item => item.Choice =
            item.Kind == ChapterMergeItemKind.OnlyLocal
                ? ChapterMergeChoice.KeepLocal
                : ChapterMergeChoice.UsePackage);
    }

    private void ApplyChoice<T>(IEnumerable<T> items, Action<T> applyChoice)
    {
        foreach (var item in items)
        {
            applyChoice(item);
        }

        foreach (var refresh in _refreshRowStates)
        {
            refresh();
        }

        SelectionChanged?.Invoke();
    }

    private UIElement Build()
    {
        var root = new StackPanel { Spacing = 10, MinWidth = 720 };

        root.Children.Add(new TextBlock
        {
            Text = $"包：{_plan.Manifest.ChapterName}（{_plan.Manifest.ChapterCode}）"
                   + $" · 导出于 {_plan.Manifest.ExportedAt:yyyy-MM-dd HH:mm}"
                   + $" · 工具箱 {_plan.Manifest.ToolVersion}",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = $"包里 {_plan.Manifest.RowCount} 行 / {_plan.Manifest.SectionCount} 个小节 / {_plan.Manifest.TodoCount} 条备注"
                   + $"　·　本机 {_plan.LocalRowCount} 行 / {_plan.LocalSectionCount} 个小节",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        root.Children.Add(new TextBlock
        {
            Text = $"差异 {_plan.RowItems.Count + _plan.TodoItems.Count} 条："
                   + $"改动 {_plan.ModifiedCount} · 只有对方有 {_plan.OnlyInPackageCount} · 只有本机有 {_plan.OnlyLocalCount}"
                   + $" · 备注 {_plan.TodoChangeCount}",
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var warning in _plan.Warnings)
        {
            root.Children.Add(new TextBlock
            {
                Text = $"提醒:{warning}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            });
        }

        root.Children.Add(new TextBlock
        {
            Text = "小节划分以本机为准；勾选「插入」的句子会跟在它前面那一句所属的小节里。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var bulkButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bulkButtons.Children.Add(CreateBulkButton("只收对方的改动", SelectPackageEditsOnly));
        bulkButtons.Children.Add(CreateBulkButton("全部用对方", SelectAllFromPackage));
        bulkButtons.Children.Add(CreateBulkButton("全部用本机", SelectAllLocal));
        root.Children.Add(bulkButtons);

        _validationText = new TextBlock
        {
            Text = "先在上面勾选至少一条要采用的改动。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_validationText);

        var rows = new StackPanel { Spacing = 6 };
        foreach (var item in _plan.RowItems)
        {
            rows.Children.Add(CreateRowCard(item));
        }

        foreach (var item in _plan.TodoItems)
        {
            rows.Children.Add(CreateTodoCard(item));
        }

        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 430,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            Content = rows
        });
        return root;
    }

    private Button CreateBulkButton(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    private Border CreateRowCard(ChapterMergeItem item)
    {
        var panel = new StackPanel { Spacing = 4 };

        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var checkBox = new CheckBox
        {
            Content = GetChoiceLabel(item.Kind),
            IsChecked = item.Choice == ChapterMergeChoice.UsePackage,
            MinWidth = 0
        };
        checkBox.Checked += (_, _) => SetChoice(item, true);
        checkBox.Unchecked += (_, _) => SetChoice(item, false);
        _refreshRowStates.Add(() => checkBox.IsChecked = item.Choice == ChapterMergeChoice.UsePackage);

        var location = new TextBlock
        {
            Text = BuildLocationText(item),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(location, 1);
        header.Children.Add(checkBox);
        header.Children.Add(location);
        panel.Children.Add(header);

        if (item.Kind == ChapterMergeItemKind.Modified && item.ChangedFields.Count > 0)
        {
            panel.Children.Add(CreateMuted($"不一样的字段：{string.Join("、", item.ChangedFields)}"));
        }

        panel.Children.Add(CreateSided("本机", item.LocalSummary, item.Kind == ChapterMergeItemKind.OnlyInPackage));
        panel.Children.Add(CreateSided("对方", item.PackageSummary, item.Kind == ChapterMergeItemKind.OnlyLocal));
        return CreateCard(panel);
    }

    private Border CreateTodoCard(ChapterTodoMergeItem item)
    {
        var panel = new StackPanel { Spacing = 4 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var checkBox = new CheckBox
        {
            Content = GetChoiceLabel(item.Kind),
            IsChecked = item.Choice == ChapterMergeChoice.UsePackage,
            MinWidth = 0
        };
        checkBox.Checked += (_, _) => SetTodoChoice(item, true);
        checkBox.Unchecked += (_, _) => SetTodoChoice(item, false);
        _refreshRowStates.Add(() => checkBox.IsChecked = item.Choice == ChapterMergeChoice.UsePackage);

        var location = new TextBlock
        {
            Text = item.Kind == ChapterMergeItemKind.OnlyInPackage
                ? $"备注 · 包里第 {item.PackageRowNumber} 句"
                : $"备注 · 本机第 {item.LocalRowNumber} 句",
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(location, 1);
        header.Children.Add(checkBox);
        header.Children.Add(location);
        panel.Children.Add(header);
        panel.Children.Add(CreateSided(
            "本机",
            string.IsNullOrWhiteSpace(item.LocalText) ? "（本机没有这条备注）" : FormatTodoText(item.LocalText, item.LocalDone),
            item.Kind == ChapterMergeItemKind.OnlyInPackage));
        panel.Children.Add(CreateSided(
            "对方",
            string.IsNullOrWhiteSpace(item.PackageText) ? "（对方没有这条备注）" : FormatTodoText(item.PackageText, item.PackageDone),
            item.Kind == ChapterMergeItemKind.OnlyLocal));
        return CreateCard(panel);
    }

    private void SetChoice(ChapterMergeItem item, bool usePackage)
    {
        item.Choice = usePackage ? ChapterMergeChoice.UsePackage : ChapterMergeChoice.KeepLocal;
        _validationText.Visibility = Visibility.Collapsed;
        SelectionChanged?.Invoke();
    }

    private void SetTodoChoice(ChapterTodoMergeItem item, bool usePackage)
    {
        item.Choice = usePackage ? ChapterMergeChoice.UsePackage : ChapterMergeChoice.KeepLocal;
        _validationText.Visibility = Visibility.Collapsed;
        SelectionChanged?.Invoke();
    }

    private static string GetChoiceLabel(ChapterMergeItemKind kind)
    {
        return kind switch
        {
            ChapterMergeItemKind.Modified => "用对方的",
            ChapterMergeItemKind.OnlyInPackage => "插入这一句",
            _ => "按对方删除这一句"
        };
    }

    private static string BuildLocationText(ChapterMergeItem item)
    {
        return item.Kind switch
        {
            ChapterMergeItemKind.Modified =>
                $"本机第 {item.LocalRowNumber} 句（第 {StorySectionDisplay.ToDisplayIndex(item.LocalSection)} 小节）"
                + $"　↔　包里第 {item.PackageRowNumber} 句",
            ChapterMergeItemKind.OnlyInPackage =>
                $"包里第 {item.PackageRowNumber} 句（第 {StorySectionDisplay.ToDisplayIndex(item.PackageSection)} 小节）",
            _ => $"本机第 {item.LocalRowNumber} 句（第 {StorySectionDisplay.ToDisplayIndex(item.LocalSection)} 小节）"
        };
    }

    private static string FormatTodoText(string text, bool done)
    {
        return done ? $"{text}（对方已勾选处理）" : text;
    }

    private static TextBlock CreateSided(string side, string text, bool missing)
    {
        return new TextBlock
        {
            Text = $"{side}：{(string.IsNullOrWhiteSpace(text) ? "（没有这一句）" : text)}",
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 4,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = missing
                ? (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };
    }

    private static TextBlock CreateMuted(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
    }

    private static Border CreateCard(UIElement content)
    {
        return new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            Child = content
        };
    }
}
