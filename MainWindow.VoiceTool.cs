using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
        /// 当前页面上的语音按钮，key = 行 Id。
        ///
        /// 为什么要留一份：批量规范命名会改掉文件名，得把这些按钮的文字就地换掉。
        /// 不重铺页面 —— 重铺会把滚动位置打回顶部（那是素材页反复挨骂的一条）。
        /// </summary>
        private readonly Dictionary<string, Button> _voiceButtons =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 每行的「刚才播放过」标记（行底部那条 2px 主题色线），key = 行 Id。
        ///
        /// 用户对照着听的时候，回头要能一眼看出刚才播的是哪一条 ——
        /// 只标当前这一条，播下一条时把上一条撤掉（不然满屏都是线，等于没标）。
        /// </summary>
        private readonly Dictionary<string, Border> _voicePlayMarkers =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前被标记为"正在/刚播过"的行 Id。</summary>
        private string? _voicePlayingRowId;

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
            _voiceButtons.Clear();
            _voicePlayMarkers.Clear();
            _voicePlayingRowId = null;
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
            // 「没问题」统一用淡色（跟原来两个数字一样），不再给绿色：
            // 整列绿油油反而看不出重点，有问题的才染色（2026-11-04 用户要求）。
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
                // 达标不染色（以前给的是绿色，见 SetVoiceDbText 的注释）。
                return null;
            }

            // 超出容差之后才分方向：比目标响 = 红（容易在混音里盖住别人），
            // 比目标轻 = 黄（听不见，但通常只是要抬一点）。
            return item.Measurement.IntegratedLufs > preset.TargetLufs
                ? Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush
                : Application.Current.Resources["SystemFillColorCautionBrush"] as Brush;
        }

        /// <summary>真峰值超标才红；没超标保持淡色。峰值超标最容易在混音里削波，所以用红不用黄。</summary>
        private static Brush? BrushForDbPeak(VoiceComplianceItem item) =>
            item.PeakVerdict == VoicePeakVerdict.Over
                ? Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush
                : null;

        /// <summary>
        /// 响度范围（LRA）的上限。超过就标红。
        ///
        /// 10 LU 是对话素材的常见上限：再大意味着这一句里忽大忽小，
        /// 进游戏后玩家得反复调音量。要改就改这一个常量。
        /// </summary>
        private const double MaxLoudnessRangeLu = 10d;

        private static Brush? BrushForDbRange(VoiceComplianceItem item) =>
            item.Measurement.LoudnessRangeLu > MaxLoudnessRangeLu
                ? Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush
                : null;

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

            // 「能修的」才点亮：测不出的、被峰值天花板卡住的都排除。
            // 后者再跑一万遍也到不了目标（只能重做素材），让按钮为它们常亮，
            // 结果就是每次点都重跑同一条、什么也没变（2026-11-04 实测）。
            var needsFix = _chapterVoiceLoudness.Values.Any(item =>
                item.Measurement.IsUsable && item.NeedsFix && !item.IsPeakLimited);
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
            var skippedCompliant = 0;
            var skippedUnmeasured = 0;
            var skippedPeakLimited = 0;
            foreach (var file in allFiles)
            {
                if (_chapterVoiceLoudness.TryGetValue(Path.GetFileName(file), out var item) &&
                    item.Measurement.IsUsable)
                {
                    if (item.NeedsFix && item.IsPeakLimited)
                    {
                        // 被峰值天花板卡住的，**再跑多少遍也到不了目标**：
                        // 想拉到 −12 就得整体加 2.5 dB，峰值必然冲破 −3，只能被压回来。
                        //
                        // 这种人只能重做素材（压峰均比）。把它留在目标里会造成两个后果：
                        // 按钮永远亮着、每次点都重跑同一条且毫无变化
                        // ——2026-11-04 用户实测就是这个循环。
                        skippedPeakLimited++;
                    }
                    else if (item.NeedsFix)
                    {
                        targets.Add(file);
                    }
                    else
                    {
                        skippedCompliant++;
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
                ShowFloatingTip(
                    skippedPeakLimited > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                    skippedPeakLimited > 0 ? "没有可规整的语音（剩下的被峰值卡住）" : "没有需要规整的语音",
                    $"{allFiles.Count} 条里没有可规整的超差项（达标 {skippedCompliant} 条"
                    + (skippedUnmeasured > 0 ? $"，未检查 {skippedUnmeasured} 条" : string.Empty)
                    + (skippedPeakLimited > 0
                        ? $"，{skippedPeakLimited} 条受峰值限制 —— 拉到目标会让峰值超标，只能重做素材（压峰均比）"
                        : string.Empty)
                    + "）。先点「检查DB规范」再回来。");
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

            var progress = new Progress<VoiceBatchProgress>(item =>
                UpdateGlobalProgress(
                    $"正在规整 {item.CompletedCount} / {item.TotalCount}",
                    item.OverallFraction * 100d,
                    item.FileName));

            ShowGlobalProgress("规整语音", $"正在规整 {targets.Count} 条超差语音…");
            VoiceNormalizeResult result;
            try
            {
                // 不做整章备份：回退由「原始版」制度负责（每个文件固定一份最初副本）。
                result = await _voiceBatchProcessor.NormalizeAsync(
                    ffmpegPath,
                    targets,
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

            if (skippedPeakLimited > 0)
            {
                // 必须说清楚：这些行在 DB 列里还是红的，但不是"没处理到"，
                // 而是"处理了也没用"—— 用户否则会以为工具漏了它们。
                skipParts.Add($"{skippedPeakLimited} 条受峰值限制（规整解决不了）");
            }

            var skipNote = skipParts.Count == 0
                ? string.Empty
                : $"（未处理：{string.Join("、", skipParts)}）";

            var summary = (renamedCount > 0 ? $"已规范 {renamedCount} 个文件名；" : string.Empty)
                + $"成功 {result.SuccessCount} / {result.Outcomes.Count} 条"
                + (result.FailureCount > 0 ? $"，失败 {result.FailureCount} 条" : string.Empty)
                + skipNote;
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

        /// <summary>
        /// 把「刚才播过」的标记移到这一行：撤掉上一条，点亮当前这条。
        ///
        /// 只保留一条 —— 每条播过的都留着线，满屏都是绿的，等于没标。
        /// 页面重铺时标记全部重建（见 <see cref="ResetVoiceLoudnessColumnState"/>），自然清空。
        /// </summary>
        private void MarkVoicePlayingRow(string rowId)
        {
            if (string.Equals(_voicePlayingRowId, rowId, StringComparison.Ordinal))
            {
                return;
            }

            if (_voicePlayingRowId is not null &&
                _voicePlayMarkers.TryGetValue(_voicePlayingRowId, out var previous))
            {
                previous.Visibility = Visibility.Collapsed;
            }

            _voicePlayingRowId = rowId;
            if (_voicePlayMarkers.TryGetValue(rowId, out var marker))
            {
                marker.Visibility = Visibility.Visible;
                PlayVoiceMarkerEntrance(marker);
            }
        }

        /// <summary>
        /// 标记线的入场：**从中间向两侧铺开 + 渐入**，200ms。
        ///
        /// 为什么用 200ms 而不是更长：这条线只是"刚才播过"的提示，
        /// 用户视线在音频和文本之间来回跳，动画抢戏反而碍事。
        /// </summary>
        private static void PlayVoiceMarkerEntrance(Border marker)
        {
            if (marker.RenderTransform is not ScaleTransform scale)
            {
                return;
            }

            // 兜底：先把 Opacity 落成 1、缩放归位到可见状态。
            // 万一 Storyboard 没跑起来（目标不在树里等），线至少是"看着正常"的，
            // 不会留一个看不见的标记 —— 那种问题排查起来最费劲。
            marker.Opacity = 1;
            scale.ScaleX = 0;

            var duration = new Duration(TimeSpan.FromMilliseconds(200));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var grow = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, "ScaleX");

            var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(fade, marker);
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
                // 动画失败不影响功能：缩放落回 1，线静态显示。
                scale.ScaleX = 1;
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
        private void EditProjectVoiceAudio(ProjectTextRow row)
        {
            var fileName = _projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) &&
                !string.IsNullOrWhiteSpace(voicePath)
                ? Path.GetFileName(voicePath)
                : "这条语音";

            ShowFloatingTip(
                InfoBarSeverity.Informational,
                "编辑音频（还没做）",
                $"「{fileName}」的音频编辑入口先放在这里，功能以后再补。");
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
