using System;
using System.Collections.Generic;

namespace GalExcleTools;

internal sealed class AiStoryFillManifest
{
    public int SchemaVersion { get; set; } = 1;
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
    public List<string> AllowedFields { get; set; } = ["TalkChar", "Tesxt"];
}

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
