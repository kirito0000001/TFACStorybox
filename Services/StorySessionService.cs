using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static GalExcleTools.Services.FileSystemUtility;

namespace GalExcleTools.Services;

internal sealed class StorySessionService
{
    private readonly StoryCsvService _storyCsvService;
    private readonly StoryStateService _storyStateService;
    private readonly StoryEditorService _storyEditorService;

    public StorySessionService(
        StoryCsvService storyCsvService,
        StoryStateService storyStateService,
        StoryEditorService storyEditorService)
    {
        _storyCsvService = storyCsvService;
        _storyStateService = storyStateService;
        _storyEditorService = storyEditorService;
    }

    public StoryRowsLoadResult LoadRowsFromSectionFiles(ChapterInfo chapter)
    {
        var rows = new List<StoryRow>();
        var sections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var mainCsvPath = _storyCsvService.GetChapterCsvPath(chapter);
        Directory.CreateDirectory(chapter.Path);
        var sectionFiles = _storyCsvService.GetLocalSectionCsvPaths(chapter)
            .OrderBy(item => item.Section)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!sectionFiles.Any(item => item.Section == 1))
        {
            sectionFiles.Insert(0, new StorySectionCsvFile(mainCsvPath, 1));
        }

        var legacySectionMap = _storyStateService.ReadSectionMap(chapter);
        if (!sectionFiles.Any(item => item.Section > 1) &&
            legacySectionMap.Values.Any(section => section > 1) &&
            File.Exists(mainCsvPath))
        {
            var previousSection = 1;
            foreach (var row in _storyCsvService.ReadRows(mainCsvPath))
            {
                var originalName = row.Get("Name");
                if (legacySectionMap.TryGetValue(originalName, out var section))
                {
                    previousSection = Math.Max(1, section);
                }

                AddRenamedRow(rows, sections, row, previousSection);
            }

            if (rows.Count > 0)
            {
                return new StoryRowsLoadResult(rows, sections, 0);
            }
        }

        var removedEmptyCount = 0;
        foreach (var sectionFile in sectionFiles)
        {
            var sectionRows = _storyCsvService.ReadRows(sectionFile.Path);
            var isMainSection = sectionFile.Section == 1 && PathsEqual(sectionFile.Path, mainCsvPath);
            if (sectionRows.Count == 0 || !sectionRows.Any(_storyCsvService.RowHasContent))
            {
                if (!isMainSection && File.Exists(sectionFile.Path))
                {
                    File.Delete(sectionFile.Path);
                    removedEmptyCount++;
                }

                continue;
            }

            foreach (var row in sectionRows)
            {
                AddRenamedRow(rows, sections, row, sectionFile.Section);
            }
        }

        if (rows.Count == 0)
        {
            var row = _storyCsvService.CreateDefaultRow();
            rows.Add(row);
            sections[row.Get("Name")] = 1;
            _storyCsvService.WriteRows(mainCsvPath, rows);
        }

