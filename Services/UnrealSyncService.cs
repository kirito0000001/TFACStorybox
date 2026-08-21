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
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools.Services;

internal sealed class UnrealSyncService
{
    private const int CurrentUnrealSyncStateVersion = 3;
    public const string MusicPlayerBlueprintAssetPath = "/Game/BaseC/EXActor/MusicPlayer.MusicPlayer";
    private const string InvalidUnrealObjectNameCharacters = "\\:*?\"<>|' ,/.&!~@#(){}[]=;^%$`";

    public static readonly string[] ExpectedNarrativeFolders =
    [
        "BackGround",
        "BGM",
        "ExcelTexts",
        "Lustration",
        "Scene_Effect",
        "Voice"
    ];

    private readonly JsonSerializerOptions _jsonOptions;
    private readonly StoryCsvService _storyCsvService;
    private readonly StoryDocumentRepository _storyDocumentRepository;

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
        AddImportGroup(groups, $"{context.TargetAssetRoot}/BackGround", backgroundPaths);
        AddImportGroup(groups, $"{context.TargetAssetRoot}/BGM", musicPaths);
        AddImportGroup(groups, $"{context.TargetAssetRoot}/Scene_Effect", ambientPaths.Concat(soundEffectPaths).ToList());
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
            GeneratedAt = DateTimeOffset.Now,
            ToolProject = new
            {
                context.Project.Name,
                context.Project.Code,
                context.Project.Path
            },
            UnrealProjectPath = context.UnrealProjectPath,
            TargetRoot = context.TargetAssetRoot,
            LustrationInfo = new
            {
                DataAsset = $"{context.TargetAssetRoot}/Lustration/DA_LustrationInfor.DA_LustrationInfor",
                MapProperty = "Infor",
                ShouldUpdate = changePlan.LustrationChanged,
                Rows = changePlan.LustrationChanged ? changePlan.LustrationRows : []
            },
            PortraitsInfo = new
            {
                DataAsset = $"{context.TargetAssetRoot}/Lustration/DA_Portraits.DA_Portraits",
                MapProperty = "Infor",
                ShouldUpdate = changePlan.PortraitsChanged,
                Rows = changePlan.PortraitsChanged ? changePlan.PortraitRows : []
            },
            StoryTables = changePlan.StoryTables,
            AssetIndexTables = changePlan.AssetIndexTables,
            VoiceMap = new
            {
                BlueprintAsset = MusicPlayerBlueprintAssetPath,
                MapProperty = "VoiceMaps",
                ManagedTableRoot = $"{context.TargetAssetRoot}/ExcelTexts/",
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

            write_result(True)
            write_progress("Completed", "Unreal 同步已完成" if not errors else "Unreal 同步完成，但存在失败项", 100)
            unreal.log("GalExcleTools sync finished. Imported task count: {}, deleted extra asset count: {}".format(len(tasks), deleted_count))
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public UnrealSyncResult Run(
        UnrealSyncContext context,
        UnrealSyncChangePlan changePlan,
        IReadOnlyList<CharacterFilterEntry> filters,
        IProgress<UnrealSyncProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var savedFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "GalExcleTools");
        Directory.CreateDirectory(savedFolder);
        var manifestPath = Path.Combine(savedFolder, "gal-sync-manifest.json");
        var scriptPath = Path.Combine(savedFolder, "gal_sync_import.py");
        var progressPath = Path.Combine(savedFolder, "gal-sync-progress.json");
        var resultPath = Path.Combine(savedFolder, "gal-sync-result.json");
        File.Delete(progressPath);
        File.Delete(resultPath);

        progress?.Report(new UnrealSyncProgressUpdate("正在写入同步清单...", 40));
        WriteManifest(context, changePlan, filters, manifestPath, progressPath, resultPath);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new UnrealSyncProgressUpdate("正在写入 Unreal Python 脚本...", 48));
        WritePythonScript(scriptPath, manifestPath);
        cancellationToken.ThrowIfCancellationRequested();

