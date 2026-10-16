using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Controls;
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
        // 带预览图的选择走「图片条带」（上方大图 + 下方横向缩略图），
        // 纯文字的选择保持 ListView（函数、说话人那些）—— 判据就是"有没有图"，不需要调用方多传开关。
        foreach (var candidate in request.Items)
        {
            if (!string.IsNullOrWhiteSpace(candidate.ImagePath))
            {
                return await SelectWithImageStripAsync(request, cancellationToken);
            }
        }

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

    /// <summary>
    /// 带预览图的选择：图片条带形态。
    ///
    /// 形态本身（大图 + 横向条带 + 选中态 + 方向键）在 <c>Controls/ImageStripPicker</c> 里，
    /// 这里只负责把请求喂进去、把选中值带出来 —— 以后改条带只动那个控件。
    /// </summary>
    private async Task<T?> SelectWithImageStripAsync<T>(
        SelectionDialogRequest<T> request,
        CancellationToken cancellationToken)
    {
        var items = new List<ImageStripPickerItem>(request.Items.Count);
        var initialIndex = 0;
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            if (request.IsSelected?.Invoke(item.Value) == true)
            {
                initialIndex = index;
            }

            items.Add(new ImageStripPickerItem(item.Label, item.ImagePath, item.Value));
        }

        // 大图要占地方：条带那点高度不够看细节（请求里的 MaxHeight 是给列表用的）。
        var picker = new ImageStripPicker
        {
            Width = request.Width,
            Height = Math.Max(420, request.MaxHeight)
        };
        picker.SetItems(items, initialIndex);
        picker.SelectionChanged += (_, _) => _uiSoundService?.Play(UiSoundKind.Selection);

        var panel = new StackPanel
        {
            Spacing = 12
        };
        panel.Children.Add(new TextBlock
        {
            Text = request.Message,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(picker);

        var dialog = CreateDialog(
            request.Title,
            panel,
            request.PrimaryButtonText,
            request.CloseButtonText);
        AttachButtonSounds(dialog, DialogSoundIntent.Positive, DialogSoundIntent.Negative);
        AttachCancelShortcuts(dialog, DialogSoundIntent.Negative);

        var result = await ShowDialogAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary && picker.SelectedValue is T selectedValue
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
        var xamlRoot = _getXamlRoot();
        return new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = secondaryButtonText ?? string.Empty,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            // 对话框挂在 XamlRoot 上、不在 RootGrid 的可视树里，不会跟着夜晚模式走；
            // 把根内容当前的实际主题抄给它（对齐零境工具箱 WinUiDialogService 的做法）。
            RequestedTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default
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
