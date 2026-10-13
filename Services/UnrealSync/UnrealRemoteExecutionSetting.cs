using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools.Services;

/// <summary>
/// 查「Python 远程执行」开没开。
///
/// 【为什么值得单查一项】
/// 这个开关不开，**在线执行那条路永远连不上** —— 每次同步都退回离线：
/// 慢十倍，而且从现象上完全看不出来（只看到"这次又慢了"，
/// 或者像 2026-10-13 那样，报一句"没有写出同步结果"）。
/// 而它在编辑器里藏得挺深：
/// <c>Project Settings &gt; Plugins &gt; Python &gt; Python Remote Execution</c>。
///
/// 【值在哪】
/// `[/Script/PythonScriptPlugin.PythonScriptPluginSettings]` 段的 `bRemoteExecution`。
/// 查两处：工程的 `Config/DefaultEngine.ini`，以及 `Saved/Config/&lt;平台&gt;/Engine.ini` ——
/// Unreal 合并配置时**后者覆盖前者**（那是"本机改过的值"）。
/// 两处都没有就是**没开**：插件里这个属性的默认值就是 false，编辑器里默认不打勾。
/// </summary>
internal static class UnrealRemoteExecutionSetting
{
    private const string SectionName = "/Script/PythonScriptPlugin.PythonScriptPluginSettings";
    private const string KeyName = "bRemoteExecution";

    /// <summary>开没开。查不到就当没开（默认值就是 false）。</summary>
    public static bool IsEnabled(string? unrealProjectPath)
    {
        if (string.IsNullOrWhiteSpace(unrealProjectPath) || !File.Exists(unrealProjectPath))
        {
            return false;
        }

        string projectDirectory;
        try
        {
            projectDirectory = Path.GetDirectoryName(Path.GetFullPath(unrealProjectPath)) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return false;
        }

        // 顺序：先默认配置、再本机覆盖 —— 后面读到哪个就用哪个，所以覆盖要在后。
        var candidates = new List<string>
        {
            Path.Combine(projectDirectory, "Config", "DefaultEngine.ini")
        };

        var savedConfigRoot = Path.Combine(projectDirectory, "Saved", "Config");
        if (Directory.Exists(savedConfigRoot))
        {
            try
            {
                candidates.AddRange(Directory
                    .EnumerateFiles(savedConfigRoot, "Engine.ini", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            }
            catch (IOException)
            {
                // 读不到就算了 —— 下面按"没开"处理，那是默认值，不会冤枉谁。
            }
        }

        var value = false;
        foreach (var candidate in candidates)
        {
            var parsed = TryReadValue(candidate);
            if (parsed is { } parsedValue)
            {
                value = parsedValue;
            }
        }

        return value;
    }

    /// <summary>某个 ini 里那个键说了什么；没有这个键就返回 null（区别于"说了 false"）。</summary>
    private static bool? TryReadValue(string iniPath)
    {
        if (!File.Exists(iniPath))
        {
            return null;
        }

        bool? result = null;
        var inSection = false;
        try
        {
            foreach (var rawLine in File.ReadLines(iniPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = string.Equals(line[1..^1].Trim(), SectionName, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection)
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (!line[..separator].Trim().Equals(KeyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = line[(separator + 1)..].Trim();
                // 同一个键可能被后面几行覆盖（ini 的 `+` / `-` 那套这里用不上），
                // 所以取**最后**一次出现的值 —— 与 Unreal 的合取口径一致。
                result = text.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return result;
    }
}
