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
