using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools.Services;

internal static class StoryFunctionService
{
    public const string ChoiceFunctionCategory = "触发选项";
    public const string ChoiceFunctionTemplateId = "default-choice";
    public const string ChoiceFunctionTemplateIndicator = "自动生成当前章节小节Choice";
    public const string JumpFunctionCategory = "跳转";
    public const string ChapterJumpFunctionTemplateId = "default-into-chapter";
    public const string ChapterJumpFunctionTemplateIndicator = "IntoChapter_{章节}";
    public const string SegmentJumpFunctionTemplateId = "default-into-segment";
    public const string SegmentJumpFunctionTemplateIndicator = "IntoSegment_{小节}";
    public const string BgmFunctionTemplateId = "default-bgm-control";
    public const string BgmFunctionTemplateIndicator = "BGM_Start/BGM_Stop";

    public static List<FunctionEntry> CreateDefaultFunctions()
    {
        return
        [
            CreateChoiceFunctionTemplate(),
            CreateChapterJumpFunctionTemplate(),
            CreateSegmentJumpFunctionTemplate(),
            new FunctionEntry("default-scene-sfx", "播放一次性特殊音效", "Scene", "音频", [], "播放一次特殊音效，不循环。") { BranchMode = FunctionBranchMode.AssetIndex },
            new FunctionEntry("default-bglerp-mode", "背景切换模式", "BGLerpMode", "背景", [], "设置背景图切换时使用的过渡方式。") { BranchMode = FunctionBranchMode.Fixed, Branches = [new("0", "游戏入场黑屏"), new("1", "正常黑屏转场"), new("2", "背景图渐变过渡")] },
            new FunctionEntry("default-vfx-on", "开启指定特效", "VFXON", "特效", [], "开启指定名称的画面特效。") { BranchMode = FunctionBranchMode.Fixed },
            new FunctionEntry("default-vfx-off", "关闭指定特效", "VFXOFF", "特效", [], "关闭指定名称的画面特效。") { BranchMode = FunctionBranchMode.Fixed },
            new FunctionEntry("default-transanim", "播放动画序列", "TransAnim", "动画", [], "播放指定索引的动画序列。") { BranchMode = FunctionBranchMode.Fixed },
            new FunctionEntry("default-medplay", "播放视频", "MedPlay", "视频", [], "播放指定索引的视频。") { BranchMode = FunctionBranchMode.Fixed },
            CreateBgmFunctionTemplate(),
            new FunctionEntry("default-title-show", "大标题显示", "TitleShowMode", "标题", [], "使用当前剧情文本显示大标题。"),
            new FunctionEntry("default-close-all-fx", "关闭所有特效", "CloseAllFX", "特效", [], "关闭当前所有画面特效。")
        ];
    }

    public static FunctionEntry CreateChoiceFunctionTemplate()
    {
        return new FunctionEntry(ChoiceFunctionTemplateId, "创建触发选项", ChoiceFunctionTemplateIndicator, ChoiceFunctionCategory, [], "创建一组剧情选项，具体行为由项目函数决定。") { BranchMode = FunctionBranchMode.System };
    }

    public static FunctionEntry CreateChapterJumpFunctionTemplate()
    {
        return new FunctionEntry(ChapterJumpFunctionTemplateId, "跳转章节", ChapterJumpFunctionTemplateIndicator, JumpFunctionCategory, [], "跳转到指定章节。") { BranchMode = FunctionBranchMode.System };
    }

    public static FunctionEntry CreateSegmentJumpFunctionTemplate()
    {
        return new FunctionEntry(SegmentJumpFunctionTemplateId, "跳转小节", SegmentJumpFunctionTemplateIndicator, JumpFunctionCategory, [], "跳转到当前章节的指定小节。") { BranchMode = FunctionBranchMode.System };
    }

    public static FunctionEntry CreateBgmFunctionTemplate()
    {
        return new FunctionEntry(BgmFunctionTemplateId, "BGM", "BGM", "音频", [], "开始或停止当前背景音乐.")
        {
            BranchMode = FunctionBranchMode.Fixed,
            Branches = [new FunctionBranch("Start", "开始"), new FunctionBranch("Stop", "停止")]
        };
    }

