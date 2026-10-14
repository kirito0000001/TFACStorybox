using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

internal sealed class ImageSetService
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static readonly HashSet<string> ConvertibleExtensions =
        new([".jpg", ".jpeg", ".webp"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 从文件名里抠出编号 —— **只服务背景图**（<c>BG(\d+)</c>）。
    ///
    /// 道具图片不走这里：它不进剧情 CSV，编号没有外部含义，只用于素材库内部排序，
    /// 而排序走的是 <see cref="RenameEntries"/>（那个认条目上的前缀）。
    ///
    /// 也正因为要保持签名 <c>(string) → int?</c>，它不能加前缀参数：
    /// 它是被当**方法组**传给委托的（<c>BuildAssetIndexRemap(_, ImageSetService.GetAssetIndex)</c>），
    /// 签名一变就转换不成了。
    /// </summary>
    public static int? GetAssetIndex(string imagePath)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(imagePath), @"^BG(?<index>\d+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups["index"].Value) : null;
    }

    public async Task<int> ImportFilesAsync(
        string folderPath,
        IEnumerable<string> sourcePaths,
        Func<string, string, Task> importAsPngAsync,
        string prefix = ImageSetPrefix.Background)
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
        var importedEntries = new List<ImageSetEntry>();

        foreach (var sourcePath in validSourcePaths)
        {
            var tempPngPath = Path.Combine(folderPath, $"__img_import_{Guid.NewGuid():N}.png");
            await importAsPngAsync(sourcePath, tempPngPath);
            importedEntries.Add(new ImageSetEntry(tempPngPath, TextUtility.SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath)), prefix));
        }

        var entries = existingOrderedPaths
            .Select(path => ParseFileName(path, prefix))
            .Concat(importedEntries)
            .ToList();
        RenameEntries(entries);
        return validSourcePaths.Count;
    }

    public async Task<int> NormalizeFilesAsync(
        string folderPath,
        Func<string, string, Task> convertToPngAsync,
        IReadOnlyList<string>? orderedPaths = null,
        string prefix = ImageSetPrefix.Background)
    {
        var sourcePaths = orderedPaths is null
            ? Directory
                .EnumerateFiles(folderPath)
                .Where(IsSupportedExtension)
                .OrderBy(Path.GetFileName)
                .ToList()
            : orderedPaths.ToList();

        var entries = new List<ImageSetEntry>();
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

            var parsed = ParseFileName(pngPath, prefix);
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

    public static void RenameEntries(IReadOnlyList<ImageSetEntry> entries)
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
                // 前缀**从条目上取**，不写死 BG —— 道具图片走同一段代码，只是前缀不同。
                var baseName = $"{entry.Prefix}{index.ToString().PadLeft(digitCount, '0')}";
                var fileName = string.IsNullOrWhiteSpace(entry.Remark)
                    ? $"{baseName}.png"
                    : $"{baseName}_{entry.Remark}.png";
                return new ImageSetRename(entry, Path.Combine(folderPath, fileName));
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

    /// <summary>
    /// 解析一个文件名，拿到（路径、备注、前缀）。
    ///
    /// <paramref name="prefix"/> 为空时**按已知前缀逐个试**，认出哪个算哪个 ——
    /// 这样"改备注""删一张"这些不关心分类的地方就不用把前缀一路传下来。
    /// 全都认不出（比如手工丢进来的文件）就按默认分类处理，备注留空。
    /// </summary>
    public static ImageSetEntry ParseFileName(string imagePath, string? prefix = null)
    {
        var name = Path.GetFileNameWithoutExtension(imagePath);
        var candidates = prefix is null ? ImageSetPrefix.All : [prefix];
        foreach (var candidate in candidates)
        {
            // 前缀转义后再进正则：万一以后有人把前缀定成带 . 或 _ 的，不至于变成通配。
            var match = Regex.Match(name, $@"^{Regex.Escape(candidate)}\d+(?:_(?<remark>.+))?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return new ImageSetEntry(imagePath, match.Groups["remark"].Value, candidate);
            }
        }

        return new ImageSetEntry(imagePath, string.Empty, prefix ?? ImageSetPrefix.Background);
    }

    public static string? FindRenamedPath(IReadOnlyList<ImageSetEntry> entries, string originalPath)
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

    public static string GetTargetPath(IReadOnlyList<ImageSetEntry> entries, int index)
    {
        var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
        var entry = entries[index];
        // 前缀从条目上取（和 RenameEntries 同一口径）—— 写死 BG 的话，
        // 道具图片重排出来的名字会变成 BG 开头，两个分类就混了。
        var baseName = $"{entry.Prefix}{index.ToString().PadLeft(digitCount, '0')}";
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
