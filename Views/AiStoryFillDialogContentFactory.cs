using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

internal sealed class AiStoryFillTaskDialogContent
{
    private readonly ComboBox _protocolBox;
    private readonly ComboBox _modeBox;
    private readonly NumberBox _sourceStartBox;
    private readonly CheckBox _talkCharCheckBox;
    private readonly CheckBox _textCheckBox;
    private readonly StackPanel _patchFieldsPanel;
    private readonly TextBlock _modeHint;
    private readonly TextBlock _validationMessage;

    public AiStoryFillTaskDialogContent(ChapterInfo chapter)
    {
        _protocolBox = new ComboBox
        {
            Header = "协议版本",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0
        };
        _protocolBox.Items.Add(CreateProtocolItem("V1", "剧情文本与说话人", 1, isEnabled: true));
        _protocolBox.Items.Add(CreateProtocolItem("V2", "角色与立绘层（尚未开放）", 2, isEnabled: false));
        _protocolBox.Items.Add(CreateProtocolItem("V3", "函数与剧情控制（尚未开放）", 3, isEnabled: false));

        _modeBox = new ComboBox
        {
            Header = "任务类型",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0
        };
        _modeBox.Items.Add(new ComboBoxItem { Content = "续写章节", Tag = "append" });
        _modeBox.Items.Add(new ComboBoxItem { Content = "补齐已有内容", Tag = "patch-empty" });

        _sourceStartBox = new NumberBox
        {
            Header = "原稿起点（可选）",
            PlaceholderText = "通常留空",
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _talkCharCheckBox = new CheckBox { Content = "当前说话人", IsChecked = true };
        _textCheckBox = new CheckBox { Content = "剧情文本" };
        _patchFieldsPanel = new StackPanel
        {
            Spacing = 4,
            Visibility = Visibility.Collapsed
        };
        _patchFieldsPanel.Children.Add(AiStoryFillDialogContentFactory.CreateHeading("需要补齐", 14));
        _patchFieldsPanel.Children.Add(_talkCharCheckBox);
        _patchFieldsPanel.Children.Add(_textCheckBox);
        _modeHint = AiStoryFillDialogContentFactory.CreateMutedText("起点只作为外接程序的原稿参考；留空时按 0 写入协议。");
        _validationMessage = new TextBlock
        {
            Text = "至少选择一个需要补齐的字段。",
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        _modeBox.SelectionChanged += (_, _) => UpdateModeState();
        _talkCharCheckBox.Checked += (_, _) => HideValidation();
        _textCheckBox.Checked += (_, _) => HideValidation();

        var panel = AiStoryFillDialogContentFactory.CreatePanel(520);
        panel.Children.Add(AiStoryFillDialogContentFactory.CreateHeading($"{chapter.Name}  /  {chapter.Code}", 18));
        panel.Children.Add(_protocolBox);
        panel.Children.Add(_modeBox);
        panel.Children.Add(_sourceStartBox);
        panel.Children.Add(_modeHint);
        panel.Children.Add(_patchFieldsPanel);
        panel.Children.Add(_validationMessage);
        panel.Children.Add(AiStoryFillDialogContentFactory.CreateActionSummary(
            "导入填写结果",
            "读取 result.jsonl，预览后追加 TalkChar 与 Tesxt。"));
        Content = panel;
    }

    public UIElement Content { get; }

    public int ProtocolVersion => (_protocolBox.SelectedItem as ComboBoxItem)?.Tag is int version ? version : 1;

    public string Mode => (_modeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "append";

    public int SourceStartParagraph => double.IsNaN(_sourceStartBox.Value)
        ? 0
        : Math.Max(0, (int)Math.Truncate(_sourceStartBox.Value));

    public bool CanExport => Mode != "patch-empty" || _talkCharCheckBox.IsChecked == true || _textCheckBox.IsChecked == true;

    public AiStoryFillTaskOptions CreateOptions()
    {
        var options = new AiStoryFillTaskOptions
        {
            ProtocolVersion = ProtocolVersion,
            Mode = Mode,
            SourceStartParagraph = SourceStartParagraph
        };
        if (Mode == "patch-empty")
        {
            if (_talkCharCheckBox.IsChecked == true)
            {
                options.PatchFields.Add("TalkChar");
            }
            if (_textCheckBox.IsChecked == true)
            {
                options.PatchFields.Add("Tesxt");
            }
        }
        return options;
    }

    public void ShowValidation()
    {
        _validationMessage.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        _validationMessage.Visibility = Visibility.Collapsed;
    }

    private void UpdateModeState()
    {
        var isPatch = Mode == "patch-empty";
        _sourceStartBox.IsEnabled = !isPatch;
        _patchFieldsPanel.Visibility = isPatch ? Visibility.Visible : Visibility.Collapsed;
        _modeHint.Text = isPatch
            ? "工具箱会按 StableId 导出已有空字段；无关素材变化不会让整包失效。"
            : "起点只作为外接程序的原稿参考；留空时按 0 写入协议。";
        HideValidation();
    }

    private static ComboBoxItem CreateProtocolItem(
        string version,
        string description,
        int value,
        bool isEnabled)
    {
        return new ComboBoxItem
        {
            Content = $"{version}  {description}",
            Tag = value,
            IsEnabled = isEnabled
        };
    }
}

internal static class AiStoryFillDialogContentFactory
{
    public static AiStoryFillTaskDialogContent CreateTaskContent(ChapterInfo chapter)
    {
        return new AiStoryFillTaskDialogContent(chapter);
    }

    public static UIElement CreatePreviewContent(AiStoryFillImportPreview preview)
    {
        var root = CreatePanel(760);
        var isPatch = string.Equals(preview.Manifest.Mode, "patch-empty", StringComparison.OrdinalIgnoreCase);
        root.Children.Add(CreateHeading(
            $"{(isPatch ? "待修补" : "待合并")} {preview.PendingIncludeCount}  |  跳过 {preview.SkipCount}  |  待人工确认 {preview.ReviewCount}  |  重复 {preview.DuplicateCount}",
            17));

        if (preview.Issues.Count > 0)
        {
            var issues = string.Join("\n", preview.Issues.Select(issue =>
                $"{(issue.Severity == AiStoryFillIssueSeverity.Error ? "错误" : "提醒")}{(issue.FileLine is null ? string.Empty : $" L{issue.FileLine}")}：{issue.Message}"));
            root.Children.Add(CreateMessageBlock(issues, preview.CanImport ? "SystemFillColorCautionBrush" : "SystemFillColorCriticalBrush"));
        }

        var rows = new StackPanel { Spacing = 6 };
        foreach (var item in preview.Items.Take(300))
        {
            rows.Children.Add(isPatch ? CreatePatchPreviewRow(item) : CreatePreviewRow(item));
        }
        if (preview.Items.Count > 300)
        {
            rows.Children.Add(CreateMutedText($"其余 {preview.Items.Count - 300} 条未在预览中展开。"));
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

    private static UIElement CreatePatchPreviewRow(AiStoryFillPreviewItem item)
    {
        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(10, 8, 10, 8) };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rowNumber = new TextBlock
        {
            Text = item.RowIndex is null ? "未知行" : $"第 {item.RowIndex.Value + 1} 句",
            FontWeight = FontWeights.SemiBold
        };
        var status = new TextBlock { Text = GetStatusText(item.Status) };
        var source = new TextBlock
        {
            Text = item.SourceParagraph is null ? string.Empty : $"原稿 P{item.SourceParagraph}",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(status, 1);
        Grid.SetColumn(source, 2);
        header.Children.Add(rowNumber);
        header.Children.Add(status);
        header.Children.Add(source);
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.CurrentTesxt) ? "（当前没有剧情文本）" : item.CurrentTesxt,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3
        });

        var changes = new List<string>();
        if (item.TalkChar is not null)
        {
            changes.Add($"当前说话人：{DisplayPatchValue(item.CurrentTalkChar)}  ->  {DisplayPatchValue(item.TalkChar)}");
        }
        if (item.Tesxt is not null)
        {
            changes.Add($"剧情文本：{DisplayPatchValue(item.CurrentTesxt)}  ->  {DisplayPatchValue(item.Tesxt)}");
        }
        if (changes.Count > 0)
        {
            panel.Children.Add(CreateMutedText(string.Join("\n", changes)));
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = panel
        };
    }

    private static string DisplayPatchValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "（空）" : value;
    }

    internal static UIElement CreateActionSummary(string title, string description)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(CreateHeading(title, 15));
        panel.Children.Add(CreateMutedText(description));
        return panel;
    }

