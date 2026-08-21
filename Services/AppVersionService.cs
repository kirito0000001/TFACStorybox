using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GalExcleTools.Services;

internal static partial class AppVersionService
{
    internal const string OverrideFileName = "ax-app-version.json";
    private const string DefaultVersion = "0.0.0";

    public static string GetDisplayVersion()
    {
        var overrideVersion = ReadOverrideVersion();
        return overrideVersion ?? ReadAssemblyVersion();
    }

    private static string? ReadOverrideVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, OverrideFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Version", out var versionElement))
            {
                return NormalizeVersion(versionElement.GetString());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        return null;
    }

    private static string ReadAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersionService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var normalized = NormalizeVersion(informationalVersion?.Split('+', 2)[0]);
        if (normalized is not null)
        {
            return normalized;
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null
            ? DefaultVersion
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(assemblyVersion.Build, 0)}";
    }

    private static string? NormalizeVersion(string? version)
    {
        var trimmed = version?.Trim();
        return trimmed is not null && DisplayVersionPattern().IsMatch(trimmed)
            ? trimmed
            : null;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")]
    private static partial Regex DisplayVersionPattern();
}
