$ErrorActionPreference = 'Stop'

# 主题画笔：代码里取画笔（Application.Current.Resources["…Brush"]）按**应用/系统**主题解析，
# 而工具的主题是 RootGrid.RequestedTheme（夜间模式开关）。两者不一致时，
# XAML 的 {ThemeResource} 是对的、代码取的是另一套 → 浅色卡片上出现浅色文字。
# 修法：把当前主题那份字典里的画笔盖到应用级（值一样时是空操作）。
$root = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern([string]$path, [string]$pattern, [string]$message, [switch]$NotMatch) {
    $matched = (Test-Path -LiteralPath $path) -and
        ((Get-Content -Raw -Encoding UTF8 -LiteralPath $path) -match $pattern)
    if ($NotMatch) { $matched = -not $matched }
    if (-not $matched) {
        $issues.Add($message)
    }
}

$settingsPath = Join-Path $root 'MainWindow.Settings.cs'

Require-Pattern $settingsPath `
    'ApplyThemeResourceOverride\(bool isDark\)[\s\S]*?ThemeDictionaries\.TryGetValue\(themeKey[\s\S]*?foreach \(var entry in themeDictionary\)[\s\S]*?entry\.Value is Brush[\s\S]*?Application\.Current\.Resources\[entry\.Key\] = brush' `
    'The theme override must copy every brush of the active theme dictionary into the application resources.'

Require-Pattern $settingsPath `
    'new\[\]\s*\{[^}]*TextFillColorSecondaryBrush' `
    'The theme override must not enumerate keys by hand (a missed key means an invisible label again).' -NotMatch

Require-Pattern $settingsPath `
    'ApplyThemeSettings\(\)[\s\S]*?ApplyThemeResourceOverride\(isDark\);[\s\S]*?RootGrid\.RequestedTheme' `
    'The brush override must be applied before the element theme is switched.'

Require-Pattern (Join-Path $root 'MainWindow.xaml.cs') `
    'Settings\.Load\(\);[\s\S]*?ApplyThemeSettings\(\);[\s\S]*?ShowWorkbenchPage\(\);' `
    'The theme must be applied after settings load and before the first page is built.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

# 代码里按资源键取东西的地方：键必须都是 Brush（被上面那步盖住）或 Style（本来就不分主题）。
# 将来有人加了个别的键（比如某个主题化字典资源），这里会提醒他想想主题这回事。
$uncoveredKeys = [System.Collections.Generic.List[string]]::new()
$lookupCount = 0
Get-ChildItem -Path $root -Recurse -File -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' } |
    ForEach-Object {
        $text = Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName
        foreach ($match in [regex]::Matches($text, 'Application\.Current\.Resources\[\s*"(?<key>[A-Za-z0-9]+)"')) {
            $lookupCount++
            $key = $match.Groups['key'].Value
            if ($key -notmatch 'Brush$' -and $key -notmatch 'Style$') {
                $uncoveredKeys.Add("$($_.Name): $key")
            }
        }
    }

if ($uncoveredKeys.Count -gt 0) {
    $uncoveredKeys | Sort-Object -Unique | ForEach-Object { Write-Host "  · 代码取了这个键：$_" -ForegroundColor Red }
    Write-Error "$($uncoveredKeys.Count) 处代码级资源键既不是 Brush 也不是 Style —— 主题覆盖盖不住它，先确认它需不需要跟主题走。"
    exit 1
}

Write-Host "Theme resource checks passed（扫到 $lookupCount 处代码级资源查找，全是 Brush / Style 键）。"
