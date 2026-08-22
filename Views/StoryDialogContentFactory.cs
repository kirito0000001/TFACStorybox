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
        IReadOnlyList<StoryChoiceTargetOption> targetOptions,
        IReadOnlyList<StoryChoiceEditorOption>? initialOptions,
        Action playPositiveSound,
        Action playNegativeSound)
    {
        return new ChoiceFunctionNoteDialogContent(choiceIndicator, targetOptions, initialOptions, playPositiveSound, playNegativeSound);
    }

    public static FunctionSelectionDialogContent CreateFunctionSelectionContent(
        IReadOnlyList<FunctionEntry> functions,
        string nextChoiceIndicator)
    {
        return new FunctionSelectionDialogContent(functions, nextChoiceIndicator);
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

    public static ToolTip CreateLazyImagePreviewToolTip(string previewPath)
    {
        var image = new Image
        {
            Width = 360,
            Height = 220,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var toolTip = new ToolTip
        {
            Content = new Border
            {
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(238, 30, 30, 30)),
                CornerRadius = new CornerRadius(6),
                Child = image
            }
        };
        var previewRequestId = 0;

        toolTip.Opened += async (_, _) =>
        {
            var requestId = ++previewRequestId;
            if (await LoadPreviewImageAsync(image, previewPath) &&
                requestId == previewRequestId &&
                toolTip.IsOpen)
            {
                image.Visibility = Visibility.Visible;
                return;
            }

            image.Source = null;
            if (requestId == previewRequestId)
            {
                toolTip.IsOpen = false;
            }
        };
        toolTip.Closed += (_, _) =>
        {
            previewRequestId++;
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
        };

        return toolTip;
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

    private static async Task<bool> LoadPreviewImageAsync(Image image, string imagePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
            return true;
        }
        catch
        {
            image.Source = null;
            return false;
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

internal sealed class FunctionSelectionDialogContent
{
    private readonly IReadOnlyList<FunctionSelectionRow> _rows;
    private readonly AutoSuggestBox _searchBox;
    private readonly ListView _listView;
    private readonly TextBlock _emptyText;

    public FunctionSelectionDialogContent(
        IReadOnlyList<FunctionEntry> functions,
        string nextChoiceIndicator)
    {
        _rows = functions
            .Select(function =>
            {
                var indicator = StoryFunctionService.IsChoiceFunctionTemplate(function)
                    ? nextChoiceIndicator
                    : function.Indicator;
                return new FunctionSelectionRow(
                    function,
                    function.Name,
                    indicator,
                    $"{function.Name} {indicator}");
            })
            .ToList();

        _listView = new ListView
        {
            Width = 560,
            MaxHeight = 460,
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _searchBox = new AutoSuggestBox
        {
            PlaceholderText = "搜索中文名称或英文代码",
            QueryIcon = new SymbolIcon(Symbol.Find),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _searchBox.TextChanged += (_, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                RefreshRows(_searchBox.Text);
            }
        };
        _searchBox.QuerySubmitted += (_, _) =>
        {
            if (_listView.SelectedItem is null && _listView.Items.Count > 0)
            {
                _listView.SelectedIndex = 0;
            }
            _listView.Focus(FocusState.Keyboard);
        };

        _emptyText = new TextBlock
        {
            Text = "没有匹配的函数",
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.6,
            Visibility = Visibility.Collapsed
        };

        Content = new StackPanel
        {
            Width = 560,
            Spacing = 10,
            Children =
            {
                _searchBox,
                _listView,
                _emptyText
            }
        };
        RefreshRows(string.Empty);
    }

    public StackPanel Content { get; }

    public FunctionEntry? SelectedFunction =>
        (_listView.SelectedItem as ListViewItem)?.Tag as FunctionEntry;

    public void AttachDialog(ContentDialog dialog)
    {
        dialog.IsPrimaryButtonEnabled = SelectedFunction is not null;
        _listView.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = SelectedFunction is not null;
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = SelectedFunction is null;
        dialog.Opened += (_, _) => _searchBox.Focus(FocusState.Keyboard);
    }

    private void RefreshRows(string searchText)
    {
        var query = searchText.Trim();
        var filteredRows = string.IsNullOrWhiteSpace(query)
            ? _rows
            : _rows.Where(row => row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        _listView.Items.Clear();
        foreach (var row in filteredRows)
        {
            var primaryText = new TextBlock
            {
                Text = row.DisplayName,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            var secondaryText = new TextBlock
            {
                Text = row.Indicator,
                FontSize = 12,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            var listItem = new ListViewItem
            {
                MinHeight = 56,
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        primaryText,
                        secondaryText
                    }
                },
                Tag = row.Function
            };
            ToolTipService.SetToolTip(listItem, row.Indicator);
            _listView.Items.Add(listItem);
        }

        _listView.SelectedIndex = _listView.Items.Count > 0 ? 0 : -1;
        _emptyText.Visibility = _listView.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record FunctionSelectionRow(
        FunctionEntry Function,
        string DisplayName,
        string Indicator,
        string SearchText);
}

internal sealed class ChoiceFunctionNoteDialogContent
{
    private readonly StackPanel _notesPanel;
    private readonly IReadOnlyList<StoryChoiceTargetOption> _targetOptions;
    private readonly TextBlock _summaryText;
    private readonly Action _playPositiveSound;
    private readonly Action _playNegativeSound;

    public ChoiceFunctionNoteDialogContent(
        string choiceIndicator,
        IReadOnlyList<StoryChoiceTargetOption> targetOptions,
        IReadOnlyList<StoryChoiceEditorOption>? initialOptions,
        Action playPositiveSound,
        Action playNegativeSound)
    {
        _targetOptions = targetOptions;
        _playPositiveSound = playPositiveSound;
        _playNegativeSound = playNegativeSound;
        _notesPanel = new StackPanel
        {
            Width = 560,
            Spacing = 8
        };
        _summaryText = new TextBlock
        {
            FontSize = 12,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (initialOptions is { Count: > 0 })
        {
            foreach (var option in initialOptions)
            {
                AddChoiceRow(option);
            }
        }
        else
        {
            AddChoiceRow();
        }

        var addButton = new Button
        {
            Height = 36,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new SymbolIcon(Symbol.Add),
                    new TextBlock { Text = "添加选项", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        addButton.Click += (_, _) =>
        {
            _playPositiveSound();
            AddChoiceRow();
        };

        var heading = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = choiceIndicator,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = "选项内容与分支目标",
                    FontSize = 12,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                }
            }
        };
        var columnHeader = CreateColumnHeader();
        var optionsScroll = new ScrollViewer
        {
            MaxHeight = 420,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _notesPanel
        };
        var footer = new Grid
        {
            ColumnSpacing = 12
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_summaryText, 1);
        footer.Children.Add(addButton);
        footer.Children.Add(_summaryText);

        var content = new Grid
        {
            Width = 560,
            RowSpacing = 12
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(columnHeader, 1);
        Grid.SetRow(optionsScroll, 2);
        Grid.SetRow(footer, 3);
        content.Children.Add(heading);
        content.Children.Add(columnHeader);
        content.Children.Add(optionsScroll);
        content.Children.Add(footer);
        Content = content;
        UpdateSummary();
    }

    public FrameworkElement Content { get; }

    public StoryChoiceEditorResult ReadResult()
    {
        var options = new List<StoryChoiceEditorOption>();
        foreach (var row in GetOptionRows())
        {
            var note = TextUtility.NormalizeFunctionChoiceNote(row.Children.OfType<TextBox>().FirstOrDefault()?.Text);
            var targetId = row.Children.OfType<ComboBox>().FirstOrDefault()?.SelectedItem is ComboBoxItem item
                ? item.Tag as string ?? string.Empty
                : string.Empty;
            options.Add(new StoryChoiceEditorOption(note, targetId));
        }

        return new StoryChoiceEditorResult(options);
    }

    private void AddChoiceRow(StoryChoiceEditorOption? initialOption = null)
    {
        var row = CreateOptionRow(initialOption);
        var container = new Border
        {
            MinHeight = 60,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            Child = row
        };
        _notesPanel.Children.Add(container);
        RenumberRows();
    }

    private Grid CreateOptionRow(StoryChoiceEditorOption? initialOption)
    {
        var row = new Grid
        {
            ColumnSpacing = 10
        };
        AddOptionColumns(row);

        var optionLabel = new TextBlock
        {
            Text = StoryBranchService.GetOptionKey(_notesPanel.Children.Count),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var noteBox = new TextBox
        {
            PlaceholderText = "例如：接受邀请",
            Text = initialOption?.Text ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 40,
            VerticalAlignment = VerticalAlignment.Center
        };
        var targetBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 40,
            VerticalAlignment = VerticalAlignment.Center
        };
        targetBox.Items.Add(new ComboBoxItem { Content = "不跳转", Tag = string.Empty });
        foreach (var target in _targetOptions)
        {
            targetBox.Items.Add(new ComboBoxItem
            {
                Content = target.DisplayName,
                Tag = target.TargetSectionFirstRowId
            });
        }
        targetBox.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(initialOption?.TargetSectionFirstRowId))
        {
            for (var index = 0; index < targetBox.Items.Count; index++)
            {
                if (targetBox.Items[index] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, initialOption.TargetSectionFirstRowId, StringComparison.OrdinalIgnoreCase))
                {
                    targetBox.SelectedIndex = index;
                    break;
                }
            }
        }
        targetBox.SelectionChanged += (_, _) => UpdateSummary();
        var removeButton = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Content = new SymbolIcon(Symbol.Delete),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(removeButton, "删除这个选项");
        removeButton.Click += (_, _) =>
        {
            _playNegativeSound();
            var container = _notesPanel.Children
                .OfType<Border>()
                .FirstOrDefault(item => ReferenceEquals(item.Child, row));
            if (container is not null)
            {
                _notesPanel.Children.Remove(container);
            }
            if (_notesPanel.Children.Count == 0)
            {
                AddChoiceRow();
                return;
            }

            RenumberRows();
        };

        Grid.SetColumn(noteBox, 1);
        Grid.SetColumn(targetBox, 2);
        Grid.SetColumn(removeButton, 3);
        row.Children.Add(optionLabel);
        row.Children.Add(noteBox);
        row.Children.Add(targetBox);
        row.Children.Add(removeButton);
        return row;
    }

    private void RenumberRows()
    {
        var rows = GetOptionRows().ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Children.OfType<TextBlock>().First().Text = StoryBranchService.GetOptionKey(i);
        }

        UpdateSummary();
    }

    private IEnumerable<Grid> GetOptionRows()
    {
        return _notesPanel.Children
            .OfType<Border>()
            .Select(container => container.Child)
            .OfType<Grid>();
    }

    private void UpdateSummary()
    {
        var rows = GetOptionRows().ToList();
        var targetCount = rows.Count(row =>
            row.Children.OfType<ComboBox>().FirstOrDefault()?.SelectedItem is ComboBoxItem item &&
            !string.IsNullOrWhiteSpace(item.Tag as string));
        _summaryText.Text = $"{rows.Count} 个选项 · {targetCount} 个目标";
    }

    private static Grid CreateColumnHeader()
    {
        var grid = new Grid
        {
            Padding = new Thickness(10, 0, 10, 0),
            ColumnSpacing = 10
        };
        AddOptionColumns(grid);
        grid.Children.Add(CreateColumnHeader("选项", 0));
        grid.Children.Add(CreateColumnHeader("选项内容", 1));
        grid.Children.Add(CreateColumnHeader("前往小节", 2));
        grid.Children.Add(CreateColumnHeader("操作", 3));
        return grid;
    }

    private static TextBlock CreateColumnHeader(string text, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            HorizontalAlignment = column is 0 or 3 ? HorizontalAlignment.Center : HorizontalAlignment.Left
        };
        Grid.SetColumn(label, column);
        return label;
    }

    private static void AddOptionColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
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
