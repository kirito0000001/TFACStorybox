using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<StoryChoiceEditorOptionViewModel> _storyChoiceEditorOptions = [];
    private IReadOnlyList<StoryChoiceTargetOption> _storyChoiceEditorTargets = [];
    private string _storyChoiceEditorChoiceIndicator = string.Empty;
    private TaskCompletionSource<StoryChoiceEditorResult?>? _storyChoiceEditorCompletion;
    private bool _isUpdatingStoryChoiceEditor;

    private Task<StoryChoiceEditorResult?> ShowStoryChoiceEditorAsync(
        string choiceIndicator,
        IReadOnlyList<StoryChoiceTargetOption> targetOptions,
        IReadOnlyList<StoryChoiceEditorOption>? initialOptions = null)
    {
        CompleteStoryChoiceEditor(null);
        _storyChoiceEditorChoiceIndicator = choiceIndicator;
        _storyChoiceEditorTargets = targetOptions;
        _storyChoiceEditorOptions.Clear();
        var sourceOptions = initialOptions is { Count: > 0 }
            ? initialOptions
            : [new StoryChoiceEditorOption(string.Empty, string.Empty)];
        for (var index = 0; index < sourceOptions.Count; index++)
        {
            var option = sourceOptions[index];
            _storyChoiceEditorOptions.Add(new StoryChoiceEditorOptionViewModel(
                StoryBranchService.GetOptionKey(index),
                option.Text,
                option.TargetSectionFirstRowId,
                ResolveStoryChoiceTargetDisplay(option.TargetSectionFirstRowId)));
        }

        StoryChoiceEditorCodeText.Text = choiceIndicator;
        StoryChoiceEditorListView.ItemsSource = _storyChoiceEditorOptions;
        PopulateStoryChoiceTargetOptions();
        StoryChoiceEditorListView.SelectedIndex = 0;
        RefreshStoryChoiceEditorSummary();
        StoryChoiceEditorHost.Visibility = Visibility.Visible;
        StoryChoiceEditorHost.Opacity = 1;
        StoryChoiceEditorHost.Focus(FocusState.Programmatic);
        StoryChoiceEditorListView.Focus(FocusState.Programmatic);
        _storyChoiceEditorCompletion = new TaskCompletionSource<StoryChoiceEditorResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return _storyChoiceEditorCompletion.Task;
    }

    private void PopulateStoryChoiceTargetOptions()
    {
        _isUpdatingStoryChoiceEditor = true;
        try
        {
            StoryChoiceEditorTargetComboBox.Items.Clear();
            StoryChoiceEditorTargetComboBox.Header = $"前往小节（{_storyChoiceEditorTargets.Count} 个已创建）";
            StoryChoiceEditorTargetComboBox.Items.Add(new ComboBoxItem
            {
                Content = "不跳转",
                Tag = string.Empty
            });
            foreach (var target in _storyChoiceEditorTargets)
            {
                StoryChoiceEditorTargetComboBox.Items.Add(new ComboBoxItem
                {
                    Content = CreateStoryChoiceTargetContent(target),
                    Tag = target.TargetSectionFirstRowId
                });
            }
        }
        finally
        {
            _isUpdatingStoryChoiceEditor = false;
        }
        RefreshStoryChoiceTargetBindingIndicators();
    }

    private void StoryChoiceEditorListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingStoryChoiceEditor)
        {
            return;
        }

        LoadSelectedStoryChoiceOption();
    }

    private void LoadSelectedStoryChoiceOption()
    {
        _isUpdatingStoryChoiceEditor = true;
        try
        {
            if (StoryChoiceEditorListView.SelectedItem is not StoryChoiceEditorOptionViewModel option)
            {
                StoryChoiceEditorDetailTitle.Text = "未选择选项";
                StoryChoiceEditorTextBox.Text = string.Empty;
                StoryChoiceEditorTextBox.IsEnabled = false;
                StoryChoiceEditorTargetComboBox.IsEnabled = false;
                StoryChoiceEditorDeleteButton.IsEnabled = false;
                return;
            }

            StoryChoiceEditorDetailTitle.Text = $"选项 {option.OptionKey}";
            StoryChoiceEditorTextBox.IsEnabled = true;
            StoryChoiceEditorTargetComboBox.IsEnabled = true;
            StoryChoiceEditorDeleteButton.IsEnabled = true;
            StoryChoiceEditorTextBox.Text = option.Text;
            SelectStoryChoiceTarget(option.TargetSectionFirstRowId);
        }
        finally
        {
            _isUpdatingStoryChoiceEditor = false;
        }
    }

    private void SelectStoryChoiceTarget(string targetId)
    {
        StoryChoiceEditorTargetComboBox.SelectedIndex = 0;
        for (var index = 0; index < StoryChoiceEditorTargetComboBox.Items.Count; index++)
        {
            if (StoryChoiceEditorTargetComboBox.Items[index] is ComboBoxItem item &&
                string.Equals(item.Tag as string, targetId, StringComparison.OrdinalIgnoreCase))
            {
                StoryChoiceEditorTargetComboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private void StoryChoiceEditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingStoryChoiceEditor ||
            StoryChoiceEditorListView.SelectedItem is not StoryChoiceEditorOptionViewModel option)
        {
            return;
        }

        option.Text = StoryChoiceEditorTextBox.Text;
    }

    private void StoryChoiceEditorTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingStoryChoiceEditor ||
            StoryChoiceEditorListView.SelectedItem is not StoryChoiceEditorOptionViewModel option ||
            StoryChoiceEditorTargetComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        option.TargetSectionFirstRowId = item.Tag as string ?? string.Empty;
        option.TargetDisplayName = ResolveStoryChoiceTargetDisplay(option.TargetSectionFirstRowId);
        RefreshStoryChoiceTargetBindingIndicators();
        RefreshStoryChoiceEditorSummary();
    }

    private void StoryChoiceEditorAddButton_Click(object sender, RoutedEventArgs e)
    {
        PlayPositiveSound();
        var option = new StoryChoiceEditorOptionViewModel(
            StoryBranchService.GetOptionKey(_storyChoiceEditorOptions.Count),
            string.Empty,
            string.Empty,
            "不跳转");
        _storyChoiceEditorOptions.Add(option);
        StoryChoiceEditorListView.SelectedItem = option;
        RefreshStoryChoiceTargetBindingIndicators();
        RefreshStoryChoiceEditorSummary();
        StoryChoiceEditorTextBox.Focus(FocusState.Programmatic);
    }

    private void StoryChoiceEditorDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (StoryChoiceEditorListView.SelectedItem is not StoryChoiceEditorOptionViewModel option)
        {
            return;
        }

        PlayNegativeSound();
        var removedIndex = _storyChoiceEditorOptions.IndexOf(option);
        _storyChoiceEditorOptions.Remove(option);
        if (_storyChoiceEditorOptions.Count == 0)
        {
            _storyChoiceEditorOptions.Add(new StoryChoiceEditorOptionViewModel("A", string.Empty, string.Empty, "不跳转"));
        }
        RenumberStoryChoiceEditorOptions();
        StoryChoiceEditorListView.SelectedIndex = Math.Clamp(removedIndex, 0, _storyChoiceEditorOptions.Count - 1);
        RefreshStoryChoiceTargetBindingIndicators();
        RefreshStoryChoiceEditorSummary();
    }

    private void RenumberStoryChoiceEditorOptions()
    {
        for (var index = 0; index < _storyChoiceEditorOptions.Count; index++)
        {
            _storyChoiceEditorOptions[index].OptionKey = StoryBranchService.GetOptionKey(index);
        }
    }

    private void StoryChoiceEditorConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        PlayPositiveSound();
        CompleteStoryChoiceEditor(new StoryChoiceEditorResult(
            _storyChoiceEditorOptions
                .Select(option => new StoryChoiceEditorOption(option.Text, option.TargetSectionFirstRowId))
                .ToList()));
    }

    private void StoryChoiceEditorCancelButton_Click(object sender, RoutedEventArgs e)
    {
        PlayNegativeSound();
        CompleteStoryChoiceEditor(null);
    }

    private void StoryChoiceEditorHost_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CompleteStoryChoiceEditor(null);
            return;
        }
        if (e.Key == VirtualKey.Enter &&
            FocusManager.GetFocusedElement(StoryChoiceEditorHost.XamlRoot) is not ComboBox)
        {
            e.Handled = true;
            StoryChoiceEditorConfirmButton_Click(sender, e);
        }
    }

    private void StoryChoiceEditorHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        PlayNegativeSound();
        CompleteStoryChoiceEditor(null);
    }

    private void StoryChoiceEditorHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, StoryChoiceEditorHost))
        {
            e.Handled = true;
            PlayNegativeSound();
            CompleteStoryChoiceEditor(null);
        }
    }

    private void StoryChoiceEditorCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void CompleteStoryChoiceEditor(StoryChoiceEditorResult? result)
    {
        var completion = _storyChoiceEditorCompletion;
        _storyChoiceEditorCompletion = null;
        if (StoryChoiceEditorHost is not null)
        {
            StoryChoiceEditorHost.Visibility = Visibility.Collapsed;
        }
        completion?.TrySetResult(result);
    }

    private void RefreshStoryChoiceEditorSummary()
    {
        var targetCount = _storyChoiceEditorOptions.Count(option =>
            !string.IsNullOrWhiteSpace(option.TargetSectionFirstRowId));
        StoryChoiceEditorSummaryText.Text = $"{_storyChoiceEditorOptions.Count} 个选项 · {targetCount} 个分支目标";
    }

    private string ResolveStoryChoiceTargetDisplay(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return "不跳转";
        }

        return _storyChoiceEditorTargets.FirstOrDefault(target => string.Equals(
                   target.TargetSectionFirstRowId,
                   targetId,
                   StringComparison.OrdinalIgnoreCase))?.DisplayName
               ?? "目标小节已不存在";
    }

    private FrameworkElement CreateStoryChoiceTargetContent(StoryChoiceTargetOption target)
    {
        var preview = string.IsNullOrWhiteSpace(target.FirstSentencePreview)
            ? "小节暂无文本"
            : target.FirstSentencePreview;
        var marker = new Ellipse
        {
            Width = 7,
            Height = 7,
            Tag = target.TargetSectionFirstRowId,
            Fill = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = target.DisplayName,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = preview,
                    FontSize = 12,
                    Opacity = 0.65,
                    MaxWidth = 440,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                }
            }
        };
        var status = new TextBlock
        {
            Text = "已绑定",
            Tag = "BoundStatus",
            FontSize = 12,
            Foreground = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var content = new Grid
        {
            MinWidth = 500,
            ColumnSpacing = 10
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(status, 2);
        content.Children.Add(marker);
        content.Children.Add(text);
        content.Children.Add(status);
        ToolTipService.SetToolTip(content, preview);
        return content;
    }

    private void RefreshStoryChoiceTargetBindingIndicators()
    {
        var boundTargetIds = new HashSet<string>(
            _storyEditorViewModel.Document.BranchRoutes
                .Where(route => !string.Equals(
                    route.ChoiceFunction,
                    _storyChoiceEditorChoiceIndicator,
                    StringComparison.OrdinalIgnoreCase))
                .Select(route => route.TargetSectionFirstRowId)
                .Where(targetId => !string.IsNullOrWhiteSpace(targetId)),
            StringComparer.OrdinalIgnoreCase);
        boundTargetIds.UnionWith(
            _storyChoiceEditorOptions
                .Select(option => option.TargetSectionFirstRowId)
                .Where(targetId => !string.IsNullOrWhiteSpace(targetId)));
        StoryChoiceEditorTargetComboBox.Header =
            $"前往小节（{_storyChoiceEditorTargets.Count} 个已创建，{boundTargetIds.Count} 个已绑定）";
        foreach (var item in StoryChoiceEditorTargetComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Content is not Grid content || item.Tag is not string targetId)
            {
                continue;
            }

            var isBound = boundTargetIds.Contains(targetId);
            var marker = content.Children.OfType<Ellipse>().FirstOrDefault();
            var status = content.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => string.Equals(text.Tag as string, "BoundStatus", StringComparison.Ordinal));
            if (marker is not null)
            {
                marker.Visibility = isBound ? Visibility.Visible : Visibility.Collapsed;
            }
            if (status is not null)
            {
                status.Visibility = isBound ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
