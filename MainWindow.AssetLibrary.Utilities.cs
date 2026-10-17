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
            var convertedCount = await _imageSetService.NormalizeFilesAsync(
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

        /// <summary>
        /// 把一个分类的素材填进新的浏览器骨架（Controls.AssetBrowser）。
        ///
        /// 用户 2026-10-17：素材页骨架照隔壁零境换。这一步**先只填背景图、只读展示** ——
        /// 不接拖拽重排、也不删旧的 Expander 界面，先看观感。
        /// 卡片尺寸：背景图走 **16:9**（CardShell.CreateBackgroundCard = 272×153）。
        /// </summary>
        private async Task FillAssetBrowserWithBackgroundsAsync(Controls.AssetBrowser browser, AssetLibraryInfo assetLibrary)
        {
            var folderPath = Services.WorkspacePathUtility.GetBackgroundFolderPath(assetLibrary);
            var orderedPaths = ImageSetService.GetFilePaths(folderPath);
            // 不设大标题：零境那页只有路径栏（页名/库名在外层已经有了，再来一遍是重复）。
            browser.SetPath(folderPath);
            browser.SetUpEnabled(true);

            var cards = new System.Collections.Generic.List<object>();
            foreach (var path in orderedPaths)
            {
                var preview = new Microsoft.UI.Xaml.Controls.Image
                {
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch
                };
                await ThumbnailFactory.LoadThumbnailFromFileAsync(preview, path);

                var content = new Microsoft.UI.Xaml.Controls.Grid();
                content.Children.Add(preview);
                // 名字压在图上：卡片只有 16:9 那一块，留白条比让文字挤掉画面划算。
                content.Children.Add(new Microsoft.UI.Xaml.Controls.Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0, 0, 0)),
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
                    Padding = new Microsoft.UI.Xaml.Thickness(8, 4, 8, 4),
                    Child = CardShell.CreateTitle(Path.GetFileNameWithoutExtension(path))
                });

                var card = CardShell.CreateBackgroundCard(path, content);
            // 交互与旧卡**同一批方法**（查看器/改备注/替换/删除都按"文件自己所在目录"找分类，
            // 所以背景图与道具图片通用）：点击看图、右键三项、悬停看文件名。
            card.Click += (_, _) =>
            {
                PlaySelectionSound();
                ShowBackgroundImageViewerPage(path);
            };
            card.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetBackgroundImageRemarkAsync(path)),
                GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceBackgroundImageAsync(path)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteBackgroundImageAsync(path)));
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(card, Path.GetFileNameWithoutExtension(path));
            cards.Add(card);
            }

            browser.SetCards(cards);

            // 新骨架的卡片网格**也开拖拽重排**：处理器与旧网格共用（已经去掉写死网格名，
            // 谁发起拖拽就用谁的 Items 算顺序），拖完照样走"改索引 → 同步剧情 BGindex"那条链。
            // -= 再 += 是幂等的：这个填充每次进分类都会跑一遍，不能重复挂处理器。
            var grid = browser.CardsHost;
            grid.AllowDrop = true;
            grid.CanDragItems = true;
            grid.CanReorderItems = true;
            grid.ReorderMode = Microsoft.UI.Xaml.Controls.ListViewReorderMode.Enabled;
            grid.DragItemsStarting -= BackgroundImagesGridView_DragItemsStarting;
            grid.DragItemsStarting += BackgroundImagesGridView_DragItemsStarting;
            grid.DragOver -= BackgroundImagesGridView_DragOver;
            grid.DragOver += BackgroundImagesGridView_DragOver;
            grid.DragItemsCompleted -= BackgroundImagesGridView_DragItemsCompleted;
            grid.DragItemsCompleted += BackgroundImagesGridView_DragItemsCompleted;
        }

    }
}
