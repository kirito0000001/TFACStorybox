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
        private static void OpenUnrealProject(UnrealSyncContext context)
        {
            var editorPath = ResolveUnrealInteractiveEditorExecutable(context.EditorPath);
            var processStartInfo = new ProcessStartInfo
            {
                FileName = editorPath,
                UseShellExecute = false
            };
            processStartInfo.ArgumentList.Add(context.UnrealProjectPath);
            Process.Start(processStartInfo);
        }

        private static void OpenUnrealLogFolder(UnrealSyncContext context)
        {
            var logsFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "Logs");
            Directory.CreateDirectory(logsFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = logsFolder,
                UseShellExecute = true
            });
        }

        private static string ResolveUnrealInteractiveEditorExecutable(string editorPath)
        {
            if (Path.GetFileName(editorPath).Equals("UnrealEditor-Cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                var interactivePath = Path.Combine(Path.GetDirectoryName(editorPath)!, "UnrealEditor.exe");
                return File.Exists(interactivePath) ? interactivePath : editorPath;
            }

            return editorPath;
        }

        // GetRunningUnrealEditorProcesses / CloseRunningUnrealEditors 已删除（2026-10-12）：
        // 它们唯一的用途是支撑那个「建议先关闭编辑器」的对话框，而在线执行上线之后
        // 那条建议正好是反的 —— 编辑器开着才是我们要的（在线走的就是它，快约十倍）。
        // 那段 WMI 查询本来也只是为了"别误杀别的工程"，现在连杀都不杀了。

        // CreateUnrealSyncBackup 已删除（2026-10-12）：同步前的回退点改成
        // 「全量本地提交」，见 Services/UnrealSync/UnrealSyncGitCommitService。
        // zip 那套的路径助手 GetUnrealBackupFolder 也一并删了。

    }
}
