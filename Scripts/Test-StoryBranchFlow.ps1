$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root 'Services\StoryBranchFlowService.cs'
$modelPath = Join-Path $root 'Models\StoryBranchFlowModels.cs'
if (!(Test-Path -LiteralPath $servicePath) -or !(Test-Path -LiteralPath $modelPath)) {
    throw 'Shared story branch-flow service is missing.'
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $serviceType = $assembly.GetType('GalExcleTools.Services.StoryBranchFlowService', $true)
    $documentType = $assembly.GetType('GalExcleTools.StoryDocument', $true)
    $rowType = $assembly.GetType('GalExcleTools.StoryRow', $true)
    $routeType = $assembly.GetType('GalExcleTools.StoryChoiceRoute', $true)
    $document = [Activator]::CreateInstance($documentType, $true)
    $rows = $documentType.GetProperty('Rows').GetValue($document)
    $sections = $documentType.GetProperty('RowSections').GetValue($document)
    $set = $rowType.GetMethod('Set')
    $created = @()
    foreach ($section in 1..5) {
        $row = [Activator]::CreateInstance($rowType, $true)
        [void]$set.Invoke($row, @('Name', $section.ToString()))
        [void]$set.Invoke($row, @('Tesxt', "Section $section first line"))
        [void]$set.Invoke($row, @('Custom', $(if ($section -eq 1) { 'M1-00-Choice1' } else { '' })))
        [void]$rows.Add($row)
        $sections.Add($section.ToString(), $section)
        $created += $row
    }

    $choices = $documentType.GetProperty('ChoiceNotes').GetValue($document).Choices
    $notes = [Collections.Generic.List[string]]::new()
    foreach ($text in @('A text', 'B text', 'C text', 'D text')) { [void]$notes.Add($text) }
    $choices.Add('M1-00-Choice1', $notes)
    $routes = $documentType.GetProperty('BranchRoutes').GetValue($document)
    [void]$routes.Add([Activator]::CreateInstance($routeType, @('M1-00-Choice1', 0, 'A', $created[1].StableId, $true)))
    [void]$routes.Add([Activator]::CreateInstance($routeType, @('M1-00-Choice1', 2, 'C', $created[2].StableId, $true)))
    [void]$documentType.GetProperty('CustomBranchSectionFirstRowIds').GetValue($document).Add($created[3].StableId)

    $flow = $serviceType.GetMethod('Build').Invoke($null, @($document))
    $bySection = @{}
    foreach ($sectionFlow in $flow.Sections) { $bySection[$sectionFlow.Section] = $sectionFlow }
    if ($flow.Sections.Count -ne 5 -or
        $bySection[1].Kind.ToString() -ne 'Choice' -or
        $bySection[1].Options.Count -ne 4 -or
        $bySection[1].Options[0].EffectiveTargetSection -ne 2 -or
        $bySection[1].Options[1].EffectiveTargetSection -ne 4 -or
        !$bySection[1].Options[1].UsesInferredCommon -or
        $bySection[1].Options[2].EffectiveTargetSection -ne 3 -or
        $bySection[2].DefaultTargetSection -ne 4 -or
        $bySection[3].DefaultTargetSection -ne 4 -or
        $bySection[4].Kind.ToString() -ne 'Custom' -or
        $bySection[5].Kind.ToString() -ne 'Terminal' -or
        $bySection[2].FirstSentencePreview -ne 'Section 2 first line') {
        throw 'Shared branch flow did not model choice, inferred common, custom, and terminal sections correctly.'
    }
}

Write-Host 'Story branch-flow checks passed.'
