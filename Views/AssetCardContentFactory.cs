using System.IO;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

internal static class AssetCardContentFactory
{
    public static StackPanel CreateImageAssetCardContent(
        string imagePath,
        double thumbnailWidth,
        double thumbnailHeight,
        bool tagWithPath = false)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            Tag = tagWithPath ? imagePath : null
        };

        panel.Children.Add(ThumbnailFactory.CreateThumbnail(imagePath, thumbnailWidth, thumbnailHeight, showAddIcon: false));
        panel.Children.Add(new TextBlock
        {
            Text = Path.GetFileNameWithoutExtension(imagePath),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
        });

        return panel;
    }

    public static Border CreateIconAssetCardContent(Symbol symbol, string title)
    {
        var titleText = new TextBlock
        {
            Text = title,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(titleText, 1);

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = new Grid
            {
                Padding = new Thickness(14, 10, 14, 10),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    new SymbolIcon { Symbol = symbol, VerticalAlignment = VerticalAlignment.Center },
                    titleText
                }
            }
        };
    }

    public static Border CreateFunctionCardContent(string name, string indicator, string description)
    {
        var nameBlock = new TextBlock
        {
            Text = name,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        var indicatorBlock = new TextBlock
        {
            Text = indicator,
            FontSize = 12,
            Opacity = 0.65,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetRow(indicatorBlock, 1);
        var descriptionBlock = new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(descriptionBlock, 2);

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new Grid
            {
                Padding = new Thickness(14, 12, 14, 10),
                RowSpacing = 3,
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                },
                Children =
                {
                    nameBlock,
                    indicatorBlock,
                    descriptionBlock
                }
            }
        };
    }

    public static Border CreateAddTextCardContent(string text)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 42,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }


    private static Border CreateCharacterColorBlock(string text, Brush background, double fontSize)
    {
        return new Border
        {
            Width = 138,
            Height = 178,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Background = background,
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }
}
