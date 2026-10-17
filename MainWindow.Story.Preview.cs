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
        private void StoryEditorCanvasHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                element.Clip = new RectangleGeometry
                {
                    Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
                };
            }
        }

        private async Task RefreshStoryPreviewAsync(bool showInheritedTransitionStatus = true)
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
                await SetStoryBackgroundAsync(ParseInt(row.Get("BGindex")), cancellationToken, showInheritedTransitionStatus);
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshStoryCharacterSlotAsync(StoryCharacterSlot1, 1, row, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshStoryCharacterSlotAsync(StoryCharacterSlot2, 2, row, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshStoryCharacterSlotAsync(StoryCharacterSlot3, 3, row, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshStoryCharacterSlotAsync(StoryCharacterSlot4, 4, row, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshStoryCharacterSlotAsync(StoryCharacterSlot5, 5, row, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await SetStorySpeakerPreviewAsync(row.Get("TalkChar"), ParseInt(row.Get("TalkBody")), ParseInt(row.Get("TalkFace")), ParseInt(row.Get("TalkAdorn")), ParseInt(row.Get("TalkVfx")), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                UpdateStoryCharacterSlotLayout();
                UpdateStoryToolbarCurrentInfo();
                _storyEditorViewModel.AssetStatusText = _currentStoryAssetLibrary is null
                    ? "当前项目未关联素材库。"
                    : $"素材库：{_currentStoryAssetLibrary.Name} | 立绘：{(ShowFullCharacterArtCheckBox?.IsChecked == true ? "完整" : "上半身")}";
                // 当前这一句画完了，顺手把前后两句的立绘预热上（见 ScheduleStoryPreviewPreload）。
                ScheduleStoryPreviewPreload();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task SetStoryBackgroundAsync(
            int index,
            CancellationToken cancellationToken,
            bool showInheritedTransitionStatus)
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

            var backgrounds = ImageSetService.GetFilePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary));
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
            if (showInheritedTransitionStatus &&
                !string.IsNullOrWhiteSpace(previousPreviewKey) &&
                !string.Equals(previousPreviewKey, path, StringComparison.OrdinalIgnoreCase))
            {
                ShowPersistentStoryStatus(
                    InfoBarSeverity.Informational,
                    "背景切换模式",
                    StoryFunctionService.GetBackgroundTransitionModeRemark(_storyBackgroundTransitionMode));
            }
        }

        /// <summary>
        /// 立绘预加载：当前句停稳之后，把**上一句 / 下一句**用到的立绘先在后台解码进
        /// <c>_storyPreviewImageCache</c>，这样按 ←/→ 或鼠标侧键切过去时是命中缓存、立刻出图。
        ///
        /// 【为什么值得做】实测（你那份素材库）：
        ///   · 单张大图首次解码 50–95 ms（DecodePixelWidth=1280，PNG 必须整张解出来再缩放）；
        ///   · 第三章 533 行里，相邻两句**立绘组合不同**的占 54%（532 次切换里 289 次），
        ///     每次要解 2–6 张 → 一次切句 0.1–0.6 秒的"图慢半拍"。
        /// 路径在**排程时**（UI 线程、很便宜：查角色已走缓存、列目录 0.04 ms）就先算好，
        /// 异步那段只负责解码 —— 免得排队期间用户换了句、拿着旧状态乱解。
        /// 解码本身是 WIC 在后台线程做的（SetSourceAsync），所以不会把 UI 卡住。
        ///
        /// 【边界】只预热相邻两句、只预热立绘（不含背景图）：缓存上限 48 张、
        /// 每张 1280px 位图约 5 MB，预加载太多会把有用的挤出去、也白占内存。
        /// </summary>
        private void ScheduleStoryPreviewPreload()
        {
            var paths = CollectStoryPreviewPreloadPaths();
            if (paths.Count == 0)
            {
                return;
            }

            _storyPreviewPreloadCancellation?.Cancel();
            _storyPreviewPreloadCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _storyPreviewPreloadCancellation = cancellation;
            _ = PreloadStoryPreviewImagesAsync(paths, cancellation.Token);
        }

        private List<string> CollectStoryPreviewPreloadPaths()
        {
            var paths = new List<string>();
            if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
            {
                return paths;
            }

            foreach (var offset in new[] { 1, -1 })
            {
                var index = _currentStoryRowIndex + offset;
                if (index < 0 || index >= _storyRows.Count)
                {
                    continue;
                }

                var row = _storyRows[index];
                AddStorySlotPreviewPaths(row, "Talk", paths);
                var visibleSlots = GetVisibleStoryCharacterSlotCount();
                for (var slotIndex = 1; slotIndex <= visibleSlots; slotIndex++)
                {
                    AddStorySlotPreviewPaths(row, slotIndex.ToString(CultureInfo.InvariantCulture), paths);
                }
            }

            // 鼠标停着的那一位：把它**相邻索引**的图层也预热上。
            // 用户是按 A/D/Z/C/Q/E 一路翻着挑的（日志里那种连按），
            // 预热 ±1 之后"下一张"基本都命中缓存，只有这一串里的第一下要等解码。
            if (_hoveredStoryCharacterSlotIndex is int hoveredSlot)
            {
                AddStorySlotNeighborPreviewPaths(_storyRows[_currentStoryRowIndex], hoveredSlot, paths);
            }

            return paths;
        }

        /// <summary>这一位当前索引 ±1 的图层路径（给"连按切换"预热用）。</summary>
        private void AddStorySlotNeighborPreviewPaths(StoryRow row, int slotIndex, List<string> paths)
        {
            var columnSuffix = slotIndex == 0 ? "Talk" : slotIndex.ToString(CultureInfo.InvariantCulture);
            var characterFieldName = slotIndex == 0 ? "TalkChar" : $"Chara{columnSuffix}";
            var character = ResolveStoryCharacter(row.Get(characterFieldName));
            if (character is null)
            {
                return;
            }

            foreach (var (layerKind, columnName) in new[]
            {
                (CharacterLayerKind.Cloth, $"Body{columnSuffix}"),
                (CharacterLayerKind.Face, $"Face{columnSuffix}"),
                (CharacterLayerKind.Adorn, $"Adorn{columnSuffix}")
            })
            {
                var currentIndex = ParseInt(row.Get(columnName));
                foreach (var delta in new[] { 1, -1 })
                {
                    // GetStoryLayerPath 自己管装饰的偏移与越界（越界返回 null）。
                    var path = CharacterLayerAssetService.GetStoryLayerPath(character, layerKind, currentIndex + delta);
                    if (IsPreviewableImagePath(path) && !paths.Contains(path!))
                    {
                        paths.Add(path!);
                    }
                }
            }
        }

        /// <summary>把这一行某一位会用到的立绘图片路径收进来（<paramref name="columnSuffix"/> 是 "Talk" 或 "1"~"5"）。</summary>
        private void AddStorySlotPreviewPaths(StoryRow row, string columnSuffix, List<string> paths)
        {
            var characterFieldName = columnSuffix == "Talk" ? "TalkChar" : $"Chara{columnSuffix}";
            var character = ResolveStoryCharacter(row.Get(characterFieldName));
            if (character is null)
            {
                return;
            }

            var candidates = new[]
            {
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Cloth, ParseInt(row.Get($"Body{columnSuffix}"))),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Face, ParseInt(row.Get($"Face{columnSuffix}"))),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Adorn, ParseInt(row.Get($"Adorn{columnSuffix}")))
            };

            foreach (var path in candidates)
            {
                if (IsPreviewableImagePath(path) && !paths.Contains(path!))
                {
                    paths.Add(path!);
                }
            }
        }

        private async Task PreloadStoryPreviewImagesAsync(List<string> paths, CancellationToken cancellationToken)
        {
            try
            {
                // 停一下再开始：连续翻页时每一页都会取消上一次预加载，
                // 所以这条只在"用户停在这一句上"时才真的跑。
                await Task.Delay(StoryPreviewPreloadDelay, cancellationToken);
                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_storyPreviewImageCache.ContainsKey(path))
                    {
                        continue;
                    }

                    await GetCachedStoryPreviewImageAsync(path, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // 预加载纯属加速：失败就当没做，绝不冒泡到界面。
            }
        }

        /// <summary>
        /// 刷新一个立绘位（1~5 号位，列名就是 Chara{n} / Body{n} / Face{n} / Adorn{n} / Vfx{n}）。
        ///
        /// ⚠️ **折叠着的位直接跳过。** 4 / 5 号位默认是收起来的，但以前照样按行数据
        /// 去查素材、解码图片 —— 一次刷新最多白解 6 张图（每张实测 50–95 ms）。
        /// 跳过的位不写预览 key，所以等它被展开时（key 里含可见位数量）会自己重建。
        /// </summary>
        private async Task RefreshStoryCharacterSlotAsync(
            Border slot,
            int slotIndex,
            StoryRow row,
            CancellationToken cancellationToken)
        {
            if (slot.Visibility != Visibility.Visible)
            {
                return;
            }

            await SetStoryCharacterSlotAsync(
                slot,
                slotIndex,
                row.Get($"Chara{slotIndex}"),
                ParseInt(row.Get($"Body{slotIndex}")),
                ParseInt(row.Get($"Face{slotIndex}")),
                ParseInt(row.Get($"Adorn{slotIndex}")),
                ParseInt(row.Get($"Vfx{slotIndex}")),
                cancellationToken);
        }

        private async Task SetStoryCharacterSlotAsync(Border slot, int slotIndex, string characterName, int bodyIndex, int faceIndex, int adornIndex, int vfxIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grid = new Grid();
            slot.Tag = slotIndex;
            // 下面这三样每次预览刷新都会走到，但**内容从不改变**：画笔是常量、
            // 右键菜单只跟 slotIndex 有关。以前每刷一次就给 6 个位重新 new 一遍
            // （画笔 + 一个 MenuFlyout + 5 个 MenuFlyoutItem + 5 个 lambda），
            // 编辑时那是每秒好几次的白活，还顺带让合成器反复失效。
            if (!ReferenceEquals(slot.Background, StorySlotIdleBrush))
            {
                slot.Background = StorySlotIdleBrush;
            }

            if (slot.ContextFlyout is null)
            {
                slot.ContextFlyout = CreateStoryCharacterSlotMenu(slotIndex);
            }

            slot.PointerMoved -= StoryCharacterSlot_PointerMoved;
            slot.PointerExited -= StoryCharacterSlot_PointerExited;
            slot.PointerMoved += StoryCharacterSlot_PointerMoved;
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
            if (StorySpeakerPreviewSlot.ContextFlyout is null)
            {
                StorySpeakerPreviewSlot.ContextFlyout = CreateStorySpeakerSlotMenu();
            }
            StorySpeakerPreviewSlot.PointerMoved -= StoryCharacterSlot_PointerMoved;
            StorySpeakerPreviewSlot.PointerExited -= StoryCharacterSlot_PointerExited;
            StorySpeakerPreviewSlot.PointerMoved += StoryCharacterSlot_PointerMoved;
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
                ImageSetService.Extensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase);
        }

        // ── 立绘位那几个"一直长这样"的画刷 ──────────────────────────────────
        // 预览是**每次编辑、每次切句**都要重刷的（RefreshStoryPreviewAsync），
        // 以前每次都给 6 个位 new 一遍 SolidColorBrush / RectangleGeometry —— 白扔对象，
        // 还会让合成器反复失效。这几个颜色是常量，共享一份就够。
        private static readonly SolidColorBrush StorySlotIdleBrush =
            new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        private static readonly SolidColorBrush StorySpeakerIdleBrush =
            new(Windows.UI.Color.FromArgb(34, 255, 255, 255));

        private static readonly SolidColorBrush StorySlotHoverBrush =
            new(Windows.UI.Color.FromArgb(42, 255, 255, 255));

        private static readonly SolidColorBrush StorySlotHoverBorderBrush =
            new(Microsoft.UI.Colors.White);

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

            // 尺寸没变就别换几何体：换一次就是一次布局 / 合成失效，
            // 而这个函数在每次预览刷新、每次 SizeChanged 上都会被调到。
            if (border.Clip is RectangleGeometry existing &&
                Math.Abs(existing.Rect.Width - width) < 0.5 &&
                Math.Abs(existing.Rect.Height - height) < 0.5)
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
