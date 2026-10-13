using System;

namespace GalExcleTools.Services;

/// <summary>
/// 一条日志属于「哪一次流程、哪一步」。
///
/// 面板上只把步骤显示成 <c>[St3]</c> 前缀，文件（和"复制全部"）那行把 run/step 都写出来。
/// </summary>
/// <param name="RunId">批次号；不属于任何批次时是 <see cref="RuntimeLogFormat.NoRunId"/>。</param>
/// <param name="Step">第几步；和步骤无关时是 0。</param>
internal readonly record struct RuntimeLogScope(string RunId, int Step)
{
    /// <summary>没有当前批次、也和步骤无关时用的空范围。</summary>
    public static RuntimeLogScope None { get; } = new(RuntimeLogFormat.NoRunId, 0);

    /// <summary>面板上的步骤前缀；step 为 0 时是空串（不显示前缀）。</summary>
    public string StepTag => Step >= 1 ? $"St{Step}" : string.Empty;
}

/// <summary>
/// 日志的行格式。全是纯函数，所以"格式有没有真的按约定写"可以直接断言，不用启动界面。
///
/// 【为什么要单独抽出来】
/// 这一段是照零境那边搬的，它的注释把问题说得很准，原话：
/// 「以前这三样都散在 MainWindow.Logging.cs 里现拼字符串：面板行拼一次、
///   文件行再拼一次（还各自嵌了一遍 [HH:mm:ss]），而「这是哪一步、哪一次」
///   干脆没有 —— 第二步和第四步共用 [Sync Execution] 这种 tag，
///   事后翻日志分不出谁是谁。」
///
/// 2026-10-12 这边就是撞在这个坑上：同步报了个错，界面状态条上有，
/// 但日志里那条**看不出是哪一段出的** —— 也就没法拿它来定位。
/// </summary>
internal static class RuntimeLogFormat
{
    /// <summary>没有当前批次时的占位。用 <c>-</c> 而不是空串，方便一眼看出来。</summary>
    public const string NoRunId = "-";

    public const string RunHeaderPrefix = "=====";
    public const string StepStartGlyph = "▶";
    public const string StepEndGlyph = "■";

    /// <summary>批次号：<c>R-yyyyMMdd-HHmm-XXXX</c>。后四位是十六进制，同一分钟里连着点两次也能分开。</summary>
    public static string CreateRunId(DateTimeOffset now) =>
        $"R-{now:yyyyMMdd-HHmm}-{(ushort)Random.Shared.Next(0, 0x10000):x4}";

    /// <summary>
    /// 面板行：<c>[HH:mm:ss] [StN] 正文</c>；step=0 时不加前缀。
    ///
    /// 故意**不带等级文字** —— 等级由颜色表达（<c>GetLogTextStyle</c>），
    /// 前面再叠一个 <c>LogTFAC: Error:</c> 只会把正文挤到很远。
    /// </summary>
    public static string FormatPanelLine(DateTimeOffset now, RuntimeLogScope scope, string body)
    {
        var tag = scope.StepTag;
        return tag.Length == 0
            ? $"[{now:HH:mm:ss}] {body}"
            : $"[{now:HH:mm:ss}] [{tag}] {body}";
    }

    /// <summary>
    /// "复制全部"用的那行：时间戳到毫秒，固定跟 run/step。
    ///
    /// 比面板行多一个 <c>[等级]</c>：这行是**拿去给别人看**的（贴到 issue、发给同事），
    /// 而复制出去之后颜色就没了 —— 光看正文分不出哪条是错误。
    /// 这是相对零境那版的一处刻意添加。
    /// </summary>
    public static string FormatCopyLine(
        DateTimeOffset now,
        RuntimeLogScope scope,
        string level,
        string body) =>
        $"[{now:yyyy-MM-dd HH:mm:ss.fff}] run={scope.RunId} step={scope.Step} [{level}] {body}";

    /// <summary>批次首行。</summary>
    public static string FormatRunHeader(string runId, string title) =>
        $"{RunHeaderPrefix} run {runId} · {title} {RunHeaderPrefix}";

    /// <summary>进入某一步的标题行。</summary>
    public static string FormatStepStart(int step, string stepName) =>
        $"{StepStartGlyph} 第 {step} 步 · {stepName}";

    /// <summary>离开某一步的标题行。</summary>
    public static string FormatStepEnd(int step, string conclusion) =>
        $"{StepEndGlyph} 第 {step} 步 · {(string.IsNullOrWhiteSpace(conclusion) ? "结束" : conclusion)}";
}
