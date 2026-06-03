namespace GalExcleTools.ViewModels;

internal sealed class UnrealSyncViewModel : ObservableObject
{
    private bool _runFullSyncRequested;
    private bool _isLoadingProjects;

    public bool RunFullSyncRequested
    {
        get => _runFullSyncRequested;
        set => SetProperty(ref _runFullSyncRequested, value);
    }

    public bool IsLoadingProjects
    {
        get => _isLoadingProjects;
        set => SetProperty(ref _isLoadingProjects, value);
    }
}
