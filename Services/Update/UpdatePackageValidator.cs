using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GalExcleTools.Services.Update;

/// <summary>
/// zip 里一条条目的最小快照。<see cref="EntryName"/> 已经过 <see cref="UpdatePackageValidator.NormalizeEntryName"/>
/// 归一化（<c>/</c> → <c>\</c>），所以比较时不用再管打包器是谁。
/// </summary>
internal readonly record struct UpdatePackageEntrySnapshot(string EntryName, long Length);

/// <summary>
/// 更新包结构校验（设计 §8.2）。这是「大小 → SHA-256 → <b>包身份</b>」的第三层：
/// 前两层只能证明「字节没坏」，证明不了「这包是给本工具的、是这一版的、主程序真的在里面」。
///
/// 校验顺序和文案都是契约（设计 §8.2 的七条），一个字都别改：updater 那边也按同样的规则判，
/// 两边不一致的后果是「客户端说能装、updater 拒装」——最难查的一类问题。
/// </summary>
internal static class UpdatePackageValidator
{
    /// <summary>包内体检单的固定名字（设计 §6.2）。</summary>
    public const string PackageManifestFileName = "update-package.json";

    /// <summary>
    /// 归一化条目名：统一分隔符为 <c>\</c>。
    /// 实测（设计 §8.2 的警告）：PowerShell <c>Compress-Archive</c> 与 .NET 写 <c>\</c>，**Bandizip 写 <c>/</c>**，
    /// 不归一化就会「本地打包能过、用 Bandizip 打包就报缺少主程序 exe」。
    /// </summary>
    public static string NormalizeEntryName(string? entryName) =>
        (entryName ?? string.Empty).Replace('/', '\\').Trim();

    /// <summary>取条目名里的文件名部分（不含目录）。</summary>
    public static string GetEntryFileName(string? entryName)
    {
        var normalized = NormalizeEntryName(entryName).TrimEnd('\\');
        var index = normalized.LastIndexOf('\\');
        return index >= 0 ? normalized[(index + 1)..] : normalized;
    }

    /// <summary>
    /// zip-slip 守卫（设计 §14）：绝对路径、盘符、UNC、<c>..</c>、非法字符一律拒绝。
    /// 在解压/复制**之前**先扫一遍条目名，是这个项目里唯一能拦住「压缩包往 C:\Windows 里写文件」的地方。
    /// </summary>
    public static bool IsUnsafeEntryName(string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return true;
        }

        var normalized = NormalizeEntryName(entryName);

        // 绝对路径（含 UNC）与盘符（C:\…）。
        if (normalized.StartsWith('\\') || (normalized.Length >= 2 && normalized[1] == ':'))
        {
            return true;
        }

        foreach (var segment in normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (trimmed is "." or "..")
            {
                return true;
            }

            // Windows 会把结尾的空格/点吃掉，于是「x.txt.」与「x.txt」会落到同一个文件上——一并拒绝。
            if (trimmed.EndsWith(' ') || trimmed.EndsWith('.'))
            {
                return true;
            }

            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>对磁盘上的包做完整校验（设计 §8.2 的七条）。</summary>
    public static UpdatePackageValidationResult ValidatePackage(
        string packagePath,
        string expectedStableKey,
        string expectedVersion,
        string expectedRuntime)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = new List<UpdatePackageEntrySnapshot>(archive.Entries.Count);

            foreach (var entry in archive.Entries)
            {
                var name = NormalizeEntryName(entry.FullName);
                if (IsUnsafeEntryName(name))
                {
                    ToolboxLog.Warn($"更新包里有不安全的路径条目，已拒绝：{name}");
                    return UpdatePackageValidationResult.Fail($"更新包里包含不安全的路径条目：{name}", entries.Count);
                }

                entries.Add(new UpdatePackageEntrySnapshot(name, entry.Length));
            }

            return Validate(entries, name => ReadEntryText(archive, name), expectedStableKey, expectedVersion, expectedRuntime);
        }
        catch (InvalidDataException error)
        {
            ToolboxLog.Warn($"更新包不是有效的 zip：{packagePath}", error);
            return UpdatePackageValidationResult.Fail("更新包不是有效的 zip 文件。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ToolboxLog.Warn($"更新包打不开：{packagePath}", error);
            return UpdatePackageValidationResult.Fail("更新包打不开（文件可能被占用）。");
        }
    }

    /// <summary>
    /// 纯逻辑部分：只吃「条目快照 + 一条按条目名读文本的委托」，不碰磁盘、不联网，
    /// 所以回归测试可以喂字典直接断言（设计 §15.1 的 <c>update-package-validate</c>）。
    /// </summary>
    public static UpdatePackageValidationResult Validate(
        IReadOnlyList<UpdatePackageEntrySnapshot> entries,
        Func<string, string?> readEntryText,
        string expectedStableKey,
        string expectedVersion,
        string expectedRuntime)
    {
        var count = entries.Count;

        // 1) 必须先找到包内清单。updater 侧是「递归找」，这里同样不限定层级，只在多个候选时取路径最短的那个。
        var manifestEntry = entries
            .Where(entry => string.Equals(GetEntryFileName(entry.EntryName), PackageManifestFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.EntryName.Length)
            .FirstOrDefault();

        if (manifestEntry.EntryName is not { Length: > 0 })
        {
            return UpdatePackageValidationResult.Fail("更新包缺少 update-package.json。", count);
        }

        // 2) 读得出来吗。
        var json = readEntryText(manifestEntry.EntryName);
        if (string.IsNullOrWhiteSpace(json))
        {
            return UpdatePackageValidationResult.Fail("update-package.json 无法读取。", count);
        }

        ToolboxPackageManifestDto? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.ToolboxPackageManifestDto);
        }
        catch (JsonException error)
        {
            ToolboxLog.Warn("包内 update-package.json 反序列化失败。", error);
            return UpdatePackageValidationResult.Fail("update-package.json 无法读取。", count);
        }

