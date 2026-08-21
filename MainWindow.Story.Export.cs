using System;
using System.Linq;
using System.Threading.Tasks;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private async Task PrepareStoryCsvForUnrealSyncAsync(ProjectInfo project)
    {
        if (_currentProject is not null &&
            string.Equals(_currentProject.Path, project.Path, StringComparison.OrdinalIgnoreCase) &&
            _currentStoryChapter is not null)
        {
            SaveCurrentStoryRow();
        }

        await _storySaveCoordinator.FlushAsync();
        var chapters = _projectWorkspaceService.GetChapters(project)
            .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SetUnrealSyncProgress(true, "正在从 StoryDocument 生成剧情 CSV... 4%", false, 4);
        await Task.Run(() => _storyCsvExporter.ExportProject(chapters));
    }
}
