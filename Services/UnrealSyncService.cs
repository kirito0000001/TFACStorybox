using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
                    BuildLegacyStoryTableAssets(context, source.Chapter, entry.CsvPath, tableFolder, assetName)));
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
            .Select(chapter => new UnrealStoryTableSource(chapter, GetChapterStoryCsvPathsForSync(context.Project, chapter)))
            .Where(source => source.CsvEntries.Count > 0)
            .ToList();
        return BuildStoryTableSyncEntries(context, sources);
    }

    public List<string> GetProjectStoryCsvPaths(ProjectInfo project, IReadOnlyList<ChapterInfo> chapters)
    {
        return chapters
            .SelectMany(chapter => GetChapterStoryCsvPathsForSync(project, chapter).Select(entry => entry.CsvPath))
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
        string manifestPath)
    {
        var manifest = new
        {
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
            import unreal

            manifest_path = r"{{normalizedManifestPath}}"
            with open(manifest_path, "r", encoding="utf-8-sig") as manifest_file:
                manifest = json.load(manifest_file)

            target_root = manifest.get("TargetRoot", "/Game")
            imports = manifest.get("Imports", [])
            deletes = manifest.get("Deletes", [])
            tasks = []

            def delete_asset_if_exists(asset_path):
                if not asset_path:
                    return False
                try:
                    if not unreal.EditorAssetLibrary.does_asset_exist(asset_path):
                        return False
                    if unreal.EditorAssetLibrary.delete_asset(asset_path):
                        unreal.log("GalExcleTools deleted extra asset: {}".format(asset_path))
                        return True
                    unreal.log_warning("GalExcleTools failed to delete extra asset: {}".format(asset_path))
                except Exception as exc:
                    unreal.log_warning("GalExcleTools failed to delete extra asset {}: {}".format(asset_path, exc))
                return False

            deleted_count = 0
            for group in deletes:
                destination = group.get("Destination")
                assets = group.get("Assets", [])
                if not destination:
                    continue
                for asset_path in assets:
                    if delete_asset_if_exists(asset_path):
                        deleted_count += 1

            for group in imports:
                destination = group.get("Destination")
                files = group.get("Files", [])
                if not destination:
                    continue
                unreal.EditorAssetLibrary.make_directory(destination)
                for filename in files:
                    if not filename or not os.path.exists(filename):
                        unreal.log_warning("GalExcleTools missing source file: {}".format(filename))
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

            if tasks:
                unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks(tasks)
                imported_paths = []
                for task in tasks:
                    try:
                        imported_paths.extend(task.get_editor_property("imported_object_paths") or [])
                    except Exception:
                        pass
                for imported_path in imported_paths:
                    try:
                        unreal.EditorAssetLibrary.save_asset(imported_path, only_if_is_dirty=False)
                    except Exception as exc:
                        unreal.log_warning("GalExcleTools failed to save imported asset {}: {}".format(imported_path, exc))

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
            for story_table in story_tables:
                table_asset_path = story_table.get("TableAsset")
                csv_path = story_table.get("CsvPath")
                row_struct_path = story_table.get("RowStruct")
                if not table_asset_path or not csv_path:
                    continue
                for legacy_asset_path in story_table.get("LegacyTableAssets", []) or []:
                    if legacy_asset_path and legacy_asset_path != table_asset_path and unreal.EditorAssetLibrary.does_asset_exist(legacy_asset_path):
                        if unreal.EditorAssetLibrary.delete_asset(legacy_asset_path):
                            unreal.log("GalExcleTools deleted legacy story data table: {}".format(legacy_asset_path))
                        else:
                            unreal.log_warning("GalExcleTools failed to delete legacy story data table: {}".format(legacy_asset_path))
                data_table, row_struct = ensure_story_data_table(table_asset_path, row_struct_path)
                if data_table:
                    try:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path, row_struct)
                    except TypeError:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path)
                    if ok:
                        unreal.EditorAssetLibrary.save_asset(table_asset_path, only_if_is_dirty=False)
                        unreal.log("GalExcleTools updated story data table: {}".format(table_asset_path))
                    else:
                        unreal.log_warning("GalExcleTools failed to update story data table: {}".format(table_asset_path))
                else:
                    unreal.log_warning("GalExcleTools could not create or load story data table: {}".format(table_asset_path))

            asset_index_tables = manifest.get("AssetIndexTables", [])
            for index_table in asset_index_tables:
                table_asset_path = index_table.get("TableAsset")
                csv_path = index_table.get("CsvPath")
                row_struct_path = index_table.get("RowStruct")
                if not table_asset_path or not csv_path:
                    continue
                data_table, row_struct = ensure_story_data_table(table_asset_path, row_struct_path)
                if data_table:
                    try:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path, row_struct)
                    except TypeError:
                        ok = unreal.DataTableFunctionLibrary.fill_data_table_from_csv_file(data_table, csv_path)
                    if ok:
                        unreal.EditorAssetLibrary.save_asset(table_asset_path, only_if_is_dirty=False)
                        unreal.log("GalExcleTools updated asset index data table: {}".format(table_asset_path))
                    else:
                        unreal.log_warning("GalExcleTools failed to update asset index data table: {}".format(table_asset_path))
                else:
                    unreal.log_warning("GalExcleTools could not create or load asset index data table: {}".format(table_asset_path))

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
                        unreal.EditorAssetLibrary.save_asset(data_asset_path, only_if_is_dirty=False)
                        unreal.log("GalExcleTools updated lustration data asset: {} rows={}".format(data_asset_path, len(lustration_map)))
                    else:
                        unreal.log_warning("GalExcleTools could not set lustration map property '{}' on {}".format(map_property, data_asset_path))
                else:
                    unreal.log_warning("GalExcleTools could not load lustration data asset: {}".format(data_asset_path))

            portraits_info = manifest.get("PortraitsInfo", {})
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
                        unreal.EditorAssetLibrary.save_asset(portraits_data_asset_path, only_if_is_dirty=False)
                        unreal.log("GalExcleTools updated portrait data asset: {} rows={}".format(portraits_data_asset_path, len(portraits_map)))
                    else:
                        unreal.log_warning("GalExcleTools could not set portrait map property '{}' on {}".format(portraits_map_property, portraits_data_asset_path))
                else:
                    unreal.log_warning("GalExcleTools could not load portrait data asset: {}".format(portraits_data_asset_path))

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

        progress?.Report(new UnrealSyncProgressUpdate("正在写入同步清单...", 40));
        WriteManifest(context, changePlan, filters, manifestPath);
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
        var lastProgressReportAt = DateTime.MinValue;
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

            if ((DateTime.UtcNow - lastProgressReportAt).TotalSeconds >= 10)
            {
                lastProgressReportAt = DateTime.UtcNow;
                var percent = Math.Min(90, 60 + elapsed.TotalSeconds / 180d * 25d);
                progress?.Report(new UnrealSyncProgressUpdate(
                    "Unreal 正在加载项目、导入资源并保存资产...",
                    percent));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new UnrealSyncProgressUpdate("正在收集 Unreal 同步结果...", 95));
        var output = outputTask.Result + errorTask.Result + ReadLatestLogSnippet(context);
        return new UnrealSyncResult(process.ExitCode, manifestPath, scriptPath, output);
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
        IReadOnlyList<UnrealStoryTableSyncEntry> allStoryTables,
        string assetIndexTableCacheFolder,
        Func<CharacterInfo, IReadOnlyDictionary<string, string>> getPortraitPreviewPathsByLayerFileName)
    {
        var importGroups = BuildImportGroups(
                context,
                characters,
                backgroundPaths,
                musicPaths,
                ambientPaths,
                soundEffectPaths,
                voicePaths)
            .Select(group => new UnrealSyncImportGroup(
                group.Destination,
                forceFullSync
                    ? group.Files.ToList()
                    : group.Files
                        .Where(path => SourceFileNeedsImport(context, group.Destination, path))
                        .ToList()))
            .ToList();

        var portraitPreviewImportGroups = context.AssetLibrary.IsPortraitPreviewEnabled
            ? BuildPortraitPreviewImportGroups(context, characters, getPortraitPreviewPathsByLayerFileName)
                .Select(group => new UnrealSyncImportGroup(
                    group.Destination,
                    forceFullSync
                        ? group.Files.ToList()
                        : group.Files
                            .Where(path => SourceFileNeedsImport(context, group.Destination, path))
                            .ToList()))
                .ToList()
            : [];

        importGroups = importGroups
            .Concat(portraitPreviewImportGroups)
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

        var storyTables = forceFullSync
            ? allStoryTables.ToList()
            : allStoryTables
                .Where(entry => SourceFileNeedsAssetUpdate(context, entry.CsvPath, entry.TableAsset))
                .ToList();

        var syncState = ReadState(context);
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
            !string.Equals(syncState.AssetIndexTablesHash, assetIndexTablesHash, StringComparison.OrdinalIgnoreCase) ||
            allAssetIndexTables.Any(entry => !File.Exists(AssetObjectPathToFilePath(context, entry.TableAsset)));
        var assetIndexTables = assetIndexTablesChanged ? allAssetIndexTables : [];

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
        var totalChanged = importCount + deleteCount + storyTableCount + assetIndexTableCount + (lustrationChanged ? 1 : 0) + (portraitsChanged ? 1 : 0);
        var planItems = new List<string>
        {
            forceFullSync ? "全部重新同步：已忽略时间戳和同步缓存" : "同步模式：仅同步检测到的变动",
            $"变动素材文件：{importCount} 个",
            deleteCount > 0
                ? $"虚幻多余素材：{deleteCount} 个将删除"
                : "虚幻多余素材：无",
            $"变动剧情 CSV/DataTable：{storyTableCount} 个",
            assetIndexTablesChanged
                ? $"素材索引表：需要更新 {assetIndexTableCount} 张 DataTable"
                : "素材索引表：无变动",
            lustrationChanged
                ? $"立绘数据资产：需要更新 {lustrationRows.Count} 个角色映射"
                : "立绘数据资产：无变动",
            portraitsChanged
                ? $"小预览数据资产：需要更新 {portraitRows.Count} 个角色映射"
                : context.AssetLibrary.IsPortraitPreviewEnabled ? "小预览数据资产：无变动" : "小预览数据资产：未启用"
        };

        foreach (var group in importGroups)
        {
            planItems.Add($"{group.Destination}：{group.Files.Count} 个文件待导入");
        }

        foreach (var group in deleteGroups)
        {
            planItems.Add($"{group.Destination}：{group.Assets.Count} 个多余资产待删除");
        }

        if (storyTableCount > 0)
        {
            planItems.Add($"ExcelTexts：{storyTableCount} 个剧情表待填入");
        }

        if (assetIndexTableCount > 0)
        {
            planItems.Add($"ExcelTexts：{assetIndexTableCount} 张素材索引表待填入");
        }

        var summary = totalChanged == 0
            ? "没有检测到需要同步的变动。"
            : forceFullSync
                ? $"已准备全部重新同步，共 {totalChanged} 项。"
                : $"检测到 {totalChanged} 项同步变动；本次只会同步这些变动项。";

        return new UnrealSyncChangePlan(
            importGroups,
            deleteGroups,
            storyTables,
            assetIndexTables,
            lustrationChanged,
            lustrationRows,
            portraitsChanged,
            portraitRows,
            lustrationHash,
            portraitsHash,
            assetIndexTablesHash,
            totalChanged,
            summary,
            planItems);
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
        var assetName = Path.GetFileNameWithoutExtension(sourcePath);
        return $"Texture2D'{destinationPath}/{assetName}.{assetName}'";
    }

    public static string BuildSoundWaveReference(string destinationPath, string sourcePath)
    {
        var assetName = Path.GetFileNameWithoutExtension(sourcePath);
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
        var assetName = Path.GetFileNameWithoutExtension(sourcePath);
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
            return JsonSerializer.Deserialize<UnrealSyncState>(File.ReadAllText(path)) ?? new UnrealSyncState();
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
        state.LastSyncedAt = DateTimeOffset.Now;
        state.LustrationHash = changePlan.LustrationHash;
        state.PortraitsHash = changePlan.PortraitsHash;
        state.AssetIndexTablesHash = changePlan.AssetIndexTablesHash;
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

    private static bool SourceFileNeedsImport(UnrealSyncContext context, string destinationPath, string sourcePath)
    {
        var objectPath = BuildAssetObjectPath(destinationPath, sourcePath);
        var assetFilePath = AssetObjectPathToFilePath(context, objectPath);
        var alternativeAssetFilePath = BuildImportedAssetFilePath(context, destinationPath, sourcePath);
        return SourceFileNeedsAnyAssetUpdate(context, sourcePath, assetFilePath, alternativeAssetFilePath);
    }

    private static bool SourceFileNeedsAssetUpdate(UnrealSyncContext context, string sourcePath, string objectPath)
    {
        return SourceFileNeedsAnyAssetUpdate(context, sourcePath, AssetObjectPathToFilePath(context, objectPath));
    }

    private static bool SourceFileNeedsAnyAssetUpdate(UnrealSyncContext context, string sourcePath, params string[] assetFilePaths)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        var existingAssetPath = assetFilePaths.FirstOrDefault(File.Exists);
        if (existingAssetPath is null)
        {
            return true;
        }

        return File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(existingAssetPath).AddSeconds(1);
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
        return assetName.Replace(' ', '_');
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

    private List<StoryTableCsvEntry> GetChapterStoryCsvPathsForSync(ProjectInfo project, ChapterInfo chapter)
    {
        CleanupStorySectionCache(project, chapter);
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
            if (!rows.Any(_storyCsvService.RowHasContent))
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

    private static string GetStorySectionCacheFolder(ProjectInfo project, ChapterInfo chapter)
    {
        return Path.Combine(
            project.Path,
            "Tools",
            "UnrealStorySections",
            StoryCsvService.RemoveChapterSectionSuffix(chapter.Code));
    }

    private static void CleanupStorySectionCache(ProjectInfo project, ChapterInfo chapter)
    {
        var folder = GetStorySectionCacheFolder(project, chapter);
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var csvPath in Directory.EnumerateFiles(folder, "*.csv", SearchOption.TopDirectoryOnly))
        {
            File.Delete(csvPath);
        }
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
