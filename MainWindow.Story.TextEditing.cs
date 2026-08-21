using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools;

public sealed partial class MainWindow
{
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

    private void StorySpeakerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleStoryRowSave();
    }

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
