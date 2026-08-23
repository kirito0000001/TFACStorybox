$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Services\StoryFunctionService.cs')
$editor = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Story.Functions.cs')
$media = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Story.Media.cs')
if ($service -notmatch 'IsRegisteredFunctionValue' -or $service -notmatch 'CreateFunctionBranchChoices') {
    throw 'Function registry validation and branch choices are missing.'
}
$buildBlock = [regex]::Match($editor, 'BuildStoryFunctionValueAsync[\s\S]*?ChooseChapterJumpFunctionValueAsync').Value
if ($buildBlock -match 'default-custom|CustomFunction|ShowNameInputDialogAsync') {
    throw 'Story function creation still allows unrestricted custom values.'
}
if ($media -notmatch '未登记函数[\s\S]*?IsRegisteredFunctionValue|IsRegisteredFunctionValue[\s\S]*?未登记函数') {
    throw 'Unknown existing functions must be marked in the story inspector.'
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $serviceType = $assembly.GetType('GalExcleTools.Services.StoryFunctionService', $true)
    $entryType = $assembly.GetType('GalExcleTools.FunctionEntry', $true)
    $branchType = $assembly.GetType('GalExcleTools.FunctionBranch', $true)
    $entriesType = [Collections.Generic.List``1].MakeGenericType($entryType)
    $branchesType = [Collections.Generic.List``1].MakeGenericType($branchType)
    $branches = [Activator]::CreateInstance($branchesType)
    [void]$branches.Add([Activator]::CreateInstance($branchType, @('Start', '开始')))
    [void]$branches.Add([Activator]::CreateInstance($branchType, @('Stop', '停止')))
    $entry = [Activator]::CreateInstance($entryType, @('bgm', 'BGM', 'BGM', '音频', [Collections.Generic.List[string]]::new(), ''))
    $entryType.GetProperty('Branches').SetValue($entry, $branches)
    $entries = [Activator]::CreateInstance($entriesType)
    [void]$entries.Add($entry)
    $registered = $serviceType.GetMethod('IsRegisteredFunctionValue')
    if (!$registered.Invoke($null, @('BGM_Start', $entries)) -or
        $registered.Invoke($null, @('BGMStart', $entries)) -or
        $registered.Invoke($null, @('BGM_Pause', $entries))) {
        throw 'Only registered branch values may be created by the story editor.'
    }
}

Write-Host 'Story function-registry checks passed.'
