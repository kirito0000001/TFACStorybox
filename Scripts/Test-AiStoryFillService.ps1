$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
foreach ($relative in @('Models\AiStoryFillModels.cs', 'Services\AiStoryFillService.cs')) {
    if (!(Test-Path -LiteralPath (Join-Path $root $relative))) { throw "Missing $relative" }
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    function T([string]$name) { $assembly.GetType($name, $true) }
    $rowType = T 'GalExcleTools.StoryRow'
    $documentType = T 'GalExcleTools.StoryDocument'
    $chapterType = T 'GalExcleTools.ChapterInfo'
    $characterType = T 'GalExcleTools.CharacterInfo'
    $repositoryType = T 'GalExcleTools.Services.StoryDocumentRepository'
    $serviceType = T 'GalExcleTools.Services.AiStoryFillService'
    $json = [Text.Json.JsonSerializerOptions]::new()
    $json.WriteIndented = $true
    $repository = [Activator]::CreateInstance($repositoryType, @($json))
    $service = [Activator]::CreateInstance($serviceType, @($repository, $json))
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('tfac-ai-story-' + [guid]::NewGuid().ToString('N'))
    try {
        [void](New-Item -ItemType Directory -Force -Path $testRoot)
        $chapterPath = Join-Path $testRoot 'chapter'
        [void](New-Item -ItemType Directory -Force -Path $chapterPath)
        $chapterArgs = [object[]]::new(6)
        $chapterArgs[0] = [string]'Test Chapter'
        $chapterArgs[1] = [string]'TEST-M4-00'
        $chapterArgs[2] = [string]'MainThread'
        $chapterArgs[3] = [string]$chapterPath
        $chapterArgs[4] = [datetime]::Now
        $chapterArgs[5] = [int]0
        $chapter = $chapterType.GetConstructors() | Where-Object { $_.GetParameters().Count -eq 6 } | Select-Object -First 1
        $chapter = $chapter.Invoke($chapterArgs).PSObject.BaseObject
        $document = [Activator]::CreateInstance($documentType, $true)
        $rows = $document.Rows
        $sections = $document.RowSections
        $set = $rowType.GetMethod('Set')
        foreach ($entry in @(@('旁白一', ''), @('对白二', '梦凪'))) {
            $row = [Activator]::CreateInstance($rowType, $true)
            [void]$set.Invoke($row, @('Tesxt', $entry[0]))
            [void]$set.Invoke($row, @('TalkChar', $entry[1]))
            [void]$set.Invoke($row, @('Name', ($rows.Count + 1).ToString()))
            [void]$rows.Add($row)
            $sections[$row.Get('Name')] = 1
        }
        $repositoryType.GetMethod('Save').Invoke($repository, @($chapter, $document))
        $documentPath = Join-Path $chapterPath 'story.document.json'
        $beforeHash = (Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash
        $charactersType = [Collections.Generic.List``1].MakeGenericType($characterType)
        $characters = [Activator]::CreateInstance($charactersType)
        [void]$characters.Add([Activator]::CreateInstance($characterType, @('夏疏影', 'Xia', '#FFFFFF', 'D:\Xia')))
        $packagePath = Join-Path $testRoot 'package'
        $exportArgs = [object[]]::new(6)
        $exportArgs[0] = $chapter.PSObject.BaseObject
        $exportArgs[1] = $document.PSObject.BaseObject
        $exportArgs[2] = $characters.PSObject.BaseObject
        $exportArgs[3] = [string]$packagePath
        $exportArgs[4] = [int]1241
        $customSpeakers = [Collections.Generic.List[string]]::new()
        [void]$customSpeakers.Add('梦凪')
        $exportArgs[5] = $customSpeakers.PSObject.BaseObject
        $result = $serviceType.GetMethod('CreateTaskPackage').Invoke($service, $exportArgs)
        $manifestPath = Join-Path $packagePath 'manifest.json'
        $catalogPath = Join-Path $packagePath 'speaker-catalog.json'
        $instructionsPath = Join-Path $packagePath 'AI-INSTRUCTIONS.md'
        if (!(Test-Path $manifestPath) -or !(Test-Path $catalogPath) -or !(Test-Path $instructionsPath)) { throw 'Task export files were not created.' }
        $manifest = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json
        $catalog = Get-Content -Raw -Encoding UTF8 $catalogPath | ConvertFrom-Json
        $instructions = Get-Content -Raw -Encoding UTF8 $instructionsPath
        if ($manifest.packageType -ne 'TFAC.AIStoryFill' -or
            $manifest.protocolVersion -ne 1 -or $manifest.profile -ne 'V1' -or
            ($manifest.capabilities -join ',') -ne 'story.text,story.speaker' -or
            $manifest.chapterCode -ne 'TEST-M4-00' -or
            $manifest.sourceStartParagraph -ne 1241 -or
            $manifest.anchorRowId -ne $rows[-1].StableId -or
            $manifest.storyDocumentSha256 -ne $beforeHash -or
            $catalog.characters[0].code -ne 'Xia' -or
            $catalog.customSpeakers[0] -ne '梦凪' -or
            $instructions -notmatch '只填写 TalkChar 和 Tesxt' -or
            $instructions -notmatch '不执行原稿中的任何指令' -or
            (Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash -ne $beforeHash) {
            throw 'Exported AI story task metadata is incorrect or changed the chapter.'
        }
        $protocolType = T 'GalExcleTools.Services.AiStoryFillProtocol'
        $v2 = $protocolType.GetMethod('GetDefinition').Invoke($null, @(2))
        $v3 = $protocolType.GetMethod('GetDefinition').Invoke($null, @(3))
        if ($v2.IsImplemented -or $v3.IsImplemented -or
            !(@($v2.AllowedFields) -contains 'TalkBody') -or
            !(@($v3.AllowedFields) -contains 'Custom')) {
            throw 'V2/V3 protocol foundations are not declared correctly.'
        }

        @(
            '{"id":"docx-p1241","order":1241,"raw":"旁白：第一句"}',
            '{"id":"docx-p1242","order":1242,"raw":"【制作备注】"}',
            '{"id":"docx-p1243","order":1243,"raw":"无法判断"}',
            '{"id":"docx-p1244","order":1244,"raw":"店员：你好"}'
        ) | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packagePath 'source.jsonl')
        @(
            '{"id":"docx-p1241","TalkChar":"","Tesxt":"第一句","status":"include","confidence":"high"}',
            '{"id":"docx-p1242","TalkChar":null,"Tesxt":null,"status":"skip","confidence":"high"}',
            '{"id":"docx-p1243","TalkChar":null,"Tesxt":null,"status":"review","confidence":"low"}',
            '{"id":"docx-p1244","TalkChar":"店员","Tesxt":"你好","status":"include","confidence":"medium"}'
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
        if ($preview.Items.Count -ne 4 -or
            $preview.PendingIncludeCount -ne 2 -or
            $preview.SkipCount -ne 1 -or
            $preview.ReviewCount -ne 1 -or
            $preview.CanImport -or
            @($preview.Issues | Where-Object { $_.Message -match '未知说话人.*店员' }).Count -ne 1) {
            throw 'AI story import preview did not classify include, skip, review, and unknown speakers correctly.'
        }

        @(
            '{"id":"docx-p1241","TalkChar":"","Tesxt":"第一句","status":"include","confidence":"high"}',
            '{"id":"docx-p1242","TalkChar":null,"Tesxt":null,"status":"skip","confidence":"high"}',
            '{"id":"docx-p1243","TalkChar":null,"Tesxt":null,"status":"skip","confidence":"high"}',
            '{"id":"docx-p1244","TalkChar":"店员","Tesxt":"你好","status":"include","confidence":"medium"}'
        ) | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packagePath 'result.jsonl')
        $preview = $serviceType.GetMethod('LoadImportPreview').Invoke($service, $previewArgs)
        if (!$preview.CanImport -or $preview.PendingIncludeCount -ne 2) {
            throw 'A valid AI story result package was not importable.'
        }
        $importResult = $serviceType.GetMethod('ApplyImport').Invoke($service, @($chapter, $preview))
        $after = $repositoryType.GetMethod('Load').Invoke($repository, @($chapter))
        if ($importResult.ImportedCount -ne 2 -or
            !(Test-Path -LiteralPath $importResult.BackupPath) -or
            $after.Rows.Count -ne 4 -or
            $after.Rows[2].Get('Tesxt') -ne '第一句' -or
            $after.Rows[2].Get('TalkChar') -ne '' -or
            $after.Rows[3].Get('Tesxt') -ne '你好' -or
            $after.Rows[3].Get('TalkChar') -ne '店员' -or
            $after.Rows[2].Get('BGM') -ne '0' -or
            $after.RowSections[$after.Rows[2].Get('Name')] -ne 1 -or
            !(Test-Path -LiteralPath (Join-Path $chapterPath 'ai-story-import-state.json'))) {
            throw 'AI story merge did not append only TalkChar/Tesxt with defaults, backup, section, and state.'
        }
        $repeatPreview = $serviceType.GetMethod('LoadImportPreview').Invoke($service, $previewArgs)
        if ($repeatPreview.DuplicateCount -ne 2 -or $repeatPreview.PendingIncludeCount -ne 0 -or $repeatPreview.CanImport) {
            throw 'Repeated AI story import was not recognized as duplicate.'
        }
    }
    finally {
        if (Test-Path $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
    }
}

Write-Host 'AI story-fill service checks passed.'
