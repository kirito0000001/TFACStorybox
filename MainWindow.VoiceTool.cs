using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Controls;
using GalExcleTools.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
// PickerLocationId 在这个命名空间里。本项目其它 partial 都是逐个文件自己 using，
// 没有 global using —— 漏了就是 CS0103（2026-11-02 编译时就是这么撞的）。
using Windows.Media.Playback;
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
        /// 当前页面上的语音按钮，key = 行 Id。
        ///
        /// 为什么要留一份：批量规范命名会改掉文件名，得把这些按钮的文字就地换掉。
        /// 不重铺页面 —— 重铺会把滚动位置打回顶部（那是素材页反复挨骂的一条）。
        /// </summary>
        private readonly Dictionary<string, Button> _voiceButtons =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>一条进度条的两层：白色轨道 + 绿色填充。</summary>
        private sealed record VoiceProgressBar(Border Track, Border Fill);

        /// <summary>
        /// 每行的播放进度条，key = 行 Id。
        ///
        /// ⚠️ 平时是 **Collapsed**：只有当前播放的那一行才亮出来。
        /// 之前把轨道铺在每一行上，整屏都是白线，既难看又让"刚才播的是哪条"失去意义
        /// （2026-11-04 用户指出）。
        /// </summary>
        private readonly Dictionary<string, VoiceProgressBar> _voiceProgressBars =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>每行的试听按钮，key = 行 Id。播放/暂停要换图标，得留个引用。</summary>
        private readonly Dictionary<string, Button> _voicePreviewButtons =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前正在/刚暂停的行 Id。</summary>
        private string? _voicePlayingRowId;

        /// <summary>拖动时鼠标当前悬停在哪一行 —— 用来收掉上一条"悬停白条"。</summary>
        private string? _voiceDropHoverRowId;

        /// <summary>
        /// 上一条"播完 / 已设置"留下的满格绿线属于哪一行。
        ///
        /// 拖动期间这条绿线要**留着**：用户靠它判断刚才听的是哪条、现在拖到哪了。
        /// 所以收白条时不能一把清空，得认出这条是真结果，别碰它。
        /// </summary>
        private string? _voiceCompletedRowId;

        /// <summary>当前是否处于"暂停但可继续"的状态（区别于换了一条重播）。</summary>
        private bool _voicePreviewPaused;

        /// <summary>
        /// 进度条的刷新节拍（约 30fps）。
        ///
        /// 【为什么不用 PositionChanged】那个事件触发得很稀（实测几百毫秒一次），
        /// 拿它直接改 ScaleX，绿条就是一段一段跳的 —— 用户 2026-11-04 反馈"卡卡的"。
        /// 自己起一个 33ms 的定时器读 PlaybackSession.Position，位移才是连续的。
        /// 只在播放期间跑，暂停/结束立刻停表，不占空转。
        /// </summary>
        private DispatcherQueueTimer? _voiceProgressTimer;

        /// <summary>
        /// 已经点了播放、但声音还没真正出来（在解码/缓冲）。
        ///
        /// 这段空档如果让绿条照常走，用户听到声音时进度已经跑到前面去了
        /// （2026-11-04 反馈）。所以绿条要等 PlaybackState 真的到 Playing、
        /// 缓冲满 100% 才开始动 —— 白条自己的 220ms 入场动画正好当作加载指示。
        /// </summary>
        private bool _voiceAwaitingPlayback;

        /// <summary>等待起播的兜底：万一状态回调没来，最多等这么久也开始推进。</summary>
        private DispatcherQueueTimer? _voiceStartWatchdog;

        /// <summary>
        /// 「正在跑一批语音」的互斥标记。全局进度条不是模态的，
        /// 双击能把同一章的两批 ffmpeg 叠起来跑 —— 那会把同一批文件写两遍。
        /// </summary>
        private bool _isVoiceLoudnessBusy;

        /// <summary>
        /// 本次运行里"压过一轮仍没达标"的文件名。规整按钮与目标筛选都会跳过它们，
        /// 避免点了又点、每次都做同一件无效的事。重启进程即清空（重新给一次机会）。
        /// </summary>
        private readonly HashSet<string> _voiceNormalizeBlocked =
            new(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>
        /// 本章有没有"格式不对"的音频（不是 wav/48k/16bit）。
        ///
        /// 只读文件头，不起进程。用来决定「规整语音」按钮要不要亮：
        /// 统一规格已经并进规整流程，如果只在"响度超差"时点亮，
        /// 那一个 44.1k 的合规文件就永远没人能处理。
        /// </summary>
        private bool HasNonCanonicalVoiceFiles()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return false;
            }

            var folderPath = WorkspacePathUtility.GetProjectChapterVoiceFolderPath(
                _currentProject,
                _selectedTextToolChapter.Code);
            if (!Directory.Exists(folderPath))
            {
                return false;
            }

            return Directory
                .EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => ProjectVoiceAssetService.Extensions.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Any(path => !VoiceAudioFormat.IsCanonicalWav(path));
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
            _voiceButtons.Clear();
            _voiceProgressBars.Clear();
            // 重建页面后旧的 Border 引用全失效，必须一起清；
            // 批量模式也退出（选中的行可能已经不存在了）。
            _voiceSelectOverlays.Clear();
            if (_voiceBatchSelectMode)
            {
                ExitVoiceBatchMode();
            }
            _voicePreviewButtons.Clear();
            _voicePlayingRowId = null;
            _voiceDropHoverRowId = null;
            _voiceCompletedRowId = null;
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
        /// 一行语音的「时长」与「DB」两格。
        ///
        /// **每一行都要建、都要登记**，哪怕当前没有语音。
        /// 早先为了省控件，没语音的行只给一个灰色横线、不进字典 ——
        /// 结果新导入一条语音后，这一行根本没有单元格可更新，
        /// 时长/DB 永远是横线，检查多少遍都不会有数字
        /// （2026-11-05 用户实测：新导入的两条怎么点检查都没数据）。
        ///
        /// 代价是每行多几个空 TextBlock；换来的是"行有没有语音"和"行有没有数字"
        /// 彻底解耦，不会再出现这种整行哑掉的死角。
        /// </summary>
        private (Border DurationCell, Border DbCell) CreateVoiceMetricsCells(ProjectTextRow row, string voicePath)
        {
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
                string.IsNullOrWhiteSpace(voicePath) ? string.Empty : Path.GetFileName(voicePath),
                durationText,
                loudnessText,
                peakText,
                rangeText);
            _voiceDbCells[row.Id] = cell;
            UpdateVoiceDbCell(cell);

            return (WrapVoiceColumn(durationText, 2), WrapVoiceColumn(grid, 3));
        }

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
                BrushForDbLoudness(item),
                BrushForDbPeak(item),
                BrushForDbRange(item));
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
            Brush? loudnessBrush,
            Brush? peakBrush = null,
            Brush? rangeBrush = null)
        {
            // 三列各有各的职责：
            // · 积分响度 —— 唯一的"绿灯"，绿=量过且合格（一列绿能看出规整到哪了）；
            // · 真峰值 / LRA —— 平时保持淡色，只在贴近上限(黄)或超标(红)时才染色。
            //   它们不给绿，否则三列全绿会稀释红色的分量。
            var idleBrush = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush;

            cell.DurationText.Text = duration;
            cell.DurationText.Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            cell.LoudnessText.Text = loudness;
            cell.LoudnessText.Foreground = loudnessBrush ?? idleBrush;
            cell.PeakText.Text = peak;
            cell.PeakText.Foreground = peakBrush ?? idleBrush;
            cell.RangeText.Text = range;
            cell.RangeText.Foreground = rangeBrush ?? idleBrush;
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
                // **只有这一列给绿**：一列绿 = "这些是量过且合格的"。
                // 另外两个数字不给绿，否则三列全绿，红色就淹没在绿色里了
                // （2026-11-04 用户明确：三个数值只有一个染绿）。
                return Application.Current.Resources["SystemFillColorSuccessBrush"] as Brush;
            }

            // 超出容差之后才分方向：比目标响 = 红（容易在混音里盖住别人），
            // 比目标轻 = 黄（听不见，但通常只是要抬一点）。
            return item.Measurement.IntegratedLufs > preset.TargetLufs
                ? Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush
                : Application.Current.Resources["SystemFillColorCautionBrush"] as Brush;
        }

        /// <summary>
        /// 真峰值：超标=红；贴着上限（还在容差带里）＝黄；其余不染色。
        /// 峰值最容易在混音里削波，所以超标用红不用黄。
        /// </summary>
        private static Brush? BrushForDbPeak(VoiceComplianceItem item)
        {
            if (item.PeakVerdict == VoicePeakVerdict.Over)
            {
                return Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush;
            }

            // 还没超，但余量只剩量化带那么点 —— 提醒一下，别等下次重导出才炸。
            return item.PeakHeadroomDb <= VoicePeakTolerance.QuantizationDb
                ? Application.Current.Resources["SystemFillColorCautionBrush"] as Brush
                : null;
        }

        /// <summary>响度范围（LRA）：超过上限=红，接近上限=黄，其余不染色。</summary>
        private const double MaxLoudnessRangeLu = 10d;

        private const double WarnLoudnessRangeLu = 8d;

        private static Brush? BrushForDbRange(VoiceComplianceItem item)
        {
            if (item.Measurement.LoudnessRangeLu > MaxLoudnessRangeLu)
            {
                return Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush;
            }

            return item.Measurement.LoudnessRangeLu > WarnLoudnessRangeLu
                ? Application.Current.Resources["SystemFillColorCautionBrush"] as Brush
                : null;
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

            // 「真能试的」才点亮：测不出的、这一轮已压过仍不达标（blocked）的都排除。
            var needsFix = _chapterVoiceLoudness.Any(pair =>
                pair.Value.Measurement.IsUsable &&
                pair.Value.NeedsFix &&
                !_voiceNormalizeBlocked.Contains(pair.Key));

            // 只有"格式不对"时也要能点：统一规格已经并进规整流程了，
            // 不让点的话用户就只能干看着一个 44.1k 文件没法处理。
            VoiceLoudnessNormalizeButton.IsEnabled = needsFix || HasNonCanonicalVoiceFiles();
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

            var healedCount = 0;
            foreach (var file in GetCurrentChapterVoiceFiles())
            {
                var name = Path.GetFileName(file);
                var info = new FileInfo(file);
                var length = info.Length;
                var ticks = info.LastWriteTimeUtc.Ticks;

                var matchedByName = cached.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.FileName, name, StringComparison.OrdinalIgnoreCase) &&
                    entry.Matches(length, ticks));

                // ⚠️ 名字对不上时，按「大小 + 修改时间」再认一次。
                //
                // 改备注会**重命名** wav，而缓存是按文件名索引的 —— 只认名字的话，
                // 改完名字重启，整列「时长 / DB」会突然变横线，用户以为数据丢了
                // （2026-11-04 实测：32 条缓存全在，只是键是旧名）。
                // 重命名不动内容，Length 与 LastWriteTimeUtcTicks 一个都不会变，
                // 这两个值合起来足以当"同一条语音"的身份证。
                var hit = matchedByName ?? cached.Entries.FirstOrDefault(entry => entry.Matches(length, ticks));
                if (hit is null)
                {
                    continue;
                }

                if (matchedByName is null)
                {
                    healedCount++;
                }

                _chapterVoiceLoudness[name] = BuildVoiceComplianceItem(file, hit.ToMeasurement(), preset);
            }

            // 认回来的那些顺手把缓存里的旧名字改成新名字，下次就不用再兜一遍；
            // 顺带把匹配不上的陈旧条目清掉（WriteVoiceLoudnessCache 只写当前还在的文件）。
            if (healedCount > 0)
            {
                WriteVoiceLoudnessCache(GetCurrentChapterVoiceFiles(), preset);
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

        /// <summary>
        /// 语音改名之后，把响度缓存跟着挪到新文件名上。
        ///
        /// 【为什么必须搬】内存字典 <c>_chapterVoiceLoudness</c> 和磁盘缓存
        /// （<c>Tools/VoiceLoudness/{章节}.json</c>）都是**按文件名索引**的：
        /// 改备注会重命名 wav，名字一变旧条目就再也查不到 ——
        /// 表现是这一行的「时长 / DB」全变横线，用户会以为数据丢了（2026-11-04 实测）。
        /// 内容没变、只是名字变了，所以判定结果整份搬过去即可，不用重测。
        /// </summary>
        private void CarryVoiceLoudnessToRenamedFile(string oldPath, string newPath)
        {
            var oldName = Path.GetFileName(oldPath);
            var newName = Path.GetFileName(newPath);
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) ||
                string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_chapterVoiceLoudness.Remove(oldName, out var measured))
            {
                // SourcePath 是 record 的位置参数（init-only），用 with 造一份新的：
                // FileName 是从它推出来的，改它等于改键。
                _chapterVoiceLoudness[newName] = measured with { SourcePath = newPath };
                WriteVoiceLoudnessCache(GetCurrentChapterVoiceFiles(), VoiceLoudnessPreset.GameDialogue);
            }

            // 单元格里也记着旧文件名：不换掉的话，刷新时仍按旧名去查（照样横线）。
            var touched = false;
            foreach (var key in _voiceDbCells.Keys.ToList())
            {
                var cell = _voiceDbCells[key];
                if (string.Equals(cell.FileName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _voiceDbCells[key] = cell with { FileName = newName };
                    touched = true;
                }
            }

            if (touched)
            {
                UpdateVoiceDbCells();
            }
        }

        /// <summary>
        /// 把映射表里指向 <paramref name="oldPath"/> 的条目改到 <paramref name="newPath"/>。
        /// 改名不同步映射，等于把那一行的语音"弄丢" —— 重启后就找不回来了。
        /// </summary>
        private void UpdateVoiceMapPath(string oldPath, string newPath)
        {
            var oldName = Path.GetFileName(oldPath);
            foreach (var key in _projectVoiceMapState.Voices.Keys.ToList())
            {
                var current = _projectVoiceMapState.Voices[key];
                if (string.IsNullOrWhiteSpace(current))
                {
                    continue;
                }

                // 内存里的映射在读入时已经解析成绝对路径（见 ProjectTextDataService.ResolveVoiceMapPaths），
                // 所以先直接比全路径；再退一步比"同目录 + 同名"，兜住分隔符/大小写的差异。
                var samePath = string.Equals(
                    current, oldPath, StringComparison.OrdinalIgnoreCase);
                var sameNameInSameFolder =
                    string.Equals(Path.GetFileName(current), oldName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        Path.GetDirectoryName(Path.GetFullPath(current)),
                        Path.GetDirectoryName(Path.GetFullPath(oldPath)),
                        StringComparison.OrdinalIgnoreCase);

                if (samePath || sameNameInSameFolder)
                {
                    _projectVoiceMapState.Voices[key] = newPath;
                }
            }
        }

        /// <summary>
        /// 把本章语音文件名里「Unreal 不接受的字符」换成 <c>_</c>，返回改了几个。
        ///
        /// 【为什么放在「规整语音」里】导入时资产名是按源文件名清洗出来的
        /// （<see cref="UnrealSyncService.NormalizeImportedAssetName"/>）。源文件名里带着
        /// <c>? . , ! ( )</c> 这些，清洗后资产名和文件名对不上，改名/复扫时就容易打架
        /// （2026-11-03 那条 `Vo-112-…改1＊` 就是例子）。与其在同步那边猜，不如在源头统一。
        ///
        /// 规则**复用同步那一份**，绝不在页面里再列一遍：两份一旦走岔，
        /// "文件名洗成什么样"和"判重时按什么比"就会互相矛盾。
        ///
        /// 冲突（目标已存在）和失败一律跳过并记一条警告 —— 宁可不改，也不覆盖别人。
        /// </summary>
        private int NormalizeVoiceFileNames()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return 0;
            }

            var renamed = 0;
            foreach (var file in GetCurrentChapterVoiceFiles())
            {
                var oldName = Path.GetFileName(file);
                var stem = Path.GetFileNameWithoutExtension(oldName);
                var safeStem = UnrealSyncService.NormalizeImportedAssetName(stem);
                if (string.Equals(stem, safeStem, StringComparison.Ordinal))
                {
                    continue;
                }

                var folder = Path.GetDirectoryName(file)!;
                var targetPath = Path.Combine(folder, safeStem + Path.GetExtension(file));
                if (string.Equals(targetPath, file, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(targetPath))
                {
                    AppendLog(LogKind.Warning, $"语音改名跳过（目标已存在）：{oldName} → {Path.GetFileName(targetPath)}");
                    continue;
                }

                try
                {
                    File.Move(file, targetPath);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    AppendLog(LogKind.Warning, $"语音改名失败：{oldName}", error);
                    continue;
                }

                // 三处都得跟着换：映射（重启后靠它）、缓存键、页面按钮。
                UpdateVoiceMapPath(file, targetPath);
                CarryVoiceLoudnessToRenamedFile(file, targetPath);
                AppendLog(LogKind.Info, $"语音改名：{oldName} → {Path.GetFileName(targetPath)}");
                renamed++;
            }

            if (renamed == 0)
            {
                return 0;
            }

            _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);

            // 按钮文字就地换（刷新整页会把滚动位置打回顶部）。
            foreach (var (rowId, voicePath) in _projectVoiceMapState.Voices)
            {
                if (_voiceButtons.TryGetValue(rowId, out var button))
                {
                    UpdateProjectVoiceButton(button, voicePath);
                }
            }

            return renamed;
        }

        /// <summary>
        /// 清理语音文件夹里的「孤儿文件」：没有任何台词行引用、却还躺在 <c>Voice/{章节}</c> 里的 wav。
        ///
        /// 【为什么会有】多试了一版、改备注后旧名字的残留、手工拷进来的备选……
        /// 它们既会被同步当成多余素材，也会被规整挑中去处理 —— 2026-11-04 用户就撞到一条
        /// `Vo-012-不要S3.wav`：规整反复跑它，表格里却一条都看不到（因为它没挂到任何行）。
        ///
        /// 【为什么不直接删】音频是不可再生的人工产物。一律**移到项目备份目录**并报出路径，
        /// 真要腾空间让人自己去删那个目录 —— 工具不该替人做不可撤销的决定。
        ///
        /// 【怎么判断"没被引用"】拿映射表里的路径做参照，**全路径或同名任一命中就算被引用**。
        /// 宁可漏判（留着），不可错删：映射里可能存相对路径，所以还要按文件名兜一层。
        /// </summary>
        private int CleanupOrphanVoiceFiles(out string backupFolder)
        {
            backupFolder = string.Empty;
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return 0;
            }

            var chapterFolder = WorkspacePathUtility.GetProjectChapterVoiceFolderPath(
                _currentProject, _selectedTextToolChapter.Code);
            if (!Directory.Exists(chapterFolder))
            {
                return 0;
            }

            var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mappedPath in _projectVoiceMapState.Voices.Values)
            {
                if (string.IsNullOrWhiteSpace(mappedPath))
                {
                    continue;
                }

                referencedNames.Add(Path.GetFileName(mappedPath));
                try
                {
                    referencedPaths.Add(Path.GetFullPath(mappedPath));
                }
                catch (Exception)
                {
                    // 路径畸形就只靠文件名兜底，不影响继续扫别的。
                }
            }

            var orphans = Directory
                .EnumerateFiles(chapterFolder, "*.wav", SearchOption.TopDirectoryOnly)
                .Where(file =>
                {
                    var name = Path.GetFileName(file);
                    if (referencedNames.Contains(name))
                    {
                        return false;
                    }

                    try
                    {
                        return !referencedPaths.Contains(Path.GetFullPath(file));
                    }
                    catch (Exception)
                    {
                        // 判断不了就当它被引用 —— 不删。
                        return false;
                    }
                })
                .ToList();

            if (orphans.Count == 0)
            {
                return 0;
            }

            // 备份目录：{项目}/Tools/Backups/VoiceOrphans/{章节}-{时间戳}/
            backupFolder = Path.Combine(
                _currentProject.Path,
                WorkspacePathUtility.ToolsFolderName,
                "Backups",
                "VoiceOrphans",
                $"{_selectedTextToolChapter.Code}-{DateTime.Now:yyyyMMdd-HHmmss}");

            var moved = 0;
            foreach (var orphan in orphans)
            {
                try
                {
                    Directory.CreateDirectory(backupFolder);
                    File.Move(orphan, Path.Combine(backupFolder, Path.GetFileName(orphan)));
                    AppendLog(LogKind.Info, $"清理多余语音：{Path.GetFileName(orphan)} → {backupFolder}");
                    moved++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    AppendLog(LogKind.Warning, $"清理多余语音失败：{Path.GetFileName(orphan)}", error);
                }
            }

            return moved;
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
        /// <summary>
        /// 对"压完仍超差且被峰值卡住"的条目，用 SoundCraft 的 Maximizer 再救一次。
        ///
        /// 只挑 IsPeakLimited 的：普通超差 ffmpeg 自己就能修，没必要多起一个进程。
        /// 找不到 soundcraft-cli 就整体跳过 —— 这是可选增强，不该让规整因此失败。
        /// </summary>
        /// <summary>
        /// 规整的多阶段进度：「当前阶段占总进度的哪一段」由调用方给。
        ///
        /// 为什么按**预估耗时**分配而不是按条目数：去噪一条要 100 秒，
        /// 格式转换一条不到 0.5 秒 —— 平均分的话进度条会先疯跑再卡死。
        /// </summary>
        /// <summary>规整的总计时器 —— 用来在副标题里显示"已用 / 剩余"。</summary>
        private readonly System.Diagnostics.Stopwatch _voiceProgressWatch = new();

        private double _voiceStepBasePercent;

        private double _voiceStepSpanPercent = 100;

        /// <summary>
        /// 影视化（轻度）：把对白往"贴脸、沉稳"的方向推一点。
        ///
        /// 四步缺一不可，顺序也不能换：
        ///   高通 90Hz     去隆隆和喷麦
        ///   250Hz -2.5dB  去胸腔浑浊（"闷"的主要来源）
        ///   3kHz  +2dB    提"存在感"，贴脸感的来源
        ///   去齿音 0.3    提亮之后必须补，否则 s/sh 发刺
        ///   压缩 3:1      句内忽大忽小拉平 —— 专业感最大的来源
        /// 最后再用 loudnorm + alimiter 收回 -12 / -3，保证不破坏规范。
        /// </summary>
        private const string CinematicLightFilter =
            "highpass=f=90,"
            + "equalizer=f=250:width_type=q:w=1.2:g=-2.5,"
            + "equalizer=f=3000:width_type=q:w=1.2:g=2,"
            + "deesser=i=0.3,"
            + "acompressor=threshold=0.1:ratio=3:attack=10:release=150,"
            + "loudnorm=I=-12:TP=-3,"
            + "alimiter=limit=0.695:level=false";

        /// <summary>右键「后期处理」的入口：单行走这里，转成批量接口。</summary>
        private async Task ShowVoicePostProcessDialogAsync(ProjectTextRow row)
        {
            if (_projectVoiceMapState.Voices.TryGetValue(row.Id, out var path) && File.Exists(path))
            {
                await ShowVoicePostProcessDialogAsync([path]);
                return;
            }

            ShowChapterStatus(InfoBarSeverity.Warning, "没有可处理的语音", "请先为这一行设置 wav 文件。");
        }

        /// <summary>
        /// 后期处理弹窗（支持一次处理多条）。
        /// 目前只有「影视化（轻度）」一项，结构留好以后加别的。
        /// </summary>
        private async Task ShowVoicePostProcessDialogAsync(IReadOnlyList<string> paths)
        {
            if (_currentProject is null)
            {
                return;
            }

            var cinematic = new CheckBox
            {
                Content = "影视化（轻度）",
                IsChecked = false,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var form = ToolboxDialog.BuildForm(
                new TextBlock
                {
                    Text = paths.Count == 1
                        ? Path.GetFileName(paths[0])
                        : $"已选中 {paths.Count} 条语音",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                },
                cinematic,
                new TextBlock
                {
                    Text = "高通去隆隆 · 250Hz 减浑浊 · 3kHz 提贴脸 · 去齿音 · 3:1 压缩，"
                        + "最后仍归一到 -12 LUFS / -3 dBTP。",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush
                });

            if (!await ToolboxDialog.ShowAsync(RootGrid.XamlRoot, "后期处理", form, "应用", "取消"))
            {
                return;
            }

            if (cinematic.IsChecked != true)
            {
                return;
            }

            await ApplyCinematicAsync(paths);
        }

        /// <summary>对一批文件套「影视化（轻度）」，逐条报进度。</summary>
        private async Task ApplyCinematicAsync(IReadOnlyList<string> paths)
        {
            var ffmpegPath = await ResolveFfmpegPathAsync();
            if (ffmpegPath is null)
            {
                return;
            }

            _voiceProgressWatch.Restart();
            ShowGlobalProgress("正在应用影视化", $"0 / {paths.Count}");
            var done = 0;
            var failed = 0;

            try
            {
                for (var index = 0; index < paths.Count; index++)
                {
                    var voicePath = paths[index];
                    ReportVoiceStep(
                        "正在应用影视化",
                        (double)index / Math.Max(1, paths.Count),
                        $"{index + 1} / {paths.Count} · {Path.GetFileName(voicePath)}");

                    var temp = voicePath + ".post.wav";
                    try
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = ffmpegPath,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        foreach (var argument in new[]
                        {
                            "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", voicePath,
                            "-af", CinematicLightFilter, "-ar", "48000", "-c:a", "pcm_s16le", temp
                        })
                        {
                            startInfo.ArgumentList.Add(argument);
                        }

                        using var process = Process.Start(startInfo);
                        if (process is null)
                        {
                            continue;
                        }

                        await process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        if (process.ExitCode != 0 || !File.Exists(temp))
                        {
                            AtomicFileWriter.TryDelete(temp);
                            failed++;
                            AppendLog(LogKind.Warning, $"影视化失败：{Path.GetFileName(voicePath)}");
                            continue;
                        }

                        File.Move(temp, voicePath, overwrite: true);
                        done++;

                        // 换了内容：这文件已经去过噪，别让下次规整因为 mtime 变了又重跑 UVR。
                        new VoiceDenoiseCacheService().MarkDenoised(
                            _currentProject, _selectedTextToolChapter!.Code, voicePath);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        AtomicFileWriter.TryDelete(temp);
                        failed++;
                        AppendLog(LogKind.Warning, $"影视化失败：{Path.GetFileName(voicePath)}", error);
                    }
                }

                // 全部处理完再统一复量一次，DB 列才对得上。
                if (done > 0)
                {
                    var report = await _voiceBatchProcessor.MeasureAsync(
                        ffmpegPath, paths, VoiceLoudnessPreset.GameDialogue, null, CancellationToken.None);
                    ApplyVoiceLoudnessReport(report, GetCurrentChapterVoiceFiles());
                }

                CompleteGlobalProgress("影视化完成", $"{done} 条成功" + (failed > 0 ? $"，{failed} 条失败" : string.Empty));
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }

            ShowFloatingTip(
                failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                "已应用影视化",
                $"{done} 条" + (failed > 0 ? $"，{failed} 条失败（详见 Log）" : string.Empty));
        }

        /// <summary>设置弹窗里那个「清空所有音频处理」是否被点过。</summary>
        private bool _voiceResetRequested;

        private UIElement BuildVoiceResetField()
        {
            _voiceResetRequested = false;
            var button = new Button { Content = "清空所有音频处理" };
            button.Click += (_, _) =>
            {
                _voiceResetRequested = true;
                button.Content = "已请求 · 保存后执行";
            };

            // 只放按钮本身：标题和说明都是多余的（按钮文字已经说清了）。
            return button;
        }

        /// <summary>
        /// 把所有语音还原到最初版，并清空两套缓存。
        ///
        /// 只还原**有原始版**的：没有的那几条（手工拷进来的）不能瞎动，
        /// 报出来让用户自己处理。
        /// </summary>
        private async Task ResetAllVoiceProcessingAsync()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return;
            }

            var chapter = _selectedTextToolChapter;
            var archive = new VoiceOriginalArchiveService();
            var files = GetCurrentChapterVoiceFiles();

            var confirm = ToolboxDialog.BuildForm(
                new TextBlock
                {
                    Text = $"将把「{chapter.Code}」的 {files.Count} 条语音还原成导入时的原始版，"
                        + "并清空去噪与响度记录。这个操作不可撤销。",
                    TextWrapping = TextWrapping.Wrap
                });
            if (!await ToolboxDialog.ShowAsync(RootGrid.XamlRoot, "清空所有音频处理", confirm, "确定还原", "取消"))
            {
                return;
            }

            var restored = 0;
            var missing = 0;
            var failed = 0;

            ShowGlobalProgress("还原语音", $"0 / {files.Count}");
            try
            {
                for (var index = 0; index < files.Count; index++)
                {
                    var file = files[index];
                    UpdateGlobalProgress(
                        $"正在还原 {index + 1} / {files.Count}",
                        (double)index / Math.Max(1, files.Count) * 100d,
                        Path.GetFileName(file));

                    if (!archive.HasOriginal(_currentProject, chapter.Code, file))
                    {
                        missing++;
                        continue;
                    }

                    try
                    {
                        File.Copy(archive.GetOriginalPath(_currentProject, chapter.Code, file), file, overwrite: true);
                        restored++;
                    }
                    catch (Exception error)
                    {
                        failed++;
                        AppendLog(LogKind.Warning, $"还原失败：{Path.GetFileName(file)}", error);
                    }
                }

                // 缓存全清：文件内容回到最初，任何旧的测量/去噪结论都不再成立。
                TryDeleteFile(Path.Combine(
                    _currentProject.Path, WorkspacePathUtility.ToolsFolderName, "VoiceDenoise", chapter.Code + ".json"));
                TryDeleteFile(WorkspacePathUtility.GetProjectVoiceLoudnessCachePath(_currentProject, chapter.Code));
                _chapterVoiceLoudness.Clear();

                CompleteGlobalProgress("还原完成", $"{restored} 条已还原");
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }

            ShowFloatingTip(
                failed > 0 || missing > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                "已清空音频处理",
                $"还原 {restored} 条"
                + (missing > 0 ? $"，{missing} 条没有原始版（未动）" : string.Empty)
                + (failed > 0 ? $"，{failed} 条失败（详见 Log）" : string.Empty));

            RefreshProjectTextToolPage();
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 删不掉就算了，下次覆盖写会自然失效。
            }
        }

        private void ReportVoiceStep(string message, double localFraction, string? detail = null)
        {
            var percent = Math.Clamp(
                _voiceStepBasePercent + Math.Clamp(localFraction, 0d, 1d) * _voiceStepSpanPercent,
                0d,
                100d);

            // 进度回调可能来自线程池（服务里用了 ConfigureAwait(false)），
            // 直接改 XAML 会抛 RPC_E_WRONG_THREAD —— 而那个异常会把整批处理打断，
            // 表现就是"卡在某个百分比不动"（2026-11-05 实测）。
            var text = WithTiming(percent / 100d, detail);
            if (DispatcherQueue.HasThreadAccess)
            {
                UpdateGlobalProgress(message, percent, text);
                return;
            }

            DispatcherQueue.TryEnqueue(() => UpdateGlobalProgress(message, percent, text));
        }

        /// <summary>
        /// 把"已用多久 / 大概还要多久"接到副标题尾巴上。
        ///
        /// 剩余时间用 elapsed × (1-f)/f 估：前 3% 不显示 —— 分母太小会算出离谱的数。
        /// </summary>
        private string WithTiming(double fraction, string? detail)
        {
            var text = detail ?? string.Empty;
            if (!_voiceProgressWatch.IsRunning)
            {
                return text;
            }

            var elapsed = _voiceProgressWatch.Elapsed;
            var parts = new List<string>(2);
            if (text.Length > 0)
            {
                parts.Add(text);
            }

            parts.Add("已用 " + elapsed.ToString(@"mm\:ss"));

            if (fraction > 0.03 && fraction < 1)
            {
                var remaining = TimeSpan.FromTicks((long)(elapsed.Ticks * (1 - fraction) / fraction));
                parts.Add("剩余约 " + remaining.ToString(@"mm\:ss"));
            }

            return string.Join(" · ", parts);
        }

        private async Task<List<string>> RescueStillOffWithSoundCraftAsync(VoiceNormalizeResult result, string ffmpegPath)
        {
            var cliPath = SoundCraftRescueService.LocateCli();
            if (cliPath is null || _currentProject is null || _selectedTextToolChapter is null)
            {
                return [];
            }

            var targets = new List<string>();
            foreach (var outcome in result.Outcomes.Where(item => item.Succeeded))
            {
                var name = Path.GetFileName(outcome.SourcePath);
                if (_chapterVoiceLoudness.TryGetValue(name, out var entry) &&
                    entry.Measurement.IsUsable &&
                    entry.NeedsFix &&
                    entry.IsPeakLimited)
                {
                    targets.Add(outcome.SourcePath);
                }
            }

            if (targets.Count == 0)
            {
                return [];
            }

            var service = new SoundCraftRescueService();
            var rescued = new List<string>();
            for (var index = 0; index < targets.Count; index++)
            {
                var path = targets[index];
                ReportVoiceStep(
                    "正在抢救（SoundCraft 限幅）",
                    (double)index / targets.Count,
                    $"{index + 1} / {targets.Count} · {(double)index / targets.Count * 100:0}% · {Path.GetFileName(path)}");

                var tempPath = path + ".rescue.wav";
                try
                {
                    var (succeeded, message) = await service.RescueAsync(
                        cliPath, ffmpegPath, path, tempPath, GetGlobalProgressCancellationToken());

                    if (!succeeded)
                    {
                        AtomicFileWriter.TryDelete(tempPath);
                        AppendLog(LogKind.Warning, $"SoundCraft 救援未成：{Path.GetFileName(path)}（{message}）");
                        continue;
                    }

                    File.Move(tempPath, path, overwrite: true);
                    rescued.Add(path);
                    AppendLog(LogKind.Info, $"SoundCraft 救援：{Path.GetFileName(path)} → {message}");
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    AtomicFileWriter.TryDelete(tempPath);
                    AppendLog(LogKind.Warning, $"SoundCraft 救援失败：{Path.GetFileName(path)}", error);
                }
            }

            return rescued;
        }

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

        /// <summary>
        /// 把本章里不是 wav / 48 kHz / 16 bit 的音频统一成规格，返回（转换数, 失败数）。
        ///
        /// 【为什么并进「规整」而不是单独一个按钮】两者都是"把素材收拾干净"，
        /// 点一次就该把该做的一次做完；多一个按钮只是多一次选择成本。
        ///
        /// 【必须先格式后响度】重采样会改变波形，旧的测量值随之作废 ——
        /// 顺序反了就是拿过期数字下判断。
        ///
        /// 【为什么先备份】44.1k → 48k 是不可逆的重采样（听不出来，但波形确实变了）。
        /// 原文件先复制到 Tools/Backups/VoiceFormat/{章节}-{时间戳}/。
        /// </summary>
        /// <summary>
        /// 用 PyMSS 的 UVR-DeNoise 给本章语音去噪，跳过"已去噪且文件没变"的。
        ///
        /// 【为什么要先问一句】一条 15 秒素材要 100 秒（VR 架构慢），
        /// 26 条将近 45 分钟 —— 不给预估时间，用户会以为卡死了。
        ///
        /// 【为什么去完要复量】文件内容变了，旧的响度结论全部作废；
        /// 不量一遍，接下去的响度规整会因为"没有测量值"直接跳过所有条目。
        /// </summary>
        private async Task<(int Done, int Failed)> RunVoiceDenoiseAsync()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return (0, 0);
            }

            if (!_voiceTrackSettings.UseUvrDenoise)
            {
                return (0, 0);
            }

            var cliPath = PymssDenoiseService.LocateCli();
            var modelDir = PymssDenoiseService.LocateModelDir();
            if (cliPath is null || modelDir is null)
            {
                return (0, 0);
            }

            var chapterCode = _selectedTextToolChapter.Code;
            var cache = new VoiceDenoiseCacheService();
            var files = GetCurrentChapterVoiceFiles();
            var pending = files.Where(file => !cache.IsDenoised(_currentProject, chapterCode, file)).ToList();
            if (pending.Count == 0)
            {
                return (0, 0);
            }

            var ffmpegPath = await ResolveFfmpegPathAsync();
            if (ffmpegPath is null)
            {
                return (0, 0);
            }

            var service = new PymssDenoiseService();
            var workDir = Path.Combine(_currentProject.Path, WorkspacePathUtility.ToolsFolderName, "VoiceDenoise", "work");
            var done = 0;
            var failed = 0;
            var denoisedPaths = new List<string>();

            _voiceStepBasePercent = 10;
            _voiceStepSpanPercent = 72;

            // 整批跑一次：pymss 每次启动都要重新加载模型（十几秒），
            // 一条一进程的话 40 条光加载就白烧 10 分钟。
            var phaseText = "正在准备（拉电平）";
            var batchProgress = 0d;
            var batchDetail = $"{pending.Count} 条待处理";

            void ReportDenoise()
            {
                // 主标题只写"在干什么"，一直在变的东西全放副标题。
                // 最前面那个百分比给"这一条跑到哪"（pymss 的 tqdm 值），
                // 整体进度放后面 —— 条内的数字跳得快，才看得出它没卡。
                ReportVoiceStep(
                    phaseText,
                    batchProgress,
                    $"{batchDetail} · 总 {batchProgress * 100:0}%");
            }

            ReportDenoise();
            var batch = await service.DenoiseBatchAsync(
                cliPath, modelDir, ffmpegPath, pending, workDir,
                (value, detail) =>
                {
                    batchProgress = Math.Clamp(value, 0d, 1d);
                    batchDetail = detail;
                    ReportDenoise();
                },
                phase =>
                {
                    phaseText = phase;
                    ReportDenoise();
                },
                GetGlobalProgressCancellationToken());

            foreach (var file in batch.Succeeded)
            {
                cache.MarkDenoised(_currentProject, chapterCode, file);
                denoisedPaths.Add(file);
                done++;
            }

            foreach (var (file, reason) in batch.Failed)
            {
                failed++;
                AppendLog(LogKind.Warning, $"去噪失败：{Path.GetFileName(file)}（{reason}）");
            }

            {
                if (denoisedPaths.Count > 0)
                {
                    // 内容变了 → 复量，否则后面的响度规整没有依据（会一条都不选）。
                    var report = await _voiceBatchProcessor.MeasureAsync(
                        ffmpegPath, denoisedPaths, VoiceLoudnessPreset.GameDialogue, null,
                        GetGlobalProgressCancellationToken());
                    ApplyVoiceLoudnessReport(report, GetCurrentChapterVoiceFiles());
                }

            }

            return (done, failed);
        }

        private async Task<(int Converted, int Failed)> UnifyVoiceFormatsAsync()
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return (0, 0);
            }

            var chapterCode = _selectedTextToolChapter.Code;
            var folder = WorkspacePathUtility.GetProjectChapterVoiceFolderPath(_currentProject, chapterCode);
            if (!Directory.Exists(folder))
            {
                return (0, 0);
            }

            // 只扫顶层：Originals / 备份都在别的目录，别把它们当成待转换素材。
            var candidates = Directory
                .EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => ProjectVoiceAssetService.Extensions.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => !VoiceAudioFormat.IsCanonicalWav(path))
                .ToList();

            if (candidates.Count == 0)
            {
                return (0, 0);
            }

            var ffmpegPath = await ResolveFfmpegPathAsync();
            if (ffmpegPath is null)
            {
                return (0, 0);
            }

            var backupFolder = Path.Combine(
                _currentProject.Path,
                WorkspacePathUtility.ToolsFolderName,
                "Backups",
                "VoiceFormat",
                $"{chapterCode}-{DateTime.Now:yyyyMMdd-HHmmss}");

            _voiceStepBasePercent = 82;
            _voiceStepSpanPercent = 6;

            var converted = 0;
            var failed = 0;
            var convertedIndex = 0;
            foreach (var file in candidates)
            {
                convertedIndex++;
                ReportVoiceStep(
                    "正在统一格式",
                    (double)(convertedIndex - 1) / Math.Max(1, candidates.Count),
                    $"{convertedIndex} / {candidates.Count} · {(double)(convertedIndex - 1) / Math.Max(1, candidates.Count) * 100:0}% · {Path.GetFileName(file)}");

                var name = Path.GetFileName(file);
                var tempPath = file + ".converting.wav";
                try
                {
                    Directory.CreateDirectory(backupFolder);
                    File.Copy(file, Path.Combine(backupFolder, name), overwrite: true);

                    await Task.Run(() => VoiceAudioFormat.Convert(ffmpegPath, file, tempPath, out _));
                    File.Move(tempPath, file, overwrite: true);

                    // 测量值**保留**：重采样对积分响度的影响在 0.1 dB 量级，
                    // 远小于 ±2 的容差。作废掉的话，整章刚转完就全变成"未检查"，
                    // 用户还得再点一次检查 —— 收益没有，麻烦一件。
                    // 下面的 WriteVoiceLoudnessCache 会按新的大小/时间重新记一次身份。
                    converted++;
                }
                catch (Exception error)
                {
                    failed++;
                    AtomicFileWriter.TryDelete(tempPath);
                    AppendLog(LogKind.Warning, $"统一规格失败：{name}", error);
                }
            }

            if (converted > 0)
            {
                // 缓存按「大小 + 修改时间」判新鲜度，本来也会失效；
                // 这里显式重写一次，免得磁盘上留着一份"内容已经变了"的数字。
                WriteVoiceLoudnessCache(GetCurrentChapterVoiceFiles(), VoiceLoudnessPreset.GameDialogue);
            }

            return (converted, failed);
        }

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
            catch (OperationCanceledException)
            {
                // 取消可能发生在去噪阶段 —— 那一段在原来的 try 之外，
                // 没人接这个异常，浮条就永远停在「正在取消…」。
                CompleteGlobalProgress("已取消", "语音规整已停止；已完成的条目已写回磁盘。");
                await HideGlobalProgressAfterDelayAsync();
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

            // ⓪ 先清理「没挂到任何行」的孤儿文件。
            //
            //    它们在文件夹里既会被同步当成多余素材，也会被规整挑中去处理 ——
            //    而表格里一条都看不到（没有行），用户只会觉得"这工具在乱跑"。
            //    移到备份目录而不是直接删：音频是不可再生的东西。
            var orphanCount = CleanupOrphanVoiceFiles(out var orphanBackupFolder);
            if (orphanCount > 0)
            {
                AppendLog(LogKind.Info, $"已清理 {orphanCount} 个多余语音文件，备份在：{orphanBackupFolder}");
            }

            // ⓪.5 统一音频规格（wav / 48k / 16bit）。
            //
            // 放在响度之前：格式一变，原来量到的数字就作废了（重采样改变了波形），
            // 先统一再量，顺序才对。规整自己落盘已经是 48k，所以这里只需要处理
            // "格式不对、但这次又不会被规整到"的那些（合规/未检查的）。
            _voiceProgressWatch.Restart();
            ShowGlobalProgress("语音规整", "准备中…");

            // ① 去噪（PyMSS UVR-DeNoise）。放在最前面：噪声要先清掉，
            //    后面拉响度才不会被一起推高（底噪跟着涨是这批素材最初的问题）。
            var (denoised, denoiseFailed) = await RunVoiceDenoiseAsync();

            var (formatConverted, formatFailed) = await UnifyVoiceFormatsAsync();
            if (formatConverted > 0 || formatFailed > 0)
            {
                AppendLog(
                    LogKind.Info,
                    $"统一音频规格：转换 {formatConverted} 条，失败 {formatFailed} 条。");
            }

            // ① 先把文件名规范掉（Unreal 非法字符 → _），再谈响度。
            //
            // 顺序不能反：改名会换掉缓存键与映射路径，先改完名再规整，
            // 缓存是**一次性重建**的，不会出现"规整写了一份、又被改名搬一次"的往返。
            var renamedCount = NormalizeVoiceFileNames();
            if (renamedCount > 0)
            {
                AppendLog(LogKind.Info, $"已规范 {renamedCount} 个语音文件名（Unreal 非法字符 → _）。");
            }

            var allFiles = GetCurrentChapterVoiceFiles();
            if (allFiles.Count == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "没有可规整的语音",
                    $"「{_selectedTextToolChapter.Code}」的语音文件夹里还没有 wav 文件。");
                return;
            }

            // ★ 只挑**要修的那几条**（2026-11-02 用户指出：原来把整章都重写了一遍）。
            //   · 已达标的重写没有收益，还会把它从「稳」推到「贴边」；
            //   · 没测过的（DB 显示横线）不能瞎改 —— 不知道原来是多少，改完也没法证明变好了。
            //   判定沿用与 DB 列同一把尺子（NeedsFix = 响度出容差 或 峰值超标）。
            var targets = new List<string>();
            var compressTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skippedCompliant = 0;
            var skippedUnmeasured = 0;
            var skippedBlocked = 0;
            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileName(file);
                if (_chapterVoiceLoudness.TryGetValue(fileName, out var item) &&
                    item.Measurement.IsUsable)
                {
                    if (!item.NeedsFix)
                    {
                        skippedCompliant++;
                        continue;
                    }

                    // 上一轮已经"压缩 + 归一"过仍是超差的：本次运行不再重试。
                    // 没有这条，按钮会一直亮、每点一次都重跑同一件无效的事
                    // （2026-11-04 实测的那个循环，只是换了触发条件）。
                    if (_voiceNormalizeBlocked.Contains(fileName))
                    {
                        skippedBlocked++;
                        continue;
                    }

                    targets.Add(file);

                    // 被峰值卡住的：只套增益必然撞回上限，先压动态才有机会到目标。
                    if (item.IsPeakLimited)
                    {
                        compressTargets.Add(file);
                    }
                }
                else
                {
                    // 没测过 / 测不出：一律不碰。
                    skippedUnmeasured++;
                }
            }

            if (targets.Count == 0)
            {
                if (formatConverted > 0)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Success,
                        "格式已统一",
                        $"统一了 {formatConverted} 个文件的格式；响度上没有需要规整的条目。");
                    await HideGlobalProgressAfterDelayAsync();
                    return;
                }

                ShowFloatingTip(
                    skippedBlocked > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                    skippedBlocked > 0 ? "没有可再规整的语音" : "没有需要规整的语音",
                    $"{allFiles.Count} 条里没有可规整的超差项（达标 {skippedCompliant} 条"
                    + (skippedUnmeasured > 0 ? $"，未检查 {skippedUnmeasured} 条" : string.Empty)
                    + (skippedBlocked > 0
                        ? $"，{skippedBlocked} 条已压过一轮仍到不了目标 —— 需要回 DAW 重做素材"
                        : string.Empty)
                    + "）。先点「检查DB规范」再回来。");
                await HideGlobalProgressAfterDelayAsync();
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
            // 只给**这一轮真要动的文件**建备份 —— 不动的文件不必占第二份磁盘。
            // 这一步失败就不开工 —— 没有回退点的批量覆盖不该开始。
            int createdOriginals;
            try
            {
                createdOriginals = _voiceOriginalArchive.EnsureOriginals(_currentProject, chapterCode, targets);
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

            _voiceStepBasePercent = 90;
            _voiceStepSpanPercent = 10;
            var progress = new Progress<VoiceBatchProgress>(item =>
                ReportVoiceStep(
                    "正在修响度",
                    item.OverallFraction,
                    $"{item.CompletedCount} / {item.TotalCount} · {item.FileName}"));
            VoiceNormalizeResult result;
            try
            {
                // 不做整章备份：回退由「原始版」制度负责（每个文件固定一份最初副本）。
                result = await _voiceBatchProcessor.NormalizeAsync(
                    ffmpegPath,
                    targets,
                    preset,
                    progress,
                    GetGlobalProgressCancellationToken(),
                    compressTargets);
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

            // 「跳过了哪些」也要报出来：用户上一版正是看到「全都跑了一遍」才发现问题的，
            // 反过来若只跑 3 条却不说清，也会让人怀疑漏了。
            var skipParts = new List<string>();
            if (skippedCompliant > 0)
            {
                skipParts.Add($"{skippedCompliant} 条已达标");
            }

            if (skippedUnmeasured > 0)
            {
                skipParts.Add($"{skippedUnmeasured} 条未检查");
            }

            if (skippedBlocked > 0)
            {
                // 必须说清楚：这些行在 DB 列里还是红的，但不是"没处理到"，
                // 而是"这一轮已经压过、仍到不了目标"—— 只能回 DAW 重做素材。
                skipParts.Add($"{skippedBlocked} 条压过一轮仍未达标（需重做素材）");
            }

            var skipNote = skipParts.Count == 0
                ? string.Empty
                : $"（未处理：{string.Join("、", skipParts)}）";

            var summary = (orphanCount > 0 ? $"清理 {orphanCount} 个多余文件；" : string.Empty)
                + (renamedCount > 0 ? $"已规范 {renamedCount} 个文件名；" : string.Empty)
                + (denoised > 0 ? $"去噪 {denoised} 条；" : string.Empty)
                + (denoiseFailed > 0 ? $"去噪失败 {denoiseFailed} 条；" : string.Empty)
                + (formatConverted > 0 ? $"统一 {formatConverted} 个文件格式；" : string.Empty)
                + (compressTargets.Count > 0 ? $"{compressTargets.Count} 条先压缩再拉响度；" : string.Empty)
                + $"成功 {result.SuccessCount} / {result.Outcomes.Count} 条"
                + (result.FailureCount > 0 ? $"，失败 {result.FailureCount} 条" : string.Empty)
                + (formatFailed > 0 ? $"，{formatFailed} 条格式转换失败（详见 Log）" : string.Empty)
                + skipNote;
            CompleteGlobalProgress("规整完成", summary);
            AppendLog(LogKind.Info, $"规整语音（{chapterCode}）：{summary}");

            // 就地更新 DB 列（规整后实测值），不重铺页面 —— 重铺会把滚动位置打回顶部，
            // 而这正是素材页那份问题清单里反复挨骂的一条。
            ApplyVoiceNormalizeResult(result);

            // ② 第二套：ffmpeg 拉不动的（撞在真峰值天花板上），交给 SoundCraft 的 Maximizer。
            //    它的限幅器有前视与释放曲线，比 ffmpeg 能多压 1~2 dB（2026-11-05 实测 25/26 达标）。
            _voiceStepBasePercent = 96;
            _voiceStepSpanPercent = 3;
            var rescuedPaths = await RescueStillOffWithSoundCraftAsync(result, ffmpegPath);
            if (rescuedPaths.Count > 0)
            {
                // 换过文件内容 → 必须重新量一遍，否则 DB 列显示的还是旧值。
                var rescueReport = await _voiceBatchProcessor.MeasureAsync(
                    ffmpegPath, rescuedPaths, preset, null, GetGlobalProgressCancellationToken());
                // ⚠️ 第二个参数是"要写进缓存的文件列表"，不是"这次量了哪些"。
                // 只传被救的那几条的话，整章缓存会被覆盖成只剩它们。
                ApplyVoiceLoudnessReport(rescueReport, GetCurrentChapterVoiceFiles());
                AppendLog(LogKind.Info, $"SoundCraft 救援：{rescuedPaths.Count} 条已重做并复测。");
            }

            // 这一轮压完仍超差的，记下来：本次运行不要再把它们选进目标。
            // 否则就是"点一次做一遍、屏幕上看不出任何变化、按钮还亮着"的死循环 ——
            // 2026-11-04 用户正是这么撞上的（当时是因为被峰值卡住）。
            foreach (var outcome in result.Outcomes.Where(item => item.Succeeded))
            {
                var name = Path.GetFileName(outcome.SourcePath);
                if (_chapterVoiceLoudness.TryGetValue(name, out var entry) &&
                    entry.Measurement.IsUsable &&
                    entry.NeedsFix)
                {
                    _voiceNormalizeBlocked.Add(name);
                }
            }

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
                BuildVoiceNormalizeResultView(result, chapterCode, skipNote),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None,
                ConfigureDialog: dialog => dialog.MaxWidth = 760));
        }

        private UIElement BuildVoiceNormalizeResultView(VoiceNormalizeResult result, string chapterCode, string skipNote)
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
                        : string.Empty)
                    + (string.IsNullOrEmpty(skipNote) ? string.Empty : skipNote),
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

        /// <summary>播放状态变了：确认真的出声了，才让绿条开始走。</summary>
        private void HandleVoicePlaybackStateChanged()
        {
            if (!_voiceAwaitingPlayback)
            {
                return;
            }

            var session = _projectVoicePreviewPlayer.PlaybackSession;
            if (session.PlaybackState != MediaPlaybackState.Playing || session.BufferingProgress < 1d)
            {
                return;
            }

            BeginVoiceProgress();
        }

        /// <summary>结束"等起播"，把绿条对齐到真实位置后开始推进。</summary>
        private void BeginVoiceProgress()
        {
            _voiceAwaitingPlayback = false;
            _voiceStartWatchdog?.Stop();

            // 先对齐一次：缓冲期间 Position 可能已经挪了一点，直接让绿条跳到位，
            // 而不是从 0 慢慢追（那样又会"看着比声音快"）。
            UpdateVoicePlaybackProgress();
            StartVoiceProgressTimer();
        }

        /// <summary>点下播放后开始等：状态回调没来也不能让绿条永远不走。</summary>
        private void AwaitVoicePlaybackStart()
        {
            _voiceAwaitingPlayback = true;
            StopVoiceProgressTimer();

            _voiceStartWatchdog ??= CreateVoiceStartWatchdog();
            _voiceStartWatchdog.Stop();
            _voiceStartWatchdog.Start();
        }

        private DispatcherQueueTimer CreateVoiceStartWatchdog()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_voiceAwaitingPlayback)
                {
                    BeginVoiceProgress();
                }
            };
            return timer;
        }

        private void StartVoiceProgressTimer()
        {
            _voiceProgressTimer ??= CreateVoiceProgressTimer();
            _voiceProgressTimer.Stop();
            _voiceProgressTimer.Start();
        }

        private void StopVoiceProgressTimer()
        {
            _voiceProgressTimer?.Stop();
        }

        private DispatcherQueueTimer CreateVoiceProgressTimer()
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.IsRepeating = true;
            timer.Tick += (_, _) => UpdateVoicePlaybackProgress();
            return timer;
        }

        /// <summary>
        /// 按播放进度更新当前行的绿条（ScaleX = 已播 / 总长）。
        ///
        /// 用缩放而不是改 Width：不必等布局算出行宽，也不会每帧触发一次布局 ——
        /// 一屏几百行时，后者会把滚动搞卡。
        /// </summary>
        /// <summary>
        /// 上一次刷新后**屏幕上真实显示的行**（已按当前筛选过滤，顺序即显示顺序）。
        ///
        /// 批量拖放靠它落位：用户看到的是这一屏，就按这一屏从上往下填。
        /// </summary>
        private List<ProjectTextRow> _visibleVoiceRows = [];

        /// <summary>
        /// 悬停到某一行：收掉**上一条悬停白条**，点亮这一行。
        ///
        /// 拖动时允许"已完成的那条绿线 + 当前悬停白条"同时存在（绿线是位置参照），
        /// 但绝不能越拖越多 —— 之前 DragLeave 不可靠时就会留下一串白条
        /// （2026-11-05 用户实测：拖了两个，两条都一直亮着）。
        /// </summary>
        private void SetVoiceDropHover(string rowId)
        {
            if (string.Equals(_voiceDropHoverRowId, rowId, StringComparison.Ordinal))
            {
                return;
            }

            var previous = _voiceDropHoverRowId;
            _voiceDropHoverRowId = rowId;

            if (previous is not null)
            {
                RestoreVoiceProgressVisual(previous);
            }

            ShowVoiceProgressBar(rowId);
        }

        private void ClearVoiceDropHover(string rowId)
        {
            if (!string.Equals(_voiceDropHoverRowId, rowId, StringComparison.Ordinal))
            {
                return;
            }

            _voiceDropHoverRowId = null;
            RestoreVoiceProgressVisual(rowId);
        }

        /// <summary>把某一行恢复成"它本该有的样子"：播放中/已播过的保持，其余收掉。</summary>
        private void RestoreVoiceProgressVisual(string rowId)
        {
            if (string.Equals(_voicePlayingRowId, rowId, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(_voiceCompletedRowId, rowId, StringComparison.Ordinal) &&
                _voiceProgressBars.TryGetValue(rowId, out var done))
            {
                if (done.Track.RenderTransform is ScaleTransform trackScale)
                {
                    trackScale.ScaleX = 1;
                }

                done.Track.Opacity = 1;
                done.Track.Visibility = Visibility.Visible;
                done.Fill.Visibility = Visibility.Visible;

                if (done.Fill.RenderTransform is ScaleTransform fillScale)
                {
                    fillScale.ScaleX = 1;
                }

                return;
            }

            HideVoiceProgressBar(rowId);
        }

        /// <summary>
        /// 只留一条线：把除 <paramref name="keepRowId"/> 之外所有行的进度条收掉。
        ///
        /// **只在"落下之后"调用**。拖拽过程中千万别清 —— 旧的那条绿线是位置参照，
        /// 拖的时候还看得见自己上一次处理的是哪条，心里才有数
        /// （2026-11-05 用户明确：拖动时旧绿条留着，落了只能显示一个）。
        /// </summary>
        private void HideAllVoiceProgressBarsExcept(string? keepRowId)
        {
            foreach (var pair in _voiceProgressBars)
            {
                if (string.Equals(pair.Key, keepRowId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (pair.Value.Track.Visibility == Visibility.Visible ||
                    pair.Value.Fill.Visibility == Visibility.Visible)
                {
                    HideVoiceProgressBar(pair.Key);
                }
            }
        }

        /// <summary>
        /// 把某一行的进度条直接标成"满格绿"（播完的样子）。
        /// 拖入但不开自动试听时用：不发声，也让这一行和"已经播过"看起来一致。
        /// </summary>
        private void MarkVoiceProgressComplete(string rowId)
        {
            if (!_voiceProgressBars.TryGetValue(rowId, out var bar))
            {
                return;
            }

            _voiceCompletedRowId = rowId;
            HideAllVoiceProgressBarsExcept(rowId);

            if (bar.Track.RenderTransform is ScaleTransform trackScale)
            {
                trackScale.ScaleX = 1;
            }

            bar.Track.Opacity = 1;
            bar.Track.Visibility = Visibility.Visible;
            bar.Fill.Visibility = Visibility.Visible;

            if (bar.Fill.RenderTransform is ScaleTransform fillScale)
            {
                fillScale.ScaleX = 1;
            }
        }

        /// <summary>
        /// 这一行的语音文件换了名字 → 把 DB 单元格的查询键一起换掉。
        ///
        /// <see cref="VoiceDbCell"/> 的 FileName 是建行时定下的（只读记录字段），
        /// 而响度按文件名索引；换语音 / 改备注改名 / 移除之后不重绑，
        /// 那一行的「时长 / DB」就会永远横线，哪怕刚检查过
        /// （2026-11-05 用户实测：新导入的两条怎么检查都没数字）。
        /// </summary>
        private void RebindVoiceDbCell(string rowId, string voicePath)
        {
            if (!_voiceDbCells.TryGetValue(rowId, out var cell))
            {
                return;
            }

            var rebound = cell with
            {
                FileName = string.IsNullOrWhiteSpace(voicePath) ? string.Empty : Path.GetFileName(voicePath)
            };

            _voiceDbCells[rowId] = rebound;
            UpdateVoiceDbCell(rebound);
        }

        private bool VoiceRowHasFile(string rowId) =>
            _projectVoiceMapState.Voices.TryGetValue(rowId, out var path) &&
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path);

        /// <summary>从头播一遍某一行（拖入后自动试听用；已经在播就先复位再重来）。</summary>
        private async Task PlayVoicePreviewFromStartAsync(ProjectTextRow row)
        {
            if (string.Equals(_voicePlayingRowId, row.Id, StringComparison.Ordinal))
            {
                ResetVoicePreviewProgress();
            }

            await ToggleProjectVoicePreviewAsync(row);
        }

        /// <summary>
        /// 把一份音频设成某一行的语音 —— **点击选择与拖放共用这一条路径**。
        ///
        /// 两边各写一遍的后果很具体：格式转换、原始版归档、映射落盘、按钮刷新，
        /// 迟早有一处只在其中一条路上被改过，然后"拖进来和点进来行为不一样"。
        /// </summary>
        private async Task<bool> ImportVoiceIntoRowAsync(ProjectTextRow row, int rowCount, string sourcePath)
        {
            if (_currentProject is null)
            {
                return false;
            }

            var isReplacing = _projectVoiceMapState.Voices.TryGetValue(row.Id, out var existingPath) &&
                !string.IsNullOrWhiteSpace(existingPath);

            try
            {
                // 已是统一规格的 wav 直接拷；其余格式先转 48k/16bit。
                // 只有需要转码时才去找 ffmpeg，纯 wav 用户不会被弹"没有找到 ffmpeg"。
                string? importFfmpegPath = null;
                if (!VoiceAudioFormat.IsCanonicalWav(sourcePath))
                {
                    importFfmpegPath = await ResolveFfmpegPathAsync();
                    if (importFfmpegPath is null)
                    {
                        return false;
                    }
                }

                var importedPath = _projectVoiceAssetService.ImportVoice(
                    _currentProject, row, rowCount, sourcePath, _projectVoiceMapState, importFfmpegPath);
                _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
                TouchProjectLastEditedAt(_currentProject);

                if (_voiceButtons.TryGetValue(row.Id, out var button))
                {
                    UpdateProjectVoiceButton(button, importedPath);
                }

                if (_voicePreviewButtons.TryGetValue(row.Id, out var previewButton))
                {
                    UpdateProjectVoicePreviewButton(previewButton, importedPath);
                }

                RebindVoiceDbCell(row.Id, importedPath);

                PlaySelectionSound();
                AppendLog(
                    LogKind.Info,
                    $"{(isReplacing ? "更换" : "设置")}语音文件：{row.Id} <- {Path.GetFileName(sourcePath)}");
                return true;
            }
            catch (Exception error)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    isReplacing ? "语音文件更换失败" : "语音文件设置失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    error,
                    $"{(isReplacing ? "更换" : "设置")}语音文件失败：{row.Id}");
                return false;
            }
        }

        /// <summary>
        /// 拖进来的文件落地：
        /// · 1 个 → 直接设给拖到的那一行；
        /// · 多个 → 从拖到的那一行开始，**按当前筛选结果的显示顺序**依次往下填。
        ///
        /// 【为什么不按文件名里的行号匹配】文件名是别人给的，靠不住；
        /// 而"屏幕上第几条"是用户自己看得见、可以预期的。
        /// 文件顺序取自拖入时选择的顺序，不再二次排序。
        /// </summary>
        private async Task HandleVoiceFilesDroppedAsync(
            ProjectTextRow row,
            int rowCount,
            IReadOnlyList<string> files)
        {
            if (files.Count == 1)
            {
                if (!await ImportVoiceIntoRowAsync(row, rowCount, files[0]))
                {
                    return;
                }

                // 落定：先收掉其它行的线，只留这一条。
                HideAllVoiceProgressBarsExcept(row.Id);

                if (_voiceTrackSettings.PlayAfterDropImport)
                {
                    // 播一遍：绿条跟着走，播完自然停在满格 —— 和"点试听"完全一致。
                    await PlayVoicePreviewFromStartAsync(row);
                }
                else
                {
                    // 不播：线直接变绿，标识"这条已经设好了"。
                    MarkVoiceProgressComplete(row.Id);
                }

                ShowFloatingTip(InfoBarSeverity.Success, "语音已设置", Path.GetFileName(files[0]));
                return;
            }

            var visible = _visibleVoiceRows;
            var startIndex = visible.FindIndex(item => string.Equals(item.Id, row.Id, StringComparison.Ordinal));
            if (startIndex < 0)
            {
                startIndex = 0;
            }

            var slot = startIndex;
            var placed = 0;
            var failed = 0;
            var overflow = 0;

            foreach (var file in files)
            {
                if (slot >= visible.Count)
                {
                    overflow++;
                    continue;
                }

                // 每个文件都占一个位置：失败也往下走，
                // 否则"中间错一条、后面全体错位"更难查。
                var target = visible[slot];
                slot++;

                if (await ImportVoiceIntoRowAsync(target, rowCount, file))
                {
                    placed++;
                }
                else
                {
                    failed++;
                }
            }

            // 落定后只留一条线，而且只处理**拖到的那一行** —— 它才是这次操作的落点。
            // 二十条全绿会把"刚才听的是哪条"这个信号淹掉。
            if (placed > 0 && VoiceRowHasFile(row.Id))
            {
                HideAllVoiceProgressBarsExcept(row.Id);

                if (_voiceTrackSettings.PlayAfterDropImport)
                {
                    await PlayVoicePreviewFromStartAsync(row);
                }
                else
                {
                    MarkVoiceProgressComplete(row.Id);
                }
            }
            else
            {
                // 一条都没落成 / 落点那行没文件：把悬停时的白条收掉，别留个空壳。
                HideVoiceProgressBar(row.Id);
            }

            var message = $"从当前行起，按显示顺序设置 {placed} 条";
            if (failed > 0)
            {
                message += $"；{failed} 条导入失败（详见 Log）";
            }

            if (overflow > 0)
            {
                message += $"；{overflow} 个文件超出列表末尾没放下";
            }

            ShowFloatingTip(
                failed > 0 || overflow > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                failed > 0 || overflow > 0 ? "批量导入完成（有未落位的）" : "批量导入完成",
                message);
            AppendLog(LogKind.Info, $"拖放批量导入（{_selectedTextToolChapter?.Code}）：{message}");
        }

        /// <summary>
        /// 按播放进度更新当前行的绿条（ScaleX = 已播 / 总长）。
        ///
        /// 用缩放而不是改 Width：不必等布局算出行宽，也不会每帧触发一次布局 ——
        /// 一屏几百行时，后者会把滚动搞卡。
        /// </summary>
        private void UpdateVoicePlaybackProgress()
        {
            if (_voicePlayingRowId is null ||
                !_voiceProgressBars.TryGetValue(_voicePlayingRowId, out var bar))
            {
                return;
            }

            var session = _projectVoicePreviewPlayer.PlaybackSession;
            var total = session.NaturalDuration;
            if (total <= TimeSpan.Zero || bar.Fill.RenderTransform is not ScaleTransform scale)
            {
                return;
            }

            var ratio = Math.Clamp(session.Position.TotalMilliseconds / total.TotalMilliseconds, 0d, 1d);
            scale.ScaleX = ratio;
        }

        /// <summary>把当前行的进度归零、按钮恢复成"播放"，并忘掉当前行。</summary>
        private void ResetVoicePreviewProgress()
        {
            if (_voicePlayingRowId is not null)
            {
                HideVoiceProgressBar(_voicePlayingRowId);
                SetVoicePreviewButtonGlyph(_voicePlayingRowId, isPlaying: false);
            }

            _voicePlayingRowId = null;
            _voicePreviewPaused = false;
            _voiceAwaitingPlayback = false;
            _voiceStartWatchdog?.Stop();
            StopVoiceProgressTimer();
        }

        /// <summary>
        /// 播完了：绿色填满整条、**保持可见**（等价于以前"这一行播过了"的标记），
        /// 图标切回播放，但不清 _voicePlayingRowId —— 再点一次会从头重播。
        /// </summary>
        private void CompleteVoicePreview()
        {
            if (_voicePlayingRowId is null ||
                !_voiceProgressBars.TryGetValue(_voicePlayingRowId, out var bar))
            {
                return;
            }

            if (bar.Fill.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 1;
            }

            _voiceCompletedRowId = _voicePlayingRowId;
            SetVoicePreviewButtonGlyph(_voicePlayingRowId, isPlaying: false);
            _voicePreviewPaused = false;
            _voiceAwaitingPlayback = false;
            _voiceStartWatchdog?.Stop();
            StopVoiceProgressTimer();
        }

        private void ShowVoiceProgressBar(string rowId)
        {
            if (!_voiceProgressBars.TryGetValue(rowId, out var bar))
            {
                return;
            }

            // 已经亮着就别再播一次入场：DragOver 每秒触发几十次，
            // 每次都重播的话白条永远停在"刚开始展开"的样子，看起来像卡住。
            var wasVisible = bar.Track.Visibility == Visibility.Visible;

            // 只有从"灭"变"亮"才把绿条归零。
            // 反过来（亮着还归零）会把已完成那条绿线擦掉 —— 拖拽时那正是位置参照。
            if (!wasVisible && bar.Fill.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 0;
            }
            bar.Track.Visibility = Visibility.Visible;
            bar.Fill.Visibility = Visibility.Visible;

            if (!wasVisible)
            {
                PlayTrackEntrance(bar.Track);
            }
        }

        /// <summary>
        /// 白条入场：**从中间向两边铺开 + 渐入**，220ms。
        ///
        /// 这是原来那条绿线标记的动效，换成进度条之后被我一并删掉了 ——
        /// 少了它，白条是"啪"地整条出现的，很生硬（2026-11-04 用户要求加回来）。
        /// </summary>
        private static void PlayTrackEntrance(Border track)
        {
            if (track.RenderTransform is not ScaleTransform scale)
            {
                return;
            }

            // 兜底：先把不透明度落成 1，动画没跑起来至少是可见的，不留隐形块。
            track.Opacity = 1;
            scale.ScaleX = 0;

            var duration = new Duration(TimeSpan.FromMilliseconds(220));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var grow = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, "ScaleX");

            var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(fade, track);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var storyboard = new Storyboard();
            storyboard.Children.Add(grow);
            storyboard.Children.Add(fade);

            try
            {
                storyboard.Begin();
            }
            catch (Exception)
            {
                scale.ScaleX = 1;
                track.Opacity = 1;
            }
        }

        private void HideVoiceProgressBar(string rowId)
        {
            if (!_voiceProgressBars.TryGetValue(rowId, out var bar))
            {
                return;
            }

            if (bar.Fill.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 0;
            }

            bar.Track.Visibility = Visibility.Collapsed;
            bar.Fill.Visibility = Visibility.Collapsed;
            // 复位进场状态，下次亮出来才会再播一次动画。
            bar.Track.Opacity = 0;
            if (bar.Track.RenderTransform is ScaleTransform trackScale)
            {
                trackScale.ScaleX = 0;
            }
        }

        private void SetVoicePreviewButtonGlyph(string rowId, bool isPlaying)
        {
            if (_voicePreviewButtons.TryGetValue(rowId, out var button))
            {
                button.Content = new SymbolIcon(isPlaying ? Symbol.Pause : Symbol.Play);
                ToolTipService.SetToolTip(button, isPlaying ? "暂停" : "播放语音测试");
            }
        }

        /// <summary>
        /// 「在编辑器中打开」：跳到剧情编辑器，并**定位到这一句**。
        ///
        /// 用途是"听着不对 → 立刻回去改台词"，省掉「切页 → 找章节 → 翻行」三步。
        /// 定位优先用 StableId（行内容可以改、Id 不变），找不到再提示，不做模糊匹配 ——
        /// 猜错行比不跳更糟：用户会以为改错了地方。
        /// </summary>
        private async Task OpenProjectTextRowInStoryEditorAsync(ProjectTextRow row)
        {
            if (_currentProject is null || _selectedTextToolChapter is null)
            {
                return;
            }

            var chapter = _selectedTextToolChapter;
            try
            {
                await ShowStoryEditorPageAsync(chapter);

                var index = _storyRows.FindIndex(item =>
                    string.Equals(item.StableId, row.StableRowId, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "没定位到那一句",
                        $"「{chapter.Code}」里找不到对应行（表格可能被改过）。已打开编辑器，请手动翻一下。");
                    return;
                }

                NavigateToStoryRow(index, rebuildPersistentState: true);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "打开编辑器失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    "从语音页打开剧情编辑器失败");
            }
        }

        /// <summary>
        /// 「定位文件」：在资源管理器里**选中**这条语音（不是只打开文件夹）。
        /// 找不到文件就说清楚，不弹一个空窗口让人自己找。
        /// </summary>
        private void RevealProjectVoiceFile(ProjectTextRow row)
        {
            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) ||
                string.IsNullOrWhiteSpace(voicePath) ||
                !File.Exists(voicePath))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可定位的文件", "这一行还没有有效的语音文件。");
                return;
            }

            try
            {
                // `/select,` 让资源管理器直接选中它 —— 不带这个参数只会打开文件夹，
                // 用户还得在几十个 wav 里自己翻。
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{voicePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception error)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "打开资源管理器失败", voicePath, error, "定位语音文件失败");
            }
        }

        /// <summary>
        /// 「编辑音频」：**占位**，功能还没做。
        ///
        /// 为什么先放一个空入口：菜单结构定型后很少动，先把位置占住，
        /// 以后接上真的编辑器时用户不用重新找。但点了必须给一句明确的话 ——
        /// 点了没反应的菜单项最容易被当成 bug 报上来。
        /// </summary>
        /// <summary>
        /// 外部音频编辑器。**路径写死**：换位置就改这一行（将来要成设置项也很容易）。
        /// </summary>
        private const string VoiceAudioEditorPath = @"E:\Pymss Studio\Pymss Studio.exe";

        private void EditProjectVoiceAudio(ProjectTextRow row)
        {
            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) ||
                string.IsNullOrWhiteSpace(voicePath) ||
                !File.Exists(voicePath))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "没有可编辑的语音", "这一行还没有有效的语音文件。");
                return;
            }

            if (!File.Exists(VoiceAudioEditorPath))
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "找不到音频编辑器",
                    $"路径：{VoiceAudioEditorPath}{Environment.NewLine}"
                    + "程序挪了位置的话，改 MainWindow.VoiceTool.cs 里的 VoiceAudioEditorPath 常量。");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = VoiceAudioEditorPath,
                    // UseShellExecute=true 时不能用 ArgumentList，只能自己拼引号；
                    // 路径里常有空格，这一层引号不能省。
                    Arguments = $"\"{voicePath}\"",
                    UseShellExecute = true
                });

                AppendLog(LogKind.Info, $"用外部编辑器打开语音：{Path.GetFileName(voicePath)}");
            }
            catch (Exception error)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "打开编辑器失败",
                    VoiceAudioEditorPath,
                    error,
                    $"打开外部音频编辑器失败：{Path.GetFileName(voicePath)}");
            }
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
