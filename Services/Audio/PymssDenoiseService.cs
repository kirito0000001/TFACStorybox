using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

/// <summary>
/// 用 PyMSS 的 UVR-DeNoise 给语音去噪。
///
/// 【为什么整批跑一次】每次 <c>pymss infer</c> 都是新进程，都要重新加载模型
/// （十几秒）。一条一进程的话，40 条光加载就白烧 10 分钟。
/// 所以先把整批做增益、放进暂存目录，再一次调用处理整个目录。
///
/// 【为什么先做增益】这批素材只有 -20 ~ -25 LUFS，模型按正常电平训练，
/// 直接喂判断阈值会偏。拉到 -18 只是工位电平，最终响度由后面的规整统一。
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

    /// <summary>VR 模型给两条轨：干净的是 "No Noise"，另一条是被抽走的噪声。</summary>
    private const string CleanStemSuffix = "_No Noise.wav";

    private const double WorkingLufs = -18.0;

    public static string? LocateCli() => CandidateCli.FirstOrDefault(File.Exists);

    public static string? LocateModelDir() => CandidateModelDirs.FirstOrDefault(Directory.Exists);

    /// <summary>批量结果：成功替换回原位的文件，以及失败的文件与原因。</summary>
    public sealed record DenoiseBatchResult(
        IReadOnlyList<string> Succeeded,
        IReadOnlyList<(string File, string Reason)> Failed);

    /// <summary>
    /// 一次处理整批。<paramref name="onProgress"/> 给 0~1（本批内部进度），
    /// <paramref name="onPhase"/> 给阶段名（准备 / 加载模型 / 去噪 / 回写）。
    /// </summary>
    public async Task<DenoiseBatchResult> DenoiseBatchAsync(
        string cliPath,
        string modelDir,
        string ffmpegPath,
        IReadOnlyList<string> files,
        string workDir,
        Action<double, string>? onProgress,
        Action<string>? onPhase,
        CancellationToken cancellationToken)
    {
        var stagingIn = Path.Combine(workDir, "in");
        var stagingOut = Path.Combine(workDir, "out");
        ResetDirectory(stagingIn);
        ResetDirectory(stagingOut);

        // 文件名加序号前缀：同名文件不会互相覆盖，回写时也能一一对应。
        var staged = new List<(string Source, string Stem)>();
        onPhase?.Invoke("正在准备（拉电平）");

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = files[index];
            var stem = $"{index + 1:000}_{Path.GetFileNameWithoutExtension(source)}";
            var stagedPath = Path.Combine(stagingIn, stem + ".wav");

            var lufs = await MeasureLufsAsync(ffmpegPath, source, cancellationToken).ConfigureAwait(false);
            var gain = double.IsNaN(lufs) ? 0d : Math.Clamp(WorkingLufs - lufs, -24d, 24d);

            await RunFfmpegAsync(
                ffmpegPath,
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", source,
                 "-af", $"volume={gain.ToString("0.0", CultureInfo.InvariantCulture)}dB",
                 "-c:a", "pcm_s16le", stagedPath],
                cancellationToken).ConfigureAwait(false);

            if (File.Exists(stagedPath))
            {
                staged.Add((source, stem));
            }

            // 准备只占一小截：这一步每条 0.2 秒，推理才是大头。
            onProgress?.Invoke(
                (index + 1) / (double)files.Count * 0.08,
                $"第 {index + 1} / {files.Count} 条 · {Path.GetFileName(source)} · "
                + (double.IsNaN(lufs) ? "量不出响度，按原样送入" : $"{lufs:0.0} → -18.0 LUFS（{gain:+0.0;-0.0} dB）"));
        }

        if (staged.Count == 0)
        {
            return new DenoiseBatchResult([], files.Select(file => (file, "暂存失败")).ToList());
        }

        onPhase?.Invoke("正在加载模型");
        var exitCode = await RunInferAsync(
            cliPath, modelDir, stagingIn, stagingOut, onProgress, onPhase, cancellationToken).ConfigureAwait(false);

        onPhase?.Invoke("正在回写");

        var succeeded = new List<string>();
        var failed = new List<(string, string)>();
        foreach (var (source, stem) in staged)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var clean = Path.Combine(stagingOut, stem, stem + CleanStemSuffix);
            if (!File.Exists(clean))
            {
                failed.Add((source, exitCode == 0 ? "没有产出干净轨" : $"pymss 退出码 {exitCode}"));
                continue;
            }

            try
            {
                var temp = source + ".denoised.wav";
                File.Copy(clean, temp, overwrite: true);
                File.Move(temp, source, overwrite: true);
                succeeded.Add(source);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failed.Add((source, error.Message));
            }
        }

        onProgress?.Invoke(1d, $"回写完成：{succeeded.Count} 条成功"
            + (failed.Count > 0 ? $"，{failed.Count} 条失败" : string.Empty));
        return new DenoiseBatchResult(succeeded, failed);
    }

    private static async Task<int> RunInferAsync(
        string cliPath,
        string modelDir,
        string inputDir,
        string outputDir,
        Action<double, string>? onProgress,
        Action<string>? onPhase,
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

        foreach (var argument in new[]
        {
            "infer", ModelName,
            "--model-dir", modelDir,
            "-i", inputDir,
            "-o", outputDir,
            "--save-as-folder",
            "--device", "cuda"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return -1;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var lastPercent = -1d;
        var firstLine = true;

        try
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var percent = ParseProgressPercent(line);
                if (percent is null)
                {
                    continue;
                }

                if (firstLine)
                {
                    firstLine = false;
                    onPhase?.Invoke("正在去噪");
                }

                if (Math.Abs(percent.Value - lastPercent) < 0.5)
                {
                    continue;
                }

                lastPercent = percent.Value;

                var (doneFiles, totalFiles) = ParseFileCounter(line);
                var counter = totalFiles > 0 ? $"第 {doneFiles} / {totalFiles} 条 · " : string.Empty;

                // 推理占整段 8% ~ 100%：准备只要零点几秒，推理是几十秒到几十分钟。
                var vram = ReadVramText();
                onProgress?.Invoke(
                    0.08 + percent.Value / 100d * 0.92,
                    $"本条 {percent.Value:0}% · {counter.TrimEnd(' ', '·', ' ')}"
                    + (vram.Length > 0 ? " · " + vram : string.Empty));
            }
        }
        catch (OperationCanceledException)
        {
            // 只取消"读"没用：pymss 还在跑。必须把进程树杀掉，界面才真的停下。
            TryKill(process);
            throw;
        }

        await stdoutTask.ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string _vramCache = string.Empty;
    private static DateTime _vramCacheAt = DateTime.MinValue;

    /// <summary>
    /// 读一次显存占用。用户要拿它判断"能不能同时开别的程序"。
    ///
    /// nvidia-smi 一次要 50~100ms，进度回调却是几十毫秒一次，
    /// 所以缓存 5 秒 —— 数字够新，又不会把推理拖慢。
    /// </summary>
    private static string ReadVramText()
    {
        if (DateTime.UtcNow - _vramCacheAt < TimeSpan.FromSeconds(5))
        {
            return _vramCache;
        }

        _vramCacheAt = DateTime.UtcNow;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in new[]
            {
                "--query-gpu=memory.used,memory.total",
                "--format=csv,noheader,nounits"
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                _vramCache = string.Empty;
                return _vramCache;
            }

            var output = process.StandardOutput.ReadLine();
            process.WaitForExit(1500);

            if (string.IsNullOrWhiteSpace(output))
            {
                _vramCache = string.Empty;
                return _vramCache;
            }

            var parts = output.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var used) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var total))
            {
                _vramCache = string.Empty;
                return _vramCache;
            }

            _vramCache = $"显存 {used / 1024:0.0}/{total / 1024:0.0} GB（剩 {(total - used) / 1024:0.0} GB）";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 没有 NVIDIA 显卡或没装驱动：不显示这一段就是了。
            _vramCache = string.Empty;
        }

        return _vramCache;
    }

    /// <summary>从 tqdm 的 <c>5/40</c> 里抠出"第几条 / 共几条"。</summary>
    private static (int Done, int Total) ParseFileCounter(string line)
    {
        var match = Regex.Match(line, @"(\d+)/(\d+)");
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out var done) ||
            !int.TryParse(match.Groups[2].Value, out var total) ||
            total <= 0 || done > total)
        {
            return (0, 0);
        }

        return (done, total);
    }

    /// <summary>
    /// 从 tqdm 的一行里抠百分比。形如
    /// <c>Processing VR batches:  45%|████     | 1/3 [00:20&lt;00:35, 30s/it]</c>。
    ///
    /// <c>Total progress</c> 是文件级（1/1），<c>Processing</c> 才是批内 —— 两个都收，
    /// 取任意一个能拿到的百分比即可（它们单调递增）。
    /// </summary>
    private static double? ParseProgressPercent(string line)
    {
        if (string.IsNullOrWhiteSpace(line) ||
            (!line.Contains("Processing", StringComparison.OrdinalIgnoreCase) &&
             !line.Contains("Total progress", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var match = Regex.Match(line, @"(\d{1,3})%");
        return match.Success && double.TryParse(match.Groups[1].Value, out var value) ? value : null;
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
            "-hide_banner", "-nostats", "-nostdin", "-i", path,
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

        string error;
        try
        {
            // 加超时：ffmpeg 万一卡住（比如文件被占用），不能把整批拖死。
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            error = await process.StandardError.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var match = Regex.Match(error, @"""input_i""\s*:\s*""(-?[\d.]+)""");
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : double.NaN;
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, string[] arguments, CancellationToken cancellationToken)
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

        try
        {
            await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void ResetDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 删不掉就复用：里面的旧文件会被同名覆盖。
        }

        Directory.CreateDirectory(path);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 已经退了就没什么可杀的。
        }
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
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            entries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
            return System.Text.Json.JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static string GetPath(ProjectInfo project, string chapterCode) =>
        Path.Combine(project.Path, WorkspacePathUtility.ToolsFolderName, "VoiceDenoise", chapterCode + ".json");
}
