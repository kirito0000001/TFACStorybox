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

# 立绘位 Ctrl+V：范围按**整份立绘数据**划（角色/服装/表情/装饰/滤镜任一列不同就停），
# 载荷是整份立绘数据。这一条口径改过两轮，别再退回去：
#   · 只比角色列 → 同角色、只是服装/表情不同时计划 0 句，Ctrl+V 等于没反应（用户 2026-10-16 上午）；
#   · 只看角色列又太宽 → 会刷过服装/表情已经变了的句子，把后面手改的表情覆盖
#     （同一天下午用户报的「服装或者表情有变化也得停下」）。
$clipboardType = $assembly.GetType('GalExcleTools.StoryCharacterSlotClipboard', $true)
$slotClipboard = [System.Activator]::CreateInstance($clipboardType, @('Xia', '4', '3', '0', '0'))

# ① 一模一样的两句 + 第三句服装不同：只能覆盖前两句，停止原因报"服装"
$slotRows = [System.Activator]::CreateInstance($listType)
foreach ($body in @('1', '1', '2', '1')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', 'Xia'))
    [void]$setMethod.Invoke($row, @('Body1', $body))
    [void]$setMethod.Invoke($row, @('Face1', '1'))
    [void]$setMethod.Invoke($row, @('Adorn1', '0'))
    [void]$setMethod.Invoke($row, @('Vfx1', '0'))
    [void]$slotRows.Add($row)
}

$slotPlan = $buildSlotPlanMethod.Invoke($null, @($slotRows, 0, 1, $slotClipboard))
$slotPlanType = $slotPlan.GetType()
if ([int]$slotPlanType.GetProperty('Count').GetValue($slotPlan) -ne 2) {
    throw 'Character-slot paste must stop before the clothing changes.'
}
if ([string]$slotPlanType.GetProperty('StoppingFieldLabel').GetValue($slotPlan) -ne '服装') {
    throw 'Character-slot paste must report which column stopped the range.'
}
if (-not [bool]$hasSlotChangeMethod.Invoke($null, @($slotRows, $slotPlan))) {
    throw 'Character-slot paste must notice that the clipboard differs from the range.'
}

$slotChangedCount = $applySlotPlanMethod.Invoke($null, @($slotRows, $slotPlan))
$actualSlots = @($slotRows | ForEach-Object { "$($getMethod.Invoke($_, @('Body1')))/$($getMethod.Invoke($_, @('Face1')))" })
if ([int]$slotChangedCount -ne 2 -or (Compare-Object @('4/3', '4/3', '2/1', '1/1') $actualSlots -SyncWindow 0)) {
    throw "Character-slot forward paste failed. Count=$slotChangedCount Values=$($actualSlots -join ',')"
}

# ② 表情不同也要停
$faceSlotRows = [System.Activator]::CreateInstance($listType)
foreach ($face in @('1', '5')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', 'Xia'))
    [void]$setMethod.Invoke($row, @('Body1', '1'))
    [void]$setMethod.Invoke($row, @('Face1', $face))
    [void]$setMethod.Invoke($row, @('Adorn1', '0'))
    [void]$setMethod.Invoke($row, @('Vfx1', '0'))
    [void]$faceSlotRows.Add($row)
}
$faceSlotPlan = $buildSlotPlanMethod.Invoke($null, @($faceSlotRows, 0, 1, $slotClipboard))
if ([int]$faceSlotPlan.GetType().GetProperty('Count').GetValue($faceSlotPlan) -ne 1 -or
    [string]$faceSlotPlan.GetType().GetProperty('StoppingFieldLabel').GetValue($faceSlotPlan) -ne '表情') {
    throw 'Character-slot paste must stop before the expression changes.'
}

# ③ 角色不同也要停
$mixedSlotRows = [System.Activator]::CreateInstance($listType)
foreach ($character in @('Xia', 'Xia', 'Diao')) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', $character))
    [void]$setMethod.Invoke($row, @('Body1', '1'))
    [void]$setMethod.Invoke($row, @('Face1', '1'))
    [void]$setMethod.Invoke($row, @('Adorn1', '0'))
    [void]$setMethod.Invoke($row, @('Vfx1', '0'))
    [void]$mixedSlotRows.Add($row)
}
$mixedSlotPlan = $buildSlotPlanMethod.Invoke($null, @($mixedSlotRows, 0, 1, $slotClipboard))
if ([int]$mixedSlotPlan.GetType().GetProperty('Count').GetValue($mixedSlotPlan) -ne 2) {
    throw 'Character-slot paste must stop where the character changes.'
}

# ④ 整段本来就等于剪贴板：没有要改的（UI 走"立绘数据相同"提示，不弹窗、不写）
$sameSlotRows = [System.Activator]::CreateInstance($listType)
foreach ($unused in @(0, 1)) {
    $row = [System.Activator]::CreateInstance($rowType, $true)
    [void]$setMethod.Invoke($row, @('Chara1', 'Xia'))
    [void]$setMethod.Invoke($row, @('Body1', '4'))
    [void]$setMethod.Invoke($row, @('Face1', '3'))
    [void]$setMethod.Invoke($row, @('Adorn1', '0'))
    [void]$setMethod.Invoke($row, @('Vfx1', '0'))
    [void]$sameSlotRows.Add($row)
}
$sameSlotPlan = $buildSlotPlanMethod.Invoke($null, @($sameSlotRows, 0, 1, $slotClipboard))
if ([bool]$hasSlotChangeMethod.Invoke($null, @($sameSlotRows, $sameSlotPlan)) -or
    [int]$applySlotPlanMethod.Invoke($null, @($sameSlotRows, $sameSlotPlan)) -ne 0) {
    throw 'Character-slot paste must leave an identical range untouched.'
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
