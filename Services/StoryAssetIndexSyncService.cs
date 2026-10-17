using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools.Services;

internal sealed class StoryAssetIndexSyncService
{
    private readonly Func<List<ProjectInfo>> _getProjects;
    private readonly Func<ProjectInfo, AssetLibraryInfo?> _resolveProjectAssetLibrary;
    private readonly Func<ProjectInfo, string> _getChaptersFolderPath;
    private readonly Func<string, ChapterInfo> _readChapterInfo;
    private readonly StoryDocumentRepository _storyDocumentRepository;

    public StoryAssetIndexSyncService(
        Func<List<ProjectInfo>> getProjects,
        Func<ProjectInfo, AssetLibraryInfo?> resolveProjectAssetLibrary,
        Func<ProjectInfo, string> getChaptersFolderPath,
        Func<string, ChapterInfo> readChapterInfo,
        StoryDocumentRepository storyDocumentRepository)
    {
        _getProjects = getProjects;
        _resolveProjectAssetLibrary = resolveProjectAssetLibrary;
        _getChaptersFolderPath = getChaptersFolderPath;
        _readChapterInfo = readChapterInfo;
        _storyDocumentRepository = storyDocumentRepository;
    }

    public AssetIndexSyncResult SyncGlobalAssetIndexes(
        AssetLibraryInfo assetLibrary,
        string assetLabel,
        string columnName,
        IReadOnlyDictionary<int, int> indexRemap,
        IReadOnlyDictionary<int, string> oldLabels,
        IReadOnlyDictionary<int, string> newLabels,
        int assetCount,
        IProgress<AssetIndexSyncProgress>? progress)
    {
        return SyncStoryRowsForAssetLibrary(
            assetLibrary,
            $"{assetLabel}索引同步",
            progress,
            rowContext =>
            {
                var oldIndex = ParseInt(rowContext.Row.Get(columnName));
                var changed = TryRecordStoryIndexRemap(rowContext, assetLabel, columnName, oldIndex, oldIndex, indexRemap, oldLabels, newLabels, assetCount, out var warning);
                if (warning is not null)
                {
                    rowContext.Warnings.Add(warning);
                }

                return changed;
            });
    }

    public AssetIndexSyncResult SyncCharacterFilterIndexes(
        AssetLibraryInfo assetLibrary,
        IReadOnlyDictionary<int, int> indexRemap,
        IReadOnlyDictionary<int, string> oldLabels,
        IReadOnlyDictionary<int, string> newLabels,
        int assetCount,
        IProgress<AssetIndexSyncProgress>? progress)
    {
        return SyncStoryRowsForAssetLibrary(
            assetLibrary,
            "角色滤镜索引同步",
            progress,
            rowContext =>
            {
                var changed = false;
                foreach (var columnName in Enumerable.Range(0, 6).Select(index => index == 0 ? "TalkVfx" : $"Vfx{index}"))
                {
                    var oldIndex = ParseInt(rowContext.Row.Get(columnName));
                    changed |= TryRecordStoryIndexRemap(rowContext, "角色滤镜", columnName, oldIndex, oldIndex, indexRemap, oldLabels, newLabels, assetCount, out var warning);
                    if (warning is not null)
                    {
                        rowContext.Warnings.Add(warning);
                    }
                }

                return changed;
            });
    }

    public AssetIndexSyncResult SyncCharacterLayerIndexes(
        AssetLibraryInfo assetLibrary,
        CharacterInfo character,
        CharacterLayerKind layerKind,
        string layerDisplayName,
        IReadOnlyDictionary<int, int> indexRemap,
        IReadOnlyDictionary<int, string> oldLabels,
        IReadOnlyDictionary<int, string> newLabels,
        int assetCount,
        IProgress<AssetIndexSyncProgress>? progress)
    {
        var assetLabel = $"{character.Name} {layerDisplayName}";
        var fieldPrefix = GetStoryLayerFieldPrefix(layerKind);
        return SyncStoryRowsForAssetLibrary(
            assetLibrary,
            $"{assetLabel}索引同步",
            progress,
            rowContext =>
            {
                var changed = false;
                if (StoryCharacterMatches(rowContext.Row.Get("TalkChar"), character))
                {
                    changed |= TryRecordStoryLayerRemap(rowContext, assetLabel, GetStoryLayerColumn(0, fieldPrefix), layerKind, indexRemap, oldLabels, newLabels, assetCount);
                }

                for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
                {
                    if (StoryCharacterMatches(rowContext.Row.Get(GetStoryCharacterColumn(slotIndex)), character))
                    {
                        changed |= TryRecordStoryLayerRemap(rowContext, assetLabel, GetStoryLayerColumn(slotIndex, fieldPrefix), layerKind, indexRemap, oldLabels, newLabels, assetCount);
                    }
                }

                return changed;
            });
    }

