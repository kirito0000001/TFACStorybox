using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools;

/// <summary>
/// 「移动素材」：右键菜单点一下进入**移动模式**，之后像拖动那样用鼠标选落点 ——
/// 鼠标压在哪张卡上哪张高亮，左键点它，素材就放到**那张的后面**，然后自动退出。
///
/// 【为什么不做真拖拽】用户 2026-10-17 定的：拖拽那条路（自写落位、边界判定、
/// 往后拖的下标前移）逻辑风险高、还容易误触；而"移动素材"要的只是**拖动那种方便**，
/// 不需要拖动本身 —— 判定变成"点一下"，落点规则只剩一条。
///
/// 【落点规则（用户口径）】点的那张 = 目标，插到它**后面**；点被移动的那张自己 = 取消。
/// 【链子复用】落地走 ApplyBackgroundImageOrderAsync：
/// 位置映射 → 记证据日志 → 重命名 → 同步所有章节 BGindex（一行没改）。
/// </summary>
public sealed partial class MainWindow
{
    private string? _movingBackgroundImagePath;
    private FrameworkElement? _moveTargetCard;
    private FrameworkElement? _moveSourceCard;

    /// <summary>移动模式里显示用的名字：路径取文件名，其它身份对象（如滤镜条目）用 ToString。</summary>
    private static string DescribeMoveItem(object item)
    {
        return item is string path ? Path.GetFileNameWithoutExtension(path) : item.ToString() ?? "素材";
    }

    /// <summary>
    /// 移动素材的**两个可替换零件**：取当前分类的条目列表、把新顺序落地。
    /// 条目是**不透明身份对象** —— 文件类分类放路径字符串（string），
    /// 角色滤镜放 <c>CharacterFilterEntry</c>（卡片 Tag 就是它），比较一律用 Equals。
    /// 默认（null）= 背景图那一套；各分类在填充方法里 ConfigureMoveAsset 一次即可 ——
    /// 交互（进入模式 / 悬停高亮 / 左键落位 / 右键取消）完全通用，不必按分类再写一遍。
    /// </summary>
    private Func<List<object>>? _moveItemProvider;
    private Func<List<object>, Task>? _moveOrderApplier;

    private void ConfigureMoveAsset(Func<List<object>> itemProvider, Func<List<object>, Task> orderApplier)
    {
        _moveItemProvider = itemProvider;
        _moveOrderApplier = orderApplier;
    }

    private bool IsMovingBackgroundImage => _movingBackgroundImagePath is not null;

    /// <summary>进入移动模式（右键菜单「移动素材」）。</summary>
    private void BeginMoveBackgroundImage(string imagePath, FrameworkElement? sourceCard = null)
    {
        _movingBackgroundImagePath = imagePath;
        _moveSourceCard = sourceCard;
        ClearMoveTargetHighlight();

        // 被移动的那张**自己也变样**：不然"点了移动素材没反应"（用户 2026-10-17 的反馈）——
        // 原来只有状态条一行字，太弱。
        if (sourceCard is Control source)
        {
            source.BorderThickness = new Thickness(3);
            source.Opacity = 0.55;
        }

        var name = Path.GetFileNameWithoutExtension(imagePath);
        // ⚠️ 写的是**详情页**的状态条（AssetLibraryDetailStatusText，Grid.Row=2，看得见）。
        // 之前写成 AssetLibraryStatusText —— 那是**外层素材库列表页**的（导入流程在用），
        // 此刻是隐藏的，于是"点了移动素材一点反应都没有"（用户 2026-10-17）。
        AssetLibraryDetailStatusText.Text =
            $"移动素材：鼠标压到目标卡片上、左键点它，素材会放到它后面；右键取消。（{name}）";
        // 日志也留一行：状态条不一定被看到，日志是这套工具里最稳的反馈通道。
        AppendLog(LogKind.User, $"移动素材：已选中 {name}，点目标卡片把它放到后面（右键取消）。");
    }

