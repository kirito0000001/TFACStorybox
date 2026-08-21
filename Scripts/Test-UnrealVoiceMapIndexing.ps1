$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $PSScriptRoot '..\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Debug assembly not found: $assemblyPath"
}

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
$serviceType = $assembly.GetType('GalExcleTools.Services.UnrealSyncService', $true)
$method = $serviceType.GetMethod(
    'BuildSparseVoiceWaveSlots',
    [System.Reflection.BindingFlags]'Public,Static')
if ($null -eq $method) {
    throw 'BuildSparseVoiceWaveSlots is missing.'
}

$rowNames = [System.Collections.Generic.List[string]]::new()
foreach ($rowNumber in 1..365) {
    $rowNames.Add($rowNumber.ToString())
}

$wavesByRowName = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$wavesByRowName['12'] = '/Game/Voice/Vo-012.Vo-012'
$wavesByRowName['318'] = '/Game/Voice/Vo-318.Vo-318'

$slots = $method.Invoke($null, [object[]]@($rowNames, $wavesByRowName))
if ($slots.Count -ne 366) {
    throw "Sparse voice array must retain slot 0 and all 365 story indexes. Actual=$($slots.Count)"
}
if ($null -ne $slots[0] -or $slots[12] -ne $wavesByRowName['12'] -or $slots[318] -ne $wavesByRowName['318'] -or $null -ne $slots[365]) {
    throw 'Sparse voice array did not preserve the original story-row indexes and empty gaps.'
}

Write-Host 'Unreal voice-map indexing tests passed.'
