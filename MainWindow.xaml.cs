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
    public sealed partial class MainWindow : Window
    {
        private const string ToolsFolderName = "Tools";
        private const string NoStoryCharacterChoice = "__NO_STORY_CHARACTER__";
        private const string ChapterBackupsFolderName = "ChapterBackups";
        private const string ProjectBackupsFolderName = "ProjectBackups";
        private const string AssetLibraryBackupsFolderName = "AssetLibraryBackups";
        private const string UnrealAssetIndexTablesFolderName = "UnrealAssetIndexTables";
        private const double PageEntranceOffsetX = -96;
        private static readonly TimeSpan PageEntranceDuration = TimeSpan.FromMilliseconds(280);
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };
        private readonly AppSettingsService _appSettingsService = new();
        private readonly ProjectWorkspaceService _projectWorkspaceService = new();
        private readonly ProjectRootMigrationService _projectRootMigrationService = new();
        private readonly AudioAssetService _audioAssetService = new();
        private readonly BackgroundImageService _backgroundImageService = new();
        private readonly CharacterLayerAssetService _characterLayerAssetService = new();
        private readonly CharacterFilterService _characterFilterService = new();
        private readonly CharacterWorkspaceService _characterWorkspaceService;
        private readonly FolderBackupService _folderBackupService;
        private readonly UnrealSyncService _unrealSyncService = new();
        private readonly StoryCsvService _storyCsvService = new();
        private readonly StoryStateService _storyStateService = new();
        private readonly StoryEditorService _storyEditorService;
        private readonly StorySessionService _storySessionService;
        private readonly ProjectTextDataService _projectTextDataService;
        private readonly ProjectVoiceAssetService _projectVoiceAssetService = new();
        private readonly ChapterRepairService _chapterRepairService;
        private readonly StoryAssetIndexSyncService _storyAssetIndexSyncService;
        private readonly IDialogService _dialogService;
        private readonly IShortcutService _shortcutService;
        private readonly IUiSoundService _uiSoundService;
        private readonly StoryDialogService _storyDialogService;
        private readonly FunctionDialogService _functionDialogService;
        private readonly MediaPlayer _storyBgmPlayer = new();
        private readonly MediaPlayer _storyScenePlayer = new();

        private string _projectRootPath = AppSettingsService.DefaultProjectRootPath;
        private string? _selectedProjectThumbnailPath;
        private string? _selectedAssetLibraryThumbnailPath;
        private AssetLibraryInfo? _currentAssetLibrary;
        private readonly StoryEditorViewModel _storyEditorViewModel = new();
        private ChapterInfo? _currentStoryChapter
        {
            get => _storyEditorViewModel.Chapter;
            set => _storyEditorViewModel.Chapter = value;
        }

        private AssetLibraryInfo? _currentStoryAssetLibrary
        {
            get => _storyEditorViewModel.AssetLibrary;
            set => _storyEditorViewModel.AssetLibrary = value;
        }

        private List<StoryRow> _storyRows => _storyEditorViewModel.Rows;
        private Dictionary<string, int> _storyRowSections => _storyEditorViewModel.RowSections;
        private readonly List<ProjectTextRow> _projectTextRows = [];
        private ProjectVoiceMapState _projectVoiceMapState = new();
        private ProjectLocalizationState _projectLocalizationState = new();
        private ChapterInfo? _selectedVoiceChapter;
        private ChapterInfo? _selectedTextToolChapter;
        private string _selectedLocalizationLanguage = "English";
        private ProjectTextToolMode _projectTextToolMode = ProjectTextToolMode.Voice;
        private readonly Dictionary<string, BitmapImage> _storyPreviewImageCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _storyCharacterPreviewKeys = new();
        private int _currentStoryRowIndex
        {
            get => _storyEditorViewModel.CurrentRowIndex;
            set => _storyEditorViewModel.CurrentRowIndex = value;
        }

        private StoryCharacterSlotClipboard? _storyCharacterSlotClipboard;
        private StoryAssetClipboard? _storyAssetClipboard;
        private bool _isLoadingStoryRow
        {
            get => _storyEditorViewModel.IsLoadingRow;
            set => _storyEditorViewModel.IsLoadingRow = value;
        }

        private bool _isStoryRowDirty
        {
            get => _storyEditorViewModel.IsRowDirty;
            set => _storyEditorViewModel.IsRowDirty = value;
        }

        private bool _isUpdatingStoryRowIndexText
        {
            get => _storyEditorViewModel.IsUpdatingRowIndexText;
            set => _storyEditorViewModel.IsUpdatingRowIndexText = value;
        }

        private bool _isUpdatingStorySectionOptions
        {
            get => _storyEditorViewModel.IsUpdatingSectionOptions;
            set => _storyEditorViewModel.IsUpdatingSectionOptions = value;
        }

        private bool _isPersistingStoryRows
        {
            get => _storyEditorViewModel.IsPersistingRows;
            set => _storyEditorViewModel.IsPersistingRows = value;
        }

        private string? _currentStoryCsvPath
        {
            get => _storyEditorViewModel.CsvPath;
            set => _storyEditorViewModel.CsvPath = value;
        }
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
        private bool _isApplyingAssetLibraryMetadata;
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
        private CharacterLayerViewerState? _viewingCharacterLayer;
        private bool _isViewingCharacterComposite;
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
        private readonly DispatcherQueueTimer _globalProgressElapsedTimer;
        private double _storyPaneAnimationTargetWidth = 46;
        private readonly Dictionary<InfoBar, DispatcherQueueTimer> _storyTransientTipTimers = new();
        private List<StoryEditorUndoState> _storyUndoStack => _storyEditorViewModel.UndoStack;
        private readonly Stopwatch _globalProgressStopwatch = new();
        private string _globalProgressOperationTitle = string.Empty;
        private bool _isGlobalProgressVisible;
        private double _globalProgressLastPercent;
        private CancellationTokenSource? _globalProgressCancellation;
        private CancellationTokenSource? _assetLibraryLoadCancellation;
        private CancellationTokenSource? _characterDetailLoadCancellation;
        private bool _isWindowActive = true;
        private bool _refreshBackgroundImagesAfterDelay;
        private bool _isStoryDebugModeEnabled
        {
            get => _storyEditorViewModel.IsDebugModeEnabled;
            set => _storyEditorViewModel.IsDebugModeEnabled = value;
        }
        private bool _isChangingShellSelectionInternally;
        private const int MaxStoryUndoCount = 80;

        public MainWindow()
        {
            InitializeComponent();
            StoryEditorPage.DataContext = _storyEditorViewModel;
            _characterWorkspaceService = new CharacterWorkspaceService(_jsonOptions);
            _folderBackupService = new FolderBackupService(_jsonOptions);
            _uiSoundService = new UiSoundService();
            _dialogService = new WinUiDialogService(() => Content.XamlRoot, _uiSoundService);
            _shortcutService = new ShortcutService(_dialogService);
            _storyDialogService = new StoryDialogService(_dialogService);
            _functionDialogService = new FunctionDialogService(_dialogService, _uiSoundService);
            _storyEditorViewModel.ConfigureCommands(
                UndoStoryEditorOperation,
                NavigatePreviousStoryRow,
                NavigateNextStoryRow,
                InsertStoryRowHere,
                DeleteCurrentStoryRow,
                () => NavigateStorySection(-1),
                () => NavigateStorySection(1),
                AddStorySection,
                () => ChooseStoryAssetIndexAsync("更换背景图", "BGindex", GetStoryBackgroundChoices()),
                () => ChooseStoryAssetIndexAsync("更换BGM", "BGM", GetStoryBgmChoices()),
                () => ChooseStoryAssetIndexAsync("更换环境音", "Scene", GetStorySceneChoices()),
                ChooseStoryFunctionAsync,
                ShowCurrentStoryChoicesAsync,
                RemoveStoryFunctionAsync,
                ClearStoryFunction,
                ClearCurrentStoryRow);
            _storyEditorService = new StoryEditorService(_storyCsvService);
            _storySessionService = new StorySessionService(_storyCsvService, _storyStateService, _storyEditorService);
            _projectTextDataService = new ProjectTextDataService(_storyCsvService, _storySessionService, _jsonOptions);
            _chapterRepairService = new ChapterRepairService(_storyCsvService);
            _storyAssetIndexSyncService = new StoryAssetIndexSyncService(
                GetProjects,
                ResolveProjectAssetLibrary,
                GetProjectStoryCsvPaths,
                GetChaptersFolderPath,
                _projectWorkspaceService.ReadChapterInfo,
                _storyCsvService);
            DisableListItemEntranceTransitions();
            Activated += MainWindow_Activated;
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
            _globalProgressElapsedTimer = DispatcherQueue.CreateTimer();
            _globalProgressElapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _globalProgressElapsedTimer.Tick += GlobalProgressElapsedTimer_Tick;
            _storyBgmPlayer.IsLoopingEnabled = true;
            _storyScenePlayer.IsLoopingEnabled = true;
            RegisterAssetLibraryExpanderLazyLoading();
            RegisterCharacterDetailExpanderLazyLoading();

            _appSettings = _appSettingsService.Load();
            _uiSoundService.IsEnabled = _appSettings.UiSoundEnabled;
            ApplyLogSettingsToUi();
            ApplyUnrealSyncSettingsToUi();
            ApplyStoryTextFontSizeToUi();
            _projectRootPath = _appSettingsService.ResolveProjectRootPath(_appSettings);
            EnsureProjectRootDirectory(_projectRootPath);
            AppendLog(LogKind.Info, "程序启动，已检查整体项目目录。");
            ShowWorkbenchPage();
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        private void PlayPositiveSound()
        {
            _uiSoundService.Play(UiSoundKind.Positive);
        }

        private void PlayNegativeSound()
        {
            _uiSoundService.Play(UiSoundKind.Negative);
        }

        private void PlaySelectionSound()
        {
            _uiSoundService.Play(UiSoundKind.Selection);
        }

        private void DisableListItemEntranceTransitions()
        {
            GridView[] gridViews =
            [
                ProjectsGridView,
                ChaptersGridView,
                AssetLibrariesGridView,
                BackgroundImagesGridView,
                CharacterGridView,
                MusicGridView,
                AmbientSoundGridView,
                SoundEffectGridView,
                FunctionGridView,
                CharacterFilterGridView,
                CharacterClothGridView,
                CharacterFaceGridView,
                CharacterAdornGridView,
                CharacterVfxGridView,
                UnrealSyncProjectCardsGridView
            ];

            foreach (var gridView in gridViews)
            {
                gridView.ItemContainerTransitions = null;
            }
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
                UiSoundEnabledCheckBox.IsChecked = _appSettings.UiSoundEnabled;
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
            _appSettings.UiSoundEnabled = UiSoundEnabledCheckBox.IsChecked == true;
            _uiSoundService.IsEnabled = _appSettings.UiSoundEnabled;
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
            _appSettingsService.EnsureProjectRootDirectory(projectRootPath);

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
                RefreshAssetLibraryDetailSections(_currentAssetLibrary);
            }

            _refreshBackgroundImagesAfterDelay = false;
            AppendLog(LogKind.Info, "延迟刷新完成。");
        }

        private CancellationToken ResetAssetLibraryLoadCancellation()
        {
            _assetLibraryLoadCancellation?.Cancel();
            _assetLibraryLoadCancellation?.Dispose();
            _assetLibraryLoadCancellation = new CancellationTokenSource();
            return _assetLibraryLoadCancellation.Token;
        }

        private CancellationToken GetAssetLibraryLoadToken()
        {
            return _assetLibraryLoadCancellation?.Token ?? ResetAssetLibraryLoadCancellation();
        }

        private CancellationToken ResetCharacterDetailLoadCancellation()
        {
            _characterDetailLoadCancellation?.Cancel();
            _characterDetailLoadCancellation?.Dispose();
            _characterDetailLoadCancellation = new CancellationTokenSource();
            return _characterDetailLoadCancellation.Token;
        }

        private CancellationToken GetCharacterDetailLoadToken()
        {
            return _characterDetailLoadCancellation?.Token ?? ResetCharacterDetailLoadCancellation();
        }

        private async Task AddGridViewItemsInBatchesAsync<T>(
            GridView gridView,
            IReadOnlyList<T> items,
            Func<T, GridViewItem> createItem,
            CancellationToken cancellationToken,
            int batchSize = 12)
        {
            for (var index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                gridView.Items.Add(createItem(items[index]));

                if ((index + 1) % batchSize == 0)
                {
                    await Task.Yield();
                }
            }
        }

        private void RunAssetLibraryLoad(Task task)
        {
            _ = ObserveAssetLibraryLoadAsync(task);
        }

        private async Task ObserveAssetLibraryLoadAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // A newer asset-library load replaced this one.
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "素材库内容加载失败。", ex);
                if (AssetLibraryDetailStatusText is not null)
                {
                    AssetLibraryDetailStatusText.Text = $"素材库内容加载失败：{ex.Message}";
                }
            }
        }

        private void RunCharacterDetailLoad(Task task)
        {
            _ = ObserveCharacterDetailLoadAsync(task);
        }

        private async Task ObserveCharacterDetailLoadAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // A newer character-detail load replaced this one.
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "角色分层内容加载失败。", ex);
                CharacterDetailInfoBar.Severity = InfoBarSeverity.Error;
                CharacterDetailInfoBar.Title = "角色分层加载失败";
                CharacterDetailInfoBar.Message = ex.Message;
                CharacterDetailInfoBar.IsOpen = true;
            }
        }

        private void LoadAllCards()
        {
            LoadAssetLibraries();
            LoadProjects();
            LoadAssetLibraryOptions();
        }

        private List<AssetLibraryInfo> GetAssetLibraries()
        {
            return _projectWorkspaceService.GetAssetLibraries(_projectRootPath);
        }

        private List<ProjectInfo> GetProjects()
        {
            return _projectWorkspaceService.GetProjects(_projectRootPath);
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

        private GridViewItem CreateProjectCard(ProjectInfo project)
        {
            return DashboardCardFactory.CreateInfoCard(
                project,
                project.ThumbnailPath,
                project.Name,
                $"{project.Code} | 素材库：{project.AssetLibraryName}",
                $"上次打开时间 {project.LastEditedAt:yyyy-MM-dd HH:mm}",
                ProjectCard_Tapped,
                GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("重命名", async (_, _) => await RenameProjectAsync(project)),
                GridViewItemFactory.CreateMenuItem("更改目标素材库", async (_, _) => await ChangeProjectAssetLibraryAsync(project)),
                GridViewItemFactory.CreateMenuItem("打开文件夹", (_, _) => OpenFolderInExplorer(project.Path)),
                GridViewItemFactory.CreateMenuItem("导出", async (_, _) => await ExportProjectFromUiAsync(project)),
                GridViewItemFactory.CreateMenuItem("备份", async (_, _) => await BackupProjectFromUiAsync(project)),
                GridViewItemFactory.CreateMenuItem("还原", async (_, _) => await RestoreProjectFromUiAsync(project)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteProjectAsync(project))));
        }

        private GridViewItem CreateAssetLibraryCard(AssetLibraryInfo assetLibrary)
        {
            return DashboardCardFactory.CreateInfoCard(
                assetLibrary,
                assetLibrary.ThumbnailPath,
                assetLibrary.Name,
                "素材合集",
                $"上次编辑时间 {assetLibrary.LastEditedAt:yyyy-MM-dd HH:mm}",
                AssetLibraryCard_Tapped,
                GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("重命名", async (_, _) => await RenameAssetLibraryAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("打开文件夹", (_, _) => OpenFolderInExplorer(assetLibrary.Path)),
                GridViewItemFactory.CreateMenuItem("导出", async (_, _) => await ExportAssetLibraryFromUiAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("备份", async (_, _) => await BackupAssetLibraryFromUiAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("还原", async (_, _) => await RestoreAssetLibraryFromUiAsync(assetLibrary)),
                GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteAssetLibraryAsync(assetLibrary))));
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
                    progress => Task.Run(() => _folderBackupService.CreateBackup(project.Path, ProjectBackupsFolderName, project.Code, note, progress, GetGlobalProgressCancellationToken())));
                LoadProjects();
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"备份项目：{project.Name} -> {backup.Path}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "备份项目失败。", ex);
            }
        }

        private async Task ExportProjectFromUiAsync(ProjectInfo project)
        {
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"{SanitizeBackupFileName(project.Code)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };
                picker.FileTypeChoices.Add("TFACStorybox 项目包", [".zip"]);
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

                var exportFile = await picker.PickSaveFileAsync();
                if (exportFile is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(exportFile.Path))
                {
                    AppendLog(LogKind.Warning, "导出项目失败：请选择本机文件夹里的保存位置。");
                    return;
                }

                var export = await ShowFolderBackupProgressDialogAsync(
                    "正在导出项目",
                    project.Name,
                    progress => Task.Run(() => _folderBackupService.ExportToZip(project.Path, exportFile.Path, progress, GetGlobalProgressCancellationToken())));
                AppendLog(LogKind.User, $"导出项目：{project.Name} -> {export.Path}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "导出项目失败。", ex);
            }
        }

        private async Task RestoreProjectFromUiAsync(ProjectInfo project)
        {
            try
            {
                var backups = _folderBackupService.GetBackups(project.Path, ProjectBackupsFolderName);
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

                ShowGlobalProgress("还原项目", project.Name);
                UpdateGlobalProgress("正在还原项目备份...", 15, selectedBackup.DisplayName);
                await Task.Run(() => _folderBackupService.Restore(project.Path, ProjectBackupsFolderName, selectedBackup));
                CompleteGlobalProgress("项目还原完成", selectedBackup.DisplayName);
                await HideGlobalProgressAfterDelayAsync();
                LoadProjects();
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"还原项目：{project.Name} <- {selectedBackup.Path}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("项目还原失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
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
                    progress => Task.Run(() => _folderBackupService.CreateBackup(assetLibrary.Path, AssetLibraryBackupsFolderName, assetLibrary.FolderName, note, progress, GetGlobalProgressCancellationToken())));
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

        private async Task ExportAssetLibraryFromUiAsync(AssetLibraryInfo assetLibrary)
        {
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"{SanitizeBackupFileName(assetLibrary.FolderName)}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };
                picker.FileTypeChoices.Add("TFACStorybox 素材库包", [".zip"]);
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

                var exportFile = await picker.PickSaveFileAsync();
                if (exportFile is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(exportFile.Path))
                {
                    AppendLog(LogKind.Warning, "导出素材库失败：请选择本机文件夹里的保存位置。");
                    return;
                }

                var export = await ShowFolderBackupProgressDialogAsync(
                    "正在导出素材库",
                    assetLibrary.Name,
                    progress => Task.Run(() => _folderBackupService.ExportToZip(assetLibrary.Path, exportFile.Path, progress, GetGlobalProgressCancellationToken())));
                AppendLog(LogKind.User, $"导出素材库：{assetLibrary.Name} -> {export.Path}");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, "导出素材库失败。", ex);
            }
        }

        private async Task RestoreAssetLibraryFromUiAsync(AssetLibraryInfo assetLibrary)
        {
            try
            {
                var backups = _folderBackupService.GetBackups(assetLibrary.Path, AssetLibraryBackupsFolderName);
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

                ShowGlobalProgress("还原素材库", assetLibrary.Name);
                UpdateGlobalProgress("正在还原素材库备份...", 15, selectedBackup.DisplayName);
                await Task.Run(() => _folderBackupService.Restore(assetLibrary.Path, AssetLibraryBackupsFolderName, selectedBackup));
                CompleteGlobalProgress("素材库还原完成", selectedBackup.DisplayName);
                await HideGlobalProgressAfterDelayAsync();
                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"还原素材库：{assetLibrary.Name} <- {selectedBackup.Path}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("素材库还原失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                AppendLog(LogKind.Error, "还原素材库失败。", ex);
            }
        }

        private GridViewItem CreateAddCard(string title, string subtitle, TappedEventHandler tappedHandler)
        {
            return DashboardCardFactory.CreateAddCard(title, subtitle, tappedHandler);
        }

        private async Task LoadStoryPreviewImageAsync(Image image, string imagePath)
        {
            try
            {
                image.Source = await GetCachedStoryPreviewImageAsync(imagePath);
            }
            catch
            {
                image.Source = ThumbnailFactory.CreateDefaultBitmap();
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
            PlayPositiveSound();
            ShowCreateProjectPage();
            AppendLog(LogKind.User, "打开创建项目页面。");
            e.Handled = true;
        }

        private void AddAssetLibraryCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PlayPositiveSound();
            ShowCreateAssetLibraryPage();
            AppendLog(LogKind.User, "打开创建素材库页面。");
            e.Handled = true;
        }

        private void ProjectCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is GridViewItem { Tag: ProjectInfo project })
            {
                PlaySelectionSound();
                TouchProjectLastEditedAt(project);
                ShowProjectDetailPage(_projectWorkspaceService.ReadProjectInfo(project.Path));
                RequestDelayedRefresh();
                AppendLog(LogKind.User, $"打开项目：{project.Name}");
                e.Handled = true;
            }
        }

        private void AssetLibraryCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is GridViewItem { Tag: AssetLibraryInfo assetLibrary })
            {
                PlaySelectionSound();
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
            LoadProjectTextPanels(project);
            ProjectDetailInfoText.Text = CreateProjectDetailInfoText(project, CountProjectStoryCharacters());

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Visible;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(ProjectDetailPage);
            ProjectDetailCloseButton.Focus(FocusState.Programmatic);
        }

        private static string CreateProjectDetailInfoText(ProjectInfo project, int? storyCharacterCount = null)
        {
            var storyText = storyCharacterCount is null
                ? string.Empty
                : $" | 剧情总字数：{storyCharacterCount.Value:N0}";
            return $"项目名字：{project.Name} | 英文代号：{project.Code} | 关联素材库：{project.AssetLibraryName} | 上次打开时间：{project.LastEditedAt:yyyy-MM-dd HH:mm}{storyText}";
        }

        private void CloseProjectDetailButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            ShowWorkbenchPage();
        }

        private async void SaveProjectInlineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            PlayPositiveSound();
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
                ShowProjectDetailPage(_projectWorkspaceService.ReadProjectInfo(_currentProject.Path));
            }
        }

        private void LoadChapters(ProjectInfo project)
        {
            List<ChapterInfo> chapters;
            try
            {
                var chaptersFolderPath = GetChaptersFolderPath(project);
                Directory.CreateDirectory(chaptersFolderPath);
                chapters = _projectWorkspaceService.GetChapters(project)
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

        private void LoadProjectTextPanels(ProjectInfo project)
        {
            var chapters = _projectWorkspaceService.GetChapters(project)
                .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _projectTextRows.Clear();
            _projectTextRows.AddRange(_projectTextDataService.LoadTextRows(project, chapters));
            _projectVoiceMapState = _projectTextDataService.ReadVoiceMap(project);
            _projectLocalizationState = _projectTextDataService.ReadLocalization(project);
            if (_selectedVoiceChapter is null || !chapters.Any(chapter => PathsEqual(chapter.Path, _selectedVoiceChapter.Path)))
            {
                _selectedVoiceChapter = chapters.FirstOrDefault();
            }

            if (_selectedTextToolChapter is null || !chapters.Any(chapter => PathsEqual(chapter.Path, _selectedTextToolChapter.Path)))
            {
                _selectedTextToolChapter = _selectedVoiceChapter;
            }

            RefreshProjectTextEntryCards(chapters);
        }

        private void RefreshProjectTextEntryCards(IReadOnlyList<ChapterInfo> chapters)
        {
            ProjectVoiceCardsGridView.Items.Clear();
            ProjectLocalizationCardsGridView.Items.Clear();
            var textCount = _projectTextRows.Count;
            ProjectVoiceSummaryText.Text = chapters.Count == 0
                ? "新建章节后会同步生成语音入口卡。"
                : $"已聚合 {chapters.Count} 个章节、{textCount} 行文本。点击卡片进入语音映射页面。";
            ProjectLocalizationSummaryText.Text = chapters.Count == 0
                ? "新建章节后会同步生成本地化入口卡。"
                : $"当前语言 {_selectedLocalizationLanguage}，已聚合 {textCount} 行文本。点击卡片进入本地化页面。";

            foreach (var chapter in chapters)
            {
                var chapterTextCount = _projectTextRows.Count(row => string.Equals(row.ChapterCode, chapter.Code, StringComparison.OrdinalIgnoreCase));
                ProjectVoiceCardsGridView.Items.Add(CreateProjectTextEntryCard(
                    chapter,
                    "语音",
                    $"{chapter.Code} | 文本 {chapterTextCount} 行",
                    () => OpenProjectTextTool(ProjectTextToolMode.Voice, chapter)));
                ProjectLocalizationCardsGridView.Items.Add(CreateProjectTextEntryCard(
                    chapter,
                    "本地化",
                    $"{chapter.Code} | {_selectedLocalizationLanguage} | 文本 {chapterTextCount} 行",
                    () => OpenProjectTextTool(ProjectTextToolMode.Localization, chapter)));
            }
        }

        private int CountProjectStoryCharacters()
        {
            return ProjectTextDataService.CountStoryCharacters(_projectTextRows);
        }

        private GridViewItem CreateProjectTextEntryCard(ChapterInfo chapter, string titleSuffix, string subtitle, Action open)
        {
            return DashboardCardFactory.CreateInfoCard(
                chapter,
                null,
                $"{chapter.Name} {titleSuffix}",
                subtitle,
                $"上次编辑时间 {chapter.LastEditedAt:yyyy-MM-dd HH:mm}",
                (_, _) =>
                {
                    PlaySelectionSound();
                    open();
                });
        }

        private void OpenProjectTextTool(ProjectTextToolMode mode, ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                return;
            }

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
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            RefreshProjectTextToolPage();
            PlayPageEntrance(ProjectTextToolPage);
        }

        private void BackToProjectDetailFromTextToolButton_Click(object sender, RoutedEventArgs e)
        {
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
            _selectedTextToolChapter = selected;
            _selectedVoiceChapter = selected;
            RefreshProjectTextToolPage();
        }

        private void ReloadProjectTextToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            PlaySelectionSound();
            var chapters = _projectWorkspaceService.GetChapters(_currentProject)
                .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _projectTextRows.Clear();
            _projectTextRows.AddRange(_projectTextDataService.LoadTextRows(_currentProject, chapters));
            _projectVoiceMapState = _projectTextDataService.ReadVoiceMap(_currentProject);
            _projectLocalizationState = _projectTextDataService.ReadLocalization(_currentProject);
            RefreshProjectTextToolPage();
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
            ProjectTextToolSwitchLanguageButton.Visibility = _projectTextToolMode == ProjectTextToolMode.Localization
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (_selectedTextToolChapter is null)
            {
                ProjectTextToolTitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice ? "文本语音" : "文本本地化";
                ProjectTextToolSubtitleText.Text = "当前项目还没有章节。";
                ProjectTextToolTablePanel.Children.Add(CreateProjectTextEmptyRow("暂无可显示文本。"));
                return;
            }

            var rows = _projectTextRows
                .Where(row => string.Equals(row.ChapterCode, _selectedTextToolChapter.Code, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ProjectTextToolTitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice
                ? $"文本语音 {_selectedTextToolChapter.Name}"
                : $"文本本地化 {_selectedTextToolChapter.Name}";
            ProjectTextToolSubtitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice
                ? $"当前章节：{_selectedTextToolChapter.Code}，文本 {rows.Count} 行。右侧点击选择语音文件，仅支持 wav。"
                : $"当前语言：{_selectedLocalizationLanguage}，章节：{_selectedTextToolChapter.Code}，文本 {rows.Count} 行。";
            ProjectTextToolTablePanel.Children.Add(CreateProjectTextTableHeader(
                _projectTextToolMode == ProjectTextToolMode.Voice ? "语音文件" : _selectedLocalizationLanguage));
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
                    ProjectTextToolTablePanel.Children.Add(CreateProjectTextVoiceTableRow(row, value, index + 1, rows.Count));
                }
                else
                {
                    var languageMap = GetCurrentLocalizationLanguageMap();
                    var value = languageMap.TryGetValue(row.Id, out var localizedText) ? localizedText : string.Empty;
                    ProjectTextToolTablePanel.Children.Add(CreateProjectTextLocalizationTableRow(row, value, languageMap));
                }
            }
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

        private async Task SetProjectVoiceRemarkAsync(ProjectTextRow row, int rowNumber, int rowCount, Button? button = null)
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
                rowNumber,
                rowCount,
                remarkInput,
                _projectVoiceMapState);
            _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
            TouchProjectLastEditedAt(_currentProject);
            if (button is not null)
            {
                UpdateProjectVoiceButton(button, updatedPath);
            }
            else
            {
                RefreshProjectTextToolPage();
            }

            AppendLog(LogKind.User, $"已更改语音备注：{Path.GetFileName(updatedPath)}");
        }

        private async Task RemoveProjectVoiceAsync(ProjectTextRow row, Button? button = null)
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

            var removedPath = _projectVoiceAssetService.RemoveVoice(_currentProject, row, _projectVoiceMapState);
            _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
            TouchProjectLastEditedAt(_currentProject);
            if (button is not null)
            {
                UpdateProjectVoiceButton(button, string.Empty);
            }
            else
            {
                RefreshProjectTextToolPage();
            }

            AppendLog(LogKind.User, $"已移除语音映射：{Path.GetFileName(removedPath ?? voicePath)}");
        }

        private static UIElement CreateProjectTextTableHeader(string valueHeader)
        {
            var grid = CreateProjectTextGrid();
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);
            grid.Children.Add(CreateProjectTextCell("文本", 0, isHeader: true));
            grid.Children.Add(CreateProjectTextCell(valueHeader, 1, isHeader: true));
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

        private Grid CreateProjectTextVoiceTableRow(ProjectTextRow row, string value, int rowNumber, int rowCount)
        {
            var grid = CreateProjectTextGrid();
            grid.Children.Add(CreateProjectTextSourceCell(row));
            var hasVoice = !string.IsNullOrWhiteSpace(value);
            var button = new Button
            {
                MinWidth = 520,
                Margin = new Thickness(8),
                Tag = row.Id
            };
            UpdateProjectVoiceButton(button, hasVoice ? value : string.Empty);
            button.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("更改备注", async (_, _) => await SetProjectVoiceRemarkAsync(row, rowNumber, rowCount, button)),
                GridViewItemFactory.CreateMenuItem("移除", async (_, _) => await RemoveProjectVoiceAsync(row, button)));
            button.Click += async (_, _) =>
            {
                var selectedPath = await PickReplacementFileAsync([".wav"], PickerLocationId.MusicLibrary);
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                if (_currentProject is not null)
                {
                    selectedPath = _projectVoiceAssetService.ImportVoice(
                        _currentProject,
                        row,
                        rowNumber,
                        rowCount,
                        selectedPath,
                        _projectVoiceMapState);
                    _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
                    TouchProjectLastEditedAt(_currentProject);
                }

                UpdateProjectVoiceButton(button, selectedPath);
                PlaySelectionSound();
            };
            grid.Children.Add(CreateProjectTextScrollableCell(button, 1));
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

        private static Grid CreateProjectTextGrid()
        {
            var grid = new Grid
            {
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            return grid;
        }

        private static string CreateProjectTextSourceContent(ProjectTextRow row)
        {
            return $"{row.Text}\n{row.ChapterCode} / 小节 {row.Section} / 行 {row.RowName}";
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
                Text = $"{row.ChapterCode} / 小节 {row.Section} / 行 {row.RowName}",
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
            button.Content = hasVoice ? Path.GetFileName(voicePath) : "点击选择语音文件（仅支持 wav）";
            button.Foreground = Application.Current.Resources[
                hasVoice ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"] as Brush;
            button.FontWeight = hasVoice
                ? Microsoft.UI.Text.FontWeights.Normal
                : Microsoft.UI.Text.FontWeights.Normal;
            button.Opacity = hasVoice ? 1 : 0.72;
            ToolTipService.SetToolTip(button, hasVoice ? voicePath : "未设置语音文件");
        }

        private static Border CreateProjectTextCell(string text, int column, bool isHeader = false)
        {
            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = column == 0 ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = Application.Current.Resources[isHeader ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"] as Brush
                }
            };
            Grid.SetColumn(border, column);
            return border;
        }

        private static Border CreateProjectTextScrollableCell(FrameworkElement content, int column)
        {
            var border = new Border
            {
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = column == 0 ? new Thickness(0, 0, 1, 0) : new Thickness(0),
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
                GridViewItemFactory.CreateMenuItem("导入小节", async (_, _) => await ImportStorySectionsFromUiAsync(chapter)),
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
                _projectWorkspaceService.CreateChapter(_currentProject, input);
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
                _projectWorkspaceService.UpdateChapter(_currentProject, chapter, input);
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
                BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(assetLibrary)).Count,
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

        private void ProjectsGridView_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "导入项目包";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        }

        private async void ProjectsGridView_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var archivePaths = items
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Where(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (archivePaths.Count == 0)
                {
                    WorkspaceStatusText.Text = "没有可导入的项目包：请拖入项目导出的 .zip 文件。";
                    AppendLog(LogKind.Warning, "项目导入失败：拖入内容里没有 .zip 项目包。");
                    return;
                }

                EnsureProjectRootDirectory(_projectRootPath);
                var importedProjects = new List<ProjectInfo>();
                ShowGlobalProgress("导入项目包", $"0 / {archivePaths.Count}");
                for (var index = 0; index < archivePaths.Count; index++)
                {
                    GetGlobalProgressCancellationToken().ThrowIfCancellationRequested();
                    var archivePath = archivePaths[index];
                    var percent = archivePaths.Count == 0 ? 0 : index * 100d / archivePaths.Count;
                    UpdateGlobalProgress(
                        $"正在导入项目包 {index + 1}/{archivePaths.Count}",
                        percent,
                        Path.GetFileName(archivePath));
                    importedProjects.Add(await Task.Run(() => _projectWorkspaceService.ImportProjectArchive(_projectRootPath, archivePath, GetGlobalProgressCancellationToken())));
                }
                CompleteGlobalProgress("项目导入完成", $"已导入 {importedProjects.Count} 个项目");
                await HideGlobalProgressAfterDelayAsync();

                LoadProjects();
                RequestDelayedRefresh();
                WorkspaceStatusText.Text = $"已导入 {importedProjects.Count} 个项目：{string.Join("、", importedProjects.Select(project => project.Name).Take(3))}";
                AppendLog(LogKind.User, $"拖动导入项目：{string.Join("；", importedProjects.Select(project => $"{project.Name} -> {project.Path}"))}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "项目导入已取消" : "项目导入失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                WorkspaceStatusText.Text = $"导入项目失败：{ex.Message}";
                AppendLog(LogKind.Error, "拖动导入项目失败。", ex);
            }
        }

        private void AssetLibrariesGridView_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "导入素材库包";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        }

        private async void AssetLibrariesGridView_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var archivePaths = items
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Where(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (archivePaths.Count == 0)
                {
                    AssetLibraryStatusText.Text = "没有可导入的素材库包：请拖入素材库导出的 .zip 文件。";
                    AppendLog(LogKind.Warning, "素材库导入失败：拖入内容里没有 .zip 素材库包。");
                    return;
                }

                EnsureProjectRootDirectory(_projectRootPath);
                var importedLibraries = new List<AssetLibraryInfo>();
                ShowGlobalProgress("导入素材库包", $"0 / {archivePaths.Count}");
                for (var index = 0; index < archivePaths.Count; index++)
                {
                    GetGlobalProgressCancellationToken().ThrowIfCancellationRequested();
                    var archivePath = archivePaths[index];
                    var percent = archivePaths.Count == 0 ? 0 : index * 100d / archivePaths.Count;
                    UpdateGlobalProgress(
                        $"正在导入素材库包 {index + 1}/{archivePaths.Count}",
                        percent,
                        Path.GetFileName(archivePath));
                    importedLibraries.Add(await Task.Run(() => _projectWorkspaceService.ImportAssetLibraryArchive(_projectRootPath, archivePath, GetGlobalProgressCancellationToken())));
                }
                CompleteGlobalProgress("素材库导入完成", $"已导入 {importedLibraries.Count} 个素材库");
                await HideGlobalProgressAfterDelayAsync();

                LoadAssetLibraries();
                LoadProjects();
                LoadAssetLibraryOptions();
                RequestDelayedRefresh();
                AssetLibraryStatusText.Text = $"已导入 {importedLibraries.Count} 个素材库：{string.Join("、", importedLibraries.Select(library => library.Name).Take(3))}";
                AppendLog(LogKind.User, $"拖动导入素材库：{string.Join("；", importedLibraries.Select(library => $"{library.Name} -> {library.Path}"))}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "素材库导入已取消" : "素材库导入失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
                AssetLibraryStatusText.Text = $"导入素材库失败：{ex.Message}";
                AppendLog(LogKind.Error, "拖动导入素材库失败。", ex);
            }
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

            var compatibility = _storyCsvService.InspectCompatibility(sourceCsvPath);
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

            var rows = _storyCsvService.ReadRows(sourceCsvPath);
            if (rows.Count == 0)
            {
                rows.Add(_storyCsvService.CreateDefaultRow());
            }

            var chapterCode = importInput.Code;
            var chapterPath = Path.Combine(GetChaptersFolderPath(_currentProject), SanitizeCharacterFolderName(chapterCode));
            if (Directory.Exists(chapterPath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "无法导入 CSV", $"同名章节代号已存在：{chapterCode}");
                return false;
            }

            _projectWorkspaceService.CreateImportedChapter(_currentProject, importInput);

            var targetCsvPath = Path.Combine(chapterPath, $"{chapterCode}.csv");
            _storyCsvService.WriteRows(targetCsvPath, rows);

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
                StoryDialogContentFactory.CreateStoryCsvCompatibilityContent(sourceCsvPath, compatibility));
        }

        private async Task ShowCsvCompatibilityFailedDialogAsync(string sourceCsvPath, StoryCsvCompatibility compatibility)
        {
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "CSV 结构不兼容",
                StoryDialogContentFactory.CreateStoryCsvCompatibilityContent(sourceCsvPath, compatibility),
                string.Empty,
                "知道了",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None));
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
            _currentStoryCsvPath = _storyCsvService.GetChapterCsvPath(chapter);
            ClearStoryUndoStack();
            UpdateStoryDebugModeUi();
            _storyBackgroundPreviewKey = null;
            _storyCharacterPreviewKeys.Clear();
            _storyBgmPlaybackSuppressed = false;
            ApplyStoryTextFontSizeToUi();
            UpdateStoryEditorHeader();
            _storyEditorViewModel.AssetStatusText = _currentStoryAssetLibrary is null
                ? "当前项目未关联素材库。"
                : $"素材库：{_currentStoryAssetLibrary.Name}";

            LoadStoryRowsFromSectionFiles(chapter);
            NormalizeStoryCharacterCodes();
            SynchronizeStorySectionState();
            PersistCurrentStoryRowsToFiles();

            _currentStoryRowIndex = Math.Clamp(chapter.LastEditedRowIndex, 0, _storyRows.Count - 1);
            _storyEditorViewModel.RefreshCommandStates();
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
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(StoryEditorPage);
            StoryEditorBackButton.Focus(FocusState.Programmatic);
            StoryEditorPage.Focus(FocusState.Programmatic);
            _ = WarmStoryPreviewImageCacheAsync();
            AppendLog(LogKind.User, $"打开章节编辑器：{chapter.Name}（{chapter.Code}）");
        }

        private void CloseStoryEditorButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
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

            _storyEditorViewModel.RefreshCommandStates();
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
                _currentStoryChapter is null ? null : StoryStateService.CloneChoiceNotes(_storyStateService.ReadChoiceNotes(_currentStoryChapter)));
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
            _storyEditorViewModel.RefreshUndoState();
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
                _storyStateService.WriteChoiceNotes(_currentStoryChapter, StoryStateService.CloneChoiceNotes(state.ChoiceNotes));
                RemoveUnusedStoryChoiceNotes(state.ChoiceNotes.Choices.Keys);
            }

            ApplyStorySectionsInRowOrder(GetStorySectionsInRowOrder());
            PersistCurrentStoryRowsToFiles();
            SaveCurrentChapterProgress();
            ClearStoryFunctionTips();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            LoadStoryRowIntoUi();
            UpdateStoryUndoState();
            _storyEditorViewModel.RefreshCommandStates();
            ShowStoryStatus(InfoBarSeverity.Success, "已撤回", state.Description);
            AppendLog(LogKind.User, $"撤回编辑器操作：{state.Description}");
        }

        private static List<StoryRow> CloneStoryRows(IEnumerable<StoryRow> rows)
        {
            return StoryEditorService.CloneRows(rows);
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
                _storyEditorViewModel.SpeakerText = row.Get("TalkChar");
                _storyEditorViewModel.StoryText = row.Get("Tesxt");
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
            var speakerName = NormalizeStoryCharacterNameForCsv(_storyEditorViewModel.SpeakerText);
            if (!string.Equals(row.Get("TalkChar"), speakerName, StringComparison.Ordinal))
            {
                row.Set("TalkChar", speakerName);
                changed = true;
            }

            if (!string.Equals(row.Get("Tesxt"), _storyEditorViewModel.StoryText, StringComparison.Ordinal))
            {
                row.Set("Tesxt", _storyEditorViewModel.StoryText);
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
                    _storyEditorViewModel.RowIndexText = (_currentStoryRowIndex + 1).ToString();
                    _storyEditorViewModel.RowTotalText = $"/ {_storyRows.Count} 句";
                }
                else
                {
                    var sectionInfo = _storyEditorViewModel.GetCurrentSectionPositionInfo();
                    _storyEditorViewModel.RowIndexText = sectionInfo.LocalIndex.ToString();
                    _storyEditorViewModel.RowTotalText = $"/ {sectionInfo.Total} 句";
                }

            }
            finally
            {
                _isUpdatingStoryRowIndexText = false;
            }
        }

        private void SaveCurrentChapterProgress()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            _projectWorkspaceService.SaveChapterProgress(_currentStoryChapter, _currentStoryRowIndex);
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

        private void NavigatePreviousStoryRow()
        {
            if (_storyRows.Count == 0 || _currentStoryRowIndex <= 0)
            {
                return;
            }

            NavigateToStoryRow(_currentStoryRowIndex - 1, rebuildPersistentState: true);
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
            if (_currentStoryRowIndex >= _storyRows.Count - 1)
            {
                if (_isStoryDebugModeEnabled)
                {
                    ShowStoryStatus(InfoBarSeverity.Informational, "调试模式", "已经是最后一句，调试模式下不会新建句子。");
                    return;
                }

                CaptureStoryUndoState("新建下一句");
            }

            var result = _storyEditorService.MoveNextOrCreate(_storyRows, _storyRowSections, _currentStoryRowIndex);
            _currentStoryRowIndex = result.RowIndex;
            SaveCurrentChapterProgress();
            _storyEditorViewModel.RefreshCommandStates();
            if (result.Changed)
            {
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
            var result = _storyEditorService.InsertAtCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex);
            _currentStoryRowIndex = result.RowIndex;
            PersistCurrentStoryRowsToFiles();
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            _storyEditorViewModel.RefreshCommandStates();
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

        private void DeleteCurrentStoryRow()
        {
            if (_storyRows.Count == 0 || _currentStoryCsvPath is null)
            {
                return;
            }

            var removedChoices = GetCurrentStoryChoiceValues();
            CaptureStoryUndoState("删除当前句");
            var result = _storyEditorService.DeleteCurrent(_storyRows, _storyRowSections, _currentStoryRowIndex, removedChoices);
            _currentStoryRowIndex = result.RowIndex;
            PersistCurrentStoryRowsToFiles();
            RemoveUnusedStoryChoiceNotes(result.RemovedChoiceValues);
            SaveCurrentChapterProgress();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            _storyEditorViewModel.RefreshCommandStates();
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

        private void AddStorySection()
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
            _storyEditorViewModel.RefreshCommandStates();
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
            return _storyEditorViewModel.GetCurrentSection();
        }

        private int GetStorySectionAtRowIndex(int rowIndex)
        {
            return _storyEditorViewModel.GetSectionAtRowIndex(rowIndex);
        }

        private void SetCurrentStorySection(int section)
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            _storyEditorViewModel.SetCurrentSection(section);
            EnsureStorySectionOptions(section);
        }

        private bool SetCurrentStorySectionIfChanged(int section)
        {
            if (_storyRows.Count == 0)
            {
                return false;
            }

            var changed = _storyEditorViewModel.SetCurrentSectionIfChanged(section);
            EnsureStorySectionOptions(section);
            return changed;
        }

        private void LoadStoryRowsFromSectionFiles(ChapterInfo chapter)
        {
            _storyRows.Clear();
            _storyRowSections.Clear();
            var result = _storySessionService.LoadRowsFromSectionFiles(chapter);
            _storyRows.AddRange(result.Rows);
            foreach (var pair in result.Sections)
            {
                _storyRowSections[pair.Key] = pair.Value;
            }

            _storyEditorViewModel.RefreshCollectionState();
            _storyEditorViewModel.RefreshCommandStates();

            if (result.RemovedEmptySectionCount > 0)
            {
                ShowStoryStatus(InfoBarSeverity.Informational, "已清理空小节", $"检测并删除 {result.RemovedEmptySectionCount} 个空小节 CSV。");
            }
        }

        private void ImportLooseStorySectionCsvFiles(ChapterInfo chapter)
        {
            if (_currentStoryCsvPath is null || !Directory.Exists(chapter.Path))
            {
                return;
            }

            var looseCsvPaths = _storySessionService.GetLooseSectionCsvPaths(chapter);
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
            var result = _storySessionService.ImportSectionCsvFiles(chapter, csvPaths, deleteSourceFiles);
            foreach (var log in result.Logs)
            {
                AppendLog(log.Kind, log.Message);
            }

            if (result.Changed)
            {
                if (_currentStoryChapter is not null && PathsEqual(_currentStoryChapter.Path, chapter.Path))
                {
                    LoadStoryRowsFromSectionFiles(chapter);
                    SynchronizeStorySectionState();
                    WriteStorySectionState(chapter.Path, _storyRowSections);
                }
            }

            return result.ImportedCount;
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
                var result = _storySessionService.PersistRowsToSectionFiles(_currentStoryChapter, _currentStoryCsvPath, _storyRows, _storyRowSections);

                if (showStatus)
                {
                    ShowStoryStatus(InfoBarSeverity.Success, "小节 CSV 已更新", $"已按 {result.ActiveCsvCount} 个小节文件保存。");
                }
            }
            finally
            {
                _isPersistingStoryRows = false;
            }
        }

        private void SynchronizeStorySectionState()
        {
            _storyEditorService.SynchronizeSections(_storyRows, _storyRowSections);
        }

        private List<int> GetStorySectionsInRowOrder()
        {
            return _storyEditorService.GetSectionsInRowOrder(_storyRows, _storyRowSections);
        }

        private void ApplyStorySectionsInRowOrder(IReadOnlyList<int> sections)
        {
            _storyEditorService.ApplySectionsInRowOrder(_storyRows, _storyRowSections, sections);
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
            _storyStateService.WriteSectionState(chapterPath, sections);
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
            return Task.CompletedTask;
        }

        private static string BuildSectionCsvBaseName(string chapterCode)
        {
            return StoryCsvService.BuildSectionCsvBaseName(chapterCode);
        }

        private static string BuildSectionCsvChapterBaseName(string chapterCode)
        {
            return StoryCsvService.BuildSectionCsvChapterBaseName(chapterCode);
        }

        private static string BuildSectionCsvFileBaseName(string chapterCode, int section)
        {
            return StoryCsvService.BuildSectionCsvFileBaseName(chapterCode, section);
        }

        private string BuildNextStoryChoiceFunctionIndicator()
        {
            if (_currentStoryChapter is null)
            {
                return "Choice1";
            }

            var prefix = BuildCurrentStoryChapterSectionChoicePrefix();
            var maxChoiceIndex = _storyRows
                .SelectMany(row => StoryFunctionService.SplitFunctionValues(row.Get("Custom")))
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
            var functions = StoryFunctionService.SplitFunctionValues(row.Get("Custom")).ToList();
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

        private void UpdateStoryEditorHeader()
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            _storyEditorViewModel.Title = $"{_currentStoryChapter.Name} / {BuildCurrentStoryChapterSectionCode()}";
        }

        private void UpdateStoryToolbarCurrentInfo()
        {
            if (_storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyEditorViewModel.CurrentBackgroundText = FormatStoryAssetStatus("当前背景图", ParseInt(row.Get("BGindex")), GetStoryBackgroundChoices());
            _storyEditorViewModel.CurrentBgmText = FormatStoryAssetStatus("当前BGM", ParseInt(row.Get("BGM")), GetStoryBgmChoices());
            _storyEditorViewModel.CurrentSceneText = FormatStoryAssetStatus("当前环境音", ParseInt(row.Get("Scene")), GetStorySceneChoices());
            var custom = row.Get("Custom").Trim();
            var hasFunction = !string.IsNullOrWhiteSpace(custom);
            _storyEditorViewModel.CurrentFunctionText = string.IsNullOrWhiteSpace(custom)
                ? "当前函数：无"
                : $"当前函数：{custom}";
            _storyEditorViewModel.HasCurrentFunction = hasFunction;
            _storyEditorViewModel.HasMultipleCurrentFunctions = StoryFunctionService.SplitFunctionValues(custom).Skip(1).Any();
            _storyEditorViewModel.HasCurrentChoices = GetCurrentStoryChoiceValues().Count > 0;
        }

        private static string FormatStoryAssetStatus(string label, int rawIndex, IReadOnlyList<StoryAssetChoice> choices)
        {
            var resolvedIndex = ResolveStoryAssetIndex(rawIndex, choices.Count);
            return resolvedIndex is null
                ? $"{label}：无"
                : $"{label}：{choices[resolvedIndex.Value].Index}: {choices[resolvedIndex.Value].Name}";
        }

        private void ClearStoryFunction()
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

        private void ClearCurrentStoryRow()
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var currentName = _storyRows[_currentStoryRowIndex].Get("Name");
            var removedChoices = GetCurrentStoryChoiceValues();
            CaptureStoryUndoState("清空当前行数据");
            var row = _storyCsvService.CreateDefaultRow();
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

            var selected = await _storyDialogService.SelectSimpleChoiceAsync(
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
            var functions = StoryFunctionService.SplitFunctionValues(row.Get("Custom")).ToList();
            if (functions.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可移除函数", "当前句还没有填写函数。");
                return;
            }

            var selected = await _storyDialogService.SelectSimpleChoiceAsync(
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
            return _currentStoryAssetLibrary is null ? [] : StoryFunctionService.ReadFunctions(_currentStoryAssetLibrary, _jsonOptions);
        }

        private string FormatStoryFunctionChoiceDisplay(FunctionEntry function)
        {
            return StoryFunctionService.BuildFunctionChoiceDisplay(function, BuildNextStoryChoiceFunctionIndicator());
        }

        private async Task<string?> BuildStoryFunctionValueAsync(FunctionEntry function)
        {
            var indicator = function.Indicator.Trim();
            if (StoryFunctionService.IsChoiceFunctionTemplate(function))
            {
                var choiceIndicator = BuildNextStoryChoiceFunctionIndicator();
                var optionNotes = await _functionDialogService.EditChoiceNotesAsync(choiceIndicator);
                if (optionNotes is null)
                {
                    return null;
                }

                SaveChoiceFunctionNotes(choiceIndicator, optionNotes);
                return choiceIndicator;
            }

            if (StoryFunctionService.IsChapterJumpFunctionTemplate(function))
            {
                return await ChooseChapterJumpFunctionValueAsync();
            }

            if (StoryFunctionService.IsSegmentJumpFunctionTemplate(function))
            {
                return await ChooseSegmentJumpFunctionValueAsync();
            }

            if (StoryFunctionService.IsBgmFunctionTemplate(function))
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
                var selected = await _storyDialogService.SelectSimpleChoiceAsync(
                    "背景切换模式",
                    StoryFunctionService.CreateBackgroundLerpModeChoices());
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
            var selected = await _storyDialogService.SelectSimpleChoiceAsync(
                "BGM",
                StoryFunctionService.CreateBgmChoices());
            return selected as string;
        }

        private async Task<string?> ChooseChapterJumpFunctionValueAsync()
        {
            if (_currentProject is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转章节", "当前没有打开项目。");
                return null;
            }

            List<ChapterInfo> chapters = _projectWorkspaceService.GetChapters(_currentProject)
                .OrderBy(chapter => chapter.Code)
                .ToList();
            if (chapters.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转章节", "当前项目还没有章节。");
                return null;
            }

            var choices = StoryFunctionService.CreateChapterJumpChoices(chapters, _currentProject.Code);
            return await _storyDialogService.SelectSimpleChoiceAsync("跳转章节", choices) as string;
        }

        private async Task<string?> ChooseSegmentJumpFunctionValueAsync()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有可跳转小节", "请先打开一个章节。");
                return null;
            }

            var sectionCount = GetCurrentStorySectionCount();
            var choices = StoryFunctionService.CreateSegmentJumpChoices(sectionCount);
            return await _storyDialogService.SelectSimpleChoiceAsync("跳转小节", choices) as string;
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
                    _storyCsvService.GetLocalSectionCsvPaths(_currentStoryChapter)
                        .Select(file => file.Section)
                        .DefaultIfEmpty(1)
                        .Max());
            }

            return Math.Max(1, sectionCount);
        }

        private void SaveChoiceFunctionNotes(string choiceIndicator, IReadOnlyList<string> optionNotes)
        {
            if (_currentStoryChapter is null)
            {
                return;
            }

            var state = _storyStateService.ReadChoiceNotes(_currentStoryChapter);
            state.Choices[choiceIndicator] = optionNotes
                .Select(NormalizeFunctionChoiceNote)
                .ToList();
            _storyStateService.WriteChoiceNotes(_currentStoryChapter, state);
        }

        private List<string> GetCurrentStoryChoiceValues()
        {
            if (_storyRows.Count == 0)
            {
                return [];
            }

            return StoryFunctionService.SplitFunctionValues(_storyRows[_currentStoryRowIndex].Get("Custom"))
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

            await _storyDialogService.ShowCurrentChoicesAsync(choices, GetChoiceFunctionNoteMap());
        }

        private Dictionary<string, List<string>> GetChoiceFunctionNoteMap()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (_currentStoryChapter is null)
            {
                return result;
            }

            var state = _storyStateService.ReadChoiceNotes(_currentStoryChapter);
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

        private void CopyStoryChoiceNotes(string oldChoice, string newChoice)
        {
            if (_currentStoryChapter is null ||
                string.Equals(oldChoice, newChoice, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _storyStateService.CopyChoiceNotes(_currentStoryChapter, oldChoice, newChoice);
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

            _storyStateService.RemoveChoiceNotes(
                _currentStoryChapter,
                removedChoices.Where(choice => !StoryChoiceExistsInRows(choice)));
        }

        private bool StoryChoiceExistsInRows(string choice)
        {
            return _storyRows.Any(row => StoryFunctionService.SplitFunctionValues(row.Get("Custom"))
                .Any(function => string.Equals(function, choice, StringComparison.OrdinalIgnoreCase)));
        }

        private async Task<int?> ChooseStorySoundEffectIndexAsync()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return null;
            }

            var choices = AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryObjectChoice(index.ToString(), $"{index}: {Path.GetFileNameWithoutExtension(path)}", index))
                .ToList();
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "没有特殊音效", "请先在素材库里导入特殊音效。");
                return null;
            }

            var selected = await _storyDialogService.SelectSimpleChoiceAsync("选择特殊音效", choices);
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

            var currentIndex = ParseInt(_storyRows[_currentStoryRowIndex].Get(fieldName));
            var selectedChoice = await _storyDialogService.SelectAssetChoiceAsync(title, choices, currentIndex);
            if (selectedChoice is null)
            {
                return;
            }

            if (selectedChoice.Index == currentIndex)
            {
                return;
            }

            CaptureStoryUndoState($"更换{StoryAssetFieldService.GetDisplayName(fieldName)}");
            _storyRows[_currentStoryRowIndex].Set(fieldName, selectedChoice.Index.ToString());
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
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Adorn, -1);
                    break;
                case Windows.System.VirtualKey.E:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Adorn, 1);
                    break;
                case Windows.System.VirtualKey.A:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Face, -1);
                    break;
                case Windows.System.VirtualKey.D:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Face, 1);
                    break;
                case Windows.System.VirtualKey.Z:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Cloth, -1);
                    break;
                case Windows.System.VirtualKey.C:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Cloth, 1);
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
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Vfx, 1);
                    break;
                case Windows.System.VirtualKey.NumberPad2:
                case Windows.System.VirtualKey.Down:
                    await CycleStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Vfx, -1);
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
            _storyCharacterSlotClipboard = StoryCharacterSlotService.CreateClipboard(row, slotIndex);
            ShowStoryStatus(InfoBarSeverity.Success, "已复制立绘数据", $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}：{StoryCharacterSlotService.FormatClipboard(_storyCharacterSlotClipboard)}");
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
            if (StoryCharacterSlotService.MatchesClipboard(row, slotIndex, _storyCharacterSlotClipboard))
            {
                return;
            }

            CaptureStoryUndoState($"粘贴{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}立绘数据");
            StoryCharacterSlotService.ApplyClipboard(row, slotIndex, _storyCharacterSlotClipboard);
            NormalizeStoryDetachedCharacterLayers(row);
            SyncStorySpeakerTextIfNeeded(slotIndex, row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex)));
            PersistCurrentStoryRowsToFiles();
            ShowStoryStatus(InfoBarSeverity.Success, "已粘贴立绘数据", $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}：{StoryCharacterSlotService.FormatClipboard(_storyCharacterSlotClipboard)}");
            await RefreshStoryPreviewAsync();
        }

        private void CopyStoryAssetField(string fieldName)
        {
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            _storyAssetClipboard = StoryAssetFieldService.CreateClipboard(row, fieldName);
            ShowStoryStatus(InfoBarSeverity.Success, "已复制基础素材", $"{StoryAssetFieldService.GetDisplayName(fieldName)}：{_storyAssetClipboard.Value}");
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

            if (!StoryAssetFieldService.IsSameField(_storyAssetClipboard, fieldName))
            {
                ShowStoryStatus(InfoBarSeverity.Warning, "素材类型不一致", $"复制的是{StoryAssetFieldService.GetDisplayName(_storyAssetClipboard.FieldName)}，当前悬停的是{StoryAssetFieldService.GetDisplayName(fieldName)}。");
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            if (StoryAssetFieldService.MatchesValue(row, fieldName, _storyAssetClipboard))
            {
                return;
            }

            CaptureStoryUndoState($"粘贴{StoryAssetFieldService.GetDisplayName(fieldName)}");
            StoryAssetFieldService.ApplyClipboard(row, fieldName, _storyAssetClipboard);
            PersistCurrentStoryRowsToFiles();
            UpdateStoryToolbarCurrentInfo();
            ShowStoryStatus(InfoBarSeverity.Success, "已粘贴基础素材", $"{StoryAssetFieldService.GetDisplayName(fieldName)}：{_storyAssetClipboard.Value}");
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

        private async Task ShowStoryShortcutHelpDialogAsync()
        {
            await _shortcutService.ShowShortcutHelpAsync();
        }

        private async Task CycleStoryCharacterLayerAsync(int slotIndex, CharacterLayerKind layerKind, int delta)
        {
            var layerSpec = GetStoryCharacterLayerSpec(layerKind);
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var character = ResolveStoryCharacter(row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex)));
            if (character is null)
            {
                return;
            }

            if (layerSpec.Kind == CharacterLayerKind.Vfx)
            {
                var filters = GetStoryCharacterFilters();
                if (filters.Count == 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", "当前素材库还没有角色滤镜。");
                    return;
                }

                var filterColumn = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
                var filterCurrentIndex = ParseInt(row.Get(filterColumn));
                var nextIndex = ((filterCurrentIndex + delta) % filters.Count + filters.Count) % filters.Count;
                if (nextIndex == filterCurrentIndex)
                {
                    return;
                }

                CaptureStoryUndoState($"快捷切换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}{layerSpec.DisplayName}");
                row.Set(filterColumn, nextIndex.ToString());
                PersistCurrentStoryRowsToFiles();
                ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, nextIndex, CharacterFilterService.GetDisplayName(filters[nextIndex], nextIndex));
                await RefreshStoryPreviewAsync();
                return;
            }

            var paths = CharacterLayerAssetService.GetLayerPaths(character, layerSpec.Kind);
            var layerColumn = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
            var layerCurrentIndex = ParseInt(row.Get(layerColumn));
            var validIndexes = GetStoryCompatibleLayerIndexes(character, layerSpec, paths, row, slotIndex);
            if (validIndexes.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", $"角色 {character.Name} 还没有可用的{layerSpec.DisplayName}素材。");
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

            CaptureStoryUndoState($"快捷切换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}{layerSpec.DisplayName}");
            row.Set(layerColumn, layerNextIndex.ToString());
            if (layerSpec.Kind == CharacterLayerKind.Cloth)
            {
                NormalizeStoryRowLayerCompatibility(row, character, slotIndex);
            }

            PersistCurrentStoryRowsToFiles();
            ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, layerNextIndex, StoryCharacterLayerChoiceFactory.GetDisplayName(layerSpec, paths, layerNextIndex));
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
            var characterColumn = StoryCharacterSlotService.GetCharacterColumn(slotIndex);
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

            CaptureStoryUndoState($"快捷切换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}角色");
            row.Set(characterColumn, characters[nextIndex].Code);
            StoryCharacterSlotService.ResetLayerColumns(row, slotIndex);
            SyncStorySpeakerTextIfNeeded(slotIndex, characters[nextIndex].Code);
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
            if (StoryCharacterSlotService.IsEmpty(row, slotIndex))
            {
                return;
            }

            CaptureStoryUndoState($"清空{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}立绘位");
            row.Set(StoryCharacterSlotService.GetCharacterColumn(slotIndex), string.Empty);
            StoryCharacterSlotService.ResetLayerColumns(row, slotIndex);
            SyncStorySpeakerTextIfNeeded(slotIndex, string.Empty);
            PersistCurrentStoryRowsToFiles();
            _ = RefreshStoryPreviewAsync();
        }

        private void SyncStorySpeakerTextIfNeeded(int slotIndex, string value)
        {
            if (slotIndex != 0)
            {
                return;
            }

            _isLoadingStoryRow = true;
            try
            {
                _storyEditorViewModel.SpeakerText = value;
            }
            finally
            {
                _isLoadingStoryRow = false;
            }
        }

        private bool NormalizeStoryDetachedCharacterLayers(StoryRow row)
        {
            var changed = false;
            for (var slotIndex = 0; slotIndex <= 5; slotIndex++)
            {
                var characterValue = row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex));
                if (!ShouldClearDetachedStoryCharacterLayers(characterValue))
                {
                    continue;
                }

                changed |= StoryCharacterSlotService.ResetLayerColumnsIfNeeded(row, slotIndex);
            }

            return changed;
        }

        private bool ShouldClearDetachedStoryCharacterLayers(string characterValue)
        {
            var trimmed = characterValue.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || StoryCharacterSlotService.ContainsCjk(trimmed))
            {
                return true;
            }

            return _currentStoryAssetLibrary is not null && ResolveStoryCharacter(trimmed) is null;
        }

        private void ShowStoryLayerChangedStatus(int slotIndex, string title, int index, string displayName)
        {
            ShowStoryStatus(
                InfoBarSeverity.Success,
                $"已更换{title}",
                $"{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}：{displayName}（索引 {index}）");
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
            foreach (var function in StoryFunctionService.SplitFunctionValues(functionValue))
            {
                if (StoryFunctionService.TryParseBackgroundTransitionMode(function, out var transitionMode))
                {
                    _storyBackgroundTransitionMode = transitionMode;
                }
            }

            if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTOP"))
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
            if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTART"))
            {
                _storyBgmPlaybackSuppressed = false;
                await PlayCurrentStoryBgmFromUiAsync();
            }
        }

        private bool IsCurrentStoryFunction(string normalizedFunctionKey)
        {
            return _storyRows.Count > 0 &&
                StoryFunctionService.ContainsFunction(_storyRows[_currentStoryRowIndex].Get("Custom"), normalizedFunctionKey);
        }

        private void ShowStoryFunctionTriggeredStatus(string functionValue)
        {
            ClearStoryFunctionTips();
            foreach (var functionName in StoryFunctionService.EnumerateFunctionDisplayNames(functionValue))
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
                if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTOP"))
                {
                    suppressed = true;
                }
                else if (StoryFunctionService.ContainsFunction(functionValue, "BGMSTART"))
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
                foreach (var functionValue in StoryFunctionService.SplitFunctionValues(_storyRows[i].Get("Custom")))
                {
                    if (StoryFunctionService.TryParseBackgroundTransitionMode(functionValue, out var parsedMode))
                    {
                        mode = parsedMode;
                    }
                }
            }

            return mode;
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

            var bgmPaths = AudioAssetService.GetFilePaths(GetMusicFolderPath(_currentStoryAssetLibrary));
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

            var scenePaths = AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(_currentStoryAssetLibrary));
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
                var character = ResolveStoryCharacter(row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex)));
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
            _storyEditorViewModel.AssetStatusText = _currentStoryAssetLibrary is null
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

            var backgrounds = BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary));
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
                    StoryFunctionService.GetBackgroundTransitionModeRemark(_storyBackgroundTransitionMode));
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
                var backgrounds = BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary));
                foreach (var row in _storyRows)
                {
                    var backgroundIndex = ResolveStoryAssetIndex(ParseInt(row.Get("BGindex")), backgrounds.Count);
                    if (backgroundIndex is not null)
                    {
                        paths.Add(backgrounds[backgroundIndex.Value]);
                    }

                    for (var slotIndex = 0; slotIndex <= 5; slotIndex++)
                    {
                        var character = ResolveStoryCharacter(row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex)));
                        if (character is null)
                        {
                            continue;
                        }

                        var bodyPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Cloth, row, slotIndex);
                        var facePath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Face, row, slotIndex);
                        var adornPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Adorn, row, slotIndex);
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
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Cloth, bodyIndex),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Face, faceIndex),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Adorn, adornIndex),
                CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Vfx, vfxIndex)
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
                await ThumbnailFactory.LoadThumbnailFromFileAsync(image, imagePath!);
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

            var bodyPath = CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Cloth, bodyIndex);
            var facePath = CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Face, faceIndex);
            var adornPath = CharacterLayerAssetService.GetStoryLayerPath(character, CharacterLayerKind.Adorn, adornIndex);
            var imagePaths = new[]
            {
                bodyPath,
                _characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, facePath, ComputeFileHash) ? facePath : null,
                _characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, adornPath, ComputeFileHash) ? adornPath : null
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
                VerticalAlignment = VerticalAlignment.Center,
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
                BackgroundImageService.Extensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase);
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
            return GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("角色", async (_, _) => await ChooseStoryCharacterAsync(slotIndex)),
                GridViewItemFactory.CreateMenuItem("服装", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Cloth)),
                GridViewItemFactory.CreateMenuItem("表情", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Face)),
                GridViewItemFactory.CreateMenuItem("装饰", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Adorn)),
                GridViewItemFactory.CreateMenuItem("滤镜", async (_, _) => await ChooseStoryCharacterLayerAsync(slotIndex, CharacterLayerKind.Vfx)));
        }

        private MenuFlyout CreateStorySpeakerSlotMenu()
        {
            return GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("服装", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Cloth)),
                GridViewItemFactory.CreateMenuItem("表情", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Face)),
                GridViewItemFactory.CreateMenuItem("装饰", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Adorn)),
                GridViewItemFactory.CreateMenuItem("滤镜", async (_, _) => await ChooseStoryCharacterLayerAsync(0, CharacterLayerKind.Vfx)));
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

            var selected = await _storyDialogService.SelectPreviewChoiceAsync(
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

                CaptureStoryUndoState($"清空{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}角色");
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

            CaptureStoryUndoState($"更换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}角色");
            row.Set($"Chara{slotIndex}", character.Code);
            row.Set($"Body{slotIndex}", "0");
            row.Set($"Face{slotIndex}", "0");
            row.Set($"Adorn{slotIndex}", "0");
            row.Set($"Vfx{slotIndex}", "0");
            PersistCurrentStoryRowsToFiles();
            await RefreshStoryPreviewAsync();
        }

        private async Task ChooseStoryCharacterLayerAsync(int slotIndex, CharacterLayerKind layerKind)
        {
            var layerSpec = GetStoryCharacterLayerSpec(layerKind);
            if (_currentStoryCsvPath is null || _storyRows.Count == 0)
            {
                return;
            }

            var row = _storyRows[_currentStoryRowIndex];
            var characterName = row.Get(StoryCharacterSlotService.GetCharacterColumn(slotIndex));
            var character = ResolveStoryCharacter(characterName);
            if (character is null)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"无法选择{layerSpec.DisplayName}", slotIndex == 0 ? "请先填写当前说话人。" : "请先为这个位置选择角色。");
                return;
            }

            if (layerSpec.Kind == CharacterLayerKind.Vfx)
            {
                var filters = GetStoryCharacterFilters();
                if (filters.Count == 0)
                {
                    ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", "当前素材库还没有角色滤镜。");
                    return;
                }

                var selectedFilter = await _storyDialogService.SelectPreviewChoiceAsync(
                    $"选择{layerSpec.DisplayName}",
                    filters.Select((filter, index) => new StoryObjectChoice(index.ToString(), $"{index}: {filter.Remark}", index)).ToList());
                if (selectedFilter is not int selectedFilterIndex)
                {
                    return;
                }

                var filterColumn = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
                if (ParseInt(row.Get(filterColumn)) == selectedFilterIndex)
                {
                    return;
                }

                CaptureStoryUndoState($"更换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}{layerSpec.DisplayName}");
                row.Set(filterColumn, selectedFilterIndex.ToString());
                PersistCurrentStoryRowsToFiles();
                ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, selectedFilterIndex, CharacterFilterService.GetDisplayName(filters[selectedFilterIndex], selectedFilterIndex));
                await RefreshStoryPreviewAsync();
                return;
            }

            var paths = CharacterLayerAssetService.GetLayerPaths(character, layerSpec.Kind);
            var choices = CreateStoryLayerChoices(layerSpec, paths, character, row, slotIndex);
            if (choices.Count == 0)
            {
                ShowStoryStatus(InfoBarSeverity.Warning, $"没有{layerSpec.DisplayName}", $"角色 {character.Name} 还没有可用的{layerSpec.DisplayName}素材。");
                return;
            }

            var selected = await _storyDialogService.SelectPreviewChoiceAsync(
                $"选择{layerSpec.DisplayName}",
                choices);
            if (selected is not int selectedIndex)
            {
                return;
            }

            if (ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix))) == selectedIndex)
            {
                return;
            }

            CaptureStoryUndoState($"更换{StoryCharacterSlotService.GetSlotDisplayName(slotIndex)}{layerSpec.DisplayName}");
            row.Set(StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix), selectedIndex.ToString());
            if (layerSpec.Kind == CharacterLayerKind.Cloth)
            {
                NormalizeStoryRowLayerCompatibility(row, character, slotIndex);
            }

            PersistCurrentStoryRowsToFiles();
            ShowStoryLayerChangedStatus(slotIndex, layerSpec.DisplayName, selectedIndex, StoryCharacterLayerChoiceFactory.GetDisplayName(layerSpec, paths, selectedIndex));
            await RefreshStoryPreviewAsync();
        }

        private List<StoryObjectChoice> CreateStoryLayerChoices(
            StoryCharacterLayerSpec layerSpec,
            IReadOnlyList<string> paths,
            CharacterInfo character,
            StoryRow row,
            int slotIndex)
        {
            var currentBodyPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Cloth, row, slotIndex);
            var currentFacePath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Face, row, slotIndex);
            var currentAdornPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Adorn, row, slotIndex);
            return StoryCharacterLayerChoiceFactory.CreateChoices(
                layerSpec,
                paths,
                currentBodyPath,
                currentFacePath,
                currentAdornPath,
                (clothPath, layerPath) => _characterLayerAssetService.IsCompatibleWithCloth(character, clothPath, layerPath, ComputeFileHash),
                paths => BuildStoryChoicePreviewPaths(paths.ToArray()));
        }

        private List<int> GetStoryCompatibleLayerIndexes(
            CharacterInfo character,
            StoryCharacterLayerSpec layerSpec,
            IReadOnlyList<string> paths,
            StoryRow row,
            int slotIndex)
        {
            var bodyPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Cloth, row, slotIndex);
            return StoryCharacterLayerChoiceFactory.GetCompatibleIndexes(
                layerSpec,
                paths,
                bodyPath,
                (clothPath, layerPath) => _characterLayerAssetService.IsCompatibleWithCloth(character, clothPath, layerPath, ComputeFileHash));
        }

        private bool NormalizeStoryRowLayerCompatibility(StoryRow row, CharacterInfo character, int slotIndex)
        {
            var changed = false;
            var bodyPath = GetStoryCharacterLayerPath(character, CharacterLayerKind.Cloth, row, slotIndex);
            changed |= NormalizeStoryLayerCompatibility(row, character, slotIndex, GetStoryCharacterLayerSpec(CharacterLayerKind.Face), bodyPath, false);
            changed |= NormalizeStoryLayerCompatibility(row, character, slotIndex, GetStoryCharacterLayerSpec(CharacterLayerKind.Adorn), bodyPath, true);
            return changed;
        }

        private bool NormalizeStoryLayerCompatibility(
            StoryRow row,
            CharacterInfo character,
            int slotIndex,
            StoryCharacterLayerSpec layerSpec,
            string? bodyPath,
            bool allowNone)
        {
            var columnName = StoryCharacterSlotService.GetLayerColumn(slotIndex, layerSpec.FieldPrefix);
            var currentIndex = ParseInt(row.Get(columnName));
            if (allowNone && currentIndex <= 0)
            {
                return false;
            }

            var currentPath = CharacterLayerAssetService.GetStoryLayerPath(character, layerSpec.Kind, currentIndex);
            if (_characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, currentPath, ComputeFileHash))
            {
                return false;
            }

            var paths = CharacterLayerAssetService.GetLayerPaths(character, layerSpec.Kind);
            var compatible = paths
                .Select((path, index) => new { path, index })
                .FirstOrDefault(item => _characterLayerAssetService.IsCompatibleWithCloth(character, bodyPath, item.path, ComputeFileHash));
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

        private static string? GetStoryCharacterLayerPath(
            CharacterInfo character,
            CharacterLayerKind layerKind,
            StoryRow row,
            int slotIndex)
        {
            var spec = GetStoryCharacterLayerSpec(layerKind);
            var index = ParseInt(row.Get(StoryCharacterSlotService.GetLayerColumn(slotIndex, spec.FieldPrefix)));
            return CharacterLayerAssetService.GetStoryLayerPath(character, layerKind, index);
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

            return _characterWorkspaceService
                .GetCharactersByFolderOrder(_currentStoryAssetLibrary)
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

            return _characterWorkspaceService.GetCharactersByName(_currentStoryAssetLibrary);
        }

        private List<CharacterInfo> GetStoryCharactersByFolderOrder()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return _characterWorkspaceService.GetCharactersByFolderName(_currentStoryAssetLibrary);
        }

        private List<StoryAssetChoice> GetStoryBackgroundChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private List<StoryAssetChoice> GetStoryBgmChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return AudioAssetService.GetFilePaths(GetMusicFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private List<StoryAssetChoice> GetStorySceneChoices()
        {
            if (_currentStoryAssetLibrary is null)
            {
                return [];
            }

            return AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(_currentStoryAssetLibrary))
                .Select((path, index) => new StoryAssetChoice(index, Path.GetFileNameWithoutExtension(path)))
                .ToList();
        }

        private AssetLibraryInfo? ResolveProjectAssetLibrary(ProjectInfo project)
        {
            return _projectWorkspaceService.ResolveProjectAssetLibrary(_projectRootPath, project);
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
                progress => Task.Run(() => _storyAssetIndexSyncService.SyncGlobalAssetIndexes(assetLibrary, assetLabel, columnName, indexRemap, oldLabels, newLabels, assetCount, progress)));
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
                progress => Task.Run(() => _storyAssetIndexSyncService.SyncCharacterLayerIndexes(assetLibrary, character, layerKind, GetCharacterLayerDisplayName(layerKind), indexRemap, oldLabels, newLabels, assetCount, progress)));
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
                progress => Task.Run(() => _storyAssetIndexSyncService.SyncCharacterFilterIndexes(assetLibrary, indexRemap, oldLabels, newLabels, assetCount, progress)));
            RefreshOpenStoryRowsAfterIndexSync(result.ChangedCsvPaths);
            await ShowAssetIndexSyncResultDialogAsync(result);
            return result;
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
            _storyEditorViewModel.RefreshCommandStates();
            if (StoryEditorPage.Visibility == Visibility.Visible)
            {
                RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
                LoadStoryRowIntoUi();
            }
        }

        private static bool StoryCharacterMatches(string value, CharacterInfo character)
        {
            return string.Equals(value, character.Code, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, character.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStoryLayerFieldPrefix(CharacterLayerKind layerKind)
        {
            return GetStoryCharacterLayerSpec(layerKind).FieldPrefix;
        }

        private static StoryCharacterLayerSpec GetStoryCharacterLayerSpec(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => new StoryCharacterLayerSpec(layerKind, "Body", "服装"),
                CharacterLayerKind.Face => new StoryCharacterLayerSpec(layerKind, "Face", "表情"),
                CharacterLayerKind.Adorn => new StoryCharacterLayerSpec(layerKind, "Adorn", "装饰"),
                CharacterLayerKind.Vfx => new StoryCharacterLayerSpec(layerKind, "Vfx", "滤镜"),
                _ => new StoryCharacterLayerSpec(CharacterLayerKind.Cloth, "Body", "服装")
            };
        }

        private static StoryCharacterLayerSpec GetStoryCharacterLayerSpec(string fieldPrefix)
        {
            return fieldPrefix switch
            {
                "Body" => GetStoryCharacterLayerSpec(CharacterLayerKind.Cloth),
                "Face" => GetStoryCharacterLayerSpec(CharacterLayerKind.Face),
                "Adorn" => GetStoryCharacterLayerSpec(CharacterLayerKind.Adorn),
                "Vfx" => GetStoryCharacterLayerSpec(CharacterLayerKind.Vfx),
                _ => GetStoryCharacterLayerSpec(CharacterLayerKind.Cloth)
            };
        }

        private static Dictionary<int, int> BuildAssetIndexRemap(
            IReadOnlyList<string> orderedPaths,
            Func<string, int?> getOldIndex)
        {
            var result = new Dictionary<int, int>();
            foreach (var item in orderedPaths.Select((path, newIndex) => new { OldIndex = getOldIndex(path), NewIndex = newIndex }))
            {
                if (item.OldIndex is null || item.OldIndex.Value == item.NewIndex)
                {
                    continue;
                }

                result.TryAdd(item.OldIndex.Value, item.NewIndex);
            }

            return result;
        }

        private static List<string> GetOrderedExistingTaggedPaths(GridView gridView)
        {
            return gridView.Items
                .OfType<GridViewItem>()
                .Select(item => item.Tag as string)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static GridViewItem? ResolveDraggedGridViewItem(GridView gridView, DragItemsStartingEventArgs e)
        {
            var draggedObject = e.Items.FirstOrDefault();
            return draggedObject as GridViewItem ??
                   (draggedObject is null ? null : gridView.ContainerFromItem(draggedObject) as GridViewItem) ??
                   gridView.Items
                       .OfType<GridViewItem>()
                       .FirstOrDefault(item => ReferenceEquals(item.Content, draggedObject));
        }

        private static void MoveGridViewItemToEnd(GridView gridView, GridViewItem? item)
        {
            if (item is null)
            {
                return;
            }

            var currentIndex = gridView.Items.IndexOf(item);
            var lastIndex = gridView.Items.Count - 1;
            if (currentIndex < 0 || currentIndex == lastIndex)
            {
                return;
            }

            gridView.Items.Remove(item);
            gridView.Items.Add(item);
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

        private bool IsAudioExpanderExpanded(AudioAssetKind kind)
        {
            return kind switch
            {
                AudioAssetKind.Music => MusicExpander.IsExpanded,
                AudioAssetKind.Ambient => AmbientSoundExpander.IsExpanded,
                AudioAssetKind.SoundEffect => SoundEffectExpander.IsExpanded,
                _ => false
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

        private static int? ResolveStoryAssetIndex(int rawIndex, int assetCount)
        {
            if (assetCount <= 0 || rawIndex < 0)
            {
                return null;
            }

            return rawIndex < assetCount ? rawIndex : null;
        }

        private void ShowChapterStatus(InfoBarSeverity severity, string title, string message)
        {
            ChapterInfoBar.Severity = severity;
            ChapterInfoBar.Title = title;
            ChapterInfoBar.Message = message;
            ChapterInfoBar.IsOpen = true;
        }

        private void ShowGlobalProgress(string title, string detail)
        {
            _globalProgressCancellation?.Dispose();
            _globalProgressCancellation = new CancellationTokenSource();
            _globalProgressOperationTitle = title;
            _globalProgressStopwatch.Restart();
            _globalProgressElapsedTimer.Start();
            _isGlobalProgressVisible = true;

            GlobalProgressHost.Visibility = Visibility.Visible;
            GlobalProgressTitleText.Text = detail;
            GlobalProgressDetailText.Text = title;
            GlobalProgressElapsedText.Text = FormatElapsedTime(TimeSpan.Zero);
            GlobalProgressPercentText.Text = "0%";
            GlobalProgressBar.IsIndeterminate = true;
            GlobalProgressBar.Value = 0;
            _globalProgressLastPercent = 0;
            UpdateGlobalProgressRing(0);
            AnimateGlobalProgressHost(show: true);
        }

        private void UpdateGlobalProgress(string message, double percent, string? detail = null, bool isIndeterminate = false)
        {
            if (!_isGlobalProgressVisible)
            {
                ShowGlobalProgress(_globalProgressOperationTitle.Length == 0 ? "正在处理" : _globalProgressOperationTitle, message);
            }

            var clampedPercent = Math.Clamp(percent, 0, 100);
            GlobalProgressTitleText.Text = message;
            GlobalProgressDetailText.Text = string.IsNullOrWhiteSpace(detail)
                ? _globalProgressOperationTitle
                : detail.Replace('\n', ' ');
            GlobalProgressBar.IsIndeterminate = isIndeterminate;
            if (!isIndeterminate)
            {
                GlobalProgressBar.Value = clampedPercent;
            }

            GlobalProgressPercentText.Text = $"{clampedPercent:0}%";
            _globalProgressLastPercent = clampedPercent;
            UpdateGlobalProgressRing(clampedPercent);
            UpdateGlobalProgressElapsedText();
        }

        private void CompleteGlobalProgress(string message, string? detail = null)
        {
            _globalProgressStopwatch.Stop();
            _globalProgressElapsedTimer.Stop();
            GlobalProgressBar.IsIndeterminate = false;
            GlobalProgressBar.Value = 100;
            GlobalProgressPercentText.Text = "100%";
            _globalProgressLastPercent = 100;
            UpdateGlobalProgressRing(100);
            GlobalProgressTitleText.Text = message;
            GlobalProgressDetailText.Text = string.IsNullOrWhiteSpace(detail) ? _globalProgressOperationTitle : detail;
            UpdateGlobalProgressElapsedText();
        }

        private async Task HideGlobalProgressAfterDelayAsync(int delayMilliseconds = 1400)
        {
            await Task.Delay(delayMilliseconds);
            HideGlobalProgress();
        }

        private void HideGlobalProgress()
        {
            _globalProgressStopwatch.Reset();
            _globalProgressElapsedTimer.Stop();
            _isGlobalProgressVisible = false;
            _globalProgressCancellation?.Dispose();
            _globalProgressCancellation = null;
            AnimateGlobalProgressHost(show: false);
        }

        private CancellationToken GetGlobalProgressCancellationToken()
        {
            return _globalProgressCancellation?.Token ?? CancellationToken.None;
        }

        private async void GlobalProgressRing_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (!_isGlobalProgressVisible || _globalProgressCancellation is null || _globalProgressCancellation.IsCancellationRequested)
            {
                return;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "取消当前操作？",
                $"正在进行：{_globalProgressOperationTitle}\n取消后，已经写入的文件可能会保留，未完成的部分会停止。",
                "取消操作",
                "继续等待",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
            if (confirmed)
            {
                _globalProgressCancellation.Cancel();
                UpdateGlobalProgress("正在取消...", _globalProgressLastPercent, "等待当前步骤安全停止。");
            }
        }

        private void GlobalProgressElapsedTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            UpdateGlobalProgressElapsedText();
        }

        private void UpdateGlobalProgressElapsedText()
        {
            GlobalProgressElapsedText.Text = FormatElapsedTime(_globalProgressStopwatch.Elapsed);
        }

        private void UpdateGlobalProgressRing(double percent)
        {
            var clampedPercent = Math.Clamp(percent, 0, 100);
            const double size = 86;
            const double stroke = 7;
            var radius = (size - stroke) / 2;
            var center = size / 2;

            if (clampedPercent <= 0)
            {
                GlobalProgressRingPath.Data = null;
                return;
            }

            if (clampedPercent >= 99.9)
            {
                var geometryGroup = new GeometryGroup();
                geometryGroup.Children.Add(CreateProgressRingArc(center, radius, 359.9));
                GlobalProgressRingPath.Data = geometryGroup;
                return;
            }

            GlobalProgressRingPath.Data = CreateProgressRingArc(center, radius, clampedPercent / 100d * 360d);
        }

        private static Geometry CreateProgressRingArc(double center, double radius, double angleDegrees)
        {
            var startPoint = new Windows.Foundation.Point(center, center - radius);
            var radians = (angleDegrees - 90) * Math.PI / 180d;
            var endPoint = new Windows.Foundation.Point(
                center + radius * Math.Cos(radians),
                center + radius * Math.Sin(radians));
            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Windows.Foundation.Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = angleDegrees > 180
            });

            return new PathGeometry
            {
                Figures = { figure }
            };
        }

        private string FormatProgressSpeed(long completedBytes)
        {
            var elapsedSeconds = Math.Max(0.1, _globalProgressStopwatch.Elapsed.TotalSeconds);
            if (completedBytes <= 0 || elapsedSeconds <= 0)
            {
                return string.Empty;
            }

            return $"{FormatFileSize((long)(completedBytes / elapsedSeconds))}/s";
        }

        private string FormatRemainingTime(long completedBytes, long totalBytes)
        {
            if (completedBytes <= 0 || totalBytes <= 0 || completedBytes >= totalBytes)
            {
                return "--:--";
            }

            var elapsedSeconds = Math.Max(0.1, _globalProgressStopwatch.Elapsed.TotalSeconds);
            var bytesPerSecond = completedBytes / elapsedSeconds;
            if (bytesPerSecond <= 0)
            {
                return "--:--";
            }

            return FormatElapsedTime(TimeSpan.FromSeconds((totalBytes - completedBytes) / bytesPerSecond));
        }

        private void AnimateGlobalProgressHost(bool show)
        {
            var transform = GlobalProgressHostTransform;
            var fromY = show ? 130 : 0;
            var toY = show ? 0 : 130;
            var fromOpacity = show ? 0 : 1;
            var toOpacity = show ? 1 : 0;
            GlobalProgressHost.Visibility = Visibility.Visible;
            transform.Y = fromY;
            GlobalProgressHost.Opacity = fromOpacity;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideAnimation = new DoubleAnimation
            {
                From = fromY,
                To = toY,
                Duration = TimeSpan.FromMilliseconds(show ? 240 : 180),
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, transform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.Y));

            var fadeAnimation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(show ? 220 : 160),
                EasingFunction = easing
            };
            Storyboard.SetTarget(fadeAnimation, GlobalProgressHost);
            Storyboard.SetTargetProperty(fadeAnimation, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(slideAnimation);
            storyboard.Children.Add(fadeAnimation);
            storyboard.Completed += (_, _) =>
            {
                transform.Y = toY;
                GlobalProgressHost.Opacity = toOpacity;
                if (!show)
                {
                    GlobalProgressHost.Visibility = Visibility.Collapsed;
                }
            };
            storyboard.Begin();
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
            var renamedCount = _projectWorkspaceService.UpdateChapterProjectCodePrefix(projectPath, oldProjectCode, newProjectCode);
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
                AppendLog(LogKind.Warning, $"无法重命名项目：{ex.Message}");
                return;
            }

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

            _projectWorkspaceService.DeleteProject(project);
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
            AppendLog(LogKind.User, $"项目 {project.Name} 的目标素材库改为：{selectedLibrary.Name}");
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
                AppendLog(LogKind.Warning, $"无法保存项目设置：{ex.Message}");
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
            AssetLibraryInfo updatedAssetLibrary;
            try
            {
                updatedAssetLibrary = _projectWorkspaceService.RenameAssetLibrary(_projectRootPath, assetLibrary, newName);
            }
            catch (IOException ex)
            {
                AppendLog(LogKind.Warning, $"无法重命名素材库：{ex.Message}");
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
            AppendLog(LogKind.User, $"重命名素材库：{oldName} → {newName}");
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
            AppendLog(LogKind.User, $"删除素材库：{assetLibrary.Name}");
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
            PlaySelectionSound();
            LoadProjects();
            AppendLog(LogKind.User, "手动刷新项目列表。");
        }

        private void RefreshAssetLibrariesButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectionSound();
            LoadAssetLibraries();
            AppendLog(LogKind.User, "手动刷新素材库列表。");
        }

        private void CancelCreateProjectButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            ShowWorkbenchPage();
        }

        private void CancelCreateAssetLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            ShowAssetLibraryPage();
        }

        private async void ChooseProjectThumbnailButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectionSound();
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
            PlaySelectionSound();
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

        private async Task<string?> PickReplacementFileAsync(IEnumerable<string> extensions, PickerLocationId startLocation)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = startLocation
            };
            foreach (var extension in extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFile = await picker.PickSingleFileAsync();
            return selectedFile?.Path;
        }

        private void CreateProjectButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPositiveSound();
            var projectName = CreateProjectNameTextBox.Text.Trim();
            var projectCode = CreateProjectCodeTextBox.Text.Trim();

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

            try
            {
                _projectWorkspaceService.CreateProject(
                    _projectRootPath,
                    projectName,
                    projectCode,
                    assetLibrary,
                    _selectedProjectThumbnailPath);
            }
            catch (IOException ex)
            {
                ShowCreateProjectError(ex.Message);
                return;
            }

            ResetCreateProjectForm();
            ShowWorkbenchPage();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"创建项目：{projectName}，关联素材库：{assetLibrary.Name}");
        }

        private void CreateAssetLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPositiveSound();
            var assetLibraryName = CreateAssetLibraryNameTextBox.Text.Trim();

            try
            {
                _projectWorkspaceService.CreateAssetLibrary(
                    _projectRootPath,
                    assetLibraryName,
                    _selectedAssetLibraryThumbnailPath);
            }
            catch (IOException ex)
            {
                ShowCreateAssetLibraryError(ex.Message);
                return;
            }

            ResetCreateAssetLibraryForm();
            ShowAssetLibraryPage();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"创建素材库：{assetLibraryName}");
        }

        private static void EnsureAssetLibraryCategoryFolders(string assetLibraryPath)
        {
            ProjectWorkspaceService.EnsureAssetLibraryCategoryFolders(assetLibraryPath);
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
            PlayNegativeSound();
            ShowAssetLibraryPage();
        }

        private async void AddBackgroundImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in BackgroundImageService.Extensions)
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

            var backgroundFolderPath = GetBackgroundFolderPath(_currentAssetLibrary);
            _isNormalizingBackgroundImages = true;
            var importedCount = 0;
            try
            {
                importedCount = await _backgroundImageService.ImportFilesAsync(
                    backgroundFolderPath,
                    sourcePaths,
                    ImportBackgroundImageAsPngAsync);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            if (importedCount == 0)
            {
                return 0;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            return importedCount;
        }

        private async void AddMusicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is null)
            {
                return;
            }

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var extension in AudioAssetService.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            var importedCount = ImportMusicFiles(selectedFiles.Select(file => file.Path));
            AppendLog(LogKind.User, $"导入音乐：{importedCount} 个文件。");
        }

        private int ImportMusicFiles(IEnumerable<string> sourcePaths)
        {
            return ImportAudioFiles(AudioAssetKind.Music, sourcePaths);
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

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var extension in AudioAssetService.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var selectedFiles = await picker.PickMultipleFilesAsync();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            var importedCount = ImportAudioFiles(kind, selectedFiles.Select(file => file.Path));
            AppendLog(LogKind.User, $"导入{AudioAssetService.GetDisplayName(kind)}：{importedCount} 个文件。");
        }

        private int ImportAudioFiles(AudioAssetKind kind, IEnumerable<string> sourcePaths)
        {
            if (_currentAssetLibrary is null)
            {
                return 0;
            }

            var musicFolderPath = GetAudioFolderPath(_currentAssetLibrary, kind);
            SetAudioNormalizing(kind, true);
            var importedCount = 0;
            try
            {
                importedCount = _audioAssetService.ImportFiles(kind, musicFolderPath, sourcePaths);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            if (importedCount == 0)
            {
                return 0;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            return importedCount;
        }

        private async void AddCharacterClothesButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndImportCharacterLayerAsync(ImportCharacterClothesAsync, "服装", "导入服装失败。");
        }

        private Task<int> ImportCharacterClothesAsync(IEnumerable<string> sourcePaths)
        {
            return ImportCharacterLayerAsync(
                sourcePaths,
                CharacterLayerKind.Cloth,
                string.Empty,
                () => _isNormalizingCharacterClothes = true,
                () => _isNormalizingCharacterClothes = false,
                character => character.Code);
        }

        private async void AddCharacterFacesButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndImportCharacterLayerAsync(ImportCharacterFacesAsync, "表情", "导入表情失败。");
        }

        private Task<int> ImportCharacterFacesAsync(IEnumerable<string> sourcePaths)
        {
            return ImportCharacterLayerAsync(
                sourcePaths,
                CharacterLayerKind.Face,
                string.Empty,
                () => _isNormalizingCharacterFaces = true,
                () => _isNormalizingCharacterFaces = false);
        }

        private async void AddCharacterAdornsButton_Click(object sender, RoutedEventArgs e)
        {
            await PickAndImportCharacterLayerAsync(ImportCharacterAdornsAsync, "装饰", "导入装饰失败。");
        }

        private async Task PickAndImportCharacterLayerAsync(
            Func<IEnumerable<string>, Task<int>> importAsync,
            string logLabel,
            string errorMessage)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            PlayPositiveSound();
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var extension in BackgroundImageService.Extensions)
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
                var importedCount = await importAsync(selectedFiles.Select(file => file.Path));
                AppendLog(LogKind.User, $"导入{logLabel}：{importedCount} 个文件。");
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, errorMessage, ex);
            }
        }

        private Task<int> ImportCharacterAdornsAsync(IEnumerable<string> sourcePaths)
        {
            return ImportCharacterLayerAsync(
                sourcePaths,
                CharacterLayerKind.Adorn,
                string.Empty,
                () => _isNormalizingCharacterAdorns = true,
                () => _isNormalizingCharacterAdorns = false);
        }

        private Task<int> ImportCharacterLayerAsync(
            IEnumerable<string> sourcePaths,
            CharacterLayerKind layerKind,
            string defaultScope,
            Action beginNormalize,
            Action endNormalize,
            Func<CharacterInfo, string?>? characterCodeSelector = null)
        {
            if (_currentCharacter is null)
            {
                return Task.FromResult(0);
            }

            var folderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, layerKind);
            var characterCode = characterCodeSelector?.Invoke(_currentCharacter);
            var importedCount = 0;
            beginNormalize();
            try
            {
                var entries = _characterLayerAssetService.CreateImportEntries(
                    folderPath,
                    layerKind,
                    sourcePaths,
                    defaultScope,
                    out importedCount);
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, layerKind, characterCode);
            }
            finally
            {
                endNormalize();
            }

            if (importedCount == 0)
            {
                return Task.FromResult(0);
            }

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            return Task.FromResult(importedCount);
        }

        private async void LoadBackgroundImages(AssetLibraryInfo assetLibrary)
        {
            await ReloadBackgroundImagesAsync(assetLibrary);
        }

        private void RegisterAssetLibraryExpanderLazyLoading()
        {
            BackgroundImagesExpander.Expanding += BackgroundImagesExpander_Expanded;
            MusicExpander.Expanding += MusicExpander_Expanded;
            AmbientSoundExpander.Expanding += AmbientSoundExpander_Expanded;
            SoundEffectExpander.Expanding += SoundEffectExpander_Expanded;
            CharactersExpander.Expanding += CharactersExpander_Expanded;
            FunctionExpander.Expanding += FunctionExpander_Expanded;
            CharacterFilterExpander.Expanding += CharacterFilterExpander_Expanded;
        }

        private void RegisterCharacterDetailExpanderLazyLoading()
        {
            CharacterClothExpander.Expanding += CharacterClothExpander_Expanding;
            CharacterFaceExpander.Expanding += CharacterFaceExpander_Expanding;
            CharacterAdornExpander.Expanding += CharacterAdornExpander_Expanding;
            CharacterVfxExpander.Expanding += CharacterVfxExpander_Expanding;
        }

        private void RefreshAssetLibraryDetailSections(AssetLibraryInfo assetLibrary)
        {
            var cancellationToken = GetAssetLibraryLoadToken();
            RunAssetLibraryLoad(RefreshBackgroundImageCardsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Music, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Ambient, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.SoundEffect, cancellationToken));
            RunAssetLibraryLoad(LoadFunctionsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharactersAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharacterFiltersAsync(assetLibrary, cancellationToken));
        }

        private void BackgroundImagesExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadBackgroundImagesAsync(_currentAssetLibrary, true));
            }
        }

        private void MusicExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadAudioFilesAsync(_currentAssetLibrary, AudioAssetKind.Music, true));
            }
        }

        private void AmbientSoundExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadAudioFilesAsync(_currentAssetLibrary, AudioAssetKind.Ambient, true));
            }
        }

        private void SoundEffectExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(ReloadAudioFilesAsync(_currentAssetLibrary, AudioAssetKind.SoundEffect, true));
            }
        }

        private void CharactersExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(LoadCharactersAsync(_currentAssetLibrary, GetAssetLibraryLoadToken(), true));
            }
        }

        private void FunctionExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(LoadFunctionsAsync(_currentAssetLibrary, GetAssetLibraryLoadToken(), true));
            }
        }

        private void CharacterFilterExpander_Expanded(Expander sender, ExpanderExpandingEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                RunAssetLibraryLoad(LoadCharacterFiltersAsync(_currentAssetLibrary, GetAssetLibraryLoadToken(), true));
            }
        }

        private void CharacterClothExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Cloth);
        }

        private void CharacterFaceExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Face);
        }

        private void CharacterAdornExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Adorn);
        }

        private void CharacterVfxExpander_Expanding(Expander sender, ExpanderExpandingEventArgs e)
        {
            LoadCharacterDetailLayerOnExpand(CharacterLayerKind.Vfx);
        }

        private void LoadCharacterDetailLayerOnExpand(CharacterLayerKind layerKind)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            RunCharacterDetailLoad(LoadCharacterDetailLayerAsync(_currentCharacter, layerKind, GetCharacterDetailLoadToken(), true));
        }

        private async Task ReloadBackgroundImagesAsync(AssetLibraryInfo assetLibrary, bool forcePopulateCards = false)
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

            await RefreshBackgroundImageCardsAsync(assetLibrary, GetAssetLibraryLoadToken(), forcePopulateCards);
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

        private async Task ReloadAudioFilesAsync(AssetLibraryInfo assetLibrary, AudioAssetKind kind, bool forcePopulateCards = false)
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

            await RefreshAudioCardsAsync(assetLibrary, kind, GetAssetLibraryLoadToken(), forcePopulateCards);
        }

        private async Task RefreshBackgroundImageCardsAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var backgroundFolderPath = GetBackgroundFolderPath(assetLibrary);
            var imagePaths = BackgroundImageService.GetFilePaths(backgroundFolderPath);

            BackgroundImagesGridView.Items.Clear();
            BackgroundImagesExpander.Header = $"背景图 [数量：{imagePaths.Count}]";
            AssetLibraryDetailStatusText.Text = $"背景图：{imagePaths.Count} 个文件 | {backgroundFolderPath}";
            if (forcePopulateCards || BackgroundImagesExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    BackgroundImagesGridView,
                    imagePaths,
                    CreateBackgroundImageCard,
                    cancellationToken,
                    batchSize: 8);
            }

            AppendLog(LogKind.Info, $"已加载背景图：{imagePaths.Count} 个文件。");
        }

        private void RefreshBackgroundImageCards(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(RefreshBackgroundImageCardsAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        private GridViewItem CreateBackgroundImageCard(string imagePath)
        {
            return AssetCardFactory.CreateCard(
                160,
                190,
                AssetCardContentFactory.CreateImageAssetCardContent(imagePath, 148, 148),
                imagePath,
                (_, _) =>
                {
                    PlaySelectionSound();
                    ShowBackgroundImageViewerPage(imagePath);
                },
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetBackgroundImageRemarkAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceBackgroundImageAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteBackgroundImageAsync(imagePath))));
        }

        private void RefreshMusicCards(AssetLibraryInfo assetLibrary)
        {
            RefreshAudioCards(assetLibrary, AudioAssetKind.Music);
        }

        private async Task RefreshAudioCardsAsync(AssetLibraryInfo assetLibrary, AudioAssetKind kind, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var musicFolderPath = GetAudioFolderPath(assetLibrary, kind);
            AudioAssetService.DeleteIgnoredSidecarFiles(kind, musicFolderPath);
            var musicPaths = AudioAssetService.GetFilePaths(musicFolderPath);
            var gridView = GetAudioGridView(kind);

            gridView.Items.Clear();
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

            if (forcePopulateCards || IsAudioExpanderExpanded(kind))
            {
                await AddGridViewItemsInBatchesAsync(
                    gridView,
                    musicPaths,
                    path => CreateAudioCard(kind, path),
                    cancellationToken);
            }

            AppendLog(LogKind.Info, $"已加载{AudioAssetService.GetDisplayName(kind)}：{musicPaths.Count} 个文件。");
        }

        private void RefreshAudioCards(AssetLibraryInfo assetLibrary, AudioAssetKind kind)
        {
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, kind, GetAssetLibraryLoadToken()));
        }

        private async Task LoadCharactersAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var characters = _characterWorkspaceService.GetCharactersByName(assetLibrary);

            CharacterGridView.Items.Clear();
            CharactersExpander.Header = $"立绘 [数量：{characters.Count}]";
            if (forcePopulateCards || CharactersExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    CharacterGridView,
                    characters,
                    CreateCharacterCard,
                    cancellationToken);
                CharacterGridView.Items.Add(CreateAddCharacterCard());
            }

            AppendLog(LogKind.Info, $"已加载角色卡：{characters.Count} 个。");
        }

        private void LoadCharacters(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(LoadCharactersAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        private async Task LoadFunctionsAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var functions = StoryFunctionService.ReadFunctions(assetLibrary, _jsonOptions);
            FunctionGridView.Items.Clear();
            FunctionExpander.Header = $"函数 [数量：{functions.Count}]";
            if (forcePopulateCards || FunctionExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    FunctionGridView,
                    functions,
                    CreateFunctionCard,
                    cancellationToken);
                FunctionGridView.Items.Add(CreateAddFunctionCard());
            }

            AppendLog(LogKind.Info, $"已加载函数卡：{functions.Count} 个。");
        }

        private void LoadFunctions(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(LoadFunctionsAsync(assetLibrary, GetAssetLibraryLoadToken()));
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

            var input = await _functionDialogService.EditFunctionAsync(
                "新建函数",
                null,
                BuildSuggestedChoiceFunctionIndicator());
            if (input is null)
            {
                return;
            }

            var functions = StoryFunctionService.ReadFunctions(_currentAssetLibrary, _jsonOptions);
            functions.Add(new FunctionEntry(Guid.NewGuid().ToString("N"), input.Name, input.Indicator, input.Category, input.ChoiceNotes));
            StoryFunctionService.WriteFunctions(_currentAssetLibrary, functions, _jsonOptions);
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

            var input = await _functionDialogService.EditFunctionAsync(
                "修改函数",
                function,
                string.Empty);
            if (input is null)
            {
                return;
            }

            var functions = StoryFunctionService.ReadFunctions(_currentAssetLibrary, _jsonOptions)
                .Select(entry => entry.Id == function.Id ? entry with
                {
                    Name = input.Name,
                    Indicator = input.Indicator,
                    Category = input.Category,
                    ChoiceNotes = input.ChoiceNotes
                } : entry)
                .ToList();
            StoryFunctionService.WriteFunctions(_currentAssetLibrary, functions, _jsonOptions);
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

            var functions = StoryFunctionService.ReadFunctions(_currentAssetLibrary, _jsonOptions)
                .Where(entry => entry.Id != function.Id)
                .ToList();
            StoryFunctionService.WriteFunctions(_currentAssetLibrary, functions, _jsonOptions);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadFunctions(_currentAssetLibrary);
            RequestDelayedRefresh();
        }

        private string BuildSuggestedChoiceFunctionIndicator()
        {
            if (_currentStoryChapter is null || _storyRows.Count == 0)
            {
                return string.Empty;
            }

            var prefix = BuildCurrentStoryChapterSectionChoicePrefix();
            var functions = _currentStoryAssetLibrary is null
                ? []
                : StoryFunctionService.ReadFunctions(_currentStoryAssetLibrary, _jsonOptions);
            return StoryFunctionService.BuildSuggestedChoiceIndicator(prefix, functions);
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

        private async Task LoadCharacterFiltersAsync(AssetLibraryInfo assetLibrary, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            var storedFilters = _characterFilterService.ReadStored(assetLibrary);
            var filters = CharacterFilterService.Normalize(storedFilters);
            CharacterFilterGridView.Items.Clear();
            CharacterFilterExpander.Header = $"角色滤镜 [数量：{filters.Count}]";
            if (forcePopulateCards || CharacterFilterExpander.IsExpanded)
            {
                var indexedFilters = filters
                    .Select((filter, index) => (filter, index))
                    .ToList();
                await AddGridViewItemsInBatchesAsync(
                    CharacterFilterGridView,
                    indexedFilters,
                    item => CreateCharacterFilterCard(item.filter, item.index),
                    cancellationToken);
                CharacterFilterGridView.Items.Add(CreateAddCharacterFilterCard());
            }

            AppendLog(LogKind.Info, $"已加载角色滤镜：{filters.Count} 个。");
            if (!_isRepairingCharacterFilters &&
                !_isReorderingCharacterFilters &&
                (filters.Count != storedFilters.Count || !filters.SequenceEqual(storedFilters)))
            {
                _ = RepairStoredCharacterFiltersAsync(assetLibrary, storedFilters, filters);
            }
        }

        private void LoadCharacterFilters(AssetLibraryInfo assetLibrary)
        {
            RunAssetLibraryLoad(LoadCharacterFiltersAsync(assetLibrary, GetAssetLibraryLoadToken()));
        }

        private void ApplyAssetLibraryMetadataToUi(AssetLibraryInfo assetLibrary)
        {
            _isApplyingAssetLibraryMetadata = true;
            try
            {
                AssetLibraryPortraitPreviewEnabledCheckBox.IsChecked = assetLibrary.IsPortraitPreviewEnabled;
                UpdateAssetLibraryDiskUsage(assetLibrary);
            }
            finally
            {
                _isApplyingAssetLibraryMetadata = false;
            }
        }

        private void UpdateAssetLibraryDiskUsage(AssetLibraryInfo assetLibrary)
        {
            AssetLibraryDiskUsageText.Text = $"当前占用：{FormatFileSize(CountDirectoryBytes(assetLibrary.Path))}";
        }

        private void AssetLibraryPortraitPreviewEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingAssetLibraryMetadata || _currentAssetLibrary is null)
            {
                return;
            }

            var isEnabled = AssetLibraryPortraitPreviewEnabledCheckBox.IsChecked == true;
            _currentAssetLibrary = _projectWorkspaceService.SetAssetLibraryPortraitPreviewEnabled(_currentAssetLibrary, isEnabled);
            AssetLibraryDetailStatusText.Text = isEnabled
                ? "已启用小预览；未设置的小预览会在虚幻同步前提示。"
                : "已关闭小预览；虚幻同步不会写入 DA_Portraits。";
            LoadCharacters(_currentAssetLibrary);
            ReloadCharacterDetailLayersPreservingScroll();
            AppendLog(LogKind.User, isEnabled ? "已启用素材库小预览。" : "已关闭素材库小预览。");
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
                var indexRemap = CharacterFilterService.BuildIndexRemap(oldFilters, newFilters);
                var oldLabels = oldFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
                var newLabels = newFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
                _characterFilterService.Write(assetLibrary, newFilters);

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

        private List<CharacterFilterEntry> GetStoryCharacterFilters()
        {
            return _currentStoryAssetLibrary is null ? [] : _characterFilterService.Read(_currentStoryAssetLibrary);
        }

        private string? ResolveStoryCharacterFilterName(int index)
        {
            if (index <= 0)
            {
                return null;
            }

            var filters = GetStoryCharacterFilters();
            var resolvedIndex = ResolveStoryAssetIndex(index, filters.Count);
            return resolvedIndex is null ? null : CharacterFilterService.GetDisplayName(filters[resolvedIndex.Value], resolvedIndex.Value);
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

            var filters = _characterFilterService.Read(_currentAssetLibrary);
            filters.Add(new CharacterFilterEntry(Guid.NewGuid().ToString("N"), remark));
            _characterFilterService.Write(_currentAssetLibrary, CharacterFilterService.Normalize(filters));
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

            if (CharacterFilterService.IsEmpty(filter))
            {
                return;
            }

            var remark = await ShowCharacterFilterRemarkDialogAsync("修改滤镜备注", filter.Remark);
            if (remark is null)
            {
                return;
            }

            var oldRemark = filter.Remark;
            var filters = _characterFilterService.Read(_currentAssetLibrary)
                .Select(entry => entry.Id == filter.Id ? entry with { Remark = remark } : entry)
                .ToList();
            _characterFilterService.Write(_currentAssetLibrary, CharacterFilterService.Normalize(filters));
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

            if (CharacterFilterService.IsEmpty(filter))
            {
                return;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "删除滤镜",
                $"确定删除 {filter.Remark} 吗？引用它的剧情行会重置为 VFX00，后续滤镜索引会同步前移。",
                "删除",
                "取消"));
            if (!confirmed)
            {
                return;
            }

            var oldFilters = _characterFilterService.Read(_currentAssetLibrary);
            var filters = CharacterFilterService.Normalize(oldFilters
                .Where(entry => entry.Id != filter.Id)
                .ToList());
            var indexRemap = CharacterFilterService.BuildIndexRemap(oldFilters, filters);
            var oldLabels = oldFilters.Select((entry, index) => (entry, index)).ToDictionary(item => item.index, item => item.entry.Remark);
            var newLabels = filters.Select((entry, index) => (entry, index)).ToDictionary(item => item.index, item => item.entry.Remark);
            _characterFilterService.Write(_currentAssetLibrary, filters);
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
            var input = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                title,
                "备注",
                currentRemark,
                "例如：冷色调（下雨）"));
            if (input is null)
            {
                return null;
            }

            var remark = SanitizeRemark(input);
            return string.IsNullOrWhiteSpace(remark) ? null : remark;
        }

        private GridViewItem CreateAddCharacterCard()
        {
            return AssetCardFactory.CreateCard(
                150,
                220,
                AssetCardContentFactory.CreateAddCharacterCardContent(),
                tappedHandler: async (_, _) => await CreateCharacterAsync());
        }

        private GridViewItem CreateCharacterCard(CharacterInfo character)
        {
            var content = AssetCardContentFactory.CreateCharacterCardContent(
                character.Code,
                character.Name,
                character.ColorHex);
            var warningMessage = GetCharacterPortraitPreviewWarningMessage(character);
            var cardContent = warningMessage is null
                ? content
                : CreateWarningBadgeOverlay(content, warningMessage);

            return AssetCardFactory.CreateCard(
                150,
                220,
                cardContent,
                character,
                (_, _) =>
                {
                    PlaySelectionSound();
                    ShowCharacterDetailPage(character);
                },
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("重命名", async (_, _) => await RenameCharacterAsync(character))));
        }

        private GridViewItem CreateMusicCard(string musicPath)
        {
            return CreateAudioCard(AudioAssetKind.Music, musicPath);
        }

        private string? GetCharacterPortraitPreviewWarningMessage(CharacterInfo character)
        {
            if (_currentAssetLibrary?.IsPortraitPreviewEnabled != true)
            {
                return null;
            }

            var missing = _characterLayerAssetService.GetMissingPortraitPreviewLayerNames(character);
            return missing.Count == 0
                ? null
                : $"{character.Name} 有 {missing.Count} 个立绘素材还没有设置小预览：{string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? "..." : string.Empty)}";
        }

        private string? GetCharacterLayerPortraitPreviewWarningMessage(string imagePath, CharacterLayerKind layerKind)
        {
            if (_currentAssetLibrary?.IsPortraitPreviewEnabled != true ||
                _currentCharacter is null ||
                layerKind is not (CharacterLayerKind.Cloth or CharacterLayerKind.Face or CharacterLayerKind.Adorn))
            {
                return null;
            }

            return _characterLayerAssetService.ResolvePortraitPreviewPath(_currentCharacter, Path.GetFileName(imagePath)) is null
                ? $"{GetCharacterLayerDisplayName(layerKind)} {Path.GetFileNameWithoutExtension(imagePath)} 还没有设置小预览。右键选择“设置预览”后，同步会写入 DA_Portraits。"
                : null;
        }

        private UIElement CreateWarningBadgeOverlay(UIElement content, string message)
        {
            var badge = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Gold),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkGoldenrod),
                BorderThickness = new Thickness(1),
                Content = new FontIcon
                {
                    Glyph = "\uE7BA",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black)
                }
            };
            ToolTipService.SetToolTip(badge, "缺少小预览");
            badge.Tapped += (_, args) =>
            {
                args.Handled = true;
            };
            badge.PointerPressed += (_, args) =>
            {
                args.Handled = true;
            };
            badge.Click += async (_, args) =>
            {
                PlaySelectionSound();
                await ShowPortraitPreviewWarningAsync(message);
            };

            return new Grid
            {
                Children =
                {
                    content,
                    badge
                }
            };
        }

        private async Task ShowPortraitPreviewWarningAsync(string message)
        {
            var targetInfoBar = CharacterDetailPage.Visibility == Visibility.Visible
                ? CharacterDetailInfoBar
                : null;
            if (targetInfoBar is not null)
            {
                targetInfoBar.Severity = InfoBarSeverity.Warning;
                targetInfoBar.Title = "缺少小预览";
                targetInfoBar.Message = message;
                targetInfoBar.IsOpen = true;
            }
            else
            {
                AssetLibraryDetailStatusText.Text = message;
            }

            AppendLog(LogKind.Warning, message);
            await _dialogService.ShowAsync(new DialogRequest(
                "缺少小预览",
                message,
                PrimaryButtonText: string.Empty,
                CloseButtonText: "知道了",
                PrimarySound: DialogSoundIntent.None,
                CloseSound: DialogSoundIntent.Selection));
        }

        private GridViewItem CreateFunctionCard(FunctionEntry function)
        {
            var choiceNotes = function.ChoiceNotes ?? [];
            var detailText = choiceNotes.Count > 0
                ? $"{function.Name} / {function.Category} / 选项备注 {choiceNotes.Count}"
                : $"{function.Name} / {function.Category}";
            return AssetCardFactory.CreateCard(
                240,
                104,
                AssetCardContentFactory.CreateFunctionCardContent(function.Indicator, detailText),
                function,
                async (_, _) => await EditFunctionAsync(function),
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("修改函数", async (_, _) => await EditFunctionAsync(function)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteFunctionAsync(function))),
                toolTip: choiceNotes.Count > 0
                    ? string.Join(Environment.NewLine, choiceNotes.Select((note, index) => $"{index + 1}. {note}"))
                    : null);
        }

        private GridViewItem CreateAddFunctionCard()
        {
            return AssetCardFactory.CreateCard(
                240,
                104,
                AssetCardContentFactory.CreateAddTextCardContent("+"),
                tappedHandler: async (_, _) => await AddFunctionAsync());
        }

        private GridViewItem CreateCharacterFilterCard(CharacterFilterEntry filter, int index)
        {
            var canEdit = !CharacterFilterService.IsEmpty(filter);
            return AssetCardFactory.CreateCard(
                220,
                92,
                AssetCardContentFactory.CreateCharacterFilterCardContent($"VFX{index:00}", filter.Remark),
                filter,
                contextFlyout: canEdit
                    ? GridViewItemFactory.CreateMenu(
                        GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteCharacterFilterAsync(filter)))
                    : null,
                stretchContent: true);
        }

        private GridViewItem CreateAddCharacterFilterCard()
        {
            return AssetCardFactory.CreateCard(
                220,
                64,
                AssetCardContentFactory.CreateAddCharacterFilterCardContent(),
                tappedHandler: async (_, _) => await AddCharacterFilterAsync(),
                stretchContent: true);
        }

        private GridViewItem CreateAudioCard(AudioAssetKind kind, string musicPath)
        {
            return AssetCardFactory.CreateCard(
                220,
                92,
                AssetCardContentFactory.CreateIconAssetCardContent(
                    Symbol.Audio,
                    Path.GetFileNameWithoutExtension(musicPath)),
                musicPath,
                (_, _) =>
                {
                    PlaySelectionSound();
                    ShowMusicPlayerPage(musicPath, kind);
                },
                GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetAudioRemarkAsync(kind, musicPath)),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceAudioAsync(kind, musicPath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteAudioAsync(kind, musicPath))));
        }

        private async void BackgroundImagesGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_currentAssetLibrary is null || _isNormalizingBackgroundImages)
            {
                _draggingBackgroundImageItem = null;
                return;
            }

            var orderedPaths = GetOrderedExistingTaggedPaths(BackgroundImagesGridView);

            if (orderedPaths.Count == 0)
            {
                _draggingBackgroundImageItem = null;
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, BackgroundImageService.GetAssetIndex);
            var assetLabels = StoryAssetIndexSyncService.BuildLabelMaps(orderedPaths, BackgroundImageService.GetAssetIndex);
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
            _draggingBackgroundImageItem = ResolveDraggedGridViewItem(BackgroundImagesGridView, e);

            AppendLog(
                LogKind.Info,
                _draggingBackgroundImageItem is null
                    ? $"背景图拖拽开始，但未识别拖拽项。Items[0]={e.Items.FirstOrDefault()?.GetType().Name ?? "null"}"
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
            await AudioGridView_DragItemsCompleted(AudioAssetKind.Music);
        }

        private void MusicGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            AudioGridView_DragItemsStarting(AudioAssetKind.Music, sender, e);
        }

        private void MusicGridView_DragOver(object sender, DragEventArgs e)
        {
            AudioGridView_DragOver(AudioAssetKind.Music, e);
        }

        private void MusicDropZone_DragEnter(object sender, DragEventArgs e)
        {
            AudioDropZone_DragEnter(AudioAssetKind.Music, e);
        }

        private void MusicDropZone_DragOver(object sender, DragEventArgs e)
        {
            AudioDropZone_DragOver(AudioAssetKind.Music, e);
        }

        private async void MusicDropZone_Drop(object sender, DragEventArgs e)
        {
            await AudioDropZone_Drop(AudioAssetKind.Music, e);
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
            filters = CharacterFilterService.Normalize(filters);
            if (filters.Count == 0)
            {
                _draggingCharacterFilterItem = null;
                return;
            }

            var oldFilters = _characterFilterService.Read(_currentAssetLibrary);
            var indexRemap = CharacterFilterService.BuildIndexRemap(oldFilters, filters);
            var oldLabels = oldFilters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
            var newLabels = filters.Select((filter, index) => (filter, index)).ToDictionary(item => item.index, item => item.filter.Remark);
            _isReorderingCharacterFilters = true;
            try
            {
                _characterFilterService.Write(_currentAssetLibrary, filters);
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
            _draggingCharacterFilterItem = ResolveDraggedGridViewItem(CharacterFilterGridView, e);

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
            await CharacterLayerGridView_DragItemsCompleted(
                CharacterClothGridView,
                CharacterLayerKind.Cloth,
                _isNormalizingCharacterClothes,
                () => _isNormalizingCharacterClothes = true,
                () => _isNormalizingCharacterClothes = false,
                () => _draggingCharacterClothItem = null,
                "服装",
                character => character.Code);
        }

        private void CharacterClothGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            CharacterLayerGridView_DragItemsStarting(CharacterClothGridView, e, item => _draggingCharacterClothItem = item);
        }

        private void CharacterClothGridView_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerGridView_DragOver(CharacterClothGridView, _draggingCharacterClothItem, e);
        }

        private void CharacterClothDropZone_DragEnter(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterClothItem, e);
        }

        private void CharacterClothDropZone_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterClothItem, e);
        }

        private async void CharacterClothDropZone_Drop(object sender, DragEventArgs e)
        {
            await CharacterLayerDropZone_Drop(
                e,
                _draggingCharacterClothItem,
                () => MoveGridViewItemToEnd(CharacterClothGridView, _draggingCharacterClothItem),
                ImportCharacterClothesAsync,
                "服装");
        }

        private async void CharacterFaceGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await CharacterLayerGridView_DragItemsCompleted(
                CharacterFaceGridView,
                CharacterLayerKind.Face,
                _isNormalizingCharacterFaces,
                () => _isNormalizingCharacterFaces = true,
                () => _isNormalizingCharacterFaces = false,
                () => _draggingCharacterFaceItem = null,
                "表情");
        }

        private void CharacterFaceGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            CharacterLayerGridView_DragItemsStarting(CharacterFaceGridView, e, item => _draggingCharacterFaceItem = item);
        }

        private void CharacterFaceGridView_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerGridView_DragOver(CharacterFaceGridView, _draggingCharacterFaceItem, e);
        }

        private void CharacterFaceDropZone_DragEnter(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterFaceItem, e);
        }

        private void CharacterFaceDropZone_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterFaceItem, e);
        }

        private async void CharacterFaceDropZone_Drop(object sender, DragEventArgs e)
        {
            await CharacterLayerDropZone_Drop(
                e,
                _draggingCharacterFaceItem,
                () => MoveGridViewItemToEnd(CharacterFaceGridView, _draggingCharacterFaceItem),
                ImportCharacterFacesAsync,
                "表情");
        }

        private async void CharacterAdornGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await CharacterLayerGridView_DragItemsCompleted(
                CharacterAdornGridView,
                CharacterLayerKind.Adorn,
                _isNormalizingCharacterAdorns,
                () => _isNormalizingCharacterAdorns = true,
                () => _isNormalizingCharacterAdorns = false,
                () => _draggingCharacterAdornItem = null,
                "装饰");
        }

        private async Task CharacterLayerGridView_DragItemsCompleted(
            GridView gridView,
            CharacterLayerKind layerKind,
            bool isNormalizing,
            Action beginNormalize,
            Action endNormalize,
            Action clearDraggingItem,
            string logLabel,
            Func<CharacterInfo, string?>? characterCodeSelector = null)
        {
            if (_currentCharacter is null || isNormalizing)
            {
                clearDraggingItem();
                return;
            }

            var orderedPaths = GetOrderedExistingTaggedPaths(gridView);
            if (orderedPaths.Count == 0)
            {
                clearDraggingItem();
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => CharacterLayerAssetService.GetIndex(path, layerKind));
            var assetLabels = StoryAssetIndexSyncService.BuildLabelMaps(orderedPaths, path => CharacterLayerAssetService.GetIndex(path, layerKind));
            beginNormalize();
            try
            {
                var entries = orderedPaths
                    .Select(path => CharacterLayerAssetService.ParseFileName(path, layerKind, string.Empty))
                    .ToList();
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, layerKind, characterCodeSelector?.Invoke(_currentCharacter));
            }
            finally
            {
                endNormalize();
            }

            if (_currentAssetLibrary is not null)
            {
                var syncResult = await SyncStoryCharacterLayerIndexesWithProgressAsync(
                    _currentAssetLibrary,
                    _currentCharacter,
                    layerKind,
                    indexRemap,
                    assetLabels.OldLabels,
                    assetLabels.NewLabels,
                    orderedPaths.Count);
                if (syncResult.ChangedCsvCount > 0)
                {
                    AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的{logLabel}索引。");
                }

                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            ReloadCharacterDetailLayersPreservingScroll();
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已调整{logLabel}顺序并触发自动命名。");
            clearDraggingItem();
        }

        private static void CharacterLayerDropZone_DragEnterOrOver(GridViewItem? draggingItem, DragEventArgs e)
        {
            if (draggingItem is not null)
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

        private void CharacterAdornGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            CharacterLayerGridView_DragItemsStarting(CharacterAdornGridView, e, item => _draggingCharacterAdornItem = item);
        }

        private static void CharacterLayerGridView_DragItemsStarting(
            GridView gridView,
            DragItemsStartingEventArgs e,
            Action<GridViewItem?> setDraggingItem)
        {
            setDraggingItem(ResolveDraggedGridViewItem(gridView, e));
        }

        private void CharacterAdornGridView_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerGridView_DragOver(CharacterAdornGridView, _draggingCharacterAdornItem, e);
        }

        private void CharacterAdornDropZone_DragEnter(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterAdornItem, e);
        }

        private void CharacterAdornDropZone_DragOver(object sender, DragEventArgs e)
        {
            CharacterLayerDropZone_DragEnterOrOver(_draggingCharacterAdornItem, e);
        }

        private async void CharacterAdornDropZone_Drop(object sender, DragEventArgs e)
        {
            await CharacterLayerDropZone_Drop(
                e,
                _draggingCharacterAdornItem,
                () => MoveGridViewItemToEnd(CharacterAdornGridView, _draggingCharacterAdornItem),
                ImportCharacterAdornsAsync,
                "装饰");
        }

        private async Task CharacterLayerDropZone_Drop(
            DragEventArgs e,
            GridViewItem? draggingItem,
            Action moveDraggingItemToEnd,
            Func<IEnumerable<string>, Task<int>> importAsync,
            string logLabel)
        {
            if (draggingItem is not null)
            {
                moveDraggingItemToEnd();
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
                var droppedPaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => BackgroundImageService.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var importedCount = await importAsync(droppedPaths);
                if (importedCount > 0)
                {
                    AppendLog(LogKind.User, $"拖入导入{logLabel}：{importedCount} 个文件。");
                }
                else
                {
                    AppendLog(LogKind.Warning, $"拖入内容中没有可导入的{logLabel}图片文件。");
                }
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Error, $"拖入导入{logLabel}失败。", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private static void CharacterLayerGridView_DragOver(GridView gridView, GridViewItem? draggingItem, DragEventArgs e)
        {
            if (draggingItem is null || gridView.Items.Count <= 1)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            var pointerPosition = e.GetPosition(gridView);
            if (IsPointerInTrailingBlankArea(gridView, pointerPosition, draggingItem))
            {
                MoveGridViewItemToEnd(gridView, draggingItem);
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

            PlaySelectionSound();
            if (ReferenceEquals(sender, CharacterClothGridView))
            {
                ShowCharacterLayerViewerPage(imagePath, CharacterLayerKind.Cloth);
            }
            else if (ReferenceEquals(sender, CharacterFaceGridView))
            {
                ShowCharacterLayerViewerPage(imagePath, CharacterLayerKind.Face);
            }
            else if (ReferenceEquals(sender, CharacterAdornGridView))
            {
                ShowCharacterLayerViewerPage(imagePath, CharacterLayerKind.Adorn);
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

        private async Task AudioGridView_DragItemsCompleted(AudioAssetKind kind)
        {
            if (_currentAssetLibrary is null || IsAudioNormalizing(kind))
            {
                SetDraggingAudioItem(kind, null);
                return;
            }

            var gridView = GetAudioGridView(kind);
            var orderedPaths = GetOrderedExistingTaggedPaths(gridView);

            if (orderedPaths.Count == 0)
            {
                SetDraggingAudioItem(kind, null);
                return;
            }

            var indexRemap = BuildAssetIndexRemap(orderedPaths, path => AudioAssetService.GetAssetIndex(kind, path));
            var assetLabels = StoryAssetIndexSyncService.BuildLabelMaps(orderedPaths, path => AudioAssetService.GetAssetIndex(kind, path));
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
            AppendLog(LogKind.User, $"已调整{AudioAssetService.GetDisplayName(kind)}顺序并触发自动命名。");
            SetDraggingAudioItem(kind, null);
        }

        private void AudioGridView_DragItemsStarting(AudioAssetKind kind, object sender, DragItemsStartingEventArgs e)
        {
            SetDraggingAudioItem(kind, ResolveDraggedGridViewItem(GetAudioGridView(kind), e));
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
                    .Where(AudioAssetService.IsValidAudioPath)
                    .ToList();

                var importedCount = ImportAudioFiles(kind, droppedMusicPaths);
                AppendLog(
                    importedCount > 0 ? LogKind.User : LogKind.Warning,
                    importedCount > 0
                        ? $"拖入导入{AudioAssetService.GetDisplayName(kind)}：{importedCount} 个文件。"
                        : $"拖入内容中没有可导入的 wav {AudioAssetService.GetDisplayName(kind)}文件。");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void MoveDraggingAudioToEnd(AudioAssetKind kind)
        {
            MoveGridViewItemToEnd(GetAudioGridView(kind), GetDraggingAudioItem(kind));
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
                    .Where(BackgroundImageService.IsValidSourcePath)
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
            MoveGridViewItemToEnd(BackgroundImagesGridView, _draggingBackgroundImageItem);
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

            var parsed = BackgroundImageService.ParseFileName(imagePath);
            var remarkInput = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                "设置背景图备注",
                "备注",
                parsed.Remark,
                "例如：我是备注"));
            if (remarkInput is null)
            {
                return null;
            }

            var folderPath = GetBackgroundFolderPath(_currentAssetLibrary);
            string? updatedPath;
            _isNormalizingBackgroundImages = true;
            try
            {
                updatedPath = _backgroundImageService.UpdateRemark(folderPath, imagePath, remarkInput);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置背景图备注：{Path.GetFileName(imagePath)}");
            return updatedPath;
        }

        private async Task<bool> DeleteBackgroundImageAsync(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return false;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "删除背景图",
                $"确定删除 {Path.GetFileName(imagePath)} 吗？",
                "删除",
                "取消",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
            if (!confirmed)
            {
                return false;
            }

            _isNormalizingBackgroundImages = true;
            try
            {
                _backgroundImageService.DeleteAndNormalize(GetBackgroundFolderPath(_currentAssetLibrary), imagePath);
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

        private async Task<bool> ReplaceBackgroundImageAsync(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return false;
            }

            var sourcePath = await PickReplacementFileAsync(BackgroundImageService.Extensions, PickerLocationId.PicturesLibrary);
            if (sourcePath is null)
            {
                return false;
            }

            if (!BackgroundImageService.IsValidSourcePath(sourcePath))
            {
                AppendLog(LogKind.Warning, $"替换背景图失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                return false;
            }

            _isNormalizingBackgroundImages = true;
            try
            {
                await ImportBackgroundImageAsPngAsync(sourcePath, imagePath);
            }
            finally
            {
                _isNormalizingBackgroundImages = false;
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshBackgroundImageCards(_currentAssetLibrary);
            RequestDelayedRefresh();
            if (_viewingBackgroundImagePath is not null && PathsEqual(_viewingBackgroundImagePath, imagePath))
            {
                await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, imagePath);
            }

            AppendLog(LogKind.User, $"已替换背景图素材：{Path.GetFileName(imagePath)} <- {Path.GetFileName(sourcePath)}");
            return true;
        }

        private async Task<string?> PromptRemarkAsync(string title, string currentRemark, string placeholderText)
        {
            return await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                title,
                "备注",
                currentRemark,
                placeholderText));
        }

        private async Task<bool> ConfirmDeleteAsync(string title, string message)
        {
            return await _dialogService.ConfirmAsync(new DialogRequest(
                title,
                message,
                "删除",
                "取消",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
        }

        private async Task<string?> SetCharacterClothRemarkAsync(string clothPath)
        {
            if (_currentCharacter is null || !File.Exists(clothPath))
            {
                return null;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            var parsed = CharacterLayerAssetService.ParseFileName(clothPath, CharacterLayerKind.Cloth, string.Empty);
            var remark = await PromptRemarkAsync("设置服装备注", parsed.Remark, "例如：校服");
            if (remark is null)
            {
                return null;
            }

            var clothFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Cloth);
            var updatedEntries = _characterLayerAssetService.CreateRemarkEntries(
                clothFolderPath,
                clothPath,
                CharacterLayerKind.Cloth,
                string.Empty,
                remark);
            var updatedIndex = _characterLayerAssetService.FindEntryIndex(updatedEntries, clothPath);

            _isNormalizingCharacterClothes = true;
            try
            {
                _characterLayerAssetService.RenameEntries(updatedEntries, CharacterLayerKind.Cloth, _currentCharacter.Code);
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
                ? CharacterLayerAssetService.GetTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Cloth, _currentCharacter.Code)
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

            var confirmed = await ConfirmDeleteAsync("删除服装", $"确定删除 {Path.GetFileName(clothPath)} 吗？");
            if (!confirmed)
            {
                return false;
            }

            _isNormalizingCharacterClothes = true;
            try
            {
                var entries = _characterLayerAssetService.DeleteFileAndCreateRemainingEntries(
                    clothPath,
                    CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Cloth),
                    CharacterLayerKind.Cloth,
                    string.Empty);
                _characterLayerAssetService.RenameEntries(entries, CharacterLayerKind.Cloth, _currentCharacter.Code);
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
            var parsed = CharacterLayerAssetService.ParseFileName(facePath, CharacterLayerKind.Face, string.Empty);
            var remark = await PromptRemarkAsync("设置表情备注", parsed.Remark, "例如：微笑");
            if (remark is null)
            {
                return null;
            }

            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Face);
            var updatedEntries = _characterLayerAssetService.CreateRemarkEntries(
                faceFolderPath,
                facePath,
                CharacterLayerKind.Face,
                string.Empty,
                remark);
            var updatedIndex = _characterLayerAssetService.FindEntryIndex(updatedEntries, facePath);

            _isNormalizingCharacterFaces = true;
            try
            {
                _characterLayerAssetService.RenameEntriesAndScopeMeta(updatedEntries, CharacterLayerKind.Face);
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
                ? CharacterLayerAssetService.GetTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Face)
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

            var confirmed = await ConfirmDeleteAsync("删除表情", $"确定删除 {Path.GetFileName(facePath)} 吗？");
            if (!confirmed)
            {
                return false;
            }

            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Face);
            _characterLayerAssetService.RemoveScopeEntry(faceFolderPath, CharacterLayerKind.Face, Path.GetFileName(facePath));

            _isNormalizingCharacterFaces = true;
            try
            {
                var entries = _characterLayerAssetService.DeleteFileAndCreateRemainingEntries(
                    facePath,
                    faceFolderPath,
                    CharacterLayerKind.Face,
                    string.Empty);
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, CharacterLayerKind.Face);
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

            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Face);
            await SetCharacterLayerAvailabilityAsync(
                facePath,
                faceFolderPath,
                CharacterLayerKind.Face,
                "表情可用范围",
                "表情");
        }

        private async Task SetCharacterLayerAvailabilityAsync(
            string layerPath,
            string layerFolderPath,
            CharacterLayerKind layerKind,
            string title,
            string logLabel)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return;
            }

            var clothFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Cloth);
            var clothPaths = CharacterLayerAssetService.GetImagePaths(clothFolderPath);
            if (clothPaths.Count == 0)
            {
                AppendLog(LogKind.Warning, $"还没有服装，暂时无法设置{logLabel}可用范围。");
                return;
            }

            var meta = _characterLayerAssetService.ReadScopeMeta(layerFolderPath, layerKind);
            var layerFileName = Path.GetFileName(layerPath);
            var existingEntry = meta.Entries.TryGetValue(layerFileName, out var savedEntry)
                ? savedEntry
                : new CharacterLayerScopeEntry { UseAllCostumes = true };
            var dialogContent = CharacterDialogContentFactory.CreateCharacterLayerAvailabilityContent(
                clothPaths,
                existingEntry,
                ComputeFileHash,
                clothPath => ThumbnailFactory.CreateThumbnail(clothPath, 120, 150, showAddIcon: false),
                PlaySelectionSound);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                title,
                dialogContent.Content,
                "确定",
                "取消"));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            var checkedHashes = dialogContent.ReadCheckedHashes();
            _characterLayerAssetService.SaveScopeEntry(layerFolderPath, layerKind, layerFileName, new CharacterLayerScopeEntry
            {
                UseAllCostumes = checkedHashes.Count == clothPaths.Count,
                CostumeHashes = checkedHashes.Count == clothPaths.Count ? [] : checkedHashes
            });
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
            var parsed = CharacterLayerAssetService.ParseFileName(adornPath, CharacterLayerKind.Adorn, string.Empty);
            var remark = await PromptRemarkAsync("设置装饰备注", parsed.Remark, "例如：帽子");
            if (remark is null)
            {
                return null;
            }

            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Adorn);
            var updatedEntries = _characterLayerAssetService.CreateRemarkEntries(
                adornFolderPath,
                adornPath,
                CharacterLayerKind.Adorn,
                string.Empty,
                remark);
            var updatedIndex = _characterLayerAssetService.FindEntryIndex(updatedEntries, adornPath);

            _isNormalizingCharacterAdorns = true;
            try
            {
                _characterLayerAssetService.RenameEntriesAndScopeMeta(updatedEntries, CharacterLayerKind.Adorn);
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
                ? CharacterLayerAssetService.GetTargetPath(updatedEntries, updatedIndex, CharacterLayerKind.Adorn)
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

            var confirmed = await ConfirmDeleteAsync("删除装饰", $"确定删除 {Path.GetFileName(adornPath)} 吗？");
            if (!confirmed)
            {
                return false;
            }

            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Adorn);
            _characterLayerAssetService.RemoveScopeEntry(adornFolderPath, CharacterLayerKind.Adorn, Path.GetFileName(adornPath));

            _isNormalizingCharacterAdorns = true;
            try
            {
                var entries = _characterLayerAssetService.DeleteFileAndCreateRemainingEntries(
                    adornPath,
                    adornFolderPath,
                    CharacterLayerKind.Adorn,
                    string.Empty);
                _characterLayerAssetService.RenameEntriesAndScopeMeta(entries, CharacterLayerKind.Adorn);
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

        private async Task<bool> ReplaceCharacterLayerAsync(CharacterLayerKind layerKind, string layerPath)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return false;
            }

            var sourcePath = await PickReplacementFileAsync(BackgroundImageService.Extensions, PickerLocationId.PicturesLibrary);
            if (sourcePath is null)
            {
                return false;
            }

            if (!BackgroundImageService.IsValidSourcePath(sourcePath))
            {
                AppendLog(LogKind.Warning, $"替换{GetCharacterLayerDisplayName(layerKind)}失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                return false;
            }

            var restoreHorizontalOffset = CharacterDetailScrollViewer.HorizontalOffset;
            var restoreVerticalOffset = CharacterDetailScrollViewer.VerticalOffset;
            File.Copy(sourcePath, layerPath, overwrite: true);

            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            SetSelectedCharacterLayer(layerKind, layerPath);
            ReloadCharacterDetailLayersPreservingScroll(restoreHorizontalOffset, restoreVerticalOffset);
            await UpdateCharacterLayerPreviewAsync();
            RequestDelayedRefresh();

            if (_viewingCharacterLayer is { } viewingLayer &&
                viewingLayer.Kind == layerKind &&
                PathsEqual(viewingLayer.Path, layerPath))
            {
                await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, layerPath);
            }

            if (_isViewingCharacterComposite)
            {
                await LoadCharacterCompositeViewerImagesAsync();
            }

            AppendLog(LogKind.User, $"已替换{GetCharacterLayerDisplayName(layerKind)}素材：{Path.GetFileName(layerPath)} <- {Path.GetFileName(sourcePath)}");
            return true;
        }

        private async Task SetCharacterPortraitPreviewAsync(CharacterLayerKind layerKind, string layerPath)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return;
            }

            var sourcePath = await PickReplacementFileAsync(BackgroundImageService.Extensions, PickerLocationId.PicturesLibrary);
            if (sourcePath is null)
            {
                return;
            }

            if (!BackgroundImageService.IsValidSourcePath(sourcePath))
            {
                AppendLog(LogKind.Warning, $"设置{GetCharacterLayerDisplayName(layerKind)}小预览失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                return;
            }

            _characterLayerAssetService.SetPortraitPreview(_currentCharacter, Path.GetFileName(layerPath), sourcePath);
            if (_currentAssetLibrary is not null)
            {
                TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            }

            CharacterDetailInfoBar.Severity = InfoBarSeverity.Success;
            CharacterDetailInfoBar.Title = "已设置小预览";
            CharacterDetailInfoBar.Message = $"{Path.GetFileNameWithoutExtension(layerPath)} <- {Path.GetFileName(sourcePath)}";
            CharacterDetailInfoBar.IsOpen = true;
            ReloadCharacterDetailLayersPreservingScroll();
            if (_currentAssetLibrary is not null)
            {
                LoadCharacters(_currentAssetLibrary);
            }

            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置{GetCharacterLayerDisplayName(layerKind)}小预览：{Path.GetFileName(layerPath)} <- {Path.GetFileName(sourcePath)}");
        }

        private async Task SetCharacterAdornAvailabilityAsync(string adornPath)
        {
            if (_currentCharacter is null || !File.Exists(adornPath))
            {
                return;
            }

            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(_currentCharacter, CharacterLayerKind.Adorn);
            await SetCharacterLayerAvailabilityAsync(
                adornPath,
                adornFolderPath,
                CharacterLayerKind.Adorn,
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

            var parsed = AudioAssetService.ParseFileName(kind, musicPath);
            var remarkInput = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                $"设置{AudioAssetService.GetDisplayName(kind)}备注",
                "备注",
                parsed.Remark,
                kind == AudioAssetKind.Music ? "例如：主题曲" : "例如：雨声"));
            if (remarkInput is null)
            {
                return null;
            }

            var folderPath = GetAudioFolderPath(_currentAssetLibrary, kind);
            string? renamedPath;
            SetAudioNormalizing(kind, true);
            try
            {
                renamedPath = _audioAssetService.UpdateRemark(kind, folderPath, musicPath, remarkInput);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已设置{AudioAssetService.GetDisplayName(kind)}备注：{Path.GetFileName(musicPath)}");
            return renamedPath;
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

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                $"删除{AudioAssetService.GetDisplayName(kind)}",
                $"确定删除 {Path.GetFileName(musicPath)} 吗？",
                "删除",
                "取消",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
            if (!confirmed)
            {
                return false;
            }

            SetAudioNormalizing(kind, true);
            try
            {
                _audioAssetService.DeleteAndNormalize(kind, GetAudioFolderPath(_currentAssetLibrary, kind), musicPath);
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            AppendLog(LogKind.User, $"已删除{AudioAssetService.GetDisplayName(kind)}：{Path.GetFileName(musicPath)}");
            return true;
        }

        private async Task<bool> ReplaceAudioAsync(AudioAssetKind kind, string musicPath)
        {
            if (_currentAssetLibrary is null || !File.Exists(musicPath))
            {
                return false;
            }

            var sourcePath = await PickReplacementFileAsync(AudioAssetService.Extensions, PickerLocationId.MusicLibrary);
            if (sourcePath is null)
            {
                return false;
            }

            if (!AudioAssetService.IsValidAudioPath(sourcePath))
            {
                AppendLog(LogKind.Warning, $"替换{AudioAssetService.GetDisplayName(kind)}失败：不支持的文件类型 {Path.GetFileName(sourcePath)}");
                return false;
            }

            SetAudioNormalizing(kind, true);
            try
            {
                File.Copy(sourcePath, musicPath, overwrite: true);
                AudioAssetService.DeleteIgnoredSidecarFiles(kind, GetAudioFolderPath(_currentAssetLibrary, kind));
            }
            finally
            {
                SetAudioNormalizing(kind, false);
            }

            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            RefreshAudioCards(_currentAssetLibrary, kind);
            RequestDelayedRefresh();
            if (_playingMusicPath is not null && PathsEqual(_playingMusicPath, musicPath))
            {
                MusicPlayerElement.MediaPlayer.Pause();
                var file = await StorageFile.GetFileFromPathAsync(musicPath);
                MusicPlayerElement.Source = MediaSource.CreateFromStorageFile(file);
                MusicPlayPauseButton.Content = "播放";
            }

            AppendLog(LogKind.User, $"已替换{AudioAssetService.GetDisplayName(kind)}素材：{Path.GetFileName(musicPath)} <- {Path.GetFileName(sourcePath)}");
            return true;
        }

        private async Task NormalizeMusicFilesAsync(string musicFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            await NormalizeAudioFilesAsync(AudioAssetKind.Music, musicFolderPath, orderedPaths);
        }

        private async Task NormalizeAudioFilesAsync(AudioAssetKind kind, string musicFolderPath, IReadOnlyList<string>? orderedPaths = null)
        {
            _audioAssetService.NormalizeFiles(kind, musicFolderPath, orderedPaths);
            await Task.CompletedTask;
        }

        private void ShowBackgroundImageViewerPage(string imagePath)
        {
            if (_currentAssetLibrary is null || !File.Exists(imagePath))
            {
                return;
            }

            _viewingBackgroundImagePath = imagePath;
            _viewingCharacterLayer = null;
            _isViewingCharacterComposite = false;
            BackgroundImageViewerTabTitleText.Text = Path.GetFileNameWithoutExtension(imagePath);
            SetBackgroundImageViewerEditingEnabled(true);
            ResetBackgroundImageViewerTransform();
            ClearBackgroundImageViewerLayerImages();
            _ = ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, imagePath);

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开背景图查看：{Path.GetFileName(imagePath)}");
        }

        private void ShowCharacterLayerViewerPage(string layerPath, CharacterLayerKind layerKind)
        {
            if (_currentCharacter is null || !File.Exists(layerPath))
            {
                return;
            }

            _viewingBackgroundImagePath = null;
            _viewingCharacterLayer = new CharacterLayerViewerState(layerKind, layerPath);
            _isViewingCharacterComposite = false;
            SetSelectedCharacterLayerPath(layerKind, layerPath);
            UpdateCharacterLayerViewerTitle(layerKind, layerPath);
            SetBackgroundImageViewerEditingEnabled(true);
            ResetBackgroundImageViewerTransform();
            ClearBackgroundImageViewerLayerImages();
            _ = ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, layerPath);
            _ = UpdateCharacterLayerPreviewAsync();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开{GetCharacterLayerDisplayName(layerKind)}查看：{Path.GetFileName(layerPath)}");
        }

        private async void ShowCharacterCompositeViewerPage()
        {
            if (_currentCharacter is null ||
                string.IsNullOrWhiteSpace(_selectedCharacterClothPath) ||
                !File.Exists(_selectedCharacterClothPath))
            {
                CharacterDetailInfoBar.Severity = InfoBarSeverity.Warning;
                CharacterDetailInfoBar.Title = "无法查看立绘";
                CharacterDetailInfoBar.Message = "当前角色还没有可查看的服装图层。";
                CharacterDetailInfoBar.IsOpen = true;
                return;
            }

            _viewingBackgroundImagePath = null;
            _viewingCharacterLayer = null;
            _isViewingCharacterComposite = true;
            BackgroundImageViewerTabTitleText.Text = $"{_currentCharacter.Name} / 分层预览";
            SetBackgroundImageViewerEditingEnabled(false);
            ResetBackgroundImageViewerTransform();
            await LoadCharacterCompositeViewerImagesAsync();

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Visible;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"打开组合立绘查看：{_currentCharacter.Name}");
        }

        private async Task LoadCharacterCompositeViewerImagesAsync()
        {
            await SetViewerLayerImageAsync(BackgroundImageViewerImage, _selectedCharacterClothPath);
            await SetViewerLayerImageAsync(
                BackgroundImageViewerFaceImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterFacePath) ? _selectedCharacterFacePath : null);
            await SetViewerLayerImageAsync(
                BackgroundImageViewerAdornImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterAdornPath) ? _selectedCharacterAdornPath : null);
            await SetViewerLayerImageAsync(
                BackgroundImageViewerVfxImage,
                IsCharacterLayerCompatibleWithSelectedCloth(_selectedCharacterVfxPath) ? _selectedCharacterVfxPath : null);
        }

        private async Task SetViewerLayerImageAsync(Image image, string? path)
        {
            image.RenderTransform = BackgroundImageViewerTransform;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                return;
            }

            await ThumbnailFactory.LoadThumbnailFromFileAsync(image, path);
            image.Visibility = Visibility.Visible;
        }

        private void ClearBackgroundImageViewerLayerImages()
        {
            _ = SetViewerLayerImageAsync(BackgroundImageViewerFaceImage, null);
            _ = SetViewerLayerImageAsync(BackgroundImageViewerAdornImage, null);
            _ = SetViewerLayerImageAsync(BackgroundImageViewerVfxImage, null);
            BackgroundImageViewerImage.RenderTransform = BackgroundImageViewerTransform;
        }

        private void SetBackgroundImageViewerEditingEnabled(bool isEnabled)
        {
            BackgroundImageViewerRemarkButton.IsEnabled = isEnabled;
            BackgroundImageViewerReplaceButton.IsEnabled = isEnabled;
            BackgroundImageViewerDeleteButton.IsEnabled = isEnabled;
        }

        private void SetSelectedCharacterLayerPath(CharacterLayerKind layerKind, string layerPath)
        {
            switch (layerKind)
            {
                case CharacterLayerKind.Cloth:
                    _selectedCharacterClothPath = layerPath;
                    break;
                case CharacterLayerKind.Face:
                    _selectedCharacterFacePath = layerPath;
                    break;
                case CharacterLayerKind.Adorn:
                    _selectedCharacterAdornPath = layerPath;
                    break;
            }
        }

        private void UpdateCharacterLayerViewerTitle(CharacterLayerKind layerKind, string layerPath)
        {
            BackgroundImageViewerTabTitleText.Text = $"{GetCharacterLayerDisplayName(layerKind)} {Path.GetFileNameWithoutExtension(layerPath)}";
        }

        private async Task SetViewingCharacterLayerRemarkAsync(CharacterLayerKind layerKind, string layerPath)
        {
            var updatedPath = layerKind switch
            {
                CharacterLayerKind.Cloth => await SetCharacterClothRemarkAsync(layerPath),
                CharacterLayerKind.Face => await SetCharacterFaceRemarkAsync(layerPath),
                CharacterLayerKind.Adorn => await SetCharacterAdornRemarkAsync(layerPath),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(updatedPath) || !File.Exists(updatedPath))
            {
                return;
            }

            _viewingCharacterLayer = new CharacterLayerViewerState(layerKind, updatedPath);
            SetSelectedCharacterLayerPath(layerKind, updatedPath);
            UpdateCharacterLayerViewerTitle(layerKind, updatedPath);
            await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedPath);
        }

        private async Task<bool> DeleteViewingCharacterLayerAsync(CharacterLayerKind layerKind, string layerPath)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => await DeleteCharacterClothAsync(layerPath),
                CharacterLayerKind.Face => await DeleteCharacterFaceAsync(layerPath),
                CharacterLayerKind.Adorn => await DeleteCharacterAdornAsync(layerPath),
                _ => false
            };
        }

        private void CloseBackgroundImageViewerButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
            CloseBackgroundImageViewer();
        }

        private void CloseBackgroundImageViewer()
        {
            _isPanningBackgroundImage = false;
            var shouldReturnToCharacterDetail = (_viewingCharacterLayer is not null || _isViewingCharacterComposite) && _currentCharacter is not null;
            _viewingBackgroundImagePath = null;
            _viewingCharacterLayer = null;
            _isViewingCharacterComposite = false;
            BackgroundImageViewerImage.Source = null;
            ClearBackgroundImageViewerLayerImages();
            ResetBackgroundImageViewerTransform();

            if (shouldReturnToCharacterDetail)
            {
                BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
                CharacterDetailPage.Visibility = Visibility.Visible;
                CharacterPreviewSurface.Focus(FocusState.Programmatic);
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

            if (_isViewingCharacterComposite && HandleCharacterCompositeViewerKeyDown(e))
            {
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

        private bool HandleCharacterCompositeViewerKeyDown(KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Q:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Adorn, -1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.E:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Adorn, 1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.A:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Face, -1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.D:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Face, 1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.Z:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Cloth, -1);
                    e.Handled = true;
                    return true;
                case Windows.System.VirtualKey.C:
                    _ = CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind.Cloth, 1);
                    e.Handled = true;
                    return true;
                default:
                    return false;
            }
        }

        private async Task CycleCharacterCompositeViewerLayerAsync(CharacterLayerKind layerKind, int direction)
        {
            await CycleCharacterDetailLayerAsync(layerKind, direction);
            await LoadCharacterCompositeViewerImagesAsync();
            BackgroundImageViewerPage.Focus(FocusState.Programmatic);
        }

        private void BackgroundImageViewerPage_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            PlayNegativeSound();
            CloseBackgroundImageViewer();
            e.Handled = true;
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

            var orderedPaths = BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(_currentAssetLibrary));
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

            PlaySelectionSound();
            ShowBackgroundImageViewerPage(orderedPaths[nextIndex]);
        }

        private void ShowAdjacentViewerImage(int direction)
        {
            if (_viewingCharacterLayer is not null)
            {
                ShowAdjacentCharacterLayer(direction, _viewingCharacterLayer.Kind, _viewingCharacterLayer.Path);
            }
            else
            {
                ShowAdjacentBackgroundImage(direction);
            }
        }

        private void ShowAdjacentCharacterLayer(int direction, CharacterLayerKind layerKind, string currentPath)
        {
            if (_currentCharacter is null)
            {
                return;
            }

            var orderedPaths = CharacterLayerAssetService.GetLayerPaths(_currentCharacter, layerKind);
            var currentIndex = orderedPaths.FindIndex(path => PathsEqual(path, currentPath));
            if (currentIndex < 0)
            {
                return;
            }

            var nextIndex = currentIndex + direction;
            if (nextIndex < 0 || nextIndex >= orderedPaths.Count)
            {
                return;
            }

            PlaySelectionSound();
            ShowCharacterLayerViewerPage(orderedPaths[nextIndex], layerKind);
        }

        private async void BackgroundImageViewerRemarkButton_Click(object sender, RoutedEventArgs e)
        {
            var currentLayer = _viewingCharacterLayer;
            if (currentLayer is not null)
            {
                await SetViewingCharacterLayerRemarkAsync(currentLayer.Kind, currentLayer.Path);
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
            await ThumbnailFactory.LoadThumbnailFromFileAsync(BackgroundImageViewerImage, updatedPath);
        }

        private async void BackgroundImageViewerReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            var currentLayer = _viewingCharacterLayer;
            if (currentLayer is not null)
            {
                await ReplaceCharacterLayerAsync(currentLayer.Kind, currentLayer.Path);
                return;
            }

            if (_viewingBackgroundImagePath is null)
            {
                return;
            }

            await ReplaceBackgroundImageAsync(_viewingBackgroundImagePath);
        }

        private async void BackgroundImageViewerDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var currentLayer = _viewingCharacterLayer;
            if (currentLayer is not null)
            {
                var deletedLayer = await DeleteViewingCharacterLayerAsync(currentLayer.Kind, currentLayer.Path);
                if (deletedLayer)
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
            AppendLog(LogKind.User, $"打开{AudioAssetService.GetDisplayName(kind)}播放：{Path.GetFileName(musicPath)}");
        }

        private void CloseMusicPlayerButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNegativeSound();
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

            var orderedPaths = AudioAssetService.GetFilePaths(GetAudioFolderPath(_currentAssetLibrary, _playingAudioKind));
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

            PlaySelectionSound();
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

            PlaySelectionSound();
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

            try
            {
                _characterWorkspaceService.CreateCharacter(_currentAssetLibrary, input);
            }
            catch (IOException ex)
            {
                AppendLog(LogKind.Warning, $"无法创建角色：{ex.Message}");
                return;
            }

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

            CharacterInfo updatedCharacter;
            try
            {
                updatedCharacter = _characterWorkspaceService.RenameCharacter(_currentAssetLibrary, character, input);
            }
            catch (IOException ex)
            {
                AppendLog(LogKind.Warning, $"无法重命名角色：{ex.Message}");
                return;
            }

            _characterLayerAssetService.NormalizeFiles(
                CharacterLayerAssetService.GetCharacterFolderPath(updatedCharacter, CharacterLayerKind.Cloth),
                CharacterLayerKind.Cloth,
                string.Empty,
                input.Code);
            TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
            LoadCharacters(_currentAssetLibrary);
            if (_currentCharacter is not null && PathsEqual(_currentCharacter.Path, character.Path))
            {
                ShowCharacterDetailPage(updatedCharacter);
            }
            AppendLog(LogKind.User, $"重命名角色：{input.Name}（{input.Code}）");
            await Task.CompletedTask;
        }

        private async Task<CharacterEditorInput?> ShowCharacterEditorDialogAsync(string title, CharacterInfo? character)
        {
            var editorContent = EditorDialogContentFactory.CreateCharacterEditorContent(character);

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
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code))
            {
                AppendLog(LogKind.Warning, "角色名字和英文代号不能为空。");
                return null;
            }

            return input;
        }

        private void ShowCharacterDetailPage(CharacterInfo character)
        {
            _currentCharacter = character;
            var cancellationToken = ResetCharacterDetailLoadCancellation();
            CharacterDetailTabTitleText.Text = $"{character.Name} / {character.Code}";
            CharacterNameTextBox.Text = character.Name;
            CharacterCodeTextBox.Text = character.Code;
            CharacterColorTextBox.Text = character.ColorHex;
            RunCharacterDetailLoad(LoadCharacterDetailLayersAsync(character, cancellationToken));
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

        private async Task LoadCharacterDetailLayersAsync(CharacterInfo character, CancellationToken cancellationToken)
        {
            _characterWorkspaceService.EnsureCharacterSubfolders(character.Path);

            var clothFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Cloth);
            var faceFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Face);
            var adornFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Adorn);
            var vfxFolderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Vfx);

            _characterLayerAssetService.NormalizeFiles(clothFolderPath, CharacterLayerKind.Cloth, CharacterLayerAssetService.GetDefaultScope(0), character.Code);
            var costumeCount = CharacterLayerAssetService.GetImagePaths(clothFolderPath).Count;
            var defaultCostumeScope = CharacterLayerAssetService.GetDefaultScope(costumeCount);
            _characterLayerAssetService.NormalizeFiles(faceFolderPath, CharacterLayerKind.Face, defaultCostumeScope);
            _characterLayerAssetService.NormalizeFiles(adornFolderPath, CharacterLayerKind.Adorn, defaultCostumeScope);
            _characterLayerAssetService.NormalizeFiles(vfxFolderPath, CharacterLayerKind.Vfx, "ALL");

            var clothPaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Cloth);
            var facePaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Face);
            var adornPaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Adorn);
            var vfxPaths = CharacterLayerAssetService.GetLayerPaths(character, CharacterLayerKind.Vfx);

            _selectedCharacterClothPath = ResolveSelectedCharacterLayerPath(_selectedCharacterClothPath, clothPaths);
            _selectedCharacterFacePath = ResolveSelectedCharacterLayerPath(_selectedCharacterFacePath, facePaths);
            _selectedCharacterAdornPath = ResolveSelectedCharacterLayerPath(_selectedCharacterAdornPath, adornPaths);
            _selectedCharacterVfxPath = ResolveSelectedCharacterLayerPath(_selectedCharacterVfxPath, vfxPaths);

            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Cloth, cancellationToken);
            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Face, cancellationToken);
            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Adorn, cancellationToken);
            await LoadCharacterDetailLayerAsync(character, CharacterLayerKind.Vfx, cancellationToken);
            await UpdateCharacterLayerPreviewAsync();
        }

        private void LoadCharacterDetailLayers(CharacterInfo character)
        {
            RunCharacterDetailLoad(LoadCharacterDetailLayersAsync(character, GetCharacterDetailLoadToken()));
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

            RunCharacterDetailLoad(LoadCharacterDetailLayersAsync(_currentCharacter, GetCharacterDetailLoadToken()));
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

        private async Task LoadCharacterDetailLayerAsync(
            CharacterInfo character,
            CharacterLayerKind layerKind,
            CancellationToken cancellationToken,
            bool forcePopulateCards = false)
        {
            var folderPath = CharacterLayerAssetService.GetCharacterFolderPath(character, layerKind);
            if (layerKind == CharacterLayerKind.Vfx)
            {
                await LoadCharacterVfxLayerAsync(folderPath, cancellationToken, forcePopulateCards);
            }
            else
            {
                await LoadCharacterImageLayerAsync(GetCharacterLayerGridView(layerKind), folderPath, layerKind, cancellationToken, forcePopulateCards);
            }
        }

        private async Task LoadCharacterImageLayerAsync(
            GridView gridView,
            string folderPath,
            CharacterLayerKind layerKind,
            CancellationToken cancellationToken,
            bool forcePopulateCards = false)
        {
            Directory.CreateDirectory(folderPath);
            gridView.Items.Clear();

            var imagePaths = CharacterLayerAssetService.GetImagePaths(folderPath);
            if (forcePopulateCards || IsCharacterLayerExpanderExpanded(layerKind))
            {
                await AddGridViewItemsInBatchesAsync(
                    gridView,
                    imagePaths,
                    imagePath => CreateCharacterImageLayerCard(imagePath, layerKind),
                    cancellationToken,
                    batchSize: 8);
            }

            UpdateCharacterLayerExpanderHeader(layerKind, imagePaths.Count);
        }

        private void LoadCharacterImageLayer(GridView gridView, string folderPath, CharacterLayerKind layerKind)
        {
            RunCharacterDetailLoad(LoadCharacterImageLayerAsync(gridView, folderPath, layerKind, GetCharacterDetailLoadToken()));
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

        private GridView GetCharacterLayerGridView(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => CharacterClothGridView,
                CharacterLayerKind.Face => CharacterFaceGridView,
                CharacterLayerKind.Adorn => CharacterAdornGridView,
                CharacterLayerKind.Vfx => CharacterVfxGridView,
                _ => CharacterClothGridView
            };
        }

        private bool IsCharacterLayerExpanderExpanded(CharacterLayerKind layerKind)
        {
            return layerKind switch
            {
                CharacterLayerKind.Cloth => CharacterClothExpander.IsExpanded,
                CharacterLayerKind.Face => CharacterFaceExpander.IsExpanded,
                CharacterLayerKind.Adorn => CharacterAdornExpander.IsExpanded,
                CharacterLayerKind.Vfx => CharacterVfxExpander.IsExpanded,
                _ => false
            };
        }

        private GridViewItem CreateCharacterImageLayerCard(string imagePath, CharacterLayerKind layerKind)
        {
            TappedEventHandler? tappedHandler = (_, args) =>
            {
                PlaySelectionSound();
                ShowCharacterLayerViewerPage(imagePath, layerKind);
                args.Handled = true;
            };
            MenuFlyout? contextFlyout = null;

            if (layerKind == CharacterLayerKind.Cloth)
            {
                contextFlyout = GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) => await SetCharacterClothRemarkAsync(imagePath)),
                    GridViewItemFactory.CreateMenuItem("设置预览", async (_, _) => await SetCharacterPortraitPreviewAsync(CharacterLayerKind.Cloth, imagePath)),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceCharacterLayerAsync(CharacterLayerKind.Cloth, imagePath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) => await DeleteCharacterClothAsync(imagePath)));
            }
            else if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn)
            {
                contextFlyout = GridViewItemFactory.CreateMenu(
                    GridViewItemFactory.CreateMenuItem("设置备注", async (_, _) =>
                    {
                        if (layerKind == CharacterLayerKind.Face)
                        {
                            await SetCharacterFaceRemarkAsync(imagePath);
                        }
                        else
                        {
                            await SetCharacterAdornRemarkAsync(imagePath);
                        }
                    }),
                    GridViewItemFactory.CreateMenuItem("设置预览", async (_, _) => await SetCharacterPortraitPreviewAsync(layerKind, imagePath)),
                    GridViewItemFactory.CreateMenuItem("可用范围", async (_, _) =>
                    {
                        if (layerKind == CharacterLayerKind.Face)
                        {
                            await SetCharacterFaceAvailabilityAsync(imagePath);
                        }
                        else
                        {
                            await SetCharacterAdornAvailabilityAsync(imagePath);
                        }
                    }),
                    GridViewItemFactory.CreateMenuItem("替换素材", async (_, _) => await ReplaceCharacterLayerAsync(layerKind, imagePath)),
                    GridViewItemFactory.CreateMenuItem("删除", async (_, _) =>
                    {
                        if (layerKind == CharacterLayerKind.Face)
                        {
                            await DeleteCharacterFaceAsync(imagePath);
                        }
                        else
                        {
                            await DeleteCharacterAdornAsync(imagePath);
                        }
                    }));
            }
            else
            {
                tappedHandler = async (_, args) =>
                {
                    PlaySelectionSound();
                    SetSelectedCharacterLayer(layerKind, imagePath);
                    await UpdateCharacterLayerPreviewAsync();
                    args.Handled = true;
                };
            }

            var content = AssetCardContentFactory.CreateImageAssetCardContent(imagePath, 178, 152, tagWithPath: true);
            var warningMessage = GetCharacterLayerPortraitPreviewWarningMessage(imagePath, layerKind);
            var cardContent = warningMessage is null
                ? content
                : CreateWarningBadgeOverlay(content, warningMessage);

            return AssetCardFactory.CreateCard(
                190,
                190,
                cardContent,
                imagePath,
                tappedHandler,
                contextFlyout,
                marginRight: 16,
                marginBottom: 16);
        }

        private async Task LoadCharacterVfxLayerAsync(string folderPath, CancellationToken cancellationToken, bool forcePopulateCards = false)
        {
            Directory.CreateDirectory(folderPath);
            CharacterVfxGridView.Items.Clear();

            var vfxPaths = Directory
                .EnumerateFiles(folderPath)
                .OrderBy(Path.GetFileName)
                .ToList();

            if (forcePopulateCards || CharacterVfxExpander.IsExpanded)
            {
                await AddGridViewItemsInBatchesAsync(
                    CharacterVfxGridView,
                    vfxPaths,
                    CreateCharacterVfxIndexCard,
                    cancellationToken);
            }

            UpdateCharacterLayerExpanderHeader(CharacterLayerKind.Vfx, vfxPaths.Count);
        }

        private void LoadCharacterVfxLayer(string folderPath)
        {
            RunCharacterDetailLoad(LoadCharacterVfxLayerAsync(folderPath, GetCharacterDetailLoadToken()));
        }

        private GridViewItem CreateCharacterVfxIndexCard(string vfxPath)
        {
            return AssetCardFactory.CreateCard(
                220,
                92,
                AssetCardContentFactory.CreateIconAssetCardContent(
                    Symbol.Filter,
                    Path.GetFileNameWithoutExtension(vfxPath)),
                vfxPath,
                async (_, _) =>
                {
                    _selectedCharacterVfxPath = vfxPath;
                    await UpdateCharacterLayerPreviewAsync();
                });
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
            GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("服装", async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Cloth)),
                GridViewItemFactory.CreateMenuItem("表情", async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Face)),
                GridViewItemFactory.CreateMenuItem("装饰", async (_, _) => await ChooseCharacterDetailLayerAsync(CharacterLayerKind.Adorn)))
                .ShowAt(CharacterPreviewSurface);
            e.Handled = true;
        }

        private void CharacterPreviewSurface_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void CharacterPreviewSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(CharacterPreviewSurface);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            PlaySelectionSound();
            ShowCharacterCompositeViewerPage();
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

            var selected = await _storyDialogService.SelectPreviewChoiceAsync(
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
                    _currentCharacter is not null && _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, candidatePath, _selectedCharacterFacePath, ComputeFileHash) ? _selectedCharacterFacePath : null,
                    _currentCharacter is not null && _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, candidatePath, _selectedCharacterAdornPath, ComputeFileHash) ? _selectedCharacterAdornPath : null),
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

            var paths = CharacterLayerAssetService.GetLayerPaths(_currentCharacter, layerKind);
            if (layerKind is CharacterLayerKind.Face or CharacterLayerKind.Adorn or CharacterLayerKind.Vfx)
            {
                paths = paths
                    .Where(path => _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, _selectedCharacterClothPath, path, ComputeFileHash))
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
                : _characterLayerAssetService.IsCompatibleWithCloth(_currentCharacter, _selectedCharacterClothPath, layerPath, ComputeFileHash);
        }

        private static async Task<bool> SetCharacterPreviewImageAsync(Image image, string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) ||
                !File.Exists(imagePath) ||
                !BackgroundImageService.Extensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase))
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                return false;
            }

            image.Visibility = Visibility.Visible;
            await ThumbnailFactory.LoadThumbnailFromFileAsync(image, imagePath);
            return true;
        }

        private void CloseCharacterDetailButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAssetLibrary is not null)
            {
                PlayNegativeSound();
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

            PlayPositiveSound();
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
            var convertedCount = await _backgroundImageService.NormalizeFilesAsync(
                backgroundFolderPath,
                ConvertImageToPngAsync,
                orderedPaths);
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

            var newProjectRootPath = _appSettingsService.BuildProjectRootPathFromParent(selectedFolder.Path);
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
                ShowGlobalProgress("迁移整体项目目录", newProjectRootPath);
                UpdateGlobalProgress("正在复制和校验项目文件...", 5, $"{oldProjectRootPath} -> {newProjectRootPath}");
                var result = await Task.Run(() => _projectRootMigrationService.Migrate(oldProjectRootPath, newProjectRootPath, GetGlobalProgressCancellationToken()));

                _projectRootPath = newProjectRootPath;
                _appSettings.ProjectRootPath = _projectRootPath;
                SaveAppSettings();
                EnsureProjectRootDirectory(_projectRootPath);
                CompleteGlobalProgress("目录迁移完成", $"已迁移 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹");
                await HideGlobalProgressAfterDelayAsync();
                SetProjectRootStatus(InfoBarSeverity.Success, "目录迁移完成", $"已迁移并校验 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹。旧目录已删除：{oldProjectRootPath}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress(ex is OperationCanceledException ? "目录迁移已取消" : "目录迁移失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
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

        private void SaveAppSettings()
        {
            _appSettingsService.Save(_appSettings);
        }

        private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            if (!_isChangingShellSelectionInternally)
            {
                PlaySelectionSound();
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
            SetUnrealSyncProgress(true, "正在检测虚幻同步差异... 5%", false, 5);
            UnrealSyncSummaryText.Text = forceFullSync
                ? "正在准备全量重新同步，将忽略本地缓存和 .uasset 时间戳。"
                : "正在比较工具箱源文件与虚幻项目内的 .uasset 时间戳；文件名不变但内容重新导出时，也会因源文件写入时间更新而进入同步计划。";
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
                var cancellationToken = GetGlobalProgressCancellationToken();
                string? backupPath = null;
                if (backupChoice == true)
                {
                    SetUnrealSyncProgress(true, "正在备份虚幻项目... 20%", false, 20);
                    backupPath = await Task.Run(() => CreateCleanUnrealProjectBackup(validation.Context, cancellationToken), cancellationToken);
                    SetUnrealSyncProgress(true, "虚幻项目备份完成... 30%", false, 30);
                    AppendLog(LogKind.User, $"虚幻项目备份完成：{backupPath}");
                }

                var progress = new Progress<UnrealSyncProgressUpdate>(update =>
                    SetUnrealSyncProgress(true, $"{update.Message} {update.Percent:0}%", false, update.Percent));
                var result = await Task.Run(() => RunUnrealSync(validation.Context, changePlan, progress, cancellationToken), cancellationToken);
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
                var portraitsConfirmed = !changePlan.PortraitsChanged ||
                    result.Output.Contains("GalExcleTools updated portrait data asset", StringComparison.OrdinalIgnoreCase);
                var storyTableFailed =
                    result.Output.Contains("GalExcleTools failed to update story data table", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools could not create or load story data table", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools story row struct missing", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools failed to load story row struct", StringComparison.OrdinalIgnoreCase);
                var assetIndexTableFailed =
                    result.Output.Contains("GalExcleTools failed to update asset index data table", StringComparison.OrdinalIgnoreCase) ||
                    result.Output.Contains("GalExcleTools could not create or load asset index data table", StringComparison.OrdinalIgnoreCase);
                if (result.ExitCode == 0 && lustrationConfirmed && portraitsConfirmed && !assetIndexTableFailed)
                {
                    _unrealSyncService.WriteState(validation.Context, changePlan);
                    SetUnrealSyncProgress(true, "同步完成 100%", false, 100);
                    CompleteGlobalProgress("虚幻同步完成", $"处理 {changePlan.TotalChangedItems} 项，退出码 {result.ExitCode}");
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
                else if (result.ExitCode == 0 && changePlan.PortraitsChanged)
                {
                    SetUnrealSyncStatus(InfoBarSeverity.Warning, "同步结束，但小预览数据未确认", "Unreal 日志没有返回 DA_Portraits 写入确认。本次不会缓存为已同步，请关闭虚幻编辑器后再同步一次。");
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
            var contentFolderPath = UnrealContentFolderTextBox.Text.Trim();
            var selectedProject = GetSelectedUnrealSyncProject();
            _appSettings.UnrealToolProjectFolderName = selectedProject?.FolderName ?? (UnrealSyncProjectComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            _appSettings.UnrealEnginePath = enginePath;
            _appSettings.UnrealProjectPath = unrealProjectPath;
            _appSettings.UnrealContentFolderPath = contentFolderPath;
            SaveAppSettings();

            if (selectedProject is not null)
            {
                _projectWorkspaceService.SaveProjectUnrealBinding(selectedProject, enginePath, unrealProjectPath, contentFolderPath);
                LoadUnrealSyncProjectOptions();
            }
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
            UnrealSyncProgressPanel.Visibility = Visibility.Collapsed;
            UnrealSyncProgressBar.IsIndeterminate = false;
            UnrealSyncProgressBar.Value = 0;
            UnrealSyncProgressText.Text = string.Empty;
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
            foreach (var item in changePlan.PlanItems)
            {
                AddUnrealSyncPlanText($"• {item}");
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
                AddUnrealSyncPlanText($"[{group.Destination}] 待导入文件", topMargin: 8);
                foreach (var path in group.Files)
                {
                    AddUnrealSyncPlanText($"  - {Path.GetFileName(path)}");
                }
            }

            foreach (var group in changePlan.DeleteGroups)
            {
                AddUnrealSyncPlanText($"[{group.Destination}] 待删除多余资产", topMargin: 8);
                foreach (var assetPath in group.Assets)
                {
                    AddUnrealSyncPlanText($"  - {assetPath}");
                }
            }

            if (changePlan.StoryTables.Count > 0)
            {
                AddUnrealSyncPlanText("[ExcelTexts] 待更新剧情表", topMargin: 8);
                foreach (var entry in changePlan.StoryTables)
                {
                    AddUnrealSyncPlanText($"  - {Path.GetFileName(entry.CsvPath)} -> {entry.TableAsset}");
                }
            }

            if (changePlan.AssetIndexTables.Count > 0)
            {
                AddUnrealSyncPlanText("[ExcelTexts] 待更新素材索引表", topMargin: 8);
                foreach (var entry in changePlan.AssetIndexTables)
                {
                    AddUnrealSyncPlanText($"  - {Path.GetFileName(entry.CsvPath)} -> {entry.TableAsset}");
                }
            }

            if (changePlan.LustrationChanged)
            {
                AddUnrealSyncPlanText("[Lustration] 待更新立绘数据资产", topMargin: 8);
                foreach (var row in changePlan.LustrationRows)
                {
                    AddUnrealSyncPlanText($"  - {row.Key} / {row.Name}");
                }
            }

            if (changePlan.PortraitsChanged)
            {
                AddUnrealSyncPlanText("[Lustration] 待更新小预览数据资产", topMargin: 8);
                foreach (var row in changePlan.PortraitRows)
                {
                    AddUnrealSyncPlanText($"  - {row.Key} / {row.Name}");
                }
            }
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

        private async Task<bool?> ShowUnrealBackupDialogAsync(UnrealSyncContext context)
        {
            var backupFolder = GetUnrealBackupFolder(context);
            var result = await _dialogService.ShowAsync(new DialogRequest(
                "同步前备份虚幻项目？",
                $"建议在写入虚幻项目前先生成一个不带缓存的干净压缩包。\n默认位置：{backupFolder}",
                "备份并同步",
                "取消",
                "直接同步"));
            return result switch
            {
                DialogResultKind.Primary => true,
                DialogResultKind.Secondary => false,
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
            var result = await _dialogService.ShowAsync(new DialogRequest(
                "检测到 Unreal Editor 正在运行",
                $"建议先关闭当前打开的虚幻编辑器，再执行同步。否则编辑器中已加载的资产可能不会刷新，后续保存还可能覆盖同步结果。\n\n{processText}",
                "关闭后同步",
                "取消",
                "继续同步"));
            return result switch
            {
                DialogResultKind.Primary => true,
                DialogResultKind.Secondary => false,
                _ => null
            };
        }

        private async Task ShowUnrealSyncFinishedDialogAsync(
            UnrealSyncContext context,
            UnrealSyncResult result,
            UnrealSyncChangePlan changePlan)
        {
            var dialogResult = await _dialogService.ShowAsync(new DialogRequest(
                result.ExitCode == 0 ? "虚幻同步完成" : "虚幻同步已结束",
                result.ExitCode == 0
                    ? $"本次同步处理 {changePlan.TotalChangedItems} 项。现在可以直接打开虚幻项目检查结果。"
                    : $"Unreal Editor 返回退出码 {result.ExitCode}。可以打开项目或查看日志继续确认。",
                "打开虚幻项目",
                "知道了",
                "打开日志目录"));
            if (dialogResult == DialogResultKind.Primary)
            {
                OpenUnrealProject(context);
            }
            else if (dialogResult == DialogResultKind.Secondary)
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

        private static string CreateCleanUnrealProjectBackup(UnrealSyncContext context, CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();
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

            var editorPath = UnrealSyncService.ResolveEditorExecutable(enginePath);
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
                    var missingFolders = UnrealSyncService.ExpectedNarrativeFolders
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
                if (assetLibrary.IsPortraitPreviewEnabled)
                {
                    var missingPortraitPreviewNames = GetMissingPortraitPreviewLayerNames(assetLibrary);
                    if (missingPortraitPreviewNames.Count > 0)
                    {
                        warnings.Add($"小预览已启用，但有 {missingPortraitPreviewNames.Count} 个立绘素材未设置预览：{string.Join(", ", missingPortraitPreviewNames.Take(8))}{(missingPortraitPreviewNames.Count > 8 ? "..." : string.Empty)}");
                    }
                }
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
            var backgroundCount = BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(assetLibrary)).Count;
            var musicCount = AudioAssetService.GetFilePaths(GetMusicFolderPath(assetLibrary)).Count;
            var sceneCount = AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(assetLibrary)).Count;
            var soundEffectCount = AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(assetLibrary)).Count;
            var voiceCount = ProjectVoiceAssetService.GetVoiceFilePaths(project).Count;
            var csvCount = GetProjectStoryCsvPaths(project).Count;
            var characterLayerCount = GetProjectCharacterLayerImportPaths(assetLibrary).Count;
            var portraitPreviewCount = GetProjectPortraitPreviewImportPaths(assetLibrary).Count;
            var missingPortraitPreviewCount = assetLibrary.IsPortraitPreviewEnabled
                ? GetMissingPortraitPreviewLayerNames(assetLibrary).Count
                : 0;
            var lustrationRowCount = GetCharactersForAssetLibrary(assetLibrary).Count;
            var existingBackgroundCount = UnrealSyncService.CountAssets(Path.Combine(contentFolderPath, "BackGround"));
            var existingMusicCount = UnrealSyncService.CountAssets(Path.Combine(contentFolderPath, "BGM"));
            var existingSceneCount = UnrealSyncService.CountAssets(Path.Combine(contentFolderPath, "Scene_Effect"));
            var existingVoiceCount = UnrealSyncService.CountAssetsRecursive(Path.Combine(contentFolderPath, "Voice"));

            return
            [
                $"背景图：源文件 {backgroundCount} 个，引擎内已有 {existingBackgroundCount} 个 .uasset。",
                $"音乐：源文件 {musicCount} 个，引擎内已有 {existingMusicCount} 个 .uasset。",
                $"环境音/特殊音效：源文件 {sceneCount + soundEffectCount} 个，引擎内已有 {existingSceneCount} 个 .uasset。",
                $"文本语音：源文件 {voiceCount} 个，引擎内已有 {existingVoiceCount} 个 .uasset，将只同步 wav 到 Voice 文件夹。",
                $"素材索引表：背景 {backgroundCount}、BGM {musicCount}、环境音 {sceneCount}、特殊音效 {soundEffectCount}，将同步到 ExcelTexts 的 4 张 DataTable。",
                $"CSV 表格：{csvCount} 个，将导入到 ExcelTexts。",
                $"立绘图层：{characterLayerCount} 个，将按 Lustration/角色/图层目录导入。",
                assetLibrary.IsPortraitPreviewEnabled
                    ? $"小预览：源文件 {portraitPreviewCount} 个，缺失设置 {missingPortraitPreviewCount} 个，将同步到 Lustration/角色/Log_Preview 和 DA_Portraits。"
                    : "小预览：当前素材库未启用。",
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
            IProgress<UnrealSyncProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var filters = File.Exists(GetCharacterFilterIndexPath(context.AssetLibrary))
                ? _characterFilterService.Read(context.AssetLibrary)
                : [];
            return _unrealSyncService.Run(context, changePlan, filters, progress, cancellationToken);
        }

        private List<UnrealStoryTableSyncEntry> BuildStoryTableSyncEntries(UnrealSyncContext context)
        {
            return _unrealSyncService.BuildStoryTableSyncEntries(context, _projectWorkspaceService.GetChapters(context.Project));
        }

        private UnrealSyncChangePlan BuildUnrealSyncChangePlan(UnrealSyncContext context, bool forceFullSync = false)
        {
            var characters = GetCharactersForAssetLibrary(context.AssetLibrary);
            var allStoryTables = BuildStoryTableSyncEntries(context);
            var assetIndexTableCacheFolder = Path.Combine(context.Project.Path, ToolsFolderName, UnrealAssetIndexTablesFolderName);

            return _unrealSyncService.BuildChangePlan(
                context,
                forceFullSync,
                characters,
                BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(context.AssetLibrary)),
                AudioAssetService.GetFilePaths(GetMusicFolderPath(context.AssetLibrary)),
                AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(context.AssetLibrary)),
                AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(context.AssetLibrary)),
                ProjectVoiceAssetService.GetVoiceFilePaths(context.Project),
                allStoryTables,
                assetIndexTableCacheFolder,
                GetPortraitPreviewPathsByLayerFileName);
        }

        private List<string> GetProjectStoryCsvPaths(ProjectInfo project)
        {
            return _unrealSyncService.GetProjectStoryCsvPaths(project, _projectWorkspaceService.GetChapters(project));
        }

        private List<CharacterInfo> GetCharactersForAssetLibrary(AssetLibraryInfo assetLibrary)
        {
            return _characterWorkspaceService.GetCharactersByCode(assetLibrary);
        }

        private List<string> GetProjectCharacterLayerImportPaths(AssetLibraryInfo assetLibrary)
        {
            var result = new List<string>();
            foreach (var character in GetCharactersForAssetLibrary(assetLibrary))
            {
                result.AddRange(UnrealSyncService.GetCharacterLayerImportPaths(character, CharacterLayerKind.Cloth));
                result.AddRange(UnrealSyncService.GetCharacterLayerImportPaths(character, CharacterLayerKind.Face));
                result.AddRange(UnrealSyncService.GetCharacterLayerImportPaths(character, CharacterLayerKind.Adorn));
            }

            return result;
        }

        private IReadOnlyDictionary<string, string> GetPortraitPreviewPathsByLayerFileName(CharacterInfo character)
        {
            _characterLayerAssetService.CleanupPortraitPreviewMeta(character);
            return _characterLayerAssetService.GetPortraitPreviewPathsByLayerFileName(character);
        }

        private List<string> GetProjectPortraitPreviewImportPaths(AssetLibraryInfo assetLibrary)
        {
            return GetCharactersForAssetLibrary(assetLibrary)
                .SelectMany(character => GetPortraitPreviewPathsByLayerFileName(character).Values)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> GetMissingPortraitPreviewLayerNames(AssetLibraryInfo assetLibrary)
        {
            return GetCharactersForAssetLibrary(assetLibrary)
                .SelectMany(character => _characterLayerAssetService
                    .GetMissingPortraitPreviewLayerNames(character)
                    .Select(fileName => $"{character.Code}/{fileName}"))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async void ShowProjectRootHelpButton_Click(object sender, RoutedEventArgs e)
        {
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "整体项目位置说明",
                DialogContentFactory.CreateProjectRootHelpContent(),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None));
        }

        private async void ShowLogHelpButton_Click(object sender, RoutedEventArgs e)
        {
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "辅助显示说明",
                DialogContentFactory.CreateLogHelpContent(),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None));
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
                SelectShellNavigationItem(WorkbenchNavItem);
            }
            LoadProjects();
            RefreshWorkbenchUnrealSyncTip();
            PlayPageEntrance(WorkbenchPage);
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
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, AssetLibraryNavItem))
            {
                SelectShellNavigationItem(AssetLibraryNavItem);
            }
            LoadAssetLibraries();
            PlayPageEntrance(AssetLibraryPage);
        }

        private void ShowAssetLibraryDetailPage(AssetLibraryInfo assetLibrary)
        {
            StopStoryEditorAudio();
            var cancellationToken = ResetAssetLibraryLoadCancellation();
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
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, AssetLibraryNavItem))
            {
                SelectShellNavigationItem(AssetLibraryNavItem);
            }
            RunAssetLibraryLoad(RefreshBackgroundImageCardsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Music, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Ambient, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.SoundEffect, cancellationToken));
            RunAssetLibraryLoad(LoadFunctionsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharactersAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharacterFiltersAsync(assetLibrary, cancellationToken));
            ApplyAssetLibraryMetadataToUi(assetLibrary);
            UpdateAssetLibraryDiskUsage(assetLibrary);
            PlayPageEntrance(AssetLibraryDetailPage);
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
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Visible;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(CreateProjectPage);

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
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Visible;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(CreateAssetLibraryPage);
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
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Visible;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, UnrealSyncNavItem))
            {
                SelectShellNavigationItem(UnrealSyncNavItem);
            }

            RefreshUnrealSyncStatus();
            PlayPageEntrance(UnrealSyncPage);
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
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
            PlayPageEntrance(SettingsPage);
        }

        private void SelectShellNavigationItem(NavigationViewItem item)
        {
            _isChangingShellSelectionInternally = true;
            try
            {
                ShellNavigation.SelectedItem = item;
            }
            finally
            {
                _isChangingShellSelectionInternally = false;
            }
        }

        private static void PlayPageEntrance(FrameworkElement page)
        {
            if (page.Visibility != Visibility.Visible)
            {
                return;
            }

            page.Transitions = null;
            page.Resources["PageEntranceStoryboard"] = null;

            if (page.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                page.RenderTransform = transform;
            }

            transform.X = PageEntranceOffsetX;
            transform.Y = 0;
            page.Opacity = 0;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideAnimation = new DoubleAnimation
            {
                From = PageEntranceOffsetX,
                To = 0,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, transform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.X));

            var fadeAnimation = new DoubleAnimation
            {
                From = 0.82,
                To = 1,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(fadeAnimation, page);
            Storyboard.SetTargetProperty(fadeAnimation, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(slideAnimation);
            storyboard.Children.Add(fadeAnimation);
            storyboard.Completed += (_, _) =>
            {
                transform.X = 0;
                transform.Y = 0;
                page.Opacity = 1;
                page.Resources.Remove("PageEntranceStoryboard");
            };
            page.Resources["PageEntranceStoryboard"] = storyboard;
            storyboard.Begin();
        }

        private sealed record UnrealSyncValidation(
            InfoBarSeverity Severity,
            string Title,
            string Message,
            string Summary,
            bool CanSync,
            UnrealSyncContext? Context,
            IReadOnlyList<string> PlanItems);

        private static readonly string[] StoryCsvColumns = StoryCsvService.Columns;

        private static readonly HashSet<string> StoryNumericColumns = StoryCsvService.NumericColumns;


    }
}




