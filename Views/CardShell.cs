using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

/// <summary>
/// 卡片外壳的**唯一出处**：两种变体的尺寸、内边距、圆角、描边、字号。
///
/// 【口径来自隔壁零境工具箱（CrossingVoidZDTool）】用户 2026-10-17 定的：
///   · 卡片**就是 Button 自己**，别再套一层 Border —— Button 的内容对齐默认居中不拉伸，
///     套进去那层 Border 只有一行字那么高，卡框的高度落不到内容上；
///   · **底色不要自己设** —— 悬停 / 按下由系统状态色换亮一档、过渡自然；自己铺不透明底
///     会在悬停那一刻被顶掉，卡片反而闪一下；
///   · 描边圆角照零境：1px <c>CardStrokeColorDefaultBrush</c> + 圆角 8 + 内边距 20,0。
///
/// 【两种变体，只用这两种】
///   · <see cref="CreateFlatCard"/>：280×76 小扁平卡（纯文字 / 图标+文字）；
///   · <see cref="CreateImageCard"/>：190×220 带缩略图卡（背景图 / 立绘 / 角色卡）。
/// 迁移时一律换成这两个入口，别在页面里再写死尺寸、圆角、颜色。
/// </summary>
internal static class CardShell
{
    public const double FlatCardWidth = 280;
    public const double FlatCardHeight = 76;
    public const double ImageCardWidth = 190;
    public const double ImageCardHeight = 220;

    /// <summary>背景图专属尺寸：**严格 16:9**（272×153）。272 + 12 间距 = 284 ≤ 292（零境那套列宽）。
    /// 用户 2026-10-17 定的：背景图卡按 16:9 出，方便一眼看构图。</summary>
    public const double BackgroundCardWidth = 272;
    public const double BackgroundCardHeight = 153;

    /// <summary>卡片间距（零境用 12；卡片自身不带外边距时靠 ItemsWrapGrid 的 ItemWidth 排）。</summary>
    public const double CardGap = 12;

    public const double TitleFontSize = 16;
    public const double SubtitleFontSize = 12;

    private const double CardCornerRadius = 8;
    private const double CardBorderThickness = 1;
    private const double FlatCardPaddingX = 20;

    /// <summary>280×76 小扁平卡：内容横向靠左、纵向居中。</summary>
    public static Button CreateFlatCard(object? tag = null, UIElement? content = null)
    {
        return CreateCardShell(FlatCardWidth, FlatCardHeight, new Thickness(FlatCardPaddingX, 0, FlatCardPaddingX, 0), tag, content);
    }

    /// <summary>190×220 带缩略图卡。</summary>
    public static Button CreateImageCard(object? tag = null, UIElement? content = null)
    {
        return CreateCardShell(ImageCardWidth, ImageCardHeight, new Thickness(12), tag, content);
    }

    /// <summary>背景图卡：**16:9**（272×153）、内边距 8（图要占满，别留大白边）。</summary>
    public static Button CreateBackgroundCard(object? tag = null, UIElement? content = null)
    {
        // canDrag: 背景图卡要参与**自写重排**（CanDrag 让框架替我们识别拖拽手势，
        // 不需要长按阈值；按下不动仍是点击，见 MainWindow.AssetLibrary.Reorder.cs 的说明）。
        return CreateCardShell(BackgroundCardWidth, BackgroundCardHeight, new Thickness(8), tag, content, canDrag: true);
    }

    /// <summary>
    /// 造一张卡片外壳：Button 形态、固定尺寸、圆角 8、1px 主题描边、**不设底色**。
    /// </summary>
    public static Button CreateCardShell(
        double width,
        double height,
        Thickness padding,
        object? tag = null,
        UIElement? content = null,
        bool canDrag = false)
    {
        var button = new Button
        {
            Width = width,
            Height = height,
            Padding = padding,
            Margin = new Thickness(0, 0, CardGap, CardGap),
            HorizontalAlignment = HorizontalAlignment.Left,
            Tag = tag,
            // CanDrag = true：框架会把"按住后移动"识别成拖拽手势（自己写重排要用），
            // 而"按住不动再松手"仍然是点击 —— 不需要长按阈值那套。
            CanDrag = canDrag
        };

        // 外观走 App 级样式（和零境同构：描边 / 圆角 / 对齐都在 XAML 里用 {ThemeResource} 定）。
        // ⚠️ 这里**只取 Style，不取画笔**：代码里 Application.Current.Resources[…] 拿到的是
        // **应用级主题**那套画笔，而本工具的夜间模式切的是元素的 RequestedTheme ——
        // 代码取画笔就会拿错（用户 2026-10-17 报的"黑底黑字"就是这个）。
        if (Application.Current?.Resources is { } resources &&
            resources.TryGetValue("CardButtonStyle", out var style) &&
            style is Style cardStyle)
        {
            button.Style = cardStyle;
        }

        if (content is not null)
        {
            button.Content = content;
        }

        return button;
    }

    /// <summary>卡片主标题（16 SemiBold、超长省略）。</summary>
    public static TextBlock CreateTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = TitleFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
    }

    /// <summary>卡片副标题 / 说明。用**不透明度**压暗，而不是取主题画笔 ——
    /// 见上面那段说明：代码里取画笔不跟夜间模式，继承主文字色 + 降透明度才是主题安全的。</summary>
    public static TextBlock CreateSubtitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = SubtitleFontSize,
            Opacity = 0.72,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
    }

    /// <summary>
    /// 主题画笔按 key 取（和零境一个规矩：**不自造颜色**，能用系统主题键就用）。
    /// 取不到返回 null —— 保持控件默认外观，绝不写死一个灰色顶上去。
    /// </summary>
    private static Brush? TryGetThemeBrush(string key)
    {
        if (Application.Current?.Resources is { } resources &&
            resources.TryGetValue(key, out var value) &&
            value is Brush brush)
        {
            return brush;
        }

        return null;
    }
}
