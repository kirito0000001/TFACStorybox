using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using GalExcleTools.Services;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 版本号的解析与比较（<b>纯逻辑</b>：不发网络、不写文件、不读设置，输入输出全是参数与返回值）。
///
/// 规则来自 `Docs/热更新-设计.md` §7.1：
/// <list type="number">
///   <item>先比 <c>Major.Minor.Patch.Build</c> 四段数字（缺的段按 0 补）；</item>
///   <item><b>正式版大于预发布版</b>：<c>1.0.0</c> &gt; <c>1.0.0-Beta</c>；</item>
///   <item>都是预发布版时，把 <c>-</c> 之后的标签按 <c>.</c> 切段逐段比：
///         数字段按整数比、<b>数字段小于非数字段</b>、其余按 <see cref="StringComparison.OrdinalIgnoreCase"/> 比；</item>
///   <item>解析前先去掉前导 <c>v</c>/<c>V</c>，并截掉 <c>+</c> 之后的构建元数据。</item>
/// </list>
///
/// 第 4 条不只是洁癖：本工程开了 SourceLink，编译出来的
/// <c>InformationalVersion</c> 实际是 <c>1.0.0+&lt;提交号&gt;</c>，
/// 不截掉那个 <c>+</c> 后面的一串，版本号根本解析不出来。
/// </summary>
internal static class UpdateVersion
{
    /// <summary>读不出任何版本信息时的兜底值（设计 §11.4）。</summary>
    public const string FallbackText = "1.0.0";

