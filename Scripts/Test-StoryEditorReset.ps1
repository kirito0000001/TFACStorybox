$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $PSScriptRoot '..\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (!(Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Debug assembly not found: $assemblyPath"
}

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
function Get-Type([string]$Name) { return $assembly.GetType($Name, $true) }
function New-Internal([string]$Name, [object[]]$Arguments = @()) {
    return [Activator]::CreateInstance((Get-Type $Name), $Arguments)
}

$rowType = Get-Type 'GalExcleTools.StoryRow'
$documentType = Get-Type 'GalExcleTools.StoryDocument'
$chapterType = Get-Type 'GalExcleTools.ChapterInfo'
$projectType = Get-Type 'GalExcleTools.ProjectInfo'
$csv = New-Internal 'GalExcleTools.Services.StoryCsvService'
$editor = New-Internal 'GalExcleTools.Services.StoryEditorService' @($csv)
$undo = New-Internal 'GalExcleTools.Services.StoryUndoService'
$jsonOptions = [Text.Json.JsonSerializerOptions]::new()
$jsonOptions.WriteIndented = $true
$repository = New-Internal 'GalExcleTools.Services.StoryDocumentRepository' @($jsonOptions)
$saveCoordinator = New-Internal 'GalExcleTools.Services.StorySaveCoordinator' @($repository)
$exporter = New-Internal 'GalExcleTools.Services.StoryCsvExporter' @($csv, $repository)
$importer = New-Internal 'GalExcleTools.Services.StoryCsvImporter' @($csv, $repository)
$textData = New-Internal 'GalExcleTools.Services.ProjectTextDataService' @($repository, $jsonOptions)
$set = $rowType.GetMethod('Set')
$get = $rowType.GetMethod('Get')
$stableIdProperty = $rowType.GetProperty('StableId')

function New-Row([int]$Index, [string]$Text) {
    $row = [Activator]::CreateInstance($rowType, $true)
    [void]$set.Invoke($row, @('Name', $Index.ToString()))
    [void]$set.Invoke($row, @('Tesxt', $Text))
    return $row
}

$document = [Activator]::CreateInstance($documentType, $true)
$rows = $documentType.GetProperty('Rows').GetValue($document)
$sections = $documentType.GetProperty('RowSections').GetValue($document)
for ($index = 1; $index -le 4; $index++) {
    $row = New-Row $index "Line-$index"
    [void]$rows.Add($row)
    $sections[$index.ToString()] = $index -lt 4 ? 1 : 2
}
$oldIds = @($rows | ForEach-Object { $stableIdProperty.GetValue($_) })

$moveResult = $editor.GetType().GetMethod('MoveNext').Invoke($editor, @($rows, $sections, 3))
if ($rows.Count -ne 4 -or $moveResult.RowIndex -ne 3 -or $moveResult.Changed) {
    throw 'Next-row navigation created data at the final row.'
}

$undo.GetType().GetMethod('BeginRowEdit').Invoke($undo, @($document, 0, 'Edit one row'))
[void]$set.Invoke($rows[0], @('Tesxt', 'Changed text'))
[void]$undo.GetType().GetMethod('Commit').Invoke($undo, @($document, 0))
$textChange = $undo.GetType().GetProperty('LastUndoChange').GetValue($undo)
if ($textChange.ChangedRowCount -ne 1 -or $textChange.HasOrderChange) {
    throw 'Text edit was not stored as one compact delta.'
}
$undoArgs = [object[]]@($document, 0, $null)
[void]$undo.GetType().GetMethod('TryUndo').Invoke($undo, $undoArgs)
if ($get.Invoke($rows[0], @('Tesxt')) -ne 'Line-1') { throw 'Text undo failed.' }
$redoArgs = [object[]]@($document, 0, $null)
[void]$undo.GetType().GetMethod('TryRedo').Invoke($undo, $redoArgs)
if ($get.Invoke($rows[0], @('Tesxt')) -ne 'Changed text') { throw 'Text redo failed.' }

