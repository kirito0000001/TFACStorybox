using System;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

internal sealed class AiStoryFillTaskDialogContent
{
    private readonly ComboBox _protocolBox;
    private readonly NumberBox _sourceStartBox;

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

        _sourceStartBox = new NumberBox
        {
            Header = "原稿起点（可选）",
            PlaceholderText = "通常留空",
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var panel = AiStoryFillDialogContentFactory.CreatePanel(520);
        panel.Children.Add(AiStoryFillDialogContentFactory.CreateHeading($"{chapter.Name}  /  {chapter.Code}", 18));
        panel.Children.Add(_protocolBox);
        panel.Children.Add(_sourceStartBox);
        panel.Children.Add(AiStoryFillDialogContentFactory.CreateMutedText("起点只作为外接程序的原稿参考；留空时按 0 写入协议。"));
        panel.Children.Add(AiStoryFillDialogContentFactory.CreateActionSummary(
            "导入填写结果",
            "读取 result.jsonl，预览后追加 TalkChar 与 Tesxt。"));
        Content = panel;
    }

    public UIElement Content { get; }

    public int ProtocolVersion => (_protocolBox.SelectedItem as ComboBoxItem)?.Tag is int version ? version : 1;

    public int SourceStartParagraph => double.IsNaN(_sourceStartBox.Value)
        ? 0
        : Math.Max(0, (int)Math.Truncate(_sourceStartBox.Value));

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
        root.Children.Add(CreateHeading(
            $"待合并 {preview.PendingIncludeCount}  |  跳过 {preview.SkipCount}  |  待人工确认 {preview.ReviewCount}  |  重复 {preview.DuplicateCount}",
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
            rows.Children.Add(CreatePreviewRow(item));
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