        if (manifest is null)
        {
            return UpdatePackageValidationResult.Fail("update-package.json 无法读取。", count);
        }

        // 3) 这是不是给本工具的包。
        if (!string.Equals(manifest.ToolboxStableKey?.Trim(), expectedStableKey?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return UpdatePackageValidationResult.Fail("更新包工具箱标识不匹配。", count);
        }

        // 4) 版本必须和清单里的版本是同一版（用语义比较，"v1.0.1" 与 "1.0.1" 视为同一版）。
        //    ★ 期望版本自己也必须能解析出来：`UpdateVersion.CompareSemantic` 对「解析不了」的入参
        //    返回 0（设计 §7.2：比不出高低时不冤枉用户），放到这里就成了「认不出就放行」——
        //    所以这一层要显式挡住（fail-closed，设计 §11.6）。updater 侧同样按严格比较判。
        if (!UpdateVersion.TryParse(manifest.Version, out _)
            || !UpdateVersion.TryParse(expectedVersion, out _)
            || UpdateVersion.CompareSemantic(manifest.Version, expectedVersion) != 0)
        {
            return UpdatePackageValidationResult.Fail("更新包版本和清单版本不一致。", count);
        }

        // 5) Runtime 必须匹配（设计 §19：客户端只认自己这一种 RID）。
        if (!string.Equals(manifest.Runtime?.Trim(), expectedRuntime?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return UpdatePackageValidationResult.Fail("更新包 Runtime 不匹配。", count);
        }

        // ★ 增量包（2026-10-06）：包里只有"变化/新增"的文件，**通常不含主程序 exe**，
        //   所以 6/7 两条换成"必须列出至少一个变化文件、且基准版本非空"。
        if (manifest.Delta)
        {
            if (string.IsNullOrWhiteSpace(manifest.BaseVersion))
            {
                return UpdatePackageValidationResult.Fail("增量包没有写基准版本。", count);
            }

            if (manifest.Files is not { Count: > 0 })
            {
                return UpdatePackageValidationResult.Fail("增量包里没有任何变化文件。", count);
            }

            foreach (var file in manifest.Files)
            {
                if (string.IsNullOrWhiteSpace(file.Path) || UpdatePackagePlan.NormalizeSha256(file.Sha256).Length != 64)
                {
                    return UpdatePackageValidationResult.Fail("增量包里的文件清单不完整。", count);
                }

                // ★ 旧内容指纹（契约 §8.2 的 baseSha256）：**有就必须是合法的 SHA-256**。
                //
                // 为什么允许没有：契约明写"新增文件没有这一项" —— 包里新增、安装目录里本来没有的文件，
                // 没有旧内容可指纹，所以这里是"可选但一旦给了就要合法"，不是"必须有"。
                //
                // 为什么要在这里挡：覆盖器（热更新覆盖.ps1 的 Assert-ZdDeltaPrecondition）会拿这个值
                // 逐个与安装目录里的旧文件比对，"对不上就拒绝覆盖"。如果这里放一个畸形值过去，
                // 覆盖器那边要到**已经解压、准备动文件**的时候才发现，而那时用户看到的是
                // "更新包有问题"而不是"清单写坏了"。校验层挡下来，失败得更早、也更好懂。
                if (!string.IsNullOrWhiteSpace(file.BaseSha256)
                    && UpdatePackagePlan.NormalizeSha256(file.BaseSha256).Length != 64)
                {
                    return UpdatePackageValidationResult.Fail(
                        $"增量包里 {file.Path} 的旧内容指纹不合法。", count);
                }
            }

            return UpdatePackageValidationResult.Pass(manifest, count);
        }

        // 6) 主程序 exe 必须真的在包里 —— 按「条目文件名 == entryExe」判断，
        //    ★ 与 热更新覆盖.ps1 的 Resolve-PackageSourceRoot 保持同一口径（设计 §8.2 的警告）。
        var entryExe = GetEntryFileName(manifest.EntryExe);
        var exeEntry = entries
            .Where(entry => string.Equals(GetEntryFileName(entry.EntryName), entryExe, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.EntryName.Length)
            .FirstOrDefault();

        if (entryExe.Length == 0 || exeEntry.EntryName is not { Length: > 0 })
        {
            return UpdatePackageValidationResult.Fail("更新包缺少主程序 exe。", count);
        }

        // 7) 且不能是空文件（Android 端「包身份」校验的等价物，设计 §8.2）。
        if (exeEntry.Length <= 0)
        {
            return UpdatePackageValidationResult.Fail("更新包里的主程序 exe 是空文件。", count);
        }

        return UpdatePackageValidationResult.Pass(manifest, count);
    }

    private static string? ReadEntryText(ZipArchive archive, string normalizedEntryName)
    {
        var entry = archive.Entries.FirstOrDefault(item =>
            string.Equals(NormalizeEntryName(item.FullName), normalizedEntryName, StringComparison.Ordinal));

        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            ToolboxLog.Warn($"读取包内文件失败：{normalizedEntryName}", error);
            return null;
        }
    }
}
