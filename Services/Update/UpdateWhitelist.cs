using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 热更新的**镜像白名单**（设计 §9.6）：哪些文件属于「程序文件、可删可换」，
/// 哪些是「用户自己塞的东西、永不触碰」。
/// <para>
/// ★ 这是本设计里唯一一处**同一份逻辑两处实现**的地方（C# 侧 + updater 的 PowerShell 脚本），
/// 因为 updater 是独立进程、跑在 Windows PowerShell 5.1 上，引用不了本程序集。
/// 规避办法就是把这个类的一部分（规则数据）落到 <c>Scripts\update-whitelist.json</c>，
/// 两边读同一个文件 —— 谁改规则都得改那一份，改不出两种行为。
/// </para>
/// <para>
/// 本类属于「纯逻辑」：只做字符串判断与集合运算，不碰文件系统、不发网络。
/// 读文件那一步在 <see cref="UpdateWhitelistLoader"/>（IO 层）里。
/// </para>
/// </summary>
internal sealed class UpdateWhitelist
{
    /// <summary>规则文件名（安装目录下 <c>Scripts\</c> 里那一份）。</summary>
    public const string FileName = "update-whitelist.json";

    /// <summary>相对安装目录的路径。</summary>
    public const string RelativePath = @"Scripts\" + FileName;

    private readonly HashSet<string> _extensions;
    private readonly HashSet<string> _directories;
    private readonly HashSet<string> _excludedDirectories;
    private readonly HashSet<string> _excludedExtensions;
    private readonly string[] _excludedNameFragments;
    private readonly string[] _excludedNameSuffixes;

    public UpdateWhitelist(
        IEnumerable<string> extensions,
        IEnumerable<string> directories,
        IEnumerable<string> excludedDirectories,
        IEnumerable<string> excludedExtensions,
        IEnumerable<string> excludedNameFragments,
        IEnumerable<string> excludedNameSuffixes)
    {
        _extensions = ToSet(extensions);
        _directories = ToSet(directories);
        _excludedDirectories = ToSet(excludedDirectories);
        _excludedExtensions = ToSet(excludedExtensions);
        _excludedNameFragments = ToArray(excludedNameFragments);
        _excludedNameSuffixes = ToArray(excludedNameSuffixes);
    }

    /// <summary>
    /// 内置的兜底规则：**与 <c>Scripts\update-whitelist.json</c> 必须逐字一致**（有回归用例盯着）。
    /// 只在规则文件读不出来时用，用了就要在日志里留一行 —— 不要让它悄悄变成第二真源。
    /// </summary>
    public static UpdateWhitelist Default { get; } = new(
        extensions: ["exe", "dll", "pri", "xbf", "winmd", "json", "config", "pdb", "xml", "ico", "png"],
        // Scripts\ 也要镜像：热更新覆盖器自己就住在 <安装目录>\Scripts\热更新覆盖.ps1 里。
        // 不带上它，更新器就永远修不了自己 —— 以后发现覆盖逻辑有坑，只能让用户重装，
        // 这正是源项目 versionCode 那次事故的同一类陷阱（升级判据自己把自己锁住）。
        directories: ["Assets", "Tools", "Scripts"],
        excludedDirectories: ["__pycache__"],
        excludedExtensions: ["pyc"],
        excludedNameFragments: [".old-"],
        excludedNameSuffixes: [".download"]);

    /// <summary>扩展名（不含点）与目录名都不分大小写 —— README.md:271 的硬规矩，这条踩过六次。</summary>
    public IReadOnlyCollection<string> Extensions => _extensions;

    public IReadOnlyCollection<string> Directories => _directories;

    public IReadOnlyCollection<string> ExcludedDirectories => _excludedDirectories;

    public IReadOnlyCollection<string> ExcludedExtensions => _excludedExtensions;

    public IReadOnlyCollection<string> ExcludedNameFragments => _excludedNameFragments;

    public IReadOnlyCollection<string> ExcludedNameSuffixes => _excludedNameSuffixes;

    /// <summary>
    /// 从规则文件的文本解析（纯逻辑：调用方负责把文本读进来）。
    /// 缺字段、类型不对、数组里混了非字符串 —— 一律抛，**不回落默认值**
    /// （悄悄回落会让「白名单缩水」这种事变成静默的数据删除）。
    /// </summary>
    public static UpdateWhitelist FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"白名单 {FileName} 的根节点不是对象。");
        }

        return new UpdateWhitelist(
            ReadArray(root, "extensions"),
            ReadArray(root, "directories"),
            ReadArray(root, "excludedDirectories"),
            ReadArray(root, "excludedExtensions"),
            ReadArray(root, "excludedNameFragments"),
            ReadArray(root, "excludedNameSuffixes"));
    }

    /// <summary>
    /// 判断一个**相对路径**是否落在镜像范围内（可删可换）。
    /// <para>排除项优先：<c>.old-</c>（上一轮的回滚文件）、<c>*.download</c>（下载临时名）、
    /// <c>__pycache__</c>、<c>*.pyc</c> —— 这几个是「我们自己产生的工作文件」，不属于程序文件集合。</para>
    /// </summary>
    public bool IsMirrored(string relativePath)
    {
        var normalized = NormalizeRelative(relativePath);
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var fragment in _excludedNameFragments)
        {
            if (normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var suffix in _excludedNameSuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (_excludedDirectories.Contains(segment))
            {
                return false;
            }
        }

        var extension = Path.GetExtension(segments[^1]);
        if (extension.Length > 1)
        {
            var bare = extension[1..];
            if (_excludedExtensions.Contains(bare))
            {
                return false;
            }

            if (_extensions.Contains(bare))
            {
                return true;
            }
        }

        // 没有扩展名 / 扩展名不在表里：只有落在 Assets\ / Tools\ / Scripts\ 这几个程序目录里才算镜像范围。
        return segments.Length > 1 && _directories.Contains(segments[0]);
    }

    /// <summary>把任意写法的相对路径归一成 <c>a\b\c</c>（zip 里目录分隔符可能是 <c>/</c>，§8.2）。</summary>
    public static string NormalizeRelative(string path) =>
        path.Replace('/', '\\').Trim().TrimStart('\\');

    private static HashSet<string> ToSet(IEnumerable<string> values) =>
        new(values, StringComparer.OrdinalIgnoreCase);

    private static string[] ToArray(IEnumerable<string> values)
    {
        var list = new List<string>();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(value.Trim());
            }
        }

        return [.. list];
    }

    private static List<string> ReadArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"白名单 {FileName} 缺少数组字段 {name}。");
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"白名单 {FileName} 的 {name} 里混进了非字符串项。");
            }

            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }
}
