using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 一次启动清理的结果。全部是「发生了什么」，没有「该不该继续」——清理永远不阻断启动。
/// </summary>
internal sealed record UpdateCleanupOutcome(
    bool PreviousRunProven,
    int DeletedAgedFiles,
    int DeletedPendingFiles,
    int TrimmedPackages,
    string Message)
{
    /// <summary>这一轮到底动没动盘（用来决定要不要写日志）。</summary>
    public bool TouchedAnything => DeletedAgedFiles > 0 || DeletedPendingFiles > 0 || TrimmedPackages > 0;
}

/// <summary>
/// 延迟一轮的回滚窗口与清理（设计 §9.5）。两阶段替换会把被替换 / 被删除的旧文件原地改名成
/// <c>*.old-{版本号}</c>，**当时一个都不删** —— 那是「新版装上去但起不来」唯一的路。
///
/// 什么时候才算「新版真的能用」？<b>它自己启动过一次</b>。所以：
/// <list type="number">
///   <item>每次启动都在 <c>%LOCALAPPDATA%\GalExcleTools\update-startup.json</c> 写一份
///         「我以版本 X 启动过」的标记；</item>
///   <item>下次启动读这份标记：标记里的版本 == 当前版本 ⇒ 上一轮那个版本确实跑起来过 ⇒
///         这时才删 <c>*.old-*</c>、处理待清理清单、淘汰过期包；</item>
///   <item>版本对不上（或压根没有标记）⇒ <b>一个字都不删</b>，只把标记重写成当前版本。</item>
/// </list>
///
/// 于是「更新成功、新版起不来」时用户手上始终留着完整的更新前文件：把 <c>.old-版本号</c>
/// 后缀去掉就是旧版。清理刻意**不**碰白名单外的任何东西（设计 §9.6），
/// 也不碰安装目录之外的路径（防止有人往待清理清单里塞别的路径）。
///
/// 这个类只做磁盘 IO，不抛异常：任何一步失败都只留痕，下次启动还会再来一遍。
/// </summary>
internal sealed class UpdateCleanup
{
    /// <summary>「上一轮以哪个版本启动过」的标记文件名（设计 §9.5）。</summary>
    public const string StartupMarkFileName = "update-startup.json";

    /// <summary>回滚窗口的文件名标记：<c>X.dll.old-1.0.1</c> 里的 <c>.old-</c>。</summary>
    public const string AgedSuffixMarker = ".old-";

    /// <summary>updater 删不掉、留给下一轮的文件清单（<c>Scripts\热更新覆盖.ps1</c> 里写的）。</summary>
    public const string PendingCleanupFileName = "PendingCleanup.txt";

    private readonly UpdateWhitelist _whitelist;
    private readonly UpdateCacheStore _cacheStore;

    /// <param name="installDirectory">程序目录；默认 <c>AppContext.BaseDirectory</c>。</param>
    /// <param name="workRootDirectory">工作根（标记与待清理清单住这儿）；默认 <c>%LOCALAPPDATA%\GalExcleTools</c>。</param>
    public UpdateCleanup(
        string? installDirectory = null,
        string? workRootDirectory = null,
        UpdateWhitelist? whitelist = null,
        UpdateCacheStore? cacheStore = null)
    {
        InstallDirectory = string.IsNullOrWhiteSpace(installDirectory)
            ? UpdateLaunchService.ResolveInstallDirectory()
            : installDirectory!.Trim();
        WorkRootDirectory = string.IsNullOrWhiteSpace(workRootDirectory)
            ? DefaultWorkRootDirectory
            : workRootDirectory!.Trim();
        _whitelist = whitelist ?? UpdateWhitelist.Default;
        _cacheStore = cacheStore ?? new UpdateCacheStore(Path.Combine(WorkRootDirectory, UpdateCacheStore.UpdatesFolderName));
    }

    /// <summary>设计 §9.5 指定的工作根：<c>%LOCALAPPDATA%\GalExcleTools</c>。</summary>
    public static string DefaultWorkRootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GalExcleTools");

    public string InstallDirectory { get; }

    public string WorkRootDirectory { get; }

    /// <summary>启动标记路径（设计 §9.5：「启动成功标记」）。</summary>
    public string StartupMarkPath => Path.Combine(WorkRootDirectory, StartupMarkFileName);

    /// <summary>待清理清单路径（updater 与这里共用一个文件）。</summary>
    public string PendingCleanupPath => Path.Combine(WorkRootDirectory, PendingCleanupFileName);

