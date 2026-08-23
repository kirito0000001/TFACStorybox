using System;
using System.Linq;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Views;

internal static class EditorDialogContentFactory
{
    public static ChapterEditorDialogContent CreateChapterEditorContent(
        ProjectInfo project,
        ChapterInfo? chapter,
        UIElement? introContent,
        Func<string, string, string> buildChapterCodeSegment,
        Func<string, string, string> getChapterCodeSegment)
    {
        return new ChapterEditorDialogContent(project, chapter, introContent, buildChapterCodeSegment, getChapterCodeSegment);
    }

    public static FunctionEditorDialogContent CreateFunctionEditorContent(
        FunctionEntry? function,
        string suggestedChoiceIndicator,
        Action playPositiveSound,
        Action playNegativeSound)
    {
        return new FunctionEditorDialogContent(function, suggestedChoiceIndicator, playPositiveSound, playNegativeSound);
    }

    public static CharacterEditorDialogContent CreateCharacterEditorContent(CharacterInfo? character)
    {
        return new CharacterEditorDialogContent(character);
    }
}

internal sealed class ChapterEditorDialogContent
{
    private readonly ProjectInfo _project;
    private readonly TextBox _nameBox;
    private readonly ComboBox _typeBox;
    private readonly TextBox _customCodeBox;
    private readonly TextBlock _previewText;
    private readonly Func<string, string, string> _buildChapterCodeSegment;

    public ChapterEditorDialogContent(
        ProjectInfo project,
        ChapterInfo? chapter,
        UIElement? introContent,
        Func<string, string, string> buildChapterCodeSegment,
        Func<string, string, string> getChapterCodeSegment)
    {
        _project = project;
        _buildChapterCodeSegment = buildChapterCodeSegment;
        _nameBox = new TextBox
        {
            Header = "中文显示名称",
            Text = chapter?.Name ?? string.Empty,
            PlaceholderText = "例如：第一章 雨夜"
        };

        _typeBox = new ComboBox
        {
            Header = "章节类型",
            Width = 360
        };
        ComboBoxItem? selectedTypeItem = null;
        foreach (var chapterType in ChapterTypes.Options)
        {
            var item = new ComboBoxItem
            {
                Content = chapterType.DisplayName,
                Tag = chapterType
            };
            _typeBox.Items.Add(item);
            if (string.Equals(chapter?.Type, chapterType.Kind, StringComparison.OrdinalIgnoreCase))
            {
                selectedTypeItem = item;
            }
        }

        _typeBox.SelectedItem = selectedTypeItem ?? _typeBox.Items.FirstOrDefault();

        _customCodeBox = new TextBox
        {
            Header = "自定义代号 / 编号",
            Text = chapter is null ? string.Empty : getChapterCodeSegment(chapter.Code, project.Code),
            PlaceholderText = "主线/间章可留空；养成如 Kirito-3，活动如 AF，世界对话如 World1"
        };

        _previewText = DialogContentFactory.CreateSubtleParagraph(string.Empty);
        Content = new StackPanel
        {
            Spacing = 12
        };
        if (introContent is not null)
        {
            Content.Children.Add(introContent);
        }

        Content.Children.Add(_nameBox);
        Content.Children.Add(_typeBox);
        Content.Children.Add(_customCodeBox);
        Content.Children.Add(_previewText);

        _typeBox.SelectionChanged += (_, _) => UpdatePreview();
        _customCodeBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    public StackPanel Content { get; }

    public ChapterEditorInput ReadInput()
    {
        var name = _nameBox.Text.Trim();
        var selectedOption = (_typeBox.SelectedItem as ComboBoxItem)?.Tag as ChapterTypeOption ?? ChapterTypes.Options[0];
        var segmentCode = _buildChapterCodeSegment(selectedOption.Kind, _customCodeBox.Text.Trim());
        return new ChapterEditorInput(name, $"{_project.Code}-{segmentCode}", selectedOption.Kind);
    }

    public string ReadSegmentCode()
    {
        var selectedOption = (_typeBox.SelectedItem as ComboBoxItem)?.Tag as ChapterTypeOption ?? ChapterTypes.Options[0];
        return _buildChapterCodeSegment(selectedOption.Kind, _customCodeBox.Text.Trim());
    }

    private void UpdatePreview()
    {
        var selectedOption = (_typeBox.SelectedItem as ComboBoxItem)?.Tag as ChapterTypeOption ?? ChapterTypes.Options[0];
        var segment = _buildChapterCodeSegment(selectedOption.Kind, _customCodeBox.Text.Trim());
        _previewText.Text = $"生成代码：{_project.Code}-{segment}";
    }
}

internal sealed class FunctionEditorDialogContent
{
    private readonly TextBox _nameBox;
    private readonly TextBox _mainCodeBox;
    private readonly TextBox _descriptionBox;
    private readonly ToggleSwitch _hasBranchesToggle;
    private readonly StackPanel _branchesPanel;
    private readonly Button _addBranchButton;
    private readonly bool _isSystemFunction;
    private readonly Action _playPositiveSound;
    private readonly Action _playNegativeSound;

