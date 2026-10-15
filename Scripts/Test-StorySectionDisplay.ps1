$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root 'Services\StorySectionDisplay.cs'
if (!(Test-Path -LiteralPath $servicePath)) {
    throw 'Shared zero-based story section display formatter is missing.'
}

$sources = @(
    'MainWindow.Story.Sections.cs',
    'MainWindow.Workbench.ChoiceManager.cs',
    'MainWindow.Workbench.TextTool.cs',
    'Models\StoryTodoModels.cs',
    'Services\StoryBranchService.cs',
    'Services\StoryFunctionService.cs',
    'Services\StoryCsvImporter.cs',
    'Services\ChapterRepairService.cs'
)
foreach ($source in $sources) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $source)
    if ($text -notmatch 'StorySectionDisplay\.FormatName') {
        throw "$source must use the shared zero-based section formatter."
    }
}

# 反面：不许再把**内部 1 起号**直接印进界面文字（那正是"编辑器第 6 小节、文本工具小节 7"的来源）。
$forbidden = @(
    @{ Source = 'MainWindow.Workbench.TextTool.cs'; Pattern = '小节 \{row\.Section\}' },
    @{ Source = 'Models\StoryTodoModels.cs'; Pattern = '第 \{Section\} 小节' }
)
foreach ($entry in $forbidden) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root $entry.Source)
    if ($text -match $entry.Pattern) {
        throw "$($entry.Source) must not print the internal one-based section number ($($entry.Pattern))."
    }
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $type = $assembly.GetType('GalExcleTools.Services.StorySectionDisplay', $true)
    $method = $type.GetMethod('FormatName')
    if ($method.Invoke($null, @(1)) -ne '第 0 小节' -or
        $method.Invoke($null, @(13)) -ne '第 12 小节') {
        throw 'Story section display must be zero-based while internal sections remain one-based.'
    }
}

Write-Host 'Story section-display checks passed.'
