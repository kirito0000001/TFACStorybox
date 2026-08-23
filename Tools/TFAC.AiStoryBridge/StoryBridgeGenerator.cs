using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tfac.AiStoryBridge;

internal sealed partial class StoryBridgeGenerator
{
    private const string ManifestFileName = "manifest.json";
    private const string CatalogFileName = "speaker-catalog.json";
    private const string SourceFileName = "source.jsonl";
    private const string ResultFileName = "result.jsonl";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public GenerationSummary Generate(string packageDirectory, string docxPath, bool force)
    {
        var manifest = ReadJson<TaskManifest>(Path.Combine(packageDirectory, ManifestFileName));
        ValidateManifest(manifest);
        var catalog = ReadJson<SpeakerCatalog>(Path.Combine(packageDirectory, CatalogFileName));
        var paragraphs = DocxParagraphReader.Read(docxPath);
        return manifest.Mode.ToLowerInvariant() switch
        {
            "append" => GenerateAppend(packageDirectory, manifest, catalog, paragraphs, force),
            "patch-empty" => GeneratePatch(packageDirectory, manifest, catalog, paragraphs, force),
            _ => throw new InvalidDataException($"不支持的任务模式：{manifest.Mode}")
        };
    }

    private GenerationSummary GenerateAppend(
        string packageDirectory,
        TaskManifest manifest,
        SpeakerCatalog catalog,
        IReadOnlyList<DocxParagraph> paragraphs,
        bool force)
    {
        var chapterParagraphs = SelectChapterParagraphs(manifest, paragraphs);
        var resolver = new SpeakerResolver(catalog);
        var sources = new List<AppendSourceItem>();
        var results = new List<AppendResultItem>();
        foreach (var paragraph in chapterParagraphs.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            var parsed = Parse(paragraph);
            var id = $"docx-p{paragraph.Number}";
            sources.Add(new AppendSourceItem
            {
                Id = id,
                Order = paragraph.Number,
                Raw = paragraph.Text,
                SpeakerHint = parsed.Speaker,
                TextHint = parsed.Speaker is null ? null : parsed.Body
            });
            results.Add(CreateAppendResult(id, parsed, resolver));
        }

        var sourcePath = Path.Combine(packageDirectory, SourceFileName);
        var resultPath = Path.Combine(packageDirectory, ResultFileName);
        EnsureCanWrite(sourcePath, force);
        EnsureCanWrite(resultPath, force);
        WriteJsonLinesAtomic(sourcePath, sources, force);
        WriteJsonLinesAtomic(resultPath, results, force);
        return Summarize(results.Select(item => item.Status), sources.Count, resultPath);
    }