    /// <summary>文件名里带 <c>.old-</c> 吗（只看文件名，不看目录）。</summary>
    public static bool IsAgedPath(string? relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return name is { Length: > 0 }
            && name.Length > AgedSuffixMarker.Length
            && name.Contains(AgedSuffixMarker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把 <c>Assets\logo.png.old-1.0.1</c> 还原成 <c>Assets\logo.png</c>。
    /// 版本段必须是「像版本号的一串」（数字/字母/点/加号/减号，且至少一个数字），
    /// 否则不当成回滚文件 —— 用户的 <c>笔记.old-backup.txt</c> 不能被误判。
    /// </summary>
    public static bool TryStripAgedSuffix(string? relativePath, out string originalPath)
    {
        originalPath = relativePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var name = Path.GetFileName(relativePath);
        if (name is not { Length: > 0 })
        {
            return false;
        }

        var index = name.LastIndexOf(AgedSuffixMarker, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            return false;
        }

        var version = name[(index + AgedSuffixMarker.Length)..];
        if (!IsVersionSegment(version))
        {
            return false;
        }

        originalPath = relativePath[..(relativePath.Length - version.Length - AgedSuffixMarker.Length)];
        return originalPath.Length > 0;
    }

    /// <summary>
    /// 读启动标记里的版本号；文件不在、读不动、JSON 坏、没这个字段都返回 <c>null</c>
    /// （调用方把它当「上一轮没留下证据」处理，也就是不清理）。
    /// </summary>
    public static string? TryReadStartupMarkVersion(string? markPath)
    {
        if (string.IsNullOrWhiteSpace(markPath) || !File.Exists(markPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(markPath!, Encoding.UTF8));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.String)
            {
                var text = version.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
            }

            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            ToolboxLog.Warn($"读启动标记失败，这一轮不清理回滚文件：{markPath}", error);
            return null;
        }
    }

    /// <summary>
    /// 一次启动该做的事（设计 §9.5）。<paramref name="currentVersion"/> 是**当前正在跑的这个**版本。
    /// 无论清没清，最后都会把标记重写成当前版本 —— 这样下一次启动才有「上一轮跑过」的证据。
    /// </summary>
    public UpdateCleanupOutcome Run(string? currentVersion)
    {
        var version = string.IsNullOrWhiteSpace(currentVersion) ? string.Empty : currentVersion!.Trim();
        var previousVersion = TryReadStartupMarkVersion(StartupMarkPath);
        var proven = IsSameVersion(previousVersion, version);

        var deletedAged = 0;
        var deletedPending = 0;
        var trimmed = 0;
        if (proven)
        {
            // ★ 只有这一条路会动盘：上一轮那个版本启动过 ⇒ 「新版能跑起来」已被真实验证过。
            // 顺手收掉 updater 留下的「一键修复」（正常它自己会删；只有脚本被硬杀时才留着）。
            deletedAged = DeleteAgedFiles() + DeleteRepairHelper();
            deletedPending = ProcessPendingCleanupList();
            trimmed = TrimCache();
        }

        WriteStartupMark(version);

        var message = proven
            ? $"上一轮以 {version} 启动过：清理回滚文件 {deletedAged} 个、待清理项 {deletedPending} 个、过期包 {trimmed} 个。"
            : $"启动标记是「{previousVersion ?? "无"}」，当前版本是 {version}：这一轮保留 *.old-* 回滚窗口，不清理。";

        if (proven && (deletedAged > 0 || deletedPending > 0 || trimmed > 0))
        {
            ToolboxLog.Info(message);
        }

        return new UpdateCleanupOutcome(proven, deletedAged, deletedPending, trimmed, message);
    }

    /// <summary>
    /// 删掉程序目录里所有 <c>*.old-{版本}</c>（递归）。两道闸门：后缀必须像版本号，
    /// 且<b>去掉后缀的那条路径必须落在镜像白名单里</b> —— 用户自己的
    /// <c>Data\笔记.txt.old-2026</c> 一根汗毛都不能动（设计 §9.6）。
    /// </summary>
    /// <summary>
    /// 收掉 updater 留在程序目录里的「修复更新.cmd / 修复更新.ps1」。
    /// 正常情况下 updater 更新成功后会自己删掉它们；只有"脚本被硬杀 / 回滚没跑完"才会留下 ——
    /// 而那一轮既然已经能启动到新版，回滚窗口就该按常规收掉，这两个脚本留着只会误导用户去手动回退。
    /// </summary>
    private int DeleteRepairHelper()
    {
        var deleted = 0;
        foreach (var name in new[] { "修复更新.ps1", "修复更新.cmd" })
        {
            var path = Path.Combine(InstallDirectory, name);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ToolboxLog.Warn($"删不掉更新修复脚本：{path}", error);
            }
        }

        return deleted;
    }

