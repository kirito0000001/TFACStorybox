using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools;

internal sealed class StoryDocument
{
    public List<StoryRow> Rows { get; } = [];

    public Dictionary<string, int> RowSections { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StoryChoiceNoteState ChoiceNotes { get; set; } = new();

    public static StoryDocument CreateEmpty()
    {
        var document = new StoryDocument();
        var row = CreateEmptyRow();
        document.Rows.Add(row);
        document.RowSections["1"] = 1;
        return document;
    }

    public static StoryRow CreateEmptyRow(int index = 0)
    {
        var row = new StoryRow();
        foreach (var column in StorySchema.Columns)
        {
            row.Set(column, StorySchema.NumericColumns.Contains(column) ? "0" : string.Empty);
        }

        row.Set("Name", (index + 1).ToString());
        return row;
    }

    public void ReplaceWith(StoryDocument source)
    {
        Rows.Clear();
        Rows.AddRange(source.Rows.Select(row => row.Clone()));
        RowSections.Clear();
        foreach (var pair in source.RowSections)
        {
            RowSections[pair.Key] = pair.Value;
        }

        ChoiceNotes = source.ChoiceNotes.Clone();
    }

    public StoryDocumentSnapshot CaptureSnapshot(int currentRowIndex)
    {
        var rowsById = Rows.ToDictionary(row => row.StableId, row => row.Clone(), StringComparer.OrdinalIgnoreCase);
        var sectionsById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            sectionsById[row.StableId] = RowSections.TryGetValue(row.Get("Name"), out var section)
                ? Math.Max(1, section)
                : 1;
        }

        var currentRowId = Rows.Count == 0
            ? null
            : Rows[Math.Clamp(currentRowIndex, 0, Rows.Count - 1)].StableId;
        return new StoryDocumentSnapshot(
            Rows.Select(row => row.StableId).ToList(),
            rowsById,
            sectionsById,
            currentRowId,
            ChoiceNotes.Clone());
    }

    public StoryChangeApplication ApplyChange(
        StoryChangeSet change,
        bool useBefore,
        int currentRowIndex)
    {
        var sectionsById = CaptureSectionsByStableId();
        var rowsById = Rows.ToDictionary(row => row.StableId, row => row.Clone(), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in change.RowChanges)
        {
            var target = useBefore ? pair.Value.Before : pair.Value.After;
            if (target is null)
            {
                rowsById.Remove(pair.Key);
            }
            else
            {
                rowsById[pair.Key] = target.Clone();
            }
        }

        var currentOrder = Rows.Select(row => row.StableId).ToList();
        var targetOrder = useBefore ? change.BeforeOrder : change.AfterOrder;
        targetOrder ??= currentOrder;
        Rows.Clear();
        foreach (var stableId in targetOrder)
        {
            if (rowsById.TryGetValue(stableId, out var row))
            {
                Rows.Add(row);
            }
        }

        foreach (var remaining in rowsById.Values.Where(row => Rows.All(item => !string.Equals(item.StableId, row.StableId, StringComparison.OrdinalIgnoreCase))))
        {
            Rows.Add(remaining);
        }

        foreach (var pair in change.SectionChanges)
        {
            var target = useBefore ? pair.Value.Before : pair.Value.After;
            if (target is null)
            {
                sectionsById.Remove(pair.Key);
            }
            else
            {
                sectionsById[pair.Key] = Math.Max(1, target.Value);
            }
        }

        RowSections.Clear();
        for (var index = 0; index < Rows.Count; index++)
        {
            Rows[index].Set("Name", (index + 1).ToString());
            RowSections[Rows[index].Get("Name")] = sectionsById.TryGetValue(Rows[index].StableId, out var section)
                ? Math.Max(1, section)
                : 1;
        }

        var migratedNotes = ChoiceNotes.Clone();
        foreach (var pair in change.ChoiceNoteChanges)
        {
            var target = useBefore ? pair.Value.Before : pair.Value.After;
            if (target is null)
            {
                migratedNotes.Choices.Remove(pair.Key);
            }
            else
            {
                migratedNotes.Choices[pair.Key] = target.ToList();
            }
        }

        var targetCurrentRowId = useBefore ? change.BeforeCurrentRowId : change.AfterCurrentRowId;
        var rowIndex = targetCurrentRowId is null
            ? Math.Clamp(currentRowIndex, 0, Math.Max(0, Rows.Count - 1))
            : Rows.FindIndex(row => string.Equals(row.StableId, targetCurrentRowId, StringComparison.OrdinalIgnoreCase));
        if (rowIndex < 0)
        {
            rowIndex = Math.Clamp(currentRowIndex, 0, Math.Max(0, Rows.Count - 1));
        }

        ChoiceNotes = migratedNotes;

        return new StoryChangeApplication(rowIndex, change.Description);
    }

