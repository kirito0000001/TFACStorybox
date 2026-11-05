using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

/// <summary>
/// 用 SoundCraft 引擎救"ffmpeg 拉不动"的语音。
///
/// 【为什么需要第二套】ffmpeg 只有两条路：套增益（撞真峰值）或 loudnorm（它的限幅器
/// 只削越界的峰，不做重度压限）。峰均比 12 dB 以上的素材，两条路都到不了目标。
/// SoundCraft 的 Maximizer 是真正的限幅器（带前视与释放），能多压 1~2 dB。
///
/// 【怎么调】soundcraft-cli 的 run 子命令：导入 → 插 gain → 插 maximizer → 反弹。
/// 参数必须用单独的 mix.insert_param 设，写在 mix.insert 里**不生效**（实测）。
/// </summary>
internal sealed class SoundCraftRescueService
{
    /// <summary>默认安装位置。找不到就整体跳过，不报错 —— 这是可选增强，不是必需依赖。</summary>
    private static readonly string[] CandidatePaths =
    [
        @"E:\ARTcraft\SoundCraft\soundcraft-cli.exe",
        @"D:\ARTcraft\SoundCraft\soundcraft-cli.exe",
        @"C:\Program Files\SoundCraft\soundcraft-cli.exe"
    ];

    public const double TargetLufs = -12.0;

    /// <summary>起始天花板。留 0.4 dB 给真峰值（编码后会再涨一点）。</summary>
    private const double StartCeiling = -3.1;

    public static string? LocateCli()
    {
        foreach (var path in CandidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 处理一条：调增益 → 量 LUFS → 不够重来；真峰值越线就把天花板压低再来。
    /// 返回 (是否成功, 说明)。
    /// </summary>
    public async Task<(bool Succeeded, string Message)> RescueAsync(
        string cliPath,
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var ceiling = StartCeiling;
        JsonElement? last = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var gain = 12.0;
            for (var round = 0; round < 4; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                last = await RunBounceAsync(cliPath, sourcePath, targetPath, gain, ceiling, cancellationToken)
                    .ConfigureAwait(false);
                if (last is null)
                {
                    return (false, "SoundCraft 没有返回结果");
                }

                var lufs = ReadDouble(last.Value, "lufs");
                if (Math.Abs(lufs - TargetLufs) <= 0.4)
                {
                    break;
                }

                gain += TargetLufs - lufs;
            }

            var truePeak = ReadDouble(last!.Value, "true_peak_db");
            if (truePeak <= -2.9)
            {
                var finalLufs = ReadDouble(last.Value, "lufs");
                return (true, $"{finalLufs:0.0} LUFS / {truePeak:0.0} dBTP");
            }

            // 真峰值越线：天花板按超出量下压，重来一轮。
            ceiling -= (truePeak + 3.0) + 0.4;
        }

        return (false, "SoundCraft 三轮仍没压进真峰值上限");
    }

    private static async Task<JsonElement?> RunBounceAsync(
        string cliPath,
        string sourcePath,
        string targetPath,
        double gainDb,
        double ceilingDb,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        void Add(string command) => startInfo.ArgumentList.Add(command);

        Add("run");
        Add("--cmd");
        Add($"file.import_audio={{\"path\":\"{Normalize(sourcePath)}\"}}");
        Add("--cmd");
        Add("mix.insert={\"track\":2,\"slot\":0,\"plugin\":\"gain\"}");
        Add("--cmd");
        Add($"mix.insert_param={{\"track\":2,\"slot\":0,\"param\":\"gain\",\"value\":{Format(gainDb)}}}");
        Add("--cmd");
        Add("mix.insert={\"track\":2,\"slot\":1,\"plugin\":\"maximizer\"}");
        Add("--cmd");
        Add($"mix.insert_param={{\"track\":2,\"slot\":1,\"param\":\"ceiling\",\"value\":{Format(ceilingDb)}}}");
        Add("--bounce");
        Add(Normalize(targetPath));

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        // 最后一行 JSON 就是 bounce 的统计（前面几行是各命令的回显）。
        JsonElement? result = null;
        foreach (var line in stdout.Split('\n'))
        {
            var text = line.Trim();
            if (text.Length == 0 || text[0] != '{')
            {
                continue;
            }

            try
            {
                result = JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (JsonException)
            {
                // 不是 JSON 的行直接跳过。
            }
        }

        return result;
    }

    private static double ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : double.NaN;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>soundcraft-cli 只认 Windows 路径且用正斜杠更稳（它对 /tmp 这类路径会报错）。</summary>
    private static string Normalize(string path) => path.Replace('\\', '/');
}
