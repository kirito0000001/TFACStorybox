using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services.Update;

/// <summary>
/// Gitee 请求失败。<see cref="Kind"/> 是**唯一**有语义的字段——给用户哪句话由它决定（设计 §7.2），
/// 所以「403 限流」和「403 拒绝访问」绝不能合并成一个。
/// </summary>
internal sealed class GiteeRequestException : Exception
{
    public GiteeRequestException(
        GiteeFailureKind kind,
        string message,
        int? statusCode = null,
        string? bodySnippet = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        BodySnippet = bodySnippet;
    }

    public GiteeFailureKind Kind { get; }

    public int? StatusCode { get; }

    /// <summary>响应体前若干字符（给日志用；正文只记片段，避免刷屏）。</summary>
    public string? BodySnippet { get; }
}

/// <summary>
/// 状态码 + body 文案 → 失败分类（设计 §7.2 / §4.6）。纯函数，可以直接断言。
///
/// ★ Gitee 耗尽配额时返回的是 <b>403</b>（不是 429），而且那个 403 **不带** <c>X-RateLimit-*</c> 头，
/// body 是 35 字节的 <c>403 Forbidden (Rate Limit Exceeded)</c>。但 403 也可能是「拒绝访问」，
/// 所以只能靠 body 文案区分——不能一律当限流，也不能一律当拒绝。
/// </summary>
internal static class GiteeFailureClassifier
{
    public static bool ContainsRateLimitText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || body.Contains("ratelimit", StringComparison.OrdinalIgnoreCase)
            || body.Contains("访问过于频繁", StringComparison.Ordinal)
            || body.Contains("超出限制", StringComparison.Ordinal);
    }

    public static GiteeFailureKind Classify(int statusCode, string? body)
    {
        var rateLimitText = ContainsRateLimitText(body);

        return statusCode switch
        {
            401 => rateLimitText ? GiteeFailureKind.RateLimited : GiteeFailureKind.Unauthorized,
            403 => rateLimitText ? GiteeFailureKind.RateLimited : GiteeFailureKind.Forbidden,
            404 => GiteeFailureKind.NotFound,
            429 => GiteeFailureKind.RateLimited,
            >= 500 => GiteeFailureKind.ServerError,
            _ => GiteeFailureKind.BadPayload
        };
    }
}

/// <summary>
/// 唯一碰网络的更新出口（设计 §5.1）。它只管「把内容拿回来」：状态码 → 失败分类、配额记账、
/// 防御性反序列化都集中在这里；业务判断（挑哪条 Release、要不要更新）在 <see cref="UpdateService"/>。
///
/// 默认**匿名**访问：正式运行不带 token（只有发布脚本才用 token）。
/// <paramref name="accessToken"/> 是留给联调探针的口子——本机 IP 的匿名配额可能已经耗尽；
/// <c>handler</c> 是留给回归测试的口子（注入替身，一次真实网络都不打）。
/// </summary>
internal sealed class GiteeReleaseClient
{
    /// <summary>设计 §7.4 指定的 UA。</summary>
    public const string UserAgentText = "GalExcleTools-Updater/1.0";

    private const string RateLimitRemainingHeader = "X-RateLimit-Remaining";
    private const int BodySnippetLength = 200;

    /// <summary>单例：设计 §7.4 要求复用连接，并由我们自己控制超时（<see cref="Timeout.InfiniteTimeSpan"/>）。</summary>
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly string? _accessToken;

    /// <summary>默认用共享单例；测试注入 <see cref="HttpMessageHandler"/> 时换成替身专用的实例。</summary>
    private readonly HttpClient _httpClient;

    public GiteeReleaseClient(
        GiteeRepository? repository = null,
        string? accessToken = null,
        HttpMessageHandler? handler = null)
    {
        Repository = repository ?? GiteeRepository.Default;
        _accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
        _httpClient = handler is null ? SharedHttpClient : CreateHttpClient(handler);
    }

    public GiteeRepository Repository { get; }

    /// <summary>
    /// 上一次响应里的剩余配额。Gitee 在限流响应里**不带**这个头，所以它可能是 null——
    /// 读不到就当「未知」，不当 0（否则一次网络抖动就会把自己锁到明天）。
    /// </summary>
    public int? LastRemainingQuota { get; private set; }

    /// <summary>Release 列表（设计 §7.1 第一步）。<c>per_page=100</c> 必须带，否则拿不到第二页。</summary>
    public async Task<IReadOnlyList<GiteeReleaseDto>> ListReleasesAsync(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var url = $"{Repository.ReleasesApiUrl}?per_page=100";
        var json = await GetStringAsync(url, timeoutSeconds, cancellationToken);
        var releases = Deserialize(json, UpdateJsonContext.Default.ListGiteeReleaseDto, url);
        return releases ?? [];
    }

    /// <summary>Release 附件列表（设计 §7.1：更新包一律走 attach_files，不用 <c>assets</c> 里的源码归档）。</summary>
    public async Task<IReadOnlyList<GiteeAttachmentDto>> ListAttachmentsAsync(long releaseId, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var url = $"{Repository.ReleasesApiUrl}/{releaseId.ToString(CultureInfo.InvariantCulture)}/attach_files?per_page=100";
        var json = await GetStringAsync(url, timeoutSeconds, cancellationToken);
        var attachments = Deserialize(json, UpdateJsonContext.Default.ListGiteeAttachmentDto, url);
        return attachments ?? [];
    }

