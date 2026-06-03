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
        private void StorySidePanel_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
        }

        private void StorySidePanel_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
        }

        private void ToggleStorySettingsPaneButton_Click(object sender, RoutedEventArgs e)
        {
            var shouldOpen = StorySettingsPane.Width <= 46;
            _storyPaneAnimationTargetWidth = shouldOpen ? 380 : 46;
            _storyPaneAnimationTimer.Stop();
            _storyPaneAnimationTimer.Start();
        }

        private void StoryPaneAnimationTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            var currentWidth = StorySettingsPane.Width;
            var delta = _storyPaneAnimationTargetWidth - currentWidth;
            if (Math.Abs(delta) < 1)
            {
                StorySettingsPane.Width = _storyPaneAnimationTargetWidth;
                StorySidePanelContent.Opacity = StorySettingsPane.Width > 46 ? 1 : 0;
                sender.Stop();
                return;
            }

            StorySettingsPane.Width = currentWidth + delta * 0.28;
            var openProgress = Math.Clamp((StorySettingsPane.Width - 46) / (380 - 46), 0, 1);
            StorySidePanelContent.Opacity = openProgress;
        }

        private void StorySlotVisibilityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStoryCharacterSlotLayout();
            _storyCharacterPreviewKeys.Clear();
            _ = RefreshStoryPreviewAsync();
        }

        private void StoryCharacterDisplayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _storyCharacterPreviewKeys.Clear();
            _ = RefreshStoryPreviewAsync();
        }

        private void StoryCharacterSlot_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border slot)
            {
                if (slot.Tag is int slotIndex)
                {
                    _hoveredStoryCharacterSlotIndex = slotIndex;
                    ToolTipService.SetToolTip(slot, "《F12打开快捷键提示》");
                    StoryEditorPage.Focus(FocusState.Programmatic);
                }

                slot.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                slot.BorderThickness = new Thickness(1);
                slot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 255, 255, 255));
            }
        }

        private void StoryCharacterSlot_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border slot)
            {
                if (slot.Tag is int slotIndex && _hoveredStoryCharacterSlotIndex == slotIndex)
                {
                    _hoveredStoryCharacterSlotIndex = null;
                }

                slot.BorderBrush = null;
                slot.BorderThickness = new Thickness(0);
                slot.Background = ReferenceEquals(slot, StorySpeakerPreviewSlot)
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(34, 255, 255, 255))
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        private void StoryAssetShortcut_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string fieldName)
            {
                _hoveredStoryAssetFieldName = fieldName;
                ToolTipService.SetToolTip(element, "《F12打开快捷键提示》");
                StoryEditorPage.Focus(FocusState.Programmatic);
            }
        }

        private void StoryAssetShortcut_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string fieldName } &&
                string.Equals(_hoveredStoryAssetFieldName, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                _hoveredStoryAssetFieldName = null;
            }
        }

        private async void StoryEditorPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.F12)
            {
                await ShowStoryShortcutHelpDialogAsync();
                e.Handled = true;
                return;
            }

            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (IsControlKeyDown())
            {
                if (e.Key == Windows.System.VirtualKey.Z)
                {
                    UndoStoryEditorOperation();
                    e.Handled = true;
                    return;
                }

                if (_hoveredStoryCharacterSlotIndex is int hoveredSlotIndex)
                {
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.C:
                            CopyStoryCharacterSlot(hoveredSlotIndex);
                            e.Handled = true;
                            return;
                        case Windows.System.VirtualKey.V:
                            await PasteStoryCharacterSlotAsync(hoveredSlotIndex);
                            e.Handled = true;
                            return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(_hoveredStoryAssetFieldName))
                {
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.C:
                            CopyStoryAssetField(_hoveredStoryAssetFieldName);
                            e.Handled = true;
                            return;
                        case Windows.System.VirtualKey.V:
                            await PasteStoryAssetFieldAsync(_hoveredStoryAssetFieldName);
                            e.Handled = true;
                            return;
                    }
                }
            }

            if (_hoveredStoryCharacterSlotIndex is not int slotIndex)
            {
                return;
            }

            var handled = true;
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Q:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Adorn, -1);
                    break;
                case Windows.System.VirtualKey.E:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Adorn, 1);
                    break;
                case Windows.System.VirtualKey.A:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Face, -1);
                    break;
                case Windows.System.VirtualKey.D:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Face, 1);
                    break;
                case Windows.System.VirtualKey.Z:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Cloth, -1);
                    break;
                case Windows.System.VirtualKey.C:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Cloth, 1);
                    break;
                case Windows.System.VirtualKey.NumberPad4:
                case Windows.System.VirtualKey.Left:
                    await CycleStoryCharacterAsync(slotIndex, -1);
                    break;
                case Windows.System.VirtualKey.NumberPad6:
                case Windows.System.VirtualKey.Right:
                    await CycleStoryCharacterAsync(slotIndex, 1);
                    break;
                case Windows.System.VirtualKey.NumberPad8:
                case Windows.System.VirtualKey.Up:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Vfx, 1);
                    break;
                case Windows.System.VirtualKey.NumberPad2:
                case Windows.System.VirtualKey.Down:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Vfx, -1);
                    break;
                case Windows.System.VirtualKey.Tab:
                    ClearStoryCharacterSlot(slotIndex);
                    break;
                default:
                    handled = false;
                    break;
            }

            e.Handled = handled;
        }

        private static bool IsControlKeyDown()
        {
            return InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        }

        private void CopyStoryCharacterSlot(int slotIndex)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyCharacterSlotClipboard = StoryCharacterSlotService.CreateClipboard(row, slotIndex);
            ShowStoryStatus(InfoBarSeverity.Success, "已复制立绘数据", $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}：{StoryCharacterSlotService.FormatClipboard(_storyCharacterSlotClipboard)}");
        }

        private async Task PasteStoryCharacterSlotAsync(int slotIndex)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (_storyCharacterSlotClipboard is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可粘贴数据", "先悬停一个立绘位并按 Ctrl+C 复制。");
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (StoryCharacterSlotService.MatchesClipboard(row, slotIndex, _storyCharacterSlotClipboard))
            {
                return;
            }

            CaptureStoryUndoState($"粘贴{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}立绘数据");
            StoryCharacterSlotService.ApplyClipboard(row, slotIndex, _storyCharacterSlotClipboard);
            NormalizeStoryDetachedCharacterLayers(row);
            SyncStorySpeakerTextIfNeeded(slotIndex, row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex)));
            PersistCurrentStoryRowsToFiles();
            ShowStoryStatus(InfoBarSeverity.Success, "已粘贴立绘数据", $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}：{StoryCharacterSlotService.FormatClipboard(_storyCharacterSlotClipboard)}");
            await RefreshStoryPreviewAsync();
        }

        private void CopyStoryAssetField(string fieldName)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyAssetClipboard = StoryAssetFieldService.CreateClipboard(row, fieldName);
            ShowStoryStatus(InfoBarSeverity.Success, "已复制基础素材", $"{StoryAssetFieldService.GetDisplayName(fieldName)}：{_storyAssetClipboard.Value}");
        }

        private async Task PasteStoryAssetFieldAsync(string fieldName)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (_storyAssetClipboard is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可粘贴数据", "先悬停基础素材并按 Ctrl+C 复制。");
                return;
            }

            if (!StoryAssetFieldService.IsSameField(_storyAssetClipboard, fieldName))
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "素材类型不一致", $"复制的是{StoryAssetFieldService.GetDisplayName(_storyAssetClipboard.FieldName)}，当前悬停的是{StoryAssetFieldService.GetDisplayName(fieldName)}。");
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (StoryAssetFieldService.MatchesValue(row, fieldName, _storyAssetClipboard))
            {
                return;
            }

            CaptureStoryUndoState($"粘贴{StoryAssetFieldService.GetDisplayName(fieldName)}");
            StoryAssetFieldService.ApplyClipboard(row, fieldName, _storyAssetClipboard);
            PersistCurrentStoryRowsToFiles();
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(InfoBarSeverity.Success, "已粘贴基础素材", $"{StoryAssetFieldService.GetDisplayName(fieldName)}：{_storyAssetClipboard.Value}");
            if (fieldName == "BGM")
            {
                await ApplyCurrentStoryRowMediaAndFunctionAsync();
            }
            else if (fieldName == "Scene")
            {
                await PlayCurrentStorySceneFromUiAsync();
            }

            await RefreshStoryPreviewAsync();
        }

        private async Task ShowStoryShortcutHelpDialogAsync()
        {
            await _shortcutService.ShowShortcutHelpAsync();
        }

        private async Task CycleStoryCharacterLayerAsync(int slotIndex, CharacterLayerKind layerKind, int delta)
        {
            var layerSpec = GetStoryCharacterLayerSpec(layerKind);
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var character = ResolveStoryCharacter(row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex)));
            if (character is null)
            {
                return;
            }

            if (layerSpec.Kind == CharacterLayerKind.Vfx)
            {
                var filters = GetStoryCharacterFilters();
                if (filters.Count == 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", "当前素材库还没有角色滤镜。");
                    return;
                }

                var filterColumn = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
                var filterCurrentIndex = ParseInt(row.Get(filterColumn));
                var nextIndex = ((filterCurrentIndex + delta) % filters.Count + filters.Count) % filters.Count;
                if (nextIndex == filterCurrentIndex)
                {
                    return;
                }

                CaptureStoryUndoState($"快捷切换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}{layerSpec.DisplayName}");
                row.Set(filterColumn, nextIndex.ToString());
                PersistCurrentStoryRowsToFiles();
                ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, nextIndex, CharacterFilterService.GetDisplayName(filters[nextIndex], nextIndex));
                await RefreshStoryPreviewAsync();
                return;
            }

            var paths = CharacterLayerAssetService.GetLayerPaths(character, layerSpec.Kind);
            var layerColumn = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
            var layerCurrentIndex = ParseInt(row.Get(layerColumn));
            var validIndexes = GetStoryCompatibleLayerIndexes(character, layerSpec, paths, row, slotIndex);
            if (validIndexes.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", $"角色 {character.Name} 还没有可用的{layerSpec.DisplayName}素材。");
                return;
            }

            var currentPosition = validIndexes.IndexOf(layerCurrentIndex);
            var layerNextIndex = currentPosition < 0
                ? validIndexes[0]
                : validIndexes[(currentPosition + delta + validIndexes.Count) % validIndexes.Count];
            if (layerNextIndex == layerCurrentIndex)
            {
                return;
            }

            CaptureStoryUndoState($"快捷切换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}{layerSpec.DisplayName}");
            row.Set(layerColumn, layerNextIndex.ToString());
            if (layerSpec.Kind == CharacterLayerKind.Cloth)
            {
                NormalizeStoryRowLayerCompatibility(row, character, slotIndex);
            }

            PersistCurrentStoryRowsToFiles();
            ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, layerNextIndex, StoryCharacterLayerChoiceFactory.GetDisplayName(layerSpec, paths, layerNextIndex));
            await RefreshStoryPreviewAsync();
        }

        private async Task CycleStoryCharacterAsync(int slotIndex, int delta)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var characters = GetStoryCharactersByFolderOrder();
            if (characters.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var characterColumn = StoryCharacterSlotService.GetCharacterColumn(slotIndex);
            var currentCharacter = ResolveStoryCharacter(row.Get(characterColumn));
            var currentIndex = currentCharacter is null
                ? -1
                : characters.FindIndex(character => string.Equals(character.Code, currentCharacter.Code, StringComparison.OrdinalIgnoreCase));
            var nextIndex = currentIndex < 0
                ? (delta > 0 ? 0 : characters.Count - 1)
                : ((currentIndex + delta) % characters.Count + characters.Count) % characters.Count;

            if (string.Equals(row.Get(characterColumn), characters[nextIndex].Code, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CaptureStoryUndoState($"快捷切换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}角色");
            row.Set(characterColumn, characters[nextIndex].Code);
            StoryCharacterSlotService.ResetLayerColumns(row, slotIndex);
            SyncStorySpeakerTextIfNeeded(slotIndex, characters[nextIndex].Code);
            PersistCurrentStoryRowsToFiles();
            await RefreshStoryPreviewAsync();
        }

        private void ClearStoryCharacterSlot(int slotIndex)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (StoryCharacterSlotService.IsEmpty(row, slotIndex))
            {
                return;
            }

            CaptureStoryUndoState($"清空{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}立绘位");
            row.Set(StoryCharacterSlotService.GetCharacterColumn(slotIndex), string.Empty);
            StoryCharacterSlotService.ResetLayerColumns(row, slotIndex);
            SyncStorySpeakerTextIfNeeded(slotIndex, string.Empty);
            PersistCurrentStoryRowsToFiles();
            _ = RefreshStoryPreviewAsync();
        }

        private void SyncStorySpeakerTextIfNeeded(int slotIndex, string value)
        {
            if (slotIndex != 0)
            {
                return;
            }

            _isLoadingStoryRow = true;
            try
            {
                _storyEditorViewModel.SpeakerText = value;
            }
            finally
            {
                _isLoadingStoryRow = false;
            }
        }

        private bool NormalizeStoryDetachedCharacterLayers(StoryRow row)
        {
            var changed = false;
            for (var slotIndex = 0; slotIndex <= 5; slotIndex++)
            {
                var characterValue = row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex));
                if (!ShouldClearDetachedStoryCharacterLayers(characterValue))
                {
                    continue;
                }

                changed |= StoryCharacterSlotService.ResetLayerColumnsIfNeeded(row, slotIndex);
            }

            return changed;
        }

        private bool ShouldClearDetachedStoryCharacterLayers(string characterValue)
        {
            var trimmed = characterValue.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || StoryCharacterSlotService.ContainsCjk(trimmed))
            {
                return true;
            }

            return _currentStoryAssetLibrary is not null && ResolveStoryCharacter(trimmed) is null;
        }

        private void ShowStoryLayerChangedStatus(int slotIndex, string title, int index, string displayName)
        {
            ShowStoryStatus(
                InfoBarSeverity.Success,
                $"已更换{title}",
                $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}：{displayName}（索引 {index}）");
        }

        private void UpdateStoryCharacterSlotLayout()
        {
            if (StoryCharacterSlot1 is null ||
                StoryCharacterSlot2 is null ||
                StoryCharacterSlot3 is null ||
                StoryCharacterSlot4 is null ||
                StoryCharacterSlot5 is null)
            {
                return;
            }

            var visibleSlots = new List<Border>
            {
                StoryCharacterSlot1,
                StoryCharacterSlot2,
                StoryCharacterSlot3
            };

            var showSlot4 = ShowCharacterSlot4CheckBox?.IsChecked == true;
            var showSlot5 = ShowCharacterSlot5CheckBox?.IsChecked == true;
            StoryCharacterSlot4.Visibility = showSlot4 ? Visibility.Visible : Visibility.Collapsed;
            StoryCharacterSlot5.Visibility = showSlot5 ? Visibility.Visible : Visibility.Collapsed;

            if (showSlot4)
            {
                visibleSlots.Add(StoryCharacterSlot4);
            }

            if (showSlot5)
            {
                visibleSlots.Add(StoryCharacterSlot5);
            }

            var count = visibleSlots.Count;
            var columns = count switch
            {
                3 => new[] { 0, 3, 6 },
                4 => new[] { 0, 2, 5, 7 },
                _ => Enumerable.Range(0, count).Select(i => i * 2).ToArray()
            };

            var spans = count switch
            {
                3 => new[] { 4, 4, 4 },
                4 => new[] { 3, 3, 3, 3 },
                _ => Enumerable.Repeat(2, count).ToArray()
            };

            for (var i = 0; i < visibleSlots.Count; i++)
            {
                Grid.SetColumn(visibleSlots[i], Math.Min(9, columns[i]));
                Grid.SetColumnSpan(visibleSlots[i], spans[i]);
                visibleSlots[i].Margin = count <= 3
                    ? new Thickness(4, -24, 4, -44)
                    : count == 4
                        ? new Thickness(4, -10, 4, -24)
                        : new Thickness(6, 16 + (i % 2 == 0 ? 0 : 22), 6, 146);
                visibleSlots[i].VerticalAlignment = VerticalAlignment.Stretch;
                visibleSlots[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }

    }
}
