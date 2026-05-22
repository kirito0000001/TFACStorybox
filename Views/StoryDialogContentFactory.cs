using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GalExcleTools.Views;

internal static class StoryDialogContentFactory
{
    public static ChoiceFunctionNoteDialogContent CreateChoiceFunctionNoteContent(
        string choiceIndicator,
        Action playPositiveSound,
        Action playNegativeSound)
    {
        return new ChoiceFunctionNoteDialogContent(choiceIndicator, playPositiveSound, playNegativeSound);
    }

    public static UIElement CreateCurrentStoryChoicesContent(
        IReadOnlyList<string> choices,
        IReadOnlyDictionary<string, List<string>> noteMap)
    {
        var choicesPanel = new StackPanel
        {
            Spacing = 16
        };
        foreach (var choice in choices)
        {
            noteMap.TryGetValue(choice, out var notes);
            var choicePanel = new StackPanel
            {
                Spacing = 8
            };
            choicePanel.Children.Add(DialogContentFactory.CreateResultHeading(choice));

            if (notes is { Count: > 0 })
            {
                for (var i = 0; i < notes.Count; i++)
                {
                    var row = CreateChoiceNoteRow(
                        $"选项 {i + 1}",
                        string.IsNullOrWhiteSpace(notes[i]) ? "无备注" : notes[i],
                        isReadOnly: true);
                    choicePanel.Children.Add(row);
                }
            }
            else
            {
                choicePanel.Children.Add(DialogContentFactory.CreateSubtleParagraph("无备注"));
            }

            choicesPanel.Children.Add(choicePanel);
        }

        return new ScrollViewer
        {
            Width = 520,
            MaxHeight = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = choicesPanel
        };
    }

    public static StorySectionImportDialogContent CreateStorySectionImportContent(
        Func<Task> pickCsvFilesAsync,
        Func<IReadOnlyList<string>, Task> acceptDroppedFilesAsync)
    {
        return new StorySectionImportDialogContent(pickCsvFilesAsync, acceptDroppedFilesAsync);
    }

    public static FrameworkElement CreateStoryCsvCompatibilityContent(
        string sourceCsvPath,
        StoryCsvCompatibility compatibility)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            MaxWidth = 520
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"文件：{System.IO.Path.GetFileName(sourceCsvPath)}",
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = compatibility.IsCompatible ? InfoBarSeverity.Success : InfoBarSeverity.Error,
            Title = compatibility.IsCompatible ? "结构体兼容" : "结构体不兼容",
            Message = compatibility.IsCompatible
                ? "CSV 表头可以映射到当前 FStoryStruct。额外列会在导入副本中忽略。"
                : "CSV 表头缺少剧情表必要字段，已取消导入。"
        });

        if (compatibility.MissingColumns.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"缺少字段：{string.Join(", ", compatibility.MissingColumns)}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Firebrick)
            });
        }

        if (compatibility.ExtraColumns.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"额外字段：{string.Join(", ", compatibility.ExtraColumns.Take(12))}{(compatibility.ExtraColumns.Count > 12 ? " ..." : string.Empty)}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkGoldenrod)
            });
        }

        return panel;
    }

    public static SelectionDialogRequest<StoryObjectChoice> CreateStorySimpleChoiceRequest(
        string title,
        IReadOnlyList<StoryObjectChoice> choices)
    {
        return CreateStoryChoiceRequestCore(
            title,
            choices.Select(choice => new SelectionDialogItem<StoryObjectChoice>(choice.DisplayName, choice)).ToList());
    }

    public static async Task<SelectionDialogRequest<StoryObjectChoice>> CreateStoryPreviewChoiceRequestAsync(
        string title,
        IReadOnlyList<StoryObjectChoice> choices)
    {
        var items = new List<SelectionDialogItem<StoryObjectChoice>>();
        foreach (var choice in choices)
        {
            ToolTip? toolTip = null;
            if (choice.PreviewPaths is { Count: > 0 })
            {
                toolTip = await CreateStoryChoicePreviewToolTipAsync(choice.PreviewPaths);
            }

            items.Add(new SelectionDialogItem<StoryObjectChoice>(choice.DisplayName, choice, toolTip));
        }

        return CreateStoryChoiceRequestCore(title, items);
    }

    private static SelectionDialogRequest<StoryObjectChoice> CreateStoryChoiceRequestCore(
        string title,
        IReadOnlyList<SelectionDialogItem<StoryObjectChoice>> items)
    {
        return new SelectionDialogRequest<StoryObjectChoice>(
            title,
            "选择一个项目后确认。",
            items,
            "确定",
            "取消",
            420,
            420);
    }

    private static async Task<ToolTip> CreateStoryChoicePreviewToolTipAsync(IReadOnlyList<string> previewPaths)
    {
        var grid = new Grid
        {
            Width = 220,
            Height = 320,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(224, 36, 36, 36))
        };

        foreach (var previewPath in previewPaths.Where(IsPreviewableImagePath))
        {
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(image);
            await LoadPreviewImageAsync(image, previewPath);
        }

        return new ToolTip
        {
            Content = new Border
            {
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(238, 30, 30, 30)),
                CornerRadius = new CornerRadius(6),
                Child = grid
            }
        };
    }

    private static async Task LoadPreviewImageAsync(Image image, string imagePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch
        {
            image.Source = null;
        }
    }

    private static bool IsPreviewableImagePath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static Grid CreateChoiceNoteRow(string label, string text, bool isReadOnly)
    {
        var row = new Grid
        {
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var noteBox = new TextBox
        {
            Text = text,
            IsReadOnly = isReadOnly,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = isReadOnly ? 0 : 42,
            PlaceholderText = isReadOnly ? string.Empty : "例如：接受邀请"
        };
        Grid.SetColumn(noteBox, 1);
        row.Children.Add(noteBox);
        return row;
    }
}

