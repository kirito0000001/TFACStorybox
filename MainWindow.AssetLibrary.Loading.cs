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
        private async void LoadBackgroundImages(AssetLibraryInfo assetLibrary)
        {
            await ReloadBackgroundImagesAsync(assetLibrary);
        }

        private void RegisterAssetLibraryExpanderLazyLoading()
        {
            BackgroundImagesExpander.Expanding += BackgroundImagesExpander_Expanded;
            PropImagesExpander.Expanding += PropImagesExpander_Expanded;
            MusicExpander.Expanding += MusicExpander_Expanded;
            AmbientSoundExpander.Expanding += AmbientSoundExpander_Expanded;
            SoundEffectExpander.Expanding += SoundEffectExpander_Expanded;
            CharactersExpander.Expanding += CharactersExpander_Expanded;
            FunctionExpander.Expanding += FunctionExpander_Expanded;
            CharacterFilterExpander.Expanding += CharacterFilterExpander_Expanded;
        }

        private void RegisterCharacterDetailExpanderLazyLoading()
        {
            CharacterClothExpander.Expanding += CharacterClothExpander_Expanding;
            CharacterFaceExpander.Expanding += CharacterFaceExpander_Expanding;
            CharacterAdornExpander.Expanding += CharacterAdornExpander_Expanding;
            CharacterVfxExpander.Expanding += CharacterVfxExpander_Expanding;
        }

        private void RefreshAssetLibraryDetailSections(AssetLibraryInfo assetLibrary)
        {
            var cancellationToken = GetAssetLibraryLoadToken();
            RunAssetLibraryLoad(RefreshBackgroundImageCardsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Music, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Ambient, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.SoundEffect, cancellationToken));
            RunAssetLibraryLoad(LoadFunctionsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharactersAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharacterFiltersAsync(assetLibrary, cancellationToken));
        }

        private void BackgroundImagesExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadBackgroundImagesAsync(_currentAssetLibrary, true));
            }
        }

        private void MusicExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadAudioFilesAsync(_currentAssetLibrary, AudioAssetKind.Music, true));
            }
        }

        private void AmbientSoundExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadAudioFilesAsync(_currentAssetLibrary, AudioAssetKind.Ambient, true));
            }
        }

        private void SoundEffectExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadAudioFilesAsync(_currentAssetLibrary, AudioAssetKind.SoundEffect, true));
            }
        }

        private void CharactersExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(LoadCharactersAsync(_currentAssetLibrary, GetAssetLibraryLoadToken(), true));
            }
        }

        private void FunctionExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(LoadFunctionsAsync(_currentAssetLibrary, GetAssetLibraryLoadToken(), true));
            }
        }

        private void CharacterFilterExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(LoadCharacterFiltersAsync(_currentAssetLibrary, GetAssetLibraryLoadToken(), true));
            }
        }

        private void CharacterClothExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Cloth);
        }

        private void CharacterFaceExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Face);
        }

        private void CharacterAdornExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Adorn);
        }

        private void CharacterVfxExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Vfx);
        }

        private void LoadCharacterDetailLayerOnExpand(CharacterLayerKind layerKind)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            RunCharacterDetailLoad(LoadCharacterDetailLayerAsync(_currentCharacter, layerKind, GetCharacterDetailLoadToken(), true));
        }

        private async Task ReloadBackgroundImagesAsync(AssetLibraryInfo assetLibrary, bool forcePopulateCards = false)
        {
            var backgroundFolderPath = GetBackgroundFolderPath(assetLibrary);
            Directory.CreateDirectory(backgroundFolderPath);

            if (!_isNormalizingBackgroundImages)
            {
                _isNormalizingBackgroundImages = true;
                try
                {
                    // ⚠️ 这一步会**给整个目录重新编号**（把 jpg/webp 转成 png，再按名字重排成 BG00…）。
                    // 剧情里的 BGindex 是**位置**（排序后 png 列表的下标），所以"重新编号"
                    // 必须和"把旧位置映射到新位置"一起做完 ——
                    // 2026-10-16 的坑：这条自动规范化只改名、**不同步索引**，而它的排序把
                    // jpg/webp 也算进去（剧情的位置只数 png），于是只要目录里有过一张 jpg，
                    // 变动点之后的背景就整体错位一格 —— 用户报的"换序后有些背景乱了"。
                    var beforeOrderedPaths = ImageSetService.GetFilePaths(backgroundFolderPath);
                    var normalizeOrder = ImageSetService.GetNormalizeSourcePaths(backgroundFolderPath);
                    var normalizeRemap = BuildPositionalAssetIndexRemap(beforeOrderedPaths, normalizeOrder);
                    await NormalizeBackgroundImagesAsync(backgroundFolderPath);
                    if (normalizeRemap.Count > 0)
                    {
                        LogAssetIndexRemapEvidence("背景图（打开素材库时的自动规范化）", beforeOrderedPaths, normalizeOrder);
                        var normalizeLabels = BuildPositionalLabelMaps(beforeOrderedPaths, normalizeOrder);
                        var normalizeSyncResult = await SyncStoryGlobalAssetIndexesWithProgressAsync(
                            assetLibrary,
                            "背景图",
                            "BGindex",
                            normalizeRemap,
                            normalizeLabels.OldLabels,
                            normalizeLabels.NewLabels,
                            normalizeOrder.Count);
                        if (normalizeSyncResult.ChangedCsvCount > 0)
                        {
                            AppendLog(LogKind.Info, $"背景图重新编号后已同步 {normalizeSyncResult.ChangedCsvCount} 个章节 CSV 的背景图索引。");
                        }
                    }
                }
                finally
                {
                    _isNormalizingBackgroundImages = false;
                }
            }

            await RefreshBackgroundImageCardsAsync(assetLibrary, GetAssetLibraryLoadToken(), forcePopulateCards);
        }

        private async void LoadMusicFiles(AssetLibraryInfo assetLibrary)
        {
            await ReloadMusicFilesAsync(assetLibrary);
        }

        private async Task ReloadMusicFilesAsync(AssetLibraryInfo assetLibrary)
        {
            await ReloadAudioFilesAsync(assetLibrary, AudioAssetKind.Music);
        }

        private async void LoadAmbientSoundFiles(AssetLibraryInfo assetLibrary)
        {
            await ReloadAudioFilesAsync(assetLibrary, AudioAssetKind.Ambient);
        }

        private async void LoadSoundEffectFiles(AssetLibraryInfo assetLibrary)
        {
            await ReloadAudioFilesAsync(assetLibrary, AudioAssetKind.SoundEffect);
        }

        private async Task ReloadAudioFilesAsync(AssetLibraryInfo assetLibrary, AudioAssetKind kind, bool forcePopulateCards = false)
        {
            var musicFolderPath = GetAudioFolderPath(assetLibrary, kind);
            Directory.CreateDirectory(musicFolderPath);

            if (!IsAudioNormalizing(kind))
            {
                SetAudioNormalizing(kind, true);
                try
                {
                    // 音频这条**不需要**在这里同步索引：它的重命名顺序就是 GetFilePaths
                    // （= 剧情索引认的那个列表），改名不动位置。
                    // 背景图那条不一样（它的排序把 jpg/webp 也算进去，而剧情的位置只数 png），
                    // 所以只有背景图这里必须配一次索引同步 —— 见上面 ReloadBackgroundImagesAsync。
                    await NormalizeAudioFilesAsync(kind, musicFolderPath);
                }
                finally
                {
                    SetAudioNormalizing(kind, false);
                }
            }

            await RefreshAudioCardsAsync(assetLibrary, kind, GetAssetLibraryLoadToken(), forcePopulateCards);
        }

        private async Task RefreshBackgroundImageCardsAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var backgroundFolderPath = GetBackgroundFolderPath(assetLibrary);
            var imagePaths = ImageSetService.GetFilePaths(backgroundFolderPath);

            BackgroundImagesGridView.Items.Clear();
            BackgroundImagesExpander.Header = $"背景图 [数量：{imagePaths.Count}]";
            AssetLibraryDetailStatusText.Text = $"背景图：{imagePaths.Count} 个文件 | {backgroundFolderPath}";
            if (forcePopulateCards || BackgroundImagesExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    BackgroundImagesGridView,
                    imagePaths,
                    CreateBackgroundImageCard,
                    cancellationToken,
                    batchSize: 8);
            }

            AppendLog(LogKind.Info, $"已加载背景图：{imagePaths.Count} 个文件。");
        }

        private void RefreshBackgroundImageCards(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(RefreshBackgroundImageCardsAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        /// <summary>
        /// 道具图片的卡片加载 —— 和背景图那段**逐行同构**，只换了文件夹、控件和调用点。
        ///
        /// 保持同构是刻意的：以后改"卡片怎么分批加载"这类逻辑，两处一起看清爽，
        /// 也不会出现"背景图修了、道具图片没修"这种分叉。
        /// </summary>
        private async Task RefreshPropImageCardsAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var propFolderPath = GetPropImageFolderPath(assetLibrary);
            var imagePaths = ImageSetService.GetFilePaths(propFolderPath);

            PropImagesGridView.Items.Clear();
            PropImagesExpander.Header = $"道具图片 [数量：{imagePaths.Count}]";
            if (forcePopulateCards || PropImagesExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    PropImagesGridView,
                    imagePaths,
                    CreatePropImageCard,
                    cancellationToken,
                    batchSize: 8);
            }

            AppendLog(LogKind.Info, $"已加载道具图片：{imagePaths.Count} 个文件。");
        }

        /// <summary>
        /// 一张道具图片的卡片。
        ///
        /// 点击 / 右键菜单走的都是**和背景图同一批方法** —— 它们现在按"图片自己所在的目录"
        /// 找分类，所以对两个分类都成立，不需要各写一份。
        ///
        /// ⚠️ 只有查看器的**名字**还叫 ShowBackgroundImageViewerPage，其实它已经是通用的了
        /// （显示、左右翻、改备注、替换、删除都按文件自己所在目录走）。
        /// 名字没跟着改是这一版刻意留的：改名会牵动好几处调用点，价值不大、风险不小。
        /// </summary>
        private GridViewItem CreatePropImageCard(string imagePath)
        {
            return AssetCardFactory.CreateCard(
                160,
                190,
                AssetCardContentFactory.CreateImageAssetCardContent(imagePath, 148, 148),
                imagePath,
                (_, _) =>
                {
                    PlaySelectionSound();
                    ShowBackgroundImageViewerPage(imagePath);
                },
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetBackgroundImageRemarkAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceBackgroundImageAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteBackgroundImageAsync(imagePath))));
        }

        private void RefreshPropImageCards(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(RefreshPropImageCardsAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        private void PropImagesExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RefreshPropImageCards(_currentAssetLibrary);
            }
        }

        private GridViewItem CreateBackgroundImageCard(string imagePath)
        {
            return AssetCardFactory.CreateCard(
                160,
                190,
                AssetCardContentFactory.CreateImageAssetCardContent(imagePath, 148, 148),
                imagePath,
                (_, _) =>
                {
                    PlaySelectionSound();
                    ShowBackgroundImageViewerPage(imagePath);
                },
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetBackgroundImageRemarkAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceBackgroundImageAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteBackgroundImageAsync(imagePath))));
        }

        private void RefreshMusicCards(AssetLibraryInfo assetLibrary)
        {
            RefreshAudioCards(assetLibrary, AudioAssetKind.Music);
        }

        private async Task RefreshAudioCardsAsync(AssetLibraryInfo assetLibrary, AudioAssetKind kind, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var musicFolderPath = GetAudioFolderPath(assetLibrary, kind);
            AudioAssetService.DeleteIgnoredSidecarFiles(kind, musicFolderPath);
            var musicPaths = AudioAssetService.GetFilePaths(musicFolderPath);
            var gridView = GetAudioGridView(kind);

            gridView.Items.Clear();
            switch (kind)
            {
                case AudioAssetKind.Music:
                    MusicExpander.Header = $"音乐 [数量：{musicPaths.Count}]";
                    break;
                case AudioAssetKind.Ambient:
                    AmbientSoundExpander.Header = $"环境音 [数量：{musicPaths.Count}]";
                    break;
                case AudioAssetKind.SoundEffect:
                    SoundEffectExpander.Header = $"特殊音效 [数量：{musicPaths.Count}]";
                    break;
            }

            if (forcePopulateCards || IsAudioExpanderExpanded(kind))
            {
                await AddGridViewItemsInBatchesAsync(
                    gridView,
                    musicPaths,
                    path => CreateAudioCard(kind, path),
                    cancellationToken);
            }

            AppendLog(LogKind.Info, $"已加载{AudioAssetService.GetDisplayName(kind)}：{musicPaths.Count} 个文件。");
        }

        private void RefreshAudioCards(AssetLibraryInfo assetLibrary, AudioAssetKind kind)
        {
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, kind, GetAssetLibraryLoadToken()));
        }

        private async Task LoadCharactersAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var characters = _characterWorkspaceService.GetCharactersByName(assetLibrary);

            CharacterGridView.Items.Clear();
            CharactersExpander.Header = $"立绘 [数量：{characters.Count}]";
            if (forcePopulateCards || CharactersExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    CharacterGridView,
                    characters,
                    CreateCharacterCard,
                    cancellationToken);
                CharacterGridView.Items.Add(CreateAddCharacterCard());
            }

            AppendLog(LogKind.Info, $"已加载角色卡：{characters.Count} 个。");
        }

        private void LoadCharacters(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(LoadCharactersAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        private async Task LoadFunctionsAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var linkedChapters = _projectWorkspaceService.GetProjects(_projectRootPath)
                .Where(project =>
                    string.Equals(project.AssetLibraryFolderName, assetLibrary.FolderName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(project.AssetLibraryName, assetLibrary.Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(_projectWorkspaceService.GetChapters)
                .ToList();
            var collectedCount = StoryFunctionService.CollectFunctionsFromChapters(
                assetLibrary,
                linkedChapters,
                _storyDocumentRepository,
                _jsonOptions);
            var functions = StoryFunctionService.ReadFunctions(assetLibrary, _jsonOptions);
            FunctionGridView.Items.Clear();
            FunctionExpander.Header = $"函数 [数量：{functions.Count}]";
            if (forcePopulateCards || FunctionExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    FunctionGridView,
                    functions,
                    CreateFunctionCard,
                    cancellationToken);
                FunctionGridView.Items.Add(CreateAddFunctionCard());
            }

            AppendLog(LogKind.Info, $"已加载函数卡：{functions.Count} 个。");
            if (collectedCount > 0)
            {
                AppendLog(LogKind.Info, $"已从关联项目收录函数主项：新增 {collectedCount} 个。");
            }
        }

        private void LoadFunctions(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(LoadFunctionsAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        private async void AddFunctionButton_Click(object sender, RoutedEventArgs e)
        {
            await AddFunctionAsync();
        }

        private async Task AddFunctionAsync()
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var input = await _functionDialogService.EditFunctionAsync(
                "新建函数",
                null,
                BuildSuggestedChoiceFunctionIndicator());
            if (input is null)
            {
                return;
            }

            var functions = StoryFunctionService.ReadFunctions(_currentAssetLibrary, _jsonOptions);
            if (functions.Any(function => string.Equals(function.Indicator, input.MainCode, StringComparison.OrdinalIgnoreCase)))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "主函数已存在", input.MainCode);
                return;
            }
            functions.Add(new FunctionEntry(
                Guid.NewGuid().ToString("N"),
                input.Name,
                input.MainCode,
                "函数",
                [],
                input.Description)
            {
                BranchMode = input.BranchMode,
                Branches = input.Branches
            });
            StoryFunctionService.WriteFunctions(_currentAssetLibrary, functions, _jsonOptions);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadFunctions(_currentAssetLibrary);
            RequestDelayedRefresh();
        }

        private async Task EditFunctionAsync(FunctionEntry function)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var input = await _functionDialogService.EditFunctionAsync(
                "修改函数",
                function,
                string.Empty);
            if (input is null)
            {
                return;
            }

            var existingFunctions = StoryFunctionService.ReadFunctions(_currentAssetLibrary, _jsonOptions);
            if (existingFunctions.Any(entry => entry.Id != function.Id &&
                                               string.Equals(entry.Indicator, input.MainCode, StringComparison.OrdinalIgnoreCase)))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "主函数已存在", input.MainCode);
                return;
            }
            var functions = existingFunctions
                .Select(entry => entry.Id == function.Id ? entry with
                {
                    Name = input.Name,
                    Indicator = input.MainCode,
                    Description = input.Description,
                    BranchMode = input.BranchMode,
                    Branches = input.Branches
                } : entry)
                .ToList();
            StoryFunctionService.WriteFunctions(_currentAssetLibrary, functions, _jsonOptions);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadFunctions(_currentAssetLibrary);
            RequestDelayedRefresh();
        }

        private async Task DeleteFunctionAsync(FunctionEntry function)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var confirmed = await ShowDeleteConfirmDialogAsync("删除函数", $"确定删除函数 {function.Name}（{function.Indicator}）吗？已经填入剧情 CSV 的 Custom 字段不会自动清空。");
            if (!confirmed)
            {
                return;
            }

            var functions = StoryFunctionService.ReadFunctions(_currentAssetLibrary, _jsonOptions)
                .Where(entry => entry.Id != function.Id)
                .ToList();
            StoryFunctionService.WriteFunctions(_currentAssetLibrary, functions, _jsonOptions);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadFunctions(_currentAssetLibrary);
            RequestDelayedRefresh();
        }

        private string BuildSuggestedChoiceFunctionIndicator()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                return string.Empty;
            }

            var prefix = BuildCurrentStoryChapterSectionChoicePrefix();
            var functions = _currentStoryAssetLibrary is null
                ? []
                : StoryFunctionService.ReadFunctions(_currentStoryAssetLibrary, _jsonOptions);
            return StoryFunctionService.BuildSuggestedChoiceIndicator(prefix, functions);
        }

        private static string RemoveProjectCodePrefix(string chapterCode, string? projectCode)
        {
            if (string.IsNullOrWhiteSpace(projectCode))
            {
                return chapterCode.Trim();
            }

            var prefix = $"{projectCode}-";
            return chapterCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? chapterCode[prefix.Length..]
                : chapterCode.Trim();
        }

        private async Task LoadCharacterFiltersAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var storedFilters = _characterFilterService.ReadStored(assetLibrary);
            var filters = CharacterFilterService.Normalize(storedFilters);
            CharacterFilterGridView.Items.Clear();
            CharacterFilterExpander.Header = $"角色滤镜 [数量：{filters.Count}]";
            if (forcePopulateCards || CharacterFilterExpander.IsExpanded)
            {
                var indexedFilters = filters
                    .Select((filter, index) => (filter, index))
                    .ToList();
                await AddGridViewItemsInBatchesAsync(
                    CharacterFilterGridView,
                    indexedFilters,
                    item => CreateCharacterFilterCard(item.filter, item.index),
                    cancellationToken);
                CharacterFilterGridView.Items.Add(CreateAddCharacterFilterCard());
            }

            AppendLog(LogKind.Info, $"已加载角色滤镜：{filters.Count} 个。");
            if (!_isRepairingCharacterFilters &&
                !_isReorderingCharacterFilters &&
                (filters.Count != storedFilters.Count || !filters.SequenceEqual(storedFilters)))
            {
                _ = RepairStoredCharacterFiltersAsync(assetLibrary, storedFilters, filters);
            }
        }

        private void LoadCharacterFilters(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(LoadCharacterFiltersAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        private void ApplyAssetLibraryMetadataToUi(AssetLibraryInfo assetLibrary)
        {
            _isApplyingAssetLibraryMetadata = true;
            try
            {
                AssetLibraryPortraitPreviewEnabledCheckBox.IsChecked = assetLibrary.IsPortraitPreviewEnabled;
                UpdateAssetLibraryDiskUsage(assetLibrary);
            }
            finally
            {
                _isApplyingAssetLibraryMetadata = false;
            }
        }

        private void UpdateAssetLibraryDiskUsage(AssetLibraryInfo assetLibrary)
        {
            AssetLibraryDiskUsageText.Text = $"当前占用：{FormatFileSize(CountDirectoryBytes(assetLibrary.Path))}";
        }

        private void AssetLibraryPortraitPreviewEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingAssetLibraryMetadata || _currentAssetLibrary is null)
            {
                return;
            }

            var isEnabled = AssetLibraryPortraitPreviewEnabledCheckBox.IsChecked == true;
            _currentAssetLibrary = _projectWorkspaceService.SetAssetLibraryPortraitPreviewEnabled(_currentAssetLibrary, isEnabled);
            AssetLibraryDetailStatusText.Text = isEnabled
                ? "已启用小预览；未设置的小预览会在虚幻同步前提示。"
                : "已关闭小预览；虚幻同步不会写入 DA_Portraits。";
            LoadCharacters(_currentAssetLibrary);
            ReloadCharacterDetailLayersPreservingScroll();
            AppendLog(LogKind.User, isEnabled ? "已启用素材库小预览。" : "已关闭素材库小预览。");
        }

        private async Task RepairStoredCharacterFiltersAsync(
            AssetLibraryInfo assetLibrary,
            IReadOnlyList<CharacterFilterEntry> oldFilters,
            IReadOnlyList<CharacterFilterEntry> newFilters)
        {
            if (_isRepairingCharacterFilters)
            {
                return;
            }

            _isRepairingCharacterFilters = true;
            try
            {
                var indexRemap = CharacterFilterService.BuildIndexRemap(oldFilters, newFilters);
                var oldLabels = oldFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
                var newLabels = newFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
                _characterFilterService.Write(assetLibrary, newFilters);

                AssetIndexSyncResult? syncResult = null;
                if (indexRemap.Count > 0)
                {
                    syncResult = await SyncStoryCharacterFilterIndexesWithProgressAsync(assetLibrary, indexRemap, oldLabels, newLabels, newFilters.Count);
                }

                TouchAssetLibraryLastEditedAt(assetLibrary);
                if (ReferenceEquals(_currentAssetLibrary, assetLibrary) ||
                    string.Equals(_currentAssetLibrary?.Path, assetLibrary.Path, StringComparison.OrdinalIgnoreCase))
                {
                    LoadCharacterFilters(assetLibrary);
                }

                RequestDelayedRefresh();
                if (syncResult?.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的角色滤镜索引。");
                }

                AppendLog(LogKind.User, "已修复角色滤镜索引：保留唯一 VFX00 空项。");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "修复角色滤镜索引失败。", ex);
            }
            finally
            {
                _isRepairingCharacterFilters = false;
            }
        }

        private List<CharacterFilterEntry> GetStoryCharacterFilters()
        {
            return _currentStoryAssetLibrary is null ? [] : _characterFilterService.Read(_currentStoryAssetLibrary);
        }

        private string? ResolveStoryCharacterFilterName(int index)
        {
            if (index <= 0)
            {
                return null;
            }

            var filters = GetStoryCharacterFilters();
            var resolvedIndex = ResolveStoryAssetIndex(index, filters.Count);
            return resolvedIndex is null ? null : CharacterFilterService.GetDisplayName(filters[resolvedIndex.Value], resolvedIndex.Value);
        }

        private async Task AddCharacterFilterAsync()
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var remark = await ShowCharacterFilterRemarkDialogAsync("新建滤镜", string.Empty);
            if (remark is null)
            {
                return;
            }

            var filters = _characterFilterService.Read(_currentAssetLibrary);
            filters.Add(new CharacterFilterEntry(Guid.NewGuid().ToString("N"), remark));
            _characterFilterService.Write(_currentAssetLibrary, CharacterFilterService.Normalize(filters));
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已新增角色滤镜：{remark}");
        }

        private async Task RenameCharacterFilterAsync(CharacterFilterEntry filter)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            if (CharacterFilterService.IsEmpty(filter))
            {
                return;
            }

            var remark = await ShowCharacterFilterRemarkDialogAsync("修改滤镜备注", filter.Remark);
            if (remark is null)
            {
                return;
            }

            var oldRemark = filter.Remark;
            var filters = _characterFilterService.Read(_currentAssetLibrary)
                .Select(entry => entry.Id == filter.Id ? entry with { Remark = remark } : entry)
                .ToList();
            _characterFilterService.Write(_currentAssetLibrary, CharacterFilterService.Normalize(filters));
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已修改角色滤镜：{oldRemark} -> {remark}");
        }

        private async Task DeleteCharacterFilterAsync(CharacterFilterEntry filter)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            if (CharacterFilterService.IsEmpty(filter))
            {
                return;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "删除滤镜",
                $"确定删除 {filter.Remark} 吗？引用它的剧情行会重置为 VFX00，后续滤镜索引会同步前移。",
                "删除",
                "取消"));
            if (!confirmed)
            {
                return;
            }

            var oldFilters = _characterFilterService.Read(_currentAssetLibrary);
            var filters = CharacterFilterService.Normalize(oldFilters
                .Where(entry => entry.Id != filter.Id)
                .ToList());
            var indexRemap = CharacterFilterService.BuildIndexRemap(oldFilters, filters);
            var oldLabels = oldFilters.Select((entry, index) => (entry, index)).ToDictionary(item => item.index, item => item.entry.Remark);
            var newLabels = filters.Select((entry, index) => (entry, index)).ToDictionary(item => item.index, item => item.entry.Remark);
            _characterFilterService.Write(_currentAssetLibrary, filters);
            var syncResult = await SyncStoryCharacterFilterIndexesWithProgressAsync(_currentAssetLibrary, indexRemap, oldLabels, newLabels, filters.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的角色滤镜索引。");
            }

            AppendLog(LogKind.User, $"已删除角色滤镜：{filter.Remark}");
        }

        private async Task<string?> ShowCharacterFilterRemarkDialogAsync(string title, string currentRemark)
        {
            var input = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                title,
                "备注",
                currentRemark,
                "例如：冷色调（下雨）"));
            if (input is null)
            {
                return null;
            }

            var remark = SanitizeRemark(input);
            return string.IsNullOrWhiteSpace(remark) ? null : remark;
        }

        private GridViewItem CreateAddCharacterCard()
        {
            return AssetCardFactory.CreateCard(
                150,
                220,
                AssetCardContentFactory.CreateAddCharacterCardContent(),
                tappedHandler: async (_, _) => await CreateCharacterAsync());
        }

        private GridViewItem CreateCharacterCard(CharacterInfo character)
        {
            var content = AssetCardContentFactory.CreateCharacterCardContent(
                character.Code,
                character.Name,
                character.ColorHex);
            var warningMessage = GetCharacterPortraitPreviewWarningMessage(character);
            var cardContent = warningMessage is null
                ? content
                : CreateWarningBadgeOverlay(content, warningMessage);

            return AssetCardFactory.CreateCard(
                150,
                220,
                cardContent,
                character,
                (_, _) =>
                {
                    PlaySelectionSound();
                    ShowCharacterDetailPage(character);
                },
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("重命名", async (_, _) => await RenameCharacterAsync(character))));
        }

        private GridViewItem CreateMusicCard(string musicPath)
        {
            return CreateAudioCard(AudioAssetKind.Music, musicPath);
        }

        private string? GetCharacterPortraitPreviewWarningMessage(CharacterInfo character)
        {
            if (_currentAssetLibrary?.IsPortraitPreviewEnabled != true)
            {
                return null;
            }

            var missing = _characterLayerAssetService.GetMissingPortraitPreviewLayerNames(character);
            return missing.Count == 0
                ? null
                : $"{character.Name} 有 {missing.Count} 个立绘素材还没有设置小预览：{string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? "..." : string.Empty)}";
        }

        private string? GetCharacterLayerPortraitPreviewWarningMessage(string imagePath, CharacterLayerKind layerKind)
        {
            if (_currentAssetLibrary?.IsPortraitPreviewEnabled != true ||
                _currentCharacter is null ||
                layerKind is not (CharacterLayerKind.Cloth or CharacterLayerKind.Face or CharacterLayerKind.Adorn))
            {
                return null;
            }

            return _characterLayerAssetService.ResolvePortraitPreviewPath(_currentCharacter, Path.GetFileName(imagePath)) is null
                ? $"{GetCharacterLayerDisplayName(layerKind)} {Path.GetFileNameWithoutExtension(imagePath)} 还没有设置小预览。右键选择“设置预览”后，同步会写入 DA_Portraits。"
                : null;
        }

        private UIElement CreateWarningBadgeOverlay(UIElement content, string message)
        {
            var badge = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Gold),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkGoldenrod),
                BorderThickness = new Thickness(1),
                Content = new FontIcon
                {
                    Glyph = "\uE7BA",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black)
                }
            };
            ToolTipService.SetToolTip(badge, "缺少小预览");
            badge.Tapped += (_, args) =>
            {
                args.Handled = true;
            };
            badge.PointerPressed += (_, args) =>
            {
                args.Handled = true;
            };
            badge.Click += async (_, args) =>
            {
                PlaySelectionSound();
                await ShowPortraitPreviewWarningAsync(message);
            };

            return new Grid
            {
                Children =
                {
                    content,
                    badge
                }
            };
        }

        private async Task ShowPortraitPreviewWarningAsync(string message)
        {
            var targetInfoBar = CharacterDetailPage.Visibility == Visibility.Visible
                ? CharacterDetailInfoBar
                : null;
            if (targetInfoBar is not null)
            {
                targetInfoBar.Severity = InfoBarSeverity.Warning;
                targetInfoBar.Title = "缺少小预览";
                targetInfoBar.Message = message;
                targetInfoBar.IsOpen = true;
            }
            else
            {
                AssetLibraryDetailStatusText.Text = message;
            }

            AppendLog(LogKind.Warning, message);
            await _dialogService.ShowAsync(new DialogRequest(
                "缺少小预览",
                message,
                PrimaryButtonText: string.Empty,
                CloseButtonText: "知道了",
                PrimarySound: DialogSoundIntent.None,
                CloseSound: DialogSoundIntent.Selection));
        }

        private GridViewItem CreateFunctionCard(FunctionEntry function)
        {
            var choiceNotes = function.ChoiceNotes ?? [];
            return AssetCardFactory.CreateCard(
                260,
                126,
                AssetCardContentFactory.CreateFunctionCardContent(
                    function.Name,
                    StoryFunctionService.BuildFunctionCatalogSummary(function),
                    string.IsNullOrWhiteSpace(function.Description) ? "暂无介绍" : function.Description),
                function,
                async (_, _) => await EditFunctionAsync(function),
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("修改函数", async (_, _) => await EditFunctionAsync(function)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteFunctionAsync(function))),
                stretchContent: true,
                toolTip: choiceNotes.Count > 0
                    ? string.Join(Environment.NewLine, choiceNotes.Select((note, index) => $"{index + 1}. {note}"))
                    : null);
        }

        private GridViewItem CreateAddFunctionCard()
        {
            return AssetCardFactory.CreateCard(
                260,
                126,
                AssetCardContentFactory.CreateAddTextCardContent("+"),
                stretchContent: true,
                tappedHandler: async (_, _) => await AddFunctionAsync());
        }

        private GridViewItem CreateCharacterFilterCard(CharacterFilterEntry filter, int index)
        {
            var canEdit = !CharacterFilterService.IsEmpty(filter);
            return AssetCardFactory.CreateCard(
                220,
                92,
                AssetCardContentFactory.CreateCharacterFilterCardContent($"VFX{index:00}", filter.Remark),
                filter,
                contextFlyout: canEdit
                    ? GridViewItemFactory.CreateMenu(
                        GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteCharacterFilterAsync(filter)))
                    : null,
                stretchContent: true);
        }

        private GridViewItem CreateAddCharacterFilterCard()
        {
            return AssetCardFactory.CreateCard(
                220,
                64,
                AssetCardContentFactory.CreateAddCharacterFilterCardContent(),
                tappedHandler: async (_, _) => await AddCharacterFilterAsync(),
                stretchContent: true);
        }

        private GridViewItem CreateAudioCard(AudioAssetKind kind, string musicPath)
        {
            return AssetCardFactory.CreateCard(
                220,
                92,
                AssetCardContentFactory.CreateIconAssetCardContent(
                    Symbol.Audio,
                    Path.GetFileNameWithoutExtension(musicPath)),
                musicPath,
                (_, _) =>
                {
                    PlaySelectionSound();
                    ShowMusicPlayerPage(musicPath, kind);
                },
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetAudioRemarkAsync(kind, musicPath)),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceAudioAsync(kind, musicPath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteAudioAsync(kind, musicPath))));
        }

    }
}
