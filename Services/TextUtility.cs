using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GalExcleTools.Services;

internal static class TextUtility
{
    public static string NormalizeBackupNote(string? note)
    {
        return NormalizeSingleLineText(note);
    }

    public static string NormalizeFunctionChoiceNote(string? note)
    {
        return NormalizeSingleLineText(note);
    }

    public static string NormalizeSingleLineText(string? text)
    {
        return Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
    }

    public static string SanitizeBackupFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = NormalizeBackupNote(value);
        var sanitized = new string(normalized.Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Backup";
        }

        return sanitized.Length > 40 ? sanitized[..40] : sanitized;
    }

    public static string SanitizeImportedRootFolderName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string((value ?? string.Empty).Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"ImportedProject_{DateTime.Now:yyyyMMdd_HHmmss}"
            : sanitized;
    }

    public static string BuildPrefixedFolderName(string displayName, string prefix)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string((displayName ?? string.Empty).Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
        if (sanitized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return sanitized;
        }

        return $"{prefix}{sanitized}";
    }

    public static string SanitizeRemark(string remark)
    {
        var invalidChars = Path.GetInvalidFileNameChars().Concat(['_']).ToHashSet();
        return new string(remark.Trim().Where(ch => !invalidChars.Contains(ch)).ToArray());
    }

    public static string SanitizeCharacterFolderName(string code)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(code.Trim().Where(ch => !invalidChars.Contains(ch)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? $"Character_{Guid.NewGuid():N}" : sanitized;
    }

    public static string SanitizeUnrealAssetName(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), "\\s+", "_");
        sanitized = Regex.Replace(sanitized, "[^\\p{L}\\p{N}_\\-锛堬級()]+", "_");
        return string.IsNullOrWhiteSpace(sanitized) ? "Asset" : sanitized;
    }

    public static int ParseInt(string value)
    {
        return int.TryParse(value, out var result) ? result : 0;
    }

    public static string EscapeCsvField(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    public static string EscapeCsv(string value)
    {
        return EscapeCsvField(value);
    }

    public static string FormatElapsedTime(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    public static string TrimLongText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
