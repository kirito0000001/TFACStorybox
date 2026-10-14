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
        private async Task PlayCurrentStoryVoiceFromUiAsync()
        {
            StopStoryVoicePlayback();
            if (_currentProject is null || _currentStoryChapter is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var rowKey = ProjectTextDataService.BuildStableTextRowId(
                _currentStoryChapter.Code,
                row.StableId);
            if (!_projectVoiceMapState.Voices.TryGetValue(rowKey, out var voicePath) ||
                string.IsNullOrWhiteSpace(voicePath) ||
                !File.Exists(voicePath))
            {
                return;
            }

            try
            {
                await _storyVoicePlayback.PlayAsync(voicePath, restartCurrent: true);
            }
            catch (Exception ex)
            {
                ShowStoryStatus(
                    InfoBarSeverity.Error,
                    "语音播放失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"故事编辑器语音播放失败：{rowKey}");
            }
        }

        private void StopStoryVoicePlayback()
        {
            _storyVoicePlayback.Stop();
        }

        private async Task PlayCurrentStoryBgmFromUiAsync()
        {
            if (IsCurrentStoryFunction("BGM_Stop"))
            {
                _storyBgmPlaybackSuppressed = true;
                PauseStoryBgm();
                return;
            }

            if (_storyBgmPlaybackSuppressed)
            {
                return;
            }

            try
            {
                await PlayCurrentStoryBgmAsync();
            }
            catch (Exception ex)
            {
                ShowStoryStatus(
                    InfoBarSeverity.Error,
                    "BGM播放失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "故事编辑器 BGM 播放失败。");
            }
        }

        private async Task ApplyCurrentStoryRowMediaAndFunctionAsync()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var functionValue = _storyRows[_currentStoryRowIndex].Get("Custom").Trim();
            foreach (var function in StoryFunctionService.SplitFunctionValues(functionValue))
            {
                if (StoryFunctionService.TryParseBackgroundTransitionMode(function, out var transitionMode))
                {
                    _storyBackgroundTransitionMode = transitionMode;
                }
            }

            _storySoundEffectPlayback.Stop();
            await PlayStorySoundEffectFunctionsAsync(functionValue);

            if (StoryFunctionService.ContainsExactFunction(functionValue, "BGM_Stop"))
            {
                _storyBgmPlaybackSuppressed = true;
                PauseStoryBgm();
                ShowStoryFunctionTriggeredStatus(functionValue);
                await PlayCurrentStorySceneFromUiAsync();
                return;
            }

            await PlayCurrentStoryBgmFromUiAsync();
            await PlayCurrentStorySceneFromUiAsync();
            if (!string.IsNullOrWhiteSpace(functionValue))
            {
                await PreviewStoryFunctionAsync(functionValue);
            }
        }

        private async Task PlayStorySoundEffectFunctionsAsync(string functionValue)
        {
            if (_currentStoryAssetLibrary is null)
            {
                return;
            }

            var soundEffectPaths = AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(_currentStoryAssetLibrary));
            foreach (var function in StoryFunctionService.SplitFunctionValues(functionValue))
            {
                if (!StoryFunctionService.TryParseSoundEffectIndex(function, out var index))
                {
                    continue;
                }

                if (index < 0 || index >= soundEffectPaths.Count)
                {
                    ShowStoryStatus(
                        InfoBarSeverity.Warning,
                        "特殊音效未播放",
                        $"Scene 索引 {index} 没有匹配到素材，当前共有 {soundEffectPaths.Count} 个特殊音效。");
                    continue;
                }

                try
                {
                    await _storySoundEffectPlayback.PlayAsync(soundEffectPaths[index], restartCurrent: true);
                }
                catch (Exception ex)
                {
                    ShowStoryStatus(
                        InfoBarSeverity.Error,
                        "特殊音效播放失败",
                        "错误详情已复制到剪贴板，并写入 Log。",
                        ex,
                        $"故事编辑器特殊音效播放失败：Scene_{index}");
                }
            }
        }

        private async Task PreviewStoryFunctionAsync(string functionValue)
        {
            ShowStoryFunctionTriggeredStatus(functionValue);
            if (StoryFunctionService.ContainsExactFunction(functionValue, "BGM_Start"))
            {
                _storyBgmPlaybackSuppressed = false;
                await PlayCurrentStoryBgmFromUiAsync();
            }
        }

        private bool IsCurrentStoryFunction(string functionName)
        {
            return _storyRows.Count > 0 &&
                StoryFunctionService.ContainsExactFunction(_storyRows[_currentStoryRowIndex].Get("Custom"), functionName);
        }

        private void ShowStoryFunctionTriggeredStatus(string functionValue)
        {
            UpdateStoryFunctionInspector(functionValue);
        }

        private void ClearStoryFunctionTips()
        {
            _storyEditorViewModel.SetCurrentFunctionItems([]);
            _storyEditorViewModel.HasCurrentTransitionFunction = false;
            _storyEditorViewModel.CurrentTransitionText =
                $"沿用：{StoryFunctionService.GetBackgroundTransitionModeDisplay(_storyBackgroundTransitionMode)}";
        }

        private void ClearStoryTransientTips()
        {
            ClearFloatingTips(StoryFloatingTipsPanel);
            _persistentStoryStatusTip = null;
        }

        private void RefreshCurrentStoryPersistentStatus()
        {
            ClearStoryTransientTips();
            if (_storyRows.Count == 0)
            {
                return;
            }

            int? currentTransitionMode = null;
            var functionValue = _storyRows[_currentStoryRowIndex].Get("Custom");
            foreach (var function in StoryFunctionService.SplitFunctionValues(functionValue))
            {
                if (StoryFunctionService.TryParseBackgroundTransitionMode(function, out var transitionMode))
                {
                    currentTransitionMode = transitionMode;
                }
            }

            if (currentTransitionMode is int mode)
            {
                ShowPersistentStoryStatus(
                    InfoBarSeverity.Informational,
                    "背景切换模式",
                    StoryFunctionService.GetBackgroundTransitionModeRemark(mode));
            }
        }

        private void UpdateStoryFunctionInspector(string functionValue)
        {
            var functionNames = new List<string>();
            var registeredFunctions = GetStoryFunctions();
            var hasCurrentTransition = false;
            foreach (var function in StoryFunctionService.SplitFunctionValues(functionValue))
            {
                if (StoryFunctionService.TryParseBackgroundTransitionMode(function, out var transitionMode))
                {
                    _storyBackgroundTransitionMode = transitionMode;
                    hasCurrentTransition = true;
                    functionNames.Add(StoryFunctionService.IsRegisteredFunctionValue(function, registeredFunctions)
                        ? function
                        : $"{function}（未登记函数）");
                    continue;
                }

                var displayNames = StoryFunctionService.EnumerateFunctionDisplayNames(function);
                functionNames.AddRange(StoryFunctionService.IsRegisteredFunctionValue(function, registeredFunctions)
                    ? displayNames
                    : displayNames.Select(name => $"{name}（未登记函数）"));
            }

            _storyEditorViewModel.CurrentTransitionText = hasCurrentTransition
                ? $"本句先应用：{StoryFunctionService.GetBackgroundTransitionModeDisplay(_storyBackgroundTransitionMode)}"
                : $"沿用：{StoryFunctionService.GetBackgroundTransitionModeDisplay(_storyBackgroundTransitionMode)}";
            _storyEditorViewModel.HasCurrentTransitionFunction = hasCurrentTransition;
            _storyEditorViewModel.SetCurrentFunctionItems(functionNames);
        }

        /// <summary>
        /// 「到第 i 句为止」的两个持久状态，按行存一份。
        ///
        /// 【为什么要有这张表】
        /// 原来每换一句都从第 0 句重扫到当前句（`HasPreviousBgmStopWithoutStart` +
        /// `GetStoryBackgroundTransitionMode` 各扫一遍，第二遍每句还要拆字符串）——
        /// 那是 **O(n) 且同步跑在 UI 线程上**。连点 k 次就是 O(k²)：
        /// 第 120 句时点一次要 240 次迭代 + 120 次字符串拆分，
        /// 越点越卡，卡到界面不响应（用户报的「快速切会卡很久」就是这个）。
        ///
        /// 这两个值都是"从第 0 句累加到当前句"的性质，所以整表算**一次**就够，
        /// 之后每次换句只是查一下数组 —— O(1)。
        ///
        /// 【什么时候失效】
        /// 只在**用户改动**时失效，换句不失效：
        /// - 行数变了（插入 / 删除 / 换章）→ 长度对不上，自动重建；
        /// - 编辑了某一句 → SaveCurrentStoryRow 里显式清掉；
        /// - 载入新章节 → LoadStoryDocument 里显式清掉。
        /// 换句路径上一次都不清，所以连点几百次也只建过一次表。
        /// </summary>
        private bool[]? _storyBgmSuppressionByRow;

        private int[]? _storyTransitionModeByRow;

        /// <summary>文档/行被改动时调用 —— 下一次需要时会整表重建。</summary>
        private void InvalidateStoryPersistentStateCache()
        {
            _storyBgmSuppressionByRow = null;
            _storyTransitionModeByRow = null;
        }

        /// <summary>确保前缀状态表是新的（按需整表重建，O(n) 只发生一次）。</summary>
        private void EnsureStoryPersistentStateCache()
        {
            if (_storyBgmSuppressionByRow is not null &&
                _storyTransitionModeByRow is not null &&
                _storyBgmSuppressionByRow.Length == _storyRows.Count)
            {
                return;
            }

            var suppression = new bool[_storyRows.Count];
            var transitionMode = new int[_storyRows.Count];
            var suppressed = false;
            var mode = 0;
            for (var i = 0; i < _storyRows.Count; i++)
            {
                // 一次遍历同时算两个状态 —— 原来那两遍扫描本来也是同一份数据。
                var functionValue = _storyRows[i].Get("Custom");
                if (StoryFunctionService.ContainsExactFunction(functionValue, "BGM_Stop"))
                {
                    suppressed = true;
                }
                else if (StoryFunctionService.ContainsExactFunction(functionValue, "BGM_Start"))
                {
                    suppressed = false;
                }

                foreach (var item in StoryFunctionService.SplitFunctionValues(functionValue))
                {
                    if (StoryFunctionService.TryParseBackgroundTransitionMode(item, out var parsedMode))
                    {
                        mode = parsedMode;
                    }
                }

                suppression[i] = suppressed;
                transitionMode[i] = mode;
            }

            _storyBgmSuppressionByRow = suppression;
            _storyTransitionModeByRow = transitionMode;
        }

        private void RebuildStoryPersistentFunctionState(int rowIndex)
        {
            EnsureStoryPersistentStateCache();
            if (_storyRows.Count == 0)
            {
                _storyBgmPlaybackSuppressed = false;
                _storyBackgroundTransitionMode = 0;
                return;
            }

            var index = Math.Clamp(rowIndex, 0, _storyRows.Count - 1);
            _storyBgmPlaybackSuppressed = _storyBgmSuppressionByRow![index];
            _storyBackgroundTransitionMode = _storyTransitionModeByRow![index];
            if (_storyBgmPlaybackSuppressed)
            {
                PauseStoryBgm();
            }
        }

        private async Task PlayCurrentStoryBgmAsync()
        {
            if (_storyBgmPlaybackSuppressed || IsCurrentStoryFunction("BGM_Stop"))
            {
                PauseStoryBgm();
                return;
            }

            if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
            {
                StopStoryBgm();
                ShowStoryStatus(InfoBarSeverity.Warning, "BGM未播放", "当前项目没有可用的绑定素材库。");
                return;
            }

            var bgmPaths = AudioAssetService.GetFilePaths(GetMusicFolderPath(_currentStoryAssetLibrary));
            var rawIndex = ParseInt(_storyRows[_currentStoryRowIndex].Get("BGM"));
            var index = ResolveStoryAssetIndex(rawIndex, bgmPaths.Count);
            if (index is null)
            {
                StopStoryBgm();
                ShowStoryStatus(InfoBarSeverity.Warning, "BGM未播放", $"BGM 索引 {rawIndex} 没有匹配到素材。");
                return;
            }

            var bgmPath = bgmPaths[index.Value];
            if (_storyBgmPlaybackSuppressed || IsCurrentStoryFunction("BGM_Stop"))
            {
                PauseStoryBgm();
                return;
            }

            await _storyBgmPlayback.PlayAsync(bgmPath);
        }

        private void StopStoryBgm()
        {
            _storyBgmPlayback.Stop();
        }

        private void PauseStoryBgm()
        {
            _storyBgmPlayback.Pause();
        }

        private async Task PlayCurrentStorySceneFromUiAsync()
        {
            try
            {
                await PlayCurrentStorySceneAsync();
            }
            catch (Exception ex)
            {
                ShowStoryStatus(
                    InfoBarSeverity.Error,
                    "环境音播放失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "故事编辑器环境音播放失败。");
            }
        }

        private async Task PlayCurrentStorySceneAsync()
        {
            if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
            {
                StopStoryScene();
                return;
            }

            var scenePaths = AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(_currentStoryAssetLibrary));
            var rawIndex = ParseInt(_storyRows[_currentStoryRowIndex].Get("Scene"));
            var index = ResolveStoryAssetIndex(rawIndex, scenePaths.Count);
            if (index is null)
            {
                StopStoryScene();
                if (rawIndex > 0 || scenePaths.Count > 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, "环境音未播放", $"Scene 索引 {rawIndex} 没有匹配到素材。");
                }
                return;
            }

            var scenePath = scenePaths[index.Value];
            await _storyScenePlayback.PlayAsync(scenePath);
        }

        private void StopStoryScene()
        {
            _storyScenePlayback.Stop();
        }

        private void StopStoryEditorAudio()
        {
            _storyBgmPlaybackSuppressed = false;
            StopStoryBgm();
            StopStoryScene();
            StopStoryVoicePlayback();
            _storySoundEffectPlayback.Stop();
        }

    }
}
