using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<StoryChoiceManagerSectionItem> _chapterChoiceManagerSections = [];
    private readonly ObservableCollection<StoryChoiceManagerOptionItem> _chapterChoiceManagerOptions = [];
    private StoryChoiceManagerSession? _chapterChoiceManagerSession;
    private bool _isUpdatingChapterChoiceManager;

    private Task ShowChapterChoiceManagerAsync(ChapterInfo chapter)
    {
        try
        {
            _chapterChoiceManagerSession = _storyChoiceManagerService.CreateSession(chapter);
            ChapterChoiceManagerTitle.Text = $"{chapter.Name} · 选项与分支";
            ChapterChoiceManagerCode.Text = chapter.Code;
            ChapterChoiceManagerSectionList.ItemsSource = _chapterChoiceManagerSections;
            ChapterChoiceManagerOptionList.ItemsSource = _chapterChoiceManagerOptions;
            ChapterChoiceManagerSearchBox.Text = string.Empty;
            PopulateChapterChoiceManagerTargets();
            RefreshChapterChoiceManagerSections();
            ChapterChoiceManagerHost.Visibility = Visibility.Visible;
            ChapterChoiceManagerHost.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            ShowChapterStatus(InfoBarSeverity.Error, "无法打开选项管理", ex.Message);
            AppendLog(LogKind.Error, "打开章节选项与分支管理器失败。", ex);
        }

        return Task.CompletedTask;
    }

    private void RefreshChapterChoiceManagerSections()
    {
        if (_chapterChoiceManagerSession is null)
        {
            return;
        }

        var selectedSection = (ChapterChoiceManagerSectionList.SelectedItem as StoryChoiceManagerSectionItem)?.Section;
        var query = ChapterChoiceManagerSearchBox.Text.Trim();
        _chapterChoiceManagerSections.Clear();
        foreach (var flow in _chapterChoiceManagerSession.Flow.Sections)
        {
            var status = GetChapterChoiceManagerStatus(flow.Kind);
            var matches = string.IsNullOrWhiteSpace(query) ||
                          flow.Section.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          flow.FirstSentencePreview.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          status.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          flow.Options.Any(option => option.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                     option.ChoiceFunction.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (matches)
            {
                _chapterChoiceManagerSections.Add(new StoryChoiceManagerSectionItem(
                    flow.Section,
                    StoryChoiceManagerService.GetSectionDisplayName(_chapterChoiceManagerSession.Flow, flow),
                    GetChapterChoiceManagerStructureSummary(flow),
                    status,
                    flow));
            }
        }

        ChapterChoiceManagerSectionList.SelectedItem = _chapterChoiceManagerSections
            .FirstOrDefault(item => item.Section == selectedSection) ?? _chapterChoiceManagerSections.FirstOrDefault();
        ChapterChoiceManagerSummary.Text = $"{_chapterChoiceManagerSession.Flow.Sections.Count} 个小节 · " +
            $"{_chapterChoiceManagerSession.Draft.ChoiceNotes.Choices.Count} 组 Choice";
        RefreshChapterChoiceManagerIssues();
    }

    private void RefreshChapterChoiceManagerOptions()
    {
        _chapterChoiceManagerOptions.Clear();
        if (_chapterChoiceManagerSession is not { } session ||
            ChapterChoiceManagerSectionList.SelectedItem is not StoryChoiceManagerSectionItem section)
        {
            RefreshChapterChoiceManagerDetail();
            return;
        }

        foreach (var option in section.Flow.Options)
        {
            _chapterChoiceManagerOptions.Add(new StoryChoiceManagerOptionItem(
                option.OptionKey,
                option.Text,
                option.ChoiceFunction,
                StoryChoiceManagerService.GetOptionTargetDisplayName(session.Flow, option),
                option));
        }

        ChapterChoiceManagerOptionList.SelectedIndex = _chapterChoiceManagerOptions.Count > 0 ? 0 : -1;
        RefreshChapterChoiceManagerDetail();
    }

    private void RefreshChapterChoiceManagerDetail()
    {
        _isUpdatingChapterChoiceManager = true;
        try
        {
            if (ChapterChoiceManagerSectionList.SelectedItem is not StoryChoiceManagerSectionItem section)
            {
                ChapterChoiceManagerDetailTitle.Text = "未选择小节";
                ChapterChoiceManagerOptionTextBox.IsEnabled = false;
                ChapterChoiceManagerTargetComboBox.IsEnabled = false;
                ChapterChoiceManagerCustomToggle.IsEnabled = false;
                return;
            }

            ChapterChoiceManagerCustomToggle.IsEnabled = true;
            ChapterChoiceManagerCustomToggle.IsOn = section.Flow.Kind == StoryBranchSectionKind.Custom;
            ChapterChoiceManagerSectionInfo.Text = $"第 {section.Section} 小节 · {section.Flow.FirstSentencePreview}";
            if (ChapterChoiceManagerOptionList.SelectedItem is StoryChoiceManagerOptionItem option)
            {
                ChapterChoiceManagerDetailTitle.Text = $"选项 {option.OptionKey}";
                ChapterChoiceManagerFunctionCode.Text = option.ChoiceFunction;
                ChapterChoiceManagerOptionTextBox.IsEnabled = true;
                ChapterChoiceManagerTargetComboBox.IsEnabled = true;
                ChapterChoiceManagerOptionTextBox.Text = option.Text;
                SelectChapterChoiceManagerTarget(option.FlowOption.ExplicitTargetSection);
                ChapterChoiceManagerExplicitRoute.Text = option.FlowOption.ExplicitTargetSection is null
                    ? "显式目标：进入共通线"
                    : $"显式目标：第 {option.FlowOption.ExplicitTargetSection} 小节";
                ChapterChoiceManagerEffectiveRoute.Text = option.FlowOption.EffectiveTargetSection is null
                    ? "实际路线：章节结束"
                    : $"实际路线：StoryJudge={option.OptionKey} -> 第 {option.FlowOption.EffectiveTargetSection} 小节";
                ChapterChoiceManagerTargetPreview.Text = GetChapterChoiceManagerTargetPreview(option.FlowOption.EffectiveTargetSection);
            }
            else
            {
                ChapterChoiceManagerDetailTitle.Text = $"第 {section.Section} 小节";
                ChapterChoiceManagerFunctionCode.Text = section.Status;
                ChapterChoiceManagerOptionTextBox.Text = string.Empty;
                ChapterChoiceManagerOptionTextBox.IsEnabled = false;
                ChapterChoiceManagerTargetComboBox.IsEnabled = false;
                ChapterChoiceManagerExplicitRoute.Text = "没有选项数据";
                ChapterChoiceManagerEffectiveRoute.Text = section.Flow.DefaultTargetSection is null
                    ? section.Flow.Kind == StoryBranchSectionKind.Custom ? "实际路线：保留 Unreal 手工设置" : "实际路线：章节结束"
                    : $"实际路线：默认 -> 第 {section.Flow.DefaultTargetSection} 小节";
                ChapterChoiceManagerTargetPreview.Text = GetChapterChoiceManagerTargetPreview(section.Flow.DefaultTargetSection);
            }
        }
        finally
        {
            _isUpdatingChapterChoiceManager = false;
        }
    }

    private void PopulateChapterChoiceManagerTargets()
    {
        ChapterChoiceManagerTargetComboBox.Items.Clear();
        ChapterChoiceManagerTargetComboBox.Items.Add(new StoryChoiceManagerTargetItem(string.Empty, null, "进入共通线"));
        if (_chapterChoiceManagerSession is null)
        {
            return;
        }
        foreach (var section in _chapterChoiceManagerSession.Flow.Sections)
        {
            ChapterChoiceManagerTargetComboBox.Items.Add(new StoryChoiceManagerTargetItem(
                section.FirstRowId,
                section.Section,
                $"第 {section.Section} 小节 · {section.FirstSentencePreview}"));
        }
    }

    private void SelectChapterChoiceManagerTarget(int? section)
    {
        ChapterChoiceManagerTargetComboBox.SelectedItem = ChapterChoiceManagerTargetComboBox.Items
            .OfType<StoryChoiceManagerTargetItem>()
            .FirstOrDefault(item => item.Section == section) ?? ChapterChoiceManagerTargetComboBox.Items[0];
    }

    private void ApplyChapterChoiceManagerOptionEdit()
    {
        if (_isUpdatingChapterChoiceManager || _chapterChoiceManagerSession is null ||
            ChapterChoiceManagerOptionList.SelectedItem is not StoryChoiceManagerOptionItem option)
        {
            return;
        }

        var targetId = (ChapterChoiceManagerTargetComboBox.SelectedItem as StoryChoiceManagerTargetItem)?.FirstRowId ?? string.Empty;
        _storyChoiceManagerService.UpdateOption(
            _chapterChoiceManagerSession,
            option.ChoiceFunction,
            option.FlowOption.OptionIndex,
            ChapterChoiceManagerOptionTextBox.Text,
            targetId);
        RefreshChapterChoiceManagerIssues();
        RefreshChapterChoiceManagerRoutePreview(option.ChoiceFunction, option.FlowOption.OptionIndex);
    }

    private void RefreshChapterChoiceManagerRoutePreview(string choiceFunction, int optionIndex)
    {
        var updated = _chapterChoiceManagerSession?.Flow.Sections
            .SelectMany(section => section.Options)
            .FirstOrDefault(option => string.Equals(option.ChoiceFunction, choiceFunction, StringComparison.OrdinalIgnoreCase) &&
                                      option.OptionIndex == optionIndex);
        if (updated is null)
        {
            return;
        }
        ChapterChoiceManagerExplicitRoute.Text = updated.ExplicitTargetSection is null
            ? "显式目标：进入共通线"
            : $"显式目标：第 {updated.ExplicitTargetSection} 小节";
        ChapterChoiceManagerEffectiveRoute.Text = updated.EffectiveTargetSection is null
            ? "实际路线：章节结束"
            : $"实际路线：StoryJudge={updated.OptionKey} -> 第 {updated.EffectiveTargetSection} 小节";
        ChapterChoiceManagerTargetPreview.Text = GetChapterChoiceManagerTargetPreview(updated.EffectiveTargetSection);
    }

    private void RefreshChapterChoiceManagerIssues()
    {
        if (_chapterChoiceManagerSession is null)
        {
            ChapterChoiceManagerIssueText.Text = string.Empty;
            return;
        }
        var issues = _storyChoiceManagerService.Validate(_chapterChoiceManagerSession);
        ChapterChoiceManagerIssueText.Text = issues.Count == 0 ? "未发现结构问题" : string.Join(" · ", issues);
        ChapterChoiceManagerSaveButton.IsEnabled = issues.Count == 0;
    }

    private void ChapterChoiceManagerSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => RefreshChapterChoiceManagerSections();
    private void ChapterChoiceManagerSectionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshChapterChoiceManagerOptions();
    private void ChapterChoiceManagerOptionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshChapterChoiceManagerDetail();
    private void ChapterChoiceManagerOptionTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyChapterChoiceManagerOptionEdit();
    private void ChapterChoiceManagerTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyChapterChoiceManagerOptionEdit();

    private void ChapterChoiceManagerCustomToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingChapterChoiceManager || _chapterChoiceManagerSession is null ||
            ChapterChoiceManagerSectionList.SelectedItem is not StoryChoiceManagerSectionItem section)
        {
            return;
        }
        _storyChoiceManagerService.SetCustomSection(_chapterChoiceManagerSession, section.Section, ChapterChoiceManagerCustomToggle.IsOn);
        RefreshChapterChoiceManagerSections();
    }

    private void ChapterChoiceManagerSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_chapterChoiceManagerSession is null)
        {
            return;
        }
        try
        {
            _storyChoiceManagerService.Save(_chapterChoiceManagerSession);
            var chapterName = _chapterChoiceManagerSession.Chapter.Name;
            HideChapterChoiceManager();
            if (_currentProject is not null) { LoadChapters(_currentProject); }
            ShowFloatingTip(InfoBarSeverity.Success, "选项与分支已保存", chapterName);
        }
        catch (Exception ex)
        {
            ChapterChoiceManagerIssueText.Text = ex.Message;
            AppendLog(LogKind.Error, "保存章节选项与分支失败。", ex);
        }
    }

    private void ChapterChoiceManagerCancelButton_Click(object sender, RoutedEventArgs e) => HideChapterChoiceManager();
    private void ChapterChoiceManagerHost_RightTapped(object sender, RightTappedRoutedEventArgs e) { e.Handled = true; HideChapterChoiceManager(); }
    private void ChapterChoiceManagerCard_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
    private void ChapterChoiceManagerHost_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, ChapterChoiceManagerHost)) HideChapterChoiceManager(); }

    private void ChapterChoiceManagerHost_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { HideChapterChoiceManager(); e.Handled = true; }
        else if (e.Key == VirtualKey.Enter && FocusManager.GetFocusedElement(ChapterChoiceManagerHost.XamlRoot) is not ComboBox)
        { ChapterChoiceManagerSaveButton_Click(sender, e); e.Handled = true; }
    }

    private void HideChapterChoiceManager()
    {
        ChapterChoiceManagerHost.Visibility = Visibility.Collapsed;
        _chapterChoiceManagerSession = null;
        _chapterChoiceManagerSections.Clear();
        _chapterChoiceManagerOptions.Clear();
    }

    private string GetChapterChoiceManagerTargetPreview(int? section) => section is null || _chapterChoiceManagerSession is null
        ? string.Empty
        : _chapterChoiceManagerSession.Flow.Sections.FirstOrDefault(item => item.Section == section)?.FirstSentencePreview ?? string.Empty;

    private static string GetChapterChoiceManagerStructureSummary(StoryBranchSectionFlow flow) => flow.Kind switch
    {
        StoryBranchSectionKind.Choice => $"{flow.Options.Count} 个选项",
        StoryBranchSectionKind.Branch => "分支剧情",
        StoryBranchSectionKind.Common => "分支汇合",
        StoryBranchSectionKind.Custom => "保留手工逻辑",
        StoryBranchSectionKind.Terminal => "没有后续路线",
        _ => "顺序剧情"
    };

    private static string GetChapterChoiceManagerStatus(StoryBranchSectionKind kind) => kind switch
    {
        StoryBranchSectionKind.Choice => "选项",
        StoryBranchSectionKind.Branch => "分支内容",
        StoryBranchSectionKind.Common => "共通线",
        StoryBranchSectionKind.Custom => "自定义",
        StoryBranchSectionKind.Terminal => "结尾",
        _ => "顺序"
    };
}
