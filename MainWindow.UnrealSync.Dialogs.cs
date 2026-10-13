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
        // ShowUnrealBackupDialogAsync 已删除（2026-10-12）：同步前的回退点从
        // "问要不要打 zip" 改成"直接做一次全量本地提交"，不再需要询问。
        // 见 Services/UnrealSync/UnrealSyncGitCommitService。

        // ShowRunningUnrealEditorDialogAsync 已删除（2026-10-12）：
        // 它建议"先关闭正在运行的编辑器"，而在线执行上线之后那条建议正好是反的 ——
        // 编辑器开着才是我们要的（在线走的就是它，快约十倍）。
        // 真连不上时 RunWithOfflineFallbackAsync 自己会退回离线并留痕，不需要问用户。
        // 配套的 CloseRunningUnrealEditors 也一起删了 —— 它的唯一调用点就在那段里。

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
