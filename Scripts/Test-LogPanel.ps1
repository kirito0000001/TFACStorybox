$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern([string]$path, [string]$pattern, [string]$message) {
    $fullPath = Join-Path $root $path
    if (-not (Test-Path -LiteralPath $fullPath) -or
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $fullPath) -notmatch $pattern) {
        $issues.Add($message)
    }
}

Require-Pattern 'MainWindow.Logging.cs' 'Queue<\(LogKind Kind, string DisplayText, string CopyText\)>' 'Log needs a bounded structured queue.'
# 【2026-10-12 口径变更】日志行统一走 RuntimeLogFormat（照零境那边搬的 run/step 口径），
# 早先那个 `LogTFAC: {等级}` 前缀**是故意去掉的** —— 面板上等级由颜色表达
# （见 GetLogTextStyle），前面再叠一段文字只会把正文挤到很远。
# 所以这里不再钉那个前缀，改钉这个分工：
#   · 面板行带 run/step 前缀、不带等级文字；
#   · "复制出去给别人看"那行才把等级写成文字（复制走之后颜色就没了）。
# 原来的意图（"每条日志都看得出是哪一次、哪一步"）由 run/step 接手了。
Require-Pattern 'Services\RuntimeLogFormat.cs' 'FormatPanelLine\(DateTimeOffset now, RuntimeLogScope scope, string body\)[\s\S]*?FormatCopyLine\([\s\S]*?string level[\s\S]*?string body\)' 'The panel line must carry run/step without a level word; only the copied line spells the level out.'
Require-Pattern 'Services\RuntimeLogFormat.cs' 'run=\{scope\.RunId\} step=\{scope\.Step\}' 'Copied log lines must state which run and which step they belong to.'
Require-Pattern 'MainWindow.Logging.cs' 'GetLogTextStyle\(kind\)[\s\S]*?LogErrorBlockStyle' 'The log panel must still convey the entry level - through color, not a text prefix.'
Require-Pattern 'MainWindow.Logging.cs' 'CreateLogText[\s\S]*?RuntimeLogFormat\.FormatPanelLine[\s\S]*?RuntimeLogFormat\.FormatCopyLine[\s\S]*?GetLogKindLabel\(kind\)' 'All log consumers need one shared formatter.'
Require-Pattern 'MainWindow.Logging.cs' 'FormatExceptionForLog[\s\S]*?InnerException[\s\S]*?StackTrace' 'Log exceptions need inner exceptions and stack traces.'
Require-Pattern 'MainWindow.Logging.cs' 'CreateLogText[\s\S]*?FormatExceptionForLog[\s\S]*?stackTraceLineLimit' 'Panel and copied exception text must share the same formatter.'
Require-Pattern 'MainWindow.Logging.cs' 'TrimLogTextForClipboard[\s\S]*?12000' 'Single-entry clipboard text needs a safe length limit.'
Require-Pattern 'MainWindow.Logging.cs' 'LogBlock_Tapped[\s\S]*?CopyTextToClipboard' 'Clicking a log block must copy that entry.'
Require-Pattern 'MainWindow.Logging.cs' 'CopyAllLogButton_Click[\s\S]*?_logLines' 'The panel needs copy-all support.'
Require-Pattern 'MainWindow.Logging.cs' 'maxLogCount\s*=\s*300' 'Log history must stay bounded at 300 entries.'
Require-Pattern 'MainWindow.Logging.cs' 'logWheelScrollPixelsPerNotch\s*=\s*150' 'Log wheel scrolling must use the shared 150-pixel step.'

Require-Pattern 'App.xaml' 'x:Key="Light"[\s\S]*?LogDefaultForegroundBrush[\s\S]*?LogWarningForegroundBrush[\s\S]*?LogErrorForegroundBrush' 'Light theme needs distinct log colors.'
Require-Pattern 'App.xaml' 'x:Key="Dark"[\s\S]*?LogDefaultForegroundBrush[\s\S]*?LogWarningForegroundBrush[\s\S]*?LogErrorForegroundBrush' 'Dark theme needs distinct log colors.'
Require-Pattern 'App.xaml' 'x:Key="LogDefaultBlockStyle"[\s\S]*?x:Key="LogUserBlockStyle"[\s\S]*?x:Key="LogWarningBlockStyle"[\s\S]*?x:Key="LogErrorBlockStyle"' 'Log block styles are incomplete.'

Require-Pattern 'MainWindow.xaml' 'Click="ScrollLogToBottomButton_Click"' 'Log panel needs a scroll-to-bottom command.'
Require-Pattern 'MainWindow.xaml' 'Click="CopyAllLogButton_Click"' 'Log panel needs a copy-all command.'
Require-Pattern 'MainWindow.xaml' 'PointerWheelChanged="LogScrollViewer_PointerWheelChanged"' 'Log panel needs deterministic wheel handling.'
Require-Pattern 'MainWindow.xaml' 'HorizontalScrollBarVisibility="Auto"' 'Detailed exception lines need horizontal access.'

Require-Pattern 'ViewModels\SettingsViewModel.cs' 'ShouldWriteLog[\s\S]*?LogUserOperations[\s\S]*?LogWarnings[\s\S]*?LogErrors' 'Existing per-kind log filtering must remain active.'
Require-Pattern 'MainWindow.Tips.cs' 'CopyErrorLogToClipboard[\s\S]*?CreateLogText\(LogKind\.Error' 'Automatic error-tip copies must use the same shared log format.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

Write-Host 'Log panel checks passed.'
