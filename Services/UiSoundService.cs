using System;
using System.Collections.Generic;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace GalExcleTools.Services;

internal sealed class UiSoundService : IUiSoundService, IDisposable
{
    private const string PositiveSoundPath = "D:\\INput\uFF08\u8FDB\u5165\u4E00\u4E2A\u754C\u9762\uFF09.wav";
    private const string NegativeSoundPath = "D:\\OUTput\uFF08\u9000\u51FA\u4E00\u4E2A\u754C\u9762\uFF09.wav";
    private const string SelectionSoundPath = "D:\\ListSel\uFF08\u5217\u8868\u9009\u62E9\uFF09.wav";

    private readonly Dictionary<UiSoundKind, MediaPlayer> _players = new();

    public bool IsEnabled { get; set; } = true;

    public void Play(UiSoundKind kind)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var player = GetOrCreatePlayer(kind);
            if (player is null)
            {
                return;
            }

            player.PlaybackSession.Position = TimeSpan.Zero;
            player.Play();
        }
        catch
        {
            // Sound feedback should never interrupt the main workflow.
        }
    }

    public void Dispose()
    {
        foreach (var player in _players.Values)
        {
            player.Dispose();
        }

        _players.Clear();
    }

    private MediaPlayer? GetOrCreatePlayer(UiSoundKind kind)
    {
        if (_players.TryGetValue(kind, out var existingPlayer))
        {
            return existingPlayer;
        }

        var path = GetPath(kind);
        if (!File.Exists(path))
        {
            return null;
        }

        var player = new MediaPlayer
        {
            AutoPlay = false,
            Source = MediaSource.CreateFromUri(new Uri(path))
        };
        _players[kind] = player;
        return player;
    }

    private static string GetPath(UiSoundKind kind)
    {
        return kind switch
        {
            UiSoundKind.Positive => PositiveSoundPath,
            UiSoundKind.Negative => NegativeSoundPath,
            UiSoundKind.Selection => SelectionSoundPath,
            _ => SelectionSoundPath
        };
    }
}
