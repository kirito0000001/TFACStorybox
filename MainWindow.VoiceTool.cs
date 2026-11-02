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
// PickerLocationId 在这个命名空间里。本项目其它 partial 都是逐个文件自己 using，
// 没有 global using —— 漏了就是 CS0103（2026-11-02 编译时就是这么撞的）。
using Windows.Storage.Pickers;

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
        private readonly VoiceLoudnessCacheService _voiceLoudnessCacheService = new();
        private readonly VoiceOriginalArchiveService _voiceOriginalArchive = new();

        /// <summary>
        /// 当前章节每条语音的判定结果，key = 文件名。
        ///
        /// 为什么按「文件名」而不是「行」：同一章里两行引用同一个 wav 是允许的，
        /// 量和判定都只该做一次；行只是它的显示位置。也正因为这样，DB 列的数据范围
        /// 与「检查DB规范」完全一致（整个章节文件夹的 wav），不会出现两套口径。
        /// </summary>
        private readonly Dictionary<string, VoiceComplianceItem> _chapterVoiceLoudness =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前页面上登记的 DB 单元格，key = 行 Id。重铺时必须整体作废。</summary>
        private readonly Dictionary<string, VoiceDbCell> _voiceDbCells =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 「正在跑一批语音」的互斥标记。全局进度条不是模态的，
        /// 双击能把同一章的两批 ffmpeg 叠起来跑 —— 那会把同一批文件写两遍。
        /// </summary>
        private bool _isVoiceLoudnessBusy;

        /// <summary>
        /// 一行语音的「时长 + DB」两格控件。存控件而不是字符串，是为了测量回来能就地回填。
        /// 没设语音的行不登记（只造两个占位横线），所以登记进来的这三个 TextBlock 一定非空。
        /// </summary>
        private sealed record VoiceDbCell(
            string FileName,
            TextBlock DurationText,
            TextBlock LoudnessText,
            TextBlock PeakText,
            TextBlock RangeText);

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

        // ---------------------------------------------------------------------
        // DB 列
        // ---------------------------------------------------------------------

        /// <summary>
        /// 重铺页面时把 DB 列的现场整体作废：控件引用、数字、按钮状态。
        /// 数字随即由 <see cref="PrefillVoiceLoudnessFromCache"/> 从上次手动检测的缓存里读回来
        /// —— 这里**不触发任何测量**：量不量由用户点「检查DB规范」决定。
        /// </summary>
        private void ResetVoiceLoudnessColumnState()
        {
            _voiceDbCells.Clear();
            _chapterVoiceLoudness.Clear();
            if (VoiceLoudnessNormalizeButton is not null)
            {
                // 没有数据时按钮必须是灰的：它现在只在「DB 列里真有超差项」时才亮。
                VoiceLoudnessNormalizeButton.IsEnabled = false;
            }
        }

        /// <summary>表头的时长列（在 DB 前面）。</summary>
        private static Border CreateVoiceDurationHeaderCell()
        {
            var border = new Border
            {
                Padding = new Thickness(8, 8, 8, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new TextBlock
                {
                    Text = "时长",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Right,
                    Foreground = Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
                }
            };
            Grid.SetColumn(border, 2);
            return border;
        }

        /// <summary>表头的 DB 列。列名就叫 DB，下面一行小字说明这三个数字分别是什么。</summary>
        private static Border CreateVoiceDbHeaderCell()
        {
            var panel = new StackPanel { Spacing = 1 };
            panel.Children.Add(new TextBlock
            {
                Text = "DB",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
            });
            panel.Children.Add(new TextBlock
            {
                Text = "LUFS · dBTP · LRA",
                FontSize = 11,
                Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush
            });

            var border = new Border
            {
                Padding = new Thickness(8, 8, 8, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = panel
            };
            Grid.SetColumn(border, 3);
            return border;
        }

        /// <summary>
        /// 一行语音的「时长」与「DB」两格。没设语音的行各给一个灰色横线，
        /// **不登记**进字典（366 行里只有 26 行有语音，省下的控件很可观）。
        /// </summary>
        private (Border DurationCell, Border DbCell) CreateVoiceMetricsCells(ProjectTextRow row, string voicePath)
        {
            if (string.IsNullOrWhiteSpace(voicePath))
            {
                return (
                    WrapVoiceColumn(CreateVoicePlaceholder(), 2),
                    WrapVoiceColumn(CreateVoicePlaceholder(), 3));
            }

            var durationText = CreateVoiceDbValueText(12, TextAlignment.Right);
            var loudnessText = CreateVoiceDbValueText(13, TextAlignment.Right);
            var peakText = CreateVoiceDbValueText(11, TextAlignment.Right);
            var rangeText = CreateVoiceDbValueText(11, TextAlignment.Right);

            // 间距从 8 收到 4：用户的原话是 DB 列「可以收紧一点」，
            // 收的是三个数之间的空隙，不是把数字缩小。
            var grid = new Grid { ColumnSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(loudnessText);
            Grid.SetColumn(peakText, 1);
            grid.Children.Add(peakText);
            Grid.SetColumn(rangeText, 2);
            grid.Children.Add(rangeText);

            var cell = new VoiceDbCell(
                Path.GetFileName(voicePath),
                durationText,
                loudnessText,
                peakText,
                rangeText);
            _voiceDbCells[row.Id] = cell;
            UpdateVoiceDbCell(cell);

            return (WrapVoiceColumn(durationText, 2), WrapVoiceColumn(grid, 3));
        }

        private static TextBlock CreateVoicePlaceholder() => new()
        {
            Text = "—",
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush
        };

        private static Border WrapVoiceColumn(UIElement content, int column)
        {
            var border = new Border
            {
                Padding = new Thickness(8, 8, 8, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = content
            };
            Grid.SetColumn(border, column);
            return border;
        }

        private static TextBlock CreateVoiceDbValueText(double fontSize, TextAlignment alignment) => new()
        {
            FontSize = fontSize,
            TextAlignment = alignment,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Text = string.Empty
        };

        /// <summary>
        /// 刷新一行的「时长 + DB」。三态：
        /// · 没有语音文件 → 灰色横线；
        /// · 没查过（或语音刚被换过、缓存已失效）→ 灰色横线，等用户手动检查；
        /// · 有值 → 时长 + 三个数字（只有 LUFS 染色：红=比目标响、黄=比目标轻、绿=容差内）。
        ///
        /// 颜色**不在这里重新判**，走的是 <see cref="VoiceLoudnessCompliance.ClassifyLoudness"/>，
        /// 与「检查DB规范」同一把尺子 —— 否则会出现「报告说达标、列表却是红的」。
        /// </summary>
        private void UpdateVoiceDbCell(VoiceDbCell cell)
        {
            var mutedBrush = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush;

            if (string.IsNullOrEmpty(cell.FileName) ||
                !_chapterVoiceLoudness.TryGetValue(cell.FileName, out var item))
            {
                SetVoiceDbText(cell, "—", "—", string.Empty, string.Empty, mutedBrush);
                return;
            }

            if (!item.Measurement.IsUsable)
            {
                SetVoiceDbText(
                    cell,
                    FormatVoiceDuration(item.Measurement.Duration),
                    "测不出",
                    string.Empty,
                    string.Empty,
                    mutedBrush);
                return;
            }

            SetVoiceDbText(
                cell,
                FormatVoiceDuration(item.Measurement.Duration),
                item.Measurement.IntegratedLufs.ToString("0.0", CultureInfo.InvariantCulture),
                item.Measurement.TruePeakDb.ToString("0.0", CultureInfo.InvariantCulture),
                item.Measurement.LoudnessRangeLu.ToString("0.0", CultureInfo.InvariantCulture),
                BrushForDbLoudness(item));
        }

        /// <summary>
        /// 时长的显示口径：60 秒以内报「3.2s」（语音几乎都在这一档），更长的退回「1:23」。
        /// 没读到就一条横线，不拿 0 秒冒充。
        /// </summary>
        private static string FormatVoiceDuration(TimeSpan? duration)
        {
            if (duration is not { } value || value <= TimeSpan.Zero)
            {
                return "—";
            }

            return value.TotalSeconds < 60d
                ? value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
                : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
        }

        private static void SetVoiceDbText(
            VoiceDbCell cell,
            string duration,
            string loudness,
            string peak,
            string range,
            Brush? loudnessBrush)
        {
            cell.DurationText.Text = duration;
            cell.DurationText.Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            cell.LoudnessText.Text = loudness;
            cell.LoudnessText.Foreground = loudnessBrush;
            cell.PeakText.Text = peak;
            cell.RangeText.Text = range;
            cell.PeakText.Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush;
            cell.RangeText.Foreground = cell.PeakText.Foreground;
            ToolTipService.SetToolTip(
                cell.LoudnessText,
                string.IsNullOrEmpty(peak)
                    ? "这一行还没有响度数据"
                    : $"时长 {duration} / 积分响度 {loudness} LUFS / 真峰值 {peak} dBTP / 响度范围 {range} LU");
        }

        private static Brush? BrushForDbLoudness(VoiceComplianceItem item)
        {
            var preset = VoiceLoudnessPreset.GameDialogue;

            if (item.LoudnessVerdict != VoiceLoudnessVerdict.Off)
            {
                return Application.Current.Resources["SystemFillColorSuccessBrush"] as Brush;
            }

            // 超出容差之后才分方向：比目标响 = 红（容易在混音里盖住别人），
            // 比目标轻 = 黄（听不见，但通常只是要抬一点）。
            return item.Measurement.IntegratedLufs > preset.TargetLufs
                ? Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush
                : Application.Current.Resources["SystemFillColorCautionBrush"] as Brush;
        }

        private void UpdateVoiceDbCells()
        {
            foreach (var cell in _voiceDbCells.Values)
            {
                UpdateVoiceDbCell(cell);
            }

            UpdateVoiceNormalizeButtonsState();
        }

        /// <summary>
        /// 「规整语音」平时是灰的，只有 DB 列里真有**能修的**超差项才亮。
        ///
        /// 刻意排除「测不出」的条目：它们也会被判成 NeedsFix，但规整同样修不了 ——
        /// 让按钮为它们常亮，等于给用户一个点了也没用的入口。
        /// </summary>
        private void UpdateVoiceNormalizeButtonsState()
        {
            if (VoiceLoudnessNormalizeButton is null)
            {
                return;
            }

            var needsFix = _chapterVoiceLoudness.Values.Any(item =>
                item.Measurement.IsUsable && item.NeedsFix);
            VoiceLoudnessNormalizeButton.IsEnabled = needsFix;
        }

        /// <summary>
        /// 铺行之前把上一次**手动检测**的结果读回来，让第一屏就带数字。
        ///
        /// 这里刻意不测量：量不量由用户点「检查DB规范」决定（2026-11-02 用户定案）。
        /// 缓存按「文件名 + 字节数 + 最后写入时间」判新鲜度，语音被换过就会自然失效，
        /// 那一行退回「—」，等用户下次手动检查。
        /// </summary>
        private void PrefillVoiceLoudnessFromCache()
        {
            if (_projectTextToolMode != ProjectTextToolMode.Voice ||
                _currentProject is null ||
                _selectedTextToolChapter is null)
            {
                return;
            }

            var preset = VoiceLoudnessPreset.GameDialogue;
            var cached = _voiceLoudnessCacheService.Read(_currentProject, _selectedTextToolChapter.Code, preset.TargetLufs);
            if (cached is null || cached.Entries.Count == 0)
            {
                return;
            }

            foreach (var file in GetCurrentChapterVoiceFiles())
            {
                var name = Path.GetFileName(file);
                var info = new FileInfo(file);
                var hit = cached.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.FileName, name, StringComparison.OrdinalIgnoreCase) &&
                    entry.Matches(info.Length, info.LastWriteTimeUtc.Ticks));
                if (hit is not null)
                {
                    _chapterVoiceLoudness[name] = BuildVoiceComplianceItem(file, hit.ToMeasurement(), preset);
                }
            }
        }

        /// <summary>
        /// 用判定层的同一套规则造一条结果，保证「列表里的颜色」与「报告里的判定」同源。
        /// Evaluate 对单条也只是纯算术，开销可以忽略。
        /// </summary>
        private static VoiceComplianceItem BuildVoiceComplianceItem(
            string filePath,
            VoiceLoudnessMeasurement? measurement,
            VoiceLoudnessPreset preset)
        {
            return VoiceLoudnessCompliance.Evaluate(preset, [(filePath, measurement)]).Items[0];
        }

        /// <summary>把当前章节量到的数字写进缓存。只写「文件还在、值有效」的条目。</summary>
        private void WriteVoiceLoudnessCache(IReadOnlyList<string> files, VoiceLoudnessPreset preset)
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return;
            }

            var entries = new List<VoiceLoudnessCacheEntry>(files.Count);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (!_chapterVoiceLoudness.TryGetValue(name, out var item) || !item.Measurement.IsUsable)
                {
                    continue;
                }

                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    continue;
                }

                entries.Add(VoiceLoudnessCacheEntry.FromMeasurement(
                    name,
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    item.Measurement));
            }

            _voiceLoudnessCacheService.Write(
                _currentProject,
                _selectedTextToolChapter.Code,
                preset.TargetLufs,
                entries);
        }

        /// <summary>把一次检查的结论灌进 DB 列并落缓存。</summary>
        private void ApplyVoiceLoudnessReport(VoiceComplianceReport report, IReadOnlyList<string> files)
        {
            foreach (var item in report.Items)
            {
                _chapterVoiceLoudness[item.FileName] = item;
            }

            UpdateVoiceDbCells();
            WriteVoiceLoudnessCache(files, report.Preset);
        }

        /// <summary>
        /// 规整完成后刷新 DB 列：成功的用**规整后实测值**（不是目标值 —— 有些被天花板卡住的
        /// 到不了目标，写目标值就等于骗自己），复量失败的条目直接删掉，让它下次重新测。
        /// </summary>
        private void ApplyVoiceNormalizeResult(VoiceNormalizeResult result)
        {
            foreach (var outcome in result.Outcomes)
            {
                if (!outcome.Succeeded)
                {
                    continue;
                }

                var name = Path.GetFileName(outcome.SourcePath);
                if (outcome.After is { IsUsable: true } after)
                {
                    _chapterVoiceLoudness[name] = BuildVoiceComplianceItem(outcome.SourcePath, after, result.Preset);
                }
                else
                {
                    _chapterVoiceLoudness.Remove(name);
                }
            }

            UpdateVoiceDbCells();
            WriteVoiceLoudnessCache(GetCurrentChapterVoiceFiles(), result.Preset);
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

            // 结论直接写回 DB 列与缓存 —— **不再弹报告窗**（2026-11-02 用户定案：
            // DB 列就是报告，弹窗只多一次点击）。这里只留一条浮条说明总量，
            // 明细靠列表上的颜色与数字自己看，完整统计进 Log。
            ApplyVoiceLoudnessReport(report, files);
            AppendLog(
                LogKind.Info,
                $"语音响度检查（{_selectedTextToolChapter.Code}）：{summary}；{DescribeConsistency(report.Consistency)}");
            await HideGlobalProgressAfterDelayAsync();

            ShowFloatingTip(
                report.NeedsFixItems.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                report.NeedsFixItems.Count > 0 ? "检查完成：有需要规整的语音" : "检查完成：全部达标",
                summary);
        }

        /// <summary>
        /// 一句话描述同类离散度。原来只出现在报告弹窗里；弹窗去掉之后改挂到 Log，
        /// 「这一批里最响和最轻差多少」是没做过标准化时最先要看的那个数。
        /// </summary>
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

        // ---------------------------------------------------------------------
        // 规整语音
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

            // 不再弹确认框（2026-11-02 用户定案）：有「原始版」兜底之后，
            // 「就地覆盖」不再是不可逆操作，确认框只是每次多点一下。
            PlaySelectionSound();
            StopProjectVoicePreview();

            // 只留一份「最初版」：还没有原始版的，先把**当前状态**补存下来（已有的绝不覆盖）。
            // 这一步失败就不开工 —— 没有回退点的批量覆盖不该开始。
            int createdOriginals;
            try
            {
                createdOriginals = _voiceOriginalArchive.EnsureOriginals(_currentProject, chapterCode, files);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "没能建立语音原始版备份",
                    "没有备份就不开始规整。错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "建立语音原始版失败");
                return;
            }

            if (createdOriginals > 0)
            {
                AppendLog(
                    LogKind.Info,
                    $"为 {createdOriginals} 条语音补存了原始版（{chapterCode}）——"
                    + $"存量语音第一次规整前的兜底，之后一直复用这一份。");
            }

            var progress = new Progress<VoiceBatchProgress>(item =>
                UpdateGlobalProgress(
                    $"正在规整 {item.CompletedCount} / {item.TotalCount}",
                    item.OverallFraction * 100d,
                    item.FileName));

            ShowGlobalProgress("规整语音", $"正在处理 {files.Count} 条语音…");
            VoiceNormalizeResult result;
            try
            {
                // 不做整章备份：回退由「原始版」制度负责（每个文件固定一份最初副本）。
                result = await _voiceBatchProcessor.NormalizeAsync(
                    ffmpegPath,
                    files,
                    preset,
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
            AppendLog(LogKind.Info, $"规整语音（{chapterCode}）：{summary}");

            // 就地更新 DB 列（规整后实测值），不重铺页面 —— 重铺会把滚动位置打回顶部，
            // 而这正是素材页那份问题清单里反复挨骂的一条。
            ApplyVoiceNormalizeResult(result);
            await HideGlobalProgressAfterDelayAsync();

            if (result.FailureCount == 0)
            {
                // 全成功只留一条浮条；有失败才弹窗 —— 那几行失败原因必须能逐条读到。
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "规整完成",
                    result.StillOffCount > 0
                        ? $"{summary}；其中 {result.StillOffCount} 条受峰值天花板限制未到目标（详见 DB 列）。"
                        : summary);
                return;
            }

            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "语音规整完成",
                BuildVoiceNormalizeResultView(result, chapterCode),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None,
                ConfigureDialog: dialog => dialog.MaxWidth = 760));
        }

        private UIElement BuildVoiceNormalizeResultView(VoiceNormalizeResult result, string chapterCode)
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

            // 不再有「每次一个时间戳目录」的备份；这里只说清「最初版还在哪」。
            if (_currentProject is not null)
            {
                root.Children.Add(new TextBlock
                {
                    Text = $"原始版仍保存在：{WorkspacePathUtility.GetProjectVoiceOriginalsFolderPath(_currentProject, chapterCode)}"
                        + "（只保留最初那一份，之后规整不会再复制）",
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

        /// <summary>
        /// 右键「还原最初版」：把原始版复制回工作文件。
        ///
        /// 还原之后**旧响度结论必须作废**（文件内容变了），所以顺手把它从 DB 字典里删掉，
        /// 那一行退回横线，等用户再点一次检查 —— 不能让它继续显示还原前的数字。
        /// </summary>
        private async Task RestoreProjectVoiceOriginalAsync(ProjectTextRow row)
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return;
            }

            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) || !File.Exists(voicePath))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可还原的语音", "这一行还没有有效的语音文件。");
                return;
            }

            if (!_voiceOriginalArchive.HasOriginal(_currentProject, _selectedTextToolChapter.Code, voicePath))
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "这条语音没有原始版",
                    "它是在原始版机制之前导入的。下次点「规整语音」会自动把当前状态补存成原始版。");
                return;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "还原最初版",
                $"把「{Path.GetFileName(voicePath)}」还原成导入时的原始版本？\n"
                + "当前的响度处理结果会被覆盖（文件名、备注、映射都不变）。",
                "还原",
                "取消"));
            if (!confirmed)
            {
                return;
            }

            StopProjectVoicePreview();
            try
            {
                if (!_voiceOriginalArchive.TryRestore(_currentProject, _selectedTextToolChapter.Code, voicePath))
                {
                    ShowFloatingTip(InfoBarSeverity.Warning, "还原失败", "原始版文件已经不在了。");
                    return;
                }

                _chapterVoiceLoudness.Remove(Path.GetFileName(voicePath));
                UpdateVoiceDbCells();
                AppendLog(LogKind.Info, $"已还原语音原始版：{Path.GetFileName(voicePath)}");
                PlaySelectionSound();
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "已还原为最初版",
                    $"{Path.GetFileName(voicePath)}，重新点「检查DB规范」可刷新 DB 列。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "还原失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"还原语音原始版失败：{Path.GetFileName(voicePath)}");
            }
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
