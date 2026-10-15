using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

internal sealed class StoryEditorService
{
    private readonly StoryCsvService _storyCsvService;

    public StoryEditorService(StoryCsvService storyCsvService)
    {
        _storyCsvService = storyCsvService;
    }

    public List<int> GetSectionsInRowOrder(IReadOnlyList<StoryRow> rows, IReadOnlyDictionary<string, int> rowSections)
    {
        var sections = new List<int>();
        var previousSection = 1;
        foreach (var row in rows)
        {
            var rowName = row.Get("Name");
            if (rowSections.TryGetValue(rowName, out var section))
            {
                previousSection = Math.Max(1, section);
            }

            sections.Add(previousSection);
        }

        return sections;
    }

    public void ApplySectionsInRowOrder(IReadOnlyList<StoryRow> rows, Dictionary<string, int> rowSections, IReadOnlyList<int> sections)
    {
        rowSections.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            var section = i < sections.Count ? sections[i] : 1;
            rowSections[rows[i].Get("Name")] = Math.Max(1, section);
        }
    }

    public void RenameRowsInOrder(IList<StoryRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Set("Name", StoryCsvService.CreateRowName(i));
        }
    }

    public StoryRowsEditResult MoveNext(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        if (rows.Count == 0)
        {
            return new StoryRowsEditResult(0, false, []);
        }

        currentRowIndex = Math.Clamp(currentRowIndex, 0, rows.Count - 1);
        return new StoryRowsEditResult(Math.Min(currentRowIndex + 1, rows.Count - 1), false, []);
    }

    public StoryRowsEditResult InsertAtCurrent(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        return InsertRelativeToCurrent(rows, rowSections, currentRowIndex, insertAfter: false);
    }

    public StoryRowsEditResult InsertAfterCurrent(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        return InsertRelativeToCurrent(rows, rowSections, currentRowIndex, insertAfter: true);
    }

    private StoryRowsEditResult InsertRelativeToCurrent(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex,
        bool insertAfter)
    {
        if (rows.Count == 0)
        {
            return new StoryRowsEditResult(0, false, []);
        }

        currentRowIndex = Math.Clamp(currentRowIndex, 0, rows.Count - 1);
        var sections = GetSectionsInRowOrder(rows.ToList(), rowSections);
        var copiedSection = currentRowIndex < sections.Count ? sections[currentRowIndex] : 1;
        var insertedRow = rows[currentRowIndex].Clone();
        insertedRow.StableId = StoryRow.CreateStableId();
        insertedRow.Set("Tesxt", string.Empty);
        insertedRow.Set("Custom", string.Empty);
        var insertIndex = currentRowIndex + (insertAfter ? 1 : 0);
        rows.Insert(insertIndex, insertedRow);
        sections.Insert(insertIndex, copiedSection);
        RenameRowsInOrder(rows);
        ApplySectionsInRowOrder(rows.ToList(), rowSections, sections);
        return new StoryRowsEditResult(insertIndex, true, []);
    }

    public bool NormalizeContiguousSections(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections)
    {
        if (rows.Count == 0)
        {
            rowSections.Clear();
            return false;
        }

        var rawSections = GetSectionsInRowOrder(rows.ToList(), rowSections);
        var normalizedSections = new List<int>(rawSections.Count);
        var nextSection = 1;
        var previousRawSection = rawSections[0];
        for (var index = 0; index < rawSections.Count; index++)
        {
            if (index > 0 && rawSections[index] != previousRawSection)
            {
                nextSection++;
                previousRawSection = rawSections[index];
            }

            normalizedSections.Add(nextSection);
        }

        var changed = !rawSections.SequenceEqual(normalizedSections);
        ApplySectionsInRowOrder(rows.ToList(), rowSections, normalizedSections);
        return changed;
    }

    public bool CanSplitSectionAtCurrent(
        IReadOnlyList<StoryRow> rows,
        IReadOnlyDictionary<string, int> rowSections,
        int currentRowIndex)
    {
        if (currentRowIndex <= 0 || currentRowIndex >= rows.Count)
        {
            return false;
        }

        var sections = GetSectionsInRowOrder(rows, rowSections);
        return sections[currentRowIndex - 1] == sections[currentRowIndex];
    }

    public bool SplitSectionAtCurrent(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        if (!CanSplitSectionAtCurrent(rows.ToList(), rowSections, currentRowIndex))
        {
            return false;
        }

        var sections = GetSectionsInRowOrder(rows.ToList(), rowSections);
        for (var index = currentRowIndex; index < sections.Count; index++)
        {
            sections[index] = sections[index] + 1;
        }

        ApplySectionsInRowOrder(rows.ToList(), rowSections, sections);
        return true;
    }

    public StoryRowsEditResult DeleteCurrent(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex,
        IReadOnlyList<string> removedChoiceValues)
    {
        if (rows.Count == 0)
        {
            return new StoryRowsEditResult(0, false, removedChoiceValues);
        }

        currentRowIndex = Math.Clamp(currentRowIndex, 0, rows.Count - 1);
        var sections = GetSectionsInRowOrder(rows.ToList(), rowSections);
        if (rows.Count == 1)
        {
            rows[0] = _storyCsvService.CreateDefaultRow();
            rowSections.Clear();
            ApplySectionsInRowOrder(rows.ToList(), rowSections, [1]);
            return new StoryRowsEditResult(0, true, removedChoiceValues);
        }

        rowSections.Remove(rows[currentRowIndex].Get("Name"));
        if (currentRowIndex < sections.Count)
        {
            sections.RemoveAt(currentRowIndex);
        }

        rows.RemoveAt(currentRowIndex);
        RenameRowsInOrder(rows);
        var nextIndex = Math.Min(currentRowIndex, rows.Count - 1);
        ApplySectionsInRowOrder(rows.ToList(), rowSections, sections);
        return new StoryRowsEditResult(nextIndex, true, removedChoiceValues);
    }
}
