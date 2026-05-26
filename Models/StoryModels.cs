using System;
using System.Collections.Generic;

namespace GalExcleTools;

internal sealed record StoryAssetChoice(int Index, string Name);

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

internal sealed record StoryEditorUndoState(
    List<StoryRow> Rows,
    Dictionary<string, int> Sections,
    int RowIndex,
    string Description,
    StoryChoiceNoteState? ChoiceNotes);

internal sealed record StoryRowsEditResult(int RowIndex, bool Changed, IReadOnlyList<string> RemovedChoiceValues);

internal sealed record StoryRowsLoadResult(
    List<StoryRow> Rows,
    Dictionary<string, int> Sections,
    int RemovedEmptySectionCount);

internal sealed record StorySectionImportResult(
    int ImportedCount,
    bool Changed,
    IReadOnlyList<StorySessionLogEntry> Logs);

internal sealed record StoryRowsPersistResult(int ActiveCsvCount);

internal sealed record StorySessionLogEntry(LogKind Kind, string Message);

internal sealed class StoryRow
{
    private readonly Dictionary<string, string> _cells = new(StringComparer.Ordinal);

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

        return clone;
    }
}

internal sealed class StorySectionState
{
    public Dictionary<string, int> Rows { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class StoryChoiceNoteState
{
    public Dictionary<string, List<string>> Choices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record StoryCsvCompatibility(
    bool IsCompatible,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<string> ExtraColumns);

internal sealed record StoryTableCsvEntry(string CsvPath, string AssetName, bool IsSectionCsv);

internal sealed record StorySectionCsvFile(string Path, int Section);

internal sealed record ProjectTextRow(
    string Id,
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
