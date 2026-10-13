using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private const int MaxFloatingTipCount = 2;
        private const string PersistentStoryStatusTag = "PersistentStoryStatus";

        private void ShowFloatingTip(
            InfoBarSeverity severity,
            string title,
            string message,
            Exception? exception = null,
            string? logMessage = null)
        {
            // 和 AppendLog 同一个道理：提示也可能从后台线程发出来（选路回调、进程轮询……），
            // 而它会动界面。不兜住就是 0x8001010E（RPC_E_WRONG_THREAD），
            // 而且那种 COM 异常的 **Message 是空的** —— 外面只报一句"同步失败"，
            // 看不出任何线索。2026-10-13 实测踩到，查了很久。
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(
                    () => ShowFloatingTip(severity, title, message, exception, logMessage));
                return;
            }

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
                CopyErrorLogToClipboard(effectiveLogMessage, exception);
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
                InfoBarSeverity.Informational => InfoBarSeverity.Informational,
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
                var removableTips = panel.Children
                    .OfType<InfoBar>()
                    .Where(item => !string.Equals(item.Tag as string, PersistentStoryStatusTag, StringComparison.Ordinal))
                    .ToList();
                var oldestTip = removableTips.FirstOrDefault(item => item.Severity != InfoBarSeverity.Error)
                    ?? removableTips.FirstOrDefault()
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

        private void CopyErrorLogToClipboard(string logMessage, Exception? exception)
        {
            try
            {
                var (_, copyText) = CreateLogText(LogKind.Error, logMessage, exception, DateTime.Now);
                CopyTextToClipboard(copyText, flush: true);
            }
            catch (Exception clipboardException)
            {
                AppendLog(LogKind.Warning, "复制错误日志到剪贴板失败。", clipboardException);
            }
        }
    }
}
