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
        private void ApplyUnrealSyncSettingsToUi()
        {
            if (UnrealEnginePathTextBox is null)
            {
                return;
            }

            SelectUnrealSyncProject(_appSettings.UnrealToolProjectFolderName);
            ApplySelectedUnrealProjectBindingToUi();
        }

        private void LoadUnrealSyncProjectOptions()
        {
            var selectedFolderName = GetSelectedUnrealSyncProject()?.FolderName ?? _appSettings.UnrealToolProjectFolderName;
            _isLoadingUnrealSyncProjects = true;
            UnrealSyncProjectComboBox.Items.Clear();
            UnrealSyncProjectCardsGridView.Items.Clear();
            try
            {
                foreach (var project in GetProjects())
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{project.Name} / {project.Code}",
                        Tag = project.FolderName
                    };
                    UnrealSyncProjectComboBox.Items.Add(item);
                    UnrealSyncProjectCardsGridView.Items.Add(CreateUnrealSyncProjectCard(project, selectedFolderName));
                }

                SelectUnrealSyncProject(selectedFolderName);
            }
            finally
            {
                _isLoadingUnrealSyncProjects = false;
            }
        }

        private GridViewItem CreateUnrealSyncProjectCard(ProjectInfo project, string? selectedFolderName)
        {
            var isSelected = string.Equals(project.FolderName, selectedFolderName, StringComparison.OrdinalIgnoreCase);
            var binding = _projectWorkspaceService.ReadProjectUnrealBinding(project);
            var footer = binding.IsComplete
                ? "已保存虚幻关联"
                : "未完整关联";
            if (isSelected)
            {
                footer = $"当前选择 · {footer}";
            }

            var item = DashboardCardFactory.CreateInfoCard(
                project,
                project.ThumbnailPath,
                project.Name,
                $"{project.Code} | {project.AssetLibraryName}",
                footer,
                UnrealSyncProjectCard_Tapped);
            if (isSelected)
            {
                DashboardCardFactory.MarkSelected(item);
            }

            return item;
        }

        private void UnrealSyncProjectCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not GridViewItem { Tag: ProjectInfo project })
            {
                return;
            }

            _appSettings.UnrealToolProjectFolderName = project.FolderName;
            SaveAppSettings();
            SelectUnrealSyncProject(project.FolderName);
            ApplySelectedUnrealProjectBindingToUi();
            LoadUnrealSyncProjectOptions();
            RefreshUnrealSyncStatus();
            AppendLog(LogKind.User, $"选择虚幻同步工作台项目：{project.Name}");
            e.Handled = true;
        }

        private void SelectUnrealSyncProject(string? folderName)
        {
            if (UnrealSyncProjectComboBox is null || string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            foreach (var item in UnrealSyncProjectComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    UnrealSyncProjectComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private ProjectInfo? GetSelectedUnrealSyncProject()
        {
            var folderName = (UnrealSyncProjectComboBox?.SelectedItem as ComboBoxItem)?.Tag as string
                ?? _appSettings.UnrealToolProjectFolderName;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return null;
            }

            return GetProjects()
                .FirstOrDefault(project => string.Equals(project.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplySelectedUnrealProjectBindingToUi()
        {
            var project = GetSelectedUnrealSyncProject();
            var binding = project is null
                ? new UnrealProjectBinding(_appSettings.UnrealEnginePath, _appSettings.UnrealProjectPath, _appSettings.UnrealContentFolderPath)
                : _projectWorkspaceService.ReadProjectUnrealBinding(project);
            if (project is not null &&
                !binding.IsComplete &&
                string.Equals(project.FolderName, _appSettings.UnrealToolProjectFolderName, StringComparison.OrdinalIgnoreCase))
            {
                binding = new UnrealProjectBinding(_appSettings.UnrealEnginePath, _appSettings.UnrealProjectPath, _appSettings.UnrealContentFolderPath);
            }

            UnrealEnginePathTextBox.Text = binding.EnginePath ?? string.Empty;
            UnrealProjectPathTextBox.Text = binding.ProjectPath ?? string.Empty;
            UnrealContentFolderTextBox.Text = binding.ContentFolderPath ?? string.Empty;
        }

        private async void ChooseUnrealEngineButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add(".exe");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null)
            {
                return;
            }

            UnrealEnginePathTextBox.Text = selectedFile.Path;
            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        private async void ChooseUnrealProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add(".uproject");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null)
            {
                return;
            }

            UnrealProjectPathTextBox.Text = selectedFile.Path;
            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        // ChooseUnrealContentFolderButton_Click 已删除（2026-10-12）：
        // 目标落点不再由用户手选 —— 它固定是 <工程>/Content/AssetMaterial，由 .uproject 推导。
        // 老方案是"手选一个夹 + 硬性要求它叫 Narrative"，新落点跨了 /Game 下三个顶层目录，
        // 选一个"目标夹"已经没有意义；界面那个框改成只读，显示推导结果。

        private void UnrealSyncProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUnrealSyncProjects)
            {
                return;
            }

            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        private async void CheckUnrealSyncButton_Click(object sender, RoutedEventArgs e)
        {
            SaveUnrealSyncSettingsFromUi();
            var validation = ValidateUnrealSync(useUiValues: true);
            ApplyUnrealSyncValidation(validation);
            if (!validation.CanSync || validation.Context is null)
            {
                AppendLog(LogKind.User, "已重新检测虚幻同步台关联状态。");
                return;
            }

            var checkButton = sender as Button;
            if (checkButton is not null)
            {
                checkButton.IsEnabled = false;
            }
            SetUnrealSyncProgress(true, "正在检测虚幻同步差异... 5%", false, 5);
            UnrealSyncSummaryText.Text = "正在比较工具箱源文件与虚幻项目内的 .uasset 时间戳。";
            try
            {
                await PrepareStoryCsvForUnrealSyncAsync(validation.Context.Project);
                var changePlan = await Task.Run(() => BuildUnrealSyncChangePlan(validation.Context));
                ApplyUnrealSyncChangePlan(changePlan);
                AppendUnrealSyncDiffDetails(changePlan);
                SetUnrealSyncStatus(
                    changePlan.HasChanges ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                    changePlan.HasChanges ? "检测到同步差异" : "没有同步差异",
                    changePlan.HasChanges ? "差异文件已列在下方。" : "工具箱项目、素材库和虚幻目标目录已经一致。");
                SetUnrealSyncProgress(false, string.Empty);
                AppendLog(LogKind.User, $"已重新检测虚幻同步差异：{changePlan.TotalChangedItems} 项。");
            }
            catch (Exception ex)
            {
                SetUnrealSyncProgress(false, string.Empty);
                SetUnrealSyncStatus(InfoBarSeverity.Error, "检测差异失败", ex.Message);
                UnrealSyncSummaryText.Text = $"检测差异失败：{ex.Message}";
                AppendLog(LogKind.Error, "重新检测虚幻同步差异失败。", ex);
            }
            finally
            {
                if (checkButton is not null)
                {
                    checkButton.IsEnabled = true;
                }
            }
        }

        private void RunFullUnrealSyncButton_Click(object sender, RoutedEventArgs e)
        {
            _runFullUnrealSyncRequested = true;
            RunUnrealSyncButton_Click(sender, e);
        }

        private async void RunUnrealSyncButton_Click(object sender, RoutedEventArgs e)
        {
            SaveUnrealSyncSettingsFromUi();
            var forceFullSync = _runFullUnrealSyncRequested;
            _runFullUnrealSyncRequested = false;
            var validation = ValidateUnrealSync(useUiValues: true);
            ApplyUnrealSyncValidation(validation);
            if (!validation.CanSync || validation.Context is null)
            {
                return;
            }

            RunUnrealSyncButton.IsEnabled = false;

            // 进度条按阶段加权分配（见 UnrealSyncProgressPlan）：各段耗时差得很远
            // —— 写清单不到一秒、Unreal 执行几十秒、同步前提交在 LFS 工程上可能几分钟 ——
            // 平摊会让快的阶段一闪而过、慢的阶段一路龟速。
            //
            // 这里**先按"会关编辑器"建计划**：关编辑器那一步在下面的询问之后才知道要不要做，
            // 而差异检测在询问之前。它权重只有 3%，选了"继续同步"就跳过那一段，
            // 不值得为它把计划拆成两份。
            var progressPlan = UnrealSyncProgressPlan.ForSync(includesCloseEditors: true);
            var detectBand = progressPlan[UnrealSyncProgressPlan.Detect];
            SetUnrealSyncProgress(
                true,
                progressPlan.Caption(UnrealSyncProgressPlan.Detect, "正在检测虚幻同步差异..."),
                false,
                detectBand.At(5));
            UnrealSyncSummaryText.Text = forceFullSync
                ? "正在准备全量重新同步，将忽略本地同步状态。"
                : "正在计算工具箱源文件的 SHA-256，并与上次成功同步状态比较；只有内容真正变化时才会进入同步计划。";
            UnrealSyncChangePlan changePlan;
            try
            {
                await PrepareStoryCsvForUnrealSyncAsync(validation.Context.Project);
                changePlan = await Task.Run(() => BuildUnrealSyncChangePlan(validation.Context, forceFullSync));
            }
            catch (Exception ex)
            {
                SetUnrealSyncProgress(false, string.Empty);
                SetUnrealSyncStatus(InfoBarSeverity.Error, "准备剧情CSV失败", ex.Message);
                UnrealSyncSummaryText.Text = $"准备剧情CSV失败：{ex.Message}";
                AppendLog(LogKind.Error, "同步前生成剧情CSV失败。", ex);
                RunUnrealSyncButton.IsEnabled = true;
                return;
            }
            ApplyUnrealSyncChangePlan(changePlan);
            if (!changePlan.HasChanges)
            {
                SetUnrealSyncProgress(false, string.Empty);
                SetUnrealSyncStatus(InfoBarSeverity.Informational, "无需同步", "没有检测到素材、CSV 或立绘数据资产变动，本次不会启动虚幻同步。");
                UnrealSyncSummaryText.Text = changePlan.Summary;
                RunUnrealSyncButton.IsEnabled = true;
                return;
            }

            SetUnrealSyncProgress(
                true,
                progressPlan.Caption(UnrealSyncProgressPlan.Detect, "差异检测完成，等待确认同步..."),
                false,
                detectBand.At(100));
            var runningUnrealChoice = await ShowRunningUnrealEditorDialogAsync(validation.Context);
            if (runningUnrealChoice is null)
            {
                SetUnrealSyncProgress(false, string.Empty);
                RunUnrealSyncButton.IsEnabled = true;
                return;
            }

            if (runningUnrealChoice == true)
            {
                var closeEditorsBand = progressPlan[UnrealSyncProgressPlan.CloseEditors];
                SetUnrealSyncProgress(
                    true,
                    progressPlan.Caption(UnrealSyncProgressPlan.CloseEditors, "正在尝试关闭已打开的 Unreal Editor..."),
                    false,
                    closeEditorsBand.At(20));
                await Task.Run(() => CloseRunningUnrealEditors(validation.Context));
                await Task.Delay(1200);
            }

            SetUnrealSyncStatus(InfoBarSeverity.Informational, "正在同步", "正在生成导入清单并调用 Unreal Editor，请等待引擎完成资源导入。");
            try
            {
                var cancellationToken = GetGlobalProgressCancellationToken();

                // 同步前的回退点：**全量本地提交**（取代原先给 Unreal 工程打 zip）。
                //
                // 口径：只提交、不推送。同步是高频动作，顺手往远端推是在替用户做他没同意的事。
                // 没有仓库、或提交失败，都**不阻断同步** —— 那只是回退点，不是前置条件；
                // 但必须说清楚"这次没有可回退的提交点"，用户以为有、实际没有是最坏的情况。
                var commitBand = progressPlan[UnrealSyncProgressPlan.LocalCommit];
                SetUnrealSyncProgress(
                    true,
                    progressPlan.Caption(UnrealSyncProgressPlan.LocalCommit, "正在提交同步前的本地改动..."),
                    false,
                    commitBand.At(0));
                var unrealProjectRoot = Path.GetDirectoryName(validation.Context.UnrealProjectPath) ?? string.Empty;
                UnrealSyncGitCommitResult commitResult;
                try
                {
                    commitResult = await UnrealSyncGitCommitService.CommitEverythingAsync(
                        unrealProjectRoot,
                        $"同步前快照：{DateTime.Now:yyyy-MM-dd HH:mm:ss}（{validation.Context.Project.Name}）",
                        commitProgress =>
                        {
                            // 进度**既进进度条也进日志**：这一段 git add -A 在 LFS 工程上可能几分钟，
                            // 只进日志等于进度条在这段是冻的 —— 用户看到的就是卡死。
                            SetUnrealSyncProgress(
                                true,
                                progressPlan.Caption(UnrealSyncProgressPlan.LocalCommit, commitProgress.Message),
                                false,
                                commitBand.At(commitProgress.SubPercent));
                            if (!commitProgress.IsTick)
                            {
                                AppendLog(LogKind.Info, commitProgress.Message);
                            }
                        },
                        cancellationToken);
                }
                catch (Exception commitError)
                {
                    commitResult = new UnrealSyncGitCommitResult(
                        false,
                        false,
                        string.Empty,
                        $"同步前的本地提交失败：{commitError.Message}");
                }

                SetUnrealSyncProgress(
                    true,
                    progressPlan.Caption(UnrealSyncProgressPlan.LocalCommit, "同步前的本地提交已完成"),
                    false,
                    commitBand.At(100));
                if (commitResult.IsRepository)
                {
                    AppendLog(LogKind.User, commitResult.Detail);
                }
                else
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "没有可回退的提交点",
                        commitResult.Detail + " 建议把 Unreal 工程纳入 git，或在同步前自行备份。");
                }

                // Unreal 执行这一段：脚本自己会报 0-100，还带 Stage 与"已完成/总数"。
                // 把它**映射进"执行"那一段**，而不是直接当全局百分比用 ——
                // 否则前面准备阶段的百分号和脚本自己的百分号会互相打架。
                var executeBand = progressPlan[UnrealSyncProgressPlan.Execute];
                var progress = new Progress<UnrealSyncProgressUpdate>(update =>
                {
                    // 「细节」：脚本报的项数（例如"已导入 3/12"）原来在 C# 侧被丢掉了，现在带上。
                    var countText = string.IsNullOrWhiteSpace(update.CountText)
                        ? string.Empty
                        : $"（{update.CountText}）";
                    SetUnrealSyncProgress(
                        true,
                        progressPlan.Caption(UnrealSyncProgressPlan.Execute, update.Message + countText),
                        false,
                        executeBand.At(update.Percent));
                });
                var result = await RunUnrealSyncAsync(validation.Context, changePlan, progress, cancellationToken);
                var report = result.Report;
                var reportSuccess = result.ExitCode == 0 &&
                    report is { ProtocolVersion: 1, Completed: true, Success: true } &&
                    report.ImportedCount == changePlan.ImportGroups.Sum(group => group.Files.Count) &&
                    report.StoryTableCount == changePlan.StoryTables.Count &&
                    report.AssetIndexTableCount == changePlan.AssetIndexTables.Count &&
                    (!changePlan.StoryBranchesChanged || report.StoryBranchesUpdated) &&
                    (!changePlan.VoiceMapsChanged || report.VoiceMapsUpdated) &&
                    (!changePlan.LustrationChanged || report.LustrationUpdated) &&
                    (!changePlan.PortraitsChanged || report.PortraitsUpdated);
                SetUnrealSyncStatus(
                    reportSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    reportSuccess ? "同步完成" : "同步未完整完成",
                    reportSuccess
                        ? $"Unreal 已确认导入 {report!.ImportedCount} 项、更新 {report.StoryTableCount + report.AssetIndexTableCount} 张表{(report.StoryBranchesUpdated ? "，并刷新剧情选项分支" : string.Empty)}{(report.VoiceMapsUpdated ? "，并刷新剧情语音映射" : string.Empty)}。"
                        : report is null
                            ? $"退出码 {result.ExitCode}，但没有收到结构化结果文件：{result.ResultPath}"
                            : string.Join(" ", report.Errors.DefaultIfEmpty($"退出码 {result.ExitCode}，部分计划项没有完成。")));
                // 回退点那一行：以前是 zip 备份路径，现在是本地提交的 HEAD。
                var commitSummary = commitResult.Committed && !string.IsNullOrWhiteSpace(commitResult.HeadSha)
                    ? $"同步前本地提交：{commitResult.HeadSha}\n"
                    : string.Empty;
                UnrealSyncSummaryText.Text = result.Output.Length == 0
                    ? $"{commitSummary}{changePlan.Summary}\n已写入导入清单：{result.ManifestPath}"
                    : $"{commitSummary}{changePlan.Summary}\n已写入导入清单：{result.ManifestPath}\n{TrimLongText(result.Output, 900)}";
                if (reportSuccess)
                {
                    _unrealSyncService.WriteState(validation.Context, changePlan);
                    SetUnrealSyncProgress(true, "同步完成 100%", false, 100);
                    CompleteGlobalProgress("虚幻同步完成", $"处理 {changePlan.TotalChangedItems} 项，退出码 {result.ExitCode}");
                }
                else
                {
                    CompleteGlobalProgress("虚幻同步未完整完成", report is null
                        ? "Unreal 未返回结构化结果，本次不会记录为已同步。"
                        : string.Join("；", report.Errors.DefaultIfEmpty("部分计划项没有完成，本次不会记录为已同步。")));
                }

                AppendLog(result.ExitCode == 0 ? LogKind.User : LogKind.Warning, $"虚幻同步结束，退出码：{result.ExitCode}");
                NotifyUnrealSyncFinishedIfInactive(result, changePlan);
                await ShowUnrealSyncFinishedDialogAsync(validation.Context, result, changePlan);
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("虚幻同步已取消", "已停止当前同步步骤；如果 Unreal 命令进程已经启动，工具箱会尝试一并终止。");
                SetUnrealSyncStatus(InfoBarSeverity.Warning, "同步已取消", "本次虚幻同步没有完成。已写入的临时清单或 Unreal 侧中途导入结果可能会保留。");
                AppendLog(LogKind.Warning, "虚幻同步已取消。");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("虚幻同步失败", ex.Message);
                SetUnrealSyncStatus(InfoBarSeverity.Error, "同步失败", ex.Message);
                AppendLog(LogKind.Error, "虚幻同步失败。", ex);
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
                RunUnrealSyncButton.IsEnabled = true;
                RefreshWorkbenchUnrealSyncTip();
            }
        }

        private void SaveUnrealSyncSettingsFromUi()
        {
            if (UnrealEnginePathTextBox is null)
            {
                return;
            }

            var enginePath = UnrealEnginePathTextBox.Text.Trim();
            var unrealProjectPath = UnrealProjectPathTextBox.Text.Trim();
            var selectedProject = GetSelectedUnrealSyncProject();
            _appSettings.UnrealToolProjectFolderName = selectedProject?.FolderName ?? (UnrealSyncProjectComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            _appSettings.UnrealEnginePath = enginePath;
            _appSettings.UnrealProjectPath = unrealProjectPath;
            // 内容夹不再持久化：它由 .uproject 推导（UnrealSyncPathPolicy.ResolveContentFolderPath），
            // 存下来只会在换工程时变成一个过期的值、并让人以为它还能手选。
            _appSettings.UnrealContentFolderPath = null;
            SaveAppSettings();

            if (selectedProject is not null)
            {
                // 每项目那份绑定里的内容夹也写成"推导值" —— 它不再由用户选。
                // 留空会让 UnrealProjectBinding.IsComplete 判成"绑定不完整"，
                // 于是回落到全局设置，而那条路已经不再是权威来源。
                var derivedContentFolder = string.Empty;
                try
                {
                    if (!string.IsNullOrWhiteSpace(unrealProjectPath))
                    {
                        derivedContentFolder = UnrealSyncPathPolicy.ResolveTargetContentFolderPath(unrealProjectPath);
                    }
                }
                catch (Exception exception)
                {
                    AppendLog(LogKind.Warning, $"推导目标落点失败，本次不写入项目绑定：{exception.Message}");
                }

                _projectWorkspaceService.SaveProjectUnrealBinding(selectedProject, enginePath, unrealProjectPath, derivedContentFolder);
                LoadUnrealSyncProjectOptions();
            }
        }

        private void RefreshUnrealSyncStatus()
        {
            var validation = ValidateUnrealSync(useUiValues: true);
            ApplyUnrealSyncValidation(validation);
            // 顺手把「同步前检测」也刷一遍。它只读磁盘、不跑 Unreal，所以很便宜；
            // 而它的触发点恰好和状态刷新一模一样（进页面、换项目、换引擎、换文件夹）。
            RunUnrealSyncPreflightCheck();
        }

        /// <summary>
        /// 跑一遍「同步前检测」并铺到检测层上（三层里的第二层）。
        ///
        /// 只读磁盘，所以随点随跑。检查项与判定口径都在
        /// <see cref="UnrealSyncPreflightCheck"/> 里 —— 这个方法只负责
        /// "从界面上把路径取出来、把结果交给 VM"。
        /// </summary>
        private void RunUnrealSyncPreflightCheck()
        {
            var enginePath = UnrealEnginePathTextBox.Text.Trim();
            var unrealProjectPath = UnrealProjectPathTextBox.Text.Trim();

            // 素材库是从项目推导的（同步台不单独选它），拿不到就算了 ——
            // 检测会把这一项跳过，而不是报一条假的"目录不存在"。
            var assetLibraryPath = ValidateUnrealSync(useUiValues: true).Context?.AssetLibrary.Path
                ?? string.Empty;

            try
            {
                var result = UnrealSyncPreflightCheck.Run(enginePath, unrealProjectPath, assetLibraryPath);
                _applicationViewModel.UnrealSync.ApplyCheckResult(result);
            }
            catch (Exception exception)
            {
                _applicationViewModel.UnrealSync.ClearCheckResult($"检测失败：{exception.Message}");
                AppendLog(LogKind.Error, "同步前检测失败。", exception);
            }
        }

        private void RefreshWorkbenchUnrealSyncTip()
        {
            if (UnrealSyncWorkbenchTipInfoBar is null)
            {
                return;
            }

            UnrealSyncWorkbenchTipInfoBar.IsOpen = false;
        }

        private void ApplyUnrealSyncValidation(UnrealSyncValidation validation)
        {
            SetUnrealSyncStatus(validation.Severity, validation.Title, validation.Message);
            UnrealSyncPlanItemsControl.Items.Clear();
            foreach (var item in validation.PlanItems)
            {
                UnrealSyncPlanItemsControl.Items.Add(new TextBlock
                {
                    Text = $"• {item}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                });
            }

            UnrealSyncSummaryText.Text = validation.Summary;
            RunUnrealSyncButton.IsEnabled = validation.CanSync;

            // 目标落点不再手选：它由 .uproject 推导。这里把推导结果显示出来 ——
            // 那个框留着是为了让人**看得见会同步到哪**，而不是让人填。
            var derivedContentFolder = validation.Context?.TargetContentFolderPath;
            if (!string.IsNullOrWhiteSpace(derivedContentFolder))
            {
                UnrealContentFolderTextBox.Text = derivedContentFolder;
            }
        }

        private void SetUnrealSyncStatus(InfoBarSeverity severity, string title, string message)
        {
            UnrealSyncStatusInfoBar.IsOpen = true;
            UnrealSyncStatusInfoBar.Severity = severity;
            UnrealSyncStatusInfoBar.Title = title;
            UnrealSyncStatusInfoBar.Message = message;
        }

        private void SetUnrealSyncProgress(bool isVisible, string message, bool isIndeterminate = true, double value = 0)
        {
            // 这里原来还有三行重置页面内进度面板（UnrealSyncProgressPanel / Bar / Text）的代码。
            // 那三个元素**恒为 Collapsed**、从来没显示过 —— 真正的进度走下面的全局浮层。
            // 一并删掉：死控件 + 死代码只会让人以为"页面上也有个进度条"。
            if (isVisible)
            {
                if (!_isGlobalProgressVisible)
                {
                    ShowGlobalProgress("虚幻同步", message);
                }

                UpdateGlobalProgress(message, value, "正在同步工具箱项目、素材库和 Unreal 目标目录。", isIndeterminate);
            }
            else if (_isGlobalProgressVisible && string.Equals(_globalProgressOperationTitle, "虚幻同步", StringComparison.Ordinal))
            {
                HideGlobalProgress();
            }
        }

        private void NotifyUnrealSyncFinishedIfInactive(UnrealSyncResult result, UnrealSyncChangePlan changePlan)
        {
            if (_isWindowActive)
            {
                return;
            }

            try
            {
                var manager = AppNotificationManager.Default;
                manager.Register();

                var title = result.ExitCode == 0 ? "虚幻同步完成" : "虚幻同步结束";
                var status = result.ExitCode == 0 ? "同步成功" : $"退出码 {result.ExitCode}";
                var body = $"{status}，处理 {changePlan.TotalChangedItems} 项。";
                var notification = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification();
                notification.Tag = "UnrealSync";
                notification.Group = "GalExcleTools";
                manager.Show(notification);
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Warning, $"虚幻同步完成通知发送失败：{ex.Message}");
            }
        }

        private void ApplyUnrealSyncChangePlan(UnrealSyncChangePlan changePlan)
        {
            UnrealSyncPlanItemsControl.Items.Clear();
            AddUnrealSyncPlanHeading("同步方式");
            AddUnrealSyncPlanText(changePlan.IsFullSync
                ? "全部重新同步：忽略同步记录，重新处理全部内容。"
                : "仅同步本次检测到的变化，未变化的内容不会重复处理。");

            AddUnrealSyncPlanHeading("本次同步", topMargin: 10);
            if (changePlan.ChangeItems.Count == 0)
            {
                AddUnrealSyncPlanText("没有需要处理的内容。");
            }
            else
            {
                foreach (var item in changePlan.ChangeItems)
                {
                    AddUnrealSyncPlanText($"• {item}");
                }
            }

            if (changePlan.UnchangedItems.Count > 0)
            {
                AddUnrealSyncPlanHeading("无需处理", topMargin: 10);
                foreach (var item in changePlan.UnchangedItems)
                {
                    AddUnrealSyncPlanText($"• {item}");
                }
            }

            UnrealSyncSummaryText.Text = changePlan.Summary;
        }

        private void AppendUnrealSyncDiffDetails(UnrealSyncChangePlan changePlan)
        {
            if (!changePlan.HasChanges)
            {
                return;
            }

            foreach (var group in changePlan.ImportGroups)
            {
                var displayName = GetUnrealSyncDestinationDisplayName(group.Destination);
                AddUnrealSyncPlanHeading($"新增或更新：{displayName}（{group.Files.Count} 个）", topMargin: 12);
                AddUnrealSyncPlanText($"目标位置：{group.Destination}");
                foreach (var path in group.Files)
                {
                    AddUnrealSyncPlanText($"  - {Path.GetFileName(path)}");
                }
            }

            foreach (var group in changePlan.DeleteGroups)
            {
                var displayName = GetUnrealSyncDestinationDisplayName(group.Destination);
                AddUnrealSyncPlanHeading($"清理多余素材：{displayName}（{group.Assets.Count} 个）", topMargin: 12);
                AddUnrealSyncPlanText($"虚幻位置：{group.Destination}");
                foreach (var assetPath in group.Assets)
                {
                    AddUnrealSyncPlanText($"  - {assetPath}");
                }
            }

            if (changePlan.StoryTables.Count > 0)
            {
                AddUnrealSyncPlanHeading($"更新剧情表（{changePlan.StoryTables.Count} 张）", topMargin: 12);
                AddUnrealSyncPlanText($"目标位置：{GetUnrealAssetFolderPath(changePlan.StoryTables[0].TableAsset)}");
                foreach (var entry in changePlan.StoryTables)
                {
                    AddUnrealSyncPlanText($"  - {Path.GetFileName(entry.CsvPath)} -> {entry.TableAsset}");
                }
            }

            if (changePlan.AssetIndexTables.Count > 0)
            {
                AddUnrealSyncPlanHeading($"更新素材索引表（{changePlan.AssetIndexTables.Count} 张）", topMargin: 12);
                AddUnrealSyncPlanText($"目标位置：{GetUnrealAssetFolderPath(changePlan.AssetIndexTables[0].TableAsset)}");
                foreach (var entry in changePlan.AssetIndexTables)
                {
                    AddUnrealSyncPlanText($"  - {Path.GetFileName(entry.CsvPath)} -> {entry.TableAsset}");
                }
            }

            if (changePlan.LustrationChanged)
            {
                AddUnrealSyncPlanHeading($"更新立绘数据（{changePlan.LustrationRows.Count} 个角色）", topMargin: 12);
                foreach (var row in changePlan.LustrationRows)
                {
                    AddUnrealSyncPlanText($"  - {row.Key} / {row.Name}");
                }
            }

            if (changePlan.PortraitsChanged)
            {
                AddUnrealSyncPlanHeading($"更新小预览数据（{changePlan.PortraitRows.Count} 个角色）", topMargin: 12);
                foreach (var row in changePlan.PortraitRows)
                {
                    AddUnrealSyncPlanText($"  - {row.Key} / {row.Name}");
                }
            }

            if (changePlan.StoryBranchesChanged)
            {
                AddUnrealSyncPlanHeading($"更新剧情选项分支（{changePlan.StoryBranches.Sum(source => source.Routes.Count)} 条）", topMargin: 12);
                AddUnrealSyncPlanText($"目标表：{UnrealSyncService.StoryBranchDataTableAssetPath}");
                foreach (var source in changePlan.StoryBranches)
                {
                    AddUnrealSyncPlanText($"  - {source.SourceTable}");
                    foreach (var route in source.Routes)
                    {
                        AddUnrealSyncPlanText($"      选项 {route.OptionKey}：{route.OptionText} -> {route.TargetTable}");
                    }
                }
            }

            if (changePlan.VoiceMapsChanged)
            {
                AddUnrealSyncPlanHeading($"更新剧情语音映射（{changePlan.VoiceMaps.Sum(entry => entry.AssignedCount)} 条）", topMargin: 12);
                AddUnrealSyncPlanText($"目标蓝图：{UnrealSyncService.MusicPlayerBlueprintAssetPath}");
                foreach (var entry in changePlan.VoiceMaps)
                {
                    AddUnrealSyncPlanText($"  - {entry.TableAsset}：{entry.AssignedCount} 条语音 / {entry.Waves.Count} 个索引槽位");
                }
            }
        }

        private static string GetUnrealSyncDestinationDisplayName(string destination)
        {
            var normalized = destination.Replace('\\', '/').TrimEnd('/');
            if (normalized.EndsWith("/BackGround", StringComparison.OrdinalIgnoreCase))
            {
                return "背景图";
            }
            if (normalized.EndsWith("/BGM", StringComparison.OrdinalIgnoreCase))
            {
                return "BGM";
            }
            if (normalized.EndsWith("/Scene_Effect", StringComparison.OrdinalIgnoreCase))
            {
                return "环境音与特殊音效";
            }

            var voiceMarker = "/Voice/";
            var voiceIndex = normalized.IndexOf(voiceMarker, StringComparison.OrdinalIgnoreCase);
            if (voiceIndex >= 0)
            {
                return $"章节语音：{normalized[(voiceIndex + voiceMarker.Length)..]}";
            }
            if (normalized.EndsWith("/Voice", StringComparison.OrdinalIgnoreCase))
            {
                return "语音";
            }
            if (normalized.Contains("/Lustration", StringComparison.OrdinalIgnoreCase))
            {
                return "立绘素材";
            }

            var separatorIndex = normalized.LastIndexOf('/');
            return separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : normalized;
        }

        private static string GetUnrealAssetFolderPath(string assetPath)
        {
            var separatorIndex = assetPath.LastIndexOf('/');
            return separatorIndex > 0 ? assetPath[..separatorIndex] : assetPath;
        }

        private void AddUnrealSyncPlanHeading(string text, double topMargin = 0)
        {
            UnrealSyncPlanItemsControl.Items.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, topMargin, 0, 5),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
            });
        }

        private void AddUnrealSyncPlanText(string text, double topMargin = 0)
        {
            UnrealSyncPlanItemsControl.Items.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, topMargin, 0, 4),
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });
        }

    }
}
