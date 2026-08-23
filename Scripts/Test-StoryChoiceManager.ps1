$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
foreach ($relative in @('Models\StoryChoiceManagerModels.cs', 'Services\StoryChoiceManagerService.cs')) {
    if (!(Test-Path -LiteralPath (Join-Path $root $relative))) { throw "Missing $relative" }
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    function T([string]$name) { $assembly.GetType($name, $true) }
    $rowType = T 'GalExcleTools.StoryRow'
    $documentType = T 'GalExcleTools.StoryDocument'
    $chapterType = T 'GalExcleTools.ChapterInfo'
    $repositoryType = T 'GalExcleTools.Services.StoryDocumentRepository'
    $managerType = T 'GalExcleTools.Services.StoryChoiceManagerService'
    $json = [Text.Json.JsonSerializerOptions]::new()
    $repository = [Activator]::CreateInstance($repositoryType, @($json))
    $manager = [Activator]::CreateInstance($managerType, @($repository))
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('tfac-choice-manager-' + [guid]::NewGuid().ToString('N'))
    try {
        [void](New-Item -ItemType Directory -Force -Path $testRoot)
        $chapterCtor = $chapterType.GetConstructors() | Where-Object { $_.GetParameters().Count -eq 6 } | Select-Object -First 1
        $chapterArgs = [object[]]::new(6)
        $chapterArgs[0] = [string]'Test'
        $chapterArgs[1] = [string]'TEST-M1-00'
        $chapterArgs[2] = [string]'MainThread'
        $chapterArgs[3] = [string]$testRoot
        $chapterArgs[4] = [datetime]::Now
        $chapterArgs[5] = [int]0
        $chapter = $chapterCtor.Invoke($chapterArgs).PSObject.BaseObject
        $document = [Activator]::CreateInstance($documentType, $true)
        $rows = $document.Rows
        $sections = $document.RowSections
        $set = $rowType.GetMethod('Set')
        $created = @()
        foreach ($section in 1..3) {
            $row = [Activator]::CreateInstance($rowType, $true)
            [void]$set.Invoke($row, @('Name', $section.ToString()))
            [void]$set.Invoke($row, @('Tesxt', "Section $section"))
            [void]$set.Invoke($row, @('Custom', $(if ($section -eq 1) { 'M1-00-Choice1' } else { '' })))
            [void]$rows.Add($row)
            $sections.Add($section.ToString(), $section)
            $created += $row
        }
        $notes = [Collections.Generic.List[string]]::new()
        [void]$notes.Add('Old option')
        $document.ChoiceNotes.Choices.Add('M1-00-Choice1', $notes)
        $repositoryType.GetMethod('Save').Invoke($repository, @($chapter, $document))
        $documentPath = Join-Path $testRoot 'story.document.json'
        $beforeHash = (Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash

        $session = $managerType.GetMethod('CreateSession').Invoke($manager, @($chapter))
        $managerType.GetMethod('UpdateOption').Invoke($manager, @($session, 'M1-00-Choice1', 0, 'Updated option', $created[1].StableId))
        $managerType.GetMethod('SetCustomSection').Invoke($manager, @($session, 3, $true))
        $displayMethod = $managerType.GetMethod('GetSectionDisplayName')
        $choiceName = $displayMethod.Invoke($null, @($session.Flow, $session.Flow.Sections[0]))
        $branchName = $displayMethod.Invoke($null, @($session.Flow, $session.Flow.Sections[1]))
        $commonName = $displayMethod.Invoke($null, @($session.Flow, $session.Flow.Sections[2]))
        if ($choiceName -ne '选项1' -or $branchName -ne '选项1-分支1' -or $commonName -ne '选项1-共通线') {
            throw "Choice manager must use conceptual structure names. Choice=$choiceName Branch=$branchName Common=$commonName"
        }
        if ((Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash -ne $beforeHash) {
            throw 'Editing the manager draft changed the source document before save.'
        }
        $issues = @($managerType.GetMethod('Validate').Invoke($manager, @($session)))
        if ($issues.Count -ne 0) { throw "Valid draft produced issues: $($issues -join '; ')" }
        $managerType.GetMethod('Save').Invoke($manager, @($session))
        $loaded = $repositoryType.GetMethod('Load').Invoke($repository, @($chapter))
        if ($loaded.ChoiceNotes.Choices['M1-00-Choice1'][0] -ne 'Updated option' -or
            $loaded.BranchRoutes.Count -ne 1 -or
            $loaded.BranchRoutes[0].TargetSectionFirstRowId -ne $created[1].StableId -or
            !$loaded.CustomBranchSectionFirstRowIds.Contains($created[2].StableId)) {
            throw 'Saving the manager draft did not persist option, target, and custom section changes.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
    }
}

Write-Host 'Story choice-manager checks passed.'
