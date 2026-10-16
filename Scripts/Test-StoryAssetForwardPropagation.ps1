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
$buildLayerPlanMethod = $serviceType.GetMethod(
    'BuildForwardContiguousCharacterLayerPlan',
    [System.Reflection.BindingFlags]'Public,Static')
$buildValuePlanMethod = $serviceType.GetMethod(
    'BuildForwardContiguousValuePlan',
    [System.Reflection.BindingFlags]'Public,Static')
$applyValuePlanMethod = $serviceType.GetMethod(
    'ApplyForwardContiguousValuePlan',
    [System.Reflection.BindingFlags]'Public,Static')
$buildSlotPlanMethod = $serviceType.GetMethod(
    'BuildForwardContiguousCharacterSlotPlan',
    [System.Reflection.BindingFlags]'Public,Static')
$applySlotPlanMethod = $serviceType.GetMethod(
    'ApplyForwardContiguousCharacterSlotPlan',
    [System.Reflection.BindingFlags]'Public,Static')
$hasSlotChangeMethod = $serviceType.GetMethod(
    'HasCharacterSlotChange',
    [System.Reflection.BindingFlags]'Public,Static')
if ($null -eq $buildPlanMethod -or
    $null -eq $applyPlanMethod -or
    $null -eq $buildLayerPlanMethod -or
    $null -eq $buildValuePlanMethod -or
    $null -eq $applyValuePlanMethod -or
    $null -eq $buildSlotPlanMethod -or
    $null -eq $applySlotPlanMethod -or
    $null -eq $hasSlotChangeMethod) {
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

$filterRows = [System.Activator]::CreateInstance($listType)
foreach ($value in @('2', '2', '2', '4')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Vfx1', $value))
    [void]$filterRows.Add($row)
}

$filterPlan = $buildPlanMethod.Invoke($null, @($filterRows, 0, 'Vfx1', 3))
$filterChangedCount = $applyPlanMethod.Invoke($null, @($filterRows, $filterPlan))
$actualFilters = @($filterRows | ForEach-Object { $getMethod.Invoke($_, @('Vfx1')) })
if ([int]$filterChangedCount -ne 3 -or (Compare-Object @('3', '3', '3', '4') $actualFilters -SyncWindow 0)) {
    throw "Character-filter propagation failed. Count=$filterChangedCount Values=$($actualFilters -join ',')"
}

$talkFilterRows = [System.Activator]::CreateInstance($listType)
foreach ($value in @('1', '1')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('TalkVfx', $value))
    [void]$talkFilterRows.Add($row)
}
[void]$buildLayerPlanMethod.Invoke($null, @($talkFilterRows, 0, 'TalkVfx', 2, 'TalkChar'))

$layerRows = [System.Activator]::CreateInstance($listType)
foreach ($entry in @(
    @('A', '1'),
    @('A', '1'),
    @('B', '1'),
    @('A', '1'))) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', $entry[0]))
    [void]$setMethod.Invoke($row, @('Body1', $entry[1]))
    [void]$layerRows.Add($row)
}
$layerPlan = $buildLayerPlanMethod.Invoke($null, @($layerRows, 0, 'Body1', 2, 'Chara1'))
$layerChangedCount = $applyPlanMethod.Invoke($null, @($layerRows, $layerPlan))
$actualLayers = @($layerRows | ForEach-Object { $getMethod.Invoke($_, @('Body1')) })
if ([int]$layerChangedCount -ne 2 -or (Compare-Object @('2', '2', '1', '1') $actualLayers -SyncWindow 0)) {
    throw "Character-layer propagation crossed a character boundary. Count=$layerChangedCount Values=$($actualLayers -join ',')"
}

$faceRows = [System.Activator]::CreateInstance($listType)
foreach ($entry in @(
    @('1', '1'),
    @('1', '1'),
    @('2', '1'))) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', 'A'))
    [void]$setMethod.Invoke($row, @('Body1', $entry[0]))
    [void]$setMethod.Invoke($row, @('Face1', $entry[1]))
    [void]$faceRows.Add($row)
}
$facePlan = $buildLayerPlanMethod.Invoke($null, @($faceRows, 0, 'Face1', 2, 'Chara1'))
if ([int]$facePlan.GetType().GetProperty('Count').GetValue($facePlan) -ne 2) {
    throw 'Expression propagation must stop when clothing changes.'
}

$staleLayerRows = [System.Activator]::CreateInstance($listType)
foreach ($value in @('A', 'A')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', $value))
    [void]$setMethod.Invoke($row, @('Vfx1', '1'))
    [void]$staleLayerRows.Add($row)
}
$staleLayerPlan = $buildLayerPlanMethod.Invoke($null, @($staleLayerRows, 0, 'Vfx1', 2, 'Chara1'))
[void]$setMethod.Invoke($staleLayerRows[1], @('Chara1', 'B'))
$staleLayerRejected = $false
try {
    [void]$applyPlanMethod.Invoke($null, @($staleLayerRows, $staleLayerPlan))
}
catch {
    $exception = $_.Exception
    while ($null -ne $exception) {
        if ($exception -is [System.InvalidOperationException]) {
            $staleLayerRejected = $true
            break
        }
        $exception = $exception.InnerException
    }
}
if (-not $staleLayerRejected) {
    throw 'Applying a character-layer plan after its character range changed must be rejected.'
}

