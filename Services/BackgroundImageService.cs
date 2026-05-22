using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

internal sealed class BackgroundImageService
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static readonly HashSet<string> ConvertibleExtensions =
        new([".jpg", ".jpeg", ".webp"], StringComparer.OrdinalIgnoreCase);

    public static int? GetAssetIndex(string imagePath)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(imagePath), @"^BG(?<index>\d+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups["index"].Value) : null;
    }

    public async Task<int> ImportFilesAsync(
        string folderPath,
        IEnumerable<string> sourcePaths,
        Func<string, string, Task> importAsPngAsync)
    {
        var validSourcePaths = sourcePaths
            .Where(IsValidSourcePath)
            .ToList();

        if (validSourcePaths.Count == 0)
        {
            return 0;
        }

        Directory.CreateDirectory(folderPath);
        var existingOrderedPaths = GetFilePaths(folderPath);
        var importedEntries = new List<BackgroundImageEntry>();

        foreach (var sourcePath in validSourcePaths)
        {
            var tempPngPath = Path.Combine(folderPath, $"__bg_import_{Guid.NewGuid():N}.png");
            await importAsPngAsync(sourcePath, tempPngPath);
            importedEntries.Add(new BackgroundImageEntry(tempPngPath, TextUtility.SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath))));
        }

        var entries = existingOrderedPaths
            .Select(ParseFileName)
            .Concat(importedEntries)
            .ToList();
        RenameEntries(entries);
        return validSourcePaths.Count;
    }

    public async Task<int> NormalizeFilesAsync(
        string folderPath,
        Func<string, string, Task> convertToPngAsync,
        IReadOnlyList<string>? orderedPaths = null)
    {
        var sourcePaths = orderedPaths is null
            ? Directory
                .EnumerateFiles(folderPath)
                .Where(IsSupportedExtension)
                .OrderBy(Path.GetFileName)
                .ToList()
            : orderedPaths.ToList();

        var entries = new List<BackgroundImageEntry>();
        var convertedCount = 0;
        foreach (var sourcePath in sourcePaths)
        {
            var pngPath = sourcePath;
            var extension = Path.GetExtension(sourcePath);
            if (ConvertibleExtensions.Contains(extension))
            {
                pngPath = Path.Combine(folderPath, $"__bg_convert_{Guid.NewGuid():N}.png");
                await convertToPngAsync(sourcePath, pngPath);
                File.Delete(sourcePath);
                convertedCount++;
            }

            var parsed = ParseFileName(pngPath);
            entries.Add(parsed with { Path = pngPath });
        }

        RenameEntries(entries);
        return convertedCount;
    }

    public string? UpdateRemark(string folderPath, string imagePath, string remark)
    {
        var orderedPaths = GetFilePaths(folderPath);
        var entries = orderedPaths
            .Select(path =>
            {
                var entry = ParseFileName(path);
                return FileSystemUtility.PathsEqual(path, imagePath)
                    ? entry with { Remark = TextUtility.SanitizeRemark(remark) }
                    : entry;
            })
            .ToList();

        RenameEntries(entries);
        return FindRenamedPath(entries, imagePath);
    }

    public void DeleteAndNormalize(string folderPath, string imagePath)
    {
        File.Delete(imagePath);
        RenameEntries(GetFilePaths(folderPath).Select(ParseFileName).ToList());
    }

    public static void RenameEntries(IReadOnlyList<BackgroundImageEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
        var plannedMoves = entries
            .Select((entry, index) =>
            {
                var folderPath = Path.GetDirectoryName(entry.Path)!;
                var baseName = $"BG{index.ToString().PadLeft(digitCount, '0')}";
                var fileName = string.IsNullOrWhiteSpace(entry.Remark)
                    ? $"{baseName}.png"
                    : $"{baseName}_{entry.Remark}.png";
                return new BackgroundImageRename(entry, Path.Combine(folderPath, fileName));
            })
            .ToList();

        if (plannedMoves.All(move => FileSystemUtility.PathsExactlyEqual(move.Entry.Path, move.TargetPath)))
        {
            return;
        }

        var tempMoves = plannedMoves
            .Select(move =>
            {
                var tempPath = Path.Combine(Path.GetDirectoryName(move.Entry.Path)!, $"__bg_rename_{Guid.NewGuid():N}.png");
                File.Move(move.Entry.Path, tempPath, overwrite: true);
                return move with { Entry = move.Entry with { Path = tempPath } };
            })
            .ToList();

        foreach (var move in tempMoves)
        {
            File.Move(move.Entry.Path, move.TargetPath, overwrite: true);
        }
    }

    public static List<string> GetFilePaths(string folderPath)
    {
        return Directory
            .EnumerateFiles(folderPath, "*.png")
            .OrderBy(Path.GetFileName)
            .ToList();
    }

    public static BackgroundImageEntry ParseFileName(string imagePath)
    {
        var name = Path.GetFileNameWithoutExtension(imagePath);
        var match = Regex.Match(name, @"^BG\d+(?:_(?<remark>.+))?$", RegexOptions.IgnoreCase);
        return new BackgroundImageEntry(
            imagePath,
            match.Success ? match.Groups["remark"].Value : string.Empty);
    }

    public static string? FindRenamedPath(IReadOnlyList<BackgroundImageEntry> entries, string originalPath)
    {
        var originalIndex = entries
            .Select((entry, index) => new { entry, index })
            .FirstOrDefault(pair => FileSystemUtility.PathsEqual(pair.entry.Path, originalPath))?.index;
        if (originalIndex is null)
        {
            return null;
        }

        return GetTargetPath(entries, originalIndex.Value);
    }

    public static string GetTargetPath(IReadOnlyList<BackgroundImageEntry> entries, int index)
    {
        var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
        var entry = entries[index];
        var baseName = $"BG{index.ToString().PadLeft(digitCount, '0')}";
        var fileName = string.IsNullOrWhiteSpace(entry.Remark)
            ? $"{baseName}.png"
            : $"{baseName}_{entry.Remark}.png";
        return Path.Combine(Path.GetDirectoryName(entry.Path)!, fileName);
    }

    public static bool IsValidSourcePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path) &&
            IsSupportedExtension(path);
    }

    public static bool IsSupportedExtension(string path)
    {
        return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
