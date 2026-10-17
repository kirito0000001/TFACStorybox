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
        // BackgroundImagesGridView_DragItemsCompleted 已删：旧背景图网格的拖拽落位。
        // 素材页背景图排序由「移动素材」接管；落地方 ApplyBackgroundImageOrderAsync 保留。

        /// <summary>
        /// 把"用户拖出来的这个新顺序"落地：算位置映射 → 记证据日志 → 重命名 → 同步所有章节的 BGindex。
        /// 两条入口共用：框架的 DragItemsCompleted（旧网格）与自写重排的 Drop（新骨架卡片）。
        /// </summary>
        private async Task ApplyBackgroundImageOrderAsync(IReadOnlyList<string> orderedPaths)
        {
            if (_currentAssetLibrary is null || orderedPaths.Count == 0)
            {
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
            // 只重铺新骨架那一份：旧网格（BackgroundImagesGridView）正在按文档第二节退休。
            if (AssetBrowserHost is not null)
            {
                await FillAssetBrowserWithBackgroundsAsync(AssetBrowserHost, _currentAssetLibrary);
            }

            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的背景图索引。");
            }
            AppendLog(LogKind.User, "已调整背景图顺序并触发自动命名。");
        }

        // BackgroundImagesGridView_DragItemsStarting 与 _DragOver 已删：旧背景图网格的拖拽接线。
        // MoveDraggingBackgroundImageToEnd 暂时还有人用（旧拖放区），留到第 5 步一起清。

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

        // MusicGridView_DragItemsCompleted 已删：旧音乐网格的拖拽落位。

        // MusicGridView_DragItemsStarting 已删：旧音乐网格的拖拽起手。

        // MusicGridView_DragOver 已删：旧音乐网格的拖拽悬停。

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

        // AmbientSoundGridView_DragItemsCompleted 已删：旧环境音网格的拖拽落位。

        // AmbientSoundGridView_DragItemsStarting 已删：旧环境音网格的拖拽起手。

        // AmbientSoundGridView_DragOver 已删：旧环境音网格的拖拽悬停。

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

        // SoundEffectGridView_DragItemsCompleted 已删：旧特殊音效网格的拖拽落位。

        // SoundEffectGridView_DragItemsStarting 已删：旧特殊音效网格的拖拽起手。

        // SoundEffectGridView_DragOver 已删：旧特殊音效网格的拖拽悬停。

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

        // CharacterFilterGridView_DragItemsCompleted 已删：旧滤镜网格的拖拽落位。
        // 它体内那套「读清单 → Normalize → BuildIndexRemap → 写回（带守卫）→
        // SyncStoryCharacterFilterIndexesWithProgressAsync → 重铺」逻辑，
        // 新骨架的「移动素材」走 ApplyCharacterFilterOrderAsync（已完整复刻）。

        // CharacterFilterGridView_DragItemsStarting 已删：旧滤镜网格的拖拽起手。
        // 素材页的滤镜排序由「移动素材」接管（ApplyCharacterFilterOrderAsync）。

        // CharacterFilterGridView_DragOver 已删：旧滤镜网格的拖拽悬停。

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

        private static void CharacterLayerDropZone_DragEnterOrOver(FrameworkElement? draggingItem, DragEventArgs e)
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
            Action<FrameworkElement?> setDraggingItem)
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
            FrameworkElement? draggingItem,
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

        private static void CharacterLayerGridView_DragOver(GridView gridView, FrameworkElement? draggingItem, DragEventArgs e)
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

        // AudioGridView_DragItemsCompleted 已删：旧音频网格的拖拽落位。
        // 它体内那套「算位置映射 → 重命名 → SyncStoryAudioIndexesAsync」逻辑，
        // 新骨架的「移动素材」走的是 ApplyAudioOrderAsync（同一个形状，已复刻）。
        // ⚠️ MoveDraggingAudioToEnd 与 AudioDropZone_* 一族仍在（拖放区用），留到第 5 步。

        // AudioGridView_DragItemsStarting 与 AudioGridView_DragOver 已删：
        // 旧音频网格的拖拽起手与悬停（三个包装处理器删完后它们已无调用者）。
        // ⚠️ MoveDraggingAudioToEnd **不能**一起删 —— 它的调用者在 AudioDropZone_Drop
        // （拖放区）里，而拖放区要到第 5 步随整段 Expander 才退休。

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

        /// <summary>
        /// 当前"活着"的那个背景图网格。拖放区（外层 StackPanel）**不是网格**，
        /// 它要挪卡片时得知道往哪个网格里挪：新骨架的浏览器优先 ——
        /// 旧的 Expander 是折叠的退路，两者互斥，同一时刻只有一个在跟用户交互。
        /// </summary>
        private GridView ResolveActiveBackgroundImageGrid()
        {
            return AssetBrowserHost?.CardsHost ?? BackgroundImagesGridView;
        }

        private async void BackgroundImagesDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_draggingBackgroundImageItem is not null)
            {
                MoveDraggingBackgroundImageToEnd(ResolveActiveBackgroundImageGrid());
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

        private void MoveDraggingBackgroundImageToEnd(GridView grid)
        {
            MoveGridViewItemToEnd(grid, _draggingBackgroundImageItem);
        }

        // 原来这里有个单参包装方法（内部写死 BackgroundImagesGridView）——
        // 现在静态重载已经收网格参数，包装方法同签名会**自己调自己**（无限递归），所以删掉，
        // 调用方直接用 IsPointerInTrailingBlankArea(grid, point, item)。

        private static bool IsPointerInTrailingBlankArea(
            GridView gridView,
            Windows.Foundation.Point pointerPosition,
            FrameworkElement draggingItem)
        {
            var visibleItems = gridView.Items
                .OfType<FrameworkElement>()
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
