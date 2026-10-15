$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern([string]$path, [string]$pattern, [string]$message) {
    if (-not (Test-Path -LiteralPath $path) -or
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $path) -notmatch $pattern) {
        $issues.Add($message)
    }
}

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Preview.cs') `
    '背景切换模式[\s\S]*?ShowPersistentStoryStatus|ShowPersistentStoryStatus[\s\S]*?背景切换模式' `
    'Applied background-transition status must use the persistent story-status path.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Sync.cs') `
    'ShowPersistentStoryStatus[\s\S]*?CreateFloatingTipBar[\s\S]*?PersistentStoryStatusTag[\s\S]*?AddFloatingTipWithEntrance' `
    'Persistent story status needs a closable InfoBar without the normal auto-dismiss path.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.cs') `
    'LoadStoryRowIntoUi[\s\S]*?RefreshCurrentStoryPersistentStatus\(\)[\s\S]*?RefreshStoryPreviewAsync' `
    'Changing the active story row must clear the previous row status before refreshing the preview.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Tips.cs') `
    'TrimFloatingTipsBeforeAdd[\s\S]*?PersistentStoryStatusTag' `
    'Ordinary floating tips must not evict the current-row persistent status.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Media.cs') `
    'RefreshCurrentStoryPersistentStatus[\s\S]*?ClearStoryTransientTips\(\)[\s\S]*?TryParseBackgroundTransitionMode[\s\S]*?ShowPersistentStoryStatus' `
    'Refreshing current-row functions must replace the old persistent transition status.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Media.cs') `
    'UpdateStoryFunctionInspector[\s\S]*?TryParseBackgroundTransitionMode[\s\S]*?functionNames\.Add\(function\)[\s\S]*?continue;' `
    'The right-side function list must include the explicit BGLerpMode function code.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Functions.cs') `
    'RefreshCurrentStoryFunctionStateAfterEdit[\s\S]*?RebuildStoryPersistentFunctionState[\s\S]*?UpdateStoryToolbarCurrentInfo[\s\S]*?RefreshCurrentStoryPersistentStatus' `
    'Function edits need one shared state-and-status refresh path.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Functions.cs') `
    'ClearStoryFunction\(\)[\s\S]*?RefreshCurrentStoryFunctionStateAfterEdit[\s\S]*?ChooseStoryFunctionAsync\(\)[\s\S]*?RefreshCurrentStoryFunctionStateAfterEdit[\s\S]*?RemoveStoryFunctionAsync\(\)[\s\S]*?RefreshCurrentStoryFunctionStateAfterEdit' `
    'Adding, removing, and clearing functions must all refresh persistent status.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.cs') `
    'NavigatePreviousStoryRow[\s\S]*?NavigateToStoryRow\([^;]*?showInheritedTransitionStatus:\s*false\)' `
    'Previous-row debugging must suppress inherited forward-only transition status.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.cs') `
    'LoadStoryRowIntoUi\([^)]*showInheritedTransitionStatus[\s\S]*?RefreshCurrentStoryPersistentStatus\(\)[\s\S]*?RefreshStoryPreviewAsync\(showInheritedTransitionStatus\)' `
    'Story-row loading must always show explicit row functions while passing inherited transition visibility to the preview.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Preview.cs') `
    'SetStoryBackgroundAsync\([^)]*showInheritedTransitionStatus[\s\S]*?if\s*\(showInheritedTransitionStatus\s*&&[\s\S]*?ShowPersistentStoryStatus' `
    'Background preview must not emit inherited transition status when backward navigation suppresses it.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1

}

Write-Host 'Story persistent-status checks passed.'
