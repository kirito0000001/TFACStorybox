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
    (Join-Path $root 'Views\AssetCardContentFactory.cs') `
    'CreateFunctionCardContent\(string name, string indicator, string description\)[\s\S]*?Text = name[\s\S]*?Text = indicator[\s\S]*?Text = description' `
    'Function cards must display name, indicator, and description in that order.'

Require-Pattern `
    (Join-Path $root 'MainWindow.AssetLibrary.Loading.cs') `
    'CreateFunctionCard\(FunctionEntry function\)[\s\S]*?260,[\s\S]*?126,[\s\S]*?CreateFunctionCardContent\(\s*function\.Name,\s*StoryFunctionService\.BuildFunctionCatalogSummary\(function\),\s*string\.IsNullOrWhiteSpace\(function\.Description\)[\s\S]*?stretchContent:\s*true' `
    'Function cards must use one fixed stretched size.'

Require-Pattern `
    (Join-Path $root 'Views\EditorDialogContentFactory.cs') `
    'FunctionEditorDialogContent[\s\S]*?_descriptionBox[\s\S]*?Header = "介绍"' `
    'Function editing must expose a description field.'

$loadingSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.AssetLibrary.Loading.cs')
if ($loadingSource -match 'CreateFunctionCard\(FunctionEntry function\)[\s\S]*?function\.Category') {
    $issues.Add('Function cards must not display categories.')
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1

}

Write-Host 'Story function card checks passed.'
