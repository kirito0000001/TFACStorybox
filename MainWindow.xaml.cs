using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    public sealed partial class MainWindow : Window
    {
        private const string ProjectRootFolderName = "GalExcelProject";
        private const string DefaultProjectRootPath = @"D:\GalExcelProject";
        private const string ToolsFolderName = "Tools";
        private const string ExcelFolderName = "Excel";
        private const string BackgroundFolderName = "背景图";
        private const string CharacterFolderName = "立绘";
        private const string MusicFolderName = "音乐";
        private const string AmbientSoundFolderName = "环境音";
        private const string SoundEffectFolderName = "特殊音效";
        private const string FunctionFolderName = "函数";
        private const string FunctionIndexFileName = "functions.json";
        private const string ChoiceFunctionCategory = "触发选项";
        private const string ChoiceFunctionTemplateId = "default-choice";
        private const string ChoiceFunctionTemplateIndicator = "自动生成当前章节小节Choice";
        private const string JumpFunctionCategory = "跳转";
        private const string ChapterJumpFunctionTemplateId = "default-into-chapter";
        private const string ChapterJumpFunctionTemplateIndicator = "IntoChapter_{章节}";
        private const string SegmentJumpFunctionTemplateId = "default-into-segment";
        private const string SegmentJumpFunctionTemplateIndicator = "IntoSegment_{小节}";
        private const string BgmFunctionTemplateId = "default-bgm-control";
        private const string BgmFunctionTemplateIndicator = "BGM_Start/BGM_Stop";
        private const string CharacterFilterFolderName = "角色滤镜";
        private const string CharacterFilterIndexFileName = "vfx-filters.json";
        private const string NoStoryCharacterChoice = "__NO_STORY_CHARACTER__";
        private const string ProjectMetaFileName = "project.meta.json";
        private const string AssetLibraryMetaFileName = "asset-library.meta.json";
        private const string ChapterMetaFileName = "chapter.meta.json";
        private const string StorySectionsFileName = "story.sections.json";
        private const string StoryChoiceNotesFileName = "story.choice-notes.json";
        private const string StorySectionExportsFolderName = "SectionCsv";
        private const string UnrealStorySectionCacheFolderName = "UnrealStorySections";
        private const string ChapterBackupsFolderName = "ChapterBackups";
        private const string ProjectBackupsFolderName = "ProjectBackups";
        private const string AssetLibraryBackupsFolderName = "AssetLibraryBackups";
        private const string UnrealBackupsFolderName = "UnrealBackups";
        private const string UnrealAssetIndexTablesFolderName = "UnrealAssetIndexTables";
        private const int MaxFolderBackupCount = 3;
        private const string ChaptersFolderName = "Chapters";
        private const string DefaultThumbnailUri = "ms-appx:///Assets/DefaultProjectThumbnail.png";
        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];
        private static readonly HashSet<string> ConvertibleImageExtensions =
            new([".jpg", ".jpeg", ".webp"], StringComparer.OrdinalIgnoreCase);
        private static readonly string[] MusicExtensions = [".wav"];

        private static readonly string SettingsDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GalExcleTools");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectoryPath, "settings.json");

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };
        private readonly MediaPlayer _storyBgmPlayer = new();
        private readonly MediaPlayer _storyScenePlayer = new();

        private string _projectRootPath = DefaultProjectRootPath;
        private string? _selectedProjectThumbnailPath;
        private string? _selectedAssetLibraryThumbnailPath;
        private AssetLibraryInfo? _currentAssetLibrary;
        private ChapterInfo? _currentStoryChapter;
        private AssetLibraryInfo? _currentStoryAssetLibrary;
        private readonly List<StoryRow> _storyRows = [];
        private readonly Dictionary<string, int> _storyRowSections = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BitmapImage> _storyPreviewImageCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _storyCharacterPreviewKeys = new();
        private int _currentStoryRowIndex;
        private StoryCharacterSlotClipboard? _storyCharacterSlotClipboard;
        private StoryAssetClipboard? _storyAssetClipboard;
        private bool _isLoadingStoryRow;
        private bool _isStoryRowDirty;
        private bool _isUpdatingStoryRowIndexText;
        private bool _isUpdatingStorySectionOptions;
        private bool _isPersistingStoryRows;
        private string? _currentStoryCsvPath;
        private string? _storyBackgroundPreviewKey;
        private int _storyBackgroundTransitionMode;
        private bool _storyBgmPlaybackSuppressed;
        private AppSettings _appSettings = new();
        private bool _isApplyingLogSettings;
        private bool _isNormalizingBackgroundImages;
        private bool _isNormalizingMusicFiles;
        private bool _isNormalizingAmbientSoundFiles;
        private bool _isNormalizingSoundEffectFiles;
        private bool _isNormalizingCharacterClothes;
        private bool _isNormalizingCharacterFaces;
        private bool _isNormalizingCharacterAdorns;
        private bool _isReorderingCharacterFilters;
        private bool _isRepairingCharacterFilters;
        private bool _runFullUnrealSyncRequested;
        private bool _isLoadingUnrealSyncProjects;
        private GridViewItem? _draggingBackgroundImageItem;
        private GridViewItem? _draggingMusicItem;
        private GridViewItem? _draggingAmbientSoundItem;
        private GridViewItem? _draggingSoundEffectItem;
        private GridViewItem? _draggingCharacterFilterItem;
        private GridViewItem? _draggingCharacterClothItem;
        private GridViewItem? _draggingCharacterFaceItem;
        private GridViewItem? _draggingCharacterAdornItem;
        private string? _viewingBackgroundImagePath;
        private string? _viewingCharacterClothPath;
        private string? _viewingCharacterFacePath;
        private string? _viewingCharacterAdornPath;
        private string? _playingMusicPath;
        private AudioAssetKind _playingAudioKind = AudioAssetKind.Music;
        private string? _storyBgmPath;
        private string? _storyScenePath;
        private int _storyBgmPlaybackRequestId;
        private int _storyScenePlaybackRequestId;
        private ProjectInfo? _currentProject;
        private CharacterInfo? _currentCharacter;
        private bool _isCreatingChapter;
        private int? _hoveredStoryCharacterSlotIndex;
        private string? _hoveredStoryAssetFieldName;
        private string? _selectedCharacterClothPath;
        private string? _selectedCharacterFacePath;
        private string? _selectedCharacterAdornPath;
        private string? _selectedCharacterVfxPath;
        private bool _isPanningBackgroundImage;
        private Windows.Foundation.Point _lastBackgroundImagePointerPosition;
        private double _backgroundImageViewerScale = 1;
        private readonly DispatcherQueueTimer _refreshDelayTimer;
        private readonly DispatcherQueueTimer? _storyEditSaveTimer;
        private readonly DispatcherQueueTimer _storyPaneAnimationTimer;
        private double _storyPaneAnimationTargetWidth = 46;
        private readonly Dictionary<InfoBar, DispatcherQueueTimer> _storyTransientTipTimers = new();
        private readonly List<StoryEditorUndoState> _storyUndoStack = [];
        private bool _refreshBackgroundImagesAfterDelay;
        private bool _isStoryDebugModeEnabled;
        private const int MaxStoryUndoCount = 80;

        public MainWindow()
        {
            InitializeComponent();
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();
            _refreshDelayTimer = DispatcherQueue.CreateTimer();
            _refreshDelayTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshDelayTimer.Tick += RefreshDelayTimer_Tick;
            _storyEditSaveTimer = DispatcherQueue.CreateTimer();
            _storyEditSaveTimer.Interval = TimeSpan.FromMilliseconds(650);
            _storyEditSaveTimer.Tick += StoryEditSaveTimer_Tick;
            _storyPaneAnimationTimer = DispatcherQueue.CreateTimer();
            _storyPaneAnimationTimer.Interval = TimeSpan.FromMilliseconds(16);
            _storyPaneAnimationTimer.Tick += StoryPaneAnimationTimer_Tick;
            _storyBgmPlayer.IsLoopingEnabled = true;
            _storyScenePlayer.IsLoopingEnabled = true;

            _appSettings = LoadAppSettings();
            ApplyLogSettingsToUi();
            ApplyUnrealSyncSettingsToUi();
            ApplyStoryTextFontSizeToUi();
            _projectRootPath = string.IsNullOrWhiteSpace(_appSettings.ProjectRootPath) ? DefaultProjectRootPath : _appSettings.ProjectRootPath;
            EnsureProjectRootDirectory(_projectRootPath);
            AppendLog(LogKind.Info, "程序启动，已检查整体项目目录。");
            ShowWorkbenchPage();
        }

        private void ApplyWindowIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }

        private void ApplyCustomTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }

        private void ApplyInitialWindowPlacement()
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void ApplyLogSettingsToUi()
        {
            _isApplyingLogSettings = true;
            try
            {
                WorkspacePathVisibleCheckBox.IsChecked = _appSettings.ShowWorkspacePath;
                LogEnabledCheckBox.IsChecked = _appSettings.LogEnabled;
                LogUserOperationsCheckBox.IsChecked = _appSettings.LogUserOperations;
                LogWarningCheckBox.IsChecked = _appSettings.LogWarnings;
                LogErrorCheckBox.IsChecked = _appSettings.LogErrors;
                AssetLibraryScrollSpeedSlider.Value = _appSettings.AssetLibraryScrollSpeedMultiplier;
                StoryTextFontSizeSlider.Value = Math.Clamp(_appSettings.StoryTextFontSize, 16, 32);
                ShowFullChapterLengthCheckBox.IsChecked = _appSettings.ShowFullStoryChapterLength;
                UpdateAssetLibraryScrollSpeedText();
                UpdateStoryTextFontSizeText();
                UpdateLogOptionEnabledState();
                UpdateAuxiliaryDisplayVisibility();
            }
            finally
            {
                _isApplyingLogSettings = false;
            }
        }

        private void UpdateLogOptionEnabledState()
        {
            var enabled = LogEnabledCheckBox.IsChecked == true;
            LogUserOperationsCheckBox.IsEnabled = enabled;
            LogWarningCheckBox.IsEnabled = enabled;
            LogErrorCheckBox.IsEnabled = enabled;
        }

        private void UpdateAuxiliaryDisplayVisibility()
        {
            var showWorkspacePath = _appSettings.ShowWorkspacePath ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceStatusBorder.Visibility = showWorkspacePath;
            AssetLibraryStatusBorder.Visibility = showWorkspacePath;
            AssetLibraryDetailStatusBorder.Visibility = showWorkspacePath;
            LogPanelBorder.Visibility = _appSettings.LogEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LogSettingControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingLogSettings)
            {
                return;
            }

            _appSettings.ShowWorkspacePath = WorkspacePathVisibleCheckBox.IsChecked == true;
            _appSettings.LogEnabled = LogEnabledCheckBox.IsChecked == true;
            _appSettings.LogUserOperations = LogUserOperationsCheckBox.IsChecked == true;
            _appSettings.LogWarnings = LogWarningCheckBox.IsChecked == true;
            _appSettings.LogErrors = LogErrorCheckBox.IsChecked == true;
            SaveAppSettings();
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();

            if (_appSettings.LogEnabled)
            {
                AppendLog(LogKind.User, "已更新日志输出设置。");
            }
        }

        private void AssetLibraryScrollSpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isApplyingLogSettings || AssetLibraryScrollSpeedValueText is null)
            {
                return;
            }

            _appSettings.AssetLibraryScrollSpeedMultiplier = Math.Round(AssetLibraryScrollSpeedSlider.Value, 1);
            UpdateAssetLibraryScrollSpeedText();
            SaveAppSettings();
        }

        private void UpdateAssetLibraryScrollSpeedText()
        {
            AssetLibraryScrollSpeedValueText.Text = $"{_appSettings.AssetLibraryScrollSpeedMultiplier:0.0}x";
        }

        private void StoryTextFontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isApplyingLogSettings || StoryTextFontSizeValueText is null)
            {
                return;
            }

            _appSettings.StoryTextFontSize = Math.Round(StoryTextFontSizeSlider.Value);
            UpdateStoryTextFontSizeText();
            ApplyStoryTextFontSizeToUi();
            SaveAppSettings();
        }

        private void StoryChapterLengthDisplayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingLogSettings)
            {
                return;
            }

            _appSettings.ShowFullStoryChapterLength = ShowFullChapterLengthCheckBox?.IsChecked == true;
            SaveAppSettings();
            UpdateStoryRowIndexInput();
        }

        private void UpdateStoryTextFontSizeText()
        {
            StoryTextFontSizeValueText.Text = $"{Math.Round(_appSettings.StoryTextFontSize)}";
        }

        private void ApplyStoryTextFontSizeToUi()
        {
            if (StoryTextTextBox is not null)
            {
                StoryTextTextBox.FontSize = Math.Clamp(_appSettings.StoryTextFontSize, 16, 32);
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogItemsControl.Items.Clear();
            AppendLog(LogKind.User, "已清空输出日志。");
        }

        private bool ShouldWriteLog(LogKind kind)
        {
            if (!_appSettings.LogEnabled)
            {
                return false;
            }

            return kind switch
            {
                LogKind.User => _appSettings.LogUserOperations,
                LogKind.Warning => _appSettings.LogWarnings,
                LogKind.Error => _appSettings.LogErrors,
                _ => true
            };
        }

        private void AppendLog(LogKind kind, string message, Exception? exception = null)
        {
            if (!ShouldWriteLog(kind))
            {
                return;
            }

            var text = $"[{DateTime.Now:HH:mm:ss}] [{GetLogKindLabel(kind)}] {message}";
            if (exception is not null)
            {
                text += $" 原因：{exception.Message}";
            }

            LogItemsControl.Items.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = GetLogBrush(kind)
            });

            const int maxLogCount = 300;
            while (LogItemsControl.Items.Count > maxLogCount)
            {
                LogItemsControl.Items.RemoveAt(0);
            }

            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        }

        private static string GetLogKindLabel(LogKind kind)
        {
            return kind switch
            {
                LogKind.User => "用户",
                LogKind.Warning => "提示",
                LogKind.Error => "错误",
                _ => "程序"
            };
        }

        private static Brush? GetLogBrush(LogKind kind)
        {
            return kind switch
            {
                LogKind.Warning => Application.Current.Resources["SystemFillColorCautionBrush"] as Brush,
                LogKind.Error => Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush,
                _ => Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
            };
        }

        private void EnsureProjectRootDirectory(string projectRootPath)
        {
            Directory.CreateDirectory(projectRootPath);

            ProjectRootPathTextBox.Text = projectRootPath;
            ProjectRootStatusInfoBar.Message = $"已确认目录存在：{projectRootPath}";
            WorkspaceStatusText.Text = $"工作区路径：{projectRootPath}";
            AssetLibraryStatusText.Text = $"素材库根目录：{projectRootPath}";

            LoadAllCards();
            AppendLog(LogKind.Info, $"已确认整体项目目录：{projectRootPath}");
        }

        private void RequestDelayedRefresh(bool includeBackgroundImages = false)
        {
            _refreshBackgroundImagesAfterDelay |= includeBackgroundImages;
            _refreshDelayTimer.Stop();
            _refreshDelayTimer.Start();
        }

        private void RefreshDelayTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            LoadAllCards();

            if (_refreshBackgroundImagesAfterDelay && _currentAssetLibrary is not null)
            {
                LoadBackgroundImages(_currentAssetLibrary);
            }

            _refreshBackgroundImagesAfterDelay = false;
            AppendLog(LogKind.Info, "延迟刷新完成。");
        }

        private void LoadAllCards()
        {
            LoadAssetLibraries();
            LoadProjects();
            LoadAssetLibraryOptions();
        }

        private List<AssetLibraryInfo> GetAssetLibraries()
        {
            if (!Directory.Exists(_projectRootPath))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(_projectRootPath)
                .Where(path => File.Exists(Path.Combine(path, ToolsFolderName, AssetLibraryMetaFileName)))
                .Select(ReadAssetLibraryInfo)
                .OrderByDescending(library => library.LastEditedAt)
                .ToList();
        }

        private List<ProjectInfo> GetProjects()
        {
            if (!Directory.Exists(_projectRootPath))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(_projectRootPath)
                .Where(path => File.Exists(Path.Combine(path, ToolsFolderName, ProjectMetaFileName)))
                .Select(ReadProjectInfo)
                .OrderByDescending(project => project.LastEditedAt)
                .ToList();
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

        private ProjectInfo ReadProjectInfo(string projectPath)
        {
            var projectName = Path.GetFileName(projectPath);
            var toolsPath = Path.Combine(projectPath, ToolsFolderName);
            var metaPath = Path.Combine(toolsPath, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            var thumbnailPath = ResolveThumbnailPath(toolsPath, meta.ThumbnailFileName);

            return new ProjectInfo(
                string.IsNullOrWhiteSpace(meta.ProjectName) ? projectName : meta.ProjectName,
                string.IsNullOrWhiteSpace(meta.ProjectCode) ? Path.GetFileName(projectPath) : meta.ProjectCode,
                Path.GetFileName(projectPath),
                projectPath,
                thumbnailPath,
                string.IsNullOrWhiteSpace(meta.AssetLibraryName) ? "未关联素材库" : meta.AssetLibraryName,
                meta.AssetLibraryFolderName,
                meta.LastEditedAt == default ? Directory.GetLastWriteTime(projectPath) : meta.LastEditedAt);
        }

        private AssetLibraryInfo ReadAssetLibraryInfo(string assetLibraryPath)
        {
            var libraryName = Path.GetFileName(assetLibraryPath);
            var toolsPath = Path.Combine(assetLibraryPath, ToolsFolderName);
            var metaPath = Path.Combine(toolsPath, AssetLibraryMetaFileName);
            var meta = ReadJson<AssetLibraryMeta>(metaPath) ?? new AssetLibraryMeta();
            var thumbnailPath = ResolveThumbnailPath(toolsPath, meta.ThumbnailFileName);

            return new AssetLibraryInfo(
                string.IsNullOrWhiteSpace(meta.AssetLibraryName) ? libraryName : meta.AssetLibraryName,
                Path.GetFileName(assetLibraryPath),
                assetLibraryPath,
                thumbnailPath,
                meta.LastEditedAt == default ? Directory.GetLastWriteTime(assetLibraryPath) : meta.LastEditedAt);
        }

        private static string? ResolveThumbnailPath(string toolsPath, string? thumbnailFileName)
        {
            if (string.IsNullOrWhiteSpace(thumbnailFileName))
            {
                return null;
            }

            var thumbnailPath = Path.Combine(toolsPath, thumbnailFileName);
            return File.Exists(thumbnailPath) ? thumbnailPath : null;
        }

        private static T? ReadJson<T>(string path)
        {
            if (!File.Exists(path))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch
            {
                return default;
            }
        }

        private GridViewItem CreateProjectCard(ProjectInfo project)
        {
            var item = CreateBaseCard(project);
            var flyout = new MenuFlyout();
            var renameItem = new MenuFlyoutItem
            {
                Text = "重命名"
            };
            renameItem.Click += async (_, _) => await RenameProjectAsync(project);
            flyout.Items.Add(renameItem);

            var changeLibraryItem = new MenuFlyoutItem
            {
                Text = "更改目标素材库"
            };
            changeLibraryItem.Click += async (_, _) => await ChangeProjectAssetLibraryAsync(project);
            flyout.Items.Add(changeLibraryItem);

            var openFolderItem = new MenuFlyoutItem
            {
                Text = "打开文件夹"
            };
            openFolderItem.Click += (_, _) => OpenFolderInExplorer(project.Path);
            flyout.Items.Add(openFolderItem);

            var backupItem = new MenuFlyoutItem
            {
                Text = "备份"
            };
            backupItem.Click += async (_, _) => await BackupProjectFromUiAsync(project);
            flyout.Items.Add(backupItem);

            var restoreItem = new MenuFlyoutItem
            {
                Text = "还原"
            };
            restoreItem.Click += async (_, _) => await RestoreProjectFromUiAsync(project);
            flyout.Items.Add(restoreItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除"
            };
            deleteItem.Click += async (_, _) => await DeleteProjectAsync(project);
            flyout.Items.Add(deleteItem);
            item.ContextFlyout = flyout;
            item.Tapped += ProjectCard_Tapped;
            item.Content = CreateCardContent(project.ThumbnailPath, project.Name, $"{project.Code} | 素材库：{project.AssetLibraryName}", $"上次打开时间 {project.LastEditedAt:yyyy-MM-dd HH:mm}");
            return item;
        }

        private GridViewItem CreateAssetLibraryCard(AssetLibraryInfo assetLibrary)
        {
            var item = CreateBaseCard(assetLibrary);
            var flyout = new MenuFlyout();
            var renameItem = new MenuFlyoutItem
            {
                Text = "重命名"
            };
            renameItem.Click += async (_, _) => await RenameAssetLibraryAsync(assetLibrary);
            flyout.Items.Add(renameItem);

            var openFolderItem = new MenuFlyoutItem
            {
                Text = "打开文件夹"
            };
            openFolderItem.Click += (_, _) => OpenFolderInExplorer(assetLibrary.Path);
            flyout.Items.Add(openFolderItem);

            var backupItem = new MenuFlyoutItem
            {
                Text = "备份"
            };
            backupItem.Click += async (_, _) => await BackupAssetLibraryFromUiAsync(assetLibrary);
            flyout.Items.Add(backupItem);

            var restoreItem = new MenuFlyoutItem
            {
                Text = "还原"
            };
            restoreItem.Click += async (_, _) => await RestoreAssetLibraryFromUiAsync(assetLibrary);
            flyout.Items.Add(restoreItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除"
            };
            deleteItem.Click += async (_, _) => await DeleteAssetLibraryAsync(assetLibrary);
            flyout.Items.Add(deleteItem);
            item.ContextFlyout = flyout;
            item.Tapped += AssetLibraryCard_Tapped;
            item.Content = CreateCardContent(assetLibrary.ThumbnailPath, assetLibrary.Name, "素材合集", $"上次编辑时间 {assetLibrary.LastEditedAt:yyyy-MM-dd HH:mm}");
            return item;
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
                    progress => Task.Run(() => CreateFolderBackup(project.Path, ProjectBackupsFolderName, project.Code, note, progress)));
                LoadProjects();
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"备份项目：{project.Name} -> {backup.Path}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "备份项目失败。", ex);
            }
        }

        private async Task RestoreProjectFromUiAsync(ProjectInfo project)
        {
            try
            {
                var backups = GetFolderBackups(project.Path, ProjectBackupsFolderName);
                if (backups.Count == 0)
                {
                    AppendLog(LogKind.Warning, $"项目没有可还原的备份：{project.Name}");
                    return;
                }

                var selectedBackup = await ShowFolderRestoreDialogAsync("还原项目", project.Name, backups);
                if (selectedBackup is null)
                {
                    return;
                }

                RestoreFolderBackup(project.Path, ProjectBackupsFolderName, selectedBackup);
                LoadProjects();
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"还原项目：{project.Name} <- {selectedBackup.Path}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "还原项目失败。", ex);
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
                    progress => Task.Run(() => CreateFolderBackup(assetLibrary.Path, AssetLibraryBackupsFolderName, assetLibrary.FolderName, note, progress)));
                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"备份素材库：{assetLibrary.Name} -> {backup.Path}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "备份素材库失败。", ex);
            }
        }

        private async Task RestoreAssetLibraryFromUiAsync(AssetLibraryInfo assetLibrary)
        {
            try
            {
                var backups = GetFolderBackups(assetLibrary.Path, AssetLibraryBackupsFolderName);
                if (backups.Count == 0)
                {
                    AppendLog(LogKind.Warning, $"素材库没有可还原的备份：{assetLibrary.Name}");
                    return;
                }

                var selectedBackup = await ShowFolderRestoreDialogAsync("还原素材库", assetLibrary.Name, backups);
                if (selectedBackup is null)
                {
                    return;
                }

                RestoreFolderBackup(assetLibrary.Path, AssetLibraryBackupsFolderName, selectedBackup);
                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"还原素材库：{assetLibrary.Name} <- {selectedBackup.Path}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "还原素材库失败。", ex);
            }
        }

        private FolderBackupEntry CreateFolderBackup(
            string folderPath,
            string backupsFolderName,
            string nameSeed,
            string note,
            IProgress<FolderBackupProgress>? progress = null)
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"文件夹不存在：{folderPath}");
            }

            var backupsPath = GetFolderBackupsPath(folderPath, backupsFolderName);
            Directory.CreateDirectory(backupsPath);

            var createdAt = DateTime.Now;
            var safeName = SanitizeBackupFileName(nameSeed);
            var safeNote = SanitizeBackupFileName(note);
            var noteSuffix = string.IsNullOrWhiteSpace(safeNote) ? string.Empty : $"_{safeNote}";
            var backupPath = Path.Combine(backupsPath, $"{safeName}_{createdAt:yyyyMMdd_HHmmss}{noteSuffix}.zip");
            var duplicateIndex = 1;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(backupsPath, $"{safeName}_{createdAt:yyyyMMdd_HHmmss}{noteSuffix}_{duplicateIndex}.zip");
                duplicateIndex++;
            }

            progress?.Report(new FolderBackupProgress("正在扫描要写入备份的文件...", 0, 0, 0, 0, 0, null));
            var files = EnumerateFolderBackupFiles(folderPath, backupsPath).ToList();
            var totalBytes = files.Sum(filePath => new FileInfo(filePath).Length);

            using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
            {
                long completedBytes = 0;
                for (var index = 0; index < files.Count; index++)
                {
                    var filePath = files[index];
                    var fileLength = new FileInfo(filePath).Length;
                    var relativePath = Path.GetRelativePath(folderPath, filePath).Replace('\\', '/');
                    var percent = files.Count == 0
                        ? 90
                        : Math.Min(90, Math.Max(1, completedBytes * 90d / Math.Max(1, totalBytes)));
                    progress?.Report(new FolderBackupProgress(
                        $"正在压缩 {index + 1}/{files.Count}：{relativePath}",
                        percent,
                        index,
                        files.Count,
                        completedBytes,
                        totalBytes,
                        relativePath));
                    archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                    completedBytes += fileLength;
                }
            }

            progress?.Report(new FolderBackupProgress("正在写入备份备注...", 94, files.Count, files.Count, totalBytes, totalBytes, null));
            var meta = new FolderBackupMeta
            {
                CreatedAt = createdAt,
                Note = NormalizeBackupNote(note)
            };
            File.WriteAllText(GetBackupMetaPath(backupPath), JsonSerializer.Serialize(meta, _jsonOptions));

            progress?.Report(new FolderBackupProgress("正在清理旧备份，最多保留 3 份...", 97, files.Count, files.Count, totalBytes, totalBytes, null));
            PruneFolderBackups(folderPath, backupsFolderName);
            progress?.Report(new FolderBackupProgress("备份完成。", 100, files.Count, files.Count, totalBytes, totalBytes, null));
            return BuildFolderBackupEntry(backupPath);
        }

        private static IEnumerable<string> EnumerateFolderBackupFiles(string folderPath, string currentBackupsPath)
        {
            return Directory
                .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(filePath => !ShouldSkipFolderBackupFile(filePath, currentBackupsPath));
        }

        private static bool ShouldSkipFolderBackupFile(string filePath, string currentBackupsPath)
        {
            if (IsPathInsideDirectory(filePath, currentBackupsPath))
            {
                return true;
            }

            var segments = Path.GetRelativePath(Path.GetPathRoot(Path.GetFullPath(filePath))!, filePath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return segments.Any(segment =>
                string.Equals(segment, ProjectBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, AssetLibraryBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ChapterBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, UnrealBackupsFolderName, StringComparison.OrdinalIgnoreCase));
        }

        private static void RestoreFolderBackup(string folderPath, string backupsFolderName, FolderBackupEntry backup)
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"文件夹不存在：{folderPath}");
            }

            if (!File.Exists(backup.Path))
            {
                throw new FileNotFoundException("选择的备份文件不存在。", backup.Path);
            }

            var tempPath = Path.Combine(Path.GetTempPath(), "GalExcleTools", "FolderRestore", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPath);
            try
            {
                ZipFile.ExtractToDirectory(backup.Path, tempPath);
                ClearFolderForRestore(folderPath, backupsFolderName);
                CopyDirectoryContents(tempPath, folderPath);
            }
            finally
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, recursive: true);
                }
            }
        }

        private static void ClearFolderForRestore(string folderPath, string backupsFolderName)
        {
            var backupsPath = GetFolderBackupsPath(folderPath, backupsFolderName);
            Directory.CreateDirectory(backupsPath);

            var folderRoot = Path.GetFullPath(folderPath);
            var backupsRoot = Path.GetFullPath(backupsPath);

            foreach (var filePath in Directory.EnumerateFiles(folderRoot, "*", SearchOption.AllDirectories))
            {
                if (IsPathInsideDirectory(filePath, backupsRoot))
                {
                    continue;
                }

                File.Delete(filePath);
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(folderRoot, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                if (PathsEqual(directoryPath, backupsRoot) || IsPathInsideDirectory(directoryPath, backupsRoot))
                {
                    continue;
                }

                if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    Directory.Delete(directoryPath);
                }
            }
        }

        private static void CopyDirectoryContents(string sourcePath, string targetPath)
        {
            foreach (var directoryPath in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, directoryPath);
                Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
            }

            foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, filePath);
                var targetFilePath = Path.Combine(targetPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
                File.Copy(filePath, targetFilePath, overwrite: true);
            }
        }

        private static void PruneFolderBackups(string folderPath, string backupsFolderName)
        {
            foreach (var backup in GetFolderBackups(folderPath, backupsFolderName).Skip(MaxFolderBackupCount))
            {
                DeleteFolderBackup(backup);
            }
        }

        private static void DeleteFolderBackup(FolderBackupEntry backup)
        {
            if (File.Exists(backup.Path))
            {
                File.Delete(backup.Path);
            }

            var metaPath = GetBackupMetaPath(backup.Path);
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        private static List<FolderBackupEntry> GetFolderBackups(string folderPath, string backupsFolderName)
        {
            var backupsPath = GetFolderBackupsPath(folderPath, backupsFolderName);
            if (!Directory.Exists(backupsPath))
            {
                return [];
            }

            return Directory
                .EnumerateFiles(backupsPath, "*.zip", SearchOption.TopDirectoryOnly)
                .Select(BuildFolderBackupEntry)
                .OrderByDescending(backup => backup.CreatedAt)
                .ToList();
        }

        private static FolderBackupEntry BuildFolderBackupEntry(string backupPath)
        {
            var fileInfo = new FileInfo(backupPath);
            var meta = ReadJson<FolderBackupMeta>(GetBackupMetaPath(backupPath));
            var createdAt = meta?.CreatedAt is { } metaCreatedAt && metaCreatedAt != default
                ? metaCreatedAt
                : fileInfo.LastWriteTime;
            var note = NormalizeBackupNote(meta?.Note ?? string.Empty);
            var noteText = string.IsNullOrWhiteSpace(note) ? "无备注" : note;
            var displayName = $"{createdAt:yyyy-MM-dd HH:mm:ss} · {noteText} · {FormatFileSize(fileInfo.Length)}";
            return new FolderBackupEntry(backupPath, createdAt, fileInfo.Length, note, displayName);
        }

        private static string GetFolderBackupsPath(string folderPath, string backupsFolderName)
        {
            return Path.Combine(folderPath, ToolsFolderName, backupsFolderName);
        }

        private static string GetBackupMetaPath(string backupPath)
        {
            return $"{backupPath}.meta.json";
        }

        private static string NormalizeBackupNote(string? note)
        {
            return Regex.Replace(note ?? string.Empty, @"\s+", " ").Trim();
        }

        private static string SanitizeBackupFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
            var normalized = NormalizeBackupNote(value);
            var sanitized = new string(normalized.Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return "Backup";
            }

            return sanitized.Length > 40 ? sanitized[..40] : sanitized;
        }

        private static string FormatFileSize(long byteCount)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            var size = (double)byteCount;
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0 ? $"{byteCount} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
        }

        private GridViewItem CreateAddCard(string title, string subtitle, TappedEventHandler tappedHandler)
        {
            var item = CreateBaseCard(null);
            item.Tapped += tappedHandler;
            ToolTipService.SetToolTip(item, title);
            item.Content = CreateCardContent(null, title, subtitle, string.Empty, showAddIcon: true);
            return item;
        }

        private static GridViewItem CreateBaseCard(object? tag)
        {
            return new GridViewItem
            {
                Width = 260,
                Height = 318,
                Margin = new Thickness(0, 0, 18, 18),
                Tag = tag
            };
        }

        private StackPanel CreateCardContent(string? thumbnailPath, string title, string subtitle, string footer, bool showAddIcon = false)
        {
            var panel = new StackPanel
            {
                Spacing = 8
            };
            ToolTipService.SetToolTip(panel, title);

            panel.Children.Add(CreateThumbnail(thumbnailPath, 236, 178, showAddIcon));
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (!string.IsNullOrWhiteSpace(footer))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = footer,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12
                });
            }

            return panel;
        }

        private FrameworkElement CreateThumbnail(string? thumbnailPath, double width, double height, bool showAddIcon)
        {
            var grid = new Grid
            {
                Width = width,
                Height = height
            };

            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1)
            });

            if (showAddIcon)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = "+",
                    FontSize = 84,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                return grid;
            }

            var image = new Image
            {
                Stretch = Stretch.Uniform
            };

            if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
            {
                image.Source = new BitmapImage(new Uri(DefaultThumbnailUri));
            }
            else
            {
                _ = LoadThumbnailFromFileAsync(image, thumbnailPath);
            }

            grid.Children.Add(image);
            return grid;
        }

        private static async Task LoadThumbnailFromFileAsync(Image image, string thumbnailPath)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(thumbnailPath);
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                image.Source = bitmap;
            }
            catch
            {
                image.Source = new BitmapImage(new Uri(DefaultThumbnailUri));
            }
        }

        private async Task LoadStoryPreviewImageAsync(Image image, string imagePath)
        {
            try
            {
                image.Source = await GetCachedStoryPreviewImageAsync(imagePath);
            }
            catch
            {
                image.Source = new BitmapImage(new Uri(DefaultThumbnailUri));
            }
        }

        private async Task<BitmapImage> GetCachedStoryPreviewImageAsync(string imagePath)
        {
            if (_storyPreviewImageCache.TryGetValue(imagePath, out var cached))
            {
                return cached;
            }

            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            _storyPreviewImageCache[imagePath] = bitmap;
            return bitmap;
        }

        private void AddProjectCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ShowCreateProjectPage();
            AppendLog(LogKind.User, "打开创建项目页面。");
            e.Handled = true;
        }

        private void AddAssetLibraryCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ShowCreateAssetLibraryPage();
            AppendLog(LogKind.User, "打开创建素材库页面。");
            e.Handled = true;
        }

        private void ProjectCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is GridViewItem { Tag: ProjectInfo project })
            {
                TouchProjectLastEditedAt(project);
                ShowProjectDetailPage(ReadProjectInfo(project.Path));
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"打开项目：{project.Name}");
                e.Handled = true;
            }
        }

        private void AssetLibraryCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is GridViewItem { Tag: AssetLibraryInfo assetLibrary })
            {
                TouchAssetLibraryLastEditedAt(assetLibrary);
                ShowAssetLibraryDetailPage(assetLibrary);
                AppendLog(LogKind.User, $"打开素材库：{assetLibrary.Name}");
                e.Handled = true;
            }
        }

        private void ShowProjectDetailPage(ProjectInfo project)
        {
            StopStoryEditorAudio();
            _currentProject = project;
            Directory.CreateDirectory(GetChaptersFolderPath(project));
            ProjectDetailTabTitleText.Text = $"{project.Name} / {project.Code}";
            ProjectDetailInfoText.Text = CreateProjectDetailInfoText(project);
            ProjectDetailNameTextBox.Text = project.Name;
            ProjectDetailCodeTextBox.Text = project.Code;
            LoadChapters(project);

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Visible;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            ProjectDetailCloseButton.Focus(FocusState.Programmatic);
        }

        private static string CreateProjectDetailInfoText(ProjectInfo project)
        {
            return $"项目名字：{project.Name} | 英文代号：{project.Code} | 关联素材库：{project.AssetLibraryName} | 上次打开时间：{project.LastEditedAt:yyyy-MM-dd HH:mm}";
        }

        private void CloseProjectDetailButton_Click(object sender, RoutedEventArgs e)
        {
            ShowWorkbenchPage();
        }

        private async void SaveProjectInlineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

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
                ShowProjectDetailPage(ReadProjectInfo(_currentProject.Path));
            }
        }

        private void LoadChapters(ProjectInfo project)
        {
            List<ChapterInfo> chapters;
            try
            {
                var chaptersFolderPath = GetChaptersFolderPath(project);
                Directory.CreateDirectory(chaptersFolderPath);
                chapters = Directory
                    .EnumerateDirectories(chaptersFolderPath)
                    .Select(ReadChapterInfo)
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

        private GridViewItem CreateChapterCard(ChapterInfo chapter)
        {
            var item = CreateBaseCard(chapter);
            var flyout = new MenuFlyout();
            var editItem = new MenuFlyoutItem
            {
                Text = "修改"
            };
            editItem.Click += async (_, _) => await EditChapterFromUiAsync(chapter);
            flyout.Items.Add(editItem);

            var importSectionItem = new MenuFlyoutItem
            {
                Text = "导入小节"
            };
            importSectionItem.Click += async (_, _) => await ImportStorySectionsFromUiAsync(chapter);
            flyout.Items.Add(importSectionItem);

            var backupItem = new MenuFlyoutItem
            {
                Text = "备份"
            };
            backupItem.Click += async (_, _) => await BackupChapterFromUiAsync(chapter);
            flyout.Items.Add(backupItem);

            var restoreItem = new MenuFlyoutItem
            {
                Text = "还原"
            };
            restoreItem.Click += async (_, _) => await RestoreChapterFromUiAsync(chapter);
            flyout.Items.Add(restoreItem);

            var repairItem = new MenuFlyoutItem
            {
                Text = "修复"
            };
            repairItem.Click += async (_, _) => await RepairChapterIndexesFromUiAsync(chapter);
            flyout.Items.Add(repairItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除"
            };
            deleteItem.Click += async (_, _) => await DeleteChapterFromUiAsync(chapter);
            flyout.Items.Add(deleteItem);
            item.ContextFlyout = flyout;
            item.Tapped += (_, _) => OpenStoryEditorFromUi(chapter);

            var typeName = ChapterTypeOptions.FirstOrDefault(option => option.Kind == chapter.Type)?.DisplayName ?? chapter.Type;
            item.Content = CreateCardContent(null, chapter.Name, $"{typeName} | {chapter.Code}", $"上次编辑时间 {chapter.LastEditedAt:yyyy-MM-dd HH:mm}");
            return item;
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

            var chapterFolderName = SanitizeCharacterFolderName(input.Code);
            var chapterPath = Path.Combine(GetChaptersFolderPath(_currentProject), chapterFolderName);
            if (Directory.Exists(chapterPath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法创建章节", $"同名英文代号已存在：{input.Code}");
                AppendLog(LogKind.Warning, $"无法创建章节，同名英文代号已存在：{input.Code}");
                return;
            }

            Directory.CreateDirectory(chapterPath);
            WriteChapterMeta(chapterPath, input);
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

            var targetPath = Path.Combine(GetChaptersFolderPath(_currentProject), SanitizeCharacterFolderName(input.Code));
            if (!PathsEqual(chapter.Path, targetPath) && Directory.Exists(targetPath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法修改章节", $"同名英文代号已存在：{input.Code}");
                AppendLog(LogKind.Warning, $"无法修改章节，同名英文代号已存在：{input.Code}");
                return;
            }

            if (!PathsEqual(chapter.Path, targetPath))
            {
                Directory.Move(chapter.Path, targetPath);
            }

            WriteChapterMeta(targetPath, input);
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

            Directory.Delete(chapter.Path, recursive: true);
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
                    progress => Task.Run(() => CreateFolderBackup(chapter.Path, ChapterBackupsFolderName, chapter.Code, note, progress)));
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

                var backups = GetFolderBackups(chapter.Path, ChapterBackupsFolderName);
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

                RestoreFolderBackup(chapter.Path, ChapterBackupsFolderName, selectedBackup);
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
                    progress => Task.Run(() => ScanChapterIndexIssues(project, chapter, assetLibrary, repair: false, progress)));
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
                    progress => Task.Run(() => ScanChapterIndexIssues(project, chapter, assetLibrary, repair: true, progress)));
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

        private ChapterRepairResult ScanChapterIndexIssues(
            ProjectInfo project,
            ChapterInfo chapter,
            AssetLibraryInfo assetLibrary,
            bool repair,
            IProgress<ChapterRepairProgress>? progress)
        {
            var csvFiles = GetLocalStorySectionCsvPaths(chapter)
                .Where(file => File.Exists(file.Path))
                .OrderBy(file => file.Section)
                .ToList();
            var issues = new List<ChapterRepairIssue>();
            var changedCsvPaths = new List<string>();
            var fixedCount = 0;
            var context = BuildChapterRepairAssetContext(assetLibrary);

            for (var csvIndex = 0; csvIndex < csvFiles.Count; csvIndex++)
            {
                var csvFile = csvFiles[csvIndex];
                progress?.Report(new ChapterRepairProgress(
                    $"正在检查第 {csvFile.Section} 小节 CSV",
                    csvFiles.Count == 0 ? 100 : csvIndex * 90d / csvFiles.Count,
                    csvIndex,
                    csvFiles.Count,
                    issues.Count,
                    fixedCount,
                    Path.GetFileName(csvFile.Path)));

                var rows = ReadStoryRows(csvFile.Path);
                var changed = false;
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];
                    CheckRepairIndex(row, project, chapter, csvFile.Path, rowIndex, "BGindex", "背景图", context.BackgroundCount, repair, issues, ref changed, ref fixedCount);
                    CheckRepairIndex(row, project, chapter, csvFile.Path, rowIndex, "BGM", "BGM", context.BgmCount, repair, issues, ref changed, ref fixedCount);
                    CheckRepairIndex(row, project, chapter, csvFile.Path, rowIndex, "Scene", "环境音", context.SceneCount, repair, issues, ref changed, ref fixedCount);
                    ValidateRepairCharacterLayer(row, project, chapter, csvFile.Path, rowIndex, "TalkChar", "TalkBody", "TalkFace", "TalkAdorn", "TalkVfx", "说话人", allowRawUnknownCharacter: true, context, repair, issues, ref changed, ref fixedCount);
                    for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
                    {
                        ValidateRepairCharacterLayer(row, project, chapter, csvFile.Path, rowIndex, $"Chara{slotIndex}", $"Body{slotIndex}", $"Face{slotIndex}", $"Adorn{slotIndex}", $"Vfx{slotIndex}", $"{slotIndex}号位", allowRawUnknownCharacter: false, context, repair, issues, ref changed, ref fixedCount);
                    }
                }

                if (changed)
                {
                    WriteStoryRows(csvFile.Path, rows);
                    changedCsvPaths.Add(csvFile.Path);
                }
            }

            progress?.Report(new ChapterRepairProgress("章节索引检查完成。", 100, csvFiles.Count, csvFiles.Count, issues.Count, fixedCount, null));
            return new ChapterRepairResult(project.Name, chapter.Name, chapter.Code, csvFiles.Count, issues.Count, fixedCount, changedCsvPaths, issues);
        }

        private ChapterRepairAssetContext BuildChapterRepairAssetContext(AssetLibraryInfo assetLibrary)
        {
            var characters = GetCharactersForAssetLibrary(assetLibrary);
            var characterAssets = characters.ToDictionary(
                character => character.Code,
                character => new CharacterRepairAssetCounts(
                    GetCharacterLayerImagePaths(Path.Combine(character.Path, "DN_Cloth")).Count,
                    GetCharacterLayerImagePaths(Path.Combine(character.Path, "FC_Face")).Count,
                    GetCharacterLayerImagePaths(Path.Combine(character.Path, "AD_Adorn")).Count),
                StringComparer.OrdinalIgnoreCase);
            var characterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var character in characters)
            {
                characterAliases[character.Code] = character.Code;
                characterAliases[character.Name] = character.Code;
            }

            return new ChapterRepairAssetContext(
                GetBackgroundImagePaths(GetBackgroundFolderPath(assetLibrary)).Count,
                GetMusicFilePaths(GetMusicFolderPath(assetLibrary)).Count,
                GetAudioFilePaths(GetAmbientSoundFolderPath(assetLibrary)).Count,
                ReadCharacterFilters(assetLibrary).Count,
                characterAliases,
                characterAssets);
        }

        private static void ValidateRepairCharacterLayer(
            StoryRow row,
            ProjectInfo project,
            ChapterInfo chapter,
            string csvPath,
            int rowIndex,
            string characterColumn,
            string bodyColumn,
            string faceColumn,
            string adornColumn,
            string vfxColumn,
            string label,
            bool allowRawUnknownCharacter,
            ChapterRepairAssetContext context,
            bool repair,
            List<ChapterRepairIssue> issues,
            ref bool changed,
            ref int fixedCount)
        {
            var characterValue = row.Get(characterColumn);
            if (string.IsNullOrWhiteSpace(characterValue))
            {
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}角色为空，身体索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}角色为空，表情索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}角色为空，装饰索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}角色为空，滤镜索引应归零。", repair, issues, ref changed, ref fixedCount);
                return;
            }

            if (ContainsCjk(characterValue))
            {
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}角色 `{characterValue}` 是中文/显示名，身体索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}角色 `{characterValue}` 是中文/显示名，表情索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}角色 `{characterValue}` 是中文/显示名，装饰索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}角色 `{characterValue}` 是中文/显示名，滤镜索引应归零。", repair, issues, ref changed, ref fixedCount);
                return;
            }

            if (!context.CharacterAliases.TryGetValue(characterValue, out var characterCode) ||
                !context.CharacterAssets.TryGetValue(characterCode, out var counts))
            {
                if (!allowRawUnknownCharacter)
                {
                    issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, characterColumn, $"{label}角色 `{characterValue}` 不在当前素材库角色列表中，未自动改动。", canAutoFix: false));
                }

                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，身体索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，表情索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，装饰索引应归零。", repair, issues, ref changed, ref fixedCount);
                CheckRepairDetachedCharacterLayer(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}角色 `{characterValue}` 没有匹配到立绘卡，滤镜索引应归零。", repair, issues, ref changed, ref fixedCount);
                return;
            }

            CheckRepairIndex(row, project, chapter, csvPath, rowIndex, bodyColumn, $"{label}身体", counts.ClothCount, repair, issues, ref changed, ref fixedCount);
            CheckRepairIndex(row, project, chapter, csvPath, rowIndex, faceColumn, $"{label}表情", counts.FaceCount, repair, issues, ref changed, ref fixedCount);
            CheckRepairAdornIndex(row, project, chapter, csvPath, rowIndex, adornColumn, $"{label}装饰", counts.AdornCount, repair, issues, ref changed, ref fixedCount);
            CheckRepairIndex(row, project, chapter, csvPath, rowIndex, vfxColumn, $"{label}滤镜", context.FilterCount, repair, issues, ref changed, ref fixedCount);
        }

        private static void CheckRepairDetachedCharacterLayer(
            StoryRow row,
            ProjectInfo project,
            ChapterInfo chapter,
            string csvPath,
            int rowIndex,
            string columnName,
            string message,
            bool repair,
            List<ChapterRepairIssue> issues,
            ref bool changed,
            ref int fixedCount)
        {
            var value = ParseInt(row.Get(columnName));
            if (value == 0)
            {
                return;
            }

            issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, columnName, message, canAutoFix: true));
            if (repair)
            {
                row.Set(columnName, "0");
                changed = true;
                fixedCount++;
            }
        }

        private static void CheckRepairIndex(
            StoryRow row,
            ProjectInfo project,
            ChapterInfo chapter,
            string csvPath,
            int rowIndex,
            string columnName,
            string label,
            int assetCount,
            bool repair,
            List<ChapterRepairIssue> issues,
            ref bool changed,
            ref int fixedCount)
        {
            var value = ParseInt(row.Get(columnName));
            if (value >= 0 && value < assetCount)
            {
                return;
            }

            var canAutoFix = assetCount > 0 && value != 0;
            issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, columnName, $"{label}索引 {value} 超出范围；当前可用数量 {assetCount}。", canAutoFix));
            if (repair && canAutoFix)
            {
                row.Set(columnName, "0");
                changed = true;
                fixedCount++;
            }
        }

        private static void CheckRepairAdornIndex(
            StoryRow row,
            ProjectInfo project,
            ChapterInfo chapter,
            string csvPath,
            int rowIndex,
            string columnName,
            string label,
            int assetCount,
            bool repair,
            List<ChapterRepairIssue> issues,
            ref bool changed,
            ref int fixedCount)
        {
            var value = ParseInt(row.Get(columnName));
            if (value == 0 || value > 0 && value <= assetCount)
            {
                return;
            }

            var canAutoFix = value != 0;
            issues.Add(CreateChapterRepairIssue(project, chapter, csvPath, row, rowIndex, columnName, $"{label}索引 {value} 超出范围；0 表示无装饰，当前可用装饰数量 {assetCount}。", canAutoFix));
            if (repair && canAutoFix)
            {
                row.Set(columnName, "0");
                changed = true;
                fixedCount++;
            }
        }

        private static ChapterRepairIssue CreateChapterRepairIssue(ProjectInfo project, ChapterInfo chapter, string csvPath, StoryRow row, int rowIndex, string columnName, string message, bool canAutoFix)
        {
            return new ChapterRepairIssue(
                project.Name,
                chapter.Name,
                chapter.Code,
                Path.GetFileName(csvPath),
                row.Get("Name"),
                rowIndex + 1,
                columnName,
                message,
                canAutoFix);
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

        private async Task<List<string>?> ShowStorySectionImportDialogAsync(ChapterInfo chapter)
        {
            var selectedPaths = new List<string>();
            ContentDialog? dialog = null;

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
                    dialog?.Hide();
                }
            }

            var plusText = new TextBlock
            {
                Text = "+",
                FontSize = 72,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var hintText = new TextBlock
            {
                Text = "点击选择 CSV，或把小节 CSV 拖到这里",
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            };

            var dropZone = new Border
            {
                MinHeight = 260,
                Padding = new Thickness(24),
                Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                AllowDrop = true,
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 8,
                    Children =
                    {
                        plusText,
                        hintText
                    }
                }
            };

            dropZone.Tapped += async (_, _) => await PickCsvFilesAsync();
            dropZone.DragOver += (_, e) =>
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    e.AcceptedOperation = DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = "导入小节 CSV";
                    e.DragUIOverride.IsCaptionVisible = true;
                }
            };
            dropZone.Drop += async (_, e) =>
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    return;
                }

                var items = await e.DataView.GetStorageItemsAsync();
                selectedPaths = items
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (selectedPaths.Count > 0)
                {
                    dialog?.Hide();
                }
            };

            dialog = new ContentDialog
            {
                Title = $"导入小节：{chapter.Name}",
                Content = dropZone,
                CloseButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };

            await dialog.ShowAsync();
            return selectedPaths.Count > 0 ? selectedPaths : null;
        }

        private void ChaptersGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "导入章节 CSV";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        }

        private async void ChaptersGridView_Drop(object sender, DragEventArgs e)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导入 CSV", "请先打开一个项目。");
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var csvPaths = items
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (csvPaths.Count == 0)
                {
                    ShowChapterStatus(InfoBarSeverity.Warning, "没有可导入的 CSV", "请把 .csv 文件拖到章节区域。");
                    return;
                }

                var importedCount = 0;
                foreach (var csvPath in csvPaths)
                {
                    importedCount += await ImportChapterCsvAsync(csvPath) ? 1 : 0;
                }

                LoadChapters(_currentProject);
                ShowChapterStatus(InfoBarSeverity.Success, "CSV 已导入", $"已导入 {importedCount} 个章节。导入表会保留原始索引，打开后可逐句检查素材对应关系。");
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "导入 CSV 失败", ex.Message);
                AppendLog(LogKind.Error, "拖入导入章节 CSV 失败。", ex);
            }
        }

        private async Task<bool> ImportChapterCsvAsync(string sourceCsvPath)
        {
            if (_currentProject is null || !File.Exists(sourceCsvPath))
            {
                return false;
            }

            var compatibility = InspectStoryCsvCompatibility(sourceCsvPath);
            if (!compatibility.IsCompatible)
            {
                await ShowCsvCompatibilityFailedDialogAsync(sourceCsvPath, compatibility);
                return false;
            }

            var suggestedCode = GetUniqueImportedChapterCode(Path.GetFileNameWithoutExtension(sourceCsvPath));
            var importInput = await ShowImportChapterCsvDialogAsync(sourceCsvPath, compatibility, suggestedCode);
            if (importInput is null)
            {
                return false;
            }

            var rows = ReadStoryRows(sourceCsvPath);
            if (rows.Count == 0)
            {
                rows.Add(CreateDefaultStoryRow());
            }

            var chapterCode = importInput.Code;
            var chapterPath = Path.Combine(GetChaptersFolderPath(_currentProject), SanitizeCharacterFolderName(chapterCode));
            if (Directory.Exists(chapterPath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导入 CSV", $"同名章节代号已存在：{chapterCode}");
                return false;
            }

            Directory.CreateDirectory(chapterPath);
            WriteChapterMeta(chapterPath, importInput);

            var targetCsvPath = Path.Combine(chapterPath, $"{chapterCode}.csv");
            WriteStoryRows(targetCsvPath, rows);

            var sections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                sections[row.Get("Name")] = 1;
            }

            WriteStorySectionState(chapterPath, sections);
            AppendLog(LogKind.User, $"导入章节 CSV：{sourceCsvPath} -> {targetCsvPath}");
            return true;
        }

        private async Task<ChapterEditorInput?> ShowImportChapterCsvDialogAsync(
            string sourceCsvPath,
            StoryCsvCompatibility compatibility,
            string suggestedCode)
        {
            if (_currentProject is null)
            {
                return null;
            }

            var suggestedName = Path.GetFileNameWithoutExtension(sourceCsvPath);
            if (suggestedName.EndsWith(".story", StringComparison.OrdinalIgnoreCase))
            {
                suggestedName = suggestedName[..^".story".Length];
            }

            var initialChapter = new ChapterInfo(
                suggestedName,
                suggestedCode,
                ChapterKind.MainThread,
                string.Empty,
                DateTime.Now,
                0);

            return await ShowChapterEditorDialogAsync(
                "导入章节 CSV",
                initialChapter,
                CreateStoryCsvCompatibilityPanel(sourceCsvPath, compatibility));
        }

        private async Task ShowCsvCompatibilityFailedDialogAsync(string sourceCsvPath, StoryCsvCompatibility compatibility)
        {
            var dialog = new ContentDialog
            {
                Title = "CSV 结构不兼容",
                Content = CreateStoryCsvCompatibilityPanel(sourceCsvPath, compatibility),
                CloseButtonText = "知道了",
                XamlRoot = Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private FrameworkElement CreateStoryCsvCompatibilityPanel(string sourceCsvPath, StoryCsvCompatibility compatibility)
        {
            var panel = new StackPanel
            {
                Spacing = 8,
                MaxWidth = 520
            };

            panel.Children.Add(new TextBlock
            {
                Text = $"文件：{Path.GetFileName(sourceCsvPath)}",
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = compatibility.IsCompatible ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                Title = compatibility.IsCompatible ? "结构体兼容" : "结构体不兼容",
                Message = compatibility.IsCompatible
                    ? "CSV 表头可以映射到当前 FStoryStruct。额外列会在导入副本中忽略。"
                    : "CSV 表头缺少剧情表必要字段，已取消导入。"
            });

            if (compatibility.MissingColumns.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"缺少字段：{string.Join(", ", compatibility.MissingColumns)}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Firebrick)
                });
            }

            if (compatibility.ExtraColumns.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"额外字段：{string.Join(", ", compatibility.ExtraColumns.Take(12))}{(compatibility.ExtraColumns.Count > 12 ? " ..." : string.Empty)}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkGoldenrod)
                });
            }

            return panel;
        }

        private static StoryCsvCompatibility InspectStoryCsvCompatibility(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                return new StoryCsvCompatibility(false, StoryCsvColumns.ToList(), []);
            }

            var firstLine = File.ReadLines(csvPath, Encoding.UTF8).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return new StoryCsvCompatibility(false, StoryCsvColumns.ToList(), []);
            }

            var headers = NormalizeStoryCsvHeaders(ParseCsvLine(firstLine));
            var headerSet = headers.ToHashSet(StringComparer.Ordinal);
            var expectedSet = StoryCsvColumns.ToHashSet(StringComparer.Ordinal);
            var missing = StoryCsvColumns.Where(column => !headerSet.Contains(column)).ToList();
            var extra = headers.Where(column => !expectedSet.Contains(column)).ToList();
            return new StoryCsvCompatibility(missing.Count == 0, missing, extra);
        }

        private string GetUniqueImportedChapterCode(string sourceName)
        {
            var code = SanitizeChapterCodeSegment(sourceName);
            if (code.EndsWith(".story", StringComparison.OrdinalIgnoreCase))
            {
                code = code[..^".story".Length];
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                code = $"Imported-{DateTime.Now:yyyyMMddHHmmss}";
            }

            var existingCodes = GetExistingChapterCodes().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var chaptersPath = GetChaptersFolderPath(_currentProject!);
            var uniqueCode = code;
            var suffix = 1;
            while (existingCodes.Contains(uniqueCode) ||
                Directory.Exists(Path.Combine(chaptersPath, SanitizeCharacterFolderName(uniqueCode))))
            {
                uniqueCode = $"{code}-Import{suffix}";
                suffix++;
            }

            return uniqueCode;
        }

        private void OpenStoryEditorFromUi(ChapterInfo chapter)
        {
            ChapterInfoBar.IsOpen = false;
            try
            {
                ShowStoryEditorPage(chapter);
            }
            catch (Exception ex)
            {
                ShowChapterStatus(InfoBarSeverity.Error, "打开章节失败", ex.Message);
                AppendLog(LogKind.Error, "打开章节编辑器失败。", ex);
            }
        }

        private void ShowStoryEditorPage(ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法打开章节", "请先打开一个项目。");
                return;
            }

            SaveCurrentStoryRow();
            _storyEditSaveTimer?.Stop();
            _currentStoryChapter = chapter;
            _currentStoryAssetLibrary = ResolveProjectAssetLibrary(_currentProject);
            _currentStoryCsvPath = GetChapterStoryCsvPath(chapter);
            ClearStoryUndoStack();
            UpdateStoryDebugModeUi();
            _storyBackgroundPreviewKey = null;
            _storyCharacterPreviewKeys.Clear();
            _storyBgmPlaybackSuppressed = false;
            ApplyStoryTextFontSizeToUi();
            UpdateStoryEditorHeader();
            StoryEditorCsvPathText.Text = _currentStoryCsvPath;
            StoryAssetStatusText.Text = _currentStoryAssetLibrary is null
                ? "当前项目未关联素材库。"
                : $"素材库：{_currentStoryAssetLibrary.Name}";

            LoadStoryRowsFromSectionFiles(chapter);
            NormalizeStoryCharacterCodes();
            SynchronizeStorySectionState();
            PersistCurrentStoryRowsToFiles();

            _currentStoryRowIndex = Math.Clamp(chapter.LastEditedRowIndex, 0, _storyRows.Count - 1);
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            UpdateStoryCharacterSlotLayout();
            LoadStoryRowIntoUi();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Visible;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            StoryEditorBackButton.Focus(FocusState.Programmatic);
            StoryEditorPage.Focus(FocusState.Programmatic);
            _ = WarmStoryPreviewImageCacheAsync();
            AppendLog(LogKind.User, $"打开章节编辑器：{chapter.Name}（{chapter.Code}）");
        }

        private void CloseStoryEditorButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentStoryRow();
            SaveCurrentChapterProgress();
            _storyEditSaveTimer?.Stop();
            StopStoryEditorAudio();
            if (_currentProject is not null)
            {
                ShowProjectDetailPage(_currentProject);
            }
            else
            {
                ShowWorkbenchPage();
            }
        }

        private void UndoStoryEditorButton_Click(object sender, RoutedEventArgs e)
        {
            UndoStoryEditorOperation();
        }

        private void StoryDebugModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _isStoryDebugModeEnabled = StoryDebugModeCheckBox?.IsChecked == true;
            UpdateStoryDebugModeUi();
            AppendLog(LogKind.User, _isStoryDebugModeEnabled ? "故事编辑器调试模式已开启。" : "故事编辑器调试模式已关闭。");
        }

        private void UpdateStoryDebugModeUi()
        {
            if (InsertStoryRowHereButton is not null)
            {
                InsertStoryRowHereButton.Visibility = _isStoryDebugModeEnabled ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private StoryEditorUndoState? CaptureStoryUndoState(string description)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return null;
            }

            var state = new StoryEditorUndoState(
                CloneStoryRows(_storyRows),
                new Dictionary<string, int>(_storyRowSections, StringComparer.OrdinalIgnoreCase),
                Math.Clamp(_currentStoryRowIndex, 0, _storyRows.Count - 1),
                description,
                _currentStoryChapter is null ? null : CloneStoryChoiceNoteState(ReadStoryChoiceNoteState(_currentStoryChapter)));
            _storyUndoStack.Add(state);
            if (_storyUndoStack.Count > MaxStoryUndoCount)
            {
                _storyUndoStack.RemoveAt(0);
            }

            UpdateStoryUndoState();
            AppendLog(LogKind.User, $"编辑器操作：{description}");
            return state;
        }

        private void ClearStoryUndoStack()
        {
            _storyUndoStack.Clear();
            UpdateStoryUndoState();
        }

        private void UpdateStoryUndoState()
        {
            if (StoryUndoButton is not null)
            {
                StoryUndoButton.IsEnabled = _storyUndoStack.Count > 0;
            }
        }

        private void UndoStoryEditorOperation()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (_storyUndoStack.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有可撤回操作", "当前章节编辑器还没有可撤回的操作。");
                return;
            }

            _storyEditSaveTimer?.Stop();
            var state = _storyUndoStack[^1];
            _storyUndoStack.RemoveAt(_storyUndoStack.Count - 1);
            _storyRows.Clear();
            _storyRows.AddRange(CloneStoryRows(state.Rows));
            _storyRowSections.Clear();
            foreach (var pair in state.Sections)
            {
                _storyRowSections[pair.Key] = Math.Max(1, pair.Value);
            }

            _currentStoryRowIndex = Math.Clamp(state.RowIndex, 0, Math.Max(0, _storyRows.Count - 1));
            _isStoryRowDirty = false;
            if (_currentStoryChapter is not null && state.ChoiceNotes is not null)
            {
                WriteStoryChoiceNoteState(_currentStoryChapter, CloneStoryChoiceNoteState(state.ChoiceNotes));
                RemoveUnusedStoryChoiceNotes(state.ChoiceNotes.Choices.Keys);
            }

            ApplyStorySectionsInRowOrder(GetStorySectionsInRowOrder());
            PersistCurrentStoryRowsToFiles();
            SaveCurrentChapterProgress();
            ClearStoryFunctionTips();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            UpdateStoryUndoState();
            ShowStoryStatus(InfoBarSeverity.Success, "已撤回", state.Description);
            AppendLog(LogKind.User, $"撤回编辑器操作：{state.Description}");
        }

        private static List<StoryRow> CloneStoryRows(IEnumerable<StoryRow> rows)
        {
            return rows.Select(row => row.Clone()).ToList();
        }

        private static StoryChoiceNoteState CloneStoryChoiceNoteState(StoryChoiceNoteState state)
        {
            var clone = new StoryChoiceNoteState();
            foreach (var pair in state.Choices)
            {
                clone.Choices[pair.Key] = pair.Value.ToList();
            }

            return clone;
        }

        private void LoadStoryRowIntoUi()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            ClearStoryFunctionTips();
            _isLoadingStoryRow = true;
            try
            {
                var row = _storyRows[_currentStoryRowIndex];
                StorySpeakerTextBox.Text = row.Get("TalkChar");
                StoryTextTextBox.Text = row.Get("Tesxt");
                UpdateStoryRowIndexInput();
                var section = GetCurrentStorySection();
                EnsureStorySectionOptions(section);
                SelectStorySection(section);
                NormalizeCurrentStoryChoiceFunctionPrefix();
                UpdateStoryEditorHeader();
                UpdateStoryToolbarCurrentInfo();
                _isStoryRowDirty = false;
            }
            finally
            {
                _isLoadingStoryRow = false;
            }

            _ = RefreshStoryPreviewAsync();
            _ = ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private bool SaveCurrentStoryRow()
        {
            if (_isLoadingStoryRow || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return false;
            }

            var sectionChanged = SetCurrentStorySectionIfChanged(GetSelectedStorySection());
            if (!_isStoryRowDirty && !sectionChanged)
            {
                return false;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var changed = false;
            var speakerName = NormalizeStoryCharacterNameForCsv(StorySpeakerTextBox.Text);
            if (!string.Equals(row.Get("TalkChar"), speakerName, StringComparison.Ordinal))
            {
                row.Set("TalkChar", speakerName);
                changed = true;
            }

            if (!string.Equals(row.Get("Tesxt"), StoryTextTextBox.Text, StringComparison.Ordinal))
            {
                row.Set("Tesxt", StoryTextTextBox.Text);
                changed = true;
            }

            changed |= NormalizeStoryDetachedCharacterLayers(row);

            if (changed)
            {
                PersistCurrentStoryRowsToFiles();
                _isStoryRowDirty = false;
                SaveCurrentChapterProgress();
                _ = RefreshStoryPreviewAsync();
            }

            if (sectionChanged)
            {
                PersistCurrentStoryRowsToFiles();
                SaveCurrentChapterProgress();
            }

            _isStoryRowDirty = false;
            return changed || sectionChanged;
        }

        private void ScheduleStoryRowSave()
        {
            if (_isLoadingStoryRow)
            {
                return;
            }

            if (_storyEditSaveTimer is null)
            {
                return;
            }

            if (!_isStoryRowDirty)
            {
                CaptureStoryUndoState("编辑当前句文本");
            }

            _isStoryRowDirty = true;
            _storyEditSaveTimer.Stop();
            _storyEditSaveTimer.Start();
        }

        private void StoryEditSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            SaveCurrentStoryRow();
        }

        private void StorySpeakerTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleStoryRowSave();
        }

        private void StoryTextTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleStoryRowSave();
        }

        private void UpdateStoryRowIndexInput()
        {
            _isUpdatingStoryRowIndexText = true;
            try
            {
                if (_appSettings.ShowFullStoryChapterLength)
                {
                    StoryRowIndexTextBox.Text = (_currentStoryRowIndex + 1).ToString();
                    StoryRowTotalText.Text = $"/ {_storyRows.Count} 句";
                }
                else
                {
                    var sectionInfo = GetCurrentStorySectionPositionInfo();
                    StoryRowIndexTextBox.Text = sectionInfo.LocalIndex.ToString();
                    StoryRowTotalText.Text = $"/ {sectionInfo.Total} 句";
                }
            }
            finally
            {
                _isUpdatingStoryRowIndexText = false;
            }
        }

        private (int LocalIndex, int Total) GetCurrentStorySectionPositionInfo()
        {
            if (_storyRows.Count == 0)
            {
                return (1, 1);
            }

            var currentSection = GetCurrentStorySection();
            var total = 0;
            var localIndex = 1;
            for (var i = 0; i < _storyRows.Count; i++)
            {
                if (GetStorySectionAtRowIndex(i) != currentSection)
                {
                    continue;
                }

                total++;
                if (i == _currentStoryRowIndex)
                {
                    localIndex = total;
                }
            }

            return (Math.Max(1, localIndex), Math.Max(1, total));
        }

        private void SaveCurrentChapterProgress()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            var metaPath = Path.Combine(_currentStoryChapter.Path, ChapterMetaFileName);
            var meta = ReadJson<ChapterMeta>(metaPath) ?? new ChapterMeta();
            meta.ChapterName = string.IsNullOrWhiteSpace(meta.ChapterName) ? _currentStoryChapter.Name : meta.ChapterName;
            meta.ChapterCode = string.IsNullOrWhiteSpace(meta.ChapterCode) ? _currentStoryChapter.Code : meta.ChapterCode;
            meta.ChapterType = string.IsNullOrWhiteSpace(meta.ChapterType) ? _currentStoryChapter.Type : meta.ChapterType;
            meta.LastEditedAt = DateTime.Now;
            meta.LastEditedRowIndex = Math.Max(0, _currentStoryRowIndex);
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private void StoryRowIndexTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitStoryRowIndexJump();
                e.Handled = true;
            }
        }

        private void StoryRowIndexTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitStoryRowIndexJump();
        }

        private void CommitStoryRowIndexJump()
        {
            if (_isUpdatingStoryRowIndexText || _storyRows.Count == 0)
            {
                return;
            }

            if (!int.TryParse(StoryRowIndexTextBox.Text.Trim(), out var rowNumber))
            {
                UpdateStoryRowIndexInput();
                return;
            }

            if (_appSettings.ShowFullStoryChapterLength)
            {
                NavigateToStoryRow(Math.Clamp(rowNumber, 1, _storyRows.Count) - 1, rebuildPersistentState: true);
                return;
            }

            var currentSection = GetCurrentStorySection();
            var sectionIndexes = Enumerable.Range(0, _storyRows.Count)
                .Where(index => GetStorySectionAtRowIndex(index) == currentSection)
                .ToList();
            if (sectionIndexes.Count == 0)
            {
                UpdateStoryRowIndexInput();
                return;
            }

            var targetLocalIndex = Math.Clamp(rowNumber, 1, sectionIndexes.Count) - 1;
            NavigateToStoryRow(sectionIndexes[targetLocalIndex], rebuildPersistentState: true);
        }

        private void NavigateToStoryRow(int targetIndex, bool rebuildPersistentState)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            targetIndex = Math.Clamp(targetIndex, 0, _storyRows.Count - 1);
            SaveCurrentStoryRow();
            if (targetIndex == _currentStoryRowIndex)
            {
                UpdateStoryRowIndexInput();
                return;
            }

            _currentStoryRowIndex = targetIndex;
            SaveCurrentChapterProgress();
            if (rebuildPersistentState)
            {
                RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            }

            LoadStoryRowIntoUi();
        }

        private void PreviousStoryRowButton_Click(object sender, RoutedEventArgs e)
        {
            NavigatePreviousStoryRow();
        }

        private void NavigatePreviousStoryRow()
        {
            if (_storyRows.Count == 0 || _currentStoryRowIndex <= 0)
            {
                return;
            }

            NavigateToStoryRow(_currentStoryRowIndex - 1, rebuildPersistentState: true);
        }

        private void NextStoryRowButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateNextStoryRow();
        }

        private void InsertStoryRowHereButton_Click(object sender, RoutedEventArgs e)
        {
            InsertStoryRowHere();
        }

        private void PreviousStorySectionButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateStorySection(-1);
        }

        private void NextStorySectionButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateStorySection(1);
        }

        private void NavigateStorySection(int direction)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var orderedSections = Enumerable.Range(0, _storyRows.Count)
                .Select(index => new
                {
                    Section = GetStorySectionAtRowIndex(index),
                    FirstRowIndex = index
                })
                .GroupBy(item => item.Section)
                .Select(group => group.First())
                .OrderBy(item => item.FirstRowIndex)
                .ToList();
            if (orderedSections.Count <= 1)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有其他小节", "当前章节只有一个小节。");
                return;
            }

            var currentSection = GetCurrentStorySection();
            var currentSectionIndex = orderedSections.FindIndex(item => item.Section == currentSection);
            if (currentSectionIndex < 0)
            {
                currentSectionIndex = 0;
            }

            var targetSectionIndex = currentSectionIndex + Math.Sign(direction);
            if (targetSectionIndex < 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已经是第一节", "没有更前面的小节。");
                return;
            }

            if (targetSectionIndex >= orderedSections.Count)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已经是最后一节", "没有更后面的小节。");
                return;
            }

            NavigateToStoryRow(orderedSections[targetSectionIndex].FirstRowIndex, rebuildPersistentState: true);
        }

        private void NavigateNextStoryRow()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            SaveCurrentStoryRow();
            var sections = GetStorySectionsInRowOrder();
            var copiedSection = _currentStoryRowIndex < sections.Count ? sections[_currentStoryRowIndex] : 1;
            var createdRow = false;
            if (_currentStoryRowIndex >= _storyRows.Count - 1)
            {
                if (_isStoryDebugModeEnabled)
                {
                    ShowStoryStatus(InfoBarSeverity.Informational, "调试模式", "已经是最后一句，调试模式下不会新建句子。");
                    return;
                }

                CaptureStoryUndoState("新建下一句");
                var nextRow = _storyRows[_currentStoryRowIndex].Clone();
                nextRow.Set("Name", CreateStoryRowName(_storyRows.Count));
                nextRow.Set("Tesxt", string.Empty);
                nextRow.Set("Custom", string.Empty);
                _storyRows.Add(nextRow);
                sections.Add(copiedSection);
                createdRow = true;
            }

            _currentStoryRowIndex++;
            SaveCurrentChapterProgress();
            if (createdRow)
            {
                ApplyStorySectionsInRowOrder(sections);
                PersistCurrentStoryRowsToFiles();
            }

            LoadStoryRowIntoUi();
        }

        private void InsertStoryRowHere()
        {
            if (_storyRows.Count == 0 || _currentStoryCsvPath is null)
            {
                return;
            }

            if (_isStoryDebugModeEnabled)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "调试模式", "调试模式下已隐藏原地新建。");
                return;
            }

            SaveCurrentStoryRow();
            CaptureStoryUndoState("原地新建一句");
            var sections = GetStorySectionsInRowOrder();
            var copiedSection = _currentStoryRowIndex < sections.Count ? sections[_currentStoryRowIndex] : GetCurrentStorySection();
            var insertedRow = _storyRows[_currentStoryRowIndex].Clone();
            insertedRow.Set("Tesxt", string.Empty);
            insertedRow.Set("Custom", string.Empty);

            _storyRows.Insert(_currentStoryRowIndex, insertedRow);
            sections.Insert(_currentStoryRowIndex, copiedSection);
            for (var i = 0; i < _storyRows.Count; i++)
            {
                _storyRows[i].Set("Name", CreateStoryRowName(i));
            }

            ApplyStorySectionsInRowOrder(sections);
            PersistCurrentStoryRowsToFiles();
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            ShowStoryStatus(InfoBarSeverity.Success, "已原地新建", "新句子已插入当前位置，后面的剧情已顺延。");
        }

        private void StoryEditorPage_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (StoryEditorPage.Visibility != Visibility.Visible)
            {
                return;
            }

            var properties = e.GetCurrentPoint(StoryEditorPage).Properties;
            if (properties.IsXButton1Pressed)
            {
                NavigateNextStoryRow();
                e.Handled = true;
            }
            else if (properties.IsXButton2Pressed)
            {
                NavigatePreviousStoryRow();
                e.Handled = true;
            }
        }

        private void DeleteStoryRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_storyRows.Count == 0 || _currentStoryCsvPath is null)
            {
                return;
            }

            var sections = GetStorySectionsInRowOrder();
            var removedChoices = GetCurrentStoryChoiceValues();
            CaptureStoryUndoState("删除当前句");
            if (_storyRows.Count == 1)
            {
                _storyRows[0] = CreateDefaultStoryRow();
                _currentStoryRowIndex = 0;
                _storyRowSections.Clear();
                sections = [1];
            }
            else
            {
                _storyRowSections.Remove(_storyRows[_currentStoryRowIndex].Get("Name"));
                if (_currentStoryRowIndex < sections.Count)
                {
                    sections.RemoveAt(_currentStoryRowIndex);
                }

                _storyRows.RemoveAt(_currentStoryRowIndex);
                _currentStoryRowIndex = Math.Min(_currentStoryRowIndex, _storyRows.Count - 1);
            }

            ApplyStorySectionsInRowOrder(sections);
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes(removedChoices);
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
        }

        private void StorySectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingStoryRow || _isUpdatingStorySectionOptions || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var selectedSection = GetSelectedStorySection();
            if (selectedSection != GetCurrentStorySection())
            {
                CaptureStoryUndoState("调整当前句小节");
            }

            if (SetCurrentStorySectionIfChanged(selectedSection))
            {
                NormalizeCurrentStoryChoiceFunctionPrefix();
                PersistCurrentStoryRowsToFiles();
                UpdateStoryRowIndexInput();
                UpdateStoryEditorHeader();
                UpdateStoryToolbarCurrentInfo();
            }
        }

        private void EnsureStorySectionOptions(int requiredSection)
        {
            var maxSection = Math.Max(1, Math.Max(requiredSection, _storyRowSections.Values.DefaultIfEmpty(1).Max()));
            if (StorySectionComboBox.Items.Count == maxSection)
            {
                return;
            }

            _isUpdatingStorySectionOptions = true;
            try
            {
                StorySectionComboBox.Items.Clear();
                for (var i = 1; i <= maxSection; i++)
                {
                    StorySectionComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = $"第 {i} 小节",
                        Tag = i
                    });
                }
            }
            finally
            {
                _isUpdatingStorySectionOptions = false;
            }
        }

        private void AddStorySectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var nextSection = Math.Max(StorySectionComboBox.Items.Count, _storyRowSections.Values.DefaultIfEmpty(1).Max()) + 1;
            CaptureStoryUndoState("新增小节");
            EnsureStorySectionOptions(nextSection);
            _isUpdatingStorySectionOptions = true;
            try
            {
                SelectStorySection(nextSection);
            }
            finally
            {
                _isUpdatingStorySectionOptions = false;
            }

            SetCurrentStorySection(nextSection);
            NormalizeCurrentStoryChoiceFunctionPrefix();
            PersistCurrentStoryRowsToFiles();
            UpdateStoryRowIndexInput();
            UpdateStoryEditorHeader();
            UpdateStoryToolbarCurrentInfo();
        }

        private void SelectStorySection(int section)
        {
            foreach (var item in StorySectionComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is int value && value == section)
                {
                    StorySectionComboBox.SelectedItem = item;
                    return;
                }
            }

            StorySectionComboBox.SelectedIndex = 0;
        }

        private int GetSelectedStorySection()
        {
            return StorySectionComboBox.SelectedItem is ComboBoxItem { Tag: int section }
                ? Math.Max(1, section)
                : 1;
        }

        private int GetCurrentStorySection()
        {
            if (_storyRows.Count == 0)
            {
                return 1;
            }

            return GetStorySectionAtRowIndex(_currentStoryRowIndex);
        }

        private int GetStorySectionAtRowIndex(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _storyRows.Count)
            {
                return 1;
            }

            var rowName = _storyRows[rowIndex].Get("Name");
            return _storyRowSections.TryGetValue(rowName, out var section) ? Math.Max(1, section) : 1;
        }

        private int GetPreviousStorySection(int rowIndex)
        {
            if (rowIndex <= 0 || rowIndex > _storyRows.Count - 1)
            {
                return 1;
            }

            var rowName = _storyRows[rowIndex - 1].Get("Name");
            return _storyRowSections.TryGetValue(rowName, out var section) ? Math.Max(1, section) : 1;
        }

        private void SetCurrentStorySection(int section)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            _storyRowSections[_storyRows[_currentStoryRowIndex].Get("Name")] = Math.Max(1, section);
            EnsureStorySectionOptions(section);
        }

        private bool SetCurrentStorySectionIfChanged(int section)
        {
            if (_storyRows.Count == 0)
            {
                return false;
            }

            section = Math.Max(1, section);
            var rowName = _storyRows[_currentStoryRowIndex].Get("Name");
            if (_storyRowSections.TryGetValue(rowName, out var currentSection) &&
                Math.Max(1, currentSection) == section)
            {
                EnsureStorySectionOptions(section);
                return false;
            }

            _storyRowSections[rowName] = section;
            EnsureStorySectionOptions(section);
            return true;
        }

        private void LoadStorySectionState(ChapterInfo chapter)
        {
            _storyRowSections.Clear();
            var state = ReadJson<StorySectionState>(GetStorySectionsPath(chapter));
            if (state?.Rows is null)
            {
                return;
            }

            foreach (var pair in state.Rows)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    _storyRowSections[pair.Key] = Math.Max(1, pair.Value);
                }
            }
        }

        private void LoadStoryRowsFromSectionFiles(ChapterInfo chapter)
        {
            _storyRows.Clear();
            _storyRowSections.Clear();

            var mainCsvPath = GetChapterStoryCsvPath(chapter);
            Directory.CreateDirectory(chapter.Path);
            var sectionFiles = GetLocalStorySectionCsvPaths(chapter)
                .OrderBy(item => item.Section)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!sectionFiles.Any(item => item.Section == 1))
            {
                sectionFiles.Insert(0, new StorySectionCsvFile(mainCsvPath, 1));
            }

            var legacySectionMap = ReadStorySectionMap(chapter);
            if (!sectionFiles.Any(item => item.Section > 1) &&
                legacySectionMap.Values.Any(section => section > 1) &&
                File.Exists(mainCsvPath))
            {
                var legacyRows = ReadStoryRows(mainCsvPath);
                var previousSection = 1;
                foreach (var row in legacyRows)
                {
                    var originalName = row.Get("Name");
                    if (legacySectionMap.TryGetValue(originalName, out var section))
                    {
                        previousSection = Math.Max(1, section);
                    }

                    var clone = row.Clone();
                    clone.Set("Name", CreateStoryRowName(_storyRows.Count));
                    _storyRows.Add(clone);
                    _storyRowSections[clone.Get("Name")] = previousSection;
                }

                if (_storyRows.Count > 0)
                {
                    return;
                }
            }

            var removedEmptyCount = 0;
            foreach (var sectionFile in sectionFiles)
            {
                var rows = ReadStoryRows(sectionFile.Path);
                var isMainSection = sectionFile.Section == 1 && PathsEqual(sectionFile.Path, mainCsvPath);
                if (rows.Count == 0 || !rows.Any(StoryRowHasContent))
                {
                    if (!isMainSection && File.Exists(sectionFile.Path))
                    {
                        File.Delete(sectionFile.Path);
                        removedEmptyCount++;
                    }

                    continue;
                }

                foreach (var row in rows)
                {
                    var clone = row.Clone();
                    clone.Set("Name", CreateStoryRowName(_storyRows.Count));
                    _storyRows.Add(clone);
                    _storyRowSections[clone.Get("Name")] = Math.Max(1, sectionFile.Section);
                }
            }

            if (_storyRows.Count == 0)
            {
                var row = CreateDefaultStoryRow();
                _storyRows.Add(row);
                _storyRowSections[row.Get("Name")] = 1;
                WriteStoryRows(mainCsvPath, _storyRows);
            }

            if (removedEmptyCount > 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已清理空小节", $"检测并删除 {removedEmptyCount} 个空小节 CSV。");
            }
        }

        private void ImportLooseStorySectionCsvFiles(ChapterInfo chapter)
        {
            if (_currentStoryCsvPath is null || !Directory.Exists(chapter.Path))
            {
                return;
            }

            var looseCsvPaths = Directory
                .EnumerateFiles(chapter.Path, "*.csv", SearchOption.TopDirectoryOnly)
                .Where(path => IsLooseStorySectionCsvCandidate(chapter, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (looseCsvPaths.Count == 0)
            {
                return;
            }

            var importedCount = ImportStorySectionCsvFiles(chapter, looseCsvPaths, deleteSourceFiles: true);
            if (importedCount > 0)
            {
                EnsureStorySectionOptions(_storyRowSections.Values.DefaultIfEmpty(1).Max());
                ShowStoryStatus(InfoBarSeverity.Informational, "已导入小节 CSV", $"检测并导入 {importedCount} 个章节小节。");
            }
        }

        private int ImportStorySectionCsvFiles(ChapterInfo chapter, IReadOnlyList<string> csvPaths, bool deleteSourceFiles)
        {
            var mainCsvPath = GetChapterStoryCsvPath(chapter);
            var changed = false;
            var importedCount = 0;
            var nextSection = GetLocalStorySectionCsvPaths(chapter)
                .Select(item => item.Section)
                .DefaultIfEmpty(1)
                .Max() + 1;
            foreach (var csvPath in csvPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (PathsEqual(csvPath, mainCsvPath) || !File.Exists(csvPath))
                {
                    continue;
                }

                var compatibility = InspectStoryCsvCompatibility(csvPath);
                if (!compatibility.IsCompatible)
                {
                    AppendLog(LogKind.Warning, $"跳过结构不兼容的小节 CSV：{csvPath}");
                    continue;
                }

                var sectionRows = ReadStoryRows(csvPath);
                if (sectionRows.Count == 0 || !sectionRows.Any(StoryRowHasContent))
                {
                    if (deleteSourceFiles)
                    {
                        File.Delete(csvPath);
                    }

                    changed = true;
                    AppendLog(LogKind.Info, $"已删除空小节 CSV：{csvPath}");
                    continue;
                }

                var section = TryParseStorySectionFromFileName(chapter, csvPath) ?? nextSection++;
                var targetCsvPath = GetStorySectionCsvPath(chapter, section);
                var rowsToWrite = sectionRows.Select(row => row.Clone()).ToList();
                WriteStoryRows(targetCsvPath, rowsToWrite);

                if (deleteSourceFiles && !PathsEqual(csvPath, targetCsvPath))
                {
                    File.Delete(csvPath);
                }

                changed = true;
                importedCount++;
                AppendLog(LogKind.User, $"已导入小节 CSV 为第 {section} 小节：{Path.GetFileName(csvPath)}");
            }

            if (changed)
            {
                if (_currentStoryChapter is not null && PathsEqual(_currentStoryChapter.Path, chapter.Path))
                {
                    LoadStoryRowsFromSectionFiles(chapter);
                    SynchronizeStorySectionState();
                    WriteStorySectionState(chapter.Path, _storyRowSections);
                }
            }

            return importedCount;
        }

        private static bool IsLooseStorySectionCsvCandidate(ChapterInfo chapter, string csvPath)
        {
            var mainCsvPath = GetChapterStoryCsvPath(chapter);
            if (PathsEqual(csvPath, mainCsvPath))
            {
                return false;
            }

            var fileName = Path.GetFileName(csvPath);
            var baseName = BuildSectionCsvBaseName(chapter.Code);
            var sectionBaseName = BuildSectionCsvChapterBaseName(chapter.Code);
            if (fileName.StartsWith($"{baseName}_小节", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Regex.IsMatch(
                Path.GetFileNameWithoutExtension(csvPath),
                $"^{Regex.Escape(sectionBaseName)}[-_](?<index>\\d+)$",
                RegexOptions.IgnoreCase))
            {
                return true;
            }

            return !fileName.EndsWith(".story.csv", StringComparison.OrdinalIgnoreCase) &&
                !fileName.StartsWith($"{sectionBaseName}_", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStorySectionCsvPath(ChapterInfo chapter, int section)
        {
            return section <= 1
                ? GetChapterStoryCsvPath(chapter)
                : Path.Combine(chapter.Path, $"{BuildSectionCsvFileBaseName(chapter.Code, section)}.csv");
        }

        private static List<StorySectionCsvFile> GetLocalStorySectionCsvPaths(ChapterInfo chapter)
        {
            if (!Directory.Exists(chapter.Path))
            {
                return [];
            }

            var mainCsvPath = GetChapterStoryCsvPath(chapter);
            var result = new List<StorySectionCsvFile>();
            foreach (var csvPath in Directory.EnumerateFiles(chapter.Path, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(csvPath).EndsWith(".story.csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PathsEqual(csvPath, mainCsvPath))
                {
                    result.Add(new StorySectionCsvFile(csvPath, 1));
                    continue;
                }

                var section = TryParseStorySectionFromFileName(chapter, csvPath);
                if (section is not null)
                {
                    result.Add(new StorySectionCsvFile(csvPath, Math.Max(1, section.Value)));
                }
            }

            return result;
        }

        private void PersistCurrentStoryRowsToFiles(bool showStatus = false)
        {
            if (_isPersistingStoryRows || _currentStoryChapter is null || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            _isPersistingStoryRows = true;
            try
            {
                for (var i = 0; i < _storyRows.Count; i++)
                {
                    _storyRows[i].Set("Name", CreateStoryRowName(i));
                }

                SynchronizeStorySectionState();
                var groupedRows = _storyRows
                    .Select((row, index) => new
                    {
                        Row = row,
                        Section = _storyRowSections.TryGetValue(row.Get("Name"), out var section) ? Math.Max(1, section) : 1,
                        Index = index
                    })
                    .GroupBy(item => item.Section)
                    .OrderBy(group => group.Key)
                    .ToList();

                var activeCsvPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in groupedRows)
                {
                    var rows = group.Select(item => item.Row.Clone()).ToList();
                    var targetCsvPath = GetStorySectionCsvPath(_currentStoryChapter, group.Key);
                    if (!rows.Any(StoryRowHasContent))
                    {
                        if (group.Key > 1 && File.Exists(targetCsvPath))
                        {
                            File.Delete(targetCsvPath);
                        }

                        continue;
                    }

                    WriteStoryRows(targetCsvPath, rows);
                    activeCsvPaths.Add(targetCsvPath);
                }

                if (activeCsvPaths.Count == 0)
                {
                    var defaultRows = new List<StoryRow> { CreateDefaultStoryRow() };
                    WriteStoryRows(_currentStoryCsvPath, defaultRows);
                    activeCsvPaths.Add(_currentStoryCsvPath);
                }

                DeleteInactiveLocalStorySectionCsvFiles(_currentStoryChapter, activeCsvPaths);
                WriteStorySectionState(_currentStoryChapter.Path, _storyRowSections);

                if (showStatus)
                {
                    ShowStoryStatus(InfoBarSeverity.Success, "小节 CSV 已更新", $"已按 {activeCsvPaths.Count} 个小节文件保存。");
                }
            }
            finally
            {
                _isPersistingStoryRows = false;
            }
        }

        private static void DeleteInactiveLocalStorySectionCsvFiles(ChapterInfo chapter, IReadOnlySet<string> activeCsvPaths)
        {
            foreach (var sectionFile in GetLocalStorySectionCsvPaths(chapter))
            {
                if (!activeCsvPaths.Contains(sectionFile.Path) && File.Exists(sectionFile.Path))
                {
                    File.Delete(sectionFile.Path);
                }
            }
        }

        private void SynchronizeStorySectionState()
        {
            var synchronized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var previousSection = 1;
            foreach (var row in _storyRows)
            {
                var rowName = row.Get("Name");
                if (_storyRowSections.TryGetValue(rowName, out var section))
                {
                    previousSection = Math.Max(1, section);
                }

                synchronized[rowName] = previousSection;
            }

            _storyRowSections.Clear();
            foreach (var pair in synchronized)
            {
                _storyRowSections[pair.Key] = pair.Value;
            }
        }

        private List<int> GetStorySectionsInRowOrder()
        {
            var sections = new List<int>();
            var previousSection = 1;
            foreach (var row in _storyRows)
            {
                var rowName = row.Get("Name");
                if (_storyRowSections.TryGetValue(rowName, out var section))
                {
                    previousSection = Math.Max(1, section);
                }

                sections.Add(previousSection);
            }

            return sections;
        }

        private void ApplyStorySectionsInRowOrder(IReadOnlyList<int> sections)
        {
            _storyRowSections.Clear();
            for (var i = 0; i < _storyRows.Count; i++)
            {
                var section = i < sections.Count ? sections[i] : 1;
                _storyRowSections[_storyRows[i].Get("Name")] = Math.Max(1, section);
            }
        }

        private void WriteStorySectionState()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            WriteStorySectionState(_currentStoryChapter.Path, _storyRowSections);
        }

        private void WriteStorySectionState(string chapterPath, IReadOnlyDictionary<string, int> sections)
        {
            Directory.CreateDirectory(chapterPath);
            var state = new StorySectionState();
            foreach (var pair in sections.OrderBy(pair => ParseInt(pair.Key)))
            {
                state.Rows[pair.Key] = Math.Max(1, pair.Value);
            }

            File.WriteAllText(
                Path.Combine(chapterPath, StorySectionsFileName),
                JsonSerializer.Serialize(state, _jsonOptions),
                Encoding.UTF8);
        }

        private static string GetStorySectionsPath(ChapterInfo chapter)
        {
            return Path.Combine(chapter.Path, StorySectionsFileName);
        }

        private async void ExportStorySectionsButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportStorySectionsAsync(true);
        }

        private void SynchronizeStorySectionCsvExports(bool showStatus = false)
        {
            _ = ExportStorySectionsAsync(showStatus);
        }

        private Task ExportStorySectionsAsync(bool showStatus = false)
        {
            if (_currentProject is null || _currentStoryChapter is null || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return Task.CompletedTask;
            }

            PersistCurrentStoryRowsToFiles(showStatus);
            if (showStatus || !showStatus)
            {
                return Task.CompletedTask;
            }

            SynchronizeStorySectionState();
            WriteStorySectionState();

            CleanupVisibleStorySectionCsvFiles(_currentStoryChapter);
            var exportPath = GetUnrealStorySectionCacheFolder(_currentProject, _currentStoryChapter);
            Directory.CreateDirectory(exportPath);
            var baseName = BuildSectionCsvBaseName(_currentStoryChapter.Code);
            var sectionBaseName = BuildSectionCsvChapterBaseName(_currentStoryChapter.Code);
            var activeCsvPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = _storyRows
                .Select(row => new
                {
                    Row = row,
                    Section = _storyRowSections.TryGetValue(row.Get("Name"), out var section) ? Math.Max(1, section) : 1
                })
                .GroupBy(item => item.Section)
                .OrderBy(group => group.Key)
                .ToList();

            if (groups.Select(group => group.Key).Distinct().Count() <= 1)
            {
                CleanupStorySectionExports(exportPath, baseName, sectionBaseName);
                CleanupUnrealStorySectionCache(_currentProject, _currentStoryChapter);
                CleanupLegacyStorySectionExports(_currentStoryChapter.Path, baseName);
                return Task.CompletedTask;
            }

            foreach (var group in groups)
            {
                var rows = group.Select(item => item.Row.Clone()).ToList();
                var csvPath = Path.Combine(exportPath, $"{BuildSectionCsvFileBaseName(_currentStoryChapter.Code, group.Key)}.csv");
                if (!rows.Any(StoryRowHasContent))
                {
                    if (File.Exists(csvPath))
                    {
                        File.Delete(csvPath);
                    }

                    continue;
                }

                WriteStoryRows(csvPath, rows);
                activeCsvPaths.Add(csvPath);
            }

            foreach (var staleCsvPath in Directory.EnumerateFiles(exportPath, $"{sectionBaseName}-*.csv")
                .Concat(Directory.EnumerateFiles(exportPath, $"{sectionBaseName}_*.csv"))
                .Concat(Directory.EnumerateFiles(exportPath, $"{baseName}_小节*.csv")))
            {
                if (!activeCsvPaths.Contains(staleCsvPath))
                {
                    File.Delete(staleCsvPath);
                }
            }

            CleanupLegacyStorySectionExports(_currentStoryChapter.Path, baseName);

            if (!showStatus)
            {
                return Task.CompletedTask;
            }

            ShowStoryStatus(InfoBarSeverity.Success, "小节 CSV 已生成", $"已生成 {groups.Count} 个小节 CSV：{exportPath}");
            return Task.CompletedTask;
        }

        private static void CleanupStorySectionExports(string chapterPath, string baseName, string sectionBaseName)
        {
            if (!Directory.Exists(chapterPath))
            {
                return;
            }

            foreach (var staleCsvPath in Directory.EnumerateFiles(chapterPath, $"{baseName}_小节*.csv")
                .Concat(Directory.EnumerateFiles(chapterPath, $"{sectionBaseName}-*.csv"))
                .Concat(Directory.EnumerateFiles(chapterPath, $"{sectionBaseName}_*.csv")))
            {
                File.Delete(staleCsvPath);
            }
        }

        private static void CleanupLegacyStorySectionExports(string chapterPath, string baseName)
        {
            var legacyExportPath = Path.Combine(chapterPath, StorySectionExportsFolderName);
            if (!Directory.Exists(legacyExportPath))
            {
                return;
            }

            foreach (var staleCsvPath in Directory.EnumerateFiles(legacyExportPath, $"{baseName}_小节*.csv"))
            {
                File.Delete(staleCsvPath);
            }

            if (!Directory.EnumerateFileSystemEntries(legacyExportPath).Any())
            {
                Directory.Delete(legacyExportPath);
            }
        }

        private static string BuildSectionCsvBaseName(string chapterCode)
        {
            var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
            var builder = new StringBuilder();
            foreach (var ch in chapterCode.Trim())
            {
                if (invalidChars.Contains(ch) || char.IsWhiteSpace(ch))
                {
                    continue;
                }

                builder.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-');
            }

            return builder.Length == 0 ? "Story" : builder.ToString();
        }

        private static string BuildSectionCsvChapterBaseName(string chapterCode)
        {
            var chapterBaseCode = RemoveChapterSectionSuffix(chapterCode);
            return BuildSectionCsvBaseName(chapterBaseCode);
        }

        private static string BuildSectionCsvFileBaseName(string chapterCode, int section)
        {
            return $"{BuildSectionCsvChapterBaseName(chapterCode)}-{Math.Max(0, section - 1):00}";
        }

        private string BuildNextStoryChoiceFunctionIndicator()
        {
            if (_currentStoryChapter is null)
            {
                return "Choice1";
            }

            var prefix = BuildCurrentStoryChapterSectionChoicePrefix();
            var maxChoiceIndex = _storyRows
                .SelectMany(row => SplitStoryFunctionValues(row.Get("Custom")))
                .Select(function => TryParseChoiceFunctionIndex(function, prefix))
                .Where(index => index > 0)
                .DefaultIfEmpty(0)
                .Max();

            return $"{prefix}{maxChoiceIndex + 1}";
        }

        private string BuildCurrentStoryChapterSectionChoicePrefix()
        {
            if (_currentStoryChapter is null)
            {
                return "Choice";
            }

            var chapterSectionCode = RemoveProjectCodePrefix(BuildCurrentStoryChapterSectionCode(), _currentProject?.Code);
            return $"{chapterSectionCode}-Choice";
        }

        private string BuildCurrentStoryChapterSectionCode()
        {
            return _currentStoryChapter is null
                ? string.Empty
                : BuildStoryChapterSectionCode(_currentStoryChapter.Code, GetCurrentStorySection());
        }

        private static string BuildStoryChapterSectionCode(string chapterCode, int editorSection)
        {
            return $"{RemoveChapterSectionSuffix(chapterCode)}-{Math.Max(0, editorSection - 1):00}";
        }

        private void NormalizeCurrentStoryChoiceFunctionPrefix()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var functions = SplitStoryFunctionValues(row.Get("Custom")).ToList();
            if (functions.Count == 0)
            {
                return;
            }

            var chapterBaseCode = RemoveProjectCodePrefix(RemoveChapterSectionSuffix(_currentStoryChapter.Code), _currentProject?.Code);
            if (string.IsNullOrWhiteSpace(chapterBaseCode))
            {
                return;
            }

            var targetPrefix = BuildCurrentStoryChapterSectionChoicePrefix();
            var changed = false;
            var oldChoices = new List<string>();
            var newChoices = new List<string>();
            for (var i = 0; i < functions.Count; i++)
            {
                var match = Regex.Match(
                    functions[i],
                    $"^{Regex.Escape(chapterBaseCode)}-(?<section>\\d{{2}})-Choice(?<index>\\d+)$",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                var normalized = $"{targetPrefix}{match.Groups["index"].Value}";
                if (string.Equals(functions[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                oldChoices.Add(functions[i]);
                newChoices.Add(normalized);
                functions[i] = normalized;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            row.Set("Custom", string.Join("/", functions));
            PersistCurrentStoryRowsToFiles();
            for (var i = 0; i < oldChoices.Count; i++)
            {
                CopyStoryChoiceNotes(oldChoices[i], newChoices[i]);
            }

            RemoveUnusedStoryChoiceNotes(oldChoices);
        }

        private static int TryParseChoiceFunctionIndex(string functionValue, string prefix)
        {
            if (!functionValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return int.TryParse(functionValue[prefix.Length..].Trim(), out var index) ? index : 0;
        }

        private static string RemoveChapterSectionSuffix(string chapterCode)
        {
            var match = Regex.Match(chapterCode.Trim(), @"^(?<prefix>.+)-(?<section>\d+)$");
            return match.Success ? match.Groups["prefix"].Value : chapterCode.Trim();
        }

        private static bool StoryRowHasContent(StoryRow row)
        {
            foreach (var column in StoryCsvColumns.Where(column => column != "Name"))
            {
                var value = row.Get(column).Trim();
                if (StoryNumericColumns.Contains(column))
                {
                    if (ParseInt(value) != 0)
                    {
                        return true;
                    }

                    continue;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateStoryEditorHeader()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            StoryEditorTitleText.Text = $"{_currentStoryChapter.Name} / {BuildCurrentStoryChapterSectionCode()}";
        }

        private void UpdateStoryToolbarCurrentInfo()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            StoryCurrentBackgroundText.Text = FormatStoryAssetStatus("当前背景图", ParseInt(row.Get("BGindex")), GetStoryBackgroundChoices());
            StoryCurrentBgmText.Text = FormatStoryAssetStatus("当前BGM", ParseInt(row.Get("BGM")), GetStoryBgmChoices());
            StoryCurrentSceneText.Text = FormatStoryAssetStatus("当前环境音", ParseInt(row.Get("Scene")), GetStorySceneChoices());
            var custom = row.Get("Custom").Trim();
            var hasFunction = !string.IsNullOrWhiteSpace(custom);
            StoryCurrentFunctionText.Text = string.IsNullOrWhiteSpace(custom)
                ? "当前函数：无"
                : $"当前函数：{custom}";
            StoryRemoveFunctionButton.Visibility = hasFunction ? Visibility.Visible : Visibility.Collapsed;
            StoryClearFunctionButton.Visibility = hasFunction ? Visibility.Visible : Visibility.Collapsed;
            StoryViewChoicesButton.Visibility = GetCurrentStoryChoiceValues().Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static string FormatStoryAssetStatus(string label, int rawIndex, IReadOnlyList<StoryAssetChoice> choices)
        {
            var resolvedIndex = ResolveStoryAssetIndex(rawIndex, choices.Count);
            return resolvedIndex is null
                ? $"{label}：无"
                : $"{label}：{choices[resolvedIndex.Value].Index}: {choices[resolvedIndex.Value].Name}";
        }

        private async void ChangeStoryBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            await ChooseStoryAssetIndexAsync("更换背景图", "BGindex", GetStoryBackgroundChoices());
        }

        private async void ChangeStoryBgmButton_Click(object sender, RoutedEventArgs e)
        {
            await ChooseStoryAssetIndexAsync("更换BGM", "BGM", GetStoryBgmChoices());
        }

        private async void ChangeStorySceneButton_Click(object sender, RoutedEventArgs e)
        {
            await ChooseStoryAssetIndexAsync("更换环境音", "Scene", GetStorySceneChoices());
        }

        private async void ChooseStoryFunctionButton_Click(object sender, RoutedEventArgs e)
        {
            await ChooseStoryFunctionAsync();
        }

        private async void RemoveStoryFunctionButton_Click(object sender, RoutedEventArgs e)
        {
            await RemoveStoryFunctionAsync();
        }

        private async void ViewStoryChoicesButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowCurrentStoryChoicesAsync();
        }

        private void ClearStoryFunctionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var removedChoices = GetCurrentStoryChoiceValues();
            if (!string.IsNullOrWhiteSpace(_storyRows[_currentStoryRowIndex].Get("Custom")))
            {
                CaptureStoryUndoState("清空当前句函数");
            }

            _storyRows[_currentStoryRowIndex].Set("Custom", string.Empty);
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes(removedChoices);
            UpdateStoryToolbarCurrentInfo();
            ClearStoryFunctionTips();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已清空", "当前句的 Custom 字段已清空。");
        }

        private void ClearCurrentStoryRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var currentName = _storyRows[_currentStoryRowIndex].Get("Name");
            var removedChoices = GetCurrentStoryChoiceValues();
            CaptureStoryUndoState("清空当前行数据");
            var row = CreateDefaultStoryRow();
            row.Set("Name", currentName);
            _storyRows[_currentStoryRowIndex] = row;
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes(removedChoices);
            ClearStoryFunctionTips();
            StopStoryEditorAudio();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            ShowStoryStatus(InfoBarSeverity.Success, "当前行已清空", "已保留句子位置并重置当前行数据。");
        }

        private async Task ChooseStoryFunctionAsync()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var functions = GetStoryFunctions();
            if (functions.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可用函数", "当前项目绑定的素材库里还没有函数卡。");
                return;
            }

            var selected = await ShowStoryChoiceDialogAsync(
                "填写函数",
                functions
                    .Select(function => new StoryObjectChoice(
                        function.Id,
                        FormatStoryFunctionChoiceDisplay(function),
                        function))
                    .ToList());
            if (selected is not FunctionEntry function)
            {
                return;
            }

            var functionValue = await BuildStoryFunctionValueAsync(function);
            if (functionValue is null)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var currentValue = row.Get("Custom").Trim();
            CaptureStoryUndoState("添加函数");
            row.Set("Custom", string.IsNullOrWhiteSpace(currentValue) ? functionValue : $"{currentValue}/{functionValue}");
            PersistCurrentStoryRowsToFiles();
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已填写", functionValue);
            await ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private async Task RemoveStoryFunctionAsync()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var functions = SplitStoryFunctionValues(row.Get("Custom")).ToList();
            if (functions.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可移除函数", "当前句还没有填写函数。");
                return;
            }

            var selected = await ShowStoryChoiceDialogAsync(
                "移除函数",
                functions.Select((function, index) => new StoryObjectChoice(index.ToString(), function, index)).ToList());
            if (selected is not int selectedIndex || selectedIndex < 0 || selectedIndex >= functions.Count)
            {
                return;
            }

            var removed = functions[selectedIndex];
            CaptureStoryUndoState("移除函数");
            functions.RemoveAt(selectedIndex);
            row.Set("Custom", string.Join("/", functions));
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes([removed]);
            UpdateStoryToolbarCurrentInfo();
            ClearStoryFunctionTips();
            ShowStoryStatus(InfoBarSeverity.Success, "函数已移除", removed);
            await ApplyCurrentStoryRowMediaAndFunctionAsync();
        }

        private List<FunctionEntry> GetStoryFunctions()
        {
            return _currentStoryAssetLibrary is null ? [] : ReadFunctions(_currentStoryAssetLibrary);
        }

        private string FormatStoryFunctionChoiceDisplay(FunctionEntry function)
        {
            var indicator = IsChoiceFunctionTemplate(function)
                ? BuildNextStoryChoiceFunctionIndicator()
                : function.Indicator;
            return $"{function.Name} / {indicator} / {function.Category}";
        }

        private async Task<string?> BuildStoryFunctionValueAsync(FunctionEntry function)
        {
            var indicator = function.Indicator.Trim();
            if (IsChoiceFunctionTemplate(function))
            {
                var choiceIndicator = BuildNextStoryChoiceFunctionIndicator();
                var optionNotes = await ShowChoiceFunctionNoteDialogAsync(choiceIndicator);
                if (optionNotes is null)
                {
                    return null;
                }

                SaveChoiceFunctionNotes(choiceIndicator, optionNotes);
                return choiceIndicator;
            }

            if (IsChapterJumpFunctionTemplate(function))
            {
                return await ChooseChapterJumpFunctionValueAsync();
            }

            if (IsSegmentJumpFunctionTemplate(function))
            {
                return await ChooseSegmentJumpFunctionValueAsync();
            }

            if (IsBgmFunctionTemplate(function))
            {
                return await ChooseBgmFunctionValueAsync();
            }

            if (string.Equals(function.Id, "default-custom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(indicator, "CustomFunction", StringComparison.OrdinalIgnoreCase))
            {
                return await ShowNameInputDialogAsync("自定义函数", "函数指示器", string.Empty);
            }

            if (indicator.StartsWith("Scene_", StringComparison.OrdinalIgnoreCase))
            {
                var soundEffectIndex = await ChooseStorySoundEffectIndexAsync();
                return soundEffectIndex is null ? null : $"Scene_{soundEffectIndex.Value}";
            }

            if (string.Equals(indicator, "BGLerpMode_", StringComparison.OrdinalIgnoreCase))
            {
                var selected = await ShowStoryChoiceDialogAsync(
                    "背景切换模式",
                    new List<StoryObjectChoice>
                    {
                        new("0", "0：游戏入场黑屏", 0),
                        new("1", "1：正常黑屏转场", 1),
                        new("2", "2：背景图渐变过渡", 2)
                    });
                return selected is int mode ? $"BGLerpMode_{mode}" : null;
            }

            if (indicator.StartsWith("VFXON_", StringComparison.OrdinalIgnoreCase) ||
                indicator.StartsWith("VFXOFF_", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = await ShowNameInputDialogAsync("特效标识", "自定义字符串", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            if (string.Equals(indicator, "TransAnim_", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = await ShowNameInputDialogAsync("动画序列索引", "索引", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            if (string.Equals(indicator, "MedPlay_", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = await ShowNameInputDialogAsync("视频索引", "索引", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            if (indicator.EndsWith('_'))
            {
                var suffix = await ShowNameInputDialogAsync("函数参数", "参数", string.Empty);
                return string.IsNullOrWhiteSpace(suffix) ? null : indicator + suffix;
            }

            return indicator;
        }

        private async Task<string?> ChooseBgmFunctionValueAsync()
        {
            var selected = await ShowStoryChoiceDialogAsync(
                "BGM",
                new List<StoryObjectChoice>
                {
                    new("BGM_Start", "Start / BGM_Start", "BGM_Start"),
                    new("BGM_Stop", "Stop / BGM_Stop", "BGM_Stop")
                });
            return selected as string;
        }

        private async Task<string?> ChooseChapterJumpFunctionValueAsync()
        {
            if (_currentProject is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转章节", "当前没有打开项目。");
                return null;
            }

            var chaptersFolderPath = GetChaptersFolderPath(_currentProject);
            List<ChapterInfo> chapters = Directory.Exists(chaptersFolderPath)
                ? Directory.EnumerateDirectories(chaptersFolderPath)
                    .Select(ReadChapterInfo)
                    .OrderBy(chapter => chapter.Code)
                    .ToList()
                : new List<ChapterInfo>();
            if (chapters.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转章节", "当前项目还没有章节。");
                return null;
            }

            var choices = chapters
                .Select(chapter =>
                {
                    var functionValue = BuildChapterJumpFunctionValue(chapter);
                    return new StoryObjectChoice(
                        chapter.Code,
                        $"{chapter.Name} / {functionValue}",
                        functionValue);
                })
                .ToList();
            return await ShowStoryChoiceDialogAsync("跳转章节", choices) as string;
        }

        private string BuildChapterJumpFunctionValue(ChapterInfo chapter)
        {
            var chapterCode = RemoveProjectCodePrefix(RemoveChapterSectionSuffix(chapter.Code), _currentProject?.Code);
            return $"IntoChapter_{chapterCode}";
        }

        private async Task<string?> ChooseSegmentJumpFunctionValueAsync()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转小节", "请先打开一个章节。");
                return null;
            }

            var sectionCount = GetCurrentStorySectionCount();
            var choices = Enumerable.Range(1, sectionCount)
                .Select(section =>
                {
                    var functionValue = BuildSegmentJumpFunctionValue(section);
                    return new StoryObjectChoice(
                        section.ToString(CultureInfo.InvariantCulture),
                        $"第 {section} 小节 / {functionValue}",
                        functionValue);
                })
                .ToList();
            return await ShowStoryChoiceDialogAsync("跳转小节", choices) as string;
        }

        private int GetCurrentStorySectionCount()
        {
            var sectionCount = 1;
            if (_storyRowSections.Count > 0)
            {
                sectionCount = Math.Max(sectionCount, _storyRowSections.Values.DefaultIfEmpty(1).Max());
            }

            foreach (var item in StorySectionComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is int section)
                {
                    sectionCount = Math.Max(sectionCount, section);
                }
            }

            if (_currentStoryChapter is not null)
            {
                sectionCount = Math.Max(
                    sectionCount,
                    GetLocalStorySectionCsvPaths(_currentStoryChapter)
                        .Select(file => file.Section)
                        .DefaultIfEmpty(1)
                        .Max());
            }

            return Math.Max(1, sectionCount);
        }

        private static string BuildSegmentJumpFunctionValue(int section)
        {
            return $"IntoSegment_{Math.Max(0, section - 1):00}";
        }

        private async Task<List<string>?> ShowChoiceFunctionNoteDialogAsync(string choiceIndicator)
        {
            var notesPanel = new StackPanel
            {
                Spacing = 8
            };

            void RenumberRows()
            {
                for (var i = 0; i < notesPanel.Children.Count; i++)
                {
                    if (notesPanel.Children[i] is not Grid row)
                    {
                        continue;
                    }

                    foreach (var textBox in row.Children.OfType<TextBox>())
                    {
                        textBox.Header = $"选项备注 {i + 1}";
                    }

                    foreach (var textBlock in row.Children.OfType<TextBlock>())
                    {
                        textBlock.Text = $"选项 {i + 1}";
                    }
                }
            }

            void AddChoiceRow()
            {
                var row = new Grid
                {
                    ColumnSpacing = 8
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var optionLabel = new TextBlock
                {
                    Text = $"选项 {notesPanel.Children.Count + 1}",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var noteBox = new TextBox
                {
                    PlaceholderText = "例如：接受邀请",
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 42
                };
                var removeButton = new Button
                {
                    Content = "删除",
                    Margin = new Thickness(0, 28, 0, 0)
                };
                removeButton.Click += (_, _) =>
                {
                    notesPanel.Children.Remove(row);
                    if (notesPanel.Children.Count == 0)
                    {
                        AddChoiceRow();
                        return;
                    }

                    RenumberRows();
                };

                Grid.SetColumn(noteBox, 1);
                Grid.SetColumn(removeButton, 2);
                row.Children.Add(optionLabel);
                row.Children.Add(noteBox);
                row.Children.Add(removeButton);
                notesPanel.Children.Add(row);
                RenumberRows();
            }

            AddChoiceRow();
            var addButton = new Button
            {
                Width = 36,
                Height = 32,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = "+"
            };
            addButton.Click += (_, _) => AddChoiceRow();

            var panel = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = choiceIndicator,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "这一个 Choice 会在虚幻里弹出选择界面；下面每行只是这个界面里的一个选项备注。",
                        TextWrapping = TextWrapping.Wrap,
                        Style = TryGetTextBlockStyle("SubtleTextStyle")
                    },
                    notesPanel,
                    addButton,
                    new TextBlock
                    {
                        Text = $"表格只会写入 {choiceIndicator}。",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "备注不会写入剧情表 Custom 字段。",
                        TextWrapping = TextWrapping.Wrap,
                        Style = TryGetTextBlockStyle("SubtleTextStyle")
                    }
                }
            };

            var dialog = new ContentDialog
            {
                Title = "添加触发选项",
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var notes = new List<string>();
            foreach (var row in notesPanel.Children.OfType<Grid>())
            {
                var note = NormalizeFunctionChoiceNote(row.Children.OfType<TextBox>().FirstOrDefault()?.Text);
                notes.Add(note);
            }

            return notes;
        }

        private void SaveChoiceFunctionNotes(string choiceIndicator, IReadOnlyList<string> optionNotes)
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            var state = ReadStoryChoiceNoteState(_currentStoryChapter);
            state.Choices[choiceIndicator] = optionNotes
                .Select(NormalizeFunctionChoiceNote)
                .ToList();
            WriteStoryChoiceNoteState(_currentStoryChapter, state);
        }

        private List<string> GetCurrentStoryChoiceValues()
        {
            if (_storyRows.Count == 0)
            {
                return [];
            }

            return SplitStoryFunctionValues(_storyRows[_currentStoryRowIndex].Get("Custom"))
                .Where(IsCurrentStoryChoiceFunctionValue)
                .ToList();
        }

        private bool IsCurrentStoryChoiceFunctionValue(string functionValue)
        {
            var prefix = BuildCurrentStoryChapterSectionChoicePrefix();
            return TryParseChoiceFunctionIndex(functionValue, prefix) > 0;
        }

        private async Task ShowCurrentStoryChoicesAsync()
        {
            var choices = GetCurrentStoryChoiceValues();
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "没有选项", "当前句还没有触发选项。");
                return;
            }

            var noteMap = GetChoiceFunctionNoteMap();
            var choicesPanel = new StackPanel
            {
                Spacing = 16
            };
            foreach (var choice in choices)
            {
                noteMap.TryGetValue(choice, out var notes);
                var choicePanel = new StackPanel
                {
                    Spacing = 8
                };
                choicePanel.Children.Add(new TextBlock
                {
                    Text = choice,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                if (notes is { Count: > 0 })
                {
                    for (var i = 0; i < notes.Count; i++)
                    {
                        var row = new Grid
                        {
                            ColumnSpacing = 8
                        };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.Children.Add(new TextBlock
                        {
                            Text = $"选项 {i + 1}",
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        var noteBox = new TextBox
                        {
                            Text = string.IsNullOrWhiteSpace(notes[i]) ? "无备注" : notes[i],
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap
                        };
                        Grid.SetColumn(noteBox, 1);
                        row.Children.Add(noteBox);
                        choicePanel.Children.Add(row);
                    }
                }
                else
                {
                    choicePanel.Children.Add(new TextBlock
                    {
                        Text = "无备注",
                        TextWrapping = TextWrapping.Wrap,
                        Style = TryGetTextBlockStyle("SubtleTextStyle")
                    });
                }

                choicesPanel.Children.Add(choicePanel);
            }

            var dialog = new ContentDialog
            {
                Title = "查看选项",
                Content = new ScrollViewer
                {
                    Width = 520,
                    MaxHeight = 420,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = choicesPanel
                },
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            await dialog.ShowAsync();
        }

        private Dictionary<string, List<string>> GetChoiceFunctionNoteMap()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (_currentStoryChapter is null)
            {
                return result;
            }

            var state = ReadStoryChoiceNoteState(_currentStoryChapter);
            foreach (var pair in state.Choices)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    result[pair.Key] = pair.Value
                        .Select(NormalizeFunctionChoiceNote)
                        .ToList();
                }
            }

            return result;
        }

        private StoryChoiceNoteState ReadStoryChoiceNoteState(ChapterInfo chapter)
        {
            var state = ReadJson<StoryChoiceNoteState>(GetStoryChoiceNotesPath(chapter)) ?? new StoryChoiceNoteState();
            state.Choices = new Dictionary<string, List<string>>(
                state.Choices
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .Select(pair => new KeyValuePair<string, List<string>>(
                        pair.Key.Trim(),
                        (pair.Value ?? [])
                            .Select(NormalizeFunctionChoiceNote)
                            .ToList())),
                StringComparer.OrdinalIgnoreCase);
            return state;
        }

        private void WriteStoryChoiceNoteState(ChapterInfo chapter, StoryChoiceNoteState state)
        {
            File.WriteAllText(GetStoryChoiceNotesPath(chapter), JsonSerializer.Serialize(state, _jsonOptions));
        }

        private void CopyStoryChoiceNotes(string oldChoice, string newChoice)
        {
            if (_currentStoryChapter is null ||
                string.Equals(oldChoice, newChoice, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var state = ReadStoryChoiceNoteState(_currentStoryChapter);
            if (!state.Choices.TryGetValue(oldChoice, out var oldNotes) || oldNotes.Count == 0)
            {
                return;
            }

            if (!state.Choices.TryGetValue(newChoice, out var newNotes) || newNotes.Count == 0)
            {
                state.Choices[newChoice] = oldNotes.ToList();
            }

            WriteStoryChoiceNoteState(_currentStoryChapter, state);
        }

        private static string GetStoryChoiceNotesPath(ChapterInfo chapter)
        {
            return Path.Combine(chapter.Path, StoryChoiceNotesFileName);
        }

        private void RemoveUnusedStoryChoiceNotes(IEnumerable<string> removedFunctions)
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            var removedChoices = removedFunctions
                .Where(IsCurrentStoryChoiceFunctionValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (removedChoices.Count == 0)
            {
                return;
            }

            var state = ReadStoryChoiceNoteState(_currentStoryChapter);
            var changed = false;
            foreach (var choice in removedChoices)
            {
                if (StoryChoiceExistsInRows(choice))
                {
                    continue;
                }

                changed |= state.Choices.Remove(choice);
            }

            if (changed)
            {
                WriteStoryChoiceNoteState(_currentStoryChapter, state);
            }
        }

        private bool StoryChoiceExistsInRows(string choice)
        {
            return _storyRows.Any(row => SplitStoryFunctionValues(row.Get("Custom"))
                .Any(function => string.Equals(function, choice, StringComparison.OrdinalIgnoreCase)));
        }

        private async Task<int?> ChooseStorySoundEffectIndexAsync()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return null;
            }

            var choices = GetAudioFilePaths(GetSoundEffectFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryObjectChoice(index.ToString(), $"{index}: {Path.GetFileNameWithoutExtension(path)}", index))
                .ToList();
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有特殊音效", "请先在素材库里导入特殊音效。");
                return null;
            }

            var selected = await ShowStoryChoiceDialogAsync("选择特殊音效", choices);
            return selected is int index ? index : null;
        }

        private async Task ChooseStoryAssetIndexAsync(string title, string fieldName, List<StoryAssetChoice> choices)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可用素材", "当前项目绑定的素材库里没有对应素材。");
                return;
            }

            var listView = new ListView
            {
                Width = 420,
                MaxHeight = 420,
                SelectionMode = ListViewSelectionMode.Single
            };
            var currentIndex = ParseInt(_storyRows[_currentStoryRowIndex].Get(fieldName));
            foreach (var choice in choices)
            {
                var item = new ListViewItem
                {
                    Content = $"{choice.Index}: {choice.Name}",
                    Tag = choice.Index
                };
                listView.Items.Add(item);
                if (choice.Index == currentIndex)
                {
                    listView.SelectedItem = item;
                }
            }

            listView.SelectedItem ??= listView.Items.FirstOrDefault();

            var dialog = new ContentDialog
            {
                Title = title,
                Content = listView,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary ||
                listView.SelectedItem is not ListViewItem { Tag: int selectedIndex })
            {
                return;
            }

            if (selectedIndex == currentIndex)
            {
                return;
            }

            CaptureStoryUndoState($"更换{GetStoryAssetFieldDisplayName(fieldName)}");
            _storyRows[_currentStoryRowIndex].Set(fieldName, selectedIndex.ToString());
            PersistCurrentStoryRowsToFiles();
            UpdateStoryToolbarCurrentInfo();
            await RefreshStoryPreviewAsync();
            if (fieldName == "BGM")
            {
                await ApplyCurrentStoryRowMediaAndFunctionAsync();
            }
            else if (fieldName == "Scene")
            {
                await PlayCurrentStorySceneFromUiAsync();
            }
        }

        private void StorySidePanel_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
        }

        private void StorySidePanel_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
        }

        private void ToggleStorySettingsPaneButton_Click(object sender, RoutedEventArgs e)
        {
            var shouldOpen = StorySettingsPane.Width <= 46;
            _storyPaneAnimationTargetWidth = shouldOpen ? 380 : 46;
            _storyPaneAnimationTimer.Stop();
            _storyPaneAnimationTimer.Start();
        }

        private void StoryPaneAnimationTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            var currentWidth = StorySettingsPane.Width;
            var delta = _storyPaneAnimationTargetWidth - currentWidth;
            if (Math.Abs(delta) < 1)
            {
                StorySettingsPane.Width = _storyPaneAnimationTargetWidth;
                StorySidePanelContent.Opacity = StorySettingsPane.Width > 46 ? 1 : 0;
                sender.Stop();
                return;
            }

            StorySettingsPane.Width = currentWidth + delta * 0.28;
            var openProgress = Math.Clamp((StorySettingsPane.Width - 46) / (380 - 46), 0, 1);
            StorySidePanelContent.Opacity = openProgress;
        }

        private void StorySlotVisibilityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStoryCharacterSlotLayout();
            _storyCharacterPreviewKeys.Clear();
            _ = RefreshStoryPreviewAsync();
        }

        private void StoryCharacterDisplayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _storyCharacterPreviewKeys.Clear();
            _ = RefreshStoryPreviewAsync();
        }

        private void StoryCharacterSlot_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border slot)
            {
                if (slot.Tag is int slotIndex)
                {
                    _hoveredStoryCharacterSlotIndex = slotIndex;
                    ToolTipService.SetToolTip(slot, "《F12打开快捷键提示》");
                    StoryEditorPage.Focus(FocusState.Programmatic);
                }

                slot.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                slot.BorderThickness = new Thickness(1);
                slot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 255, 255, 255));
            }
        }

        private void StoryCharacterSlot_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border slot)
            {
                if (slot.Tag is int slotIndex && _hoveredStoryCharacterSlotIndex == slotIndex)
                {
                    _hoveredStoryCharacterSlotIndex = null;
                }

                slot.BorderBrush = null;
                slot.BorderThickness = new Thickness(0);
                slot.Background = ReferenceEquals(slot, StorySpeakerPreviewSlot)
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(34, 255, 255, 255))
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        private void StoryAssetShortcut_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string fieldName)
            {
                _hoveredStoryAssetFieldName = fieldName;
                ToolTipService.SetToolTip(element, "《F12打开快捷键提示》");
                StoryEditorPage.Focus(FocusState.Programmatic);
            }
        }

        private void StoryAssetShortcut_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string fieldName } &&
                string.Equals(_hoveredStoryAssetFieldName, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                _hoveredStoryAssetFieldName = null;
            }
        }

        private async void StoryEditorPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.F12)
            {
                await ShowStoryShortcutHelpDialogAsync();
                e.Handled = true;
                return;
            }

            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (IsControlKeyDown())
            {
                if (e.Key == Windows.System.VirtualKey.Z)
                {
                    UndoStoryEditorOperation();
                    e.Handled = true;
                    return;
                }

                if (_hoveredStoryCharacterSlotIndex is int hoveredSlotIndex)
                {
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.C:
                            CopyStoryCharacterSlot(hoveredSlotIndex);
                            e.Handled = true;
                            return;
                        case Windows.System.VirtualKey.V:
                            await PasteStoryCharacterSlotAsync(hoveredSlotIndex);
                            e.Handled = true;
                            return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(_hoveredStoryAssetFieldName))
                {
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.C:
                            CopyStoryAssetField(_hoveredStoryAssetFieldName);
                            e.Handled = true;
                            return;
                        case Windows.System.VirtualKey.V:
                            await PasteStoryAssetFieldAsync(_hoveredStoryAssetFieldName);
                            e.Handled = true;
                            return;
                    }
                }
            }

            if (_hoveredStoryCharacterSlotIndex is not int slotIndex)
            {
                return;
            }

            var handled = true;
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Q:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Adorn", "AD_Adorn", "装饰", -1);
                    break;
                case Windows.System.VirtualKey.E:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Adorn", "AD_Adorn", "装饰", 1);
                    break;
                case Windows.System.VirtualKey.A:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Face", "FC_Face", "表情", -1);
                    break;
                case Windows.System.VirtualKey.D:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Face", "FC_Face", "表情", 1);
                    break;
                case Windows.System.VirtualKey.Z:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Body", "DN_Cloth", "服装", -1);
                    break;
                case Windows.System.VirtualKey.C:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Body", "DN_Cloth", "服装", 1);
                    break;
                case Windows.System.VirtualKey.NumberPad4:
                case Windows.System.VirtualKey.Left:
                    await CycleStoryCharacterAsync(slotIndex, -1);
                    break;
                case Windows.System.VirtualKey.NumberPad6:
                case Windows.System.VirtualKey.Right:
                    await CycleStoryCharacterAsync(slotIndex, 1);
                    break;
                case Windows.System.VirtualKey.NumberPad8:
                case Windows.System.VirtualKey.Up:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Vfx", "VFX", "滤镜", 1);
                    break;
                case Windows.System.VirtualKey.NumberPad2:
                case Windows.System.VirtualKey.Down:
                    await CycleStoryCharacterLayerAsync(slotIndex, "Vfx", "VFX", "滤镜", -1);
                    break;
                case Windows.System.VirtualKey.Tab:
                    ClearStoryCharacterSlot(slotIndex);
                    break;
                default:
                    handled = false;
                    break;
            }

            e.Handled = handled;
        }

        private static bool IsControlKeyDown()
        {
            return InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        }

        private void CopyStoryCharacterSlot(int slotIndex)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyCharacterSlotClipboard = new StoryCharacterSlotClipboard(
                row.Get(GetStoryCharacterColumn(slotIndex)),
                row.Get(GetStoryLayerColumn(slotIndex, "Body")),
                row.Get(GetStoryLayerColumn(slotIndex, "Face")),
                row.Get(GetStoryLayerColumn(slotIndex, "Adorn")),
                row.Get(GetStoryLayerColumn(slotIndex, "Vfx")));
            ShowStoryStatus(InfoBarSeverity.Success, "已复制立绘数据", $"{GetStorySlotDisplayName(slotIndex)}：{FormatStoryCharacterSlotClipboard(_storyCharacterSlotClipboard)}");
        }

        private async Task PasteStoryCharacterSlotAsync(int slotIndex)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (_storyCharacterSlotClipboard is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可粘贴数据", "先悬停一个立绘位并按 Ctrl+C 复制。");
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (string.Equals(row.Get(GetStoryCharacterColumn(slotIndex)), _storyCharacterSlotClipboard.Character, StringComparison.Ordinal) &&
                string.Equals(row.Get(GetStoryLayerColumn(slotIndex, "Body")), _storyCharacterSlotClipboard.Body, StringComparison.Ordinal) &&
                string.Equals(row.Get(GetStoryLayerColumn(slotIndex, "Face")), _storyCharacterSlotClipboard.Face, StringComparison.Ordinal) &&
                string.Equals(row.Get(GetStoryLayerColumn(slotIndex, "Adorn")), _storyCharacterSlotClipboard.Adorn, StringComparison.Ordinal) &&
                string.Equals(row.Get(GetStoryLayerColumn(slotIndex, "Vfx")), _storyCharacterSlotClipboard.Vfx, StringComparison.Ordinal))
            {
                return;
            }

            CaptureStoryUndoState($"粘贴{GetStorySlotDisplayName(slotIndex)}立绘数据");
            row.Set(GetStoryCharacterColumn(slotIndex), _storyCharacterSlotClipboard.Character);
            row.Set(GetStoryLayerColumn(slotIndex, "Body"), _storyCharacterSlotClipboard.Body);
            row.Set(GetStoryLayerColumn(slotIndex, "Face"), _storyCharacterSlotClipboard.Face);
            row.Set(GetStoryLayerColumn(slotIndex, "Adorn"), _storyCharacterSlotClipboard.Adorn);
            row.Set(GetStoryLayerColumn(slotIndex, "Vfx"), _storyCharacterSlotClipboard.Vfx);
            NormalizeStoryDetachedCharacterLayers(row);
            SyncStorySpeakerTextBoxIfNeeded(slotIndex, row.Get(GetStoryCharacterColumn(slotIndex)));
            PersistCurrentStoryRowsToFiles();
            ShowStoryStatus(InfoBarSeverity.Success, "已粘贴立绘数据", $"{GetStorySlotDisplayName(slotIndex)}：{FormatStoryCharacterSlotClipboard(_storyCharacterSlotClipboard)}");
            await RefreshStoryPreviewAsync();
        }

        private static string FormatStoryCharacterSlotClipboard(StoryCharacterSlotClipboard data)
        {
            var character = string.IsNullOrWhiteSpace(data.Character) ? "无角色" : data.Character;
            return $"{character} / 服装 {data.Body} / 表情 {data.Face} / 装饰 {data.Adorn} / 滤镜 {data.Vfx}";
        }

        private void CopyStoryAssetField(string fieldName)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var value = row.Get(fieldName);
            _storyAssetClipboard = new StoryAssetClipboard(fieldName, value);
            ShowStoryStatus(InfoBarSeverity.Success, "已复制基础素材", $"{GetStoryAssetFieldDisplayName(fieldName)}：{value}");
        }

        private async Task PasteStoryAssetFieldAsync(string fieldName)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            if (_storyAssetClipboard is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可粘贴数据", "先悬停基础素材并按 Ctrl+C 复制。");
                return;
            }

            if (!string.Equals(_storyAssetClipboard.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "素材类型不一致", $"复制的是{GetStoryAssetFieldDisplayName(_storyAssetClipboard.FieldName)}，当前悬停的是{GetStoryAssetFieldDisplayName(fieldName)}。");
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (string.Equals(row.Get(fieldName), _storyAssetClipboard.Value, StringComparison.Ordinal))
            {
                return;
            }

            CaptureStoryUndoState($"粘贴{GetStoryAssetFieldDisplayName(fieldName)}");
            row.Set(fieldName, _storyAssetClipboard.Value);
            PersistCurrentStoryRowsToFiles();
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(InfoBarSeverity.Success, "已粘贴基础素材", $"{GetStoryAssetFieldDisplayName(fieldName)}：{_storyAssetClipboard.Value}");
            if (fieldName == "BGM")
            {
                await ApplyCurrentStoryRowMediaAndFunctionAsync();
            }
            else if (fieldName == "Scene")
            {
                await PlayCurrentStorySceneFromUiAsync();
            }

            await RefreshStoryPreviewAsync();
        }

        private static string GetStoryAssetFieldDisplayName(string fieldName)
        {
            return fieldName switch
            {
                "BGindex" => "背景图",
                "BGM" => "BGM",
                "Scene" => "环境音",
                _ => "基础素材"
            };
        }

        private async Task ShowStoryShortcutHelpDialogAsync()
        {
            var panel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    CreateShortcutHelpText("Q / E", "切换装饰"),
                    CreateShortcutHelpText("A / D", "切换表情"),
                    CreateShortcutHelpText("Z / C", "切换服装"),
                    CreateShortcutHelpText("小键盘 4 / 6 或方向键 ← / →", "切换角色"),
                    CreateShortcutHelpText("小键盘 8 / 2 或方向键 ↑ / ↓", "切换滤镜"),
                    CreateShortcutHelpText("鼠标侧键", "上一句 / 下一句"),
                    CreateShortcutHelpText("Tab", "清空悬停立绘位"),
                    CreateShortcutHelpText("Ctrl+Z", "撤回上一步编辑"),
                    CreateShortcutHelpText("Ctrl+C / Ctrl+V", "复制/粘贴悬停立绘位或基础素材")
                }
            };

            var dialog = new ContentDialog
            {
                Title = "快捷键提示",
                Content = panel,
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private static TextBlock CreateShortcutHelpText(string keys, string description)
        {
            return new TextBlock
            {
                Text = $"{keys}：{description}",
                TextWrapping = TextWrapping.Wrap
            };
        }

        private async Task CycleStoryCharacterLayerAsync(int slotIndex, string fieldPrefix, string folderName, string title, int delta)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var character = ResolveStoryCharacter(row.Get(GetStoryCharacterColumn(slotIndex)));
            if (character is null)
            {
                return;
            }

            if (fieldPrefix == "Vfx")
            {
                var filters = GetStoryCharacterFilters();
                if (filters.Count == 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, "没有滤镜", "当前素材库还没有角色滤镜。");
                    return;
                }

                var filterColumn = GetStoryLayerColumn(slotIndex, fieldPrefix);
                var filterCurrentIndex = ParseInt(row.Get(filterColumn));
                var nextIndex = ((filterCurrentIndex + delta) % filters.Count + filters.Count) % filters.Count;
                if (nextIndex == filterCurrentIndex)
                {
                    return;
                }

                CaptureStoryUndoState($"快捷切换{GetStorySlotDisplayName(slotIndex)}{title}");
                row.Set(filterColumn, nextIndex.ToString());
                PersistCurrentStoryRowsToFiles();
                ShowStoryLayerChangedStatus(slotIndex, title, nextIndex, GetCharacterFilterDisplayName(filters[nextIndex], nextIndex));
                await RefreshStoryPreviewAsync();
                return;
            }

            var folderPath = Path.Combine(character.Path, folderName);
            var paths = GetStoryCharacterLayerChoicePaths(folderPath, fieldPrefix);
            var layerColumn = GetStoryLayerColumn(slotIndex, fieldPrefix);
            var layerCurrentIndex = ParseInt(row.Get(layerColumn));
            var validIndexes = GetStoryCompatibleLayerIndexes(character, fieldPrefix, paths, row, slotIndex);
            if (validIndexes.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"没有{title}", $"角色 {character.Name} 还没有可用的{title}素材。");
                return;
            }

            var currentPosition = validIndexes.IndexOf(layerCurrentIndex);
            var layerNextIndex = currentPosition < 0
                ? validIndexes[0]
                : validIndexes[(currentPosition + delta + validIndexes.Count) % validIndexes.Count];
            if (layerNextIndex == layerCurrentIndex)
            {
                return;
            }

            CaptureStoryUndoState($"快捷切换{GetStorySlotDisplayName(slotIndex)}{title}");
            row.Set(layerColumn, layerNextIndex.ToString());
            if (fieldPrefix == "Body")
            {
                NormalizeStoryRowLayerCompatibility(row, character, slotIndex);
            }

            PersistCurrentStoryRowsToFiles();
            ShowStoryLayerChangedStatus(slotIndex, title, layerNextIndex, GetStoryLayerChoiceDisplayName(fieldPrefix, paths, layerNextIndex));
            await RefreshStoryPreviewAsync();
        }

        private async Task CycleStoryCharacterAsync(int slotIndex, int delta)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var characters = GetStoryCharactersByFolderOrder();
            if (characters.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var characterColumn = GetStoryCharacterColumn(slotIndex);
            var currentCharacter = ResolveStoryCharacter(row.Get(characterColumn));
            var currentIndex = currentCharacter is null
                ? -1
                : characters.FindIndex(character => string.Equals(character.Code, currentCharacter.Code, StringComparison.OrdinalIgnoreCase));
            var nextIndex = currentIndex < 0
                ? (delta > 0 ? 0 : characters.Count - 1)
                : ((currentIndex + delta) % characters.Count + characters.Count) % characters.Count;

            if (string.Equals(row.Get(characterColumn), characters[nextIndex].Code, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CaptureStoryUndoState($"快捷切换{GetStorySlotDisplayName(slotIndex)}角色");
            row.Set(characterColumn, characters[nextIndex].Code);
            ResetStoryCharacterLayerColumns(row, slotIndex);
            SyncStorySpeakerTextBoxIfNeeded(slotIndex, characters[nextIndex].Code);
            PersistCurrentStoryRowsToFiles();
            await RefreshStoryPreviewAsync();
        }

        private void ClearStoryCharacterSlot(int slotIndex)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (string.IsNullOrWhiteSpace(row.Get(GetStoryCharacterColumn(slotIndex))) &&
                ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Body"))) == 0 &&
                ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Face"))) == 0 &&
                ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Adorn"))) == 0 &&
                ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Vfx"))) == 0)
            {
                return;
            }

            CaptureStoryUndoState($"清空{GetStorySlotDisplayName(slotIndex)}立绘位");
            row.Set(GetStoryCharacterColumn(slotIndex), string.Empty);
            ResetStoryCharacterLayerColumns(row, slotIndex);
            SyncStorySpeakerTextBoxIfNeeded(slotIndex, string.Empty);
            PersistCurrentStoryRowsToFiles();
            _ = RefreshStoryPreviewAsync();
        }

        private void SyncStorySpeakerTextBoxIfNeeded(int slotIndex, string value)
        {
            if (slotIndex != 0)
            {
                return;
            }

            _isLoadingStoryRow = true;
            try
            {
                StorySpeakerTextBox.Text = value;
            }
            finally
            {
                _isLoadingStoryRow = false;
            }
        }

        private static void ResetStoryCharacterLayerColumns(StoryRow row, int slotIndex)
        {
            ResetStoryCharacterLayerColumnsIfNeeded(row, slotIndex);
        }

        private static bool ResetStoryCharacterLayerColumnsIfNeeded(StoryRow row, int slotIndex)
        {
            var changed = false;
            changed |= SetStoryCellIfChanged(row, GetStoryLayerColumn(slotIndex, "Body"), "0");
            changed |= SetStoryCellIfChanged(row, GetStoryLayerColumn(slotIndex, "Face"), "0");
            changed |= SetStoryCellIfChanged(row, GetStoryLayerColumn(slotIndex, "Adorn"), "0");
            changed |= SetStoryCellIfChanged(row, GetStoryLayerColumn(slotIndex, "Vfx"), "0");
            return changed;
        }

        private static bool SetStoryCellIfChanged(StoryRow row, string columnName, string value)
        {
            if (string.Equals(row.Get(columnName), value, StringComparison.Ordinal))
            {
                return false;
            }

            row.Set(columnName, value);
            return true;
        }

        private bool NormalizeStoryDetachedCharacterLayers(StoryRow row)
        {
            var changed = false;
            for (var slotIndex = 0; slotIndex <= 5; slotIndex++)
            {
                var characterValue = row.Get(GetStoryCharacterColumn(slotIndex));
                if (!ShouldClearDetachedStoryCharacterLayers(characterValue))
                {
                    continue;
                }

                changed |= ResetStoryCharacterLayerColumnsIfNeeded(row, slotIndex);
            }

            return changed;
        }

        private bool ShouldClearDetachedStoryCharacterLayers(string characterValue)
        {
            var trimmed = characterValue.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || ContainsCjk(trimmed))
            {
                return true;
            }

            return _currentStoryAssetLibrary is not null && ResolveStoryCharacter(trimmed) is null;
        }

        private static bool ContainsCjk(string value)
        {
            return value.Any(ch =>
                ch is >= '\u3400' and <= '\u4DBF' ||
                ch is >= '\u4E00' and <= '\u9FFF' ||
                ch is >= '\uF900' and <= '\uFAFF');
        }

        private static string GetStoryCharacterColumn(int slotIndex)
        {
            return slotIndex == 0 ? "TalkChar" : $"Chara{slotIndex}";
        }

        private static string GetStoryLayerColumn(int slotIndex, string fieldPrefix)
        {
            return slotIndex == 0 ? $"Talk{fieldPrefix}" : $"{fieldPrefix}{slotIndex}";
        }

        private void ShowStoryLayerChangedStatus(int slotIndex, string title, int index, string displayName)
        {
            ShowStoryStatus(
                InfoBarSeverity.Success,
                $"已更换{title}",
                $"{GetStorySlotDisplayName(slotIndex)}：{displayName}（索引 {index}）");
        }

        private static string GetStorySlotDisplayName(int slotIndex)
        {
            return slotIndex == 0 ? "当前说话人" : $"{slotIndex}号位";
        }

        private void UpdateStoryCharacterSlotLayout()
        {
            if (StoryCharacterSlot1 is null ||
                StoryCharacterSlot2 is null ||
                StoryCharacterSlot3 is null ||
                StoryCharacterSlot4 is null ||
                StoryCharacterSlot5 is null)
            {
                return;
            }

            var visibleSlots = new List<Border>
            {
                StoryCharacterSlot1,
                StoryCharacterSlot2,
                StoryCharacterSlot3
            };

            var showSlot4 = ShowCharacterSlot4CheckBox?.IsChecked == true;
            var showSlot5 = ShowCharacterSlot5CheckBox?.IsChecked == true;
            StoryCharacterSlot4.Visibility = showSlot4 ? Visibility.Visible : Visibility.Collapsed;
            StoryCharacterSlot5.Visibility = showSlot5 ? Visibility.Visible : Visibility.Collapsed;

            if (showSlot4)
            {
                visibleSlots.Add(StoryCharacterSlot4);
            }

            if (showSlot5)
            {
                visibleSlots.Add(StoryCharacterSlot5);
            }

            var count = visibleSlots.Count;
            var columns = count switch
            {
                3 => new[] { 0, 3, 6 },
                4 => new[] { 0, 2, 5, 7 },
                _ => Enumerable.Range(0, count).Select(i => i * 2).ToArray()
            };

            var spans = count switch
            {
                3 => new[] { 4, 4, 4 },
                4 => new[] { 3, 3, 3, 3 },
                _ => Enumerable.Repeat(2, count).ToArray()
            };

            for (var i = 0; i < visibleSlots.Count; i++)
            {
                Grid.SetColumn(visibleSlots[i], Math.Min(9, columns[i]));
                Grid.SetColumnSpan(visibleSlots[i], spans[i]);
                visibleSlots[i].Margin = count <= 3
                    ? new Thickness(4, -24, 4, -44)
                    : count == 4
                        ? new Thickness(4, -10, 4, -24)
                        : new Thickness(6, 16 + (i % 2 == 0 ? 0 : 22), 6, 146);
                visibleSlots[i].VerticalAlignment = VerticalAlignment.Stretch;
                visibleSlots[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }

        private async Task PlayCurrentStoryBgmFromUiAsync()
        {
            if (IsCurrentStoryFunction("BGMSTOP"))
            {
                _storyBgmPlaybackSuppressed = true;
                PauseStoryBgm();
                return;
            }

            if (_storyBgmPlaybackSuppressed)
            {
                return;
            }

            var requestId = ++_storyBgmPlaybackRequestId;
            try
            {
                await PlayCurrentStoryBgmAsync(requestId);
            }
            catch (Exception ex)
            {
                ShowStoryStatus(InfoBarSeverity.Error, "BGM播放失败", ex.Message);
                AppendLog(LogKind.Error, "故事编辑器 BGM 播放失败。", ex);
            }
        }

        private async Task ApplyCurrentStoryRowMediaAndFunctionAsync()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var functionValue = _storyRows[_currentStoryRowIndex].Get("Custom").Trim();
            foreach (var function in SplitStoryFunctionValues(functionValue))
            {
                if (TryParseStoryBackgroundTransitionMode(function, out var transitionMode))
                {
                    _storyBackgroundTransitionMode = transitionMode;
                }
            }

            if (StoryFunctionContains(functionValue, "BGMSTOP"))
            {
                _storyBgmPlaybackSuppressed = true;
                PauseStoryBgm();
                ShowStoryFunctionTriggeredStatus(functionValue);
                await PlayCurrentStorySceneFromUiAsync();
                return;
            }

            await PlayCurrentStoryBgmFromUiAsync();
            await PlayCurrentStorySceneFromUiAsync();
            if (!string.IsNullOrWhiteSpace(functionValue))
            {
                await PreviewStoryFunctionAsync(functionValue);
            }
        }

        private async Task PreviewStoryFunctionAsync(string functionValue)
        {
            ShowStoryFunctionTriggeredStatus(functionValue);
            if (StoryFunctionContains(functionValue, "BGMSTART"))
            {
                _storyBgmPlaybackSuppressed = false;
                await PlayCurrentStoryBgmFromUiAsync();
            }
        }

        private static string NormalizeStoryFunctionKey(string functionValue)
        {
            return functionValue.Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        }

        private bool IsCurrentStoryFunction(string normalizedFunctionKey)
        {
            return _storyRows.Count > 0 &&
                StoryFunctionContains(_storyRows[_currentStoryRowIndex].Get("Custom"), normalizedFunctionKey);
        }

        private static bool StoryFunctionContains(string functionValue, string normalizedFunctionKey)
        {
            if (string.IsNullOrWhiteSpace(functionValue))
            {
                return false;
            }

            return EnumerateStoryFunctionKeys(functionValue)
                .Any(key => string.Equals(key, normalizedFunctionKey, StringComparison.Ordinal));
        }

        private static IEnumerable<string> EnumerateStoryFunctionKeys(string functionValue)
        {
            var parts = functionValue.Split(
                ['/', '\\', '|', ';', '；', ',', '，', '\r', '\n', '\t', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                yield return NormalizeStoryFunctionKey(functionValue);
                yield break;
            }

            foreach (var part in parts)
            {
                yield return NormalizeStoryFunctionKey(part);
            }
        }

        private void ShowStoryFunctionTriggeredStatus(string functionValue)
        {
            ClearStoryFunctionTips();
            foreach (var functionName in EnumerateStoryFunctionDisplayNames(functionValue))
            {
                AddStoryTipWithEntrance(
                    StoryFunctionTipsPanel,
                    CreateStoryTipBar(InfoBarSeverity.Success, $"触发函数：{functionName}", string.Empty));
            }
        }

        private void ClearStoryFunctionTips()
        {
            StoryFunctionTipsPanel.Children.Clear();
        }

        private void ClearStoryTransientTips()
        {
            foreach (var timer in _storyTransientTipTimers.Values)
            {
                timer.Stop();
            }

            _storyTransientTipTimers.Clear();
            StoryFloatingTipsPanel.Children.Clear();
            StoryTransientTipsPanel.Children.Clear();
        }

        private InfoBar CreateStoryTipBar(InfoBarSeverity severity, string title, string message)
        {
            return new InfoBar
            {
                Severity = severity == InfoBarSeverity.Error ? InfoBarSeverity.Error : InfoBarSeverity.Success,
                Title = title,
                Message = message,
                IsOpen = true,
                IsClosable = true,
                RenderTransform = new TranslateTransform { Y = -18 },
                Opacity = 0
            };
        }

        private void AddStoryTipWithEntrance(Panel panel, InfoBar tip)
        {
            panel.Children.Add(tip);
            var steps = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (_, _) =>
            {
                steps++;
                var progress = Math.Min(1, steps / 12.0);
                var eased = 1 - Math.Pow(1 - progress, 3);
                tip.Opacity = eased;
                if (tip.RenderTransform is TranslateTransform transform)
                {
                    transform.Y = -18 + 18 * eased;
                }

                if (progress >= 1)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        private static IEnumerable<string> EnumerateStoryFunctionDisplayNames(string functionValue)
        {
            var parts = SplitStoryFunctionValues(functionValue).ToArray();
            if (parts.Length == 0 && !string.IsNullOrWhiteSpace(functionValue))
            {
                yield return functionValue.Trim();
                yield break;
            }

            foreach (var part in parts)
            {
                if (TryParseStoryBackgroundTransitionMode(part, out var transitionMode))
                {
                    yield return GetStoryBackgroundTransitionModeRemark(transitionMode);
                    continue;
                }

                yield return part;
            }
        }

        private static IEnumerable<string> SplitStoryFunctionValues(string functionValue)
        {
            return string.IsNullOrWhiteSpace(functionValue)
                ? []
                : functionValue.Split(
                    ['/'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private void RebuildStoryPersistentFunctionState(int rowIndex)
        {
            _storyBgmPlaybackSuppressed = HasPreviousBgmStopWithoutStart(rowIndex);
            _storyBackgroundTransitionMode = GetStoryBackgroundTransitionMode(rowIndex);
            if (_storyBgmPlaybackSuppressed)
            {
                PauseStoryBgm();
            }
        }

        private bool HasPreviousBgmStopWithoutStart(int rowIndex)
        {
            var suppressed = false;
            for (var i = 0; i <= rowIndex && i < _storyRows.Count; i++)
            {
                var functionValue = _storyRows[i].Get("Custom");
                if (StoryFunctionContains(functionValue, "BGMSTOP"))
                {
                    suppressed = true;
                }
                else if (StoryFunctionContains(functionValue, "BGMSTART"))
                {
                    suppressed = false;
                }
            }

            return suppressed;
        }

        private int GetStoryBackgroundTransitionMode(int rowIndex)
        {
            var mode = 0;
            for (var i = 0; i <= rowIndex && i < _storyRows.Count; i++)
            {
                foreach (var functionValue in SplitStoryFunctionValues(_storyRows[i].Get("Custom")))
                {
                    if (TryParseStoryBackgroundTransitionMode(functionValue, out var parsedMode))
                    {
                        mode = parsedMode;
                    }
                }
            }

            return mode;
        }

        private static bool TryParseStoryBackgroundTransitionMode(string functionValue, out int mode)
        {
            var match = Regex.Match(functionValue.Trim(), @"^BGLerpMode_(?<mode>\d+)$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["mode"].Value, out mode))
            {
                mode = Math.Clamp(mode, 0, 2);
                return true;
            }

            mode = 0;
            return false;
        }

        private static string GetStoryBackgroundTransitionModeDisplay(int mode)
        {
            return mode switch
            {
                1 => "1：正常黑屏转场",
                2 => "2：背景图渐变过渡",
                _ => "0：游戏入场黑屏"
            };
        }

        private static string GetStoryBackgroundTransitionModeRemark(int mode)
        {
            return mode switch
            {
                1 => "正常黑屏转场",
                2 => "背景图渐变过渡",
                _ => "游戏入场黑屏"
            };
        }

        private async Task PlayCurrentStoryBgmAsync(int requestId)
        {
            if (_storyBgmPlaybackSuppressed || IsCurrentStoryFunction("BGMSTOP"))
            {
                PauseStoryBgm();
                return;
            }

            if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
            {
                StopStoryBgm();
                ShowStoryStatus(InfoBarSeverity.Warning, "BGM未播放", "当前项目没有可用的绑定素材库。");
                return;
            }

            var bgmPaths = GetMusicFilePaths(GetMusicFolderPath(_currentStoryAssetLibrary));
            var rawIndex = ParseInt(_storyRows[_currentStoryRowIndex].Get("BGM"));
            var index = ResolveStoryAssetIndex(rawIndex, bgmPaths.Count);
            if (index is null)
            {
                StopStoryBgm();
                ShowStoryStatus(InfoBarSeverity.Warning, "BGM未播放", $"BGM 索引 {rawIndex} 没有匹配到素材。");
                return;
            }

            var bgmPath = bgmPaths[index.Value];
            if (!string.IsNullOrWhiteSpace(_storyBgmPath) &&
                PathsEqual(_storyBgmPath, bgmPath) &&
                _storyBgmPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                return;
            }

            _storyBgmPath = bgmPath;
            var file = await StorageFile.GetFileFromPathAsync(bgmPath);
            if (requestId != _storyBgmPlaybackRequestId)
            {
                return;
            }

            if (_storyBgmPlaybackSuppressed || IsCurrentStoryFunction("BGMSTOP"))
            {
                PauseStoryBgm();
                return;
            }

            _storyBgmPlayer.Source = MediaSource.CreateFromStorageFile(file);
            _storyBgmPlayer.Play();
            AppendLog(LogKind.Info, $"故事编辑器播放 BGM：索引 {rawIndex} -> {Path.GetFileName(bgmPath)}");
        }

        private void StopStoryBgm()
        {
            _storyBgmPlayer.Pause();
            _storyBgmPlayer.Source = null;
            _storyBgmPath = null;
        }

        private void PauseStoryBgm()
        {
            _storyBgmPlaybackRequestId++;
            _storyBgmPlayer.Pause();
        }

        private async Task PlayCurrentStorySceneFromUiAsync()
        {
            var requestId = ++_storyScenePlaybackRequestId;
            try
            {
                await PlayCurrentStorySceneAsync(requestId);
            }
            catch (Exception ex)
            {
                ShowStoryStatus(InfoBarSeverity.Error, "环境音播放失败", ex.Message);
                AppendLog(LogKind.Error, "故事编辑器环境音播放失败。", ex);
            }
        }

        private async Task PlayCurrentStorySceneAsync(int requestId)
        {
            if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
            {
                StopStoryScene();
                return;
            }

            var scenePaths = GetAudioFilePaths(GetAmbientSoundFolderPath(_currentStoryAssetLibrary));
            var rawIndex = ParseInt(_storyRows[_currentStoryRowIndex].Get("Scene"));
            var index = ResolveStoryAssetIndex(rawIndex, scenePaths.Count);
            if (index is null)
            {
                StopStoryScene();
                if (rawIndex > 0 || scenePaths.Count > 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, "环境音未播放", $"Scene 索引 {rawIndex} 没有匹配到素材。");
                }
                return;
            }

            var scenePath = scenePaths[index.Value];
            if (!string.IsNullOrWhiteSpace(_storyScenePath) &&
                PathsEqual(_storyScenePath, scenePath) &&
                _storyScenePlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                return;
            }

            _storyScenePath = scenePath;
            var file = await StorageFile.GetFileFromPathAsync(scenePath);
            if (requestId != _storyScenePlaybackRequestId)
            {
                return;
            }

            _storyScenePlayer.Source = MediaSource.CreateFromStorageFile(file);
            _storyScenePlayer.Play();
            AppendLog(LogKind.Info, $"故事编辑器播放环境音：索引 {rawIndex} -> {Path.GetFileName(scenePath)}");
        }

        private void StopStoryScene()
        {
            _storyScenePlayer.Pause();
            _storyScenePlayer.Source = null;
            _storyScenePath = null;
        }

        private void StopStoryEditorAudio()
        {
            _storyBgmPlaybackRequestId++;
            _storyScenePlaybackRequestId++;
            _storyBgmPlaybackSuppressed = false;
            StopStoryBgm();
            StopStoryScene();
        }

        private async Task RefreshStoryPreviewAsync()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var normalizedLayers = false;
            normalizedLayers |= NormalizeStoryDetachedCharacterLayers(row);
            for (var slotIndex = 0; slotIndex <= 5; slotIndex++)
            {
                var character = ResolveStoryCharacter(row.Get(GetStoryCharacterColumn(slotIndex)));
                if (character is not null)
                {
                    normalizedLayers |= NormalizeStoryRowLayerCompatibility(row, character, slotIndex);
                }
            }

            if (normalizedLayers && _currentStoryCsvPath is not null)
            {
                PersistCurrentStoryRowsToFiles();
            }

            await SetStoryBackgroundAsync(ParseInt(row.Get("BGindex")));
            await SetStoryCharacterSlotAsync(StoryCharacterSlot1, 1, row.Get("Chara1"), ParseInt(row.Get("Body1")), ParseInt(row.Get("Face1")), ParseInt(row.Get("Adorn1")), ParseInt(row.Get("Vfx1")));
            await SetStoryCharacterSlotAsync(StoryCharacterSlot2, 2, row.Get("Chara2"), ParseInt(row.Get("Body2")), ParseInt(row.Get("Face2")), ParseInt(row.Get("Adorn2")), ParseInt(row.Get("Vfx2")));
            await SetStoryCharacterSlotAsync(StoryCharacterSlot3, 3, row.Get("Chara3"), ParseInt(row.Get("Body3")), ParseInt(row.Get("Face3")), ParseInt(row.Get("Adorn3")), ParseInt(row.Get("Vfx3")));
            await SetStoryCharacterSlotAsync(StoryCharacterSlot4, 4, row.Get("Chara4"), ParseInt(row.Get("Body4")), ParseInt(row.Get("Face4")), ParseInt(row.Get("Adorn4")), ParseInt(row.Get("Vfx4")));
            await SetStoryCharacterSlotAsync(StoryCharacterSlot5, 5, row.Get("Chara5"), ParseInt(row.Get("Body5")), ParseInt(row.Get("Face5")), ParseInt(row.Get("Adorn5")), ParseInt(row.Get("Vfx5")));
            await SetStorySpeakerPreviewAsync(row.Get("TalkChar"), ParseInt(row.Get("TalkBody")), ParseInt(row.Get("TalkFace")), ParseInt(row.Get("TalkAdorn")), ParseInt(row.Get("TalkVfx")));
            UpdateStoryCharacterSlotLayout();
            UpdateStoryToolbarCurrentInfo();
            StoryAssetStatusText.Text = _currentStoryAssetLibrary is null
                ? "当前项目未关联素材库。"
                : $"素材库：{_currentStoryAssetLibrary.Name} | 立绘：{(ShowFullCharacterArtCheckBox?.IsChecked == true ? "完整" : "上半身")}";
        }

        private async Task SetStoryBackgroundAsync(int index)
        {
            if (_currentStoryAssetLibrary is null)
            {
                if (_storyBackgroundPreviewKey != string.Empty)
                {
                    StoryBackgroundImage.Source = null;
                    _storyBackgroundPreviewKey = string.Empty;
                }

                return;
            }

            var backgrounds = GetBackgroundImagePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary));
            var resolvedIndex = ResolveStoryAssetIndex(index, backgrounds.Count);
            if (resolvedIndex is null)
            {
                if (_storyBackgroundPreviewKey != string.Empty)
                {
                    StoryBackgroundImage.Source = null;
                    _storyBackgroundPreviewKey = string.Empty;
                }

                return;
            }

            var path = backgrounds[resolvedIndex.Value];
            var previousPreviewKey = _storyBackgroundPreviewKey;
            if (string.Equals(_storyBackgroundPreviewKey, path, StringComparison.OrdinalIgnoreCase) &&
                StoryBackgroundImage.Source is not null)
            {
                return;
            }

            _storyBackgroundPreviewKey = path;
            await LoadStoryPreviewImageAsync(StoryBackgroundImage, path);
            if (!string.IsNullOrWhiteSpace(previousPreviewKey) &&
                !string.Equals(previousPreviewKey, path, StringComparison.OrdinalIgnoreCase))
            {
                ShowStoryStatus(
                    InfoBarSeverity.Informational,
                    "背景切换模式",
                    GetStoryBackgroundTransitionModeRemark(_storyBackgroundTransitionMode));
            }
        }

        private async Task WarmStoryPreviewImageCacheAsync()
        {
            if (_currentStoryAssetLibrary is null || _storyRows.Count == 0)
            {
                return;
            }

            try
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var backgrounds = GetBackgroundImagePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary));
                foreach (var row in _storyRows)
                {
                    var backgroundIndex = ResolveStoryAssetIndex(ParseInt(row.Get("BGindex")), backgrounds.Count);
                    if (backgroundIndex is not null)
                    {
                        paths.Add(backgrounds[backgroundIndex.Value]);
                    }

                    for (var slotIndex = 0; slotIndex <= 5; slotIndex++)
                    {
                        var character = ResolveStoryCharacter(row.Get(GetStoryCharacterColumn(slotIndex)));
                        if (character is null)
                        {
                            continue;
                        }

                        var bodyPath = GetCharacterLayerPath(character, "DN_Cloth", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Body"))));
                        var facePath = GetCharacterLayerPath(character, "FC_Face", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Face"))));
                        var adornPath = GetCharacterLayerPath(character, "AD_Adorn", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Adorn"))));
                        foreach (var path in new[] { bodyPath, facePath, adornPath }.Where(IsPreviewableImagePath))
                        {
                            paths.Add(path!);
                        }
                    }
                }

                foreach (var path in paths)
                {
                    await GetCachedStoryPreviewImageAsync(path);
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Warning, "故事编辑器预热图片缓存失败。", ex);
            }
        }

        private async Task SetStoryCharacterSlotAsync(Border slot, int slotIndex, string characterName, int bodyIndex, int faceIndex, int adornIndex, int vfxIndex)
        {
            var grid = new Grid();
            slot.Tag = slotIndex;
            slot.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            slot.ContextFlyout = CreateStoryCharacterSlotMenu(slotIndex);
            slot.PointerEntered -= StoryCharacterSlot_PointerEntered;
            slot.PointerExited -= StoryCharacterSlot_PointerExited;
            slot.PointerEntered += StoryCharacterSlot_PointerEntered;
            slot.PointerExited += StoryCharacterSlot_PointerExited;
            ClipPreviewBorderToBounds(slot);
            var useUnifiedPreview = true;
            if (useUnifiedPreview)
            {
                await SetStoryCharacterPreviewAsync(
                    slot,
                    characterName,
                    bodyIndex,
                    faceIndex,
                    adornIndex,
                    vfxIndex,
                    $"{slotIndex}号位",
                    false);
                return;
            }
            slot.Child = grid;

            var label = new TextBlock
            {
                Text = $"{slotIndex}号位",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 12)
            };

            if (_currentStoryAssetLibrary is null || string.IsNullOrWhiteSpace(characterName))
            {
                grid.Children.Add(label);
                return;
            }

            var character = ResolveStoryCharacter(characterName);
            if (character is null)
            {
                grid.Children.Add(label);
                return;
            }

            var imagePaths = new[]
            {
                GetCharacterLayerPath(character, "DN_Cloth", bodyIndex),
                GetCharacterLayerPath(character, "FC_Face", faceIndex),
                GetCharacterLayerPath(character, "AD_Adorn", adornIndex),
                GetCharacterLayerPath(character, "VFX", vfxIndex)
            };

            foreach (var imagePath in imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var image = new Image
                {
                    Stretch = ShowFullCharacterArtCheckBox?.IsChecked == true ? Stretch.Uniform : Stretch.UniformToFill,
                    VerticalAlignment = ShowFullCharacterArtCheckBox?.IsChecked == true ? VerticalAlignment.Bottom : VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                image.Height = ShowFullCharacterArtCheckBox?.IsChecked == true ? double.NaN : 760;
                grid.Children.Add(image);
                await LoadThumbnailFromFileAsync(image, imagePath!);
            }

            grid.Children.Add(label);
        }

        private async Task SetStorySpeakerPreviewAsync(string characterName, int bodyIndex, int faceIndex, int adornIndex, int vfxIndex)
        {
            StorySpeakerPreviewSlot.Tag = 0;
            StorySpeakerPreviewSlot.ContextFlyout = CreateStorySpeakerSlotMenu();
            StorySpeakerPreviewSlot.PointerEntered -= StoryCharacterSlot_PointerEntered;
            StorySpeakerPreviewSlot.PointerExited -= StoryCharacterSlot_PointerExited;
            StorySpeakerPreviewSlot.PointerEntered += StoryCharacterSlot_PointerEntered;
            StorySpeakerPreviewSlot.PointerExited += StoryCharacterSlot_PointerExited;
            ClipPreviewBorderToBounds(StorySpeakerPreviewSlot);

            await SetStoryCharacterPreviewAsync(
                StorySpeakerPreviewSlot,
                characterName,
                bodyIndex,
                faceIndex,
                adornIndex,
                vfxIndex,
                "说话人",
                true);
        }

        private async Task SetStoryCharacterPreviewAsync(
            Border slot,
            string characterName,
            int bodyIndex,
            int faceIndex,
            int adornIndex,
            int vfxIndex,
            string positionLabel,
            bool compact)
        {
            var showFull = ShowFullCharacterArtCheckBox?.IsChecked == true;
            var previewSlotIndex = slot.Tag is int tagIndex ? tagIndex : -1;
            var previewKey = string.Join(
                "|",
                characterName,
                bodyIndex,
                faceIndex,
                adornIndex,
                vfxIndex,
                positionLabel,
                compact,
                showFull,
                GetVisibleStoryCharacterSlotCount());
            if (_storyCharacterPreviewKeys.TryGetValue(previewSlotIndex, out var existingKey) &&
                string.Equals(existingKey, previewKey, StringComparison.Ordinal) &&
                slot.Child is not null)
            {
                return;
            }

            _storyCharacterPreviewKeys[previewSlotIndex] = previewKey;
            var grid = new Grid();
            slot.Child = grid;

            var character = ResolveStoryCharacter(characterName);
            var displayName = character?.Name ?? (string.IsNullOrWhiteSpace(characterName) ? "无角色" : characterName);
            var filterName = character is null ? null : ResolveStoryCharacterFilterName(vfxIndex);
            var label = CreateStoryCharacterPreviewLabel(displayName, positionLabel, compact, filterName);

            if (_currentStoryAssetLibrary is null || string.IsNullOrWhiteSpace(characterName) || character is null)
            {
                grid.Children.Add(label);
                return;
            }

            var bodyPath = GetCharacterLayerPath(character, "DN_Cloth", bodyIndex);
            var facePath = GetCharacterLayerPath(character, "FC_Face", faceIndex);
            var adornPath = GetCharacterLayerPath(character, "AD_Adorn", adornIndex);
            var imagePaths = new[]
            {
                bodyPath,
                IsCharacterLayerCompatibleWithCloth(character, bodyPath, facePath) ? facePath : null,
                IsCharacterLayerCompatibleWithCloth(character, bodyPath, adornPath) ? adornPath : null
            };

            foreach (var imagePath in imagePaths.Where(IsPreviewableImagePath))
            {
                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = showFull ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    RenderTransformOrigin = showFull
                        ? new Windows.Foundation.Point(0.5, 1.0)
                        : new Windows.Foundation.Point(0.5, compact ? 0.22 : 0.3)
                };

                var scale = GetStoryCharacterPreviewScale(compact, showFull);
                if (Math.Abs(scale - 1) > 0.01)
                {
                    image.RenderTransform = new CompositeTransform
                    {
                        ScaleX = scale,
                        ScaleY = scale
                    };
                }

                grid.Children.Add(image);
                await LoadStoryPreviewImageAsync(image, imagePath!);
            }

            grid.Children.Add(label);
        }

        private static Border CreateStoryCharacterPreviewLabel(string displayName, string positionLabel, bool compact, string? filterName)
        {
            var text = string.IsNullOrWhiteSpace(filterName)
                ? $"{displayName}\n{positionLabel}"
                : $"{displayName}\n{positionLabel}\n{filterName}";
            return new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, compact ? 6 : 12),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(148, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    FontSize = compact ? 11 : 14,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        private int GetVisibleStoryCharacterSlotCount()
        {
            var count = 3;
            if (ShowCharacterSlot4CheckBox?.IsChecked == true)
            {
                count++;
            }

            if (ShowCharacterSlot5CheckBox?.IsChecked == true)
            {
                count++;
            }

            return count;
        }

        private double GetStoryCharacterPreviewScale(bool compact, bool showFull)
        {
            if (compact)
            {
                return showFull ? 1.75 : 4.4;
            }

            return GetVisibleStoryCharacterSlotCount() switch
            {
                <= 3 => showFull ? 2.05 : 3.15,
                4 => showFull ? 1.85 : 2.75,
                _ => showFull ? 1.7 : 2.45
            };
        }

        private static bool IsPreviewableImagePath(string? imagePath)
        {
            return !string.IsNullOrWhiteSpace(imagePath) &&
                File.Exists(imagePath) &&
                ImageExtensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase);
        }

        private static void ClipPreviewBorderToBounds(Border border)
        {
            border.SizeChanged -= StoryPreviewBorder_SizeChanged;
            border.SizeChanged += StoryPreviewBorder_SizeChanged;
            UpdatePreviewBorderClip(border, border.ActualWidth, border.ActualHeight);
        }

        private static void StoryPreviewBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Border border)
            {
                UpdatePreviewBorderClip(border, e.NewSize.Width, e.NewSize.Height);
            }
        }

        private static void UpdatePreviewBorderClip(Border border, double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            border.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, width, height)
            };
        }

        private MenuFlyout CreateStoryCharacterSlotMenu(int slotIndex)
        {
            var flyout = new MenuFlyout();

            var characterItem = new MenuFlyoutItem { Text = "角色" };
            characterItem.Click += async (_, _) => await ChooseStoryCharacterAsync(slotIndex);
            flyout.Items.Add(characterItem);

            var bodyItem = new MenuFlyoutItem { Text = "服装" };
            bodyItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, "Body", "DN_Cloth", "服装");
            flyout.Items.Add(bodyItem);

            var faceItem = new MenuFlyoutItem { Text = "表情" };
            faceItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, "Face", "FC_Face", "表情");
            flyout.Items.Add(faceItem);

            var adornItem = new MenuFlyoutItem { Text = "装饰" };
            adornItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, "Adorn", "AD_Adorn", "装饰");
            flyout.Items.Add(adornItem);

            var vfxItem = new MenuFlyoutItem { Text = "滤镜" };
            vfxItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, "Vfx", "VFX", "滤镜");
            flyout.Items.Add(vfxItem);

            return flyout;
        }

        private MenuFlyout CreateStorySpeakerSlotMenu()
        {
            var flyout = new MenuFlyout();

            var bodyItem = new MenuFlyoutItem { Text = "服装" };
            bodyItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(0, "Body", "DN_Cloth", "服装");
            flyout.Items.Add(bodyItem);

            var faceItem = new MenuFlyoutItem { Text = "表情" };
            faceItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(0, "Face", "FC_Face", "表情");
            flyout.Items.Add(faceItem);

            var adornItem = new MenuFlyoutItem { Text = "装饰" };
            adornItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(0, "Adorn", "AD_Adorn", "装饰");
            flyout.Items.Add(adornItem);

            var vfxItem = new MenuFlyoutItem { Text = "滤镜" };
            vfxItem.Click += async (_, _) => await ChooseStoryCharacterLayerAsync(0, "Vfx", "VFX", "滤镜");
            flyout.Items.Add(vfxItem);

            return flyout;
        }

        private async Task ChooseStoryCharacterAsync(int slotIndex)
        {
            if (_currentStoryAssetLibrary is null || _currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "无法选择角色", "当前项目没有可用的绑定素材库。");
                return;
            }

            var characters = GetStoryCharacters();
            if (characters.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有角色", "绑定素材库里还没有角色卡。");
            }

            var selected = await ShowStoryChoiceDialogAsync(
                "选择角色",
                new[] { new StoryObjectChoice(NoStoryCharacterChoice, "无角色", NoStoryCharacterChoice) }
                    .Concat(characters.Select(character => new StoryObjectChoice(character.Code, $"{character.Name} / {character.Code}", character)))
                    .ToList());
            if (selected is string noCharacter && string.Equals(noCharacter, NoStoryCharacterChoice, StringComparison.Ordinal))
            {
                var emptyRow = _storyRows[_currentStoryRowIndex];
                if (string.IsNullOrWhiteSpace(emptyRow.Get($"Chara{slotIndex}")) &&
                    ParseInt(emptyRow.Get($"Body{slotIndex}")) == 0 &&
                    ParseInt(emptyRow.Get($"Face{slotIndex}")) == 0 &&
                    ParseInt(emptyRow.Get($"Adorn{slotIndex}")) == 0 &&
                    ParseInt(emptyRow.Get($"Vfx{slotIndex}")) == 0)
                {
                    return;
                }

                CaptureStoryUndoState($"清空{GetStorySlotDisplayName(slotIndex)}角色");
                emptyRow.Set($"Chara{slotIndex}", string.Empty);
                emptyRow.Set($"Body{slotIndex}", "0");
                emptyRow.Set($"Face{slotIndex}", "0");
                emptyRow.Set($"Adorn{slotIndex}", "0");
                emptyRow.Set($"Vfx{slotIndex}", "0");
                PersistCurrentStoryRowsToFiles();
                await RefreshStoryPreviewAsync();
                return;
            }

            if (selected is not CharacterInfo character)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (string.Equals(row.Get($"Chara{slotIndex}"), character.Code, StringComparison.Ordinal) &&
                ParseInt(row.Get($"Body{slotIndex}")) == 0 &&
                ParseInt(row.Get($"Face{slotIndex}")) == 0 &&
                ParseInt(row.Get($"Adorn{slotIndex}")) == 0 &&
                ParseInt(row.Get($"Vfx{slotIndex}")) == 0)
            {
                return;
            }

            CaptureStoryUndoState($"更换{GetStorySlotDisplayName(slotIndex)}角色");
            row.Set($"Chara{slotIndex}", character.Code);
            row.Set($"Body{slotIndex}", "0");
            row.Set($"Face{slotIndex}", "0");
            row.Set($"Adorn{slotIndex}", "0");
            row.Set($"Vfx{slotIndex}", "0");
            PersistCurrentStoryRowsToFiles();
            await RefreshStoryPreviewAsync();
        }

        private async Task ChooseStoryCharacterLayerAsync(int slotIndex, string fieldPrefix, string folderName, string title)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var characterName = row.Get(GetStoryCharacterColumn(slotIndex));
            var character = ResolveStoryCharacter(characterName);
            if (character is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"无法选择{title}", slotIndex == 0 ? "请先填写当前说话人。" : "请先为这个位置选择角色。");
                return;
            }

            if (fieldPrefix == "Vfx")
            {
                var filters = GetStoryCharacterFilters();
                if (filters.Count == 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, "没有滤镜", "当前素材库还没有角色滤镜。");
                    return;
                }

                var selectedFilter = await ShowStoryChoiceDialogAsync(
                    $"选择{title}",
                    filters.Select((filter, index) => new StoryObjectChoice(index.ToString(), $"{index}: {filter.Remark}", index)).ToList());
                if (selectedFilter is not int selectedFilterIndex)
                {
                    return;
                }

                var filterColumn = GetStoryLayerColumn(slotIndex, fieldPrefix);
                if (ParseInt(row.Get(filterColumn)) == selectedFilterIndex)
                {
                    return;
                }

                CaptureStoryUndoState($"更换{GetStorySlotDisplayName(slotIndex)}{title}");
                row.Set(filterColumn, selectedFilterIndex.ToString());
                PersistCurrentStoryRowsToFiles();
                ShowStoryLayerChangedStatus(slotIndex, title, selectedFilterIndex, GetCharacterFilterDisplayName(filters[selectedFilterIndex], selectedFilterIndex));
                await RefreshStoryPreviewAsync();
                return;
            }

            var folderPath = Path.Combine(character.Path, folderName);
            var paths = GetStoryCharacterLayerChoicePaths(folderPath, fieldPrefix);
            var choices = CreateStoryLayerChoices(fieldPrefix, paths, character, row, slotIndex);
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"没有{title}", $"角色 {character.Name} 还没有可用的{title}素材。");
                return;
            }

            var selected = await ShowStoryChoiceDialogAsync(
                $"选择{title}",
                choices);
            if (selected is not int selectedIndex)
            {
                return;
            }

            if (ParseInt(row.Get(GetStoryLayerColumn(slotIndex, fieldPrefix))) == selectedIndex)
            {
                return;
            }

            CaptureStoryUndoState($"更换{GetStorySlotDisplayName(slotIndex)}{title}");
            row.Set(GetStoryLayerColumn(slotIndex, fieldPrefix), selectedIndex.ToString());
            if (fieldPrefix == "Body")
            {
                NormalizeStoryRowLayerCompatibility(row, character, slotIndex);
            }

            PersistCurrentStoryRowsToFiles();
            ShowStoryLayerChangedStatus(slotIndex, title, selectedIndex, GetStoryLayerChoiceDisplayName(fieldPrefix, paths, selectedIndex));
            await RefreshStoryPreviewAsync();
        }

        private async Task<object?> ShowStoryChoiceDialogAsync(string title, List<StoryObjectChoice> choices)
        {
            var listView = new ListView
            {
                Width = 420,
                MaxHeight = 420,
                SelectionMode = ListViewSelectionMode.Single
            };

            foreach (var choice in choices)
            {
                var choiceItem = new ListViewItem
                {
                    Content = choice.DisplayName,
                    Tag = choice.Value
                };

                if (choice.PreviewPaths is { Count: > 0 })
                {
                    ToolTipService.SetToolTip(choiceItem, await CreateStoryChoicePreviewToolTipAsync(choice.PreviewPaths));
                }

                listView.Items.Add(choiceItem);
            }

            listView.SelectedItem = listView.Items.FirstOrDefault();
            var dialog = new ContentDialog
            {
                Title = title,
                Content = listView,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary &&
                listView.SelectedItem is ListViewItem item
                    ? item.Tag
                    : null;
        }

        private static List<string> GetStoryCharacterLayerChoicePaths(string folderPath, string fieldPrefix)
        {
            return GetCharacterLayerImagePaths(folderPath);
        }

        private static async Task<ToolTip> CreateStoryChoicePreviewToolTipAsync(IReadOnlyList<string> previewPaths)
        {
            var grid = new Grid
            {
                Width = 220,
                Height = 320,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(224, 36, 36, 36))
            };

            foreach (var previewPath in previewPaths.Where(IsPreviewableImagePath))
            {
                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                grid.Children.Add(image);
                await LoadThumbnailFromFileAsync(image, previewPath);
            }

            return new ToolTip
            {
                Content = new Border
                {
                    Padding = new Thickness(8),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(238, 30, 30, 30)),
                    CornerRadius = new CornerRadius(6),
                    Child = grid
                }
            };
        }

        private static int GetStoryLayerChoiceCount(string fieldPrefix, int assetCount)
        {
            return fieldPrefix == "Adorn" ? assetCount + 1 : assetCount;
        }

        private List<StoryObjectChoice> CreateStoryLayerChoices(
            string fieldPrefix,
            IReadOnlyList<string> paths,
            CharacterInfo character,
            StoryRow row,
            int slotIndex)
        {
            var choices = new List<StoryObjectChoice>();
            var currentBodyPath = GetCharacterLayerPath(character, "DN_Cloth", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Body"))));
            var currentFacePath = GetCharacterLayerPath(character, "FC_Face", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Face"))));
            var currentAdornPath = GetCharacterLayerPath(character, "AD_Adorn", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Adorn"))));

            if (fieldPrefix == "Adorn")
            {
                choices.Add(new StoryObjectChoice(
                    "0",
                    "0: 无装饰",
                    0,
                    BuildStoryChoicePreviewPaths(currentBodyPath, currentFacePath)));
                choices.AddRange(paths
                    .Select((path, index) => new { path, index })
                    .Where(item => IsCharacterLayerCompatibleWithCloth(character, currentBodyPath, item.path))
                    .Select(item =>
                        new StoryObjectChoice(
                            (item.index + 1).ToString(),
                            $"{item.index + 1}: {Path.GetFileNameWithoutExtension(item.path)}",
                            item.index + 1,
                            BuildStoryChoicePreviewPaths(currentBodyPath, currentFacePath, item.path))));
                return choices;
            }

            if (fieldPrefix == "Body")
            {
                choices.AddRange(paths.Select((path, index) =>
                    new StoryObjectChoice(
                        index.ToString(),
                        $"{index}: {Path.GetFileNameWithoutExtension(path)}",
                        index,
                        BuildStoryChoicePreviewPaths(
                            path,
                            IsCharacterLayerCompatibleWithCloth(character, path, currentFacePath) ? currentFacePath : null,
                            IsCharacterLayerCompatibleWithCloth(character, path, currentAdornPath) ? currentAdornPath : null))));
                return choices;
            }

            if (fieldPrefix == "Face")
            {
                choices.AddRange(paths
                    .Select((path, index) => new { path, index })
                    .Where(item => IsCharacterLayerCompatibleWithCloth(character, currentBodyPath, item.path))
                    .Select(item =>
                        new StoryObjectChoice(
                            item.index.ToString(),
                            $"{item.index}: {Path.GetFileNameWithoutExtension(item.path)}",
                            item.index,
                            BuildStoryChoicePreviewPaths(currentBodyPath, item.path, currentAdornPath))));
                return choices;
            }

            choices.AddRange(paths.Select((path, index) =>
                new StoryObjectChoice(
                    index.ToString(),
                    $"{index}: {Path.GetFileNameWithoutExtension(path)}",
                    index,
                    BuildStoryChoicePreviewPaths(path))));
            return choices;
        }

        private List<int> GetStoryCompatibleLayerIndexes(
            CharacterInfo character,
            string fieldPrefix,
            IReadOnlyList<string> paths,
            StoryRow row,
            int slotIndex)
        {
            if (fieldPrefix == "Adorn")
            {
                var bodyPath = GetCharacterLayerPath(character, "DN_Cloth", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Body"))));
                var indexes = new List<int> { 0 };
                indexes.AddRange(paths
                    .Select((path, index) => new { path, index })
                    .Where(item => IsCharacterLayerCompatibleWithCloth(character, bodyPath, item.path))
                    .Select(item => item.index + 1));
                return indexes;
            }

            if (fieldPrefix == "Face")
            {
                var bodyPath = GetCharacterLayerPath(character, "DN_Cloth", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Body"))));
                return paths
                    .Select((path, index) => new { path, index })
                    .Where(item => IsCharacterLayerCompatibleWithCloth(character, bodyPath, item.path))
                    .Select(item => item.index)
                    .ToList();
            }

            return Enumerable.Range(0, paths.Count).ToList();
        }

        private bool NormalizeStoryRowLayerCompatibility(StoryRow row, CharacterInfo character, int slotIndex)
        {
            var changed = false;
            var bodyPath = GetCharacterLayerPath(character, "DN_Cloth", ParseInt(row.Get(GetStoryLayerColumn(slotIndex, "Body"))));
            changed |= NormalizeStoryLayerCompatibility(row, character, slotIndex, "Face", "FC_Face", bodyPath, false);
            changed |= NormalizeStoryLayerCompatibility(row, character, slotIndex, "Adorn", "AD_Adorn", bodyPath, true);
            return changed;
        }

        private bool NormalizeStoryLayerCompatibility(
            StoryRow row,
            CharacterInfo character,
            int slotIndex,
            string fieldPrefix,
            string folderName,
            string? bodyPath,
            bool allowNone)
        {
            var columnName = GetStoryLayerColumn(slotIndex, fieldPrefix);
            var currentIndex = ParseInt(row.Get(columnName));
            if (allowNone && currentIndex <= 0)
            {
                return false;
            }

            var currentPath = GetCharacterLayerPath(character, folderName, currentIndex);
            if (IsCharacterLayerCompatibleWithCloth(character, bodyPath, currentPath))
            {
                return false;
            }

            var folderPath = Path.Combine(character.Path, folderName);
            var paths = GetCharacterLayerImagePaths(folderPath);
            var compatible = paths
                .Select((path, index) => new { path, index })
                .FirstOrDefault(item => IsCharacterLayerCompatibleWithCloth(character, bodyPath, item.path));
            var nextIndex = compatible is null
                ? 0
                : allowNone ? compatible.index + 1 : compatible.index;
            row.Set(columnName, nextIndex.ToString());
            return currentIndex != nextIndex;
        }

        private static IReadOnlyList<string> BuildStoryChoicePreviewPaths(params string?[] paths)
        {
            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();
        }

        private static string GetStoryLayerChoiceDisplayName(string fieldPrefix, IReadOnlyList<string> paths, int selectedIndex)
        {
            if (fieldPrefix == "Adorn")
            {
                return selectedIndex == 0
                    ? "无装饰"
                    : Path.GetFileNameWithoutExtension(paths[Math.Clamp(selectedIndex - 1, 0, paths.Count - 1)]);
            }

            return Path.GetFileNameWithoutExtension(paths[Math.Clamp(selectedIndex, 0, paths.Count - 1)]);
        }

        private string NormalizeStoryCharacterNameForCsv(string characterName)
        {
            var trimmed = characterName.Trim();
            var character = ResolveStoryCharacter(trimmed);
            return character?.Code ?? trimmed;
        }

        private bool NormalizeStoryCharacterCodes()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return false;
            }

            var changed = false;
            foreach (var row in _storyRows)
            {
                var talkCharacter = ResolveStoryCharacter(row.Get("TalkChar"));
                if (talkCharacter is not null &&
                    !string.Equals(row.Get("TalkChar"), talkCharacter.Code, StringComparison.OrdinalIgnoreCase))
                {
                    row.Set("TalkChar", talkCharacter.Code);
                    changed = true;
                }

                for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
                {
                    var column = $"Chara{slotIndex}";
                    var value = row.Get(column);
                    var character = ResolveStoryCharacter(value);
                    if (character is not null &&
                        !string.Equals(value, character.Code, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Set(column, character.Code);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private CharacterInfo? ResolveStoryCharacter(string characterName)
        {
            if (_currentStoryAssetLibrary is null)
            {
                return null;
            }

            var normalizedCharacterName = characterName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCharacterName))
            {
                return null;
            }

            var characterFolderPath = GetCharacterFolderPath(_currentStoryAssetLibrary);
            if (!Directory.Exists(characterFolderPath))
            {
                return null;
            }

            return Directory
                .EnumerateDirectories(characterFolderPath)
                .Select(ReadCharacterInfo)
                .FirstOrDefault(character =>
                    string.Equals(character.Name, normalizedCharacterName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(character.Code, normalizedCharacterName, StringComparison.OrdinalIgnoreCase));
        }

        private List<CharacterInfo> GetStoryCharacters()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            var characterFolderPath = GetCharacterFolderPath(_currentStoryAssetLibrary);
            if (!Directory.Exists(characterFolderPath))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(characterFolderPath)
                .Select(ReadCharacterInfo)
                .OrderBy(character => character.Name)
                .ToList();
        }

        private List<CharacterInfo> GetStoryCharactersByFolderOrder()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            var characterFolderPath = GetCharacterFolderPath(_currentStoryAssetLibrary);
            if (!Directory.Exists(characterFolderPath))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(characterFolderPath)
                .OrderBy(Path.GetFileName)
                .Select(ReadCharacterInfo)
                .ToList();
        }

        private static string? GetCharacterLayerPath(CharacterInfo character, string folderName, int index)
        {
            if (folderName == "AD_Adorn" && index <= 0)
            {
                return null;
            }

            var folderPath = Path.Combine(character.Path, folderName);
            if (!Directory.Exists(folderPath))
            {
                return null;
            }

            var paths = folderName == "VFX"
                ? Directory.EnumerateFiles(folderPath).OrderBy(Path.GetFileName).ToList()
                : GetCharacterLayerImagePaths(folderPath);
            var resolvedIndex = ResolveStoryAssetIndex(folderName == "AD_Adorn" ? index - 1 : index, paths.Count);
            return resolvedIndex is null ? null : paths[resolvedIndex.Value];
        }

        private List<StoryAssetChoice> GetStoryBackgroundChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return GetBackgroundImagePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private List<StoryAssetChoice> GetStoryBgmChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return GetMusicFilePaths(GetMusicFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private List<StoryAssetChoice> GetStorySceneChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return GetAudioFilePaths(GetAmbientSoundFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private AssetLibraryInfo? ResolveProjectAssetLibrary(ProjectInfo project)
        {
            return GetAssetLibraries()
                .FirstOrDefault(library =>
                    string.Equals(library.FolderName, project.AssetLibraryFolderName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(library.Name, project.AssetLibraryName, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<AssetIndexSyncResult> SyncStoryGlobalAssetIndexesWithProgressAsync(
            AssetLibraryInfo assetLibrary,
            string assetLabel,
            string columnName,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var result = await ShowAssetIndexSyncProgressDialogAsync(
                $"{assetLabel}索引同步",
                progress => Task.Run(() => SyncStoryGlobalAssetIndexes(assetLibrary, assetLabel, columnName, indexRemap, oldLabels, newLabels, assetCount, progress)));
            RefreshOpenStoryRowsAfterIndexSync(result.ChangedCsvPaths);
            await ShowAssetIndexSyncResultDialogAsync(result);
            return result;
        }

        private async Task<AssetIndexSyncResult> SyncStoryCharacterLayerIndexesWithProgressAsync(
            AssetLibraryInfo assetLibrary,
            CharacterInfo character,
            CharacterLayerKind layerKind,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var assetLabel = $"{character.Name} {GetCharacterLayerDisplayName(layerKind)}";
            var result = await ShowAssetIndexSyncProgressDialogAsync(
                $"{assetLabel}索引同步",
                progress => Task.Run(() => SyncStoryCharacterLayerIndexes(assetLibrary, character, layerKind, indexRemap, oldLabels, newLabels, assetCount, progress)));
            RefreshOpenStoryRowsAfterIndexSync(result.ChangedCsvPaths);
            await ShowAssetIndexSyncResultDialogAsync(result);
            return result;
        }

        private async Task<AssetIndexSyncResult> SyncStoryCharacterFilterIndexesWithProgressAsync(
            AssetLibraryInfo assetLibrary,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var result = await ShowAssetIndexSyncProgressDialogAsync(
                "角色滤镜索引同步",
                progress => Task.Run(() => SyncStoryCharacterFilterIndexes(assetLibrary, indexRemap, oldLabels, newLabels, assetCount, progress)));
            RefreshOpenStoryRowsAfterIndexSync(result.ChangedCsvPaths);
            await ShowAssetIndexSyncResultDialogAsync(result);
            return result;
        }

        private AssetIndexSyncResult SyncStoryGlobalAssetIndexes(
            AssetLibraryInfo assetLibrary,
            string assetLabel,
            string columnName,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount,
            IProgress<AssetIndexSyncProgress>? progress)
        {
            return SyncStoryRowsForAssetLibrary(
                assetLibrary,
                $"{assetLabel}索引同步",
                progress,
                rowContext =>
                {
                    var oldIndex = ParseInt(rowContext.Row.Get(columnName));
                    var changed = TryRecordStoryIndexRemap(rowContext, assetLabel, columnName, oldIndex, oldIndex, indexRemap, oldLabels, newLabels, assetCount, out var warning);
                    if (warning is not null)
                    {
                        rowContext.Warnings.Add(warning);
                    }

                    return changed;
                });
        }

        private AssetIndexSyncResult SyncStoryCharacterFilterIndexes(
            AssetLibraryInfo assetLibrary,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount,
            IProgress<AssetIndexSyncProgress>? progress)
        {
            return SyncStoryRowsForAssetLibrary(
                assetLibrary,
                "角色滤镜索引同步",
                progress,
                rowContext =>
                {
                    var changed = false;
                    foreach (var columnName in Enumerable.Range(0, 6).Select(index => index == 0 ? "TalkVfx" : $"Vfx{index}"))
                    {
                        var oldIndex = ParseInt(rowContext.Row.Get(columnName));
                        changed |= TryRecordStoryIndexRemap(rowContext, "角色滤镜", columnName, oldIndex, oldIndex, indexRemap, oldLabels, newLabels, assetCount, out var warning);
                        if (warning is not null)
                        {
                            rowContext.Warnings.Add(warning);
                        }
                    }

                    return changed;
                });
        }

        private AssetIndexSyncResult SyncStoryCharacterLayerIndexes(
            AssetLibraryInfo assetLibrary,
            CharacterInfo character,
            CharacterLayerKind layerKind,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount,
            IProgress<AssetIndexSyncProgress>? progress)
        {
            var assetLabel = $"{character.Name} {GetCharacterLayerDisplayName(layerKind)}";
            var fieldPrefix = GetStoryLayerFieldPrefix(layerKind);
            return SyncStoryRowsForAssetLibrary(
                assetLibrary,
                $"{assetLabel}索引同步",
                progress,
                rowContext =>
                {
                    var changed = false;
                    if (StoryCharacterMatches(rowContext.Row.Get("TalkChar"), character))
                    {
                        changed |= TryRecordStoryLayerRemap(rowContext, assetLabel, GetStoryLayerColumn(0, fieldPrefix), layerKind, indexRemap, oldLabels, newLabels, assetCount);
                    }

                    for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
                    {
                        if (StoryCharacterMatches(rowContext.Row.Get(GetStoryCharacterColumn(slotIndex)), character))
                        {
                            changed |= TryRecordStoryLayerRemap(rowContext, assetLabel, GetStoryLayerColumn(slotIndex, fieldPrefix), layerKind, indexRemap, oldLabels, newLabels, assetCount);
                        }
                    }

                    return changed;
                });
        }

        private AssetIndexSyncResult SyncStoryRowsForAssetLibrary(
            AssetLibraryInfo assetLibrary,
            string title,
            IProgress<AssetIndexSyncProgress>? progress,
            Func<StoryIndexRowContext, bool> updateRow)
        {
            var csvFiles = GetRelatedStoryCsvFiles(assetLibrary);
            var changes = new List<AssetIndexChange>();
            var warnings = new List<AssetIndexWarning>();
            var changedCsvPaths = new List<string>();
            progress?.Report(new AssetIndexSyncProgress("正在收集关联项目章节 CSV...", 0, 0, csvFiles.Count, 0, 0, null));

            for (var fileIndex = 0; fileIndex < csvFiles.Count; fileIndex++)
            {
                var csvFile = csvFiles[fileIndex];
                progress?.Report(new AssetIndexSyncProgress(
                    $"正在扫描 {csvFile.ProjectName} / {csvFile.ChapterName}",
                    csvFiles.Count == 0 ? 100 : fileIndex * 80d / csvFiles.Count,
                    fileIndex,
                    csvFiles.Count,
                    changes.Count,
                    warnings.Count,
                    Path.GetFileName(csvFile.CsvPath)));

                var rows = ReadStoryRows(csvFile.CsvPath);
                var changed = false;
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var context = new StoryIndexRowContext(csvFile, rows[rowIndex], rowIndex, changes, warnings);
                    changed |= updateRow(context);
                }

                if (changed)
                {
                    WriteStoryRows(csvFile.CsvPath, rows);
                    changedCsvPaths.Add(csvFile.CsvPath);
                }
            }

            progress?.Report(new AssetIndexSyncProgress("索引同步检查完成。", 100, csvFiles.Count, csvFiles.Count, changes.Count, warnings.Count, null));
            return new AssetIndexSyncResult(title, csvFiles.Count, changedCsvPaths.Count, changes.Count, warnings.Count, changedCsvPaths, changes, warnings);
        }

        private List<RelatedStoryCsvFile> GetRelatedStoryCsvFiles(AssetLibraryInfo assetLibrary)
        {
            var result = new List<RelatedStoryCsvFile>();
            foreach (var project in GetProjects())
            {
                var projectAssetLibrary = ResolveProjectAssetLibrary(project);
                if (projectAssetLibrary is null || !PathsEqual(projectAssetLibrary.Path, assetLibrary.Path))
                {
                    continue;
                }

                var chaptersFolderPath = GetChaptersFolderPath(project);
                if (!Directory.Exists(chaptersFolderPath))
                {
                    continue;
                }

                foreach (var chapter in Directory.EnumerateDirectories(chaptersFolderPath).Select(ReadChapterInfo))
                {
                    foreach (var sectionFile in GetLocalStorySectionCsvPaths(chapter).Where(file => File.Exists(file.Path)))
                    {
                        result.Add(new RelatedStoryCsvFile(project.Name, chapter.Name, chapter.Code, sectionFile.Path));
                    }
                }
            }

            return result.OrderBy(file => file.ProjectName).ThenBy(file => file.ChapterCode).ThenBy(file => file.CsvPath).ToList();
        }

        private static bool TryRecordStoryLayerRemap(
            StoryIndexRowContext rowContext,
            string assetLabel,
            string columnName,
            CharacterLayerKind layerKind,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount)
        {
            var storyIndex = ParseInt(rowContext.Row.Get(columnName));
            if (layerKind == CharacterLayerKind.Adorn)
            {
                if (storyIndex <= 0)
                {
                    return false;
                }

                var oldAssetIndex = storyIndex - 1;
                return TryRecordStoryIndexRemap(
                    rowContext,
                    assetLabel,
                    columnName,
                    oldAssetIndex,
                    storyIndex,
                    indexRemap,
                    oldLabels,
                    newLabels,
                    assetCount,
                    out var warning,
                    newStoryIndexOffset: 1,
                    validStoryIndexOffset: 1) || AddWarning(rowContext, warning);
            }

            return TryRecordStoryIndexRemap(rowContext, assetLabel, columnName, storyIndex, storyIndex, indexRemap, oldLabels, newLabels, assetCount, out var directWarning) ||
                AddWarning(rowContext, directWarning);
        }

        private static bool TryRecordStoryIndexRemap(
            StoryIndexRowContext rowContext,
            string assetLabel,
            string columnName,
            int oldAssetIndex,
            int oldStoryValue,
            IReadOnlyDictionary<int, int> indexRemap,
            IReadOnlyDictionary<int, string> oldLabels,
            IReadOnlyDictionary<int, string> newLabels,
            int assetCount,
            out AssetIndexWarning? warning,
            int newStoryIndexOffset = 0,
            int validStoryIndexOffset = 0)
        {
            warning = null;
            if (oldAssetIndex < 0 || oldAssetIndex >= assetCount)
            {
                warning = rowContext.CreateWarning(columnName, $"{assetLabel} 索引 {oldStoryValue} 超出当前素材数量 {assetCount}，未自动改动。可在章节卡右键使用“修复”检查。");
                return false;
            }

            if (!indexRemap.TryGetValue(oldAssetIndex, out var newAssetIndex) || oldAssetIndex == newAssetIndex)
            {
                return false;
            }

            var newStoryValue = newAssetIndex + newStoryIndexOffset;
            rowContext.Row.Set(columnName, newStoryValue.ToString());
            rowContext.Changes.Add(rowContext.CreateChange(
                columnName,
                oldStoryValue.ToString(),
                newStoryValue.ToString(),
                FormatAssetIndexLabel(oldStoryValue, oldLabels.TryGetValue(oldAssetIndex, out var oldLabel) ? oldLabel : string.Empty),
                FormatAssetIndexLabel(newStoryValue, newLabels.TryGetValue(newAssetIndex, out var newLabel) ? newLabel : string.Empty)));

            if (newAssetIndex < 0 || newAssetIndex >= assetCount + validStoryIndexOffset)
            {
                warning = rowContext.CreateWarning(columnName, $"{assetLabel} remap 后索引 {newStoryValue} 仍然超出当前素材数量 {assetCount}。");
            }

            return true;
        }

        private static bool AddWarning(StoryIndexRowContext rowContext, AssetIndexWarning? warning)
        {
            if (warning is null)
            {
                return false;
            }

            rowContext.Warnings.Add(warning);
            return false;
        }

        private static string FormatAssetIndexLabel(int index, string label)
        {
            return string.IsNullOrWhiteSpace(label) ? index.ToString() : $"{index} / {label}";
        }

        private static (Dictionary<int, string> OldLabels, Dictionary<int, string> NewLabels) BuildAssetIndexLabelMaps(
            IReadOnlyList<string> orderedPaths,
            Func<string, int?> getOldIndex)
        {
            var oldLabels = orderedPaths
                .Select(path => new { OldIndex = getOldIndex(path), Label = Path.GetFileNameWithoutExtension(path) })
                .Where(item => item.OldIndex is not null)
                .GroupBy(item => item.OldIndex!.Value)
                .ToDictionary(group => group.Key, group => group.First().Label);
            var newLabels = orderedPaths
                .Select((path, newIndex) => new { NewIndex = newIndex, Label = Path.GetFileNameWithoutExtension(path) })
                .ToDictionary(item => item.NewIndex, item => item.Label);
            return (oldLabels, newLabels);
        }

        private void RefreshOpenStoryRowsAfterIndexSync(IReadOnlyList<string> changedCsvPaths)
        {
            if (_currentStoryChapter is null || changedCsvPaths.Count == 0)
            {
                return;
            }

            if (!changedCsvPaths.Any(path => IsPathInsideDirectory(path, _currentStoryChapter.Path)))
            {
                return;
            }

            LoadStoryRowsFromSectionFiles(_currentStoryChapter);
            SynchronizeStorySectionState();
            ClearStoryUndoStack();
            _currentStoryRowIndex = Math.Clamp(_currentStoryRowIndex, 0, Math.Max(0, _storyRows.Count - 1));
            if (StoryEditorPage.Visibility == Visibility.Visible)
            {
                RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
                LoadStoryRowIntoUi();
            }
        }

        private int UpdateStoryRowsForAssetLibrary(AssetLibraryInfo assetLibrary, Func<StoryRow, bool> updateRow)
        {
            var changedFileCount = 0;
            foreach (var project in GetProjects())
            {
                var projectAssetLibrary = ResolveProjectAssetLibrary(project);
                if (projectAssetLibrary is null || !PathsEqual(projectAssetLibrary.Path, assetLibrary.Path))
                {
                    continue;
                }

                foreach (var csvPath in GetProjectStoryCsvPaths(project))
                {
                    var rows = ReadStoryRows(csvPath);
                    var changed = false;
                    foreach (var row in rows)
                    {
                        changed |= updateRow(row);
                    }

                    if (!changed)
                    {
                        continue;
                    }

                    WriteStoryRows(csvPath, rows);
                    changedFileCount++;
                    if (_currentStoryCsvPath is not null && PathsEqual(_currentStoryCsvPath, csvPath))
                    {
                        _storyRows.Clear();
                        _storyRows.AddRange(rows);
                        _currentStoryRowIndex = Math.Clamp(_currentStoryRowIndex, 0, Math.Max(0, _storyRows.Count - 1));
                        if (StoryEditorPage.Visibility == Visibility.Visible)
                        {
                            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
                            LoadStoryRowIntoUi();
                        }
                    }
                }
            }

            return changedFileCount;
        }

        private int UpdateStoryGlobalAssetIndexes(AssetLibraryInfo assetLibrary, string columnName, IReadOnlyDictionary<int, int> indexRemap)
        {
            if (indexRemap.Count == 0)
            {
                return 0;
            }

            return UpdateStoryRowsForAssetLibrary(assetLibrary, row => RemapStoryIndex(row, columnName, indexRemap));
        }

        private int UpdateStoryCharacterLayerIndexes(
            AssetLibraryInfo assetLibrary,
            CharacterInfo character,
            CharacterLayerKind layerKind,
            IReadOnlyDictionary<int, int> indexRemap)
        {
            if (indexRemap.Count == 0)
            {
                return 0;
            }

            return UpdateStoryRowsForAssetLibrary(assetLibrary, row =>
            {
                var changed = false;
                if (StoryCharacterMatches(row.Get("TalkChar"), character))
                {
                    changed |= RemapStoryLayerIndex(row, GetStoryLayerColumn(0, GetStoryLayerFieldPrefix(layerKind)), layerKind, indexRemap);
                }

                for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
                {
                    if (StoryCharacterMatches(row.Get(GetStoryCharacterColumn(slotIndex)), character))
                    {
                        changed |= RemapStoryLayerIndex(row, GetStoryLayerColumn(slotIndex, GetStoryLayerFieldPrefix(layerKind)), layerKind, indexRemap);
                    }
                }

                return changed;
            });
        }

        private int UpdateStoryCharacterFilterIndexes(AssetLibraryInfo assetLibrary, IReadOnlyDictionary<int, int> indexRemap)
        {
            if (indexRemap.Count == 0)
            {
                return 0;
            }

            return UpdateStoryRowsForAssetLibrary(assetLibrary, row =>
            {
                var changed = RemapStoryIndex(row, "TalkVfx", indexRemap);
                for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
                {
                    changed |= RemapStoryIndex(row, $"Vfx{slotIndex}", indexRemap);
                }

                return changed;
            });
        }

        private static bool RemapStoryIndex(StoryRow row, string columnName, IReadOnlyDictionary<int, int> indexRemap)
        {
            var oldIndex = ParseInt(row.Get(columnName));
            if (!indexRemap.TryGetValue(oldIndex, out var newIndex) || oldIndex == newIndex)
            {
                return false;
            }

            row.Set(columnName, newIndex.ToString());
            return true;
        }

        private static bool RemapStoryLayerIndex(
            StoryRow row,
            string columnName,
            CharacterLayerKind layerKind,
            IReadOnlyDictionary<int, int> indexRemap)
        {
            if (layerKind != CharacterLayerKind.Adorn)
            {
                return RemapStoryIndex(row, columnName, indexRemap);
            }

            var storyIndex = ParseInt(row.Get(columnName));
            if (storyIndex <= 0)
            {
                return false;
            }

            var oldAssetIndex = storyIndex - 1;
            if (!indexRemap.TryGetValue(oldAssetIndex, out var newAssetIndex) || oldAssetIndex == newAssetIndex)
            {
                return false;
            }

            row.Set(columnName, (newAssetIndex + 1).ToString());
            return true;
        }

        private static bool StoryCharacterMatches(string value, CharacterInfo character)
        {
            return string.Equals(value, character.Code, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, character.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStoryLayerFieldPrefix(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => "Body",
                CharacterLayerKind.Face => "Face",
                CharacterLayerKind.Adorn => "Adorn",
                CharacterLayerKind.Vfx => "Vfx",
                _ => "Body"
            };
        }

        private static Dictionary<int, int> BuildAssetIndexRemap(
            IReadOnlyList<string> orderedPaths,
            Func<string, int?> getOldIndex)
        {
            return orderedPaths
                .Select((path, newIndex) => new { OldIndex = getOldIndex(path), NewIndex = newIndex })
                .Where(item => item.OldIndex is not null && item.OldIndex.Value != item.NewIndex)
                .ToDictionary(item => item.OldIndex!.Value, item => item.NewIndex);
        }

        private static Dictionary<int, int> BuildCharacterFilterIndexRemap(
            IReadOnlyList<CharacterFilterEntry> oldFilters,
            IReadOnlyList<CharacterFilterEntry> newFilters)
        {
            var newIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < newFilters.Count; i++)
            {
                if (!newIndexes.ContainsKey(newFilters[i].Id))
                {
                    newIndexes[newFilters[i].Id] = i;
                }
            }

            var result = new Dictionary<int, int>();
            for (var oldIndex = 0; oldIndex < oldFilters.Count; oldIndex++)
            {
                var filter = oldFilters[oldIndex];
                if (newIndexes.TryGetValue(filter.Id, out var newIndex))
                {
                    if (oldIndex != newIndex)
                    {
                        result[oldIndex] = newIndex;
                    }
                }
                else if (oldIndex != 0)
                {
                    result[oldIndex] = 0;
                }
            }

            return result;
        }

        private static int? GetBackgroundImageIndex(string imagePath)
        {
            var match = Regex.Match(Path.GetFileNameWithoutExtension(imagePath), @"^BG(?<index>\d+)", RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups["index"].Value) : null;
        }

        private static int? GetAudioAssetIndex(AudioAssetKind kind, string audioPath)
        {
            var match = Regex.Match(
                Path.GetFileNameWithoutExtension(audioPath),
                $"^{Regex.Escape(GetAudioPrefix(kind))}(?<index>\\d+)",
                RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups["index"].Value) : null;
        }

        private void ShowStoryStatus(InfoBarSeverity severity, string title, string message)
        {
            var tip = CreateStoryTipBar(severity, title, message);
            AddStoryTipWithEntrance(StoryFloatingTipsPanel, tip);

            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(severity == InfoBarSeverity.Error ? 5 : 2.6);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _storyTransientTipTimers.Remove(tip);
                StoryFloatingTipsPanel.Children.Remove(tip);
            };
            _storyTransientTipTimers[tip] = timer;
            timer.Start();
        }

        private GridView GetAudioGridView(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => MusicGridView,
                AudioAssetKind.Ambient => AmbientSoundGridView,
                AudioAssetKind.SoundEffect => SoundEffectGridView,
                _ => MusicGridView
            };
        }

        private bool IsAudioNormalizing(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => _isNormalizingMusicFiles,
                AudioAssetKind.Ambient => _isNormalizingAmbientSoundFiles,
                AudioAssetKind.SoundEffect => _isNormalizingSoundEffectFiles,
                _ => false
            };
        }

        private void SetAudioNormalizing(AudioAssetKind kind, bool value)
        {
            switch (kind)
            {
                case AudioAssetKind.Music:
                    _isNormalizingMusicFiles = value;
                    break;
                case AudioAssetKind.Ambient:
                    _isNormalizingAmbientSoundFiles = value;
                    break;
                case AudioAssetKind.SoundEffect:
                    _isNormalizingSoundEffectFiles = value;
                    break;
            }
        }

        private GridViewItem? GetDraggingAudioItem(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => _draggingMusicItem,
                AudioAssetKind.Ambient => _draggingAmbientSoundItem,
                AudioAssetKind.SoundEffect => _draggingSoundEffectItem,
                _ => null
            };
        }

        private void SetDraggingAudioItem(AudioAssetKind kind, GridViewItem? item)
        {
            switch (kind)
            {
                case AudioAssetKind.Music:
                    _draggingMusicItem = item;
                    break;
                case AudioAssetKind.Ambient:
                    _draggingAmbientSoundItem = item;
                    break;
                case AudioAssetKind.SoundEffect:
                    _draggingSoundEffectItem = item;
                    break;
            }
        }

        private static string GetAudioDisplayName(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => "音乐",
                AudioAssetKind.Ambient => "环境音",
                AudioAssetKind.SoundEffect => "特殊音效",
                _ => "音频"
            };
        }

        private static string GetAudioPrefix(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => "BGM",
                AudioAssetKind.Ambient => "Sc",
                AudioAssetKind.SoundEffect => "SE",
                _ => "Audio"
            };
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out var result) ? result : 0;
        }

        private static int? ResolveStoryAssetIndex(int rawIndex, int assetCount)
        {
            if (assetCount <= 0 || rawIndex < 0)
            {
                return null;
            }

            return rawIndex < assetCount ? rawIndex : null;
        }

        private static string GetChapterStoryCsvPath(ChapterInfo chapter)
        {
            var expectedCsv = Path.Combine(chapter.Path, $"{chapter.Code}.csv");
            if (File.Exists(expectedCsv))
            {
                return expectedCsv;
            }

            var legacyStoryCsv = Directory
                .EnumerateFiles(chapter.Path, "*.story.csv")
                .OrderBy(Path.GetFileName)
                .FirstOrDefault();
            if (legacyStoryCsv is not null)
            {
                if (!File.Exists(expectedCsv))
                {
                    File.Move(legacyStoryCsv, expectedCsv);
                }

                return expectedCsv;
            }

            return expectedCsv;
        }

        private static StoryRow CreateDefaultStoryRow()
        {
            var row = new StoryRow();
            foreach (var column in StoryCsvColumns)
            {
                row.Set(column, StoryNumericColumns.Contains(column) ? "0" : string.Empty);
            }

            row.Set("Name", CreateStoryRowName(0));
            return row;
        }

        private static string CreateStoryRowName(int index)
        {
            return (index + 1).ToString();
        }

        private static List<StoryRow> ReadStoryRows(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                return [];
            }

            var lines = File.ReadAllLines(csvPath, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (lines.Count == 0)
            {
                return [];
            }

            var headers = NormalizeStoryCsvHeaders(ParseCsvLine(lines[0]));
            var rows = new List<StoryRow>();
            foreach (var line in lines.Skip(1))
            {
                var cells = ParseCsvLine(line);
                var row = CreateDefaultStoryRow();
                for (var i = 0; i < headers.Count && i < cells.Count; i++)
                {
                    row.Set(headers[i], cells[i]);
                }

                if (string.IsNullOrWhiteSpace(row.Get("Name")))
                {
                    row.Set("Name", CreateStoryRowName(rows.Count));
                }

                rows.Add(row);
            }

            return rows;
        }

        private static void WriteStoryRows(string csvPath, IReadOnlyList<StoryRow> rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", StoryCsvColumns.Select(column => EscapeCsvField(GetStoryCsvHeaderName(column)))));
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].Set("Name", CreateStoryRowName(i));
                builder.AppendLine(string.Join(",", StoryCsvColumns.Select(column => EscapeCsvField(rows[i].Get(column)))));
            }

            File.WriteAllText(csvPath, builder.ToString(), Encoding.UTF8);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var cells = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    cells.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(ch);
                }
            }

            cells.Add(builder.ToString());
            return cells;
        }

        private static List<string> NormalizeStoryCsvHeaders(IReadOnlyList<string> headers)
        {
            return headers
                .Select((header, index) => IsStoryRowNameHeader(header, index) ? "Name" : header)
                .ToList();
        }

        private static bool IsStoryRowNameHeader(string header, int index)
        {
            return index == 0 &&
                (string.IsNullOrWhiteSpace(header) ||
                    string.Equals(header.Trim(), "---", StringComparison.Ordinal) ||
                    string.Equals(header.Trim(), "Name", StringComparison.Ordinal));
        }

        private static string GetStoryCsvHeaderName(string column)
        {
            return string.Equals(column, "Name", StringComparison.Ordinal) ? "---" : column;
        }

        private static string EscapeCsvField(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private void ShowChapterStatus(InfoBarSeverity severity, string title, string message)
        {
            ChapterInfoBar.Severity = severity;
            ChapterInfoBar.Title = title;
            ChapterInfoBar.Message = message;
            ChapterInfoBar.IsOpen = true;
        }

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
            var textBox = new TextBox
            {
                Width = 420,
                Header = "备注（可留空）",
                PlaceholderText = "例如：改对白前、导入小节后",
                MaxLength = 80
            };

            var panel = new StackPanel
            {
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = targetName,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(textBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "备份",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? NormalizeBackupNote(textBox.Text) : null;
        }

        private async Task<FolderBackupEntry> ShowFolderBackupProgressDialogAsync(
            string title,
            string targetName,
            Func<IProgress<FolderBackupProgress>, Task<FolderBackupEntry>> backupAction)
        {
            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                IsIndeterminate = true
            };
            var stageText = new TextBlock
            {
                Text = "准备开始备份...",
                TextWrapping = TextWrapping.Wrap
            };
            var detailText = new TextBlock
            {
                Style = TryGetTextBlockStyle("SubtleTextStyle"),
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new StackPanel
            {
                Spacing = 10,
                Width = 520
            };
            panel.Children.Add(new TextBlock
            {
                Text = targetName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(progressBar);
            panel.Children.Add(stageText);
            panel.Children.Add(detailText);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                XamlRoot = Content.XamlRoot
            };

            var progress = new Progress<FolderBackupProgress>(update =>
            {
                progressBar.IsIndeterminate = update.Percent <= 0;
                progressBar.Value = Math.Clamp(update.Percent, 0, 100);
                stageText.Text = update.Message;
                var byteText = update.TotalBytes > 0
                    ? $"{FormatFileSize(update.CompletedBytes)} / {FormatFileSize(update.TotalBytes)}"
                    : "正在统计大小";
                var fileText = update.TotalFiles > 0
                    ? $"{Math.Min(update.CompletedFiles + 1, update.TotalFiles)} / {update.TotalFiles} 个文件"
                    : "正在扫描文件";
                detailText.Text = update.CurrentRelativePath is null
                    ? $"{fileText}，{byteText}"
                    : $"{fileText}，{byteText}\n{update.CurrentRelativePath}";
            });

            var dialogOperation = dialog.ShowAsync();
            await Task.Delay(120);
            try
            {
                return await backupAction(progress);
            }
            finally
            {
                dialog.Hide();
                _ = dialogOperation;
            }
        }

        private async Task<AssetIndexSyncResult> ShowAssetIndexSyncProgressDialogAsync(
            string title,
            Func<IProgress<AssetIndexSyncProgress>, Task<AssetIndexSyncResult>> syncAction)
        {
            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                IsIndeterminate = true
            };
            var stageText = new TextBlock
            {
                Text = "准备扫描关联项目...",
                TextWrapping = TextWrapping.Wrap
            };
            var detailText = new TextBlock
            {
                Style = TryGetTextBlockStyle("SubtleTextStyle"),
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new StackPanel
            {
                Spacing = 10,
                Width = 560
            };
            panel.Children.Add(progressBar);
            panel.Children.Add(stageText);
            panel.Children.Add(detailText);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                XamlRoot = Content.XamlRoot
            };

            var progress = new Progress<AssetIndexSyncProgress>(update =>
            {
                progressBar.IsIndeterminate = update.Percent <= 0;
                progressBar.Value = Math.Clamp(update.Percent, 0, 100);
                stageText.Text = update.Message;
                var csvText = update.TotalCsvFiles > 0
                    ? $"{Math.Min(update.CompletedCsvFiles + 1, update.TotalCsvFiles)} / {update.TotalCsvFiles} 个 CSV"
                    : "正在收集 CSV";
                detailText.Text = update.CurrentCsvName is null
                    ? $"{csvText}，已变更 {update.ChangeCount} 处，异常 {update.WarningCount} 处"
                    : $"{csvText}，已变更 {update.ChangeCount} 处，异常 {update.WarningCount} 处\n{update.CurrentCsvName}";
            });

            var dialogOperation = dialog.ShowAsync();
            await Task.Delay(120);
            try
            {
                return await syncAction(progress);
            }
            finally
            {
                dialog.Hide();
                _ = dialogOperation;
            }
        }

        private async Task ShowAssetIndexSyncResultDialogAsync(AssetIndexSyncResult result)
        {
            if (result.ChangeCount == 0 && result.WarningCount == 0)
            {
                AppendLog(LogKind.Info, $"{result.Title}：已检查 {result.ScannedCsvCount} 个 CSV，没有发现需要更新的索引。");
                return;
            }

            var panel = new StackPanel
            {
                Spacing = 10,
                Width = 720
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"已扫描 {result.ScannedCsvCount} 个 CSV，更新 {result.ChangedCsvCount} 个 CSV，变更 {result.ChangeCount} 处，异常 {result.WarningCount} 处。",
                TextWrapping = TextWrapping.Wrap
            });

            if (result.Changes.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "变更前后对比",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                panel.Children.Add(CreateScrollableTextBlock(string.Join("\n", result.Changes.Take(80).Select(change =>
                    $"{change.ProjectName}/{change.ChapterName}/{change.CsvName} 行{change.RowName} {change.ColumnName}: {change.OldValueLabel} -> {change.NewValueLabel}"))));
            }

            if (result.Warnings.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "需要注意的数据",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                panel.Children.Add(CreateScrollableTextBlock(string.Join("\n", result.Warnings.Take(80).Select(warning =>
                    $"{warning.ProjectName}/{warning.ChapterName}/{warning.CsvName} 行{warning.RowName} {warning.ColumnName}: {warning.Message}"))));
                panel.Children.Add(new TextBlock
                {
                    Text = "这些数据没有被强行改动。可以在章节卡右键点击“修复”做单章体检和保守修复。",
                    TextWrapping = TextWrapping.Wrap,
                    Style = TryGetTextBlockStyle("SubtleTextStyle")
                });
            }

            var dialog = new ContentDialog
            {
                Title = result.WarningCount > 0 ? $"{result.Title}：有异常数据" : $"{result.Title}完成",
                Content = panel,
                PrimaryButtonText = "知道了",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task<ChapterRepairResult> ShowChapterRepairProgressDialogAsync(
            string title,
            ChapterInfo chapter,
            Func<IProgress<ChapterRepairProgress>, Task<ChapterRepairResult>> repairAction)
        {
            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                IsIndeterminate = true
            };
            var stageText = new TextBlock
            {
                Text = "准备检查章节索引...",
                TextWrapping = TextWrapping.Wrap
            };
            var detailText = new TextBlock
            {
                Style = TryGetTextBlockStyle("SubtleTextStyle"),
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new StackPanel
            {
                Spacing = 10,
                Width = 560
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"{chapter.Name}（{chapter.Code}）",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(progressBar);
            panel.Children.Add(stageText);
            panel.Children.Add(detailText);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                XamlRoot = Content.XamlRoot
            };

            var progress = new Progress<ChapterRepairProgress>(update =>
            {
                progressBar.IsIndeterminate = update.Percent <= 0;
                progressBar.Value = Math.Clamp(update.Percent, 0, 100);
                stageText.Text = update.Message;
                var csvText = update.TotalCsvFiles > 0
                    ? $"{Math.Min(update.CompletedCsvFiles + 1, update.TotalCsvFiles)} / {update.TotalCsvFiles} 个 CSV"
                    : "正在收集 CSV";
                detailText.Text = update.CurrentCsvName is null
                    ? $"{csvText}，发现 {update.IssueCount} 处异常，已修复 {update.FixedCount} 处"
                    : $"{csvText}，发现 {update.IssueCount} 处异常，已修复 {update.FixedCount} 处\n{update.CurrentCsvName}";
            });

            var dialogOperation = dialog.ShowAsync();
            await Task.Delay(120);
            try
            {
                return await repairAction(progress);
            }
            finally
            {
                dialog.Hide();
                _ = dialogOperation;
            }
        }

        private async Task<bool> ShowChapterRepairResultDialogAsync(ChapterRepairResult result)
        {
            var panel = new StackPanel
            {
                Spacing = 10,
                Width = 720
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"已扫描 {result.ScannedCsvCount} 个 CSV，发现 {result.IssueCount} 处异常。其中 {result.AutoFixableCount} 处可以自动归零修复。",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(CreateScrollableTextBlock(string.Join("\n", result.Issues.Take(100).Select(issue =>
                $"{issue.ProjectName}/{issue.ChapterName}/{issue.CsvName} 行{issue.RowName} {issue.ColumnName}: {issue.Message}{(issue.CanAutoFix ? " [可自动修复]" : " [需手动确认]")}"))));

            var dialog = new ContentDialog
            {
                Title = "章节索引检查结果",
                Content = panel,
                PrimaryButtonText = result.AutoFixableCount > 0 ? "自动修复" : string.Empty,
                SecondaryButtonText = "只查看",
                CloseButtonText = "取消",
                DefaultButton = result.AutoFixableCount > 0 ? ContentDialogButton.Primary : ContentDialogButton.Secondary,
                XamlRoot = Content.XamlRoot
            };

            var dialogResult = await dialog.ShowAsync();
            return result.AutoFixableCount > 0 && dialogResult == ContentDialogResult.Primary;
        }

        private UIElement CreateScrollableTextBlock(string text)
        {
            return new ScrollViewer
            {
                MaxHeight = 260,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    Style = TryGetTextBlockStyle("SubtleTextStyle")
                }
            };
        }

        private async Task<FolderBackupEntry?> ShowFolderRestoreDialogAsync(string title, string targetName, IReadOnlyList<FolderBackupEntry> backups)
        {
            var listView = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 320,
                Width = 420
            };

            foreach (var backup in backups)
            {
                listView.Items.Add(new ListViewItem
                {
                    Content = backup.DisplayName,
                    Tag = backup
                });
            }

            if (listView.Items.Count > 0)
            {
                listView.SelectedIndex = 0;
            }

            var panel = new StackPanel
            {
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"选择要还原的备份：{targetName}",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(listView);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "还原",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary &&
                listView.SelectedItem is ListViewItem { Tag: FolderBackupEntry selectedBackup }
                    ? selectedBackup
                    : null;
        }

        private async Task<ChapterEditorInput?> ShowChapterEditorDialogAsync(string title, ChapterInfo? chapter, UIElement? introContent = null)
        {
            if (_currentProject is null)
            {
                return null;
            }

            var nameBox = new TextBox
            {
                Header = "中文显示名称",
                Text = chapter?.Name ?? string.Empty,
                PlaceholderText = "例如：第一章 雨夜"
            };

            var typeBox = new ComboBox
            {
                Header = "章节类型",
                Width = 360
            };
            ComboBoxItem? selectedTypeItem = null;
            foreach (var chapterType in ChapterTypeOptions)
            {
                var item = new ComboBoxItem
                {
                    Content = chapterType.DisplayName,
                    Tag = chapterType
                };
                typeBox.Items.Add(item);
                if (string.Equals(chapter?.Type, chapterType.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    selectedTypeItem = item;
                }
            }

            typeBox.SelectedItem = selectedTypeItem ?? typeBox.Items.FirstOrDefault();

            var customCodeBox = new TextBox
            {
                Header = "自定义代号 / 编号",
                Text = chapter is null ? string.Empty : GetChapterCodeSegment(chapter.Code, _currentProject.Code),
                PlaceholderText = "主线/间章可留空；养成如 Kirito-3，活动如 AF，世界对话如 World1"
            };

            var previewText = new TextBlock
            {
                Style = TryGetTextBlockStyle("SubtleTextStyle")
            };

            void UpdatePreview()
            {
                var option = (typeBox.SelectedItem as ComboBoxItem)?.Tag as ChapterTypeOption ?? ChapterTypeOptions[0];
                var segment = BuildChapterCodeSegment(option.Kind, customCodeBox.Text.Trim());
                previewText.Text = $"生成代码：{_currentProject.Code}-{segment}";
            }

            typeBox.SelectionChanged += (_, _) => UpdatePreview();
            customCodeBox.TextChanged += (_, _) => UpdatePreview();
            UpdatePreview();

            var panel = new StackPanel
            {
                Spacing = 12
            };
            if (introContent is not null)
            {
                panel.Children.Add(introContent);
            }

            panel.Children.Add(nameBox);
            panel.Children.Add(typeBox);
            panel.Children.Add(customCodeBox);
            panel.Children.Add(previewText);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var name = nameBox.Text.Trim();
            var selectedOption = (typeBox.SelectedItem as ComboBoxItem)?.Tag as ChapterTypeOption ?? ChapterTypeOptions[0];
            var segmentCode = BuildChapterCodeSegment(selectedOption.Kind, customCodeBox.Text.Trim());
            var code = $"{_currentProject.Code}-{segmentCode}";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(segmentCode))
            {
                AppendLog(LogKind.Warning, "章节名称和章节代号不能为空。");
                return null;
            }

            if (!ValidateCardName(code, "章节英文代号"))
            {
                return null;
            }

            return new ChapterEditorInput(name, code, selectedOption.Kind);
        }

        private static ChapterInfo ReadChapterInfo(string chapterPath)
        {
            var metaPath = Path.Combine(chapterPath, ChapterMetaFileName);
            var meta = ReadJson<ChapterMeta>(metaPath) ?? new ChapterMeta();
            var fallbackCode = Path.GetFileName(chapterPath);
            return new ChapterInfo(
                string.IsNullOrWhiteSpace(meta.ChapterName) ? fallbackCode : meta.ChapterName!,
                string.IsNullOrWhiteSpace(meta.ChapterCode) ? fallbackCode : meta.ChapterCode!,
                string.IsNullOrWhiteSpace(meta.ChapterType) ? ChapterKind.MainThread : meta.ChapterType!,
                chapterPath,
                meta.LastEditedAt == default ? Directory.GetLastWriteTime(chapterPath) : meta.LastEditedAt,
                Math.Max(0, meta.LastEditedRowIndex));
        }

        private void WriteChapterMeta(string chapterPath, ChapterEditorInput input)
        {
            var metaPath = Path.Combine(chapterPath, ChapterMetaFileName);
            var existingMeta = ReadJson<ChapterMeta>(metaPath);
            var meta = new ChapterMeta
            {
                ChapterName = input.Name,
                ChapterCode = input.Code,
                ChapterType = input.Type,
                LastEditedAt = DateTime.Now,
                LastEditedRowIndex = Math.Max(0, existingMeta?.LastEditedRowIndex ?? 0)
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private static string GetChaptersFolderPath(ProjectInfo project)
        {
            return Path.Combine(project.Path, ChaptersFolderName);
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

            return Directory
                .EnumerateDirectories(GetChaptersFolderPath(_currentProject))
                .Select(ReadChapterInfo)
                .Select(chapter => chapter.Code)
                .ToList();
        }

        private void UpdateChapterProjectCodePrefix(string projectPath, string oldProjectCode, string newProjectCode)
        {
            var chaptersFolderPath = Path.Combine(projectPath, ChaptersFolderName);
            if (!Directory.Exists(chaptersFolderPath))
            {
                return;
            }

            var renamePlans = Directory
                .EnumerateDirectories(chaptersFolderPath)
                .Select(ReadChapterInfo)
                .Select(chapter =>
                {
                    var newCode = ReplaceChapterProjectCode(chapter.Code, oldProjectCode, newProjectCode);
                    var newPath = Path.Combine(chaptersFolderPath, SanitizeCharacterFolderName(newCode));
                    return new
                    {
                        Chapter = chapter,
                        NewCode = newCode,
                        NewPath = newPath
                    };
                })
                .Where(plan => !string.Equals(plan.Chapter.Code, plan.NewCode, StringComparison.Ordinal) ||
                    !PathsEqual(plan.Chapter.Path, plan.NewPath))
                .ToList();

            var duplicateTarget = renamePlans
                .GroupBy(plan => plan.NewPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateTarget is not null)
            {
                throw new InvalidOperationException($"章节代号同步后出现重复目录：{Path.GetFileName(duplicateTarget.Key)}");
            }

            foreach (var plan in renamePlans)
            {
                if (!PathsEqual(plan.Chapter.Path, plan.NewPath) && Directory.Exists(plan.NewPath))
                {
                    throw new InvalidOperationException($"章节代号同步目标已存在：{Path.GetFileName(plan.NewPath)}");
                }
            }

            foreach (var plan in renamePlans)
            {
                if (!PathsEqual(plan.Chapter.Path, plan.NewPath))
                {
                    Directory.Move(plan.Chapter.Path, plan.NewPath);
                }

                WriteChapterMeta(
                    plan.NewPath,
                    new ChapterEditorInput(plan.Chapter.Name, plan.NewCode, plan.Chapter.Type));
            }

            if (renamePlans.Count > 0)
            {
                AppendLog(LogKind.User, $"同步章节项目代号前缀：{oldProjectCode} -> {newProjectCode}，共 {renamePlans.Count} 个章节。");
            }
        }

        private static string ReplaceChapterProjectCode(string chapterCode, string oldProjectCode, string newProjectCode)
        {
            var oldPrefix = $"{oldProjectCode}-";
            if (chapterCode.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return $"{newProjectCode}-{chapterCode[oldPrefix.Length..]}";
            }

            var separatorIndex = chapterCode.IndexOf('-');
            return separatorIndex >= 0
                ? $"{newProjectCode}{chapterCode[separatorIndex..]}"
                : $"{newProjectCode}-{chapterCode}";
        }

        private static string GetChapterCodeSegment(string chapterCode, string projectCode)
        {
            var prefix = $"{projectCode}-";
            if (chapterCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return chapterCode[prefix.Length..];
            }

            var separatorIndex = chapterCode.IndexOf('-');
            return separatorIndex >= 0 ? chapterCode[(separatorIndex + 1)..] : chapterCode;
        }

        private static string SanitizeChapterCodeSegment(string code)
        {
            var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
            return new string(code.Trim().Where(ch => !invalidChars.Contains(ch) && !char.IsWhiteSpace(ch)).ToArray());
        }

        private void TouchProjectLastEditedAt(ProjectInfo project)
        {
            var toolsPath = Path.Combine(project.Path, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);

            var metaPath = Path.Combine(toolsPath, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            meta.ProjectName = project.Name;
            meta.ProjectCode = project.Code;
            meta.LastEditedAt = DateTime.Now;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private void TouchAssetLibraryLastEditedAt(AssetLibraryInfo assetLibrary)
        {
            var toolsPath = Path.Combine(assetLibrary.Path, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);

            var metaPath = Path.Combine(toolsPath, AssetLibraryMetaFileName);
            var meta = ReadJson<AssetLibraryMeta>(metaPath) ?? new AssetLibraryMeta();
            meta.AssetLibraryName = assetLibrary.Name;
            meta.LastEditedAt = DateTime.Now;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
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

            var newPath = Path.Combine(_projectRootPath, newName);
            if (!PathsEqual(project.Path, newPath))
            {
                if (Directory.Exists(newPath))
                {
                    AppendLog(LogKind.Warning, $"无法重命名项目，同名文件夹已存在：{newName}");
                    return;
                }

                Directory.Move(project.Path, newPath);
            }

            var toolsPath = Path.Combine(newPath, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);
            var metaPath = Path.Combine(toolsPath, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            meta.ProjectName = newName;
            meta.ProjectCode = project.Code;
            meta.LastEditedAt = DateTime.Now;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
            LoadProjects();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"重命名项目：{project.Name} → {newName}");
        }

        private async Task DeleteProjectAsync(ProjectInfo project)
        {
            var confirmed = await ShowDeleteConfirmDialogAsync("删除项目", $"确定删除项目 {project.Name} 吗？这会删除整个项目文件夹。");
            if (!confirmed)
            {
                return;
            }

            Directory.Delete(project.Path, recursive: true);
            LoadProjects();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"删除项目：{project.Name}");
        }

        private async Task ChangeProjectAssetLibraryAsync(ProjectInfo project)
        {
            var assetLibraries = GetAssetLibraries().OrderBy(library => library.Name).ToList();
            if (assetLibraries.Count == 0)
            {
                AppendLog(LogKind.Warning, "无法更改目标素材库：当前没有可选择的素材库。");
                return;
            }

            var comboBox = new ComboBox
            {
                Width = 360,
                Header = "目标素材库"
            };
            foreach (var assetLibrary in assetLibraries)
            {
                var item = new ComboBoxItem
                {
                    Content = assetLibrary.Name,
                    Tag = assetLibrary.FolderName
                };
                comboBox.Items.Add(item);
                if (string.Equals(assetLibrary.FolderName, project.AssetLibraryFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                }
            }

            comboBox.SelectedItem ??= comboBox.Items.FirstOrDefault();

            var dialog = new ContentDialog
            {
                Title = "更改目标素材库",
                Content = comboBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary ||
                comboBox.SelectedItem is not ComboBoxItem { Tag: string selectedFolderName })
            {
                return;
            }

            var selectedLibrary = assetLibraries.FirstOrDefault(library => library.FolderName == selectedFolderName);
            if (selectedLibrary is null)
            {
                AppendLog(LogKind.Warning, "无法更改目标素材库：选择的素材库不存在。");
                return;
            }

            var metaPath = Path.Combine(project.Path, ToolsFolderName, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            meta.ProjectName = project.Name;
            meta.AssetLibraryName = selectedLibrary.Name;
            meta.AssetLibraryFolderName = selectedLibrary.FolderName;
            meta.LastEditedAt = DateTime.Now;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
            LoadProjects();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"项目 {project.Name} 的目标素材库改为：{selectedLibrary.Name}");
        }

        private async Task SaveProjectSettingsAsync(ProjectInfo project, string name, string code)
        {
            if (!ValidateCardName(name, "项目名称") || !ValidateCardName(code, "项目英文代号"))
            {
                return;
            }

            var oldCode = project.Code;
            var newPath = Path.Combine(_projectRootPath, name);
            if (!PathsEqual(project.Path, newPath))
            {
                if (Directory.Exists(newPath))
                {
                    AppendLog(LogKind.Warning, $"无法保存项目设置，同名文件夹已存在：{name}");
                    return;
                }

                Directory.Move(project.Path, newPath);
            }

            var chapterPrefixChanged = !string.Equals(oldCode, code, StringComparison.Ordinal);
            if (chapterPrefixChanged)
            {
                UpdateChapterProjectCodePrefix(newPath, oldCode, code);
            }

            var toolsPath = Path.Combine(newPath, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);
            var metaPath = Path.Combine(toolsPath, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            meta.ProjectName = name;
            meta.ProjectCode = code;
            meta.LastEditedAt = DateTime.Now;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));

            var updatedProject = ReadProjectInfo(newPath);
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
            AppendLog(LogKind.User, $"保存项目设置：{updatedProject.Name}（{updatedProject.Code}）");
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
            var newPath = Path.Combine(_projectRootPath, newName);
            if (!PathsEqual(assetLibrary.Path, newPath))
            {
                if (Directory.Exists(newPath))
                {
                    AppendLog(LogKind.Warning, $"无法重命名素材库，同名文件夹已存在：{newName}");
                    return;
                }

                Directory.Move(assetLibrary.Path, newPath);
            }

            var toolsPath = Path.Combine(newPath, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);
            var metaPath = Path.Combine(toolsPath, AssetLibraryMetaFileName);
            var meta = ReadJson<AssetLibraryMeta>(metaPath) ?? new AssetLibraryMeta();
            meta.AssetLibraryName = newName;
            meta.LastEditedAt = DateTime.Now;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));

            UpdateProjectAssetLibraryReferences(oldFolderName, oldName, newName, Path.GetFileName(newPath));
            LoadAssetLibraries();
            LoadProjects();
            LoadAssetLibraryOptions();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"重命名素材库：{oldName} → {newName}");
        }

        private async Task DeleteAssetLibraryAsync(AssetLibraryInfo assetLibrary)
        {
            var confirmed = await ShowDeleteConfirmDialogAsync("删除素材库", $"确定删除素材库 {assetLibrary.Name} 吗？引用它的项目会变为未关联素材库。");
            if (!confirmed)
            {
                return;
            }

            ClearProjectAssetLibraryReferences(assetLibrary.FolderName, assetLibrary.Name);
            Directory.Delete(assetLibrary.Path, recursive: true);
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
            AppendLog(LogKind.User, $"删除素材库：{assetLibrary.Name}");
        }

        private void UpdateProjectAssetLibraryReferences(string oldFolderName, string oldName, string newName, string newFolderName)
        {
            foreach (var project in GetProjects())
            {
                var metaPath = Path.Combine(project.Path, ToolsFolderName, ProjectMetaFileName);
                var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
                if (string.Equals(meta.AssetLibraryFolderName, oldFolderName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(meta.AssetLibraryName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    meta.AssetLibraryName = newName;
                    meta.AssetLibraryFolderName = newFolderName;
                    meta.LastEditedAt = DateTime.Now;
                    File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
                }
            }
        }

        private void ClearProjectAssetLibraryReferences(string folderName, string name)
        {
            foreach (var project in GetProjects())
            {
                var metaPath = Path.Combine(project.Path, ToolsFolderName, ProjectMetaFileName);
                var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
                if (string.Equals(meta.AssetLibraryFolderName, folderName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(meta.AssetLibraryName, name, StringComparison.OrdinalIgnoreCase))
                {
                    meta.AssetLibraryName = null;
                    meta.AssetLibraryFolderName = null;
                    meta.LastEditedAt = DateTime.Now;
                    File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
                }
            }
        }

        private async Task<string?> ShowNameInputDialogAsync(string title, string header, string currentName)
        {
            var textBox = new TextBox
            {
                Width = 360,
                Header = header,
                Text = currentName
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = textBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? textBox.Text.Trim() : null;
        }

        private async Task<bool> ShowDeleteConfirmDialogAsync(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

        private void ShowCreateProjectPageButton_Click(object sender, RoutedEventArgs e)
        {
            ShowCreateProjectPage();
            AppendLog(LogKind.User, "打开创建项目页面。");
        }

        private void ShowCreateAssetLibraryPageButton_Click(object sender, RoutedEventArgs e)
        {
            ShowCreateAssetLibraryPage();
            AppendLog(LogKind.User, "打开创建素材库页面。");
        }

        private void RefreshProjectsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadProjects();
            AppendLog(LogKind.User, "手动刷新项目列表。");
        }

        private void RefreshAssetLibrariesButton_Click(object sender, RoutedEventArgs e)
        {
            LoadAssetLibraries();
            AppendLog(LogKind.User, "手动刷新素材库列表。");
        }

        private void CancelCreateProjectButton_Click(object sender, RoutedEventArgs e)
        {
            ShowWorkbenchPage();
        }

        private void CancelCreateAssetLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAssetLibraryPage();
        }

        private async void ChooseProjectThumbnailButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPath = await PickThumbnailPathAsync();
            if (selectedPath is null)
            {
                return;
            }

            _selectedProjectThumbnailPath = selectedPath;
            CreateProjectThumbnailImage.Source = new BitmapImage(new Uri(selectedPath));
            CreateProjectThumbnailImage.Visibility = Visibility.Visible;
            CreateProjectDefaultThumbnail.Visibility = Visibility.Collapsed;
            AppendLog(LogKind.User, $"已选择项目缩略图：{selectedPath}");
        }

        private async void ChooseAssetLibraryThumbnailButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPath = await PickThumbnailPathAsync();
            if (selectedPath is null)
            {
                return;
            }

            _selectedAssetLibraryThumbnailPath = selectedPath;
            CreateAssetLibraryThumbnailImage.Source = new BitmapImage(new Uri(selectedPath));
            CreateAssetLibraryThumbnailImage.Visibility = Visibility.Visible;
            CreateAssetLibraryDefaultThumbnail.Visibility = Visibility.Collapsed;
            AppendLog(LogKind.User, $"已选择素材库缩略图：{selectedPath}");
        }

        private async System.Threading.Tasks.Task<string?> PickThumbnailPathAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".webp");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFile = await picker.PickSingleFileAsync();
            return selectedFile?.Path;
        }

        private void CreateProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var projectName = CreateProjectNameTextBox.Text.Trim();
            if (!ValidateFolderName(projectName, "项目名称", ShowCreateProjectError))
            {
                return;
            }

            var projectCode = CreateProjectCodeTextBox.Text.Trim();
            if (!ValidateFolderName(projectCode, "项目英文代号", ShowCreateProjectError))
            {
                return;
            }

            if (ProjectAssetLibraryComboBox.SelectedItem is not ComboBoxItem { Tag: string assetLibraryFolderName })
            {
                ShowCreateProjectError("请先创建并选择一个素材库。");
                return;
            }

            var assetLibrary = GetAssetLibraries().FirstOrDefault(library => library.FolderName == assetLibraryFolderName);
            if (assetLibrary is null)
            {
                ShowCreateProjectError("选择的素材库不存在，请刷新后重试。");
                return;
            }

            var projectPath = Path.Combine(_projectRootPath, $"项目-{projectName}");
            if (Directory.Exists(projectPath))
            {
                ShowCreateProjectError("同名文件夹已经存在。");
                return;
            }

            var toolsPath = Path.Combine(projectPath, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);
            Directory.CreateDirectory(Path.Combine(projectPath, ExcelFolderName));

            var thumbnailFileName = CopyThumbnailToTools(_selectedProjectThumbnailPath, toolsPath);
            var meta = new ProjectMeta
            {
                ProjectName = projectName,
                ProjectCode = projectCode,
                ThumbnailFileName = thumbnailFileName,
                AssetLibraryName = assetLibrary.Name,
                AssetLibraryFolderName = assetLibrary.FolderName,
                LastEditedAt = DateTime.Now
            };
            File.WriteAllText(Path.Combine(toolsPath, ProjectMetaFileName), JsonSerializer.Serialize(meta, _jsonOptions));
            Directory.CreateDirectory(Path.Combine(projectPath, ChaptersFolderName));

            ResetCreateProjectForm();
            ShowWorkbenchPage();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"创建项目：{projectName}，关联素材库：{assetLibrary.Name}");
        }

        private void CreateAssetLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            var assetLibraryName = CreateAssetLibraryNameTextBox.Text.Trim();
            if (!ValidateFolderName(assetLibraryName, "素材库名称", ShowCreateAssetLibraryError))
            {
                return;
            }

            var assetLibraryPath = Path.Combine(_projectRootPath, $"素材库-{assetLibraryName}");
            if (Directory.Exists(assetLibraryPath))
            {
                ShowCreateAssetLibraryError("同名文件夹已经存在。");
                return;
            }

            var toolsPath = Path.Combine(assetLibraryPath, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, ExcelFolderName));
            EnsureAssetLibraryCategoryFolders(assetLibraryPath);

            var thumbnailFileName = CopyThumbnailToTools(_selectedAssetLibraryThumbnailPath, toolsPath);
            var meta = new AssetLibraryMeta
            {
                AssetLibraryName = assetLibraryName,
                ThumbnailFileName = thumbnailFileName,
                LastEditedAt = DateTime.Now
            };
            File.WriteAllText(Path.Combine(toolsPath, AssetLibraryMetaFileName), JsonSerializer.Serialize(meta, _jsonOptions));

            ResetCreateAssetLibraryForm();
            ShowAssetLibraryPage();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"创建素材库：{assetLibraryName}");
        }

        private static void EnsureAssetLibraryCategoryFolders(string assetLibraryPath)
        {
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, BackgroundFolderName));
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, CharacterFolderName));
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, MusicFolderName));
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, AmbientSoundFolderName));
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, SoundEffectFolderName));
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, FunctionFolderName));
            Directory.CreateDirectory(Path.Combine(assetLibraryPath, CharacterFilterFolderName));
        }

        private static bool ValidateFolderName(string value, string label, Action<string> showError)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                showError($"请输入{label}。");
                return false;
            }

            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                showError($"{label}包含不能用于文件夹名称的字符。");
                return false;
            }

            return true;
        }

        private static string? CopyThumbnailToTools(string? sourcePath, string toolsPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return null;
            }

            var extension = Path.GetExtension(sourcePath);
            var thumbnailFileName = $"thumbnail{extension}";
            File.Copy(sourcePath, Path.Combine(toolsPath, thumbnailFileName), overwrite: true);
            return thumbnailFileName;
        }

        private void ShowCreateProjectError(string message)
        {
            CreateProjectInfoBar.Title = "无法创建项目";
            CreateProjectInfoBar.Message = message;
            CreateProjectInfoBar.Severity = InfoBarSeverity.Error;
            CreateProjectInfoBar.IsOpen = true;
            AppendLog(LogKind.Error, $"无法创建项目：{message}");
        }

        private void ShowCreateAssetLibraryError(string message)
        {
            CreateAssetLibraryInfoBar.Title = "无法创建素材库";
            CreateAssetLibraryInfoBar.Message = message;
            CreateAssetLibraryInfoBar.Severity = InfoBarSeverity.Error;
            CreateAssetLibraryInfoBar.IsOpen = true;
            AppendLog(LogKind.Error, $"无法创建素材库：{message}");
        }

        private void ResetCreateProjectForm()
        {
            _selectedProjectThumbnailPath = null;
            CreateProjectNameTextBox.Text = string.Empty;
            CreateProjectCodeTextBox.Text = string.Empty;
            ProjectAssetLibraryComboBox.SelectedItem = null;
            CreateProjectInfoBar.IsOpen = false;
            CreateProjectThumbnailImage.Source = null;
            CreateProjectThumbnailImage.Visibility = Visibility.Collapsed;
            CreateProjectDefaultThumbnail.Visibility = Visibility.Visible;
        }

        private void ResetCreateAssetLibraryForm()
        {
            _selectedAssetLibraryThumbnailPath = null;
            CreateAssetLibraryNameTextBox.Text = string.Empty;
            CreateAssetLibraryInfoBar.IsOpen = false;
            CreateAssetLibraryThumbnailImage.Source = null;
            CreateAssetLibraryThumbnailImage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryDefaultThumbnail.Visibility = Visibility.Visible;
        }

        private void BackToAssetLibraryPageButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAssetLibraryPage();
        }

        private async void AddBackgroundImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in ImageExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            var backgroundFolderPath = GetBackgroundFolderPath(_currentAssetLibrary);
            Directory.CreateDirectory(backgroundFolderPath);

            try
            {
                var importedCount = await ImportBackgroundImagesAsync(selectedFiles.Select(file => file.Path));
                AppendLog(LogKind.User, $"导入背景图：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "导入背景图失败。", ex);
                AssetLibraryDetailStatusText.Text = $"导入背景图失败：{ex.Message}";
            }
        }

        private async Task<int> ImportBackgroundImagesAsync(IEnumerable<string> sourcePaths)
        {
            if (_currentAssetLibrary is null)
            {
                return 0;
            }

            var validSourcePaths = sourcePaths
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path) &&
                    ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (validSourcePaths.Count == 0)
            {
                return 0;
            }

            var backgroundFolderPath = GetBackgroundFolderPath(_currentAssetLibrary);
            Directory.CreateDirectory(backgroundFolderPath);
            var existingOrderedPaths = GetBackgroundImagePaths(backgroundFolderPath);
            var importedEntries = new List<BackgroundImageEntry>();

            foreach (var sourcePath in validSourcePaths)
            {
                var tempPngPath = Path.Combine(backgroundFolderPath, $"__bg_import_{Guid.NewGuid():N}.png");
                await ImportBackgroundImageAsPngAsync(sourcePath, tempPngPath);
                importedEntries.Add(new BackgroundImageEntry(tempPngPath, SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath))));
            }

            _isNormalizingBackgroundImages = true;
            try
            {
                var entries = existingOrderedPaths
                    .Select(ParseBackgroundImageFileName)
                    .Concat(importedEntries)
                    .ToList();
                await RenameBackgroundEntriesAsync(entries);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            return validSourcePaths.Count;
        }

        private async void AddMusicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var extension in MusicExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            var importedCount = await ImportMusicFilesAsync(selectedFiles.Select(file => file.Path));
            AppendLog(LogKind.User, $"导入音乐：{importedCount} 个文件。");
        }

        private async Task<int> ImportMusicFilesAsync(IEnumerable<string> sourcePaths)
        {
            return await ImportAudioFilesAsync(AudioAssetKind.Music, sourcePaths);
        }

        private async void AddAmbientSoundButton_Click(object sender, RoutedEventArgs e)
        {
            await AddAudioFilesAsync(AudioAssetKind.Ambient);
        }

        private async void AddSoundEffectButton_Click(object sender, RoutedEventArgs e)
        {
            await AddAudioFilesAsync(AudioAssetKind.SoundEffect);
        }

        private async Task AddAudioFilesAsync(AudioAssetKind kind)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var extension in MusicExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            var importedCount = await ImportAudioFilesAsync(kind, selectedFiles.Select(file => file.Path));
            AppendLog(LogKind.User, $"导入{GetAudioDisplayName(kind)}：{importedCount} 个文件。");
        }

        private async Task<int> ImportAudioFilesAsync(AudioAssetKind kind, IEnumerable<string> sourcePaths)
        {
            if (_currentAssetLibrary is null)
            {
                return 0;
            }

            var validSourcePaths = sourcePaths
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path) &&
                    MusicExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (validSourcePaths.Count == 0)
            {
                return 0;
            }

            var musicFolderPath = GetAudioFolderPath(_currentAssetLibrary, kind);
            Directory.CreateDirectory(musicFolderPath);
            var existingOrderedPaths = GetAudioFilePaths(musicFolderPath);
            var importedEntries = new List<MusicEntry>();

            foreach (var sourcePath in validSourcePaths)
            {
                var tempWavPath = Path.Combine(musicFolderPath, $"__{GetAudioPrefix(kind).ToLowerInvariant()}_import_{Guid.NewGuid():N}.wav");
                File.Copy(sourcePath, tempWavPath, overwrite: true);
                importedEntries.Add(new MusicEntry(tempWavPath, SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath))));
            }

            SetAudioNormalizing(kind, true);
            try
            {
                var entries = existingOrderedPaths
                    .Select(path => ParseAudioFileName(kind, path))
                    .Concat(importedEntries)
                    .ToList();
                await RenameAudioEntriesAsync(kind, entries);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            return validSourcePaths.Count;
        }

        private async void AddCharacterClothesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in ImageExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            try
            {
                var importedCount = await ImportCharacterClothesAsync(selectedFiles.Select(file => file.Path));
                AppendLog(LogKind.User, $"导入服装：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "导入服装失败。", ex);
            }
        }

        private Task<int> ImportCharacterClothesAsync(IEnumerable<string> sourcePaths)
        {
            if (_currentCharacter is null)
            {
                return Task.FromResult(0);
            }

            var validSourcePaths = sourcePaths
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path) &&
                    ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (validSourcePaths.Count == 0)
            {
                return Task.FromResult(0);
            }

            var clothFolderPath = Path.Combine(_currentCharacter.Path, "DN_Cloth");
            Directory.CreateDirectory(clothFolderPath);
            var existingOrderedPaths = GetCharacterLayerImagePaths(clothFolderPath);
            var importedEntries = new List<CharacterLayerEntry>();

            foreach (var sourcePath in validSourcePaths)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                var tempPath = Path.Combine(clothFolderPath, $"__dn_import_{Guid.NewGuid():N}{extension}");
                File.Copy(sourcePath, tempPath, overwrite: true);
                importedEntries.Add(new CharacterLayerEntry(
                    tempPath,
                    SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath)),
                    string.Empty));
            }

            _isNormalizingCharacterClothes = true;
            try
            {
                var entries = existingOrderedPaths
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Cloth, string.Empty))
                    .Concat(importedEntries)
                    .ToList();
                RenameCharacterLayerEntries(entries, CharacterLayerKind.Cloth, _currentCharacter.Code);
            }
            finally
            {
                _isNormalizingCharacterClothes = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            return Task.FromResult(validSourcePaths.Count);
        }

        private async void AddCharacterFacesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in ImageExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            try
            {
                var importedCount = await ImportCharacterFacesAsync(selectedFiles.Select(file => file.Path));
                AppendLog(LogKind.User, $"导入表情：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "导入表情失败。", ex);
            }
        }

        private Task<int> ImportCharacterFacesAsync(IEnumerable<string> sourcePaths)
        {
            if (_currentCharacter is null)
            {
                return Task.FromResult(0);
            }

            var validSourcePaths = sourcePaths
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path) &&
                    ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (validSourcePaths.Count == 0)
            {
                return Task.FromResult(0);
            }

            var faceFolderPath = Path.Combine(_currentCharacter.Path, "FC_Face");
            Directory.CreateDirectory(faceFolderPath);
            var existingOrderedPaths = GetCharacterLayerImagePaths(faceFolderPath);
            var importedEntries = new List<CharacterLayerEntry>();

            foreach (var sourcePath in validSourcePaths)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                var tempPath = Path.Combine(faceFolderPath, $"__fc_import_{Guid.NewGuid():N}{extension}");
                File.Copy(sourcePath, tempPath, overwrite: true);
                importedEntries.Add(new CharacterLayerEntry(
                    tempPath,
                    SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath)),
                    string.Empty));
            }

            _isNormalizingCharacterFaces = true;
            try
            {
                var entries = existingOrderedPaths
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Face, string.Empty))
                    .Concat(importedEntries)
                    .ToList();
                RenameCharacterFaceEntriesAndUpdateMeta(entries);
            }
            finally
            {
                _isNormalizingCharacterFaces = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            return Task.FromResult(validSourcePaths.Count);
        }

        private async void AddCharacterAdornsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in ImageExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            try
            {
                var importedCount = await ImportCharacterAdornsAsync(selectedFiles.Select(file => file.Path));
                AppendLog(LogKind.User, $"导入装饰：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "导入装饰失败。", ex);
            }
        }

        private Task<int> ImportCharacterAdornsAsync(IEnumerable<string> sourcePaths)
        {
            if (_currentCharacter is null)
            {
                return Task.FromResult(0);
            }

            var validSourcePaths = sourcePaths
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path) &&
                    ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (validSourcePaths.Count == 0)
            {
                return Task.FromResult(0);
            }

            var adornFolderPath = Path.Combine(_currentCharacter.Path, "AD_Adorn");
            Directory.CreateDirectory(adornFolderPath);
            var existingOrderedPaths = GetCharacterLayerImagePaths(adornFolderPath);
            var importedEntries = new List<CharacterLayerEntry>();

            foreach (var sourcePath in validSourcePaths)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                var tempPath = Path.Combine(adornFolderPath, $"__ad_import_{Guid.NewGuid():N}{extension}");
                File.Copy(sourcePath, tempPath, overwrite: true);
                importedEntries.Add(new CharacterLayerEntry(
                    tempPath,
                    SanitizeRemark(Path.GetFileNameWithoutExtension(sourcePath)),
                    string.Empty));
            }

            _isNormalizingCharacterAdorns = true;
            try
            {
                var entries = existingOrderedPaths
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Adorn, string.Empty))
                    .Concat(importedEntries)
                    .ToList();
                RenameCharacterAdornEntriesAndUpdateMeta(entries);
            }
            finally
            {
                _isNormalizingCharacterAdorns = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            return Task.FromResult(validSourcePaths.Count);
        }

        private async void LoadBackgroundImages(AssetLibraryInfo assetLibrary)
        {
            await ReloadBackgroundImagesAsync(assetLibrary);
        }

        private async Task ReloadBackgroundImagesAsync(AssetLibraryInfo assetLibrary)
        {
            var backgroundFolderPath = GetBackgroundFolderPath(assetLibrary);
            Directory.CreateDirectory(backgroundFolderPath);

            if (!_isNormalizingBackgroundImages)
            {
                _isNormalizingBackgroundImages = true;
                try
                {
                    await NormalizeBackgroundImagesAsync(backgroundFolderPath);
                }
                finally
                {
                    _isNormalizingBackgroundImages = false;
                }
            }

            RefreshBackgroundImageCards(assetLibrary);
        }

        private async void LoadMusicFiles(AssetLibraryInfo assetLibrary)
        {
            await ReloadMusicFilesAsync(assetLibrary);
        }

        private async Task ReloadMusicFilesAsync(AssetLibraryInfo assetLibrary)
        {
            await ReloadAudioFilesAsync(assetLibrary, AudioAssetKind.Music);
        }

        private async void LoadAmbientSoundFiles(AssetLibraryInfo assetLibrary)
        {
            await ReloadAudioFilesAsync(assetLibrary, AudioAssetKind.Ambient);
        }

        private async void LoadSoundEffectFiles(AssetLibraryInfo assetLibrary)
        {
            await ReloadAudioFilesAsync(assetLibrary, AudioAssetKind.SoundEffect);
        }

        private async Task ReloadAudioFilesAsync(AssetLibraryInfo assetLibrary, AudioAssetKind kind)
        {
            var musicFolderPath = GetAudioFolderPath(assetLibrary, kind);
            Directory.CreateDirectory(musicFolderPath);

            if (!IsAudioNormalizing(kind))
            {
                SetAudioNormalizing(kind, true);
                try
                {
                    await NormalizeAudioFilesAsync(kind, musicFolderPath);
                }
                finally
                {
                    SetAudioNormalizing(kind, false);
                }
            }

            RefreshAudioCards(assetLibrary, kind);
        }

        private void RefreshBackgroundImageCards(AssetLibraryInfo assetLibrary)
        {
            var backgroundFolderPath = GetBackgroundFolderPath(assetLibrary);
            var imagePaths = GetBackgroundImagePaths(backgroundFolderPath);

            BackgroundImagesGridView.Items.Clear();
            foreach (var imagePath in imagePaths)
            {
                BackgroundImagesGridView.Items.Add(CreateBackgroundImageCard(imagePath));
            }

            BackgroundImagesExpander.Header = $"背景图 [数量：{imagePaths.Count}]";
            AssetLibraryDetailStatusText.Text = $"背景图：{imagePaths.Count} 个文件 | {backgroundFolderPath}";
            AppendLog(LogKind.Info, $"已加载背景图：{imagePaths.Count} 个文件。");
        }

        private GridViewItem CreateBackgroundImageCard(string imagePath)
        {
            var item = new GridViewItem
            {
                Width = 160,
                Height = 190,
                Margin = new Thickness(0, 0, 14, 14),
                Tag = imagePath
            };

            var flyout = new MenuFlyout();
            var remarkItem = new MenuFlyoutItem
            {
                Text = "设置备注"
            };
            remarkItem.Click += async (_, _) => await SetBackgroundImageRemarkAsync(imagePath);
            flyout.Items.Add(remarkItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除"
            };
            deleteItem.Click += async (_, _) => await DeleteBackgroundImageAsync(imagePath);
            flyout.Items.Add(deleteItem);
            item.ContextFlyout = flyout;
            item.Tapped += (_, _) => ShowBackgroundImageViewerPage(imagePath);

            var panel = new StackPanel
            {
                Spacing = 6
            };

            panel.Children.Add(CreateThumbnail(imagePath, 148, 148, showAddIcon: false));
            panel.Children.Add(new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(imagePath),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });

            item.Content = panel;
            return item;
        }

        private void RefreshMusicCards(AssetLibraryInfo assetLibrary)
        {
            RefreshAudioCards(assetLibrary, AudioAssetKind.Music);
        }

        private void RefreshAudioCards(AssetLibraryInfo assetLibrary, AudioAssetKind kind)
        {
            var musicFolderPath = GetAudioFolderPath(assetLibrary, kind);
            var musicPaths = GetAudioFilePaths(musicFolderPath);
            var gridView = GetAudioGridView(kind);

            gridView.Items.Clear();
            foreach (var musicPath in musicPaths)
            {
                gridView.Items.Add(CreateAudioCard(kind, musicPath));
            }

            switch (kind)
            {
                case AudioAssetKind.Music:
                    MusicExpander.Header = $"音乐 [数量：{musicPaths.Count}]";
                    break;
                case AudioAssetKind.Ambient:
                    AmbientSoundExpander.Header = $"环境音 [数量：{musicPaths.Count}]";
                    break;
                case AudioAssetKind.SoundEffect:
                    SoundEffectExpander.Header = $"特殊音效 [数量：{musicPaths.Count}]";
                    break;
            }
            AppendLog(LogKind.Info, $"已加载{GetAudioDisplayName(kind)}：{musicPaths.Count} 个文件。");
        }

        private void LoadCharacters(AssetLibraryInfo assetLibrary)
        {
            var characterFolderPath = GetCharacterFolderPath(assetLibrary);
            Directory.CreateDirectory(characterFolderPath);
            var characters = Directory
                .EnumerateDirectories(characterFolderPath)
                .Select(ReadCharacterInfo)
                .OrderBy(character => character.Name)
                .ToList();

            CharacterGridView.Items.Clear();
            foreach (var character in characters)
            {
                CharacterGridView.Items.Add(CreateCharacterCard(character));
            }

            CharacterGridView.Items.Add(CreateAddCharacterCard());
            CharactersExpander.Header = $"立绘 [数量：{characters.Count}]";
            AppendLog(LogKind.Info, $"已加载角色卡：{characters.Count} 个。");
        }

        private void LoadFunctions(AssetLibraryInfo assetLibrary)
        {
            var functions = ReadFunctions(assetLibrary);
            FunctionGridView.Items.Clear();
            foreach (var function in functions)
            {
                FunctionGridView.Items.Add(CreateFunctionCard(function));
            }

            FunctionGridView.Items.Add(CreateAddFunctionCard());
            FunctionExpander.Header = $"函数 [数量：{functions.Count}]";
            AppendLog(LogKind.Info, $"已加载函数卡：{functions.Count} 个。");
        }

        private List<FunctionEntry> ReadFunctions(AssetLibraryInfo assetLibrary)
        {
            var folderPath = GetFunctionFolderPath(assetLibrary);
            Directory.CreateDirectory(folderPath);
            var indexPath = GetFunctionIndexPath(assetLibrary);
            var index = ReadJson<FunctionIndex>(indexPath);
            if (index?.Entries is not { Count: > 0 })
            {
                var defaults = CreateDefaultFunctions();
                WriteFunctions(assetLibrary, defaults);
                return defaults;
            }

            var changed = false;
            var normalized = index.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Indicator))
                .Select(entry =>
                {
                    var id = entry.Id;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        id = Guid.NewGuid().ToString("N");
                        changed = true;
                    }

                    var name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Indicator : entry.Name.Trim();
                    var category = string.IsNullOrWhiteSpace(entry.Category) ? "自定义" : entry.Category.Trim();
                    var choiceNotes = (entry.ChoiceNotes ?? [])
                        .Select(NormalizeFunctionChoiceNote)
                        .Where(note => !string.IsNullOrWhiteSpace(note))
                        .ToList();
                    var originalChoiceNotes = entry.ChoiceNotes ?? [];
                    if (id != entry.Id ||
                        name != entry.Name ||
                        category != entry.Category ||
                        !choiceNotes.SequenceEqual(originalChoiceNotes))
                    {
                        changed = true;
                    }

                    return entry with
                    {
                        Id = id,
                        Name = name,
                        Indicator = entry.Indicator.Trim(),
                        Category = category,
                        ChoiceNotes = choiceNotes
                    };
                })
                .ToList();

            changed |= EnsureBuiltInFunctionTemplates(normalized);

            if (changed || normalized.Count != index.Entries.Count)
            {
                WriteFunctions(assetLibrary, normalized);
            }

            return normalized;
        }

        private void WriteFunctions(AssetLibraryInfo assetLibrary, IReadOnlyList<FunctionEntry> functions)
        {
            var folderPath = GetFunctionFolderPath(assetLibrary);
            Directory.CreateDirectory(folderPath);
            var index = new FunctionIndex
            {
                Entries = functions.ToList()
            };
            File.WriteAllText(GetFunctionIndexPath(assetLibrary), JsonSerializer.Serialize(index, _jsonOptions));
        }

        private static List<FunctionEntry> CreateDefaultFunctions()
        {
            return
            [
                CreateChoiceFunctionTemplate(),
                CreateChapterJumpFunctionTemplate(),
                CreateSegmentJumpFunctionTemplate(),
                new FunctionEntry("default-scene-sfx", "播放一次性特殊音效", "Scene_", "音频", []),
                new FunctionEntry("default-bglerp-mode", "背景切换模式", "BGLerpMode_", "背景", []),
                new FunctionEntry("default-vfx-on", "开启指定特效", "VFXON_", "特效", []),
                new FunctionEntry("default-vfx-off", "关闭指定特效", "VFXOFF_", "特效", []),
                new FunctionEntry("default-transanim", "播放动画序列", "TransAnim_", "动画", []),
                new FunctionEntry("default-transanim-end", "停止目前动画", "TransAnim_END", "动画", []),
                new FunctionEntry("default-medplay", "播放视频", "MedPlay_", "视频", []),
                CreateBgmFunctionTemplate(),
                new FunctionEntry("default-title-show", "大标题显示", "TitleShowMode", "标题", []),
                new FunctionEntry("default-close-all-fx", "关闭所有特效", "CloseAllFX", "特效", []),
                new FunctionEntry("default-custom", "纯自定义函数", "CustomFunction", "自定义", [])
            ];
        }

        private static FunctionEntry CreateChoiceFunctionTemplate()
        {
            return new FunctionEntry(ChoiceFunctionTemplateId, "创建触发选项", ChoiceFunctionTemplateIndicator, ChoiceFunctionCategory, []);
        }

        private static FunctionEntry CreateChapterJumpFunctionTemplate()
        {
            return new FunctionEntry(ChapterJumpFunctionTemplateId, "跳转章节", ChapterJumpFunctionTemplateIndicator, JumpFunctionCategory, []);
        }

        private static FunctionEntry CreateSegmentJumpFunctionTemplate()
        {
            return new FunctionEntry(SegmentJumpFunctionTemplateId, "跳转小节", SegmentJumpFunctionTemplateIndicator, JumpFunctionCategory, []);
        }

        private static FunctionEntry CreateBgmFunctionTemplate()
        {
            return new FunctionEntry(BgmFunctionTemplateId, "BGM", BgmFunctionTemplateIndicator, "音频", []);
        }

        private static bool EnsureBuiltInFunctionTemplates(List<FunctionEntry> functions)
        {
            var changed = false;
            changed |= RemoveLegacyBgmFunctionTemplates(functions);
            changed |= EnsureBuiltInFunctionTemplate(functions, CreateChoiceFunctionTemplate(), IsChoiceFunctionTemplate, 0);
            changed |= EnsureBuiltInFunctionTemplate(functions, CreateChapterJumpFunctionTemplate(), IsChapterJumpFunctionTemplate, 1);
            changed |= EnsureBuiltInFunctionTemplate(functions, CreateSegmentJumpFunctionTemplate(), IsSegmentJumpFunctionTemplate, 2);
            changed |= EnsureBuiltInFunctionTemplate(functions, CreateBgmFunctionTemplate(), IsBgmFunctionTemplate, 3);
            return changed;
        }

        private static bool RemoveLegacyBgmFunctionTemplates(List<FunctionEntry> functions)
        {
            var removed = functions.RemoveAll(function =>
                string.Equals(function.Id, "default-bgm-start", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Id, "default-bgm-stop", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Indicator, "BGM_Start", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Indicator, "BGM_Stop", StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }

        private static bool EnsureBuiltInFunctionTemplate(
            List<FunctionEntry> functions,
            FunctionEntry target,
            Func<FunctionEntry, bool> isTemplate,
            int desiredIndex)
        {
            var templateIndex = functions.FindIndex(function =>
                string.Equals(function.Id, target.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Indicator, target.Indicator, StringComparison.OrdinalIgnoreCase) ||
                isTemplate(function));
            desiredIndex = Math.Clamp(desiredIndex, 0, functions.Count);
            if (templateIndex < 0)
            {
                functions.Insert(desiredIndex, target);
                return true;
            }

            var template = functions[templateIndex];
            var normalizedTemplate = template with
            {
                Id = target.Id,
                Name = string.IsNullOrWhiteSpace(template.Name) ? target.Name : template.Name.Trim(),
                Indicator = target.Indicator,
                Category = target.Category,
                ChoiceNotes = []
            };
            var changed =
                !string.Equals(normalizedTemplate.Id, template.Id, StringComparison.Ordinal) ||
                !string.Equals(normalizedTemplate.Name, template.Name, StringComparison.Ordinal) ||
                !string.Equals(normalizedTemplate.Indicator, template.Indicator, StringComparison.Ordinal) ||
                !string.Equals(normalizedTemplate.Category, template.Category, StringComparison.Ordinal) ||
                (template.ChoiceNotes?.Count ?? 0) != 0 ||
                templateIndex != desiredIndex;

            functions.RemoveAt(templateIndex);
            if (templateIndex < desiredIndex)
            {
                desiredIndex--;
            }

            desiredIndex = Math.Clamp(desiredIndex, 0, functions.Count);
            functions.Insert(desiredIndex, normalizedTemplate);
            return changed;
        }

        private static bool EnsureChoiceFunctionTemplate(List<FunctionEntry> functions)
        {
            var templateIndex = functions.FindIndex(function => string.Equals(function.Id, ChoiceFunctionTemplateId, StringComparison.OrdinalIgnoreCase));
            if (templateIndex < 0)
            {
                functions.Insert(0, CreateChoiceFunctionTemplate());
                return true;
            }

            var template = functions[templateIndex];
            var normalizedTemplate = template with
            {
                Name = string.IsNullOrWhiteSpace(template.Name) ? "创建触发选项" : template.Name.Trim(),
                Indicator = ChoiceFunctionTemplateIndicator,
                Category = ChoiceFunctionCategory,
                ChoiceNotes = []
            };
            if (normalizedTemplate == template && templateIndex == 0)
            {
                return false;
            }

            functions[templateIndex] = normalizedTemplate;
            if (templateIndex > 0)
            {
                functions.RemoveAt(templateIndex);
                functions.Insert(0, normalizedTemplate);
            }

            return true;
        }

        private static bool IsChoiceFunctionTemplate(FunctionEntry function)
        {
            return string.Equals(function.Id, ChoiceFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Indicator, ChoiceFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChapterJumpFunctionTemplate(FunctionEntry function)
        {
            return string.Equals(function.Id, ChapterJumpFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Indicator, ChapterJumpFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSegmentJumpFunctionTemplate(FunctionEntry function)
        {
            return string.Equals(function.Id, SegmentJumpFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Indicator, SegmentJumpFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBgmFunctionTemplate(FunctionEntry function)
        {
            return string.Equals(function.Id, BgmFunctionTemplateId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(function.Indicator, BgmFunctionTemplateIndicator, StringComparison.OrdinalIgnoreCase);
        }

        private async void AddFunctionButton_Click(object sender, RoutedEventArgs e)
        {
            await AddFunctionAsync();
        }

        private async Task AddFunctionAsync()
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var input = await ShowFunctionEditorDialogAsync("新建函数", null);
            if (input is null)
            {
                return;
            }

            var functions = ReadFunctions(_currentAssetLibrary);
            functions.Add(new FunctionEntry(Guid.NewGuid().ToString("N"), input.Name, input.Indicator, input.Category, input.ChoiceNotes));
            WriteFunctions(_currentAssetLibrary, functions);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadFunctions(_currentAssetLibrary);
            RequestDelayedRefresh();
        }

        private async Task EditFunctionAsync(FunctionEntry function)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var input = await ShowFunctionEditorDialogAsync("修改函数", function);
            if (input is null)
            {
                return;
            }

            var functions = ReadFunctions(_currentAssetLibrary)
                .Select(entry => entry.Id == function.Id ? entry with
                {
                    Name = input.Name,
                    Indicator = input.Indicator,
                    Category = input.Category,
                    ChoiceNotes = input.ChoiceNotes
                } : entry)
                .ToList();
            WriteFunctions(_currentAssetLibrary, functions);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadFunctions(_currentAssetLibrary);
            RequestDelayedRefresh();
        }

        private async Task DeleteFunctionAsync(FunctionEntry function)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var confirmed = await ShowDeleteConfirmDialogAsync("删除函数", $"确定删除函数 {function.Name}（{function.Indicator}）吗？已经填入剧情 CSV 的 Custom 字段不会自动清空。");
            if (!confirmed)
            {
                return;
            }

            var functions = ReadFunctions(_currentAssetLibrary)
                .Where(entry => entry.Id != function.Id)
                .ToList();
            WriteFunctions(_currentAssetLibrary, functions);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadFunctions(_currentAssetLibrary);
            RequestDelayedRefresh();
        }

        private async Task<FunctionEditorInput?> ShowFunctionEditorDialogAsync(string title, FunctionEntry? function)
        {
            var isNew = function is null;
            var suggestedChoiceIndicator = isNew ? BuildSuggestedChoiceFunctionIndicator() : string.Empty;
            var nameBox = new TextBox
            {
                Width = 420,
                Header = "中文名称",
                Text = function?.Name ?? (string.IsNullOrWhiteSpace(suggestedChoiceIndicator) ? string.Empty : ChoiceFunctionCategory),
                PlaceholderText = "例如：播放一次性特殊音效"
            };
            var indicatorBox = new TextBox
            {
                Width = 420,
                Header = "函数指示器",
                Text = function?.Indicator ?? suggestedChoiceIndicator,
                PlaceholderText = "例如：Scene_、BGM_Stop、M2-04-Choice2"
            };
            var categoryBox = new TextBox
            {
                Width = 420,
                Header = "分类",
                Text = function?.Category ?? (string.IsNullOrWhiteSpace(suggestedChoiceIndicator) ? "自定义" : ChoiceFunctionCategory),
                PlaceholderText = "音频 / 背景 / 特效 / 动画 / 视频 / 标题 / 触发选项 / 自定义"
            };
            var choiceNotesPanel = new StackPanel
            {
                Spacing = 8
            };

            void AddChoiceNoteRow(string note = "")
            {
                var row = new Grid
                {
                    ColumnSpacing = 8
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var noteBox = new TextBox
                {
                    Header = $"选项备注 {choiceNotesPanel.Children.Count + 1}",
                    Text = note,
                    PlaceholderText = "仅用于查看，不写入剧情表"
                };
                var removeButton = new Button
                {
                    Content = "删除",
                    Margin = new Thickness(0, 28, 0, 0)
                };
                removeButton.Click += (_, _) =>
                {
                    choiceNotesPanel.Children.Remove(row);
                    RenumberFunctionChoiceNoteRows(choiceNotesPanel);
                };

                Grid.SetColumn(removeButton, 1);
                row.Children.Add(noteBox);
                row.Children.Add(removeButton);
                choiceNotesPanel.Children.Add(row);
            }

            foreach (var note in function?.ChoiceNotes ?? [])
            {
                AddChoiceNoteRow(note);
            }

            var addChoiceNoteButton = new Button
            {
                Width = 36,
                Height = 32,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = "+"
            };
            addChoiceNoteButton.Click += (_, _) => AddChoiceNoteRow();

            var panel = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    nameBox,
                    indicatorBox,
                    categoryBox,
                    new TextBlock
                    {
                        Text = "选项备注",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    choiceNotesPanel,
                    addChoiceNoteButton,
                    new TextBlock
                    {
                        Text = "备注只保存在函数卡里，方便查看选项内容，不会写入剧情表 Custom 字段。",
                        TextWrapping = TextWrapping.Wrap,
                        Style = TryGetTextBlockStyle("SubtleTextStyle")
                    }
                }
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var name = nameBox.Text.Trim();
            var indicator = indicatorBox.Text.Trim();
            var category = string.IsNullOrWhiteSpace(categoryBox.Text) ? "自定义" : categoryBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(indicator))
            {
                return null;
            }

            var choiceNotes = choiceNotesPanel.Children
                .OfType<Grid>()
                .Select(row => row.Children.OfType<TextBox>().FirstOrDefault()?.Text)
                .Select(NormalizeFunctionChoiceNote)
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .ToList();

            return new FunctionEditorInput(name, indicator, category, choiceNotes);
        }

        private static void RenumberFunctionChoiceNoteRows(StackPanel choiceNotesPanel)
        {
            var index = 1;
            foreach (var noteBox in choiceNotesPanel.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<TextBox>()))
            {
                noteBox.Header = $"选项备注 {index}";
                index++;
            }
        }

        private string BuildSuggestedChoiceFunctionIndicator()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                return string.Empty;
            }

            var prefix = BuildCurrentStoryChapterSectionChoicePrefix();
            var existingCount = _currentStoryAssetLibrary is null
                ? 0
                : ReadFunctions(_currentStoryAssetLibrary)
                    .Count(function => string.Equals(function.Category, ChoiceFunctionCategory, StringComparison.OrdinalIgnoreCase) &&
                        function.Indicator.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return $"{prefix}{existingCount + 1}";
        }

        private static string RemoveProjectCodePrefix(string chapterCode, string? projectCode)
        {
            if (string.IsNullOrWhiteSpace(projectCode))
            {
                return chapterCode.Trim();
            }

            var prefix = $"{projectCode}-";
            return chapterCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? chapterCode[prefix.Length..]
                : chapterCode.Trim();
        }

        private static string NormalizeFunctionChoiceNote(string? note)
        {
            return Regex.Replace(note ?? string.Empty, @"\s+", " ").Trim();
        }

        private void LoadCharacterFilters(AssetLibraryInfo assetLibrary)
        {
            var storedFilters = ReadStoredCharacterFilters(assetLibrary);
            var filters = NormalizeCharacterFilters(storedFilters);
            CharacterFilterGridView.Items.Clear();
            foreach (var indexedFilter in filters.Select((filter, index) => new { filter, index }))
            {
                CharacterFilterGridView.Items.Add(CreateCharacterFilterCard(indexedFilter.filter, indexedFilter.index));
            }

            CharacterFilterGridView.Items.Add(CreateAddCharacterFilterCard());
            CharacterFilterExpander.Header = $"角色滤镜 [数量：{filters.Count}]";
            AppendLog(LogKind.Info, $"已加载角色滤镜：{filters.Count} 个。");
            if (!_isRepairingCharacterFilters &&
                !_isReorderingCharacterFilters &&
                (filters.Count != storedFilters.Count || !filters.SequenceEqual(storedFilters)))
            {
                _ = RepairStoredCharacterFiltersAsync(assetLibrary, storedFilters, filters);
            }
        }

        private List<CharacterFilterEntry> ReadCharacterFilters(AssetLibraryInfo assetLibrary)
        {
            return NormalizeCharacterFilters(ReadStoredCharacterFilters(assetLibrary));
        }

        private List<CharacterFilterEntry> ReadStoredCharacterFilters(AssetLibraryInfo assetLibrary)
        {
            var folderPath = GetCharacterFilterFolderPath(assetLibrary);
            Directory.CreateDirectory(folderPath);
            var indexPath = GetCharacterFilterIndexPath(assetLibrary);
            var index = ReadJson<CharacterFilterIndex>(indexPath);
            if (index?.Entries is not { Count: > 0 })
            {
                var defaults = CreateDefaultCharacterFilters();
                WriteCharacterFilters(assetLibrary, defaults);
                return defaults;
            }

            return index.Entries.ToList();
        }

        private void WriteCharacterFilters(AssetLibraryInfo assetLibrary, IReadOnlyList<CharacterFilterEntry> filters)
        {
            var folderPath = GetCharacterFilterFolderPath(assetLibrary);
            Directory.CreateDirectory(folderPath);
            var index = new CharacterFilterIndex
            {
                Entries = filters.ToList()
            };
            File.WriteAllText(GetCharacterFilterIndexPath(assetLibrary), JsonSerializer.Serialize(index, _jsonOptions));
        }

        private async Task RepairStoredCharacterFiltersAsync(
            AssetLibraryInfo assetLibrary,
            IReadOnlyList<CharacterFilterEntry> oldFilters,
            IReadOnlyList<CharacterFilterEntry> newFilters)
        {
            if (_isRepairingCharacterFilters)
            {
                return;
            }

            _isRepairingCharacterFilters = true;
            try
            {
                var indexRemap = BuildCharacterFilterIndexRemap(oldFilters, newFilters);
                var oldLabels = oldFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
                var newLabels = newFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
                WriteCharacterFilters(assetLibrary, newFilters);

                AssetIndexSyncResult? syncResult = null;
                if (indexRemap.Count > 0)
                {
                    syncResult = await SyncStoryCharacterFilterIndexesWithProgressAsync(assetLibrary, indexRemap, oldLabels, newLabels, newFilters.Count);
                }

                TouchAssetLibraryLastEditedAt(assetLibrary);
                if (ReferenceEquals(_currentAssetLibrary, assetLibrary) ||
                    string.Equals(_currentAssetLibrary?.Path, assetLibrary.Path, StringComparison.OrdinalIgnoreCase))
                {
                    LoadCharacterFilters(assetLibrary);
                }

                RequestDelayedRefresh();
                if (syncResult?.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的角色滤镜索引。");
                }

                AppendLog(LogKind.User, "已修复角色滤镜索引：保留唯一 VFX00 空项。");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "修复角色滤镜索引失败。", ex);
            }
            finally
            {
                _isRepairingCharacterFilters = false;
            }
        }

        private static List<CharacterFilterEntry> CreateDefaultCharacterFilters()
        {
            return
            [
                CreateEmptyCharacterFilter(),
                new CharacterFilterEntry("default-cool-rain", "冷色调（下雨）"),
                new CharacterFilterEntry("default-warm-dusk", "暖色调（黄昏）"),
                new CharacterFilterEntry("default-half-black-mask", "上半身黑遮罩")
            ];
        }

        private static CharacterFilterEntry CreateEmptyCharacterFilter()
        {
            return new CharacterFilterEntry("default-none", "空");
        }

        private static List<CharacterFilterEntry> NormalizeCharacterFilters(IEnumerable<CharacterFilterEntry> filters)
        {
            var normalized = new List<CharacterFilterEntry> { CreateEmptyCharacterFilter() };
            foreach (var filter in filters)
            {
                var remark = (filter.Remark ?? string.Empty).Trim();
                var current = filter with
                {
                    Id = string.IsNullOrWhiteSpace(filter.Id) ? Guid.NewGuid().ToString("N") : filter.Id.Trim(),
                    Remark = remark
                };
                if (IsEmptyCharacterFilter(current) || string.IsNullOrWhiteSpace(current.Remark))
                {
                    continue;
                }

                normalized.Add(current);
            }

            return normalized;
        }

        private static bool IsEmptyCharacterFilter(CharacterFilterEntry filter)
        {
            return string.Equals(filter.Id, "default-none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filter.Remark, "空", StringComparison.OrdinalIgnoreCase);
        }

        private List<CharacterFilterEntry> GetStoryCharacterFilters()
        {
            return _currentStoryAssetLibrary is null ? [] : ReadCharacterFilters(_currentStoryAssetLibrary);
        }

        private string? ResolveStoryCharacterFilterName(int index)
        {
            if (index <= 0)
            {
                return null;
            }

            var filters = GetStoryCharacterFilters();
            var resolvedIndex = ResolveStoryAssetIndex(index, filters.Count);
            return resolvedIndex is null ? null : GetCharacterFilterDisplayName(filters[resolvedIndex.Value], resolvedIndex.Value);
        }

        private static string GetCharacterFilterDisplayName(CharacterFilterEntry filter, int index)
        {
            return $"VFX{index:00}_{filter.Remark}";
        }

        private async Task AddCharacterFilterAsync()
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var remark = await ShowCharacterFilterRemarkDialogAsync("新建滤镜", string.Empty);
            if (remark is null)
            {
                return;
            }

            var filters = ReadCharacterFilters(_currentAssetLibrary);
            filters.Add(new CharacterFilterEntry(Guid.NewGuid().ToString("N"), remark));
            WriteCharacterFilters(_currentAssetLibrary, NormalizeCharacterFilters(filters));
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已新增角色滤镜：{remark}");
        }

        private async Task RenameCharacterFilterAsync(CharacterFilterEntry filter)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            if (IsEmptyCharacterFilter(filter))
            {
                return;
            }

            var remark = await ShowCharacterFilterRemarkDialogAsync("修改滤镜备注", filter.Remark);
            if (remark is null)
            {
                return;
            }

            var oldRemark = filter.Remark;
            var filters = ReadCharacterFilters(_currentAssetLibrary)
                .Select(entry => entry.Id == filter.Id ? entry with { Remark = remark } : entry)
                .ToList();
            WriteCharacterFilters(_currentAssetLibrary, NormalizeCharacterFilters(filters));
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已修改角色滤镜：{oldRemark} -> {remark}");
        }

        private async Task DeleteCharacterFilterAsync(CharacterFilterEntry filter)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            if (IsEmptyCharacterFilter(filter))
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "删除滤镜",
                Content = $"确定删除 {filter.Remark} 吗？引用它的剧情行会重置为 VFX00，后续滤镜索引会同步前移。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonStyle = CreateDestructivePrimaryButtonStyle(),
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var oldFilters = ReadCharacterFilters(_currentAssetLibrary);
            var filters = NormalizeCharacterFilters(oldFilters
                .Where(entry => entry.Id != filter.Id)
                .ToList());
            var indexRemap = BuildCharacterFilterIndexRemap(oldFilters, filters);
            var oldLabels = oldFilters.Select((entry, index) => (entry, index)).ToDictionary(item => item.index, item => item.entry.Remark);
            var newLabels = filters.Select((entry, index) => (entry, index)).ToDictionary(item => item.index, item => item.entry.Remark);
            WriteCharacterFilters(_currentAssetLibrary, filters);
            var syncResult = await SyncStoryCharacterFilterIndexesWithProgressAsync(_currentAssetLibrary, indexRemap, oldLabels, newLabels, filters.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的角色滤镜索引。");
            }

            AppendLog(LogKind.User, $"已删除角色滤镜：{filter.Remark}");
        }

        private async Task<string?> ShowCharacterFilterRemarkDialogAsync(string title, string currentRemark)
        {
            var remarkBox = new TextBox
            {
                Width = 360,
                Header = "备注",
                Text = currentRemark,
                PlaceholderText = "例如：冷色调（下雨）"
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = remarkBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var remark = SanitizeRemark(remarkBox.Text);
            return string.IsNullOrWhiteSpace(remark) ? null : remark;
        }

        private GridViewItem CreateAddCharacterCard()
        {
            var item = new GridViewItem
            {
                Width = 150,
                Height = 220,
                Margin = new Thickness(0, 0, 14, 14)
            };
            item.Tapped += async (_, _) => await CreateCharacterAsync();

            item.Content = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Child = new TextBlock
                {
                    Text = "+",
                    FontSize = 64,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            return item;
        }

        private GridViewItem CreateCharacterCard(CharacterInfo character)
        {
            var item = new GridViewItem
            {
                Width = 150,
                Height = 220,
                Margin = new Thickness(0, 0, 14, 14),
                Tag = character
            };

            var flyout = new MenuFlyout();
            var renameItem = new MenuFlyoutItem
            {
                Text = "重命名"
            };
            renameItem.Click += async (_, _) => await RenameCharacterAsync(character);
            flyout.Items.Add(renameItem);
            item.ContextFlyout = flyout;
            item.Tapped += (_, _) => ShowCharacterDetailPage(character);

            var color = ParseColor(character.ColorHex, Microsoft.UI.Colors.LightGray);
            item.Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Width = 138,
                        Height = 178,
                        CornerRadius = new CornerRadius(8),
                        BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                        BorderThickness = new Thickness(1),
                        Background = new SolidColorBrush(color),
                        Child = new TextBlock
                        {
                            Text = character.Code,
                            FontSize = 22,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    new TextBlock
                    {
                        Text = character.Name,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };
            return item;
        }

        private GridViewItem CreateMusicCard(string musicPath)
        {
            return CreateAudioCard(AudioAssetKind.Music, musicPath);
        }

        private GridViewItem CreateFunctionCard(FunctionEntry function)
        {
            var choiceNotes = function.ChoiceNotes ?? [];
            var detailText = choiceNotes.Count > 0
                ? $"{function.Name} / {function.Category} / 选项备注 {choiceNotes.Count}"
                : $"{function.Name} / {function.Category}";
            var item = new GridViewItem
            {
                Width = 240,
                Height = 104,
                Margin = new Thickness(0, 0, 14, 14),
                Tag = function
            };

            var flyout = new MenuFlyout();
            var editItem = new MenuFlyoutItem { Text = "修改函数" };
            editItem.Click += async (_, _) => await EditFunctionAsync(function);
            flyout.Items.Add(editItem);

            var deleteItem = new MenuFlyoutItem { Text = "删除" };
            deleteItem.Click += async (_, _) => await DeleteFunctionAsync(function);
            flyout.Items.Add(deleteItem);
            item.ContextFlyout = flyout;
            item.Tapped += async (_, _) => await EditFunctionAsync(function);

            item.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Child = new Grid
                {
                    Padding = new Thickness(14, 10, 14, 10),
                    RowDefinitions =
                    {
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                        new RowDefinition { Height = GridLength.Auto }
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = function.Indicator,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = detailText,
                            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Bottom
                        }
                    }
                }
            };

            if (item.Content is Border { Child: Grid grid } &&
                grid.Children.Count > 1 &&
                grid.Children[1] is FrameworkElement detailElement)
            {
                Grid.SetRow(detailElement, 1);
            }

            if (choiceNotes.Count > 0)
            {
                ToolTipService.SetToolTip(item, string.Join(Environment.NewLine, choiceNotes.Select((note, index) => $"{index + 1}. {note}")));
            }

            return item;
        }

        private GridViewItem CreateAddFunctionCard()
        {
            var item = new GridViewItem
            {
                Width = 240,
                Height = 104,
                Margin = new Thickness(0, 0, 14, 14)
            };
            item.Tapped += async (_, _) => await AddFunctionAsync();

            item.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Child = new TextBlock
                {
                    Text = "+",
                    FontSize = 42,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            return item;
        }

        private GridViewItem CreateCharacterFilterCard(CharacterFilterEntry filter, int index)
        {
            var canEdit = !IsEmptyCharacterFilter(filter);
            var item = new GridViewItem
            {
                Width = 220,
                Height = 92,
                Margin = new Thickness(0, 0, 14, 14),
                Tag = filter,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };

            var flyout = new MenuFlyout();
            if (canEdit)
            {
                var deleteItem = new MenuFlyoutItem { Text = "删除" };
                deleteItem.Click += async (_, _) => await DeleteCharacterFilterAsync(filter);
                flyout.Items.Add(deleteItem);
                item.ContextFlyout = flyout;
            }

            var title = CreateCharacterFilterCardTitle(filter);
            Grid.SetColumn(title, 1);

            var grid = new Grid
            {
                Padding = new Thickness(14, 10, 10, 10),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = $"VFX{index:00}",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    title
                }
            };

            item.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = grid
            };

            return item;
        }

        private GridViewItem CreateAddCharacterFilterCard()
        {
            var item = new GridViewItem
            {
                Width = 220,
                Height = 64,
                Margin = new Thickness(0, 0, 14, 14),
                Tag = null,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            item.Tapped += async (_, _) => await AddCharacterFilterAsync();

            item.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new SymbolIcon
                        {
                            Symbol = Symbol.Add,
                            Width = 18,
                            Height = 18,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "新增滤镜",
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };
            return item;
        }

        private static TextBlock CreateCharacterFilterCardTitle(CharacterFilterEntry filter)
        {
            return new TextBlock
            {
                Text = filter.Remark,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private GridViewItem CreateAudioCard(AudioAssetKind kind, string musicPath)
        {
            var item = new GridViewItem
            {
                Width = 220,
                Height = 92,
                Margin = new Thickness(0, 0, 14, 14),
                Tag = musicPath
            };

            var flyout = new MenuFlyout();
            var remarkItem = new MenuFlyoutItem
            {
                Text = "设置备注"
            };
            remarkItem.Click += async (_, _) => await SetAudioRemarkAsync(kind, musicPath);
            flyout.Items.Add(remarkItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除"
            };
            deleteItem.Click += async (_, _) => await DeleteAudioAsync(kind, musicPath);
            flyout.Items.Add(deleteItem);
            item.ContextFlyout = flyout;
            item.Tapped += (_, _) => ShowMusicPlayerPage(musicPath, kind);

            var title = CreateMusicCardTitle(musicPath);
            Grid.SetColumn(title, 1);
            item.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Child = new Grid
                {
                    Padding = new Thickness(14, 10, 14, 10),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    Children =
                    {
                        new SymbolIcon { Symbol = Symbol.Audio, VerticalAlignment = VerticalAlignment.Center },
                        title
                    }
                }
            };
            return item;
        }

        private static TextBlock CreateMusicCardTitle(string musicPath)
        {
            return new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(musicPath),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private async void BackgroundImagesGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentAssetLibrary is null || _isNormalizingBackgroundImages)
            {
                _draggingBackgroundImageItem = null;
                return;
            }

            var orderedPaths = BackgroundImagesGridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();

            if (orderedPaths.Count == 0)
            {
                _draggingBackgroundImageItem = null;
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, GetBackgroundImageIndex);
            var assetLabels = BuildAssetIndexLabelMaps(orderedPaths, GetBackgroundImageIndex);
            _isNormalizingBackgroundImages = true;
            try
            {
                await NormalizeBackgroundImagesAsync(GetBackgroundFolderPath(_currentAssetLibrary), orderedPaths);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            var syncResult = await SyncStoryGlobalAssetIndexesWithProgressAsync(
                _currentAssetLibrary,
                "背景图",
                "BGindex",
                indexRemap,
                assetLabels.OldLabels,
                assetLabels.NewLabels,
                orderedPaths.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的背景图索引。");
            }
            AppendLog(LogKind.User, "已调整背景图顺序并触发自动命名。");
            _draggingBackgroundImageItem = null;
        }

        private void BackgroundImagesGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            _draggingBackgroundImageItem =
                draggedObject as GridViewItem ??
                (draggedObject is null ? null : BackgroundImagesGridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                BackgroundImagesGridView.Items
                    .OfType<GridViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));

            AppendLog(
                LogKind.Info,
                _draggingBackgroundImageItem is null
                    ? $"背景图拖拽开始，但未识别拖拽项。Items[0]={draggedObject?.GetType().Name ?? "null"}"
                    : $"背景图拖拽开始：{Path.GetFileName(_draggingBackgroundImageItem.Tag as string)}");
        }

        private void BackgroundImagesGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingBackgroundImageItem is null || BackgroundImagesGridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;

            var pointerPosition = e.GetPosition(BackgroundImagesGridView);
            if (!IsPointerInTrailingBlankArea(pointerPosition, _draggingBackgroundImageItem))
            {
                return;
            }

            MoveDraggingBackgroundImageToEnd();
        }

        private void BackgroundImagesDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingBackgroundImageItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void BackgroundImagesDropZone_DragEnter(object sender, DragEventArgs e)
        {
            var containsStorageItems = e.DataView.Contains(StandardDataFormats.StorageItems);
            AppendLog(
                LogKind.Info,
                _draggingBackgroundImageItem is not null
                    ? "拖入背景图区：检测到内部背景图拖拽。"
                    : $"拖入背景图区：外部数据进入，StorageItems={containsStorageItems}。");

            if (_draggingBackgroundImageItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (containsStorageItems)
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async void MusicGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentAssetLibrary is null || _isNormalizingMusicFiles)
            {
                _draggingMusicItem = null;
                return;
            }

            var orderedPaths = MusicGridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();

            if (orderedPaths.Count == 0)
            {
                _draggingMusicItem = null;
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => GetAudioAssetIndex(AudioAssetKind.Music, path));
            var assetLabels = BuildAssetIndexLabelMaps(orderedPaths, path => GetAudioAssetIndex(AudioAssetKind.Music, path));
            _isNormalizingMusicFiles = true;
            try
            {
                await NormalizeMusicFilesAsync(GetMusicFolderPath(_currentAssetLibrary), orderedPaths);
            }
            finally
            {
                _isNormalizingMusicFiles = false;
            }

            var syncResult = await SyncStoryGlobalAssetIndexesWithProgressAsync(
                _currentAssetLibrary,
                "BGM",
                "BGM",
                indexRemap,
                assetLabels.OldLabels,
                assetLabels.NewLabels,
                orderedPaths.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshMusicCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的 BGM 索引。");
            }
            AppendLog(LogKind.User, "已调整音乐顺序并触发自动命名。");
            _draggingMusicItem = null;
        }

        private void MusicGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            _draggingMusicItem =
                draggedObject as GridViewItem ??
                (draggedObject is null ? null : MusicGridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                MusicGridView.Items
                    .OfType<GridViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));
        }

        private void MusicGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingMusicItem is null || MusicGridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private void MusicDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (_draggingMusicItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void MusicDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingMusicItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async void MusicDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_draggingMusicItem is not null)
            {
                MoveDraggingMusicToEnd();
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedMusicPaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => MusicExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await ImportMusicFilesAsync(droppedMusicPaths);
                if (importedCount > 0)
                {
                    AppendLog(LogKind.User, $"拖入导入音乐：{importedCount} 个文件。");
                }
                else
                {
                    AppendLog(LogKind.Warning, "拖入内容中没有可导入的 wav 音乐文件。");
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void AmbientSoundGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await AudioGridView_DragItemsCompleted(AudioAssetKind.Ambient);
        }

        private void AmbientSoundGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            AudioGridView_DragItemsStarting(AudioAssetKind.Ambient, sender, e);
        }

        private void AmbientSoundGridView_DragOver(object sender, DragEventArgs e)
        {
            AudioGridView_DragOver(AudioAssetKind.Ambient, e);
        }

        private void AmbientSoundDropZone_DragEnter(object sender, DragEventArgs e)
        {
            AudioDropZone_DragEnter(AudioAssetKind.Ambient, e);
        }

        private void AmbientSoundDropZone_DragOver(object sender, DragEventArgs e)
        {
            AudioDropZone_DragOver(AudioAssetKind.Ambient, e);
        }

        private async void AmbientSoundDropZone_Drop(object sender, DragEventArgs e)
        {
            await AudioDropZone_Drop(AudioAssetKind.Ambient, e);
        }

        private async void SoundEffectGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await AudioGridView_DragItemsCompleted(AudioAssetKind.SoundEffect);
        }

        private void SoundEffectGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            AudioGridView_DragItemsStarting(AudioAssetKind.SoundEffect, sender, e);
        }

        private void SoundEffectGridView_DragOver(object sender, DragEventArgs e)
        {
            AudioGridView_DragOver(AudioAssetKind.SoundEffect, e);
        }

        private void SoundEffectDropZone_DragEnter(object sender, DragEventArgs e)
        {
            AudioDropZone_DragEnter(AudioAssetKind.SoundEffect, e);
        }

        private void SoundEffectDropZone_DragOver(object sender, DragEventArgs e)
        {
            AudioDropZone_DragOver(AudioAssetKind.SoundEffect, e);
        }

        private async void SoundEffectDropZone_Drop(object sender, DragEventArgs e)
        {
            await AudioDropZone_Drop(AudioAssetKind.SoundEffect, e);
        }

        private async void CharacterFilterGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentAssetLibrary is null || _isReorderingCharacterFilters)
            {
                _draggingCharacterFilterItem = null;
                return;
            }

            var filters = CharacterFilterGridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as CharacterFilterEntry)
                .Where(entry => entry is not null)
                .Cast<CharacterFilterEntry>()
                .ToList();
            filters = NormalizeCharacterFilters(filters);
            if (filters.Count == 0)
            {
                _draggingCharacterFilterItem = null;
                return;
            }

            var oldFilters = ReadCharacterFilters(_currentAssetLibrary);
            var indexRemap = BuildCharacterFilterIndexRemap(oldFilters, filters);
            var oldLabels = oldFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
            var newLabels = filters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
            _isReorderingCharacterFilters = true;
            try
            {
                WriteCharacterFilters(_currentAssetLibrary, filters);
            }
            finally
            {
                _isReorderingCharacterFilters = false;
            }

            var syncResult = await SyncStoryCharacterFilterIndexesWithProgressAsync(_currentAssetLibrary, indexRemap, oldLabels, newLabels, filters.Count);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacterFilters(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (syncResult.ChangedCsvCount > 0)
            {
                AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的角色滤镜索引。");
            }
            AppendLog(LogKind.User, "已调整角色滤镜顺序。");
            _draggingCharacterFilterItem = null;
        }

        private void CharacterFilterGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            _draggingCharacterFilterItem =
                draggedObject as GridViewItem ??
                (draggedObject is null ? null : CharacterFilterGridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                CharacterFilterGridView.Items
                    .OfType<GridViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));

            if (_draggingCharacterFilterItem?.Tag is not CharacterFilterEntry)
            {
                _draggingCharacterFilterItem = null;
            }
        }

        private void CharacterFilterGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterFilterItem is null || CharacterFilterGridView.Items.Count <= 2)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private async void CharacterClothGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentCharacter is null || _isNormalizingCharacterClothes)
            {
                _draggingCharacterClothItem = null;
                return;
            }

            var orderedPaths = CharacterClothGridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();

            if (orderedPaths.Count == 0)
            {
                _draggingCharacterClothItem = null;
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => GetCharacterLayerIndex(path, CharacterLayerKind.Cloth));
            var assetLabels = BuildAssetIndexLabelMaps(orderedPaths, path => GetCharacterLayerIndex(path, CharacterLayerKind.Cloth));
            _isNormalizingCharacterClothes = true;
            try
            {
                var entries = orderedPaths
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Cloth, string.Empty))
                    .ToList();
                RenameCharacterLayerEntries(entries, CharacterLayerKind.Cloth, _currentCharacter.Code);
            }
            finally
            {
                _isNormalizingCharacterClothes = false;
            }

            if (_currentAssetLibrary is not null)
            {
                var syncResult = await SyncStoryCharacterLayerIndexesWithProgressAsync(
                    _currentAssetLibrary,
                    _currentCharacter,
                    CharacterLayerKind.Cloth,
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    orderedPaths.Count);
                if (syncResult.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的服装索引。");
                }

                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, "已调整服装顺序并触发自动命名。");
            _draggingCharacterClothItem = null;
            await Task.CompletedTask;
        }

        private void CharacterClothGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            _draggingCharacterClothItem =
                draggedObject as GridViewItem ??
                (draggedObject is null ? null : CharacterClothGridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                CharacterClothGridView.Items
                    .OfType<GridViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));
        }

        private void CharacterClothGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterClothItem is null || CharacterClothGridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            var pointerPosition = e.GetPosition(CharacterClothGridView);
            if (IsPointerInTrailingBlankArea(CharacterClothGridView, pointerPosition, _draggingCharacterClothItem))
            {
                MoveDraggingCharacterClothToEnd();
            }
        }

        private void CharacterClothDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (_draggingCharacterClothItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void CharacterClothDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterClothItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async void CharacterClothDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_draggingCharacterClothItem is not null)
            {
                MoveDraggingCharacterClothToEnd();
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedClothPaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await ImportCharacterClothesAsync(droppedClothPaths);
                if (importedCount > 0)
                {
                    AppendLog(LogKind.User, $"拖入导入服装：{importedCount} 个文件。");
                }
                else
                {
                    AppendLog(LogKind.Warning, "拖入内容中没有可导入的服装图片文件。");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "拖入导入服装失败。", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void CharacterFaceGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentCharacter is null || _isNormalizingCharacterFaces)
            {
                _draggingCharacterFaceItem = null;
                return;
            }

            var orderedPaths = CharacterFaceGridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();

            if (orderedPaths.Count == 0)
            {
                _draggingCharacterFaceItem = null;
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => GetCharacterLayerIndex(path, CharacterLayerKind.Face));
            var assetLabels = BuildAssetIndexLabelMaps(orderedPaths, path => GetCharacterLayerIndex(path, CharacterLayerKind.Face));
            _isNormalizingCharacterFaces = true;
            try
            {
                var entries = orderedPaths
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Face, string.Empty))
                    .ToList();
                RenameCharacterFaceEntriesAndUpdateMeta(entries);
            }
            finally
            {
                _isNormalizingCharacterFaces = false;
            }

            if (_currentAssetLibrary is not null)
            {
                var syncResult = await SyncStoryCharacterLayerIndexesWithProgressAsync(
                    _currentAssetLibrary,
                    _currentCharacter,
                    CharacterLayerKind.Face,
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    orderedPaths.Count);
                if (syncResult.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的表情索引。");
                }

                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, "已调整表情顺序并触发自动命名。");
            _draggingCharacterFaceItem = null;
            await Task.CompletedTask;
        }

        private void CharacterFaceGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            _draggingCharacterFaceItem =
                draggedObject as GridViewItem ??
                (draggedObject is null ? null : CharacterFaceGridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                CharacterFaceGridView.Items
                    .OfType<GridViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));
        }

        private void CharacterFaceGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterFaceItem is null || CharacterFaceGridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            var pointerPosition = e.GetPosition(CharacterFaceGridView);
            if (IsPointerInTrailingBlankArea(CharacterFaceGridView, pointerPosition, _draggingCharacterFaceItem))
            {
                MoveDraggingCharacterFaceToEnd();
            }
        }

        private void CharacterFaceDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (_draggingCharacterFaceItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void CharacterFaceDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterFaceItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async void CharacterFaceDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_draggingCharacterFaceItem is not null)
            {
                MoveDraggingCharacterFaceToEnd();
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedFacePaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await ImportCharacterFacesAsync(droppedFacePaths);
                if (importedCount > 0)
                {
                    AppendLog(LogKind.User, $"拖入导入表情：{importedCount} 个文件。");
                }
                else
                {
                    AppendLog(LogKind.Warning, "拖入内容中没有可导入的表情图片文件。");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "拖入导入表情失败。", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void CharacterAdornGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentCharacter is null || _isNormalizingCharacterAdorns)
            {
                _draggingCharacterAdornItem = null;
                return;
            }

            var orderedPaths = CharacterAdornGridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();

            if (orderedPaths.Count == 0)
            {
                _draggingCharacterAdornItem = null;
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => GetCharacterLayerIndex(path, CharacterLayerKind.Adorn));
            var assetLabels = BuildAssetIndexLabelMaps(orderedPaths, path => GetCharacterLayerIndex(path, CharacterLayerKind.Adorn));
            _isNormalizingCharacterAdorns = true;
            try
            {
                var entries = orderedPaths
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Adorn, string.Empty))
                    .ToList();
                RenameCharacterAdornEntriesAndUpdateMeta(entries);
            }
            finally
            {
                _isNormalizingCharacterAdorns = false;
            }

            if (_currentAssetLibrary is not null)
            {
                var syncResult = await SyncStoryCharacterLayerIndexesWithProgressAsync(
                    _currentAssetLibrary,
                    _currentCharacter,
                    CharacterLayerKind.Adorn,
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    orderedPaths.Count);
                if (syncResult.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的装饰索引。");
                }

                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, "已调整装饰顺序并触发自动命名。");
            _draggingCharacterAdornItem = null;
            await Task.CompletedTask;
        }

        private void CharacterAdornGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            _draggingCharacterAdornItem =
                draggedObject as GridViewItem ??
                (draggedObject is null ? null : CharacterAdornGridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                CharacterAdornGridView.Items
                    .OfType<GridViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));
        }

        private void CharacterAdornGridView_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterAdornItem is null || CharacterAdornGridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            var pointerPosition = e.GetPosition(CharacterAdornGridView);
            if (IsPointerInTrailingBlankArea(CharacterAdornGridView, pointerPosition, _draggingCharacterAdornItem))
            {
                MoveDraggingCharacterAdornToEnd();
            }
        }

        private void CharacterAdornDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (_draggingCharacterAdornItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void CharacterAdornDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (_draggingCharacterAdornItem is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async void CharacterAdornDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_draggingCharacterAdornItem is not null)
            {
                MoveDraggingCharacterAdornToEnd();
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedAdornPaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await ImportCharacterAdornsAsync(droppedAdornPaths);
                if (importedCount > 0)
                {
                    AppendLog(LogKind.User, $"拖入导入装饰：{importedCount} 个文件。");
                }
                else
                {
                    AppendLog(LogKind.Warning, "拖入内容中没有可导入的装饰图片文件。");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "拖入导入装饰失败。", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void CharacterLayerGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var imagePath = GetImagePathFromCharacterLayerClickedItem(e.ClickedItem);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                AppendLog(LogKind.Warning, $"点击立绘图层时未识别图片路径：{e.ClickedItem?.GetType().Name ?? "null"}");
                return;
            }

            if (ReferenceEquals(sender, CharacterClothGridView))
            {
                ShowCharacterClothViewerPage(imagePath);
            }
            else if (ReferenceEquals(sender, CharacterFaceGridView))
            {
                ShowCharacterFaceViewerPage(imagePath);
            }
            else if (ReferenceEquals(sender, CharacterAdornGridView))
            {
                ShowCharacterAdornViewerPage(imagePath);
            }
        }

        private static string? GetImagePathFromCharacterLayerClickedItem(object? clickedItem)
        {
            if (clickedItem is GridViewItem gridViewItem)
            {
                if (gridViewItem.Tag is string itemPath)
                {
                    return itemPath;
                }

                if (gridViewItem.Content is FrameworkElement contentElement &&
                    contentElement.Tag is string contentPath)
                {
                    return contentPath;
                }
            }

            if (clickedItem is FrameworkElement element)
            {
                if (element.Tag is string elementPath)
                {
                    return elementPath;
                }

                if (element.DataContext is GridViewItem dataItem &&
                    dataItem.Tag is string dataItemPath)
                {
                    return dataItemPath;
                }
            }

            return null;
        }

        private void CharacterDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
                AppendLog(LogKind.Info, "拖入立绘区：角色压缩包识别接口已保留，当前版本暂不导入。");
            }
        }

        private void CharacterDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void CharacterDropZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            AppendLog(LogKind.Warning, "角色拖入导入暂未实现：后续会用于识别角色压缩包。");
        }

        private void MoveDraggingMusicToEnd()
        {
            if (_draggingMusicItem is null)
            {
                return;
            }

            var currentIndex = MusicGridView.Items.IndexOf(_draggingMusicItem);
            var lastIndex = MusicGridView.Items.Count - 1;
            if (currentIndex == lastIndex)
            {
                return;
            }

            MusicGridView.Items.Remove(_draggingMusicItem);
            MusicGridView.Items.Add(_draggingMusicItem);
        }

        private async Task AudioGridView_DragItemsCompleted(AudioAssetKind kind)
        {
            if (_currentAssetLibrary is null || IsAudioNormalizing(kind))
            {
                SetDraggingAudioItem(kind, null);
                return;
            }

            var gridView = GetAudioGridView(kind);
            var orderedPaths = gridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .ToList();

            if (orderedPaths.Count == 0)
            {
                SetDraggingAudioItem(kind, null);
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => GetAudioAssetIndex(kind, path));
            var assetLabels = BuildAssetIndexLabelMaps(orderedPaths, path => GetAudioAssetIndex(kind, path));
            SetAudioNormalizing(kind, true);
            try
            {
                await NormalizeAudioFilesAsync(kind, GetAudioFolderPath(_currentAssetLibrary, kind), orderedPaths);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            if (kind == AudioAssetKind.Ambient)
            {
                var syncResult = await SyncStoryGlobalAssetIndexesWithProgressAsync(
                    _currentAssetLibrary,
                    "环境音",
                    "Scene",
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    orderedPaths.Count);
                if (syncResult.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的环境音索引。");
                }
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已调整{GetAudioDisplayName(kind)}顺序并触发自动命名。");
            SetDraggingAudioItem(kind, null);
        }

        private void AudioGridView_DragItemsStarting(AudioAssetKind kind, object sender, DragItemsStartingEventArgs e)
        {
            var gridView = GetAudioGridView(kind);
            var draggedObject = e.Items.FirstOrDefault();
            SetDraggingAudioItem(
                kind,
                draggedObject as GridViewItem ??
                (draggedObject is null ? null : gridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                gridView.Items
                    .OfType<GridViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject)));
        }

        private void AudioGridView_DragOver(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is null || GetAudioGridView(kind).Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private void AudioDropZone_DragEnter(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private void AudioDropZone_DragOver(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is not null)
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.Handled = true;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
        }

        private async Task AudioDropZone_Drop(AudioAssetKind kind, DragEventArgs e)
        {
            if (GetDraggingAudioItem(kind) is not null)
            {
                MoveDraggingAudioToEnd(kind);
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedMusicPaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => MusicExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await ImportAudioFilesAsync(kind, droppedMusicPaths);
                AppendLog(
                    importedCount > 0 ? LogKind.User : LogKind.Warning,
                    importedCount > 0
                        ? $"拖入导入{GetAudioDisplayName(kind)}：{importedCount} 个文件。"
                        : $"拖入内容中没有可导入的 wav {GetAudioDisplayName(kind)}文件。");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void MoveDraggingAudioToEnd(AudioAssetKind kind)
        {
            var draggingItem = GetDraggingAudioItem(kind);
            if (draggingItem is null)
            {
                return;
            }

            var gridView = GetAudioGridView(kind);
            var currentIndex = gridView.Items.IndexOf(draggingItem);
            var lastIndex = gridView.Items.Count - 1;
            if (currentIndex == lastIndex)
            {
                return;
            }

            gridView.Items.Remove(draggingItem);
            gridView.Items.Add(draggingItem);
        }

        private void MoveDraggingCharacterClothToEnd()
        {
            if (_draggingCharacterClothItem is null)
            {
                return;
            }

            var currentIndex = CharacterClothGridView.Items.IndexOf(_draggingCharacterClothItem);
            var lastIndex = CharacterClothGridView.Items.Count - 1;
            if (currentIndex == lastIndex)
            {
                return;
            }

            CharacterClothGridView.Items.Remove(_draggingCharacterClothItem);
            CharacterClothGridView.Items.Add(_draggingCharacterClothItem);
        }

        private void MoveDraggingCharacterFaceToEnd()
        {
            if (_draggingCharacterFaceItem is null)
            {
                return;
            }

            var currentIndex = CharacterFaceGridView.Items.IndexOf(_draggingCharacterFaceItem);
            var lastIndex = CharacterFaceGridView.Items.Count - 1;
            if (currentIndex == lastIndex)
            {
                return;
            }

            CharacterFaceGridView.Items.Remove(_draggingCharacterFaceItem);
            CharacterFaceGridView.Items.Add(_draggingCharacterFaceItem);
        }

        private void MoveDraggingCharacterAdornToEnd()
        {
            if (_draggingCharacterAdornItem is null)
            {
                return;
            }

            var currentIndex = CharacterAdornGridView.Items.IndexOf(_draggingCharacterAdornItem);
            var lastIndex = CharacterAdornGridView.Items.Count - 1;
            if (currentIndex == lastIndex)
            {
                return;
            }

            CharacterAdornGridView.Items.Remove(_draggingCharacterAdornItem);
            CharacterAdornGridView.Items.Add(_draggingCharacterAdornItem);
        }

        private async void BackgroundImagesDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_draggingBackgroundImageItem is not null)
            {
                MoveDraggingBackgroundImageToEnd();
                e.Handled = true;
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                var storageItems = await e.DataView.GetStorageItemsAsync();
                var droppedImagePaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await ImportBackgroundImagesAsync(droppedImagePaths);
                if (importedCount > 0)
                {
                    AppendLog(LogKind.User, $"拖入导入背景图：{importedCount} 个文件。");
                }
                else
                {
                    AppendLog(LogKind.Warning, "拖入内容中没有可导入的背景图文件。");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "拖入导入背景图失败。", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void MoveDraggingBackgroundImageToEnd()
        {
            if (_draggingBackgroundImageItem is null)
            {
                return;
            }

            var currentIndex = BackgroundImagesGridView.Items.IndexOf(_draggingBackgroundImageItem);
            var lastIndex = BackgroundImagesGridView.Items.Count - 1;
            if (currentIndex == lastIndex)
            {
                return;
            }

            BackgroundImagesGridView.Items.Remove(_draggingBackgroundImageItem);
            BackgroundImagesGridView.Items.Add(_draggingBackgroundImageItem);
        }

        private bool IsPointerInTrailingBlankArea(Windows.Foundation.Point pointerPosition, GridViewItem draggingItem)
        {
            return IsPointerInTrailingBlankArea(BackgroundImagesGridView, pointerPosition, draggingItem);
        }

        private static bool IsPointerInTrailingBlankArea(
            GridView gridView,
            Windows.Foundation.Point pointerPosition,
            GridViewItem draggingItem)
        {
            var visibleItems = gridView.Items
                .OfType<GridViewItem>()
                .Where(item => !ReferenceEquals(item, draggingItem))
                .ToList();

            if (visibleItems.Count == 0)
            {
                return false;
            }

            var lastItem = visibleItems.Last();
            var lastItemOrigin = lastItem.TransformToVisual(gridView)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            var lastItemRight = lastItemOrigin.X + lastItem.ActualWidth;
            var sameRowAsLastItem =
                pointerPosition.Y >= lastItemOrigin.Y &&
                pointerPosition.Y <= lastItemOrigin.Y + lastItem.ActualHeight;

            return sameRowAsLastItem && pointerPosition.X > lastItemRight;
        }

        private async Task<string?> SetBackgroundImageRemarkAsync(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return null;
            }

            var parsed = ParseBackgroundImageFileName(imagePath);
            var remarkBox = new TextBox
            {
                Width = 360,
                Header = "备注",
                Text = parsed.Remark,
                PlaceholderText = "例如：我是备注"
            };

            var dialog = new ContentDialog
            {
                Title = "设置背景图备注",
                Content = remarkBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var orderedPaths = GetBackgroundImagePaths(GetBackgroundFolderPath(_currentAssetLibrary));
            var entries = orderedPaths
                .Select(path =>
                {
                    var entry = ParseBackgroundImageFileName(path);
                    return PathsEqual(path, imagePath)
                        ? entry with { Remark = SanitizeRemark(remarkBox.Text) }
                        : entry;
                })
                .ToList();

            _isNormalizingBackgroundImages = true;
            try
            {
                await RenameBackgroundEntriesAsync(entries);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置背景图备注：{Path.GetFileName(imagePath)}");
            return entries
                .Select((entry, index) =>
                {
                    var folderPath = Path.GetDirectoryName(entry.Path)!;
                    var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
                    var baseName = $"BG{index.ToString().PadLeft(digitCount, '0')}";
                    var fileName = string.IsNullOrWhiteSpace(entry.Remark)
                        ? $"{baseName}.png"
                        : $"{baseName}_{entry.Remark}.png";
                    return Path.Combine(folderPath, fileName);
                })
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileNameWithoutExtension(path).Split('_')[0],
                    Path.GetFileNameWithoutExtension(imagePath).Split('_')[0],
                    StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> DeleteBackgroundImageAsync(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = "删除背景图",
                Content = $"确定删除 {Path.GetFileName(imagePath)} 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonStyle = CreateDestructivePrimaryButtonStyle(),
                XamlRoot = Content.XamlRoot
            };

            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return false;
            }

            File.Delete(imagePath);

            _isNormalizingBackgroundImages = true;
            try
            {
                await NormalizeBackgroundImagesAsync(GetBackgroundFolderPath(_currentAssetLibrary));
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已删除背景图：{Path.GetFileName(imagePath)}");
            return true;
        }

        private async Task<string?> SetCharacterClothRemarkAsync(string clothPath)
        {
            if (_currentCharacter is null || !File.Exists(clothPath))
            {
                return null;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            var parsed = ParseCharacterLayerFileName(clothPath, CharacterLayerKind.Cloth, string.Empty);
            var remarkBox = new TextBox
            {
                Width = 360,
                Header = "备注",
                Text = parsed.Remark,
                PlaceholderText = "例如：校服"
            };

            var dialog = new ContentDialog
            {
                Title = "设置服装备注",
                Content = remarkBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var clothFolderPath = Path.Combine(_currentCharacter.Path, "DN_Cloth");
            var orderedPaths = GetCharacterLayerImagePaths(clothFolderPath);
            var updatedEntries = orderedPaths
                .Select(path =>
                {
                    var entry = ParseCharacterLayerFileName(path, CharacterLayerKind.Cloth, string.Empty);
                    return PathsEqual(path, clothPath)
                        ? entry with { Remark = SanitizeRemark(remarkBox.Text) }
                        : entry;
                })
                .ToList();
            var updatedIndex = orderedPaths.FindIndex(path => PathsEqual(path, clothPath));

            _isNormalizingCharacterClothes = true;
            try
            {
                RenameCharacterLayerEntries(updatedEntries, CharacterLayerKind.Cloth, _currentCharacter.Code);
            }
            finally
            {
                _isNormalizingCharacterClothes = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            var updatedPath = updatedIndex >= 0
                ? GetCharacterLayerTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Cloth, _currentCharacter.Code)
                : null;
            ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置服装备注：{Path.GetFileName(clothPath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteCharacterClothAsync(string clothPath)
        {
            if (_currentCharacter is null || !File.Exists(clothPath))
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = "删除服装",
                Content = $"确定删除 {Path.GetFileName(clothPath)} 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonStyle = CreateDestructivePrimaryButtonStyle(),
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return false;
            }

            File.Delete(clothPath);
            _isNormalizingCharacterClothes = true;
            try
            {
                NormalizeCharacterLayerFiles(
                    Path.Combine(_currentCharacter.Path, "DN_Cloth"),
                    CharacterLayerKind.Cloth,
                    string.Empty,
                    _currentCharacter.Code);
            }
            finally
            {
                _isNormalizingCharacterClothes = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已删除服装：{Path.GetFileName(clothPath)}");
            return true;
        }

        private async Task<string?> SetCharacterFaceRemarkAsync(string facePath)
        {
            if (_currentCharacter is null || !File.Exists(facePath))
            {
                return null;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            var parsed = ParseCharacterLayerFileName(facePath, CharacterLayerKind.Face, string.Empty);
            var remarkBox = new TextBox
            {
                Width = 360,
                Header = "备注",
                Text = parsed.Remark,
                PlaceholderText = "例如：微笑"
            };

            var dialog = new ContentDialog
            {
                Title = "设置表情备注",
                Content = remarkBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var faceFolderPath = Path.Combine(_currentCharacter.Path, "FC_Face");
            var orderedPaths = GetCharacterLayerImagePaths(faceFolderPath);
            var updatedEntries = orderedPaths
                .Select(path =>
                {
                    var entry = ParseCharacterLayerFileName(path, CharacterLayerKind.Face, string.Empty);
                    return PathsEqual(path, facePath)
                        ? entry with { Remark = SanitizeRemark(remarkBox.Text) }
                        : entry;
                })
                .ToList();
            var updatedIndex = orderedPaths.FindIndex(path => PathsEqual(path, facePath));

            _isNormalizingCharacterFaces = true;
            try
            {
                RenameCharacterFaceEntriesAndUpdateMeta(updatedEntries);
            }
            finally
            {
                _isNormalizingCharacterFaces = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            var updatedPath = updatedIndex >= 0
                ? GetCharacterLayerTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Face)
                : null;
            ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置表情备注：{Path.GetFileName(facePath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteCharacterFaceAsync(string facePath)
        {
            if (_currentCharacter is null || !File.Exists(facePath))
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = "删除表情",
                Content = $"确定删除 {Path.GetFileName(facePath)} 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonStyle = CreateDestructivePrimaryButtonStyle(),
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return false;
            }

            var faceFolderPath = Path.Combine(_currentCharacter.Path, "FC_Face");
            RemoveCharacterFaceScopeEntry(faceFolderPath, Path.GetFileName(facePath));
            File.Delete(facePath);

            _isNormalizingCharacterFaces = true;
            try
            {
                var entries = GetCharacterLayerImagePaths(faceFolderPath)
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Face, string.Empty))
                    .ToList();
                RenameCharacterFaceEntriesAndUpdateMeta(entries);
            }
            finally
            {
                _isNormalizingCharacterFaces = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已删除表情：{Path.GetFileName(facePath)}");
            return true;
        }

        private async Task SetCharacterFaceAvailabilityAsync(string facePath)
        {
            if (_currentCharacter is null || !File.Exists(facePath))
            {
                return;
            }

            var faceFolderPath = Path.Combine(_currentCharacter.Path, "FC_Face");
            await SetCharacterLayerAvailabilityAsync(
                facePath,
                faceFolderPath,
                ReadCharacterFaceScopeMeta,
                WriteCharacterFaceScopeMeta,
                "表情可用范围",
                "表情");
        }

        private async Task SetCharacterLayerAvailabilityAsync(
            string layerPath,
            string layerFolderPath,
            Func<string, CharacterLayerScopeMeta> readMeta,
            Action<string, CharacterLayerScopeMeta> writeMeta,
            string title,
            string logLabel)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return;
            }

            var clothFolderPath = Path.Combine(_currentCharacter.Path, "DN_Cloth");
            var clothPaths = GetCharacterLayerImagePaths(clothFolderPath);
            if (clothPaths.Count == 0)
            {
                AppendLog(LogKind.Warning, $"还没有服装，暂时无法设置{logLabel}可用范围。");
                return;
            }

            var meta = readMeta(layerFolderPath);
            var layerFileName = Path.GetFileName(layerPath);
            var existingEntry = meta.Entries.TryGetValue(layerFileName, out var savedEntry)
                ? savedEntry
                : new CharacterLayerScopeEntry { UseAllCostumes = true };
            var selectedHashes = existingEntry.CostumeHashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var checkBoxes = new List<(CheckBox CheckBox, string CostumeHash)>();
            var cards = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 18,
                Padding = new Thickness(8, 0, 8, 8)
            };

            foreach (var clothPath in clothPaths)
            {
                var costumeHash = ComputeFileHash(clothPath);
                var checkBox = new CheckBox
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsChecked = existingEntry.UseAllCostumes || selectedHashes.Contains(costumeHash)
                };
                checkBoxes.Add((checkBox, costumeHash));
                var card = new StackPanel
                {
                    Width = 132,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        CreateThumbnail(clothPath, 120, 150, showAddIcon: false),
                        new TextBlock
                        {
                            Text = Path.GetFileNameWithoutExtension(clothPath),
                            Width = 132,
                            TextAlignment = TextAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        checkBox
                    }
                };
                cards.Children.Add(card);
            }

            var dialogContent = new ScrollViewer
            {
                MaxWidth = 640,
                MaxHeight = 430,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled,
                Content = cards
            };
            dialogContent.PointerWheelChanged += (_, args) =>
            {
                var delta = args.GetCurrentPoint(dialogContent).Properties.MouseWheelDelta;
                if (delta == 0)
                {
                    return;
                }

                var direction = delta > 0 ? -1 : 1;
                var targetOffset = Math.Clamp(
                    dialogContent.HorizontalOffset + 72 * direction,
                    0,
                    dialogContent.ScrollableWidth);
                dialogContent.ChangeView(targetOffset, null, null, true);
                args.Handled = true;
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = dialogContent,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var checkedHashes = checkBoxes
                .Where(pair => pair.CheckBox.IsChecked == true)
                .Select(pair => pair.CostumeHash)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            meta.Entries[layerFileName] = new CharacterLayerScopeEntry
            {
                UseAllCostumes = checkedHashes.Count == clothPaths.Count,
                CostumeHashes = checkedHashes.Count == clothPaths.Count ? [] : checkedHashes
            };
            writeMeta(layerFolderPath, meta);
            await UpdateCharacterLayerPreviewAsync();
            AppendLog(LogKind.User, $"已设置{logLabel}可用范围：{layerFileName}");
        }

        private async Task<string?> SetCharacterAdornRemarkAsync(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return null;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            var parsed = ParseCharacterLayerFileName(adornPath, CharacterLayerKind.Adorn, string.Empty);
            var remarkBox = new TextBox
            {
                Width = 360,
                Header = "备注",
                Text = parsed.Remark,
                PlaceholderText = "例如：帽子"
            };

            var dialog = new ContentDialog
            {
                Title = "设置装饰备注",
                Content = remarkBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var adornFolderPath = Path.Combine(_currentCharacter.Path, "AD_Adorn");
            var orderedPaths = GetCharacterLayerImagePaths(adornFolderPath);
            var updatedEntries = orderedPaths
                .Select(path =>
                {
                    var entry = ParseCharacterLayerFileName(path, CharacterLayerKind.Adorn, string.Empty);
                    return PathsEqual(path, adornPath)
                        ? entry with { Remark = SanitizeRemark(remarkBox.Text) }
                        : entry;
                })
                .ToList();
            var updatedIndex = orderedPaths.FindIndex(path => PathsEqual(path, adornPath));

            _isNormalizingCharacterAdorns = true;
            try
            {
                RenameCharacterAdornEntriesAndUpdateMeta(updatedEntries);
            }
            finally
            {
                _isNormalizingCharacterAdorns = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            var updatedPath = updatedIndex >= 0
                ? GetCharacterLayerTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Adorn)
                : null;
            ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置装饰备注：{Path.GetFileName(adornPath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteCharacterAdornAsync(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = "删除装饰",
                Content = $"确定删除 {Path.GetFileName(adornPath)} 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonStyle = CreateDestructivePrimaryButtonStyle(),
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return false;
            }

            var adornFolderPath = Path.Combine(_currentCharacter.Path, "AD_Adorn");
            RemoveCharacterAdornScopeEntry(adornFolderPath, Path.GetFileName(adornPath));
            File.Delete(adornPath);

            _isNormalizingCharacterAdorns = true;
            try
            {
                var entries = GetCharacterLayerImagePaths(adornFolderPath)
                    .Select(path => ParseCharacterLayerFileName(path, CharacterLayerKind.Adorn, string.Empty))
                    .ToList();
                RenameCharacterAdornEntriesAndUpdateMeta(entries);
            }
            finally
            {
                _isNormalizingCharacterAdorns = false;
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已删除装饰：{Path.GetFileName(adornPath)}");
            return true;
        }

        private async Task SetCharacterAdornAvailabilityAsync(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return;
            }

            var adornFolderPath = Path.Combine(_currentCharacter.Path, "AD_Adorn");
            await SetCharacterLayerAvailabilityAsync(
                adornPath,
                adornFolderPath,
                ReadCharacterAdornScopeMeta,
                WriteCharacterAdornScopeMeta,
                "装饰可用范围",
                "装饰");
        }

        private async Task<string?> SetMusicRemarkAsync(string musicPath)
        {
            return await SetAudioRemarkAsync(AudioAssetKind.Music, musicPath);
        }

        private async Task<string?> SetAudioRemarkAsync(AudioAssetKind kind, string musicPath)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return null;
            }

            var parsed = ParseAudioFileName(kind, musicPath);
            var remarkBox = new TextBox
            {
                Width = 360,
                Header = "备注",
                Text = parsed.Remark,
                PlaceholderText = kind == AudioAssetKind.Music ? "例如：主题曲" : "例如：雨声"
            };

            var dialog = new ContentDialog
            {
                Title = $"设置{GetAudioDisplayName(kind)}备注",
                Content = remarkBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var orderedPaths = GetAudioFilePaths(GetAudioFolderPath(_currentAssetLibrary, kind));
            var entries = orderedPaths
                .Select(path =>
                {
                    var entry = ParseAudioFileName(kind, path);
                    return PathsEqual(path, musicPath)
                        ? entry with { Remark = SanitizeRemark(remarkBox.Text) }
                        : entry;
                })
                .ToList();

            SetAudioNormalizing(kind, true);
            try
            {
                await RenameAudioEntriesAsync(kind, entries);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置{GetAudioDisplayName(kind)}备注：{Path.GetFileName(musicPath)}");
            return FindRenamedAudioPath(kind, entries, musicPath);
        }

        private async Task<bool> DeleteMusicAsync(string musicPath)
        {
            return await DeleteAudioAsync(AudioAssetKind.Music, musicPath);
        }

        private async Task<bool> DeleteAudioAsync(AudioAssetKind kind, string musicPath)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = $"删除{GetAudioDisplayName(kind)}",
                Content = $"确定删除 {Path.GetFileName(musicPath)} 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonStyle = CreateDestructivePrimaryButtonStyle(),
                XamlRoot = Content.XamlRoot
            };
            dialog.RightTapped += (_, args) =>
            {
                dialog.Hide();
                args.Handled = true;
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return false;
            }

            File.Delete(musicPath);

            SetAudioNormalizing(kind, true);
            try
            {
                await NormalizeAudioFilesAsync(kind, GetAudioFolderPath(_currentAssetLibrary, kind));
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已删除{GetAudioDisplayName(kind)}：{Path.GetFileName(musicPath)}");
            return true;
        }

        private async Task NormalizeMusicFilesAsync(string musicFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            await NormalizeAudioFilesAsync(AudioAssetKind.Music, musicFolderPath, orderedPaths);
        }

        private async Task NormalizeAudioFilesAsync(AudioAssetKind kind, string musicFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            var sourcePaths = orderedPaths is null
                ? Directory
                    .EnumerateFiles(musicFolderPath)
                    .Where(path => MusicExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName)
                    .ToList()
                : orderedPaths
                    .Where(path => MusicExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

            var entries = sourcePaths
                .Select(path => ParseAudioFileName(kind, path))
                .ToList();

            await RenameAudioEntriesAsync(kind, entries);
        }

        private static Task RenameMusicEntriesAsync(IReadOnlyList<MusicEntry> entries)
        {
            return RenameAudioEntriesAsync(AudioAssetKind.Music, entries);
        }

        private static Task RenameAudioEntriesAsync(AudioAssetKind kind, IReadOnlyList<MusicEntry> entries)
        {
            if (entries.Count == 0)
            {
                return Task.CompletedTask;
            }

            var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
            var plannedMoves = entries
                .Select((entry, index) =>
                {
                    var folderPath = Path.GetDirectoryName(entry.Path)!;
                    var baseName = $"{GetAudioPrefix(kind)}{index.ToString().PadLeft(digitCount, '0')}";
                    var fileName = string.IsNullOrWhiteSpace(entry.Remark)
                        ? $"{baseName}.wav"
                        : $"{baseName}_{entry.Remark}.wav";
                    return new MusicRename(entry, Path.Combine(folderPath, fileName));
                })
                .ToList();

            if (plannedMoves.All(move => PathsExactlyEqual(move.Entry.Path, move.TargetPath)))
            {
                return Task.CompletedTask;
            }

            var tempMoves = plannedMoves
                .Select(move =>
                {
                    var tempPath = Path.Combine(Path.GetDirectoryName(move.Entry.Path)!, $"__{GetAudioPrefix(kind).ToLowerInvariant()}_rename_{Guid.NewGuid():N}.wav");
                    File.Move(move.Entry.Path, tempPath, overwrite: true);
                    return move with { Entry = move.Entry with { Path = tempPath } };
                })
                .ToList();

            foreach (var move in tempMoves)
            {
                File.Move(move.Entry.Path, move.TargetPath, overwrite: true);
            }

            return Task.CompletedTask;
        }

        private static List<string> GetMusicFilePaths(string musicFolderPath)
        {
            return GetAudioFilePaths(musicFolderPath);
        }

        private static List<string> GetAudioFilePaths(string musicFolderPath)
        {
            return Directory
                .EnumerateFiles(musicFolderPath, "*.wav")
                .OrderBy(Path.GetFileName)
                .ToList();
        }

        private static MusicEntry ParseMusicFileName(string musicPath)
        {
            return ParseAudioFileName(AudioAssetKind.Music, musicPath);
        }

        private static MusicEntry ParseAudioFileName(AudioAssetKind kind, string musicPath)
        {
            var name = Path.GetFileNameWithoutExtension(musicPath);
            var match = Regex.Match(name, $"^{Regex.Escape(GetAudioPrefix(kind))}\\d+(?:_(?<remark>.+))?$", RegexOptions.IgnoreCase);
            return new MusicEntry(
                musicPath,
                match.Success ? match.Groups["remark"].Value : string.Empty);
        }

        private static string? FindRenamedMusicPath(IReadOnlyList<MusicEntry> entries, string originalPath)
        {
            return FindRenamedAudioPath(AudioAssetKind.Music, entries, originalPath);
        }

        private static string? FindRenamedAudioPath(AudioAssetKind kind, IReadOnlyList<MusicEntry> entries, string originalPath)
        {
            var originalIndex = entries
                .Select((entry, index) => new { entry, index })
                .FirstOrDefault(pair => PathsEqual(pair.entry.Path, originalPath))?.index;
            if (originalIndex is null)
            {
                return null;
            }

            var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
            var entryAtIndex = entries[originalIndex.Value];
            var baseName = $"{GetAudioPrefix(kind)}{originalIndex.Value.ToString().PadLeft(digitCount, '0')}";
            var fileName = string.IsNullOrWhiteSpace(entryAtIndex.Remark)
                ? $"{baseName}.wav"
                : $"{baseName}_{entryAtIndex.Remark}.wav";
            return Path.Combine(Path.GetDirectoryName(entryAtIndex.Path)!, fileName);
        }

        private void ShowBackgroundImageViewerPage(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return;
            }

            _viewingBackgroundImagePath = imagePath;
            _viewingCharacterClothPath = null;
            _viewingCharacterFacePath = null;
            _viewingCharacterAdornPath = null;
            BackgroundImageViewerTabTitleText.Text = Path.GetFileNameWithoutExtension(imagePath);
            ResetBackgroundImageViewerTransform();
            _ = LoadThumbnailFromFileAsync(BackgroundImageViewerImage, imagePath);

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerCloseButton.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开背景图查看：{Path.GetFileName(imagePath)}");
        }

        private void ShowCharacterClothViewerPage(string clothPath)
        {
            if (_currentCharacter is null || !File.Exists(clothPath))
            {
                return;
            }

            _viewingBackgroundImagePath = null;
            _viewingCharacterClothPath = clothPath;
            _viewingCharacterFacePath = null;
            _viewingCharacterAdornPath = null;
            _selectedCharacterClothPath = clothPath;
            BackgroundImageViewerTabTitleText.Text = $"服装 {Path.GetFileNameWithoutExtension(clothPath)}";
            ResetBackgroundImageViewerTransform();
            _ = LoadThumbnailFromFileAsync(BackgroundImageViewerImage, clothPath);
            _ = UpdateCharacterLayerPreviewAsync();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerCloseButton.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开服装查看：{Path.GetFileName(clothPath)}");
        }

        private void ShowCharacterFaceViewerPage(string facePath)
        {
            if (_currentCharacter is null || !File.Exists(facePath))
            {
                return;
            }

            _viewingBackgroundImagePath = null;
            _viewingCharacterClothPath = null;
            _viewingCharacterFacePath = facePath;
            _viewingCharacterAdornPath = null;
            _selectedCharacterFacePath = facePath;
            BackgroundImageViewerTabTitleText.Text = $"表情 {Path.GetFileNameWithoutExtension(facePath)}";
            ResetBackgroundImageViewerTransform();
            _ = LoadThumbnailFromFileAsync(BackgroundImageViewerImage, facePath);
            _ = UpdateCharacterLayerPreviewAsync();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerCloseButton.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开表情查看：{Path.GetFileName(facePath)}");
        }

        private void ShowCharacterAdornViewerPage(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return;
            }

            _viewingBackgroundImagePath = null;
            _viewingCharacterClothPath = null;
            _viewingCharacterFacePath = null;
            _viewingCharacterAdornPath = adornPath;
            _selectedCharacterAdornPath = adornPath;
            BackgroundImageViewerTabTitleText.Text = $"装饰 {Path.GetFileNameWithoutExtension(adornPath)}";
            ResetBackgroundImageViewerTransform();
            _ = LoadThumbnailFromFileAsync(BackgroundImageViewerImage, adornPath);
            _ = UpdateCharacterLayerPreviewAsync();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerCloseButton.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开装饰查看：{Path.GetFileName(adornPath)}");
        }

        private void CloseBackgroundImageViewerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseBackgroundImageViewer();
        }

        private void CloseBackgroundImageViewer()
        {
            _isPanningBackgroundImage = false;
            var wasViewingCharacterLayer =
                _viewingCharacterClothPath is not null ||
                _viewingCharacterFacePath is not null ||
                _viewingCharacterAdornPath is not null;
            _viewingBackgroundImagePath = null;
            _viewingCharacterClothPath = null;
            _viewingCharacterFacePath = null;
            _viewingCharacterAdornPath = null;
            BackgroundImageViewerImage.Source = null;
            ResetBackgroundImageViewerTransform();

            if (wasViewingCharacterLayer && _currentCharacter is not null)
            {
                BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
                CharacterDetailPage.Visibility = Visibility.Visible;
                CharacterDetailCloseButton.Focus(FocusState.Programmatic);
            }
            else if (_currentAssetLibrary is not null)
            {
                ShowAssetLibraryDetailPage(_currentAssetLibrary);
            }
        }

        private void BackgroundImageViewerPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseBackgroundImageViewer();
                e.Handled = true;
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.NumberPad4 ||
                e.Key == Windows.System.VirtualKey.A)
            {
                ShowAdjacentViewerImage(-1);
                e.Handled = true;
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Right or Windows.System.VirtualKey.NumberPad6 ||
                e.Key == Windows.System.VirtualKey.D)
            {
                ShowAdjacentViewerImage(1);
                e.Handled = true;
            }
        }

        private void BackgroundImageViewerCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(BackgroundImageViewerCanvas);
            var pointerPosition = point.Position;
            var previousScale = _backgroundImageViewerScale;
            var zoomFactor = point.Properties.MouseWheelDelta > 0 ? 1.1 : 0.9;
            _backgroundImageViewerScale = Math.Clamp(_backgroundImageViewerScale * zoomFactor, 0.1, 8);

            var actualZoomFactor = _backgroundImageViewerScale / previousScale;
            var canvasCenterX = BackgroundImageViewerCanvas.ActualWidth / 2;
            var canvasCenterY = BackgroundImageViewerCanvas.ActualHeight / 2;
            var offsetFromCenterX = pointerPosition.X - canvasCenterX - BackgroundImageViewerTransform.TranslateX;
            var offsetFromCenterY = pointerPosition.Y - canvasCenterY - BackgroundImageViewerTransform.TranslateY;
            var proposedTranslateX =
                BackgroundImageViewerTransform.TranslateX - offsetFromCenterX * (actualZoomFactor - 1);
            var proposedTranslateY =
                BackgroundImageViewerTransform.TranslateY - offsetFromCenterY * (actualZoomFactor - 1);
            ClampBackgroundImageTranslation(ref proposedTranslateX, ref proposedTranslateY);

            BackgroundImageViewerTransform.ScaleX = _backgroundImageViewerScale;
            BackgroundImageViewerTransform.ScaleY = _backgroundImageViewerScale;
            BackgroundImageViewerTransform.TranslateX = proposedTranslateX;
            BackgroundImageViewerTransform.TranslateY = proposedTranslateY;
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(BackgroundImageViewerCanvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPanningBackgroundImage = true;
            _lastBackgroundImagePointerPosition = point.Position;
            BackgroundImageViewerCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanningBackgroundImage)
            {
                return;
            }

            var point = e.GetCurrentPoint(BackgroundImageViewerCanvas);
            var deltaX = point.Position.X - _lastBackgroundImagePointerPosition.X;
            var deltaY = point.Position.Y - _lastBackgroundImagePointerPosition.Y;
            var proposedX = BackgroundImageViewerTransform.TranslateX + deltaX;
            var proposedY = BackgroundImageViewerTransform.TranslateY + deltaY;
            ClampBackgroundImageTranslation(ref proposedX, ref proposedY);
            BackgroundImageViewerTransform.TranslateX = proposedX;
            BackgroundImageViewerTransform.TranslateY = proposedY;
            _lastBackgroundImagePointerPosition = point.Position;
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetBackgroundImageViewerTransform();
            e.Handled = true;
        }

        private void BackgroundImageViewerCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndBackgroundImagePan(e);
        }

        private void BackgroundImageViewerCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndBackgroundImagePan(e);
        }

        private void BackgroundImageViewerCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPanningBackgroundImage = false;
        }

        private void EndBackgroundImagePan(PointerRoutedEventArgs e)
        {
            _isPanningBackgroundImage = false;
            BackgroundImageViewerCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void ResetBackgroundImageViewerTransform()
        {
            _backgroundImageViewerScale = 1;
            BackgroundImageViewerTransform.ScaleX = 1;
            BackgroundImageViewerTransform.ScaleY = 1;
            BackgroundImageViewerTransform.TranslateX = 0;
            BackgroundImageViewerTransform.TranslateY = 0;
        }

        private void ClampBackgroundImageTranslation(ref double translateX, ref double translateY)
        {
            var viewerWidth = Math.Max(BackgroundImageViewerCanvas.ActualWidth, 1);
            var viewerHeight = Math.Max(BackgroundImageViewerCanvas.ActualHeight, 1);
            var imageWidth = BackgroundImageViewerImage.ActualWidth * _backgroundImageViewerScale;
            var imageHeight = BackgroundImageViewerImage.ActualHeight * _backgroundImageViewerScale;
            var horizontalAllowance = Math.Max(viewerWidth * 0.35, imageWidth * 0.35);
            var verticalAllowance = Math.Max(viewerHeight * 0.35, imageHeight * 0.35);

            translateX = Math.Clamp(translateX, -horizontalAllowance, horizontalAllowance);
            translateY = Math.Clamp(translateY, -verticalAllowance, verticalAllowance);
        }

        private void ShowAdjacentBackgroundImage(int direction)
        {
            if (_currentAssetLibrary is null || _viewingBackgroundImagePath is null)
            {
                return;
            }

            var orderedPaths = GetBackgroundImagePaths(GetBackgroundFolderPath(_currentAssetLibrary));
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, _viewingBackgroundImagePath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            ShowBackgroundImageViewerPage(orderedPaths[nextIndex]);
        }

        private void ShowAdjacentViewerImage(int direction)
        {
            if (_viewingCharacterClothPath is not null)
            {
                ShowAdjacentCharacterCloth(direction);
            }
            else if (_viewingCharacterFacePath is not null)
            {
                ShowAdjacentCharacterFace(direction);
            }
            else if (_viewingCharacterAdornPath is not null)
            {
                ShowAdjacentCharacterAdorn(direction);
            }
            else
            {
                ShowAdjacentBackgroundImage(direction);
            }
        }

        private void ShowAdjacentCharacterCloth(int direction)
        {
            if (_currentCharacter is null || _viewingCharacterClothPath is null)
            {
                return;
            }

            var orderedPaths = GetCharacterLayerImagePaths(Path.Combine(_currentCharacter.Path, "DN_Cloth"));
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, _viewingCharacterClothPath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            ShowCharacterClothViewerPage(orderedPaths[nextIndex]);
        }

        private void ShowAdjacentCharacterFace(int direction)
        {
            if (_currentCharacter is null || _viewingCharacterFacePath is null)
            {
                return;
            }

            var orderedPaths = GetCharacterLayerImagePaths(Path.Combine(_currentCharacter.Path, "FC_Face"));
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, _viewingCharacterFacePath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            ShowCharacterFaceViewerPage(orderedPaths[nextIndex]);
        }

        private void ShowAdjacentCharacterAdorn(int direction)
        {
            if (_currentCharacter is null || _viewingCharacterAdornPath is null)
            {
                return;
            }

            var orderedPaths = GetCharacterLayerImagePaths(Path.Combine(_currentCharacter.Path, "AD_Adorn"));
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, _viewingCharacterAdornPath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            ShowCharacterAdornViewerPage(orderedPaths[nextIndex]);
        }

        private async void BackgroundImageViewerRemarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewingCharacterClothPath is not null)
            {
                var updatedClothPath = await SetCharacterClothRemarkAsync(_viewingCharacterClothPath);
                if (!string.IsNullOrWhiteSpace(updatedClothPath) && File.Exists(updatedClothPath))
                {
                    _viewingCharacterClothPath = updatedClothPath;
                    _selectedCharacterClothPath = updatedClothPath;
                    BackgroundImageViewerTabTitleText.Text = $"服装 {Path.GetFileNameWithoutExtension(updatedClothPath)}";
                    await LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedClothPath);
                }

                return;
            }

            if (_viewingCharacterFacePath is not null)
            {
                var updatedFacePath = await SetCharacterFaceRemarkAsync(_viewingCharacterFacePath);
                if (!string.IsNullOrWhiteSpace(updatedFacePath) && File.Exists(updatedFacePath))
                {
                    _viewingCharacterFacePath = updatedFacePath;
                    _selectedCharacterFacePath = updatedFacePath;
                    BackgroundImageViewerTabTitleText.Text = $"表情 {Path.GetFileNameWithoutExtension(updatedFacePath)}";
                    await LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedFacePath);
                }

                return;
            }

            if (_viewingCharacterAdornPath is not null)
            {
                var updatedAdornPath = await SetCharacterAdornRemarkAsync(_viewingCharacterAdornPath);
                if (!string.IsNullOrWhiteSpace(updatedAdornPath) && File.Exists(updatedAdornPath))
                {
                    _viewingCharacterAdornPath = updatedAdornPath;
                    _selectedCharacterAdornPath = updatedAdornPath;
                    BackgroundImageViewerTabTitleText.Text = $"装饰 {Path.GetFileNameWithoutExtension(updatedAdornPath)}";
                    await LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedAdornPath);
                }

                return;
            }

            if (_viewingBackgroundImagePath is null)
            {
                return;
            }

            var updatedPath = await SetBackgroundImageRemarkAsync(_viewingBackgroundImagePath);
            if (string.IsNullOrWhiteSpace(updatedPath) || !File.Exists(updatedPath))
            {
                return;
            }

            _viewingBackgroundImagePath = updatedPath;
            BackgroundImageViewerTabTitleText.Text = Path.GetFileNameWithoutExtension(updatedPath);
            await LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedPath);
        }

        private async void BackgroundImageViewerDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewingCharacterClothPath is not null)
            {
                var deletedCloth = await DeleteCharacterClothAsync(_viewingCharacterClothPath);
                if (deletedCloth)
                {
                    CloseBackgroundImageViewer();
                }

                return;
            }

            if (_viewingCharacterFacePath is not null)
            {
                var deletedFace = await DeleteCharacterFaceAsync(_viewingCharacterFacePath);
                if (deletedFace)
                {
                    CloseBackgroundImageViewer();
                }

                return;
            }

            if (_viewingCharacterAdornPath is not null)
            {
                var deletedAdorn = await DeleteCharacterAdornAsync(_viewingCharacterAdornPath);
                if (deletedAdorn)
                {
                    CloseBackgroundImageViewer();
                }

                return;
            }

            if (_viewingBackgroundImagePath is null)
            {
                return;
            }

            var deleted = await DeleteBackgroundImageAsync(_viewingBackgroundImagePath);
            if (deleted)
            {
                CloseBackgroundImageViewer();
            }
        }

        private void ShowMusicPlayerPage(string musicPath)
        {
            ShowMusicPlayerPage(musicPath, AudioAssetKind.Music);
        }

        private async void ShowMusicPlayerPage(string musicPath, AudioAssetKind kind)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return;
            }

            _playingMusicPath = musicPath;
            _playingAudioKind = kind;
            MusicPlayerTabTitleText.Text = Path.GetFileNameWithoutExtension(musicPath);
            MusicPlayerTrackTitleText.Text = Path.GetFileNameWithoutExtension(musicPath);

            var file = await StorageFile.GetFileFromPathAsync(musicPath);
            MusicPlayerElement.Source = MediaSource.CreateFromStorageFile(file);
            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayPauseButton.Content = "播放";

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Visible;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            MusicPlayerCloseButton.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开{GetAudioDisplayName(kind)}播放：{Path.GetFileName(musicPath)}");
        }

        private void CloseMusicPlayerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseMusicPlayer();
        }

        private void CloseMusicPlayer()
        {
            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayerElement.Source = null;
            _playingMusicPath = null;

            if (_currentAssetLibrary is not null)
            {
                ShowAssetLibraryDetailPage(_currentAssetLibrary);
            }
        }

        private void MusicPlayerPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseMusicPlayer();
                e.Handled = true;
            }
        }

        private void PreviousMusicButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAdjacentMusic(-1);
        }

        private void NextMusicButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAdjacentMusic(1);
        }

        private void ShowAdjacentMusic(int direction)
        {
            if (_currentAssetLibrary is null || _playingMusicPath is null)
            {
                return;
            }

            var orderedPaths = GetAudioFilePaths(GetAudioFolderPath(_currentAssetLibrary, _playingAudioKind));
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, _playingMusicPath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            ShowMusicPlayerPage(orderedPaths[nextIndex], _playingAudioKind);
        }

        private void MusicPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (MusicPlayerElement.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
            {
                MusicPlayerElement.MediaPlayer.Pause();
                MusicPlayPauseButton.Content = "播放";
            }
            else
            {
                MusicPlayerElement.MediaPlayer.Play();
                MusicPlayPauseButton.Content = "暂停";
            }
        }

        private async void MusicPlayerRemarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playingMusicPath is null)
            {
                return;
            }

            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayerElement.Source = null;
            var updatedPath = await SetAudioRemarkAsync(_playingAudioKind, _playingMusicPath);
            if (string.IsNullOrWhiteSpace(updatedPath) || !File.Exists(updatedPath))
            {
                await ReloadMusicPlayerSourceAsync(_playingMusicPath);
                return;
            }

            _playingMusicPath = updatedPath;
            MusicPlayerTabTitleText.Text = Path.GetFileNameWithoutExtension(updatedPath);
            MusicPlayerTrackTitleText.Text = Path.GetFileNameWithoutExtension(updatedPath);
            await ReloadMusicPlayerSourceAsync(updatedPath);
        }

        private async void MusicPlayerDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playingMusicPath is null)
            {
                return;
            }

            MusicPlayerElement.MediaPlayer.Pause();
            MusicPlayerElement.Source = null;
            var deleted = await DeleteAudioAsync(_playingAudioKind, _playingMusicPath);
            if (deleted)
            {
                CloseMusicPlayer();
            }
            else
            {
                await ReloadMusicPlayerSourceAsync(_playingMusicPath);
            }
        }

        private async Task CreateCharacterAsync()
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            var input = await ShowCharacterEditorDialogAsync("创建角色", null);
            if (input is null)
            {
                return;
            }

            var characterFolderName = SanitizeCharacterFolderName(input.Code);
            var characterPath = Path.Combine(GetCharacterFolderPath(_currentAssetLibrary), characterFolderName);
            if (Directory.Exists(characterPath))
            {
                AppendLog(LogKind.Warning, $"无法创建角色，同名英文代号已存在：{input.Code}");
                return;
            }

            Directory.CreateDirectory(characterPath);
            EnsureCharacterSubfolders(characterPath);
            WriteCharacterMeta(characterPath, input);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacters(_currentAssetLibrary);
            AppendLog(LogKind.User, $"创建角色：{input.Name}（{input.Code}）");
        }

        private async Task RenameCharacterAsync(CharacterInfo character)
        {
            var input = await ShowCharacterEditorDialogAsync("重命名角色", character);
            if (input is not null)
            {
                await RenameCharacterAsync(character, input);
            }
        }

        private async Task RenameCharacterAsync(CharacterInfo character, CharacterEditorInput input)
        {
            if (_currentAssetLibrary is null || !Directory.Exists(character.Path))
            {
                return;
            }

            var newFolderName = SanitizeCharacterFolderName(input.Code);
            var newPath = Path.Combine(GetCharacterFolderPath(_currentAssetLibrary), newFolderName);
            if (!PathsEqual(character.Path, newPath))
            {
                if (Directory.Exists(newPath))
                {
                    AppendLog(LogKind.Warning, $"无法重命名角色，同名英文代号已存在：{input.Code}");
                    return;
                }

                Directory.Move(character.Path, newPath);
            }

            EnsureCharacterSubfolders(newPath);
            WriteCharacterMeta(newPath, input);
            NormalizeCharacterLayerFiles(
                Path.Combine(newPath, "DN_Cloth"),
                CharacterLayerKind.Cloth,
                string.Empty,
                input.Code);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacters(_currentAssetLibrary);
            if (_currentCharacter is not null && PathsEqual(_currentCharacter.Path, character.Path))
            {
                ShowCharacterDetailPage(ReadCharacterInfo(newPath));
            }
            AppendLog(LogKind.User, $"重命名角色：{input.Name}（{input.Code}）");
            await Task.CompletedTask;
        }

        private async Task<CharacterEditorInput?> ShowCharacterEditorDialogAsync(string title, CharacterInfo? character)
        {
            var nameBox = new TextBox
            {
                Header = "角色名字",
                Text = character?.Name ?? string.Empty,
                PlaceholderText = "例如：明绪"
            };
            var codeBox = new TextBox
            {
                Header = "英文代号",
                Text = character?.Code ?? string.Empty,
                PlaceholderText = "例如：Mio"
            };
            var colorBox = new TextBox
            {
                Header = "代表色",
                Text = character?.ColorHex ?? "#D9E8FF",
                PlaceholderText = "#RRGGBB"
            };
            var panel = new StackPanel
            {
                Spacing = 12,
                Children = { nameBox, codeBox, colorBox }
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var name = nameBox.Text.Trim();
            var code = codeBox.Text.Trim();
            var color = NormalizeColorHex(colorBox.Text.Trim());
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                AppendLog(LogKind.Warning, "角色名字和英文代号不能为空。");
                return null;
            }

            return new CharacterEditorInput(name, code, color);
        }

        private void ShowCharacterDetailPage(CharacterInfo character)
        {
            _currentCharacter = character;
            CharacterDetailTabTitleText.Text = $"{character.Name} / {character.Code}";
            CharacterNameTextBox.Text = character.Name;
            CharacterCodeTextBox.Text = character.Code;
            CharacterColorTextBox.Text = character.ColorHex;
            LoadCharacterDetailLayers(character);
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Visible;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            CharacterDetailCloseButton.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开角色详情：{character.Name}");
        }

        private void LoadCharacterDetailLayers(CharacterInfo character)
        {
            EnsureCharacterSubfolders(character.Path);

            var clothFolderPath = Path.Combine(character.Path, "DN_Cloth");
            var faceFolderPath = Path.Combine(character.Path, "FC_Face");
            var adornFolderPath = Path.Combine(character.Path, "AD_Adorn");
            var vfxFolderPath = Path.Combine(character.Path, "VFX");

            NormalizeCharacterLayerFiles(clothFolderPath, CharacterLayerKind.Cloth, GetCharacterLayerDefaultScope(0), character.Code);
            var costumeCount = GetCharacterLayerImagePaths(clothFolderPath).Count;
            var defaultCostumeScope = GetCharacterLayerDefaultScope(costumeCount);
            NormalizeCharacterLayerFiles(faceFolderPath, CharacterLayerKind.Face, defaultCostumeScope);
            NormalizeCharacterLayerFiles(adornFolderPath, CharacterLayerKind.Adorn, defaultCostumeScope);
            NormalizeCharacterLayerFiles(vfxFolderPath, CharacterLayerKind.Vfx, "ALL");

            var clothPaths = GetCharacterLayerImagePaths(clothFolderPath);
            var facePaths = GetCharacterLayerImagePaths(faceFolderPath);
            var adornPaths = GetCharacterLayerImagePaths(adornFolderPath);
            var vfxPaths = Directory
                .EnumerateFiles(vfxFolderPath)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName)
                .ToList();

            _selectedCharacterClothPath = ResolveSelectedCharacterLayerPath(_selectedCharacterClothPath, clothPaths);
            _selectedCharacterFacePath = ResolveSelectedCharacterLayerPath(_selectedCharacterFacePath, facePaths);
            _selectedCharacterAdornPath = ResolveSelectedCharacterLayerPath(_selectedCharacterAdornPath, adornPaths);
            _selectedCharacterVfxPath = ResolveSelectedCharacterLayerPath(_selectedCharacterVfxPath, vfxPaths);

            LoadCharacterImageLayer(CharacterClothGridView, clothFolderPath, CharacterLayerKind.Cloth);
            LoadCharacterImageLayer(CharacterFaceGridView, faceFolderPath, CharacterLayerKind.Face);
            LoadCharacterImageLayer(CharacterAdornGridView, adornFolderPath, CharacterLayerKind.Adorn);
            LoadCharacterVfxLayer(vfxFolderPath);
            _ = UpdateCharacterLayerPreviewAsync();
        }

        private static string? ResolveSelectedCharacterLayerPath(string? selectedPath, IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                var exactMatch = paths.FirstOrDefault(path => PathsEqual(path, selectedPath));
                if (exactMatch is not null)
                {
                    return exactMatch;
                }

                var selectedName = Path.GetFileName(selectedPath);
                var nameMatch = paths.FirstOrDefault(path => string.Equals(Path.GetFileName(path), selectedName, StringComparison.OrdinalIgnoreCase));
                if (nameMatch is not null)
                {
                    return nameMatch;
                }
            }

            return paths[0];
        }

        private void ReloadCharacterDetailLayersPreservingScroll()
        {
            if (_currentCharacter is null)
            {
                return;
            }

            ReloadCharacterDetailLayersPreservingScroll(
                CharacterDetailScrollViewer.HorizontalOffset,
                CharacterDetailScrollViewer.VerticalOffset);
        }

        private void ReloadCharacterDetailLayersPreservingScroll(double horizontalOffset, double verticalOffset)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            LoadCharacterDetailLayers(_currentCharacter);
            _ = RestoreCharacterDetailScrollAsync(horizontalOffset, verticalOffset);
        }

        private async Task RestoreCharacterDetailScrollAsync(double horizontalOffset, double verticalOffset)
        {
            CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
            DispatcherQueue.TryEnqueue(() =>
            {
                CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
                DispatcherQueue.TryEnqueue(() =>
                {
                    CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
                });
            });
            await Task.Delay(60);
            CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
            await Task.Delay(160);
            CharacterDetailScrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
        }

        private void LoadCharacterImageLayer(GridView gridView, string folderPath, CharacterLayerKind layerKind)
        {
            Directory.CreateDirectory(folderPath);
            gridView.Items.Clear();

            var imagePaths = GetCharacterLayerImagePaths(folderPath);
            foreach (var imagePath in imagePaths)
            {
                gridView.Items.Add(CreateCharacterImageLayerCard(imagePath, layerKind));
            }

            UpdateCharacterLayerExpanderHeader(layerKind, imagePaths.Count);
        }

        private void UpdateCharacterLayerExpanderHeader(CharacterLayerKind layerKind, int count)
        {
            switch (layerKind)
            {
                case CharacterLayerKind.Cloth:
                    CharacterClothExpander.Header = $"服装 DN_Cloth [数量：{count}]";
                    break;
                case CharacterLayerKind.Face:
                    CharacterFaceExpander.Header = $"表情 FC_Face [数量：{count}]";
                    break;
                case CharacterLayerKind.Adorn:
                    CharacterAdornExpander.Header = $"装饰 AD_Adorn [数量：{count}]";
                    break;
                case CharacterLayerKind.Vfx:
                    CharacterVfxExpander.Header = $"VFX 滤镜 [数量：{count}]";
                    break;
            }
        }

        private static List<string> GetCharacterLayerImagePaths(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                return [];
            }

            return Directory
                .EnumerateFiles(folderPath)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName)
                .ToList();
        }

        private GridViewItem CreateCharacterImageLayerCard(string imagePath, CharacterLayerKind layerKind)
        {
            var item = new GridViewItem
            {
                Width = 190,
                Height = 190,
                Margin = new Thickness(0, 0, 16, 16),
                Tag = imagePath
            };

            if (layerKind == CharacterLayerKind.Cloth)
            {
                var flyout = new MenuFlyout();
                var remarkItem = new MenuFlyoutItem
                {
                    Text = "设置备注"
                };
                remarkItem.Click += async (_, _) => await SetCharacterClothRemarkAsync(imagePath);
                flyout.Items.Add(remarkItem);

                var deleteItem = new MenuFlyoutItem
                {
                    Text = "删除"
                };
                deleteItem.Click += async (_, _) => await DeleteCharacterClothAsync(imagePath);
                flyout.Items.Add(deleteItem);
                item.ContextFlyout = flyout;
            }
            else if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn)
            {
                var flyout = new MenuFlyout();
                var remarkItem = new MenuFlyoutItem
                {
                    Text = "设置备注"
                };
                remarkItem.Click += async (_, _) =>
                {
                    if (layerKind == CharacterLayerKind.Face)
                    {
                        await SetCharacterFaceRemarkAsync(imagePath);
                    }
                    else
                    {
                        await SetCharacterAdornRemarkAsync(imagePath);
                    }
                };
                flyout.Items.Add(remarkItem);

                var rangeItem = new MenuFlyoutItem
                {
                    Text = "可用范围"
                };
                rangeItem.Click += async (_, _) =>
                {
                    if (layerKind == CharacterLayerKind.Face)
                    {
                        await SetCharacterFaceAvailabilityAsync(imagePath);
                    }
                    else
                    {
                        await SetCharacterAdornAvailabilityAsync(imagePath);
                    }
                };
                flyout.Items.Add(rangeItem);

                var deleteItem = new MenuFlyoutItem
                {
                    Text = "删除"
                };
                deleteItem.Click += async (_, _) =>
                {
                    if (layerKind == CharacterLayerKind.Face)
                    {
                        await DeleteCharacterFaceAsync(imagePath);
                    }
                    else
                    {
                        await DeleteCharacterAdornAsync(imagePath);
                    }
                };
                flyout.Items.Add(deleteItem);
                item.ContextFlyout = flyout;
            }
            else
            {
                item.Tapped += async (_, _) =>
                {
                    SetSelectedCharacterLayer(layerKind, imagePath);
                    await UpdateCharacterLayerPreviewAsync();
                };
            }

            var panel = new StackPanel
            {
                Spacing = 6,
                Tag = imagePath
            };

            panel.Children.Add(CreateThumbnail(imagePath, 178, 152, showAddIcon: false));
            panel.Children.Add(new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(imagePath),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });

            item.Content = panel;
            return item;
        }

        private void LoadCharacterVfxLayer(string folderPath)
        {
            Directory.CreateDirectory(folderPath);
            CharacterVfxGridView.Items.Clear();

            var vfxPaths = Directory
                .EnumerateFiles(folderPath)
                .OrderBy(Path.GetFileName)
                .ToList();

            foreach (var vfxPath in vfxPaths)
            {
                CharacterVfxGridView.Items.Add(CreateCharacterVfxIndexCard(vfxPath));
            }

            UpdateCharacterLayerExpanderHeader(CharacterLayerKind.Vfx, vfxPaths.Count);
        }

        private GridViewItem CreateCharacterVfxIndexCard(string vfxPath)
        {
            var item = new GridViewItem
            {
                Width = 220,
                Height = 92,
                Margin = new Thickness(0, 0, 14, 14),
                Tag = vfxPath
            };
            item.Tapped += async (_, _) =>
            {
                _selectedCharacterVfxPath = vfxPath;
                await UpdateCharacterLayerPreviewAsync();
            };

            var title = new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(vfxPath),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 1);

            item.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                Child = new Grid
                {
                    Padding = new Thickness(14, 10, 14, 10),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    Children =
                    {
                        new SymbolIcon { Symbol = Symbol.Filter, VerticalAlignment = VerticalAlignment.Center },
                        title
                    }
                }
            };
            return item;
        }

        private void SetSelectedCharacterLayer(CharacterLayerKind layerKind, string imagePath)
        {
            switch (layerKind)
            {
                case CharacterLayerKind.Cloth:
                    _selectedCharacterClothPath = imagePath;
                    break;
                case CharacterLayerKind.Face:
                    _selectedCharacterFacePath = imagePath;
                    break;
                case CharacterLayerKind.Adorn:
                    _selectedCharacterAdornPath = imagePath;
                    break;
                case CharacterLayerKind.Vfx:
                    _selectedCharacterVfxPath = imagePath;
                    break;
            }

            if (layerKind == CharacterLayerKind.Cloth && _currentCharacter is not null)
            {
                _selectedCharacterFacePath = ResolveSelectedCharacterLayerPath(_selectedCharacterFacePath, GetCharacterDetailLayerPaths(CharacterLayerKind.Face));
                _selectedCharacterAdornPath = ResolveSelectedCharacterLayerPath(_selectedCharacterAdornPath, GetCharacterDetailLayerPaths(CharacterLayerKind.Adorn));
                _selectedCharacterVfxPath = ResolveSelectedCharacterLayerPath(_selectedCharacterVfxPath, GetCharacterDetailLayerPaths(CharacterLayerKind.Vfx));
            }
        }

        private async Task UpdateCharacterLayerPreviewAsync()
        {
            var hasCloth = await SetCharacterPreviewImageAsync(CharacterPreviewClothImage, _selectedCharacterClothPath);
            var hasFace = await SetCharacterPreviewImageAsync(
                CharacterPreviewFaceImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterFacePath) ? _selectedCharacterFacePath : null);
            var hasAdorn = await SetCharacterPreviewImageAsync(
                CharacterPreviewAdornImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterAdornPath) ? _selectedCharacterAdornPath : null);
            var hasVfx = await SetCharacterPreviewImageAsync(
                CharacterPreviewVfxImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterVfxPath) ? _selectedCharacterVfxPath : null);
            var hasAnyLayer = hasCloth || hasFace || hasAdorn || hasVfx;

            CharacterPreviewEmptyText.Visibility = hasAnyLayer ? Visibility.Collapsed : Visibility.Visible;
        }

        private void CharacterPreviewSurface_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            CharacterPreviewSurface.BorderBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
            CharacterPreviewSurface.BorderThickness = new Thickness(2);
            CharacterPreviewSurface.Focus(FocusState.Programmatic);
        }

        private void CharacterPreviewSurface_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            CharacterPreviewSurface.BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush;
            CharacterPreviewSurface.BorderThickness = new Thickness(1);
        }

        private void CharacterPreviewSurface_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            var clothItem = new MenuFlyoutItem { Text = "服装" };
            clothItem.Click += async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Cloth);
            flyout.Items.Add(clothItem);

            var faceItem = new MenuFlyoutItem { Text = "表情" };
            faceItem.Click += async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Face);
            flyout.Items.Add(faceItem);

            var adornItem = new MenuFlyoutItem { Text = "装饰" };
            adornItem.Click += async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Adorn);
            flyout.Items.Add(adornItem);

            flyout.ShowAt(CharacterPreviewSurface);
            e.Handled = true;
        }

        private async void CharacterPreviewSurface_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Q:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Adorn, -1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.E:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Adorn, 1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.A:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Face, -1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.D:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Face, 1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Z:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Cloth, -1);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.C:
                    await CycleCharacterDetailLayerAsync(CharacterLayerKind.Cloth, 1);
                    e.Handled = true;
                    break;
            }
        }

        private async void CharacterPreviewChooseCloth_Click(object sender, RoutedEventArgs e)
        {
            await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Cloth);
        }

        private async void CharacterPreviewChooseFace_Click(object sender, RoutedEventArgs e)
        {
            await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Face);
        }

        private async void CharacterPreviewChooseAdorn_Click(object sender, RoutedEventArgs e)
        {
            await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Adorn);
        }

        private async Task ChooseCharacterDetailLayerAsync(CharacterLayerKind layerKind)
        {
            var paths = GetCharacterDetailLayerPaths(layerKind);
            if (paths.Count == 0)
            {
                AppendLog(LogKind.Warning, $"当前角色没有可选择的{GetCharacterLayerDisplayName(layerKind)}。");
                return;
            }

            var selected = await ShowStoryChoiceDialogAsync(
                $"选择{GetCharacterLayerDisplayName(layerKind)}",
                paths.Select((path, index) => new StoryObjectChoice(
                    index.ToString(),
                    $"{index}: {Path.GetFileNameWithoutExtension(path)}",
                    path,
                    BuildCharacterDetailLayerPreviewPaths(layerKind, path))).ToList());
            if (selected is not string selectedPath || !File.Exists(selectedPath))
            {
                return;
            }

            SetSelectedCharacterLayer(layerKind, selectedPath);
            await UpdateCharacterLayerPreviewAsync();
            var selectedIndex = paths.FindIndex(path => PathsEqual(path, selectedPath));
            ShowCharacterDetailLayerChangedStatus(layerKind, selectedIndex, selectedPath);
            CharacterPreviewSurface.Focus(FocusState.Programmatic);
        }

        private async Task CycleCharacterDetailLayerAsync(CharacterLayerKind layerKind, int direction)
        {
            var paths = GetCharacterDetailLayerPaths(layerKind);
            if (paths.Count == 0)
            {
                return;
            }

            var currentPath = GetSelectedCharacterLayerPath(layerKind);
            var currentIndex = string.IsNullOrWhiteSpace(currentPath)
                ? -1
                : paths.FindIndex(path => PathsEqual(path, currentPath));
            var nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + direction + paths.Count) % paths.Count;

            SetSelectedCharacterLayer(layerKind, paths[nextIndex]);
            await UpdateCharacterLayerPreviewAsync();
            ShowCharacterDetailLayerChangedStatus(layerKind, nextIndex, paths[nextIndex]);
        }

        private IReadOnlyList<string> BuildCharacterDetailLayerPreviewPaths(CharacterLayerKind layerKind, string candidatePath)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => BuildStoryChoicePreviewPaths(
                    candidatePath,
                    _currentCharacter is not null && IsCharacterLayerCompatibleWithCloth(_currentCharacter, candidatePath, _selectedCharacterFacePath) ? _selectedCharacterFacePath : null,
                    _currentCharacter is not null && IsCharacterLayerCompatibleWithCloth(_currentCharacter, candidatePath, _selectedCharacterAdornPath) ? _selectedCharacterAdornPath : null),
                CharacterLayerKind.Face => BuildStoryChoicePreviewPaths(_selectedCharacterClothPath, candidatePath, _selectedCharacterAdornPath),
                CharacterLayerKind.Adorn => BuildStoryChoicePreviewPaths(_selectedCharacterClothPath, _selectedCharacterFacePath, candidatePath),
                CharacterLayerKind.Vfx => BuildStoryChoicePreviewPaths(_selectedCharacterClothPath, _selectedCharacterFacePath, _selectedCharacterAdornPath, candidatePath),
                _ => BuildStoryChoicePreviewPaths(candidatePath)
            };
        }

        private void ShowCharacterDetailLayerChangedStatus(CharacterLayerKind layerKind, int index, string path)
        {
            CharacterDetailInfoBar.Severity = InfoBarSeverity.Success;
            CharacterDetailInfoBar.Title = $"已更换{GetCharacterLayerDisplayName(layerKind)}";
            CharacterDetailInfoBar.Message = $"{Path.GetFileNameWithoutExtension(path)}（索引 {Math.Max(0, index)}）";
            CharacterDetailInfoBar.IsOpen = true;
        }

        private void CycleCharacterDetailCharacter(int direction)
        {
            if (_currentAssetLibrary is null || _currentCharacter is null)
            {
                return;
            }

            var characters = GetCharactersForAssetLibrary(_currentAssetLibrary)
                .OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (characters.Count == 0)
            {
                return;
            }

            var currentIndex = characters.FindIndex(character => PathsEqual(character.Path, _currentCharacter.Path));
            var nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + direction + characters.Count) % characters.Count;
            ShowCharacterDetailPage(characters[nextIndex]);
        }

        private void ClearCharacterDetailPreview()
        {
            _selectedCharacterClothPath = null;
            _selectedCharacterFacePath = null;
            _selectedCharacterAdornPath = null;
            _selectedCharacterVfxPath = null;
            _ = UpdateCharacterLayerPreviewAsync();
        }

        private List<string> GetCharacterDetailLayerPaths(CharacterLayerKind layerKind)
        {
            if (_currentCharacter is null)
            {
                return [];
            }

            var folderName = layerKind switch
            {
                CharacterLayerKind.Cloth => "DN_Cloth",
                CharacterLayerKind.Face => "FC_Face",
                CharacterLayerKind.Adorn => "AD_Adorn",
                CharacterLayerKind.Vfx => "VFX",
                _ => "DN_Cloth"
            };
            var paths = GetCharacterLayerImagePaths(Path.Combine(_currentCharacter.Path, folderName));
            if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn or CharacterLayerKind.Vfx)
            {
                paths = paths
                    .Where(path => IsCharacterLayerCompatibleWithCloth(_currentCharacter, _selectedCharacterClothPath, path))
                    .ToList();
            }

            return paths;
        }

        private string? GetSelectedCharacterLayerPath(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => _selectedCharacterClothPath,
                CharacterLayerKind.Face => _selectedCharacterFacePath,
                CharacterLayerKind.Adorn => _selectedCharacterAdornPath,
                CharacterLayerKind.Vfx => _selectedCharacterVfxPath,
                _ => null
            };
        }

        private static string GetCharacterLayerDisplayName(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => "服装",
                CharacterLayerKind.Face => "表情",
                CharacterLayerKind.Adorn => "装饰",
                CharacterLayerKind.Vfx => "滤镜",
                _ => "图层"
            };
        }

        private bool IsCharacterLayerCompatibleWithSelectedCloth(string? layerPath)
        {
            return _currentCharacter is null
                ? false
                : IsCharacterLayerCompatibleWithCloth(_currentCharacter, _selectedCharacterClothPath, layerPath);
        }

        private bool IsCharacterLayerCompatibleWithCloth(CharacterInfo character, string? clothPath, string? layerPath)
        {
            if (string.IsNullOrWhiteSpace(layerPath) || !File.Exists(layerPath))
            {
                return false;
            }

            var clothIndex = GetCharacterLayerIndex(clothPath, CharacterLayerKind.Cloth);
            if (clothIndex is null)
            {
                return true;
            }

            var layerKind = GetCharacterLayerKindFromPath(layerPath);
            if (layerKind is null || layerKind == CharacterLayerKind.Cloth)
            {
                return true;
            }

            if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn)
            {
                var layerFolderPath = layerKind == CharacterLayerKind.Face
                    ? Path.Combine(character.Path, "FC_Face")
                    : Path.Combine(character.Path, "AD_Adorn");
                var meta = layerKind == CharacterLayerKind.Face
                    ? ReadCharacterFaceScopeMeta(layerFolderPath)
                    : ReadCharacterAdornScopeMeta(layerFolderPath);
                if (meta.Entries.TryGetValue(Path.GetFileName(layerPath), out var metaEntry))
                {
                    if (metaEntry.UseAllCostumes || string.IsNullOrWhiteSpace(clothPath) || !File.Exists(clothPath))
                    {
                        return true;
                    }

                    var selectedCostumeHash = ComputeFileHash(clothPath);
                    return metaEntry.CostumeHashes.Contains(selectedCostumeHash, StringComparer.OrdinalIgnoreCase);
                }
            }

            if (!CharacterLayerUsesScope(layerKind.Value))
            {
                return true;
            }

            var entry = ParseCharacterLayerFileName(layerPath, layerKind.Value, "ALL");
            return IsCharacterScopeMatchingCostume(entry.Scope, clothIndex.Value);
        }

        private bool IsCharacterLayerMetaCompatibleWithSelectedCloth(string layerPath, CharacterLayerKind layerKind)
        {
            if (_currentCharacter is null || _selectedCharacterClothPath is null)
            {
                return true;
            }

            var layerFolderPath = layerKind == CharacterLayerKind.Face
                ? Path.Combine(_currentCharacter.Path, "FC_Face")
                : Path.Combine(_currentCharacter.Path, "AD_Adorn");
            var meta = layerKind == CharacterLayerKind.Face
                ? ReadCharacterFaceScopeMeta(layerFolderPath)
                : ReadCharacterAdornScopeMeta(layerFolderPath);
            if (!meta.Entries.TryGetValue(Path.GetFileName(layerPath), out var entry) || entry.UseAllCostumes)
            {
                return true;
            }

            if (!File.Exists(_selectedCharacterClothPath))
            {
                return true;
            }

            var selectedCostumeHash = ComputeFileHash(_selectedCharacterClothPath);
            return entry.CostumeHashes.Contains(selectedCostumeHash, StringComparer.OrdinalIgnoreCase);
        }

        private static async Task<bool> SetCharacterPreviewImageAsync(Image image, string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) ||
                !File.Exists(imagePath) ||
                !ImageExtensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase))
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                return false;
            }

            image.Visibility = Visibility.Visible;
            await LoadThumbnailFromFileAsync(image, imagePath);
            return true;
        }

        private void NormalizeCharacterLayerFiles(
            string folderPath,
            CharacterLayerKind layerKind,
            string defaultScope,
            string? characterName = null)
        {
            Directory.CreateDirectory(folderPath);
            var sourcePaths = Directory
                .EnumerateFiles(folderPath)
                .Where(path => layerKind == CharacterLayerKind.Vfx ||
                               ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName)
                .ToList();

            if (sourcePaths.Count == 0)
            {
                return;
            }

            var entries = sourcePaths
                .Select(path => ParseCharacterLayerFileName(path, layerKind, defaultScope))
                .ToList();
            if (layerKind == CharacterLayerKind.Face)
            {
                RenameCharacterFaceEntriesAndUpdateMeta(entries);
                return;
            }

            if (layerKind == CharacterLayerKind.Adorn)
            {
                RenameCharacterAdornEntriesAndUpdateMeta(entries);
                return;
            }

            RenameCharacterLayerEntries(entries, layerKind, characterName);
        }

        private void RenameCharacterLayerEntries(
            IReadOnlyList<CharacterLayerEntry> entries,
            CharacterLayerKind layerKind,
            string? characterName = null)
        {
            var renames = new List<CharacterLayerRename>();
            var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var folderPath = Path.GetDirectoryName(entry.Path)!;
                var extension = Path.GetExtension(entry.Path).ToLowerInvariant();
                var indexName = $"{GetCharacterLayerPrefix(layerKind)}{i.ToString().PadLeft(digitCount, '0')}";
                var fileName = BuildCharacterLayerFileName(indexName, entry.Remark, entry.Scope, layerKind, extension, characterName);
                renames.Add(new CharacterLayerRename(entry, Path.Combine(folderPath, fileName)));
            }

            var tempRenames = new List<(string TempPath, string TargetPath)>();
            foreach (var rename in renames)
            {
                if (PathsExactlyEqual(rename.Entry.Path, rename.TargetPath))
                {
                    continue;
                }

                var tempPath = Path.Combine(Path.GetDirectoryName(rename.Entry.Path)!, $"__character_layer_rename_{Guid.NewGuid():N}{Path.GetExtension(rename.Entry.Path)}");
                File.Move(rename.Entry.Path, tempPath);
                tempRenames.Add((tempPath, rename.TargetPath));
            }

            foreach (var (tempPath, targetPath) in tempRenames)
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);
            }
        }

        private void RenameCharacterFaceEntriesAndUpdateMeta(IReadOnlyList<CharacterLayerEntry> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }

            var faceFolderPath = Path.GetDirectoryName(entries[0].Path)!;
            var renameMap = entries
                .Select((entry, index) => new
                {
                    OldFileName = Path.GetFileName(entry.Path),
                    NewFileName = Path.GetFileName(GetCharacterLayerTargetPath(entries, index, CharacterLayerKind.Face))
                })
                .Where(item => !string.Equals(item.OldFileName, item.NewFileName, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.OldFileName, item => item.NewFileName, StringComparer.OrdinalIgnoreCase);

            RenameCharacterLayerEntries(entries, CharacterLayerKind.Face);
            RemapCharacterFaceScopeMeta(faceFolderPath, renameMap);
        }

        private void RenameCharacterAdornEntriesAndUpdateMeta(IReadOnlyList<CharacterLayerEntry> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }

            var adornFolderPath = Path.GetDirectoryName(entries[0].Path)!;
            var renameMap = entries
                .Select((entry, index) => new
                {
                    OldFileName = Path.GetFileName(entry.Path),
                    NewFileName = Path.GetFileName(GetCharacterLayerTargetPath(entries, index, CharacterLayerKind.Adorn))
                })
                .Where(item => !string.Equals(item.OldFileName, item.NewFileName, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.OldFileName, item => item.NewFileName, StringComparer.OrdinalIgnoreCase);

            RenameCharacterLayerEntries(entries, CharacterLayerKind.Adorn);
            RemapCharacterLayerScopeMeta(adornFolderPath, GetCharacterAdornScopeMetaPath(adornFolderPath), renameMap);
        }

        private static string GetCharacterFaceScopeMetaPath(string faceFolderPath)
        {
            return Path.Combine(faceFolderPath, "face-scope.meta.json");
        }

        private static string GetCharacterAdornScopeMetaPath(string adornFolderPath)
        {
            return Path.Combine(adornFolderPath, "adorn-scope.meta.json");
        }

        private CharacterLayerScopeMeta ReadCharacterFaceScopeMeta(string faceFolderPath)
        {
            return ReadCharacterLayerScopeMeta(GetCharacterFaceScopeMetaPath(faceFolderPath));
        }

        private void WriteCharacterFaceScopeMeta(string faceFolderPath, CharacterLayerScopeMeta meta)
        {
            Directory.CreateDirectory(faceFolderPath);
            File.WriteAllText(GetCharacterFaceScopeMetaPath(faceFolderPath), JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private void RemapCharacterFaceScopeMeta(string faceFolderPath, IReadOnlyDictionary<string, string> renameMap)
        {
            RemapCharacterLayerScopeMeta(faceFolderPath, GetCharacterFaceScopeMetaPath(faceFolderPath), renameMap);
        }

        private void RemoveCharacterFaceScopeEntry(string faceFolderPath, string faceFileName)
        {
            var meta = ReadCharacterFaceScopeMeta(faceFolderPath);
            if (meta.Entries.Remove(faceFileName))
            {
                WriteCharacterFaceScopeMeta(faceFolderPath, meta);
            }
        }

        private CharacterLayerScopeMeta ReadCharacterAdornScopeMeta(string adornFolderPath)
        {
            return ReadCharacterLayerScopeMeta(GetCharacterAdornScopeMetaPath(adornFolderPath));
        }

        private void WriteCharacterAdornScopeMeta(string adornFolderPath, CharacterLayerScopeMeta meta)
        {
            Directory.CreateDirectory(adornFolderPath);
            File.WriteAllText(GetCharacterAdornScopeMetaPath(adornFolderPath), JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private void RemoveCharacterAdornScopeEntry(string adornFolderPath, string adornFileName)
        {
            var meta = ReadCharacterAdornScopeMeta(adornFolderPath);
            if (meta.Entries.Remove(adornFileName))
            {
                WriteCharacterAdornScopeMeta(adornFolderPath, meta);
            }
        }

        private CharacterLayerScopeMeta ReadCharacterLayerScopeMeta(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return new CharacterLayerScopeMeta();
            }

            try
            {
                return JsonSerializer.Deserialize<CharacterLayerScopeMeta>(File.ReadAllText(metaPath)) ?? new CharacterLayerScopeMeta();
            }
            catch
            {
                return new CharacterLayerScopeMeta();
            }
        }

        private void RemapCharacterLayerScopeMeta(
            string folderPath,
            string metaPath,
            IReadOnlyDictionary<string, string> renameMap)
        {
            if (renameMap.Count == 0)
            {
                return;
            }

            var meta = ReadCharacterLayerScopeMeta(metaPath);
            if (meta.Entries.Count == 0)
            {
                return;
            }

            var updatedEntries = new Dictionary<string, CharacterLayerScopeEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fileName, entry) in meta.Entries)
            {
                var targetFileName = renameMap.TryGetValue(fileName, out var renamedFileName)
                    ? renamedFileName
                    : fileName;
                updatedEntries[targetFileName] = entry;
            }

            meta.Entries = updatedEntries;
            Directory.CreateDirectory(folderPath);
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private static CharacterLayerEntry ParseCharacterLayerFileName(
            string layerPath,
            CharacterLayerKind layerKind,
            string defaultScope)
        {
            var name = Path.GetFileNameWithoutExtension(layerPath);
            var prefix = GetCharacterLayerPrefix(layerKind);
            var prefixPattern = layerKind == CharacterLayerKind.Cloth
                ? $"^(?:.+_)?{Regex.Escape(prefix)}\\d+(?:_(?<tail>.+))?$"
                : $"^{Regex.Escape(prefix)}\\d+(?:_(?<tail>.+))?$";
            var prefixMatch = Regex.Match(name, prefixPattern, RegexOptions.IgnoreCase);
            if (!prefixMatch.Success)
            {
                return new CharacterLayerEntry(layerPath, SanitizeRemark(name), defaultScope);
            }

            var tail = prefixMatch.Groups["tail"].Value;
            if (string.IsNullOrWhiteSpace(tail))
            {
                return new CharacterLayerEntry(layerPath, string.Empty, defaultScope);
            }

            if (IsCharacterLayerScope(tail))
            {
                return new CharacterLayerEntry(layerPath, string.Empty, NormalizeCharacterLayerScope(tail));
            }

            var lastSeparatorIndex = tail.LastIndexOf('_');
            if (lastSeparatorIndex > 0)
            {
                var maybeScope = tail[(lastSeparatorIndex + 1)..];
                if (IsCharacterLayerScope(maybeScope))
                {
                    return new CharacterLayerEntry(
                        layerPath,
                        SanitizeRemark(tail[..lastSeparatorIndex]),
                        NormalizeCharacterLayerScope(maybeScope));
                }
            }

            if (!CharacterLayerUsesScope(layerKind))
            {
                return new CharacterLayerEntry(layerPath, SanitizeRemark(tail), string.Empty);
            }

            return new CharacterLayerEntry(layerPath, SanitizeRemark(tail), defaultScope);
        }

        private static string BuildCharacterLayerFileName(
            string indexName,
            string remark,
            string scope,
            CharacterLayerKind layerKind,
            string extension,
            string? characterName = null)
        {
            var safeRemark = SanitizeRemark(remark);
            var normalizedScope = CharacterLayerUsesScope(layerKind)
                ? NormalizeCharacterLayerScope(string.IsNullOrWhiteSpace(scope) ? "ALL" : scope)
                : string.Empty;
            var safeCharacterName = string.IsNullOrWhiteSpace(characterName) ? string.Empty : SanitizeRemark(characterName);
            var normalizedIndexName = layerKind == CharacterLayerKind.Cloth && !string.IsNullOrWhiteSpace(safeCharacterName)
                ? $"{safeCharacterName}_{indexName}"
                : indexName;

            if (!CharacterLayerUsesScope(layerKind))
            {
                return string.IsNullOrWhiteSpace(safeRemark)
                    ? $"{normalizedIndexName}{extension}"
                    : $"{normalizedIndexName}_{safeRemark}{extension}";
            }

            return string.IsNullOrWhiteSpace(safeRemark)
                ? $"{normalizedIndexName}_{normalizedScope}{extension}"
                : $"{normalizedIndexName}_{safeRemark}_{normalizedScope}{extension}";
        }

        private static string GetCharacterLayerTargetPath(
            IReadOnlyList<CharacterLayerEntry> entries,
            int index,
            CharacterLayerKind layerKind,
            string? characterCode = null)
        {
            var entry = entries[index];
            var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
            var indexName = $"{GetCharacterLayerPrefix(layerKind)}{index.ToString().PadLeft(digitCount, '0')}";
            var fileName = BuildCharacterLayerFileName(
                indexName,
                entry.Remark,
                entry.Scope,
                layerKind,
                Path.GetExtension(entry.Path).ToLowerInvariant(),
                characterCode);
            return Path.Combine(Path.GetDirectoryName(entry.Path)!, fileName);
        }

        private static string GetCharacterLayerDefaultScope(int costumeCount)
        {
            if (costumeCount <= 0)
            {
                return "ALL";
            }

            var lastIndex = costumeCount - 1;
            var digitCount = Math.Max(2, lastIndex.ToString().Length);
            var startText = 0.ToString().PadLeft(digitCount, '0');
            if (costumeCount == 1)
            {
                return $"DN{startText}";
            }

            var endText = lastIndex.ToString().PadLeft(digitCount, '0');
            return $"DN{startText}-{endText}";
        }

        private static string NormalizeCharacterLayerScope(string scope)
        {
            var trimmed = scope.Trim().ToUpperInvariant();
            if (trimmed == "ALL")
            {
                return "ALL";
            }

            var match = Regex.Match(trimmed, @"^DN(?<start>\d+)(?:-(?<end>\d+))?$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return "ALL";
            }

            var start = int.Parse(match.Groups["start"].Value);
            var endText = match.Groups["end"].Value;
            var digitCount = Math.Max(2, Math.Max(match.Groups["start"].Value.Length, endText.Length));
            var startText = start.ToString().PadLeft(digitCount, '0');
            if (string.IsNullOrWhiteSpace(endText))
            {
                return $"DN{startText}";
            }

            var end = int.Parse(endText);
            var endTextPadded = end.ToString().PadLeft(digitCount, '0');
            return $"DN{startText}-{endTextPadded}";
        }

        private static bool IsCharacterLayerScope(string scope)
        {
            return Regex.IsMatch(scope.Trim(), @"^(ALL|DN\d+(?:-\d+)?)$", RegexOptions.IgnoreCase);
        }

        private static bool IsCharacterScopeMatchingCostume(string scope, int costumeIndex)
        {
            var normalized = NormalizeCharacterLayerScope(scope);
            if (normalized == "ALL")
            {
                return true;
            }

            var match = Regex.Match(normalized, @"^DN(?<start>\d+)(?:-(?<end>\d+))?$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return true;
            }

            var start = int.Parse(match.Groups["start"].Value);
            var end = string.IsNullOrWhiteSpace(match.Groups["end"].Value)
                ? start
                : int.Parse(match.Groups["end"].Value);
            return costumeIndex >= Math.Min(start, end) && costumeIndex <= Math.Max(start, end);
        }

        private static int? GetCharacterLayerIndex(string? layerPath, CharacterLayerKind expectedKind)
        {
            if (string.IsNullOrWhiteSpace(layerPath))
            {
                return null;
            }

            var prefix = GetCharacterLayerPrefix(expectedKind);
            var pattern = expectedKind == CharacterLayerKind.Cloth
                ? $"^(?:.+_)?{Regex.Escape(prefix)}(?<index>\\d+)"
                : $"^{Regex.Escape(prefix)}(?<index>\\d+)";
            var match = Regex.Match(Path.GetFileNameWithoutExtension(layerPath), pattern, RegexOptions.IgnoreCase);
            return match.Success ? int.Parse(match.Groups["index"].Value) : null;
        }

        private static CharacterLayerKind? GetCharacterLayerKindFromPath(string layerPath)
        {
            var name = Path.GetFileNameWithoutExtension(layerPath);
            if (Regex.IsMatch(name, "^(?:.+_)?DN\\d+", RegexOptions.IgnoreCase))
            {
                return CharacterLayerKind.Cloth;
            }

            if (Regex.IsMatch(name, "^FC\\d+", RegexOptions.IgnoreCase))
            {
                return CharacterLayerKind.Face;
            }

            if (Regex.IsMatch(name, "^AD\\d+", RegexOptions.IgnoreCase))
            {
                return CharacterLayerKind.Adorn;
            }

            if (Regex.IsMatch(name, "^VFX\\d+", RegexOptions.IgnoreCase))
            {
                return CharacterLayerKind.Vfx;
            }

            return null;
        }

        private static string GetCharacterLayerPrefix(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => "DN",
                CharacterLayerKind.Face => "FC",
                CharacterLayerKind.Adorn => "AD",
                CharacterLayerKind.Vfx => "VFX",
                _ => "LY"
            };
        }

        private static bool CharacterLayerUsesScope(CharacterLayerKind layerKind)
        {
            return layerKind is CharacterLayerKind.Adorn or CharacterLayerKind.Vfx;
        }

        private void CloseCharacterDetailButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                ShowAssetLibraryDetailPage(_currentAssetLibrary);
            }
        }

        private async void CharacterDetailRenameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is not null)
            {
                await RenameCharacterAsync(_currentCharacter);
            }
        }

        private async void SaveCharacterInlineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var input = new CharacterEditorInput(
                CharacterNameTextBox.Text.Trim(),
                CharacterCodeTextBox.Text.Trim(),
                NormalizeColorHex(CharacterColorTextBox.Text.Trim()));

            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code))
            {
                AppendLog(LogKind.Warning, "角色名称和英文代号不能为空。");
                return;
            }

            await RenameCharacterAsync(_currentCharacter, input);
        }

        private async Task ReloadMusicPlayerSourceAsync(string musicPath)
        {
            if (!File.Exists(musicPath))
            {
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(musicPath);
            MusicPlayerElement.Source = MediaSource.CreateFromStorageFile(file);
            MusicPlayPauseButton.Content = "播放";
        }

        private void AssetLibraryDetailScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(AssetLibraryDetailScrollViewer).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            var direction = delta > 0 ? -1 : 1;
            var scrollDelta = 48 * _appSettings.AssetLibraryScrollSpeedMultiplier * direction;
            var targetOffset = Math.Clamp(
                AssetLibraryDetailScrollViewer.VerticalOffset + scrollDelta,
                0,
                AssetLibraryDetailScrollViewer.ScrollableHeight);
            AssetLibraryDetailScrollViewer.ChangeView(null, targetOffset, null, true);
            e.Handled = true;
        }

        private static Style CreateDestructivePrimaryButtonStyle()
        {
            var style = new Style(typeof(Button))
            {
                BasedOn = Application.Current.Resources["AccentButtonStyle"] as Style
            };
            style.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.IndianRed)));
            style.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)));
            return style;
        }

        private async Task NormalizeBackgroundImagesAsync(string backgroundFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            var sourcePaths = orderedPaths is null
                ? Directory
                    .EnumerateFiles(backgroundFolderPath)
                    .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName)
                    .ToList()
                : orderedPaths.ToList();

            var entries = new List<BackgroundImageEntry>();
            var convertedCount = 0;
            foreach (var sourcePath in sourcePaths)
            {
                var pngPath = sourcePath;
                var extension = Path.GetExtension(sourcePath);
                if (ConvertibleImageExtensions.Contains(extension))
                {
                    pngPath = Path.Combine(backgroundFolderPath, $"__bg_convert_{Guid.NewGuid():N}.png");
                    await ConvertImageToPngAsync(sourcePath, pngPath);
                    File.Delete(sourcePath);
                    convertedCount++;
                }

                var parsed = ParseBackgroundImageFileName(pngPath);
                entries.Add(parsed with { Path = pngPath });
            }

            await RenameBackgroundEntriesAsync(entries);
            if (convertedCount > 0)
            {
                AppendLog(LogKind.Info, $"已转换 JPG/JPEG/WebP 背景图为 png：{convertedCount} 个。");
            }
        }

        private static Task ImportBackgroundImageAsPngAsync(string sourcePath, string targetPngPath)
        {
            var extension = Path.GetExtension(sourcePath);
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPngPath, overwrite: true);
                return Task.CompletedTask;
            }

            return ConvertImageToPngAsync(sourcePath, targetPngPath);
        }

        private static Task RenameBackgroundEntriesAsync(IReadOnlyList<BackgroundImageEntry> entries)
        {
            if (entries.Count == 0)
            {
                return Task.CompletedTask;
            }

            var digitCount = Math.Max(2, (entries.Count - 1).ToString().Length);
            var plannedMoves = entries
                .Select((entry, index) =>
                {
                    var folderPath = Path.GetDirectoryName(entry.Path)!;
                    var baseName = $"BG{index.ToString().PadLeft(digitCount, '0')}";
                    var fileName = string.IsNullOrWhiteSpace(entry.Remark)
                        ? $"{baseName}.png"
                        : $"{baseName}_{entry.Remark}.png";
                    return new BackgroundImageRename(entry, Path.Combine(folderPath, fileName));
                })
                .ToList();

            if (plannedMoves.All(move => PathsExactlyEqual(move.Entry.Path, move.TargetPath)))
            {
                return Task.CompletedTask;
            }

            var tempMoves = plannedMoves
                .Select(move =>
                {
                    var tempPath = Path.Combine(Path.GetDirectoryName(move.Entry.Path)!, $"__bg_rename_{Guid.NewGuid():N}.png");
                    File.Move(move.Entry.Path, tempPath, overwrite: true);
                    return move with { Entry = move.Entry with { Path = tempPath } };
                })
                .ToList();

            foreach (var move in tempMoves)
            {
                File.Move(move.Entry.Path, move.TargetPath, overwrite: true);
            }

            return Task.CompletedTask;
        }

        private static async Task ConvertImageToPngAsync(string sourcePath, string targetPngPath)
        {
            var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
            using var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(sourceStream);
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            File.Create(targetPngPath).Dispose();
            var targetFile = await StorageFile.GetFileFromPathAsync(targetPngPath);
            using var targetStream = await targetFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, targetStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                decoder.PixelWidth,
                decoder.PixelHeight,
                decoder.DpiX,
                decoder.DpiY,
                pixelData.DetachPixelData());
            await encoder.FlushAsync();
        }

        private static List<string> GetBackgroundImagePaths(string backgroundFolderPath)
        {
            return Directory
                .EnumerateFiles(backgroundFolderPath, "*.png")
                .OrderBy(Path.GetFileName)
                .ToList();
        }

        private static BackgroundImageEntry ParseBackgroundImageFileName(string imagePath)
        {
            var name = Path.GetFileNameWithoutExtension(imagePath);
            var match = Regex.Match(name, @"^BG\d+(?:_(?<remark>.+))?$", RegexOptions.IgnoreCase);
            return new BackgroundImageEntry(
                imagePath,
                match.Success ? match.Groups["remark"].Value : string.Empty);
        }

        private static string SanitizeRemark(string remark)
        {
            var invalidChars = Path.GetInvalidFileNameChars().Concat(['_']).ToHashSet();
            return new string(remark.Trim().Where(ch => !invalidChars.Contains(ch)).ToArray());
        }

        private static string GetBackgroundFolderPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(assetLibrary.Path, BackgroundFolderName);
        }

        private static string GetMusicFolderPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(assetLibrary.Path, MusicFolderName);
        }

        private static string GetAmbientSoundFolderPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(assetLibrary.Path, AmbientSoundFolderName);
        }

        private static string GetSoundEffectFolderPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(assetLibrary.Path, SoundEffectFolderName);
        }

        private static string GetFunctionFolderPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(assetLibrary.Path, FunctionFolderName);
        }

        private static string GetFunctionIndexPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(GetFunctionFolderPath(assetLibrary), FunctionIndexFileName);
        }

        private static string GetCharacterFilterFolderPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(assetLibrary.Path, CharacterFilterFolderName);
        }

        private static string GetCharacterFilterIndexPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(GetCharacterFilterFolderPath(assetLibrary), CharacterFilterIndexFileName);
        }

        private static string GetAudioFolderPath(AssetLibraryInfo assetLibrary, AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => GetMusicFolderPath(assetLibrary),
                AudioAssetKind.Ambient => GetAmbientSoundFolderPath(assetLibrary),
                AudioAssetKind.SoundEffect => GetSoundEffectFolderPath(assetLibrary),
                _ => GetMusicFolderPath(assetLibrary)
            };
        }

        private static string GetCharacterFolderPath(AssetLibraryInfo assetLibrary)
        {
            return Path.Combine(assetLibrary.Path, CharacterFolderName);
        }

        private static void EnsureCharacterSubfolders(string characterPath)
        {
            Directory.CreateDirectory(Path.Combine(characterPath, "DN_Cloth"));
            Directory.CreateDirectory(Path.Combine(characterPath, "FC_Face"));
            Directory.CreateDirectory(Path.Combine(characterPath, "AD_Adorn"));
            Directory.CreateDirectory(Path.Combine(characterPath, "VFX"));
        }

        private static CharacterInfo ReadCharacterInfo(string characterPath)
        {
            var metaPath = Path.Combine(characterPath, "character.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<CharacterMeta>(File.ReadAllText(metaPath));
                    return new CharacterInfo(
                        string.IsNullOrWhiteSpace(meta?.Name) ? Path.GetFileName(characterPath) : meta.Name!,
                        string.IsNullOrWhiteSpace(meta?.Code) ? Path.GetFileName(characterPath) : meta.Code!,
                        string.IsNullOrWhiteSpace(meta?.ColorHex) ? "#D9E8FF" : meta.ColorHex!,
                        characterPath);
                }
                catch
                {
                    // Fall through to folder-derived defaults.
                }
            }

            var fallbackCode = Path.GetFileName(characterPath);
            return new CharacterInfo(fallbackCode, fallbackCode, "#D9E8FF", characterPath);
        }

        private void WriteCharacterMeta(string characterPath, CharacterEditorInput input)
        {
            var meta = new CharacterMeta
            {
                Name = input.Name,
                Code = input.Code,
                ColorHex = input.ColorHex
            };
            File.WriteAllText(Path.Combine(characterPath, "character.json"), JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private static string SanitizeCharacterFolderName(string code)
        {
            var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
            var sanitized = new string(code.Trim().Where(ch => !invalidChars.Contains(ch)).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? $"Character_{Guid.NewGuid():N}" : sanitized;
        }

        private static string NormalizeColorHex(string value)
        {
            var trimmed = value.Trim();
            if (Regex.IsMatch(trimmed, "^#?[0-9a-fA-F]{6}$"))
            {
                return trimmed.StartsWith('#') ? trimmed.ToUpperInvariant() : $"#{trimmed.ToUpperInvariant()}";
            }

            return "#D9E8FF";
        }

        private static Windows.UI.Color ParseColor(string hex, Windows.UI.Color fallback)
        {
            var normalized = NormalizeColorHex(hex);
            try
            {
                return Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(normalized.Substring(1, 2), 16),
                    Convert.ToByte(normalized.Substring(3, 2), 16),
                    Convert.ToByte(normalized.Substring(5, 2), 16));
            }
            catch
            {
                return fallback;
            }
        }

        private async void ChooseProjectRootButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFolder = await picker.PickSingleFolderAsync();
            if (selectedFolder is null)
            {
                return;
            }

            var newProjectRootPath = Path.GetFullPath(Path.Combine(selectedFolder.Path, ProjectRootFolderName));
            var oldProjectRootPath = Path.GetFullPath(_projectRootPath);
            AppendLog(LogKind.User, $"选择新的整体项目父目录：{selectedFolder.Path}");

            if (PathsEqual(oldProjectRootPath, newProjectRootPath))
            {
                SetProjectRootStatus(InfoBarSeverity.Informational, "目录未变化", $"当前已经在使用：{newProjectRootPath}");
                return;
            }

            if (IsPathInsideDirectory(newProjectRootPath, oldProjectRootPath))
            {
                SetProjectRootStatus(InfoBarSeverity.Error, "无法迁移目录", "新位置不能放在旧项目总目录里面，否则迁移完成后删除旧目录时会连新目录一起删除。");
                return;
            }

            try
            {
                AppendLog(LogKind.Info, $"开始迁移整体项目目录：{oldProjectRootPath} -> {newProjectRootPath}");
                var result = MigrateProjectRoot(oldProjectRootPath, newProjectRootPath);

                _projectRootPath = newProjectRootPath;
                _appSettings.ProjectRootPath = _projectRootPath;
                SaveAppSettings();
                EnsureProjectRootDirectory(_projectRootPath);
                SetProjectRootStatus(InfoBarSeverity.Success, "目录迁移完成", $"已迁移并校验 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹。旧目录已删除：{oldProjectRootPath}");
            }
            catch (Exception ex)
            {
                EnsureProjectRootDirectory(_projectRootPath);
                AppendLog(LogKind.Error, "整体项目目录迁移失败。", ex);
                SetProjectRootStatus(InfoBarSeverity.Error, "目录迁移失败", $"已保留原目录和设置，未删除旧目录。错误：{ex.Message}");
            }
        }

        private void SetProjectRootStatus(InfoBarSeverity severity, string title, string message)
        {
            ProjectRootStatusInfoBar.Severity = severity;
            ProjectRootStatusInfoBar.Title = title;
            ProjectRootStatusInfoBar.Message = message;
            ProjectRootStatusInfoBar.IsOpen = true;

            var logKind = severity == InfoBarSeverity.Error
                ? LogKind.Error
                : severity == InfoBarSeverity.Warning
                    ? LogKind.Warning
                    : LogKind.Info;
            AppendLog(logKind, $"{title}：{message}");
        }

        private static MigrationResult MigrateProjectRoot(string oldProjectRootPath, string newProjectRootPath)
        {
            if (!Directory.Exists(oldProjectRootPath))
            {
                Directory.CreateDirectory(newProjectRootPath);
                return new MigrationResult(0, 0);
            }

            Directory.CreateDirectory(newProjectRootPath);

            var sourceDirectories = Directory.EnumerateDirectories(oldProjectRootPath, "*", SearchOption.AllDirectories).ToList();
            foreach (var sourceDirectory in sourceDirectories)
            {
                var targetDirectory = Path.Combine(newProjectRootPath, Path.GetRelativePath(oldProjectRootPath, sourceDirectory));
                Directory.CreateDirectory(targetDirectory);
            }

            var sourceFiles = Directory.EnumerateFiles(oldProjectRootPath, "*", SearchOption.AllDirectories).ToList();
            foreach (var sourceFile in sourceFiles)
            {
                var targetFile = Path.Combine(newProjectRootPath, Path.GetRelativePath(oldProjectRootPath, sourceFile));
                var targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(sourceFile, targetFile, overwrite: true);
            }

            VerifyMigratedFiles(oldProjectRootPath, newProjectRootPath, sourceFiles);
            Directory.Delete(oldProjectRootPath, recursive: true);

            return new MigrationResult(sourceFiles.Count, sourceDirectories.Count);
        }

        private static void VerifyMigratedFiles(string oldProjectRootPath, string newProjectRootPath, IReadOnlyCollection<string> sourceFiles)
        {
            var failures = new List<string>();

            foreach (var sourceFile in sourceFiles)
            {
                var relativePath = Path.GetRelativePath(oldProjectRootPath, sourceFile);
                var targetFile = Path.Combine(newProjectRootPath, relativePath);

                if (!File.Exists(targetFile))
                {
                    failures.Add($"{relativePath} 缺失");
                    continue;
                }

                var sourceInfo = new FileInfo(sourceFile);
                var targetInfo = new FileInfo(targetFile);
                if (sourceInfo.Length != targetInfo.Length)
                {
                    failures.Add($"{relativePath} 大小不一致");
                    continue;
                }

                if (!HashesEqual(sourceFile, targetFile))
                {
                    failures.Add($"{relativePath} 内容校验失败");
                }
            }

            if (failures.Count > 0)
            {
                throw new IOException($"迁移校验失败：{string.Join("；", failures.Take(5))}");
            }
        }

        private static bool HashesEqual(string leftPath, string rightPath)
        {
            using var hashAlgorithm = SHA256.Create();
            using var leftStream = File.OpenRead(leftPath);
            using var rightStream = File.OpenRead(rightPath);

            return hashAlgorithm.ComputeHash(leftStream).SequenceEqual(hashAlgorithm.ComputeHash(rightStream));
        }

        private static string ComputeFileHash(string path)
        {
            using var hashAlgorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(hashAlgorithm.ComputeHash(stream));
        }

        private static bool PathsEqual(string leftPath, string rightPath)
        {
            return string.Equals(
                TrimDirectorySeparator(Path.GetFullPath(leftPath)),
                TrimDirectorySeparator(Path.GetFullPath(rightPath)),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsExactlyEqual(string leftPath, string rightPath)
        {
            return string.Equals(
                TrimDirectorySeparator(Path.GetFullPath(leftPath)),
                TrimDirectorySeparator(Path.GetFullPath(rightPath)),
                StringComparison.Ordinal);
        }

        private static bool IsPathInsideDirectory(string path, string directoryPath)
        {
            var normalizedPath = TrimDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
            var normalizedDirectoryPath = TrimDirectorySeparator(Path.GetFullPath(directoryPath)) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimDirectorySeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static AppSettings LoadAppSettings()
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            try
            {
                var settingsJson = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(settingsJson) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        private void SaveAppSettings()
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(_appSettings, _jsonOptions));
        }

        private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            if (tag == "Settings")
            {
                ShowSettingsPage();
                return;
            }

            if (tag == "AssetLibrary")
            {
                ShowAssetLibraryPage();
                return;
            }

            if (tag == "UnrealSync")
            {
                ShowUnrealSyncPage();
                return;
            }

            ShowWorkbenchPage();
        }

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
            var binding = ReadProjectUnrealBinding(project);
            var footer = binding.IsComplete
                ? "已保存虚幻关联"
                : "未完整关联";
            if (isSelected)
            {
                footer = $"当前选择 · {footer}";
            }

            var item = CreateBaseCard(project);
            item.Tapped += UnrealSyncProjectCard_Tapped;
            item.Content = CreateCardContent(project.ThumbnailPath, project.Name, $"{project.Code} | {project.AssetLibraryName}", footer);
            if (isSelected)
            {
                item.BorderThickness = new Thickness(2);
                item.BorderBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush;
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
                : ReadProjectUnrealBinding(project);
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

        private static UnrealProjectBinding ReadProjectUnrealBinding(ProjectInfo project)
        {
            var metaPath = Path.Combine(project.Path, ToolsFolderName, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            return new UnrealProjectBinding(meta.UnrealEnginePath, meta.UnrealProjectPath, meta.UnrealContentFolderPath);
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

        private async void ChooseUnrealContentFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var selectedFolder = await picker.PickSingleFolderAsync();
            if (selectedFolder is null)
            {
                return;
            }

            UnrealContentFolderTextBox.Text = selectedFolder.Path;
            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        private void UnrealSyncProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUnrealSyncProjects)
            {
                return;
            }

            SaveUnrealSyncSettingsFromUi();
            RefreshUnrealSyncStatus();
        }

        private void CheckUnrealSyncButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshUnrealSyncStatus();
            AppendLog(LogKind.User, "已重新检测虚幻同步台关联状态。");
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
            SetUnrealSyncProgress(true, "正在检测虚幻同步差异... 5%", false, 5);
            UnrealSyncSummaryText.Text = forceFullSync
                ? "正在准备全量重新同步，将忽略本地缓存和 .uasset 时间戳。"
                : "正在比较工具箱源文件与虚幻项目内的 .uasset 时间戳。";
            var changePlan = await Task.Run(() => BuildUnrealSyncChangePlan(validation.Context, forceFullSync));
            ApplyUnrealSyncChangePlan(changePlan);
            if (!changePlan.HasChanges)
            {
                SetUnrealSyncProgress(false, string.Empty);
                SetUnrealSyncStatus(InfoBarSeverity.Informational, "无需同步", "没有检测到素材、CSV 或立绘数据资产变动，本次不会启动虚幻同步。");
                UnrealSyncSummaryText.Text = changePlan.Summary;
                RunUnrealSyncButton.IsEnabled = true;
                return;
            }

            SetUnrealSyncProgress(true, "差异检测完成，等待确认备份... 10%", false, 10);
            var runningUnrealChoice = await ShowRunningUnrealEditorDialogAsync();
            if (runningUnrealChoice is null)
            {
                SetUnrealSyncProgress(false, string.Empty);
                RunUnrealSyncButton.IsEnabled = true;
                return;
            }

            if (runningUnrealChoice == true)
            {
                SetUnrealSyncProgress(true, "正在尝试关闭已打开的 Unreal Editor... 12%", false, 12);
                await Task.Run(CloseRunningUnrealEditors);
                await Task.Delay(1200);
            }

            var backupChoice = await ShowUnrealBackupDialogAsync(validation.Context);
            if (backupChoice is null)
            {
                SetUnrealSyncProgress(false, string.Empty);
                RunUnrealSyncButton.IsEnabled = true;
                return;
            }

            SetUnrealSyncStatus(InfoBarSeverity.Informational, "正在同步", "正在生成导入清单并调用 Unreal Editor，请等待引擎完成资源导入。");
            try
            {
                string? backupPath = null;
                if (backupChoice == true)
                {
                    SetUnrealSyncProgress(true, "正在备份虚幻项目... 20%", false, 20);
                    backupPath = await Task.Run(() => CreateCleanUnrealProjectBackup(validation.Context));
                    SetUnrealSyncProgress(true, "虚幻项目备份完成... 30%", false, 30);
                    AppendLog(LogKind.User, $"虚幻项目备份完成：{backupPath}");
                }

                var progress = new Progress<UnrealSyncProgressUpdate>(update =>
                    SetUnrealSyncProgress(true, $"{update.Message} {update.Percent:0}%", false, update.Percent));
                var result = await Task.Run(() => RunUnrealSync(validation.Context, changePlan, progress));
                SetUnrealSyncStatus(
                    result.ExitCode == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    result.ExitCode == 0 ? "同步完成" : "同步命令已结束",
                    $"退出码 {result.ExitCode}。脚本：{result.ScriptPath}");
                var backupSummary = backupPath is null ? string.Empty : $"备份：{backupPath}\n";
                UnrealSyncSummaryText.Text = result.Output.Length == 0
                    ? $"{backupSummary}{changePlan.Summary}\n已写入导入清单：{result.ManifestPath}"
                    : $"{backupSummary}{changePlan.Summary}\n已写入导入清单：{result.ManifestPath}\n{TrimLongText(result.Output, 900)}";
                var lustrationConfirmed = !changePlan.LustrationChanged ||
                    result.Output.Contains("GalExcleTools updated lustration data asset", StringComparison.OrdinalIgnoreCase);
                var storyTableFailed =
                    result.Output.Contains("GalExcleTools failed to update story data table", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools could not create or load story data table", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools story row struct missing", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools failed to load story row struct", StringComparison.OrdinalIgnoreCase);
                var assetIndexTableFailed =
                    result.Output.Contains("GalExcleTools failed to update asset index data table", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools could not create or load asset index data table", StringComparison.OrdinalIgnoreCase);
                if (result.ExitCode == 0 && lustrationConfirmed && !assetIndexTableFailed)
                {
                    WriteUnrealSyncState(validation.Context, changePlan);
                    SetUnrealSyncProgress(true, "同步完成 100%", false, 100);
                    if (storyTableFailed)
                    {
                        SetUnrealSyncStatus(InfoBarSeverity.Warning, "同步完成，但剧情表未确认", "Unreal 日志提示 StoryStruct 或 DataTable 写入失败，请查看下方日志输出。");
                    }
                }
                else if (result.ExitCode == 0 && assetIndexTableFailed)
                {
                    SetUnrealSyncStatus(InfoBarSeverity.Warning, "同步结束，但素材索引表未确认", "Unreal 日志提示素材索引 DataTable 写入失败，请检查 FTexture2DTable / FWaveTable 结构体路径和表格资产。");
                }
                else if (result.ExitCode == 0 && changePlan.LustrationChanged)
                {
                    SetUnrealSyncStatus(InfoBarSeverity.Warning, "同步结束，但立绘数据未确认", "Unreal 日志没有返回立绘数据资产写入确认。本次不会缓存为已同步，请关闭虚幻编辑器后再同步一次。");
                }

                AppendLog(result.ExitCode == 0 ? LogKind.User : LogKind.Warning, $"虚幻同步结束，退出码：{result.ExitCode}");
                await ShowUnrealSyncFinishedDialogAsync(validation.Context, result, changePlan);
            }
            catch (Exception ex)
            {
                SetUnrealSyncStatus(InfoBarSeverity.Error, "同步失败", ex.Message);
                AppendLog(LogKind.Error, "虚幻同步失败。", ex);
            }
            finally
            {
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
            var contentFolderPath = UnrealContentFolderTextBox.Text.Trim();
            var selectedProject = GetSelectedUnrealSyncProject();
            _appSettings.UnrealToolProjectFolderName = selectedProject?.FolderName ?? (UnrealSyncProjectComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            _appSettings.UnrealEnginePath = enginePath;
            _appSettings.UnrealProjectPath = unrealProjectPath;
            _appSettings.UnrealContentFolderPath = contentFolderPath;
            SaveAppSettings();

            if (selectedProject is not null)
            {
                SaveProjectUnrealBinding(selectedProject, enginePath, unrealProjectPath, contentFolderPath);
                LoadUnrealSyncProjectOptions();
            }
        }

        private void SaveProjectUnrealBinding(ProjectInfo project, string enginePath, string unrealProjectPath, string contentFolderPath)
        {
            var toolsPath = Path.Combine(project.Path, ToolsFolderName);
            Directory.CreateDirectory(toolsPath);
            var metaPath = Path.Combine(toolsPath, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            meta.ProjectName = string.IsNullOrWhiteSpace(meta.ProjectName) ? project.Name : meta.ProjectName;
            meta.ProjectCode = string.IsNullOrWhiteSpace(meta.ProjectCode) ? project.Code : meta.ProjectCode;
            meta.AssetLibraryName = string.IsNullOrWhiteSpace(meta.AssetLibraryName) ? project.AssetLibraryName : meta.AssetLibraryName;
            meta.AssetLibraryFolderName = string.IsNullOrWhiteSpace(meta.AssetLibraryFolderName) ? project.AssetLibraryFolderName : meta.AssetLibraryFolderName;
            meta.UnrealEnginePath = enginePath;
            meta.UnrealProjectPath = unrealProjectPath;
            meta.UnrealContentFolderPath = contentFolderPath;
            meta.LastEditedAt = DateTime.Now;
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _jsonOptions));
        }

        private void RefreshUnrealSyncStatus()
        {
            var validation = ValidateUnrealSync(useUiValues: true);
            ApplyUnrealSyncValidation(validation);
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
            UnrealSyncProgressPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            UnrealSyncProgressBar.IsIndeterminate = isVisible && isIndeterminate;
            if (!isIndeterminate)
            {
                UnrealSyncProgressBar.Value = value;
            }

            UnrealSyncProgressText.Text = message;
        }

        private void ApplyUnrealSyncChangePlan(UnrealSyncChangePlan changePlan)
        {
            UnrealSyncPlanItemsControl.Items.Clear();
            foreach (var item in changePlan.PlanItems)
            {
                UnrealSyncPlanItemsControl.Items.Add(new TextBlock
                {
                    Text = $"• {item}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                });
            }

            UnrealSyncSummaryText.Text = changePlan.Summary;
        }

        private async Task<bool?> ShowUnrealBackupDialogAsync(UnrealSyncContext context)
        {
            var backupFolder = GetUnrealBackupFolder(context);
            var dialog = new ContentDialog
            {
                Title = "同步前备份虚幻项目？",
                Content = $"建议在写入虚幻项目前先生成一个不带缓存的干净压缩包。\n默认位置：{backupFolder}",
                PrimaryButtonText = "备份并同步",
                SecondaryButtonText = "直接同步",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => true,
                ContentDialogResult.Secondary => false,
                _ => null
            };
        }

        private async Task<bool?> ShowRunningUnrealEditorDialogAsync()
        {
            var runningEditors = GetRunningUnrealEditorProcesses();
            if (runningEditors.Count == 0)
            {
                return false;
            }

            var processText = string.Join("\n", runningEditors.Select(process => $"{process.ProcessName}  PID {process.Id}"));
            var dialog = new ContentDialog
            {
                Title = "检测到 Unreal Editor 正在运行",
                Content = $"建议先关闭当前打开的虚幻编辑器，再执行同步。否则编辑器中已加载的资产可能不会刷新，后续保存还可能覆盖同步结果。\n\n{processText}",
                PrimaryButtonText = "关闭后同步",
                SecondaryButtonText = "继续同步",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => true,
                ContentDialogResult.Secondary => false,
                _ => null
            };
        }

        private async Task ShowUnrealSyncFinishedDialogAsync(
            UnrealSyncContext context,
            UnrealSyncResult result,
            UnrealSyncChangePlan changePlan)
        {
            var dialog = new ContentDialog
            {
                Title = result.ExitCode == 0 ? "虚幻同步完成" : "虚幻同步已结束",
                Content = result.ExitCode == 0
                    ? $"本次同步处理 {changePlan.TotalChangedItems} 项。现在可以直接打开虚幻项目检查结果。"
                    : $"Unreal Editor 返回退出码 {result.ExitCode}。可以打开项目或查看日志继续确认。",
                PrimaryButtonText = "打开虚幻项目",
                SecondaryButtonText = "打开日志目录",
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult == ContentDialogResult.Primary)
            {
                OpenUnrealProject(context);
            }
            else if (dialogResult == ContentDialogResult.Secondary)
            {
                OpenUnrealLogFolder(context);
            }
        }

        private static void OpenUnrealProject(UnrealSyncContext context)
        {
            var editorPath = ResolveUnrealInteractiveEditorExecutable(context.EditorPath);
            var processStartInfo = new ProcessStartInfo
            {
                FileName = editorPath,
                UseShellExecute = false
            };
            processStartInfo.ArgumentList.Add(context.UnrealProjectPath);
            Process.Start(processStartInfo);
        }

        private static void OpenUnrealLogFolder(UnrealSyncContext context)
        {
            var logsFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "Logs");
            Directory.CreateDirectory(logsFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = logsFolder,
                UseShellExecute = true
            });
        }

        private static string ResolveUnrealInteractiveEditorExecutable(string editorPath)
        {
            if (Path.GetFileName(editorPath).Equals("UnrealEditor-Cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                var interactivePath = Path.Combine(Path.GetDirectoryName(editorPath)!, "UnrealEditor.exe");
                return File.Exists(interactivePath) ? interactivePath : editorPath;
            }

            return editorPath;
        }

        private static List<Process> GetRunningUnrealEditorProcesses()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "UnrealEditor",
                "UnrealEditor-Cmd"
            };

            return Process.GetProcesses()
                .Where(process =>
                {
                    try
                    {
                        return names.Contains(process.ProcessName);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
        }

        private static void CloseRunningUnrealEditors()
        {
            foreach (var process in GetRunningUnrealEditorProcesses())
            {
                try
                {
                    if (!process.CloseMainWindow())
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    else if (!process.WaitForExit(8000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // The editor may have already closed or denied access; continue with the remaining processes.
                }
            }
        }

        private static string GetUnrealBackupFolder(UnrealSyncContext context)
        {
            return Path.Combine(context.Project.Path, ToolsFolderName, UnrealBackupsFolderName);
        }

        private static string CreateCleanUnrealProjectBackup(UnrealSyncContext context)
        {
            var unrealProjectRoot = Path.GetDirectoryName(context.UnrealProjectPath)
                ?? throw new InvalidOperationException("无法定位虚幻项目根目录。");
            var backupFolder = GetUnrealBackupFolder(context);
            Directory.CreateDirectory(backupFolder);

            var projectName = Path.GetFileNameWithoutExtension(context.UnrealProjectPath);
            var backupPath = Path.Combine(backupFolder, $"{projectName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".vs",
                "Binaries",
                "DerivedDataCache",
                "Intermediate",
                "Saved"
            };

            using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
            foreach (var filePath in Directory.EnumerateFiles(unrealProjectRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(unrealProjectRoot, filePath);
                var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(excludedFolders.Contains))
                {
                    continue;
                }

                archive.CreateEntryFromFile(filePath, relativePath.Replace('\\', '/'), CompressionLevel.Optimal);
            }

            return backupPath;
        }

        private static string GetUnrealSyncStatePath(UnrealSyncContext context)
        {
            return Path.Combine(context.Project.Path, ToolsFolderName, "unreal-sync-state.json");
        }

        private UnrealSyncState ReadUnrealSyncState(UnrealSyncContext context)
        {
            var path = GetUnrealSyncStatePath(context);
            if (!File.Exists(path))
            {
                return new UnrealSyncState();
            }

            try
            {
                return JsonSerializer.Deserialize<UnrealSyncState>(File.ReadAllText(path)) ?? new UnrealSyncState();
            }
            catch
            {
                return new UnrealSyncState();
            }
        }

        private void WriteUnrealSyncState(UnrealSyncContext context, UnrealSyncChangePlan changePlan)
        {
            var statePath = GetUnrealSyncStatePath(context);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            var state = ReadUnrealSyncState(context);
            state.LastSyncedAt = DateTimeOffset.Now;
            state.LustrationHash = changePlan.LustrationHash;
            state.AssetIndexTablesHash = changePlan.AssetIndexTablesHash;
            File.WriteAllText(statePath, JsonSerializer.Serialize(state, _jsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string ComputeSha256Hex(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        private UnrealSyncValidation ValidateUnrealSync(bool useUiValues)
        {
            var enginePath = useUiValues ? UnrealEnginePathTextBox.Text.Trim() : _appSettings.UnrealEnginePath ?? string.Empty;
            var unrealProjectPath = useUiValues ? UnrealProjectPathTextBox.Text.Trim() : _appSettings.UnrealProjectPath ?? string.Empty;
            var contentFolderPath = useUiValues ? UnrealContentFolderTextBox.Text.Trim() : _appSettings.UnrealContentFolderPath ?? string.Empty;
            var projectFolderName = useUiValues
                ? (UnrealSyncProjectComboBox.SelectedItem as ComboBoxItem)?.Tag as string
                : _appSettings.UnrealToolProjectFolderName;

            var planItems = new List<string>();
            var warnings = new List<string>();
            var errors = new List<string>();

            var editorPath = ResolveUnrealEditorExecutable(enginePath);
            if (editorPath is null)
            {
                errors.Add("请选择有效的 UnrealEditor.exe 或 UnrealEditor-Cmd.exe。");
            }
            else
            {
                planItems.Add($"引擎执行器：{editorPath}");
            }

            if (string.IsNullOrWhiteSpace(unrealProjectPath) || !File.Exists(unrealProjectPath) || !unrealProjectPath.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("请选择有效的 .uproject 文件。");
            }

            var project = GetProjects()
                .FirstOrDefault(item => string.Equals(item.FolderName, projectFolderName, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                errors.Add("请选择有效的工作台项目。");
            }

            AssetLibraryInfo? assetLibrary = null;
            if (project is not null)
            {
                assetLibrary = ResolveProjectAssetLibrary(project);
                if (assetLibrary is null)
                {
                    errors.Add($"工作台项目 {project.Name} 没有关联可用素材库。");
                }
                else
                {
                    planItems.Add($"工作台项目：{project.Name} / {project.Code}，素材库：{assetLibrary.Name}");
                }
            }

            string? contentRootPath = null;
            string? targetAssetRoot = null;
            if (!string.IsNullOrWhiteSpace(unrealProjectPath) && File.Exists(unrealProjectPath))
            {
                var unrealProjectRoot = Path.GetDirectoryName(unrealProjectPath)!;
                contentRootPath = Path.Combine(unrealProjectRoot, "Content");
                if (!Directory.Exists(contentRootPath))
                {
                    errors.Add("虚幻项目缺少 Content 文件夹。");
                }

            }

            if (string.IsNullOrWhiteSpace(contentFolderPath) || !Directory.Exists(contentFolderPath))
            {
                errors.Add("请选择 Content 下的目标同步文件夹。");
            }
            else if (contentRootPath is not null && Directory.Exists(contentRootPath))
            {
                if (!IsPathInsideDirectoryOrEqual(contentFolderPath, contentRootPath))
                {
                    errors.Add("目标同步文件夹必须位于所选虚幻项目的 Content 目录内。");
                }
                else if (!string.Equals(new DirectoryInfo(contentFolderPath).Name, "Narrative", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("目标同步文件夹必须命名为 Narrative，避免素材同步到错误目录。");
                }
                else
                {
                    targetAssetRoot = ToUnrealAssetPath(contentRootPath, contentFolderPath);
                    planItems.Add($"目标内容路径：{targetAssetRoot}");
                    var missingFolders = ExpectedUnrealNarrativeFolders
                        .Where(folder => !Directory.Exists(Path.Combine(contentFolderPath, folder)))
                        .ToList();
                    if (missingFolders.Count > 0)
                    {
                        warnings.Add($"目标目录缺少参考结构：{string.Join(", ", missingFolders)}。同步时会在引擎导入阶段尝试创建。");
                    }
                }
            }

            if (contentFolderPath.Length > 0 && Directory.Exists(contentFolderPath) && assetLibrary is not null && project is not null)
            {
                planItems.AddRange(CreateUnrealSyncPlanItems(project, assetLibrary, contentFolderPath));
            }

            var canSync = errors.Count == 0 && editorPath is not null && project is not null && assetLibrary is not null && targetAssetRoot is not null;
            var severity = errors.Count > 0
                ? InfoBarSeverity.Error
                : warnings.Count > 0
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success;
            var title = errors.Count > 0
                ? "关联不完整"
                : warnings.Count > 0
                    ? "可以同步，但需要确认"
                    : "关联一致，可以同步";
            var message = errors.Count > 0
                ? string.Join(" ", errors)
                : warnings.Count > 0
                    ? string.Join(" ", warnings)
                    : "工具箱项目、素材库和虚幻目标目录已通过基础验证。";
            var summary = project is null || assetLibrary is null
                ? "等待选择工作台项目和素材库。"
                : CreateUnrealSyncSummary(project, assetLibrary);

            var context = canSync
                ? new UnrealSyncContext(editorPath!, unrealProjectPath, contentFolderPath, targetAssetRoot!, project!, assetLibrary!)
                : null;
            return new UnrealSyncValidation(severity, title, message, summary, canSync, context, planItems);
        }

        private List<string> CreateUnrealSyncPlanItems(ProjectInfo project, AssetLibraryInfo assetLibrary, string contentFolderPath)
        {
            var backgroundCount = GetBackgroundImagePaths(GetBackgroundFolderPath(assetLibrary)).Count;
            var musicCount = GetAudioFilePaths(GetMusicFolderPath(assetLibrary)).Count;
            var sceneCount = GetAudioFilePaths(GetAmbientSoundFolderPath(assetLibrary)).Count;
            var soundEffectCount = GetAudioFilePaths(GetSoundEffectFolderPath(assetLibrary)).Count;
            var csvCount = GetProjectStoryCsvPaths(project).Count;
            var characterLayerCount = GetProjectCharacterLayerImportPaths(assetLibrary).Count;
            var lustrationRowCount = GetCharactersForAssetLibrary(assetLibrary).Count;
            var existingBackgroundCount = CountUnrealAssets(Path.Combine(contentFolderPath, "BackGround"));
            var existingMusicCount = CountUnrealAssets(Path.Combine(contentFolderPath, "BGM"));
            var existingSceneCount = CountUnrealAssets(Path.Combine(contentFolderPath, "Scene_Effect"));

            return
            [
                $"背景图：源文件 {backgroundCount} 个，引擎内已有 {existingBackgroundCount} 个 .uasset。",
                $"音乐：源文件 {musicCount} 个，引擎内已有 {existingMusicCount} 个 .uasset。",
                $"环境音/特殊音效：源文件 {sceneCount + soundEffectCount} 个，引擎内已有 {existingSceneCount} 个 .uasset。",
                $"素材索引表：背景 {backgroundCount}、BGM {musicCount}、环境音 {sceneCount}、特殊音效 {soundEffectCount}，将同步到 ExcelTexts 的 4 张 DataTable。",
                $"CSV 表格：{csvCount} 个，将导入到 ExcelTexts。",
                $"立绘图层：{characterLayerCount} 个，将按 Lustration/角色/图层目录导入。",
                $"立绘信息表：{lustrationRowCount} 个角色，将同步到 Lustration/DA_LustrationInfor。"
            ];
        }

        private static string CreateUnrealSyncSummary(ProjectInfo project, AssetLibraryInfo assetLibrary)
        {
            return $"当前关联：工作台项目 {project.Name} / {project.Code}，素材库 {assetLibrary.Name}。同步不会自动触发，必须点击“确认同步”。";
        }

        private UnrealSyncResult RunUnrealSync(
            UnrealSyncContext context,
            UnrealSyncChangePlan changePlan,
            IProgress<UnrealSyncProgressUpdate>? progress = null)
        {
            var savedFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "GalExcleTools");
            Directory.CreateDirectory(savedFolder);
            var manifestPath = Path.Combine(savedFolder, "gal-sync-manifest.json");
            var scriptPath = Path.Combine(savedFolder, "gal_sync_import.py");

            progress?.Report(new UnrealSyncProgressUpdate("正在写入同步清单...", 40));
            WriteUnrealSyncManifest(context, changePlan, manifestPath);
            progress?.Report(new UnrealSyncProgressUpdate("正在写入 Unreal Python 脚本...", 48));
            WriteUnrealSyncPythonScript(scriptPath, manifestPath);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = context.EditorPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add(context.UnrealProjectPath);
            processStartInfo.ArgumentList.Add($"-ExecutePythonScript={scriptPath}");
            processStartInfo.ArgumentList.Add("-unattended");
            processStartInfo.ArgumentList.Add("-nop4");
            processStartInfo.ArgumentList.Add("-nosplash");

            progress?.Report(new UnrealSyncProgressUpdate("正在启动 Unreal Editor 命令进程...", 55));
            using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("无法启动 Unreal Editor。");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            progress?.Report(new UnrealSyncProgressUpdate("Unreal 正在导入变动资源并保存资产...", 70));
            if (!process.WaitForExit(30 * 60 * 1000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Unreal Editor 同步超过 30 分钟，已终止进程。");
            }

            progress?.Report(new UnrealSyncProgressUpdate("正在收集 Unreal 同步结果...", 95));
            var output = outputTask.Result + errorTask.Result + ReadLatestUnrealSyncLogSnippet(context);
            return new UnrealSyncResult(process.ExitCode, manifestPath, scriptPath, output);
        }

        private static string ReadLatestUnrealSyncLogSnippet(UnrealSyncContext context)
        {
            try
            {
                var logsFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "Logs");
                if (!Directory.Exists(logsFolder))
                {
                    return string.Empty;
                }

                var latestLogPath = Directory
                    .EnumerateFiles(logsFolder, "*.log", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (latestLogPath is null)
                {
                    return string.Empty;
                }

                var lines = File
                    .ReadLines(latestLogPath, Encoding.UTF8)
                    .Where(line =>
                        line.Contains("GalExcleTools", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("LogCSVImportFactory", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(200)
                    .ToList();
                return lines.Count == 0
                    ? string.Empty
                    : $"\n[Unreal 日志确认：{latestLogPath}]\n{string.Join('\n', lines)}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private void WriteUnrealSyncManifest(UnrealSyncContext context, UnrealSyncChangePlan changePlan, string manifestPath)
        {
            var filters = File.Exists(GetCharacterFilterIndexPath(context.AssetLibrary))
                ? ReadCharacterFilters(context.AssetLibrary)
                : [];
            var manifest = new
            {
                GeneratedAt = DateTimeOffset.Now,
                ToolProject = new
                {
                    context.Project.Name,
                    context.Project.Code,
                    context.Project.Path
                },
                UnrealProjectPath = context.UnrealProjectPath,
                TargetRoot = context.TargetAssetRoot,
                LustrationInfo = new
                {
                    DataAsset = $"{context.TargetAssetRoot}/Lustration/DA_LustrationInfor.DA_LustrationInfor",
                    MapProperty = "Infor",
                    ShouldUpdate = changePlan.LustrationChanged,
                    Rows = changePlan.LustrationChanged ? changePlan.LustrationRows : []
                },
                StoryTables = changePlan.StoryTables,
                AssetIndexTables = changePlan.AssetIndexTables,
                Imports = changePlan.ImportGroups,
                Filters = filters
            };

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private List<UnrealStoryTableSyncEntry> BuildStoryTableSyncEntries(UnrealSyncContext context)
        {
            var chaptersFolderPath = GetChaptersFolderPath(context.Project);
            if (!Directory.Exists(chaptersFolderPath))
            {
                return [];
            }

            var result = new List<UnrealStoryTableSyncEntry>();
            foreach (var chapter in Directory.EnumerateDirectories(chaptersFolderPath).Select(ReadChapterInfo))
            {
                var chapterCsvs = GetChapterStoryCsvPathsForUnrealSync(context.Project, chapter);
                foreach (var entry in chapterCsvs)
                {
                    var assetName = entry.AssetName;
                    var tableFolder = BuildUnrealStoryTableFolder(context, chapter, entry.IsSectionCsv);
                    result.Add(new UnrealStoryTableSyncEntry(
                        entry.CsvPath,
                        $"{tableFolder}/{assetName}.{assetName}",
                        "/Script/GALLibrary.StoryStruct",
                        BuildLegacyUnrealStoryTableAssets(context, chapter, entry.CsvPath, tableFolder, assetName)));
                }
            }

            return result
                .OrderBy(entry => entry.TableAsset, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<UnrealAssetIndexTableSyncEntry> BuildUnrealAssetIndexTableSyncEntries(UnrealSyncContext context)
        {
            var cacheFolder = Path.Combine(context.Project.Path, ToolsFolderName, UnrealAssetIndexTablesFolderName);
            Directory.CreateDirectory(cacheFolder);

            return
            [
                CreateUnrealAssetIndexTableSyncEntry(
                    cacheFolder,
                    "BGIndexMap",
                    $"{context.TargetAssetRoot}/ExcelTexts/BGIndexMap.BGIndexMap",
                    "/Script/GALLibrary.Texture2DTable",
                    "Texture2D",
                    GetBackgroundImagePaths(GetBackgroundFolderPath(context.AssetLibrary))
                        .Select(path => BuildUnrealTextureReference($"{context.TargetAssetRoot}/BackGround", path))
                        .ToList()),
                CreateUnrealAssetIndexTableSyncEntry(
                    cacheFolder,
                    "BGMap",
                    $"{context.TargetAssetRoot}/ExcelTexts/BGMap.BGMap",
                    "/Script/GALLibrary.WaveTable",
                    "Wave",
                    GetAudioFilePaths(GetMusicFolderPath(context.AssetLibrary))
                        .Select(path => BuildUnrealSoundWaveReference($"{context.TargetAssetRoot}/BGM", path))
                        .ToList()),
                CreateUnrealAssetIndexTableSyncEntry(
                    cacheFolder,
                    "SceneIndexMap",
                    $"{context.TargetAssetRoot}/ExcelTexts/SceneIndexMap.SceneIndexMap",
                    "/Script/GALLibrary.WaveTable",
                    "Wave",
                    GetAudioFilePaths(GetAmbientSoundFolderPath(context.AssetLibrary))
                        .Select(path => BuildUnrealSoundWaveReference($"{context.TargetAssetRoot}/Scene_Effect", path))
                        .ToList()),
                CreateUnrealAssetIndexTableSyncEntry(
                    cacheFolder,
                    "ExsIndexMap",
                    $"{context.TargetAssetRoot}/ExcelTexts/ExsIndexMap.ExsIndexMap",
                    "/Script/GALLibrary.WaveTable",
                    "Wave",
                    GetAudioFilePaths(GetSoundEffectFolderPath(context.AssetLibrary))
                        .Select(path => BuildUnrealSoundWaveReference($"{context.TargetAssetRoot}/Scene_Effect", path))
                        .ToList())
            ];
        }

        private static UnrealAssetIndexTableSyncEntry CreateUnrealAssetIndexTableSyncEntry(
            string cacheFolder,
            string tableName,
            string tableAsset,
            string rowStruct,
            string valueColumnName,
            IReadOnlyList<string> assetReferences)
        {
            var csvPath = Path.Combine(cacheFolder, $"{tableName}.csv");
            WriteUnrealAssetIndexTableCsv(csvPath, valueColumnName, assetReferences);
            var hashSource = $"{tableAsset}|{rowStruct}|{valueColumnName}|{string.Join('\n', assetReferences)}";
            return new UnrealAssetIndexTableSyncEntry(csvPath, tableAsset, rowStruct, ComputeSha256Hex(hashSource));
        }

        private static void WriteUnrealAssetIndexTableCsv(string csvPath, string valueColumnName, IReadOnlyList<string> assetReferences)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", [EscapeCsvField("---"), EscapeCsvField(valueColumnName)]));
            for (var i = 0; i < assetReferences.Count; i++)
            {
                builder.AppendLine(string.Join(",",
                [
                    EscapeCsvField(i.ToString(CultureInfo.InvariantCulture)),
                    EscapeCsvField(assetReferences[i])
                ]));
            }

            File.WriteAllText(csvPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        private static string ComputeUnrealAssetIndexTablesHash(IReadOnlyList<UnrealAssetIndexTableSyncEntry> entries)
        {
            return ComputeSha256Hex("asset-index-tables-v1|" + string.Join(
                "\n",
                entries
                    .OrderBy(entry => entry.TableAsset, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => $"{entry.TableAsset}|{entry.SourceHash}")));
        }

        private List<StoryTableCsvEntry> GetChapterStoryCsvPathsForUnrealSync(ProjectInfo project, ChapterInfo chapter)
        {
            CleanupUnrealStorySectionCache(project, chapter);
            var sectionFiles = GetLocalStorySectionCsvPaths(chapter)
                .OrderBy(item => item.Section)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sectionFiles.Count == 0)
            {
                return [];
            }

            var activeSections = new List<StorySectionCsvFile>();
            foreach (var sectionFile in sectionFiles)
            {
                var rows = ReadStoryRows(sectionFile.Path);
                if (!rows.Any(StoryRowHasContent))
                {
                    if (sectionFile.Section > 1 && File.Exists(sectionFile.Path))
                    {
                        File.Delete(sectionFile.Path);
                    }

                    continue;
                }

                activeSections.Add(sectionFile);
            }

            if (activeSections.Count == 0)
            {
                return [];
            }

            var hasMultipleSections = activeSections.Count > 1 || activeSections.Any(item => item.Section > 1);
            return activeSections
                .Select(item => new StoryTableCsvEntry(
                    item.Path,
                    hasMultipleSections ? BuildSectionCsvFileBaseName(chapter.Code, item.Section) : BuildSectionCsvBaseName(chapter.Code),
                    hasMultipleSections))
                .ToList();
        }

        private static string GetUnrealStorySectionCacheFolder(ProjectInfo project, ChapterInfo chapter)
        {
            return Path.Combine(
                project.Path,
                ToolsFolderName,
                UnrealStorySectionCacheFolderName,
                RemoveChapterSectionSuffix(chapter.Code));
        }

        private static void CleanupUnrealStorySectionCache(ProjectInfo project, ChapterInfo chapter)
        {
            var folder = GetUnrealStorySectionCacheFolder(project, chapter);
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var csvPath in Directory.EnumerateFiles(folder, "*.csv", SearchOption.TopDirectoryOnly))
            {
                File.Delete(csvPath);
            }
        }

        private static void CleanupVisibleStorySectionCsvFiles(ChapterInfo chapter)
        {
            if (!Directory.Exists(chapter.Path))
            {
                return;
            }

            var mainCsvPath = GetChapterStoryCsvPath(chapter);
            var baseName = BuildSectionCsvBaseName(chapter.Code);
            var sectionBaseName = BuildSectionCsvChapterBaseName(chapter.Code);
            var stalePaths = Directory
                .EnumerateFiles(chapter.Path, $"{baseName}_小节*.csv", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(chapter.Path, $"{sectionBaseName}_*.csv", SearchOption.TopDirectoryOnly))
                .Concat(Directory.EnumerateFiles(chapter.Path, $"{sectionBaseName}-*.csv", SearchOption.TopDirectoryOnly))
                .Where(path => !PathsEqual(path, mainCsvPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var csvPath in stalePaths)
            {
                File.Delete(csvPath);
            }
        }

        private static Dictionary<string, int> ReadStorySectionMap(ChapterInfo chapter)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var state = ReadJson<StorySectionState>(GetStorySectionsPath(chapter));
            if (state?.Rows is null)
            {
                return result;
            }

            foreach (var pair in state.Rows)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    result[pair.Key] = Math.Max(1, pair.Value);
                }
            }

            return result;
        }

        private static List<int> GetSynchronizedStorySections(
            IReadOnlyList<StoryRow> rows,
            IReadOnlyDictionary<string, int> rowSections)
        {
            var result = new List<int>();
            var previousSection = 1;
            foreach (var row in rows)
            {
                var rowName = row.Get("Name");
                if (rowSections.TryGetValue(rowName, out var section))
                {
                    previousSection = Math.Max(1, section);
                }

                result.Add(previousSection);
            }

            return result;
        }

        private static string BuildUnrealStoryTableFolder(UnrealSyncContext context, ChapterInfo chapter, bool hasMultipleSections)
        {
            var categoryFolder = GetUnrealChapterCategoryFolder(chapter.Type);
            var folder = $"{context.TargetAssetRoot}/ExcelTexts/{categoryFolder}";
            return hasMultipleSections
                ? $"{folder}/{SanitizeUnrealAssetName(RemoveChapterSectionSuffix(chapter.Code))}"
                : folder;
        }

        private static List<string> BuildLegacyUnrealStoryTableAssets(
            UnrealSyncContext context,
            ChapterInfo chapter,
            string csvPath,
            string targetFolder,
            string targetAssetName)
        {
            var categoryFolder = GetUnrealChapterCategoryFolder(chapter.Type);
            var rootFolder = $"{context.TargetAssetRoot}/ExcelTexts/{categoryFolder}";
            var previousFolder = $"{rootFolder}/{SanitizeUnrealAssetName(chapter.Code)}";
            var previousUnderscoreFolder = $"{rootFolder}/{SanitizeUnrealAssetName(chapter.Code.Replace('-', '_'))}";
            var legacyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(csvPath)),
                SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(csvPath).Replace('-', '_')),
                $"{BuildSectionCsvBaseName(chapter.Code)}_小节{TryParseStorySectionFromFileName(chapter, csvPath) ?? 1}"
            };
            foreach (var legacyUnderscoreName in legacyNames.Select(name => name.Replace('-', '_')).ToList())
            {
                legacyNames.Add(legacyUnderscoreName);
            }

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in new[] { rootFolder, previousFolder, previousUnderscoreFolder, targetFolder })
            {
                foreach (var name in legacyNames)
                {
                    var objectPath = $"{folder}/{name}.{name}";
                    if (!string.Equals(objectPath, $"{targetFolder}/{targetAssetName}.{targetAssetName}", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(objectPath);
                    }
                }
            }

            return result.ToList();
        }

        private static int? TryParseStorySectionFromFileName(ChapterInfo chapter, string csvPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(csvPath);
            var currentPrefix = BuildSectionCsvChapterBaseName(chapter.Code);
            var currentMatch = Regex.Match(fileName, $"^{Regex.Escape(currentPrefix)}[-_](?<index>\\d+)$", RegexOptions.IgnoreCase);
            if (currentMatch.Success)
            {
                return ParseInt(currentMatch.Groups["index"].Value) + 1;
            }

            var oldPrefix = BuildSectionCsvBaseName(chapter.Code);
            var oldMatch = Regex.Match(fileName, $"^{Regex.Escape(oldPrefix)}_小节(?<index>\\d+)$", RegexOptions.IgnoreCase);
            if (oldMatch.Success)
            {
                return Math.Max(1, ParseInt(oldMatch.Groups["index"].Value));
            }

            var anyOldSectionSuffix = Regex.Match(fileName, @"_小节(?<index>\d+)$", RegexOptions.IgnoreCase);
            return anyOldSectionSuffix.Success ? Math.Max(1, ParseInt(anyOldSectionSuffix.Groups["index"].Value)) : null;
        }

        private static string GetUnrealChapterCategoryFolder(string chapterType)
        {
            return chapterType switch
            {
                ChapterKind.MainThread => "MainStory",
                ChapterKind.Interlude => "Interlude",
                ChapterKind.Simulation => "Simulation",
                ChapterKind.EventActivity => "EventActivity",
                ChapterKind.WorldDialog => "WorldDialog",
                ChapterKind.Minecraft => "Minecraft",
                _ => "Other"
            };
        }

        private UnrealSyncChangePlan BuildUnrealSyncChangePlan(UnrealSyncContext context, bool forceFullSync = false)
        {
            var importGroups = BuildUnrealImportGroups(context)
                .Select(group => new UnrealSyncImportGroup(
                    group.Destination,
                    forceFullSync
                        ? group.Files.ToList()
                        : group.Files
                            .Where(path => SourceFileNeedsUnrealImport(context, group.Destination, path))
                            .ToList()))
                .Where(group => group.Files.Count > 0)
                .ToList();

            var allStoryTables = BuildStoryTableSyncEntries(context);
            var storyTables = forceFullSync
                ? allStoryTables
                : allStoryTables
                    .Where(entry => SourceFileNeedsUnrealAssetUpdate(context, entry.CsvPath, entry.TableAsset))
                    .ToList();

            var syncState = ReadUnrealSyncState(context);
            var allAssetIndexTables = BuildUnrealAssetIndexTableSyncEntries(context);
            var assetIndexTablesHash = ComputeUnrealAssetIndexTablesHash(allAssetIndexTables);
            var assetIndexTablesChanged =
                forceFullSync ||
                !string.Equals(syncState.AssetIndexTablesHash, assetIndexTablesHash, StringComparison.OrdinalIgnoreCase) ||
                allAssetIndexTables.Any(entry => !File.Exists(UnrealAssetObjectPathToFilePath(context, entry.TableAsset)));
            var assetIndexTables = assetIndexTablesChanged ? allAssetIndexTables : [];

            var lustrationRows = BuildUnrealLustrationSyncEntries(context);
            var lustrationHash = ComputeSha256Hex("lustration-dataasset-v3-preserve-vfx|" + JsonSerializer.Serialize(lustrationRows, _jsonOptions));
            var lustrationAssetPath = $"{context.TargetAssetRoot}/Lustration/DA_LustrationInfor.DA_LustrationInfor";
            var lustrationAssetFilePath = UnrealAssetObjectPathToFilePath(context, lustrationAssetPath);
            var lustrationChanged =
                forceFullSync ||
                !File.Exists(lustrationAssetFilePath) ||
                !string.Equals(syncState.LustrationHash, lustrationHash, StringComparison.OrdinalIgnoreCase);

            var importCount = importGroups.Sum(group => group.Files.Count);
            var storyTableCount = storyTables.Count;
            var assetIndexTableCount = assetIndexTables.Count;
            var totalChanged = importCount + storyTableCount + assetIndexTableCount + (lustrationChanged ? 1 : 0);
            var planItems = new List<string>
            {
                forceFullSync ? "全部重新同步：已忽略时间戳和同步缓存" : "同步模式：仅同步检测到的变动",
                $"变动素材文件：{importCount} 个",
                $"变动剧情 CSV/DataTable：{storyTableCount} 个",
                assetIndexTablesChanged
                    ? $"素材索引表：需要更新 {assetIndexTableCount} 张 DataTable"
                    : "素材索引表：无变动",
                lustrationChanged
                    ? $"立绘数据资产：需要更新 {lustrationRows.Count} 个角色映射"
                    : "立绘数据资产：无变动"
            };

            foreach (var group in importGroups)
            {
                planItems.Add($"{group.Destination}：{group.Files.Count} 个文件待导入");
            }

            if (storyTableCount > 0)
            {
                planItems.Add($"ExcelTexts：{storyTableCount} 个剧情表待填充");
            }

            if (assetIndexTableCount > 0)
            {
                planItems.Add($"ExcelTexts：{assetIndexTableCount} 张素材索引表待填充");
            }

            var summary = totalChanged == 0
                ? "没有检测到需要同步的变动。"
                : forceFullSync
                    ? $"已准备全部重新同步，共 {totalChanged} 项。"
                    : $"检测到 {totalChanged} 项同步变动；本次只会同步这些变动项。";

            return new UnrealSyncChangePlan(
                importGroups,
                storyTables,
                assetIndexTables,
                lustrationChanged,
                lustrationRows,
                lustrationHash,
                assetIndexTablesHash,
                totalChanged,
                summary,
                planItems);
        }

        private List<UnrealLustrationSyncEntry> BuildUnrealLustrationSyncEntries(UnrealSyncContext context)
        {
            return GetCharactersForAssetLibrary(context.AssetLibrary)
                .Select(character =>
                {
                    var clothRefs = GetCharacterLayerImagePaths(Path.Combine(character.Path, "DN_Cloth"))
                        .Select(path => BuildUnrealAssetObjectPath($"{context.TargetAssetRoot}/Lustration/{character.Code}/DN_Cloths", path))
                        .ToList();
                    var faceRefs = GetCharacterLayerImagePaths(Path.Combine(character.Path, "FC_Face"))
                        .Select(path => BuildUnrealAssetObjectPath($"{context.TargetAssetRoot}/Lustration/{character.Code}/FC_Face", path))
                        .ToList();
                    var adornRefs = new List<string?> { null };
                    adornRefs.AddRange(GetCharacterLayerImagePaths(Path.Combine(character.Path, "AD_Adorn"))
                        .Select(path => (string?)BuildUnrealAssetObjectPath($"{context.TargetAssetRoot}/Lustration/{character.Code}/AD_Adorn", path)));
                    var color = ParseColor(character.ColorHex, Windows.UI.Color.FromArgb(255, 217, 232, 255));

                    return new UnrealLustrationSyncEntry(
                        character.Code,
                        character.Name,
                        new UnrealLinearColor(color.R / 255d, color.G / 255d, color.B / 255d, color.A / 255d),
                        clothRefs,
                        faceRefs,
                        adornRefs);
                })
                .ToList();
        }

        private List<UnrealSyncImportGroup> BuildUnrealImportGroups(UnrealSyncContext context)
        {
            var groups = new List<UnrealSyncImportGroup>();
            AddUnrealImportGroup(groups, $"{context.TargetAssetRoot}/BackGround", GetBackgroundImagePaths(GetBackgroundFolderPath(context.AssetLibrary)));
            AddUnrealImportGroup(groups, $"{context.TargetAssetRoot}/BGM", GetAudioFilePaths(GetMusicFolderPath(context.AssetLibrary)));
            AddUnrealImportGroup(groups, $"{context.TargetAssetRoot}/Scene_Effect", GetAudioFilePaths(GetAmbientSoundFolderPath(context.AssetLibrary)).Concat(GetAudioFilePaths(GetSoundEffectFolderPath(context.AssetLibrary))).ToList());

            foreach (var character in GetCharactersForAssetLibrary(context.AssetLibrary))
            {
                AddUnrealImportGroup(groups, $"{context.TargetAssetRoot}/Lustration/{character.Code}/DN_Cloths", GetCharacterLayerImagePaths(Path.Combine(character.Path, "DN_Cloth")));
                AddUnrealImportGroup(groups, $"{context.TargetAssetRoot}/Lustration/{character.Code}/FC_Face", GetCharacterLayerImagePaths(Path.Combine(character.Path, "FC_Face")));
                AddUnrealImportGroup(groups, $"{context.TargetAssetRoot}/Lustration/{character.Code}/AD_Adorn", GetCharacterLayerImagePaths(Path.Combine(character.Path, "AD_Adorn")));
            }

            return groups;
        }

        private void WriteLustrationInfoCsv(UnrealSyncContext context, string csvPath)
        {
            var rows = new List<string>
            {
                string.Join(",", ["", "Name", "Color", "Cloth", "Face", "Adorn"])
            };

            foreach (var character in GetCharactersForAssetLibrary(context.AssetLibrary))
            {
                var clothRefs = GetCharacterLayerImagePaths(Path.Combine(character.Path, "DN_Cloth"))
                    .Select(path => BuildUnrealTextureReference($"{context.TargetAssetRoot}/Lustration/{character.Code}/DN_Cloths", path))
                    .ToList();
                var faceRefs = GetCharacterLayerImagePaths(Path.Combine(character.Path, "FC_Face"))
                    .Select(path => BuildUnrealTextureReference($"{context.TargetAssetRoot}/Lustration/{character.Code}/FC_Face", path))
                    .ToList();
                var adornRefs = new List<string> { "None" };
                adornRefs.AddRange(GetCharacterLayerImagePaths(Path.Combine(character.Path, "AD_Adorn"))
                    .Select(path => BuildUnrealTextureReference($"{context.TargetAssetRoot}/Lustration/{character.Code}/AD_Adorn", path)));

                rows.Add(string.Join(",",
                [
                    EscapeCsv(character.Code),
                    EscapeCsv(character.Name),
                    EscapeCsv(ToUnrealLinearColorLiteral(character.ColorHex)),
                    EscapeCsv(ToUnrealArrayLiteral(clothRefs)),
                    EscapeCsv(ToUnrealArrayLiteral(faceRefs)),
                    EscapeCsv(ToUnrealArrayLiteral(adornRefs))
                ]));
            }

            File.WriteAllLines(csvPath, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        private static string BuildUnrealTextureReference(string destinationPath, string sourcePath)
        {
            var assetName = SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(sourcePath));
            return $"Texture2D'{destinationPath}/{assetName}.{assetName}'";
        }

        private static string BuildUnrealSoundWaveReference(string destinationPath, string sourcePath)
        {
            var assetName = SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(sourcePath));
            return $"SoundWave'{destinationPath}/{assetName}.{assetName}'";
        }

        private static string BuildUnrealAssetObjectPath(string destinationPath, string sourcePath)
        {
            var assetName = SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(sourcePath));
            return $"{destinationPath}/{assetName}.{assetName}";
        }

        private static bool SourceFileNeedsUnrealImport(UnrealSyncContext context, string destinationPath, string sourcePath)
        {
            var objectPath = BuildUnrealAssetObjectPath(destinationPath, sourcePath);
            return SourceFileNeedsUnrealAssetUpdate(context, sourcePath, objectPath);
        }

        private static bool SourceFileNeedsUnrealAssetUpdate(UnrealSyncContext context, string sourcePath, string objectPath)
        {
            if (!File.Exists(sourcePath))
            {
                return false;
            }

            var assetFilePath = UnrealAssetObjectPathToFilePath(context, objectPath);
            if (!File.Exists(assetFilePath))
            {
                return true;
            }

            return File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(assetFilePath).AddSeconds(1);
        }

        private static string UnrealAssetObjectPathToFilePath(UnrealSyncContext context, string objectPath)
        {
            var packagePath = objectPath.Split('.')[0].Trim('/');
            var targetRoot = context.TargetAssetRoot.Trim('/');
            var relativePath = packagePath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)
                ? packagePath[targetRoot.Length..].Trim('/')
                : packagePath;

            return Path.Combine(
                context.TargetContentFolderPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar) + ".uasset");
        }

        private static string ToUnrealArrayLiteral(IReadOnlyList<string> values)
        {
            return values.Count == 0 ? "()" : $"({string.Join(",", values)})";
        }

        private static string ToUnrealLinearColorLiteral(string colorHex)
        {
            var fallback = Windows.UI.Color.FromArgb(255, 217, 232, 255);
            var color = ParseColor(colorHex, fallback);
            return FormattableString.Invariant(
                $"(R={color.R / 255d:0.######},G={color.G / 255d:0.######},B={color.B / 255d:0.######},A={color.A / 255d:0.######})");
        }

        private static string SanitizeUnrealAssetName(string value)
        {
            var sanitized = Regex.Replace(value.Trim(), "\\s+", "_");
            sanitized = Regex.Replace(sanitized, "[^\\p{L}\\p{N}_\\-（）()]+", "_");
            return string.IsNullOrWhiteSpace(sanitized) ? "Asset" : sanitized;
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static void AddUnrealImportGroup(List<UnrealSyncImportGroup> groups, string destination, IReadOnlyCollection<string> files)
        {
            if (files.Count == 0)
            {
                return;
            }

            groups.Add(new UnrealSyncImportGroup(destination, files.ToList()));
        }

        private static void WriteUnrealSyncPythonScript(string scriptPath, string manifestPath)
        {
            var normalizedManifestPath = manifestPath.Replace("\\", "/");
            var script = $$"""
                import json
                import os
                import unreal

                manifest_path = r"{{normalizedManifestPath}}"
                with open(manifest_path, "r", encoding="utf-8-sig") as manifest_file:
                    manifest = json.load(manifest_file)

                target_root = manifest.get("TargetRoot", "/Game")
                imports = manifest.get("Imports", [])
                tasks = []

                for group in imports:
                    destination = group.get("Destination")
                    files = group.get("Files", [])
                    if not destination:
                        continue
                    unreal.EditorAssetLibrary.make_directory(destination)
                    for filename in files:
                        if not filename or not os.path.exists(filename):
                            unreal.log_warning("GalExcleTools missing source file: {}".format(filename))
                            continue
                        task = unreal.AssetImportTask()
                        task.set_editor_property("filename", filename)
                        task.set_editor_property("destination_path", destination)
                        task.set_editor_property("automated", True)
                        task.set_editor_property("replace_existing", True)
                        try:
                            task.set_editor_property("save", True)
                        except Exception:
                            pass
                        tasks.append(task)

                if tasks:
                    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks(tasks)
                    imported_paths = []
                    for task in tasks:
                        try:
                            imported_paths.extend(task.get_editor_property("imported_object_paths") or [])
                        except Exception:
                            pass
                    for imported_path in imported_paths:
                        try:
                            unreal.EditorAssetLibrary.save_asset(imported_path, only_if_is_dirty=False)
                        except Exception as exc:
                            unreal.log_warning("GalExcleTools failed to save imported asset {}: {}".format(imported_path, exc))

                def set_first_editor_property(obj, property_names, value):
                    for property_name in property_names:
                        try:
                            obj.set_editor_property(property_name, value)
                            return True
                        except Exception:
                            pass
                    return False

                def get_first_editor_property(obj, property_names):
                    if not obj:
                        return None
                    for property_name in property_names:
                        try:
                            return obj.get_editor_property(property_name)
                        except Exception:
                            pass
                    return None

                def get_map_value_by_string_key(source_map, key):
                    if not source_map:
                        return None
                    string_key = str(key)
                    try:
                        value = source_map.get(string_key)
                        if value is not None:
                            return value
                    except Exception:
                        pass
                    try:
                        return source_map[string_key]
                    except Exception:
                        pass
                    try:
                        for existing_key, existing_value in source_map.items():
                            if str(existing_key) == string_key:
                                return existing_value
                    except Exception:
                        pass
                    return None

                def load_asset_or_none(asset_path):
                    if not asset_path:
                        return None
                    try:
                        return unreal.EditorAssetLibrary.load_asset(asset_path)
                    except Exception as exc:
                        unreal.log_warning("GalExcleTools failed to load asset reference {}: {}".format(asset_path, exc))
                        return None

                def load_asset_array(asset_paths):
                    result = []
                    for asset_path in asset_paths or []:
                        result.append(load_asset_or_none(asset_path))
                    return result

                def split_asset_object_path(asset_object_path):
                    package_path = asset_object_path.split(".")[0]
                    asset_name = package_path.rsplit("/", 1)[-1]
                    destination_path = package_path.rsplit("/", 1)[0]
                    return destination_path, asset_name

                def load_row_struct(row_struct_path):
                    if not row_struct_path:
                        return None
                    try:
                        row_struct = unreal.load_object(None, row_struct_path)
                        if row_struct:
                            unreal.log("GalExcleTools story row struct ready: {}".format(row_struct_path))
                        else:
                            unreal.log_warning("GalExcleTools story row struct missing: {}".format(row_struct_path))
                        return row_struct
                    except Exception as exc:
                        unreal.log_warning("GalExcleTools failed to load story row struct {}: {}".format(row_struct_path, exc))
                        return None

                def create_data_table_asset(table_asset_path, row_struct):
                    if not row_struct:
                        return None
                    destination_path, asset_name = split_asset_object_path(table_asset_path)
                    unreal.EditorAssetLibrary.make_directory(destination_path)
                    try:
                        factory = unreal.DataTableFactory()
                        if not set_first_editor_property(factory, ["struct", "row_struct", "Struct", "RowStruct"], row_struct):
                            unreal.log_warning("GalExcleTools could not assign row struct before creating data table: {}".format(table_asset_path))
                        data_table = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
                            asset_name,
                            destination_path,
                            unreal.DataTable,
                            factory)
                        if data_table:
                            unreal.log("GalExcleTools created story data table: {}".format(table_asset_path))
                        return data_table
                    except Exception as exc:
                        unreal.log_warning("GalExcleTools failed to create story data table {}: {}".format(table_asset_path, exc))
                        return None

                def ensure_story_data_table(table_asset_path, row_struct_path):
                    data_table = unreal.EditorAssetLibrary.load_asset(table_asset_path)
                    if data_table:
                        return data_table, load_row_struct(row_struct_path)
                    row_struct = load_row_struct(row_struct_path)
                    data_table = create_data_table_asset(table_asset_path, row_struct)
                    return data_table, row_struct

                story_tables = manifest.get("StoryTables", [])
                for story_table in story_tables:
                    table_asset_path = story_table.get("TableAsset")
                    csv_path = story_table.get("CsvPath")
                    row_struct_path = story_table.get("RowStruct")
                    if not table_asset_path or not csv_path:
                        continue
                    for legacy_asset_path in story_table.get("LegacyTableAssets", []) or []:
                        if legacy_asset_path and legacy_asset_path != table_asset_path and unreal.EditorAssetLibrary.does_asset_exist(legacy_asset_path):
                            if unreal.EditorAssetLibrary.delete_asset(legacy_asset_path):
                                unreal.log("GalExcleTools deleted legacy story data table: {}".format(legacy_asset_path))
                            else:
                                unreal.log_warning("GalExcleTools failed to delete legacy story data table: {}".format(legacy_asset_path))
                    data_table, row_struct = ensure_story_data_table(table_asset_path, row_struct_path)
                    if data_table:
                        try:
                            ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path, row_struct)
                        except TypeError:
                            ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path)
                        if ok:
                            unreal.EditorAssetLibrary.save_asset(table_asset_path, only_if_is_dirty=False)
                            unreal.log("GalExcleTools updated story data table: {}".format(table_asset_path))
                        else:
                            unreal.log_warning("GalExcleTools failed to update story data table: {}".format(table_asset_path))
                    else:
                        unreal.log_warning("GalExcleTools could not create or load story data table: {}".format(table_asset_path))

                asset_index_tables = manifest.get("AssetIndexTables", [])
                for index_table in asset_index_tables:
                    table_asset_path = index_table.get("TableAsset")
                    csv_path = index_table.get("CsvPath")
                    row_struct_path = index_table.get("RowStruct")
                    if not table_asset_path or not csv_path:
                        continue
                    data_table, row_struct = ensure_story_data_table(table_asset_path, row_struct_path)
                    if data_table:
                        try:
                            ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path, row_struct)
                        except TypeError:
                            ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path)
                        if ok:
                            unreal.EditorAssetLibrary.save_asset(table_asset_path, only_if_is_dirty=False)
                            unreal.log("GalExcleTools updated asset index data table: {}".format(table_asset_path))
                        else:
                            unreal.log_warning("GalExcleTools failed to update asset index data table: {}".format(table_asset_path))
                    else:
                        unreal.log_warning("GalExcleTools could not create or load asset index data table: {}".format(table_asset_path))

                def build_lustration_struct(row, existing_item=None):
                    # Keep Unreal-side Vfx data intact. The tool owns imported texture
                    # layers, but VFX materials are configured manually in the data asset.
                    item = existing_item if existing_item is not None else unreal.LustrationStruct()
                    color = row.get("Color") or {}
                    linear_color = unreal.LinearColor(
                        float(color.get("R", 0.0)),
                        float(color.get("G", 0.0)),
                        float(color.get("B", 0.0)),
                        float(color.get("A", 1.0)))
                    set_first_editor_property(item, ["Name", "name"], row.get("Name", ""))
                    set_first_editor_property(item, ["Color", "color"], linear_color)
                    set_first_editor_property(item, ["Cloth", "cloth"], load_asset_array(row.get("Cloth", [])))
                    set_first_editor_property(item, ["Face", "face"], load_asset_array(row.get("Face", [])))
                    set_first_editor_property(item, ["Adorn", "adorn"], load_asset_array(row.get("Adorn", [])))
                    return item

                lustration_info = manifest.get("LustrationInfo", {})
                data_asset_path = lustration_info.get("DataAsset")
                map_property = lustration_info.get("MapProperty", "Infor")
                lustration_rows = lustration_info.get("Rows", [])
                should_update_lustration = lustration_info.get("ShouldUpdate", bool(lustration_rows))
                if data_asset_path and should_update_lustration:
                    data_asset = unreal.EditorAssetLibrary.load_asset(data_asset_path)
                    if data_asset:
                        existing_lustration_map = get_first_editor_property(data_asset, [map_property, map_property.lower(), "Infor", "infor"]) or {}
                        lustration_map = {}
                        for row in lustration_rows:
                            key = row.get("Key")
                            if not key:
                                continue
                            existing_item = get_map_value_by_string_key(existing_lustration_map, key)
                            lustration_map[str(key)] = build_lustration_struct(row, existing_item)
                        try:
                            data_asset.modify()
                        except Exception:
                            pass
                        if set_first_editor_property(data_asset, [map_property, map_property.lower(), "Infor", "infor"], lustration_map):
                            unreal.EditorAssetLibrary.save_asset(data_asset_path, only_if_is_dirty=False)
                            unreal.log("GalExcleTools updated lustration data asset: {} rows={}".format(data_asset_path, len(lustration_map)))
                        else:
                            unreal.log_warning("GalExcleTools could not set lustration map property '{}' on {}".format(map_property, data_asset_path))
                    else:
                        unreal.log_warning("GalExcleTools could not load lustration data asset: {}".format(data_asset_path))

                unreal.log("GalExcleTools sync finished. Imported task count: {}".format(len(tasks)))
                """;

            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string? ResolveUnrealEditorExecutable(string enginePath)
        {
            if (string.IsNullOrWhiteSpace(enginePath))
            {
                return null;
            }

            if (File.Exists(enginePath))
            {
                var fileName = Path.GetFileName(enginePath);
                if (fileName.Equals("UnrealEditor-Cmd.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return enginePath;
                }

                if (fileName.Equals("UnrealEditor.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var cmdPath = Path.Combine(Path.GetDirectoryName(enginePath)!, "UnrealEditor-Cmd.exe");
                    return File.Exists(cmdPath) ? cmdPath : enginePath;
                }

                return null;
            }

            var binPath = Path.Combine(enginePath, "Engine", "Binaries", "Win64");
            var cmdCandidate = Path.Combine(binPath, "UnrealEditor-Cmd.exe");
            if (File.Exists(cmdCandidate))
            {
                return cmdCandidate;
            }

            var editorCandidate = Path.Combine(binPath, "UnrealEditor.exe");
            return File.Exists(editorCandidate) ? editorCandidate : null;
        }

        private static readonly string[] ExpectedUnrealNarrativeFolders =
        [
            "BackGround",
            "BGM",
            "ExcelTexts",
            "Lustration",
            "Scene_Effect"
        ];

        private List<string> GetProjectStoryCsvPaths(ProjectInfo project)
        {
            var chaptersFolderPath = GetChaptersFolderPath(project);
            if (!Directory.Exists(chaptersFolderPath))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(chaptersFolderPath)
                .Select(ReadChapterInfo)
                .SelectMany(chapter => GetChapterStoryCsvPathsForUnrealSync(project, chapter).Select(entry => entry.CsvPath))
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<CharacterInfo> GetCharactersForAssetLibrary(AssetLibraryInfo assetLibrary)
        {
            var characterFolderPath = GetCharacterFolderPath(assetLibrary);
            if (!Directory.Exists(characterFolderPath))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(characterFolderPath)
                .Select(ReadCharacterInfo)
                .OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> GetProjectCharacterLayerImportPaths(AssetLibraryInfo assetLibrary)
        {
            var result = new List<string>();
            foreach (var character in GetCharactersForAssetLibrary(assetLibrary))
            {
                result.AddRange(GetCharacterLayerImagePaths(Path.Combine(character.Path, "DN_Cloth")));
                result.AddRange(GetCharacterLayerImagePaths(Path.Combine(character.Path, "FC_Face")));
                result.AddRange(GetCharacterLayerImagePaths(Path.Combine(character.Path, "AD_Adorn")));
            }

            return result;
        }

        private static int CountUnrealAssets(string folderPath)
        {
            return Directory.Exists(folderPath)
                ? Directory.EnumerateFiles(folderPath, "*.uasset", SearchOption.TopDirectoryOnly).Count()
                : 0;
        }

        private static bool IsPathInsideDirectoryOrEqual(string path, string directoryPath)
        {
            return PathsEqual(path, directoryPath) || IsPathInsideDirectory(path, directoryPath);
        }

        private static string ToUnrealAssetPath(string contentRootPath, string contentFolderPath)
        {
            var relativePath = Path.GetRelativePath(contentRootPath, contentFolderPath)
                .Replace('\\', '/')
                .Trim('/');
            return string.IsNullOrWhiteSpace(relativePath) || relativePath == "."
                ? "/Game"
                : $"/Game/{relativePath}";
        }

        private static string TrimLongText(string text, int maxLength)
        {
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text[..maxLength] + "...";
        }

        private async void ShowProjectRootHelpButton_Click(object sender, RoutedEventArgs e)
        {
            var helpContent = new ScrollViewer
            {
                Width = 440,
                MaxHeight = 420,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Auto,
                Content = CreateProjectRootHelpContent()
            };

            var dialog = new ContentDialog
            {
                Title = "整体项目位置说明",
                Content = helpContent,
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async void ShowLogHelpButton_Click(object sender, RoutedEventArgs e)
        {
            var helpContent = new ScrollViewer
            {
                Width = 440,
                MaxHeight = 420,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Auto,
                Content = CreateLogHelpContent()
            };

            var dialog = new ContentDialog
            {
                Title = "辅助显示说明",
                Content = helpContent,
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private static StackPanel CreateProjectRootHelpContent()
        {
            var panel = new StackPanel
            {
                Spacing = 12,
                Width = 420,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            panel.Children.Add(CreateHelpHeading("整体项目位置"));
            panel.Children.Add(CreateHelpParagraph("这里设置的是所有 GalExcleTools 项目的总存放目录。程序启动时会检查这个目录，不存在就自动创建。"));
            panel.Children.Add(CreateHelpHeading("选择位置"));
            panel.Children.Add(CreateHelpParagraph("点击“选择位置”时，你选择的是父目录。程序会自动在该目录下追加 GalExcelProject 文件夹名。"));
            panel.Children.Add(CreateHelpCodeBlock("""
                示例：
                选择：E:\VNWork
                实际使用：E:\VNWork\GalExcelProject
                """));
            panel.Children.Add(CreateHelpHeading("迁移规则"));
            panel.Children.Add(CreateHelpParagraph("如果你更换了目录，程序会把旧目录里的所有文件复制到新目录，逐个校验文件大小和 SHA-256 内容哈希。全部确认无误后，才会保存新路径并删除旧目录。"));
            panel.Children.Add(CreateHelpHeading("安全限制"));
            panel.Children.Add(CreateHelpParagraph("新目录不能放在旧目录里面。这样可以避免迁移后删除旧目录时，把新目录也一起删除。"));

            return panel;
        }

        private static StackPanel CreateLogHelpContent()
        {
            var panel = new StackPanel
            {
                Spacing = 12,
                Width = 420,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            panel.Children.Add(CreateHelpHeading("辅助显示"));
            panel.Children.Add(CreateHelpParagraph("这里集中控制不影响项目文件内容的辅助界面。工作区路径用于查看当前文件位置；底部输出框用于记录程序触发、进度、用户操作、提示和错误。"));
            panel.Children.Add(CreateHelpHeading("用户操作"));
            panel.Children.Add(CreateHelpParagraph("用户操作会记录创建项目、创建素材库、导入素材、排序、备注、切换目录等动作。故事编辑器的数据编辑会额外记录可撤回操作，方便用 Ctrl+Z 或撤回按钮回到上一步。"));
            panel.Children.Add(CreateHelpHeading("提示和错误"));
            panel.Children.Add(CreateHelpParagraph("提示用于标记潜在风险或不规范操作；错误会带上失败原因。关闭对应开关后，底部输出框会过滤该类型。"));

            return panel;
        }

        private static TextBlock CreateHelpHeading(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
        }

        private static TextBlock CreateHelpParagraph(string text)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Width = 420,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            };
        }

        private static Border CreateHelpCodeBlock(string text)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Background = Application.Current.Resources["LayerFillColorAltBrush"] as Brush,
                Child = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Consolas"),
                    Width = 396,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        private void ShowWorkbenchPage()
        {
            StopStoryEditorAudio();
            ResetCreateProjectForm();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Visible;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, WorkbenchNavItem))
            {
                ShellNavigation.SelectedItem = WorkbenchNavItem;
            }
            LoadProjects();
            RefreshWorkbenchUnrealSyncTip();
        }

        private void ShowAssetLibraryPage()
        {
            StopStoryEditorAudio();
            ResetCreateAssetLibraryForm();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Visible;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, AssetLibraryNavItem))
            {
                ShellNavigation.SelectedItem = AssetLibraryNavItem;
            }
            LoadAssetLibraries();
        }

        private void ShowAssetLibraryDetailPage(AssetLibraryInfo assetLibrary)
        {
            StopStoryEditorAudio();
            _currentAssetLibrary = assetLibrary;
            EnsureAssetLibraryCategoryFolders(assetLibrary.Path);
            AssetLibraryDetailTitleText.Text = assetLibrary.Name;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Visible;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, AssetLibraryNavItem))
            {
                ShellNavigation.SelectedItem = AssetLibraryNavItem;
            }
            LoadBackgroundImages(assetLibrary);
            LoadMusicFiles(assetLibrary);
            LoadAmbientSoundFiles(assetLibrary);
            LoadSoundEffectFiles(assetLibrary);
            LoadFunctions(assetLibrary);
            LoadCharacters(assetLibrary);
            LoadCharacterFilters(assetLibrary);
        }

        private void ShowCreateProjectPage()
        {
            LoadAssetLibraryOptions();
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Visible;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;

            if (ProjectAssetLibraryComboBox.Items.Count == 0)
            {
                ShowCreateProjectError("还没有素材库，请先到“素材库”页面创建素材库。");
            }
        }

        private void ShowCreateAssetLibraryPage()
        {
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Visible;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
        }

        private void ShowUnrealSyncPage()
        {
            StopStoryEditorAudio();
            LoadUnrealSyncProjectOptions();
            ApplyUnrealSyncSettingsToUi();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Visible;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, UnrealSyncNavItem))
            {
                ShellNavigation.SelectedItem = UnrealSyncNavItem;
            }

            RefreshUnrealSyncStatus();
        }

        private void ShowSettingsPage()
        {
            StopStoryEditorAudio();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
        }

        private sealed record ProjectInfo(string Name, string Code, string FolderName, string Path, string? ThumbnailPath, string AssetLibraryName, string? AssetLibraryFolderName, DateTime LastEditedAt);

        private sealed record ChapterInfo(string Name, string Code, string Type, string Path, DateTime LastEditedAt, int LastEditedRowIndex);

        private sealed record ChapterEditorInput(string Name, string Code, string Type);

        private sealed record FolderBackupEntry(string Path, DateTime CreatedAt, long SizeBytes, string Note, string DisplayName);

        private sealed record FolderBackupProgress(
            string Message,
            double Percent,
            int CompletedFiles,
            int TotalFiles,
            long CompletedBytes,
            long TotalBytes,
            string? CurrentRelativePath);

        private sealed record AssetIndexSyncProgress(
            string Message,
            double Percent,
            int CompletedCsvFiles,
            int TotalCsvFiles,
            int ChangeCount,
            int WarningCount,
            string? CurrentCsvName);

        private sealed record AssetIndexSyncResult(
            string Title,
            int ScannedCsvCount,
            int ChangedCsvCount,
            int ChangeCount,
            int WarningCount,
            IReadOnlyList<string> ChangedCsvPaths,
            IReadOnlyList<AssetIndexChange> Changes,
            IReadOnlyList<AssetIndexWarning> Warnings);

        private sealed record AssetIndexChange(
            string ProjectName,
            string ChapterName,
            string ChapterCode,
            string CsvName,
            string RowName,
            int RowNumber,
            string ColumnName,
            string OldValue,
            string NewValue,
            string OldValueLabel,
            string NewValueLabel);

        private sealed record AssetIndexWarning(
            string ProjectName,
            string ChapterName,
            string ChapterCode,
            string CsvName,
            string RowName,
            int RowNumber,
            string ColumnName,
            string Message);

        private sealed record RelatedStoryCsvFile(string ProjectName, string ChapterName, string ChapterCode, string CsvPath);

        private sealed record StoryIndexRowContext(
            RelatedStoryCsvFile CsvFile,
            StoryRow Row,
            int RowIndex,
            List<AssetIndexChange> Changes,
            List<AssetIndexWarning> Warnings)
        {
            public AssetIndexChange CreateChange(string columnName, string oldValue, string newValue, string oldValueLabel, string newValueLabel)
            {
                return new AssetIndexChange(
                    CsvFile.ProjectName,
                    CsvFile.ChapterName,
                    CsvFile.ChapterCode,
                    Path.GetFileName(CsvFile.CsvPath),
                    Row.Get("Name"),
                    RowIndex + 1,
                    columnName,
                    oldValue,
                    newValue,
                    oldValueLabel,
                    newValueLabel);
            }

            public AssetIndexWarning CreateWarning(string columnName, string message)
            {
                return new AssetIndexWarning(
                    CsvFile.ProjectName,
                    CsvFile.ChapterName,
                    CsvFile.ChapterCode,
                    Path.GetFileName(CsvFile.CsvPath),
                    Row.Get("Name"),
                    RowIndex + 1,
                    columnName,
                    message);
            }
        }

        private sealed record ChapterRepairProgress(
            string Message,
            double Percent,
            int CompletedCsvFiles,
            int TotalCsvFiles,
            int IssueCount,
            int FixedCount,
            string? CurrentCsvName);

        private sealed record ChapterRepairResult(
            string ProjectName,
            string ChapterName,
            string ChapterCode,
            int ScannedCsvCount,
            int IssueCount,
            int FixedCount,
            IReadOnlyList<string> ChangedCsvPaths,
            IReadOnlyList<ChapterRepairIssue> Issues)
        {
            public int AutoFixableCount => Issues.Count(issue => issue.CanAutoFix);
        }

        private sealed record ChapterRepairIssue(
            string ProjectName,
            string ChapterName,
            string ChapterCode,
            string CsvName,
            string RowName,
            int RowNumber,
            string ColumnName,
            string Message,
            bool CanAutoFix);

        private sealed record ChapterRepairAssetContext(
            int BackgroundCount,
            int BgmCount,
            int SceneCount,
            int FilterCount,
            IReadOnlyDictionary<string, string> CharacterAliases,
            IReadOnlyDictionary<string, CharacterRepairAssetCounts> CharacterAssets);

        private sealed record CharacterRepairAssetCounts(int ClothCount, int FaceCount, int AdornCount);

        private sealed record ChapterTypeOption(string Kind, string DisplayName);

        private sealed record StoryAssetChoice(int Index, string Name);

        private sealed record StoryObjectChoice(string Key, string DisplayName, object Value, IReadOnlyList<string>? PreviewPaths = null);

        private sealed record UnrealSyncValidation(
            InfoBarSeverity Severity,
            string Title,
            string Message,
            string Summary,
            bool CanSync,
            UnrealSyncContext? Context,
            IReadOnlyList<string> PlanItems);

        private sealed record UnrealSyncContext(
            string EditorPath,
            string UnrealProjectPath,
            string TargetContentFolderPath,
            string TargetAssetRoot,
            ProjectInfo Project,
            AssetLibraryInfo AssetLibrary);

        private sealed record UnrealSyncProgressUpdate(string Message, double Percent);

        private sealed record UnrealProjectBinding(string? EnginePath, string? ProjectPath, string? ContentFolderPath)
        {
            public bool IsComplete =>
                !string.IsNullOrWhiteSpace(EnginePath) &&
                !string.IsNullOrWhiteSpace(ProjectPath) &&
                !string.IsNullOrWhiteSpace(ContentFolderPath);
        }

        private sealed record UnrealSyncChangePlan(
            List<UnrealSyncImportGroup> ImportGroups,
            List<UnrealStoryTableSyncEntry> StoryTables,
            List<UnrealAssetIndexTableSyncEntry> AssetIndexTables,
            bool LustrationChanged,
            List<UnrealLustrationSyncEntry> LustrationRows,
            string LustrationHash,
            string AssetIndexTablesHash,
            int TotalChangedItems,
            string Summary,
            List<string> PlanItems)
        {
            public bool HasChanges => TotalChangedItems > 0;
        }

        private sealed record UnrealSyncImportGroup(string Destination, List<string> Files);

        private sealed record StoryTableCsvEntry(string CsvPath, string AssetName, bool IsSectionCsv);

        private sealed record StorySectionCsvFile(string Path, int Section);

        private sealed record StoryCharacterSlotClipboard(string Character, string Body, string Face, string Adorn, string Vfx);

        private sealed record StoryAssetClipboard(string FieldName, string Value);

        private sealed record StoryEditorUndoState(
            List<StoryRow> Rows,
            Dictionary<string, int> Sections,
            int RowIndex,
            string Description,
            StoryChoiceNoteState? ChoiceNotes);

        private sealed record UnrealStoryTableSyncEntry(
            string CsvPath,
            string TableAsset,
            string RowStruct,
            List<string> LegacyTableAssets);

        private sealed record UnrealAssetIndexTableSyncEntry(
            string CsvPath,
            string TableAsset,
            string RowStruct,
            string SourceHash);

        private sealed record UnrealLinearColor(double R, double G, double B, double A);

        private sealed record UnrealLustrationSyncEntry(
            string Key,
            string Name,
            UnrealLinearColor Color,
            List<string> Cloth,
            List<string> Face,
            List<string?> Adorn);

        private sealed record UnrealSyncResult(int ExitCode, string ManifestPath, string ScriptPath, string Output);

        private sealed class UnrealSyncState
        {
            public DateTimeOffset LastSyncedAt { get; set; }

            public string? LustrationHash { get; set; }

            public string? AssetIndexTablesHash { get; set; }
        }

        private sealed class StoryRow
        {
            private readonly Dictionary<string, string> _cells = new(StringComparer.Ordinal);

            public string Get(string column)
            {
                return _cells.TryGetValue(column, out var value) ? value : string.Empty;
            }

            public void Set(string column, string value)
            {
                _cells[column] = value;
            }

            public StoryRow Clone()
            {
                var clone = new StoryRow();
                foreach (var pair in _cells)
                {
                    clone.Set(pair.Key, pair.Value);
                }

                return clone;
            }
        }

        private sealed class StorySectionState
        {
            public Dictionary<string, int> Rows { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class StoryChoiceNoteState
        {
            public Dictionary<string, List<string>> Choices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed record StoryCsvCompatibility(bool IsCompatible, IReadOnlyList<string> MissingColumns, IReadOnlyList<string> ExtraColumns);

        private static readonly string[] StoryCsvColumns =
        [
            "Name",
            "Tesxt",
            "Custom",
            "BGindex",
            "BGM",
            "Scene",
            "TalkChar",
            "TalkBody",
            "TalkFace",
            "TalkAdorn",
            "TalkVfx",
            "Chara1",
            "Body1",
            "Face1",
            "Adorn1",
            "Vfx1",
            "Chara2",
            "Body2",
            "Face2",
            "Adorn2",
            "Vfx2",
            "Chara3",
            "Body3",
            "Face3",
            "Adorn3",
            "Vfx3",
            "Chara4",
            "Body4",
            "Face4",
            "Adorn4",
            "Vfx4",
            "Chara5",
            "Body5",
            "Face5",
            "Adorn5",
            "Vfx5"
        ];

        private static readonly HashSet<string> StoryNumericColumns =
            new(StoryCsvColumns.Where(column =>
                column is "BGindex" or "BGM" or "Scene" or
                    "TalkBody" or "TalkFace" or "TalkAdorn" or "TalkVfx" ||
                Regex.IsMatch(column, "^(Body|Face|Adorn|Vfx)\\d+$")), StringComparer.Ordinal);

        private static readonly ChapterTypeOption[] ChapterTypeOptions =
        [
            new(ChapterKind.MainThread, "主线剧情 / Main Thread"),
            new(ChapterKind.Interlude, "间章 / Interlude"),
            new(ChapterKind.Simulation, "养成 / Simulation"),
            new(ChapterKind.EventActivity, "活动关 / Event Activity"),
            new(ChapterKind.WorldDialog, "世界对话 / World Dialog"),
            new(ChapterKind.Minecraft, "我的世界 NPC 对话 / Minecraft")
        ];

        private static class ChapterKind
        {
            public const string MainThread = "MainThread";
            public const string Interlude = "Interlude";
            public const string Simulation = "Simulation";
            public const string EventActivity = "EventActivity";
            public const string WorldDialog = "WorldDialog";
            public const string Minecraft = "Minecraft";
        }

        private sealed record AssetLibraryInfo(string Name, string FolderName, string Path, string? ThumbnailPath, DateTime LastEditedAt);

        private sealed record BackgroundImageEntry(string Path, string Remark);

        private sealed record BackgroundImageRename(BackgroundImageEntry Entry, string TargetPath);

        private enum AudioAssetKind
        {
            Music,
            Ambient,
            SoundEffect
        }

        private sealed class FunctionIndex
        {
            public List<FunctionEntry> Entries { get; set; } = [];
        }

        private sealed record FunctionEntry(string Id, string Name, string Indicator, string Category, List<string> ChoiceNotes);

        private sealed record FunctionEditorInput(string Name, string Indicator, string Category, List<string> ChoiceNotes);

        private sealed class CharacterFilterIndex
        {
            public List<CharacterFilterEntry> Entries { get; set; } = [];
        }

        private sealed record CharacterFilterEntry(string Id, string Remark);

        private sealed record MusicEntry(string Path, string Remark);

        private sealed record MusicRename(MusicEntry Entry, string TargetPath);

        private sealed record CharacterInfo(string Name, string Code, string ColorHex, string Path);

        private sealed record CharacterEditorInput(string Name, string Code, string ColorHex);

        private sealed record CharacterLayerEntry(string Path, string Remark, string Scope);

        private sealed record CharacterLayerRename(CharacterLayerEntry Entry, string TargetPath);

        private sealed class CharacterLayerScopeMeta
        {
            public Dictionary<string, CharacterLayerScopeEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CharacterLayerScopeEntry
        {
            public bool UseAllCostumes { get; set; } = true;

            public List<string> CostumeHashes { get; set; } = [];
        }

        private sealed record MigrationResult(int FileCount, int DirectoryCount);

        private enum CharacterLayerKind
        {
            Cloth,
            Face,
            Adorn,
            Vfx
        }

        private enum LogKind
        {
            Info,
            User,
            Warning,
            Error
        }

        private sealed class AppSettings
        {
            public string? ProjectRootPath { get; set; }

            public bool ShowWorkspacePath { get; set; } = true;

            public bool LogEnabled { get; set; } = true;

            public bool LogUserOperations { get; set; } = true;

            public bool LogWarnings { get; set; } = true;

            public bool LogErrors { get; set; } = true;

            public double AssetLibraryScrollSpeedMultiplier { get; set; } = 1.5;

            public double StoryTextFontSize { get; set; } = 20;

            public bool ShowFullStoryChapterLength { get; set; }

            public string? UnrealEnginePath { get; set; }

            public string? UnrealProjectPath { get; set; }

            public string? UnrealContentFolderPath { get; set; }

            public string? UnrealToolProjectFolderName { get; set; }
        }

        private sealed class CharacterMeta
        {
            public string? Name { get; set; }

            public string? Code { get; set; }

            public string? ColorHex { get; set; }
        }

        private sealed class ProjectMeta
        {
            public string? ProjectName { get; set; }
            public string? ProjectCode { get; set; }
            public string? ThumbnailFileName { get; set; }
            public string? AssetLibraryName { get; set; }
            public string? AssetLibraryFolderName { get; set; }
            public string? UnrealEnginePath { get; set; }
            public string? UnrealProjectPath { get; set; }
            public string? UnrealContentFolderPath { get; set; }
            public DateTime LastEditedAt { get; set; }
        }

        private sealed class ChapterMeta
        {
            public string? ChapterName { get; set; }

            public string? ChapterCode { get; set; }

            public string? ChapterType { get; set; }

            public DateTime LastEditedAt { get; set; }

            public int LastEditedRowIndex { get; set; }
        }

        private sealed class FolderBackupMeta
        {
            public DateTime CreatedAt { get; set; }

            public string? Note { get; set; }
        }

        private sealed class AssetLibraryMeta
        {
            public string? AssetLibraryName { get; set; }
            public string? ThumbnailFileName { get; set; }
            public DateTime LastEditedAt { get; set; }
        }
    }
}
