using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

internal sealed class StoryDocumentRepository
{
    public const string DocumentFileName = "story.document.json";
    private const int CurrentSchemaVersion = 3;
    private readonly JsonSerializerOptions _jsonOptions;

    public StoryDocumentRepository(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = new JsonSerializerOptions(jsonOptions)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public string GetDocumentPath(ChapterInfo chapter)
    {
        return Path.Combine(chapter.Path, DocumentFileName);
    }

    public bool Exists(ChapterInfo chapter)
    {
        return File.Exists(GetDocumentPath(chapter));
    }

    public StoryDocument Load(ChapterInfo chapter)
    {
        var path = GetDocumentPath(chapter);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("章节还没有新版 StoryDocument。", path);
        }

        var state = JsonSerializer.Deserialize<StoryDocumentFile>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException($"StoryDocument 无法解析：{path}");
        Validate(state, chapter, path);
        return CreateDocument(state);
    }

    public void UpdateChapterCode(ChapterInfo chapter)
    {
        var path = GetDocumentPath(chapter);
        var state = JsonSerializer.Deserialize<StoryDocumentFile>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException($"StoryDocument 无法解析：{path}");
        state.ChapterCode = chapter.Code;
        state.SavedAt = DateTimeOffset.Now;
        SaveFile(path, state);
    }

    public StoryDocumentFile CreateFile(ChapterInfo chapter, StoryDocument document)
    {
        var state = new StoryDocumentFile
        {
            SchemaVersion = CurrentSchemaVersion,
            ChapterCode = chapter.Code,
            SavedAt = DateTimeOffset.Now,
            ChoiceNotes = document.ChoiceNotes.Clone(),
            BranchRoutes = document.BranchRoutes.Select(route => route with { }).ToList(),
            CustomBranchSectionFirstRowIds = document.CustomBranchSectionFirstRowIds.ToList()
        };
        StoryDocumentSectionFile? currentSection = null;
        var previousSection = -1;
        foreach (var row in document.Rows)
        {
            var section = document.RowSections.TryGetValue(row.Get("Name"), out var mappedSection)
                ? Math.Max(1, mappedSection)
                : 1;
            if (currentSection is null || section != previousSection)
            {
                currentSection = new StoryDocumentSectionFile { Number = state.Sections.Count + 1 };
                state.Sections.Add(currentSection);
                previousSection = section;
            }

            var line = new StoryDocumentLineFile { StableId = row.StableId };
            foreach (var column in StorySchema.Columns.Where(column => column != "Name"))
            {
                line.Cells[column] = row.Get(column);
            }

            currentSection.Lines.Add(line);
        }

        if (state.Sections.Count == 0)
        {
            state.Sections.Add(new StoryDocumentSectionFile { Number = 1 });
        }

        return state;
    }

    public void Save(ChapterInfo chapter, StoryDocument document)
    {
        SaveFile(GetDocumentPath(chapter), CreateFile(chapter, document));
    }

    public Task SaveAsync(ChapterInfo chapter, StoryDocument document, CancellationToken cancellationToken = default)
    {
        return SaveFileAsync(GetDocumentPath(chapter), CreateFile(chapter, document), cancellationToken);
    }

    public Task SaveFileAsync(
        ChapterInfo chapter,
        StoryDocumentFile state,
        CancellationToken cancellationToken = default)
    {
        return SaveFileAsync(GetDocumentPath(chapter), state, cancellationToken);
    }

    private StoryDocument CreateDocument(StoryDocumentFile state)
    {
        var document = new StoryDocument();
        document.ChoiceNotes = state.ChoiceNotes?.Clone() ?? new StoryChoiceNoteState();
        document.BranchRoutes.AddRange((state.BranchRoutes ?? []).Select(route => route with { }));
        document.CustomBranchSectionFirstRowIds.UnionWith(state.CustomBranchSectionFirstRowIds ?? []);
        var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in state.Sections)
        {
            foreach (var line in section.Lines)
            {
                var row = new StoryRow
                {
                    StableId = Guid.TryParseExact(line.StableId, "N", out _) && assignedIds.Add(line.StableId)
                        ? line.StableId
                        : StoryRow.CreateStableId()
                };
                foreach (var column in StorySchema.Columns)
                {
                    row.Set(column, StorySchema.NumericColumns.Contains(column) ? "0" : string.Empty);
                }

                foreach (var pair in line.Cells)
                {
                    if (StorySchema.Columns.Contains(pair.Key, StringComparer.Ordinal))
                    {
                        row.Set(pair.Key, pair.Value ?? string.Empty);
                    }
                }

                row.Set("Name", (document.Rows.Count + 1).ToString());
                document.Rows.Add(row);
                document.RowSections[row.Get("Name")] = Math.Max(1, section.Number);
            }
        }

        if (document.Rows.Count == 0)
        {
            return StoryDocument.CreateEmpty();
        }

        return document;
    }

    private void SaveFile(string path, StoryDocumentFile state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, _jsonOptions));
            File.Move(tempPath, path, true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private async Task SaveFileAsync(string path, StoryDocumentFile state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, path, true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void Validate(StoryDocumentFile state, ChapterInfo chapter, string path)
    {
        if (state.SchemaVersion is < 1 or > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"StoryDocument 版本不受支持：{state.SchemaVersion}，文件：{path}");
        }

        if (!string.Equals(state.ChapterCode, chapter.Code, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"StoryDocument 章节代码不匹配：{state.ChapterCode} != {chapter.Code}");
        }
    }
}
