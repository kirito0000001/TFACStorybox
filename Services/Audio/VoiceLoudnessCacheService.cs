using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GalExcleTools.Services;

/// <summary>
/// 章节级响度缓存的读写。**这是 DB 列「常驻」的那一层**：量过的数字落一个 json，
/// 下次进页面先读它，不重复跑 ffmpeg。
///
/// 三条纪律：
/// · <b>缓存是可丢弃的派生数据</b>：读坏了就当没有（返回 null），绝不抛给界面 ——
///   为了一个缓存把语音页打不开是本末倒置；
/// · <b>新鲜度由调用方判</b>（`VoiceLoudnessCacheEntry.Matches`），这一层只负责存和取；
/// · <b>写要原子</b>：走 <see cref="AtomicFileWriter"/>，避免写一半断电留下半截 json，
///   那种文件会让「下次读」永远解析失败。
/// </summary>
internal sealed class VoiceLoudnessCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 读某一章的缓存。文件不存在、解析失败、版本或口径对不上，一律返回 null
    /// —— 调用方据此「全部重测」，而不是拿着过期结论去染色。
    /// </summary>
    public VoiceLoudnessCacheDocument? Read(ProjectInfo project, string chapterCode, double targetLufs)
    {
        var path = WorkspacePathUtility.GetProjectVoiceLoudnessCachePath(project, chapterCode);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<VoiceLoudnessCacheDocument>(json, JsonOptions);
            if (document is null || !document.IsUsableFor(targetLufs))
            {
                return null;
            }

            return document;
        }
        catch (Exception error) when (error is IOException or JsonException or NotSupportedException)
        {
            // 缓存坏了不是错误，是「这次重新量」。留一行日志是为了别让人以为缓存机制没生效。
            ToolboxLog.Warn($"语音响度缓存读取失败，本次重新测量：{path}", error);
            return null;
        }
    }

    /// <summary>写某一章的缓存（整份覆盖）。失败只记日志：缓存写不进去不该影响已经显示的数字。</summary>
    public void Write(
        ProjectInfo project,
        string chapterCode,
        double targetLufs,
        IEnumerable<VoiceLoudnessCacheEntry> entries)
    {
        var path = WorkspacePathUtility.GetProjectVoiceLoudnessCachePath(project, chapterCode);
        var document = new VoiceLoudnessCacheDocument
        {
            Version = VoiceLoudnessCacheDocument.CurrentVersion,
            ChapterCode = chapterCode,
            TargetLufs = targetLufs,
            UpdatedAt = DateTimeOffset.Now,
            Entries = [.. entries]
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ToolboxLog.Warn($"语音响度缓存写入失败（不影响本次显示）：{path}", error);
        }
    }
}
