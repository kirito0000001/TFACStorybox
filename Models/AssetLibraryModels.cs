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

/// <summary>
/// 一个"图集"条目 —— 背景图和道具图片**共用**这个类型。
///
/// <paramref name="Prefix"/> 是这个分类的文件名前缀：背景图 <c>BG</c>、道具图片 <c>Item</c>。
/// 编号和备注都拼在文件名里（<c>BG00_洗澡中心.png</c>），**没有单独的索引文件** ——
/// 所以"这个条目属于哪个分类"只能靠前缀认，它必须跟着条目走
/// （重排逻辑就是照它生成新名字的）。默认值让老调用点一个字都不用改。
/// </summary>
internal sealed record ImageSetEntry(string Path, string Remark, string Prefix = ImageSetPrefix.Background);

internal sealed record ImageSetRename(ImageSetEntry Entry, string TargetPath);

/// <summary>
/// 各分类的**文件名前缀**。加新分类时在这里加一个常量，别处不用动。
///
/// ⚠️ 这些前缀会变成虚幻里的**资产名**（<c>BG00_备注.uasset</c>），
/// 也决定本地文件怎么排序 —— 定完再改就要连带挪资产，所以定之前想清楚。
/// </summary>
internal static class ImageSetPrefix
{
    /// <summary>背景图：<c>BG00_备注.png</c>。它同时是剧情 CSV 里 <c>BGindex</c> 取值的来源。</summary>
    public const string Background = "BG";

    /// <summary>道具图片：<c>Item00_备注.png</c>（和落点目录名 ItemImage 对得上）。</summary>
    public const string Prop = "Item";

    /// <summary>
    /// 全部前缀。给"认不出这个条目属于哪个分类"的场合按顺序试。
    ///
    /// 加新分类时**这里也要加一个** —— 忘了加的话，那个分类的文件会被当成
    /// 默认分类（背景图）来解析，备注就认不出来了。
    /// </summary>
    public static readonly string[] All = [Background, Prop];
}

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
    public int SchemaVersion { get; set; }

    public List<FunctionEntry> Entries { get; set; } = [];
}

internal enum FunctionBranchMode
{
    None,
    Fixed,
    AssetIndex,
    System
}

internal sealed record FunctionBranch(string Code, string Remark);

internal sealed record FunctionEntry(
    string Id,
    string Name,
    string Indicator,
    string Category,
    List<string> ChoiceNotes,
    string Description = "")
{
    public FunctionBranchMode BranchMode { get; init; }

    public List<FunctionBranch> Branches { get; init; } = [];
}

internal sealed record FunctionEditorInput(
    string Name,
    string MainCode,
    string Description,
    FunctionBranchMode BranchMode,
    List<FunctionBranch> Branches);

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
