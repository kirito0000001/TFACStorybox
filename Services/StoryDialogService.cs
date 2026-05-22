using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using GalExcleTools.Views;

namespace GalExcleTools.Services;

internal sealed class StoryDialogService
{
    private readonly IDialogService _dialogService;

    public StoryDialogService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public async Task<object?> SelectSimpleChoiceAsync(string title, IReadOnlyList<StoryObjectChoice> choices)
    {
        var selected = await _dialogService.SelectAsync(
            StoryDialogContentFactory.CreateStorySimpleChoiceRequest(title, choices));
        return selected?.Value;
    }

    public async Task<object?> SelectPreviewChoiceAsync(string title, IReadOnlyList<StoryObjectChoice> choices)
    {
        var selected = await _dialogService.SelectAsync(
            await StoryDialogContentFactory.CreateStoryPreviewChoiceRequestAsync(title, choices));
        return selected?.Value;
    }

    public Task<StoryAssetChoice?> SelectAssetChoiceAsync(
        string title,
        IReadOnlyList<StoryAssetChoice> choices,
        int currentIndex)
    {
        return _dialogService.SelectAsync(new SelectionDialogRequest<StoryAssetChoice>(
            title,
            "选择要写入当前剧情行的素材索引。",
            choices
                .Select(choice => new SelectionDialogItem<StoryAssetChoice>($"{choice.Index}: {choice.Name}", choice))
                .ToList(),
            "确定",
            "取消",
            420,
            420,
            choice => choice.Index == currentIndex));
    }

    public Task ShowCurrentChoicesAsync(
        IReadOnlyList<string> choices,
        IReadOnlyDictionary<string, List<string>> noteMap)
    {
        return _dialogService.ShowContentAsync(new ContentDialogRequest(
            "查看选项",
            StoryDialogContentFactory.CreateCurrentStoryChoicesContent(choices, noteMap),
            string.Empty,
            "关闭",
            DefaultButton: ContentDialogButton.Close,
            PrimarySound: DialogSoundIntent.None));
    }
}
