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
        private async void OpenStoryEditorFromUi(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                await ShowStoryEditorPageAsync(chapter);
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "打开章节失败", ex.Message);
                AppendLog(LogKind.Error, "打开章节编辑器失败。", ex);
            }
        }

        private async Task ShowStoryEditorPageAsync(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法打开章节", "请先打开一个项目。");
                return;
            }

            _workbenchReturnPage = WorkbenchReturnPage.StoryEditor;
            SaveCurrentStoryRow();
            // 换章同理：先把当前句的备注落盘，再切走。
            SaveStoryTodosIfDirty();
            _storyEditSaveTimer?.Stop();
            await _storySaveCoordinator.FlushAsync();
            _currentStoryChapter = chapter;
            _currentStoryAssetLibrary = ResolveProjectAssetLibrary(_currentProject);
            _currentStoryDocumentPath = _storyDocumentRepository.GetDocumentPath(chapter);
            ClearStoryUndoStack();
            UpdateStoryDebugModeUi();
            _storyBackgroundPreviewKey = null;
            _lastStoryVoiceRowId = null;
            _storyCharacterPreviewKeys.Clear();
            _storyPreviewRefreshCancellation?.Cancel();
            _lastStoryVoiceRowId = null;
            _storyPreviewRefreshCancellation?.Dispose();
            _storyPreviewRefreshCancellation = null;
            _storyPreviewImageCache.Clear();
            _storyPreviewImageCacheOrder.Clear();
            _storyBgmPlaybackSuppressed = false;
            ApplyStoryTextFontSizeToUi();
            UpdateStoryEditorHeader();
            _storyEditorViewModel.AssetStatusText = _currentStoryAssetLibrary is null
                ? "当前项目未关联素材库。"
                : $"素材库：{_currentStoryAssetLibrary.Name}";

            LoadStoryDocument(chapter);
            // 换了章节 → 前缀状态表整张作废（行数多半不同，但同章重新载入时行数可能一样，
            // 所以这里显式清一次，不靠"长度对不上"那条兜底）。
            InvalidateStoryPersistentStateCache();
            var storyDataMigrated = NormalizeStoryCharacterCodes();
            storyDataMigrated |= SynchronizeStorySectionState();
            storyDataMigrated |= NormalizeStoryChoiceFunctionPrefixes(out var migratedChoiceNotes);
            if (storyDataMigrated)
            {
                QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            }

            _currentStoryRowIndex = Math.Clamp(chapter.LastEditedRowIndex, 0, _storyRows.Count - 1);
            // 打开一章就把备注读回来（一个项目一份文件）。
            LoadStoryTodos();
            _storyEditorViewModel.RefreshCommandStates();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            UpdateStoryCharacterSlotLayout();
            LoadStoryRowIntoUi();
            // 打开章节就核一次素材引用的身份指纹（见 MainWindow.Story.AssetIdentity.cs）：
            // 号还在、指向的图被换过（素材重排 / 改名 / 中间插了一张）时按记录修回来。
            VerifyAndRepairStoryAssetIdentities();

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
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(StoryEditorPage);
            StoryEditorBackButton.Focus(FocusState.Programmatic);
            StoryEditorPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开章节编辑器：{chapter.Name}（{chapter.Code}）");
        }

        private async void CloseStoryEditorButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            SaveCurrentStoryRow();
            SaveCurrentChapterProgress();
            _storyEditSaveTimer?.Stop();
            await _storySaveCoordinator.FlushAsync();
            _storyPreviewRefreshCancellation?.Cancel();
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
            if (StorySpeakerTextBox is not null)
            {
                StorySpeakerTextBox.IsEnabled = !_isStoryDebugModeEnabled;
            }

            if (StoryTextTextBox is not null)
            {
                StoryTextTextBox.IsReadOnly = _isStoryDebugModeEnabled;
            }

            if (InsertStoryRowBeforeButton is not null)
            {
                InsertStoryRowBeforeButton.Visibility = _isStoryDebugModeEnabled ? Visibility.Collapsed : Visibility.Visible;
            }

            if (InsertStoryRowAfterButton is not null)
            {
                InsertStoryRowAfterButton.Visibility = _isStoryDebugModeEnabled ? Visibility.Collapsed : Visibility.Visible;
            }

            _storyEditorViewModel.RefreshCommandStates();
        }

        private bool CanEditStoryData()
        {
            if (!_isStoryDebugModeEnabled)
            {
                return true;
            }

            ShowStoryStatus(InfoBarSeverity.Informational, "调试模式只读", "关闭调试模式后才能修改剧情数据。");
            return false;
        }

        private void CaptureStoryUndoState(string description)
        {
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            _storyEditorViewModel.UndoService.Begin(
                _storyEditorViewModel.Document,
                _currentStoryRowIndex,
                description);
        }

        private void CaptureStoryRowUndoState(string description)
        {
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            _storyEditorViewModel.UndoService.BeginRowEdit(
                _storyEditorViewModel.Document,
                _currentStoryRowIndex,
                description);
        }

        private void CommitStoryUndoState()
        {
            if (_storyEditorViewModel.UndoService.Commit(
                    _storyEditorViewModel.Document,
                    _currentStoryRowIndex))
            {
                var change = _storyEditorViewModel.UndoService.LastUndoChange;
                AppendLog(
                    LogKind.User,
                    $"编辑器操作：{change?.Description}（变化行 {change?.ChangedRowCount ?? 0}）");
            }

            UpdateStoryUndoState();
        }

        private void ClearStoryUndoStack()
        {
            _storyEditorViewModel.UndoService.Clear();
            UpdateStoryUndoState();
        }

        private void UpdateStoryUndoState()
        {
            _storyEditorViewModel.RefreshUndoState();
        }

        private void UndoStoryEditorOperation()
        {
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            _storyEditSaveTimer?.Stop();
            SaveCurrentStoryRow();
            CommitStoryUndoState();
            if (!_storyEditorViewModel.UndoService.TryUndo(
                    _storyEditorViewModel.Document,
                    _currentStoryRowIndex,
                    out var application) || application is null)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有可撤回操作", "当前章节编辑器还没有可撤回的操作。");
                return;
            }

            _currentStoryRowIndex = application.RowIndex;
            _isStoryRowDirty = false;
            _storyTextEditUndoCaptured = false;
            QueueCurrentStoryDocumentSave();
            SaveCurrentChapterProgress();
            ClearStoryFunctionTips();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            UpdateStoryUndoState();
            _storyEditorViewModel.RefreshCommandStates();
            ShowStoryStatus(InfoBarSeverity.Success, "已撤回", application.Description);
            AppendLog(LogKind.User, $"撤回编辑器操作：{application.Description}");
        }

        private void RedoStoryEditorOperation()
        {
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (!_storyEditorViewModel.UndoService.TryRedo(
                    _storyEditorViewModel.Document,
                    _currentStoryRowIndex,
                    out var application) || application is null)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有可重做操作", "当前章节编辑器还没有可重做的操作。");
                return;
            }

            _storyEditSaveTimer?.Stop();
            _currentStoryRowIndex = application.RowIndex;
            _isStoryRowDirty = false;
            _storyTextEditUndoCaptured = false;
            QueueCurrentStoryDocumentSave();
            SaveCurrentChapterProgress();
            ClearStoryFunctionTips();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            UpdateStoryUndoState();
            _storyEditorViewModel.RefreshCommandStates();
            ShowStoryStatus(InfoBarSeverity.Success, "已重做", application.Description);
            AppendLog(LogKind.User, $"重做编辑器操作：{application.Description}");
        }

        private void LoadStoryRowIntoUi(bool playMedia = true, bool showInheritedTransitionStatus = true)
        {
            if (_storyRows.Count == 0)
            {
                StopStoryVoicePlayback();
                return;
            }

            ClearStoryFunctionTips();
            var hotPath = Stopwatch.StartNew();
            _isLoadingStoryRow = true;
            _storyTextEditUndoCaptured = false;
            try
            {
                var row = _storyRows[_currentStoryRowIndex];
                _storyEditorViewModel.SpeakerText = GetStorySpeakerDisplayName(row.Get("TalkChar"));
                _storyEditorViewModel.StoryText = row.Get("Tesxt");
                // 备注和剧情文本**同一层**：就在 StoryText 旁边一起刷。
                //
                // 为什么要放这儿而不是 NavigateToStoryRow：这个函数才是"把当前行刷进界面"
                // 的中心入口 —— 换句、撤销、重做、换小节全都走它。放旁路上的话，
                // 撤销 / 重做之后备注面板还停在上一行的内容上，一编辑就写到错的句子上。
                // 2026-10-13 用户的原话：「这两个本质是一个东西」——位置也该在一起。
                LoadStoryTodoIntoUi();
                UpdateStoryRowIndexInput();
                var section = GetCurrentStorySection();
                EnsureStorySectionOptions(section);
                SelectStorySection(section);
                UpdateStoryEditorHeader();
                UpdateStoryToolbarCurrentInfo();
                _isStoryRowDirty = false;
            }
            finally
            {
                _isLoadingStoryRow = false;
            }

            RefreshCurrentStoryPersistentStatus();
            _ = RefreshStoryPreviewAsync(showInheritedTransitionStatus);
            // 到这里为止都是**同步**跑在 UI 线程上的（再往下是即发即忘的媒体播放）——
            // 这一段有多久，就是"切句顿一下"有多久。
            LogStoryHotPath("切句", hotPath);
            if (!playMedia)
            {
                return;
            }

            var activeRowId = _storyRows[_currentStoryRowIndex].StableId;
            if (!string.Equals(_lastStoryVoiceRowId, activeRowId, StringComparison.OrdinalIgnoreCase))
            {
                _lastStoryVoiceRowId = activeRowId;
                _ = PlayCurrentStoryVoiceFromUiAsync();
            }
            _ = ApplyCurrentStoryRowMediaAndFunctionAsync();
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

        private void NavigateToStoryRow(
            int targetIndex,
            bool rebuildPersistentState,
            bool showInheritedTransitionStatus = true)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            targetIndex = Math.Clamp(targetIndex, 0, _storyRows.Count - 1);
            SaveCurrentStoryRow();
            // 备注也要跟着存一次：打完备注立刻点别的行时，debounce 那一拍可能还没到 ——
            // 不在这里存的话那段文字就丢了（"一句话都存不了"的另一种表现）。
            SaveStoryTodosIfDirty();
            if (targetIndex == _currentStoryRowIndex)
            {
                UpdateStoryRowIndexInput();
                return;
            }

            _currentStoryRowIndex = targetIndex;
            SaveCurrentChapterProgress();
            _storyEditorViewModel.RefreshCommandStates();
            if (rebuildPersistentState)
            {
                RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            }

            LoadStoryRowIntoUi(showInheritedTransitionStatus: showInheritedTransitionStatus);
        }

        private void NavigatePreviousStoryRow()
        {
            if (_storyRows.Count == 0 || _currentStoryRowIndex <= 0)
            {
                return;
            }

            NavigateToStoryRow(
                _currentStoryRowIndex - 1,
                rebuildPersistentState: true,
                showInheritedTransitionStatus: false);
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
                ShowStoryStatus(InfoBarSeverity.Informational, "已经是第一个小节", "没有更前面的小节。");
                return;
            }

            if (targetSectionIndex >= orderedSections.Count)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已经是最后一个小节", "没有更后面的小节。");
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
                ShowStoryStatus(InfoBarSeverity.Informational, "已经是最后一句", "“下一句”只负责导航，不会自动创建新句子。");
                _storyEditorViewModel.RefreshCommandStates();
                return;
            }

            var result = _storyEditorService.MoveNext(_storyRows, _storyRowSections, _currentStoryRowIndex);
            _currentStoryRowIndex = result.RowIndex;
            SaveCurrentChapterProgress();
            _storyEditorViewModel.RefreshCommandStates();
            LoadStoryRowIntoUi();
        }

        private void InsertStoryRowBefore()
        {
            InsertStoryRow(insertAfter: false);
        }

        private void InsertStoryRowAfter()
        {
            InsertStoryRow(insertAfter: true);
        }

        private void InsertStoryRow(bool insertAfter)
        {
            if (_storyRows.Count == 0 || _currentStoryDocumentPath is null)
            {
                return;
            }

            if (_isStoryDebugModeEnabled)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "调试模式", "调试模式下已隐藏插入功能。");
                return;
            }

            SaveCurrentStoryRow();
            CaptureStoryUndoState(insertAfter ? "向右插入一句" : "向左插入一句");
            var result = insertAfter
                ? _storyEditorService.InsertAfterCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex)
                : _storyEditorService.InsertAtCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex);
            _currentStoryRowIndex = result.RowIndex;
            QueueCurrentStoryDocumentSave();
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            _storyEditorViewModel.RefreshCommandStates();
            LoadStoryRowIntoUi();
            ShowStoryStatus(
                InfoBarSeverity.Success,
                insertAfter ? "已右插入" : "已左插入",
                insertAfter ? "新句子已插入原句右侧。" : "新句子已插入原句左侧，后面的剧情已顺延。");
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
            if (!CanEditStoryData() || _storyRows.Count == 0 || _currentStoryDocumentPath is null)
            {
                return;
            }

            var removedChoices = GetCurrentStoryChoiceValues();
            // 这一句可能是某个小节的锚点（小节第一句）。删之前先把身份记下来 ——
            // 删完就查不到它属于哪一节了，而「接替行是不是还在同一节里」正是接手规则要用到的判断。
            var deletedRowId = _storyRows[_currentStoryRowIndex].StableId;
            var deletedRowSection = GetStorySectionAtRowIndex(_currentStoryRowIndex);
            CaptureStoryUndoState("删除当前句");
            var result = _storyEditorService.DeleteCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex, removedChoices);
            _currentStoryRowIndex = result.RowIndex;
            // 接替行 = 落在被删行位置上的那一句（删的是最后一句时，就是新的最后一句）。
            var replacementRowId = _storyRows.Count == 0
                ? null
                : _storyRows[Math.Clamp(_currentStoryRowIndex, 0, _storyRows.Count - 1)].StableId;
            // ① 小节锚点顺延。**必须排在补编号空洞之前** —— 那一步会重排小节号，
            //    而「接替行还在不在被删行那一节里」要拿删除前这套号来比。
            // 选项目标和「自定义跳转逻辑」标记存的是小节第一行的 StableId，行没了引用就悬空。
            var sectionAnchorsRepointed = StoryBranchService.RepointSectionAnchorsAfterRowDelete(
                _storyEditorViewModel.Document,
                deletedRowId,
                deletedRowSection,
                replacementRowId);
            // ② 剪掉被删句子上那些再没人用的选项备注（顺带摘掉它们的 AllRoad 路线）。
            //    要赶在前缀迁移之前：它认的是**迁移前**那套函数名。
            var prunedChoiceNotes = RemoveUnusedStoryChoiceNotes(result.RemovedChoiceValues);
            if (prunedChoiceNotes is not null)
            {
                // 先落进文档。前缀迁移是从**文档当前状态**克隆的 —— 不先落这一步，
                // 刚剪掉的备注会在迁移结果里复活（迁移的产物后面会整个覆盖 ChoiceNotes）。
                _storyEditorViewModel.Document.ChoiceNotes = prunedChoiceNotes.Clone();
            }

            // ③ 删掉「只有一句的小节」会在小节号里留下空洞（1,2,4,5）：小节下拉框会多出一个
            //    点不动的空小节，「跳转小节」的函数候选里也会多一节根本不存在的小节。
            //    补法和打开章节时同一套：重排连续号 → 号真变了就迁移嵌着小节号的 Choice 函数名
            //    （函数名长这样：WHK-M6-06-Choice3，不迁移就全指到别的小节去了）。
            StoryChoiceNoteState? migratedChoiceNotes = null;
            var sectionsRenumbered = SynchronizeStorySectionState();
            if (sectionsRenumbered)
            {
                NormalizeStoryChoiceFunctionPrefixes(out migratedChoiceNotes);
            }

            QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            _storyEditorViewModel.RefreshCommandStates();
            LoadStoryRowIntoUi();
            if (sectionAnchorsRepointed)
            {
                ShowStoryStatus(
                    InfoBarSeverity.Informational,
                    "小节锚点已顺延",
                    "选项跳转 / 自定义跳转原来指向这一句，已改指向接替的那一句。");
            }
            else if (sectionsRenumbered)
            {
                ShowStoryStatus(
                    InfoBarSeverity.Informational,
                    "小节已重排",
                    "这一节被删空了，后面的小节号往前补了一位，Choice 函数名已跟着更新。");
            }
        }

    }
}
