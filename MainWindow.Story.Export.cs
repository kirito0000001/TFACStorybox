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
        // 进度由调用方按「生成剧情 CSV」那一段的区间来报（见 UnrealSyncProgressPlan）。
        // 这里原来硬编码了一条 "4%"，和计划里其它阶段的百分比互相打架。
        await Task.Run(() => _storyCsvExporter.ExportProject(chapters));
    }
}
