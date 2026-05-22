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
    private const string ChoiceFunctionCategory = "触发选项";
    private readonly TextBox _nameBox;
    private readonly TextBox _indicatorBox;
    private readonly TextBox _categoryBox;
    private readonly StackPanel _choiceNotesPanel;
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
        _nameBox = new TextBox
        {
            Width = 420,
            Header = "中文名称",
            Text = function?.Name ?? (string.IsNullOrWhiteSpace(suggestedChoiceIndicator) ? string.Empty : ChoiceFunctionCategory),
            PlaceholderText = "例如：播放一次性特殊音效"
        };
        _indicatorBox = new TextBox
        {
            Width = 420,
            Header = "函数指示器",
            Text = function?.Indicator ?? suggestedChoiceIndicator,
            PlaceholderText = "例如：Scene_、BGM_Stop、M2-04-Choice2"
        };
        _categoryBox = new TextBox
        {
            Width = 420,
            Header = "分类",
            Text = function?.Category ?? (string.IsNullOrWhiteSpace(suggestedChoiceIndicator) ? "自定义" : ChoiceFunctionCategory),
            PlaceholderText = "音频 / 背景 / 特效 / 动画 / 视频 / 标题 / 触发选项 / 自定义"
        };
        _choiceNotesPanel = new StackPanel
        {
            Spacing = 8
        };

        foreach (var note in function?.ChoiceNotes ?? [])
        {
            AddChoiceNoteRow(note);
        }

        var addChoiceNoteButton = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = "+"
        };
        addChoiceNoteButton.Click += (_, _) =>
        {
            _playPositiveSound();
            AddChoiceNoteRow();
        };

        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                _nameBox,
                _indicatorBox,
                _categoryBox,
                DialogContentFactory.CreateResultHeading("选项备注"),
                _choiceNotesPanel,
                addChoiceNoteButton,
                DialogContentFactory.CreateSubtleParagraph("备注只保存在函数卡里，方便查看选项内容，不会写入剧情表 Custom 字段。")
            }
        };
    }

    public StackPanel Content { get; }

    public FunctionEditorInput ReadInput()
    {
        var name = _nameBox.Text.Trim();
        var indicator = _indicatorBox.Text.Trim();
        var category = string.IsNullOrWhiteSpace(_categoryBox.Text) ? "自定义" : _categoryBox.Text.Trim();
        var choiceNotes = _choiceNotesPanel.Children
            .OfType<Grid>()
            .Select(row => row.Children.OfType<TextBox>().FirstOrDefault()?.Text)
            .Select(TextUtility.NormalizeFunctionChoiceNote)
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .ToList();

        return new FunctionEditorInput(name, indicator, category, choiceNotes);
    }

    private void AddChoiceNoteRow(string note = "")
    {
        var row = new Grid
        {
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var noteBox = new TextBox
        {
            Header = $"选项备注 {_choiceNotesPanel.Children.Count + 1}",
            Text = note,
            PlaceholderText = "仅用于查看，不写入剧情表"
        };
        var removeButton = new Button
        {
            Content = "删除",
            Margin = new Thickness(0, 28, 0, 0)
        };
        removeButton.Click += (_, _) =>
        {
            _playNegativeSound();
            _choiceNotesPanel.Children.Remove(row);
            RenumberChoiceNoteRows();
        };

        Grid.SetColumn(removeButton, 1);
        row.Children.Add(noteBox);
        row.Children.Add(removeButton);
        _choiceNotesPanel.Children.Add(row);
    }

    private void RenumberChoiceNoteRows()
    {
        var index = 1;
        foreach (var noteBox in _choiceNotesPanel.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<TextBox>()))
        {
            noteBox.Header = $"选项备注 {index}";
            index++;
        }
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
