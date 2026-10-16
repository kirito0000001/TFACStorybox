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
        private async void ChooseProjectRootButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFolder = await picker.PickSingleFolderAsync();
            if (selectedFolder is null)
            {
                return;
            }

            var newProjectRootPath = Settings.BuildProjectRootPathFromParent(selectedFolder.Path);
            var oldProjectRootPath = Path.GetFullPath(Settings.ProjectRootPath);
            AppendLog(LogKind.User, $"选择新的整体项目父目录：{selectedFolder.Path}");

            if (Settings.IsCurrentProjectRoot(newProjectRootPath))
            {
                SetProjectRootStatus(InfoBarSeverity.Informational, "目录未变化", $"当前已经在使用：{newProjectRootPath}");
                return;
            }

            if (Settings.IsCandidateInsideCurrentRoot(newProjectRootPath))
            {
                SetProjectRootStatus(InfoBarSeverity.Error, "无法迁移目录", "新位置不能放在旧项目总目录里面，否则迁移完成后删除旧目录时会连新目录一起删除。");
                return;
            }

            try
            {
                AppendLog(LogKind.Info, $"开始迁移整体项目目录：{oldProjectRootPath} -> {newProjectRootPath}");
                ShowGlobalProgress("迁移整体项目目录", newProjectRootPath);
                UpdateGlobalProgress("正在复制和校验项目文件...", 5, $"{oldProjectRootPath} -> {newProjectRootPath}");
                var progress = new Progress<ProgressUpdate>(update =>
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
                var result = await Settings.ChangeProjectRootAsync(
                    newProjectRootPath,
                    progress,
                    GetGlobalProgressCancellationToken());

                LoadAllCards();
                CompleteGlobalProgress("目录迁移完成", $"已迁移 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹");
                await HideGlobalProgressAfterDelayAsync();
                SetProjectRootStatus(InfoBarSeverity.Success, "目录迁移完成", $"已迁移并校验 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹。旧目录已删除：{oldProjectRootPath}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "目录迁移已取消" : "目录迁移失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                EnsureProjectRootDirectory(Settings.ProjectRootPath);
                AppendLog(LogKind.Error, "整体项目目录迁移失败。", ex);
                SetProjectRootStatus(InfoBarSeverity.Error, "目录迁移失败", $"已保留原目录和设置，未删除旧目录。错误：{ex.Message}");
            }
        }

        private void SetProjectRootStatus(InfoBarSeverity severity, string title, string message)
        {
            Settings.SetProjectRootStatus(severity, title, message);

            var logKind = severity == InfoBarSeverity.Error
                ? LogKind.Error
                : severity == InfoBarSeverity.Warning
                    ? LogKind.Warning
                    : LogKind.Info;
            AppendLog(logKind, $"{title}：{message}");
        }

        private void SaveAppSettings()
        {
            Settings.Save();
        }

        private void ApplyThemeSettings()
        {
            var isDark = Settings.NightModeEnabled;
            // 先盖画笔再换元素主题：这一页之后建出来的东西（卡片、对比表那些**在代码里**
            // 取画笔的地方）都得取到"工具正在显示的那一套"。
            // 返回 0 表示一个画笔都没取到 —— 那条路这次没兜住，留一行日志好排查。
            if (ApplyThemeResourceOverride(isDark) == 0)
            {
                AppendLog(
                    LogKind.Warning,
                    $"主题画笔覆盖没取到任何画笔（themeKey={(isDark ? "Dark" : "Light")}）："
                    + "应用级那条查找路可能摸不到系统主题字典，切换主题后颜色也许不会立刻跟上，重启即可。");
            }

            RootGrid.RequestedTheme = isDark
                ? ElementTheme.Dark
                : ElementTheme.Light;
            ApplyTitleBarTheme(isDark);
        }

        /// <summary>
        /// 把**当前主题那一套画笔**提到应用级资源里。
        ///
        /// 【为什么非有这一步】代码里取画笔只能写 <c>Application.Current.Resources["…Brush"]</c>，
        /// 而那个索引器按**应用主题**解析（<c>Application.RequestedTheme</c>），
        /// 跟 <c>RootGrid.RequestedTheme</c>（「整体设置 → 夜间模式」定的界面主题）**是两回事**：
        ///   · XAML 里的 <c>{ThemeResource}</c> 跟着**元素**主题走 → 对；
        ///   · 代码里取的却是那条应用级的路 → 另一套 → 错。
        /// 2026-10-16 实测（系统白天、界面也浅色）那条路落在**深色**那套上：素材库里图片名字
        /// 取到 <c>TextFillColorSecondaryBrush</c> 的深色值（#C5FFFFFF，压在浅色卡片上就是
        /// 看不见的白字），同步对比表的行取到纯白（整片空白）。截图取样核过：
        /// 文字像素比底色**更亮**，正是深色主题那套笔画上去的结果。
        ///
        /// 【根治在别处】启动时 <c>App.ApplySavedThemeBeforeFirstWindow</c> 已经把
        /// <c>Application.RequestedTheme</c> 钉成用户设置的主题了，所以开机那次本来就是对的。
        /// 这里是**运行中切换开关**的兜底：那个属性过时就设不动，只能把目标主题的画笔盖上来。
        ///
        /// 【做法】把目标主题那份字典里的画笔逐个盖到应用级：之后无论 XAML 还是代码，
        /// 取到的都是同一套（工具真正在显示的那套）。**值本来就相同时是空操作**，所以安全。
        ///
        /// ⚠️ 只覆盖 Brush：样式（AccentButtonStyle 之类）在应用级没有这个主题差异问题，
        /// 复制一份出来反而会和控件模板打架。
        /// ⚠️ 高对比度主题不受这里影响（工具本来就强制 Light/Dark，这是既有行为，没动它）。
        /// ⚠️ 万一这份字典摸不到系统主题画笔（返回 0），调用方会记一条警告 —— 那种情况下
        /// 切换主题得重启才彻底生效。
        /// </summary>
        private static int ApplyThemeResourceOverride(bool isDark)
        {
            var themeKey = isDark ? "Dark" : "Light";
            if (!Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var value) ||
                value is not ResourceDictionary themeDictionary)
            {
                return 0;
            }

            var copied = 0;
            foreach (var entry in themeDictionary)
            {
                if (entry.Value is Brush brush)
                {
                    Application.Current.Resources[entry.Key] = brush;
                    copied++;
                }
            }

            return copied;
        }

        // 最小化/最大化/关闭这三个按钮是系统画的，不跟 RootGrid.RequestedTheme 走，
        // 而是按系统深浅色决定字形颜色。强制浅色时（系统可能正是深色）字形会保持浅色，
        // 压在浅色标题栏上就看不见了，所以这里跟着主题显式指定。
        private void ApplyTitleBarTheme(bool isDark)
        {
            var titleBar = AppWindow.TitleBar;
            var foreground = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = isDark
                ? Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x80, 0x00, 0x00, 0x00);
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x20, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x40, 0x00, 0x00, 0x00);
        }

        private async void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            var navigationRequestId = ++_shellNavigationRequestId;
            if (!_isChangingShellSelectionInternally)
            {
                PlaySelectionSound();
            }

            var module = _applicationViewModel.FindModuleByTag(tag);
            if (module is not null)
            {
                _applicationViewModel.SelectedModule = module.Key;
            }

            if (module?.Key == ToolboxModuleKey.Settings)
            {
                await PrepareWorkbenchSessionForModuleChangeAsync();
                if (navigationRequestId != _shellNavigationRequestId)
                {
                    return;
                }
                ShowSettingsPage();
                return;
            }

            if (module?.Key == ToolboxModuleKey.AssetLibrary)
            {
                await PrepareWorkbenchSessionForModuleChangeAsync();
                if (navigationRequestId != _shellNavigationRequestId)
                {
                    return;
                }
                ShowAssetLibraryPage();
                return;
            }

            if (module?.Key == ToolboxModuleKey.UnrealSync)
            {
                await PrepareWorkbenchSessionForModuleChangeAsync();
                if (navigationRequestId != _shellNavigationRequestId)
                {
                    return;
                }
                ShowUnrealSyncPage();
                return;
            }

            RestoreRememberedWorkbenchPage();
        }

    }
}
