using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.Threading;

namespace GalExcleTools.Services;

internal sealed class ProjectWorkspaceService
{
    private const string ProjectFolderPrefix = "项目-";
    private const string AssetLibraryFolderPrefix = "素材库-";
    private const string ToolsFolderName = "Tools";
    private const string BackgroundFolderName = "背景图";
    private const string CharacterFolderName = "立绘";
    private const string MusicFolderName = "音乐";
    private const string AmbientSoundFolderName = "环境音";
    private const string SoundEffectFolderName = "特殊音效";
    private const string FunctionFolderName = "函数";
    private const string CharacterFilterFolderName = "角色滤镜";
    private const string ProjectMetaFileName = "project.meta.json";
    private const string AssetLibraryMetaFileName = "asset-library.meta.json";
    private const string ChapterMetaFileName = "chapter.meta.json";
    private const string ChaptersFolderName = "Chapters";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public List<ProjectInfo> GetProjects(string projectRootPath)
    {
        if (!Directory.Exists(projectRootPath))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(projectRootPath)
            .Where(path => File.Exists(Path.Combine(path, ToolsFolderName, ProjectMetaFileName)))
            .Select(ReadProjectInfo)
            .OrderByDescending(project => project.LastEditedAt)
            .ToList();
    }

    public List<AssetLibraryInfo> GetAssetLibraries(string projectRootPath)
    {
        if (!Directory.Exists(projectRootPath))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(projectRootPath)
            .Where(path => File.Exists(Path.Combine(path, ToolsFolderName, AssetLibraryMetaFileName)))
            .Select(ReadAssetLibraryInfo)
            .OrderByDescending(library => library.LastEditedAt)
            .ToList();
    }

    public ProjectInfo ReadProjectInfo(string projectPath)
    {
        var projectName = Path.GetFileName(projectPath);
        var toolsPath = Path.Combine(projectPath, ToolsFolderName);
        var metaPath = Path.Combine(toolsPath, ProjectMetaFileName);
        var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
        var thumbnailPath = ResolveThumbnailPath(toolsPath, meta.ThumbnailFileName);

        return new ProjectInfo(
            string.IsNullOrWhiteSpace(meta.ProjectName) ? projectName : meta.ProjectName,
            string.IsNullOrWhiteSpace(meta.ProjectCode) ? Path.GetFileName(projectPath) : meta.ProjectCode,
            Path.GetFileName(projectPath),
            projectPath,
            thumbnailPath,
            string.IsNullOrWhiteSpace(meta.AssetLibraryName) ? "未关联素材库" : meta.AssetLibraryName,
            meta.AssetLibraryFolderName,
            meta.LastEditedAt == default ? Directory.GetLastWriteTime(projectPath) : meta.LastEditedAt);
    }

    public AssetLibraryInfo ReadAssetLibraryInfo(string assetLibraryPath)
    {
        var libraryName = Path.GetFileName(assetLibraryPath);
        var toolsPath = Path.Combine(assetLibraryPath, ToolsFolderName);
        var metaPath = Path.Combine(toolsPath, AssetLibraryMetaFileName);
        var meta = ReadJson<AssetLibraryMeta>(metaPath) ?? new AssetLibraryMeta();
        var thumbnailPath = ResolveThumbnailPath(toolsPath, meta.ThumbnailFileName);

        return new AssetLibraryInfo(
            string.IsNullOrWhiteSpace(meta.AssetLibraryName) ? libraryName : meta.AssetLibraryName,
            Path.GetFileName(assetLibraryPath),
            assetLibraryPath,
            thumbnailPath,
            meta.IsPortraitPreviewEnabled,
            meta.LastEditedAt == default ? Directory.GetLastWriteTime(assetLibraryPath) : meta.LastEditedAt);
    }

