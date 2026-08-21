using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services;

internal sealed class UpdateService
{
    public const string ToolboxStableKey = "GalExcleTools";
    public const string DisplayName = "TFAC剧情箱-轮椅版";
    public const string EntryExeName = "TFAC剧情箱-轮椅版.exe";
    public const string RuntimeIdentifier = "win-x64";
    public const string ManifestAssetName = "toolbox-update.json";
    public const string GiteeReleaseApiUrl = "https://gitee.com/api/v5/repos/xiaojie578/TFACStorybox/releases";
    public const string GiteeReleasePageUrl = "https://gitee.com/xiaojie578/TFACStorybox/releases";
    public const string GitHubReleaseApiUrl = "https://api.github.com/repos/kirito0000001/TFACStorybox/releases";
    public const string GitHubReleasePageUrl = "https://github.com/kirito0000001/TFACStorybox/releases";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly string _currentVersion = AppVersionService.GetDisplayVersion();

    public string UpdatesDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ToolboxStableKey,
        "Updates");

    public async Task<UpdateCheckResult> CheckAsync(
        UpdateChannel channel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var source in new[] { UpdateSource.Gitee, UpdateSource.GitHub })
        {
            try
            {
                var result = await CheckSourceAsync(source, channel, timeoutSeconds, cancellationToken);
                if (result is not null)
                {
                    return result;
                }

                failures.Add($"{GetSourceDisplayName(source)} 没有可用 Release");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{GetSourceDisplayName(source)}：{ex.Message}");
            }
        }

        return new UpdateCheckResult(
            false,
            _currentVersion,
            null,
            null,
            null,
            UpdateSource.Gitee,
            "未找到可用更新。" + string.Join("；", failures),
            string.Empty);
    }

    public async Task<UpdateConnectionResult> TestConnectionAsync(
        UpdateChannel channel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await CheckAsync(channel, timeoutSeconds, cancellationToken);
        stopwatch.Stop();
        return new UpdateConnectionResult(
            result.Manifest is not null,
            result.Source,
            stopwatch.Elapsed,
            result.Manifest is null
                ? result.Message
                : $"{GetSourceDisplayName(result.Source)} 连接正常，远端版本 {result.LatestVersion ?? result.Manifest.Version}。");
    }

    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateManifest manifest,
        UpdateAssetManifest asset,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(UpdatesDirectoryPath);
        var fileName = Path.GetFileName(asset.FileName);
        if (!string.Equals(fileName, asset.FileName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("更新包文件名不安全。");
        }

        var packagePath = Path.Combine(UpdatesDirectoryPath, fileName);
        var downloadPath = packagePath + ".download";
        if (File.Exists(packagePath))
        {
            try
            {
                await ValidateDownloadedFileAsync(packagePath, manifest, asset, cancellationToken);
                progress?.Report(new ProgressUpdate("已复用校验通过的更新包。", 100, fileName));
                return new UpdateDownloadResult(packagePath, manifest, asset);
            }
            catch
            {
                File.Delete(packagePath);
            }
        }

        var existingLength = File.Exists(downloadPath) ? new FileInfo(downloadPath).Length : 0;
        if (asset.SizeBytes > 0 && existingLength > asset.SizeBytes)
        {
            File.Delete(downloadPath);
            existingLength = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (existingLength > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            existingLength = 0;
        }

        response.EnsureSuccessStatusCode();
        var totalBytes = asset.SizeBytes > 0
            ? asset.SizeBytes
            : existingLength + (response.Content.Headers.ContentLength ?? 0);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            downloadPath,
            existingLength > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            1024 * 128,
            useAsync: true);
        var buffer = new byte[1024 * 128];
        var completed = existingLength;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            completed += read;
            var percent = totalBytes > 0 ? completed * 90d / totalBytes : 0;
            progress?.Report(new ProgressUpdate(
                "正在下载更新包...",
                Math.Clamp(percent, 0, 90),
                $"{FormatBytes(completed)} / {(totalBytes > 0 ? FormatBytes(totalBytes) : "未知")}",
                totalBytes <= 0));
        }

        await destination.FlushAsync(cancellationToken);
        File.Move(downloadPath, packagePath, true);
        progress?.Report(new ProgressUpdate("正在校验更新包...", 94, fileName));
        await ValidateDownloadedFileAsync(packagePath, manifest, asset, cancellationToken);
        progress?.Report(new ProgressUpdate("更新包校验完成。", 100, fileName));
        CleanOldPackages(packagePath);
        return new UpdateDownloadResult(packagePath, manifest, asset);
    }

    public async Task LaunchUpdaterAsync(
        string packagePath,
        UpdateManifest manifest,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var sourceUpdaterPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "热更新覆盖.ps1");
        if (!File.Exists(sourceUpdaterPath))
        {
            throw new FileNotFoundException("热更新覆盖脚本不存在。", sourceUpdaterPath);
        }

        var runnerDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ToolboxStableKey,
            "UpdateRunners");
        Directory.CreateDirectory(runnerDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var updaterPath = Path.Combine(runnerDirectory, $"Update-{stamp}.ps1");
        var readySignalPath = Path.Combine(runnerDirectory, $"Update-{stamp}.ready");
        File.Copy(sourceUpdaterPath, updaterPath, true);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            WorkingDirectory = runnerDirectory,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(updaterPath);
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("-InstallDirectory");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-ExpectedVersion");
        startInfo.ArgumentList.Add(manifest.Version);
        startInfo.ArgumentList.Add("-ReadySignalPath");
        startInfo.ArgumentList.Add(readySignalPath);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动外部更新程序。");

        progress?.Report(new ProgressUpdate("正在等待外部更新程序预检...", 100, updaterPath, true));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(readySignalPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("外部更新程序预检超时，主程序不会退出。");
            }

            await Task.Delay(150, cancellationToken);
        }
    }

    public static string GetSourceDisplayName(UpdateSource source) => source == UpdateSource.Gitee ? "Gitee" : "GitHub";

    public static string GetReleasePageUrl(UpdateSource source) => source == UpdateSource.Gitee
        ? GiteeReleasePageUrl
        : GitHubReleasePageUrl;

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private async Task<UpdateCheckResult?> CheckSourceAsync(
        UpdateSource source,
        UpdateChannel channel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 10, 180)));
        var release = source == UpdateSource.Gitee
            ? await GetGiteeReleaseAsync(channel, timeout.Token)
            : await GetGitHubReleaseAsync(channel, timeout.Token);
        if (release is null)
        {
            return null;
        }

        var manifestAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, ManifestAssetName, StringComparison.OrdinalIgnoreCase));
        if (manifestAsset is null)
        {
            return null;
        }

        var manifestJson = await HttpClient.GetStringAsync(manifestAsset.DownloadUrl, timeout.Token);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidOperationException("更新清单无法解析。");
        ValidateManifest(manifest);
        foreach (var asset in manifest.Assets)
        {
            var releaseAsset = release.Assets.FirstOrDefault(item =>
                string.Equals(item.Name, asset.FileName, StringComparison.OrdinalIgnoreCase));
            if (releaseAsset is not null)
            {
                asset.DownloadUrl = releaseAsset.DownloadUrl;
                if (asset.SizeBytes <= 0)
                {
                    asset.SizeBytes = releaseAsset.Size;
                }
            }
        }

        var latest = NormalizeVersion(manifest.Version);
        var notes = string.IsNullOrWhiteSpace(manifest.ReleaseNotes) ? release.Notes : manifest.ReleaseNotes;
        if (CompareVersions(latest, _currentVersion) <= 0)
        {
            return new UpdateCheckResult(
                false,
                _currentVersion,
                latest,
                manifest,
                null,
                source,
                $"当前已是最新版本：{_currentVersion}（{GetSourceDisplayName(source)}）",
                notes);
        }

        if (manifest.RequiresManualMigration)
        {
            return new UpdateCheckResult(
                false,
                _currentVersion,
                latest,
                manifest,
                null,
                source,
                $"发现 {latest}，该版本需要手动更新。",
                notes);
        }

        var runtimeAsset = manifest.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Runtime, RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(asset.DownloadUrl));
        return runtimeAsset is null
            ? null
            : new UpdateCheckResult(
                true,
                _currentVersion,
                latest,
                manifest,
                runtimeAsset,
                source,
                $"发现新版本：{_currentVersion} -> {latest}（{GetSourceDisplayName(source)}）",
                notes);
    }

    private static async Task<RemoteRelease?> GetGiteeReleaseAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        using var document = await GetJsonDocumentAsync(GiteeReleaseApiUrl + "?per_page=50", cancellationToken);
        var releases = new List<(int Id, string Tag, bool Prerelease, string Notes)>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var prerelease = item.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            if (channel == UpdateChannel.Stable && prerelease)
            {
                continue;
            }

            releases.Add((
                item.GetProperty("id").GetInt32(),
                item.GetProperty("tag_name").GetString() ?? string.Empty,
                prerelease,
                item.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty));
        }

        var selected = releases.OrderByDescending(item => NormalizeVersion(item.Tag), VersionStringComparer.Instance).FirstOrDefault();
        if (selected.Id == 0)
        {
            return null;
        }

        var assetsUrl = $"{GiteeReleaseApiUrl}/{selected.Id}/attach_files?per_page=100";
        using var assetsDocument = await GetJsonDocumentAsync(assetsUrl, cancellationToken);
        return new RemoteRelease(selected.Tag, selected.Notes, ReadAssets(assetsDocument.RootElement));
    }

    private static async Task<RemoteRelease?> GetGitHubReleaseAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        using var document = await GetJsonDocumentAsync(GitHubReleaseApiUrl + "?per_page=50", cancellationToken);
        var releases = new List<RemoteRelease>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var prerelease = item.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            var draft = item.TryGetProperty("draft", out var draftValue) && draftValue.GetBoolean();
            if (draft || (channel == UpdateChannel.Stable && prerelease))
            {
                continue;
            }

            releases.Add(new RemoteRelease(
                item.GetProperty("tag_name").GetString() ?? string.Empty,
                item.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                ReadAssets(item.GetProperty("assets"))));
        }

        return releases.OrderByDescending(item => NormalizeVersion(item.Tag), VersionStringComparer.Instance).FirstOrDefault();
    }

    private static List<RemoteAsset> ReadAssets(JsonElement assetsElement)
    {
        var assets = new List<RemoteAsset>();
        foreach (var asset in assetsElement.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? string.Empty : string.Empty;
            var size = asset.TryGetProperty("size", out var sizeValue) ? sizeValue.GetInt64() : 0;
            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() ?? string.Empty : string.Empty;
            assets.Add(new RemoteAsset(name, size, url));
        }

        return assets;
    }

    private static async Task<JsonDocument> GetJsonDocumentAsync(string url, CancellationToken cancellationToken)
    {
        await using var stream = await HttpClient.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.ToolboxStableKey, ToolboxStableKey, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException("更新清单身份或版本无效。");
        }
    }

    private static async Task ValidateDownloadedFileAsync(
        string packagePath,
        UpdateManifest manifest,
        UpdateAssetManifest asset,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(packagePath);
        if (asset.SizeBytes > 0 && fileInfo.Length != asset.SizeBytes)
        {
            throw new InvalidOperationException($"更新包大小不一致：应为 {asset.SizeBytes}，实际为 {fileInfo.Length}。");
        }

        await using var stream = File.OpenRead(packagePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualHash = Convert.ToHexString(hash);
        if (string.IsNullOrWhiteSpace(asset.Sha256) ||
            !actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("更新包 SHA-256 校验失败。");
        }

        ValidatePackage(packagePath, manifest, asset);
    }

    private static void ValidatePackage(string packagePath, UpdateManifest manifest, UpdateAssetManifest asset)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine("C:\\UpdateValidation", normalized));
            if (!fullPath.StartsWith("C:\\UpdateValidation" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新包包含不安全路径。");
            }
        }

        var packageManifestEntry = archive.GetEntry("update-package.json")
            ?? throw new InvalidOperationException("更新包缺少 update-package.json。");
        using var manifestStream = packageManifestEntry.Open();
        var packageManifest = JsonSerializer.Deserialize<UpdatePackageManifest>(manifestStream, JsonOptions)
            ?? throw new InvalidOperationException("更新包身份清单无法解析。");
        if (packageManifest.SchemaVersion != 1 ||
            !string.Equals(packageManifest.ToolboxStableKey, ToolboxStableKey, StringComparison.Ordinal) ||
            !string.Equals(NormalizeVersion(packageManifest.Version), NormalizeVersion(manifest.Version), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(packageManifest.Runtime, asset.Runtime, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(packageManifest.EntryExe, EntryExeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("更新包工具标识、版本或 Runtime 不匹配。");
        }

        if (archive.GetEntry(EntryExeName) is null)
        {
            throw new InvalidOperationException("更新包缺少剧情工具箱主程序。");
        }
    }

    private void CleanOldPackages(string keepPath)
    {
        foreach (var file in Directory.EnumerateFiles(UpdatesDirectoryPath, "TFACStorybox-v*-win-*.zip")
                     .Where(path => !string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase))
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Skip(1))
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Cache cleanup is best effort.
            }
        }
    }

    private static string NormalizeVersion(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith('v') || normalized.StartsWith('V') ? normalized[1..] : normalized;
    }

    private static int CompareVersions(string first, string second)
    {
        return SemanticVersion.Parse(first).CompareTo(SemanticVersion.Parse(second));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TFACStorybox-Updater/1.0");
        return client;
    }

    private sealed record RemoteRelease(string Tag, string Notes, List<RemoteAsset> Assets);

    private sealed record RemoteAsset(string Name, long Size, string DownloadUrl);

    private readonly record struct SemanticVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<SemanticVersion>
    {
        public static SemanticVersion Parse(string value)
        {
            var normalized = NormalizeVersion(value);
            var split = normalized.Split('-', 2, StringSplitOptions.TrimEntries);
            var parts = split[0].Split('.');
            return new SemanticVersion(
                parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 0,
                parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : 0,
                parts.Length > 2 && int.TryParse(parts[2], out var patch) ? patch : 0,
                split.Length > 1 ? split[1] : string.Empty);
        }

        public int CompareTo(SemanticVersion other)
        {
            var core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;
            if (Prerelease.Length == 0 && other.Prerelease.Length > 0) return 1;
            if (Prerelease.Length > 0 && other.Prerelease.Length == 0) return -1;
            return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static VersionStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            return SemanticVersion.Parse(x ?? string.Empty).CompareTo(SemanticVersion.Parse(y ?? string.Empty));
        }
    }
}
