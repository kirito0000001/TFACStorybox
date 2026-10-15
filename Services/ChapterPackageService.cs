using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
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

    public ChapterPackageService(StoryDocumentRepository repository)
    {
        _repository = repository;
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
