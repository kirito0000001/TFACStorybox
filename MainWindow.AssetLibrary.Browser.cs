using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools;

/// <summary>
/// 素材页新骨架的**第一层**：分类卡列表（照隔壁零境那张"一个库一张卡"的页）。
///
/// 用户 2026-10-17：素材页骨架整体换成零境那套 ——
///   第一层：页头（页名 + 说明）+ 素材库元数据 + **分类卡**（这张表就是分类卡的数据）；
///   第二层：点进某个分类 → Controls/AssetBrowser（路径栏 + 卡片网格）。
/// 旧的 Expander 那一套先**隐藏不删**，方便对照与回退；叶子层按分类一个一个迁
/// （背景图 → 音频三类 → 道具/函数/滤镜 → 立绘两级）。
///
/// 计数只填**能立刻核实的**几类（背景图 / 音频三类 / 立绘）；函数、角色滤镜、道具图片
/// 的计数等它们各自那一层迁移时再接（别在这里凭记忆写访问器）。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>当前正开着的分类 key —— 改备注 / 替换 / 删除 / 导入那些功能路径靠它决定重铺谁。
    /// ⚠️ **回到分类层时不再清空**（原来是清空的 ✗）：清空后重铺就变成 no-op，
    /// 于是"改完备注/移动完素材后卡片没刷新"（用户 2026-10-17 反馈）。现在它表示
    /// "最近打开过的分类"，是否真的在分类里由下面的 _isAssetCategoryLayerOpen 决定。</summary>
    private string? _currentAssetCategoryKey;

    /// <summary>true = 现在停在分类层（铺分类卡）；false = 已进到某个分类里（铺素材卡）。
    /// 重铺时按这个决定铺哪一层 —— 这样在分类层也能刷新（计数会跟着变）。</summary>
    private bool _isAssetCategoryLayerOpen = true;

    /// <summary>一个分类卡：key 用来分发到各自的叶子层，count 是卡上的副标题。</summary>
    private sealed record AssetCategoryEntry(string Key, string Title, string CountText);

    private List<AssetCategoryEntry> BuildAssetCategoryEntries(AssetLibraryInfo assetLibrary)
    {
        var entries = new List<AssetCategoryEntry>
        {
            new("backgrounds", "背景图",
                $"{ImageSetService.GetFilePaths(GetBackgroundFolderPath(assetLibrary)).Count} 张"),
            new("props", "道具图片", "待迁移"),
            new("portraits", "立绘",
                $"{_characterWorkspaceService.GetCharactersByFolderName(assetLibrary).Count} 个角色"),
            new("music", "音乐",
                $"{AudioAssetService.GetFilePaths(GetMusicFolderPath(assetLibrary)).Count} 首"),
            new("ambient", "环境音",
                $"{AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(assetLibrary)).Count} 个"),
            new("soundeffects", "特殊音效",
                $"{AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(assetLibrary)).Count} 个"),
            new("functions", "函数", "待迁移"),
            new("filters", "角色滤镜", "待迁移")
        };

        return entries;
    }

    /// <summary>把分类卡造出来（280×76 扁平卡：标题 + 数量，照零境的库卡）。</summary>
    /// <summary>
    /// 角色主题色。取不到就返回 null（继承主题前景色），
    /// **绝不**塞一个默认灰 —— 那会在夜间模式下变成看不清的深色。
    /// </summary>
    private static Windows.UI.Color? ResolveCharacterTitleColor(CharacterInfo character)
    {
        if (string.IsNullOrWhiteSpace(character.ColorHex))
        {
            return null;
        }

        var hex = ColorUtility.NormalizeColorHex(character.ColorHex);
        if (hex.Length != 7)
        {
            return null;
        }

        try
        {
            return Windows.UI.Color.FromArgb(
                255,
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16));
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            return null;
        }
    }

    private List<object> BuildAssetCategoryCards(AssetLibraryInfo assetLibrary)
    {
        var cards = new List<object>();
        foreach (var entry in BuildAssetCategoryEntries(assetLibrary))
        {
            var content = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    CardShell.CreateTitle(entry.Title),
                    CardShell.CreateSubtitle(entry.CountText)
                }
            };

            var card = CardShell.CreateFlatCard(entry.Key, content);
            var key = entry.Key;
            card.Click += async (_, _) => await OpenAssetCategoryAsync(key);
            cards.Add(card);
        }

        return cards;
    }

    /// <summary>
    /// 点分类卡 → 切到第二层。**目前只有背景图迁好了**，其余分类先给一句"还没迁移"的提示，
    /// 这样第一层可以先立起来看观感，而不会假装它们能用。
    /// </summary>
    private async Task OpenAssetCategoryAsync(string key)
    {
        if (_currentAssetLibrary is null)
        {
            return;
        }

        // 记下"现在开着哪个分类"：改备注 / 替换 / 删除 / 导入那些功能路径靠它决定重铺谁。
        // （这个 key 在回到分类层时**不清空**，只把下面的层标志翻回分类层。）
        _currentAssetCategoryKey = key;
        _isAssetCategoryLayerOpen = false;

        switch (key)
        {
            case "backgrounds" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                await FillAssetBrowserWithBackgroundsAsync(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "music" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                FillAssetBrowserWithAudio(AssetBrowserHost, AudioAssetKind.Music, _currentAssetLibrary);
                break;
            case "ambient" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                FillAssetBrowserWithAudio(AssetBrowserHost, AudioAssetKind.Ambient, _currentAssetLibrary);
                break;
            case "soundeffects" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                FillAssetBrowserWithAudio(AssetBrowserHost, AudioAssetKind.SoundEffect, _currentAssetLibrary);
                break;
            case "props" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                await FillAssetBrowserWithPropImagesAsync(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "functions" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                FillAssetBrowserWithFunctions(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "filters" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                FillAssetBrowserWithCharacterFilters(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "portraits" when AssetBrowserHost is not null:
                ShowAssetBrowserLayer();
                FillAssetBrowserWithCharacters(AssetBrowserHost, _currentAssetLibrary);
                break;
            default:
                AssetLibraryStatusText.Text = "这个分类还没迁移到新骨架。";
                break;
        }

        // 前进一层：整片内容从右侧滑入。放在 switch 之后 ——
        // 在填充之前播的话，动画跑的是空卡片。
        PlayLayerSlide(AssetBrowserHost, forward: true);
    }

    /// <summary>
    /// 重铺"当前正开着的那个分类"的卡片。
    ///
    /// 为什么需要它：改备注 / 替换素材 / 删除 / 导入这些**功能路径**（共 12 处）
    /// 以前是重铺旧网格的；旧网格退休后它们得重铺新骨架，但它们只知道文件路径或音频 kind，
    /// 不知道用户此刻在哪个分类 —— 所以按 key 在这里统一分发，各处只调这一个方法。
    /// （返回分类层时 key 会被清空，这时什么都不用做。）
    /// </summary>
    private async Task RefreshCurrentAssetCategoryAsync()
    {
        if (AssetBrowserHost is null || _currentAssetLibrary is null)
        {
            return;
        }

        // 停在分类层时重铺分类卡（计数会跟着变）；进在某个分类里时重铺那个分类的素材卡。
        // 这样"改完备注 / 移动完素材后没刷新"就不会再发生（原来 key 被清空 → 整个重铺 no-op）。
        if (_isAssetCategoryLayerOpen || _currentAssetCategoryKey is null)
        {
            FillAssetBrowserWithCategories(_currentAssetLibrary);
            return;
        }

        switch (_currentAssetCategoryKey)
        {
            case "backgrounds":
                await FillAssetBrowserWithBackgroundsAsync(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "props":
                await FillAssetBrowserWithPropImagesAsync(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "portraits":
                FillAssetBrowserWithCharacters(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "music":
                FillAssetBrowserWithAudio(AssetBrowserHost, AudioAssetKind.Music, _currentAssetLibrary);
                break;
            case "ambient":
                FillAssetBrowserWithAudio(AssetBrowserHost, AudioAssetKind.Ambient, _currentAssetLibrary);
                break;
            case "soundeffects":
                FillAssetBrowserWithAudio(AssetBrowserHost, AudioAssetKind.SoundEffect, _currentAssetLibrary);
                break;
            case "functions":
                FillAssetBrowserWithFunctions(AssetBrowserHost, _currentAssetLibrary);
                break;
            case "filters":
                FillAssetBrowserWithCharacterFilters(AssetBrowserHost, _currentAssetLibrary);
                break;
        }
    }

    /// <summary>
    /// 立绘分类：**角色小卡**（扁平卡 280×76：中文名 + 英文代号）。
    ///
    /// 用户 2026-10-17 定的口径："也做成小卡片，只需要显示角色中文名字和英文代号就行了，
    /// 点开就是之前的详情，很容易复用" —— 所以这里只做入口：
    /// 点击直接调 <c>ShowCharacterDetailPage</c>，图层管理 / 小预览 / 重命名全在那边，
    /// 一行都不用重写。
    ///
    /// 缺小预览的角色：旧卡是**角标覆盖**在卡上，扁平卡塞角标会花 —— 改成放进 ToolTip。
    /// 立绘没有排序需求（角色顺序不是剧情索引），所以**不需要**「移动素材」。
    /// </summary>
    private void FillAssetBrowserWithCharacters(Controls.AssetBrowser browser, AssetLibraryInfo assetLibrary)
    {
        var folderPath = GetCharacterFolderPath(assetLibrary);
        SetAssetLibraryPath(folderPath);
        SetAssetLibraryUpEnabled(true);
        browser.SetCardsVisible(true);
        // 从角色详情退回来时要重新露出卡片区（详情是盖在它上面的）。
        browser.SetCardsVisible(true);

        // ⚠️ 顺序要和旧界面一致：旧立绘网格用的是 GetCharactersByName
        // （我一开始写成 GetCharactersByFolderName，两者顺序可能不同 → 用户会看到"立绘顺序变了"）。
        var characters = _characterWorkspaceService.GetCharactersByName(assetLibrary);
        var cards = new List<object>();
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            var content = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    // 只给名字染色（用户 2026-11-06）：整卡染色会盖掉卡片自己的描边与悬停态，
                    // 一行彩色文字已经足够把角色区分开。
                    CardShell.CreateTitle(character.Name, ResolveCharacterTitleColor(character)),
                    CardShell.CreateSubtitle(character.Code)
                }
            };

            var card = CardShell.CreateFlatCard(character, content);
            var warning = GetCharacterPortraitPreviewWarningMessage(character);
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(
                card,
                warning is null
                    ? $"{character.Name}（{character.Code}）"
                    : $"{character.Name}（{character.Code}）{Environment.NewLine}{warning}");
            card.Click += (_, _) =>
            {
                PlaySelectionSound();
                ShowCharacterDetailPage(character);
            };
            card.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("重命名", async (_, _) => await RenameCharacterAsync(character)));
            cards.Add(card);
        }

        browser.SetCards(cards);
    }

    /// <summary>
    /// 角色滤镜铺进浏览器：卡片直接复用 <c>CreateCharacterFilterCard</c>（VFX 编号 + 备注）
    /// 与 <c>CreateAddCharacterFilterCard</c>（一枚居中加号）。
    ///
    /// 列表来源与旧界面**完全一致**：<c>ReadStored</c> → <c>CharacterFilterService.Normalize</c>，
    /// 下标就是 VFX 序号（0 起；VFX00 固定为空，和旧界面同一套口径）。
    ///
    /// ⚠️ 滤镜**有顺序**（顺序就是 VFX 索引），排序要同步剧情里的 VFX 列 ——
    /// 这一步还没接（要先读旧拖拽处理器里标签映射怎么算的），所以暂时没有「移动素材」。
    /// </summary>
    private void FillAssetBrowserWithCharacterFilters(Controls.AssetBrowser browser, AssetLibraryInfo assetLibrary)
    {
        var folderPath = GetCharacterFilterFolderPath(assetLibrary);
        SetAssetLibraryPath(folderPath);
        SetAssetLibraryUpEnabled(true);
        browser.SetCardsVisible(true);
        // 「移动素材」的两个零件：滤镜的条目身份就是 CharacterFilterEntry（卡片 Tag 也是它），
        // 落地走 ApplyCharacterFilterOrderAsync（写清单 + 同步所有章节的 VFX 索引）。
        ConfigureMoveAsset(
            () => CharacterFilterService.Normalize(_characterFilterService.ReadStored(assetLibrary)).Cast<object>().ToList(),
            items => ApplyCharacterFilterOrderAsync(items.Cast<CharacterFilterEntry>().ToList()));

        var storedFilters = _characterFilterService.ReadStored(assetLibrary);
        var filters = CharacterFilterService.Normalize(storedFilters);
        // 旧加载器末尾藏着一步"回写修复"：规范化若改动过内容（数量或顺序不同），就把清单写回。
        // 这是非显示行为，删旧网格时不能连它一起丢 —— 原样搬过来（含两个守卫判断）。
        if (!_isRepairingCharacterFilters &&
            !_isReorderingCharacterFilters &&
            (filters.Count != storedFilters.Count || !filters.SequenceEqual(storedFilters)))
        {
            _ = RepairStoredCharacterFiltersAsync(assetLibrary, storedFilters, filters);
        }

        var cards = new List<object>();
        // 不用 LINQ：这个文件没有 using System.Linq（用下标循环，顺便和旧界面的
        // Select((filter, index) => (filter, index)) 是同一个口径）。
        for (var index = 0; index < filters.Count; index++)
        {
            var filterCard = CreateCharacterFilterCard(filters[index], index);
            // 滤镜卡自己**没有任何点击处理**（查过），所以挂"移动模式点击"不会双重触发：
            // 不在移动模式时这里什么都不做，右键菜单与删除照旧。
            filterCard.Tapped += async (_, _) =>
            {
                if (filterCard.Tag is { } identity)
                {
                    await TryCompleteMoveBackgroundImageAsync(identity);
                }
            };
            filterCard.PointerMoved += Card_PointerMoved;
            filterCard.ContextRequested += Card_ContextRequested;
            cards.Add(filterCard);
        }

        cards.Add(CreateAddCharacterFilterCard());
        browser.SetCards(cards);
    }

    /// <summary>
    /// 函数铺进浏览器：卡片**直接复用**已经迁到新样式的
    /// <c>CreateFunctionCard</c>（280×76 扁平卡：名称 + 目录摘要，介绍与选项备注在 ToolTip 里）
    /// 与 <c>CreateAddFunctionCard</c>（一枚居中加号、用途走 ToolTip）。
    ///
    /// 函数**不参与排序**（旧界面的说明就写着"函数卡不参与排序"），
    /// 所以这一类**不需要**「移动素材」。
    /// </summary>
    private void FillAssetBrowserWithFunctions(Controls.AssetBrowser browser, AssetLibraryInfo assetLibrary)
    {
        var folderPath = GetFunctionFolderPath(assetLibrary);
        SetAssetLibraryPath(folderPath);
        SetAssetLibraryUpEnabled(true);
        browser.SetCardsVisible(true);

        // ⚠️ 函数的来源是**素材库自己的函数目录**（StoryFunctionService.ReadFunctions），
        // 不是剧情侧那份（GetStoryFunctions 读的是 _currentStoryAssetLibrary）——
        // 我一开始写成了后者，那样在素材页里很可能一张函数卡都不显示。
        // 顺带把旧加载器里的"从关联项目收录函数主项"搬过来：那是**数据初始化**，不是显示。
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

        var cards = new List<object>();
        for (var index = 0; index < functions.Count; index++)
        {
            cards.Add(CreateFunctionCard(functions[index]));
        }

        cards.Add(CreateAddFunctionCard());
        browser.SetCards(cards);

        AppendLog(LogKind.Info, $"已加载函数卡：{functions.Count} 个。");
        if (collectedCount > 0)
        {
            AppendLog(LogKind.Info, $"已从关联项目收录函数主项：新增 {collectedCount} 个。");
        }
    }

    /// <summary>
    /// 道具图片的"顺序落地"：**只按新顺序重命名**，没有同步步骤 ——
    /// 道具图片不进剧情 CSV、编号没有外部含义（ImageSetService.GetAssetIndex 的注释写明了），
    /// 排序纯属素材库内部顺序。前缀用 <c>ImageSetPrefix.Prop</c>，png 转换复用背景图那支。
    /// </summary>
    private async Task ApplyPropImageOrderAsync(IReadOnlyList<string> orderedPaths)
    {
        if (_currentAssetLibrary is null || orderedPaths.Count == 0)
        {
            return;
        }

        var folderPath = GetPropImageFolderPath(_currentAssetLibrary);
        var beforeOrderedPaths = ImageSetService.GetFilePaths(folderPath);
        LogAssetIndexRemapEvidence("道具图片", beforeOrderedPaths, orderedPaths);

        await _imageSetService.NormalizeFilesAsync(
            folderPath,
            ImportBackgroundImageAsPngAsync,
            orderedPaths,
            ImageSetPrefix.Prop);

        TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
        // 只重铺新骨架那一份（另外三个排序方法上一轮都清了，道具这个当时漏了）。
        if (AssetBrowserHost is not null)
        {
            await FillAssetBrowserWithPropImagesAsync(AssetBrowserHost, _currentAssetLibrary);
        }

        RequestDelayedRefresh();
        AppendLog(LogKind.User, "已调整道具图片顺序（不进剧情 CSV，只重排编号）。");
    }

    /// <summary>
    /// 道具图片铺进浏览器：和背景图**同一批方法** —— 点击看图、右键改备注/替换/删除
    /// 都按"文件自己所在目录"找分类（Editing.cs 的注释写明了），所以不需要另写一套。
    /// 卡片用带图卡 190×220（道具图通常不是宽幅，用背景图那个 16:9 会显得很扁）。
    /// </summary>
    private async Task FillAssetBrowserWithPropImagesAsync(Controls.AssetBrowser browser, AssetLibraryInfo assetLibrary)
    {
        var folderPath = GetPropImageFolderPath(assetLibrary);
        var orderedPaths = ImageSetService.GetFilePaths(folderPath);
        // 这行日志原来在旧网格的填充方法里（已退休），挪过来免得用户熟悉的日志消失。
        AppendLog(LogKind.Info, $"已加载道具图片：{orderedPaths.Count} 个文件。");
        SetAssetLibraryPath(folderPath);
        SetAssetLibraryUpEnabled(true);
        browser.SetCardsVisible(true);
        // 「移动素材」的两个零件：取道具图片列表 + 只重排编号（无 CSV 同步）。
        ConfigureMoveAsset(
            () => ImageSetService.GetFilePaths(GetPropImageFolderPath(assetLibrary)).Cast<object>().ToList(),
            items => ApplyPropImageOrderAsync(items.Cast<string>().ToList()));

        var cards = new List<object>();
        foreach (var path in orderedPaths)
        {
            var card = CardShell.CreateImageCard(
                path,
                AssetCardContentFactory.CreateImageAssetCardContent(path, 148, 148));
            card.Click += async (_, _) =>
            {
                // 移动模式下：点哪张就放到哪张后面（和背景图同一套交互）。
                if (await TryCompleteMoveBackgroundImageAsync(path))
                {
                    return;
                }

                PlaySelectionSound();
                ShowBackgroundImageViewerPage(path);
            };
            card.PointerMoved += Card_PointerMoved;
            card.ContextRequested += Card_ContextRequested;
            card.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetBackgroundImageRemarkAsync(path)),
                GridViewItemFactory.CreateMenuItem("移动素材", (_, _) => BeginMoveBackgroundImage(path, card)),
                GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceBackgroundImageAsync(path)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteBackgroundImageAsync(path)));
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(card, System.IO.Path.GetFileNameWithoutExtension(path));
            cards.Add(card);
        }

        browser.SetCards(cards);
    }

    /// <summary>
    /// 音频三类（音乐 / 环境音 / 特殊音效）铺进浏览器：扁平卡（音符图标 + 文件名）。
    /// 点击进播放器页、右键三项与旧卡**完全同一批方法**（按 kind 分发，所以三类共用这一个方法）。
    ///
    /// ⚠️ 这三类的「移动素材」还没接：它们的顺序同样要同步剧情（BGM / Scene / Scene_n），
    /// 得把 MoveAsset 那套按 kind 泛化一次；在那之前，旧网格里的拖拽仍是可用的退路。
    /// </summary>
    private void FillAssetBrowserWithAudio(Controls.AssetBrowser browser, AudioAssetKind kind, AssetLibraryInfo assetLibrary)
    {
        var folderPath = GetAudioFolderPath(assetLibrary, kind);
        // 旧填充里藏着一步"清理被忽略的旁挂文件"（AudioAssetService.DeleteIgnoredSidecarFiles）——
        // 那不是显示行为，删旧网格时不能连它一起丢，所以搬到这里。
        AudioAssetService.DeleteIgnoredSidecarFiles(kind, folderPath);
        var orderedPaths = AudioAssetService.GetFilePaths(folderPath);
        AppendLog(LogKind.Info, $"已加载{AudioAssetService.GetDisplayName(kind)}：{orderedPaths.Count} 个文件。");
        SetAssetLibraryPath(folderPath);
        SetAssetLibraryUpEnabled(true);
        browser.SetCardsVisible(true);
        // 「移动素材」的两个零件：取这一类的路径列表 + 用 ApplyAudioOrderAsync 落地
        //（音乐 BGM、环境音 Scene、特殊音效 Scene_n —— 落地方自己按 kind 分）。
        ConfigureMoveAsset(
            () => AudioAssetService.GetFilePaths(GetAudioFolderPath(assetLibrary, kind)).Cast<object>().ToList(),
            items => ApplyAudioOrderAsync(kind, items.Cast<string>().ToList()));

        var cards = new List<object>();
        foreach (var path in orderedPaths)
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FontIcon { FontSize = 16, Glyph = "\uE8D6" },
                    CardShell.CreateTitle(System.IO.Path.GetFileNameWithoutExtension(path))
                }
            };

            var card = CardShell.CreateFlatCard(path, content);
            card.Click += async (_, _) =>
            {
                // 移动模式下：点哪张就放到哪张后面（和背景图同一套交互），点完自动退出。
                if (await TryCompleteMoveBackgroundImageAsync(path))
                {
                    return;
                }

                PlaySelectionSound();
                ShowMusicPlayerPage(path, kind);
            };
            card.PointerMoved += Card_PointerMoved;
            card.ContextRequested += Card_ContextRequested;
            card.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetAudioRemarkAsync(kind, path)),
                GridViewItemFactory.CreateMenuItem("移动素材", (_, _) => BeginMoveBackgroundImage(path, card)),
                GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceAudioAsync(kind, path)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteAudioAsync(kind, path)));
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(card, System.IO.Path.GetFileNameWithoutExtension(path));
            cards.Add(card);
        }

        browser.SetCards(cards);
    }

    /// <summary>
    /// 打开素材库时铺的是**分类文件夹卡**（零境那种：图标 + 名字 + 数量），路径栏显示素材库根目录。
    /// 详情页现在只有这一个浏览器，所以这层就是"第一层"（外层选库那页是最外面那层）。
    /// </summary>
    private void FillAssetBrowserWithCategories(AssetLibraryInfo assetLibrary)
    {
        if (AssetBrowserHost is null)
        {
            return;
        }

        // 路径栏事件只挂一次；分类层是每次打开库的必经之路，最稳的接线点。
        EnsureAssetBrowserHandlers();

        SetAssetLibraryPath(assetLibrary.Path);
        AssetBrowserHost.SetCardsVisible(true);
        SetAssetLibraryUpEnabled(false);
        AssetBrowserHost.SetCards(BuildAssetCategoryCards(assetLibrary));
        // 回到分类层：只翻层标志，**不清 key**（清了会让功能路径的重铺变成 no-op）。
        _isAssetCategoryLayerOpen = true;
        EnsureAssetBrowserHandlers();
    }

    /// <summary>进到某个分类里：只把事件处理器挂好（卡片内容由各分类自己的填充方法负责）。</summary>
    private void ShowAssetBrowserLayer()
    {
        EnsureAssetBrowserHandlers();
    }

    /// <summary>退回分类层（路径栏的「上一层」与左侧返回箭头都走这里；已在分类层时等于重铺一次）。</summary>
    /// <summary>
    /// 整层切换的入场动画：内容从右（前进）或从左（后退）滑入 + 淡入。
    ///
    /// 为什么不是 PlayPageEntrance：素材库内部的"分类 → 素材"根本不切页面，
    /// 只是重铺卡片；只用页面级动画的话，中间这层永远是"啪"地换掉（用户 2026-11-06）。
    /// 参数与页面入场对齐：28px、220ms、EaseOut。
    /// </summary>
    private void PlayLayerSlide(FrameworkElement? element, bool forward)
    {
        if (element is null || element.Visibility != Visibility.Visible)
        {
            return;
        }

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        var easing = compositor.CreateCubicBezierEasingFunction(
            new System.Numerics.Vector2(0.1f, 0.9f),
            new System.Numerics.Vector2(0.2f, 1f));

        visual.Offset = new System.Numerics.Vector3(forward ? 28f : -28f, 0f, 0f);
        visual.Opacity = 0.82f;

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, easing);
        slide.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Offset", slide);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f, easing);
        fade.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Opacity", fade);
    }

    private void ReturnToAssetCategoryLayer()
    {
        if (_currentAssetLibrary is not null)
        {
            FillAssetBrowserWithCategories(_currentAssetLibrary);
            PlayLayerSlide(AssetBrowserHost, forward: false);
        }
    }

    /// <summary>事件只挂一次：先 -= 再 += 是幂等的（这个入口会被反复走到）。</summary>
    private void EnsureAssetBrowserHandlers()
    {
        if (AssetBrowserHost is null)
        {
            return;
        }

        // 路径栏已经提升到页面级（AssetLibraryPathBar），事件也挂它。
        AssetLibraryPathBar.BackRequested -= AssetBrowser_BackRequested;
        AssetLibraryPathBar.BackRequested += AssetBrowser_BackRequested;
        AssetLibraryPathBar.UpRequested -= AssetBrowser_UpRequested;
        AssetLibraryPathBar.UpRequested += AssetBrowser_UpRequested;
        AssetLibraryPathBar.OpenFolderRequested -= AssetBrowser_OpenFolderRequested;
        AssetLibraryPathBar.OpenFolderRequested += AssetBrowser_OpenFolderRequested;
    }

    /// <summary>路径栏文字（页面级 PathBar）。</summary>
    /// <summary>路径栏当前指向的目录（"在资源管理器打开"用它）。</summary>
    private string _currentAssetLibraryPath = string.Empty;

    private void SetAssetLibraryPath(string path)
    {
        AssetLibraryPathBar?.SetPath(path);
        _currentAssetLibraryPath = path;
    }

    private void SetAssetLibraryUpEnabled(bool enabled)
    {
        AssetLibraryPathBar?.SetUpEnabled(enabled);
    }

    private void AssetBrowser_BackRequested(object? sender, EventArgs e)
    {
        // 两级语义（用户最早的设计，2026-11-06 又被提了一次）：
        //   左键「返回」= 回素材主页面（库列表）；
        //   右键「上一层」= 只在当前库里往上退一级。
        // 之前两个按钮都接 ReturnToAssetCategoryLayer，等于重复。
        // 层级：图层浏览 → 角色详情 → 分类层 → 库列表，一次退一层。
        if (TryCloseCharacterLayerBrowse())
        {
            return;
        }

        if (TryCloseCharacterDetailOnBack())
        {
            return;
        }

        ShowAssetLibraryPage();
    }

    /// <summary>
    /// 在资源管理器里打开路径栏当前指向的目录。
    /// 详情打开时路径指向角色文件夹，所以同一个按钮也顺带能开角色的目录。
    /// </summary>
    private void AssetBrowser_OpenFolderRequested(object? sender, EventArgs e)
    {
        var path = _currentAssetLibraryPath;
        if (path.Length == 0 || !Directory.Exists(path))
        {
            ShowFloatingTip(InfoBarSeverity.Warning, "目录不存在", path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            ShowFloatingTip(InfoBarSeverity.Error, "打开资源管理器失败", path, error);
        }
    }

    private void AssetBrowser_UpRequested(object? sender, EventArgs e)
    {
        if (TryCloseCharacterLayerBrowse())
        {
            return;
        }

        if (TryCloseCharacterDetailOnBack())
        {
            return;
        }

        ReturnToAssetCategoryLayer();
    }
}
