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
            var nextChoiceIndex = 1;
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

                    var normalized = $"{targetPrefix}{nextChoiceIndex.ToString(CultureInfo.InvariantCulture)}";
                    nextChoiceIndex++;
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
                    StoryBranchService.RemapChoiceFunction(
                        _storyEditorViewModel.Document,
                        oldChoice,
                        normalized);
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
            var backgroundIndex = ParseInt(row.Get("BGindex"));
            var bgmIndex = ParseInt(row.Get("BGM"));
            var sceneIndex = ParseInt(row.Get("Scene"));
            var backgroundChoices = GetStoryBackgroundChoices();
            var bgmChoices = GetStoryBgmChoices();
            var sceneChoices = GetStorySceneChoices();
            _storyEditorViewModel.CurrentBackgroundText = FormatStoryAssetStatus("当前背景图", backgroundIndex, backgroundChoices);
            _storyEditorViewModel.CurrentBgmText = FormatStoryAssetStatus("当前BGM", bgmIndex, bgmChoices);
            _storyEditorViewModel.CurrentSceneText = FormatStoryAssetStatus("当前环境音", sceneIndex, sceneChoices);
            _storyEditorViewModel.CurrentBackgroundValue = FormatStoryAssetValue(backgroundIndex, backgroundChoices);
            _storyEditorViewModel.CurrentBgmValue = FormatStoryAssetValue(bgmIndex, bgmChoices);
            _storyEditorViewModel.CurrentSceneValue = FormatStoryAssetValue(sceneIndex, sceneChoices);
            var custom = row.Get("Custom").Trim();
            var hasFunction = !string.IsNullOrWhiteSpace(custom);
            _storyEditorViewModel.CurrentFunctionText = string.IsNullOrWhiteSpace(custom)
                ? "当前函数：无"
                : $"当前函数：{custom}";
            _storyEditorViewModel.HasCurrentFunction = hasFunction;
            _storyEditorViewModel.HasMultipleCurrentFunctions = StoryFunctionService.SplitFunctionValues(custom).Skip(1).Any();
            _storyEditorViewModel.HasCurrentChoices = GetCurrentStoryChoiceValues().Count > 0;
            _storyEditorViewModel.SetCurrentBranchItems(
                StoryBranchService.GetRoutesForSection(
                    _storyEditorViewModel.Document,
                    GetCurrentStorySection()));
            UpdateStoryFunctionInspector(custom);
        }

        private void RefreshCurrentStoryFunctionStateAfterEdit()
        {
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            UpdateStoryToolbarCurrentInfo();
            RefreshCurrentStoryPersistentStatus();
        }

        private static string FormatStoryAssetStatus(string label, int rawIndex, IReadOnlyList<StoryAssetChoice> choices)
        {
            return StoryFunctionService.FormatAssetStatus(label, rawIndex, choices, ResolveStoryAssetIndex);
        }

        private static string FormatStoryAssetValue(int rawIndex, IReadOnlyList<StoryAssetChoice> choices)
        {
            var resolvedIndex = ResolveStoryAssetIndex(rawIndex, choices.Count);
            return resolvedIndex is null
                ? "无"
                : $"{choices[resolvedIndex.Value].Index}: {choices[resolvedIndex.Value].Name}";
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
            RefreshCurrentStoryFunctionStateAfterEdit();
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

            var function = await _functionDialogService.SelectFunctionAsync(
                "填写函数",
                functions,
                BuildNextStoryChoiceFunctionIndicator());
            if (function is null)
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
            RefreshCurrentStoryFunctionStateAfterEdit();
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
            RefreshCurrentStoryFunctionStateAfterEdit();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已移除", removed);
            await ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private List<FunctionEntry> GetStoryFunctions()
        {
            return _currentStoryAssetLibrary is null
                ? []
                : StoryFunctionService.OrderByUsageFrequency(
                    StoryFunctionService.ReadFunctions(_currentStoryAssetLibrary, _jsonOptions));
        }

        private async Task<string?> BuildStoryFunctionValueAsync(FunctionEntry function)
        {
            var indicator = function.Indicator.Trim();
            if (StoryFunctionService.IsChoiceFunctionTemplate(function))
            {
                var choiceIndicator = BuildNextStoryChoiceFunctionIndicator();
                var choiceResult = await ShowStoryChoiceEditorAsync(
                    choiceIndicator,
                    StoryBranchService.GetTargetOptions(_storyEditorViewModel.Document));
                if (choiceResult is null)
                {
                    return null;
                }

                SaveChoiceFunctionDefinition(choiceIndicator, choiceResult);
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

            if (function.Branches.Count > 0)
            {
                return await _storyDialogService.SelectSimpleChoiceAsync(
                    function.Name,
                    StoryFunctionService.CreateFunctionBranchChoices(function)) as string;
            }
            if (function.BranchMode is FunctionBranchMode.Fixed or FunctionBranchMode.AssetIndex)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "函数没有可用分支", $"请先到素材库为 {function.Name} 添加分支。");
                return null;
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

        private void SaveChoiceFunctionDefinition(
            string choiceIndicator,
            StoryChoiceEditorResult choiceResult)
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            var state = _storyEditorViewModel.Document.ChoiceNotes.Clone();
            state.Choices[choiceIndicator] = choiceResult.Options
                .Select(option => NormalizeFunctionChoiceNote(option.Text))
                .ToList();
            StoryBranchService.ReplaceRoutesForChoice(
                _storyEditorViewModel.Document,
                choiceIndicator,
                choiceResult.Options);
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

            if (_isStoryDebugModeEnabled)
            {
                await _storyDialogService.ShowCurrentChoicesAsync(choices, GetChoiceFunctionNoteMap());
                return;
            }

            var choice = choices.Count == 1
                ? choices[0]
                : await _storyDialogService.SelectSimpleChoiceAsync(
                    "选择要编辑的 Choice",
                    choices.Select((value, index) => new StoryObjectChoice(index.ToString(), value, value)).ToList()) as string;
            if (string.IsNullOrWhiteSpace(choice))
            {
                return;
            }

            var document = _storyEditorViewModel.Document;
            var notes = document.ChoiceNotes.Choices.TryGetValue(choice, out var storedNotes)
                ? storedNotes
                : [];
            var routes = document.BranchRoutes
                .Where(route => string.Equals(route.ChoiceFunction, choice, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(route => route.OptionIndex, route => route.TargetSectionFirstRowId);
            var initialOptions = notes
                .Select((text, index) => new StoryChoiceEditorOption(
                    text,
                    routes.TryGetValue(index, out var targetId) ? targetId : string.Empty))
                .ToList();
            var result = await ShowStoryChoiceEditorAsync(
                choice,
                StoryBranchService.GetTargetOptions(document),
                initialOptions);
            if (result is null)
            {
                return;
            }

            CaptureStoryUndoState("编辑触发选项");
            var state = document.ChoiceNotes.Clone();
            state.Choices[choice] = result.Options
                .Select(option => NormalizeFunctionChoiceNote(option.Text))
                .ToList();
            StoryBranchService.ReplaceRoutesForChoice(document, choice, result.Options);
            QueueCurrentStoryDocumentSave(choiceNotes: state);
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(InfoBarSeverity.Success, "选项已更新", choice);
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
                StoryBranchService.RemoveRoutesForChoices(_storyEditorViewModel.Document, [choice]);
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
            var replacementChoice = await ConfirmStoryAssetReplacementAsync(
                replacementPlan,
                displayName,
                index => FormatStoryAssetChoiceValue(choices, index));
            if (replacementChoice is DialogResultKind.None or DialogResultKind.Cancel)
            {
                return;
            }

            if (replacementChoice == DialogResultKind.Secondary)
            {
                // 「只换一张」：把范围压成当前这一句 —— 后面那段应用逻辑照旧。
                replacementPlan = replacementPlan with
                {
                    EndIndex = replacementPlan.StartIndex,
                    Count = 1
                };
            }

            CaptureStoryUndoState(replacementPlan.Count > 1 ? $"连续更换{displayName}" : $"更换{displayName}");
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
            // 文案跟着实际范围走：选「只换一张」时 Count 已经被压成 1，
            // 再报"已连续更换"就名不副实了（另外两个调用点本来就是这个口径）。
            ShowStoryStatus(
                InfoBarSeverity.Success,
                changedCount > 1 ? $"{displayName}已连续更换" : $"{displayName}已更换",
                changedCount > 1
                    ? $"共修改 {changedCount} 句（第 {_currentStoryRowIndex + 1}-{endRowNumber} 句）。"
                    : $"第 {replacementPlan.StartIndex + 1} 句。");
        }

        /// <summary>
        /// 连续更换前的确认。**三种结果**，不再是"确认/取消"两种：
        ///   - <see cref="DialogResultKind.Primary"/>：连续更换（整段）；
        ///   - <see cref="DialogResultKind.Secondary"/>：只换当前这一句；
        ///   - <see cref="DialogResultKind.None"/>：关掉了，什么都不做。
        ///
        /// ⚠️ 这里有个容易搞错的地方：以前那个「取消」按钮**不是 Secondary**，
        /// 而是 <c>DialogRequest</c> 的第 4 个参数 <c>CloseButtonText</c>（关闭键）——
        /// 它和右上角的 X 是同一个东西。所以那时候弹窗上只有两个按钮，
        /// 也没有"只换一张"这个选项。
        ///
        /// 现在关掉键传空串：按项目的规范，弹窗可以**右键关闭、Esc 关闭**，
        /// 不需要可视的关闭键，而且有撤回兜底。
        /// </summary>
        /// <summary>
        /// 粘贴那条路要不要每次都问（判据在下面两个确认方法的开头）。
        /// 用户 2026-10-16 的要求：Ctrl+V 之后要能自己选连续更换还是只换一句。
        /// </summary>
        private bool _alwaysAskStoryReplacementConfirm;

        private async Task<DialogResultKind> ConfirmStoryAssetReplacementAsync(
            StoryAssetReplacementPlan replacementPlan,
            string displayName,
            Func<int, string> formatValue)
        {
            if (!_alwaysAskStoryReplacementConfirm && replacementPlan.Count <= 1)
            {
                // 范围内本来就只有一句，"只换一张"和"连续更换"是同一件事，不必问。
                return DialogResultKind.Primary;
            }

            var stopText = replacementPlan.StopReason switch
            {
                StoryAssetReplacementStopReason.DifferentValue =>
                    $"到第 {replacementPlan.StoppingIndex!.Value + 1} 句遇到不同素材“{formatValue(replacementPlan.StoppingValue!.Value)}”后停止。",
                StoryAssetReplacementStopReason.DifferentContext =>
                    $"到第 {replacementPlan.StoppingIndex!.Value + 1} 句角色或服装发生变化后停止。",
                StoryAssetReplacementStopReason.EndOfChapter => "连续范围一直到本章最后一句。",
                _ => string.Empty
            };
            var result = await _dialogService.ShowAsync(new DialogRequest(
                $"连续更换{displayName}",
                $"将第 {replacementPlan.StartIndex + 1}-{replacementPlan.EndIndex + 1} 句，共 {replacementPlan.Count} 句，从“{formatValue(replacementPlan.OriginalIndex)}”改为“{formatValue(replacementPlan.ReplacementIndex)}”。\n{stopText}\n\n也可以只换当前这一句（第 {replacementPlan.StartIndex + 1} 句）。",
                "连续更换",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "只换一张"));
            // 右键 / Esc 关掉时是 None —— 什么都不做，和以前"取消"一个意思。
            return result;
        }

        private async Task<DialogResultKind> ConfirmStoryValueReplacementAsync(
            StoryValueReplacementPlan replacementPlan,
            string displayName,
            Func<string, string> formatValue)
        {
            if (!_alwaysAskStoryReplacementConfirm && replacementPlan.Count <= 1)
            {
                return DialogResultKind.Primary;
            }

            var stopText = replacementPlan.StopReason switch
            {
                StoryAssetReplacementStopReason.DifferentValue =>
                    $"到第 {replacementPlan.StoppingIndex!.Value + 1} 句遇到不同角色“{formatValue(replacementPlan.StoppingValue ?? string.Empty)}”后停止。",
                StoryAssetReplacementStopReason.EndOfChapter => "连续范围一直到本章最后一句。",
                _ => string.Empty
            };
            var result = await _dialogService.ShowAsync(new DialogRequest(
                $"连续更换{displayName}",
                $"将第 {replacementPlan.StartIndex + 1}-{replacementPlan.EndIndex + 1} 句，共 {replacementPlan.Count} 句，从“{formatValue(replacementPlan.OriginalValue)}”改为“{formatValue(replacementPlan.ReplacementValue)}”。\n{stopText}\n\n也可以只换当前这一句（第 {replacementPlan.StartIndex + 1} 句）。",
                "连续更换",
                CloseButtonText: string.Empty,
                SecondaryButtonText: "只换一张"));
            return result;
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
