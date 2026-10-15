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
    (Join-Path $root 'Models\UnrealSyncModels.cs') `
    'UnrealStoryBranchSourceSyncEntry[\s\S]*?UnrealStoryBranchRouteSyncEntry[\s\S]*?StoryBranchesUpdated[\s\S]*?StoryBranchesHash[\s\S]*?StoryBranchesAssetHash' `
    'Unreal sync must model branch sources, routes, verification, and state hashing.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'StoryBranchDataTableAssetPath[\s\S]*?story-branches-v2[\s\S]*?BuildStoryBranchSyncEntries' `
    'Change planning must derive and hash managed branch routes.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'export_data_table_to_json_string[\s\S]*?fill_data_table_from_json_string[\s\S]*?verify_story_branches[\s\S]*?save_loaded_asset_with_retry' `
    'Unreal Python must merge, verify, save, and re-verify AllRoad.'

Require-Pattern `
    (Join-Path $root 'Services\StoryBranchFlowService.cs') `
    'StoryBranchFlowService[\s\S]*?convergenceSection[\s\S]*?explicitTarget\s*\?\?\s*convergenceSection' `
    'AllRoad generation must cover conditional, sequential, and terminal section routes.'

Require-Pattern `
    (Join-Path $root 'Services\StoryBranchFlowService.cs') `
    'explicitTarget\s*\?\?\s*convergenceSection[\s\S]*?explicitTarget is null && convergenceSection is not null' `
    'Unbound choices must conditionally enter the inferred common section.'

