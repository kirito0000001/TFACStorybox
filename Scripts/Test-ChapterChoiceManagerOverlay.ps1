$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$xaml = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.xaml')
$chapterCode = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Workbench.Chapters.cs')
$managerPath = Join-Path $root 'MainWindow.Workbench.ChoiceManager.cs'
$manager = if (Test-Path -LiteralPath $managerPath) { Get-Content -Raw -Encoding UTF8 -LiteralPath $managerPath } else { '' }
$issues = [Collections.Generic.List[string]]::new()

if ($chapterCode -notmatch 'CreateMenuItem\("选项与分支"[\s\S]*?ShowChapterChoiceManager') { $issues.Add('Chapter cards need an option-and-branch context command.') }
if ($xaml -notmatch 'x:Name="ChapterChoiceManagerHost"[\s\S]*?x:Name="ChapterChoiceManagerSectionList"[\s\S]*?x:Name="ChapterChoiceManagerOptionList"[\s\S]*?x:Name="ChapterChoiceManagerDetailPanel"') { $issues.Add('Choice manager must provide a three-column modal overlay.') }
if ($xaml -notmatch 'ChapterChoiceManagerSearchBox[\s\S]*?ChapterChoiceManagerIssueText[\s\S]*?ChapterChoiceManagerSaveButton[\s\S]*?ChapterChoiceManagerCancelButton') { $issues.Add('Choice manager needs search, validation, save, and cancel controls.') }
if ($xaml -notmatch 'Text="剧情结构"[\s\S]*?x:Name="ChapterChoiceManagerSectionInfo"') { $issues.Add('The left list must lead with story structure while section metadata stays in the detail panel.') }
if ($manager -notmatch 'CreateSession[\s\S]*?RefreshChapterChoiceManagerSections[\s\S]*?RefreshChapterChoiceManagerOptions[\s\S]*?RefreshChapterChoiceManagerDetail') { $issues.Add('Overlay must load and refresh one staged chapter session.') }
if ($manager -notmatch 'GetSectionDisplayName[\s\S]*?ChapterChoiceManagerSectionInfo') { $issues.Add('Section and option rows must use conceptual choice/branch names.') }
if ($manager -notmatch 'UpdateOption[\s\S]*?SetCustomSection[\s\S]*?_chapterChoiceManagerSession\.Flow') { $issues.Add('Overlay edits must use staged option/custom operations and shared flow preview.') }
if ($manager -notmatch 'VirtualKey\.Escape[\s\S]*?VirtualKey\.Enter[\s\S]*?HideChapterChoiceManager') { $issues.Add('Overlay must support Escape cancel and Enter save.') }

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Chapter choice-manager overlay checks passed.'