    public static StoryChangeSet CreateChangeSet(
        string description,
        StoryDocumentSnapshot before,
        StoryDocumentSnapshot after)
    {
        var rowChanges = new Dictionary<string, StoryRowDelta>(StringComparer.OrdinalIgnoreCase);
        foreach (var stableId in before.RowsById.Keys.Union(after.RowsById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            before.RowsById.TryGetValue(stableId, out var beforeRow);
            after.RowsById.TryGetValue(stableId, out var afterRow);
            if (beforeRow is null || afterRow is null || !RowsHaveSameContent(beforeRow, afterRow))
            {
                rowChanges[stableId] = new StoryRowDelta(beforeRow?.Clone(), afterRow?.Clone());
            }
        }

        var orderChanged = !before.Order.SequenceEqual(after.Order, StringComparer.OrdinalIgnoreCase);
        var sectionChanges = new Dictionary<string, StorySectionDelta>(StringComparer.OrdinalIgnoreCase);
        foreach (var stableId in before.SectionsById.Keys.Union(after.SectionsById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var hasBefore = before.SectionsById.TryGetValue(stableId, out var beforeSection);
            var hasAfter = after.SectionsById.TryGetValue(stableId, out var afterSection);
            if (!hasBefore || !hasAfter || beforeSection != afterSection)
            {
                sectionChanges[stableId] = new StorySectionDelta(
                    hasBefore ? beforeSection : null,
                    hasAfter ? afterSection : null);
            }
        }

        var noteChanges = new Dictionary<string, StoryChoiceNoteDelta>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in before.ChoiceNotes.Choices.Keys.Union(after.ChoiceNotes.Choices.Keys, StringComparer.OrdinalIgnoreCase))
        {
            before.ChoiceNotes.Choices.TryGetValue(key, out var beforeNotes);
            after.ChoiceNotes.Choices.TryGetValue(key, out var afterNotes);
            if (beforeNotes is null || afterNotes is null || !beforeNotes.SequenceEqual(afterNotes, StringComparer.Ordinal))
            {
                noteChanges[key] = new StoryChoiceNoteDelta(beforeNotes?.ToList(), afterNotes?.ToList());
            }
        }

        return new StoryChangeSet(
            description,
            rowChanges,
            orderChanged ? before.Order.ToList() : null,
            orderChanged ? after.Order.ToList() : null,
            sectionChanges,
            noteChanges,
            before.CurrentRowId,
            after.CurrentRowId);
    }

    public static StoryChangeSet CreateRowChangeSet(
        string description,
        StoryRow before,
        StoryRow after)
    {
        var changes = new Dictionary<string, StoryRowDelta>(StringComparer.OrdinalIgnoreCase);
        if (!RowsHaveSameContent(before, after))
        {
            changes[before.StableId] = new StoryRowDelta(before.Clone(), after.Clone());
        }

        return new StoryChangeSet(
            description,
            changes,
            null,
            null,
            new Dictionary<string, StorySectionDelta>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, StoryChoiceNoteDelta>(StringComparer.OrdinalIgnoreCase),
            before.StableId,
            after.StableId);
    }

    private Dictionary<string, int> CaptureSectionsByStableId()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            result[row.StableId] = RowSections.TryGetValue(row.Get("Name"), out var section)
                ? Math.Max(1, section)
                : 1;
        }

        return result;
    }

    private static bool RowsHaveSameContent(StoryRow first, StoryRow second)
    {
        return StorySchema.Columns
            .Where(column => column != "Name")
            .All(column => string.Equals(first.Get(column), second.Get(column), StringComparison.Ordinal));
    }

}

internal sealed record StoryDocumentSnapshot(
    List<string> Order,
    Dictionary<string, StoryRow> RowsById,
    Dictionary<string, int> SectionsById,
    string? CurrentRowId,
    StoryChoiceNoteState ChoiceNotes);

internal sealed record StoryRowDelta(StoryRow? Before, StoryRow? After);

internal sealed record StorySectionDelta(int? Before, int? After);

internal sealed record StoryChoiceNoteDelta(List<string>? Before, List<string>? After);

internal sealed record StoryChangeSet(
    string Description,
    Dictionary<string, StoryRowDelta> RowChanges,
    List<string>? BeforeOrder,
    List<string>? AfterOrder,
    Dictionary<string, StorySectionDelta> SectionChanges,
    Dictionary<string, StoryChoiceNoteDelta> ChoiceNoteChanges,
    string? BeforeCurrentRowId,
    string? AfterCurrentRowId)
{
    public bool IsEmpty =>
        RowChanges.Count == 0 &&
        BeforeOrder is null &&
        SectionChanges.Count == 0 &&
        ChoiceNoteChanges.Count == 0;

    public int ChangedRowCount => RowChanges.Count;

    public bool HasOrderChange => BeforeOrder is not null;
}

internal sealed record StoryChangeApplication(
    int RowIndex,
    string Description);
