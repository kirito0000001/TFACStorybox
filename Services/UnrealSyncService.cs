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
        IReadOnlyCollection<string> backgroundPaths,
        IReadOnlyCollection<string> musicPaths,
        IReadOnlyCollection<string> ambientPaths,
        IReadOnlyCollection<string> soundEffectPaths,
        IReadOnlyCollection<string> voicePaths)
    {
        var groups = new List<UnrealSyncImportGroup>();
        AddImportGroup(groups, UnrealSyncPathPolicy.BackgroundFolder, backgroundPaths);
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
            tasks = []
            errors = []
            warnings = []
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
                    "Warnings": warnings
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

            def delete_asset_if_exists(asset_path):
                if not asset_path:
                    return False
                try:
                    if not unreal.EditorAssetLibrary.does_asset_exist(asset_path):
                        return False
                    if unreal.EditorAssetLibrary.delete_asset(asset_path):
                        unreal.log("GalExcleTools deleted extra asset: {}".format(asset_path))
                        return True
                    add_error("GalExcleTools failed to delete extra asset: {}".format(asset_path))
                except Exception as exc:
                    add_error("GalExcleTools failed to delete extra asset {}: {}".format(asset_path, exc))
                return False

            write_progress("PrepareImports", "正在准备素材导入任务", 62)
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
                    task = unreal.AssetImportTask()
                    task.set_editor_property("filename", filename)
                    task.set_editor_property("destination_path", destination)
                    task.set_editor_property("automated", True)
                    task.set_editor_property("replace_existing", True)
                    try:
                        task.set_editor_property("save", True)
                    except Exception:
                        pass
                    tasks.append(task)

            write_progress("PrepareImports", "素材导入任务准备完成", 64, len(tasks), len(tasks))
            if tasks:
                asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
                for task_index, task in enumerate(tasks):
                    try:
                        asset_tools.import_asset_tasks([task])
                    except Exception as exc:
                        add_error("GalExcleTools failed to import source {}: {}".format(task.get_editor_property("filename"), exc))
                        write_progress("ImportAssets", "正在导入并保存素材", 65 + 15.0 * (task_index + 1) / len(tasks), task_index + 1, len(tasks))
                        continue
                    imported_paths = []
                    try:
                        imported_paths.extend(task.get_editor_property("imported_object_paths") or [])
                    except Exception:
                        pass
                    if not imported_paths:
                        add_error("GalExcleTools import produced no asset: {}".format(task.get_editor_property("filename")))
                    else:
                        imported_count += 1
                    for imported_path in imported_paths:
                        save_asset_path_with_retry(imported_path)
                    write_progress("ImportAssets", "正在导入并保存素材", 65 + 15.0 * (task_index + 1) / len(tasks), task_index + 1, len(tasks))
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
                            matched_value = str(existing_value)
                            break
                    if matched_value is None:
                        add_error("GalExcleTools AllRoad verification missed source table: {}".format(source_path))
                        return False
                    routes = source.get("Routes", [])
                    expected_route_count = len(routes)
                    actual_route_count = matched_value.count("TargetStory=")
                    if actual_route_count != expected_route_count:
                        add_error("GalExcleTools AllRoad route count mismatch for {}: expected={} actual={}".format(
                            source_path, expected_route_count, actual_route_count))
                        return False
                    for route in routes:
                        target_path = normalize_data_table_path(route.get("TargetTable"))
                        option_key = str(route.get("OptionKey") or "")
                        has_expected_condition = (not route.get("RequiresChoiceCondition") or
                                                  'TargetValue="{}"'.format(option_key) in matched_value)
                        if target_path not in matched_value or not has_expected_condition:
                            add_error("GalExcleTools AllRoad verification missed route {} -> {}".format(option_key, target_path))
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
                        for source in sources:
                            manager_row = find_manager_row(rows, source.get("ManagerRow"))
                            story_map = manager_row.get("StoryOnSetTables") or {}
                            source_path = normalize_data_table_path(source.get("SourceTable"))
                            for existing_key in list(story_map.keys()):
                                if normalize_data_table_path(existing_key) == source_path:
                                    del story_map[existing_key]
                            story_map[data_table_reference(source.get("SourceTable"))] = build_story_branch_value(source.get("Routes", []))
                            manager_row["StoryOnSetTables"] = story_map
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

            pending_deletes = []
            for group in deletes:
                pending_deletes.extend(group.get("Assets", []) or [])
            pending_deletes.extend(legacy_assets_to_delete)
            write_progress("DeleteAssets", "正在清理虚幻中的多余素材", 96, 0, len(pending_deletes))
            for delete_index, asset_path in enumerate(pending_deletes):
                if delete_asset_if_exists(asset_path):
                    deleted_count += 1
                write_progress("DeleteAssets", "正在清理虚幻中的多余素材", 96 + (3.0 * (delete_index + 1) / max(len(pending_deletes), 1)), delete_index + 1, len(pending_deletes))

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
                        return str(class_path.get_editor_property("asset_name"))
                    except Exception:
                        text = str(class_path)
                        marker = text.rfind('"')
                        start = text.rfind('"', 0, marker)
                        return text[start + 1:marker] if start >= 0 and marker > start else text
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
        offlineStartInfo.ArgumentList.Add($"-ExecutePythonScript={scriptPath}");
        offlineStartInfo.ArgumentList.Add("-unattended");
        offlineStartInfo.ArgumentList.Add("-nop4");
        offlineStartInfo.ArgumentList.Add("-nosplash");

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
                verdict: result => UnrealSyncProcessRunner.IsFreshOutput(resultPath, result.StartedAtUtc)
                    ? null
                    : "Unreal 没有写出这一轮的同步结果（结果文件缺失，或仍是上一轮留下的）。",
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
        IReadOnlyCollection<string> backgroundPaths,
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
        var allImportGroups = BuildImportGroups(
                context,
                characters,
                backgroundPaths,
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
            backgroundPaths,
            musicPaths,
            ambientPaths,
            soundEffectPaths,
            voicePaths,
            allStoryTables,
            getPortraitPreviewPathsByLayerFileName);

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

        var tablesBySection = chapterTables.ToDictionary(table => table.Section);
        return StoryBranchFlowService.Build(document).Sections
            .Where(section => section.Kind != StoryBranchSectionKind.Custom)
            .Select(section => new UnrealStoryBranchSourceSyncEntry(
                managerRow,
                tablesBySection[section.Section].TableAsset,
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
                        : []))
            .ToList();
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

    public void WriteState(UnrealSyncContext context, UnrealSyncChangePlan changePlan)
    {
        var statePath = GetUnrealSyncStatePath(context);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var state = ReadState(context);
        state.StateVersion = CurrentUnrealSyncStateVersion;
        state.LastSyncedAt = DateTimeOffset.Now;
        state.LustrationHash = changePlan.LustrationHash;
        state.PortraitsHash = changePlan.PortraitsHash;
        state.AssetIndexTablesHash = changePlan.AssetIndexTablesHash;
        state.VoiceMapsHash = changePlan.VoiceMapsHash;
        state.StoryBranchesHash = changePlan.StoryBranchesHash;
        var storyBranchAssetPath = UnrealObjectPathToContentFilePath(context, StoryBranchDataTableAssetPath);
        state.StoryBranchesAssetHash = File.Exists(storyBranchAssetPath)
            ? ComputeFileHash(storyBranchAssetPath)
            : string.Empty;
        state.SourceHashes = new Dictionary<string, string>(changePlan.SourceHashes, StringComparer.OrdinalIgnoreCase);
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

    private List<UnrealSyncDeleteGroup> BuildDeleteGroups(
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters,
        IReadOnlyCollection<string> backgroundPaths,
        IReadOnlyCollection<string> musicPaths,
        IReadOnlyCollection<string> ambientPaths,
        IReadOnlyCollection<string> soundEffectPaths,
        IReadOnlyCollection<string> voicePaths,
        IReadOnlyList<UnrealStoryTableSyncEntry> storyTables,
        Func<CharacterInfo, IReadOnlyDictionary<string, string>> getPortraitPreviewPathsByLayerFileName)
    {
        var groups = new List<UnrealSyncDeleteGroup>();
        AddDeleteGroup(groups, context, UnrealSyncPathPolicy.BackgroundFolder, backgroundPaths.Select(GetExpectedImportedAssetName));
        AddDeleteGroup(groups, context, UnrealSyncPathPolicy.BgmFolder, musicPaths.Select(GetExpectedImportedAssetName));
        AddDeleteGroup(groups, context, UnrealSyncPathPolicy.SceneEffectFolder, ambientPaths.Concat(soundEffectPaths).Select(GetExpectedImportedAssetName));
        AddProjectVoiceDeleteGroups(groups, context, voicePaths);

        var expectedExcelTextAssets = storyTables
            .Select(entry => entry.TableAsset)
            .Concat(new[]
            {
                UnrealSyncPathPolicy.EffectIndexTableAsset,
                // ⚠️ 剧情选项分支表**必须列进来**：这个删除组是 recursive 的，
                // 凡是 ExcelTexts 下不在期望集合里的资产都会被删。
                // 而 WHK 里 AllRoad.uasset 恰好就放在 ExcelTexts 下
                // （工具箱的常量指向 AssetMaterial/AllRoad —— 两处路径目前对不上，
                //  这一条是保底，避免同步顺手把它删掉）。
                StoryBranchDataTableAssetPath
            });
        AddDeleteGroupByExpectedAssets(groups, context, UnrealSyncPathPolicy.ExcelTextsRoot, expectedExcelTextAssets, recursive: true);

        var expectedLustrationRootAssets = new[] { "DA_LustrationInfor" };
        if (context.AssetLibrary.IsPortraitPreviewEnabled)
        {
            expectedLustrationRootAssets = expectedLustrationRootAssets.Append("DA_Portraits").ToArray();
        }

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
            .Select(path => BuildAssetObjectPath(GetProjectVoiceDestinationPath(context, path), path));
        AddDeleteGroupByExpectedAssets(groups, context, UnrealSyncPathPolicy.VoiceFolder, expectedAssetPaths, recursive: true);
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
        foreach (var character in characters)
        {
            var destination = GetPortraitPreviewDestinationPath(context, character);
            var expectedAssetNames = getPortraitPreviewPathsByLayerFileName(character)
                .Values
                .Select(GetExpectedImportedAssetName);
            AddDeleteGroup(groups, context, destination, expectedAssetNames);
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
        bool recursive = false)
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
        return activeSections
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
