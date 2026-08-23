using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools.Services;

internal sealed class StoryCsvImporter
{
    private readonly StoryCsvService _csvService;
    private readonly StoryDocumentRepository _repository;

    public StoryCsvImporter(StoryCsvService csvService, StoryDocumentRepository repository)
    {
        _csvService = csvService;
        _repository = repository;
    }

    public List<string> GetLooseCsvPaths(ChapterInfo chapter)
    {
        if (!Directory.Exists(chapter.Path))
        {
            return [];
        }

        return Directory.EnumerateFiles(chapter.Path, "*.csv", SearchOption.TopDirectoryOnly)
            .Where(path => !IsGeneratedCsv(chapter, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public StorySectionImportResult Import(
        ChapterInfo chapter,
        IReadOnlyList<string> csvPaths,
        bool deleteSourceFiles)
    {
        var document = _repository.Load(chapter);
        var logs = new List<StorySessionLogEntry>();
        var nextSection = document.RowSections.Values.DefaultIfEmpty(0).Max() + 1;
        var importedCount = 0;
        foreach (var csvPath in csvPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(csvPath))
            {
                continue;
            }

            if (!_csvService.InspectCompatibility(csvPath).IsCompatible)
            {
                logs.Add(new StorySessionLogEntry(LogKind.Warning, $"跳过结构不兼容的CSV：{csvPath}"));
                continue;
            }

            var importedRows = _csvService.ReadRows(csvPath);
            if (importedRows.Count == 0)
            {
                logs.Add(new StorySessionLogEntry(LogKind.Warning, $"跳过空CSV：{csvPath}"));
                continue;
            }

            foreach (var row in importedRows)
            {
                row.StableId = StoryRow.CreateStableId();
                row.Set("Name", (document.Rows.Count + 1).ToString());
                document.Rows.Add(row);
                document.RowSections[row.Get("Name")] = nextSection;
            }

            logs.Add(new StorySessionLogEntry(LogKind.User, $"已导入为{StorySectionDisplay.FormatName(nextSection)}：{Path.GetFileName(csvPath)}"));
            nextSection++;
            importedCount++;
            if (deleteSourceFiles && !IsGeneratedCsv(chapter, csvPath))
            {
                File.Delete(csvPath);
            }
        }

        if (importedCount > 0)
        {
            _repository.Save(chapter, document);
        }

        return new StorySectionImportResult(importedCount, importedCount > 0, logs);
    }

    private bool IsGeneratedCsv(ChapterInfo chapter, string path)
    {
        return _csvService.GetLocalSectionCsvPaths(chapter)
            .Any(file => FileSystemUtility.PathsEqual(file.Path, path));
    }
}
