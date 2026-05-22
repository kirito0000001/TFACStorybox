using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Views;

internal static class GridViewItemFactory
{
    public static GridViewItem CreateDashboardCard(object? tag = null)
    {
        return CreateCard(260, 318, tag, marginRight: 18, marginBottom: 18);
    }

    public static GridViewItem CreateCard(
        double width,
        double height,
        object? tag = null,
        double marginRight = 14,
        double marginBottom = 14,
        bool stretchContent = false)
    {
        var item = new GridViewItem
        {
            Width = width,
            Height = height,
            Margin = new Thickness(0, 0, marginRight, marginBottom),
            Tag = tag
        };

        if (stretchContent)
        {
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            item.VerticalContentAlignment = VerticalAlignment.Stretch;
        }

        return item;
    }

    public static MenuFlyout CreateMenu(params MenuFlyoutItem[] items)
    {
        var flyout = new MenuFlyout();
        foreach (var item in items)
        {
            flyout.Items.Add(item);
        }

        return flyout;
    }

    public static MenuFlyoutItem CreateMenuItem(string text, RoutedEventHandler click)
    {
        var item = new MenuFlyoutItem
        {
            Text = text
        };
        item.Click += click;
        return item;
    }
}
