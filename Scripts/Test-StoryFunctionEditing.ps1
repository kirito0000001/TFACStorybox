$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $repoRoot 'Services\StoryFunctionListService.cs'
$functionServicePath = Join-Path $repoRoot 'Services\StoryFunctionService.cs'
$issues = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $servicePath)) {
    $issues.Add('StoryFunctionListService is missing.')
}
else {
    $source = Get-Content -Raw -Encoding UTF8 -LiteralPath $servicePath
    foreach ($requirement in @(
        @{ Pattern = 'GetValues'; Message = 'Function list reads must be centralized.' },
        @{ Pattern = 'Move'; Message = 'Function list must support ordered moves.' },
        @{ Pattern = 'InsertAfter'; Message = 'Function paste must insert after the selection.' },
        @{ Pattern = 'RemoveAt'; Message = 'Function removal must use the shared service.' },
        @{ Pattern = 'CopyAt'; Message = 'Function copy must preserve the exact token.' }
    )) {
        if ($source -notmatch $requirement.Pattern) {
            $issues.Add($requirement.Message)
        }
    }
}

if (-not (Test-Path -LiteralPath $functionServicePath) -or
    (Get-Content -Raw -Encoding UTF8 -LiteralPath $functionServicePath) -notmatch 'OrderByFixedUsage[\s\S]*?BGM[\s\S]*?VFX[\s\S]*?BGLerpMode[\s\S]*?Scene_[\s\S]*?TransAnim') {
    $issues.Add('Function templates must expose the fixed two-chapter usage order.')
}

$viewModelPath = Join-Path $repoRoot 'ViewModels\StoryEditorViewModel.cs'
$xamlPath = Join-Path $repoRoot 'MainWindow.xaml'
$interactionPath = Join-Path $repoRoot 'MainWindow.Story.Interaction.cs'
$windowFunctionPath = Join-Path $repoRoot 'MainWindow.Story.Functions.cs'
$editorDialogPath = Join-Path $repoRoot 'Views\EditorDialogContentFactory.cs'
if ((Get-Content -Raw -Encoding UTF8 -LiteralPath $viewModelPath) -notmatch 'ObservableCollection<StoryFunctionListItem>[\s\S]*?SelectedFunctionIndex[\s\S]*?MoveFunctionUpCommand[\s\S]*?CopyFunctionCommand[\s\S]*?PasteFunctionCommand') {
    $issues.Add('Story function list selection and commands are missing from the view model.')
}
if ((Get-Content -Raw -Encoding UTF8 -LiteralPath $xamlPath) -notmatch 'x:Name="StoryFunctionListView"[\s\S]*?CanReorderItems="True"[\s\S]*?DragItemsCompleted="StoryFunctionListView_DragItemsCompleted"') {
    $issues.Add('The story function list must support direct selection and drag reordering.')
}
if ((Get-Content -Raw -Encoding UTF8 -LiteralPath $interactionPath) -notmatch 'IsStoryFunctionListInteraction[\s\S]*?VirtualKey\.C[\s\S]*?CopySelectedStoryFunction[\s\S]*?VirtualKey\.V[\s\S]*?PasteStoryFunctionAsync') {
    $issues.Add('Ctrl+C and Ctrl+V must be scoped to the story function list.')
}
if ((Get-Content -Raw -Encoding UTF8 -LiteralPath $windowFunctionPath) -notmatch 'StoryFunctionListView_DragItemsCompleted[\s\S]*?ApplyOrder[\s\S]*?QueueCurrentStoryDocumentSave') {
    $issues.Add('Drag reorder must write the final function order in one save transaction.')
}
if ((Get-Content -Raw -Encoding UTF8 -LiteralPath $editorDialogPath) -notmatch 'ComboBox _categoryBox[\s\S]*?FunctionCategories[\s\S]*?Expander[\s\S]*?IsExpanded = false') {
    $issues.Add('Function template settings must use fixed categories and collapse optional Choice notes by default.')
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

$assemblyPath = Join-Path $repoRoot 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Debug assembly not found: $assemblyPath"
}

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
$rowType = $assembly.GetType('GalExcleTools.StoryRow', $true)
$serviceType = $assembly.GetType('GalExcleTools.Services.StoryFunctionListService', $true)
$row = [System.Activator]::CreateInstance($rowType, $true)
$set = $rowType.GetMethod('Set')
$get = $rowType.GetMethod('Get')
[void]$set.Invoke($row, @('Custom', 'BGM_Start/VFXON_Fog/PhoneMessageBurst'))

