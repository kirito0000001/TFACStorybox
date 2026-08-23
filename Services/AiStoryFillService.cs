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
