using System;
using System.Linq;
using GalExcleTools.Controls;
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

            var normalizedSeverity = RecordFloatingTip(severity, title, message, exception, logMessage);

            // 顶部居中的轻提示（见 Controls/TipToast.cs）。
            // 原来是右上角堆 InfoBar —— 2026-11-04 按用户要求换成「居中、圆角、白/黄/红字」。
            // 这里是**唯一**的出口：114 个调用点用的都是这个签名，换实现不用动它们。
            TipToast.Show(ToTipToastSeverity(normalizedSeverity), title, message);
        }

        private void ShowFloatingTip(
            Panel panel,
            InfoBarSeverity severity,
            string title,
            string message,
            Exception? exception = null,
            string? logMessage = null)
        {
            // 剧情编辑器那条**持久提示**仍然走 InfoBar：它要一直挂着、要能被 Trim 认出来，
            // 和"几秒就消失"的轻提示不是一类东西，所以没有跟着换。
            var normalizedSeverity = RecordFloatingTip(severity, title, message, exception, logMessage);
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

        /// <summary>InfoBar 的档位 → TipToast 的档位。一一对应；TipToast 不引用 WinUI 控件类型，方便单独搬走。</summary>
        private static TipToastSeverity ToTipToastSeverity(InfoBarSeverity severity) => severity switch
        {
            InfoBarSeverity.Warning => TipToastSeverity.Warning,
            InfoBarSeverity.Error => TipToastSeverity.Error,
            InfoBarSeverity.Success => TipToastSeverity.Success,
            _ => TipToastSeverity.Info
        };

        /// <summary>
        /// 记日志 + 报错时复制到剪贴板。轻提示与持久提示两条路共用 ——
        /// 免得换了样式之后"日志却没记"，那种缺口只有出事时才会发现。
        /// </summary>
        private InfoBarSeverity RecordFloatingTip(
            InfoBarSeverity severity,
            string title,
            string message,
            Exception? exception,
            string? logMessage)
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

            return normalizedSeverity;
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
