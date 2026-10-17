using System;
using System.Collections.Generic;
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

        // TODO（叶子层迁移）：每个分类迁完就把这里换成它自己的填充方法。
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
            default:
                AssetLibraryStatusText.Text = "这个分类还没迁移到新骨架：背景图、音乐、环境音、特殊音效已好，其余按顺序来。";
                break;
        }
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
        var orderedPaths = AudioAssetService.GetFilePaths(folderPath);
        browser.SetPath(folderPath);
        browser.SetUpEnabled(true);

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
            card.Click += (_, _) =>
            {
                PlaySelectionSound();
                ShowMusicPlayerPage(path, kind);
            };
            card.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetAudioRemarkAsync(kind, path)),
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

        AssetBrowserHost.SetPath(assetLibrary.Path);
        AssetBrowserHost.SetUpEnabled(false);
        AssetBrowserHost.SetCards(BuildAssetCategoryCards(assetLibrary));
        EnsureAssetBrowserHandlers();
    }

    /// <summary>进到某个分类里：只把事件处理器挂好（卡片内容由各分类自己的填充方法负责）。</summary>
    private void ShowAssetBrowserLayer()
    {
        EnsureAssetBrowserHandlers();
    }

    /// <summary>退回分类层（路径栏的「上一层」与左侧返回箭头都走这里；已在分类层时等于重铺一次）。</summary>
    private void ReturnToAssetCategoryLayer()
    {
        if (_currentAssetLibrary is not null)
        {
            FillAssetBrowserWithCategories(_currentAssetLibrary);
        }
    }

    /// <summary>事件只挂一次：先 -= 再 += 是幂等的（这个入口会被反复走到）。</summary>
    private void EnsureAssetBrowserHandlers()
    {
        if (AssetBrowserHost is null)
        {
            return;
        }

        AssetBrowserHost.BackRequested -= AssetBrowser_BackRequested;
        AssetBrowserHost.BackRequested += AssetBrowser_BackRequested;
        AssetBrowserHost.UpRequested -= AssetBrowser_UpRequested;
        AssetBrowserHost.UpRequested += AssetBrowser_UpRequested;
    }

    private void AssetBrowser_BackRequested(object? sender, EventArgs e)
    {
        ReturnToAssetCategoryLayer();
    }

    private void AssetBrowser_UpRequested(object? sender, EventArgs e)
    {
        ReturnToAssetCategoryLayer();
    }
}
