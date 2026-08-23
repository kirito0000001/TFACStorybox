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
        private readonly UpdateService _updateService = new();
        private readonly BackgroundImageService _backgroundImageService = new();
        private readonly CharacterLayerAssetService _characterLayerAssetService = new();
        private readonly CharacterFilterService _characterFilterService = new();
        private readonly CharacterWorkspaceService _characterWorkspaceService;
        private readonly FolderBackupService _folderBackupService;
        private readonly UnrealSyncService _unrealSyncService = new();
        private readonly StoryCsvService _storyCsvService = new();
        private readonly StoryDocumentRepository _storyDocumentRepository;
        private readonly StorySaveCoordinator _storySaveCoordinator;
        private readonly StoryCsvExporter _storyCsvExporter;
        private readonly StoryCsvImporter _storyCsvImporter;
        private readonly StoryEditorService _storyEditorService;
        private readonly ProjectTextDataService _projectTextDataService;
        private readonly ProjectVoiceAssetService _projectVoiceAssetService = new();
        private readonly ChapterRepairService _chapterRepairService;
        private readonly StoryAssetIndexSyncService _storyAssetIndexSyncService;
        private readonly IDialogService _dialogService;
        private readonly IShortcutService _shortcutService;
        private readonly IUiSoundService _uiSoundService;
        private readonly StoryDialogService _storyDialogService;
        private readonly FunctionDialogService _functionDialogService;
        private readonly StoryAudioPlaybackService _storyBgmPlayback = new(looping: true);
        private readonly StoryAudioPlaybackService _storyScenePlayback = new(looping: true);
        private readonly StoryAudioPlaybackService _storyVoicePlayback = new(looping: false);
        private readonly StoryAudioPlaybackService _storySoundEffectPlayback = new(looping: false);
        private readonly MediaPlayer _projectVoicePreviewPlayer = new();
        private readonly ApplicationViewModel _applicationViewModel;
        private SettingsViewModel Settings => _applicationViewModel.Settings;
        private GlobalProgressViewModel GlobalProgress => _applicationViewModel.GlobalProgress;
        private AppSettings _appSettings => Settings.CurrentSettings;
        private string _projectRootPath => Settings.ProjectRootPath;
        private ProjectWorkbenchViewModel Workbench => _applicationViewModel.Workbench;
        private AssetLibraryViewModel AssetLibraryState => _applicationViewModel.AssetLibrary;
        private UnrealSyncViewModel UnrealSyncState => _applicationViewModel.UnrealSync;

        private string? _selectedProjectThumbnailPath
        {
            get => Workbench.SelectedProjectThumbnailPath;
            set => Workbench.SelectedProjectThumbnailPath = value;
        }

        private string? _selectedAssetLibraryThumbnailPath
        {
            get => AssetLibraryState.SelectedAssetLibraryThumbnailPath;
            set => AssetLibraryState.SelectedAssetLibraryThumbnailPath = value;
        }

        private AssetLibraryInfo? _currentAssetLibrary
        {
            get => AssetLibraryState.CurrentAssetLibrary;
            set => AssetLibraryState.CurrentAssetLibrary = value;
        }

        private StoryEditorViewModel _storyEditorViewModel => _applicationViewModel.StoryEditor;
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
        private List<ProjectTextRow> _projectTextRows => Workbench.ProjectTextRows;

        private ProjectVoiceMapState _projectVoiceMapState
        {
            get => Workbench.ProjectVoiceMapState;
            set => Workbench.ProjectVoiceMapState = value;
        }

        private ProjectLocalizationState _projectLocalizationState
        {
            get => Workbench.ProjectLocalizationState;
            set => Workbench.ProjectLocalizationState = value;
        }

        private ChapterInfo? _selectedVoiceChapter
        {
            get => Workbench.SelectedVoiceChapter;
            set => Workbench.SelectedVoiceChapter = value;
        }

        private ChapterInfo? _selectedTextToolChapter
        {
            get => Workbench.SelectedTextToolChapter;
            set => Workbench.SelectedTextToolChapter = value;
        }

        private string _selectedLocalizationLanguage
        {
            get => Workbench.SelectedLocalizationLanguage;
            set => Workbench.SelectedLocalizationLanguage = value;
        }

        private ProjectTextToolMode _projectTextToolMode
        {
            get => Workbench.ProjectTextToolMode;
            set => Workbench.ProjectTextToolMode = value;
        }

        private bool _showOnlyConfiguredVoices
        {
            get => Workbench.ShowOnlyConfiguredVoices;
            set => Workbench.ShowOnlyConfiguredVoices = value;
        }

        private Dictionary<string, BitmapImage> _storyPreviewImageCache => _storyEditorViewModel.PreviewImageCache;
        private Dictionary<int, string> _storyCharacterPreviewKeys => _storyEditorViewModel.CharacterPreviewKeys;
        private int _currentStoryRowIndex
        {
            get => _storyEditorViewModel.CurrentRowIndex;
            set => _storyEditorViewModel.CurrentRowIndex = value;
        }

        private StoryCharacterSlotClipboard? _storyCharacterSlotClipboard
        {
            get => _storyEditorViewModel.CharacterSlotClipboard;
            set => _storyEditorViewModel.CharacterSlotClipboard = value;
        }

        private StoryAssetClipboard? _storyAssetClipboard
        {
            get => _storyEditorViewModel.AssetClipboard;
            set => _storyEditorViewModel.AssetClipboard = value;
        }
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

        private string? _currentStoryDocumentPath
        {
            get => _storyEditorViewModel.DocumentPath;
            set => _storyEditorViewModel.DocumentPath = value;
        }
        private string? _storyBackgroundPreviewKey
        {
            get => _storyEditorViewModel.BackgroundPreviewKey;
            set => _storyEditorViewModel.BackgroundPreviewKey = value;
        }

        private int _storyBackgroundTransitionMode
        {
            get => _storyEditorViewModel.BackgroundTransitionMode;
            set => _storyEditorViewModel.BackgroundTransitionMode = value;
        }

        private bool _storyBgmPlaybackSuppressed
        {
            get => _storyEditorViewModel.BgmPlaybackSuppressed;
            set => _storyEditorViewModel.BgmPlaybackSuppressed = value;
        }

        private bool _isNormalizingBackgroundImages
        {
            get => AssetLibraryState.IsNormalizingBackgroundImages;
            set => AssetLibraryState.IsNormalizingBackgroundImages = value;
        }

        private bool _isNormalizingMusicFiles
        {
            get => AssetLibraryState.IsNormalizingMusicFiles;
            set => AssetLibraryState.IsNormalizingMusicFiles = value;
        }

        private bool _isNormalizingAmbientSoundFiles
        {
            get => AssetLibraryState.IsNormalizingAmbientSoundFiles;
            set => AssetLibraryState.IsNormalizingAmbientSoundFiles = value;
        }

        private bool _isNormalizingSoundEffectFiles
        {
            get => AssetLibraryState.IsNormalizingSoundEffectFiles;
            set => AssetLibraryState.IsNormalizingSoundEffectFiles = value;
        }

        private bool _isNormalizingCharacterClothes
        {
            get => AssetLibraryState.IsNormalizingCharacterClothes;
            set => AssetLibraryState.IsNormalizingCharacterClothes = value;
        }

        private bool _isNormalizingCharacterFaces
        {
            get => AssetLibraryState.IsNormalizingCharacterFaces;
            set => AssetLibraryState.IsNormalizingCharacterFaces = value;
        }

        private bool _isNormalizingCharacterAdorns
        {
            get => AssetLibraryState.IsNormalizingCharacterAdorns;
            set => AssetLibraryState.IsNormalizingCharacterAdorns = value;
        }

        private bool _isReorderingCharacterFilters
        {
            get => AssetLibraryState.IsReorderingCharacterFilters;
            set => AssetLibraryState.IsReorderingCharacterFilters = value;
        }

        private bool _isRepairingCharacterFilters
        {
            get => AssetLibraryState.IsRepairingCharacterFilters;
            set => AssetLibraryState.IsRepairingCharacterFilters = value;
        }

        private bool _isApplyingAssetLibraryMetadata
        {
            get => AssetLibraryState.IsApplyingAssetLibraryMetadata;
            set => AssetLibraryState.IsApplyingAssetLibraryMetadata = value;
        }

        private bool _runFullUnrealSyncRequested
        {
            get => UnrealSyncState.RunFullSyncRequested;
            set => UnrealSyncState.RunFullSyncRequested = value;
        }

        private bool _isLoadingUnrealSyncProjects
        {
            get => UnrealSyncState.IsLoadingProjects;
            set => UnrealSyncState.IsLoadingProjects = value;
        }
        private GridViewItem? _draggingBackgroundImageItem;
        private GridViewItem? _draggingMusicItem;
        private GridViewItem? _draggingAmbientSoundItem;
        private GridViewItem? _draggingSoundEffectItem;
        private GridViewItem? _draggingCharacterFilterItem;
        private GridViewItem? _draggingCharacterClothItem;
        private GridViewItem? _draggingCharacterFaceItem;
        private GridViewItem? _draggingCharacterAdornItem;
        private string? _viewingBackgroundImagePath
        {
            get => AssetLibraryState.ViewingBackgroundImagePath;
            set => AssetLibraryState.ViewingBackgroundImagePath = value;
        }

        private CharacterLayerViewerState? _viewingCharacterLayer
        {
            get => AssetLibraryState.ViewingCharacterLayer;
            set => AssetLibraryState.ViewingCharacterLayer = value;
        }

        private bool _isViewingCharacterComposite
        {
            get => AssetLibraryState.IsViewingCharacterComposite;
            set => AssetLibraryState.IsViewingCharacterComposite = value;
        }

        private string? _playingMusicPath
        {
            get => AssetLibraryState.PlayingMusicPath;
            set => AssetLibraryState.PlayingMusicPath = value;
        }

        private AudioAssetKind _playingAudioKind
        {
            get => AssetLibraryState.PlayingAudioKind;
            set => AssetLibraryState.PlayingAudioKind = value;
        }

        private ProjectInfo? _currentProject
        {
            get => Workbench.CurrentProject;
            set => Workbench.CurrentProject = value;
        }

        private CharacterInfo? _currentCharacter
        {
            get => AssetLibraryState.CurrentCharacter;
            set => AssetLibraryState.CurrentCharacter = value;
        }

        private bool _isCreatingChapter
        {
            get => Workbench.IsCreatingChapter;
            set => Workbench.IsCreatingChapter = value;
        }

        private int? _hoveredStoryCharacterSlotIndex
        {
            get => _storyEditorViewModel.HoveredCharacterSlotIndex;
            set => _storyEditorViewModel.HoveredCharacterSlotIndex = value;
        }

        private string? _hoveredStoryAssetFieldName
        {
            get => _storyEditorViewModel.HoveredAssetFieldName;
            set => _storyEditorViewModel.HoveredAssetFieldName = value;
        }

        private string? _selectedCharacterClothPath
        {
            get => AssetLibraryState.SelectedCharacterClothPath;
            set => AssetLibraryState.SelectedCharacterClothPath = value;
        }

        private string? _selectedCharacterFacePath
        {
            get => AssetLibraryState.SelectedCharacterFacePath;
            set => AssetLibraryState.SelectedCharacterFacePath = value;
        }

        private string? _selectedCharacterAdornPath
        {
            get => AssetLibraryState.SelectedCharacterAdornPath;
            set => AssetLibraryState.SelectedCharacterAdornPath = value;
        }

        private string? _selectedCharacterVfxPath
        {
            get => AssetLibraryState.SelectedCharacterVfxPath;
            set => AssetLibraryState.SelectedCharacterVfxPath = value;
        }
        private bool _isPanningBackgroundImage;
        private Windows.Foundation.Point _lastBackgroundImagePointerPosition;
        private double _backgroundImageViewerScale = 1;
        private readonly DispatcherQueueTimer _refreshDelayTimer;
        private readonly DispatcherQueueTimer? _storyEditSaveTimer;
        private readonly DispatcherQueueTimer _globalProgressElapsedTimer;
        private readonly Dictionary<InfoBar, DispatcherQueueTimer> _floatingTipTimers = new();
        private readonly Stopwatch _globalProgressStopwatch = new();
        private string _globalProgressOperationTitle = string.Empty;
        private bool _isGlobalProgressVisible;
        private double _globalProgressLastPercent;
        private CancellationTokenSource? _globalProgressCancellation;
        private CancellationTokenSource? _assetLibraryLoadCancellation;
        private CancellationTokenSource? _characterDetailLoadCancellation;
        private CancellationTokenSource? _storyPreviewRefreshCancellation;
        private readonly Queue<string> _storyPreviewImageCacheOrder = new();
        private const int MaxStoryPreviewImageCacheCount = 48;
        private bool _isWindowActive = true;
        private bool _refreshBackgroundImagesAfterDelay;
        private bool _storyTextEditUndoCaptured;
        private StoryChoiceNoteState? _pendingStoryChoiceNotes;
        private string? _lastStoryVoiceRowId;
        private bool _isStoryDebugModeEnabled
        {
            get => _storyEditorViewModel.IsDebugModeEnabled;
            set => _storyEditorViewModel.IsDebugModeEnabled = value;
        }
        private bool _isChangingShellSelectionInternally;

        public MainWindow()
        {
            InitializeComponent();
            var displayVersion = AppVersionService.GetDisplayVersion();
            AppTitleVersionText.Text = displayVersion;
            SettingsAppVersionText.Text = $"版本 {displayVersion}";
            SettingsVersionNamingText.Text = $"采用 主版本.功能版本.修订版本。{displayVersion} 表示当前功能版本；当天同一批修补合并进同一个版本号。";
            _applicationViewModel = new ApplicationViewModel(
                new SettingsViewModel(_appSettingsService, _projectRootMigrationService),
                new GlobalProgressViewModel());
            RootGrid.DataContext = _applicationViewModel;
            StoryEditorPage.DataContext = _storyEditorViewModel;
            _characterWorkspaceService = new CharacterWorkspaceService(_jsonOptions);
            _folderBackupService = new FolderBackupService(_jsonOptions);
            _uiSoundService = new UiSoundService();
            _dialogService = new WinUiDialogService(() => Content.XamlRoot, _uiSoundService);
            _shortcutService = new ShortcutService(_dialogService);
            _storyDialogService = new StoryDialogService(_dialogService);
            _functionDialogService = new FunctionDialogService(_dialogService, _uiSoundService);
            _applicationViewModel.Settings.AuxiliaryDisplayChanged += (_, _) => UpdateAuxiliaryDisplayVisibility();
            _applicationViewModel.Settings.LogSettingsChanged += (_, _) =>
            {
                UpdateLogOptionEnabledState();
                UpdateAuxiliaryDisplayVisibility();
                if (_applicationViewModel.Settings.LogEnabled)
                {
                    AppendLog(LogKind.User, "已更新日志输出设置。");
                }
            };
            _applicationViewModel.Settings.UiSoundSettingChanged += (_, _) =>
            {
                _uiSoundService.IsEnabled = _applicationViewModel.Settings.UiSoundEnabled;
            };
            _applicationViewModel.Settings.StoryTextFontSizeChanged += (_, _) => ApplyStoryTextFontSizeToUi();
            _applicationViewModel.Settings.ShowFullStoryChapterLengthChanged += (_, _) => UpdateStoryRowIndexInput();
            _storyEditorViewModel.ConfigureCommands(
                UndoStoryEditorOperation,
                RedoStoryEditorOperation,
                NavigatePreviousStoryRow,
                NavigateNextStoryRow,
                InsertStoryRowBefore,
                InsertStoryRowAfter,
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
            _storyEditorViewModel.CommandExecutionFailed += (_, exception) =>
            {
                _storyEditorViewModel.UndoService.CancelPending();
                ShowStoryStatus(
                    InfoBarSeverity.Error,
                    "剧情编辑操作失败",
                    exception.Message,
                    exception,
                    $"剧情编辑异步命令失败：{exception}");
            };
            _storyEditorService = new StoryEditorService(_storyCsvService);
            _storyDocumentRepository = new StoryDocumentRepository(_jsonOptions);
            _storySaveCoordinator = new StorySaveCoordinator(_storyDocumentRepository);
            _storyCsvExporter = new StoryCsvExporter(_storyCsvService, _storyDocumentRepository);
            _storyCsvImporter = new StoryCsvImporter(_storyCsvService, _storyDocumentRepository);
            _storySaveCoordinator.SaveFailed += (_, exception) => DispatcherQueue.TryEnqueue(() =>
            {
                _storyEditorViewModel.SaveStatusText = "保存失败";
                ShowStoryStatus(
                    InfoBarSeverity.Error,
                    "剧情自动保存失败",
                    exception.Message,
                    exception,
                    $"StoryDocument 后台保存失败：{exception}");
            });
            _storySaveCoordinator.SaveCompleted += (chapter, savedAt) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_currentStoryChapter is not null &&
                    string.Equals(_currentStoryChapter.Path, chapter.Path, StringComparison.OrdinalIgnoreCase))
                {
                    _storyEditorViewModel.SaveStatusText = $"已保存 {savedAt.LocalDateTime:HH:mm:ss}";
                }
            });
            _projectTextDataService = new ProjectTextDataService(_storyDocumentRepository, _jsonOptions);
            _chapterRepairService = new ChapterRepairService(_storyDocumentRepository);
            _storyAssetIndexSyncService = new StoryAssetIndexSyncService(
                GetProjects,
                ResolveProjectAssetLibrary,
                GetChaptersFolderPath,
                _projectWorkspaceService.ReadChapterInfo,
                _storyDocumentRepository);
            DisableListItemEntranceTransitions();
            Activated += MainWindow_Activated;
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Closing += (_, _) =>
            {
                _storySaveCoordinator.FlushAsync().GetAwaiter().GetResult();
                _storyBgmPlayback.Dispose();
                _storyScenePlayback.Dispose();
                _storyVoicePlayback.Dispose();
                _storySoundEffectPlayback.Dispose();
                _storySaveCoordinator.Dispose();
            };
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();
            _refreshDelayTimer = DispatcherQueue.CreateTimer();
            _refreshDelayTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshDelayTimer.Tick += RefreshDelayTimer_Tick;
            _storyEditSaveTimer = DispatcherQueue.CreateTimer();
            _storyEditSaveTimer.Interval = TimeSpan.FromMilliseconds(350);
            _storyEditSaveTimer.Tick += StoryEditSaveTimer_Tick;
            _globalProgressElapsedTimer = DispatcherQueue.CreateTimer();
            _globalProgressElapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _globalProgressElapsedTimer.Tick += GlobalProgressElapsedTimer_Tick;
            RegisterAssetLibraryExpanderLazyLoading();
            RegisterCharacterDetailExpanderLazyLoading();

            Settings.Load();
            SyncUpdateControls();
            _uiSoundService.IsEnabled = Settings.UiSoundEnabled;
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();
            ApplyUnrealSyncSettingsToUi();
            ApplyStoryTextFontSizeToUi();
            EnsureProjectRootDirectory(Settings.ProjectRootPath);
            AppendLog(LogKind.Info, "程序启动，已检查整体项目目录。");
            ShowWorkbenchPage();
            _ = CheckUpdateOnStartupAsync();
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

        private void UpdateLogOptionEnabledState()
        {
            var enabled = Settings.LogEnabled;
            LogUserOperationsCheckBox.IsEnabled = enabled;
            LogWarningCheckBox.IsEnabled = enabled;
            LogErrorCheckBox.IsEnabled = enabled;
        }

        private void UpdateAuxiliaryDisplayVisibility()
        {
            var showWorkspacePath = Settings.ShowWorkspacePath ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceStatusBorder.Visibility = showWorkspacePath;
            AssetLibraryStatusBorder.Visibility = showWorkspacePath;
            AssetLibraryDetailStatusBorder.Visibility = showWorkspacePath;
            LogPanelBorder.Visibility = Settings.LogEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LogSettingControl_Changed(object sender, RoutedEventArgs e)
        {
            Settings.ShowWorkspacePath = WorkspacePathVisibleCheckBox.IsChecked == true;
            Settings.LogEnabled = LogEnabledCheckBox.IsChecked == true;
            Settings.LogUserOperations = LogUserOperationsCheckBox.IsChecked == true;
            Settings.LogWarnings = LogWarningCheckBox.IsChecked == true;
            Settings.LogErrors = LogErrorCheckBox.IsChecked == true;
            Settings.UiSoundEnabled = UiSoundEnabledCheckBox.IsChecked == true;
            UpdateLogOptionEnabledState();
            UpdateAuxiliaryDisplayVisibility();

            if (Settings.LogEnabled)
            {
                AppendLog(LogKind.User, "已更新日志输出设置。");
            }
        }

        private void AssetLibraryScrollSpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (AssetLibraryScrollSpeedValueText is null)
            {
                return;
            }

            Settings.AssetLibraryScrollSpeedMultiplier = AssetLibraryScrollSpeedSlider.Value;
        }

        private void UpdateAssetLibraryScrollSpeedText()
        {
            AssetLibraryScrollSpeedValueText.Text = Settings.AssetLibraryScrollSpeedText;
        }

        private void StoryTextFontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StoryTextFontSizeValueText is null)
            {
                return;
            }

            Settings.StoryTextFontSize = StoryTextFontSizeSlider.Value;
        }

        private void StoryChapterLengthDisplayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            Settings.ShowFullStoryChapterLength = ShowFullChapterLengthCheckBox?.IsChecked == true;
        }

        private void UpdateStoryTextFontSizeText()
        {
            StoryTextFontSizeValueText.Text = Settings.StoryTextFontSizeText;
        }

        private void ApplyStoryTextFontSizeToUi()
        {
            if (StoryTextTextBox is not null)
            {
                StoryTextTextBox.FontSize = Math.Clamp(Settings.StoryTextFontSize, 16, 32);
            }
        }

        private void EnsureProjectRootDirectory(string projectRootPath)
        {
            Settings.EnsureCurrentProjectRoot();

            LoadAllCards();
            AppendLog(LogKind.Info, $"已确认整体项目目录：{Settings.ProjectRootPath}");
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




