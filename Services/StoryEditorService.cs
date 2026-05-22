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

    public static List<StoryRow> CloneRows(IEnumerable<StoryRow> rows)
    {
        return rows.Select(row => row.Clone()).ToList();
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

    public void SynchronizeSections(IReadOnlyList<StoryRow> rows, Dictionary<string, int> rowSections)
    {
        var synchronized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousSection = 1;
        foreach (var row in rows)
        {
            var rowName = row.Get("Name");
            if (rowSections.TryGetValue(rowName, out var section))
            {
                previousSection = Math.Max(1, section);
            }

            synchronized[rowName] = previousSection;
        }

        rowSections.Clear();
        foreach (var pair in synchronized)
        {
            rowSections[pair.Key] = pair.Value;
        }
    }

    public void RenameRowsInOrder(IList<StoryRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Set("Name", StoryCsvService.CreateRowName(i));
        }
    }

    public StoryRowsEditResult MoveNextOrCreate(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        if (rows.Count == 0)
        {
            return new StoryRowsEditResult(0, false, []);
        }

        currentRowIndex = Math.Clamp(currentRowIndex, 0, rows.Count - 1);
        if (currentRowIndex < rows.Count - 1)
        {
            return new StoryRowsEditResult(currentRowIndex + 1, false, []);
        }

        var sections = GetSectionsInRowOrder(rows.ToList(), rowSections);
        var copiedSection = currentRowIndex < sections.Count ? sections[currentRowIndex] : 1;
        var nextRow = rows[currentRowIndex].Clone();
        nextRow.Set("Name", StoryCsvService.CreateRowName(rows.Count));
        nextRow.Set("Tesxt", string.Empty);
        nextRow.Set("Custom", string.Empty);
        rows.Add(nextRow);
        sections.Add(copiedSection);
        ApplySectionsInRowOrder(rows.ToList(), rowSections, sections);
        return new StoryRowsEditResult(currentRowIndex + 1, true, []);
    }

    public StoryRowsEditResult InsertAtCurrent(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        if (rows.Count == 0)
        {
            return new StoryRowsEditResult(0, false, []);
        }

        currentRowIndex = Math.Clamp(currentRowIndex, 0, rows.Count - 1);
        var sections = GetSectionsInRowOrder(rows.ToList(), rowSections);
        var copiedSection = currentRowIndex < sections.Count ? sections[currentRowIndex] : 1;
        var insertedRow = rows[currentRowIndex].Clone();
        insertedRow.Set("Tesxt", string.Empty);
        insertedRow.Set("Custom", string.Empty);
        rows.Insert(currentRowIndex, insertedRow);
        sections.Insert(currentRowIndex, copiedSection);
        RenameRowsInOrder(rows);
        ApplySectionsInRowOrder(rows.ToList(), rowSections, sections);
        return new StoryRowsEditResult(currentRowIndex, true, []);
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
