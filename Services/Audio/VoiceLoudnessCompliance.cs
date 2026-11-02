using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

/// <summary>
/// 响度判定。**纯逻辑**：给一批「已经量好的测量值」+ 一条口径，出三段判定。
///
/// 刻意不在这里起 ffmpeg：测量由 <see cref="VoiceBatchProcessor.MeasureAsync"/> 负责，
/// 这一层只做判断 —— 所以能脱离 ffmpeg 直接回归（喂几个数字就能断言）。
/// </summary>
internal static class VoiceLoudnessCompliance
{
    /// <summary>
    /// 同类一致性的惯例容差（LU）。业界口径：「每种声音的 RMS 大约有上下 2 dB 的允许差距空间」。
    /// </summary>
    public const double ConsistencyToleranceLu = 2.0d;

    /// <summary>
    /// 判定一批。
    ///
    /// <paramref name="offsetLu"/> 是整批的<b>上下幅度偏移</b>（让用户能在口径基础上
    /// 整体压一点或抬一点）。它只作用在目标上，不动容差。
    /// </summary>
    public static VoiceComplianceReport Evaluate(
        VoiceLoudnessPreset preset,
        IReadOnlyList<(string SourcePath, VoiceLoudnessMeasurement? Measurement)> measured,
        double offsetLu = 0d)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(measured);

        var target = preset.TargetLufs + offsetLu;
        var items = new List<VoiceComplianceItem>(measured.Count);

        foreach (var (sourcePath, measurement) in measured)
        {
            // 量不出来也要占一行：**不许悄悄丢掉**，否则「报告说 12 条全达标」
            // 而文件夹里明明有 15 条，用户会以为程序漏扫。
            var value = measurement ?? new VoiceLoudnessMeasurement(double.NaN, double.NaN, double.NaN);
            items.Add(new VoiceComplianceItem(
                sourcePath,
                value,
                ClassifyLoudness(value.IntegratedLufs, target, preset.ToleranceLu),
                ClassifyPeak(value.TruePeakDb, preset.PeakCeilingDbtp))
            {
                LoudnessDeltaLufs = value.IntegratedLufs - target,
                PeakHeadroomDb = preset.PeakCeilingDbtp - value.TruePeakDb,
                // 判定用的目标存下来：IsPeakLimited 要用它，而不是从 delta 反推（反推会把符号搞反）。
                TargetLufs = target
            });
        }

        return new VoiceComplianceReport(preset, items, SummarizeConsistency(items, preset.ToleranceLu));
    }

    /// <summary>
    /// 响度三级。<b>容差的一半</b>以内算 Pass：口径说的是「上下 2 dB 的允许差距空间」，
    /// 那么正好卡在 ±2 的边上就不该算「稳」。
    /// </summary>
    internal static VoiceLoudnessVerdict ClassifyLoudness(double measuredLufs, double targetLufs, double toleranceLu)
    {
        if (double.IsNaN(measuredLufs) || double.IsNaN(targetLufs) || toleranceLu <= 0d)
        {
            // 量不出来就是**没判过**，不能当成通过。归到 Off 让用户看见。
            return VoiceLoudnessVerdict.Off;
        }

        var delta = Math.Abs(measuredLufs - targetLufs);
        if (delta <= toleranceLu * 0.5d)
        {
            return VoiceLoudnessVerdict.Pass;
        }

        return delta <= toleranceLu ? VoiceLoudnessVerdict.Near : VoiceLoudnessVerdict.Off;
    }

    /// <summary>
    /// 峰值二元。量不出来（NaN）判 <see cref="VoicePeakVerdict.Over"/> —— 同样不许当通过。
    /// 天花板之上留 <see cref="VoicePeakTolerance.QuantizationDb"/> 的量化带（理由见那个常量的注释）。
    /// </summary>
    internal static VoicePeakVerdict ClassifyPeak(double measuredDbtp, double ceilingDbtp)
    {
        if (double.IsNaN(measuredDbtp))
        {
            return VoicePeakVerdict.Over;
        }

        return measuredDbtp <= ceilingDbtp + VoicePeakTolerance.QuantizationDb
            ? VoicePeakVerdict.Pass
            : VoicePeakVerdict.Over;
    }

    /// <summary>
    /// 同类一致性：极差 vs 惯例。极差上限 = 2 × 单条容差。
    /// 推导：口径说「每种声音相对目标各自 ±2」，那么同批里最响那条可以到 +2、
    /// 最轻那条可以到 −2，于是极差上限就是 4 LU。拿单条容差（2 LU）直接当极差上限
    /// 会过严，会把一批正常素材全报成「太散」。
    /// <b>样本数 &lt; 2 时判 NotApplicable</b>，而不是 Pass —— 只有一条素材时「通过」是假信息。
    /// </summary>
    internal static VoiceConsistencySummary SummarizeConsistency(
        IReadOnlyList<VoiceComplianceItem> items,
        double toleranceLu)
    {
        var usable = items
            .Where(item => !double.IsNaN(item.Measurement.IntegratedLufs))
            .ToList();

        if (usable.Count < 2)
        {
            return VoiceConsistencySummary.NotApplicable(usable.Count, toleranceLu);
        }

        var loudest = usable.MaxBy(item => item.Measurement.IntegratedLufs)!;
        var quietest = usable.MinBy(item => item.Measurement.IntegratedLufs)!;
        var spread = loudest.Measurement.IntegratedLufs - quietest.Measurement.IntegratedLufs;
        var ceiling = toleranceLu * 2d;

        return new VoiceConsistencySummary(
            spread <= ceiling ? VoiceConsistencyVerdict.Pass : VoiceConsistencyVerdict.TooWide,
            usable.Count,
            ceiling,
            spread,
            loudest.FileName,
            loudest.Measurement.IntegratedLufs,
            quietest.FileName,
            quietest.Measurement.IntegratedLufs);
    }
}
