using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.Views;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private async Task ShowAiStoryFillWorkflowAsync(ChapterInfo chapter)
    {
        try
        {
            var action = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "AI 剧情填表",
                AiStoryFillDialogContentFactory.CreateOverviewContent(chapter),
                "导出 V1 任务",
                "取消",
                "导入填写结果",
                ContentDialogButton.Primary));

            if (action == DialogResultKind.Primary)
            {
                await ExportAiStoryFillTaskAsync(chapter);
            }
            else if (action == DialogResultKind.Secondary)
            {
                await ImportAiStoryFillResultAsync(chapter);
            }
        }
        catch (Exception ex)
        {
            ShowFloatingTip(InfoBarSeverity.Error, "AI 剧情填表失败", ex.Message, ex, "AI 剧情填表工作流失败。");
        }
    }

    private async Task ExportAiStoryFillTaskAsync(ChapterInfo chapter)
    {
        var paragraphText = await _dialogService.PromptTextAsync(new TextInputDialogRequest(
            "导出 V1 任务",
            "原稿起始段落",
            "0",
            "例如 1241",
            "选择导出位置",
            "取消",
            "该数字会写入任务协议，供外接程序定位原稿。"));
        if (paragraphText is null)
        {
            return;
        }
        if (!int.TryParse(paragraphText, out var sourceStartParagraph) || sourceStartParagraph < 0)
        {
            ShowFloatingTip(InfoBarSeverity.Warning, "无法导出任务", "原稿起始段落必须是 0 或更大的整数。");
            return;
        }

        var selectedFolder = await PickAiStoryFillFolderAsync();
        if (selectedFolder is null)
        {
            return;
        }

        var document = _storyDocumentRepository.Load(chapter);
        var (characters, customSpeakers, _) = BuildAiStoryFillSpeakerContext(chapter, document);
        var safeCode = TextUtility.SanitizeBackupFileName(chapter.Code);
        var packagePath = Path.Combine(selectedFolder.Path, $"TFAC-AI-Story-{safeCode}-{DateTime.Now:yyyyMMdd-HHmmss}");
        var export = _aiStoryFillService.CreateTaskPackage(
            chapter,
            document,
            characters,
            packagePath,
            sourceStartParagraph,
            customSpeakers);
        ShowFloatingTip(
            InfoBarSeverity.Success,
            "V1 任务已导出",
            export.PackageDirectory,
            logMessage: $"导出 AI 剧情填表任务：{chapter.Code} -> {export.PackageDirectory}");
    }

    private async Task ImportAiStoryFillResultAsync(ChapterInfo chapter)
    {
        var selectedFolder = await PickAiStoryFillFolderAsync();
        if (selectedFolder is null)
        {
            return;
        }

        var document = _storyDocumentRepository.Load(chapter);
        var (_, _, knownSpeakers) = BuildAiStoryFillSpeakerContext(chapter, document);
        var preview = _aiStoryFillService.LoadImportPreview(chapter, selectedFolder.Path, knownSpeakers);
        var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            $"预览合并：{chapter.Name}",
            AiStoryFillDialogContentFactory.CreatePreviewContent(preview),
            preview.CanImport ? $"合并 {preview.PendingIncludeCount} 条" : string.Empty,
            "关闭",
            DefaultButton: preview.CanImport ? ContentDialogButton.Primary : ContentDialogButton.Close));
        if (result != DialogResultKind.Primary || !preview.CanImport)
        {
            return;
        }

        var import = _aiStoryFillService.ApplyImport(chapter, preview);
        if (_currentProject is not null)
        {
            LoadChapters(_currentProject);
        }
        ShowFloatingTip(
            InfoBarSeverity.Success,
            "AI 剧情已合并",
            $"追加 {import.ImportedCount} 条，合并前备份已保留。",
            logMessage: $"导入 AI 剧情填表结果：{chapter.Code}，新增 {import.ImportedCount} 条，备份 {import.BackupPath}");
    }

    private async Task<StorageFolder?> PickAiStoryFillFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFolderAsync();
    }

    private (List<CharacterInfo> Characters, List<string> CustomSpeakers, HashSet<string> KnownSpeakers)
        BuildAiStoryFillSpeakerContext(ChapterInfo chapter, StoryDocument document)
    {
        var project = FindProjectForChapter(chapter);
        var assetLibrary = project is null ? null : ResolveProjectAssetLibrary(project);
        var characters = assetLibrary is null ? [] : GetCharactersForAssetLibrary(assetLibrary);
        var characterCodes = characters
            .Select(character => character.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var customSpeakers = document.Rows
            .Select(row => row.Get("TalkChar").Trim())
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker) && !characterCodes.Contains(speaker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(speaker => speaker, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var knownSpeakers = new HashSet<string>(characterCodes, StringComparer.OrdinalIgnoreCase) { string.Empty };
        knownSpeakers.UnionWith(customSpeakers);
        return (characters, customSpeakers, knownSpeakers);
    }
}
