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
        private async Task PlayCurrentStoryBgmFromUiAsync()
        {
            if (IsCurrentStoryFunction("BGMSTOP"))
            {
                _storyBgmPlaybackSuppressed = true;
                PauseStoryBgm();
                return;
            }

            if (_storyBgmPlaybackSuppressed)
            {
                return;
            }

            var requestId = ++_storyBgmPlaybackRequestId;
            try
            {
                await PlayCurrentStoryBgmAsync(requestId);
            }
            catch (Exception ex)
            {
                ShowStoryStatus(InfoBarSeverity.Error, "BGM播放失败", ex.Message);
                AppendLog(LogKind.Error, "故事编辑器 BGM 播放失败。", ex);
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

            if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTOP"))
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

        private async Task PreviewStoryFunctionAsync(string functionValue)
        {
            ShowStoryFunctionTriggeredStatus(functionValue);
            if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTART"))
            {
                _storyBgmPlaybackSuppressed = false;
                await PlayCurrentStoryBgmFromUiAsync();
            }
        }

        private bool IsCurrentStoryFunction(string normalizedFunctionKey)
        {
            return _storyRows.Count > 0 &&
                StoryFunctionService.ContainsFunction(_storyRows[_currentStoryRowIndex].Get("Custom"), normalizedFunctionKey);
        }

        private void ShowStoryFunctionTriggeredStatus(string functionValue)
        {
            ClearStoryFunctionTips();
            foreach (var functionName in StoryFunctionService.EnumerateFunctionDisplayNames(functionValue))
            {
                AddStoryTipWithEntrance(
                    StoryFunctionTipsPanel,
                    CreateStoryTipBar(InfoBarSeverity.Success, $"触发函数：{functionName}", string.Empty));
            }
        }

        private void ClearStoryFunctionTips()
        {
            StoryFunctionTipsPanel.Children.Clear();
        }

        private void ClearStoryTransientTips()
        {
            foreach (var timer in _storyTransientTipTimers.Values)
            {
                timer.Stop();
            }

            _storyTransientTipTimers.Clear();
            StoryFloatingTipsPanel.Children.Clear();
            StoryTransientTipsPanel.Children.Clear();
        }

        private InfoBar CreateStoryTipBar(InfoBarSeverity severity, string title, string message)
        {
            return new InfoBar
            {
                Severity = severity == InfoBarSeverity.Error ? InfoBarSeverity.Error : InfoBarSeverity.Success,
                Title = title,
                Message = message,
                IsOpen = true,
                IsClosable = true,
                RenderTransform = new TranslateTransform { Y = -18 },
                Opacity = 0
            };
        }

        private void AddStoryTipWithEntrance(Panel panel, InfoBar tip)
        {
            panel.Children.Add(tip);
            var steps = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (_, _) =>
            {
                steps++;
                var progress = Math.Min(1, steps / 12.0);
                var eased = 1 - Math.Pow(1 - progress, 3);
                tip.Opacity = eased;
                if (tip.RenderTransform is TranslateTransform transform)
                {
                    transform.Y = -18 + 18 * eased;
                }

                if (progress >= 1)
                {
                    timer.Stop();
                }
            };
            timer.Start();
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
                if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTOP"))
                {
                    suppressed = true;
                }
                else if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTART"))
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

        private async Task PlayCurrentStoryBgmAsync(int requestId)
        {
            if (_storyBgmPlaybackSuppressed || IsCurrentStoryFunction("BGMSTOP"))
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
            if (!string.IsNullOrWhiteSpace(_storyBgmPath) &&
                PathsEqual(_storyBgmPath, bgmPath) &&
                _storyBgmPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                return;
            }

            _storyBgmPath = bgmPath;
            var file = await StorageFile.GetFileFromPathAsync(bgmPath);
            if (requestId != _storyBgmPlaybackRequestId)
            {
                return;
            }

            if (_storyBgmPlaybackSuppressed || IsCurrentStoryFunction("BGMSTOP"))
            {
                PauseStoryBgm();
                return;
            }

            _storyBgmPlayer.Source = MediaSource.CreateFromStorageFile(file);
            _storyBgmPlayer.Play();
            AppendLog(LogKind.Info, $"故事编辑器播放 BGM：索引 {rawIndex} -> {Path.GetFileName(bgmPath)}");
        }

        private void StopStoryBgm()
        {
            _storyBgmPlayer.Pause();
            _storyBgmPlayer.Source = null;
            _storyBgmPath = null;
        }

        private void PauseStoryBgm()
        {
            _storyBgmPlaybackRequestId++;
            _storyBgmPlayer.Pause();
        }

        private async Task PlayCurrentStorySceneFromUiAsync()
        {
            var requestId = ++_storyScenePlaybackRequestId;
            try
            {
                await PlayCurrentStorySceneAsync(requestId);
            }
            catch (Exception ex)
            {
                ShowStoryStatus(InfoBarSeverity.Error, "环境音播放失败", ex.Message);
                AppendLog(LogKind.Error, "故事编辑器环境音播放失败。", ex);
            }
        }

        private async Task PlayCurrentStorySceneAsync(int requestId)
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
            if (!string.IsNullOrWhiteSpace(_storyScenePath) &&
                PathsEqual(_storyScenePath, scenePath) &&
                _storyScenePlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                return;
            }

            _storyScenePath = scenePath;
            var file = await StorageFile.GetFileFromPathAsync(scenePath);
            if (requestId != _storyScenePlaybackRequestId)
            {
                return;
            }

            _storyScenePlayer.Source = MediaSource.CreateFromStorageFile(file);
            _storyScenePlayer.Play();
            AppendLog(LogKind.Info, $"故事编辑器播放环境音：索引 {rawIndex} -> {Path.GetFileName(scenePath)}");
        }

        private void StopStoryScene()
        {
            _storyScenePlayer.Pause();
            _storyScenePlayer.Source = null;
            _storyScenePath = null;
        }

        private void StopStoryEditorAudio()
        {
            _storyBgmPlaybackRequestId++;
            _storyScenePlaybackRequestId++;
            _storyBgmPlaybackSuppressed = false;
            StopStoryBgm();
            StopStoryScene();
        }

    }
}
