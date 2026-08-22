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
    (Join-Path $root 'Models\UnrealSyncModels.cs') `
    'UnrealStoryBranchSourceSyncEntry[\s\S]*?UnrealStoryBranchRouteSyncEntry[\s\S]*?StoryBranchesUpdated[\s\S]*?StoryBranchesHash[\s\S]*?StoryBranchesAssetHash' `
    'Unreal sync must model branch sources, routes, verification, and state hashing.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'StoryBranchDataTableAssetPath[\s\S]*?story-branches-v1[\s\S]*?BuildStoryBranchSyncEntries' `
    'Change planning must derive and hash managed branch routes.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSyncService.cs') `
    'export_data_table_to_json_string[\s\S]*?fill_data_table_from_json_string[\s\S]*?verify_story_branches[\s\S]*?save_loaded_asset_with_retry' `
    'Unreal Python must merge, verify, save, and re-verify AllRoad.'

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.Process.cs') `
    'StoryBranchDataTableAssetPath[\s\S]*?filesToBackup\.Add' `
    'AllRoad must be included in the Unreal sync backup.'

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.cs') `
    '!changePlan\.StoryBranchesChanged\s*\|\|\s*report\.StoryBranchesUpdated' `
    'The UI must not record branch sync success without Unreal verification.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
$pythonPath = 'D:\YuanMa\UnrealEngine-release\Engine\Binaries\ThirdParty\Python3\Win64\python.exe'
if ((Test-Path -LiteralPath $assemblyPath) -and (Test-Path -LiteralPath $pythonPath)) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $serviceType = $assembly.GetType('GalExcleTools.Services.UnrealSyncService', $true)
    $service = [System.Activator]::CreateInstance($serviceType, $true)
    [string]$scriptPath = Join-Path $env:TEMP ('gal-story-branches-' + [guid]::NewGuid().ToString('N') + '.py')
    try {
        $arguments = [object[]]@($scriptPath, [string]'D:\Temp\gal-sync-manifest.json')
        [void]$serviceType.GetMethod('WritePythonScript').Invoke($service, $arguments)
        & $pythonPath -m py_compile $scriptPath
        if ($LASTEXITCODE -ne 0) {
            throw 'Generated Unreal Python failed syntax validation.'
        }
    }
    finally {
        Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
        $cache = Join-Path (Split-Path $scriptPath -Parent) '__pycache__'
        if (Test-Path -LiteralPath $cache) {
            Get-ChildItem -LiteralPath $cache -Filter (([IO.Path]::GetFileNameWithoutExtension($scriptPath)) + '*.pyc') -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Unreal story-branch source checks passed.'
