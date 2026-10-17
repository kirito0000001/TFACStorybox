using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static GalExcleTools.Services.TextUtility;

namespace GalExcleTools.Services;

/// <summary>
/// 素材引用的**身份指纹**：记下"这一句的这个号，当时指向哪个文件"，
/// 下次打开章节时校验"号还在，图还是不是那一张"。
///
/// 【为什么需要它】剧情里的 BGindex / BGM / Scene 存的是**位置**（排序后文件列表的下标）。
/// 位置会因为重排 / 改名 / 中间插进一张素材而变，而号本身照样能解析成一张合法图片 ——
/// 所以错位**看不出来**：用户 2026-10-16 遇到的就是"换序之后有些背景乱了，已经没法复原"。
/// 有了指纹，这种事会在下次打开章节时被当场发现，并且能按记着的文件名修回正确的号。
///
/// 【为什么记"不含扩展名的文件名"】工具箱规范化时会把 jpg/webp 转成 png，
/// 基名不变、扩展名变了 —— 不带扩展名记，转换后指纹依然对得上。
///
/// 【边界】只覆盖三个全局素材列（BGindex / BGM / Scene），也就是会被拖拽重排的那三样；
/// 角色图层（Body*/Face*/…）和特殊音效的 Scene_n 还没纳入，见提交说明。
/// </summary>
internal static class StoryAssetIdentityService
{
    /// <summary>这三个列会被重排，纳入指纹。</summary>
    public static readonly string[] TrackedColumns = ["BGindex", "BGM", "Scene"];

    /// <summary>按当前列表把"这个号现在指向哪个文件"记进这一行的指纹里（解析不出来就清掉指纹）。</summary>
    public static void Capture(StoryRow row, string columnName, IReadOnlyList<string> orderedPaths)
    {
        var current = ResolveCurrentFileName(row, columnName, orderedPaths);
        if (current is null)
        {
            row.AssetIdentities.Remove(columnName);
            return;
        }

        row.AssetIdentities[columnName] = current;
    }

    /// <summary>这一行的这个列现在指向的文件基名；越界 / 空列表返回 null。</summary>
    public static string? ResolveCurrentFileName(StoryRow row, string columnName, IReadOnlyList<string> orderedPaths)
    {
        if (orderedPaths.Count == 0)
        {
            return null;
        }

        var index = ParseInt(row.Get(columnName));
        if (index < 0 || index >= orderedPaths.Count)
        {
            return null;
        }

        return Path.GetFileNameWithoutExtension(orderedPaths[index]);
    }

    /// <summary>记录里那个基名在**当前**列表里的下标；找不到（素材被删/改名）返回 null。</summary>
    public static int? FindIndexByFileName(IReadOnlyList<string> orderedPaths, string fileName)
    {
        for (var index = 0; index < orderedPaths.Count; index++)
        {
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(orderedPaths[index]),
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// 校验一批章节行：返回"指纹和现状对不上"的清单。
    /// <paramref name="orderedPathsByColumn"/> 是列名 → 当前素材列表（调用方解析好，避免每行重复列目录）。
    /// </summary>
    public static List<StoryAssetIdentityMismatch> Verify(
        IReadOnlyList<StoryRow> rows,
        IReadOnlyDictionary<string, IReadOnlyList<string>> orderedPathsByColumn)
    {
        var mismatches = new List<StoryAssetIdentityMismatch>();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            foreach (var columnName in TrackedColumns)
            {
                if (!row.AssetIdentities.TryGetValue(columnName, out var recorded) ||
                    string.IsNullOrWhiteSpace(recorded) ||
                    !orderedPathsByColumn.TryGetValue(columnName, out var orderedPaths) ||
                    orderedPaths.Count == 0)
                {
                    continue;
                }

                var currentName = ResolveCurrentFileName(row, columnName, orderedPaths);
                if (string.Equals(currentName, recorded, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                mismatches.Add(new StoryAssetIdentityMismatch(
                    rowIndex,
                    columnName,
                    recorded,
                    currentName,
                    FindIndexByFileName(orderedPaths, recorded)));
            }
        }

        return mismatches;
    }

    /// <summary>
    /// 按指纹把号修回来。找不到那张素材的（素材被删/改名）不动，返回 false ——
    /// 那种只能报给人看，不能瞎改。
    /// </summary>
    public static bool Repair(StoryRow row, StoryAssetIdentityMismatch mismatch)
    {
        if (mismatch.RepairedIndex is not int repairedIndex)
        {
            return false;
        }

        row.Set(mismatch.ColumnName, repairedIndex.ToString());
        return true;
    }

    /// <summary>把这一行所有列的指纹按现状重记一遍（导入 / 同步这类"索引本来就该变"的场合用）。</summary>
    public static void Recapture(StoryRow row, IReadOnlyDictionary<string, IReadOnlyList<string>> orderedPathsByColumn)
    {
        foreach (var columnName in TrackedColumns)
        {
            if (orderedPathsByColumn.TryGetValue(columnName, out var orderedPaths))
            {
                Capture(row, columnName, orderedPaths);
            }
        }
    }

    /// <summary>指纹里出现过的所有文件名（给"这张素材还在不在"这类检查用）。</summary>
    public static IEnumerable<string> EnumerateRecordedFileNames(IEnumerable<StoryRow> rows)
    {
        return rows
            .SelectMany(row => row.AssetIdentities.Values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 一处"指纹和现状对不上"。<paramref name="CurrentFileName"/> 为 null = 号越界（没指向任何素材）；
/// <paramref name="RepairedIndex"/> 为 null = 记录里那张素材现在不在列表里（删了或改名了）。
/// </summary>
internal sealed record StoryAssetIdentityMismatch(
    int RowIndex,
    string ColumnName,
    string RecordedFileName,
    string? CurrentFileName,
    int? RepairedIndex)
{
    public string Describe()
    {
        var current = CurrentFileName is null ? "越界（没指向任何素材）" : CurrentFileName;
        var repair = RepairedIndex is int index ? $"可修回第 {index} 张" : "记录里那张素材现在不在列表里";
        return $"第 {RowIndex + 1} 句 {ColumnName}：记录是「{RecordedFileName}」，现在是「{current}」（{repair}）";
    }
}
