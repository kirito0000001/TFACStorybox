using System;
using System.Collections.Generic;
using System.Globalization;

namespace GalExcleTools.Services;

/// <summary>
/// 拼 ffmpeg 命令行。<b>全是纯函数</b>：给一组参数，出一串 argv，不碰磁盘、不起进程。
///
/// ★ <b>一个文件最多两段，而且两段施加的滤镜必须完全一致</b>：
/// <code>
///   ① 测量   源        loudnorm=…:print_format=json，只读不写（-f null）
///   ② 落盘   源 → 成品 loudnorm=… + measured_*（线性套增益）
/// </code>
/// 为什么不能「量的时候顺手带上别的滤镜、写的时候再带一次」：
/// <b>滤镜施加两次 = 时间轴/能量被改两次</b>，测量窗口和成品不是同一段音频，
/// 增益必然算偏（隔壁实测偏轻 0.6~2.4 dB，而且看起来「程序没报错、就是不太对」）。
/// 所以这里**不做裁静音**：一期只做响度规整，两段之间没有任何会改变波形的中间步骤。
///
/// ★ <b>落盘必须显式给 <c>-ar</c></b>：loudnorm 为了测真峰值内部升采样到 192 kHz，
/// 不给输出采样率的话成品会变成 192 kHz 的 wav —— 听着没问题，但 Unreal 里
/// 一条语音占的空间、导入耗时都会跟着变，而且和其它素材不一致。
/// 采样率由调用方从 <c>-i</c> 的头信息里读出来原样喂回（见
/// <see cref="VoiceBatchProcessor.ParseSampleRate"/>）。
/// </summary>
internal static class AudioFfmpegArguments
{
    /// <summary>
    /// 响度范围目标（LU）。语音是短素材，实测 LRA 通常 0~3，这个值只在
    /// 「线性增益会把峰值顶穿天花板、只能退化成动态压缩」时起作用 ——
    /// 取 11（EBU R128 对一般节目的默认）意味着**尽量少压**，保住语气起伏。
    /// </summary>
    public const double TargetLra = 11d;

    private const string NullSink = "-";

    /// <summary>日志里不刷帧、不留版本横幅 —— 但**不静音**，我们要从 stderr 里读测量值。</summary>
    private static readonly string[] CommonLeadingArguments =
    [
        "-hide_banner",
        "-nostats",
        "-nostdin"
    ];

    /// <summary>
    /// ① 测量：只读。产出 JSON 落在 stderr，调用方用
    /// <see cref="AudioLoudnormJson.Parse"/> 解。
    /// <c>-f null -</c> 表示只走一遍滤镜、什么都不写。
    /// </summary>
    public static IReadOnlyList<string> BuildLoudnessProbe(string inputPath, VoiceLoudnessPreset preset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(preset);

        var arguments = new List<string>(CommonLeadingArguments)
        {
            "-i", inputPath,
            "-af", BuildLoudnormFilter(preset, printJson: true),
            "-f", "null",
            NullSink
        };
        return arguments;
    }

    /// <summary>
    /// ② 落盘：按量到的值套增益。输出一律 <c>pcm_s16le</c>（Unreal SoundWave 导入后
    /// 内部就是这个形态，最稳的一档）。
    ///
    /// <paramref name="measurement"/> 为 null 时退化成<b>单遍动态模式</b>
    /// （loudnorm 自己边测边调）—— 精度差一档，但不会因为一次测量失败就整条不做。
    /// </summary>
    public static IReadOnlyList<string> BuildWritePass(
        string inputPath,
        string outputPath,
        VoiceLoudnessPreset preset,
        VoiceLoudnessMeasurement? measurement,
        int sampleRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(preset);

        var arguments = new List<string>(CommonLeadingArguments)
        {
            "-y",
            "-i", inputPath,
            "-af", BuildLoudnormFilter(preset, printJson: false, measurement)
        };

        if (sampleRate > 0)
        {
            arguments.Add("-ar");
            arguments.Add(sampleRate.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("-c:a");
        arguments.Add("pcm_s16le");
        arguments.Add(outputPath);
        return arguments;
    }

    private static string BuildLoudnormFilter(
        VoiceLoudnessPreset preset,
        bool printJson,
        VoiceLoudnessMeasurement? measurement = null)
    {
        var parts = new List<string>
        {
            "loudnorm=I=" + FormatNumber(preset.TargetLufs),
            ":TP=" + FormatNumber(preset.PeakCeilingDbtp),
            ":LRA=" + FormatNumber(TargetLra)
        };

        if (printJson)
        {
            parts.Add(":print_format=json");
            return string.Concat(parts);
        }

        if (measurement is { } measured)
        {
            // 两遍法：linear=true + 全套 measured_*，这一遍是**线性套增益**，不做动态压缩。
            // measured_thresh 缺了增益会算错，所以测量那一步必须把门限一起读出来。
            parts.Add(":linear=true");
            parts.Add(":measured_I=" + FormatNumber(measured.IntegratedLufs));
            parts.Add(":measured_TP=" + FormatNumber(measured.TruePeakDb));
            parts.Add(":measured_LRA=" + FormatNumber(measured.LoudnessRangeLu));
            parts.Add(":measured_thresh=" + FormatNumber(measured.ThresholdLufs));
            parts.Add(":offset=" + FormatNumber(measured.TargetOffsetLu));
        }

        // measurement 为 null → 什么都不加，让它走自带的单遍动态模式。
        // **不要传 linear=false**：那个写法在某些版本上会被当成「显式要求动态」，行为反而更跳。
        return string.Concat(parts);
    }

    /// <summary>
    /// 数字一律用不变文化格式化。
    /// <b>这是必须的</b>：中文区域设置下 <c>double.ToString()</c> 会吐 <c>-12,5</c>，
    /// 而 ffmpeg 只认小数点 —— 整批滤镜参数会全部解析失败。
    /// </summary>
    internal static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
