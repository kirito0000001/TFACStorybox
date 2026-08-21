$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $PSScriptRoot '..\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Debug assembly not found: $assemblyPath"
}

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
$rowType = $assembly.GetType('GalExcleTools.StoryRow', $true)
$serviceType = $assembly.GetType('GalExcleTools.Services.StoryAssetFieldService', $true)
$replaceMethod = $serviceType.GetMethod(
    'ReplaceForwardContiguousIndex',
    [System.Reflection.BindingFlags]'Public,Static')
if ($null -eq $replaceMethod) {
    throw 'ReplaceForwardContiguousIndex is missing.'
}

$setMethod = $rowType.GetMethod('Set')
$getMethod = $rowType.GetMethod('Get')
$listType = [System.Collections.Generic.List``1].MakeGenericType($rowType)
$rows = [System.Activator]::CreateInstance($listType)
foreach ($value in @('13', '13', '13', '8', '13')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('BGM', $value))
    [void]$rows.Add($row)
}

$changedCount = $replaceMethod.Invoke($null, @($rows, 1, 'BGM', 21))
$actual = @($rows | ForEach-Object { $getMethod.Invoke($_, @('BGM')) })
$expected = @('13', '21', '21', '8', '13')
if ([int]$changedCount -ne 2 -or (Compare-Object $expected $actual -SyncWindow 0)) {
    throw "Forward propagation failed. Count=$changedCount Values=$($actual -join ',')"
}

$unsupportedRejected = $false
try {
    [void]$replaceMethod.Invoke($null, @($rows, 0, 'TalkBody', 4))
}
catch {
    $exception = $_.Exception
    while ($null -ne $exception) {
        if ($exception -is [System.InvalidOperationException]) {
            $unsupportedRejected = $true
            break
        }

        $exception = $exception.InnerException
    }
}

if (-not $unsupportedRejected) {
    throw 'Portrait fields must not support forward propagation.'
}

Write-Host 'Story asset forward propagation tests passed.'
