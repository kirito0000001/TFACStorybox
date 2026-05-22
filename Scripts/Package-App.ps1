param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV",

    [string]$Version = "",

    [switch]$Clean,

    [switch]$KeepWorkFolder
)

$ErrorActionPreference = "Stop"

function Get-PlatformFromRuntime {
    param([string]$RuntimeIdentifier)

    switch ($RuntimeIdentifier) {
        "win-x64" { return "x64" }
        "win-x86" { return "x86" }
        "win-arm64" { return "ARM64" }
        default { throw "Unsupported runtime: $RuntimeIdentifier" }
    }
}

function Remove-DirectoryIfExists {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-AppVersion {
    param(
        [string]$ExplicitVersion,
        [string]$XamlPath
    )

    if (!([string]::IsNullOrWhiteSpace($ExplicitVersion))) {
        return $ExplicitVersion.Trim()
    }

    $xaml = Get-Content -LiteralPath $XamlPath -Raw
    if ($xaml -match 'Text="版本\s+([0-9]+\.[0-9]+\.[0-9]+)"') {
        return $Matches[1]
    }

    if ($xaml -match 'Text="([0-9]+\.[0-9]+\.[0-9]+)"') {
        return $Matches[1]
    }

    throw "App version was not found in $XamlPath. Pass -Version manually."
}

function Get-ProjectTargetFramework {
    param([string]$ProjectPath)

    $projectText = Get-Content -LiteralPath $ProjectPath -Raw
    if ($projectText -notmatch '<TargetFramework>\s*([^<\s]+)\s*</TargetFramework>') {
        throw "TargetFramework was not found in $ProjectPath."
    }

    return $Matches[1]
}

function Copy-WinUiCompiledResources {
    param(
        [string]$BuildOutputDir,
        [string]$DestinationDir,
        [string]$AppExeBaseName
    )

    $resourceNames = @(
        "$AppExeBaseName.pri",
        "App.xbf",
        "MainWindow.xbf"
    )

    foreach ($resourceName in $resourceNames) {
        $sourcePath = Join-Path $BuildOutputDir $resourceName
        if (!(Test-Path -LiteralPath $sourcePath)) {
            throw "Required WinUI resource was not found: $sourcePath"
        }

        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $DestinationDir $resourceName) -Force
    }
}

function New-AppShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$Description
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Description = $Description
    $shortcut.Save()
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "GalExcleTools.csproj"
$xamlPath = Join-Path $repoRoot "MainWindow.xaml"
$platform = Get-PlatformFromRuntime -RuntimeIdentifier $Runtime
$targetFramework = Get-ProjectTargetFramework -ProjectPath $projectPath
$appDisplayName = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("VEZBQ+WJp+aDheeusS3ova7mpIXniYg="))
$appVersion = Get-AppVersion -ExplicitVersion $Version -XamlPath $xamlPath
$packageBaseName = "$appDisplayName" + "V" + $appVersion
$workRoot = Join-Path $OutputRoot ".package-work"
$publishDir = Join-Path $workRoot "publish"
$buildOutputDir = Join-Path $repoRoot (Join-Path "bin" (Join-Path $platform (Join-Path $Configuration (Join-Path $targetFramework $Runtime))))
$packageRoot = Join-Path $OutputRoot $packageBaseName
$programDir = Join-Path $packageRoot $appDisplayName
$shortcutPath = Join-Path $packageRoot "$appDisplayName.lnk"

if (!(Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

if ($Clean) {
    Remove-DirectoryIfExists -Path (Join-Path $repoRoot "bin")
    Remove-DirectoryIfExists -Path (Join-Path $repoRoot "obj")
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
Remove-DirectoryIfExists -Path $workRoot
Remove-DirectoryIfExists -Path $packageRoot

Write-Host "==> Restoring packages"
dotnet restore $projectPath

Write-Host "==> Publishing $Configuration / $Runtime"
dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    -p:Platform=$platform `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$runtimeHelperExeNames = @(
    "createdump.exe",
    "RestartAgent.exe"
)
$publishedExe = Get-ChildItem -LiteralPath $publishDir -Filter "*.exe" -File |
    Where-Object { $runtimeHelperExeNames -notcontains $_.Name } |
    Sort-Object Length -Descending |
    Select-Object -First 1
if ($null -eq $publishedExe) {
    throw "Publish completed, but no app .exe was found in $publishDir"
}

Write-Host "==> Building release layout"
New-Item -ItemType Directory -Force -Path $programDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $programDir -Recurse -Force
Copy-WinUiCompiledResources `
    -BuildOutputDir $buildOutputDir `
    -DestinationDir $programDir `
    -AppExeBaseName $publishedExe.BaseName

$appExe = Join-Path $programDir $publishedExe.Name
New-AppShortcut `
    -ShortcutPath $shortcutPath `
    -TargetPath $appExe `
    -WorkingDirectory $programDir `
    -Description $appDisplayName

if (!$KeepWorkFolder) {
    Remove-DirectoryIfExists -Path $workRoot
}

Write-Host ""
Write-Host "Package folder: $packageRoot"
Write-Host "Version:        $appVersion"
Write-Host "Runtime:        $Runtime"
Write-Host "Program folder: $appDisplayName"
Write-Host "Shortcut:       $appDisplayName.lnk"
if ($KeepWorkFolder) {
    Write-Host "Work folder:    $workRoot"
}
