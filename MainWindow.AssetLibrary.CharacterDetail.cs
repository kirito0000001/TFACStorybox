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
using GalExcleTools.Controls;
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
using Microsoft.UI.Xaml.Hosting;
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
        private async Task CreateCharacterAsync()
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var input = await ShowCharacterEditorDialogAsync("创建角色", null);
            if (input is null)
            {
                return;
            }

            try
            {
                _characterWorkspaceService.CreateCharacter(_currentAssetLibrary, input);
            }
            catch (IOException ex)
            {
                AppendLog(LogKind.Warning, $"无法创建角色：{ex.Message}");
                return;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
            AppendLog(LogKind.User, $"创建角色：{input.Name}（{input.Code}）");
        }

        private async Task RenameCharacterAsync(CharacterInfo character)
        {
            var input = await ShowCharacterEditorDialogAsync("重命名角色", character);
            if (input is not null)
            {
                await RenameCharacterAsync(character, input);
            }
        }

        private async Task RenameCharacterAsync(CharacterInfo character, CharacterEditorInput input)
        {
            if (_currentAssetLibrary is null || !Directory.Exists(character.Path))
            {
                return;
            }

            CharacterInfo updatedCharacter;
            try
            {
                updatedCharacter = _characterWorkspaceService.RenameCharacter(_currentAssetLibrary, character, input);
            }
            catch (IOException ex)
            {
                AppendLog(LogKind.Warning, $"无法重命名角色：{ex.Message}");
                return;
            }

            _characterLayerAssetService.NormalizeFiles(
                CharacterLayerAssetService.GetCharacterFolderPath(updatedCharacter, CharacterLayerKind.Cloth),
                CharacterLayerKind.Cloth,
                string.Empty,
                input.Code);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
            if (_currentCharacter is not null && PathsEqual(_currentCharacter.Path, character.Path))
            {
                ShowCharacterDetailPage(updatedCharacter);
            }
            AppendLog(LogKind.User, $"重命名角色：{input.Name}（{input.Code}）");
            await Task.CompletedTask;
        }

        private async Task<CharacterEditorInput?> ShowCharacterEditorDialogAsync(string title, CharacterInfo? character)
        {
            var editorContent = EditorDialogContentFactory.CreateCharacterEditorContent(character);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                title,
                editorContent.Content,
                "确定",
                "取消"));
            if (result != DialogResultKind.Primary)
            {
                return null;
            }

            var input = editorContent.ReadInput();
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code))
            {
                AppendLog(LogKind.Warning, "角色名字和英文代号不能为空。");
                return null;
            }

            return input;
        }

        /// <summary>打开详情前的分类目录，收起时用它还原路径栏。</summary>
        private string _characterDetailReturnPath = string.Empty;

        private void ShowCharacterDetailPage(CharacterInfo character)
        {
            _currentCharacter = character;
            var cancellationToken = ResetCharacterDetailLoadCancellation();
            RefreshCharacterSettingButtons(character.Name, character.Code, character.ColorHex);
            RunCharacterDetailLoad(LoadCharacterDetailLayersAsync(character, cancellationToken));

            // 不再切页面：详情作为子面板盖在卡片区上，外层（元数据条 / 路径栏 / 状态栏）不动。
            // 这样从分类 → 立绘 → 角色 一路点下去是连续的，不会像换了个软件。
            _characterDetailReturnPath = _currentAssetLibrary is null
                ? string.Empty
                : GetCharacterFolderPath(_currentAssetLibrary);
            AssetBrowserHost?.SetCardsVisible(false);
            AssetBrowserHost?.SetPath($"{_characterDetailReturnPath}\\{character.Name}（{character.Code}）");
            if (!_characterDetailInsetHooked && AssetBrowserHost is not null)
            {
                // 窗口尺寸变了，路径栏高度会变（字换行/内边距），让位量得跟着走。
                _characterDetailInsetHooked = true;
                AssetBrowserHost.SizeChanged += (_, _) =>
                {
                    if (CharacterDetailPage.Visibility == Visibility.Visible)
                    {
                        ApplyCharacterDetailInset();
                    }
                };
            }

            // 底部状态条这一段在详情打开时用不上（它显示的是分类层的信息），
            // 收掉它，预览就能一直落到窗口底部，不会留一条黑边。
            AssetLibraryDetailStatusBorder.Visibility = Visibility.Collapsed;
            // 行距也收掉：状态条隐藏后它还留着 16px 的空档，预览会在底部凭空断开。
            if (AssetLibraryDetailPage is { } page)
            {
                page.RowSpacing = 0;
            }

            // 递增令牌：万一下一帧还有"退场后折叠"的任务在排队，它会被作废，
            // 否则快速来回点时面板会被自己上一次的退场逻辑收起。
            _characterDetailAnimToken++;
            ApplyCharacterDetailInset();
            CharacterDetailPage.Visibility = Visibility.Visible;
            PlayCharacterDetailEntrance();
            AppendLog(LogKind.User, $"打开角色详情：{character.Name}");
        }

        private async Task LoadCharacterDetailLayersAsync(CharacterInfo character, CancellationToken cancellationToken)
        {
            _characterWorkspaceService.EnsureCharacterSubfolders(character.Path);

            var clothFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Cloth);
            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Face);
            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Adorn);
            var vfxFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Vfx);

            _characterLayerAssetService.NormalizeFiles(clothFolderPath, CharacterLayerKind.Cloth, CharacterLayerAssetService.GetDefaultScope(0), character.Code);
            var costumeCount = CharacterLayerAssetService.GetImagePaths(clothFolderPath).Count;
            var defaultCostumeScope = CharacterLayerAssetService.GetDefaultScope(costumeCount);
            _characterLayerAssetService.NormalizeFiles(faceFolderPath, CharacterLayerKind.Face, defaultCostumeScope);
            _characterLayerAssetService.NormalizeFiles(adornFolderPath, CharacterLayerKind.Adorn, defaultCostumeScope);
            _characterLayerAssetService.NormalizeFiles(vfxFolderPath, CharacterLayerKind.Vfx, "ALL");

            var clothPaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Cloth);
            var facePaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Face);
            var adornPaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Adorn);
            var vfxPaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Vfx);

            _selectedCharacterClothPath = ResolveSelectedCharacterLayerPath(_selectedCharacterClothPath, clothPaths);
            _selectedCharacterFacePath = ResolveSelectedCharacterLayerPath(_selectedCharacterFacePath, facePaths);
            _selectedCharacterAdornPath = ResolveSelectedCharacterLayerPath(_selectedCharacterAdornPath, adornPaths);
            _selectedCharacterVfxPath = ResolveSelectedCharacterLayerPath(_selectedCharacterVfxPath, vfxPaths);

            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Cloth, cancellationToken);
            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Face, cancellationToken);
            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Adorn, cancellationToken);
            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Vfx, cancellationToken);
            await UpdateCharacterLayerPreviewAsync();
        }

        private void LoadCharacterDetailLayers(CharacterInfo character)
        {
            RunCharacterDetailLoad(LoadCharacterDetailLayersAsync(character, GetCharacterDetailLoadToken()));
        }

        private static string? ResolveSelectedCharacterLayerPath(string? selectedPath, IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                var exactMatch = paths.FirstOrDefault(path => PathsEqual(path, selectedPath));
                if (exactMatch is not null)
                {
                    return exactMatch;
                }

                var selectedName = Path.GetFileName(selectedPath);
                var nameMatch = paths.FirstOrDefault(path => string.Equals(Path.GetFileName(path), selectedName, StringComparison.OrdinalIgnoreCase));
                if (nameMatch is not null)
                {
                    return nameMatch;
                }
            }

            return paths[0];
        }

        private void ReloadCharacterDetailLayersPreservingScroll()
        {
            if (_currentCharacter is null)
            {
                return;
            }

            ReloadCharacterDetailLayersPreservingScroll(
                CharacterDetailScrollViewer.HorizontalOffset,
                CharacterDetailScrollViewer.VerticalOffset);
        }

        private void ReloadCharacterDetailLayersPreservingScroll(double horizontalOffset, double verticalOffset)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            RunCharacterDetailLoad(LoadCharacterDetailLayersAsync(_currentCharacter, GetCharacterDetailLoadToken()));
            _ = RestoreCharacterDetailScrollAsync(horizontalOffset, verticalOffset);
        }

        private async Task RestoreCharacterDetailScrollAsync(double horizontalOffset, double verticalOffset)
        {
            CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
            DispatcherQueue.TryEnqueue(() =>
            {
                CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
                DispatcherQueue.TryEnqueue(() =>
                {
                    CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
                });
            });
            await Task.Delay(60);
            CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
            await Task.Delay(160);
            CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
        }

        private async Task LoadCharacterDetailLayerAsync(
            CharacterInfo character,
            CharacterLayerKind layerKind,
            CancellationToken cancellationToken,
            bool forcePopulateCards = false)
        {
            var folderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, layerKind);
            if (layerKind == CharacterLayerKind.Vfx)
            {
                await LoadCharacterVfxLayerAsync(folderPath, cancellationToken, forcePopulateCards);
            }
            else
            {
                await LoadCharacterImageLayerAsync(GetCharacterLayerGridView(layerKind), folderPath, layerKind, cancellationToken, forcePopulateCards);
            }
        }

        private async Task LoadCharacterImageLayerAsync(
            GridView gridView,
            string folderPath,
            CharacterLayerKind layerKind,
            CancellationToken cancellationToken,
            bool forcePopulateCards = false)
        {
            Directory.CreateDirectory(folderPath);
            gridView.Items.Clear();

            var imagePaths = CharacterLayerAssetService.GetImagePaths(folderPath);
            if (forcePopulateCards || IsCharacterLayerExpanderExpanded(layerKind))
            {
                await AddGridViewItemsInBatchesAsync(
                    gridView,
                    imagePaths,
                    imagePath => CreateCharacterImageLayerCard(imagePath, layerKind),
                    cancellationToken,
                    batchSize: 8);
            }

            UpdateCharacterLayerExpanderHeader(layerKind, imagePaths.Count);
        }

        private void LoadCharacterImageLayer(GridView gridView, string folderPath, CharacterLayerKind layerKind)
        {
            RunCharacterDetailLoad(LoadCharacterImageLayerAsync(gridView, folderPath, layerKind, GetCharacterDetailLoadToken()));
        }

        private void UpdateCharacterLayerExpanderHeader(CharacterLayerKind layerKind, int count)
        {
            switch (layerKind)
            {
                case CharacterLayerKind.Cloth:
                    CharacterClothExpander.Header = $"服装 DN_Cloth [数量：{count}]";
                    break;
                case CharacterLayerKind.Face:
                    CharacterFaceExpander.Header = $"表情 FC_Face [数量：{count}]";
                    break;
                case CharacterLayerKind.Adorn:
                    CharacterAdornExpander.Header = $"装饰 AD_Adorn [数量：{count}]";
                    break;
                case CharacterLayerKind.Vfx:
                    CharacterVfxExpander.Header = $"VFX 滤镜 [数量：{count}]";
                    break;
            }
        }

        private GridView GetCharacterLayerGridView(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => CharacterClothGridView,
                CharacterLayerKind.Face => CharacterFaceGridView,
                CharacterLayerKind.Adorn => CharacterAdornGridView,
                CharacterLayerKind.Vfx => CharacterVfxGridView,
                _ => CharacterClothGridView
            };
        }

        private bool IsCharacterLayerExpanderExpanded(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => CharacterClothExpander.IsExpanded,
                CharacterLayerKind.Face => CharacterFaceExpander.IsExpanded,
                CharacterLayerKind.Adorn => CharacterAdornExpander.IsExpanded,
                CharacterLayerKind.Vfx => CharacterVfxExpander.IsExpanded,
                _ => false
            };
        }

        private GridViewItem CreateCharacterImageLayerCard(string imagePath, CharacterLayerKind layerKind)
        {
            TappedEventHandler? tappedHandler = (_, args) =>
            {
                PlaySelectionSound();
                ShowCharacterLayerViewerPage(imagePath, layerKind);
                args.Handled = true;
            };
            MenuFlyout? contextFlyout = null;

            if (layerKind == CharacterLayerKind.Cloth)
            {
                contextFlyout = GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetCharacterClothRemarkAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("设置预览", async (_, _) => await SetCharacterPortraitPreviewAsync(CharacterLayerKind.Cloth, imagePath)),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceCharacterLayerAsync(CharacterLayerKind.Cloth, imagePath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteCharacterClothAsync(imagePath)));
            }
            else if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn)
            {
                contextFlyout = GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) =>
                    {
                        if (layerKind == CharacterLayerKind.Face)
                        {
                            await SetCharacterFaceRemarkAsync(imagePath);
                        }
                        else
                        {
                            await SetCharacterAdornRemarkAsync(imagePath);
                        }
                    }),
                    GridViewItemFactory.CreateMenuItem("设置预览", async (_, _) => await SetCharacterPortraitPreviewAsync(layerKind, imagePath)),
                    GridViewItemFactory.CreateMenuItem("可用范围", async (_, _) =>
                    {
                        if (layerKind == CharacterLayerKind.Face)
                        {
                            await SetCharacterFaceAvailabilityAsync(imagePath);
                        }
                        else
                        {
                            await SetCharacterAdornAvailabilityAsync(imagePath);
                        }
                    }),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceCharacterLayerAsync(layerKind, imagePath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) =>
                    {
                        if (layerKind == CharacterLayerKind.Face)
                        {
                            await DeleteCharacterFaceAsync(imagePath);
                        }
                        else
                        {
                            await DeleteCharacterAdornAsync(imagePath);
                        }
                    }));
            }
            else
            {
                tappedHandler = async (_, args) =>
                {
                    PlaySelectionSound();
                    SetSelectedCharacterLayer(layerKind, imagePath);
                    await UpdateCharacterLayerPreviewAsync();
                    args.Handled = true;
                };
            }

            var content = AssetCardContentFactory.CreateImageAssetCardContent(imagePath, 178, 152, tagWithPath: true);
            var warningMessage = GetCharacterLayerPortraitPreviewWarningMessage(imagePath, layerKind);
            var cardContent = warningMessage is null
                ? content
                : CreateWarningBadgeOverlay(content, warningMessage);

            return AssetCardFactory.CreateCard(
                190,
                190,
                cardContent,
                imagePath,
                tappedHandler,
                contextFlyout,
                marginRight: 16,
                marginBottom: 16);
        }

        private async Task LoadCharacterVfxLayerAsync(string folderPath, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            Directory.CreateDirectory(folderPath);
            CharacterVfxGridView.Items.Clear();

            var vfxPaths = Directory
                .EnumerateFiles(folderPath)
                .OrderBy(Path.GetFileName)
                .ToList();

            if (forcePopulateCards || CharacterVfxExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    CharacterVfxGridView,
                    vfxPaths,
                    CreateCharacterVfxIndexCard,
                    cancellationToken);
            }

            UpdateCharacterLayerExpanderHeader(CharacterLayerKind.Vfx, vfxPaths.Count);
        }

        private void LoadCharacterVfxLayer(string folderPath)
        {
            RunCharacterDetailLoad(LoadCharacterVfxLayerAsync(folderPath, GetCharacterDetailLoadToken()));
        }

        private GridViewItem CreateCharacterVfxIndexCard(string vfxPath)
        {
            return AssetCardFactory.CreateCard(
                220,
                92,
                AssetCardContentFactory.CreateIconAssetCardContent(
                    Symbol.Filter,
                    Path.GetFileNameWithoutExtension(vfxPath)),
                vfxPath,
                async (_, _) =>
                {
                    _selectedCharacterVfxPath = vfxPath;
                    await UpdateCharacterLayerPreviewAsync();
                });
        }

        private void SetSelectedCharacterLayer(CharacterLayerKind layerKind, string imagePath)
        {
            switch (layerKind)
            {
                case CharacterLayerKind.Cloth:
                    _selectedCharacterClothPath = imagePath;
                    break;
                case CharacterLayerKind.Face:
                    _selectedCharacterFacePath = imagePath;
                    break;
                case CharacterLayerKind.Adorn:
                    _selectedCharacterAdornPath = imagePath;
                    break;
                case CharacterLayerKind.Vfx:
                    _selectedCharacterVfxPath = imagePath;
                    break;
            }

            if (layerKind == CharacterLayerKind.Cloth && _currentCharacter is not null)
            {
                _selectedCharacterFacePath = ResolveSelectedCharacterLayerPath(_selectedCharacterFacePath, GetCharacterDetailLayerPaths(CharacterLayerKind.Face));
                _selectedCharacterAdornPath = ResolveSelectedCharacterLayerPath(_selectedCharacterAdornPath, GetCharacterDetailLayerPaths(CharacterLayerKind.Adorn));
                _selectedCharacterVfxPath = ResolveSelectedCharacterLayerPath(_selectedCharacterVfxPath, GetCharacterDetailLayerPaths(CharacterLayerKind.Vfx));
            }
        }

        private async Task UpdateCharacterLayerPreviewAsync()
        {
            var hasCloth = await SetCharacterPreviewImageAsync(CharacterPreviewClothImage, _selectedCharacterClothPath);
            var hasFace = await SetCharacterPreviewImageAsync(
                CharacterPreviewFaceImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterFacePath) ? _selectedCharacterFacePath : null);
            var hasAdorn = await SetCharacterPreviewImageAsync(
                CharacterPreviewAdornImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterAdornPath) ? _selectedCharacterAdornPath : null);
            var hasVfx = await SetCharacterPreviewImageAsync(
                CharacterPreviewVfxImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterVfxPath) ? _selectedCharacterVfxPath : null);
            var hasAnyLayer = hasCloth || hasFace || hasAdorn || hasVfx;

            CharacterPreviewEmptyText.Visibility = hasAnyLayer ? Visibility.Collapsed : Visibility.Visible;
        }

        private void CharacterPreviewSurface_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("服装", async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Cloth)),
                GridViewItemFactory.CreateMenuItem("表情", async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Face)),
                GridViewItemFactory.CreateMenuItem("装饰", async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Adorn)))
                .ShowAt(CharacterPreviewSurface);
            e.Handled = true;
        }

        // ---------- 预览区：滚轮缩放 / 左键拖动 / 双击复原 ----------

        private bool _previewDragging;
        private Windows.Foundation.Point _previewDragOrigin;
        private double _previewTranslateOriginX;
        private double _previewTranslateOriginY;

        private const double PreviewMinScale = 0.3;
        private const double PreviewMaxScale = 6.0;

        private void CharacterPreviewSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(CharacterPreviewSurface);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            // 按住即拖动：这个区域不再有"点一下打开大图"的语义（左栏本身就是大图）。
            _previewDragging = true;
            _previewDragOrigin = point.Position;
            _previewTranslateOriginX = CharacterPreviewTranslate.X;
            _previewTranslateOriginY = CharacterPreviewTranslate.Y;

            CharacterPreviewSurface.CapturePointer(e.Pointer);
            CharacterPreviewSurface.Focus(FocusState.Programmatic);
            e.Handled = true;
        }

        private void CharacterPreviewSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_previewDragging)
            {
                return;
            }

            var position = e.GetCurrentPoint(CharacterPreviewSurface).Position;
            CharacterPreviewTranslate.X = _previewTranslateOriginX + (position.X - _previewDragOrigin.X);
            CharacterPreviewTranslate.Y = _previewTranslateOriginY + (position.Y - _previewDragOrigin.Y);
            e.Handled = true;
        }

        private void CharacterPreviewSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_previewDragging)
            {
                return;
            }

            _previewDragging = false;
            CharacterPreviewSurface.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void CharacterPreviewSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            // 拖到窗口外松手时收不到 Released，靠这个兜底，否则会一直"粘"在鼠标上。
            _previewDragging = false;
        }

        private void CharacterPreviewSurface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(CharacterPreviewSurface).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            // 每格 10%：一次滚动一格滚轮，手感比"每格 1.2 倍"细腻。
            var factor = delta > 0 ? 1.1 : 1 / 1.1;
            var next = Math.Clamp(CharacterPreviewScale.ScaleX * factor, PreviewMinScale, PreviewMaxScale);
            CharacterPreviewScale.ScaleX = next;
            CharacterPreviewScale.ScaleY = next;
            e.Handled = true;
        }

        private void CharacterPreviewSurface_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetCharacterPreviewTransform();
            e.Handled = true;
        }

        private void ResetCharacterPreviewTransform()
        {
            CharacterPreviewScale.ScaleX = 1;
            CharacterPreviewScale.ScaleY = 1;
            CharacterPreviewTranslate.X = 0;
            CharacterPreviewTranslate.Y = 0;
        }

        private async void CharacterPreviewSurface_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Q:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Adorn, -1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.E:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Adorn, 1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.A:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Face, -1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.D:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Face, 1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Z:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Cloth, -1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.C:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Cloth, 1);
                    e.Handled = true;
                    break;
            }
        }

        private async void CharacterPreviewChooseCloth_Click(object sender, RoutedEventArgs e)
        {
            await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Cloth);
        }

        private async void CharacterPreviewChooseFace_Click(object sender, RoutedEventArgs e)
        {
            await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Face);
        }

        private async void CharacterPreviewChooseAdorn_Click(object sender, RoutedEventArgs e)
        {
            await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Adorn);
        }

        private async Task ChooseCharacterDetailLayerAsync(CharacterLayerKind layerKind)
        {
            var paths = GetCharacterDetailLayerPaths(layerKind);
            if (paths.Count == 0)
            {
                AppendLog(LogKind.Warning, $"当前角色没有可选择的{GetCharacterLayerDisplayName(layerKind)}。");
                return;
            }

            var selected = await _storyDialogService.SelectPreviewChoiceAsync(
                $"选择{GetCharacterLayerDisplayName(layerKind)}",
                paths.Select((path, index) => new StoryObjectChoice(
                    index.ToString(),
                    $"{index}: {Path.GetFileNameWithoutExtension(path)}",
                    path,
                    BuildCharacterDetailLayerPreviewPaths(layerKind, path))).ToList());
            if (selected is not string selectedPath || !File.Exists(selectedPath))
            {
                return;
            }

            SetSelectedCharacterLayer(layerKind, selectedPath);
            await UpdateCharacterLayerPreviewAsync();
            var selectedIndex = paths.FindIndex(path => PathsEqual(path, selectedPath));
            ShowCharacterDetailLayerChangedStatus(layerKind, selectedIndex, selectedPath);
            CharacterPreviewSurface.Focus(FocusState.Programmatic);
        }

        private async Task CycleCharacterDetailLayerAsync(CharacterLayerKind layerKind, int direction)
        {
            var paths = GetCharacterDetailLayerPaths(layerKind);
            if (paths.Count == 0)
            {
                return;
            }

            var currentPath = GetSelectedCharacterLayerPath(layerKind);
            var currentIndex = string.IsNullOrWhiteSpace(currentPath)
                ? -1
                : paths.FindIndex(path => PathsEqual(path, currentPath));
            var nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + direction + paths.Count) % paths.Count;

            SetSelectedCharacterLayer(layerKind, paths[nextIndex]);
            await UpdateCharacterLayerPreviewAsync();
            ShowCharacterDetailLayerChangedStatus(layerKind, nextIndex, paths[nextIndex]);
        }

        private IReadOnlyList<string> BuildCharacterDetailLayerPreviewPaths(CharacterLayerKind layerKind, string candidatePath)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => BuildStoryChoicePreviewPaths(
                    candidatePath,
                    _currentCharacter is not null && _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, candidatePath, _selectedCharacterFacePath, ComputeFileHash) ? _selectedCharacterFacePath : null,
                    _currentCharacter is not null && _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, candidatePath, _selectedCharacterAdornPath, ComputeFileHash) ? _selectedCharacterAdornPath : null),
                CharacterLayerKind.Face => BuildStoryChoicePreviewPaths(_selectedCharacterClothPath, candidatePath, _selectedCharacterAdornPath),
                CharacterLayerKind.Adorn => BuildStoryChoicePreviewPaths(_selectedCharacterClothPath, _selectedCharacterFacePath, candidatePath),
                CharacterLayerKind.Vfx => BuildStoryChoicePreviewPaths(_selectedCharacterClothPath, _selectedCharacterFacePath, _selectedCharacterAdornPath, candidatePath),
                _ => BuildStoryChoicePreviewPaths(candidatePath)
            };
        }

        private void ShowCharacterDetailLayerChangedStatus(CharacterLayerKind layerKind, int index, string path)
        {
            CharacterDetailInfoBar.Severity = InfoBarSeverity.Success;
            CharacterDetailInfoBar.Title = $"已更换{GetCharacterLayerDisplayName(layerKind)}";
            CharacterDetailInfoBar.Message = $"{Path.GetFileNameWithoutExtension(path)}（索引 {Math.Max(0, index)}）";
            CharacterDetailInfoBar.IsOpen = true;
        }

        private void CycleCharacterDetailCharacter(int direction)
        {
            if (_currentAssetLibrary is null || _currentCharacter is null)
            {
                return;
            }

            var characters = GetCharactersForAssetLibrary(_currentAssetLibrary)
                .OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (characters.Count == 0)
            {
                return;
            }

            var currentIndex = characters.FindIndex(character => PathsEqual(character.Path, _currentCharacter.Path));
            var nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + direction + characters.Count) % characters.Count;
            ShowCharacterDetailPage(characters[nextIndex]);
        }

        private void ClearCharacterDetailPreview()
        {
            _selectedCharacterClothPath = null;
            _selectedCharacterFacePath = null;
            _selectedCharacterAdornPath = null;
            _selectedCharacterVfxPath = null;
            _ = UpdateCharacterLayerPreviewAsync();
        }

        private List<string> GetCharacterDetailLayerPaths(CharacterLayerKind layerKind)
        {
            if (_currentCharacter is null)
            {
                return [];
            }

            var paths = CharacterLayerAssetService.GetLayerPaths(_currentCharacter, layerKind);
            if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn or CharacterLayerKind.Vfx)
            {
                paths = paths
                    .Where(path => _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, _selectedCharacterClothPath, path, ComputeFileHash))
                    .ToList();
            }

            return paths;
        }

        private string? GetSelectedCharacterLayerPath(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => _selectedCharacterClothPath,
                CharacterLayerKind.Face => _selectedCharacterFacePath,
                CharacterLayerKind.Adorn => _selectedCharacterAdornPath,
                CharacterLayerKind.Vfx => _selectedCharacterVfxPath,
                _ => null
            };
        }

        private static string GetCharacterLayerDisplayName(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => "服装",
                CharacterLayerKind.Face => "表情",
                CharacterLayerKind.Adorn => "装饰",
                CharacterLayerKind.Vfx => "滤镜",
                _ => "图层"
            };
        }

        private bool IsCharacterLayerCompatibleWithSelectedCloth(string? layerPath)
        {
            return _currentCharacter is null
                ? false
                : _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, _selectedCharacterClothPath, layerPath, ComputeFileHash);
        }

        private static async Task<bool> SetCharacterPreviewImageAsync(Image image, string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) ||
                !File.Exists(imagePath) ||
                !ImageSetService.Extensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase))
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                return false;
            }

            image.Visibility = Visibility.Visible;
            await ThumbnailFactory.LoadThumbnailFromFileAsync(image, imagePath);
            return true;
        }

        private void CloseCharacterDetailButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            PlayNegativeSound();
            HideCharacterDetailPanel();
        }

        /// <summary>
        /// 给详情面板留出路径栏那一整条的高度。
        ///
        /// 为什么不把面板重挂进浏览器的 DetailHost：UIElement 换父节点会抛
        /// COMException 0x800F1000，而构造期抛异常就是"启动即退出"（2026-11-06 连崩两次）。
        /// 现在面板仍是原来的兄弟节点，只靠 Margin 让位 —— 高度从控件实际尺寸读，
        /// 路径栏改高也不用改这里。
        /// </summary>
        private int _characterDetailAnimToken;

        /// <summary>
        /// 详情面板的入场：淡入 + 从右边推进来 18px。
        ///
        /// 详情从"独立页面"改成"子面板"之后，原来那套 PlayPageEntrance 就轮不到它了
        /// —— 页面根本没切换。但完全无动画会显得生硬（用户 2026-11-06 指出"动画没了"），
        /// 所以单独补一段，参数与 PlayPageEntrance 保持一致（220ms、EaseOut）。
        /// </summary>
        private void PlayCharacterDetailEntrance()
        {
            var compositor = ElementCompositionPreview.GetElementVisual(CharacterDetailPage).Compositor;
            var visual = ElementCompositionPreview.GetElementVisual(CharacterDetailPage);

            var easing = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f),
                new System.Numerics.Vector2(0.2f, 1f));

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 1f, easing);
            fade.Duration = TimeSpan.FromMilliseconds(220);
            visual.Opacity = 0;
            visual.StartAnimation("Opacity", fade);

            var slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, easing);
            slide.Duration = TimeSpan.FromMilliseconds(220);
            visual.Offset = new System.Numerics.Vector3(18f, 0f, 0f);
            visual.StartAnimation("Offset", slide);
        }

        /// <summary>退场：淡出 140ms 后再真正折叠，否则会"啪"地消失。</summary>
        private void PlayCharacterDetailExit()
        {
            var visual = ElementCompositionPreview.GetElementVisual(CharacterDetailPage);
            var compositor = visual.Compositor;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.4f, 0f), new System.Numerics.Vector2(1f, 1f)));
            fade.Duration = TimeSpan.FromMilliseconds(140);
            visual.StartAnimation("Opacity", fade);
        }

        private bool _characterDetailInsetHooked;

        private void ApplyCharacterDetailInset()
        {
            if (AssetBrowserHost is null)
            {
                return;
            }

            // 让出路径栏高度，再留 4px 呼吸缝。
            // 底部再往外探 12：状态条虽已折叠，容器自身还留着一点内边距，
            // 实测那道空隙还有约 18px（用户要求"再少 2/3"）。
            var inset = AssetBrowserHost.ChromeHeight + 4;
            CharacterDetailPage.Margin = new Thickness(0, inset, 0, -12);
        }

        /// <summary>
        /// 收起角色详情子面板：卡片区回来，路径栏恢复成分类目录。
        /// **不重铺整个素材库页** —— 重铺会闪一下，正好破坏"还在同一页"的感觉。
        /// </summary>
        private void HideCharacterDetailPanel()
        {
            PlayCharacterDetailExit();

            // 140ms 后再折叠：立即 Collapsed 会看不到淡出。
            var token = ++_characterDetailAnimToken;
            _ = Task.Delay(160).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
            {
                if (token == _characterDetailAnimToken && CharacterDetailPage.Visibility == Visibility.Visible)
                {
                    CharacterDetailPage.Visibility = Visibility.Collapsed;
                }
            }));

            // 没有文字就别恢复：空 Border 会显示成一条灰条（用户看到的"底下多出一块 UI"）。
            AssetLibraryDetailStatusBorder.Visibility =
                string.IsNullOrWhiteSpace(AssetLibraryDetailStatusText.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            if (AssetLibraryDetailPage is { } page)
            {
                page.RowSpacing = 16;
            }

            ResetCharacterDetailLoadCancellation();

            if (AssetBrowserHost is null)
            {
                return;
            }

            AssetBrowserHost.SetCardsVisible(true);
            if (!string.IsNullOrEmpty(_characterDetailReturnPath))
            {
                AssetBrowserHost.SetPath(_characterDetailReturnPath);
            }
        }

        /// <summary>角色详情打开时，路径栏的"返回"先退详情，而不是直接退回库列表。</summary>
        private bool TryCloseCharacterDetailOnBack()
        {
            if (CharacterDetailPage.Visibility != Visibility.Visible)
            {
                return false;
            }

            CloseCharacterDetailButton_Click(this, new RoutedEventArgs());
            return true;
        }

        private async void CharacterDetailRenameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is not null)
            {
                await RenameCharacterAsync(_currentCharacter);
            }
        }

        /// <summary>把三项当前值刷到右侧的只读按钮上。</summary>
        private void RefreshCharacterSettingButtons(string name, string code, string colorHex)
        {
            SetCharacterSettingButton(CharacterNameButton, "名称", name);
            SetCharacterSettingButton(CharacterCodeButton, "英文代号", code);
            SetCharacterSettingButton(CharacterColorButton, "代表色", colorHex);
        }

        /// <summary>
        /// 一行"标签 + 值"的只读按钮内容：标签常驻左侧（次要色），值靠右。
        /// 不用 TextBox —— 编辑要走弹窗，避免误触改坏和"改了忘保存"。
        /// </summary>
        private static void SetCharacterSettingButton(Button button, string label, string value)
        {
            if (button is null)
            {
                return;
            }

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Opacity = 0.72,
                VerticalAlignment = VerticalAlignment.Center
            };

            var valueText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "（未设置）" : value,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueText, 1);

            grid.Children.Add(labelText);
            grid.Children.Add(valueText);
            button.Content = grid;
        }

        private async void EditCharacterNameButton_Click(object sender, RoutedEventArgs e)
        {
            await EditCharacterFieldAsync("修改名称", "名称", _currentCharacter?.Name, value => (value, null, null));
        }

        private async void EditCharacterCodeButton_Click(object sender, RoutedEventArgs e)
        {
            await EditCharacterFieldAsync("修改英文代号", "英文代号", _currentCharacter?.Code, value => (null, value, null));
        }

        private async void EditCharacterColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            // WinUI 自带的 ColorPicker：色环 + 明度/饱和度滑块 + HEX 输入，
            // 正好是"标准 HSV 那种"，不用自己画色轮。
            var picker = new ColorPicker
            {
                Color = ParseColor(_currentCharacter.ColorHex, Windows.UI.Color.FromArgb(255, 0, 0x8F, 0x8D)),
                IsAlphaEnabled = false,
                IsAlphaSliderVisible = false,
                IsAlphaTextInputVisible = false,
                IsMoreButtonVisible = false,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
                MinWidth = 340
            };

            var form = ToolboxDialog.BuildForm(
                ToolboxDialog.BuildField("代表色", picker, "拖色环取色，或直接输入十六进制。"));

            if (!await ToolboxDialog.ShowAsync(RootGrid.XamlRoot, "修改代表色", form, "保存", "取消"))
            {
                return;
            }

            var picked = picker.Color;
            var hex = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            await SaveCharacterSettingsAsync(null, null, NormalizeColorHex(hex));
        }

        /// <summary>弹一个小输入框，确认后立即保存（不再有"保存设置"这一步）。</summary>
        private async Task EditCharacterFieldAsync(
            string title,
            string label,
            string? current,
            Func<string, (string? Name, string? Code, string? Color)> pick)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var box = new TextBox
            {
                Text = current ?? string.Empty,
                MinWidth = 280
            };

            var form = ToolboxDialog.BuildForm(
                ToolboxDialog.BuildField(label, box, "改完点「保存」，立即生效。"));

            if (!await ToolboxDialog.ShowAsync(RootGrid.XamlRoot, title, form, "保存", "取消"))
            {
                return;
            }

            var (name, code, color) = pick(box.Text);
            await SaveCharacterSettingsAsync(name, code, color);
        }

        /// <summary>把三项合并成一次重命名（只传改动的那个，其余取当前值）。</summary>
        private async Task SaveCharacterSettingsAsync(string? name, string? code, string? color)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var input = new CharacterEditorInput(
                (name ?? _currentCharacter.Name).Trim(),
                (code ?? _currentCharacter.Code).Trim(),
                color ?? _currentCharacter.ColorHex);

            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "不能为空", "角色名称和英文代号都必须有值。");
                return;
            }

            PlayPositiveSound();
            await RenameCharacterAsync(_currentCharacter, input);
        }

        private async Task ReloadMusicPlayerSourceAsync(string musicPath)
        {
            if (!File.Exists(musicPath))
            {
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(musicPath);
            MusicPlayerElement.Source = MediaSource.CreateFromStorageFile(file);
            MusicPlayPauseButton.Content = "播放";
        }

        private void AssetLibraryDetailScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(AssetLibraryDetailScrollViewer).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            var direction = delta > 0 ? -1 : 1;
            var scrollDelta = 48 * _appSettings.AssetLibraryScrollSpeedMultiplier * direction;
            var targetOffset = Math.Clamp(
                AssetLibraryDetailScrollViewer.VerticalOffset + scrollDelta,
                0,
                AssetLibraryDetailScrollViewer.ScrollableHeight);
            AssetLibraryDetailScrollViewer.ChangeView(null, targetOffset, null, true);
            e.Handled = true;
        }

    }
}
