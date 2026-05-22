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
            new FunctionEntry("default-scene-sfx", "播放一次性特殊音效", "Scene_", "音频", []),
            new FunctionEntry("default-bglerp-mode", "背景切换模式", "BGLerpMode_", "背景", []),
            new FunctionEntry("default-vfx-on", "开启指定特效", "VFXON_", "特效", []),
            new FunctionEntry("default-vfx-off", "关闭指定特效", "VFXOFF_", "特效", []),
            new FunctionEntry("default-transanim", "播放动画序列", "TransAnim_", "动画", []),
            new FunctionEntry("default-transanim-end", "停止目前动画", "TransAnim_END", "动画", []),
            new FunctionEntry("default-medplay", "播放视频", "MedPlay_", "视频", []),
            CreateBgmFunctionTemplate(),
            new FunctionEntry("default-title-show", "大标题显示", "TitleShowMode", "标题", []),
            new FunctionEntry("default-close-all-fx", "关闭所有特效", "CloseAllFX", "特效", []),
            new FunctionEntry("default-custom", "纯自定义函数", "CustomFunction", "自定义", [])
        ];
    }

    public static FunctionEntry CreateChoiceFunctionTemplate()
    {
        return new FunctionEntry(ChoiceFunctionTemplateId, "创建触发选项", ChoiceFunctionTemplateIndicator, ChoiceFunctionCategory, []);
    }

    public static FunctionEntry CreateChapterJumpFunctionTemplate()
    {
        return new FunctionEntry(ChapterJumpFunctionTemplateId, "跳转章节", ChapterJumpFunctionTemplateIndicator, JumpFunctionCategory, []);
    }

    public static FunctionEntry CreateSegmentJumpFunctionTemplate()
    {
        return new FunctionEntry(SegmentJumpFunctionTemplateId, "跳转小节", SegmentJumpFunctionTemplateIndicator, JumpFunctionCategory, []);
    }

    public static FunctionEntry CreateBgmFunctionTemplate()
    {
        return new FunctionEntry(BgmFunctionTemplateId, "BGM", BgmFunctionTemplateIndicator, "音频", []);
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
            string.Equals(function.Indicator, BgmFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
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
                    $"第 {section} 小节 / {functionValue}",
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

    public static string BuildSuggestedChoiceIndicator(string prefix, IEnumerable<FunctionEntry> functions)
    {
        var existingCount = functions.Count(function =>
            string.Equals(function.Category, ChoiceFunctionCategory, StringComparison.OrdinalIgnoreCase) &&
            function.Indicator.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return $"{prefix}{existingCount + 1}";
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

        var changed = false;
        var normalized = index.Entries
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
                var choiceNotes = (entry.ChoiceNotes ?? [])
                    .Select(NormalizeFunctionChoiceNote)
                    .Where(note => !string.IsNullOrWhiteSpace(note))
                    .ToList();
                var originalChoiceNotes = entry.ChoiceNotes ?? [];
                if (id != entry.Id ||
                    name != entry.Name ||
                    category != entry.Category ||
                    !choiceNotes.SequenceEqual(originalChoiceNotes))
                {
                    changed = true;
                }

                return entry with
                {
                    Id = id,
                    Name = name,
                    Indicator = entry.Indicator.Trim(),
                    Category = category,
                    ChoiceNotes = choiceNotes
                };
            })
            .ToList();

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
            ChoiceNotes = []
        };
        var changed =
            !string.Equals(normalizedTemplate.Id, template.Id, StringComparison.Ordinal) ||
            !string.Equals(normalizedTemplate.Name, template.Name, StringComparison.Ordinal) ||
            !string.Equals(normalizedTemplate.Indicator, template.Indicator, StringComparison.Ordinal) ||
            !string.Equals(normalizedTemplate.Category, template.Category, StringComparison.Ordinal) ||
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
