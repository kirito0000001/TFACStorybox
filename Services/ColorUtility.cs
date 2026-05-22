using System;
using System.Text.RegularExpressions;

namespace GalExcleTools.Services;

internal static class ColorUtility
{
    public const string DefaultCharacterColorHex = "#008F8D";

    public static string NormalizeColorHex(string value)
    {
        var trimmed = value.Trim();
        if (Regex.IsMatch(trimmed, "^#?[0-9a-fA-F]{6}$"))
        {
            var normalized = trimmed.StartsWith('#') ? trimmed.ToUpperInvariant() : $"#{trimmed.ToUpperInvariant()}";
            return string.Equals(normalized, "#D9E8FF", StringComparison.OrdinalIgnoreCase) ? DefaultCharacterColorHex : normalized;
        }

        return DefaultCharacterColorHex;
    }

    public static string NormalizeLegacyCharacterColor(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultCharacterColorHex : NormalizeColorHex(value);
    }

    public static Windows.UI.Color ParseColor(string hex, Windows.UI.Color fallback)
    {
        var normalized = NormalizeColorHex(hex);
        try
        {
            return Windows.UI.Color.FromArgb(
                255,
                Convert.ToByte(normalized.Substring(1, 2), 16),
                Convert.ToByte(normalized.Substring(3, 2), 16),
                Convert.ToByte(normalized.Substring(5, 2), 16));
        }
        catch
        {
            return fallback;
        }
    }
}
