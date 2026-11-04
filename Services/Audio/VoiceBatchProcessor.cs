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
/// 语音批处理：测量（只读）与规整（就地改）。
///
/// 编排纪律（照项目规矩，不破例）：
/// · <b>起进程一律走 <see cref="UnrealSyncProcessRunner"/></b> —— 它已经把「取消/超时先杀
///   进程树、两个管道在进程活着时就开始读」做对了。代价是这里不能自己读 <c>-progress</c>
///   做实时百分比：进度粒度是「每个文件」而不是「文件内部」（语音通常几秒一条，够用）。
/// · <b>一个文件失败只记这一行</b>，不把整批报成失败 —— 一批 30 条里坏 1 条，
///   用户要的是「那 29 条在哪、第 30 条为什么不行」。
/// · <b>规整必须可回退</b>：先整章备份，再逐条「写临时文件 → 成功后替换」。
///   任何一步失败，原文件一个字节都没动。
/// </summary>
internal sealed class VoiceBatchProcessor
{
    /// <summary>
    /// 并发文件数。ffmpeg 处理音频本身基本单线程，所以<b>并发文件</b>才是提速手段；
    /// 卡在 3 是因为语音文件都很短，再往上收益被磁盘吃掉，而每个进程要各装一份 86 MB 的 avcodec。
    /// </summary>
    public const int MaxConcurrentFiles = 3;

    /// <summary>单个文件的超时。语音最多几十秒，10 分钟纯粹是防「卡死不退」。</summary>
    public static readonly TimeSpan PerFileTimeout = TimeSpan.FromMinutes(10);

    /// <summary>规整时的临时文件后缀。它一定和源文件同目录，替换才是同卷原子操作。</summary>
    private const string TemporarySuffix = ".galvoice-tmp.wav";

    private const int FallbackSampleRate = 48000;

    private static readonly Regex SampleRatePattern = new(@"(?<rate>\d{4,6})\s*Hz", RegexOptions.Compiled);

    /// <summary>
    /// <c>-i</c> 输出头里的时长（<c>Duration: 00:00:03.24, start: …</c>）。
    /// 只认「两位:两位:两位.小数」这种完整形态 —— <c>Duration: N/A</c> 自然匹配不上，
    /// 那种流式/损坏文件的时长就当没有，而不是瞎填一个 0。
    /// </summary>
    private static readonly Regex DurationPattern = new(
        @"Duration:\s*(?<value>\d{2}:\d{2}:\d{2}\.\d+)",
        RegexOptions.Compiled);

    /// <summary>
    /// 测量一批文件，出三段判定。
    /// <b>不改任何文件</b>：只跑 <c>loudnorm=…:print_format=json</c> 到 <c>-f null</c>。
    /// </summary>
    public async Task<VoiceComplianceReport> MeasureAsync(
        string ffmpegPath,
        IReadOnlyList<string> files,
        VoiceLoudnessPreset preset,
        IProgress<VoiceBatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(preset);

        var measured = new (string SourcePath, VoiceLoudnessMeasurement? Measurement)[files.Count];
        await RunWorkersAsync(
            files,
            "测量",
            async file =>
            {
                var measurement = await MeasureOneAsync(ffmpegPath, file, preset, cancellationToken)
                    .ConfigureAwait(false);
                if (measurement is null)
                {
                    ToolboxLog.Warn($"「{Path.GetFileName(file)}」的响度没量出来（文件可能损坏或为空）。");
                }

                return measurement;
            },
            (index, value) => measured[index] = (files[index], value),
            progress,
            cancellationToken).ConfigureAwait(false);

        return VoiceLoudnessCompliance.Evaluate(preset, measured);
    }

    /// <summary>
    /// 规整一批文件：全部拉到目标响度、真峰值压到天花板以下，<b>就地覆盖</b>。
    ///
    /// <b>这里不做备份</b>：回退由「原始版」（<see cref="VoiceOriginalArchiveService"/>）负责 ——
    /// 每个工作文件固定一份最初副本，规整只覆盖工作版。上一版是「每规整一次复制一整章」，
    /// 十次之后目录就没法看了（2026-11-02 用户定案改掉）。
    /// </summary>
    public async Task<VoiceNormalizeResult> NormalizeAsync(
        string ffmpegPath,
        IReadOnlyList<string> files,
        VoiceLoudnessPreset preset,
        IProgress<VoiceBatchProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? compressFirstFiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(preset);

        if (files.Count == 0)
        {
            return new VoiceNormalizeResult(preset, []);
        }

        var outcomes = new VoiceNormalizeOutcome[files.Count];
        await RunWorkersAsync(
            files,
            "规整",
            file => NormalizeOneAsync(ffmpegPath, file, preset, cancellationToken),
            (index, value) => outcomes[index] = value!,
            progress,
            cancellationToken).ConfigureAwait(false);

        return new VoiceNormalizeResult(preset, outcomes);
    }

