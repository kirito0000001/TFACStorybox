using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools.Services;

internal sealed class StoryCsvExporter
{
    private readonly StoryCsvService _csvService;
    private readonly StoryDocumentRepository _repository;

    public StoryCsvExporter(StoryCsvService csvService, StoryDocumentRepository repository)
    {
        _csvService = csvService;
        _repository = repository;
    }

    public IReadOnlyList<string> ExportProject(IReadOnlyList<ChapterInfo> chapters)
    {
        var exported = new List<string>();
        foreach (var chapter in chapters)
        {
            exported.AddRange(ExportChapter(chapter));
        }

        return exported;
    }

    public IReadOnlyList<string> ExportChapter(ChapterInfo chapter)
    {
        RecoverInterruptedExport(chapter);
        var document = _repository.Load(chapter);
        var groups = document.Rows
            .Select(row => new
            {
                Row = row.Clone(),
                Section = document.RowSections.TryGetValue(row.Get("Name"), out var section) ? Math.Max(1, section) : 1
            })
            .GroupBy(item => item.Section)
            .OrderBy(group => group.Key)
            .ToList();
        var files = new Dictionary<string, List<StoryRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            files[_csvService.GetSectionCsvPath(chapter, group.Key)] = group.Select(item => item.Row).ToList();
        }

        if (files.Count == 0)
        {
            files[_csvService.GetChapterCsvPath(chapter)] = [CreateEmptyRow()];
        }

        ExportTransactionally(chapter, files);
        return files.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ExportTransactionally(ChapterInfo chapter, IReadOnlyDictionary<string, List<StoryRow>> files)
    {
        var transactionRoot = Path.Combine(chapter.Path, ".story-csv-export-transaction");
        var stagedDirectory = Path.Combine(transactionRoot, "staged");
        var backupDirectory = Path.Combine(transactionRoot, "backup");
        var markerPath = Path.Combine(transactionRoot, "commit.started");
        var targetListPath = Path.Combine(transactionRoot, "targets.txt");
        Directory.CreateDirectory(stagedDirectory);
        Directory.CreateDirectory(backupDirectory);
        var targetNames = new List<string>();
        foreach (var pair in files)
        {
            var targetName = Path.GetFileName(pair.Key);
            var stagedPath = Path.Combine(stagedDirectory, targetName);
            _csvService.WriteRows(stagedPath, pair.Value);
            if (!_csvService.InspectCompatibility(stagedPath).IsCompatible ||
                _csvService.ReadRows(stagedPath).Count != pair.Value.Count)
            {
                throw new InvalidDataException($"同步前CSV校验失败：{targetName}");
            }

            targetNames.Add(targetName);
        }

        File.WriteAllLines(targetListPath, targetNames);
        var oldCsvPaths = _csvService.GetLocalSectionCsvPaths(chapter)
            .Select(file => file.Path)
            .Where(File.Exists)
            .ToList();
        File.WriteAllText(markerPath, DateTimeOffset.Now.ToString("O"));
        try
        {
            foreach (var oldPath in oldCsvPaths)
            {
                File.Move(oldPath, Path.Combine(backupDirectory, Path.GetFileName(oldPath)), true);
            }

            foreach (var targetName in targetNames)
            {
                File.Move(Path.Combine(stagedDirectory, targetName), Path.Combine(chapter.Path, targetName), true);
            }

            Directory.Delete(transactionRoot, recursive: true);
        }
        catch
        {
            RestoreInterruptedExport(chapter, transactionRoot);
            throw;
        }
    }

    private void RecoverInterruptedExport(ChapterInfo chapter)
    {
        var transactionRoot = Path.Combine(chapter.Path, ".story-csv-export-transaction");
        if (!Directory.Exists(transactionRoot))
        {
            return;
        }

        if (File.Exists(Path.Combine(transactionRoot, "commit.started")))
        {
            RestoreInterruptedExport(chapter, transactionRoot);
        }
        else
        {
            Directory.Delete(transactionRoot, recursive: true);
        }
    }

    private static void RestoreInterruptedExport(ChapterInfo chapter, string transactionRoot)
    {
        var targetListPath = Path.Combine(transactionRoot, "targets.txt");
        if (File.Exists(targetListPath))
        {
            foreach (var targetName in File.ReadAllLines(targetListPath).Select(Path.GetFileName))
            {
                if (!string.IsNullOrWhiteSpace(targetName))
                {
                    File.Delete(Path.Combine(chapter.Path, targetName));
                }
            }
        }

        var backupDirectory = Path.Combine(transactionRoot, "backup");
        if (Directory.Exists(backupDirectory))
        {
            foreach (var backupPath in Directory.EnumerateFiles(backupDirectory))
            {
                File.Move(backupPath, Path.Combine(chapter.Path, Path.GetFileName(backupPath)), true);
            }
        }

        Directory.Delete(transactionRoot, recursive: true);
    }

    private static StoryRow CreateEmptyRow()
    {
        var row = new StoryRow();
        foreach (var column in StorySchema.Columns)
        {
            row.Set(column, StorySchema.NumericColumns.Contains(column) ? "0" : string.Empty);
        }

        row.Set("Name", "1");
        return row;
    }
}
