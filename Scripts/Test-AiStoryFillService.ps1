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
        if (!(Test-Path $manifestPath) -or !(Test-Path $catalogPath)) { throw 'Task export files were not created.' }
        $manifest = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json
        $catalog = Get-Content -Raw -Encoding UTF8 $catalogPath | ConvertFrom-Json
        if ($manifest.packageType -ne 'TFAC.AIStoryFill' -or
            $manifest.chapterCode -ne 'TEST-M4-00' -or
            $manifest.sourceStartParagraph -ne 1241 -or
            $manifest.anchorRowId -ne $rows[-1].StableId -or
            $manifest.storyDocumentSha256 -ne $beforeHash -or
            $catalog.characters[0].code -ne 'Xia' -or
            $catalog.customSpeakers[0] -ne '梦凪' -or
            (Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash -ne $beforeHash) {
            throw 'Exported AI story task metadata is incorrect or changed the chapter.'
        }
    }
    finally {
        if (Test-Path $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
    }
}

Write-Host 'AI story-fill service checks passed.'
