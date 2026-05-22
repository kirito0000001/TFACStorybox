using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GalExcleTools.Views;

internal static class StoryCharacterLayerChoiceFactory
{
    public static List<StoryObjectChoice> CreateChoices(
        StoryCharacterLayerSpec layerSpec,
        IReadOnlyList<string> paths,
        string? currentBodyPath,
        string? currentFacePath,
        string? currentAdornPath,
        Func<string?, string?, bool> isCompatibleWithCloth,
        Func<IReadOnlyList<string?>, IReadOnlyList<string>> buildPreviewPaths)
    {
        var choices = new List<StoryObjectChoice>();
        if (layerSpec.Kind == CharacterLayerKind.Adorn)
        {
            choices.Add(new StoryObjectChoice(
                "0",
                "0: 无装饰",
                0,
                buildPreviewPaths([currentBodyPath, currentFacePath])));
            choices.AddRange(paths
                .Select((path, index) => new { path, index })
                .Where(item => isCompatibleWithCloth(currentBodyPath, item.path))
                .Select(item =>
                    new StoryObjectChoice(
                        (item.index + 1).ToString(),
                        $"{item.index + 1}: {Path.GetFileNameWithoutExtension(item.path)}",
                        item.index + 1,
                        buildPreviewPaths([currentBodyPath, currentFacePath, item.path]))));
            return choices;
        }

        if (layerSpec.Kind == CharacterLayerKind.Cloth)
        {
            choices.AddRange(paths.Select((path, index) =>
                new StoryObjectChoice(
                    index.ToString(),
                    $"{index}: {Path.GetFileNameWithoutExtension(path)}",
                    index,
                    buildPreviewPaths(
                        [
                            path,
                            isCompatibleWithCloth(path, currentFacePath) ? currentFacePath : null,
                            isCompatibleWithCloth(path, currentAdornPath) ? currentAdornPath : null
                        ]))));
            return choices;
        }

        if (layerSpec.Kind == CharacterLayerKind.Face)
        {
            choices.AddRange(paths
                .Select((path, index) => new { path, index })
                .Where(item => isCompatibleWithCloth(currentBodyPath, item.path))
                .Select(item =>
                    new StoryObjectChoice(
                        item.index.ToString(),
                        $"{item.index}: {Path.GetFileNameWithoutExtension(item.path)}",
                        item.index,
                        buildPreviewPaths([currentBodyPath, item.path, currentAdornPath]))));
            return choices;
        }

        choices.AddRange(paths.Select((path, index) =>
            new StoryObjectChoice(
                index.ToString(),
                $"{index}: {Path.GetFileNameWithoutExtension(path)}",
                index,
                buildPreviewPaths([path]))));
        return choices;
    }

    public static List<int> GetCompatibleIndexes(
        StoryCharacterLayerSpec layerSpec,
        IReadOnlyList<string> paths,
        string? bodyPath,
        Func<string?, string?, bool> isCompatibleWithCloth)
    {
        if (layerSpec.Kind == CharacterLayerKind.Adorn)
        {
            var indexes = new List<int> { 0 };
            indexes.AddRange(paths
                .Select((path, index) => new { path, index })
                .Where(item => isCompatibleWithCloth(bodyPath, item.path))
                .Select(item => item.index + 1));
            return indexes;
        }

        if (layerSpec.Kind == CharacterLayerKind.Face)
        {
            return paths
                .Select((path, index) => new { path, index })
                .Where(item => isCompatibleWithCloth(bodyPath, item.path))
                .Select(item => item.index)
                .ToList();
        }

        return Enumerable.Range(0, paths.Count).ToList();
    }

    public static string GetDisplayName(
        StoryCharacterLayerSpec layerSpec,
        IReadOnlyList<string> paths,
        int selectedIndex)
    {
        if (layerSpec.Kind == CharacterLayerKind.Adorn)
        {
            return selectedIndex == 0
                ? "无装饰"
                : Path.GetFileNameWithoutExtension(paths[Math.Clamp(selectedIndex - 1, 0, paths.Count - 1)]);
        }

        return Path.GetFileNameWithoutExtension(paths[Math.Clamp(selectedIndex, 0, paths.Count - 1)]);
    }
}
