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
        private void ShowBackgroundImageViewerPage(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return;
            }

            _viewingBackgroundImagePath = imagePath;
            _viewingCharacterLayer = null;
            _isViewingCharacterComposite = false;
            BackgroundImageViewerTabTitleText.Text = Path.GetFileNameWithoutExtension(imagePath);
            SetBackgroundImageViewerEditingEnabled(true);
            ResetBackgroundImageViewerTransform();
            ClearBackgroundImageViewerLayerImages();
            _ = ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, imagePath);

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开背景图查看：{Path.GetFileName(imagePath)}");
        }

        private void ShowCharacterLayerViewerPage(string layerPath, CharacterLayerKind layerKind)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return;
            }

            _viewingBackgroundImagePath = null;
            _viewingCharacterLayer = new CharacterLayerViewerState(layerKind, layerPath);
            _isViewingCharacterComposite = false;
            SetSelectedCharacterLayerPath(layerKind, layerPath);
            UpdateCharacterLayerViewerTitle(layerKind, layerPath);
            SetBackgroundImageViewerEditingEnabled(true);
            ResetBackgroundImageViewerTransform();
            ClearBackgroundImageViewerLayerImages();
            _ = ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, layerPath);
            _ = UpdateCharacterLayerPreviewAsync();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开{GetCharacterLayerDisplayName(layerKind)}查看：{Path.GetFileName(layerPath)}");
        }

        private async void ShowCharacterCompositeViewerPage()
        {
            if (_currentCharacter is null ||
                string.IsNullOrWhiteSpace(_selectedCharacterClothPath) ||
                !File.Exists(_selectedCharacterClothPath))
            {
                CharacterDetailInfoBar.Severity = InfoBarSeverity.Warning;
                CharacterDetailInfoBar.Title = "无法查看立绘";
                CharacterDetailInfoBar.Message = "当前角色还没有可查看的服装图层。";
                CharacterDetailInfoBar.IsOpen = true;
                return;
            }

            _viewingBackgroundImagePath = null;
            _viewingCharacterLayer = null;
            _isViewingCharacterComposite = true;
            BackgroundImageViewerTabTitleText.Text = $"{_currentCharacter.Name} / 分层预览";
            SetBackgroundImageViewerEditingEnabled(false);
            ResetBackgroundImageViewerTransform();
            await LoadCharacterCompositeViewerImagesAsync();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开组合立绘查看：{_currentCharacter.Name}");
        }

        private async Task LoadCharacterCompositeViewerImagesAsync()
        {
            await SetViewerLayerImageAsync(BackgroundImageViewerImage, _selectedCharacterClothPath);
            await SetViewerLayerImageAsync(
                BackgroundImageViewerFaceImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterFacePath) ? _selectedCharacterFacePath : null);
            await SetViewerLayerImageAsync(
                BackgroundImageViewerAdornImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterAdornPath) ? _selectedCharacterAdornPath : null);
            await SetViewerLayerImageAsync(
                BackgroundImageViewerVfxImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterVfxPath) ? _selectedCharacterVfxPath : null);
        }

        private async Task SetViewerLayerImageAsync(Image image, string? path)
        {
            image.RenderTransform = BackgroundImageViewerTransform;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                return;
            }

            await ThumbnailFactory.LoadThumbnailFromFileAsync(image, path);
            image.Visibility = Visibility.Visible;
        }

        private void ClearBackgroundImageViewerLayerImages()
        {
            _ = SetViewerLayerImageAsync(BackgroundImageViewerFaceImage, null);
            _ = SetViewerLayerImageAsync(BackgroundImageViewerAdornImage, null);
            _ = SetViewerLayerImageAsync(BackgroundImageViewerVfxImage, null);
            BackgroundImageViewerImage.RenderTransform = BackgroundImageViewerTransform;
        }

        private void SetBackgroundImageViewerEditingEnabled(bool isEnabled)
        {
            BackgroundImageViewerRemarkButton.IsEnabled = isEnabled;
            BackgroundImageViewerReplaceButton.IsEnabled = isEnabled;
            BackgroundImageViewerDeleteButton.IsEnabled = isEnabled;
        }

        private void SetSelectedCharacterLayerPath(CharacterLayerKind layerKind, string layerPath)
        {
            switch (layerKind)
            {
                case CharacterLayerKind.Cloth:
                    _selectedCharacterClothPath = layerPath;
                    break;
                case CharacterLayerKind.Face:
                    _selectedCharacterFacePath = layerPath;
                    break;
                case CharacterLayerKind.Adorn:
                    _selectedCharacterAdornPath = layerPath;
                    break;
            }
        }

        private void UpdateCharacterLayerViewerTitle(CharacterLayerKind layerKind, string layerPath)
        {
            BackgroundImageViewerTabTitleText.Text = $"{GetCharacterLayerDisplayName(layerKind)} {Path.GetFileNameWithoutExtension(layerPath)}";
        }

        private async Task SetViewingCharacterLayerRemarkAsync(CharacterLayerKind layerKind, string layerPath)
        {
            var updatedPath = layerKind switch
            {
                CharacterLayerKind.Cloth => await SetCharacterClothRemarkAsync(layerPath),
                CharacterLayerKind.Face => await SetCharacterFaceRemarkAsync(layerPath),
                CharacterLayerKind.Adorn => await SetCharacterAdornRemarkAsync(layerPath),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(updatedPath) || !File.Exists(updatedPath))
            {
                return;
            }

            _viewingCharacterLayer = new CharacterLayerViewerState(layerKind, updatedPath);
            SetSelectedCharacterLayerPath(layerKind, updatedPath);
            UpdateCharacterLayerViewerTitle(layerKind, updatedPath);
            await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedPath);
        }

        private async Task<bool> DeleteViewingCharacterLayerAsync(CharacterLayerKind layerKind, string layerPath)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => await DeleteCharacterClothAsync(layerPath),
                CharacterLayerKind.Face => await DeleteCharacterFaceAsync(layerPath),
                CharacterLayerKind.Adorn => await DeleteCharacterAdornAsync(layerPath),
                _ => false
            };
        }

        private void CloseBackgroundImageViewerButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            CloseBackgroundImageViewer();
        }

        private void CloseBackgroundImageViewer()
        {
            _isPanningBackgroundImage = false;
            var shouldReturnToCharacterDetail = (_viewingCharacterLayer is not null || _isViewingCharacterComposite) && _currentCharacter is not null;
            _viewingBackgroundImagePath = null;
            _viewingCharacterLayer = null;
            _isViewingCharacterComposite = false;
            BackgroundImageViewerImage.Source = null;
            ClearBackgroundImageViewerLayerImages();
            ResetBackgroundImageViewerTransform();

            if (shouldReturnToCharacterDetail)
            {
                BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
                CharacterDetailPage.Visibility = Visibility.Visible;
                CharacterPreviewSurface.Focus(FocusState.Programmatic);
            }
            else if (_currentAssetLibrary is not null)
            {
                ShowAssetLibraryDetailPage(_currentAssetLibrary);
            }
        }

        private void BackgroundImageViewerPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseBackgroundImageViewer();
                e.Handled = true;
                return;
            }

            if (_isViewingCharacterComposite && HandleCharacterCompositeViewerKeyDown(e))
            {
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.NumberPad4 ||
                e.Key == Windows.System.VirtualKey.A)
            {
                ShowAdjacentViewerImage(-1);
                e.Handled = true;
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Right or Windows.System.VirtualKey.NumberPad6 ||
                e.Key == Windows.System.VirtualKey.D)
            {
                ShowAdjacentViewerImage(1);
                e.Handled = true;
            }
        }

        private bool HandleCharacterCompositeViewerKeyDown(KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Q:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Adorn, -1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.E:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Adorn, 1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.A:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Face, -1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.D:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Face, 1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.Z:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Cloth, -1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.C:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Cloth, 1);
                    e.Handled = true;
                    return true;
                default:
                    return false;
            }
        }

        private async Task CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind layerKind, int direction)
        {
            await CycleCharacterDetailLayerAsync(layerKind, direction);
            await LoadCharacterCompositeViewerImagesAsync();
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
        }

        private void BackgroundImageViewerPage_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            PlayNegativeSound();
            CloseBackgroundImageViewer();
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(BackgroundImageViewerCanvas);
            var pointerPosition = point.Position;
            var previousScale = _backgroundImageViewerScale;
            var zoomFactor = point.Properties.MouseWheelDelta > 0 ? 1.1 : 0.9;
            _backgroundImageViewerScale = Math.Clamp(_backgroundImageViewerScale * zoomFactor, 0.1, 8);

            var actualZoomFactor = _backgroundImageViewerScale / previousScale;
            var canvasCenterX = BackgroundImageViewerCanvas.ActualWidth / 2;
            var canvasCenterY = BackgroundImageViewerCanvas.ActualHeight / 2;
            var offsetFromCenterX = pointerPosition.X - canvasCenterX - BackgroundImageViewerTransform.TranslateX;
            var offsetFromCenterY = pointerPosition.Y - canvasCenterY - BackgroundImageViewerTransform.TranslateY;
            var proposedTranslateX =
                BackgroundImageViewerTransform.TranslateX - offsetFromCenterX * (actualZoomFactor - 1);
            var proposedTranslateY =
                BackgroundImageViewerTransform.TranslateY - offsetFromCenterY * (actualZoomFactor - 1);
            ClampBackgroundImageTranslation(ref proposedTranslateX, ref proposedTranslateY);

            BackgroundImageViewerTransform.ScaleX = _backgroundImageViewerScale;
            BackgroundImageViewerTransform.ScaleY = _backgroundImageViewerScale;
            BackgroundImageViewerTransform.TranslateX = proposedTranslateX;
            BackgroundImageViewerTransform.TranslateY = proposedTranslateY;
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(BackgroundImageViewerCanvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPanningBackgroundImage = true;
            _lastBackgroundImagePointerPosition = point.Position;
            BackgroundImageViewerCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanningBackgroundImage)
            {
                return;
            }

            var point = e.GetCurrentPoint(BackgroundImageViewerCanvas);
            var deltaX = point.Position.X - _lastBackgroundImagePointerPosition.X;
            var deltaY = point.Position.Y - _lastBackgroundImagePointerPosition.Y;
            var proposedX = BackgroundImageViewerTransform.TranslateX + deltaX;
            var proposedY = BackgroundImageViewerTransform.TranslateY + deltaY;
            ClampBackgroundImageTranslation(ref proposedX, ref proposedY);
            BackgroundImageViewerTransform.TranslateX = proposedX;
            BackgroundImageViewerTransform.TranslateY = proposedY;
            _lastBackgroundImagePointerPosition = point.Position;
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetBackgroundImageViewerTransform();
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndBackgroundImagePan(e);
        }

        private void BackgroundImageViewerCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndBackgroundImagePan(e);
        }

        private void BackgroundImageViewerCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPanningBackgroundImage = false;
        }

        private void EndBackgroundImagePan(PointerRoutedEventArgs e)
        {
            _isPanningBackgroundImage = false;
            BackgroundImageViewerCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void ResetBackgroundImageViewerTransform()
        {
            _backgroundImageViewerScale = 1;
            BackgroundImageViewerTransform.ScaleX = 1;
            BackgroundImageViewerTransform.ScaleY = 1;
            BackgroundImageViewerTransform.TranslateX = 0;
            BackgroundImageViewerTransform.TranslateY = 0;
        }

        private void ClampBackgroundImageTranslation(ref double translateX, ref double translateY)
        {
            var viewerWidth = Math.Max(BackgroundImageViewerCanvas.ActualWidth, 1);
            var viewerHeight = Math.Max(BackgroundImageViewerCanvas.ActualHeight, 1);
            var imageWidth = BackgroundImageViewerImage.ActualWidth * _backgroundImageViewerScale;
            var imageHeight = BackgroundImageViewerImage.ActualHeight * _backgroundImageViewerScale;
            var horizontalAllowance = Math.Max(viewerWidth * 0.35, imageWidth * 0.35);
            var verticalAllowance = Math.Max(viewerHeight * 0.35, imageHeight * 0.35);

            translateX = Math.Clamp(translateX, -horizontalAllowance, horizontalAllowance);
            translateY = Math.Clamp(translateY, -verticalAllowance, verticalAllowance);
        }

        private void ShowAdjacentBackgroundImage(int direction)
        {
            if (_currentAssetLibrary is null || _viewingBackgroundImagePath is null)
            {
                return;
            }

            // 按**正在看的那张图自己所在的目录**取邻居 —— 这样背景图和道具图片
            // 各在各的分类里前后翻，不会翻到另一个分类去。
            var orderedPaths = ImageSetService.GetFilePaths(GetImageSetFolderOf(_viewingBackgroundImagePath));
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, _viewingBackgroundImagePath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            PlaySelectionSound();
            ShowBackgroundImageViewerPage(orderedPaths[nextIndex]);
        }

        private void ShowAdjacentViewerImage(int direction)
        {
            if (_viewingCharacterLayer is not null)
            {
                ShowAdjacentCharacterLayer(direction, _viewingCharacterLayer.Kind, _viewingCharacterLayer.Path);
            }
            else
            {
                ShowAdjacentBackgroundImage(direction);
            }
        }

        private void ShowAdjacentCharacterLayer(int direction, CharacterLayerKind layerKind, string currentPath)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var orderedPaths = CharacterLayerAssetService.GetLayerPaths(_currentCharacter, layerKind);
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, currentPath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            PlaySelectionSound();
            ShowCharacterLayerViewerPage(orderedPaths[nextIndex], layerKind);
        }

        private async void BackgroundImageViewerRemarkButton_Click(object sender, RoutedEventArgs e)
        {
            var currentLayer = _viewingCharacterLayer;
            if (currentLayer is not null)
            {
                await SetViewingCharacterLayerRemarkAsync(currentLayer.Kind, currentLayer.Path);
                return;
            }

            if (_viewingBackgroundImagePath is null)
            {
                return;
            }

            var updatedPath = await SetBackgroundImageRemarkAsync(_viewingBackgroundImagePath);
            if (string.IsNullOrWhiteSpace(updatedPath) || !File.Exists(updatedPath))
            {
                return;
            }

            _viewingBackgroundImagePath = updatedPath;
            BackgroundImageViewerTabTitleText.Text = Path.GetFileNameWithoutExtension(updatedPath);
            await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedPath);
        }

        private async void BackgroundImageViewerReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            var currentLayer = _viewingCharacterLayer;
            if (currentLayer is not null)
            {
                await ReplaceCharacterLayerAsync(currentLayer.Kind, currentLayer.Path);
                return;
            }

            if (_viewingBackgroundImagePath is null)
            {
                return;
            }

            await ReplaceBackgroundImageAsync(_viewingBackgroundImagePath);
        }

        private async void BackgroundImageViewerDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var currentLayer = _viewingCharacterLayer;
            if (currentLayer is not null)
            {
                var deletedLayer = await DeleteViewingCharacterLayerAsync(currentLayer.Kind, currentLayer.Path);
                if (deletedLayer)
                {
                    CloseBackgroundImageViewer();
                }

                return;
            }

            if (_viewingBackgroundImagePath is null)
            {
                return;
            }

            var deleted = await DeleteBackgroundImageAsync(_viewingBackgroundImagePath);
            if (deleted)
            {
                CloseBackgroundImageViewer();
            }
        }

        private void ShowMusicPlayerPage(string musicPath)
        {
            ShowMusicPlayerPage(musicPath, AudioAssetKind.Music);
        }

        private async void ShowMusicPlayerPage(string musicPath, AudioAssetKind kind)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return;
            }

            _playingMusicPath = musicPath;
            _playingAudioKind = kind;
            MusicPlayerTabTitleText.Text = Path.GetFileNameWithoutExtension(musicPath);
            MusicPlayerTrackTitleText.Text = Path.GetFileNameWithoutExtension(musicPath);

            var file = await StorageFile.GetFileFromPathAsync(musicPath);
            MusicPlayerElement.Source = MediaSource.CreateFromStorageFile(file);
            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayPauseButton.Content = "播放";

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Visible;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            MusicPlayerCloseButton.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开{AudioAssetService.GetDisplayName(kind)}播放：{Path.GetFileName(musicPath)}");
        }

        private void CloseMusicPlayerButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            CloseMusicPlayer();
        }

        private void CloseMusicPlayer()
        {
            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayerElement.Source = null;
            _playingMusicPath = null;

            if (_currentAssetLibrary is not null)
            {
                ShowAssetLibraryDetailPage(_currentAssetLibrary);
            }
        }

        private void MusicPlayerPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseMusicPlayer();
                e.Handled = true;
            }
        }

        private void PreviousMusicButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAdjacentMusic(-1);
        }

        private void NextMusicButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAdjacentMusic(1);
        }

        private void ShowAdjacentMusic(int direction)
        {
            if (_currentAssetLibrary is null || _playingMusicPath is null)
            {
                return;
            }

            var orderedPaths = AudioAssetService.GetFilePaths(GetAudioFolderPath(_currentAssetLibrary, _playingAudioKind));
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, _playingMusicPath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            PlaySelectionSound();
            ShowMusicPlayerPage(orderedPaths[nextIndex], _playingAudioKind);
        }

        private void MusicPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (MusicPlayerElement.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
            {
                MusicPlayerElement.MediaPlayer.Pause();
                MusicPlayPauseButton.Content = "播放";
            }
            else
            {
                MusicPlayerElement.MediaPlayer.Play();
                MusicPlayPauseButton.Content = "暂停";
            }

            PlaySelectionSound();
        }

        private async void MusicPlayerRemarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playingMusicPath is null)
            {
                return;
            }

            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayerElement.Source = null;
            var updatedPath = await SetAudioRemarkAsync(_playingAudioKind, _playingMusicPath);
            if (string.IsNullOrWhiteSpace(updatedPath) || !File.Exists(updatedPath))
            {
                await ReloadMusicPlayerSourceAsync(_playingMusicPath);
                return;
            }

            _playingMusicPath = updatedPath;
            MusicPlayerTabTitleText.Text = Path.GetFileNameWithoutExtension(updatedPath);
            MusicPlayerTrackTitleText.Text = Path.GetFileNameWithoutExtension(updatedPath);
            await ReloadMusicPlayerSourceAsync(updatedPath);
        }

        private async void MusicPlayerDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playingMusicPath is null)
            {
                return;
            }

            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayerElement.Source = null;
            var deleted = await DeleteAudioAsync(_playingAudioKind, _playingMusicPath);
            if (deleted)
            {
                CloseMusicPlayer();
            }
            else
            {
                await ReloadMusicPlayerSourceAsync(_playingMusicPath);
            }
        }

    }
}
