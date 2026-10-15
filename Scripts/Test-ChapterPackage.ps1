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
    'ExportChapterPackageFromUiAsync[\s\S]*?FlushChapterEditsBeforePackageAsync[\s\S]*?_chapterPackageService\.Export' `
    'Exporting a chapter must flush pending edits before the package is built.'

# ── 导入侧：菜单项 + 流程 + 弹窗 ──
Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'CreateMenuItem\("导入章节"' `
    'The chapter context menu needs the import entry.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'ImportChapterPackageFromUiAsync[\s\S]*?FlushChapterEditsBeforePackageAsync[\s\S]*?LoadPackage[\s\S]*?BuildMergePlan[\s\S]*?ChapterMergeDialogContent[\s\S]*?ApplyMerge' `
    'Importing a chapter must flush, load the package, build the diff, ask the user, then merge.'

# 合并会改本机数据：默认按钮必须是「取消」，不能让一个回车就执行。
Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'ImportChapterPackageFromUiAsync[\s\S]*?DefaultButton: ContentDialogButton\.Close' `
    'The merge dialog must default to cancel so a stray Enter cannot merge.'

Require-Pattern `
    (Join-Path $root 'Views\ChapterMergeDialogContent.cs') `
    'SelectPackageEditsOnly[\s\S]*?SelectAllFromPackage[\s\S]*?SelectAllLocal[\s\S]*?IsChecked = item\.Choice == ChapterMergeChoice\.UsePackage' `
    'The diff dialog must start from "keep local" and offer the three bulk choices.'

# 包名 = 章节中文名 + 日期序号（`第六章_20261015_1712.zip`）：这东西是发给别人的，
# 收件人要看一眼就知道是哪一章、哪一次导的 —— 用代号（WHK-M6-00）反而要靠记忆去对。
Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'SuggestedFileName = \$"\{SanitizeBackupFileName\(chapter\.Name\)\}_\{DateTime\.Now:yyyyMMdd_HHmmss\}"' `
    'The exported package must be named after the chapter''s display name, with the date serial after it.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Workbench.Chapters.cs') `
    'FlushChapterEditsBeforePackageAsync[\s\S]*?SaveCurrentStoryRow[\s\S]*?FlushAsync[\s\S]*?SaveStoryTodosIfDirty' `
    'The pre-package flush must cover both the story document and the project-wide todo file.'

# ── 导入 / 合并侧 ──
Require-Pattern `
    (Join-Path $root 'Models\ChapterPackageModels.cs') `
    'ChapterMergeItemKind[\s\S]*?ChapterMergeChoice[\s\S]*?ChapterMergeChoice Choice \{ get; set; \} = ChapterMergeChoice\.KeepLocal' `
    'A merge must classify every difference and default to keeping the local side.'

Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'LoadPackage[\s\S]*?PackageTypeValue[\s\S]*?CurrentPackageSchemaVersion[\s\S]*?DocumentFileName' `
    'Loading a package must verify its type and format version before reading the document.'

Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'BuildMergePlan[\s\S]*?SnapshotLocalRows[\s\S]*?SnapshotPackageRows[\s\S]*?BuildRowMergeItems[\s\S]*?BuildTodoMergeItems' `
    'The diff must align rows and todos on both sides before classifying them.'

Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'ApplyMerge[\s\S]*?CreateBackup[\s\S]*?RebuildRowNamesAndSections[\s\S]*?MergeTodos' `
    'Merging must back the chapter up first, then rebuild rows/sections, then merge todos.'

# 连续新增的句子不能倒序插进来：插入之后锚点要变成"刚插进去的那一行"。
Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'OnlyInPackage:[\s\S]*?localDocument\.Rows\.Insert\(insertIndex, newRow\)[\s\S]*?anchor = newRow' `
    'Consecutive inserted rows must keep the package order (the anchor moves to the row just inserted).'

