using System;

namespace GalExcleTools.Services.Update;

/// <summary>一次检查该不该真的发请求。</summary>
internal enum UpdateBudgetDecision
{
    /// <summary>发请求。</summary>
    Proceed,

    /// <summary>不发请求，直接用上次的结果（10 秒内重复手点）。</summary>
    ReuseCache
}

/// <summary>预算判定的结论 + 给日志/界面的一句话。</summary>
internal sealed record UpdateBudgetVerdict(UpdateBudgetDecision Decision, string? Note = null);

/// <summary>
/// 请求预算（设计 §7.3）：只看时间和开关，不碰网络。
///
/// ★ 2026-10-06 改动：**删掉"限流后暂停自动检查到当天 24:00"这套自锁**，以及"剩余配额低于 10 就收手"。
///   那是个自动推导的闸门：一次 403 就把当天的自动检测整个关掉，还要写进 settings.json 跨重启生效
///   （用户看到的就是「已暂停自动检查（次日 00:00 之后恢复）」）。
///   参照 FantasyTools 的做法：失败就是这一次失败，下次照常检查，结论如实显示给用户。
///   现在这里只剩"10 秒内重复手点复用上次结果"这一条。
/// </summary>
internal static class UpdateRequestBudget
{
    /// <summary>一次检查最多两个 API 请求（Release 列表 + 附件列表）。写成常量是为了让它可见。</summary>
    public const int MaxRequestsPerCheck = 2;

    /// <summary>手点「检查更新」在这么多秒内直接复用上次结果（只挡"手一抖点两下"）。</summary>
    public static readonly TimeSpan ManualCacheWindow = TimeSpan.FromSeconds(10);

    /// <summary>判定这一次检查该怎么做。</summary>
    /// <param name="isManual">是不是用户手点。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="lastCheckAt">上次检查时间（没有就传 null）。</param>
    /// <param name="hasCachedResult">手上有没有上次的结果可以复用。</param>
    public static UpdateBudgetVerdict Evaluate(
        bool isManual,
        DateTimeOffset now,
        DateTimeOffset? lastCheckAt,
        bool hasCachedResult)
    {
        if (isManual
            && hasCachedResult
            && lastCheckAt is { } last
            && now >= last
            && now - last < ManualCacheWindow)
        {
            return new UpdateBudgetVerdict(
                UpdateBudgetDecision.ReuseCache,
                $"距上次检查不到 {ManualCacheWindow.TotalSeconds:0} 秒，直接使用上次结果。");
        }

        return new UpdateBudgetVerdict(UpdateBudgetDecision.Proceed);
    }

    /// <summary>把设置里的超时秒数夹到 10~600（§6.3）。</summary>
    public static int ClampTimeoutSeconds(int seconds) => seconds < 10 ? 10 : seconds > 600 ? 600 : seconds;
}
