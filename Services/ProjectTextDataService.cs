using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GalExcleTools.Services;

internal sealed class ProjectTextDataService
{
    private const string ToolsFolderName = "Tools";
    private const string VoiceMapFileName = "story.voice-map.json";
    private const string LocalizationFileName = "story.localization.json";

    private readonly StoryDocumentRepository _storyDocumentRepository;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectTextDataService(
        StoryDocumentRepository storyDocumentRepository,
        JsonSerializerOptions jsonOptions)
    {
        _storyDocumentRepository = storyDocumentRepository;
        _jsonOptions = jsonOptions;
    }

    public List<ProjectTextRow> LoadTextRows(ProjectInfo project, IEnumerable<ChapterInfo> chapters)
    {
        var rows = new List<ProjectTextRow>();
        foreach (var chapter in chapters.OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase))
        {
            var document = _storyDocumentRepository.Load(chapter);
            for (var i = 0; i < document.Rows.Count; i++)
            {
                var row = document.Rows[i];
                var text = row.Get("Tesxt");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var rowName = string.IsNullOrWhiteSpace(row.Get("Name"))
                    ? StoryCsvService.CreateRowName(i)
                    : row.Get("Name");
                var section = document.RowSections.TryGetValue(rowName, out var mappedSection)
                    ? mappedSection
                    : 1;
                rows.Add(new ProjectTextRow(
                    BuildStableTextRowId(chapter.Code, row.StableId),
                    row.StableId,
                    chapter.Code,
                    chapter.Name,
                    section,
                    rowName,
                    text));
            }
        }

        return rows;
    }

    public static int CountStoryCharacters(IEnumerable<ProjectTextRow> rows)
    {
        return rows.Sum(row => CountStoryTextCharacters(row.Text));
    }

    private static int CountStoryTextCharacters(string text)
    {
        return string.IsNullOrEmpty(text)
            ? 0
            : text.Count(character => !char.IsWhiteSpace(character));
    }

    public ProjectVoiceMapState ReadVoiceMap(ProjectInfo project)
    {
        var storedState = ReadJson<ProjectVoiceMapState>(GetVoiceMapPath(project)) ?? new ProjectVoiceMapState();
        return ResolveVoiceMapPaths(project, storedState);
    }

    public void WriteVoiceMap(ProjectInfo project, ProjectVoiceMapState state)
    {
        WriteJson(GetVoiceMapPath(project), CreatePortableVoiceMap(project, state));
    }

    private static ProjectVoiceMapState ResolveVoiceMapPaths(ProjectInfo project, ProjectVoiceMapState storedState)
    {
        var resolvedState = new ProjectVoiceMapState();
        foreach (var pair in storedState.Voices)
        {
            resolvedState.Voices[pair.Key] = string.IsNullOrWhiteSpace(pair.Value) || Path.IsPathRooted(pair.Value)
                ? pair.Value
                : Path.GetFullPath(Path.Combine(project.Path, pair.Value));
        }

        return resolvedState;
    }

    private static ProjectVoiceMapState CreatePortableVoiceMap(ProjectInfo project, ProjectVoiceMapState state)
    {
        var portableState = new ProjectVoiceMapState();
        var projectRoot = Path.GetFullPath(project.Path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var projectPrefix = projectRoot + Path.DirectorySeparatorChar;

        foreach (var pair in state.Voices)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                portableState.Voices[pair.Key] = pair.Value;
                continue;
            }

            var fullPath = Path.GetFullPath(pair.Value);
            portableState.Voices[pair.Key] = fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(project.Path, fullPath)
                : pair.Value;
        }

        return portableState;
    }

    public ProjectLocalizationState ReadLocalization(ProjectInfo project)
    {
        return ReadJson<ProjectLocalizationState>(GetLocalizationPath(project)) ?? new ProjectLocalizationState();
    }

    public void WriteLocalization(ProjectInfo project, ProjectLocalizationState state)
    {
        WriteJson(GetLocalizationPath(project), state);
    }

    public static string BuildExportRowKey(string chapterCode, int section, string rowName)
    {
        return $"{chapterCode}#{Math.Max(1, section)}#{rowName}";
    }

    public static string BuildStableTextRowId(string chapterCode, string stableRowId)
    {
        return $"{chapterCode}#id#{stableRowId}";
    }

    private static string GetVoiceMapPath(ProjectInfo project)
    {
        return Path.Combine(project.Path, ToolsFolderName, VoiceMapFileName);
    }

    private static string GetLocalizationPath(ProjectInfo project)
    {
        return Path.Combine(project.Path, ToolsFolderName, LocalizationFileName);
    }

    private T? ReadJson<T>(string path)
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

    private void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, _jsonOptions));
            File.Move(tempPath, path, true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
