using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static GalExcleTools.Services.TextUtility;

namespace GalExcleTools.Services;

internal sealed class ChapterRepairService
{
    private readonly StoryDocumentRepository _storyDocumentRepository;

    public ChapterRepairService(StoryDocumentRepository storyDocumentRepository)
    {
        _storyDocumentRepository = storyDocumentRepository;
    }

    public static ChapterRepairAssetContext BuildAssetContext(
        IReadOnlyList<CharacterInfo> characters,
        int backgroundCount,
        int bgmCount,
        int sceneCount,
        int filterCount)
    {
        var characterAssets = characters.ToDictionary(
            character => character.Code,
            character => new CharacterRepairAssetCounts(
                CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Cloth).Count,
                CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Face).Count,
                CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Adorn).Count),
            StringComparer.OrdinalIgnoreCase);
        var characterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in characters)
        {
            characterAliases[character.Code] = character.Code;
            characterAliases[character.Name] = character.Code;
        }

        return new ChapterRepairAssetContext(
            backgroundCount,
            bgmCount,
            sceneCount,
            filterCount,
            characterAliases,
            characterAssets);
    }

    public ChapterRepairResult Scan(
        ProjectInfo project,
        ChapterInfo chapter,
        ChapterRepairAssetContext context,
        bool repair,
        IProgress<ChapterRepairProgress>? progress)
    {
        var document = _storyDocumentRepository.Load(chapter);
        var documentPath = _storyDocumentRepository.GetDocumentPath(chapter);
        var sectionCount = document.RowSections.Values.Distinct().Count();
        var issues = new List<ChapterRepairIssue>();
        var changedCsvPaths = new List<string>();
        var fixedCount = 0;
        for (var rowIndex = 0; rowIndex < document.Rows.Count; rowIndex++)
        {
            var row = document.Rows[rowIndex];
            var section = document.RowSections.TryGetValue(row.Get("Name"), out var mappedSection)
                ? Math.Max(1, mappedSection)
                : 1;
            progress?.Report(new ChapterRepairProgress(
                $"正在检查第 {section} 小节剧情",
                document.Rows.Count == 0 ? 100 : rowIndex * 90d / document.Rows.Count,
                rowIndex,
                document.Rows.Count,
                issues.Count,
                fixedCount,
                Path.GetFileName(documentPath)));
            var changed = false;
            CheckRepairIndex(row, project, chapter, documentPath, rowIndex, "BGindex", "背景图", context.BackgroundCount, repair, issues, ref changed, ref fixedCount);
            CheckRepairIndex(row, project, chapter, documentPath, rowIndex, "BGM", "BGM", context.BgmCount, repair, issues, ref changed, ref fixedCount);
            CheckRepairIndex(row, project, chapter, documentPath, rowIndex, "Scene", "环境音", context.SceneCount, repair, issues, ref changed, ref fixedCount);
            ValidateRepairCharacterLayer(row, project, chapter, documentPath, rowIndex, "TalkChar", "TalkBody", "TalkFace", "TalkAdorn", "TalkVfx", "说话人", allowRawUnknownCharacter: true, context, repair, issues, ref changed, ref fixedCount);
            for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
            {
                ValidateRepairCharacterLayer(row, project, chapter, documentPath, rowIndex, $"Chara{slotIndex}", $"Body{slotIndex}", $"Face{slotIndex}", $"Adorn{slotIndex}", $"Vfx{slotIndex}", $"{slotIndex}号位", allowRawUnknownCharacter: false, context, repair, issues, ref changed, ref fixedCount);
            }

            if (changed)
            {
                if (!changedCsvPaths.Contains(documentPath, StringComparer.OrdinalIgnoreCase))
                {
                    changedCsvPaths.Add(documentPath);
                }
            }
        }

        if (repair && changedCsvPaths.Count > 0)
        {
            _storyDocumentRepository.Save(chapter, document);
        }

        progress?.Report(new ChapterRepairProgress("章节索引检查完成。", 100, sectionCount, sectionCount, issues.Count, fixedCount, null));
        return new ChapterRepairResult(project.Name, chapter.Name, chapter.Code, sectionCount, issues.Count, fixedCount, changedCsvPaths, issues);
    }

    private static void ValidateRepairCharacterLayer(
        StoryRow row,
        ProjectInfo project,
        ChapterInfo chapter,
        string csvPath,
        int rowIndex,
        string characterColumn,
        string bodyColumn,
        string faceColumn,
        string adornColumn,
        string vfxColumn,
        string label,
        bool allowRawUnknownCharacter,
        ChapterRepairAssetContext context,
        bool repair,
        List<ChapterRepairIssue> issues,
        ref bool changed,
        ref int fixedCount)
    {
        var characterValue = row.Get(characterColumn);
        if (string.IsNullOrWhiteSpace(characterValue))
        {
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}角色为空，身体索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}角色为空，表情索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}角色为空，装饰索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}角色为空，滤镜索引应归零。", repair, issues, ref changed, ref fixedCount);
            return;
        }

        if (ContainsCjk(characterValue))
        {
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}角色 `{characterValue}` 是中文/显示名，身体索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}角色 `{characterValue}` 是中文/显示名，表情索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}角色 `{characterValue}` 是中文/显示名，装饰索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}角色 `{characterValue}` 是中文/显示名，滤镜索引应归零。", repair, issues, ref changed, ref fixedCount);
            return;
        }

        if (!context.CharacterAliases.TryGetValue(characterValue, out var characterCode) ||
            !context.CharacterAssets.TryGetValue(characterCode, out var counts))
        {
            if (!allowRawUnknownCharacter)
            {
                issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, characterColumn, $"{label}角色 `{characterValue}` 不在当前素材库角色列表中，未自动改动。", canAutoFix: false));
            }

            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，身体索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，表情索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，装饰索引应归零。", repair, issues, ref changed, ref fixedCount);
            CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，滤镜索引应归零。", repair, issues, ref changed, ref fixedCount);
            return;
        }

        CheckRepairIndex(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}身体", counts.ClothCount, repair, issues, ref changed, ref fixedCount);
        CheckRepairIndex(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}表情", counts.FaceCount, repair, issues, ref changed, ref fixedCount);
        CheckRepairAdornIndex(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}装饰", counts.AdornCount, repair, issues, ref changed, ref fixedCount);
        CheckRepairIndex(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}滤镜", context.FilterCount, repair, issues, ref changed, ref fixedCount);
    }

    private static void CheckRepairDetachedCharacterLayer(
        StoryRow row,
        ProjectInfo project,
        ChapterInfo chapter,
        string csvPath,
        int rowIndex,
        string columnName,
        string message,
        bool repair,
        List<ChapterRepairIssue> issues,
        ref bool changed,
        ref int fixedCount)
    {
        var value = ParseInt(row.Get(columnName));
        if (value == 0)
        {
            return;
        }

        issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, columnName, message, canAutoFix: true));
        if (repair)
        {
            row.Set(columnName, "0");
            changed = true;
            fixedCount++;
        }
    }

    private static void CheckRepairIndex(
        StoryRow row,
        ProjectInfo project,
        ChapterInfo chapter,
        string csvPath,
        int rowIndex,
        string columnName,
        string label,
        int assetCount,
        bool repair,
        List<ChapterRepairIssue> issues,
        ref bool changed,
        ref int fixedCount)
    {
        var value = ParseInt(row.Get(columnName));
        if (value >= 0 && value < assetCount)
        {
            return;
        }

        var canAutoFix = assetCount > 0 && value != 0;
        issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, columnName, $"{label}索引 {value} 超出范围；当前可用数量 {assetCount}。", canAutoFix));
        if (repair && canAutoFix)
        {
            row.Set(columnName, "0");
            changed = true;
            fixedCount++;
        }
    }

    private static void CheckRepairAdornIndex(
        StoryRow row,
        ProjectInfo project,
        ChapterInfo chapter,
        string csvPath,
        int rowIndex,
        string columnName,
        string label,
        int assetCount,
        bool repair,
        List<ChapterRepairIssue> issues,
        ref bool changed,
        ref int fixedCount)
    {
        var value = ParseInt(row.Get(columnName));
        if (value == 0 || value > 0 && value <= assetCount)
        {
            return;
        }

        var canAutoFix = value != 0;
        issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, columnName, $"{label}索引 {value} 超出范围；0 表示无装饰，当前可用装饰数量 {assetCount}。", canAutoFix));
        if (repair && canAutoFix)
        {
            row.Set(columnName, "0");
            changed = true;
            fixedCount++;
        }
    }

    private static ChapterRepairIssue CreateChapterRepairIssue(ProjectInfo project, ChapterInfo chapter, string csvPath, StoryRow row, int rowIndex, string columnName, string message, bool canAutoFix)
    {
        return new ChapterRepairIssue(
            project.Name,
            chapter.Name,
            chapter.Code,
            Path.GetFileName(csvPath),
            row.Get("Name"),
            rowIndex + 1,
            columnName,
            message,
            canAutoFix);
    }

    private static bool ContainsCjk(string value)
    {
        return value.Any(ch =>
            ch is >= '\u3400' and <= '\u4DBF' ||
            ch is >= '\u4E00' and <= '\u9FFF' ||
            ch is >= '\uF900' and <= '\uFAFF');
    }
}
