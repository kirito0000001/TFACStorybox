using System.Text.Json.Serialization;

namespace Tfac.AiStoryBridge;

internal sealed class TaskManifest
{
    public int SchemaVersion { get; set; }
    public int ProtocolVersion { get; set; }
    public string Profile { get; set; } = string.Empty;
    public string PackageType { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string ChapterCode { get; set; } = string.Empty;
    public string ChapterName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int SourceStartParagraph { get; set; }
    public List<string> AllowedFields { get; set; } = [];
    public List<string> PatchFields { get; set; } = [];
}

internal sealed class SpeakerCatalog
{
    public List<SpeakerCharacter> Characters { get; set; } = [];
    public List<string> CustomSpeakers { get; set; } = [];
    public string NarrationValue { get; set; } = string.Empty;
}

internal sealed class SpeakerCharacter
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

internal sealed class PatchSourceItem
{
    public string Id { get; set; } = string.Empty;
    public string StableId { get; set; } = string.Empty;
    public int RowIndex { get; set; }
    public int Section { get; set; }
    public string RowBaselineSha256 { get; set; } = string.Empty;
    public string TalkChar { get; set; } = string.Empty;
    public string Tesxt { get; set; } = string.Empty;
    public string PreviousText { get; set; } = string.Empty;
    public string NextText { get; set; } = string.Empty;
}

internal sealed class AppendSourceItem
{
    public string Id { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Raw { get; set; } = string.Empty;
    public string? SpeakerHint { get; set; }
    public string? TextHint { get; set; }
}

internal sealed class AppendResultItem
{
    public string Id { get; set; } = string.Empty;
    public string? TalkChar { get; set; }
    public string? Tesxt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
}

internal sealed class PatchResultItem
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, string>? Patch { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceParagraph { get; set; }
}

internal sealed record DocxParagraph(int Number, string Text);

internal sealed record ParsedParagraph(
    DocxParagraph Paragraph,
    string? Speaker,
    string Body,
    string NormalizedBody,
    bool IsProductionNote);

internal sealed record GenerationSummary(
    int SourceCount,
    int IncludeCount,
    int SkipCount,
    int ReviewCount,
    string ResultPath);
