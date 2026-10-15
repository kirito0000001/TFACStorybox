$ErrorActionPreference = 'Stop'

# 小节完整性：删除句子之后小节号必须立刻恢复连续，且嵌着小节号的 Choice 函数名要跟着迁移；
# 同步侧不许再按节号直取剧情表（重复键 / 缺键都会让整次同步崩掉）。
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
    (Join-Path $root 'MainWindow.Story.cs') `
    'RepointSectionAnchorsAfterRowDelete[\s\S]*?RemoveUnusedStoryChoiceNotes[\s\S]*?SynchronizeStorySectionState[\s\S]*?NormalizeStoryChoiceFunctionPrefixes[\s\S]*?QueueCurrentStoryDocumentSave' `
    'The delete flow must repoint anchors, prune notes, renumber sections, migrate choice prefixes, then save - in that order.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.cs') `
    'sectionsRenumbered[\s\S]*?SynchronizeStorySectionState\(\)' `
    'The delete flow must close the section-number gap left by removing a single-line section.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'BuildChapterTablesBySection[\s\S]*?GroupBy\(table => table\.Section\)[\s\S]*?IsChapterMainStoryTable' `
    'Chapter story tables must be grouped by section before AllRoad generation.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'tablesBySection\[section\.Section\]' `
    'AllRoad generation must not index section tables directly by section number.' -NotMatch

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'GetChapterStoryCsvPathsForSync[\s\S]*?GroupBy\(item => item\.Section\)[\s\S]*?mainCsvPath' `
    'One story table per section: the chapter-named CSV must win over stray -00 style files.'

# 小节下拉框只负责**导航**（跳到该节第一句），不是"把当前句改成第几节"的编辑器。
# 后者那半个方案（SetCurrentStorySection / 视图模型里的 SetCurrentSection）已经删掉了，
# 这里钉住它别又长回来 —— 长回来的话，选一次下拉框就会悄悄改数据。
Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Sections.cs') `
    'StorySectionComboBox_SelectionChanged[\s\S]*?NavigateToStoryRow' `
    'The section combo box must navigate to the section, not edit the current row.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Sections.cs') `
    'SetCurrentStorySection' `
    'The unwired section-editing path must stay removed.' -NotMatch

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
    $setCell = $rowType.GetMethod('Set')

    # ── 前提：删掉「只有一句的小节」确实会留下编号空洞，靠 NormalizeContiguousSections 补 ──
    # 删除路径自己跑不动（它在 MainWindow 里），所以这里只钉住"为什么必须补这一步"。
    $editorServiceType = $assembly.GetType('GalExcleTools.Services.StoryEditorService', $true)
    $csvServiceType = $assembly.GetType('GalExcleTools.Services.StoryCsvService', $true)
    $csvService = [System.Activator]::CreateInstance($csvServiceType, $true)
    $editorService = [System.Activator]::CreateInstance($editorServiceType, @($csvService))
    $gapDocument = [System.Activator]::CreateInstance($documentType, $true)
    $gapRows = $documentType.GetProperty('Rows').GetValue($gapDocument)
    $gapSections = $documentType.GetProperty('RowSections').GetValue($gapDocument)
    foreach ($entry in @(@('1', 1), @('2', 2), @('3', 3), @('4', 3))) {
        $row = [System.Activator]::CreateInstance($rowType, $true)
        [void]$setCell.Invoke($row, @('Name', $entry[0]))
        [void]$gapRows.Add($row)
        $gapSections.Add($entry[0], $entry[1])
    }

    $deleteResult = $editorServiceType.GetMethod('DeleteCurrent').Invoke(
        $editorService,
        @($gapRows, $gapSections, 1, [System.Collections.Generic.List[string]]::new()))
    if ($gapRows.Count -ne 3 -or $deleteResult.RowIndex -ne 1) {
        throw 'Deleting the middle sentence must leave three rows and select the successor.'
    }
    if ($gapSections['2'] -ne 3) {
        throw 'Deleting a single-line section is expected to leave a section-number gap for the caller to close.'
    }

    $normalized = $editorServiceType.GetMethod('NormalizeContiguousSections').Invoke(
        $editorService,
        @($gapRows, $gapSections))
    if (-not $normalized -or
        $gapSections['1'] -ne 1 -or
        $gapSections['2'] -ne 2 -or
        $gapSections['3'] -ne 2) {
        throw 'Normalizing must close the section-number gap and keep section boundaries in row order.'
    }

    # ── 同步侧：同一节两张表不许崩，缺表的小节要跳过 ──
    # 这两项断言的是本次改动后的行为，所以等编译过再跑（旧 DLL 里没有这个私有方法）。
    $syncServiceType = $assembly.GetType('GalExcleTools.Services.UnrealSyncService', $true)
    $tableType = $assembly.GetType('GalExcleTools.UnrealStoryTableSyncEntry', $true)
    $tablesBySectionMethod = $syncServiceType.GetMethod(
        'BuildChapterTablesBySection',
        [System.Reflection.BindingFlags]'NonPublic,Static')
    if ($null -eq $tablesBySectionMethod) {
        Write-Host '(skip sync-table reflection: 当前 DLL 早于本次改动，重新编译后会做「同节两张表」和「小节没有表」这两项。)'
    }
    else {
        $syncService = [System.Activator]::CreateInstance($syncServiceType, $true)
        $branchDocument = [System.Activator]::CreateInstance($documentType, $true)
        $branchRows = $documentType.GetProperty('Rows').GetValue($branchDocument)
        $branchSections = $documentType.GetProperty('RowSections').GetValue($branchDocument)
        foreach ($entry in @(@('1', 1), @('2', 2), @('3', 3))) {
            $row = [System.Activator]::CreateInstance($rowType, $true)
            [void]$setCell.Invoke($row, @('Name', $entry[0]))
            [void]$setCell.Invoke($row, @('Tesxt', "第 $($entry[1]) 小节正文"))
            [void]$branchRows.Add($row)
            $branchSections.Add($entry[0], $entry[1])
        }

        $tableListType = [System.Collections.Generic.List``1].MakeGenericType($tableType)
        $tables = [System.Activator]::CreateInstance($tableListType)
        $makeTable = {
            param([string]$csvPath, [string]$asset, [int]$section)
            $legacy = [System.Collections.Generic.List[string]]::new()
            return [System.Activator]::CreateInstance(
                $tableType,
                @($csvPath, $asset, '/Script/GALLibrary.StoryStruct', $legacy, 'WHK-M1-00', $section))
        }
        # 第 1 节故意放两张：先来的那张是历史遗留的 `WHK-M1.csv`，正式的应是文件名 = 章节号的那张。
        [void]$tables.Add((& $makeTable 'C:\Temp\WHK-M1.csv' '/Game/Story/Stray.Stray' 1))
        [void]$tables.Add((& $makeTable 'C:\Temp\WHK-M1-00.csv' '/Game/Story/WHK-M1-00.WHK-M1-00' 1))
        [void]$tables.Add((& $makeTable 'C:\Temp\WHK-M1-01.csv' '/Game/Story/WHK-M1-01.WHK-M1-01' 2))
        # 第 3 节故意没有表：文档里有小节、磁盘上没有它的 CSV —— 旧代码在这里 KeyNotFoundException。

        $builder = $syncServiceType.GetMethod('BuildChapterStoryBranchSyncEntries')
        $sources = @($builder.Invoke($syncService, @('M1', 'WHK-M1-00', $branchDocument, $tables)))
        $bySource = @{}
        foreach ($source in $sources) { $bySource[$source.SourceTable] = $source }
        if ($sources.Count -ne 2 -or
            -not $bySource.ContainsKey('/Game/Story/WHK-M1-00.WHK-M1-00') -or
            $bySource.ContainsKey('/Game/Story/Stray.Stray')) {
            throw 'A duplicate section table must be resolved to the chapter-named CSV, and a section without a table must be skipped.'
        }
    }
}

Write-Host 'Story section-integrity checks passed.'
