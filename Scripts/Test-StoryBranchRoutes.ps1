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
    'ShowStoryChoiceEditorAsync[\s\S]*?StoryChoiceTargetOption[\s\S]*?进入共通线' `
    'Choice editing must capture optional target sections alongside option text.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.ChoiceEditor.cs') `
    'StoryChoiceEditorResult[\s\S]*?StoryChoiceEditorOption' `
    'Choice editing must return option text and selected targets together.'

Require-Pattern `
    (Join-Path $root 'MainWindow.xaml') `
    'Text="当前分支"[\s\S]*?CurrentBranchItems[\s\S]*?OptionLabel[\s\S]*?SourceText' `
    'The current-story inspector must persistently show the active branch and option content.'

Require-Pattern `
    (Join-Path $root 'Services\StoryBranchService.cs') `
    'RepointSectionAnchorsAfterRowDelete[\s\S]*?route with \{ TargetSectionFirstRowId = replacementId \}[\s\S]*?CustomBranchSectionFirstRowIds\.Remove\(deletedId\)' `
    'Deleting a sentence must repoint the route and custom-AllRoad anchors that pointed at it.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.cs') `
    'DeleteCurrent\(_storyRows, _storyRowSections, _currentStoryRowIndex, removedChoices\)[\s\S]*?RepointSectionAnchorsAfterRowDelete[\s\S]*?QueueCurrentStoryDocumentSave' `
    'The delete flow must repoint section anchors before the document is queued for saving.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
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
        $targets[29].DisplayName -ne '第 29 小节' -or
        $targets[29].FirstSentencePreview -ne '第 30 小节第一句话') {
        throw 'Target sections must grow dynamically without a fixed count limit.'
    }

    # 删除句子后的小节锚点接手：选项目标一律顺延到接替行，
    # 「自定义跳转逻辑」标记只在接替行还留在同一小节里时才跟着挪。
    $repointMethod = $serviceType.GetMethod('RepointSectionAnchorsAfterRowDelete')
    if ($null -eq $repointMethod) {
        Write-Host '(skip anchor reflection: 当前 DLL 还没有 RepointSectionAnchorsAfterRowDelete，重新编译后会做这一项。)'
    }
    else {
        $anchorDocument = [System.Activator]::CreateInstance($documentType, $true)
        $anchorRows = $documentType.GetProperty('Rows').GetValue($anchorDocument)
        $anchorSections = $documentType.GetProperty('RowSections').GetValue($anchorDocument)
        $anchorRowsCreated = @()
        foreach ($entry in @(@('1', 1), @('2', 2), @('3', 2), @('4', 3), @('5', 3))) {
            $row = [System.Activator]::CreateInstance($rowType, $true)
            [void]$setCell.Invoke($row, @('Name', $entry[0]))
            [void]$anchorRows.Add($row)
            $anchorSections.Add($entry[0], $entry[1])
            $anchorRowsCreated += $row
        }

        $anchorIdA = $rowType.GetProperty('StableId').GetValue($anchorRowsCreated[0])
        $anchorIdB = $rowType.GetProperty('StableId').GetValue($anchorRowsCreated[1])
        $anchorIdC = $rowType.GetProperty('StableId').GetValue($anchorRowsCreated[2])
        $anchorIdD = $rowType.GetProperty('StableId').GetValue($anchorRowsCreated[3])
        $anchorIdE = $rowType.GetProperty('StableId').GetValue($anchorRowsCreated[4])
        $anchorRoutes = $documentType.GetProperty('BranchRoutes').GetValue($anchorDocument)
        $anchorCustom = $documentType.GetProperty('CustomBranchSectionFirstRowIds').GetValue($anchorDocument)
        [void]$anchorRoutes.Add([System.Activator]::CreateInstance($routeType, @('M3-00-Choice1', 0, 'A', $anchorIdB, $true)))
        [void]$anchorRoutes.Add([System.Activator]::CreateInstance($routeType, @('M3-00-Choice1', 1, 'B', $anchorIdA, $true)))
        [void]$anchorCustom.Add($anchorIdB)
        [void]$anchorCustom.Add($anchorIdA)

        # 删掉第 2 句（第 2 小节开头），接替的是第 3 句 —— 仍然在第 2 小节里。
        [void]$anchorRows.Remove($anchorRowsCreated[1])
        [void]$anchorSections.Remove('2')
        $changed = $repointMethod.Invoke($null, @($anchorDocument, $anchorIdB, 2, $anchorIdC))
        if (-not $changed -or
            $anchorRoutes[0].TargetSectionFirstRowId -ne $anchorIdC -or
            $anchorCustom.Contains($anchorIdB) -or
            -not $anchorCustom.Contains($anchorIdC)) {
            throw 'Deleting a section anchor must repoint its routes and keep the custom AllRoad flag inside the same section.'
        }

        # 删掉第 1 句（第 1 小节只有这一句），接替行落在第 3 小节：路线顺延，但标记不能跟着串节。
        [void]$anchorRows.Remove($anchorRowsCreated[0])
        [void]$anchorSections.Remove('1')
        $changed = $repointMethod.Invoke($null, @($anchorDocument, $anchorIdA, 1, $anchorIdD))
        if (-not $changed -or
            $anchorRoutes[1].TargetSectionFirstRowId -ne $anchorIdD -or
            $anchorCustom.Contains($anchorIdA) -or
            $anchorCustom.Contains($anchorIdD)) {
            throw 'A vanished section must not hand its manual AllRoad flag to the next section.'
        }

        if ($repointMethod.Invoke($null, @($anchorDocument, $anchorIdE, 3, $null))) {
            throw 'Deleting a sentence nobody points at must not report anchor changes.'
        }
    }
}

Write-Host 'Story branch-route source checks passed.'
