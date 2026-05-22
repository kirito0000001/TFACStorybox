using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;

namespace GalExcleTools.Services;

internal sealed class StoryCsvService
{
    public static readonly string[] Columns =
    [
        "Name",
        "Tesxt",
        "Custom",
        "BGindex",
        "BGM",
        "Scene",
        "TalkChar",
        "TalkBody",
        "TalkFace",
        "TalkAdorn",
        "TalkVfx",
        "Chara1",
        "Body1",
        "Face1",
        "Adorn1",
        "Vfx1",
        "Chara2",
        "Body2",
        "Face2",
        "Adorn2",
        "Vfx2",
        "Chara3",
        "Body3",
        "Face3",
        "Adorn3",
        "Vfx3",
        "Chara4",
        "Body4",
        "Face4",
        "Adorn4",
        "Vfx4",
        "Chara5",
        "Body5",
        "Face5",
        "Adorn5",
        "Vfx5"
    ];

    public static readonly HashSet<string> NumericColumns =
        new(Columns.Where(column =>
            column is "BGindex" or "BGM" or "Scene" or
                "TalkBody" or "TalkFace" or "TalkAdorn" or "TalkVfx" ||
            Regex.IsMatch(column, "^(Body|Face|Adorn|Vfx)\\d+$")), StringComparer.Ordinal);

    public StoryCsvCompatibility InspectCompatibility(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            return new StoryCsvCompatibility(false, Columns.ToList(), []);
        }

