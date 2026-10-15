using System;
using System.Collections.Generic;
using System.Linq;
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
/// 记住是**哪一句**，跳回去直接复用 <c>NavigateToStoryRow</c>。挂在小节上反而跳不准。
///
/// 【定位认 StableId，行号只是显示】
/// 这里原来写的是"定位只认章节 + 行号"，理由是"行号才是编辑器的稳定口径"—— **判断错了**：
/// 在某一句前面插一句、或者删掉一句，它后面所有行的行号都会往前挪一位，
/// 于是那条备注就贴到了别的句子上，而且一点异常都没有（最难发现的那一类）。
/// 现在定位认 <c>StoryRow.StableId</c>；行号 / 小节退化成"最后知道的位置"，
/// 每次重建列表时按 StableId 重算一遍，找不到行就明说「该句已删除」。
/// 老备注（文件里还没有 StableId）在**打开它所属章节**时按行号就地迁移。
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<StoryTodoNote> _storyTodoNotes = [];
    private bool _isStoryTodoDirty;
    private bool _isStoryTodoLoaded;

    /// <summary>
    /// 文本框里那份文字**属于哪一句** —— 载入时记下来，写回去只认它。
    ///
    /// 为什么不看 _currentStoryRowIndex：那个值在"存"和"载"之间会变。
    /// 具体地说，LoadStoryRowIntoUi 跑的时候 _currentStoryRowIndex 已经是**新行**了，
    /// 而文本框里还是**旧行**的内容 —— 这时候按"当前行"写，就把旧行的字写到新行上了。
    ///
    /// 配合"只在存的时候读（见 TextChanged 那段注释）"，两者一起才对：
    ///   - 只在存的时候读 → 读到的不是过期的 VM 值；
    ///   - 只认载入时记下的行 → 落点不受 _currentStoryRowIndex 影响。
    /// 单用哪一条都不够 —— 2026-10-13 两种单用各翻过一次车。
    /// </summary>
    private string _storyTodoEditingChapterCode = string.Empty;

    private int _storyTodoEditingRowNumber;

    /// <summary>
    /// 文本框里那份文字属于**哪一句** —— 和行号一起在载入时记下来。
    ///
    /// 和 <see cref="_storyTodoEditingRowNumber"/> 的分工：StableId 是**身份**（写回去认它），
    /// 行号是**位置**（显示、以及老备注的兜底）。在它前面插一句，行号会变、身份不变 ——
    /// 前者正是当初"备注贴错句子"的原因。
    /// </summary>
    private string _storyTodoEditingStableId = string.Empty;

    /// <summary>
    /// 用户在备注框里**实际打的字** —— 在 TextChanged 里直接从控件抓下来。
    ///
    /// ⚠️ 为什么不信 <c>_storyEditorViewModel.TodoText</c>：2026-10-14 实测，
    /// 文件是**写成功了的**（时间戳会动、JSON 结构也对），但 Notes 是**空的** ——
    /// 也就是说保存跑到时 TodoText 还是空串，用户打的字根本没被读进来。
    ///
    /// 原因是那份 VM 属性会被别处改写：载入某一句时把它设成那一句的备注
    /// （没有备注就是空串），而"程序设成空串"和"用户真的清空了"在属性上
    /// **长得一模一样**，从属性上分不出来。**控件里的字才是用户真正打的那个。**
    /// </summary>
    private string? _pendingStoryTodoText;

    /// <summary>
    /// 把**当前行**的备注刷进界面。
    ///
    /// 这个函数由 <c>LoadStoryRowIntoUi</c> 调用 —— 就是那个"把当前行刷进界面"的中心入口，
    /// 和 <c>_storyEditorViewModel.StoryText = row.Get("Tesxt")</c> 紧挨着。
    /// 换句、撤销、重做、换小节都走那一条路，所以放这儿等于**一次覆盖全部**。
    ///
    /// 只在该函数里调，不要另外挂到导航之类的旁路上去：
    /// 挂旁路会漏掉撤销 / 重做，那时候备注面板还停在上一行的内容上，
    /// 一编辑就写到错的句子上 —— 这类错最难发现。
    ///
    /// 载入前会先落一次待存的（SaveStoryTodosIfDirty）：换行时上一句刚打的字
    /// 可能还没到 debounce 那一拍，不落就丢了。
    /// </summary>
    private void LoadStoryTodoIntoUi()
    {
        if (_storyEditorViewModel is null)
        {
            return;
        }

        if (!_isStoryTodoLoaded)
        {
            // 备注还没载入（正在打开章节的过程中会走到这儿）—— 先别刷，
            // 否则会把上一章的备注当成这一章的。LoadStoryTodos 之后会整体刷一次。
            return;
        }

        // 注意：这里**不存**。存的那一步在 SaveCurrentStoryRow 开头 ——
        // 这个函数跑的时候 _currentStoryRowIndex 往往已经换成新行了，
        // 在这儿存会把旧行的文字写到新行上（TextEditing 里那套顺序是"先存旧行、
        // 再执行换行、最后载入"，照着那个来才安全）。
        RefreshStoryTodoPanel();
    }

    /// <summary>当前句对应的那条备注；没有就返回 null。</summary>
    private StoryTodoNote? FindStoryTodoForCurrentRow()
    {
        if (_currentStoryChapter is null || _storyRows.Count == 0)
        {
            return null;
        }

        var index = FindStoryTodoIndex(
            _currentStoryChapter.Code,
            GetCurrentStoryRowStableId(),
            _currentStoryRowIndex + 1);
        return index < 0 ? null : _storyTodoNotes[index];
    }

    /// <summary>
    /// 找某一句的备注在 <see cref="_storyTodoNotes"/> 里的下标（-1 = 没有）。
    ///
    /// **先认 StableId**；只有"还没有 StableId 的老备注"才退化成"章节 + 行号"。
    /// 兜底那条特意要求对方也没有 StableId —— 否则一个行号就能把别人（按身份认的那条）
    /// 的备注顶掉，等于又回到"贴错句子"。
    /// </summary>
    private int FindStoryTodoIndex(string chapterCode, string stableId, int rowNumber)
    {
        if (!string.IsNullOrWhiteSpace(stableId))
        {
            var byStableId = _storyTodoNotes.FindIndex(note =>
                HasStoryTodoStableId(note) &&
                string.Equals(note.ChapterCode, chapterCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(note.StableId, stableId, StringComparison.OrdinalIgnoreCase));
            if (byStableId >= 0)
            {
                return byStableId;
            }
        }

        return _storyTodoNotes.FindIndex(note =>
            !HasStoryTodoStableId(note) &&
            note.RowNumber == rowNumber &&
            string.Equals(note.ChapterCode, chapterCode, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasStoryTodoStableId(StoryTodoNote note)
    {
        return !string.IsNullOrWhiteSpace(note.StableId);
    }

    /// <summary>当前句的稳定身份；没有行（或行表还没载入）就给空串。</summary>
    private string GetCurrentStoryRowStableId()
    {
        return _storyRows.Count == 0
            ? string.Empty
            : _storyRows[Math.Clamp(_currentStoryRowIndex, 0, _storyRows.Count - 1)].StableId;
    }

    /// <summary>在**当前章节**的行里找这个 StableId 的下标（-1 = 这一句已经不在了）。</summary>
    private int FindStoryRowIndexByStableId(string stableId)
    {
        return string.IsNullOrWhiteSpace(stableId)
            ? -1
            : _storyRows.FindIndex(row => string.Equals(
                row.StableId,
                stableId,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>换项目 / 打开章节时调用：把备注整个读回来。</summary>
    private void LoadStoryTodos()
    {
        _storyTodoNotes.Clear();
        _storyTodoNotes.AddRange(StoryTodoStore.Load(_currentProject?.Path));
        _isStoryTodoDirty = false;
        _isStoryTodoLoaded = true;
        // 老文件（只按行号定位）里的备注，趁这一章的文档刚载入，就地补上 StableId。
        // 补不上的（行号越界之类）先留着空串：查找那边有行号兜底，
        // 等它所属的章节被打开时再补 —— 别的章节的行表这会儿根本没载入。
        // 标脏但不在这里存：下面 RefreshStoryTodoPanel 还没跑，
        // 「这份文字属于哪一句」还是上一章的值，这时候存会把上一章的字写进这一章。
        // 下一次真正的保存会顺手把迁移结果写下去。
        if (MigrateStoryTodoStableIdsForCurrentChapter())
        {
            _isStoryTodoDirty = true;
        }

        RefreshStoryTodoPanel();
    }

    /// <summary>给「本章还没有 StableId 的备注」按它们记着的行号补上 StableId。</summary>
    private bool MigrateStoryTodoStableIdsForCurrentChapter()
    {
        if (_currentStoryChapter is null || _storyRows.Count == 0)
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < _storyTodoNotes.Count; index++)
        {
            var note = _storyTodoNotes[index];
            if (HasStoryTodoStableId(note) ||
                note.RowNumber < 1 || note.RowNumber > _storyRows.Count ||
                !string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _storyTodoNotes[index] = note with { StableId = _storyRows[note.RowNumber - 1].StableId };
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 把"当前句的备注"刷到右栏：文本框 + 归属记录。
    ///
    /// 换句、换章、载入之后都要调一次 —— 否则文本框里还留着上一句的内容，
    /// 一编辑就会把它写到**错误的句子**上。那种错很难发现，所以宁可多刷几次。
    ///
    /// ⚠️ **这里刻意不重建备注列表。**
    /// 换句时变的只有"当前是第几句"，而列表里**根本不显示当前句**（没有高亮），
    /// 所以重建出来的东西和原来一模一样 —— 纯浪费。而这是**每次换句**都会走的路径：
    /// 50 条备注就是 300 多个控件重建一遍，连点"下一句"时开销直接乘上点击次数。
    /// （用户 2026-10-14 报过"快速切会卡很久"，这是第二顺位的原因；
    ///   第一顺位是前缀状态每次 O(n) 重扫，已在 RebuildStoryPersistentFunctionState 修掉。）
    ///
    /// 列表该重建的时机只有两个：打开章节（LoadStoryTodos）、真正存盘后
    /// （SaveStoryTodosIfDirty —— 刚打的备注要出现在列表里）。那两处各自会调。
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
            // 先记下"这份文字属于哪一句"，再改 TodoText —— 顺序不能反。
            _storyTodoEditingChapterCode = _currentStoryChapter?.Code ?? string.Empty;
            _storyTodoEditingRowNumber = _storyRows.Count == 0 ? 0 : _currentStoryRowIndex + 1;
            _storyTodoEditingStableId = GetCurrentStoryRowStableId();

            var note = FindStoryTodoForCurrentRow();
            _storyEditorViewModel.TodoText = note?.Text ?? string.Empty;
            // 控件里那份也同步成同一个值 —— 不然后面保存时会拿**上一句**的字去写这一句。
            // 同步成"这一句该有的内容"之后，迟到的保存写回去等于空操作，天然安全。
            _pendingStoryTodoText = note?.Text ?? string.Empty;
        }
        finally
        {
            _storyEditorViewModel.IsLoadingTodo = false;
        }
    }

    /// <summary>
    /// 重建备注列表。按章节 + 行号稳定排序，勾掉的只灰显、不重排。
    ///
    /// 列表上那个"第几句 / 第几小节"是**每次重建时按 StableId 重算**的
    /// （只算得动本章 —— 别的章节的行表没载入）。备注自己记的行号只是"最后知道的位置"：
    /// 在它前面插一句，那个数字就不对了，但备注仍然挂在原来那一句上。
    /// </summary>
    private void RefreshStoryTodoList()
    {
        if (_storyEditorViewModel is null)
        {
            return;
        }

        RefreshStoryTodoPositionsForCurrentChapter();
        var rows = _storyTodoNotes
            .OrderBy(note => note.ChapterCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(note => note.RowNumber)
            .Select(note => new StoryTodoRowViewModel(note, IsStoryTodoOrphaned(note)))
            .ToList();

        _storyEditorViewModel.TodoItems.Clear();
        foreach (var row in rows)
        {
            _storyEditorViewModel.TodoItems.Add(row);
        }
    }

    /// <summary>
    /// 本章备注的位置按 StableId 重算。
    /// 找不到行的（这一句被删了）**保持原样** —— 那串数字就是"它当年在第几句"，
    /// 列表上会带一句「该句已删除」，比硬改成别的句子诚实。
    /// </summary>
    private void RefreshStoryTodoPositionsForCurrentChapter()
    {
        if (_currentStoryChapter is null)
        {
            return;
        }

        for (var index = 0; index < _storyTodoNotes.Count; index++)
        {
            var note = _storyTodoNotes[index];
            if (!HasStoryTodoStableId(note) ||
                !string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rowIndex = FindStoryRowIndexByStableId(note.StableId);
            if (rowIndex < 0)
            {
                continue;
            }

            var section = GetStorySectionAtRowIndex(rowIndex);
            if (rowIndex + 1 == note.RowNumber && section == note.Section)
            {
                continue;
            }

            _storyTodoNotes[index] = note with
            {
                RowNumber = rowIndex + 1,
                Section = section
            };
        }
    }

    /// <summary>
    /// 这条备注的句子还在不在本章里。
    /// 老备注（还没迁移出 StableId）无从判断，算它还在 —— 走行号那条老路。
    /// </summary>
    private bool IsStoryTodoOrphaned(StoryTodoNote note)
    {
        if (_currentStoryChapter is null ||
            !HasStoryTodoStableId(note) ||
            !string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return FindStoryRowIndexByStableId(note.StableId) < 0;
    }

    /// <summary>
    /// 保存前把「勾选状态」从列表行里收回来。
    ///
    /// ⚠️ **绝对不能拿行里的 Note 整体覆盖 _storyTodoNotes** ——
    /// 那是 2026-10-13「备注写完就没了」的根子：
    ///
    ///   行是**快照**（刷新列表时建的），你打字加进去的备注不在快照里；
    ///   而保存时如果拿快照反过来盖一遍，刚写的那条就没了。
    ///   表现成"点下一句，备注不见了"，看着像跑到别处去了，其实是被盖掉了。
    ///
    /// 行里唯一"比数据新"的只有 <c>Done</c>（勾选框双向绑定在行上）。
    /// 所以就只收它 —— 一句话：**谁的字段谁负责，别互相盖。**
    /// </summary>
    private void MergeStoryTodoDoneStateFromRows()
    {
        if (_storyEditorViewModel is null)
        {
            return;
        }

        foreach (var row in _storyEditorViewModel.TodoItems)
        {
            // 按身份找回去（老备注才会走章节 + 行号那条兜底）。
            // 不用 ReferenceEquals 比行里那份 Note：列表重建过之后它可能是旧快照。
            var index = FindStoryTodoIndex(row.ChapterCode, row.StableId, row.RowNumber);
            if (index >= 0 && _storyTodoNotes[index].Done != row.Done)
            {
                _storyTodoNotes[index] = _storyTodoNotes[index] with
                {
                    Done = row.Done,
                    UpdatedAt = DateTimeOffset.Now
                };
            }
        }
    }

    private void SaveStoryTodosIfDirty()
    {
        if (!_isStoryTodoDirty)
        {
            return;
        }

        _isStoryTodoDirty = false;
        // **在存之前才把文本框的内容收进数据** —— 不是控件事件里读，见 TextChanged 那段注释。
        // 走到这儿时绑定早已把最新文本推给 TodoText，读到的才是用户真正打的字。
        ApplyStoryTodoTextToCurrentRow();
        MergeStoryTodoDoneStateFromRows();

        // 用**编辑器本来就有的那个**指示器（右上角「已保存 HH:mm:ss」）。
        //
        // ⚠️ 不要另造一个 —— 我 2026-10-13 造过一个（备注面板底下的 TodoStatusText），
        // 结果它自己写着"已保存"，而实际上一个字都没存进去：**它在撒谎**，
        // 于是我看不见 bug，用户也看不见。用户的原话：
        // 「你自己给备注底下新建了一个变化，你把那个去掉，我猜你是以为那个是正确的」。
        // 一个界面只该有一个"存没存上"的说法，而且必须是同一个来源。
        _storyEditorViewModel!.SaveStatusText = "等待保存...";
        StoryTodoStore.Save(_currentProject?.Path, _storyTodoNotes);
        _storyEditorViewModel.SaveStatusText = $"已保存 {DateTime.Now:HH:mm:ss}";

        // 存完刷一次列表：刚打的那条要能出现在里面（不刷就得等下次换句才看得到）。
        RefreshStoryTodoList();
    }

    /// <summary>
    /// 把文本框里的内容落到**它属于的那一句**上（没有就新建）。
    ///
    /// 两个要点，缺一不可（2026-10-13 各翻过一次车，都是只做对一半）：
    ///
    /// 1. **只在"存"的时候调用**（SaveStoryTodosIfDirty 开头），不在控件的 TextChanged 里调。
    ///    因为 TextChanged 比 TwoWay 绑定推值**更早**，在那里读 TodoText 拿到的是旧值 ——
    ///    新打的字永远写不进去。剧情文本就是这么做的：TextChanged 只标脏 + 启动计时器，
    ///    350 毫秒后才在 SaveCurrentStoryRow 里读 StoryText。
    ///
    /// 2. **落点只认"载入时记下的那一句"**，不看 <c>_currentStoryRowIndex</c>。
    ///    因为 LoadStoryRowIntoUi 跑的时候下标**已经是新行**了，而文本框里还是旧行的内容 ——
    ///    这时候按"当前行"写，就把旧行的字写到新行上去了。
    ///
    /// 单做第 1 条：落点会跟着下标漂（换行时写错地方）。
    /// 单做第 2 条：读到的还是过期值（新打的字进不去）。
    /// 两条一起才等价于剧情文本那套"VM 属性 ↔ 当前行"的稳定配对。
    /// </summary>
    private void ApplyStoryTodoTextToCurrentRow()
    {
        if (_storyRows.Count == 0 || string.IsNullOrEmpty(_storyTodoEditingChapterCode) ||
            _storyTodoEditingRowNumber <= 0)
        {
            return;
        }

        // 读**控件里那份**（_pendingStoryTodoText），读不到才退回 VM 属性。
        // 为什么：VM 属性会被"载入某一句"改写，而"程序设成空串"和"用户真的清空"
        // 长得一模一样 —— 只看属性分不出来，于是出现过"文件写成功但 Notes 是空的"。
        var text = _pendingStoryTodoText ?? _storyEditorViewModel?.TodoText ?? string.Empty;
        var rowNumber = _storyTodoEditingRowNumber;
        var chapterCode = _storyTodoEditingChapterCode;
        // 按**身份**找回去（StableId）；老备注才走章节 + 行号。在它前面插过句也不要紧。
        var stableId = _storyTodoEditingStableId;
        var index = FindStoryTodoIndex(chapterCode, stableId, rowNumber);

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

        // 小节按**那条备注自己的行号**算，不用 GetCurrentSection() ——
        // 存的时候当前行可能已经换过去了，用当前行的小节会记错。
        var section = _storyEditorViewModel?.GetSectionAtRowIndex(rowNumber - 1) ?? 0;
        if (index >= 0)
        {
            var existing = _storyTodoNotes[index];
            if (string.Equals(existing.Text, text, StringComparison.Ordinal) &&
                string.Equals(existing.StableId, stableId, StringComparison.OrdinalIgnoreCase) &&
                existing.RowNumber == rowNumber &&
                existing.Section == section)
            {
                return;
            }

            _storyTodoNotes[index] = existing with
            {
                Text = text,
                RowNumber = rowNumber,
                Section = section,
                UpdatedAt = DateTimeOffset.Now,
                // 老备注正好趁这次编辑补上身份；已经有身份的不动。
                StableId = HasStoryTodoStableId(existing) ? existing.StableId : stableId
            };
        }
        else
        {
            _storyTodoNotes.Add(new StoryTodoNote(
                chapterCode,
                rowNumber,
                section,
                text,
                Done: false,
                UpdatedAt: DateTimeOffset.Now)
            {
                StableId = stableId
            });
        }

        _isStoryTodoDirty = true;
    }

    // ── 界面事件 ──────────────────────────────────────────────────

    private void StoryTodoTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // ⚠️ **这里只标脏 + 启动计时器，绝不在这里读文本、绝不在这里写数据。**
        //
        // 原因（2026-10-13 用户第三次报同一个问题时查出来的）：
        // TextChanged 是**控件自己的事件**，它比 TwoWay 绑定把新值推给 VM **更早**。
        // 在这里读 _storyEditorViewModel.TodoText 拿到的是**上一次的旧值** ——
        // 于是新打的字从来没被写进数据，表现就是"写完切句子就丢了"。
        // 而且它连"正在保存"都不会亮：旧值等于原值，保存时逐字段比对没发现变化。
        //
        // 剧情文本就是这么做的：TextChanged 里只 ScheduleStoryRowSave，
        // 等 350 毫秒后在 SaveCurrentStoryRow 里才读 _storyEditorViewModel.StoryText ——
        // **那时候绑定早就推完了**。备注现在照搬这一套：
        // 真正的写入放在 SaveStoryTodosIfDirty 开头（那里读 TodoText）。
        if (_storyEditorViewModel?.IsLoadingTodo == true)
        {
            // 载入某句的备注时也会触发这里 —— 那种"变化"不是用户编辑，别抓。
            return;
        }

        // **从控件本身抓字**，不从 VM 属性读 —— 原因见 _pendingStoryTodoText 的注释：
        // VM 属性会被"载入某句"改写，而"程序设成空串"和"用户真的清空"长得一样。
        if (sender is TextBox box)
        {
            _pendingStoryTodoText = box.Text;
        }

        _isStoryTodoDirty = true;

        // 和剧情文本共用同一个入口 —— 用户的原话：
        // 「这两个本质是触发同一种保存，不用做什么触发时机区分」。
        ScheduleStoryRowSave();
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

    /// <summary>按当前章的行号顺序，跳到前/后一条备注。到头了绕回去。</summary>
    private void JumpStoryTodoByOffset(int offset)
    {
        if (_currentStoryChapter is null)
        {
            return;
        }

        // 位置先按身份重算一次：插过 / 删过句子之后备注记的行号会过期，
        // 不重算的话"上一条 / 下一条"会按旧号挑错邻居。
        RefreshStoryTodoPositionsForCurrentChapter();
        var currentRowNumber = _currentStoryRowIndex + 1;
        var ordered = _storyTodoNotes
            .Where(note => string.Equals(note.ChapterCode, _currentStoryChapter.Code, StringComparison.OrdinalIgnoreCase))
            .OrderBy(note => note.RowNumber)
            .ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        var target = offset < 0
            ? ordered.LastOrDefault(note => note.RowNumber < currentRowNumber)
            : ordered.FirstOrDefault(note => note.RowNumber > currentRowNumber);

        // 到头了就绕回去 —— 比"什么也不发生"好，至少知道到头了。
        target ??= offset < 0 ? ordered[^1] : ordered[0];
        JumpToStoryTodo(target);
    }

    private async void JumpToStoryTodo(StoryTodoNote note)
    {
        if (_currentProject is null)
        {
            return;
        }

        if (!string.Equals(note.ChapterCode, _currentStoryChapter?.Code, StringComparison.OrdinalIgnoreCase))
        {
            // 跨章：**先切过去再跳**。
            //
            // 备注列表是整个项目的（"回头快速找回来"的时候没人记得它在第几章），
            // 所以跨章是**常态**，不是异常。
            //
            // 这里**不用担心丢编辑**：切章走的是 ShowStoryEditorPageAsync，
            // 它开头就是 SaveCurrentStoryRow() → 停掉 debounce 计时器 →
            // await _storySaveCoordinator.FlushAsync()。这正是编辑器平时换章走的那条路，
            // 复用同一条比另造一条安全得多。
            var chapter = _projectWorkspaceService.GetChapters(_currentProject)
                .FirstOrDefault(item => string.Equals(item.Code, note.ChapterCode, StringComparison.OrdinalIgnoreCase));
            if (chapter is null)
            {
                return;
            }

            await ShowStoryEditorPageAsync(chapter);
        }

        // 落点按**身份**找行。找不到 = 这一句已经被删掉了。
        // 旧行为是按行号硬跳 —— 那会跳到**别的句子**上，等于把备注指错了地方，比不跳更糟。
        var targetIndex = FindStoryRowIndexByStableId(note.StableId);
        if (targetIndex < 0)
        {
            if (HasStoryTodoStableId(note))
            {
                ShowStoryStatus(
                    InfoBarSeverity.Informational,
                    "这一句已经没有了",
                    $"{note.HeaderText}：备注还留着，它原来那一句已经被删除，没有可跳的地方。");
                return;
            }

            // 还没迁移出身份的老备注：只能照旧按行号走。
            targetIndex = Math.Clamp(note.RowNumber - 1, 0, Math.Max(0, _storyRows.Count - 1));
        }

        NavigateToStoryRow(targetIndex, rebuildPersistentState: true);
        RefreshStoryTodoPanel();
    }
}
