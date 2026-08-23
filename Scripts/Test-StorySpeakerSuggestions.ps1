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
if ($code -notmatch 'RefreshStorySpeakerNameSuggestions[\s\S]*?!string\.IsNullOrWhiteSpace\(query\)[\s\S]*?GetStoryCharacters\(\)[\s\S]*?character\.Name\.Contains[\s\S]*?character\.Code\.Contains[\s\S]*?UsageCount:\s*0') {
    $issues.Add('A non-empty search must include unused asset-library characters by Chinese name or English code with a zero count.')
}
if ($code -notmatch 'RefreshStorySpeakerNameSuggestions[\s\S]*?knownValues[\s\S]*?StringComparer\.OrdinalIgnoreCase[\s\S]*?knownValues\.Add') {
    $issues.Add('Asset-library search results must not duplicate speakers already used in the chapter.')
}
if ($code -notmatch 'RefreshStorySpeakerNameSuggestions[\s\S]*?new StorySpeakerNameSuggestion\([\s\S]*?创建说话人[\s\S]*?OrderBy\([\s\S]*?suggestions\.Add[\s\S]*?清空') {
    $issues.Add('The upward-opening popup source must be reversed so clear renders first, frequent speakers descend, and create renders last.')
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
if ($code -notmatch 'string\.IsNullOrWhiteSpace\(query\)\s*\?\s*"直接采用输入"' -or
    $code -match '输入名称后直接采用') {
    $issues.Add('The empty create-speaker hint must stay short enough for one line.')
}
if ($code -match 'StorySpeakerTextBox_TextChanged[\s\S]*?StoryCharacterSlotService|StorySpeakerTextBox_TextChanged[\s\S]*?TalkBody') {
    $issues.Add('Speaker-name suggestions must not modify portrait data.')
}
if ($code -notmatch 'GetStorySpeakerDisplayName[\s\S]*?ResolveStoryCharacter[\s\S]*?character\?\.Name') {
    $issues.Add('Known speaker codes must display their Chinese character names in the editor.')
}
if ($code -notmatch 'StorySpeakerTextBox_SuggestionChosen[\s\S]*?IsClearAction[\s\S]*?suggestion\.DisplayName') {
    $issues.Add('Choosing a known speaker must show its Chinese display name while clear remains empty.')
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Story speaker suggestion checks passed.'
