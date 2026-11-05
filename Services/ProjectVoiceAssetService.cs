using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GalExcleTools.Services;

internal sealed class ProjectVoiceAssetService
{
    /// <summary>
    /// 可选进来的源格式。**最终一律转成 wav/48k/16bit**（见 <see cref="VoiceAudioFormat"/>）——
    /// 用户手上有什么格式就丢什么，统一规格是导入这一层的事，不该让用户先自己转。
    /// </summary>
    public static readonly string[] Extensions = VoiceAudioFormat.ImportableExtensions;

    /// <summary>语音的「原始版」档案（见 <see cref="VoiceOriginalArchiveService"/>）。</summary>
    private readonly VoiceOriginalArchiveService _originalArchive = new();

    public static List<string> GetVoiceFilePaths(ProjectInfo project)
    {
        var folderPath = WorkspacePathUtility.GetProjectVoiceFolderPath(project);
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(folderPath, "*.wav", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(folderPath, path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetVoiceRowKey(string voicePath)
    {
        var name = Path.GetFileNameWithoutExtension(voicePath);
        var match = Regex.Match(name, @"^(?<key>Vo-\d+)(?:-.+)?$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["key"].Value : name;
    }

        public string ImportVoice(
            ProjectInfo project,
            ProjectTextRow row,
            int rowCount,
            string sourcePath,
            ProjectVoiceMapState state,
            string? ffmpegPath = null)
        {
            if (!IsValidVoicePath(sourcePath))
            {
                throw new InvalidOperationException(
                    "不支持的音频格式：" + Path.GetExtension(sourcePath) + "。");
            }

            var remark = GetDefaultRemark(sourcePath);
            var targetPath = WriteVoice(
                project, row, rowCount, sourcePath, remark, state, copySource: true, ffmpegPath: ffmpegPath);

            // 导入/更换即**刷新**原始版（覆盖旧的）：这是用户有意换的一份素材，
            // 旧原始版留着只会让人「还原」到一个已经不想要的版本。
            // 只改备注（UpdateRemark）不走这里 —— 那不算换素材。
            // 命名跟工作版（targetPath）走，内容取用户选的原文件（sourcePath）。
            _originalArchive.RefreshOriginal(project, row.ChapterCode, sourcePath, targetPath);
            return targetPath;
        }

    public string UpdateRemark(
        ProjectInfo project,
        ProjectTextRow row,
        int rowCount,
        string remark,
        ProjectVoiceMapState state)
    {
        if (!state.Voices.TryGetValue(row.Id, out var currentPath) || !File.Exists(currentPath))
        {
            throw new FileNotFoundException("当前行还没有可修改的语音文件。");
        }

        var updatedPath = WriteVoice(project, row, rowCount, currentPath, remark, state, copySource: !IsManagedVoicePath(project, currentPath));

        // 改备注会重命名工作文件，原始版必须跟着改 —— 否则「还原」按新名找不到文件。
        _originalArchive.RenameOriginal(project, row.ChapterCode, currentPath, updatedPath);
        return updatedPath;
    }

    public string? RemoveVoice(ProjectInfo project, ProjectTextRow row, ProjectVoiceMapState state)
    {
        if (!state.Voices.Remove(row.Id, out var removedPath))
        {
            return null;
        }

        if (IsManagedVoicePath(project, removedPath) &&
            !state.Voices.Values.Any(path => FileSystemUtility.PathsEqual(path, removedPath)) &&
            File.Exists(removedPath))
        {
            File.Delete(removedPath);
        }

        // 语音都删了，原始版留着就是孤儿。同一个工作文件被别的行复用时不动它。
        if (!state.Voices.Values.Any(path => FileSystemUtility.PathsEqual(path, removedPath)))
        {
            _originalArchive.DeleteOriginal(project, row.ChapterCode, removedPath);
        }

        return removedPath;
    }

    public static string ParseRemark(string voicePath)
    {
        var name = Path.GetFileNameWithoutExtension(voicePath);
        var match = Regex.Match(name, @"^Vo-\d+(?:-(?<remark>.+))?$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["remark"].Value : GetDefaultRemark(voicePath);
    }

    public static bool IsValidVoicePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path) &&
            Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static string GetDefaultRemark(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var match = Regex.Match(name, @"^(?:Vo[-_]?)?\d+[-_\s]*(?<remark>.+)$", RegexOptions.IgnoreCase);
        var remark = match.Success ? match.Groups["remark"].Value : name;
        return TextUtility.SanitizeRemark(remark);
    }

    private static string WriteVoice(
        ProjectInfo project,
        ProjectTextRow row,
        int rowCount,
        string sourcePath,
        string remark,
        ProjectVoiceMapState state,
        bool copySource,
        string? ffmpegPath = null)
    {
        var folderPath = WorkspacePathUtility.GetProjectChapterVoiceFolderPath(project, row.ChapterCode);
        Directory.CreateDirectory(folderPath);

        if (!int.TryParse(row.RowName, out var rowNumber) || rowNumber < 1)
        {
            throw new InvalidOperationException($"文本索引无效，无法生成语音文件名：{row.RowName}");
        }

        var targetPath = BuildTargetPath(folderPath, rowNumber, rowCount, remark);
        var oldPath = state.Voices.TryGetValue(row.Id, out var existingPath) ? existingPath : null;
        if (!FileSystemUtility.PathsEqual(sourcePath, targetPath))
        {
            if (copySource)
            {
                var tempPath = Path.Combine(folderPath, $"__voice_import_{Guid.NewGuid():N}.wav");

                // 源已经是统一规格就直接拷（不重编码，省时间也不引入任何损失）；
                // 否则交给 ffmpeg 转 —— mp3/flac 这类**不能原样塞进 .wav 文件名**，
                // 那样文件头是 mp3、扩展名是 wav，ffmpeg 读得出来、UE 却会当成坏文件。
                if (VoiceAudioFormat.IsCanonicalWav(sourcePath))
                {
                    File.Copy(sourcePath, tempPath, overwrite: true);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(ffmpegPath))
                    {
                        throw new InvalidOperationException(
                            "这份素材需要转成 wav/48k/16bit，但没找到 ffmpeg。请在设置里指定 ffmpeg 路径。");
                    }

                    try
                    {
                        VoiceAudioFormat.Convert(ffmpegPath, sourcePath, tempPath, out _);
                    }
                    catch (Exception)
                    {
                        // 半截临时文件必须清掉，否则下次导入会被它干扰（同名不会撞，但会留垃圾）。
                        AtomicFileWriter.TryDelete(tempPath);
                        throw;
                    }
                }

                File.Move(tempPath, targetPath, overwrite: true);
            }
            else
            {
                File.Move(sourcePath, targetPath, overwrite: true);
            }
        }

        state.Voices[row.Id] = targetPath;
        DeleteOldManagedVoiceIfUnused(project, state, oldPath, targetPath);
        return targetPath;
    }

    private static string BuildTargetPath(string folderPath, int rowNumber, int rowCount, string remark)
    {
        var digitCount = Math.Max(2, Math.Max(1, rowCount).ToString().Length);
        var baseName = $"Vo-{Math.Max(1, rowNumber).ToString().PadLeft(digitCount, '0')}";
        var safeRemark = TextUtility.SanitizeRemark(remark);
        var fileName = string.IsNullOrWhiteSpace(safeRemark)
            ? $"{baseName}.wav"
            : $"{baseName}-{safeRemark}.wav";
        return Path.Combine(folderPath, fileName);
    }

    private static void DeleteOldManagedVoiceIfUnused(
        ProjectInfo project,
        ProjectVoiceMapState state,
        string? oldPath,
        string targetPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) ||
            FileSystemUtility.PathsEqual(oldPath, targetPath) ||
            !IsManagedVoicePath(project, oldPath) ||
            state.Voices.Values.Any(path => FileSystemUtility.PathsEqual(path, oldPath)) ||
            !File.Exists(oldPath))
        {
            return;
        }

        File.Delete(oldPath);
    }

    private static bool IsManagedVoicePath(ProjectInfo project, string path)
    {
        var voiceRoot = WorkspacePathUtility.GetProjectVoiceFolderPath(project);
        var fullRoot = Path.GetFullPath(voiceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
