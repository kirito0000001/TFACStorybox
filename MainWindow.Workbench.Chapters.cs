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
                GridViewItemFactory.CreateMenuItem("AI 剧情填表", async (_, _) => await ShowAiStoryFillWorkflowAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("导入小节", async (_, _) => await ImportStorySectionsFromUiAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("导出章节", async (_, _) => await ExportChapterPackageFromUiAsync(chapter)),
                GridViewItemFactory.CreateMenuItem("导入章节", async (_, _) => await ImportChapterPackageFromUiAsync(chapter)),
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
                // 先过一遍"素材引用身份指纹"（记录 vs 现状）。这块**要用户在窗口里决定**
                //（能按记录修回的 / 记录里名字已经不在的，处理方式不一样），
                // 所以放在自动修复之前，而不是替用户做决定。
                if (await RepairStoryAssetIdentitiesAsync(chapter, assetLibrary) >= 0)
                {
                    // 处理窗口可能改了文档（修回或重记），重新扫一遍才知道最新状态。
                    scanResult = await ShowChapterRepairProgressDialogAsync(
                        "正在检查章节索引",
                        chapter,
                        progress => Task.Run(() => _chapterRepairService.Scan(project, chapter, BuildChapterRepairAssetContext(assetLibrary), repair: false, progress)));
                }

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
                ImageSetService.GetFilePaths(GetBackgroundFolderPath(assetLibrary)).Count,
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

        /// <summary>
        /// 「导出章节」：把这一章的**剧情数据**导成一个编辑包，给另一边的人编辑。
        ///
        /// 【和「备份」不是一回事】
        /// 备份 = 给自己回退用的整目录 zip（含 CSV、AI 填表备份，落点在自己目录下、留 3 份）。
        /// 这个包 = 给**对面的人**用的对接件：只有剧情主数据 + 本章 TODO 备注 + 一份清单，
        /// 不放素材（两边素材一致）。对方编完把它发回来，走「导入章节」做差异对比、选一方合并。
        ///
        /// 包的结构与理由见 <see cref="ChapterPackageService"/>。
        /// </summary>
        private async Task ExportChapterPackageFromUiAsync(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导出章节", "请先打开一个项目。");
                return;
            }

            try
            {
                ChapterInfoBar.IsOpen = false;

                // ① 先把还没落盘的编辑写下去。导出的可能正是**当前打开的这一章**，
                //    而自动保存有 900ms debounce —— 不 flush 的话包里会少最后一拍。
                //    这一步必须在生成包**之前**：包直接搬磁盘上那份文档。
                await FlushChapterEditsBeforePackageAsync(chapter);

                // ② 选保存位置（和项目 / 素材库的「导出」同一套手感）。
                //    文件名是**章节中文名 + 日期序号**（例如 `第六章_20261015_1712.zip`）——
                //    这个包是要发出去的，收件人一眼就知道是哪一章、哪一次导的；
                //    用代号（WHK-M6-00）当文件名反而要靠记忆去对。
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"{SanitizeBackupFileName(chapter.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };
                picker.FileTypeChoices.Add("TFACStorybox 章节编辑包", [".zip"]);
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var packageFile = await picker.PickSaveFileAsync();
                if (packageFile is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(packageFile.Path))
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "章节未导出", "请选择本机文件夹里的保存位置。");
                    return;
                }

                // ③ 生成包。压一个几百 KB 的 zip 是秒级的，不另开进度弹窗
                //    （AI 剧情填表导出也是直接出结果）。
                var projectPath = _currentProject.Path;
                var export = await Task.Run(() => _chapterPackageService.Export(chapter, projectPath, packageFile.Path));
                ShowChapterStatus(
                    InfoBarSeverity.Success,
                    "章节已导出",
                    $"{chapter.Name}（{chapter.Code}）\n"
                    + $"{export.Manifest.RowCount} 行 / {export.Manifest.SectionCount} 个小节 / {export.Manifest.TodoCount} 条备注\n"
                    + "把这个 zip 发给另一边；他编完发回来时用「导入章节」做差异对比。");
                AppendLog(
                    LogKind.User,
                    $"导出章节编辑包：{chapter.Name}（{chapter.Code}）-> {export.PackagePath}"
                    + $"（{export.Manifest.RowCount} 行 / {export.Manifest.SectionCount} 小节 / {export.Manifest.TodoCount} 备注，包号 {export.Manifest.PackageId[..8]}）");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "导出章节失败", ex.Message);
                AppendLog(LogKind.Error, "导出章节失败。", ex);
            }
        }

        /// <summary>
        /// 导出 / 导入章节包之前，把"还没落盘的编辑"写下去。
        ///
        /// - 故事文档：只有"要处理的正好是**当前打开的那一章**"才需要（保存走 900ms debounce 的
        ///   <c>StorySaveCoordinator</c>，"改完最后一句 → 立刻导出"时磁盘上还是上一拍的样子）。
        /// - TODO 备注：项目级一个文件，无论是哪一章都先落一次；没脏就是空操作。
        /// </summary>
        private async Task FlushChapterEditsBeforePackageAsync(ChapterInfo chapter)
        {
            if (_currentStoryChapter is not null && PathsEqual(_currentStoryChapter.Path, chapter.Path))
            {
                SaveCurrentStoryRow();
                _storyEditSaveTimer?.Stop();
                await _storySaveCoordinator.FlushAsync();
            }

            SaveStoryTodosIfDirty();
        }

        /// <summary>
        /// 「导入章节」：读对面发回来的编辑包，逐句对比，让人挑一边，然后合并。
        ///
        /// 【为什么要有这一步，而不是直接覆盖】两边是在同一份文档上分头改的（他写他的小节、
        /// 我写我的），二方对比分不出"谁改的"，所以把差异全摆出来让人点名 ——
        /// 默认一条都不动本机，弹窗上给了"只收对方的改动 / 全部用对方 / 全部用本机"三个批量档。
        ///
        /// 【合并前自动备份】在 <see cref="ChapterPackageService.ApplyMerge"/> 里做（走现成的
        /// 「备份 / 还原」那一套），所以合并错了随时能在章节右键「还原」里退回来。
        /// </summary>
        private async Task ImportChapterPackageFromUiAsync(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导入章节", "请先打开一个项目。");
                return;
            }

            try
            {
                ChapterInfoBar.IsOpen = false;

                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add(".zip");
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var packageFile = await picker.PickSingleFileAsync();
                if (packageFile is null || string.IsNullOrWhiteSpace(packageFile.Path))
                {
                    return;
                }

                // 差异对比读的是磁盘上那份文档 —— 和导出一样，先把手上的改动落盘。
                await FlushChapterEditsBeforePackageAsync(chapter);

                var projectPath = _currentProject.Path;
                var packagePath = packageFile.Path;
                var (localDocument, plan) = await Task.Run(() =>
                {
                    var package = _chapterPackageService.LoadPackage(packagePath);
                    var document = _storyDocumentRepository.Load(chapter);
                    var todos = StoryTodoStore.Load(projectPath);
                    return (document, _chapterPackageService.BuildMergePlan(chapter, package, document, todos));
                });

                if (!plan.HasAnyChange)
                {
                    ShowChapterStatus(
                        InfoBarSeverity.Informational,
                        "没有差异",
                        $"包里的 {chapter.Name} 和本机这一章内容一致，不需要合并。");
                    return;
                }

                var mergeContent = new ChapterMergeDialogContent(plan);
                var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                    $"导入章节：{chapter.Name}（{chapter.Code}）",
                    mergeContent.Content,
                    mergeContent.PrimaryButtonText,
                    "取消",
                    // 默认落在"取消"上：合并会改本机数据，不该让一个回车就执行。
                    DefaultButton: ContentDialogButton.Close,
                    ConfigureDialog: dialog =>
                    {
                        mergeContent.SelectionChanged += () => dialog.PrimaryButtonText = mergeContent.PrimaryButtonText;
                        dialog.PrimaryButtonClick += (_, args) =>
                        {
                            if (mergeContent.SelectedCount == 0)
                            {
                                args.Cancel = true;
                                mergeContent.ShowValidation();
                            }
                        };
                    }));
                if (result != DialogResultKind.Primary || mergeContent.SelectedCount == 0)
                {
                    return;
                }

                var apply = await Task.Run(() => _chapterPackageService.ApplyMerge(chapter, plan, localDocument, projectPath));

                // 刷新界面：章节卡（上次编辑时间变了）；如果这一章正开着，编辑器整份重新载入
                //（行可能被插进 / 删掉了，沿用「索引同步之后刷新」那一套顺序）。
                LoadChapters(_currentProject);
                if (_currentStoryChapter is not null && PathsEqual(_currentStoryChapter.Path, chapter.Path))
                {
                    LoadStoryDocument(chapter);
                    SynchronizeStorySectionState();
                    ClearStoryUndoStack();
                    LoadStoryTodos();
                    _currentStoryRowIndex = Math.Clamp(_currentStoryRowIndex, 0, Math.Max(0, _storyRows.Count - 1));
                    _storyEditorViewModel.RefreshCommandStates();
                    if (StoryEditorPage.Visibility == Visibility.Visible)
                    {
                        RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
                        LoadStoryRowIntoUi();
                    }
                }

                ShowChapterStatus(
                    InfoBarSeverity.Success,
                    "章节已合并",
                    $"{chapter.Name}（{chapter.Code}）\n"
                    + $"改了 {apply.UpdatedRowCount} 句 · 插入 {apply.InsertedRowCount} 句 · 删除 {apply.DeletedRowCount} 句 · 备注 {apply.UpdatedTodoCount} 条\n"
                    + "合并前的备份已保留，可用右键「还原」退回。");
                AppendLog(
                    LogKind.User,
                    $"导入章节编辑包：{chapter.Name}（{chapter.Code}）<- {packagePath}"
                    + $"（改 {apply.UpdatedRowCount} / 插 {apply.InsertedRowCount} / 删 {apply.DeletedRowCount} / 备注 {apply.UpdatedTodoCount}，合并前备份 {apply.BackupPath}）");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "导入章节失败", ex.Message);
                AppendLog(LogKind.Error, "导入章节失败。", ex);
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