$undo.GetType().GetMethod('Begin').Invoke($undo, @($document, 1, 'Insert one row'))
[void]$editor.GetType().GetMethod('InsertAfterCurrent').Invoke($editor, @($rows, $sections, 1))
[void]$undo.GetType().GetMethod('Commit').Invoke($undo, @($document, 2))
$insertChange = $undo.GetType().GetProperty('LastUndoChange').GetValue($undo)
if ($insertChange.ChangedRowCount -ne 1 -or !$insertChange.HasOrderChange) {
    throw 'Insertion was not stored as one row plus order delta.'
}
if ($stableIdProperty.GetValue($rows[0]) -ne $oldIds[0] -or
    $stableIdProperty.GetValue($rows[1]) -ne $oldIds[1] -or
    $stableIdProperty.GetValue($rows[3]) -ne $oldIds[2]) {
    throw 'Stable row identities shifted during insertion.'
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('GalExcleTools-story-json-' + [Guid]::NewGuid().ToString('N'))
$resolvedRoot = [IO.Path]::GetFullPath($testRoot)
if (!$resolvedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Story JSON test escaped temp.'
}

try {
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $chapterCtor = $chapterType.GetConstructors([Reflection.BindingFlags]'Instance,Public,NonPublic') |
        Where-Object { $_.GetParameters().Count -eq 6 } | Select-Object -First 1
    $chapterArgs = [object[]]::new(6)
    $chapterArgs[0] = [string]'Test Chapter'
    $chapterArgs[1] = [string]'RESETCH'
    $chapterArgs[2] = [string]'MainThread'
    $chapterArgs[3] = [string]$testRoot
    $chapterArgs[4] = [DateTime]::Now
    $chapterArgs[5] = [int]0
    $chapter = $chapterCtor.Invoke($chapterArgs).PSObject.BaseObject
    $multilineText = "First line, with comma`r`nSecond line says `"quoted`"."
    [void]$set.Invoke($rows[0], @('Tesxt', $multilineText))
    $customBranchIds = $documentType.GetProperty('CustomBranchSectionFirstRowIds').GetValue($document)
    $customBranchId = $stableIdProperty.GetValue($rows[0])
    [void]$customBranchIds.Add($customBranchId)
    [void]$repository.GetType().GetMethod('Save').Invoke($repository, @($chapter, $document))
    $documentPath = Join-Path $testRoot 'story.document.json'
    if (!(Test-Path -LiteralPath $documentPath)) { throw 'StoryDocument JSON was not created.' }
    $loadedDocument = $repository.GetType().GetMethod('Load').Invoke($repository, @($chapter))
    $loadedCustomBranchIds = $documentType.GetProperty('CustomBranchSectionFirstRowIds').GetValue($loadedDocument)
    if (!$loadedCustomBranchIds.Contains($customBranchId)) {
        throw 'StoryDocument did not preserve the custom AllRoad section marker.'
    }

    $exported = $exporter.GetType().GetMethod('ExportChapter').Invoke($exporter, @($chapter))
    $mainCsvPath = Join-Path $testRoot 'RESETCH.csv'
    if (!(Test-Path -LiteralPath $mainCsvPath)) { throw 'CSV export was not created.' }
    $readCsvArgs = [object[]]::new(1)
    $readCsvArgs[0] = [string]$mainCsvPath
    $roundTripRows = $csv.GetType().GetMethod('ReadRows').Invoke($csv, $readCsvArgs)
    $roundTripText = [string]$get.Invoke($roundTripRows[0], @('Tesxt'))
    if ($roundTripText -ne $multilineText) {
        throw "CSV round-trip did not preserve a multiline quoted story field. ExpectedText=$($multilineText | ConvertTo-Json -Compress) ActualText=$($roundTripText | ConvertTo-Json -Compress)"
    }
    $csvHashBeforeJsonEdit = (Get-FileHash -LiteralPath $mainCsvPath -Algorithm SHA256).Hash
    [void]$set.Invoke($rows[0], @('Tesxt', 'JSON is canonical'))
    [void]$saveCoordinator.GetType().GetMethod('QueueSave').Invoke($saveCoordinator, @($chapter, $document))
    $flushTask = $saveCoordinator.GetType().GetMethod('FlushAsync').Invoke(
        $saveCoordinator,
        @([Threading.CancellationToken]::None))
    [void]$flushTask.GetAwaiter().GetResult()
    if ((Get-FileHash -LiteralPath $mainCsvPath -Algorithm SHA256).Hash -ne $csvHashBeforeJsonEdit) {
        throw 'Normal JSON save unexpectedly rewrote CSV.'
    }
    [void]$exporter.GetType().GetMethod('ExportChapter').Invoke($exporter, @($chapter))
    if ((Get-FileHash -LiteralPath $mainCsvPath -Algorithm SHA256).Hash -eq $csvHashBeforeJsonEdit) {
        throw 'Explicit CSV export did not include the JSON text change.'
    }

    $projectCtor = $projectType.GetConstructors([Reflection.BindingFlags]'Instance,Public,NonPublic') |
        Where-Object { $_.GetParameters().Count -eq 8 } | Select-Object -First 1
    $projectArgs = [object[]]::new(8)
    $projectArgs[0] = [string]'Reset Project'
    $projectArgs[1] = [string]'RESET'
    $projectArgs[2] = [string]'Project-Reset'
    $projectArgs[3] = [string]$testRoot
    $projectArgs[4] = $null
    $projectArgs[5] = [string]'Assets'
    $projectArgs[6] = $null
    $projectArgs[7] = [DateTime]::Now
    $project = $projectCtor.Invoke($projectArgs).PSObject.BaseObject
    $chapterListType = [Collections.Generic.List``1].MakeGenericType($chapterType)
    $chapters = [Activator]::CreateInstance($chapterListType)
    [void]$chapters.Add($chapter)
    $textRows = $textData.GetType().GetMethod('LoadTextRows').Invoke($textData, @($project, $chapters))
    if (@($textRows).Count -lt 4 -or @($textRows)[0].Text -ne 'JSON is canonical') {
        throw 'Text tool did not read canonical StoryDocument data.'
    }

    $importCsvPath = Join-Path $testRoot 'manual-import.csv'
    $importRowsType = [Collections.Generic.List``1].MakeGenericType($rowType)
    $importRows = [Activator]::CreateInstance($importRowsType)
    [void]$importRows.Add((New-Row 1 'Imported line'))
    $writeCsvArgs = [object[]]::new(2)
    $writeCsvArgs[0] = [string]$importCsvPath
    $writeCsvArgs[1] = $importRows.PSObject.BaseObject
    [void]$csv.GetType().GetMethod('WriteRows').Invoke($csv, $writeCsvArgs)
    $pathList = [Collections.Generic.List[string]]::new()
    [void]$pathList.Add($importCsvPath)
    $importResult = $importer.GetType().GetMethod('Import').Invoke($importer, @($chapter, $pathList, $false))
    $afterImport = $repository.GetType().GetMethod('Load').Invoke($repository, @($chapter))
    if ($importResult.ImportedCount -ne 1 -or $get.Invoke($afterImport.Rows[-1], @('Tesxt')) -ne 'Imported line') {
        throw 'Explicit CSV import did not append to StoryDocument.'
    }

    $collisionArgs = $chapterArgs.Clone()
    $collisionArgs[1] = 'CH-01'
    $collisionChapter = $chapterCtor.Invoke($collisionArgs).PSObject.BaseObject
    $mainPath = $csv.GetType().GetMethod('GetSectionCsvPath').Invoke($csv, @($collisionChapter, 1))
    $sectionPath = $csv.GetType().GetMethod('GetSectionCsvPath').Invoke($csv, @($collisionChapter, 2))
    if ([IO.Path]::GetFullPath($mainPath) -eq [IO.Path]::GetFullPath($sectionPath)) {
        throw 'Numeric chapter suffix still collides with section CSV.'
    }
}
finally {
    $saveCoordinator.Dispose()
    if (Test-Path -LiteralPath $resolvedRoot) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}

Write-Host 'Story editor JSON tests passed.'
