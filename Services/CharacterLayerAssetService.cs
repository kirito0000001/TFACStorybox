using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GalExcleTools.Services;

internal sealed class CharacterLayerAssetService
{
    private readonly JsonSerializerOptions _jsonOptions;

    public CharacterLayerAssetService()
        : this(new JsonSerializerOptions { WriteIndented = true })
    {
    }

    public CharacterLayerAssetService(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public List<CharacterLayerEntry> CreateImportEntries(
        string folderPath,
        CharacterLayerKind layerKind,
        IEnumerable<string> sourcePaths,
        string defaultScope,
        out int importedCount)
    {
        var validSourcePaths = sourcePaths
            .Where(IsValidImagePath)
            .ToList();

        importedCount = validSourcePaths.Count;
        if (validSourcePaths.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(folderPath);
        var existingOrderedPaths = GetImagePaths(folderPath);
        var importedEntries = new List<CharacterLayerEntry>();

        foreach (var sourcePath in validSourcePaths)
        {
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            var tempPath = Path.Combine(folderPath, $"__{GetPrefix(layerKind).ToLowerInvariant()}_import_{Guid.NewGuid():N}{extension}");
            File.Copy(sourcePath, tempPath, overwrite: true);
            importedEntries.Add(new CharacterLayerEntry(
                tempPath,
                TextUtility.SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath)),
                defaultScope));
        }

        return existingOrderedPaths
            .Select(path => ParseFileName(path, layerKind, defaultScope))
            .Concat(importedEntries)
            .ToList();
    }

    public void NormalizeFiles(
        string folderPath,
        CharacterLayerKind layerKind,
        string defaultScope,
        string? characterCode = null,
        IReadOnlyList<string>? orderedPaths = null)
    {
        Directory.CreateDirectory(folderPath);
        var sourcePaths = orderedPaths is null
            ? GetLayerPaths(folderPath, layerKind)
            : orderedPaths.ToList();

        if (sourcePaths.Count == 0)
        {
            return;
        }

        var entries = sourcePaths
            .Select(path => ParseFileName(path, layerKind, defaultScope))
            .ToList();
        RenameEntriesAndScopeMeta(entries, layerKind, characterCode);
    }

    public void RenameEntriesAndScopeMeta(
        IReadOnlyList<CharacterLayerEntry> entries,
        CharacterLayerKind layerKind,
        string? characterCode = null)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var folderPath = Path.GetDirectoryName(entries[0].Path)!;
        var renameMap = BuildRenameMap(entries, layerKind, characterCode);

        RenameEntries(entries, layerKind, characterCode);
        if (UsesScopeMeta(layerKind))
        {
            RemapScopeMeta(folderPath, layerKind, renameMap);
        }

