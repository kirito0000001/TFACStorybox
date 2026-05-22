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

    public static Border CreateFunctionCardContent(string indicator, string detailText)
    {
        var detailBlock = new TextBlock
        {
            Text = detailText,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(detailBlock, 1);

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = new Grid
            {
                Padding = new Thickness(14, 10, 14, 10),
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                    new RowDefinition { Height = GridLength.Auto }
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = indicator,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    detailBlock
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

    public static Border CreateCharacterFilterCardContent(string indexText, string title)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(titleBlock, 1);

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
                Padding = new Thickness(14, 10, 10, 10),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = indexText,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    titleBlock
                }
            }
        };
    }

    public static Border CreateAddCharacterFilterCardContent()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new SymbolIcon
                    {
                        Symbol = Symbol.Add,
                        Width = 18,
                        Height = 18,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "新增滤镜",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
    }

    public static StackPanel CreateCharacterCardContent(string code, string name, string colorHex)
    {
        var color = ColorUtility.ParseColor(colorHex, Windows.UI.Color.FromArgb(255, 0, 143, 141));

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateCharacterColorBlock(code, new SolidColorBrush(color), 22),
                new TextBlock
                {
                    Text = name,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
    }

    public static StackPanel CreateAddCharacterCardContent()
    {
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateCharacterColorBlock("+", new SolidColorBrush(Microsoft.UI.Colors.White), 64),
                new TextBlock
                {
                    Text = "新建立绘",
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
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