Require-Pattern `
    (Join-Path $root 'Models\StoryDocument.cs') `
    'CustomBranchSectionFirstRowIds' `
    'StoryDocument must persist sections whose AllRoad logic stays manually managed.'

Require-Pattern `
    (Join-Path $root 'MainWindow.xaml') `
    'x:Name="StoryCustomBranchLogicToggle"[\s\S]*?Toggled="StoryCustomBranchLogicToggle_Toggled"' `
    'The right story workbench needs a current-section custom AllRoad logic toggle.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Sections.cs') `
    'StoryCustomBranchLogicToggle_Toggled[\s\S]*?SetCustomBranchSection[\s\S]*?QueueCurrentStoryDocumentSave' `
    'Changing custom AllRoad logic must be undoable and persisted.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'expected_route_count[\s\S]*?actual_route_count[\s\S]*?TargetStory=' `
    'AllRoad verification must reject missing or stale extra routes.'

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.Process.cs') `
    'StoryBranchDataTableAssetPath[\s\S]*?filesToBackup\.Add' `
    'AllRoad must be included in the Unreal sync backup.'

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.cs') `
    '!changePlan\.StoryBranchesChanged\s*\|\|\s*report\.StoryBranchesUpdated' `
    'The UI must not record branch sync success without Unreal verification.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1

}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
$pythonPath = 'D:\YuanMa\UnrealEngine-release\Engine\Binaries\ThirdParty\Python3\Win64\python.exe'
if ((Test-Path -LiteralPath $assemblyPath) -and (Test-Path -LiteralPath $pythonPath)) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $serviceType = $assembly.GetType('GalExcleTools.Services.UnrealSyncService', $true)
    $service = [System.Activator]::CreateInstance($serviceType, $true)
    [string]$scriptPath = Join-Path $env:TEMP ('gal-story-branches-' + [guid]::NewGuid().ToString('N') + '.py')
    try {
        $arguments = [object[]]@($scriptPath, [string]'D:\Temp\gal-sync-manifest.json')
        [void]$serviceType.GetMethod('WritePythonScript').Invoke($service, $arguments)
        & $pythonPath -m py_compile $scriptPath
        if ($LASTEXITCODE -ne 0) {
            throw 'Generated Unreal Python failed syntax validation.'
        }
    }
    finally {
        Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
        $cache = Join-Path (Split-Path $scriptPath -Parent) '__pycache__'
        if (Test-Path -LiteralPath $cache) {
            Get-ChildItem -LiteralPath $cache -Filter (([IO.Path]::GetFileNameWithoutExtension($scriptPath)) + '*.pyc') -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }
}

if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $serviceType = $assembly.GetType('GalExcleTools.Services.UnrealSyncService', $true)
    $documentType = $assembly.GetType('GalExcleTools.StoryDocument', $true)
    $rowType = $assembly.GetType('GalExcleTools.StoryRow', $true)
    $routeType = $assembly.GetType('GalExcleTools.StoryChoiceRoute', $true)
    $tableType = $assembly.GetType('GalExcleTools.UnrealStoryTableSyncEntry', $true)
    $service = [System.Activator]::CreateInstance($serviceType, $true)
    $document = [System.Activator]::CreateInstance($documentType, $true)
    $rows = $documentType.GetProperty('Rows').GetValue($document)
    $sections = $documentType.GetProperty('RowSections').GetValue($document)
    $setCell = $rowType.GetMethod('Set')
    $createdRows = @()
    foreach ($section in 1..5) {
        $row = [System.Activator]::CreateInstance($rowType, $true)
        [void]$setCell.Invoke($row, @('Name', $section.ToString()))
        $customValue = if ($section -eq 1) { 'M1-00-Choice1' } else { '' }
        [void]$setCell.Invoke($row, @('Custom', $customValue))
        [void]$rows.Add($row)
        $sections.Add($section.ToString(), $section)
        $createdRows += $row
    }

    $targetIdA = $rowType.GetProperty('StableId').GetValue($createdRows[1])
    $targetIdC = $rowType.GetProperty('StableId').GetValue($createdRows[2])
    $managedRouteA = [System.Activator]::CreateInstance(
        $routeType,
        @('M1-00-Choice1', 0, 'A', $targetIdA, $true))
    $managedRouteC = [System.Activator]::CreateInstance(
        $routeType,
        @('M1-00-Choice1', 2, 'C', $targetIdC, $true))
    $branchRoutes = $documentType.GetProperty('BranchRoutes').GetValue($document)
    $branchRoutes.Add($managedRouteA)
    $branchRoutes.Add($managedRouteC)
    $choiceNotes = $documentType.GetProperty('ChoiceNotes').GetValue($document)
    $choices = $choiceNotes.GetType().GetProperty('Choices').GetValue($choiceNotes)
    $notes = [System.Collections.Generic.List[string]]::new()
    foreach ($text in @('A text', 'B text', 'C text', 'D text')) { [void]$notes.Add($text) }
    $choices.Add('M1-00-Choice1', $notes)
    $customSectionIds = $documentType.GetProperty('CustomBranchSectionFirstRowIds').GetValue($document)
    [void]$customSectionIds.Add($rowType.GetProperty('StableId').GetValue($createdRows[3]))

    $tableListType = [System.Collections.Generic.List``1].MakeGenericType($tableType)
    $tables = [System.Activator]::CreateInstance($tableListType)
    foreach ($section in 1..5) {
        $legacy = [System.Collections.Generic.List[string]]::new()
        $table = [System.Activator]::CreateInstance(
            $tableType,
            @(
                "C:\Temp\TEST-M1-$section.csv",
                "/Game/Story/S$section.S$section",
                '/Script/GALLibrary.StoryStruct',
                $legacy,
                'TEST-M1-00',
                $section))
        [void]$tables.Add($table)
    }

    $builder = $serviceType.GetMethod('BuildChapterStoryBranchSyncEntries')
    if ($null -eq $builder) {
        throw 'BuildChapterStoryBranchSyncEntries is missing.'
    }
    $sources = @($builder.Invoke($service, @('M1', 'TEST-M1-00', $document, $tables)))
    $bySource = @{}
    foreach ($source in $sources) { $bySource[$source.SourceTable] = $source }
    if ($sources.Count -ne 4 -or
        $bySource['/Game/Story/S1.S1'].Routes.Count -ne 4 -or
        !$bySource['/Game/Story/S1.S1'].Routes[0].RequiresChoiceCondition -or
        $bySource['/Game/Story/S1.S1'].Routes[0].TargetTable -ne '/Game/Story/S2.S2' -or
        $bySource['/Game/Story/S1.S1'].Routes[1].OptionKey -ne 'B' -or
        $bySource['/Game/Story/S1.S1'].Routes[1].TargetTable -ne '/Game/Story/S4.S4' -or
        $bySource['/Game/Story/S1.S1'].Routes[2].TargetTable -ne '/Game/Story/S3.S3' -or
        $bySource['/Game/Story/S1.S1'].Routes[3].OptionKey -ne 'D' -or
        $bySource['/Game/Story/S1.S1'].Routes[3].TargetTable -ne '/Game/Story/S4.S4' -or
        $bySource['/Game/Story/S2.S2'].Routes[0].RequiresChoiceCondition -or
        $bySource['/Game/Story/S2.S2'].Routes[0].TargetTable -ne '/Game/Story/S4.S4' -or
        $bySource['/Game/Story/S3.S3'].Routes[0].TargetTable -ne '/Game/Story/S4.S4' -or
        $bySource.ContainsKey('/Game/Story/S4.S4') -or
        $bySource['/Game/Story/S5.S5'].Routes.Count -ne 0) {
        throw 'AllRoad generation did not converge occupied choice sections, skip custom logic, and preserve an empty terminal section.'
    }
}

Write-Host 'Unreal story-branch source checks passed.'
