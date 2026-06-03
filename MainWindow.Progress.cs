using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using GalExcleTools.Views;
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private void ShowGlobalProgress(string title, string detail)
        {
            _globalProgressCancellation?.Dispose();
            _globalProgressCancellation = new CancellationTokenSource();
            _globalProgressOperationTitle = title;
            _globalProgressStopwatch.Restart();
            _globalProgressElapsedTimer.Start();
            _isGlobalProgressVisible = true;
            GlobalProgress.Start(title, detail);

            GlobalProgressHost.Visibility = Visibility.Visible;
            _globalProgressLastPercent = 0;
            UpdateGlobalProgressRing(0);
            AnimateGlobalProgressHost(show: true);
        }

        private void UpdateGlobalProgress(string message, double percent, string? detail = null, bool isIndeterminate = false)
        {
            if (!_isGlobalProgressVisible)
            {
                ShowGlobalProgress(_globalProgressOperationTitle.Length == 0 ? "正在处理" : _globalProgressOperationTitle, message);
            }

            var clampedPercent = Math.Clamp(percent, 0, 100);
            GlobalProgress.Update(message, clampedPercent, detail, isIndeterminate);
            _globalProgressLastPercent = clampedPercent;
            UpdateGlobalProgressRing(clampedPercent);
            UpdateGlobalProgressElapsedText();
        }

        private void CompleteGlobalProgress(string message, string? detail = null)
        {
            _globalProgressStopwatch.Stop();
            _globalProgressElapsedTimer.Stop();
            GlobalProgress.Complete(message, detail);
            _globalProgressLastPercent = 100;
            UpdateGlobalProgressRing(100);
            UpdateGlobalProgressElapsedText();
        }

        private async Task HideGlobalProgressAfterDelayAsync(int delayMilliseconds = 1400)
        {
            await Task.Delay(delayMilliseconds);
            HideGlobalProgress();
        }

        private void HideGlobalProgress()
        {
            _globalProgressStopwatch.Reset();
            _globalProgressElapsedTimer.Stop();
            _isGlobalProgressVisible = false;
            GlobalProgress.Hide();
            _globalProgressCancellation?.Dispose();
            _globalProgressCancellation = null;
            AnimateGlobalProgressHost(show: false);
        }

        private CancellationToken GetGlobalProgressCancellationToken()
        {
            return _globalProgressCancellation?.Token ?? CancellationToken.None;
        }

        private async void GlobalProgressRing_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (!_isGlobalProgressVisible || _globalProgressCancellation is null || _globalProgressCancellation.IsCancellationRequested)
            {
                return;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "取消当前操作？",
                $"正在进行：{_globalProgressOperationTitle}\n取消后，已经写入的文件可能会保留，未完成的部分会停止。",
                "取消操作",
                "继续等待",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
            if (confirmed)
            {
                _globalProgressCancellation.Cancel();
                UpdateGlobalProgress("正在取消...", _globalProgressLastPercent, "等待当前步骤安全停止。");
            }
        }

        private void GlobalProgressElapsedTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            UpdateGlobalProgressElapsedText();
        }

        private void UpdateGlobalProgressElapsedText()
        {
            GlobalProgress.UpdateElapsed(_globalProgressStopwatch.Elapsed);
        }

        private void UpdateGlobalProgressRing(double percent)
        {
            var clampedPercent = Math.Clamp(percent, 0, 100);
            const double size = 86;
            const double stroke = 7;
            var radius = (size - stroke) / 2;
            var center = size / 2;

            if (clampedPercent <= 0)
            {
                GlobalProgressRingPath.Data = null;
                return;
            }

            if (clampedPercent >= 99.9)
            {
                var geometryGroup = new GeometryGroup();
                geometryGroup.Children.Add(CreateProgressRingArc(center, radius, 359.9));
                GlobalProgressRingPath.Data = geometryGroup;
                return;
            }

            GlobalProgressRingPath.Data = CreateProgressRingArc(center, radius, clampedPercent / 100d * 360d);
        }

        private static Geometry CreateProgressRingArc(double center, double radius, double angleDegrees)
        {
            var startPoint = new Windows.Foundation.Point(center, center - radius);
            var radians = (angleDegrees - 90) * Math.PI / 180d;
            var endPoint = new Windows.Foundation.Point(
                center + radius * Math.Cos(radians),
                center + radius * Math.Sin(radians));
            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Windows.Foundation.Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = angleDegrees > 180
            });

            return new PathGeometry
            {
                Figures = { figure }
            };
        }

        private string FormatProgressSpeed(long completedBytes)
        {
            var elapsedSeconds = Math.Max(0.1, _globalProgressStopwatch.Elapsed.TotalSeconds);
            if (completedBytes <= 0 || elapsedSeconds <= 0)
            {
                return string.Empty;
            }

            return $"{FormatFileSize((long)(completedBytes / elapsedSeconds))}/s";
        }

        private string FormatRemainingTime(long completedBytes, long totalBytes)
        {
            if (completedBytes <= 0 || totalBytes <= 0 || completedBytes >= totalBytes)
            {
                return "--:--";
            }

            var elapsedSeconds = Math.Max(0.1, _globalProgressStopwatch.Elapsed.TotalSeconds);
            var bytesPerSecond = completedBytes / elapsedSeconds;
            if (bytesPerSecond <= 0)
            {
                return "--:--";
            }

            return FormatElapsedTime(TimeSpan.FromSeconds((totalBytes - completedBytes) / bytesPerSecond));
        }

        private void AnimateGlobalProgressHost(bool show)
        {
            var transform = GlobalProgressHostTransform;
            var fromY = show ? 130 : 0;
            var toY = show ? 0 : 130;
            var fromOpacity = show ? 0 : 1;
            var toOpacity = show ? 1 : 0;
            GlobalProgressHost.Visibility = Visibility.Visible;
            transform.Y = fromY;
            GlobalProgressHost.Opacity = fromOpacity;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideAnimation = new DoubleAnimation
            {
                From = fromY,
                To = toY,
                Duration = TimeSpan.FromMilliseconds(show ? 240 : 180),
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, transform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.Y));

            var fadeAnimation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(show ? 220 : 160),
                EasingFunction = easing
            };
            Storyboard.SetTarget(fadeAnimation, GlobalProgressHost);
            Storyboard.SetTargetProperty(fadeAnimation, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(slideAnimation);
            storyboard.Children.Add(fadeAnimation);
            storyboard.Completed += (_, _) =>
            {
                transform.Y = toY;
                GlobalProgressHost.Opacity = toOpacity;
                if (!show)
                {
                    GlobalProgressHost.Visibility = Visibility.Collapsed;
                }
            };
            storyboard.Begin();
        }

    }
}
