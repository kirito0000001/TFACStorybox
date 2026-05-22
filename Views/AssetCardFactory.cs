using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GalExcleTools.Views;

internal static class AssetCardFactory
{
    public static GridViewItem CreateCard(
        double width,
        double height,
        UIElement content,
        object? tag = null,
        TappedEventHandler? tappedHandler = null,
        MenuFlyout? contextFlyout = null,
        bool stretchContent = false,
        double marginRight = 14,
        double marginBottom = 14,
        object? toolTip = null)
    {
        var item = GridViewItemFactory.CreateCard(
            width,
            height,
            tag,
            marginRight,
            marginBottom,
            stretchContent);
        item.Content = content;
        item.ContextFlyout = contextFlyout;

        if (tappedHandler is not null)
        {
            item.Tapped += tappedHandler;
        }

        if (toolTip is not null)
        {
            ToolTipService.SetToolTip(item, toolTip);
        }

        return item;
    }
}
