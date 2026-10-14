using System;
using CommunityToolkit.Mvvm.ComponentModel;
using GalExcleTools.Models;

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

    public StoryTodoRowViewModel(StoryTodoNote note)
    {
        Note = note;
        _done = note.Done;
    }

    /// <summary>当前这条备注。改动会通过 <see cref="Note"/> 的替换体现出来。</summary>
    public StoryTodoNote Note { get; private set; }

    public string ChapterCode => Note.ChapterCode;

    public int RowNumber => Note.RowNumber;

    /// <summary>列表标题：第几句、在哪一章、第几小节。</summary>
    public string HeaderText => Note.HeaderText;

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
            }
        }
    }

    /// <summary>勾掉的那些在界面上灰显 —— 用透明度表达，不引新样式。</summary>
    public double RowOpacity => _done ? 0.45 : 1.0;
}
