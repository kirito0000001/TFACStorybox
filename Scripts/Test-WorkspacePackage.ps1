$ErrorActionPreference = 'Stop'

# 项目包 / 素材库包（工作区级导出）：
#   · 导出要跳过"本机工件"（同步状态、中间产物、历史迁移备份、AI 填表备份）
#   · 但**备份**不能跳（备份要的是完整快照）
#   · 包里带一份清单，包名用"中文名_日期序号"
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
    (Join-Path $root 'Models\WorkspacePackageModels.cs') `
    'ProjectPackageTypeValue = "TFAC\.Project"[\s\S]*?AssetLibraryPackageTypeValue = "TFAC\.AssetLibrary"[\s\S]*?FileName = "package-manifest\.json"[\s\S]*?AssetLibraryFolderName[\s\S]*?FileCount[\s\S]*?TotalBytes' `
    'A workspace package must carry a manifest that names its type, its object and its paired asset library.'

Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    'ExportExcludedLocalFolderNames[\s\S]*?UnrealAssetIndexTables[\s\S]*?UnrealSync[\s\S]*?AIStoryFillBackups[\s\S]*?story-csv-export-transaction[\s\S]*?unreal-sync-state\.json[\s\S]*?ai-story-import-state\.json' `
    'Exporting must leave machine-local artifacts behind (sync state, caches, AI backups).'

Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    'ExportLocalArtifactFilePrefix[\s\S]*?KeptVoiceMapFileName' `
    'Only the real voice map may travel; its historical migration backups must not.'

# 备份 = 完整快照：本机那批**不能**跳。两处的过滤必须是分开的。
Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    'CreateBackup[\s\S]*?EnumerateBackupFiles\(folderPath, backupsPath\)[\s\S]*?ExportToZip[\s\S]*?excludeLocalArtifacts: true' `
    'Backups must stay complete snapshots while exports drop the machine-local artifacts.'

# 跳过判定要按"相对被导出文件夹"的路径段算 —— 从盘符算的话，
# 上层目录只要叫 ChapterBackups 之类，整份导出就会被跳空。
Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    'ShouldSkipBackupFile[\s\S]*?Path\.GetRelativePath\(folderPath, filePath\)[\s\S]*?IsBackupsFolderName' `
    'Skip decisions must be relative to the exported folder, not to the drive root.'

Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    'WriteManifestEntry\(archive, manifest\)[\s\S]*?archive\.CreateEntryFromFile' `
    'The manifest entry must be written before the payload so readers can peek it.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.cs') `
    'ExportProjectFromUiAsync[\s\S]*?SuggestedFileName = \$"\{SanitizeBackupFileName\(project\.Name\)\}_\{DateTime\.Now:yyyyMMdd_HHmmss\}"[\s\S]*?WorkspacePackageManifest\.ProjectPackageTypeValue[\s\S]*?AssetLibraryFolderName = project\.AssetLibraryFolderName' `
    'Exporting a project must be named after its display name and carry the paired asset library.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.cs') `
    'ExportAssetLibraryFromUiAsync[\s\S]*?SuggestedFileName = \$"\{SanitizeBackupFileName\(assetLibrary\.Name\)\}_\{DateTime\.Now:yyyyMMdd_HHmmss\}"[\s\S]*?WorkspacePackageManifest\.AssetLibraryPackageTypeValue' `
    'Exporting an asset library must be named after its display name (not the prefixed folder name).'

# ── 导入侧：先看条目表、直接铺到目标目录、挡 zip-slip、进度接到底部进度条 ──
Require-Pattern `
    (Join-Path $root 'Services\ProjectWorkspaceService.cs') `
    'ImportProjectArchive[\s\S]*?FindPackageMetaEntry[\s\S]*?ExtractPackageEntries[\s\S]*?DeleteDirectoryQuietly' `
    'Importing must peek the entry table, extract straight into the target, and clean up on failure.'

