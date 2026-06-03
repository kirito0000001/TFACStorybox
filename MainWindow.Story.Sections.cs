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
            if (_isLoadingStoryRow || _isUpdatingStorySectionOptions || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var selectedSection = GetSelectedStorySection();
            if (selectedSection != GetCurrentStorySection())
            {
                CaptureStoryUndoState("调整当前句小节");
            }

            if (SetCurrentStorySectionIfChanged(selectedSection))
            {
                NormalizeCurrentStoryChoiceFunctionPrefix();
                PersistCurrentStoryRowsToFiles();
                UpdateStoryRowIndexInput();
                UpdateStoryEditorHeader();
                UpdateStoryToolbarCurrentInfo();
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
                        Content = $"第 {i} 小节",
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
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var nextSection = Math.Max(StorySectionComboBox.Items.Count, _storyRowSections.Values.DefaultIfEmpty(1).Max()) + 1;
            CaptureStoryUndoState("新增小节");
            EnsureStorySectionOptions(nextSection);
            _isUpdatingStorySectionOptions = true;
            try
            {
                SelectStorySection(nextSection);
            }
            finally
            {
                _isUpdatingStorySectionOptions = false;
            }

            SetCurrentStorySection(nextSection);
            NormalizeCurrentStoryChoiceFunctionPrefix();
            PersistCurrentStoryRowsToFiles();
            UpdateStoryRowIndexInput();
            UpdateStoryEditorHeader();
            UpdateStoryToolbarCurrentInfo();
            _storyEditorViewModel.RefreshCommandStates();
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

        private int GetStorySectionAtRowIndex(int rowIndex)
        {
            return _storyEditorViewModel.GetSectionAtRowIndex(rowIndex);
        }

        private void SetCurrentStorySection(int section)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            _storyEditorViewModel.SetCurrentSection(section);
            EnsureStorySectionOptions(section);
        }

        private bool SetCurrentStorySectionIfChanged(int section)
        {
            if (_storyRows.Count == 0)
            {
                return false;
            }

            var changed = _storyEditorViewModel.SetCurrentSectionIfChanged(section);
            EnsureStorySectionOptions(section);
            return changed;
        }

        private void LoadStoryRowsFromSectionFiles(ChapterInfo chapter)
        {
            _storyRows.Clear();
            _storyRowSections.Clear();
            var result = _storySessionService.LoadRowsFromSectionFiles(chapter);
            _storyRows.AddRange(result.Rows);
            foreach (var pair in result.Sections)
            {
                _storyRowSections[pair.Key] = pair.Value;
            }

            _storyEditorViewModel.RefreshCollectionState();
            _storyEditorViewModel.RefreshCommandStates();

            if (result.RemovedEmptySectionCount > 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已清理空小节", $"检测并删除 {result.RemovedEmptySectionCount} 个空小节 CSV。");
            }
        }

        private void ImportLooseStorySectionCsvFiles(ChapterInfo chapter)
        {
            if (_currentStoryCsvPath is null || !Directory.Exists(chapter.Path))
            {
                return;
            }

            var looseCsvPaths = _storySessionService.GetLooseSectionCsvPaths(chapter);
            if (looseCsvPaths.Count == 0)
            {
                return;
            }

            var importedCount = ImportStorySectionCsvFiles(chapter, looseCsvPaths, deleteSourceFiles: true);
            if (importedCount > 0)
            {
                EnsureStorySectionOptions(_storyRowSections.Values.DefaultIfEmpty(1).Max());
                ShowStoryStatus(InfoBarSeverity.Informational, "已导入小节 CSV", $"检测并导入 {importedCount} 个章节小节。");
            }
        }

        private int ImportStorySectionCsvFiles(ChapterInfo chapter, IReadOnlyList<string> csvPaths, bool deleteSourceFiles)
        {
            var result = _storySessionService.ImportSectionCsvFiles(chapter, csvPaths, deleteSourceFiles);
            foreach (var log in result.Logs)
            {
                AppendLog(log.Kind, log.Message);
            }

            if (result.Changed)
            {
                if (_currentStoryChapter is not null && PathsEqual(_currentStoryChapter.Path, chapter.Path))
                {
                    LoadStoryRowsFromSectionFiles(chapter);
                    SynchronizeStorySectionState();
                    WriteStorySectionState(chapter.Path, _storyRowSections);
                }
            }

            return result.ImportedCount;
        }

        private void PersistCurrentStoryRowsToFiles(bool showStatus = false)
        {
            if (_isPersistingStoryRows || _currentStoryChapter is null || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            _isPersistingStoryRows = true;
            try
            {
                var result = _storySessionService.PersistRowsToSectionFiles(_currentStoryChapter, _currentStoryCsvPath, _storyRows, _storyRowSections);

                if (showStatus)
                {
                    ShowStoryStatus(InfoBarSeverity.Success, "小节 CSV 已更新", $"已按 {result.ActiveCsvCount} 个小节文件保存。");
                }
            }
            finally
            {
                _isPersistingStoryRows = false;
            }
        }

        private void SynchronizeStorySectionState()
        {
            _storyEditorService.SynchronizeSections(_storyRows, _storyRowSections);
        }

        private List<int> GetStorySectionsInRowOrder()
        {
            return _storyEditorService.GetSectionsInRowOrder(_storyRows, _storyRowSections);
        }

        private void ApplyStorySectionsInRowOrder(IReadOnlyList<int> sections)
        {
            _storyEditorService.ApplySectionsInRowOrder(_storyRows, _storyRowSections, sections);
        }

        private void WriteStorySectionState()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            WriteStorySectionState(_currentStoryChapter.Path, _storyRowSections);
        }

        private void WriteStorySectionState(string chapterPath, IReadOnlyDictionary<string, int> sections)
        {
            _storyStateService.WriteSectionState(chapterPath, sections);
        }

        private async void ExportStorySectionsButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportStorySectionsAsync(true);
        }

        private void SynchronizeStorySectionCsvExports(bool showStatus = false)
        {
            _ = ExportStorySectionsAsync(showStatus);
        }

        private Task ExportStorySectionsAsync(bool showStatus = false)
        {
            if (_currentProject is null || _currentStoryChapter is null || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return Task.CompletedTask;
            }

            PersistCurrentStoryRowsToFiles(showStatus);
            return Task.CompletedTask;
        }

        private static string BuildSectionCsvBaseName(string chapterCode)
        {
            return StoryCsvService.BuildSectionCsvBaseName(chapterCode);
        }

        private static string BuildSectionCsvChapterBaseName(string chapterCode)
        {
            return StoryCsvService.BuildSectionCsvChapterBaseName(chapterCode);
        }

        private static string BuildSectionCsvFileBaseName(string chapterCode, int section)
        {
            return StoryCsvService.BuildSectionCsvFileBaseName(chapterCode, section);
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
