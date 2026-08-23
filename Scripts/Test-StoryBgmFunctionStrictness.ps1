$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Services\StoryFunctionService.cs')
$media = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Story.Media.cs')
if ($service -notmatch 'ContainsExactFunction') {
    throw 'Strict runtime function matching is missing.'
}
if ($media -notmatch 'ContainsExactFunction[\s\S]*?BGM_Stop[\s\S]*?ContainsExactFunction[\s\S]*?BGM_Start') {
    throw 'Story BGM feedback must use strict standard function names.'
}
if ($media -match 'ContainsFunction\([^\r\n]*BGM(?:START|STOP)') {
    throw 'Story BGM feedback still uses underscore-insensitive legacy matching.'
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $type = $assembly.GetType('GalExcleTools.Services.StoryFunctionService', $true)
    $method = $type.GetMethod('ContainsExactFunction')
    if (!$method.Invoke($null, @('TitleShowMode/BGM_Start', 'BGM_Start')) -or
        !$method.Invoke($null, @('BGM_Stop', 'BGM_Stop')) -or
        $method.Invoke($null, @('TitleShowMode/BGMStart', 'BGM_Start')) -or
        $method.Invoke($null, @('BGMStop', 'BGM_Stop'))) {
        throw 'Legacy BGMStart/BGMStop must remain stored but never control toolbox BGM feedback.'
    }
}

Write-Host 'Story BGM function strictness checks passed.'