    /// <summary>下载清单文本（小文件，直接读成字符串）。</summary>
    public Task<string> DownloadTextAsync(string url, int timeoutSeconds, CancellationToken cancellationToken) =>
        GetStringAsync(url, timeoutSeconds, cancellationToken, expectsJson: false);

    /// <summary>
    /// 更新包的 HEAD 预检（设计 §8.1 ③）：跟随 302 拿到远端真实大小，别等 100MB 下完才发现拿错了文件。
    /// 返回 <c>null</c> 表示服务端没给 <c>Content-Length</c>（跳过这道预检，本地的大小校验还在后面兜底）。
    /// </summary>
    public async Task<long?> HeadContentLengthAsync(string url, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var kind = GiteeFailureClassifier.Classify(statusCode, null);
                ToolboxLog.Warn($"更新包 HEAD 预检失败：HTTP {statusCode}（{kind}）；URL {url}");
                throw new GiteeRequestException(kind, $"更新包 HEAD 预检失败：HTTP {statusCode}（{kind}）。", statusCode);
            }

            return response.Content.Headers.ContentLength;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ToolboxLog.Warn($"更新包 HEAD 预检超时（{timeoutSeconds} 秒）：{url}");
            throw new GiteeRequestException(GiteeFailureKind.Timeout, $"更新包 HEAD 预检超时（{timeoutSeconds} 秒）。");
        }
        catch (HttpRequestException error)
        {
            ToolboxLog.Warn($"更新包 HEAD 预检发不出去：{url}", error);
            throw new GiteeRequestException(GiteeFailureKind.Network, $"无法连接更新包地址：{url}", innerException: error);
        }
    }

    /// <summary>
    /// 打开更新包的下载响应（设计 §8.1 ④）。这里**只负责把流拿到**：下载没有总时长上限
    /// （100MB 可能要几分钟），超时一律由调用方的「连续 N 秒没有字节」看门狗判定（设计 §7.4）。
    /// 调用方负责 <c>Dispose</c> 返回值。
    /// </summary>
    public async Task<HttpResponseMessage> OpenDownloadResponseAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = (int)response.StatusCode;
            string? snippet = null;
            try
            {
                snippet = BuildBodySnippet(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException)
            {
                // 错误页读不出来不影响判断：状态码已经够定位了，这里只是想多留一点线索（§13）。
                ToolboxLog.Warn("读取下载失败的响应体时出错，忽略。", error);
            }

            response.Dispose();
            var kind = GiteeFailureClassifier.Classify(statusCode, snippet);
            ToolboxLog.Warn($"下载更新包失败：HTTP {statusCode}（{kind}）；URL {url}；响应片段：{snippet ?? "(无)"}");
            throw new GiteeRequestException(kind, $"下载更新包失败：HTTP {statusCode}（{kind}）。", statusCode, snippet);
        }
        catch (HttpRequestException error)
        {
            ToolboxLog.Warn($"下载更新包时连接中断：{url}", error);
            throw new GiteeRequestException(GiteeFailureKind.Network, $"下载更新包时连接中断：{url}", innerException: error);
        }
    }

    private async Task<string> GetStringAsync(string url, int timeoutSeconds, CancellationToken cancellationToken, bool expectsJson = true)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (expectsJson)
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
            }

            if (_accessToken is { } token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                linkedSource.Token);

            RecordRemainingQuota(response);
            var body = await response.Content.ReadAsStringAsync(linkedSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var kind = GiteeFailureClassifier.Classify(statusCode, body);
                var snippet = BuildBodySnippet(body);

                // §13：失败必须留痕，403 的限流原文要原样记下来（不然下次还是分不清两种 403）。
                ToolboxLog.Warn($"Gitee 请求失败：HTTP {statusCode} {response.StatusCode}；分类 {kind}；URL {url}；响应片段：{snippet}");
                throw new GiteeRequestException(
                    kind,
                    $"Gitee 请求失败：HTTP {statusCode}（{kind}）。",
                    statusCode,
                    snippet);
            }

            return body;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ToolboxLog.Warn($"Gitee 请求超时（{timeoutSeconds} 秒）：{url}");
            throw new GiteeRequestException(GiteeFailureKind.Timeout, $"Gitee 请求超时（{timeoutSeconds} 秒）：{url}");
        }
        catch (HttpRequestException error)
        {
            ToolboxLog.Warn($"Gitee 请求发不出去：{url}", error);
            throw new GiteeRequestException(GiteeFailureKind.Network, $"无法连接 Gitee：{url}", innerException: error);
        }
        catch (OperationCanceledException)
        {
            // 用户取消：不是失败，原样上抛给上层（设计 §7.2：取消不算失败）。
            throw;
        }
    }

    private void RecordRemainingQuota(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(RateLimitRemainingHeader, out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
            {
                LastRemainingQuota = remaining;
                return;
            }
        }
    }

    private static T? Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string url)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException error)
        {
            ToolboxLog.Warn($"Gitee 返回的内容不是预期的 JSON：{url}", error);
            throw new GiteeRequestException(
                GiteeFailureKind.BadPayload,
                $"Gitee 返回的内容读不出来：{url}",
                bodySnippet: BuildBodySnippet(json),
                innerException: error);
        }
    }

    private static string BuildBodySnippet(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(空)";
        }

        var collapsed = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return collapsed.Length <= BodySnippetLength ? collapsed : collapsed[..BodySnippetLength] + "…";
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        var client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 })
        {
            // 超时全部由调用方的 CancellationTokenSource 控制（设计 §7.4）。
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentText);
        return client;
    }
}
