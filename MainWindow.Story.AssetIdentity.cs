using System;
using System.Collections.Generic;
using System.Linq;
using GalExcleTools.Services;
using Microsoft.UI.Xaml.Controls;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools;

/// <summary>
/// 素材引用的身份指纹：采集 / 校验 / 按记录修回。
///
/// 剧情里的 BGindex / BGM / Scene 存的是**位置**，重排或中间插一张素材就会让"号还在、
/// 图换了"，而且看不出来（用户 2026-10-16：「换序之后有些背景乱了，已经没法复原」）。
/// 这里给每一行记一份"当时那个号指向哪个文件"，打开章节时对一遍：
///   · 对得上 → 什么都不做；
///   · 对不上但记录里那张素材还在 → **按记录把号修回来**（这才是用户原本要的那张图）；
///   · 对不上且记录里那张素材已经不在列表里（删了/改名了）→ 只报不改。
/// 指纹只写进章节 JSON，不写 CSV、不进虚幻，所以**引擎那边完全不受影响**。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>三个受管列的当前素材列表（一次解析，给一批行共用）。</summary>
    private Dictionary<string, IReadOnlyList<string>> GetStoryTrackedAssetPaths()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (_currentStoryAssetLibrary is null)
        {
            return result;
        }

        result["BGindex"] = ImageSetService.GetFilePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary));
        result["BGM"] = AudioAssetService.GetFilePaths(GetMusicFolderPath(_currentStoryAssetLibrary));
        result["Scene"] = AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(_currentStoryAssetLibrary));
        return result;
    }

    /// <summary>
    /// 用户刚改过这几列（选素材 / 粘贴）→ 把指纹重记成现状。
    /// 只在"用户自己挑的"路径上调：导入、同步那些"索引本来就该变"的场合各自有各自的处理。
    /// </summary>
    private void CaptureStoryAssetIdentities(int startIndex, int endIndex)
    {
        if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
        {
            return;
        }

        var paths = GetStoryTrackedAssetPaths();
        var from = Math.Max(0, Math.Min(startIndex, endIndex));
        var to = Math.Min(_storyRows.Count - 1, Math.Max(startIndex, endIndex));
        for (var index = from; index <= to; index++)
        {
            StoryAssetIdentityService.Recapture(_storyRows[index], paths);
        }
    }

    /// <summary>
    /// 打开章节后校验一次（见类注释）。第一次打开旧章节时指纹是空的 ——
    /// 那种情况以**现状**为基线记一遍、不做判断，这样不会把"这次之前就已经错的"
    /// 当成刚发生的问题；从下一次开始才真的能发现变化。
    /// </summary>
    private void VerifyAndRepairStoryAssetIdentities()
    {
        if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
        {
            return;
        }

        var paths = GetStoryTrackedAssetPaths();
        if (paths.Count == 0)
        {
            return;
        }

        var bootstrapped = 0;
        foreach (var row in _storyRows)
        {
            foreach (var columnName in StoryAssetIdentityService.TrackedColumns)
            {
                if (row.AssetIdentities.ContainsKey(columnName) ||
                    !paths.TryGetValue(columnName, out var orderedPaths))
                {
                    continue;
                }

                StoryAssetIdentityService.Capture(row, columnName, orderedPaths);
                bootstrapped++;
            }
        }

        var mismatches = StoryAssetIdentityService.Verify(_storyRows, paths);
        if (mismatches.Count == 0)
        {
            if (bootstrapped > 0)
            {
                // 首次建立基线要落盘，否则下次打开又当"第一次"。
                QueueCurrentStoryDocumentSave();
            }

            return;
        }

        var repaired = 0;
        var unresolved = new List<StoryAssetIdentityMismatch>();
        foreach (var mismatch in mismatches)
        {
            if (StoryAssetIdentityService.Repair(_storyRows[mismatch.RowIndex], mismatch))
            {
                repaired++;
            }
            else
            {
                unresolved.Add(mismatch);
            }
        }

        var details = string.Join("；", mismatches.Take(6).Select(item => item.Describe()));
        AppendLog(
            LogKind.Warning,
            $"素材引用校验：{mismatches.Count} 处与记录不符（已按记录修回 {repaired} 处，另有 {unresolved.Count} 处记录里的素材已不在列表里）。{details}");
        ShowStoryStatus(
            InfoBarSeverity.Warning,
            repaired > 0 ? "已按记录修回素材引用" : "素材引用与记录不符",
            repaired > 0
                ? $"检测到 {mismatches.Count} 处素材引用被改动过（多半是素材重排 / 改名），已按记录修回 {repaired} 处"
                    + (unresolved.Count > 0 ? $"，另有 {unresolved.Count} 处记录里的素材已不在列表里，需要你确认（详见日志）。" : "。")
                : $"{mismatches.Count} 处素材引用与记录不符，但记录里的素材已经不在列表里，没法自动修（详见日志）。");

        QueueCurrentStoryDocumentSave();
        _ = RefreshStoryPreviewAsync();
    }
}
