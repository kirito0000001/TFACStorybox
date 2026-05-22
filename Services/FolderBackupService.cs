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
    private const string ChapterBackupsFolderName = "ChapterBackups";
    private const string ProjectBackupsFolderName = "ProjectBackups";
    private const string AssetLibraryBackupsFolderName = "AssetLibraryBackups";
    private const string UnrealBackupsFolderName = "UnrealBackups";

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

    public FolderBackupEntry ExportToZip(
        string folderPath,
        string exportPath,
        IProgress<FolderBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
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
        progress?.Report(new FolderBackupProgress("正在扫描要导出的项目文件...", 0, 0, 0, 0, 0, null));
        cancellationToken.ThrowIfCancellationRequested();
        var files = EnumerateBackupFiles(folderPath, backupsPath)
            .Where(filePath => !PathsEqual(filePath, exportFullPath))
            .ToList();
        var totalBytes = files.Sum(filePath => new FileInfo(filePath).Length);

        using (var archive = ZipFile.Open(exportFullPath, ZipArchiveMode.Create))
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

        progress?.Report(new FolderBackupProgress("项目导出完成。", 100, files.Count, files.Count, totalBytes, totalBytes, null));
        return BuildEntry(exportFullPath);
    }

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

    private IEnumerable<string> EnumerateBackupFiles(string folderPath, string currentBackupsPath)
    {
        return Directory
            .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .Where(filePath => !ShouldSkipBackupFile(filePath, currentBackupsPath));
    }

    private static bool ShouldSkipBackupFile(string filePath, string currentBackupsPath)
    {
        if (IsPathInsideDirectory(filePath, currentBackupsPath))
        {
            return true;
        }

        var segments = Path.GetRelativePath(Path.GetPathRoot(Path.GetFullPath(filePath))!, filePath)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            string.Equals(segment, ProjectBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, AssetLibraryBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, ChapterBackupsFolderName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, UnrealBackupsFolderName, StringComparison.OrdinalIgnoreCase));
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
