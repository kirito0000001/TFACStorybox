param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV",

    [string]$Version = "",

    [switch]$Clean,

    [switch]$KeepWorkFolder,

    [string]$ReleaseAssetRoot = "",

    [ValidateSet("stable", "beta")]
    [string]$Channel = "stable",

    [string]$ReleaseNotesPath = "",

    [switch]$SkipUpdateAssets
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
        [string]$ProjectPath
    )

    if (!([string]::IsNullOrWhiteSpace($ExplicitVersion))) {
        return $ExplicitVersion.Trim()
    }

    $projectText = Get-Content -LiteralPath $ProjectPath -Raw
    if ($projectText -match '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') {
        return $Matches[1]
    }

    throw "App version was not found in $ProjectPath. Pass -Version manually."
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
$platform = Get-PlatformFromRuntime -RuntimeIdentifier $Runtime
$targetFramework = Get-ProjectTargetFramework -ProjectPath $projectPath
$appDisplayName = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("VEZBQ+WJp+aDheeusS3ova7mpIXniYg="))
$appVersion = Get-AppVersion -ExplicitVersion $Version -ProjectPath $projectPath
$packageBaseName = "$appDisplayName" + "V" + $appVersion
$workRoot = Join-Path $OutputRoot ".package-work"
$publishDir = Join-Path $workRoot "publish"
$buildOutputDir = Join-Path $repoRoot (Join-Path "bin" (Join-Path $platform (Join-Path $Configuration (Join-Path $targetFramework $Runtime))))
$packageRoot = Join-Path $OutputRoot $packageBaseName
$programDir = Join-Path $packageRoot $appDisplayName
$shortcutPath = Join-Path $packageRoot "$appDisplayName.lnk"
if ([string]::IsNullOrWhiteSpace($ReleaseAssetRoot)) {
    $ReleaseAssetRoot = Join-Path $repoRoot "ReleaseAssets"
}
if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $ReleaseNotesPath = Join-Path $repoRoot "Scripts\ReleaseNotes.txt"
}

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
$versionMetadata = @{ Version = $appVersion } | ConvertTo-Json
$versionMetadataPath = Join-Path $programDir "ax-app-version.json"
[System.IO.File]::WriteAllText($versionMetadataPath, $versionMetadata, [System.Text.UTF8Encoding]::new($false))
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

if (!$SkipUpdateAssets) {
    Write-Host "==> Building update assets"
    New-Item -ItemType Directory -Force -Path $ReleaseAssetRoot | Out-Null
    $updateStageDir = Join-Path $workRoot "update-package"
    Remove-DirectoryIfExists -Path $updateStageDir
    New-Item -ItemType Directory -Force -Path $updateStageDir | Out-Null
    Copy-Item -Path (Join-Path $programDir "*") -Destination $updateStageDir -Recurse -Force

    $updatePackageManifest = [ordered]@{
        schemaVersion = 1
        toolboxStableKey = "GalExcleTools"
        version = $appVersion
        runtime = $Runtime
        entryExe = $publishedExe.Name
        createdAt = [DateTimeOffset]::Now.ToString("o")
    }
    $updatePackageManifestPath = Join-Path $updateStageDir "update-package.json"
    [IO.File]::WriteAllText(
        $updatePackageManifestPath,
        ($updatePackageManifest | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))

    $updateArchiveName = "TFACStorybox-v$appVersion-$Runtime.zip"
    $updateArchivePath = Join-Path $ReleaseAssetRoot $updateArchiveName
    Remove-Item -LiteralPath $updateArchivePath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $updateStageDir "*") -DestinationPath $updateArchivePath -CompressionLevel Optimal
    $updateArchiveFile = Get-Item -LiteralPath $updateArchivePath
    $updateArchiveHash = (Get-FileHash -LiteralPath $updateArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashFilePath = Join-Path $ReleaseAssetRoot "$updateArchiveName.sha256.txt"
    [IO.File]::WriteAllText(
        $hashFilePath,
        "$updateArchiveHash  $updateArchiveName`r`n",
        [Text.UTF8Encoding]::new($false))

    $releaseNotes = if (Test-Path -LiteralPath $ReleaseNotesPath -PathType Leaf) {
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $ReleaseNotesPath).Trim()
    }
    else {
        "TFAC剧情箱-轮椅版 V$appVersion 更新。"
    }
    $releaseTag = "v$appVersion"
    $updateManifest = [ordered]@{
        schemaVersion = 1
        toolboxStableKey = "GalExcleTools"
        displayName = $appDisplayName
        version = $appVersion
        channel = $Channel
        publishedAt = [DateTimeOffset]::Now.ToString("o")
        releaseNotesUrl = "https://gitee.com/xiaojie578/TFACStorybox/releases/tag/$releaseTag"
        releaseNotes = $releaseNotes
        requiresManualMigration = $false
        assets = @(
            [ordered]@{
                runtime = $Runtime
                fileName = $updateArchiveName
                sha256 = $updateArchiveHash
                sizeBytes = $updateArchiveFile.Length
            }
        )
    }
    $updateManifestPath = Join-Path $ReleaseAssetRoot "toolbox-update.json"
    [IO.File]::WriteAllText(
        $updateManifestPath,
        ($updateManifest | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
}

if (!$KeepWorkFolder) {
    Remove-DirectoryIfExists -Path $workRoot
}

Write-Host ""
Write-Host "Package folder: $packageRoot"
Write-Host "Version:        $appVersion"
Write-Host "Runtime:        $Runtime"
Write-Host "Program folder: $appDisplayName"
Write-Host "Shortcut:       $appDisplayName.lnk"
if (!$SkipUpdateAssets) {
    Write-Host "Update assets:  $ReleaseAssetRoot"
}
if ($KeepWorkFolder) {
    Write-Host "Work folder:    $workRoot"
}
