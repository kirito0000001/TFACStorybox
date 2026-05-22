using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

internal sealed class AudioAssetService
{
    public static readonly string[] Extensions = [".wav"];

    public static string GetDisplayName(AudioAssetKind kind)
    {
        return kind switch
        {
            AudioAssetKind.Music => "音乐",
            AudioAssetKind.Ambient => "环境音",
            AudioAssetKind.SoundEffect => "特殊音效",
            _ => "音频"
        };
    }

    public static string GetPrefix(AudioAssetKind kind)
    {
        return kind switch
        {
            AudioAssetKind.Music => "BGM",
            AudioAssetKind.Ambient => "Sc",
            AudioAssetKind.SoundEffect => "SE",
            _ => "Audio"
        };
    }

    public static int? GetAssetIndex(AudioAssetKind kind, string audioPath)
    {
        var match = Regex.Match(
            Path.GetFileNameWithoutExtension(audioPath),
            $"^{Regex.Escape(GetPrefix(kind))}(?<index>\\d+)",
            RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups["index"].Value) : null;
    }

    public int ImportFiles(AudioAssetKind kind, string folderPath, IEnumerable<string> sourcePaths)
    {
        var validSourcePaths = sourcePaths
            .Where(IsValidAudioPath)
            .ToList();

        if (validSourcePaths.Count == 0)
        {
            return 0;
        }

        Directory.CreateDirectory(folderPath);
        var existingOrderedPaths = GetFilePaths(folderPath);
        var importedEntries = new List<MusicEntry>();

        foreach (var sourcePath in validSourcePaths)
        {
            var tempWavPath = Path.Combine(folderPath, $"__{GetPrefix(kind).ToLowerInvariant()}_import_{Guid.NewGuid():N}.wav");
            File.Copy(sourcePath, tempWavPath, overwrite: true);
            importedEntries.Add(new MusicEntry(tempWavPath, TextUtility.SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath))));
        }

        var entries = existingOrderedPaths
            .Select(path => ParseFileName(kind, path))
            .Concat(importedEntries)
            .ToList();
        RenameEntries(kind, entries);
        return validSourcePaths.Count;
    }

    public void NormalizeFiles(AudioAssetKind kind, string folderPath, IReadOnlyList<string>? orderedPaths = null)
    {
        var sourcePaths = orderedPaths is null
            ? GetFilePaths(folderPath)
            : orderedPaths
                .Where(IsSupportedExtension)
                .ToList();

        var entries = sourcePaths
            .Select(path => ParseFileName(kind, path))
            .ToList();

        RenameEntries(kind, entries);
    }

    public string? UpdateRemark(AudioAssetKind kind, string folderPath, string audioPath, string remark)
    {
        var orderedPaths = GetFilePaths(folderPath);
        var entries = orderedPaths
            .Select(path =>
            {
                var entry = ParseFileName(kind, path);
                return FileSystemUtility.PathsEqual(path, audioPath)
                    ? entry with { Remark = TextUtility.SanitizeRemark(remark) }
                    : entry;
            })
            .ToList();

        RenameEntries(kind, entries);
        return FindRenamedPath(kind, entries, audioPath);
    }

    public void DeleteAndNormalize(AudioAssetKind kind, string folderPath, string audioPath)
    {
        File.Delete(audioPath);
        NormalizeFiles(kind, folderPath);
    }

    public static List<string> GetFilePaths(string folderPath)
    {
        return Directory
            .EnumerateFiles(folderPath, "*.wav")
            .OrderBy(Path.GetFileName)
            .ToList();
    }

    public static MusicEntry ParseFileName(AudioAssetKind kind, string audioPath)
    {
        var name = Path.GetFileNameWithoutExtension(audioPath);
        var match = Regex.Match(name, $"^{Regex.Escape(GetPrefix(kind))}\\d+(?:_(?<remark>.+))?$", RegexOptions.IgnoreCase);
        return new MusicEntry(
            audioPath,
            match.Success ? match.Groups["remark"].Value : string.Empty);
    }

    public static string? FindRenamedPath(AudioAssetKind kind, IReadOnlyList<MusicEntry> entries, string originalPath)
    {
        var originalIndex = entries
            .Select((entry, index) => new { entry, index })
            .FirstOrDefault(pair => FileSystemUtility.PathsEqual(pair.entry.Path, originalPath))?.index;
        if (originalIndex is null)
        {
            return null;
        }

        var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
        var entryAtIndex = entries[originalIndex.Value];
        var baseName = $"{GetPrefix(kind)}{originalIndex.Value.ToString().PadLeft(digitCount, '0')}";
        var fileName = string.IsNullOrWhiteSpace(entryAtIndex.Remark)
            ? $"{baseName}.wav"
            : $"{baseName}_{entryAtIndex.Remark}.wav";
        return Path.Combine(Path.GetDirectoryName(entryAtIndex.Path)!, fileName);
    }

    public static Task RenameEntriesAsync(AudioAssetKind kind, IReadOnlyList<MusicEntry> entries)
    {
        RenameEntries(kind, entries);
        return Task.CompletedTask;
    }

    public static void RenameEntries(AudioAssetKind kind, IReadOnlyList<MusicEntry> entries)
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
                var baseName = $"{GetPrefix(kind)}{index.ToString().PadLeft(digitCount, '0')}";
                var fileName = string.IsNullOrWhiteSpace(entry.Remark)
                    ? $"{baseName}.wav"
                    : $"{baseName}_{entry.Remark}.wav";
                return new MusicRename(entry, Path.Combine(folderPath, fileName));
            })
            .ToList();

        if (plannedMoves.All(move => FileSystemUtility.PathsExactlyEqual(move.Entry.Path, move.TargetPath)))
        {
            return;
        }

        var tempMoves = plannedMoves
            .Select(move =>
            {
                var tempPath = Path.Combine(Path.GetDirectoryName(move.Entry.Path)!, $"__{GetPrefix(kind).ToLowerInvariant()}_rename_{Guid.NewGuid():N}.wav");
                File.Move(move.Entry.Path, tempPath, overwrite: true);
                return move with { Entry = move.Entry with { Path = tempPath } };
            })
            .ToList();

        foreach (var move in tempMoves)
        {
            File.Move(move.Entry.Path, move.TargetPath, overwrite: true);
        }
    }

    public static bool IsValidAudioPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path) &&
            IsSupportedExtension(path);
    }

    private static bool IsSupportedExtension(string path)
    {
        return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