Require-Pattern `
    (Join-Path $root 'Services\ProjectWorkspaceService.cs') `
    'ExtractPackageEntries[\s\S]*?WorkspacePackageManifest\.FileName[\s\S]*?IsPathInsideDirectory\(destination, targetRoot\)' `
    'Extracting must not unpack the manifest into the workspace object, and must reject zip-slip entries.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Import.cs') `
    'new Progress<FolderBackupProgress>' `
    'Importing must wire the extraction progress to the bottom progress bar.'

# 进度文案里的名词由调用方给（"项目" / "素材库"）——
# 写死会说错话，从清单里推断则没有清单的调用又会退回错的词。谁调用谁清楚。
Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    'ExportToZip\(\s*string folderPath,\s*string exportPath,\s*string label' `
    'ExportToZip must take the noun for its progress copy from the caller.'

Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    '正在扫描要导出的\{noun\}文件' `
    'Export progress copy must use the caller-provided noun.'

Require-Pattern `
    (Join-Path $root 'Services\FolderBackupService.cs') `
    'GetPackageNoun' `
    'The guessed noun helper must stay removed (it is what printed the wrong word).' -NotMatch

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.cs') `
    'ExportProjectFromUiAsync[\s\S]*?_folderBackupService\.ExportToZip\([\s\S]*?"项目"[\s\S]*?manifest' `
    'The project export must label its progress copy as 项目.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.cs') `
    'ExportAssetLibraryFromUiAsync[\s\S]*?_folderBackupService\.ExportToZip\([\s\S]*?"素材库"[\s\S]*?manifest' `
    'The asset-library export must label its progress copy as 素材库.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $manifestType = $assembly.GetType('GalExcleTools.WorkspacePackageManifest', $false)
    if ($null -eq $manifestType) {
        Write-Host '(skip workspace-package reflection: 当前 DLL 早于本次改动，重新编译后会做「导出内容 + 清单 + 备份不跳 + 导入解压/zip-slip」这几项。)'
    }
    else {
        $backupServiceType = $assembly.GetType('GalExcleTools.Services.FolderBackupService', $true)
        $backupService = [System.Activator]::CreateInstance(
            $backupServiceType,
            @([System.Text.Json.JsonSerializerOptions]::new()))

        # 故意把临时工程放在一个叫 ChapterBackups 的目录里：
        # 老实现是拿"从盘符开始的路径段"去比目录名的，这种情况会把整份导出跳空。
        $outer = Join-Path $env:TEMP ('gal-package-test-' + [guid]::NewGuid().ToString('N'))
        $projectRoot = Join-Path $outer 'ChapterBackups\项目-测试项目'
        [string]$projectRoot = $projectRoot
        [string]$packagePath = Join-Path $outer 'out\测试项目_package.zip'
        [string]$backupFolderName = 'ChapterBackups'
        try {
            $keep = @(
                'Tools\project.meta.json',
                'Tools\story-todos.json',
                'Tools\story.voice-map.json',
                'Chapters\WHK-M1-00\story.document.json',
                'Chapters\WHK-M1-00\WHK-M1-00.csv',
                'Voice\WHK-M1-00\Vo-001.wav'
            )
            $drop = @(
                'Tools\unreal-sync-state.json',
                'Tools\story.voice-map.pre-index-fix-20260816-154103.json',
                'Tools\UnrealAssetIndexTables\ExsIndexMap.csv',
                'Tools\UnrealSync\gal-sync-manifest.json',
                'Tools\ProjectBackups\old.zip',
                'Tools\UnrealBackups\unreal.zip',
                'Chapters\WHK-M1-00\AIStoryFillBackups\story.document.before.json',
                'Chapters\WHK-M1-00\ai-story-import-state.json',
                '.story-csv-export-transaction\staged.csv'
            )
            # 身份文件得写**真 JSON**：导入要靠它认项目名/代号与素材库关联，
            # 随便塞一个字符的话会退回"用 zip 文件名当项目名"（那是它的兜底路径）。
            $projectMetaJson = '{ "ProjectName": "测试项目", "ProjectCode": "TEST", ' +
                '"AssetLibraryName": "测试素材库", "AssetLibraryFolderName": "素材库-测试素材库" }'
            foreach ($relative in ($keep + $drop)) {
                $full = Join-Path $projectRoot $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force | Out-Null
                $content = if ($relative -eq 'Tools\project.meta.json') { $projectMetaJson } else { 'x' }
                [System.IO.File]::WriteAllText($full, $content, [System.Text.UTF8Encoding]::new($false))
            }

            $manifest = [System.Activator]::CreateInstance($manifestType, $true)
            $manifest.PackageType = 'TFAC.Project'
            $manifest.PackageId = '0123456789abcdef0123456789abcdef'
            $manifest.ToolVersion = '9.9.9'
            $manifest.Name = '测试项目'
            $manifest.Code = 'TEST'
            $manifest.FolderName = '项目-测试项目'
            $manifest.AssetLibraryFolderName = '素材库-测试素材库'

            $exportMethod = $backupServiceType.GetMethod('ExportToZip')
            if ($exportMethod.GetParameters().Count -ne 6) {
                throw 'ExportToZip 应当有 6 个参数（folderPath / exportPath / label / progress / token / manifest）—— 文案名词由调用方给。'
            }

            $labels = @()
            foreach ($parameter in $exportMethod.GetParameters()) { $labels += $parameter.Name }
            if ($labels -notcontains 'label') {
                throw "ExportToZip 的第三个参数应当叫 label（实际：$($labels -join ' / ')）。"
            }

            [void]$exportMethod.Invoke(
                $backupService,
                @($projectRoot, $packagePath, '项目', $null, [System.Threading.CancellationToken]::None, $manifest))

            if (-not (Test-Path -LiteralPath $packagePath)) {
                throw '导出应当产出 zip 文件。'
            }

            $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
            try {
                $names = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
                if ($names.Count -eq 0 -or $names[0] -ne 'package-manifest.json') {
                    throw "清单必须是第一个条目（实际第一个是 $($names[0])）。"
                }

                foreach ($relative in $keep) {
                    $expected = $relative -replace '\\', '/'
                    if ($names -notcontains $expected) {
                        throw "该带走的文件没进包：$expected"
                    }
                }

                foreach ($relative in $drop) {
                    $unexpected = $relative -replace '\\', '/'
                    if ($names -contains $unexpected) {
                        throw "本机工件不该进包：$unexpected"
                    }
                }

                $manifestEntry = $archive.Entries | Where-Object { $_.FullName -eq 'package-manifest.json' }
                $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
                try { $packed = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                if ($packed.PackageType -ne 'TFAC.Project' -or
                    $packed.FileCount -ne $keep.Count -or
                    $packed.TotalBytes -le 0 -or
                    $packed.AssetLibraryFolderName -ne '素材库-测试素材库' -or
                    [string]::IsNullOrWhiteSpace($packed.ToolVersion)) {
                    throw "清单内容不对：type=$($packed.PackageType) files=$($packed.FileCount) bytes=$($packed.TotalBytes) lib=$($packed.AssetLibraryFolderName) ver=$($packed.ToolVersion)"
                }
            }
            finally {
                $archive.Dispose()
            }

            # 备份要的是完整快照：本机那批**不能**跳（和导出正好相反）。
            $backup = $backupServiceType.GetMethod('CreateBackup').Invoke(
                $backupService,
                @($projectRoot, $backupFolderName, 'TEST', '检查用', $null, [System.Threading.CancellationToken]::None))
            $backupArchive = [System.IO.Compression.ZipFile]::OpenRead($backup.Path)
            try {
                $backupNames = @($backupArchive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
                if ($backupNames -notcontains 'Tools/unreal-sync-state.json' -or
                    $backupNames -notcontains 'Tools/UnrealAssetIndexTables/ExsIndexMap.csv') {
                    throw '备份必须包含本机工件（备份是完整快照，不是"发给别人的包"）。'
                }

                if ($backupNames -contains 'Tools/ProjectBackups/old.zip') {
                    throw '备份不该把上一次的备份套进来。'
                }
            }
            finally {
                $backupArchive.Dispose()
            }

            # ── 导入侧：直接铺到目标目录（不再解到临时目录再拷一遍）+ zip-slip 防护 ──
            $projectServiceType = $assembly.GetType('GalExcleTools.Services.ProjectWorkspaceService', $true)
            $projectService = [System.Activator]::CreateInstance($projectServiceType, $true)
            [string]$importRoot = Join-Path $outer 'import-workspace'
            New-Item -ItemType Directory -Path $importRoot -Force | Out-Null

            $importMethod = $projectServiceType.GetMethod('ImportProjectArchive')
            if ($importMethod.GetParameters().Count -ne 4) {
                throw 'ImportProjectArchive 应当有 4 个参数（rootPath / archivePath / progress / token）—— 导入要能按条目报进度。'
            }

            $imported = $importMethod.Invoke(
                $projectService,
                @($importRoot, $packagePath, $null, [System.Threading.CancellationToken]::None))
            if ((Split-Path -Leaf $imported.Path) -ne '项目-测试项目') {
                throw "没有上层目录的包应当落到 项目-{项目名}，实际是 $($imported.Path)"
            }
            if (-not (Test-Path -LiteralPath (Join-Path $imported.Path 'Chapters\WHK-M1-00\story.document.json'))) {
                throw '导入应当把剧情文档铺出来。'
            }
            if (Test-Path -LiteralPath (Join-Path $imported.Path 'package-manifest.json')) {
                throw '清单是"关于这个包"的说明，不该铺进工作区对象里。'
            }
            $importedMeta = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $imported.Path 'Tools\project.meta.json') | ConvertFrom-Json
            if ($importedMeta.AssetLibraryFolderName -ne '素材库-测试素材库') {
                throw '导入必须保住项目与素材库的关联（AssetLibraryFolderName）。'
            }

            # 别人手动压的包（多一层目录）：要认，并且用那一层的名字当文件夹名。
            [string]$manualSource = Join-Path $outer 'manual-src'
            New-Item -ItemType Directory -Path (Join-Path $manualSource '项目-手压项目\Tools') -Force | Out-Null
            [System.IO.File]::WriteAllText(
                (Join-Path $manualSource '项目-手压项目\Tools\project.meta.json'),
                '{ "ProjectName": "手压项目", "ProjectCode": "MANUAL", "AssetLibraryName": "", "AssetLibraryFolderName": null }',
                [System.Text.UTF8Encoding]::new($false))
            [string]$manualZip = Join-Path $outer 'manual.zip'
            [System.IO.Compression.ZipFile]::CreateFromDirectory($manualSource, $manualZip)
            $manualImported = $importMethod.Invoke(
                $projectService,
                @($importRoot, $manualZip, $null, [System.Threading.CancellationToken]::None))
            if ((Split-Path -Leaf $manualImported.Path) -ne '项目-手压项目') {
                throw "有上层目录的包应当沿用那一层的名字，实际是 $($manualImported.Path)"
            }

            # zip-slip：条目名用 .. 往目标目录外面跑，必须拒绝，且不能在目标外留下东西。
            [string]$evilZip = Join-Path $outer 'evil.zip'
            $evilArchive = [System.IO.Compression.ZipFile]::Open($evilZip, [System.IO.Compression.ZipArchiveMode]::Create)
            try {
                foreach ($pair in @(
                    @{ Name = 'Tools/project.meta.json'; Text = '{ "ProjectName": "坏包", "ProjectCode": "EVIL" }' },
                    @{ Name = '../escaped.txt'; Text = 'x' }
                )) {
                    $entry = $evilArchive.CreateEntry($pair.Name)
                    $writer = [System.IO.StreamWriter]::new($entry.Open())
                    try { $writer.Write($pair.Text) } finally { $writer.Dispose() }
                }
            }
            finally {
                $evilArchive.Dispose()
            }

            $rejected = $false
            try {
                [void]$importMethod.Invoke(
                    $projectService,
                    @($importRoot, $evilZip, $null, [System.Threading.CancellationToken]::None))
            }
            catch {
                $rejected = $true
            }

            if (-not $rejected) {
                throw '带 .. 的条目必须被拒绝（zip-slip）。'
            }
            foreach ($escaped in @((Join-Path $importRoot 'escaped.txt'), (Join-Path $outer 'escaped.txt'))) {
                if (Test-Path -LiteralPath $escaped) {
                    throw "zip-slip 条目跑到了目标目录之外：$escaped"
                }
            }
            if (Test-Path -LiteralPath (Join-Path $importRoot '项目-坏包')) {
                throw '导入失败后必须把刚铺的那半份目录清掉。'
            }
        }
        finally {
            Remove-Item -LiteralPath $outer -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Workspace package (project / asset library) checks passed.'
