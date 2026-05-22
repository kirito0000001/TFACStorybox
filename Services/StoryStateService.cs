using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools.Services;

internal sealed class StoryStateService
{
    private readonly JsonSerializerOptions _jsonOptions;

    public StoryStateService()
        : this(new JsonSerializerOptions { WriteIndented = true })
    {
    }

    public StoryStateService(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public Dictionary<string, int> ReadSectionMap(ChapterInfo chapter)
    {
        var state = ReadJson<StorySectionState>(GetStorySectionsPath(chapter));
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (state?.Rows is null)
        {
            return result;
        }

        foreach (var pair in state.Rows)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                result[pair.Key] = Math.Max(1, pair.Value);
            }
        }

        return result;
    }

    public void WriteSectionState(string chapterPath, IReadOnlyDictionary<string, int> sections)
    {
        Directory.CreateDirectory(chapterPath);
        var state = new StorySectionState();
        foreach (var pair in sections.OrderBy(pair => ParseInt(pair.Key)))
        {
            state.Rows[pair.Key] = Math.Max(1, pair.Value);
        }

        File.WriteAllText(
            Path.Combine(chapterPath, "story.sections.json"),
            JsonSerializer.Serialize(state, _jsonOptions),
            Encoding.UTF8);
    }

    public StoryChoiceNoteState ReadChoiceNotes(ChapterInfo chapter)
    {
        var state = ReadJson<StoryChoiceNoteState>(GetStoryChoiceNotesPath(chapter)) ?? new StoryChoiceNoteState();
        state.Choices = new Dictionary<string, List<string>>(
            state.Choices
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => new KeyValuePair<string, List<string>>(
                    pair.Key.Trim(),
                    (pair.Value ?? [])
                        .Select(NormalizeFunctionChoiceNote)
                        .ToList())),
            StringComparer.OrdinalIgnoreCase);
        return state;
    }

    public void WriteChoiceNotes(ChapterInfo chapter, StoryChoiceNoteState state)
    {
        File.WriteAllText(GetStoryChoiceNotesPath(chapter), JsonSerializer.Serialize(state, _jsonOptions));
    }

    public void CopyChoiceNotes(ChapterInfo chapter, string oldChoice, string newChoice)
    {
        if (string.Equals(oldChoice, newChoice, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var state = ReadChoiceNotes(chapter);
        if (!state.Choices.TryGetValue(oldChoice, out var oldNotes) || oldNotes.Count == 0)
        {
            return;
        }

        if (!state.Choices.TryGetValue(newChoice, out var newNotes) || newNotes.Count == 0)
        {
            state.Choices[newChoice] = oldNotes.ToList();
        }

        WriteChoiceNotes(chapter, state);
    }

    public bool RemoveChoiceNotes(ChapterInfo chapter, IEnumerable<string> choices)
    {
        var state = ReadChoiceNotes(chapter);
        var changed = false;
        foreach (var choice in choices.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            changed |= state.Choices.Remove(choice);
        }

        if (changed)
        {
            WriteChoiceNotes(chapter, state);
        }

        return changed;
    }

    public static StoryChoiceNoteState CloneChoiceNotes(StoryChoiceNoteState state)
    {
        var clone = new StoryChoiceNoteState();
        foreach (var pair in state.Choices)
        {
            clone.Choices[pair.Key] = pair.Value.ToList();
        }

        return clone;
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
