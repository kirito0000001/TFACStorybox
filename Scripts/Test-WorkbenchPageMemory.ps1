$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern([string]$path, [string]$pattern, [string]$message) {
    if (-not (Test-Path -LiteralPath $path) -or
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $path) -notmatch $pattern) {
        $issues.Add($message)
    }
}

function Reject-Pattern([string]$path, [string]$pattern, [string]$message) {
    if ((Test-Path -LiteralPath $path) -and
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $path) -match $pattern) {
        $issues.Add($message)
    }
}

$sessionPath = Join-Path $root 'MainWindow.WorkbenchSession.cs'
$settingsPath = Join-Path $root 'MainWindow.Settings.cs'
$navigationPath = Join-Path $root 'MainWindow.Navigation.cs'

Require-Pattern `
    $sessionPath `
    'enum\s+WorkbenchReturnPage[\s\S]*?Root[\s\S]*?ProjectDetail[\s\S]*?StoryEditor[\s\S]*?ProjectTextTool' `
    'The project module needs an explicit stable-page return state.'

Require-Pattern `
    $sessionPath `
    'PrepareWorkbenchSessionForModuleChangeAsync[\s\S]*?SaveCurrentStoryRow\(\)[\s\S]*?SaveCurrentChapterProgress\(\)[\s\S]*?_storySaveCoordinator\.FlushAsync\(\)[\s\S]*?StopStoryEditorAudio\(\)' `
    'Leaving the story editor must save its row and chapter before stopping the session audio.'

Require-Pattern `
    $sessionPath `
    'RestoreStoryEditorSession[\s\S]*?_currentStoryAssetLibrary\s*=\s*ResolveProjectAssetLibrary[\s\S]*?LoadStoryRowIntoUi\([^)]*\)' `
    'Returning to the story editor must refresh its asset-library binding and active row.'

Reject-Pattern `
    $sessionPath `
    'RestoreStoryEditorSession[\s\S]*?ClearStoryUndoStack\(\)' `
    'Returning to the story editor must preserve its undo stack.'

Require-Pattern `
    $settingsPath `
    'async\s+void\s+ShellNavigation_SelectionChanged[\s\S]*?PrepareWorkbenchSessionForModuleChangeAsync[\s\S]*?RestoreRememberedWorkbenchPage' `
    'Shell navigation must preserve the project session before leaving and restore it on return.'

Require-Pattern `
    $settingsPath `
    'ShellNavigation_SelectionChanged[\s\S]*?navigationRequestId\s*=\s*\+\+_shellNavigationRequestId[\s\S]*?navigationRequestId\s*!=\s*_shellNavigationRequestId' `
    'An older async shell-navigation request must not overwrite a newer selection.'

Require-Pattern `
    $navigationPath `
    'ShowAssetLibraryPage\(\)[\s\S]*?AssetLibraryPage\.Visibility\s*=\s*Visibility\.Visible[\s\S]*?LoadAssetLibraries\(\)' `
    'The asset-library module must continue opening at its root page.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

Write-Host 'Workbench page-memory checks passed.'
