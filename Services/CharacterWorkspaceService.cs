using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GalExcleTools.Services;

internal sealed class CharacterWorkspaceService
{
    private const string CharacterMetaFileName = "character.json";
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// 角色表缓存（按素材库目录）。
    ///
    /// 【为什么要缓存】剧情编辑器的热路径上，查角色是**按格子**发生的：
    /// 开一章 <c>NormalizeStoryCharacterCodes</c> 要按每个非空角色格查一遍，
    /// 每次切句（说话人显示名）、每次编辑（归一掉队图层）、每次预览刷新（每个位一位）
    /// 也各查几次。而原来的实现**每次调用**都重新
    /// <c>EnumerateDirectories</c> + 逐个角色读 <c>character.json</c> 反序列化。
    /// 实测（C#，7 个角色）：中位 0.62 ms / 次，冷盘最慢 53 ms / 次 ——
    /// 一章五百多行按格子算就是上千次，全挂在 UI 线程上。
    ///
    /// 【失效口径】只有"目录的 LastWriteTimeUtc 变了"（新建 / 改名 / 删除角色文件夹都会动它）
    /// 或本服务自己写过 character.json 时才重建；否则直接复用。
    /// </summary>
    private string? _cachedCharacterFolderPath;
    private DateTime _cachedCharacterFolderWriteTimeUtc;
    private List<CharacterInfo>? _cachedCharacters;

    public CharacterWorkspaceService(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public List<CharacterInfo> GetCharactersByName(AssetLibraryInfo assetLibrary)
    {
        return GetCharactersCached(assetLibrary, ensureFolder: true)
            .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<CharacterInfo> GetCharactersByCode(AssetLibraryInfo assetLibrary)
    {
        return GetCharactersCached(assetLibrary, ensureFolder: false)
            .OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<CharacterInfo> GetCharactersByFolderOrder(AssetLibraryInfo assetLibrary)
    {
        // 最热的一条（ResolveStoryCharacter 每次调用都走这里）：交出去的必须是副本，
        // 别让调用方就地排序 / 增删把缓存本体改了。
        return GetCharactersCached(assetLibrary, ensureFolder: false).ToList();
    }

    public List<CharacterInfo> GetCharactersByFolderName(AssetLibraryInfo assetLibrary)
    {
        return GetCharactersCached(assetLibrary, ensureFolder: false)
            .OrderBy(character => Path.GetFileName(character.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>角色表本体（缓存）。调用方只读，别就地改。</summary>
    private List<CharacterInfo> GetCharactersCached(AssetLibraryInfo assetLibrary, bool ensureFolder)
    {
        var characterFolderPath = WorkspacePathUtility.GetCharacterFolderPath(assetLibrary);
        if (!Directory.Exists(characterFolderPath))
        {
            if (!ensureFolder)
            {
                return [];
            }

            Directory.CreateDirectory(characterFolderPath);
        }

        var writeTimeUtc = Directory.GetLastWriteTimeUtc(characterFolderPath);
        if (_cachedCharacters is not null &&
            string.Equals(_cachedCharacterFolderPath, characterFolderPath, StringComparison.OrdinalIgnoreCase) &&
            _cachedCharacterFolderWriteTimeUtc == writeTimeUtc)
        {
            return _cachedCharacters;
        }

        var characters = Directory
            .EnumerateDirectories(characterFolderPath)
            .Select(ReadCharacterInfo)
            .ToList();
        _cachedCharacterFolderPath = characterFolderPath;
        _cachedCharacterFolderWriteTimeUtc = writeTimeUtc;
        _cachedCharacters = characters;
        return characters;
    }

    private void InvalidateCharacterCache()
    {
        _cachedCharacters = null;
        _cachedCharacterFolderPath = null;
    }

    public CharacterInfo CreateCharacter(AssetLibraryInfo assetLibrary, CharacterEditorInput input)
    {
        var characterFolderName = TextUtility.SanitizeCharacterFolderName(input.Code);
        var characterPath = Path.Combine(WorkspacePathUtility.GetCharacterFolderPath(assetLibrary), characterFolderName);
        if (Directory.Exists(characterPath))
        {
            throw new IOException($"同名英文代号已存在：{input.Code}");
        }

        Directory.CreateDirectory(characterPath);
        EnsureCharacterSubfolders(characterPath);
        WriteCharacterMeta(characterPath, input);
        return ReadCharacterInfo(characterPath);
    }

    public CharacterInfo RenameCharacter(AssetLibraryInfo assetLibrary, CharacterInfo character, CharacterEditorInput input)
    {
        if (!Directory.Exists(character.Path))
        {
            throw new DirectoryNotFoundException(character.Path);
        }

        var newFolderName = TextUtility.SanitizeCharacterFolderName(input.Code);
        var newPath = Path.Combine(WorkspacePathUtility.GetCharacterFolderPath(assetLibrary), newFolderName);
        if (!FileSystemUtility.PathsEqual(character.Path, newPath))
        {
            if (Directory.Exists(newPath))
            {
                throw new IOException($"同名英文代号已存在：{input.Code}");
            }

            Directory.Move(character.Path, newPath);
        }

        EnsureCharacterSubfolders(newPath);
        WriteCharacterMeta(newPath, input);
        return ReadCharacterInfo(newPath);
    }

    public void EnsureCharacterSubfolders(string characterPath)
    {
        var character = new CharacterInfo(string.Empty, string.Empty, string.Empty, characterPath);
        Directory.CreateDirectory(CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Cloth));
        Directory.CreateDirectory(CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Face));
        Directory.CreateDirectory(CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Adorn));
        Directory.CreateDirectory(CharacterLayerAssetService.GetCharacterFolderPath(character, CharacterLayerKind.Vfx));
        Directory.CreateDirectory(WorkspacePathUtility.GetCharacterPortraitPreviewFolderPath(character));
    }

    public CharacterInfo ReadCharacterInfo(string characterPath)
    {
        var metaPath = Path.Combine(characterPath, CharacterMetaFileName);
        if (File.Exists(metaPath))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<CharacterMeta>(File.ReadAllText(metaPath));
                return new CharacterInfo(
                    string.IsNullOrWhiteSpace(meta?.Name) ? Path.GetFileName(characterPath) : meta.Name!,
                    string.IsNullOrWhiteSpace(meta?.Code) ? Path.GetFileName(characterPath) : meta.Code!,
                    ColorUtility.NormalizeLegacyCharacterColor(meta?.ColorHex),
                    characterPath);
            }
            catch
            {
                // Fall through to folder-derived defaults.
            }
        }

        var fallbackCode = Path.GetFileName(characterPath);
        return new CharacterInfo(fallbackCode, fallbackCode, ColorUtility.DefaultCharacterColorHex, characterPath);
    }

    private void WriteCharacterMeta(string characterPath, CharacterEditorInput input)
    {
        var meta = new CharacterMeta
        {
            Name = input.Name,
            Code = input.Code,
            ColorHex = input.ColorHex
        };
        File.WriteAllText(Path.Combine(characterPath, CharacterMetaFileName), JsonSerializer.Serialize(meta, _jsonOptions));
        // 名字 / 代号 / 颜色写过了 —— 目录 mtime 这一条兜不住（改的是文件不是目录），
        // 所以显式作废一次，免得界面还拿着旧的显示名。
        InvalidateCharacterCache();
    }
}
