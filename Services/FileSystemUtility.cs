using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GalExcleTools.Services;

internal static class FileSystemUtility
{
    public static string FormatFileSize(long byteCount)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)byteCount;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{byteCount} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
    }

    public static long CountDirectoryBytes(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(filePath).Length;
                }
                catch
                {
                    // Ignore files that are locked or disappear during the scan.
                }
            }
        }
        catch
        {
            return total;
        }

        return total;
    }

    public static bool HashesEqual(string leftPath, string rightPath)
    {
        using var hashAlgorithm = SHA256.Create();
        using var leftStream = File.OpenRead(leftPath);
        using var rightStream = File.OpenRead(rightPath);

        return hashAlgorithm.ComputeHash(leftStream).SequenceEqual(hashAlgorithm.ComputeHash(rightStream));
    }

    public static string ComputeFileHash(string path)
    {
        using var hashAlgorithm = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(hashAlgorithm.ComputeHash(stream));
    }

    public static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public static bool PathsEqual(string leftPath, string rightPath)
    {
        return string.Equals(
            TrimDirectorySeparator(Path.GetFullPath(leftPath)),
            TrimDirectorySeparator(Path.GetFullPath(rightPath)),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool PathsExactlyEqual(string leftPath, string rightPath)
    {
        return string.Equals(
            TrimDirectorySeparator(Path.GetFullPath(leftPath)),
            TrimDirectorySeparator(Path.GetFullPath(rightPath)),
            StringComparison.Ordinal);
    }

    public static bool IsPathInsideDirectory(string path, string directoryPath)
    {
        var normalizedPath = TrimDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var normalizedDirectoryPath = TrimDirectorySeparator(Path.GetFullPath(directoryPath)) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathInsideDirectoryOrEqual(string path, string directoryPath)
    {
        return PathsEqual(path, directoryPath) || IsPathInsideDirectory(path, directoryPath);
    }

    public static string TrimDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
