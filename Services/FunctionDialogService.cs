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

    public async Task<FunctionEntry?> SelectFunctionAsync(
        string title,
        IReadOnlyList<FunctionEntry> functions,
        string nextChoiceIndicator)
    {
        var selectionContent = StoryDialogContentFactory.CreateFunctionSelectionContent(
            functions,
            nextChoiceIndicator);
        var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            title,
            selectionContent.Content,
            "确定",
            "取消",
            ConfigureDialog: selectionContent.AttachDialog));
        return result == DialogResultKind.Primary
            ? selectionContent.SelectedFunction
            : null;
    }

    public async Task<StoryChoiceEditorResult?> EditChoiceNotesAsync(
        string choiceIndicator,
        IReadOnlyList<StoryChoiceTargetOption> targetOptions)
    {
        var dialogContent = StoryDialogContentFactory.CreateChoiceFunctionNoteContent(
            choiceIndicator,
            targetOptions,
            PlayPositiveSound,
            PlayNegativeSound);

        var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            "添加触发选项",
            dialogContent.Content,
            "确定",
            "取消"));
        return result == DialogResultKind.Primary
            ? dialogContent.ReadResult()
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
