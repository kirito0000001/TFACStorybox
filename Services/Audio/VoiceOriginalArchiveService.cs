using System;
using System.Collections.Generic;
using System.IO;

namespace GalExcleTools.Services;

/// <summary>
/// 语音的「原始版」档案：每个工作文件旁边（其实在同项目的 Tools 下）留**一份**未经处理的副本，
/// 规整多少次都只覆盖工作版，原始版永远不动。
///
/// 【为什么要它】
/// 上一版是「每次规整都把整章复制到 <c>Tools/AudioBackups/{时间戳}_{章节}</c>」——
/// 规整十次就是十份整章，语音库几十兆到几百兆，很快就没法看。
/// 而用户真正要的只有**最初那一版**（其它中间版本没有价值），所以改成固定路径的单份副本。
///
/// 【语义】（2026-11-02 定）
/// · 原始版 = 该行语音**最近一次通过界面导入**时的原文件；
/// · 「规整语音」只改工作版，**永不碰原始版**；
/// · 存量文件（早就导进来、那时还没有原始版机制）在第一次规整前**补存当前状态** ——
///   否则第一次规整就把「最初版」永久覆盖掉了。
///
/// 【为什么不放在 Voice 目录里】
/// Voice 的枚举是 AllDirectories，放进去会被当成一条语音：一起检查、一起规整、一起同步进 Unreal。
/// 所以它落在 <c>Tools/VoiceOriginals/{章节}</c>，那是工具箱私有区。
/// </summary>
internal sealed class VoiceOriginalArchiveService
{
    /// <summary>原始版的路径（工作文件名原样沿用，一个工作文件对应一个原始文件）。</summary>
    public string GetOriginalPath(ProjectInfo project, string chapterCode, string workPath)
    {
        return Path.Combine(
            WorkspacePathUtility.GetProjectVoiceOriginalsFolderPath(project, chapterCode),
            Path.GetFileName(workPath));
    }

    public bool HasOriginal(ProjectInfo project, string chapterCode, string workPath)
    {
        return File.Exists(GetOriginalPath(project, chapterCode, workPath));
    }

    /// <summary>
    /// 导入语音时刷新原始版：**覆盖**。语义是「用户有意换了一份素材」，
    /// 旧的原始版从此没有意义，留着只会让「还原」还原到一个用户已经不想要的版本。
    ///
    /// <b>命名必须跟工作版走</b>（<paramref name="workPath"/>），不能跟用户选的原文件走：
    /// 用户选进来的是 <c>record_001.wav</c>，工具箱会把工作版改名成 <c>Vo-094-笨蛋.wav</c>，
    /// 若按源文件名存原始版，还原时按工作文件名去找永远找不到 —— 而且是静默失效。
    /// </summary>
    public void RefreshOriginal(
        ProjectInfo project,
        string chapterCode,
        string sourcePath,
        string workPath)
    {
        var targetPath = GetOriginalPath(project, chapterCode, workPath);
        if (FileSystemUtility.PathsEqual(sourcePath, targetPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        // 内容取**用户选的原文件**（工作版可能刚被重命名/移动，也可能已经被别的东西改过）。
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    /// <summary>
    /// 规整前的兜底：还没有原始版的，把**当前状态**存成原始版（不覆盖已有的）。
    /// 返回这一轮新补了几份，供日志与提示用。
    ///
    /// 失败直接抛：没有回退点的批量覆盖不该开始（和上一版整章备份的纪律一致）。
    /// </summary>
    public int EnsureOriginals(ProjectInfo project, string chapterCode, IReadOnlyList<string> workPaths)
    {
        var created = 0;
        foreach (var workPath in workPaths)
        {
            var targetPath = GetOriginalPath(project, chapterCode, workPath);
            if (File.Exists(targetPath) || FileSystemUtility.PathsEqual(workPath, targetPath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(workPath, targetPath, overwrite: false);
            created++;
        }

        return created;
    }

    /// <summary>
    /// 语音被改名（改备注会重命名工作文件）时，把原始版**跟着改名**。
    /// 不改的话「还原」会按新工作名去找一个不存在的原始版，静默失效。
    ///
    /// 改名失败只记日志、不打断备注保存：最坏情况是丢一次原始版，
    /// 而下次规整的 <see cref="EnsureOriginals"/> 会拿当前状态补一份（虽然不再是"最初"）。
    /// </summary>
    public void RenameOriginal(ProjectInfo project, string chapterCode, string oldWorkPath, string newWorkPath)
    {
        var oldOriginalPath = GetOriginalPath(project, chapterCode, oldWorkPath);
        var newOriginalPath = GetOriginalPath(project, chapterCode, newWorkPath);
        if (!File.Exists(oldOriginalPath) ||
            FileSystemUtility.PathsEqual(oldOriginalPath, newOriginalPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newOriginalPath)!);
            File.Move(oldOriginalPath, newOriginalPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"原始版跟着改名失败（下次规整会补一份新的）：{oldOriginalPath}", error);
        }
    }

    /// <summary>语音被移除时顺手删掉原始版，避免 Tools/VoiceOriginals 里越积越多孤儿文件。</summary>
    public void DeleteOriginal(ProjectInfo project, string chapterCode, string workPath)
    {
        var originalPath = GetOriginalPath(project, chapterCode, workPath);
        try
        {
            if (File.Exists(originalPath))
            {
                File.Delete(originalPath);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"删除原始版失败（不影响移除语音）：{originalPath}", error);
        }
    }

    /// <summary>把原始版复制回工作文件。没有原始版返回 false（调用方据此提示，而不是静默成功）。</summary>
    public bool TryRestore(ProjectInfo project, string chapterCode, string workPath)
    {
        var originalPath = GetOriginalPath(project, chapterCode, workPath);
        if (!File.Exists(originalPath))
        {
            return false;
        }

        // 写到同目录临时件再替换：正在播放/被杀软占用时，不会留下半截文件。
        var temporaryPath = workPath + ".galrestore-tmp.wav";
        File.Copy(originalPath, temporaryPath, overwrite: true);
        File.Move(temporaryPath, workPath, overwrite: true);
        return true;
    }
}
