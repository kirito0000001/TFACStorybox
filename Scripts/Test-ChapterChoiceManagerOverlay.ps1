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
if ($xaml -notmatch 'Text="选项位置"[\s\S]*?Text="选择后进入"[\s\S]*?Text="同步结果"[\s\S]*?x:Name="ChapterChoiceManagerContinuationText"') { $issues.Add('The detail panel must explain start, selection target, sync result, and continuation in reading order.') }
if ($manager -notmatch 'CreateSession[\s\S]*?RefreshChapterChoiceManagerSections[\s\S]*?RefreshChapterChoiceManagerOptions[\s\S]*?RefreshChapterChoiceManagerDetail') { $issues.Add('Overlay must load and refresh one staged chapter session.') }
if ($manager -notmatch 'section\.Flow\.Options\.Count\s*==\s*0[\s\S]*?StorySectionDisplay\.FormatName\(section\.Section\)[\s\S]*?-无选项') { $issues.Add('Sections without choices must show a named no-choice row instead of an empty middle column.') }
if ($manager -notmatch 'GetSectionDisplayName[\s\S]*?ChapterChoiceManagerSectionInfo') { $issues.Add('Section and option rows must use conceptual choice/branch names.') }
# 「自定义跳转逻辑」/「同步方式」那个开关 2026-10-15 封存了（UI 入口收掉，服务层操作留着），
# 所以这里不再要求 overlay 去调 SetCustomSection；钉的仍然是
# "编辑走暂存会话 + 共用同一份流图预览"，外加一条"那个开关不许长回来"。
if ($manager -notmatch 'UpdateOption[\s\S]*?_chapterChoiceManagerSession\.Flow') { $issues.Add('Overlay edits must use staged option operations and the shared flow preview.') }
# 钉"不许使用那个控件"（`控件名.` —— 赋值/读取都长这样），而不是"不许提到这个名字"：
# 处理器的位置留着一段说明注释，里面本来就会写到这个名字。
if ($manager -match 'ChapterChoiceManagerCustomToggle\s*\.') { $issues.Add('The shelved custom-AllRoad switch must not come back into the overlay.') }
if ($manager -notmatch 'VirtualKey\.Escape[\s\S]*?VirtualKey\.Enter[\s\S]*?HideChapterChoiceManager') { $issues.Add('Overlay must support Escape cancel and Enter save.') }

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

Write-Host 'Chapter choice-manager overlay checks passed.'
