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
        private void OpenStoryEditorFromUi(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                ShowStoryEditorPage(chapter);
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "打开章节失败", ex.Message);
                AppendLog(LogKind.Error, "打开章节编辑器失败。", ex);
            }
        }

        private void ShowStoryEditorPage(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法打开章节", "请先打开一个项目。");
                return;
            }

            SaveCurrentStoryRow();
            _storyEditSaveTimer?.Stop();
            _currentStoryChapter = chapter;
            _currentStoryAssetLibrary = ResolveProjectAssetLibrary(_currentProject);
            _currentStoryCsvPath = _storyCsvService.GetChapterCsvPath(chapter);
            ClearStoryUndoStack();
            UpdateStoryDebugModeUi();
            _storyBackgroundPreviewKey = null;
            _storyCharacterPreviewKeys.Clear();
            _storyBgmPlaybackSuppressed = false;
            ApplyStoryTextFontSizeToUi();
            UpdateStoryEditorHeader();
            _storyEditorViewModel.AssetStatusText = _currentStoryAssetLibrary is null
                ? "当前项目未关联素材库。"
                : $"素材库：{_currentStoryAssetLibrary.Name}";

            LoadStoryRowsFromSectionFiles(chapter);
            NormalizeStoryCharacterCodes();
            SynchronizeStorySectionState();
            PersistCurrentStoryRowsToFiles();

            _currentStoryRowIndex = Math.Clamp(chapter.LastEditedRowIndex, 0, _storyRows.Count - 1);
            _storyEditorViewModel.RefreshCommandStates();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            UpdateStoryCharacterSlotLayout();
            LoadStoryRowIntoUi();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Visible;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(StoryEditorPage);
            StoryEditorBackButton.Focus(FocusState.Programmatic);
            StoryEditorPage.Focus(FocusState.Programmatic);
            _ = WarmStoryPreviewImageCacheAsync();
            AppendLog(LogKind.User, $"打开章节编辑器：{chapter.Name}（{chapter.Code}）");
        }

        private void CloseStoryEditorButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            SaveCurrentStoryRow();
            SaveCurrentChapterProgress();
            _storyEditSaveTimer?.Stop();
            StopStoryEditorAudio();
            if (_currentProject is not null)
            {
                ShowProjectDetailPage(_currentProject);
            }
            else
            {
                ShowWorkbenchPage();
            }
        }

        private void StoryDebugModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _isStoryDebugModeEnabled = StoryDebugModeCheckBox?.IsChecked == true;
            UpdateStoryDebugModeUi();
            AppendLog(LogKind.User, _isStoryDebugModeEnabled ? "故事编辑器调试模式已开启。" : "故事编辑器调试模式已关闭。");
        }

        private void UpdateStoryDebugModeUi()
        {
            if (InsertStoryRowHereButton is not null)
            {
                InsertStoryRowHereButton.Visibility = _isStoryDebugModeEnabled ? Visibility.Collapsed : Visibility.Visible;
            }

            _storyEditorViewModel.RefreshCommandStates();
        }

        private StoryEditorUndoState? CaptureStoryUndoState(string description)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return null;
            }

            var state = new StoryEditorUndoState(
                CloneStoryRows(_storyRows),
                new Dictionary<string, int>(_storyRowSections, StringComparer.OrdinalIgnoreCase),
                Math.Clamp(_currentStoryRowIndex, 0, _storyRows.Count - 1),
                description,
                _currentStoryChapter is null ? null : StoryStateService.CloneChoiceNotes(_storyStateService.ReadChoiceNotes(_currentStoryChapter)));
            _storyUndoStack.Add(state);
            if (_storyUndoStack.Count > MaxStoryUndoCount)
            {
                _storyUndoStack.RemoveAt(0);
            }

            UpdateStoryUndoState();
            AppendLog(LogKind.User, $"编辑器操作：{description}");
            return state;
        }

        private void ClearStoryUndoStack()
        {
            _storyUndoStack.Clear();
            UpdateStoryUndoState();
        }

        private void UpdateStoryUndoState()
        {
            _storyEditorViewModel.RefreshUndoState();
        }

        private void UndoStoryEditorOperation()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (_storyUndoStack.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有可撤回操作", "当前章节编辑器还没有可撤回的操作。");
                return;
            }

            _storyEditSaveTimer?.Stop();
            var state = _storyUndoStack[^1];
            _storyUndoStack.RemoveAt(_storyUndoStack.Count - 1);
            _storyRows.Clear();
            _storyRows.AddRange(CloneStoryRows(state.Rows));
            _storyRowSections.Clear();
            foreach (var pair in state.Sections)
            {
                _storyRowSections[pair.Key] = Math.Max(1, pair.Value);
            }

            _currentStoryRowIndex = Math.Clamp(state.RowIndex, 0, Math.Max(0, _storyRows.Count - 1));
            _isStoryRowDirty = false;
            if (_currentStoryChapter is not null && state.ChoiceNotes is not null)
            {
                _storyStateService.WriteChoiceNotes(_currentStoryChapter, StoryStateService.CloneChoiceNotes(state.ChoiceNotes));
                RemoveUnusedStoryChoiceNotes(state.ChoiceNotes.Choices.Keys);
            }

            ApplyStorySectionsInRowOrder(GetStorySectionsInRowOrder());
            PersistCurrentStoryRowsToFiles();
            SaveCurrentChapterProgress();
            ClearStoryFunctionTips();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            UpdateStoryUndoState();
            _storyEditorViewModel.RefreshCommandStates();
            ShowStoryStatus(InfoBarSeverity.Success, "已撤回", state.Description);
            AppendLog(LogKind.User, $"撤回编辑器操作：{state.Description}");
        }

        private static List<StoryRow> CloneStoryRows(IEnumerable<StoryRow> rows)
        {
            return StoryEditorService.CloneRows(rows);
        }

        private void LoadStoryRowIntoUi()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            ClearStoryFunctionTips();
            _isLoadingStoryRow = true;
            try
            {
                var row = _storyRows[_currentStoryRowIndex];
                _storyEditorViewModel.SpeakerText = row.Get("TalkChar");
                _storyEditorViewModel.StoryText = row.Get("Tesxt");
                UpdateStoryRowIndexInput();
                var section = GetCurrentStorySection();
                EnsureStorySectionOptions(section);
                SelectStorySection(section);
                NormalizeCurrentStoryChoiceFunctionPrefix();
                UpdateStoryEditorHeader();
                UpdateStoryToolbarCurrentInfo();
                _isStoryRowDirty = false;
            }
            finally
            {
                _isLoadingStoryRow = false;
            }

            _ = RefreshStoryPreviewAsync();
            _ = ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private bool SaveCurrentStoryRow()
        {
            if (_isLoadingStoryRow || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return false;
            }

            var sectionChanged = SetCurrentStorySectionIfChanged(GetSelectedStorySection());
            if (!_isStoryRowDirty && !sectionChanged)
            {
                return false;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var changed = false;
            var speakerName = NormalizeStoryCharacterNameForCsv(_storyEditorViewModel.SpeakerText);
            if (!string.Equals(row.Get("TalkChar"), speakerName, StringComparison.Ordinal))
            {
                row.Set("TalkChar", speakerName);
                changed = true;
            }

            if (!string.Equals(row.Get("Tesxt"), _storyEditorViewModel.StoryText, StringComparison.Ordinal))
            {
                row.Set("Tesxt", _storyEditorViewModel.StoryText);
                changed = true;
            }

            changed |= NormalizeStoryDetachedCharacterLayers(row);

            if (changed)
            {
                PersistCurrentStoryRowsToFiles();
                _isStoryRowDirty = false;
                SaveCurrentChapterProgress();
                _ = RefreshStoryPreviewAsync();
            }

            if (sectionChanged)
            {
                PersistCurrentStoryRowsToFiles();
                SaveCurrentChapterProgress();
            }

            _isStoryRowDirty = false;
            return changed || sectionChanged;
        }

        private void ScheduleStoryRowSave()
        {
            if (_isLoadingStoryRow)
            {
                return;
            }

            if (_storyEditSaveTimer is null)
            {
                return;
            }

            if (!_isStoryRowDirty)
            {
                CaptureStoryUndoState("编辑当前句文本");
            }

            _isStoryRowDirty = true;
            _storyEditSaveTimer.Stop();
            _storyEditSaveTimer.Start();
        }

        private void StoryEditSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            SaveCurrentStoryRow();
        }

        private void StorySpeakerTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleStoryRowSave();
        }

        private void StoryTextTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleStoryRowSave();
        }

        private void UpdateStoryRowIndexInput()
        {
            _isUpdatingStoryRowIndexText = true;
            try
            {
                if (_appSettings.ShowFullStoryChapterLength)
                {
                    _storyEditorViewModel.RowIndexText = (_currentStoryRowIndex + 1).ToString();
                    _storyEditorViewModel.RowTotalText = $"/ {_storyRows.Count} 句";
                }
                else
                {
                    var sectionInfo = _storyEditorViewModel.GetCurrentSectionPositionInfo();
                    _storyEditorViewModel.RowIndexText = sectionInfo.LocalIndex.ToString();
                    _storyEditorViewModel.RowTotalText = $"/ {sectionInfo.Total} 句";
                }

            }
            finally
            {
                _isUpdatingStoryRowIndexText = false;
            }
        }

        private void SaveCurrentChapterProgress()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            _projectWorkspaceService.SaveChapterProgress(_currentStoryChapter, _currentStoryRowIndex);
        }

        private void StoryRowIndexTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitStoryRowIndexJump();
                e.Handled = true;
            }
        }

        private void StoryRowIndexTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitStoryRowIndexJump();
        }

        private void CommitStoryRowIndexJump()
        {
            if (_isUpdatingStoryRowIndexText || _storyRows.Count == 0)
            {
                return;
            }

            if (!int.TryParse(StoryRowIndexTextBox.Text.Trim(), out var rowNumber))
            {
                UpdateStoryRowIndexInput();
                return;
            }

            if (_appSettings.ShowFullStoryChapterLength)
            {
                NavigateToStoryRow(Math.Clamp(rowNumber, 1, _storyRows.Count) - 1, rebuildPersistentState: true);
                return;
            }

            var currentSection = GetCurrentStorySection();
            var sectionIndexes = Enumerable.Range(0, _storyRows.Count)
                .Where(index => GetStorySectionAtRowIndex(index) == currentSection)
                .ToList();
            if (sectionIndexes.Count == 0)
            {
                UpdateStoryRowIndexInput();
                return;
            }

            var targetLocalIndex = Math.Clamp(rowNumber, 1, sectionIndexes.Count) - 1;
            NavigateToStoryRow(sectionIndexes[targetLocalIndex], rebuildPersistentState: true);
        }

        private void NavigateToStoryRow(int targetIndex, bool rebuildPersistentState)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            targetIndex = Math.Clamp(targetIndex, 0, _storyRows.Count - 1);
            SaveCurrentStoryRow();
            if (targetIndex == _currentStoryRowIndex)
            {
                UpdateStoryRowIndexInput();
                return;
            }

            _currentStoryRowIndex = targetIndex;
            SaveCurrentChapterProgress();
            if (rebuildPersistentState)
            {
                RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            }

            LoadStoryRowIntoUi();
        }

        private void NavigatePreviousStoryRow()
        {
            if (_storyRows.Count == 0 || _currentStoryRowIndex <= 0)
            {
                return;
            }

            NavigateToStoryRow(_currentStoryRowIndex - 1, rebuildPersistentState: true);
        }

        private void NavigateStorySection(int direction)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var orderedSections = Enumerable.Range(0, _storyRows.Count)
                .Select(index => new
                {
                    Section = GetStorySectionAtRowIndex(index),
                    FirstRowIndex = index
                })
                .GroupBy(item => item.Section)
                .Select(group => group.First())
                .OrderBy(item => item.FirstRowIndex)
                .ToList();
            if (orderedSections.Count <= 1)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有其他小节", "当前章节只有一个小节。");
                return;
            }

            var currentSection = GetCurrentStorySection();
            var currentSectionIndex = orderedSections.FindIndex(item => item.Section == currentSection);
            if (currentSectionIndex < 0)
            {
                currentSectionIndex = 0;
            }

            var targetSectionIndex = currentSectionIndex + Math.Sign(direction);
            if (targetSectionIndex < 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已经是第一节", "没有更前面的小节。");
                return;
            }

            if (targetSectionIndex >= orderedSections.Count)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已经是最后一节", "没有更后面的小节。");
                return;
            }

            NavigateToStoryRow(orderedSections[targetSectionIndex].FirstRowIndex, rebuildPersistentState: true);
        }

        private void NavigateNextStoryRow()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            SaveCurrentStoryRow();
            if (_currentStoryRowIndex >= _storyRows.Count - 1)
            {
                if (_isStoryDebugModeEnabled)
                {
                    ShowStoryStatus(InfoBarSeverity.Informational, "调试模式", "已经是最后一句，调试模式下不会新建句子。");
                    return;
                }

                CaptureStoryUndoState("新建下一句");
            }

            var result = _storyEditorService.MoveNextOrCreate(_storyRows, _storyRowSections, _currentStoryRowIndex);
            _currentStoryRowIndex = result.RowIndex;
            SaveCurrentChapterProgress();
            _storyEditorViewModel.RefreshCommandStates();
            if (result.Changed)
            {
                PersistCurrentStoryRowsToFiles();
            }

            LoadStoryRowIntoUi();
        }

        private void InsertStoryRowHere()
        {
            if (_storyRows.Count == 0 || _currentStoryCsvPath is null)
            {
                return;
            }

            if (_isStoryDebugModeEnabled)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "调试模式", "调试模式下已隐藏原地新建。");
                return;
            }

            SaveCurrentStoryRow();
            CaptureStoryUndoState("原地新建一句");
            var result = _storyEditorService.InsertAtCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex);
            _currentStoryRowIndex = result.RowIndex;
            PersistCurrentStoryRowsToFiles();
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            _storyEditorViewModel.RefreshCommandStates();
            LoadStoryRowIntoUi();
            ShowStoryStatus(InfoBarSeverity.Success, "已原地新建", "新句子已插入当前位置，后面的剧情已顺延。");
        }

        private void StoryEditorPage_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (StoryEditorPage.Visibility != Visibility.Visible)
            {
                return;
            }

            var properties = e.GetCurrentPoint(StoryEditorPage).Properties;
            if (properties.IsXButton1Pressed)
            {
                NavigateNextStoryRow();
                e.Handled = true;
            }
            else if (properties.IsXButton2Pressed)
            {
                NavigatePreviousStoryRow();
                e.Handled = true;
            }
        }

        private void DeleteCurrentStoryRow()
        {
            if (_storyRows.Count == 0 || _currentStoryCsvPath is null)
            {
                return;
            }

            var removedChoices = GetCurrentStoryChoiceValues();
            CaptureStoryUndoState("删除当前句");
            var result = _storyEditorService.DeleteCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex, removedChoices);
            _currentStoryRowIndex = result.RowIndex;
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes(result.RemovedChoiceValues);
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            _storyEditorViewModel.RefreshCommandStates();
            LoadStoryRowIntoUi();
        }

    }
}
