using System;

namespace GalExcleTools.ViewModels;

/// <summary>
/// TODO 备注列表里的一行。
///
/// 为什么要有这么个小 VM：那一行的「已处理」勾选框是**双向绑定**的 ——
/// 勾一下要立刻改到数据里并触发存盘。纯 record 做不到这件事。
///
/// 【列表为什么不按"已处理"重排】
/// 直觉上想把勾掉的挪到后面，但那样每勾一下**整张列表都会跳**，
/// 定位反而更难。所以保持按章节 / 行号稳定排序，勾掉的只是**灰掉**。
/// 想专门看待办，看那些不灰的就行。
/// </summary>
internal sealed partial class StoryTodoRowViewModel : ObservableObject
{
    private bool _done;

    public StoryTodoRowViewModel(StoryTodoNote note, bool isOrphaned = false)
    {
        Note = note;
        _done = note.Done;
        IsOrphaned = isOrphaned;
    }

    /// <summary>当前这条备注。改动会通过 <see cref="Note"/> 的替换体现出来。</summary>
    public StoryTodoNote Note { get; private set; }

    public string ChapterCode => Note.ChapterCode;

    public int RowNumber => Note.RowNumber;

    /// <summary>定位用的稳定身份（<see cref="StoryRow.StableId"/>）。空串 = 还没迁移的老备注。</summary>
    public string StableId => Note.StableId;

    /// <summary>
    /// 这一句在本章里已经找不到了 —— 通常是那一句被删掉了。
    ///
    /// 备注本身**不删**（"我给这句写了备注，然后把这句删了"是正常操作，
    /// 备注里往往还写着为什么要删），但要说清楚它没地方可跳，
    /// 不能让它悄悄留着旧行号、点一下跳到别的句子上。
    /// </summary>
    public bool IsOrphaned { get; }

    /// <summary>列表标题：第几句、在哪一章、第几小节；这一句已经没了就明说。</summary>
    public string HeaderText => IsOrphaned ? $"{Note.HeaderText} · 该句已删除" : Note.HeaderText;

    /// <summary>备注正文。列表上显示的这个是**只读**的 —— 改内容在右边那个文本框里改。</summary>
    public string BodyText => Note.Text;

    public bool Done
    {
        get => _done;
        set
        {
            if (SetProperty(ref _done, value))
            {
                Note = Note with { Done = value, UpdatedAt = DateTimeOffset.Now };
                // 灰显是算出来的，得跟着一起通知 —— 不通知的话勾了不会变灰。
                OnPropertyChanged(nameof(RowOpacity));
            }
        }
    }

    /// <summary>勾掉的那些、以及已经无句可跳的那些，在界面上灰显 —— 用透明度表达，不引新样式。</summary>
    public double RowOpacity => _done || IsOrphaned ? 0.45 : 1.0;
}
