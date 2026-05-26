using System.IO;

namespace GalExcleTools.Services;

internal static class WorkspacePathUtility
{
    private const string ToolsFolderName = "Tools";
    private const string BackgroundFolderName = "背景图";
    private const string CharacterFolderName = "立绘";
    private const string MusicFolderName = "音乐";
    private const string AmbientSoundFolderName = "环境音";
    private const string SoundEffectFolderName = "特殊音效";
    private const string FunctionFolderName = "函数";
    private const string FunctionIndexFileName = "functions.json";
    private const string CharacterFilterFolderName = "角色滤镜";
    private const string CharacterFilterIndexFileName = "vfx-filters.json";
    private const string StorySectionsFileName = "story.sections.json";
    private const string StoryChoiceNotesFileName = "story.choice-notes.json";
    private const string ChaptersFolderName = "Chapters";
    private const string UnrealBackupsFolderName = "UnrealBackups";

    public static string GetFolderBackupsPath(string folderPath, string backupsFolderName)
    {
        return Path.Combine(folderPath, ToolsFolderName, backupsFolderName);
    }

    public static string GetBackupMetaPath(string backupPath)
    {
        return $"{backupPath}.meta.json";
    }

    public static string GetChaptersFolderPath(ProjectInfo project)
    {
        return Path.Combine(project.Path, ChaptersFolderName);
    }

    public static string GetStorySectionsPath(ChapterInfo chapter)
    {
        return Path.Combine(chapter.Path, StorySectionsFileName);
    }

    public static string GetStoryChoiceNotesPath(ChapterInfo chapter)
    {
        return Path.Combine(chapter.Path, StoryChoiceNotesFileName);
    }

    public static string GetProjectVoiceFolderPath(ProjectInfo project)
    {
        return Path.Combine(project.Path, "Voice");
    }

    public static string GetProjectChapterVoiceFolderPath(ProjectInfo project, string chapterCode)
    {
        return Path.Combine(GetProjectVoiceFolderPath(project), TextUtility.SanitizeCharacterFolderName(chapterCode));
    }

    public static string GetCharacterFaceScopeMetaPath(string faceFolderPath)
    {
        return Path.Combine(faceFolderPath, "face-scope.meta.json");
    }

    public static string GetCharacterAdornScopeMetaPath(string adornFolderPath)
    {
        return Path.Combine(adornFolderPath, "adorn-scope.meta.json");
    }

    public static string GetCharacterPortraitPreviewFolderPath(CharacterInfo character)
    {
        return Path.Combine(character.Path, "Log_Preview");
    }

    public static string GetCharacterPortraitPreviewMetaPath(CharacterInfo character)
    {
        return Path.Combine(GetCharacterPortraitPreviewFolderPath(character), "portrait-preview.meta.json");
    }

    public static string GetBackgroundFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, BackgroundFolderName);
    }

    public static string GetMusicFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, MusicFolderName);
    }

    public static string GetAmbientSoundFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, AmbientSoundFolderName);
    }

    public static string GetSoundEffectFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, SoundEffectFolderName);
    }

    public static string GetFunctionFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, FunctionFolderName);
    }

    public static string GetFunctionIndexPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(GetFunctionFolderPath(assetLibrary), FunctionIndexFileName);
    }

    public static string GetCharacterFilterFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, CharacterFilterFolderName);
    }

    public static string GetCharacterFilterIndexPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(GetCharacterFilterFolderPath(assetLibrary), CharacterFilterIndexFileName);
    }

    public static string GetAudioFolderPath(AssetLibraryInfo assetLibrary, AudioAssetKind kind)
    {
        return kind switch
        {
            AudioAssetKind.Music => GetMusicFolderPath(assetLibrary),
            AudioAssetKind.Ambient => GetAmbientSoundFolderPath(assetLibrary),
            AudioAssetKind.SoundEffect => GetSoundEffectFolderPath(assetLibrary),
            _ => GetMusicFolderPath(assetLibrary)
        };
    }

    public static string GetCharacterFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, CharacterFolderName);
    }

    public static string GetUnrealBackupFolder(UnrealSyncContext context)
    {
        return Path.Combine(context.Project.Path, ToolsFolderName, UnrealBackupsFolderName);
    }

    public static string GetUnrealSyncStatePath(UnrealSyncContext context)
    {
        return Path.Combine(context.Project.Path, ToolsFolderName, "unreal-sync-state.json");
    }

    public static string ToUnrealAssetPath(string contentRootPath, string contentFolderPath)
    {
        var relativePath = Path.GetRelativePath(contentRootPath, contentFolderPath)
            .Replace('\\', '/')
            .Trim('/');
        return string.IsNullOrWhiteSpace(relativePath) || relativePath == "."
            ? "/Game"
            : $"/Game/{relativePath}";
    }
}