$characterRows = [System.Activator]::CreateInstance($listType)
foreach ($value in @('A', 'A', 'B', 'A')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara2', $value))
    [void]$characterRows.Add($row)
}
$characterPlan = $buildValuePlanMethod.Invoke($null, @($characterRows, 0, 'Chara2', 'C'))
$characterChangedCount = $applyValuePlanMethod.Invoke($null, @($characterRows, $characterPlan))
$actualCharacters = @($characterRows | ForEach-Object { $getMethod.Invoke($_, @('Chara2')) })
if ([int]$characterChangedCount -ne 2 -or (Compare-Object @('C', 'C', 'B', 'A') $actualCharacters -SyncWindow 0)) {
    throw "Character propagation failed. Count=$characterChangedCount Values=$($actualCharacters -join ',')"
}

# 立绘位 Ctrl+V：范围要按**角色**划，载荷是**整份立绘数据**。
# 2026-10-16 用户报的「Ctrl+V 无法连续更改」就是这里：同角色、只有服装/表情不同时，
# 角色列没有变化，只算角色列的话计划是 0 句 → 后面一句都不动。
$clipboardType = $assembly.GetType('GalExcleTools.StoryCharacterSlotClipboard', $true)
$slotClipboard = [System.Activator]::CreateInstance($clipboardType, @('Xia', '4', '3', '0', '0'))
$slotRows = [System.Activator]::CreateInstance($listType)
foreach ($body in @('4', '1', '1', '2')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', 'Xia'))
    [void]$setMethod.Invoke($row, @('Body1', $body))
    [void]$setMethod.Invoke($row, @('Face1', '3'))
    [void]$setMethod.Invoke($row, @('Adorn1', '0'))
    [void]$setMethod.Invoke($row, @('Vfx1', '0'))
    [void]$slotRows.Add($row)
}

$slotPlan = $buildSlotPlanMethod.Invoke($null, @($slotRows, 0, 1, $slotClipboard))
if ([int]$slotPlan.GetType().GetProperty('Count').GetValue($slotPlan) -ne 4) {
    throw 'Character-slot paste must cover the whole same-character run.'
}
if (-not [bool]$hasSlotChangeMethod.Invoke($null, @($slotRows, $slotPlan))) {
    throw 'Character-slot paste must notice that later rows carry different layers.'
}

$slotChangedCount = $applySlotPlanMethod.Invoke($null, @($slotRows, $slotPlan))
$actualSlots = @($slotRows | ForEach-Object { "$($getMethod.Invoke($_, @('Body1')))/$($getMethod.Invoke($_, @('Face1')))" })
if ([int]$slotChangedCount -ne 3 -or (Compare-Object @('4/3', '4/3', '4/3', '4/3') $actualSlots -SyncWindow 0)) {
    throw "Character-slot forward paste failed. Count=$slotChangedCount Values=$($actualSlots -join ',')"
}

$mixedSlotRows = [System.Activator]::CreateInstance($listType)
foreach ($character in @('Xia', 'Xia', 'Diao')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', $character))
    [void]$setMethod.Invoke($row, @('Body1', '1'))
    [void]$mixedSlotRows.Add($row)
}
$mixedSlotPlan = $buildSlotPlanMethod.Invoke($null, @($mixedSlotRows, 0, 1, $slotClipboard))
if ([int]$mixedSlotPlan.GetType().GetProperty('Count').GetValue($mixedSlotPlan) -ne 2) {
    throw 'Character-slot paste must stop where the character changes.'
}

$unsupportedRejected = $false
try {
    [void]$buildPlanMethod.Invoke($null, @($rows, 0, 'Name', 4))
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
    throw 'Non-asset fields must not support forward propagation.'
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

$assetSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot '..\MainWindow.Story.Assets.cs')
if ($assetSource -notmatch 'ChooseStoryCharacterAsync[\s\S]*?BuildForwardContiguousValuePlan[\s\S]*?ApplyForwardContiguousValuePlan') {
    throw 'Character selection must use the shared forward replacement workflow.'
}
if ($assetSource -notmatch 'ChooseStoryCharacterLayerAsync[\s\S]*?BuildForwardContiguousCharacterLayerPlan[\s\S]*?ApplyForwardContiguousPlan') {
    throw 'Clothing, expression, adornment, and VFX selection must use the shared forward replacement workflow.'
}
if ($assetSource -notmatch 'ApplyForwardContiguousValuePlan[\s\S]*?ResetLayerColumns') {
    throw 'Replacing a character range must reset stale clothing, expression, adornment, and VFX values.'
}

# Ctrl+V 两条路各自该走哪套计划（2026-10-16 两条都修过，别再退回去）。
$interactionSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot '..\MainWindow.Story.Interaction.cs')
if ($interactionSource -notmatch 'PasteStoryCharacterSlotAsync[\s\S]*?BuildForwardContiguousCharacterSlotPlan[\s\S]*?ApplyForwardContiguousCharacterSlotPlan') {
    throw 'Pasting a portrait slot must use the character-slot plan (range by character, payload = whole 立绘数据).'
}
if ($interactionSource -notmatch 'PasteStoryAssetFieldAsync[\s\S]*?BuildForwardContiguousPlan[\s\S]*?ApplyForwardContiguousPlan') {
    throw 'Pasting an asset-index field must use the asset-index plan.'
}
# 值计划只认 TalkChar / Chara1-5；素材索引列喂进去会当场抛（素材栏 Ctrl+V 必崩那条的根因）。
if ($interactionSource -match 'BuildForwardContiguousValuePlan\s*\(') {
    throw 'Asset-index pasting must not call the character value plan.'
}

Write-Host 'Story asset forward propagation tests passed.'