        if (layerKind is CharacterLayerKind.Cloth or CharacterLayerKind.Face or CharacterLayerKind.Adorn)
        {
            RemapPortraitPreviewMeta(folderPath, layerKind, renameMap);
        }
    }

    public void RenameEntries(
        IReadOnlyList<CharacterLayerEntry> entries,
        CharacterLayerKind layerKind,
        string? characterCode = null)
    {
        var renames = BuildRenames(entries, layerKind, characterCode);
        var tempRenames = new List<(string TempPath, string TargetPath)>();
        foreach (var rename in renames)
        {
            if (FileSystemUtility.PathsExactlyEqual(rename.Entry.Path, rename.TargetPath))
            {
                continue;
            }

            var tempPath = Path.Combine(Path.GetDirectoryName(rename.Entry.Path)!, $"__character_layer_rename_{Guid.NewGuid():N}{Path.GetExtension(rename.Entry.Path)}");
            File.Move(rename.Entry.Path, tempPath);
            tempRenames.Add((tempPath, rename.TargetPath));
        }

        foreach (var (tempPath, targetPath) in tempRenames)
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
        }
    }

    public List<CharacterLayerEntry> CreateRemarkEntries(
        string folderPath,
        string targetPath,
        CharacterLayerKind layerKind,
        string defaultScope,
        string remark)
    {
        var safeRemark = TextUtility.SanitizeRemark(remark);
        return GetImagePaths(folderPath)
            .Select(path =>
            {
                var entry = ParseFileName(path, layerKind, defaultScope);
                return FileSystemUtility.PathsExactlyEqual(path, targetPath)
                    ? entry with { Remark = safeRemark }
                    : entry;
            })
            .ToList();
    }

    public List<CharacterLayerEntry> DeleteFileAndCreateRemainingEntries(
        string filePath,
        string folderPath,
        CharacterLayerKind layerKind,
        string defaultScope)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return GetImagePaths(folderPath)
            .Select(path => ParseFileName(path, layerKind, defaultScope))
            .ToList();
    }

    public int FindEntryIndex(IReadOnlyList<CharacterLayerEntry> entries, string targetPath)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (FileSystemUtility.PathsExactlyEqual(entries[i].Path, targetPath))
            {
                return i;
            }
        }

        return -1;
    }

    public IReadOnlyDictionary<string, string> BuildRenameMap(
        IReadOnlyList<CharacterLayerEntry> entries,
        CharacterLayerKind layerKind,
        string? characterCode = null)
    {
        return entries
            .Select((entry, index) => new
            {
                OldFileName = Path.GetFileName(entry.Path),
                NewFileName = Path.GetFileName(GetTargetPath(entries, index, layerKind, characterCode))
            })
            .Where(item => !string.Equals(item.OldFileName, item.NewFileName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.OldFileName, item => item.NewFileName, StringComparer.OrdinalIgnoreCase);
    }

    public CharacterLayerScopeMeta ReadScopeMeta(string folderPath, CharacterLayerKind layerKind)
    {
        var metaPath = GetScopeMetaPath(folderPath, layerKind);
        if (string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath))
        {
            return new CharacterLayerScopeMeta();
        }

        try
        {
            return JsonSerializer.Deserialize<CharacterLayerScopeMeta>(File.ReadAllText(metaPath)) ?? new CharacterLayerScopeMeta();
        }
        catch
        {
            return new CharacterLayerScopeMeta();
        }
    }

    public void WriteScopeMeta(string folderPath, CharacterLayerKind layerKind, CharacterLayerScopeMeta meta)
    {
        var metaPath = GetScopeMetaPath(folderPath, layerKind);
        if (string.IsNullOrWhiteSpace(metaPath))
        {
            return;
        }

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
    }

    public void SaveScopeEntry(
        string folderPath,
        CharacterLayerKind layerKind,
        string fileName,
        CharacterLayerScopeEntry entry)
    {
        var meta = ReadScopeMeta(folderPath, layerKind);
        meta.Entries[fileName] = entry;
        WriteScopeMeta(folderPath, layerKind, meta);
    }

    public void RemoveScopeEntry(string folderPath, CharacterLayerKind layerKind, string fileName)
    {
        var meta = ReadScopeMeta(folderPath, layerKind);
        if (meta.Entries.Remove(fileName))
        {
            WriteScopeMeta(folderPath, layerKind, meta);
        }
    }

    public void RemapScopeMeta(
        string folderPath,
        CharacterLayerKind layerKind,
        IReadOnlyDictionary<string, string> renameMap)
    {
        if (renameMap.Count == 0)
        {
            return;
        }

        var meta = ReadScopeMeta(folderPath, layerKind);
        if (meta.Entries.Count == 0)
        {
            return;
        }

        var updatedEntries = new Dictionary<string, CharacterLayerScopeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, entry) in meta.Entries)
        {
            var targetFileName = renameMap.TryGetValue(fileName, out var renamedFileName)
                ? renamedFileName
                : fileName;
            updatedEntries[targetFileName] = entry;
        }

        meta.Entries = updatedEntries;
        WriteScopeMeta(folderPath, layerKind, meta);
    }

    public CharacterPortraitPreviewMeta ReadPortraitPreviewMeta(CharacterInfo character)
    {
        var metaPath = WorkspacePathUtility.GetCharacterPortraitPreviewMetaPath(character);
        if (!File.Exists(metaPath))
        {
            return new CharacterPortraitPreviewMeta();
        }

        try
        {
            return JsonSerializer.Deserialize<CharacterPortraitPreviewMeta>(File.ReadAllText(metaPath)) ?? new CharacterPortraitPreviewMeta();
        }
        catch
        {
            return new CharacterPortraitPreviewMeta();
        }
    }

    public void WritePortraitPreviewMeta(CharacterInfo character, CharacterPortraitPreviewMeta meta)
    {
        var folderPath = WorkspacePathUtility.GetCharacterPortraitPreviewFolderPath(character);
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(WorkspacePathUtility.GetCharacterPortraitPreviewMetaPath(character), JsonSerializer.Serialize(meta, _jsonOptions));
    }

    public string? ResolvePortraitPreviewPath(CharacterInfo character, string layerFileName)
    {
        var meta = ReadPortraitPreviewMeta(character);
        return ResolvePortraitPreviewPath(character, meta, layerFileName);
    }

    public void SetPortraitPreview(CharacterInfo character, string layerFileName, string sourcePath)
    {
        if (!IsValidImagePath(sourcePath))
        {
            throw new IOException($"Unsupported preview image: {Path.GetFileName(sourcePath)}");
        }

        var previewFolderPath = WorkspacePathUtility.GetCharacterPortraitPreviewFolderPath(character);
        Directory.CreateDirectory(previewFolderPath);
        var layerKey = GetPortraitPreviewLayerKey(layerFileName);
        if (layerKey is null)
        {
            throw new IOException($"Unsupported preview layer: {layerFileName}");
        }

        var previewFileName = BuildPortraitPreviewFileName(character, layerKey, sourcePath);
        var previewPath = Path.Combine(previewFolderPath, previewFileName);
        if (!FileSystemUtility.PathsExactlyEqual(sourcePath, previewPath))
        {
            File.Copy(sourcePath, previewPath, overwrite: true);
        }

        var meta = ReadPortraitPreviewMeta(character);
        var oldPreviewFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in GetPortraitPreviewMatchingKeys(meta, layerKey, layerFileName))
        {
            if (meta.Entries.TryGetValue(key, out var existingEntry) &&
                !string.IsNullOrWhiteSpace(existingEntry.PreviewFileName) &&
                !string.Equals(existingEntry.PreviewFileName, previewFileName, StringComparison.OrdinalIgnoreCase))
            {
                oldPreviewFileNames.Add(existingEntry.PreviewFileName);
            }

            meta.Entries.Remove(key);
        }

        meta.Entries[layerKey] = new CharacterPortraitPreviewEntry { PreviewFileName = previewFileName };
        foreach (var oldPreviewFileName in oldPreviewFileNames)
        {
            DeletePortraitPreviewFileIfUnreferenced(previewFolderPath, meta, oldPreviewFileName);
        }

        WritePortraitPreviewMeta(character, meta);
    }

    public void RemovePortraitPreview(CharacterInfo character, string layerFileName)
    {
        var previewFolderPath = WorkspacePathUtility.GetCharacterPortraitPreviewFolderPath(character);
        var meta = ReadPortraitPreviewMeta(character);
        var layerKey = GetPortraitPreviewLayerKey(layerFileName);
        var previewFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = false;
        foreach (var key in GetPortraitPreviewMatchingKeys(meta, layerKey, layerFileName))
        {
            if (meta.Entries.TryGetValue(key, out var entry) && !string.IsNullOrWhiteSpace(entry.PreviewFileName))
            {
                previewFileNames.Add(entry.PreviewFileName);
            }

            removed |= meta.Entries.Remove(key);
        }

        if (!removed)
        {
            return;
        }

        foreach (var previewFileName in previewFileNames)
        {
            DeletePortraitPreviewFileIfUnreferenced(previewFolderPath, meta, previewFileName);
        }

        WritePortraitPreviewMeta(character, meta);
    }

    public List<string> GetMissingPortraitPreviewLayerNames(CharacterInfo character)
    {
        var result = new List<string>();
        var meta = ReadPortraitPreviewMeta(character);
        foreach (var layerKind in new[] { CharacterLayerKind.Cloth, CharacterLayerKind.Face, CharacterLayerKind.Adorn })
        {
            foreach (var layerPath in GetLayerPaths(character, layerKind))
            {
                var fileName = Path.GetFileName(layerPath);
                if (ResolvePortraitPreviewPath(character, meta, fileName) is null)
                {
                    result.Add(fileName);
                }
            }
        }

        return result;
    }

    public IReadOnlyDictionary<string, string> GetPortraitPreviewPathsByLayerFileName(CharacterInfo character)
    {
        var meta = ReadPortraitPreviewMeta(character);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layerKind in new[] { CharacterLayerKind.Cloth, CharacterLayerKind.Face, CharacterLayerKind.Adorn })
        {
            foreach (var layerPath in GetLayerPaths(character, layerKind))
            {
                var fileName = Path.GetFileName(layerPath);
                var previewPath = ResolvePortraitPreviewPath(character, meta, fileName);
                if (previewPath is not null)
                {
                    result[fileName] = previewPath;
                }
            }
        }

        return result;
    }

    public void CleanupPortraitPreviewMeta(CharacterInfo character)
    {
        var previewFolderPath = WorkspacePathUtility.GetCharacterPortraitPreviewFolderPath(character);
        Directory.CreateDirectory(previewFolderPath);
        var validLayersByKey = new[] { CharacterLayerKind.Cloth, CharacterLayerKind.Face, CharacterLayerKind.Adorn }
            .SelectMany(layerKind => GetLayerPaths(character, layerKind))
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .Select(fileName => new { FileName = fileName, Key = GetPortraitPreviewLayerKey(fileName) })
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(item => item.Key!, item => item.FileName, StringComparer.OrdinalIgnoreCase);
        var meta = ReadPortraitPreviewMeta(character);
        var cleanedEntries = new Dictionary<string, CharacterPortraitPreviewEntry>(StringComparer.OrdinalIgnoreCase);
        var referencedPreviewFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (layerKey, layerFileName) in validLayersByKey)
        {
            if (!TryGetPortraitPreviewEntry(meta, layerKey, layerFileName, out var entry) ||
                string.IsNullOrWhiteSpace(entry.PreviewFileName))
            {
                continue;
            }

            var previewPath = Path.Combine(previewFolderPath, entry.PreviewFileName);
            if (!File.Exists(previewPath))
            {
                continue;
            }

            cleanedEntries[layerKey] = entry;
            referencedPreviewFiles.Add(entry.PreviewFileName);
        }

        var changed = !ArePortraitPreviewEntriesEqual(meta.Entries, cleanedEntries);
        meta.Entries = cleanedEntries;
        foreach (var previewPath in GetImagePaths(previewFolderPath))
        {
            if (!referencedPreviewFiles.Contains(Path.GetFileName(previewPath)))
            {
                File.Delete(previewPath);
                changed = true;
            }
        }

        if (changed)
        {
            WritePortraitPreviewMeta(character, meta);
        }
    }

    private void RemapPortraitPreviewMeta(
        string layerFolderPath,
        CharacterLayerKind layerKind,
        IReadOnlyDictionary<string, string> renameMap)
    {
        if (renameMap.Count == 0)
        {
            return;
        }

        var characterPath = Directory.GetParent(layerFolderPath)?.FullName;
        if (string.IsNullOrWhiteSpace(characterPath))
        {
            return;
        }

        var character = new CharacterInfo(string.Empty, string.Empty, string.Empty, characterPath);
        var meta = ReadPortraitPreviewMeta(character);
        if (meta.Entries.Count == 0)
        {
            return;
        }

        var keyRenameMap = renameMap
            .Select(item => new
            {
                OldKey = GetPortraitPreviewLayerKey(item.Key),
                NewKey = GetPortraitPreviewLayerKey(item.Value)
            })
            .Where(item => item.OldKey is not null && item.NewKey is not null)
            .Where(item => !string.Equals(item.OldKey, item.NewKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.OldKey!, item => item.NewKey!, StringComparer.OrdinalIgnoreCase);
        var updatedEntries = new Dictionary<string, CharacterPortraitPreviewEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, entry) in meta.Entries)
        {
            var layerKey = GetPortraitPreviewLayerKey(fileName);
            var targetKey = fileName;
            if (layerKey is not null && keyRenameMap.TryGetValue(layerKey, out var renamedKey))
            {
                targetKey = renamedKey;
            }
            else if (renameMap.TryGetValue(fileName, out var renamedFileName))
            {
                targetKey = GetPortraitPreviewLayerKey(renamedFileName) ?? renamedFileName;
            }
            else if (layerKey is not null && layerKind == GetKindFromLayerKey(layerKey))
            {
                targetKey = layerKey;
            }

            updatedEntries[targetKey] = entry;
        }

        meta.Entries = updatedEntries;
        WritePortraitPreviewMeta(character, meta);
    }

    public bool IsCompatibleWithCloth(
        CharacterInfo character,
        string? clothPath,
        string? layerPath,
        Func<string, string> computeFileHash)
    {
        if (string.IsNullOrWhiteSpace(layerPath) || !File.Exists(layerPath))
        {
            return false;
        }

        var clothIndex = GetIndex(clothPath, CharacterLayerKind.Cloth);
        if (clothIndex is null)
        {
            return true;
        }

        var layerKind = GetKindFromPath(layerPath);
        if (layerKind is null || layerKind == CharacterLayerKind.Cloth)
        {
            return true;
        }

        if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn)
        {
            var layerFolderPath = GetCharacterFolderPath(character, layerKind.Value);
            var meta = ReadScopeMeta(layerFolderPath, layerKind.Value);
            if (meta.Entries.TryGetValue(Path.GetFileName(layerPath), out var metaEntry))
            {
                if (metaEntry.UseAllCostumes || string.IsNullOrWhiteSpace(clothPath) || !File.Exists(clothPath))
                {
                    return true;
                }

                var selectedCostumeHash = computeFileHash(clothPath);
                return metaEntry.CostumeHashes.Contains(selectedCostumeHash, StringComparer.OrdinalIgnoreCase);
            }
        }

        if (!UsesScope(layerKind.Value))
        {
            return true;
        }

        var entry = ParseFileName(layerPath, layerKind.Value, "ALL");
        return IsScopeMatchingCostume(entry.Scope, clothIndex.Value);
    }

    public bool IsScopeMetaCompatibleWithCloth(
        CharacterInfo character,
        string? clothPath,
        string layerPath,
        CharacterLayerKind layerKind,
        Func<string, string> computeFileHash)
    {
        if (clothPath is null)
        {
            return true;
        }

        var layerFolderPath = GetCharacterFolderPath(character, layerKind);
        var meta = ReadScopeMeta(layerFolderPath, layerKind);
        if (!meta.Entries.TryGetValue(Path.GetFileName(layerPath), out var entry) || entry.UseAllCostumes)
        {
            return true;
        }

        if (!File.Exists(clothPath))
        {
            return true;
        }

        var selectedCostumeHash = computeFileHash(clothPath);
        return entry.CostumeHashes.Contains(selectedCostumeHash, StringComparer.OrdinalIgnoreCase);
    }

    public static List<string> GetImagePaths(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(folderPath)
            .Where(BackgroundImageService.IsSupportedExtension)
            .OrderBy(Path.GetFileName)
            .ToList();
    }

    public static List<string> GetLayerPaths(CharacterInfo character, CharacterLayerKind layerKind)
    {
        return GetLayerPaths(GetCharacterFolderPath(character, layerKind), layerKind);
    }

    public static string? GetStoryLayerPath(CharacterInfo character, CharacterLayerKind layerKind, int index)
    {
        if (layerKind == CharacterLayerKind.Adorn && index <= 0)
        {
            return null;
        }

        var paths = GetLayerPaths(character, layerKind);
        var lookupIndex = layerKind == CharacterLayerKind.Adorn ? index - 1 : index;
        if (lookupIndex < 0 || lookupIndex >= paths.Count)
        {
            return null;
        }

        return paths[lookupIndex];
    }

    public static List<string> GetLayerPaths(string folderPath, CharacterLayerKind layerKind)
    {
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(folderPath)
            .Where(path => layerKind == CharacterLayerKind.Vfx || BackgroundImageService.IsSupportedExtension(path))
            .OrderBy(Path.GetFileName)
            .ToList();
    }

    public static CharacterLayerEntry ParseFileName(
        string layerPath,
        CharacterLayerKind layerKind,
        string defaultScope)
    {
        var name = Path.GetFileNameWithoutExtension(layerPath);
        var prefix = GetPrefix(layerKind);
        var prefixPattern = layerKind == CharacterLayerKind.Cloth
            ? $"(?:.+_)?{Regex.Escape(prefix)}\\d+"
            : $"{Regex.Escape(prefix)}\\d+";
        var match = Regex.Match(name, $"^{prefixPattern}(?:_(?<tail>.+))?$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return new CharacterLayerEntry(layerPath, TextUtility.SanitizeRemark(name), defaultScope);
        }

        var tail = match.Groups["tail"].Value;
        if (string.IsNullOrWhiteSpace(tail))
        {
            return new CharacterLayerEntry(layerPath, string.Empty, defaultScope);
        }

        if (IsScope(tail))
        {
            return new CharacterLayerEntry(layerPath, string.Empty, NormalizeScope(tail));
        }

        var lastSeparatorIndex = tail.LastIndexOf('_');
        if (lastSeparatorIndex > 0)
        {
            var maybeScope = tail[(lastSeparatorIndex + 1)..];
            if (IsScope(maybeScope))
            {
                return new CharacterLayerEntry(
                    layerPath,
                    TextUtility.SanitizeRemark(tail[..lastSeparatorIndex]),
                    NormalizeScope(maybeScope));
            }
        }

        if (!UsesScope(layerKind))
        {
            return new CharacterLayerEntry(layerPath, TextUtility.SanitizeRemark(tail), string.Empty);
        }

        return new CharacterLayerEntry(layerPath, TextUtility.SanitizeRemark(tail), defaultScope);
    }

    public static string BuildFileName(
        string indexName,
        string remark,
        string scope,
        CharacterLayerKind layerKind,
        string extension,
        string? characterCode = null)
    {
        var safeRemark = TextUtility.SanitizeRemark(remark);
        var normalizedScope = UsesScope(layerKind)
            ? NormalizeScope(string.IsNullOrWhiteSpace(scope) ? "ALL" : scope)
            : string.Empty;
        var safeCharacterName = string.IsNullOrWhiteSpace(characterCode) ? string.Empty : TextUtility.SanitizeRemark(characterCode);
        var normalizedIndexName = layerKind == CharacterLayerKind.Cloth && !string.IsNullOrWhiteSpace(safeCharacterName)
            ? $"{safeCharacterName}_{indexName}"
            : indexName;

        if (!UsesScope(layerKind))
        {
            return string.IsNullOrWhiteSpace(safeRemark)
                ? $"{normalizedIndexName}{extension}"
                : $"{normalizedIndexName}_{safeRemark}{extension}";
        }

        return string.IsNullOrWhiteSpace(safeRemark)
            ? $"{normalizedIndexName}_{normalizedScope}{extension}"
            : $"{normalizedIndexName}_{safeRemark}_{normalizedScope}{extension}";
    }

    public static string GetTargetPath(
        IReadOnlyList<CharacterLayerEntry> entries,
        int index,
        CharacterLayerKind layerKind,
        string? characterCode = null)
    {
        var entry = entries[index];
        var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
        var indexName = $"{GetPrefix(layerKind)}{index.ToString().PadLeft(digitCount, '0')}";
        var fileName = BuildFileName(
            indexName,
            entry.Remark,
            entry.Scope,
            layerKind,
            Path.GetExtension(entry.Path).ToLowerInvariant(),
            characterCode);
        return Path.Combine(Path.GetDirectoryName(entry.Path)!, fileName);
    }

    public static string GetDefaultScope(int costumeCount)
    {
        if (costumeCount <= 0)
        {
            return "ALL";
        }

        var lastIndex = costumeCount - 1;
        var digitCount = Math.Max(2, lastIndex.ToString().Length);
        var startText = 0.ToString().PadLeft(digitCount, '0');
        if (costumeCount == 1)
        {
            return $"DN{startText}";
        }

        var endText = lastIndex.ToString().PadLeft(digitCount, '0');
        return $"DN{startText}-{endText}";
    }

    public static string NormalizeScope(string scope)
    {
        var trimmed = scope.Trim().ToUpperInvariant();
        if (trimmed == "ALL")
        {
            return "ALL";
        }

        var match = Regex.Match(trimmed, @"^DN(?<start>\d+)(?:-(?<end>\d+))?$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return "ALL";
        }

        var start = int.Parse(match.Groups["start"].Value);
        var endText = match.Groups["end"].Value;
        var digitCount = Math.Max(2, Math.Max(match.Groups["start"].Value.Length, endText.Length));
        var startText = start.ToString().PadLeft(digitCount, '0');
        if (string.IsNullOrWhiteSpace(endText))
        {
            return $"DN{startText}";
        }

        var end = int.Parse(endText);
        var endTextPadded = end.ToString().PadLeft(digitCount, '0');
        return $"DN{startText}-{endTextPadded}";
    }

    public static bool IsScope(string scope)
    {
        return Regex.IsMatch(scope.Trim(), @"^(ALL|DN\d+(?:-\d+)?)$", RegexOptions.IgnoreCase);
    }

    public static bool IsScopeMatchingCostume(string scope, int costumeIndex)
    {
        var normalized = NormalizeScope(scope);
        if (normalized == "ALL")
        {
            return true;
        }

        var match = Regex.Match(normalized, @"^DN(?<start>\d+)(?:-(?<end>\d+))?$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return true;
        }

        var start = int.Parse(match.Groups["start"].Value);
        var end = string.IsNullOrWhiteSpace(match.Groups["end"].Value)
            ? start
            : int.Parse(match.Groups["end"].Value);
        return costumeIndex >= Math.Min(start, end) && costumeIndex <= Math.Max(start, end);
    }

    public static int? GetIndex(string? layerPath, CharacterLayerKind expectedKind)
    {
        if (string.IsNullOrWhiteSpace(layerPath))
        {
            return null;
        }

        var prefix = GetPrefix(expectedKind);
        var pattern = expectedKind == CharacterLayerKind.Cloth
            ? $"^(?:.+_)?{Regex.Escape(prefix)}(?<index>\\d+)"
            : $"^{Regex.Escape(prefix)}(?<index>\\d+)";
        var match = Regex.Match(Path.GetFileNameWithoutExtension(layerPath), pattern, RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups["index"].Value) : null;
    }

    public static CharacterLayerKind? GetKindFromPath(string layerPath)
    {
        var name = Path.GetFileNameWithoutExtension(layerPath);
        if (Regex.IsMatch(name, "^(?:.+_)?DN\\d+", RegexOptions.IgnoreCase))
        {
            return CharacterLayerKind.Cloth;
        }

        if (Regex.IsMatch(name, "^FC\\d+", RegexOptions.IgnoreCase))
        {
            return CharacterLayerKind.Face;
        }

        if (Regex.IsMatch(name, "^AD\\d+", RegexOptions.IgnoreCase))
        {
            return CharacterLayerKind.Adorn;
        }

        if (Regex.IsMatch(name, "^VFX\\d+", RegexOptions.IgnoreCase))
        {
            return CharacterLayerKind.Vfx;
        }

        return null;
    }

    public static string GetPrefix(CharacterLayerKind layerKind)
    {
        return layerKind switch
        {
            CharacterLayerKind.Cloth => "DN",
            CharacterLayerKind.Face => "FC",
            CharacterLayerKind.Adorn => "AD",
            CharacterLayerKind.Vfx => "VFX",
            _ => "LY"
        };
    }

    public static string? GetPortraitPreviewLayerKey(string? layerFileName)
    {
        if (string.IsNullOrWhiteSpace(layerFileName))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(layerFileName);
        var match = Regex.Match(name, @"(?:^|_)DN(?<index>\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"DN{match.Groups["index"].Value}";
        }

        match = Regex.Match(name, @"^FC(?<index>\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"FC{match.Groups["index"].Value}";
        }

        match = Regex.Match(name, @"^AD(?<index>\d+)", RegexOptions.IgnoreCase);
        return match.Success ? $"AD{match.Groups["index"].Value}" : null;
    }

    private static CharacterLayerKind? GetKindFromLayerKey(string layerKey)
    {
        if (layerKey.StartsWith("DN", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterLayerKind.Cloth;
        }

        if (layerKey.StartsWith("FC", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterLayerKind.Face;
        }

        if (layerKey.StartsWith("AD", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterLayerKind.Adorn;
        }

        return null;
    }

    private static IEnumerable<string> GetPortraitPreviewMetaKeys(string? layerKey, string layerFileName)
    {
        if (!string.IsNullOrWhiteSpace(layerKey))
        {
            yield return layerKey;
        }

        if (string.IsNullOrWhiteSpace(layerKey) ||
            !string.Equals(layerKey, layerFileName, StringComparison.OrdinalIgnoreCase))
        {
            yield return layerFileName;
        }
    }

    private static bool TryGetPortraitPreviewEntry(
        CharacterPortraitPreviewMeta meta,
        string? layerKey,
        string layerFileName,
        out CharacterPortraitPreviewEntry entry)
    {
        foreach (var key in GetPortraitPreviewMetaKeys(layerKey, layerFileName))
        {
            if (meta.Entries.TryGetValue(key, out entry!))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(layerKey))
        {
            foreach (var (key, value) in meta.Entries)
            {
                if (string.Equals(GetPortraitPreviewLayerKey(key), layerKey, StringComparison.OrdinalIgnoreCase))
                {
                    entry = value;
                    return true;
                }
            }
        }

        entry = new CharacterPortraitPreviewEntry();
        return false;
    }

    private static IReadOnlyCollection<string> GetPortraitPreviewMatchingKeys(
        CharacterPortraitPreviewMeta meta,
        string? layerKey,
        string layerFileName)
    {
        var keys = new HashSet<string>(GetPortraitPreviewMetaKeys(layerKey, layerFileName), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(layerKey))
        {
            foreach (var key in meta.Entries.Keys)
            {
                if (string.Equals(GetPortraitPreviewLayerKey(key), layerKey, StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static string? ResolvePortraitPreviewPath(
        CharacterInfo character,
        CharacterPortraitPreviewMeta meta,
        string layerFileName)
    {
        var layerKey = GetPortraitPreviewLayerKey(layerFileName);
        if (!TryGetPortraitPreviewEntry(meta, layerKey, layerFileName, out var entry) ||
            string.IsNullOrWhiteSpace(entry.PreviewFileName))
        {
            return null;
        }

        var path = Path.Combine(WorkspacePathUtility.GetCharacterPortraitPreviewFolderPath(character), entry.PreviewFileName);
        return File.Exists(path) ? path : null;
    }

    private static void DeletePortraitPreviewFileIfUnreferenced(
        string previewFolderPath,
        CharacterPortraitPreviewMeta meta,
        string previewFileName)
    {
        if (meta.Entries.Values.Any(entry =>
                string.Equals(entry.PreviewFileName, previewFileName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var previewPath = Path.Combine(previewFolderPath, previewFileName);
        if (File.Exists(previewPath))
        {
            File.Delete(previewPath);
        }
    }

    private static bool ArePortraitPreviewEntriesEqual(
        IReadOnlyDictionary<string, CharacterPortraitPreviewEntry> first,
        IReadOnlyDictionary<string, CharacterPortraitPreviewEntry> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        foreach (var (key, value) in first)
        {
            if (!second.TryGetValue(key, out var otherValue) ||
                !string.Equals(value.PreviewFileName, otherValue.PreviewFileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool UsesScope(CharacterLayerKind layerKind)
    {
        return layerKind is CharacterLayerKind.Adorn or CharacterLayerKind.Vfx;
    }

    public static bool UsesScopeMeta(CharacterLayerKind layerKind)
    {
        return layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn;
    }

    public static string? GetScopeMetaPath(string folderPath, CharacterLayerKind layerKind)
    {
        return layerKind switch
        {
            CharacterLayerKind.Face => WorkspacePathUtility.GetCharacterFaceScopeMetaPath(folderPath),
            CharacterLayerKind.Adorn => WorkspacePathUtility.GetCharacterAdornScopeMetaPath(folderPath),
            _ => null
        };
    }

    public static string GetFolderName(CharacterLayerKind layerKind)
    {
        return layerKind switch
        {
            CharacterLayerKind.Cloth => "DN_Cloth",
            CharacterLayerKind.Face => "FC_Face",
            CharacterLayerKind.Adorn => "AD_Adorn",
            CharacterLayerKind.Vfx => "VFX",
            _ => "DN_Cloth"
        };
    }

    public static string BuildPortraitPreviewFileName(CharacterInfo character, string layerKey, string sourcePath)
    {
        var characterCode = TextUtility.SanitizeCharacterFolderName(character.Code);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        return $"Preview-{characterCode}-{layerKey}{extension}";
    }

    public static string GetCharacterFolderPath(CharacterInfo character, CharacterLayerKind layerKind)
    {
        return Path.Combine(character.Path, GetFolderName(layerKind));
    }

    public static bool IsValidImagePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path) &&
            BackgroundImageService.IsSupportedExtension(path);
    }

    private List<CharacterLayerRename> BuildRenames(
        IReadOnlyList<CharacterLayerEntry> entries,
        CharacterLayerKind layerKind,
        string? characterCode = null)
    {
        var renames = new List<CharacterLayerRename>();
        for (var i = 0; i < entries.Count; i++)
        {
            renames.Add(new CharacterLayerRename(entries[i], GetTargetPath(entries, i, layerKind, characterCode)));
        }

        return renames;
    }
}