        var processStartInfo = new ProcessStartInfo
        {
            FileName = context.EditorPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        processStartInfo.ArgumentList.Add(context.UnrealProjectPath);
        processStartInfo.ArgumentList.Add($"-ExecutePythonScript={scriptPath}");
        processStartInfo.ArgumentList.Add("-unattended");
        processStartInfo.ArgumentList.Add("-nop4");
        processStartInfo.ArgumentList.Add("-nosplash");

        progress?.Report(new UnrealSyncProgressUpdate("正在启动 Unreal Editor 命令进程...", 55));
        using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("无法启动 Unreal Editor。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        progress?.Report(new UnrealSyncProgressUpdate("Unreal 已启动，正在加载项目并执行导入脚本...", 60));

        var waitStartedAt = DateTime.UtcNow;
        string? lastProgressJson = null;
        while (!process.WaitForExit(1000))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                KillUnrealSyncProcess(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var elapsed = DateTime.UtcNow - waitStartedAt;
            if (elapsed >= TimeSpan.FromMinutes(30))
            {
                KillUnrealSyncProcess(process);
                throw new TimeoutException("Unreal Editor 同步超过 30 分钟，已终止进程。");
            }

            var progressUpdate = ReadProgressUpdate(progressPath, ref lastProgressJson);
            if (progressUpdate is not null)
            {
                progress?.Report(progressUpdate);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new UnrealSyncProgressUpdate("正在收集 Unreal 同步结果...", 95));
        var output = outputTask.Result + errorTask.Result + ReadLatestLogSnippet(context);
        var report = ReadExecutionReport(resultPath);
        return new UnrealSyncResult(process.ExitCode, manifestPath, scriptPath, progressPath, resultPath, output, report);
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
        var lustrationAssetPath = $"{context.TargetAssetRoot}/Lustration/DA_LustrationInfor.DA_LustrationInfor";
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
        var portraitsAssetPath = $"{context.TargetAssetRoot}/Lustration/DA_Portraits.DA_Portraits";
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
        var totalChanged = importCount + deleteCount + storyTableCount + assetIndexTableCount + (voiceMapsChanged ? 1 : 0) + (lustrationChanged ? 1 : 0) + (portraitsChanged ? 1 : 0);
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

        return new UnrealSyncChangePlan(
            importGroups,
            deleteGroups,
            storyTables,
            assetIndexTables,
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

        return
        [
            CreateAssetIndexTableSyncEntry(
                cacheFolder,
                "BGIndexMap",
                $"{context.TargetAssetRoot}/ExcelTexts/BGIndexMap.BGIndexMap",
                "/Script/GALLibrary.Texture2DTable",
                "Texture2D",
                backgroundPaths
                    .Select(path => BuildTextureReference($"{context.TargetAssetRoot}/BackGround", path))
                    .ToList()),
            CreateAssetIndexTableSyncEntry(
                cacheFolder,
                "BGMap",
                $"{context.TargetAssetRoot}/ExcelTexts/BGMap.BGMap",
                "/Script/GALLibrary.WaveTable",
                "Wave",
                musicPaths
                    .Select(path => BuildSoundWaveReference($"{context.TargetAssetRoot}/BGM", path))
                    .ToList()),
            CreateAssetIndexTableSyncEntry(
                cacheFolder,
                "SceneIndexMap",
                $"{context.TargetAssetRoot}/ExcelTexts/SceneIndexMap.SceneIndexMap",
                "/Script/GALLibrary.WaveTable",
                "Wave",
                ambientPaths
                    .Select(path => BuildSoundWaveReference($"{context.TargetAssetRoot}/Scene_Effect", path))
                    .ToList()),
            CreateAssetIndexTableSyncEntry(
                cacheFolder,
                "ExsIndexMap",
                $"{context.TargetAssetRoot}/ExcelTexts/ExsIndexMap.ExsIndexMap",
                "/Script/GALLibrary.WaveTable",
                "Wave",
                soundEffectPaths
                    .Select(path => BuildSoundWaveReference($"{context.TargetAssetRoot}/Scene_Effect", path))
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
        var folderName = layerKind == CharacterLayerKind.Cloth
            ? "DN_Cloths"
            : CharacterLayerAssetService.GetFolderName(layerKind);
        return $"{context.TargetAssetRoot}/Lustration/{character.Code}/{folderName}";
    }

    public static string GetPortraitPreviewDestinationPath(UnrealSyncContext context, CharacterInfo character)
    {
        return $"{context.TargetAssetRoot}/Lustration/{character.Code}/Log_Preview";
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
            return $"{context.TargetAssetRoot}/Voice";
        }

        var segments = relativeFolder
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        if (segments.Length == 0)
        {
            return $"{context.TargetAssetRoot}/Voice";
        }

        segments[0] = SanitizeUnrealAssetName(StoryCsvService.RemoveChapterSectionSuffix(segments[0]));
        return $"{context.TargetAssetRoot}/Voice/{string.Join("/", segments)}";
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
        var targetRoot = context.TargetAssetRoot.Trim('/');
        var relativePath = packagePath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)
            ? packagePath[targetRoot.Length..].Trim('/')
            : packagePath;

        return Path.Combine(
            context.TargetContentFolderPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar) + ".uasset");
    }

    private static string AssetFolderPathToFilePath(UnrealSyncContext context, string folderPath)
    {
        var targetRoot = context.TargetAssetRoot.Trim('/');
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
        return $"{context.TargetAssetRoot.TrimEnd('/')}/{packagePath}.{assetName}";
    }

    private static string BuildAssetFolderPathFromFolder(UnrealSyncContext context, string folderPath)
    {
        var relativePath = Path.GetRelativePath(context.TargetContentFolderPath, folderPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return context.TargetAssetRoot;
        }

        return $"{context.TargetAssetRoot.TrimEnd('/')}/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
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
        AddDeleteGroup(groups, context, $"{context.TargetAssetRoot}/BackGround", backgroundPaths.Select(GetExpectedImportedAssetName));
        AddDeleteGroup(groups, context, $"{context.TargetAssetRoot}/BGM", musicPaths.Select(GetExpectedImportedAssetName));
        AddDeleteGroup(groups, context, $"{context.TargetAssetRoot}/Scene_Effect", ambientPaths.Concat(soundEffectPaths).Select(GetExpectedImportedAssetName));
        AddProjectVoiceDeleteGroups(groups, context, voicePaths);

        var expectedExcelTextAssets = storyTables
            .Select(entry => entry.TableAsset)
            .Concat(new[]
            {
                $"{context.TargetAssetRoot}/ExcelTexts/BGIndexMap.BGIndexMap",
                $"{context.TargetAssetRoot}/ExcelTexts/BGMap.BGMap",
                $"{context.TargetAssetRoot}/ExcelTexts/SceneIndexMap.SceneIndexMap",
                $"{context.TargetAssetRoot}/ExcelTexts/ExsIndexMap.ExsIndexMap"
            });
        AddDeleteGroupByExpectedAssets(groups, context, $"{context.TargetAssetRoot}/ExcelTexts", expectedExcelTextAssets, recursive: true);

        var expectedLustrationRootAssets = new[] { "DA_LustrationInfor" };
        if (context.AssetLibrary.IsPortraitPreviewEnabled)
        {
            expectedLustrationRootAssets = expectedLustrationRootAssets.Append("DA_Portraits").ToArray();
        }

        AddDeleteGroup(groups, context, $"{context.TargetAssetRoot}/Lustration", expectedLustrationRootAssets);
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
        AddDeleteGroupByExpectedAssets(groups, context, $"{context.TargetAssetRoot}/Voice", expectedAssetPaths, recursive: true);
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

        var lustrationFolderPath = AssetFolderPathToFilePath(context, $"{context.TargetAssetRoot}/Lustration");
        if (!Directory.Exists(lustrationFolderPath))
        {
            return;
        }

        var managedLayerFolders = new HashSet<string>(
            new[] { "DN_Cloths", CharacterLayerAssetService.GetFolderName(CharacterLayerKind.Face), CharacterLayerAssetService.GetFolderName(CharacterLayerKind.Adorn) },
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

        var lustrationFolderPath = AssetFolderPathToFilePath(context, $"{context.TargetAssetRoot}/Lustration");
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
        var targetRoot = context.TargetAssetRoot.Trim('/');
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
        var folder = $"{context.TargetAssetRoot}/ExcelTexts/{categoryFolder}";
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
        var rootFolder = $"{context.TargetAssetRoot}/ExcelTexts/{categoryFolder}";
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
