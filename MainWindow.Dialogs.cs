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
        private Style? TryGetTextBlockStyle(string key)
        {
            if (Content is FrameworkElement root &&
                root.Resources.TryGetValue(key, out var localResource) &&
                localResource is Style localStyle)
            {
                return localStyle;
            }

            return Application.Current.Resources.TryGetValue(key, out var appResource) &&
                appResource is Style appStyle
                    ? appStyle
                    : null;
        }

        private async Task<string?> ShowBackupNoteDialogAsync(string title, string targetName)
        {
            var note = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                title,
                "备注（可留空）",
                PlaceholderText: "例如：改对白前、导入小节后",
                PrimaryButtonText: "备份",
                CloseButtonText: "取消",
                Message: targetName,
                Width: 420,
                MaxLength: 80));
            return note is null ? null : NormalizeBackupNote(note);
        }

        private async Task<FolderBackupEntry> ShowFolderBackupProgressDialogAsync(
            string title,
            string targetName,
            Func<IProgress<FolderBackupProgress>, Task<FolderBackupEntry>> backupAction)
        {
            ShowGlobalProgress(title, targetName);
            var progress = new Progress<FolderBackupProgress>(update =>
            {
                var byteText = update.TotalBytes > 0
                    ? $"{FormatFileSize(update.CompletedBytes)} / {FormatFileSize(update.TotalBytes)}"
                    : "统计大小中";
                var fileText = update.TotalFiles > 0
                    ? $"{Math.Min(update.CompletedFiles + 1, update.TotalFiles)} / {update.TotalFiles} 个文件"
                    : "扫描文件中";
                var speedText = FormatProgressSpeed(update.CompletedBytes);
                var remainingText = FormatRemainingTime(update.CompletedBytes, update.TotalBytes);
                var transferText = string.IsNullOrWhiteSpace(speedText)
                    ? byteText
                    : $"{byteText}，{speedText}，剩余 {remainingText}";
                var detail = update.CurrentRelativePath is null
                    ? $"{fileText}，{transferText}"
                    : $"{fileText}，{transferText}\n{update.CurrentRelativePath}";
                UpdateGlobalProgress(update.Message, update.Percent, detail, update.Percent <= 0);
            });

            try
            {
                var result = await backupAction(progress);
                CompleteGlobalProgress("完成", result.DisplayName);
                return result;
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "当前操作已停止。");
                throw;
            }
            catch
            {
                CompleteGlobalProgress("失败", "操作没有完成，请查看日志或错误提示。");
                throw;
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task<AssetIndexSyncResult> ShowAssetIndexSyncProgressDialogAsync(
            string title,
            Func<IProgress<AssetIndexSyncProgress>, Task<AssetIndexSyncResult>> syncAction)
        {
            ShowGlobalProgress(title, "正在扫描关联项目...");
            var progress = new Progress<AssetIndexSyncProgress>(update =>
            {
                var csvText = update.TotalCsvFiles > 0
                    ? $"{Math.Min(update.CompletedCsvFiles + 1, update.TotalCsvFiles)} / {update.TotalCsvFiles} 个 CSV"
                    : "收集 CSV 中";
                var detail = update.CurrentCsvName is null
                    ? $"{csvText}，已变更 {update.ChangeCount} 处，异常 {update.WarningCount} 处"
                    : $"{csvText}，已变更 {update.ChangeCount} 处，异常 {update.WarningCount} 处\n{update.CurrentCsvName}";
                UpdateGlobalProgress(update.Message, update.Percent, detail, update.Percent <= 0);
            });

            try
            {
                var result = await syncAction(progress);
                CompleteGlobalProgress("索引同步检查完成", $"扫描 {result.ScannedCsvCount} 个 CSV，变更 {result.ChangeCount} 处，异常 {result.WarningCount} 处");
                return result;
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "当前操作已停止。");
                throw;
            }
            catch
            {
                CompleteGlobalProgress("失败", "素材索引同步没有完成。");
                throw;
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task ShowAssetIndexSyncResultDialogAsync(AssetIndexSyncResult result)
        {
            if (result.ChangeCount == 0 && result.WarningCount == 0)
            {
                AppendLog(LogKind.Info, $"{result.Title}：已检查 {result.ScannedCsvCount} 个 CSV，没有发现需要更新的索引。");
                return;
            }

            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                result.WarningCount > 0 ? $"{result.Title}：有异常数据" : $"{result.Title}完成",
                DialogContentFactory.CreateAssetIndexSyncResultContent(result),
                "知道了",
                string.Empty,
                DefaultButton: ContentDialogButton.Primary,
                CloseSound: DialogSoundIntent.None));
        }

        private async Task<ChapterRepairResult> ShowChapterRepairProgressDialogAsync(
            string title,
            ChapterInfo chapter,
            Func<IProgress<ChapterRepairProgress>, Task<ChapterRepairResult>> repairAction)
        {
            ShowGlobalProgress(title, $"{chapter.Name}（{chapter.Code}）");
            var progress = new Progress<ChapterRepairProgress>(update =>
            {
                var csvText = update.TotalCsvFiles > 0
                    ? $"{Math.Min(update.CompletedCsvFiles + 1, update.TotalCsvFiles)} / {update.TotalCsvFiles} 个 CSV"
                    : "收集 CSV 中";
                var detail = update.CurrentCsvName is null
                    ? $"{csvText}，发现 {update.IssueCount} 处异常，已修复 {update.FixedCount} 处"
                    : $"{csvText}，发现 {update.IssueCount} 处异常，已修复 {update.FixedCount} 处\n{update.CurrentCsvName}";
                UpdateGlobalProgress(update.Message, update.Percent, detail, update.Percent <= 0);
            });

            try
            {
                var result = await repairAction(progress);
                CompleteGlobalProgress("章节索引检查完成", $"扫描 {result.ScannedCsvCount} 个 CSV，发现 {result.IssueCount} 处，已修复 {result.FixedCount} 处");
                return result;
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "当前操作已停止。");
                throw;
            }
            catch
            {
                CompleteGlobalProgress("失败", "章节索引检查没有完成。");
                throw;
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task<bool> ShowChapterRepairResultDialogAsync(ChapterRepairResult result)
        {
            var dialogResult = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "章节索引检查结果",
                DialogContentFactory.CreateChapterRepairResultContent(result),
                result.AutoFixableCount > 0 ? "自动修复" : string.Empty,
                "取消",
                "只查看",
                result.AutoFixableCount > 0 ? ContentDialogButton.Primary : ContentDialogButton.Secondary));
            return result.AutoFixableCount > 0 && dialogResult == DialogResultKind.Primary;
        }

        private async Task<FolderBackupEntry?> ShowFolderRestoreDialogAsync(string title, string targetName, IReadOnlyList<FolderBackupEntry> backups)
        {
            return await _dialogService.SelectAsync(new SelectionDialogRequest<FolderBackupEntry>(
                title,
                $"选择要还原的备份：{targetName}",
                backups.Select(backup => new SelectionDialogItem<FolderBackupEntry>(backup.DisplayName, backup)).ToList(),
                PrimaryButtonText: "还原",
                CloseButtonText: "取消"));
        }

        private async Task<ChapterEditorInput?> ShowChapterEditorDialogAsync(string title, ChapterInfo? chapter, UIElement? introContent = null)
        {
            if (_currentProject is null)
            {
                return null;
            }

            var editorContent = EditorDialogContentFactory.CreateChapterEditorContent(
                _currentProject,
                chapter,
                introContent,
                BuildChapterCodeSegment,
                ProjectWorkspaceService.GetChapterCodeSegment);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                title,
                editorContent.Content,
                "确定",
                "取消"));
            if (result != DialogResultKind.Primary)
            {
                return null;
            }

            var input = editorContent.ReadInput();
            var segmentCode = editorContent.ReadSegmentCode();
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(segmentCode))
            {
                AppendLog(LogKind.Warning, "章节名称和章节代号不能为空。");
                return null;
            }

            if (!ValidateCardName(input.Code, "章节英文代号"))
            {
                return null;
            }

            return input;
        }

        private string BuildChapterCodeSegment(string chapterKind, string customCode)
        {
            var sanitizedCustom = SanitizeChapterCodeSegment(customCode);
            return chapterKind switch
            {
                ChapterKind.MainThread => string.IsNullOrWhiteSpace(sanitizedCustom)
                    ? $"M{GetNextMainChapterNumber()}-00"
                    : sanitizedCustom,
                ChapterKind.Interlude => string.IsNullOrWhiteSpace(sanitizedCustom)
                    ? $"M{Math.Max(1, GetLastMainChapterNumber())}&L{GetNextInterludeNumber(Math.Max(1, GetLastMainChapterNumber()))}-00"
                    : sanitizedCustom,
                ChapterKind.Simulation => string.IsNullOrWhiteSpace(sanitizedCustom)
                    ? "ST&Custom-1"
                    : (sanitizedCustom.StartsWith("ST&", StringComparison.OrdinalIgnoreCase) ? sanitizedCustom : $"ST&{sanitizedCustom}"),
                ChapterKind.EventActivity => string.IsNullOrWhiteSpace(sanitizedCustom)
                    ? "EA-Custom-00"
                    : (sanitizedCustom.StartsWith("EA-", StringComparison.OrdinalIgnoreCase) ? sanitizedCustom : $"EA-{sanitizedCustom}"),
                ChapterKind.WorldDialog => string.IsNullOrWhiteSpace(sanitizedCustom)
                    ? "W1-World-00"
                    : (sanitizedCustom.StartsWith("W", StringComparison.OrdinalIgnoreCase) ? sanitizedCustom : $"W1-{sanitizedCustom}"),
                ChapterKind.Minecraft => string.IsNullOrWhiteSpace(sanitizedCustom)
                    ? "MI-Custom-00"
                    : (sanitizedCustom.StartsWith("MI-", StringComparison.OrdinalIgnoreCase) ? sanitizedCustom : $"MI-{sanitizedCustom}"),
                _ => string.IsNullOrWhiteSpace(sanitizedCustom) ? $"M{GetNextMainChapterNumber()}-00" : sanitizedCustom
            };
        }

        private int GetNextMainChapterNumber()
        {
            return GetExistingChapterCodes()
                .Select(code => Regex.Match(code, @"-M(?<index>\d+)(?:-\d+)?$", RegexOptions.IgnoreCase))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups["index"].Value))
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        private int GetLastMainChapterNumber()
        {
            return GetExistingChapterCodes()
                .Select(code => Regex.Match(code, @"-M(?<index>\d+)(?:-\d+)?$", RegexOptions.IgnoreCase))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups["index"].Value))
                .DefaultIfEmpty(1)
                .Max();
        }

        private int GetNextInterludeNumber(int mainChapterNumber)
        {
            return GetExistingChapterCodes()
                .Select(code => Regex.Match(code, $@"-M{mainChapterNumber}&L(?<index>\d+)(?:-\d+)?$", RegexOptions.IgnoreCase))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups["index"].Value))
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        private List<string> GetExistingChapterCodes()
        {
            if (_currentProject is null)
            {
                return [];
            }

            return _projectWorkspaceService.GetChapters(_currentProject)
                .Select(chapter => chapter.Code)
                .ToList();
        }

        private void UpdateChapterProjectCodePrefix(string projectPath, string oldProjectCode, string newProjectCode)
        {
            var projectBeforeRename = _projectWorkspaceService.ReadProjectInfo(projectPath);
            var oldExports = _projectWorkspaceService.GetChapters(projectBeforeRename)
                .ToDictionary(
                    chapter => chapter.Code,
                    chapter => _storyCsvService.GetLocalSectionCsvPaths(chapter)
                        .Select(file => Path.GetFileName(file.Path))
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
            var renamedCount = _projectWorkspaceService.UpdateChapterProjectCodePrefix(projectPath, oldProjectCode, newProjectCode);
            var updatedProject = _projectWorkspaceService.ReadProjectInfo(projectPath);
            foreach (var chapter in _projectWorkspaceService.GetChapters(updatedProject))
            {
                _storyDocumentRepository.UpdateChapterCode(chapter);
                var oldCode = oldExports.Keys.FirstOrDefault(code =>
                    string.Equals(
                        code.Replace(oldProjectCode, newProjectCode, StringComparison.OrdinalIgnoreCase),
                        chapter.Code,
                        StringComparison.OrdinalIgnoreCase));
                if (oldCode is null)
                {
                    continue;
                }

                foreach (var oldExportName in oldExports[oldCode])
                {
                    File.Delete(Path.Combine(chapter.Path, oldExportName));
                }
            }
            if (renamedCount > 0)
            {
                AppendLog(LogKind.User, $"同步章节项目代号前缀：{oldProjectCode} -> {newProjectCode}，共 {renamedCount} 个章节。");
            }
        }

        private static string SanitizeChapterCodeSegment(string code)
        {
            var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
            return new string(code.Trim().Where(ch => !invalidChars.Contains(ch) && !char.IsWhiteSpace(ch)).ToArray());
        }

        private void TouchProjectLastEditedAt(ProjectInfo project)
        {
            _projectWorkspaceService.TouchProjectLastEditedAt(project);
        }

        private void TouchAssetLibraryLastEditedAt(AssetLibraryInfo assetLibrary)
        {
            _projectWorkspaceService.TouchAssetLibraryLastEditedAt(assetLibrary);
        }

        private async Task RenameProjectAsync(ProjectInfo project)
        {
            var newName = await ShowNameInputDialogAsync("重命名项目", "项目名称", project.Name);
            if (newName is null)
            {
                return;
            }

            if (!ValidateCardName(newName, "项目名称"))
            {
                return;
            }

            try
            {
                _projectWorkspaceService.RenameProject(_projectRootPath, project, newName);
            }
            catch (IOException ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "项目未重命名",
                    ex.Message,
                    logMessage: $"无法重命名项目：{ex.Message}");
                return;
            }

            LoadProjects();
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "项目已重命名",
                $"{project.Name} -> {newName}",
                logMessage: $"重命名项目：{project.Name} -> {newName}");
        }

        private async Task DeleteProjectAsync(ProjectInfo project)
        {
            var confirmed = await ShowDeleteConfirmDialogAsync("删除项目", $"确定删除项目 {project.Name} 吗？这会删除整个项目文件夹。");
            if (!confirmed)
            {
                return;
            }

            _projectWorkspaceService.DeleteProject(project);
            LoadProjects();
            RequestDelayedRefresh();
            ShowFloatingTip(InfoBarSeverity.Success, "项目已删除", project.Name, logMessage: $"删除项目：{project.Name}");
        }

        private async Task ChangeProjectAssetLibraryAsync(ProjectInfo project)
        {
            var assetLibraries = GetAssetLibraries().OrderBy(library => library.Name).ToList();
            if (assetLibraries.Count == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "无法更改素材库",
                    "当前没有可选择的素材库。",
                    logMessage: "无法更改目标素材库：当前没有可选择的素材库。");
                return;
            }

            var selectedLibrary = await _dialogService.SelectAsync(new SelectionDialogRequest<AssetLibraryInfo>(
                "更改目标素材库",
                "选择这个项目后续读取索引时使用的素材库。",
                assetLibraries
                    .Select(assetLibrary => new SelectionDialogItem<AssetLibraryInfo>(assetLibrary.Name, assetLibrary))
                    .ToList(),
                "确定",
                "取消",
                420,
                360,
                assetLibrary => string.Equals(assetLibrary.FolderName, project.AssetLibraryFolderName, StringComparison.OrdinalIgnoreCase)));
            if (selectedLibrary is null)
            {
                return;
            }

            _projectWorkspaceService.SetProjectAssetLibrary(project, selectedLibrary);
            LoadProjects();
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "目标素材库已更改",
                selectedLibrary.Name,
                logMessage: $"项目 {project.Name} 的目标素材库改为：{selectedLibrary.Name}");
        }

        private async Task SaveProjectSettingsAsync(ProjectInfo project, string name, string code)
        {
            if (!ValidateCardName(name, "项目名称") || !ValidateCardName(code, "项目英文代号"))
            {
                return;
            }

            var oldCode = project.Code;
            var chapterPrefixChanged = !string.Equals(oldCode, code, StringComparison.Ordinal);

            ProjectInfo updatedProject;
            try
            {
                updatedProject = _projectWorkspaceService.UpdateProjectInfo(_projectRootPath, project, name, code);
            }
            catch (IOException ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "项目设置未保存",
                    ex.Message,
                    logMessage: $"无法保存项目设置：{ex.Message}");
                return;
            }

            if (chapterPrefixChanged)
            {
                UpdateChapterProjectCodePrefix(updatedProject.Path, oldCode, code);
            }

            _currentProject = updatedProject;
            ProjectDetailNameTextBox.Text = updatedProject.Name;
            ProjectDetailCodeTextBox.Text = updatedProject.Code;
            ProjectDetailTabTitleText.Text = $"{updatedProject.Name} / {updatedProject.Code}";
            ProjectDetailInfoText.Text = CreateProjectDetailInfoText(updatedProject);
            LoadChapters(updatedProject);
            if (chapterPrefixChanged)
            {
                ShowChapterStatus(InfoBarSeverity.Success, "章节代号已同步", $"已将章节前缀从 {oldCode} 更新为 {code}。");
            }

            LoadProjects();
            RequestDelayedRefresh();
            await Task.CompletedTask;
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "项目设置已保存",
                $"{updatedProject.Name}（{updatedProject.Code}）",
                logMessage: $"保存项目设置：{updatedProject.Name}（{updatedProject.Code}）");
        }

        private async Task RenameAssetLibraryAsync(AssetLibraryInfo assetLibrary)
        {
            var newName = await ShowNameInputDialogAsync("重命名素材库", "素材库名称", assetLibrary.Name);
            if (newName is null)
            {
                return;
            }

            if (!ValidateCardName(newName, "素材库名称"))
            {
                return;
            }

            var oldName = assetLibrary.Name;
            var oldFolderName = assetLibrary.FolderName;
            AssetLibraryInfo updatedAssetLibrary;
            try
            {
                updatedAssetLibrary = _projectWorkspaceService.RenameAssetLibrary(_projectRootPath, assetLibrary, newName);
            }
            catch (IOException ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "素材库未重命名",
                    ex.Message,
                    logMessage: $"无法重命名素材库：{ex.Message}");
                return;
            }

            _projectWorkspaceService.UpdateProjectAssetLibraryReferences(
                _projectRootPath,
                oldFolderName,
                oldName,
                newName,
                updatedAssetLibrary.FolderName);
            LoadAssetLibraries();
            LoadProjects();
            LoadAssetLibraryOptions();
            RequestDelayedRefresh();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "素材库已重命名",
                $"{oldName} -> {newName}",
                logMessage: $"重命名素材库：{oldName} -> {newName}");
        }

        private async Task DeleteAssetLibraryAsync(AssetLibraryInfo assetLibrary)
        {
            var confirmed = await ShowDeleteConfirmDialogAsync("删除素材库", $"确定删除素材库 {assetLibrary.Name} 吗？引用它的项目会变为未关联素材库。");
            if (!confirmed)
            {
                return;
            }

            _projectWorkspaceService.DeleteAssetLibrary(_projectRootPath, assetLibrary);
            if (_currentAssetLibrary is not null && PathsEqual(_currentAssetLibrary.Path, assetLibrary.Path))
            {
                _currentAssetLibrary = null;
                ShowAssetLibraryPage();
            }
            else
            {
                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
            }

            RequestDelayedRefresh();
            ShowFloatingTip(InfoBarSeverity.Success, "素材库已删除", assetLibrary.Name, logMessage: $"删除素材库：{assetLibrary.Name}");
        }

        private async Task<string?> ShowNameInputDialogAsync(string title, string header, string currentName)
        {
            return await _dialogService.PromptTextAsync(new TextInputDialogRequest(title, header, currentName));
        }

        private async Task<bool> ShowDeleteConfirmDialogAsync(string title, string content)
        {
            return await _dialogService.ConfirmAsync(new DialogRequest(
                title,
                content,
                PrimaryButtonText: "删除",
                CloseButtonText: "取消"));
        }

        private bool ValidateCardName(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                AppendLog(LogKind.Warning, $"无法重命名：请输入{label}。");
                return false;
            }

            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                AppendLog(LogKind.Warning, $"无法重命名：{label}包含不能用于文件夹名称的字符。");
                return false;
            }

            return true;
        }

    }
}
