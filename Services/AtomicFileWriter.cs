using System;
using System.IO;
using System.Text;
using System.Threading;

namespace GalExcleTools.Services;

/// <summary>
/// 文本文件的原子替换与容错删除。
///
/// 「写临时文件再移过去」这套做法原先在各服务里各写了一份，行为并不一致：
/// 有的用 <c>File.Move(overwrite: true)</c>，有的先 <c>Delete</c> 再 <c>Move</c>
/// （删和移之间崩溃就等于原文件消失），临时名有的带 GUID 有的写死 <c>.tmp</c>
/// （写死的那种，两处并发保存会互相踩）。
///
/// 另外本项目**一次重试都没有**：Unreal 编辑器、杀毒软件或资源管理器
/// 正好在读同一个文件时，写入会直接抛 IOException 给用户，而这类占用通常
/// 几十毫秒就过去了。所以这里统一补上短重试。
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>重试次数。三次覆盖掉常见的瞬时占用，再多就该让用户知道确实有问题。</summary>
    private const int DefaultAttempts = 3;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 原子写入文本。先落到带 GUID 的临时文件，再整体替换目标；
    /// 中途失败会清掉临时文件，不在用户目录里留垃圾。
    /// </summary>
    public static void WriteAllText(string path, string text, Encoding? encoding = null) =>
        WriteAtomic(path, temporary => File.WriteAllText(temporary, text, encoding ?? Utf8NoBom));

    /// <summary>
    /// 原子写入二进制。和 <see cref="WriteAllText"/> 同一条路：
    /// 先落到带 GUID 的临时文件再整体替换，中途失败不留垃圾。
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes) =>
        WriteAtomic(path, temporary =>
        {
            ArgumentNullException.ThrowIfNull(bytes);
            File.WriteAllBytes(temporary, bytes);
        });

    /// <summary>
    /// 两条原子写共用的那一半：**临时文件 + 整体替换 + 失败清理**只写在这里。
    /// </summary>
    private static void WriteAtomic(string path, Action<string> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        // 目标目录不存在就先补出来：让写入者自己保证目录在，
        // 这类「新调用方忘了建目录」就不会再复发（表现为 DirectoryNotFoundException 且日志里一个字都没有）。
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            write(temporaryPath);
            Replace(temporaryPath, path);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// 把临时文件顶替成目标文件。目标被占用时短暂重试——
    /// 这里是整条链路上最容易撞上「Unreal 正在读这个文件」的一步。
    /// </summary>
    public static void Replace(string sourcePath, string destinationPath, int attempts = DefaultAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
            }

            // 退避时间随次数增长：50ms、100ms。同步阻塞是有意的——
            // 调用方大多是同步的保存路径，改成 async 会牵动一大片。
            Thread.Sleep(50 * attempt);
        }
    }

    /// <summary>删不掉就算了的删除。</summary>
    public static bool TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
