$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern {
    param([string]$Path, [string]$Pattern, [string]$Message)
    if (-not (Test-Path -LiteralPath $Path) -or
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path) -notmatch $Pattern) {
        $issues.Add($Message)
    }
}

Require-Pattern `
    (Join-Path $repoRoot 'Views\StoryDialogContentFactory.cs') `
    'FunctionSelectionDialogContent[\s\S]*?AutoSuggestBox[\s\S]*?DisplayName[\s\S]*?Indicator[\s\S]*?FontSize\s*=\s*12[\s\S]*?Opacity\s*=\s*0\.6' `
    'Function picker must provide search and two-line primary/secondary rows.'

Require-Pattern `
    (Join-Path $repoRoot 'Services\FunctionDialogService.cs') `
    'SelectFunctionAsync[\s\S]*?CreateFunctionSelectionContent[\s\S]*?ShowContentAsync' `
    'Function picker must use a dedicated searchable content dialog.'

Require-Pattern `
    (Join-Path $repoRoot 'Services\StoryFunctionService.cs') `
    'OrderByUsageFrequency[\s\S]*?BGM[\s\S]*?VFX[\s\S]*?BGLerpMode[\s\S]*?Scene_[\s\S]*?TransAnim' `
    'Function picker must use the fixed two-chapter frequency order.'

$pickerSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot 'Views\StoryDialogContentFactory.cs')
if ($pickerSource -match 'FunctionSelectionDialogContent[\s\S]*?function\.Category') {
    $issues.Add('Function picker rows must not display categories.')
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1

}

Write-Host 'Story function picker checks passed.'
