using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

internal static class CardContentFactory
{
    public static StackPanel CreateDashboardCardContent(
        string? thumbnailPath,
        string title,
        string subtitle,
        string footer)
    {
        return CreateCardContent(
            thumbnailPath,
            title,
            subtitle,
            footer,
            path => ThumbnailFactory.CreateThumbnail(path, 236, 178, showAddIcon: false));
    }

    public static StackPanel CreateCardContent(
        string? thumbnailPath,
        string title,
        string subtitle,
        string footer,
        Func<string?, UIElement> createThumbnail)
    {
        var panel = new StackPanel
        {
            Height = 294,
            Spacing = 8
        };
        ToolTipService.SetToolTip(panel, title);

        panel.Children.Add(createThumbnail(thumbnailPath));
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (!string.IsNullOrWhiteSpace(footer))
        {
            panel.Children.Add(new TextBlock
            {
                Text = footer,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
                TextAlignment = TextAlignment.Center,
                FontSize = 12
            });
        }

        return panel;
    }

    public static Grid CreateAddCardContent(string title, string subtitle)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new TextBlock
        {
            Text = "+",
            FontSize = 78,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            LineHeight = 82
        });
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var root = new Grid
        {
            Width = 236,
            Height = 294
        };
        ToolTipService.SetToolTip(root, title);
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1)
        });
        root.Children.Add(panel);
        return root;
    }
}