    public int UpdateGlobalAssetIndexes(AssetLibraryInfo assetLibrary, string columnName, IReadOnlyDictionary<int, int> indexRemap)
    {
        return indexRemap.Count == 0
            ? 0
            : UpdateStoryRowsForAssetLibrary(assetLibrary, row => RemapStoryIndex(row, columnName, indexRemap));
    }

    public int UpdateCharacterLayerIndexes(
        AssetLibraryInfo assetLibrary,
        CharacterInfo character,
        CharacterLayerKind layerKind,
        IReadOnlyDictionary<int, int> indexRemap)
    {
        if (indexRemap.Count == 0)
        {
            return 0;
        }

        return UpdateStoryRowsForAssetLibrary(assetLibrary, row =>
        {
            var changed = false;
            if (StoryCharacterMatches(row.Get("TalkChar"), character))
            {
                changed |= RemapStoryLayerIndex(row, GetStoryLayerColumn(0, GetStoryLayerFieldPrefix(layerKind)), layerKind, indexRemap);
            }

            for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
            {
                if (StoryCharacterMatches(row.Get(GetStoryCharacterColumn(slotIndex)), character))
                {
                    changed |= RemapStoryLayerIndex(row, GetStoryLayerColumn(slotIndex, GetStoryLayerFieldPrefix(layerKind)), layerKind, indexRemap);
                }
            }

            return changed;
        });
    }

    public int UpdateCharacterFilterIndexes(AssetLibraryInfo assetLibrary, IReadOnlyDictionary<int, int> indexRemap)
    {
        if (indexRemap.Count == 0)
        {
            return 0;
        }

        return UpdateStoryRowsForAssetLibrary(assetLibrary, row =>
        {
            var changed = RemapStoryIndex(row, "TalkVfx", indexRemap);
            for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
            {
                changed |= RemapStoryIndex(row, $"Vfx{slotIndex}", indexRemap);
            }

            return changed;
        });
    }

    public static (Dictionary<int, string> OldLabels, Dictionary<int, string> NewLabels) BuildLabelMaps(
        IReadOnlyList<string> orderedPaths,
        Func<string, int?> getOldIndex)
    {
        var oldLabels = orderedPaths
            .Select(path => new { OldIndex = getOldIndex(path), Label = Path.GetFileNameWithoutExtension(path) })
            .Where(item => item.OldIndex is not null)
            .GroupBy(item => item.OldIndex!.Value)
            .ToDictionary(group => group.Key, group => group.First().Label);
        var newLabels = orderedPaths
            .Select((path, newIndex) => new { NewIndex = newIndex, Label = Path.GetFileNameWithoutExtension(path) })
            .ToDictionary(item => item.NewIndex, item => item.Label);
        return (oldLabels, newLabels);
    }

