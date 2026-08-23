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
        private GridViewItem CreateChapterCard(ChapterInfo chapter)
        {
            var typeName = ChapterTypes.Options.FirstOrDefault(option => option.Kind == chapter.Type)?.DisplayName ?? chapter.Type;
            return DashboardCardFactory.CreateInfoCard(
                chapter,
                null,
                chapter.Name,
                $"{typeName} | {chapter.Code}",
                $"上次编辑时间 {chapter.LastEditedAt:yyyy-MM-dd HH:mm}",
                (_, _) => OpenStoryEditorFromUi(chapter),
                GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("修改", async (_, _) => await EditChapterFromUiAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("选项与分支", async (_, _) => await ShowChapterChoiceManagerAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("导入小节", async (_, _) => await ImportStorySectionsFromUiAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("备份", async (_, _) => await BackupChapterFromUiAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("还原", async (_, _) => await RestoreChapterFromUiAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("修复", async (_, _) => await RepairChapterIndexesFromUiAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteChapterFromUiAsync(chapter))));
        }

        private async void AddChapterButton_Click(object sender, RoutedEventArgs e)
        {
            await CreateChapterFromUiAsync();
        }

        private async void AddChapterCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            await CreateChapterFromUiAsync();
        }

        private async Task CreateChapterFromUiAsync()
        {
            if (_isCreatingChapter)
            {
                return;
            }

            _isCreatingChapter = true;
            ChapterInfoBar.IsOpen = false;
            try
            {
                await CreateChapterAsync();
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "创建章节失败", ex.Message);
                AppendLog(LogKind.Error, "创建章节失败。", ex);
            }
            finally
            {
                _isCreatingChapter = false;
            }
        }

        private async Task CreateChapterAsync()
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法创建章节", "请先打开一个项目。");
                return;
            }

            var input = await ShowChapterEditorDialogAsync("创建章节", null);
            if (input is null)
            {
                return;
            }

            try
            {
                var chapter = _projectWorkspaceService.CreateChapter(_currentProject, input);
                _storyDocumentRepository.Save(chapter, StoryDocument.CreateEmpty());
            }
            catch (IOException ex)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法创建章节", ex.Message);
                AppendLog(LogKind.Warning, $"无法创建章节：{ex.Message}");
                return;
            }

            LoadChapters(_currentProject);
            ShowChapterStatus(InfoBarSeverity.Success, "章节已创建", $"{input.Name}（{input.Code}）");
            AppendLog(LogKind.User, $"创建章节：{input.Name}（{input.Code}）");
        }

        private async Task EditChapterFromUiAsync(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                await EditChapterAsync(chapter);
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "修改章节失败", ex.Message);
                AppendLog(LogKind.Error, "修改章节失败。", ex);
            }
        }

        private async Task EditChapterAsync(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法修改章节", "请先打开一个项目。");
                return;
            }

            var input = await ShowChapterEditorDialogAsync("修改章节", chapter);
            if (input is null)
            {
                return;
            }

            try
            {
                var oldExportNames = _storyCsvService.GetLocalSectionCsvPaths(chapter)
                    .Select(file => Path.GetFileName(file.Path))
                    .ToList();
                var updatedChapter = _projectWorkspaceService.UpdateChapter(_currentProject, chapter, input);
                _storyDocumentRepository.UpdateChapterCode(updatedChapter);
                foreach (var oldExportName in oldExportNames)
                {
                    File.Delete(Path.Combine(updatedChapter.Path, oldExportName));
                }
            }
            catch (IOException ex)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法修改章节", ex.Message);
                AppendLog(LogKind.Warning, $"无法修改章节：{ex.Message}");
                return;
            }

            LoadChapters(_currentProject);
            ShowChapterStatus(InfoBarSeverity.Success, "章节已修改", $"{input.Name}（{input.Code}）");
            AppendLog(LogKind.User, $"修改章节：{chapter.Code} -> {input.Code}");
        }

        private async Task DeleteChapterFromUiAsync(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                await DeleteChapterAsync(chapter);
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "删除章节失败", ex.Message);
                AppendLog(LogKind.Error, "删除章节失败。", ex);
            }
        }

        private async Task DeleteChapterAsync(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法删除章节", "请先打开一个项目。");
                return;
            }

            var confirmed = await ShowDeleteConfirmDialogAsync("删除章节", $"确定删除章节 {chapter.Name}（{chapter.Code}）吗？这会删除整个章节文件夹。");
            if (!confirmed)
            {
                return;
            }

            _projectWorkspaceService.DeleteChapter(chapter);
            LoadChapters(_currentProject);
            ShowChapterStatus(InfoBarSeverity.Success, "章节已删除", $"{chapter.Name}（{chapter.Code}）");
            AppendLog(LogKind.User, $"删除章节：{chapter.Name}（{chapter.Code}）");
        }

        private async Task BackupChapterFromUiAsync(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                var note = await ShowBackupNoteDialogAsync("备份章节", $"{chapter.Name}（{chapter.Code}）");
                if (note is null)
                {
                    return;
                }

                var backup = await ShowFolderBackupProgressDialogAsync(
                    "正在备份章节",
                    $"{chapter.Name}（{chapter.Code}）",
                    progress => Task.Run(() => _folderBackupService.CreateBackup(chapter.Path, ChapterBackupsFolderName, chapter.Code, note, progress, GetGlobalProgressCancellationToken())));
                ShowChapterStatus(InfoBarSeverity.Success, "章节已备份", $"{chapter.Name}（{chapter.Code}）\n{backup.DisplayName}");
                AppendLog(LogKind.User, $"备份章节：{chapter.Name}（{chapter.Code}）-> {backup.Path}");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "备份章节失败", ex.Message);
                AppendLog(LogKind.Error, "备份章节失败。", ex);
            }
        }

        private async Task RestoreChapterFromUiAsync(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                if (_currentProject is null)
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "无法还原章节", "请先打开一个项目。");
                    return;
                }

                var backups = _folderBackupService.GetBackups(chapter.Path, ChapterBackupsFolderName);
                if (backups.Count == 0)
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "没有可还原的备份", "这个章节还没有创建过备份。");
                    return;
                }

                var selectedBackup = await ShowFolderRestoreDialogAsync("还原章节", $"{chapter.Name}（{chapter.Code}）", backups);
                if (selectedBackup is null)
                {
                    return;
                }

                _folderBackupService.Restore(chapter.Path, ChapterBackupsFolderName, selectedBackup);
                LoadChapters(_currentProject);
                ShowChapterStatus(InfoBarSeverity.Success, "章节已还原", $"{chapter.Name}（{chapter.Code}）\n{selectedBackup.DisplayName}");
                AppendLog(LogKind.User, $"还原章节：{chapter.Name}（{chapter.Code}）<- {selectedBackup.Path}");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "还原章节失败", ex.Message);
                AppendLog(LogKind.Error, "还原章节失败。", ex);
            }
        }

        private async Task RepairChapterIndexesFromUiAsync(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                var project = FindProjectForChapter(chapter);
                if (project is null)
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "无法修复章节", "没有找到这个章节所属的工作台项目。");
                    return;
                }

                var assetLibrary = ResolveProjectAssetLibrary(project);
                if (assetLibrary is null)
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "无法修复章节", "这个章节所属项目没有可用的绑定素材库。");
                    return;
                }

                var scanResult = await ShowChapterRepairProgressDialogAsync(
                    "正在检查章节索引",
                    chapter,
                    progress => Task.Run(() => _chapterRepairService.Scan(project, chapter, BuildChapterRepairAssetContext(assetLibrary), repair: false, progress)));
                if (scanResult.IssueCount == 0)
                {
                    ShowChapterStatus(InfoBarSeverity.Success, "章节索引正常", $"{chapter.Name}（{chapter.Code}）没有发现错开的素材索引。");
                    return;
                }

                var shouldRepair = await ShowChapterRepairResultDialogAsync(scanResult);
                if (!shouldRepair)
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "章节索引有异常", $"发现 {scanResult.IssueCount} 处需要注意的数据，尚未改动。");
                    return;
                }

                var repairResult = await ShowChapterRepairProgressDialogAsync(
                    "正在修复章节索引",
                    chapter,
                    progress => Task.Run(() => _chapterRepairService.Scan(project, chapter, BuildChapterRepairAssetContext(assetLibrary), repair: true, progress)));
                RefreshOpenStoryRowsAfterIndexSync(repairResult.ChangedCsvPaths);
                ShowChapterStatus(
                    repairResult.FixedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    repairResult.FixedCount > 0 ? "章节索引已修复" : "没有可自动修复项",
                    $"发现 {repairResult.IssueCount} 处异常，自动修复 {repairResult.FixedCount} 处。其余项目需要手动确认。");
                AppendLog(LogKind.User, $"修复章节索引：{chapter.Name}（{chapter.Code}），自动修复 {repairResult.FixedCount} 处。");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "修复章节失败", ex.Message);
                AppendLog(LogKind.Error, "修复章节索引失败。", ex);
            }
        }

        private ProjectInfo? FindProjectForChapter(ChapterInfo chapter)
        {
            return _currentProject is not null && IsPathInsideDirectory(chapter.Path, _currentProject.Path)
                ? _currentProject
                : GetProjects().FirstOrDefault(project => IsPathInsideDirectory(chapter.Path, project.Path));
        }

        private ChapterRepairAssetContext BuildChapterRepairAssetContext(AssetLibraryInfo assetLibrary)
        {
            var characters = GetCharactersForAssetLibrary(assetLibrary);
            return ChapterRepairService.BuildAssetContext(
                characters,
                BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(assetLibrary)).Count,
                AudioAssetService.GetFilePaths(GetMusicFolderPath(assetLibrary)).Count,
                AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(assetLibrary)).Count,
                _characterFilterService.Read(assetLibrary).Count);
        }

        private async Task ImportStorySectionsFromUiAsync(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导入小节", "请先打开一个项目。");
                return;
            }

            var csvPaths = await ShowStorySectionImportDialogAsync(chapter);
            if (csvPaths is null || csvPaths.Count == 0)
            {
                return;
            }

            try
            {
                var importedCount = ImportStorySectionCsvFiles(chapter, csvPaths, deleteSourceFiles: false);
                LoadChapters(_currentProject);
                ShowChapterStatus(
                    importedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    importedCount > 0 ? "小节已导入" : "没有导入小节",
                    importedCount > 0 ? $"已导入 {importedCount} 个小节 CSV。" : "选择的 CSV 为空或结构不兼容。");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "导入小节失败", ex.Message);
                AppendLog(LogKind.Error, "导入小节 CSV 失败。", ex);
            }
        }

        private async Task<List<string>?> ShowStorySectionImportDialogAsync(ChapterInfo chapter)
        {
            var selectedPaths = new List<string>();
            ContentDialog? activeDialog = null;

            async Task PickCsvFilesAsync()
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                };
                picker.FileTypeFilter.Add(".csv");
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var selectedFiles = await picker.PickMultipleFilesAsync();
                selectedPaths = selectedFiles
                    .Select(file => file.Path)
                    .Where(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (selectedPaths.Count > 0)
                {
                    activeDialog?.Hide();
                }
            }

            Task AcceptDroppedFilesAsync(IReadOnlyList<string> csvPaths)
            {
                selectedPaths = csvPaths.ToList();
                if (selectedPaths.Count > 0)
                {
                    activeDialog?.Hide();
                }
                return Task.CompletedTask;
            }

            var importContent = StoryDialogContentFactory.CreateStorySectionImportContent(
                PickCsvFilesAsync,
                AcceptDroppedFilesAsync);

            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                $"导入小节：{chapter.Name}",
                importContent.Content,
                string.Empty,
                "取消",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None,
                ConfigureDialog: dialog => activeDialog = dialog));
            return selectedPaths.Count > 0 ? selectedPaths : null;
        }

    }
}
