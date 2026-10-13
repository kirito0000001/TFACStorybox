using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private readonly Queue<(LogKind Kind, string DisplayText, string CopyText)> _logLines = new();

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logLines.Clear();
            LogItemsControl.Items.Clear();
            AppendLog(LogKind.User, "已清空输出日志。");
        }

        private void CopyAllLogButton_Click(object sender, RoutedEventArgs e)
        {
            var text = string.Join(Environment.NewLine + Environment.NewLine, _logLines.Select(item => item.CopyText));
            CopyTextToClipboard(text);
            ShowFloatingTip(InfoBarSeverity.Success, "已复制全部日志", $"{_logLines.Count} 条记录");
        }

        private void ScrollLogToBottomButton_Click(object sender, RoutedEventArgs e)
        {
            ScrollLogToBottom();
        }

        private void LogScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(LogScrollViewer).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            const double logWheelScrollPixelsPerNotch = 150;
            var notchCount = delta / 120d;
            var targetOffset = Math.Clamp(
                LogScrollViewer.VerticalOffset - notchCount * logWheelScrollPixelsPerNotch,
                0,
                LogScrollViewer.ScrollableHeight);
            LogScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
            e.Handled = true;
        }

        private bool ShouldWriteLog(LogKind kind)
        {
            return Settings.ShouldWriteLog(kind);
        }

        /// <summary>
        /// 当前日志范围（哪一次流程、哪一步）。
        ///
        /// 为什么要它：同步一跑就是几分钟、几十条日志，而没有批次号与步号的时候，
        /// "这次报的错"和"上次留下的错"在面板上长得一样 —— 事后翻日志分不出谁是谁。
        /// 2026-10-12 就是撞在这上面：同步报了个错，日志里那条看不出是哪一段出的。
        /// </summary>
        private RuntimeLogScope _logScope = RuntimeLogScope.None;

        /// <summary>开一次新的日志批次，返回批次号。首行是一条显眼的批次头。</summary>
        private string BeginLogRun(string title)
        {
            var runId = RuntimeLogFormat.CreateRunId(DateTimeOffset.Now);
            _logScope = new RuntimeLogScope(runId, 0);
            AppendLog(LogKind.User, RuntimeLogFormat.FormatRunHeader(runId, title));
            return runId;
        }

        /// <summary>切到某一步。传 0 表示"不在任何一步里"。</summary>
        private void SetLogStep(int step) => _logScope = _logScope with { Step = step };

        /// <summary>
        /// 记一行"进入第 N 步"。
        /// 上一步没显式收尾的话这里替它收一下 —— 不然日志里会出现"进了第 3 步"
        /// 却找不到"第 2 步结束"的情况，读起来像漏了东西。
        /// </summary>
        private void EnterLogStep(int step, string stepName)
        {
            if (_logScope.Step == step)
            {
                return;
            }

            if (_logScope.Step > 0)
            {
                AppendLog(LogKind.User, RuntimeLogFormat.FormatStepEnd(_logScope.Step, string.Empty));
            }

            SetLogStep(step);
            AppendLog(LogKind.User, RuntimeLogFormat.FormatStepStart(step, stepName));
        }

        /// <summary>给当前这一步写个结论并收尾。</summary>
        private void EndLogStep(string conclusion)
        {
            if (_logScope.Step <= 0)
            {
                return;
            }

            AppendLog(LogKind.User, RuntimeLogFormat.FormatStepEnd(_logScope.Step, conclusion));
            SetLogStep(0);
        }

        /// <summary>
        /// 日志文件路径（<c>runtime.log</c>，和零境同名便于对照）。
        ///
        /// 放在 LocalApplicationData 而不是 exe 旁边：exe 可能被安装到只读位置，
        /// 而日志是"出了事必须写得进去"的东西。
        /// </summary>
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GalExcleTools",
            "Logs",
            RuntimeLogFile.FileName);

        private bool _logFilePrepared;

        /// <summary>
        /// 这一次会话第一次写日志之前，先按零境的规则轮转一次
        /// （超过 8MB 或跨天就把旧的归档成 runtime-YYYYMMDD.log，只留 5 份）。
        /// 放在"第一次写"而不是构造函数里：构造函数里做的事越少越好，
        /// 而且那时候可能还没到该轮转的时候。
        /// </summary>
        private void PrepareLogFileOnce()
        {
            if (_logFilePrepared)
            {
                return;
            }

            _logFilePrepared = true;
            try
            {
                RuntimeLogFile.RotateIfNeeded(LogFilePath, DateTimeOffset.Now);
            }
            catch (Exception)
            {
                // 轮转失败不该挡住写日志 —— 大不了这份文件继续变大。
            }
        }

        /// <summary>打开日志文件夹。出问题时把那个 runtime.log 发出去就够定位了。</summary>
        private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
                AppendLog(LogKind.User, $"已打开日志文件夹：{directory}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "打开日志文件夹失败。", ex);
                ShowFloatingTip(InfoBarSeverity.Error, "打开日志文件夹失败", ex.Message, ex);
            }
        }

        private void AppendLog(LogKind kind, string message, Exception? exception = null)
        {
            // ⚠️ **这个方法可能从任何线程被调进来**：进程轮询循环、远程任务回调、
            // 在线/离线选路的通知……而下面要动界面（Items.Clear / Add）。
            //
            // 不在这里是 UI 线程就直接排队回去 —— 否则轻则 0x8001010E（RPC_E_WRONG_THREAD）、
            // 重则界面状态乱掉。2026-10-13 实测：`onUsedOffline` 那条回调就炸在这儿，
            // 而它抛出来的异常 Message 是**空的**（COM 异常常常没有消息文本），
            // 于是外层只报出一句"虚幻同步失败。"，什么都看不出来 ——
            // 查了半天才发现是"回调跑在后台线程"。
            //
            // 与其在每个调用点各 marshal 一遍（漏一个就是这次这种事），
            // 不如在这里统一兜住 —— **日志方法本来就该能从任何线程调**。
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => AppendLog(kind, message, exception));
                return;
            }

            var (displayText, copyText) = CreateLogText(kind, message, exception, DateTimeOffset.Now);

            // ⚠️ 顺序很要紧：**先写文件，再看面板开关**。
            // 文件写的是全量 —— 面板按等级过滤是"给人看的取舍"，
            // 文件是"出了事能查的证据"。被过滤掉的那条在文件里必须还在，
            // 否则"提示一闪没看清、回头也查不到"那个毛病就还在（2026-10-12 就是这个）。
            PrepareLogFileOnce();
            RuntimeLogFile.AppendLine(LogFilePath, copyText);

            if (!ShouldWriteLog(kind))
            {
                return;
            }

            _logLines.Enqueue((kind, displayText, copyText));

            const int maxLogCount = 300;
            while (_logLines.Count > maxLogCount)
            {
                _logLines.Dequeue();
            }

            RenderLogItems();
        }

        private void RenderLogItems()
        {
            if (LogItemsControl is null || LogScrollViewer is null)
            {
                return;
            }

            LogItemsControl.Items.Clear();
            foreach (var (kind, displayText, copyText) in _logLines)
            {
                LogItemsControl.Items.Add(CreateLogBlock(kind, displayText, copyText));
            }

            ScrollLogToBottom();
        }

        private void ScrollLogToBottom()
        {
            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        }

        private Border CreateLogBlock(LogKind kind, string displayText, string copyText)
        {
            var block = new TextBlock
            {
                Text = displayText,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                IsTextSelectionEnabled = false,
                Style = GetLogTextStyle(kind)
            };

            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Child = block,
                Tag = copyText,
                Style = GetLogBlockStyle(kind)
            };
            border.Tapped += LogBlock_Tapped;
            return border;
        }

        private void LogBlock_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Border { Tag: string text })
            {
                CopyTextToClipboard(TrimLogTextForClipboard(text));
                ShowFloatingTip(InfoBarSeverity.Success, "已复制日志", "已复制这一条日志记录。");
                e.Handled = true;
            }
        }

        private static string TrimLogTextForClipboard(string text)
        {
            const int maxClipboardLogLength = 12000;
            if (text.Length <= maxClipboardLogLength)
            {
                return text;
            }

            return text[..maxClipboardLogLength] +
                $"{Environment.NewLine}... log trimmed for clipboard ({text.Length - maxClipboardLogLength} more characters in UI)";
        }

        private static void CopyTextToClipboard(string text, bool flush = false)
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            if (flush)
            {
                Clipboard.Flush();
            }
        }

        private (string DisplayText, string CopyText) CreateLogText(
            LogKind kind,
            string message,
            Exception? exception,
            DateTimeOffset timestamp)
        {
            // ⚠️ 面板行与复制行**必须走同一个格式函数**。
            // 以前这两行是在这里各拼一遍的（还各自嵌了一遍时间戳），
            // 于是改一处就会两边不一致；而且那时候**没有"哪一次、哪一步"** ——
            // 报错之后翻日志分不出是在哪一段出的。这一段照零境那边搬。
            var header = RuntimeLogFormat.FormatPanelLine(timestamp, _logScope, message);
            var copyHeader = RuntimeLogFormat.FormatCopyLine(
                timestamp,
                _logScope,
                GetLogKindLabel(kind),
                message);

            if (exception is null)
            {
                return (header, copyHeader);
            }

            // 面板上限制堆栈行数（太长会把日志挤爆）；复制出去的那份给足 8 行 ——
            // 那一份是拿去定位问题的，宁可长一点。
            return (
                $"{header}{Environment.NewLine}{FormatExceptionForLog(exception)}",
                $"{copyHeader}{Environment.NewLine}{FormatExceptionForLog(exception, stackTraceLineLimit: 8)}");
        }

        private static string GetLogKindLabel(LogKind kind)
        {
            return kind switch
            {
                LogKind.User => "User",
                LogKind.Warning => "Warning",
                LogKind.Error => "Error",
                _ => "Log"
            };
        }

        private static string FormatExceptionForLog(Exception exception, int? stackTraceLineLimit = null)
        {
            var lines = new List<string>();
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                var message = string.IsNullOrWhiteSpace(current.Message) ? "<empty message>" : current.Message;
                var hResult = current.HResult == 0 ? string.Empty : $" HRESULT=0x{current.HResult:X8}";
                lines.Add($"    Exception={current.GetType().FullName}{hResult} Message={message}");
                if (current is COMException comException)
                {
                    lines.Add($"    COMErrorCode=0x{comException.ErrorCode:X8}");
                }
            }

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                lines.Add("    StackTrace:");
                var stackTraceLines = exception.StackTrace
                    .Split(Environment.NewLine)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => $"        {line.Trim()}")
                    .ToList();
                var emittedStackTraceLines = stackTraceLineLimit is null
                    ? stackTraceLines
                    : stackTraceLines.Take(stackTraceLineLimit.Value).ToList();
                lines.AddRange(emittedStackTraceLines);
                if (stackTraceLineLimit is not null && stackTraceLines.Count > stackTraceLineLimit.Value)
                {
                    lines.Add($"        ... stack trace trimmed for clipboard ({stackTraceLines.Count - stackTraceLineLimit.Value} more lines in UI)");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static Style GetLogTextStyle(LogKind kind)
        {
            return Application.Current.Resources[kind switch
            {
                LogKind.User => "LogUserTextStyle",
                LogKind.Warning => "LogWarningTextStyle",
                LogKind.Error => "LogErrorTextStyle",
                _ => "LogDefaultTextStyle"
            }] as Style ?? throw new InvalidOperationException("日志文字样式资源不可用。");
        }

        private static Style GetLogBlockStyle(LogKind kind)
        {
            return Application.Current.Resources[kind switch
            {
                LogKind.User => "LogUserBlockStyle",
                LogKind.Warning => "LogWarningBlockStyle",
                LogKind.Error => "LogErrorBlockStyle",
                _ => "LogDefaultBlockStyle"
            }] as Style ?? throw new InvalidOperationException("日志容器样式资源不可用。");
        }
    }
}