    /// <summary>
    /// 特殊音效的索引重定向。它**不在剧情列里**，而是藏在函数串里的 <c>Scene_n</c> 标记
    ///（见 StoryFunctionService.TryParseSoundEffectIndex —— 播放时按这个号在
    ///  特殊音效目录里取第 n 个文件）。
    /// 所以拖拽重排特殊音效 = 位置变了，这些标记必须跟着改，否则"某句放的那个音效"
    /// 会静默换成另一个（和背景图/BGM 是同一类坑）。
    /// </summary>
    public AssetIndexSyncResult SyncSoundEffectIndexes(
        AssetLibraryInfo assetLibrary,
        IReadOnlyDictionary<int, int> indexRemap,
        IReadOnlyDictionary<int, string> oldLabels,
        IReadOnlyDictionary<int, string> newLabels,
        int assetCount,
        IProgress<AssetIndexSyncProgress>? progress)
    {
        return SyncStoryRowsForAssetLibrary(
            assetLibrary,
            "特殊音效索引同步",
            progress,
            rowContext =>
            {
                var functions = StoryFunctionService.SplitFunctionValues(rowContext.Row.Get("Custom")).ToList();
                if (functions.Count == 0)
                {
                    return false;
                }

                var changed = false;
                for (var index = 0; index < functions.Count; index++)
                {
                    if (!StoryFunctionService.TryParseSoundEffectIndex(functions[index], out var oldIndex))
                    {
                        continue;
                    }

                    if (oldIndex < 0 || oldIndex >= assetCount)
                    {
                        rowContext.Warnings.Add(rowContext.CreateWarning(
                            "Custom",
                            $"特殊音效 索引 {oldIndex} 超出当前素材数量 {assetCount}，未自动改动。可在章节卡右键使用“修复”检查。"));
                        continue;
                    }

                    if (!indexRemap.TryGetValue(oldIndex, out var newIndex) || oldIndex == newIndex)
                    {
                        continue;
                    }

                    functions[index] = $"Scene_{newIndex}";
                    changed = true;
                    rowContext.Changes.Add(rowContext.CreateChange(
                        "Custom",
                        oldIndex.ToString(),
                        newIndex.ToString(),
                        FormatAssetIndexLabel(oldIndex, oldLabels.TryGetValue(oldIndex, out var oldLabel) ? oldLabel : string.Empty),
                        FormatAssetIndexLabel(newIndex, newLabels.TryGetValue(newIndex, out var newLabel) ? newLabel : string.Empty)));
                }

                if (changed)
                {
                    // 函数串的分隔符就是 "/"（SplitFunctionValues 按它切）。
                    rowContext.Row.Set("Custom", string.Join("/", functions));
                }

                return changed;
            });
    }

    private AssetIndexSyncResult SyncStoryRowsForAssetLibrary(
        AssetLibraryInfo assetLibrary,
        string title,
        IProgress<AssetIndexSyncProgress>? progress,
        Func<StoryIndexRowContext, bool> updateRow)
    {
        var csvFiles = GetRelatedStoryCsvFiles(assetLibrary);
        var changes = new List<AssetIndexChange>();
        var warnings = new List<AssetIndexWarning>();
        var changedCsvPaths = new List<string>();
        progress?.Report(new AssetIndexSyncProgress("正在收集关联项目章节 CSV...", 0, 0, csvFiles.Count, 0, 0, null));

        for (var fileIndex = 0; fileIndex < csvFiles.Count; fileIndex++)
        {
            var csvFile = csvFiles[fileIndex];
            progress?.Report(new AssetIndexSyncProgress(
                $"正在扫描 {csvFile.ProjectName} / {csvFile.ChapterName}",
                csvFiles.Count == 0 ? 100 : fileIndex * 80d / csvFiles.Count,
                fileIndex,
                csvFiles.Count,
                changes.Count,
                warnings.Count,
                Path.GetFileName(csvFile.CsvPath)));

            var document = _storyDocumentRepository.Load(csvFile.Chapter);
            var rows = document.Rows;
            var changed = false;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var context = new StoryIndexRowContext(csvFile, rows[rowIndex], rowIndex, changes, warnings);
                changed |= updateRow(context);
            }

            if (changed)
            {
                _storyDocumentRepository.Save(csvFile.Chapter, document);
                changedCsvPaths.Add(csvFile.CsvPath);
            }
        }