    private async Task RunWorkersAsync<T>(
        IReadOnlyList<string> files,
        string stage,
        Func<string, Task<T>> work,
        Action<int, T?> store,
        IProgress<VoiceBatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var total = files.Count;
        var nextIndex = -1;
        var completed = 0;
        var workerCount = Math.Min(MaxConcurrentFiles, Math.Max(1, total));

        async Task WorkerAsync()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= total)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var file = files[index];
                var value = await work(file).ConfigureAwait(false);
                store(index, value);

                var done = Interlocked.Increment(ref completed);
                progress?.Report(new VoiceBatchProgress(stage, done, total, Path.GetFileName(file)));
            }
        }

        var workers = Enumerable.Range(0, workerCount).Select(_ => WorkerAsync()).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>量一个文件。量不出来返回 null（调用方据此判「测不出」）。</summary>
    private static async Task<VoiceLoudnessMeasurement?> MeasureOneAsync(
        string ffmpegPath,
        string filePath,
        VoiceLoudnessPreset preset,
        CancellationToken cancellationToken)
    {
        var invocation = AudioFfmpegArguments.BuildLoudnessProbe(filePath, preset);
        var result = await RunFfmpegAsync(ffmpegPath, invocation, cancellationToken).ConfigureAwait(false);
        var measurement = AudioLoudnormJson.Parse(result.Output);
        if (measurement is null)
        {
            return null;
        }

        // 时长白捡：它和响度在**同一次** ffmpeg 调用的输出里，多解析一行而已。
        // 为它单独起一次进程（或加 ffprobe）纯属浪费 —— 语音页每行都要显示它。
        return ParseDuration(result.Output) is { } duration
            ? measurement with { Duration = duration }
            : measurement;
    }

    /// <summary>
    /// 从 ffmpeg 的 stderr 里读 <c>Duration: 00:00:02.40</c>。纯函数，回归里直接喂字符串断言。
    /// </summary>
    internal static TimeSpan? ParseDuration(string ffmpegOutput)
    {
        if (string.IsNullOrWhiteSpace(ffmpegOutput))
        {
            return null;
        }

        var match = DurationPattern.Match(ffmpegOutput);
        if (!match.Success)
        {
            return null;
        }

        return TimeSpan.TryParse(match.Groups["value"].Value, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// 规整一个文件：量 → 写到同目录临时件 → 替换原件 → 复量。
    /// 中途任何一步失败都只删临时件，原文件保持原样。
    /// </summary>
    private static async Task<VoiceNormalizeOutcome> NormalizeOneAsync(
        string ffmpegPath,
        string filePath,
        VoiceLoudnessPreset preset,
        CancellationToken cancellationToken)
    {
        var temporaryPath = filePath + TemporarySuffix;

        try
        {
            // ① 测量。量不出来就**不动这个文件**：就地覆盖是不可逆的，
            //    没有测量值就只能让 loudnorm 自己动态压，结果不可预期、事后也查不出来。
            //    宁可这一条报失败、原件一个字节不变。
            var probe = await RunFfmpegAsync(
                ffmpegPath,
                AudioFfmpegArguments.BuildLoudnessProbe(filePath, preset),
                cancellationToken).ConfigureAwait(false);
            var before = AudioLoudnormJson.Parse(probe.Output);
            if (before is not { IsUsable: true })
            {
                return Failure(filePath, null, "没有量出响度，已跳过（原文件未改动）", TailLines(probe.Output, 3));
            }

            var sampleRate = ParseSampleRate(probe.Output);
            if (sampleRate <= 0)
            {
                sampleRate = FallbackSampleRate;
                ToolboxLog.Warn(
                    $"「{Path.GetFileName(filePath)}」没读出采样率，按 {FallbackSampleRate} Hz 落盘（避免 loudnorm 把成品升到 192 kHz）。");
            }

            // ② 落盘到临时件
            //    compressFirst：被峰值卡住的素材先压动态，否则套多少增益都会撞回上限。
            var compressFirst = compressFirstFiles is not null &&
                compressFirstFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase);

            var write = await RunFfmpegAsync(
                ffmpegPath,
                AudioFfmpegArguments.BuildWritePass(filePath, temporaryPath, preset, before, sampleRate, compressFirst),
                cancellationToken).ConfigureAwait(false);

            if (write.ExitCode != 0)
            {
                AtomicFileWriter.TryDelete(temporaryPath);
                return Failure(filePath, before, "规整失败", TailLines(write.Output, 3));
            }

            if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length <= 44)
            {
                AtomicFileWriter.TryDelete(temporaryPath);
                return Failure(filePath, before, "规整失败", "ffmpeg 退出码是 0，但没有产出有效的音频文件。");
            }

            // ③ 替换原件。同目录 + 同卷 → File.Move 走的是「先删后改名」，
            //    中间失败只会留下临时件，不会出现半截文件。
            File.Move(temporaryPath, filePath, overwrite: true);

            // ④ 复量成品。这一步是「给用户看一眼对不对」的证据，不是判定依据；
            //    量不到不影响已经成功的替换。
            var after = await MeasureOneAsync(ffmpegPath, filePath, preset, cancellationToken).ConfigureAwait(false);
            if (before is { IsUsable: true } && after is { IsUsable: true })
            {
                ToolboxLog.Info(
                    $"规整语音「{Path.GetFileName(filePath)}」："
                    + $"{before.IntegratedLufs:0.0} → {after.IntegratedLufs:0.0} LUFS，"
                    + $"峰值 {before.TruePeakDb:0.0} → {after.TruePeakDb:0.0} dBTP。");
            }

            return new VoiceNormalizeOutcome(filePath, true, before, after, null);
        }
        catch (OperationCanceledException)
        {
            AtomicFileWriter.TryDelete(temporaryPath);
            throw;
        }
        catch (Exception error)
        {
            AtomicFileWriter.TryDelete(temporaryPath);
            ToolboxLog.Warn($"规整语音失败（{Path.GetFileName(filePath)}）：{error.Message}", error);
            return new VoiceNormalizeOutcome(filePath, false, null, null, error.Message);
        }
    }

    private static VoiceNormalizeOutcome Failure(
        string filePath,
        VoiceLoudnessMeasurement? before,
        string reason,
        string ffmpegOutput)
    {
        var tail = TailLines(ffmpegOutput, 3);
        var message = string.IsNullOrWhiteSpace(tail) ? reason : $"{reason}：{tail}";
        ToolboxLog.Warn($"规整语音失败（{Path.GetFileName(filePath)}）：{message}");
        return new VoiceNormalizeOutcome(filePath, false, before, null, message);
    }

    private static string TailLines(string text, int lineCount)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" / ", lines.TakeLast(lineCount));
    }

    /// <summary>
    /// 起一次 ffmpeg 并等它结束。<b>只走 <see cref="UnrealSyncProcessRunner"/></b>：
    /// 取消/超时先杀进程树、管道边跑边读，这两件事只有一处实现。
    /// </summary>
    private static Task<UnrealSyncProcessResult> RunFfmpegAsync(
        string ffmpegPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // ★ 重定向了输出就必须固定编码：不设的话 .NET 按父进程 OEM 代码页（中文 Windows
            //   是 936）解码，而 ffmpeg 吐的是 UTF-8 —— 素材路径里有中文时报错信息整段乱码。
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? AppContext.BaseDirectory
        };
        // 参数走 ArgumentList（.NET 自己按 Windows 规则转义），不手工拼字符串 ——
        // 语音文件名里带空格、引号、中文都靠这一层兜住。
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return UnrealSyncProcessRunner.RunAsync(
            startInfo,
            PerFileTimeout,
            "没能启动 ffmpeg（文件可能被安全软件拦了）。",
            "ffmpeg 处理超时，已中止（进程树已结束）。",
            onPoll: null,
            verdict: null,
            cancellationToken);
    }

    /// <summary>
    /// 从 <c>-i</c> 的输出头里读采样率（<c>Stream #0:0: Audio: pcm_s16le …, 44100 Hz, mono</c>）。
    ///
    /// <b>为什么非要读它</b>：loudnorm 为了测真峰值内部升采样到 192 kHz，不给 <c>-ar</c>
    /// 的话成品会被写成 192 kHz 的 wav —— 听着没问题，但一条语音的体积和导入耗时都会翻几倍，
    /// 而且和项目里其它素材不一致。读不到返回 0，调用方用兜底值。
    ///
    /// 纯函数，回归里直接喂字符串断言。
    /// </summary>
    internal static int ParseSampleRate(string ffmpegOutput)
    {
        if (string.IsNullOrWhiteSpace(ffmpegOutput))
        {
            return 0;
        }

        var match = SampleRatePattern.Match(ffmpegOutput);
        if (!match.Success)
        {
            return 0;
        }

        return int.TryParse(match.Groups["rate"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate)
            ? rate
            : 0;
    }
}
