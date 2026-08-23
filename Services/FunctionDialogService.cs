using System;
using System.Collections.Generic;
using System.Linq;
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
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.MainCode) ||
            input.MainCode.Contains('_') || input.MainCode.Contains('/') || input.MainCode.Any(char.IsWhiteSpace))
        {
            return null;
        }
        if (input.BranchMode == FunctionBranchMode.Fixed && input.Branches.Count == 0)
        {
            return null;
        }
        var branchCodes = input.Branches.Select(branch => branch.Code).ToList();
        return branchCodes.Any(code => code.Contains('/') || code.Any(char.IsWhiteSpace)) ||
               branchCodes.Distinct(System.StringComparer.OrdinalIgnoreCase).Count() != branchCodes.Count
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

    private void PlayPositiveSound()
    {
        _uiSoundService.Play(UiSoundKind.Positive);
    }

    private void PlayNegativeSound()
    {
        _uiSoundService.Play(UiSoundKind.Negative);
    }
}
