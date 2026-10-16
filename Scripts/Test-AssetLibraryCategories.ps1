$ErrorActionPreference = 'Stop'

# 素材库分类文件夹：
#   · 分类清单只能有一份（建目录和同步读目录必须说同一件事）
#   · 同步/检测差异之前要把工具箱侧的目录补齐（用户 2026-10-16 的要求）
#   · 目录不存在时枚举要给空集，不许抛"找不到路径"
# 起因：2026-10-14 新加的「道具图片」只加在了读的那边，建目录的清单漏了它，
#       没有道具图片素材的素材库一同步就炸 → 「检测差异失败：Could not find a part of the path …\道具图片」。
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

Require-Pattern `
    (Join-Path $root 'Services\WorkspacePathUtility.cs') `
    'AssetLibraryCategoryFolderNames[\s\S]*?BackgroundFolderName[\s\S]*?PropImageFolderName[\s\S]*?CharacterFolderName[\s\S]*?MusicFolderName[\s\S]*?AmbientSoundFolderName[\s\S]*?SoundEffectFolderName[\s\S]*?FunctionFolderName[\s\S]*?CharacterFilterFolderName' `
    'The asset-library category list must be the single source of truth (and include 道具图片).'

Require-Pattern `
    (Join-Path $root 'Services\ProjectWorkspaceService.cs') `
    'EnsureAssetLibraryCategoryFolders[\s\S]*?GetAssetLibraryCategoryFolderPaths' `
    'Creating the category folders must follow the shared list, not a private copy.'

Require-Pattern `
    (Join-Path $root 'Services\ProjectWorkspaceService.cs') `
    'Path\.Combine\(assetLibraryPath, (Background|PropImage|Character|Music|AmbientSound|SoundEffect|Function|CharacterFilter)FolderName\)' `
    'The old hardcoded per-category CreateDirectory calls must stay removed.' -NotMatch

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.Runner.cs') `
    'BuildUnrealSyncChangePlan[\s\S]*?EnsureAssetLibraryCategoryFolders\(context\.AssetLibrary\.Path\)[\s\S]*?new ImageSetSyncInput' `
    'Building the sync plan must top up the toolbox-side category folders before reading them.'

foreach ($service in @('Services\ImageSetService.cs', 'Services\AudioAssetService.cs')) {
    Require-Pattern `
        (Join-Path $root $service) `
        'GetFilePaths\(string folderPath\)\s*\{\s*if \(!Directory\.Exists\(folderPath\)\)\s*\{\s*return \[\];\s*\}' `
        "Listing files in $service must treat a missing folder as an empty set, not an error."
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $utilityType = $assembly.GetType('GalExcleTools.Services.WorkspacePathUtility', $true)
    $namesProperty = $utilityType.GetProperty('AssetLibraryCategoryFolderNames')
    if ($null -eq $namesProperty) {
        Write-Host '(skip category reflection: 当前 DLL 早于本次改动，重新编译后会核对「清单/建目录/枚举」这三层。)'
    }
    else {
        $names = @($namesProperty.GetValue($null))
        if ($names -notcontains '道具图片') {
            throw "唯一清单里没有「道具图片」：$($names -join '、')"
        }

        if ($names.Count -ne 8) {
            throw "分类数量变成 $($names.Count) 了（期望 8）：加/删分类时请一起更新这条断言，顺便想一下建目录与同步读目录是否都跟着走。"
        }

        # 每个分类的 getter 必须落在清单里的某个目录上 —— 这正是当初分叉的地方：
        # 同步读目录用的是这些 getter，建目录用的是另一份手写清单。
        # （构造 AssetLibraryInfo 走 GetConstructors()[0].Invoke 并逐个显式转型：
        #   Activator.CreateInstance 对 record 的可空参数/PSObject 包装不买账，会报"找不到构造函数"。）
        $infoType = $assembly.GetType('GalExcleTools.AssetLibraryInfo', $true)
        $info = $infoType.GetConstructors()[0].Invoke(@(
            [string]'测试素材库',
            [string]'素材库-测试',
            [string](Join-Path $env:TEMP 'gal-lib-probe'),
            [string]'',
            [bool]$false,
            [datetime]::Now))
        foreach ($getter in @(
            'GetBackgroundFolderPath', 'GetPropImageFolderPath', 'GetCharacterFolderPath',
            'GetMusicFolderPath', 'GetAmbientSoundFolderPath', 'GetSoundEffectFolderPath',
            'GetFunctionFolderPath', 'GetCharacterFilterFolderPath')) {
            $folder = $utilityType.GetMethod($getter).Invoke($null, @($info))
            $leaf = Split-Path -Leaf $folder
            if ($names -notcontains $leaf) {
                throw "分类 getter $getter 指向「$leaf」，但它不在唯一清单里 —— 建目录和读目录又开始分叉了。"
            }
        }

        # 真建一次：补目录之后，清单里的每一个都必须真的在磁盘上。
        [string]$tempLibrary = Join-Path $env:TEMP ('gal-library-' + [guid]::NewGuid().ToString('N'))
        try {
            $projectServiceType = $assembly.GetType('GalExcleTools.Services.ProjectWorkspaceService', $true)
            [void]$projectServiceType.GetMethod('EnsureAssetLibraryCategoryFolders').Invoke($null, @($tempLibrary))
            foreach ($folderPath in @($utilityType.GetMethod('GetAssetLibraryCategoryFolderPaths').Invoke($null, @($tempLibrary)))) {
                if (-not (Test-Path -LiteralPath $folderPath)) {
                    throw "补齐分类目录之后仍然缺：$folderPath"
                }
            }

            if (-not (Test-Path -LiteralPath (Join-Path $tempLibrary '道具图片'))) {
                throw '「道具图片」目录没有被补出来 —— 用户 2026-10-16 报的就是它。'
            }

            # 目录不存在时，两个枚举都要给空集（原来它们直接 EnumerateFiles → 抛）。
            foreach ($typeName in @('GalExcleTools.Services.ImageSetService', 'GalExcleTools.Services.AudioAssetService')) {
                $serviceType = $assembly.GetType($typeName, $true)
                [string]$missingFolder = Join-Path $tempLibrary ('不存在的分类-' + [guid]::NewGuid().ToString('N'))
                $listed = @($serviceType.GetMethod('GetFilePaths').Invoke($null, @($missingFolder)))
                if ($listed.Count -ne 0) {
                    throw "$typeName.GetFilePaths 对不存在的目录应当返回空集，实际返回了 $($listed.Count) 项。"
                }
            }
        }
        finally {
            Remove-Item -LiteralPath $tempLibrary -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Asset-library category-folder checks passed.'
