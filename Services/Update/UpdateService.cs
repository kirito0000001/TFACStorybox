using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services.Update;

/// <summary>失败映射的中间产物：一次失败该给用户哪句话、属于哪一类。</summary>
internal sealed record UpdateFailureOutcome(
    UpdateCheckStatus Status,
    string Message,
    string Detail);

/// <summary>
/// 更新链路的编排（设计 §5.1）。这里只串联：拉列表 → 挑 Release → 取附件 → 下载清单 →
/// 组 plan → 比版本。每个细节都在别人家里（选择器 / 预算 / 校验 / 客户端），出问题一眼定位。
/// </summary>
internal sealed class UpdateService
{
    /// <summary>清单固定叫这个名字（设计 §6.1），匹配时必须 OrdinalIgnoreCase。</summary>
    public const string ManifestFileName = "toolbox-update.json";

    /// <summary>固定 ASCII 标识，用于校验清单确实属于本工具（设计 §6.1）。</summary>
    public const string ToolboxStableKey = "GalExcleTools";

    private readonly GiteeReleaseClient _client;
    private readonly string _toolboxStableKey;
    private readonly UpdateCacheStore _cacheStore;
    private readonly UpdateDownloader _downloader;

    /// <param name="toolboxStableKey">
    /// 只在联调/测试里覆盖（默认就是本工具的固定标识）。生产路径永远走默认值。
    /// </param>
    /// <param name="cacheStore">缓存目录；测试可以指到临时目录，不去碰 <c>%LOCALAPPDATA%</c>。</param>
    public UpdateService(
        GiteeReleaseClient? client = null,
        string? toolboxStableKey = null,
        UpdateCacheStore? cacheStore = null)
    {
        _client = client ?? new GiteeReleaseClient();
        _toolboxStableKey = string.IsNullOrWhiteSpace(toolboxStableKey) ? ToolboxStableKey : toolboxStableKey;
        _cacheStore = cacheStore ?? new UpdateCacheStore();
        _downloader = new UpdateDownloader(_client, _cacheStore);
    }

    public GiteeRepository Repository => _client.Repository;

    /// <summary>
    /// 本次实际使用的清单标识（默认就是 <see cref="ToolboxStableKey"/>，联调/测试可能被覆盖）。
    /// 覆盖器要拿它去认包内清单，所以必须读这里而不是那个常量。
    /// </summary>
    public string StableKey => _toolboxStableKey;

    /// <summary>缓存目录（界面「打开更新缓存目录」之类的地方要用时从这里拿，别再自己拼路径）。</summary>
    public UpdateCacheStore CacheStore => _cacheStore;

    /// <summary>
    /// 检查更新（设计 §7.1）。返回值里 <see cref="UpdateCheckResult.Message"/> 就是给用户看的那句话。
    /// 除了「用户取消」，这个方法**不会**抛异常：任何失败都变成 <see cref="UpdateCheckStatus.Failed"/>。
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(UpdateCheckRequest request, CancellationToken cancellationToken)
    {
        ToolboxLog.Info(
            $"开始检查更新：通道 {request.Channel}，当前版本 {request.CurrentVersion}，runtime {request.Runtime}，仓库 {Repository}。");

        if (!UpdateVersion.TryParse(request.CurrentVersion, out _))
        {
            // fail-closed：本机版本读不出来就不敢比较，绝不能顺势说「已是最新」（设计 §11.6）。
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                $"本机版本号（{request.CurrentVersion}）读不出来，无法比较，请到 Gitee 手动下载。")
            {
                Detail = "本机版本号解析失败。"
            };
        }

