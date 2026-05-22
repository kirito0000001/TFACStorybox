using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace GalExcleTools.Views;

internal static class ThumbnailFactory
{
    public const string DefaultThumbnailUri = "ms-appx:///Assets/DefaultProjectThumbnail.png";

    public static FrameworkElement CreateThumbnail(string? thumbnailPath, double width, double height, bool showAddIcon)
    {
        var grid = new Grid
        {
            Width = width,
            Height = height
        };

        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1)
        });

        if (showAddIcon)
        {
            grid.Children.Add(new TextBlock
            {
                Text = "+",
                FontSize = 84,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return grid;
        }

        var image = new Image
        {
            Stretch = Stretch.Uniform
        };

        if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
        {
            image.Source = CreateDefaultBitmap();
        }
        else
        {
            _ = LoadThumbnailFromFileAsync(image, thumbnailPath);
        }

        grid.Children.Add(image);
        return grid;
    }

    public static async Task LoadThumbnailFromFileAsync(Image image, string thumbnailPath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(thumbnailPath);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch
        {
            image.Source = CreateDefaultBitmap();
        }
    }

    public static BitmapImage CreateDefaultBitmap()
    {
        return new BitmapImage(new Uri(DefaultThumbnailUri));
    }
}