    private static UIElement CreatePreviewRow(AiStoryFillPreviewItem item)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var status = new TextBlock
        {
            Text = GetStatusText(item.Status),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top
        };
        var speaker = new TextBlock
        {
            Text = string.IsNullOrEmpty(item.TalkChar) ? "旁白" : item.TalkChar,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Top
        };
        var text = new TextBlock
        {
            Text = item.Tesxt ?? "-",
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3
        };
        Grid.SetColumn(status, 0);
        Grid.SetColumn(speaker, 1);
        Grid.SetColumn(text, 2);
        grid.Children.Add(status);
        grid.Children.Add(speaker);
        grid.Children.Add(text);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private static string GetStatusText(AiStoryFillItemStatus status)
    {
        return status switch
        {
            AiStoryFillItemStatus.Include => "待合并",
            AiStoryFillItemStatus.Skip => "跳过",
            AiStoryFillItemStatus.Review => "待人工确认",
            AiStoryFillItemStatus.Duplicate => "重复",
            _ => "错误"
        };
    }

    private static UIElement CreateMessageBlock(string text, string brushKey)
    {
        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources[brushKey],
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
        };
    }

    internal static StackPanel CreatePanel(double width)
    {
        return new StackPanel { Width = width, Spacing = 12 };
    }

    internal static TextBlock CreateHeading(string text, double fontSize)
    {
        return new TextBlock { Text = text, FontSize = fontSize, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    }

    internal static TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
    }
}
