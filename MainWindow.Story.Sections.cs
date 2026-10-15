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
        private bool _isUpdatingStoryCustomBranchLogicToggle;

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
            NormalizeStoryChoiceFunctionPrefixes(out var migratedChoiceNotes);
            QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            UpdateStoryRowIndexInput();
            UpdateStoryEditorHeader();
            UpdateStoryToolbarCurrentInfo();
            _storyEditorViewModel.RefreshCommandStates();
            ShowStoryStatus(InfoBarSeverity.Success, "已拆分小节", $"当前句现在是{StorySectionDisplay.FormatName(nextSection)}的第一句。");
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

        private void UpdateStoryCustomBranchLogicToggle(int section)
        {
            _isUpdatingStoryCustomBranchLogicToggle = true;
            try
            {
                StoryCustomBranchLogicToggle.IsOn = StoryBranchService.IsCustomBranchSection(
                    _storyEditorViewModel.Document,
                    section);
            }
            finally
            {
                _isUpdatingStoryCustomBranchLogicToggle = false;
            }
        }

        private void StoryCustomBranchLogicToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingStoryRow || _isUpdatingStoryCustomBranchLogicToggle ||
                _currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (!CanEditStoryData())
            {
                UpdateStoryCustomBranchLogicToggle(GetCurrentStorySection());
                return;
            }

            var section = GetCurrentStorySection();
            var isCustom = StoryCustomBranchLogicToggle.IsOn;
            CaptureStoryUndoState(isCustom ? "启用小节自定义跳转逻辑" : "恢复小节自动跳转同步");
            StoryBranchService.SetCustomBranchSection(
                _storyEditorViewModel.Document,
                section,
                isCustom);
            QueueCurrentStoryDocumentSave();
            _uiSoundService.Play(UiSoundKind.Selection);
            ShowStoryStatus(
                InfoBarSeverity.Success,
                isCustom ? "已保留自定义跳转" : "已恢复自动同步",
                isCustom
                    ? $"{StorySectionDisplay.FormatName(section)}将在同步 AllRoad 时保持现有手工设置。"
                    : $"{StorySectionDisplay.FormatName(section)}将重新由工具箱生成 AllRoad 跳转。");
        }

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
