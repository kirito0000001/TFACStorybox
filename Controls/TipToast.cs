using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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

    private const double TopMargin = 18;
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

        // 根 Grid 往往分了行（标题栏 + 内容），不跨行的话提示会被塞进标题栏那一行。
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
        out TranslateTransform transform)
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

        transform = new TranslateTransform { Y = -10 };
        var border = new Border
        {
            Child = content,
            Padding = new Thickness(16, 10, 16, 10),
            CornerRadius = new CornerRadius(CornerRadiusValue),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1F, 0x1F, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            RenderTransform = transform,
            Tag = severity == TipToastSeverity.Error ? "error" : null
        };

        return border;
    }

    private static void Play(TranslateTransform transform, Border root)
    {
        // 先把不透明度落到 1 再起动画：动画里 From=0 会盖住它。
        // 万一 Storyboard 因故没跑起来（目标不在树里等），提示至少"看得见"，
        // 而不是留一个 Opacity=0 的隐形块在那 —— 那种 bug 排查起来最费劲。
        root.Opacity = 1;

        var duration = new Duration(TimeSpan.FromMilliseconds(170));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(fade, root);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation { From = -10, To = 0, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);

        try
        {
            storyboard.Begin();
        }
        catch (Exception)
        {
            // 动画失败不影响功能：已经 Opacity=1，静态显示。
            transform.Y = 0;
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
