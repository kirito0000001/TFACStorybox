using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools;

internal sealed record StoryAssetChoice(int Index, string Name, string? PreviewPath = null);

internal sealed record StoryObjectChoice(
    string Key,
    string DisplayName,
    object Value,
    IReadOnlyList<string>? PreviewPaths = null);

internal sealed record StoryCharacterSlotClipboard(
    string Character,
    string Body,
    string Face,
    string Adorn,
    string Vfx);

internal sealed record StoryAssetClipboard(string FieldName, string Value);

internal sealed record StoryRowsEditResult(int RowIndex, bool Changed, IReadOnlyList<string> RemovedChoiceValues);

internal sealed record StorySectionImportResult(
    int ImportedCount,
    bool Changed,
    IReadOnlyList<StorySessionLogEntry> Logs);

internal sealed record StorySessionLogEntry(LogKind Kind, string Message);

internal sealed class StoryRow
{
    private readonly Dictionary<string, string> _cells = new(StringComparer.Ordinal);

    public string StableId { get; set; } = CreateStableId();

    /// <summary>
    /// 素材引用的**身份指纹**：列名 → 记录当时那个号指向的文件名（含扩展名）。
    ///
    /// 【它解决什么】剧情里的 BGindex / BGM / Scene 存的是**位置**（排序后列表的下标），
    /// 位置会因为"重排 / 改名 / 中间插进一张图"而变，而号本身照样能解析成一张合法图片 ——
    /// 也就是错位**看不出来**（用户 2026-10-16 报的"换序后有些背景乱了、已经没法复原"）。
    /// 记下"这一句当时要的是哪个文件"，下次打开章节就能发现"号还在、图换了"，
    /// 并按记着的名字把号修回去。
    ///
    /// 只存在章节 JSON 里（CSV 不写、游戏侧看不到），所以**不碰引擎那边的约定**。
    /// </summary>
    public Dictionary<string, string> AssetIdentities { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string CreateStableId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public string Get(string column)
    {
        return _cells.TryGetValue(column, out var value) ? value : string.Empty;
    }

    public void Set(string column, string value)
    {
        _cells[column] = value;
    }

    public StoryRow Clone()
    {
        var clone = new StoryRow();
        foreach (var pair in _cells)
        {
            clone.Set(pair.Key, pair.Value);
        }

        foreach (var pair in AssetIdentities)
        {
            clone.AssetIdentities[pair.Key] = pair.Value;
        }

        clone.StableId = StableId;

        return clone;
    }
}

internal sealed class StoryChoiceNoteState
{
    public Dictionary<string, List<string>> Choices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public StoryChoiceNoteState Clone()
    {
        var clone = new StoryChoiceNoteState();
        foreach (var pair in Choices)
        {
            clone.Choices[pair.Key] = pair.Value.ToList();
        }

        return clone;
    }
}

internal sealed record StoryChoiceRoute(
    string ChoiceFunction,
    int OptionIndex,
    string OptionKey,
    string TargetSectionFirstRowId,
    bool ManagedByToolbox = true);

internal sealed record StoryChoiceTargetOption(
    string TargetSectionFirstRowId,
    int Section,
    string DisplayName,
    string FirstSentencePreview);

internal sealed record StoryChoiceEditorOption(
    string Text,
    string TargetSectionFirstRowId);

internal sealed record StoryChoiceEditorResult(
    IReadOnlyList<StoryChoiceEditorOption> Options);

internal sealed record StoryBranchInspectorItem(
    string OptionLabel,
    string Content,
    string SourceText);

internal sealed record StoryCsvCompatibility(
    bool IsCompatible,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<string> ExtraColumns);

internal sealed record StoryTableCsvEntry(string CsvPath, string AssetName, bool IsSectionCsv);

internal sealed record StorySectionCsvFile(string Path, int Section);

internal sealed record ProjectTextRow(
    string Id,
    string StableRowId,
    string ChapterCode,
    string ChapterName,
    int Section,
    string RowName,
    string Text);

internal sealed class ProjectVoiceMapState
{
    public Dictionary<string, string> Voices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ProjectLocalizationState
{
    public Dictionary<string, Dictionary<string, string>> Languages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal enum ProjectTextToolMode
{
    Voice,
    Localization
}
