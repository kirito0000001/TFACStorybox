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
        private bool NormalizeStoryChoiceFunctionPrefixes(out StoryChoiceNoteState? migratedChoiceNotes)
        {
            migratedChoiceNotes = null;
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                return false;
            }

            var chapterBaseCode = RemoveProjectCodePrefix(StoryCsvService.RemoveChapterSectionSuffix(_currentStoryChapter.Code), _currentProject?.Code);
            if (string.IsNullOrWhiteSpace(chapterBaseCode))
            {
                return false;
            }

            var changed = false;
            var choiceRemap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            for (var rowIndex = 0; rowIndex < _storyRows.Count; rowIndex++)
            {
                var row = _storyRows[rowIndex];
                var functions = StoryFunctionService.SplitFunctionValues(row.Get("Custom")).ToList();
                var rowChanged = false;
                var targetPrefix = StoryFunctionService.BuildChapterSectionChoicePrefix(
                    _currentStoryChapter.Code,
                    GetStorySectionAtRowIndex(rowIndex),
                    _currentProject?.Code);
                for (var functionIndex = 0; functionIndex < functions.Count; functionIndex++)
                {
                    var match = Regex.Match(
                        functions[functionIndex],
                        $"^{Regex.Escape(chapterBaseCode)}-(?<section>\\d{{2}})-Choice(?<index>\\d+)$",
                        RegexOptions.IgnoreCase);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var normalized = $"{targetPrefix}{match.Groups["index"].Value}";
                    if (string.Equals(functions[functionIndex], normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var oldChoice = functions[functionIndex];
                    if (!choiceRemap.TryGetValue(oldChoice, out var targets))
                    {
                        targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        choiceRemap[oldChoice] = targets;
                    }

                    targets.Add(normalized);
                    functions[functionIndex] = normalized;
                    rowChanged = true;
                }

                if (rowChanged)
                {
                    row.Set("Custom", string.Join("/", functions));
                    changed = true;
                }
            }

            if (!changed || _currentStoryChapter is null)
            {
                return changed;
            }

            var noteState = _storyEditorViewModel.Document.ChoiceNotes.Clone();
            var notesChanged = false;
            foreach (var pair in choiceRemap)
            {
                if (!noteState.Choices.TryGetValue(pair.Key, out var notes))
                {
                    continue;
                }

                foreach (var newChoice in pair.Value)
                {
                    if (!noteState.Choices.ContainsKey(newChoice))
                    {
                        noteState.Choices[newChoice] = notes.ToList();
                    }
                }

                notesChanged |= noteState.Choices.Remove(pair.Key);
            }

            migratedChoiceNotes = notesChanged ? noteState : null;
            return true;
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
            if (!CanEditStoryData() || _currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var removedChoices = GetCurrentStoryChoiceValues();
            if (!string.IsNullOrWhiteSpace(_storyRows[_currentStoryRowIndex].Get("Custom")))
            {
                CaptureStoryUndoState("清空当前句函数");
            }

            _storyRows[_currentStoryRowIndex].Set("Custom", string.Empty);
            var migratedChoiceNotes = RemoveUnusedStoryChoiceNotes(removedChoices);
            QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            UpdateStoryToolbarCurrentInfo();
            ClearStoryFunctionTips();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已清空", "当前句的 Custom 字段已清空。");
        }

        private void ClearCurrentStoryRow()
        {
            if (!CanEditStoryData() || _currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var currentName = _storyRows[_currentStoryRowIndex].Get("Name");
            var removedChoices = GetCurrentStoryChoiceValues();
            CaptureStoryUndoState("清空当前行数据");
            var row = StoryDocument.CreateEmptyRow();
            row.StableId = _storyRows[_currentStoryRowIndex].StableId;
            row.Set("Name", currentName);
            _storyRows[_currentStoryRowIndex] = row;
            var migratedChoiceNotes = RemoveUnusedStoryChoiceNotes(removedChoices);
            QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            ClearStoryFunctionTips();
            StopStoryEditorAudio();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            ShowStoryStatus(InfoBarSeverity.Success, "当前行已清空", "已保留句子位置并重置当前行数据。");
        }

        private async Task ChooseStoryFunctionAsync()
        {
            if (!CanEditStoryData() || _currentStoryDocumentPath is null || _storyRows.Count == 0)
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

            _pendingStoryChoiceNotes = null;
            CaptureStoryUndoState("添加函数");
            var functionValue = await BuildStoryFunctionValueAsync(function);
            if (functionValue is null)
            {
                _storyEditorViewModel.UndoService.CancelPending();
                _pendingStoryChoiceNotes = null;
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var currentValue = row.Get("Custom").Trim();
            row.Set("Custom", string.IsNullOrWhiteSpace(currentValue) ? functionValue : $"{currentValue}/{functionValue}");
            QueueCurrentStoryDocumentSave(choiceNotes: _pendingStoryChoiceNotes);
            _pendingStoryChoiceNotes = null;
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已填写", functionValue);
            await ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private async Task RemoveStoryFunctionAsync()
        {
            if (!CanEditStoryData() || _currentStoryDocumentPath is null || _storyRows.Count == 0)
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
            var migratedChoiceNotes = RemoveUnusedStoryChoiceNotes([removed]);
            QueueCurrentStoryDocumentSave(choiceNotes: migratedChoiceNotes);
            UpdateStoryToolbarCurrentInfo();
            ClearStoryFunctionTips();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已移除", removed);
            await ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private List<FunctionEntry> GetStoryFunctions()
        {
            return _currentStoryAssetLibrary is null
                ? []
                : StoryFunctionService.OrderByFixedUsage(
                    StoryFunctionService.ReadFunctions(_currentStoryAssetLibrary, _jsonOptions));
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
                    _storyRowSections.Values
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

            var state = _storyEditorViewModel.Document.ChoiceNotes.Clone();
            state.Choices[choiceIndicator] = optionNotes
                .Select(NormalizeFunctionChoiceNote)
                .ToList();
            _pendingStoryChoiceNotes = state;
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

            var state = _storyEditorViewModel.Document.ChoiceNotes;
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

        private StoryChoiceNoteState? RemoveUnusedStoryChoiceNotes(IEnumerable<string> removedFunctions)
        {
            if (_currentStoryChapter is null)
            {
                return null;
            }

            var removedChoices = removedFunctions
                .Where(IsCurrentStoryChoiceFunctionValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (removedChoices.Count == 0)
            {
                return null;
            }

            var state = _storyEditorViewModel.Document.ChoiceNotes.Clone();
            var changed = false;
            foreach (var choice in removedChoices.Where(choice => !StoryChoiceExistsInRows(choice)))
            {
                changed |= state.Choices.Remove(choice);
            }

            return changed ? state : null;
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
            if (!CanEditStoryData() || _currentStoryDocumentPath is null || _storyRows.Count == 0)
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

            var displayName = StoryAssetFieldService.GetDisplayName(fieldName);
            var replacementPlan = StoryAssetFieldService.BuildForwardContiguousPlan(
                _storyRows,
                _currentStoryRowIndex,
                fieldName,
                selectedChoice.Index);
            if (replacementPlan.Count > 1)
            {
                var stopText = replacementPlan.StopReason switch
                {
                    StoryAssetReplacementStopReason.DifferentValue =>
                        $"到第 {replacementPlan.StoppingIndex!.Value + 1} 句遇到不同素材“{FormatStoryAssetChoiceValue(choices, replacementPlan.StoppingValue!.Value)}”后停止。",
                    StoryAssetReplacementStopReason.EndOfChapter => "连续范围一直到本章最后一句。",
                    _ => string.Empty
                };
                var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                    $"连续更换{displayName}",
                    $"将第 {replacementPlan.StartIndex + 1}-{replacementPlan.EndIndex + 1} 句，共 {replacementPlan.Count} 句，从“{FormatStoryAssetChoiceValue(choices, replacementPlan.OriginalIndex)}”改为“{FormatStoryAssetChoiceValue(choices, replacementPlan.ReplacementIndex)}”。\n{stopText}",
                    "连续更换",
                    "取消"));
                if (!confirmed)
                {
                    return;
                }
            }

            CaptureStoryUndoState($"连续更换{displayName}");
            var changedCount = StoryAssetFieldService.ApplyForwardContiguousPlan(
                _storyRows,
                replacementPlan);
            QueueCurrentStoryDocumentSave();
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

            var endRowNumber = replacementPlan.EndIndex + 1;
            ShowStoryStatus(
                InfoBarSeverity.Success,
                $"{displayName}已连续更换",
                $"共修改 {changedCount} 句（第 {_currentStoryRowIndex + 1}-{endRowNumber} 句）。");
        }

        private static string FormatStoryAssetChoiceValue(
            IReadOnlyList<StoryAssetChoice> choices,
            int index)
        {
            var choice = choices.FirstOrDefault(item => item.Index == index);
            return choice is null ? index.ToString(CultureInfo.InvariantCulture) : $"{choice.Index}: {choice.Name}";
        }

    }
}
