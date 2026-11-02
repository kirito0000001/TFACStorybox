using System.Collections.Generic;
using System.IO;

namespace GalExcleTools.Services;

internal static class WorkspacePathUtility
{
    private const string ToolsFolderName = "Tools";
    private const string BackgroundFolderName = "背景图";

    /// <summary>
    /// 道具图片的素材库文件夹。
    ///
    /// 和背景图**同一套结构**（一个文件夹一堆 png、编号即排序、备注写在文件名里），
    /// 只是文件名前缀换成了 <see cref="ImageSetPrefix.Prop"/>。
    /// 落点是 <c>ImageS/PlotUse/ItemImage</c>（见 UnrealSyncPathPolicy）。
    /// </summary>
    private const string PropImageFolderName = "道具图片";
    private const string CharacterFolderName = "立绘";
    private const string MusicFolderName = "音乐";
    private const string AmbientSoundFolderName = "环境音";
    private const string SoundEffectFolderName = "特殊音效";
    private const string FunctionFolderName = "函数";
    private const string FunctionIndexFileName = "functions.json";
    private const string CharacterFilterFolderName = "角色滤镜";
    private const string CharacterFilterIndexFileName = "vfx-filters.json";
    private const string ChaptersFolderName = "Chapters";
    private const string UnrealBackupsFolderName = "UnrealBackups";

    /// <summary>语音响度缓存（DB 列）的目录名。</summary>
    private const string VoiceLoudnessFolderName = "VoiceLoudness";

    /// <summary>
    /// 语音「原始版」的目录名（只读副本，永不参与同步）。
    /// 旧的 <c>AudioBackups</c>（时间戳目录，每次规整复制一整章）已废弃，目录里有历史文件可直接删。
    /// </summary>
    private const string VoiceOriginalsFolderName = "VoiceOriginals";

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

    public static string GetPropImageFolderPath(AssetLibraryInfo assetLibrary)
    {
        return Path.Combine(assetLibrary.Path, PropImageFolderName);
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

    /// <summary>
    /// 语音页 DB 列的缓存目录：<c>{项目}/Tools/VoiceLoudness</c>。
    ///
    /// 一章一个 json（按章节代号命名）。它是**可丢弃的派生数据** —— 删掉只会让下次进页面
    /// 重新量一遍，不影响任何项目内容；所以不放进 Voice/ 里，免得被当成素材同步出去。
    /// </summary>
    public static string GetProjectVoiceLoudnessFolderPath(ProjectInfo project)
    {
        return Path.Combine(project.Path, ToolsFolderName, VoiceLoudnessFolderName);
    }

    /// <summary>某一章的响度缓存文件路径。</summary>
    public static string GetProjectVoiceLoudnessCachePath(ProjectInfo project, string chapterCode)
    {
        return Path.Combine(
            GetProjectVoiceLoudnessFolderPath(project),
            TextUtility.SanitizeCharacterFolderName(chapterCode) + ".json");
    }

    /// <summary>
    /// 语音「原始版」的存放目录：<c>{项目}/Tools/VoiceOriginals/{章节}</c>。
    ///
    /// 为什么放 Tools 而不是 Voice 里：<b>它绝对不能参与同步</b>。
    /// Voice 目录的枚举是 AllDirectories（见 <c>GetCurrentChapterVoiceFiles</c>），
    /// 放进去会被当成一条语音一起检查、一起规整、一起同步进 Unreal —— 那就成灾难了。
    /// Tools 是工具箱私有区，同步清单不认。
    /// </summary>
    public static string GetProjectVoiceOriginalsFolderPath(ProjectInfo project, string chapterCode)
    {
        return Path.Combine(
            project.Path,
            ToolsFolderName,
            VoiceOriginalsFolderName,
            TextUtility.SanitizeCharacterFolderName(chapterCode));
    }

    /// <summary>
    /// 素材库的**分类文件夹名** —— 唯一清单。
    ///
    /// 【为什么要有这么一份】建目录（<c>ProjectWorkspaceService.EnsureAssetLibraryCategoryFolders</c>）
    /// 和同步时读目录（<c>ImageSetService</c> / <c>AudioAssetService</c> 那一串）必须说同一件事，
    /// 而它们原来**各写一份**：2026-10-14 新加了「道具图片」分类，读的那边加了、建的这边忘了，
    /// 于是没有道具图片的素材库一同步就炸在
    /// <c>Directory.EnumerateFiles</c> 的"找不到路径 …\道具图片"上（2026-10-16 用户报的）。
    /// 现在两边都从这儿取，加分类只改这一处。
    /// </summary>
    public static IReadOnlyList<string> AssetLibraryCategoryFolderNames { get; } =
    [
        BackgroundFolderName,
        PropImageFolderName,
        CharacterFolderName,
        MusicFolderName,
        AmbientSoundFolderName,
        SoundEffectFolderName,
        FunctionFolderName,
        CharacterFilterFolderName
    ];

    /// <summary>展开成磁盘绝对路径（给"确保目录存在"用）。</summary>
    public static IReadOnlyList<string> GetAssetLibraryCategoryFolderPaths(string assetLibraryPath)
    {
        var result = new List<string>(AssetLibraryCategoryFolderNames.Count);
        foreach (var folderName in AssetLibraryCategoryFolderNames)
        {
            result.Add(Path.Combine(assetLibraryPath, folderName));
        }

        return result;
    }

    // GetUnrealBackupFolder 已删除（2026-10-12）：同步前的回退点从 zip 备份
    // 改成了全量本地提交，UnrealBackups 这个落点不再使用。

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