    public static bool EnsureBuiltInFunctionTemplates(List<FunctionEntry> functions)
    {
        var changed = false;
        changed |= RemoveLegacyBgmFunctionTemplates(functions);
        changed |= EnsureBuiltInFunctionTemplate(functions, CreateChoiceFunctionTemplate(), IsChoiceFunctionTemplate, 0);
        changed |= EnsureBuiltInFunctionTemplate(functions, CreateChapterJumpFunctionTemplate(), IsChapterJumpFunctionTemplate, 1);
        changed |= EnsureBuiltInFunctionTemplate(functions, CreateSegmentJumpFunctionTemplate(), IsSegmentJumpFunctionTemplate, 2);
        changed |= EnsureBuiltInFunctionTemplate(functions, CreateBgmFunctionTemplate(), IsBgmFunctionTemplate, 3);
        return changed;
    }

    public static bool IsChoiceFunctionTemplate(FunctionEntry function)
    {
        return string.Equals(function.Id, ChoiceFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, ChoiceFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsChapterJumpFunctionTemplate(FunctionEntry function)
    {
        return string.Equals(function.Id, ChapterJumpFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, ChapterJumpFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSegmentJumpFunctionTemplate(FunctionEntry function)
    {
        return string.Equals(function.Id, SegmentJumpFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, SegmentJumpFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBgmFunctionTemplate(FunctionEntry function)
    {
        return string.Equals(function.Id, BgmFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, "BGM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, BgmFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
    }

    public static List<FunctionEntry> BuildCollectedFunctionEntries(IEnumerable<string> functionValues)
    {
        var families = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in functionValues.Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (string.Equals(value, "BGMStart", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "BGMStop", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(value, @"-\d{2}-Choice\d+$", RegexOptions.IgnoreCase) ||
                value.StartsWith("IntoChapter_", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("IntoSegment_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = value.Split('_', 2, StringSplitOptions.TrimEntries);
            var mainCode = parts[0];
            if (!families.TryGetValue(mainCode, out var branches))
            {
                branches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                families[mainCode] = branches;
            }
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                branches.Add(parts[1]);
            }
        }

        return families
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FunctionEntry(
                Guid.NewGuid().ToString("N"),
                pair.Key,
                pair.Key,
                string.Empty,
                [],
                "从剧情项目扫描收录。")
            {
                BranchMode = pair.Value.Count == 0 ? FunctionBranchMode.None : FunctionBranchMode.Fixed,
                Branches = pair.Value.OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                    .Select(code => new FunctionBranch(code, string.Empty))
                    .ToList()
            })
            .ToList();
    }

    public static List<FunctionEntry> MergeCollectedFunctionEntries(
        IEnumerable<FunctionEntry> existingEntries,
        IEnumerable<FunctionEntry> collectedEntries)
    {
        var result = NormalizeFunctionCatalogEntries(existingEntries).ToList();
        foreach (var collected in collectedEntries)
        {
            var index = result.FindIndex(entry =>
                string.Equals(entry.Indicator, collected.Indicator, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                result.Add(collected);
                continue;
            }

            var existing = result[index];
            var branches = existing.Branches.Concat(collected.Branches)
                .GroupBy(branch => branch.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.FirstOrDefault(branch => !string.IsNullOrWhiteSpace(branch.Remark)) ?? group.First())
                .OrderBy(branch => branch.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result[index] = existing with
            {
                BranchMode = existing.BranchMode == FunctionBranchMode.AssetIndex
                    ? FunctionBranchMode.AssetIndex
                    : branches.Count > 0 ? FunctionBranchMode.Fixed : existing.BranchMode,
                Branches = branches
            };
        }

        return result;
    }

    public static int CollectFunctionsFromChapters(
        AssetLibraryInfo assetLibrary,
        IEnumerable<ChapterInfo> chapters,
        StoryDocumentRepository repository,
        JsonSerializerOptions jsonOptions)
    {
        var values = new List<string>();
        foreach (var chapter in chapters)
        {
            if (!repository.Exists(chapter))
            {
                continue;
            }
            var document = repository.Load(chapter);
            values.AddRange(document.Rows.SelectMany(row => SplitFunctionValues(row.Get("Custom"))));
        }

        var existing = ReadFunctions(assetLibrary, jsonOptions);
        var merged = MergeCollectedFunctionEntries(existing, BuildCollectedFunctionEntries(values));
        if (JsonSerializer.Serialize(existing, jsonOptions) == JsonSerializer.Serialize(merged, jsonOptions))
        {
            return 0;
        }

        WriteFunctions(assetLibrary, merged, jsonOptions);
        return merged.Count - existing.Count;
    }

    public static string BuildChapterJumpFunctionValue(ChapterInfo chapter, string? projectCode)
    {
        var chapterCode = RemoveProjectCodePrefix(StoryCsvService.RemoveChapterSectionSuffix(chapter.Code), projectCode);
        return $"IntoChapter_{chapterCode}";
    }

    public static string BuildSegmentJumpFunctionValue(int section)
    {
        return $"IntoSegment_{Math.Max(0, section - 1):00}";
    }

    public static List<StoryObjectChoice> CreateBgmChoices()
    {
        return
        [
            new StoryObjectChoice("BGM_Start", "Start / BGM_Start", "BGM_Start"),
            new StoryObjectChoice("BGM_Stop", "Stop / BGM_Stop", "BGM_Stop")
        ];
    }

    public static List<StoryObjectChoice> CreateBackgroundLerpModeChoices()
    {
        return
        [
            new StoryObjectChoice("0", "0：游戏入场黑屏", 0),
            new StoryObjectChoice("1", "1：正常黑屏转场", 1),
            new StoryObjectChoice("2", "2：背景图渐变过渡", 2)
        ];
    }

    public static List<StoryObjectChoice> CreateChapterJumpChoices(IEnumerable<ChapterInfo> chapters, string? projectCode)
    {
        return chapters
            .Select(chapter =>
            {
                var functionValue = BuildChapterJumpFunctionValue(chapter, projectCode);
                return new StoryObjectChoice(
                    chapter.Code,
                    $"{chapter.Name} / {functionValue}",
                    functionValue);
            })
            .ToList();
    }

    public static List<StoryObjectChoice> CreateSegmentJumpChoices(int sectionCount)
    {
        return Enumerable.Range(1, Math.Max(1, sectionCount))
            .Select(section =>
            {
                var functionValue = BuildSegmentJumpFunctionValue(section);
                return new StoryObjectChoice(
                    section.ToString(CultureInfo.InvariantCulture),
                    $"{StorySectionDisplay.FormatName(section)} / {functionValue}",
                    functionValue);
            })
            .ToList();
    }

    public static string BuildFunctionChoiceDisplay(FunctionEntry function, string nextChoiceIndicator)
    {
        var indicator = IsChoiceFunctionTemplate(function)
            ? nextChoiceIndicator
            : function.Indicator;
        return $"{function.Name} / {indicator} / {function.Category}";
    }

    public static List<StoryObjectChoice> CreateFunctionBranchChoices(FunctionEntry function)
    {
        return function.Branches
            .Select(branch => new StoryObjectChoice(
                branch.Code,
                string.IsNullOrWhiteSpace(branch.Remark)
                    ? branch.Code
                    : $"{branch.Remark} / {branch.Code}",
                $"{function.Indicator}_{branch.Code}"))
            .ToList();
    }

    public static bool IsRegisteredFunctionValue(
        string functionValue,
        IReadOnlyList<FunctionEntry> functions)
    {
        var value = functionValue.Trim();
        foreach (var function in functions)
        {
            if (IsChoiceFunctionTemplate(function) && Regex.IsMatch(value, @"-\d{2}-Choice\d+$", RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (IsChapterJumpFunctionTemplate(function) && value.StartsWith("IntoChapter_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (IsSegmentJumpFunctionTemplate(function) && value.StartsWith("IntoSegment_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (function.Branches.Count == 0 && string.Equals(value, function.Indicator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (function.Branches.Any(branch => string.Equals(
                    value,
                    $"{function.Indicator}_{branch.Code}",
                    StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildFunctionCatalogSummary(FunctionEntry function)
    {
        return function.BranchMode == FunctionBranchMode.System
            ? function.Indicator
            : function.Branches.Count == 0
                ? $"{function.Indicator} · 无分支"
                : $"{function.Indicator} · {function.Branches.Count} 个分支";
    }

    public static List<FunctionEntry> OrderByUsageFrequency(IEnumerable<FunctionEntry> functions)
    {
        return functions
            .Select((function, index) => new { Function = function, Index = index })
            .OrderByDescending(item => GetUsageFrequency(item.Function))
            .ThenBy(item => item.Index)
            .Select(item => item.Function)
            .ToList();
    }

    private static int GetUsageFrequency(FunctionEntry function)
    {
        var indicator = function.Indicator.Trim();
        if (IsBgmFunctionTemplate(function) || indicator.StartsWith("BGM", StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }
        if (indicator.StartsWith("VFX", StringComparison.OrdinalIgnoreCase) ||
            indicator.Equals("CloseAllFX", StringComparison.OrdinalIgnoreCase))
        {
            return 27;
        }
        if (indicator.StartsWith("BGLerpMode", StringComparison.OrdinalIgnoreCase))
        {
            return 15;
        }
        if (indicator.StartsWith("Scene_", StringComparison.OrdinalIgnoreCase))
        {
            return 12;
        }
        if (indicator.StartsWith("TransAnim", StringComparison.OrdinalIgnoreCase))
        {
            return 11;
        }
        if (IsChoiceFunctionTemplate(function) || indicator.Contains("Choice", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        if (indicator.StartsWith("TitleShowMode", StringComparison.OrdinalIgnoreCase) ||
            indicator.StartsWith("ChapterShow", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        if (indicator.StartsWith("MedPlay", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        if (IsChapterJumpFunctionTemplate(function) ||
            IsSegmentJumpFunctionTemplate(function) ||
            indicator.StartsWith("Into", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    public static string BuildSuggestedChoiceIndicator(string prefix, IEnumerable<FunctionEntry> functions)
    {
        var existingCount = functions.Count(function =>
            string.Equals(function.Category, ChoiceFunctionCategory, StringComparison.OrdinalIgnoreCase) &&
            function.Indicator.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return $"{prefix}{existingCount + 1}";
    }

    public static string BuildChapterSectionCode(string chapterCode, int editorSection)
    {
        return $"{StoryCsvService.RemoveChapterSectionSuffix(chapterCode)}-{Math.Max(0, editorSection - 1):00}";
    }

    public static string BuildChapterSectionChoicePrefix(string chapterCode, int editorSection, string? projectCode)
    {
        var chapterSectionCode = RemoveProjectCodePrefix(BuildChapterSectionCode(chapterCode, editorSection), projectCode);
        return $"{chapterSectionCode}-Choice";
    }

    public static string BuildNextChoiceIndicator(
        string chapterCode,
        int editorSection,
        string? projectCode,
        IEnumerable<StoryRow> rows)
    {
        var prefix = BuildChapterSectionChoicePrefix(chapterCode, editorSection, projectCode);
        var chapterBaseCode = RemoveProjectCodePrefix(
            StoryCsvService.RemoveChapterSectionSuffix(chapterCode),
            projectCode);
        var maxChoiceIndex = rows
            .SelectMany(row => SplitFunctionValues(row.Get("Custom")))
            .Select(function => TryParseChapterChoiceFunctionIndex(function, chapterBaseCode))
            .Where(index => index > 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{maxChoiceIndex + 1}";
    }

    public static int TryParseChapterChoiceFunctionIndex(string functionValue, string chapterBaseCode)
    {
        var match = Regex.Match(
            functionValue.Trim(),
            $"^{Regex.Escape(chapterBaseCode)}-\\d{{2}}-Choice(?<index>\\d+)$",
            RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["index"].Value, out var index)
            ? index
            : 0;
    }

    public static int TryParseChoiceFunctionIndex(string functionValue, string prefix)
    {
        if (!functionValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(functionValue[prefix.Length..].Trim(), out var index) ? index : 0;
    }

    public static string FormatAssetStatus(
        string label,
        int rawIndex,
        IReadOnlyList<StoryAssetChoice> choices,
        Func<int, int, int?> resolveIndex)
    {
        var resolvedIndex = resolveIndex(rawIndex, choices.Count);
        return resolvedIndex is null
            ? $"{label}：无"
            : $"{label}：{choices[resolvedIndex.Value].Index}: {choices[resolvedIndex.Value].Name}";
    }

    public static IEnumerable<string> SplitFunctionValues(string functionValue)
    {
        return string.IsNullOrWhiteSpace(functionValue)
            ? []
            : functionValue.Split(
                ['/'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static bool ContainsFunction(string functionValue, string normalizedFunctionKey)
    {
        if (string.IsNullOrWhiteSpace(functionValue))
        {
            return false;
        }

        return EnumerateFunctionKeys(functionValue)
            .Any(key => string.Equals(key, normalizedFunctionKey, StringComparison.Ordinal));
    }

    public static bool ContainsExactFunction(string functionValue, string functionName)
    {
        return SplitFunctionValues(functionValue)
            .Any(function => string.Equals(function, functionName, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<string> EnumerateFunctionDisplayNames(string functionValue)
    {
        var parts = SplitFunctionValues(functionValue).ToArray();
        if (parts.Length == 0 && !string.IsNullOrWhiteSpace(functionValue))
        {
            yield return functionValue.Trim();
            yield break;
        }

        foreach (var part in parts)
        {
            if (TryParseBackgroundTransitionMode(part, out var transitionMode))
            {
                yield return GetBackgroundTransitionModeRemark(transitionMode);
                continue;
            }

            yield return part;
        }
    }

    public static bool TryParseBackgroundTransitionMode(string functionValue, out int mode)
    {
        var match = Regex.Match(functionValue.Trim(), @"^BGLerpMode_(?<mode>\d+)$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups["mode"].Value, out mode))
        {
            mode = Math.Clamp(mode, 0, 2);
            return true;
        }

        mode = 0;
        return false;
    }

    public static bool TryParseSoundEffectIndex(string functionValue, out int index)
    {
        index = 0;
        var match = Regex.Match(functionValue.Trim(), @"^Scene_(?<index>\d+)$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["index"].Value, out index);
    }

    public static string GetBackgroundTransitionModeDisplay(int mode)
    {
        return mode switch
        {
            1 => "1：正常黑屏转场",
            2 => "2：背景图渐变过渡",
            _ => "0：游戏入场黑屏"
        };
    }

    public static string GetBackgroundTransitionModeRemark(int mode)
    {
        return mode switch
        {
            1 => "正常黑屏转场",
            2 => "背景图渐变过渡",
            _ => "游戏入场黑屏"
        };
    }

    public static List<FunctionEntry> ReadFunctions(
        AssetLibraryInfo assetLibrary,
        JsonSerializerOptions jsonOptions)
    {
        var folderPath = GetFunctionFolderPath(assetLibrary);
        Directory.CreateDirectory(folderPath);
        var indexPath = GetFunctionIndexPath(assetLibrary);
        var index = ReadFunctionIndex(indexPath, jsonOptions);
        if (index?.Entries is not { Count: > 0 })
        {
            var defaults = CreateDefaultFunctions();
            WriteFunctions(assetLibrary, defaults, jsonOptions);
            return defaults;
        }

        var changed = index.SchemaVersion < 2;
        if (index.SchemaVersion < 2)
        {
            var backupPath = indexPath + ".before-v2.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(indexPath, backupPath);
            }
        }
        var defaultDescriptions = CreateDefaultFunctions()
            .ToDictionary(entry => entry.Id, entry => entry.Description, StringComparer.OrdinalIgnoreCase);
        var cleanedEntries = index.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Indicator))
            .Select(entry =>
            {
                var id = entry.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    changed = true;
                }

                var name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Indicator : entry.Name.Trim();
                var category = string.IsNullOrWhiteSpace(entry.Category) ? "自定义" : entry.Category.Trim();
                var description = string.IsNullOrWhiteSpace(entry.Description) &&
                    defaultDescriptions.TryGetValue(id, out var defaultDescription)
                        ? defaultDescription
                        : entry.Description?.Trim() ?? string.Empty;
                var choiceNotes = (entry.ChoiceNotes ?? [])
                    .Select(NormalizeFunctionChoiceNote)
                    .Where(note => !string.IsNullOrWhiteSpace(note))
                    .ToList();
                var originalChoiceNotes = entry.ChoiceNotes ?? [];
                if (id != entry.Id ||
                    name != entry.Name ||
                    category != entry.Category ||
                    description != entry.Description ||
                    !choiceNotes.SequenceEqual(originalChoiceNotes))
                {
                    changed = true;
                }

                var indicator = entry.Indicator.Trim();
                return entry with
                {
                    Id = id,
                    Name = name,
                    Indicator = indicator,
                    Category = category,
                    ChoiceNotes = choiceNotes,
                    Description = description
                };
            })
            .ToList();

        var normalized = NormalizeFunctionCatalogEntries(cleanedEntries);
        changed |= normalized.Count != cleanedEntries.Count ||
                   normalized.Where((entry, indexValue) => indexValue >= cleanedEntries.Count || entry != cleanedEntries[indexValue]).Any();

        changed |= EnsureBuiltInFunctionTemplates(normalized);

        if (changed || normalized.Count != index.Entries.Count)
        {
            WriteFunctions(assetLibrary, normalized, jsonOptions);
        }

        return normalized;
    }

    public static void WriteFunctions(
        AssetLibraryInfo assetLibrary,
        IReadOnlyList<FunctionEntry> functions,
        JsonSerializerOptions jsonOptions)
    {
        var folderPath = GetFunctionFolderPath(assetLibrary);
        Directory.CreateDirectory(folderPath);
        var index = new FunctionIndex
        {
            SchemaVersion = 2,
            Entries = functions.ToList()
        };
        File.WriteAllText(GetFunctionIndexPath(assetLibrary), JsonSerializer.Serialize(index, jsonOptions));
    }

    private static bool RemoveLegacyBgmFunctionTemplates(List<FunctionEntry> functions)
    {
        var removed = functions.RemoveAll(function =>
            string.Equals(function.Id, "default-bgm-start", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Id, "default-bgm-stop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, "BGM_Start", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, "BGM_Stop", StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    private static List<FunctionEntry> NormalizeFunctionCatalogEntries(IEnumerable<FunctionEntry> entries)
    {
        var result = new List<FunctionEntry>();
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Id, "default-custom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Indicator, "CustomFunction", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsChoiceFunctionTemplate(entry) || IsChapterJumpFunctionTemplate(entry) || IsSegmentJumpFunctionTemplate(entry))
            {
                result.Add(entry with { BranchMode = FunctionBranchMode.System, Branches = [] });
                continue;
            }

            var mainCode = entry.Indicator.Trim();
            var branches = (entry.Branches ?? [])
                .Where(branch => !string.IsNullOrWhiteSpace(branch.Code))
                .Select(branch => new FunctionBranch(branch.Code.Trim(), branch.Remark?.Trim() ?? string.Empty))
                .ToList();
            if (IsBgmFunctionTemplate(entry))
            {
                mainCode = "BGM";
                branches.AddRange([new FunctionBranch("Start", "开始"), new FunctionBranch("Stop", "停止")]);
            }
            else if (mainCode.EndsWith('_'))
            {
                mainCode = mainCode.TrimEnd('_');
            }
            else if (mainCode.Contains('_'))
            {
                var parts = mainCode.Split('_', 2, StringSplitOptions.TrimEntries);
                mainCode = parts[0];
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    branches.Add(new FunctionBranch(parts[1], string.Empty));
                }
            }

            var existingIndex = result.FindIndex(item =>
                string.Equals(item.Indicator, mainCode, StringComparison.OrdinalIgnoreCase));
            var normalizedBranches = branches
                .GroupBy(branch => branch.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.FirstOrDefault(branch => !string.IsNullOrWhiteSpace(branch.Remark)) ?? group.First())
                .OrderBy(branch => branch.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var normalized = entry with
            {
                Indicator = mainCode,
                BranchMode = entry.BranchMode == FunctionBranchMode.AssetIndex
                    ? FunctionBranchMode.AssetIndex
                    : normalizedBranches.Count > 0 ? FunctionBranchMode.Fixed : FunctionBranchMode.None,
                Branches = normalizedBranches
            };
            if (existingIndex < 0)
            {
                result.Add(normalized);
                continue;
            }

            var existing = result[existingIndex];
            var mergedBranches = existing.Branches.Concat(normalized.Branches)
                .GroupBy(branch => branch.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.FirstOrDefault(branch => !string.IsNullOrWhiteSpace(branch.Remark)) ?? group.First())
                .OrderBy(branch => branch.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result[existingIndex] = existing with
            {
                BranchMode = existing.BranchMode == FunctionBranchMode.AssetIndex
                    ? FunctionBranchMode.AssetIndex
                    : mergedBranches.Count > 0 ? FunctionBranchMode.Fixed : FunctionBranchMode.None,
                Branches = mergedBranches
            };
        }

        return result;
    }

    private static bool EnsureBuiltInFunctionTemplate(
        List<FunctionEntry> functions,
        FunctionEntry target,
        Func<FunctionEntry, bool> isTemplate,
        int desiredIndex)
    {
        var templateIndex = functions.FindIndex(function =>
            string.Equals(function.Id, target.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.Indicator, target.Indicator, StringComparison.OrdinalIgnoreCase) ||
            isTemplate(function));
        desiredIndex = Math.Clamp(desiredIndex, 0, functions.Count);
        if (templateIndex < 0)
        {
            functions.Insert(desiredIndex, target);
            return true;
        }

        var template = functions[templateIndex];
        var normalizedTemplate = template with
        {
            Id = target.Id,
            Name = string.IsNullOrWhiteSpace(template.Name) ? target.Name : template.Name.Trim(),
            Indicator = target.Indicator,
            Category = target.Category,
            ChoiceNotes = [],
            Description = string.IsNullOrWhiteSpace(template.Description)
                ? target.Description
                : template.Description.Trim(),
            BranchMode = target.BranchMode,
            Branches = template.Branches.Count > 0 ? template.Branches : target.Branches
        };
        var changed =
            !string.Equals(normalizedTemplate.Id, template.Id, StringComparison.Ordinal) ||
            !string.Equals(normalizedTemplate.Name, template.Name, StringComparison.Ordinal) ||
            !string.Equals(normalizedTemplate.Indicator, template.Indicator, StringComparison.Ordinal) ||
            !string.Equals(normalizedTemplate.Category, template.Category, StringComparison.Ordinal) ||
            !string.Equals(normalizedTemplate.Description, template.Description, StringComparison.Ordinal) ||
            normalizedTemplate.BranchMode != template.BranchMode ||
            !normalizedTemplate.Branches.SequenceEqual(template.Branches) ||
            (template.ChoiceNotes?.Count ?? 0) != 0 ||
            templateIndex != desiredIndex;

        functions.RemoveAt(templateIndex);
        if (templateIndex < desiredIndex)
        {
            desiredIndex--;
        }

        desiredIndex = Math.Clamp(desiredIndex, 0, functions.Count);
        functions.Insert(desiredIndex, normalizedTemplate);
        return changed;
    }

    private static string RemoveProjectCodePrefix(string chapterCode, string? projectCode)
    {
        if (string.IsNullOrWhiteSpace(projectCode))
        {
            return chapterCode.Trim();
        }

        var prefix = $"{projectCode}-";
        return chapterCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? chapterCode[prefix.Length..]
            : chapterCode.Trim();
    }

    private static string NormalizeFunctionKey(string functionValue)
    {
        return functionValue.Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private static IEnumerable<string> EnumerateFunctionKeys(string functionValue)
    {
        var parts = functionValue.Split(
            ['/', '\\', '|', ';', '；', ',', '，', '\r', '\n', '\t', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            yield return NormalizeFunctionKey(functionValue);
            yield break;
        }

        foreach (var part in parts)
        {
            yield return NormalizeFunctionKey(part);
        }
    }

    private static FunctionIndex? ReadFunctionIndex(string path, JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FunctionIndex>(File.ReadAllText(path), jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
