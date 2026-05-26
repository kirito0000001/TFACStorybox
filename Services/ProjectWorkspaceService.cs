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

    public ProjectInfo ImportProjectArchive(string projectRootPath, string archivePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("项目包不存在。", archivePath);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "GalExcleTools", "ProjectImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            ZipFile.ExtractToDirectory(archivePath, tempPath);
            cancellationToken.ThrowIfCancellationRequested();
            var importedRootPath = FindImportedProjectRoot(tempPath);
            if (importedRootPath is null)
            {
                throw new InvalidDataException("这个 zip 里没有找到 Tools/project.meta.json，无法识别为项目包。");
            }

            var metaPath = Path.Combine(importedRootPath, ToolsFolderName, ProjectMetaFileName);
            var meta = ReadJson<ProjectMeta>(metaPath) ?? new ProjectMeta();
            var projectName = string.IsNullOrWhiteSpace(meta.ProjectName)
                ? Path.GetFileNameWithoutExtension(archivePath)
                : meta.ProjectName;
            var projectCode = string.IsNullOrWhiteSpace(meta.ProjectCode)
                ? Path.GetFileNameWithoutExtension(archivePath)
                : meta.ProjectCode;
            var sourceFolderName = FileSystemUtility.PathsEqual(importedRootPath, tempPath)
                ? $"{ProjectFolderPrefix}{projectName}"
                : Path.GetFileName(importedRootPath);
            var targetFolderName = TextUtility.SanitizeImportedRootFolderName(sourceFolderName);
            var targetPath = GetUniqueDirectoryPath(projectRootPath, targetFolderName);

            Directory.CreateDirectory(targetPath);
            cancellationToken.ThrowIfCancellationRequested();
            CopyDirectoryContents(importedRootPath, targetPath);

            WriteProjectMeta(targetPath, importedMeta =>
            {
                importedMeta.ProjectName = string.IsNullOrWhiteSpace(importedMeta.ProjectName) ? projectName : importedMeta.ProjectName;
                importedMeta.ProjectCode = string.IsNullOrWhiteSpace(importedMeta.ProjectCode) ? projectCode : importedMeta.ProjectCode;
                importedMeta.LastEditedAt = DateTime.Now;
            });

            return ReadProjectInfo(targetPath);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    public AssetLibraryInfo ImportAssetLibraryArchive(string projectRootPath, string archivePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("素材库包不存在。", archivePath);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "GalExcleTools", "AssetLibraryImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            ZipFile.ExtractToDirectory(archivePath, tempPath);
            cancellationToken.ThrowIfCancellationRequested();
            var importedRootPath = FindImportedAssetLibraryRoot(tempPath);
            if (importedRootPath is null)
            {
                throw new InvalidDataException("这个 zip 里没有找到 Tools/asset-library.meta.json，无法识别为素材库包。");
            }

            var metaPath = Path.Combine(importedRootPath, ToolsFolderName, AssetLibraryMetaFileName);
            var meta = ReadJson<AssetLibraryMeta>(metaPath) ?? new AssetLibraryMeta();
            var assetLibraryName = string.IsNullOrWhiteSpace(meta.AssetLibraryName)
                ? Path.GetFileNameWithoutExtension(archivePath)
                : meta.AssetLibraryName;
            var sourceFolderName = FileSystemUtility.PathsEqual(importedRootPath, tempPath)
                ? TextUtility.BuildPrefixedFolderName(assetLibraryName, AssetLibraryFolderPrefix)
                : Path.GetFileName(importedRootPath);
            var targetFolderName = TextUtility.BuildPrefixedFolderName(
                TextUtility.SanitizeImportedRootFolderName(sourceFolderName),
                AssetLibraryFolderPrefix);
            var targetPath = GetUniqueDirectoryPath(projectRootPath, targetFolderName);

            Directory.CreateDirectory(targetPath);
            cancellationToken.ThrowIfCancellationRequested();
            CopyDirectoryContents(importedRootPath, targetPath);
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
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
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

    private static void CopyDirectoryContents(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var directoryPath in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var targetFilePath = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            File.Copy(filePath, targetFilePath, overwrite: true);
        }
    }

    private static string? FindImportedAssetLibraryRoot(string extractedRootPath)
    {
        if (File.Exists(Path.Combine(extractedRootPath, ToolsFolderName, AssetLibraryMetaFileName)))
        {
            return extractedRootPath;
        }

        return Directory
            .EnumerateDirectories(extractedRootPath, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, ToolsFolderName, AssetLibraryMetaFileName)));
    }

    private static string? FindImportedProjectRoot(string extractedRootPath)
    {
        if (File.Exists(Path.Combine(extractedRootPath, ToolsFolderName, ProjectMetaFileName)))
        {
            return extractedRootPath;
        }

        return Directory
            .EnumerateDirectories(extractedRootPath, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, ToolsFolderName, ProjectMetaFileName)));
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
}
