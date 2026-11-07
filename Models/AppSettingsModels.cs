using System;

namespace GalExcleTools;

/// <summary>
/// 「音频合成器」的通道音量（0~100，百分比）。
///
/// 口径对齐商业 GAL 的设置页（2026-11-07 参考《ライムライト・レモネード・ジャム》）：
/// 每个通道都是**独立百分比**，实际音量 = 主音量 × 通道音量。
/// 默认值也照抄他们：语音 100、BGM 50、SE（系统）50、影像 70。
/// </summary>
internal sealed class VoiceMixerSettings
{
    public double Master { get; set; } = 100;

    public double Voice { get; set; } = 100;

    public double Bgm { get; set; } = 50;

    /// <summary>语音播放时 BGM 降到的音量（游戏里的 ducking）。</summary>
    public double BgmDuringVoice { get; set; } = 50;

    /// <summary>勾上 = 语音播放时**不**降低 BGM（对应设置页那句「不降低BGM音量」）。</summary>
    public bool KeepBgmDuringVoice { get; set; }

    public double SceneSe { get; set; } = 80;

    public double SystemSe { get; set; } = 50;

    public double SystemVoice { get; set; } = 100;

    public double Movie { get; set; } = 70;

    public double Performance { get; set; } = 100;

    public double BgvNormal { get; set; } = 70;

    public double BgvScene { get; set; } = 70;
}

internal sealed class AppSettings
{
    public string? ProjectRootPath { get; set; }

    public bool NightModeEnabled { get; set; }

    public bool ShowWorkspacePath { get; set; } = true;

    public bool LogEnabled { get; set; } = true;

    public bool LogUserOperations { get; set; } = true;

    public bool LogWarnings { get; set; } = true;

    public bool LogErrors { get; set; } = true;

    public bool UiSoundEnabled { get; set; }

    /// <summary>音频合成器各通道音量；老存档没有这一段时用默认值。</summary>
    public VoiceMixerSettings Mixer { get; set; } = new();

    public double AssetLibraryScrollSpeedMultiplier { get; set; } = 1.5;

    public double StoryTextFontSize { get; set; } = 20;

    public bool ShowFullStoryChapterLength { get; set; }

    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    public bool UpdateAutoCheckEnabled { get; set; } = true;

    public int UpdateConnectionTimeoutSeconds { get; set; } = 60;

    public DateTimeOffset? UpdateLastCheckAt { get; set; }

    public string? UpdateLastStatus { get; set; }

    public string? UnrealEnginePath { get; set; }

    public string? UnrealProjectPath { get; set; }

    public string? UnrealContentFolderPath { get; set; }

    public string? UnrealToolProjectFolderName { get; set; }

    /// <summary>
    /// 语音规整用的 ffmpeg.exe。留空 = 用随包副本（<c>Tools\Audio\ffmpeg\ffmpeg.exe</c>）。
    ///
    /// 配过就**只认配的那个**、不偷偷回落到随包版本 —— 否则用户「明明改了路径却没生效」，
    /// 而且不知道为什么。解析顺序的唯一实现在 <c>Services/Audio/FfmpegLocator</c>。
    /// </summary>
    public string? FFmpegPath { get; set; }
}

internal sealed class ProjectMeta
{
    public string? ProjectName { get; set; }

    public string? ProjectCode { get; set; }

    public string? ThumbnailFileName { get; set; }

    public string? AssetLibraryName { get; set; }

    public string? AssetLibraryFolderName { get; set; }

    public string? UnrealEnginePath { get; set; }

    public string? UnrealProjectPath { get; set; }

    public string? UnrealContentFolderPath { get; set; }

    public DateTime LastEditedAt { get; set; }
}

internal sealed class ChapterMeta
{
    public string? ChapterName { get; set; }

    public string? ChapterCode { get; set; }

    public string? ChapterType { get; set; }

    public DateTime LastEditedAt { get; set; }

    public int LastEditedRowIndex { get; set; }
}
