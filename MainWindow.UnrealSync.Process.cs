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

        private static List<Process> GetRunningUnrealEditorProcesses()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "UnrealEditor",
                "UnrealEditor-Cmd"
            };

            return Process.GetProcesses()
                .Where(process =>
                {
                    try
                    {
                        return names.Contains(process.ProcessName);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
        }

        private static void CloseRunningUnrealEditors()
        {
            foreach (var process in GetRunningUnrealEditorProcesses())
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

        private static string CreateCleanUnrealProjectBackup(UnrealSyncContext context, CancellationToken cancellationToken = default)
        {
            var unrealProjectRoot = Path.GetDirectoryName(context.UnrealProjectPath)
                ?? throw new InvalidOperationException("无法定位虚幻项目根目录。");
            var backupFolder = GetUnrealBackupFolder(context);
            Directory.CreateDirectory(backupFolder);

            var projectName = Path.GetFileNameWithoutExtension(context.UnrealProjectPath);
            var backupPath = Path.Combine(backupFolder, $"{projectName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".vs",
                "Binaries",
                "DerivedDataCache",
                "Intermediate",
                "Saved"
            };

            using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
            foreach (var filePath in Directory.EnumerateFiles(unrealProjectRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(unrealProjectRoot, filePath);
                var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(excludedFolders.Contains))
                {
                    continue;
                }

                archive.CreateEntryFromFile(filePath, relativePath.Replace('\\', '/'), CompressionLevel.Optimal);
            }

            return backupPath;
        }

    }
}
