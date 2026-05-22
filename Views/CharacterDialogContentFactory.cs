using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Views;

internal static class CharacterDialogContentFactory
{
    public static CharacterLayerAvailabilityDialogContent CreateCharacterLayerAvailabilityContent(
        IReadOnlyList<string> clothPaths,
        CharacterLayerScopeEntry existingEntry,
        Func<string, string> computeFileHash,
        Func<string, UIElement> createThumbnail,
        Action playSelectionSound)
    {
        return new CharacterLayerAvailabilityDialogContent(
            clothPaths,
            existingEntry,
            computeFileHash,
            createThumbnail,
            playSelectionSound);
    }
}

internal sealed class CharacterLayerAvailabilityDialogContent
{
    private readonly List<(CheckBox CheckBox, string CostumeHash)> _checkBoxes = [];

    public CharacterLayerAvailabilityDialogContent(
        IReadOnlyList<string> clothPaths,
        CharacterLayerScopeEntry existingEntry,
        Func<string, string> computeFileHash,
        Func<string, UIElement> createThumbnail,
        Action playSelectionSound)
    {
        var selectedHashes = existingEntry.CostumeHashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cards = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            Padding = new Thickness(8, 0, 8, 8)
        };

        foreach (var clothPath in clothPaths)
        {
            var costumeHash = computeFileHash(clothPath);
            var checkBox = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                IsChecked = existingEntry.UseAllCostumes || selectedHashes.Contains(costumeHash)
            };
            checkBox.Checked += (_, _) => playSelectionSound();
            checkBox.Unchecked += (_, _) => playSelectionSound();
            _checkBoxes.Add((checkBox, costumeHash));

            var card = new StackPanel
            {
                Width = 132,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    createThumbnail(clothPath),
                    new TextBlock
                    {
                        Text = Path.GetFileNameWithoutExtension(clothPath),
                        Width = 132,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    checkBox
                }
            };
            cards.Children.Add(card);
        }

        Content = new ScrollViewer
        {
            MaxWidth = 640,
            MaxHeight = 430,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = cards
        };
        Content.PointerWheelChanged += (_, args) =>
        {
            var delta = args.GetCurrentPoint(Content).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            var direction = delta > 0 ? -1 : 1;
            var targetOffset = Math.Clamp(
                Content.HorizontalOffset + 72 * direction,
                0,
                Content.ScrollableWidth);
            Content.ChangeView(targetOffset, null, null, true);
            args.Handled = true;
        };
    }

    public ScrollViewer Content { get; }

    public List<string> ReadCheckedHashes()
    {
        return _checkBoxes
            .Where(pair => pair.CheckBox.IsChecked == true)
            .Select(pair => pair.CostumeHash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
