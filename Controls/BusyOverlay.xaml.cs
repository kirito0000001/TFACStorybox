using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace GalExcleTools.Controls;

/// <summary>
/// 全屏忙碌遮罩（可复用控件）。
///
/// 视觉三层：深紫底 → 三团极光（径向渐变缓慢漂移）→ 中心声波柱。
/// 对外只有 <see cref="Show"/> / <see cref="Hide"/> / <see cref="UpdateMessage"/>。
///
/// 几个踩过的坑都封在内部了，调用方不需要知道：
/// · 不能用 Collapsed 控制可见性（没有布局尺寸 → 首次动画建不出来），
///   改用 Opacity + IsHitTestVisible；
/// · 极光视觉只建一次并缓存，复用比重建少卡一帧；
/// · 动画全部走 Composition，主线程忙的时候也不掉帧。
/// </summary>
public sealed partial class BusyOverlay : UserControl
{
    private const int BarCount = 7;

    private readonly List<Rectangle> _bars = [];

    private Storyboard? _barsStoryboard;

    private ContainerVisual? _auroraVisual;

    private readonly List<CompositionRadialGradientBrush> _auroraBrushes = [];

    private readonly List<(Vector2 From, Vector2 Via, Vector2 To, int Seconds, bool Alternate)> _auroraMotions = [];

    public BusyOverlay()
    {
        InitializeComponent();
        BuildBars();
        Loaded += (_, _) =>
        {
            // 初始透明放在**合成层**上，不设 XAML 的 Opacity ——
            // 元素的 Opacity 会和视觉的 Opacity 相乘，设成 0 之后
            // 再动画到 1 也只会得到 0。
            ElementCompositionPreview.GetElementVisual(this).Opacity = 0f;
            EnsureAurora();
        };
        SizeChanged += (_, _) =>
        {
            // 首次布局完才会有真实尺寸 —— 这时候才建得出来。
            if (_auroraVisual is null)
            {
                EnsureAurora();
                return;
            }

            ResizeAurora();
        };
    }

    /// <summary>当前是否处于"忙碌"状态。</summary>
    public bool IsActive { get; private set; }

    /// <summary>显示遮罩并换文案。重复调用只更新文案，不会重播动画。</summary>
    public void Show(string message)
    {
        UpdateMessage(message);

        if (IsActive)
        {
            return;
        }

        IsActive = true;
        IsHitTestVisible = true;

        EnsureAurora();
        StartBars();
        Fade(1f, 450);
    }

    public void Hide()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        IsHitTestVisible = false;