    /// <summary>
    /// 归一化版本文本：去空白 → 去前导 <c>v</c>/<c>V</c> → 截掉 <c>+</c> 之后的构建元数据。
    /// **保留** <c>-</c> 之后的预发布标签 —— 通道判定（正式版 / 测试版）全靠它。
    /// 归一化不出内容时返回空串，由调用方决定怎么兜底。
    /// </summary>
    public static string NormalizeVersionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = text.Trim().TrimStart('v', 'V').Trim();
        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value[..plus];
        }

        return value.Trim();
    }

    /// <summary>把版本文本解析成「四段数字 + 预发布标签」。解析不出来返回 false。</summary>
    public static bool TryParse(string? text, out UpdateVersionKey key)
    {
        key = UpdateVersionKey.Empty;

        var normalized = NormalizeVersionText(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        var dash = normalized.IndexOf('-');
        var core = dash < 0 ? normalized : normalized[..dash];
        var label = dash < 0 ? string.Empty : normalized[(dash + 1)..];

        var parts = core.Split('.');
        if (parts.Length is 0 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var index = 0; index < parts.Length; index++)
        {
            // NumberStyles.None：不接受正负号、空格、千分位 —— 「1.0.-1」「+1」「1 0」都算非法。
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return false;
            }
        }

        key = new UpdateVersionKey(numbers, SplitPreReleaseLabels(label));
        return true;
    }

    /// <summary>
    /// 语义比较。<c>&gt; 0</c> 表示左侧更新（只有这一种结果才会被当成「有更新」）。
    ///
    /// 任一侧解析不出来时返回 <c>0</c>（当作「无从比较」）——**绝不**在解析失败时
    /// 报出「发现新版本」。远端清单是外部输入，宁可什么都不提示，也不能凭一串
    /// 读不懂的字符把用户骗去下载。需要提示时由编排层先用 <see cref="TryParse"/> 判断。
    /// </summary>
    public static int CompareSemantic(string? left, string? right)
    {
        if (!TryParse(left, out var first) || !TryParse(right, out var second))
        {
            return 0;
        }

        for (var index = 0; index < 4; index++)
        {
            var diff = first.Numbers[index].CompareTo(second.Numbers[index]);
            if (diff != 0)
            {
                return Math.Sign(diff);
            }
        }

        return Math.Sign(ComparePreRelease(first.PreRelease, second.PreRelease));
    }

    private static string[] SplitPreReleaseLabels(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return [];
        }

        var raw = label.Split('.');
        var labels = new List<string>(raw.Length);
        foreach (var item in raw)
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
            {
                labels.Add(trimmed);
            }
        }

        return [.. labels];
    }

    private static int ComparePreRelease(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            if (left.Length == right.Length)
            {
                return 0;
            }

            // 一边是正式版、一边是预发布版：正式版更大。
            return left.Length == 0 ? 1 : -1;
        }

        var shared = Math.Min(left.Length, right.Length);
        for (var index = 0; index < shared; index++)
        {
            var diff = CompareLabel(left[index], right[index]);
            if (diff != 0)
            {
                return diff;
            }
        }

        // 前缀完全相同：段数多的更大（1.0.0-Beta < 1.0.0-Beta.1）。
        return left.Length.CompareTo(right.Length);
    }

    private static int CompareLabel(string left, string right)
    {
        var leftIsNumber = IsAllDigits(left);
        var rightIsNumber = IsAllDigits(right);

        if (leftIsNumber && rightIsNumber)
        {
            if (long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue)
                && long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue))
            {
                return leftValue.CompareTo(rightValue);
            }

            // 位数多到 long 装不下：位数多的更大，位数相同再按字符串比。
            var lengthDiff = left.Length.CompareTo(right.Length);
            return lengthDiff != 0 ? lengthDiff : string.CompareOrdinal(left, right);
        }

        if (leftIsNumber != rightIsNumber)
        {
            // 数字段小于非数字段（语义化版本规范第 11 条）。
            return leftIsNumber ? -1 : 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>解析结果：四段数字（缺的段已补 0）+ 预发布标签（空数组表示正式版）。</summary>
internal sealed record UpdateVersionKey(int[] Numbers, string[] PreRelease)
{
    public static UpdateVersionKey Empty { get; } = new(new int[4], []);

    /// <summary>带 <c>-</c> 后缀的预发布版（测试版）。</summary>
    public bool IsPreRelease => PreRelease.Length > 0;
}

/// <summary>
/// 读当前应用的版本号文本（五级回退，取第一个能解析成功的）：
/// <c>ax-app-version.json</c> → <c>AssemblyInformationalVersion</c> → <c>Assembly.GetName().Version</c> →
/// <c>FileVersionInfo.ProductVersion</c> → <c>FileVersion</c> → 兜底 <c>"1.0.0"</c>。
///
/// **必须以 ax-app-version.json 为首选**：AX 工具箱把同一个源码编译成多个不同版本号的产物时，
/// 只改这个旁挂文件，不动程序集元数据。不先读它，同一个 exe 在所有 AX 产物里都会报同一个旧版本号，
/// 更新比较也跟着错。
///
/// **其次必须以 InformationalVersion 为首选**：csproj 里 <c>AssemblyVersion</c> 是四段的
/// <c>1.0.0.0</c>，而 <c>&lt;Version&gt;</c> / <c>InformationalVersion</c> 是三段的 <c>1.0.0</c>；
/// 发 <c>-Beta</c> 包时**只有 InformationalVersion 能看出「这是测试版」** ——
/// 读错了会把测试版显示成正式版号，更新比较也跟着错。
///
/// 只读自身程序集的元数据与旁边那个 JSON（反射 + 文件版本信息），不发网络、不写文件、不碰设置。
/// </summary>
internal static class ApplicationVersionReader
{
    public static string ReadCurrentVersionText()
    {
        var assembly = typeof(ApplicationVersionReader).Assembly;
        foreach (var candidate in EnumerateCandidates(assembly))
        {
            if (UpdateVersion.TryParse(candidate, out _))
            {
                return UpdateVersion.NormalizeVersionText(candidate);
            }
        }

        return UpdateVersion.FallbackText;
    }

    private static IEnumerable<string?> EnumerateCandidates(Assembly assembly)
    {
        yield return ReadOverrideVersion();
        yield return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        yield return assembly.GetName().Version?.ToString();

        var fileInfo = TryReadFileVersionInfo(assembly);
        yield return fileInfo?.ProductVersion;
        yield return fileInfo?.FileVersion;
    }

    /// <summary>
    /// 读 <c>ax-app-version.json</c>（AX 工具箱旁挂的版本覆盖文件，随包复制到 exe 同级）。
    /// 读不出来只留痕并跳到下一个候选：这个文件是**可选覆盖**，不是必需依赖。
    /// </summary>
    private static string? ReadOverrideVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, AppVersionService.OverrideFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Version", out var versionElement))
            {
                return versionElement.GetString();
            }

            ToolboxLog.Warn($"版本覆盖文件里没有 Version 字段：{path}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            ToolboxLog.Warn($"版本覆盖文件读不出来，改用程序集版本号：{path}", error);
        }

        return null;
    }

    private static FileVersionInfo? TryReadFileVersionInfo(Assembly assembly)
    {
        // 单文件发布时 Location 是空串；这里不是错误，直接跳到兜底值。
        var location = assembly.Location;
        if (string.IsNullOrEmpty(location))
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(location);
        }
        catch (Exception error)
        {
            // 读不到文件版本信息不该让「关于」区变成空白：还有兜底值，但必须留痕。
            ToolboxLog.Warn($"读取程序集文件版本信息失败：{location}", error);
            return null;
        }
    }
}
