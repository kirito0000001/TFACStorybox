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
        private void StorySidePanel_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
        }

        private void StorySidePanel_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
        }

        private void StoryInspectorTabButton_Click(object sender, RoutedEventArgs e)
        {
            var showState = ReferenceEquals(sender, StoryInspectorStateTabButton);
            StoryInspectorStateTabButton.IsChecked = showState;
            StoryInspectorToolsTabButton.IsChecked = !showState;
            StorySidePanelContent.Visibility = showState ? Visibility.Visible : Visibility.Collapsed;
            StoryInspectorToolsScrollViewer.Visibility = showState ? Visibility.Collapsed : Visibility.Visible;
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

        /// <summary>
        /// 鼠标移到某个立绘位上（含说话人那个，它的 Tag 是 0）。
        ///
        /// ⚠️ 挂的是 <c>PointerMoved</c>，**不是** <c>Pointer&#69;ntered</c>。
        /// Pointer&#69;ntered 在"控件因为重新布局跑到光标下面"时**也会触发** ——
        /// 快速点上一句/下一句时界面整体刷新、鼠标其实没动，它却接连触发，
        /// 每次都去做 Focus(Programmatic) 抢焦点，焦点落到说话人输入框上，
        /// 那个下拉就自己弹出来了。这就是「切快了就自动聚焦」那个 bug。
        ///
        /// PointerMoved 只在鼠标**真的动了**才触发，切句时鼠标没动就不会触发；
        /// 而快捷键照样能用 —— 鼠标移到立绘上时这里才设 hover 并聚焦。
        ///
        /// 另外加了"只在目标变了才动"的守卫：PointerMoved 触发很频繁，
        /// 不加守卫的话鼠标在同一个位上滑动也会反复调 Focus()。
        /// </summary>
        private void StoryCharacterSlot_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border slot)
            {
                if (slot.Tag is int slotIndex && _hoveredStoryCharacterSlotIndex != slotIndex)
                {
                    _hoveredStoryCharacterSlotIndex = slotIndex;
                    StoryEditorPage.Focus(FocusState.Programmatic);
                }

                // 高亮是幂等的，但 PointerMoved 触发频繁 —— 每次都 new 一个 Brush 是白扔对象。
                if (slot.BorderBrush is null)
                {
                    slot.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                    slot.BorderThickness = new Thickness(1);
                    slot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 255, 255, 255));
                }
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

        private void StoryAssetShortcut_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string fieldName)
            {
                _hoveredStoryAssetFieldName = fieldName;
                ToolTipService.SetToolTip(element, "Ctrl+C / Ctrl+V：复制或粘贴当前素材");
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

        private void StoryEditorPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Tab = 清空鼠标停着的那个立绘位（快捷键说明里就是这么写的）。
            //
            // 【为什么必须放在**隧道**这一层】Tab 是**焦点导航键**：它在冒泡到
            // StoryEditorPage_KeyDown 之前就被焦点管理器吃掉了，所以挂在那边的那条
            // `case Tab` 永远执行不到 —— 表现是"鼠标停在立绘上按 Tab 没清空，
            // 反而跳到左边导航的第一个按钮"（用户 2026-10-16 报的）。
            // Preview 先于焦点导航到场，再 handled = true 把导航摁住。
            //
            // 只认**不带修饰键**的 Tab：Shift+Tab 仍然走焦点导航 —— 鼠标压在立绘上时
            // 留一个"还能把焦点挪走"的出口（Ctrl+Tab 同理不动）。
            if (e.Key == Windows.System.VirtualKey.Tab &&
                !IsControlKeyDown() &&
                !IsShiftKeyDown() &&
                _currentStoryDocumentPath is not null &&
                _storyRows.Count > 0 &&
                !_isStoryDebugModeEnabled &&
                _hoveredStoryCharacterSlotIndex is int hoveredSlotIndex)
            {
                ClearStoryCharacterSlot(hoveredSlotIndex);
                e.Handled = true;
                return;
            }

            if (_currentStoryDocumentPath is null ||
                _storyRows.Count == 0 ||
                _isStoryDebugModeEnabled ||
                !IsControlKeyDown())
            {
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Y ||
                (e.Key == Windows.System.VirtualKey.Z && IsShiftKeyDown()))
            {
                RedoStoryEditorOperation();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Z)
            {
                UndoStoryEditorOperation();
                e.Handled = true;
            }
        }

        private async void StoryEditorPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // F12 是老的快捷键提示入口，现在直接开 F1 那层参考浮层（内容同一份）——
            // 原来它弹的是一个内容写死在服务里的英文对话框，2026-10-16 并进参考层。
            if (e.Key == Windows.System.VirtualKey.F12)
            {
                ShowShortcutGuideOverlay();
                e.Handled = true;
                return;
            }

            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (IsControlKeyDown())
            {
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
                // Tab（清空这一位）不在这儿 —— 它是焦点导航键，冒泡到这里之前就被吃掉了，
                // 所以那条在 StoryEditorPage_PreviewKeyDown 里。别把它加回来。
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

        private static bool IsShiftKeyDown()
        {
            return InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        }

        private void CopyStoryCharacterSlot(int slotIndex)
        {
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyCharacterSlotClipboard = StoryCharacterSlotService.CreateClipboard(row, slotIndex);
            ShowStoryStatus(InfoBarSeverity.Success, "已复制立绘数据", $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}：{StoryCharacterSlotService.FormatClipboard(_storyCharacterSlotClipboard)}");
        }

        private async Task PasteStoryCharacterSlotAsync(int slotIndex)
        {
            if (!CanEditStoryData())
            {
                return;
            }

            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            // 粘贴一律弹窗让用户自己选（连续更换 / 只换一句）——判据在 ConfirmStoryValueReplacementAsync。
            _alwaysAskStoryReplacementConfirm = true;

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

            // 和「更换角色」走同一条路（MainWindow.Story.Assets.cs 那套三段式）：
            // 先问一句"要不要连续到后面"，再落地。
            // ⚠️ 顺序不能反：先改当前句再问的话，「取消」就退不回去了。
            var slotDisplayName = StoryCharacterSlotService.GetSlotDisplayName(slotIndex);
            var displayName = $"{slotDisplayName}角色";
            var characterField = StoryCharacterSlotService.GetCharacterColumn(slotIndex);
            var replacementPlan = StoryAssetFieldService.BuildForwardContiguousValuePlan(
                _storyRows,
                _currentStoryRowIndex,
                characterField,
                _storyCharacterSlotClipboard.Character);
            var replacementChoice = await ConfirmStoryValueReplacementAsync(
                replacementPlan,
                displayName,
                value => ResolveStoryCharacter(value)?.Name ?? value);
            if (replacementChoice is DialogResultKind.None or DialogResultKind.Cancel)
            {
                return;
            }

            if (replacementChoice == DialogResultKind.Secondary)
            {
                // 「只换一句」：把范围压成当前这一句，下面那段一行都不用改。
                replacementPlan = replacementPlan with
                {
                    EndIndex = replacementPlan.StartIndex,
                    Count = 1
                };
            }

            CaptureStoryUndoState(replacementPlan.Count > 1
                ? $"连续粘贴{displayName}立绘数据"
                : $"粘贴{slotDisplayName}立绘数据");
            var changedCount = StoryAssetFieldService.ApplyForwardContiguousValuePlan(_storyRows, replacementPlan);
            // 换了角色，后面那些句已有的图层分层就不一定配得上了 —— 和「连续更换角色」同一个口径：清掉。
            for (var index = replacementPlan.StartIndex; index <= replacementPlan.EndIndex; index++)
            {
                StoryCharacterSlotService.ResetLayerColumns(_storyRows[index], slotIndex);
            }

            // 当前这一句还要按剪贴板把**图层**一起写上（这正是"粘贴立绘数据"和"只换角色"的区别）；
            // 连续段里的其它句只跟角色，图层留给归一逻辑收拾。
            StoryCharacterSlotService.ApplyClipboard(row, slotIndex, _storyCharacterSlotClipboard);
            NormalizeStoryDetachedCharacterLayers(row);
            SyncStorySpeakerTextIfNeeded(slotIndex, row.Get(characterField));
            QueueCurrentStoryDocumentSave();
            ShowStoryStatus(
                InfoBarSeverity.Success,
                changedCount > 1 ? $"{displayName}已连续粘贴" : "已粘贴立绘数据",
                changedCount > 1
                    ? $"共修改 {changedCount} 句（第 {replacementPlan.StartIndex + 1}-{replacementPlan.EndIndex + 1} 句）。"
                    : $"{slotDisplayName}：{StoryCharacterSlotService.FormatClipboard(_storyCharacterSlotClipboard)}");
            await RefreshStoryPreviewAsync();
        }

        private void CopyStoryAssetField(string fieldName)
        {
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyAssetClipboard = StoryAssetFieldService.CreateClipboard(row, fieldName);
            ShowStoryStatus(InfoBarSeverity.Success, "已复制基础素材", $"{StoryAssetFieldService.GetDisplayName(fieldName)}：{_storyAssetClipboard.Value}");
        }

        private async Task PasteStoryAssetFieldAsync(string fieldName)
        {
            if (!CanEditStoryData())
            {
                return;
            }

            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
            {
                return;
            }

            // 粘贴一律弹窗让用户自己选（连续更换 / 只换一句）——判据在 ConfirmStoryValueReplacementAsync。
            _alwaysAskStoryReplacementConfirm = true;

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

            // 同样走「连续更换」那一套（和「更换背景图 / BGM / 环境音」共用确认框与计划）。
            var displayName = StoryAssetFieldService.GetDisplayName(fieldName);
            var replacementPlan = StoryAssetFieldService.BuildForwardContiguousValuePlan(
                _storyRows,
                _currentStoryRowIndex,
                fieldName,
                _storyAssetClipboard.Value);
            var replacementChoice = await ConfirmStoryValueReplacementAsync(
                replacementPlan,
                displayName,
                value => value);
            if (replacementChoice is DialogResultKind.None or DialogResultKind.Cancel)
            {
                return;
            }

            if (replacementChoice == DialogResultKind.Secondary)
            {
                replacementPlan = replacementPlan with
                {
                    EndIndex = replacementPlan.StartIndex,
                    Count = 1
                };
            }

            CaptureStoryUndoState(replacementPlan.Count > 1 ? $"连续粘贴{displayName}" : $"粘贴{displayName}");
            var changedCount = StoryAssetFieldService.ApplyForwardContiguousValuePlan(_storyRows, replacementPlan);
            QueueCurrentStoryDocumentSave();
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(
                InfoBarSeverity.Success,
                changedCount > 1 ? $"{displayName}已连续粘贴" : "已粘贴基础素材",
                changedCount > 1
                    ? $"共修改 {changedCount} 句（第 {replacementPlan.StartIndex + 1}-{replacementPlan.EndIndex + 1} 句）。"
                    : $"{displayName}：{_storyAssetClipboard.Value}");
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

        private async Task CycleStoryCharacterLayerAsync(int slotIndex, CharacterLayerKind layerKind, int delta)
        {
            if (!CanEditStoryData())
            {
                return;
            }

            var layerSpec = GetStoryCharacterLayerSpec(layerKind);
            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
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
                QueueCurrentStoryDocumentSave();
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

            QueueCurrentStoryDocumentSave();
            ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, layerNextIndex, StoryCharacterLayerChoiceFactory.GetDisplayName(layerSpec, paths, layerNextIndex));
            await RefreshStoryPreviewAsync();
        }

        private async Task CycleStoryCharacterAsync(int slotIndex, int delta)
        {
            if (!CanEditStoryData())
            {
                return;
            }

            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
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
            QueueCurrentStoryDocumentSave();
            await RefreshStoryPreviewAsync();
        }

        private void ClearStoryCharacterSlot(int slotIndex)
        {
            if (!CanEditStoryData())
            {
                return;
            }

            if (_currentStoryDocumentPath is null || _storyRows.Count == 0)
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
            QueueCurrentStoryDocumentSave();
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