    /// <summary>鼠标压在卡片上：移动模式下高亮它（用户要的"像拖动那样"）。</summary>
    private void Card_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!IsMovingBackgroundImage || sender is not FrameworkElement card)
        {
            return;
        }

        if (ReferenceEquals(card, _moveTargetCard))
        {
            return;
        }

        ClearMoveTargetHighlight();
        _moveTargetCard = card;
        if (card is Control control)
        {
            // 和拖动那版同一个手法：只加粗描边，不换底色（免得和主题悬停色打架）。
            // 3px 而不是 2px：1px→2px 在深色主题下几乎看不出来（用户反馈"没反应"）。
            control.BorderThickness = new Thickness(3);
        }
    }

    /// <summary>
    /// 左键点某张卡：移动模式下 = 把素材放到它后面（返回 true，调用方别再打开看图页）；
    /// 不在移动模式返回 false，调用方照旧。
    /// </summary>
    private async Task<bool> TryCompleteMoveBackgroundImageAsync(object targetItem)
    {
        if (_movingBackgroundImagePath is null || _currentAssetLibrary is null)
        {
            return false;
        }

        var movingItem = _movingBackgroundImagePath;
        // 条目列表与落地动作都走"可替换零件"：默认是背景图，各分类配置过就用自己的。
        var items = _moveItemProvider?.Invoke()
            ?? ImageSetService.GetFilePaths(GetBackgroundFolderPath(_currentAssetLibrary)).Cast<object>().ToList();
        ClearMoveTargetHighlight();
        RestoreMoveSourceCard();
        _movingBackgroundImagePath = null;

        if (Equals(movingItem, targetItem))
        {
            AssetLibraryDetailStatusText.Text = "移动素材：已取消（点的是它自己）。";
            return true;
        }

        var movingIndex = items.FindIndex(item => Equals(item, movingItem));
        var targetIndex = items.FindIndex(item => Equals(item, targetItem));
        if (movingIndex < 0 || targetIndex < 0)
        {
            AssetLibraryDetailStatusText.Text = "移动素材：找不到那张素材，已取消。";
            return true;
        }

        // 先摘掉自己：目标在原位置之后时，摘掉会让目标下标前移一格。
        items.RemoveAt(movingIndex);
        if (targetIndex > movingIndex)
        {
            targetIndex--;
        }

        items.Insert(Math.Clamp(targetIndex + 1, 0, items.Count), movingItem);
        AssetLibraryDetailStatusText.Text =
            $"移动素材：已把 {DescribeMoveItem(movingItem)} 放到 {DescribeMoveItem(targetItem)} 后面。";
        await (_moveOrderApplier?.Invoke(items)
            ?? ApplyBackgroundImageOrderAsync(items.Cast<string>().ToList()));
        return true;
    }

    /// <summary>
    /// 移动模式下**右键 = 取消**（用户 2026-10-17 定的：左键确定、右键取消）。
    ///
    /// ⚠️ 挂的是 <c>ContextRequested</c> 而不是 <c>RightTapped</c>：卡片的右键菜单是
    /// 在 ContextRequested 上弹出的，只把 RightTapped 标记处理掉**拦不住菜单**。
    /// 这里 args.Handled = true 才是"这次右键不弹菜单"的正解。
    /// </summary>
    private void Card_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (!IsMovingBackgroundImage)
        {
            return;
        }

        args.Handled = true;
        CancelMoveBackgroundImage();
    }

    /// <summary>取消移动模式（右键，或点到自己那张）。</summary>
    private void CancelMoveBackgroundImage()
    {
        ClearMoveTargetHighlight();
        RestoreMoveSourceCard();
        _movingBackgroundImagePath = null;
        AssetLibraryDetailStatusText.Text = "移动素材：已取消。";
        AppendLog(LogKind.User, "移动素材：已取消。");
    }

    /// <summary>把"被移动的那张"恢复成普通样子（落位成功或取消时都要调）。</summary>
    private void RestoreMoveSourceCard()
    {
        if (_moveSourceCard is Control source)
        {
            source.BorderThickness = new Thickness(1);
            source.Opacity = 1;
        }

        _moveSourceCard = null;
    }

    private void ClearMoveTargetHighlight()
    {
        if (_moveTargetCard is Control control)
        {
            control.BorderThickness = new Thickness(1);
        }

        _moveTargetCard = null;
    }
}
