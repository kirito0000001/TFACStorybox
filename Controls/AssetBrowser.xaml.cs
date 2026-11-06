using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Controls;

/// <summary>
/// 素材浏览器骨架（照隔壁零境工具箱的 Controls/AssetLibraryBrowser）：
/// 路径栏 + 卡片网格。这里**只管壳**：
///   · 卡片集合由外面塞（<see cref="SetCards"/>）—— 因为每类素材的卡片内容与尺寸都不同
///     （背景图 16:9、音频扁平、立绘带缩略图），统一模板反而绕；
///   · 拖拽重排的接线（CanDragItems / DragItemsStarting / DragItemsCompleted）由外面挂到
///     <see cref="CardGrid"/> 上 —— 那套还要和"改索引 → 同步剧情"绑在一起，属于页面的事。
///
/// ⚠️ 卡片用 <c>CardGrid.Items</c> 直接放，**不绑 ItemsSource**：外面的拖拽代码会
/// <c>Items.Remove/Add</c> 来挪卡片（MoveGridViewItemToEnd），绑了 ItemsSource 就会抛。
/// </summary>
public sealed partial class AssetBrowser : UserControl
{
    public AssetBrowser()
    {
        InitializeComponent();

    }

    /// <summary>
    /// 详情宿主：路径栏下方那一片，和卡片区重叠。
    /// 页面把"角色详情"这类子面板塞进来，就不用自己算"路径栏多高"。
    /// </summary>
    // 名字不能叫 DetailHost：x:Name="DetailHost" 生成的字段已经占用了这个名字。
    public Grid DetailHostPanel => DetailHost;

    /// <summary>拖拽重排的接线处（CanDragItems / DragItemsStarting / DragItemsCompleted 都挂它）。</summary>
    public GridView CardsHost => CardGrid;


    public void SetCards(IEnumerable<object> cards)
    {
        CardGrid.Items.Clear();
        foreach (var card in cards)
        {
            CardGrid.Items.Add(card);
        }
    }

    /// <summary>
    /// 卡片网格的显隐。角色详情这类"子面板"要盖在卡片区上，
    /// 但**路径栏必须留着** —— 所以不能整个控件藏掉，只藏网格。
    /// </summary>
    public void SetCardsVisible(bool visible)
    {
        if (CardGrid is not null)
        {
            CardGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>能不能往上退（根分类时置 false，让"上一层"按钮变灰）。</summary>
    public void SetUpEnabled(bool enabled)
    {
        PathBarHost.SetUpEnabled(enabled);
    }

}