        StopBars();
        Fade(0f, 300);
    }

    public void UpdateMessage(string message)
    {
        if (MessageText is not null && !string.IsNullOrEmpty(message))
        {
            MessageText.Text = message;
        }
    }

    /// <summary>提示行（默认是"可随时取消"），不同场景可以换。</summary>
    public void SetHint(string? hint)
    {
        if (HintText is null)
        {
            return;
        }

        HintText.Text = hint ?? string.Empty;
        HintText.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- 声波柱 ----------

    private void BuildBars()
    {
        if (BarsPanel is null || _bars.Count > 0)
        {
            return;
        }

        for (var index = 0; index < BarCount; index++)
        {
            var bar = new Rectangle
            {
                Width = 5,
                Height = 30,
                RadiusX = 2.5,
                RadiusY = 2.5,
                Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1),
                RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 0.3 }
            };

            _bars.Add(bar);
            BarsPanel.Children.Add(bar);
        }
    }

    /// <summary>从左往右依次起伏 —— 相邻错开 85ms，看起来像"播放"，但不代表进度。</summary>
    private void StartBars()
    {
        _barsStoryboard ??= BuildBarsStoryboard();
        _barsStoryboard.Begin();
    }

    private void StopBars()
    {
        _barsStoryboard?.Stop();
    }

    private Storyboard BuildBarsStoryboard()
    {
        var storyboard = new Storyboard();

        for (var index = 0; index < _bars.Count; index++)
        {
            var rise = new DoubleAnimation
            {
                From = 0.25,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(index * 85),
                // ScaleY 不是合成器属性，不开这个动画会静默不执行。
                EnableDependentAnimation = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            // 必须把**元素**当目标、用属性路径，不能直接把 Transform 当目标 ——
            // 那样 Storyboard 找不到可动画的属性，静默不动（"动效没了"就是这个）。
            Storyboard.SetTarget(rise, _bars[index]);
            Storyboard.SetTargetProperty(rise, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
            storyboard.Children.Add(rise);
        }

        return storyboard;
    }

    // ---------- 极光 ----------

    private void EnsureAurora()
    {
        if (AuroraHost is null)
        {
            return;
        }

        // 还没布局出真实尺寸时不建：建了就是 2×2，极光等于不存在。
        // SizeChanged 会再来一次。
        if (AuroraHost.ActualWidth < 2 || AuroraHost.ActualHeight < 2)
        {
            return;
        }

        var width = (float)AuroraHost.ActualWidth;
        var height = (float)AuroraHost.ActualHeight;

        if (_auroraVisual is not null)
        {
            _auroraVisual.Size = new Vector2(width, height);

            if (_auroraBrushes.Count > 0)
            {
                StartAuroraAnimations();
            }

            return;
        }

        // 元素进树之后才有 ComposableVisual；EnsureAurora 只会在 Loaded / Show 之后被调用。
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;

        var container = compositor.CreateContainerVisual();
        container.Size = new Vector2(width, height);
        ElementCompositionPreview.SetElementChildVisual(AuroraHost, container);

        _auroraMotions.Clear();

        void AddBlob(Color core, float radius, Vector2 from, Vector2 via, Vector2 to, int seconds, bool alternate)
        {
            var brush = compositor.CreateRadialGradientBrush();
            brush.MappingMode = CompositionMappingMode.Relative;
            brush.EllipseCenter = from;
            brush.EllipseRadius = new Vector2(radius, radius);

            void Stop(float offset, byte alpha)
            {
                var stop = compositor.CreateColorGradientStop();
                stop.Offset = offset;
                stop.Color = Color.FromArgb(alpha, core.R, core.G, core.B);
                brush.ColorStops.Add(stop);
            }

            // 边缘必须归零，否则会看到圆盘边。
            Stop(0.00f, core.A);
            Stop(0.45f, (byte)(core.A * 0.55));
            Stop(1.00f, 0x00);

            var visual = compositor.CreateSpriteVisual();
            visual.Brush = brush;
            visual.Size = new Vector2(width, height);
            container.Children.InsertAtTop(visual);

            _auroraBrushes.Add(brush);
            _auroraMotions.Add((from, via, to, seconds, alternate));
        }

        // 周期互质（19 / 26 / 34），永远不同步 —— 这是"看不出循环"的关键。
        AddBlob(Color.FromArgb(0x34, 0x7B, 0x00, 0xD4), 0.62f,
            new Vector2(0.22f, 0.28f), new Vector2(0.45f, 0.55f), new Vector2(0.30f, 0.80f), 26, true);
        AddBlob(Color.FromArgb(0x2A, 0x3B, 0x82, 0xF6), 0.70f,
            new Vector2(0.82f, 0.34f), new Vector2(0.55f, 0.62f), new Vector2(0.78f, 0.86f), 34, false);
        AddBlob(Color.FromArgb(0x22, 0xE0, 0xA0, 0xFF), 0.52f,
            new Vector2(0.50f, 0.72f), new Vector2(0.25f, 0.45f), new Vector2(0.66f, 0.22f), 19, true);

        _auroraVisual = container;
        StartAuroraAnimations();
    }

    private void StartAuroraAnimations()
    {
        if (_auroraBrushes.Count == 0 || _auroraMotions.Count != _auroraBrushes.Count)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));

        for (var index = 0; index < _auroraBrushes.Count; index++)
        {
            var (from, via, to, seconds, alternate) = _auroraMotions[index];

            var move = compositor.CreateVector2KeyFrameAnimation();
            move.InsertKeyFrame(0.00f, from);
            move.InsertKeyFrame(0.45f, via, easing);
            move.InsertKeyFrame(1.00f, to, easing);
            move.Duration = TimeSpan.FromSeconds(seconds);
            move.IterationBehavior = AnimationIterationBehavior.Forever;
            if (alternate)
            {
                move.Direction = AnimationDirection.Alternate;
            }

            _auroraBrushes[index].StartAnimation("EllipseCenter", move);
        }
    }

    private void ResizeAurora()
    {
        if (_auroraVisual is null || AuroraHost is null)
        {
            return;
        }

        if (AuroraHost.ActualWidth < 2 || AuroraHost.ActualHeight < 2)
        {
            return;
        }

        var size = new Vector2((float)AuroraHost.ActualWidth, (float)AuroraHost.ActualHeight);
        _auroraVisual.Size = size;

        // 三团色斑是独立的 SpriteVisual，容器变大不会带着它们变 ——
        // 不逐个改尺寸的话，窗口一放大极光就只剩左上角一小块。
        foreach (var child in _auroraVisual.Children)
        {
            if (child is SpriteVisual sprite)
            {
                sprite.Size = size;
            }
        }
    }

    // ---------- 淡入淡出 ----------

    /// <summary>合成器 Opacity：Storyboard 的 Opacity 是主线程逐帧算的，忙的时候会"蹦出来"。</summary>
    private void Fade(float to, int milliseconds)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var visual = ElementCompositionPreview.GetElementVisual(this);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, to, compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.2f, 0f), new Vector2(0f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("Opacity", fade);
    }
}
