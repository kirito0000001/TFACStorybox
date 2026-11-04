using System.Collections.Generic;

namespace GalExcleTools.ViewModels;

internal sealed class ProjectWorkbenchViewModel : ObservableObject
{
    private string? _selectedProjectThumbnailPath;
    private ProjectInfo? _currentProject;
    private ProjectVoiceMapState _projectVoiceMapState = new();
    private ProjectLocalizationState _projectLocalizationState = new();
    private ChapterInfo? _selectedVoiceChapter;
    private ChapterInfo? _selectedTextToolChapter;
    private string _selectedLocalizationLanguage = "English";
    private ProjectTextToolMode _projectTextToolMode = ProjectTextToolMode.Voice;
    private bool _isCreatingChapter;

    public string? SelectedProjectThumbnailPath
    {
        get => _selectedProjectThumbnailPath;
        set => SetProperty(ref _selectedProjectThumbnailPath, value);
    }

    public ProjectInfo? CurrentProject
    {
        get => _currentProject;
        set => SetProperty(ref _currentProject, value);
    }

    public List<ProjectTextRow> ProjectTextRows { get; } = [];

    public ProjectVoiceMapState ProjectVoiceMapState
    {
        get => _projectVoiceMapState;
        set => SetProperty(ref _projectVoiceMapState, value);
    }

    public ProjectLocalizationState ProjectLocalizationState
    {
        get => _projectLocalizationState;
        set => SetProperty(ref _projectLocalizationState, value);
    }

    public ChapterInfo? SelectedVoiceChapter
    {
        get => _selectedVoiceChapter;
        set => SetProperty(ref _selectedVoiceChapter, value);
    }

    public ChapterInfo? SelectedTextToolChapter
    {
        get => _selectedTextToolChapter;
        set => SetProperty(ref _selectedTextToolChapter, value);
    }

    public string SelectedLocalizationLanguage
    {
        get => _selectedLocalizationLanguage;
        set => SetProperty(ref _selectedLocalizationLanguage, value);
    }

    public ProjectTextToolMode ProjectTextToolMode
    {
        get => _projectTextToolMode;
        set => SetProperty(ref _projectTextToolMode, value);
    }

    public bool IsCreatingChapter
    {
        get => _isCreatingChapter;
        set => SetProperty(ref _isCreatingChapter, value);
    }
}
