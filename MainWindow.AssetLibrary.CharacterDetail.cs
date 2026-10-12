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
            LoadCharacters(_currentAssetLibrary);
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
            LoadCharacters(_currentAssetLibrary);
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

        private void ShowCharacterDetailPage(CharacterInfo character)
        {
            _currentCharacter = character;
            var cancellationToken = ResetCharacterDetailLoadCancellation();
            CharacterDetailTabTitleText.Text = $"{character.Name} / {character.Code}";
            CharacterNameTextBox.Text = character.Name;
            CharacterCodeTextBox.Text = character.Code;
            CharacterColorTextBox.Text = character.ColorHex;
            RunCharacterDetailLoad(LoadCharacterDetailLayersAsync(character, cancellationToken));
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Visible;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            CharacterDetailCloseButton.Focus(FocusState.Programmatic);
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

        private void CharacterPreviewSurface_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            CharacterPreviewSurface.BorderBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
            CharacterPreviewSurface.BorderThickness = new Thickness(2);
            CharacterPreviewSurface.Focus(FocusState.Programmatic);
        }

        private void CharacterPreviewSurface_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            CharacterPreviewSurface.BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush;
            CharacterPreviewSurface.BorderThickness = new Thickness(1);
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

        private void CharacterPreviewSurface_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void CharacterPreviewSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(CharacterPreviewSurface);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            PlaySelectionSound();
            ShowCharacterCompositeViewerPage();
            e.Handled = true;
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
                !BackgroundImageService.Extensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase))
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
            if (_currentAssetLibrary is not null)
            {
                PlayNegativeSound();
                ShowAssetLibraryDetailPage(_currentAssetLibrary);
            }
        }

        private async void CharacterDetailRenameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is not null)
            {
                await RenameCharacterAsync(_currentCharacter);
            }
        }

        private async void SaveCharacterInlineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var input = new CharacterEditorInput(
                CharacterNameTextBox.Text.Trim(),
                CharacterCodeTextBox.Text.Trim(),
                NormalizeColorHex(CharacterColorTextBox.Text.Trim()));

            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code))
            {
                AppendLog(LogKind.Warning, "角色名称和英文代号不能为空。");
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
