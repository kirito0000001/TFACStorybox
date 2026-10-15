$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$xaml = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.xaml')
$issues = [System.Collections.Generic.List[string]]::new()

if ($xaml -notmatch 'x:Name="StorySpeakerTextBox"[\s\S]*?Width="190"[\s\S]*?HorizontalAlignment="Center"[\s\S]*?PlaceholderText="输入或选择说话人"') {
    $issues.Add('Speaker input must align with the 190-pixel speaker portrait preview.')
}
if ($xaml -match 'x:Name="StorySpeakerTextBox"[^>]*Header="当前说话人"') {
    $issues.Add('Speaker input must not retain the redundant visible header.')
}
if ($xaml -notmatch 'x:Name="StoryTextTextBox"[\s\S]*?PlaceholderText="剧情文本"') {
    $issues.Add('Story text must use a placeholder instead of a visible header.')
}
if ($xaml -match 'x:Name="StoryTextTextBox"[^>]*Header="剧情文本"') {
    $issues.Add('Story text must not retain the redundant visible header.')
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1

}

Write-Host 'Story editor dock layout checks passed.'
