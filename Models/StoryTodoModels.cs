using System;
using System.Collections.Generic;
using GalExcleTools.Services;

// 命名空间是 GalExcleTools（根）—— 仓库里 Models/ 下所有模型都这样，别按"文件夹名=命名空间"写。
namespace GalExcleTools;

/// <summary>
/// 一条 TODO 备注。挂在**某一章的某一句**上。
///
/// 【为什么是"句"而不是"小节"或"章"】
/// 编辑器本来就是按句走的（「第 45 / 366 句」），所以"回来查看"这件事它已经会了 ——
/// 我们只要记住是**哪一句**，点一下就跳回去。挂在小节上反而跳不准。
///
/// 【为什么不写进剧情 CSV】
/// CSV 是要**导出给 Unreal** 的（同步会把它变成数据表）。往里面加一列备注，
/// 那一列会跟着同步进工程 —— 备注是给工具箱自己看的，不该跑到游戏里。
/// 所以另存一个旁挂文件（见 <c>GalExcleTools.Services.StoryTodoStore</c>）。
///
/// 【定位认 StableId，行号只是显示】
/// 早先这里是"认章节 + 行号"，理由是"行号才是编辑器的稳定口径"—— **这个判断是错的**：
/// 在某一句前面插一句、或者删掉一句，它后面所有行的行号都会往前挪一位，
/// 于是那条备注就贴到了别的句子上，而且**一点异常都没有**。
/// 真正稳定的是 <see cref="StoryRow.StableId"/>（每行一个 GUID，插删都不动它），
/// 所以定位改成认它；<see cref="RowNumber"/> / <see cref="Section"/> 退化成
/// "最后知道的位置"，只用来显示，打开章节时会按 StableId 重算一遍。
/// </summary>
/// <param name="ChapterCode">章节代码，例如 <c>WHK-M1</c>。</param>
/// <param name="RowNumber">最后知道的行号，从 1 起（和界面上那个「第 N 句」同一个口径）。</param>
/// <param name="Section">
/// 最后知道的小节（内部 1 起号，显示时走 StorySectionDisplay）。**只用来显示，不参与定位。**
/// </param>
/// <param name="Text">备注正文。</param>
/// <param name="Done">是否已处理。勾掉的灰显。</param>
/// <param name="UpdatedAt">最后一次改动时间。</param>
internal sealed record StoryTodoNote(
    string ChapterCode,
    int RowNumber,
    int Section,
    string Text,
    bool Done,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// 这一句的稳定身份（<see cref="StoryRow.StableId"/>）。**定位只认它。**
    ///
    /// 为什么写成"带初始化器的普通属性"而不是主构造参数：
    /// 老文件（Version 1）里没有这个字段，反序列化时参数拿不到值会变成 <c>null</c>，
    /// 于是每处都要判空。带初始化器的话，JSON 里没有它就保持空串 —— 空串就是天然的
    /// "老数据，还没迁移"标记，正好走行号兜底那条路。
    /// </summary>
    public string StableId { get; init; } = string.Empty;

    /// <summary>列表里那行标题：第几句、在哪一章、第几小节。</summary>
    public string HeaderText =>
        Section > 0
            ? $"第 {RowNumber} 句 · {ChapterCode} · {StorySectionDisplay.FormatName(Section)}"
            : $"第 {RowNumber} 句 · {ChapterCode}";
}

/// <summary><c>story-todos.json</c> 的形状。留个版本号，以后改结构好办。</summary>
internal sealed class StoryTodoFile
{
    /// <summary>1 = 只按行号定位；2 = 带 StableId 定位（老文件读进来还是 1，迁移后写成 2）。</summary>
    public int Version { get; set; } = 2;

    public List<StoryTodoNote> Notes { get; set; } = [];
}
