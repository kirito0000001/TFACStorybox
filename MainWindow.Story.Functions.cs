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
        private void NormalizeCurrentStoryChoiceFunctionPrefix()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var functions = StoryFunctionService.SplitFunctionValues(row.Get("Custom")).ToList();
            if (functions.Count == 0)
            {
                return;
            }

            var chapterBaseCode = RemoveProjectCodePrefix(StoryCsvService.RemoveChapterSectionSuffix(_currentStoryChapter.Code), _currentProject?.Code);
            if (string.IsNullOrWhiteSpace(chapterBaseCode))
            {
                return;
            }

            var targetPrefix = BuildCurrentStoryChapterSectionChoicePrefix();
            var changed = false;
            var oldChoices = new List<string>();
            var newChoices = new List<string>();
            for (var i = 0; i < functions.Count; i++)
            {
                var match = Regex.Match(
                    functions[i],
                    $"^{Regex.Escape(chapterBaseCode)}-(?<section>\\d{{2}})-Choice(?<index>\\d+)$",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                var normalized = $"{targetPrefix}{match.Groups["index"].Value}";
                if (string.Equals(functions[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                oldChoices.Add(functions[i]);
                newChoices.Add(normalized);
                functions[i] = normalized;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            row.Set("Custom", string.Join("/", functions));
            PersistCurrentStoryRowsToFiles();
            for (var i = 0; i < oldChoices.Count; i++)
            {
                CopyStoryChoiceNotes(oldChoices[i], newChoices[i]);
            }

            RemoveUnusedStoryChoiceNotes(oldChoices);
        }

        private void UpdateStoryEditorHeader()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            _storyEditorViewModel.Title = $"{_currentStoryChapter.Name} / {BuildCurrentStoryChapterSectionCode()}";
        }

        private void UpdateStoryToolbarCurrentInfo()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyEditorViewModel.CurrentBackgroundText = FormatStoryAssetStatus("当前背景图", ParseInt(row.Get("BGindex")), GetStoryBackgroundChoices());
            _storyEditorViewModel.CurrentBgmText = FormatStoryAssetStatus("当前BGM", ParseInt(row.Get("BGM")), GetStoryBgmChoices());
            _storyEditorViewModel.CurrentSceneText = FormatStoryAssetStatus("当前环境音", ParseInt(row.Get("Scene")), GetStorySceneChoices());
            var custom = row.Get("Custom").Trim();
            var hasFunction = !string.IsNullOrWhiteSpace(custom);
            _storyEditorViewModel.CurrentFunctionText = string.IsNullOrWhiteSpace(custom)
                ? "当前函数：无"
                : $"当前函数：{custom}";
            _storyEditorViewModel.HasCurrentFunction = hasFunction;
            _storyEditorViewModel.HasMultipleCurrentFunctions = StoryFunctionService.SplitFunctionValues(custom).Skip(1).Any();
            _storyEditorViewModel.HasCurrentChoices = GetCurrentStoryChoiceValues().Count > 0;
        }

        private static string FormatStoryAssetStatus(string label, int rawIndex, IReadOnlyList<StoryAssetChoice> choices)
        {
            return StoryFunctionService.FormatAssetStatus(label, rawIndex, choices, ResolveStoryAssetIndex);
        }

        private void ClearStoryFunction()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var removedChoices = GetCurrentStoryChoiceValues();
            if (!string.IsNullOrWhiteSpace(_storyRows[_currentStoryRowIndex].Get("Custom")))
            {
                CaptureStoryUndoState("清空当前句函数");
            }

            _storyRows[_currentStoryRowIndex].Set("Custom", string.Empty);
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes(removedChoices);
            UpdateStoryToolbarCurrentInfo();
            ClearStoryFunctionTips();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已清空", "当前句的 Custom 字段已清空。");
        }

        private void ClearCurrentStoryRow()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var currentName = _storyRows[_currentStoryRowIndex].Get("Name");
            var removedChoices = GetCurrentStoryChoiceValues();
            CaptureStoryUndoState("清空当前行数据");
            var row = _storyCsvService.CreateDefaultRow();
            row.Set("Name", currentName);
            _storyRows[_currentStoryRowIndex] = row;
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes(removedChoices);
            ClearStoryFunctionTips();
            StopStoryEditorAudio();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            ShowStoryStatus(InfoBarSeverity.Success, "当前行已清空", "已保留句子位置并重置当前行数据。");
        }

        private async Task ChooseStoryFunctionAsync()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var functions = GetStoryFunctions();
            if (functions.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可用函数", "当前项目绑定的素材库里还没有函数卡。");
                return;
            }

            var selected = await _storyDialogService.SelectSimpleChoiceAsync(
                "填写函数",
                functions
                    .Select(function => new StoryObjectChoice(
                        function.Id,
                        FormatStoryFunctionChoiceDisplay(function),
                        function))
                    .ToList());
            if (selected is not FunctionEntry function)
            {
                return;
            }

            var functionValue = await BuildStoryFunctionValueAsync(function);
            if (functionValue is null)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var currentValue = row.Get("Custom").Trim();
            CaptureStoryUndoState("添加函数");
            row.Set("Custom", string.IsNullOrWhiteSpace(currentValue) ? functionValue : $"{currentValue}/{functionValue}");
            PersistCurrentStoryRowsToFiles();
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已填写", functionValue);
            await ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private async Task RemoveStoryFunctionAsync()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var functions = StoryFunctionService.SplitFunctionValues(row.Get("Custom")).ToList();
            if (functions.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可移除函数", "当前句还没有填写函数。");
                return;
            }

            var selected = await _storyDialogService.SelectSimpleChoiceAsync(
                "移除函数",
                functions.Select((function, index) => new StoryObjectChoice(index.ToString(), function, index)).ToList());
            if (selected is not int selectedIndex || selectedIndex < 0 || selectedIndex >= functions.Count)
            {
                return;
            }

            var removed = functions[selectedIndex];
            CaptureStoryUndoState("移除函数");
            functions.RemoveAt(selectedIndex);
            row.Set("Custom", string.Join("/", functions));
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes([removed]);
            UpdateStoryToolbarCurrentInfo();
            ClearStoryFunctionTips();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已移除", removed);
            await ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private List<FunctionEntry> GetStoryFunctions()
        {
            return _currentStoryAssetLibrary is null ? [] : StoryFunctionService.ReadFunctions(_currentStoryAssetLibrary, _jsonOptions);
        }

        private string FormatStoryFunctionChoiceDisplay(FunctionEntry function)
        {
            return StoryFunctionService.BuildFunctionChoiceDisplay(function, BuildNextStoryChoiceFunctionIndicator());
        }

        private async Task<string?> BuildStoryFunctionValueAsync(FunctionEntry function)
        {
            var indicator = function.Indicator.Trim();
            if (StoryFunctionService.IsChoiceFunctionTemplate(function))
            {
                var choiceIndicator = BuildNextStoryChoiceFunctionIndicator();
                var optionNotes = await _functionDialogService.EditChoiceNotesAsync(choiceIndicator);
                if (optionNotes is null)
                {
                    return null;
                }

                SaveChoiceFunctionNotes(choiceIndicator, optionNotes);
                return choiceIndicator;
            }

            if (StoryFunctionService.IsChapterJumpFunctionTemplate(function))
            {
                return await ChooseChapterJumpFunctionValueAsync();
            }

            if (StoryFunctionService.IsSegmentJumpFunctionTemplate(function))
            {
                return await ChooseSegmentJumpFunctionValueAsync();
            }

            if (StoryFunctionService.IsBgmFunctionTemplate(function))
            {
                return await ChooseBgmFunctionValueAsync();
            }

            if (string.Equals(function.Id, "default-custom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(indicator, "CustomFunction", StringComparison.OrdinalIgnoreCase))
            {
                return await ShowNameInputDialogAsync("自定义函数", "函数指示器", string.Empty);
            }

            if (indicator.StartsWith("Scene_", StringComparison.OrdinalIgnoreCase))
            {
                var soundEffectIndex = await ChooseStorySoundEffectIndexAsync();
                return soundEffectIndex is null ? null : $"Scene_{soundEffectIndex.Value}";
            }

            if (string.Equals(indicator, "BGLerpMode_", StringComparison.OrdinalIgnoreCase))
            {
                var selected = await _storyDialogService.SelectSimpleChoiceAsync(
                    "背景切换模式",
                    StoryFunctionService.CreateBackgroundLerpModeChoices());
                return selected is int mode ? $"BGLerpMode_{mode}" : null;
            }

            if (indicator.StartsWith("VFXON_", StringComparison.OrdinalIgnoreCase) ||
                indicator.StartsWith("VFXOFF_", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = await ShowNameInputDialogAsync("特效标识", "自定义字符串", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            if (string.Equals(indicator, "TransAnim_", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = await ShowNameInputDialogAsync("动画序列索引", "索引", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            if (string.Equals(indicator, "MedPlay_", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = await ShowNameInputDialogAsync("视频索引", "索引", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            if (indicator.EndsWith('_'))
            {
                var suffix = await ShowNameInputDialogAsync("函数参数", "参数", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            return indicator;
        }

        private async Task<string?> ChooseBgmFunctionValueAsync()
        {
            var selected = await _storyDialogService.SelectSimpleChoiceAsync(
                "BGM",
                StoryFunctionService.CreateBgmChoices());
            return selected as string;
        }

        private async Task<string?> ChooseChapterJumpFunctionValueAsync()
        {
            if (_currentProject is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转章节", "当前没有打开项目。");
                return null;
            }

            List<ChapterInfo> chapters = _projectWorkspaceService.GetChapters(_currentProject)
                .OrderBy(chapter => chapter.Code)
                .ToList();
            if (chapters.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转章节", "当前项目还没有章节。");
                return null;
            }

            var choices = StoryFunctionService.CreateChapterJumpChoices(chapters, _currentProject.Code);
            return await _storyDialogService.SelectSimpleChoiceAsync("跳转章节", choices) as string;
        }

        private async Task<string?> ChooseSegmentJumpFunctionValueAsync()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转小节", "请先打开一个章节。");
                return null;
            }

            var sectionCount = GetCurrentStorySectionCount();
            var choices = StoryFunctionService.CreateSegmentJumpChoices(sectionCount);
            return await _storyDialogService.SelectSimpleChoiceAsync("跳转小节", choices) as string;
        }

        private int GetCurrentStorySectionCount()
        {
            var sectionCount = 1;
            if (_storyRowSections.Count > 0)
            {
                sectionCount = Math.Max(sectionCount, _storyRowSections.Values.DefaultIfEmpty(1).Max());
            }

            foreach (var item in StorySectionComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is int section)
                {
                    sectionCount = Math.Max(sectionCount, section);
                }
            }

            if (_currentStoryChapter is not null)
            {
                sectionCount = Math.Max(
                    sectionCount,
                    _storyCsvService.GetLocalSectionCsvPaths(_currentStoryChapter)
                        .Select(file => file.Section)
                        .DefaultIfEmpty(1)
                        .Max());
            }

            return Math.Max(1, sectionCount);
        }

        private void SaveChoiceFunctionNotes(string choiceIndicator, IReadOnlyList<string> optionNotes)
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            var state = _storyStateService.ReadChoiceNotes(_currentStoryChapter);
            state.Choices[choiceIndicator] = optionNotes
                .Select(NormalizeFunctionChoiceNote)
                .ToList();
            _storyStateService.WriteChoiceNotes(_currentStoryChapter, state);
        }

        private List<string> GetCurrentStoryChoiceValues()
        {
            if (_storyRows.Count == 0)
            {
                return [];
            }

            return StoryFunctionService.SplitFunctionValues(_storyRows[_currentStoryRowIndex].Get("Custom"))
                .Where(IsCurrentStoryChoiceFunctionValue)
                .ToList();
        }

        private bool IsCurrentStoryChoiceFunctionValue(string functionValue)
        {
            var prefix = BuildCurrentStoryChapterSectionChoicePrefix();
            return StoryFunctionService.TryParseChoiceFunctionIndex(functionValue, prefix) > 0;
        }

        private async Task ShowCurrentStoryChoicesAsync()
        {
            var choices = GetCurrentStoryChoiceValues();
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有选项", "当前句还没有触发选项。");
                return;
            }

            await _storyDialogService.ShowCurrentChoicesAsync(choices, GetChoiceFunctionNoteMap());
        }

        private Dictionary<string, List<string>> GetChoiceFunctionNoteMap()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (_currentStoryChapter is null)
            {
                return result;
            }

            var state = _storyStateService.ReadChoiceNotes(_currentStoryChapter);
            foreach (var pair in state.Choices)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    result[pair.Key] = pair.Value
                        .Select(NormalizeFunctionChoiceNote)
                        .ToList();
                }
            }

            return result;
        }

        private void CopyStoryChoiceNotes(string oldChoice, string newChoice)
        {
            if (_currentStoryChapter is null ||
                string.Equals(oldChoice, newChoice, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _storyStateService.CopyChoiceNotes(_currentStoryChapter, oldChoice, newChoice);
        }

        private void RemoveUnusedStoryChoiceNotes(IEnumerable<string> removedFunctions)
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            var removedChoices = removedFunctions
                .Where(IsCurrentStoryChoiceFunctionValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (removedChoices.Count == 0)
            {
                return;
            }

            _storyStateService.RemoveChoiceNotes(
                _currentStoryChapter,
                removedChoices.Where(choice => !StoryChoiceExistsInRows(choice)));
        }

        private bool StoryChoiceExistsInRows(string choice)
        {
            return _storyRows.Any(row => StoryFunctionService.SplitFunctionValues(row.Get("Custom"))
                .Any(function => string.Equals(function, choice, StringComparison.OrdinalIgnoreCase)));
        }

        private async Task<int?> ChooseStorySoundEffectIndexAsync()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return null;
            }

            var choices = AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryObjectChoice(index.ToString(), $"{index}: {Path.GetFileNameWithoutExtension(path)}", index))
                .ToList();
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有特殊音效", "请先在素材库里导入特殊音效。");
                return null;
            }

            var selected = await _storyDialogService.SelectSimpleChoiceAsync("选择特殊音效", choices);
            return selected is int index ? index : null;
        }

        private async Task ChooseStoryAssetIndexAsync(string title, string fieldName, List<StoryAssetChoice> choices)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可用素材", "当前项目绑定的素材库里没有对应素材。");
                return;
            }

            var currentIndex = ParseInt(_storyRows[_currentStoryRowIndex].Get(fieldName));
            var selectedChoice = await _storyDialogService.SelectAssetChoiceAsync(title, choices, currentIndex);
            if (selectedChoice is null)
            {
                return;
            }

            if (selectedChoice.Index == currentIndex)
            {
                return;
            }

            CaptureStoryUndoState($"更换{StoryAssetFieldService.GetDisplayName(fieldName)}");
            _storyRows[_currentStoryRowIndex].Set(fieldName, selectedChoice.Index.ToString());
            PersistCurrentStoryRowsToFiles();
            UpdateStoryToolbarCurrentInfo();
            await RefreshStoryPreviewAsync();
            if (fieldName == "BGM")
            {
                await ApplyCurrentStoryRowMediaAndFunctionAsync();
            }
            else if (fieldName == "Scene")
            {
                await PlayCurrentStorySceneFromUiAsync();
            }
        }

    }
}
