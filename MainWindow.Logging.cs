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

        /// <summary>
        /// 「这次运行临时打开日志面板」的标记。
        ///
        /// 只活在内存里，**不写 settings**：用户平时把日志关掉是为了省地方，
        /// 自动打开如果落了盘，下次启动还是开着的 —— 等于把人的选择悄悄改掉。
        /// </summary>
        private bool _autoLogPanelVisible;

        /// <summary>
        /// 报错时临时把日志面板打开（本次运行有效）。
        ///
        /// 队列里已经攒着这之前的所有条目（含被过滤掉的 Info/User），
        /// 所以面板一打开，「复制全部」拿到的是**完整现场**，不是孤零零一条错误。
        /// </summary>
        private void EnableLogPanelForSession()
        {
            if (_autoLogPanelVisible)
            {
                return;
            }

            _autoLogPanelVisible = true;
            if (LogPanelBorder is not null)
            {
                LogPanelBorder.Visibility = Visibility.Visible;
            }

            RenderLogItems();

            // 注意：这里**不传 logMessage** —— 传了会再走一遍 AppendLog，
            // 虽然不会死循环（只有 Error 才触发本方法），但会往日志里插一条噪音。
            ShowFloatingTip(
                InfoBarSeverity.Warning,
                "已自动打开日志",
                "检测到报错，这次运行的日志已经留在下面的面板里（含报错之前那几条）。点「复制全部」就能发出来；重启后恢复你的日志设置。");
        }

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

        /// <summary>
        /// 剧情编辑器热路径的耗时探针：只在超过阈值时写一行日志。
        ///
        /// 【为什么要它】"哪一段慢"读代码读不出来 —— 每一段的静态开销单看都很小
        ///（角色表 0.62 ms/次、目录枚举 0.04 ms、单张大图首次解码 50–95 ms），
        /// 卡顿是这些叠出来的，得让程序在**真实交互**里自己说。
        /// 阈值 50 ms：正常一次刷新只有几毫秒，超过了才值得记一行。
        /// 有了它，下次"卡"的时候日志里直接就有 [性能] 那几行，不用再猜。
        /// </summary>
        private const int StoryHotPathLogThresholdMs = 50;

        private void LogStoryHotPath(string label, Stopwatch stopwatch)
        {
            if (stopwatch.ElapsedMilliseconds < StoryHotPathLogThresholdMs)
            {
                return;
            }

            AppendLog(LogKind.Info, $"[性能] {label}：{stopwatch.ElapsedMilliseconds} ms");
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

            // ⚠️ 队列**永远收**（环形 300 条），不再由开关决定要不要留。
            //
            // 【为什么】日志开关关着时，ShouldWriteLog 把 Info/User 一起拦掉，
            // 内存里一条都不剩 —— 而报错时最要紧的恰恰是"报错之前那几条"。
            // 文件那边一直全量写，可面板拿不到上下文，用户只能看到孤零零一条错误，
            // 想说明"到底出了什么事"无从下手（2026-11-03 用户提的就是这个）。
            //
            // 所以：**收归收，显示归显示**。开关只决定渲染，不该决定要不要留证据。
            _logLines.Enqueue((kind, displayText, copyText));

            const int maxLogCount = 300;
            while (_logLines.Count > maxLogCount)
            {
                _logLines.Dequeue();
            }

            // 报错而且面板没开 → 这次运行里把面板打开，让上下文直接可复制。
            if (kind == LogKind.Error && !Settings.LogEnabled && !_autoLogPanelVisible)
            {
                EnableLogPanelForSession();
                return;
            }

            if (!_autoLogPanelVisible && !ShouldWriteLog(kind))
            {
                return;
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
                // 过滤挪到渲染这一层：队列里留着全部，显示的仍按用户的开关走。
                // 自动打开那一档例外 —— 用户要的是"完整现场"，不再按等级挑。
                if (!_autoLogPanelVisible && !ShouldWriteLog(kind))
                {
                    continue;
                }

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
