using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools.Services;

internal sealed class UnrealSyncService
{
    private const int CurrentUnrealSyncStateVersion = 4;
    public const string MusicPlayerBlueprintAssetPath = UnrealSyncPathPolicy.MusicPlayerAsset;
    public const string StoryBranchDataTableAssetPath = UnrealSyncPathPolicy.StoryBranchTableAsset;
    private const string InvalidUnrealObjectNameCharacters = "\\:*?\"<>|' ,/.&!~@#(){}[]=;^%$`";

    // 这里原来有一份 ExpectedNarrativeFolders（BackGround / BGM / ExcelTexts / Lustration /
    // Scene_Effect / Voice），已删 —— 落点整组换到 UnrealSyncPathPolicy.ExpectedContentFolders，
    // 而且那份是**相对 Content** 的路径（Content 由 .uproject 推导，不再手选目标夹）。
    // 与之配套的那条「目标同步文件夹必须命名为 Narrative」的硬校验也一并退休。

    private readonly JsonSerializerOptions _jsonOptions;
    private readonly StoryCsvService _storyCsvService;
    private readonly StoryDocumentRepository _storyDocumentRepository;

    /// <summary>同步一次的超时。老实现里这个 30 分钟写死在等待循环里。</summary>
    private static readonly TimeSpan UnrealSyncTimeout = TimeSpan.FromMinutes(30);

    /// <summary>在线 / 离线的选路与启动构造。见 <see cref="UnrealPythonTaskExecutionService"/>。</summary>
    private readonly UnrealPythonTaskExecutionService _taskExecutionService = new();

    public UnrealSyncService()
        : this(new JsonSerializerOptions { WriteIndented = true }, new StoryCsvService())
    {
    }

    public UnrealSyncService(JsonSerializerOptions jsonOptions)
        : this(jsonOptions, new StoryCsvService())
    {
    }

    public UnrealSyncService(JsonSerializerOptions jsonOptions, StoryCsvService storyCsvService)
    {
        _jsonOptions = jsonOptions;
        _storyCsvService = storyCsvService;
        _storyDocumentRepository = new StoryDocumentRepository(jsonOptions);
    }

    public List<UnrealLustrationSyncEntry> BuildLustrationSyncEntries(
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters)
    {
        return characters
            .Select(character =>
            {
                var clothDestination = GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Cloth);
                var faceDestination = GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Face);
                var adornDestination = GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Adorn);
                var clothRefs = GetCharacterLayerImportPaths(character, CharacterLayerKind.Cloth)
                    .Select(path => BuildAssetObjectPath(clothDestination, path))
                    .ToList();
                var faceRefs = GetCharacterLayerImportPaths(character, CharacterLayerKind.Face)
                    .Select(path => BuildAssetObjectPath(faceDestination, path))
                    .ToList();
                var adornRefs = new List<string?> { null };
                adornRefs.AddRange(GetCharacterLayerImportPaths(character, CharacterLayerKind.Adorn)
                    .Select(path => (string?)BuildAssetObjectPath(adornDestination, path)));
                var color = ParseColor(character.ColorHex, Windows.UI.Color.FromArgb(255, 217, 232, 255));

