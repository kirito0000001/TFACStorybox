using System;
using System.IO;
using System.Text.Json;

namespace GalExcleTools.Services;

internal sealed class AppSettingsService
{
    public const string ProjectRootFolderName = "GalExcelProject";
    public const string DefaultProjectRootPath = @"D:\GalExcelProject";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GalExcleTools");

    public string SettingsFilePath => Path.Combine(SettingsDirectoryPath, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var settingsJson = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(settingsJson) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectoryPath);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public string ResolveProjectRootPath(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.ProjectRootPath)
            ? DefaultProjectRootPath
            : settings.ProjectRootPath;
    }

    public void EnsureProjectRootDirectory(string projectRootPath)
    {
        Directory.CreateDirectory(projectRootPath);
    }

    public string BuildProjectRootPathFromParent(string parentPath)
    {
        return Path.GetFullPath(Path.Combine(parentPath, ProjectRootFolderName));
    }
}
