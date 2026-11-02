using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools
{
    /// <summary>
    /// 文本语音页的「响度规范」两块：
    /// · <b>检查DB规范</b> —— 只量不改，按游戏语音口径（−12 LUFS ±2、真峰值 ≤ −3 dBTP）
    ///   把当前章节的语音判成「达标 / 贴边 / 超差 / 峰值超」，并额外报同类离散度；
    /// · <b>一键规整语音</b> —— 两遍 loudnorm 就地拉平，<b>先整章备份再动手</b>。
    ///
    /// 口径与判定逻辑来自隔壁工具箱（CrossingVoidZDTool）的 <c>Services/Audio/**</c>，
    /// 这里只保留了语音这一路：裁静音、音效链、输出格式/声道选项都没搬 ——
    /// 语音素材是「一句话一条」，那些开关在这条链路上不产生价值，只会多一层误操作。
    /// </summary>
    public sealed partial class MainWindow
    {
        private readonly VoiceBatchProcessor _voiceBatchProcessor = new();

        /// <summary>
        /// 「正在跑一批语音」的互斥标记。全局进度条不是模态的，
        /// 双击能把同一章的两批 ffmpeg 叠起来跑 —— 那会把同一批文件写两遍。
        /// </summary>
        private bool _isVoiceLoudnessBusy;

        /// <summary>当前章节的语音文件夹（<c>{项目}/Voice/{章节代号}</c>）里的全部 wav。</summary>
        private IReadOnlyList<string> GetCurrentChapterVoiceFiles()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return [];
            }

            var folderPath = WorkspacePathUtility.GetProjectChapterVoiceFolderPath(
                _currentProject,
                _selectedTextToolChapter.Code);
            if (!Directory.Exists(folderPath))
            {
                return [];
            }

            return Directory
                .EnumerateFiles(folderPath, "*.wav", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 找 ffmpeg。找不到不是「静默失败」：告诉用户该放哪、或者直接跳去设置页填路径。
        /// </summary>
        private async Task<string?> ResolveFfmpegPathAsync()
        {
            var locator = new FfmpegLocator(() => _appSettings.FFmpegPath);
            var resolution = locator.Resolve();
            if (resolution.IsFound)
            {
                return resolution.ExecutablePath;
            }

            var result = await _dialogService.ShowAsync(new DialogRequest(
                "没有找到 ffmpeg",
                $"{resolution.Detail}\n\n"
                + "两条出路：\n"
                + $"· 把 ffmpeg 放到随包目录：{locator.BundledExecutablePath}\n"
                + "· 或者在「整体设置 → 音频工具（ffmpeg）」里指定 ffmpeg.exe 的位置。",
                "去设置",
                "关闭",
                PrimarySound: DialogSoundIntent.None,
                CloseSound: DialogSoundIntent.None));

            if (result == DialogResultKind.Primary)
            {
                ShowSettingsPage();
            }

            return null;
        }

        // ---------------------------------------------------------------------
        // 检查DB规范
        // ---------------------------------------------------------------------

        private async void CheckVoiceLoudnessButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isVoiceLoudnessBusy)
            {
                return;
            }

            _isVoiceLoudnessBusy = true;
            try
            {
                await RunVoiceLoudnessCheckAsync();
            }
            finally
            {
                _isVoiceLoudnessBusy = false;
            }
        }

        private async Task RunVoiceLoudnessCheckAsync()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return;
            }

            var files = GetCurrentChapterVoiceFiles();
            if (files.Count == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "没有可检查的语音",
                    $"「{_selectedTextToolChapter.Code}」的语音文件夹里还没有 wav 文件。");
                return;
            }

            var ffmpegPath = await ResolveFfmpegPathAsync();
            if (ffmpegPath is null)
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();

            var preset = VoiceLoudnessPreset.GameDialogue;
            var progress = new Progress<VoiceBatchProgress>(item =>
                UpdateGlobalProgress(
                    $"正在测量 {item.CompletedCount} / {item.TotalCount}",
                    item.OverallFraction * 100d,
                    item.FileName));

            ShowGlobalProgress("检查语音响度", $"正在测量 {files.Count} 条语音…");
            VoiceComplianceReport report;
            try
            {
                report = await _voiceBatchProcessor.MeasureAsync(
                    ffmpegPath,
                    files,
                    preset,
                    progress,
                    GetGlobalProgressCancellationToken());
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "语音响度检查已停止。");
                await HideGlobalProgressAfterDelayAsync();
                return;
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("检查失败", ex.Message);
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "语音响度检查失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "语音响度检查失败");
                await HideGlobalProgressAfterDelayAsync();
                return;
            }

            var summary = $"共 {report.TotalCount} 条：达标 {report.PassCount} · 贴边 {report.NearCount} · "
                + $"超差 {report.OffCount} · 峰值超标 {report.PeakOverCount}";
            CompleteGlobalProgress("检查完成", summary);
            AppendLog(LogKind.Info, $"语音响度检查（{_selectedTextToolChapter.Code}）：{summary}");
            await HideGlobalProgressAfterDelayAsync();

            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "语音响度检查",
                BuildVoiceComplianceReportView(report, _selectedTextToolChapter.Code),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None,
                ConfigureDialog: dialog => dialog.MaxWidth = 760));
        }

        /// <summary>
        /// 报告正文：三段分开写（口径 / 统计+一致性 / 逐条明细），
        /// 末尾必须跟着测量口径声明 —— 报告里写清口径，比假装合规值钱。
        /// </summary>
        private static UIElement BuildVoiceComplianceReportView(VoiceComplianceReport report, string chapterCode)
        {
            var root = new StackPanel
            {
                Spacing = 10,
                MinWidth = 660
            };

            root.Children.Add(new TextBlock
            {
                Text = report.Preset.Summary,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });

            root.Children.Add(new TextBlock
            {
                Text = $"章节 {chapterCode} · 共 {report.TotalCount} 条："
                    + $"达标 {report.PassCount} · 贴边 {report.NearCount} · "
                    + $"超差 {report.OffCount} · 峰值超标 {report.PeakOverCount}",
                TextWrapping = TextWrapping.Wrap
            });

            root.Children.Add(new TextBlock
            {
                Text = DescribeConsistency(report.Consistency),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });

            // 「普遍被天花板卡住」要单独解释一句，否则用户会以为程序坏了：
            // 语音峰均比大，要达成 −12 LUFS 得把真峰值放到 +3 以上，天花板却是 −3，
            // 物理上不可能同时满足，loudnorm 只能顶在天花板上。
            if (report.IsBroadlyPeakLimited)
            {
                root.Children.Add(new TextBlock
                {
                    Text = $"有 {report.PeakLimitedCount} 条被峰值天花板卡住：这批素材的峰均比太大，"
                        + "「−12 LUFS」和「峰值 ≤ −3 dBTP」在物理上不能同时满足。"
                        + "要么先压缩/限幅把峰均比降下来，要么把目标调低一点。",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["SystemFillColorCautionBrush"] as Brush
                });
            }

            root.Children.Add(CreateVoiceReportRow(null, isHeader: true));
            var list = new StackPanel { Spacing = 0 };
            foreach (var item in report.Items)
            {
                list.Children.Add(CreateVoiceReportRow(item, isHeader: false));
            }

            root.Children.Add(new ScrollViewer
            {
                Height = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            });

            root.Children.Add(new TextBlock
            {
                Text = report.MeasurementCaveat,
                TextWrapping = TextWrapping.Wrap,
                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style
            });

            return root;
        }

        private static string DescribeConsistency(VoiceConsistencySummary consistency)
        {
            if (consistency.Verdict == VoiceConsistencyVerdict.NotApplicable)
            {
                return "同类一致性：样本不足 2 条，算不了离散度。";
            }

            var spread = consistency.SpreadLu ?? 0d;
            var head = consistency.Verdict == VoiceConsistencyVerdict.Pass ? "通过" : "偏散";
            return $"同类一致性：{head} —— 最响与最轻相差 {spread:0.0} LU"
                + $"（上限 {consistency.ToleranceLu:0.0} LU）。"
                + $"最响 {consistency.LoudestFile}（{consistency.LoudestLufs:0.0}），"
                + $"最轻 {consistency.QuietestFile}（{consistency.QuietestLufs:0.0}）。";
        }

        /// <summary>报告里的一行。<paramref name="item"/> 为 null 时画表头。</summary>
        private static Grid CreateVoiceReportRow(VoiceComplianceItem? item, bool isHeader)
        {
            var grid = new Grid
            {
                Padding = new Thickness(0, 4, 0, 4),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 0, isHeader ? 1 : 0)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

            var primaryBrush = Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
            var secondaryBrush = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;

            var fileName = item is null ? "文件" : item.FileName;
            var loudness = item is null
                ? "积分响度"
                : item.Measurement.IsUsable
                    ? FormatOneDecimal(item.Measurement.IntegratedLufs) + " LUFS"
                    : "—";
            var peak = item is null
                ? "真峰值"
                : item.Measurement.IsUsable
                    ? FormatOneDecimal(item.Measurement.TruePeakDb) + " dBTP"
                    : "—";
            var verdict = item is null ? "判定" : item.VerdictText;

            AddVoiceReportCell(grid, 0, fileName, isHeader, secondaryBrush, TextAlignment.Left);
            AddVoiceReportCell(grid, 1, loudness, isHeader, secondaryBrush, TextAlignment.Right);
            AddVoiceReportCell(grid, 2, peak, isHeader, secondaryBrush, TextAlignment.Right);
            AddVoiceReportCell(grid, 3, verdict, isHeader, isHeader ? primaryBrush : BrushForVerdict(item!), TextAlignment.Right);
            return grid;
        }

        private static void AddVoiceReportCell(
            Grid grid,
            int column,
            string text,
            bool isHeader,
            Brush? foreground,
            TextAlignment alignment)
        {
            var block = new TextBlock
            {
                Text = text,
                FontWeight = isHeader
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = foreground,
                TextAlignment = alignment,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, column == 0 ? 12 : 8, 0)
            };
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        private static Brush? BrushForVerdict(VoiceComplianceItem item)
        {
            if (!item.Measurement.IsUsable)
            {
                return Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush;
            }

            if (item.PeakVerdict == VoicePeakVerdict.Over ||
                item.LoudnessVerdict == VoiceLoudnessVerdict.Off)
            {
                return Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush;
            }

            return item.LoudnessVerdict == VoiceLoudnessVerdict.Near
                ? Application.Current.Resources["SystemFillColorCautionBrush"] as Brush
                : Application.Current.Resources["SystemFillColorSuccessBrush"] as Brush;
        }

        private static string FormatOneDecimal(double value) =>
            value.ToString("0.0", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------------
        // 一键规整语音
        // ---------------------------------------------------------------------

        private async void NormalizeVoiceLoudnessButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isVoiceLoudnessBusy)
            {
                return;
            }

            _isVoiceLoudnessBusy = true;
            try
            {
                await RunVoiceLoudnessNormalizeAsync();
            }
            finally
            {
                _isVoiceLoudnessBusy = false;
            }
        }

        private async Task RunVoiceLoudnessNormalizeAsync()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return;
            }

            var files = GetCurrentChapterVoiceFiles();
            if (files.Count == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "没有可规整的语音",
                    $"「{_selectedTextToolChapter.Code}」的语音文件夹里还没有 wav 文件。");
                return;
            }

            var ffmpegPath = await ResolveFfmpegPathAsync();
            if (ffmpegPath is null)
            {
                return;
            }

            var preset = VoiceLoudnessPreset.GameDialogue;
            var chapterCode = _selectedTextToolChapter.Code;
            var backupDirectory = Path.Combine(
                WorkspacePathUtility.GetProjectAudioBackupsFolderPath(_currentProject),
                $"{DateTime.Now:yyyyMMdd-HHmmss}_{TextUtility.SanitizeCharacterFolderName(chapterCode)}");

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "一键规整语音",
                $"将把「{chapterCode}」的 {files.Count} 条语音就地拉到 {preset.Summary}。\n\n"
                + $"· 原文件会先整章备份到：{backupDirectory}\n"
                + "· 每条语音会重写一遍（16bit WAV，原采样率），文件名与目录不变；\n"
                + "· 处理期间不要关闭程序，可以点环形进度条取消（已处理的条目不会回滚）。",
                "开始规整",
                "取消"));
            if (!confirmed)
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();

            var progress = new Progress<VoiceBatchProgress>(item =>
                UpdateGlobalProgress(
                    $"正在规整 {item.CompletedCount} / {item.TotalCount}",
                    item.OverallFraction * 100d,
                    item.FileName));

            ShowGlobalProgress("一键规整语音", $"正在处理 {files.Count} 条语音…");
            VoiceNormalizeResult result;
            try
            {
                result = await _voiceBatchProcessor.NormalizeAsync(
                    ffmpegPath,
                    files,
                    preset,
                    backupDirectory,
                    progress,
                    GetGlobalProgressCancellationToken());
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("已取消", "语音规整已停止；已处理完的条目已写回磁盘。");
                await HideGlobalProgressAfterDelayAsync();
                return;
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("规整失败", ex.Message);
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "语音规整失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "语音规整失败");
                await HideGlobalProgressAfterDelayAsync();
                return;
            }

            var summary = $"成功 {result.SuccessCount} / {result.Outcomes.Count} 条"
                + (result.FailureCount > 0 ? $"，失败 {result.FailureCount} 条" : string.Empty);
            CompleteGlobalProgress("规整完成", summary);
            AppendLog(LogKind.Info, $"一键规整语音（{chapterCode}）：{summary}");
            await HideGlobalProgressAfterDelayAsync();

            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "语音规整完成",
                BuildVoiceNormalizeResultView(result),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None,
                ConfigureDialog: dialog => dialog.MaxWidth = 760));

            // 文件被原地替换过：让当前页重新读一遍，避免之后试听拿到旧句柄。
            RefreshProjectTextToolPage();
        }

        private static UIElement BuildVoiceNormalizeResultView(VoiceNormalizeResult result)
        {
            var root = new StackPanel
            {
                Spacing = 10,
                MinWidth = 620
            };

            root.Children.Add(new TextBlock
            {
                Text = $"成功 {result.SuccessCount} 条，失败 {result.FailureCount} 条。"
                    + (result.StillOffCount > 0
                        ? $"其中 {result.StillOffCount} 条规整后仍未进容差（多半是被峰值天花板卡住）。"
                        : string.Empty),
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(result.BackupDirectory))
            {
                root.Children.Add(new TextBlock
                {
                    Text = $"原文件备份：{result.BackupDirectory}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                });
            }

            if (result.FailedItems.Count > 0)
            {
                var list = new StackPanel { Spacing = 4 };
                foreach (var item in result.FailedItems)
                {
                    list.Children.Add(new TextBlock
                    {
                        Text = $"{item.FileName}：{item.FailureReason}",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush
                    });
                }

                root.Children.Add(new TextBlock
                {
                    Text = "失败的条目（原文件未改动）：",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                root.Children.Add(new ScrollViewer
                {
                    Height = 180,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = list
                });
            }

            root.Children.Add(new TextBlock
            {
                Text = "规整只改响度与峰值，不改文件名、不改目录，因此语音映射与同步清单都不受影响。",
                TextWrapping = TextWrapping.Wrap,
                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style
            });

            return root;
        }

        // ---------------------------------------------------------------------
        // 设置页：ffmpeg 路径
        // ---------------------------------------------------------------------

        private void UpdateFFmpegStatusText()
        {
            if (FFmpegStatusText is null)
            {
                return;
            }

            var locator = new FfmpegLocator(() => _appSettings.FFmpegPath);
            var resolution = locator.Resolve();
            FFmpegStatusText.Text = resolution.IsFound
                ? $"当前使用：{resolution.ExecutablePath}"
                : resolution.Detail;
            FFmpegStatusText.Foreground = Application.Current.Resources[
                resolution.IsFound ? "TextFillColorSecondaryBrush" : "SystemFillColorCautionBrush"] as Brush;
        }

        private async void ChooseFFmpegPathButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPath = await PickReplacementFileAsync([".exe"], PickerLocationId.ComputerFolder);
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            // 存之前验一次：一个改了名的假 exe 现在挡下来，比批量跑到一半炸便宜得多。
            var locator = new FfmpegLocator(() => null);
            var probe = await locator.ProbeAsync(selectedPath, default);
            if (!probe.IsUsable)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "这个文件不是可用的 ffmpeg",
                    probe.FailureReason ?? "探测失败。");
                return;
            }

            Settings.FFmpegPath = selectedPath;
            PlaySelectionSound();
            UpdateFFmpegStatusText();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "已指定 ffmpeg",
                probe.VersionText,
                logMessage: $"ffmpeg 路径已更新：{selectedPath}");
        }

        private void ClearFFmpegPathButton_Click(object sender, RoutedEventArgs e)
        {
            Settings.FFmpegPath = string.Empty;
            PlaySelectionSound();
            UpdateFFmpegStatusText();
            ShowFloatingTip(
                InfoBarSeverity.Success,
                "已改回随包 ffmpeg",
                "设置里的路径已清空，之后按 Tools\\Audio\\ffmpeg 里的副本查找。");
        }
    }
}
