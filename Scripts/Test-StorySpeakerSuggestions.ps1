$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$xaml = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.xaml')
$code = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.Story.TextEditing.cs')
$issues = [System.Collections.Generic.List[string]]::new()

if ($xaml -notmatch '<AutoSuggestBox[\s\S]*?x:Name="StorySpeakerTextBox"[\s\S]*?Text="\{Binding SpeakerText, Mode=TwoWay') {
    $issues.Add('Current speaker must be one free-entry AutoSuggestBox.')
}
if ($xaml -match 'StorySpeakerNameFlyout|StorySpeakerNamePickerButton|清空说话人') {
    $issues.Add('Speaker suggestions must not add a separate flyout, picker, or clear button.')
}
if ($code -notmatch 'BuildStorySpeakerNameSuggestions[\s\S]*?GroupBy[\s\S]*?Count\(\)[\s\S]*?OrderByDescending') {
    $issues.Add('Speaker suggestions must be ranked by usage count in the current chapter.')
}
if ($code -notmatch 'StorySpeakerTextBox_TextChanged[\s\S]*?AutoSuggestionBoxTextChangeReason\.UserInput[\s\S]*?Contains\(') {
    $issues.Add('Typing in the speaker box must filter suggestions while preserving free input.')
}
if ($code -notmatch 'StorySpeakerTextBox_GotFocus[\s\S]*?IsSuggestionListOpen\s*=') {
    $issues.Add('Focusing the speaker box must show chapter suggestions immediately.')
}
if ($xaml -notmatch 'StorySpeakerTextBox[\s\S]*?TextMemberPath="Value"[\s\S]*?SuggestionChosen="StorySpeakerTextBox_SuggestionChosen"') {
    $issues.Add('The same free-entry box must accept both existing names and the create action.')
}
if ($code -notmatch 'RefreshStorySpeakerNameSuggestions[\s\S]*?Contains\([\s\S]*?suggestions\.Add[\s\S]*?创建说话人') {
    $issues.Add('The fixed create-speaker action must remain last after filtering existing names.')
}
if ($code -match 'StorySpeakerTextBox_TextChanged[\s\S]*?StoryCharacterSlotService|StorySpeakerTextBox_TextChanged[\s\S]*?TalkBody') {
    $issues.Add('Speaker-name suggestions must not modify portrait data.')
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Story speaker suggestion checks passed.'
