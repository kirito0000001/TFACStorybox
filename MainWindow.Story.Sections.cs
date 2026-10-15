using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using GalExcleTools.Views;
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private void StorySectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingStoryRow || _isUpdatingStorySectionOptions || _currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var selectedSection = GetSelectedStorySection();
            if (selectedSection == GetCurrentStorySection())
            {
                return;
            }

            var targetIndex = Enumerable.Range(0, _storyRows.Count)
                .FirstOrDefault(index => GetStorySectionAtRowIndex(index) == selectedSection, -1);
            if (targetIndex >= 0)
            {
                NavigateToStoryRow(targetIndex, rebuildPersistentState: true);
            }
        }

        private void EnsureStorySectionOptions(int requiredSection)
        {
            var maxSection = Math.Max(1, Math.Max(requiredSection, _storyRowSections.Values.DefaultIfEmpty(1).Max()));
            if (StorySectionComboBox.Items.Count == maxSection)
            {
                return;
            }

            _isUpdatingStorySectionOptions = true;
            try
            {
                StorySectionComboBox.Items.Clear();
                for (var i = 1; i <= maxSection; i++)
                {
                    StorySectionComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = StorySectionDisplay.FormatName(i),
                        Tag = i
                    });
                }
            }
            finally
            {
                _isUpdatingStorySectionOptions = false;
            }
        }

        private void AddStorySection()
        {
            if (!CanEditStoryData() || _currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            SaveCurrentStoryRow();
            if (!_storyEditorService.CanSplitSectionAtCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex))
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "无法拆分小节", "当前句已经是小节第一句，或章节中没有可拆分的前置句子。");
                return;
            }

            CaptureStoryUndoState("从当前句拆分新小节");
            _storyEditorService.SplitSectionAtCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex);
            var nextSection = GetCurrentStorySection();
            EnsureStorySectionOptions(_storyRowSections.Values.DefaultIfEmpty(1).Max());
            SelectStorySection(nextSection);
            // （这里原来还要重算小节那边的「自定义跳转逻辑」开关，2026-10-15 随那个开关一起删了。）
            NormalizeStoryChoiceFunctionPrefixes(out var migratedChoiceNotes);
            QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            UpdateStoryRowIndexInput();
            UpdateStoryEditorHeader();
            UpdateStoryToolbarCurrentInfo();
            _storyEditorViewModel.RefreshCommandStates();
            ShowStoryStatus(InfoBarSeverity.Success, "已拆分小节", $"当前句现在是{StorySectionDisplay.FormatName(nextSection)}的第一句。");
        }

        private void MergePreviousStorySection()
        {
            MergeStorySection(mergeWithPrevious: true);
        }

        private void MergeNextStorySection()
        {
            MergeStorySection(mergeWithPrevious: false);
        }

        /// <summary>
        /// 合并小节：向前 = 并进前面那一节，向后 = 把后面那一节并进来。
        ///
        /// 和「创建新小节」是**互为逆操作**的一对，所以这条链照着它抄：
        /// 拆分是"在当前句这里**加**一条小节边界"，合并是"**去掉**一条小节边界"。
        ///
        /// 【两条容易漏掉的连带影响】
        /// 1. 小节号变了 → 嵌在小节号里的 Choice 函数名（<c>WHK-M6-06-Choice3</c>）
        ///    必须跟着迁移，不然那些选项会指到别的小节上。拆分走的是同一个迁移函数。
        /// 2. 「自定义跳转逻辑」（这一节保留手工 AllRoad）的标记：两节里**只要有一节标着**，
        ///    合并后那一节就继续标着 —— 否则合并会把手工写的那套 AllRoad
        ///    变成"下次同步自动重建"，而且这一步不会有任何报错。
        ///    注意这个标记现在只在**选项管理器**里能改（小节那边的开关 2026-10-15 封存了），
        ///    但这里的行为必须留着：旧文档里可能就带着标记。
        ///
        /// 行一条都不动，所以小节锚点（选项目标 / 自定义标记存的都是行 StableId）不受影响。
        /// </summary>
        private void MergeStorySection(bool mergeWithPrevious)
        {
            if (!CanEditStoryData() || _currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            SaveCurrentStoryRow();
            var currentSection = GetCurrentStorySection();
            var canMerge = mergeWithPrevious
                ? _storyEditorService.CanMergeSectionWithPrevious(_storyRows, _storyRowSections, _currentStoryRowIndex)
                : _storyEditorService.CanMergeSectionWithNext(_storyRows, _storyRowSections, _currentStoryRowIndex);
            if (!canMerge)
            {
                ShowStoryStatus(
                    InfoBarSeverity.Informational,
                    "无法合并小节",
                    mergeWithPrevious
                        ? "当前句已经在第一个小节里，前面没有可合并的小节。"
                        : "当前句已经在最后一个小节里，后面没有可合并的小节。");
                return;
            }

            // 前后两节：合并后留下的是前面那一节的号（小节号整体往前收一位）。
            var precedingSection = mergeWithPrevious ? currentSection - 1 : currentSection;
            var followingSection = mergeWithPrevious ? currentSection : currentSection + 1;
            var keepCustomBranchLogic =
                StoryBranchService.IsCustomBranchSection(_storyEditorViewModel.Document, precedingSection) ||
                StoryBranchService.IsCustomBranchSection(_storyEditorViewModel.Document, followingSection);

            CaptureStoryUndoState(mergeWithPrevious ? "向前合并小节" : "向后合并小节");
            var merged = mergeWithPrevious
                ? _storyEditorService.MergeSectionWithPrevious(_storyRows, _storyRowSections, _currentStoryRowIndex)
                : _storyEditorService.MergeSectionWithNext(_storyRows, _storyRowSections, _currentStoryRowIndex);
            if (!merged)
            {
                // 正常走不到（能不能合并在上面刚问过）。真走到这儿：把这次没提交的撤销事务撤掉，
                // 免得它挂在 UndoService 里，把下一次操作的快照顶掉。
                _storyEditorViewModel.UndoService.CancelPending();
                ShowStoryStatus(InfoBarSeverity.Warning, "无法合并小节", "小节边界和刚才看到的不一样了，请再看一眼当前句所在的小节。");
                return;
            }

            if (keepCustomBranchLogic)
            {
                StoryBranchService.SetCustomBranchSection(_storyEditorViewModel.Document, precedingSection, true);
            }

            // 小节号变了 → Choice 函数名要跟着迁移（和打开章节、拆分小节是同一套）。
            StoryChoiceNoteState? migratedChoiceNotes = null;
            if (SynchronizeStorySectionState())
            {
                NormalizeStoryChoiceFunctionPrefixes(out migratedChoiceNotes);
            }

            EnsureStorySectionOptions(_storyRowSections.Values.DefaultIfEmpty(1).Max());
            SelectStorySection(precedingSection);
            QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            UpdateStoryRowIndexInput();
            UpdateStoryEditorHeader();
            UpdateStoryToolbarCurrentInfo();
            _storyEditorViewModel.RefreshCommandStates();
            ShowStoryStatus(
                InfoBarSeverity.Success,
                mergeWithPrevious ? "已向前合并" : "已向后合并",
                $"{StorySectionDisplay.FormatName(precedingSection)}和{StorySectionDisplay.FormatName(followingSection)}现在是同一节了。");
        }

        private void SelectStorySection(int section)
        {
            foreach (var item in StorySectionComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is int value && value == section)
                {
                    StorySectionComboBox.SelectedItem = item;
                    return;
                }
            }

            StorySectionComboBox.SelectedIndex = 0;
        }

        private int GetSelectedStorySection()
        {
            return StorySectionComboBox.SelectedItem is ComboBoxItem { Tag: int section }
                ? Math.Max(1, section)
                : 1;
        }

        private int GetCurrentStorySection()
        {
            return _storyEditorViewModel.GetCurrentSection();
        }

        // UpdateStoryCustomBranchLogicToggle / StoryCustomBranchLogicToggle_Toggled 已删除（2026-10-15）：
        // 「自定义跳转逻辑」那个开关从小节这边封存了，详见
        // Docs/2026-10-15-小节自定义跳转逻辑-封存说明.md。
        // 数据本身没动：标记仍然是 StoryDocument.CustomBranchSectionFirstRowIds，
        // 而且选项管理器那个入口也一起收了 —— 现在界面上没有任何地方能改它。

        private int GetStorySectionAtRowIndex(int rowIndex)
        {
            return _storyEditorViewModel.GetSectionAtRowIndex(rowIndex);
        }

        private bool SynchronizeStorySectionState()
        {
            return _storyEditorService.NormalizeContiguousSections(_storyRows, _storyRowSections);
        }

        private string BuildNextStoryChoiceFunctionIndicator()
        {
            if (_currentStoryChapter is null)
            {
                return "Choice1";
            }

            return StoryFunctionService.BuildNextChoiceIndicator(
                _currentStoryChapter.Code,
                GetCurrentStorySection(),
                _currentProject?.Code,
                _storyRows);
        }

        private string BuildCurrentStoryChapterSectionChoicePrefix()
        {
            if (_currentStoryChapter is null)
            {
                return "Choice";
            }

            return StoryFunctionService.BuildChapterSectionChoicePrefix(
                _currentStoryChapter.Code,
                GetCurrentStorySection(),
                _currentProject?.Code);
        }

        private string BuildCurrentStoryChapterSectionCode()
        {
            return _currentStoryChapter is null
                ? string.Empty
                : StoryFunctionService.BuildChapterSectionCode(_currentStoryChapter.Code, GetCurrentStorySection());
        }

    }
}
