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
        private async Task<bool?> ShowUnrealBackupDialogAsync(UnrealSyncContext context)
        {
            var backupFolder = GetUnrealBackupFolder(context);
            var result = await _dialogService.ShowAsync(new DialogRequest(
                "同步前备份虚幻项目？",
                $"建议先备份本次同步会影响的 Narrative 目录、Config 和 .uproject 文件。仅保留最近 5 份。\n默认位置：{backupFolder}",
                "备份并同步",
                "取消",
                "直接同步"));
            return result switch
            {
                DialogResultKind.Primary => true,
                DialogResultKind.Secondary => false,
                _ => null
            };
        }

        private async Task<bool?> ShowRunningUnrealEditorDialogAsync(UnrealSyncContext context)
        {
            var runningEditors = GetRunningUnrealEditorProcesses(context);
            if (runningEditors.Count == 0)
            {
                return false;
            }

            var processText = string.Join("\n", runningEditors.Select(process => $"{process.ProcessName}  PID {process.Id}"));
            var result = await _dialogService.ShowAsync(new DialogRequest(
                "检测到当前项目正在 Unreal Editor 中运行",
                $"建议先关闭当前绑定项目的虚幻编辑器，再执行同步。其他 Unreal 项目不会被关闭。否则已加载的资产可能不会刷新，后续保存还可能覆盖同步结果。\n\n{processText}",
                "关闭后同步",
                "取消",
                "继续同步"));
            return result switch
            {
                DialogResultKind.Primary => true,
                DialogResultKind.Secondary => false,
                _ => null
            };
        }

        private async Task ShowUnrealSyncFinishedDialogAsync(
            UnrealSyncContext context,
            UnrealSyncResult result,
            UnrealSyncChangePlan changePlan)
        {
            var dialogResult = await _dialogService.ShowAsync(new DialogRequest(
                result.ExitCode == 0 ? "虚幻同步完成" : "虚幻同步已结束",
                result.ExitCode == 0
                    ? $"本次同步处理 {changePlan.TotalChangedItems} 项。现在可以直接打开虚幻项目检查结果。"
                    : $"Unreal Editor 返回退出码 {result.ExitCode}。可以打开项目或查看日志继续确认。",
                "打开虚幻项目",
                "知道了",
                "打开日志目录"));
            if (dialogResult == DialogResultKind.Primary)
            {
                OpenUnrealProject(context);
            }
            else if (dialogResult == DialogResultKind.Secondary)
            {
                OpenUnrealLogFolder(context);
            }
        }

    }
}