    public AssetLibraryInfo? ResolveProjectAssetLibrary(string projectRootPath, ProjectInfo project)
    {
        return GetAssetLibraries(projectRootPath)
            .FirstOrDefault(library =>
                string.Equals(library.FolderName, project.AssetLibraryFolderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(library.Name, project.AssetLibraryName, StringComparison.OrdinalIgnoreCase));
    }

    public ChapterInfo ReadChapterInfo(string chapterPath)
    {
        var metaPath = Path.Combine(chapterPath, ChapterMetaFileName);
        var meta = ReadJson<ChapterMeta>(metaPath) ?? new ChapterMeta();
        var fallbackCode = Path.GetFileName(chapterPath);
        return new ChapterInfo(
            string.IsNullOrWhiteSpace(meta.ChapterName) ? fallbackCode : meta.ChapterName,
            string.IsNullOrWhiteSpace(meta.ChapterCode) ? fallbackCode : meta.ChapterCode,
            string.IsNullOrWhiteSpace(meta.ChapterType) ? ChapterKind.MainThread : meta.ChapterType,
            chapterPath,
            meta.LastEditedAt == default ? Directory.GetLastWriteTime(chapterPath) : meta.LastEditedAt,
            Math.Max(0, meta.LastEditedRowIndex));
    }

    public List<ChapterInfo> GetChapters(ProjectInfo project)
    {
        var chaptersFolderPath = WorkspacePathUtility.GetChaptersFolderPath(project);
        if (!Directory.Exists(chaptersFolderPath))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(chaptersFolderPath)
            .Select(ReadChapterInfo)
            .OrderByDescending(chapter => chapter.LastEditedAt)
            .ToList();
    }

    public ChapterInfo CreateChapter(ProjectInfo project, ChapterEditorInput input)
    {
        var chapterFolderName = TextUtility.SanitizeCharacterFolderName(input.Code);
        var chapterPath = Path.Combine(WorkspacePathUtility.GetChaptersFolderPath(project), chapterFolderName);
        if (Directory.Exists(chapterPath))
        {
            throw new IOException($"同名英文代号已存在：{input.Code}");
        }

        Directory.CreateDirectory(chapterPath);
        WriteChapterMeta(chapterPath, input);
        return ReadChapterInfo(chapterPath);
    }

    public ChapterInfo UpdateChapter(ProjectInfo project, ChapterInfo chapter, ChapterEditorInput input)
    {
        var targetPath = Path.Combine(WorkspacePathUtility.GetChaptersFolderPath(project), TextUtility.SanitizeCharacterFolderName(input.Code));
        if (!FileSystemUtility.PathsEqual(chapter.Path, targetPath) && Directory.Exists(targetPath))
        {
            throw new IOException($"同名英文代号已存在：{input.Code}");
        }

        if (!FileSystemUtility.PathsEqual(chapter.Path, targetPath))
        {
            Directory.Move(chapter.Path, targetPath);
        }

        WriteChapterMeta(targetPath, input);
        return ReadChapterInfo(targetPath);
    }

    public ChapterInfo CreateImportedChapter(ProjectInfo project, ChapterEditorInput input)
    {
        return CreateChapter(project, input);
    }

    public void DeleteChapter(ChapterInfo chapter)
    {
        Directory.Delete(chapter.Path, recursive: true);
    }

    public int UpdateChapterProjectCodePrefix(string projectPath, string oldProjectCode, string newProjectCode)
    {
        var project = ReadProjectInfo(projectPath);
        var chaptersFolderPath = WorkspacePathUtility.GetChaptersFolderPath(project);
        if (!Directory.Exists(chaptersFolderPath))
        {
            return 0;
        }

        var renamePlans = GetChapters(project)
            .Select(chapter =>
            {
                var newCode = ReplaceChapterProjectCode(chapter.Code, oldProjectCode, newProjectCode);
                var newPath = Path.Combine(chaptersFolderPath, TextUtility.SanitizeCharacterFolderName(newCode));
                return new ChapterRenamePlan(chapter, newCode, newPath);
            })
            .Where(plan => !string.Equals(plan.Chapter.Code, plan.NewCode, StringComparison.Ordinal) ||
                !FileSystemUtility.PathsEqual(plan.Chapter.Path, plan.NewPath))
            .ToList();

        var duplicateTarget = renamePlans
            .GroupBy(plan => plan.NewPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTarget is not null)
        {
            throw new InvalidOperationException($"章节代号同步后出现重复目录：{Path.GetFileName(duplicateTarget.Key)}");
        }

        foreach (var plan in renamePlans)
        {
            if (!FileSystemUtility.PathsEqual(plan.Chapter.Path, plan.NewPath) && Directory.Exists(plan.NewPath))
            {
                throw new InvalidOperationException($"章节代号同步目标已存在：{Path.GetFileName(plan.NewPath)}");
            }
        }

        foreach (var plan in renamePlans)
        {
            if (!FileSystemUtility.PathsEqual(plan.Chapter.Path, plan.NewPath))
            {
                Directory.Move(plan.Chapter.Path, plan.NewPath);
            }

            WriteChapterInfo(
                plan.NewPath,
                new ChapterEditorInput(plan.Chapter.Name, plan.NewCode, plan.Chapter.Type));
        }

        return renamePlans.Count;
    }

    public void WriteChapterInfo(string chapterPath, ChapterEditorInput input)
    {
        Directory.CreateDirectory(chapterPath);
        WriteChapterMeta(chapterPath, input);
    }

    public static string GetChapterCodeSegment(string chapterCode, string projectCode)
    {
        var prefix = $"{projectCode}-";
        if (chapterCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return chapterCode[prefix.Length..];
        }

        var separatorIndex = chapterCode.IndexOf('-');
        return separatorIndex >= 0 ? chapterCode[(separatorIndex + 1)..] : chapterCode;
    }

    public void SaveChapterProgress(ChapterInfo chapter, int rowIndex)
    {
        WriteChapterMeta(chapter.Path, meta =>
        {
            meta.ChapterName = string.IsNullOrWhiteSpace(meta.ChapterName) ? chapter.Name : meta.ChapterName;
            meta.ChapterCode = string.IsNullOrWhiteSpace(meta.ChapterCode) ? chapter.Code : meta.ChapterCode;
            meta.ChapterType = string.IsNullOrWhiteSpace(meta.ChapterType) ? chapter.Type : meta.ChapterType;
            meta.LastEditedAt = DateTime.Now;
            meta.LastEditedRowIndex = Math.Max(0, rowIndex);
        });
    }

    public ProjectInfo CreateProject(
        string projectRootPath,
        string projectName,
        string projectCode,
        AssetLibraryInfo assetLibrary,
        string? thumbnailSourcePath)
    {
        ValidateFolderName(projectName, "项目名称");
        ValidateFolderName(projectCode, "项目英文代号");
        var projectPath = BuildProjectFolderPath(projectRootPath, projectName);
        if (Directory.Exists(projectPath))
        {
            throw new IOException("同名文件夹已经存在。");
        }

        var toolsPath = Path.Combine(projectPath, ToolsFolderName);
        Directory.CreateDirectory(toolsPath);
        Directory.CreateDirectory(Path.Combine(projectPath, ChaptersFolderName));

        var thumbnailFileName = CopyThumbnailToTools(thumbnailSourcePath, toolsPath);
        var meta = new ProjectMeta
        {
            ProjectName = projectName,
            ProjectCode = projectCode,
            ThumbnailFileName = thumbnailFileName,
            AssetLibraryName = assetLibrary.Name,
            AssetLibraryFolderName = assetLibrary.FolderName,
            LastEditedAt = DateTime.Now
        };
        File.WriteAllText(Path.Combine(toolsPath, ProjectMetaFileName), JsonSerializer.Serialize(meta, JsonOptions));
        return ReadProjectInfo(projectPath);
    }

    public AssetLibraryInfo CreateAssetLibrary(string projectRootPath, string assetLibraryName, string? thumbnailSourcePath)
    {
        ValidateFolderName(assetLibraryName, "素材库名称");
        var assetLibraryPath = BuildAssetLibraryFolderPath(projectRootPath, assetLibraryName);
        if (Directory.Exists(assetLibraryPath))
        {
            throw new IOException("同名文件夹已经存在。");
        }

        var toolsPath = Path.Combine(assetLibraryPath, ToolsFolderName);
        Directory.CreateDirectory(toolsPath);
        EnsureAssetLibraryCategoryFolders(assetLibraryPath);

        var thumbnailFileName = CopyThumbnailToTools(thumbnailSourcePath, toolsPath);
        var meta = new AssetLibraryMeta
        {
            AssetLibraryName = assetLibraryName,
            ThumbnailFileName = thumbnailFileName,
            IsPortraitPreviewEnabled = false,
            LastEditedAt = DateTime.Now
        };
        File.WriteAllText(Path.Combine(toolsPath, AssetLibraryMetaFileName), JsonSerializer.Serialize(meta, JsonOptions));
        return ReadAssetLibraryInfo(assetLibraryPath);
    }

    public ProjectInfo RenameProject(string projectRootPath, ProjectInfo project, string newName)
    {
        var newPath = BuildProjectFolderPath(projectRootPath, newName);
        if (!FileSystemUtility.PathsEqual(project.Path, newPath))
        {
            if (Directory.Exists(newPath))
            {
                throw new IOException("同名文件夹已经存在。");
            }

            Directory.Move(project.Path, newPath);
        }

        WriteProjectMeta(newPath, meta =>
        {
            meta.ProjectName = newName;
            meta.ProjectCode = project.Code;
            meta.LastEditedAt = DateTime.Now;
        });
        return ReadProjectInfo(newPath);
    }

    public ProjectInfo UpdateProjectInfo(string projectRootPath, ProjectInfo project, string name, string code)
    {
        var newPath = BuildProjectFolderPath(projectRootPath, name);
        if (!FileSystemUtility.PathsEqual(project.Path, newPath))
        {
            if (Directory.Exists(newPath))
            {
                throw new IOException("同名文件夹已经存在。");
            }

            Directory.Move(project.Path, newPath);
        }

        WriteProjectMeta(newPath, meta =>
        {
            meta.ProjectName = name;
            meta.ProjectCode = code;
            meta.LastEditedAt = DateTime.Now;
        });
        return ReadProjectInfo(newPath);
    }

    public AssetLibraryInfo RenameAssetLibrary(string projectRootPath, AssetLibraryInfo assetLibrary, string newName)
    {
        var newPath = BuildAssetLibraryFolderPath(projectRootPath, newName);
        if (!FileSystemUtility.PathsEqual(assetLibrary.Path, newPath))
        {
            if (Directory.Exists(newPath))
            {
                throw new IOException("同名文件夹已经存在。");
            }

            Directory.Move(assetLibrary.Path, newPath);
        }

        WriteAssetLibraryMeta(newPath, meta =>
        {
            meta.AssetLibraryName = newName;
            meta.LastEditedAt = DateTime.Now;
        });
        return ReadAssetLibraryInfo(newPath);
    }

    public void TouchProjectLastEditedAt(ProjectInfo project)
    {
        WriteProjectMeta(project.Path, meta =>
        {
            meta.ProjectName = project.Name;
            meta.ProjectCode = project.Code;
            meta.LastEditedAt = DateTime.Now;
        });
    }

    public void TouchAssetLibraryLastEditedAt(AssetLibraryInfo assetLibrary)
    {
        WriteAssetLibraryMeta(assetLibrary.Path, meta =>
        {
            meta.AssetLibraryName = assetLibrary.Name;
            meta.LastEditedAt = DateTime.Now;
        });
    }

    public AssetLibraryInfo SetAssetLibraryPortraitPreviewEnabled(AssetLibraryInfo assetLibrary, bool isEnabled)
    {
        WriteAssetLibraryMeta(assetLibrary.Path, meta =>
        {
            meta.AssetLibraryName = assetLibrary.Name;
            meta.IsPortraitPreviewEnabled = isEnabled;
            meta.LastEditedAt = DateTime.Now;
        });
        return ReadAssetLibraryInfo(assetLibrary.Path);
    }

    public void DeleteProject(ProjectInfo project)
    {
        Directory.Delete(project.Path, recursive: true);
    }

    public void DeleteAssetLibrary(string projectRootPath, AssetLibraryInfo assetLibrary)
    {
        ClearProjectAssetLibraryReferences(projectRootPath, assetLibrary.FolderName, assetLibrary.Name);
        Directory.Delete(assetLibrary.Path, recursive: true);
    }

    public void SetProjectAssetLibrary(ProjectInfo project, AssetLibraryInfo assetLibrary)
    {
        WriteProjectMeta(project.Path, meta =>
        {
            meta.ProjectName = project.Name;
            meta.AssetLibraryName = assetLibrary.Name;
            meta.AssetLibraryFolderName = assetLibrary.FolderName;
            meta.LastEditedAt = DateTime.Now;
        });
    }

    public UnrealProjectBinding ReadProjectUnrealBinding(ProjectInfo project)
    {
        var metaPath = Path.Combine(project.Path, ToolsFolderName, ProjectMetaFileName);
        var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
        return new UnrealProjectBinding(meta.UnrealEnginePath, meta.UnrealProjectPath, meta.UnrealContentFolderPath);
    }

    public void SaveProjectUnrealBinding(
        ProjectInfo project,
        string enginePath,
        string unrealProjectPath,
        string contentFolderPath)
    {
        WriteProjectMeta(project.Path, meta =>
        {
            meta.ProjectName = string.IsNullOrWhiteSpace(meta.ProjectName) ? project.Name : meta.ProjectName;
            meta.ProjectCode = string.IsNullOrWhiteSpace(meta.ProjectCode) ? project.Code : meta.ProjectCode;
            meta.AssetLibraryName = string.IsNullOrWhiteSpace(meta.AssetLibraryName) ? project.AssetLibraryName : meta.AssetLibraryName;
            meta.AssetLibraryFolderName = string.IsNullOrWhiteSpace(meta.AssetLibraryFolderName) ? project.AssetLibraryFolderName : meta.AssetLibraryFolderName;
            meta.UnrealEnginePath = enginePath;
            meta.UnrealProjectPath = unrealProjectPath;
            meta.UnrealContentFolderPath = contentFolderPath;
            meta.LastEditedAt = DateTime.Now;
        });
    }

    public void UpdateProjectAssetLibraryReferences(
        string projectRootPath,
        string oldFolderName,
        string oldName,
        string newName,
        string newFolderName)
    {
        foreach (var project in GetProjects(projectRootPath))
        {
            UpdateProjectAssetLibraryReferenceIfMatches(
                project,
                oldFolderName,
                oldName,
                meta =>
                {
                    meta.AssetLibraryName = newName;
                    meta.AssetLibraryFolderName = newFolderName;
                });
        }
    }

    public void ClearProjectAssetLibraryReferences(string projectRootPath, string folderName, string name)
    {
        foreach (var project in GetProjects(projectRootPath))
        {
            UpdateProjectAssetLibraryReferenceIfMatches(
                project,
                folderName,
                name,
                meta =>
                {
                    meta.AssetLibraryName = null;
                    meta.AssetLibraryFolderName = null;
                });
        }
    }

    /// <summary>
    /// 导入一个项目包。
    ///
    /// 【2026-10-15 换了实现】原来是"先整包解到临时目录、再拷进工作区"：
    /// 3 GB 的素材库包等于 6 GB 写入，而且 <c>ZipFile.ExtractToDirectory</c> 没有进度回调，
    /// 进度条会好几分钟不动。现在先读**条目表**（很便宜）定出根目录与身份文件，
    /// 再逐条直接写进最终位置 —— 省掉一次全量拷贝，进度也是真的。
    /// </summary>
    public ProjectInfo ImportProjectArchive(
        string projectRootPath,
        string archivePath,
        IProgress<FolderBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("项目包不存在。", archivePath);
        }

        using var archive = ZipFile.OpenRead(archivePath);
        var metaEntry = FindPackageMetaEntry(archive, ProjectMetaFileName);
        if (metaEntry is null)
        {
            throw new InvalidDataException("这个 zip 里没有找到 Tools/project.meta.json，无法识别为项目包。");
        }

        var rootPrefix = GetPackageRootPrefix(metaEntry, ProjectMetaFileName);
        var meta = ReadJsonText<ProjectMeta>(ReadEntryText(metaEntry)) ?? new ProjectMeta();
        var projectName = string.IsNullOrWhiteSpace(meta.ProjectName)
            ? Path.GetFileNameWithoutExtension(archivePath)
            : meta.ProjectName;
        var projectCode = string.IsNullOrWhiteSpace(meta.ProjectCode)
            ? Path.GetFileNameWithoutExtension(archivePath)
            : meta.ProjectCode;
        var rootFolderName = GetPackageRootFolderName(rootPrefix);
        var sourceFolderName = rootFolderName.Length == 0
            ? $"{ProjectFolderPrefix}{projectName}"
            : rootFolderName;
        var targetFolderName = TextUtility.SanitizeImportedRootFolderName(sourceFolderName);

        Directory.CreateDirectory(projectRootPath);
        var targetPath = GetUniqueDirectoryPath(projectRootPath, targetFolderName);
        try
        {
            ExtractPackageEntries(archive, rootPrefix, targetPath, progress, cancellationToken);
            WriteProjectMeta(targetPath, importedMeta =>
            {
                importedMeta.ProjectName = string.IsNullOrWhiteSpace(importedMeta.ProjectName) ? projectName : importedMeta.ProjectName;
                importedMeta.ProjectCode = string.IsNullOrWhiteSpace(importedMeta.ProjectCode) ? projectCode : importedMeta.ProjectCode;
                importedMeta.LastEditedAt = DateTime.Now;
            });

            return ReadProjectInfo(targetPath);
        }
        catch
        {
            // 半份"看起来像个项目"的目录比没有更糟：失败就把刚铺的那个整个删掉。
            DeleteDirectoryQuietly(targetPath);
            throw;
        }
    }

