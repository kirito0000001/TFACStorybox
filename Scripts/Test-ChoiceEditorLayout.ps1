$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'Views\StoryDialogContentFactory.cs'
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $path
$issues = [System.Collections.Generic.List[string]]::new()

function Require([string]$pattern, [string]$message) {
    if ($source -notmatch $pattern) {
        $issues.Add($message)
    }
}

Require 'ChoiceFunctionNoteDialogContent[\s\S]*?_notesPanel\s*=\s*new StackPanel[\s\S]*?Width\s*=\s*560[\s\S]*?var content = new Grid[\s\S]*?Width\s*=\s*560' 'Choice editor and its scroll content must share a constrained width.'
Require 'CreateColumnHeader[\s\S]*?选项内容[\s\S]*?前往小节[\s\S]*?操作' 'Choice editor needs one clear shared column header.'
Require 'ComboBoxItem\s*\{\s*Content\s*=\s*"不跳转"' 'The empty branch target must clearly say that it does not jump.'
Require 'ScrollViewer[\s\S]*?MaxHeight\s*=\s*420[\s\S]*?_notesPanel' 'Only the option list should scroll when it grows.'
Require 'optionsScroll[\s\S]*?HorizontalContentAlignment\s*=\s*HorizontalAlignment\.Stretch' 'The option list must stretch rows inside the viewport instead of measuring them infinitely wide.'
Require 'SymbolIcon\(Symbol\.Delete\)[\s\S]*?ToolTipService\.SetToolTip' 'Delete must use a compact icon button with a tooltip.'
Require 'SymbolIcon\(Symbol\.Add\)[\s\S]*?添加选项' 'Adding an option should use an icon-and-text command.'
Require 'UpdateSummary[\s\S]*?个选项[\s\S]*?个目标' 'The footer should summarize option and target counts.'
Require 'CreateOptionRow[\s\S]*?CornerRadius\s*=\s*new CornerRadius\(6\)' 'Each option should use one restrained repeated-item surface.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Choice editor layout checks passed.'
