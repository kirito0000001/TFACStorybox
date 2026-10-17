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
            // PropImagesExpander.Expanding += PropImagesExpander_Expanded;  ← 旧网格的展开器入口，已退休
            // MusicExpander.Expanding += MusicExpander_Expanded;  ← 旧网格的展开器入口，已退休
            // AmbientSoundExpander.Expanding += AmbientSoundExpander_Expanded;  ← 旧网格的展开器入口，已退休
            // SoundEffectExpander.Expanding += SoundEffectExpander_Expanded;  ← 旧网格的展开器入口，已退休
            // CharactersExpander.Expanding += CharactersExpander_Expanded;  ← 旧网格的展开器入口，已退休
            // FunctionExpander.Expanding += FunctionExpander_Expanded;  ← 旧网格的展开器入口，已退休
            // CharacterFilterExpander.Expanding += CharacterFilterExpander_Expanded;  ← 旧网格的展开器入口，已退休
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
            // 旧背景网格的填充已退休（打开素材库只铺分类卡，点分类时才填素材卡）。
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Music, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Ambient, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.SoundEffect, cancellationToken));
            // 打开素材库时不再刷新旧函数网格（浏览器由分类卡点击时填充，
            // 函数的"从关联项目收录"也在打开函数分类时做）。
            // 打开素材库时不再刷新旧角色网格（浏览器由分类卡点击时填充）。
            // 打开素材库时不再刷新旧滤镜网格（浏览器由分类卡点击时填充）。
        }

        private void BackgroundImagesExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadBackgroundImagesAsync(_currentAssetLibrary, true));
            }
        }

        // MusicExpander_Expanded / AmbientSoundExpander_Expanded / SoundEffectExpander_Expanded 已删：
        // 旧网格的三个展开器入口。新骨架的音频三类在点分类卡时填充。
        // ⚠️ ReloadAudioFilesAsync 与三个 Load*Files 包装**保留** —— 那是打开素材库时的加载链
        //（建目录 + "已加载音乐：N 个文件"这类日志），与旧网格无关。

        // CharactersExpander_Expanded 已删：旧网格的展开器入口（注册上一轮已注释）。

        // FunctionExpander_Expanded 已删：旧网格的展开器入口（注册已注释），随旧加载器退休。

        // CharacterFilterExpander_Expanded 已删：旧网格的展开器入口（注册上一轮已注释）。

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

            // 这里原来还有一步"填旧背景图网格"（RefreshBackgroundImageCardsAsync）——
            // 旧网格按文档第二节退休，已删除。
            // ⚠️ 上面那段**自动规范化与 BGindex 同步必须留**：打开素材库时给整个目录重新编号
            // 并把旧位置映射到新位置（2026-10-16"换序后背景错位"就是它修的，文档专门标注过）。
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

        // 背景图旧网格的填充方法（RefreshBackgroundImageCardsAsync）已删：
        // 通读确认它只做"清网格 / 设 Expander 标题 / 设状态条 / 分批加卡片 / 记一行日志"，
        // 没有任何规范化或索引同步（那段在 ReloadBackgroundImagesAsync 里，必须留）。
        // 日志那行已挪到新骨架的 FillAssetBrowserWithBackgroundsAsync。

        // 同步包装方法 RefreshBackgroundImageCards 已删：它只是转调 Async 那个，
        // 而旧背景网格不再需要填充（打开素材库只铺分类卡）。

        /// <summary>
        /// 道具图片的卡片加载 —— 和背景图那段**逐行同构**，只换了文件夹、控件和调用点。
        ///
        /// 保持同构是刻意的：以后改"卡片怎么分批加载"这类逻辑，两处一起看清爽，
        /// 也不会出现"背景图修了、道具图片没修"这种分叉。
        /// </summary>
        // RefreshPropImageCardsAsync 已删：通读确认它只做旧网格填充（清网格、设 Expander 标题、
        // 分批加卡片、记一行日志），没有规范化或同步。日志那行已挪到新骨架的
        // FillAssetBrowserWithPropImagesAsync。与背景图那条逐行同构 —— 删法也一样。

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
        // CreatePropImageCard 已删：160x190 的旧道具卡工厂，只被上面的旧填充引用；
        // 新骨架的道具卡是 CardShell.CreateImageCard（190x220 带图卡）。

        // RefreshPropImageCards 与 PropImagesExpander_Expanded 已删：旧网格的同步包装与展开器入口，
        // 两者互为闭环（包装只被那个处理器调用），外面没人用。

        // CreateBackgroundImageCard 已删：旧背景图卡工厂（160x190 的 AssetCardFactory 版本），
        // 旧网格退休后引用数为 0。新骨架的背景图卡在 CardShell.CreateBackgroundCard。

        // RefreshMusicCards 已删：它只是转调 RefreshAudioCards(音乐)。

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

        // RefreshAudioCards 已删：旧音频网格的同步包装。
        // RefreshAudioCardsAsync 本体暂留（尾部还没读全，已无调用者，下次收掉）。

        // LoadCharactersAsync 与 LoadCharacters 已删：旧角色网格的填充与其同步包装。
        // 调用点（创建 / 重命名角色后、打开素材库时）都已改走统一重铺或删除。
        // 日志"已加载角色卡：N 个"暂未搬 —— 下次连同两个旧角色卡工厂一起收尾。

        // LoadFunctionsAsync 已删：函数的来源与"从关联项目收录函数主项"都搬到了新骨架的
        // FillAssetBrowserWithFunctions（见 f2576d0），这里只剩旧网格填充，所以退休。

        // LoadFunctions 已删：它只是转调 LoadFunctionsAsync，而那个已随旧网格退休。

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
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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

        // LoadCharacterFiltersAsync 与 LoadCharacterFilters 已删：旧滤镜网格的填充与同步包装。
        // 日志「已加载角色滤镜：N 个」与末尾的「回写修复」都已搬到新骨架的
        // FillAssetBrowserWithCharacterFilters（见 b90c900）。

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
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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
                    RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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
            RunAssetLibraryLoad(RefreshCurrentAssetCategoryAsync());
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

        // CreateAddCharacterCard 与 CreateCharacterCard 已删：旧立绘网格的卡片工厂
        //（150×220 的 AssetCardFactory 版本，调用者 LoadCharactersAsync 已随旧网格删除）。
        // 新骨架的立绘卡是 CardShell 扁平小卡（中文名 + 英文代号，点开进角色详情页）。

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

        /// <summary>
        /// 函数卡 —— **第一批换成新卡片外壳的样板**（用户 2026-10-17：先做函数卡看观感）。
        ///
        /// 口径照隔壁零境工具箱：卡片就是 Button 自己、280×76 扁平、圆角 8、1px 主题描边、
        /// **不设底色**（悬停/按下交给系统状态色）。原来那张 260×126 的卡里挤了三行字
        /// （名称 / 目录摘要 / 介绍），正是"样式很花"的来源之一 —— 现在卡面只留
        /// 一行标题 + 一行副标题，介绍和选项备注都挪进 ToolTip。
        /// </summary>
        private Button CreateFunctionCard(FunctionEntry function)
        {
            var choiceNotes = function.ChoiceNotes ?? [];
            var description = string.IsNullOrWhiteSpace(function.Description) ? "暂无介绍" : function.Description;

            var content = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    CardShell.CreateTitle(function.Name),
                    CardShell.CreateSubtitle(StoryFunctionService.BuildFunctionCatalogSummary(function))
                }
            };

            var card = CardShell.CreateFlatCard(function, content);
            var toolTipLines = new List<string> { description };
            toolTipLines.AddRange(choiceNotes.Select((note, index) => $"{index + 1}. {note}"));
            ToolTipService.SetToolTip(card, string.Join(Environment.NewLine, toolTipLines));

            card.Click += async (_, _) => await EditFunctionAsync(function);
            card.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("修改函数", async (_, _) => await EditFunctionAsync(function)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteFunctionAsync(function)));
            return card;
        }

        private Button CreateAddFunctionCard()
        {
            // 照零境那张新建卡**抄全**（用户 2026-10-17 指着零境的卡确认）：
            // 一枚居中加号、**不带文字**，用途靠 ToolTip 说 —— 零境那边也是
            // ToolTipService.ToolTip 写「新建文件夹」，卡面只有一个 +。
            var content = new FontIcon
            {
                FontSize = 20,
                Glyph = "\uE710",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var card = CardShell.CreateFlatCard(null, content);
            card.HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTipService.SetToolTip(card, "新建函数");
            card.Click += async (_, _) => await AddFunctionAsync();
            return card;
        }

        private Button CreateCharacterFilterCard(CharacterFilterEntry filter, int index)
        {
            var canEdit = !CharacterFilterService.IsEmpty(filter);
            // 换到新卡片外壳（用户 2026-10-17 看到的那排"白框"就是这张旧卡：
            // 220x92 的旧工厂 + 自绘内容）。现在 = 扁平卡 280x76：VFX 编号 + 备注。
            // Tag 仍然是 filter 本身 —— 旧拖拽处理器与新的「移动素材」都按它认卡片。
            var content = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    CardShell.CreateTitle($"VFX{index:00}"),
                    CardShell.CreateSubtitle(string.IsNullOrWhiteSpace(filter.Remark) ? "（空）" : filter.Remark)
                }
            };

            var card = CardShell.CreateFlatCard(filter, content);
            card.ContextFlyout = canEdit
                ? GridViewItemFactory.CreateMenu(
                    // 空滤镜（VFX00 固定为空）不给「移动素材」：把第 0 位挪走会把所有索引顶掉。
                    GridViewItemFactory.CreateMenuItem("移动素材", (_, _) => BeginMoveBackgroundImage(filter, card)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteCharacterFilterAsync(filter)))
                : null;
            return card;
        }

        private Button CreateAddCharacterFilterCard()
        {
            // 和新建函数卡同一条口径（用户 2026-10-17 确认过）：一枚居中加号、不带文字、用途走 ToolTip。
            var card = CardShell.CreateFlatCard(null, new FontIcon
            {
                FontSize = 20,
                Glyph = "\uE710",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            card.HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTipService.SetToolTip(card, "新建角色滤镜");
            card.Click += async (_, _) => await AddCharacterFilterAsync();
            return card;
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
