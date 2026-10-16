using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using GalExcleTools.Views;

namespace GalExcleTools.Controls;

/// <summary>
/// 条带里的一项。<see cref="ImagePath"/> 为空时这一格退化成文字块。
///
/// ⚠️ 必须 public：<see cref="ImageStripPicker"/> 是 public（XAML 控件那半边要求），
/// 而 public 方法不能拿 internal 类型当参数（CS0051，2026-10-16 编译时踩到）。
/// 这边的口径和 DialogContracts 里的 SelectionDialogItem 一致 —— 对话框契约类是 public。
/// </summary>
public sealed record ImageStripPickerItem(string Label, string? ImagePath, object? Value);

/// <summary>
/// 图片条带选择器：上面大图看细节，下面横向条带一眼看全部。
///
/// 【为什么要抽成控件】原来这类选择框走的是通用对话框里的一个 ListView（文字列表 +
/// 每个候选项挂一个 ToolTip 当预览）。ToolTip 是系统弹层，会画在对话框**外面**，
/// 而且一次只能看一个 —— 用户 2026-10-16 报的就是"悬停那张图飘在卡片外面、看不清能选什么"。
/// 现在形态收在这一个文件里：以后要改条带（换尺寸、按图层分组、双击直接确定…）只动这里，
/// 调用方一个都不用碰。
///
/// 【谁用它】所有**带预览图**的候选（背景图 / 道具图片 / 立绘各图层 / 小预览…）。
/// 判断在 WinUiDialogService 里：任意一项带了图就走条带；纯文字的选择（函数、说话人…）
/// 仍旧走 ListView，不误伤。
/// </summary>
public sealed partial class ImageStripPicker : UserControl
{
    private const int ThumbnailWidth = 104;
    private const int ThumbnailHeight = 150;

    private readonly List<string?> _imagePaths = [];

    public ImageStripPicker()
    {
        InitializeComponent();
        StripGridView.SelectionChanged += StripGridView_SelectionChanged;
    }

    /// <summary>选中项变了（拖条带、点格子、按左右键都会走到）。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>当前选中项带出去的那个值（调用方自己知道 T 是什么）。</summary>
    public object? SelectedValue =>
        StripGridView.SelectedItem is GridViewItem { Tag: { } tag } ? tag : null;

    /// <summary>
    /// 填条目。<paramref name="selectedIndex"/> 是打开时预选哪一项（越界就退回第一项）。
    /// </summary>
    public void SetItems(IReadOnlyList<ImageStripPickerItem> items, int selectedIndex)
    {
        _imagePaths.Clear();
        StripGridView.Items.Clear();
        StripGridView.SelectedIndex = -1;

        for (var index = 0; index < items.Count; index++)
        {
            _imagePaths.Add(items[index].ImagePath);
            StripGridView.Items.Add(BuildStripItem(items[index]));
        }

        if (StripGridView.Items.Count == 0)
        {
            PreviewImage.Source = null;
            PreviewHintText.Visibility = Visibility.Visible;
            PreviewHintText.Text = "没有可选项。";
            SelectedCaptionText.Text = string.Empty;
            return;
        }

        StripGridView.SelectedIndex = selectedIndex >= 0 && selectedIndex < StripGridView.Items.Count
            ? selectedIndex
            : 0;
    }

    /// <summary>
    /// 一格长什么样：缩略图 + 名称 + （标题里带上）序号。
    ///
    /// 名称**必须留着**：显示什么就得能跟剧情 CSV 里存的那个号对上
    ///（背景图/立绘的号就是文件名里的序号，也正是存进 CSV 的东西）。
    /// 只给图不给名，对号时会抓瞎。
    /// </summary>
    private static GridViewItem BuildStripItem(ImageStripPickerItem item)
    {
        var thumbnailHost = new Border
        {
            Width = ThumbnailWidth,
            Height = ThumbnailHeight,
            CornerRadius = new CornerRadius(4),
            Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush,
            Child = string.IsNullOrWhiteSpace(item.ImagePath)
                ? new TextBlock
                {
                    Text = item.Label,
                    Margin = new Thickness(6),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = Application.Current.Resources["SubtleTextStyle"] as Style
                }
                : ThumbnailFactory.CreateThumbnail(item.ImagePath!, ThumbnailWidth, ThumbnailHeight, showAddIcon: false)
        };

        var caption = new TextBlock
        {
            Text = item.Label,
            Width = ThumbnailWidth,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(thumbnailHost);
        content.Children.Add(caption);

        return new GridViewItem
        {
            Content = content,
            Tag = item.Value,
            Margin = new Thickness(2, 0, 6, 0)
        };
    }

    private void StripGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshPreview();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把大图与标题换成当前选中项。</summary>
    private void RefreshPreview()
    {
        var index = StripGridView.SelectedIndex;
        if (index < 0 || index >= _imagePaths.Count)
        {
            PreviewImage.Source = null;
            PreviewHintText.Visibility = Visibility.Visible;
            PreviewHintText.Text = "选一项看看。";
            SelectedCaptionText.Text = string.Empty;
            return;
        }

        var label = StripGridView.SelectedItem is GridViewItem { Content: StackPanel content } &&
                    content.Children.Count > 1 &&
                    content.Children[1] is TextBlock caption
            ? caption.Text
            : string.Empty;
        SelectedCaptionText.Text = string.IsNullOrEmpty(label) ? $"第 {index} 项" : $"{index}：{label}";

        var imagePath = _imagePaths[index];
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            PreviewImage.Source = null;
            PreviewHintText.Visibility = Visibility.Visible;
            PreviewHintText.Text = "这一项没有预览图。";
            return;
        }

        PreviewHintText.Visibility = Visibility.Collapsed;
        // 先上占位图，再异步换成真图：素材在别的盘上时这一步会有可感的等待。
        PreviewImage.Source = ThumbnailFactory.CreateDefaultBitmap();
        _ = ThumbnailFactory.LoadThumbnailFromFileAsync(PreviewImage, imagePath);
    }
}
