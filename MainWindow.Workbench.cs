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
        private void LoadAllCards()
        {
            LoadAssetLibraries();
            LoadProjects();
            LoadAssetLibraryOptions();
        }

        private List<AssetLibraryInfo> GetAssetLibraries()
        {
            return _projectWorkspaceService.GetAssetLibraries(_projectRootPath);
        }

        private List<ProjectInfo> GetProjects()
        {
            return _projectWorkspaceService.GetProjects(_projectRootPath);
        }

        private void LoadProjects()
        {
            ProjectsGridView.Items.Clear();

            foreach (var project in GetProjects())
            {
                ProjectsGridView.Items.Add(CreateProjectCard(project));
            }

            ProjectsGridView.Items.Add(CreateAddCard("新建项目", "创建项目文件夹", AddProjectCard_Tapped));
        }

        private void LoadAssetLibraries()
        {
            AssetLibrariesGridView.Items.Clear();

            foreach (var assetLibrary in GetAssetLibraries())
            {
                AssetLibrariesGridView.Items.Add(CreateAssetLibraryCard(assetLibrary));
            }

            AssetLibrariesGridView.Items.Add(CreateAddCard("新建素材库", "创建素材合集", AddAssetLibraryCard_Tapped));
        }

        private void LoadAssetLibraryOptions()
        {
            var selectedFolderName = (ProjectAssetLibraryComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            ProjectAssetLibraryComboBox.Items.Clear();

            foreach (var assetLibrary in GetAssetLibraries().OrderBy(library => library.Name))
            {
                var item = new ComboBoxItem
                {
                    Content = assetLibrary.Name,
                    Tag = assetLibrary.FolderName
                };
                ProjectAssetLibraryComboBox.Items.Add(item);

                if (assetLibrary.FolderName == selectedFolderName)
                {
                    ProjectAssetLibraryComboBox.SelectedItem = item;
                }
            }

            if (ProjectAssetLibraryComboBox.SelectedItem is null && ProjectAssetLibraryComboBox.Items.Count > 0)
            {
                ProjectAssetLibraryComboBox.SelectedIndex = 0;
            }
        }

        private GridViewItem CreateProjectCard(ProjectInfo project)
        {
            return DashboardCardFactory.CreateInfoCard(
                project,
                project.ThumbnailPath,
                project.Name,
                $"{project.Code} | 素材库：{project.AssetLibraryName}",
                $"上次打开时间 {project.LastEditedAt:yyyy-MM-dd HH:mm}",
                ProjectCard_Tapped,
                GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("重命名", async (_, _) => await RenameProjectAsync(project)),
                GridViewItemFactory.CreateMenuItem("更改目标素材库", async (_, _) => await ChangeProjectAssetLibraryAsync(project)),
                GridViewItemFactory.CreateMenuItem("打开文件夹", (_, _) => OpenFolderInExplorer(project.Path)),
                GridViewItemFactory.CreateMenuItem("导出", async (_, _) => await ExportProjectFromUiAsync(project)),
                GridViewItemFactory.CreateMenuItem("备份", async (_, _) => await BackupProjectFromUiAsync(project)),
                GridViewItemFactory.CreateMenuItem("还原", async (_, _) => await RestoreProjectFromUiAsync(project)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteProjectAsync(project))));
        }

        private GridViewItem CreateAssetLibraryCard(AssetLibraryInfo assetLibrary)
        {
            return DashboardCardFactory.CreateInfoCard(
                assetLibrary,
                assetLibrary.ThumbnailPath,
                assetLibrary.Name,
                "素材合集",
                $"上次编辑时间 {assetLibrary.LastEditedAt:yyyy-MM-dd HH:mm}",
                AssetLibraryCard_Tapped,
                GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("重命名", async (_, _) => await RenameAssetLibraryAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("打开文件夹", (_, _) => OpenFolderInExplorer(assetLibrary.Path)),
                GridViewItemFactory.CreateMenuItem("导出", async (_, _) => await ExportAssetLibraryFromUiAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("备份", async (_, _) => await BackupAssetLibraryFromUiAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("还原", async (_, _) => await RestoreAssetLibraryFromUiAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteAssetLibraryAsync(assetLibrary))));
        }

        private void OpenFolderInExplorer(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }

        private async Task BackupProjectFromUiAsync(ProjectInfo project)
        {
            try
            {
                var note = await ShowBackupNoteDialogAsync("备份项目", project.Name);
                if (note is null)
                {
                    return;
                }

                var backup = await ShowFolderBackupProgressDialogAsync(
                    "正在备份项目",
                    project.Name,
                    progress => Task.Run(() => _folderBackupService.CreateBackup(project.Path, ProjectBackupsFolderName, project.Code, note, progress, GetGlobalProgressCancellationToken())));
                LoadProjects();
                RequestDelayedRefresh();
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "项目已备份",
                    backup.DisplayName,
                    logMessage: $"备份项目：{project.Name} -> {backup.Path}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "项目备份失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "备份项目失败。");
            }
        }

        private async Task ExportProjectFromUiAsync(ProjectInfo project)
        {
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"{SanitizeBackupFileName(project.Code)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };
                picker.FileTypeChoices.Add("TFACStorybox 项目包", [".zip"]);
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

                var exportFile = await picker.PickSaveFileAsync();
                if (exportFile is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(exportFile.Path))
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "项目未导出",
                        "请选择本机文件夹里的保存位置。",
                        logMessage: "导出项目失败：请选择本机文件夹里的保存位置。");
                    return;
                }

                var export = await ShowFolderBackupProgressDialogAsync(
                    "正在导出项目",
                    project.Name,
                    progress => Task.Run(() => _folderBackupService.ExportToZip(project.Path, exportFile.Path, progress, GetGlobalProgressCancellationToken())));
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "项目已导出",
                    Path.GetFileName(export.Path),
                    logMessage: $"导出项目：{project.Name} -> {export.Path}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "项目导出失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "导出项目失败。");
            }
        }

        private async Task RestoreProjectFromUiAsync(ProjectInfo project)
        {
            try
            {
                var backups = _folderBackupService.GetBackups(project.Path, ProjectBackupsFolderName);
                if (backups.Count == 0)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "没有项目备份",
                        project.Name,
                        logMessage: $"项目没有可还原的备份：{project.Name}");
                    return;
                }

                var selectedBackup = await ShowFolderRestoreDialogAsync("还原项目", project.Name, backups);
                if (selectedBackup is null)
                {
                    return;
                }

                ShowGlobalProgress("还原项目", project.Name);
                UpdateGlobalProgress("正在还原项目备份...", 15, selectedBackup.DisplayName);
                await Task.Run(() => _folderBackupService.Restore(project.Path, ProjectBackupsFolderName, selectedBackup));
                CompleteGlobalProgress("项目还原完成", selectedBackup.DisplayName);
                await HideGlobalProgressAfterDelayAsync();
                LoadProjects();
                RequestDelayedRefresh();
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "项目已还原",
                    selectedBackup.DisplayName,
                    logMessage: $"还原项目：{project.Name} <- {selectedBackup.Path}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("项目还原失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "项目还原失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "还原项目失败。");
            }
        }

        private async Task BackupAssetLibraryFromUiAsync(AssetLibraryInfo assetLibrary)
        {
            try
            {
                var note = await ShowBackupNoteDialogAsync("备份素材库", assetLibrary.Name);
                if (note is null)
                {
                    return;
                }

                var backup = await ShowFolderBackupProgressDialogAsync(
                    "正在备份素材库",
                    assetLibrary.Name,
                    progress => Task.Run(() => _folderBackupService.CreateBackup(assetLibrary.Path, AssetLibraryBackupsFolderName, assetLibrary.FolderName, note, progress, GetGlobalProgressCancellationToken())));
                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
                RequestDelayedRefresh();
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "素材库已备份",
                    backup.DisplayName,
                    logMessage: $"备份素材库：{assetLibrary.Name} -> {backup.Path}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "素材库备份失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "备份素材库失败。");
            }
        }

        private async Task ExportAssetLibraryFromUiAsync(AssetLibraryInfo assetLibrary)
        {
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"{SanitizeBackupFileName(assetLibrary.FolderName)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };
                picker.FileTypeChoices.Add("TFACStorybox 素材库包", [".zip"]);
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

                var exportFile = await picker.PickSaveFileAsync();
                if (exportFile is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(exportFile.Path))
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "素材库未导出",
                        "请选择本机文件夹里的保存位置。",
                        logMessage: "导出素材库失败：请选择本机文件夹里的保存位置。");
                    return;
                }

                var export = await ShowFolderBackupProgressDialogAsync(
                    "正在导出素材库",
                    assetLibrary.Name,
                    progress => Task.Run(() => _folderBackupService.ExportToZip(assetLibrary.Path, exportFile.Path, progress, GetGlobalProgressCancellationToken())));
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "素材库已导出",
                    Path.GetFileName(export.Path),
                    logMessage: $"导出素材库：{assetLibrary.Name} -> {export.Path}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "素材库导出失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "导出素材库失败。");
            }
        }

        private async Task RestoreAssetLibraryFromUiAsync(AssetLibraryInfo assetLibrary)
        {
            try
            {
                var backups = _folderBackupService.GetBackups(assetLibrary.Path, AssetLibraryBackupsFolderName);
                if (backups.Count == 0)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "没有素材库备份",
                        assetLibrary.Name,
                        logMessage: $"素材库没有可还原的备份：{assetLibrary.Name}");
                    return;
                }

                var selectedBackup = await ShowFolderRestoreDialogAsync("还原素材库", assetLibrary.Name, backups);
                if (selectedBackup is null)
                {
                    return;
                }

                ShowGlobalProgress("还原素材库", assetLibrary.Name);
                UpdateGlobalProgress("正在还原素材库备份...", 15, selectedBackup.DisplayName);
                await Task.Run(() => _folderBackupService.Restore(assetLibrary.Path, AssetLibraryBackupsFolderName, selectedBackup));
                CompleteGlobalProgress("素材库还原完成", selectedBackup.DisplayName);
                await HideGlobalProgressAfterDelayAsync();
                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
                RequestDelayedRefresh();
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "素材库已还原",
                    selectedBackup.DisplayName,
                    logMessage: $"还原素材库：{assetLibrary.Name} <- {selectedBackup.Path}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("素材库还原失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "素材库还原失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "还原素材库失败。");
            }
        }

        private GridViewItem CreateAddCard(string title, string subtitle, TappedEventHandler tappedHandler)
        {
            return DashboardCardFactory.CreateAddCard(title, subtitle, tappedHandler);
        }

        private async Task LoadStoryPreviewImageAsync(
            Image image,
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var bitmap = await GetCachedStoryPreviewImageAsync(imagePath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                image.Source = bitmap;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                image.Source = ThumbnailFactory.CreateDefaultBitmap();
            }
        }

        private async Task<BitmapImage> GetCachedStoryPreviewImageAsync(
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_storyPreviewImageCache.TryGetValue(imagePath, out var cached))
            {
                return cached;
            }

            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 1280
            };
            await bitmap.SetSourceAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            while (_storyPreviewImageCache.Count >= MaxStoryPreviewImageCacheCount &&
                   _storyPreviewImageCacheOrder.TryDequeue(out var oldestPath))
            {
                _storyPreviewImageCache.Remove(oldestPath);
            }

            _storyPreviewImageCache[imagePath] = bitmap;
            _storyPreviewImageCacheOrder.Enqueue(imagePath);
            return bitmap;
        }

        private void AddProjectCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PlayPositiveSound();
            ShowCreateProjectPage();
            AppendLog(LogKind.User, "打开创建项目页面。");
            e.Handled = true;
        }

        private void AddAssetLibraryCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PlayPositiveSound();
            ShowCreateAssetLibraryPage();
            AppendLog(LogKind.User, "打开创建素材库页面。");
            e.Handled = true;
        }

        private void ProjectCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is GridViewItem { Tag: ProjectInfo project })
            {
                PlaySelectionSound();
                TouchProjectLastEditedAt(project);
                ShowProjectDetailPage(_projectWorkspaceService.ReadProjectInfo(project.Path));
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"打开项目：{project.Name}");
                e.Handled = true;
            }
        }

        private void AssetLibraryCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is GridViewItem { Tag: AssetLibraryInfo assetLibrary })
            {
                PlaySelectionSound();
                TouchAssetLibraryLastEditedAt(assetLibrary);
                ShowAssetLibraryDetailPage(assetLibrary);
                AppendLog(LogKind.User, $"打开素材库：{assetLibrary.Name}");
                e.Handled = true;
            }
        }

        private void ShowProjectDetailPage(ProjectInfo project)
        {
            _workbenchReturnPage = WorkbenchReturnPage.ProjectDetail;
            StopStoryEditorAudio();
            _currentProject = project;
            Directory.CreateDirectory(GetChaptersFolderPath(project));
            ProjectDetailTabTitleText.Text = $"{project.Name} / {project.Code}";
            ProjectDetailInfoText.Text = CreateProjectDetailInfoText(project);
            ProjectDetailNameTextBox.Text = project.Name;
            ProjectDetailCodeTextBox.Text = project.Code;
            LoadChapters(project);
            LoadProjectTextPanels(project);
            ProjectDetailInfoText.Text = CreateProjectDetailInfoText(project, CountProjectStoryCharacters());

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Visible;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(ProjectDetailPage);
            ProjectDetailCloseButton.Focus(FocusState.Programmatic);
        }

        private static string CreateProjectDetailInfoText(ProjectInfo project, int? storyCharacterCount = null)
        {
            var storyText = storyCharacterCount is null
                ? string.Empty
                : $" | 剧情总字数：{storyCharacterCount.Value:N0}";
            return $"项目名字：{project.Name} | 英文代号：{project.Code} | 关联素材库：{project.AssetLibraryName} | 上次打开时间：{project.LastEditedAt:yyyy-MM-dd HH:mm}{storyText}";
        }

        private void CloseProjectDetailButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            ShowWorkbenchPage();
        }

        private async void SaveProjectInlineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            PlayPositiveSound();
            try
            {
                await SaveProjectSettingsAsync(
                    _currentProject,
                    ProjectDetailNameTextBox.Text.Trim(),
                    ProjectDetailCodeTextBox.Text.Trim());
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "保存项目设置失败", ex.Message);
                AppendLog(LogKind.Error, "保存项目设置失败。", ex);
            }
        }

        private async void ProjectDetailChangeAssetLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            await ChangeProjectAssetLibraryAsync(_currentProject);
            if (Directory.Exists(_currentProject.Path))
            {
                ShowProjectDetailPage(_projectWorkspaceService.ReadProjectInfo(_currentProject.Path));
            }
        }

        private void LoadChapters(ProjectInfo project)
        {
            List<ChapterInfo> chapters;
            try
            {
                var chaptersFolderPath = GetChaptersFolderPath(project);
                Directory.CreateDirectory(chaptersFolderPath);
                chapters = _projectWorkspaceService.GetChapters(project)
                    .OrderBy(chapter => chapter.Code)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "加载章节卡失败。", ex);
                return;
            }

            ChaptersGridView.Items.Clear();
            foreach (var chapter in chapters)
            {
                ChaptersGridView.Items.Add(CreateChapterCard(chapter));
            }

            ChaptersGridView.Items.Add(CreateAddCard("新建章节", "创建故事章节", AddChapterCard_Tapped));
            AppendLog(LogKind.Info, $"已加载章节卡：{chapters.Count} 个。");
        }

        private void LoadProjectTextPanels(ProjectInfo project)
        {
            var chapters = _projectWorkspaceService.GetChapters(project)
                .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _projectTextRows.Clear();
            _projectTextRows.AddRange(_projectTextDataService.LoadTextRows(project, chapters));
            _projectVoiceMapState = _projectTextDataService.ReadVoiceMap(project);
            _projectLocalizationState = _projectTextDataService.ReadLocalization(project);
            if (_selectedVoiceChapter is null || !chapters.Any(chapter => PathsEqual(chapter.Path, _selectedVoiceChapter.Path)))
            {
                _selectedVoiceChapter = chapters.FirstOrDefault();
            }

            if (_selectedTextToolChapter is null || !chapters.Any(chapter => PathsEqual(chapter.Path, _selectedTextToolChapter.Path)))
            {
                _selectedTextToolChapter = _selectedVoiceChapter;
            }

            RefreshProjectTextEntryCards(chapters);
        }

        private void RefreshProjectTextEntryCards(IReadOnlyList<ChapterInfo> chapters)
        {
            ProjectVoiceCardsGridView.Items.Clear();
            ProjectLocalizationCardsGridView.Items.Clear();
            var textCount = _projectTextRows.Count;
            ProjectVoiceSummaryText.Text = chapters.Count == 0
                ? "新建章节后会同步生成语音入口卡。"
                : $"已聚合 {chapters.Count} 个章节、{textCount} 行文本。点击卡片进入语音映射页面。";
            ProjectLocalizationSummaryText.Text = chapters.Count == 0
                ? "新建章节后会同步生成本地化入口卡。"
                : $"当前语言 {_selectedLocalizationLanguage}，已聚合 {textCount} 行文本。点击卡片进入本地化页面。";

            foreach (var chapter in chapters)
            {
                var chapterTextCount = _projectTextRows.Count(row => string.Equals(row.ChapterCode, chapter.Code, StringComparison.OrdinalIgnoreCase));
                ProjectVoiceCardsGridView.Items.Add(CreateProjectTextEntryCard(
                    chapter,
                    "语音",
                    $"{chapter.Code} | 文本 {chapterTextCount} 行",
                    () => OpenProjectTextTool(ProjectTextToolMode.Voice, chapter)));
                ProjectLocalizationCardsGridView.Items.Add(CreateProjectTextEntryCard(
                    chapter,
                    "本地化",
                    $"{chapter.Code} | {_selectedLocalizationLanguage} | 文本 {chapterTextCount} 行",
                    () => OpenProjectTextTool(ProjectTextToolMode.Localization, chapter)));
            }
        }

        private int CountProjectStoryCharacters()
        {
            return ProjectTextDataService.CountStoryCharacters(_projectTextRows);
        }

        private GridViewItem CreateProjectTextEntryCard(ChapterInfo chapter, string titleSuffix, string subtitle, Action open)
        {
            return DashboardCardFactory.CreateInfoCard(
                chapter,
                null,
                $"{chapter.Name} {titleSuffix}",
                subtitle,
                $"上次编辑时间 {chapter.LastEditedAt:yyyy-MM-dd HH:mm}",
                (_, _) =>
                {
                    PlaySelectionSound();
                    open();
                });
        }

    }
}
