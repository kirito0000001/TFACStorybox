using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using static GalExcleTools.Services.FileSystemUtility;

namespace GalExcleTools.Services;

/// <summary>
/// 章节编辑包（<c>TFAC.ChapterEdit</c>）：给"两边分工写同一章"做对接用的**数据**包。
///
/// 【包里放什么、不放什么】
/// 放：<c>story.document.json</c>（剧情主数据，原样搬）、<c>todos.json</c>（这一章的 TODO 备注）、
/// <c>chapter-package.json</c>（清单）。
/// 不放：任何素材（两边的素材是一致的 —— 用户的原话："不传递素材，因为两边素材一致"），
/// 也不放 CSV / AI 填表备份 / 备份目录 —— 那些要么是派生文件，要么是"自己给自己回退"用的，
/// 混进对接包里只会让"谁的是最新"变成一锅粥。
///
/// 【为什么是 zip 而不是目录】
/// 这个包要**发给对面的人**（聊天工具、网盘、U 盘），单个文件比一个目录省事得多。
/// （AI 剧情填表那套是给 AI 读的、就地读目录更顺，所以它是目录 —— 场景不同。）
///
/// 【对齐键】
/// 剧情行与 TODO 备注的定位键都是行的 StableId（GUID）。两边从同一份文档分出去之后，
/// 同一个 StableId 在两边指同一句 —— 导入时才能逐句比对、逐句选边。
/// 清单里那份 SHA-256 是"两边是不是同源"的依据（做法照 AI 剧情填表那套）。
/// </summary>
internal sealed class ChapterPackageService
{
    public const string ManifestFileName = "chapter-package.json";
    public const string DocumentFileName = "story.document.json";
    public const string TodoFileName = "todos.json";

    /// <summary>
    /// 清单和备注用缩进 + 不转义中文：这两个文件是给人看的（排查时直接打开 zip 就能读）。
    /// 故事文档本身走 <see cref="ZipArchive.CreateEntryFromFile"/> 原样搬，不经过这里。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly StoryDocumentRepository _repository;
    private readonly FolderBackupService _folderBackupService;

    /// <summary>读包时用：和 StoryDocumentRepository 一样对属性名大小写不敏感（包是我们自己写的，
    /// 读的时候宽容一点没坏处；写的时候仍然是人能直接看的 PascalCase）。</summary>
    private static readonly JsonSerializerOptions PackageReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>包格式版本上限：比这还新的包不读（多半是对方工具更新了）。</summary>
    private const int CurrentPackageSchemaVersion = 1;

    public ChapterPackageService(StoryDocumentRepository repository, FolderBackupService folderBackupService)
    {
        _repository = repository;
        _folderBackupService = folderBackupService;
    }

    /// <summary>
    /// 把一章导成编辑包。
    ///
    /// ⚠️ **调用方要先落盘**（见 MainWindow 里那个 FlushChapterEditsBeforePackageAsync）：
    /// 这里读的是磁盘上的 <c>story.document.json</c>，编辑器的自动保存有 debounce，
    /// 不先 flush 的话包里会少最后一拍。
    /// </summary>
    /// <param name="chapter">要导出的章节。</param>
    /// <param name="projectPath">项目根目录（用来从项目级备注文件里切出这一章的 TODO）。</param>
    /// <param name="packagePath">包落到哪（.zip，已存在就覆盖）。</param>
    public ChapterPackageExportResult Export(
        ChapterInfo chapter,
        string? projectPath,
        string packagePath)
    {
        if (chapter is null)
        {
            throw new ArgumentNullException(nameof(chapter));
        }

        if (!Directory.Exists(chapter.Path))
        {
            throw new DirectoryNotFoundException($"章节文件夹不存在：{chapter.Path}");
        }

        var documentPath = _repository.GetDocumentPath(chapter);
        if (!File.Exists(documentPath))
        {
            throw new FileNotFoundException(
                "这一章还没有 StoryDocument（story.document.json）。先打开一次章节编辑器再导出。",
                documentPath);
        }

        var documentState = JsonSerializer.Deserialize<StoryDocumentFile>(
                File.ReadAllText(documentPath),
                JsonOptions)
            ?? throw new InvalidDataException($"StoryDocument 无法解析：{documentPath}");

        // 走一遍仓库的读法拿行数 / 小节数（顺带校验章节代号对不对得上）。
        var document = _repository.Load(chapter);

        var todos = LoadChapterTodos(projectPath, chapter.Code);
        var manifest = new ChapterPackageManifest
        {
            PackageId = Guid.NewGuid().ToString("N"),
            ExportedAt = DateTimeOffset.Now,
            ToolVersion = AppVersionService.GetDisplayVersion(),
            ChapterCode = chapter.Code,
            ChapterName = chapter.Name,
            ChapterType = chapter.Type,
            DocumentSchemaVersion = documentState.SchemaVersion,
            StoryDocumentSha256 = ComputeFileHash(documentPath),
            RowCount = document.Rows.Count,
            SectionCount = document.Rows.Count == 0
                ? 0
                : document.RowSections.Values.DefaultIfEmpty(1).Max(),
            TodoCount = todos.Count,
            Files = [ManifestFileName, DocumentFileName, TodoFileName]
        };

        var targetPath = Path.GetFullPath(packagePath);
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new IOException("导出路径无效。");
        }

