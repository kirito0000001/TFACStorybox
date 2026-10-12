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
            if (SyncPanel is null)
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
            SyncPanel.BeginProjectOptions();
            try
            {
                foreach (var project in GetProjects())
                {
                    // 卡片内容归窗口的业务（CreateUnrealSyncProjectCard 用的是窗口的资源），
                    // 面板只负责把卡片和它对应的下拉项一起放进去。
                    SyncPanel.AddProjectOption(
                        project.FolderName,
                        $"{project.Name} / {project.Code}",
                        CreateUnrealSyncProjectCard(project, selectedFolderName));
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
            SyncPanel?.SelectProjectByTag(folderName);
        }

        private ProjectInfo? GetSelectedUnrealSyncProject()
        {
            var folderName = SyncPanel?.SelectedProjectTag
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

            SyncPanel.EnginePath = binding.EnginePath ?? string.Empty;
            SyncPanel.ProjectPath = binding.ProjectPath ?? string.Empty;
            SyncPanel.TargetFolderText = binding.ContentFolderPath ?? string.Empty;
        }

        /// <summary>点「选择引擎」。原来是 XAML 直接挂的处理函数，现在由面板发事件叫过来。</summary>
        private async Task ChooseUnrealEngineAsync()
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

            SyncPanel.EnginePath = selectedFile.Path;
            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        /// <summary>点「选择 .uproject」。原来是 XAML 直接挂的处理函数，现在由面板发事件叫过来。</summary>
        private async Task ChooseUnrealProjectAsync()
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

            SyncPanel.ProjectPath = selectedFile.Path;
            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        // ChooseUnrealContentFolderButton_Click 已删除（2026-10-12）：
        // 目标落点不再由用户手选 —— 它固定是 <工程>/Content/AssetMaterial，由 .uproject 推导。
        // 老方案是"手选一个夹 + 硬性要求它叫 Narrative"，新落点跨了 /Game 下三个顶层目录，
        // 选一个"目标夹"已经没有意义；界面那个框改成只读，显示推导结果。

        /// <summary>
        /// 换了工作台项目。
        /// <c>_isLoadingUnrealSyncProjects</c> 那道闸门在这儿 —— 它是窗体的状态，
        /// 所以面板不掺和：面板只管发"选中项变了"，要不要响应由窗口说了算。
        /// </summary>
        private void OnUnrealSyncProjectSelectionChanged()
        {
            if (_isLoadingUnrealSyncProjects)
            {
                return;
            }

            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        /// <summary>点「重新检测」：只做检测，不跑 Unreal。</summary>
        private async Task RunUnrealSyncDetectionAsync()
        {
            SaveUnrealSyncSettingsFromUi();
            var validation = ValidateUnrealSync(useUiValues: true);
            ApplyUnrealSyncValidation(validation);
            if (!validation.CanSync || validation.Context is null)
            {
                AppendLog(LogKind.User, "已重新检测虚幻同步台关联状态。");
                return;
            }

            // 原来是靠 sender 反推"是哪个按钮"再置灰它；现在事件里只有面板，
            // 所以走面板自己的属性 —— 也比从 sender 反推稳。
            SyncPanel.CheckButtonEnabled = false;
            // 「重新检测」这条路不跑 Unreal，但它的头两步和完整同步是一样的：
            // 先导出剧情 CSV，再做全量扫描比较。所以也走计划区间，别再硬编码百分比。
            var detectionPlan = UnrealSyncProgressPlan.ForDetection();
            SetUnrealSyncProgress(
                true,
                detectionPlan.Caption(UnrealSyncProgressPlan.StoryCsvExport, "正在从 StoryDocument 生成剧情 CSV..."),
                false,
                detectionPlan[UnrealSyncProgressPlan.StoryCsvExport].At(5));
            SyncPanel.SummaryText = "正在比较工具箱源文件与虚幻项目内的 .uasset 时间戳。";
            try
            {
                await PrepareStoryCsvForUnrealSyncAsync(validation.Context.Project);
                SetUnrealSyncProgress(
                    true,
                    detectionPlan.Caption(UnrealSyncProgressPlan.Detect, "正在检测虚幻同步差异..."),
                    false,
                    detectionPlan[UnrealSyncProgressPlan.Detect].At(10));
                var changePlan = await Task.Run(() => BuildUnrealSyncChangePlan(validation.Context));
                // 变更细节已经并进 ApplyUnrealSyncChangePlan 了（原来还有一个
                // AppendUnrealSyncDiffDetails 往同一块区域再铺一遍、把每个文件名都列出来）。
                ApplyUnrealSyncChangePlan(changePlan);
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
                SyncPanel.SummaryText = $"检测差异失败：{ex.Message}";
                AppendLog(LogKind.Error, "重新检测虚幻同步差异失败。", ex);
            }
            finally
            {
                SyncPanel.CheckButtonEnabled = true;
            }
        }

        /// <summary>点「全部重新同步」：先立个牌子，再走同一条同步流程。</summary>
        private async Task RunUnrealSyncWithFullResyncAsync()
        {
            _runFullUnrealSyncRequested = true;
            await RunUnrealSyncFlowAsync();
        }

        /// <summary>点「确认同步到虚幻」：完整同步流程。</summary>
        private async Task RunUnrealSyncFlowAsync()
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

            SyncPanel.SyncButtonEnabled = false;

            // 进度条按阶段加权分配（见 UnrealSyncProgressPlan）：各段耗时差得很远
            // —— 写清单不到一秒、Unreal 执行几十秒、同步前提交在 LFS 工程上可能几分钟 ——
            // 平摊会让快的阶段一闪而过、慢的阶段一路龟速。
            //
            // 这里**先按"会关编辑器"建计划**：关编辑器那一步在下面的询问之后才知道要不要做，
            // 而差异检测在询问之前。它权重只有 3%，选了"继续同步"就跳过那一段，
            // 不值得为它把计划拆成两份。
            var progressPlan = UnrealSyncProgressPlan.ForSync(includesCloseEditors: true);
            var csvExportBand = progressPlan[UnrealSyncProgressPlan.StoryCsvExport];
            var detectBand = progressPlan[UnrealSyncProgressPlan.Detect];

            // 这里的顺序是"先导出剧情 CSV、再做差异检测"，所以进度也从 CSV 那一段开始报 ——
            // 原来这一段被硬编码成 4%，和计划里其它阶段的百分比互相打架。
            SetUnrealSyncProgress(
                true,
                progressPlan.Caption(UnrealSyncProgressPlan.StoryCsvExport, "正在从 StoryDocument 生成剧情 CSV..."),
                false,
                csvExportBand.At(5));
            SyncPanel.SummaryText = forceFullSync
                ? "正在准备全量重新同步，将忽略本地同步状态。"
                : "正在计算工具箱源文件的 SHA-256，并与上次成功同步状态比较；只有内容真正变化时才会进入同步计划。";
            UnrealSyncChangePlan changePlan;
            try
            {
                await PrepareStoryCsvForUnrealSyncAsync(validation.Context.Project);
                SetUnrealSyncProgress(
                    true,
                    progressPlan.Caption(UnrealSyncProgressPlan.Detect, "正在检测虚幻同步差异..."),
                    false,
                    detectBand.At(5));
                changePlan = await Task.Run(() => BuildUnrealSyncChangePlan(validation.Context, forceFullSync));
            }
            catch (Exception ex)
            {
                SetUnrealSyncProgress(false, string.Empty);
                SetUnrealSyncStatus(InfoBarSeverity.Error, "准备剧情CSV失败", ex.Message);
                SyncPanel.SummaryText = $"准备剧情CSV失败：{ex.Message}";
                AppendLog(LogKind.Error, "同步前生成剧情CSV失败。", ex);
                SyncPanel.SyncButtonEnabled = true;
                return;
            }
            ApplyUnrealSyncChangePlan(changePlan);
            if (!changePlan.HasChanges)
            {
                SetUnrealSyncProgress(false, string.Empty);
                SetUnrealSyncStatus(InfoBarSeverity.Informational, "无需同步", "没有检测到素材、CSV 或立绘数据资产变动，本次不会启动虚幻同步。");
                SyncPanel.SummaryText = changePlan.Summary;
                SyncPanel.SyncButtonEnabled = true;
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
                SyncPanel.SyncButtonEnabled = true;
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
                SyncPanel.SummaryText = result.Output.Length == 0
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
                SyncPanel.SyncButtonEnabled = true;
                RefreshWorkbenchUnrealSyncTip();
            }
        }

        private void SaveUnrealSyncSettingsFromUi()
        {
            if (SyncPanel is null)
            {
                return;
            }

            var enginePath = SyncPanel.EnginePath.Trim();
            var unrealProjectPath = SyncPanel.ProjectPath.Trim();
            var selectedProject = GetSelectedUnrealSyncProject();
            _appSettings.UnrealToolProjectFolderName = selectedProject?.FolderName ?? SyncPanel.SelectedProjectTag;
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
            var enginePath = SyncPanel.EnginePath.Trim();
            var unrealProjectPath = SyncPanel.ProjectPath.Trim();

            // 素材库是从项目推导的（同步台不单独选它），拿不到就算了 ——
            // 检测会把这一项跳过，而不是报一条假的"目录不存在"。
            var assetLibraryPath = ValidateUnrealSync(useUiValues: true).Context?.AssetLibrary.Path
                ?? string.Empty;

            try
            {
                // 注意参数顺序：Run(unrealProjectPath, enginePath, assetLibraryPath)。
                // 这里原先传反了（引擎在前），后果不只是两行显示错 ——
                // 检测发现"工程不存在"就会**提前返回**，后面十几项目录/资产检查根本没跑，
                // 于是整层只剩两条"不合规"。2026-10-12 从截图上看出来的。
                var result = UnrealSyncPreflightCheck.Run(unrealProjectPath, enginePath, assetLibraryPath);
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
            // 校验信息也铺进同一张对比表 —— 检测之前它是这张表唯一的内容。
            //
            // 那些行是 Runner 里拼好的「类别：说明」文本，所以在第一个全角冒号处切开
            // （只切第一个：说明里可以再出现冒号）。拼装格式在 Runner 的
            // CreateUnrealSyncPlanItems 里，改那边时记得这里的假设。
            SyncPanel.ClearDiffRows();
            foreach (var item in validation.PlanItems)
            {
                var separator = item.IndexOf('：');
                SyncPanel.AddDiffRow(
                    separator > 0 ? item[..separator] : item,
                    string.Empty,
                    separator > 0 ? item[(separator + 1)..] : string.Empty);
            }

            SyncPanel.SummaryText = validation.Summary;
            SyncPanel.SyncButtonEnabled = validation.CanSync;

            // 目标落点不再手选：它由 .uproject 推导。这里把推导结果显示出来 ——
            // 那个框留着是为了让人**看得见会同步到哪**，而不是让人填。
            var derivedContentFolder = validation.Context?.TargetContentFolderPath;
            if (!string.IsNullOrWhiteSpace(derivedContentFolder))
            {
                SyncPanel.TargetFolderText = derivedContentFolder;
            }
        }

        private void SetUnrealSyncStatus(InfoBarSeverity severity, string title, string message)
        {
            SyncPanel.SetStatus(severity, title, message);
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

        /// <summary>
        /// 把变更计划铺成**一张对比表**：一行 = 一个类别变了多少。
        ///
        /// 这里原来是"把每个文件名都列出来"（27 个背景 + 18 个 BGM + 138 个立绘……几百行），
        /// 一屏装不下、也没人看得下去。现在只报数量与目标位置 ——
        /// 想知道具体是哪些文件，去 Unreal 内容浏览器里看比在日志里翻更直接。
        /// </summary>
        private void ApplyUnrealSyncChangePlan(UnrealSyncChangePlan changePlan)
        {
            SyncPanel.ClearDiffRows();

            if (!changePlan.HasChanges)
            {
                SyncPanel.AddDiffRow("（无变化）", "当前内容与虚幻项目一致", "不会启动 Unreal，也不会做本地提交。");
                SyncPanel.SummaryText = changePlan.Summary;
                return;
            }

            foreach (var group in changePlan.ImportGroups)
            {
                SyncPanel.AddDiffRow(
                    GetUnrealSyncDestinationDisplayName(group.Destination),
                    $"新增或更新 {group.Files.Count} 个",
                    group.Destination);
            }

            foreach (var group in changePlan.DeleteGroups)
            {
                SyncPanel.AddDiffRow(
                    GetUnrealSyncDestinationDisplayName(group.Destination),
                    $"清理多余素材 {group.Assets.Count} 个",
                    group.Destination);
            }

            if (changePlan.StoryTables.Count > 0)
            {
                SyncPanel.AddDiffRow(
                    "剧情表",
                    $"更新 {changePlan.StoryTables.Count} 张",
                    GetUnrealAssetFolderPath(changePlan.StoryTables[0].TableAsset));
            }

            if (changePlan.AssetIndexTables.Count > 0)
            {
                SyncPanel.AddDiffRow(
                    "特殊音效索引表",
                    $"更新 {changePlan.AssetIndexTables.Count} 张",
                    GetUnrealAssetFolderPath(changePlan.AssetIndexTables[0].TableAsset));
            }

            if (changePlan.StoryBranchesChanged)
            {
                SyncPanel.AddDiffRow(
                    "剧情选项分支",
                    $"更新 {changePlan.StoryBranches.Sum(source => source.Routes.Count)} 条",
                    UnrealSyncService.StoryBranchDataTableAssetPath);
            }

            if (changePlan.VoiceMapsChanged)
            {
                SyncPanel.AddDiffRow(
                    "剧情语音映射",
                    $"写入 {changePlan.VoiceMaps.Sum(entry => entry.AssignedCount)} 条",
                    UnrealSyncService.MusicPlayerBlueprintAssetPath);
            }

            if (changePlan.LustrationChanged)
            {
                SyncPanel.AddDiffRow(
                    "立绘信息表",
                    $"更新 {changePlan.LustrationRows.Count} 个角色",
                    UnrealSyncPathPolicy.LustrationInfoAsset);
            }

            if (changePlan.PortraitsChanged)
            {
                SyncPanel.AddDiffRow(
                    "小预览数据",
                    $"更新 {changePlan.PortraitRows.Count} 个角色",
                    UnrealSyncPathPolicy.PortraitsInfoAsset);
            }

            // 变量列表：几条请求，每条都是整个数组重写（不是增量）。
            if (changePlan.VariablePlan.Requests.Count > 0)
            {
                SyncPanel.AddDiffRow(
                    "变量列表",
                    $"写入 {changePlan.VariablePlan.Requests.Count} 组",
                    string.Join("、", changePlan.VariablePlan.Requests.Select(request => request.VariableName)));
            }

            SyncPanel.SummaryText = changePlan.Summary;
        }

        /// <summary>
        /// 目标目录 → 给人看的类别名。
        ///
        /// 这里必须跟着 <see cref="UnrealSyncPathPolicy"/> 的落点走：落点换了而这里没换，
        /// 对比表里就会出现一串 /Game/... 路径当"类别名"。
        /// （2026-10-12 之前这里判的还是 /BackGround、/BGM、/Lustration 那套旧布局。）
        /// </summary>
        private static string GetUnrealSyncDestinationDisplayName(string destination)
        {
            var normalized = destination.Replace('\\', '/').TrimEnd('/');

            if (normalized.StartsWith(UnrealSyncPathPolicy.BackgroundFolder, StringComparison.OrdinalIgnoreCase))
            {
                return "背景图";
            }
            if (normalized.StartsWith(UnrealSyncPathPolicy.BgmFolder, StringComparison.OrdinalIgnoreCase))
            {
                return "BGM";
            }
            if (normalized.StartsWith(UnrealSyncPathPolicy.SceneEffectFolder, StringComparison.OrdinalIgnoreCase))
            {
                return "环境音与特殊音效";
            }
            if (normalized.StartsWith(UnrealSyncPathPolicy.LustrationPreviewRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "小预览";
            }
            if (normalized.StartsWith(UnrealSyncPathPolicy.LustrationRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "立绘素材";
            }
            if (normalized.StartsWith(UnrealSyncPathPolicy.ExcelTextsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "剧情表";
            }
            if (normalized.StartsWith(UnrealSyncPathPolicy.VoiceFolder, StringComparison.OrdinalIgnoreCase))
            {
                var chapter = normalized[UnrealSyncPathPolicy.VoiceFolder.Length..].Trim('/');
                return string.IsNullOrEmpty(chapter) ? "语音" : $"章节语音：{chapter}";
            }

            var separatorIndex = normalized.LastIndexOf('/');
            return separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : normalized;
        }

        private static string GetUnrealAssetFolderPath(string assetPath)
        {
            var separatorIndex = assetPath.LastIndexOf('/');
            return separatorIndex > 0 ? assetPath[..separatorIndex] : assetPath;
        }

    }
}
