$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$chapterSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Workbench.Chapters.cs')
$workflowPath = Join-Path $root 'MainWindow.Workbench.AiStoryFill.cs'
$factoryPath = Join-Path $root 'Views\AiStoryFillDialogContentFactory.cs'

if ($chapterSource -notmatch 'AI 剧情填表' -or $chapterSource -notmatch 'ShowAiStoryFillWorkflowAsync') {
    throw 'Chapter context menu does not expose the AI story-fill workflow.'
}
if (!(Test-Path -LiteralPath $workflowPath) -or !(Test-Path -LiteralPath $factoryPath)) {
    throw 'AI story-fill UI is not separated into workflow and content-factory files.'
}

$workflow = Get-Content -Raw -Encoding UTF8 -LiteralPath $workflowPath
$factory = Get-Content -Raw -Encoding UTF8 -LiteralPath $factoryPath
foreach ($required in @(
    'CreateTaskPackage',
    'LoadImportPreview',
    'ApplyImport',
    'FolderPicker',
    'ProtocolVersion',
    'SourceStartParagraph',
    'ShowFloatingTip'
)) {
    if ($workflow -notmatch [regex]::Escape($required)) {
        throw "AI story-fill workflow is missing $required."
    }
}
if ($workflow -match 'PromptTextAsync') {
    throw 'AI story-fill export must not open a second paragraph prompt.'
}
foreach ($required in @('协议版本', 'V1', 'V2', 'V3', '原稿起点（可选）', '导入填写结果', 'TalkChar', 'Tesxt', '待人工确认', '重复')) {
    if ($factory -notmatch [regex]::Escape($required)) {
        throw "AI story-fill dialog content is missing $required."
    }
}

Write-Host 'AI story-fill UI checks passed.'
