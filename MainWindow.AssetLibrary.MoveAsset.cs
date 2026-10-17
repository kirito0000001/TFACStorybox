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

    private bool IsMovingBackgroundImage => _movingBackgroundImagePath is not null;

    /// <summary>进入移动模式（右键菜单「移动素材」）。</summary>
    private void BeginMoveBackgroundImage(string imagePath)
    {
        _movingBackgroundImagePath = imagePath;
        ClearMoveTargetHighlight();
        AssetLibraryStatusText.Text =
            $"移动素材：鼠标压到目标卡片上、左键点它，素材会放到它后面；点自己取消。（{Path.GetFileNameWithoutExtension(imagePath)}）";
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
            control.BorderThickness = new Thickness(2);
        }
    }

    /// <summary>
    /// 左键点某张卡：移动模式下 = 把素材放到它后面（返回 true，调用方别再打开看图页）；
    /// 不在移动模式返回 false，调用方照旧。
    /// </summary>
    private async Task<bool> TryCompleteMoveBackgroundImageAsync(string targetPath)
    {
        if (_movingBackgroundImagePath is not string movingPath || _currentAssetLibrary is null)
        {
            return false;
        }

        var orderedPaths = ImageSetService.GetFilePaths(GetBackgroundFolderPath(_currentAssetLibrary)).ToList();
        ClearMoveTargetHighlight();
        _movingBackgroundImagePath = null;

        if (string.Equals(movingPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            AssetLibraryStatusText.Text = "移动素材：已取消（点的是它自己）。";
            return true;
        }

        var movingIndex = orderedPaths.FindIndex(path => string.Equals(path, movingPath, StringComparison.OrdinalIgnoreCase));
        var targetIndex = orderedPaths.FindIndex(path => string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase));
        if (movingIndex < 0 || targetIndex < 0)
        {
            AssetLibraryStatusText.Text = "移动素材：找不到那张素材，已取消。";
            return true;
        }

        // 先摘掉自己：目标在原位置之后时，摘掉会让目标下标前移一格。
        orderedPaths.RemoveAt(movingIndex);
        if (targetIndex > movingIndex)
        {
            targetIndex--;
        }

        orderedPaths.Insert(Math.Clamp(targetIndex + 1, 0, orderedPaths.Count), movingPath);
        AssetLibraryStatusText.Text =
            $"移动素材：已把 {Path.GetFileNameWithoutExtension(movingPath)} 放到 {Path.GetFileNameWithoutExtension(targetPath)} 后面。";
        await ApplyBackgroundImageOrderAsync(orderedPaths);
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
        _movingBackgroundImagePath = null;
        AssetLibraryStatusText.Text = "移动素材：已取消。";
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
