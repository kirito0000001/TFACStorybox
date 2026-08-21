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
