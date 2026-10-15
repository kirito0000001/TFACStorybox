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

    /// <summary>当前小节能不能和**前面**那一节合并（当前句已经在第一个小节里就不能）。</summary>
    public bool CanMergeSectionWithPrevious(
        IReadOnlyList<StoryRow> rows,
        IReadOnlyDictionary<string, int> rowSections,
        int currentRowIndex)
    {
        return FindMergeBoundaryIndex(rows, rowSections, currentRowIndex, mergeWithPrevious: true) > 0;
    }

    /// <summary>当前小节能不能和**后面**那一节合并（当前句已经在最后一个小节里就不能）。</summary>
    public bool CanMergeSectionWithNext(
        IReadOnlyList<StoryRow> rows,
        IReadOnlyDictionary<string, int> rowSections,
        int currentRowIndex)
    {
        return FindMergeBoundaryIndex(rows, rowSections, currentRowIndex, mergeWithPrevious: false) > 0;
    }

    /// <summary>
    /// 把当前小节并进**前面**那一节。
    ///
    /// 合并和拆分是**互为逆操作**的一对（见 <see cref="SplitSectionAtCurrent"/>）：
    ///   - 拆分 = 在某一行**加**一条小节边界（从那一行起，所有小节号 +1）；
    ///   - 合并 = **去掉**一条小节边界（从那条边界起，所有小节号 -1）。
    /// 所以两个方向只差"去掉哪条边界"这一个判断，实现共用一套。
    ///
    /// 行本身**一条都不动** —— 小节号只是给连续行块编的序号，
    /// 去掉一条边界之后剩下的号天然还是连续的，不用再补。
    /// </summary>
    public bool MergeSectionWithPrevious(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        return MergeSectionBoundary(rows, rowSections, currentRowIndex, mergeWithPrevious: true);
    }

    /// <summary>把**后面**那一节并进当前小节。</summary>
    public bool MergeSectionWithNext(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex)
    {
        return MergeSectionBoundary(rows, rowSections, currentRowIndex, mergeWithPrevious: false);
    }

    private bool MergeSectionBoundary(
        IList<StoryRow> rows,
        Dictionary<string, int> rowSections,
        int currentRowIndex,
        bool mergeWithPrevious)
    {
        var readOnlyRows = rows as IReadOnlyList<StoryRow> ?? rows.ToList();
        var boundaryIndex = FindMergeBoundaryIndex(readOnlyRows, rowSections, currentRowIndex, mergeWithPrevious);
        if (boundaryIndex <= 0)
        {
            return false;
        }

        var sections = GetSectionsInRowOrder(readOnlyRows, rowSections);
        for (var index = boundaryIndex; index < sections.Count; index++)
        {
            sections[index] = Math.Max(1, sections[index] - 1);
        }

        ApplySectionsInRowOrder(readOnlyRows, rowSections, sections);
        return true;
    }

    /// <summary>
    /// 要**去掉**的那条小节边界落在第几行（-1 = 没有可合并的边界）。
    ///
    /// 向前合并去掉的是「当前小节的开头」，向后合并去掉的是「下一小节的开头」——
    /// 两条边界的共同点是"从这一行起，小节号都要减 1"。
    /// 当前小节本来就从第 0 行开始的那一档算作没有边界：前面没有东西可并。
    /// </summary>
    private int FindMergeBoundaryIndex(
        IReadOnlyList<StoryRow> rows,
        IReadOnlyDictionary<string, int> rowSections,
        int currentRowIndex,
        bool mergeWithPrevious)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        currentRowIndex = Math.Clamp(currentRowIndex, 0, rows.Count - 1);
        var sections = GetSectionsInRowOrder(rows, rowSections);
        var currentSection = sections[currentRowIndex];
        if (mergeWithPrevious)
        {
            // 往前走到小节号变化的那一行的下一行 = 当前小节的第一行。
            var firstRowIndex = currentRowIndex;
            while (firstRowIndex > 0 && sections[firstRowIndex - 1] == currentSection)
            {
                firstRowIndex--;
            }

            return firstRowIndex > 0 ? firstRowIndex : -1;
        }

        // 往后找到第一个小节号不同的行 = 下一小节的第一行（小节是连续块，中间不会夹别的）。
        for (var index = currentRowIndex + 1; index < sections.Count; index++)
        {
            if (sections[index] != currentSection)
            {
                return index;
            }
        }

        return -1;
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
