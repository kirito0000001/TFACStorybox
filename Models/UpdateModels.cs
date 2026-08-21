using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GalExcleTools;

internal enum UpdateSource
{
    Gitee,
    GitHub
}

internal enum UpdateChannel
{
    Stable,
    Beta
}

internal sealed class UpdateManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("toolboxStableKey")]
    public string ToolboxStableKey { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("releaseNotesUrl")]
    public string ReleaseNotesUrl { get; set; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("requiresManualMigration")]
    public bool RequiresManualMigration { get; set; }

    [JsonPropertyName("assets")]
    public List<UpdateAssetManifest> Assets { get; set; } = [];
}

internal sealed class UpdateAssetManifest
{
    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;
}

internal sealed class UpdatePackageManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("toolboxStableKey")]
    public string ToolboxStableKey { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = string.Empty;

    [JsonPropertyName("entryExe")]
    public string EntryExe { get; set; } = string.Empty;
}

internal sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string? LatestVersion,
    UpdateManifest? Manifest,
    UpdateAssetManifest? Asset,
    UpdateSource Source,
    string Message,
    string ReleaseNotes);

internal sealed record UpdateDownloadResult(
    string PackagePath,
    UpdateManifest Manifest,
    UpdateAssetManifest Asset);

internal sealed record UpdateConnectionResult(
    bool IsSuccess,
    UpdateSource Source,
    TimeSpan Elapsed,
    string Message);
