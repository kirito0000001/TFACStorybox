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
    (Join-Path $root 'MainWindow.Story.Functions.cs') `
    'RefreshCurrentStoryFunctionStateAfterEdit[\s\S]*?RebuildStoryPersistentFunctionState[\s\S]*?UpdateStoryToolbarCurrentInfo[\s\S]*?RefreshCurrentStoryPersistentStatus' `
    'Function edits need one shared state-and-status refresh path.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Functions.cs') `
    'ClearStoryFunction\(\)[\s\S]*?RefreshCurrentStoryFunctionStateAfterEdit[\s\S]*?ChooseStoryFunctionAsync\(\)[\s\S]*?RefreshCurrentStoryFunctionStateAfterEdit[\s\S]*?RemoveStoryFunctionAsync\(\)[\s\S]*?RefreshCurrentStoryFunctionStateAfterEdit' `
    'Adding, removing, and clearing functions must all refresh persistent status.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Story persistent-status checks passed.'
