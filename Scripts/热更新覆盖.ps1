param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$InstallDirectory,

    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [string]$ReadySignalPath,

    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'

$stableKey = 'GalExcleTools'
$entryExe = 'TFAC剧情箱-轮椅版.exe'
$runtime = 'win-x64'
$installFullPath = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$packageFullPath = [IO.Path]::GetFullPath($PackagePath)
$runnerRoot = Split-Path -Parent ([IO.Path]::GetFullPath($ReadySignalPath))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stagingPath = Join-Path $runnerRoot "Staging-$stamp"
$backupPath = "$installFullPath.update-backup"
$logRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "$stableKey\UpdateLogs"
$logPath = Join-Path $logRoot "Update-$stamp.log"

function Write-UpdateLog {
    param([string]$Message)

    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
    Add-Content -LiteralPath $logPath -Encoding UTF8 -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message"
}

function Remove-DirectoryIfExists {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

try {
    Write-UpdateLog "Preflight started. Package=$packageFullPath Install=$installFullPath PID=$ProcessId"
    if (!(Test-Path -LiteralPath $packageFullPath -PathType Leaf)) {
        throw "更新包不存在：$packageFullPath"
    }

    if (!(Test-Path -LiteralPath $installFullPath -PathType Container)) {
        throw "当前程序目录不存在：$installFullPath"
    }

    Remove-DirectoryIfExists -Path $stagingPath
    New-Item -ItemType Directory -Force -Path $stagingPath | Out-Null
    Expand-Archive -LiteralPath $packageFullPath -DestinationPath $stagingPath -Force

    $packageManifestPath = Join-Path $stagingPath 'update-package.json'
    if (!(Test-Path -LiteralPath $packageManifestPath -PathType Leaf)) {
        throw '更新包缺少 update-package.json。'
    }

    $packageManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $packageManifestPath | ConvertFrom-Json
    if ([int]$packageManifest.schemaVersion -ne 1 -or
        [string]$packageManifest.toolboxStableKey -cne $stableKey -or
        [string]$packageManifest.version -cne $ExpectedVersion -or
        [string]$packageManifest.runtime -cne $runtime -or
        [string]$packageManifest.entryExe -cne $entryExe) {
        throw '更新包身份、版本或 Runtime 与当前更新任务不匹配。'
    }

    $stagedExe = Join-Path $stagingPath $entryExe
    if (!(Test-Path -LiteralPath $stagedExe -PathType Leaf)) {
        throw "更新包缺少主程序：$entryExe"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReadySignalPath) | Out-Null
    Set-Content -LiteralPath $ReadySignalPath -Encoding ASCII -Value 'ready'
    Write-UpdateLog 'Preflight passed; waiting for the main process to exit.'

    $deadline = (Get-Date).AddMinutes(2)
    while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        if ((Get-Date) -ge $deadline) {
            throw "等待主程序退出超时：PID $ProcessId"
        }

        Start-Sleep -Milliseconds 250
    }

    Remove-DirectoryIfExists -Path $backupPath
    Move-Item -LiteralPath $installFullPath -Destination $backupPath
    try {
        New-Item -ItemType Directory -Force -Path $installFullPath | Out-Null
        Get-ChildItem -LiteralPath $stagingPath -Force |
            Where-Object { $_.Name -ne 'update-package.json' } |
            Copy-Item -Destination $installFullPath -Recurse -Force

        $installedExe = Join-Path $installFullPath $entryExe
        if (!(Test-Path -LiteralPath $installedExe -PathType Leaf)) {
            throw '新版本覆盖后没有找到主程序。'
        }

        Remove-DirectoryIfExists -Path $backupPath
        if ($SkipLaunch) {
            Write-UpdateLog 'Update completed. Launch skipped by verification mode.'
        }
        else {
            Write-UpdateLog "Update completed. Launching $installedExe"
            Start-Process -FilePath $installedExe -WorkingDirectory $installFullPath
        }
    }
    catch {
        Write-UpdateLog "Install failed; restoring backup. $($_.Exception.Message)"
        Remove-DirectoryIfExists -Path $installFullPath
        if (Test-Path -LiteralPath $backupPath -PathType Container) {
            Move-Item -LiteralPath $backupPath -Destination $installFullPath
        }

        throw
    }
}
catch {
    Write-UpdateLog "Update failed. $($_.Exception)"
    throw
}
finally {
    Remove-DirectoryIfExists -Path $stagingPath
    Remove-Item -LiteralPath $ReadySignalPath -Force -ErrorAction SilentlyContinue
}
