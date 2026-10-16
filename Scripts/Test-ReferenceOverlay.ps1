$ErrorActionPreference = 'Stop'

# 参考层（照隔壁零境工具箱移植的 F1/F2）：
#   · F1 上下文帮助（设置页给 Tips 合集，其它页给快捷键大全）
#   · F2 当前章节参考（没打开章节就给提示）
#   · 加速器挂在 RootGrid 上（任何页面都按得动），浮层可 Esc / 右键 / 关闭按钮关
$root = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern([string]$path, [string]$pattern, [string]$message, [switch]$NotMatch) {
    $matched = (Test-Path -LiteralPath $path) -and
        ((Get-Content -Raw -Encoding UTF8 -LiteralPath $path) -match $pattern)
    if ($NotMatch) { $matched = -not $matched }
    if (-not $matched) {
        $issues.Add($message)
    }
}

$overlayPath = Join-Path $root 'MainWindow.ReferenceOverlay.cs'
$xamlPath = Join-Path $root 'MainWindow.xaml'

Require-Pattern $overlayPath `
    'RegisterReferenceOverlayShortcuts[\s\S]*?VirtualKey\.F1[\s\S]*?ScopeOwner = RootGrid[\s\S]*?VirtualKey\.F2[\s\S]*?ScopeOwner = RootGrid' `
    'F1 and F2 must be window-level accelerators scoped to RootGrid (so they work on every page).'

Require-Pattern (Join-Path $root 'MainWindow.xaml.cs') `
    'RegisterReferenceOverlayShortcuts\(\);' `
    'The reference-layer accelerators must be registered at startup.'

# F1 的上下文口径：设置页给 Tips，其它页给快捷键大全。
Require-Pattern $overlayPath `
    'ShowShortcutGuideOverlay\(\)[\s\S]*?SettingsPage\.Visibility == Visibility\.Visible[\s\S]*?设置页 Tips 合集[\s\S]*?快捷键大全' `
    'F1 must be context sensitive: settings page gets the tips collection, everything else the shortcut guide.'

# F2 的兜底：没打开章节要给一句话，而不是空白浮层。
Require-Pattern $overlayPath `
    'ShowCurrentChapterReferenceOverlay\(\)[\s\S]*?var chapter = _currentStoryChapter;[\s\S]*?chapter is null[\s\S]*?当前没有打开章节' `
    'F2 must explain itself when no chapter is open instead of opening an empty layer.'

# F2 的内容要真从当前文档/备注里取，而不是写死的样板。
Require-Pattern $overlayPath `
    'BuildCurrentChapterReferenceText[\s\S]*?document\.RowSections[\s\S]*?StoryTodoStore[\s\S]*?\.Load\(_currentProject\?\.Path\)' `
    'The chapter reference must be built from the live document and the chapter todo notes.'

# 浮层的关闭方式：Esc / 右键 / 关闭按钮 / 点暗底；点卡片本身不关（选文本时手一抖不该关掉）。
Require-Pattern $overlayPath `
    'ReferenceOverlayHost_KeyDown[\s\S]*?VirtualKey\.Escape[\s\S]*?HideReferenceOverlay\(\)' `
    'Esc must close the reference layer.'
Require-Pattern $overlayPath `
    'ReferenceOverlayHost_RightTapped[\s\S]*?HideReferenceOverlay\(\)' `
    'Right click must close the reference layer.'
Require-Pattern $overlayPath `
    'ReferenceOverlayHost_Tapped[\s\S]*?ReferenceEquals\(e\.OriginalSource, ReferenceOverlayHost\)[\s\S]*?HideReferenceOverlay\(\)' `
    'Clicking the dim backdrop closes the layer, clicking the card itself must not.'

Require-Pattern $xamlPath `
    'x:Name="ReferenceOverlayHost"[\s\S]*?Canvas\.ZIndex="5[6-9]\d"[\s\S]*?Visibility="Collapsed"' `
    'The reference layer must be a top-most collapsed overlay host.'
Require-Pattern $xamlPath `
    'ReferenceOverlayHost[\s\S]*?ReferenceOverlayTitleText[\s\S]*?ReferenceOverlayTextBlock[\s\S]*?IsTextSelectionEnabled="True"' `
    'The reference layer card must carry the title and a selectable text block.'

# F12 是老的入口，现在走同一层；旧的那套英文对话框不该留下。
Require-Pattern (Join-Path $root 'MainWindow.Story.Interaction.cs') `
    'VirtualKey\.F12[\s\S]*?ShowShortcutGuideOverlay\(\)' `
    'F12 must keep working and open the same reference layer.'

# 旧的服务类（内容写死的英文对话框）应当已经删干净 —— 这是全目录扫描，不是单文件。
$leftoverShortcutService = @(
    Get-ChildItem -Path $root -Recurse -File -Include *.cs |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' } |
        Select-String -Pattern 'ShortcutService'
)
if ($leftoverShortcutService.Count -gt 0) {
    $issues.Add("The old standalone shortcut dialog service must stay removed（还有引用：$($leftoverShortcutService[0].Filename):$($leftoverShortcutService[0].LineNumber)）。")
}

# 说明里写到的键，代码里必须真的有处理 —— 这张表是给人照着按的，写错比不写更坏。
Require-Pattern $overlayPath `
    'Tab：清空这一位的立绘' `
    'The guide must still document the Tab shortcut.'
Require-Pattern (Join-Path $root 'MainWindow.Story.Interaction.cs') `
    'VirtualKey\.Tab[\s\S]*?ClearStoryCharacterSlot\(hoveredSlotIndex\)' `
    'The guide documents Tab, so the Tab handler must exist.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

Write-Host 'Reference layer (F1/F2) checks passed.'
