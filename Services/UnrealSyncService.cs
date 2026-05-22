using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
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
        "Scene_Effect"
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

    public List<UnrealSyncImportGroup> BuildImportGroups(
        UnrealSyncContext context,
        IReadOnlyList<CharacterInfo> characters,
        IReadOnlyCollection<string> backgroundPaths,
        IReadOnlyCollection<string> musicPaths,
        IReadOnlyCollection<string> ambientPaths,
        IReadOnlyCollection<string> soundEffectPaths)
    {
        var groups = new List<UnrealSyncImportGroup>();
        AddImportGroup(groups, $"{context.TargetAssetRoot}/BackGround", backgroundPaths);
        AddImportGroup(groups, $"{context.TargetAssetRoot}/BGM", musicPaths);
        AddImportGroup(groups, $"{context.TargetAssetRoot}/Scene_Effect", ambientPaths.Concat(soundEffectPaths).ToList());

        foreach (var character in characters)
        {
            AddImportGroup(groups, GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Cloth), GetCharacterLayerImportPaths(character, CharacterLayerKind.Cloth));
            AddImportGroup(groups, GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Face), GetCharacterLayerImportPaths(character, CharacterLayerKind.Face));
            AddImportGroup(groups, GetLustrationLayerDestinationPath(context, character, CharacterLayerKind.Adorn), GetCharacterLayerImportPaths(character, CharacterLayerKind.Adorn));
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
            StoryTables = changePlan.StoryTables,
            AssetIndexTables = changePlan.AssetIndexTables,
            Imports = changePlan.ImportGroups,
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
            tasks = []

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

            unreal.log("GalExcleTools sync finished. Imported task count: {}".format(len(tasks)))
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public UnrealSyncResult Run(
        UnrealSyncContext context,
        UnrealSyncChangePlan changePlan,
        IReadOnlyList<CharacterFilterEntry> filters,
        IProgress<UnrealSyncProgressUpdate>? progress = null)
    {
        var savedFolder = Path.Combine(Path.GetDirectoryName(context.UnrealProjectPath)!, "Saved", "GalExcleTools");
        Directory.CreateDirectory(savedFolder);
        var manifestPath = Path.Combine(savedFolder, "gal-sync-manifest.json");
        var scriptPath = Path.Combine(savedFolder, "gal_sync_import.py");

        progress?.Report(new UnrealSyncProgressUpdate("正在写入同步清单...", 40));
        WriteManifest(context, changePlan, filters, manifestPath);
        progress?.Report(new UnrealSyncProgressUpdate("正在写入 Unreal Python 脚本...", 48));
        WritePythonScript(scriptPath, manifestPath);

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
        progress?.Report(new UnrealSyncProgressUpdate("Unreal 正在导入变动资源并保存资产...", 70));
        if (!process.WaitForExit(30 * 60 * 1000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Unreal Editor 同步超过 30 分钟，已终止进程。");
        }

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
        IReadOnlyList<UnrealStoryTableSyncEntry> allStoryTables,
        string assetIndexTableCacheFolder)
    {
        var importGroups = BuildImportGroups(
                context,
                characters,
                backgroundPaths,
                musicPaths,
                ambientPaths,
                soundEffectPaths)
            .Select(group => new UnrealSyncImportGroup(
                group.Destination,
                forceFullSync
                    ? group.Files.ToList()
                    : group.Files
                        .Where(path => SourceFileNeedsImport(context, group.Destination, path))
                        .ToList()))
            .Where(group => group.Files.Count > 0)
            .ToList();

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

        var importCount = importGroups.Sum(group => group.Files.Count);
        var storyTableCount = storyTables.Count;
        var assetIndexTableCount = assetIndexTables.Count;
        var totalChanged = importCount + storyTableCount + assetIndexTableCount + (lustrationChanged ? 1 : 0);
        var planItems = new List<string>
        {
            forceFullSync ? "全部重新同步：已忽略时间戳和同步缓存" : "同步模式：仅同步检测到的变动",
            $"变动素材文件：{importCount} 个",
            $"变动剧情 CSV/DataTable：{storyTableCount} 个",
            assetIndexTablesChanged
                ? $"素材索引表：需要更新 {assetIndexTableCount} 张 DataTable"
                : "素材索引表：无变动",
            lustrationChanged
                ? $"立绘数据资产：需要更新 {lustrationRows.Count} 个角色映射"
                : "立绘数据资产：无变动"
        };

        foreach (var group in importGroups)
        {
            planItems.Add($"{group.Destination}：{group.Files.Count} 个文件待导入");
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
            storyTables,
            assetIndexTables,
            lustrationChanged,
            lustrationRows,
            lustrationHash,
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

    public static string BuildTextureReference(string destinationPath, string sourcePath)
    {
        var assetName = SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(sourcePath));
        return $"Texture2D'{destinationPath}/{assetName}.{assetName}'";
    }

    public static string BuildSoundWaveReference(string destinationPath, string sourcePath)
    {
        var assetName = SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(sourcePath));
        return $"SoundWave'{destinationPath}/{assetName}.{assetName}'";
    }

    public static string BuildAssetObjectPath(string destinationPath, string sourcePath)
    {
        var assetName = SanitizeUnrealAssetName(Path.GetFileNameWithoutExtension(sourcePath));
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

    private static bool SourceFileNeedsImport(UnrealSyncContext context, string destinationPath, string sourcePath)
    {
        var objectPath = BuildAssetObjectPath(destinationPath, sourcePath);
        return SourceFileNeedsAssetUpdate(context, sourcePath, objectPath);
    }

    private static bool SourceFileNeedsAssetUpdate(UnrealSyncContext context, string sourcePath, string objectPath)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        var assetFilePath = AssetObjectPathToFilePath(context, objectPath);
        if (!File.Exists(assetFilePath))
        {
            return true;
        }

        return File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(assetFilePath).AddSeconds(1);
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