        var firstLine = File.ReadLines(csvPath, Encoding.UTF8).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return new StoryCsvCompatibility(false, Columns.ToList(), []);
        }

        var headers = NormalizeHeaders(ParseCsvLine(firstLine));
        var headerSet = headers.ToHashSet(StringComparer.Ordinal);
        var expectedSet = Columns.ToHashSet(StringComparer.Ordinal);
        var missing = Columns.Where(column => !headerSet.Contains(column)).ToList();
        var extra = headers.Where(column => !expectedSet.Contains(column)).ToList();
        return new StoryCsvCompatibility(missing.Count == 0, missing, extra);
    }

    public StoryRow CreateDefaultRow(int index = 0)
    {
        var row = new StoryRow();
        foreach (var column in Columns)
        {
            row.Set(column, NumericColumns.Contains(column) ? "0" : string.Empty);
        }

        row.Set("Name", CreateRowName(index));
        return row;
    }

    public List<StoryRow> ReadRows(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            return [];
        }

        var lines = File.ReadAllLines(csvPath, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (lines.Count == 0)
        {
            return [];
        }

        var headers = NormalizeHeaders(ParseCsvLine(lines[0]));
        var rows = new List<StoryRow>();
        foreach (var line in lines.Skip(1))
        {
            var cells = ParseCsvLine(line);
            var row = CreateDefaultRow();
            for (var i = 0; i < headers.Count && i < cells.Count; i++)
            {
                row.Set(headers[i], cells[i]);
            }

            if (string.IsNullOrWhiteSpace(row.Get("Name")))
            {
                row.Set("Name", CreateRowName(rows.Count));
            }

            rows.Add(row);
        }

        return rows;
    }

    public void WriteRows(string csvPath, IReadOnlyList<StoryRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Columns.Select(column => EscapeCsvField(GetHeaderName(column)))));
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Set("Name", CreateRowName(i));
            builder.AppendLine(string.Join(",", Columns.Select(column => EscapeCsvField(rows[i].Get(column)))));
        }

        File.WriteAllText(csvPath, builder.ToString(), Encoding.UTF8);
    }

    public bool RowHasContent(StoryRow row)
    {
        foreach (var column in Columns.Where(column => column != "Name"))
        {
            if (!string.IsNullOrWhiteSpace(row.Get(column)) &&
                (!NumericColumns.Contains(column) || row.Get(column) != "0"))
            {
                return true;
            }
        }

        return false;
    }

    public string GetChapterCsvPath(ChapterInfo chapter)
    {
        var expectedCsv = Path.Combine(chapter.Path, $"{chapter.Code}.csv");
        if (File.Exists(expectedCsv))
        {
            return expectedCsv;
        }

        var legacyStoryCsv = Directory
            .EnumerateFiles(chapter.Path, "*.story.csv")
            .OrderBy(Path.GetFileName)
            .FirstOrDefault();
        if (legacyStoryCsv is not null)
        {
            if (!File.Exists(expectedCsv))
            {
                File.Move(legacyStoryCsv, expectedCsv);
            }

            return expectedCsv;
        }

        return expectedCsv;
    }

    public string GetSectionCsvPath(ChapterInfo chapter, int section)
    {
        return section <= 1
            ? GetChapterCsvPath(chapter)
            : Path.Combine(chapter.Path, $"{BuildSectionCsvFileBaseName(chapter.Code, section)}.csv");
    }

    public List<StorySectionCsvFile> GetLocalSectionCsvPaths(ChapterInfo chapter)
    {
        if (!Directory.Exists(chapter.Path))
        {
            return [];
        }

        var mainCsvPath = GetChapterCsvPath(chapter);
        var result = new List<StorySectionCsvFile>();
        foreach (var csvPath in Directory.EnumerateFiles(chapter.Path, "*.csv", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(csvPath).EndsWith(".story.csv", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (PathsEqual(csvPath, mainCsvPath))
            {
                result.Add(new StorySectionCsvFile(csvPath, 1));
                continue;
            }

            var section = TryParseStorySectionFromFileName(chapter, csvPath);
            if (section is not null)
            {
                result.Add(new StorySectionCsvFile(csvPath, section.Value));
            }
        }

        return result
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Section)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsLooseSectionCsvCandidate(ChapterInfo chapter, string csvPath)
    {
        var mainCsvPath = GetChapterCsvPath(chapter);
        if (PathsEqual(csvPath, mainCsvPath))
        {
            return false;
        }

        var fileName = Path.GetFileName(csvPath);
        var baseName = BuildSectionCsvBaseName(chapter.Code);
        var sectionBaseName = BuildSectionCsvChapterBaseName(chapter.Code);
        if (fileName.StartsWith($"{baseName}_小节", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(
            Path.GetFileNameWithoutExtension(csvPath),
            $"^{Regex.Escape(sectionBaseName)}[-_](?<index>\\d+)$",
            RegexOptions.IgnoreCase))
        {
            return true;
        }

        return !fileName.EndsWith(".story.csv", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith($"{sectionBaseName}_", StringComparison.OrdinalIgnoreCase);
    }

    public int? TryParseStorySectionFromFileName(ChapterInfo chapter, string csvPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(csvPath);
        var currentPrefix = BuildSectionCsvChapterBaseName(chapter.Code);
        var currentMatch = Regex.Match(
            fileName,
            $"^{Regex.Escape(currentPrefix)}[-_](?<index>\\d+)$",
            RegexOptions.IgnoreCase);
        if (currentMatch.Success)
        {
            return ParseInt(currentMatch.Groups["index"].Value) + 1;
        }

        var oldPrefix = BuildSectionCsvBaseName(chapter.Code);
        var oldMatch = Regex.Match(
            fileName,
            $"^{Regex.Escape(oldPrefix)}_小节(?<index>\\d+)$",
            RegexOptions.IgnoreCase);
        if (oldMatch.Success)
        {
            return Math.Max(1, ParseInt(oldMatch.Groups["index"].Value));
        }

        var anyOldSectionSuffix = Regex.Match(fileName, @"_小节(?<index>\d+)$", RegexOptions.IgnoreCase);
        return anyOldSectionSuffix.Success ? Math.Max(1, ParseInt(anyOldSectionSuffix.Groups["index"].Value)) : null;
    }

    public static string BuildSectionCsvBaseName(string chapterCode)
    {
        var sanitized = TextUtility.SanitizeUnrealAssetName(chapterCode);
        var suffixMatch = Regex.Match(sanitized, @"^(?<base>.+?)[-_](?<section>\d+)$");
        if (suffixMatch.Success)
        {
            return suffixMatch.Groups["base"].Value;
        }

        return sanitized;
    }

    public static string BuildSectionCsvChapterBaseName(string chapterCode)
    {
        var chapterBaseCode = RemoveChapterSectionSuffix(chapterCode);
        return BuildSectionCsvBaseName(chapterBaseCode);
    }

    public static string BuildSectionCsvFileBaseName(string chapterCode, int section)
    {
        return $"{BuildSectionCsvChapterBaseName(chapterCode)}-{Math.Max(0, section - 1):00}";
    }

    public static string RemoveChapterSectionSuffix(string chapterCode)
    {
        var match = Regex.Match(chapterCode, @"^(?<base>.+?)[-_](?<section>\d+)$");
        return match.Success ? match.Groups["base"].Value : chapterCode;
    }

    public static string CreateRowName(int index)
    {
        return (index + 1).ToString();
    }

    public static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                cells.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(ch);
            }
        }

        cells.Add(builder.ToString());
        return cells;
    }

    public static List<string> NormalizeHeaders(IReadOnlyList<string> headers)
    {
        return headers
            .Select((header, index) => IsRowNameHeader(header, index) ? "Name" : header)
            .ToList();
    }

    private static bool IsRowNameHeader(string header, int index)
    {
        return index == 0 &&
            (string.IsNullOrWhiteSpace(header) ||
                string.Equals(header.Trim(), "---", StringComparison.Ordinal) ||
                string.Equals(header.Trim(), "Name", StringComparison.Ordinal));
    }

    private static string GetHeaderName(string column)
    {
        return string.Equals(column, "Name", StringComparison.Ordinal) ? "---" : column;
    }
}
