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
        private async void BackgroundImagesGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentAssetLibrary is null || _isNormalizingBackgroundImages)
            {
                _draggingBackgroundImageItem = null;
                return;
            }

            var orderedPaths = GetOrderedExistingTaggedPaths(BackgroundImagesGridView);

            if (orderedPaths.Count == 0)
            {
                _draggingBackgroundImageItem = null;
                return;
            }

            // ⚠️ 映射按**位置**算，不按文件名里的编号（剧情里的 BGindex 是位置）。
            // 旧位置 = 改名**之前** GetFilePaths 的下标；新位置 = 这次拖出来的顺序里的下标
            //（改完名后 BG{i} 正好排在位置 i，两者是同一个坐标系）。
            var beforeOrderedPaths = ImageSetService.GetFilePaths(GetBackgroundFolderPath(_currentAssetLibrary));
            var indexRemap = BuildPositionalAssetIndexRemap(beforeOrderedPaths, orderedPaths);
            var assetLabels = BuildPositionalLabelMaps(beforeOrderedPaths, orderedPaths);
            LogAssetIndexRemapEvidence("背景图", beforeOrderedPaths, orderedPaths);
            _isNormalizingBackgroundImages = true;
            try
            {
                await NormalizeBackgroundImagesAsync(GetBackgroundFolderPath(_currentAssetLibrary), orderedPaths);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            var syncResult = await SyncStoryGlobalAssetIndexesWithProgressAsync(
                _currentAssetLibrary,
                "背景图",
                "BGindex",
                indexRemap,
                assetLabels.OldLabels,
                assetLabels.NewLabels,
                orderedPaths.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的背景图索引。");
            }
            AppendLog(LogKind.User, "已调整背景图顺序并触发自动命名。");
            _draggingBackgroundImageItem = null;
        }

        private void BackgroundImagesGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            _draggingBackgroundImageItem = ResolveDraggedGridViewItem(BackgroundImagesGridView, e);

            AppendLog(
                LogKind.Info,
                _draggingBackgroundImageItem is null
                    ? $"背景图拖拽开始，但未识别拖拽项。Items[0]={e.Items.FirstOrDefault()?.GetType().Name ?? "null"}"
                    : $"背景图拖拽开始：{Path.GetFileName(_draggingBackgroundImageItem.Tag as string)}");
        }

        private void BackgroundImagesGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingBackgroundImageItem is null || BackgroundImagesGridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;

            var pointerPosition = e.GetPosition(BackgroundImagesGridView);
            if (!IsPointerInTrailingBlankArea(pointerPosition, _draggingBackgroundImageItem))
            {
                return;
            }

            MoveDraggingBackgroundImageToEnd();
        }

        private void BackgroundImagesDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingBackgroundImageItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void BackgroundImagesDropZone_DragEnter(object sender, DragEventArgs e)
        {
            var containsStorageItems = e.DataView.Contains(StandardDataFormats.StorageItems);
            AppendLog(
                LogKind.Info,
                _draggingBackgroundImageItem is not null
                    ? "拖入背景图区：检测到内部背景图拖拽。"
                    : $"拖入背景图区：外部数据进入，StorageItems={containsStorageItems}。");

            if (_draggingBackgroundImageItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (containsStorageItems)
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async void MusicGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await AudioGridView_DragItemsCompleted(AudioAssetKind.Music);
        }

        private void MusicGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            AudioGridView_DragItemsStarting(AudioAssetKind.Music, sender, e);
        }

        private void MusicGridView_DragOver(object sender, DragEventArgs e)
        {
            AudioGridView_DragOver(AudioAssetKind.Music, e);
        }

        private void MusicDropZone_DragEnter(object sender, DragEventArgs e)
        {
            AudioDropZone_DragEnter(AudioAssetKind.Music, e);
        }

        private void MusicDropZone_DragOver(object sender, DragEventArgs e)
        {
            AudioDropZone_DragOver(AudioAssetKind.Music, e);
        }

        private async void MusicDropZone_Drop(object sender, DragEventArgs e)
        {
            await AudioDropZone_Drop(AudioAssetKind.Music, e);
        }

        private async void AmbientSoundGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await AudioGridView_DragItemsCompleted(AudioAssetKind.Ambient);
        }

        private void AmbientSoundGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            AudioGridView_DragItemsStarting(AudioAssetKind.Ambient, sender, e);
        }

        private void AmbientSoundGridView_DragOver(object sender, DragEventArgs e)
        {
            AudioGridView_DragOver(AudioAssetKind.Ambient, e);
        }

        private void AmbientSoundDropZone_DragEnter(object sender, DragEventArgs e)
        {
            AudioDropZone_DragEnter(AudioAssetKind.Ambient, e);
        }

        private void AmbientSoundDropZone_DragOver(object sender, DragEventArgs e)
        {
            AudioDropZone_DragOver(AudioAssetKind.Ambient, e);
        }

        private async void AmbientSoundDropZone_Drop(object sender, DragEventArgs e)
        {
            await AudioDropZone_Drop(AudioAssetKind.Ambient, e);
        }

        private async void SoundEffectGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await AudioGridView_DragItemsCompleted(AudioAssetKind.SoundEffect);
        }

        private void SoundEffectGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            AudioGridView_DragItemsStarting(AudioAssetKind.SoundEffect, sender, e);
        }

        private void SoundEffectGridView_DragOver(object sender, DragEventArgs e)
        {
            AudioGridView_DragOver(AudioAssetKind.SoundEffect, e);
        }

        private void SoundEffectDropZone_DragEnter(object sender, DragEventArgs e)
        {
            AudioDropZone_DragEnter(AudioAssetKind.SoundEffect, e);
        }

        private void SoundEffectDropZone_DragOver(object sender, DragEventArgs e)
        {
            AudioDropZone_DragOver(AudioAssetKind.SoundEffect, e);
        }

        private async void SoundEffectDropZone_Drop(object sender, DragEventArgs e)
        {
            await AudioDropZone_Drop(AudioAssetKind.SoundEffect, e);
        }

        private async void CharacterFilterGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentAssetLibrary is null || _isReorderingCharacterFilters)
            {
                _draggingCharacterFilterItem = null;
                return;
            }

            var filters = CharacterFilterGridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as CharacterFilterEntry)
                .Where(entry => entry is not null)
                .Cast<CharacterFilterEntry>()
                .ToList();
            filters = CharacterFilterService.Normalize(filters);
            if (filters.Count == 0)
            {
                _draggingCharacterFilterItem = null;
                return;
            }

            var oldFilters = _characterFilterService.Read(_currentAssetLibrary);
            var indexRemap = CharacterFilterService.BuildIndexRemap(oldFilters, filters);
            var oldLabels = oldFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
            var newLabels = filters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
            _isReorderingCharacterFilters = true;
            try
            {
                _characterFilterService.Write(_currentAssetLibrary, filters);
            }
            finally
            {
                _isReorderingCharacterFilters = false;
            }

            var syncResult = await SyncStoryCharacterFilterIndexesWithProgressAsync(_currentAssetLibrary, indexRemap, oldLabels, newLabels, filters.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的角色滤镜索引。");
            }
            AppendLog(LogKind.User, "已调整角色滤镜顺序。");
            _draggingCharacterFilterItem = null;
        }

        private void CharacterFilterGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            _draggingCharacterFilterItem = ResolveDraggedGridViewItem(CharacterFilterGridView, e);

            if (_draggingCharacterFilterItem?.Tag is not CharacterFilterEntry)
            {
                _draggingCharacterFilterItem = null;
            }
        }

        private void CharacterFilterGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterFilterItem is null || CharacterFilterGridView.Items.Count <= 2)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private async void CharacterClothGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await CharacterLayerGridView_DragItemsCompleted(
                CharacterClothGridView,
                CharacterLayerKind.Cloth,
                _isNormalizingCharacterClothes,
                () => _isNormalizingCharacterClothes = true,
                () => _isNormalizingCharacterClothes = false,
                () => _draggingCharacterClothItem = null,
                "服装",
                character => character.Code);
        }

        private void CharacterClothGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            CharacterLayerGridView_DragItemsStarting(CharacterClothGridView, e, item => _draggingCharacterClothItem = item);
        }

        private void CharacterClothGridView_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerGridView_DragOver(CharacterClothGridView, _draggingCharacterClothItem, e);
        }

        private void CharacterClothDropZone_DragEnter(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterClothItem, e);
        }

        private void CharacterClothDropZone_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterClothItem, e);
        }

        private async void CharacterClothDropZone_Drop(object sender, DragEventArgs e)
        {
            await CharacterLayerDropZone_Drop(
                e,
                _draggingCharacterClothItem,
                () => MoveGridViewItemToEnd(CharacterClothGridView, _draggingCharacterClothItem),
                ImportCharacterClothesAsync,
                "服装");
        }

        private async void CharacterFaceGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await CharacterLayerGridView_DragItemsCompleted(
                CharacterFaceGridView,
                CharacterLayerKind.Face,
                _isNormalizingCharacterFaces,
                () => _isNormalizingCharacterFaces = true,
                () => _isNormalizingCharacterFaces = false,
                () => _draggingCharacterFaceItem = null,
                "表情");
        }

        private void CharacterFaceGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            CharacterLayerGridView_DragItemsStarting(CharacterFaceGridView, e, item => _draggingCharacterFaceItem = item);
        }

        private void CharacterFaceGridView_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerGridView_DragOver(CharacterFaceGridView, _draggingCharacterFaceItem, e);
        }

        private void CharacterFaceDropZone_DragEnter(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterFaceItem, e);
        }

        private void CharacterFaceDropZone_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterFaceItem, e);
        }

        private async void CharacterFaceDropZone_Drop(object sender, DragEventArgs e)
        {
            await CharacterLayerDropZone_Drop(
                e,
                _draggingCharacterFaceItem,
                () => MoveGridViewItemToEnd(CharacterFaceGridView, _draggingCharacterFaceItem),
                ImportCharacterFacesAsync,
                "表情");
        }

        private async void CharacterAdornGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await CharacterLayerGridView_DragItemsCompleted(
                CharacterAdornGridView,
                CharacterLayerKind.Adorn,
                _isNormalizingCharacterAdorns,
                () => _isNormalizingCharacterAdorns = true,
                () => _isNormalizingCharacterAdorns = false,
                () => _draggingCharacterAdornItem = null,
                "装饰");
        }

        private async Task CharacterLayerGridView_DragItemsCompleted(
            GridView gridView,
            CharacterLayerKind layerKind,
            bool isNormalizing,
            Action beginNormalize,
            Action endNormalize,
            Action clearDraggingItem,
            string logLabel,
            Func<CharacterInfo, string?>? characterCodeSelector = null)
        {
            if (_currentCharacter is null || isNormalizing)
            {
                clearDraggingItem();
                return;
            }

            var orderedPaths = GetOrderedExistingTaggedPaths(gridView);
            if (orderedPaths.Count == 0)
            {
                clearDraggingItem();
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => CharacterLayerAssetService.GetIndex(path, layerKind));
            var assetLabels = StoryAssetIndexSyncService.BuildLabelMaps(orderedPaths, path => CharacterLayerAssetService.GetIndex(path, layerKind));
            beginNormalize();
            try
            {
                var entries = orderedPaths
                    .Select(path => CharacterLayerAssetService.ParseFileName(path, layerKind, string.Empty))
                    .ToList();
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, layerKind, characterCodeSelector?.Invoke(_currentCharacter));
            }
            finally
            {
                endNormalize();
            }

            if (_currentAssetLibrary is not null)
            {
                var syncResult = await SyncStoryCharacterLayerIndexesWithProgressAsync(
                    _currentAssetLibrary,
                    _currentCharacter,
                    layerKind,
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    orderedPaths.Count);
                if (syncResult.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的{logLabel}索引。");
                }

                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已调整{logLabel}顺序并触发自动命名。");
            clearDraggingItem();
        }

        private static void CharacterLayerDropZone_DragEnterOrOver(GridViewItem? draggingItem, DragEventArgs e)
        {
            if (draggingItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void CharacterAdornGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            CharacterLayerGridView_DragItemsStarting(CharacterAdornGridView, e, item => _draggingCharacterAdornItem = item);
        }

        private static void CharacterLayerGridView_DragItemsStarting(
            GridView gridView,
            DragItemsStartingEventArgs e,
            Action<GridViewItem?> setDraggingItem)
        {
            setDraggingItem(ResolveDraggedGridViewItem(gridView, e));
        }

        private void CharacterAdornGridView_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerGridView_DragOver(CharacterAdornGridView, _draggingCharacterAdornItem, e);
        }

        private void CharacterAdornDropZone_DragEnter(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterAdornItem, e);
        }

        private void CharacterAdornDropZone_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterAdornItem, e);
        }

        private async void CharacterAdornDropZone_Drop(object sender, DragEventArgs e)
        {
            await CharacterLayerDropZone_Drop(
                e,
                _draggingCharacterAdornItem,
                () => MoveGridViewItemToEnd(CharacterAdornGridView, _draggingCharacterAdornItem),
                ImportCharacterAdornsAsync,
                "装饰");
        }

        private async Task CharacterLayerDropZone_Drop(
            DragEventArgs e,
            GridViewItem? draggingItem,
            Action moveDraggingItemToEnd,
            Func<IEnumerable<string>, Task<int>> importAsync,
            string logLabel)
        {
            if (draggingItem is not null)
            {
                moveDraggingItemToEnd();
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedPaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => ImageSetService.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await importAsync(droppedPaths);
                if (importedCount > 0)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Success,
                        $"{logLabel}已导入",
                        $"已拖入导入 {importedCount} 个文件。",
                        logMessage: $"拖入导入{logLabel}：{importedCount} 个文件。");
                }
                else
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        $"没有导入{logLabel}",
                        "拖入内容中没有可导入的图片文件。",
                        logMessage: $"拖入内容中没有可导入的{logLabel}图片文件。");
                }
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    $"{logLabel}导入失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"拖入导入{logLabel}失败。");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private static void CharacterLayerGridView_DragOver(GridView gridView, GridViewItem? draggingItem, DragEventArgs e)
        {
            if (draggingItem is null || gridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            var pointerPosition = e.GetPosition(gridView);
            if (IsPointerInTrailingBlankArea(gridView, pointerPosition, draggingItem))
            {
                MoveGridViewItemToEnd(gridView, draggingItem);
            }
        }

        private void CharacterLayerGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var imagePath = GetImagePathFromCharacterLayerClickedItem(e.ClickedItem);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                AppendLog(LogKind.Warning, $"点击立绘图层时未识别图片路径：{e.ClickedItem?.GetType().Name ?? "null"}");
                return;
            }

            PlaySelectionSound();
            if (ReferenceEquals(sender, CharacterClothGridView))
            {
                ShowCharacterLayerViewerPage(imagePath, CharacterLayerKind.Cloth);
            }
            else if (ReferenceEquals(sender, CharacterFaceGridView))
            {
                ShowCharacterLayerViewerPage(imagePath, CharacterLayerKind.Face);
            }
            else if (ReferenceEquals(sender, CharacterAdornGridView))
            {
                ShowCharacterLayerViewerPage(imagePath, CharacterLayerKind.Adorn);
            }
        }

        private static string? GetImagePathFromCharacterLayerClickedItem(object? clickedItem)
        {
            if (clickedItem is GridViewItem gridViewItem)
            {
                if (gridViewItem.Tag is string itemPath)
                {
                    return itemPath;
                }

                if (gridViewItem.Content is FrameworkElement contentElement &&
                    contentElement.Tag is string contentPath)
                {
                    return contentPath;
                }
            }

            if (clickedItem is FrameworkElement element)
            {
                if (element.Tag is string elementPath)
                {
                    return elementPath;
                }

                if (element.DataContext is GridViewItem dataItem &&
                    dataItem.Tag is string dataItemPath)
                {
                    return dataItemPath;
                }
            }

            return null;
        }

        private void CharacterDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
                AppendLog(LogKind.Info, "拖入立绘区：角色压缩包识别接口已保留，当前版本暂不导入。");
            }
        }

        private void CharacterDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void CharacterDropZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            AppendLog(LogKind.Warning, "角色拖入导入暂未实现：后续会用于识别角色压缩包。");
        }

        private async Task AudioGridView_DragItemsCompleted(AudioAssetKind kind)
        {
            if (_currentAssetLibrary is null || IsAudioNormalizing(kind))
            {
                SetDraggingAudioItem(kind, null);
                return;
            }

            var gridView = GetAudioGridView(kind);
            var orderedPaths = GetOrderedExistingTaggedPaths(gridView);

            if (orderedPaths.Count == 0)
            {
                SetDraggingAudioItem(kind, null);
                return;
            }

            // ⚠️ 映射按**位置**算（剧情里的 BGM / Scene / 特殊音效 Scene_n 全是位置），
            // 而且**每种素材都要同步**：以前只有「环境音」那条同步了 —— 音乐拖完顺序后
            // BGM 列还指着旧位置（= 换完音乐顺序，剧情里的 BGM 全部错位），同一类坑。
            var audioFolderPath = GetAudioFolderPath(_currentAssetLibrary, kind);
            var beforeOrderedPaths = AudioAssetService.GetFilePaths(audioFolderPath);
            var indexRemap = BuildPositionalAssetIndexRemap(beforeOrderedPaths, orderedPaths);
            var assetLabels = BuildPositionalLabelMaps(beforeOrderedPaths, orderedPaths);
            LogAssetIndexRemapEvidence(AudioAssetService.GetDisplayName(kind), beforeOrderedPaths, orderedPaths);
            SetAudioNormalizing(kind, true);
            try
            {
                await NormalizeAudioFilesAsync(kind, audioFolderPath, orderedPaths);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            await SyncStoryAudioIndexesAsync(_currentAssetLibrary, kind, indexRemap, assetLabels, orderedPaths.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已调整{AudioAssetService.GetDisplayName(kind)}顺序并触发自动命名。");
            SetDraggingAudioItem(kind, null);
        }

        private void AudioGridView_DragItemsStarting(AudioAssetKind kind, object sender, DragItemsStartingEventArgs e)
        {
            SetDraggingAudioItem(kind, ResolveDraggedGridViewItem(GetAudioGridView(kind), e));
        }

        private void AudioGridView_DragOver(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is null || GetAudioGridView(kind).Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private void AudioDropZone_DragEnter(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void AudioDropZone_DragOver(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async Task AudioDropZone_Drop(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is not null)
            {
                MoveDraggingAudioToEnd(kind);
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedMusicPaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(AudioAssetService.IsValidAudioPath)
                    .ToList();

                var importedCount = ImportAudioFiles(kind, droppedMusicPaths);
                var displayName = AudioAssetService.GetDisplayName(kind);
                ShowFloatingTip(
                    importedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    importedCount > 0 ? $"{displayName}已导入" : $"没有导入{displayName}",
                    importedCount > 0 ? $"已拖入导入 {importedCount} 个文件。" : "拖入内容中没有可导入的音频文件。",
                    logMessage: importedCount > 0
                        ? $"拖入导入{displayName}：{importedCount} 个文件。"
                        : $"拖入内容中没有可导入的 wav {displayName}文件。");
            }
            catch (Exception ex)
            {
                var displayName = AudioAssetService.GetDisplayName(kind);
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    $"{displayName}导入失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"拖入导入{displayName}失败。");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void MoveDraggingAudioToEnd(AudioAssetKind kind)
        {
            MoveGridViewItemToEnd(GetAudioGridView(kind), GetDraggingAudioItem(kind));
        }

        private async void BackgroundImagesDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_draggingBackgroundImageItem is not null)
            {
                MoveDraggingBackgroundImageToEnd();
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedImagePaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(ImageSetService.IsValidSourcePath)
                    .ToList();

                var importedCount = await ImportBackgroundImagesAsync(droppedImagePaths);
                if (importedCount > 0)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Success,
                        "背景图已导入",
                        $"已拖入导入 {importedCount} 个文件。",
                        logMessage: $"拖入导入背景图：{importedCount} 个文件。");
                }
                else
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "没有导入背景图",
                        "拖入内容中没有可导入的图片文件。",
                        logMessage: "拖入内容中没有可导入的背景图文件。");
                }
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "背景图导入失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "拖入导入背景图失败。");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void MoveDraggingBackgroundImageToEnd()
        {
            MoveGridViewItemToEnd(BackgroundImagesGridView, _draggingBackgroundImageItem);
        }

        private bool IsPointerInTrailingBlankArea(Windows.Foundation.Point pointerPosition, GridViewItem draggingItem)
        {
            return IsPointerInTrailingBlankArea(BackgroundImagesGridView, pointerPosition, draggingItem);
        }

        private static bool IsPointerInTrailingBlankArea(
            GridView gridView,
            Windows.Foundation.Point pointerPosition,
            GridViewItem draggingItem)
        {
            var visibleItems = gridView.Items
                .OfType<GridViewItem>()
                .Where(item => !ReferenceEquals(item, draggingItem))
                .ToList();

            if (visibleItems.Count == 0)
            {
                return false;
            }

            var lastItem = visibleItems.Last();
            var lastItemOrigin = lastItem.TransformToVisual(gridView)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            var lastItemRight = lastItemOrigin.X + lastItem.ActualWidth;
            var sameRowAsLastItem =
                pointerPosition.Y >= lastItemOrigin.Y &&
                pointerPosition.Y <= lastItemOrigin.Y + lastItem.ActualHeight;

            return sameRowAsLastItem && pointerPosition.X > lastItemRight;
        }

    }
}
