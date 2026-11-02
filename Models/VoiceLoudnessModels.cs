using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools;

/// <summary>
/// 响度判定：有目标、有容差 → 三级。<b>容差内分两档</b>，因为「贴着边」和「差得远」修法不同。
/// </summary>
internal enum VoiceLoudnessVerdict
{
    /// <summary>|实测 − 目标| ≤ 容差的一半。稳。</summary>
    Pass,

    /// <summary>在容差内但贴着边。这一批里如果有一半是 Near，说明源素材本身就不齐。</summary>
    Near,

    /// <summary>超出容差。要修。</summary>
    Off
}

/// <summary>
/// 真峰值判定：只有上限 → <b>二元</b>。
/// 刻意没有「接近」档：超 0.1 dB 也是超 —— 转码之后就是削顶。
/// </summary>
internal enum VoicePeakVerdict
{
    Pass,
    Over
}

/// <summary>
/// 同类一致性判定：没有目标，只看离散度。
/// <see cref="NotApplicable"/> 是**必须有的第三态**：只有一条素材时算不了离散度，
/// 这时候判 Pass 会让用户以为「查过了」。
/// </summary>
internal enum VoiceConsistencyVerdict
{
    NotApplicable,
    Pass,
    TooWide
}

/// <summary>
/// 一条口径。**这是唯一的事实来源**，判定、文案、规整目标都从它取。
///
/// 语音只有一档：游戏业界惯例的<b>对白锚点</b>（−12 LUFS ±2 LU、真峰值 ≤ −3 dBTP）。
/// 隔壁的预设表里还有电影/EBU 那套（−23 LUFS、−1 dBTP），本项目没搬 ——
/// 这里做的是 Galgame 语音，按广播口径压到 −23 只会让语音比 BGM（−16）低一大截。
/// </summary>
internal sealed record VoiceLoudnessPreset(
    string DisplayName,
    double TargetLufs,
    double ToleranceLu,
    double PeakCeilingDbtp,
    string Wording,
    string Evidence)
{
    public static VoiceLoudnessPreset GameDialogue { get; } = new(
        "游戏语音 · 对白锚点",
        -12d,
        2d,
        -3d,
        "按游戏业界惯例建议范围检测（该口径没有官方标准，不说「合规」）",
        "【惯】两套中文参考量一致：语音 −12 LUFS、峰值全类 ≤ −3 dBTP");

    /// <summary>口径的一句话摘要，报告第一行就用它。</summary>
    public string Summary =>
        $"{DisplayName}：目标 {Format(TargetLufs)} LUFS"
        + $" ± {Format(ToleranceLu)} LU，"
        + $"真峰值 ≤ {Format(PeakCeilingDbtp)} dBTP";

    /// <summary>
    /// 数字一律不变文化（中文区域会吐 <c>-12,5</c>）。这里自带一份而不是复用
    /// <c>AudioFfmpegArguments.FormatNumber</c>：模型层不该反过来依赖服务层。
    /// </summary>
    private static string Format(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// 一次响度测量的结果。
///
/// 两个来源共用这一个类型：<c>loudnorm=print_format=json</c>（带门限与 offset，
/// 供两遍法回喂）与 <c>ebur128</c> 摘要。本项目只走前者 —— 一次探测就够用。
/// </summary>
internal sealed record VoiceLoudnessMeasurement(
    double IntegratedLufs,
    double TruePeakDb,
    double LoudnessRangeLu)
{
    /// <summary>loudnorm 第一遍才有：门限（阈值门）。缺了它第二遍的增益会算错。</summary>
    public double ThresholdLufs { get; init; } = double.NaN;

    /// <summary>loudnorm 第一遍才有：目标偏移量（LU），两遍法必须原样喂回第二遍。</summary>
    public double TargetOffsetLu { get; init; }

    /// <summary>量不出来（NaN）不算「通过」，调用方据此判 Off。</summary>
    public bool IsUsable => !double.IsNaN(IntegratedLufs) && !double.IsNaN(TruePeakDb);
}

/// <summary>
/// 真峰值的量化容差（dB）。
///
/// 依据（隔壁 2026-10-08 端到端实测）：loudnorm 用 <c>TP=-3</c> 限制出来的成品偶尔是
/// <b>−2.9 dBTP</b>，即超天花板 0.1 dB。这不是「超标」，是过采样真峰值测量本身的
/// 不确定度 —— BS.1770 的真峰值靠过采样重建采样间峰值，不同实现之间本来就有零点几 dB
/// 的出入（EBU R 128-2023 自己给的测量容差就是 ±0.3 dB）。判定因此给一条 0.3 dB 的带：
/// <b>带内算过</b>。不给的话，用户会看到「限制器明明开着，报告却说峰值超标」。
/// </summary>
internal static class VoicePeakTolerance
{
    public const double QuantizationDb = 0.3d;
}

/// <summary>一个文件的判定结果。</summary>
internal sealed record VoiceComplianceItem(
    string SourcePath,
    VoiceLoudnessMeasurement Measurement,
    VoiceLoudnessVerdict LoudnessVerdict,
    VoicePeakVerdict PeakVerdict)
{
    public string FileName => Path.GetFileName(SourcePath);

    /// <summary>离目标差多少（LU，<b>正数 = 实测比目标响</b>）。</summary>
    public double LoudnessDeltaLufs { get; init; }

    /// <summary>判定这一条时用的目标响度。别从 <see cref="LoudnessDeltaLufs"/> 反推（符号易错）。</summary>
    public double TargetLufs { get; init; }

    /// <summary>离峰值天花板还有多少余量（dB，负数 = 已经超了）。</summary>
    public double PeakHeadroomDb { get; init; }

    /// <summary>
    /// 「目标达不到、因为被**峰值天花板**卡住了」。
    ///
    /// 语音素材峰均比大（一句带情绪的台词常有 15~20 dB），要达成 −12 LUFS 需要真峰值
    /// 到 +3~+8 dBTP，而天花板是 −3 —— 两者物理上不可能同时满足，loudnorm 只能顶在
    /// 天花板上，于是整批落在 −16 附近、全判 Off。这时该说的是「素材峰均比太大，
    /// 要么放宽天花板、要么调低目标」，而不是「你的素材偏轻」。
    ///
    /// 判据两条都要成立：
    /// ① 目标对这个峰均比不可达：目标 + 峰均比 &gt; 天花板 + 0.3；
    /// ② 成品确实顶在天花板上：峰值余量 ≤ 0.3。
    /// 只有 ① 会把「峰均比 0 dB 的合成素材」也判成被卡住（它明明没被 limiter 压）。
    /// </summary>
    public bool IsPeakLimited
    {
        get
        {
            if (!Measurement.IsUsable || LoudnessVerdict != VoiceLoudnessVerdict.Off)
            {
                return false;
            }

            var crestFactor = Measurement.TruePeakDb - Measurement.IntegratedLufs;
            var peakNeededToHitTarget = TargetLufs + crestFactor;
            var ceiling = Measurement.TruePeakDb + PeakHeadroomDb;

            if (peakNeededToHitTarget <= ceiling + VoicePeakTolerance.QuantizationDb)
            {
                return false;
            }

            return PeakHeadroomDb <= VoicePeakTolerance.QuantizationDb;
        }
    }

    /// <summary>
    /// 这一条整体要不要修：响度出容差，或峰值超标。
    ///
    /// ★ 「被天花板卡住」的照样算要修：卡住恰恰说明<b>素材的峰均比太大</b>，
    /// 而「把峰均比压下来」正是用户该做的事。把它排除掉的后果是报告出现
    /// 「要修 0 条」却一条都没达标，比不报还糟。<see cref="IsPeakLimited"/> 的职责是
    /// <b>解释原因</b>，不是豁免。
    /// </summary>
    public bool NeedsFix => LoudnessVerdict == VoiceLoudnessVerdict.Off || PeakVerdict == VoicePeakVerdict.Over;

    /// <summary>报告里那一格文字。</summary>
    public string VerdictText
    {
        get
        {
            if (!Measurement.IsUsable)
            {
                return "测不出";
            }

            if (PeakVerdict == VoicePeakVerdict.Over)
            {
                return "峰值超";
            }

            return LoudnessVerdict switch
            {
                VoiceLoudnessVerdict.Pass => "达标",
                VoiceLoudnessVerdict.Near => "贴边",
                _ => IsPeakLimited ? "超差·卡天花板" : "超差"
            };
        }
    }
}

/// <summary>
/// 同类一致性。这是检测里最有实用价值的一段：没做过标准化的语音库，
/// 典型毛病不是「平均值差 1 LU」，而是「这一批里最响和最轻差 8~12 dB」。
/// 只报「合不合规」会把这个问题藏起来。
/// </summary>
internal sealed record VoiceConsistencySummary(
    VoiceConsistencyVerdict Verdict,
    int SampleCount,
    double ToleranceLu,
    double? SpreadLu,
    string? LoudestFile,
    double? LoudestLufs,
    string? QuietestFile,
    double? QuietestLufs)
{
    public static VoiceConsistencySummary NotApplicable(int sampleCount, double toleranceLu) =>
        new(VoiceConsistencyVerdict.NotApplicable, sampleCount, toleranceLu, null, null, null, null, null);
}

/// <summary>
/// 一次检测的完整结果。<b>三段分开报，不是一个布尔值</b>：
/// 响度三级、峰值二元、同类离散度。
/// </summary>
internal sealed record VoiceComplianceReport(
    VoiceLoudnessPreset Preset,
    IReadOnlyList<VoiceComplianceItem> Items,
    VoiceConsistencySummary Consistency)
{
    public int TotalCount => Items.Count;

    public int PassCount => Items.Count(item => item.LoudnessVerdict == VoiceLoudnessVerdict.Pass);

    public int NearCount => Items.Count(item => item.LoudnessVerdict == VoiceLoudnessVerdict.Near);

    public int OffCount => Items.Count(item => item.LoudnessVerdict == VoiceLoudnessVerdict.Off);

    public int PeakOverCount => Items.Count(item => item.PeakVerdict == VoicePeakVerdict.Over);

    public int UnmeasurableCount => Items.Count(item => !item.Measurement.IsUsable);

    public int PeakLimitedCount => Items.Count(item => item.IsPeakLimited);

    /// <summary>整批是不是「普遍被天花板卡住」（过半即算）—— 报告要专门为它出一段说明。</summary>
    public bool IsBroadlyPeakLimited => Items.Count > 0 && PeakLimitedCount * 2 > Items.Count;

    public IReadOnlyList<VoiceComplianceItem> NeedsFixItems =>
        Items.Where(item => item.NeedsFix).ToList();

    /// <summary>
    /// 测量口径声明。<b>这句话必须跟着报告一起出现</b> ——
    /// 报告里写清口径，比假装合规值钱。
    /// </summary>
    public string MeasurementCaveat =>
        "积分响度按 ITU-R BS.1770（ffmpeg loudnorm / ebur128），未做对白门控。";
}

/// <summary>
/// 「一键规整」的一条结果。失败也占一行 —— 一批里坏一个不该把整批报成失败。
/// </summary>
internal sealed record VoiceNormalizeOutcome(
    string SourcePath,
    bool Succeeded,
    VoiceLoudnessMeasurement? Before,
    VoiceLoudnessMeasurement? After,
    string? FailureReason)
{
    public string FileName => Path.GetFileName(SourcePath);
}

/// <summary>一次规整的总结果。</summary>
internal sealed record VoiceNormalizeResult(
    VoiceLoudnessPreset Preset,
    string? BackupDirectory,
    IReadOnlyList<VoiceNormalizeOutcome> Outcomes)
{
    public int SuccessCount => Outcomes.Count(item => item.Succeeded);

    public int FailureCount => Outcomes.Count(item => !item.Succeeded);

    public IReadOnlyList<VoiceNormalizeOutcome> FailedItems =>
        Outcomes.Where(item => !item.Succeeded).ToList();

    /// <summary>规整后仍没进容差的条数（成功但没达标）。</summary>
    public int StillOffCount => Outcomes.Count(item =>
        item.Succeeded &&
        item.After is { IsUsable: true } after &&
        Math.Abs(after.IntegratedLufs - Preset.TargetLufs) > Preset.ToleranceLu);
}

/// <summary>批量处理的进度快照。UI 层据此刷进度条。</summary>
internal sealed record VoiceBatchProgress(
    string Stage,
    int CompletedCount,
    int TotalCount,
    string FileName)
{
    public double OverallFraction => TotalCount == 0
        ? 0d
        : Math.Clamp((double)CompletedCount / TotalCount, 0d, 1d);
}
