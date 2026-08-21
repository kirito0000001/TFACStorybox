using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private const int MaxFloatingTipCount = 4;

        private void ShowFloatingTip(
            InfoBarSeverity severity,
            string title,
            string message,
            Exception? exception = null,
            string? logMessage = null)
        {
            ShowFloatingTip(FloatingTipsPanel, severity, title, message, exception, logMessage);
        }

        private void ShowFloatingTip(
            Panel panel,
            InfoBarSeverity severity,
            string title,
            string message,
            Exception? exception = null,
            string? logMessage = null)
        {
            var normalizedSeverity = NormalizeFloatingTipSeverity(severity);
            var effectiveLogMessage = string.IsNullOrWhiteSpace(logMessage)
                ? string.IsNullOrWhiteSpace(message) ? title : $"{title}：{message}"
                : logMessage;
            AppendLog(GetFloatingTipLogKind(normalizedSeverity), effectiveLogMessage, exception);

            if (normalizedSeverity == InfoBarSeverity.Error)
            {
                CopyErrorLogToClipboard(title, message, effectiveLogMessage, exception);
            }

            var tip = CreateFloatingTipBar(normalizedSeverity, title, message);
            TrimFloatingTipsBeforeAdd(panel);
            AddFloatingTipWithEntrance(panel, tip);

            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = GetFloatingTipDuration(normalizedSeverity);
            timer.Tick += (_, _) => RemoveFloatingTip(panel, tip);
            _floatingTipTimers[tip] = timer;
            timer.Start();
        }

        private static InfoBarSeverity NormalizeFloatingTipSeverity(InfoBarSeverity severity)
        {
            return severity switch
            {
                InfoBarSeverity.Informational => InfoBarSeverity.Success,
                InfoBarSeverity.Warning => InfoBarSeverity.Warning,
                InfoBarSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Success
            };
        }

        private static TimeSpan GetFloatingTipDuration(InfoBarSeverity severity)
        {
            return severity switch
            {
                InfoBarSeverity.Error => TimeSpan.FromSeconds(5),
                InfoBarSeverity.Warning => TimeSpan.FromSeconds(3.2),
                _ => TimeSpan.FromSeconds(2.6)
            };
        }

        private static LogKind GetFloatingTipLogKind(InfoBarSeverity severity)
        {
            return severity switch
            {
                InfoBarSeverity.Error => LogKind.Error,
                InfoBarSeverity.Warning => LogKind.Warning,
                _ => LogKind.User
            };
        }

        private static InfoBar CreateFloatingTipBar(InfoBarSeverity severity, string title, string message)
        {
            return new InfoBar
            {
                Severity = NormalizeFloatingTipSeverity(severity),
                Title = title,
                Message = message,
                IsOpen = true,
                IsClosable = true,
                RenderTransform = new TranslateTransform { Y = -18 },
                Opacity = 0
            };
        }

        private void AddFloatingTipWithEntrance(Panel panel, InfoBar tip)
        {
            tip.Closed += (_, _) => RemoveFloatingTip(panel, tip);
            panel.Children.Add(tip);
            PlayFloatingTipEntrance(tip);
        }

        private void PlayFloatingTipEntrance(InfoBar tip)
        {
            var steps = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (_, _) =>
            {
                steps++;
                var progress = Math.Min(1, steps / 12.0);
                var eased = 1 - Math.Pow(1 - progress, 3);
                tip.Opacity = eased;
                if (tip.RenderTransform is TranslateTransform transform)
                {
                    transform.Y = -18 + 18 * eased;
                }

                if (progress >= 1)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        private void TrimFloatingTipsBeforeAdd(Panel panel)
        {
            while (panel.Children.OfType<InfoBar>().Count() >= MaxFloatingTipCount)
            {
                var oldestTip = panel.Children
                    .OfType<InfoBar>()
                    .FirstOrDefault(item => item.Severity != InfoBarSeverity.Error)
                    ?? panel.Children.OfType<InfoBar>().First();
                RemoveFloatingTip(panel, oldestTip);
            }
        }

        private void RemoveFloatingTip(Panel panel, InfoBar tip)
        {
            if (_floatingTipTimers.Remove(tip, out var timer))
            {
                timer.Stop();
            }

            panel.Children.Remove(tip);
        }

        private void ClearFloatingTips(Panel panel)
        {
            foreach (var tip in panel.Children.OfType<InfoBar>().ToList())
            {
                RemoveFloatingTip(panel, tip);
            }
        }

        private void CopyErrorLogToClipboard(
            string title,
            string message,
            string logMessage,
            Exception? exception)
        {
            var errorText = $"[{DateTime.Now:HH:mm:ss}] [Error] {logMessage}\r\n" +
                $"Title: {title}\r\n" +
                $"Message: {message}";
            if (exception is not null)
            {
                errorText += $"\r\nException: {exception}";
            }

            try
            {
                var package = new DataPackage();
                package.SetText(errorText);
                Clipboard.SetContent(package);
                Clipboard.Flush();
            }
            catch (Exception clipboardException)
            {
                AppendLog(LogKind.Warning, "复制错误日志到剪贴板失败。", clipboardException);
            }
        }
    }
}
