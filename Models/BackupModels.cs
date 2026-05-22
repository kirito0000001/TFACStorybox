using System;

namespace GalExcleTools;

internal sealed record FolderBackupEntry(
    string Path,
    DateTime CreatedAt,
    long SizeBytes,
    string Note,
    string DisplayName);

internal sealed record FolderBackupProgress(
    string Message,
    double Percent,
    int CompletedFiles,
    int TotalFiles,
    long CompletedBytes,
    long TotalBytes,
    string? CurrentRelativePath);

internal sealed class FolderBackupMeta
{
    public DateTime CreatedAt { get; set; }

    public string? Note { get; set; }
}
