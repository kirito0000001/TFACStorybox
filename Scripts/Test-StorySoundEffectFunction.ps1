$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern([string]$path, [string]$pattern, [string]$message) {
    if (-not (Test-Path -LiteralPath $path) -or
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $path) -notmatch $pattern) {
        $issues.Add($message)
    }
}

Require-Pattern `
    (Join-Path $root 'MainWindow.xaml.cs') `
    '_storySoundEffectPlayback\s*=\s*new\(looping:\s*false\)[\s\S]*?_storySoundEffectPlayback\.Dispose' `
    'One-shot story effects need an independent non-looping player lifecycle.'

Require-Pattern `
    (Join-Path $root 'Services\StoryFunctionService.cs') `
    'TryParseSoundEffectIndex[\s\S]*?\^Scene_\(\?<index>\\d\+\)\$' `
    'Scene_ function parsing must accept only a numeric one-shot effect index.'

Require-Pattern `
    (Join-Path $root 'Services\StoryFunctionService.cs') `
    'TryParseSoundEffectIndex[\s\S]*?index\s*=\s*0[\s\S]*?match\.Success' `
    'Scene_ parsing must initialize its out index when the function does not match.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Media.cs') `
    'ApplyCurrentStoryRowMediaAndFunctionAsync[\s\S]*?_storySoundEffectPlayback\.Stop\(\)[\s\S]*?PlayStorySoundEffectFunctionsAsync[\s\S]*?BGMSTOP' `
    'One-shot effects must stop and play before BGM branch handling.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Media.cs') `
    'PlayStorySoundEffectFunctionsAsync[\s\S]*?GetSoundEffectFolderPath[\s\S]*?_storySoundEffectPlayback\.PlayAsync' `
    'Scene_ functions must resolve special-effect assets and play them once.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

Write-Host 'Story one-shot sound effect checks passed.'
