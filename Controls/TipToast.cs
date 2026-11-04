using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;

namespace GalExcleTools.Controls;

/// <summary>
/// 提示等级。刻意不复用 <c>InfoBarSeverity</c>：这个文件要能整体搬到别的项目，
/// 少一个对 WinUI 具体控件类型的依赖，就少一处要跟着改的地方。
/// </summary>
internal enum TipToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// 顶部居中的轻提示（toast）。
///
/// 【为什么是「单文件、无 XAML」】
/// 目标就是「一个地方改好，拷到别的项目还能用」。UserControl 得配 .xaml + x:Class +
/// csproj 里的 Page 项，换个项目要同步改命名空间、资源字典、工程文件 ——
/// 复制成本比重写还高。这里整个视觉树都在代码里搭，而且**不引用任何 ThemeResource**：
/// 拷走这一个文件、改掉 namespace 就能跑，长相不随目标项目的主题资源漂移。
///
/// 【用法】
///   TipToast.Attach(RootGrid);                                   // 窗口根 Grid，构造里一次
///   TipToast.Show(TipToastSeverity.Warning, "标题", "说明文字");
///   TipToast.Show(TipToastSeverity.Error, "失败", ex.Message, TimeSpan.FromSeconds(6));
///
/// 【配色（用户 2026-11-04 定）】
///   普通 / 成功 → 白字；警告 → 黄字；报错 → 红字。
///   背景统一是深色半透明块 —— 深浅两套主题下都读得清，不用为两套主题各写一份。
/// </summary>
internal static class TipToast
{
    /// <summary>屏幕上同时最多几条。超出时挤掉最旧的那条（报错优先保留，见 <see cref="Trim"/>）。</summary>
    private const int MaxVisible = 2;

    /// <summary>
    /// 距窗口顶端的距离。参考 AX 工具箱那个提示：上沿大约落在顶端往下 30px，
    /// 也就是**贴着标题栏下沿**，而不是躲进内容区里。
    /// </summary>
    private const double TopMargin = 28;
    private const double CornerRadiusValue = 8;

    private static readonly List<ToastEntry> Live = new();
    private static StackPanel? _host;

    /// <summary>
    /// 把提示层挂到某个容器上（一般就是窗口根 Grid），整个进程调一次即可。
    /// 重复调用是安全的：只认第一次，后面的直接返回。
    /// </summary>
    public static void Attach(Panel layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_host is not null)
        {
            return;
        }

