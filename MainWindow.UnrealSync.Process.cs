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

        private static List<Process> GetRunningUnrealEditorProcesses(UnrealSyncContext context)
        {
            var result = new List<Process>();
            var projectPath = Path.GetFullPath(context.UnrealProjectPath);
            try
            {
                var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                    ?? throw new InvalidOperationException("当前系统不支持 WMI 进程查询。");
                dynamic locator = Activator.CreateInstance(locatorType)!;
                dynamic services = locator.ConnectServer(".", "root\\cimv2");
                dynamic processes = services.ExecQuery(
                    "SELECT ProcessId, Name, CommandLine FROM Win32_Process " +
                    "WHERE Name = 'UnrealEditor.exe' OR Name = 'UnrealEditor-Cmd.exe'");
                foreach (dynamic processInfo in processes)
                {
                    var commandLine = Convert.ToString(processInfo.CommandLine, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(commandLine) ||
                        !commandLine.Contains(projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        result.Add(Process.GetProcessById(Convert.ToInt32(processInfo.ProcessId, CultureInfo.InvariantCulture)));
                    }
                    catch
                    {
                        // The process may have exited after the WMI snapshot was created.
                    }
                }
            }
            catch
            {
                // Failing closed prevents an unrelated Unreal project from being terminated.
            }

            return result;
        }

        private static void CloseRunningUnrealEditors(UnrealSyncContext context)
        {
            foreach (var process in GetRunningUnrealEditorProcesses(context))
            {
                try
                {
                    if (!process.CloseMainWindow())
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    else if (!process.WaitForExit(8000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // The editor may have already closed or denied access; continue with the remaining processes.
                }
            }
        }

        private static string CreateUnrealSyncBackup(UnrealSyncContext context, CancellationToken cancellationToken = default)
        {
            var unrealProjectRoot = Path.GetDirectoryName(context.UnrealProjectPath)
                ?? throw new InvalidOperationException("无法定位虚幻项目根目录。");
            var backupFolder = GetUnrealBackupFolder(context);
            Directory.CreateDirectory(backupFolder);

            var projectName = Path.GetFileNameWithoutExtension(context.UnrealProjectPath);
            var backupPath = Path.Combine(backupFolder, $"{projectName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
            var filesToBackup = new List<string> { context.UnrealProjectPath };
            var configFolder = Path.Combine(unrealProjectRoot, "Config");
            if (Directory.Exists(configFolder))
            {
                filesToBackup.AddRange(Directory.EnumerateFiles(configFolder, "*", SearchOption.AllDirectories));
            }
            if (Directory.Exists(context.TargetContentFolderPath))
            {
                filesToBackup.AddRange(Directory.EnumerateFiles(context.TargetContentFolderPath, "*", SearchOption.AllDirectories));
            }
            var musicPlayerBlueprintPath = UnrealSyncService.UnrealObjectPathToContentFilePath(
                context,
                UnrealSyncService.MusicPlayerBlueprintAssetPath);
            if (File.Exists(musicPlayerBlueprintPath))
            {
                filesToBackup.Add(musicPlayerBlueprintPath);
            }
            var storyBranchDataTablePath = UnrealSyncService.UnrealObjectPathToContentFilePath(
                context,
                UnrealSyncService.StoryBranchDataTableAssetPath);
            if (File.Exists(storyBranchDataTablePath))
            {
                filesToBackup.Add(storyBranchDataTablePath);
            }

            foreach (var filePath in filesToBackup.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(unrealProjectRoot, filePath);
                archive.CreateEntryFromFile(filePath, relativePath.Replace('\\', '/'), CompressionLevel.Optimal);
            }

            archive.Dispose();
            var retainedBackups = Directory
                .EnumerateFiles(backupFolder, $"{projectName}_*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var oldBackupPath in Directory.EnumerateFiles(backupFolder, $"{projectName}_*.zip", SearchOption.TopDirectoryOnly)
                         .Where(path => !retainedBackups.Contains(path)))
            {
                File.Delete(oldBackupPath);
            }

            return backupPath;
        }

    }
}
