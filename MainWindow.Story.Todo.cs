using System;
using System.Collections.Generic;
using System.Linq;
using GalExcleTools.Models;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools;

/// <summary>
/// 剧情编辑器右栏那个「TODO 备注」。
///
/// 【它是什么】
/// 给**当前这一句**写个备注，回头能在列表里点一下跳回来。
/// 备注存在旁挂文件里（<see cref="StoryTodoStore"/>），**不进剧情 CSV** ——
/// CSV 是要导出给 Unreal 的，备注不该跟着跑到游戏里去。
///
/// 【为什么是"句"】
/// 编辑器本来就按句走（「第 45 / 366 句」），跳转能力是现成的 ——
/// 我们只要记住行号，跳回去这件事就能直接复用 <c>NavigateToStoryRow</c>。
/// 挂在小节上反而跳不准。
///
/// 【行号会漂】
/// 中间插一句、删一句，后面的行号就全错位了。所以每条备注都存了
/// <see cref="StoryTodoNote.Snippet"/>（写的时候那句的开头）——
/// 列表里显示出来，跳歪了也能一眼认出来"原来说的是哪句"。
/// 这比假装行号永远准要诚实。
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<StoryTodoNote> _storyTodoNotes = [];
    private bool _isStoryTodoDirty;
    private bool _isStoryTodoLoaded;

    /// <summary>
    /// 换句时刷新备注面板 —— 但**只在备注已经载入过之后**才刷。
    ///
    /// 为什么要这道闸门：<c>NavigateToStoryRow</c> 在"打开章节"的过程中也会被调到，
    /// 而那时候备注还没载入（<c>LoadStoryTodos</c> 在后面）。
    /// 提前刷一次会把**上一章**的内容当成这一章的 —— 那正是最难查的一种错。
    /// </summary>
    private void RefreshStoryTodoForCurrentRowIfLoaded()
    {
        if (!_isStoryTodoLoaded)
        {
            return;
        }

        RefreshStoryTodoPanel();
    }

    /// <summary>当前句对应的那条备注；没有就返回 null。</summary>
    private StoryTodoNote? FindStoryTodoForCurrentRow()
    {
        if (_currentStoryChapter is null || _storyRows.Count == 0)
        {
            return null;
        }

        var rowNumber = _currentStoryRowIndex + 1;
        return _storyTodoNotes.FirstOrDefault(note =>
            note.RowNumber == rowNumber &&
            string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>换项目 / 打开章节时调用：把备注整个读回来。</summary>
    private void LoadStoryTodos()
    {
        _storyTodoNotes.Clear();
        _storyTodoNotes.AddRange(StoryTodoStore.Load(_currentProject?.Path));
        _isStoryTodoDirty = false;
        _isStoryTodoLoaded = true;
        RefreshStoryTodoPanel();
    }

    /// <summary>
    /// 把"当前句的备注"刷到右栏：文本框、状态行、列表。
    ///
    /// 换句、换章、载入之后都要调一次 —— 否则文本框里还留着上一句的内容，
    /// 一编辑就会把它写到**错误的句子**上。那种错很难发现，所以宁可多刷几次。
    /// </summary>
    private void RefreshStoryTodoPanel()
    {
        if (_storyEditorViewModel is null)
        {
            return;
        }

        _storyEditorViewModel.IsLoadingTodo = true;
        try
        {
            var note = FindStoryTodoForCurrentRow();
            _storyEditorViewModel.TodoText = note?.Text ?? string.Empty;

            var pending = _storyTodoNotes.Count(item => !item.Done);
            _storyEditorViewModel.TodoStatusText = _storyTodoNotes.Count == 0
                ? "还没有任何备注"
                : note is null
                    ? $"本句没有备注 · 全书待办 {pending} 条 / 共 {_storyTodoNotes.Count} 条"
                    : $"本句有备注 · 全书待办 {pending} 条 / 共 {_storyTodoNotes.Count} 条";
        }
        finally
        {
            _storyEditorViewModel.IsLoadingTodo = false;
        }

        RefreshStoryTodoList();
    }

    /// <summary>重建备注列表。按章节 + 行号稳定排序，勾掉的只灰显、不重排。</summary>
    private void RefreshStoryTodoList()
    {
        if (_storyEditorViewModel is null)
        {
            return;
        }

        var rows = _storyTodoNotes
            .OrderBy(note => note.ChapterCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(note => note.RowNumber)
            .Select(note => new StoryTodoRowViewModel(note))
            .ToList();

        _storyEditorViewModel.TodoItems.Clear();
        foreach (var row in rows)
        {
            _storyEditorViewModel.TodoItems.Add(row);
        }
    }

    /// <summary>
    /// 备注列表里的当前内容（含"已处理"的最新勾选状态）。
    ///
    /// **必须从列表行里读**，不能直接用 <c>_storyTodoNotes</c> ——
    /// 勾选是双向绑定到行上的，字段变化只落在行对象里。
    /// </summary>
    private List<StoryTodoNote> CollectStoryTodoNotesFromRows() =>
        _storyEditorViewModel is null
            ? _storyTodoNotes.ToList()
            : _storyEditorViewModel.TodoItems.Select(row => row.Note).ToList();

    private void SaveStoryTodosIfDirty()
    {
        if (!_isStoryTodoDirty)
        {
            return;
        }

        _isStoryTodoDirty = false;
        var notes = CollectStoryTodoNotesFromRows();
        _storyTodoNotes.Clear();
        _storyTodoNotes.AddRange(notes);
        StoryTodoStore.Save(_currentProject?.Path, notes);
    }

    /// <summary>把文本框里的内容落到"当前句"那条备注上（没有就新建）。</summary>
    private void ApplyStoryTodoTextToCurrentRow()
    {
        if (_currentStoryChapter is null || _storyRows.Count == 0)
        {
            return;
        }

        var text = _storyEditorViewModel?.TodoText ?? string.Empty;
        var rowNumber = _currentStoryRowIndex + 1;
        var index = _storyTodoNotes.FindIndex(note =>
            note.RowNumber == rowNumber &&
            string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(text))
        {
            // 清空文本框 = 删掉这条备注。留一条空备注只会让列表变成噪音。
            if (index >= 0)
            {
                _storyTodoNotes.RemoveAt(index);
                _isStoryTodoDirty = true;
            }

            return;
        }

        var snippet = BuildStoryTodoSnippet();
        var section = _storyEditorViewModel?.GetCurrentSection() ?? 0;
        if (index >= 0)
        {
            var existing = _storyTodoNotes[index];
            if (string.Equals(existing.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            _storyTodoNotes[index] = existing with
            {
                Text = text,
                Section = section,
                Snippet = snippet,
                UpdatedAt = DateTimeOffset.Now
            };
        }
        else
        {
            _storyTodoNotes.Add(new StoryTodoNote(
                _currentStoryChapter.Code,
                rowNumber,
                section,
                snippet,
                text,
                Done: false,
                UpdatedAt: DateTimeOffset.Now));
        }

        _isStoryTodoDirty = true;
    }

    /// <summary>拿当前这句的开头几十个字当"这是哪句"的凭据。</summary>
    private string BuildStoryTodoSnippet()
    {
        var text = _storyEditorViewModel?.StoryText ?? string.Empty;
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        const int maxLength = 24;
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }

    // ── 界面事件 ──────────────────────────────────────────────────

    private void StoryTodoTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 载入某句的备注时也会触发这里 —— 那种"变化"不是用户编辑，别写回去。
        if (_storyEditorViewModel?.IsLoadingTodo == true)
        {
            return;
        }

        ApplyStoryTodoTextToCurrentRow();
        _isStoryTodoDirty = true;
        _storyEditorViewModel!.TodoStatusText = "本句有备注 · 待保存";
        // 存盘走编辑器本来那套 debounce（StoryEditSaveTimer_Tick 里顺带存备注），
        // 所以这里只标脏，不自己起计时器 —— 两套计时器迟早会打架。
    }

    private void StoryTodoJumpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StoryTodoRowViewModel row })
        {
            JumpToStoryTodo(row.Note);
        }
    }

    private void StoryTodoPreviousButton_Click(object sender, RoutedEventArgs e) => JumpStoryTodoByOffset(-1);

    private void StoryTodoNextButton_Click(object sender, RoutedEventArgs e) => JumpStoryTodoByOffset(1);

    /// <summary>按当前章的行号顺序，跳到前/后一条备注。</summary>
    private void JumpStoryTodoByOffset(int offset)
    {
        if (_currentStoryChapter is null)
        {
            return;
        }

        var currentRowNumber = _currentStoryRowIndex + 1;
        var ordered = _storyTodoNotes
            .Where(note => string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase))
            .OrderBy(note => note.RowNumber)
            .ToList();
        if (ordered.Count == 0)
        {
            _storyEditorViewModel!.TodoStatusText = "本章还没有任何备注";
            return;
        }

        var target = offset < 0
            ? ordered.LastOrDefault(note => note.RowNumber < currentRowNumber)
            : ordered.FirstOrDefault(note => note.RowNumber > currentRowNumber);

        // 到头了就绕回去 —— 比"什么也不发生"好，至少知道到头了。
        target ??= offset < 0 ? ordered[^1] : ordered[0];
        JumpToStoryTodo(target);
    }

    private void JumpToStoryTodo(StoryTodoNote note)
    {
        if (_currentStoryChapter is null)
        {
            return;
        }

        if (!string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase))
        {
            // 跨章的备注：这里不自动切章节。切章会丢掉当前未保存的编辑状态，
            // 那是另一件事，得先问清楚再做。先把话说明白。
            _storyEditorViewModel!.TodoStatusText =
                $"这条备注在「{note.ChapterCode}」第 {note.RowNumber} 句 —— 先切到那一章再跳";
            return;
        }

        var targetIndex = Math.Clamp(note.RowNumber - 1, 0, Math.Max(0, _storyRows.Count - 1));
        NavigateToStoryRow(targetIndex, rebuildPersistentState: true);
        RefreshStoryTodoPanel();
    }
}
