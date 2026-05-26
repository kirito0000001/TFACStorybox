using System;
using System.Collections.Generic;

namespace GalExcleTools;

internal sealed record AssetLibraryInfo(
    string Name,
    string FolderName,
    string Path,
    string? ThumbnailPath,
    bool IsPortraitPreviewEnabled,
    DateTime LastEditedAt);

internal sealed record BackgroundImageEntry(string Path, string Remark);

internal sealed record BackgroundImageRename(BackgroundImageEntry Entry, string TargetPath);

internal enum AudioAssetKind
{
    Music,
    Ambient,
    SoundEffect
}

internal sealed record MusicEntry(string Path, string Remark);

internal sealed record MusicRename(MusicEntry Entry, string TargetPath);

internal sealed class FunctionIndex
{
    public List<FunctionEntry> Entries { get; set; } = [];
}

internal sealed record FunctionEntry(
    string Id,
    string Name,
    string Indicator,
    string Category,
    List<string> ChoiceNotes);

internal sealed record FunctionEditorInput(
    string Name,
    string Indicator,
    string Category,
    List<string> ChoiceNotes);

internal sealed class CharacterFilterIndex
{
    public List<CharacterFilterEntry> Entries { get; set; } = [];
}

internal sealed record CharacterFilterEntry(string Id, string Remark);

internal sealed record CharacterInfo(string Name, string Code, string ColorHex, string Path);

internal sealed record CharacterEditorInput(string Name, string Code, string ColorHex);

internal enum CharacterLayerKind
{
    Cloth,
    Face,
    Adorn,
    Vfx
}

internal sealed record CharacterLayerEntry(string Path, string Remark, string Scope);

internal sealed record CharacterLayerRename(CharacterLayerEntry Entry, string TargetPath);

internal sealed record CharacterLayerViewerState(CharacterLayerKind Kind, string Path);

internal sealed record StoryCharacterLayerSpec(CharacterLayerKind Kind, string FieldPrefix, string DisplayName);

internal sealed class CharacterLayerScopeMeta
{
    public Dictionary<string, CharacterLayerScopeEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CharacterPortraitPreviewMeta
{
    public Dictionary<string, CharacterPortraitPreviewEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CharacterPortraitPreviewEntry
{
    public string? PreviewFileName { get; set; }
}

internal sealed class CharacterLayerScopeEntry
{
    public bool UseAllCostumes { get; set; } = true;

    public List<string> CostumeHashes { get; set; } = [];
}

internal sealed class CharacterMeta
{
    public string? Name { get; set; }

    public string? Code { get; set; }

    public string? ColorHex { get; set; }
}

internal sealed class AssetLibraryMeta
{
    public string? AssetLibraryName { get; set; }

    public string? ThumbnailFileName { get; set; }

    public bool IsPortraitPreviewEnabled { get; set; }

    public DateTime LastEditedAt { get; set; }
}
