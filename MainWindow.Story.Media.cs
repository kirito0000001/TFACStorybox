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

        private void RebuildStoryPersistentFunctionState(int rowIndex)
        {
            _storyBgmPlaybackSuppressed = HasPreviousBgmStopWithoutStart(rowIndex);
            _storyBackgroundTransitionMode = GetStoryBackgroundTransitionMode(rowIndex);
            if (_storyBgmPlaybackSuppressed)
            {
                PauseStoryBgm();
            }
        }

        private bool HasPreviousBgmStopWithoutStart(int rowIndex)
        {
            var suppressed = false;
            for (var i = 0; i <= rowIndex && i < _storyRows.Count; i++)
            {
                var functionValue = _storyRows[i].Get("Custom");
                if (StoryFunctionService.ContainsExactFunction(functionValue, "BGM_Stop"))
                {
                    suppressed = true;
                }
                else if (StoryFunctionService.ContainsExactFunction(functionValue, "BGM_Start"))
                {
                    suppressed = false;
                }
            }

            return suppressed;
        }

        private int GetStoryBackgroundTransitionMode(int rowIndex)
        {
            var mode = 0;
            for (var i = 0; i <= rowIndex && i < _storyRows.Count; i++)
            {
                foreach (var functionValue in StoryFunctionService.SplitFunctionValues(_storyRows[i].Get("Custom")))
                {
                    if (StoryFunctionService.TryParseBackgroundTransitionMode(functionValue, out var parsedMode))
                    {
                        mode = parsedMode;
                    }
                }
            }

            return mode;
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
