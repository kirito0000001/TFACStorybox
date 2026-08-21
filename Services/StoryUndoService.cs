using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

internal sealed class StoryUndoService
{
    private const int MaxChangeCount = 120;
    private readonly List<StoryChangeSet> _undoStack = [];
    private readonly List<StoryChangeSet> _redoStack = [];
    private StoryDocumentSnapshot? _pendingSnapshot;
    private StoryRow? _pendingRowBefore;
    private string? _pendingDescription;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public StoryChangeSet? LastUndoChange => _undoStack.Count == 0 ? null : _undoStack[^1];

    public void Begin(
        StoryDocument document,
        int currentRowIndex,
        string description)
    {
        if (_pendingSnapshot is not null)
        {
            return;
        }

        _pendingSnapshot = document.CaptureSnapshot(currentRowIndex);
        _pendingDescription = description;
    }

    public void BeginRowEdit(StoryDocument document, int currentRowIndex, string description)
    {
        if (_pendingSnapshot is not null || _pendingRowBefore is not null || document.Rows.Count == 0)
        {
            return;
        }

        _pendingRowBefore = document.Rows[Math.Clamp(currentRowIndex, 0, document.Rows.Count - 1)].Clone();
        _pendingDescription = description;
    }

    public bool Commit(StoryDocument document, int currentRowIndex)
    {
        if (string.IsNullOrWhiteSpace(_pendingDescription))
        {
            return false;
        }

        StoryChangeSet change;
        if (_pendingRowBefore is not null)
        {
            var afterRow = document.Rows.FirstOrDefault(row =>
                string.Equals(row.StableId, _pendingRowBefore.StableId, System.StringComparison.OrdinalIgnoreCase));
            change = afterRow is null
                ? StoryDocument.CreateChangeSet(
                    _pendingDescription,
                    document.CaptureSnapshot(currentRowIndex),
                    document.CaptureSnapshot(currentRowIndex))
                : StoryDocument.CreateRowChangeSet(_pendingDescription, _pendingRowBefore, afterRow);
        }
        else if (_pendingSnapshot is not null)
        {
            var after = document.CaptureSnapshot(currentRowIndex);
            change = StoryDocument.CreateChangeSet(_pendingDescription, _pendingSnapshot, after);
        }
        else
        {
            return false;
        }

        _pendingSnapshot = null;
        _pendingRowBefore = null;
        _pendingDescription = null;
        if (change.IsEmpty)
        {
            return false;
        }

        _undoStack.Add(change);
        if (_undoStack.Count > MaxChangeCount)
        {
            _undoStack.RemoveAt(0);
        }

        _redoStack.Clear();
        return true;
    }

    public void CancelPending()
    {
        _pendingSnapshot = null;
        _pendingRowBefore = null;
        _pendingDescription = null;
    }

    public bool TryUndo(
        StoryDocument document,
        int currentRowIndex,
        out StoryChangeApplication? application)
    {
        application = null;
        if (_undoStack.Count == 0)
        {
            return false;
        }

        var change = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        application = document.ApplyChange(change, useBefore: true, currentRowIndex);
        _redoStack.Add(change);
        return true;
    }

    public bool TryRedo(
        StoryDocument document,
        int currentRowIndex,
        out StoryChangeApplication? application)
    {
        application = null;
        if (_redoStack.Count == 0)
        {
            return false;
        }

        var change = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        application = document.ApplyChange(change, useBefore: false, currentRowIndex);
        _undoStack.Add(change);
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        CancelPending();
    }
}