                return new UnrealLustrationSyncEntry(
                    character.Code,
                    character.Name,
                    new UnrealLinearColor(color.R / 255d, color.G / 255d, color.B / 255d, color.A / 255d),
                    clothRefs,
                    faceRefs,
                    adornRefs);
            })
            .ToList();
    }

    public List<UnrealLustrationSyncEntry> BuildPortraitSyncEntries(
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters,
        Func<CharacterInfo, IReadOnlyDictionary<string, string>> getPortraitPreviewPathsByLayerFileName)
    {
        return characters
            .Select(character =>
            {
                var previewPaths = getPortraitPreviewPathsByLayerFileName(character);
                var destination = GetPortraitPreviewDestinationPath(context, character);
                var clothRefs = GetCharacterLayerImportPaths(character, CharacterLayerKind.Cloth)
                    .Select(path => BuildPortraitPreviewAssetReference(destination, previewPaths, path))
                    .Where(reference => reference is not null)
                    .Cast<string>()
                    .ToList();
                var faceRefs = GetCharacterLayerImportPaths(character, CharacterLayerKind.Face)
                    .Select(path => BuildPortraitPreviewAssetReference(destination, previewPaths, path))
                    .Where(reference => reference is not null)
                    .Cast<string>()
                    .ToList();
                var adornRefs = new List<string?> { null };
                adornRefs.AddRange(GetCharacterLayerImportPaths(character, CharacterLayerKind.Adorn)
                    .Select(path => BuildPortraitPreviewAssetReference(destination, previewPaths, path)));
                var color = ParseColor(character.ColorHex, Windows.UI.Color.FromArgb(255, 217, 232, 255));

                return new UnrealLustrationSyncEntry(
                    character.Code,
                    character.Name,
                    new UnrealLinearColor(color.R / 255d, color.G / 255d, color.B / 255d, color.A / 255d),
                    clothRefs,
                    faceRefs,
                    adornRefs);
            })
            .ToList();
    }

    public List<UnrealSyncImportGroup> BuildImportGroups(
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters,
        IReadOnlyList<ImageSetSyncInput> imageSets,
        IReadOnlyCollection<string> musicPaths,
        IReadOnlyCollection<string> ambientPaths,
        IReadOnlyCollection<string> soundEffectPaths,
        IReadOnlyCollection<string> voicePaths)
    {
        var groups = new List<UnrealSyncImportGroup>();
        // 图集分类逐个建导入组 —— 背景图、道具图片、以及以后任何一个都走这里。
        // 加分类**不用改这个函数**：多造一个 ImageSetSyncInput 就行。
        foreach (var imageSet in imageSets)
        {
            AddImportGroup(groups, imageSet.DestinationFolder, imageSet.FilePaths);
        }

        AddImportGroup(groups, UnrealSyncPathPolicy.BgmFolder, musicPaths);
        AddImportGroup(groups, UnrealSyncPathPolicy.SceneEffectFolder, ambientPaths.Concat(soundEffectPaths).ToList());
        AddProjectVoiceImportGroups(groups, context, voicePaths);

        foreach (var character in characters)
        {
            AddImportGroup(groups, GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Cloth), GetCharacterLayerImportPaths(character, CharacterLayerKind.Cloth));
            AddImportGroup(groups, GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Face), GetCharacterLayerImportPaths(character, CharacterLayerKind.Face));
            AddImportGroup(groups, GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Adorn), GetCharacterLayerImportPaths(character, CharacterLayerKind.Adorn));
        }

        return groups;
    }

    public List<UnrealSyncImportGroup> BuildPortraitPreviewImportGroups(
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters,
        Func<CharacterInfo, IReadOnlyDictionary<string, string>> getPortraitPreviewPathsByLayerFileName)
    {
        var groups = new List<UnrealSyncImportGroup>();
        foreach (var character in characters)
        {
            var previewPaths = getPortraitPreviewPathsByLayerFileName(character).Values.ToList();
            AddImportGroup(groups, GetPortraitPreviewDestinationPath(context, character), previewPaths);
        }

        return groups;
    }

    public List<UnrealStoryTableSyncEntry> BuildStoryTableSyncEntries(
        UnrealSyncContext context,
        IReadOnlyList<UnrealStoryTableSource> sources)
    {
        var result = new List<UnrealStoryTableSyncEntry>();
        foreach (var source in sources)
        {
            foreach (var entry in source.CsvEntries)
            {
                var assetName = entry.AssetName;
                var tableFolder = BuildStoryTableFolder(context, source.Chapter, entry.IsSectionCsv);
                result.Add(new UnrealStoryTableSyncEntry(
                    entry.CsvPath,
                    $"{tableFolder}/{assetName}.{assetName}",
                    "/Script/GALLibrary.StoryStruct",
                    BuildLegacyStoryTableAssets(context, source.Chapter, entry.CsvPath, tableFolder, assetName),
                    source.Chapter.Code,
                    _storyCsvService.TryParseStorySectionFromFileName(source.Chapter, entry.CsvPath) ?? 1));
            }
        }

        return result
            .OrderBy(entry => entry.TableAsset, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<UnrealStoryTableSyncEntry> BuildStoryTableSyncEntries(
        UnrealSyncContext context,
        IReadOnlyList<ChapterInfo> chapters)
    {
        var sources = chapters
            .Select(chapter => new UnrealStoryTableSource(chapter, GetChapterStoryCsvPathsForSync(chapter)))
            .Where(source => source.CsvEntries.Count > 0)
            .ToList();
        return BuildStoryTableSyncEntries(context, sources);
    }

    public List<string> GetProjectStoryCsvPaths(IReadOnlyList<ChapterInfo> chapters)
    {
        return chapters
            .SelectMany(chapter => GetChapterStoryCsvPathsForSync(chapter).Select(entry => entry.CsvPath))
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void WriteLustrationInfoCsv(UnrealSyncContext context, IReadOnlyList<CharacterInfo> characters, string csvPath)
    {
        var rows = new List<string>
        {
            string.Join(",", ["", "Name", "Color", "Cloth", "Face", "Adorn"])
        };

        foreach (var character in characters)
        {
            var clothDestination = GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Cloth);
            var faceDestination = GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Face);
            var adornDestination = GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Adorn);
            var clothRefs = GetCharacterLayerImportPaths(character, CharacterLayerKind.Cloth)
                .Select(path => BuildTextureReference(clothDestination, path))
                .ToList();
            var faceRefs = GetCharacterLayerImportPaths(character, CharacterLayerKind.Face)
                .Select(path => BuildTextureReference(faceDestination, path))
                .ToList();
            var adornRefs = new List<string> { "None" };
            adornRefs.AddRange(GetCharacterLayerImportPaths(character, CharacterLayerKind.Adorn)
                .Select(path => BuildTextureReference(adornDestination, path)));

            rows.Add(string.Join(",",
            [
                EscapeCsv(character.Code),
                EscapeCsv(character.Name),
                EscapeCsv(ToLinearColorLiteral(character.ColorHex)),
                EscapeCsv(ToArrayLiteral(clothRefs)),
                EscapeCsv(ToArrayLiteral(faceRefs)),
                EscapeCsv(ToArrayLiteral(adornRefs))
            ]));
        }

        File.WriteAllLines(csvPath, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public void WriteManifest(
        UnrealSyncContext context,
        UnrealSyncChangePlan changePlan,
        IReadOnlyList<CharacterFilterEntry> filters,
        string manifestPath,
        string progressPath,
        string resultPath)
    {
        var manifest = new
        {
            ProtocolVersion = 1,
            // 资产名里**会被 Unreal 换成下划线**的那些字符。
            //
            // 为什么要把这条规则带进清单：脚本要按它算"这个素材该落到哪个包"，
            // 而 Unreal 的导入器也按它清洗文件名 —— 两边必须**同一条规则**，
            // 否则"我算的期望路径"和"它实际建出来的资产"对不上，
            // 复扫就会把成功的导入报成失败（假红）。
            //
            // 2026-10-13 实测：源文件名叫 Vo-012-.不要.wav，Unreal 把那个点换成了下划线
            // （Vo-012-_不要），而脚本没换 —— 55 项语音全部"验证没通过"，
            // 于是整次同步被判成"未完整完成"，连同步状态都不写，
            // 下一次检测又是"全部都有差异"。一环扣一环，起点就是这一个点。
            //
            // 规则**只在这边维护一份**（旧版就是这么做的：C# 算好名字放进清单，
            // 脚本直接照用）。这里选择把字符集带过去，而不是把成品名字一个个塞进清单，
            // 是因为清单里的结构（每组一个目录 + 一串文件）更紧凑，不值得为它改结构。
            ImportNameInvalidChars = InvalidUnrealObjectNameCharacters,
            ProgressPath = progressPath,
            ResultPath = resultPath,
            // 资产类型清单的落点：和进度 / 结果那几个文件同一个目录（Saved/GalExcleTools）。
            // 脚本会把这次涉及到的资产**当时的类名**写进去，给"同步前检测"用 ——
            // 类型必须是问 Unreal 要来的，不是从文件里猜的（零境也是这个路子）。
            AssetTypesPath = Path.Combine(
                Path.GetDirectoryName(resultPath) ?? string.Empty,
                UnrealAssetTypeManifest.FileName),
            GeneratedAt = DateTimeOffset.Now,
            ToolProject = new
            {
                context.Project.Name,
                context.Project.Code,
                context.Project.Path
            },
            UnrealProjectPath = context.UnrealProjectPath,
            TargetRoot = UnrealSyncPathPolicy.AssetMaterialRoot,
            LustrationInfo = new
            {
                DataAsset = UnrealSyncPathPolicy.LustrationInfoAsset,
                MapProperty = "Infor",
                ShouldUpdate = changePlan.LustrationChanged,
                Rows = changePlan.LustrationChanged ? changePlan.LustrationRows : []
            },
            PortraitsInfo = new
            {
                DataAsset = UnrealSyncPathPolicy.PortraitsInfoAsset,
                MapProperty = "Infor",
                ShouldUpdate = changePlan.PortraitsChanged,
                Rows = changePlan.PortraitsChanged ? changePlan.PortraitRows : []
            },
            StoryTables = changePlan.StoryTables,
            AssetIndexTables = changePlan.AssetIndexTables,
            // 素材改名：脚本会对它们做 rename（**保留引用**），而不是"删旧 + 导新"。
            // 旧路径已经从 DeleteGroups 里摘掉了，所以不会出现"改名又删除"两条矛盾的行。
            Renames = changePlan.Renames.Select(entry => new
            {
                entry.SourceObjectPath,
                entry.TargetObjectPath,
                entry.DisplayName
            }),
            // 变量列表写入（背景图 / BGM / 环境音）。C# 侧只声明"写什么"，
            // 具体怎么落由脚本按每条请求的 Route 决定（见 UnrealVariableWriteRoute）。
            Variables = changePlan.VariablePlan.Requests.Select(request => new
            {
                request.TargetAssetPath,
                request.VariableName,
                Route = request.Route.ToString(),
                Kind = request.Kind.ToString(),
                request.ReferenceClassName,
                request.AssetObjectPaths,
                request.Numbers,
                ExportText = request.BuildPayloadText(),
                request.Description
            }),
            StoryBranches = new
            {
                DataTable = StoryBranchDataTableAssetPath,
                ShouldUpdate = changePlan.StoryBranchesChanged,
                Sources = changePlan.StoryBranchesChanged ? changePlan.StoryBranches : []
            },
            VoiceMap = new
            {
                BlueprintAsset = MusicPlayerBlueprintAssetPath,
                MapProperty = "VoiceMaps",
                ManagedTableRoot = UnrealSyncPathPolicy.ExcelTextsRoot + "/",
                ShouldUpdate = changePlan.VoiceMapsChanged,
                Entries = changePlan.VoiceMapsChanged ? changePlan.VoiceMaps : []
            },
            Imports = changePlan.ImportGroups,
            Deletes = changePlan.DeleteGroups,
            Filters = filters
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void WritePythonScript(string scriptPath, string manifestPath)
    {
        var normalizedManifestPath = manifestPath.Replace("\\", "/");
        var script = $$"""
            import json
            import os
            import sys
            import time
            import unreal

            manifest_path = r"{{normalizedManifestPath}}"
            with open(manifest_path, "r", encoding="utf-8-sig") as manifest_file:
                manifest = json.load(manifest_file)

            protocol_version = int(manifest.get("ProtocolVersion", 1))
            progress_path = manifest.get("ProgressPath")
            result_path = manifest.get("ResultPath")
            asset_types_path = manifest.get("AssetTypesPath")
            target_root = manifest.get("TargetRoot", "/Game")
            imports = manifest.get("Imports", [])
            deletes = manifest.get("Deletes", [])
            renames = manifest.get("Renames", []) or []
            tasks = []
            errors = []
            warnings = []
            # 复扫验证的逐项结果。在这里先开好，是因为 write_result 会读它 ——
            # 而 write_result 定义得比填充它的那段早（Python 里读的是调用时的值，没问题，
            # 但先声明一下更清楚）。
            verification = []
            # 改名成功的那些（旧包路径 / 新包路径），收尾时逐条复扫。
            renamed_packages = []
            imported_count = 0
            deleted_count = 0
            story_table_count = 0
            asset_index_table_count = 0
            story_branches_updated = False
            voice_maps_updated = False
            lustration_updated = False
            portraits_updated = False

            def atomic_write_json(path, payload):
                if not path:
                    return
                folder = os.path.dirname(path)
                if folder:
                    os.makedirs(folder, exist_ok=True)
                temporary_path = path + ".tmp"
                with open(temporary_path, "w", encoding="utf-8") as output_file:
                    json.dump(payload, output_file, ensure_ascii=False, indent=2)
                    output_file.flush()
                    os.fsync(output_file.fileno())
                for attempt in range(40):
                    try:
                        os.replace(temporary_path, path)
                        return
                    except PermissionError:
                        if attempt == 39:
                            raise
                        time.sleep(0.05)

            def write_progress(stage, message, percent, completed=0, total=0):
                atomic_write_json(progress_path, {
                    "ProtocolVersion": protocol_version,
                    "Stage": stage,
                    "Message": message,
                    "Percent": float(percent),
                    "Completed": int(completed),
                    "Total": int(total)
                })

            def add_warning(message):
                warnings.append(str(message))
                unreal.log_warning(str(message))

            def add_error(message):
                errors.append(str(message))
                unreal.log_error(str(message))

            def save_loaded_asset_with_retry(asset, asset_path):
                last_error = None
                for attempt in range(4):
                    try:
                        if unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=False):
                            return True
                        last_error = "save_loaded_asset returned false"
                    except Exception as exc:
                        last_error = str(exc)
                    if attempt < 3:
                        unreal.log_warning("GalExcleTools retrying asset save ({}/4): {}".format(attempt + 2, asset_path))
                        time.sleep(1.0)
                add_error("GalExcleTools failed to save asset {}: {}".format(asset_path, last_error))
                return False

            def save_asset_path_with_retry(asset_path):
                asset = unreal.EditorAssetLibrary.load_asset(asset_path)
                if not asset:
                    add_error("GalExcleTools could not load imported asset for save: {}".format(asset_path))
                    return False
                return save_loaded_asset_with_retry(asset, asset_path)

            def write_result(completed):
                atomic_write_json(result_path, {
                    "ProtocolVersion": protocol_version,
                    "Completed": bool(completed),
                    "Success": bool(completed and not errors),
                    "ImportedCount": imported_count,
                    "DeletedCount": deleted_count,
                    "StoryTableCount": story_table_count,
                    "AssetIndexTableCount": asset_index_table_count,
                    "StoryBranchesUpdated": story_branches_updated,
                    "VoiceMapsUpdated": voice_maps_updated,
                    "LustrationUpdated": lustration_updated,
                    "PortraitsUpdated": portraits_updated,
                    "Errors": errors,
                    "Warnings": warnings,
                    # 执行后复扫的逐项结果。C# 那边拿它判成败 ——
                    # 计数对得上但内容是错的，就是靠这一段拦住的。
                    "Verification": verification
                })

            def sync_excepthook(exception_type, exception_value, exception_traceback):
                add_error("GalExcleTools unexpected sync failure: {}".format(exception_value))
                try:
                    write_result(False)
                    write_progress("Failed", "Unreal 同步执行失败", 100)
                finally:
                    sys.__excepthook__(exception_type, exception_value, exception_traceback)

            sys.excepthook = sync_excepthook
            write_progress("ReadManifest", "Unreal 已读取同步清单", 60)

            # ── 素材改名 ────────────────────────────────────────────────
            # **必须排在导入之前**。导入目标就是改名目标（同一个源文件的新名字）：
            # 先改名、再导入，等于"把旧资产挪到新路径，然后把新内容写进去"——
            # 资产的**身份（GUID）不变**，Unreal 里引用它的地方就不受影响。
            # 反过来先导入的话，新路径上已经有资产了，rename 会直接失败。
            #
            # 老做法是"删旧 + 导新"：那会新建一个资产，**引用旧资产的地方全断**。
            # （零境那边专门有一个 OP_RENAME 处理这件事，判别靠"同一个 stableId
            #   现在指向了不同的对象路径"；这边靠"新路径的源哈希 == 某个待删旧路径的源哈希"。）
            if renames:
                write_progress("RenameAssets", "正在改名（保留引用）", 61)
            for rename_entry in renames:
                rename_source = (rename_entry.get("SourceObjectPath") or "").split(".", 1)[0]
                rename_target = (rename_entry.get("TargetObjectPath") or "").split(".", 1)[0]
                rename_display = rename_entry.get("DisplayName") or rename_source
                if not rename_source or not rename_target:
                    continue
                try:
                    if not unreal.EditorAssetLibrary.does_asset_exist(rename_source):
                        # 旧资产已经不在（比如上一轮就改过了）—— 不是错误，跳过。
                        unreal.log("GalExcleTools rename skipped, source missing: {}".format(rename_source))
                        continue
                    rename_folder = rename_target.rsplit("/", 1)[0]
                    if not unreal.EditorAssetLibrary.does_directory_exist(rename_folder):
                        unreal.EditorAssetLibrary.make_directory(rename_folder)
                    if not unreal.EditorAssetLibrary.rename_asset(rename_source, rename_target):
                        add_error("GalExcleTools 改名失败：{} -> {}".format(rename_source, rename_target))
                        continue
                    renamed_packages.append((rename_display, rename_source, rename_target))
                    unreal.log("GalExcleTools renamed: {} -> {}".format(rename_source, rename_target))
                except Exception as rename_error:
                    add_error("GalExcleTools 改名失败 {} -> {}：{}".format(rename_source, rename_target, rename_error))

            def asset_path_to_disk_file(asset_path):
                # /Game/A/B.C  →  <工程>/Content/A/B.uasset
                #
                # 为什么要落到"磁盘上的文件"这一层：删除这件事上有**三个口径**，
                # 而它们并不一致（2026-10-13 读引擎源码 + 实地核对磁盘查清的）：
                #   - EditorAssetLibrary.delete_asset  内部是 ObjectTools::ForceDeleteObjects，
                #     返回值是"处理了几个对象"，**不代表文件没了**；
                #   - EditorAssetLibrary.does_asset_exist 查的是**资产注册表**；
                #   - 磁盘上的 .uasset 才是最终事实。
                # 实测：动作返回 True、注册表说存在、磁盘上也确实还在。
                # 所以删除之后要**以磁盘为准**再确认一次。
                try:
                    package = str(asset_path).split(".")[0]
                    if not package.startswith("/Game/"):
                        return ""
                    relative = package[len("/Game/"):]
                    return os.path.join(
                        unreal.Paths.project_content_dir(),
                        relative.replace("/", os.sep) + ".uasset")
                except Exception:
                    return ""

            def asset_file_exists(asset_path):
                disk_file = asset_path_to_disk_file(asset_path)
                return bool(disk_file) and os.path.exists(disk_file)

            def delete_asset_if_exists(asset_path):
                if not asset_path:
                    return False
                try:
                    if not unreal.EditorAssetLibrary.does_asset_exist(asset_path):
                        return False
                    if not unreal.EditorAssetLibrary.delete_asset(asset_path):
                        add_error("GalExcleTools failed to delete extra asset: {}".format(asset_path))
                        return False

                    # ⚠️ **动作成功不等于文件没了** —— 以磁盘为准再确认一次。
                    #
                    # 2026-10-13 实测：45 个删除动作全部返回成功，复扫却有 38 个仍在，
                    # 而且**磁盘上的 .uasset 也真的还在**（M1-2 的时间戳还是几小时前的）。
                    # 三个口径不一致：动作返回"处理了几个对象"、注册表说存在、磁盘也还在。
                    #
                    # 所以：动作走完之后，**文件还在就直接删文件**。
                    # 为什么敢直接删 —— 这些路径是工具箱自己判定为多余的
                    # （"素材文件夹里不能有别的"），而且都在工程 Content 底下，
                    # 删掉它们是这次同步的**目的**，不是副作用。
                    disk_file = asset_path_to_disk_file(asset_path)
                    if disk_file and os.path.exists(disk_file):
                        try:
                            os.remove(disk_file)
                        except Exception as exc:
                            add_error("GalExcleTools 删除文件失败 {}：{}".format(disk_file, exc))

                    # 最终以磁盘为准：还在才算没删掉。
                    if asset_file_exists(asset_path):
                        still_present.append(asset_path)
                    unreal.log("GalExcleTools deleted extra asset: {}".format(asset_path))
                    return True
                except Exception as exc:
                    add_error("GalExcleTools failed to delete extra asset {}: {}".format(asset_path, exc))
                return False

            write_progress("PrepareImports", "正在准备素材导入任务", 62)
            task_targets = []

            # 资产名的清洗规则，从清单里带过来（见 C# 那边 ImportNameInvalidChars 的注释）。
            # **这边不自己维护一份** —— 两份就会走岔，走岔就是假红。
            # 那个 join：清单给的是字符串，join 字符串正好原样返回；
            # 万一将来改成数组也照样能用。两种形态都收下。
            import_name_invalid_chars = "".join(manifest.get("ImportNameInvalidChars") or "")

            def sanitize_asset_name(value):
                normalized = "".join(
                    "_" if (character.isspace() or character in import_name_invalid_chars) else character
                    for character in value)
                return normalized or "Asset"

            for group in imports:
                destination = group.get("Destination")
                files = group.get("Files", [])
                if not destination:
                    continue
                unreal.EditorAssetLibrary.make_directory(destination)
                for filename in files:
                    if not filename or not os.path.exists(filename):
                        add_error("GalExcleTools missing source file: {}".format(filename))
                        continue
                    asset_name = sanitize_asset_name(os.path.splitext(os.path.basename(filename))[0])
                    task = unreal.AssetImportTask()
                    task.set_editor_property("filename", filename)
                    task.set_editor_property("destination_path", destination)
                    task.set_editor_property("automated", True)
                    task.set_editor_property("replace_existing", True)
                    # ⚠️ 刻意**不设** replace_existing_settings：设了它，重复导入会把资产上的
                    # 设置重置成导入器默认值 —— 连人在 Unreal 里调过的压缩 / 采样一起抹掉。
                    # 这是"同步"不是"重建"：源文件换了就换内容，别动人家调好的参数。
                    # （零境那边是设了的，因为它的语义是"让 Unreal 与工具箱完全一致"；
                    #   这边是"把素材库推过去"，取舍不同。2026-10-12 想清楚的，不是漏写。）
                    try:
                        # ⚠️ **导入阶段一个都不写盘。**
                        #
                        # 以前这里是 save=True，再加上每个导入完还调一次
                        # save_asset_path_with_retry —— 等于"导一个写一次盘"。
                        # 368 个素材就是 368 次落盘，几分钟起步，而且进度条一格一格爬。
                        # 现在改成：全部导完之后统一存一次（见下面的"一次性保存"）。
                        # 2026-10-13 用户要求的：「先全部写入，最后再触发保存」。
                        task.set_editor_property("save", False)
                    except Exception:
                        pass
                    tasks.append(task)
                    # 记下"它应该落到哪个包"：导入完成后要拿它复扫验证，
                    # 而且导入不回填 imported_object_paths 时也要靠它兜底。
                    task_targets.append(("导入 {}".format(asset_name), "{}/{}".format(destination, asset_name)))

            write_progress("PrepareImports", "素材导入任务准备完成", 64, len(tasks), len(tasks))
            if tasks:
                asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
                write_progress("ImportAssets", "正在导入素材（{} 个，一次提交给引擎）".format(len(tasks)), 65, 0, len(tasks))

                # ⚠️ **一次性把全部任务交给引擎**，不要一个任务调一次。
                #
                # import_asset_tasks 收的就是**列表**。一个任务调一次的话，Unreal 每趟都要
                # 走一遍完整的编辑器事务 / 资产注册 / 通知流程 —— 几百个素材就是几百趟。
                # 用户 2026-10-13 的原话：「虚幻引擎的保存都是一次就全部保存完了，
                # 我看着像是一个一个写入，然后再一个一个保存」。
                # 上一版只把"保存"合成了批量，导入还是一次一个 —— 那一半没改到。
                #
                # 逐项报错的能力没有丢：任务对象仍然是**单个**的，导入完照样能一项项
                # 读 imported_object_paths 来核对是哪条没成 —— 只是"执行"合并成了一次。
                #
                # 兜底：整批失败就退回逐个。那样慢，但能指出到底是哪个文件 ——
                # 批量导入的代价正是"一个坏文件可能带倒一整批"，所以这条退路必须留着。
                try:
                    asset_tools.import_asset_tasks(tasks)
                except Exception as exc:
                    add_warning("GalExcleTools 整批导入失败，退回逐个导入：{}".format(exc))
                    for task in tasks:
                        try:
                            asset_tools.import_asset_tasks([task])
                        except Exception as item_exc:
                            add_error("GalExcleTools failed to import source {}: {}".format(
                                task.get_editor_property("filename"), item_exc))

                # 逐项核对：这一步只是"读结果"，不动编辑器，所以再细也不费时间。
                for task_index, task in enumerate(tasks):
                    imported_paths = []
                    try:
                        imported_paths.extend(task.get_editor_property("imported_object_paths") or [])
                    except Exception:
                        pass
                    if not imported_paths:
                        # 兜底：有些导入（或某些引擎版本）不回填 imported_object_paths，
                        # 但资产其实已经落盘了。再问一句"目标资产在不在"，
                        # 免得把成功的导入报成失败 —— 那是假红。
                        # （零境那边也是这么兜的：imported_paths 为空时再看 does_asset_exist。）
                        fallback_package = task_targets[task_index][1]
                        if unreal.EditorAssetLibrary.does_asset_exist(fallback_package):
                            imported_paths = [fallback_package]
                        else:
                            add_error("GalExcleTools import produced no asset: {}".format(task.get_editor_property("filename")))
                    if imported_paths:
                        imported_count += 1
                    # 这里以前会 for 一遍 imported_paths 逐个 save_asset_path_with_retry。
                    # 那和任务上的 save 是重复的（两边都在写盘），已经一起去掉了 ——
                    # 写盘统一挪到全部导入之后，只做一次。
                    write_progress("ImportAssets", "正在导入并保存素材", 65 + 15.0 * (task_index + 1) / len(tasks), task_index + 1, len(tasks))

                # ── 一次性保存 ────────────────────────────────────────────────
                # 「先全部写入，最后再触发保存」：导入阶段只往内存里放，
                # 全部放完之后统一落盘一次。
                #
                # only_if_is_dirty=True 是关键：只写**脏了的**包。
                # 于是这一步的代价跟"这次改了多少"成正比，而不是跟工程多大成正比 ——
                # 没动过的素材一个字节都不会重写。
                #
                # 保存失败**必须报出来**：导入的东西这时还只在内存里，
                # 存不下去就等于没导（关掉编辑器就没了）。
                if tasks:
                    save_root = manifest.get("TargetRoot") or "/Game"
                    try:
                        saved = unreal.EditorAssetLibrary.save_directory(save_root, only_if_is_dirty=True, recursive=True)
                        if not saved:
                            add_error("GalExcleTools 统一保存素材失败：{}".format(save_root))
                    except Exception as exc:
                        add_error("GalExcleTools 统一保存素材时出错：{} —— {}".format(save_root, exc))
            else:
                write_progress("ImportAssets", "本次没有需要导入的素材", 80)

            def set_first_editor_property(obj, property_names, value):
                for property_name in property_names:
                    try:
                        obj.set_editor_property(property_name, value)
                        return True
                    except Exception:
                        pass
                return False

            def get_first_editor_property(obj, property_names):
                if not obj:
                    return None
                for property_name in property_names:
                    try:
                        return obj.get_editor_property(property_name)
                    except Exception:
                        pass
                return None

            def get_map_value_by_string_key(source_map, key):
                if not source_map:
                    return None
                string_key = str(key)
                try:
                    value = source_map.get(string_key)
                    if value is not None:
                        return value
                except Exception:
                    pass
                try:
                    return source_map[string_key]
                except Exception:
                    pass
                try:
                    for existing_key, existing_value in source_map.items():
                        if str(existing_key) == string_key:
                            return existing_value
                except Exception:
                    pass
                return None

            def load_asset_or_none(asset_path):
                if not asset_path:
                    return None
                try:
                    return unreal.EditorAssetLibrary.load_asset(asset_path)
                except Exception as exc:
                    unreal.log_warning("GalExcleTools failed to load asset reference {}: {}".format(asset_path, exc))
                    return None

            def load_asset_array(asset_paths):
                result = []
                for asset_path in asset_paths or []:
                    result.append(load_asset_or_none(asset_path))
                return result

            def split_asset_object_path(asset_object_path):
                package_path = asset_object_path.split(".")[0]
                asset_name = package_path.rsplit("/", 1)[-1]
                destination_path = package_path.rsplit("/", 1)[0]
                return destination_path, asset_name

            def load_row_struct(row_struct_path):
                if not row_struct_path:
                    return None
                try:
                    row_struct = unreal.load_object(None, row_struct_path)
                    if row_struct:
                        unreal.log("GalExcleTools story row struct ready: {}".format(row_struct_path))
                    else:
                        unreal.log_warning("GalExcleTools story row struct missing: {}".format(row_struct_path))
                    return row_struct
                except Exception as exc:
                    unreal.log_warning("GalExcleTools failed to load story row struct {}: {}".format(row_struct_path, exc))
                    return None

            def create_data_table_asset(table_asset_path, row_struct):
                if not row_struct:
                    return None
                destination_path, asset_name = split_asset_object_path(table_asset_path)
                unreal.EditorAssetLibrary.make_directory(destination_path)
                try:
                    factory = unreal.DataTableFactory()
                    if not set_first_editor_property(factory, ["struct", "row_struct", "Struct", "RowStruct"], row_struct):
                        unreal.log_warning("GalExcleTools could not assign row struct before creating data table: {}".format(table_asset_path))
                    data_table = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
                        asset_name,
                        destination_path,
                        unreal.DataTable,
                        factory)
                    if data_table:
                        unreal.log("GalExcleTools created story data table: {}".format(table_asset_path))
                    return data_table
                except Exception as exc:
                    unreal.log_warning("GalExcleTools failed to create story data table {}: {}".format(table_asset_path, exc))
                    return None

            def ensure_story_data_table(table_asset_path, row_struct_path):
                data_table = unreal.EditorAssetLibrary.load_asset(table_asset_path)
                if data_table:
                    return data_table, load_row_struct(row_struct_path)
                row_struct = load_row_struct(row_struct_path)
                data_table = create_data_table_asset(table_asset_path, row_struct)
                return data_table, row_struct

            story_tables = manifest.get("StoryTables", [])
            asset_index_tables = manifest.get("AssetIndexTables", [])
            total_tables = len(story_tables) + len(asset_index_tables)
            completed_tables = 0
            legacy_assets_to_delete = []
            write_progress("UpdateTables", "正在更新剧情表和素材索引表", 82, 0, total_tables)
            for story_table in story_tables:
                table_asset_path = story_table.get("TableAsset")
                csv_path = story_table.get("CsvPath")
                row_struct_path = story_table.get("RowStruct")
                if not table_asset_path or not csv_path:
                    add_error("GalExcleTools story table entry is incomplete: {}".format(story_table))
                    continue
                for legacy_asset_path in story_table.get("LegacyTableAssets", []) or []:
                    if legacy_asset_path and legacy_asset_path != table_asset_path and unreal.EditorAssetLibrary.does_asset_exist(legacy_asset_path):
                        legacy_assets_to_delete.append(legacy_asset_path)
                data_table, row_struct = ensure_story_data_table(table_asset_path, row_struct_path)
                if data_table:
                    try:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path, row_struct)
                    except TypeError:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path)
                    if ok:
                        if save_loaded_asset_with_retry(data_table, table_asset_path):
                            unreal.log("GalExcleTools updated story data table: {}".format(table_asset_path))
                            story_table_count += 1
                    else:
                        add_error("GalExcleTools failed to update story data table: {}".format(table_asset_path))
                else:
                    add_error("GalExcleTools could not create or load story data table: {}".format(table_asset_path))
                completed_tables += 1
                write_progress("UpdateTables", "正在更新剧情表和素材索引表", 82 + (8.0 * completed_tables / max(total_tables, 1)), completed_tables, total_tables)

            for index_table in asset_index_tables:
                table_asset_path = index_table.get("TableAsset")
                csv_path = index_table.get("CsvPath")
                row_struct_path = index_table.get("RowStruct")
                if not table_asset_path or not csv_path:
                    add_error("GalExcleTools asset index table entry is incomplete: {}".format(index_table))
                    continue
                data_table, row_struct = ensure_story_data_table(table_asset_path, row_struct_path)
                if data_table:
                    try:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path, row_struct)
                    except TypeError:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path)
                    if ok:
                        if save_loaded_asset_with_retry(data_table, table_asset_path):
                            unreal.log("GalExcleTools updated asset index data table: {}".format(table_asset_path))
                            asset_index_table_count += 1
                    else:
                        add_error("GalExcleTools failed to update asset index data table: {}".format(table_asset_path))
                else:
                    add_error("GalExcleTools could not create or load asset index data table: {}".format(table_asset_path))
                completed_tables += 1
                write_progress("UpdateTables", "正在更新剧情表和素材索引表", 82 + (8.0 * completed_tables / max(total_tables, 1)), completed_tables, total_tables)

            def normalize_data_table_path(value):
                text = str(value or "")
                if "'" in text:
                    parts = text.split("'")
                    if len(parts) >= 2:
                        text = parts[1]
                return text.strip('"').split(".")[0]

            def data_table_reference(asset_path):
                return "/Script/Engine.DataTable'{}'".format(asset_path)

            def escape_unreal_string(value):
                return str(value or "").replace("\\", "\\\\").replace('"', '\\"')

            def build_story_branch_value(routes):
                entries = []
                for route in routes:
                    target_table = escape_unreal_string(route.get("TargetTable"))
                    option_key = escape_unreal_string(route.get("OptionKey"))
                    if route.get("RequiresChoiceCondition"):
                        entries.append(
                            '(TargetStory="{}",Judjes=((PropName="StoryJudge",JudjeType=Equal,TargetValue="{}")),ConditionType=All,CompleteCount=0)'.format(
                                data_table_reference(target_table),
                                option_key))
                    else:
                        entries.append(
                            '(TargetStory="{}",Judjes=(),ConditionType=All,CompleteCount=0)'.format(
                                data_table_reference(target_table)))
                return "(StoryOnSetMaps=({}))".format(",".join(entries)) if entries else "(StoryOnSetMaps=)"

            def find_manager_row(rows, manager_row):
                for row in rows:
                    if str(row.get("Name", "")).lower() == str(manager_row).lower():
                        return row
                row = dict(Name=manager_row, StoryOnSetTables=dict())
                rows.append(row)
                return row

            def collect_route_texts(value):
                # 把一个单元格的值里所有文本抠出来，**不管它被包成什么形状**。
                #
                # 为什么不能直接 str(value) 再数字符串：
                # export_data_table_to_json_string 给回来的是**结构化 JSON**，
                # 那个结构字段在 Python 里是个 dict（或是 dict 的 list），
                # str() 出来是 {'StoryOnSetMaps': [...]} 这种 repr ——
                # 用的是单引号加冒号，跟写进去的 Unreal 文本格式（TargetStory="..."）
                # **长得完全不一样**，于是数出来永远是 0。
                #
                # 2026-10-13 实测：AllRoad route count mismatch ... expected=3 actual=0
                # 就是这么来的 —— 不是没写进去，是**读的方式不对**。
                #
                # ⚠️ 这段刻意用 # 注释而不是 Python 的文档字符串：
                # 三引号独占一行时，会被 C# 原始字符串当成**结束标记**，
                # 下面整段就变成 C# 代码了 —— 编译必炸。
                texts = []
                if isinstance(value, str):
                    texts.append(value)
                elif isinstance(value, dict):
                    for key, item in value.items():
                        # ⚠️ **键也要收** —— 这一次就是漏在这儿。
                        #
                        # 复扫要数的 "TargetStory" 是 JSON 里的**键名**，不是值：
                        #     {"TargetStory": "...", "Judjes": [...], "CompleteType": ...}
                        # 只收值的话它永远不会出现在文本里，于是永远数出 0 ——
                        # 明明数据写进去了，却报"expected=3 actual=0"。
                        #
                        # 2026-10-13 实测的实际值长这样（键一个都没有）：
                        #     /Script/Engine.DataTable'.../WHK-M2-01.WHK-M2-01' StoryJudge Equal A All 0
                        # 值全在，就是没有 TargetStory —— 一眼就能看出是"收漏了键"。
                        texts.append(str(key))
                        texts.extend(collect_route_texts(item))
                elif isinstance(value, (list, tuple)):
                    for item in value:
                        texts.extend(collect_route_texts(item))
                elif value is not None:
                    texts.append(str(value))
                return texts

            def verify_story_branches(data_table, sources):
                try:
                    rows = json.loads(unreal.DataTableFunctionLibrary.export_data_table_to_json_string(data_table))
                except Exception as exc:
                    add_error("GalExcleTools could not export AllRoad for verification: {}".format(exc))
                    return False
                for source in sources:
                    manager_row = find_manager_row(rows, source.get("ManagerRow"))
                    story_map = manager_row.get("StoryOnSetTables") or {}
                    source_path = normalize_data_table_path(source.get("SourceTable"))
                    matched_value = None
                    for existing_key, existing_value in story_map.items():
                        if normalize_data_table_path(existing_key) == source_path:
                            matched_value = existing_value
                            break
                    if matched_value is None:
                        add_error("GalExcleTools AllRoad verification missed source table: {}".format(source_path))
                        return False
                    # 把值里所有文本摊平了再数。**两种形状都认**：
                    # 结构化 JSON（dict/list）和 Unreal 文本（字符串）——
                    # 前者是 export 给回来的真身，后者是脚本自己写进去的样子。
                    matched_text = " ".join(collect_route_texts(matched_value))
                    routes = source.get("Routes", [])
                    expected_route_count = len(routes)
                    # ⚠️ 数**裸词** TargetStory —— 一种写法覆盖全部三种形状：
                    #   JSON 的键名        → TargetStory
                    #   Unreal 的文本格式  → TargetStory="..."
                    #   摊平之后的拼接文本 → TargetStory（前后是空格）
                    # 之前分别数 "TargetStory=" 和 "\"TargetStory\"" 两种，结果**都匹配不上**：
                    # 摊平时是用空格拼的，引号和等号早在摊平那一步就没了。
                    # 2026-10-13 实测的实际值：
                    #     StoryOnSetMaps TargetStory /Script/.../WHK-M2-01.WHK-M2-01' Judjes PropName ...
                    # ——TargetStory 明明白白在那儿，只是不带等号也不带引号。
                    actual_route_count = matched_text.count("TargetStory")
                    if actual_route_count != expected_route_count:
                        add_error("GalExcleTools AllRoad route count mismatch for {}: expected={} actual={} 实际值：{}".format(
                            source_path, expected_route_count, actual_route_count, matched_text[:200]))
                        return False
                    for route in routes:
                        target_path = normalize_data_table_path(route.get("TargetTable"))
                        option_key = str(route.get("OptionKey") or "")
                        has_expected_condition = (not route.get("RequiresChoiceCondition") or
                                                  option_key in matched_text)
                        if target_path not in matched_text or not has_expected_condition:
                            add_error("GalExcleTools AllRoad verification missed route {} -> {} 实际值：{}".format(
                                option_key, target_path, matched_text[:200]))
                            return False
                return True

            story_branch_info = manifest.get("StoryBranches", {})
            if story_branch_info.get("ShouldUpdate"):
                write_progress("UpdateStoryBranches", "正在更新剧情选项分支", 90)
                all_road_path = story_branch_info.get("DataTable")
                sources = story_branch_info.get("Sources", [])
                all_road = unreal.EditorAssetLibrary.load_asset(all_road_path) if all_road_path else None
                if not all_road:
                    add_error("GalExcleTools could not load AllRoad data table: {}".format(all_road_path))
                else:
                    try:
                        rows = json.loads(unreal.DataTableFunctionLibrary.export_data_table_to_json_string(all_road))

                        # 先按 manager 行把**本次要写的来源**归好组。
                        expected_by_manager = {}
                        for source in sources:
                            manager_name = str(source.get("ManagerRow") or "")
                            expected_by_manager.setdefault(manager_name, []).append(source)

                        # 工具箱管的章节表在这底下。只有落在这里面的旧条目才算"我的"，
                        # 才允许被这次重建清掉 —— 见下面那段注释。
                        managed_story_root = normalize_data_table_path(
                            "{}/ExcelTexts/MainStory".format(target_root))

                        for manager_name, manager_sources in expected_by_manager.items():
                            manager_row = find_manager_row(rows, manager_name)
                            existing_map = manager_row.get("StoryOnSetTables") or {}

                            # ⚠️ **重建**，不是逐条替换。
                            #
                            # 旧写法是"把键与本次来源同路径的那条删掉，再写新的"——
                            # 而章节表**改名过**（M1-1 → WHK-M1）之后旧键根本对不上，
                            # 于是一条都删不掉，新章节只能追加在末尾。
                            # 用户看到的就是：M1 那一格里有 M1-1 / M1-2 / M1-3 / WHK-M1，
                            # **新的第一章排到了第四位**。2026-10-13 实测。
                            #
                            # 注意：那段旧写法在最初版里**就是这样**（00e9ed3），不是谁改坏的 ——
                            # 它的前提是章节表名字稳定；名字一改，它就只剩追加。
                            #
                            # 重建的两条边界：
                            # 1. 只清**落在工具箱的章节表目录里**的旧条目 ——
                            #    指向别处的条目不是工具箱放的，一律原样留着；
                            # 2. 新条目的顺序跟着本次的 sources 走，
                            #    不再有"追加到最后"这回事。
                            rebuilt_story_map = {
                                key: value
                                for key, value in existing_map.items()
                                if not normalize_data_table_path(key).startswith(managed_story_root)
                            }
                            for source in manager_sources:
                                rebuilt_story_map[data_table_reference(source.get("SourceTable"))] = \
                                    build_story_branch_value(source.get("Routes", []))
                            manager_row["StoryOnSetTables"] = rebuilt_story_map

                        updated_json = json.dumps(rows, ensure_ascii=False)
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_json_string(all_road, updated_json)
                        if not ok:
                            add_error("GalExcleTools failed to update AllRoad from merged branch JSON")
                        elif verify_story_branches(all_road, sources) and save_loaded_asset_with_retry(all_road, all_road_path):
                            reloaded_all_road = unreal.EditorAssetLibrary.load_asset(all_road_path)
                            story_branches_updated = bool(reloaded_all_road and verify_story_branches(reloaded_all_road, sources))
                            if story_branches_updated:
                                unreal.log("GalExcleTools updated and verified AllRoad story branches")
                    except Exception as exc:
                        add_error("GalExcleTools failed to update AllRoad story branches: {}".format(exc))

            def get_object_path(asset):
                if not asset:
                    return ""
                try:
                    return str(asset.get_path_name())
                except Exception:
                    return str(asset)

            def get_map_value_by_object_path(source_map, object_path):
                if not source_map:
                    return None
                normalized_path = str(object_path).split(".")[0]
                try:
                    for existing_key, existing_value in source_map.items():
                        if get_object_path(existing_key).split(".")[0] == normalized_path:
                            return existing_value
                except Exception:
                    pass
                return None

            def verify_voice_map(source_map, entries, managed_table_root):
                expected_paths = set(str(entry.get("TableAsset", "")).split(".")[0] for entry in entries)
                try:
                    managed_paths = set(
                        get_object_path(key).split(".")[0]
                        for key in source_map.keys()
                        if get_object_path(key).startswith(managed_table_root))
                except Exception:
                    return False
                if managed_paths != expected_paths:
                    return False
                for entry in entries:
                    table_asset_path = entry.get("TableAsset")
                    table_voices = get_map_value_by_object_path(source_map, table_asset_path)
                    actual_waves = get_first_editor_property(table_voices, ["TableWaves", "table_waves"])
                    expected_waves = entry.get("Waves", []) or []
                    if actual_waves is None or len(actual_waves) != len(expected_waves):
                        return False
                    for actual_wave, expected_wave in zip(actual_waves, expected_waves):
                        if not expected_wave:
                            if actual_wave:
                                return False
                        elif get_object_path(actual_wave).split(".")[0] != str(expected_wave).split(".")[0]:
                            return False
                return True

            voice_map_info = manifest.get("VoiceMap", {})
            should_update_voice_maps = voice_map_info.get("ShouldUpdate", False)
            write_progress("UpdateVoiceMaps", "正在更新剧情语音映射", 91)
            if should_update_voice_maps:
                blueprint_asset_path = voice_map_info.get("BlueprintAsset")
                map_property = voice_map_info.get("MapProperty", "VoiceMaps")
                managed_table_root = voice_map_info.get("ManagedTableRoot", target_root + "/ExcelTexts/")
                voice_map_entries = voice_map_info.get("Entries", []) or []
                blueprint_asset = unreal.EditorAssetLibrary.load_asset(blueprint_asset_path) if blueprint_asset_path else None
                generated_class = None
                if blueprint_asset_path:
                    try:
                        generated_class = unreal.EditorAssetLibrary.load_blueprint_class(blueprint_asset_path.split(".")[0])
                    except Exception:
                        generated_class = None
                if generated_class is None and blueprint_asset is not None:
                    try:
                        generated_class = blueprint_asset.generated_class()
                    except Exception:
                        generated_class = None
                class_default_object = unreal.get_default_object(generated_class) if generated_class else None
                if not blueprint_asset or not class_default_object:
                    add_error("GalExcleTools could not load MusicPlayer blueprint defaults: {}".format(blueprint_asset_path))
                else:
                    existing_voice_map = get_first_editor_property(class_default_object, [map_property, map_property.lower(), "VoiceMaps", "voice_maps"]) or {}
                    merged_voice_map = {}
                    try:
                        for existing_table, existing_value in existing_voice_map.items():
                            if not get_object_path(existing_table).startswith(managed_table_root):
                                merged_voice_map[existing_table] = existing_value
                    except Exception as exc:
                        add_error("GalExcleTools could not read existing MusicPlayer voice map: {}".format(exc))

                    voice_map_valid = not errors
                    for entry in voice_map_entries:
                        table_asset_path = entry.get("TableAsset")
                        data_table = unreal.EditorAssetLibrary.load_asset(table_asset_path) if table_asset_path else None
                        if not data_table:
                            add_error("GalExcleTools could not load voice DataTable: {}".format(table_asset_path))
                            voice_map_valid = False
                            continue
                        loaded_waves = []
                        for wave_path in entry.get("Waves", []) or []:
                            if not wave_path:
                                loaded_waves.append(None)
                                continue
                            wave_asset = unreal.EditorAssetLibrary.load_asset(wave_path)
                            if not wave_asset:
                                add_error("GalExcleTools could not load voice SoundWave: {}".format(wave_path))
                                voice_map_valid = False
                            loaded_waves.append(wave_asset)
                        table_voices = unreal.TableVoices()
                        if not set_first_editor_property(table_voices, ["TableWaves", "table_waves"], loaded_waves):
                            add_error("GalExcleTools could not set TableVoices.TableWaves for {}".format(table_asset_path))
                            voice_map_valid = False
                        merged_voice_map[data_table] = table_voices

                    if voice_map_valid:
                        try:
                            class_default_object.modify()
                        except Exception:
                            pass
                        if set_first_editor_property(class_default_object, [map_property, map_property.lower(), "VoiceMaps", "voice_maps"], merged_voice_map):
                            if save_loaded_asset_with_retry(blueprint_asset, blueprint_asset_path):
                                saved_voice_map = get_first_editor_property(class_default_object, [map_property, map_property.lower(), "VoiceMaps", "voice_maps"]) or {}
                                if verify_voice_map(saved_voice_map, voice_map_entries, managed_table_root):
                                    voice_maps_updated = True
                                    unreal.log("GalExcleTools updated MusicPlayer voice maps: tables={} waves={}".format(
                                        len(voice_map_entries),
                                        sum(int(entry.get("AssignedCount", 0)) for entry in voice_map_entries)))
                                else:
                                    add_error("GalExcleTools MusicPlayer voice-map verification failed after save: {}".format(blueprint_asset_path))
                        else:
                            add_error("GalExcleTools could not set MusicPlayer voice-map property '{}' on {}".format(map_property, blueprint_asset_path))

            # ── 变量列表写入 ────────────────────────────────────────────
            # 背景图 / BGM / 环境音改成**直接写变量列表**，不再走 ExcelTexts 的索引表。
            # 两条路线由 C# 侧决定（UnrealVariableWriteRoute）：
            #   CdoProperty      —— C++ 声明的 UPROPERTY，写蓝图子类的 CDO；Python 直接能做，
            #                       手法和上面 VoiceMaps 那段一致（那条已经跑通）。
            #   BlueprintDefault —— 蓝图自建的变量，必须写 FBPVariableDescription::DefaultValue。
            #                       脚本层做不可靠（蓝图下次编译会把 CDO 改动盖回去），
            #                       所以调 GALLibrary 里那个 C++ 写入器。
            write_progress("UpdateVariables", "正在写入素材变量列表", 94)
            variable_requests = manifest.get("Variables", []) or []
            variables_written = 0
            variables_unchanged = 0
            for variable_request in variable_requests:
                variable_asset_path = variable_request.get("TargetAssetPath") or ""
                variable_name = variable_request.get("VariableName") or ""
                variable_route = variable_request.get("Route") or ""
                if not variable_asset_path or not variable_name:
                    continue

                if variable_route == "BlueprintDefault":
                    variable_writer = getattr(unreal, "GALLBlueprintVariableWriter", None)
                    if variable_writer is None:
                        add_error("GalExcleTools 找不到 GALLBlueprintVariableWriter，无法写蓝图变量 {}（确认 GALLibrary 插件已编译）".format(variable_name))
                        continue
                    try:
                        writer_result = variable_writer.ensure_blueprint_variable_default(
                            variable_asset_path,
                            variable_name,
                            variable_request.get("ExportText") or "")
                    except Exception as exc:
                        add_error("GalExcleTools 写蓝图变量 {} 失败：{}".format(variable_name, exc))
                        continue
                    # C++ 那边返回 JSON 字符串；这里只做粗判，细节整条留在日志里给人看。
                    compact_result = str(writer_result).replace(" ", "")
                    if '"ok":true' in compact_result:
                        if '"changed":true' in compact_result:
                            variables_written += 1
                        else:
                            variables_unchanged += 1
                        unreal.log("GalExcleTools 变量 {}：{}".format(variable_name, writer_result))
                    else:
                        add_error("GalExcleTools 写蓝图变量 {} 返回失败：{}".format(variable_name, writer_result))
                    continue

                variable_asset = unreal.EditorAssetLibrary.load_asset(variable_asset_path)
                variable_class = None
                try:
                    variable_class = unreal.EditorAssetLibrary.load_blueprint_class(variable_asset_path.split(".")[0])
                except Exception:
                    variable_class = None
                if variable_class is None and variable_asset is not None:
                    try:
                        variable_class = variable_asset.generated_class()
                    except Exception:
                        variable_class = None
                variable_cdo = unreal.get_default_object(variable_class) if variable_class else None
                if not variable_asset or not variable_cdo:
                    add_error("GalExcleTools 变量写入：载入蓝图失败 {}".format(variable_asset_path))
                    continue

                variable_kind = variable_request.get("Kind") or ""
                if variable_kind == "AssetList":
                    loaded_assets = []
                    list_valid = True
                    for asset_object_path in variable_request.get("AssetObjectPaths", []) or []:
                        loaded_asset = unreal.EditorAssetLibrary.load_asset(asset_object_path)
                        if not loaded_asset:
                            add_error("GalExcleTools 变量写入：找不到资产 {}".format(asset_object_path))
                            list_valid = False
                        loaded_assets.append(loaded_asset)
                    if not list_valid:
                        continue
                    new_variable_value = loaded_assets
                elif variable_kind == "FloatList":
                    new_variable_value = [float(number) for number in (variable_request.get("Numbers", []) or [])]
                else:
                    add_error("GalExcleTools 变量写入：不认识的载荷类型 {}".format(variable_kind))
                    continue

                try:
                    variable_cdo.modify()
                except Exception:
                    pass
                if not set_first_editor_property(variable_cdo, [variable_name, variable_name.lower()], new_variable_value):
                    add_error("GalExcleTools 变量写入：设置 {} 失败（{}）".format(variable_name, variable_asset_path))
                    continue
                if save_loaded_asset_with_retry(variable_asset, variable_asset_path):
                    variables_written += 1
                    unreal.log("GalExcleTools 变量 {} 已写入 {}".format(variable_name, variable_asset_path))
                else:
                    add_error("GalExcleTools 变量写入：保存失败 {}".format(variable_asset_path))
            unreal.log("GalExcleTools 变量写入完成：写入 {} 项，未变 {} 项".format(variables_written, variables_unchanged))

            def build_lustration_struct(row, existing_item=None):
                # Keep Unreal-side Vfx data intact. The tool owns imported texture
                # layers, but VFX materials are configured manually in the data asset.
                item = existing_item if existing_item is not None else unreal.LustrationStruct()
                color = row.get("Color") or {}
                linear_color = unreal.LinearColor(
                    float(color.get("R", 0.0)),
                    float(color.get("G", 0.0)),
                    float(color.get("B", 0.0)),
                    float(color.get("A", 1.0)))
                set_first_editor_property(item, ["Name", "name"], row.get("Name", ""))
                set_first_editor_property(item, ["Color", "color"], linear_color)
                set_first_editor_property(item, ["Cloth", "cloth"], load_asset_array(row.get("Cloth", [])))
                set_first_editor_property(item, ["Face", "face"], load_asset_array(row.get("Face", [])))
                set_first_editor_property(item, ["Adorn", "adorn"], load_asset_array(row.get("Adorn", [])))
                return item

            lustration_info = manifest.get("LustrationInfo", {})
            write_progress("UpdateLustration", "正在更新立绘数据", 92)
            data_asset_path = lustration_info.get("DataAsset")
            map_property = lustration_info.get("MapProperty", "Infor")
            lustration_rows = lustration_info.get("Rows", [])
            should_update_lustration = lustration_info.get("ShouldUpdate", bool(lustration_rows))
            if data_asset_path and should_update_lustration:
                data_asset = unreal.EditorAssetLibrary.load_asset(data_asset_path)
                if data_asset:
                    existing_lustration_map = get_first_editor_property(data_asset, [map_property, map_property.lower(), "Infor", "infor"]) or {}
                    lustration_map = {}
                    for row in lustration_rows:
                        key = row.get("Key")
                        if not key:
                            continue
                        existing_item = get_map_value_by_string_key(existing_lustration_map, key)
                        lustration_map[str(key)] = build_lustration_struct(row, existing_item)
                    try:
                        data_asset.modify()
                    except Exception:
                        pass
                    if set_first_editor_property(data_asset, [map_property, map_property.lower(), "Infor", "infor"], lustration_map):
                        if save_loaded_asset_with_retry(data_asset, data_asset_path):
                            unreal.log("GalExcleTools updated lustration data asset: {} rows={}".format(data_asset_path, len(lustration_map)))
                            lustration_updated = True
                    else:
                        add_error("GalExcleTools could not set lustration map property '{}' on {}".format(map_property, data_asset_path))
                else:
                    add_error("GalExcleTools could not load lustration data asset: {}".format(data_asset_path))

            portraits_info = manifest.get("PortraitsInfo", {})
            write_progress("UpdatePortraits", "正在更新小预览数据", 94)
            portraits_data_asset_path = portraits_info.get("DataAsset")
            portraits_map_property = portraits_info.get("MapProperty", "Infor")
            portrait_rows = portraits_info.get("Rows", [])
            should_update_portraits = portraits_info.get("ShouldUpdate", bool(portrait_rows))
            if portraits_data_asset_path and should_update_portraits:
                portraits_data_asset = unreal.EditorAssetLibrary.load_asset(portraits_data_asset_path)
                if portraits_data_asset:
                    portraits_map = {}
                    for row in portrait_rows:
                        key = row.get("Key")
                        if not key:
                            continue
                        portraits_map[str(key)] = build_lustration_struct(row, None)
                    try:
                        portraits_data_asset.modify()
                    except Exception:
                        pass
                    if set_first_editor_property(portraits_data_asset, [portraits_map_property, portraits_map_property.lower(), "Infor", "infor"], portraits_map):
                        if save_loaded_asset_with_retry(portraits_data_asset, portraits_data_asset_path):
                            unreal.log("GalExcleTools updated portrait data asset: {} rows={}".format(portraits_data_asset_path, len(portraits_map)))
                            portraits_updated = True
                    else:
                        add_error("GalExcleTools could not set portrait map property '{}' on {}".format(portraits_map_property, portraits_data_asset_path))
                else:
                    add_error("GalExcleTools could not load portrait data asset: {}".format(portraits_data_asset_path))

            # ── 清理多余素材（**只在前面没出错时才做**）──────────────────
            #
            # 这些素材之所以"用不上了"，是因为前面几步把引用（变量列表、数据表）
            # 改指向了新的东西 —— **前提是改成功了**。
            # 要是写变量 / 写表失败了，那些引用**还指着这些素材**，
            # 这时候删掉就是"引用全丢"。
            #
            # 2026-10-13 实测：写蓝图变量 BGMaps 返回失败，而删除照跑，
            # 于是 19 个文件被清掉、蓝图引用全断（靠同步前的提交才恢复回来）。
            #
            # 顺带说明：删除**范围**本身是对的 —— 素材文件夹里不能有别的，
            # 旧素材的引用一旦改掉就是垃圾，留着才乱。要防的是"没改成就删"，
            # 不是"删得太多"。
            pending_deletes = []
            for group in deletes:
                pending_deletes.extend(group.get("Assets", []) or [])
            pending_deletes.extend(legacy_assets_to_delete)

            if pending_deletes and errors:
                # 有失败项就不删：宁可留一堆旧素材（下次同步会再清一遍），
                # 也不要删掉还被引用着的东西 —— 后者不可逆。
                add_warning(
                    "GalExcleTools 跳过清理多余素材：本次有失败项（{} 条），"
                    "现在删可能删掉还被引用着的资产；待清理项共 {} 个。".format(
                        len(errors), len(pending_deletes)))
                pending_deletes = []

            # ⚠️ **把"这次刚写进去的"从待删名单里摘掉** —— 这一步是零境那边用血的教训换来的。
            #
            # 它的原话（sync_character_sequences.py 的 _cleanup_old_assets）：
            #   「比价必须忽略大小写。……逐字节比会把自己刚建好的资产判成"该清掉的旧资产"：
            #     实测 2026-09-22，特效层新建的图集贴图 / 3 只精灵 / Flipbook 被整批 force delete」
            # 它的做法就是先把这次的新路径归一化成小写集合，再从待删里剔掉。
            #
            # 这边 2026-10-13 踩的是同一个坑：Dn_* 那批立绘层贴图**这次刚导入**（18:31:27），
            # 紧接着删除阶段就判它们是"多余" —— 名字比对差一个大小写或归一化就够了。
            # 后果不只是白删：删完引用就断了，而复扫还会说"删除后文件仍在"
            # （包被加载着，ForceDeleteObjects 卸载不掉，文件就留在盘上）。
            #
            # 判据用**这次真正导进去的包路径**（task_targets 里那一串），归一化成小写再比。
            just_written = set()
            for entry in task_targets:
                package = str(entry[1] or "").split(".")[0].lower()
                if package:
                    just_written.add(package)
            if just_written and pending_deletes:
                kept = []
                skipped_just_written = 0
                for asset_path in pending_deletes:
                    if str(asset_path).split(".")[0].lower() in just_written:
                        skipped_just_written += 1
                        continue
                    kept.append(asset_path)
                if skipped_just_written:
                    add_warning(
                        "GalExcleTools 待清理里有 {} 个是这次刚导入的，已从删除名单里摘掉"
                        "（不然会自己删掉自己刚写的东西）。".format(skipped_just_written))
                pending_deletes = kept

            write_progress("DeleteAssets", "正在清理虚幻中的多余素材", 96, 0, len(pending_deletes))
            still_present = []
            for delete_index, asset_path in enumerate(pending_deletes):
                if delete_asset_if_exists(asset_path):
                    # 只有**复核确认盘上没了**才算真删掉 —— 别把"动作返回成功"当结果。
                    # 2026-10-13：45 个动作全返回成功，实际只删掉 7 个。
                    if asset_path not in still_present:
                        deleted_count += 1
                write_progress("DeleteAssets", "正在清理虚幻中的多余素材", 96 + (3.0 * (delete_index + 1) / max(len(pending_deletes), 1)), delete_index + 1, len(pending_deletes))

            # ── 删除之后**必须再存一次** ─────────────────────────────────
            # 批量保存跑在删除**之前**，所以删除只改到了内存：
            # 对象没了、盘上的 .uasset 一个都没少，而 does_asset_exist 查的正是文件。
            # 2026-10-13 实测就是这么来的：45 个删除动作全部"返回成功"，复扫 38 个还在。
            if pending_deletes:
                delete_save_root = manifest.get("TargetRoot") or "/Game"
                try:
                    unreal.EditorAssetLibrary.save_directory(delete_save_root, only_if_is_dirty=True, recursive=True)
                except Exception as exc:
                    add_warning("GalExcleTools 删除后统一保存出错：{} —— {}".format(delete_save_root, exc))
                if still_present:
                    # 当场复核就已经发现还在 —— 那说明删除动作本身没生效，
                    # 不是"没落盘"。这条要单独报，否则会被当成同一类问题反复查。
                    add_warning("GalExcleTools 有 {} 个资产删除动作返回成功、但当场复核仍在（共 {} 个待删）：{}".format(
                        len(still_present), len(pending_deletes), "；".join(still_present[:5])))

            # ── 资产类型清单 ────────────────────────────────────────────
            # 把这次涉及到的资产**当时的类名**写下来，给"同步前检测"用。
            #
            # 为什么非要问 Unreal：光看文件认不出蓝图化的数据资产
            # （它们报的类名是 Blueprint，不是 DataAsset），更认不出 PaperZD 那种细类名。
            # 检测那张表的价值全在"标红的就真要去处理"，所以类型必须是**问来的**。
            # 这一段不额外花时间 —— 脚本本来就在 Unreal 里跑着。
            write_progress("WriteAssetTypes", "正在记录资产类型", 98)
            type_paths = []
            for variable_request in manifest.get("Variables", []) or []:
                type_paths.append(variable_request.get("TargetAssetPath") or "")
            for section_key in ("LustrationInfo", "PortraitsInfo"):
                type_paths.append((manifest.get(section_key) or {}).get("DataAsset") or "")
            type_paths.append((manifest.get("VoiceMap") or {}).get("BlueprintAsset") or "")
            type_paths.append((manifest.get("StoryBranches") or {}).get("DataTable") or "")
            for entry in manifest.get("AssetIndexTables", []) or []:
                type_paths.append(entry.get("TableAsset") or "")
            for entry in manifest.get("StoryTables", []) or []:
                type_paths.append(entry.get("TableAsset") or "")

            def describe_asset_class(object_path):
                # 返回类名（"Blueprint" / "WidgetBlueprint" / "DataTable"…）；
                # 读不到就返回空串 —— 空串在 C# 那边一律当"不判"，绝不判失败。
                try:
                    asset_data = unreal.EditorAssetLibrary.find_asset_data(object_path)
                    if not asset_data.is_valid:
                        return ""
                    class_path = asset_data.get_editor_property("asset_class_path")
                    # TopLevelAssetPath 在 5.1+ 有 asset_name；老版本只能从字符串里抠。
                    try:
                        value = class_path.get_editor_property("asset_name")
                    except Exception:
                        text = str(class_path)
                        marker = text.rfind('"')
                        start = text.rfind('"', 0, marker)
                        value = text[start + 1:marker] if start >= 0 and marker > start else text
                    # ⚠️ 必须把字面量 "None" 干掉。Python 里 get_editor_property 拿不到
                    # 某个字段时返回的是 None，而 str(None) 是 "None" —— 那个字符串
                    # 到了 C# 那边会被当成一个真实的类名，于是判成"类型不对"（假红）。
                    if value is None or str(value) in ("", "None"):
                        return ""
                    return str(value)
                except Exception as exc:
                    unreal.log_warning("GalExcleTools 读资产类型失败 {}：{}".format(object_path, exc))
                    return ""

            type_entries = []
            seen_type_paths = set()
            for object_path in type_paths:
                if not object_path or object_path in seen_type_paths:
                    continue
                seen_type_paths.add(object_path)
                type_entries.append({
                    "objectPath": object_path,
                    "assetClass": describe_asset_class(object_path)
                })

            if asset_types_path:
                try:
                    atomic_write_json(asset_types_path, type_entries)
                except Exception as exc:
                    # 写不了清单不影响同步本身 —— 检测那边会退回"不判类型"。
                    add_warning("GalExcleTools 写资产类型清单失败：{}".format(exc))

            # ── 执行后复扫验证 ──────────────────────────────────────────
            # 光靠计数判断不了"同步成功"：计数能对上、内容却是错的完全可能
            # （导入了 27 个文件，但不是那 27 个）。
            # 所以执行完**再扫一遍实际状态**，逐项核对计划里的每一条 ——
            # 该在的在不在、该没的是不是真没了。
            # 这一层是零境那边最值得学的：它专门有个 UnrealBridgeVerificationService 干这个，
            # 而且核对不过就**拒绝写入成功状态**（下面 errors 非空 → Success=false）。
            write_progress("Verify", "正在复扫验证同步结果", 98)
            for display_name, package_path in task_targets:
                present = bool(unreal.EditorAssetLibrary.does_asset_exist(package_path))
                verification.append({
                    "DisplayName": display_name,
                    "ObjectPath": package_path,
                    "ExpectPresent": True,
                    "Present": present,
                    "Ok": present
                })
                if not present:
                    add_error("GalExcleTools 复扫验证未通过：{} 不在 {}".format(display_name, package_path))

            for asset_path in pending_deletes:
                # **以磁盘为准**：does_asset_exist 查的是资产注册表，而删除这件事上
                # 注册表、动作返回值、磁盘三方口径并不一致（见 asset_path_to_disk_file
                # 那段注释）。文件没了才算删掉 —— 而删文件这件事上面那一步已经做过了。
                still_there = asset_file_exists(asset_path)
                verification.append({
                    "DisplayName": "清理 {}".format(asset_path.rsplit("/", 1)[-1]),
                    "ObjectPath": asset_path,
                    "ExpectPresent": False,
                    "Present": still_there,
                    "Ok": not still_there
                })
                if still_there:
                    add_error("GalExcleTools 复扫验证未通过：{} 删除后文件仍在磁盘上".format(asset_path))

            # 改名：旧路径必须没了、新路径必须在了 —— 两条都要查，
            # 只查一条的话"改到一半"（旧没了新也没有）会被当成功。
            for rename_display, rename_source, rename_target in renamed_packages:
                source_still_there = bool(unreal.EditorAssetLibrary.does_asset_exist(rename_source))
                target_present = bool(unreal.EditorAssetLibrary.does_asset_exist(rename_target))
                verification.append({
                    "DisplayName": "改名 {}".format(rename_display),
                    "ObjectPath": rename_target,
                    "ExpectPresent": True,
                    "Present": target_present and not source_still_there,
                    "Ok": target_present and not source_still_there
                })
                if not (target_present and not source_still_there):
                    add_error("GalExcleTools 复扫验证未通过：改名 {} 之后旧路径 {} 或新路径 {} 不对".format(
                        rename_display, rename_source, rename_target))

            failed_verifications = [item for item in verification if not item["Ok"]]
            unreal.log("GalExcleTools verification: {} item(s), {} failed".format(
                len(verification), len(failed_verifications)))

            write_result(True)
            write_progress("Completed", "Unreal 同步已完成" if not errors else "Unreal 同步完成，但存在失败项", 100)
            unreal.log("GalExcleTools sync finished. Imported task count: {}, deleted extra asset count: {}".format(len(tasks), deleted_count))
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// 跑一次同步。
    ///
    /// 【为什么改成 async】进程编排交给了 <see cref="UnrealSyncProcessRunner"/> ——
    /// 那套修掉了老实现里的三个坑：取消时进程杀不掉（取消落在 Task.Delay 里直接掀掉循环，
    /// 循环顶部那句 Kill 永远轮不到，编辑器成孤儿占着工程锁）、子进程输出没固定 UTF-8
    /// （中文日志必然乱码，偏偏只在出错时被人看）、拿退出码判成败（commandlet 只要工程里
    /// 别处有编译错误就返回非 0）。
    ///
    /// 【在线 / 离线】本项目选的是"**只给同步这条链路走在线**"（2026-10-12 定的选项 C）：
    /// 编辑器开着时走 Python 远程执行、快约十倍；连不上才退回离线冷启。
    /// 选路与回退都在 <see cref="UnrealPythonTaskExecutionService"/> 里，
    /// 这里只把"没匹配到在线"和"在线失败退回"两种情况转成给别人看的话（两个回调）。
    /// </summary>
    /// <summary>
    /// 进程输出只留末尾这一段。
    ///
    /// 留**末尾**而不是开头：报错时要的是"最后发生了什么"（脚本抛的异常、编辑器崩在哪），
    /// 开头那几十行都是插件加载、日志初始化之类，对定位没多大用。
    /// </summary>
    private static string TrimProcessOutput(string? output, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "（进程没有任何输出 —— 这一条本身就是线索：它可能根本没启动起来）";
        }

        return output.Length <= maxLength
            ? output
            : $"…（前文已省略 {output.Length - maxLength} 个字符）…{Environment.NewLine}{output[^maxLength..]}";
    }

    public async Task<UnrealSyncResult> RunAsync(
        UnrealSyncContext context,
        UnrealSyncChangePlan changePlan,
        IReadOnlyList<CharacterFilterEntry> filters,
        IProgress<UnrealSyncProgressUpdate>? progress = null,
        Action<string>? onUsedOffline = null,
        Action<string>? onOfflineFallback = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var savedFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "GalExcleTools");
        Directory.CreateDirectory(savedFolder);
        var manifestPath = Path.Combine(savedFolder, "gal-sync-manifest.json");
        var scriptPath = Path.Combine(savedFolder, "gal_sync_import.py");
        var progressPath = Path.Combine(savedFolder, "gal-sync-progress.json");
        var resultPath = Path.Combine(savedFolder, "gal-sync-result.json");

        // 上一轮的进度/结果文件先清掉。删不掉不是致命错误 —— 判成败看的是
        // 「结果文件是不是这一轮写的」（下面 verdict 里的 IsFreshOutput），那才是兜底。
        UnrealSyncProcessRunner.TryClearStaleFile(progressPath);
        UnrealSyncProcessRunner.TryClearStaleFile(resultPath);

        progress?.Report(new UnrealSyncProgressUpdate("正在写入同步清单...", 40));
        WriteManifest(context, changePlan, filters, manifestPath, progressPath, resultPath);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new UnrealSyncProgressUpdate("正在写入 Unreal Python 脚本...", 48));
        WritePythonScript(scriptPath, manifestPath);
        cancellationToken.ThrowIfCancellationRequested();

        // 离线的启动参数：冷启一个无头编辑器跑同一份脚本（这条路一直有，
        // 也是唯一一条"编辑器没开"时能走的路）。
        var offlineStartInfo = new ProcessStartInfo
        {
            FileName = context.EditorPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        offlineStartInfo.ArgumentList.Add(context.UnrealProjectPath);
        // ⚠️ 用 `-run=pythonscript -script=<路径>`，**不是** `-ExecutePythonScript=<路径>`。
        //
        // 后者是"编辑器启动选项"，在 UnrealEditor-Cmd 这条无头路径下不一定被处理 ——
        // 表现就是"进程跑完了、脚本根本没执行、所以没写结果文件"，
        // 然后被"结果文件新鲜度"那条判成败的规则判成失败。现象绕得很，不好往回推。
        //
        // 2026-10-13 实测撞上，去零境那边对了一眼才发现命令形式不同
        // （它一直是 `-run=pythonscript -script=...`，还多一个 `-nosound`）。
        offlineStartInfo.ArgumentList.Add("-run=pythonscript");
        offlineStartInfo.ArgumentList.Add($"-script={scriptPath}");
        offlineStartInfo.ArgumentList.Add("-unattended");
        offlineStartInfo.ArgumentList.Add("-nosplash");
        offlineStartInfo.ArgumentList.Add("-nosound");
        offlineStartInfo.ArgumentList.Add("-nop4");

        var launch = _taskExecutionService.BuildLaunch(
            context.EditorPath,
            context.UnrealProjectPath,
            scriptPath,
            Path.Combine(savedFolder, "gal-sync-remote-job.json"),
            offlineStartInfo);

        progress?.Report(new UnrealSyncProgressUpdate(
            launch.UsesRunningEditor
                ? "正在通过运行中的编辑器执行（在线）..."
                : "正在启动 Unreal Editor 命令进程（离线，较慢）...",
            55));

        var processResult = await UnrealPythonTaskExecutionService.RunWithOfflineFallbackAsync(
            launch,
            offlineStartInfo,
            startInfo => UnrealSyncProcessRunner.RunAsync(
                startInfo,
                progressPath,
                progress,
                _jsonOptions,
                UnrealSyncTimeout,
                "无法启动 Unreal Editor。",
                "Unreal Editor 同步超过 30 分钟，已终止进程。",
                // 判成败看**结果文件是不是这一轮写的**，不看退出码：
                // 退出码非 0 可能只是工程里别处有编译错误，与本轮成不成无关；
                // 而脚本走到最后一定会 write_result（见脚本末尾），
                // 所以"结果不新鲜"就等于"它没跑完"。
                //
                // ⚠️ 失败时**必须把进程输出一起带上**。
                // 2026-10-13 实测：这里只报了一句"没有写出结果"，而唯一能说明
                // "编辑器为什么没跑起来"的就是那段 stdout/stderr —— 结果它被扔了，
                // 于是只能靠猜。verdict 拿得到 result，就没有理由不带。
                verdict: result => UnrealSyncProcessRunner.IsFreshOutput(resultPath, result.StartedAtUtc)
                    ? null
                    : $"Unreal 没有写出这一轮的同步结果（结果文件缺失，或仍是上一轮留下的）。"
                      + $"\r\n退出码：{result.ExitCode}；用时：{(result.StartedAtUtc == default ? "未知" : (DateTime.UtcNow - result.StartedAtUtc).ToString(@"mm\:ss"))}"
                      + $"\r\n结果文件：{resultPath}"
                      + $"\r\n—— Unreal 进程输出 ——\r\n{TrimProcessOutput(result.Output, 3000)}",
                cancellationToken: cancellationToken),
            onNoOnlineEditor: onUsedOffline,
            onOfflineFallback: onOfflineFallback).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new UnrealSyncProgressUpdate("正在收集 Unreal 同步结果...", 95));
        var output = processResult.Output + ReadLatestLogSnippet(context);
        var report = ReadExecutionReport(resultPath);
        return new UnrealSyncResult(processResult.ExitCode, manifestPath, scriptPath, progressPath, resultPath, output, report);
    }

    public UnrealSyncChangePlan BuildChangePlan(
        UnrealSyncContext context,
        bool forceFullSync,
        IReadOnlyList<CharacterInfo> characters,
        IReadOnlyList<ImageSetSyncInput> imageSets,
        IReadOnlyCollection<string> musicPaths,
        IReadOnlyCollection<string> ambientPaths,
        IReadOnlyCollection<string> soundEffectPaths,
        IReadOnlyCollection<string> voicePaths,
        ProjectVoiceMapState voiceMapState,
        IReadOnlyList<UnrealStoryTableSyncEntry> allStoryTables,
        string assetIndexTableCacheFolder,
        Func<CharacterInfo, IReadOnlyDictionary<string, string>> getPortraitPreviewPathsByLayerFileName)
    {
        var syncState = ReadState(context);
        var requiresVerifiedTableSync = syncState.StateVersion < CurrentUnrealSyncStateVersion;

        // 背景图在这一版里还有两处**只有它才需要**的用处：素材索引表、写 UI_Story 的变量。
        // 用前缀把它挑出来 —— 以后加图集分类时，这两处一个字都不用动
        // （前提是不要写"第一个就是背景图"那种假设，所以我没按下标取）。
        var backgroundPaths = imageSets
            .FirstOrDefault(set => set.Prefix == ImageSetPrefix.Background)
            ?.FilePaths ?? [];

        var allImportGroups = BuildImportGroups(
                context,
                characters,
                imageSets,
                musicPaths,
                ambientPaths,
                soundEffectPaths,
                voicePaths)
            .Concat(context.AssetLibrary.IsPortraitPreviewEnabled
                ? BuildPortraitPreviewImportGroups(context, characters, getPortraitPreviewPathsByLayerFileName)
                : [])
            .ToList();
        var sourceHashes = ComputeSourceHashes(allImportGroups, allStoryTables);
        var importGroups = allImportGroups
            .Select(group => new UnrealSyncImportGroup(
                group.Destination,
                forceFullSync
                    ? group.Files.ToList()
                    : group.Files
                        .Where(path => SourceFileNeedsImport(context, group.Destination, path, syncState, sourceHashes))
                        .ToList()))
            .Where(group => group.Files.Count > 0)
            .ToList();

        var deleteGroups = BuildDeleteGroups(
            context,
            characters,
            imageSets,
            musicPaths,
            ambientPaths,
            soundEffectPaths,
            voicePaths,
            allStoryTables,
            getPortraitPreviewPathsByLayerFileName,
            syncState);

        // 删除范围的规矩（2026-10-13 定下来的，两句话）：
        //
        //   1. **工具箱独占的子目录** → 清空（"素材文件夹里不能有别的"）。
        //      背景图 ImageS/Background_Image/PlotUse、BGM Sound/BGM/PlotUse、
        //      语音 Sound/Voice/<章节>/、ExcelTexts —— 这些目录里只有工具箱的东西。
        //
        //   2. **和工程共用的目录** → 只删**工具箱自己写过的那些**（名单来自同步状态）。
        //      Sound/Scene_Effect 就是这一类：里面同时住着工程自己的
        //      MetaScene / Scene_Class，整目录清一次就把它俩清掉了。
        //
        // 落点搬进 Sound/ 之后，"路径在工具箱的落点里"不再等于"这个目录归工具箱" ——
        // 这是这次弄丢六个文件的根子。判断依据要落到"目录里除了工具箱的还有没有别人的"。
        //
        // 补一条相关的：**引用还没改好就删** 是另一个坑（写 BGMaps 失败、删除照跑，
        // 于是引用全断）。那道闸门加在脚本里 —— 删之前先看有没有失败项，不在这一层。

        // ── 素材改名：能改名就不要"删旧 + 导新" ──────────────────────
        //
        // 源文件改了名，老做法是"旧资产进删除组被删掉 + 新资产再导入一遍"——
        // 那会把 Unreal 里**所有引用旧资产的地方全部打断**。
        // 改名的正确做法是保留引用（零境那边专门有一个 OP_RENAME 干这个）。
        //
        // ⚠️ 顺序很关键：**先认出改名，再把它从删除组里摘掉**。
        // 不摘的话，计划表上会同时出现"改名"和"删除同一个旧路径"两条互相矛盾的行 ——
        // 虽然实际执行时旧路径已经不存在、删除是空操作，但看的人会以为工具在乱来。
        var renames = BuildUnrealSyncRenameEntries(context, allImportGroups, deleteGroups, sourceHashes, syncState);
        if (renames.Count > 0)
        {
            var renamedSourcePaths = renames
                .Select(entry => entry.SourceObjectPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            deleteGroups = deleteGroups
                .Select(group => group with
                {
                    Assets = group.Assets.Where(asset => !renamedSourcePaths.Contains(asset)).ToList()
                })
                .Where(group => group.Assets.Count > 0)
                .ToList();
        }

        var storyTables = forceFullSync || requiresVerifiedTableSync
            ? allStoryTables.ToList()
            : allStoryTables
                .Where(entry => SourceFileNeedsAssetUpdate(context, entry.CsvPath, entry.TableAsset, syncState, sourceHashes))
                .ToList();

        var allAssetIndexTables = BuildAssetIndexTableSyncEntries(
            context,
            assetIndexTableCacheFolder,
            backgroundPaths.ToList(),
            musicPaths.ToList(),
            ambientPaths.ToList(),
            soundEffectPaths.ToList());
        var assetIndexTablesHash = ComputeAssetIndexTablesHash(allAssetIndexTables);
        var assetIndexTablesChanged =
            forceFullSync ||
            requiresVerifiedTableSync ||
            !string.Equals(syncState.AssetIndexTablesHash, assetIndexTablesHash, StringComparison.OrdinalIgnoreCase) ||
            allAssetIndexTables.Any(entry => !File.Exists(AssetObjectPathToFilePath(context, entry.TableAsset)));
        var assetIndexTables = assetIndexTablesChanged ? allAssetIndexTables : [];

        var storyBranches = BuildStoryBranchSyncEntries(context, allStoryTables);
        var storyBranchesHash = ComputeSha256Hex(
            "story-branches-v2|" + JsonSerializer.Serialize(storyBranches, _jsonOptions));
        var storyBranchAssetFilePath = UnrealObjectPathToContentFilePath(context, StoryBranchDataTableAssetPath);
        var currentStoryBranchAssetHash = File.Exists(storyBranchAssetFilePath)
            ? ComputeFileHash(storyBranchAssetFilePath)
            : string.Empty;
        var storyBranchesChanged = storyBranches.Count > 0 &&
            (forceFullSync ||
             requiresVerifiedTableSync ||
             !string.Equals(syncState.StoryBranchesHash, storyBranchesHash, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(syncState.StoryBranchesAssetHash, currentStoryBranchAssetHash, StringComparison.OrdinalIgnoreCase) ||
             !File.Exists(storyBranchAssetFilePath));

        var voiceMaps = BuildVoiceMapSyncEntries(context, allStoryTables, voiceMapState);
        var voiceMapsHash = ComputeSha256Hex("music-player-voice-maps-v1|" + JsonSerializer.Serialize(voiceMaps, _jsonOptions));
        var voiceMapsChanged =
            allStoryTables.Count > 0 &&
            (forceFullSync ||
                requiresVerifiedTableSync ||
                !string.Equals(syncState.VoiceMapsHash, voiceMapsHash, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(UnrealObjectPathToContentFilePath(context, MusicPlayerBlueprintAssetPath)));

        var lustrationRows = BuildLustrationSyncEntries(context, characters);
        var lustrationHash = ComputeSha256Hex("lustration-dataasset-v3-preserve-vfx|" + JsonSerializer.Serialize(lustrationRows, _jsonOptions));
        var lustrationAssetPath = UnrealSyncPathPolicy.LustrationInfoAsset;
        var lustrationAssetFilePath = AssetObjectPathToFilePath(context, lustrationAssetPath);
        var lustrationChanged =
            forceFullSync ||
            !File.Exists(lustrationAssetFilePath) ||
            !string.Equals(syncState.LustrationHash, lustrationHash, StringComparison.OrdinalIgnoreCase);

        var portraitRows = context.AssetLibrary.IsPortraitPreviewEnabled
            ? BuildPortraitSyncEntries(context, characters, getPortraitPreviewPathsByLayerFileName)
            : [];
        var portraitsHash = context.AssetLibrary.IsPortraitPreviewEnabled
            ? ComputeSha256Hex("portrait-dataasset-v1|" + JsonSerializer.Serialize(portraitRows, _jsonOptions))
            : string.Empty;
        var portraitsAssetPath = UnrealSyncPathPolicy.PortraitsInfoAsset;
        var portraitsAssetFilePath = AssetObjectPathToFilePath(context, portraitsAssetPath);
        var portraitsChanged =
            context.AssetLibrary.IsPortraitPreviewEnabled &&
            (forceFullSync ||
                !File.Exists(portraitsAssetFilePath) ||
                !string.Equals(syncState.PortraitsHash, portraitsHash, StringComparison.OrdinalIgnoreCase));

        var importCount = importGroups.Sum(group => group.Files.Count);
        var deleteCount = deleteGroups.Sum(group => group.Assets.Count);
        var storyTableCount = storyTables.Count;
        var assetIndexTableCount = assetIndexTables.Count;
        var totalChanged = importCount + deleteCount + storyTableCount + assetIndexTableCount +
            (storyBranchesChanged ? 1 : 0) +
            (voiceMapsChanged ? 1 : 0) +
            (lustrationChanged ? 1 : 0) +
            (portraitsChanged ? 1 : 0);
        var changeItems = new List<string>();
        if (importCount > 0)
        {
            changeItems.Add($"新增或更新素材：{importCount} 个");
        }
        if (deleteCount > 0)
        {
            changeItems.Add($"清理虚幻中的多余素材：{deleteCount} 个");
        }
        if (storyTableCount > 0)
        {
            changeItems.Add($"更新剧情表：{storyTableCount} 张");
        }
        if (assetIndexTableCount > 0)
        {
            changeItems.Add($"更新素材索引表：{assetIndexTableCount} 张");
        }
        if (storyBranchesChanged)
        {
            changeItems.Add($"更新剧情选项分支：{storyBranches.Sum(source => source.Routes.Count)} 条");
        }
        if (voiceMapsChanged)
        {
            changeItems.Add($"更新剧情语音映射：{voiceMaps.Sum(entry => entry.AssignedCount)} 条");
        }
        if (lustrationChanged)
        {
            changeItems.Add($"更新立绘数据：{lustrationRows.Count} 个角色");
        }
        if (portraitsChanged)
        {
            changeItems.Add($"更新小预览数据：{portraitRows.Count} 个角色");
        }

        var unchangedItems = new List<string>();
        if (importCount == 0)
        {
            unchangedItems.Add("素材文件：没有变化");
        }
        if (deleteCount == 0)
        {
            unchangedItems.Add("多余素材：无需清理");
        }
        if (storyTableCount == 0)
        {
            unchangedItems.Add("剧情表：没有变化");
        }
        if (!assetIndexTablesChanged)
        {
            unchangedItems.Add("素材索引表：没有变化");
        }
        if (!storyBranchesChanged)
        {
            unchangedItems.Add(storyBranches.Count > 0
                ? "剧情选项分支：没有变化"
                : "剧情选项分支：尚未配置");
        }
        if (!voiceMapsChanged)
        {
            unchangedItems.Add("剧情语音映射：没有变化");
        }
        if (!lustrationChanged)
        {
            unchangedItems.Add("立绘数据：没有变化");
        }
        if (!portraitsChanged)
        {
            unchangedItems.Add(context.AssetLibrary.IsPortraitPreviewEnabled
                ? "小预览数据：没有变化"
                : "小预览数据：未启用");
        }

        var summary = totalChanged == 0
            ? "当前内容与虚幻项目一致，无需同步。"
            : forceFullSync
                ? $"已准备全部重新同步，共处理 {totalChanged} 项：{string.Join("；", changeItems)}。"
                : $"本次同步将处理 {totalChanged} 项：{string.Join("；", changeItems)}。";

        // 变量写入计划（背景图 / BGM / 环境音三个列表）。
        // 「要不要写」不在这里做增量判断：值一样时 GALLibrary 的写入器会直接跳过，
        // CDO 那条也只是赋一次值；真正的开销在"编译蓝图"，而那只发生在真的变了的时候。
        var variablePlan = UnrealSyncVariablePlanBuilder.Build(backgroundPaths, musicPaths, ambientPaths);

        return new UnrealSyncChangePlan(
            importGroups,
            deleteGroups,
            storyTables,
            assetIndexTables,
            renames,
            variablePlan,
            storyBranchesChanged,
            storyBranches,
            storyBranchesHash,
            voiceMapsChanged,
            voiceMaps,
            voiceMapsHash,
            lustrationChanged,
            lustrationRows,
            portraitsChanged,
            portraitRows,
            lustrationHash,
            portraitsHash,
            assetIndexTablesHash,
            sourceHashes,
            forceFullSync,
            totalChanged,
            summary,
            changeItems,
            unchangedItems);
    }

    public List<UnrealStoryBranchSourceSyncEntry> BuildStoryBranchSyncEntries(
        UnrealSyncContext context,
        IReadOnlyList<UnrealStoryTableSyncEntry> storyTables)
    {
        var result = new List<UnrealStoryBranchSourceSyncEntry>();
        var workspaceService = new ProjectWorkspaceService();
        foreach (var chapter in workspaceService.GetChapters(context.Project))
        {
            if (!_storyDocumentRepository.Exists(chapter))
            {
                continue;
            }

            var document = _storyDocumentRepository.Load(chapter);
            var chapterBaseCode = StoryCsvService.RemoveChapterSectionSuffix(chapter.Code);
            var projectPrefix = context.Project.Code.Trim() + "-";
            var managerRow = chapterBaseCode.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)
                ? chapterBaseCode[projectPrefix.Length..]
                : chapterBaseCode;
            result.AddRange(BuildChapterStoryBranchSyncEntries(
                managerRow,
                chapter.Code,
                document,
                storyTables));
        }

        return result
            .OrderBy(source => source.ManagerRow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.SourceTable, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<UnrealStoryBranchSourceSyncEntry> BuildChapterStoryBranchSyncEntries(
        string managerRow,
        string chapterCode,
        StoryDocument document,
        IReadOnlyList<UnrealStoryTableSyncEntry> storyTables)
    {
        var chapterTables = storyTables
            .Where(table => string.Equals(table.ChapterCode, chapterCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(table => table.Section)
            .ToList();
        if (chapterTables.Count == 0)
        {
            return [];
        }

        var tablesBySection = BuildChapterTablesBySection(chapterTables, chapterCode);
        var result = new List<UnrealStoryBranchSourceSyncEntry>();
        foreach (var section in StoryBranchFlowService.Build(document).Sections)
        {
            // Custom = 这一节的 AllRoad 交给手工，本来就不生成；没有对应表 = 文档里有小节、
            // 磁盘上却没有它的 CSV。后者正常不该出现，但真出现时少生成一条路线，
            // 也好过整次同步崩在 KeyNotFoundException 上（原先是 tablesBySection[...] 直取）。
            if (section.Kind == StoryBranchSectionKind.Custom ||
                !tablesBySection.TryGetValue(section.Section, out var sourceTable))
            {
                continue;
            }

            result.Add(new UnrealStoryBranchSourceSyncEntry(
                managerRow,
                sourceTable.TableAsset,
                section.Options.Count > 0
                    ? section.Options
                        .Where(option => option.EffectiveTargetSection is not null &&
                                         tablesBySection.ContainsKey(option.EffectiveTargetSection.Value))
                        .Select(option => new UnrealStoryBranchRouteSyncEntry(
                            option.ChoiceFunction,
                            option.OptionKey,
                            option.Text,
                            tablesBySection[option.EffectiveTargetSection!.Value].TableAsset,
                            RequiresChoiceCondition: true))
                        .ToList()
                    : section.DefaultTargetSection is not null && tablesBySection.ContainsKey(section.DefaultTargetSection.Value)
                        ?
                        [
                            new UnrealStoryBranchRouteSyncEntry(
                                ChoiceFunction: string.Empty,
                                OptionKey: string.Empty,
                                OptionText: string.Empty,
                                TargetTable: tablesBySection[section.DefaultTargetSection.Value].TableAsset,
                                RequiresChoiceCondition: false)
                        ]
                        : []));
        }

        return result;
    }

    /// <summary>
    /// 按小节号把章节的剧情表归组。
    ///
    /// 一个章节号只该有一张表，直接 ToDictionary 本来也行 —— 但章节目录里可能同时躺着
    /// `WHK-M6-06.csv`（导出器给第 1 节写的正式文件，名字就是章节号）和 `WHK-M6-00.csv`
    /// 这类**都能被解析成同一节**的历史文件（老的 -00 命名、手工拷贝……）。
    /// 那种情况下 ToDictionary 会抛「已添加了具有相同键的项」，整条同步链就断在这里，
    /// 而且报出来的异常跟真正的原因（多了一个 csv）毫无关系。
    /// 所以这里按节号归组，组内优先取**文件名就是章节号**的那一张，其余丢掉。
    /// </summary>
    private static Dictionary<int, UnrealStoryTableSyncEntry> BuildChapterTablesBySection(
        IReadOnlyList<UnrealStoryTableSyncEntry> chapterTables,
        string chapterCode)
    {
        var result = new Dictionary<int, UnrealStoryTableSyncEntry>();
        foreach (var group in chapterTables.GroupBy(table => table.Section))
        {
            result[group.Key] = group.FirstOrDefault(table => IsChapterMainStoryTable(table, chapterCode))
                                ?? group.First();
        }

        return result;
    }

    private static bool IsChapterMainStoryTable(UnrealStoryTableSyncEntry table, string chapterCode)
    {
        return string.Equals(
            Path.GetFileNameWithoutExtension(table.CsvPath),
            chapterCode,
            StringComparison.OrdinalIgnoreCase);
    }

    public List<UnrealVoiceMapSyncEntry> BuildVoiceMapSyncEntries(
        UnrealSyncContext context,
        IReadOnlyList<UnrealStoryTableSyncEntry> storyTables,
        ProjectVoiceMapState voiceMapState)
    {
        var result = new List<UnrealVoiceMapSyncEntry>();
        var stableRowIds = BuildStableVoiceRowIds(context);
        foreach (var table in storyTables)
        {
            if (!File.Exists(table.CsvPath))
            {
                continue;
            }

            var rows = _storyCsvService.ReadRows(table.CsvPath);
            var indexedRows = rows
                .Select((row, index) => new
                {
                    RowName = string.IsNullOrWhiteSpace(row.Get("Name"))
                        ? StoryCsvService.CreateRowName(index)
                        : row.Get("Name")
                })
                .Select(item =>
                {
                    var isNumeric = int.TryParse(item.RowName, out var rowNumber);
                    return new
                    {
                        item.RowName,
                        IsNumeric = isNumeric,
                        RowNumber = rowNumber
                    };
                })
                .Where(item => item.IsNumeric && item.RowNumber >= 0)
                .ToList();
            if (indexedRows.Count == 0)
            {
                continue;
            }

            var wavesByRowName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in indexedRows)
            {
                var exportRowKey = ProjectTextDataService.BuildExportRowKey(table.ChapterCode, table.Section, row.RowName);
                var rowId = stableRowIds.TryGetValue(exportRowKey, out var stableRowId)
                    ? ProjectTextDataService.BuildStableTextRowId(table.ChapterCode, stableRowId)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(rowId) ||
                    !voiceMapState.Voices.TryGetValue(rowId, out var voicePath) ||
                    !File.Exists(voicePath))
                {
                    continue;
                }

                var destination = GetProjectVoiceDestinationPath(context, voicePath);
                wavesByRowName[row.RowName] = BuildAssetObjectPath(destination, voicePath);
            }

            var waves = BuildSparseVoiceWaveSlots(indexedRows.Select(row => row.RowName).ToList(), wavesByRowName);
            var assignedCount = wavesByRowName.Count;
            if (assignedCount > 0)
            {
                result.Add(new UnrealVoiceMapSyncEntry(table.TableAsset, waves, assignedCount));
            }
        }

        return result
            .OrderBy(entry => entry.TableAsset, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<string, string> BuildStableVoiceRowIds(UnrealSyncContext context)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var workspaceService = new ProjectWorkspaceService();
        foreach (var chapter in workspaceService.GetChapters(context.Project))
        {
            var document = _storyDocumentRepository.Load(chapter);
            var localIndexes = new Dictionary<int, int>();
            foreach (var row in document.Rows)
            {
                var section = document.RowSections.TryGetValue(row.Get("Name"), out var mappedSection)
                    ? Math.Max(1, mappedSection)
                    : 1;
                var localIndex = localIndexes.TryGetValue(section, out var currentIndex) ? currentIndex + 1 : 1;
                localIndexes[section] = localIndex;
                var exportRowKey = ProjectTextDataService.BuildExportRowKey(
                    chapter.Code,
                    section,
                    StoryCsvService.CreateRowName(localIndex - 1));
                result[exportRowKey] = row.StableId;
            }
        }

        return result;
    }

    public static List<string?> BuildSparseVoiceWaveSlots(
        IReadOnlyList<string> rowNames,
        IReadOnlyDictionary<string, string> wavesByRowName)
    {
        var rowNumbers = rowNames
            .Select(rowName => int.TryParse(rowName, out var rowNumber) ? rowNumber : -1)
            .Where(rowNumber => rowNumber >= 0)
            .ToList();
        if (rowNumbers.Count == 0)
        {
            return [];
        }

        var maxRowNumber = rowNumbers.Max();
        var waves = new string?[maxRowNumber + 1];
        foreach (var pair in wavesByRowName)
        {
            if (int.TryParse(pair.Key, out var rowNumber) && rowNumber >= 0 && rowNumber < waves.Length)
            {
                waves[rowNumber] = pair.Value;
            }
        }

        return waves.ToList();
    }

    public List<UnrealAssetIndexTableSyncEntry> BuildAssetIndexTableSyncEntries(
        UnrealSyncContext context,
        string cacheFolder,
        IReadOnlyList<string> backgroundPaths,
        IReadOnlyList<string> musicPaths,
        IReadOnlyList<string> ambientPaths,
        IReadOnlyList<string> soundEffectPaths)
    {
        Directory.CreateDirectory(cacheFolder);

        // 只剩特殊音效这一张表。
        //
        // 老做法是 4 张索引/映射表（BGIndexMap 装背景图、BGMap 其实装的是 BGM、
        // SceneIndexMap 装环境音、ExsIndexMap 装特殊音效），游戏侧查表取素材。
        // 现在背景图 / BGM / 环境音都改成**直接写变量列表**（见 UnrealSyncVariablePlanBuilder），
        // 那三张表连同它们的行结构体一起退休 —— 而且那两个结构体
        // （/Script/GALLibrary.Texture2DTable、/Script/GALLibrary.WaveTable）
        // 在两个工程里都已经不存在了，本来也建不起来。
        //
        // 特殊音效**没有目标变量**，所以它仍然需要这张表。
        return
        [
            CreateAssetIndexTableSyncEntry(
                cacheFolder,
                "ExsIndexMap",
                UnrealSyncPathPolicy.EffectIndexTableAsset,
                UnrealSyncPathPolicy.EffectIndexTableRowStruct,
                UnrealSyncPathPolicy.EffectIndexTableValueColumn,
                soundEffectPaths
                    .Select(path => BuildSoundWaveReference(UnrealSyncPathPolicy.SceneEffectFolder, path))
                    .ToList())
        ];
    }

    public static string ComputeAssetIndexTablesHash(IReadOnlyList<UnrealAssetIndexTableSyncEntry> entries)
    {
        return ComputeSha256Hex("asset-index-tables-v1|" + string.Join(
            "\n",
            entries
                .OrderBy(entry => entry.TableAsset, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.TableAsset}|{entry.SourceHash}")));
    }

    public static List<string> GetCharacterLayerImportPaths(CharacterInfo character, CharacterLayerKind layerKind)
    {
        return CharacterLayerAssetService.GetLayerPaths(character, layerKind);
    }

    public static string GetLustrationLayerDestinationPath(
        UnrealSyncContext context,
        CharacterInfo character,
        CharacterLayerKind layerKind)
    {
        // 图层目录名的映射收口到 UnrealSyncPathPolicy.GetCharacterLayerFolderName 一处。
        //
        // 这里原来写的是：服装硬编码 "DN_Cloths"、其余走 CharacterLayerAssetService.GetFolderName。
        // 而那个 GetFolderName 是**本地素材库**那套（服装叫 DN_Cloth，单数），
        // 和目标不是同一套名字 —— 两边混用迟早出事，现在明确分开：
        // 本地布局用 GetFolderName，Unreal 目标用 PathPolicy。
        return UnrealSyncPathPolicy.GetCharacterLayerFolder(character.Code, layerKind);
    }

    public static string GetPortraitPreviewDestinationPath(UnrealSyncContext context, CharacterInfo character)
    {
        return UnrealSyncPathPolicy.GetPortraitPreviewFolder(character.Code);
    }

    private static string? BuildPortraitPreviewAssetReference(
        string destinationPath,
        IReadOnlyDictionary<string, string> previewPathsByLayerFileName,
        string layerPath)
    {
        return previewPathsByLayerFileName.TryGetValue(Path.GetFileName(layerPath), out var previewPath) && File.Exists(previewPath)
            ? BuildTextureReference(destinationPath, previewPath)
            : null;
    }

    public static string BuildTextureReference(string destinationPath, string sourcePath)
    {
        var assetName = NormalizeImportedAssetName(Path.GetFileNameWithoutExtension(sourcePath));
        return $"Texture2D'{destinationPath}/{assetName}.{assetName}'";
    }

    public static string BuildSoundWaveReference(string destinationPath, string sourcePath)
    {
        var assetName = NormalizeImportedAssetName(Path.GetFileNameWithoutExtension(sourcePath));
        return $"SoundWave'{destinationPath}/{assetName}.{assetName}'";
    }

    public static string GetProjectVoiceDestinationPath(UnrealSyncContext context, string voicePath)
    {
        var voiceRootPath = GetProjectVoiceFolderPath(context.Project);
        var relativeFolder = GetProjectVoiceRelativeFolder(voiceRootPath, voicePath);
        if (string.IsNullOrWhiteSpace(relativeFolder))
        {
            return UnrealSyncPathPolicy.VoiceFolder;
        }

        var segments = relativeFolder
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        if (segments.Length == 0)
        {
            return UnrealSyncPathPolicy.VoiceFolder;
        }

        segments[0] = SanitizeUnrealAssetName(StoryCsvService.RemoveChapterSectionSuffix(segments[0]));
        return $"{UnrealSyncPathPolicy.VoiceFolder}/{string.Join("/", segments)}";
    }

    public static string BuildAssetObjectPath(string destinationPath, string sourcePath)
    {
        var assetName = NormalizeImportedAssetName(Path.GetFileNameWithoutExtension(sourcePath));
        return $"{destinationPath}/{assetName}.{assetName}";
    }

    public static string? ResolveEditorExecutable(string enginePath)
    {
        if (string.IsNullOrWhiteSpace(enginePath))
        {
            return null;
        }

        if (File.Exists(enginePath))
        {
            var fileName = Path.GetFileName(enginePath);
            if (fileName.Equals("UnrealEditor-Cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                return enginePath;
            }

            if (fileName.Equals("UnrealEditor.exe", StringComparison.OrdinalIgnoreCase))
            {
                var cmdPath = Path.Combine(Path.GetDirectoryName(enginePath)!, "UnrealEditor-Cmd.exe");
                return File.Exists(cmdPath) ? cmdPath : enginePath;
            }

            return null;
        }

        var binPath = Path.Combine(enginePath, "Engine", "Binaries", "Win64");
        var cmdCandidate = Path.Combine(binPath, "UnrealEditor-Cmd.exe");
        if (File.Exists(cmdCandidate))
        {
            return cmdCandidate;
        }

        var editorCandidate = Path.Combine(binPath, "UnrealEditor.exe");
        return File.Exists(editorCandidate) ? editorCandidate : null;
    }

    public static int CountAssets(string folderPath)
    {
        return Directory.Exists(folderPath)
            ? Directory.EnumerateFiles(folderPath, "*.uasset", SearchOption.TopDirectoryOnly).Count()
            : 0;
    }

    public static int CountAssetsRecursive(string folderPath)
    {
        return Directory.Exists(folderPath)
            ? Directory.EnumerateFiles(folderPath, "*.uasset", SearchOption.AllDirectories).Count()
            : 0;
    }

    public static string AssetObjectPathToFilePath(UnrealSyncContext context, string objectPath)
    {
        var packagePath = objectPath.Split('.')[0].Trim('/');
        var targetRoot = UnrealSyncPathPolicy.AssetMaterialRoot.Trim('/');
        var relativePath = packagePath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)
            ? packagePath[targetRoot.Length..].Trim('/')
            : packagePath;

        return Path.Combine(
            context.TargetContentFolderPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar) + ".uasset");
    }

    private static string AssetFolderPathToFilePath(UnrealSyncContext context, string folderPath)
    {
        var targetRoot = UnrealSyncPathPolicy.AssetMaterialRoot.Trim('/');
        var relativePath = folderPath.Trim('/').StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)
            ? folderPath.Trim('/')[targetRoot.Length..].Trim('/')
            : folderPath.Trim('/');

        return Path.Combine(context.TargetContentFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string? BuildAssetObjectPathFromAssetFile(UnrealSyncContext context, string assetFilePath)
    {
        var relativePath = Path.GetRelativePath(context.TargetContentFolderPath, assetFilePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var packagePath = Path.ChangeExtension(relativePath, null)?.Replace(Path.DirectorySeparatorChar, '/');
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return null;
        }

        var assetName = Path.GetFileNameWithoutExtension(assetFilePath);
        return $"{UnrealSyncPathPolicy.AssetMaterialRoot.TrimEnd('/')}/{packagePath}.{assetName}";
    }

    private static string BuildAssetFolderPathFromFolder(UnrealSyncContext context, string folderPath)
    {
        var relativePath = Path.GetRelativePath(context.TargetContentFolderPath, folderPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return UnrealSyncPathPolicy.AssetMaterialRoot;
        }

        return $"{UnrealSyncPathPolicy.AssetMaterialRoot.TrimEnd('/')}/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
    }

    public UnrealSyncState ReadState(UnrealSyncContext context)
    {
        var path = GetUnrealSyncStatePath(context);
        if (!File.Exists(path))
        {
            return new UnrealSyncState();
        }

        try
        {
            var state = JsonSerializer.Deserialize<UnrealSyncState>(File.ReadAllText(path)) ?? new UnrealSyncState();
            state.SourceHashes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return state;
        }
        catch
        {
            return new UnrealSyncState();
        }
    }

    /// <summary>
    /// 记下"同步到哪了"。
    ///
    /// ⚠️ **部分成功也要记** —— 这是这份状态最要紧的一条语义。
    ///
    /// 以前只有整次全部成功才写（看调用点那个 if），而那意味着：只要有一项没过，
    /// 下次就把**全部**重新导一遍。用户 2026-10-13 的原话是「每次都要全量导入」——
    /// 368 个素材每次重导几分钟，而其中绝大多数上一次就已经导好了。
    ///
    /// 所以现在按「这次确实成功了的」来记：
    /// - **导入**：只记复扫里 Ok 的那些，failedImportObjectPaths 里的排除掉；
    /// - **数据表**：只有本次表数量对得上时才记（tablesComplete），否则保留旧值下次重来；
    /// - **那几个整段哈希**（立绘 / 预览 / 索引表 / 语音映射 / 剧情分支）：
    ///   只有整次成功才更新 —— 它们对应的是"改一处、整段重写"的操作，
    ///   做一半就记下来会让失败的那段**永远补不上**。
    ///
    /// 一句话：**成功的别重做，失败的别漏掉。**
    /// </summary>
    public void WriteState(
        UnrealSyncContext context,
        UnrealSyncChangePlan changePlan,
        IReadOnlyCollection<string>? failedImportObjectPaths = null,
        bool tablesComplete = true,
        bool wholeRunSucceeded = true)
    {
        var statePath = GetUnrealSyncStatePath(context);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var state = ReadState(context);
        state.StateVersion = CurrentUnrealSyncStateVersion;
        state.LastSyncedAt = DateTimeOffset.Now;

        if (wholeRunSucceeded)
        {
            state.LustrationHash = changePlan.LustrationHash;
            state.PortraitsHash = changePlan.PortraitsHash;
            state.AssetIndexTablesHash = changePlan.AssetIndexTablesHash;
            state.VoiceMapsHash = changePlan.VoiceMapsHash;
            state.StoryBranchesHash = changePlan.StoryBranchesHash;
            var storyBranchAssetPath = UnrealObjectPathToContentFilePath(context, StoryBranchDataTableAssetPath);
            state.StoryBranchesAssetHash = File.Exists(storyBranchAssetPath)
                ? ComputeFileHash(storyBranchAssetPath)
                : string.Empty;
        }
        // 整段哈希不更新时**保持旧值**（ReadState 已经把旧状态读回来了）——
        // 下次检测会发现它们还是"变了"，于是那几段会重来一遍。这正是我们要的。

        // 哪些不记：这次复扫明确失败了的导入。
        var failed = failedImportObjectPaths is null || failedImportObjectPaths.Count == 0
            ? null
            : new HashSet<string>(failedImportObjectPaths, StringComparer.OrdinalIgnoreCase);

        // 数据表没全过时，把表那几条也排除掉（它们的键就是表的资产路径）。
        HashSet<string>? incompleteTables = null;
        if (!tablesComplete && changePlan.StoryTables.Count > 0)
        {
            incompleteTables = new HashSet<string>(
                changePlan.StoryTables.Select(table => table.TableAsset),
                StringComparer.OrdinalIgnoreCase);
        }

        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (objectPath, hash) in changePlan.SourceHashes)
        {
            if (failed?.Contains(objectPath) == true || incompleteTables?.Contains(objectPath) == true)
            {
                continue;
            }

            sourceHashes[objectPath] = hash;
        }

        state.SourceHashes = sourceHashes;
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, _jsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void AddImportGroup(List<UnrealSyncImportGroup> groups, string destination, IReadOnlyCollection<string> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        groups.Add(new UnrealSyncImportGroup(destination, files.OrderBy(Path.GetFileName).ToList()));
    }

    private static void AddProjectVoiceImportGroups(
        List<UnrealSyncImportGroup> groups,
        UnrealSyncContext context,
        IReadOnlyCollection<string> voicePaths)
    {
        foreach (var group in voicePaths
            .Where(File.Exists)
            .GroupBy(path => GetProjectVoiceDestinationPath(context, path), StringComparer.OrdinalIgnoreCase))
        {
            AddImportGroup(groups, group.Key, group.ToList());
        }
    }

    /// <summary>
    /// 找出"其实是同一个素材改了名"的项 —— 新目标路径的源哈希，等于某个待删旧路径的源哈希。
    ///
    /// 【判据的两个前提】
    /// 1. **文件改名内容不变**，所以哈希相同；内容改过的话哈希不同，就不算改名
    ///    （那种本该导新删旧，是对的）。
    /// 2. **目标路径上还没有资产** —— 已经有的那是"覆盖"，不是"改名"。
    ///    改名的前提是旧资产还在（下面查了磁盘），两者不会同时成立。
    ///
    /// 【为什么不新增状态字段】
    /// <c>state.SourceHashes</c> 已经是"目标对象路径 → 源文件内容哈希"，
    /// 而这一轮的 <paramref name="sourceHashes"/> 是同一个口径的新值 —— 两下一比就够了。
    /// 多存一份"源文件 → 目标路径"的映射当然更直接，但那要改状态文件结构、
    /// 还得处理老状态没有那个字段的情况；用哈希比是零成本的。
    /// </summary>
    private List<UnrealSyncRenameEntry> BuildUnrealSyncRenameEntries(
        UnrealSyncContext context,
        IReadOnlyList<UnrealSyncImportGroup> allImportGroups,
        IReadOnlyList<UnrealSyncDeleteGroup> deleteGroups,
        IReadOnlyDictionary<string, string> sourceHashes,
        UnrealSyncState syncState)
    {
        if (syncState.SourceHashes.Count == 0 || deleteGroups.Count == 0)
        {
            return [];
        }

        // 待删的旧路径 → 它当初的源哈希。只有"磁盘上确实还有这个 .uasset"的才是候选 ——
        // 已经没了的没什么可改名的。
        var deleteCandidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in deleteGroups)
        {
            foreach (var assetPath in group.Assets)
            {
                if (string.IsNullOrWhiteSpace(assetPath) ||
                    !syncState.SourceHashes.TryGetValue(assetPath, out var oldHash) ||
                    string.IsNullOrWhiteSpace(oldHash))
                {
                    continue;
                }

                if (File.Exists(AssetObjectPathToFilePath(context, assetPath)))
                {
                    deleteCandidates[assetPath] = oldHash;
                }
            }
        }

        if (deleteCandidates.Count == 0)
        {
            return [];
        }

        var renames = new List<UnrealSyncRenameEntry>();
        var claimedOldPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in allImportGroups)
        {
            foreach (var filePath in group.Files)
            {
                var targetObjectPath = BuildAssetObjectPath(group.Destination, filePath);

                // 目标已经有了 → 那是覆盖，不是改名。
                if (File.Exists(AssetObjectPathToFilePath(context, targetObjectPath)))
                {
                    continue;
                }

                if (!sourceHashes.TryGetValue(targetObjectPath, out var newHash) ||
                    string.IsNullOrWhiteSpace(newHash))
                {
                    continue;
                }

                // 一对一：一个旧路径只能被认领一次（两个源文件内容碰巧一样时，
                // 不限制就会让第二个也去改同一个旧资产）。
                var oldPath = deleteCandidates
                    .Where(pair => !claimedOldPaths.Contains(pair.Key) &&
                                   string.Equals(pair.Value, newHash, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(oldPath))
                {
                    continue;
                }

                claimedOldPaths.Add(oldPath);
                var oldName = Path.GetFileName(oldPath).Split('.')[0];
                renames.Add(new UnrealSyncRenameEntry(
                    oldPath,
                    targetObjectPath,
                    $"{oldName} → {Path.GetFileNameWithoutExtension(filePath)}"));
            }
        }

        return renames;
    }

    private List<UnrealSyncDeleteGroup> BuildDeleteGroups(
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters,
        IReadOnlyList<ImageSetSyncInput> imageSets,
        IReadOnlyCollection<string> musicPaths,
        IReadOnlyCollection<string> ambientPaths,
        IReadOnlyCollection<string> soundEffectPaths,
        IReadOnlyCollection<string> voicePaths,
        IReadOnlyList<UnrealStoryTableSyncEntry> storyTables,
        Func<CharacterInfo, IReadOnlyDictionary<string, string>> getPortraitPreviewPathsByLayerFileName,
        UnrealSyncState syncState)
    {
        var groups = new List<UnrealSyncDeleteGroup>();
        // 「上一次同步时工具箱写下去的那些」—— 给和工程共用的目录当白名单用。
        var managedObjectPaths = syncState.SourceHashes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 图集分类：和导入组同一个循环，一一对应。
        // 它们的落点都是工具箱独占的子目录，所以直接清空"不在期望集合里的"。
        foreach (var imageSet in imageSets)
        {
            AddDeleteGroup(
                groups,
                context,
                imageSet.DestinationFolder,
                imageSet.FilePaths.Select(GetExpectedImportedAssetName));
        }

        AddDeleteGroup(groups, context, UnrealSyncPathPolicy.BgmFolder, musicPaths.Select(GetExpectedImportedAssetName));

        // ⚠️ 场景音效的目录**和工程共用**：`Sound/Scene_Effect` 里除了工具箱导进去的
        // Sc*/SE*，还住着工程自己的 MetaScene / Scene_Class。
        // 所以这里不能"清空不在期望集合里的"，只能删**工具箱自己写过的那些**。
        // 2026-10-13 实测：整目录清一次就把那两个工程资产清掉了（已从提交还原）。
        //
        // 判断"这个目录归谁"不能只看路径 —— 落点搬进 `Sound/` 就意味着一部分目录是共用的。
        AddDeleteGroup(
            groups,
            context,
            UnrealSyncPathPolicy.SceneEffectFolder,
            ambientPaths.Concat(soundEffectPaths).Select(GetExpectedImportedAssetName),
            restrictToObjectPaths: managedObjectPaths);

        AddProjectVoiceDeleteGroups(groups, context, voicePaths);

        var expectedExcelTextAssets = storyTables
            .Select(entry => entry.TableAsset)
            .Concat(new[]
            {
                UnrealSyncPathPolicy.EffectIndexTableAsset,
                // ⚠️ 剧情选项分支表**必须列进来**：这个删除组是 recursive 的，
                // 凡是 ExcelTexts 下不在期望集合里的资产都会被删。
                // （2026-10-13：常量与落点已经对齐 —— StoryBranchDataTableAssetPath
                //   现在就是 ExcelTexts/AllRoad。这条保留是**结构上的保底**：
                //   以后常量万一再和落点走岔，至少不会顺手把它删掉。）
                StoryBranchDataTableAssetPath
            });
        AddDeleteGroupByExpectedAssets(groups, context, UnrealSyncPathPolicy.ExcelTextsRoot, expectedExcelTextAssets, recursive: true);

        // ⚠️ 这里原来用的名字是 **DA_Portraits** —— 那是旧布局的叫法。
        // 新布局里立绘信息就是 DA_LustrationInfor 一个，预览那个叫
        // DA_LustrationPreviewInfor 且**不在这个目录**（它在 LustrationPreview_Image）。
        // 所以这个"期望集合"里只该有 DA_LustrationInfor；
        // 挂着个不存在的旧名字看着无害，其实是在掩盖"这里没跟上落点变化"这件事。
        // 2026-10-13 对的这一遍。
        var expectedLustrationRootAssets = new[] { "DA_LustrationInfor" };
        AddDeleteGroup(groups, context, $"{UnrealSyncPathPolicy.LustrationRoot}", expectedLustrationRootAssets);
        AddLustrationLayerDeleteGroups(groups, context, characters);
        if (context.AssetLibrary.IsPortraitPreviewEnabled)
        {
            AddPortraitPreviewDeleteGroups(groups, context, characters, getPortraitPreviewPathsByLayerFileName);
        }

        return groups;
    }

    private static void AddProjectVoiceDeleteGroups(
        List<UnrealSyncDeleteGroup> groups,
        UnrealSyncContext context,
        IReadOnlyCollection<string> voicePaths)
    {
        var expectedAssetPaths = voicePaths
            .Where(File.Exists)
            .Select(path => BuildAssetObjectPath(GetProjectVoiceDestinationPath(context, path), path))
            .ToList();

        // ⚠️ **逐章清，不是把 Sound/Voice 整个递归清一遍。**
        //
        // 为什么：`Sound/Voice` 底下**同时住着工程自己的东西** ——
        // Voice_Class / Voice_Meta / 角色1 / 角色2。整目录递归会把它们一起清掉。
        // 2026-10-13 实测就是这么弄丢的（六个文件已从提交还原）。
        //
        // 而工具箱的东西都在**章节子目录**里（`Sound/Voice/WHK-M1/…`）——
        // 扫到那一层就正好：既能把几百条历史语音清干净（用户要的就是这个），
        // 又碰不到散在 Sound/Voice 根下的那几个。
        //
        // 顺带：章节改名/删除后留下的**旧章节目录**照样会被扫到（因为我们遍历的是
        // 磁盘上的子目录，不是"这次要导的章"），所以历史残留不会越积越多。
        var voiceFolderPath = AssetFolderPathToFilePath(context, UnrealSyncPathPolicy.VoiceFolder);
        if (!Directory.Exists(voiceFolderPath))
        {
            return;
        }

        foreach (var chapterFolderPath in Directory.EnumerateDirectories(voiceFolderPath))
        {
            var chapterFolder = BuildAssetFolderPathFromFolder(context, chapterFolderPath);
            if (string.IsNullOrWhiteSpace(chapterFolder))
            {
                continue;
            }

            var expectedInChapter = expectedAssetPaths
                .Where(path => path.StartsWith(chapterFolder + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            AddDeleteGroupByExpectedAssets(groups, context, chapterFolder, expectedInChapter, recursive: true);
        }
    }

    private static string GetProjectVoiceRelativeFolder(string voiceRootPath, string voicePath)
    {
        if (string.IsNullOrWhiteSpace(voiceRootPath) || !File.Exists(voicePath))
        {
            return string.Empty;
        }

        var relativePath = Path.GetRelativePath(voiceRootPath, voicePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return string.Empty;
        }

        return Path.GetDirectoryName(relativePath) ?? string.Empty;
    }

    private static void AddLustrationLayerDeleteGroups(
        List<UnrealSyncDeleteGroup> groups,
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters)
    {
        var expectedByDestination = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in characters)
        {
            AddExpectedLustrationLayerAssets(expectedByDestination, context, character, CharacterLayerKind.Cloth);
            AddExpectedLustrationLayerAssets(expectedByDestination, context, character, CharacterLayerKind.Face);
            AddExpectedLustrationLayerAssets(expectedByDestination, context, character, CharacterLayerKind.Adorn);
        }

        var lustrationFolderPath = AssetFolderPathToFilePath(context, $"{UnrealSyncPathPolicy.LustrationRoot}");
        if (!Directory.Exists(lustrationFolderPath))
        {
            return;
        }

        var managedLayerFolders = new HashSet<string>(
            Enum.GetValues<CharacterLayerKind>()
                .Where(UnrealSyncPathPolicy.IsSyncableCharacterLayer)
                .Select(UnrealSyncPathPolicy.GetCharacterLayerFolderName),
            StringComparer.OrdinalIgnoreCase);
        var extrasByDestination = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetFilePath in Directory.EnumerateFiles(lustrationFolderPath, "*.uasset", SearchOption.AllDirectories))
        {
            var layerFolderPath = Path.GetDirectoryName(assetFilePath);
            if (string.IsNullOrWhiteSpace(layerFolderPath) || !managedLayerFolders.Contains(Path.GetFileName(layerFolderPath)))
            {
                continue;
            }

            var destination = BuildAssetFolderPathFromFolder(context, layerFolderPath);
            var assetName = Path.GetFileNameWithoutExtension(assetFilePath);
            if (expectedByDestination.TryGetValue(destination, out var expectedNames) && expectedNames.Contains(assetName))
            {
                continue;
            }

            var assetPath = BuildAssetObjectPathFromAssetFile(context, assetFilePath);
            if (assetPath is null)
            {
                continue;
            }

            if (!extrasByDestination.TryGetValue(destination, out var extras))
            {
                extras = [];
                extrasByDestination[destination] = extras;
            }

            extras.Add(assetPath);
        }

        foreach (var (destination, extras) in extrasByDestination)
        {
            groups.Add(new UnrealSyncDeleteGroup(
                destination,
                extras.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()));
        }
    }

    private static void AddExpectedLustrationLayerAssets(
        Dictionary<string, HashSet<string>> expectedByDestination,
        UnrealSyncContext context,
        CharacterInfo character,
        CharacterLayerKind layerKind)
    {
        var destination = GetLustrationLayerDestinationPath(context, character, layerKind);
        if (!expectedByDestination.TryGetValue(destination, out var expectedNames))
        {
            expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            expectedByDestination[destination] = expectedNames;
        }

        foreach (var assetName in GetCharacterLayerImportPaths(character, layerKind).Select(GetExpectedImportedAssetName))
        {
            expectedNames.Add(assetName);
        }
    }

    private static void AddPortraitPreviewDeleteGroups(
        List<UnrealSyncDeleteGroup> groups,
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters,
        Func<CharacterInfo, IReadOnlyDictionary<string, string>> getPortraitPreviewPathsByLayerFileName)
    {
        // ⚠️ 预览现在是**一个平铺目录**（LustrationPreview_Image 下直接放），
        // 不是按角色分子目录。所以这里必须**一次性**为整个目录建一个删除组，
        // 期望集合是「所有角色的预览 + 那个数据资产」。
        //
        // 原来写成"按角色各建一个"，而 destination 对所有角色是同一个 ——
        // 于是第一个角色的那一份就把别人的预览、以及数据资产
        // DA_LustrationPreviewInfor 全当成"多余的"删掉。
        // 2026-10-13 实测就是这么把它弄丢的（已从快照恢复）。
        var previewAssetNames = characters
            .SelectMany(character => getPortraitPreviewPathsByLayerFileName(character).Values)
            .Select(GetExpectedImportedAssetName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 数据资产跟预览图放在同一个目录里，必须一起列进期望集合 ——
        // 不列进去，它就会被自己那个"清理多余素材"的步骤删掉。
        previewAssetNames.Add("DA_LustrationPreviewInfor");
        if (characters.Count > 0)
        {
            AddDeleteGroup(
                groups,
                context,
                GetPortraitPreviewDestinationPath(context, characters[0]),
                previewAssetNames);
        }

        var lustrationFolderPath = AssetFolderPathToFilePath(context, $"{UnrealSyncPathPolicy.LustrationRoot}");
        if (!Directory.Exists(lustrationFolderPath))
        {
            return;
        }

        var activeCharacterCodes = characters.Select(character => character.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var logPreviewFolderPath in Directory.EnumerateDirectories(lustrationFolderPath, "Log_Preview", SearchOption.AllDirectories))
        {
            var characterFolderName = Directory.GetParent(logPreviewFolderPath)?.Name;
            if (!string.IsNullOrWhiteSpace(characterFolderName) && activeCharacterCodes.Contains(characterFolderName))
            {
                continue;
            }

            var destination = BuildAssetFolderPathFromFolder(context, logPreviewFolderPath);
            var extras = Directory
                .EnumerateFiles(logPreviewFolderPath, "*.uasset", SearchOption.TopDirectoryOnly)
                .Select(path => BuildAssetObjectPathFromAssetFile(context, path))
                .Where(assetPath => assetPath is not null)
                .Cast<string>()
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (extras.Count > 0)
            {
                groups.Add(new UnrealSyncDeleteGroup(destination, extras));
            }
        }
    }

    private static void AddDeleteGroup(
        List<UnrealSyncDeleteGroup> groups,
        UnrealSyncContext context,
        string destination,
        IEnumerable<string> expectedAssetNames,
        bool recursive = false,
        IReadOnlySet<string>? restrictToObjectPaths = null)
    {
        var folderPath = AssetFolderPathToFilePath(context, destination);
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        var expected = new HashSet<string>(expectedAssetNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var extras = Directory
            .EnumerateFiles(folderPath, "*.uasset", searchOption)
            .Select(path => BuildAssetObjectPathFromAssetFile(context, path))
            .Where(assetPath => assetPath is not null)
            .Cast<string>()
            .Where(assetPath => !expected.Contains(GetAssetNameFromObjectPath(assetPath)))
            // ⚠️ restrictToObjectPaths：**只有在这个名单里的才删**。
            //
            // 给「和工程共用」的目录用（`Sound/Scene_Effect` 里同时住着工具箱的 Sc*/SE*
            // 和工程自己的 MetaScene / Scene_Class）。名单来自同步状态 ——
            // 记的是**上一次同步时工具箱确实写下去的那些**。
            //
            // 2026-10-13 实测：整目录清一次，那两个工程资产就被清掉了（已从提交还原）。
            // 判断"这个目录归谁"不能只看路径 —— 落点在 Sound/ 底下就意味着共用。
            .Where(assetPath => restrictToObjectPaths is null || restrictToObjectPaths.Contains(assetPath))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extras.Count > 0)
        {
            groups.Add(new UnrealSyncDeleteGroup(destination, extras));
        }
    }

    private static void AddDeleteGroupByExpectedAssets(
        List<UnrealSyncDeleteGroup> groups,
        UnrealSyncContext context,
        string destination,
        IEnumerable<string> expectedAssetPaths,
        bool recursive = false)
    {
        var folderPath = AssetFolderPathToFilePath(context, destination);
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        var expected = new HashSet<string>(expectedAssetPaths.Where(path => !string.IsNullOrWhiteSpace(path)), StringComparer.OrdinalIgnoreCase);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var extras = Directory
            .EnumerateFiles(folderPath, "*.uasset", searchOption)
            .Select(path => BuildAssetObjectPathFromAssetFile(context, path))
            .Where(assetPath => assetPath is not null)
            .Cast<string>()
            .Where(assetPath => !expected.Contains(assetPath))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (extras.Count > 0)
        {
            groups.Add(new UnrealSyncDeleteGroup(destination, extras));
        }
    }

    private static bool SourceFileNeedsImport(
        UnrealSyncContext context,
        string destinationPath,
        string sourcePath,
        UnrealSyncState state,
        IReadOnlyDictionary<string, string> sourceHashes)
    {
        var objectPath = BuildAssetObjectPath(destinationPath, sourcePath);
        var legacyObjectPath = BuildLegacyAssetObjectPath(destinationPath, sourcePath);
        var assetFilePath = AssetObjectPathToFilePath(context, objectPath);
        var alternativeAssetFilePath = BuildImportedAssetFilePath(context, destinationPath, sourcePath);
        return SourceFileNeedsAnyAssetUpdate(objectPath, legacyObjectPath, state, sourceHashes, assetFilePath, alternativeAssetFilePath);
    }

    private static bool SourceFileNeedsAssetUpdate(
        UnrealSyncContext context,
        string sourcePath,
        string objectPath,
        UnrealSyncState state,
        IReadOnlyDictionary<string, string> sourceHashes)
    {
        return SourceFileNeedsAnyAssetUpdate(objectPath, null, state, sourceHashes, AssetObjectPathToFilePath(context, objectPath));
    }

    private static bool SourceFileNeedsAnyAssetUpdate(
        string objectPath,
        string? legacyObjectPath,
        UnrealSyncState state,
        IReadOnlyDictionary<string, string> sourceHashes,
        params string[] assetFilePaths)
    {
        var existingAssetPath = assetFilePaths.FirstOrDefault(File.Exists);
        if (existingAssetPath is null)
        {
            return true;
        }

        if (!sourceHashes.TryGetValue(objectPath, out var currentHash))
        {
            return true;
        }

        var hasPreviousHash = state.SourceHashes.TryGetValue(objectPath, out var previousHash);
        if (!hasPreviousHash && !string.IsNullOrWhiteSpace(legacyObjectPath))
        {
            hasPreviousHash = state.SourceHashes.TryGetValue(legacyObjectPath, out previousHash);
        }

        return !hasPreviousHash || !string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ComputeSourceHashes(
        IReadOnlyList<UnrealSyncImportGroup> importGroups,
        IReadOnlyList<UnrealStoryTableSyncEntry> storyTables)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in importGroups)
        {
            foreach (var sourcePath in group.Files.Where(File.Exists))
            {
                hashes[BuildAssetObjectPath(group.Destination, sourcePath)] = ComputeFileSha256Hex(sourcePath);
            }
        }

        foreach (var table in storyTables.Where(table => File.Exists(table.CsvPath)))
        {
            hashes[table.TableAsset] = ComputeFileSha256Hex(table.CsvPath);
        }

        return hashes;
    }

    private static string ComputeFileSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string BuildImportedAssetFilePath(UnrealSyncContext context, string destinationPath, string sourcePath)
    {
        var targetRoot = UnrealSyncPathPolicy.AssetMaterialRoot.Trim('/');
        var relativeDestination = destinationPath.Trim('/').StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)
            ? destinationPath.Trim('/')[targetRoot.Length..].Trim('/')
            : destinationPath.Trim('/');
        var assetName = NormalizeImportedAssetName(Path.GetFileNameWithoutExtension(sourcePath));
        return Path.Combine(
            context.TargetContentFolderPath,
            relativeDestination.Replace('/', Path.DirectorySeparatorChar),
            assetName + ".uasset");
    }

    public static string UnrealObjectPathToContentFilePath(UnrealSyncContext context, string objectPath)
    {
        var packagePath = objectPath.Split('.')[0].Replace('\\', '/');
        var relativePath = packagePath.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)
            ? packagePath["/Game/".Length..]
            : packagePath.Trim('/');
        var contentRoot = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Content");
        return Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar) + ".uasset");
    }

    private static string GetExpectedImportedAssetName(string sourcePath)
    {
        return NormalizeImportedAssetName(Path.GetFileNameWithoutExtension(sourcePath));
    }

    private static string GetAssetNameFromObjectPath(string objectPath)
    {
        var packagePath = objectPath.Split('.')[0].Trim('/');
        return packagePath.Split('/').Last();
    }

    private static string NormalizeImportedAssetName(string assetName)
    {
        var normalized = new string(assetName
            .Select(character => char.IsWhiteSpace(character) || InvalidUnrealObjectNameCharacters.Contains(character)
                ? '_'
                : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "Asset" : normalized;
    }

    private static string BuildLegacyAssetObjectPath(string destinationPath, string sourcePath)
    {
        var assetName = Path.GetFileNameWithoutExtension(sourcePath);
        return $"{destinationPath}/{assetName}.{assetName}";
    }

    private UnrealSyncProgressUpdate? ReadProgressUpdate(string progressPath, ref string? lastProgressJson)
    {
        try
        {
            if (!File.Exists(progressPath))
            {
                return null;
            }

            var json = ReadSharedText(progressPath);
            if (string.Equals(json, lastProgressJson, StringComparison.Ordinal))
            {
                return null;
            }

            var update = JsonSerializer.Deserialize<UnrealSyncProgressFile>(json, _jsonOptions);
            if (update is null || update.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(update.Message))
            {
                return null;
            }

            lastProgressJson = json;
            var detail = update.Total > 0 ? $" ({update.Completed}/{update.Total})" : string.Empty;
            return new UnrealSyncProgressUpdate(update.Message + detail, Math.Clamp(update.Percent, 0, 100));
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private UnrealSyncExecutionReport? ReadExecutionReport(string resultPath)
    {
        try
        {
            return File.Exists(resultPath)
                ? JsonSerializer.Deserialize<UnrealSyncExecutionReport>(ReadSharedText(resultPath), _jsonOptions)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void KillUnrealSyncProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort: the process may have exited between the check and kill.
        }
    }

    private static string BuildStoryTableFolder(UnrealSyncContext context, ChapterInfo chapter, bool hasMultipleSections)
    {
        var categoryFolder = GetChapterCategoryFolder(chapter.Type);
        var folder = $"{UnrealSyncPathPolicy.ExcelTextsRoot}/{categoryFolder}";
        return hasMultipleSections
            ? $"{folder}/{SanitizeUnrealAssetName(StoryCsvService.RemoveChapterSectionSuffix(chapter.Code))}"
            : folder;
    }

    private List<string> BuildLegacyStoryTableAssets(
        UnrealSyncContext context,
        ChapterInfo chapter,
        string csvPath,
        string targetFolder,
        string targetAssetName)
    {
        var categoryFolder = GetChapterCategoryFolder(chapter.Type);
        var rootFolder = $"{UnrealSyncPathPolicy.ExcelTextsRoot}/{categoryFolder}";
        var previousFolder = $"{rootFolder}/{SanitizeUnrealAssetName(chapter.Code)}";
        var previousUnderscoreFolder = $"{rootFolder}/{SanitizeUnrealAssetName(chapter.Code.Replace('-', '_'))}";
        var legacyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(csvPath)),
            SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(csvPath).Replace('-', '_')),
            $"{StoryCsvService.BuildSectionCsvBaseName(chapter.Code)}_小节{_storyCsvService.TryParseStorySectionFromFileName(chapter, csvPath) ?? 1}"
        };
        foreach (var legacyUnderscoreName in legacyNames.Select(name => name.Replace('-', '_')).ToList())
        {
            legacyNames.Add(legacyUnderscoreName);
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { rootFolder, previousFolder, previousUnderscoreFolder, targetFolder })
        {
            foreach (var name in legacyNames)
            {
                var objectPath = $"{folder}/{name}.{name}";
                if (!string.Equals(objectPath, $"{targetFolder}/{targetAssetName}.{targetAssetName}", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(objectPath);
                }
            }
        }

        return result.ToList();
    }

    private List<StoryTableCsvEntry> GetChapterStoryCsvPathsForSync(ChapterInfo chapter)
    {
        var sectionFiles = _storyCsvService.GetLocalSectionCsvPaths(chapter)
            .OrderBy(item => item.Section)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sectionFiles.Count == 0)
        {
            return [];
        }

        var activeSections = new List<StorySectionCsvFile>();
        foreach (var sectionFile in sectionFiles)
        {
            var rows = _storyCsvService.ReadRows(sectionFile.Path);
            if (rows.Count == 0)
            {
                if (sectionFile.Section > 1 && File.Exists(sectionFile.Path))
                {
                    File.Delete(sectionFile.Path);
                }

                continue;
            }

            activeSections.Add(sectionFile);
        }

        if (activeSections.Count == 0)
        {
            return [];
        }

        var hasMultipleSections = activeSections.Count > 1 || activeSections.Any(item => item.Section > 1);
        var mainCsvPath = _storyCsvService.GetChapterCsvPath(chapter);
        return activeSections
            // 同一节只留一张表：正牌文件是**文件名就是章节号**的那份（导出器给第 1 节写的就是它）。
            // 目录里多出来的 `-00.csv` 这类历史文件同样能被解析成第 1 节，放过去的话
            // 计划里会出现两个算出来同名（都是 `章节基名-00`）的目标资产，而且会让按节号
            // 建索引的 AllRoad 生成直接撞重复键。
            .GroupBy(item => item.Section)
            .Select(group => group.FirstOrDefault(item => PathsEqual(item.Path, mainCsvPath)) ?? group.First())
            .OrderBy(item => item.Section)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => new StoryTableCsvEntry(
                item.Path,
                hasMultipleSections ? StoryCsvService.BuildSectionCsvFileBaseName(chapter.Code, item.Section) : StoryCsvService.BuildSectionCsvBaseName(chapter.Code),
                hasMultipleSections))
            .ToList();
    }

    private static string GetChapterCategoryFolder(string chapterType)
    {
        return chapterType switch
        {
            ChapterKind.MainThread => "MainStory",
            ChapterKind.Interlude => "Interlude",
            ChapterKind.Simulation => "Simulation",
            ChapterKind.EventActivity => "EventActivity",
            ChapterKind.WorldDialog => "WorldDialog",
            ChapterKind.Minecraft => "Minecraft",
            _ => "Other"
        };
    }

    private static UnrealAssetIndexTableSyncEntry CreateAssetIndexTableSyncEntry(
        string cacheFolder,
        string tableName,
        string tableAsset,
        string rowStruct,
        string valueColumnName,
        IReadOnlyList<string> assetReferences)
    {
        var csvPath = Path.Combine(cacheFolder, $"{tableName}.csv");
        WriteAssetIndexTableCsv(csvPath, valueColumnName, assetReferences);
        var hashSource = $"{tableAsset}|{rowStruct}|{valueColumnName}|{string.Join('\n', assetReferences)}";
        return new UnrealAssetIndexTableSyncEntry(csvPath, tableAsset, rowStruct, ComputeSha256Hex(hashSource));
    }

    private static void WriteAssetIndexTableCsv(string csvPath, string valueColumnName, IReadOnlyList<string> assetReferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", [EscapeCsvField("---"), EscapeCsvField(valueColumnName)]));
        for (var i = 0; i < assetReferences.Count; i++)
        {
            builder.AppendLine(string.Join(",",
            [
                EscapeCsvField(i.ToString(CultureInfo.InvariantCulture)),
                EscapeCsvField(assetReferences[i])
            ]));
        }

        File.WriteAllText(csvPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string ReadLatestLogSnippet(UnrealSyncContext context)
    {
        try
        {
            var logsFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "Logs");
            if (!Directory.Exists(logsFolder))
            {
                return string.Empty;
            }

            var latestLogPath = Directory
                .EnumerateFiles(logsFolder, "*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latestLogPath is null)
            {
                return string.Empty;
            }

            var lines = File
                .ReadLines(latestLogPath, Encoding.UTF8)
                .Where(line =>
                    line.Contains("GalExcleTools", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("LogCSVImportFactory", StringComparison.OrdinalIgnoreCase))
                .TakeLast(200)
                .ToList();
            return lines.Count == 0
                ? string.Empty
                : $"\n[Unreal 日志确认：{latestLogPath}]\n{string.Join('\n', lines)}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ToArrayLiteral(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "()" : $"({string.Join(",", values)})";
    }

    private static string ToLinearColorLiteral(string colorHex)
    {
        var fallback = Windows.UI.Color.FromArgb(255, 217, 232, 255);
        var color = ParseColor(colorHex, fallback);
        return FormattableString.Invariant(
            $"(R={color.R / 255d:0.######},G={color.G / 255d:0.######},B={color.B / 255d:0.######},A={color.A / 255d:0.######})");
    }
}
