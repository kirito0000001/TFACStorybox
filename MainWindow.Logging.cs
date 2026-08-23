using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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

        private void AppendLog(LogKind kind, string message, Exception? exception = null)
        {
            if (!ShouldWriteLog(kind))
            {
                return;
            }

            var (displayText, copyText) = CreateLogText(kind, message, exception, DateTime.Now);

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

        private static (string DisplayText, string CopyText) CreateLogText(
            LogKind kind,
            string message,
            Exception? exception,
            DateTime timestamp)
        {
            var header = $"[{timestamp:HH:mm:ss}] LogTFAC: {GetLogKindLabel(kind)}: {message}";
            if (exception is null)
            {
                return (header, header);
            }

            return (
                $"{header}{Environment.NewLine}{FormatExceptionForLog(exception)}",
                $"{header}{Environment.NewLine}{FormatExceptionForLog(exception, stackTraceLineLimit: 8)}");
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