    private GenerationSummary GeneratePatch(
        string packageDirectory,
        TaskManifest manifest,
        SpeakerCatalog catalog,
        IReadOnlyList<DocxParagraph> paragraphs,
        bool force)
    {
        var sourcePath = Path.Combine(packageDirectory, SourceFileName);
        var sources = ReadJsonLines<PatchSourceItem>(sourcePath);
        var resolver = new SpeakerResolver(catalog);
        var parsedParagraphs = paragraphs
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text))
            .Select(Parse)
            .ToList();
        var results = sources.Select(source => CreatePatchResult(source, manifest, parsedParagraphs, resolver)).ToList();
        var resultPath = Path.Combine(packageDirectory, ResultFileName);
        EnsureCanWrite(resultPath, force);
        WriteJsonLinesAtomic(resultPath, results, force);
        return Summarize(results.Select(item => item.Status), sources.Count, resultPath);
    }

    private AppendResultItem CreateAppendResult(string id, ParsedParagraph parsed, SpeakerResolver resolver)
    {
        if (parsed.IsProductionNote)
        {
            return new AppendResultItem { Id = id, Status = "skip", Confidence = "high" };
        }
        if (parsed.Speaker is null)
        {
            return new AppendResultItem { Id = id, Status = "review", Confidence = "low" };
        }
        if (string.Equals(parsed.Speaker, "旁白", StringComparison.OrdinalIgnoreCase))
        {
            return new AppendResultItem { Id = id, TalkChar = string.Empty, Tesxt = parsed.Body, Status = "include", Confidence = "high" };
        }
        if (resolver.TryResolve(parsed.Speaker, out var value))
        {
            return new AppendResultItem { Id = id, TalkChar = value, Tesxt = parsed.Body, Status = "include", Confidence = "high" };
        }
        return new AppendResultItem { Id = id, Status = "review", Confidence = "low" };
    }

    private PatchResultItem CreatePatchResult(
        PatchSourceItem source,
        TaskManifest manifest,
        IReadOnlyList<ParsedParagraph> paragraphs,
        SpeakerResolver resolver)
    {
        var match = FindUniquePatchMatch(source, paragraphs);
        if (match is null)
        {
            return new PatchResultItem { Id = source.Id, Status = "review", Confidence = "low" };
        }

        var patch = new Dictionary<string, string>(StringComparer.Ordinal);
        if (manifest.PatchFields.Contains("TalkChar", StringComparer.Ordinal) && string.IsNullOrWhiteSpace(source.TalkChar))
        {
            if (string.Equals(match.Speaker, "旁白", StringComparison.OrdinalIgnoreCase))
            {
                return new PatchResultItem { Id = source.Id, Status = "skip", Confidence = "high", SourceParagraph = match.Paragraph.Number };
            }
            if (match.Speaker is null || !resolver.TryResolve(match.Speaker, out var speakerValue))
            {
                return new PatchResultItem { Id = source.Id, Status = "review", Confidence = "low", SourceParagraph = match.Paragraph.Number };
            }
            patch["TalkChar"] = speakerValue;
        }
        if (manifest.PatchFields.Contains("Tesxt", StringComparer.Ordinal) && string.IsNullOrWhiteSpace(source.Tesxt))
        {
            if (string.IsNullOrWhiteSpace(match.Body))
            {
                return new PatchResultItem { Id = source.Id, Status = "review", Confidence = "low", SourceParagraph = match.Paragraph.Number };
            }
            patch["Tesxt"] = match.Body;
        }
        return patch.Count == 0
            ? new PatchResultItem { Id = source.Id, Status = "skip", Confidence = "high", SourceParagraph = match.Paragraph.Number }
            : new PatchResultItem { Id = source.Id, Patch = patch, Status = "include", Confidence = "high", SourceParagraph = match.Paragraph.Number };
    }

    private static ParsedParagraph? FindUniquePatchMatch(
        PatchSourceItem source,
        IReadOnlyList<ParsedParagraph> paragraphs)
    {
        var normalized = NormalizeText(source.Tesxt);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }
        var matches = paragraphs.Where(paragraph => paragraph.NormalizedBody == normalized).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }
        if (matches.Count == 0)
        {
            return null;
        }

        var previous = NormalizeText(source.PreviousText);
        var next = NormalizeText(source.NextText);
        var scored = matches.Select(match =>
        {
            var index = IndexOfParagraph(paragraphs, match.Paragraph.Number);
            var score = 0;
            if (index > 0 && previous.Length > 0 && paragraphs[index - 1].NormalizedBody == previous) score++;
            if (index + 1 < paragraphs.Count && next.Length > 0 && paragraphs[index + 1].NormalizedBody == next) score++;
            return (Match: match, Score: score);
        }).OrderByDescending(item => item.Score).ToList();
        return scored.Count > 0 && scored[0].Score > 0 && (scored.Count == 1 || scored[0].Score > scored[1].Score)
            ? scored[0].Match
            : null;
    }

    private static int IndexOfParagraph(IReadOnlyList<ParsedParagraph> paragraphs, int paragraphNumber)
    {
        for (var index = 0; index < paragraphs.Count; index++)
        {
            if (paragraphs[index].Paragraph.Number == paragraphNumber) return index;
        }
        return -1;
    }

    private static List<DocxParagraph> SelectChapterParagraphs(TaskManifest manifest, IReadOnlyList<DocxParagraph> paragraphs)
    {
        var startIndex = manifest.SourceStartParagraph > 0
            ? paragraphs.ToList().FindIndex(item => item.Number >= manifest.SourceStartParagraph)
            : paragraphs.ToList().FindIndex(item => item.Text.StartsWith(manifest.ChapterName, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0)
        {
            throw new InvalidDataException($"无法在 Word 原稿中定位章节：{manifest.ChapterName}");
        }
        if (IsChapterHeading(paragraphs[startIndex].Text))
        {
            startIndex++;
        }
        var result = new List<DocxParagraph>();
        for (var index = startIndex; index < paragraphs.Count; index++)
        {
            if (IsChapterHeading(paragraphs[index].Text)) break;
            result.Add(paragraphs[index]);
        }
        return result;
    }

    private static ParsedParagraph Parse(DocxParagraph paragraph)
    {
        var text = paragraph.Text.Trim();
        var isProduction = IsProductionNote(text);
        var match = SpeakerPrefixRegex().Match(text);
        var speaker = match.Success ? match.Groups["speaker"].Value.Trim().TrimStart('【') : null;
        var body = match.Success ? match.Groups["body"].Value.Trim().TrimEnd('】') : text;
        return new ParsedParagraph(paragraph, speaker, body, NormalizeText(body), isProduction);
    }

    private static bool IsProductionNote(string text)
    {
        return text.StartsWith('【') || text.StartsWith("*****", StringComparison.Ordinal) || IsChapterHeading(text);
    }

    private static bool IsChapterHeading(string text)
    {
        return ChapterHeadingRegex().IsMatch(text.Trim());
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var withoutRoleSuffix = RoleSuffixRegex().Replace(value, string.Empty);
        var builder = new StringBuilder(withoutRoleSuffix.Length);
        foreach (var character in withoutRoleSuffix)
        {
            var category = char.GetUnicodeCategory(character);
            if (category is not (UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or
                UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or
                UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or
                UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol))
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    private T ReadJson<T>(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("任务包缺少必要文件。", path);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), _jsonOptions)
            ?? throw new InvalidDataException($"JSON 无法解析：{path}");
    }

    private List<T> ReadJsonLines<T>(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("任务包缺少 source.jsonl。", path);
        var result = new List<T>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.Add(JsonSerializer.Deserialize<T>(line, _jsonOptions)
                ?? throw new InvalidDataException($"JSONL 第 {lineNumber} 行无法解析：{path}"));
        }
        return result;
    }

    private void WriteJsonLinesAtomic<T>(string path, IReadOnlyList<T> items, bool force)
    {
        if (force && File.Exists(path) && new FileInfo(path).Length > 0)
        {
            File.Copy(path, $"{path}.before-{DateTime.Now:yyyyMMdd-HHmmssfff}.bak", overwrite: false);
        }
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(
                tempPath,
                items.Select(item => JsonSerializer.Serialize(item, _jsonOptions)),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void EnsureCanWrite(string path, bool force)
    {
        if (!force && File.Exists(path) && new FileInfo(path).Length > 0)
        {
            throw new IOException($"文件已有内容，未覆盖：{path}。需要覆盖时使用 --force。");
        }
    }

    private static GenerationSummary Summarize(IEnumerable<string> statuses, int sourceCount, string resultPath)
    {
        var values = statuses.ToList();
        return new GenerationSummary(
            sourceCount,
            values.Count(value => value == "include"),
            values.Count(value => value == "skip"),
            values.Count(value => value == "review"),
            resultPath);
    }

    private static void ValidateManifest(TaskManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.ProtocolVersion != 1 || manifest.PackageType != "TFAC.AIStoryFill")
        {
            throw new InvalidDataException("当前外接程序只支持 TFAC AI 剧情协议 V1。");
        }
    }

    [GeneratedRegex("^(?<speaker>[^：]{1,20})：(?<body>.*)$", RegexOptions.Singleline)]
    private static partial Regex SpeakerPrefixRegex();

    [GeneratedRegex("^第[0-9一二三四五六七八九十百零〇两]+章")]
    private static partial Regex ChapterHeadingRegex();

    [GeneratedRegex("[（(]角色[^）)]*[）)]\\s*$")]
    private static partial Regex RoleSuffixRegex();
}
