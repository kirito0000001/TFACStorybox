using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace GalExcleTools.Services;

internal sealed class StoryAudioPlaybackService : IDisposable
{
    private readonly MediaPlayer _player = new();
    private int _requestId;

    public StoryAudioPlaybackService(bool looping)
    {
        _player.IsLoopingEnabled = looping;
    }

    public string? CurrentPath { get; private set; }

    public async Task<bool> PlayAsync(string path, bool restartCurrent = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Stop();
            return false;
        }

        if (!restartCurrent &&
            !string.IsNullOrWhiteSpace(CurrentPath) &&
            FileSystemUtility.PathsEqual(CurrentPath, path) &&
            _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            return false;
        }

        var requestId = ++_requestId;
        var file = await StorageFile.GetFileFromPathAsync(path);
        if (requestId != _requestId)
        {
            return false;
        }

        CurrentPath = path;
        _player.Source = MediaSource.CreateFromStorageFile(file);
        _player.Play();
        return true;
    }

    public void Pause()
    {
        _requestId++;
        _player.Pause();
    }

    public void Stop()
    {
        _requestId++;
        _player.Pause();
        _player.Source = null;
        CurrentPath = null;
    }

    public void Dispose()
    {
        Stop();
        _player.Dispose();
    }
}
