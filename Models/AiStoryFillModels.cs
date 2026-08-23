using System;
using System.Collections.Generic;
using System.Linq;

namespace GalExcleTools;

internal sealed class AiStoryFillManifest
{
    public int SchemaVersion { get; set; } = 1;
    public int ProtocolVersion { get; set; } = 1;
    public string Profile { get; set; } = "V1";
    public string PackageType { get; set; } = "TFAC.AIStoryFill";
    public string PackageId { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public string ChapterCode { get; set; } = string.Empty;
    public string ChapterName { get; set; } = string.Empty;
    public string Mode { get; set; } = "append";
    public string AnchorRowId { get; set; } = string.Empty;
    public int AnchorRowIndex { get; set; }
    public string StoryDocumentSha256 { get; set; } = string.Empty;
    public int SourceStartParagraph { get; set; }
    public List<string> Capabilities { get; set; } = ["story.text", "story.speaker"];
    public string AiInstructionsFile { get; set; } = "AI-INSTRUCTIONS.md";
    public List<string> AllowedFields { get; set; } = ["TalkChar", "Tesxt"];
}

internal sealed record AiStoryFillProtocolDefinition(
    int Version,
    string Profile,
    bool IsImplemented,
    List<string> Capabilities,
    List<string> AllowedFields);

internal sealed class AiStorySpeakerCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<AiStorySpeakerCharacter> Characters { get; set; } = [];
    public List<string> CustomSpeakers { get; set; } = [];
    public string NarrationValue { get; set; } = string.Empty;
}

internal sealed record AiStorySpeakerCharacter(string Name, string Code);

internal sealed record AiStoryFillTaskExportResult(
    string PackageDirectory,
    string ManifestPath,
    string SpeakerCatalogPath,
    AiStoryFillManifest Manifest);

internal enum AiStoryFillItemStatus
{
    Include,
    Skip,
    Review,
    Duplicate,
    Invalid
}

internal enum AiStoryFillIssueSeverity
{
    Warning,
    Error
}

internal sealed record AiStoryFillIssue(
    AiStoryFillIssueSeverity Severity,
    string Message,
    int? FileLine = null);

internal sealed record AiStoryFillPreviewItem(
    string Id,
    int FileLine,
    string? TalkChar,
    string? Tesxt,
    string Confidence,
    AiStoryFillItemStatus Status,
    string StatusText);

internal sealed class AiStoryFillImportPreview
{
    public required string PackageDirectory { get; init; }
    public required AiStoryFillManifest Manifest { get; init; }
    public List<AiStoryFillPreviewItem> Items { get; init; } = [];
    public List<AiStoryFillIssue> Issues { get; init; } = [];
    public int PendingIncludeCount => Items.Count(item => item.Status == AiStoryFillItemStatus.Include);
    public int SkipCount => Items.Count(item => item.Status == AiStoryFillItemStatus.Skip);
    public int ReviewCount => Items.Count(item => item.Status == AiStoryFillItemStatus.Review);
    public int DuplicateCount => Items.Count(item => item.Status == AiStoryFillItemStatus.Duplicate);
    public bool CanImport => PendingIncludeCount > 0 && ReviewCount == 0 &&
        Issues.All(issue => issue.Severity != AiStoryFillIssueSeverity.Error);
}

internal sealed class AiStoryFillImportState
{
    public int SchemaVersion { get; set; } = 1;
    public List<AiStoryFillImportedPackage> Packages { get; set; } = [];
}

internal sealed record AiStoryFillImportResult(
    int ImportedCount,
    string BackupPath,
    string StatePath);

internal sealed class AiStoryFillImportedPackage
{
    public string PackageId { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
    public List<string> SourceIds { get; set; } = [];
}
