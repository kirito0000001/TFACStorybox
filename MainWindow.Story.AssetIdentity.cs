using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        return _currentStoryAssetLibrary is null
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            : GetStoryTrackedAssetPaths(_currentStoryAssetLibrary);
    }

    /// <summary>同上，但素材库由调用方给（章节右键「修复」那条路修的不一定是当前打开的章节）。</summary>
    private static Dictionary<string, IReadOnlyList<string>> GetStoryTrackedAssetPaths(AssetLibraryInfo assetLibrary)
    {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["BGindex"] = ImageSetService.GetFilePaths(GetBackgroundFolderPath(assetLibrary)),
            ["BGM"] = AudioAssetService.GetFilePaths(GetMusicFolderPath(assetLibrary)),
            ["Scene"] = AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(assetLibrary))
        };
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

        // ⚠️ 这里**不自动改任何东西**：数据上分不清"记录里的名字不在了"是改备注还是被删了，
        // 所以只报出来，让用户在「修复」窗口里决定（用户 2026-10-17：
        // 「如果有记录不同的应该给个窗口可以处理，而不是只报一次」）。
        var repairable = mismatches.Count(item => item.RepairedIndex is not null);
        var details = string.Join("；", mismatches.Take(6).Select(item => item.Describe()));
        AppendLog(
            LogKind.Warning,
            $"素材引用校验：{mismatches.Count} 处与记录不符（能按记录修回 {repairable} 处，"
                + $"另有 {mismatches.Count - repairable} 处记录里的名字已不在列表里；均未改动）。"
                + $"打开这一章后，右键章节卡「修复」可以逐条核对处理。{details}");
        ShowStoryStatus(
            InfoBarSeverity.Warning,
            "素材引用与记录不符",
            $"{mismatches.Count} 处对不上（能按记录修回 {repairable} 处）—— 右键章节卡「修复」里可以核对处理。");
    }

    /// <summary>
    /// 素材引用校验的**处理窗口**（入口：章节卡右键「修复」）。
    ///
    /// 列出"记录 vs 现状"不符的句子，让用户二选一：
    ///   · 按记录修回：用记录里的文件名在当前素材列表里重新定位到正确的号（能修几处修几处）；
    ///   · 以现状为准：接受现在的号，把记录改成现状（之后不再提示这些）。
    /// 关掉窗口 = 什么都不动 —— 问题还在，下次打开章节还会提示（这是有意的：
    /// 悄悄重记一笔就等于把"这里可能被动过"这件事抹掉了，用户明确不要那种做法）。
    /// </summary>
    private async Task<int> RepairStoryAssetIdentitiesAsync(ChapterInfo chapter, AssetLibraryInfo assetLibrary)
    {
        var isOpenChapter = _currentStoryChapter is not null &&
            string.Equals(_currentStoryChapter.Path, chapter.Path, StringComparison.OrdinalIgnoreCase);
        StoryDocument document;
        if (isOpenChapter)
        {
            document = _storyEditorViewModel.Document;
        }
        else if (_storyDocumentRepository.Exists(chapter))
        {
            document = _storyDocumentRepository.Load(chapter);
        }
        else
        {
            return 0;
        }

        var paths = GetStoryTrackedAssetPaths(assetLibrary);
        var mismatches = StoryAssetIdentityService.Verify(document.Rows, paths);
        if (mismatches.Count == 0)
        {
            ShowChapterStatus(
                InfoBarSeverity.Success,
                "素材引用正常",
                $"{chapter.Name}（{chapter.Code}）没有发现素材引用与记录不符的句子。");
            return 0;
        }

        var repairable = mismatches.Count(item => item.RepairedIndex is not null);
        var panel = new StackPanel { Spacing = 6, Width = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = $"共 {mismatches.Count} 处与记录不符：{repairable} 处能按记录修回，"
                + $"{mismatches.Count - repairable} 处记录里的素材已经不在列表里（删了或改了名，只能以现状为准）。",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var item in mismatches.Take(200))
        {
            panel.Children.Add(new TextBlock
            {
                Text = item.Describe(),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            });
        }

        var choice = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            "素材引用校验",
            new ScrollViewer { Content = panel, MaxHeight = 360 },
            $"按记录修回（{repairable} 处）",
            CloseButtonText: string.Empty,
            SecondaryButtonText: "以现状为准"));
        if (choice is DialogResultKind.None or DialogResultKind.Cancel)
        {
            ShowChapterStatus(
                InfoBarSeverity.Warning,
                "素材引用未处理",
                $"还有 {mismatches.Count} 处对不上，未做任何改动。");
            return 0;
        }

        if (isOpenChapter)
        {
            // 当前打开的章节：走撤回栈，改错了能 Ctrl+Z 退回来。
            CaptureStoryUndoState(choice == DialogResultKind.Primary ? "按记录修回素材引用" : "素材引用以现状为准");
        }

        if (choice == DialogResultKind.Primary)
        {
            var repaired = 0;
            foreach (var item in mismatches)
            {
                if (StoryAssetIdentityService.Repair(document.Rows[item.RowIndex], item))
                {
                    repaired++;
                }
            }

            CommitStoryAssetIdentityChange(chapter, document, isOpenChapter);
            ShowChapterStatus(
                repaired > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                repaired > 0 ? "已按记录修回素材引用" : "没有可修回的项",
                $"修回 {repaired} 处；另有 {mismatches.Count - repaired} 处记录里的素材已经不在列表里，未改动。");
            AppendLog(LogKind.User, $"素材引用校验：按记录修回 {repaired} 处（共 {mismatches.Count} 处不符），章节 {chapter.Code}。");
            return repaired;
        }

        // Secondary = 以现状为准：把记录改成现状（承认现在这样）。
        foreach (var item in mismatches)
        {
            StoryAssetIdentityService.Capture(document.Rows[item.RowIndex], item.ColumnName, paths[item.ColumnName]);
        }

        CommitStoryAssetIdentityChange(chapter, document, isOpenChapter);
        ShowChapterStatus(
            InfoBarSeverity.Success,
            "素材引用已按现状重记",
            $"重记 {mismatches.Count} 处 —— 之后不再提示这些位置。");
        AppendLog(LogKind.User, $"素材引用校验：以现状为准重记 {mismatches.Count} 处，章节 {chapter.Code}。");
        return 0;
    }

    /// <summary>处理完把改动落下去：打开的章节走自动保存 + 刷界面，没打开的章节直接写盘。</summary>
    private void CommitStoryAssetIdentityChange(ChapterInfo chapter, StoryDocument document, bool isOpenChapter)
    {
        if (isOpenChapter)
        {
            QueueCurrentStoryDocumentSave();
            UpdateStoryToolbarCurrentInfo();
            _ = RefreshStoryPreviewAsync();
            return;
        }

        _storyDocumentRepository.Save(chapter, document);
    }
}
