$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$models = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Models\AssetLibraryModels.cs')
$service = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Services\StoryFunctionService.cs')
$loading = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.AssetLibrary.Loading.cs')
if ($models -notmatch 'SchemaVersion[\s\S]*?FunctionBranch[\s\S]*?Code[\s\S]*?Remark[\s\S]*?Branches') {
    throw 'Function catalog v2 models are missing.'
}
if ($service -notmatch 'BuildCollectedFunctionEntries[\s\S]*?BGMStart[\s\S]*?Choice[\s\S]*?IntoChapter') {
    throw 'Function catalog collection rules are missing.'
}
$defaultBlock = [regex]::Match($service, 'CreateDefaultFunctions\(\)[\s\S]*?public static FunctionEntry CreateChoiceFunctionTemplate').Value
if ($defaultBlock -match 'default-custom|纯自定义函数') {
    throw 'The built-in unrestricted custom function must be removed.'
}
if ($loading -notmatch 'CollectFunctionsFromChapters[\s\S]*?_storyDocumentRepository') {
    throw 'Asset-library loading must collect functions from linked projects.'
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $type = $assembly.GetType('GalExcleTools.Services.StoryFunctionService', $true)
    $indexType = $assembly.GetType('GalExcleTools.FunctionIndex', $true)
    $legacyIndex = [Text.Json.JsonSerializer]::Deserialize('{"Entries":[]}', $indexType)
    if ($indexType.GetProperty('SchemaVersion').GetValue($legacyIndex) -ne 0) {
        throw 'A function index without SchemaVersion must be detected as legacy for backup.'
    }
    $values = [Collections.Generic.List[string]]::new()
    foreach ($value in @(
        'BGM_Start', 'BGM_Stop', 'BGMStart', 'BGMStop',
        'VFXON_Wind', 'VFXON_Sunshine', 'TitleShowMode', 'PhoneMessageBurst',
        'M2-00-Choice1', 'IntoChapter_M2', 'IntoSegment_03')) {
        [void]$values.Add($value)
    }
    $collectArgs = [object[]]::new(1)
    $collectArgs[0] = $values.PSObject.BaseObject
    $entries = @($type.GetMethod('BuildCollectedFunctionEntries').Invoke($null, $collectArgs))
    $byCode = @{}
    foreach ($entry in $entries) { $byCode[$entry.Indicator] = $entry }
    if (!$byCode.ContainsKey('BGM') -or
        (@($byCode['BGM'].Branches.Code | Sort-Object) -join ',') -ne 'Start,Stop' -or
        !$byCode.ContainsKey('VFXON') -or
        (@($byCode['VFXON'].Branches.Code | Sort-Object) -join ',') -ne 'Sunshine,Wind' -or
        !$byCode.ContainsKey('TitleShowMode') -or $byCode['TitleShowMode'].Branches.Count -ne 0 -or
        !$byCode.ContainsKey('PhoneMessageBurst') -or
        $byCode.ContainsKey('BGMStart') -or $byCode.ContainsKey('BGMStop') -or
        @($entries | Where-Object Indicator -match 'Choice|IntoChapter|IntoSegment').Count -ne 0) {
        throw 'Collected function families do not match the normalized catalog rules.'
    }
}

Write-Host 'Function catalog v2 checks passed.'