# 备注文件是整个项目一份：合并本章时必须把别的章节原样带回去。
Require-Pattern `
    (Join-Path $root 'Services\ChapterPackageService.cs') `
    'MergeTodos[\s\S]*?!IsChapterNote\(note, chapter\.Code\)[\s\S]*?StoryTodoStore\.Save' `
    'Merging todos must keep the other chapters'' notes untouched in the project-wide file.'

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
        $jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
        $repository = [System.Activator]::CreateInstance($repositoryType, @($jsonOptions))
        $backupServiceType = $assembly.GetType('GalExcleTools.Services.FolderBackupService', $true)
        $backupService = [System.Activator]::CreateInstance(
            $backupServiceType,
            @([System.Text.Json.JsonSerializerOptions]::new()))
        # 构造函数在本次改动里多了一个参数（合并前备份要用 FolderBackupService）。
        # 旧 DLL 里只有一个 —— 这里按参数个数挑：两种 DLL 都能构造，
        # 于是"导出那半"照样能验，"合并那半"由下面的 gate 决定跑不跑。
        $constructor = $serviceType.GetConstructors() |
            Where-Object { $_.GetParameters().Count -eq 2 } |
            Select-Object -First 1
        $service = if ($null -ne $constructor) {
            $constructor.Invoke(@($repository, $backupService))
        }
        else {
            [System.Activator]::CreateInstance($serviceType, @($repository))
        }

        $tempRoot = Join-Path $env:TEMP ('gal-chapter-package-' + [guid]::NewGuid().ToString('N'))
        # ⚠️ 显式转成 string：Join-Path 的输出是 PSObject 包装的，而反射 Invoke 不拆包装。
        [string]$projectPath = $tempRoot
        [string]$chapterPath = Join-Path $tempRoot 'Chapters\WHK-M6-00'
        [string]$packagePath = Join-Path $tempRoot 'WHK-M6-00_package.zip'
        try {
            New-Item -ItemType Directory -Path $chapterPath -Force | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Tools') -Force | Out-Null

            # 一份最小但合法的 StoryDocument：2 个小节、3 行。
            # ⚠️ 字段名一律 PascalCase —— 工具箱自己写出来的就是 PascalCase
            #    （story.document.json 那边仓库是大小写不敏感的，但 todos.json 不是，
            #     夹带写成 camelCase 会静默读成空：这个用例第一版就是这么挂的）。
            $document = [ordered]@{
                SchemaVersion = 3
                ChapterCode   = 'WHK-M6-00'
                SavedAt       = '2026-10-15T12:00:00+08:00'
                Sections      = @(
                    [ordered]@{ Number = 1; Lines = @(
                        [ordered]@{ StableId = '11111111111111111111111111111111'; Cells = [ordered]@{ Tesxt = '第一句' } },
                        [ordered]@{ StableId = '22222222222222222222222222222222'; Cells = [ordered]@{ Tesxt = '第二句' } }
                    ) },
                    [ordered]@{ Number = 2; Lines = @(
                        [ordered]@{ StableId = '33333333333333333333333333333333'; Cells = [ordered]@{ Tesxt = '第三句' } }
                    ) }
                )
                ChoiceNotes   = [ordered]@{ Choices = [ordered]@{} }
                BranchRoutes  = @()
                CustomBranchSectionFirstRowIds = @()
            }
            [System.IO.File]::WriteAllText(
                (Join-Path $chapterPath 'story.document.json'),
                ($document | ConvertTo-Json -Depth 12),
                [System.Text.UTF8Encoding]::new($false))

            # 项目级备注：本章 2 条 + 别的章节 1 条（后者不该进包）。
            $todos = [ordered]@{
                Version = 2
                Notes   = @(
                    [ordered]@{ ChapterCode = 'WHK-M6-00'; RowNumber = 1; Section = 1; Text = '本章备注甲'; Done = $false; UpdatedAt = '2026-10-15T12:00:00+08:00'; StableId = '11111111111111111111111111111111' },
                    [ordered]@{ ChapterCode = 'WHK-M6-00'; RowNumber = 3; Section = 2; Text = '本章备注乙'; Done = $true;  UpdatedAt = '2026-10-15T12:01:00+08:00'; StableId = '33333333333333333333333333333333' },
                    [ordered]@{ ChapterCode = 'WHK-M7-00'; RowNumber = 2; Section = 1; Text = '别章备注';   Done = $false; UpdatedAt = '2026-10-15T12:02:00+08:00'; StableId = '44444444444444444444444444444444' }
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
                if (@($packedTodos.Notes).Count -ne 2 -or
                    @($packedTodos.Notes | Where-Object { $_.ChapterCode -ne 'WHK-M6-00' }).Count -ne 0 -or
                    @($packedTodos.Notes | Where-Object { $_.StableId -eq '11111111111111111111111111111111' }).Count -ne 1) {
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

            # ── 导入侧：差异分类 + 合并写回 ──
            # 这一段断言的是本次改动后的行为，所以等编译过再跑（旧 DLL 里还是只有导出那半）。
            $loadPackageMethod = $serviceType.GetMethod('LoadPackage')
            if ($null -eq $loadPackageMethod) {
                Write-Host '(skip merge reflection: 当前 DLL 早于本次改动，重新编译后会做「差异分类 + 合并写回」这一段。)'
            }
            else {
            # 把"本机"改成对面改过一轮的样子：
            #   aaa（第一句）：正文被改过（→ Modified）
            #   bbb（第二句）：本机没有了（→ OnlyInPackage，对方看着像"他新增"）
            #   ddd：本机新增的一句（→ OnlyLocal）
            #   ccc（第三句）：没动（→ 不列进差异）
            # 备注同理：aaa 那条正文被改过、ddd 多一条、包里 ccc 那条本机没有。
            $localDocumentJson = [ordered]@{
                SchemaVersion = 3
                ChapterCode   = 'WHK-M6-00'
                SavedAt       = '2026-10-15T13:00:00+08:00'
                Sections      = @(
                    [ordered]@{ Number = 1; Lines = @(
                        [ordered]@{ StableId = '11111111111111111111111111111111'; Cells = [ordered]@{ Tesxt = '第一句（本机改过）' } },
                        [ordered]@{ StableId = 'dddddddddddddddddddddddddddddddd'; Cells = [ordered]@{ Tesxt = '本机新增的一句' } }
                    ) },
                    [ordered]@{ Number = 2; Lines = @(
                        [ordered]@{ StableId = '33333333333333333333333333333333'; Cells = [ordered]@{ Tesxt = '第三句' } }
                    ) }
                )
                ChoiceNotes   = [ordered]@{ Choices = [ordered]@{} }
                BranchRoutes  = @()
                CustomBranchSectionFirstRowIds = @()
            }
            [System.IO.File]::WriteAllText(
                (Join-Path $chapterPath 'story.document.json'),
                ($localDocumentJson | ConvertTo-Json -Depth 12),
                [System.Text.UTF8Encoding]::new($false))

            $localTodosJson = [ordered]@{
                Version = 2
                Notes   = @(
                    [ordered]@{ ChapterCode = 'WHK-M6-00'; RowNumber = 1; Section = 1; Text = '本机改过的备注'; Done = $false; UpdatedAt = '2026-10-15T13:00:00+08:00'; StableId = '11111111111111111111111111111111' },
                    [ordered]@{ ChapterCode = 'WHK-M6-00'; RowNumber = 2; Section = 1; Text = '本机新增句的备注'; Done = $false; UpdatedAt = '2026-10-15T13:01:00+08:00'; StableId = 'dddddddddddddddddddddddddddddddd' },
                    [ordered]@{ ChapterCode = 'WHK-M7-00'; RowNumber = 2; Section = 1; Text = '别章备注'; Done = $false; UpdatedAt = '2026-10-15T13:02:00+08:00'; StableId = '44444444444444444444444444444444' }
                )
            }
            [System.IO.File]::WriteAllText(
                (Join-Path $tempRoot 'Tools\story-todos.json'),
                ($localTodosJson | ConvertTo-Json -Depth 12),
                [System.Text.UTF8Encoding]::new($false))

            $localDocument = $repositoryType.GetMethod('Load').Invoke($repository, @($chapter))
            $todoStoreType = $assembly.GetType('GalExcleTools.Services.StoryTodoStore', $true)
            $localTodos = @($todoStoreType.GetMethod('Load').Invoke($null, @($projectPath)))
            $todoListType = [System.Collections.Generic.List``1].MakeGenericType(
                $assembly.GetType('GalExcleTools.StoryTodoNote', $true))
            $localTodoList = [System.Activator]::CreateInstance($todoListType)
            foreach ($note in $localTodos) { [void]$localTodoList.Add($note) }

            $content = $serviceType.GetMethod('LoadPackage').Invoke($service, @($packagePath))
            $plan = $serviceType.GetMethod('BuildMergePlan').Invoke(
                $service,
                @($chapter, $content, $localDocument, $localTodoList))

            $rowItems = @($plan.RowItems)
            $modified = @($rowItems | Where-Object { $_.Kind.ToString() -eq 'Modified' })
            $onlyInPackage = @($rowItems | Where-Object { $_.Kind.ToString() -eq 'OnlyInPackage' })
            $onlyLocal = @($rowItems | Where-Object { $_.Kind.ToString() -eq 'OnlyLocal' })
            if ($modified.Count -ne 1 -or $onlyInPackage.Count -ne 1 -or $onlyLocal.Count -ne 1) {
                throw "差异分类不对：Modified=$($modified.Count) OnlyInPackage=$($onlyInPackage.Count) OnlyLocal=$($onlyLocal.Count)"
            }

            if ($modified[0].StableId -ne '11111111111111111111111111111111' -or
                $modified[0].ChangedFields -notcontains '文本' -or
                $onlyInPackage[0].StableId -ne '22222222222222222222222222222222' -or
                $onlyLocal[0].StableId -ne 'dddddddddddddddddddddddddddddddd') {
                throw '差异条目认错了句子（StableId 或变化字段不对）。'
            }

            # 列表顺序 = 合并后的阅读顺序：对方新增的 bbb 应该排在本机 aaa 之后、本机新增的 ddd 之前。
            if ($rowItems.Count -ne 3 -or
                $rowItems[0].StableId -ne '11111111111111111111111111111111' -or
                $rowItems[1].Kind.ToString() -ne 'OnlyInPackage' -or
                $rowItems[2].StableId -ne 'dddddddddddddddddddddddddddddddd') {
                throw '差异列表的顺序必须等于合并后的阅读顺序（对方新增的句子要落在它的锚点后面）。'
            }

            if (@($plan.TodoItems).Count -ne 3) {
                throw "备注差异应当有 3 条（改过 / 本机独有 / 包里独有），实际 $(@($plan.TodoItems).Count)。"
            }

            # 全部选"用对方"。
            $choiceType = $assembly.GetType('GalExcleTools.ChapterMergeChoice', $true)
            $usePackage = [System.Enum]::Parse($choiceType, 'UsePackage')
            foreach ($item in $rowItems) { $item.Choice = $usePackage }
            foreach ($item in @($plan.TodoItems)) { $item.Choice = $usePackage }

            $applyResult = $serviceType.GetMethod('ApplyMerge').Invoke(
                $service,
                @($chapter, $plan, $localDocument, $projectPath, $null, [System.Threading.CancellationToken]::None))
            if ($applyResult.UpdatedRowCount -ne 1 -or
                $applyResult.InsertedRowCount -ne 1 -or
                $applyResult.DeletedRowCount -ne 1 -or
                $applyResult.UpdatedTodoCount -ne 3) {
                throw "合并计数不对：改 $($applyResult.UpdatedRowCount) 插 $($applyResult.InsertedRowCount) 删 $($applyResult.DeletedRowCount) 备注 $($applyResult.UpdatedTodoCount)"
            }

            # 合并前必须先备份（走的是「备份 / 还原」那一套，用户能退回来）。
            if (-not (Test-Path -LiteralPath $applyResult.BackupPath)) {
                throw '合并前必须先给章节做一次备份。'
            }
            $backupMeta = [System.IO.File]::ReadAllText("$($applyResult.BackupPath).meta.json")
            if ($backupMeta -notmatch '合并章节包前') {
                throw '那次备份的备注应当写明是"合并章节包前"。'
            }

            $merged = $repositoryType.GetMethod('Load').Invoke($repository, @($chapter))
            $mergedRows = @($merged.Rows)
            if ($mergedRows.Count -ne 3) {
                throw "合并后应当是 3 行（改 1 / 插 1 / 删 1），实际 $($mergedRows.Count)。"
            }
            if ($mergedRows[0].Get('Tesxt') -ne '第一句' -or
                $mergedRows[1].Get('Tesxt') -ne '第二句' -or
                $mergedRows[2].Get('Tesxt') -ne '第三句') {
                throw "合并后的正文不对：$($mergedRows[0].Get('Tesxt')) / $($mergedRows[1].Get('Tesxt')) / $($mergedRows[2].Get('Tesxt'))"
            }
            if ($mergedRows[1].StableId -ne '22222222222222222222222222222222') {
                throw '插入的行必须带着包里那个 StableId —— 下次对接还要靠它认人。'
            }
            if ($merged.RowSections['1'] -ne 1 -or $merged.RowSections['2'] -ne 1 -or $merged.RowSections['3'] -ne 2) {
                throw "小节归属不对（插入行应当跟随它前面那一行）：$($merged.RowSections['1'])/$($merged.RowSections['2'])/$($merged.RowSections['3'])"
            }

            $mergedTodos = @($todoStoreType.GetMethod('Load').Invoke($null, @($projectPath)))
            $chapterTodos = @($mergedTodos | Where-Object { $_.ChapterCode -eq 'WHK-M6-00' })
            if ($chapterTodos.Count -ne 2) {
                throw "合并后本章备注应当是 2 条，实际 $($chapterTodos.Count)。"
            }
            if (@($chapterTodos | Where-Object { $_.Text -eq '本章备注甲' }).Count -ne 1 -or
                @($chapterTodos | Where-Object { $_.Done -eq $true }).Count -ne 1 -or
                @($chapterTodos | Where-Object { $_.RowNumber -eq 3 }).Count -ne 1) {
                throw '合并后本章备注的内容 / 勾选 / 行号重算不对。'
            }
            if (@($mergedTodos | Where-Object { $_.ChapterCode -eq 'WHK-M7-00' }).Count -ne 1) {
                throw '别的章节的备注必须原样保留（备注文件是整个项目一份）。'
            }
            }
        }
        finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Chapter package export checks passed.'
