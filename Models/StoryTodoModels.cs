using System;

namespace GalExcleTools.Models;

/// <summary>
/// 一条 TODO 备注。挂在**某一章的某一句**上。
///
/// 【为什么是"句"而不是"小节"或"章"】
/// 编辑器本来就是按句走的（「第 45 / 366 句」），所以"回来查看"这件事它已经会了 ——
/// 我们只要记住行号，点一下就跳回去。挂在小节上反而跳不准。
///
/// 【为什么不写进剧情 CSV】
/// CSV 是要**导出给 Unreal** 的（同步会把它变成数据表）。往里面加一列备注，
/// 那一列会跟着同步进工程 —— 备注是给工具箱自己看的，不该跑到游戏里。
/// 所以另存一个旁挂文件（见 <see cref="Services.StoryTodoStore"/>）。
/// </summary>
/// <param name="ChapterCode">章节代码，例如 <c>WHK-M1</c>。</param>
/// <param name="RowNumber">第几句，从 1 起（和界面上那个「第 N 句」同一个口径）。</param>
/// <param name="Section">
/// 写备注时在第几小节。**只用来显示，不参与定位。**
///
/// 为什么不拿它当键：小节会被重新拆分（「创建新小节」按钮就是把当前句拆出去），
/// 键里带上它的话，每拆一次小节，那一章后面的备注就全成孤儿了。
/// 定位只认「章节 + 行号」——这两样才是编辑器的稳定口径。
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
    /// <summary>列表里那行标题：第几句、在哪一章。</summary>
    public string HeaderText =>
        Section > 0
            ? $"第 {RowNumber} 句 · {ChapterCode} · 第 {Section} 小节"
            : $"第 {RowNumber} 句 · {ChapterCode}";
}

/// <summary><c>story-todos.json</c> 的形状。留个版本号，以后改结构好办。</summary>
internal sealed class StoryTodoFile
{
    public int Version { get; set; } = 1;

    public List<StoryTodoNote> Notes { get; set; } = [];
}
