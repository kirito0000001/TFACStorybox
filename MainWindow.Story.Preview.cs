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
        private async Task RefreshStoryPreviewAsync()
        {
            _storyPreviewRefreshCancellation?.Cancel();
            _storyPreviewRefreshCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _storyPreviewRefreshCancellation = cancellation;
            var cancellationToken = cancellation.Token;
            if (_storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex].Clone();
            try
            {
                await SetStoryBackgroundAsync(ParseInt(row.Get("BGindex")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await SetStoryCharacterSlotAsync(StoryCharacterSlot1, 1, row.Get("Chara1"), ParseInt(row.Get("Body1")), ParseInt(row.Get("Face1")), ParseInt(row.Get("Adorn1")), ParseInt(row.Get("Vfx1")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await SetStoryCharacterSlotAsync(StoryCharacterSlot2, 2, row.Get("Chara2"), ParseInt(row.Get("Body2")), ParseInt(row.Get("Face2")), ParseInt(row.Get("Adorn2")), ParseInt(row.Get("Vfx2")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await SetStoryCharacterSlotAsync(StoryCharacterSlot3, 3, row.Get("Chara3"), ParseInt(row.Get("Body3")), ParseInt(row.Get("Face3")), ParseInt(row.Get("Adorn3")), ParseInt(row.Get("Vfx3")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await SetStoryCharacterSlotAsync(StoryCharacterSlot4, 4, row.Get("Chara4"), ParseInt(row.Get("Body4")), ParseInt(row.Get("Face4")), ParseInt(row.Get("Adorn4")), ParseInt(row.Get("Vfx4")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await SetStoryCharacterSlotAsync(StoryCharacterSlot5, 5, row.Get("Chara5"), ParseInt(row.Get("Body5")), ParseInt(row.Get("Face5")), ParseInt(row.Get("Adorn5")), ParseInt(row.Get("Vfx5")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await SetStorySpeakerPreviewAsync(row.Get("TalkChar"), ParseInt(row.Get("TalkBody")), ParseInt(row.Get("TalkFace")), ParseInt(row.Get("TalkAdorn")), ParseInt(row.Get("TalkVfx")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                UpdateStoryCharacterSlotLayout();
                UpdateStoryToolbarCurrentInfo();
                _storyEditorViewModel.AssetStatusText = _currentStoryAssetLibrary is null
                    ? "当前项目未关联素材库。"
                    : $"素材库：{_currentStoryAssetLibrary.Name} | 立绘：{(ShowFullCharacterArtCheckBox?.IsChecked == true ? "完整" : "上半身")}";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task SetStoryBackgroundAsync(int index, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_currentStoryAssetLibrary is null)
            {
                if (_storyBackgroundPreviewKey != string.Empty)
                {
                    StoryBackgroundImage.Source = null;
                    _storyBackgroundPreviewKey = string.Empty;
                }

                return;
            }

            var backgrounds = BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary));
            var resolvedIndex = ResolveStoryAssetIndex(index, backgrounds.Count);
            if (resolvedIndex is null)
            {
                if (_storyBackgroundPreviewKey != string.Empty)
                {
                    StoryBackgroundImage.Source = null;
                    _storyBackgroundPreviewKey = string.Empty;
                }

                return;
            }

            var path = backgrounds[resolvedIndex.Value];
            var previousPreviewKey = _storyBackgroundPreviewKey;
            if (string.Equals(_storyBackgroundPreviewKey, path, StringComparison.OrdinalIgnoreCase) &&
                StoryBackgroundImage.Source is not null)
            {
                return;
            }

            _storyBackgroundPreviewKey = path;
            await LoadStoryPreviewImageAsync(StoryBackgroundImage, path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(previousPreviewKey) &&
                !string.Equals(previousPreviewKey, path, StringComparison.OrdinalIgnoreCase))
            {
                ShowStoryStatus(
                    InfoBarSeverity.Informational,
                    "背景切换模式",
                    StoryFunctionService.GetBackgroundTransitionModeRemark(_storyBackgroundTransitionMode));
            }
        }

        private async Task SetStoryCharacterSlotAsync(Border slot, int slotIndex, string characterName, int bodyIndex, int faceIndex, int adornIndex, int vfxIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grid = new Grid();
            slot.Tag = slotIndex;
            slot.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            slot.ContextFlyout = CreateStoryCharacterSlotMenu(slotIndex);
            slot.PointerEntered -= StoryCharacterSlot_PointerEntered;
            slot.PointerExited -= StoryCharacterSlot_PointerExited;
            slot.PointerEntered += StoryCharacterSlot_PointerEntered;
            slot.PointerExited += StoryCharacterSlot_PointerExited;
            ClipPreviewBorderToBounds(slot);
            var useUnifiedPreview = true;
            if (useUnifiedPreview)
            {
                await SetStoryCharacterPreviewAsync(
                    slot,
                    characterName,
                    bodyIndex,
                    faceIndex,
                    adornIndex,
                    vfxIndex,
                    $"{slotIndex}号位",
                    false,
                    cancellationToken);
                return;
            }
            slot.Child = grid;

            var label = new TextBlock
            {
                Text = $"{slotIndex}号位",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 12)
            };

            if (_currentStoryAssetLibrary is null || string.IsNullOrWhiteSpace(characterName))
            {
                grid.Children.Add(label);
                return;
            }

            var character = ResolveStoryCharacter(characterName);
            if (character is null)
            {
                grid.Children.Add(label);
                return;
            }

            var imagePaths = new[]
            {
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Cloth, bodyIndex),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Face, faceIndex),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Adorn, adornIndex),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Vfx, vfxIndex)
            };

            foreach (var imagePath in imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var image = new Image
                {
                    Stretch = ShowFullCharacterArtCheckBox?.IsChecked == true ? Stretch.Uniform : Stretch.UniformToFill,
                    VerticalAlignment = ShowFullCharacterArtCheckBox?.IsChecked == true ? VerticalAlignment.Bottom : VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                image.Height = ShowFullCharacterArtCheckBox?.IsChecked == true ? double.NaN : 760;
                grid.Children.Add(image);
                await ThumbnailFactory.LoadThumbnailFromFileAsync(image, imagePath!);
            }

            grid.Children.Add(label);
        }

        private async Task SetStorySpeakerPreviewAsync(string characterName, int bodyIndex, int faceIndex, int adornIndex, int vfxIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StorySpeakerPreviewSlot.Tag = 0;
            StorySpeakerPreviewSlot.ContextFlyout = CreateStorySpeakerSlotMenu();
            StorySpeakerPreviewSlot.PointerEntered -= StoryCharacterSlot_PointerEntered;
            StorySpeakerPreviewSlot.PointerExited -= StoryCharacterSlot_PointerExited;
            StorySpeakerPreviewSlot.PointerEntered += StoryCharacterSlot_PointerEntered;
            StorySpeakerPreviewSlot.PointerExited += StoryCharacterSlot_PointerExited;
            ClipPreviewBorderToBounds(StorySpeakerPreviewSlot);

            await SetStoryCharacterPreviewAsync(
                StorySpeakerPreviewSlot,
                characterName,
                bodyIndex,
                faceIndex,
                adornIndex,
                vfxIndex,
                "说话人",
                true,
                cancellationToken);
        }

        private async Task SetStoryCharacterPreviewAsync(
            Border slot,
            string characterName,
            int bodyIndex,
            int faceIndex,
            int adornIndex,
            int vfxIndex,
            string positionLabel,
            bool compact,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var showFull = ShowFullCharacterArtCheckBox?.IsChecked == true;
            var previewSlotIndex = slot.Tag is int tagIndex ? tagIndex : -1;
            var previewKey = string.Join(
                "|",
                characterName,
                bodyIndex,
                faceIndex,
                adornIndex,
                vfxIndex,
                positionLabel,
                compact,
                showFull,
                GetVisibleStoryCharacterSlotCount());
            if (_storyCharacterPreviewKeys.TryGetValue(previewSlotIndex, out var existingKey) &&
                string.Equals(existingKey, previewKey, StringComparison.Ordinal) &&
                slot.Child is not null)
            {
                return;
            }

            _storyCharacterPreviewKeys[previewSlotIndex] = previewKey;
            var grid = new Grid();
            slot.Child = grid;

            var character = ResolveStoryCharacter(characterName);
            var displayName = character?.Name ?? (string.IsNullOrWhiteSpace(characterName) ? "无角色" : characterName);
            var filterName = character is null ? null : ResolveStoryCharacterFilterName(vfxIndex);
            var label = CreateStoryCharacterPreviewLabel(displayName, positionLabel, compact, filterName);

            if (_currentStoryAssetLibrary is null || string.IsNullOrWhiteSpace(characterName) || character is null)
            {
                grid.Children.Add(label);
                return;
            }

            var bodyPath = CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Cloth, bodyIndex);
            var facePath = CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Face, faceIndex);
            var adornPath = CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Adorn, adornIndex);
            var imagePaths = new[]
            {
                bodyPath,
                _characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, facePath, ComputeFileHash) ? facePath : null,
                _characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, adornPath, ComputeFileHash) ? adornPath : null
            };

            foreach (var imagePath in imagePaths.Where(IsPreviewableImagePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = showFull ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    RenderTransformOrigin = showFull
                        ? new Windows.Foundation.Point(0.5, 1.0)
                        : new Windows.Foundation.Point(0.5, compact ? 0.22 : 0.3)
                };

                var scale = GetStoryCharacterPreviewScale(compact, showFull);
                if (Math.Abs(scale - 1) > 0.01)
                {
                    image.RenderTransform = new CompositeTransform
                    {
                        ScaleX = scale,
                        ScaleY = scale
                    };
                }

                grid.Children.Add(image);
                await LoadStoryPreviewImageAsync(image, imagePath!, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            grid.Children.Add(label);
        }

        private static Border CreateStoryCharacterPreviewLabel(string displayName, string positionLabel, bool compact, string? filterName)
        {
            var text = string.IsNullOrWhiteSpace(filterName)
                ? $"{displayName}\n{positionLabel}"
                : $"{displayName}\n{positionLabel}\n{filterName}";
            return new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(148, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    FontSize = compact ? 11 : 14,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        private int GetVisibleStoryCharacterSlotCount()
        {
            var count = 3;
            if (ShowCharacterSlot4CheckBox?.IsChecked == true)
            {
                count++;
            }

            if (ShowCharacterSlot5CheckBox?.IsChecked == true)
            {
                count++;
            }

            return count;
        }

        private double GetStoryCharacterPreviewScale(bool compact, bool showFull)
        {
            if (compact)
            {
                return showFull ? 1.75 : 4.4;
            }

            return GetVisibleStoryCharacterSlotCount() switch
            {
                <= 3 => showFull ? 2.05 : 3.15,
                4 => showFull ? 1.85 : 2.75,
                _ => showFull ? 1.7 : 2.45
            };
        }

        private static bool IsPreviewableImagePath(string? imagePath)
        {
            return !string.IsNullOrWhiteSpace(imagePath) &&
                File.Exists(imagePath) &&
                BackgroundImageService.Extensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase);
        }

        private static void ClipPreviewBorderToBounds(Border border)
        {
            border.SizeChanged -= StoryPreviewBorder_SizeChanged;
            border.SizeChanged += StoryPreviewBorder_SizeChanged;
            UpdatePreviewBorderClip(border, border.ActualWidth, border.ActualHeight);
        }

        private static void StoryPreviewBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Border border)
            {
                UpdatePreviewBorderClip(border, e.NewSize.Width, e.NewSize.Height);
            }
        }

        private static void UpdatePreviewBorderClip(Border border, double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            border.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, width, height)
            };
        }

    }
}
