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
        private async Task<AssetIndexSyncResult> SyncStoryGlobalAssetIndexesWithProgressAsync(
            AssetLibraryInfo assetLibrary,
            string assetLabel,
            string columnName,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var result = await ShowAssetIndexSyncProgressDialogAsync(
                $"{assetLabel}索引同步",
                progress => Task.Run(() => _storyAssetIndexSyncService.SyncGlobalAssetIndexes(assetLibrary, assetLabel, columnName, indexRemap, oldLabels, newLabels, assetCount, progress)));
            RefreshOpenStoryRowsAfterIndexSync(result.ChangedCsvPaths);
            await ShowAssetIndexSyncResultDialogAsync(result);
            return result;
        }

        private async Task<AssetIndexSyncResult> SyncStoryCharacterLayerIndexesWithProgressAsync(
            AssetLibraryInfo assetLibrary,
            CharacterInfo character,
            CharacterLayerKind layerKind,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var assetLabel = $"{character.Name} {GetCharacterLayerDisplayName(layerKind)}";
            var result = await ShowAssetIndexSyncProgressDialogAsync(
                $"{assetLabel}索引同步",
                progress => Task.Run(() => _storyAssetIndexSyncService.SyncCharacterLayerIndexes(assetLibrary, character, layerKind, GetCharacterLayerDisplayName(layerKind), indexRemap, oldLabels, newLabels, assetCount, progress)));
            RefreshOpenStoryRowsAfterIndexSync(result.ChangedCsvPaths);
            await ShowAssetIndexSyncResultDialogAsync(result);
            return result;
        }

        private async Task<AssetIndexSyncResult> SyncStoryCharacterFilterIndexesWithProgressAsync(
            AssetLibraryInfo assetLibrary,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var result = await ShowAssetIndexSyncProgressDialogAsync(
                "角色滤镜索引同步",
                progress => Task.Run(() => _storyAssetIndexSyncService.SyncCharacterFilterIndexes(assetLibrary, indexRemap, oldLabels, newLabels, assetCount, progress)));
            RefreshOpenStoryRowsAfterIndexSync(result.ChangedCsvPaths);
            await ShowAssetIndexSyncResultDialogAsync(result);
            return result;
        }

        private void RefreshOpenStoryRowsAfterIndexSync(IReadOnlyList<string> changedCsvPaths)
        {
            if (_currentStoryChapter is null || changedCsvPaths.Count == 0)
            {
                return;
            }

            if (!changedCsvPaths.Any(path => IsPathInsideDirectory(path, _currentStoryChapter.Path)))
            {
                return;
            }

            LoadStoryRowsFromSectionFiles(_currentStoryChapter);
            SynchronizeStorySectionState();
            ClearStoryUndoStack();
            _currentStoryRowIndex = Math.Clamp(_currentStoryRowIndex, 0, Math.Max(0, _storyRows.Count - 1));
            _storyEditorViewModel.RefreshCommandStates();
            if (StoryEditorPage.Visibility == Visibility.Visible)
            {
                RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
                LoadStoryRowIntoUi();
            }
        }

        private static bool StoryCharacterMatches(string value, CharacterInfo character)
        {
            return string.Equals(value, character.Code, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, character.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStoryLayerFieldPrefix(CharacterLayerKind layerKind)
        {
            return GetStoryCharacterLayerSpec(layerKind).FieldPrefix;
        }

        private static StoryCharacterLayerSpec GetStoryCharacterLayerSpec(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => new StoryCharacterLayerSpec(layerKind, "Body", "服装"),
                CharacterLayerKind.Face => new StoryCharacterLayerSpec(layerKind, "Face", "表情"),
                CharacterLayerKind.Adorn => new StoryCharacterLayerSpec(layerKind, "Adorn", "装饰"),
                CharacterLayerKind.Vfx => new StoryCharacterLayerSpec(layerKind, "Vfx", "滤镜"),
                _ => new StoryCharacterLayerSpec(CharacterLayerKind.Cloth, "Body", "服装")
            };
        }

        private static StoryCharacterLayerSpec GetStoryCharacterLayerSpec(string fieldPrefix)
        {
            return fieldPrefix switch
            {
                "Body" => GetStoryCharacterLayerSpec(CharacterLayerKind.Cloth),
                "Face" => GetStoryCharacterLayerSpec(CharacterLayerKind.Face),
                "Adorn" => GetStoryCharacterLayerSpec(CharacterLayerKind.Adorn),
                "Vfx" => GetStoryCharacterLayerSpec(CharacterLayerKind.Vfx),
                _ => GetStoryCharacterLayerSpec(CharacterLayerKind.Cloth)
            };
        }

        private static Dictionary<int, int> BuildAssetIndexRemap(
            IReadOnlyList<string> orderedPaths,
            Func<string, int?> getOldIndex)
        {
            var result = new Dictionary<int, int>();
            foreach (var item in orderedPaths.Select((path, newIndex) => new { OldIndex = getOldIndex(path), NewIndex = newIndex }))
            {
                if (item.OldIndex is null || item.OldIndex.Value == item.NewIndex)
                {
                    continue;
                }

                result.TryAdd(item.OldIndex.Value, item.NewIndex);
            }

            return result;
        }

        private static List<string> GetOrderedExistingTaggedPaths(GridView gridView)
        {
            return gridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static GridViewItem? ResolveDraggedGridViewItem(GridView gridView, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            return draggedObject as GridViewItem ??
                   (draggedObject is null ? null : gridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                   gridView.Items
                       .OfType<GridViewItem>()
                       .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));
        }

        private static void MoveGridViewItemToEnd(GridView gridView, GridViewItem? item)
        {
            if (item is null)
            {
                return;
            }

            var currentIndex = gridView.Items.IndexOf(item);
            var lastIndex = gridView.Items.Count - 1;
            if (currentIndex < 0 || currentIndex == lastIndex)
            {
                return;
            }

            gridView.Items.Remove(item);
            gridView.Items.Add(item);
        }

        private void ShowStoryStatus(InfoBarSeverity severity, string title, string message)
        {
            var tip = CreateStoryTipBar(severity, title, message);
            AddStoryTipWithEntrance(StoryFloatingTipsPanel, tip);

            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(severity == InfoBarSeverity.Error ? 5 : 2.6);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _storyTransientTipTimers.Remove(tip);
                StoryFloatingTipsPanel.Children.Remove(tip);
            };
            _storyTransientTipTimers[tip] = timer;
            timer.Start();
        }

    }
}
