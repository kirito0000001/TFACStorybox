$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$xaml = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.xaml')
$code = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Story.ChoiceEditor.cs') -ErrorAction SilentlyContinue
$factory = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Views\StoryDialogContentFactory.cs')
$issues = [System.Collections.Generic.List[string]]::new()

if ($factory -match 'ChoiceFunctionNoteDialogContent') {
    $issues.Add('The discarded ContentDialog choice editor must be removed completely.')
}
if ($xaml -notmatch 'x:Name="StoryChoiceEditorHost"[\s\S]*?Width="920"[\s\S]*?<ColumnDefinition Width="300"[\s\S]*?<ColumnDefinition Width="\*"') {
    $issues.Add('The replacement must use an independent 920-pixel two-pane overlay.')
}
if ($xaml -notmatch 'StoryChoiceEditorListView[\s\S]*?StoryChoiceEditorDetailTitle[\s\S]*?StoryChoiceEditorTextBox[\s\S]*?StoryChoiceEditorTargetComboBox') {
    $issues.Add('The overlay must separate option selection from detail editing.')
}
if ($xaml -notmatch 'StoryChoiceEditorAddButton[\s\S]*?StoryChoiceEditorDeleteButton[\s\S]*?StoryChoiceEditorConfirmButton[\s\S]*?StoryChoiceEditorCancelButton') {
    $issues.Add('The overlay must expose complete add, delete, confirm, and cancel commands.')
}
if ($xaml -notmatch 'StoryChoiceEditorCard[\s\S]*?<RowDefinition Height="88"[\s\S]*?StoryChoiceEditorCodeText') {
    $issues.Add('The overlay header must leave enough height for the Choice code.')
}
if ($xaml -notmatch '<ColumnDefinition Width="180"\s*/>[\s\S]*?<ColumnDefinition Width="180"\s*/>[\s\S]*?StoryChoiceEditorConfirmButton[\s\S]*?HorizontalAlignment="Stretch"[\s\S]*?StoryChoiceEditorCancelButton[\s\S]*?HorizontalAlignment="Stretch"') {
    $issues.Add('Confirm and cancel must be large equal-width horizontal actions.')
}
if ($code -notmatch 'ShowStoryChoiceEditorAsync[\s\S]*?TaskCompletionSource<StoryChoiceEditorResult\?>') {
    $issues.Add('The overlay must return the existing editor result asynchronously.')
}
if ($code -notmatch 'StoryChoiceEditorHost_KeyDown[\s\S]*?VirtualKey\.Escape[\s\S]*?VirtualKey\.Enter[\s\S]*?StoryChoiceEditorHost_RightTapped') {
    $issues.Add('The overlay must retain Escape, Enter, and right-click behavior.')
}
if ($code -notmatch '前往小节（[\s\S]*?个已创建[\s\S]*?CreateStoryChoiceTargetContent[\s\S]*?FirstSentencePreview') {
    $issues.Add('Target selection must show the dynamic existing-section count and first-sentence previews.')
}
if ($code -notmatch 'CreateStoryChoiceTargetContent[\s\S]*?Ellipse[\s\S]*?AccentFillColorDefaultBrush[\s\S]*?已绑定' -or
    $code -notmatch 'RefreshStoryChoiceTargetBindingIndicators[\s\S]*?HashSet<string>[\s\S]*?个已绑定') {
    $issues.Add('Existing target bindings must use an accessible system-accent marker.')
}
if ($code -notmatch 'RefreshStoryChoiceTargetBindingIndicators[\s\S]*?Document\.BranchRoutes[\s\S]*?ChoiceFunction[\s\S]*?_storyChoiceEditorChoiceIndicator[\s\S]*?UnionWith') {
    $issues.Add('Binding markers must merge chapter-wide saved routes with the current unsaved Choice.')
}
if ($code -notmatch 'StoryChoiceEditorTargetComboBox_SelectionChanged[\s\S]*?RefreshStoryChoiceTargetBindingIndicators[\s\S]*?StoryChoiceEditorDeleteButton_Click[\s\S]*?RefreshStoryChoiceTargetBindingIndicators') {
    $issues.Add('Binding markers must refresh after target changes and option deletion.')
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

Write-Host 'Choice editor overlay checks passed.'
