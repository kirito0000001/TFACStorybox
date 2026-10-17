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

        /// <summary>
        /// 音频重排后把剧情里对应的位置索引一起改掉 —— **每种素材各有落点**：
        /// 音乐 → BGM 列、环境音 → Scene 列、特殊音效 → 函数串里的 Scene_n 标记。
        ///
        /// 【为什么非做不可】这三种索引都是**位置**（第 n 个文件），拖拽一改顺序，
        /// 同一个号就指向另一个素材了。2026-10-16 之前只有「环境音」那条同步过 ——
        /// 也就是说：换完音乐顺序，剧情里的 BGM 会**静默**全部错位。
        /// </summary>
        private async Task SyncStoryAudioIndexesAsync(
            AssetLibraryInfo assetLibrary,
            AudioAssetKind kind,
            IReadOnlyDictionary<int, int> indexRemap,
            (Dictionary<int, string> OldLabels, Dictionary<int, string> NewLabels) assetLabels,
            int assetCount)
        {
            if (indexRemap.Count == 0)
            {
                return;
            }

            AssetIndexSyncResult syncResult;
            string assetLabel;
            if (kind == AudioAssetKind.SoundEffect)
            {
                assetLabel = "特殊音效";
                syncResult = await SyncStorySoundEffectIndexesWithProgressAsync(
                    assetLibrary,
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    assetCount);
            }
            else
            {
                var isMusic = kind == AudioAssetKind.Music;
                assetLabel = isMusic ? "BGM" : "环境音";
                syncResult = await SyncStoryGlobalAssetIndexesWithProgressAsync(
                    assetLibrary,
                    assetLabel,
                    isMusic ? "BGM" : "Scene",
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    assetCount);
            }

            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的{assetLabel}索引。");
            }
        }

        private async Task<AssetIndexSyncResult> SyncStorySoundEffectIndexesWithProgressAsync(
            AssetLibraryInfo assetLibrary,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var result = await ShowAssetIndexSyncProgressDialogAsync(
                "特殊音效索引同步",
                progress => Task.Run(() => _storyAssetIndexSyncService.SyncSoundEffectIndexes(assetLibrary, indexRemap, oldLabels, newLabels, assetCount, progress)));
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

            LoadStoryDocument(_currentStoryChapter);
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

        /// <summary>
        /// 按**位置**算"旧下标 → 新下标"。剧情里的 BGindex / BGM / Scene 存的全是
        /// 位置（排序后文件列表里的下标），不是文件名里的编号 —— 映射必须在同一个坐标系里算。
        ///
        /// 【为什么不能按文件名里的编号算】目录规范（BG00…BGnn 连续）时两者相等；
        /// 一旦有文件名里没有编号、编号重复或有洞，按编号算出来的就是**错的键**
        ///（下面 BuildAssetIndexRemap 保留着旧口径，新代码一律用这一个）。
        /// </summary>
        private static Dictionary<int, int> BuildPositionalAssetIndexRemap(
            IReadOnlyList<string> beforeOrderedPaths,
            IReadOnlyList<string> afterOrderedPaths)
        {
            var beforePositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < beforeOrderedPaths.Count; index++)
            {
                beforePositions.TryAdd(beforeOrderedPaths[index], index);
            }

            var remap = new Dictionary<int, int>();
            for (var newIndex = 0; newIndex < afterOrderedPaths.Count; newIndex++)
            {
                if (beforePositions.TryGetValue(afterOrderedPaths[newIndex], out var oldIndex) && oldIndex != newIndex)
                {
                    remap[oldIndex] = newIndex;
                }
            }

            return remap;
        }

        /// <summary>按位置做的标签表：(旧位置 → 文件名)、(新位置 → 文件名)。同步结果框里显示"从哪张改到哪张"用。</summary>
        private static (Dictionary<int, string> OldLabels, Dictionary<int, string> NewLabels) BuildPositionalLabelMaps(
            IReadOnlyList<string> beforeOrderedPaths,
            IReadOnlyList<string> afterOrderedPaths)
        {
            var oldLabels = new Dictionary<int, string>();
            for (var index = 0; index < beforeOrderedPaths.Count; index++)
            {
                oldLabels.TryAdd(index, Path.GetFileNameWithoutExtension(beforeOrderedPaths[index]));
            }

            var newLabels = new Dictionary<int, string>();
            for (var index = 0; index < afterOrderedPaths.Count; index++)
            {
                newLabels.TryAdd(index, Path.GetFileNameWithoutExtension(afterOrderedPaths[index]));
            }

            return (oldLabels, newLabels);
        }

        /// <summary>
        /// 把这一次"重命名 / 重排"的完整映射写进日志。
        ///
        /// 剧情索引是位置，改错了**当场看不出来**（图照样显示，只是显示成了另一张）——
        /// 用户 2026-10-16 就是"已经没办法复原了"。所以每次重排都留一份
        /// 「文件名：旧位置 → 新位置」，万一出事能照着它手工改回来。
        /// </summary>
        private void LogAssetIndexRemapEvidence(
            string assetLabel,
            IReadOnlyList<string> beforeOrderedPaths,
            IReadOnlyList<string> afterOrderedPaths)
        {
            var beforePositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < beforeOrderedPaths.Count; index++)
            {
                beforePositions.TryAdd(beforeOrderedPaths[index], index);
            }

            var moved = new List<string>();
            for (var newIndex = 0; newIndex < afterOrderedPaths.Count; newIndex++)
            {
                var path = afterOrderedPaths[newIndex];
                if (beforePositions.TryGetValue(path, out var oldIndex) && oldIndex != newIndex)
                {
                    moved.Add($"{Path.GetFileName(path)}：{oldIndex} → {newIndex}");
                }
            }

            if (moved.Count == 0)
            {
                return;
            }

            AppendLog(
                LogKind.Info,
                $"{assetLabel}重排映射（共 {moved.Count} 项，旧位置 → 新位置）：{string.Join("；", moved)}");
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

        private void ShowStoryStatus(
            InfoBarSeverity severity,
            string title,
            string message,
            Exception? exception = null,
            string? logMessage = null)
        {
            ShowFloatingTip(StoryFloatingTipsPanel, severity, title, message, exception, logMessage);
        }

        private InfoBar? _persistentStoryStatusTip;

        private void ShowPersistentStoryStatus(
            InfoBarSeverity severity,
            string title,
            string message)
        {
            if (_persistentStoryStatusTip is not null)
            {
                RemoveFloatingTip(StoryFloatingTipsPanel, _persistentStoryStatusTip);
            }

            var normalizedSeverity = NormalizeFloatingTipSeverity(severity);
            AppendLog(GetFloatingTipLogKind(normalizedSeverity), $"{title}：{message}");
            var tip = CreateFloatingTipBar(normalizedSeverity, title, message);
            tip.Tag = PersistentStoryStatusTag;
            tip.Closed += (_, _) =>
            {
                if (ReferenceEquals(_persistentStoryStatusTip, tip))
                {
                    _persistentStoryStatusTip = null;
                }
            };
            _persistentStoryStatusTip = tip;
            TrimFloatingTipsBeforeAdd(StoryFloatingTipsPanel);
            AddFloatingTipWithEntrance(StoryFloatingTipsPanel, tip);
        }

    }
}
