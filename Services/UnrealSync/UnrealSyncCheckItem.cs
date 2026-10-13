using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools.Services;

/// <summary>提醒：能同步，但结果可能不是你要的。</summary>
internal enum UnrealSyncCheckSeverity
{
    /// <summary>拦路：这一项不过，同步按钮是灰的。</summary>
    Error,

    /// <summary>提醒：能同步，但结果可能不是你要的。</summary>
    Warning,
}

/// <summary>
/// 这一项如果"可更换"，换的是什么。
///
/// 引擎和工程这两项不是"去 Unreal 里建一个"能解决的 —— 它们填错了要重新选文件。
/// 所以它们除了"点击复制"之外还要多一个「更换」入口；
/// 其余各项（目录、资产）按零境的做法，点一下把正确名字复制走，去 Unreal 里建。
/// </summary>
internal enum UnrealSyncCheckReplaceTarget
{
    /// <summary>不可更换：只能复制名字去建。</summary>
    None,

    /// <summary>换引擎（选 UnrealEditor.exe / UnrealEditor-Cmd.exe）。</summary>
    Engine,

    /// <summary>换工程（选 .uproject）。</summary>
    Project,
}

/// <summary>
/// 这一项查的是什么。**只影响"合规时那句话怎么说"** ——
/// 目录说"目录已经存在。"、资产报类型、文件说"已找到。"，
/// 三类混着用同一句话读起来会很别扭。
/// </summary>
internal enum UnrealSyncCheckKind
{
    /// <summary>目录。</summary>
    Directory,

    /// <summary>资产（.uasset），有类型可言。</summary>
    Asset,

    /// <summary>普通文件（引擎可执行、.uproject、.git 之类）。</summary>
    File,
}

/// <summary>
/// 一条检测项。字段与派生属性都照零境的 <c>UnrealPublishFoundationCheckItem</c> 来。
///
/// 【为什么四个字段都要留着】
/// 我上一版把「期望路径 / 实际路径 / 期望类型 / 实际类型」压成了「路径 + 一句说明」，
/// 理由是"合规时期望等于实际、显示两遍很啰嗦"。那个理由只在**目录**这一类上成立：
/// 资产那一类恰恰要靠类型说话 ——「期望 WidgetBlueprint，实际是 Blueprint」，
/// 这条信息一压就没了，而它正是最难查的那类错。
///
/// 【显示逻辑为什么写在模型里】
/// 界面上那三行字（标题 / 期望 / 说明）和"复制什么"都是从这几个字段**派生**出来的。
/// 放在模型里，界面就只是绑定，不用在 XAML 或 VM 里各算一遍。
/// </summary>
/// <param name="DisplayName">给人看的名字，例如「角色 Item」「背景图目录」。</param>
/// <param name="ExpectedPath">它**应该**在哪：磁盘路径，或 <c>/Game/...</c> 对象路径。</param>
/// <param name="ActualPath">实际找到在哪；没找到就是空串。</param>
/// <param name="IsCompliant">合不合规。</param>
/// <param name="Problem">不合规的原因（「类型不对」「缺少 CharVoice」）；合规时为空串。</param>
/// <param name="ExpectedType">期望的资产类型（Blueprint / WidgetBlueprint / DataTable…）；目录类为空。</param>
/// <param name="ActualType">从磁盘读到的类型；**读不到就是空串，一律当"不判"**。</param>
/// <param name="Severity">不合规时算拦路还是提醒。</param>
/// <param name="ReplaceTarget">可更换时换什么；见 <see cref="UnrealSyncCheckReplaceTarget"/>。</param>
/// <param name="Kind">查的是目录 / 资产 / 文件；见 <see cref="UnrealSyncCheckKind"/>。</param>
internal sealed record UnrealSyncCheckItem(
    string DisplayName,
    string ExpectedPath,
    string ActualPath,
    bool IsCompliant,
    string Problem = "",
    string ExpectedType = "",
    string ActualType = "",
    UnrealSyncCheckSeverity Severity = UnrealSyncCheckSeverity.Error,
    UnrealSyncCheckReplaceTarget ReplaceTarget = UnrealSyncCheckReplaceTarget.None,
    UnrealSyncCheckKind Kind = UnrealSyncCheckKind.Directory)
{
    /// <summary>右侧那两个字。不合规时直接显示原因，比"不合规"三个字有用。</summary>
    public string StatusText =>
        IsCompliant ? "合规" : string.IsNullOrWhiteSpace(Problem) ? "不合规" : Problem;

    /// <summary>
    /// **复制用的「正确名称」** —— 这是整张卡点击后复制的东西。
    ///
    /// 它不是路径，是**叶子名字**：从
    /// <c>/Game/GameActor2D/Misaka/Misaka_AnimBP.Misaka_AnimBP</c>
    /// 里取 <c>Misaka_AnimBP</c>（再把包内名那段 <c>.Misaka_AnimBP</c> 去掉）。
    ///
    /// 因为用户拿它是去 Unreal 里**新建**资产 —— 新建对话框里填的就是这个名字，
    /// 路径由他所在的那个目录决定。复制一整条路径粘进去是建不出来的
    /// （我第一版就是复制路径，等于这个功能白做）。
    /// </summary>
    public string CorrectName
    {
        get
        {
            var leaf = ExpectedPath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
            var dotIndex = leaf.IndexOf('.', StringComparison.Ordinal);
            return dotIndex > 0 ? leaf[..dotIndex] : leaf;
        }
    }

    /// <summary>卡片第二行：期望路径，有类型就带上（「… · 类型 WidgetBlueprint」）。</summary>
    public string ExpectedSummaryText => string.IsNullOrWhiteSpace(ExpectedType)
        ? ExpectedPath
        : $"{ExpectedPath} · 类型 {ExpectedType}";

    /// <summary>
    /// 卡片第三行：**查到了什么**（合规）或**哪里不对**（不合规）。
    ///
    /// 合规的资产报类型、合规的目录报"目录已经存在。"、合规的文件报"已找到。" ——
    /// 资产那句让你一眼看出"它确实是我要的那种资产"，另外两句只是确认在。
    /// 读不到类型时退成"资产已经存在。"，不会显示成一句空话。
    /// </summary>
    public string DetailText
    {
        get
        {
            if (IsCompliant)
            {
                return Kind switch
                {
                    UnrealSyncCheckKind.Directory => "目录已经存在。",
                    UnrealSyncCheckKind.File => "已找到。",
                    _ => string.IsNullOrWhiteSpace(ActualType) ? "资产已经存在。" : $"资产类型：{ActualType}",
                };
            }

            if (string.IsNullOrWhiteSpace(ActualPath))
            {
                // 按类别说人话：引擎那一项原来也跟着说"未找到对应目录或资产"，
                // 可它既不是目录也不是资产，读起来很别扭。
                return Kind switch
                {
                    UnrealSyncCheckKind.File => "没找到这个文件。",
                    UnrealSyncCheckKind.Asset => "未找到对应资产。",
                    _ => "未找到对应目录。",
                };
            }

            return string.IsNullOrWhiteSpace(ActualType)
                ? $"当前位于：{ActualPath}"
                : $"当前位于：{ActualPath}；类型：{ActualType}";
        }
    }
}

/// <summary>同步前检测的结果。</summary>
internal sealed record UnrealSyncPreflightResult(
    bool CanSync,
    IReadOnlyList<UnrealSyncCheckItem> Items,
    string SummaryText);
