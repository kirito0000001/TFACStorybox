using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools.Services;

internal sealed class CharacterFilterService
{
    private readonly JsonSerializerOptions _jsonOptions;

    public CharacterFilterService()
        : this(new JsonSerializerOptions { WriteIndented = true })
    {
    }

    public CharacterFilterService(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public List<CharacterFilterEntry> Read(AssetLibraryInfo assetLibrary)
    {
        return Normalize(ReadStored(assetLibrary));
    }

    public List<CharacterFilterEntry> ReadStored(AssetLibraryInfo assetLibrary)
    {
        var folderPath = GetCharacterFilterFolderPath(assetLibrary);
        Directory.CreateDirectory(folderPath);
        var indexPath = GetCharacterFilterIndexPath(assetLibrary);
        CharacterFilterIndex? index = null;
        if (File.Exists(indexPath))
        {
            try
            {
                index = JsonSerializer.Deserialize<CharacterFilterIndex>(File.ReadAllText(indexPath));
            }
            catch
            {
                index = null;
            }
        }

        if (index?.Entries is not { Count: > 0 })
        {
            var defaults = CreateDefault();
            Write(assetLibrary, defaults);
            return defaults;
        }

        return index.Entries.ToList();
    }

    public void Write(AssetLibraryInfo assetLibrary, IReadOnlyList<CharacterFilterEntry> filters)
    {
        var folderPath = GetCharacterFilterFolderPath(assetLibrary);
        Directory.CreateDirectory(folderPath);
        var index = new CharacterFilterIndex
        {
            Entries = filters.ToList()
        };
        File.WriteAllText(GetCharacterFilterIndexPath(assetLibrary), JsonSerializer.Serialize(index, _jsonOptions));
    }

    public static List<CharacterFilterEntry> CreateDefault()
    {
        return
        [
            CreateEmpty(),
            new CharacterFilterEntry("default-cool-rain", "冷色调（下雨）"),
            new CharacterFilterEntry("default-warm-dusk", "暖色调（黄昏）"),
            new CharacterFilterEntry("default-half-black-mask", "上半身黑遮罩")
        ];
    }

    public static CharacterFilterEntry CreateEmpty()
    {
        return new CharacterFilterEntry("default-none", "空");
    }

    public static List<CharacterFilterEntry> Normalize(IEnumerable<CharacterFilterEntry> filters)
    {
        var normalized = new List<CharacterFilterEntry> { CreateEmpty() };
        foreach (var filter in filters)
        {
            var remark = (filter.Remark ?? string.Empty).Trim();
            var current = filter with
            {
                Id = string.IsNullOrWhiteSpace(filter.Id) ? Guid.NewGuid().ToString("N") : filter.Id.Trim(),
                Remark = remark
            };
            if (IsEmpty(current) || string.IsNullOrWhiteSpace(current.Remark))
            {
                continue;
            }

            normalized.Add(current);
        }

        return normalized;
    }

    public static bool IsEmpty(CharacterFilterEntry filter)
    {
        return string.Equals(filter.Id, "default-none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(filter.Remark, "空", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDisplayName(CharacterFilterEntry filter, int index)
    {
        return $"VFX{index:00}_{filter.Remark}";
    }

    public static Dictionary<int, int> BuildIndexRemap(
        IReadOnlyList<CharacterFilterEntry> oldFilters,
        IReadOnlyList<CharacterFilterEntry> newFilters)
    {
        var newIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < newFilters.Count; i++)
        {
            if (!newIndexes.ContainsKey(newFilters[i].Id))
            {
                newIndexes[newFilters[i].Id] = i;
            }
        }

        var result = new Dictionary<int, int>();
        for (var oldIndex = 0; oldIndex < oldFilters.Count; oldIndex++)
        {
            var filter = oldFilters[oldIndex];
            if (newIndexes.TryGetValue(filter.Id, out var newIndex))
            {
                if (oldIndex != newIndex)
                {
                    result[oldIndex] = newIndex;
                }
            }
            else if (oldIndex != 0)
            {
                result[oldIndex] = 0;
            }
        }

        return result;
    }
}
