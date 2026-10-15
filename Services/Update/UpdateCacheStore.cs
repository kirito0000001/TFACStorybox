using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 更新包缓存目录（设计 §8.1 的 ① 与 ⑩）。它**只管磁盘**：路径怎么算、上次失败的临时文件怎么清、
/// 旧包怎么淘汰。「这个包能不能用」是 <see cref="UpdatePackageValidator"/> 的事，
/// 「怎么把它下下来」是 <see cref="UpdateDownloader"/> 的事。
///
/// 目录固定为 <c>%LOCALAPPDATA%\GalExcleTools\Updates</c>（设计 §8.1）：放进
/// <c>LOCALAPPDATA</c> 而不是安装目录，是因为安装目录可能在 Program Files 下（写不进去），
/// 而且 updater 覆盖安装目录时也不希望被这一百兆的包碍事。
/// </summary>
internal sealed class UpdateCacheStore
{
    /// <summary>缓存目录名（设计 §8.1）。</summary>
    public const string UpdatesFolderName = "Updates";

    /// <summary>半截包的临时后缀。正式包名存在 ⇒ 它一定完整（同分区 <see cref="File.Move(string, string)"/> 是原子的）。</summary>
    public const string DownloadSuffix = ".download";

    /// <summary>只保留最近几个包（设计 §8.1 ⑩）。留一点余量是为了「回滚到上一版」时不用重新下。</summary>
    public const int DefaultKeepPackageCount = 2;

    public UpdateCacheStore(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? DefaultRootDirectory : rootDirectory;
    }

    /// <summary>设计 §8.1 指定的默认目录。</summary>
    public static string DefaultRootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GalExcleTools",
        UpdatesFolderName);

    public string RootDirectory { get; }

    /// <summary>
    /// 包名是不是「一个纯文件名」。清单里的 <c>fileName</c> 是远端输入，<b>不能被当成路径</b>：
    /// 带分隔符或 <c>..</c> 的一律拒绝（设计 §14 的 zip-slip 同一类问题）。
    /// </summary>
    public static bool IsSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (!string.Equals(fileName, fileName.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (fileName is "." or "..")
        {
            return false;
        }

        if (fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0)
        {
            return false;
        }

        return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    /// <summary>包名 → 正式包路径；包名不合法时返回 <c>null</c>（调用方必须当成失败处理）。</summary>
    public string? ResolvePackagePath(string? fileName) =>
        IsSafeFileName(fileName) ? Path.Combine(RootDirectory, fileName!) : null;

    /// <summary>正式包路径 → 临时包路径（<c>{packagePath}.download</c>，设计 §8.1）。</summary>
    public static string ResolveDownloadPath(string packagePath) => packagePath + DownloadSuffix;

    public void EnsureRootDirectory()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 建不出来不算致命：后面的写入会失败并给出更具体的错误。这里只留痕。
            ToolboxLog.Warn($"创建更新包缓存目录失败：{RootDirectory}", error);
        }
    }

    /// <summary>删掉目录下所有 <c>*.download</c>（上次失败留下的半截包），返回删掉的个数。</summary>
    public int DeleteDownloadLeftovers()
    {
        var deleted = 0;
        foreach (var path in EnumerateFiles($"*{DownloadSuffix}"))
        {
            if (DeleteQuietly(path))
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>只保留最近 <paramref name="keepCount"/> 个包（按写入时间倒序），返回删掉的个数。</summary>
    public int TrimPackages(int keepCount = DefaultKeepPackageCount)
    {
        if (keepCount < 0)
        {
            keepCount = 0;
        }

        List<FileInfo> files;
        try
        {
            files = [.. new DirectoryInfo(RootDirectory)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(file => !file.Name.EndsWith(DownloadSuffix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            ToolboxLog.Warn($"读取更新包缓存目录失败，跳过淘汰：{RootDirectory}", error);
            return 0;
        }

        var deleted = 0;
        foreach (var file in files.Skip(keepCount))
        {
            if (DeleteQuietly(file.FullName))
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>删除一个文件，失败只留痕（缓存清理永远不能把主流程带崩）。</summary>
    public static bool DeleteQuietly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 被占用或没有权限：留着不动，下次启动还会再清一遍（设计 §9.5 的延迟清理同一个思路）。
            ToolboxLog.Warn($"删除文件失败，已跳过：{path}", error);
            return false;
        }
    }

    /// <summary>取文件大小；文件不存在或读不到时返回 <c>null</c>。</summary>
    public static long? TryGetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ToolboxLog.Warn($"读取文件大小失败：{path}", error);
            return null;
        }
    }

    private IEnumerable<string> EnumerateFiles(string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(RootDirectory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            ToolboxLog.Warn($"枚举更新包缓存目录失败：{RootDirectory}", error);
            return [];
        }
    }
}
