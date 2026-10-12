using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private enum WorkbenchReturnPage
        {
            Root,
            ProjectDetail,
            StoryEditor,
            ProjectTextTool
        }

        private WorkbenchReturnPage _workbenchReturnPage = WorkbenchReturnPage.Root;
        private int _shellNavigationRequestId;

        private async Task PrepareWorkbenchSessionForModuleChangeAsync()
        {
            if (_workbenchReturnPage == WorkbenchReturnPage.StoryEditor &&
                StoryEditorPage.Visibility == Visibility.Visible)
            {
                try
                {
                    SaveCurrentStoryRow();
                    SaveCurrentChapterProgress();
                    _storyEditSaveTimer?.Stop();
                    await _storySaveCoordinator.FlushAsync();
                }
                catch (Exception ex)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        "剧情保存失败",
                        "切换页面前未能完成保存，请查看 Log。",
                        ex,
                        "切换模块前保存剧情编辑状态失败。");
                }
                finally
                {
                    StopStoryEditorAudio();
                }
                return;
            }

            if (_workbenchReturnPage == WorkbenchReturnPage.ProjectTextTool &&
                ProjectTextToolPage.Visibility == Visibility.Visible)
            {
                StopProjectVoicePreview();
            }
        }

        private void RestoreRememberedWorkbenchPage()
        {
            switch (_workbenchReturnPage)
            {
                case WorkbenchReturnPage.StoryEditor when RestoreStoryEditorSession():
                    return;
                case WorkbenchReturnPage.ProjectTextTool
                    when _currentProject is not null &&
                         _selectedTextToolChapter is not null &&
                         Directory.Exists(_currentProject.Path):
                    OpenProjectTextTool(_projectTextToolMode, _selectedTextToolChapter);
                    return;
                case WorkbenchReturnPage.ProjectDetail
                    when _currentProject is not null && Directory.Exists(_currentProject.Path):
                    ShowProjectDetailPage(_projectWorkspaceService.ReadProjectInfo(_currentProject.Path));
                    return;
                default:
                    ShowWorkbenchPage();
                    return;
            }
        }

        private bool RestoreStoryEditorSession()
        {
            if (_currentProject is null ||
                _currentStoryChapter is null ||
                string.IsNullOrWhiteSpace(_currentStoryDocumentPath) ||
                !Directory.Exists(_currentProject.Path) ||
                !File.Exists(_currentStoryDocumentPath) ||
                _storyRows.Count == 0)
            {
                return false;
            }

            try
            {
                _currentProject = _projectWorkspaceService.ReadProjectInfo(_currentProject.Path);
                _currentStoryAssetLibrary = ResolveProjectAssetLibrary(_currentProject);
            }
            catch (Exception ex)
            {
                AppendLog(LogKind.Warning, "恢复剧情编辑页面失败，已返回项目列表。", ex);
                return false;
            }

            _currentAssetLibrary = null;
            _currentStoryRowIndex = Math.Clamp(_currentStoryRowIndex, 0, _storyRows.Count - 1);
            _storyEditorViewModel.AssetStatusText = _currentStoryAssetLibrary is null
                ? "当前项目未关联素材库。"
                : $"素材库：{_currentStoryAssetLibrary.Name}";
            ApplyStoryTextFontSizeToUi();
            UpdateStoryDebugModeUi();
            RebuildStoryPersistentFunctionState(_currentStoryRowIndex);
            UpdateStoryCharacterSlotLayout();
            LoadStoryRowIntoUi(playMedia: false);

            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Visible;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            SyncPanel.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(StoryEditorPage);
            StoryEditorPage.Focus(FocusState.Programmatic);
            AppendLog(LogKind.User, $"返回章节编辑器：{_currentStoryChapter.Name}（第 {_currentStoryRowIndex + 1} 句）");
            return true;
        }
    }
}
