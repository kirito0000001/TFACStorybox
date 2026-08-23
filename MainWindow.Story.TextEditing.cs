using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private List<StorySpeakerNameSuggestion> _storySpeakerNameSuggestions = [];
    private int _storySpeakerSuggestionOpenGeneration;

    private bool SaveCurrentStoryRow()
    {
        if (_isLoadingStoryRow || _currentStoryDocumentPath is null || _storyRows.Count == 0)
        {
            return false;
        }

        if (!_isStoryRowDirty)
        {
            return false;
        }

        var row = _storyRows[_currentStoryRowIndex];
        var changed = false;
        var speakerName = NormalizeStoryCharacterNameForCsv(_storyEditorViewModel.SpeakerText);
        if (!string.Equals(row.Get("TalkChar"), speakerName, StringComparison.Ordinal))
        {
            row.Set("TalkChar", speakerName);
            changed = true;
        }

        if (!string.Equals(row.Get("Tesxt"), _storyEditorViewModel.StoryText, StringComparison.Ordinal))
        {
            row.Set("Tesxt", _storyEditorViewModel.StoryText);
            changed = true;
        }

        changed |= NormalizeStoryDetachedCharacterLayers(row);
        if (changed)
        {
            QueueCurrentStoryDocumentSave();
            SaveCurrentChapterProgress();
            _ = RefreshStoryPreviewAsync();
        }

        _isStoryRowDirty = false;
        return changed;
    }

    private void ScheduleStoryRowSave()
    {
        if (_isLoadingStoryRow || _isStoryDebugModeEnabled || _storyEditSaveTimer is null)
        {
            return;
        }

        _isStoryRowDirty = true;
        _storyEditSaveTimer.Stop();
        _storyEditSaveTimer.Start();
    }

    private void StoryEditSaveTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        SaveCurrentStoryRow();
    }

    private void StorySpeakerTextBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_isLoadingStoryRow)
        {
            return;
        }

        if (args.Reason != AutoSuggestionBoxTextChangeReason.ProgrammaticChange &&
            !_storyTextEditUndoCaptured)
        {
            CaptureStoryRowUndoState("编辑当前说话人");
            _storyTextEditUndoCaptured = true;
        }
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            RefreshStorySpeakerNameSuggestions(sender);
        }

        ScheduleStoryRowSave();
    }

    private void StorySpeakerTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        StoryTextEditor_GotFocus(sender, e);
        if (sender is not AutoSuggestBox box)
        {
            return;
        }

        _storySpeakerNameSuggestions = BuildStorySpeakerNameSuggestions();
        RefreshStorySpeakerNameSuggestions(box);
        DispatcherQueue.TryEnqueue(() => box.IsSuggestionListOpen = true);
    }

    private List<StorySpeakerNameSuggestion> BuildStorySpeakerNameSuggestions()
    {
        return _storyRows
            .Select((row, index) => new
            {
                RawValue = row.Get("TalkChar").Trim(),
                Index = index
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.RawValue))
            .Select(item =>
            {
                var character = ResolveStoryCharacter(item.RawValue);
                return new
                {
                    Value = character?.Code ?? item.RawValue,
                    DisplayName = character?.Name ?? item.RawValue,
                    SecondaryText = character?.Code ?? "nullptr",
                    item.Index
                };
            })
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Item = group.First(),
                UsageCount = group.Count(),
                FirstIndex = group.Min(item => item.Index)
            })
            .OrderByDescending(item => item.UsageCount)
            .ThenBy(item => item.FirstIndex)
            .Select(item => new StorySpeakerNameSuggestion(
                item.Item.Value,
                item.Item.DisplayName,
                item.Item.SecondaryText,
                item.UsageCount,
                IsClearAction: false,
                IsCreateAction: false))
            .ToList();
    }

    private void RefreshStorySpeakerNameSuggestions(AutoSuggestBox box)
    {
        var query = box.Text.Trim();
        var filteredSuggestions = _storySpeakerNameSuggestions
            .Where(item => string.IsNullOrWhiteSpace(query) ||
                           item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.SecondaryText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var knownValues = new HashSet<string>(
                _storySpeakerNameSuggestions.Select(item => item.Value),
                StringComparer.OrdinalIgnoreCase);
            foreach (var character in GetStoryCharacters()
                         .Where(character =>
                             character.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             character.Code.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                if (!knownValues.Add(character.Code))
                {
                    continue;
                }

                filteredSuggestions.Add(new StorySpeakerNameSuggestion(
                    character.Code,
                    character.Name,
                    character.Code,
                    UsageCount: 0,
                    IsClearAction: false,
                    IsCreateAction: false));
            }
        }

        var suggestions = new List<StorySpeakerNameSuggestion>
        {
            new(
                query,
                "创建说话人",
                string.IsNullOrWhiteSpace(query) ? "直接采用输入" : $"使用“{query}”",
                UsageCount: null,
                IsClearAction: false,
                IsCreateAction: true)
        };
        suggestions.AddRange(filteredSuggestions
            .OrderBy(item => item.UsageCount ?? 0)
            .ThenByDescending(item => item.DisplayName, StringComparer.OrdinalIgnoreCase));
        suggestions.Add(new StorySpeakerNameSuggestion(
            string.Empty,
            "清空",
            "移除当前说话人",
            UsageCount: null,
            IsClearAction: true,
            IsCreateAction: false));
        box.ItemsSource = suggestions;
        box.IsSuggestionListOpen = true;
    }

    private void StorySpeakerTextBox_SuggestionListOpenChanged(
        DependencyObject sender,
        DependencyProperty property)
    {
        var generation = ++_storySpeakerSuggestionOpenGeneration;
        if (sender is not AutoSuggestBox { IsSuggestionListOpen: true } box)
        {
            return;
        }

        QueueStorySpeakerSuggestionListReset(box, generation, attempt: 0);
    }

    private void QueueStorySpeakerSuggestionListReset(
        AutoSuggestBox box,
        int generation,
        int attempt)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation != _storySpeakerSuggestionOpenGeneration || !box.IsSuggestionListOpen)
            {
                return;
            }

            var suggestionsList = VisualTreeHelper.GetOpenPopupsForXamlRoot(box.XamlRoot)
                .Select(popup => popup.Child)
                .Where(child => child is not null)
                .SelectMany(child => FindStorySpeakerSuggestionLists(child!))
                .FirstOrDefault(list => string.Equals(list.Name, "SuggestionsList", StringComparison.Ordinal));
            if (suggestionsList is null)
            {
                if (attempt < 4)
                {
                    QueueStorySpeakerSuggestionListReset(box, generation, attempt + 1);
                }
                return;
            }

            AlignStorySpeakerSuggestionListAfterLayout(box, suggestionsList, generation);
        });
    }

    private void AlignStorySpeakerSuggestionListAfterLayout(
        AutoSuggestBox box,
        ListView suggestionsList,
        int generation)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(32);
        timer.IsRepeating = false;
        EventHandler<object>? layoutUpdated = null;
        layoutUpdated = (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            suggestionsList.LayoutUpdated -= layoutUpdated;
            if (generation == _storySpeakerSuggestionOpenGeneration &&
                box.IsSuggestionListOpen &&
                suggestionsList.Items.Count > 0)
            {
                suggestionsList.ScrollIntoView(
                    suggestionsList.Items[0],
                    ScrollIntoViewAlignment.Leading);
            }
        };
        suggestionsList.LayoutUpdated += layoutUpdated;
        timer.Start();
    }

    private static IEnumerable<ListView> FindStorySpeakerSuggestionLists(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ListView listView)
            {
                yield return listView;
            }

            foreach (var descendant in FindStorySpeakerSuggestionLists(child))
            {
                yield return descendant;
            }
        }
    }

    private void StorySpeakerTextBox_SuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is StorySpeakerNameSuggestion suggestion)
        {
            sender.Text = suggestion.IsClearAction
                ? string.Empty
                : suggestion.IsCreateAction ? suggestion.Value : suggestion.DisplayName;
        }
    }

    private string GetStorySpeakerDisplayName(string characterValue)
    {
        var trimmed = characterValue.Trim();
        var character = ResolveStoryCharacter(trimmed);
        return string.IsNullOrWhiteSpace(character?.Name) ? trimmed : character.Name;
    }

    private sealed record StorySpeakerNameSuggestion(
        string Value,
        string DisplayName,
        string SecondaryText,
        int? UsageCount,
        bool IsClearAction,
        bool IsCreateAction);

    private void StoryTextTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleStoryRowSave();
    }

    private void StoryTextEditor_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_isStoryRowDirty)
        {
            _storyTextEditUndoCaptured = false;
        }
    }

    private void StoryTextEditor_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        if (_isLoadingStoryRow || _isStoryDebugModeEnabled || _storyTextEditUndoCaptured)
        {
            return;
        }

        CaptureStoryRowUndoState("编辑当前句文本");
        _storyTextEditUndoCaptured = true;
    }

    private void StoryTextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveCurrentStoryRow();
        _storyTextEditUndoCaptured = false;
    }
}
