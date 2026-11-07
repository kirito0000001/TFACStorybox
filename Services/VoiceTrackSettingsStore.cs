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
    /// 拖入语音后自动试听一次。
    ///
    /// 默认开：拖进来的素材十有八九是要立刻确认"这条对不对"，
    /// 播一遍能顺手验掉装错行、录坏、静音这类问题。
    /// </summary>
    public bool PlayAfterDropImport { get; set; } = true;

    /// <summary>
    /// 规整时是否先跑一遍 UVR 降噪。
    ///
    /// 默认开：这批素材的底噪不上 UVR 基本去不掉（ffmpeg 与其它模型都试过）。
    /// 留开关是因为它**很慢**（一条 1~2 分钟），赶时间或素材本来就干净时可以关掉。
    /// </summary>
    public bool UseUvrDenoise { get; set; } = true;

    /// <summary>
    /// 规整末尾是否用 SoundCraft 抢救"ffmpeg 到不了目标"的素材。
    ///
    /// 默认开：ffmpeg 的 loudnorm 只有弱限幅，峰均比高的素材到不了 -12 LUFS。
    /// 但它会串 去齿音 + 大增益 + Maximizer 三级，用户 2026-11-07 实测听感发闷，
    /// 所以留一个能一键关掉的开关（关掉后那批素材会停在 -14 左右）。
    /// </summary>
    public bool UseSoundCraftRescue { get; set; } = true;

    /// <summary>
    /// 每章"有语音"的说话人（角色名，来自 CSV 的 TalkChar）。
    ///
    /// ⚠️ **必须按章节分开**：同一个名字在不同章可能指的不是同一个人，
    /// 全局共用一份会把第二章的"？"当成第一章的"？"（2026-11-04 用户实测）。
    ///
    /// 勾上的角色，其台词行左侧会出现一条角色专属颜色的竖线。
    /// </summary>
    public Dictionary<string, List<string>> ChapterSpeakers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取某一章勾选过的说话人键。</summary>
    public IReadOnlyList<string> GetSpeakers(string chapterCode) =>
        ChapterSpeakers is not null && ChapterSpeakers.TryGetValue(chapterCode, out var list)
            ? list
            : [];

    /// <summary>覆盖某一章的勾选结果（空集合=清空这一章）。</summary>
    public void SetSpeakers(string chapterCode, IEnumerable<string> speakers)
    {
        // JSON 里可能是 null（手改坏了 / 旧版本），这里兜一下再写。
        ChapterSpeakers ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var normalized = speakers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            ChapterSpeakers.Remove(chapterCode);
            return;
        }

        ChapterSpeakers[chapterCode] = normalized;
    }

    public VoiceTrackSettings Normalize()
    {
        TrackCount = Math.Clamp(TrackCount, MinTrackCount, MaxTrackCount);
        ActiveTrack = Math.Clamp(ActiveTrack, MinTrackCount, TrackCount);
        var cleaned = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ChapterSpeakers ?? new Dictionary<string, List<string>>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var speakers = (pair.Value ?? new List<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (speakers.Count > 0)
            {
                cleaned[pair.Key.Trim()] = speakers;
            }
        }

        ChapterSpeakers = cleaned;
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
