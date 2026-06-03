using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using GalExcleTools.Views;
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private static Style CreateDestructivePrimaryButtonStyle()
        {
            var style = new Style(typeof(Button))
            {
                BasedOn = Application.Current.Resources["AccentButtonStyle"] as Style
            };
            style.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.IndianRed)));
            style.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)));
            return style;
        }

        private async Task NormalizeBackgroundImagesAsync(string backgroundFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            var convertedCount = await _backgroundImageService.NormalizeFilesAsync(
                backgroundFolderPath,
                ConvertImageToPngAsync,
                orderedPaths);
            if (convertedCount > 0)
            {
                AppendLog(LogKind.Info, $"已转换 JPG/JPEG/WebP 背景图为 png：{convertedCount} 个。");
            }
        }

        private static Task ImportBackgroundImageAsPngAsync(string sourcePath, string targetPngPath)
        {
            var extension = Path.GetExtension(sourcePath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPngPath, overwrite: true);
                return Task.CompletedTask;
            }

            return ConvertImageToPngAsync(sourcePath, targetPngPath);
        }

        private static async Task ConvertImageToPngAsync(string sourcePath, string targetPngPath)
        {
            var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
            using var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(sourceStream);
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            File.Create(targetPngPath).Dispose();
            var targetFile = await StorageFile.GetFileFromPathAsync(targetPngPath);
            using var targetStream = await targetFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, targetStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                decoder.PixelWidth,
                decoder.PixelHeight,
                decoder.DpiX,
                decoder.DpiY,
                pixelData.DetachPixelData());
            await encoder.FlushAsync();
        }

    }
}
