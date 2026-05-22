using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Services;

public enum DialogResultKind
{
    None,
    Primary,
    Secondary,
    Cancel
}

public enum DialogSoundIntent
{
    None,
    Positive,
    Negative,
    Selection
}

public sealed record DialogRequest(
    string Title,
    string Message,
    string PrimaryButtonText = "OK",
    string CloseButtonText = "Cancel",
    string? SecondaryButtonText = null,
    DialogSoundIntent PrimarySound = DialogSoundIntent.Positive,
    DialogSoundIntent CloseSound = DialogSoundIntent.Negative,
    Style? PrimaryButtonStyle = null);

public sealed record TextInputDialogRequest(
    string Title,
    string Header,
    string InitialText = "",
    string PlaceholderText = "",
    string PrimaryButtonText = "OK",
    string CloseButtonText = "Cancel",
    string? Message = null,
    double Width = 360,
    int MaxLength = 0);

public sealed record ContentDialogRequest(
    string Title,
    UIElement Content,
    string PrimaryButtonText = "OK",
    string CloseButtonText = "Cancel",
    string? SecondaryButtonText = null,
    ContentDialogButton DefaultButton = ContentDialogButton.Primary,
    DialogSoundIntent PrimarySound = DialogSoundIntent.Positive,
    DialogSoundIntent CloseSound = DialogSoundIntent.Negative,
    Style? PrimaryButtonStyle = null,
    Action<ContentDialog>? ConfigureDialog = null);

public sealed record SelectionDialogItem<T>(string Label, T Value, ToolTip? ToolTip = null);

public sealed record SelectionDialogRequest<T>(
    string Title,
    string Message,
    IReadOnlyList<SelectionDialogItem<T>> Items,
    string PrimaryButtonText = "OK",
    string CloseButtonText = "Cancel",
    double Width = 420,
    double MaxHeight = 320,
    Func<T, bool>? IsSelected = null,
    Func<T, string>? DescriptionSelector = null);