        progress?.Report(new AssetIndexSyncProgress("索引同步检查完成。", 100, csvFiles.Count, csvFiles.Count, changes.Count, warnings.Count, null));
        return new AssetIndexSyncResult(title, csvFiles.Count, changedCsvPaths.Count, changes.Count, warnings.Count, changedCsvPaths, changes, warnings);
    }

    private List<RelatedStoryCsvFile> GetRelatedStoryCsvFiles(AssetLibraryInfo assetLibrary)
    {
        var result = new List<RelatedStoryCsvFile>();
        foreach (var project in _getProjects())
        {
            var projectAssetLibrary = _resolveProjectAssetLibrary(project);
            if (projectAssetLibrary is null || !PathsEqual(projectAssetLibrary.Path, assetLibrary.Path))
            {
                continue;
            }

            var chaptersFolderPath = _getChaptersFolderPath(project);
            if (!Directory.Exists(chaptersFolderPath))
            {
                continue;
            }

            foreach (var chapter in Directory.EnumerateDirectories(chaptersFolderPath).Select(_readChapterInfo))
            {
                if (_storyDocumentRepository.Exists(chapter))
                {
                    result.Add(new RelatedStoryCsvFile(
                        project,
                        chapter,
                        _storyDocumentRepository.GetDocumentPath(chapter)));
                }
            }
        }

        return result.OrderBy(file => file.ProjectName).ThenBy(file => file.ChapterCode).ThenBy(file => file.CsvPath).ToList();
    }

    /// <summary>
    /// 「只改了备注（文件名里的标签）」时用：号没变、位置没变，所以剧情里的**索引不用动**；
    /// 但**身份指纹记的是文件名**，必须跟着改 —— 否则下次打开章节会报一片
    /// 「记录里那张素材已不在列表里」（用户 2026-10-17 在工具箱里改备注后遇到的就是这个）。
    /// </summary>
    public int RenameAssetIdentity(
        AssetLibraryInfo assetLibrary,
        string columnName,
        string oldFileName,
        string newFileName)
    {
        if (string.IsNullOrWhiteSpace(oldFileName) ||
            string.IsNullOrWhiteSpace(newFileName) ||
            string.Equals(oldFileName, newFileName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return UpdateStoryRowsForAssetLibrary(assetLibrary, row =>
        {
            if (!row.AssetIdentities.TryGetValue(columnName, out var recorded) ||
                !string.Equals(recorded, oldFileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            row.AssetIdentities[columnName] = newFileName;
            return true;
        });
    }

    private int UpdateStoryRowsForAssetLibrary(AssetLibraryInfo assetLibrary, Func<StoryRow, bool> updateRow)
    {
        var changedFileCount = 0;
        foreach (var project in _getProjects())
        {
            var projectAssetLibrary = _resolveProjectAssetLibrary(project);
            if (projectAssetLibrary is null || !PathsEqual(projectAssetLibrary.Path, assetLibrary.Path))
            {
                continue;
            }

            var chaptersFolderPath = _getChaptersFolderPath(project);
            if (!Directory.Exists(chaptersFolderPath))
            {
                continue;
            }

            foreach (var chapter in Directory.EnumerateDirectories(chaptersFolderPath).Select(_readChapterInfo))
            {
                if (!_storyDocumentRepository.Exists(chapter))
                {
                    continue;
                }

                var document = _storyDocumentRepository.Load(chapter);
                var changed = false;
                foreach (var row in document.Rows)
                {
                    changed |= updateRow(row);
                }

                if (!changed)
                {
                    continue;
                }

                _storyDocumentRepository.Save(chapter, document);
                changedFileCount++;
            }
        }

        return changedFileCount;
    }

    private static bool TryRecordStoryLayerRemap(
        StoryIndexRowContext rowContext,
        string assetLabel,
        string columnName,
        CharacterLayerKind layerKind,
        IReadOnlyDictionary<int, int> indexRemap,
        IReadOnlyDictionary<int, string> oldLabels,
        IReadOnlyDictionary<int, string> newLabels,
        int assetCount)
    {
        var storyIndex = ParseInt(rowContext.Row.Get(columnName));
        if (layerKind == CharacterLayerKind.Adorn)
        {
            if (storyIndex <= 0)
            {
                return false;
            }

            var oldAssetIndex = storyIndex - 1;
            return TryRecordStoryIndexRemap(
                rowContext,
                assetLabel,
                columnName,
                oldAssetIndex,
                storyIndex,
                indexRemap,
                oldLabels,
                newLabels,
                assetCount,
                out var warning,
                newStoryIndexOffset: 1,
                validStoryIndexOffset: 1) || AddWarning(rowContext, warning);
        }

        return TryRecordStoryIndexRemap(rowContext, assetLabel, columnName, storyIndex, storyIndex, indexRemap, oldLabels, newLabels, assetCount, out var directWarning) ||
            AddWarning(rowContext, directWarning);
    }

    private static bool TryRecordStoryIndexRemap(
        StoryIndexRowContext rowContext,
        string assetLabel,
        string columnName,
        int oldAssetIndex,
        int oldStoryValue,
        IReadOnlyDictionary<int, int> indexRemap,
        IReadOnlyDictionary<int, string> oldLabels,
        IReadOnlyDictionary<int, string> newLabels,
        int assetCount,
        out AssetIndexWarning? warning,
        int newStoryIndexOffset = 0,
        int validStoryIndexOffset = 0)
    {
        warning = null;
        if (oldAssetIndex < 0 || oldAssetIndex >= assetCount)
        {
            warning = rowContext.CreateWarning(columnName, $"{assetLabel} 索引 {oldStoryValue} 超出当前素材数量 {assetCount}，未自动改动。可在章节卡右键使用“修复”检查。");
            return false;
        }

        if (!indexRemap.TryGetValue(oldAssetIndex, out var newAssetIndex) || oldAssetIndex == newAssetIndex)
        {
            return false;
        }

        var newStoryValue = newAssetIndex + newStoryIndexOffset;
        rowContext.Row.Set(columnName, newStoryValue.ToString());
        // 改了"号"，同一件事也必须落到**身份指纹**上：否则下次打开章节，校验会把这次
        // **有意的**重定向当成"被人动过"，白白报一片（指纹记的是文件名）。
        if (newLabels.TryGetValue(newAssetIndex, out var newIdentity) && !string.IsNullOrWhiteSpace(newIdentity))
        {
            rowContext.Row.AssetIdentities[columnName] = newIdentity;
        }
        rowContext.Changes.Add(rowContext.CreateChange(
            columnName,
            oldStoryValue.ToString(),
            newStoryValue.ToString(),
            FormatAssetIndexLabel(oldStoryValue, oldLabels.TryGetValue(oldAssetIndex, out var oldLabel) ? oldLabel : string.Empty),
            FormatAssetIndexLabel(newStoryValue, newLabels.TryGetValue(newAssetIndex, out var newLabel) ? newLabel : string.Empty)));

        if (newAssetIndex < 0 || newAssetIndex >= assetCount + validStoryIndexOffset)
        {
            warning = rowContext.CreateWarning(columnName, $"{assetLabel} remap 后索引 {newStoryValue} 仍然超出当前素材数量 {assetCount}。");
        }

        return true;
    }

    private static bool AddWarning(StoryIndexRowContext rowContext, AssetIndexWarning? warning)
    {
        if (warning is null)
        {
            return false;
        }

        rowContext.Warnings.Add(warning);
        return false;
    }

    private static string FormatAssetIndexLabel(int index, string label)
    {
        return string.IsNullOrWhiteSpace(label) ? index.ToString() : $"{index} / {label}";
    }

    private static bool RemapStoryIndex(StoryRow row, string columnName, IReadOnlyDictionary<int, int> indexRemap)
    {
        var oldIndex = ParseInt(row.Get(columnName));
        if (!indexRemap.TryGetValue(oldIndex, out var newIndex) || oldIndex == newIndex)
        {
            return false;
        }

        row.Set(columnName, newIndex.ToString());
        return true;
    }

    private static bool RemapStoryLayerIndex(
        StoryRow row,
        string columnName,
        CharacterLayerKind layerKind,
        IReadOnlyDictionary<int, int> indexRemap)
    {
        if (layerKind != CharacterLayerKind.Adorn)
        {
            return RemapStoryIndex(row, columnName, indexRemap);
        }

        var storyIndex = ParseInt(row.Get(columnName));
        if (storyIndex <= 0)
        {
            return false;
        }

        var oldAssetIndex = storyIndex - 1;
        if (!indexRemap.TryGetValue(oldAssetIndex, out var newAssetIndex) || oldAssetIndex == newAssetIndex)
        {
            return false;
        }

        row.Set(columnName, (newAssetIndex + 1).ToString());
        return true;
    }

    private static bool StoryCharacterMatches(string value, CharacterInfo character)
    {
        return string.Equals(value, character.Code, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, character.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStoryCharacterColumn(int slotIndex)
    {
        return slotIndex == 0 ? "TalkChar" : $"Chara{slotIndex}";
    }

    private static string GetStoryLayerColumn(int slotIndex, string fieldPrefix)
    {
        return slotIndex == 0 ? $"Talk{fieldPrefix}" : $"{fieldPrefix}{slotIndex}";
    }

    private static string GetStoryLayerFieldPrefix(CharacterLayerKind layerKind)
    {
        return layerKind switch
        {
            CharacterLayerKind.Cloth => "Body",
            CharacterLayerKind.Face => "Face",
            CharacterLayerKind.Adorn => "Adorn",
            CharacterLayerKind.Vfx => "Vfx",
            _ => "Body"
        };
    }
}
