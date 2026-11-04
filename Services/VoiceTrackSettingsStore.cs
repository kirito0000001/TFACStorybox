using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GalExcleTools.Services;

/// <summary>
/// 语音音轨相关的项目级设置（<c>{项目}/Tools/voice.settings.json</c>）。
///
/// 【为什么单独一个文件，而不是塞进 story.voice-map.json】
/// 映射表是"哪一行对应哪个文件"的事实来源，改它的结构风险最大（一坏就丢语音）。
/// 音轨这类**偏好设置**另存一份，坏了顶多回到默认值，不会伤到映射。
/// </summary>
internal sealed class VoiceTrackSettings
{
    public const int MinTrackCount = 1;
    public const int MaxTrackCount = 8;

    /// <summary>这个项目有几条音轨（多国语音用）。默认 1。</summary>
    public int TrackCount { get; set; } = 1;

    /// <summary>当前正在编辑/试听哪一条音轨，1 起。</summary>
    public int ActiveTrack { get; set; } = 1;

    /// <summary>
    /// 「同步播放」：多音轨时，点试听是否把各音轨**同时**播出来。
    /// 用于"同一句台词要叠多条音轨"的场景。
    /// </summary>
    public bool SyncPlayback { get; set; }

    /// <summary>
    /// 有语音的说话人（角色名，来自 CSV 的 TalkChar）。
    ///
    /// 勾上的角色，其台词行左侧会出现一条角色专属颜色的竖线 —— 用来一眼区分
    /// "这句是谁说的、有没有配音"，比逐行看语音列快得多。
    /// </summary>
    public List<string> SpeakersWithVoice { get; set; } = [];

    public VoiceTrackSettings Normalize()
    {
        TrackCount = Math.Clamp(TrackCount, MinTrackCount, MaxTrackCount);
        ActiveTrack = Math.Clamp(ActiveTrack, MinTrackCount, TrackCount);
        SpeakersWithVoice = (SpeakersWithVoice ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return this;
    }
}

internal sealed class VoiceTrackSettingsStore
{
    private const string FileName = "voice.settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public VoiceTrackSettings Read(ProjectInfo project)
    {
        var path = GetPath(project);
        if (!File.Exists(path))
        {
            return new VoiceTrackSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<VoiceTrackSettings>(File.ReadAllText(path), JsonOptions);
            // 读坏了就回默认 —— 这是偏好设置，不值得为它弹窗或挡住页面。
            return (settings ?? new VoiceTrackSettings()).Normalize();
        }
        catch (Exception error) when (error is IOException or JsonException or NotSupportedException)
        {
            ToolboxLog.Warn($"音轨设置读取失败，按默认值处理：{path}", error);
            return new VoiceTrackSettings();
        }
    }

    public void Write(ProjectInfo project, VoiceTrackSettings settings)
    {
        var path = GetPath(project);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"音轨设置保存失败：{path}", error);
        }
    }

    private static string GetPath(ProjectInfo project) =>
        Path.Combine(project.Path, WorkspacePathUtility.ToolsFolderName, FileName);
}
