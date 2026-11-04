using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GalExcleTools.Controls;

/// <summary>
/// 本项目统一弹窗的**单文件版本**。
///
/// 【为什么单独做一个】原来的弹窗散在 `IDialogService` / `FunctionDialogService` 里，
/// 每个页面各自拼 ContentDialog，样式和按钮顺序都不完全一致。这里先把"长得一样"
/// 这件事收敛到一个文件：确认、提示、带内容三种，全部走同一套参数与排版。
/// 之后逐步把老调用点换过来（先给语音设置用），换完再删旧的。
///
/// 【为什么不用 XAML】和 TipToast 同一个理由：单文件、零依赖，拷到别的项目改个
/// namespace 就能用；也方便以后抽成共享源文件。
/// </summary>
internal static class ToolboxDialog
{
    /// <summary>确认框。返回 true 表示点了主按钮。</summary>
    public static async Task<bool> ConfirmAsync(
        XamlRoot? xamlRoot,
        string title,
        string message,
        string primaryText = "确定",
        string closeText = "取消")
    {
        var result = await ShowCoreAsync(
            xamlRoot,
            title,
            BuildMessageContent(message),
            primaryText,
            closeText,
            isPrimaryDestructive: false);
        return result == ContentDialogResult.Primary;
    }

    /// <summary>只有一个关闭按钮的提示框。</summary>
    public static async Task ShowAsync(
        XamlRoot? xamlRoot,
        string title,
        string message,
        string closeText = "知道了")
    {
        await ShowCoreAsync(xamlRoot, title, BuildMessageContent(message), null, closeText, isPrimaryDestructive: false);
    }

    /// <summary>带自定义内容的提示框（只有一个关闭按钮）。</summary>
    public static async Task ShowAsync(
        XamlRoot? xamlRoot,
        string title,
        UIElement content,
        string closeText = "关闭")
    {
        await ShowCoreAsync(xamlRoot, title, content, null, closeText, isPrimaryDestructive: false);
    }

    /// <summary>带自定义内容 + 确认按钮。返回 true 表示点了主按钮。</summary>
    public static async Task<bool> ShowAsync(
        XamlRoot? xamlRoot,
        string title,
        UIElement content,
        string primaryText,
        string closeText)
    {
        var result = await ShowCoreAsync(
            xamlRoot, title, content, primaryText, closeText, isPrimaryDestructive: false);
        return result == ContentDialogResult.Primary;
    }

    private static TextBlock BuildMessageContent(string message) => new()
    {
        Text = message ?? string.Empty,
        TextWrapping = TextWrapping.Wrap,
        // 长报错不该把弹窗撑成一条细线，也不该让它无限制地拉宽。
        MinWidth = 320,
        MaxWidth = 560
    };

    /// <summary>
    /// 真正弹窗的地方。**统一在这里兜异常**：
    /// WinUI 同一个 XamlRoot 同时只允许一个 ContentDialog，抢不到会抛
    /// （本项目已有 DialogGate 处理这套，但这里也要自己兜住 —— 组件要能独立用）。
    /// </summary>
    private static async Task<ContentDialogResult> ShowCoreAsync(
        XamlRoot? xamlRoot,
        string title,
        UIElement content,
        string? primaryText,
        string closeText,
        bool isPrimaryDestructive)
    {
        if (xamlRoot is null)
        {
            return ContentDialogResult.None;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 460,
                // 内容淡入 + 轻微上移：ContentDialog 自带的是"整块弹出"，
                // 内容再补一层会让它看起来像"展开"而不是"砸下来"。
                Transitions = new TransitionCollection { new EntranceThemeTransition() }
            },
            CloseButtonText = closeText,
            DefaultButton = primaryText is null ? ContentDialogButton.Close : ContentDialogButton.Primary
        };

        // 快捷键与鼠标习惯（对齐 WinUiDialogService.AttachCancelShortcuts）：
        // Esc 关、右键关。ContentDialog 本身不处理右键，不补的话用户会以为"这弹窗关不掉"。
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape)
            {
                dialog.Hide();
                args.Handled = true;
            }
        };
        dialog.RightTapped += (_, args) =>
        {
            dialog.Hide();
            args.Handled = true;
        };

        if (!string.IsNullOrWhiteSpace(primaryText))
        {
            dialog.PrimaryButtonText = primaryText;
        }

        if (isPrimaryDestructive)
        {
            dialog.PrimaryButtonStyle = Application.Current.Resources["AccentButtonStyle"] as Style;
        }

        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // 已经有一个弹窗开着 / XamlRoot 还没准备好 —— 当作"用户没确认"处理，
            // 绝不把异常抛给调用方（调用方通常在一个业务流程中间，抛出去就断在半路）。
            return ContentDialogResult.None;
        }
    }

    /// <summary>
    /// 造一行"标签 + 说明"的表单项，弹窗内容用它可以保持同一套间距。
    /// </summary>
    public static UIElement BuildField(string label, UIElement control, string? hint = null)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        panel.Children.Add(control);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            panel.Children.Add(new TextBlock
            {
                Text = hint,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });
        }

        return panel;
    }

    /// <summary>造一个纵向表单容器，Spacing 统一。</summary>
    public static StackPanel BuildForm(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 14, MinWidth = 360 };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }
}
