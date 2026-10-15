$ErrorActionPreference = 'Stop'

# 章节编辑包（TFAC.ChapterEdit）：导出侧。给"两边分工写同一章"做对接用，
# 包里只放剧情主数据 + 本章 TODO 备注 + 清单，不传素材。
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
    (Join-Path $root 'Models\ChapterPackageModels.cs') `
    'PackageTypeValue = "TFAC\.ChapterEdit"[\s\S]*?PackageId[\s\S]*?StoryDocumentSha256[\s\S]*?RowCount[\s\S]*?SectionCount[\s\S]*?TodoCount' `
    'The chapter package manifest must identify itself and carry the fields later merges need.'

Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'ManifestFileName[\s\S]*?DocumentFileName[\s\S]*?TodoFileName' `
    'A chapter package must contain the manifest, the story document, and this chapter''s todos.'

Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'CreateEntryFromFile\(documentPath, DocumentFileName' `
    'The story document must be copied byte-for-byte so the other side reads exactly this revision.'

Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'LoadChapterTodos[\s\S]*?StoryTodoStore[\s\S]*?ChapterCode, chapterCode' `
    'Only this chapter''s todos may be packed (the todo file is project-wide).'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'Content="?导出章节"?|CreateMenuItem\("导出章节"' `
    'The chapter context menu needs the export entry.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'ExportChapterPackageFromUiAsync[\s\S]*?FlushChapterEditsBeforeExportAsync[\s\S]*?_chapterPackageService\.Export' `
    'Exporting a chapter must flush pending edits before the package is built.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'FlushChapterEditsBeforeExportAsync[\s\S]*?SaveCurrentStoryRow[\s\S]*?FlushAsync[\s\S]*?SaveStoryTodosIfDirty' `
    'The pre-export flush must cover both the story document and the project-wide todo file.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    # ⚠️ 这个类型是本次新增的：用 GetType(..., $false)（不抛）来判新旧 ——
    # 写 $true 的话，DLL 还没编译出来时这里会直接抛，绕过下面那段 skip 提示。
    $serviceType = $assembly.GetType('GalExcleTools.Services.ChapterPackageService', $false)
    if ($null -eq $serviceType) {
        Write-Host '(skip chapter-package reflection: 当前 DLL 早于本次改动，重新编译后会做「拆包核对」这一项。)'
    }
    else {
        $chapterType = $assembly.GetType('GalExcleTools.ChapterInfo', $true)
        $repositoryType = $assembly.GetType('GalExcleTools.Services.StoryDocumentRepository', $true)
        $repository = [System.Activator]::CreateInstance(
            $repositoryType,
            @([System.Text.Json.JsonSerializerOptions]::new()))
        $service = [System.Activator]::CreateInstance($serviceType, @($repository))

        $tempRoot = Join-Path $env:TEMP ('gal-chapter-package-' + [guid]::NewGuid().ToString('N'))
        # ⚠️ 显式转成 string：Join-Path 的输出是 PSObject 包装的，而反射 Invoke 不拆包装。
        [string]$projectPath = $tempRoot
        [string]$chapterPath = Join-Path $tempRoot 'Chapters\WHK-M6-00'
        [string]$packagePath = Join-Path $tempRoot 'WHK-M6-00_package.zip'
        try {
            New-Item -ItemType Directory -Path $chapterPath -Force | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Tools') -Force | Out-Null

            # 一份最小但合法的 StoryDocument：2 个小节、3 行。
            $document = [ordered]@{
                schemaVersion = 3
                chapterCode   = 'WHK-M6-00'
                savedAt       = '2026-10-15T12:00:00+08:00'
                sections      = @(
                    [ordered]@{ number = 1; lines = @(
                        [ordered]@{ stableId = '11111111111111111111111111111111'; cells = [ordered]@{ Tesxt = '第一句' } },
                        [ordered]@{ stableId = '22222222222222222222222222222222'; cells = [ordered]@{ Tesxt = '第二句' } }
                    ) },
                    [ordered]@{ number = 2; lines = @(
                        [ordered]@{ stableId = '33333333333333333333333333333333'; cells = [ordered]@{ Tesxt = '第三句' } }
                    ) }
                )
                choiceNotes   = [ordered]@{ choices = [ordered]@{} }
                branchRoutes  = @()
                customBranchSectionFirstRowIds = @()
            }
            [System.IO.File]::WriteAllText(
                (Join-Path $chapterPath 'story.document.json'),
                ($document | ConvertTo-Json -Depth 12),
                [System.Text.UTF8Encoding]::new($false))

            # 项目级备注：本章 2 条 + 别的章节 1 条（后者不该进包）。
            $todos = [ordered]@{
                version = 2
                notes   = @(
                    [ordered]@{ chapterCode = 'WHK-M6-00'; rowNumber = 1; section = 1; text = '本章备注甲'; done = $false; updatedAt = '2026-10-15T12:00:00+08:00'; stableId = '11111111111111111111111111111111' },
                    [ordered]@{ chapterCode = 'WHK-M6-00'; rowNumber = 3; section = 2; text = '本章备注乙'; done = $true;  updatedAt = '2026-10-15T12:01:00+08:00'; stableId = '33333333333333333333333333333333' },
                    [ordered]@{ chapterCode = 'WHK-M7-00'; rowNumber = 2; section = 1; text = '别章备注';   done = $false; updatedAt = '2026-10-15T12:02:00+08:00'; stableId = '44444444444444444444444444444444' }
                )
            }
            [System.IO.File]::WriteAllText(
                (Join-Path $tempRoot 'Tools\story-todos.json'),
                ($todos | ConvertTo-Json -Depth 12),
                [System.Text.UTF8Encoding]::new($false))

            $chapter = [System.Activator]::CreateInstance(
                $chapterType,
                @('第六章', 'WHK-M6-00', 'MainThread', $chapterPath, [datetime]::Now, 0))
            $export = $serviceType.GetMethod('Export').Invoke($service, @($chapter, $projectPath, $packagePath))
            $manifest = $export.Manifest

            if (-not (Test-Path -LiteralPath $packagePath)) {
                throw 'Exporting a chapter must produce a package file.'
            }

            if ($manifest.PackageType -ne 'TFAC.ChapterEdit' -or
                $manifest.ChapterCode -ne 'WHK-M6-00' -or
                $manifest.RowCount -ne 3 -or
                $manifest.SectionCount -ne 2 -or
                $manifest.TodoCount -ne 2 -or
                $manifest.DocumentSchemaVersion -ne 3 -or
                [string]::IsNullOrWhiteSpace($manifest.StoryDocumentSha256) -or
                [string]::IsNullOrWhiteSpace($manifest.PackageId)) {
                throw "The chapter package manifest is wrong: type=$($manifest.PackageType) code=$($manifest.ChapterCode) rows=$($manifest.RowCount) sections=$($manifest.SectionCount) todos=$($manifest.TodoCount) schema=$($manifest.DocumentSchemaVersion)"
            }

            $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
            try {
                $names = @($archive.Entries | ForEach-Object { $_.FullName })
                foreach ($expected in @('chapter-package.json', 'story.document.json', 'todos.json')) {
                    if ($names -notcontains $expected) {
                        throw "The chapter package is missing $expected (has: $($names -join ', '))."
                    }
                }

                $todoEntry = $archive.Entries | Where-Object { $_.FullName -eq 'todos.json' }
                $reader = [System.IO.StreamReader]::new($todoEntry.Open())
                try { $packedTodos = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                if (@($packedTodos.notes).Count -ne 2 -or
                    @($packedTodos.notes | Where-Object { $_.chapterCode -ne 'WHK-M6-00' }).Count -ne 0 -or
                    @($packedTodos.notes | Where-Object { $_.stableId -eq '11111111111111111111111111111111' }).Count -ne 1) {
                    throw 'Only this chapter''s todos (with their stable ids) may be packed.'
                }

                # 文档必须与源文件字节一致（对方读到的就是我这一版）。
                $sourceBytes = [System.IO.File]::ReadAllBytes((Join-Path $chapterPath 'story.document.json'))
                $docEntry = $archive.Entries | Where-Object { $_.FullName -eq 'story.document.json' }
                $stream = $docEntry.Open()
                try {
                    $memory = [System.IO.MemoryStream]::new()
                    $stream.CopyTo($memory)
                    $packedBytes = $memory.ToArray()
                }
                finally { $stream.Dispose() }
                if ($packedBytes.Length -ne $sourceBytes.Length) {
                    throw 'The packed story document must be the same bytes as the chapter file.'
                }
            }
            finally {
                $archive.Dispose()
            }
        }
        finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Chapter package export checks passed.'
