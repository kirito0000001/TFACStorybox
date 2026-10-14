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
        /// <summary>
        /// 这张图**自己所在**的分类文件夹。
        ///
        /// 图片永远躺在它那个分类的目录里（<c>&lt;素材库&gt;/背景图/</c> 或 <c>&lt;素材库&gt;/道具图片/</c>），
        /// 所以"所在目录"就是"它的分类文件夹"—— 不用再去问"这是背景图还是道具图片"，
        /// 也就不会出现"处理道具图片时却去查背景图目录"这种串味。
        /// </summary>
        private static string GetImageSetFolderOf(string imagePath) =>
            Path.GetDirectoryName(imagePath) ?? string.Empty;

        /// <summary>
        /// 按图片所在的分类，刷新对应的卡片列表。
        ///
        /// 为什么不用一个统一的 RefreshImageSetCards：两个分类的 GridView 是两个控件，
        /// 各自有自己的懒加载状态（没展开就不铺卡片），刷错那个等于白刷。
        /// </summary>
        private void RefreshImageSetCardsFor(string imagePath)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var folder = GetImageSetFolderOf(imagePath);
            if (string.Equals(folder, GetBackgroundFolderPath(_currentAssetLibrary), StringComparison.OrdinalIgnoreCase))
            {
                RefreshBackgroundImageCards(_currentAssetLibrary);
            }
            else if (string.Equals(folder, GetPropImageFolderPath(_currentAssetLibrary), StringComparison.OrdinalIgnoreCase))
            {
                RefreshPropImageCards(_currentAssetLibrary);
            }
        }

        private async Task<string?> SetBackgroundImageRemarkAsync(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return null;
            }

            var parsed = ImageSetService.ParseFileName(imagePath);
            var remarkInput = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                "设置背景图备注",
                "备注",
                parsed.Remark,
                "例如：我是备注"));
            if (remarkInput is null)
            {
                return null;
            }

            var folderPath = GetImageSetFolderOf(imagePath);
            string? updatedPath;
            _isNormalizingBackgroundImages = true;
            try
            {
                updatedPath = _imageSetService.UpdateRemark(folderPath, imagePath, remarkInput);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshImageSetCardsFor(imagePath);
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "背景图备注已保存",
                Path.GetFileName(updatedPath ?? imagePath),
                logMessage: $"已设置背景图备注：{Path.GetFileName(imagePath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteBackgroundImageAsync(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return false;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "删除背景图",
                $"确定删除 {Path.GetFileName(imagePath)} 吗？",
                "删除",
                "取消",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
            if (!confirmed)
            {
                return false;
            }

            _isNormalizingBackgroundImages = true;
            try
            {
                _imageSetService.DeleteAndNormalize(GetImageSetFolderOf(imagePath), imagePath);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshImageSetCardsFor(imagePath);
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "背景图已删除",
                Path.GetFileName(imagePath),
                logMessage: $"已删除背景图：{Path.GetFileName(imagePath)}");
            return true;
        }

        private async Task<bool> ReplaceBackgroundImageAsync(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return false;
            }

            try
            {
                var sourcePath = await PickReplacementFileAsync(ImageSetService.Extensions, PickerLocationId.PicturesLibrary);
                if (sourcePath is null)
                {
                    return false;
                }

                if (!ImageSetService.IsValidSourcePath(sourcePath))
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "背景图未替换",
                        $"不支持文件 {Path.GetFileName(sourcePath)}。",
                        logMessage: $"替换背景图失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                    return false;
                }

                _isNormalizingBackgroundImages = true;
                try
                {
                    await ImportBackgroundImageAsPngAsync(sourcePath, imagePath);
                }
                finally
                {
                    _isNormalizingBackgroundImages = false;
                }

                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
                RefreshImageSetCardsFor(imagePath);
                RequestDelayedRefresh();
                if (_viewingBackgroundImagePath is not null && PathsEqual(_viewingBackgroundImagePath, imagePath))
                {
                    await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, imagePath);
                }

                var logMessage = $"已替换背景图素材：{Path.GetFileName(imagePath)} <- {Path.GetFileName(sourcePath)}";
                ShowFloatingTip(InfoBarSeverity.Success, "背景图已替换", Path.GetFileName(imagePath), logMessage: logMessage);
                return true;
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "背景图替换失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"替换背景图失败：{Path.GetFileName(imagePath)}");
                return false;
            }
        }

        private async Task<string?> PromptRemarkAsync(string title, string currentRemark, string placeholderText)
        {
            return await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                title,
                "备注",
                currentRemark,
                placeholderText));
        }

        private async Task<bool> ConfirmDeleteAsync(string title, string message)
        {
            return await _dialogService.ConfirmAsync(new DialogRequest(
                title,
                message,
                "删除",
                "取消",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
        }

        private async Task<string?> SetCharacterClothRemarkAsync(string clothPath)
        {
            if (_currentCharacter is null || !File.Exists(clothPath))
            {
                return null;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            var parsed = CharacterLayerAssetService.ParseFileName(clothPath, CharacterLayerKind.Cloth, string.Empty);
            var remark = await PromptRemarkAsync("设置服装备注", parsed.Remark, "例如：校服");
            if (remark is null)
            {
                return null;
            }

            var clothFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Cloth);
            var updatedEntries = _characterLayerAssetService.CreateRemarkEntries(
                clothFolderPath,
                clothPath,
                CharacterLayerKind.Cloth,
                string.Empty,
                remark);
            var updatedIndex = _characterLayerAssetService.FindEntryIndex(updatedEntries, clothPath);

            _isNormalizingCharacterClothes = true;
            try
            {
                _characterLayerAssetService.RenameEntries(updatedEntries, CharacterLayerKind.Cloth, _currentCharacter.Code);
            }
            finally
            {
                _isNormalizingCharacterClothes = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            var updatedPath = updatedIndex >= 0
                ? CharacterLayerAssetService.GetTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Cloth, _currentCharacter.Code)
                : null;
            ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "服装备注已保存",
                Path.GetFileName(updatedPath ?? clothPath),
                logMessage: $"已设置服装备注：{Path.GetFileName(clothPath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteCharacterClothAsync(string clothPath)
        {
            if (_currentCharacter is null || !File.Exists(clothPath))
            {
                return false;
            }

            var confirmed = await ConfirmDeleteAsync("删除服装", $"确定删除 {Path.GetFileName(clothPath)} 吗？");
            if (!confirmed)
            {
                return false;
            }

            _isNormalizingCharacterClothes = true;
            try
            {
                var entries = _characterLayerAssetService.DeleteFileAndCreateRemainingEntries(
                    clothPath,
                    CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Cloth),
                    CharacterLayerKind.Cloth,
                    string.Empty);
                _characterLayerAssetService.RenameEntries(entries, CharacterLayerKind.Cloth, _currentCharacter.Code);
            }
            finally
            {
                _isNormalizingCharacterClothes = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            ShowFloatingTip(InfoBarSeverity.Success, "服装已删除", Path.GetFileName(clothPath), logMessage: $"已删除服装：{Path.GetFileName(clothPath)}");
            return true;
        }

        private async Task<string?> SetCharacterFaceRemarkAsync(string facePath)
        {
            if (_currentCharacter is null || !File.Exists(facePath))
            {
                return null;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            var parsed = CharacterLayerAssetService.ParseFileName(facePath, CharacterLayerKind.Face, string.Empty);
            var remark = await PromptRemarkAsync("设置表情备注", parsed.Remark, "例如：微笑");
            if (remark is null)
            {
                return null;
            }

            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Face);
            var updatedEntries = _characterLayerAssetService.CreateRemarkEntries(
                faceFolderPath,
                facePath,
                CharacterLayerKind.Face,
                string.Empty,
                remark);
            var updatedIndex = _characterLayerAssetService.FindEntryIndex(updatedEntries, facePath);

            _isNormalizingCharacterFaces = true;
            try
            {
                _characterLayerAssetService.RenameEntriesAndScopeMeta(updatedEntries, CharacterLayerKind.Face);
            }
            finally
            {
                _isNormalizingCharacterFaces = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            var updatedPath = updatedIndex >= 0
                ? CharacterLayerAssetService.GetTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Face)
                : null;
            ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "表情备注已保存",
                Path.GetFileName(updatedPath ?? facePath),
                logMessage: $"已设置表情备注：{Path.GetFileName(facePath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteCharacterFaceAsync(string facePath)
        {
            if (_currentCharacter is null || !File.Exists(facePath))
            {
                return false;
            }

            var confirmed = await ConfirmDeleteAsync("删除表情", $"确定删除 {Path.GetFileName(facePath)} 吗？");
            if (!confirmed)
            {
                return false;
            }

            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Face);
            _characterLayerAssetService.RemoveScopeEntry(faceFolderPath, CharacterLayerKind.Face, Path.GetFileName(facePath));

            _isNormalizingCharacterFaces = true;
            try
            {
                var entries = _characterLayerAssetService.DeleteFileAndCreateRemainingEntries(
                    facePath,
                    faceFolderPath,
                    CharacterLayerKind.Face,
                    string.Empty);
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, CharacterLayerKind.Face);
            }
            finally
            {
                _isNormalizingCharacterFaces = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            ShowFloatingTip(InfoBarSeverity.Success, "表情已删除", Path.GetFileName(facePath), logMessage: $"已删除表情：{Path.GetFileName(facePath)}");
            return true;
        }

        private async Task SetCharacterFaceAvailabilityAsync(string facePath)
        {
            if (_currentCharacter is null || !File.Exists(facePath))
            {
                return;
            }

            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Face);
            await SetCharacterLayerAvailabilityAsync(
                facePath,
                faceFolderPath,
                CharacterLayerKind.Face,
                "表情可用范围",
                "表情");
        }

        private async Task SetCharacterLayerAvailabilityAsync(
            string layerPath,
            string layerFolderPath,
            CharacterLayerKind layerKind,
            string title,
            string logLabel)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return;
            }

            var clothFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Cloth);
            var clothPaths = CharacterLayerAssetService.GetImagePaths(clothFolderPath);
            if (clothPaths.Count == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    $"无法设置{logLabel}",
                    "请先导入至少一套服装。",
                    logMessage: $"还没有服装，暂时无法设置{logLabel}可用范围。");
                return;
            }

            var meta = _characterLayerAssetService.ReadScopeMeta(layerFolderPath, layerKind);
            var layerFileName = Path.GetFileName(layerPath);
            var existingEntry = meta.Entries.TryGetValue(layerFileName, out var savedEntry)
                ? savedEntry
                : new CharacterLayerScopeEntry { UseAllCostumes = true };
            var dialogContent = CharacterDialogContentFactory.CreateCharacterLayerAvailabilityContent(
                clothPaths,
                existingEntry,
                ComputeFileHash,
                clothPath => ThumbnailFactory.CreateThumbnail(clothPath, 120, 150, showAddIcon: false),
                PlaySelectionSound);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                title,
                dialogContent.Content,
                "确定",
                "取消"));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            var checkedHashes = dialogContent.ReadCheckedHashes();
            _characterLayerAssetService.SaveScopeEntry(layerFolderPath, layerKind, layerFileName, new CharacterLayerScopeEntry
            {
                UseAllCostumes = checkedHashes.Count == clothPaths.Count,
                CostumeHashes = checkedHashes.Count == clothPaths.Count ? [] : checkedHashes
            });
            await UpdateCharacterLayerPreviewAsync();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                $"{logLabel}范围已保存",
                layerFileName,
                logMessage: $"已设置{logLabel}可用范围：{layerFileName}");
        }

        private async Task<string?> SetCharacterAdornRemarkAsync(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return null;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            var parsed = CharacterLayerAssetService.ParseFileName(adornPath, CharacterLayerKind.Adorn, string.Empty);
            var remark = await PromptRemarkAsync("设置装饰备注", parsed.Remark, "例如：帽子");
            if (remark is null)
            {
                return null;
            }

            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Adorn);
            var updatedEntries = _characterLayerAssetService.CreateRemarkEntries(
                adornFolderPath,
                adornPath,
                CharacterLayerKind.Adorn,
                string.Empty,
                remark);
            var updatedIndex = _characterLayerAssetService.FindEntryIndex(updatedEntries, adornPath);

            _isNormalizingCharacterAdorns = true;
            try
            {
                _characterLayerAssetService.RenameEntriesAndScopeMeta(updatedEntries, CharacterLayerKind.Adorn);
            }
            finally
            {
                _isNormalizingCharacterAdorns = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            var updatedPath = updatedIndex >= 0
                ? CharacterLayerAssetService.GetTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Adorn)
                : null;
            ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "装饰备注已保存",
                Path.GetFileName(updatedPath ?? adornPath),
                logMessage: $"已设置装饰备注：{Path.GetFileName(adornPath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteCharacterAdornAsync(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return false;
            }

            var confirmed = await ConfirmDeleteAsync("删除装饰", $"确定删除 {Path.GetFileName(adornPath)} 吗？");
            if (!confirmed)
            {
                return false;
            }

            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Adorn);
            _characterLayerAssetService.RemoveScopeEntry(adornFolderPath, CharacterLayerKind.Adorn, Path.GetFileName(adornPath));

            _isNormalizingCharacterAdorns = true;
            try
            {
                var entries = _characterLayerAssetService.DeleteFileAndCreateRemainingEntries(
                    adornPath,
                    adornFolderPath,
                    CharacterLayerKind.Adorn,
                    string.Empty);
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, CharacterLayerKind.Adorn);
            }
            finally
            {
                _isNormalizingCharacterAdorns = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            ShowFloatingTip(InfoBarSeverity.Success, "装饰已删除", Path.GetFileName(adornPath), logMessage: $"已删除装饰：{Path.GetFileName(adornPath)}");
            return true;
        }

        private async Task<bool> ReplaceCharacterLayerAsync(CharacterLayerKind layerKind, string layerPath)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return false;
            }

            var displayName = GetCharacterLayerDisplayName(layerKind);
            try
            {
                var sourcePath = await PickReplacementFileAsync(ImageSetService.Extensions, PickerLocationId.PicturesLibrary);
                if (sourcePath is null)
                {
                    return false;
                }

                if (!ImageSetService.IsValidSourcePath(sourcePath))
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        $"{displayName}未替换",
                        $"不支持文件 {Path.GetFileName(sourcePath)}。",
                        logMessage: $"替换{displayName}失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                    return false;
                }

                var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
                var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
                File.Copy(sourcePath, layerPath, overwrite: true);

                if (_currentAssetLibrary is not null)
                {
                    TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
                }

                SetSelectedCharacterLayer(layerKind, layerPath);
                ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
                await UpdateCharacterLayerPreviewAsync();
                RequestDelayedRefresh();

                if (_viewingCharacterLayer is { } viewingLayer &&
                    viewingLayer.Kind == layerKind &&
                    PathsEqual(viewingLayer.Path, layerPath))
                {
                    await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, layerPath);
                }

                if (_isViewingCharacterComposite)
                {
                    await LoadCharacterCompositeViewerImagesAsync();
                }

                var logMessage = $"已替换{displayName}素材：{Path.GetFileName(layerPath)} <- {Path.GetFileName(sourcePath)}";
                ShowFloatingTip(InfoBarSeverity.Success, $"{displayName}已替换", Path.GetFileName(layerPath), logMessage: logMessage);
                return true;
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    $"{displayName}替换失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"替换{displayName}失败：{Path.GetFileName(layerPath)}");
                return false;
            }
        }

        private async Task SetCharacterPortraitPreviewAsync(CharacterLayerKind layerKind, string layerPath)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return;
            }

            var sourcePath = await PickReplacementFileAsync(ImageSetService.Extensions, PickerLocationId.PicturesLibrary);
            if (sourcePath is null)
            {
                return;
            }

            if (!ImageSetService.IsValidSourcePath(sourcePath))
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "小预览未设置",
                    $"不支持文件 {Path.GetFileName(sourcePath)}。",
                    logMessage: $"设置{GetCharacterLayerDisplayName(layerKind)}小预览失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                return;
            }

            _characterLayerAssetService.SetPortraitPreview(_currentCharacter, Path.GetFileName(layerPath), sourcePath);
            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            CharacterDetailInfoBar.Severity = InfoBarSeverity.Success;
            CharacterDetailInfoBar.Title = "已设置小预览";
            CharacterDetailInfoBar.Message = $"{Path.GetFileNameWithoutExtension(layerPath)} <- {Path.GetFileName(sourcePath)}";
            CharacterDetailInfoBar.IsOpen = true;
            ReloadCharacterDetailLayersPreservingScroll();
            if (_currentAssetLibrary is not null)
            {
                LoadCharacters(_currentAssetLibrary);
            }

            RequestDelayedRefresh();
            var displayName = GetCharacterLayerDisplayName(layerKind);
            ShowFloatingTip(
                InfoBarSeverity.Success,
                $"{displayName}小预览已设置",
                Path.GetFileName(layerPath),
                logMessage: $"已设置{displayName}小预览：{Path.GetFileName(layerPath)} <- {Path.GetFileName(sourcePath)}");
        }

        private async Task SetCharacterAdornAvailabilityAsync(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return;
            }

            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Adorn);
            await SetCharacterLayerAvailabilityAsync(
                adornPath,
                adornFolderPath,
                CharacterLayerKind.Adorn,
                "装饰可用范围",
                "装饰");
        }

        private async Task<string?> SetMusicRemarkAsync(string musicPath)
        {
            return await SetAudioRemarkAsync(AudioAssetKind.Music, musicPath);
        }

        private async Task<string?> SetAudioRemarkAsync(AudioAssetKind kind, string musicPath)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return null;
            }

            var parsed = AudioAssetService.ParseFileName(kind, musicPath);
            var remarkInput = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                $"设置{AudioAssetService.GetDisplayName(kind)}备注",
                "备注",
                parsed.Remark,
                kind == AudioAssetKind.Music ? "例如：主题曲" : "例如：雨声"));
            if (remarkInput is null)
            {
                return null;
            }

            var folderPath = GetAudioFolderPath(_currentAssetLibrary, kind);
            string? renamedPath;
            SetAudioNormalizing(kind, true);
            try
            {
                renamedPath = _audioAssetService.UpdateRemark(kind, folderPath, musicPath, remarkInput);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            var displayName = AudioAssetService.GetDisplayName(kind);
            ShowFloatingTip(
                InfoBarSeverity.Success,
                $"{displayName}备注已保存",
                Path.GetFileName(renamedPath ?? musicPath),
                logMessage: $"已设置{displayName}备注：{Path.GetFileName(musicPath)}");
            return renamedPath;
        }

        private async Task<bool> DeleteMusicAsync(string musicPath)
        {
            return await DeleteAudioAsync(AudioAssetKind.Music, musicPath);
        }

        private async Task<bool> DeleteAudioAsync(AudioAssetKind kind, string musicPath)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return false;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                $"删除{AudioAssetService.GetDisplayName(kind)}",
                $"确定删除 {Path.GetFileName(musicPath)} 吗？",
                "删除",
                "取消",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
            if (!confirmed)
            {
                return false;
            }

            SetAudioNormalizing(kind, true);
            try
            {
                _audioAssetService.DeleteAndNormalize(kind, GetAudioFolderPath(_currentAssetLibrary, kind), musicPath);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            var displayName = AudioAssetService.GetDisplayName(kind);
            ShowFloatingTip(
                InfoBarSeverity.Success,
                $"{displayName}已删除",
                Path.GetFileName(musicPath),
                logMessage: $"已删除{displayName}：{Path.GetFileName(musicPath)}");
            return true;
        }

        private async Task<bool> ReplaceAudioAsync(AudioAssetKind kind, string musicPath)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return false;
            }

            var displayName = AudioAssetService.GetDisplayName(kind);
            try
            {
                var sourcePath = await PickReplacementFileAsync(AudioAssetService.Extensions, PickerLocationId.MusicLibrary);
                if (sourcePath is null)
                {
                    return false;
                }

                if (!AudioAssetService.IsValidAudioPath(sourcePath))
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        $"{displayName}未替换",
                        $"不支持文件 {Path.GetFileName(sourcePath)}。",
                        logMessage: $"替换{displayName}失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                    return false;
                }

                SetAudioNormalizing(kind, true);
                try
                {
                    File.Copy(sourcePath, musicPath, overwrite: true);
                    AudioAssetService.DeleteIgnoredSidecarFiles(kind, GetAudioFolderPath(_currentAssetLibrary, kind));
                }
                finally
                {
                    SetAudioNormalizing(kind, false);
                }

                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
                RefreshAudioCards(_currentAssetLibrary, kind);
                RequestDelayedRefresh();
                if (_playingMusicPath is not null && PathsEqual(_playingMusicPath, musicPath))
                {
                    MusicPlayerElement.MediaPlayer.Pause();
                    var file = await StorageFile.GetFileFromPathAsync(musicPath);
                    MusicPlayerElement.Source = MediaSource.CreateFromStorageFile(file);
                    MusicPlayPauseButton.Content = "播放";
                }

                var logMessage = $"已替换{displayName}素材：{Path.GetFileName(musicPath)} <- {Path.GetFileName(sourcePath)}";
                ShowFloatingTip(InfoBarSeverity.Success, $"{displayName}已替换", Path.GetFileName(musicPath), logMessage: logMessage);
                return true;
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    $"{displayName}替换失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"替换{displayName}失败：{Path.GetFileName(musicPath)}");
                return false;
            }
        }

        private async Task NormalizeMusicFilesAsync(string musicFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            await NormalizeAudioFilesAsync(AudioAssetKind.Music, musicFolderPath, orderedPaths);
        }

        private async Task NormalizeAudioFilesAsync(AudioAssetKind kind, string musicFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            _audioAssetService.NormalizeFiles(kind, musicFolderPath, orderedPaths);
            await Task.CompletedTask;
        }

    }
}
