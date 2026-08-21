using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using GalExcleTools.Views;
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private void OpenProjectTextTool(ProjectTextToolMode mode, ChapterInfo chapter)
        {
            if (_currentProject is null)
            {
                return;
            }

            _workbenchReturnPage = WorkbenchReturnPage.ProjectTextTool;
            _projectTextToolMode = mode;
            _selectedTextToolChapter = chapter;
            _selectedVoiceChapter = chapter;
            ProjectTextToolPage.Visibility = Visibility.Visible;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            RefreshProjectTextToolPage();
            PlayPageEntrance(ProjectTextToolPage);
        }

        private void BackToProjectDetailFromTextToolButton_Click(object sender, RoutedEventArgs e)
        {
            StopProjectVoicePreview();
            if (_currentProject is null)
            {
                ShowWorkbenchPage();
                return;
            }

            PlayNegativeSound();
            ShowProjectDetailPage(_projectWorkspaceService.ReadProjectInfo(_currentProject.Path));
        }

        private async void SwitchProjectTextToolChapterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            var selected = await SelectProjectChapterAsync("切换章节", _selectedTextToolChapter);
            if (selected is null)
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();
            _selectedTextToolChapter = selected;
            _selectedVoiceChapter = selected;
            RefreshProjectTextToolPage();
        }

        private void ReloadProjectTextToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProject is null)
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();
            var chapters = _projectWorkspaceService.GetChapters(_currentProject)
                .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _projectTextRows.Clear();
            _projectTextRows.AddRange(_projectTextDataService.LoadTextRows(_currentProject, chapters));
            _projectVoiceMapState = _projectTextDataService.ReadVoiceMap(_currentProject);
            _projectLocalizationState = _projectTextDataService.ReadLocalization(_currentProject);
            RefreshProjectTextToolPage();
        }

        private void ProjectTextToolVoiceFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectTextToolMode != ProjectTextToolMode.Voice)
            {
                return;
            }

            PlaySelectionSound();
            StopProjectVoicePreview();
            _showOnlyConfiguredVoices = !_showOnlyConfiguredVoices;
            RefreshProjectTextToolPage();
        }

        private async void SwitchProjectTextToolLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            var language = await ShowNameInputDialogAsync("切换语言", "目标语言", _selectedLocalizationLanguage);
            if (string.IsNullOrWhiteSpace(language))
            {
                return;
            }

            PlaySelectionSound();
            _selectedLocalizationLanguage = language.Trim();
            RefreshProjectTextToolPage();
        }

        private async Task<ChapterInfo?> SelectProjectChapterAsync(string title, ChapterInfo? selectedChapter)
        {
            if (_currentProject is null)
            {
                return null;
            }

            var chapters = _projectWorkspaceService.GetChapters(_currentProject)
                .OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (chapters.Count == 0)
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "没有可切换章节", "当前项目还没有章节。");
                return null;
            }

            var selected = await _dialogService.SelectAsync(new SelectionDialogRequest<ChapterInfo>(
                title,
                "选择要显示文本表格的章节。",
                chapters
                    .Select(chapter => new SelectionDialogItem<ChapterInfo>($"{chapter.Name} / {chapter.Code}", chapter))
                    .ToList(),
                "确定",
                "取消",
                460,
                420,
                chapter => selectedChapter is not null && PathsEqual(chapter.Path, selectedChapter.Path)));
            return selected;
        }

        private void RefreshProjectTextToolPage()
        {
            ProjectTextToolTablePanel.Children.Clear();
            ProjectTextToolSwitchLanguageButton.Visibility = _projectTextToolMode == ProjectTextToolMode.Localization
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectTextToolVoiceFilterButton.Visibility = _projectTextToolMode == ProjectTextToolMode.Voice
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProjectTextToolVoiceFilterButton.Content = _showOnlyConfiguredVoices ? "显示全部" : "仅看已设置";
            if (_selectedTextToolChapter is null)
            {
                ProjectTextToolTitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice ? "文本语音" : "文本本地化";
                ProjectTextToolSubtitleText.Text = "当前项目还没有章节。";
                ProjectTextToolTablePanel.Children.Add(CreateProjectTextEmptyRow("暂无可显示文本。"));
                return;
            }

            var chapterRows = _projectTextRows
                .Where(row => string.Equals(row.ChapterCode, _selectedTextToolChapter.Code, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var rows = _projectTextToolMode == ProjectTextToolMode.Voice && _showOnlyConfiguredVoices
                ? chapterRows
                    .Where(row => _projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) &&
                        !string.IsNullOrWhiteSpace(voicePath))
                    .ToList()
                : chapterRows;
            ProjectTextToolTitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice
                ? $"文本语音 {_selectedTextToolChapter.Name}"
                : $"文本本地化 {_selectedTextToolChapter.Name}";
            ProjectTextToolSubtitleText.Text = _projectTextToolMode == ProjectTextToolMode.Voice
                ? _showOnlyConfiguredVoices
                    ? $"当前章节：{_selectedTextToolChapter.Code}，已设置语音 {rows.Count} / 全部 {chapterRows.Count} 行。"
                    : $"当前章节：{_selectedTextToolChapter.Code}，文本 {chapterRows.Count} 行。右侧点击选择语音文件，仅支持 wav。"
                : $"当前语言：{_selectedLocalizationLanguage}，章节：{_selectedTextToolChapter.Code}，文本 {rows.Count} 行。";
            ProjectTextToolTablePanel.Children.Add(CreateProjectTextTableHeader(
                _projectTextToolMode == ProjectTextToolMode.Voice ? "语音文件" : _selectedLocalizationLanguage,
                _projectTextToolMode == ProjectTextToolMode.Voice));
            if (rows.Count == 0)
            {
                ProjectTextToolTablePanel.Children.Add(CreateProjectTextEmptyRow("这个章节还没有可编辑的文本。"));
                return;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (_projectTextToolMode == ProjectTextToolMode.Voice)
                {
                    var value = _projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) ? voicePath : string.Empty;
                    ProjectTextToolTablePanel.Children.Add(CreateProjectTextVoiceTableRow(row, value, chapterRows.Count));
                }
                else
                {
                    var languageMap = GetCurrentLocalizationLanguageMap();
                    var value = languageMap.TryGetValue(row.Id, out var localizedText) ? localizedText : string.Empty;
                    ProjectTextToolTablePanel.Children.Add(CreateProjectTextLocalizationTableRow(row, value, languageMap));
                }
            }
        }

        private Dictionary<string, string> GetCurrentLocalizationLanguageMap()
        {
            if (!_projectLocalizationState.Languages.TryGetValue(_selectedLocalizationLanguage, out var languageMap))
            {
                languageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _projectLocalizationState.Languages[_selectedLocalizationLanguage] = languageMap;
            }

            return languageMap;
        }

        private async Task SetProjectVoiceRemarkAsync(
            ProjectTextRow row,
            int rowCount,
            Button? button = null,
            Button? previewButton = null)
        {
            if (_currentProject is null)
            {
                return;
            }

            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) || !File.Exists(voicePath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "没有可修改的语音", "请先为这一行选择 wav 语音文件。");
                return;
            }

            var remarkInput = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
                "更改语音备注",
                "备注",
                ProjectVoiceAssetService.ParseRemark(voicePath),
                "例如：不要S3"));
            if (remarkInput is null)
            {
                return;
            }

            var updatedPath = _projectVoiceAssetService.UpdateRemark(
                _currentProject,
                row,
                rowCount,
                remarkInput,
                _projectVoiceMapState);
            _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
            TouchProjectLastEditedAt(_currentProject);
            if (button is not null)
            {
                UpdateProjectVoiceButton(button, updatedPath);
                if (previewButton is not null)
                {
                    UpdateProjectVoicePreviewButton(previewButton, updatedPath);
                }
            }
            else
            {
                RefreshProjectTextToolPage();
            }

            ShowFloatingTip(
                InfoBarSeverity.Success,
                "语音备注已保存",
                Path.GetFileName(updatedPath),
                logMessage: $"已更改语音备注：{Path.GetFileName(updatedPath)}");
        }

        private async Task RemoveProjectVoiceAsync(ProjectTextRow row, Button? button = null, Button? previewButton = null)
        {
            if (_currentProject is null)
            {
                return;
            }

            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath))
            {
                ShowChapterStatus(InfoBarSeverity.Warning, "没有可移除的语音", "这一行还没有设置语音文件。");
                return;
            }

            var confirmed = await _dialogService.ConfirmAsync(new DialogRequest(
                "移除语音映射",
                $"确定移除 {Path.GetFileName(voicePath)} 吗？项目 Voice 文件夹中的对应文件也会删除。",
                "移除",
                "取消",
                PrimaryButtonStyle: CreateDestructivePrimaryButtonStyle()));
            if (!confirmed)
            {
                return;
            }

            var removedPath = _projectVoiceAssetService.RemoveVoice(_currentProject, row, _projectVoiceMapState);
            StopProjectVoicePreview();
            _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
            TouchProjectLastEditedAt(_currentProject);
            if (button is not null)
            {
                UpdateProjectVoiceButton(button, string.Empty);
                if (previewButton is not null)
                {
                    UpdateProjectVoicePreviewButton(previewButton, string.Empty);
                }
            }
            else
            {
                RefreshProjectTextToolPage();
            }

            ShowFloatingTip(
                InfoBarSeverity.Success,
                "语音文件已移除",
                Path.GetFileName(removedPath ?? voicePath),
                logMessage: $"已移除语音映射：{Path.GetFileName(removedPath ?? voicePath)}");
        }

        private static UIElement CreateProjectTextTableHeader(string valueHeader, bool includeVoicePreview)
        {
            var grid = CreateProjectTextGrid(includeVoicePreview);
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);
            grid.Children.Add(CreateProjectTextCell("文本", 0, isHeader: true, hasRightBorder: true));
            grid.Children.Add(CreateProjectTextCell(valueHeader, 1, isHeader: true, hasRightBorder: includeVoicePreview));
            if (includeVoicePreview)
            {
                grid.Children.Add(CreateProjectTextCell("试听", 2, isHeader: true, centerContent: true));
            }

            return grid;
        }

        private static UIElement CreateProjectTextEmptyRow(string text)
        {
            var border = new Border
            {
                Padding = new Thickness(12),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            return border;
        }

        private Grid CreateProjectTextVoiceTableRow(ProjectTextRow row, string value, int rowCount)
        {
            var grid = CreateProjectTextGrid(includeVoicePreview: true);
            grid.Children.Add(CreateProjectTextSourceCell(row));
            var hasVoice = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
            var button = new Button
            {
                MinWidth = 520,
                Margin = new Thickness(8),
                Tag = row.Id
            };
            UpdateProjectVoiceButton(button, hasVoice ? value : string.Empty);
            var previewButton = CreateProjectVoicePreviewButton(row, hasVoice ? value : string.Empty);
            button.ContextFlyout = GridViewItemFactory.CreateMenu(
                GridViewItemFactory.CreateMenuItem("更改备注", async (_, _) => await SetProjectVoiceRemarkAsync(row, rowCount, button, previewButton)),
                GridViewItemFactory.CreateMenuItem("移除", async (_, _) => await RemoveProjectVoiceAsync(row, button, previewButton)));
            button.Click += async (_, _) =>
            {
                var selectedPath = await PickReplacementFileAsync([".wav"], PickerLocationId.MusicLibrary);
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                if (_currentProject is null)
                {
                    return;
                }

                var isReplacing = _projectVoiceMapState.Voices.TryGetValue(row.Id, out var existingPath) &&
                    !string.IsNullOrWhiteSpace(existingPath);
                try
                {
                    var sourceFileName = Path.GetFileName(selectedPath);
                    var importedPath = _projectVoiceAssetService.ImportVoice(
                        _currentProject,
                        row,
                        rowCount,
                        selectedPath,
                        _projectVoiceMapState);
                    _projectTextDataService.WriteVoiceMap(_currentProject, _projectVoiceMapState);
                    TouchProjectLastEditedAt(_currentProject);

                    UpdateProjectVoiceButton(button, importedPath);
                    UpdateProjectVoicePreviewButton(previewButton, importedPath);
                    PlaySelectionSound();
                    ShowFloatingTip(
                        InfoBarSeverity.Success,
                        isReplacing ? "语音文件已更换" : "语音文件已设置",
                        Path.GetFileName(importedPath),
                        logMessage: $"{(isReplacing ? "更换" : "设置")}语音文件：{row.Id} <- {sourceFileName}");
                }
                catch (Exception ex)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        isReplacing ? "语音文件更换失败" : "语音文件设置失败",
                        "错误详情已复制到剪贴板，并写入 Log。",
                        ex,
                        $"{(isReplacing ? "更换" : "设置")}语音文件失败：{row.Id}");
                }
            };
            grid.Children.Add(CreateProjectTextScrollableCell(button, 1, hasRightBorder: true));
            grid.Children.Add(CreateProjectVoicePreviewCell(previewButton));
            return grid;
        }

        private Grid CreateProjectTextLocalizationTableRow(
            ProjectTextRow row,
            string value,
            Dictionary<string, string> languageMap)
        {
            var grid = CreateProjectTextGrid();
            grid.Children.Add(CreateProjectTextSourceCell(row));
            var textBox = new TextBox
            {
                Text = value,
                PlaceholderText = "填写目标语言文本",
                AcceptsReturn = false,
                MinWidth = 520,
                Margin = new Thickness(8),
                Tag = row.Id
            };
            textBox.TextChanged += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    languageMap.Remove(row.Id);
                }
                else
                {
                    languageMap[row.Id] = textBox.Text;
                }

                if (_currentProject is not null)
                {
                    _projectTextDataService.WriteLocalization(_currentProject, _projectLocalizationState);
                }
            };
            grid.Children.Add(CreateProjectTextScrollableCell(textBox, 1));
            return grid;
        }

        private static Grid CreateProjectTextGrid(bool includeVoicePreview = false)
        {
            var grid = new Grid
            {
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
            if (includeVoicePreview)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            }

            return grid;
        }

        private static string CreateProjectTextSourceContent(ProjectTextRow row)
        {
            return $"{row.Text}\n{row.ChapterCode} / 小节 {row.Section} / 行 {row.RowName}";
        }

        private static Border CreateProjectTextSourceCell(ProjectTextRow row)
        {
            var panel = new StackPanel
            {
                Spacing = 4
            };
            panel.Children.Add(new TextBlock
            {
                Text = row.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{row.ChapterCode} / 小节 {row.Section} / 行 {row.RowName}",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            });

            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = panel
            };
            Grid.SetColumn(border, 0);
            return border;
        }

        private static void UpdateProjectVoiceButton(Button button, string voicePath)
        {
            var hasVoice = !string.IsNullOrWhiteSpace(voicePath);
            button.Content = hasVoice ? Path.GetFileName(voicePath) : "点击选择语音文件（仅支持 wav）";
            button.Foreground = Application.Current.Resources[
                hasVoice ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"] as Brush;
            button.FontWeight = hasVoice
                ? Microsoft.UI.Text.FontWeights.Normal
                : Microsoft.UI.Text.FontWeights.Normal;
            button.Opacity = hasVoice ? 1 : 0.72;
            ToolTipService.SetToolTip(button, hasVoice ? voicePath : "未设置语音文件");
        }

        private Button CreateProjectVoicePreviewButton(ProjectTextRow row, string voicePath)
        {
            var button = new Button
            {
                Width = 40,
                Height = 36,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new SymbolIcon(Symbol.Play),
                Tag = row.Id
            };
            UpdateProjectVoicePreviewButton(button, voicePath);
            button.Click += async (_, _) => await PlayProjectVoicePreviewAsync(row);
            return button;
        }

        private static void UpdateProjectVoicePreviewButton(Button button, string voicePath)
        {
            var canPreview = !string.IsNullOrWhiteSpace(voicePath) && File.Exists(voicePath);
            button.IsEnabled = canPreview;
            ToolTipService.SetToolTip(button, canPreview ? "播放语音测试" : "当前行没有可播放的语音");
        }

        private async Task PlayProjectVoicePreviewAsync(ProjectTextRow row)
        {
            if (!_projectVoiceMapState.Voices.TryGetValue(row.Id, out var voicePath) || !File.Exists(voicePath))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "语音无法播放", "当前行没有可用的语音文件。");
                return;
            }

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(voicePath);
                _projectVoicePreviewPlayer.Source = MediaSource.CreateFromStorageFile(file);
                _projectVoicePreviewPlayer.Play();
                AppendLog(LogKind.Info, $"试听语音：{Path.GetFileName(voicePath)}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "语音播放失败",
                    "错误详情已复制到剪贴板，并写入 Log。",
                    ex,
                    $"试听语音失败：{Path.GetFileName(voicePath)}");
            }
        }

        private void StopProjectVoicePreview()
        {
            _projectVoicePreviewPlayer.Pause();
            _projectVoicePreviewPlayer.Source = null;
        }

        private static Border CreateProjectVoicePreviewCell(Button button)
        {
            var border = new Border
            {
                Child = button
            };
            Grid.SetColumn(border, 2);
            return border;
        }

        private static Border CreateProjectTextCell(
            string text,
            int column,
            bool isHeader = false,
            bool hasRightBorder = false,
            bool centerContent = false)
        {
            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = hasRightBorder ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = centerContent ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = Application.Current.Resources[isHeader ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"] as Brush
                }
            };
            Grid.SetColumn(border, column);
            return border;
        }

        private static Border CreateProjectTextScrollableCell(FrameworkElement content, int column, bool hasRightBorder = false)
        {
            var border = new Border
            {
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
                BorderThickness = hasRightBorder ? new Thickness(0, 0, 1, 0) : new Thickness(0),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollMode = ScrollMode.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollMode = ScrollMode.Disabled,
                    Content = content
                }
            };
            Grid.SetColumn(border, column);
            return border;
        }

    }
}