    private int DeleteAgedFiles()
    {
        var deleted = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(InstallDirectory, "*", SearchOption.AllDirectories);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            ToolboxLog.Warn($"枚举程序目录失败，跳过回滚文件清理：{InstallDirectory}", error);
            return 0;
        }

        foreach (var path in files)
        {
            string relative;
            try
            {
                relative = Path.GetRelativePath(InstallDirectory, path);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                ToolboxLog.Warn($"算不出相对路径，跳过：{path}", error);
                continue;
            }

            if (!TryStripAgedSuffix(relative, out var original) || !_whitelist.IsMirrored(original))
            {
                continue;
            }

            if (UpdateCacheStore.DeleteQuietly(path))
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>
    /// 处理 updater 留下的待清理清单（它当时删不掉的文件：多半是被别的进程占着）。
    /// <b>只认程序目录里的路径</b>——清单是磁盘上的文本，谁都能改，不能拿它当删除任意文件的凭据。
    /// 删掉了就从清单里去掉；还删不掉的留着，下次启动再试。
    /// </summary>
    private int ProcessPendingCleanupList()
    {
        if (!File.Exists(PendingCleanupPath))
        {
            return 0;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(PendingCleanupPath, Encoding.UTF8);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"读待清理清单失败，跳过：{PendingCleanupPath}", error);
            return 0;
        }

        var deleted = 0;
        var remaining = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!IsInsideInstallDirectory(line))
            {
                ToolboxLog.Warn($"待清理清单里有一条不指程序目录的路径，已忽略：{line}");
                continue;
            }

            if (!File.Exists(line))
            {
                continue;
            }

            if (UpdateCacheStore.DeleteQuietly(line))
            {
                deleted++;
            }
            else
            {
                remaining.Add(line);
            }
        }

        try
        {
            if (remaining.Count == 0)
            {
                File.Delete(PendingCleanupPath);
            }
            else
            {
                // updater 写这个文件用的是「UTF-8 带 BOM」，这里跟着它的口味来（人也要看得懂）。
                AtomicFileWriter.WriteAllText(
                    PendingCleanupPath,
                    string.Join(Environment.NewLine, remaining) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ToolboxLog.Warn($"回写待清理清单失败（不影响这次启动）：{PendingCleanupPath}", error);
        }

        return deleted;
    }

    /// <summary>半截包与过期包（设计 §8.1 ⑩）。</summary>
    private int TrimCache()
    {
        var deleted = _cacheStore.DeleteDownloadLeftovers();
        return deleted + _cacheStore.TrimPackages();
    }

    /// <summary>路径在程序目录里面吗（比前缀时要带上分隔符，免得 <c>C:\工具2</c> 混进 <c>C:\工具</c>）。</summary>
    private bool IsInsideInstallDirectory(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(InstallDirectory);
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ToolboxLog.Warn($"待清理清单里的路径不合法，已忽略：{path}", error);
            return false;
        }
    }

    /// <summary>把「当前版本 + 时间戳」写进启动标记（原子写，失败只留痕）。</summary>
    private void WriteStartupMark(string version)
    {
        try
        {
            var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteString("version", version);
                writer.WriteString("writtenAt", DateTimeOffset.Now.ToString("O"));
                writer.WriteString("note", "客户端每次启动都会重写这份标记；下一次启动读到同一个版本才会清理 *.old-*（设计 §9.5）。");
                writer.WriteEndObject();
            }

            AtomicFileWriter.WriteAllBytes(StartupMarkPath, buffer.ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 写不进去的后果很轻：下一轮不清理而已（回滚文件多留一轮，方向是安全的）。
            ToolboxLog.Warn($"写启动标记失败，下一轮不会清理回滚文件：{StartupMarkPath}", error);
        }
    }

    /// <summary>两个版本号算不算「同一个版本」（先按语义比，比不了再按字面比）。</summary>
    private static bool IsSameVersion(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (UpdateVersion.TryParse(left, out _) && UpdateVersion.TryParse(right, out _))
        {
            return UpdateVersion.CompareSemantic(left, right) == 0;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>版本段是不是「像版本号的一串」：数字 / 字母 / <c>. + - _</c>，且至少一个数字。</summary>
    private static bool IsVersionSegment(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var hasDigit = false;
        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch))
            {
                hasDigit = true;
                continue;
            }

            if (char.IsAsciiLetter(ch) || ch is '.' or '+' or '-' or '_')
            {
                continue;
            }

            return false;
        }

        return hasDigit;
    }
}
