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

    private readonly StoryCsvService _storyCsvService;
    private readonly StorySessionService _storySessionService;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectTextDataService(
        StoryCsvService storyCsvService,
        StorySessionService storySessionService,
        JsonSerializerOptions jsonOptions)
    {
        _storyCsvService = storyCsvService;
        _storySessionService = storySessionService;
        _jsonOptions = jsonOptions;
    }

    public List<ProjectTextRow> LoadTextRows(ProjectInfo project, IEnumerable<ChapterInfo> chapters)
    {
        var rows = new List<ProjectTextRow>();
        foreach (var chapter in chapters.OrderBy(chapter => chapter.Code, StringComparer.OrdinalIgnoreCase))
        {
            var loadResult = _storySessionService.LoadRowsFromSectionFiles(chapter);
            for (var i = 0; i < loadResult.Rows.Count; i++)
            {
                var row = loadResult.Rows[i];
                var text = row.Get("Tesxt");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var rowName = string.IsNullOrWhiteSpace(row.Get("Name"))
                    ? StoryCsvService.CreateRowName(i)
                    : row.Get("Name");
                var section = loadResult.Sections.TryGetValue(rowName, out var mappedSection)
                    ? mappedSection
                    : 1;
                rows.Add(new ProjectTextRow(
                    BuildTextRowId(chapter.Code, section, rowName),
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
        return ReadJson<ProjectVoiceMapState>(GetVoiceMapPath(project)) ?? new ProjectVoiceMapState();
    }

    public void WriteVoiceMap(ProjectInfo project, ProjectVoiceMapState state)
    {
        WriteJson(GetVoiceMapPath(project), state);
    }

    public ProjectLocalizationState ReadLocalization(ProjectInfo project)
    {
        return ReadJson<ProjectLocalizationState>(GetLocalizationPath(project)) ?? new ProjectLocalizationState();
    }

    public void WriteLocalization(ProjectInfo project, ProjectLocalizationState state)
    {
        WriteJson(GetLocalizationPath(project), state);
    }

    public static string BuildTextRowId(string chapterCode, int section, string rowName)
    {
        return $"{chapterCode}#{Math.Max(1, section)}#{rowName}";
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
        File.WriteAllText(path, JsonSerializer.Serialize(value, _jsonOptions));
    }
}