        try
        {
            return await CheckCoreAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 取消不是失败（设计 §7.2），原样交给上层显示「检查已取消」。
            ToolboxLog.Info("检查更新被取消。");
            throw;
        }
        catch (GiteeRequestException error)
        {
            var outcome = MapFailure(error, request.TimeoutSeconds);
            ToolboxLog.Warn($"检查更新失败：{outcome.Message}（{outcome.Detail}）");
            return new UpdateCheckResult(outcome.Status, outcome.Message)
            {
                RateLimitRemaining = _client.LastRemainingQuota,
                Detail = outcome.Detail
            };
        }
        catch (Exception error)
        {
            // 兜底：宁可报「失败」，也不能把异常漏给界面当成「已是最新」。
            ToolboxLog.Error($"检查更新时发生未预期的错误：{error.Message}", error);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, "检查更新失败，请稍后重试或到 Gitee 手动下载。")
            {
                Detail = error.Message
            };
        }
    }

    /// <summary>「测试 Gitee 连接」：只发一个列表请求，顺带把配额和 Release 数量报出来。</summary>
    public async Task<UpdateConnectionTestResult> TestConnectionAsync(int timeoutSeconds, CancellationToken cancellationToken)
    {
        try
        {
            var releases = await _client.ListReleasesAsync(timeoutSeconds, cancellationToken);
            var remaining = _client.LastRemainingQuota;
            var quotaText = remaining is { } quota ? $"，剩余配额 {quota} 次" : string.Empty;
            var message = $"Gitee 连接正常：仓库 {Repository} 里有 {releases.Count} 个 Release{quotaText}。";
            ToolboxLog.Info(message);
            return new UpdateConnectionTestResult(true, message) { RemainingQuota = remaining };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GiteeRequestException error)
        {
            var outcome = MapFailure(error, timeoutSeconds);
            ToolboxLog.Warn($"测试 Gitee 连接失败：{outcome.Message}（{outcome.Detail}）");
            return new UpdateConnectionTestResult(false, outcome.Message)
            {
                RemainingQuota = _client.LastRemainingQuota
            };
        }
        catch (Exception error)
        {
            ToolboxLog.Error($"测试 Gitee 连接时发生未预期的错误：{error.Message}", error);
            return new UpdateConnectionTestResult(false, "测试连接失败，请稍后重试。");
        }
    }

    /// <summary>
    /// 下载并校验更新包（设计 §8.1）。**不含** ⓪ 多实例预检 —— 按设计那一步属于覆盖链路
    /// （M4，设计 §9.7）：下一个包不影响正在运行的另一个实例，但覆盖会。
    ///
    /// 除了「用户取消」，这里不会抛异常（和 <see cref="CheckForUpdateAsync"/> 一个规矩）。
    /// </summary>
    public Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdatePackagePlan plan,
        int preflightTimeoutSeconds,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken) =>
        _downloader.DownloadAndVerifyAsync(plan, _toolboxStableKey, preflightTimeoutSeconds, progress, cancellationToken);

    /// <summary>清掉半截包、只保留最近两个包（设计 §8.1 ⑩）。它自己不会抛，可以随手调。</summary>
    public void CleanUpdateCache()
    {
        _cacheStore.DeleteDownloadLeftovers();
        var removed = _cacheStore.TrimPackages();
        if (removed > 0)
        {
            ToolboxLog.Info($"清理更新包缓存，删掉 {removed} 个旧包。");
        }
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(UpdateCheckRequest request, CancellationToken cancellationToken)
    {
        var releases = await _client.ListReleasesAsync(request.TimeoutSeconds, cancellationToken);
        var remaining = _client.LastRemainingQuota;
        ToolboxLog.Info($"Gitee Release 列表拿到 {releases.Count} 条，剩余配额 {(remaining is { } r ? r.ToString(CultureInfo.InvariantCulture) : "未知")}。");


        var release = UpdateChannelSelector.SelectRelease(releases, request.Channel);
        if (release is null)
        {
            var message = $"Gitee 上没有找到可用的 Release。{Repository.ReleasePageUrl}";
            return new UpdateCheckResult(UpdateCheckStatus.NoRelease, message)
            {
                RateLimitRemaining = remaining,
                ReleaseNotesUrl = Repository.ReleasePageUrl,
                Detail = $"列表 {releases.Count} 条，通道 {request.Channel} 内没有可用 Release。"
            };
        }

        var attachments = await _client.ListAttachmentsAsync(release.Id, request.TimeoutSeconds, cancellationToken);
        var noteFallback = string.IsNullOrWhiteSpace(release.Body) ? null : release.Body;
        var notesUrlFallback = string.IsNullOrWhiteSpace(release.HtmlUrl)
            ? Repository.BuildReleaseTagUrl(release.TagName ?? string.Empty)
            : release.HtmlUrl;

        var manifestAttachment = attachments.FirstOrDefault(attachment =>
            string.Equals(attachment.Name?.Trim(), ManifestFileName, StringComparison.OrdinalIgnoreCase));

        if (manifestAttachment?.BrowserDownloadUrl is not { Length: > 0 } manifestUrl)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.ManifestMissing,
                $"发现 {release.TagName}，但该 Release 没有更新清单，请手动下载。")
            {
                ReleaseTag = release.TagName,
                ReleaseName = release.Name,
                ReleaseNotes = noteFallback,
                ReleaseNotesUrl = notesUrlFallback,
                RateLimitRemaining = remaining,
                Detail = $"Release {release.TagName} 的 {attachments.Count} 个附件里没有 {ManifestFileName}。"
            };
        }

        var manifestJson = await _client.DownloadTextAsync(manifestUrl, request.TimeoutSeconds, cancellationToken);
        var manifest = ParseManifest(manifestJson, release.TagName, out var parseFailure);
        if (manifest is null)
        {
            return parseFailure!;
        }

        var stableKey = manifest.ToolboxStableKey?.Trim();
        if (!string.Equals(stableKey, _toolboxStableKey, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                $"更新清单不属于本工具（toolboxStableKey = {stableKey ?? "空"}），已忽略。")
            {
                ReleaseTag = release.TagName,
                Detail = $"期望 {_toolboxStableKey}，实际 {stableKey ?? "空"}。"
            };
        }

        var version = UpdateVersion.NormalizeVersionText(manifest.Version);
        if (!UpdateVersion.TryParse(version, out _))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                $"更新清单里的版本号（{manifest.Version ?? "空"}）读不出来，请手动下载。")
            {
                ReleaseTag = release.TagName,
                Detail = "清单 version 字段解析失败。"
            };
        }

        var notes = string.IsNullOrWhiteSpace(manifest.ReleaseNotes) ? noteFallback : manifest.ReleaseNotes;
        var notesUrl = string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl) ? notesUrlFallback : manifest.ReleaseNotesUrl;

        var asset = manifest.FindAsset(request.Runtime);
        if (asset is null)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.AssetMissing,
                $"发现 {version}，但没有 {request.Runtime} 更新包。")
            {
                LatestVersion = version,
                ReleaseTag = release.TagName,
                ReleaseName = release.Name,
                ReleaseNotes = notes,
                ReleaseNotesUrl = notesUrl,
                RateLimitRemaining = remaining,
                Detail = $"清单里只有：{string.Join("、", (manifest.Assets ?? []).Select(item => item.Runtime ?? "?"))}。"
            };
        }

        if (manifest.RequiresManualMigration)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.ManualMigrationRequired,
                $"发现 {version}，但该版本需要手动更新。")
            {
                LatestVersion = version,
                ReleaseTag = release.TagName,
                ReleaseName = release.Name,
                ReleaseNotes = notes,
                ReleaseNotesUrl = notesUrl,
                RequiresManualMigration = true,
                RateLimitRemaining = remaining,
                Detail = "清单 requiresManualMigration = true。"
            };
        }

        // ★ 增量包（2026-10-06）：本机版本正好等于基准版、且增量包确实更小 → 直接下增量。
        //   任何一条不满足都走整包（老清单没有 delta 字段；跨版升级基准版对不上；增量反而更大）。
        var useDelta = false;
        string? deltaBaseVersion = null;
        var delta = manifest.Delta;
        if (delta is not null
            && !string.IsNullOrWhiteSpace(delta.FileName)
            && !string.IsNullOrWhiteSpace(delta.BaseVersion)
            && delta.SizeBytes > 0
            && UpdatePackagePlan.NormalizeSha256(delta.Sha256).Length == 64
            && string.Equals(delta.BaseVersion.Trim(), request.CurrentVersion?.Trim(), StringComparison.OrdinalIgnoreCase)
            && (asset.SizeBytes <= 0 || delta.SizeBytes < asset.SizeBytes))
        {
            useDelta = true;
            deltaBaseVersion = delta.BaseVersion.Trim();
        }
        else if (delta is not null)
        {
            ToolboxLog.Info($"清单里有增量包（基准 v{delta.BaseVersion}），但本机 v{request.CurrentVersion} 不适用或增量更大，改走整包。");
        }

        var fileName = useDelta ? delta!.FileName!.Trim() : asset.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                $"更新清单里没有写包文件名（{version}），请手动下载。")
            {
                LatestVersion = version,
                ReleaseTag = release.TagName,
                Detail = "清单 assets[].fileName 为空。"
            };
        }

        // ★ 分卷（2026-10-06 契约 §3）：parts 非空就逐卷找附件 —— 此时**整包名自己不是附件**，
        //   别拿 fileName 去附件列表里找它（找不到是正常的，找到了反而说明清单和附件对不上）。
        var parts = new List<UpdatePackagePart>();
        var declaredParts = useDelta ? null : asset.Parts;
        if (declaredParts is { Count: > 0 })
        {
            foreach (var part in declaredParts)
            {
                var partName = part.FileName?.Trim();
                var partAttachment = string.IsNullOrWhiteSpace(partName)
                    ? null
                    : attachments.FirstOrDefault(item =>
                        string.Equals(item.Name?.Trim(), partName, StringComparison.OrdinalIgnoreCase));

                if (partAttachment?.BrowserDownloadUrl is not { Length: > 0 } partUrl)
                {
                    return new UpdateCheckResult(
                        UpdateCheckStatus.AssetMissing,
                        $"发现 {version}，但更新包分卷 {partName ?? "(没写卷名)"} 不在 Release 附件里，请手动下载。")
                    {
                        LatestVersion = version,
                        ReleaseTag = release.TagName,
                        ReleaseName = release.Name,
                        ReleaseNotes = notes,
                        ReleaseNotesUrl = notesUrl,
                        RateLimitRemaining = remaining,
                        Detail = $"清单 parts 有 {declaredParts.Count} 卷；附件里有：{string.Join("、", attachments.Select(item => item.Name ?? "?"))}。"
                    };
                }

                parts.Add(new UpdatePackagePart(
                    partName!,
                    UpdatePackagePlan.NormalizeSha256(part.Sha256),
                    part.SizeBytes,
                    partUrl));
            }
        }

        var downloadUrl = string.Empty;
        if (parts.Count == 0)
        {
            var attachment = attachments.FirstOrDefault(item =>
                string.Equals(item.Name?.Trim(), fileName, StringComparison.OrdinalIgnoreCase));
            if (attachment?.BrowserDownloadUrl is not { Length: > 0 } singleUrl)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    $"清单里的 {fileName} 在 Release 附件里找不到，请手动下载。")
                {
                    LatestVersion = version,
                    ReleaseTag = release.TagName,
                    Detail = $"附件里有：{string.Join("、", attachments.Select(item => item.Name ?? "?"))}。"
                };
            }

            downloadUrl = singleUrl;
        }

        var plan = new UpdatePackagePlan(
            version,
            request.Runtime,
            fileName,
            UpdatePackagePlan.NormalizeSha256(useDelta ? delta!.Sha256 : asset.Sha256),
            useDelta ? delta!.SizeBytes : asset.SizeBytes,
            downloadUrl,
            parts.Count > 0 ? parts : null)
        {
            IsDelta = useDelta,
            DeltaBaseVersion = deltaBaseVersion
        };

        // ★ 两维判定（设计 §11.6 第 ③ 条）：版本更高 **且** plan 五项齐全，缺一维就退回 Failed。
        if (!UpdatePackagePlan.IsComplete(plan))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                $"更新清单里的包信息不完整（{version}），已停止更新，请手动下载。")
            {
                LatestVersion = version,
                ReleaseTag = release.TagName,
                Detail = plan.IsSplit
                    ? $"分卷 {plan.Parts!.Count} 卷，合计 {plan.Parts!.Sum(item => item.SizeBytes)} 字节 / 整包 {plan.SizeBytes} 字节；sha256={plan.Sha256}。"
                    : $"sizeBytes={plan.SizeBytes}；sha256={plan.Sha256}；downloadUrl={(string.IsNullOrWhiteSpace(plan.DownloadUrl) ? "空" : "有")}。"
            };
        }

        var comparison = UpdateVersion.CompareSemantic(version, request.CurrentVersion);
        if (comparison <= 0)
        {
            var currentIsPreRelease = UpdateVersion.TryParse(request.CurrentVersion, out var currentKey) && currentKey.IsPreRelease;
            var localIsNewer = request.Channel == UpdateChannel.Stable && currentIsPreRelease;
            var message = localIsNewer
                ? $"当前测试版 V{request.CurrentVersion} 比正式版 V{version} 更新；如果要用正式版，请自行去 Gitee 下载。"
                : $"当前已是最新版本：{request.CurrentVersion}";

            return new UpdateCheckResult(localIsNewer ? UpdateCheckStatus.LocalIsNewer : UpdateCheckStatus.Latest, message)
            {
                LatestVersion = version,
                ReleaseTag = release.TagName,
                ReleaseName = release.Name,
                ReleaseNotes = notes,
                ReleaseNotesUrl = notesUrl,
                RateLimitRemaining = remaining,
                Detail = $"版本比较结果 {comparison}（线上 {version}，本机 {request.CurrentVersion}）。"
            };
        }

        return new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            $"发现新版本 {version}（当前 {request.CurrentVersion}）。")
        {
            LatestVersion = version,
            ReleaseTag = release.TagName,
            ReleaseName = release.Name,
            ReleaseNotes = notes,
            ReleaseNotesUrl = notesUrl,
            RequiresManualMigration = manifest.RequiresManualMigration,
            Plan = plan,
            RateLimitRemaining = remaining,
            Detail = $"plan：{plan.FileName}（{plan.SizeBytes} 字节）。"
        };
    }

    private static ToolboxUpdateManifestDto? ParseManifest(string manifestJson, string? releaseTag, out UpdateCheckResult? failure)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize(manifestJson, UpdateJsonContext.Default.ToolboxUpdateManifestDto);
            if (manifest is not null)
            {
                failure = null;
                return manifest;
            }
        }
        catch (JsonException error)
        {
            ToolboxLog.Warn($"更新清单解析失败（{releaseTag}）：{error.Message}", error);
        }

        failure = new UpdateCheckResult(
            UpdateCheckStatus.Failed,
            $"发现 {releaseTag}，但更新清单读不出来，请手动下载。")
        {
            ReleaseTag = releaseTag,
            Detail = "toolbox-update.json 反序列化失败或内容为 null。"
        };
        return null;
    }

    /// <summary>把客户端异常翻译成给用户的那句话（设计 §7.2 的文案表就是这里）。</summary>
    private static UpdateFailureOutcome MapFailure(GiteeRequestException error, int timeoutSeconds)
    {
        var message = error.Kind switch
        {
            GiteeFailureKind.RateLimited => "Gitee 接口访问过于频繁，请稍后再试。",
            GiteeFailureKind.Forbidden => "Gitee 拒绝了对该仓库的访问。",
            GiteeFailureKind.Unauthorized => "Gitee 拒绝了对该仓库的访问。",
            GiteeFailureKind.NotFound => "找不到 Gitee 仓库或无权访问。",
            GiteeFailureKind.Timeout => $"Gitee 连接超时（{timeoutSeconds} 秒）。请检查网络或稍后重试。",
            GiteeFailureKind.Network => "无法连接到 Gitee。请检查网络后重试。",
            GiteeFailureKind.ServerError => $"Gitee 服务器返回错误（HTTP {error.StatusCode}），请稍后重试。",
            _ => "Gitee 返回的内容读不出来，可能是接口有变化；请到 Gitee 手动下载。"
        };

        var status = error.Kind switch
        {
            GiteeFailureKind.RateLimited => UpdateCheckStatus.RateLimited,
            GiteeFailureKind.Forbidden => UpdateCheckStatus.RepositoryForbidden,
            GiteeFailureKind.Unauthorized => UpdateCheckStatus.RepositoryForbidden,
            GiteeFailureKind.NotFound => UpdateCheckStatus.RepositoryMissing,
            _ => UpdateCheckStatus.Failed
        };

        var statusCode = error.StatusCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "-";
        var detail = $"HTTP {statusCode}；{error.Message}；响应片段：{error.BodySnippet ?? "(无)"}";
        return new UpdateFailureOutcome(status, message, detail);
    }
}
