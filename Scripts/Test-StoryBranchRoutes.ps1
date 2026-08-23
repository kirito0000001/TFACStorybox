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
    (Join-Path $root 'Models\StoryModels.cs') `
    'StoryChoiceRoute[\s\S]*?ChoiceFunction[\s\S]*?OptionKey[\s\S]*?TargetSectionFirstRowId' `
    'Choice metadata must persist stable option-to-section routes.'

Require-Pattern `
    (Join-Path $root 'Services\StoryDocumentRepository.cs') `
    'SchemaVersion\s*=\s*3[\s\S]*?BranchRoutes[\s\S]*?CustomBranchSectionFirstRowIds' `
    'StoryDocument schema 3 must persist branch routes and custom AllRoad sections.'

Require-Pattern `
    (Join-Path $root 'Models\StoryDocument.cs') `
    'BranchRoutes[\s\S]*?BeforeBranchRoutes[\s\S]*?AfterBranchRoutes' `
    'Branch-route edits must participate in compact undo and redo.'

Require-Pattern `
    (Join-Path $root 'Services\StoryBranchService.cs') `
    'GetOptionKey[\s\S]*?GetSectionFirstRowId[\s\S]*?GetRoutesForSection' `
    'Branch resolution must live in a focused reusable service.'

Require-Pattern `
    (Join-Path $root 'Services\StoryBranchService.cs') `
    'GetTargetOptions[\s\S]*?Distinct\(\)[\s\S]*?OrderBy[\s\S]*?FirstSentencePreview' `
    'Branch targets must be generated from every existing section with its first sentence.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.ChoiceEditor.cs') `
    'ShowStoryChoiceEditorAsync[\s\S]*?StoryChoiceTargetOption[\s\S]*?不跳转' `
    'Choice editing must capture optional target sections alongside option text.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.ChoiceEditor.cs') `
    'StoryChoiceEditorResult[\s\S]*?StoryChoiceEditorOption' `
    'Choice editing must return option text and selected targets together.'

Require-Pattern `
    (Join-Path $root 'MainWindow.xaml') `
    'Text="当前分支"[\s\S]*?CurrentBranchItems[\s\S]*?OptionLabel[\s\S]*?SourceText' `
    'The current-story inspector must persistently show the active branch and option content.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $documentType = $assembly.GetType('GalExcleTools.StoryDocument', $true)
    $rowType = $assembly.GetType('GalExcleTools.StoryRow', $true)
    $routeType = $assembly.GetType('GalExcleTools.StoryChoiceRoute', $true)
    $serviceType = $assembly.GetType('GalExcleTools.Services.StoryBranchService', $true)
    $document = [System.Activator]::CreateInstance($documentType, $true)
    $rows = $documentType.GetProperty('Rows').GetValue($document)
    $sections = $documentType.GetProperty('RowSections').GetValue($document)
    $setCell = $rowType.GetMethod('Set')
    $createdRows = @()
    foreach ($entry in @(@('1', 1), @('2', 2), @('3', 2))) {
        $row = [System.Activator]::CreateInstance($rowType, $true)
        [void]$setCell.Invoke($row, @('Name', $entry[0]))
        [void]$rows.Add($row)
        $sections.Add($entry[0], $entry[1])
        $createdRows += $row
    }

    $choiceNotes = $documentType.GetProperty('ChoiceNotes').GetValue($document)
    $choices = $choiceNotes.GetType().GetProperty('Choices').GetValue($choiceNotes)
    $notes = [System.Collections.Generic.List[string]]::new()
    $notes.Add('选项内容 A')
    $choices.Add('M2-00-Choice1', $notes)
    $beforeSnapshot = $documentType.GetMethod('CaptureSnapshot').Invoke($document, @(1))
    $targetId = $rowType.GetProperty('StableId').GetValue($createdRows[1])
    $route = [System.Activator]::CreateInstance(
        $routeType,
        @('M2-00-Choice1', 0, 'A', $targetId, $true))
    $routes = $documentType.GetProperty('BranchRoutes').GetValue($document)
    [void]$routes.Add($route)
    $customSections = $documentType.GetProperty('CustomBranchSectionFirstRowIds').GetValue($document)
    [void]$customSections.Add($targetId)
    $afterSnapshot = $documentType.GetMethod('CaptureSnapshot').Invoke($document, @(1))
    $changeSet = $documentType.GetMethod('CreateChangeSet').Invoke(
        $null,
        @('设置选项目标', $beforeSnapshot, $afterSnapshot))

    $optionKey = $serviceType.GetMethod('GetOptionKey').Invoke($null, @(26))
    $resolved = @($serviceType.GetMethod('GetRoutesForSection').Invoke($null, @($document, 2)))
    if ($optionKey -ne 'AA' -or $resolved.Count -ne 1 -or
        $resolved[0].OptionLabel -ne '选项 A' -or
        $resolved[0].Content -ne '选项内容 A') {
        throw 'Stable branch resolution returned the wrong option label or content.'
    }

    [void]$documentType.GetMethod('ApplyChange').Invoke($document, @($changeSet, $true, 1))
    if ($routes.Count -ne 0 -or $customSections.Count -ne 0) {
        throw 'Undo must remove the branch route and custom AllRoad marker created by the edit transaction.'
    }
    [void]$documentType.GetMethod('ApplyChange').Invoke($document, @($changeSet, $false, 1))
    if ($routes.Count -ne 1 -or $customSections.Count -ne 1) {
        throw 'Redo must restore the branch route and custom AllRoad marker created by the edit transaction.'
    }

    $largeDocument = [System.Activator]::CreateInstance($documentType, $true)
    $largeRows = $documentType.GetProperty('Rows').GetValue($largeDocument)
    $largeSections = $documentType.GetProperty('RowSections').GetValue($largeDocument)
    for ($section = 1; $section -le 30; $section++) {
        $row = [System.Activator]::CreateInstance($rowType, $true)
        [void]$setCell.Invoke($row, @('Name', $section.ToString()))
        [void]$setCell.Invoke($row, @('Tesxt', "第 $section 小节第一句话"))
        [void]$largeRows.Add($row)
        $largeSections.Add($section.ToString(), $section)
    }
    $targets = @($serviceType.GetMethod('GetTargetOptions').Invoke($null, @($largeDocument)))
    if ($targets.Count -ne 30 -or
        $targets[29].DisplayName -ne '第 30 小节' -or
        $targets[29].FirstSentencePreview -ne '第 30 小节第一句话') {
        throw 'Target sections must grow dynamically without a fixed count limit.'
    }
}

Write-Host 'Story branch-route source checks passed.'
