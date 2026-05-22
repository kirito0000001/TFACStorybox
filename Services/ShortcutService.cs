using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

internal sealed class ShortcutService : IShortcutService
{
    private readonly IDialogService _dialogService;

    public ShortcutService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public async Task ShowShortcutHelpAsync(CancellationToken cancellationToken = default)
    {
        var message = string.Join(
            "\n",
            [
                "Q / E: switch adorn",
                "A / D: switch face",
                "Z / C: switch costume",
                "Numpad 4 / 6 or Left / Right: switch character",
                "Numpad 8 / 2 or Up / Down: switch filter",
                "Mouse side buttons: previous / next row",
                "Tab: clear hovered character slot",
                "Ctrl+Z: undo last edit",
                "Ctrl+C / Ctrl+V: copy/paste hovered character slot or base asset"
            ]);

        await _dialogService.ShowAsync(
            new DialogRequest(
                "Shortcut Help",
                message,
                PrimaryButtonText: string.Empty,
                CloseButtonText: "Close"),
            cancellationToken);
    }
}
