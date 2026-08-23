$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$functionSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Services\StoryFunctionService.cs')
$editorSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Story.Functions.cs')

if ($functionSource -notmatch 'BuildNextChoiceIndicator[\s\S]*?TryParseChapterChoiceFunctionIndex') {
    throw 'Choice generation must scan indexes across the entire chapter.'
}
if ($editorSource -notmatch 'NormalizeStoryChoiceFunctionPrefixes[\s\S]*?nextChoiceIndex[\s\S]*?nextChoiceIndex\+\+[\s\S]*?RemapChoiceFunction') {
    throw 'Existing choices must be renumbered in story-row order with route remapping.'
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $serviceType = $assembly.GetType('GalExcleTools.Services.StoryFunctionService', $true)
    $rowType = $assembly.GetType('GalExcleTools.StoryRow', $true)
    $rowsType = [Collections.Generic.List``1].MakeGenericType($rowType)
    $rows = [Activator]::CreateInstance($rowsType)
    $set = $rowType.GetMethod('Set')
    foreach ($custom in @('M2-00-Choice1', 'M2-04-Choice2')) {
        $row = [Activator]::CreateInstance($rowType, $true)
        [void]$set.Invoke($row, @('Custom', $custom))
        [void]$rows.Add($row)
    }

    $nextIndicator = $serviceType.GetMethod('BuildNextChoiceIndicator').Invoke(
        $null,
        @('WHK-M2-00', 8, 'WHK', $rows))
    if ($nextIndicator -ne 'M2-07-Choice3') {
        throw "Choice numbering must be chapter-wide. Actual=$nextIndicator"
    }
}

Write-Host 'Story choice-numbering checks passed.'
