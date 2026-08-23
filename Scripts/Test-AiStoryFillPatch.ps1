$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (!(Test-Path -LiteralPath $assemblyPath)) { throw 'Debug assembly is missing.' }

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
function T([string]$name) { $assembly.GetType($name, $true) }
$rowType = T 'GalExcleTools.StoryRow'
$documentType = T 'GalExcleTools.StoryDocument'
$chapterType = T 'GalExcleTools.ChapterInfo'
$characterType = T 'GalExcleTools.CharacterInfo'
$optionsType = T 'GalExcleTools.AiStoryFillTaskOptions'
$repositoryType = T 'GalExcleTools.Services.StoryDocumentRepository'
$serviceType = T 'GalExcleTools.Services.AiStoryFillService'
$json = [Text.Json.JsonSerializerOptions]::new()
$json.WriteIndented = $true
$repository = [Activator]::CreateInstance($repositoryType, @($json))
$service = [Activator]::CreateInstance($serviceType, @($repository, $json))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('tfac-ai-patch-' + [guid]::NewGuid().ToString('N'))

try {
    $chapterPath = Join-Path $testRoot 'chapter'
    [void](New-Item -ItemType Directory -Force -Path $chapterPath)
    $chapterArgs = [object[]]::new(6)
    $chapterArgs[0] = [string]'Patch Chapter'
    $chapterArgs[1] = [string]'TEST-PATCH-00'
    $chapterArgs[2] = [string]'MainThread'
    $chapterArgs[3] = [string]$chapterPath
    $chapterArgs[4] = [datetime]::Now
    $chapterArgs[5] = [int]0
    $chapter = ($chapterType.GetConstructors() | Where-Object { $_.GetParameters().Count -eq 6 } | Select-Object -First 1).Invoke($chapterArgs).PSObject.BaseObject

    $document = [Activator]::CreateInstance($documentType, $true)
    $set = $rowType.GetMethod('Set')
    foreach ($entry in @(
        @('第一句对白', '', '1'),
        @('已有说话人', 'Xia', '2'),
        @('第三句对白', '', '3')
    )) {
        $row = $documentType.GetMethod('CreateEmptyRow').Invoke($null, @($document.Rows.Count))
        [void]$set.Invoke($row, @('Tesxt', $entry[0]))
        [void]$set.Invoke($row, @('TalkChar', $entry[1]))
        [void]$set.Invoke($row, @('BGM', $entry[2]))
        [void]$document.Rows.Add($row)
        $document.RowSections[$row.Get('Name')] = 1
    }
    $repositoryType.GetMethod('Save').Invoke($repository, @($chapter, $document))

    $charactersType = [Collections.Generic.List``1].MakeGenericType($characterType)
    $characters = [Activator]::CreateInstance($charactersType)
    [void]$characters.Add([Activator]::CreateInstance($characterType, @('夏疏影', 'Xia', '#FFFFFF', 'D:\Xia')))
    $customSpeakers = [Collections.Generic.List[string]]::new()
    [void]$customSpeakers.Add('梦凪')
    $options = [Activator]::CreateInstance($optionsType)
    $options.Mode = 'patch-empty'
    $options.ProtocolVersion = 1
    [void]$options.PatchFields.Add('TalkChar')
    $packagePath = Join-Path $testRoot 'patch-package'
    $exportMethod = $serviceType.GetMethod('CreateConfiguredTaskPackage')
    $configuredArgs = [object[]]::new(6)
    $configuredArgs[0] = $chapter.PSObject.BaseObject
    $configuredArgs[1] = $document.PSObject.BaseObject
    $configuredArgs[2] = $characters.PSObject.BaseObject
    $configuredArgs[3] = [string]$packagePath
    $configuredArgs[4] = $customSpeakers.PSObject.BaseObject
    $configuredArgs[5] = $options.PSObject.BaseObject
    $export = $exportMethod.Invoke($service, $configuredArgs)

    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $packagePath 'manifest.json') | ConvertFrom-Json
    $source = @(Get-Content -Encoding UTF8 -LiteralPath (Join-Path $packagePath 'source.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    if ($manifest.mode -ne 'patch-empty' -or
        ($manifest.patchFields -join ',') -ne 'TalkChar' -or
        $source.Count -ne 2 -or
        $source[0].stableId -ne $document.Rows[0].StableId -or
        $source[0].rowIndex -ne 0 -or
        $source[0].Tesxt -ne '第一句对白' -or
        $source[0].nextText -ne '已有说话人' -or
        [string]::IsNullOrWhiteSpace($source[0].rowBaselineSha256)) {
        throw 'patch-empty export did not generate strict row sources.'
    }

    $document.Rows[0].Set('BGM', '9')
    $repositoryType.GetMethod('Save').Invoke($repository, @($chapter, $document))
    @(
        ('{"id":"row:' + $document.Rows[0].StableId + '","patch":{"TalkChar":"梦凪"},"status":"include","confidence":"high","sourceParagraph":455}'),
        ('{"id":"row:' + $document.Rows[2].StableId + '","patch":null,"status":"skip","confidence":"high"}')
    ) | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packagePath 'result.jsonl')
    $knownSpeakers = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$knownSpeakers.Add('')
    [void]$knownSpeakers.Add('梦凪')
    [void]$knownSpeakers.Add('Xia')
    $previewArgs = [object[]]::new(3)
    $previewArgs[0] = $chapter.PSObject.BaseObject
    $previewArgs[1] = [string]$packagePath
    $previewArgs[2] = $knownSpeakers.PSObject.BaseObject
    $preview = $serviceType.GetMethod('LoadImportPreview').Invoke($service, $previewArgs)
    if (!$preview.CanImport -or
        $preview.PendingIncludeCount -ne 1 -or
        $preview.Items[0].StableId -ne $document.Rows[0].StableId -or
        $preview.Items[0].CurrentTalkChar -ne '' -or
        $preview.Items[0].TalkChar -ne '梦凪' -or
        $preview.Items[0].SourceParagraph -ne 455) {
        throw 'patch-empty preview did not expose a valid field-level change.'
    }

    $import = $serviceType.GetMethod('ApplyImport').Invoke($service, @($chapter.PSObject.BaseObject, $preview.PSObject.BaseObject))
    $after = $repositoryType.GetMethod('Load').Invoke($repository, @($chapter))
    if ($import.ImportedCount -ne 1 -or
        !(Test-Path -LiteralPath $import.BackupPath) -or
        $after.Rows.Count -ne 3 -or
        $after.Rows[0].Get('TalkChar') -ne '梦凪' -or
        $after.Rows[0].Get('Tesxt') -ne '第一句对白' -or
        $after.Rows[0].Get('BGM') -ne '9' -or
        $after.Rows[1].Get('TalkChar') -ne 'Xia') {
        throw 'patch-empty import changed row order or non-target fields.'
    }
    $repeat = $serviceType.GetMethod('LoadImportPreview').Invoke($service, $previewArgs)
    if ($repeat.DuplicateCount -ne 1 -or $repeat.CanImport) {
        throw 'Repeated patch import was not recognized inside the same package.'
    }

    $conflictDocument = $repositoryType.GetMethod('Load').Invoke($repository, @($chapter))
    $conflictOptions = [Activator]::CreateInstance($optionsType)
    $conflictOptions.Mode = 'patch-empty'
    $conflictOptions.ProtocolVersion = 1
    [void]$conflictOptions.PatchFields.Add('TalkChar')
    $conflictPackage = Join-Path $testRoot 'conflict-package'
    $conflictExportArgs = [object[]]::new(6)
    $conflictExportArgs[0] = $chapter.PSObject.BaseObject
    $conflictExportArgs[1] = $conflictDocument.PSObject.BaseObject
    $conflictExportArgs[2] = $characters.PSObject.BaseObject
    $conflictExportArgs[3] = [string]$conflictPackage
    $conflictExportArgs[4] = $customSpeakers.PSObject.BaseObject
    $conflictExportArgs[5] = $conflictOptions.PSObject.BaseObject
    [void]$exportMethod.Invoke($service, $conflictExportArgs)
    $conflictId = $conflictDocument.Rows[2].StableId
    ('{"id":"row:' + $conflictId + '","patch":{"Tesxt":"禁止改写"},"status":"include","confidence":"high"}') |
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $conflictPackage 'result.jsonl')
    $strictPreviewArgs = [object[]]::new(3)
    $strictPreviewArgs[0] = $chapter.PSObject.BaseObject
    $strictPreviewArgs[1] = [string]$conflictPackage
    $strictPreviewArgs[2] = $knownSpeakers.PSObject.BaseObject
    $strictPreview = $serviceType.GetMethod('LoadImportPreview').Invoke($service, $strictPreviewArgs)
    if ($strictPreview.CanImport -or
        @($strictPreview.Issues | Where-Object { $_.Message -match '补丁字段未被本任务允许.*Tesxt' }).Count -ne 1) {
        throw 'Patch result was allowed to write a field not selected at export.'
    }

    $conflictDocument.Rows[2].Set('Tesxt', '导出后被修改')
    $repositoryType.GetMethod('Save').Invoke($repository, @($chapter, $conflictDocument))
    ('{"id":"row:' + $conflictId + '","patch":{"TalkChar":"梦凪"},"status":"include","confidence":"high"}') |
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $conflictPackage 'result.jsonl')
    $conflictPreviewArgs = [object[]]::new(3)
    $conflictPreviewArgs[0] = $chapter.PSObject.BaseObject
    $conflictPreviewArgs[1] = [string]$conflictPackage
    $conflictPreviewArgs[2] = $knownSpeakers.PSObject.BaseObject
    $conflictPreview = $serviceType.GetMethod('LoadImportPreview').Invoke($service, $conflictPreviewArgs)
    if ($conflictPreview.CanImport -or
        @($conflictPreview.Issues | Where-Object { $_.Message -match '内容已变化' }).Count -ne 1) {
        throw 'Per-row TalkChar/Tesxt baseline conflict was not detected.'
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}

Write-Host 'AI story-fill patch checks passed.'
