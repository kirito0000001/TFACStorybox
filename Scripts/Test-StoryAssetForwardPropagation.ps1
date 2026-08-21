$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $PSScriptRoot '..\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Debug assembly not found: $assemblyPath"
}

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
$rowType = $assembly.GetType('GalExcleTools.StoryRow', $true)
$serviceType = $assembly.GetType('GalExcleTools.Services.StoryAssetFieldService', $true)
$buildPlanMethod = $serviceType.GetMethod(
    'BuildForwardContiguousPlan',
    [System.Reflection.BindingFlags]'Public,Static')
$applyPlanMethod = $serviceType.GetMethod(
    'ApplyForwardContiguousPlan',
    [System.Reflection.BindingFlags]'Public,Static')
if ($null -eq $buildPlanMethod -or $null -eq $applyPlanMethod) {
    throw 'Forward replacement must expose separate build/apply operations.'
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

$plan = $buildPlanMethod.Invoke($null, @($rows, 1, 'BGM', 21))
$unchangedBeforeApply = @($rows | ForEach-Object { $getMethod.Invoke($_, @('BGM')) })
if (Compare-Object @('13', '13', '13', '8', '13') $unchangedBeforeApply -SyncWindow 0) {
    throw 'Building a replacement plan must not mutate story rows.'
}

$planType = $plan.GetType()
if ([int]$planType.GetProperty('StartIndex').GetValue($plan) -ne 1 -or
    [int]$planType.GetProperty('EndIndex').GetValue($plan) -ne 2 -or
    [int]$planType.GetProperty('Count').GetValue($plan) -ne 2) {
    throw 'Forward replacement plan calculated the wrong row range.'
}

$changedCount = $applyPlanMethod.Invoke($null, @($rows, $plan))
$actual = @($rows | ForEach-Object { $getMethod.Invoke($_, @('BGM')) })
$expected = @('13', '21', '21', '8', '13')
if ([int]$changedCount -ne 2 -or (Compare-Object $expected $actual -SyncWindow 0)) {
    throw "Forward propagation failed. Count=$changedCount Values=$($actual -join ',')"
}

$unsupportedRejected = $false
try {
    [void]$buildPlanMethod.Invoke($null, @($rows, 0, 'TalkBody', 4))
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

$stalePlan = $buildPlanMethod.Invoke($null, @($rows, 1, 'BGM', 5))
[void]$setMethod.Invoke($rows[2], @('BGM', '99'))
$staleRejected = $false
try {
    [void]$applyPlanMethod.Invoke($null, @($rows, $stalePlan))
}
catch {
    $exception = $_.Exception
    while ($null -ne $exception) {
        if ($exception -is [System.InvalidOperationException]) {
            $staleRejected = $true
            break
        }
        $exception = $exception.InnerException
    }
}
if (-not $staleRejected) {
    throw 'Applying a stale replacement plan must be rejected.'
}

Write-Host 'Story asset forward propagation tests passed.'