        var host = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, TopMargin, 0, 0),
            Spacing = 8,
            MaxWidth = 720,
            // 容器不吃点击：提示是"看"的，挡在页面上会拦住下面的按钮。
            IsHitTestVisible = false
        };

        // ⚠️ 从**第 0 行**开始跨行，配上面那个 28px 边距 —— 让提示贴住标题栏下沿。
        //
        // 2026-11-04 试过挂第 1 行（内容区），结果整体低了 48px（标题栏高度），
        // 用户一眼看出"太下方"：AX 那个 TeachingTip 是没有 Target 的，
        // 它相对**窗口**定位，不认同内容区的排版。
        // 这里同理：`IsHitTestVisible=false`，压在标题栏上也不会挡住拖动和按钮。
        if (layer is Grid grid && grid.RowDefinitions.Count > 1)
        {
            Grid.SetRow(host, 0);
            Grid.SetRowSpan(host, grid.RowDefinitions.Count);
        }

        // UWP/WinUI 里 Canvas.ZIndex 对任意 Panel 的子元素都生效，不限于 Canvas。
        Canvas.SetZIndex(host, 9000);
        layer.Children.Add(host);
        _host = host;
    }

    /// <summary>弹一条提示。可以从任意线程调用。</summary>
    public static void Show(
        TipToastSeverity severity,
        string title,
        string? message = null,
        TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var host = _host;
        if (host is null)
        {
            // 没 Attach 就当没这回事：提示丢了不影响主流程（日志那边另有记录）。
            return;
        }

        var dispatcher = host.DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        if (!dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => Show(severity, title, message, duration));
            return;
        }

        Trim();

        var root = BuildToast(severity, title, message, out var transform);
        var entry = new ToastEntry(root);
        host.Children.Add(root);
        Live.Add(entry);

        Play(transform, root);

        var timer = dispatcher.CreateTimer();
        timer.Interval = duration ?? DefaultDuration(severity);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Dismiss(entry);
        };
        entry.Timer = timer;
        timer.Start();
    }

    /// <summary>立刻清掉所有提示（切页面、退出前收尾用）。</summary>
    public static void Clear()
    {
        foreach (var entry in Live.ToArray())
        {
            Dismiss(entry, immediate: true);
        }
    }

    // ------------------------------------------------------------------
    // 内部实现
    // ------------------------------------------------------------------

    private sealed class ToastEntry(Border root)
    {
        public Border Root { get; } = root;
        public DispatcherQueueTimer? Timer { get; set; }
        public bool Removed { get; set; }
    }

    /// <summary>
    /// 超出上限时挤掉一条。优先挤「非报错」里最旧的 ——
    /// 报错往往才是用户要看的那条，被一条"已复制"顶掉就白弹了。
    /// </summary>
    private static void Trim()
    {
        while (Live.Count >= MaxVisible)
        {
            var victim = Live.Find(item => !item.Removed && item.Root.Tag as string != "error")
                ?? Live.Find(item => !item.Removed);
            if (victim is null)
            {
                break;
            }

            Dismiss(victim, immediate: true);
        }
    }

    private static Border BuildToast(
        TipToastSeverity severity,
        string title,
        string? message,
        out ScaleTransform transform)
    {
        var titleColor = severity switch
        {
            TipToastSeverity.Warning => Color.FromArgb(0xFF, 0xFF, 0xD1, 0x66), // 黄
            TipToastSeverity.Error => Color.FromArgb(0xFF, 0xFF, 0x7B, 0x7B),   // 红
            _ => Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)                         // 白（普通 / 成功）
        };

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? message : title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(titleColor)
        });

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(message))
        {
            content.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 3,
                Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF))
            });
        }

        // 入场**只有淡入 + 极轻微放大**，没有位移 —— 照 WinUI TeachingTip 的默认观感
        // （AX 工具箱那个提示就是 TeachingTip）。原先自己加的"从上滑下来"是多余的，
        // 2026-11-04 用户点名去掉了。
        transform = new ScaleTransform { ScaleX = 0.94, ScaleY = 0.94 };
        var border = new Border
        {
            Child = content,
            Padding = new Thickness(16, 10, 16, 10),
            CornerRadius = new CornerRadius(CornerRadiusValue),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1F, 0x1F, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            // 从正上方那一点放大，看起来像"从顶上浮现"，而不是从中心胀开。
            RenderTransformOrigin = new Point(0.5, 0),
            RenderTransform = transform,
            Tag = severity == TipToastSeverity.Error ? "error" : null
        };

        return border;
    }

    private static void Play(ScaleTransform transform, Border root)
    {
        // 先把不透明度落到 1 再起动画：动画里 From=0 会盖住它。
        // 万一 Storyboard 因故没跑起来（目标不在树里等），提示至少"看得见"，
        // 而不是留一个 Opacity=0 的隐形块在那 —— 那种 bug 排查起来最费劲。
        root.Opacity = 1;

        var duration = new Duration(TimeSpan.FromMilliseconds(200));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(fade, root);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var growX = new DoubleAnimation { From = 0.94, To = 1, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(growX, transform);
        Storyboard.SetTargetProperty(growX, "ScaleX");

        var growY = new DoubleAnimation { From = 0.94, To = 1, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(growY, transform);
        Storyboard.SetTargetProperty(growY, "ScaleY");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(growX);
        storyboard.Children.Add(growY);

        try
        {
            storyboard.Begin();
        }
        catch (Exception)
        {
            // 动画失败不影响功能：已经 Opacity=1，缩放落回 1，静态显示。
            transform.ScaleX = 1;
            transform.ScaleY = 1;
        }
    }

    private static void Dismiss(ToastEntry entry, bool immediate = false)
    {
        if (entry.Removed)
        {
            return;
        }

        entry.Removed = true;
        entry.Timer?.Stop();
        Live.Remove(entry);

        var host = _host;
        if (host is null)
        {
            return;
        }

        if (immediate)
        {
            host.Children.Remove(entry.Root);
            return;
        }

        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(120))
        };
        Storyboard.SetTarget(fade, entry.Root);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        // 动画走完再摘：直接从树里删会让退场变成"啪一下没了"。
        storyboard.Completed += (_, _) => host.Children.Remove(entry.Root);

        try
        {
            storyboard.Begin();
        }
        catch (Exception)
        {
            // 退场动画失败也必须摘掉，否则这条提示会永远挂在屏幕上。
            host.Children.Remove(entry.Root);
        }
    }

    private static TimeSpan DefaultDuration(TipToastSeverity severity) => severity switch
    {
        TipToastSeverity.Error => TimeSpan.FromSeconds(5),
        TipToastSeverity.Warning => TimeSpan.FromSeconds(3.2),
        _ => TimeSpan.FromSeconds(2.6)
    };
}