    /// <summary>导入一个素材库包。实现说明同 <see cref="ImportProjectArchive"/>。</summary>
    public AssetLibraryInfo ImportAssetLibraryArchive(
        string projectRootPath,
        string archivePath,
        IProgress<FolderBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("素材库包不存在。", archivePath);
        }

        using var archive = ZipFile.OpenRead(archivePath);
        var metaEntry = FindPackageMetaEntry(archive, AssetLibraryMetaFileName);
        if (metaEntry is null)
        {
            throw new InvalidDataException("这个 zip 里没有找到 Tools/asset-library.meta.json，无法识别为素材库包。");
        }

        var rootPrefix = GetPackageRootPrefix(metaEntry, AssetLibraryMetaFileName);
        var meta = ReadJsonText<AssetLibraryMeta>(ReadEntryText(metaEntry)) ?? new AssetLibraryMeta();
        var assetLibraryName = string.IsNullOrWhiteSpace(meta.AssetLibraryName)
            ? Path.GetFileNameWithoutExtension(archivePath)
            : meta.AssetLibraryName;
        var rootFolderName = GetPackageRootFolderName(rootPrefix);
        var sourceFolderName = rootFolderName.Length == 0
            ? TextUtility.BuildPrefixedFolderName(assetLibraryName, AssetLibraryFolderPrefix)
            : rootFolderName;
        var targetFolderName = TextUtility.BuildPrefixedFolderName(
            TextUtility.SanitizeImportedRootFolderName(sourceFolderName),
            AssetLibraryFolderPrefix);

