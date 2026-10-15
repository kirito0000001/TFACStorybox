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
        private void ProjectsGridView_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "导入项目包";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        }

        private async void ProjectsGridView_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var archivePaths = items
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Where(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (archivePaths.Count == 0)
                {
                    WorkspaceStatusText.Text = "没有可导入的项目包：请拖入项目导出的 .zip 文件。";
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "没有导入项目",
                        "请拖入项目导出的 .zip 文件。",
                        logMessage: "项目导入失败：拖入内容里没有 .zip 项目包。");
                    return;
                }

                EnsureProjectRootDirectory(_projectRootPath);
                var importedProjects = new List<ProjectInfo>();
                ShowGlobalProgress("导入项目包", $"0 / {archivePaths.Count}");
                for (var index = 0; index < archivePaths.Count; index++)
                {
                    GetGlobalProgressCancellationToken().ThrowIfCancellationRequested();
                    var archivePath = archivePaths[index];
                    var bandStart = archivePaths.Count == 0 ? 0 : index * 100d / archivePaths.Count;
                    var bandSize = archivePaths.Count == 0 ? 100d : 100d / archivePaths.Count;
                    UpdateGlobalProgress(
                        $"正在导入项目包 {index + 1}/{archivePaths.Count}",
                        bandStart,
                        Path.GetFileName(archivePath));
                    // 解压过程按条目报进度（3 GB 的包也看得见动静）。Progress<T> 在 UI 线程建，
                    // 回调会自动回到 UI 线程，所以下面那个 Task.Run 里放心用。
                    var fileProgress = new Progress<FolderBackupProgress>(update =>
                        UpdateGlobalProgress(
                            $"导入项目包 {index + 1}/{archivePaths.Count}：{update.Message}",
                            Math.Min(100, bandStart + update.Percent * bandSize / 100d),
                            Path.GetFileName(archivePath)));
                    importedProjects.Add(await Task.Run(() => _projectWorkspaceService.ImportProjectArchive(
                        _projectRootPath,
                        archivePath,
                        fileProgress,
                        GetGlobalProgressCancellationToken())));
                }
                CompleteGlobalProgress("项目导入完成", $"已导入 {importedProjects.Count} 个项目");
                await HideGlobalProgressAfterDelayAsync();

                LoadProjects();
                RequestDelayedRefresh();
                WorkspaceStatusText.Text = $"已导入 {importedProjects.Count} 个项目：{string.Join("、", importedProjects.Select(project => project.Name).Take(3))}";
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "项目已导入",
                    $"已导入 {importedProjects.Count} 个项目。",
                    logMessage: $"拖动导入项目：{string.Join("；", importedProjects.Select(project => $"{project.Name} -> {project.Path}"))}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "项目导入已取消" : "项目导入失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                WorkspaceStatusText.Text = $"导入项目失败：{ex.Message}";
                if (ex is OperationCanceledException)
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "项目导入已取消", "没有继续写入项目包。", logMessage: "拖动导入项目已取消。");
                }
                else
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        "项目导入失败",
                        "错误详情已复制到剪贴板，并写入 Log。",
                        ex,
                        "拖动导入项目失败。");
                }
            }
        }

        private void AssetLibrariesGridView_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "导入素材库包";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        }

        private async void AssetLibrariesGridView_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var archivePaths = items
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Where(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (archivePaths.Count == 0)
                {
                    AssetLibraryStatusText.Text = "没有可导入的素材库包：请拖入素材库导出的 .zip 文件。";
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "没有导入素材库",
                        "请拖入素材库导出的 .zip 文件。",
                        logMessage: "素材库导入失败：拖入内容里没有 .zip 素材库包。");
                    return;
                }

                EnsureProjectRootDirectory(_projectRootPath);
                var importedLibraries = new List<AssetLibraryInfo>();
                ShowGlobalProgress("导入素材库包", $"0 / {archivePaths.Count}");
                for (var index = 0; index < archivePaths.Count; index++)
                {
                    GetGlobalProgressCancellationToken().ThrowIfCancellationRequested();
                    var archivePath = archivePaths[index];
                    var bandStart = archivePaths.Count == 0 ? 0 : index * 100d / archivePaths.Count;
                    var bandSize = archivePaths.Count == 0 ? 100d : 100d / archivePaths.Count;
                    UpdateGlobalProgress(
                        $"正在导入素材库包 {index + 1}/{archivePaths.Count}",
                        bandStart,
                        Path.GetFileName(archivePath));
                    // 素材库包动辄几个 GB：进度按条目走，不然进度条会一直冻着。
                    var fileProgress = new Progress<FolderBackupProgress>(update =>
                        UpdateGlobalProgress(
                            $"导入素材库包 {index + 1}/{archivePaths.Count}：{update.Message}",
                            Math.Min(100, bandStart + update.Percent * bandSize / 100d),
                            Path.GetFileName(archivePath)));
                    importedLibraries.Add(await Task.Run(() => _projectWorkspaceService.ImportAssetLibraryArchive(
                        _projectRootPath,
                        archivePath,
                        fileProgress,
                        GetGlobalProgressCancellationToken())));
                }
                CompleteGlobalProgress("素材库导入完成", $"已导入 {importedLibraries.Count} 个素材库");
                await HideGlobalProgressAfterDelayAsync();

                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
                RequestDelayedRefresh();
                AssetLibraryStatusText.Text = $"已导入 {importedLibraries.Count} 个素材库：{string.Join("、", importedLibraries.Select(library => library.Name).Take(3))}";
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "素材库已导入",
                    $"已导入 {importedLibraries.Count} 个素材库。",
                    logMessage: $"拖动导入素材库：{string.Join("；", importedLibraries.Select(library => $"{library.Name} -> {library.Path}"))}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "素材库导入已取消" : "素材库导入失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                AssetLibraryStatusText.Text = $"导入素材库失败：{ex.Message}";
                if (ex is OperationCanceledException)
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "素材库导入已取消", "没有继续写入素材库包。", logMessage: "拖动导入素材库已取消。");
                }
                else
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        "素材库导入失败",
                        "错误详情已复制到剪贴板，并写入 Log。",
                        ex,
                        "拖动导入素材库失败。");
                }
            }
        }

        private void ChaptersGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "导入章节 CSV";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        }

        private async void ChaptersGridView_Drop(object sender, DragEventArgs e)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导入 CSV", "请先打开一个项目。");
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var csvPaths = items
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (csvPaths.Count == 0)
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "没有可导入的 CSV", "请把 .csv 文件拖到章节区域。");
                    return;
                }

                var importedCount = 0;
                foreach (var csvPath in csvPaths)
                {
                    importedCount += await ImportChapterCsvAsync(csvPath) ? 1 : 0;
                }

                LoadChapters(_currentProject);
                ShowChapterStatus(InfoBarSeverity.Success, "CSV 已导入", $"已导入 {importedCount} 个章节。导入表会保留原始索引，打开后可逐句检查素材对应关系。");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "导入 CSV 失败", ex.Message);
                AppendLog(LogKind.Error, "拖入导入章节 CSV 失败。", ex);
            }
        }

        private async Task<bool> ImportChapterCsvAsync(string sourceCsvPath)
        {
            if (_currentProject is null || !File.Exists(sourceCsvPath))
            {
                return false;
            }

            var compatibility = _storyCsvService.InspectCompatibility(sourceCsvPath);
            if (!compatibility.IsCompatible)
            {
                await ShowCsvCompatibilityFailedDialogAsync(sourceCsvPath, compatibility);
                return false;
            }

            var suggestedCode = GetUniqueImportedChapterCode(Path.GetFileNameWithoutExtension(sourceCsvPath));
            var importInput = await ShowImportChapterCsvDialogAsync(sourceCsvPath, compatibility, suggestedCode);
            if (importInput is null)
            {
                return false;
            }

            var rows = _storyCsvService.ReadRows(sourceCsvPath);
            if (rows.Count == 0)
            {
                rows.Add(_storyCsvService.CreateDefaultRow());
            }

            var chapterCode = importInput.Code;
            var chapterPath = Path.Combine(GetChaptersFolderPath(_currentProject), SanitizeCharacterFolderName(chapterCode));
            if (Directory.Exists(chapterPath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导入 CSV", $"同名章节代号已存在：{chapterCode}");
                return false;
            }

            var chapter = _projectWorkspaceService.CreateImportedChapter(_currentProject, importInput);
            var document = new StoryDocument();
            for (var index = 0; index < rows.Count; index++)
            {
                rows[index].StableId = StoryRow.CreateStableId();
                rows[index].Set("Name", (index + 1).ToString());
                document.Rows.Add(rows[index]);
                document.RowSections[rows[index].Get("Name")] = 1;
            }

            _storyDocumentRepository.Save(chapter, document);
            AppendLog(LogKind.User, $"导入章节 CSV 为 StoryDocument：{sourceCsvPath} -> {_storyDocumentRepository.GetDocumentPath(chapter)}");
            return true;
        }

        private async Task<ChapterEditorInput?> ShowImportChapterCsvDialogAsync(
            string sourceCsvPath,
            StoryCsvCompatibility compatibility,
            string suggestedCode)
        {
            if (_currentProject is null)
            {
                return null;
            }

            var suggestedName = Path.GetFileNameWithoutExtension(sourceCsvPath);
            if (suggestedName.EndsWith(".story", StringComparison.OrdinalIgnoreCase))
            {
                suggestedName = suggestedName[..^".story".Length];
            }

            var initialChapter = new ChapterInfo(
                suggestedName,
                suggestedCode,
                ChapterKind.MainThread,
                string.Empty,
                DateTime.Now,
                0);

            return await ShowChapterEditorDialogAsync(
                "导入章节 CSV",
                initialChapter,
                StoryDialogContentFactory.CreateStoryCsvCompatibilityContent(sourceCsvPath, compatibility));
        }

        private async Task ShowCsvCompatibilityFailedDialogAsync(string sourceCsvPath, StoryCsvCompatibility compatibility)
        {
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "CSV 结构不兼容",
                StoryDialogContentFactory.CreateStoryCsvCompatibilityContent(sourceCsvPath, compatibility),
                string.Empty,
                "知道了",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None));
        }

        private string GetUniqueImportedChapterCode(string sourceName)
        {
            var code = SanitizeChapterCodeSegment(sourceName);
            if (code.EndsWith(".story", StringComparison.OrdinalIgnoreCase))
            {
                code = code[..^".story".Length];
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                code = $"Imported-{DateTime.Now:yyyyMMddHHmmss}";
            }

            var existingCodes = GetExistingChapterCodes().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var chaptersPath = GetChaptersFolderPath(_currentProject!);
            var uniqueCode = code;
            var suffix = 1;
            while (existingCodes.Contains(uniqueCode) ||
                Directory.Exists(Path.Combine(chaptersPath, SanitizeCharacterFolderName(uniqueCode))))
            {
                uniqueCode = $"{code}-Import{suffix}";
                suffix++;
            }

            return uniqueCode;
        }

    }
}
