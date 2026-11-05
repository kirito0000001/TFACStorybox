using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

/// <summary>
/// 用 PyMSS 的 UVR-DeNoise 给语音去噪。
///
/// 【为什么是它】实测五六个模型在同一条素材上，只有它把"滋滋"的底噪真正抽走了，
/// Apollo / denoise_aufr33 那几个几乎是原样输出（高频能量差 &lt;0.3 dB）。
///
/// 【代价】VR 架构很慢：15 秒素材要 104 秒（约 7 倍实时）。一条一条跑，
/// 26 条就是 45 分钟 —— 所以调用方必须给用户看到预估时间。
/// </summary>
internal sealed class PymssDenoiseService
{
    private static readonly string[] CandidateCli =
    [
        @"E:\Pymss Studio\pymss-cli\Scripts\pymss.exe",
        @"D:\Pymss Studio\pymss-cli\Scripts\pymss.exe",
        @"C:\Pymss Studio\pymss-cli\Scripts\pymss.exe"
    ];

    private static readonly string[] CandidateModelDirs =
    [
        @"E:\Pymss Studio\models",
        @"D:\Pymss Studio\models",
        @"C:\Pymss Studio\models"
    ];

    public const string ModelName = "UVR-DeNoise";

    /// <summary>输出轨道名。VR 模型给的是 "No Noise"（干净）和 "Noise"（抽走的噪声）。</summary>
    private const string CleanStemSuffix = "_No Noise.wav";

    public static string? LocateCli()
    {
        return CandidateCli.FirstOrDefault(File.Exists);
    }

    public static string? LocateModelDir()
    {
        return CandidateModelDirs.FirstOrDefault(Directory.Exists);
    }

    /// <summary>用 ffmpeg 量一次积分响度（NaN = 量不出来）。</summary>
    private static async Task<double> MeasureLufsAsync(string ffmpegPath, string path, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
        {
            "-hide_banner", "-nostats", "-i", path,
            "-af", "loudnorm=I=-12:TP=-3:print_format=json",
            "-f", "null", "-"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return double.NaN;
        }

        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var match = System.Text.RegularExpressions.Regex.Match(error, @"""input_i""\s*:\s*""(-?[\d.]+)""");
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : double.NaN;
    }

    private static async Task RunFfmpegAsync(
        string ffmpegPath, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 去噪一条。成功后**就地覆盖**原文件，并把"抽走的噪声"留在 workDir 里备查。
    /// </summary>
    public async Task<(bool Succeeded, string Message)> DenoiseAsync(
        string cliPath,
        string modelDir,
        string ffmpegPath,
        string voicePath,
        string workDir,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDir);

        // 先把素材拉到"模型熟悉的电平"再喂给它。
        // 这批语音大多在 -20 ~ -25 LUFS，直接去噪时模型的判断阈值会偏 ——
        // 拉到 -18 只是临时工位电平，最终响度由后面那一步统一到 -12。
        var workingPath = voicePath;
        var preGainPath = Path.Combine(workDir, "__denoise_pre.wav");
        var lufs = await MeasureLufsAsync(ffmpegPath, voicePath, cancellationToken).ConfigureAwait(false);
        if (!double.IsNaN(lufs))
        {
            var gain = Math.Clamp(-18.0 - lufs, -24.0, 24.0);
            if (Math.Abs(gain) > 0.5)
            {
                await RunFfmpegAsync(
                    ffmpegPath,
                    ["-hide_banner", "-loglevel", "error", "-y", "-i", voicePath,
                     "-af", $"volume={gain.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}dB",
                     "-c:a", "pcm_s16le", preGainPath],
                    cancellationToken).ConfigureAwait(false);

                if (File.Exists(preGainPath))
                {
                    workingPath = preGainPath;
                }
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
        {
            "infer", ModelName,
            "--model-dir", modelDir,
            "-i", workingPath,
            "-o", workDir,
            "--save-as-folder",
            "--device", "cuda"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (false, "无法启动 pymss");
        }

        // 进度条走 stderr，不能等到结束再读 —— 那条管道会写满把进程卡死。
        var drain = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await drain.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            return (false, stdout.Trim().Split('\n').LastOrDefault()?.Trim() ?? "pymss 执行失败");
        }

        var stemDir = Path.Combine(workDir, Path.GetFileNameWithoutExtension(workingPath));
        var clean = Path.Combine(stemDir, Path.GetFileNameWithoutExtension(workingPath) + CleanStemSuffix);
        if (!File.Exists(clean))
        {
            var actual = Directory.Exists(stemDir)
                ? string.Join("、", Directory.GetFiles(stemDir).Select(Path.GetFileName))
                : "（没有输出目录）";
            return (false, "没找到干净轨，实际输出：" + actual);
        }

        var temp = voicePath + ".denoised.wav";
        File.Copy(clean, temp, overwrite: true);
        File.Move(temp, voicePath, overwrite: true);
        return (true, "已去噪");
    }
}

/// <summary>
/// 记录"哪些文件已经去噪过"。
///
/// 去噪一条要一两分钟，不能每次规整都重跑 —— 靠「文件名 + 大小 + 修改时间」
/// 认身份，文件被换掉（大小或时间变了）就自然失效，需要重新去噪。
/// </summary>
internal sealed class VoiceDenoiseCacheService
{
    private sealed record Entry(string FileName, long Length, long Ticks);

    public bool IsDenoised(ProjectInfo project, string chapterCode, string filePath)
    {
        var entry = Load(project, chapterCode).FirstOrDefault(item =>
            string.Equals(item.FileName, Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return false;
        }

        var info = new FileInfo(filePath);
        return info.Exists && entry.Length == info.Length && entry.Ticks == info.LastWriteTimeUtc.Ticks;
    }

    public void MarkDenoised(ProjectInfo project, string chapterCode, string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            return;
        }

        var entries = Load(project, chapterCode)
            .Where(item => !string.Equals(item.FileName, info.Name, StringComparison.OrdinalIgnoreCase))
            .Append(new Entry(info.Name, info.Length, info.LastWriteTimeUtc.Ticks))
            .ToList();

        var path = GetPath(project, chapterCode);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<Entry> Load(ProjectInfo project, string chapterCode)
    {
        var path = GetPath(project, chapterCode);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return [];
        }
    }

    private static string GetPath(ProjectInfo project, string chapterCode) =>
        Path.Combine(project.Path, WorkspacePathUtility.ToolsFolderName, "VoiceDenoise", chapterCode + ".json");
}
