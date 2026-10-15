using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Text.Json;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools.Services;

internal sealed class FolderBackupService
{
    private const int MaxFolderBackupCount = 3;

    /// <summary>章节备份目录名。公开出来是因为"合并章节包前"也要往这里备份一次（见 ChapterPackageService）。</summary>
    public const string ChapterBackupsFolderName = "ChapterBackups";

    private const string ProjectBackupsFolderName = "ProjectBackups";
    private const string AssetLibraryBackupsFolderName = "AssetLibraryBackups";
    private const string UnrealBackupsFolderName = "UnrealBackups";

    /// <summary>
    /// **导出**（发给别人 / 存档）时额外要跳过的目录名 —— 比备份多跳这一批"本机工件"：
    /// 同步中间产物（<c>UnrealAssetIndexTables</c> / <c>UnrealSync</c>）、
    /// AI 填表的逐次备份（<c>AIStoryFillBackups</c>）、
    /// CSV 导出的事务残留（<c>.story-csv-export-transaction</c>，崩在中途才会留下）。
    /// 它们描述的是"**这台机器**上做到哪一步了"，发给对方只会让对方以为已经同步过。
    /// 2026-10-15 交接协作者时加的。
    ///
    /// ⚠️ 备份（<see cref="CreateBackup"/>）**不**跳这批：备份要的是"完整快照"，
    /// 少了同步状态还算什么快照。所以两处的过滤是分开的。
    /// </summary>
    private static readonly string[] ExportExcludedLocalFolderNames =
    [
        "UnrealAssetIndexTables",
        "UnrealSync",
        "AIStoryFillBackups",
        ".story-csv-export-transaction"
    ];

    /// <summary>导出时要额外跳过的**文件**名（同样只描述本机状态）。</summary>
    private static readonly string[] ExportExcludedLocalFileNames =
    [
        "unreal-sync-state.json",
        "ai-story-import-state.json"
    ];

    /// <summary>导出时额外跳过的文件名前缀：<c>story.voice-map.*.json</c> 这类历史迁移备份。</summary>
    private const string ExportLocalArtifactFilePrefix = "story.voice-map.";

    /// <summary>上面那条前缀要放行的正牌文件（备注映射本身是要带走的）。</summary>
    private const string KeptVoiceMapFileName = "story.voice-map.json";

    private readonly JsonSerializerOptions _jsonOptions;

    public FolderBackupService(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public FolderBackupEntry CreateBackup(
        string folderPath,
        string backupsFolderName,
        string nameSeed,
        string note,
        IProgress<FolderBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"文件夹不存在：{folderPath}");
        }

        var backupsPath = GetFolderBackupsPath(folderPath, backupsFolderName);
        Directory.CreateDirectory(backupsPath);

