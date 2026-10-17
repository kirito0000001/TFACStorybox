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
        private GridView GetAudioGridView(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => MusicGridView,
                AudioAssetKind.Ambient => AmbientSoundGridView,
                AudioAssetKind.SoundEffect => SoundEffectGridView,
                _ => MusicGridView
            };
        }

        private bool IsAudioExpanderExpanded(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => MusicExpander.IsExpanded,
                AudioAssetKind.Ambient => AmbientSoundExpander.IsExpanded,
                AudioAssetKind.SoundEffect => SoundEffectExpander.IsExpanded,
                _ => false
            };
        }

        private bool IsAudioNormalizing(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => _isNormalizingMusicFiles,
                AudioAssetKind.Ambient => _isNormalizingAmbientSoundFiles,
                AudioAssetKind.SoundEffect => _isNormalizingSoundEffectFiles,
                _ => false
            };
        }

        private void SetAudioNormalizing(AudioAssetKind kind, bool value)
        {
            switch (kind)
            {
                case AudioAssetKind.Music:
                    _isNormalizingMusicFiles = value;
                    break;
                case AudioAssetKind.Ambient:
                    _isNormalizingAmbientSoundFiles = value;
                    break;
                case AudioAssetKind.SoundEffect:
                    _isNormalizingSoundEffectFiles = value;
                    break;
            }
        }

        // GetDraggingAudioItem 已删：音频拖放区读"当前拖拽项"用的 getter。
        // 拖放区退休后它已无调用者，配套的三个字段也一并删除。

        // SetDraggingAudioItem 已删：音频旧网格拖拽项的写入方法（处理器删完后归零）。
        // GetDraggingAudioItem 仍保留 —— 拖放区（AudioDropZone_* 一族）还在读它，
        // 要等第 5 步随整段 Expander 一起退休。

        private static int? ResolveStoryAssetIndex(int rawIndex, int assetCount)
        {
            if (assetCount <= 0 || rawIndex < 0)
            {
                return null;
            }

            return rawIndex < assetCount ? rawIndex : null;
        }

        private void ShowChapterStatus(InfoBarSeverity severity, string title, string message)
        {
            ChapterInfoBar.Severity = severity;
            ChapterInfoBar.Title = title;
            ChapterInfoBar.Message = message;
            ChapterInfoBar.IsOpen = true;
        }

    }
}
