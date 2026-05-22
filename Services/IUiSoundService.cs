namespace GalExcleTools.Services;

public enum UiSoundKind
{
    Positive,
    Negative,
    Selection
}

public interface IUiSoundService
{
    bool IsEnabled { get; set; }

    void Play(UiSoundKind kind);
}
