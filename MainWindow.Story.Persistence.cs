using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static GalExcleTools.Services.FileSystemUtility;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private void LoadStoryDocument(ChapterInfo chapter)
    {
        _storyEditorViewModel.Document.ReplaceWith(_storyDocumentRepository.Load(chapter));
        _storyEditorViewModel.SaveStatusText = "已加载";
        _storyEditorViewModel.RefreshCollectionState();
        _storyEditorViewModel.RefreshCommandStates();
    }

    private void ImportLooseStorySectionCsvFiles(ChapterInfo chapter)
    {
        if (_currentStoryDocumentPath is null || !Directory.Exists(chapter.Path))
        {
            return;
        }

        var looseCsvPaths = _storyCsvImporter.GetLooseCsvPaths(chapter);
        if (looseCsvPaths.Count == 0)
        {
            return;
        }

        var importedCount = ImportStorySectionCsvFiles(chapter, looseCsvPaths, deleteSourceFiles: true);
        if (importedCount > 0)
        {
            EnsureStorySectionOptions(_storyRowSections.Values.DefaultIfEmpty(1).Max());
            ShowStoryStatus(InfoBarSeverity.Informational, "已导入小节 CSV", $"检测并导入 {importedCount} 个章节小节。");
        }
    }

    private int ImportStorySectionCsvFiles(
        ChapterInfo chapter,
        IReadOnlyList<string> csvPaths,
        bool deleteSourceFiles)
    {
        var result = _storyCsvImporter.Import(chapter, csvPaths, deleteSourceFiles);
        foreach (var log in result.Logs)
        {
            AppendLog(log.Kind, log.Message);
        }

        if (result.Changed &&
            _currentStoryChapter is not null &&
            PathsEqual(_currentStoryChapter.Path, chapter.Path))
        {
            LoadStoryDocument(chapter);
            SynchronizeStorySectionState();
        }

        return result.ImportedCount;
    }

    private void QueueCurrentStoryDocumentSave(
        bool showStatus = false,
        StoryChoiceNoteState? choiceNotes = null)
    {
        // ⚠️ **这里是"文档被改动"的唯一出口** —— 任何改动想被保存都得经过它。
        // 所以"到每句为止的前缀状态"表在这里作废。
        //
        // 【为什么必须挂在这儿】2026-10-14 的回归：我原来只在 SaveCurrentStoryRow 里清，
        // 而那个函数管的是**说话人 / 台词**那些列 —— **函数（Custom 列）的改动根本不走它**。
        // 于是缓存陈旧：在第 1 句把过渡模式设成 2 之后，第 30 句仍按"改之前"的状态算，
        // 弹出默认的入场黑屏模式。用户的原话：「某处的记录丢了」—— 丢的是缓存的新鲜度。
        //
        // 教训：**缓存的失效点要挂在"唯一出口"上，不是挂在"我记得的那几个地方"上** ——
        // 后者必然漏，而且漏了之后症状离原因很远（改模式 → 三十句之后才表现出来）。
        //
        // 换句**不经过**这里（导航不存文档），所以缓存依然命中，优化效果不受影响。
        InvalidateStoryPersistentStateCache();

        if (_currentStoryChapter is null || _storyRows.Count == 0)
        {
            return;
        }

        if (choiceNotes is not null)
        {
            _storyEditorViewModel.Document.ChoiceNotes = choiceNotes.Clone();
        }

        _storySaveCoordinator.QueueSave(_currentStoryChapter, _storyEditorViewModel.Document);
        _storyEditorViewModel.SaveStatusText = "等待保存...";
        CommitStoryUndoState();
        if (showStatus)
        {
            ShowStoryStatus(InfoBarSeverity.Success, "剧情已保存", "StoryDocument 已加入后台保存队列。");
        }
    }

}
