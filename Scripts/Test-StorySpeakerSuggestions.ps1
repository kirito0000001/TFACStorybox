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
if ($code -notmatch 'BuildStorySpeakerNameSuggestions[\s\S]*?ResolveStoryCharacter[\s\S]*?Value\s*=\s*character\?\.Code[\s\S]*?DisplayName\s*=\s*character\?\.Name[\s\S]*?SecondaryText\s*=\s*character\?\.Code\s*\?\?\s*"nullptr"[\s\S]*?GroupBy\(item\s*=>\s*item\.Value') {
    $issues.Add('Speaker suggestions must canonicalize character-backed names to codes before counting and label custom names as nullptr.')
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
if ($code -notmatch 'RefreshStorySpeakerNameSuggestions[\s\S]*?DisplayName\.Contains[\s\S]*?Value\.Contains[\s\S]*?SecondaryText\.Contains') {
    $issues.Add('Speaker search must match Chinese display names, English codes, and custom names.')
}
if ($code -notmatch 'RefreshStorySpeakerNameSuggestions[\s\S]*?new StorySpeakerNameSuggestion\([\s\S]*?清空[\s\S]*?Concat\([\s\S]*?suggestions\.Add[\s\S]*?创建说话人') {
    $issues.Add('The clear action must remain first and the create-speaker action must remain last.')
}
if ($xaml -notmatch 'StorySpeakerTextBox[\s\S]*?Text="\{Binding DisplayName\}"[\s\S]*?Text="\{Binding SecondaryText\}"[\s\S]*?Text="\{Binding UsageCount\}"') {
    $issues.Add('Speaker suggestions must show display name, character code, and a right-aligned numeric usage count.')
}
if ($xaml -notmatch 'Text="\{Binding UsageCount\}"[\s\S]*?HorizontalAlignment="Right"') {
    $issues.Add('Speaker usage count must be right-aligned.')
}
if ($code -notmatch 'RefreshStorySpeakerNameSuggestions[\s\S]*?suggestions\.Add[\s\S]*?创建说话人') {
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
