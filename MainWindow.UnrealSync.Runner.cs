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
        private UnrealSyncValidation ValidateUnrealSync(bool useUiValues)
        {
            var enginePath = useUiValues ? UnrealEnginePathTextBox.Text.Trim() : _appSettings.UnrealEnginePath ?? string.Empty;
            var unrealProjectPath = useUiValues ? UnrealProjectPathTextBox.Text.Trim() : _appSettings.UnrealProjectPath ?? string.Empty;
            var contentFolderPath = useUiValues ? UnrealContentFolderTextBox.Text.Trim() : _appSettings.UnrealContentFolderPath ?? string.Empty;
            var projectFolderName = useUiValues
                ? (UnrealSyncProjectComboBox.SelectedItem as ComboBoxItem)?.Tag as string
                : _appSettings.UnrealToolProjectFolderName;

            var planItems = new List<string>();
            var warnings = new List<string>();
            var errors = new List<string>();

            var editorPath = UnrealSyncService.ResolveEditorExecutable(enginePath);
            if (editorPath is null)
            {
                errors.Add("请选择有效的 UnrealEditor.exe 或 UnrealEditor-Cmd.exe。");
            }
            else
            {
                planItems.Add($"引擎执行器：{editorPath}");
            }

            if (string.IsNullOrWhiteSpace(unrealProjectPath) || !File.Exists(unrealProjectPath) || !unrealProjectPath.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("请选择有效的 .uproject 文件。");
            }

            var project = GetProjects()
                .FirstOrDefault(item => string.Equals(item.FolderName, projectFolderName, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                errors.Add("请选择有效的工作台项目。");
            }

            AssetLibraryInfo? assetLibrary = null;
            if (project is not null)
            {
                assetLibrary = ResolveProjectAssetLibrary(project);
                if (assetLibrary is null)
                {
                    errors.Add($"工作台项目 {project.Name} 没有关联可用素材库。");
                }
                else
                {
                    planItems.Add($"工作台项目：{project.Name} / {project.Code}，素材库：{assetLibrary.Name}");
                }
            }

            string? contentRootPath = null;
            string? targetAssetRoot = null;
            if (!string.IsNullOrWhiteSpace(unrealProjectPath) && File.Exists(unrealProjectPath))
            {
                var unrealProjectRoot = Path.GetDirectoryName(unrealProjectPath)!;
                contentRootPath = Path.Combine(unrealProjectRoot, "Content");
                if (!Directory.Exists(contentRootPath))
                {
                    errors.Add("虚幻项目缺少 Content 文件夹。");
                }

            }

            if (string.IsNullOrWhiteSpace(contentFolderPath) || !Directory.Exists(contentFolderPath))
            {
                errors.Add("请选择 Content 下的目标同步文件夹。");
            }
            else if (contentRootPath is not null && Directory.Exists(contentRootPath))
            {
                if (!IsPathInsideDirectoryOrEqual(contentFolderPath, contentRootPath))
                {
                    errors.Add("目标同步文件夹必须位于所选虚幻项目的 Content 目录内。");
                }
                else if (!string.Equals(new DirectoryInfo(contentFolderPath).Name, "Narrative", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("目标同步文件夹必须命名为 Narrative，避免素材同步到错误目录。");
                }
                else
                {
                    targetAssetRoot = ToUnrealAssetPath(contentRootPath, contentFolderPath);
                    planItems.Add($"目标内容路径：{targetAssetRoot}");
                    var missingFolders = UnrealSyncService.ExpectedNarrativeFolders
                        .Where(folder => !Directory.Exists(Path.Combine(contentFolderPath, folder)))
                        .ToList();
                    if (missingFolders.Count > 0)
                    {
                        warnings.Add($"目标目录缺少参考结构：{string.Join(", ", missingFolders)}。同步时会在引擎导入阶段尝试创建。");
                    }
                }
            }

            if (contentFolderPath.Length > 0 && Directory.Exists(contentFolderPath) && assetLibrary is not null && project is not null)
            {
                planItems.AddRange(CreateUnrealSyncPlanItems(project, assetLibrary, contentFolderPath));
                if (assetLibrary.IsPortraitPreviewEnabled)
                {
                    var missingPortraitPreviewNames = GetMissingPortraitPreviewLayerNames(assetLibrary);
                    if (missingPortraitPreviewNames.Count > 0)
                    {
                        warnings.Add($"小预览已启用，但有 {missingPortraitPreviewNames.Count} 个立绘素材未设置预览：{string.Join(", ", missingPortraitPreviewNames.Take(8))}{(missingPortraitPreviewNames.Count > 8 ? "..." : string.Empty)}");
                    }
                }
            }

            var canSync = errors.Count == 0 && editorPath is not null && project is not null && assetLibrary is not null && targetAssetRoot is not null;
            var severity = errors.Count > 0
                ? InfoBarSeverity.Error
                : warnings.Count > 0
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success;
            var title = errors.Count > 0
                ? "关联不完整"
                : warnings.Count > 0
                    ? "可以同步，但需要确认"
                    : "关联一致，可以同步";
            var message = errors.Count > 0
                ? string.Join(" ", errors)
                : warnings.Count > 0
                    ? string.Join(" ", warnings)
                    : "工具箱项目、素材库和虚幻目标目录已通过基础验证。";
            var summary = project is null || assetLibrary is null
                ? "等待选择工作台项目和素材库。"
                : CreateUnrealSyncSummary(project, assetLibrary);

            var context = canSync
                ? new UnrealSyncContext(editorPath!, unrealProjectPath, contentFolderPath, targetAssetRoot!, project!, assetLibrary!)
                : null;
            return new UnrealSyncValidation(severity, title, message, summary, canSync, context, planItems);
        }

        private List<string> CreateUnrealSyncPlanItems(ProjectInfo project, AssetLibraryInfo assetLibrary, string contentFolderPath)
        {
            var backgroundCount = BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(assetLibrary)).Count;
            var musicCount = AudioAssetService.GetFilePaths(GetMusicFolderPath(assetLibrary)).Count;
            var sceneCount = AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(assetLibrary)).Count;
            var soundEffectCount = AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(assetLibrary)).Count;
            var voiceCount = ProjectVoiceAssetService.GetVoiceFilePaths(project).Count;
            var csvCount = GetProjectStoryCsvPaths(project).Count;
            var characterLayerCount = GetProjectCharacterLayerImportPaths(assetLibrary).Count;
            var portraitPreviewCount = GetProjectPortraitPreviewImportPaths(assetLibrary).Count;
            var missingPortraitPreviewCount = assetLibrary.IsPortraitPreviewEnabled
                ? GetMissingPortraitPreviewLayerNames(assetLibrary).Count
                : 0;
            var lustrationRowCount = GetCharactersForAssetLibrary(assetLibrary).Count;
            var existingBackgroundCount = UnrealSyncService.CountAssets(Path.Combine(contentFolderPath, "BackGround"));
            var existingMusicCount = UnrealSyncService.CountAssets(Path.Combine(contentFolderPath, "BGM"));
            var existingSceneCount = UnrealSyncService.CountAssets(Path.Combine(contentFolderPath, "Scene_Effect"));
            var existingVoiceCount = UnrealSyncService.CountAssetsRecursive(Path.Combine(contentFolderPath, "Voice"));

            return
            [
                $"背景图：源文件 {backgroundCount} 个，引擎内已有 {existingBackgroundCount} 个 .uasset。",
                $"音乐：源文件 {musicCount} 个，引擎内已有 {existingMusicCount} 个 .uasset。",
                $"环境音/特殊音效：源文件 {sceneCount + soundEffectCount} 个，引擎内已有 {existingSceneCount} 个 .uasset。",
                $"文本语音：源文件 {voiceCount} 个，引擎内已有 {existingVoiceCount} 个 .uasset，将只同步 wav 到 Voice 文件夹。",
                $"素材索引表：背景 {backgroundCount}、BGM {musicCount}、环境音 {sceneCount}、特殊音效 {soundEffectCount}，将同步到 ExcelTexts 的 4 张 DataTable。",
                $"CSV 表格：{csvCount} 个，将导入到 ExcelTexts。",
                $"立绘图层：{characterLayerCount} 个，将按 Lustration/角色/图层目录导入。",
                assetLibrary.IsPortraitPreviewEnabled
                    ? $"小预览：源文件 {portraitPreviewCount} 个，缺失设置 {missingPortraitPreviewCount} 个，将同步到 Lustration/角色/Log_Preview 和 DA_Portraits。"
                    : "小预览：当前素材库未启用。",
                $"立绘信息表：{lustrationRowCount} 个角色，将同步到 Lustration/DA_LustrationInfor。"
            ];
        }

        private static string CreateUnrealSyncSummary(ProjectInfo project, AssetLibraryInfo assetLibrary)
        {
            return $"当前关联：工作台项目 {project.Name} / {project.Code}，素材库 {assetLibrary.Name}。同步不会自动触发，必须点击“确认同步”。";
        }

        private UnrealSyncResult RunUnrealSync(
            UnrealSyncContext context,
            UnrealSyncChangePlan changePlan,
            IProgress<UnrealSyncProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var filters = File.Exists(GetCharacterFilterIndexPath(context.AssetLibrary))
                ? _characterFilterService.Read(context.AssetLibrary)
                : [];
            return _unrealSyncService.Run(context, changePlan, filters, progress, cancellationToken);
        }

        private List<UnrealStoryTableSyncEntry> BuildStoryTableSyncEntries(UnrealSyncContext context)
        {
            return _unrealSyncService.BuildStoryTableSyncEntries(context, _projectWorkspaceService.GetChapters(context.Project));
        }

        private UnrealSyncChangePlan BuildUnrealSyncChangePlan(UnrealSyncContext context, bool forceFullSync = false)
        {
            var characters = GetCharactersForAssetLibrary(context.AssetLibrary);
            var allStoryTables = BuildStoryTableSyncEntries(context);
            var assetIndexTableCacheFolder = Path.Combine(context.Project.Path, ToolsFolderName, UnrealAssetIndexTablesFolderName);

            return _unrealSyncService.BuildChangePlan(
                context,
                forceFullSync,
                characters,
                BackgroundImageService.GetFilePaths(GetBackgroundFolderPath(context.AssetLibrary)),
                AudioAssetService.GetFilePaths(GetMusicFolderPath(context.AssetLibrary)),
                AudioAssetService.GetFilePaths(GetAmbientSoundFolderPath(context.AssetLibrary)),
                AudioAssetService.GetFilePaths(GetSoundEffectFolderPath(context.AssetLibrary)),
                ProjectVoiceAssetService.GetVoiceFilePaths(context.Project),
                allStoryTables,
                assetIndexTableCacheFolder,
                GetPortraitPreviewPathsByLayerFileName);
        }

        private List<string> GetProjectStoryCsvPaths(ProjectInfo project)
        {
            return _unrealSyncService.GetProjectStoryCsvPaths(project, _projectWorkspaceService.GetChapters(project));
        }

        private List<CharacterInfo> GetCharactersForAssetLibrary(AssetLibraryInfo assetLibrary)
        {
            return _characterWorkspaceService.GetCharactersByCode(assetLibrary);
        }

        private List<string> GetProjectCharacterLayerImportPaths(AssetLibraryInfo assetLibrary)
        {
            var result = new List<string>();
            foreach (var character in GetCharactersForAssetLibrary(assetLibrary))
            {
                result.AddRange(UnrealSyncService.GetCharacterLayerImportPaths(character, CharacterLayerKind.Cloth));
                result.AddRange(UnrealSyncService.GetCharacterLayerImportPaths(character, CharacterLayerKind.Face));
                result.AddRange(UnrealSyncService.GetCharacterLayerImportPaths(character, CharacterLayerKind.Adorn));
            }

            return result;
        }

        private IReadOnlyDictionary<string, string> GetPortraitPreviewPathsByLayerFileName(CharacterInfo character)
        {
            _characterLayerAssetService.CleanupPortraitPreviewMeta(character);
            return _characterLayerAssetService.GetPortraitPreviewPathsByLayerFileName(character);
        }

        private List<string> GetProjectPortraitPreviewImportPaths(AssetLibraryInfo assetLibrary)
        {
            return GetCharactersForAssetLibrary(assetLibrary)
                .SelectMany(character => GetPortraitPreviewPathsByLayerFileName(character).Values)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> GetMissingPortraitPreviewLayerNames(AssetLibraryInfo assetLibrary)
        {
            return GetCharactersForAssetLibrary(assetLibrary)
                .SelectMany(character => _characterLayerAssetService
                    .GetMissingPortraitPreviewLayerNames(character)
                    .Select(fileName => $"{character.Code}/{fileName}"))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

    }
}
