using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GalExcleTools.Services;

internal sealed class WinUiDialogService : IDialogService
{
    private readonly Func<XamlRoot> _getXamlRoot;
    private readonly IUiSoundService? _uiSoundService;

    public WinUiDialogService(Func<XamlRoot> getXamlRoot, IUiSoundService? uiSoundService = null)
    {
        _getXamlRoot = getXamlRoot;
        _uiSoundService = uiSoundService;
    }

    public async Task<DialogResultKind> ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
    {
        var dialog = CreateDialog(
            request.Title,
            request.Message,
            request.PrimaryButtonText,
            request.CloseButtonText,
            request.SecondaryButtonText);
        dialog.PrimaryButtonStyle = request.PrimaryButtonStyle;
        AttachButtonSounds(dialog, request.PrimarySound, request.CloseSound);
        AttachCancelShortcuts(dialog, request.CloseSound);
        var result = MapResult(await ShowDialogAsync(dialog, cancellationToken));
        return result;
    }

    public async Task<bool> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default)
    {
        return await ShowAsync(request, cancellationToken) == DialogResultKind.Primary;
    }

    public async Task<string?> PromptTextAsync(TextInputDialogRequest request, CancellationToken cancellationToken = default)
    {
        var textBox = new TextBox
        {
            Width = request.Width,
            Header = request.Header,
            Text = request.InitialText,
            PlaceholderText = request.PlaceholderText
        };
        if (request.MaxLength > 0)
        {
            textBox.MaxLength = request.MaxLength;
        }

        object content = textBox;
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            var panel = new StackPanel
            {
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = request.Message,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(textBox);
            content = panel;
        }

        var dialog = CreateDialog(
            request.Title,
            content,
            request.PrimaryButtonText,
            request.CloseButtonText);
        AttachButtonSounds(dialog, DialogSoundIntent.Positive, DialogSoundIntent.Negative);
        AttachCancelShortcuts(dialog, DialogSoundIntent.Negative);

        var result = await ShowDialogAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary ? textBox.Text.Trim() : null;
    }

    public async Task<DialogResultKind> ShowContentAsync(ContentDialogRequest request, CancellationToken cancellationToken = default)
    {
        var dialog = CreateDialog(
            request.Title,
            request.Content,
            request.PrimaryButtonText,
            request.CloseButtonText,
            request.SecondaryButtonText);
        dialog.DefaultButton = request.DefaultButton;
        dialog.PrimaryButtonStyle = request.PrimaryButtonStyle;
        request.ConfigureDialog?.Invoke(dialog);
        AttachButtonSounds(dialog, request.PrimarySound, request.CloseSound);
        AttachCancelShortcuts(dialog, request.CloseSound);
        return MapResult(await ShowDialogAsync(dialog, cancellationToken));
    }

    public async Task<T?> SelectAsync<T>(SelectionDialogRequest<T> request, CancellationToken cancellationToken = default)
    {
        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = request.MaxHeight,
            Width = request.Width
        };

        foreach (var item in request.Items)
        {
            var listItem = new ListViewItem
            {
                Content = item.Label,
                Tag = item.Value
            };
            if (item.ToolTip is not null)
            {
                ToolTipService.SetToolTip(listItem, item.ToolTip);
            }

            listView.Items.Add(listItem);
            if (request.IsSelected?.Invoke(item.Value) == true)
            {
                listView.SelectedItem = listItem;
            }
        }

        if (listView.SelectedItem is null && listView.Items.Count > 0)
        {
            listView.SelectedIndex = 0;
        }
        listView.SelectionChanged += (_, _) => _uiSoundService?.Play(UiSoundKind.Selection);

        var panel = new StackPanel
        {
            Spacing = 12
        };
        panel.Children.Add(new TextBlock
        {
            Text = request.Message,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(listView);

        var dialog = CreateDialog(
            request.Title,
            panel,
            request.PrimaryButtonText,
            request.CloseButtonText);
        AttachButtonSounds(dialog, DialogSoundIntent.Positive, DialogSoundIntent.Negative);
        AttachCancelShortcuts(dialog, DialogSoundIntent.Negative);

        var result = await ShowDialogAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary &&
            listView.SelectedItem is ListViewItem { Tag: T selectedValue }
                ? selectedValue
                : default;
    }

    private ContentDialog CreateDialog(
        string title,
        object content,
        string primaryButtonText,
        string closeButtonText,
        string? secondaryButtonText = null)
    {
        return new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = secondaryButtonText ?? string.Empty,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _getXamlRoot()
        };
    }

    private void AttachButtonSounds(
        ContentDialog dialog,
        DialogSoundIntent primarySound,
        DialogSoundIntent closeSound)
    {
        dialog.PrimaryButtonClick += (_, _) => PlayDialogSound(primarySound);
        dialog.SecondaryButtonClick += (_, _) => PlayDialogSound(DialogSoundIntent.Selection);
        dialog.CloseButtonClick += (_, _) => PlayDialogSound(closeSound);
    }

    private void AttachCancelShortcuts(ContentDialog dialog, DialogSoundIntent closeSound)
    {
        dialog.RightTapped += (_, args) =>
        {
            PlayDialogSound(closeSound);
            dialog.Hide();
            args.Handled = true;
        };
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape)
            {
                PlayDialogSound(closeSound);
                dialog.Hide();
                args.Handled = true;
            }
        };
    }

    private static async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(dialog.Hide);
        return await dialog.ShowAsync();
    }

    private static DialogResultKind MapResult(ContentDialogResult result)
    {
        return result switch
        {
            ContentDialogResult.Primary => DialogResultKind.Primary,
            ContentDialogResult.Secondary => DialogResultKind.Secondary,
            ContentDialogResult.None => DialogResultKind.None,
            _ => DialogResultKind.Cancel
        };
    }

    private void PlayDialogSound(DialogSoundIntent intent)
    {
        var kind = intent switch
        {
            DialogSoundIntent.Positive => UiSoundKind.Positive,
            DialogSoundIntent.Negative => UiSoundKind.Negative,
            DialogSoundIntent.Selection => UiSoundKind.Selection,
            _ => (UiSoundKind?)null
        };

        if (kind is not null)
        {
            _uiSoundService?.Play(kind.Value);
        }
    }
}