        Directory.CreateDirectory(targetDirectory);

        // 先写临时文件再改名 —— 和仓库里其它落盘一个规矩：中途失败不会留下半个包，
        // 也不会把用户原来的同名文件弄坏。
        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, ManifestFileName, JsonSerializer.Serialize(manifest, JsonOptions));
                // 故事文档**原样搬字节**：对方读到的就是这一份，连 SavedAt / 字段顺序都不变。
                archive.CreateEntryFromFile(documentPath, DocumentFileName, CompressionLevel.Optimal);
                WriteTextEntry(
                    archive,
                    TodoFileName,
                    JsonSerializer.Serialize(new StoryTodoFile { Notes = todos }, JsonOptions));
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            // 成功时它已经改名挪走了；这句只清失败路径留下的残片。
            File.Delete(temporaryPath);
        }

        return new ChapterPackageExportResult(targetPath, manifest);
    }

    // ────────────────────────────── 导入侧：拆包 ──────────────────────────────

    /// <summary>
    /// 拆开一个章节编辑包。
    /// 缺件 / 类型不对 / 版本太新都抛 <see cref="InvalidDataException"/> —— 消息是可以直接给用户看的那种。
    /// </summary>
    public ChapterPackageContent LoadPackage(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new FileNotFoundException("包文件不存在。", packagePath);
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadJsonEntry<ChapterPackageManifest>(archive, ManifestFileName)
            ?? throw new InvalidDataException("这个 zip 里没有章节包清单（chapter-package.json）—— 确定是章节编辑包吗？");
        if (!string.Equals(manifest.PackageType, ChapterPackageManifest.PackageTypeValue, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"包类型不对：{manifest.PackageType}（期望 {ChapterPackageManifest.PackageTypeValue}）。");
        }

        if (manifest.SchemaVersion > CurrentPackageSchemaVersion)
        {
            throw new InvalidDataException(
                $"包格式版本 {manifest.SchemaVersion} 比当前工具箱支持的（{CurrentPackageSchemaVersion}）新，请先升级工具箱。");
        }

        var document = ReadJsonEntry<StoryDocumentFile>(archive, DocumentFileName)
            ?? throw new InvalidDataException("包里的 story.document.json 读不出来（缺件或损坏）。");
        // 备注允许缺件：老包 / 这一章本来就没有备注。
        var todoFile = ReadJsonEntry<StoryTodoFile>(archive, TodoFileName);
        return new ChapterPackageContent(
            Path.GetFullPath(packagePath),
            manifest,
            document,
            todoFile?.Notes?.Where(note => note is not null).ToList() ?? []);
    }

    // ────────────────────────────── 差异计算 ──────────────────────────────

    /// <summary>
    /// 逐句比对"本机这一章"和"包里的这一章"。
    ///
    /// 【对齐键】行的 StableId。两边从同一份文档分出去之后，同一个 id 就是同一句。
    ///
    /// 【为什么分三类、而且默认都不动本机】
    /// 二方对比分不出"谁改的"：包里有一句、本机没有 —— 可能是他新增的，也可能是本机删掉了。
    /// 所以默认一律**保持本机现状**，只把差异列出来让用户点名。这样最坏是"少收了几条"，
    /// 而不是"悄悄删了 / 覆盖了用户手上的东西"。
    ///
    /// 【小节以本机为准】对方重新划过小节边界时本机小节不变；
    /// 采用对方的新句会归入它插入位置所属的那一节。
    /// </summary>
    public ChapterMergePlan BuildMergePlan(
        ChapterInfo chapter,
        ChapterPackageContent package,
        StoryDocument localDocument,
        IReadOnlyList<StoryTodoNote> localTodos)
    {
        var localRows = SnapshotLocalRows(localDocument);
        var packageRows = SnapshotPackageRows(package.Document);
        var plan = new ChapterMergePlan
        {
            Package = package,
            RowItems = BuildRowMergeItems(localRows, packageRows),
            TodoItems = BuildTodoMergeItems(chapter, localTodos, package.Todos),
            LocalRowCount = localRows.Count,
            LocalSectionCount = localRows.Count == 0 ? 0 : localRows[^1].Section
        };

        if (!string.Equals(chapter.Code, package.Manifest.ChapterCode, StringComparison.OrdinalIgnoreCase))
        {
            plan.Warnings.Add(
                $"包里的章节代号是 {package.Manifest.ChapterCode}，本机这一章是 {chapter.Code} —— 先确认没选错章。");
        }

        if (package.Manifest.SchemaVersion > CurrentPackageSchemaVersion)
        {
            plan.Warnings.Add("包格式版本比当前支持的还新，读出来的内容可能不全。");
        }

        var missingChoiceFunctions = CollectMissingChoiceFunctions(localDocument, package);
        if (missingChoiceFunctions.Count > 0)
        {
            plan.Warnings.Add(
                $"包里有 {missingChoiceFunctions.Count} 个选项函数本机没有"
                + $"（{string.Join("、", missingChoiceFunctions.Take(3))}{(missingChoiceFunctions.Count > 3 ? " 等" : string.Empty)}）"
                + " —— 采用带这些函数的句子后，选项内容与跳转不会自动补上，需要到「选项与分支」里重新确认。");
        }

        return plan;
    }

    /// <summary>
    /// 按计划合并：**先备份**，再改剧情行与备注，最后落盘。
    ///
    /// 【为什么备份放在服务里】合并不可逆（用户多半已经把包删了），而"合并前先备份"
    /// 这一步太容易被调用方忘掉。走的就是「备份 / 还原」那一套：落在章节的
    /// <c>Tools/ChapterBackups</c>，留 3 份，合并错了随时能在「还原」里退回来。
    ///
    /// 【顺序】先按计划动行的增删改（同时维护插入锚点）→ 重排行名与小节 → 再写备注
    ///（备注的行号要按**合并后**的文档重算）。
    /// </summary>
    public ChapterMergeApplyResult ApplyMerge(
        ChapterInfo chapter,
        ChapterMergePlan plan,
        StoryDocument localDocument,
        string? projectPath,
        IProgress<FolderBackupProgress>? backupProgress = null,
        CancellationToken cancellationToken = default)
    {
        var backup = _folderBackupService.CreateBackup(
            chapter.Path,
            FolderBackupService.ChapterBackupsFolderName,
            chapter.Code,
            "合并章节包前",
            backupProgress,
            cancellationToken);

        var packageRowsById = SnapshotPackageRows(plan.Package.Document)
            .Where(row => !string.IsNullOrWhiteSpace(row.StableId))
            .GroupBy(row => row.StableId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var rowByStableId = new Dictionary<string, StoryRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in localDocument.Rows)
        {
            rowByStableId.TryAdd(row.StableId, row);
        }

        var sectionByStableId = SnapshotSectionByStableId(localDocument);
        var insertedStableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updatedRows = 0;
        var insertedRows = 0;
        var deletedRows = 0;

        // 计划里的顺序 = 合并后的阅读顺序（本机行序当骨架，"只有对方有"的句排在它的锚点后面），
        // 所以顺着走一遍就能同时知道"锚点是谁"和"先后关系"。
        StoryRow? anchor = null;
        foreach (var item in plan.RowItems)
        {
            switch (item.Kind)
            {
                case ChapterMergeItemKind.Modified:
                    if (!rowByStableId.TryGetValue(item.StableId, out var modifiedRow))
                    {
                        break;
                    }

                    if (item.Choice == ChapterMergeChoice.UsePackage &&
                        packageRowsById.TryGetValue(item.StableId, out var packageRow))
                    {
                        ApplyPackageCells(modifiedRow, packageRow);
                        updatedRows++;
                    }

                    anchor = modifiedRow;
                    break;

                case ChapterMergeItemKind.OnlyLocal:
                    if (!rowByStableId.TryGetValue(item.StableId, out var localOnlyRow))
                    {
                        break;
                    }

                    if (item.Choice == ChapterMergeChoice.UsePackage)
                    {
                        // "按对方删除"：这一句从文档里去掉（行号由后面的重排补上）
                        localDocument.Rows.Remove(localOnlyRow);
                        rowByStableId.Remove(item.StableId);
                        deletedRows++;
                    }
                    else
                    {
                        anchor = localOnlyRow;
                    }
                    break;

                case ChapterMergeItemKind.OnlyInPackage:
                    if (item.Choice != ChapterMergeChoice.UsePackage ||
                        !packageRowsById.TryGetValue(item.StableId, out var newRowSnapshot))
                    {
                        break;
                    }

                    var newRow = CreateRowFromPackage(newRowSnapshot);
                    var insertIndex = anchor is null ? 0 : localDocument.Rows.IndexOf(anchor) + 1;
                    localDocument.Rows.Insert(insertIndex, newRow);
                    rowByStableId[newRow.StableId] = newRow;
                    insertedStableIds.Add(newRow.StableId);
                    insertedRows++;
                    // 刚插进去的这一行成为后面那些插入行的新锚点 ——
                    // 否则对方连续新增的两句会**倒序**插进来。
                    anchor = newRow;
                    break;
            }
        }

        RebuildRowNamesAndSections(localDocument, sectionByStableId, insertedStableIds);
        _repository.Save(chapter, localDocument);

        var updatedTodos = MergeTodos(chapter, plan, localDocument, projectPath);
        return new ChapterMergeApplyResult(updatedRows, insertedRows, deletedRows, updatedTodos, backup.Path);
    }

    // ────────────────────────────── 差异：剧情行 ──────────────────────────────

    private static List<ChapterMergeItem> BuildRowMergeItems(
        List<RowSnapshot> localRows,
        List<RowSnapshot> packageRows)
    {
        var localById = BuildRowIndex(localRows);
        var packageById = BuildRowIndex(packageRows);
        var items = new List<ChapterMergeItem>();

        // ① 本机行序当骨架：相同的不列，不同的列 Modified，本机独有的列 OnlyLocal。
        foreach (var local in localRows)
        {
            if (string.IsNullOrWhiteSpace(local.StableId))
            {
                continue;
            }

            if (!packageById.TryGetValue(local.StableId, out var packageRow))
            {
                items.Add(new ChapterMergeItem
                {
                    Kind = ChapterMergeItemKind.OnlyLocal,
                    StableId = local.StableId,
                    LocalRowNumber = local.RowNumber,
                    LocalSection = local.Section,
                    LocalSummary = BuildRowSummary(local)
                });
                continue;
            }

            var changedFields = CollectChangedFields(local, packageRow);
            if (changedFields.Count == 0)
            {
                continue;
            }

            items.Add(new ChapterMergeItem
            {
                Kind = ChapterMergeItemKind.Modified,
                StableId = local.StableId,
                LocalRowNumber = local.RowNumber,
                PackageRowNumber = packageRow.RowNumber,
                LocalSection = local.Section,
                PackageSection = packageRow.Section,
                LocalSummary = BuildRowSummary(local),
                PackageSummary = BuildRowSummary(packageRow),
                ChangedFields = changedFields
            });
        }

        // ② 只有包里有的行：按包里的顺序，插到"它前面最近的那条双方都有的行"后面 ——
        //    那条行就是合并时的插入锚点，列表顺序因此等于合并后的阅读顺序。
        var itemByStableId = new Dictionary<string, ChapterMergeItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.Kind == ChapterMergeItemKind.Modified)
            {
                itemByStableId[item.StableId] = item;
            }
        }

        var anchorIndex = -1;
        foreach (var packageRow in packageRows)
        {
            if (string.IsNullOrWhiteSpace(packageRow.StableId))
            {
                continue;
            }

            if (itemByStableId.TryGetValue(packageRow.StableId, out var sharedItem))
            {
                anchorIndex = items.IndexOf(sharedItem);
                continue;
            }

            if (localById.ContainsKey(packageRow.StableId))
            {
                // 双方都有但内容一样：不列进差异，但它仍然是个合法锚点。
                continue;
            }

            items.Insert(anchorIndex + 1, new ChapterMergeItem
            {
                Kind = ChapterMergeItemKind.OnlyInPackage,
                StableId = packageRow.StableId,
                PackageRowNumber = packageRow.RowNumber,
                PackageSection = packageRow.Section,
                PackageSummary = BuildRowSummary(packageRow)
            });
            anchorIndex++;
        }

        return items;
    }

    // ────────────────────────────── 差异：TODO 备注 ──────────────────────────────

    private static List<ChapterTodoMergeItem> BuildTodoMergeItems(
        ChapterInfo chapter,
        IReadOnlyList<StoryTodoNote> localTodos,
        IReadOnlyList<StoryTodoNote> packageTodos)
    {
        var localNotes = localTodos
            .Where(note => IsChapterNote(note, chapter.Code))
            .OrderBy(note => note.RowNumber)
            .ToList();
        var packageNotes = packageTodos
            .Where(note => string.IsNullOrWhiteSpace(note.ChapterCode) || IsChapterNote(note, chapter.Code))
            .OrderBy(note => note.RowNumber)
            .ToList();

        var localByKey = new Dictionary<string, StoryTodoNote>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in localNotes)
        {
            localByKey.TryAdd(BuildTodoMergeKey(note), note);
        }

        var packageByKey = new Dictionary<string, StoryTodoNote>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in packageNotes)
        {
            packageByKey.TryAdd(BuildTodoMergeKey(note), note);
        }

        var items = new List<ChapterTodoMergeItem>();
        foreach (var local in localNotes)
        {
            var key = BuildTodoMergeKey(local);
            if (!packageByKey.TryGetValue(key, out var packageNote))
            {
                items.Add(new ChapterTodoMergeItem
                {
                    Kind = ChapterMergeItemKind.OnlyLocal,
                    MergeKey = key,
                    StableId = local.StableId,
                    LocalRowNumber = local.RowNumber,
                    LocalText = local.Text,
                    LocalDone = local.Done
                });
                continue;
            }

            if (string.Equals(local.Text, packageNote.Text, StringComparison.Ordinal) &&
                local.Done == packageNote.Done)
            {
                continue;
            }

            items.Add(new ChapterTodoMergeItem
            {
                Kind = ChapterMergeItemKind.Modified,
                MergeKey = key,
                StableId = string.IsNullOrWhiteSpace(local.StableId) ? packageNote.StableId : local.StableId,
                LocalRowNumber = local.RowNumber,
                PackageRowNumber = packageNote.RowNumber,
                LocalText = local.Text,
                PackageText = packageNote.Text,
                LocalDone = local.Done,
                PackageDone = packageNote.Done
            });
        }

        foreach (var packageNote in packageNotes)
        {
            var key = BuildTodoMergeKey(packageNote);
            if (localByKey.ContainsKey(key))
            {
                continue;
            }

            items.Add(new ChapterTodoMergeItem
            {
                Kind = ChapterMergeItemKind.OnlyInPackage,
                MergeKey = key,
                StableId = packageNote.StableId,
                PackageRowNumber = packageNote.RowNumber,
                PackageText = packageNote.Text,
                PackageDone = packageNote.Done
            });
        }

        return items;
    }

    /// <summary>备注的对齐键：有 StableId 就用它；老备注（还没迁移出 StableId）退化成 <c>#行号</c>。</summary>
    private static string BuildTodoMergeKey(StoryTodoNote note)
    {
        return string.IsNullOrWhiteSpace(note.StableId) ? $"#row{note.RowNumber}" : note.StableId.Trim();
    }

    private static bool IsChapterNote(StoryTodoNote note, string chapterCode)
    {
        return string.Equals(note.ChapterCode, chapterCode, StringComparison.OrdinalIgnoreCase);
    }

    // ────────────────────────────── 合并写回的小工具 ──────────────────────────────

    /// <summary>
    /// 合并完把行名（= 行号）和小节归属重排一遍。
    ///
    /// 小节规则：**以本机为准** —— 原来的行带着自己那一节；合并插入的新行跟随**前面**那一行的小节
    ///（插在最前面、前面还没有已知小节的，跟随后面第一个已知小节；整章空着就当第 1 小节）。
    /// </summary>
    private static void RebuildRowNamesAndSections(
        StoryDocument document,
        IReadOnlyDictionary<string, int> sectionByStableId,
        IReadOnlySet<string> insertedStableIds)
    {
        var sections = new int[document.Rows.Count];
        for (var index = 0; index < document.Rows.Count; index++)
        {
            var row = document.Rows[index];
            if (!insertedStableIds.Contains(row.StableId) &&
                sectionByStableId.TryGetValue(row.StableId, out var section))
            {
                sections[index] = Math.Max(1, section);
            }
        }

        // 插入行跟随前面最近的一个已知小节。
        var previous = 0;
        for (var index = 0; index < document.Rows.Count; index++)
        {
            if (sections[index] > 0)
            {
                previous = sections[index];
            }
            else if (previous > 0)
            {
                sections[index] = previous;
            }
        }

        // 头部的插入行没有前驱：跟随后面第一个已知小节，都没有就当第 1 小节。
        var headSection = 0;
        foreach (var section in sections)
        {
            if (section > 0)
            {
                headSection = section;
                break;
            }
        }

        if (headSection == 0)
        {
            headSection = 1;
        }

        for (var index = 0; index < sections.Length && sections[index] == 0; index++)
        {
            sections[index] = headSection;
        }

        document.RowSections.Clear();
        for (var index = 0; index < document.Rows.Count; index++)
        {
            var row = document.Rows[index];
            row.Set("Name", StoryCsvService.CreateRowName(index));
            document.RowSections[row.Get("Name")] = sections[index] > 0 ? sections[index] : 1;
        }
    }

    /// <summary>
    /// 合并这一章的 TODO 备注：读**全量** → 只改本章的条目 → 别的章节原样 → 写回。
    /// 备注的定位键是 StableId，行号只是显示缓存，所以这里按**合并后**的文档把它重算一遍。
    /// </summary>
    private static int MergeTodos(
        ChapterInfo chapter,
        ChapterMergePlan plan,
        StoryDocument mergedDocument,
        string? projectPath)
    {
        if (plan.TodoItems.Count == 0 || string.IsNullOrWhiteSpace(projectPath))
        {
            return 0;
        }

        var allNotes = StoryTodoStore.Load(projectPath);
        var others = allNotes.Where(note => !IsChapterNote(note, chapter.Code)).ToList();
        var chapterNotes = allNotes
            .Where(note => IsChapterNote(note, chapter.Code))
            .OrderBy(note => note.RowNumber)
            .ToList();
        var byKey = new Dictionary<string, StoryTodoNote>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in chapterNotes)
        {
            byKey.TryAdd(BuildTodoMergeKey(note), note);
        }

        var updated = 0;
        var removedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, StoryTodoNote>(StringComparer.OrdinalIgnoreCase);
        var addedNotes = new List<StoryTodoNote>();
        foreach (var item in plan.TodoItems)
        {
            byKey.TryGetValue(item.MergeKey, out var existing);
            switch (item.Kind)
            {
                case ChapterMergeItemKind.Modified:
                    if (item.Choice == ChapterMergeChoice.UsePackage && existing is not null)
                    {
                        // 备注是 record（init-only），不能就地改 —— 记下替换，最后重建列表。
                        replacements[item.MergeKey] = existing with
                        {
                            Text = item.PackageText,
                            Done = item.PackageDone,
                            UpdatedAt = DateTimeOffset.Now
                        };
                        updated++;
                    }
                    break;

                case ChapterMergeItemKind.OnlyLocal:
                    if (item.Choice == ChapterMergeChoice.UsePackage && existing is not null)
                    {
                        removedKeys.Add(item.MergeKey);
                        updated++;
                    }
                    break;

                case ChapterMergeItemKind.OnlyInPackage:
                    if (item.Choice == ChapterMergeChoice.UsePackage)
                    {
                        addedNotes.Add(new StoryTodoNote(
                            chapter.Code,
                            Math.Max(1, item.PackageRowNumber),
                            0,
                            item.PackageText,
                            item.PackageDone,
                            DateTimeOffset.Now)
                        {
                            StableId = item.StableId
                        });
                        updated++;
                    }
                    break;
            }
        }

        var rowNumberByStableId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < mergedDocument.Rows.Count; index++)
        {
            rowNumberByStableId.TryAdd(mergedDocument.Rows[index].StableId, index + 1);
        }

        var sectionByStableId = SnapshotSectionByStableId(mergedDocument);
        var result = new List<StoryTodoNote>(others);
        foreach (var note in chapterNotes.Concat(addedNotes))
        {
            var key = BuildTodoMergeKey(note);
            if (removedKeys.Contains(key))
            {
                continue;
            }

            var current = replacements.TryGetValue(key, out var replaced) ? replaced : note;
            var rowNumber = !string.IsNullOrWhiteSpace(current.StableId) &&
                            rowNumberByStableId.TryGetValue(current.StableId, out var number)
                ? number
                : current.RowNumber;
            var section = !string.IsNullOrWhiteSpace(current.StableId) &&
                          sectionByStableId.TryGetValue(current.StableId, out var mappedSection)
                ? mappedSection
                : current.Section;
            result.Add(current with { RowNumber = rowNumber, Section = section });
        }

        StoryTodoStore.Save(projectPath, result);
        return updated;
    }

    private static void ApplyPackageCells(StoryRow row, RowSnapshot snapshot)
    {
        foreach (var column in StorySchema.Columns)
        {
            if (column == "Name")
            {
                continue;
            }

            row.Set(column, NormalizeCellValue(column, snapshot.Get(column)));
        }
    }

    private static StoryRow CreateRowFromPackage(RowSnapshot snapshot)
    {
        var row = new StoryRow { StableId = snapshot.StableId };
        foreach (var column in StorySchema.Columns)
        {
            var value = column == "Name" ? "1" : NormalizeCellValue(column, snapshot.Get(column));
            row.Set(column, value);
        }

        return row;
    }

    /// <summary>数字列的空白统一成 "0"：和仓库里建行 / 读文档时的规矩一致。</summary>
    private static string NormalizeCellValue(string column, string value)
    {
        return value.Length == 0 && StorySchema.NumericColumns.Contains(column) ? "0" : value;
    }

    /// <summary>本机文档里"行的 StableId → 小节"，合并前先拍下来（插入行的小节要跟着邻居走）。</summary>
    private static Dictionary<string, int> SnapshotSectionByStableId(StoryDocument document)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousSection = 1;
        foreach (var row in document.Rows)
        {
            if (document.RowSections.TryGetValue(row.Get("Name"), out var section))
            {
                previousSection = Math.Max(1, section);
            }

            result[row.StableId] = previousSection;
        }

        return result;
    }

    private static List<string> CollectMissingChoiceFunctions(StoryDocument localDocument, ChapterPackageContent package)
    {
        var localKeys = localDocument.ChoiceNotes.Choices.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packageChoices = package.Document.ChoiceNotes?.Choices;
        if (packageChoices is null)
        {
            return [];
        }

        return packageChoices.Keys
            .Where(key => !localKeys.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, RowSnapshot> BuildRowIndex(IEnumerable<RowSnapshot> rows)
    {
        var result = new Dictionary<string, RowSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.StableId))
            {
                result.TryAdd(row.StableId, row);
            }
        }

        return result;
    }

    private static List<RowSnapshot> SnapshotLocalRows(StoryDocument document)
    {
        var columns = GetDiffColumns();
        var result = new List<RowSnapshot>(document.Rows.Count);
        var previousSection = 1;
        for (var index = 0; index < document.Rows.Count; index++)
        {
            var row = document.Rows[index];
            if (document.RowSections.TryGetValue(row.Get("Name"), out var section))
            {
                previousSection = Math.Max(1, section);
            }

            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                cells[column] = row.Get(column);
            }

            result.Add(new RowSnapshot(row.StableId, index + 1, previousSection, cells));
        }

        return result;
    }

    private static List<RowSnapshot> SnapshotPackageRows(StoryDocumentFile document)
    {
        var columns = GetDiffColumns();
        var result = new List<RowSnapshot>();
        foreach (var section in document.Sections ?? [])
        {
            foreach (var line in section.Lines ?? [])
            {
                var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var column in columns)
                {
                    cells[column] = line.Cells is not null &&
                                    line.Cells.TryGetValue(column, out var value) &&
                                    value is not null
                        ? value
                        : string.Empty;
                }

                result.Add(new RowSnapshot(
                    line.StableId ?? string.Empty,
                    result.Count + 1,
                    Math.Max(1, section.Number),
                    cells));
            }
        }

        return result;
    }

    private static List<string> GetDiffColumns()
    {
        return StorySchema.Columns.Where(column => column != "Name").ToList();
    }

    private static List<string> CollectChangedFields(RowSnapshot local, RowSnapshot package)
    {
        return GetDiffColumns()
            .Where(column => !string.Equals(local.Get(column), package.Get(column), StringComparison.Ordinal))
            .Select(GetFieldDisplayName)
            .ToList();
    }

    private static string BuildRowSummary(RowSnapshot row)
    {
        var speaker = row.Get("TalkChar").Trim();
        var text = TextUtility.NormalizeSingleLineText(row.Get("Tesxt"));
        var body = text.Length == 0 ? "（空文本）" : text;
        return speaker.Length == 0 ? body : $"{speaker}：{body}";
    }

    /// <summary>差异列表里那几个字段名的中文说法（够用就好，认不出来的直接显示列名）。</summary>
    private static string GetFieldDisplayName(string column)
    {
        switch (column)
        {
            case "Tesxt":
                return "文本";
            case "TalkChar":
                return "说话人";
            case "Custom":
                return "函数";
            case "BGindex":
                return "背景图";
            case "BGM":
                return "BGM";
            case "Scene":
                return "环境音";
            case "TalkBody":
                return "说话人服装";
            case "TalkFace":
                return "说话人表情";
            case "TalkAdorn":
                return "说话人装饰";
            case "TalkVfx":
                return "说话人滤镜";
        }

        if (column.StartsWith("Chara", StringComparison.Ordinal) &&
            int.TryParse(column["Chara".Length..], out var characterSlot))
        {
            return $"{characterSlot}号位角色";
        }

        foreach (var (prefix, label) in new[] { ("Body", "服装"), ("Face", "表情"), ("Adorn", "装饰"), ("Vfx", "滤镜") })
        {
            if (column.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(column[prefix.Length..], out var layerSlot))
            {
                return $"{layerSlot}号位{label}";
            }
        }

        return column;
    }

    private static T? ReadJsonEntry<T>(ZipArchive archive, string entryName)
        where T : class
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        return string.IsNullOrWhiteSpace(text)
            ? null
            : JsonSerializer.Deserialize<T>(text, PackageReadOptions);
    }

    /// <summary>包里/本机的一行，统一成同一副样子才好逐字段比。</summary>
    private sealed record RowSnapshot(
        string StableId,
        int RowNumber,
        int Section,
        IReadOnlyDictionary<string, string> Cells)
    {
        public string Get(string column)
        {
            return Cells.TryGetValue(column, out var value) ? value : string.Empty;
        }
    }

    /// <summary>
    /// 从项目级的备注文件里切出**这一章**的条目。
    /// 文件不在 / 读不动一律当"没有备注"（<see cref="StoryTodoStore"/> 自己的规矩），
    /// 不让一条备注把整次导出拦住。
    /// </summary>
    private static List<StoryTodoNote> LoadChapterTodos(string? projectPath, string chapterCode)
    {
        return StoryTodoStore
            .Load(projectPath)
            .Where(note => string.Equals(note.ChapterCode, chapterCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(note => note.RowNumber)
            .ToList();
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