$publicStatic = [System.Reflection.BindingFlags]'Public,Static'
$move = $serviceType.GetMethod('Move', $publicStatic)
$insertAfter = $serviceType.GetMethod('InsertAfter', $publicStatic)
$copyAt = $serviceType.GetMethod('CopyAt', $publicStatic)
$removeAt = $serviceType.GetMethod('RemoveAt', $publicStatic)
$applyOrder = $serviceType.GetMethod('ApplyOrder', $publicStatic)

if (-not [bool]$move.Invoke($null, @($row, 2, 0))) {
    throw 'Move should report a change.'
}
$copied = [string]$copyAt.Invoke($null, @($row, 1))
[void]$insertAfter.Invoke($null, @($row, 1, $copied))
$removed = [string]$removeAt.Invoke($null, @($row, 2))
$actual = [string]$get.Invoke($row, @('Custom'))
$expected = 'PhoneMessageBurst/BGM_Start/VFXON_Fog'
if ($copied -ne 'BGM_Start' -or $removed -ne 'BGM_Start' -or $actual -ne $expected) {
    throw "Function editing changed token data. Copy=$copied Removed=$removed Actual=$actual"
}

$orderedValues = [System.Collections.Generic.List[string]]::new()
@('VFXON_Fog', 'PhoneMessageBurst', 'BGM_Start') | ForEach-Object { [void]$orderedValues.Add($_) }
$orderArguments = [object[]]::new(2)
$orderArguments[0] = $row
$orderArguments[1] = $orderedValues
if (-not [bool]$applyOrder.Invoke($null, $orderArguments) -or
    [string]$get.Invoke($row, @('Custom')) -ne 'VFXON_Fog/PhoneMessageBurst/BGM_Start') {
    throw 'Drag order application failed.'
}

$entryType = $assembly.GetType('GalExcleTools.FunctionEntry', $true)
$entryListType = [System.Collections.Generic.List``1].MakeGenericType($entryType)
$entries = [System.Activator]::CreateInstance($entryListType)
$entryConstructor = $entryType.GetConstructors() | Where-Object { $_.GetParameters().Count -eq 5 } | Select-Object -First 1
$emptyNotes = [System.Collections.Generic.List[string]]::new()
foreach ($spec in @(
    @('custom', 'CustomFunction'),
    @('jump', 'IntoChapter_M2'),
    @('media', 'MedPlay_'),
    @('choice', 'M2-00-Choice1'),
    @('title', 'TitleShowMode'),
    @('anim', 'TransAnim_'),
    @('scene', 'Scene_'),
    @('background', 'BGLerpMode_'),
    @('vfx', 'VFXON_'),
    @('bgm', 'BGM_Start')
)) {
    $entry = $entryConstructor.Invoke(@($spec[0], $spec[0], $spec[1], 'test', $emptyNotes))
    [void]$entries.Add($entry)
}

$functionServiceType = $assembly.GetType('GalExcleTools.Services.StoryFunctionService', $true)
$templateOrderArguments = [object[]]::new(1)
$templateOrderArguments[0] = $entries
$orderedEntries = $functionServiceType.GetMethod('OrderByFixedUsage', $publicStatic).Invoke($null, $templateOrderArguments)
$indicatorProperty = $entryType.GetProperty('Indicator')
$actualOrder = @($orderedEntries | ForEach-Object { $indicatorProperty.GetValue($_) })
$expectedOrder = @('BGM_Start', 'VFXON_', 'BGLerpMode_', 'Scene_', 'TransAnim_', 'TitleShowMode', 'M2-00-Choice1', 'MedPlay_', 'IntoChapter_M2', 'CustomFunction')
if (Compare-Object $expectedOrder $actualOrder -SyncWindow 0) {
    throw "Fixed function order is incorrect: $($actualOrder -join ',')"
}

Write-Host 'Story function editing tests passed.'
