using System.Collections.Generic;
using System.Threading.Tasks;
using GalExcleTools.Views;

namespace GalExcleTools.Services;

internal sealed class FunctionDialogService
{
    private readonly IDialogService _dialogService;
    private readonly IUiSoundService _uiSoundService;

    public FunctionDialogService(
        IDialogService dialogService,
        IUiSoundService uiSoundService)
    {
        _dialogService = dialogService;
        _uiSoundService = uiSoundService;
    }

    public async Task<FunctionEditorInput?> EditFunctionAsync(
        string title,
        FunctionEntry? function,
        string suggestedChoiceIndicator)
    {
        var editorContent = EditorDialogContentFactory.CreateFunctionEditorContent(
            function,
            suggestedChoiceIndicator,
            PlayPositiveSound,
            PlayNegativeSound);

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
        return string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Indicator)
            ? null
            : input;
    }

    public async Task<List<string>?> EditChoiceNotesAsync(string choiceIndicator)
    {
        var dialogContent = StoryDialogContentFactory.CreateChoiceFunctionNoteContent(
            choiceIndicator,
            PlayPositiveSound,
            PlayNegativeSound);

        var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            "添加触发选项",
            dialogContent.Content,
            "确定",
            "取消"));
        return result == DialogResultKind.Primary
            ? dialogContent.ReadNotes()
            : null;
    }

    private void PlayPositiveSound()
    {
        _uiSoundService.Play(UiSoundKind.Positive);
    }

    private void PlayNegativeSound()
    {
        _uiSoundService.Play(UiSoundKind.Negative);
    }
}
