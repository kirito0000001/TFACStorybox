using System;
using System.Collections.Generic;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 启动热更新覆盖器需要的一切（设计 §9.3）。
/// <para>
/// 参数刻意做成**扁平列表**而不是一堆字段：updater 的命令行是跨语言契约，
/// 用例要能逐字断言，壳里也只是转发，多一层结构没有好处。
/// </para>
/// </summary>
internal sealed record UpdaterLaunchRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string ScriptPath,
    string SignalPath);

/// <summary>
/// 起进程的那一步。<see cref="UpdateLaunchService"/> 负责准备与等待，
/// 但**不自己开进程** —— 回归守卫 <c>ProcessOrchestrationIsNotDuplicated</c> 禁止
/// <c>Services/**</c> 里出现起进程的调用（整个仓库里只有界面壳允许起进程；连注释里写出那个调用名
/// 都会被守卫当字面量抓到），所以这里只留一个接口，由 <c>MainWindow.*.cs</c> 实现。
/// </summary>
internal interface IUpdaterProcessLauncher
{
    /// <summary>起进程。失败时 <paramref name="error"/> 给出人能看懂的原因，不要抛。</summary>
    bool TryStart(UpdaterLaunchRequest request, out int processId, out string error);
}
