namespace GalExcleTools.ViewModels;

internal sealed class StoryChoiceEditorOptionViewModel : ObservableObject
{
    private string _optionKey;
    private string _text;
    private string _targetSectionFirstRowId;
    private string _targetDisplayName;

    public StoryChoiceEditorOptionViewModel(
        string optionKey,
        string text,
        string targetSectionFirstRowId,
        string targetDisplayName)
    {
        _optionKey = optionKey;
        _text = text;
        _targetSectionFirstRowId = targetSectionFirstRowId;
        _targetDisplayName = targetDisplayName;
    }

    public string OptionKey
    {
        get => _optionKey;
        set => SetProperty(ref _optionKey, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string DisplayText => string.IsNullOrWhiteSpace(Text) ? "未填写选项内容" : Text;

    public string TargetSectionFirstRowId
    {
        get => _targetSectionFirstRowId;
        set => SetProperty(ref _targetSectionFirstRowId, value);
    }

    public string TargetDisplayName
    {
        get => _targetDisplayName;
        set => SetProperty(ref _targetDisplayName, value);
    }
}
