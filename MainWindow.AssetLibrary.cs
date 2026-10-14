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
        private void ShowCreateProjectPageButton_Click(object sender, RoutedEventArgs e)
        {
            ShowCreateProjectPage();
            AppendLog(LogKind.User, "打开创建项目页面。");
        }

        private void ShowCreateAssetLibraryPageButton_Click(object sender, RoutedEventArgs e)
        {
            ShowCreateAssetLibraryPage();
            AppendLog(LogKind.User, "打开创建素材库页面。");
        }

        private void RefreshProjectsButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectionSound();
            LoadProjects();
            AppendLog(LogKind.User, "手动刷新项目列表。");
        }

        private void RefreshAssetLibrariesButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectionSound();
            LoadAssetLibraries();
            AppendLog(LogKind.User, "手动刷新素材库列表。");
        }

        private void CancelCreateProjectButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            ShowWorkbenchPage();
        }

        private void CancelCreateAssetLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            ShowAssetLibraryPage();
        }

        private async void ChooseProjectThumbnailButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectionSound();
            var selectedPath = await PickThumbnailPathAsync();
            if (selectedPath is null)
            {
                return;
            }

            _selectedProjectThumbnailPath = selectedPath;
            CreateProjectThumbnailImage.Source = new BitmapImage(new Uri(selectedPath));
            CreateProjectThumbnailImage.Visibility = Visibility.Visible;
            CreateProjectDefaultThumbnail.Visibility = Visibility.Collapsed;
            AppendLog(LogKind.User, $"已选择项目缩略图：{selectedPath}");
        }

        private async void ChooseAssetLibraryThumbnailButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectionSound();
            var selectedPath = await PickThumbnailPathAsync();
            if (selectedPath is null)
            {
                return;
            }

            _selectedAssetLibraryThumbnailPath = selectedPath;
            CreateAssetLibraryThumbnailImage.Source = new BitmapImage(new Uri(selectedPath));
            CreateAssetLibraryThumbnailImage.Visibility = Visibility.Visible;
            CreateAssetLibraryDefaultThumbnail.Visibility = Visibility.Collapsed;
            AppendLog(LogKind.User, $"已选择素材库缩略图：{selectedPath}");
        }

        private async System.Threading.Tasks.Task<string?> PickThumbnailPathAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".webp");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFile = await picker.PickSingleFileAsync();
            return selectedFile?.Path;
        }

        private async Task<string?> PickReplacementFileAsync(IEnumerable<string> extensions, PickerLocationId startLocation)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = startLocation
            };
            foreach (var extension in extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFile = await picker.PickSingleFileAsync();
            return selectedFile?.Path;
        }

        private void CreateProjectButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPositiveSound();
            var projectName = CreateProjectNameTextBox.Text.Trim();
            var projectCode = CreateProjectCodeTextBox.Text.Trim();

            if (ProjectAssetLibraryComboBox.SelectedItem is not ComboBoxItem { Tag: string assetLibraryFolderName })
            {
                ShowCreateProjectError("请先创建并选择一个素材库。");
                return;
            }

            var assetLibrary = GetAssetLibraries().FirstOrDefault(library => library.FolderName == assetLibraryFolderName);
            if (assetLibrary is null)
            {
                ShowCreateProjectError("选择的素材库不存在，请刷新后重试。");
                return;
            }

            try
            {
                _projectWorkspaceService.CreateProject(
                    _projectRootPath,
                    projectName,
                    projectCode,
                    assetLibrary,
                    _selectedProjectThumbnailPath);
            }
            catch (IOException ex)
            {
                ShowCreateProjectError(ex.Message);
                return;
            }

            ResetCreateProjectForm();
            ShowWorkbenchPage();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"创建项目：{projectName}，关联素材库：{assetLibrary.Name}");
        }

        private void CreateAssetLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPositiveSound();
            var assetLibraryName = CreateAssetLibraryNameTextBox.Text.Trim();

            try
            {
                _projectWorkspaceService.CreateAssetLibrary(
                    _projectRootPath,
                    assetLibraryName,
                    _selectedAssetLibraryThumbnailPath);
            }
            catch (IOException ex)
            {
                ShowCreateAssetLibraryError(ex.Message);
                return;
            }

            ResetCreateAssetLibraryForm();
            ShowAssetLibraryPage();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"创建素材库：{assetLibraryName}");
        }

        private static void EnsureAssetLibraryCategoryFolders(string assetLibraryPath)
        {
            ProjectWorkspaceService.EnsureAssetLibraryCategoryFolders(assetLibraryPath);
        }

        private void ShowCreateProjectError(string message)
        {
            CreateProjectInfoBar.Title = "无法创建项目";
            CreateProjectInfoBar.Message = message;
            CreateProjectInfoBar.Severity = InfoBarSeverity.Error;
            CreateProjectInfoBar.IsOpen = true;
            AppendLog(LogKind.Error, $"无法创建项目：{message}");
        }

        private void ShowCreateAssetLibraryError(string message)
        {
            CreateAssetLibraryInfoBar.Title = "无法创建素材库";
            CreateAssetLibraryInfoBar.Message = message;
            CreateAssetLibraryInfoBar.Severity = InfoBarSeverity.Error;
            CreateAssetLibraryInfoBar.IsOpen = true;
            AppendLog(LogKind.Error, $"无法创建素材库：{message}");
        }

        private void ResetCreateProjectForm()
        {
            _selectedProjectThumbnailPath = null;
            CreateProjectNameTextBox.Text = string.Empty;
            CreateProjectCodeTextBox.Text = string.Empty;
            ProjectAssetLibraryComboBox.SelectedItem = null;
            CreateProjectInfoBar.IsOpen = false;
            CreateProjectThumbnailImage.Source = null;
            CreateProjectThumbnailImage.Visibility = Visibility.Collapsed;
            CreateProjectDefaultThumbnail.Visibility = Visibility.Visible;
        }

        private void ResetCreateAssetLibraryForm()
        {
            _selectedAssetLibraryThumbnailPath = null;
            CreateAssetLibraryNameTextBox.Text = string.Empty;
            CreateAssetLibraryInfoBar.IsOpen = false;
            CreateAssetLibraryThumbnailImage.Source = null;
            CreateAssetLibraryThumbnailImage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryDefaultThumbnail.Visibility = Visibility.Visible;
        }

        private void BackToAssetLibraryPageButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            ShowAssetLibraryPage();
        }

        private async void AddBackgroundImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in ImageSetService.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            try
            {
                var importedCount = await ImportBackgroundImagesAsync(selectedFiles.Select(file => file.Path));
                ShowFloatingTip(
                    importedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    importedCount > 0 ? "背景图已导入" : "没有导入背景图",
                    importedCount > 0 ? $"已导入 {importedCount} 个文件。" : "选择的文件没有可导入内容。",
                    logMessage: $"导入背景图：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                AssetLibraryDetailStatusText.Text = $"导入背景图失败：{ex.Message}";
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "背景图导入失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "导入背景图失败。");
            }
        }

        private async Task<int> ImportBackgroundImagesAsync(IEnumerable<string> sourcePaths)
        {
            if (_currentAssetLibrary is null)
            {
                return 0;
            }

            var backgroundFolderPath = GetBackgroundFolderPath(_currentAssetLibrary);
            _isNormalizingBackgroundImages = true;
            var importedCount = 0;
            try
            {
                importedCount = await _imageSetService.ImportFilesAsync(
                    backgroundFolderPath,
                    sourcePaths,
                    ImportBackgroundImageAsPngAsync);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            if (importedCount == 0)
            {
                return 0;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            return importedCount;
        }

        private async void AddMusicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var extension in AudioAssetService.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            try
            {
                var importedCount = ImportMusicFiles(selectedFiles.Select(file => file.Path));
                ShowFloatingTip(
                    importedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    importedCount > 0 ? "音乐已导入" : "没有导入音乐",
                    importedCount > 0 ? $"已导入 {importedCount} 个文件。" : "选择的文件没有可导入内容。",
                    logMessage: $"导入音乐：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "音乐导入失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "导入音乐失败。");
            }
        }

        private int ImportMusicFiles(IEnumerable<string> sourcePaths)
        {
            return ImportAudioFiles(AudioAssetKind.Music, sourcePaths);
        }

        private async void AddAmbientSoundButton_Click(object sender, RoutedEventArgs e)
        {
            await AddAudioFilesAsync(AudioAssetKind.Ambient);
        }

        private async void AddSoundEffectButton_Click(object sender, RoutedEventArgs e)
        {
            await AddAudioFilesAsync(AudioAssetKind.SoundEffect);
        }

        private async Task AddAudioFilesAsync(AudioAssetKind kind)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var extension in AudioAssetService.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            var displayName = AudioAssetService.GetDisplayName(kind);
            try
            {
                var importedCount = ImportAudioFiles(kind, selectedFiles.Select(file => file.Path));
                ShowFloatingTip(
                    importedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    importedCount > 0 ? $"{displayName}已导入" : $"没有导入{displayName}",
                    importedCount > 0 ? $"已导入 {importedCount} 个文件。" : "选择的文件没有可导入内容。",
                    logMessage: $"导入{displayName}：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    $"{displayName}导入失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"导入{displayName}失败。");
            }
        }

        private int ImportAudioFiles(AudioAssetKind kind, IEnumerable<string> sourcePaths)
        {
            if (_currentAssetLibrary is null)
            {
                return 0;
            }

            var musicFolderPath = GetAudioFolderPath(_currentAssetLibrary, kind);
            SetAudioNormalizing(kind, true);
            var importedCount = 0;
            try
            {
                importedCount = _audioAssetService.ImportFiles(kind, musicFolderPath, sourcePaths);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            if (importedCount == 0)
            {
                return 0;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            return importedCount;
        }

        private async void AddCharacterClothesButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndImportCharacterLayerAsync(ImportCharacterClothesAsync, "服装", "导入服装失败。");
        }

        private Task<int> ImportCharacterClothesAsync(IEnumerable<string> sourcePaths)
        {
            return ImportCharacterLayerAsync(
                sourcePaths,
                CharacterLayerKind.Cloth,
                string.Empty,
                () => _isNormalizingCharacterClothes = true,
                () => _isNormalizingCharacterClothes = false,
                character => character.Code);
        }

        private async void AddCharacterFacesButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndImportCharacterLayerAsync(ImportCharacterFacesAsync, "表情", "导入表情失败。");
        }

        private Task<int> ImportCharacterFacesAsync(IEnumerable<string> sourcePaths)
        {
            return ImportCharacterLayerAsync(
                sourcePaths,
                CharacterLayerKind.Face,
                string.Empty,
                () => _isNormalizingCharacterFaces = true,
                () => _isNormalizingCharacterFaces = false);
        }

        private async void AddCharacterAdornsButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndImportCharacterLayerAsync(ImportCharacterAdornsAsync, "装饰", "导入装饰失败。");
        }

        private async Task PickAndImportCharacterLayerAsync(
            Func<IEnumerable<string>, Task<int>> importAsync,
            string logLabel,
            string errorMessage)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in ImageSetService.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            try
            {
                var importedCount = await importAsync(selectedFiles.Select(file => file.Path));
                ShowFloatingTip(
                    importedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    importedCount > 0 ? $"{logLabel}已导入" : $"没有导入{logLabel}",
                    importedCount > 0 ? $"已导入 {importedCount} 个文件。" : "选择的文件没有可导入内容。",
                    logMessage: $"导入{logLabel}：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    $"{logLabel}导入失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    errorMessage);
            }
        }

        private Task<int> ImportCharacterAdornsAsync(IEnumerable<string> sourcePaths)
        {
            return ImportCharacterLayerAsync(
                sourcePaths,
                CharacterLayerKind.Adorn,
                string.Empty,
                () => _isNormalizingCharacterAdorns = true,
                () => _isNormalizingCharacterAdorns = false);
        }

        private Task<int> ImportCharacterLayerAsync(
            IEnumerable<string> sourcePaths,
            CharacterLayerKind layerKind,
            string defaultScope,
            Action beginNormalize,
            Action endNormalize,
            Func<CharacterInfo, string?>? characterCodeSelector = null)
        {
            if (_currentCharacter is null)
            {
                return Task.FromResult(0);
            }

            var folderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, layerKind);
            var characterCode = characterCodeSelector?.Invoke(_currentCharacter);
            var importedCount = 0;
            beginNormalize();
            try
            {
                var entries = _characterLayerAssetService.CreateImportEntries(
                    folderPath,
                    layerKind,
                    sourcePaths,
                    defaultScope,
                    out importedCount);
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, layerKind, characterCode);
            }
            finally
            {
                endNormalize();
            }

            if (importedCount == 0)
            {
                return Task.FromResult(0);
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            return Task.FromResult(importedCount);
        }

    }
}