        var createdAt = DateTime.Now;
        var safeName = SanitizeBackupFileName(nameSeed);
        var safeNote = SanitizeBackupFileName(note);
        var noteSuffix = string.IsNullOrWhiteSpace(safeNote) ? string.Empty : $"_{safeNote}";
        var backupPath = Path.Combine(backupsPath, $"{safeName}_{createdAt:yyyyMMdd_HHmmss}{noteSuffix}.zip");
        var duplicateIndex = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupsPath, $"{safeName}_{createdAt:yyyyMMdd_HHmmss}{noteSuffix}_{duplicateIndex}.zip");
            duplicateIndex++;
        }

        progress?.Report(new FolderBackupProgress("正在扫描要写入备份的文件...", 0, 0, 0, 0, 0, null));
        cancellationToken.ThrowIfCancellationRequested();
        var files = EnumerateBackupFiles(folderPath, backupsPath).ToList();
        var totalBytes = files.Sum(filePath => new FileInfo(filePath).Length);

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            long completedBytes = 0;
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = files[index];
                var fileLength = new FileInfo(filePath).Length;
                var relativePath = Path.GetRelativePath(folderPath, filePath).Replace('\\', '/');
                var percent = files.Count == 0
                    ? 90
                    : Math.Min(90, Math.Max(1, completedBytes * 90d / Math.Max(1, totalBytes)));
                progress?.Report(new FolderBackupProgress(
                    $"正在压缩 {index + 1}/{files.Count}：{relativePath}",
                    percent,
                    index,
                    files.Count,
                    completedBytes,
                    totalBytes,
                    relativePath));
                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                completedBytes += fileLength;
            }
        }

        progress?.Report(new FolderBackupProgress("正在写入备份备注...", 94, files.Count, files.Count, totalBytes, totalBytes, null));
        cancellationToken.ThrowIfCancellationRequested();
        var meta = new FolderBackupMeta
        {
            CreatedAt = createdAt,
            Note = NormalizeBackupNote(note)
        };
        File.WriteAllText(GetBackupMetaPath(backupPath), JsonSerializer.Serialize(meta, _jsonOptions));

        progress?.Report(new FolderBackupProgress("正在清理旧备份，最多保留 3 份...", 97, files.Count, files.Count, totalBytes, totalBytes, null));
        cancellationToken.ThrowIfCancellationRequested();
        PruneBackups(folderPath, backupsFolderName);
        progress?.Report(new FolderBackupProgress("备份完成。", 100, files.Count, files.Count, totalBytes, totalBytes, null));
        return BuildEntry(backupPath);
    }

    /// <summary>
    /// 把一个工作区对象（项目 / 素材库）压成可以发出去的包。
    ///
    /// <paramref name="label"/> 是进度文案里的名词（"项目" / "素材库"），**由调用方给** ——
    /// 原来这里是写死的"项目"，导出素材库时会说"正在扫描要导出的项目文件"；
    /// 后来改成从清单里推断，但没清单的调用又会退回错的词。索性让调用方直接说，
    /// 谁调用谁清楚，也就没有"猜错"这条路了。
    /// </summary>
    public FolderBackupEntry ExportToZip(
        string folderPath,
        string exportPath,
        string label,
        IProgress<FolderBackupProgress>? progress = null,
        CancellationToken cancellationToken = default,
        WorkspacePackageManifest? manifest = null)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"文件夹不存在：{folderPath}");
        }

        var exportFullPath = Path.GetFullPath(exportPath);
        var exportDirectory = Path.GetDirectoryName(exportFullPath);
        if (string.IsNullOrWhiteSpace(exportDirectory))
        {
            throw new IOException("导出路径无效。");
        }

        Directory.CreateDirectory(exportDirectory);
        if (File.Exists(exportFullPath))
        {
            File.Delete(exportFullPath);
        }

        var backupsPath = GetFolderBackupsPath(folderPath, ProjectBackupsFolderName);
        var noun = string.IsNullOrWhiteSpace(label) ? "内容" : label.Trim();
        progress?.Report(new FolderBackupProgress($"正在扫描要导出的{noun}文件...", 0, 0, 0, 0, 0, null));
        cancellationToken.ThrowIfCancellationRequested();
        var files = EnumerateBackupFiles(folderPath, backupsPath, excludeLocalArtifacts: true)
            .Where(filePath => !PathsEqual(filePath, exportFullPath))
            .ToList();
        var totalBytes = files.Sum(filePath => new FileInfo(filePath).Length);

        // 清单放在**第一个**条目：读取方扫一下条目表就能拿到"这是什么包"。
        if (manifest is not null)
        {
            manifest.FileCount = files.Count;
            manifest.TotalBytes = totalBytes;
        }

        using (var archive = ZipFile.Open(exportFullPath, ZipArchiveMode.Create))
        {
            if (manifest is not null)
            {
                WriteManifestEntry(archive, manifest);
            }

            long completedBytes = 0;
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = files[index];
                var fileLength = new FileInfo(filePath).Length;
                var relativePath = Path.GetRelativePath(folderPath, filePath).Replace('\\', '/');
                var percent = files.Count == 0
                    ? 90
                    : Math.Min(90, Math.Max(1, completedBytes * 90d / Math.Max(1, totalBytes)));
                progress?.Report(new FolderBackupProgress(
                    $"正在打包 {index + 1}/{files.Count}：{relativePath}",
                    percent,
                    index,
                    files.Count,
                    completedBytes,
                    totalBytes,
                    relativePath));
                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                completedBytes += fileLength;
            }
        }

        progress?.Report(new FolderBackupProgress($"{noun}导出完成。", 100, files.Count, files.Count, totalBytes, totalBytes, null));
        return BuildEntry(exportFullPath);
    }

    private static void WriteManifestEntry(ZipArchive archive, WorkspacePackageManifest manifest)
    {
        var entry = archive.CreateEntry(WorkspacePackageManifest.FileName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(JsonSerializer.Serialize(manifest, ManifestJsonOptions));
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Restore(string folderPath, string backupsFolderName, FolderBackupEntry backup)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"文件夹不存在：{folderPath}");
        }

        if (!File.Exists(backup.Path))
        {
            throw new FileNotFoundException("选择的备份文件不存在。", backup.Path);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "GalExcleTools", "FolderRestore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            ZipFile.ExtractToDirectory(backup.Path, tempPath);
            ClearFolderForRestore(folderPath, backupsFolderName);
            CopyDirectoryContents(tempPath, folderPath);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    public List<FolderBackupEntry> GetBackups(string folderPath, string backupsFolderName)
    {
        var backupsPath = GetFolderBackupsPath(folderPath, backupsFolderName);
        if (!Directory.Exists(backupsPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(backupsPath, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(BuildEntry)
            .OrderByDescending(backup => backup.CreatedAt)
            .ToList();
    }

    private IEnumerable<string> EnumerateBackupFiles(
        string folderPath,
        string currentBackupsPath,
        bool excludeLocalArtifacts = false)
    {
        return Directory
            .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .Where(filePath => !ShouldSkipBackupFile(folderPath, filePath, currentBackupsPath, excludeLocalArtifacts));
    }

    /// <summary>
    /// 这个文件要不要跳过。
    ///
    /// 【为什么相对路径算，而不是从盘符算】原先是拿"从盘符根开始的路径段"去比名字，
    /// 于是**工作区所在的上层目录**只要叫 <c>ChapterBackups</c> 之类，整份导出就会被跳空。
    /// 改成"相对被导出文件夹的路径段"，只认这份数据内部的目录名。
    /// </summary>
    private static bool ShouldSkipBackupFile(
        string folderPath,
        string filePath,
        string currentBackupsPath,
        bool excludeLocalArtifacts)
    {
        if (IsPathInsideDirectory(filePath, currentBackupsPath))
        {
            return true;
        }

        var relativePath = Path.GetRelativePath(folderPath, filePath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 备份目录：两种模式都跳过（备份也不该把自己上一次的备份套进去）。
        if (segments.Any(IsBackupsFolderName))
        {
            return true;
        }

        // 下面这批只在**导出**时跳：备份要的是完整快照。
        if (!excludeLocalArtifacts)
        {
            return false;
        }

        if (segments.Any(segment => ExportExcludedLocalFolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fileName = segments[^1];
        if (ExportExcludedLocalFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.StartsWith(ExportLocalArtifactFilePrefix, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(fileName, KeptVoiceMapFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBackupsFolderName(string segment)
    {
        return string.Equals(segment, ProjectBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, AssetLibraryBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, ChapterBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, UnrealBackupsFolderName, StringComparison.OrdinalIgnoreCase);
    }

    private void PruneBackups(string folderPath, string backupsFolderName)
    {
        foreach (var backup in GetBackups(folderPath, backupsFolderName).Skip(MaxFolderBackupCount))
        {
            DeleteBackup(backup);
        }
    }

    private static void DeleteBackup(FolderBackupEntry backup)
    {
        if (File.Exists(backup.Path))
        {
            File.Delete(backup.Path);
        }

        var metaPath = GetBackupMetaPath(backup.Path);
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }
    }

    private FolderBackupEntry BuildEntry(string backupPath)
    {
        var fileInfo = new FileInfo(backupPath);
        var meta = ReadJson<FolderBackupMeta>(GetBackupMetaPath(backupPath));
        var createdAt = meta?.CreatedAt is { } metaCreatedAt && metaCreatedAt != default
            ? metaCreatedAt
            : fileInfo.LastWriteTime;
        var note = NormalizeBackupNote(meta?.Note ?? string.Empty);
        var noteText = string.IsNullOrWhiteSpace(note) ? "无备注" : note;
        var displayName = $"{createdAt:yyyy-MM-dd HH:mm:ss} · {noteText} · {FormatFileSize(fileInfo.Length)}";
        return new FolderBackupEntry(backupPath, createdAt, fileInfo.Length, note, displayName);
    }

    private T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void ClearFolderForRestore(string folderPath, string backupsFolderName)
    {
        var backupsPath = GetFolderBackupsPath(folderPath, backupsFolderName);
        Directory.CreateDirectory(backupsPath);

        var folderRoot = Path.GetFullPath(folderPath);
        var backupsRoot = Path.GetFullPath(backupsPath);

        foreach (var filePath in Directory.EnumerateFiles(folderRoot, "*", SearchOption.AllDirectories))
        {
            if (IsPathInsideDirectory(filePath, backupsRoot))
            {
                continue;
            }

            File.Delete(filePath);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(folderRoot, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
        {
            if (PathsEqual(directoryPath, backupsRoot) || IsPathInsideDirectory(directoryPath, backupsRoot))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
            }
        }
    }

    private static void CopyDirectoryContents(string sourcePath, string targetPath)
    {
        foreach (var directoryPath in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var targetFilePath = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            File.Copy(filePath, targetFilePath, overwrite: true);
        }
    }
}