    public FunctionEditorDialogContent(
        FunctionEntry? function,
        string suggestedChoiceIndicator,
        Action playPositiveSound,
        Action playNegativeSound)
    {
        _playPositiveSound = playPositiveSound;
        _playNegativeSound = playNegativeSound;
        _isSystemFunction = function?.BranchMode == FunctionBranchMode.System;
        _nameBox = new TextBox
        {
            Width = 420,
            Header = "中文名称",
            Text = function?.Name ?? string.Empty,
            PlaceholderText = "例如：播放一次性特殊音效"
        };
        _mainCodeBox = new TextBox
        {
            Width = 420,
            Header = "主函数代号",
            Text = function?.Indicator ?? suggestedChoiceIndicator,
            PlaceholderText = "例如：VFXON",
            IsEnabled = !_isSystemFunction
        };
        _descriptionBox = new TextBox
        {
            Width = 420,
            Header = "介绍",
            Text = function?.Description ?? string.Empty,
            PlaceholderText = "简要说明这个函数会做什么",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxLength = 160
        };
        _hasBranchesToggle = new ToggleSwitch
        {
            Header = "分支",
            OffContent = "无分支",
            OnContent = "拥有分支",
            IsOn = function?.BranchMode is FunctionBranchMode.Fixed or FunctionBranchMode.AssetIndex ||
                   (function?.Branches.Count ?? 0) > 0,
            IsEnabled = !_isSystemFunction
        };
        _branchesPanel = new StackPanel { Spacing = 10 };
        foreach (var branch in function?.Branches ?? [])
        {
            AddBranchRow(branch);
        }
        _addBranchButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = "+ 新增分支",
            IsEnabled = !_isSystemFunction
        };
        _addBranchButton.Click += (_, _) =>
        {
            _playPositiveSound();
            AddBranchRow(new FunctionBranch(string.Empty, string.Empty));
        };
        _hasBranchesToggle.Toggled += (_, _) => UpdateBranchVisibility();

        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                _nameBox,
                _mainCodeBox,
                _descriptionBox,
                _hasBranchesToggle,
                DialogContentFactory.CreateResultHeading("分支列表"),
                new ScrollViewer
                {
                    MaxHeight = 300,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _branchesPanel
                },
                _addBranchButton,
                DialogContentFactory.CreateSubtleParagraph("实际代号会拼接到主函数之后并写入 Custom；备注只在工具箱中显示。")
            }
        };
        UpdateBranchVisibility();
    }

    public StackPanel Content { get; }

    public FunctionEditorInput ReadInput()
    {
        var name = _nameBox.Text.Trim();
        var mainCode = _mainCodeBox.Text.Trim();
        var description = _descriptionBox.Text.Trim();
        var branches = _branchesPanel.Children
            .OfType<Grid>()
            .Select(row => row.Children.OfType<TextBox>().ToList())
            .Where(boxes => boxes.Count >= 2 && !string.IsNullOrWhiteSpace(boxes[0].Text))
            .Select(boxes => new FunctionBranch(boxes[0].Text.Trim(), boxes[1].Text.Trim()))
            .ToList();
        var mode = _isSystemFunction
            ? FunctionBranchMode.System
            : !_hasBranchesToggle.IsOn
                ? FunctionBranchMode.None
                : FunctionBranchMode.Fixed;
        return new FunctionEditorInput(name, mainCode, description, mode, branches);
    }

    private void AddBranchRow(FunctionBranch branch)
    {
        var row = new Grid
        {
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var codeBox = new TextBox
        {
            Header = "实际代号",
            Text = branch.Code,
            PlaceholderText = "例如：Sunshine"
        };
        var remarkBox = new TextBox
        {
            Header = "备注",
            Text = branch.Remark,
            PlaceholderText = "例如：阳光特效"
        };
        var removeButton = new Button
        {
            Content = "删除",
            Margin = new Thickness(0, 28, 0, 0),
            IsEnabled = !_isSystemFunction
        };
        removeButton.Click += (_, _) =>
        {
            _playNegativeSound();
            _branchesPanel.Children.Remove(row);
        };

        Grid.SetColumn(remarkBox, 1);
        Grid.SetColumn(removeButton, 2);
        row.Children.Add(codeBox);
        row.Children.Add(remarkBox);
        row.Children.Add(removeButton);
        _branchesPanel.Children.Add(row);
    }

    private void UpdateBranchVisibility()
    {
        var visibility = _hasBranchesToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        _branchesPanel.Visibility = visibility;
        _addBranchButton.Visibility = visibility;
    }
}

internal sealed class CharacterEditorDialogContent
{
    private readonly TextBox _nameBox;
    private readonly TextBox _codeBox;
    private readonly TextBox _colorBox;

    public CharacterEditorDialogContent(CharacterInfo? character)
    {
        _nameBox = new TextBox
        {
            Header = "角色名字",
            Text = character?.Name ?? string.Empty,
            PlaceholderText = "例如：明绪"
        };
        _codeBox = new TextBox
        {
            Header = "英文代号",
            Text = character?.Code ?? string.Empty,
            PlaceholderText = "例如：Mio"
        };
        _colorBox = new TextBox
        {
            Header = "代表色",
            Text = character?.ColorHex ?? ColorUtility.DefaultCharacterColorHex,
            PlaceholderText = "#RRGGBB"
        };
        Content = new StackPanel
        {
            Spacing = 12,
            Children = { _nameBox, _codeBox, _colorBox }
        };
    }

    public StackPanel Content { get; }

    public CharacterEditorInput ReadInput()
    {
        return new CharacterEditorInput(
            _nameBox.Text.Trim(),
            _codeBox.Text.Trim(),
            ColorUtility.NormalizeColorHex(_colorBox.Text.Trim()));
    }
}
