using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

/// <summary>进度条上分配给某个阶段的区间。</summary>
internal readonly record struct UnrealSyncProgressBand(double Start, double End)
{
    /// <summary>把阶段内部的 0-100 子进度映射到这一段。</summary>
    public double At(double subPercent) =>
        Start + (End - Start) * Math.Clamp(subPercent, 0, 100) / 100d;
}

/// <summary>
/// 按阶段耗时分配同步进度条的百分比。
///
/// 【为什么需要它】
/// 原来是一条流程里散落着硬编码的百分比（4 / 5 / 10 / 12 / 20 / 30 / 40 / 48 / 55 / 95 / 100）。
/// 那种写法有两个毛病：
/// 一是**阶段耗时一变，数字就骗人** —— 「同步前本地提交」在 LFS 工程上可能跑几分钟，
/// 却固定占 20%→30%，用户盯着一条十分钟不动的进度条只会以为卡死；
/// 二是**各阶段的耗时比例差得很远**（写清单不到一秒，Unreal 执行要几十秒），
/// 平摊会让快的阶段一闪而过、慢的阶段一路龟速。
///
/// 【权重从哪来】
/// 照参考项目（零境的 WorkflowProgressPlan）的做法：取**实测耗时**当权重，
/// 而**不抹平**某一段特别长的事实 —— 那一段本来就该占掉进度条的一大截。
/// 下面几个数是估算（2026-10-12），跑过几次之后应该按真实耗时调。
///
/// 【收尾为什么留 2%】
/// 最后还有写同步状态、刷新界面这些零碎动作，不参与权重分配，
/// 否则走到 100% 之后进度条还挂着，看起来像没完。
/// </summary>
internal sealed class UnrealSyncProgressPlan
{
    /// <summary>差异检测：读全部素材 + 逐文件算哈希。</summary>
    public const string Detect = "detect";

    /// <summary>关闭已打开的编辑器（只有用户选了"关闭后同步"才有这一段）。</summary>
    public const string CloseEditors = "close-editors";

    /// <summary>同步前的全量本地提交。**耗时最不可控的一段**（LFS 工程上 add -A 可能几分钟）。</summary>
    public const string LocalCommit = "local-commit";

    /// <summary>写同步清单 + 写 Unreal Python 脚本。很快。</summary>
    public const string Prepare = "prepare";

    /// <summary>Unreal 执行：在线（远程执行）或离线（冷启无头编辑器）。脚本自己会报 0-100。</summary>
    public const string Execute = "execute";

    private const double FinalizeReserve = 2d;

    private readonly Dictionary<string, UnrealSyncProgressBand> _bands;
    private readonly List<string> _order;

    private UnrealSyncProgressPlan(IReadOnlyList<(string Phase, double Weight)> phases)
    {
        var total = phases.Sum(item => item.Weight);
        _bands = new Dictionary<string, UnrealSyncProgressBand>(StringComparer.OrdinalIgnoreCase);
        _order = phases.Select(item => item.Phase).ToList();
        var cursor = 0d;
        var usable = 100d - FinalizeReserve;
        foreach (var (phase, weight) in phases)
        {
            var span = total <= 0 ? 0 : usable * weight / total;
            _bands[phase] = new UnrealSyncProgressBand(cursor, cursor + span);
            cursor += span;
        }
    }

    /// <summary>这条流程一共几个阶段，用于「阶段 N/M」。</summary>
    public int PhaseCount => _order.Count;

    /// <summary>某个阶段是第几个，从 1 开始；不认识的阶段返回 1。</summary>
    public int PhaseNumber(string phase)
    {
        var index = _order.FindIndex(item => string.Equals(item, phase, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 1 : index + 1;
    }

    /// <summary>「阶段 2/5 · 正在暂存改动」里的前半截。</summary>
    public string Caption(string phase, string title) =>
        PhaseCount <= 1 ? title : $"阶段 {PhaseNumber(phase)}/{PhaseCount} · {title}";

    /// <summary>取某一段的区间。不认识的阶段返回整条（除了收尾那点尾巴）。</summary>
    public UnrealSyncProgressBand this[string phase] =>
        _bands.TryGetValue(phase, out var band) ? band : new UnrealSyncProgressBand(0, 100 - FinalizeReserve);

    /// <summary>
    /// 一次完整同步的阶段与权重（估算，跑过几次后按实测调）。
    ///
    /// 差异检测 ≈10s（要读完素材并逐个算哈希）、关闭编辑器 ≈3s、
    /// 同步前本地提交 ≈25s（首次在 LFS 工程上会更久）、写清单与脚本 ≈2s、
    /// Unreal 执行 ≈60s（离线冷启；在线通常只要几秒到十几秒）。
    /// </summary>
    public static UnrealSyncProgressPlan ForSync(bool includesCloseEditors) =>
        new(BuildPhases(includesCloseEditors));

    /// <summary>只做差异检测：唯一的重活就是那次全量扫描。</summary>
    public static UnrealSyncProgressPlan ForDetection() =>
        new([(Detect, 100d)]);

    private static List<(string, double)> BuildPhases(bool includesCloseEditors)
    {
        var phases = new List<(string, double)> { (Detect, 10d) };
        if (includesCloseEditors)
        {
            phases.Add((CloseEditors, 3d));
        }

        phases.Add((LocalCommit, 25d));
        phases.Add((Prepare, 2d));
        phases.Add((Execute, 60d));
        return phases;
    }
}
