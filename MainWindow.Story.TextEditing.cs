using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private List<StorySpeakerNameSuggestion> _storySpeakerNameSuggestions = [];

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
                Name = row.Get("TalkChar").Trim(),
                Index = index
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.First().Name,
                UsageCount = group.Count(),
                FirstIndex = group.Min(item => item.Index)
            })
            .OrderByDescending(item => item.UsageCount)
            .ThenBy(item => item.FirstIndex)
            .Select(item => new StorySpeakerNameSuggestion(
                item.Name,
                item.Name,
                $"本章使用 {item.UsageCount} 次",
                IsCreateAction: false))
            .ToList();
    }

    private void RefreshStorySpeakerNameSuggestions(AutoSuggestBox box)
    {
        var query = box.Text.Trim();
        var suggestions = _storySpeakerNameSuggestions
            .Where(item => string.IsNullOrWhiteSpace(query) ||
                           item.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        suggestions.Add(new StorySpeakerNameSuggestion(
            query,
            "创建说话人",
            string.IsNullOrWhiteSpace(query) ? "输入名称后直接采用" : $"使用“{query}”",
            IsCreateAction: true));
        box.ItemsSource = suggestions;
        box.IsSuggestionListOpen = true;
    }

    private void StorySpeakerTextBox_SuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is StorySpeakerNameSuggestion suggestion)
        {
            sender.Text = suggestion.Value;
        }
    }

    private sealed record StorySpeakerNameSuggestion(
        string Value,
        string DisplayName,
        string SecondaryText,
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
