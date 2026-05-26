param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$filesToCheck = @(
    "MainWindow.xaml",
    "MainWindow.xaml.cs",
    "README.md",
    "Docs/CodexInternalNotes.md"
)

$issues = [System.Collections.Generic.List[string]]::new()

foreach ($relativePath in $filesToCheck) {
    if (-not (Test-Path -LiteralPath $relativePath)) {
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $relativePath))
    try {
        $text = $utf8Strict.GetString($bytes)
    }
    catch {
        $issues.Add("$relativePath is not strict UTF-8: $($_.Exception.Message)")
        continue
    }

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $issues.Add("$relativePath is UTF-16 LE; keep repository text files as UTF-8.")
    }

    if ($text.Contains([char]0xFFFD)) {
        $issues.Add("$relativePath contains replacement characters.")
    }
}

$xamlText = $utf8Strict.GetString([System.IO.File]::ReadAllBytes((Resolve-Path "MainWindow.xaml")))
try {
    [xml]$xamlText | Out-Null
}
catch {
    $issues.Add("MainWindow.xaml is not well-formed XML: $($_.Exception.Message)")
}

$mojibakeChars = @(
    0x934A,
    0x675E,
    0x7EF1,
    0x6924,
    0x94CF,
    0x9417,
    0x93C4,
    0x6D63,
    0x6D7C,
    0xFFFD
) | ForEach-Object { [char]$_ }

$xamlCodeText = $utf8Strict.GetString([System.IO.File]::ReadAllBytes((Resolve-Path "MainWindow.xaml.cs")))
foreach ($entry in @(
    @{ Path = "MainWindow.xaml"; Text = $xamlText },
    @{ Path = "MainWindow.xaml.cs"; Text = $xamlCodeText }
)) {
    $lineNumber = 0
    foreach ($line in ($entry.Text -split "\r?\n")) {
        $lineNumber++
        foreach ($marker in $mojibakeChars) {
            if ($line.Contains($marker)) {
                $issues.Add("$($entry.Path):$lineNumber contains likely mojibake: $($line.Trim())")
                break
            }
        }

        if ($line -match '\?\s+(Style|Click|Tag|FontSize|Content|Header|Text|VerticalAlignment|HorizontalAlignment)=') {
            $issues.Add("$($entry.Path):$lineNumber may have a broken XAML attribute: $($line.Trim())")
        }
    }
}

if ($Build) {
    dotnet build GalExcleTools.csproj --configuration Release --runtime win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Source health checks passed."
