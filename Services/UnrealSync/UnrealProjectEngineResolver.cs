using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace GalExcleTools.Services;

/// <summary>
/// 从 <c>.uproject</c> 的 <c>EngineAssociation</c> 反查**它到底用的是哪个引擎**。
///
/// 【为什么不能拿"配置里的引擎路径"当答案】
/// 那个设置是**会过期的**（换过引擎、引擎挪过位置），而它一旦过期，
/// 在线执行那条路就**永远连不上** —— 因为节点匹配要求"引擎根目录也对得上"
/// （见 <c>Tools/UnrealBridge/run_remote_unreal_job.py</c> 的 <c>_find_matching_node</c>）。
///
/// 2026-10-12 实测：配置指向 <c>D:\YuanMa\UnrealEngine-release</c>（整个路径都不存在），
/// 而工程实际用的是 <c>D:/UnrealEngine-5.8.2</c> —— 于是每次同步都只能退回离线，
/// 慢十倍，**而且不报错**（看起来只是"这次又慢了"）。那种失败最难查。
///
/// 权威来源是**工程自己**说的：<c>EngineAssociation</c>。它有两种写法：
/// - 一串 GUID：对应注册表 <c>HKCU\Software\Epic Games\Unreal Engine\Builds</c> 下的一项
///   （源码版引擎就是这么注册的）；
/// - 一个版本号（<c>"5.8"</c>）：对应
///   <c>HKLM\SOFTWARE\EpicGames\Unreal Engine\5.8\InstalledDirectory</c>
///   （启动器装的引擎）。
///
/// 【查不到怎么办】
/// 返回 null，让调用方退回用配置里的引擎路径 —— 也就是保持原来的行为。
/// **不要**在这里抛错：查不到不等于不能用，只是"没有更权威的答案"而已。
/// </summary>
internal static class UnrealProjectEngineResolver
{
    /// <summary>读出这个工程用的引擎根目录（<c>&lt;UE&gt;/Engine</c> 的上一级）；查不到返回 null。</summary>
    public static string? ResolveEngineRoot(string? unrealProjectPath)
    {
        if (string.IsNullOrWhiteSpace(unrealProjectPath) || !File.Exists(unrealProjectPath))
        {
            return null;
        }

        string? association;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(unrealProjectPath));
            association = document.RootElement.TryGetProperty("EngineAssociation", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(association))
        {
            return null;
        }

        var trimmed = association.Trim();
        var resolved = LooksLikeGuid(trimmed)
            ? ResolveFromBuildsRegistry(trimmed)
            : ResolveFromLauncherRegistry(trimmed);

        // 注册表里指到的地方**必须真的在**：注册项本身也可能是陈的。
        // 不在就当没查到，让调用方退回去用配置 —— 那至少是用户显式选过的。
        if (string.IsNullOrWhiteSpace(resolved) || !Directory.Exists(resolved))
        {
            return null;
        }

        return NormalizeEngineRoot(resolved);
    }

    /// <summary>
    /// 把注册表给的那两种写法统一成 <c>&lt;UE&gt;\Engine</c>。
    ///
    /// ⚠️ 这一步是必须的：<c>EngineRoot</c> 在这个代码库里有**两种含义** ——
    /// <see cref="UnrealPythonTaskExecutionService.ResolveEngineRoot"/> 返回
    /// <c>&lt;UE&gt;\Engine</c>（从 <c>Binaries\Win64</c> 往上两级），
    /// 而注册表的 <c>InstalledDirectory</c> 给的是 <c>&lt;UE&gt;</c>。
    /// 下游（拼自带 Python 的路径、写远程任务）按前者算 ——
    /// 拿 <c>&lt;UE&gt;</c> 直接去拼就成了 <c>&lt;UE&gt;\Binaries\...</c>，找不到东西。
    ///
    /// 2026-10-12 实测踩过：在线路径报"当前 Unreal 引擎没有找到远程执行所需的 Python"，
    /// 而那个 Python 明明在 <c>&lt;UE&gt;\Engine\Binaries\ThirdParty\Python3\Win64</c>。
    /// </summary>
    private static string NormalizeEngineRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(Path.GetFileName(full), "Engine", StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, "Engine");
    }

    private static bool LooksLikeGuid(string value) =>
        Guid.TryParse(value.Trim('{', '}'), out _);

    /// <summary>
    /// 源码版引擎：<c>HKCU\Software\Epic Games\Unreal Engine\Builds</c> 下
    /// <c>"GUID" = "引擎根目录"</c>。键名带不带花括号两种写法都试 —— Epic 自己的写法变过。
    /// </summary>
    private static string? ResolveFromBuildsRegistry(string association)
    {
        var bare = association.Trim('{', '}');
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\Unreal Engine\Builds");
            if (key is null)
            {
                return null;
            }

            return key.GetValue(bare) as string
                ?? key.GetValue("{" + bare + "}") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>启动器装的引擎：<c>HKLM\SOFTWARE\EpicGames\Unreal Engine\&lt;版本&gt;\InstalledDirectory</c>。</summary>
    private static string? ResolveFromLauncherRegistry(string version)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\EpicGames\Unreal Engine\" + version);
            return key?.GetValue("InstalledDirectory") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
