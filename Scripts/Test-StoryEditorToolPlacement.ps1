$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$xamlPath = Join-Path $root 'MainWindow.xaml'
$xaml = Get-Content -Raw -Encoding UTF8 -LiteralPath $xamlPath
$issues = [System.Collections.Generic.List[string]]::new()

$toolsStart = $xaml.IndexOf('x:Name="StoryInspectorToolsScrollViewer"', [StringComparison]::Ordinal)
$canvasStart = $xaml.IndexOf('x:Name="StoryEditorCanvasHost"', [StringComparison]::Ordinal)
$tipsStart = $xaml.IndexOf('x:Name="StoryFloatingTipsPanel"', [StringComparison]::Ordinal)
if ($toolsStart -lt 0 -or $canvasStart -le $toolsStart -or $tipsStart -le $canvasStart) {
    $issues.Add('Could not isolate the story tools and bottom-editor XAML regions.')
}
else {
    $toolsBlock = $xaml.Substring($toolsStart, $canvasStart - $toolsStart)
    $editorBlock = $xaml.Substring($canvasStart, $tipsStart - $canvasStart)

    if ($toolsBlock -notmatch 'Header="行与小节"[\s\S]*?AddSectionCommand[\s\S]*?DeleteRowCommand') {
        $issues.Add('Create-section and delete-row commands must live in the collapsed tools panel.')
    }
    if ($editorBlock -match 'AddSectionCommand|DeleteRowCommand') {
        $issues.Add('Low-frequency create-section and delete-row commands must be removed from the bottom editor controls.')
    }
    if ($editorBlock -notmatch '<ColumnDefinition Width="224"\s*/>') {
        $issues.Add('The bottom navigation column should shrink to 224 pixels to give text editing more width.')
    }
    if ($editorBlock -notmatch 'InsertStoryRowBeforeButton[\s\S]*?InsertStoryRowAfterButton') {
        $issues.Add('The bottom editor must retain both left and right insertion commands.')
    }
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Story editor tool-placement checks passed.'
