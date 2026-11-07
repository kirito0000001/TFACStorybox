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
using GalExcleTools.Controls;
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
        private void OpenProjectTextTool(ProjectTextToolMode mode, ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                return;
            }

            _workbenchReturnPage = WorkbenchReturnPage.ProjectTextTool;
            _projectTextToolMode = mode;
            _selectedTextToolChapter = chapter;
            _selectedVoiceChapter = chapter;
            ProjectTextToolPage.Visibility = Visibility.Visible;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            RefreshProjectTextToolPage();
            PlayPageEntrance(ProjectTextToolPage);
        }

        private void BackToProjectDetailFromTextToolButton_Click(object sender, RoutedEventArgs e)
        {
            StopProjectVoicePreview();
            if (_currentProject is null)
            {
                ShowWorkbenchPage();
                return;
            }

            PlayNegativeSound();
            ShowProjectDetailPage(_projectWorkspaceService.ReadProjectInfo(_currentProject.Path));
        }

        private async void SwitchProjectTextToolChapterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            var selected = await SelectProjectChapterAsync("切换章节", _selectedTextToolChapter);
            if (selected is null)
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();
            _selectedTextToolChapter = selected;
            _selectedVoiceChapter = selected;

            // 筛选是**本章**的视角，不是全局偏好：换章还留着上一章的"夏疏影语音"，
            // 在新章里筛出来是空的，看着像数据没了（2026-11-04 用户反馈）。
            _voiceRowFilter = VoiceRowFilter.All;
            _voiceRowFilterSpeaker = null;

            RefreshProjectTextToolPage();
        }

        private void ReloadProjectTextToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();
            var chapters = _projectWorkspaceService.GetChapters(_currentProject)
                .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _projectTextRows.Clear();
            _projectTextRows.AddRange(_projectTextDataService.LoadTextRows(_currentProject, chapters));
            _projectVoiceMapState = _projectTextDataService.ReadVoiceMap(_currentProject);
            _projectLocalizationState = _projectTextDataService.ReadLocalization(_currentProject);
            RefreshProjectTextToolPage();
        }

        /// <summary>语音页的筛选范围。菜单文案不带"只看"——筛选本来就是"只看"。</summary>
        private enum VoiceRowFilter
        {
            All,
            /// <summary>只显示"说话人勾了有语音"的台词（含还没录音的，就看人）。</summary>
            AnyVoicedSpeaker,
            Configured,
            Missing,
            Unchecked,
            /// <summary>量过且超标（响度出容差 或 峰值超）—— 包含被峰值卡住的那种。</summary>
            OffSpec,
            /// <summary>说话人那边勾了"应该有语音"，但这行还没配文件（漏配）。</summary>
            MissingVoiced,
            /// <summary>按某个说话人过滤，具体是谁存在 <see cref="_voiceRowFilterSpeaker"/>。</summary>
            Speaker
        }

        private VoiceRowFilter _voiceRowFilter = VoiceRowFilter.All;

        private string? _voiceRowFilterSpeaker;

        private readonly VoiceTrackSettingsStore _voiceTrackSettingsStore = new();

        private VoiceTrackSettings _voiceTrackSettings = new();

        private void ProjectTextToolVoiceFilterItem_Click(object sender, RoutedEventArgs e)
        {
            if (_projectTextToolMode != ProjectTextToolMode.Voice ||
                sender is not MenuFlyoutItem { Tag: string tag })
            {
                return;
            }

            VoiceRowFilter filter;
            string? speakerKey = null;

            if (tag.StartsWith("Speaker:", StringComparison.Ordinal))
            {
                filter = VoiceRowFilter.Speaker;
                speakerKey = tag["Speaker:".Length..];
            }
            else if (!Enum.TryParse(tag, out filter))
            {
                return;
            }

            if (filter == _voiceRowFilter &&
                string.Equals(speakerKey, _voiceRowFilterSpeaker, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();
            _voiceRowFilter = filter;
            _voiceRowFilterSpeaker = speakerKey;
            RefreshProjectTextToolPage();
        }

        /// <summary>
        /// 重建筛选菜单。
        ///
        /// 为什么放在代码里生成：最后一组「xxx语音」是按**当前章节的说话人**动态拼的，
        /// XAML 里写不出来；章节一切换，菜单就得跟着换。
        /// </summary>
        private void RebuildVoiceFilterFlyout()
        {
            if (ProjectTextToolVoiceFilterButton is null)
            {
                return;
            }

            var flyout = new MenuFlyout();

            void AddItem(string text, string tag)
            {
                var item = new MenuFlyoutItem { Text = text, Tag = tag };
                item.Click += ProjectTextToolVoiceFilterItem_Click;
                flyout.Items.Add(item);
            }

            AddItem("全部行", "All");
            AddItem("说话人语音", "AnyVoicedSpeaker");
            AddItem("已设置语音", "Configured");
            AddItem("未设置语音", "Missing");

            flyout.Items.Add(new MenuFlyoutSeparator());
            AddItem("未检查", "Unchecked");
            AddItem("不合规语音", "OffSpec");
            AddItem("缺少设置", "MissingVoiced");

            // 只给"有语音"的角色生成 —— 依据就是说话人标识里的勾选。
            // 没勾的角色不出现：这个菜单是拿来找配音的，不是角色字典。
            var speakers = GetChapterSpeakerKeys()
                .Where(key => IsSpeakerVoiced(key))
                .ToList();
            if (speakers.Count > 0)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                foreach (var key in speakers)
                {
                    AddItem($"{DisplaySpeaker(key)}语音", $"Speaker:{key}");
                }
            }

            ProjectTextToolVoiceFilterButton.Flyout = flyout;
        }

        /// <summary>本章出现过的说话人（已归一化、去重、按名字排序）。</summary>
        private List<string> GetChapterSpeakerKeys()
        {
            return _projectTextRows
                .Where(row => _selectedTextToolChapter is not null &&
                    string.Equals(row.ChapterCode, _selectedTextToolChapter.Code, StringComparison.OrdinalIgnoreCase))
                .Select(row => ToSpeakerKey(row.Speaker))
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>「语音设置」：音轨数 + 同步播放。走统一弹窗组件（见 Controls/ToolboxDialog.cs）。</summary>
        private async void ProjectTextToolVoiceSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null || _projectTextToolMode != ProjectTextToolMode.Voice)
            {
                return;
            }

            var trackCountBox = new NumberBox
            {
                Minimum = VoiceTrackSettings.MinTrackCount,
                Maximum = VoiceTrackSettings.MaxTrackCount,
                Value = _voiceTrackSettings.TrackCount,
                SmallChange = 1,
                LargeChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };

            var syncPlaybackSwitch = new ToggleSwitch
            {
                IsOn = _voiceTrackSettings.SyncPlayback,
                OnContent = "所有音轨同时播放",
                OffContent = "只播当前音轨"
            };

            var denoiseSwitch = new ToggleSwitch
            {
                IsOn = _voiceTrackSettings.UseUvrDenoise,
                OnContent = "规整时先去噪",
                OffContent = "跳过去噪"
            };

            var dropPreviewSwitch = new ToggleSwitch
            {
                IsOn = _voiceTrackSettings.PlayAfterDropImport,
                OnContent = "拖入后自动试听一遍",
                OffContent = "拖入后不播，直接标成已设置"
            };

            var form = ToolboxDialog.BuildForm(
                ToolboxDialog.BuildField(
                    "音轨数",
                    trackCountBox,
                    $"多国语音用：每条音轨放一套配音，{VoiceTrackSettings.MinTrackCount}–{VoiceTrackSettings.MaxTrackCount} 条。"),
                ToolboxDialog.BuildField(
                    "同步播放",
                    syncPlaybackSwitch,
                    "打开后点试听会把各音轨同时播出来（用于需要叠音的台词）。"),
                ToolboxDialog.BuildField(
                    "UVR 降噪",
                    denoiseSwitch,
                    "用 PyMSS 的 UVR-DeNoise 去掉底噪。一条要 1~2 分钟，赶时间可以关。"),
                ToolboxDialog.BuildField(
                    "拖入试听",
                    dropPreviewSwitch,
                    "拖文件到某一行后自动播一遍。关掉的话，那条线会直接变绿，不发声。"),
                BuildSpeakerIndicatorField(out var speakerChecks),
                BuildVoiceResetField());

            var confirmed = await ToolboxDialog.ShowAsync(
                RootGrid.XamlRoot,
                "语音设置",
                form,
                "保存",
                "取消");
            if (!confirmed)
            {
                return;
            }

            // 「清空所有音频处理」是在弹窗里点的，真正执行放到弹窗关掉之后 ——
            // 弹窗是模态的，在里面跑几十秒的还原用户啥也看不到。
            if (_voiceResetRequested)
            {
                await ResetAllVoiceProcessingAsync();
                return;
            }

            var count = double.IsNaN(trackCountBox.Value)
                ? _voiceTrackSettings.TrackCount
                : (int)Math.Round(trackCountBox.Value);

            _voiceTrackSettings.TrackCount = count;
            _voiceTrackSettings.SyncPlayback = syncPlaybackSwitch.IsOn;
            _voiceTrackSettings.PlayAfterDropImport = dropPreviewSwitch.IsOn;
            _voiceTrackSettings.UseUvrDenoise = denoiseSwitch.IsOn;
            // 只写**当前章**：别的章的勾选原样留着。
            _voiceTrackSettings.SetSpeakers(
                _selectedTextToolChapter?.Code ?? string.Empty,
                speakerChecks
                    .Where(pair => pair.Box.IsChecked == true)
                    .Select(pair => pair.Speaker));
            _voiceTrackSettings.Normalize();
            _voiceTrackSettingsStore.Write(_currentProject, _voiceTrackSettings);
            RefreshProjectTextToolPage();

            ShowFloatingTip(
                InfoBarSeverity.Success,
                "语音设置已保存",
                $"音轨数 {_voiceTrackSettings.TrackCount}，同步播放{(_voiceTrackSettings.SyncPlayback ? "开启" : "关闭")}。");
        }

        /// <summary>
        /// 「音轨 N」：点开选第几条。
        ///
        /// ⚠️ 现在只切换**当前编辑的音轨号**并记住选择：语音映射表还是"一行一个文件"，
        /// 多音轨的文件落位（比如 `Vo-012-〈音轨2〉.wav`）要改 story.voice-map.json 的结构，
        /// 那是另一步。这里不做半截数据模型，免得映射被写坏。
        /// </summary>
        private void ProjectTextToolVoiceTrackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null || _voiceTrackSettings.TrackCount <= 1)
            {
                return;
            }

            var flyout = new MenuFlyout();
            for (var track = 1; track <= _voiceTrackSettings.TrackCount; track++)
            {
                var index = track;
                var item = new MenuFlyoutItem
                {
                    Text = $"音轨 {index}",
                    IsEnabled = index != _voiceTrackSettings.ActiveTrack
                };
                item.Click += (_, _) =>
                {
                    _voiceTrackSettings.ActiveTrack = index;
                    _voiceTrackSettingsStore.Write(_currentProject, _voiceTrackSettings);
                    UpdateVoiceTrackButtonState();
                    ShowFloatingTip(
                        InfoBarSeverity.Informational,
                        $"已切到音轨 {index}",
                        "多音轨的文件映射还没接；这一步先把选择记住。");
                };
                flyout.Items.Add(item);
            }

            flyout.ShowAt(ProjectTextToolVoiceTrackButton);
        }

        private Dictionary<string, CharacterInfo> _speakerDirectory = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 说话人的**归一化键**：NFKC 归一（全角 → 半角）+ 去首尾空白 + 折叠内部空白。
        ///
        /// 归一的目的只有一个：**同一种写法的不同形式要合并**。
        /// 同一章里同时存在 `？`(U+FF1F) 和 `?`(U+003F)，不归一列表里会出现两个
        /// 长得一模一样的「？」，谁也分不清（2026-11-04 用户截图指出）。
        ///
        /// ⚠️ 但 `?` 和 `???` 是**两个不同的角色**（用户明确说过），
        /// 所以只归一化，**不做"纯标点合并"** —— 那会把人家两个角色并成一个。
        /// </summary>
        private static string ToSpeakerKey(string? raw)
        {
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return string.Empty;
            }

            text = text.Normalize(NormalizationForm.FormKC);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        /// <summary>
        /// 这一行的说话人是否在**本章**被勾成"有语音"。
        ///
        /// 必须带章节：同名角色在不同章可能不是同一个人，
        /// 全局共用一份勾选会把第二章的"？"当成第一章的"？"（2026-11-04 用户实测）。
        /// </summary>
        private bool IsSpeakerVoiced(string speakerKey)
        {
            if (_selectedTextToolChapter is not { } chapter || string.IsNullOrEmpty(speakerKey))
            {
                return false;
            }

            return _voiceTrackSettings
                .GetSpeakers(chapter.Code)
                .Contains(speakerKey, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>列表里显示的名字：能对上角色就显示中文名，其余原样（`?`、`???`、`梦凪`…）。</summary>
        private string DisplaySpeaker(string key)
        {
            return _speakerDirectory.TryGetValue(key, out var character) ? character.Name : key;
        }

        /// <summary>
        /// 说话人标识：把本章出现过、且被勾了「有语音」的角色，在台词行左侧画一条角色色竖线。
        /// 列表放在弹窗内滚动 —— 一章几十个角色，平铺会把弹窗撑成一条长龙。
        /// </summary>
        private UIElement BuildSpeakerIndicatorField(out List<(string Speaker, CheckBox Box)> checks)
        {
            checks = [];

            var speakers = GetChapterSpeakerKeys();

            if (speakers.Count == 0)
            {
                return ToolboxDialog.BuildField(
                    "说话人标识",
                    new TextBlock
                    {
                        Text = "本章的剧情表里还没有填说话人（TalkChar）。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                    });
            }

            var panel = new StackPanel { Spacing = 2 };
            foreach (var key in speakers)
            {
                var box = new CheckBox
                {
                    // 显示中文名（角色代号如 Xia 在立绘设置里有 Name），存的仍是归一化键。
                    Content = DisplaySpeaker(key),
                    Tag = key,
                    IsChecked = IsSpeakerVoiced(key)
                };
                checks.Add((key, box));
                panel.Children.Add(box);
            }

            return ToolboxDialog.BuildField(
                "说话人标识（勾选 = 该角色有语音）",
                new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = 220,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                "勾上的角色，其台词行左侧会出现一条角色专属颜色的竖线。");
        }

        /// <summary>
        /// 角色目录：把素材库里的角色按**代号**和**中文名**各登记一次（同一角色指向同一份数据）。
        ///
        /// 剧情表里 TalkChar 有的是代号（`Xia`）、有的是中文名（`梦凪`），
        /// 两边都要能查到，否则代号那批会显示成英文、颜色也取不到。
        /// 每次重铺读一次，不逐行读文件。
        /// </summary>
        private Dictionary<string, CharacterInfo> BuildSpeakerDirectory()
        {
            var directory = new Dictionary<string, CharacterInfo>(StringComparer.OrdinalIgnoreCase);
            if (_currentProject is null)
            {
                return directory;
            }

            try
            {
                var library = _projectWorkspaceService.ResolveProjectAssetLibrary(Settings.ProjectRootPath, _currentProject);
                if (library is null)
                {
                    return directory;
                }

                foreach (var character in _characterWorkspaceService.GetCharactersByName(library))
                {
                    var codeKey = ToSpeakerKey(character.Code);
                    if (codeKey.Length > 0)
                    {
                        directory[codeKey] = character;
                    }

                    var nameKey = ToSpeakerKey(character.Name);
                    if (nameKey.Length > 0)
                    {
                        directory[nameKey] = character;
                    }
                }
            }
            catch (Exception error)
            {
                // 读不到不该让页面开不了 —— 退回按名字算的稳定色、原样显示。
                AppendLog(LogKind.Warning, "读取角色目录失败，说话人将显示原始代号并使用生成色。", error);
            }

            return directory;
        }

        private Brush ResolveSpeakerBrush(string speakerKey)
        {
            if (_speakerDirectory.TryGetValue(speakerKey, out var character) &&
                TryParseHexColor(character.ColorHex, out var configured))
            {
                return new SolidColorBrush(configured);
            }

            // 没设颜色的角色：用名字算一个**稳定**的颜色。
            // 不能用 string.GetHashCode()——.NET 里它每个进程都不一样，重启就换色。
            var hash = 0;
            foreach (var ch in speakerKey)
            {
                hash = unchecked(hash * 31 + ch) & 0x7FFFFFFF;
            }

            return new SolidColorBrush(FromHsl(hash % 360, 0.55, 0.58));
        }

        private static bool TryParseHexColor(string hex, out Windows.UI.Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            var text = hex.Trim().TrimStart('#');
            if (text.Length == 8)
            {
                text = text[2..]; // 带 alpha 的写法只取 RGB
            }

            if (text.Length != 6 ||
                !byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
                !byte.TryParse(text[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
                !byte.TryParse(text[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                return false;
            }

            color = Windows.UI.Color.FromArgb(0xFF, r, g, b);
            return true;
        }

        private static Windows.UI.Color FromHsl(double hueDegrees, double saturation, double lightness)
        {
            var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            var hue = hueDegrees / 60d;
            var x = chroma * (1 - Math.Abs(hue % 2 - 1));
            var (r1, g1, b1) = hue switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x, chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };
            var m = lightness - chroma / 2;
            return Windows.UI.Color.FromArgb(
                0xFF,
                (byte)Math.Round((r1 + m) * 255),
                (byte)Math.Round((g1 + m) * 255),
                (byte)Math.Round((b1 + m) * 255));
        }

        private void UpdateVoiceTrackButtonState()
        {
            if (ProjectTextToolVoiceTrackButton is null)
            {
                return;
            }

            var count = _voiceTrackSettings.TrackCount;
            ProjectTextToolVoiceTrackButton.Content = $"音轨 {_voiceTrackSettings.ActiveTrack}";
            ProjectTextToolVoiceTrackButton.IsEnabled = count > 1;
            ToolTipService.SetToolTip(
                ProjectTextToolVoiceTrackButton,
                count > 1
                    ? $"当前音轨 {_voiceTrackSettings.ActiveTrack} / 共 {count} 条，点击切换"
                    : "只有一条音轨；到「语音设置」里调整音轨数");
        }

        /// <summary>
        /// 按当前筛选范围过滤语音行。
        ///
        /// 「超差 / 未检查」依赖响度数据，只对**已设置语音**的行有意义 ——
        /// 没语音的行放进来只会是一片横线，徒增噪音。
        /// </summary>
        private List<ProjectTextRow> ApplyVoiceRowFilter(List<ProjectTextRow> rows)
        {
            if (_voiceRowFilter == VoiceRowFilter.All)
            {
                return rows;
            }

            return rows.Where(row =>
            {
                var hasVoice = _projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) &&
                    !string.IsNullOrWhiteSpace(voicePath) &&
                    File.Exists(voicePath);
                var speakerKey = ToSpeakerKey(row.Speaker);

                switch (_voiceRowFilter)
                {
                    // 「说话人语音」：这个角色的所有台词，不管录没录 ——
                    // 用来按角色过一遍"该配的都在不在"，是最常用的看法。
                    case VoiceRowFilter.AnyVoicedSpeaker:
                        return IsSpeakerVoiced(speakerKey);

                    case VoiceRowFilter.Configured:
                        return hasVoice;

                    case VoiceRowFilter.Missing:
                        return !hasVoice;

                    // 「缺少设置」：说话人那边勾了"该角色有语音"，但这行还没配文件。
                    // 这是唯一能"按预期找出漏配"的筛选 —— 光看"未设置"会把旁白也带上。
                    case VoiceRowFilter.MissingVoiced:
                        return !hasVoice && speakerKey.Length > 0 &&
                            IsSpeakerVoiced(speakerKey);

                    case VoiceRowFilter.Speaker:
                        return speakerKey.Length > 0 &&
                            string.Equals(speakerKey, _voiceRowFilterSpeaker, StringComparison.OrdinalIgnoreCase);
                }

                // 后面两项都要看响度数据，没语音的行不参与。
                if (!hasVoice)
                {
                    return false;
                }

                // voicePath 在上面已经判过非空（hasVoice），这里给个 ! 让编译器也认可。
                var name = Path.GetFileName(voicePath!);
                var measured = _chapterVoiceLoudness.TryGetValue(name, out var item) && item.Measurement.IsUsable;

                if (_voiceRowFilter == VoiceRowFilter.OffSpec)
                {
                    // 包含"被峰值卡住"的：它们确实没合规，只是规整修不了 ——
                    // 筛选要把它们露出来，别让人以为只剩达标的。
                    return measured && item!.NeedsFix;
                }

                // 剩下只有「未检查」：有语音、但这次没量过（DB 列是横线）。
                return !measured;
            }).ToList();
        }

        private string BuildVoiceRowsSubtitle(int shownCount, int totalCount)
        {
            var filterText = _voiceRowFilter switch
            {
                VoiceRowFilter.AnyVoicedSpeaker => "说话人语音",
                VoiceRowFilter.Configured => "已设置语音",
                VoiceRowFilter.Missing => "未设置语音",
                VoiceRowFilter.Unchecked => "未检查",
                VoiceRowFilter.OffSpec => "不合规语音",
                VoiceRowFilter.MissingVoiced => "缺少设置",
                VoiceRowFilter.Speaker => $"{DisplaySpeaker(_voiceRowFilterSpeaker ?? string.Empty)}的台词",
                _ => "全部行"
            };

            var baseText = $"当前章节：{_selectedTextToolChapter?.Code}，{filterText}，显示 {shownCount} / 共 {totalCount} 行。";
            return _voiceRowFilter == VoiceRowFilter.All
                ? baseText + "右侧点击选择语音文件；导入时统一转成 wav / 48 kHz / 16 bit。"
                : baseText;
        }

        private async void SwitchProjectTextToolLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            var language = await ShowNameInputDialogAsync("切换语言", "目标语言", _selectedLocalizationLanguage);
            if (string.IsNullOrWhiteSpace(language))
            {
                return;
            }

            PlaySelectionSound();
            _selectedLocalizationLanguage = language.Trim();
            RefreshProjectTextToolPage();
        }

        private async Task<ChapterInfo?> SelectProjectChapterAsync(string title, ChapterInfo? selectedChapter)
        {
            if (_currentProject is null)
            {
                return null;
            }

            var chapters = _projectWorkspaceService.GetChapters(_currentProject)
                .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (chapters.Count == 0)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "没有可切换章节", "当前项目还没有章节。");
                return null;
            }

            var selected = await _dialogService.SelectAsync(new SelectionDialogRequest<ChapterInfo>(
                title,
                "选择要显示文本表格的章节。",
                chapters
                    .Select(chapter => new SelectionDialogItem<ChapterInfo>($"{chapter.Name} / {chapter.Code}", chapter))
                    .ToList(),
                "确定",
                "取消",
                460,
                420,
                chapter => selectedChapter is not null && PathsEqual(chapter.Path, selectedChapter.Path)));
            return selected;
        }

        private void RefreshProjectTextToolPage()
        {
            ProjectTextToolTablePanel.Children.Clear();
            // 重铺会把行控件全部新造一遍，旧的 DB 单元格引用必须一起作废，
            // 否则后台测量回来会往已经不在界面上的 TextBlock 里写字（改了也看不见）。
            ResetVoiceLoudnessColumnState();
            // 缓存**在铺行之前**就读进来：这样第一屏画出来就带着数字，
            // 不会先闪一片「…」再被后台填上（那看起来像在卡）。
            PrefillVoiceLoudnessFromCache();
            ProjectTextToolSwitchLanguageButton.Visibility = _projectTextToolMode == ProjectTextToolMode.Localization
                ? Visibility.Visible
                : Visibility.Collapsed;
            // 语音模式的三个按钮一起藏：过滤、检查DB规范、规整语音。
            // 本地化页没有音频可查/可规整，露出来只会让人点出一句「没有可检查的语音」。
            var voiceToolVisibility = _projectTextToolMode == ProjectTextToolMode.Voice
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectTextToolVoiceFilterButton.Visibility = voiceToolVisibility;
            ProjectTextToolVoiceSettingsButton.Visibility = voiceToolVisibility;
            ProjectTextToolVoiceTrackButton.Visibility = voiceToolVisibility;
            VoiceLoudnessCheckButton.Visibility = voiceToolVisibility;
            VoiceLoudnessNormalizeButton.Visibility = voiceToolVisibility;

            // 音轨设置跟着项目走（{项目}/Tools/voice.settings.json）。每次重铺读一遍：
            // 文件很小，换来的是"改了设置不用重启、也不会和磁盘对不上"。
            if (_currentProject is not null)
            {
                _voiceTrackSettings = _voiceTrackSettingsStore.Read(_currentProject);

                // 旧值可能存的是原始写法（`？`、`Xia`）——读进来先按**本章**归一化一遍，
                // 否则勾选框会因为按键比对不上而全部变成未勾选。
                if (_selectedTextToolChapter is { } chapter)
                {
                    var normalized = _voiceTrackSettings
                        .GetSpeakers(chapter.Code)
                        .Select(ToSpeakerKey)
                        .Where(key => key.Length > 0)
                        .ToList();
                    _voiceTrackSettings.SetSpeakers(chapter.Code, normalized);
                }

                // 角色目录一次读齐，逐行判断时只查字典（否则每行都会去读角色文件）。
                _speakerDirectory = _projectTextToolMode == ProjectTextToolMode.Voice
                    ? BuildSpeakerDirectory()
                    : new Dictionary<string, CharacterInfo>(StringComparer.OrdinalIgnoreCase);
            }

            UpdateVoiceTrackButtonState();
            // 菜单最后那组「xxx语音」依赖本章说话人，章节一换就得重建。
            RebuildVoiceFilterFlyout();
            if (_selectedTextToolChapter is null)
            {
                ProjectTextToolTitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice ? "文本语音" : "文本本地化";
                ProjectTextToolSubtitleText.Text = "当前项目还没有章节。";
                ProjectTextToolTablePanel.Children.Add(CreateProjectTextEmptyRow("暂无可显示文本。"));
                return;
            }

            var chapterRows = _projectTextRows
                .Where(row => string.Equals(row.ChapterCode, _selectedTextToolChapter.Code, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var rows = _projectTextToolMode == ProjectTextToolMode.Voice
                ? ApplyVoiceRowFilter(chapterRows)
                : chapterRows;

            // 记下"当前屏幕上这一屏到底是哪些行、什么顺序"：
            // 批量拖放要按这个顺序落位（用户看到什么就填什么），
            // 而不是去猜文件名里的行号。
            _visibleVoiceRows = rows;
            ProjectTextToolTitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice
                ? $"文本语音 {_selectedTextToolChapter.Name}"
                : $"文本本地化 {_selectedTextToolChapter.Name}";
            ProjectTextToolSubtitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice
                ? BuildVoiceRowsSubtitle(rows.Count, chapterRows.Count)
                : $"当前语言：{_selectedLocalizationLanguage}，章节：{_selectedTextToolChapter.Code}，文本 {rows.Count} 行。";
            ProjectTextToolTablePanel.Children.Add(CreateProjectTextTableHeader(
                _projectTextToolMode == ProjectTextToolMode.Voice ? "语音文件" : _selectedLocalizationLanguage,
                _projectTextToolMode == ProjectTextToolMode.Voice,
                _projectTextToolMode == ProjectTextToolMode.Voice));
            if (rows.Count == 0)
            {
                ProjectTextToolTablePanel.Children.Add(CreateProjectTextEmptyRow("这个章节还没有可编辑的文本。"));
                return;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (_projectTextToolMode == ProjectTextToolMode.Voice)
                {
                    var value = _projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) ? voicePath : string.Empty;
                    ProjectTextToolTablePanel.Children.Add(CreateProjectTextVoiceTableRow(row, value, chapterRows.Count));
                }
                else
                {
                    var languageMap = GetCurrentLocalizationLanguageMap();
                    var value = languageMap.TryGetValue(row.Id, out var localizedText) ? localizedText : string.Empty;
                    ProjectTextToolTablePanel.Children.Add(CreateProjectTextLocalizationTableRow(row, value, languageMap));
                }
            }

            // 「规整语音」的可用性只取决于 DB 列里有没有超差项。行铺完之后才判一次：
            // 缓存里带回来的旧结果也算数，所以「什么都没点、但上次查出有问题」时它是能点的。
            UpdateVoiceNormalizeButtonsState();
        }

        private Dictionary<string, string> GetCurrentLocalizationLanguageMap()
        {
            if (!_projectLocalizationState.Languages.TryGetValue(_selectedLocalizationLanguage, out var languageMap))
            {
                languageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _projectLocalizationState.Languages[_selectedLocalizationLanguage] = languageMap;
            }

            return languageMap;
        }

        private async Task SetProjectVoiceRemarkAsync(
            ProjectTextRow row,
            int rowCount,
            Button? button = null,
            Button? previewButton = null)
        {
            if (_currentProject is null)
            {
                return;
            }

            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) || !File.Exists(voicePath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "没有可修改的语音", "请先为这一行选择 wav 语音文件。");
                return;
            }

            var remarkInput = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                "更改语音备注",
                "备注",
                ProjectVoiceAssetService.ParseRemark(voicePath),
                "例如：不要S3"));
            if (remarkInput is null)
            {
                return;
            }

            var updatedPath = _projectVoiceAssetService.UpdateRemark(
                _currentProject,
                row,
                rowCount,
                remarkInput,
                _projectVoiceMapState);

            // 改名换掉了文件名，而响度缓存是**按文件名索引**的 —— 不把它一起搬，
            // 这一行的「时长 / DB」立刻变成横线（用户看到的就是"数据丢了"）。
            CarryVoiceLoudnessToRenamedFile(voicePath, updatedPath);

            _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
            TouchProjectLastEditedAt(_currentProject);
            if (button is not null)
            {
                UpdateProjectVoiceButton(button, updatedPath);
                if (previewButton is not null)
                {
                    UpdateProjectVoicePreviewButton(previewButton, updatedPath);
                }

                // 改名后单元格的查询键也要跟着换，否则这一行立刻变横线。
                RebindVoiceDbCell(row.Id, updatedPath);
            }
            else
            {
                RefreshProjectTextToolPage();
            }

            ShowFloatingTip(
                InfoBarSeverity.Success,
                "语音备注已保存",
                Path.GetFileName(updatedPath),
                logMessage: $"已更改语音备注：{Path.GetFileName(updatedPath)}");
        }

        /// <param name="skipConfirm">
        /// 批量操作时用：确认框由调用方统一弹一次，这里不再逐条问，
        /// 也不再逐条重铺页面（533 行重铺一次约 1 秒，批量会等到崩溃）。
        /// </param>
        private async Task RemoveProjectVoiceAsync(
            ProjectTextRow row,
            Button? button = null,
            Button? previewButton = null,
            bool skipConfirm = false)
        {
            if (_currentProject is null)
            {
                return;
            }

            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "没有可移除的语音", "这一行还没有设置语音文件。");
                return;
            }

            if (!skipConfirm)
            {
                var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                    "移除语音映射",
                    $"确定移除 {Path.GetFileName(voicePath)} 吗？项目 Voice 文件夹中的对应文件也会删除。",
                    "移除",
                    "取消",
                    PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
                if (!confirmed)
                {
                    return;
                }
            }

            var removedPath = _projectVoiceAssetService.RemoveVoice(_currentProject, row, _projectVoiceMapState);
            StopProjectVoicePreview();
            _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
            TouchProjectLastEditedAt(_currentProject);
            if (button is not null)
            {
                UpdateProjectVoiceButton(button, string.Empty);
                // 移除后清掉查询键，别让那一行继续挂着上一条的数字。
                RebindVoiceDbCell(row.Id, string.Empty);
                if (previewButton is not null)
                {
                    UpdateProjectVoicePreviewButton(previewButton, string.Empty);
                }
            }
            else if (!skipConfirm)
            {
                RefreshProjectTextToolPage();
            }

            if (skipConfirm)
            {
                // 批量：逐条弹提示会刷屏，调用方最后统一给一条。
                AppendLog(LogKind.Info, $"已移除语音映射：{Path.GetFileName(removedPath ?? voicePath)}");
                return;
            }

            ShowFloatingTip(
                InfoBarSeverity.Success,
                "语音文件已移除",
                Path.GetFileName(removedPath ?? voicePath),
                logMessage: $"已移除语音映射：{Path.GetFileName(removedPath ?? voicePath)}");
        }

        private static UIElement CreateProjectTextTableHeader(
            string valueHeader,
            bool includeVoicePreview,
            bool includeLoudness)
        {
            var grid = CreateProjectTextGrid(includeVoicePreview, includeLoudness);
            grid.Background = Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"] as Brush;
            grid.Children.Add(CreateProjectTextCell("文本", 0, isHeader: true, hasRightBorder: true));
            grid.Children.Add(CreateProjectTextCell(valueHeader, 1, isHeader: true, hasRightBorder: true));
            if (includeLoudness)
            {
                grid.Children.Add(CreateVoiceDurationHeaderCell());
                grid.Children.Add(CreateVoiceDbHeaderCell());
            }

            if (includeVoicePreview)
            {
                grid.Children.Add(CreateProjectTextCell(
                    "试听",
                    includeLoudness ? 4 : 2,
                    isHeader: true,
                    centerContent: true));
            }

            return grid;
        }

        private static UIElement CreateProjectTextEmptyRow(string text)
        {
            var border = new Border
            {
                Padding = new Thickness(12),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            return border;
        }

        private Grid CreateProjectTextVoiceTableRow(ProjectTextRow row, string value, int rowCount)
        {
            var grid = CreateProjectTextGrid(includeVoicePreview: true, includeLoudness: true);
            grid.Children.Add(CreateProjectTextSourceCell(row));
            var hasVoice = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
            var button = new Button
            {
                // 520 → 240：语音列砍半之后，520 会把这一列顶回去，DB 列就没地方站了。
                MinWidth = 240,
                Margin = new Thickness(8),
                Tag = row.Id
            };
            UpdateProjectVoiceButton(button, hasVoice ? value : string.Empty);
            // 批量规范命名时要就地改按钮文字，这里登记一份（见 NormalizeVoiceFileNames）。
            _voiceButtons[row.Id] = button;
            var previewButton = CreateProjectVoicePreviewButton(row, hasVoice ? value : string.Empty);
            button.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("在编辑器中打开", async (_, _) => await OpenProjectTextRowInStoryEditorAsync(row)),
                GridViewItemFactory.CreateMenuItem("更改备注", async (_, _) => await SetProjectVoiceRemarkAsync(row, rowCount, button, previewButton)),
                GridViewItemFactory.CreateMenuItem("定位文件", (_, _) => RevealProjectVoiceFile(row)),
                GridViewItemFactory.CreateMenuItem("编辑音频", (_, _) => EditProjectVoiceAudio(row)),
                GridViewItemFactory.CreateMenuItem("后期处理", async (_, _) => await ShowVoicePostProcessDialogAsync(row)),
                GridViewItemFactory.CreateMenuItem("还原最初版", async (_, _) => await RestoreProjectVoiceOriginalAsync(row)),
                GridViewItemFactory.CreateMenuItem("移除", async (_, _) => await RemoveProjectVoiceAsync(row, button, previewButton)));
            button.Click += async (_, _) =>
            {
                // 批量模式下，点这个按钮是"选中/取消"，不是换文件。
                if (_voiceBatchSelectMode)
                {
                    ToggleVoiceBatchSelection(row.Id);
                    return;
                }

                var selectedPath = await PickReplacementFileAsync(
                    ProjectVoiceAssetService.Extensions, PickerLocationId.MusicLibrary);
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                var isReplacing = _projectVoiceMapState.Voices.TryGetValue(row.Id, out var existingPath) &&
                    !string.IsNullOrWhiteSpace(existingPath);

                if (await ImportVoiceIntoRowAsync(row, rowCount, selectedPath))
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Success,
                        isReplacing ? "语音文件已更换" : "语音文件已设置",
                        Path.GetFileName(selectedPath));
                }
            };

            // 整行都能接文件：拖放目标是这一行的 Grid，不是那个小按钮。
            // 按钮只有 240px 宽，而拖文件时视线落在整行上。
            //
            // 必须给透明背景：Grid 默认没有背景就不参与命中测试，
            // 空白处根本收不到 DragOver，只有字和按钮那一小块能接。
            grid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            grid.AllowDrop = true;
            grid.Tag = row.Id;
            grid.DragOver += (_, args) =>
            {
                if (!args.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    args.AcceptedOperation = DataPackageOperation.None;
                    return;
                }

                args.AcceptedOperation = DataPackageOperation.Copy;
                args.DragUIOverride.Caption = "设为这一行的语音";
                args.DragUIOverride.IsCaptionVisible = true;

                // 悬停即预览：亮出这一行的进度条（白条那 220ms 入场动画），
                // 同时收掉上一条悬停白条 —— 靠"当前悬停是谁"来收，
                // 不指望 DragLeave（它在行与行之间经常不触发，会留一串白条）。
                SetVoiceDropHover(row.Id);
            };
            grid.DragLeave += (_, args) =>
            {
                // 指针只是移到这一行的子元素（文字、按钮）上时也会收到 DragLeave，
                // 那种情况不该收线 —— 用位置判断有没有真的离开这一行的范围。
                var position = args.GetPosition(grid);
                if (position.X < 0 || position.Y < 0 ||
                    position.X > grid.ActualWidth || position.Y > grid.ActualHeight)
                {
                    ClearVoiceDropHover(row.Id);
                }
            };
            grid.Drop += async (_, args) =>
            {
                // DragOver 里没法 await（拿不到文件列表），所以"是不是音频"只能在落地时判。
                if (!args.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    return;
                }

                var items = await args.DataView.GetStorageItemsAsync();
                var files = items
                    .OfType<StorageFile>()
                    .Select(item => item.Path)
                    .Where(path => ProjectVoiceAssetService.IsValidVoicePath(path))
                    .ToList();

                if (files.Count == 0)
                {
                    // 拖了不支持的东西：把悬停时亮起来的线收回去，别留一条空的。
                    if (!string.Equals(_voicePlayingRowId, row.Id, StringComparison.Ordinal))
                    {
                        HideVoiceProgressBar(row.Id);
                    }

                    ShowFloatingTip(
                        InfoBarSeverity.Warning,
                        "不是支持的音频格式",
                        "支持：" + string.Join(" / ", ProjectVoiceAssetService.Extensions));
                    return;
                }

                await HandleVoiceFilesDroppedAsync(row, rowCount, files);
            };

            grid.Children.Add(CreateProjectTextScrollableCell(button, 1, hasRightBorder: true));
            // 时长列（DB 前面）+ DB 列：两格都是「测量后才知道」的信息，
            // 一起登记、一起回填，见 MainWindow.VoiceTool.cs。
            var (durationCell, loudnessCell) = CreateVoiceMetricsCells(row, hasVoice ? value : string.Empty);
            grid.Children.Add(durationCell);
            grid.Children.Add(loudnessCell);
            grid.Children.Add(CreateProjectVoicePreviewCell(previewButton));

            // 说话人竖线：勾了「有语音」的角色，台词行左侧一条角色色竖线。
            // 宽度和底部那条播放标记线一致，只是方向不同；**没有动画** ——
            // 它是常驻标识，不是一次性反馈，动起来反而吵。
            var speakerKey = ToSpeakerKey(row.Speaker);
            if (speakerKey.Length > 0 &&
                IsSpeakerVoiced(speakerKey))
            {
                grid.Children.Add(new Border
                {
                    Width = 2,
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = ResolveSpeakerBrush(speakerKey),
                    IsHitTestVisible = false
                });
            }

            // 批量选择标记：叠在角色色竖线上的一层炫彩条。
            // 用渐变而不是纯色，是为了和"角色颜色"一眼分得开 ——
            // 用户看的是"我选了谁"，不是"这行是谁说的"。
            // 紫→蓝（violet-600 #7C3AED → indigo-500 #6366F1 → blue-500 #3B82F6）。
            // 选这三档是因为它们在深色/浅色背景上都够亮，且和角色色（暖色系）不撞。
            //
            // 初始 ScaleY = 0、Opacity = 0：选中时「从中间向两边长开 + 淡入」，
            // 和底部那条播放线同一套语言（用户要的就是这个手势）。
            var selectOverlay = new Border
            {
                Width = 4,
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(2),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 0 },
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0xFF, 0x7C, 0x3A, 0xED), Offset = 0.0 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0xFF, 0x63, 0x66, 0xF1), Offset = 0.5 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6), Offset = 1.0 }
                    }
                }
            };
            grid.Children.Add(selectOverlay);
            _voiceSelectOverlays[row.Id] = selectOverlay;

            // 播放进度条：一条 2px 的轨道（白）+ 一条从左侧生长的绿线。
            //
            // 两个细节是刻意的：
            // · 靠下对齐放在第 0 行里，**不新增行** —— 新增一行会让每行都长高 2px，
            //   而它只是指示，不该改变排版；
            // · 必须**最后**加，否则会被后面的单元格盖住（同级子元素按添加顺序叠）。
            var progressTrack = new Border
            {
                Height = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                // 白条是"从中间向两边铺开"的：原点放在正中，ScaleX 0→1 就是从中心长出来。
                Opacity = 0,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform { ScaleX = 0, ScaleY = 1 }
            };
            Grid.SetColumnSpan(progressTrack, grid.ColumnDefinitions.Count);
            grid.Children.Add(progressTrack);

            var progressFill = new Border
            {
                Height = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush,
                IsHitTestVisible = false,
                // 从头往右长：原点是左边中点，ScaleX 直接从 0 到 1。
                RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5),
                RenderTransform = new ScaleTransform { ScaleX = 0 },
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumnSpan(progressFill, grid.ColumnDefinitions.Count);
            grid.Children.Add(progressFill);
            _voiceProgressBars[row.Id] = new VoiceProgressBar(progressTrack, progressFill);

            return grid;
        }

        private Grid CreateProjectTextLocalizationTableRow(
            ProjectTextRow row,
            string value,
            Dictionary<string, string> languageMap)
        {
            var grid = CreateProjectTextGrid();
            grid.Children.Add(CreateProjectTextSourceCell(row));
            var textBox = new TextBox
            {
                Text = value,
                PlaceholderText = "填写目标语言文本",
                AcceptsReturn = false,
                MinWidth = 520,
                Margin = new Thickness(8),
                Tag = row.Id
            };
            textBox.TextChanged += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    languageMap.Remove(row.Id);
                }
                else
                {
                    languageMap[row.Id] = textBox.Text;
                }

                if (_currentProject is not null)
                {
                    _projectTextDataService.WriteLocalization(_currentProject, _projectLocalizationState);
                }
            };
            grid.Children.Add(CreateProjectTextScrollableCell(textBox, 1));
            return grid;
        }

        private static Grid CreateProjectTextGrid(bool includeVoicePreview = false, bool includeLoudness = false)
        {
            var grid = new Grid
            {
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            if (includeLoudness)
            {
                // 语音页（2026-11-02 二次改版）：文本 3 : 语音 2 : 时长 96 : DB 230 : 试听 64。
                // 起因是用户看完第一版截图后要求「文本再缩 1/3、语音再缩 2/5，并在 DB 前面加一列时长」。
                // 必须知道的账：星号列永远吃满剩余，所以「两列同时绝对变窄」只有把空间让给固定列才成立
                // —— 这一版让出的就是新增的时长列（DB 反而按用户要求收紧了 90）。
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 200 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 180 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                if (includeVoicePreview)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                }

                return grid;
            }

            // 文本本地化页维持原来的 1:1（这次只动语音页）。
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            if (includeVoicePreview)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            }

            return grid;
        }

        /// <summary>
        /// 文本工具里那句「章节 / 第 N 小节 / 行 M」的位置文字。
        ///
        /// 小节号必须走 <see cref="StorySectionDisplay"/> 的**显示口径（0 起）**。
        /// 这里原来直接印内部的 1 起号，于是同一句话在剧情编辑器里是「第 6 小节」、
        /// 到文本语音/本地化里变成「小节 7」—— 两边对不上，用户没法确认自己看的是不是同一节。
        /// （2026-08-23 那次「统一成 0 起」的改动漏了这里，还有 TODO 备注列表。）
        ///
        /// 顺带删掉了一个没人调用的 CreateProjectTextSourceContent —— 它拼的是同一段文字，
        /// 留着就是第二份口径，迟早各自漂。
        /// </summary>
        private static string CreateProjectTextRowLocation(ProjectTextRow row)
        {
            return $"{row.ChapterCode} / {StorySectionDisplay.FormatName(row.Section)} / 行 {row.RowName}";
        }

        private static Border CreateProjectTextSourceCell(ProjectTextRow row)
        {
            var panel = new StackPanel
            {
                Spacing = 4
            };
            panel.Children.Add(new TextBlock
            {
                Text = row.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });
            panel.Children.Add(new TextBlock
            {
                Text = CreateProjectTextRowLocation(row),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });

            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = panel
            };
            Grid.SetColumn(border, 0);
            return border;
        }

        private static void UpdateProjectVoiceButton(Button button, string voicePath)
        {
            var hasVoice = !string.IsNullOrWhiteSpace(voicePath);
            button.Content = hasVoice ? Path.GetFileName(voicePath) : "点击/拖入 设置语音文件";
            button.Foreground = Application.Current.Resources[
                hasVoice ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"] as Brush;
            button.FontWeight = hasVoice
                ? Microsoft.UI.Text.FontWeights.Normal
                : Microsoft.UI.Text.FontWeights.Normal;
            button.Opacity = hasVoice ? 1 : 0.72;
            ToolTipService.SetToolTip(button, hasVoice ? voicePath : "未设置语音文件");
        }

        private Button CreateProjectVoicePreviewButton(ProjectTextRow row, string voicePath)
        {
            var button = new Button
            {
                Width = 40,
                Height = 36,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new SymbolIcon(Symbol.Play),
                Tag = row.Id
            };
            UpdateProjectVoicePreviewButton(button, voicePath);
            // 播放/暂停要换图标，按钮得留个引用（见 SetVoicePreviewButtonGlyph）。
            _voicePreviewButtons[row.Id] = button;
            button.Click += async (_, _) => await ToggleProjectVoicePreviewAsync(row);
            return button;
        }

        private static void UpdateProjectVoicePreviewButton(Button button, string voicePath)
        {
            var canPreview = !string.IsNullOrWhiteSpace(voicePath) && File.Exists(voicePath);
            button.IsEnabled = canPreview;
            ToolTipService.SetToolTip(button, canPreview ? "播放语音测试" : "当前行没有可播放的语音");
        }

        /// <summary>
        /// 试听按钮：**播放 / 暂停切换**。
        ///
        /// 以前不管什么状态都重设 Source 再 Play，所以点第二次等于"从头再播"，
        /// 想暂停根本没门（2026-11-04 用户实测）。现在分三种情况：
        /// · 正在播这一行 → 暂停（进度停在原地，绿条也停住）
        /// · 暂停的就是这一行 → 从原位置继续
        /// · 其它情况（换了一行 / 上一条已播完）→ 换源，从头播
        /// </summary>
        private async Task ToggleProjectVoicePreviewAsync(ProjectTextRow row)
        {
            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) || !File.Exists(voicePath))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "语音无法播放", "当前行没有可用的语音文件。");
                return;
            }

            var isSameRow = string.Equals(_voicePlayingRowId, row.Id, StringComparison.Ordinal);
            var state = _projectVoicePreviewPlayer.PlaybackSession.PlaybackState;

            if (isSameRow && state == MediaPlaybackState.Playing)
            {
                _projectVoicePreviewPlayer.Pause();
                StopVoiceProgressTimer();
                _voicePreviewPaused = true;
                SetVoicePreviewButtonGlyph(row.Id, isPlaying: false);
                return;
            }

            if (isSameRow && _voicePreviewPaused && state == MediaPlaybackState.Paused)
            {
                // 续播不用再等缓冲，直接接着走。
                _voiceAwaitingPlayback = false;
                _projectVoicePreviewPlayer.Play();
                StartVoiceProgressTimer();
                _voicePreviewPaused = false;
                SetVoicePreviewButtonGlyph(row.Id, isPlaying: true);
                return;
            }

            // 换一条：先清掉上一条的进度与图标，再装新的。
            ResetVoicePreviewProgress();

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(voicePath);
                _projectVoicePreviewPlayer.Source = MediaSource.CreateFromStorageFile(file);
                _projectVoicePreviewPlayer.Play();

                // 别急着推进度：声音要解码缓冲，这段时间绿条不该动
                // （白条那 220ms 入场动画正好当加载指示）。
                AwaitVoicePlaybackStart();

                _voicePlayingRowId = row.Id;
                _voicePreviewPaused = false;
                ShowVoiceProgressBar(row.Id);
                SetVoicePreviewButtonGlyph(row.Id, isPlaying: true);
                AppendLog(LogKind.Info, $"试听语音：{Path.GetFileName(voicePath)}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "语音播放失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"试听语音失败：{Path.GetFileName(voicePath)}");
            }
        }

        private void StopProjectVoicePreview()
        {
            _projectVoicePreviewPlayer.Pause();
            _projectVoicePreviewPlayer.Source = null;
            // 停播也要把进度条和图标复位，否则那一行会永远停在"正在播"的样子。
            ResetVoicePreviewProgress();
        }

        private static Border CreateProjectVoicePreviewCell(Button button)
        {
            var border = new Border
            {
                Child = button
            };
            // 列 4：语音页现在的列序是 文本 / 语音 / 时长 / DB / 试听。
            Grid.SetColumn(border, 4);
            return border;
        }

        private static Border CreateProjectTextCell(
            string text,
            int column,
            bool isHeader = false,
            bool hasRightBorder = false,
            bool centerContent = false)
        {
            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = hasRightBorder ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = centerContent ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = Application.Current.Resources[isHeader ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"] as Brush
                }
            };
            Grid.SetColumn(border, column);
            return border;
        }

        private static Border CreateProjectTextScrollableCell(FrameworkElement content, int column, bool hasRightBorder = false)
        {
            var border = new Border
            {
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = hasRightBorder ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollMode = ScrollMode.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollMode = ScrollMode.Disabled,
                    Content = content
                }
            };
            Grid.SetColumn(border, column);
            return border;
        }

    }
}
