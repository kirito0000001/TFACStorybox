using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

/// <summary>
/// 卡片外观的**唯一出处**：尺寸、圆角、画笔、标题/副标题字号。
///
/// 【为什么要它】之前卡片是各页面自己拿 C# 拼的（GridViewItemFactory 只管尺寸，
/// 圆角/底色/字号各写各的），所以"同一个工具箱里像好几套卡片"。
/// 用户 2026-10-17 定的口径：**只用两种变体** ——
///   · 大卡（<see cref="BigCardWidth"/> × <see cref="BigCardHeight"/>）：素材库 / 项目这类"入口卡"；
///   · 小扁平卡（<see cref="SmallCardWidth"/> × <see cref="SmallCardHeight"/>）：其余列表项。
/// 观感对齐隔壁零境工具箱（CrossingVoidZDTool）：扁平、半透明白底、圆角 8、无阴影无描边。
///
/// 【怎么用】新卡片一律走 <see cref="CreateBigCard"/> / <see cref="CreateSmallCard"/>，
/// 内容塞进返回的 Border 里（标题用 <see cref="CreateTitle"/>、副标题用 <see cref="CreateSubtitle"/>）；
/// 旧卡片在迁移时逐个换成这两个入口，别在页面里再写死颜色和圆角。
/// </summary>
internal static class CardStyles
{
    // ── 尺寸（两种变体，别再出现第三种）──────────────────────────────
    public const double BigCardWidth = 280;
    public const double BigCardHeight = 84;
    public const double SmallCardWidth = 148;
    public const double SmallCardHeight = 64;
    public const double CardGap = 16;

    // ── 颜色（扁平半透明，和零境一致：底色只比页面亮一点，没有描边和阴影）──
    private static readonly Windows.UI.Color SurfaceColor = Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
    private static readonly Windows.UI.Color SurfaceHoverColor = Windows.UI.Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
    private static readonly Windows.UI.Color SurfacePressedColor = Windows.UI.Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF);

    /// <summary>卡片底色（常态）。</summary>
    public static readonly SolidColorBrush SurfaceBrush = new(SurfaceColor);

    /// <summary>卡片底色（悬停）。零境那套的悬停就是"底色亮一格"，不换描边。</summary>
    public static readonly SolidColorBrush SurfaceHoverBrush = new(SurfaceHoverColor);

    /// <summary>卡片底色（按下）。</summary>
    public static readonly SolidColorBrush SurfacePressedBrush = new(SurfacePressedColor);

    /// <summary>卡片主标题。</summary>
    public static readonly SolidColorBrush TitleBrush = new(Microsoft.UI.Colors.White);

    /// <summary>卡片副标题 / 说明文字。</summary>
    public static readonly SolidColorBrush SubtitleBrush = new(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

    private const double CornerRadiusValue = 8;
    private const double CardPaddingX = 16;
    private const double CardPaddingY = 12;
    public const double TitleFontSize = 15;
    public const double SubtitleFontSize = 12;

    /// <summary>大卡：素材库 / 项目这类入口卡。</summary>
    public static GridViewItem CreateBigCard(object? tag = null)
    {
        return CreateCard(BigCardWidth, BigCardHeight, tag);
    }

    /// <summary>小扁平卡：其余列表项。</summary>
    public static GridViewItem CreateSmallCard(object? tag = null)
    {
        return CreateCard(SmallCardWidth, SmallCardHeight, tag);
    }

    /// <summary>
    /// 造一张卡片：外层 GridViewItem（管尺寸 / 边距 / 命中），内层 Border（管圆角 / 底色 / 内边距）。
    /// 内容往 <c>(Grid)item.Content</c> 的 Border 里加。
    /// </summary>
    public static GridViewItem CreateCard(double width, double height, object? tag = null)
    {
        var surface = new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(CornerRadiusValue),
            Padding = new Thickness(CardPaddingX, CardPaddingY, CardPaddingX, CardPaddingY),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var item = new GridViewItem
        {
            Width = width,
            Height = height,
            Margin = new Thickness(0, 0, CardGap, CardGap),
            Tag = tag,
            Content = surface,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            // 悬停 / 按下的底色变化就交给这两条画笔（零境的手感：不换描边、不加阴影）。
            Background = SurfaceBrush
        };

        return item;
    }

    /// <summary>卡片主标题。</summary>
    public static TextBlock CreateTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = TitleFontSize,
            Foreground = TitleBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    /// <summary>卡片副标题（说明行）。</summary>
    public static TextBlock CreateSubtitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = SubtitleFontSize,
            Foreground = SubtitleBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }
}
