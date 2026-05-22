using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

internal static class DashboardCardFactory
{
    public static GridViewItem CreateInfoCard(
        object tag,
        string? thumbnailPath,
        string title,
        string subtitle,
        string footer,
        TappedEventHandler tappedHandler,
        MenuFlyout? contextFlyout = null)
    {
        var item = GridViewItemFactory.CreateDashboardCard(tag);
        item.Tapped += tappedHandler;
        item.ContextFlyout = contextFlyout;
        item.Content = CardContentFactory.CreateDashboardCardContent(thumbnailPath, title, subtitle, footer);
        return item;
    }

    public static GridViewItem CreateAddCard(string title, string subtitle, TappedEventHandler tappedHandler)
    {
        var item = GridViewItemFactory.CreateDashboardCard();
        item.Tapped += tappedHandler;
        ToolTipService.SetToolTip(item, title);
        item.Content = CardContentFactory.CreateAddCardContent(title, subtitle);
        return item;
    }

    public static void MarkSelected(GridViewItem item)
    {
        item.BorderThickness = new Thickness(2);
        item.BorderBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
    }
}