        Directory.CreateDirectory(projectRootPath);
        var targetPath = GetUniqueDirectoryPath(projectRootPath, targetFolderName);
        try
        {
            ExtractPackageEntries(archive, rootPrefix, targetPath, progress, cancellationToken);
            EnsureAssetLibraryCategoryFolders(targetPath);
            WriteAssetLibraryMeta(targetPath, importedMeta =>
            {
                importedMeta.AssetLibraryName = string.IsNullOrWhiteSpace(importedMeta.AssetLibraryName)
                    ? assetLibraryName
                    : importedMeta.AssetLibraryName;
                importedMeta.LastEditedAt = DateTime.Now;
            });

            return ReadAssetLibraryInfo(targetPath);
        }
        catch
        {
            DeleteDirectoryQuietly(targetPath);
            throw;
        }
    }

    public string BuildProjectFolderPath(string projectRootPath, string projectName)
    {
        return Path.Combine(projectRootPath, TextUtility.BuildPrefixedFolderName(projectName, ProjectFolderPrefix));
    }

    public string BuildAssetLibraryFolderPath(string projectRootPath, string assetLibraryName)
    {
        return Path.Combine(projectRootPath, TextUtility.BuildPrefixedFolderName(assetLibraryName, AssetLibraryFolderPrefix));
    }

    private static string ReplaceChapterProjectCode(string chapterCode, string oldProjectCode, string newProjectCode)
    {
        var oldPrefix = $"{oldProjectCode}-";
        if (chapterCode.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"{newProjectCode}-{chapterCode[oldPrefix.Length..]}";
        }

        var separatorIndex = chapterCode.IndexOf('-');
        return separatorIndex >= 0
            ? $"{newProjectCode}{chapterCode[separatorIndex..]}"
            : $"{newProjectCode}-{chapterCode}";
    }

    public static void ValidateFolderName(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new IOException($"请输入{label}。");
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new IOException($"{label}包含不能用于文件夹名称的字符。");
        }
    }

    public static void EnsureAssetLibraryCategoryFolders(string assetLibraryPath)
    {
        Directory.CreateDirectory(Path.Combine(assetLibraryPath, BackgroundFolderName));
        Directory.CreateDirectory(Path.Combine(assetLibraryPath, CharacterFolderName));
        Directory.CreateDirectory(Path.Combine(assetLibraryPath, MusicFolderName));
        Directory.CreateDirectory(Path.Combine(assetLibraryPath, AmbientSoundFolderName));
        Directory.CreateDirectory(Path.Combine(assetLibraryPath, SoundEffectFolderName));
        Directory.CreateDirectory(Path.Combine(assetLibraryPath, FunctionFolderName));
        Directory.CreateDirectory(Path.Combine(assetLibraryPath, CharacterFilterFolderName));
    }

    private static string GetUniqueDirectoryPath(string parentPath, string folderName)
    {
        var candidatePath = Path.Combine(parentPath, folderName);
        var duplicateIndex = 1;
        while (Directory.Exists(candidatePath) || File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(parentPath, $"{folderName}_{duplicateIndex}");
            duplicateIndex++;
        }

        return candidatePath;
    }

    /// <summary>
    /// 在 zip 的条目表里找身份文件（<c>Tools/xxx.meta.json</c>），取**最浅**的那个。
    ///
    /// 为什么要允许有上层目录：别人不一定会用工具箱导出 —— 也可能把文件夹直接右键压成 zip，
    /// 那样条目名前面会多一层（甚至两层）目录。原来的实现是把整个包解到临时目录再搜，
    /// 现在只扫条目表，效果一样但不用先落盘。
    /// </summary>
    private static ZipArchiveEntry? FindPackageMetaEntry(ZipArchive archive, string metaFileName)
    {
        var suffix = $"{ToolsFolderName}/{metaFileName}";
        return archive.Entries
            .Where(entry => !entry.FullName.Replace('\\', '/').EndsWith('/'))
            .Where(entry => entry.FullName.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName.Count(character => character is '/' or '\\'))
            .ThenBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>身份文件所在的那层根目录（相对 zip 根；没有上层目录就是空串）。</summary>
    private static string GetPackageRootPrefix(ZipArchiveEntry metaEntry, string metaFileName)
    {
        var normalized = metaEntry.FullName.Replace('\\', '/');
        var suffix = $"{ToolsFolderName}/{metaFileName}";
        return normalized[..^suffix.Length].Trim('/');
    }

    /// <summary>根目录那个文件夹的名字（<c>a/b</c> → <c>b</c>）；没有上层目录就是空串。</summary>
    private static string GetPackageRootFolderName(string rootPrefix)
    {
        return rootPrefix.Length == 0 ? string.Empty : rootPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 逐条把 zip 铺到目标目录，边铺边报进度。
    ///
    /// 【安全】条目名必须落在目标目录里：绝对路径、盘符、用 <c>..</c> 往上跑的，一律拒绝
    ///（zip-slip）。<c>ZipFile.ExtractToDirectory</c> 内部有这层校验，自己解就得自己兜住。
    ///
    /// 【清单不铺进去】<c>package-manifest.json</c> 是"关于这个包"的说明，不属于工作区对象本身。
    /// </summary>
    private static void ExtractPackageEntries(
        ZipArchive archive,
        string rootPrefix,
        string targetPath,
        IProgress<FolderBackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var prefix = rootPrefix.Length == 0 ? string.Empty : rootPrefix + "/";
        var entries = archive.Entries
            .Where(entry => !entry.FullName.Replace('\\', '/').EndsWith('/'))
            .Where(entry => prefix.Length == 0 ||
                            entry.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var totalBytes = entries.Sum(entry => entry.Length);
        var targetRoot = Path.GetFullPath(targetPath);
        var completedBytes = 0L;

        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var name = entry.FullName.Replace('\\', '/');
            var relative = prefix.Length == 0 ? name : name[prefix.Length..];
            if (string.Equals(relative, WorkspacePackageManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!FileSystemUtility.IsPathInsideDirectory(destination, targetRoot))
            {
                throw new InvalidDataException($"包里的条目想写到目标目录之外，已拒绝：{entry.FullName}");
            }

            progress?.Report(new FolderBackupProgress(
                $"正在解压 {index + 1}/{entries.Count}：{relative}",
                entries.Count == 0
                    ? 90
                    : Math.Min(90, Math.Max(1, completedBytes * 90d / Math.Max(1, totalBytes))),
                index,
                entries.Count,
                completedBytes,
                totalBytes,
                relative));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var source = entry.Open())
            using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                source.CopyTo(target);
            }

            completedBytes += entry.Length;
        }

        progress?.Report(new FolderBackupProgress("解压完成。", 100, entries.Count, entries.Count, totalBytes, totalBytes, null));
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // 清不掉就算了：报错信息里已经带上真正的原因，别用一个清理异常把它盖掉。
        }
    }

    private static void WriteProjectMeta(string projectPath, Action<ProjectMeta> update)
    {
        var toolsPath = Path.Combine(projectPath, ToolsFolderName);
        Directory.CreateDirectory(toolsPath);
        var metaPath = Path.Combine(toolsPath, ProjectMetaFileName);
        var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
        update(meta);
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));
    }

    private static void UpdateProjectAssetLibraryReferenceIfMatches(
        ProjectInfo project,
        string folderName,
        string name,
        Action<ProjectMeta> update)
    {
        var metaPath = Path.Combine(project.Path, ToolsFolderName, ProjectMetaFileName);
        var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
        if (!string.Equals(meta.AssetLibraryFolderName, folderName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(meta.AssetLibraryName, name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        update(meta);
        meta.LastEditedAt = DateTime.Now;
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));
    }

    private static void WriteAssetLibraryMeta(string assetLibraryPath, Action<AssetLibraryMeta> update)
    {
        var toolsPath = Path.Combine(assetLibraryPath, ToolsFolderName);
        Directory.CreateDirectory(toolsPath);
        var metaPath = Path.Combine(toolsPath, AssetLibraryMetaFileName);
        var meta = ReadJson<AssetLibraryMeta>(metaPath) ?? new AssetLibraryMeta();
        update(meta);
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));
    }

    private static void WriteChapterMeta(string chapterPath, ChapterEditorInput input)
    {
        WriteChapterMeta(chapterPath, meta =>
        {
            meta.ChapterName = input.Name;
            meta.ChapterCode = input.Code;
            meta.ChapterType = input.Type;
            meta.LastEditedAt = DateTime.Now;
        });
    }

    private static void WriteChapterMeta(string chapterPath, Action<ChapterMeta> update)
    {
        var metaPath = Path.Combine(chapterPath, ChapterMetaFileName);
        var meta = ReadJson<ChapterMeta>(metaPath) ?? new ChapterMeta();
        update(meta);
        if (meta.LastEditedAt == default)
        {
            meta.LastEditedAt = DateTime.Now;
        }

        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));
    }

    private static string? CopyThumbnailToTools(string? sourcePath, string toolsPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        var extension = Path.GetExtension(sourcePath);
        var thumbnailFileName = $"thumbnail{extension}";
        File.Copy(sourcePath, Path.Combine(toolsPath, thumbnailFileName), overwrite: true);
        return thumbnailFileName;
    }

    private static string? ResolveThumbnailPath(string toolsPath, string? thumbnailFileName)
    {
        if (string.IsNullOrWhiteSpace(thumbnailFileName))
        {
            return null;
        }

        var thumbnailPath = Path.Combine(toolsPath, thumbnailFileName);
        return File.Exists(thumbnailPath) ? thumbnailPath : null;
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// 从**内存里的一段 JSON** 反序列化（导入时身份文件是从 zip 条目里直接读出来的，
    /// 还没落盘）。容错口径和 <see cref="ReadJson{T}"/> 一致：读不动就当没有。
    /// </summary>
    private static T? ReadJsonText<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }
}
