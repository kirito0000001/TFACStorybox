using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.ViewModels;

internal sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _settingsService;
    private readonly ProjectRootMigrationService _migrationService;
    private AppSettings _settings = new();
    private string _projectRootPath = AppSettingsService.DefaultProjectRootPath;
    private string _projectRootStatusTitle = "目录已就绪";
    private string _projectRootStatusMessage = string.Empty;
    private InfoBarSeverity _projectRootStatusSeverity = InfoBarSeverity.Success;
    private bool _showWorkspacePath = true;
    private bool _logEnabled = true;
    private bool _logUserOperations = true;
    private bool _logWarnings = true;
    private bool _logErrors = true;
    private bool _uiSoundEnabled;
    private double _assetLibraryScrollSpeedMultiplier = 1.5;
    private double _storyTextFontSize = 20;
    private bool _showFullStoryChapterLength;
    private bool _isLoadingSettings;

    public SettingsViewModel(
        AppSettingsService settingsService,
        ProjectRootMigrationService migrationService)
    {
        _settingsService = settingsService;
        _migrationService = migrationService;
    }

    public event EventHandler? AuxiliaryDisplayChanged;

    public event EventHandler? LogSettingsChanged;

    public event EventHandler? UiSoundSettingChanged;

    public event EventHandler? StoryTextFontSizeChanged;

    public event EventHandler? ShowFullStoryChapterLengthChanged;

    public AppSettings CurrentSettings => _settings;

    public string ProjectRootPath
    {
        get => _projectRootPath;
        private set
        {
            if (SetProperty(ref _projectRootPath, value))
            {
                OnPropertyChanged(nameof(WorkspaceStatusText));
                OnPropertyChanged(nameof(AssetLibraryStatusText));
            }
        }
    }

    public string WorkspaceStatusText => $"工作区路径：{ProjectRootPath}";

    public string AssetLibraryStatusText => $"素材库根目录：{ProjectRootPath}";

    public string ProjectRootStatusTitle
    {
        get => _projectRootStatusTitle;
        private set => SetProperty(ref _projectRootStatusTitle, value);
    }

    public string ProjectRootStatusMessage
    {
        get => _projectRootStatusMessage;
        private set => SetProperty(ref _projectRootStatusMessage, value);
    }

    public InfoBarSeverity ProjectRootStatusSeverity
    {
        get => _projectRootStatusSeverity;
        private set => SetProperty(ref _projectRootStatusSeverity, value);
    }

    public bool ShowWorkspacePath
    {
        get => _showWorkspacePath;
        set
        {
            if (SetProperty(ref _showWorkspacePath, value))
            {
                _settings.ShowWorkspacePath = value;
                if (!_isLoadingSettings)
                {
                    Save();
                    AuxiliaryDisplayChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public bool LogEnabled
    {
        get => _logEnabled;
        set
        {
            if (SetProperty(ref _logEnabled, value))
            {
                _settings.LogEnabled = value;
                if (!_isLoadingSettings)
                {
                    Save();
                    LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public bool LogUserOperations
    {
        get => _logUserOperations;
        set
        {
            if (SetProperty(ref _logUserOperations, value))
            {
                _settings.LogUserOperations = value;
                if (!_isLoadingSettings)
                {
                    Save();
                    LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public bool LogWarnings
    {
        get => _logWarnings;
        set
        {
            if (SetProperty(ref _logWarnings, value))
            {
                _settings.LogWarnings = value;
                if (!_isLoadingSettings)
                {
                    Save();
                    LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public bool LogErrors
    {
        get => _logErrors;
        set
        {
            if (SetProperty(ref _logErrors, value))
            {
                _settings.LogErrors = value;
                if (!_isLoadingSettings)
                {
                    Save();
                    LogSettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public bool UiSoundEnabled
    {
        get => _uiSoundEnabled;
        set
        {
            if (SetProperty(ref _uiSoundEnabled, value))
            {
                _settings.UiSoundEnabled = value;
                if (!_isLoadingSettings)
                {
                    Save();
                    UiSoundSettingChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public double AssetLibraryScrollSpeedMultiplier
    {
        get => _assetLibraryScrollSpeedMultiplier;
        set
        {
            var roundedValue = Math.Round(value, 1);
            if (SetProperty(ref _assetLibraryScrollSpeedMultiplier, roundedValue))
            {
                _settings.AssetLibraryScrollSpeedMultiplier = roundedValue;
                OnPropertyChanged(nameof(AssetLibraryScrollSpeedText));
                if (!_isLoadingSettings)
                {
                    Save();
                }
            }
        }
    }

    public string AssetLibraryScrollSpeedText => $"{AssetLibraryScrollSpeedMultiplier:0.0}x";

    public double StoryTextFontSize
    {
        get => _storyTextFontSize;
        set
        {
            var roundedValue = Math.Round(value);
            if (SetProperty(ref _storyTextFontSize, roundedValue))
            {
                _settings.StoryTextFontSize = roundedValue;
                OnPropertyChanged(nameof(StoryTextFontSizeText));
                if (!_isLoadingSettings)
                {
                    Save();
                    StoryTextFontSizeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public string StoryTextFontSizeText => $"{Math.Round(StoryTextFontSize)}";

    public bool ShowFullStoryChapterLength
    {
        get => _showFullStoryChapterLength;
        set
        {
            if (SetProperty(ref _showFullStoryChapterLength, value))
            {
                _settings.ShowFullStoryChapterLength = value;
                if (!_isLoadingSettings)
                {
                    Save();
                    ShowFullStoryChapterLengthChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public void Load()
    {
        _settings = _settingsService.Load();
        ProjectRootPath = _settingsService.ResolveProjectRootPath(_settings);
        SetBindableSettings(_settings);
    }

    public string BuildProjectRootPathFromParent(string parentPath)
    {
        return _settingsService.BuildProjectRootPathFromParent(parentPath);
    }

    public bool IsCurrentProjectRoot(string candidateProjectRootPath)
    {
        return PathsEqual(ProjectRootPath, candidateProjectRootPath);
    }

    public bool IsCandidateInsideCurrentRoot(string candidateProjectRootPath)
    {
        return IsPathInsideDirectory(candidateProjectRootPath, ProjectRootPath);
    }

    public void EnsureCurrentProjectRoot()
    {
        _settingsService.EnsureProjectRootDirectory(ProjectRootPath);
        SetProjectRootStatus(InfoBarSeverity.Success, "目录已就绪", $"已确认目录存在：{ProjectRootPath}");
    }

    public void SetProjectRootStatus(InfoBarSeverity severity, string title, string message)
    {
        ProjectRootStatusSeverity = severity;
        ProjectRootStatusTitle = title;
        ProjectRootStatusMessage = message;
    }

    public async Task<MigrationResult> ChangeProjectRootAsync(
        string newProjectRootPath,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var oldProjectRootPath = Path.GetFullPath(ProjectRootPath);
        var result = await Task.Run(
            () => _migrationService.Migrate(
                oldProjectRootPath,
                newProjectRootPath,
                progress,
                cancellationToken),
            cancellationToken);

        ProjectRootPath = newProjectRootPath;
        _settings.ProjectRootPath = ProjectRootPath;
        Save();
        EnsureCurrentProjectRoot();
        return result;
    }

    public bool ShouldWriteLog(LogKind kind)
    {
        if (!LogEnabled)
        {
            return false;
        }

        return kind switch
        {
            LogKind.User => LogUserOperations,
            LogKind.Warning => LogWarnings,
            LogKind.Error => LogErrors,
            _ => true
        };
    }

    public void Save()
    {
        _settingsService.Save(_settings);
    }

    private void SetBindableSettings(AppSettings settings)
    {
        _isLoadingSettings = true;
        try
        {
            ShowWorkspacePath = settings.ShowWorkspacePath;
            LogEnabled = settings.LogEnabled;
            LogUserOperations = settings.LogUserOperations;
            LogWarnings = settings.LogWarnings;
            LogErrors = settings.LogErrors;
            UiSoundEnabled = settings.UiSoundEnabled;
            AssetLibraryScrollSpeedMultiplier = settings.AssetLibraryScrollSpeedMultiplier;
            StoryTextFontSize = Math.Clamp(settings.StoryTextFontSize, 16, 32);
            ShowFullStoryChapterLength = settings.ShowFullStoryChapterLength;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        var firstFullPath = Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var secondFullPath = Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(firstFullPath, secondFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideDirectory(string candidatePath, string parentDirectoryPath)
    {
        var candidateFullPath = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentFullPath = Path.GetFullPath(parentDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidateFullPath.StartsWith(
            parentFullPath + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