internal sealed class ChoiceFunctionNoteDialogContent
{
    private readonly StackPanel _notesPanel;
    private readonly Action _playPositiveSound;
    private readonly Action _playNegativeSound;

    public ChoiceFunctionNoteDialogContent(
        string choiceIndicator,
        Action playPositiveSound,
        Action playNegativeSound)
    {
        _playPositiveSound = playPositiveSound;
        _playNegativeSound = playNegativeSound;
        _notesPanel = new StackPanel
        {
            Spacing = 8
        };

        AddChoiceRow();
        var addButton = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = "+"
        };
        addButton.Click += (_, _) =>
        {
            _playPositiveSound();
            AddChoiceRow();
        };

        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                DialogContentFactory.CreateResultHeading(choiceIndicator),
                DialogContentFactory.CreateSubtleParagraph("这一个 Choice 会在虚幻里弹出选择界面；下面每行只是这个界面里的一个选项备注。"),
                _notesPanel,
                addButton,
                DialogContentFactory.CreateResultHeading($"表格只会写入 {choiceIndicator}。"),
                DialogContentFactory.CreateSubtleParagraph("备注不会写入剧情表 Custom 字段。")
            }
        };
    }

    public StackPanel Content { get; }

    public List<string> ReadNotes()
    {
        var notes = new List<string>();
        foreach (var row in _notesPanel.Children.OfType<Grid>())
        {
            var note = TextUtility.NormalizeFunctionChoiceNote(row.Children.OfType<TextBox>().FirstOrDefault()?.Text);
            notes.Add(note);
        }

        return notes;
    }

    private void AddChoiceRow()
    {
        var row = new Grid
        {
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var optionLabel = new TextBlock
        {
            Text = $"选项 {_notesPanel.Children.Count + 1}",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var noteBox = new TextBox
        {
            PlaceholderText = "例如：接受邀请",
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 42
        };
        var removeButton = new Button
        {
            Content = "删除",
            Margin = new Thickness(0, 28, 0, 0)
        };
        removeButton.Click += (_, _) =>
        {
            _playNegativeSound();
            _notesPanel.Children.Remove(row);
            if (_notesPanel.Children.Count == 0)
            {
                AddChoiceRow();
                return;
            }

            RenumberRows();
        };

        Grid.SetColumn(noteBox, 1);
        Grid.SetColumn(removeButton, 2);
        row.Children.Add(optionLabel);
        row.Children.Add(noteBox);
        row.Children.Add(removeButton);
        _notesPanel.Children.Add(row);
        RenumberRows();
    }

    private void RenumberRows()
    {
        for (var i = 0; i < _notesPanel.Children.Count; i++)
        {
            if (_notesPanel.Children[i] is not Grid row)
            {
                continue;
            }

            foreach (var textBox in row.Children.OfType<TextBox>())
            {
                textBox.Header = $"选项备注 {i + 1}";
            }

            foreach (var textBlock in row.Children.OfType<TextBlock>())
            {
                textBlock.Text = $"选项 {i + 1}";
            }
        }
    }
}

internal sealed class StorySectionImportDialogContent
{
    private readonly Func<Task> _pickCsvFilesAsync;
    private readonly Func<IReadOnlyList<string>, Task> _acceptDroppedFilesAsync;

    public StorySectionImportDialogContent(
        Func<Task> pickCsvFilesAsync,
        Func<IReadOnlyList<string>, Task> acceptDroppedFilesAsync)
    {
        _pickCsvFilesAsync = pickCsvFilesAsync;
        _acceptDroppedFilesAsync = acceptDroppedFilesAsync;
        var plusText = new TextBlock
        {
            Text = "+",
            FontSize = 72,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var hintText = new TextBlock
        {
            Text = "点击选择 CSV，或把小节 CSV 拖到这里",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
        };

        Content = new Border
        {
            MinHeight = 260,
            Padding = new Thickness(24),
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            AllowDrop = true,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    plusText,
                    hintText
                }
            }
        };

        Content.Tapped += async (_, _) => await _pickCsvFilesAsync();
        Content.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "导入小节 CSV";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        };
        Content.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            var csvPaths = items
                .OfType<StorageFile>()
                .Select(file => file.Path)
                .Where(path => string.Equals(System.IO.Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                .ToList();
            await _acceptDroppedFilesAsync(csvPaths);
        };
    }

    public Border Content { get; }
}
