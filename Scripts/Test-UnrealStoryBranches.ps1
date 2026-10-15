$ErrorActionPreference = 'Stop'

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

# 原断言钉的是"报错话术里必须出现 TargetStory=" —— 而 count 那一步早就改成数**裸词**
# TargetStory 了（摊平之后引号和等号都没了，见那段注释里 2026-10-13 的实测值）。
# 改成钉**校验逻辑本身**：期望条数取自清单里的 routes，实际条数从导出的表里数出来，
# 对不上就 add_error + return False（不是只写日志）。
Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'expected_route_count\s*=\s*len\(routes\)[\s\S]*?actual_route_count\s*=\s*matched_text\.count\("TargetStory"\)[\s\S]*?if actual_route_count != expected_route_count:[\s\S]*?add_error\([\s\S]*?return False' `
    'AllRoad verification must reject missing or stale extra routes.'

# 【2026-10-12 回退点换了】原先这条钉的是"zip 备份清单里必须有 AllRoad"。
# zip 那套已经退休（MainWindow.UnrealSync.Process.cs 里留了说明），
# 回退点改成**同步前的全量本地提交**（Services/UnrealSync/UnrealSyncGitCommitService.cs）：
#   - 覆盖范围不再是一份手写的清单，而是 `git add -A`（工作区里的全部改动），
#     所以同步会改到的资产 —— AllRoad、MusicPlayer 蓝图、剧情表……天然都在里面；
#   - 口径是**只提交、不推送**（同步是高频动作，替用户往远端推不是我们该做的事）。
# 所以这里改成钉"全量"这一个性质本身。
Require-Pattern `
    (Join-Path $root 'Services\UnrealSync\UnrealSyncGitCommitService.cs') `
    'CommitEverythingAsync[\s\S]*?\["add", "-A"\][\s\S]*?\["commit", "-m", commitMessage\][\s\S]*?TryReadHeadShaAsync' `
    'The pre-sync rollback point must commit every local change (which covers AllRoad and the other synced assets).'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSync\UnrealSyncGitCommitService.cs') `
    '\["push"' `
    'The pre-sync rollback commit must never push to a remote.' -NotMatch

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
