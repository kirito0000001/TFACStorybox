using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace GalExcleTools;

/// <summary>
/// **自写重排**：卡片是纯 Button（不搞容器嵌套），落位逻辑归我们。
///
/// 【为什么要自己写】Button 会捕获指针，GridView 的内置重排等不到手势。
/// 而卡片设了 <c>CanDrag = true</c>（见 CardShell）之后，框架会把"按住后移动"
/// 识别成**数据拖拽**（按住不动仍是点击，不需要长按阈值）——
/// 于是落点计算、插入位置、落地动作都由我们负责：
///   · DragOver：算目标下标（离指针最近的卡片），高亮目标卡；
///   · Drop：把卡片挪到目标下标，取新顺序，交给 ApplyBackgroundImageOrderAsync 走同步链。
///
/// 【和框架那套的差别（预期内，不是 bug）】
///   · 拖动过程中卡片**不会实时让位**，松手才落位；
///   · 没有"拖到边缘自动滚动"。
/// 换来的是：卡片保持零境那种纯 Button 结构，判定权完全在我们手里。
/// </summary>
public sealed partial class MainWindow
{
    private FrameworkElement? _cardReorderHighlightTarget;

    /// <summary>给网格挂上自写重排（幂等：-= 再 +=，重复进分类不会挂两遍）。</summary>
    private void AttachCardReorder(GridView grid)
    {
        grid.AllowDrop = true;
        grid.DragOver -= CardReorderGrid_DragOver;
        grid.DragOver += CardReorderGrid_DragOver;
        grid.Drop -= CardReorderGrid_Drop;
        grid.Drop += CardReorderGrid_Drop;
        grid.DragLeave -= CardReorderGrid_DragLeave;
        grid.DragLeave += CardReorderGrid_DragLeave;
    }

    /// <summary>
    /// 卡片起拖时记下"被拖的是哪张"：拖拽由**卡片自己**发起（CanDrag），
    /// 所以 GridView 的 DragItemsStarting 不会响 —— 这里挂的是卡片的 DragStarting。
    /// </summary>
    private void Card_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        _draggingBackgroundImageItem = sender as FrameworkElement;
    }

    private void CardReorderGrid_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not GridView grid)
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
        HighlightCardReorderTarget(grid, e.GetPosition(grid));
    }

    private void CardReorderGrid_DragLeave(object sender, DragEventArgs e)
    {
        ClearCardReorderHighlight();
    }

    private async void CardReorderGrid_Drop(object sender, DragEventArgs e)
    {
        if (sender is not GridView grid)
        {
            return;
        }

        e.Handled = true;
        var targetIndex = ResolveCardReorderTargetIndex(grid, e.GetPosition(grid));
        ClearCardReorderHighlight();

        var dragged = _draggingBackgroundImageItem;
        if (dragged is null)
        {
            return;
        }

        var currentIndex = grid.Items.IndexOf(dragged);
        if (currentIndex < 0)
        {
            return;
        }

        // 目标下标是"要插到哪张之前"；往后拖时，移除自己会让后面的下标前移一格。
        var insertIndex = targetIndex < 0 ? grid.Items.Count - 1 : targetIndex;
        if (insertIndex > currentIndex)
        {
            insertIndex--;
        }

        _draggingBackgroundImageItem = null;
        if (insertIndex == currentIndex)
        {
            return;
        }

        grid.Items.Remove(dragged);
        grid.Items.Insert(Math.Clamp(insertIndex, 0, grid.Items.Count), dragged);

        var orderedPaths = GetOrderedExistingTaggedPaths(grid);
        await ApplyBackgroundImageOrderAsync(orderedPaths);
    }

    /// <summary>目标下标：取"中心离指针最近"的那张卡；一张都没量到就返回 -1（= 落到末尾）。</summary>
    private static int ResolveCardReorderTargetIndex(GridView grid, Windows.Foundation.Point position)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        var cards = grid.Items.OfType<FrameworkElement>().ToList();
        for (var index = 0; index < cards.Count; index++)
        {
            if (cards[index].ActualWidth <= 0 || cards[index].ActualHeight <= 0)
            {
                continue;
            }

            var origin = cards[index].TransformToVisual(grid).TransformPoint(new Windows.Foundation.Point(0, 0));
            var centerX = origin.X + (cards[index].ActualWidth / 2);
            var centerY = origin.Y + (cards[index].ActualHeight / 2);
            var distance = Math.Abs(position.X - centerX) + Math.Abs(position.Y - centerY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    private void HighlightCardReorderTarget(GridView grid, Windows.Foundation.Point position)
    {
        var cards = grid.Items.OfType<FrameworkElement>().ToList();
        var index = ResolveCardReorderTargetIndex(grid, position);
        var target = index >= 0 && index < cards.Count ? cards[index] : null;
        if (ReferenceEquals(target, _cardReorderHighlightTarget))
        {
            return;
        }

        ClearCardReorderHighlight();
        _cardReorderHighlightTarget = target;
        if (target is Control control)
        {
            // 只加粗描边：不换底色，免得和主题的悬停状态色打架。
            control.BorderThickness = new Thickness(2);
        }
    }

    private void ClearCardReorderHighlight()
    {
        if (_cardReorderHighlightTarget is Control control)
        {
            control.BorderThickness = new Thickness(1);
        }

        _cardReorderHighlightTarget = null;
    }
}
