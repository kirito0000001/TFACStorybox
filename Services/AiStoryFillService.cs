using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GalExcleTools.Services;

internal sealed class AiStoryFillService
{
    public const string ManifestFileName = "manifest.json";
    public const string SpeakerCatalogFileName = "speaker-catalog.json";
    public const string SourceFileName = "source.jsonl";
    public const string ResultFileName = "result.jsonl";
    public const string ImportStateFileName = "ai-story-import-state.json";
    private readonly StoryDocumentRepository _repository;
    private readonly JsonSerializerOptions _protocolJsonOptions;

    public AiStoryFillService(
        StoryDocumentRepository repository,
        JsonSerializerOptions jsonOptions)
    {
        _repository = repository;
        _protocolJsonOptions = new JsonSerializerOptions(jsonOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public AiStoryFillTaskExportResult CreateTaskPackage(
        ChapterInfo chapter,
        StoryDocument document,
        IReadOnlyList<CharacterInfo> characters,
        string packageDirectory,
        int sourceStartParagraph,
        IReadOnlyList<string> customSpeakers)
    {
        if (document.Rows.Count == 0)
        {
            throw new InvalidDataException("章节没有可作为追加锚点的剧情行。");
        }

        var documentPath = _repository.GetDocumentPath(chapter);
        if (!File.Exists(documentPath))
        {
            throw new FileNotFoundException("章节 StoryDocument 不存在。", documentPath);
        }

        Directory.CreateDirectory(packageDirectory);
        var anchor = document.Rows[^1];
        var manifest = new AiStoryFillManifest
        {
            PackageId = Guid.NewGuid().ToString("N"),
            GeneratedAt = DateTimeOffset.Now,
            ChapterCode = chapter.Code,
            ChapterName = chapter.Name,
            AnchorRowId = anchor.StableId,
            AnchorRowIndex = document.Rows.Count - 1,
            StoryDocumentSha256 = ComputeFileSha256(documentPath),
            SourceStartParagraph = Math.Max(0, sourceStartParagraph)
        };
        var catalog = new AiStorySpeakerCatalog
        {
            Characters = characters
                .Where(character => !string.IsNullOrWhiteSpace(character.Code))
                .GroupBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .Select(character => new AiStorySpeakerCharacter(character.Name, character.Code))
                .ToList(),
            CustomSpeakers = customSpeakers
                .Select(speaker => speaker.Trim())
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(speaker => speaker, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
        var catalogPath = Path.Combine(packageDirectory, SpeakerCatalogFileName);
        WriteJsonAtomic(manifestPath, manifest);
        WriteJsonAtomic(catalogPath, catalog);
        return new AiStoryFillTaskExportResult(packageDirectory, manifestPath, catalogPath, manifest);
    }

    public AiStoryFillImportPreview LoadImportPreview(
        ChapterInfo chapter,
        string packageDirectory,
        IReadOnlySet<string> knownSpeakers)
    {
        var issues = new List<AiStoryFillIssue>();
        var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
        var sourcePath = Path.Combine(packageDirectory, SourceFileName);
        var resultPath = Path.Combine(packageDirectory, ResultFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("AI 填表包缺少 manifest.json。", manifestPath);
        }
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("AI 填表包缺少 source.jsonl。", sourcePath);
        }
        if (!File.Exists(resultPath))
        {
            throw new FileNotFoundException("AI 填表包缺少 result.jsonl。", resultPath);
        }

        var manifest = JsonSerializer.Deserialize<AiStoryFillManifest>(
                           File.ReadAllText(manifestPath, Encoding.UTF8),
                           _protocolJsonOptions)
                       ?? throw new InvalidDataException("manifest.json 无法解析。");
        ValidateManifest(chapter, manifest, issues);
        var sourceIds = ReadSourceIds(sourcePath, issues);
        var importedIds = ReadImportState(chapter).Packages
            .SelectMany(package => package.SourceIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = ReadResultItems(resultPath, sourceIds, importedIds, knownSpeakers, issues);
        if (items.Any(item => item.Status == AiStoryFillItemStatus.Include))
        {
            ValidateCurrentChapterState(chapter, manifest, issues);
        }

        return new AiStoryFillImportPreview
        {
            PackageDirectory = packageDirectory,
            Manifest = manifest,
            Items = items,
            Issues = issues
        };
    }

    private void ValidateManifest(
        ChapterInfo chapter,
        AiStoryFillManifest manifest,
        List<AiStoryFillIssue> issues)
    {
        if (manifest.SchemaVersion != 1 || !string.Equals(manifest.PackageType, "TFAC.AIStoryFill", StringComparison.Ordinal))
        {
            issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "任务包协议或版本不受支持。"));
        }
        if (!string.Equals(manifest.ChapterCode, chapter.Code, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, $"章节不匹配：{manifest.ChapterCode} != {chapter.Code}"));
        }
        if (!string.Equals(manifest.Mode, "append", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "第一版只支持 append 合并模式。"));
        }
        if (!manifest.AllowedFields.ToHashSet(StringComparer.Ordinal).SetEquals(["TalkChar", "Tesxt"]))
        {
            issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "allowedFields 必须严格为 TalkChar/Tesxt。"));
        }
    }

    private void ValidateCurrentChapterState(
        ChapterInfo chapter,
        AiStoryFillManifest manifest,
        List<AiStoryFillIssue> issues)
    {
        var documentPath = _repository.GetDocumentPath(chapter);
        if (!string.Equals(ComputeFileSha256(documentPath), manifest.StoryDocumentSha256, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "导出任务后章节已发生变化，请重新导出 AI 填表任务。"));
        }
        var document = _repository.Load(chapter);
        if (document.Rows.All(row => !string.Equals(row.StableId, manifest.AnchorRowId, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "任务锚点已不存在，请重新导出任务。"));
        }
    }

    private static HashSet<string> ReadSourceIds(
        string sourcePath,
        List<AiStoryFillIssue> issues)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(sourcePath, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                using var json = JsonDocument.Parse(line);
                var id = GetStringProperty(json.RootElement, "id");
                if (string.IsNullOrWhiteSpace(id) || !result.Add(id))
                {
                    issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "source.jsonl 的 id 为空或重复。", lineNumber));
                }
            }
            catch (JsonException ex)
            {
                issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, $"source.jsonl 无法解析：{ex.Message}", lineNumber));
            }
        }
        return result;
    }

    private static List<AiStoryFillPreviewItem> ReadResultItems(
        string resultPath,
        IReadOnlySet<string> sourceIds,
        IReadOnlySet<string> importedIds,
        IReadOnlySet<string> knownSpeakers,
        List<AiStoryFillIssue> issues)
    {
        var result = new List<AiStoryFillPreviewItem>();
        var resultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedProperties = new HashSet<string>(
            ["id", "TalkChar", "Tesxt", "status", "confidence"],
            StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(resultPath, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("记录不是 JSON 对象。");
                }
                var extraProperties = root.EnumerateObject()
                    .Select(property => property.Name)
                    .Where(name => !allowedProperties.Contains(name))
                    .ToList();
                var id = GetStringProperty(root, "id") ?? string.Empty;
                var rawStatus = (GetStringProperty(root, "status") ?? string.Empty).Trim().ToLowerInvariant();
                var confidence = GetStringProperty(root, "confidence") ?? string.Empty;
                var talkChar = GetNullableStringProperty(root, "TalkChar");
                var text = GetNullableStringProperty(root, "Tesxt");
                var status = rawStatus switch
                {
                    "include" => AiStoryFillItemStatus.Include,
                    "skip" => AiStoryFillItemStatus.Skip,
                    "review" => AiStoryFillItemStatus.Review,
                    _ => AiStoryFillItemStatus.Invalid
                };
                if (extraProperties.Count > 0)
                {
                    status = AiStoryFillItemStatus.Invalid;
                    issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, $"结果包含协议外字段：{string.Join(", ", extraProperties)}", lineNumber));
                }
                if (string.IsNullOrWhiteSpace(id) || !resultIds.Add(id))
                {
                    status = AiStoryFillItemStatus.Invalid;
                    issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "result.jsonl 的 id 为空或重复。", lineNumber));
                }
                else if (!sourceIds.Contains(id))
                {
                    status = AiStoryFillItemStatus.Invalid;
                    issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, $"结果 id 不存在于 source.jsonl：{id}", lineNumber));
                }
                else if (importedIds.Contains(id))
                {
                    status = AiStoryFillItemStatus.Duplicate;
                }
                if (status == AiStoryFillItemStatus.Include && string.IsNullOrWhiteSpace(text))
                {
                    status = AiStoryFillItemStatus.Invalid;
                    issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, "include 记录的 Tesxt 不能为空。", lineNumber));
                }
                if (status == AiStoryFillItemStatus.Review)
                {
                    issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, $"存在待人工确认记录：{id}", lineNumber));
                }
                if (status == AiStoryFillItemStatus.Include && talkChar is not null && !knownSpeakers.Contains(talkChar))
                {
                    issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Warning, $"未知说话人，将作为自定义名称保留：{talkChar}", lineNumber));
                }
                result.Add(new AiStoryFillPreviewItem(
                    id,
                    lineNumber,
                    talkChar,
                    text,
                    confidence,
                    status,
                    status.ToString()));
            }
            catch (JsonException ex)
            {
                issues.Add(new AiStoryFillIssue(AiStoryFillIssueSeverity.Error, $"result.jsonl 无法解析：{ex.Message}", lineNumber));
                result.Add(new AiStoryFillPreviewItem(
                    string.Empty,
                    lineNumber,
                    null,
                    null,
                    string.Empty,
                    AiStoryFillItemStatus.Invalid,
                    "Invalid"));
            }
        }
        return result;
    }

    private AiStoryFillImportState ReadImportState(ChapterInfo chapter)
    {
        var path = Path.Combine(chapter.Path, ImportStateFileName);
        if (!File.Exists(path))
        {
            return new AiStoryFillImportState();
        }
        return JsonSerializer.Deserialize<AiStoryFillImportState>(
                   File.ReadAllText(path, Encoding.UTF8),
                   _protocolJsonOptions)
               ?? new AiStoryFillImportState();
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        return TryGetPropertyIgnoreCase(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? GetNullableStringProperty(JsonElement element, string name)
    {
        return !TryGetPropertyIgnoreCase(element, name, out var value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    public static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private void WriteJsonAtomic<T>(string path, T value)
    {
        var tempPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(value, _protocolJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
