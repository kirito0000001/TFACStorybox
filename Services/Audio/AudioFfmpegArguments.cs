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
        int sampleRate,
        bool compressFirst = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(preset);

        var arguments = new List<string>(CommonLeadingArguments)
        {
            "-y",
            "-i", inputPath,
            "-af", compressFirst
                ? BuildCompressThenLoudnormFilter(preset)
                : measurement is { IsUsable: true } usable
                    ? BuildGainLimitFilter(preset, usable)
                    : BuildLoudnormFilter(preset, printJson: false, measurement)
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

    /// <summary>
    /// 「先压缩再归一」的串联滤镜。
    ///
    /// 【什么时候用】被峰值天花板卡住的素材：平均电平很轻、峰值却贴着上限
    /// （峰均比 &gt; 9 dB 左右）。只套增益必然撞顶，唯一出路是把动态压下来。
    ///
    /// 【为什么这时不走两遍法】<c>measured_*</c> 是在**压缩之前**量的，
    /// 压完电平全变了，再拿旧值套线性增益就是错的。
    /// 所以这里用单遍动态模式，让 loudnorm 自己边测边调 —— 精度差一档，但结果自洽。
    /// </summary>
    private static string BuildCompressThenLoudnormFilter(VoiceLoudnessPreset preset)
    {
        return string.Concat(
            "acompressor=threshold=", FormatNumber(CompressThresholdLinear),
            ":ratio=", FormatNumber(CompressRatio),
            ":attack=", FormatNumber(CompressAttackMs),
            ":release=", FormatNumber(CompressReleaseMs),
            ",loudnorm=I=", FormatNumber(preset.TargetLufs),
            ":TP=", FormatNumber(preset.PeakCeilingDbtp),
            ":LRA=", FormatNumber(TargetLra));
    }

    // 压缩参数：对话素材的保守档。threshold 低到只压"过响的那几下"，
    // ratio 2.5 不会听出抽吸感，attack 8ms 保住字头，release 100ms 让句子之间的呼吸不被拽。
    //
    // ⚠️ ffmpeg 的 acompressor.threshold 是**线性幅度**（0.000976563–1），
    // 不是 dB。0.125 ≈ −18 dBFS —— 写成 "-18dB" 会直接报参数错误。
    private const double CompressThresholdLinear = 0.125d;
    private const double CompressRatio = 2.5d;
    private const double CompressAttackMs = 8d;
    private const double CompressReleaseMs = 100d;

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

        // measurement 为 null → 什么都不加（调用方会另走增益+限幅那条）。
        // **不要传 linear=false**：那个写法在某些版本上会被当成「显式要求动态」，行为反而更跳。
        return string.Concat(parts);
    }

    /// <summary>
    /// 「一次增益 + 一个限幅器」—— 2026-11-07 起规整的唯一落盘链。
    ///
    /// 为什么不用 loudnorm：它的单遍模式按约 3 秒块做时变增益，遇到
    /// "说话—停顿—说话"的语音会把停顿抬起来、字尾拉长，听感是"回音"。
    /// 用户拿 Pymss 的响度匹配对比后确认：**只做一次增益 + 前瞻限幅**最干净。
    ///
    /// 参数沿用 Pymss 那套（前瞻 12ms / 释放 50ms），天花板按新口径抬到 −1 dBTP：
    /// 目标是 −18 LUFS、真峰值 ≤ −1 dBTP（游戏音频便携端标准）。
    /// 这个组合下大多数素材只需要**纯增益**，限幅器基本不参与 —— 音色几乎不动。
    /// </summary>
    private static string BuildGainLimitFilter(VoiceLoudnessPreset preset, VoiceLoudnessMeasurement measurement)
    {
        var gain = preset.TargetLufs - measurement.IntegratedLufs;

        // limit=0.85 ≈ −1.4 dBFS：比 −1 dBTP 的天花板留 0.4 dB 余量，
        // 因为 16bit 落盘后真峰值会再涨一点。目标改成 −18 之后这个限幅器很少触发。
        return string.Concat(
            "volume=", FormatNumber(gain), "dB,",
            "alimiter=limit=0.85:attack=12:release=50:level=false");
    }

    /// <summary>
    /// 数字一律用不变文化格式化。
    /// <b>这是必须的</b>：中文区域设置下 <c>double.ToString()</c> 会吐 <c>-12,5</c>，
    /// 而 ffmpeg 只认小数点 —— 整批滤镜参数会全部解析失败。
    /// </summary>
    internal static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
