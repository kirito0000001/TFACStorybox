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
Require-Pattern 'MainWindow.Logging.cs' 'LogTFAC:\s*\{GetLogKindLabel\(kind\)\}' 'Log entries need the TFAC source prefix.'
Require-Pattern 'MainWindow.Logging.cs' 'FormatExceptionForLog[\s\S]*?InnerException[\s\S]*?StackTrace' 'Log exceptions need inner exceptions and stack traces.'
Require-Pattern 'MainWindow.Logging.cs' 'CreateLogText[\s\S]*?LogTFAC[\s\S]*?FormatExceptionForLog' 'All log consumers need one shared formatter.'
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
Require-Pattern 'MainWindow.Tips.cs' 'CopyErrorLogToClipboard[\s\S]*?CreateLogText\(LogKind\.Error' 'Automatic error-tip copies must use the same TFAC log format.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Log panel checks passed.'
