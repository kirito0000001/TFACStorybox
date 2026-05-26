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

    public CharacterWorkspaceService(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public List<CharacterInfo> GetCharactersByName(AssetLibraryInfo assetLibrary)
    {
        return EnumerateCharacters(assetLibrary, ensureFolder: true)
            .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<CharacterInfo> GetCharactersByCode(AssetLibraryInfo assetLibrary)
    {
        return EnumerateCharacters(assetLibrary, ensureFolder: false)
            .OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<CharacterInfo> GetCharactersByFolderOrder(AssetLibraryInfo assetLibrary)
    {
        return EnumerateCharacters(assetLibrary, ensureFolder: false).ToList();
    }

    public List<CharacterInfo> GetCharactersByFolderName(AssetLibraryInfo assetLibrary)
    {
        return EnumerateCharacterPaths(assetLibrary, ensureFolder: false)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(ReadCharacterInfo)
            .ToList();
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
    }

    private IEnumerable<CharacterInfo> EnumerateCharacters(AssetLibraryInfo assetLibrary, bool ensureFolder)
    {
        var characterFolderPath = WorkspacePathUtility.GetCharacterFolderPath(assetLibrary);
        if (ensureFolder)
        {
            Directory.CreateDirectory(characterFolderPath);
        }
        else if (!Directory.Exists(characterFolderPath))
        {
            return [];
        }

        return EnumerateCharacterPaths(assetLibrary, ensureFolder).Select(ReadCharacterInfo);
    }

    private static IEnumerable<string> EnumerateCharacterPaths(AssetLibraryInfo assetLibrary, bool ensureFolder)
    {
        var characterFolderPath = WorkspacePathUtility.GetCharacterFolderPath(assetLibrary);
        if (ensureFolder)
        {
            Directory.CreateDirectory(characterFolderPath);
        }
        else if (!Directory.Exists(characterFolderPath))
        {
            return [];
        }

        return Directory.EnumerateDirectories(characterFolderPath);
    }
}
