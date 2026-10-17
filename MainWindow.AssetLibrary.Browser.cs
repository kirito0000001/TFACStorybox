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
            default:
                AssetLibraryStatusText.Text = "这个分类还没迁移到新骨架：目前只有背景图迁好了，其余按顺序来。";
                break;
        }
    }

    /// <summary>切到第二层（浏览器）：收起分类卡、显示浏览器。旧的 Expander 堆叠保持折叠（它是退路）。</summary>
    private void ShowAssetBrowserLayer()
    {
        if (AssetBrowserHost is not null)
        {
            // -= 再 += ：这个入口可能被点很多次，不能一次挂一个处理器。
            AssetBrowserHost.BackRequested -= AssetBrowser_BackRequested;
            AssetBrowserHost.BackRequested += AssetBrowser_BackRequested;
            AssetBrowserHost.Visibility = Visibility.Visible;
        }

        if (AssetCategoryGridView is not null)
        {
            AssetCategoryGridView.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>第二层返回第一层（浏览器左上角那个返回箭头）。</summary>
    private void CloseAssetBrowser()
    {
        if (AssetBrowserHost is not null)
        {
            AssetBrowserHost.Visibility = Visibility.Collapsed;
        }

        if (AssetCategoryGridView is not null)
        {
            AssetCategoryGridView.Visibility = Visibility.Visible;
        }
    }

    private void AssetBrowser_BackRequested(object? sender, EventArgs e)
    {
        CloseAssetBrowser();
    }
}