        return new StoryRowsLoadResult(rows, sections, removedEmptyCount);
    }

    public List<string> GetLooseSectionCsvPaths(ChapterInfo chapter)
    {
        if (!Directory.Exists(chapter.Path))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(chapter.Path, "*.csv", SearchOption.TopDirectoryOnly)
            .Where(path => _storyCsvService.IsLooseSectionCsvCandidate(chapter, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public StorySectionImportResult ImportSectionCsvFiles(ChapterInfo chapter, IReadOnlyList<string> csvPaths, bool deleteSourceFiles)
    {
        var logs = new List<StorySessionLogEntry>();
        var mainCsvPath = _storyCsvService.GetChapterCsvPath(chapter);
        var changed = false;
        var importedCount = 0;
        var nextSection = _storyCsvService.GetLocalSectionCsvPaths(chapter)
            .Select(item => item.Section)
            .DefaultIfEmpty(1)
            .Max() + 1;

        foreach (var csvPath in csvPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (PathsEqual(csvPath, mainCsvPath) || !File.Exists(csvPath))
            {
                continue;
            }

            var compatibility = _storyCsvService.InspectCompatibility(csvPath);
            if (!compatibility.IsCompatible)
            {
                logs.Add(new StorySessionLogEntry(LogKind.Warning, $"跳过结构不兼容的小节 CSV：{csvPath}"));
                continue;
            }

            var sectionRows = _storyCsvService.ReadRows(csvPath);
            if (sectionRows.Count == 0 || !sectionRows.Any(_storyCsvService.RowHasContent))
            {
                if (deleteSourceFiles)
                {
                    File.Delete(csvPath);
                }

                changed = true;
                logs.Add(new StorySessionLogEntry(LogKind.Info, $"已删除空小节 CSV：{csvPath}"));
                continue;
            }

            var section = _storyCsvService.TryParseStorySectionFromFileName(chapter, csvPath) ?? nextSection++;
            var targetCsvPath = _storyCsvService.GetSectionCsvPath(chapter, section);
            _storyCsvService.WriteRows(targetCsvPath, StoryEditorService.CloneRows(sectionRows));

            if (deleteSourceFiles && !PathsEqual(csvPath, targetCsvPath))
            {
                File.Delete(csvPath);
            }

            changed = true;
            importedCount++;
            logs.Add(new StorySessionLogEntry(LogKind.User, $"已导入小节 CSV 为第 {section} 小节：{Path.GetFileName(csvPath)}"));
        }

        return new StorySectionImportResult(importedCount, changed, logs);
    }

    public StoryRowsPersistResult PersistRowsToSectionFiles(
        ChapterInfo chapter,
        string currentStoryCsvPath,
        List<StoryRow> rows,
        Dictionary<string, int> rowSections)
    {
        _storyEditorService.RenameRowsInOrder(rows);
        _storyEditorService.SynchronizeSections(rows, rowSections);

        var groupedRows = rows
            .Select((row, index) => new
            {
                Row = row,
                Section = rowSections.TryGetValue(row.Get("Name"), out var section) ? Math.Max(1, section) : 1,
                Index = index
            })
            .GroupBy(item => item.Section)
            .OrderBy(group => group.Key)
            .ToList();

        var activeCsvPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groupedRows)
        {
            var rowsToWrite = group.Select(item => item.Row.Clone()).ToList();
            var targetCsvPath = _storyCsvService.GetSectionCsvPath(chapter, group.Key);
            if (!rowsToWrite.Any(_storyCsvService.RowHasContent))
            {
                if (group.Key > 1 && File.Exists(targetCsvPath))
                {
                    File.Delete(targetCsvPath);
                }

                continue;
            }

            _storyCsvService.WriteRows(targetCsvPath, rowsToWrite);
            activeCsvPaths.Add(targetCsvPath);
        }

        if (activeCsvPaths.Count == 0)
        {
            var defaultRows = new List<StoryRow> { _storyCsvService.CreateDefaultRow() };
            _storyCsvService.WriteRows(currentStoryCsvPath, defaultRows);
            activeCsvPaths.Add(currentStoryCsvPath);
        }

        DeleteInactiveLocalSectionCsvFiles(chapter, activeCsvPaths);
        _storyStateService.WriteSectionState(chapter.Path, rowSections);
        return new StoryRowsPersistResult(activeCsvPaths.Count);
    }

    private void DeleteInactiveLocalSectionCsvFiles(ChapterInfo chapter, IReadOnlySet<string> activeCsvPaths)
    {
        foreach (var sectionFile in _storyCsvService.GetLocalSectionCsvPaths(chapter))
        {
            if (!activeCsvPaths.Contains(sectionFile.Path) && File.Exists(sectionFile.Path))
            {
                File.Delete(sectionFile.Path);
            }
        }
    }

    private static void AddRenamedRow(
        List<StoryRow> rows,
        Dictionary<string, int> sections,
        StoryRow row,
        int section)
    {
        var clone = row.Clone();
        clone.Set("Name", StoryCsvService.CreateRowName(rows.Count));
        rows.Add(clone);
        sections[clone.Get("Name")] = Math.Max(1, section);
    }
}
