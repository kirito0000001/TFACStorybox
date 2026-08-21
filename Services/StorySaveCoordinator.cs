using System;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

internal sealed class StorySaveCoordinator : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(900);
    private readonly StoryDocumentRepository _repository;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _scheduledCancellation;
    private Task _scheduledSave = Task.CompletedTask;
    private ChapterInfo? _pendingChapter;
    private StoryDocumentFile? _pendingState;

    public StorySaveCoordinator(StoryDocumentRepository repository)
    {
        _repository = repository;
    }

    public event EventHandler<Exception>? SaveFailed;

    public event Action<ChapterInfo, DateTimeOffset>? SaveCompleted;

    public void QueueSave(ChapterInfo chapter, StoryDocument document)
    {
        var state = _repository.CreateFile(chapter, document);
        lock (_sync)
        {
            _pendingChapter = chapter;
            _pendingState = state;
            _scheduledCancellation?.Cancel();
            _scheduledCancellation?.Dispose();
            _scheduledCancellation = new CancellationTokenSource();
            _scheduledSave = SaveAfterDelayAsync(chapter, state, _scheduledCancellation.Token);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ChapterInfo? chapter;
        StoryDocumentFile? state;
        lock (_sync)
        {
            _scheduledCancellation?.Cancel();
            chapter = _pendingChapter;
            state = _pendingState;
            _pendingChapter = null;
            _pendingState = null;
        }

        if (chapter is not null && state is not null)
        {
            await SaveNowAsync(chapter, state, cancellationToken).ConfigureAwait(false);
        }

        Task scheduled;
        lock (_sync)
        {
            scheduled = _scheduledSave;
        }

        try
        {
            await scheduled.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveAfterDelayAsync(
        ChapterInfo chapter,
        StoryDocumentFile state,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SaveDelay, cancellationToken).ConfigureAwait(false);
            await SaveNowAsync(chapter, state, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (ReferenceEquals(_pendingState, state))
                {
                    _pendingChapter = null;
                    _pendingState = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(this, ex);
        }
    }

    private async Task SaveNowAsync(
        ChapterInfo chapter,
        StoryDocumentFile state,
        CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _repository.SaveFileAsync(chapter, state, cancellationToken).ConfigureAwait(false);
            SaveCompleted?.Invoke(chapter, DateTimeOffset.Now);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        _scheduledCancellation?.Cancel();
        _scheduledCancellation?.Dispose();
        _saveGate.Dispose();
    }
}
